using JurisFlow.Server.Contracts;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using JurisFlow.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace JurisFlow.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Policy = "StaffOnly")]
    [RequestSizeLimit(MaxTrustRequestBodyBytes)]
    public class TrustController : ControllerBase
    {
        private const int MaxTrustRequestBodyBytes = 1024 * 1024;
        private const int DefaultTrustPageSize = 50;
        private const int MaxTrustPageSize = 200;
        private readonly JurisFlowDbContext _context;
        private readonly LegalBillingEngineService _billingEngine;
        private readonly TrustComplianceExportService _trustExportService;
        private readonly TrustComplianceService _trustComplianceService;
        private readonly TrustAccountingService _trustAccountingService;
        private readonly TrustStatementIngestionService _trustStatementIngestionService;
        private readonly TrustOpsInboxService _trustOpsInboxService;
        private readonly TrustCloseAutomationService _trustCloseAutomationService;
        private readonly TrustRecoveryService _trustRecoveryService;
        private readonly TrustBundleIntegrityService _trustBundleIntegrityService;

        public TrustController(
            JurisFlowDbContext context,
            LegalBillingEngineService billingEngine,
            TrustComplianceExportService trustExportService,
            TrustComplianceService trustComplianceService,
            TrustAccountingService trustAccountingService,
            TrustStatementIngestionService trustStatementIngestionService,
            TrustOpsInboxService trustOpsInboxService,
            TrustCloseAutomationService trustCloseAutomationService,
            TrustRecoveryService trustRecoveryService,
            TrustBundleIntegrityService trustBundleIntegrityService)
        {
            _context = context;
            _billingEngine = billingEngine;
            _trustExportService = trustExportService;
            _trustComplianceService = trustComplianceService;
            _trustAccountingService = trustAccountingService;
            _trustStatementIngestionService = trustStatementIngestionService;
            _trustOpsInboxService = trustOpsInboxService;
            _trustCloseAutomationService = trustCloseAutomationService;
            _trustRecoveryService = trustRecoveryService;
            _trustBundleIntegrityService = trustBundleIntegrityService;
        }

        [HttpGet("accounts")]
        public async Task<ActionResult<IEnumerable<TrustBankAccountListItemDto>>> GetTrustAccounts(
            [FromQuery] string? entityId,
            [FromQuery] string? officeId,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = DefaultTrustPageSize)
        {
            var query = _context.TrustBankAccounts.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(entityId))
            {
                query = query.Where(a => a.EntityId == entityId);
            }

            if (!string.IsNullOrWhiteSpace(officeId))
            {
                query = query.Where(a => a.OfficeId == officeId);
            }

            var normalizedPage = NormalizePage(page);
            var normalizedPageSize = NormalizePageSize(pageSize);
            var totalCount = await query.CountAsync(HttpContext.RequestAborted);
            var accounts = await query
                .OrderBy(a => a.Name)
                .Skip((normalizedPage - 1) * normalizedPageSize)
                .Take(normalizedPageSize)
                .Select(a => new TrustBankAccountListItemDto
                {
                    Id = a.Id,
                    Name = a.Name,
                    BankName = a.BankName,
                    Jurisdiction = a.Jurisdiction,
                    AccountType = a.AccountType,
                    ResponsibleLawyerUserId = a.ResponsibleLawyerUserId,
                    StatementCadence = a.StatementCadence,
                    OverdraftNotificationEnabled = a.OverdraftNotificationEnabled,
                    CurrentBalance = a.CurrentBalance,
                    ClearedBalance = a.ClearedBalance,
                    UnclearedBalance = a.UnclearedBalance,
                    AvailableDisbursementCapacity = a.AvailableDisbursementCapacity,
                    Status = a.Status,
                    EntityId = a.EntityId,
                    OfficeId = a.OfficeId,
                    CreatedAt = a.CreatedAt,
                    UpdatedAt = a.UpdatedAt
                })
                .ToListAsync(HttpContext.RequestAborted);

            AddPaginationHeaders(totalCount, normalizedPage, normalizedPageSize);
            return accounts;
        }

        [HttpPost("accounts")]
        public Task<ActionResult<TrustBankAccount>> CreateTrustAccount([FromBody] CreateTrustAccountRequest request)
        {
            return ExecuteAsync(
                () => _trustAccountingService.CreateTrustAccountAsync(request, HttpContext.RequestAborted),
                account => CreatedAtAction(nameof(GetTrustAccounts), new { id = account.Id }, account));
        }

        [HttpGet("accounts/{id}/governance")]
        public Task<ActionResult<TrustAccountGovernanceDto>> GetAccountGovernance(string id)
        {
            return ExecuteAsync(
                () => _trustAccountingService.GetAccountGovernanceAsync(id, HttpContext.RequestAborted),
                dto => Ok(dto));
        }

        [HttpPost("accounts/{id}/governance")]
        public Task<ActionResult<TrustAccountGovernanceDto>> UpdateAccountGovernance(string id, [FromBody] TrustAccountGovernanceDto dto)
        {
            return ExecuteAsync(
                () => _trustAccountingService.UpdateAccountGovernanceAsync(id, dto, HttpContext.RequestAborted),
                result => Ok(result));
        }

        [HttpGet("policies")]
        public Task<ActionResult<IReadOnlyList<TrustJurisdictionPolicyUpsertDto>>> GetPolicies([FromQuery] string? jurisdiction = null, [FromQuery] string? accountType = null)
        {
            return ExecuteAsync(
                () => _trustAccountingService.GetJurisdictionPoliciesAsync(jurisdiction, accountType, HttpContext.RequestAborted),
                result => Ok(result));
        }

        [HttpPost("policies")]
        public Task<ActionResult<TrustJurisdictionPolicyUpsertDto>> UpsertPolicy([FromBody] TrustJurisdictionPolicyUpsertDto dto)
        {
            return ExecuteAsync(
                () => _trustAccountingService.UpsertJurisdictionPolicyAsync(dto, HttpContext.RequestAborted),
                result => Ok(result));
        }

        [HttpGet("ledgers")]
        public async Task<ActionResult<IEnumerable<ClientTrustLedgerListItemDto>>> GetLedgers(
            [FromQuery] string? entityId,
            [FromQuery] string? officeId,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = DefaultTrustPageSize)
        {
            var query = _context.ClientTrustLedgers.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(entityId))
            {
                query = query.Where(l => l.EntityId == entityId);
            }

            if (!string.IsNullOrWhiteSpace(officeId))
            {
                query = query.Where(l => l.OfficeId == officeId);
            }

            var normalizedPage = NormalizePage(page);
            var normalizedPageSize = NormalizePageSize(pageSize);
            var totalCount = await query.CountAsync(HttpContext.RequestAborted);

            var ledgers = await query
                .OrderByDescending(l => l.UpdatedAt)
                .Skip((normalizedPage - 1) * normalizedPageSize)
                .Take(normalizedPageSize)
                .Select(l => new ClientTrustLedgerListItemDto
                {
                    Id = l.Id,
                    ClientId = l.ClientId,
                    ClientName = l.Client != null ? l.Client.Name : null,
                    MatterId = l.MatterId,
                    TrustAccountId = l.TrustAccountId,
                    TrustAccountName = l.TrustAccount != null ? l.TrustAccount.Name : null,
                    EntityId = l.EntityId,
                    OfficeId = l.OfficeId,
                    RunningBalance = l.RunningBalance,
                    ClearedBalance = l.ClearedBalance,
                    UnclearedBalance = l.UnclearedBalance,
                    AvailableToDisburse = l.AvailableToDisburse,
                    HoldAmount = l.HoldAmount,
                    Status = l.Status,
                    Notes = l.Notes,
                    CreatedAt = l.CreatedAt,
                    UpdatedAt = l.UpdatedAt
                })
                .ToListAsync(HttpContext.RequestAborted);

            AddPaginationHeaders(totalCount, normalizedPage, normalizedPageSize);
            return ledgers;
        }

        [HttpPost("ledgers")]
        public Task<ActionResult<ClientTrustLedger>> CreateLedger([FromBody] ClientTrustLedger ledger)
        {
            return ExecuteAsync(
                () => _trustAccountingService.CreateLedgerAsync(ledger, HttpContext.RequestAborted),
                created => CreatedAtAction(nameof(GetLedgers), new { id = created.Id }, created));
        }

        [HttpGet("transactions")]
        public async Task<ActionResult<IEnumerable<TrustTransactionListItemDto>>> GetTransactions(
            [FromQuery] int? limit = null,
            [FromQuery] string? entityId = null,
            [FromQuery] string? officeId = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = DefaultTrustPageSize)
        {
            var query = _context.TrustTransactions.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(entityId))
            {
                query = query.Where(t => t.EntityId == entityId);
            }

            if (!string.IsNullOrWhiteSpace(officeId))
            {
                query = query.Where(t => t.OfficeId == officeId);
            }

            var normalizedPage = NormalizePage(page);
            var normalizedPageSize = NormalizePageSize(limit ?? pageSize);
            var totalCount = await query.CountAsync(HttpContext.RequestAborted);

            var transactions = await query
                .OrderByDescending(t => t.CreatedAt)
                .Skip((normalizedPage - 1) * normalizedPageSize)
                .Take(normalizedPageSize)
                .Select(t => new TrustTransactionListItemDto
                {
                    Id = t.Id,
                    TrustAccountId = t.TrustAccountId,
                    MatterId = t.MatterId,
                    MatterName = t.Matter != null ? t.Matter.Name : null,
                    Type = t.Type,
                    Amount = t.Amount,
                    Description = t.Description,
                    Reference = t.Reference,
                    PayorPayee = t.PayorPayee,
                    CheckNumber = t.CheckNumber,
                    LedgerId = t.LedgerId,
                    DisbursementClass = t.DisbursementClass,
                    ApprovalStatus = t.ApprovalStatus,
                    EntityId = t.EntityId,
                    OfficeId = t.OfficeId,
                    Status = t.Status,
                    CreatedBy = t.CreatedBy,
                    ApprovedBy = t.ApprovedBy,
                    ApprovedAt = t.ApprovedAt,
                    RejectedBy = t.RejectedBy,
                    RejectedAt = t.RejectedAt,
                    RejectionReason = t.RejectionReason,
                    IsVoided = t.IsVoided,
                    VoidedAt = t.VoidedAt,
                    VoidReason = t.VoidReason,
                    ClearingStatus = t.ClearingStatus,
                    ClearedAt = t.ClearedAt,
                    ReturnedAt = t.ReturnedAt,
                    ReturnReason = t.ReturnReason,
                    IsEarned = t.IsEarned,
                    EarnedDate = t.EarnedDate,
                    BalanceBefore = t.BalanceBefore,
                    BalanceAfter = t.BalanceAfter,
                    CreatedAt = t.CreatedAt,
                    UpdatedAt = t.UpdatedAt
                })
                .ToListAsync(HttpContext.RequestAborted);

            AddPaginationHeaders(totalCount, normalizedPage, normalizedPageSize);
            return transactions;
        }

        [HttpPost("deposit")]
        public Task<ActionResult<TrustTransaction>> Deposit([FromBody] DepositRequest request)
        {
            return ExecuteAsync(
                () => _trustAccountingService.CreateDepositAsync(request, ResolveIdempotencyKey(request.IdempotencyKey), HttpContext.RequestAborted),
                tx => Ok(tx));
        }

        [HttpPost("withdrawal")]
        public Task<ActionResult<TrustTransaction>> Withdrawal([FromBody] WithdrawalRequest request)
        {
            return ExecuteAsync(
                () => _trustAccountingService.CreateWithdrawalAsync(request, ResolveIdempotencyKey(request.IdempotencyKey), HttpContext.RequestAborted),
                tx => Ok(tx));
        }

        [HttpPost("transactions/{id}/approve")]
        public Task<ActionResult<TrustTransaction>> ApproveTransaction(string id)
        {
            return ExecuteAsync(
                async () =>
                {
                    var tx = await _trustAccountingService.ApproveTransactionAsync(id, null, ResolveIdempotencyKey(), HttpContext.RequestAborted);
                    await SyncBillingAllocationAsync(tx, null);
                    return tx;
                },
                tx => Ok(tx));
        }

        [HttpPost("transactions/{id}/approve-step")]
        public Task<ActionResult<TrustTransaction>> ApproveTransactionStep(string id, [FromBody] TrustApproveStepDto dto)
        {
            return ExecuteAsync(
                async () =>
                {
                    var tx = await _trustAccountingService.ApproveTransactionAsync(id, dto, ResolveIdempotencyKey(dto?.IdempotencyKey), HttpContext.RequestAborted);
                    await SyncBillingAllocationAsync(tx, null);
                    return tx;
                },
                tx => Ok(tx));
        }

        [HttpPost("transactions/{id}/override")]
        public Task<ActionResult<TrustTransaction>> OverrideTransaction(string id, [FromBody] TrustOverrideDto dto)
        {
            return ExecuteAsync(
                async () =>
                {
                    var tx = await _trustAccountingService.OverrideTransactionAsync(id, dto, ResolveIdempotencyKey(dto?.IdempotencyKey), HttpContext.RequestAborted);
                    await SyncBillingAllocationAsync(tx, dto?.Reason);
                    return tx;
                },
                tx => Ok(tx));
        }

        [HttpPost("transactions/{id}/reject")]
        public Task<ActionResult<TrustTransaction>> RejectTransaction(string id, [FromBody] TrustRejectDto dto)
        {
            return ExecuteAsync(
                async () =>
                {
                    var tx = await _trustAccountingService.RejectTransactionAsync(id, dto, ResolveIdempotencyKey(dto?.IdempotencyKey), HttpContext.RequestAborted);
                    await SyncBillingAllocationAsync(tx, dto?.Reason);
                    return tx;
                },
                tx => Ok(tx));
        }

        [HttpPost("transactions/{id}/void")]
        public Task<ActionResult<TrustTransaction>> VoidTransaction(string id, [FromBody] TrustVoidDto dto)
        {
            return ExecuteAsync(
                async () =>
                {
                    var tx = await _trustAccountingService.VoidTransactionAsync(id, dto, ResolveIdempotencyKey(dto?.IdempotencyKey), HttpContext.RequestAborted);
                    await SyncBillingAllocationAsync(tx, dto?.Reason);
                    return tx;
                },
                tx => Ok(tx));
        }

        [HttpPost("transactions/{id}/clear")]
        public Task<ActionResult<TrustTransaction>> ClearDeposit(string id, [FromBody] TrustClearDepositDto? dto)
        {
            return ExecuteAsync(
                () => _trustAccountingService.ClearDepositAsync(id, dto, ResolveIdempotencyKey(dto?.IdempotencyKey), HttpContext.RequestAborted),
                tx => Ok(tx));
        }

        [HttpPost("transactions/{id}/return")]
        public Task<ActionResult<TrustTransaction>> ReturnDeposit(string id, [FromBody] TrustReturnDepositDto? dto)
        {
            return ExecuteAsync(
                () => _trustAccountingService.ReturnDepositAsync(id, dto, ResolveIdempotencyKey(dto?.IdempotencyKey), HttpContext.RequestAborted),
                tx => Ok(tx));
        }

        [HttpGet("transactions/{id}/approval-state")]
        public Task<ActionResult<TrustTransactionApprovalStateDto>> GetApprovalState(string id)
        {
            return ExecuteAsync(
                () => _trustAccountingService.GetTransactionApprovalStateAsync(id, HttpContext.RequestAborted),
                result => Ok(result));
        }

        [HttpGet("approvals")]
        public Task<ActionResult<IReadOnlyList<TrustApprovalQueueItemDto>>> GetApprovalQueue([FromQuery] string? trustAccountId = null)
        {
            return ExecuteAsync(
                () => _trustAccountingService.GetApprovalQueueAsync(trustAccountId, HttpContext.RequestAborted),
                result => Ok(result));
        }

        [HttpGet("reconciliations")]
        public async Task<ActionResult<IEnumerable<ReconciliationRecordListItemDto>>> GetReconciliations(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = DefaultTrustPageSize)
        {
            var normalizedPage = NormalizePage(page);
            var normalizedPageSize = NormalizePageSize(pageSize);
            var query = _context.ReconciliationRecords.AsNoTracking();
            var totalCount = await query.CountAsync(HttpContext.RequestAborted);

            var reconciliations = await query
                .OrderByDescending(r => r.PeriodEnd)
                .Skip((normalizedPage - 1) * normalizedPageSize)
                .Take(normalizedPageSize)
                .Select(r => new ReconciliationRecordListItemDto
                {
                    Id = r.Id,
                    TrustAccountId = r.TrustAccountId,
                    TrustAccountName = r.TrustAccount != null ? r.TrustAccount.Name : null,
                    PeriodEnd = r.PeriodEnd,
                    BankStatementBalance = r.BankStatementBalance,
                    TrustLedgerBalance = r.TrustLedgerBalance,
                    ClientLedgerSumBalance = r.ClientLedgerSumBalance,
                    IsReconciled = r.IsReconciled,
                    DiscrepancyAmount = r.DiscrepancyAmount,
                    Notes = r.Notes,
                    CreatedAt = r.CreatedAt
                })
                .ToListAsync(HttpContext.RequestAborted);

            AddPaginationHeaders(totalCount, normalizedPage, normalizedPageSize);
            return reconciliations;
        }

        [HttpGet("statements")]
        public async Task<ActionResult<IEnumerable<TrustStatementImport>>> GetStatements([FromQuery] string? trustAccountId = null, [FromQuery] bool includeHistory = false)
        {
            var query = _context.TrustStatementImports.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(trustAccountId))
            {
                query = query.Where(s => s.TrustAccountId == trustAccountId);
            }

            if (!includeHistory)
            {
                query = query.Where(s => s.Status != "superseded");
            }

            return await query
                .OrderByDescending(s => s.PeriodEnd)
                .ThenBy(s => s.Status == "duplicate" ? 1 : 0)
                .ThenByDescending(s => s.ImportedAt)
                .ToListAsync();
        }

        [HttpGet("evidence-files")]
        public Task<ActionResult<IReadOnlyList<TrustEvidenceFile>>> GetEvidenceFiles([FromQuery] string? trustAccountId = null, [FromQuery] bool includeHistory = false)
        {
            return ExecuteAsync(
                () => _trustStatementIngestionService.GetEvidenceFilesAsync(trustAccountId, includeHistory, HttpContext.RequestAborted),
                result => Ok(result));
        }

        [HttpPost("evidence-files/register")]
        public Task<ActionResult<TrustEvidenceFile>> RegisterEvidenceFile([FromBody] TrustEvidenceFileRegisterRequest request)
        {
            return ExecuteAsync(
                () => _trustStatementIngestionService.RegisterEvidenceFileAsync(request, HttpContext.RequestAborted),
                result => Ok(result));
        }

        [HttpGet("parser-runs")]
        public Task<ActionResult<IReadOnlyList<TrustStatementParserRun>>> GetParserRuns([FromQuery] string? trustAccountId = null)
        {
            return ExecuteAsync(
                () => _trustStatementIngestionService.GetParserRunsAsync(trustAccountId, HttpContext.RequestAborted),
                result => Ok(result));
        }

        [HttpPost("parser-runs")]
        public Task<ActionResult<TrustStatementParserRun>> CreateParserRun([FromBody] TrustStatementParserRunCreateDto request)
        {
            return ExecuteAsync(
                () => _trustStatementIngestionService.CreateParserRunAsync(request, HttpContext.RequestAborted),
                result => Ok(result));
        }

        [HttpPost("statements/import")]
        public Task<ActionResult<TrustStatementImport>> ImportStatement([FromBody] TrustStatementImportRequest request)
        {
            return ExecuteAsync(
                () => _trustAccountingService.ImportStatementAsync(request, HttpContext.RequestAborted),
                statement => Ok(statement));
        }

        [HttpGet("statements/{id}/lines")]
        public Task<ActionResult<IReadOnlyList<TrustStatementLine>>> GetStatementLines(string id)
        {
            return ExecuteAsync(
                () => _trustAccountingService.GetStatementLinesAsync(id, HttpContext.RequestAborted),
                result => Ok(result));
        }

        [HttpPost("statements/{id}/match-run")]
        public Task<ActionResult<TrustStatementMatchingRunResultDto>> RunStatementMatch(string id)
        {
            return ExecuteAsync(
                () => _trustAccountingService.RunStatementMatchingAsync(id, HttpContext.RequestAborted),
                result => Ok(result));
        }

        [HttpPost("statement-lines/{id}/resolve")]
        public Task<ActionResult<TrustStatementLine>> ResolveStatementLine(string id, [FromBody] TrustStatementLineMatchDto dto)
        {
            return ExecuteAsync(
                () => _trustAccountingService.ResolveStatementLineAsync(id, dto, HttpContext.RequestAborted),
                result => Ok(result));
        }

        [HttpGet("outstanding-items")]
        public async Task<ActionResult<IEnumerable<TrustOutstandingItem>>> GetOutstandingItems([FromQuery] string? trustAccountId = null, [FromQuery] string? status = null)
        {
            var query = _context.TrustOutstandingItems.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(trustAccountId))
            {
                query = query.Where(i => i.TrustAccountId == trustAccountId);
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(i => i.Status == status);
            }

            return await query
                .OrderByDescending(i => i.PeriodEnd)
                .ThenByDescending(i => i.OccurredAt)
                .ToListAsync();
        }

        [HttpPost("outstanding-items")]
        public Task<ActionResult<TrustOutstandingItem>> CreateOutstandingItem([FromBody] TrustOutstandingItemCreateDto request)
        {
            return ExecuteAsync(
                () => _trustAccountingService.CreateOutstandingItemAsync(request, HttpContext.RequestAborted),
                item => Ok(item));
        }

        [HttpGet("reconciliation-packets")]
        public async Task<ActionResult<IEnumerable<TrustReconciliationPacket>>> GetReconciliationPackets([FromQuery] string? trustAccountId = null, [FromQuery] bool includeHistory = false)
        {
            var query = _context.TrustReconciliationPackets.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(trustAccountId))
            {
                query = query.Where(p => p.TrustAccountId == trustAccountId);
            }

            if (!includeHistory)
            {
                query = query.Where(p => p.IsCanonical);
            }

            return await query
                .OrderByDescending(p => p.PeriodEnd)
                .ThenByDescending(p => p.VersionNumber)
                .ThenByDescending(p => p.PreparedAt)
                .ToListAsync();
        }

        [HttpGet("reconciliation-packets/{id}")]
        public async Task<ActionResult<object>> GetReconciliationPacketDetail(string id)
        {
            var packet = await _context.TrustReconciliationPackets.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
            if (packet == null)
            {
                return Problem(statusCode: StatusCodes.Status404NotFound, title: "Reconciliation packet not found.", detail: $"Reconciliation packet '{id}' was not found.");
            }

            var signoffs = await _context.TrustReconciliationSignoffs.AsNoTracking()
                .Where(s => s.TrustReconciliationPacketId == id)
                .OrderByDescending(s => s.SignedAt)
                .ToListAsync();
            var outstandingItems = await _context.TrustOutstandingItems.AsNoTracking()
                .Where(i => i.TrustReconciliationPacketId == id)
                .OrderByDescending(i => i.OccurredAt)
                .ToListAsync();
            var statementImport = string.IsNullOrWhiteSpace(packet.StatementImportId)
                ? null
                : await _context.TrustStatementImports.AsNoTracking()
                    .FirstOrDefaultAsync(i => i.Id == packet.StatementImportId);
            var statementLines = string.IsNullOrWhiteSpace(packet.StatementImportId)
                ? new List<TrustStatementLine>()
                : await _context.TrustStatementLines.AsNoTracking()
                    .Where(l => l.TrustStatementImportId == packet.StatementImportId)
                    .OrderBy(l => l.PostedAt)
                    .ThenBy(l => l.Amount)
                    .ToListAsync();

            return Ok(new
            {
                packet,
                statementImport,
                signoffs,
                outstandingItems,
                statementLines
            });
        }

        [HttpPost("reconciliation-packets")]
        public Task<ActionResult<TrustReconciliationPacket>> CreateReconciliationPacket([FromBody] TrustReconciliationPacketCreateDto request)
        {
            return ExecuteAsync(
                () => _trustAccountingService.GenerateReconciliationPacketAsync(request, HttpContext.RequestAborted),
                packet => Ok(packet));
        }

        [HttpPost("reconciliation-packets/{id}/signoff")]
        public Task<ActionResult<TrustReconciliationPacket>> SignoffReconciliationPacket(string id, [FromBody] TrustReconciliationPacketSignoffDto? dto)
        {
            return ExecuteAsync(
                () => _trustAccountingService.SignoffReconciliationPacketAsync(id, dto, HttpContext.RequestAborted),
                packet => Ok(packet));
        }

        [HttpPost("reconciliation-packets/{id}/supersede")]
        public Task<ActionResult<TrustReconciliationPacket>> SupersedeReconciliationPacket(string id, [FromBody] TrustReconciliationPacketSupersedeDto dto)
        {
            return ExecuteAsync(
                () => _trustAccountingService.SupersedeReconciliationPacketAsync(id, dto, HttpContext.RequestAborted),
                packet => Ok(packet));
        }

        [HttpGet("month-close")]
        public Task<ActionResult<IReadOnlyList<TrustMonthCloseDto>>> GetMonthCloses([FromQuery] string? trustAccountId = null, [FromQuery] bool includeHistory = false)
        {
            return ExecuteAsync(
                () => _trustAccountingService.GetMonthClosesAsync(trustAccountId, includeHistory, HttpContext.RequestAborted),
                result => Ok(result));
        }

        [HttpPost("month-close/prepare")]
        public Task<ActionResult<TrustMonthCloseDto>> PrepareMonthClose([FromBody] TrustMonthClosePrepareDto dto)
        {
            return ExecuteAsync(
                () => _trustAccountingService.PrepareMonthCloseAsync(dto, HttpContext.RequestAborted),
                result => Ok(result));
        }

        [HttpPost("month-close/{id}/signoff")]
        public Task<ActionResult<TrustMonthCloseDto>> SignoffMonthClose(string id, [FromBody] TrustMonthCloseSignoffDto dto)
        {
            return ExecuteAsync(
                () => _trustAccountingService.SignoffMonthCloseAsync(id, dto, HttpContext.RequestAborted),
                result => Ok(result));
        }

        [HttpPost("month-close/{id}/reopen")]
        public Task<ActionResult<TrustMonthCloseDto>> ReopenMonthClose(string id, [FromBody] TrustMonthCloseReopenDto dto)
        {
            return ExecuteAsync(
                () => _trustAccountingService.ReopenMonthCloseAsync(id, dto, HttpContext.RequestAborted),
                result => Ok(result));
        }

        [HttpGet("packet-templates")]
        public Task<ActionResult<IReadOnlyList<TrustJurisdictionPacketTemplateUpsertDto>>> GetPacketTemplates([FromQuery] string? jurisdiction = null, [FromQuery] string? accountType = null, [FromQuery] string? policyKey = null)
        {
            return ExecuteAsync(
                () => _trustAccountingService.GetPacketTemplatesAsync(jurisdiction, accountType, policyKey, HttpContext.RequestAborted),
                result => Ok(result));
        }

        [HttpPost("packet-templates")]
        public Task<ActionResult<TrustJurisdictionPacketTemplateUpsertDto>> UpsertPacketTemplate([FromBody] TrustJurisdictionPacketTemplateUpsertDto dto)
        {
            return ExecuteAsync(
                () => _trustAccountingService.UpsertPacketTemplateAsync(dto, HttpContext.RequestAborted),
                result => Ok(result));
        }

        [HttpGet("exports")]
        public Task<ActionResult<IReadOnlyList<TrustComplianceExportListItemDto>>> GetExports([FromQuery] string? trustAccountId = null, [FromQuery] string? exportType = null)
        {
            return ExecuteAsync(
                () => _trustExportService.ListExportsAsync(trustAccountId, exportType, HttpContext.RequestAborted),
                result => Ok(result));
        }

        [HttpGet("exports/{id}")]
        public Task<ActionResult<TrustComplianceExportDto>> GetExport(string id)
        {
            return ExecuteAsync(
                () => _trustExportService.GetExportAsync(id, HttpContext.RequestAborted),
                result => Ok(result));
        }

        [HttpPost("exports")]
        public Task<ActionResult<TrustComplianceExportDto>> GenerateExport([FromBody] TrustComplianceExportRequest request)
        {
            return ExecuteAsync(
                () => _trustExportService.GenerateExportAsync(request, HttpContext.RequestAborted),
                result => Ok(result));
        }

        [HttpGet("projection-health")]
        public Task<ActionResult<TrustProjectionHealthResponse>> GetProjectionHealth([FromQuery] string? trustAccountId = null)
        {
            return ExecuteAsync(
                () => _trustAccountingService.GetProjectionHealthAsync(trustAccountId, HttpContext.RequestAborted),
                result => Ok(result));
        }

        [HttpPost("projection-rebuild")]
        public Task<ActionResult<TrustProjectionRebuildResult>> RebuildProjections([FromBody] TrustProjectionRebuildRequest? request)
        {
            return ExecuteAsync(
                () => _trustAccountingService.RebuildProjectionsAsync(request, HttpContext.RequestAborted),
                result => Ok(result));
        }

        [HttpPost("recovery/as-of-rebuild")]
        public Task<ActionResult<TrustAsOfProjectionRecoveryResult>> RunAsOfRecovery([FromBody] TrustAsOfProjectionRecoveryRequest? request)
        {
            return ExecuteAsync(
                () => _trustRecoveryService.GenerateAsOfProjectionRecoveryAsync(request, HttpContext.RequestAborted),
                result => Ok(result));
        }

        [HttpPost("recovery/packet-regeneration")]
        public Task<ActionResult<TrustPacketRegenerationResult>> RegeneratePacket([FromBody] TrustPacketRegenerationRequest request)
        {
            return ExecuteAsync(
                () => _trustRecoveryService.RegeneratePacketAsync(request, HttpContext.RequestAborted),
                result => Ok(result));
        }

        [HttpPost("recovery/compliance-bundle")]
        public Task<ActionResult<TrustComplianceBundleResult>> GenerateComplianceBundle([FromBody] TrustComplianceBundleRequest request)
        {
            return ExecuteAsync(
                () => _trustRecoveryService.GenerateComplianceBundleAsync(request, HttpContext.RequestAborted),
                result => Ok(result));
        }

        [HttpGet("recovery/compliance-bundle/{id}/integrity")]
        public Task<ActionResult<TrustBundleIntegrityDto>> GetComplianceBundleIntegrity(string id)
        {
            return ExecuteAsync(
                () => _trustBundleIntegrityService.GetBundleIntegrityAsync(id, HttpContext.RequestAborted),
                result => Ok(result));
        }

        [HttpPost("recovery/compliance-bundle/{id}/sign")]
        public Task<ActionResult<TrustBundleIntegrityDto>> SignComplianceBundle(string id, [FromBody] TrustBundleSignRequest? request)
        {
            return ExecuteAsync(
                () => _trustBundleIntegrityService.SignBundleAsync(id, request, HttpContext.RequestAborted),
                result => Ok(result));
        }

        [HttpGet("close-forecast")]
        public Task<ActionResult<TrustCloseForecastSummaryDto>> GetCloseForecast(
            [FromQuery] string? trustAccountId = null,
            [FromQuery] string? readinessStatus = null,
            [FromQuery] bool actionableOnly = false)
        {
            return ExecuteAsync(
                () => _trustCloseAutomationService.GetCloseForecastsAsync(trustAccountId, readinessStatus, actionableOnly, HttpContext.RequestAborted),
                result => Ok(result));
        }

        [HttpPost("close-forecast/sync")]
        public Task<ActionResult<TrustCloseForecastSyncResultDto>> SyncCloseForecast([FromQuery] bool generateDraftBundles = true)
        {
            return ExecuteAsync(
                () => _trustCloseAutomationService.SyncCloseForecastsAsync(generateDraftBundles, HttpContext.RequestAborted),
                result => Ok(result));
        }

        [HttpGet("compliance")]
        public async Task<ActionResult> GetCompliance([FromQuery] string trustAccountId, [FromQuery] decimal? bankStatementBalance = null)
        {
            if (string.IsNullOrWhiteSpace(trustAccountId))
            {
                return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Trust account id is required.", detail: "trustAccountId query parameter is required.");
            }

            var summary = await _trustComplianceService.EvaluateAsync(trustAccountId, bankStatementBalance);
            if (summary == null)
            {
                return Problem(statusCode: StatusCodes.Status404NotFound, title: "Trust account not found.", detail: $"Trust account '{trustAccountId}' was not found.");
            }

            return Ok(summary);
        }

        [HttpGet("operational-alerts")]
        public async Task<ActionResult<TrustOperationalAlertSummary>> GetOperationalAlerts([FromQuery] string? trustAccountId = null, [FromQuery] string? severity = null, [FromQuery] string? alertType = null)
        {
            var summary = await _trustComplianceService.GetOperationalAlertsAsync(trustAccountId, severity, alertType, ct: HttpContext.RequestAborted);
            return Ok(summary);
        }

        [HttpPost("operational-alerts/sync")]
        public Task<ActionResult<TrustOperationalAlertSyncResultDto>> SyncOperationalAlerts()
        {
            return ExecuteAsync(
                () => _trustComplianceService.SyncOperationalAlertsAsync(HttpContext.RequestAborted),
                result => Ok(result));
        }

        [HttpGet("operational-alert-records")]
        public Task<ActionResult<IReadOnlyList<TrustOperationalAlertRecordDto>>> GetOperationalAlertRecords(
            [FromQuery] string? trustAccountId = null,
            [FromQuery] string? workflowStatus = null,
            [FromQuery] string? severity = null,
            [FromQuery] string? alertType = null)
        {
            return ExecuteAsync(
                () => _trustComplianceService.GetOperationalAlertRecordsAsync(trustAccountId, workflowStatus, severity, alertType, HttpContext.RequestAborted),
                result => Ok(result));
        }

        [HttpGet("operational-alerts/{id}/history")]
        public Task<ActionResult<IReadOnlyList<TrustOperationalAlertEventDto>>> GetOperationalAlertHistory(string id)
        {
            return ExecuteAsync(
                () => _trustComplianceService.GetOperationalAlertHistoryAsync(id, HttpContext.RequestAborted),
                result => Ok(result));
        }

        [HttpPost("operational-alerts/{id}/ack")]
        public Task<ActionResult<TrustOperationalAlertRecordDto>> AcknowledgeOperationalAlert(string id, [FromBody] TrustOperationalAlertActionDto? dto)
        {
            return ExecuteAsync(
                () => _trustComplianceService.AcknowledgeOperationalAlertAsync(id, dto, HttpContext.RequestAborted),
                result => Ok(result));
        }

        [HttpPost("operational-alerts/{id}/assign")]
        public Task<ActionResult<TrustOperationalAlertRecordDto>> AssignOperationalAlert(string id, [FromBody] TrustOperationalAlertAssignDto dto)
        {
            return ExecuteAsync(
                () => _trustComplianceService.AssignOperationalAlertAsync(id, dto, HttpContext.RequestAborted),
                result => Ok(result));
        }

        [HttpPost("operational-alerts/{id}/escalate")]
        public Task<ActionResult<TrustOperationalAlertRecordDto>> EscalateOperationalAlert(string id, [FromBody] TrustOperationalAlertActionDto? dto)
        {
            return ExecuteAsync(
                () => _trustComplianceService.EscalateOperationalAlertAsync(id, dto, HttpContext.RequestAborted),
                result => Ok(result));
        }

        [HttpPost("operational-alerts/{id}/resolve")]
        public Task<ActionResult<TrustOperationalAlertRecordDto>> ResolveOperationalAlert(string id, [FromBody] TrustOperationalAlertActionDto? dto)
        {
            return ExecuteAsync(
                () => _trustComplianceService.ResolveOperationalAlertAsync(id, dto, HttpContext.RequestAborted),
                result => Ok(result));
        }

        [HttpPost("ops-inbox/sync")]
        public Task<ActionResult<TrustOpsInboxSummaryDto>> SyncOpsInbox()
        {
            return ExecuteAsync(
                () => _trustOpsInboxService.SyncInboxAsync(ct: HttpContext.RequestAborted),
                result => Ok(result));
        }

        [HttpGet("ops-inbox")]
        public Task<ActionResult<TrustOpsInboxSummaryDto>> GetOpsInbox(
            [FromQuery] string? assignedUserId = null,
            [FromQuery] string? officeId = null,
            [FromQuery] string? jurisdiction = null,
            [FromQuery] string? severity = null,
            [FromQuery] string? blockerGroup = null,
            [FromQuery] string? workflowStatus = null,
            [FromQuery] bool breachedOnly = false)
        {
            return ExecuteAsync(
                () => _trustOpsInboxService.GetInboxAsync(assignedUserId, officeId, jurisdiction, severity, blockerGroup, workflowStatus, breachedOnly, HttpContext.RequestAborted),
                result => Ok(result));
        }

        [HttpGet("ops-inbox/{id}/history")]
        public Task<ActionResult<IReadOnlyList<TrustOpsInboxEventDto>>> GetOpsInboxHistory(string id)
        {
            return ExecuteAsync(
                () => _trustOpsInboxService.GetInboxHistoryAsync(id, HttpContext.RequestAborted),
                result => Ok(result));
        }

        [HttpPost("ops-inbox/{id}/claim")]
        public Task<ActionResult<TrustOpsInboxItemDto>> ClaimOpsInboxItem(string id, [FromBody] TrustOperationalAlertActionDto? dto)
        {
            return ExecuteAsync(
                () => _trustOpsInboxService.ClaimAsync(id, dto?.Notes, HttpContext.RequestAborted),
                result => Ok(result));
        }

        [HttpPost("ops-inbox/{id}/assign")]
        public Task<ActionResult<TrustOpsInboxItemDto>> AssignOpsInboxItem(string id, [FromBody] TrustOpsInboxAssignDto dto)
        {
            return ExecuteAsync(
                () => _trustOpsInboxService.AssignAsync(id, dto, HttpContext.RequestAborted),
                result => Ok(result));
        }

        [HttpPost("ops-inbox/{id}/defer")]
        public Task<ActionResult<TrustOpsInboxItemDto>> DeferOpsInboxItem(string id, [FromBody] TrustOpsInboxDeferDto dto)
        {
            return ExecuteAsync(
                () => _trustOpsInboxService.DeferAsync(id, dto, HttpContext.RequestAborted),
                result => Ok(result));
        }

        [HttpPost("ops-inbox/{id}/escalate")]
        public Task<ActionResult<TrustOpsInboxItemDto>> EscalateOpsInboxItem(string id, [FromBody] TrustOperationalAlertActionDto? dto)
        {
            return ExecuteAsync(
                () => _trustOpsInboxService.EscalateAsync(id, dto?.Notes, HttpContext.RequestAborted),
                result => Ok(result));
        }

        [HttpPost("ops-inbox/{id}/resolve")]
        public Task<ActionResult<TrustOpsInboxItemDto>> ResolveOpsInboxItem(string id, [FromBody] TrustOperationalAlertActionDto? dto)
        {
            return ExecuteAsync(
                () => _trustOpsInboxService.ResolveAsync(id, dto?.Notes, HttpContext.RequestAborted),
                result => Ok(result));
        }

        [HttpPost("reconcile")]
        public Task<ActionResult<ReconciliationRecord>> Reconcile([FromBody] ReconcileRequest request)
        {
            return ExecuteAsync(
                () => _trustAccountingService.ReconcileAsync(request, HttpContext.RequestAborted),
                rec => Ok(rec));
        }

        private async Task<ActionResult<T>> ExecuteAsync<T>(Func<Task<T>> action, Func<T, ActionResult<T>> onSuccess)
        {
            try
            {
                var result = await action();
                return onSuccess(result);
            }
            catch (TrustCommandException ex)
            {
                return StatusCode(ex.StatusCode, new ProblemDetails
                {
                    Status = ex.StatusCode,
                    Title = "Trust command failed.",
                    Detail = ex.Message
                });
            }
        }

        private static int NormalizePage(int page) => page <= 0 ? 1 : page;

        private static int NormalizePageSize(int pageSize)
        {
            if (pageSize <= 0)
            {
                return DefaultTrustPageSize;
            }

            return Math.Clamp(pageSize, 1, MaxTrustPageSize);
        }

        private void AddPaginationHeaders(int totalCount, int page, int pageSize)
        {
            Response.Headers["X-Total-Count"] = totalCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Response.Headers["X-Page"] = page.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Response.Headers["X-Page-Size"] = pageSize.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private string? ResolveIdempotencyKey(string? requestValue = null)
        {
            if (!string.IsNullOrWhiteSpace(requestValue))
            {
                return requestValue;
            }

            return Request.Headers["Idempotency-Key"].FirstOrDefault();
        }

        private async Task SyncBillingAllocationAsync(TrustTransaction tx, string? reason)
        {
            if (!string.Equals(tx.Type, "EARNED_FEE_TRANSFER", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            await _billingEngine.SyncTrustAllocationAsync(
                tx.Id,
                tx.Status,
                reason,
                HttpContext.User?.Identity?.Name,
                HttpContext.RequestAborted);
        }
    }
}
