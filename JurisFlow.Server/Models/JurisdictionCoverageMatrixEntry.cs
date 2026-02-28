using System.ComponentModel.DataAnnotations;

namespace JurisFlow.Server.Models
{
    public class JurisdictionCoverageMatrixEntry
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(160)]
        public string CoverageKey { get; set; } = string.Empty; // normalized composite key

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
        public string? FilingMethod { get; set; }

        [Required]
        [MaxLength(24)]
        public string SupportLevel { get; set; } = "planned"; // none | planned | partial | beta | production

        [Required]
        [MaxLength(24)]
        public string Status { get; set; } = "active"; // active | deprecated | archived

        public int Version { get; set; } = 1;

        public DateTime EffectiveFrom { get; set; } = DateTime.UtcNow.Date;
        public DateTime? EffectiveTo { get; set; }

        [MaxLength(32)]
        public string ConfidenceLevel { get; set; } = "medium";

        public decimal ConfidenceScore { get; set; } = 0.5m;

        [MaxLength(128)]
        public string? RulePackId { get; set; }

        public string? CapabilitiesJson { get; set; } // precheck, filing, service, notice_ingest, etc.
        public string? ConstraintsJson { get; set; } // page limit, file types, size limits, local caveats
        public string? MetadataJson { get; set; }

        [MaxLength(2048)]
        public string? SourceCitation { get; set; }

        [MaxLength(128)]
        public string? UpdatedBy { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}

