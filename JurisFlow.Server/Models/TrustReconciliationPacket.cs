using System.ComponentModel.DataAnnotations;

namespace JurisFlow.Server.Models
{
    public class TrustReconciliationPacket
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(128)]
        public string TrustAccountId { get; set; } = string.Empty;

        [MaxLength(128)]
        public string? StatementImportId { get; set; }

        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }

        public decimal StatementEndingBalance { get; set; }
        public decimal AdjustedBankBalance { get; set; }
        public decimal JournalBalance { get; set; }
        public decimal ClientLedgerBalance { get; set; }
        public decimal OutstandingDepositsTotal { get; set; }
        public decimal OutstandingChecksTotal { get; set; }
        public decimal OtherAdjustmentsTotal { get; set; }
        public int ExceptionCount { get; set; }
        public int MatchedStatementLineCount { get; set; }
        public int UnmatchedStatementLineCount { get; set; }
        public int VersionNumber { get; set; } = 1;

        public bool IsCanonical { get; set; } = true;

        [Required]
        [MaxLength(24)]
        public string Status { get; set; } = "draft";

        [MaxLength(128)]
        public string? PreparedBy { get; set; }

        [MaxLength(128)]
        public string? SupersededByPacketId { get; set; }
        [MaxLength(128)]
        public string? SupersededBy { get; set; }
        [MaxLength(2048)]
        public string? SupersedeReason { get; set; }
        public DateTime? SupersededAt { get; set; }
        public DateTime PreparedAt { get; set; } = DateTime.UtcNow;

        public string? Notes { get; set; }
        public string? PayloadJson { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class TrustReconciliationSignoff
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(128)]
        public string TrustReconciliationPacketId { get; set; } = string.Empty;

        [Required]
        [MaxLength(128)]
        public string SignedBy { get; set; } = string.Empty;

        [MaxLength(64)]
        public string? SignerRole { get; set; }

        [MaxLength(24)]
        public string Status { get; set; } = "signed_off";

        public string? Notes { get; set; }
        public DateTime SignedAt { get; set; } = DateTime.UtcNow;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
