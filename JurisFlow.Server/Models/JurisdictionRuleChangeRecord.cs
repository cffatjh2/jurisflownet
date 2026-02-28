using System.ComponentModel.DataAnnotations;

namespace JurisFlow.Server.Models
{
    public class JurisdictionRuleChangeRecord
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [MaxLength(128)]
        public string? RulePackId { get; set; }

        [MaxLength(128)]
        public string? CoverageEntryId { get; set; }

        [Required]
        [MaxLength(32)]
        public string JurisdictionCode { get; set; } = string.Empty;

        [MaxLength(64)]
        public string? CourtSystem { get; set; }

        [MaxLength(64)]
        public string? CaseType { get; set; }

        [MaxLength(64)]
        public string? FilingMethod { get; set; }

        [Required]
        [MaxLength(32)]
        public string ChangeType { get; set; } = "detected"; // detected | review_submitted | approved | published | rejected

        [Required]
        [MaxLength(24)]
        public string Status { get; set; } = "open"; // open | in_review | published | rejected | closed

        [MaxLength(16)]
        public string Severity { get; set; } = "medium";

        [MaxLength(2048)]
        public string? Summary { get; set; }

        [MaxLength(2048)]
        public string? SourceCitation { get; set; }

        public string? DiffJson { get; set; }
        public string? SourcePayloadJson { get; set; }

        [MaxLength(128)]
        public string? CreatedBy { get; set; }

        [MaxLength(128)]
        public string? ReviewedBy { get; set; }
        public DateTime? ReviewedAt { get; set; }

        [MaxLength(2048)]
        public string? ReviewNotes { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}

