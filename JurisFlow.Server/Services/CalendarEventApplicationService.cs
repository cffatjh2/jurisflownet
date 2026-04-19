using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using JurisFlow.Server.Contracts;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using ThreadingTask = System.Threading.Tasks.Task;

namespace JurisFlow.Server.Services
{
    public sealed class CalendarEventApplicationService
    {
        private const int DefaultListLimit = 100;
        private const int MaxListLimit = 200;

        private readonly JurisFlowDbContext _context;
        private readonly AuditLogger _auditLogger;
        private readonly TenantContext _tenantContext;
        private readonly CalendarEventRequestValidator _validator;
        private readonly LegacyMatterResponsibilityAdapter _responsibilityAdapter;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public CalendarEventApplicationService(
            JurisFlowDbContext context,
            AuditLogger auditLogger,
            TenantContext tenantContext,
            CalendarEventRequestValidator validator,
            LegacyMatterResponsibilityAdapter responsibilityAdapter,
            IHttpContextAccessor httpContextAccessor)
        {
            _context = context;
            _auditLogger = auditLogger;
            _tenantContext = tenantContext;
            _validator = validator;
            _responsibilityAdapter = responsibilityAdapter;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task<IReadOnlyList<CalendarEventResponse>> GetEventsAsync(string? matterId, DateTime? from, DateTime? to, int limit)
        {
            var normalizedLimit = NormalizeLimit(limit);
            var query = TenantScope(_context.CalendarEvents).AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(matterId))
            {
                query = query.Where(e => e.MatterId == matterId.Trim());
            }

            if (from.HasValue)
            {
                query = query.Where(e => e.Date >= NormalizeIncomingDate(from.Value));
            }

            if (to.HasValue)
            {
                query = query.Where(e => e.Date <= NormalizeIncomingDate(to.Value));
            }

            var items = await query
                .OrderBy(e => e.Date)
                .Take(normalizedLimit)
                .Select(e => new CalendarEventResponse
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

            return items;
        }

        public async Task<CalendarEventResponse?> GetEventAsync(string id)
        {
            return await TenantScope(_context.CalendarEvents)
                .AsNoTracking()
                .Where(e => e.Id == id)
                .Select(e => new CalendarEventResponse
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
        }

        public async Task<ApplicationServiceResult<CalendarEventResponse>> CreateEventAsync(CalendarEventRequest request)
        {
            var validation = _validator.Validate(request);
            if (!validation.Succeeded || validation.Value == null)
            {
                return ApplicationServiceResult<CalendarEventResponse>.Failure(validation.StatusCode, validation.Title!, validation.Detail!);
            }

            var matter = await ResolveMatterAsync(validation.Value.MatterId);
            if (validation.Value.MatterId != null && matter == null)
            {
                return ApplicationServiceResult<CalendarEventResponse>.Failure(StatusCodes.Status400BadRequest, "Invalid event", "Matter not found.");
            }

            var calendarEvent = new CalendarEvent
            {
                Id = Guid.NewGuid().ToString(),
                Title = validation.Value.Title,
                Date = validation.Value.Date,
                Type = validation.Value.Type,
                Description = validation.Value.Description,
                Location = validation.Value.Location,
                RecurrencePattern = validation.Value.RecurrencePattern,
                Duration = validation.Value.Duration,
                ReminderMinutes = validation.Value.ReminderMinutes,
                ReminderSent = false,
                MatterId = validation.Value.MatterId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.CalendarEvents.Add(calendarEvent);
            await TryCreateUpcomingNotificationAsync(calendarEvent, matter);
            await _context.SaveChangesAsync();

            await LogAuditAsync("event.create", nameof(CalendarEvent), calendarEvent.Id, $"Type={calendarEvent.Type}, MatterId={calendarEvent.MatterId}, ReminderSent={calendarEvent.ReminderSent}");

            return ApplicationServiceResult<CalendarEventResponse>.Success(CalendarEventResponse.FromModel(calendarEvent, matter?.Name));
        }

        public async Task<ApplicationServiceResult<CalendarEventResponse>> UpdateEventAsync(string id, CalendarEventRequest request)
        {
            var calendarEvent = await TenantScope(_context.CalendarEvents).FirstOrDefaultAsync(e => e.Id == id);
            if (calendarEvent == null)
            {
                return ApplicationServiceResult<CalendarEventResponse>.Failure(StatusCodes.Status404NotFound, "Event not found", "Event was not found.");
            }

            var validation = _validator.Validate(request);
            if (!validation.Succeeded || validation.Value == null)
            {
                return ApplicationServiceResult<CalendarEventResponse>.Failure(validation.StatusCode, validation.Title!, validation.Detail!);
            }

            if (string.IsNullOrWhiteSpace(validation.Value.RowVersion))
            {
                return ApplicationServiceResult<CalendarEventResponse>.Failure(StatusCodes.Status400BadRequest, "Invalid event", "RowVersion is required.");
            }

            if (!string.Equals(calendarEvent.RowVersion, validation.Value.RowVersion, StringComparison.Ordinal))
            {
                return ApplicationServiceResult<CalendarEventResponse>.Failure(StatusCodes.Status409Conflict, "Concurrency conflict", "The event was modified by another user. Reload and try again.");
            }

            var matter = await ResolveMatterAsync(validation.Value.MatterId);
            if (validation.Value.MatterId != null && matter == null)
            {
                return ApplicationServiceResult<CalendarEventResponse>.Failure(StatusCodes.Status400BadRequest, "Invalid event", "Matter not found.");
            }

            calendarEvent.Title = validation.Value.Title;
            calendarEvent.Date = validation.Value.Date;
            calendarEvent.Type = validation.Value.Type;
            calendarEvent.Description = validation.Value.Description;
            calendarEvent.Location = validation.Value.Location;
            calendarEvent.RecurrencePattern = validation.Value.RecurrencePattern;
            calendarEvent.Duration = validation.Value.Duration;
            calendarEvent.ReminderMinutes = validation.Value.ReminderMinutes;
            calendarEvent.MatterId = validation.Value.MatterId;
            calendarEvent.UpdatedAt = DateTime.UtcNow;
            calendarEvent.RowVersion = Guid.NewGuid().ToString("N");

            await TryCreateUpcomingNotificationAsync(calendarEvent, matter);

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                return ApplicationServiceResult<CalendarEventResponse>.Failure(StatusCodes.Status409Conflict, "Concurrency conflict", "The event was modified by another user. Reload and try again.");
            }

            await LogAuditAsync("event.update", nameof(CalendarEvent), calendarEvent.Id, $"Type={calendarEvent.Type}, MatterId={calendarEvent.MatterId}, ReminderSent={calendarEvent.ReminderSent}");

            return ApplicationServiceResult<CalendarEventResponse>.Success(CalendarEventResponse.FromModel(calendarEvent, matter?.Name));
        }

        public async Task<ApplicationServiceResult<object>> DeleteEventAsync(string id)
        {
            var calendarEvent = await TenantScope(_context.CalendarEvents).FirstOrDefaultAsync(e => e.Id == id);
            if (calendarEvent == null)
            {
                return ApplicationServiceResult<object>.Failure(StatusCodes.Status404NotFound, "Event not found", "Event was not found.");
            }

            _context.CalendarEvents.Remove(calendarEvent);
            await _context.SaveChangesAsync();
            await LogAuditAsync("event.delete", nameof(CalendarEvent), id, $"Deleted event {calendarEvent.Title}");

            return ApplicationServiceResult<object>.Success(new object());
        }

        private async ThreadingTask TryCreateUpcomingNotificationAsync(CalendarEvent evt, Matter? matter)
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

            var userIds = await _responsibilityAdapter.ResolveNotificationTargetsAsync(matter, GetUserId());
            if (userIds.Count == 0)
            {
                return;
            }

            var title = "Upcoming event";
            var message = $"{evt.Title} - scheduled for {evt.Date.ToUniversalTime():O}";

            foreach (var userId in userIds)
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

        private async Task<Matter?> ResolveMatterAsync(string? matterId)
        {
            if (string.IsNullOrWhiteSpace(matterId))
            {
                return null;
            }

            return await TenantScope(_context.Matters).FirstOrDefaultAsync(m => m.Id == matterId);
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

        private static int NormalizeLimit(int limit)
        {
            if (limit <= 0)
            {
                return DefaultListLimit;
            }

            return Math.Clamp(limit, 1, MaxListLimit);
        }

        private string? GetUserId()
        {
            var user = _httpContextAccessor.HttpContext?.User;
            return user?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user?.FindFirst("sub")?.Value;
        }

        private ThreadingTask LogAuditAsync(string action, string entityType, string entityId, string details)
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext == null)
            {
                return ThreadingTask.CompletedTask;
            }

            return _auditLogger.LogAsync(httpContext, action, entityType, entityId, details);
        }
    }
}
