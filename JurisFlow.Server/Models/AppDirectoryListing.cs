using System.ComponentModel.DataAnnotations;

namespace JurisFlow.Server.Models
{
    public class AppDirectoryListing
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(128)]
        public string ProviderKey { get; set; } = string.Empty;

        [Required]
        [MaxLength(160)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [MaxLength(64)]
        public string Category { get; set; } = "General";

        [Required]
        [MaxLength(16)]
        public string ConnectionMode { get; set; } = "oauth";

        [MaxLength(512)]
        public string Summary { get; set; } = string.Empty;

        [MaxLength(4000)]
        public string? Description { get; set; }

        [MaxLength(32)]
        public string ManifestVersion { get; set; } = "1.0";

        public string ManifestJson { get; set; } = "{}";

        [MaxLength(512)]
        public string? WebsiteUrl { get; set; }

        [MaxLength(512)]
        public string? DocumentationUrl { get; set; }

        [MaxLength(256)]
        public string? SupportEmail { get; set; }

        [MaxLength(512)]
        public string? SupportUrl { get; set; }

        [MaxLength(512)]
        public string? LogoUrl { get; set; }

        public bool SupportsWebhook { get; set; }
        public bool WebhookFirst { get; set; }
        public int? FallbackPollingMinutes { get; set; }

        [MaxLength(32)]
        public string SlaTier { get; set; } = "standard";
        public int? SlaResponseHours { get; set; }
        public int? SlaResolutionHours { get; set; }
        public double? SlaUptimePercent { get; set; }

        [MaxLength(32)]
        public string Status { get; set; } = "draft";

        public int SubmissionCount { get; set; }
        public DateTime? LastSubmittedAt { get; set; }

        [MaxLength(32)]
        public string LastTestStatus { get; set; } = "not_run";
        public DateTime? LastTestedAt { get; set; }

        [MaxLength(2048)]
        public string? LastTestSummary { get; set; }
        public string? LastTestReportJson { get; set; }

        [MaxLength(2048)]
        public string? ReviewNotes { get; set; }

        [MaxLength(128)]
        public string? ReviewedBy { get; set; }
        public DateTime? ReviewedAt { get; set; }

        public bool IsFeatured { get; set; }
        public DateTime? PublishedAt { get; set; }

        [MaxLength(128)]
        public string? CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
