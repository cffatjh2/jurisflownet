using JurisFlow.Server.Contracts;

namespace JurisFlow.Server.Services
{
    public sealed class CalendarEventRequestValidator
    {
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

        public ApplicationServiceResult<CalendarEventWriteModel> Validate(CalendarEventRequest request)
        {
            var title = NormalizeOptional(request.Title, 256);
            if (string.IsNullOrWhiteSpace(title))
            {
                return ApplicationServiceResult<CalendarEventWriteModel>.Failure(StatusCodes.Status400BadRequest, "Invalid event", "Title is required.");
            }

            if (request.Date == default)
            {
                return ApplicationServiceResult<CalendarEventWriteModel>.Failure(StatusCodes.Status400BadRequest, "Invalid event", "Date is required.");
            }

            var type = string.IsNullOrWhiteSpace(request.Type) ? "Meeting" : request.Type.Trim();
            if (!AllowedEventTypes.Contains(type))
            {
                return ApplicationServiceResult<CalendarEventWriteModel>.Failure(StatusCodes.Status400BadRequest, "Invalid event", "Event type is invalid.");
            }

            var duration = request.Duration ?? 60;
            if (duration < 1 || duration > 1440)
            {
                return ApplicationServiceResult<CalendarEventWriteModel>.Failure(StatusCodes.Status400BadRequest, "Invalid event", "Duration must be between 1 and 1440 minutes.");
            }

            var reminderMinutes = request.ReminderMinutes ?? 30;
            if (reminderMinutes < 0 || reminderMinutes > 10080)
            {
                return ApplicationServiceResult<CalendarEventWriteModel>.Failure(StatusCodes.Status400BadRequest, "Invalid event", "ReminderMinutes must be between 0 and 10080.");
            }

            return ApplicationServiceResult<CalendarEventWriteModel>.Success(new CalendarEventWriteModel
            {
                Title = title!,
                Date = NormalizeIncomingDate(request.Date),
                Type = type,
                Description = NormalizeOptional(request.Description, 2000),
                Location = NormalizeOptional(request.Location, 512),
                RecurrencePattern = NormalizeOptional(request.RecurrencePattern, 128),
                Duration = duration,
                ReminderMinutes = reminderMinutes,
                MatterId = NormalizeOptional(request.MatterId, 128),
                RowVersion = NormalizeOptional(request.RowVersion, 64)
            });
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

        private static string? NormalizeOptional(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var trimmed = value.Trim();
            return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
        }
    }

    public sealed class CalendarEventWriteModel
    {
        public string Title { get; init; } = string.Empty;
        public DateTime Date { get; init; }
        public string Type { get; init; } = "Meeting";
        public string? Description { get; init; }
        public string? Location { get; init; }
        public string? RecurrencePattern { get; init; }
        public int Duration { get; init; }
        public int ReminderMinutes { get; init; }
        public string? MatterId { get; init; }
        public string? RowVersion { get; init; }
    }
}
