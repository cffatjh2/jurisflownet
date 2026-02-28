using System;
using System.ComponentModel.DataAnnotations;

namespace JurisFlow.Server.Models
{
    public class IntegrationRun
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(128)]
        public string ConnectionId { get; set; } = string.Empty;

        [Required]
        [MaxLength(128)]
        public string ProviderKey { get; set; } = string.Empty;

        [Required]
        [MaxLength(32)]
        public string Trigger { get; set; } = "scheduled";

        [Required]
        [MaxLength(32)]
        public string Status { get; set; } = "pending";

        [Required]
        [MaxLength(160)]
        public string IdempotencyKey { get; set; } = string.Empty;

        public int AttemptCount { get; set; }
        public int MaxAttempts { get; set; } = 1;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }

        [MaxLength(2048)]
        public string? CursorBefore { get; set; }

        [MaxLength(2048)]
        public string? CursorAfter { get; set; }

        [MaxLength(2048)]
        public string? DeltaTokenBefore { get; set; }

        [MaxLength(2048)]
        public string? DeltaTokenAfter { get; set; }

        [MaxLength(64)]
        public string? ErrorCode { get; set; }

        [MaxLength(2048)]
        public string? ErrorMessage { get; set; }

        public bool IsDeadLetter { get; set; }

        public string? ResultJson { get; set; }
    }
}
