using System.ComponentModel.DataAnnotations;

namespace JurisFlow.Server.Models
{
    public class IntegrationOutboxEvent
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [MaxLength(128)]
        public string? ConnectionId { get; set; }

        [MaxLength(128)]
        public string? RunId { get; set; }

        [Required]
        [MaxLength(128)]
        public string ProviderKey { get; set; } = string.Empty;

        [Required]
        [MaxLength(64)]
        public string EventType { get; set; } = "integration.run.completed";

        [MaxLength(64)]
        public string? EntityType { get; set; }

        [MaxLength(128)]
        public string? EntityId { get; set; }

        [Required]
        [MaxLength(160)]
        public string IdempotencyKey { get; set; } = string.Empty;

        [MaxLength(128)]
        public string? CorrelationId { get; set; }

        [Required]
        [MaxLength(32)]
        public string Status { get; set; } = "pending";

        public int AttemptCount { get; set; }
        public DateTime? NextAttemptAt { get; set; }
        public DateTime? DispatchedAt { get; set; }

        public string? HeadersJson { get; set; }
        public string? PayloadJson { get; set; }
        public string? MetadataJson { get; set; }

        [MaxLength(64)]
        public string? ErrorCode { get; set; }

        [MaxLength(2048)]
        public string? ErrorMessage { get; set; }

        public bool DeadLettered { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
