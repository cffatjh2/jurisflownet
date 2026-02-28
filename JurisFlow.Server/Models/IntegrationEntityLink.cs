using System.ComponentModel.DataAnnotations;

namespace JurisFlow.Server.Models
{
    public class IntegrationEntityLink
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
        [MaxLength(64)]
        public string LocalEntityType { get; set; } = string.Empty;

        [Required]
        [MaxLength(128)]
        public string LocalEntityId { get; set; } = string.Empty;

        [Required]
        [MaxLength(64)]
        public string ExternalEntityType { get; set; } = string.Empty;

        [Required]
        [MaxLength(256)]
        public string ExternalEntityId { get; set; } = string.Empty;

        [MaxLength(128)]
        public string? ExternalVersion { get; set; }

        [MaxLength(16)]
        public string LastDirection { get; set; } = "outbound";

        [MaxLength(128)]
        public string? IdempotencyKey { get; set; }

        public DateTime LastSyncedAt { get; set; } = DateTime.UtcNow;

        public string? MetadataJson { get; set; }
    }
}
