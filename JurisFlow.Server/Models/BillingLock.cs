using System;
using System.ComponentModel.DataAnnotations;

namespace JurisFlow.Server.Models
{
    public class BillingLock
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        public DateTime PeriodStart { get; set; }

        [Required]
        public DateTime PeriodEnd { get; set; }

        public string? LockedByUserId { get; set; }
        public DateTime LockedAt { get; set; } = DateTime.UtcNow;
        public string? Notes { get; set; }
    }
}
