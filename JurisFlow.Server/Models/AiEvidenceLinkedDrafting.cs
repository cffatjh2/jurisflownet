using System.ComponentModel.DataAnnotations;

namespace JurisFlow.Server.Models
{
    public class AiDraftSession
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [MaxLength(128)]
        public string? MatterId { get; set; }

        [MaxLength(128)]
        public string? UserId { get; set; }

        [MaxLength(255)]
        public string? Title { get; set; }

        [MaxLength(64)]
        public string? Purpose { get; set; } // motion_draft | demand_letter | filing_narrative | memo | other

        [MaxLength(24)]
        public string Status { get; set; } = "draft"; // draft | generated | review_required | published | archived

        public string? JurisdictionContextJson { get; set; }
        public string? MetadataJson { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class AiDraftOutput
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(128)]
        public string SessionId { get; set; } = string.Empty;

        [MaxLength(24)]
        public string Status { get; set; } = "generated"; // generated | verified | review_required | published

        [Required]
        public string RenderedText { get; set; } = string.Empty;

        [MaxLength(128)]
        public string? Model { get; set; }

        [MaxLength(64)]
        public string? PromptTemplateVersion { get; set; }

        [MaxLength(128)]
        public string? RetrievalBundleId { get; set; }

        [MaxLength(128)]
        public string? CorrelationId { get; set; }

        public string? RetrievalBundleJson { get; set; }
        public string? StructuredClaimsJson { get; set; }
        public string? MetadataJson { get; set; }

        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class AiDraftClaim
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(128)]
        public string DraftOutputId { get; set; } = string.Empty;

        public int OrderIndex { get; set; }

        [Required]
        public string ClaimText { get; set; } = string.Empty;

        [MaxLength(64)]
        public string? ClaimType { get; set; } // fact | procedural | rule_based | strategy | date | amount | other

        public bool IsCritical { get; set; }

        public decimal? Confidence { get; set; }

        [MaxLength(32)]
        public string Status { get; set; } = "needs_review"; // supported | partially_supported | unsupported | needs_review

        [MaxLength(1024)]
        public string? SupportSummary { get; set; }

        public string? MetadataJson { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class AiDraftEvidenceLink
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(128)]
        public string ClaimId { get; set; } = string.Empty;

        [MaxLength(128)]
        public string? DocumentId { get; set; }

        [MaxLength(128)]
        public string? DocumentVersionId { get; set; }

        [MaxLength(128)]
        public string? Sha256 { get; set; }

        public int? Page { get; set; }

        [MaxLength(128)]
        public string? ParagraphId { get; set; }

        public int? CharStart { get; set; }
        public int? CharEnd { get; set; }

        [MaxLength(2048)]
        public string? Excerpt { get; set; }

        [MaxLength(24)]
        public string? SupportStrength { get; set; } // weak | medium | strong

        [MaxLength(1024)]
        public string? WhySupports { get; set; }

        public string? MetadataJson { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class AiDraftRuleCitation
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(128)]
        public string ClaimId { get; set; } = string.Empty;

        [MaxLength(128)]
        public string? JurisdictionRulePackId { get; set; }

        public int? RulePackVersion { get; set; }

        [MaxLength(128)]
        public string? RuleCode { get; set; }

        [MaxLength(2048)]
        public string? SourceCitation { get; set; }

        [MaxLength(2048)]
        public string? CitationText { get; set; }

        public DateTime? EffectiveAt { get; set; }

        public decimal? Confidence { get; set; }

        public string? MetadataJson { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class AiDraftVerificationRun
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(128)]
        public string DraftOutputId { get; set; } = string.Empty;

        [MaxLength(64)]
        public string? VerifierVersion { get; set; }

        [MaxLength(24)]
        public string Status { get; set; } = "completed"; // pending | completed | failed

        [MaxLength(128)]
        public string? CorrelationId { get; set; }

        public string? ResultJson { get; set; }
        public string? MetadataJson { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}

