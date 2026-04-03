using System;
using System.ComponentModel.DataAnnotations;

namespace JurisFlow.Server.Models
{
    public class BillingSettings
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public double DefaultHourlyRate { get; set; } = 350;
        public double PartnerRate { get; set; } = 500;
        public double AssociateRate { get; set; } = 300;
        public double ParalegalRate { get; set; } = 150;

        public int BillingIncrement { get; set; } = 6;
        public int MinimumTimeEntry { get; set; } = 6;
        public string RoundingRule { get; set; } = "up";

        public int DefaultPaymentTerms { get; set; } = 30;
        public string InvoicePrefix { get; set; } = "INV-";
        public double DefaultTaxRate { get; set; } = 0;

        public bool LedesEnabled { get; set; } = false;
        public bool UtbmsCodesRequired { get; set; } = false;

        public double EvergreenRetainerMinimum { get; set; } = 5000;
        public bool TrustBalanceAlerts { get; set; } = true;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
