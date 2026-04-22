using JurisFlow.Server.Enums;

namespace JurisFlow.Server.DTOs
{
    public sealed class InvoiceListItemDto
    {
        public string Id { get; set; } = string.Empty;
        public string? Number { get; set; }
        public string ClientId { get; set; } = string.Empty;
        public string? MatterId { get; set; }
        public string? EntityId { get; set; }
        public string? OfficeId { get; set; }
        public InvoiceStatus Status { get; set; }
        public DateTime IssueDate { get; set; }
        public DateTime? DueDate { get; set; }
        public decimal Subtotal { get; set; }
        public decimal Tax { get; set; }
        public decimal Discount { get; set; }
        public decimal Total { get; set; }
        public decimal AmountPaid { get; set; }
        public decimal Balance { get; set; }
        public int LineItemCount { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public sealed class PaymentPlanListItemDto
    {
        public string Id { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public string? InvoiceId { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal TotalAmount { get; set; }
        public decimal InstallmentAmount { get; set; }
        public string Frequency { get; set; } = string.Empty;
        public DateTime StartDate { get; set; }
        public DateTime NextRunDate { get; set; }
        public DateTime? EndDate { get; set; }
        public decimal RemainingAmount { get; set; }
        public string Status { get; set; } = string.Empty;
        public bool AutoPayEnabled { get; set; }
        public string? AutoPayMethod { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public sealed class TimeEntryListItemDto
    {
        public string Id { get; set; } = string.Empty;
        public string? MatterId { get; set; }
        public string Description { get; set; } = string.Empty;
        public int Duration { get; set; }
        public double Rate { get; set; }
        public DateTime Date { get; set; }
        public bool Billed { get; set; }
        public bool IsBillable { get; set; }
        public string Type { get; set; } = string.Empty;
        public string? ActivityCode { get; set; }
        public string? TaskCode { get; set; }
        public string ApprovalStatus { get; set; } = string.Empty;
        public DateTime? SubmittedAt { get; set; }
        public DateTime? ApprovedAt { get; set; }
        public DateTime? RejectedAt { get; set; }
        public string? RejectionReason { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public sealed class ExpenseListItemDto
    {
        public string Id { get; set; } = string.Empty;
        public string? MatterId { get; set; }
        public string Description { get; set; } = string.Empty;
        public double Amount { get; set; }
        public DateTime Date { get; set; }
        public string Category { get; set; } = string.Empty;
        public bool Billed { get; set; }
        public string Type { get; set; } = string.Empty;
        public string? ExpenseCode { get; set; }
        public string ApprovalStatus { get; set; } = string.Empty;
        public DateTime? SubmittedAt { get; set; }
        public DateTime? ApprovedAt { get; set; }
        public DateTime? RejectedAt { get; set; }
        public string? RejectionReason { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
