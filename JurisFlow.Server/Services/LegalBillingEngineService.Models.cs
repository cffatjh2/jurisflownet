using JurisFlow.Server.Models;

namespace JurisFlow.Server.Services
{
    public sealed class SplitAllocationComponentRequest
    {
        public string PayorClientId { get; set; } = string.Empty;
        public string? ResponsibilityType { get; set; }
        public decimal? Percent { get; set; }
        public decimal? AmountCap { get; set; }
        public int? Priority { get; set; }
        public bool? IsPrimary { get; set; }
        public string? Status { get; set; }
        public string? Terms { get; set; }
        public string? Reference { get; set; }
        public string? PurchaseOrder { get; set; }
        public string? EbillingProfileJson { get; set; }
        public string? MetadataJson { get; set; }
    }

    public sealed class MatterBillingPolicyUpsertRequest
    {
        public string? Id { get; set; }
        public string MatterId { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public string? ThirdPartyPayorClientId { get; set; }
        public string? ArrangementType { get; set; }
        public string? BillingCycle { get; set; }
        public string? RateCardId { get; set; }
        public string? Currency { get; set; }
        public string? TaxPolicyMode { get; set; }
        public string? TrustHandlingMode { get; set; }
        public string? CollectionPolicy { get; set; }
        public string? EbillingFormat { get; set; }
        public string? EbillingStatus { get; set; }
        public bool? RequirePrebillApproval { get; set; }
        public bool? EnforceUtbmsCodes { get; set; }
        public bool? EnforceTrustOperatingSplit { get; set; }
        public DateTime? EffectiveFrom { get; set; }
        public DateTime? EffectiveTo { get; set; }
        public string? Status { get; set; }
        public string? TaxPolicyJson { get; set; }
        public string? SplitBillingJson { get; set; }
        public string? EbillingProfileJson { get; set; }
        public string? CollectionPolicyJson { get; set; }
        public string? TrustPolicyJson { get; set; }
        public string? MetadataJson { get; set; }
        public string? Notes { get; set; }
        public IReadOnlyList<SplitAllocationComponentRequest>? SplitAllocations { get; set; }
    }

    public sealed class BillingRateCardQuery
    {
        public string? Scope { get; set; }
        public string? ClientId { get; set; }
        public string? MatterId { get; set; }
        public string? Status { get; set; }
        public int? Limit { get; set; }
    }

    public sealed class BillingRateCardUpsertRequest
    {
        public string? Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Currency { get; set; }
        public string? Scope { get; set; }
        public string? ClientId { get; set; }
        public string? MatterId { get; set; }
        public string? Status { get; set; }
        public DateTime? EffectiveFrom { get; set; }
        public DateTime? EffectiveTo { get; set; }
        public string? MetadataJson { get; set; }
    }

    public sealed class BillingRateCardEntryUpsertRequest
    {
        public string? Id { get; set; }
        public string RateCardId { get; set; } = string.Empty;
        public string? EntryType { get; set; }
        public string? TimekeeperRole { get; set; }
        public string? EmployeeId { get; set; }
        public string? ClientId { get; set; }
        public string? MatterId { get; set; }
        public string? TaskCode { get; set; }
        public string? ActivityCode { get; set; }
        public string? ExpenseCode { get; set; }
        public string? Unit { get; set; }
        public decimal Rate { get; set; }
        public decimal? MinimumUnits { get; set; }
        public decimal? MaximumUnits { get; set; }
        public string? Status { get; set; }
        public int? Priority { get; set; }
        public string? MetadataJson { get; set; }
    }

    public sealed class PrebillGenerateRequest
    {
        public string MatterId { get; set; } = string.Empty;
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }
        public bool? IncludeUnapproved { get; set; }
        public DateTime? AsOfUtc { get; set; }
    }

    public sealed class PrebillGenerationResult
    {
        public BillingPrebillBatch Batch { get; set; } = null!;
        public IReadOnlyList<BillingPrebillLine> Lines { get; set; } = [];
        public string? ReviewItemId { get; set; }
        public IReadOnlyList<string> Warnings { get; set; } = [];
    }

    public sealed class BillingPrebillBatchQuery
    {
        public string? MatterId { get; set; }
        public string? ClientId { get; set; }
        public string? Status { get; set; }
        public DateTime? PeriodStart { get; set; }
        public DateTime? PeriodEnd { get; set; }
        public int? Limit { get; set; }
    }

    public sealed class PrebillDetailResult
    {
        public BillingPrebillBatch Batch { get; set; } = null!;
        public IReadOnlyList<BillingPrebillLine> Lines { get; set; } = [];
        public IReadOnlyList<BillingLedgerEntry> LedgerEntries { get; set; } = [];
    }

    public sealed class PrebillLineAdjustmentRequest
    {
        public bool? Exclude { get; set; }
        public string? Status { get; set; }
        public decimal? Quantity { get; set; }
        public decimal? Rate { get; set; }
        public decimal? ProposedAmount { get; set; }
        public decimal? ApprovedAmount { get; set; }
        public decimal? DiscountAmount { get; set; }
        public decimal? WriteDownAmount { get; set; }
        public decimal? TaxAmount { get; set; }
        public bool? RecomputeApprovedFromProposed { get; set; }
        public string? TaskCode { get; set; }
        public string? ActivityCode { get; set; }
        public string? ExpenseCode { get; set; }
        public string? TaxCode { get; set; }
        public string? Description { get; set; }
        public string? ThirdPartyPayorClientId { get; set; }
        public string? SplitAllocationJson { get; set; }
        public IReadOnlyList<SplitAllocationComponentRequest>? SplitAllocations { get; set; }
        public string? ReviewerNotes { get; set; }
    }

