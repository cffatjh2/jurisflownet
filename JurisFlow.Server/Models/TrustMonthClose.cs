using System.ComponentModel.DataAnnotations;

namespace JurisFlow.Server.Models
{
    public class TrustMonthClose
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(128)]
        public string TrustAccountId { get; set; } = string.Empty;

        [Required]
        [MaxLength(64)]
        public string PolicyKey { get; set; } = string.Empty;

        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }

        [MaxLength(128)]
        public string? ReconciliationPacketId { get; set; }

        public int VersionNumber { get; set; } = 1;
        public bool IsCanonical { get; set; } = true;

        [Required]
        [MaxLength(24)]
        public string Status { get; set; } = "draft"; // draft | in_progress | ready_for_signoff | partially_signed | closed

        public int OpenExceptionCount { get; set; }

        [MaxLength(128)]
        public string? PreparedBy { get; set; }
        public DateTime PreparedAt { get; set; } = DateTime.UtcNow;

        [MaxLength(128)]
        public string? ReviewerSignedBy { get; set; }
        public DateTime? ReviewerSignedAt { get; set; }

        [MaxLength(128)]
        public string? ResponsibleLawyerSignedBy { get; set; }
        public DateTime? ResponsibleLawyerSignedAt { get; set; }

        [MaxLength(128)]
        public string? ReopenedFromMonthCloseId { get; set; }
        [MaxLength(128)]
        public string? SupersededByMonthCloseId { get; set; }
        [MaxLength(128)]
        public string? ReopenedBy { get; set; }
        public DateTime? ReopenedAt { get; set; }
        [MaxLength(2048)]
        public string? ReopenReason { get; set; }
        [MaxLength(128)]
        public string? SupersededBy { get; set; }
        [MaxLength(2048)]
        public string? SupersedeReason { get; set; }
        public DateTime? SupersededAt { get; set; }

        public string? SummaryJson { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class TrustMonthCloseStep
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(128)]
        public string TrustMonthCloseId { get; set; } = string.Empty;

        [Required]
        [MaxLength(48)]
        public string StepKey { get; set; } = string.Empty;

        [Required]
        [MaxLength(24)]
        public string Status { get; set; } = "pending"; // pending | ready | completed | blocked

        [MaxLength(2048)]
        public string? Notes { get; set; }

        [MaxLength(128)]
        public string? CompletedBy { get; set; }
        public DateTime? CompletedAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
