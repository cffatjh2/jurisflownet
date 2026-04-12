using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using JurisFlow.Server.Services;
using Task = System.Threading.Tasks.Task;

namespace JurisFlow.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Policy = "StaffOnly")]
    public class TrustController : ControllerBase
    {
        private readonly JurisFlowDbContext _context;
        private readonly AuditLogger _auditLogger;
        private readonly BillingPeriodLockService _billingPeriodLockService;
        private readonly FirmStructureService _firmStructure;
        private readonly TrustComplianceService _trustComplianceService;

        public TrustController(
            JurisFlowDbContext context,
            AuditLogger auditLogger,
            BillingPeriodLockService billingPeriodLockService,
            FirmStructureService firmStructure,
            TrustComplianceService trustComplianceService)
        {
            _context = context;
            _auditLogger = auditLogger;
            _billingPeriodLockService = billingPeriodLockService;
            _firmStructure = firmStructure;
            _trustComplianceService = trustComplianceService;
        }

        private string? GetUserId()
        {
            return User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
        }

        private bool IsApprover()
        {
            var role = User.FindFirst(ClaimTypes.Role)?.Value ?? User.FindFirst("role")?.Value ?? string.Empty;
            return role.Equals("Admin", StringComparison.OrdinalIgnoreCase)
                || role.Equals("Partner", StringComparison.OrdinalIgnoreCase)
                || role.Equals("Associate", StringComparison.OrdinalIgnoreCase)
                || role.Equals("Accountant", StringComparison.OrdinalIgnoreCase);
        }

        private List<AllocationDto> ParseAllocations(string? allocationsJson)
        {
            if (string.IsNullOrWhiteSpace(allocationsJson)) return new List<AllocationDto>();
            try
            {
                return JsonSerializer.Deserialize<List<AllocationDto>>(allocationsJson) ?? new List<AllocationDto>();
            }
            catch
            {
                return new List<AllocationDto>();
            }
        }

        private async Task<bool> ApplyDepositAsync(TrustTransaction tx, TrustBankAccount account, List<AllocationDto> allocations)
        {
            if (tx.Amount <= 0) return false;
            if (account.Status != TrustAccountStatus.ACTIVE) return false;

            var balanceBefore = account.CurrentBalance;
            account.CurrentBalance += tx.Amount;
            account.UpdatedAt = DateTime.UtcNow;

            if (allocations.Count > 0)
            {
                var ledgerIds = allocations.Select(a => a.LedgerId).Distinct().ToList();
                var ledgers = await _context.ClientTrustLedgers
                    .Where(l => ledgerIds.Contains(l.Id))
                    .ToListAsync();
                if (ledgers.Count != ledgerIds.Count) return false;
                if (ledgers.Any(l => l.TrustAccountId != account.Id)) return false;
                if (ledgers.Any(l => l.Status != LedgerStatus.ACTIVE)) return false;

                foreach (var allocation in allocations)
                {
                    if (allocation.Amount <= 0) return false;
                    var ledger = ledgers.First(l => l.Id == allocation.LedgerId);
                    ledger.RunningBalance += allocation.Amount;
                    ledger.UpdatedAt = DateTime.UtcNow;
                }
            }

            tx.BalanceBefore = balanceBefore;
            tx.BalanceAfter = account.CurrentBalance;
            tx.UpdatedAt = DateTime.UtcNow;
            return true;
        }

        private async Task<bool> ApplyWithdrawalAsync(TrustTransaction tx, TrustBankAccount account, ClientTrustLedger ledger)
        {
            if (tx.Amount <= 0) return false;
            if (account.Status != TrustAccountStatus.ACTIVE) return false;
            if (ledger.Status != LedgerStatus.ACTIVE) return false;
            if (ledger.TrustAccountId != account.Id) return false;
            if (ledger.RunningBalance < tx.Amount) return false;
            if (account.CurrentBalance < tx.Amount) return false;

            var balanceBefore = account.CurrentBalance;
            account.CurrentBalance -= tx.Amount;
            ledger.RunningBalance -= tx.Amount;
            account.UpdatedAt = DateTime.UtcNow;
            ledger.UpdatedAt = DateTime.UtcNow;

            tx.BalanceBefore = balanceBefore;
            tx.BalanceAfter = account.CurrentBalance;
            tx.UpdatedAt = DateTime.UtcNow;
            return true;
        }

        private async Task<bool> ReverseDepositAsync(TrustTransaction tx, TrustBankAccount account, List<AllocationDto> allocations)
        {
            if (tx.Amount <= 0) return false;
            if (account.CurrentBalance < tx.Amount) return false;

            if (allocations.Count > 0)
            {
                var ledgerIds = allocations.Select(a => a.LedgerId).Distinct().ToList();
                var ledgers = await _context.ClientTrustLedgers
                    .Where(l => ledgerIds.Contains(l.Id))
                    .ToListAsync();
                if (ledgers.Count != ledgerIds.Count) return false;
                if (ledgers.Any(l => l.TrustAccountId != account.Id)) return false;
                foreach (var allocation in allocations)
                {
                    var ledger = ledgers.First(l => l.Id == allocation.LedgerId);
                    if (ledger.RunningBalance < allocation.Amount) return false;
                }
                foreach (var allocation in allocations)
                {
                    var ledger = ledgers.First(l => l.Id == allocation.LedgerId);
                    ledger.RunningBalance -= allocation.Amount;
                    ledger.UpdatedAt = DateTime.UtcNow;
                }
            }

            account.CurrentBalance -= tx.Amount;
            account.UpdatedAt = DateTime.UtcNow;
            return true;
        }

        private async Task<bool> ReverseWithdrawalAsync(TrustTransaction tx, TrustBankAccount account, ClientTrustLedger ledger)
        {
            if (tx.Amount <= 0) return false;
            if (ledger.TrustAccountId != account.Id) return false;

            ledger.RunningBalance += tx.Amount;
            account.CurrentBalance += tx.Amount;
            ledger.UpdatedAt = DateTime.UtcNow;
            account.UpdatedAt = DateTime.UtcNow;
            return true;
        }

        // --- ACCOUNTS ---

        [HttpGet("accounts")]
        public async Task<ActionResult<IEnumerable<TrustBankAccount>>> GetTrustAccounts([FromQuery] string? entityId, [FromQuery] string? officeId)
        {
            var query = _context.TrustBankAccounts.AsQueryable();
            if (!string.IsNullOrWhiteSpace(entityId))
            {
                query = query.Where(a => a.EntityId == entityId);
            }
            if (!string.IsNullOrWhiteSpace(officeId))
            {
                query = query.Where(a => a.OfficeId == officeId);
            }
            return await query.ToListAsync();
        }

        [HttpPost("accounts")]
        public async Task<ActionResult<TrustBankAccount>> CreateTrustAccount(TrustBankAccount account)
        {
            var resolved = await _firmStructure.ResolveEntityOfficeAsync(account.EntityId, account.OfficeId);
            account.EntityId = resolved.entityId;
            account.OfficeId = resolved.officeId;
            account.Id = Guid.NewGuid().ToString();
            account.CreatedAt = DateTime.UtcNow;
            account.UpdatedAt = DateTime.UtcNow;
            _context.TrustBankAccounts.Add(account);
            await _context.SaveChangesAsync();
            await _auditLogger.LogAsync(HttpContext, "trust.account.create", "TrustBankAccount", account.Id, $"Name={account.Name}, Balance={account.CurrentBalance}");
            return CreatedAtAction(nameof(GetTrustAccounts), new { id = account.Id }, account);
        }

        // --- LEDGERS ---

        [HttpGet("ledgers")]
        public async Task<ActionResult<IEnumerable<ClientTrustLedger>>> GetLedgers([FromQuery] string? entityId, [FromQuery] string? officeId)
        {
            var query = _context.ClientTrustLedgers.AsQueryable();
            if (!string.IsNullOrWhiteSpace(entityId))
            {
                query = query.Where(l => l.EntityId == entityId);
            }
            if (!string.IsNullOrWhiteSpace(officeId))
            {
                query = query.Where(l => l.OfficeId == officeId);
            }

            return await query
                .Include(l => l.Client)
                .Include(l => l.TrustAccount)
                .ToListAsync();
        }

        [HttpPost("ledgers")]
        public async Task<ActionResult<ClientTrustLedger>> CreateLedger(ClientTrustLedger ledger)
        {
            var account = await _context.TrustBankAccounts.FindAsync(ledger.TrustAccountId);
            if (account != null)
            {
                ledger.EntityId = account.EntityId;
                ledger.OfficeId = account.OfficeId;
            }
            ledger.Id = Guid.NewGuid().ToString();
            ledger.CreatedAt = DateTime.UtcNow;
            ledger.UpdatedAt = DateTime.UtcNow;
            _context.ClientTrustLedgers.Add(ledger);
            await _context.SaveChangesAsync();
            await _auditLogger.LogAsync(HttpContext, "trust.ledger.create", "ClientTrustLedger", ledger.Id, $"ClientId={ledger.ClientId}, Account={ledger.TrustAccountId}");
            return CreatedAtAction(nameof(GetLedgers), new { id = ledger.Id }, ledger);
        }

        // --- TRANSACTIONS ---

        [HttpGet("transactions")]
        public async Task<ActionResult<IEnumerable<TrustTransaction>>> GetTransactions([FromQuery] int limit = 50, [FromQuery] string? entityId = null, [FromQuery] string? officeId = null)
        {
            var query = _context.TrustTransactions.AsQueryable();
            if (!string.IsNullOrWhiteSpace(entityId))
            {
                query = query.Where(t => t.EntityId == entityId);
            }
            if (!string.IsNullOrWhiteSpace(officeId))
            {
                query = query.Where(t => t.OfficeId == officeId);
            }

            return await query
                .Include(t => t.Matter)
                .OrderByDescending(t => t.CreatedAt)
                .Take(limit)
                .ToListAsync();
        }

        [HttpPost("deposit")]
        public async Task<ActionResult<TrustTransaction>> Deposit(DepositRequest request)
        {
            var account = await _context.TrustBankAccounts.FindAsync(request.TrustAccountId);
            if (account == null) return NotFound("Trust account not found");
            if (account.Status != TrustAccountStatus.ACTIVE)
            {
                return BadRequest("Trust account is not active.");
            }

            if (await _billingPeriodLockService.IsLockedAsync(DateTime.UtcNow, HttpContext.RequestAborted))
            {
                return BadRequest("Billing period is locked. Cannot post deposit.");
            }

            if (request.Amount <= 0) return BadRequest("Deposit amount must be positive");

            var allocations = request.Allocations ?? new List<AllocationDto>();
            if (allocations.Count == 0)
            {
                return BadRequest("At least one allocation is required.");
            }

            if (allocations.Any(a => a.Amount <= 0))
            {
                return BadRequest("Allocation amounts must be positive.");
            }

            var totalAllocations = allocations.Sum(a => a.Amount);
            if (Math.Abs(totalAllocations - request.Amount) > 0.01)
            {
                return BadRequest("Allocation total must match deposit amount.");
            }

            var ledgerIds = allocations.Select(a => a.LedgerId).Distinct().ToList();
            var ledgers = await _context.ClientTrustLedgers
                .Where(l => ledgerIds.Contains(l.Id))
                .ToListAsync();
            if (ledgers.Count != ledgerIds.Count)
            {
                return BadRequest("One or more client ledgers were not found.");
            }

            if (ledgers.Any(l => l.TrustAccountId != request.TrustAccountId))
            {
                return BadRequest("Ledger does not belong to the selected trust account.");
            }

            if (ledgers.Any(l => l.Status != LedgerStatus.ACTIVE))
            {
                return BadRequest("One or more ledgers are not active.");
            }

            var matterId = ledgers.FirstOrDefault()?.MatterId;
            if (string.IsNullOrWhiteSpace(matterId) || ledgers.Any(l => l.MatterId != matterId))
            {
                matterId = null;
            }

            var userId = GetUserId();
            var isApprover = IsApprover();

            var tx = new TrustTransaction
            {
                Id = Guid.NewGuid().ToString(),
                TrustAccountId = request.TrustAccountId,
                MatterId = matterId,
                LedgerId = allocations.Count == 1 ? allocations[0].LedgerId : null,
                EntityId = account.EntityId,
                OfficeId = account.OfficeId,
                Type = "DEPOSIT",
                Amount = request.Amount,
                Description = request.Description,
                Reference = request.CheckNumber,
                PayorPayee = request.PayorPayee,
                CheckNumber = request.CheckNumber,
                AllocationsJson = JsonSerializer.Serialize(allocations),
                Status = isApprover ? "APPROVED" : "PENDING",
                CreatedBy = userId,
                ApprovedBy = isApprover ? userId : null,
                ApprovedAt = isApprover ? DateTime.UtcNow : null,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            if (isApprover)
            {
                var applied = await ApplyDepositAsync(tx, account, allocations);
                if (!applied)
                {
                    return BadRequest("Failed to apply deposit.");
                }
            }

            _context.TrustTransactions.Add(tx);
            await _context.SaveChangesAsync();
            await _auditLogger.LogAsync(HttpContext, "trust.deposit", "TrustTransaction", tx.Id, $"Amount={request.Amount}, Account={account.Id}, Status={tx.Status}");
            return Ok(tx);
        }

        [HttpPost("withdrawal")]
        public async Task<ActionResult<TrustTransaction>> Withdrawal(WithdrawalRequest request)
        {
            var account = await _context.TrustBankAccounts.FindAsync(request.TrustAccountId);
            if (account == null) return NotFound("Trust account not found");
            if (account.Status != TrustAccountStatus.ACTIVE)
            {
                return BadRequest("Trust account is not active.");
            }
            
            if (await _billingPeriodLockService.IsLockedAsync(DateTime.UtcNow, HttpContext.RequestAborted))
            {
                return BadRequest("Billing period is locked. Cannot post withdrawal.");
            }

            if (request.Amount <= 0) return BadRequest("Withdrawal amount must be positive");

            var ledger = await _context.ClientTrustLedgers.FindAsync(request.LedgerId);
            if (ledger == null) return BadRequest("Client ledger not found");
            if (ledger.TrustAccountId != request.TrustAccountId)
            {
                return BadRequest("Ledger does not belong to the selected trust account.");
            }
            if (ledger.Status != LedgerStatus.ACTIVE)
            {
                return BadRequest("Ledger is not active.");
            }

            var userId = GetUserId();
            var isApprover = IsApprover();

            var tx = new TrustTransaction
            {
                Id = Guid.NewGuid().ToString(),
                TrustAccountId = request.TrustAccountId,
                LedgerId = request.LedgerId,
                MatterId = ledger.MatterId,
                EntityId = account.EntityId,
                OfficeId = account.OfficeId,
                Type = "WITHDRAWAL",
                Amount = request.Amount,
                Description = request.Description,
                Reference = request.CheckNumber,
                PayorPayee = request.PayorPayee,
                CheckNumber = request.CheckNumber,
                Status = isApprover ? "APPROVED" : "PENDING",
                CreatedBy = userId,
                ApprovedBy = isApprover ? userId : null,
                ApprovedAt = isApprover ? DateTime.UtcNow : null,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            if (isApprover)
            {
                var applied = await ApplyWithdrawalAsync(tx, account, ledger);
                if (!applied)
                {
                    return BadRequest("Failed to apply withdrawal.");
                }
            }

            _context.TrustTransactions.Add(tx);
            await _context.SaveChangesAsync();
            await _auditLogger.LogAsync(HttpContext, "trust.withdrawal", "TrustTransaction", tx.Id, $"Amount={request.Amount}, Account={account.Id}, Status={tx.Status}");
            return Ok(tx);
        }

        [HttpPost("transactions/{id}/approve")]
        public async Task<IActionResult> ApproveTransaction(string id)
        {
            if (!IsApprover()) return Forbid();

            if (await _billingPeriodLockService.IsLockedAsync(DateTime.UtcNow, HttpContext.RequestAborted))
            {
                return BadRequest("Billing period is locked. Cannot approve transaction.");
            }

            var tx = await _context.TrustTransactions.FirstOrDefaultAsync(t => t.Id == id);
            if (tx == null) return NotFound("Transaction not found");
            if (tx.Status == "APPROVED") return BadRequest("Transaction already approved.");
            if (tx.Status == "VOIDED") return BadRequest("Voided transactions cannot be approved.");
            if (tx.Status == "REJECTED") return BadRequest("Rejected transactions cannot be approved.");

            var account = await _context.TrustBankAccounts.FindAsync(tx.TrustAccountId);
            if (account == null) return NotFound("Trust account not found");

            var applied = false;
            if (tx.Type == "DEPOSIT")
            {
                var allocations = ParseAllocations(tx.AllocationsJson);
                applied = await ApplyDepositAsync(tx, account, allocations);
            }
            else if (tx.Type == "WITHDRAWAL")
            {
                if (string.IsNullOrWhiteSpace(tx.LedgerId))
                {
                    return BadRequest("Ledger is required for withdrawals.");
                }
                var ledger = await _context.ClientTrustLedgers.FindAsync(tx.LedgerId);
                if (ledger == null) return BadRequest("Client ledger not found");
                applied = await ApplyWithdrawalAsync(tx, account, ledger);
            }
            else
            {
                return BadRequest("Unsupported transaction type.");
            }

            if (!applied)
            {
                return BadRequest("Failed to apply transaction.");
            }

            tx.Status = "APPROVED";
            tx.ApprovedBy = GetUserId();
            tx.ApprovedAt = DateTime.UtcNow;
            tx.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await _auditLogger.LogAsync(HttpContext, "trust.transaction.approve", "TrustTransaction", tx.Id, $"Amount={tx.Amount}, Account={tx.TrustAccountId}");

            return Ok(tx);
        }

        [HttpPost("transactions/{id}/reject")]
        public async Task<IActionResult> RejectTransaction(string id, [FromBody] TrustRejectDto dto)
        {
            if (!IsApprover()) return Forbid();

            var tx = await _context.TrustTransactions.FirstOrDefaultAsync(t => t.Id == id);
            if (tx == null) return NotFound("Transaction not found");
            if (tx.Status == "APPROVED") return BadRequest("Approved transactions cannot be rejected.");
            if (tx.Status == "VOIDED") return BadRequest("Voided transactions cannot be rejected.");
            if (tx.Status == "REJECTED") return BadRequest("Transaction already rejected.");

            tx.Status = "REJECTED";
            tx.RejectedBy = GetUserId();
            tx.RejectedAt = DateTime.UtcNow;
            tx.RejectionReason = dto?.Reason;
            tx.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await _auditLogger.LogAsync(HttpContext, "trust.transaction.reject", "TrustTransaction", tx.Id, $"Reason={dto?.Reason}");

            return Ok(tx);
        }

        [HttpPost("transactions/{id}/void")]
        public async Task<IActionResult> VoidTransaction(string id, [FromBody] TrustVoidDto dto)
        {
            if (!IsApprover()) return Forbid();

            if (await _billingPeriodLockService.IsLockedAsync(DateTime.UtcNow, HttpContext.RequestAborted))
            {
                return BadRequest("Billing period is locked. Cannot void transaction.");
            }

            var tx = await _context.TrustTransactions.FirstOrDefaultAsync(t => t.Id == id);
            if (tx == null) return NotFound("Transaction not found");
            if (tx.Status != "APPROVED") return BadRequest("Only approved transactions can be voided.");
            if (tx.IsVoided || tx.Status == "VOIDED") return BadRequest("Transaction already voided.");

            var account = await _context.TrustBankAccounts.FindAsync(tx.TrustAccountId);
            if (account == null) return NotFound("Trust account not found");

            var reversed = false;
            if (tx.Type == "DEPOSIT")
            {
                var allocations = ParseAllocations(tx.AllocationsJson);
                reversed = await ReverseDepositAsync(tx, account, allocations);
            }
            else if (tx.Type == "WITHDRAWAL")
            {
                if (string.IsNullOrWhiteSpace(tx.LedgerId))
                {
                    return BadRequest("Ledger is required for withdrawals.");
                }
                var ledger = await _context.ClientTrustLedgers.FindAsync(tx.LedgerId);
                if (ledger == null) return BadRequest("Client ledger not found");
                reversed = await ReverseWithdrawalAsync(tx, account, ledger);
            }
            else
            {
                return BadRequest("Unsupported transaction type.");
            }

            if (!reversed)
            {
                return BadRequest("Failed to void transaction.");
            }

            tx.IsVoided = true;
            tx.Status = "VOIDED";
            tx.VoidReason = dto?.Reason;
            tx.VoidedAt = DateTime.UtcNow;
            tx.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await _auditLogger.LogAsync(HttpContext, "trust.transaction.void", "TrustTransaction", tx.Id, $"Reason={dto?.Reason}");

            return Ok(tx);
        }

        // --- RECONCILIATIONS ---

        [HttpGet("reconciliations")]
        public async Task<ActionResult<IEnumerable<ReconciliationRecord>>> GetReconciliations()
        {
            return await _context.ReconciliationRecords.Include(r => r.TrustAccount).OrderByDescending(r => r.PeriodEnd).ToListAsync();
        }

        [HttpGet("compliance")]
        public async Task<ActionResult> GetCompliance([FromQuery] string trustAccountId, [FromQuery] double? bankStatementBalance = null)
        {
            if (string.IsNullOrWhiteSpace(trustAccountId))
            {
                return BadRequest("Trust account id is required.");
            }

            var summary = await _trustComplianceService.EvaluateAsync(trustAccountId, bankStatementBalance);
            if (summary == null) return NotFound("Trust account not found");
            return Ok(summary);
        }

        [HttpPost("reconcile")]
        public async Task<ActionResult<ReconciliationRecord>> Reconcile(ReconcileRequest request)
        {
            var account = await _context.TrustBankAccounts.FindAsync(request.TrustAccountId);
            if (account == null) return NotFound("Trust account not found");

            if (await _billingPeriodLockService.IsLockedAsync(DateTime.UtcNow, HttpContext.RequestAborted))
            {
                return BadRequest("Billing period is locked. Cannot reconcile in locked period.");
            }

            var clientLedgerSum = await _context.ClientTrustLedgers
                .Where(l => l.TrustAccountId == request.TrustAccountId)
                .SumAsync(l => l.RunningBalance);

            var discrepancy = Math.Abs(account.CurrentBalance - request.BankStatementBalance);
            bool isReconciled = discrepancy < 0.01 && Math.Abs(clientLedgerSum - account.CurrentBalance) < 0.01;

            var rec = new ReconciliationRecord
            {
                Id = Guid.NewGuid().ToString(),
                TrustAccountId = request.TrustAccountId,
                PeriodEnd = DateTime.Parse(request.PeriodEnd),
                BankStatementBalance = request.BankStatementBalance,
                TrustLedgerBalance = account.CurrentBalance,
                ClientLedgerSumBalance = clientLedgerSum,
                IsReconciled = isReconciled,
                DiscrepancyAmount = discrepancy,
                Notes = request.Notes,
                CreatedAt = DateTime.UtcNow
            };

            _context.ReconciliationRecords.Add(rec);
            await _context.SaveChangesAsync();

            await _auditLogger.LogAsync(HttpContext, "trust.reconcile", "ReconciliationRecord", rec.Id, $"IsReconciled={isReconciled}, Discrepancy={discrepancy}");

            return Ok(rec);
        }
    }

    // DTOs
    public class DepositRequest
    {
        public string TrustAccountId { get; set; }
        public double Amount { get; set; }
        public string Description { get; set; }
        public string PayorPayee { get; set; }
        public string? CheckNumber { get; set; }
        public List<AllocationDto> Allocations { get; set; }
    }

    public class AllocationDto
    {
        public string LedgerId { get; set; }
        public double Amount { get; set; }
        public string? Description { get; set; }
    }

    public class WithdrawalRequest
    {
        public string TrustAccountId { get; set; }
        public string LedgerId { get; set; }
        public double Amount { get; set; }
        public string Description { get; set; }
        public string PayorPayee { get; set; }
        public string? CheckNumber { get; set; }
    }

    public class ReconcileRequest
    {
        public string TrustAccountId { get; set; }
        public string PeriodEnd { get; set; }
        public double BankStatementBalance { get; set; }
        public string Notes { get; set; }
    }

    public class TrustRejectDto
    {
        public string? Reason { get; set; }
    }

    public class TrustVoidDto
    {
        public string? Reason { get; set; }
    }
}
