using System.ComponentModel.DataAnnotations;

namespace JurisFlow.Server.Models
{
    public class TrustJournalEntry
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(128)]
        public string TrustTransactionId { get; set; } = string.Empty;

        [Required]
        [MaxLength(128)]
        public string PostingBatchId { get; set; } = string.Empty;

        [Required]
        [MaxLength(128)]
        public string TrustAccountId { get; set; } = string.Empty;

        [MaxLength(128)]
        public string? ClientTrustLedgerId { get; set; }

        [MaxLength(128)]
        public string? MatterId { get; set; }

        [Required]
        [MaxLength(24)]
        public string EntryKind { get; set; } = "posting"; // posting | reversal | clearance

        [Required]
        [MaxLength(32)]
        public string OperationType { get; set; } = "deposit"; // deposit | withdrawal | earned_fee_transfer | adjustment | clearance | return

        public decimal Amount { get; set; }

        [MaxLength(3)]
        public string Currency { get; set; } = "USD";

        [Required]
        [MaxLength(24)]
        public string AvailabilityClass { get; set; } = "cleared"; // cleared | uncleared

        [MaxLength(128)]
        public string? ReversalOfTrustJournalEntryId { get; set; }

        [MaxLength(256)]
        public string? CorrelationKey { get; set; }

        [MaxLength(2048)]
        public string? Description { get; set; }

        public string? MetadataJson { get; set; }

        [MaxLength(128)]
        public string? CreatedBy { get; set; }

        public DateTime EffectiveAt { get; set; } = DateTime.UtcNow;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
