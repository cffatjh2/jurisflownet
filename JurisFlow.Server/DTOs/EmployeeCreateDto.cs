using System.ComponentModel.DataAnnotations;
using JurisFlow.Server.Enums;

namespace JurisFlow.Server.DTOs
{
    public class EmployeeCreateDto
    {
        [Required]
        public string FirstName { get; set; } = string.Empty;

        [Required]
        public string LastName { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        /// <summary>
        /// Initial password for the staff member.
        /// </summary>
        public string? Password { get; set; }

        public string? Phone { get; set; }
        public string? Mobile { get; set; }

        [Required]
        public EmployeeRole Role { get; set; }

        public EmployeeStatus Status { get; set; } = EmployeeStatus.Active;

        public DateTime? HireDate { get; set; }

        public double? HourlyRate { get; set; }
        public double? Salary { get; set; }

        public string? Notes { get; set; }
        public string? Address { get; set; }
        public string? EmergencyContact { get; set; }
        public string? EmergencyPhone { get; set; }

        public string? EntityId { get; set; }
        public string? OfficeId { get; set; }

        public BarLicenseDto? BarLicense { get; set; }
    }

    public class BarLicenseDto
    {
        public string BarNumber { get; set; } = string.Empty;
        public string Jurisdiction { get; set; } = string.Empty;
        public DateTime? AdmissionDate { get; set; }
        public BarLicenseStatus Status { get; set; } = BarLicenseStatus.Active;
    }
}
