using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace JurisFlow.Server.Models
{
    public class EfilingSubmission
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(128)]
        public string ProviderKey { get; set; } = string.Empty;

        [Required]
        [MaxLength(128)]
        public string ExternalSubmissionId { get; set; } = string.Empty;

        [MaxLength(128)]
        public string? ExternalDocketId { get; set; }

        [MaxLength(128)]
        public string? ReferenceNumber { get; set; }

        [MaxLength(32)]
        public string Status { get; set; } = "pending";

        public string? MatterId { get; set; }

        [ForeignKey("MatterId")]
        [JsonIgnore]
        public Matter? Matter { get; set; }

        public DateTime? SubmittedAt { get; set; }

        public DateTime? AcceptedAt { get; set; }

        public DateTime? RejectedAt { get; set; }

        [MaxLength(1024)]
        public string? RejectionReason { get; set; }

        public string? MetadataJson { get; set; }

        public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
