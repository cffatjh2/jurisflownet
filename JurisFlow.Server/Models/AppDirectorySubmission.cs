using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace JurisFlow.Server.Models
{
    public class AppDirectorySubmission
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(128)]
        public string ListingId { get; set; } = string.Empty;

        [ForeignKey(nameof(ListingId))]
        public AppDirectoryListing? Listing { get; set; }

        [Required]
        [MaxLength(128)]
        public string SubmittedBy { get; set; } = string.Empty;

        [Required]
        [MaxLength(32)]
        public string Status { get; set; } = "submitted";

        [Required]
        public string ManifestJson { get; set; } = "{}";

        public string? ValidationErrorsJson { get; set; }
        public string? TestReportJson { get; set; }

        [MaxLength(32)]
        public string TestStatus { get; set; } = "pending";

        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
