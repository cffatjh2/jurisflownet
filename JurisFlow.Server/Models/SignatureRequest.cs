using System;
using System.ComponentModel.DataAnnotations;

namespace JurisFlow.Server.Models
{
    /// <summary>
    /// E-Signature request for documents
    /// </summary>
    public class SignatureRequest
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        public string DocumentId { get; set; } = string.Empty;

        [Required]
        public string SignerEmail { get; set; } = string.Empty;

        public string SignerName { get; set; } = string.Empty;

        public string? MatterId { get; set; }

        public string? ClientId { get; set; }

        /// <summary>
        /// Pending, Sent, Viewed, Signed, Declined, Voided, Expired
        /// </summary>
        public string Status { get; set; } = "Pending";

        /// <summary>
        /// External envelope ID from DocuSign
        /// </summary>
        public string? ExternalEnvelopeId { get; set; }

        /// <summary>
        /// URL for signing the document
        /// </summary>
        public string? SigningUrl { get; set; }

        public string? SignedDocumentPath { get; set; }

        public DateTime? SentAt { get; set; }

        public DateTime? ViewedAt { get; set; }

        public DateTime? SignedAt { get; set; }

        public DateTime? ExpiresAt { get; set; }

        public string? DeclineReason { get; set; }

        public string? RequestedBy { get; set; }

        // KBA / Identity verification and audit trail
        public bool RequiresKba { get; set; } = false;
        public string? SignerIp { get; set; }
        public string? SignerUserAgent { get; set; }
        public string? SignerLocation { get; set; }

        // Verification & reminder tracking
        public string? VerificationMethod { get; set; }
        public string? VerificationStatus { get; set; }
        public DateTime? VerificationCompletedAt { get; set; }
        public string? VerificationNotes { get; set; }
        public int ReminderCount { get; set; } = 0;
        public DateTime? LastReminderAt { get; set; }
        public DateTime? ExpiredAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
