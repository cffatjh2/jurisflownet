using System.ComponentModel.DataAnnotations;

namespace JurisFlow.Server.Models
{
    public class JurisdictionValidationTestRun
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [MaxLength(32)]
        public string? JurisdictionCode { get; set; }

        [MaxLength(64)]
        public string? CourtSystem { get; set; }

        [MaxLength(64)]
        public string? CaseType { get; set; }

        [MaxLength(64)]
        public string? FilingMethod { get; set; }

        [MaxLength(128)]
        public string? RulePackId { get; set; }

        [Required]
        [MaxLength(24)]
        public string Status { get; set; } = "completed"; // completed | failed

        public int TotalCases { get; set; }
        public int PassedCases { get; set; }
        public int FailedCases { get; set; }

        [MaxLength(2048)]
        public string? Summary { get; set; }

        public string? ResultJson { get; set; }

        [MaxLength(128)]
        public string? TriggeredBy { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}

