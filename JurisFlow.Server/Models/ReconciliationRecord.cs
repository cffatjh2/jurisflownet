using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace JurisFlow.Server.Models
{
    public class ReconciliationRecord
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public string TrustAccountId { get; set; }
        [ForeignKey("TrustAccountId")]
        [JsonIgnore]
        public TrustBankAccount? TrustAccount { get; set; }

        public DateTime PeriodEnd { get; set; }
        public decimal BankStatementBalance { get; set; }
        public decimal TrustLedgerBalance { get; set; }
        public decimal ClientLedgerSumBalance { get; set; }
        public bool IsReconciled { get; set; }
        public decimal DiscrepancyAmount { get; set; } = 0m;
        public string? Notes { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
