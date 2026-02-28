using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using JurisFlow.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace JurisFlow.Server.Controllers
{
    [Route("api/integrations/webhooks")]
    [ApiController]
    public class IntegrationWebhooksController : ControllerBase
    {
        private readonly JurisFlowDbContext _context;
        private readonly IntegrationSyncRunner _integrationSyncRunner;
        private readonly IntegrationWebhookService _integrationWebhookService;
        private readonly IIntegrationOperationsGuard _operationsGuard;
        private readonly IntegrationPiiMinimizationService _piiMinimizer;
        private readonly ILogger<IntegrationWebhooksController> _logger;

        public IntegrationWebhooksController(
            JurisFlowDbContext context,
            IntegrationSyncRunner integrationSyncRunner,
            IntegrationWebhookService integrationWebhookService,
            IIntegrationOperationsGuard operationsGuard,
            IntegrationPiiMinimizationService piiMinimizer,
            ILogger<IntegrationWebhooksController> logger)
        {
            _context = context;
            _integrationSyncRunner = integrationSyncRunner;
            _integrationWebhookService = integrationWebhookService;
            _operationsGuard = operationsGuard;
            _piiMinimizer = piiMinimizer;
            _logger = logger;
        }

        [AllowAnonymous]
        [HttpGet("{providerKey}")]
        public IActionResult ValidateWebhookEndpoint(string providerKey, [FromQuery] string? validationToken)
        {
            var provider = IntegrationProviderCatalog.Find(providerKey);
            if (provider == null || !provider.SupportsWebhook)
            {
                return NotFound(new { message = "Webhook provider was not found." });
            }

            if (!string.Equals(provider.ProviderKey, IntegrationProviderKeys.MicrosoftOutlookMail, StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { message = "Webhook validation challenge is not supported for this provider." });
            }

            if (string.IsNullOrWhiteSpace(validationToken))
            {
                return BadRequest(new { message = "validationToken query parameter is required." });
            }

            Response.Headers.CacheControl = "no-store";
            return Content(validationToken, "text/plain", Encoding.UTF8);
        }

        [AllowAnonymous]
        [EnableRateLimiting("IntegrationWebhook")]
        [HttpPost("{providerKey}")]
        public async Task<IActionResult> ReceiveWebhook(string providerKey, CancellationToken cancellationToken)
        {
            var provider = IntegrationProviderCatalog.Find(providerKey);
            if (provider == null || !provider.SupportsWebhook)
            {
                return NotFound(new { message = "Webhook provider was not found." });
            }

            if (string.Equals(provider.ProviderKey, IntegrationProviderKeys.MicrosoftOutlookMail, StringComparison.OrdinalIgnoreCase))
            {
                var validationToken = Request.Query["validationToken"].FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(validationToken))
                {
                    Response.Headers.CacheControl = "no-store";
                    return Content(validationToken, "text/plain", Encoding.UTF8);
                }
            }

            string payload;
            using (var reader = new StreamReader(Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true))
            {
                payload = await reader.ReadToEndAsync(cancellationToken);
            }

            if (string.IsNullOrWhiteSpace(payload))
            {
                return BadRequest(new { message = "Webhook payload is required." });
            }

            var validation = _integrationWebhookService.Validate(provider.ProviderKey, Request, payload);
            var eventId = string.IsNullOrWhiteSpace(validation.EventId)
                ? $"{provider.ProviderKey}:{Guid.NewGuid():N}"
                : validation.EventId;

            var inboxEvent = await _context.IntegrationInboxEvents
                .FirstOrDefaultAsync(
                    e => e.ProviderKey == provider.ProviderKey && e.ExternalEventId == eventId,
                    cancellationToken);

            if (inboxEvent != null &&
                (string.Equals(inboxEvent.Status, IntegrationEventStatuses.Processed, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(inboxEvent.Status, IntegrationEventStatuses.Rejected, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(inboxEvent.Status, IntegrationEventStatuses.NoConnection, StringComparison.OrdinalIgnoreCase)))
            {
                return Ok(new
                {
                    success = true,
                    deduplicated = true,
                    provider = provider.ProviderKey,
                    webhookEventId = eventId,
                    signatureValidated = inboxEvent.SignatureValidated
                });
            }

            var isReplay = inboxEvent != null;
            inboxEvent ??= new IntegrationInboxEvent
            {
                Id = Guid.NewGuid().ToString(),
                ProviderKey = provider.ProviderKey,
                ExternalEventId = Truncate(eventId, 160) ?? $"{provider.ProviderKey}:{Guid.NewGuid():N}",
                ReceivedAt = DateTime.UtcNow
            };

            if (isReplay)
            {
                inboxEvent.ReplayCount += 1;
            }
            inboxEvent.SignatureValidated = validation.SignatureValidated;
            inboxEvent.CorrelationId = Truncate(
                Request.Headers["X-Correlation-ID"].FirstOrDefault()
                ?? HttpContext.TraceIdentifier
                ?? eventId,
                128);
            inboxEvent.HeadersJson = _piiMinimizer.SanitizeWebhookHeadersForStorage(Request);
            inboxEvent.PayloadJson = _piiMinimizer.SanitizeWebhookPayloadForStorage(provider.ProviderKey, payload);
            inboxEvent.MetadataJson = JsonSerializer.Serialize(new
            {
                traceId = HttpContext.TraceIdentifier,
                correlationId = inboxEvent.CorrelationId,
                provider = provider.ProviderKey,
                receivedAt = DateTime.UtcNow
            });
            inboxEvent.PayloadHash = ComputePayloadSha256(payload);
            inboxEvent.UpdatedAt = DateTime.UtcNow;
            inboxEvent.Status = validation.Success ? IntegrationEventStatuses.Validated : IntegrationEventStatuses.Rejected;
            inboxEvent.ErrorMessage = validation.Success ? null : Truncate(validation.ErrorMessage, 2048);

            if (_context.Entry(inboxEvent).State == EntityState.Detached)
            {
                _context.IntegrationInboxEvents.Add(inboxEvent);
            }

            if (!validation.Success)
            {
                _logger.LogWarning(
                    "Integration webhook rejected. ProviderKey={ProviderKey} Reason={Reason}",
                    provider.ProviderKey,
                    validation.ErrorMessage ?? "validation_failed");

                QueueReviewItem(
                    provider.ProviderKey,
                    inboxEvent,
                    itemType: "webhook_validation_failure",
                    priority: "high",
                    title: "Integration webhook validation failed",
                    summary: validation.ErrorMessage ?? "Webhook validation failed.");

                await _context.SaveChangesAsync(cancellationToken);

                return Unauthorized(new
                {
                    message = validation.ErrorMessage ?? "Webhook validation failed."
                });
            }

            var connection = await _context.IntegrationConnections
                .FirstOrDefaultAsync(
                    c => c.ProviderKey == provider.ProviderKey &&
                         c.SyncEnabled &&
                         (c.Status == "connected" || c.Status == "error"),
                    cancellationToken);

            if (connection == null)
            {
                inboxEvent.Status = IntegrationEventStatuses.NoConnection;
                inboxEvent.ProcessedAt = DateTime.UtcNow;
                inboxEvent.UpdatedAt = DateTime.UtcNow;
                QueueReviewItem(
                    provider.ProviderKey,
                    inboxEvent,
                    itemType: "webhook_no_connection",
                    priority: "medium",
                    title: "Webhook received with no active connection",
                    summary: $"Webhook accepted for {provider.ProviderKey}, but no active integration connection was found.");
                await _context.SaveChangesAsync(cancellationToken);

                return Accepted(new
                {
                    message = "Webhook accepted, but no active integration connection was found.",
                    provider = provider.ProviderKey
                });
            }

            connection.LastWebhookAt = DateTime.UtcNow;
            connection.LastWebhookEventId = Truncate(eventId, 160);
            connection.UpdatedAt = DateTime.UtcNow;
            inboxEvent.ConnectionId = connection.Id;
            AuditTraceContext.SetIntegrationTrace(
                HttpContext,
                connectionId: connection.Id,
                providerKey: connection.ProviderKey,
                eventId: inboxEvent.ExternalEventId,
                correlationId: inboxEvent.CorrelationId);

            var gateDecision = await _operationsGuard.EvaluateForConnectionAsync(
                connection,
                IntegrationOperationKinds.Webhook,
                cancellationToken);
            if (!gateDecision.Allowed)
            {
                inboxEvent.Status = IntegrationEventStatuses.Failed;
                inboxEvent.ErrorMessage = Truncate(gateDecision.Message ?? "Webhook-triggered sync is blocked.", 2048);
                inboxEvent.ProcessedAt = DateTime.UtcNow;
                inboxEvent.UpdatedAt = DateTime.UtcNow;
                QueueReviewItem(
                    provider.ProviderKey,
                    inboxEvent,
                    itemType: "webhook_blocked_by_ops_guard",
                    priority: "high",
                    title: "Webhook-triggered sync blocked",
                    summary: gateDecision.Message ?? "Webhook-triggered sync blocked by kill switch or connector policy.");
                await _context.SaveChangesAsync(cancellationToken);
                return Accepted(new
                {
                    success = false,
                    blocked = true,
                    message = gateDecision.Message ?? "Webhook-triggered sync is blocked.",
                    provider = provider.ProviderKey,
                    webhookEventId = eventId
                });
            }

            var baseIdempotencyKey = BuildWebhookIdempotencyKey(connection.Id, eventId);
            var existingRun = await _context.IntegrationRuns
                .AsNoTracking()
                .Where(r => r.ConnectionId == connection.Id && r.IdempotencyKey.StartsWith(baseIdempotencyKey))
                .OrderByDescending(r => r.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            var idempotencyKey = baseIdempotencyKey;
            if (existingRun != null &&
                (string.Equals(existingRun.Status, IntegrationRunStatuses.Failed, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(existingRun.Status, IntegrationRunStatuses.DeadLetter, StringComparison.OrdinalIgnoreCase)))
            {
                idempotencyKey = $"{baseIdempotencyKey}:{DateTime.UtcNow:yyyyMMddHHmmss}";
            }

            var runResult = await _integrationSyncRunner.RunAsync(
                connection,
                new IntegrationSyncRunRequest
                {
                    Trigger = IntegrationRunTriggers.Webhook,
                    IdempotencyKey = idempotencyKey
                },
                cancellationToken);

            connection.LastWebhookAt = DateTime.UtcNow;
            connection.LastWebhookEventId = Truncate(eventId, 160);
            connection.UpdatedAt = DateTime.UtcNow;
            inboxEvent.ConnectionId = connection.Id;
            inboxEvent.RunId = runResult.RunId;
            inboxEvent.Status = runResult.Success ? IntegrationEventStatuses.Processed : IntegrationEventStatuses.Failed;
            inboxEvent.ErrorMessage = runResult.Success ? null : Truncate(runResult.Message, 2048);
            inboxEvent.ProcessedAt = DateTime.UtcNow;
            inboxEvent.UpdatedAt = DateTime.UtcNow;

            if (!runResult.Success)
            {
                QueueReviewItem(
                    provider.ProviderKey,
                    inboxEvent,
                    itemType: "webhook_sync_failure",
                    priority: runResult.IsDeadLetter ? "high" : "medium",
                    title: "Webhook-triggered sync failed",
                    summary: runResult.Message ?? "Webhook-triggered sync failed.",
                    runId: runResult.RunId);
            }

            await _context.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                success = runResult.Success,
                deduplicated = runResult.Deduplicated,
                runId = runResult.RunId,
                syncedCount = runResult.SyncedCount,
                message = runResult.Message,
                provider = provider.ProviderKey,
                webhookEventId = eventId,
                signatureValidated = validation.SignatureValidated
            });
        }

        private static string BuildWebhookIdempotencyKey(string connectionId, string eventId)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(eventId));
            var digest = Convert.ToHexString(hash).ToLowerInvariant();
            return $"webhook:{connectionId}:{digest[..24]}";
        }

        private static string? Truncate(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return value.Length <= maxLength ? value : value[..maxLength];
        }

        private static string SerializeHeaders(HttpRequest request)
        {
            var headers = request.Headers
                .ToDictionary(
                    h => h.Key,
                    h => h.Value.ToString(),
                    StringComparer.OrdinalIgnoreCase);
            return JsonSerializer.Serialize(headers);
        }

        private static string ComputePayloadSha256(string payload)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private void QueueReviewItem(
            string providerKey,
            IntegrationInboxEvent inboxEvent,
            string itemType,
            string priority,
            string title,
            string summary,
            string? runId = null)
        {
            _context.IntegrationReviewQueueItems.Add(new IntegrationReviewQueueItem
            {
                Id = Guid.NewGuid().ToString(),
                ConnectionId = inboxEvent.ConnectionId,
                RunId = runId ?? inboxEvent.RunId,
                ProviderKey = providerKey,
                ItemType = itemType,
                SourceId = inboxEvent.Id,
                SourceType = nameof(IntegrationInboxEvent),
                Status = IntegrationReviewQueueStatuses.Pending,
                Priority = priority,
                Title = Truncate(title, 160),
                Summary = Truncate(summary, 2048),
                ContextJson = JsonSerializer.Serialize(new
                {
                    inboxEventId = inboxEvent.Id,
                    externalEventId = inboxEvent.ExternalEventId,
                    signatureValidated = inboxEvent.SignatureValidated,
                    status = inboxEvent.Status,
                    processedAt = inboxEvent.ProcessedAt
                }),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }
    }
}
