using System.ComponentModel.DataAnnotations;

namespace JurisFlow.Server.Models
{
    public class TrustEvidenceFile
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(128)]
        public string TrustAccountId { get; set; } = string.Empty;

        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }

        [Required]
        [MaxLength(64)]
        public string Source { get; set; } = "manual_manifest";

        [Required]
        [MaxLength(256)]
        public string FileName { get; set; } = string.Empty;

        [MaxLength(128)]
        public string? ContentType { get; set; }

        [Required]
        [MaxLength(128)]
        public string FileHash { get; set; } = string.Empty;

        [MaxLength(256)]
        public string? EvidenceKey { get; set; }

        public long? FileSizeBytes { get; set; }

        [Required]
        [MaxLength(24)]
        public string Status { get; set; } = "registered";

        [MaxLength(128)]
        public string? LatestParserRunId { get; set; }

        [MaxLength(128)]
        public string? CanonicalStatementImportId { get; set; }

        [MaxLength(128)]
        public string? DuplicateOfEvidenceFileId { get; set; }

        [MaxLength(128)]
        public string? SupersededByEvidenceFileId { get; set; }

        [MaxLength(128)]
        public string? SupersededBy { get; set; }

        public DateTime? SupersededAt { get; set; }

        [MaxLength(128)]
        public string? RegisteredBy { get; set; }

        [MaxLength(2048)]
        public string? Notes { get; set; }

        public string? MetadataJson { get; set; }
        public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class TrustStatementParserRun
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(128)]
        public string TrustAccountId { get; set; } = string.Empty;

        [Required]
        [MaxLength(128)]
        public string TrustEvidenceFileId { get; set; } = string.Empty;

        [MaxLength(128)]
        public string? TrustStatementImportId { get; set; }

        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }

        [Required]
        [MaxLength(64)]
        public string ParserKey { get; set; } = "manual_manifest_v1";

        [Required]
        [MaxLength(24)]
        public string Status { get; set; } = "pending";

        public int AttemptCount { get; set; } = 1;

        [MaxLength(64)]
        public string Source { get; set; } = "evidence_registry";

        [MaxLength(128)]
        public string? StartedBy { get; set; }

        [MaxLength(2048)]
        public string? Notes { get; set; }

        [MaxLength(2048)]
        public string? ErrorMessage { get; set; }

        public string? SummaryJson { get; set; }
        public DateTime StartedAt { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class TrustJurisdictionPacketTemplate
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(64)]
        public string PolicyKey { get; set; } = "default";

        [Required]
        [MaxLength(24)]
        public string Jurisdiction { get; set; } = "DEFAULT";

        [Required]
        [MaxLength(24)]
        public string AccountType { get; set; } = "all";

        [Required]
        [MaxLength(64)]
        public string TemplateKey { get; set; } = "default-packet-template";

        [MaxLength(128)]
        public string? Name { get; set; }

        public int VersionNumber { get; set; } = 1;
        public bool IsActive { get; set; } = true;
        public string? RequiredSectionsJson { get; set; }
        public string? RequiredAttestationsJson { get; set; }
        public string? DisclosureBlocksJson { get; set; }
        public string? RenderingProfileJson { get; set; }
        public string? MetadataJson { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class TrustMonthCloseAttestation
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(128)]
        public string TrustMonthCloseId { get; set; } = string.Empty;

        [Required]
        [MaxLength(48)]
        public string Role { get; set; } = string.Empty;

        [Required]
        [MaxLength(64)]
        public string AttestationKey { get; set; } = string.Empty;

        [MaxLength(256)]
        public string? Label { get; set; }

        public bool Accepted { get; set; }

        [MaxLength(2048)]
        public string? Notes { get; set; }

        [MaxLength(128)]
        public string? SignedBy { get; set; }

        public DateTime? SignedAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class TrustOpsInboxItem
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [MaxLength(128)]
        public string? TrustOperationalAlertId { get; set; }

        [MaxLength(128)]
        public string? TrustCloseForecastSnapshotId { get; set; }

        [Required]
        [MaxLength(48)]
        public string ItemType { get; set; } = "operational_alert";

        [Required]
        [MaxLength(48)]
        public string BlockerGroup { get; set; } = "general_blocker";

        [Required]
        [MaxLength(24)]
        public string Severity { get; set; } = "warning";

        [MaxLength(128)]
        public string? TrustAccountId { get; set; }

        [MaxLength(24)]
        public string? Jurisdiction { get; set; }

        [MaxLength(128)]
        public string? OfficeId { get; set; }

        [MaxLength(128)]
        public string? AssignedUserId { get; set; }

        [Required]
        [MaxLength(32)]
        public string WorkflowStatus { get; set; } = "open"; // open | claimed | assigned | deferred | escalated | resolved

        public DateTime? DueAt { get; set; }
        public DateTime? DeferredUntil { get; set; }
        public DateTime? LastActionAt { get; set; }

        [Required]
        [MaxLength(256)]
        public string Title { get; set; } = string.Empty;

        [Required]
        [MaxLength(2048)]
        public string Summary { get; set; } = string.Empty;

        [MaxLength(2048)]
        public string? ActionHint { get; set; }

        [MaxLength(48)]
        public string? SuggestedExportType { get; set; }

        [MaxLength(256)]
        public string? SuggestedRoute { get; set; }

        public string? MetadataJson { get; set; }
        public DateTime OpenedAt { get; set; } = DateTime.UtcNow;
        public DateTime LastDetectedAt { get; set; } = DateTime.UtcNow;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class TrustCloseForecastSnapshot
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(128)]
        public string TrustAccountId { get; set; } = string.Empty;

        [MaxLength(24)]
        public string? Jurisdiction { get; set; }

        [MaxLength(128)]
        public string? OfficeId { get; set; }

        [Required]
        [MaxLength(24)]
        public string StatementCadence { get; set; } = "monthly";

        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }
        public DateTime CloseDueAt { get; set; }

        [Required]
        [MaxLength(24)]
        public string ReadinessStatus { get; set; } = "blocked"; // ready | at_risk | blocked | overdue | closed

        [Required]
        [MaxLength(24)]
        public string Severity { get; set; } = "warning"; // info | warning | critical

        public bool MissingStatementImport { get; set; }

        [MaxLength(128)]
        public string? LatestStatementImportId { get; set; }

        public DateTime? StatementImportedAt { get; set; }

        public bool HasCanonicalPacket { get; set; }

        [MaxLength(128)]
        public string? CanonicalPacketId { get; set; }

        [MaxLength(24)]
        public string? PacketStatus { get; set; }

        public bool HasCanonicalMonthClose { get; set; }

        [MaxLength(128)]
        public string? CanonicalMonthCloseId { get; set; }

        [MaxLength(24)]
        public string? MonthCloseStatus { get; set; }

        public int OpenExceptionCount { get; set; }
        public int OutstandingItemCount { get; set; }
        public int MissingRequiredSectionCount { get; set; }
        public int MissingAttestationCount { get; set; }

        public decimal UnclearedBalance { get; set; }
        public int UnclearedEntryCount { get; set; }
        public int? OldestOutstandingAgeDays { get; set; }
        public int? OldestUnclearedAgeDays { get; set; }

        public bool DraftBundleEligible { get; set; }

        [MaxLength(128)]
        public string? DraftBundleManifestExportId { get; set; }

        public DateTime? DraftBundleGeneratedAt { get; set; }

        [MaxLength(2048)]
        public string? RecommendedAction { get; set; }

        public int ReminderCount { get; set; }
        public DateTime? LastReminderAt { get; set; }
        public DateTime? NextReminderAt { get; set; }
        public DateTime? EscalatedAt { get; set; }
        public DateTime? LastAutomationRunAt { get; set; }

        [MaxLength(128)]
        public string? LastAutomationRunBy { get; set; }

        public string? SummaryJson { get; set; }
        public string? MetadataJson { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class TrustOpsInboxEvent
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(128)]
        public string TrustOpsInboxItemId { get; set; } = string.Empty;

        [Required]
        [MaxLength(48)]
        public string EventType { get; set; } = string.Empty;

        [MaxLength(128)]
        public string? ActorUserId { get; set; }

        [MaxLength(2048)]
        public string? Notes { get; set; }

        public string? MetadataJson { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class TrustBundleSignature
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(128)]
        public string ManifestExportId { get; set; } = string.Empty;

        [Required]
        [MaxLength(64)]
        public string SignatureAlgorithm { get; set; } = "hmac-sha256";

        [Required]
        [MaxLength(256)]
        public string SignatureDigest { get; set; } = string.Empty;

        [Required]
        [MaxLength(32)]
        public string IntegrityStatus { get; set; } = "signed"; // signed | verified | invalid

        [MaxLength(64)]
        public string VerificationStatus { get; set; } = "verified";

        [MaxLength(128)]
        public string? SignedBy { get; set; }

        public DateTime SignedAt { get; set; } = DateTime.UtcNow;

        public DateTime? VerifiedAt { get; set; }

        [MaxLength(64)]
        public string? RetentionPolicyTag { get; set; }

        [MaxLength(64)]
        public string? RedactionProfile { get; set; }

        [MaxLength(128)]
        public string? ParentManifestExportId { get; set; }

        public string? EvidenceManifestJson { get; set; }
        public string? MetadataJson { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
