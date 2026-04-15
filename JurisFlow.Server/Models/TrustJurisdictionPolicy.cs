using System.ComponentModel.DataAnnotations;

namespace JurisFlow.Server.Models
{
    public class TrustJurisdictionPolicy
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(64)]
        public string PolicyKey { get; set; } = "default";

        [Required]
        [MaxLength(24)]
        public string Jurisdiction { get; set; } = "DEFAULT";

        [MaxLength(128)]
        public string? Name { get; set; }

        [Required]
        [MaxLength(24)]
        public string AccountType { get; set; } = "all";

        public int VersionNumber { get; set; } = 1;
        public bool IsActive { get; set; } = true;
        public bool IsSystemBaseline { get; set; }
        public bool RequireMakerChecker { get; set; } = true;
        public bool RequireOverrideReason { get; set; } = true;
        public decimal DualApprovalThreshold { get; set; } = 10000m;
        public decimal ResponsibleLawyerApprovalThreshold { get; set; } = 25000m;
        public decimal SignatoryApprovalThreshold { get; set; } = 5000m;
        public int MonthlyCloseCadenceDays { get; set; } = 30;
        public int ExceptionAgingSlaHours { get; set; } = 48;
        public int RetentionPeriodMonths { get; set; } = 60;
        public bool RequireMonthlyThreeWayReconciliation { get; set; } = true;
        public bool RequireResponsibleLawyerAssignment { get; set; } = true;
        public string? DisbursementClassesRequiringSignatoryJson { get; set; }
        public string? OperationalApproverRolesJson { get; set; }
        public string? OverrideApproverRolesJson { get; set; }
        public string? MetadataJson { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
