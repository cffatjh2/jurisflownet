using System;
using System.ComponentModel.DataAnnotations;

namespace JurisFlow.Server.Models
{
    public class IntegrationConnection
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(128)]
        public string ProviderKey { get; set; } = string.Empty;

        [Required]
        [MaxLength(128)]
        public string Provider { get; set; } = string.Empty;

        [Required]
        [MaxLength(64)]
        public string Category { get; set; } = string.Empty;

        [Required]
        [MaxLength(32)]
        public string Status { get; set; } = "connected";

        [MaxLength(256)]
        public string? AccountLabel { get; set; }

        [MaxLength(256)]
        public string? AccountEmail { get; set; }

        public bool SyncEnabled { get; set; } = true;
        public DateTime? LastSyncAt { get; set; }
        public DateTime? LastWebhookAt { get; set; }
        [MaxLength(160)]
        public string? LastWebhookEventId { get; set; }
        public DateTime ConnectedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public string? ExternalAccountId { get; set; }
        [MaxLength(2048)]
        public string? SyncCursor { get; set; }
        [MaxLength(2048)]
        public string? DeltaToken { get; set; }
        public string? MetadataJson { get; set; }
    }
}
