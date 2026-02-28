using System;
using System.ComponentModel.DataAnnotations;

namespace JurisFlow.Server.Models
{
    /// <summary>
    /// Payment transaction record for online payments
    /// </summary>
    public class PaymentTransaction
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// Related invoice ID
        /// </summary>
        public string? InvoiceId { get; set; }

        public string? MatterId { get; set; }

        public string? ClientId { get; set; }

        public string? PayorClientId { get; set; }

        public string? InvoicePayorAllocationId { get; set; }

        [Required]
        public decimal Amount { get; set; }

        // UTBMS/LEDES task/expense codes (optional)
        public string? TaskCode { get; set; }
        public string? ExpenseCode { get; set; }
        public string? ActivityCode { get; set; }

        public string Currency { get; set; } = "USD";

        /// <summary>
        /// Stripe, LawPay, PayPal, Check, Wire, Cash
        /// </summary>
        public string PaymentMethod { get; set; } = "Stripe";

        /// <summary>
        /// card | ach | echeck | card_or_ach (provider capability dependent)
        /// </summary>
        public string? PaymentRail { get; set; }

        /// <summary>
        /// External session identifier (e.g., Stripe Checkout Session)
        /// </summary>
        public string? ProviderSessionId { get; set; }

        /// <summary>
        /// External payment intent identifier
        /// </summary>
        public string? ProviderPaymentIntentId { get; set; }

        /// <summary>
        /// External charge identifier
        /// </summary>
        public string? ProviderChargeId { get; set; }

        /// <summary>
        /// External refund identifier
        /// </summary>
        public string? ProviderRefundId { get; set; }

        /// <summary>
        /// External customer identifier
        /// </summary>
        public string? ProviderCustomerId { get; set; }

        /// <summary>
        /// External transaction ID from payment provider
        /// </summary>
        public string? ExternalTransactionId { get; set; }

        /// <summary>
        /// Pending, Processing, Succeeded, Failed, Refunded, Partially Refunded
        /// </summary>
        public string Status { get; set; } = "Pending";

        public string? FailureReason { get; set; }

        public decimal? RefundAmount { get; set; }

        public string? RefundReason { get; set; }

        public DateTime? RefundedAt { get; set; }

        public string? ReceiptUrl { get; set; }

        public string? PayerEmail { get; set; }

        public string? PayerName { get; set; }

        /// <summary>
        /// Last 4 digits of card
        /// </summary>
        public string? CardLast4 { get; set; }

        public string? CardBrand { get; set; } // Visa, Mastercard, Amex, etc.

        public string? ProcessedBy { get; set; } // User ID

        public string? PaymentPlanId { get; set; }

        public DateTime? ScheduledFor { get; set; }

        public string? Source { get; set; } // Manual, AutoPay, Plan

        public DateTime? ProcessedAt { get; set; }

        /// <summary>
        /// Cumulative amount applied to invoice balance (idempotency guard).
        /// </summary>
        public decimal InvoiceAppliedAmount { get; set; }

        /// <summary>
        /// Cumulative refunded amount already reversed from invoice.
        /// </summary>
        public decimal InvoiceRefundAppliedAmount { get; set; }

        public DateTime? InvoiceAppliedAt { get; set; }
        public DateTime? InvoiceRefundAppliedAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
