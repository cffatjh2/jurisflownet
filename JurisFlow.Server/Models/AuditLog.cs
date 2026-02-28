using System;
using System.ComponentModel.DataAnnotations;

namespace JurisFlow.Server.Models
{
    public class AuditLog
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public string? UserId { get; set; }
        public string? ClientId { get; set; }
        public string? TenantId { get; set; }
        public string? Role { get; set; }

        [Required]
        [MaxLength(128)]
        public string Action { get; set; } = string.Empty;

        [MaxLength(128)]
        public string? Entity { get; set; }

        [MaxLength(128)]
        public string? EntityId { get; set; }

        public string? Details { get; set; }
        public string? IpAddress { get; set; }
        public string? UserAgent { get; set; }

        public long Sequence { get; set; }

        [MaxLength(128)]
        public string? PreviousHash { get; set; }

        [MaxLength(128)]
        public string? Hash { get; set; }

        [MaxLength(32)]
        public string? HashAlgorithm { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
