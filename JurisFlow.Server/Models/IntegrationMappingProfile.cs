using System.ComponentModel.DataAnnotations;

namespace JurisFlow.Server.Models
{
    public class IntegrationMappingProfile
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(128)]
        public string ConnectionId { get; set; } = string.Empty;

        [Required]
        [MaxLength(128)]
        public string ProviderKey { get; set; } = string.Empty;

        [Required]
        [MaxLength(128)]
        public string ProfileKey { get; set; } = string.Empty;

        [Required]
        [MaxLength(160)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [MaxLength(64)]
        public string EntityType { get; set; } = string.Empty;

        [Required]
        [MaxLength(16)]
        public string Direction { get; set; } = "both";

        [Required]
        [MaxLength(32)]
        public string Status { get; set; } = "active";

        [Required]
        [MaxLength(32)]
        public string ConflictPolicy { get; set; } = "manual_review";

        public bool IsDefault { get; set; }
        public int Version { get; set; } = 1;

        public string? FieldMappingsJson { get; set; }
        public string? EnumMappingsJson { get; set; }
        public string? TaxMappingsJson { get; set; }
        public string? AccountMappingsJson { get; set; }
        public string? DefaultsJson { get; set; }
        public string? MetadataJson { get; set; }

        [MaxLength(2048)]
        public string? ValidationSummary { get; set; }
        public DateTime? LastValidatedAt { get; set; }

        [MaxLength(128)]
        public string? UpdatedBy { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
