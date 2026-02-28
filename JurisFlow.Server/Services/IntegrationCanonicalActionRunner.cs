using System.Text.Json;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace JurisFlow.Server.Services
{
    public sealed class IntegrationCanonicalActionRunner
    {
        private readonly JurisFlowDbContext _context;
        private readonly IntegrationConnectorRegistry _connectorRegistry;
        private readonly IIntegrationOperationsGuard _operationsGuard;
        private readonly ILogger<IntegrationCanonicalActionRunner> _logger;

        public IntegrationCanonicalActionRunner(
            JurisFlowDbContext context,
            IntegrationConnectorRegistry connectorRegistry,
            IIntegrationOperationsGuard operationsGuard,
            ILogger<IntegrationCanonicalActionRunner> logger)
        {
            _context = context;
            _connectorRegistry = connectorRegistry;
            _operationsGuard = operationsGuard;
            _logger = logger;
        }

        public async Task<CanonicalIntegrationActionResult> RunAsync(
            IntegrationConnection connection,
            CanonicalIntegrationActionRequest request,
            CancellationToken cancellationToken)
        {
            var action = NormalizeAction(request.Action);
            request.Action = action;
            request.ConnectionId = connection.Id;
            request.ProviderKey = connection.ProviderKey;
            request.RequestedAt = DateTime.UtcNow;

            var gateDecision = await _operationsGuard.EvaluateForConnectionAsync(
                connection,
                IntegrationOperationKinds.CanonicalAction,
                cancellationToken);
            if (!gateDecision.Allowed)
            {
                return new CanonicalIntegrationActionResult
                {
                    Success = false,
                    Retryable = false,
                    Action = action,
                    Status = "blocked",
                    ErrorCode = gateDecision.Code ?? "integration_operation_blocked",
                    ErrorMessage = gateDecision.Message ?? "Integration operation is blocked."
                };
            }

            var actionConnector = _connectorRegistry.ResolveActionConnector(connection.ProviderKey);
            if (actionConnector == null)
            {
                return CanonicalIntegrationActionResult.Unsupported(action, connection.ProviderKey);
            }

            if (!actionConnector.SupportedActions.Contains(action, StringComparer.Ordinal))
            {
                return CanonicalIntegrationActionResult.Unsupported(action, connection.ProviderKey);
            }

            var requestEvent = new IntegrationOutboxEvent
            {
                Id = Guid.NewGuid().ToString(),
                ConnectionId = connection.Id,
                ProviderKey = connection.ProviderKey,
                EventType = "integration.action.requested",
                EntityType = "integration_action",
                EntityId = action,
                IdempotencyKey = NormalizeIdempotencyKey(request.IdempotencyKey, connection.Id, action),
                CorrelationId = request.CorrelationId,
                Status = IntegrationEventStatuses.Pending,
                PayloadJson = JsonSerializer.Serialize(new
                {
                    request.Action,
                    request.EntityType,
                    request.LocalEntityId,
                    request.ExternalEntityId,
                    request.DryRun,
                    request.RequiresReview,
                    request.Cursor,
                    request.DeltaToken,
                    request.RequestedAt
                }),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _context.IntegrationOutboxEvents.Add(requestEvent);
            await _context.SaveChangesAsync(cancellationToken);

            var retryPolicy = _operationsGuard.ResolveRetryPolicy(connection.ProviderKey);
            var maxAttempts = Math.Clamp(retryPolicy.MaxAttempts, 1, 10);
            CanonicalIntegrationActionResult result = new()
            {
                Success = false,
                Retryable = false,
                Action = action,
                Status = "failed",
                ErrorCode = "canonical_action_not_executed",
                ErrorMessage = "Canonical action was not executed."
            };

            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    result = await actionConnector.ExecuteAsync(connection, request, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Canonical integration action failed. ConnectionId={ConnectionId} ProviderKey={ProviderKey} Action={Action} Attempt={Attempt}",
                        connection.Id, connection.ProviderKey, action, attempt);
                    result = new CanonicalIntegrationActionResult
                    {
                        Success = false,
                        Retryable = ex is HttpRequestException or TimeoutException or TaskCanceledException or IntegrationProviderRateLimitException,
                        Action = action,
                        Status = "failed",
                        ErrorCode = ex is IntegrationProviderRateLimitException ? "provider_rate_limited" : "canonical_action_exception",
                        ErrorMessage = ex.Message
                    };
                }

                if (result.Success || !result.Retryable || attempt >= maxAttempts)
                {
                    break;
                }

                var retryDelay = _operationsGuard.ComputeRetryDelay(
                    connection.ProviderKey,
                    attempt,
                    (result.ErrorCode == "provider_rate_limited" ? TryParseRetryAfterHint(result.ErrorMessage) : null));
                _logger.LogWarning(
                    "Retrying canonical integration action. ConnectionId={ConnectionId} ProviderKey={ProviderKey} Action={Action} Attempt={Attempt} DelaySeconds={DelaySeconds}",
                    connection.Id,
                    connection.ProviderKey,
                    action,
                    attempt,
                    retryDelay.TotalSeconds);
                await Task.Delay(retryDelay, cancellationToken);
            }

            requestEvent.Status = result.Success ? IntegrationEventStatuses.Dispatched : (result.Retryable ? IntegrationEventStatuses.Failed : IntegrationEventStatuses.DeadLetter);
            requestEvent.ErrorCode = result.ErrorCode;
            requestEvent.ErrorMessage = Truncate(result.ErrorMessage ?? result.Message, 2048);
            requestEvent.DispatchedAt = DateTime.UtcNow;
            requestEvent.AttemptCount += 1;
            requestEvent.DeadLettered = !result.Success && !result.Retryable;
            requestEvent.UpdatedAt = DateTime.UtcNow;

            _context.IntegrationOutboxEvents.Add(new IntegrationOutboxEvent
            {
                Id = Guid.NewGuid().ToString(),
                ConnectionId = connection.Id,
                ProviderKey = connection.ProviderKey,
                EventType = "integration.action.completed",
                EntityType = "integration_action",
                EntityId = action,
                IdempotencyKey = $"action-result:{connection.Id}:{action}:{Guid.NewGuid():N}",
                CorrelationId = request.CorrelationId,
                Status = result.Success ? IntegrationEventStatuses.Dispatched : IntegrationEventStatuses.Failed,
                PayloadJson = JsonSerializer.Serialize(new
                {
                    result.Success,
                    result.Action,
                    result.Status,
                    result.Message,
                    result.ErrorCode,
                    result.ErrorMessage,
                    result.ReadCount,
                    result.WriteCount,
                    result.ConflictCount,
                    result.ReviewCount,
                    result.NextCursor,
                    result.NextDeltaToken
                }),
                ErrorCode = result.ErrorCode,
                ErrorMessage = Truncate(result.ErrorMessage, 2048),
                DispatchedAt = DateTime.UtcNow,
                AttemptCount = 1,
                DeadLettered = !result.Success && !result.Retryable,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            if (!result.Success && request.RequiresReview)
            {
                _context.IntegrationReviewQueueItems.Add(new IntegrationReviewQueueItem
                {
                    Id = Guid.NewGuid().ToString(),
                    ConnectionId = connection.Id,
                    ProviderKey = connection.ProviderKey,
                    ItemType = "canonical_action_failure",
                    SourceType = nameof(IntegrationOutboxEvent),
                    SourceId = requestEvent.Id,
                    Status = IntegrationReviewQueueStatuses.Pending,
                    Priority = "high",
                    Title = Truncate($"Canonical action failed: {action}", 160),
                    Summary = Truncate(result.ErrorMessage ?? result.Message ?? "Canonical action failed.", 2048),
                    ContextJson = JsonSerializer.Serialize(new
                    {
                        action,
                        connectionId = connection.Id,
                        providerKey = connection.ProviderKey,
                        request.IdempotencyKey,
                        result.Status,
                        result.ErrorCode
                    }),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            }

            await _context.SaveChangesAsync(cancellationToken);
            return result;
        }

        private static string NormalizeAction(string? action)
        {
            var normalized = action?.Trim().ToLowerInvariant();
            return normalized switch
            {
                IntegrationCanonicalActions.Validate => IntegrationCanonicalActions.Validate,
                IntegrationCanonicalActions.Pull => IntegrationCanonicalActions.Pull,
                IntegrationCanonicalActions.Push => IntegrationCanonicalActions.Push,
                IntegrationCanonicalActions.Webhook => IntegrationCanonicalActions.Webhook,
                IntegrationCanonicalActions.Backfill => IntegrationCanonicalActions.Backfill,
                IntegrationCanonicalActions.Reconcile => IntegrationCanonicalActions.Reconcile,
                _ => IntegrationCanonicalActions.Pull
            };
        }

        private static string NormalizeIdempotencyKey(string? value, string connectionId, string action)
        {
            var normalized = string.IsNullOrWhiteSpace(value)
                ? $"action:{connectionId}:{action}:{Guid.NewGuid():N}"
                : value.Trim();
            return normalized.Length <= 160 ? normalized : normalized[..160];
        }

        private static string? Truncate(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var trimmed = value.Trim();
            return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
        }

        private static TimeSpan? TryParseRetryAfterHint(string? errorMessage)
        {
            if (string.IsNullOrWhiteSpace(errorMessage))
            {
                return null;
            }

            const string marker = "retry_after=";
            var index = errorMessage.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return null;
            }

            var suffix = errorMessage[(index + marker.Length)..];
            var digits = new string(suffix.TakeWhile(char.IsDigit).ToArray());
            if (!int.TryParse(digits, out var seconds) || seconds <= 0)
            {
                return null;
            }

            return TimeSpan.FromSeconds(Math.Min(seconds, 1800));
        }
    }
}
