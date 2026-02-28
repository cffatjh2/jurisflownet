using System;
using System.ComponentModel.DataAnnotations;

namespace JurisFlow.Server.Models
{
    public class AuthSession
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public string? UserId { get; set; }

        public string? ClientId { get; set; }

        public string? TenantId { get; set; }

        public string SubjectType { get; set; } = "User"; // User | Client

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;

        public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddHours(8);

        public string? IpAddress { get; set; }

        public string? UserAgent { get; set; }

        public DateTime? RevokedAt { get; set; }

        public string? RevokedReason { get; set; }

        public string? RefreshTokenHash { get; set; }

        public DateTime? RefreshTokenIssuedAt { get; set; }

        public DateTime? RefreshTokenExpiresAt { get; set; }

        public DateTime? RefreshTokenRotatedAt { get; set; }
    }
}
