using System.ComponentModel.DataAnnotations;

namespace JurisFlow.Server.Models
{
    public class TrustOperationalAlert
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(256)]
        public string AlertKey { get; set; } = string.Empty;

        [Required]
        [MaxLength(64)]
        public string AlertType { get; set; } = string.Empty;

        [Required]
        [MaxLength(24)]
        public string Severity { get; set; } = "warning";

        [MaxLength(128)]
        public string? TrustAccountId { get; set; }

        [MaxLength(128)]
        public string? RelatedEntityType { get; set; }

        [MaxLength(128)]
        public string? RelatedEntityId { get; set; }

        public DateTime? PeriodEnd { get; set; }

        [Required]
        [MaxLength(256)]
        public string Title { get; set; } = string.Empty;

        [Required]
        [MaxLength(2048)]
        public string Summary { get; set; } = string.Empty;

        [MaxLength(2048)]
        public string? ActionHint { get; set; }

        [Required]
        [MaxLength(32)]
        public string SourceStatus { get; set; } = "open";

        [Required]
        [MaxLength(32)]
        public string WorkflowStatus { get; set; } = "open"; // open | acknowledged | assigned | escalated | resolved

        [MaxLength(128)]
        public string? AssignedUserId { get; set; }

        public DateTime OpenedAt { get; set; }
        public DateTime FirstDetectedAt { get; set; } = DateTime.UtcNow;
        public DateTime LastDetectedAt { get; set; } = DateTime.UtcNow;

        [MaxLength(128)]
        public string? AcknowledgedBy { get; set; }
        public DateTime? AcknowledgedAt { get; set; }

        [MaxLength(128)]
        public string? EscalatedBy { get; set; }
        public DateTime? EscalatedAt { get; set; }

        [MaxLength(128)]
        public string? ResolvedBy { get; set; }
        public DateTime? ResolvedAt { get; set; }

        public DateTime? LastNotificationAt { get; set; }
        public int NotificationCount { get; set; }

        public string? MetadataJson { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class TrustOperationalAlertEvent
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(128)]
        public string TrustOperationalAlertId { get; set; } = string.Empty;

        [Required]
        [MaxLength(48)]
        public string EventType { get; set; } = string.Empty; // detected | acknowledged | assigned | escalated | resolved | reopened | auto_resolved | notification_sent

        [MaxLength(128)]
        public string? ActorUserId { get; set; }

        [MaxLength(2048)]
        public string? Notes { get; set; }

        public string? MetadataJson { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
