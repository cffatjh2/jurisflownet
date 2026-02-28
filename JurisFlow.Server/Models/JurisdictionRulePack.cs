using System.ComponentModel.DataAnnotations;

namespace JurisFlow.Server.Models
{
    public class JurisdictionRulePack
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(160)]
        public string ScopeKey { get; set; } = string.Empty; // normalized composite key

        [Required]
        [MaxLength(32)]
        public string JurisdictionCode { get; set; } = string.Empty;

        [MaxLength(64)]
        public string? CourtSystem { get; set; }

        [MaxLength(128)]
        public string? CourtDivision { get; set; }

        [MaxLength(128)]
        public string? Venue { get; set; }

        [MaxLength(64)]
        public string? CaseType { get; set; }

        [MaxLength(64)]
        public string? FilingMethod { get; set; } // e_filing, paper, hybrid

        [Required]
        [MaxLength(160)]
        public string Name { get; set; } = string.Empty;

        public int Version { get; set; } = 1;

        [Required]
        [MaxLength(24)]
        public string Status { get; set; } = "draft"; // draft | in_review | published | retired

        public DateTime EffectiveFrom { get; set; } = DateTime.UtcNow.Date;
        public DateTime? EffectiveTo { get; set; }

        [MaxLength(32)]
        public string ConfidenceLevel { get; set; } = "medium"; // low | medium | high

        public decimal ConfidenceScore { get; set; } = 0.5m;

        [MaxLength(2048)]
        public string? SourceCitation { get; set; }

        [MaxLength(128)]
        public string? SourceReferenceId { get; set; }

        public string? DocumentRulesJson { get; set; }
        public string? FeeRulesJson { get; set; }
        public string? ServiceRulesJson { get; set; }
        public string? DeadlineRulesJson { get; set; }
        public string? LocalOverridesJson { get; set; }
        public string? ValidationRulesJson { get; set; }
        public string? MetadataJson { get; set; }

        [MaxLength(128)]
        public string? SubmittedForReviewBy { get; set; }
        public DateTime? SubmittedForReviewAt { get; set; }

        [MaxLength(128)]
        public string? PublishedBy { get; set; }
        public DateTime? PublishedAt { get; set; }

        [MaxLength(128)]
        public string? SupersededByRulePackId { get; set; }

        [MaxLength(2048)]
        public string? ReviewNotes { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}

