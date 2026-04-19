using System.Data;
using System.Data.Common;
using JurisFlow.Server.Contracts;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using JurisFlow.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Task = System.Threading.Tasks.Task;

namespace JurisFlow.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Policy = "StaffOnly")]
    [RequestSizeLimit(MaxMatterRequestBodyBytes)]
    public class MattersController : ControllerBase
    {
        private const int MaxMatterRequestBodyBytes = 256 * 1024;
        private const int DefaultMatterPageSize = 50;
        private const int MaxMatterPageSize = 200;
        private const string DeletedMatterStatus = "Deleted";
        private static bool EnableSecondaryClientWrites => false;
        private readonly JurisFlowDbContext _context;
        private readonly AuditLogger _auditLogger;
        private readonly FirmStructureService _firmStructure;
        private readonly MatterWorkflowTriggerDispatcher _workflowTriggerDispatcher;
        private readonly MatterAccessService _matterAccess;
        private readonly MatterClientLinkService _matterClientLinks;
        private readonly ILogger<MattersController> _logger;
        private readonly Dictionary<string, bool> _schemaPresenceCache = new(StringComparer.OrdinalIgnoreCase);

        public MattersController(
            JurisFlowDbContext context,
            AuditLogger auditLogger,
            FirmStructureService firmStructure,
            MatterWorkflowTriggerDispatcher workflowTriggerDispatcher,
            MatterAccessService matterAccess,
            MatterClientLinkService matterClientLinks,
            ILogger<MattersController> logger)
        {
            _context = context;
            _auditLogger = auditLogger;
            _firmStructure = firmStructure;
            _workflowTriggerDispatcher = workflowTriggerDispatcher;
            _matterAccess = matterAccess;
            _matterClientLinks = matterClientLinks;
            _logger = logger;
        }

        // GET: api/Matters
        [HttpGet]
        public async Task<ActionResult<IEnumerable<MatterResponse>>> GetMatters(
            [FromQuery] string? status,
            [FromQuery] string? entityId,
            [FromQuery] string? officeId,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = DefaultMatterPageSize)
        {
            var query = _matterAccess.ApplyReadableScope(_context.Matters.AsNoTracking(), User);
            var normalizedStatus = status?.Trim();

            if (!string.IsNullOrEmpty(normalizedStatus))
            {
                if (string.Equals(normalizedStatus, "archive", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(normalizedStatus, "archived", StringComparison.OrdinalIgnoreCase))
                {
                    query = query.Where(m => m.Status == "Archived");
                }
                else
                {
                    query = query.Where(m => m.Status == normalizedStatus ||
                                             (string.Equals(normalizedStatus, "Open", StringComparison.OrdinalIgnoreCase) &&
                                              m.Status != "Archived" &&
                                              m.Status != "Closed"));
                }
            }

            if (!string.IsNullOrWhiteSpace(entityId))
            {
                query = query.Where(m => m.EntityId == entityId);
            }

            if (!string.IsNullOrWhiteSpace(officeId))
            {
                query = query.Where(m => m.OfficeId == officeId);
            }

            var normalizedPage = NormalizePage(page);
            var normalizedPageSize = NormalizePageSize(pageSize);
            var totalCount = await query.CountAsync(HttpContext.RequestAborted);
            var matters = await query
                .OrderByDescending(m => m.OpenDate)
                .Skip((normalizedPage - 1) * normalizedPageSize)
                .Take(normalizedPageSize)
                .ToListAsync(HttpContext.RequestAborted);

            await TryPopulateRelatedClientsAsync(matters, HttpContext.RequestAborted);
            AddPaginationHeaders(totalCount, normalizedPage, normalizedPageSize);
            return matters.Select(MatterResponse.FromModel).ToList();
        }

        // GET: api/Matters/5
        [HttpGet("{id}")]
        public async Task<ActionResult<MatterResponse>> GetMatter(string id)
        {
            var matter = await _matterAccess.ApplyReadableScope(_context.Matters.AsNoTracking(), User)
                .FirstOrDefaultAsync(m => m.Id == id, HttpContext.RequestAborted);

            if (matter == null)
            {
                return NotFoundProblem("Matter not found.", $"Matter '{id}' was not found or is not accessible.");
            }

            await TryPopulateRelatedClientsAsync(matter, HttpContext.RequestAborted);
            return MatterResponse.FromModel(matter);
        }

        // POST: api/Matters
        [HttpPost]
        public async Task<ActionResult<MatterResponse>> PostMatter([FromBody] CreateMatterRequest request)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            try
            {
                var currentUserId = GetUserId();
                if (string.IsNullOrWhiteSpace(currentUserId))
                {
                    return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Authentication required.", detail: "An authenticated user id is required to create a matter.");
                }
                if (string.IsNullOrWhiteSpace(request.ClientId))
                {
                    return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Client id is required.", detail: "ClientId is required.");
                }

                var resolvedClientId = await ResolveValidClientIdAsync(request.ClientId);
                if (resolvedClientId == null)
                {
                    return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Client not found.", detail: "Selected client was not found in the active tenant scope.");
                }

                var relatedClientResolution = await ResolveRelatedClientsForWriteAsync(resolvedClientId, request.RelatedClientIds);
                var matter = MapCreateRequest(request, currentUserId, resolvedClientId);

                NormalizeSharingSettings(matter);

                var resolved = await _firmStructure.ResolveEntityOfficeAsync(request.EntityId, request.OfficeId);
                matter.EntityId = resolved.entityId;
                matter.OfficeId = resolved.officeId;

                _context.Matters.Add(matter);
                await _context.SaveChangesAsync(HttpContext.RequestAborted);
                if (relatedClientResolution.ClientIds.Count > 0)
                {
                    await TrySyncRelatedClientsAsync(matter.Id, relatedClientResolution.ClientIds, HttpContext.RequestAborted);
                }

                await TryPopulateRelatedClientsAsync(matter, HttpContext.RequestAborted);

                await TryAuditAsync("matter.create", "Matter", matter.Id, $"ClientId={matter.ClientId}, Name={matter.Name}");

                return CreatedAtAction(nameof(GetMatter), new { id = matter.Id }, MatterResponse.FromModel(matter));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Matter create failed. RootCause={RootCause}", ex.GetBaseException().Message);
                return Problem(statusCode: StatusCodes.Status500InternalServerError, title: "Matter create failed.", detail: "The server failed to create the matter.");
            }
        }

        // PUT: api/Matters/5
        [HttpPut("{id}")]
        public async Task<IActionResult> PutMatter(string id, [FromBody] UpdateMatterRequest request)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            try
            {
                if (!await _matterAccess.CanManageMatterAsync(id, User, cancellationToken: HttpContext.RequestAborted))
                {
                    return Problem(statusCode: StatusCodes.Status403Forbidden, title: "Matter update is forbidden.", detail: $"You are not allowed to update matter '{id}'.");
                }
                if (string.IsNullOrWhiteSpace(request.ClientId))
                {
                    return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Client id is required.", detail: "ClientId is required.");
                }

                var existingMatter = await _context.Matters.FirstOrDefaultAsync(m => m.Id == id, HttpContext.RequestAborted);
                if (existingMatter == null)
                {
                    return NotFoundProblem("Matter not found.", $"Matter '{id}' was not found.");
                }

                var resolvedClientId = await ResolveValidClientIdAsync(request.ClientId);
                if (resolvedClientId == null)
                {
                    return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Client not found.", detail: "Selected client was not found in the active tenant scope.");
                }

                var relatedClientResolution = await ResolveRelatedClientsForWriteAsync(resolvedClientId, request.RelatedClientIds);

                var resolved = await _firmStructure.ResolveEntityOfficeAsync(request.EntityId, request.OfficeId);

                existingMatter.CaseNumber = request.CaseNumber;
                existingMatter.Name = request.Name;
                existingMatter.PracticeArea = request.PracticeArea ?? string.Empty;
                existingMatter.CourtType = NormalizeOptionalText(request.CourtType);
                existingMatter.Outcome = NormalizeOptionalText(request.Outcome);
                existingMatter.Status = request.Status;
                existingMatter.FeeStructure = request.FeeStructure;
                existingMatter.ResponsibleAttorney = request.ResponsibleAttorney;
                existingMatter.BillableRate = request.BillableRate;
                existingMatter.EntityId = resolved.entityId;
                existingMatter.OfficeId = resolved.officeId;
                existingMatter.ClientId = resolvedClientId;
                NormalizeSharingSettings(existingMatter);

                await _context.SaveChangesAsync(HttpContext.RequestAborted);
                if (relatedClientResolution.ClientIds.Count > 0)
                {
                    await TrySyncRelatedClientsAsync(existingMatter.Id, relatedClientResolution.ClientIds, HttpContext.RequestAborted);
                }

                await TryAuditAsync("matter.update", "Matter", existingMatter.Id, $"Status={existingMatter.Status}");
                await TryTriggerOutcomeFeePlannerAsync(existingMatter.Id, "matter_update");

                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Matter update failed for matter {MatterId}. RootCause={RootCause}", id, ex.GetBaseException().Message);
                return Problem(statusCode: StatusCodes.Status500InternalServerError, title: "Matter update failed.", detail: "The server failed to update the matter.");
            }
        }

        // DELETE: api/Matters/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteMatter(string id)
        {
            if (!await _matterAccess.CanManageMatterAsync(id, User, ignoreQueryFilters: true, cancellationToken: HttpContext.RequestAborted))
            {
                return Problem(statusCode: StatusCodes.Status403Forbidden, title: "Matter delete is forbidden.", detail: $"You are not allowed to delete matter '{id}'.");
            }

            var matter = await _context.Matters.IgnoreQueryFilters().FirstOrDefaultAsync(m => m.Id == id);
            if (matter == null) return NotFoundProblem("Matter not found.", $"Matter '{id}' was not found.");
            if (string.Equals(matter.Status, DeletedMatterStatus, StringComparison.OrdinalIgnoreCase)) return NoContent();

            var ct = HttpContext.RequestAborted;
            await using var tx = await _context.Database.BeginTransactionAsync(ct);
            try
            {
                matter.CurrentOutcomeFeePlanId = null;

                // Optional direct references.
                await SafeClearMatterReferenceAsync("Tasks", token => _context.Tasks.IgnoreQueryFilters().Where(t => t.MatterId == id).ExecuteUpdateAsync(setters => setters.SetProperty(t => t.MatterId, (string?)null), token), ct);
                await SafeClearMatterReferenceAsync("Documents", token => _context.Documents.IgnoreQueryFilters().Where(d => d.MatterId == id).ExecuteUpdateAsync(setters => setters.SetProperty(d => d.MatterId, (string?)null), token), ct);
                await SafeClearMatterReferenceAsync("CalendarEvents", token => _context.CalendarEvents.IgnoreQueryFilters().Where(e => e.MatterId == id).ExecuteUpdateAsync(setters => setters.SetProperty(e => e.MatterId, (string?)null), token), ct);
                await SafeClearMatterReferenceAsync("ClientTrustLedgers", token => _context.ClientTrustLedgers.IgnoreQueryFilters().Where(l => l.MatterId == id).ExecuteUpdateAsync(setters => setters.SetProperty(l => l.MatterId, (string?)null), token), ct);
                await SafeClearMatterReferenceAsync("TrustTransactions", token => _context.TrustTransactions.IgnoreQueryFilters().Where(t => t.MatterId == id).ExecuteUpdateAsync(setters => setters.SetProperty(t => t.MatterId, (string?)null), token), ct);
                await SafeClearMatterReferenceAsync("Expenses", token => _context.Expenses.IgnoreQueryFilters().Where(e => e.MatterId == id).ExecuteUpdateAsync(setters => setters.SetProperty(e => e.MatterId, (string?)null), token), ct);
                await SafeClearMatterReferenceAsync("TimeEntries", token => _context.TimeEntries.IgnoreQueryFilters().Where(t => t.MatterId == id).ExecuteUpdateAsync(setters => setters.SetProperty(t => t.MatterId, (string?)null), token), ct);
                await SafeClearMatterReferenceAsync("CourtDocketEntries", token => _context.CourtDocketEntries.IgnoreQueryFilters().Where(d => d.MatterId == id).ExecuteUpdateAsync(setters => setters.SetProperty(d => d.MatterId, (string?)null), token), ct);
                await SafeClearMatterReferenceAsync("EfilingSubmissions", token => _context.EfilingSubmissions.IgnoreQueryFilters().Where(s => s.MatterId == id).ExecuteUpdateAsync(setters => setters.SetProperty(s => s.MatterId, (string?)null), token), ct);
                await SafeClearMatterReferenceAsync("AppointmentRequests", token => _context.AppointmentRequests.IgnoreQueryFilters().Where(a => a.MatterId == id).ExecuteUpdateAsync(setters => setters.SetProperty(a => a.MatterId, (string?)null), token), ct);
                await SafeClearMatterReferenceAsync("ClientMessages", token => _context.ClientMessages.IgnoreQueryFilters().Where(m => m.MatterId == id).ExecuteUpdateAsync(setters => setters.SetProperty(m => m.MatterId, (string?)null), token), ct);
                await SafeClearMatterReferenceAsync("EmailMessages", token => _context.EmailMessages.IgnoreQueryFilters().Where(m => m.MatterId == id).ExecuteUpdateAsync(setters => setters.SetProperty(m => m.MatterId, (string?)null), token), ct);
                await SafeClearMatterReferenceAsync("PaymentTransactions", token => _context.PaymentTransactions.IgnoreQueryFilters().Where(t => t.MatterId == id).ExecuteUpdateAsync(setters => setters.SetProperty(t => t.MatterId, (string?)null), token), ct);
                await SafeClearMatterReferenceAsync("SignatureRequests", token => _context.SignatureRequests.IgnoreQueryFilters().Where(r => r.MatterId == id).ExecuteUpdateAsync(setters => setters.SetProperty(r => r.MatterId, (string?)null), token), ct);
                await SafeClearMatterReferenceAsync("SmsMessages", token => _context.SmsMessages.IgnoreQueryFilters().Where(m => m.MatterId == id).ExecuteUpdateAsync(setters => setters.SetProperty(m => m.MatterId, (string?)null), token), ct);
                await SafeClearMatterReferenceAsync("ResearchSessions", token => _context.ResearchSessions.IgnoreQueryFilters().Where(s => s.MatterId == id).ExecuteUpdateAsync(setters => setters.SetProperty(s => s.MatterId, (string?)null), token), ct);
                await SafeClearMatterReferenceAsync("ContractAnalyses", token => _context.ContractAnalyses.IgnoreQueryFilters().Where(a => a.MatterId == id).ExecuteUpdateAsync(setters => setters.SetProperty(a => a.MatterId, (string?)null), token), ct);
                await SafeClearMatterReferenceAsync("AiDraftSessions", token => _context.AiDraftSessions.IgnoreQueryFilters().Where(s => s.MatterId == id).ExecuteUpdateAsync(setters => setters.SetProperty(s => s.MatterId, (string?)null), token), ct);
                await SafeClearMatterReferenceAsync("Invoices", token => _context.Invoices.IgnoreQueryFilters().Where(i => i.MatterId == id).ExecuteUpdateAsync(setters => setters.SetProperty(i => i.MatterId, (string?)null), token), ct);
                await SafeClearMatterReferenceAsync("BillingRateCards", token => _context.BillingRateCards.IgnoreQueryFilters().Where(c => c.MatterId == id).ExecuteUpdateAsync(setters => setters.SetProperty(c => c.MatterId, (string?)null), token), ct);
                await SafeClearMatterReferenceAsync("BillingRateCardEntries", token => _context.BillingRateCardEntries.IgnoreQueryFilters().Where(e => e.MatterId == id).ExecuteUpdateAsync(setters => setters.SetProperty(e => e.MatterId, (string?)null), token), ct);
                await SafeClearMatterReferenceAsync("BillingLedgerEntries", token => _context.BillingLedgerEntries.IgnoreQueryFilters().Where(e => e.MatterId == id).ExecuteUpdateAsync(setters => setters.SetProperty(e => e.MatterId, (string?)null), token), ct);
                await SafeClearMatterReferenceAsync("BillingPaymentAllocations", token => _context.BillingPaymentAllocations.IgnoreQueryFilters().Where(a => a.MatterId == id).ExecuteUpdateAsync(setters => setters.SetProperty(a => a.MatterId, (string?)null), token), ct);
                await SafeClearMatterReferenceAsync("BillingEbillingTransmissions", token => _context.BillingEbillingTransmissions.IgnoreQueryFilters().Where(t => t.MatterId == id).ExecuteUpdateAsync(setters => setters.SetProperty(t => t.MatterId, (string?)null), token), ct);
                await SafeClearMatterReferenceAsync("BillingEbillingResultEvents", token => _context.BillingEbillingResultEvents.IgnoreQueryFilters().Where(e => e.MatterId == id).ExecuteUpdateAsync(setters => setters.SetProperty(e => e.MatterId, (string?)null), token), ct);
                await SafeClearMatterReferenceAsync("TrustRiskEvents", token => _context.TrustRiskEvents.IgnoreQueryFilters().Where(e => e.MatterId == id).ExecuteUpdateAsync(setters => setters.SetProperty(e => e.MatterId, (string?)null), token), ct);

                // Required direct dependents.
                await SafeDeleteByMatterAsync("OpposingParties", _context.OpposingParties.IgnoreQueryFilters().Where(p => p.MatterId == id), ct);
                await SafeDeleteByMatterAsync("MatterClientLinks", _context.MatterClientLinks.IgnoreQueryFilters().Where(link => link.MatterId == id), ct);
                await SafeDeleteByMatterAsync("MatterNotes", _context.MatterNotes.IgnoreQueryFilters().Where(note => note.MatterId == id), ct);
                await SafeDeleteByMatterAsync("Deadlines", _context.Deadlines.IgnoreQueryFilters().Where(d => d.MatterId == id), ct);
                await SafeDeleteByMatterAsync("CasePredictions", _context.CasePredictions.IgnoreQueryFilters().Where(p => p.MatterId == id), ct);
                await SafeDeleteByMatterAsync("MatterBillingPolicies", _context.MatterBillingPolicies.IgnoreQueryFilters().Where(p => p.MatterId == id), ct);
                await SafeDeleteByMatterAsync("BillingPrebillLines", _context.BillingPrebillLines.IgnoreQueryFilters().Where(l => l.MatterId == id), ct);
                await SafeDeleteByMatterAsync("BillingPrebillBatches", _context.BillingPrebillBatches.IgnoreQueryFilters().Where(b => b.MatterId == id), ct);

                // Planner hierarchy.
                var outcomePlanIds = await SafeCollectIdsAsync("OutcomeFeePlans", "MatterId", _context.OutcomeFeePlans
                    .IgnoreQueryFilters()
                    .Where(p => p.MatterId == id)
                    .Select(p => p.Id), ct);
                var planVersionIds = outcomePlanIds.Count == 0
                    ? new List<string>()
                    : await SafeCollectIdsAsync("OutcomeFeePlanVersions", "PlanId", _context.OutcomeFeePlanVersions
                        .IgnoreQueryFilters()
                        .Where(v => outcomePlanIds.Contains(v.PlanId))
                        .Select(v => v.Id), ct);
                var scenarioIds = planVersionIds.Count == 0
                    ? new List<string>()
                    : await SafeCollectIdsAsync("OutcomeFeeScenarios", "PlanVersionId", _context.OutcomeFeeScenarios
                        .IgnoreQueryFilters()
                        .Where(s => planVersionIds.Contains(s.PlanVersionId))
                        .Select(s => s.Id), ct);

                if (scenarioIds.Count > 0)
                {
                    await SafeDeleteAsync("OutcomeFeeCollectionsForecasts", "ScenarioId", _context.OutcomeFeeCollectionsForecasts.IgnoreQueryFilters().Where(f => scenarioIds.Contains(f.ScenarioId)), ct);
                    await SafeDeleteAsync("OutcomeFeePhaseForecasts", "ScenarioId", _context.OutcomeFeePhaseForecasts.IgnoreQueryFilters().Where(f => scenarioIds.Contains(f.ScenarioId)), ct);
                    await SafeDeleteAsync("OutcomeFeeStaffingLines", "ScenarioId", _context.OutcomeFeeStaffingLines.IgnoreQueryFilters().Where(l => scenarioIds.Contains(l.ScenarioId)), ct);
                    await SafeDeleteAsync("OutcomeFeeScenarios", "PlanVersionId", _context.OutcomeFeeScenarios.IgnoreQueryFilters().Where(s => planVersionIds.Contains(s.PlanVersionId)), ct);
                }

                if (planVersionIds.Count > 0)
                {
                    await SafeDeleteAsync("OutcomeFeeAssumptions", "PlanVersionId", _context.OutcomeFeeAssumptions.IgnoreQueryFilters().Where(a => planVersionIds.Contains(a.PlanVersionId)), ct);
                    await SafeDeleteAsync("OutcomeFeePlanVersions", "PlanId", _context.OutcomeFeePlanVersions.IgnoreQueryFilters().Where(v => outcomePlanIds.Contains(v.PlanId)), ct);
                }

                if (outcomePlanIds.Count > 0)
                {
                    await SafeDeleteAsync("OutcomeFeeUpdateEvents", "PlanId", _context.OutcomeFeeUpdateEvents.IgnoreQueryFilters().Where(e => outcomePlanIds.Contains(e.PlanId)), ct);
                    await SafeDeleteByMatterAsync("OutcomeFeePlans", _context.OutcomeFeePlans.IgnoreQueryFilters().Where(p => p.MatterId == id), ct);
                }

                // Client transparency hierarchy.
                var transparencySnapshotIds = await SafeCollectIdsAsync("ClientTransparencySnapshots", "MatterId", _context.ClientTransparencySnapshots
                    .IgnoreQueryFilters()
                    .Where(s => s.MatterId == id)
                    .Select(s => s.Id), ct);

                if (transparencySnapshotIds.Count > 0)
                {
                    await SafeDeleteAsync("ClientTransparencyTimelineItems", "SnapshotId", _context.ClientTransparencyTimelineItems.IgnoreQueryFilters().Where(i => transparencySnapshotIds.Contains(i.SnapshotId)), ct);
                    await SafeDeleteAsync("ClientTransparencyDelayReasons", "SnapshotId", _context.ClientTransparencyDelayReasons.IgnoreQueryFilters().Where(r => transparencySnapshotIds.Contains(r.SnapshotId)), ct);
                    await SafeDeleteAsync("ClientTransparencyNextSteps", "SnapshotId", _context.ClientTransparencyNextSteps.IgnoreQueryFilters().Where(s => transparencySnapshotIds.Contains(s.SnapshotId)), ct);
                    await SafeDeleteAsync("ClientTransparencyCostImpacts", "SnapshotId", _context.ClientTransparencyCostImpacts.IgnoreQueryFilters().Where(c => transparencySnapshotIds.Contains(c.SnapshotId)), ct);
                    await SafeDeleteAsync("ClientTransparencyReviewActions", "SnapshotId", _context.ClientTransparencyReviewActions.IgnoreQueryFilters().Where(a => transparencySnapshotIds.Contains(a.SnapshotId)), ct);
                    await SafeDeleteByMatterAsync("ClientTransparencySnapshots", _context.ClientTransparencySnapshots.IgnoreQueryFilters().Where(s => s.MatterId == id), ct);
                }

                await SafeDeleteByMatterAsync("ClientTransparencyUpdateEvents", _context.ClientTransparencyUpdateEvents.IgnoreQueryFilters().Where(e => e.MatterId == id), ct);
                await SafeDeleteByMatterAsync("ClientTransparencyProfiles", _context.ClientTransparencyProfiles.IgnoreQueryFilters().Where(p => p.MatterId == id), ct);

                _context.Matters.Remove(matter);
                await _context.SaveChangesAsync(ct);

            await TryAuditAsync("matter.delete", "Matter", id, $"Deleted matter {matter.Name}");
                await tx.CommitAsync(ct);

                return NoContent();
            }
            catch (Exception ex) when (IsForeignKeyViolation(ex))
            {
                await tx.RollbackAsync(ct);
                _logger.LogWarning(ex, "Matter delete blocked by related records for matter {MatterId}. RootCause={RootCause}", id, ex.GetBaseException().Message);
                return Problem(statusCode: StatusCodes.Status409Conflict, title: "Matter delete blocked.", detail: "Matter could not be deleted because related records are still linked.");
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync(ct);
                _logger.LogError(ex, "Matter delete failed for matter {MatterId}. RootCause={RootCause}", id, ex.GetBaseException().Message);
                if (await TrySoftDeleteMatterAsync(id, ct))
                {
                    return NoContent();
                }

                return Problem(statusCode: StatusCodes.Status500InternalServerError, title: "Matter delete failed.", detail: "The server failed to delete the matter.");
            }
        }
        
        // POST: api/Matters/5/archive
        [HttpPost("{id}/archive")]
        public async Task<IActionResult> ArchiveMatter(string id)
        {
            if (!await _matterAccess.CanManageMatterAsync(id, User, cancellationToken: HttpContext.RequestAborted))
            {
                return Problem(statusCode: StatusCodes.Status403Forbidden, title: "Matter archive is forbidden.", detail: $"You are not allowed to archive matter '{id}'.");
            }

            var matter = await _context.Matters.FirstOrDefaultAsync(m => m.Id == id);
            if (matter == null) return NotFoundProblem("Matter not found.", $"Matter '{id}' was not found.");

            matter.Status = "Archived";
            await _context.SaveChangesAsync();

            await TryAuditAsync("matter.archive", "Matter", id, "Archived matter");
            await TryTriggerOutcomeFeePlannerAsync(id, "matter_status_archive");

            return Ok(MatterResponse.FromModel(matter));
        }

        // POST: api/Matters/5/restore
        [HttpPost("{id}/restore")]
        public async Task<IActionResult> RestoreMatter(string id)
        {
            if (!await _matterAccess.CanManageMatterAsync(id, User, cancellationToken: HttpContext.RequestAborted))
            {
                return Problem(statusCode: StatusCodes.Status403Forbidden, title: "Matter restore is forbidden.", detail: $"You are not allowed to restore matter '{id}'.");
            }

            var matter = await _context.Matters.FirstOrDefaultAsync(m => m.Id == id);
            if (matter == null) return NotFoundProblem("Matter not found.", $"Matter '{id}' was not found.");

            matter.Status = "Open"; // Restore to Open
            await _context.SaveChangesAsync();

            await TryAuditAsync("matter.restore", "Matter", id, "Restored matter");
            await TryTriggerOutcomeFeePlannerAsync(id, "matter_status_restore");

            return Ok(MatterResponse.FromModel(matter));
        }

        private async Task SafeClearMatterReferenceAsync(
            string tableName,
            Func<CancellationToken, Task> operation,
            CancellationToken cancellationToken)
        {
            if (!await TableHasColumnAsync(tableName, "MatterId", cancellationToken))
            {
                return;
            }

            try
            {
                await operation(cancellationToken);
            }
            catch (Exception ex) when (IsMissingSchemaException(ex))
            {
                _logger.LogWarning(ex, "Skipping matter cleanup for missing schema on table {TableName}.", tableName);
            }
        }

        private Task SafeDeleteByMatterAsync<TEntity>(string tableName, IQueryable<TEntity> query, CancellationToken cancellationToken)
            where TEntity : class
            => SafeDeleteAsync(tableName, "MatterId", query, cancellationToken);

        private async Task SafeDeleteAsync<TEntity>(
            string tableName,
            string columnName,
            IQueryable<TEntity> query,
            CancellationToken cancellationToken)
            where TEntity : class
        {
            if (!await TableHasColumnAsync(tableName, columnName, cancellationToken))
            {
                return;
            }

            try
            {
                await query.ExecuteDeleteAsync(cancellationToken);
            }
            catch (Exception ex) when (IsMissingSchemaException(ex))
            {
                _logger.LogWarning(ex, "Skipping delete cleanup for missing schema on table {TableName}.", tableName);
            }
        }

        private async Task<List<string>> SafeCollectIdsAsync(
            string tableName,
            string columnName,
            IQueryable<string> query,
            CancellationToken cancellationToken)
        {
            if (!await TableHasColumnAsync(tableName, columnName, cancellationToken))
            {
                return new List<string>();
            }

            try
            {
                return await query.ToListAsync(cancellationToken);
            }
            catch (Exception ex) when (IsMissingSchemaException(ex))
            {
                _logger.LogWarning(ex, "Skipping id collection for missing schema on table {TableName}.", tableName);
                return new List<string>();
            }
        }

        private async Task<bool> TableHasColumnAsync(string tableName, string columnName, CancellationToken cancellationToken)
        {
            var cacheKey = $"{tableName}.{columnName}";
            if (_schemaPresenceCache.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }

            var connection = _context.Database.GetDbConnection();
            var shouldClose = connection.State != ConnectionState.Open;
            if (shouldClose)
            {
                await connection.OpenAsync(cancellationToken);
            }

            try
            {
                using var command = connection.CreateCommand();
                command.CommandText = BuildColumnExistsSql(tableName);

                AddParameter(command, "@tableName", tableName);
                AddParameter(command, "@columnName", columnName);

                var scalar = await command.ExecuteScalarAsync(cancellationToken);
                var exists = scalar switch
                {
                    bool booleanValue => booleanValue,
                    long longValue => longValue > 0,
                    int intValue => intValue > 0,
                    decimal decimalValue => decimalValue > 0,
                    _ => false
                };

                _schemaPresenceCache[cacheKey] = exists;
                return exists;
            }
            finally
            {
                if (shouldClose)
                {
                    await connection.CloseAsync();
                }
            }
        }

        private string BuildColumnExistsSql(string tableName)
        {
            if (_context.Database.IsNpgsql())
            {
                return """
                    select exists (
                        select 1
                        from information_schema.columns
                        where table_schema = current_schema()
                          and table_name = @tableName
                          and column_name = @columnName
                    );
                    """;
            }

            if (_context.Database.IsSqlite())
            {
                return $"select count(1) from pragma_table_info('{EscapeSqlLiteral(tableName)}') where name = @columnName;";
            }

            return """
                select count(1)
                from information_schema.columns
                where table_name = @tableName
                  and column_name = @columnName;
                """;
        }

        private static void AddParameter(DbCommand command, string name, object value)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }

        private static string EscapeSqlLiteral(string value) => value.Replace("'", "''", StringComparison.Ordinal);

        private static bool IsMissingSchemaException(Exception ex)
        {
            var root = ex.GetBaseException();
            return root switch
            {
                PostgresException postgresEx => postgresEx.SqlState is PostgresErrorCodes.UndefinedTable or PostgresErrorCodes.UndefinedColumn,
                SqliteException sqliteEx => sqliteEx.SqliteErrorCode == 1 &&
                                            (sqliteEx.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase) ||
                                             sqliteEx.Message.Contains("no such column", StringComparison.OrdinalIgnoreCase)),
                _ => false
            };
        }

        private static bool IsForeignKeyViolation(Exception ex)
        {
            var root = ex.GetBaseException();
            return root switch
            {
                PostgresException postgresEx => postgresEx.SqlState == PostgresErrorCodes.ForeignKeyViolation,
                SqliteException sqliteEx => sqliteEx.SqliteErrorCode == 19 &&
                                            sqliteEx.Message.Contains("FOREIGN KEY constraint failed", StringComparison.OrdinalIgnoreCase),
                _ => false
            };
        }

        private async Task<bool> TrySoftDeleteMatterAsync(string id, CancellationToken cancellationToken)
        {
            try
            {
                _context.ChangeTracker.Clear();

                var matter = await _context.Matters
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(m => m.Id == id, cancellationToken);

                if (matter == null)
                {
                    return true;
                }

                matter.Status = DeletedMatterStatus;
                matter.CurrentOutcomeFeePlanId = null;

                await _context.SaveChangesAsync(cancellationToken);

                try
                {
                    await _auditLogger.LogAsync(HttpContext, "matter.soft_delete", "Matter", id, "Soft-deleted after hard delete failure.");
                }
                catch (Exception auditEx)
                {
                    _logger.LogWarning(auditEx, "Soft-delete audit logging failed for matter {MatterId}", id);
                }

                _logger.LogWarning("Matter {MatterId} was soft-deleted after hard delete failure.", id);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Matter soft-delete fallback failed for matter {MatterId}. RootCause={RootCause}", id, ex.GetBaseException().Message);
                return false;
            }
        }
        
        private async Task<string?> ResolveValidClientIdAsync(string? clientId)
        {
            var normalizedClientId = clientId?.Trim();
            if (string.IsNullOrWhiteSpace(normalizedClientId))
            {
                return null;
            }

            var clientExists = await _context.Clients
                .AsNoTracking()
                .AnyAsync(c => c.Id == normalizedClientId);

            return clientExists ? normalizedClientId : null;
        }

        private static Matter MapCreateRequest(CreateMatterRequest request, string currentUserId, string resolvedClientId)
        {
            return new Matter
            {
                Id = Guid.NewGuid().ToString(),
                CaseNumber = request.CaseNumber,
                Name = request.Name,
                PracticeArea = request.PracticeArea ?? string.Empty,
                CourtType = NormalizeOptionalText(request.CourtType),
                Outcome = NormalizeOptionalText(request.Outcome),
                Status = request.Status,
                FeeStructure = request.FeeStructure,
                OpenDate = DateTime.UtcNow,
                ResponsibleAttorney = request.ResponsibleAttorney,
                BillableRate = request.BillableRate,
                ClientId = resolvedClientId,
                CreatedByUserId = currentUserId,
                RelatedClientIds = request.RelatedClientIds
            };
        }

        private Task<MatterRelatedClientResolution> ResolveRelatedClientsForWriteAsync(string primaryClientId, IEnumerable<string>? requestedClientIds)
        {
            if ((requestedClientIds?.Any(id => !string.IsNullOrWhiteSpace(id)) ?? false))
            {
                _logger.LogInformation("Ignoring secondary client links on matter write while the feature is temporarily disabled. PrimaryClientId={PrimaryClientId}", primaryClientId);
            }

            return Task.FromResult(new MatterRelatedClientResolution(Array.Empty<string>(), Array.Empty<string>()));
        }

        private async Task TrySyncRelatedClientsAsync(string matterId, IReadOnlyCollection<string> relatedClientIds, CancellationToken cancellationToken)
        {
            if (relatedClientIds.Count == 0)
            {
                return;
            }

            try
            {
                await _matterClientLinks.SyncRelatedClientsAsync(matterId, relatedClientIds, cancellationToken);
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                ResetFailedMatterClientLinkEntries(matterId);

                if (IsMissingSchemaException(ex))
                {
                    _logger.LogWarning(ex, "MatterClientLinks schema is unavailable while syncing related clients for matter {MatterId}. Proceeding without secondary client links.", matterId);
                    return;
                }

                _logger.LogError(ex, "Secondary client link persistence failed for matter {MatterId}. Matter was saved, but related client links were skipped.", matterId);
            }
        }

        private async Task TryRemovePrimaryClientDuplicatesAsync(string matterId, string primaryClientId, CancellationToken cancellationToken)
        {
            try
            {
                await _matterClientLinks.RemovePrimaryClientDuplicatesAsync(matterId, primaryClientId, cancellationToken);
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                ResetFailedMatterClientLinkEntries(matterId);

                if (IsMissingSchemaException(ex))
                {
                    _logger.LogWarning(ex, "MatterClientLinks schema is unavailable while removing duplicate primary links for matter {MatterId}.", matterId);
                    return;
                }

                _logger.LogError(ex, "Failed to normalize secondary client links for matter {MatterId} after update. Matter changes were kept.", matterId);
            }
        }

        private async Task TryPopulateRelatedClientsAsync(IList<Matter> matters, CancellationToken cancellationToken)
        {
            if (!EnableSecondaryClientWrites)
            {
                foreach (var matter in matters)
                {
                    matter.RelatedClientIds = new List<string>();
                    matter.RelatedClients = new List<Client>();
                }

                return;
            }

            try
            {
                await _matterClientLinks.PopulateRelatedClientsAsync(matters, cancellationToken);
            }
            catch (Exception ex)
            {
                foreach (var matter in matters)
                {
                    matter.RelatedClientIds = new List<string>();
                    matter.RelatedClients = new List<Client>();
                }

                _logger.LogWarning(ex, "Matter related client population failed. Returning matters without secondary client links.");
            }
        }

        private Task TryPopulateRelatedClientsAsync(Matter matter, CancellationToken cancellationToken)
        {
            return TryPopulateRelatedClientsAsync(new List<Matter> { matter }, cancellationToken);
        }

        private async Task TryAuditAsync(string action, string entity, string entityId, string? details = null)
        {
            try
            {
                await _auditLogger.LogAsync(HttpContext, action, entity, entityId, details);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Audit logging failed during matter workflow. Action={Action} EntityId={EntityId}", action, entityId);
            }
        }

        private void ResetFailedMatterClientLinkEntries(string matterId)
        {
            foreach (var entry in _context.ChangeTracker.Entries<MatterClientLink>()
                         .Where(entry => string.Equals(entry.Entity.MatterId, matterId, StringComparison.Ordinal))
                         .ToList())
            {
                entry.State = EntityState.Detached;
            }
        }

        private Task TryTriggerOutcomeFeePlannerAsync(string matterId, string triggerType)
        {
            if (string.IsNullOrWhiteSpace(matterId)) return Task.CompletedTask;
            try
            {
                _workflowTriggerDispatcher.TryEnqueue(
                    GetUserId() ?? "system",
                    new OutcomeFeePlanTriggerRequest
                    {
                        MatterId = matterId,
                        TriggerType = triggerType,
                        TriggerEntityType = nameof(Matter),
                        TriggerEntityId = matterId
                    },
                    new ClientTransparencyTriggerRequest
                    {
                        MatterId = matterId,
                        TriggerType = triggerType,
                        TriggerEntityType = nameof(Matter),
                        TriggerEntityId = matterId
                    });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Workflow trigger enqueue failed for matter {MatterId}", matterId);
            }

            return Task.CompletedTask;
        }

        private string? GetUserId()
        {
            return User.FindFirst("sub")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        }

        private static string? NormalizeOptionalText(string? value)
        {
            var trimmed = value?.Trim();
            return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
        }

        private static int NormalizePage(int page) => page <= 0 ? 1 : page;

        private static int NormalizePageSize(int pageSize)
        {
            if (pageSize <= 0)
            {
                return DefaultMatterPageSize;
            }

            return Math.Clamp(pageSize, 1, MaxMatterPageSize);
        }

        private void AddPaginationHeaders(int totalCount, int page, int pageSize)
        {
            Response.Headers["X-Total-Count"] = totalCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Response.Headers["X-Page"] = page.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Response.Headers["X-Page-Size"] = pageSize.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private ActionResult NotFoundProblem(string title, string detail)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, title: title, detail: detail);
        }

        private static void NormalizeSharingSettings(Matter matter)
        {
            if (!matter.ShareWithFirm)
            {
                matter.ShareBillingWithFirm = false;
                matter.ShareNotesWithFirm = false;
                return;
            }

            if (!matter.ShareBillingWithFirm)
            {
                matter.ShareNotesWithFirm = false;
            }
        }
    }
}
