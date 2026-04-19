using JurisFlow.Server.Models;

namespace JurisFlow.Server.Contracts
{
    public sealed class TrustBankAccountListItemDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string BankName { get; set; } = string.Empty;
        public string Jurisdiction { get; set; } = string.Empty;
        public string AccountType { get; set; } = string.Empty;
        public string? ResponsibleLawyerUserId { get; set; }
        public string StatementCadence { get; set; } = string.Empty;
        public bool OverdraftNotificationEnabled { get; set; }
        public decimal CurrentBalance { get; set; }
        public decimal ClearedBalance { get; set; }
        public decimal UnclearedBalance { get; set; }
        public decimal AvailableDisbursementCapacity { get; set; }
        public TrustAccountStatus Status { get; set; }
        public string? EntityId { get; set; }
        public string? OfficeId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public sealed class ClientTrustLedgerListItemDto
    {
        public string Id { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public string? ClientName { get; set; }
        public string? MatterId { get; set; }
        public string TrustAccountId { get; set; } = string.Empty;
        public string? TrustAccountName { get; set; }
        public string? EntityId { get; set; }
        public string? OfficeId { get; set; }
        public decimal RunningBalance { get; set; }
        public decimal ClearedBalance { get; set; }
        public decimal UnclearedBalance { get; set; }
        public decimal AvailableToDisburse { get; set; }
        public decimal HoldAmount { get; set; }
        public LedgerStatus Status { get; set; }
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public sealed class TrustTransactionListItemDto
    {
        public string Id { get; set; } = string.Empty;
        public string TrustAccountId { get; set; } = string.Empty;
        public string? MatterId { get; set; }
        public string? MatterName { get; set; }
        public string Type { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string? Description { get; set; }
        public string? Reference { get; set; }
        public string? PayorPayee { get; set; }
        public string? CheckNumber { get; set; }
        public string? LedgerId { get; set; }
        public string? DisbursementClass { get; set; }
        public string? ApprovalStatus { get; set; }
        public string? EntityId { get; set; }
        public string? OfficeId { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? CreatedBy { get; set; }
        public string? ApprovedBy { get; set; }
        public DateTime? ApprovedAt { get; set; }
        public string? RejectedBy { get; set; }
        public DateTime? RejectedAt { get; set; }
        public string? RejectionReason { get; set; }
        public bool IsVoided { get; set; }
        public DateTime? VoidedAt { get; set; }
        public string? VoidReason { get; set; }
        public string ClearingStatus { get; set; } = string.Empty;
        public DateTime? ClearedAt { get; set; }
        public DateTime? ReturnedAt { get; set; }
        public string? ReturnReason { get; set; }
        public bool IsEarned { get; set; }
        public DateTime? EarnedDate { get; set; }
        public decimal BalanceBefore { get; set; }
        public decimal BalanceAfter { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public sealed class ReconciliationRecordListItemDto
    {
        public string Id { get; set; } = string.Empty;
        public string TrustAccountId { get; set; } = string.Empty;
        public string? TrustAccountName { get; set; }
        public DateTime PeriodEnd { get; set; }
        public decimal BankStatementBalance { get; set; }
        public decimal TrustLedgerBalance { get; set; }
        public decimal ClientLedgerSumBalance { get; set; }
        public bool IsReconciled { get; set; }
        public decimal DiscrepancyAmount { get; set; }
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
