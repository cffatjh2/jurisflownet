using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using JurisFlow.Server.Enums;

namespace JurisFlow.Server.Models
{
    public class Invoice
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [MaxLength(50)]
        public string? Number { get; set; }

        [Required]
        public string ClientId { get; set; } = string.Empty;

        public string? MatterId { get; set; }

        public string? EntityId { get; set; }

        public string? OfficeId { get; set; }

        public InvoiceStatus Status { get; set; } = InvoiceStatus.Draft;

        public DateTime IssueDate { get; set; } = DateTime.UtcNow;
        public DateTime? DueDate { get; set; }

        public decimal Subtotal { get; set; }
        public decimal Tax { get; set; }
        public decimal Discount { get; set; }
        public decimal Total { get; set; }
        public decimal AmountPaid { get; set; }
        public decimal Balance { get; set; }

        [MaxLength(500)]
        public string? Notes { get; set; }

        [MaxLength(500)]
        public string? Terms { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<InvoiceLineItem> LineItems { get; set; } = new List<InvoiceLineItem>();
    }
}
