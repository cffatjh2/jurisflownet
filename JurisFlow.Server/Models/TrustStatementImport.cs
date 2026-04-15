using System.ComponentModel.DataAnnotations;

namespace JurisFlow.Server.Models
{
    public class TrustStatementImport
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(128)]
        public string TrustAccountId { get; set; } = string.Empty;

        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }
        public decimal StatementEndingBalance { get; set; }

        [MaxLength(24)]
        public string Status { get; set; } = "imported";

        [MaxLength(64)]
        public string Source { get; set; } = "manual";

        [MaxLength(256)]
        public string? SourceFileName { get; set; }

        [MaxLength(128)]
        public string? SourceFileHash { get; set; }

        [MaxLength(256)]
        public string? SourceEvidenceKey { get; set; }

        [MaxLength(128)]
        public string? ImportFingerprint { get; set; }

        [MaxLength(128)]
        public string? DuplicateOfStatementImportId { get; set; }

        [MaxLength(128)]
        public string? SupersededByStatementImportId { get; set; }

        [MaxLength(128)]
        public string? SupersededBy { get; set; }

        public DateTime? SupersededAt { get; set; }

        public long? SourceFileSizeBytes { get; set; }

        [MaxLength(3)]
        public string Currency { get; set; } = "USD";

        [MaxLength(128)]
        public string? ImportedBy { get; set; }

        public int LineCount { get; set; }
        public string? Notes { get; set; }
        public string? MetadataJson { get; set; }

        public DateTime ImportedAt { get; set; } = DateTime.UtcNow;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class TrustStatementLine
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(128)]
        public string TrustStatementImportId { get; set; } = string.Empty;

        [Required]
        [MaxLength(128)]
        public string TrustAccountId { get; set; } = string.Empty;

        public DateTime PostedAt { get; set; }
        public DateTime? EffectiveAt { get; set; }
        public decimal Amount { get; set; }
        public decimal? BalanceAfter { get; set; }

        [MaxLength(128)]
        public string? Reference { get; set; }

        [MaxLength(128)]
        public string? CheckNumber { get; set; }

        [MaxLength(2048)]
        public string? Description { get; set; }

        [MaxLength(256)]
        public string? Counterparty { get; set; }

        [MaxLength(24)]
        public string MatchStatus { get; set; } = "unmatched";

        [MaxLength(32)]
        public string MatchMethod { get; set; } = "none";

        public decimal? MatchConfidence { get; set; }

        [MaxLength(128)]
        public string? MatchedTrustTransactionId { get; set; }

        [MaxLength(128)]
        public string? MatchedBy { get; set; }

        public DateTime? MatchedAt { get; set; }

        public string? MatchNotes { get; set; }

        [MaxLength(128)]
        public string? ExternalLineId { get; set; }

        public string? MetadataJson { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
