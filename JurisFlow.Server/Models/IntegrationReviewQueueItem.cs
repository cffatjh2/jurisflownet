using System.ComponentModel.DataAnnotations;

namespace JurisFlow.Server.Models
{
    public class IntegrationReviewQueueItem
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [MaxLength(128)]
        public string? ConnectionId { get; set; }

        [MaxLength(128)]
        public string? RunId { get; set; }

        [Required]
        [MaxLength(128)]
        public string ProviderKey { get; set; } = string.Empty;

        [Required]
        [MaxLength(64)]
        public string ItemType { get; set; } = "manual_review";

        [MaxLength(128)]
        public string? SourceId { get; set; }

        [MaxLength(64)]
        public string? SourceType { get; set; }

        [MaxLength(128)]
        public string? ConflictId { get; set; }

        [Required]
        [MaxLength(32)]
        public string Status { get; set; } = "pending";

        [Required]
        [MaxLength(16)]
        public string Priority { get; set; } = "medium";

        [MaxLength(160)]
        public string? Title { get; set; }

        [MaxLength(2048)]
        public string? Summary { get; set; }

        public string? ContextJson { get; set; }
        public string? SuggestedActionsJson { get; set; }

        [MaxLength(32)]
        public string? Decision { get; set; }

        [MaxLength(2048)]
        public string? DecisionNotes { get; set; }

        [MaxLength(128)]
        public string? AssignedTo { get; set; }

        [MaxLength(128)]
        public string? ReviewedBy { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public DateTime? DueAt { get; set; }
        public DateTime? ResolvedAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
