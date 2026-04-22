using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using JurisFlow.Server.Services;
using System.Data;
using System.Globalization;

namespace JurisFlow.Server.Controllers
{
    [Route("api/billing")]
    [ApiController]
    [Authorize(Policy = "BillingRead")]
    public class BillingController : ControllerBase
    {
        private readonly JurisFlowDbContext _context;
        private readonly AuditLogger _auditLogger;
        private readonly TenantContext _tenantContext;
        private readonly BillingObjectAuthorizationService _billingAuthorization;

        public BillingController(
            JurisFlowDbContext context,
            AuditLogger auditLogger,
            TenantContext tenantContext,
            BillingObjectAuthorizationService billingAuthorization)
        {
            _context = context;
            _auditLogger = auditLogger;
            _tenantContext = tenantContext;
            _billingAuthorization = billingAuthorization;
        }

        [HttpPost("mark-billed")]
        [Authorize(Policy = "BillingWrite")]
        public async Task<IActionResult> MarkAsBilled([FromBody] MarkBilledDto? dto)
        {
            if (dto == null)
            {
                return BadRequest(new { message = "Request body is required." });
            }

            if (string.IsNullOrWhiteSpace(dto.MatterId))
            {
                return BadRequest(new { message = "MatterId is required." });
            }

            var now = DateTime.UtcNow;
            var normalizedApproved = "APPROVED";

            await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

            var matter = await TenantScope(_context.Matters)
                .AsNoTracking()
                .Select(m => new
                {
                    m.Id,
                    m.Name
                })
                .FirstOrDefaultAsync(m => m.Id == dto.MatterId);
            if (matter == null)
            {
                return NotFound(new { message = "Matter not found for this tenant." });
            }

            if (!await _billingAuthorization.CanManageMatterAsync(dto.MatterId, User, HttpContext.RequestAborted))
            {
                return Forbid();
            }

            var eligibleTimeQuery = TenantScope(_context.TimeEntries)
                .Where(t =>
                    t.MatterId == dto.MatterId &&
                    !t.Billed &&
                    t.IsBillable &&
                    (string.IsNullOrWhiteSpace(t.ApprovalStatus) ||
                     t.ApprovalStatus.Trim().ToUpper() == normalizedApproved));

            var eligibleExpenseQuery = TenantScope(_context.Expenses)
                .Where(e =>
                    e.MatterId == dto.MatterId &&
                    !e.Billed &&
                    (string.IsNullOrWhiteSpace(e.ApprovalStatus) ||
                     e.ApprovalStatus.Trim().ToUpper() == normalizedApproved));

            var alreadyBilledTimeEntries = await TenantScope(_context.TimeEntries)
                .CountAsync(t =>
                    t.MatterId == dto.MatterId &&
                    t.Billed &&
                    t.IsBillable &&
                    (string.IsNullOrWhiteSpace(t.ApprovalStatus) ||
                     t.ApprovalStatus.Trim().ToUpper() == normalizedApproved));

            var alreadyBilledExpenses = await TenantScope(_context.Expenses)
                .CountAsync(e =>
                    e.MatterId == dto.MatterId &&
                    e.Billed &&
                    (string.IsNullOrWhiteSpace(e.ApprovalStatus) ||
                     e.ApprovalStatus.Trim().ToUpper() == normalizedApproved));

            if (await HasLockedPeriodConflictAsync(eligibleTimeQuery, eligibleExpenseQuery))
            {
                return BadRequest(new { message = "Billing period is locked. Cannot mark entries as billed." });
            }

            var markedTimeEntries = await eligibleTimeQuery.ExecuteUpdateAsync(setters => setters
                .SetProperty(t => t.Billed, true)
                .SetProperty(t => t.UpdatedAt, now));

            var markedExpenses = await eligibleExpenseQuery.ExecuteUpdateAsync(setters => setters
                .SetProperty(e => e.Billed, true)
                .SetProperty(e => e.UpdatedAt, now));

            await transaction.CommitAsync();

            await _auditLogger.LogAsync(
                HttpContext,
                "billing.mark_billed",
                "Matter",
                dto.MatterId,
                $"Matter={matter.Name}, MarkedTimeEntries={markedTimeEntries}, MarkedExpenses={markedExpenses}, AlreadyBilledTimeEntries={alreadyBilledTimeEntries}, AlreadyBilledExpenses={alreadyBilledExpenses}");

            return Ok(new
            {
                matterId = matter.Id,
                matterName = matter.Name,
                markedTimeEntries,
                markedExpenses,
                alreadyBilledTimeEntries,
                alreadyBilledExpenses
            });
        }

        private async Task<bool> HasLockedPeriodConflictAsync(IQueryable<TimeEntry> eligibleTimeQuery, IQueryable<Expense> eligibleExpenseQuery)
        {
            var eligibleDays = await eligibleTimeQuery
                .Select(t => t.Date.Date)
                .Concat(eligibleExpenseQuery.Select(e => e.Date.Date))
                .Distinct()
                .ToListAsync();

            foreach (var day in eligibleDays)
            {
                var locked = await TenantScope(_context.BillingLocks)
                    .AsNoTracking()
                    .AnyAsync(b => b.PeriodStart <= day && b.PeriodEnd >= day);
                if (locked)
                {
                    return true;
                }
            }

            return false;
        }

        private IQueryable<T> TenantScope<T>(IQueryable<T> query) where T : class
        {
            var tenantId = RequireTenantId();
            return query.Where(e => EF.Property<string>(e, "TenantId") == tenantId);
        }

        private string RequireTenantId()
        {
            var tenantId = _tenantContext.TenantId;
            if (string.IsNullOrWhiteSpace(tenantId))
            {
                throw new InvalidOperationException("Tenant context is required.");
            }

            return tenantId;
        }

    }

    public class MarkBilledDto
    {
        public string MatterId { get; set; } = string.Empty;
    }
}
