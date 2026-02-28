using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using JurisFlow.Server.Services;
using Task = System.Threading.Tasks.Task;

namespace JurisFlow.Server.Controllers
{
    [Route("api/expenses")]
    [ApiController]
    [Authorize(Policy = "StaffOnly")]
    public class ExpensesController : ControllerBase
    {
        private readonly JurisFlowDbContext _context;
        private readonly AuditLogger _auditLogger;
        private readonly ILogger<ExpensesController> _logger;
        private readonly OutcomeFeePlannerService _outcomeFeePlanner;

        public ExpensesController(JurisFlowDbContext context, AuditLogger auditLogger, ILogger<ExpensesController> logger, OutcomeFeePlannerService outcomeFeePlanner)
        {
            _context = context;
            _auditLogger = auditLogger;
            _logger = logger;
            _outcomeFeePlanner = outcomeFeePlanner;
        }

        [HttpGet]
        public async Task<IActionResult> GetExpenses([FromQuery] string? matterId = null, [FromQuery] string? approvalStatus = null)
        {
            var query = _context.Expenses.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(matterId))
            {
                query = query.Where(e => e.MatterId == matterId);
            }

            if (!string.IsNullOrWhiteSpace(approvalStatus))
            {
                query = query.Where(e => e.ApprovalStatus == approvalStatus);
            }

            var items = await query.OrderByDescending(e => e.Date).ToListAsync();
            return Ok(items);
        }

        [HttpPost]
        public async Task<IActionResult> CreateExpense([FromBody] ExpenseCreateDto dto)
        {
            if (dto.Amount <= 0)
            {
                return BadRequest(new { message = "Amount must be greater than zero." });
            }

            if (!string.IsNullOrWhiteSpace(dto.MatterId))
            {
                var exists = await _context.Matters.AnyAsync(m => m.Id == dto.MatterId);
                if (!exists)
                {
                    return BadRequest(new { message = "Selected matter was not found." });
                }
            }

            var userId = GetUserId();
            var isApprover = IsApprover();

            var expense = new Expense
            {
                MatterId = dto.MatterId,
                Description = dto.Description ?? string.Empty,
                Amount = dto.Amount,
                Date = dto.Date ?? DateTime.UtcNow,
                Category = dto.Category ?? "Other",
                Billed = dto.Billed,
                Type = "expense",
                ExpenseCode = NormalizeUtbmsCode(dto.ExpenseCode),
                ApprovalStatus = isApprover ? "Approved" : "Pending",
                SubmittedBy = userId,
                SubmittedAt = DateTime.UtcNow,
                ApprovedBy = isApprover ? userId : null,
                ApprovedAt = isApprover ? DateTime.UtcNow : null,
                UpdatedAt = DateTime.UtcNow
            };

            try
            {
                _context.Expenses.Add(expense);
                await _context.SaveChangesAsync();
                await _auditLogger.LogAsync(HttpContext, "expense.create", "Expense", expense.Id, $"MatterId={expense.MatterId}, Amount={expense.Amount}");
                await TryTriggerOutcomeFeePlannerAsync(expense.MatterId, "expense_create", expense.Id);
                return Ok(expense);
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Failed to create expense.");
                return StatusCode(500, new { message = "Failed to create expense. Please verify the matter selection." });
            }
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateExpense(string id, [FromBody] ExpenseUpdateDto dto)
        {
            var expense = await _context.Expenses.FindAsync(id);
            if (expense == null) return NotFound();

            if (expense.ApprovalStatus == "Approved")
            {
                return BadRequest(new { message = "Approved expenses cannot be edited." });
            }

            if (dto.Description != null) expense.Description = dto.Description;
            if (dto.Amount.HasValue) expense.Amount = dto.Amount.Value;
            if (dto.Date.HasValue) expense.Date = dto.Date.Value;
            if (dto.Category != null) expense.Category = dto.Category;
            if (dto.ExpenseCode != null) expense.ExpenseCode = NormalizeUtbmsCode(dto.ExpenseCode);
            expense.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await _auditLogger.LogAsync(HttpContext, "expense.update", "Expense", expense.Id, "Expense updated");
            await TryTriggerOutcomeFeePlannerAsync(expense.MatterId, "expense_update", expense.Id);

            return Ok(expense);
        }

        [HttpPost("{id}/approve")]
        public async Task<IActionResult> ApproveExpense(string id)
        {
            if (!IsApprover()) return Forbid();

            var expense = await _context.Expenses.FindAsync(id);
            if (expense == null) return NotFound();

            expense.ApprovalStatus = "Approved";
            expense.ApprovedBy = GetUserId();
            expense.ApprovedAt = DateTime.UtcNow;
            expense.RejectedBy = null;
            expense.RejectedAt = null;
            expense.RejectionReason = null;
            expense.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await _auditLogger.LogAsync(HttpContext, "expense.approve", "Expense", expense.Id, "Expense approved");
            await TryTriggerOutcomeFeePlannerAsync(expense.MatterId, "expense_approve", expense.Id);

            return Ok(expense);
        }

        [HttpPost("{id}/reject")]
        public async Task<IActionResult> RejectExpense(string id, [FromBody] ApprovalRejectDto dto)
        {
            if (!IsApprover()) return Forbid();

            var expense = await _context.Expenses.FindAsync(id);
            if (expense == null) return NotFound();

            expense.ApprovalStatus = "Rejected";
            expense.RejectedBy = GetUserId();
            expense.RejectedAt = DateTime.UtcNow;
            expense.RejectionReason = dto.Reason;
            expense.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await _auditLogger.LogAsync(HttpContext, "expense.reject", "Expense", expense.Id, $"Reason={dto.Reason}");

            return Ok(expense);
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

        private string? NormalizeUtbmsCode(string? code)
        {
            if (string.IsNullOrWhiteSpace(code)) return null;
            var trimmed = code.Trim();
            var split = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return split.Length > 0 ? split[0].Trim() : trimmed;
        }

        private async Task TryTriggerOutcomeFeePlannerAsync(string? matterId, string triggerType, string entityId)
        {
            if (string.IsNullOrWhiteSpace(matterId)) return;

            try
            {
                await _outcomeFeePlanner.TryProcessTriggerAsync(new OutcomeFeePlanTriggerRequest
                {
                    MatterId = matterId,
                    TriggerType = triggerType,
                    TriggerEntityType = nameof(Expense),
                    TriggerEntityId = entityId
                }, GetUserId() ?? "system", HttpContext.RequestAborted);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Outcome-to-Fee planner trigger failed for expense {ExpenseId}", entityId);
            }
        }
    }

    public class ExpenseCreateDto
    {
        public string? MatterId { get; set; }
        public string? Description { get; set; }
        public double Amount { get; set; }
        public DateTime? Date { get; set; }
        public string? Category { get; set; }
        public bool Billed { get; set; }
        public string? ExpenseCode { get; set; }
    }

    public class ExpenseUpdateDto
    {
        public string? Description { get; set; }
        public double? Amount { get; set; }
        public DateTime? Date { get; set; }
        public string? Category { get; set; }
        public string? ExpenseCode { get; set; }
    }
}
