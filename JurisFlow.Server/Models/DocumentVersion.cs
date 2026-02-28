using System;
using System.ComponentModel.DataAnnotations;

namespace JurisFlow.Server.Models
{
    public class DocumentVersion
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        public string DocumentId { get; set; } = string.Empty;

        [Required]
        [MaxLength(255)]
        public string FileName { get; set; } = string.Empty;

        [Required]
        [MaxLength(500)]
        public string FilePath { get; set; } = string.Empty;

        public long FileSize { get; set; }

        public bool IsEncrypted { get; set; } = false;

        [MaxLength(64)]
        public string? EncryptionKeyId { get; set; }

        [MaxLength(64)]
        public string? EncryptionIv { get; set; }

        [MaxLength(64)]
        public string? EncryptionTag { get; set; }

        [MaxLength(32)]
        public string? EncryptionAlgorithm { get; set; }

        [MaxLength(128)]
        public string? Sha256 { get; set; }

        public string? UploadedByUserId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
