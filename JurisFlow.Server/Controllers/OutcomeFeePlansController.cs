using JurisFlow.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Task = System.Threading.Tasks.Task;

namespace JurisFlow.Server.Controllers
{
    [Route("api/outcome-fee-plans")]
    [ApiController]
    [Authorize(Policy = "StaffOnly")]
    public class OutcomeFeePlansController : ControllerBase
    {
        private readonly OutcomeFeePlannerService _plannerService;
        private readonly ClientTransparencyService _clientTransparencyService;
        private readonly AuditLogger _auditLogger;
        private readonly ILogger<OutcomeFeePlansController> _logger;

        public OutcomeFeePlansController(
            OutcomeFeePlannerService plannerService,
            ClientTransparencyService clientTransparencyService,
            AuditLogger auditLogger,
            ILogger<OutcomeFeePlansController> logger)
        {
            _plannerService = plannerService;
            _clientTransparencyService = clientTransparencyService;
            _auditLogger = auditLogger;
            _logger = logger;
        }

        [HttpPost("generate")]
        public async Task<ActionResult<OutcomeFeePlanDetailResult>> Generate([FromBody] OutcomeFeePlanCreateRequest request, CancellationToken ct)
        {
            if (request == null)
            {
                return BadRequest(new { message = "Request body is required." });
            }

            try
            {
                var result = await _plannerService.CreatePlanAsync(request, GetUserId(), ct);
                if (result.Plan != null)
                {
                    await _auditLogger.LogAsync(HttpContext, "outcome_fee_plan.create", "OutcomeFeePlan", result.Plan.Id, $"MatterId={result.Plan.MatterId}, Version={result.CurrentVersion?.VersionNumber}");
                    await TryTriggerClientTransparencyAsync(result.Plan.MatterId, "outcome_fee_plan_generate", result.CurrentVersion?.Id ?? result.Plan.Id, result.CurrentVersion != null ? nameof(JurisFlow.Server.Models.OutcomeFeePlanVersion) : nameof(JurisFlow.Server.Models.OutcomeFeePlan), ct);
                }
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("matter/{matterId}")]
        public async Task<ActionResult<OutcomeFeePlanDetailResult>> GetLatestForMatter(string matterId, CancellationToken ct)
        {
            var result = await _plannerService.GetLatestPlanForMatterAsync(matterId, ct);
            return result == null ? NotFound() : Ok(result);
        }

        [HttpGet("{planId}")]
        public async Task<ActionResult<OutcomeFeePlanDetailResult>> GetPlan(string planId, CancellationToken ct)
        {
            var result = await _plannerService.GetPlanDetailAsync(planId, ct);
            return result == null ? NotFound() : Ok(result);
        }

        [HttpGet("{planId}/versions")]
        public async Task<ActionResult> ListVersions(string planId, CancellationToken ct)
        {
            return Ok(await _plannerService.ListPlanVersionsAsync(planId, ct));
        }

        [HttpGet("{planId}/compare")]
        public async Task<ActionResult<OutcomeFeePlanVersionCompareResult>> CompareVersions(
            string planId,
            [FromQuery] string? fromVersionId = null,
            [FromQuery] string? toVersionId = null,
            CancellationToken ct = default)
        {
            var result = await _plannerService.CompareVersionsAsync(planId, fromVersionId, toVersionId, ct);
            if (result == null)
            {
                return NotFound();
            }

            await _auditLogger.LogAsync(HttpContext, "outcome_fee_plan.compare", "OutcomeFeePlan", planId, $"From={result.FromVersionNumber}, To={result.ToVersionNumber}");
            return Ok(result);
        }

        [HttpPost("{planId}/recompute")]
        public async Task<ActionResult<OutcomeFeePlanDetailResult>> Recompute(string planId, [FromBody] OutcomeFeePlanRecomputeRequest? request, CancellationToken ct)
        {
            try
            {
                var result = await _plannerService.RecomputePlanAsync(planId, request, GetUserId(), ct);
                if (result == null)
                {
                    return NotFound();
                }

                if (result.Plan != null)
                {
                    await _auditLogger.LogAsync(HttpContext, "outcome_fee_plan.recompute", "OutcomeFeePlan", result.Plan.Id, $"MatterId={result.Plan.MatterId}, Version={result.CurrentVersion?.VersionNumber}, Trigger={request?.TriggerType ?? "manual_recompute"}");
                    await TryTriggerClientTransparencyAsync(result.Plan.MatterId, "outcome_fee_plan_recompute", result.CurrentVersion?.Id ?? result.Plan.Id, result.CurrentVersion != null ? nameof(JurisFlow.Server.Models.OutcomeFeePlanVersion) : nameof(JurisFlow.Server.Models.OutcomeFeePlan), ct);
                }

                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("triggers")]
        public async Task<ActionResult<OutcomeFeePlanTriggerResult>> TriggerRecompute([FromBody] OutcomeFeePlanTriggerRequest request, CancellationToken ct)
        {
            if (request == null)
            {
                return BadRequest(new { message = "Request body is required." });
            }

            try
            {
                var result = await _plannerService.TryProcessTriggerAsync(request, GetUserId(), ct);
                if (result.PlanId != null)
                {
                    await _auditLogger.LogAsync(HttpContext, "outcome_fee_plan.trigger", "OutcomeFeePlan", result.PlanId,
                        $"MatterId={result.MatterId}, Trigger={result.TriggerType}, Recomputed={result.Recomputed}, Drift={result.DriftDetected}");
                }

                if (!string.IsNullOrWhiteSpace(result.MatterId) && (result.Recomputed || result.DriftDetected))
                {
                    var transparencyTrigger = result.DriftDetected ? "outcome_fee_plan_drift" : "outcome_fee_plan_trigger_recompute";
                    var entityType = !string.IsNullOrWhiteSpace(result.CurrentVersionId)
                        ? nameof(JurisFlow.Server.Models.OutcomeFeePlanVersion)
                        : nameof(JurisFlow.Server.Models.OutcomeFeePlan);
                    var entityId = result.CurrentVersionId ?? result.PlanId;
                    if (!string.IsNullOrWhiteSpace(entityId))
                    {
                        await TryTriggerClientTransparencyAsync(result.MatterId, transparencyTrigger, entityId, entityType, ct);
                    }
                }

                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("metrics")]
        public async Task<ActionResult> GetMetrics([FromQuery] int days = 90, CancellationToken ct = default)
        {
            var result = await _plannerService.GetPortfolioMetricsAsync(days, ct);
            await _auditLogger.LogAsync(HttpContext, "outcome_fee_plan.metrics", "OutcomeFeePlan", "portfolio", $"Days={Math.Clamp(days <= 0 ? 90 : days, 7, 365)}");
            return Ok(result);
        }

        [HttpGet("calibration/snapshots")]
        public async Task<ActionResult> ListCalibrationSnapshots(
            [FromQuery] string? status = null,
            [FromQuery] string? cohortKey = null,
            [FromQuery] int limit = 50,
            CancellationToken ct = default)
        {
            var result = await _plannerService.ListCalibrationSnapshotsAsync(status, cohortKey, limit, ct);
            await _auditLogger.LogAsync(HttpContext, "outcome_fee_plan.calibration.list", "OutcomeFeeCalibrationSnapshot", "list",
                $"Status={status ?? "*"}, Cohort={cohortKey ?? "*"}, Limit={Math.Clamp(limit <= 0 ? 50 : limit, 1, 200)}");
            return Ok(result);
        }

        [HttpGet("calibration/effective/matter/{matterId}")]
        public async Task<ActionResult> GetEffectiveCalibrationForMatter(string matterId, CancellationToken ct = default)
        {
            var result = await _plannerService.GetEffectiveCalibrationForMatterAsync(matterId, ct);
            if (result == null) return NotFound();

            await _auditLogger.LogAsync(HttpContext, "outcome_fee_plan.calibration.effective", "Matter", matterId, "Effective calibration lookup.");
            return Ok(result);
        }

        [HttpPost("calibration/jobs/run")]
        public async Task<ActionResult> RunCalibrationJob([FromBody] OutcomeFeeCalibrationJobRunRequest? request, CancellationToken ct = default)
        {
            try
            {
                var result = await _plannerService.RunCalibrationJobAsync(request, GetUserId(), ct);
                await _auditLogger.LogAsync(HttpContext, "outcome_fee_plan.calibration.run", "OutcomeFeeCalibrationSnapshot", "job",
                    $"Days={request?.Days ?? 365}, MinSample={request?.MinSampleSize ?? 5}, Shadow={request?.ShadowMode ?? true}");
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("{planId}/outcome-feedback")]
        public async Task<ActionResult> RecordOutcomeFeedback(string planId, [FromBody] OutcomeFeeOutcomeFeedbackRequest request, CancellationToken ct = default)
        {
            if (request == null)
            {
                return BadRequest(new { message = "Request body is required." });
            }

            try
            {
                var result = await _plannerService.RecordOutcomeFeedbackAsync(planId, request, GetUserId(), ct);
                if (result == null) return NotFound();

                await _auditLogger.LogAsync(HttpContext, "outcome_fee_plan.outcome_feedback", "OutcomeFeePlan", planId,
                    $"Outcome={request.ActualOutcome ?? "unknown"}, ActualFees={request.ActualFeesCollected?.ToString() ?? "null"}");
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("calibration/snapshots/{snapshotId}/activate")]
        public async Task<ActionResult> ActivateCalibrationSnapshot(string snapshotId, [FromBody] OutcomeFeeCalibrationSnapshotActionRequest request, CancellationToken ct = default)
        {
            if (request == null)
            {
                return BadRequest(new { message = "Request body is required." });
            }

            try
            {
                var result = await _plannerService.ActivateCalibrationSnapshotAsync(snapshotId, request, GetUserId(), ct);
                if (result == null) return NotFound();

                await _auditLogger.LogAsync(HttpContext, "outcome_fee_plan.calibration.activate", "OutcomeFeeCalibrationSnapshot", snapshotId,
                    $"AsShadow={request.AsShadow}, Reason={request.Reason}");
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("calibration/snapshots/{snapshotId}/rollback")]
        public async Task<ActionResult> RollbackCalibrationSnapshot(string snapshotId, [FromBody] OutcomeFeeCalibrationRollbackRequest request, CancellationToken ct = default)
        {
            if (request == null)
            {
                return BadRequest(new { message = "Request body is required." });
            }

            try
            {
                var result = await _plannerService.RollbackCalibrationSnapshotAsync(snapshotId, request, GetUserId(), ct);
                if (result == null) return NotFound();

                await _auditLogger.LogAsync(HttpContext, "outcome_fee_plan.calibration.rollback", "OutcomeFeeCalibrationSnapshot", snapshotId,
                    $"Target={request.TargetSnapshotId ?? "auto"}, Reason={request.Reason}");
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        private string GetUserId()
        {
            return User.FindFirst("sub")?.Value
                ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? "system";
        }

        private async Task TryTriggerClientTransparencyAsync(string? matterId, string triggerType, string entityId, string entityType, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(matterId) || string.IsNullOrWhiteSpace(entityId))
            {
                return;
            }

            try
            {
                await _clientTransparencyService.TryProcessTriggerAsync(new ClientTransparencyTriggerRequest
                {
                    MatterId = matterId,
                    TriggerType = triggerType,
                    TriggerEntityType = entityType,
                    TriggerEntityId = entityId
                }, GetUserId(), ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Client transparency trigger failed for outcome-fee plan matter {MatterId}", matterId);
            }
        }
    }
}
