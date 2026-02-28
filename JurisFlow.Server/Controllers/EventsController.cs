using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using JurisFlow.Server.Services;
using Task = System.Threading.Tasks.Task;

namespace JurisFlow.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Policy = "StaffOnly")]
    public class EventsController : ControllerBase
    {
        private const int DefaultListLimit = 100;
        private const int MaxListLimit = 200;

        private static readonly HashSet<string> AllowedEventTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "Meeting",
            "Court",
            "Deadline",
            "Deposition",
            "Consultation",
            "Filing",
            "Hearing",
            "Trial",
            "Conference",
            "Other"
        };

        private readonly JurisFlowDbContext _context;
        private readonly AuditLogger _auditLogger;
        private readonly TenantContext _tenantContext;

        public EventsController(JurisFlowDbContext context, AuditLogger auditLogger, TenantContext tenantContext)
        {
            _context = context;
            _auditLogger = auditLogger;
            _tenantContext = tenantContext;
        }

        // GET: api/Events
        [HttpGet]
        public async Task<ActionResult<IEnumerable<CalendarEventResponseDto>>> GetEvents(
            [FromQuery] string? matterId = null,
            [FromQuery] DateTime? from = null,
            [FromQuery] DateTime? to = null,
            [FromQuery] int limit = DefaultListLimit)
        {
            RequireTenantId();
            var normalizedLimit = NormalizeLimit(limit);
            var query = TenantScope(_context.CalendarEvents).AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(matterId))
            {
                query = query.Where(e => e.MatterId == matterId.Trim());
            }

            if (from.HasValue)
            {
                var fromUtc = NormalizeIncomingDate(from.Value);
                query = query.Where(e => e.Date >= fromUtc);
            }

            if (to.HasValue)
            {
                var toUtc = NormalizeIncomingDate(to.Value);
                query = query.Where(e => e.Date <= toUtc);
            }

            var items = await query
                .OrderBy(e => e.Date)
                .Take(normalizedLimit)
                .Select(e => new CalendarEventResponseDto
                {
                    Id = e.Id,
                    Title = e.Title,
                    Date = e.Date,
                    Type = e.Type,
                    Description = e.Description,
                    Location = e.Location,
                    RecurrencePattern = e.RecurrencePattern,
                    Duration = e.Duration,
                    ReminderMinutes = e.ReminderMinutes,
                    ReminderSent = e.ReminderSent,
                    RowVersion = e.RowVersion,
                    MatterId = e.MatterId,
                    MatterName = e.Matter != null ? e.Matter.Name : null,
                    CreatedAt = e.CreatedAt,
                    UpdatedAt = e.UpdatedAt
                })
                .ToListAsync();

            return Ok(items);
        }

        // GET: api/Events/5
        [HttpGet("{id}")]
        public async Task<ActionResult<CalendarEventResponseDto>> GetEvent(string id)
        {
            RequireTenantId();

            var calendarEvent = await TenantScope(_context.CalendarEvents)
                .AsNoTracking()
                .Where(e => e.Id == id)
                .Select(e => new CalendarEventResponseDto
                {
                    Id = e.Id,
                    Title = e.Title,
                    Date = e.Date,
                    Type = e.Type,
                    Description = e.Description,
                    Location = e.Location,
                    RecurrencePattern = e.RecurrencePattern,
                    Duration = e.Duration,
                    ReminderMinutes = e.ReminderMinutes,
                    ReminderSent = e.ReminderSent,
                    RowVersion = e.RowVersion,
                    MatterId = e.MatterId,
                    MatterName = e.Matter != null ? e.Matter.Name : null,
                    CreatedAt = e.CreatedAt,
                    UpdatedAt = e.UpdatedAt
                })
                .FirstOrDefaultAsync();

            if (calendarEvent == null)
            {
                return NotFound();
            }

            return Ok(calendarEvent);
        }

        // POST: api/Events
        [HttpPost]
        public async Task<ActionResult<CalendarEventResponseDto>> CreateEvent([FromBody] CalendarEventDto dto)
        {
            RequireTenantId();
            if (dto == null)
            {
                return BadRequest(new { message = "Request body is required." });
            }

            var normalized = ValidateAndNormalizeDto(dto);
            if (!normalized.IsValid)
            {
                return BadRequest(new { message = normalized.ErrorMessage });
            }

            var matter = await ResolveMatterAsync(normalized.MatterId);
            if (normalized.MatterId != null && matter == null)
            {
                return BadRequest(new { message = "Matter not found." });
            }

            var calendarEvent = new CalendarEvent
            {
                Id = Guid.NewGuid().ToString(),
                Title = normalized.Title!,
                Date = normalized.Date,
                Type = normalized.Type!,
                Description = normalized.Description,
                Location = normalized.Location,
                RecurrencePattern = normalized.RecurrencePattern,
                Duration = normalized.Duration,
                ReminderMinutes = normalized.ReminderMinutes,
                ReminderSent = false,
                MatterId = normalized.MatterId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.CalendarEvents.Add(calendarEvent);
            await TryCreateUpcomingNotificationAsync(calendarEvent, matter);
            await _context.SaveChangesAsync();

            await _auditLogger.LogAsync(
                HttpContext,
                "event.create",
                nameof(CalendarEvent),
                calendarEvent.Id,
                $"Type={calendarEvent.Type}, MatterId={calendarEvent.MatterId}, ReminderSent={calendarEvent.ReminderSent}");

            return CreatedAtAction(nameof(GetEvent), new { id = calendarEvent.Id }, MapEvent(calendarEvent, matter?.Name));
        }

        // PUT: api/Events/5
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateEvent(string id, [FromBody] CalendarEventDto dto)
        {
            RequireTenantId();
            if (dto == null)
            {
                return BadRequest(new { message = "Request body is required." });
            }

            var calendarEvent = await TenantScope(_context.CalendarEvents)
                .FirstOrDefaultAsync(e => e.Id == id);
            if (calendarEvent == null)
            {
                return NotFound();
            }

            var normalized = ValidateAndNormalizeDto(dto);
            if (!normalized.IsValid)
            {
                return BadRequest(new { message = normalized.ErrorMessage });
            }

            if (string.IsNullOrWhiteSpace(dto.RowVersion))
            {
                return BadRequest(new { message = "RowVersion is required." });
            }

            var matter = await ResolveMatterAsync(normalized.MatterId);
            if (normalized.MatterId != null && matter == null)
            {
                return BadRequest(new { message = "Matter not found." });
            }

            if (!string.Equals(calendarEvent.RowVersion, dto.RowVersion.Trim(), StringComparison.Ordinal))
            {
                return Conflict(new { message = "The event was modified by another user. Reload and try again." });
            }

            calendarEvent.Title = normalized.Title!;
            calendarEvent.Date = normalized.Date;
            calendarEvent.Type = normalized.Type!;
            calendarEvent.Description = normalized.Description;
            calendarEvent.Location = normalized.Location;
            calendarEvent.RecurrencePattern = normalized.RecurrencePattern;
            calendarEvent.Duration = normalized.Duration;
            calendarEvent.ReminderMinutes = normalized.ReminderMinutes;
            calendarEvent.MatterId = normalized.MatterId;
            calendarEvent.UpdatedAt = DateTime.UtcNow;
            calendarEvent.RowVersion = Guid.NewGuid().ToString("N");

            await TryCreateUpcomingNotificationAsync(calendarEvent, matter);

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                return Conflict(new { message = "The event was modified by another user. Reload and try again." });
            }

            await _auditLogger.LogAsync(
                HttpContext,
                "event.update",
                nameof(CalendarEvent),
                calendarEvent.Id,
                $"Type={calendarEvent.Type}, MatterId={calendarEvent.MatterId}, ReminderSent={calendarEvent.ReminderSent}");

            return Ok(MapEvent(calendarEvent, matter?.Name));
        }

        // DELETE: api/Events/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteEvent(string id)
        {
            RequireTenantId();
            var calendarEvent = await TenantScope(_context.CalendarEvents).FirstOrDefaultAsync(e => e.Id == id);
            if (calendarEvent == null)
            {
                return NotFound();
            }

            _context.CalendarEvents.Remove(calendarEvent);
            await _context.SaveChangesAsync();

            await _auditLogger.LogAsync(HttpContext, "event.delete", nameof(CalendarEvent), id, $"Deleted event {calendarEvent.Title}");

            return NoContent();
        }

        private async Task TryCreateUpcomingNotificationAsync(CalendarEvent evt, Matter? matter)
        {
            var now = DateTime.UtcNow;
            if (evt.Date <= now)
            {
                return;
            }

            var reminderAtUtc = evt.Date.AddMinutes(-Math.Max(0, evt.ReminderMinutes));
            if (reminderAtUtc > now)
            {
                evt.ReminderSent = false;
                return;
            }

            if (evt.ReminderSent)
            {
                return;
            }

            var targetUserIds = await ResolveNotificationTargetsAsync(matter);
            if (targetUserIds.Count == 0)
            {
                return;
            }

            var title = "Upcoming event";
            var message = $"{evt.Title} - scheduled for {evt.Date.ToUniversalTime():O}";

            foreach (var userId in targetUserIds)
            {
                _context.Notifications.Add(new Notification
                {
                    UserId = userId,
                    Title = title,
                    Message = message,
                    Type = "warning",
                    Link = "tab:calendar"
                });
            }

            evt.ReminderSent = true;
        }

        private async Task<HashSet<string>> ResolveNotificationTargetsAsync(Matter? matter)
        {
            var userIds = new HashSet<string>(StringComparer.Ordinal);
            var currentUserId = GetUserId();
            if (!string.IsNullOrWhiteSpace(currentUserId))
            {
                userIds.Add(currentUserId);
            }

            if (matter == null || string.IsNullOrWhiteSpace(matter.ResponsibleAttorney))
            {
                return userIds;
            }

            var responsibleAttorney = matter.ResponsibleAttorney.Trim();

            var directUserIds = await TenantScope(_context.Users)
                .AsNoTracking()
                .Where(u => u.Id == responsibleAttorney || u.Name == responsibleAttorney)
                .Select(u => u.Id)
                .ToListAsync();

            foreach (var userId in directUserIds)
            {
                userIds.Add(userId);
            }

            var employeeUserIds = await TenantScope(_context.Employees)
                .AsNoTracking()
                .Where(e =>
                    e.Id == responsibleAttorney ||
                    ((e.FirstName + " " + e.LastName) == responsibleAttorney))
                .Where(e => e.UserId != null)
                .Select(e => e.UserId!)
                .ToListAsync();

            foreach (var userId in employeeUserIds)
            {
                userIds.Add(userId);
            }

            return userIds;
        }

        private async Task<Matter?> ResolveMatterAsync(string? matterId)
        {
            if (string.IsNullOrWhiteSpace(matterId))
            {
                return null;
            }

            var normalizedMatterId = matterId.Trim();
            return await TenantScope(_context.Matters)
                .FirstOrDefaultAsync(m => m.Id == normalizedMatterId);
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

        private string? GetUserId()
        {
            return User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                   ?? User.FindFirst("sub")?.Value;
        }

        private static int NormalizeLimit(int limit)
        {
            if (limit <= 0)
            {
                return DefaultListLimit;
            }

            return Math.Clamp(limit, 1, MaxListLimit);
        }

        private static DateTime NormalizeIncomingDate(DateTime date)
        {
            if (date.Kind == DateTimeKind.Utc)
            {
                return date;
            }

            if (date.Kind == DateTimeKind.Local)
            {
                return date.ToUniversalTime();
            }

            return DateTime.SpecifyKind(date, DateTimeKind.Utc);
        }

        private static CalendarEventResponseDto MapEvent(CalendarEvent evt, string? matterName)
        {
            return new CalendarEventResponseDto
            {
                Id = evt.Id,
                Title = evt.Title,
                Date = evt.Date,
                Type = evt.Type,
                Description = evt.Description,
                Location = evt.Location,
                RecurrencePattern = evt.RecurrencePattern,
                Duration = evt.Duration,
                ReminderMinutes = evt.ReminderMinutes,
                ReminderSent = evt.ReminderSent,
                RowVersion = evt.RowVersion,
                MatterId = evt.MatterId,
                MatterName = matterName,
                CreatedAt = evt.CreatedAt,
                UpdatedAt = evt.UpdatedAt
            };
        }

        private static CalendarEventValidationResult ValidateAndNormalizeDto(CalendarEventDto dto)
        {
            var title = string.IsNullOrWhiteSpace(dto.Title) ? null : dto.Title.Trim();
            if (string.IsNullOrWhiteSpace(title))
            {
                return CalendarEventValidationResult.Fail("Title is required.");
            }

            if (dto.Date == default)
            {
                return CalendarEventValidationResult.Fail("Date is required.");
            }

            var normalizedType = string.IsNullOrWhiteSpace(dto.Type) ? "Meeting" : dto.Type.Trim();
            if (!AllowedEventTypes.Contains(normalizedType))
            {
                return CalendarEventValidationResult.Fail("Event type is invalid.");
            }

            var duration = dto.Duration ?? 60;
            if (duration < 1 || duration > 1440)
            {
                return CalendarEventValidationResult.Fail("Duration must be between 1 and 1440 minutes.");
            }

            var reminderMinutes = dto.ReminderMinutes ?? 30;
            if (reminderMinutes < 0 || reminderMinutes > 10080)
            {
                return CalendarEventValidationResult.Fail("ReminderMinutes must be between 0 and 10080.");
            }

            return CalendarEventValidationResult.Success(
                title,
                NormalizeIncomingDate(dto.Date),
                normalizedType,
                NormalizeOptionalText(dto.Description, 2000),
                NormalizeOptionalText(dto.Location, 512),
                NormalizeOptionalText(dto.RecurrencePattern, 128),
                duration,
                reminderMinutes,
                NormalizeOptionalText(dto.MatterId, 128));
        }

        private static string? NormalizeOptionalText(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var trimmed = value.Trim();
            return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
        }
    }

    // DTO
    public class CalendarEventDto
    {
        public string Title { get; set; } = string.Empty;
        public DateTime Date { get; set; }
        public string? Type { get; set; }
        public string? Description { get; set; }
        public string? Location { get; set; }
        public string? RecurrencePattern { get; set; }
        public int? Duration { get; set; }
        public int? ReminderMinutes { get; set; }
        public string? MatterId { get; set; }
        public string? RowVersion { get; set; }
    }

    public class CalendarEventResponseDto
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public DateTime Date { get; set; }
        public string Type { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? Location { get; set; }
        public string? RecurrencePattern { get; set; }
        public int Duration { get; set; }
        public int ReminderMinutes { get; set; }
        public bool ReminderSent { get; set; }
        public string RowVersion { get; set; } = string.Empty;
        public string? MatterId { get; set; }
        public string? MatterName { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    internal sealed class CalendarEventValidationResult
    {
        public bool IsValid { get; private init; }
        public string? ErrorMessage { get; private init; }
        public string? Title { get; private init; }
        public DateTime Date { get; private init; }
        public string? Type { get; private init; }
        public string? Description { get; private init; }
        public string? Location { get; private init; }
        public string? RecurrencePattern { get; private init; }
        public int Duration { get; private init; }
        public int ReminderMinutes { get; private init; }
        public string? MatterId { get; private init; }

        public static CalendarEventValidationResult Fail(string errorMessage)
        {
            return new CalendarEventValidationResult
            {
                IsValid = false,
                ErrorMessage = errorMessage
            };
        }

        public static CalendarEventValidationResult Success(
            string title,
            DateTime date,
            string type,
            string? description,
            string? location,
            string? recurrencePattern,
            int duration,
            int reminderMinutes,
            string? matterId)
        {
            return new CalendarEventValidationResult
            {
                IsValid = true,
                Title = title,
                Date = date,
                Type = type,
                Description = description,
                Location = location,
                RecurrencePattern = recurrencePattern,
                Duration = duration,
                ReminderMinutes = reminderMinutes,
                MatterId = matterId
            };
        }
    }
}
