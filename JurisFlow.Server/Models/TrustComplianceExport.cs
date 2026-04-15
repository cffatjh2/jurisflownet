using System.ComponentModel.DataAnnotations;

namespace JurisFlow.Server.Models
{
    public class TrustComplianceExport
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(48)]
        public string ExportType { get; set; } = string.Empty;

        [Required]
        [MaxLength(16)]
        public string Format { get; set; } = "json";

        [Required]
        [MaxLength(24)]
        public string Status { get; set; } = "completed";

        [MaxLength(128)]
        public string? TrustAccountId { get; set; }

        [MaxLength(128)]
        public string? ClientTrustLedgerId { get; set; }

        [MaxLength(128)]
        public string? TrustMonthCloseId { get; set; }

        [MaxLength(128)]
        public string? TrustReconciliationPacketId { get; set; }

        [Required]
        [MaxLength(256)]
        public string FileName { get; set; } = string.Empty;

        [Required]
        [MaxLength(128)]
        public string ContentType { get; set; } = "application/json";

        public string? SummaryJson { get; set; }
        public string? PayloadJson { get; set; }

        [MaxLength(128)]
        public string? GeneratedBy { get; set; }

        [MaxLength(128)]
        public string? ParentExportId { get; set; }

        [MaxLength(32)]
        public string IntegrityStatus { get; set; } = "unsigned";

        [MaxLength(64)]
        public string? RetentionPolicyTag { get; set; }

        [MaxLength(64)]
        public string? RedactionProfile { get; set; }

        public string? ProvenanceJson { get; set; }

        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
