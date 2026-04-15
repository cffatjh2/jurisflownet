using System.ComponentModel.DataAnnotations;

namespace JurisFlow.Server.Models
{
    public class MatterBillingPolicy
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(128)]
        public string MatterId { get; set; } = string.Empty;

        [Required]
        [MaxLength(128)]
        public string ClientId { get; set; } = string.Empty;

        [MaxLength(128)]
        public string? ThirdPartyPayorClientId { get; set; }

        [Required]
        [MaxLength(24)]
        public string ArrangementType { get; set; } = "hourly"; // hourly | fixed | contingency | hybrid

        [Required]
        [MaxLength(24)]
        public string BillingCycle { get; set; } = "monthly"; // monthly | milestone | ad_hoc

        [MaxLength(128)]
        public string? RateCardId { get; set; }

        [MaxLength(8)]
        public string Currency { get; set; } = "USD";

        [MaxLength(32)]
        public string TaxPolicyMode { get; set; } = "matter"; // matter | jurisdiction | none

        [MaxLength(32)]
        public string TrustHandlingMode { get; set; } = "separate"; // separate | mixed (guarded)

        [MaxLength(32)]
        public string CollectionPolicy { get; set; } = "standard";

        [MaxLength(32)]
        public string EbillingFormat { get; set; } = "none"; // none | ledes98b | ledes1998bi

        [MaxLength(32)]
        public string EbillingStatus { get; set; } = "disabled"; // disabled | enabled

        public bool RequirePrebillApproval { get; set; } = true;
        public bool EnforceUtbmsCodes { get; set; } = false;
        public bool EnforceTrustOperatingSplit { get; set; } = true;

        public DateTime EffectiveFrom { get; set; } = DateTime.UtcNow.Date;
        public DateTime? EffectiveTo { get; set; }

        [Required]
        [MaxLength(24)]
        public string Status { get; set; } = "active";

        public string? TaxPolicyJson { get; set; }
        public string? SplitBillingJson { get; set; }
        public string? EbillingProfileJson { get; set; }
        public string? CollectionPolicyJson { get; set; }
        public string? TrustPolicyJson { get; set; }
        public string? MetadataJson { get; set; }

        [MaxLength(2048)]
        public string? Notes { get; set; }

        [MaxLength(128)]
        public string? UpdatedBy { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class BillingRateCard
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(160)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(8)]
        public string Currency { get; set; } = "USD";

        [MaxLength(24)]
        public string Scope { get; set; } = "firm"; // firm | client | matter

        [MaxLength(128)]
        public string? ClientId { get; set; }

        [MaxLength(128)]
        public string? MatterId { get; set; }

        [MaxLength(24)]
        public string Status { get; set; } = "active";

        public DateTime EffectiveFrom { get; set; } = DateTime.UtcNow.Date;
        public DateTime? EffectiveTo { get; set; }

        public string? MetadataJson { get; set; }

        [MaxLength(128)]
        public string? UpdatedBy { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class BillingRateCardEntry
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(128)]
        public string RateCardId { get; set; } = string.Empty;

        [Required]
        [MaxLength(24)]
        public string EntryType { get; set; } = "time"; // time | expense | fixed

        [MaxLength(32)]
        public string? TimekeeperRole { get; set; } // partner, associate, paralegal

        [MaxLength(128)]
        public string? EmployeeId { get; set; }

        [MaxLength(128)]
        public string? ClientId { get; set; }

        [MaxLength(128)]
        public string? MatterId { get; set; }

        [MaxLength(20)]
        public string? TaskCode { get; set; }

        [MaxLength(20)]
        public string? ActivityCode { get; set; }

        [MaxLength(20)]
        public string? ExpenseCode { get; set; }

        [MaxLength(32)]
        public string? Unit { get; set; } = "hour"; // hour | item | amount

        public decimal Rate { get; set; }
        public decimal? MinimumUnits { get; set; }
        public decimal? MaximumUnits { get; set; }

        [MaxLength(24)]
        public string Status { get; set; } = "active";

        public int Priority { get; set; } = 100;

        public string? MetadataJson { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class BillingPrebillBatch
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(128)]
        public string MatterId { get; set; } = string.Empty;

        [Required]
        [MaxLength(128)]
        public string ClientId { get; set; } = string.Empty;

        [MaxLength(128)]
        public string? PolicyId { get; set; }

        [MaxLength(128)]
        public string? RateCardId { get; set; }

        [MaxLength(128)]
        public string? InvoiceId { get; set; }

        [MaxLength(8)]
        public string Currency { get; set; } = "USD";

        [MaxLength(24)]
        public string ArrangementType { get; set; } = "hourly";

        [MaxLength(24)]
        public string Status { get; set; } = "draft"; // draft | in_review | approved | rejected | finalized

        public DateTime PeriodStart { get; set; } = DateTime.UtcNow.Date;
        public DateTime PeriodEnd { get; set; } = DateTime.UtcNow.Date;

        public decimal Subtotal { get; set; }
        public decimal TaxTotal { get; set; }
        public decimal DiscountTotal { get; set; }
        public decimal WriteDownTotal { get; set; }
        public decimal Total { get; set; }

        [MaxLength(32)]
        public string? TaxPolicyCode { get; set; }

        [MaxLength(32)]
        public string? LedesFormat { get; set; }

        [MaxLength(128)]
        public string? GeneratedBy { get; set; }
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

        [MaxLength(128)]
        public string? SubmittedBy { get; set; }
        public DateTime? SubmittedAt { get; set; }

        [MaxLength(128)]
        public string? ApprovedBy { get; set; }
        public DateTime? ApprovedAt { get; set; }

        [MaxLength(128)]
        public string? RejectedBy { get; set; }
        public DateTime? RejectedAt { get; set; }

        [MaxLength(2048)]
        public string? ReviewNotes { get; set; }

        public string? MetadataJson { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class BillingPrebillLine
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(128)]
        public string PrebillBatchId { get; set; } = string.Empty;

        [Required]
        [MaxLength(128)]
        public string MatterId { get; set; } = string.Empty;

        [Required]
        [MaxLength(128)]
        public string ClientId { get; set; } = string.Empty;

        [Required]
        [MaxLength(24)]
        public string LineType { get; set; } = "time"; // time | expense | fixed | adjustment

        [Required]
        [MaxLength(64)]
        public string SourceType { get; set; } = "manual"; // TimeEntry | Expense | Manual

        [MaxLength(128)]
        public string? SourceId { get; set; }

        [MaxLength(128)]
        public string? TimekeeperId { get; set; }

        [MaxLength(32)]
        public string? TimekeeperRole { get; set; }

        public DateTime? ServiceDate { get; set; }

        [MaxLength(255)]
        public string Description { get; set; } = string.Empty;

        [MaxLength(20)]
        public string? TaskCode { get; set; }

        [MaxLength(20)]
        public string? ActivityCode { get; set; }

        [MaxLength(20)]
        public string? ExpenseCode { get; set; }

        public decimal Quantity { get; set; } = 1m;
        public decimal Rate { get; set; } = 0m;

        public decimal ProposedAmount { get; set; } = 0m;
        public decimal ApprovedAmount { get; set; } = 0m;

        public decimal DiscountAmount { get; set; } = 0m;
        public decimal WriteDownAmount { get; set; } = 0m;
        public decimal TaxAmount { get; set; } = 0m;

        [MaxLength(32)]
        public string? TaxCode { get; set; }

        [MaxLength(128)]
        public string? ThirdPartyPayorClientId { get; set; }

        [MaxLength(24)]
        public string Status { get; set; } = "draft"; // draft | reviewed | approved | excluded

        [MaxLength(2048)]
        public string? ReviewerNotes { get; set; }

        public string? SplitAllocationJson { get; set; }
        public string? MetadataJson { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class BillingLedgerEntry
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(32)]
        public string LedgerDomain { get; set; } = "billing"; // billing | trust | operating

        [Required]
        [MaxLength(32)]
        public string LedgerBucket { get; set; } = "accounts_receivable"; // ar/revenue/tax/trust_liability/cash/writeoff

        [Required]
        [MaxLength(32)]
        public string EntryType { get; set; } = "adjustment"; // prebill_approved/payment_allocation/credit_memo/writeoff/adjustment/reversal

        [Required]
        [MaxLength(8)]
        public string Currency { get; set; } = "USD";

        [Required]
        public decimal Amount { get; set; } // signed amount; use reversal entries instead of delete/update

        [MaxLength(128)]
        public string? MatterId { get; set; }

        [MaxLength(128)]
        public string? ClientId { get; set; }

        [MaxLength(128)]
        public string? PayorClientId { get; set; }

        [MaxLength(128)]
        public string? InvoicePayorAllocationId { get; set; }

        [MaxLength(128)]
        public string? InvoiceId { get; set; }

        [MaxLength(128)]
        public string? InvoiceLineItemId { get; set; }

        [MaxLength(128)]
        public string? PaymentTransactionId { get; set; }

        [MaxLength(128)]
        public string? PrebillBatchId { get; set; }

        [MaxLength(128)]
        public string? PrebillLineId { get; set; }

        [MaxLength(128)]
        public string? TrustTransactionId { get; set; }

        [MaxLength(128)]
        public string? ReversalOfLedgerEntryId { get; set; }

        [MaxLength(128)]
        public string? CorrelationKey { get; set; }

        [MaxLength(24)]
        public string Status { get; set; } = "posted"; // posted | reversed

        [MaxLength(2048)]
        public string? Description { get; set; }

        public string? MetadataJson { get; set; }

        [MaxLength(128)]
        public string? PostedBy { get; set; }
        public DateTime PostedAt { get; set; } = DateTime.UtcNow;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class BillingPaymentAllocation
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(128)]
        public string PaymentTransactionId { get; set; } = string.Empty;

        [Required]
        [MaxLength(128)]
        public string InvoiceId { get; set; } = string.Empty;

        [MaxLength(128)]
        public string? InvoiceLineItemId { get; set; }

        [MaxLength(128)]
        public string? ClientId { get; set; }

        [MaxLength(128)]
        public string? PayorClientId { get; set; }

        [MaxLength(128)]
        public string? InvoicePayorAllocationId { get; set; }

        [MaxLength(128)]
        public string? MatterId { get; set; }

        [Required]
        public decimal Amount { get; set; }

        [Required]
        [MaxLength(24)]
        public string AllocationType { get; set; } = "invoice_line"; // invoice_line | invoice_header | tax | fee

        [MaxLength(24)]
        public string Status { get; set; } = "applied"; // applied | pending_trust_approval | reversed

        [MaxLength(128)]
        public string? LedgerEntryId { get; set; }

        [MaxLength(128)]
        public string? TrustTransactionId { get; set; }

        [MaxLength(128)]
        public string? ReversalOfAllocationId { get; set; }

        [MaxLength(2048)]
        public string? Notes { get; set; }

        [Required]
        [MaxLength(160)]
        public string IdempotencyKey { get; set; } = Guid.NewGuid().ToString("N");

        public string? MetadataJson { get; set; }

        [MaxLength(128)]
        public string? AppliedBy { get; set; }
        public DateTime AppliedAt { get; set; } = DateTime.UtcNow;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class InvoicePayorAllocation
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(128)]
        public string InvoiceId { get; set; } = string.Empty;

        [Required]
        [MaxLength(128)]
        public string PayorClientId { get; set; } = string.Empty;

        [Required]
        [MaxLength(24)]
        public string ResponsibilityType { get; set; } = "primary"; // primary | split_percent | split_amount | third_party

        public decimal? Percent { get; set; }
        public decimal? AmountCap { get; set; }
        public int Priority { get; set; } = 100;

        [Required]
        [MaxLength(24)]
        public string Status { get; set; } = "active"; // active | inactive | closed

        public bool IsPrimary { get; set; } = false;

        public decimal AllocatedAmount { get; set; } = 0m;

        [MaxLength(500)]
        public string? Terms { get; set; }

        [MaxLength(255)]
        public string? Reference { get; set; }

        [MaxLength(255)]
        public string? PurchaseOrder { get; set; }

        public string? EbillingProfileJson { get; set; }
        public string? MetadataJson { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class InvoiceLinePayorAllocation
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(128)]
        public string InvoiceId { get; set; } = string.Empty;

        [Required]
        [MaxLength(128)]
        public string InvoiceLineItemId { get; set; } = string.Empty;

        [MaxLength(128)]
        public string? InvoicePayorAllocationId { get; set; }

        [Required]
        [MaxLength(128)]
        public string PayorClientId { get; set; } = string.Empty;

        [Required]
        [MaxLength(24)]
        public string ResponsibilityType { get; set; } = "primary";

        public decimal? Percent { get; set; }

        [Required]
        public decimal Amount { get; set; }

        [MaxLength(24)]
        public string Status { get; set; } = "active";

        [MaxLength(20)]
        public string? TaskCode { get; set; }

        [MaxLength(20)]
        public string? ActivityCode { get; set; }

        [MaxLength(20)]
        public string? ExpenseCode { get; set; }

        public string? EbillingProfileJson { get; set; }
        public string? MetadataJson { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
