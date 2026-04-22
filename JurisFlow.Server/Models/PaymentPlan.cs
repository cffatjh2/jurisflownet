using System;
using System.ComponentModel.DataAnnotations;

namespace JurisFlow.Server.Models
{
    public class PaymentPlan
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        public string ClientId { get; set; } = string.Empty;

        public string? InvoiceId { get; set; }

        [Required]
        public string Name { get; set; } = string.Empty;

        public decimal TotalAmount { get; set; }
        public decimal InstallmentAmount { get; set; }

        public string Frequency { get; set; } = "Monthly";

        public DateTime StartDate { get; set; } = DateTime.UtcNow;
        public DateTime NextRunDate { get; set; } = DateTime.UtcNow;
        public DateTime? EndDate { get; set; }

        public decimal RemainingAmount { get; set; }

        public string Status { get; set; } = "Active";

        public bool AutoPayEnabled { get; set; } = false;
        public string? AutoPayMethod { get; set; }
        public string? AutoPayReference { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
