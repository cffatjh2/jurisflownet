using System.ComponentModel.DataAnnotations;

namespace JurisFlow.Server.Models
{
    public class ClientTransparencyProfile
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [MaxLength(32)]
        public string Scope { get; set; } = "tenant_default"; // tenant_default | matter_override

        [MaxLength(128)]
        public string? MatterId { get; set; }

        [MaxLength(64)]
        public string ProfileKey { get; set; } = "default";

        [MaxLength(24)]
        public string Status { get; set; } = "active";

        [MaxLength(24)]
        public string PublishPolicy { get; set; } = "warn_only"; // placeholder for later phases

        public string? VisibilityRulesJson { get; set; }
        public string? RedactionRulesJson { get; set; }
        public string? SourceWhitelistJson { get; set; } // docket/efiling/task/invoice/payment/planner
        public string? DelayTaxonomyJson { get; set; }
        public string? MetadataJson { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class ClientTransparencySnapshot
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(128)]
        public string MatterId { get; set; } = string.Empty;

        [MaxLength(128)]
        public string? ProfileId { get; set; }

        public int VersionNumber { get; set; }

        [MaxLength(24)]
        public string Status { get; set; } = "generated"; // generated | published | superseded | archived

        public bool IsCurrent { get; set; } = true;
        public bool IsPublished { get; set; }

        [MaxLength(24)]
        public string? DataQuality { get; set; } // low | medium | high

        public decimal? ConfidenceScore { get; set; } // 0..1

        [MaxLength(64)]
        public string? CorrelationId { get; set; }

        public string? SnapshotSummary { get; set; }
        public string? WhatChangedSummary { get; set; }
        public string? MetadataJson { get; set; }

        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
        public DateTime? PublishedAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class ClientTransparencyTimelineItem
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(128)]
        public string SnapshotId { get; set; } = string.Empty;

        public int OrderIndex { get; set; }

        [MaxLength(64)]
        public string PhaseKey { get; set; } = "general";

        [MaxLength(160)]
        public string Label { get; set; } = string.Empty;

        [MaxLength(24)]
        public string Status { get; set; } = "pending"; // completed | in_progress | pending | blocked

        public string? ClientSafeText { get; set; }
        public DateTime? StartedAtUtc { get; set; }
        public DateTime? EtaAtUtc { get; set; }
        public DateTime? CompletedAtUtc { get; set; }
        public string? SourceRefsJson { get; set; }
        public string? MetadataJson { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class ClientTransparencyDelayReason
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(128)]
        public string SnapshotId { get; set; } = string.Empty;

        [MaxLength(64)]
        public string ReasonCode { get; set; } = "general_delay";

        [MaxLength(24)]
        public string Severity { get; set; } = "low";

        public bool IsActive { get; set; } = true;
        public int? ExpectedDelayDays { get; set; }
        public string? ClientSafeText { get; set; }
        public string? SourceRefsJson { get; set; }
        public string? MetadataJson { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class ClientTransparencyNextStep
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(128)]
        public string SnapshotId { get; set; } = string.Empty;

        [MaxLength(24)]
        public string OwnerType { get; set; } = "firm"; // firm | client | court | third_party

        [MaxLength(24)]
        public string Status { get; set; } = "pending";

        public string ActionText { get; set; } = string.Empty;
        public DateTime? EtaAtUtc { get; set; }
        public string? BlockedByText { get; set; }
        public string? SourceRefsJson { get; set; }
        public string? MetadataJson { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class ClientTransparencyCostImpact
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(128)]
        public string SnapshotId { get; set; } = string.Empty;

        [MaxLength(8)]
        public string Currency { get; set; } = "USD";

        public decimal? CurrentExpectedRangeMin { get; set; }
        public decimal? CurrentExpectedRangeMax { get; set; }
        public decimal? DeltaRangeMin { get; set; }
        public decimal? DeltaRangeMax { get; set; }

        [MaxLength(24)]
        public string? ConfidenceBand { get; set; }

        public string? DriverSummary { get; set; }
        public string? DriversJson { get; set; }
        public string? SourceRefsJson { get; set; }
        public string? MetadataJson { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class ClientTransparencyUpdateEvent
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(128)]
        public string MatterId { get; set; } = string.Empty;

        [MaxLength(128)]
        public string? SnapshotId { get; set; }

        [MaxLength(48)]
        public string TriggerType { get; set; } = "manual_regenerate";

        [MaxLength(64)]
        public string? TriggerEntityType { get; set; }

        [MaxLength(128)]
        public string? TriggerEntityId { get; set; }

        [MaxLength(24)]
        public string Status { get; set; } = "applied"; // pending | applied | skipped | failed

        [MaxLength(64)]
        public string? CorrelationId { get; set; }

        [MaxLength(128)]
        public string? TriggeredBy { get; set; }

        public string? PayloadJson { get; set; }
        public string? DiffJson { get; set; }
        public string? MetadataJson { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? AppliedAt { get; set; }
    }

    public class ClientTransparencyReviewAction
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(128)]
        public string SnapshotId { get; set; } = string.Empty;

        [MaxLength(48)]
        public string ActionType { get; set; } = "reviewed"; // approve | reject | rewrite | publish | unpublish

        [MaxLength(128)]
        public string? ReviewerUserId { get; set; }

        public string? Reason { get; set; }
        public string? BeforeJson { get; set; }
        public string? AfterJson { get; set; }
        public string? MetadataJson { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
