using System.ComponentModel.DataAnnotations;

namespace JurisFlow.Server.Models
{
    public class TrustPostingBatch
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(128)]
        public string TrustTransactionId { get; set; } = string.Empty;

        [Required]
        [MaxLength(128)]
        public string TrustAccountId { get; set; } = string.Empty;

        [Required]
        [MaxLength(32)]
        public string BatchType { get; set; } = "posting"; // posting | clearance | reversal

        [MaxLength(128)]
        public string? ParentPostingBatchId { get; set; }

        [MaxLength(128)]
        public string? CreatedBy { get; set; }

        public int JournalEntryCount { get; set; }
        public decimal TotalAmount { get; set; }
        public DateTime EffectiveAt { get; set; } = DateTime.UtcNow;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
