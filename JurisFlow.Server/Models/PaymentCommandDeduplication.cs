using System.ComponentModel.DataAnnotations;

namespace JurisFlow.Server.Models
{
    public class PaymentCommandDeduplication
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(64)]
        public string CommandName { get; set; } = string.Empty;

        [Required]
        [MaxLength(128)]
        public string ActorUserId { get; set; } = string.Empty;

        [Required]
        [MaxLength(160)]
        public string IdempotencyKey { get; set; } = string.Empty;

        [Required]
        [MaxLength(64)]
        public string RequestFingerprint { get; set; } = string.Empty;

        [Required]
        [MaxLength(24)]
        public string Status { get; set; } = "in_progress";

        [MaxLength(64)]
        public string? ResultEntityType { get; set; }

        [MaxLength(128)]
        public string? ResultEntityId { get; set; }

        public int? ResultStatusCode { get; set; }

        [MaxLength(128)]
        public string? CorrelationId { get; set; }

        [MaxLength(64)]
        public string? ErrorCode { get; set; }

        public string? ResponsePayloadJson { get; set; }

        [MaxLength(64)]
        public string? ResponseContentType { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAt { get; set; }
    }
}
