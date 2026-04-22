using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Diagnostics;
using JurisFlow.Server.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using JurisFlow.Server.Data;
using JurisFlow.Server.DTOs;
using JurisFlow.Server.Models;
using JurisFlow.Server.Services;

namespace JurisFlow.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Policy = "StaffOnly")]
    public class BootstrapController : ControllerBase
    {
        private const int BootstrapTimeEntriesLimit = 50;
        private const int BootstrapExpensesLimit = 50;
        private const int BootstrapInvoicesLimit = 50;
        private readonly JurisFlowDbContext _context;
        private readonly ILogger<BootstrapController> _logger;
        private readonly MatterAccessService _matterAccess;
        private readonly TaskAccessService _taskAccess;
        private readonly MatterClientLinkService _matterClientLinks;

        public BootstrapController(
            JurisFlowDbContext context,
            ILogger<BootstrapController> logger,
            MatterAccessService matterAccess,
            TaskAccessService taskAccess,
            MatterClientLinkService matterClientLinks)
        {
            _context = context;
            _logger = logger;
            _matterAccess = matterAccess;
            _taskAccess = taskAccess;
            _matterClientLinks = matterClientLinks;
        }

        [HttpGet]
        [EnableRateLimiting("BootstrapRead")]
        public async Task<ActionResult<BootstrapResponse>> GetBootstrap([FromQuery] string? scope = null)
        {
            var userId = GetUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Unauthorized();
            }

            if (!TryNormalizeScope(scope, out var normalizedScope))
            {
                return BadRequest(new { message = "scope must be one of: initial, deferred, full." });
            }

            var includeInitial = normalizedScope is "initial" or "full";
            var includeDeferred = normalizedScope is "deferred" or "full";

            try
            {
                var totalStopwatch = Stopwatch.StartNew();
                var response = new BootstrapResponse();
                var isPrivileged = _matterAccess.IsPrivileged(User);
                var scopedMatters = TenantScope(_context.Matters.AsNoTracking());
                var readableMattersQuery = _matterAccess.ApplyReadableScope(scopedMatters, User);
                var billingReadableMattersQuery = isPrivileged
                    ? readableMattersQuery
                    : _matterAccess.ApplyBillingReadableScope(scopedMatters, User);
                var readableMatterIdsQuery = readableMattersQuery.Select(m => m.Id);
                var billingReadableMatterIdsQuery = billingReadableMattersQuery.Select(m => m.Id);
                List<Matter> readableMatters = new();
                Dictionary<string, Matter> readableMatterMap = new(StringComparer.Ordinal);

                if (includeInitial)
                {
                    var initialStopwatch = Stopwatch.StartNew();
                    readableMatters = await readableMattersQuery
                        .OrderByDescending(m => m.OpenDate)
                        .ToListAsync();
                    await _matterClientLinks.PopulateRelatedClientsAsync(readableMatters, HttpContext.RequestAborted);
                    readableMatterMap = readableMatters.ToDictionary(m => m.Id, StringComparer.Ordinal);
                    response.Matters = readableMatters;

                    var tasks = await _taskAccess.ApplyReadableScope(_context.Tasks.AsNoTracking(), User)
                        .Where(t => t.Status != "Archived")
                        .OrderByDescending(t => t.UpdatedAt)
                        .ThenByDescending(t => t.Id)
                        .Take(50)
                        .Select(t => new
                        {
                            t.Id,
                            t.Title,
                            t.Description,
                            t.DueDate,
                            t.ReminderAt,
                            t.Priority,
                            t.Status,
                            t.Outcome,
                            t.MatterId,
                            MatterName = t.Matter != null ? t.Matter.Name : null,
                            t.AssignedEmployeeId,
                            AssignedEmployeeFirstName = t.AssignedEmployee != null ? t.AssignedEmployee.FirstName : null,
                            AssignedEmployeeLastName = t.AssignedEmployee != null ? t.AssignedEmployee.LastName : null,
                            t.RowVersion,
                            t.ReminderSent,
                            t.CreatedAt,
                            t.UpdatedAt
                        })
                        .ToListAsync();

                    response.Tasks = tasks.Select(t => new TaskResponse
                    {
                        Id = t.Id,
                        Title = t.Title,
                        Description = t.Description,
                        DueDate = t.DueDate,
                        ReminderAt = t.ReminderAt,
                        Priority = t.Priority,
                        Status = t.Status,
                        Outcome = t.Outcome,
                        MatterId = t.MatterId,
                        MatterName = t.MatterName,
                        AssignedEmployeeId = t.AssignedEmployeeId,
                        AssignedTo = $"{t.AssignedEmployeeFirstName} {t.AssignedEmployeeLastName}".Trim() is var assignedTo && !string.IsNullOrWhiteSpace(assignedTo) ? assignedTo : null,
                        RowVersion = t.RowVersion,
                        ReminderSent = t.ReminderSent,
                        CreatedAt = t.CreatedAt,
                        UpdatedAt = t.UpdatedAt
                    }).ToList();

                    var timeEntriesQuery = _context.TimeEntries.AsNoTracking().AsQueryable();
                    if (isPrivileged)
                    {
                        timeEntriesQuery = TenantScope(timeEntriesQuery).Where(t => t.MatterId == null || t.MatterId == "" || TenantScope(_context.Matters).Any(m => m.Id == t.MatterId));
                    }
                    else
                    {
                        timeEntriesQuery = TenantScope(timeEntriesQuery).Where(t =>
                            (!string.IsNullOrWhiteSpace(t.MatterId) && readableMatterIdsQuery.Contains(t.MatterId!)) ||
                            ((t.MatterId == null || t.MatterId == "") && t.SubmittedBy == userId));
                    }

                    response.TimeEntries = await timeEntriesQuery
                        .OrderByDescending(t => t.Date)
                        .ThenByDescending(t => t.CreatedAt)
                        .Take(BootstrapTimeEntriesLimit)
                        .Select(t => new TimeEntryListItemDto
                        {
                            Id = t.Id,
                            MatterId = t.MatterId,
                            Description = t.Description,
                            Duration = t.Duration,
                            Rate = t.Rate,
                            Date = t.Date,
                            Billed = t.Billed,
                            IsBillable = t.IsBillable,
                            Type = t.Type,
                            ActivityCode = t.ActivityCode,
                            TaskCode = t.TaskCode,
                            ApprovalStatus = t.ApprovalStatus,
                            SubmittedAt = t.SubmittedAt,
                            ApprovedAt = t.ApprovedAt,
                            RejectedAt = t.RejectedAt,
                            RejectionReason = t.RejectionReason,
                            CreatedAt = t.CreatedAt,
                            UpdatedAt = t.UpdatedAt
                        })
                        .ToListAsync();

                    var eventsQuery = _context.CalendarEvents.AsNoTracking().AsQueryable();
                    if (isPrivileged)
                    {
                        eventsQuery = TenantScope(eventsQuery).Where(e => e.MatterId == null || e.MatterId == "" || TenantScope(_context.Matters).Any(m => m.Id == e.MatterId));
                    }
                    else
                    {
                        eventsQuery = TenantScope(eventsQuery).Where(e => !string.IsNullOrWhiteSpace(e.MatterId) && readableMatterIdsQuery.Contains(e.MatterId!));
                    }

                    response.Events = await eventsQuery
                        .OrderByDescending(e => e.Date)
                        .ToListAsync();

                    response.Notifications = await TenantScope(_context.Notifications.AsNoTracking())
                        .Where(n => n.UserId == userId)
                        .OrderByDescending(n => n.CreatedAt)
                        .Take(100)
                        .ToListAsync();
                    _logger.LogInformation("Bootstrap initial scope loaded in {ElapsedMs} ms for user {UserId}", initialStopwatch.ElapsedMilliseconds, userId);
                }

                if (includeDeferred)
                {
                    var deferredStopwatch = Stopwatch.StartNew();
                    if (!includeInitial)
                    {
                        var readableMatterAccessRows = await readableMattersQuery
                            .Select(m => new Matter
                            {
                                Id = m.Id,
                                CreatedByUserId = m.CreatedByUserId,
                                ShareWithFirm = m.ShareWithFirm,
                                ShareNotesWithFirm = m.ShareNotesWithFirm
                            })
                            .ToListAsync();

                        readableMatterMap = readableMatterAccessRows.ToDictionary(m => m.Id, StringComparer.Ordinal);
                    }

                    var expensesQuery = _context.Expenses.AsNoTracking().AsQueryable();
                    if (isPrivileged)
                    {
                        expensesQuery = TenantScope(expensesQuery).Where(e => e.MatterId == null || e.MatterId == "" || TenantScope(_context.Matters).Any(m => m.Id == e.MatterId));
                    }
                    else
                    {
                        expensesQuery = TenantScope(expensesQuery).Where(e =>
                            (!string.IsNullOrWhiteSpace(e.MatterId) && readableMatterIdsQuery.Contains(e.MatterId!)) ||
                            ((e.MatterId == null || e.MatterId == "") && e.SubmittedBy == userId));
                    }

                    response.Expenses = await expensesQuery
                        .OrderByDescending(e => e.Date)
                        .ThenByDescending(e => e.CreatedAt)
                        .Take(BootstrapExpensesLimit)
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
                        .ToListAsync();

                    var invoicesQuery = _context.Invoices.AsNoTracking().AsQueryable();
                    if (isPrivileged)
                    {
                        invoicesQuery = TenantScope(invoicesQuery).Where(i => i.MatterId == null || i.MatterId == "" || TenantScope(_context.Matters).Any(m => m.Id == i.MatterId));
                    }
                    else
                    {
                        invoicesQuery = TenantScope(invoicesQuery).Where(i => !string.IsNullOrWhiteSpace(i.MatterId) && billingReadableMatterIdsQuery.Contains(i.MatterId!));
                    }

                    response.Invoices = await invoicesQuery
                        .OrderByDescending(i => i.CreatedAt)
                        .Take(BootstrapInvoicesLimit)
                        .Select(i => new InvoiceListItemDto
                        {
                            Id = i.Id,
                            Number = i.Number,
                            ClientId = i.ClientId,
                            MatterId = i.MatterId,
                            EntityId = i.EntityId,
                            OfficeId = i.OfficeId,
                            Status = i.Status,
                            IssueDate = i.IssueDate,
                            DueDate = i.DueDate,
                            Subtotal = i.Subtotal,
                            Tax = i.Tax,
                            Discount = i.Discount,
                            Total = i.Total,
                            AmountPaid = i.AmountPaid,
                            Balance = i.Balance,
                            LineItemCount = i.LineItems.Count,
                            CreatedAt = i.CreatedAt,
                            UpdatedAt = i.UpdatedAt
                        })
                        .ToListAsync();

                    var documentsQuery = _context.Documents.AsNoTracking().AsQueryable();
                    if (isPrivileged)
                    {
                        documentsQuery = TenantScope(documentsQuery).Where(d => d.MatterId == null || d.MatterId == "" || TenantScope(_context.Matters).Any(m => m.Id == d.MatterId));
                    }
                    else
                    {
                        documentsQuery = TenantScope(documentsQuery).Where(d =>
                            (!string.IsNullOrWhiteSpace(d.MatterId) && readableMatterIdsQuery.Contains(d.MatterId!)) ||
                            ((d.MatterId == null || d.MatterId == "") && d.UploadedBy == userId));
                    }

                    response.Documents = (await documentsQuery
                        .OrderByDescending(d => d.CreatedAt)
                        .Take(200)
                        .ToListAsync())
                        .Select(BootstrapDocumentResponse.FromModel)
                        .ToList();

                    response.TaskTemplates = (await _context.Set<TaskTemplate>()
                        .AsNoTracking()
                        .Where(t => t.IsActive)
                        .OrderBy(t => t.Category)
                        .ThenBy(t => t.Name)
                        .ToListAsync())
                        .Select(TaskTemplateResponse.FromModel)
                        .ToList();
                    _logger.LogInformation("Bootstrap deferred scope loaded in {ElapsedMs} ms for user {UserId}", deferredStopwatch.ElapsedMilliseconds, userId);
                }

                _logger.LogInformation("Bootstrap scope {Scope} completed in {ElapsedMs} ms for user {UserId}", normalizedScope, totalStopwatch.ElapsedMilliseconds, userId);
                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Bootstrap data load failed for user {UserId}", userId);
                return StatusCode(500, new { message = "Failed to load initial data." });
            }
        }

        private static bool TryNormalizeScope(string? scope, out string normalizedScope)
        {
            normalizedScope = string.IsNullOrWhiteSpace(scope)
                ? "full"
                : scope.Trim().ToLowerInvariant();

            return normalizedScope == "initial"
                || normalizedScope == "deferred"
                || normalizedScope == "full";
        }

        private IQueryable<TEntity> TenantScope<TEntity>(IQueryable<TEntity> query) where TEntity : class
        {
            if (!_context.RequireTenant)
            {
                return query;
            }

            if (string.IsNullOrWhiteSpace(_context.TenantId))
            {
                throw new InvalidOperationException("Tenant context is required.");
            }

            return query.Where(entity => EF.Property<string>(entity, "TenantId") == _context.TenantId);
        }
        private string? GetUserId()
        {
            return User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                   ?? User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        }
    }
}
