using System.ComponentModel.DataAnnotations;

namespace JurisFlow.Server.Models
{
    public class JurisdictionValidationTestCase
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(160)]
        public string Name { get; set; } = string.Empty;

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

        [MaxLength(128)]
        public string? RulePackId { get; set; }

        [Required]
        [MaxLength(24)]
        public string ExpectedSupportLevel { get; set; } = "partial";

        public bool ExpectedRequiresHumanReview { get; set; }

        public string? PacketInputJson { get; set; }
        public string? ExpectedOutputJson { get; set; }

        [MaxLength(24)]
        public string Status { get; set; } = "active";

        [MaxLength(2048)]
        public string? Notes { get; set; }

        [MaxLength(128)]
        public string? UpdatedBy { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}

