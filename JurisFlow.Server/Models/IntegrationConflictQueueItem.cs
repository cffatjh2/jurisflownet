using System.ComponentModel.DataAnnotations;

namespace JurisFlow.Server.Models
{
    public class IntegrationConflictQueueItem
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [MaxLength(128)]
        public string? ConnectionId { get; set; }

        [MaxLength(128)]
        public string? RunId { get; set; }

        [Required]
        [MaxLength(128)]
        public string ProviderKey { get; set; } = string.Empty;

        [Required]
        [MaxLength(64)]
        public string EntityType { get; set; } = "unknown";

        [MaxLength(128)]
        public string? LocalEntityId { get; set; }

        [MaxLength(256)]
        public string? ExternalEntityId { get; set; }

        [Required]
        [MaxLength(64)]
        public string ConflictType { get; set; } = "data_conflict";

        [Required]
        [MaxLength(16)]
        public string Severity { get; set; } = "medium";

        [Required]
        [MaxLength(32)]
        public string Status { get; set; } = "open";

        [MaxLength(128)]
        public string? MappingProfileId { get; set; }

        [MaxLength(256)]
        public string? Fingerprint { get; set; }

        [MaxLength(128)]
        public string? AssignedTo { get; set; }

        [MaxLength(32)]
        public string? ResolutionType { get; set; }

        [MaxLength(2048)]
        public string? Summary { get; set; }

        public string? LocalSnapshotJson { get; set; }
        public string? ExternalSnapshotJson { get; set; }
        public string? SuggestedResolutionJson { get; set; }
        public string? ResolutionJson { get; set; }

        [MaxLength(2048)]
        public string? ReviewNotes { get; set; }

        [MaxLength(128)]
        public string? ReviewedBy { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public DateTime? ResolvedAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
