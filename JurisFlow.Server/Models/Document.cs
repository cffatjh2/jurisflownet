using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace JurisFlow.Server.Models
{
    public class Document
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        public string Name { get; set; }
        
        [Required]
        public string FileName { get; set; }

        [Required]
        public string FilePath { get; set; } // Local path or URL

        public long FileSize { get; set; }
        public string MimeType { get; set; } 
        public bool IsEncrypted { get; set; } = false;

        [MaxLength(64)]
        public string? EncryptionKeyId { get; set; }

        [MaxLength(64)]
        public string? EncryptionIv { get; set; }

        [MaxLength(64)]
        public string? EncryptionTag { get; set; }

        [MaxLength(32)]
        public string? EncryptionAlgorithm { get; set; }

        public string? MatterId { get; set; }

        [ForeignKey("MatterId")]
        [JsonIgnore]
        public Matter? Matter { get; set; }

        public string? UploadedBy { get; set; } // User ID
        public int Version { get; set; } = 1;
        public string? Category { get; set; }
        public string? Description { get; set; }
        public string? Tags { get; set; } // JSON string
        public string? Status { get; set; } = "Draft";
        public string? LegalHoldReason { get; set; }
        public DateTime? LegalHoldPlacedAt { get; set; }
        public string? LegalHoldPlacedBy { get; set; }
        public DateTime? LegalHoldReleasedAt { get; set; }
        public string? LegalHoldReleasedBy { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
