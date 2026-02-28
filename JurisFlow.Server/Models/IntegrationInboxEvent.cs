using System.ComponentModel.DataAnnotations;

namespace JurisFlow.Server.Models
{
    public class IntegrationInboxEvent
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
        [MaxLength(160)]
        public string ExternalEventId { get; set; } = string.Empty;

        [MaxLength(128)]
        public string? CorrelationId { get; set; }

        [MaxLength(64)]
        public string? EventType { get; set; }

        [Required]
        [MaxLength(32)]
        public string Status { get; set; } = "pending";

        public bool SignatureValidated { get; set; }

        [MaxLength(64)]
        public string? PayloadHash { get; set; }

        public string? HeadersJson { get; set; }
        public string? PayloadJson { get; set; }
        public string? MetadataJson { get; set; }

        [MaxLength(2048)]
        public string? ErrorMessage { get; set; }

        public int ReplayCount { get; set; }
        public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ProcessedAt { get; set; }
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
