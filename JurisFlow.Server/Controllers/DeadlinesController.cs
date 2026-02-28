using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using JurisFlow.Server.Services;
using Task = System.Threading.Tasks.Task;

namespace JurisFlow.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Policy = "StaffOnly")]
    public class DeadlinesController : ControllerBase
    {
        private readonly JurisFlowDbContext _context;
        private readonly DeadlineReminderService _deadlineReminderService;
        private readonly AuditLogger _auditLogger;
        private readonly TenantContext _tenantContext;
        private readonly ILogger<DeadlinesController> _logger;
        private static int _manualReminderRunState;
        private const int MaxDeadlineResults = 200;

        private static readonly HashSet<string> AllowedStatuses = new(StringComparer.OrdinalIgnoreCase)
        {
            "Pending",
            "Completed",
            "Missed",
            "Extended"
        };

        private static readonly HashSet<string> AllowedPriorities = new(StringComparer.OrdinalIgnoreCase)
        {
            "High",
            "Medium",
            "Low"
        };

        private static readonly HashSet<string> AllowedDeadlineTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "Filing",
            "Hearing",
            "Response",
            "Discovery",
            "Trial",
            "Other"
        };

        public DeadlinesController(
            JurisFlowDbContext context,
            DeadlineReminderService deadlineReminderService,
            AuditLogger auditLogger,
            TenantContext tenantContext,
            ILogger<DeadlinesController> logger)
        {
            _context = context;
            _deadlineReminderService = deadlineReminderService;
            _auditLogger = auditLogger;
            _tenantContext = tenantContext;
            _logger = logger;
        }

        // GET: api/deadlines
        [HttpGet]
        public async Task<ActionResult<IEnumerable<DeadlineResponseDto>>> GetDeadlines(
            [FromQuery] string? matterId = null,
            [FromQuery] string? status = null,
            [FromQuery] int days = 30)
        {
            var normalizedDays = Math.Clamp(days <= 0 ? 30 : days, 1, 365);
            var normalizedMatterId = NormalizeOptionalText(matterId, 100);
            var normalizedStatus = NormalizeAllowedOptional(status, AllowedStatuses, nameof(status));

            var query = TenantScope(_context.Deadlines).AsNoTracking();

            if (!string.IsNullOrEmpty(normalizedMatterId))
            {
                query = query.Where(d => d.MatterId == normalizedMatterId);
            }

            if (!string.IsNullOrEmpty(normalizedStatus))
            {
                query = query.Where(d => d.Status == normalizedStatus);
            }

            // Default: get deadlines within next N days
            var cutoffDate = DateTime.UtcNow.AddDays(normalizedDays);
            query = query.Where(d => d.DueDate <= cutoffDate);

            var deadlines = await query
                .OrderBy(d => d.DueDate)
                .Take(MaxDeadlineResults)
                .ToListAsync();

            return Ok(deadlines.Select(ToDeadlineResponse));
        }

        // GET: api/deadlines/{id}
        [HttpGet("{id}")]
        public async Task<ActionResult<DeadlineResponseDto>> GetDeadline(string id)
        {
            var deadline = await TenantScope(_context.Deadlines)
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == id);
            if (deadline == null)
            {
                return NotFound();
            }

            return Ok(ToDeadlineResponse(deadline));
        }

        // POST: api/deadlines
        [HttpPost]
        public async Task<ActionResult<DeadlineResponseDto>> CreateDeadline([FromBody] CreateDeadlineDto dto)
        {
            if (dto == null)
            {
                return BadRequest(new { message = "Request body is required." });
            }

            try
            {
                var userId = RequireUserId();
                var matterId = NormalizeRequiredText(dto.MatterId, nameof(dto.MatterId), 100);
                await EnsureMatterExistsAsync(matterId);

                var title = NormalizeRequiredText(dto.Title, nameof(dto.Title), 250);
                var courtRule = await ResolveCourtRuleAsync(dto.CourtRuleId);
                var priority = NormalizeAllowedOptional(dto.Priority, AllowedPriorities, nameof(dto.Priority)) ?? "Medium";
                var deadlineType = NormalizeAllowedOptional(dto.DeadlineType, AllowedDeadlineTypes, nameof(dto.DeadlineType)) ?? "Filing";
                var reminderDays = NormalizeReminderDays(dto.ReminderDays ?? 3);
                var assignedTo = await ResolveAssignedUserAsync(dto.AssignedTo, userId);

                if (dto.DueDate == default)
                {
                    return BadRequest(new { message = "DueDate is required." });
                }

                var deadline = new Deadline
                {
                    MatterId = matterId,
                    CourtRuleId = courtRule?.Id,
                    Title = title,
                    Description = NormalizeOptionalText(dto.Description, 4000),
                    DueDate = dto.DueDate,
                    TriggerDate = dto.TriggerDate,
                    Priority = priority,
                    DeadlineType = deadlineType,
                    ReminderDays = reminderDays,
                    AssignedTo = assignedTo,
                    Notes = NormalizeOptionalText(dto.Notes, 4000)
                };

                _context.Deadlines.Add(deadline);
                await _context.SaveChangesAsync();

                await _auditLogger.LogAsync(
                    HttpContext,
                    "deadline.create",
                    "Deadline",
                    deadline.Id,
                    $"Created deadline '{deadline.Title}' for matter {deadline.MatterId} due {deadline.DueDate:O}");

                return CreatedAtAction(nameof(GetDeadline), new { id = deadline.Id }, ToDeadlineResponse(deadline));
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Deadline create validation failed.");
                return BadRequest(new { message = ex.Message });
            }
        }

        // POST: api/deadlines/calculate
        [HttpPost("calculate")]
        public async Task<ActionResult<CalculatedDeadlineDto>> CalculateDeadline([FromBody] CalculateDeadlineDto dto)
        {
            if (dto == null)
            {
                return BadRequest(new { message = "Request body is required." });
            }

            try
            {
                var rule = await ResolveCourtRuleAsync(dto.CourtRuleId);
                if (rule == null)
                {
                    return NotFound(new { message = "Court rule not found" });
                }

                var triggerDate = (dto.TriggerDate ?? DateTime.UtcNow.Date).Date;
                var holidays = await LoadHolidays(rule.Jurisdiction);
                var serviceDaysAdded = GetServiceDaysAdd(rule, dto.ServiceMethod);
                var calculatedDate = CalculateDeadlineDate(triggerDate, rule, dto.ServiceMethod, holidays);

                var description = $"{rule.DaysCount} {rule.DayType.ToLowerInvariant()} days {rule.Direction.ToLowerInvariant()} {rule.TriggerEvent}";
                if (serviceDaysAdded > 0)
                {
                    description += $" (+{serviceDaysAdded} service days)";
                }

                return Ok(new CalculatedDeadlineDto
                {
                    TriggerDate = triggerDate,
                    DueDate = calculatedDate,
                    RuleName = rule.Name,
                    RuleCitation = rule.Citation,
                    DaysCount = rule.DaysCount,
                    ServiceDaysAdded = serviceDaysAdded,
                    Description = description
                });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Deadline calculation validation failed.");
                return BadRequest(new { message = ex.Message });
            }
        }

        // PUT: api/deadlines/{id}
        [HttpPut("{id}")]
        public async Task<ActionResult<DeadlineResponseDto>> UpdateDeadline(string id, [FromBody] UpdateDeadlineDto dto)
        {
            if (dto == null)
            {
                return BadRequest(new { message = "Request body is required." });
            }

            var deadline = await TenantScope(_context.Deadlines).FirstOrDefaultAsync(d => d.Id == id);
            if (deadline == null)
            {
                return NotFound();
            }

            try
            {
                if (dto.Title != null) deadline.Title = NormalizeRequiredText(dto.Title, nameof(dto.Title), 250);
                if (dto.Description != null) deadline.Description = NormalizeOptionalText(dto.Description, 4000);
                if (dto.DueDate.HasValue && dto.DueDate.Value != deadline.DueDate)
                {
                    deadline.DueDate = dto.DueDate.Value;
                    deadline.ReminderSent = false;
                    if (string.Equals(deadline.Status, "Missed", StringComparison.OrdinalIgnoreCase))
                    {
                        deadline.Status = "Pending";
                    }
                }
                if (dto.Status != null)
                {
                    deadline.Status = NormalizeAllowedRequired(dto.Status, AllowedStatuses, nameof(dto.Status));
                }
                if (dto.Priority != null)
                {
                    deadline.Priority = NormalizeAllowedRequired(dto.Priority, AllowedPriorities, nameof(dto.Priority));
                }
                if (dto.AssignedTo != null)
                {
                    deadline.AssignedTo = await ResolveAssignedUserAsync(dto.AssignedTo, null);
                }
                if (dto.Notes != null) deadline.Notes = NormalizeOptionalText(dto.Notes, 4000);
                if (dto.ReminderDays.HasValue && dto.ReminderDays.Value != deadline.ReminderDays)
                {
                    deadline.ReminderDays = NormalizeReminderDays(dto.ReminderDays.Value);
                    deadline.ReminderSent = false;
                }

                deadline.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                await _auditLogger.LogAsync(
                    HttpContext,
                    "deadline.update",
                    "Deadline",
                    deadline.Id,
                    $"Updated deadline '{deadline.Title}' status={deadline.Status} due={deadline.DueDate:O}");

                return Ok(ToDeadlineResponse(deadline));
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Deadline update validation failed for {DeadlineId}.", id);
                return BadRequest(new { message = ex.Message });
            }
        }

        // POST: api/deadlines/{id}/complete
        [HttpPost("{id}/complete")]
        public async Task<IActionResult> CompleteDeadline(string id)
        {
            var deadline = await TenantScope(_context.Deadlines).FirstOrDefaultAsync(d => d.Id == id);
            if (deadline == null)
            {
                return NotFound();
            }

            var userId = RequireUserId();

            deadline.Status = "Completed";
            deadline.CompletedAt = DateTime.UtcNow;
            deadline.CompletedBy = userId;
            deadline.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            await _auditLogger.LogAsync(
                HttpContext,
                "deadline.complete",
                "Deadline",
                deadline.Id,
                $"Completed deadline '{deadline.Title}' at {deadline.CompletedAt:O}");

            return Ok(new { message = "Deadline completed", completedAt = deadline.CompletedAt });
        }

        // DELETE: api/deadlines/{id}
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteDeadline(string id)
        {
            var deadline = await TenantScope(_context.Deadlines).FirstOrDefaultAsync(d => d.Id == id);
            if (deadline == null)
            {
                return NotFound();
            }

            _context.Deadlines.Remove(deadline);
            await _context.SaveChangesAsync();

            await _auditLogger.LogAsync(
                HttpContext,
                "deadline.delete",
                "Deadline",
                id,
                $"Deleted deadline '{deadline.Title}'");

            return NoContent();
        }

        // GET: api/deadlines/upcoming
        [HttpGet("upcoming")]
        public async Task<ActionResult<UpcomingDeadlinesDto>> GetUpcomingDeadlines([FromQuery] int days = 7)
        {
            var normalizedDays = Math.Clamp(days <= 0 ? 7 : days, 1, 60);
            var cutoffDate = DateTime.UtcNow.AddDays(normalizedDays);
            var today = DateTime.UtcNow.Date;

            var deadlines = await TenantScope(_context.Deadlines)
                .AsNoTracking()
                .Where(d => d.Status == "Pending" && d.DueDate <= cutoffDate)
                .OrderBy(d => d.DueDate)
                .Take(MaxDeadlineResults)
                .ToListAsync();

            var overdue = deadlines.Where(d => d.DueDate.Date < today).Select(ToDeadlineResponse).ToList();
            var dueToday = deadlines.Where(d => d.DueDate.Date == today).Select(ToDeadlineResponse).ToList();
            var upcoming = deadlines.Where(d => d.DueDate.Date > today).Select(ToDeadlineResponse).ToList();

            return Ok(new UpcomingDeadlinesDto
            {
                Overdue = overdue,
                DueToday = dueToday,
                Upcoming = upcoming,
                TotalCount = deadlines.Count
            });
        }

        // POST: api/deadlines/reminders/run
        [Authorize(Roles = "Admin,Partner,SecurityAdmin")]
        [EnableRateLimiting("AdminDangerousOps")]
        [HttpPost("reminders/run")]
        public async Task<IActionResult> RunReminders()
        {
            if (System.Threading.Interlocked.Exchange(ref _manualReminderRunState, 1) == 1)
            {
                return Conflict(new { message = "Deadline reminders are already running." });
            }

            var tenantId = RequireTenantId();
            try
            {
                var result = await _deadlineReminderService.ProcessAsync(tenantId);

                await _auditLogger.LogAsync(
                    HttpContext,
                    "deadline.reminders.run",
                    "Deadline",
                    tenantId,
                    $"Ran deadline reminders for tenant. Checked={result.TotalChecked} Reminders={result.RemindersSent} Missed={result.MissedUpdated}");

                return Ok(result);
            }
            finally
            {
                System.Threading.Interlocked.Exchange(ref _manualReminderRunState, 0);
            }
        }

        // Helper method for deadline calculation
        private async Task<List<DateTime>> LoadHolidays(string? jurisdiction)
        {
            var normalized = JurisdictionCalendar.Normalize(jurisdiction);
            var query = TenantScope(_context.Holidays).AsNoTracking();
            query = query.Where(h => h.IsCourtHoliday);
            if (!string.IsNullOrEmpty(normalized))
            {
                query = query.Where(h => h.Jurisdiction == normalized || h.Jurisdiction == "US-Federal");
            }
            else
            {
                query = query.Where(h => h.Jurisdiction == "US-Federal");
            }

            return await query
                .Select(h => h.Date.Date)
                .Distinct()
                .ToListAsync();
        }

        private DateTime CalculateDeadlineDate(DateTime triggerDate, CourtRule rule, string? serviceMethod, List<DateTime> holidays)
        {
            var serviceDays = GetServiceDaysAdd(rule, serviceMethod);
            var isBefore = string.Equals(rule.Direction, "Before", StringComparison.OrdinalIgnoreCase);

            DateTime result = string.Equals(rule.DayType, "Court", StringComparison.OrdinalIgnoreCase)
                ? AdjustForCourtDays(triggerDate, rule.DaysCount, isBefore, holidays)
                : triggerDate.AddDays(isBefore ? -rule.DaysCount : rule.DaysCount);

            if (serviceDays > 0)
            {
                result = result.AddDays(isBefore ? -serviceDays : serviceDays);
            }

            // Extend if falls on weekend/holiday
            if (rule.ExtendIfWeekend)
            {
                result = AdjustForWeekendOrHoliday(result, holidays, isBefore);
            }

            return result;
        }

        private DateTime AdjustForCourtDays(DateTime start, int courtDays, bool backwards, List<DateTime> holidays)
        {
            var current = start;
            var direction = backwards ? -1 : 1;
            var daysRemaining = courtDays;

            while (daysRemaining > 0)
            {
                current = current.AddDays(direction);
                if (IsBusinessDay(current, holidays))
                {
                    daysRemaining--;
                }
            }

            return current;
        }

        private bool IsBusinessDay(DateTime date, List<DateTime> holidays)
        {
            var isWeekend = date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday;
            var isHoliday = holidays.Any(h => h.Date == date.Date);
            return !isWeekend && !isHoliday;
        }

        private DateTime AdjustForWeekendOrHoliday(DateTime date, List<DateTime> holidays, bool backwards)
        {
            var direction = backwards ? -1 : 1;
            while (!IsBusinessDay(date, holidays))
            {
                date = date.AddDays(direction);
            }
            return date;
        }

        private int GetServiceDaysAdd(CourtRule rule, string? serviceMethod)
        {
            if (string.IsNullOrWhiteSpace(serviceMethod))
            {
                return 0;
            }

            var normalized = serviceMethod.Trim().ToLowerInvariant();
            return normalized switch
            {
                "mail" => rule.ServiceDaysAdd,
                "electronic" => 2,
                "e-service" => 2,
                "eservice" => 2,
                "personal" => 0,
                _ => 0
            };
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

        private string RequireUserId()
        {
            var userId = GetUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                throw new InvalidOperationException("Authenticated user context is required.");
            }

            return userId;
        }

        private async Task EnsureMatterExistsAsync(string matterId)
        {
            var exists = await TenantScope(_context.Matters)
                .AsNoTracking()
                .AnyAsync(m => m.Id == matterId);

            if (!exists)
            {
                throw new InvalidOperationException("Matter not found.");
            }
        }

        private async Task<CourtRule?> ResolveCourtRuleAsync(string? courtRuleId)
        {
            var normalizedRuleId = NormalizeOptionalText(courtRuleId, 100);
            if (string.IsNullOrWhiteSpace(normalizedRuleId))
            {
                return null;
            }

            return await TenantScope(_context.CourtRules)
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == normalizedRuleId && r.IsActive);
        }

        private async Task<string?> ResolveAssignedUserAsync(string? assignedTo, string? defaultUserId)
        {
            var normalizedAssignedTo = NormalizeOptionalText(assignedTo, 100);
            if (string.IsNullOrWhiteSpace(normalizedAssignedTo))
            {
                return defaultUserId;
            }

            var exists = await TenantScope(_context.Users)
                .AsNoTracking()
                .AnyAsync(u => u.Id == normalizedAssignedTo);

            if (!exists)
            {
                throw new InvalidOperationException("Assigned user not found.");
            }

            return normalizedAssignedTo;
        }

        private static string NormalizeRequiredText(string? value, string fieldName, int maxLength)
        {
            var normalized = NormalizeOptionalText(value, maxLength);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                throw new InvalidOperationException($"{fieldName} is required.");
            }

            return normalized;
        }

        private static string? NormalizeOptionalText(string? value, int maxLength)
        {
            if (value == null)
            {
                return null;
            }

            var normalized = value.Trim();
            if (normalized.Length == 0)
            {
                return null;
            }

            if (normalized.Length > maxLength)
            {
                throw new InvalidOperationException($"Value exceeds maximum length of {maxLength}.");
            }

            return normalized;
        }

        private static string? NormalizeAllowedOptional(string? value, HashSet<string> allowed, string fieldName)
        {
            var normalized = NormalizeOptionalText(value, 100);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return null;
            }

            var match = allowed.FirstOrDefault(v => string.Equals(v, normalized, StringComparison.OrdinalIgnoreCase));
            if (match == null)
            {
                throw new InvalidOperationException($"Unsupported {fieldName} value '{normalized}'.");
            }

            return match;
        }

        private static string NormalizeAllowedRequired(string? value, HashSet<string> allowed, string fieldName)
        {
            return NormalizeAllowedOptional(value, allowed, fieldName)
                ?? throw new InvalidOperationException($"{fieldName} is required.");
        }

        private static int NormalizeReminderDays(int reminderDays)
        {
            if (reminderDays < 0 || reminderDays > 365)
            {
                throw new InvalidOperationException("ReminderDays must be between 0 and 365.");
            }

            return reminderDays;
        }

        private static DeadlineResponseDto ToDeadlineResponse(Deadline deadline)
        {
            return new DeadlineResponseDto
            {
                Id = deadline.Id,
                MatterId = deadline.MatterId,
                CourtRuleId = deadline.CourtRuleId,
                Title = deadline.Title,
                Description = deadline.Description,
                DueDate = deadline.DueDate,
                TriggerDate = deadline.TriggerDate,
                Status = deadline.Status,
                Priority = deadline.Priority,
                DeadlineType = deadline.DeadlineType,
                ReminderDays = deadline.ReminderDays,
                ReminderSent = deadline.ReminderSent,
                AssignedTo = deadline.AssignedTo,
                Notes = deadline.Notes,
                CompletedAt = deadline.CompletedAt,
                CompletedBy = deadline.CompletedBy,
                CreatedAt = deadline.CreatedAt,
                UpdatedAt = deadline.UpdatedAt
            };
        }

        private string? GetUserId()
        {
            return User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
        }
    }

    // DTOs
    public class CreateDeadlineDto
    {
        public string MatterId { get; set; } = string.Empty;
        public string? CourtRuleId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public DateTime DueDate { get; set; }
        public DateTime? TriggerDate { get; set; }
        public string? Priority { get; set; }
        public string? DeadlineType { get; set; }
        public int? ReminderDays { get; set; }
        public string? AssignedTo { get; set; }
        public string? Notes { get; set; }
    }

    public class UpdateDeadlineDto
    {
        public string? Title { get; set; }
        public string? Description { get; set; }
        public DateTime? DueDate { get; set; }
        public string? Status { get; set; }
        public string? Priority { get; set; }
        public string? AssignedTo { get; set; }
        public string? Notes { get; set; }
        public int? ReminderDays { get; set; }
    }

    public class CalculateDeadlineDto
    {
        public string CourtRuleId { get; set; } = string.Empty;
        public DateTime? TriggerDate { get; set; }
        public string? ServiceMethod { get; set; } // Personal, Mail, Electronic
    }

    public class CalculatedDeadlineDto
    {
        public DateTime TriggerDate { get; set; }
        public DateTime DueDate { get; set; }
        public string RuleName { get; set; } = "";
        public string? RuleCitation { get; set; }
        public int DaysCount { get; set; }
        public int ServiceDaysAdded { get; set; }
        public string Description { get; set; } = "";
    }

    public class UpcomingDeadlinesDto
    {
        public List<DeadlineResponseDto> Overdue { get; set; } = new();
        public List<DeadlineResponseDto> DueToday { get; set; } = new();
        public List<DeadlineResponseDto> Upcoming { get; set; } = new();
        public int TotalCount { get; set; }
    }

    public class DeadlineResponseDto
    {
        public string Id { get; set; } = string.Empty;
        public string MatterId { get; set; } = string.Empty;
        public string? CourtRuleId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public DateTime DueDate { get; set; }
        public DateTime? TriggerDate { get; set; }
        public string Status { get; set; } = string.Empty;
        public string Priority { get; set; } = string.Empty;
        public string DeadlineType { get; set; } = string.Empty;
        public int ReminderDays { get; set; }
        public bool ReminderSent { get; set; }
        public string? AssignedTo { get; set; }
        public string? Notes { get; set; }
        public DateTime? CompletedAt { get; set; }
        public string? CompletedBy { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
