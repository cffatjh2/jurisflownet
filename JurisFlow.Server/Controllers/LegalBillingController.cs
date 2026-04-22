using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using JurisFlow.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text.Json;
using Task = System.Threading.Tasks.Task;

namespace JurisFlow.Server.Controllers
{
    [Route("api/legal-billing")]
    [ApiController]
    [Authorize(Policy = "BillingRead")]
    public class LegalBillingController : ControllerBase
    {
        private readonly JurisFlowDbContext _context;
        private readonly LegalBillingEngineService _billingEngine;
        private readonly AuditLogger _auditLogger;
        private readonly MatterWorkflowTriggerDispatcher _workflowTriggerDispatcher;
        private readonly BillingObjectAuthorizationService _billingAuthorization;
        private readonly ILogger<LegalBillingController> _logger;

        public LegalBillingController(
            JurisFlowDbContext context,
            LegalBillingEngineService billingEngine,
            AuditLogger auditLogger,
            MatterWorkflowTriggerDispatcher workflowTriggerDispatcher,
            BillingObjectAuthorizationService billingAuthorization,
            ILogger<LegalBillingController> logger)
        {
            _context = context;
            _billingEngine = billingEngine;
            _auditLogger = auditLogger;
            _workflowTriggerDispatcher = workflowTriggerDispatcher;
            _billingAuthorization = billingAuthorization;
            _logger = logger;
        }

        [HttpGet("policies/matter/{matterId}")]
        public async Task<ActionResult<MatterBillingPolicy?>> GetMatterPolicy(string matterId, CancellationToken ct)
        {
            var matterAuth = await RequireMatterAuthorizationAsync(matterId, requireWrite: false, ct);
            if (matterAuth != null) return matterAuth;

            var policy = await _billingEngine.GetActiveMatterPolicyAsync(matterId, DateTime.UtcNow, ct);
            if (policy == null)
            {
                return NotFound();
            }

            return Ok(policy);
        }

