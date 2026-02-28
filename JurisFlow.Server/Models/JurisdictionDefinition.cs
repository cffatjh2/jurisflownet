using System.ComponentModel.DataAnnotations;

namespace JurisFlow.Server.Models
{
    public class JurisdictionDefinition
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(32)]
        public string JurisdictionCode { get; set; } = string.Empty; // US-CA, US-Federal, US-TX, etc.

        [Required]
        [MaxLength(16)]
        public string Scope { get; set; } = "state"; // state | federal | local

        [Required]
        [MaxLength(8)]
        public string CountryCode { get; set; } = "US";

        [MaxLength(8)]
        public string? StateCode { get; set; }

        [Required]
        [MaxLength(160)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(32)]
        public string? ParentJurisdictionCode { get; set; }

        [MaxLength(64)]
        public string? CourtSystem { get; set; }

        public bool IsActive { get; set; } = true;

        public string? MetadataJson { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}

