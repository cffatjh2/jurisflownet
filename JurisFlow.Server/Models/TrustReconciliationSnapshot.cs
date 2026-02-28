using System.ComponentModel.DataAnnotations;

namespace JurisFlow.Server.Models
{
    public class TrustReconciliationSnapshot
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [MaxLength(128)]
        public string? TrustAccountId { get; set; } // null => aggregate across all visible trust accounts

        public DateTime AsOfUtc { get; set; } = DateTime.UtcNow;

        public int AccountCount { get; set; }
        public int MismatchedAccountCount { get; set; }

        public decimal BankBalance { get; set; }
        public decimal ClientLedgerTotal { get; set; }
        public decimal TrustTransactionsNet { get; set; }
        public decimal BillingTrustLedgerTotal { get; set; }

        public decimal BankVsClientLedgerDiff { get; set; }
        public decimal ClientLedgerVsTrustLedgerDiff { get; set; }
        public decimal BankVsTrustLedgerDiff { get; set; }

        [MaxLength(24)]
        public string DataQuality { get; set; } = "computed";

        [MaxLength(32)]
        public string Source { get; set; } = "api_read";

        [MaxLength(128)]
        public string? CapturedBy { get; set; }

        public string? MetadataJson { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
