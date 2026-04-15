using System;
using System.ComponentModel.DataAnnotations;

namespace JurisFlow.Server.Models
{
    public enum TrustAccountStatus
    {
        ACTIVE,
        INACTIVE,
        CLOSED
    }

    public class TrustBankAccount
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; }
        public string BankName { get; set; }
        public string AccountNumberEnc { get; set; } // Encrypted in theory, storing raw for now as per demo
        public string RoutingNumber { get; set; }
        public string Jurisdiction { get; set; }
        public string AccountType { get; set; } = "iolta";
        public string? ResponsibleLawyerUserId { get; set; }
        public string? AllowedSignatoriesJson { get; set; }
        public string? JurisdictionPolicyKey { get; set; }
        public string StatementCadence { get; set; } = "monthly";
        public bool OverdraftNotificationEnabled { get; set; } = true;
        public string? BankReferenceMetadataJson { get; set; }
        public decimal CurrentBalance { get; set; } = 0m;
        public decimal ClearedBalance { get; set; } = 0m;
        public decimal UnclearedBalance { get; set; } = 0m;
        public decimal AvailableDisbursementCapacity { get; set; } = 0m;

        [Required]
        [MaxLength(32)]
        [ConcurrencyCheck]
        public string RowVersion { get; set; } = Guid.NewGuid().ToString("N");

        public TrustAccountStatus Status { get; set; } = TrustAccountStatus.ACTIVE;
        public string? EntityId { get; set; }
        public string? OfficeId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
