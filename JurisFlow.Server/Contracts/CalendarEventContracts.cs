using System.ComponentModel.DataAnnotations;
using JurisFlow.Server.Models;

namespace JurisFlow.Server.Contracts
{
    public sealed class CalendarEventRequest : RejectUnknownFieldsRequestBase
    {
        [Required]
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

    public sealed class CalendarEventResponse
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

        public static CalendarEventResponse FromModel(CalendarEvent evt, string? matterName = null)
        {
            return new CalendarEventResponse
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
    }
}
