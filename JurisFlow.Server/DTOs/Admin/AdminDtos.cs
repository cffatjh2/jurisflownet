using System.ComponentModel.DataAnnotations;

namespace JurisFlow.Server.DTOs.Admin
{
    public class CreateUserDto
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string Name { get; set; } = string.Empty;

        public string? Role { get; set; }
        public string? Password { get; set; }
    }

    public class UpdateUserDto
    {
        public string? Name { get; set; }

        [EmailAddress]
        public string? Email { get; set; }

        public string? Role { get; set; }
        public string? Password { get; set; }
    }

    public class UpdateClientDto
    {
        public string? Name { get; set; }

        [EmailAddress]
        public string? Email { get; set; }

        public string? Phone { get; set; }
        public string? Mobile { get; set; }
        public string? Company { get; set; }
        public string? Type { get; set; }
        public string? Status { get; set; }
        public bool? PortalEnabled { get; set; }
        public string? StatusChangeNote { get; set; }
        public string? Password { get; set; }
    }

    public class CreateBillingLockDto
    {
        public string PeriodStart { get; set; } = string.Empty;
        public string PeriodEnd { get; set; } = string.Empty;
        public string? Notes { get; set; }
    }

    public class AdminUserSummaryDto
    {
        public string Id { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
    }

    public class AdminClientListItemDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? Company { get; set; }
        public string Type { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public bool PortalEnabled { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public class AdminClientResponseDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? Phone { get; set; }
        public string? Mobile { get; set; }
        public string? Company { get; set; }
        public string? Type { get; set; }
        public string? Status { get; set; }
        public bool PortalEnabled { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}
