using System.ComponentModel.DataAnnotations;

namespace JurisFlow.Server.Models
{
    public class TrustRiskPolicy
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(64)]
        public string PolicyKey { get; set; } = "default";

        public int VersionNumber { get; set; } = 1;

        public bool IsActive { get; set; } = true;

        [MaxLength(24)]
        public string Status { get; set; } = "active"; // draft | active | retired

        [MaxLength(255)]
        public string? Name { get; set; }

        [MaxLength(2048)]
        public string? Description { get; set; }

        public decimal WarnThreshold { get; set; } = 35m;
        public decimal ReviewThreshold { get; set; } = 60m;
        public decimal SoftHoldThreshold { get; set; } = 80m;
        public decimal HardHoldThreshold { get; set; } = 95m;

        [MaxLength(24)]
        public string FailMode { get; set; } = "fail_open"; // fail_open | fail_closed

        public string? EnabledRulesJson { get; set; }
        public string? RuleWeightsJson { get; set; }
        public string? ActionMapJson { get; set; }
        public string? MetadataJson { get; set; }

        [MaxLength(128)]
        public string? CreatedBy { get; set; }

        [MaxLength(128)]
        public string? UpdatedBy { get; set; }

        public DateTime? PublishedAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class TrustRiskEvent
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [MaxLength(128)]
        public string? PolicyId { get; set; }

        [MaxLength(64)]
        public string? PolicyKey { get; set; }

        public int? PolicyVersionNumber { get; set; }

        [Required]
        [MaxLength(64)]
        public string SourceType { get; set; } = "ledger_entry"; // trust_transaction | billing_ledger_entry | billing_payment_allocation

        [Required]
        [MaxLength(128)]
        public string SourceId { get; set; } = string.Empty;

        [Required]
        [MaxLength(64)]
        public string TriggerType { get; set; } = "record"; // manual_posted | reversal_posted | payment_allocation_applied | payment_allocation_reversed

        [MaxLength(128)]
        public string? TrustTransactionId { get; set; }

        [MaxLength(128)]
        public string? BillingLedgerEntryId { get; set; }

        [MaxLength(128)]
        public string? BillingPaymentAllocationId { get; set; }

        [MaxLength(128)]
        public string? PaymentTransactionId { get; set; }

        [MaxLength(128)]
        public string? InvoiceId { get; set; }

        [MaxLength(128)]
        public string? MatterId { get; set; }

        [MaxLength(128)]
        public string? ClientId { get; set; }

        [MaxLength(128)]
        public string? PayorClientId { get; set; }

        [MaxLength(128)]
        public string? SourceCorrelationKey { get; set; }

        [MaxLength(128)]
        public string? CorrelationId { get; set; }

        [MaxLength(128)]
        public string? TraceId { get; set; }

        [MaxLength(24)]
        public string EvaluationMode { get; set; } = "record_only"; // record_only | warn_only | review_only | hold_enforced

        public decimal RiskScore { get; set; } = 0m;

        [Required]
        [MaxLength(16)]
        public string Severity { get; set; } = "low"; // low | medium | high | critical

        [Required]
        [MaxLength(24)]
        public string Decision { get; set; } = "record"; // record | warn | review_required | soft_hold | hard_hold

        [Required]
        [MaxLength(24)]
        public string Status { get; set; } = "recorded"; // recorded | warned | review_queued | closed

        [MaxLength(128)]
        public string? ReviewQueueItemId { get; set; }

        public bool IsRetryable { get; set; } = false;

        public string? RiskReasonsJson { get; set; }
        public string? EvidenceJson { get; set; }
        public string? FeaturesJson { get; set; }
        public string? MetadataJson { get; set; }

        [MaxLength(128)]
        public string? EvaluatedBy { get; set; } = "system";

        public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
        public DateTime EvaluatedAt { get; set; } = DateTime.UtcNow;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class TrustRiskAction
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(128)]
        public string TrustRiskEventId { get; set; } = string.Empty;

        [Required]
        [MaxLength(32)]
        public string ActionType { get; set; } = "recorded"; // recorded | warn | review_created | soft_hold | hard_hold | release

        [Required]
        [MaxLength(24)]
        public string Status { get; set; } = "completed"; // pending | completed | failed | skipped

        [MaxLength(128)]
        public string? ActorUserId { get; set; }

        [MaxLength(64)]
        public string? ActorType { get; set; } = "system"; // system | user

        [MaxLength(128)]
        public string? CorrelationId { get; set; }

        [MaxLength(2048)]
        public string? Notes { get; set; }

        public string? MetadataJson { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class TrustRiskHold
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(128)]
        public string TrustRiskEventId { get; set; } = string.Empty;

        [Required]
        [MaxLength(64)]
        public string TargetType { get; set; } = "unknown"; // trust_transaction | billing_payment_allocation | disbursement_request

        [Required]
        [MaxLength(128)]
        public string TargetId { get; set; } = string.Empty;

        [Required]
        [MaxLength(16)]
        public string HoldType { get; set; } = "soft"; // soft | hard

        [Required]
        [MaxLength(24)]
        public string Status { get; set; } = "placed"; // placed | under_review | released | escalated | expired

        [MaxLength(128)]
        public string? CorrelationId { get; set; }

        [MaxLength(2048)]
        public string? Reason { get; set; }

        [MaxLength(128)]
        public string? PlacedBy { get; set; } = "system";

        public DateTime PlacedAt { get; set; } = DateTime.UtcNow;

        [MaxLength(128)]
        public string? ReleasedBy { get; set; }

        [MaxLength(2048)]
        public string? ReleaseReason { get; set; }

        public DateTime? ReleasedAt { get; set; }

        public string? MetadataJson { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class TrustRiskReviewLink
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(128)]
        public string TrustRiskEventId { get; set; } = string.Empty;

        [Required]
        [MaxLength(128)]
        public string ReviewQueueItemId { get; set; } = string.Empty;

        [Required]
        [MaxLength(48)]
        public string ReviewQueueType { get; set; } = "integration_review"; // integration_review | billing_review | security_review

        [MaxLength(64)]
        public string? LinkReasonCode { get; set; }

        [MaxLength(24)]
        public string Status { get; set; } = "active"; // active | resolved | detached

        [MaxLength(128)]
        public string? CorrelationId { get; set; }

        public string? MetadataJson { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
