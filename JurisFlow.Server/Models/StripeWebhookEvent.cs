using System.ComponentModel.DataAnnotations;

namespace JurisFlow.Server.Models
{
    public class StripeWebhookEvent
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(128)]
        public string EventId { get; set; } = string.Empty;

        [Required]
        [MaxLength(96)]
        public string EventType { get; set; } = string.Empty;

        [Required]
        [MaxLength(32)]
        public string Status { get; set; } = "processed";

        [MaxLength(1024)]
        public string? ErrorMessage { get; set; }

        public DateTime? ProcessedAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