        [HttpPost("policies/matter")]
        [Authorize(Policy = "BillingSettingsWrite")]
        public async Task<ActionResult<MatterBillingPolicy>> UpsertMatterPolicy([FromBody] MatterBillingPolicyUpsertRequest request, CancellationToken ct)
        {
            if (request == null) return BadRequest(new { message = "Request body is required." });
            var auth = await RequireMatterAuthorizationAsync(request.MatterId, requireWrite: true, ct);
            if (auth != null) return auth;

            try
            {
                var policy = await _billingEngine.UpsertMatterPolicyAsync(request, GetUserId(), ct);
                await _auditLogger.LogAsync(HttpContext, "billing.policy.upsert", nameof(MatterBillingPolicy), policy.Id, $"MatterId={policy.MatterId}, Arrangement={policy.ArrangementType}");
                return Ok(policy);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("rate-cards")]
        public async Task<ActionResult<IEnumerable<BillingRateCard>>> GetRateCards([FromQuery] BillingRateCardQuery query, CancellationToken ct)
        {
            return Ok(await _billingEngine.ListRateCardsAsync(query ?? new BillingRateCardQuery(), ct));
        }

        [HttpPost("rate-cards")]
        [Authorize(Policy = "BillingSettingsWrite")]
        public async Task<ActionResult<BillingRateCard>> UpsertRateCard([FromBody] BillingRateCardUpsertRequest request, CancellationToken ct)
        {
            if (request == null) return BadRequest(new { message = "Request body is required." });
            var auth = await RequireOptionalMatterClientAuthorizationAsync(request.MatterId, request.ClientId, requireWrite: true, ct);
            if (auth != null) return auth;
            try
            {
                var rateCard = await _billingEngine.UpsertRateCardAsync(request, GetUserId(), ct);
                await _auditLogger.LogAsync(HttpContext, "billing.rate_card.upsert", nameof(BillingRateCard), rateCard.Id, $"Name={rateCard.Name}, Scope={rateCard.Scope}");
                return Ok(rateCard);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("rate-cards/{rateCardId}/entries")]
        public async Task<ActionResult<IEnumerable<BillingRateCardEntry>>> GetRateCardEntries(string rateCardId, CancellationToken ct)
        {
            return Ok(await _billingEngine.ListRateCardEntriesAsync(rateCardId, ct));
        }

        [HttpPost("rate-cards/{rateCardId}/entries")]
        [Authorize(Policy = "BillingSettingsWrite")]
        public async Task<ActionResult<BillingRateCardEntry>> UpsertRateCardEntry(string rateCardId, [FromBody] BillingRateCardEntryUpsertRequest request, CancellationToken ct)
        {
            if (request == null) return BadRequest(new { message = "Request body is required." });
            var auth = await RequireOptionalMatterClientAuthorizationAsync(request.MatterId, request.ClientId, requireWrite: true, ct);
            if (auth != null) return auth;
            request.RateCardId = rateCardId;
            try
            {
                var entry = await _billingEngine.UpsertRateCardEntryAsync(request, ct);
                await _auditLogger.LogAsync(HttpContext, "billing.rate_card_entry.upsert", nameof(BillingRateCardEntry), entry.Id, $"RateCardId={entry.RateCardId}, Type={entry.EntryType}, Rate={entry.Rate}");
                return Ok(entry);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("prebills/generate")]
        [Authorize(Policy = "BillingWrite")]
        public async Task<ActionResult<PrebillGenerationResult>> GeneratePrebill([FromBody] PrebillGenerateRequest request, CancellationToken ct)
        {
            if (request == null) return BadRequest(new { message = "Request body is required." });
            var auth = await RequireMatterAuthorizationAsync(request.MatterId, requireWrite: true, ct);
            if (auth != null) return auth;
            try
            {
                var result = await _billingEngine.GeneratePrebillAsync(request, GetUserId(), ct);
                await _auditLogger.LogAsync(HttpContext, "billing.prebill.generate", nameof(BillingPrebillBatch), result.Batch.Id, $"MatterId={result.Batch.MatterId}, Lines={result.Lines.Count}, Warnings={result.Warnings.Count}");
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("prebills")]
        public async Task<ActionResult<IEnumerable<BillingPrebillBatch>>> GetPrebills([FromQuery] BillingPrebillBatchQuery query, CancellationToken ct)
        {
            var auth = await RequireOptionalMatterClientAuthorizationAsync(query?.MatterId, query?.ClientId, requireWrite: false, ct);
            if (auth != null) return auth;
            if ((query == null || (string.IsNullOrWhiteSpace(query.MatterId) && string.IsNullOrWhiteSpace(query.ClientId))) &&
                !_billingAuthorization.IsPrivileged(User))
            {
                return Forbid();
            }
            return Ok(await _billingEngine.ListPrebillsAsync(query ?? new BillingPrebillBatchQuery(), ct));
        }

        [HttpGet("prebills/{prebillId}")]
        public async Task<ActionResult<PrebillDetailResult>> GetPrebill(string prebillId, CancellationToken ct)
        {
            var auth = await RequirePrebillAuthorizationAsync(prebillId, requireWrite: false, ct);
            if (auth != null) return auth;
            var result = await _billingEngine.GetPrebillAsync(prebillId, ct);
            return result == null ? NotFound() : Ok(result);
        }

        [HttpPost("prebills/{prebillId}/submit-review")]
        [Authorize(Policy = "BillingWrite")]
        public async Task<ActionResult<BillingPrebillBatch>> SubmitPrebillForReview(string prebillId, [FromBody] ReviewDecisionDto? body, CancellationToken ct)
        {
            var auth = await RequirePrebillAuthorizationAsync(prebillId, requireWrite: true, ct);
            if (auth != null) return auth;
            try
            {
                var batch = await _billingEngine.SubmitPrebillForReviewAsync(prebillId, GetUserId(), body?.Notes, ct);
                if (batch == null) return NotFound();
                await _auditLogger.LogAsync(HttpContext, "billing.prebill.submit_review", nameof(BillingPrebillBatch), batch.Id, $"Status={batch.Status}");
                return Ok(batch);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("prebills/{prebillId}/approve")]
        [Authorize(Policy = "BillingApprove")]
        public async Task<ActionResult<BillingPrebillBatch>> ApprovePrebill(string prebillId, [FromBody] ReviewDecisionDto? body, CancellationToken ct)
        {
            var auth = await RequirePrebillAuthorizationAsync(prebillId, requireWrite: true, ct);
            if (auth != null) return auth;
            try
            {
                var batch = await _billingEngine.ApprovePrebillAsync(prebillId, GetUserId(), body?.Notes, ct);
                if (batch == null) return NotFound();
                await _auditLogger.LogAsync(HttpContext, "billing.prebill.approve", nameof(BillingPrebillBatch), batch.Id, $"Total={batch.Total}");
                return Ok(batch);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("prebills/{prebillId}/reject")]
        [Authorize(Policy = "BillingApprove")]
        public async Task<ActionResult<BillingPrebillBatch>> RejectPrebill(string prebillId, [FromBody] ReviewDecisionDto? body, CancellationToken ct)
        {
            var auth = await RequirePrebillAuthorizationAsync(prebillId, requireWrite: true, ct);
            if (auth != null) return auth;
            try
            {
                var batch = await _billingEngine.RejectPrebillAsync(prebillId, GetUserId(), body?.Notes, ct);
                if (batch == null) return NotFound();
                await _auditLogger.LogAsync(HttpContext, "billing.prebill.reject", nameof(BillingPrebillBatch), batch.Id, $"Status={batch.Status}");
                return Ok(batch);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("prebills/{prebillId}/finalize")]
        [Authorize(Policy = "BillingFinalize")]
        public async Task<ActionResult<FinalizePrebillResult>> FinalizePrebill(string prebillId, [FromBody] FinalizePrebillRequest request, CancellationToken ct)
        {
            if (request == null) return BadRequest(new { message = "Request body is required." });
            var auth = await RequirePrebillAuthorizationAsync(prebillId, requireWrite: true, ct);
            if (auth != null) return auth;
            try
            {
                var result = await _billingEngine.FinalizePrebillToInvoiceAsync(prebillId, request, GetUserId(), ct);
                if (result == null) return NotFound();
                await TryCaptureBillingEngineEbillingTransmissionAsync(result.Batch, result.Invoice, "queued", ct);
                await _auditLogger.LogAsync(HttpContext, "billing.prebill.finalize", nameof(BillingPrebillBatch), result.Batch.Id, $"InvoiceId={result.Invoice.Id}, Total={result.Invoice.Total}");
                await TryTriggerOutcomeFeePlannerAsync(result.Invoice.MatterId, "invoice_issue_finalize_prebill", nameof(Invoice), result.Invoice.Id, ct);
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("prebills/lines/{prebillLineId}/adjust")]
        [Authorize(Policy = "BillingWrite")]
        public async Task<ActionResult<BillingPrebillLine>> AdjustPrebillLine(string prebillLineId, [FromBody] PrebillLineAdjustmentRequest request, CancellationToken ct)
        {
            if (request == null) return BadRequest(new { message = "Request body is required." });
            var auth = await RequirePrebillLineAuthorizationAsync(prebillLineId, requireWrite: true, ct);
            if (auth != null) return auth;
            try
            {
                var line = await _billingEngine.AdjustPrebillLineAsync(prebillLineId, request, ct);
                if (line == null) return NotFound();
                await _auditLogger.LogAsync(HttpContext, "billing.prebill_line.adjust", nameof(BillingPrebillLine), line.Id, $"ApprovedAmount={line.ApprovedAmount}, Status={line.Status}");
                return Ok(line);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("prebills/{prebillId}/ledes-preview")]
        public async Task<ActionResult<LedesPreviewResult>> GetLedesPreview(string prebillId, CancellationToken ct)
        {
            var auth = await RequirePrebillAuthorizationAsync(prebillId, requireWrite: false, ct);
            if (auth != null) return auth;
            var result = await _billingEngine.GenerateLedesPreviewAsync(prebillId, ct);
            if (result != null)
            {
                await TryCaptureBillingEngineLedesPreviewEventAsync(prebillId, result, ct);
            }
            return result == null ? NotFound() : Ok(result);
        }

        [HttpGet("ebilling/transmissions")]
        public async Task<ActionResult<IEnumerable<BillingEbillingTransmission>>> GetEbillingTransmissions(
            [FromQuery] string? providerKey = null,
            [FromQuery] string? invoiceId = null,
            [FromQuery] int limit = 200,
            CancellationToken ct = default)
        {
            var query = _context.BillingEbillingTransmissions.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(providerKey)) query = query.Where(t => t.ProviderKey == providerKey.Trim().ToLowerInvariant());
            if (!string.IsNullOrWhiteSpace(invoiceId)) query = query.Where(t => t.InvoiceId == invoiceId);

            return Ok(await query.OrderByDescending(t => t.SubmittedAt ?? t.CreatedAt)
                .Take(Math.Clamp(limit, 1, 1000))
                .ToListAsync(ct));
        }

        [HttpGet("ebilling/events")]
        public async Task<ActionResult<IEnumerable<BillingEbillingResultEvent>>> GetEbillingEvents(
            [FromQuery] string? providerKey = null,
            [FromQuery] string? invoiceId = null,
            [FromQuery] int limit = 500,
            CancellationToken ct = default)
        {
            var query = _context.BillingEbillingResultEvents.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(providerKey)) query = query.Where(e => e.ProviderKey == providerKey.Trim().ToLowerInvariant());
            if (!string.IsNullOrWhiteSpace(invoiceId)) query = query.Where(e => e.InvoiceId == invoiceId);

            return Ok(await query.OrderByDescending(e => e.OccurredAt)
                .ThenByDescending(e => e.RecordedAt)
                .Take(Math.Clamp(limit, 1, 2000))
                .ToListAsync(ct));
        }

        [HttpPost("ebilling/transmissions")]
        [Authorize(Policy = "BillingWrite")]
        public async Task<ActionResult<BillingEbillingTransmission>> RecordEbillingTransmission([FromBody] RecordEbillingTransmissionRequest request, CancellationToken ct)
        {
            if (request == null) return BadRequest(new { message = "Request body is required." });
            var auth = await RequireFinancialObjectAuthorizationAsync(request.InvoiceId, request.MatterId, request.ClientId, requireWrite: true, ct);
            if (auth != null) return auth;

            var normalizedProviderKey = NormalizeRequiredKey(request.ProviderKey, 64);
            if (normalizedProviderKey == null)
            {
                return BadRequest(new { message = "ProviderKey is required." });
            }

            BillingEbillingTransmission? transmission = null;
            var idempotencyKey = Truncate(request.IdempotencyKey, 128);
            if (!string.IsNullOrWhiteSpace(idempotencyKey))
            {
                transmission = await _context.BillingEbillingTransmissions
                    .FirstOrDefaultAsync(t => t.MetadataJson != null && t.MetadataJson.Contains(idempotencyKey), ct);
            }
            if (transmission == null && !string.IsNullOrWhiteSpace(request.ExternalTransmissionId))
            {
                transmission = await _context.BillingEbillingTransmissions
                    .FirstOrDefaultAsync(t => t.ProviderKey == normalizedProviderKey && t.ExternalTransmissionId == request.ExternalTransmissionId, ct);
            }

            var isNew = transmission == null;
            var now = DateTime.UtcNow;
            transmission ??= new BillingEbillingTransmission
            {
                CreatedAt = now,
                CreatedBy = GetUserId()
            };

            transmission.ProviderKey = normalizedProviderKey;
            transmission.InvoiceId = NullIfEmpty(request.InvoiceId);
            transmission.MatterId = NullIfEmpty(request.MatterId);
            transmission.ClientId = NullIfEmpty(request.ClientId);
            transmission.PayorClientId = NullIfEmpty(request.PayorClientId);
            transmission.PrebillBatchId = NullIfEmpty(request.PrebillBatchId);
            transmission.Format = NormalizeEbillingFormat(request.Format);
            transmission.Status = NormalizeEbillingTransmissionStatus(request.Status);
            transmission.ExternalTransmissionId = Truncate(request.ExternalTransmissionId, 255);
            transmission.CorrelationId = Truncate(request.CorrelationId, 128);
            transmission.Reference = Truncate(request.Reference, 255);
            transmission.ErrorCode = Truncate(request.ErrorCode, 128);
            transmission.ErrorMessage = Truncate(request.ErrorMessage, 2048);
            transmission.SubmittedAt = request.SubmittedAt ?? transmission.SubmittedAt;
            transmission.CompletedAt = request.CompletedAt ?? transmission.CompletedAt;
            transmission.RequestPayloadJson = TruncateJson(request.RequestPayloadJson);
            transmission.ResponsePayloadJson = TruncateJson(request.ResponsePayloadJson);
            transmission.MetadataJson = MergeMetadataJson(transmission.MetadataJson, idempotencyKey, request.MetadataJson);
            transmission.UpdatedAt = now;

            if (isNew)
            {
                _context.BillingEbillingTransmissions.Add(transmission);
            }

            await _context.SaveChangesAsync(ct);
            await _auditLogger.LogAsync(HttpContext,
                isNew ? "billing.ebilling.transmission.create" : "billing.ebilling.transmission.update",
                nameof(BillingEbillingTransmission),
                transmission.Id,
                $"Provider={transmission.ProviderKey}, Status={transmission.Status}, InvoiceId={transmission.InvoiceId}");

            return Ok(transmission);
        }

        [HttpPost("ebilling/events")]
        [Authorize(Policy = "BillingWrite")]
        public async Task<ActionResult<BillingEbillingResultEvent>> RecordEbillingResultEvent([FromBody] RecordEbillingResultEventRequest request, CancellationToken ct)
        {
            if (request == null) return BadRequest(new { message = "Request body is required." });
            var auth = await RequireFinancialObjectAuthorizationAsync(request.InvoiceId, request.MatterId, request.ClientId, requireWrite: true, ct);
            if (auth != null) return auth;

            var normalizedProviderKey = NormalizeRequiredKey(request.ProviderKey, 64);
            if (normalizedProviderKey == null)
            {
                return BadRequest(new { message = "ProviderKey is required." });
            }

            var idempotencyKey = Truncate(request.IdempotencyKey, 128);
            BillingEbillingResultEvent? existing = null;
            if (!string.IsNullOrWhiteSpace(request.ExternalEventId))
            {
                existing = await _context.BillingEbillingResultEvents
                    .FirstOrDefaultAsync(e => e.ProviderKey == normalizedProviderKey && e.ExternalEventId == request.ExternalEventId, ct);
            }
            if (existing == null && !string.IsNullOrWhiteSpace(idempotencyKey))
            {
                existing = await _context.BillingEbillingResultEvents
                    .FirstOrDefaultAsync(e => e.MetadataJson != null && e.MetadataJson.Contains(idempotencyKey), ct);
            }
            if (existing != null)
            {
                return Ok(existing);
            }

            BillingEbillingTransmission? transmission = null;
            if (!string.IsNullOrWhiteSpace(request.TransmissionId))
            {
                transmission = await _context.BillingEbillingTransmissions.FirstOrDefaultAsync(t => t.Id == request.TransmissionId, ct);
                if (transmission == null)
                {
                    return BadRequest(new { message = "TransmissionId not found." });
                }
            }
            else if (!string.IsNullOrWhiteSpace(request.ExternalTransmissionId))
            {
                transmission = await _context.BillingEbillingTransmissions
                    .FirstOrDefaultAsync(t => t.ProviderKey == normalizedProviderKey && t.ExternalTransmissionId == request.ExternalTransmissionId, ct);
            }

            var now = DateTime.UtcNow;
            var ebillingEvent = new BillingEbillingResultEvent
            {
                TransmissionId = transmission?.Id,
                ProviderKey = normalizedProviderKey,
                ExternalTransmissionId = Truncate(request.ExternalTransmissionId, 255) ?? transmission?.ExternalTransmissionId,
                ExternalEventId = Truncate(request.ExternalEventId, 255),
                EventType = NormalizeEbillingEventType(request.EventType),
                Status = NormalizeEbillingEventStatus(request.Status),
                InvoiceId = NullIfEmpty(request.InvoiceId) ?? transmission?.InvoiceId,
                MatterId = NullIfEmpty(request.MatterId) ?? transmission?.MatterId,
                ClientId = NullIfEmpty(request.ClientId) ?? transmission?.ClientId,
                PayorClientId = NullIfEmpty(request.PayorClientId) ?? transmission?.PayorClientId,
                ResultCode = Truncate(request.ResultCode, 128),
                ResultMessage = Truncate(request.ResultMessage, 2048),
                ErrorCode = Truncate(request.ErrorCode, 128),
                ErrorCategory = Truncate(request.ErrorCategory, 64),
                ErrorMessage = Truncate(request.ErrorMessage, 2048),
                IsFinal = request.IsFinal ?? false,
                IsRetryable = request.IsRetryable ?? false,
                OccurredAt = request.OccurredAt ?? now,
                RecordedAt = now,
                PayloadJson = TruncateJson(request.PayloadJson),
                MetadataJson = MergeMetadataJson(null, idempotencyKey, request.MetadataJson),
                RecordedBy = GetUserId()
            };

            _context.BillingEbillingResultEvents.Add(ebillingEvent);

            string? previousTransmissionStatus = null;
            if (transmission != null)
            {
                previousTransmissionStatus = transmission.Status;
                transmission.Status = NormalizeTransmissionStatusFromEvent(transmission.Status, ebillingEvent.Status, ebillingEvent.IsFinal);
                transmission.CompletedAt = ebillingEvent.IsFinal ? ebillingEvent.OccurredAt : transmission.CompletedAt;
                if (ebillingEvent.Status is "rejected" or "error")
                {
                    transmission.ErrorCode = ebillingEvent.ErrorCode ?? transmission.ErrorCode;
                    transmission.ErrorMessage = ebillingEvent.ErrorMessage ?? ebillingEvent.ResultMessage ?? transmission.ErrorMessage;
                }
                transmission.UpdatedAt = now;
            }

            await _context.SaveChangesAsync(ct);
            if (transmission != null)
            {
                await _auditLogger.LogAsync(HttpContext,
                    "billing.ebilling.transmission.status_from_event",
                    nameof(BillingEbillingTransmission),
                    transmission.Id,
                    $"Provider={transmission.ProviderKey}, PrevStatus={previousTransmissionStatus}, NewStatus={transmission.Status}, EventStatus={ebillingEvent.Status}, EventId={ebillingEvent.Id}");
            }
            await _auditLogger.LogAsync(HttpContext,
                "billing.ebilling.event.record",
                nameof(BillingEbillingResultEvent),
                ebillingEvent.Id,
                $"Provider={ebillingEvent.ProviderKey}, Status={ebillingEvent.Status}, InvoiceId={ebillingEvent.InvoiceId}");

            return Ok(ebillingEvent);
        }

        [HttpPost("ebilling/transmissions/{transmissionId}/repair")]
        [Authorize(Policy = "BillingWrite")]
        public async Task<ActionResult<BillingEbillingTransmission>> RepairEbillingTransmission(string transmissionId, [FromBody] RepairEbillingTransmissionRequest? request, CancellationToken ct)
        {
            var transmission = await _context.BillingEbillingTransmissions.FirstOrDefaultAsync(t => t.Id == transmissionId, ct);
            if (transmission == null)
            {
                return NotFound(new { message = "Transmission not found." });
            }

            var auth = await RequireFinancialObjectAuthorizationAsync(transmission.InvoiceId, transmission.MatterId, transmission.ClientId, requireWrite: true, ct);
            if (auth != null) return auth;

            var currentStatus = (transmission.Status ?? string.Empty).Trim().ToLowerInvariant();
            if (currentStatus is not ("rejected" or "error" or "partial"))
            {
                return BadRequest(new { message = "Only rejected/error/partial transmissions can be moved into repair." });
            }

            var notes = Truncate(request?.Notes, 2048);
            var retryable = request?.Retryable ?? true;
            var now = DateTime.UtcNow;

            var repairEvent = new BillingEbillingResultEvent
            {
                TransmissionId = transmission.Id,
                ProviderKey = transmission.ProviderKey,
                ExternalTransmissionId = transmission.ExternalTransmissionId,
                EventType = "status_update",
                Status = "warning",
                InvoiceId = transmission.InvoiceId,
                MatterId = transmission.MatterId,
                ClientId = transmission.ClientId,
                PayorClientId = transmission.PayorClientId,
                ResultCode = "repair_requested",
                ResultMessage = notes ?? "Manual repair requested.",
                ErrorCode = null,
                ErrorCategory = "repair",
                ErrorMessage = null,
                IsFinal = false,
                IsRetryable = retryable,
                OccurredAt = now,
                RecordedAt = now,
                PayloadJson = null,
                MetadataJson = BuildRepairMetadataJson(request),
                RecordedBy = GetUserId()
            };

            var previousStatus = transmission.Status;
            transmission.Status = "queued";
            transmission.ErrorCode = null;
            transmission.ErrorMessage = null;
            transmission.CompletedAt = null;
            transmission.UpdatedAt = now;
            transmission.MetadataJson = MergeMetadataJson(transmission.MetadataJson, null, BuildRepairTransmissionMetadataJson(request, now));

            _context.BillingEbillingResultEvents.Add(repairEvent);
            await _context.SaveChangesAsync(ct);

            await _auditLogger.LogAsync(HttpContext,
                "billing.ebilling.transmission.repair_requested",
                nameof(BillingEbillingTransmission),
                transmission.Id,
                $"Provider={transmission.ProviderKey}, PrevStatus={previousStatus}, NewStatus={transmission.Status}, Retryable={retryable}");

            return Ok(transmission);
        }

        [HttpGet("ledger")]
        public async Task<ActionResult<IEnumerable<BillingLedgerEntry>>> GetLedger([FromQuery] BillingLedgerQuery query, CancellationToken ct)
        {
            return Ok(await _billingEngine.ListLedgerEntriesAsync(query ?? new BillingLedgerQuery(), ct));
        }

        [HttpPost("ledger/adjustment")]
        [Authorize(Policy = "BillingWrite")]
        public async Task<ActionResult<BillingLedgerEntry>> PostLedgerAdjustment([FromBody] ManualLedgerEntryRequest request, CancellationToken ct)
        {
            if (request == null) return BadRequest(new { message = "Request body is required." });
            var auth = await RequireFinancialObjectAuthorizationAsync(request.InvoiceId, request.MatterId, request.ClientId, requireWrite: true, ct);
            if (auth != null) return auth;
            try
            {
                var entry = await _billingEngine.PostManualLedgerEntryAsync(request, GetUserId(), ct);
                await _auditLogger.LogAsync(HttpContext, "billing.ledger.post", nameof(BillingLedgerEntry), entry.Id, $"Type={entry.EntryType}, Domain={entry.LedgerDomain}, Bucket={entry.LedgerBucket}, Amount={entry.Amount}");
                return Ok(entry);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("ledger/{ledgerEntryId}/reverse")]
        [Authorize(Policy = "BillingWrite")]
        public async Task<ActionResult<BillingLedgerEntry>> ReverseLedgerEntry(string ledgerEntryId, [FromBody] LedgerReversalRequest? request, CancellationToken ct)
        {
            var auth = await RequireLedgerEntryAuthorizationAsync(ledgerEntryId, requireWrite: true, ct);
            if (auth != null) return auth;
            try
            {
                var reversal = await _billingEngine.ReverseLedgerEntryAsync(ledgerEntryId, request ?? new LedgerReversalRequest(), GetUserId(), ct);
                if (reversal == null) return NotFound();
                await _auditLogger.LogAsync(HttpContext, "billing.ledger.reverse", nameof(BillingLedgerEntry), ledgerEntryId, $"ReversalEntryId={reversal.Id}");
                return Ok(reversal);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("allocations")]
        public async Task<ActionResult<IEnumerable<BillingPaymentAllocation>>> GetAllocations([FromQuery] string? paymentTransactionId = null, [FromQuery] string? invoiceId = null, CancellationToken ct = default)
        {
            var query = _context.BillingPaymentAllocations.AsQueryable();
            if (!string.IsNullOrWhiteSpace(paymentTransactionId)) query = query.Where(a => a.PaymentTransactionId == paymentTransactionId);
            if (!string.IsNullOrWhiteSpace(invoiceId)) query = query.Where(a => a.InvoiceId == invoiceId);

            var items = await query.OrderByDescending(a => a.AppliedAt).Take(500).ToListAsync(ct);
            return Ok(items);
        }

        [HttpPost("allocations")]
        [Authorize(Policy = "BillingWrite")]
        public async Task<ActionResult<BillingPaymentAllocation>> ApplyAllocation([FromBody] ApplyPaymentAllocationRequest request, CancellationToken ct)
        {
            if (request == null) return BadRequest(new { message = "Request body is required." });
            var auth = await RequireFinancialObjectAuthorizationAsync(request.InvoiceId, request.MatterId, request.ClientId, requireWrite: true, ct);
            if (auth != null) return auth;
            try
            {
                var allocation = await _billingEngine.ApplyPaymentAllocationAsync(request, GetUserId(), ct);
                if (allocation == null) return NotFound();
                await _auditLogger.LogAsync(HttpContext, "billing.allocation.apply", nameof(BillingPaymentAllocation), allocation.Id, $"Payment={allocation.PaymentTransactionId}, Invoice={allocation.InvoiceId}, Amount={allocation.Amount}");
                await TryTriggerOutcomeFeePlannerAsync(allocation.MatterId, "payment_allocation_apply", nameof(BillingPaymentAllocation), allocation.Id, ct);
                return Ok(allocation);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("allocations/{allocationId}/reverse")]
        [Authorize(Policy = "BillingWrite")]
        public async Task<ActionResult<BillingPaymentAllocation>> ReverseAllocation(string allocationId, [FromBody] ReversePaymentAllocationRequest? request, CancellationToken ct)
        {
            var auth = await RequireAllocationAuthorizationAsync(allocationId, requireWrite: true, ct);
            if (auth != null) return auth;
            try
            {
                var allocation = await _billingEngine.ReversePaymentAllocationAsync(allocationId, request ?? new ReversePaymentAllocationRequest(), GetUserId(), ct);
                if (allocation == null) return NotFound();
                await _auditLogger.LogAsync(HttpContext, "billing.allocation.reverse", nameof(BillingPaymentAllocation), allocation.Id, $"Status={allocation.Status}");
                await TryTriggerOutcomeFeePlannerAsync(allocation.MatterId, "payment_allocation_reverse", nameof(BillingPaymentAllocation), allocation.Id, ct);
                return Ok(allocation);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("allocations/{allocationId}/finalize-trust")]
        [Authorize(Policy = "BillingFinalize")]
        public async Task<ActionResult<BillingPaymentAllocation>> FinalizePendingTrustAllocation(string allocationId, CancellationToken ct)
        {
            var auth = await RequireAllocationAuthorizationAsync(allocationId, requireWrite: true, ct);
            if (auth != null) return auth;
            try
            {
                var allocation = await _billingEngine.FinalizePendingTrustAllocationAsync(allocationId, GetUserId(), ct);
                if (allocation == null) return NotFound();
                await _auditLogger.LogAsync(HttpContext, "billing.allocation.finalize_trust", nameof(BillingPaymentAllocation), allocation.Id, $"Status={allocation.Status}");
                await TryTriggerOutcomeFeePlannerAsync(allocation.MatterId, "payment_allocation_finalize_trust", nameof(BillingPaymentAllocation), allocation.Id, ct);
                return Ok(allocation);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("invoices/{invoiceId}/payor-statements")]
        public async Task<IActionResult> GetInvoicePayorStatements(string invoiceId, [FromQuery] string? payorClientId = null, CancellationToken ct = default)
        {
            var invoice = await _context.Invoices
                .AsNoTracking()
                .Include(i => i.LineItems)
                .FirstOrDefaultAsync(i => i.Id == invoiceId, ct);
            if (invoice == null) return NotFound(new { message = "Invoice not found." });

            var invoicePayorAllocationsQuery = _context.InvoicePayorAllocations.AsNoTracking()
                .Where(a => a.InvoiceId == invoiceId);
            var lineAllocationsQuery = _context.InvoiceLinePayorAllocations.AsNoTracking()
                .Where(a => a.InvoiceId == invoiceId);
            if (!string.IsNullOrWhiteSpace(payorClientId))
            {
                var normalizedPayor = payorClientId.Trim();
                invoicePayorAllocationsQuery = invoicePayorAllocationsQuery.Where(a => a.PayorClientId == normalizedPayor);
                lineAllocationsQuery = lineAllocationsQuery.Where(a => a.PayorClientId == normalizedPayor);
            }

            var invoicePayorAllocations = await invoicePayorAllocationsQuery
                .OrderBy(a => a.Priority)
                .ThenByDescending(a => a.IsPrimary)
                .ThenBy(a => a.PayorClientId)
                .ToListAsync(ct);
            var lineAllocations = await lineAllocationsQuery
                .OrderBy(a => a.InvoiceLineItemId)
                .ThenBy(a => a.PayorClientId)
                .ToListAsync(ct);

            var lineMap = invoice.LineItems.ToDictionary(li => li.Id, li => li, StringComparer.Ordinal);
            var allocationIds = invoicePayorAllocations.Select(a => a.PayorClientId).Distinct().ToList();
            var payors = await _context.Clients.AsNoTracking()
                .Where(c => allocationIds.Contains(c.Id))
                .Select(c => new { c.Id, c.Name, c.Email })
                .ToListAsync(ct);
            var payorMap = payors.ToDictionary(p => p.Id, p => p, StringComparer.Ordinal);

            var rows = invoicePayorAllocations.Select(a =>
            {
                var lineRows = lineAllocations.Where(l => l.InvoicePayorAllocationId == a.Id || (string.IsNullOrWhiteSpace(l.InvoicePayorAllocationId) && l.PayorClientId == a.PayorClientId))
                    .Select(l =>
                    {
                        lineMap.TryGetValue(l.InvoiceLineItemId, out var line);
                        return new
                        {
                            lineAllocationId = l.Id,
                            l.InvoiceLineItemId,
                            lineDescription = line?.Description,
                            lineType = line?.Type,
                            l.TaskCode,
                            l.ActivityCode,
                            l.ExpenseCode,
                            l.ResponsibilityType,
                            l.Percent,
                            l.Amount,
                            l.Status
                        };
                    })
                    .ToList();

                payorMap.TryGetValue(a.PayorClientId, out var payor);
                return new
                {
                    invoicePayorAllocationId = a.Id,
                    payorClientId = a.PayorClientId,
                    payorName = payor?.Name,
                    payorEmail = payor?.Email,
                    a.ResponsibilityType,
                    a.Percent,
                    a.AmountCap,
                    a.Priority,
                    a.Status,
                    a.IsPrimary,
                    a.AllocatedAmount,
                    a.Terms,
                    a.Reference,
                    a.PurchaseOrder,
                    lineCount = lineRows.Count,
                    lineAllocatedAmount = lineRows.Sum(x => (decimal)x.Amount),
                    lines = lineRows
                };
            }).ToList();

            return Ok(new
            {
                invoice = new
                {
                    invoice.Id,
                    invoice.Number,
                    invoice.ClientId,
                    invoice.MatterId,
                    invoice.Total,
                    invoice.AmountPaid,
                    invoice.Balance,
                    invoice.IssueDate,
                    invoice.DueDate,
                    invoice.Status
                },
                generatedAt = DateTime.UtcNow,
                rows
            });
        }

        [HttpGet("collections/payor-aging")]
        public async Task<IActionResult> GetPayorAging([FromQuery] DateTime? asOfUtc = null, [FromQuery] int limit = 200, CancellationToken ct = default)
        {
            var asOf = (asOfUtc ?? DateTime.UtcNow).ToUniversalTime();
            var invoices = await _context.Invoices.AsNoTracking()
                .Where(i => i.Balance > 0m)
                .Select(i => new
                {
                    i.Id,
                    i.ClientId,
                    i.Number,
                    i.MatterId,
                    i.Total,
                    i.AmountPaid,
                    i.Balance,
                    i.DueDate,
                    i.IssueDate,
                    i.Status
                })
                .ToListAsync(ct);

            if (invoices.Count == 0)
            {
                return Ok(new
                {
                    generatedAt = DateTime.UtcNow,
                    asOfUtc = asOf,
                    summary = new { rows = 0, totalOutstanding = 0m, payors = 0 },
                    buckets = Array.Empty<object>(),
                    payorSegments = Array.Empty<object>(),
                    rows = Array.Empty<object>()
                });
            }

            var invoiceIds = invoices.Select(i => i.Id).ToList();
            var payorAllocations = await _context.InvoicePayorAllocations.AsNoTracking()
                .Where(a => invoiceIds.Contains(a.InvoiceId) && a.Status == "active")
                .ToListAsync(ct);
            var appliedAllocations = await _context.BillingPaymentAllocations.AsNoTracking()
                .Where(a => invoiceIds.Contains(a.InvoiceId) && a.Status == "applied")
                .Select(a => new { a.InvoiceId, a.PayorClientId, a.InvoicePayorAllocationId, a.Amount })
                .ToListAsync(ct);

            var clientIds = invoices.Select(i => i.ClientId)
                .Concat(payorAllocations.Select(a => a.PayorClientId))
                .Concat(appliedAllocations.Where(a => !string.IsNullOrWhiteSpace(a.PayorClientId)).Select(a => a.PayorClientId!))
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var payorMap = await _context.Clients.AsNoTracking()
                .Where(c => clientIds.Contains(c.Id))
                .Select(c => new { c.Id, c.Name, c.Type })
                .ToDictionaryAsync(c => c.Id, c => c, StringComparer.Ordinal, ct);

            var appliedByAllocationId = appliedAllocations
                .Where(a => !string.IsNullOrWhiteSpace(a.InvoicePayorAllocationId))
                .GroupBy(a => a.InvoicePayorAllocationId!, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount), StringComparer.Ordinal);

            var appliedByInvoicePayor = appliedAllocations
                .Where(a => string.IsNullOrWhiteSpace(a.InvoicePayorAllocationId) && !string.IsNullOrWhiteSpace(a.PayorClientId))
                .GroupBy(a => $"{a.InvoiceId}::{a.PayorClientId}", StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount), StringComparer.Ordinal);

            var rows = new List<object>();
            var payorAgingRows = new List<(string Segment, string? PayorClientId, string? PayorName, decimal Outstanding, int DaysPastDue, string BucketKey, string BucketLabel)>();

            foreach (var invoice in invoices)
            {
                var dueDate = invoice.DueDate ?? invoice.IssueDate;
                var dueUtc = dueDate.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(dueDate, DateTimeKind.Utc) : dueDate.ToUniversalTime();
                var daysPastDue = Math.Max(0, (int)Math.Floor((asOf.Date - dueUtc.Date).TotalDays));
                var (bucketKey, bucketLabel) = GetAgingBucket(daysPastDue);

                var invoiceAllocationRows = payorAllocations.Where(a => a.InvoiceId == invoice.Id).ToList();
                if (invoiceAllocationRows.Count == 0)
                {
                    payorMap.TryGetValue(invoice.ClientId, out var legacyClient);
                    var segment = NormalizePayorSegment(legacyClient?.Type);
                    var outstanding = NormalizeMoney(invoice.Balance);
                    payorAgingRows.Add((segment, invoice.ClientId, legacyClient?.Name, outstanding, daysPastDue, bucketKey, bucketLabel));
                    rows.Add(new
                    {
                        invoiceId = invoice.Id,
                        invoiceNumber = invoice.Number,
                        invoiceStatus = invoice.Status.ToString(),
                        payorClientId = invoice.ClientId,
                        payorName = legacyClient?.Name,
                        segment,
                        outstanding,
                        allocatedAmount = NormalizeMoney(invoice.Total),
                        paidAllocated = NormalizeMoney(invoice.AmountPaid),
                        daysPastDue,
                        bucketKey,
                        bucketLabel,
                        isLegacyPrimary = true
                    });
                    continue;
                }

                foreach (var allocation in invoiceAllocationRows)
                {
                    payorMap.TryGetValue(allocation.PayorClientId, out var payor);
                    var segment = NormalizePayorSegment(payor?.Type);
                    var allocatedAmount = NormalizeMoney(allocation.AllocatedAmount);
                    var paid = appliedByAllocationId.TryGetValue(allocation.Id, out var byAlloc)
                        ? NormalizeMoney(byAlloc)
                        : appliedByInvoicePayor.TryGetValue($"{invoice.Id}::{allocation.PayorClientId}", out var byInvoicePayor)
                            ? NormalizeMoney(byInvoicePayor)
                            : 0m;
                    var outstanding = NormalizeMoney(Math.Max(0m, allocatedAmount - paid));
                    if (outstanding <= 0m) continue;

                    payorAgingRows.Add((segment, allocation.PayorClientId, payor?.Name, outstanding, daysPastDue, bucketKey, bucketLabel));
                    rows.Add(new
                    {
                        invoiceId = invoice.Id,
                        invoiceNumber = invoice.Number,
                        invoiceStatus = invoice.Status.ToString(),
                        invoicePayorAllocationId = allocation.Id,
                        payorClientId = allocation.PayorClientId,
                        payorName = payor?.Name,
                        segment,
                        responsibilityType = allocation.ResponsibilityType,
                        isPrimary = allocation.IsPrimary,
                        allocatedAmount,
                        paidAllocated = paid,
                        outstanding,
                        daysPastDue,
                        bucketKey,
                        bucketLabel
                    });
                }
            }

            var bucketAgg = payorAgingRows
                .GroupBy(r => new { r.BucketKey, r.BucketLabel })
                .Select(g => new
                {
                    bucketKey = g.Key.BucketKey,
                    bucketLabel = g.Key.BucketLabel,
                    totalOutstanding = NormalizeMoney(g.Sum(x => x.Outstanding)),
                    rowCount = g.Count(),
                    payorCount = g.Select(x => x.PayorClientId).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).Count()
                })
                .OrderBy(x => GetAgingBucketOrder(x.bucketKey))
                .ToList();

            var segmentAgg = payorAgingRows
                .GroupBy(r => r.Segment, StringComparer.OrdinalIgnoreCase)
                .Select(g => new
                {
                    segment = g.Key,
                    totalOutstanding = NormalizeMoney(g.Sum(x => x.Outstanding)),
                    rowCount = g.Count(),
                    payorCount = g.Select(x => x.PayorClientId).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).Count(),
                    buckets = g.GroupBy(x => x.BucketKey)
                        .Select(bg => new
                        {
                            bucketKey = bg.Key,
                            bucketLabel = bg.First().BucketLabel,
                            totalOutstanding = NormalizeMoney(bg.Sum(x => x.Outstanding)),
                            rowCount = bg.Count()
                        })
                        .OrderBy(x => GetAgingBucketOrder(x.bucketKey))
                        .ToList()
                })
                .OrderByDescending(x => x.totalOutstanding)
                .ToList();

            var topRows = rows
                .Cast<dynamic>()
                .OrderByDescending(r => (decimal)r.outstanding)
                .ThenByDescending(r => (int)r.daysPastDue)
                .Take(Math.Clamp(limit, 1, 1000))
                .ToList();

            return Ok(new
            {
                generatedAt = DateTime.UtcNow,
                asOfUtc = asOf,
                summary = new
                {
                    rows = payorAgingRows.Count,
                    totalOutstanding = NormalizeMoney(payorAgingRows.Sum(x => x.Outstanding)),
                    payors = payorAgingRows.Select(x => x.PayorClientId).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).Count()
                },
                buckets = bucketAgg,
                payorSegments = segmentAgg,
                rows = topRows
            });
        }

        [HttpGet("trust/reconciliation")]
        public async Task<ActionResult<TrustThreeWayReconciliationResult>> GetTrustReconciliation([FromQuery] string? trustAccountId = null, [FromQuery] DateTime? asOfUtc = null, CancellationToken ct = default)
        {
            var result = await _billingEngine.GetTrustThreeWayReconciliationAsync(new TrustReconciliationRequest
            {
                TrustAccountId = trustAccountId,
                AsOfUtc = asOfUtc
            }, ct);

            try
            {
                var mismatched = (result.Accounts ?? []).Count(a =>
                    a.BankVsClientLedgerDiff != 0m ||
                    a.ClientLedgerVsTrustLedgerDiff != 0m ||
                    a.BankVsTrustLedgerDiff != 0m);

                _context.TrustReconciliationSnapshots.Add(new TrustReconciliationSnapshot
                {
                    TrustAccountId = string.IsNullOrWhiteSpace(trustAccountId) ? null : trustAccountId.Trim(),
                    AsOfUtc = result.AsOfUtc,
                    AccountCount = result.Accounts?.Count ?? 0,
                    MismatchedAccountCount = mismatched,
                    BankBalance = result.Totals.BankBalance,
                    ClientLedgerTotal = result.Totals.ClientLedgerTotal,
                    TrustTransactionsNet = result.Totals.TrustTransactionsNet,
                    BillingTrustLedgerTotal = result.Totals.BillingTrustLedgerTotal,
                    BankVsClientLedgerDiff = result.Totals.BankVsClientLedgerDiff,
                    ClientLedgerVsTrustLedgerDiff = result.Totals.ClientLedgerVsTrustLedgerDiff,
                    BankVsTrustLedgerDiff = result.Totals.BankVsTrustLedgerDiff,
                    DataQuality = "computed",
                    Source = "api_read",
                    CapturedBy = GetUserId(),
                    MetadataJson = null
                });

                await _context.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                await _auditLogger.LogAsync(HttpContext, "billing.trust_reconciliation.snapshot_failed", nameof(TrustReconciliationSnapshot), trustAccountId ?? "aggregate", ex.Message);
            }

            return Ok(result);
        }

        private static string? NormalizeRequiredKey(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var trimmed = value.Trim().ToLowerInvariant();
            return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
        }

        private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static string? Truncate(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var trimmed = value.Trim();
            return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
        }

        private static string? TruncateJson(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var trimmed = value.Trim();
            if (trimmed.Length > 64000) trimmed = trimmed[..64000];
            try
            {
                using var _ = JsonDocument.Parse(trimmed);
                return trimmed;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static string? NormalizeEbillingFormat(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var normalized = value.Trim().ToLowerInvariant();
            return normalized is "ledes98b" or "ledes1998bi" or "xml" or "json" ? normalized : normalized;
        }

        private static string NormalizeEbillingTransmissionStatus(string? value)
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? "queued" : value.Trim().ToLowerInvariant();
            return normalized is "queued" or "submitted" or "accepted" or "rejected" or "error" or "partial" ? normalized : "queued";
        }

        private static string NormalizeEbillingEventType(string? value)
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? "submission_result" : value.Trim().ToLowerInvariant();
            return normalized is "submission_result" or "provider_error" or "status_update" or "ack" ? normalized : "submission_result";
        }

        private static string NormalizeEbillingEventStatus(string? value)
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? "received" : value.Trim().ToLowerInvariant();
            return normalized is "received" or "accepted" or "rejected" or "error" or "warning" ? normalized : "received";
        }

        private static string NormalizeTransmissionStatusFromEvent(string currentStatus, string eventStatus, bool isFinal)
        {
            if (!isFinal && currentStatus is "accepted" or "rejected" or "error")
            {
                return currentStatus;
            }

            return eventStatus switch
            {
                "accepted" => "accepted",
                "rejected" => "rejected",
                "error" => "error",
                "warning" => isFinal ? "partial" : "submitted",
                "received" => string.IsNullOrWhiteSpace(currentStatus) || currentStatus == "queued" ? "submitted" : currentStatus,
                _ => currentStatus
            };
        }

        private static decimal NormalizeMoney(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

        private static (string Key, string Label) GetAgingBucket(int daysPastDue)
        {
            if (daysPastDue <= 0) return ("current", "Current");
            if (daysPastDue <= 30) return ("1_30", "1-30");
            if (daysPastDue <= 60) return ("31_60", "31-60");
            if (daysPastDue <= 90) return ("61_90", "61-90");
            return ("91_plus", "91+");
        }

        private static int GetAgingBucketOrder(string? key)
        {
            return (key ?? string.Empty) switch
            {
                "current" => 0,
                "1_30" => 1,
                "31_60" => 2,
                "61_90" => 3,
                "91_plus" => 4,
                _ => 99
            };
        }

        private static string NormalizePayorSegment(string? clientType)
        {
            if (string.IsNullOrWhiteSpace(clientType)) return "client";
            var normalized = clientType.Trim().ToLowerInvariant();
            if (normalized.Contains("corp") || normalized.Contains("company") || normalized.Contains("business")) return "corporate";
            if (normalized.Contains("insurance") || normalized.Contains("carrier") || normalized.Contains("third")) return "third_party";
            return "client";
        }

        private static string? BuildRepairMetadataJson(RepairEbillingTransmissionRequest? request)
        {
            var payload = new
            {
                repairRequested = true,
                notes = Truncate(request?.Notes, 2048),
                retryable = request?.Retryable ?? true,
                request?.ReasonCode
            };
            return JsonSerializer.Serialize(payload);
        }

        private static string? BuildRepairTransmissionMetadataJson(RepairEbillingTransmissionRequest? request, DateTime nowUtc)
        {
            var payload = new
            {
                repairRequested = true,
                repairedAt = nowUtc,
                notes = Truncate(request?.Notes, 2048),
                retryable = request?.Retryable ?? true,
                request?.ReasonCode
            };
            return JsonSerializer.Serialize(payload);
        }

        private static string? MergeMetadataJson(string? existingJson, string? idempotencyKey, string? requestedJson)
        {
            if (string.IsNullOrWhiteSpace(idempotencyKey) && string.IsNullOrWhiteSpace(requestedJson))
            {
                return existingJson;
            }

            var map = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(existingJson))
            {
                TryMergeObject(existingJson!, map);
            }
            if (!string.IsNullOrWhiteSpace(requestedJson))
            {
                TryMergeObject(requestedJson!, map);
            }
            if (!string.IsNullOrWhiteSpace(idempotencyKey))
            {
                map["idempotencyKey"] = idempotencyKey;
            }

            return map.Count == 0 ? null : TruncateJson(JsonSerializer.Serialize(map));
        }

        private static void TryMergeObject(string json, IDictionary<string, object?> target)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) return;
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    target[prop.Name] = prop.Value.ValueKind switch
                    {
                        JsonValueKind.String => prop.Value.GetString(),
                        JsonValueKind.Number => prop.Value.GetRawText(),
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        JsonValueKind.Null => null,
                        _ => prop.Value.GetRawText()
                    };
                }
            }
            catch (JsonException)
            {
                // Ignore malformed metadata payloads; caller still gets core event persistence.
            }
        }

        private async Task TryCaptureBillingEngineLedesPreviewEventAsync(string prebillId, LedesPreviewResult preview, CancellationToken ct)
        {
            if (string.Equals(preview.Format, "none", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var batch = await _context.BillingPrebillBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == prebillId, ct);
            if (batch == null) return;

            var transmission = await UpsertEngineTransmissionAsync(
                providerKey: "billing-engine",
                prebillBatchId: batch.Id,
                invoiceId: batch.InvoiceId,
                matterId: batch.MatterId,
                clientId: batch.ClientId,
                format: preview.Format,
                status: preview.Warnings.Count > 0 ? "partial" : "queued",
                reference: $"ledes_preview:{batch.Id}",
                metadataJson: JsonSerializer.Serialize(new { phase = "precheck", warningCount = preview.Warnings.Count }),
                ct: ct);

            if (preview.Warnings.Count > 0)
            {
                var warningText = string.Join(" | ", preview.Warnings.Take(5));
                await UpsertEngineResultEventAsync(
                    transmission,
                    eventType: "status_update",
                    status: "warning",
                    resultCode: "PRECHECK_WARNINGS",
                    resultMessage: warningText,
                    errorCode: null,
                    errorCategory: "validation",
                    errorMessage: null,
                    isFinal: false,
                    payloadJson: null,
                    metadataJson: JsonSerializer.Serialize(new { warningCount = preview.Warnings.Count }),
                    ct: ct);
            }
        }

        private async Task TryCaptureBillingEngineEbillingTransmissionAsync(BillingPrebillBatch batch, Invoice invoice, string status, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(batch.PolicyId))
            {
                return;
            }

            var policy = await _context.MatterBillingPolicies.AsNoTracking().FirstOrDefaultAsync(p => p.Id == batch.PolicyId, ct);
            if (policy == null ||
                !string.Equals(policy.EbillingStatus, "enabled", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(policy.EbillingFormat) ||
                string.Equals(policy.EbillingFormat, "none", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            await UpsertEngineTransmissionAsync(
                providerKey: "billing-engine",
                prebillBatchId: batch.Id,
                invoiceId: invoice.Id,
                matterId: batch.MatterId,
                clientId: batch.ClientId,
                format: policy.EbillingFormat,
                status: status,
                reference: $"prebill_finalize:{batch.Id}",
                metadataJson: JsonSerializer.Serialize(new { phase = "finalize", invoiceNumber = invoice.Number }),
                ct: ct);
        }

        private async Task<BillingEbillingTransmission> UpsertEngineTransmissionAsync(
            string providerKey,
            string? prebillBatchId,
            string? invoiceId,
            string? matterId,
            string? clientId,
            string? format,
            string status,
            string? reference,
            string? metadataJson,
            CancellationToken ct)
        {
            var normalizedProviderKey = NormalizeRequiredKey(providerKey, 64) ?? "billing-engine";
            var now = DateTime.UtcNow;
            var transmission = await _context.BillingEbillingTransmissions
                .FirstOrDefaultAsync(t =>
                    t.ProviderKey == normalizedProviderKey &&
                    t.PrebillBatchId == prebillBatchId &&
                    t.InvoiceId == invoiceId &&
                    t.Reference == reference, ct);

            var isNew = transmission == null;
            transmission ??= new BillingEbillingTransmission
            {
                ProviderKey = normalizedProviderKey,
                CreatedAt = now,
                CreatedBy = GetUserId(),
                SubmittedAt = now
            };

            transmission.InvoiceId = NullIfEmpty(invoiceId);
            transmission.MatterId = NullIfEmpty(matterId);
            transmission.ClientId = NullIfEmpty(clientId);
            transmission.PrebillBatchId = NullIfEmpty(prebillBatchId);
            transmission.Format = NormalizeEbillingFormat(format);
            transmission.Status = NormalizeEbillingTransmissionStatus(status);
            transmission.Reference = Truncate(reference, 255);
            transmission.MetadataJson = MergeMetadataJson(transmission.MetadataJson, null, metadataJson);
            transmission.UpdatedAt = now;

            if (isNew)
            {
                _context.BillingEbillingTransmissions.Add(transmission);
            }

            await _context.SaveChangesAsync(ct);
            return transmission;
        }

        private async Task<BillingEbillingResultEvent> UpsertEngineResultEventAsync(
            BillingEbillingTransmission transmission,
            string eventType,
            string status,
            string? resultCode,
            string? resultMessage,
            string? errorCode,
            string? errorCategory,
            string? errorMessage,
            bool isFinal,
            string? payloadJson,
            string? metadataJson,
            CancellationToken ct)
        {
            var signature = $"{transmission.Id}:{eventType}:{status}:{resultCode}:{resultMessage}:{errorCode}:{errorCategory}";
            var existing = await _context.BillingEbillingResultEvents
                .FirstOrDefaultAsync(e => e.TransmissionId == transmission.Id &&
                                          e.EventType == eventType &&
                                          e.Status == status &&
                                          e.MetadataJson != null &&
                                          e.MetadataJson.Contains(signature), ct);
            if (existing != null)
            {
                return existing;
            }

            var ev = new BillingEbillingResultEvent
            {
                TransmissionId = transmission.Id,
                ProviderKey = transmission.ProviderKey,
                ExternalTransmissionId = transmission.ExternalTransmissionId,
                EventType = NormalizeEbillingEventType(eventType),
                Status = NormalizeEbillingEventStatus(status),
                InvoiceId = transmission.InvoiceId,
                MatterId = transmission.MatterId,
                ClientId = transmission.ClientId,
                PayorClientId = transmission.PayorClientId,
                ResultCode = Truncate(resultCode, 128),
                ResultMessage = Truncate(resultMessage, 2048),
                ErrorCode = Truncate(errorCode, 128),
                ErrorCategory = Truncate(errorCategory, 64),
                ErrorMessage = Truncate(errorMessage, 2048),
                IsFinal = isFinal,
                IsRetryable = false,
                OccurredAt = DateTime.UtcNow,
                RecordedAt = DateTime.UtcNow,
                PayloadJson = TruncateJson(payloadJson),
                MetadataJson = MergeMetadataJson(null, signature, metadataJson),
                RecordedBy = GetUserId()
            };
            _context.BillingEbillingResultEvents.Add(ev);
            await _context.SaveChangesAsync(ct);
            return ev;
        }

        private string? GetUserId()
        {
            return User.FindFirst("sub")?.Value ??
                   User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        }

        private async Task<ActionResult?> RequireMatterAuthorizationAsync(string? matterId, bool requireWrite, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(matterId))
            {
                return BadRequest(new { message = "MatterId is required." });
            }

            var exists = await _context.Matters.AsNoTracking().AnyAsync(m => m.Id == matterId, ct);
            if (!exists)
            {
                return NotFound(new { message = "Matter not found." });
            }

            var allowed = requireWrite
                ? await _billingAuthorization.CanManageMatterAsync(matterId, User, ct)
                : await _billingAuthorization.CanReadMatterAsync(matterId, User, ct);

            return allowed ? null : Forbid();
        }

        private async Task<ActionResult?> RequireOptionalMatterClientAuthorizationAsync(string? matterId, string? clientId, bool requireWrite, CancellationToken ct)
        {
            if (!string.IsNullOrWhiteSpace(matterId))
            {
                var matterAuth = await RequireMatterAuthorizationAsync(matterId, requireWrite, ct);
                if (matterAuth != null) return matterAuth;
            }

            if (!string.IsNullOrWhiteSpace(clientId))
            {
                var exists = await _context.Clients.AsNoTracking().AnyAsync(c => c.Id == clientId, ct);
                if (!exists)
                {
                    return NotFound(new { message = "Client not found." });
                }

                var allowed = requireWrite
                    ? await _billingAuthorization.CanManageClientAsync(clientId, User, ct)
                    : await _billingAuthorization.CanReadClientAsync(clientId, User, ct);
                if (!allowed) return Forbid();
            }

            return null;
        }

        private async Task<ActionResult?> RequireFinancialObjectAuthorizationAsync(string? invoiceId, string? matterId, string? clientId, bool requireWrite, CancellationToken ct)
        {
            if (!string.IsNullOrWhiteSpace(invoiceId))
            {
                var invoice = await _context.Invoices.AsNoTracking().FirstOrDefaultAsync(i => i.Id == invoiceId, ct);
                if (invoice == null)
                {
                    return NotFound(new { message = "Invoice not found." });
                }

                if (!string.IsNullOrWhiteSpace(matterId) &&
                    !string.Equals(invoice.MatterId, matterId, StringComparison.Ordinal))
                {
                    return BadRequest(new { message = "MatterId does not match invoice." });
                }

                if (!string.IsNullOrWhiteSpace(clientId) &&
                    !string.Equals(invoice.ClientId, clientId, StringComparison.Ordinal))
                {
                    return BadRequest(new { message = "ClientId does not match invoice." });
                }

                var allowed = requireWrite
                    ? await _billingAuthorization.CanManageInvoiceAsync(invoice, User, ct)
                    : await _billingAuthorization.CanReadInvoiceAsync(invoice, User, ct);
                return allowed ? null : Forbid();
            }

            var targetAuth = await RequireOptionalMatterClientAuthorizationAsync(matterId, clientId, requireWrite, ct);
            if (targetAuth != null) return targetAuth;

            if (string.IsNullOrWhiteSpace(matterId) && string.IsNullOrWhiteSpace(clientId) && !_billingAuthorization.IsPrivileged(User))
            {
                return BadRequest(new { message = "InvoiceId, MatterId, or ClientId is required for non-privileged billing operations." });
            }

            return null;
        }

        private async Task<ActionResult?> RequirePrebillAuthorizationAsync(string prebillId, bool requireWrite, CancellationToken ct)
        {
            var batch = await _context.BillingPrebillBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == prebillId, ct);
            if (batch == null)
            {
                return NotFound();
            }

            return await RequireMatterAuthorizationAsync(batch.MatterId, requireWrite, ct);
        }

        private async Task<ActionResult?> RequirePrebillLineAuthorizationAsync(string prebillLineId, bool requireWrite, CancellationToken ct)
        {
            var line = await _context.BillingPrebillLines.AsNoTracking().FirstOrDefaultAsync(l => l.Id == prebillLineId, ct);
            if (line == null)
            {
                return NotFound();
            }

            return await RequireMatterAuthorizationAsync(line.MatterId, requireWrite, ct);
        }

        private async Task<ActionResult?> RequireLedgerEntryAuthorizationAsync(string ledgerEntryId, bool requireWrite, CancellationToken ct)
        {
            var entry = await _context.BillingLedgerEntries.AsNoTracking().FirstOrDefaultAsync(e => e.Id == ledgerEntryId, ct);
            if (entry == null)
            {
                return NotFound();
            }

            return await RequireFinancialObjectAuthorizationAsync(entry.InvoiceId, entry.MatterId, entry.ClientId, requireWrite, ct);
        }

        private async Task<ActionResult?> RequireAllocationAuthorizationAsync(string allocationId, bool requireWrite, CancellationToken ct)
        {
            var allocation = await _context.BillingPaymentAllocations.AsNoTracking().FirstOrDefaultAsync(a => a.Id == allocationId, ct);
            if (allocation == null)
            {
                return NotFound();
            }

            return await RequireFinancialObjectAuthorizationAsync(allocation.InvoiceId, allocation.MatterId, allocation.ClientId, requireWrite, ct);
        }

        private Task TryTriggerOutcomeFeePlannerAsync(string? matterId, string triggerType, string entityType, string entityId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(matterId) && string.IsNullOrWhiteSpace(entityId))
            {
                return Task.CompletedTask;
            }

            try
            {
                _workflowTriggerDispatcher.TryEnqueue(
                    GetUserId() ?? "system",
                    new OutcomeFeePlanTriggerRequest
                    {
                        MatterId = matterId,
                        TriggerType = triggerType,
                        TriggerEntityType = entityType,
                        TriggerEntityId = entityId
                    },
                    new ClientTransparencyTriggerRequest
                    {
                        MatterId = matterId,
                        TriggerType = triggerType,
                        TriggerEntityType = entityType,
                        TriggerEntityId = entityId
                    });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Workflow trigger enqueue failed for {EntityType} {EntityId}", entityType, entityId);
            }

            return Task.CompletedTask;
        }

        public sealed class ReviewDecisionDto
        {
            public string? Notes { get; set; }
        }

        public sealed class RecordEbillingTransmissionRequest
        {
            public string ProviderKey { get; set; } = string.Empty;
            public string? InvoiceId { get; set; }
            public string? MatterId { get; set; }
            public string? ClientId { get; set; }
            public string? PayorClientId { get; set; }
            public string? PrebillBatchId { get; set; }
            public string? Format { get; set; }
            public string? Status { get; set; }
            public string? ExternalTransmissionId { get; set; }
            public string? CorrelationId { get; set; }
            public string? Reference { get; set; }
            public string? ErrorCode { get; set; }
            public string? ErrorMessage { get; set; }
            public DateTime? SubmittedAt { get; set; }
            public DateTime? CompletedAt { get; set; }
            public string? RequestPayloadJson { get; set; }
            public string? ResponsePayloadJson { get; set; }
            public string? MetadataJson { get; set; }
            public string? IdempotencyKey { get; set; }
        }

        public sealed class RecordEbillingResultEventRequest
        {
            public string ProviderKey { get; set; } = string.Empty;
            public string? TransmissionId { get; set; }
            public string? ExternalTransmissionId { get; set; }
            public string? ExternalEventId { get; set; }
            public string? EventType { get; set; }
            public string? Status { get; set; }
            public string? InvoiceId { get; set; }
            public string? MatterId { get; set; }
            public string? ClientId { get; set; }
            public string? PayorClientId { get; set; }
            public string? ResultCode { get; set; }
            public string? ResultMessage { get; set; }
            public string? ErrorCode { get; set; }
            public string? ErrorCategory { get; set; }
            public string? ErrorMessage { get; set; }
            public bool? IsFinal { get; set; }
            public bool? IsRetryable { get; set; }
            public DateTime? OccurredAt { get; set; }
            public string? PayloadJson { get; set; }
            public string? MetadataJson { get; set; }
            public string? IdempotencyKey { get; set; }
        }

        public sealed class RepairEbillingTransmissionRequest
        {
            public string? Notes { get; set; }
            public string? ReasonCode { get; set; }
            public bool? Retryable { get; set; }
        }
    }
}
