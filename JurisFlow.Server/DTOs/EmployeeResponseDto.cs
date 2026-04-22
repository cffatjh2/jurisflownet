using JurisFlow.Server.Enums;

namespace JurisFlow.Server.DTOs
{
    public sealed class EmployeeResponseDto
    {
        public string Id { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? Avatar { get; set; }
        public string? Phone { get; set; }
        public string? Mobile { get; set; }
        public EmployeeRole Role { get; set; }
        public EmployeeStatus Status { get; set; }
        public DateTime HireDate { get; set; }
        public DateTime? TerminationDate { get; set; }
        public double? HourlyRate { get; set; }
        public double? Salary { get; set; }
        public string? UserId { get; set; }
        public string? Notes { get; set; }
        public string? Address { get; set; }
        public string? EmergencyContact { get; set; }
        public string? EmergencyPhone { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string? BarNumber { get; set; }
        public string? BarJurisdiction { get; set; }
        public DateTime? BarAdmissionDate { get; set; }
        public BarLicenseStatus? BarStatus { get; set; }
        public string? EntityId { get; set; }
        public string? OfficeId { get; set; }
    }

    public sealed class EmployeeDirectoryItemDto
    {
        public string Id { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? Avatar { get; set; }
        public string? UserId { get; set; }
        public EmployeeRole Role { get; set; }
        public EmployeeStatus Status { get; set; }
        public string? EntityId { get; set; }
        public string? OfficeId { get; set; }
    }
}
