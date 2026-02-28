using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using JurisFlow.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace JurisFlow.Server.Controllers
{
    [Route("api/client-transparency")]
    [ApiController]
    [Authorize(Policy = "StaffOnly")]
    public class ClientTransparencyController : ControllerBase
    {
        private static readonly HashSet<string> AllowedTriggerEntityTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            nameof(Matter),
            "Task",
            nameof(Invoice),
            nameof(PaymentTransaction),
            nameof(CourtDocketEntry),
            nameof(EfilingSubmission),
            nameof(OutcomeFeePlan),
            nameof(OutcomeFeePlanVersion),
            nameof(ClientTransparencySnapshot)
        };

        private static readonly HashSet<string> AllowedReviewActions = new(StringComparer.OrdinalIgnoreCase)
        {
            "approve",
            "reject",
            "rewrite"
        };
        private static readonly HashSet<string> AllowedTriggerTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "manual_regenerate",
            "manual_trigger",
            "client_portal_view",
            "matter_status_change",
            "task_update",
            "invoice_update",
            "payment_update",
            "court_docket_sync",
            "efiling_status_change",
            "planner_update"
        };
        private static readonly HashSet<string> AllowedPublishPolicies = new(StringComparer.OrdinalIgnoreCase)
        {
            "warn_only",
            "auto_publish_safe",
            "review_required_for_delay_reason",
            "review_required_for_cost_impact_change_gt_x",
            "block_on_low_confidence"
        };

        private readonly JurisFlowDbContext _context;
        private readonly TenantContext _tenantContext;
        private readonly ClientTransparencyService _transparencyService;
        private readonly AuditLogger _auditLogger;
        private readonly ILogger<ClientTransparencyController> _logger;

        public ClientTransparencyController(
            JurisFlowDbContext context,
            TenantContext tenantContext,
            ClientTransparencyService transparencyService,
            AuditLogger auditLogger,
            ILogger<ClientTransparencyController> logger)
        {
            _context = context;
            _tenantContext = tenantContext;
            _transparencyService = transparencyService;
            _auditLogger = auditLogger;
            _logger = logger;
        }

        [HttpGet("matter/{matterId}")]
        public async Task<ActionResult<ClientTransparencySnapshotDetailResult>> GetCurrentSnapshot(string matterId, CancellationToken ct)
        {
            try
            {
                var normalizedMatterId = await RequireMatterAccessAsync(matterId, ct);
                var result = await _transparencyService.GetCurrentSnapshotForMatterAsync(normalizedMatterId, ct);
                if (result == null) return NotFound();

                await _auditLogger.LogAsync(HttpContext, "client_transparency.get_current", "Matter", normalizedMatterId, $"SnapshotId={result.Snapshot?.Id}, Version={result.Snapshot?.VersionNumber}");
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return HandleInvalidOperation(ex, "get_current");
            }
        }

        [EnableRateLimiting("AdminDangerousOps")]
        [HttpPost("matter/{matterId}/regenerate")]
        public async Task<ActionResult<ClientTransparencySnapshotDetailResult>> RegenerateSnapshot(string matterId, [FromBody] ClientTransparencyRegenerateRequest? request, CancellationToken ct)
        {
            try
            {
                var userId = RequireUserId();
                var normalizedMatterId = await RequireMatterAccessAsync(matterId, ct);
                request ??= new ClientTransparencyRegenerateRequest();
                if (string.IsNullOrWhiteSpace(request.TriggeredBy))
                {
                    request.TriggeredBy = userId;
                }
                NormalizeRegenerateRequest(request);

                var result = await _transparencyService.RegenerateSnapshotAsync(normalizedMatterId, request, userId, ct);
                await _auditLogger.LogAsync(HttpContext, "client_transparency.regenerate", "Matter", normalizedMatterId,
                    $"SnapshotId={result.Snapshot?.Id}, Version={result.Snapshot?.VersionNumber}, Trigger={request.TriggerType ?? "manual_regenerate"}");
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return HandleInvalidOperation(ex, "regenerate");
            }
        }

        [HttpGet("matter/{matterId}/history")]
        public async Task<ActionResult<ClientTransparencyHistoryResult>> GetUpdateHistory(string matterId, [FromQuery] int limit = 50, CancellationToken ct = default)
        {
            try
            {
                var normalizedMatterId = await RequireMatterAccessAsync(matterId, ct);
                var normalizedLimit = Math.Clamp(limit <= 0 ? 50 : limit, 1, 200);
                var result = await _transparencyService.GetUpdateHistoryForMatterAsync(normalizedMatterId, normalizedLimit, ct);
                await _auditLogger.LogAsync(HttpContext, "client_transparency.get_history", "Matter", normalizedMatterId, $"Events={result.Events.Count}, Limit={normalizedLimit}");
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return HandleInvalidOperation(ex, "get_history");
            }
        }

        [EnableRateLimiting("AdminDangerousOps")]
        [HttpPost("triggers")]
        public async Task<ActionResult<ClientTransparencyTriggerResult>> TriggerRecompute([FromBody] ClientTransparencyTriggerRequest request, CancellationToken ct)
        {
            if (request == null)
            {
                return BadRequest(new { message = "Request body is required." });
            }

            try
            {
                var userId = RequireUserId();
                request.MatterId = await RequireMatterAccessAsync(request.MatterId, ct);
                NormalizeTriggerRequest(request);

                var result = await _transparencyService.TryProcessTriggerAsync(request, userId, ct);
                if (!string.IsNullOrWhiteSpace(result.MatterId))
                {
                    await _auditLogger.LogAsync(HttpContext, "client_transparency.trigger", "Matter", result.MatterId,
                        $"Trigger={result.TriggerType}, Recomputed={result.Recomputed}, Snapshot={result.CurrentSnapshotId ?? "none"}");
                }
                else
                {
                    await _auditLogger.LogAsync(HttpContext, "client_transparency.trigger", "Matter", request.MatterId!, $"Trigger={request.TriggerType}, Recomputed={result.Recomputed}");
                }
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return HandleInvalidOperation(ex, "trigger");
            }
        }

        [HttpGet("matter/{matterId}/review-workspace")]
        public async Task<ActionResult<ClientTransparencyReviewWorkspaceResult>> GetReviewWorkspace(string matterId, CancellationToken ct)
        {
            try
            {
                var normalizedMatterId = await RequireMatterAccessAsync(matterId, ct);
                var workspace = await _transparencyService.GetReviewWorkspaceForMatterAsync(normalizedMatterId, ct);
                await _auditLogger.LogAsync(HttpContext, "client_transparency.review_workspace", "Matter", normalizedMatterId,
                    $"Draft={workspace.Draft?.Snapshot?.Id ?? "none"}, Published={workspace.Published?.Snapshot?.Id ?? "none"}");
                return Ok(workspace);
            }
            catch (InvalidOperationException ex)
            {
                return HandleInvalidOperation(ex, "review_workspace");
            }
        }

        [Authorize(Roles = "Admin,Partner,SecurityAdmin")]
        [EnableRateLimiting("AdminDangerousOps")]
        [HttpPost("matter/{matterId}/policy")]
        public async Task<ActionResult<ClientTransparencyPublishPolicySummary>> UpsertMatterPolicy(string matterId, [FromBody] ClientTransparencyPolicyUpsertRequest request, CancellationToken ct)
        {
            if (request == null) return BadRequest(new { message = "Request body is required." });

            try
            {
                var userId = RequireUserId();
                var normalizedMatterId = await RequireMatterAccessAsync(matterId, ct);
                NormalizePolicyRequest(request);

                var profile = await _transparencyService.UpsertMatterPolicyAsync(normalizedMatterId, request, userId, ct);
                var workspace = await _transparencyService.GetReviewWorkspaceForMatterAsync(normalizedMatterId, ct);
                await _auditLogger.LogAsync(HttpContext, "client_transparency.policy_upsert", "Matter", normalizedMatterId, $"Policy={profile.PublishPolicy}");
                return Ok(workspace.Policy ?? new ClientTransparencyPublishPolicySummary
                {
                    PublishPolicy = profile.PublishPolicy
                });
            }
            catch (InvalidOperationException ex)
            {
                return HandleInvalidOperation(ex, "policy_upsert");
            }
        }

        [Authorize(Roles = "Admin,Partner,SecurityAdmin")]
        [EnableRateLimiting("AdminDangerousOps")]
        [HttpPost("snapshots/{snapshotId}/review")]
        public async Task<ActionResult> ReviewSnapshot(string snapshotId, [FromBody] ClientTransparencySnapshotReviewRequest request, CancellationToken ct)
        {
            if (request == null) return BadRequest(new { message = "Request body is required." });

            try
            {
                var userId = RequireUserId();
                var snapshot = await RequireSnapshotAccessAsync(snapshotId, ct);
                NormalizeReviewRequest(request);

                var workspace = await _transparencyService.ReviewSnapshotAsync(snapshot.Id, request, userId, ct);
                await _auditLogger.LogAsync(HttpContext, "client_transparency.review_action", "ClientTransparencySnapshot", snapshot.Id,
                    $"Action={request.Action ?? "rewrite"}, PublishAfter={request.PublishAfter == true}");
                return Ok(workspace);
            }
            catch (InvalidOperationException ex)
            {
                return HandleInvalidOperation(ex, "review_snapshot");
            }
        }

        [Authorize(Roles = "Admin,Partner,SecurityAdmin")]
        [EnableRateLimiting("AdminDangerousOps")]
        [HttpPost("snapshots/{snapshotId}/publish")]
        public async Task<ActionResult<ClientTransparencyPublishResult>> PublishSnapshot(string snapshotId, [FromBody] ClientTransparencyPublishRequest? request, CancellationToken ct)
        {
            try
            {
                var userId = RequireUserId();
                var snapshot = await RequireSnapshotAccessAsync(snapshotId, ct);
                NormalizePublishRequest(request);

                var result = await _transparencyService.PublishSnapshotAsync(snapshot.Id, request, userId, ct);
                if (result.Published)
                {
                    await _auditLogger.LogAsync(HttpContext, "client_transparency.publish", "ClientTransparencySnapshot", snapshot.Id,
                        $"MatterId={result.MatterId}, Decision={result.PublishDecision}");
                    return Ok(result);
                }

                await _auditLogger.LogAsync(HttpContext, "client_transparency.publish_blocked", "ClientTransparencySnapshot", snapshot.Id,
                    $"MatterId={result.MatterId}, Decision={result.PublishDecision}, Reasons={string.Join(",", result.Reasons)}");
                return Conflict(result);
            }
            catch (InvalidOperationException ex)
            {
                return HandleInvalidOperation(ex, "publish_snapshot");
            }
        }

        [Authorize(Roles = "Admin,Partner,SecurityAdmin")]
        [HttpGet("snapshots/{snapshotId}/evidence")]
        public async Task<ActionResult> GetSnapshotEvidence(string snapshotId, CancellationToken ct)
        {
            try
            {
                var snapshot = await RequireSnapshotAccessAsync(snapshotId, ct);
                var evidence = await _transparencyService.GetSnapshotEvidenceBundleAsync(snapshot.Id, ct);
                if (evidence == null) return NotFound();
                await _auditLogger.LogAsync(HttpContext, "client_transparency.get_evidence", "ClientTransparencySnapshot", snapshot.Id, "Evidence bundle loaded");
                return Ok(evidence);
            }
            catch (InvalidOperationException ex)
            {
                return HandleInvalidOperation(ex, "get_evidence");
            }
        }

        [Authorize(Roles = "Admin,Partner,SecurityAdmin")]
        [EnableRateLimiting("AdminDangerousOps")]
        [HttpPost("batch-reverify")]
        public async Task<ActionResult> BatchReverifyEvidence([FromBody] ClientTransparencyEvidenceBatchReverifyRequest? request, CancellationToken ct)
        {
            try
            {
                var userId = RequireUserId();
                request ??= new ClientTransparencyEvidenceBatchReverifyRequest();
                NormalizeBatchRequest(request);

                if (string.IsNullOrWhiteSpace(request.MatterId) && !IsElevatedTransparencyOperator())
                {
                    return BadRequest(new { message = "MatterId is required for this request." });
                }

                if (!string.IsNullOrWhiteSpace(request.MatterId))
                {
                    request.MatterId = await RequireMatterAccessAsync(request.MatterId, ct);
                }

                var result = await _transparencyService.BatchReverifyEvidenceAsync(request, userId, ct);
                await _auditLogger.LogAsync(HttpContext, "client_transparency.batch_reverify", "ClientTransparencySnapshot", request.MatterId ?? "all",
                    $"Days={request.Days}, Limit={request.Limit}, PublishedOnly={request.OnlyPublished == true}, CurrentOnly={request.OnlyCurrent == true}");
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return HandleInvalidOperation(ex, "batch_reverify");
            }
        }

        [Authorize(Roles = "Admin,Partner,SecurityAdmin")]
        [EnableRateLimiting("AdminDangerousOps")]
        [HttpGet("metrics")]
        public async Task<ActionResult> GetEvidenceMetrics([FromQuery] int days = 90, [FromQuery] string? matterId = null, CancellationToken ct = default)
        {
            try
            {
                var normalizedDays = Math.Clamp(days <= 0 ? 90 : days, 1, 365);
                string? normalizedMatterId = null;
                if (!string.IsNullOrWhiteSpace(matterId))
                {
                    normalizedMatterId = await RequireMatterAccessAsync(matterId, ct);
                }
                else if (!IsElevatedTransparencyOperator())
                {
                    return BadRequest(new { message = "MatterId is required for this request." });
                }

                var result = await _transparencyService.GetEvidenceMetricsAsync(normalizedDays, normalizedMatterId, ct);
                await _auditLogger.LogAsync(HttpContext, "client_transparency.metrics", "ClientTransparencySnapshot", normalizedMatterId ?? "all", $"Days={normalizedDays}");
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return HandleInvalidOperation(ex, "metrics");
            }
        }

        private string RequireUserId()
        {
            return User.FindFirst("sub")?.Value
                ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? throw new InvalidOperationException("Authenticated user identifier is required.");
        }

        private IQueryable<T> TenantScope<T>(IQueryable<T> query) where T : class
        {
            var tenantId = RequireTenantId();
            return query.Where(e => EF.Property<string>(e, "TenantId") == tenantId);
        }

        private string RequireTenantId()
        {
            if (string.IsNullOrWhiteSpace(_tenantContext.TenantId))
            {
                throw new InvalidOperationException("Tenant context is required.");
            }

            return _tenantContext.TenantId;
        }

        private async Task<string> RequireMatterAccessAsync(string? matterId, CancellationToken ct)
        {
            var normalizedMatterId = NormalizeId(matterId) ?? throw new InvalidOperationException("MatterId is required.");
            var exists = await TenantScope(_context.Matters)
                .AsNoTracking()
                .AnyAsync(m => m.Id == normalizedMatterId, ct);
            if (!exists)
            {
                throw new InvalidOperationException("Matter not found.");
            }

            return normalizedMatterId;
        }

        private async Task<ClientTransparencySnapshot> RequireSnapshotAccessAsync(string? snapshotId, CancellationToken ct)
        {
            var normalizedSnapshotId = NormalizeId(snapshotId) ?? throw new InvalidOperationException("SnapshotId is required.");
            var snapshot = await TenantScope(_context.ClientTransparencySnapshots)
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == normalizedSnapshotId, ct);
            if (snapshot == null)
            {
                throw new InvalidOperationException("Snapshot not found.");
            }

            var matterExists = await TenantScope(_context.Matters)
                .AsNoTracking()
                .AnyAsync(m => m.Id == snapshot.MatterId, ct);
            if (!matterExists)
            {
                throw new InvalidOperationException("Matter not found.");
            }

            return snapshot;
        }

        private BadRequestObjectResult HandleInvalidOperation(InvalidOperationException ex, string operation)
        {
            _logger.LogWarning(ex, "Client transparency controller operation {Operation} failed.", operation);
            return BadRequest(new { message = "Request could not be completed." });
        }

        private static string? NormalizeId(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static string? TrimToMax(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var trimmed = value.Trim();
            return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
        }

        private static void NormalizeRegenerateRequest(ClientTransparencyRegenerateRequest request)
        {
            request.TriggerType = NormalizeTriggerType(request.TriggerType, "manual_regenerate");
            request.TriggerEntityType = NormalizeTriggerEntityType(request.TriggerEntityType);
            request.TriggerEntityId = TrimToMax(request.TriggerEntityId, 128);
            request.Reason = TrimToMax(request.Reason, 1000);
            request.VisibilityMode = TrimToMax(request.VisibilityMode, 32);
            request.ClientAudience = NormalizeClientAudience(request.ClientAudience);
            request.CorrelationId = TrimToMax(request.CorrelationId, 128);
            request.TriggeredBy = TrimToMax(request.TriggeredBy, 128);
        }

        private static void NormalizeTriggerRequest(ClientTransparencyTriggerRequest request)
        {
            request.TriggerType = NormalizeTriggerType(request.TriggerType, "manual_trigger");
            request.TriggerEntityType = NormalizeTriggerEntityType(request.TriggerEntityType);
            request.TriggerEntityId = TrimToMax(request.TriggerEntityId, 128);
            request.Reason = TrimToMax(request.Reason, 1000);
            request.CorrelationId = TrimToMax(request.CorrelationId, 128);
            request.ClientAudience = NormalizeClientAudience(request.ClientAudience);
            request.ClientNotificationMode = NormalizeClientNotificationMode(request.ClientNotificationMode);
        }

        private static void NormalizePolicyRequest(ClientTransparencyPolicyUpsertRequest request)
        {
            if (!string.IsNullOrWhiteSpace(request.PublishPolicy))
            {
                var normalizedPolicy = AllowedPublishPolicies.FirstOrDefault(p =>
                    string.Equals(p, request.PublishPolicy.Trim(), StringComparison.OrdinalIgnoreCase));
                if (normalizedPolicy == null)
                {
                    throw new InvalidOperationException("Invalid publish policy.");
                }

                request.PublishPolicy = normalizedPolicy;
            }

            if (request.CostImpactChangeThreshold.HasValue)
            {
                request.CostImpactChangeThreshold = Math.Clamp(request.CostImpactChangeThreshold.Value, 0m, 10000000m);
            }

            if (request.LowConfidenceThreshold.HasValue)
            {
                request.LowConfidenceThreshold = Math.Clamp(request.LowConfidenceThreshold.Value, 0m, 1m);
            }
        }

        private static void NormalizeReviewRequest(ClientTransparencySnapshotReviewRequest request)
        {
            var action = TrimToMax(request.Action, 32) ?? "rewrite";
            if (!AllowedReviewActions.Contains(action))
            {
                throw new InvalidOperationException("Invalid review action.");
            }

            request.Action = action.ToLowerInvariant();
            request.Reason = TrimToMax(request.Reason, 2000);
            request.AssignedTo = TrimToMax(request.AssignedTo, 128);
            request.SnapshotSummary = TrimToMax(request.SnapshotSummary, 4000);
            request.WhatChangedSummary = TrimToMax(request.WhatChangedSummary, 4000);
            request.NextStepActionText = TrimToMax(request.NextStepActionText, 2000);
            request.NextStepBlockedByText = TrimToMax(request.NextStepBlockedByText, 1000);

            if (request.DelayReasonTextUpdates?.Count > 50)
            {
                throw new InvalidOperationException("Too many delay reason updates.");
            }

            if (request.TimelineTextUpdates?.Count > 50)
            {
                throw new InvalidOperationException("Too many timeline updates.");
            }

            if (request.DelayReasonTextUpdates != null)
            {
                foreach (var item in request.DelayReasonTextUpdates)
                {
                    item.Id = TrimToMax(item.Id, 128);
                    item.ClientSafeText = TrimToMax(item.ClientSafeText, 2000);
                }
            }

            if (request.TimelineTextUpdates != null)
            {
                foreach (var item in request.TimelineTextUpdates)
                {
                    item.Id = TrimToMax(item.Id, 128);
                    item.ClientSafeText = TrimToMax(item.ClientSafeText, 2000);
                }
            }
        }

        private static void NormalizePublishRequest(ClientTransparencyPublishRequest? request)
        {
            if (request == null)
            {
                return;
            }

            request.Reason = TrimToMax(request.Reason, 2000);
            request.ApproverReason = TrimToMax(request.ApproverReason, 2000);
        }

        private static void NormalizeBatchRequest(ClientTransparencyEvidenceBatchReverifyRequest request)
        {
            request.MatterId = NormalizeId(request.MatterId);
            request.Days = Math.Clamp(request.Days <= 0 ? 90 : request.Days, 1, 365);
            request.Limit = Math.Clamp(request.Limit <= 0 ? 50 : request.Limit, 1, 200);
            request.SourceFilter = TrimToMax(request.SourceFilter, 32);
        }

        private static string? NormalizeTriggerEntityType(string? entityType)
        {
            var candidate = TrimToMax(entityType, 64);
            if (candidate == null)
            {
                return null;
            }

            var normalized = AllowedTriggerEntityTypes.FirstOrDefault(t => string.Equals(t, candidate, StringComparison.OrdinalIgnoreCase));
            if (normalized == null)
            {
                throw new InvalidOperationException("Invalid trigger entity type.");
            }

            return normalized;
        }

        private static string NormalizeTriggerType(string? triggerType, string fallback)
        {
            var candidate = TrimToMax(triggerType, 64) ?? fallback;
            var normalized = AllowedTriggerTypes.FirstOrDefault(t => string.Equals(t, candidate, StringComparison.OrdinalIgnoreCase));
            if (normalized == null)
            {
                throw new InvalidOperationException("Invalid trigger type.");
            }

            return normalized;
        }

        private static string NormalizeClientAudience(string? audience)
        {
            var normalized = TrimToMax(audience, 32)?.ToLowerInvariant();
            return normalized is "portal" or "internal" ? normalized : "portal";
        }

        private static string? NormalizeClientNotificationMode(string? mode)
        {
            var normalized = TrimToMax(mode, 32)?.ToLowerInvariant();
            return normalized is null or "auto" or "suppress" ? normalized : throw new InvalidOperationException("Invalid client notification mode.");
        }

        private bool IsElevatedTransparencyOperator()
        {
            return User.IsInRole("Admin") || User.IsInRole("SecurityAdmin");
        }
    }
}
