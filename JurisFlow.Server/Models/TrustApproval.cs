using System.ComponentModel.DataAnnotations;

namespace JurisFlow.Server.Models
{
    public class TrustApprovalRequirement
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(128)]
        public string TrustTransactionId { get; set; } = string.Empty;

        [Required]
        [MaxLength(48)]
        public string RequirementType { get; set; } = "operational_approval";

        public int RequiredCount { get; set; } = 1;
        public int SatisfiedCount { get; set; } = 0;

        [Required]
        [MaxLength(24)]
        public string Status { get; set; } = "pending";

        [MaxLength(64)]
        public string? PolicyKey { get; set; }

        [MaxLength(128)]
        public string? Summary { get; set; }

        public string? MetadataJson { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class TrustApprovalDecision
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(128)]
        public string TrustTransactionId { get; set; } = string.Empty;

        [Required]
        [MaxLength(128)]
        public string TrustApprovalRequirementId { get; set; } = string.Empty;

        [Required]
        [MaxLength(128)]
        public string ActorUserId { get; set; } = string.Empty;

        [MaxLength(64)]
        public string? ActorRole { get; set; }

        [Required]
        [MaxLength(24)]
        public string DecisionType { get; set; } = "approve";

        [MaxLength(2048)]
        public string? Notes { get; set; }

        [MaxLength(2048)]
        public string? Reason { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class TrustApprovalOverride
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(128)]
        public string TrustTransactionId { get; set; } = string.Empty;

        [MaxLength(128)]
        public string? TrustApprovalRequirementId { get; set; }

        [Required]
        [MaxLength(128)]
        public string ActorUserId { get; set; } = string.Empty;

        [MaxLength(64)]
        public string? ActorRole { get; set; }

        [Required]
        [MaxLength(2048)]
        public string Reason { get; set; } = string.Empty;

        public string? MetadataJson { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
