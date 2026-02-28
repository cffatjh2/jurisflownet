using System.ComponentModel.DataAnnotations;

namespace JurisFlow.Server.Models
{
    public class BillingEbillingTransmission
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(64)]
        public string ProviderKey { get; set; } = "unknown";

        [MaxLength(128)]
        public string? InvoiceId { get; set; }

        [MaxLength(128)]
        public string? MatterId { get; set; }

        [MaxLength(128)]
        public string? ClientId { get; set; }

        [MaxLength(128)]
        public string? PayorClientId { get; set; }

        [MaxLength(128)]
        public string? PrebillBatchId { get; set; }

        [MaxLength(32)]
        public string? Format { get; set; }

        [MaxLength(32)]
        public string Status { get; set; } = "queued"; // queued | submitted | accepted | rejected | error | partial

        [MaxLength(255)]
        public string? ExternalTransmissionId { get; set; }

        [MaxLength(128)]
        public string? CorrelationId { get; set; }

        [MaxLength(255)]
        public string? Reference { get; set; }

        [MaxLength(128)]
        public string? ErrorCode { get; set; }

        [MaxLength(2048)]
        public string? ErrorMessage { get; set; }

        public DateTime? SubmittedAt { get; set; }
        public DateTime? CompletedAt { get; set; }

        public string? RequestPayloadJson { get; set; }
        public string? ResponsePayloadJson { get; set; }
        public string? MetadataJson { get; set; }

        [MaxLength(128)]
        public string? CreatedBy { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class BillingEbillingResultEvent
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [MaxLength(128)]
        public string? TransmissionId { get; set; }

        [Required]
        [MaxLength(64)]
        public string ProviderKey { get; set; } = "unknown";

        [MaxLength(255)]
        public string? ExternalTransmissionId { get; set; }

        [MaxLength(255)]
        public string? ExternalEventId { get; set; }

        [Required]
        [MaxLength(48)]
        public string EventType { get; set; } = "submission_result"; // submission_result | provider_error | status_update | ack

        [Required]
        [MaxLength(32)]
        public string Status { get; set; } = "received"; // received | accepted | rejected | error | warning

        [MaxLength(128)]
        public string? InvoiceId { get; set; }

        [MaxLength(128)]
        public string? MatterId { get; set; }

        [MaxLength(128)]
        public string? ClientId { get; set; }

        [MaxLength(128)]
        public string? PayorClientId { get; set; }

        [MaxLength(128)]
        public string? ResultCode { get; set; }

        [MaxLength(2048)]
        public string? ResultMessage { get; set; }

        [MaxLength(128)]
        public string? ErrorCode { get; set; }

        [MaxLength(64)]
        public string? ErrorCategory { get; set; }

        [MaxLength(2048)]
        public string? ErrorMessage { get; set; }

        public bool IsFinal { get; set; } = false;
        public bool IsRetryable { get; set; } = false;

        public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
        public DateTime RecordedAt { get; set; } = DateTime.UtcNow;

        public string? PayloadJson { get; set; }
        public string? MetadataJson { get; set; }

        [MaxLength(128)]
        public string? RecordedBy { get; set; }
    }
}
