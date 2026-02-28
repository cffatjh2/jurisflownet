using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace JurisFlow.Server.Models
{
    public class User
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [EmailAddress]
        public string Email { get; set; }

        [Required]
        [MaxLength(320)]
        [JsonIgnore]
        public string NormalizedEmail { get; set; } = string.Empty;

        [Required]
        public string Name { get; set; }

        [Required]
        public string Role { get; set; } // Admin | Partner | Associate | Employee

        [Required]
        [JsonIgnore]
        public string PasswordHash { get; set; }

        public string? Phone { get; set; }
        public string? Mobile { get; set; }
        public string? Address { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public string? ZipCode { get; set; }
        public string? Country { get; set; }
        public string? BarNumber { get; set; }
        public string? Bio { get; set; }
        public string? Avatar { get; set; }
        public string? Preferences { get; set; } // JSON string
        public string? NotificationPreferences { get; set; } // JSON string
        public string? EmployeeRole { get; set; }
        public bool MfaEnabled { get; set; } = false;
        public string? MfaSecret { get; set; }
        public string? MfaBackupCodesJson { get; set; }
        public DateTime? MfaVerifiedAt { get; set; }
        public DateTime? MfaLastUsedAt { get; set; }

        public DateTime? CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
    }
}
