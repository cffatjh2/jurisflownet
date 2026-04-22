using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using JurisFlow.Server.Data;
using JurisFlow.Server.DTOs;
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
        private const int DefaultPageSize = 100;
        private const int MaxPageSize = 100;
        private readonly JurisFlowDbContext _context;
        private readonly AuditLogger _auditLogger;
        private readonly ILogger<ExpensesController> _logger;
        private readonly OutcomeFeePlannerTriggerQueue _plannerTriggerQueue;
        private readonly MatterAccessService _matterAccess;

        public ExpensesController(JurisFlowDbContext context, AuditLogger auditLogger, ILogger<ExpensesController> logger, OutcomeFeePlannerTriggerQueue plannerTriggerQueue, MatterAccessService matterAccess)
        {
            _context = context;
            _auditLogger = auditLogger;
            _logger = logger;
            _plannerTriggerQueue = plannerTriggerQueue;
            _matterAccess = matterAccess;
        }

        [HttpGet]
        public async Task<IActionResult> GetExpenses(
            [FromQuery] string? matterId = null,
            [FromQuery] string? approvalStatus = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = DefaultPageSize)
        {
            var query = _context.Expenses.AsNoTracking().AsQueryable();
            var currentUserId = _matterAccess.GetCurrentUserId(User);
            var isPrivileged = _matterAccess.IsPrivileged(User);
            var readableMatterIds = _matterAccess.BuildReadableMatterIdsQuery(User);
            var normalizedPage = Math.Max(1, page);
            var normalizedPageSize = NormalizePageSize(pageSize);

            if (!string.IsNullOrWhiteSpace(matterId))
            {
                if (!await _matterAccess.CanReadMatterAsync(matterId, User, HttpContext.RequestAborted))
                {
                    return Ok(Array.Empty<Expense>());
                }

                query = query.Where(e => e.MatterId == matterId);
            }
            else if (!isPrivileged)
            {
                query = query.Where(e =>
                    (!string.IsNullOrWhiteSpace(e.MatterId) && readableMatterIds.Contains(e.MatterId!)) ||
                    ((e.MatterId == null || e.MatterId == "") && e.SubmittedBy == currentUserId));
            }

            if (!string.IsNullOrWhiteSpace(approvalStatus))
            {
                query = query.Where(e => e.ApprovalStatus == approvalStatus);
            }

            var totalCount = await query.CountAsync(HttpContext.RequestAborted);
            var items = await query
                .OrderByDescending(e => e.Date)
                .ThenByDescending(e => e.CreatedAt)
                .Skip((normalizedPage - 1) * normalizedPageSize)
                .Take(normalizedPageSize)
                .Select(e => new ExpenseListItemDto
                {
                    Id = e.Id,
                    MatterId = e.MatterId,
                    Description = e.Description,
                    Amount = e.Amount,
                    Date = e.Date,
                    Category = e.Category,
                    Billed = e.Billed,
                    Type = e.Type,
                    ExpenseCode = e.ExpenseCode,
                    ApprovalStatus = e.ApprovalStatus,
                    SubmittedAt = e.SubmittedAt,
                    ApprovedAt = e.ApprovedAt,
                    RejectedAt = e.RejectedAt,
                    RejectionReason = e.RejectionReason,
                    CreatedAt = e.CreatedAt,
                    UpdatedAt = e.UpdatedAt
                })
                .ToListAsync(HttpContext.RequestAborted);

            return Ok(new PagedCollectionResponse<ExpenseListItemDto>
            {
                Items = items,
                TotalCount = totalCount,
                Page = normalizedPage,
                PageSize = normalizedPageSize,
                HasMore = normalizedPage * normalizedPageSize < totalCount
            });
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
                if (!await _matterAccess.CanReadMatterAsync(dto.MatterId, User, HttpContext.RequestAborted))
                {
                    return Forbid();
                }
            }

            var userId = GetUserId();

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
                ApprovalStatus = "Pending",
                SubmittedBy = userId,
                SubmittedAt = DateTime.UtcNow,
                ApprovedBy = null,
                ApprovedAt = null,
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
            if (!await CanAccessExpenseAsync(expense))
            {
                return Forbid();
            }

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
            if (!IsApprover())
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "Only billing approvers can approve expenses." });
            }

            var expense = await _context.Expenses.FindAsync(id);
            if (expense == null) return NotFound();
            if (!await CanAccessExpenseAsync(expense))
            {
                return Forbid();
            }

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
            if (!IsApprover())
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "Only billing approvers can reject expenses." });
            }

            var expense = await _context.Expenses.FindAsync(id);
            if (expense == null) return NotFound();
            if (!await CanAccessExpenseAsync(expense))
            {
                return Forbid();
            }

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
            return HasAnyRole("Admin", "Partner", "Manager", "Associate", "Attorney", "Accountant");
        }

        private bool HasAnyRole(params string[] roles)
        {
            return roles.Any(role =>
                User.IsInRole(role) ||
                User.Claims.Any(claim =>
                    (claim.Type == ClaimTypes.Role || claim.Type == "role") &&
                    string.Equals(claim.Value, role, StringComparison.OrdinalIgnoreCase)));
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
                var enqueued = _plannerTriggerQueue.Enqueue(new OutcomeFeePlannerTriggerJob(
                    GetTenantId(),
                    GetTenantSlug(),
                    GetUserId() ?? "system",
                    new OutcomeFeePlanTriggerRequest
                    {
                        MatterId = matterId,
                        TriggerType = triggerType,
                        TriggerEntityType = nameof(Expense),
                        TriggerEntityId = entityId,
                        QueueReviewOnDrift = true,
                        QueueNotificationOnDrift = true
                    }));

                if (!enqueued)
                {
                    _logger.LogWarning(
                        "Outcome-to-Fee planner trigger queue rejected expense trigger. MatterId={MatterId} TriggerType={TriggerType} EntityId={EntityId}",
                        matterId,
                        triggerType,
                        entityId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Outcome-to-Fee planner trigger failed for expense {ExpenseId}", entityId);
            }
        }

        private string GetTenantId() =>
            User.FindFirst("tenantId")?.Value ?? string.Empty;

        private string GetTenantSlug() =>
            User.FindFirst("tenantSlug")?.Value
            ?? HttpContext.Request.Headers["X-Tenant-Slug"].FirstOrDefault()
            ?? string.Empty;

        private async Task<bool> CanAccessExpenseAsync(Expense expense)
        {
            if (_matterAccess.IsPrivileged(User))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(expense.MatterId))
            {
                return await _matterAccess.CanReadMatterAsync(expense.MatterId, User, HttpContext.RequestAborted);
            }

            var currentUserId = _matterAccess.GetCurrentUserId(User);
            return !string.IsNullOrWhiteSpace(currentUserId) &&
                string.Equals(expense.SubmittedBy, currentUserId, StringComparison.Ordinal);
        }

        private static int NormalizePageSize(int pageSize)
        {
            if (pageSize <= 0)
            {
                return DefaultPageSize;
            }

            return Math.Clamp(pageSize, 1, MaxPageSize);
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