    public sealed class FinalizePrebillRequest
    {
        public string? InvoiceNumber { get; set; }
        public DateTime? IssueDate { get; set; }
        public DateTime? DueDate { get; set; }
        public bool? MarkAsSent { get; set; }
        public string? Terms { get; set; }
        public string? Notes { get; set; }
    }

    public sealed class FinalizePrebillResult
    {
        public BillingPrebillBatch Batch { get; set; } = null!;
        public Invoice Invoice { get; set; } = null!;
    }

    public sealed class BillingLedgerQuery
    {
        public string? LedgerDomain { get; set; }
        public string? LedgerBucket { get; set; }
        public string? InvoiceId { get; set; }
        public string? MatterId { get; set; }
        public string? PaymentTransactionId { get; set; }
        public DateTime? FromUtc { get; set; }
        public DateTime? ToUtc { get; set; }
        public int? Limit { get; set; }
    }

    public sealed class ManualLedgerEntryRequest
    {
        public string? EntryType { get; set; }
        public string? LedgerDomain { get; set; }
        public string LedgerBucket { get; set; } = string.Empty;
        public string? Currency { get; set; }
        public decimal Amount { get; set; }
        public string? MatterId { get; set; }
        public string? ClientId { get; set; }
        public string? PayorClientId { get; set; }
        public string? InvoicePayorAllocationId { get; set; }
        public string? InvoiceId { get; set; }
        public string? InvoiceLineItemId { get; set; }
        public string? PaymentTransactionId { get; set; }
        public string? TrustTransactionId { get; set; }
        public string? Description { get; set; }
        public string? MetadataJson { get; set; }
        public string? IdempotencyKey { get; set; }
        public bool? DistributeByPayor { get; set; }
        public DateTime? PostedAt { get; set; }
    }

    public sealed class LedgerReversalRequest
    {
        public string? Reason { get; set; }
        public DateTime? PostedAt { get; set; }
    }

    public sealed class ApplyPaymentAllocationRequest
    {
        public string PaymentTransactionId { get; set; } = string.Empty;
        public string InvoiceId { get; set; } = string.Empty;
        public string? InvoiceLineItemId { get; set; }
        public string? MatterId { get; set; }
        public string? ClientId { get; set; }
        public string? PayorClientId { get; set; }
        public string? InvoicePayorAllocationId { get; set; }
        public string? TrustSourceClientId { get; set; }
        public string? TrustAccountId { get; set; }
        public string? TrustLedgerId { get; set; }
        public decimal Amount { get; set; }
        public string? AllocationType { get; set; }
        public string? FundSource { get; set; }
        public bool? ApplyInvoiceHeaderIfNotAlreadyApplied { get; set; }
        public string? Reference { get; set; }
        public string? Notes { get; set; }
        public string? IdempotencyKey { get; set; }
        public DateTime? AppliedAt { get; set; }
    }

    public sealed class ReversePaymentAllocationRequest
    {
        public string? Reason { get; set; }
        public DateTime? ReversedAt { get; set; }
    }

    public sealed class TrustReconciliationRequest
    {
        public string? TrustAccountId { get; set; }
        public DateTime? AsOfUtc { get; set; }
    }

    public sealed class TrustThreeWayReconciliationResult
    {
        public DateTime AsOfUtc { get; set; }
        public IReadOnlyList<TrustThreeWayReconciliationAccountItem> Accounts { get; set; } = [];
        public TrustThreeWayReconciliationTotals Totals { get; set; } = new();
    }

    public sealed class TrustThreeWayReconciliationAccountItem
    {
        public string TrustAccountId { get; set; } = string.Empty;
        public string? TrustAccountName { get; set; }
        public decimal BankBalance { get; set; }
        public decimal ClientLedgerTotal { get; set; }
        public decimal TrustTransactionsNet { get; set; }
        public decimal BillingTrustLedgerTotal { get; set; }
        public decimal BankVsClientLedgerDiff { get; set; }
        public decimal ClientLedgerVsTrustLedgerDiff { get; set; }
        public decimal BankVsTrustLedgerDiff { get; set; }
    }

    public sealed class TrustThreeWayReconciliationTotals
    {
        public decimal BankBalance { get; set; }
        public decimal ClientLedgerTotal { get; set; }
        public decimal TrustTransactionsNet { get; set; }
        public decimal BillingTrustLedgerTotal { get; set; }
        public decimal BankVsClientLedgerDiff { get; set; }
        public decimal ClientLedgerVsTrustLedgerDiff { get; set; }
        public decimal BankVsTrustLedgerDiff { get; set; }
    }

    public sealed class LedesPreviewResult
    {
        public string PrebillId { get; set; } = string.Empty;
        public string Format { get; set; } = "none";
        public string Currency { get; set; } = "USD";
        public IReadOnlyList<string> Warnings { get; set; } = [];
        public IReadOnlyList<LedesPreviewLineRecord> Lines { get; set; } = [];
        public string PreviewText { get; set; } = string.Empty;
    }

    public sealed class LedesPreviewLineRecord
    {
        public int LineNo { get; set; }
        public string LineId { get; set; } = string.Empty;
        public string LineType { get; set; } = string.Empty;
        public DateTime? ServiceDate { get; set; }
        public string? TaskCode { get; set; }
        public string? ActivityCode { get; set; }
        public string? ExpenseCode { get; set; }
        public decimal Quantity { get; set; }
        public decimal Rate { get; set; }
        public decimal LineAmount { get; set; }
        public decimal TaxAmount { get; set; }
        public string? ThirdPartyPayorClientId { get; set; }
        public string? Description { get; set; }
    }
}
