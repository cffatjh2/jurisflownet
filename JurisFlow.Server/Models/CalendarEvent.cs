using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace JurisFlow.Server.Models
{
    public class CalendarEvent
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        public string Title { get; set; } = string.Empty;

        [Required]
        public DateTime Date { get; set; }

        // Extended event types
        public string Type { get; set; } = "Meeting"; // Meeting, Court, Deadline, Deposition, Consultation, Filing, Hearing, Trial, Conference, Other

        // Optional fields
        public string? Description { get; set; }
        public string? Location { get; set; }

        // Recurrence
        public string? RecurrencePattern { get; set; } // none, daily, weekly, monthly

        // Duration in minutes
        public int Duration { get; set; } = 60;

        // Reminder
        public int ReminderMinutes { get; set; } = 30;
        public bool ReminderSent { get; set; } = false;

        [Required]
        [MaxLength(32)]
        [ConcurrencyCheck]
        public string RowVersion { get; set; } = Guid.NewGuid().ToString("N");

        // Matter association
        public string? MatterId { get; set; }

        [ForeignKey("MatterId")]
        public virtual Matter? Matter { get; set; }

        // Timestamps
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
