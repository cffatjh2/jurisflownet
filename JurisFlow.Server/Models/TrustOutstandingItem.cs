using System.ComponentModel.DataAnnotations;

namespace JurisFlow.Server.Models
{
    public class TrustOutstandingItem
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(128)]
        public string TrustAccountId { get; set; } = string.Empty;

        [MaxLength(128)]
        public string? TrustTransactionId { get; set; }

        [MaxLength(128)]
        public string? ClientTrustLedgerId { get; set; }

        [MaxLength(128)]
        public string? TrustStatementImportId { get; set; }

        [MaxLength(128)]
        public string? TrustStatementLineId { get; set; }

        [MaxLength(128)]
        public string? TrustReconciliationPacketId { get; set; }

        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }
        public DateTime OccurredAt { get; set; }

        [Required]
        [MaxLength(48)]
        public string ItemType { get; set; } = "other_adjustment";

        [Required]
        [MaxLength(24)]
        public string ImpactDirection { get; set; } = "decrease_bank";

        [Required]
        [MaxLength(24)]
        public string Status { get; set; } = "open";

        [Required]
        [MaxLength(24)]
        public string Source { get; set; } = "manual";

        public decimal Amount { get; set; }

        [MaxLength(256)]
        public string? CorrelationKey { get; set; }

        [MaxLength(128)]
        public string? Reference { get; set; }

        [MaxLength(2048)]
        public string? Description { get; set; }

        [MaxLength(64)]
        public string? ReasonCode { get; set; }

        [MaxLength(256)]
        public string? AttachmentEvidenceKey { get; set; }

        [MaxLength(128)]
        public string? CreatedBy { get; set; }

        [MaxLength(128)]
        public string? ResolvedBy { get; set; }

        public DateTime? ResolvedAt { get; set; }
        public string? MetadataJson { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
