using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace JurisFlow.Server.Models
{
    public class TrustTransaction
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        public string TrustAccountId { get; set; } = string.Empty;

        public string? MatterId { get; set; }

        [ForeignKey("MatterId")]
        [JsonIgnore]
        public Matter? Matter { get; set; }

        [Required]
        public string Type { get; set; } // Deposit, Withdrawal, etc.

        public decimal Amount { get; set; }
        public string Description { get; set; }
        public string? Reference { get; set; }
        public string? PayorPayee { get; set; }
        public string? CheckNumber { get; set; }

        public string? LedgerId { get; set; }
        public string? AllocationsJson { get; set; }
        public string? PostingBatchId { get; set; }
        public string? PrimaryJournalEntryId { get; set; }
        public string? DisbursementClass { get; set; }
        public string? ApprovalStatus { get; set; }
        public string? PolicyDecisionJson { get; set; }

        public string? EntityId { get; set; }

        public string? OfficeId { get; set; }

        [Required]
        [MaxLength(32)]
        [ConcurrencyCheck]
        public string RowVersion { get; set; } = Guid.NewGuid().ToString("N");

        public string Status { get; set; } = "PENDING";
        public string? CreatedBy { get; set; }
        public string? ApprovedBy { get; set; }
        public DateTime? ApprovedAt { get; set; }
        public string? RejectedBy { get; set; }
        public DateTime? RejectedAt { get; set; }
        public string? RejectionReason { get; set; }

        public bool IsVoided { get; set; } = false;
        public DateTime? VoidedAt { get; set; }
        public string? VoidReason { get; set; }
        public string ClearingStatus { get; set; } = "not_applicable"; // not_applicable | pending_clearance | cleared | returned
        public DateTime? ClearedAt { get; set; }
        public DateTime? ReturnedAt { get; set; }
        public string? ReturnReason { get; set; }

        public bool IsEarned { get; set; } = false;
        public DateTime? EarnedDate { get; set; }

        public decimal BalanceBefore { get; set; } = 0m;
        public decimal BalanceAfter { get; set; } = 0m;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
