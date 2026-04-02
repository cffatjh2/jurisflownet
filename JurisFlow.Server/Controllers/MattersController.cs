using System.Data;
using System.Data.Common;
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
    public class MattersController : ControllerBase
    {
        private const string DeletedMatterStatus = "Deleted";
        private readonly JurisFlowDbContext _context;
        private readonly AuditLogger _auditLogger;
        private readonly FirmStructureService _firmStructure;
        private readonly OutcomeFeePlannerService _outcomeFeePlanner;
        private readonly ClientTransparencyService _clientTransparencyService;
        private readonly ILogger<MattersController> _logger;
        private readonly Dictionary<string, bool> _schemaPresenceCache = new(StringComparer.OrdinalIgnoreCase);

        public MattersController(
            JurisFlowDbContext context,
            AuditLogger auditLogger,
            FirmStructureService firmStructure,
            OutcomeFeePlannerService outcomeFeePlanner,
            ClientTransparencyService clientTransparencyService,
            ILogger<MattersController> logger)
        {
            _context = context;
            _auditLogger = auditLogger;
            _firmStructure = firmStructure;
            _outcomeFeePlanner = outcomeFeePlanner;
            _clientTransparencyService = clientTransparencyService;
            _logger = logger;
        }

        // GET: api/Matters
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Matter>>> GetMatters([FromQuery] string? status, [FromQuery] string? entityId, [FromQuery] string? officeId)
        {
            var query = _context.Matters
                .AsNoTracking()
                .AsQueryable();

            if (!string.IsNullOrEmpty(status))
            {
                // Frontend might send "Archive" or "Open"
                // The Matter model has 'Status' field.
                // Assuming "Archived" is the status for archive.
                if (status.ToLower() == "archive" || status.ToLower() == "archived")
                {
                     query = query.Where(m => m.Status == "Archived");
                }
                else
                {
                     query = query.Where(m => m.Status == status || (status == "Open" && m.Status != "Archived" && m.Status != "Closed"));
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

            return await query.OrderByDescending(m => m.OpenDate).ToListAsync();
        }

        // GET: api/Matters/5
        [HttpGet("{id}")]
        public async Task<ActionResult<Matter>> GetMatter(string id)
        {
            var matter = await _context.Matters
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == id);

            if (matter == null)
            {
                return NotFound();
            }

            return matter;
        }

        // POST: api/Matters
        [HttpPost]
        public async Task<ActionResult<Matter>> PostMatter(Matter matter)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            if (string.IsNullOrWhiteSpace(matter.ClientId))
            {
                return BadRequest(new { message = "ClientId is required." });
            }

            var resolvedClientId = await ResolveValidClientIdAsync(matter.ClientId);
            if (resolvedClientId == null)
            {
                return BadRequest(new { message = "Selected client was not found." });
            }

            matter.Id = Guid.NewGuid().ToString();
            matter.OpenDate = DateTime.UtcNow;
            matter.ClientId = resolvedClientId;

            var resolved = await _firmStructure.ResolveEntityOfficeAsync(matter.EntityId, matter.OfficeId);
            matter.EntityId = resolved.entityId;
            matter.OfficeId = resolved.officeId;
            
            _context.Matters.Add(matter);
            await _context.SaveChangesAsync();

            await _auditLogger.LogAsync(HttpContext, "matter.create", "Matter", matter.Id, $"ClientId={matter.ClientId}, Name={matter.Name}");

            return CreatedAtAction("GetMatter", new { id = matter.Id }, matter);
        }

        // PUT: api/Matters/5
        [HttpPut("{id}")]
        public async Task<IActionResult> PutMatter(string id, Matter matter)
        {
            if (id != matter.Id) return BadRequest();
            if (!ModelState.IsValid) return BadRequest(ModelState);
            if (string.IsNullOrWhiteSpace(matter.ClientId))
            {
                return BadRequest(new { message = "ClientId is required." });
            }

            var existingMatter = await _context.Matters.FirstOrDefaultAsync(m => m.Id == id);
            if (existingMatter == null)
            {
                return NotFound();
            }

            var resolvedClientId = await ResolveValidClientIdAsync(matter.ClientId);
            if (resolvedClientId == null)
            {
                return BadRequest(new { message = "Selected client was not found." });
            }

            var resolved = await _firmStructure.ResolveEntityOfficeAsync(matter.EntityId, matter.OfficeId);

            existingMatter.CaseNumber = matter.CaseNumber;
            existingMatter.Name = matter.Name;
            existingMatter.PracticeArea = matter.PracticeArea;
            existingMatter.CourtType = matter.CourtType;
            existingMatter.Outcome = matter.Outcome;
            existingMatter.Status = matter.Status;
            existingMatter.FeeStructure = matter.FeeStructure;
            existingMatter.OpenDate = matter.OpenDate;
            existingMatter.ResponsibleAttorney = matter.ResponsibleAttorney;
            existingMatter.BillableRate = matter.BillableRate;
            existingMatter.TrustBalance = matter.TrustBalance;
            existingMatter.CurrentOutcomeFeePlanId = matter.CurrentOutcomeFeePlanId;
            existingMatter.EntityId = resolved.entityId;
            existingMatter.OfficeId = resolved.officeId;
            existingMatter.ClientId = resolvedClientId;
            existingMatter.ConflictCheckDate = matter.ConflictCheckDate;
            existingMatter.ConflictCheckCleared = matter.ConflictCheckCleared;
            existingMatter.ConflictWaiverObtained = matter.ConflictWaiverObtained;

            await _context.SaveChangesAsync();

            await _auditLogger.LogAsync(HttpContext, "matter.update", "Matter", existingMatter.Id, $"Status={existingMatter.Status}");
            await TryTriggerOutcomeFeePlannerAsync(existingMatter.Id, "matter_update");

            return NoContent();
        }

        // DELETE: api/Matters/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteMatter(string id)
        {
            var matter = await _context.Matters.IgnoreQueryFilters().FirstOrDefaultAsync(m => m.Id == id);
            if (matter == null) return NotFound();
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

                await _auditLogger.LogAsync(HttpContext, "matter.delete", "Matter", id, $"Deleted matter {matter.Name}");
                await tx.CommitAsync(ct);

                return NoContent();
            }
            catch (Exception ex) when (IsForeignKeyViolation(ex))
            {
                await tx.RollbackAsync(ct);
                _logger.LogWarning(ex, "Matter delete blocked by related records for matter {MatterId}. RootCause={RootCause}", id, ex.GetBaseException().Message);
                return Conflict(new { message = "Matter could not be deleted because related records are still linked." });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync(ct);
                _logger.LogError(ex, "Matter delete failed for matter {MatterId}. RootCause={RootCause}", id, ex.GetBaseException().Message);
                if (await TrySoftDeleteMatterAsync(id, ct))
                {
                    return NoContent();
                }

                return StatusCode(500, new { message = "Failed to delete matter." });
            }
        }
        
        // POST: api/Matters/5/archive
        [HttpPost("{id}/archive")]
        public async Task<IActionResult> ArchiveMatter(string id)
        {
            var matter = await _context.Matters.FirstOrDefaultAsync(m => m.Id == id);
            if (matter == null) return NotFound();

            matter.Status = "Archived";
            await _context.SaveChangesAsync();

            await _auditLogger.LogAsync(HttpContext, "matter.archive", "Matter", id, "Archived matter");
            await TryTriggerOutcomeFeePlannerAsync(id, "matter_status_archive");

            return Ok(matter);
        }

        // POST: api/Matters/5/restore
        [HttpPost("{id}/restore")]
        public async Task<IActionResult> RestoreMatter(string id)
        {
            var matter = await _context.Matters.FirstOrDefaultAsync(m => m.Id == id);
            if (matter == null) return NotFound();

            matter.Status = "Open"; // Restore to Open
            await _context.SaveChangesAsync();

            await _auditLogger.LogAsync(HttpContext, "matter.restore", "Matter", id, "Restored matter");
            await TryTriggerOutcomeFeePlannerAsync(id, "matter_status_restore");

            return Ok(matter);
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

        private async Task TryTriggerOutcomeFeePlannerAsync(string matterId, string triggerType)
        {
            if (string.IsNullOrWhiteSpace(matterId)) return;
            try
            {
                await _outcomeFeePlanner.TryProcessTriggerAsync(new OutcomeFeePlanTriggerRequest
                {
                    MatterId = matterId,
                    TriggerType = triggerType,
                    TriggerEntityType = nameof(Matter),
                    TriggerEntityId = matterId
                }, GetUserId() ?? "system", HttpContext.RequestAborted);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Outcome-to-Fee planner trigger failed for matter {MatterId}", matterId);
            }

            try
            {
                await _clientTransparencyService.TryProcessTriggerAsync(new ClientTransparencyTriggerRequest
                {
                    MatterId = matterId,
                    TriggerType = triggerType,
                    TriggerEntityType = nameof(Matter),
                    TriggerEntityId = matterId
                }, GetUserId() ?? "system", HttpContext.RequestAborted);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Client transparency trigger failed for matter {MatterId}", matterId);
            }
        }

        private string? GetUserId()
        {
            return User.FindFirst("sub")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        }
    }
}
