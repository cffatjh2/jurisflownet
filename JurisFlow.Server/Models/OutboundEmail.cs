using System;
using System.ComponentModel.DataAnnotations;

namespace JurisFlow.Server.Models
{
    public class OutboundEmail
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public string ToAddress { get; set; } = string.Empty;
        public string? FromAddress { get; set; }
        public string Subject { get; set; } = string.Empty;
        public string? BodyText { get; set; }
        public string? BodyHtml { get; set; }

        public string Status { get; set; } = "Queued"; // Queued, Sent, Failed
        public string? ErrorMessage { get; set; }

        public DateTime ScheduledFor { get; set; } = DateTime.UtcNow;
        public DateTime? SentAt { get; set; }

        public string? RelatedEntityType { get; set; }
        public string? RelatedEntityId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
