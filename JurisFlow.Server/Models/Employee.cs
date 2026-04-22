using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using JurisFlow.Server.Enums;

namespace JurisFlow.Server.Models
{
    public class Employee
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        public string FirstName { get; set; }

        [Required]
        public string LastName { get; set; }

        [Required]
        [EmailAddress]
        public string Email { get; set; }

        public string? Phone { get; set; }
        public string? Mobile { get; set; }

        [Required]
        public EmployeeRole Role { get; set; }

        public EmployeeStatus Status { get; set; } = EmployeeStatus.Active;

        public DateTime HireDate { get; set; } = DateTime.UtcNow;
        public DateTime? TerminationDate { get; set; }

        public double? HourlyRate { get; set; }
        public double? Salary { get; set; }

        public string? Notes { get; set; }
        public string? Address { get; set; }
        public string? EmergencyContact { get; set; }
        public string? EmergencyPhone { get; set; }

        public string? EntityId { get; set; }

        [ForeignKey("EntityId")]
        public FirmEntity? Entity { get; set; }

        public string? OfficeId { get; set; }

        [ForeignKey("OfficeId")]
        public Office? Office { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Bar License Information
        public string? BarNumber { get; set; }
        public string? BarJurisdiction { get; set; }
        public DateTime? BarAdmissionDate { get; set; }
        public BarLicenseStatus? BarStatus { get; set; }

        // Link to Login User
        public string? UserId { get; set; }
        
        [ForeignKey("UserId")]
        [JsonIgnore]
        public User? User { get; set; }
    }
}
