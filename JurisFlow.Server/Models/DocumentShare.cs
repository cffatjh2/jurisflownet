using System;
using System.ComponentModel.DataAnnotations;

namespace JurisFlow.Server.Models
{
    public class DocumentShare
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        public string DocumentId { get; set; } = string.Empty;

        [Required]
        public string ClientId { get; set; } = string.Empty;

        public string? SharedByUserId { get; set; }
        public DateTime SharedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public bool CanView { get; set; } = true;
        public bool CanDownload { get; set; } = true;
        public bool CanComment { get; set; } = true;
        public bool CanUpload { get; set; } = false;

        public DateTime? ExpiresAt { get; set; }
        public string? Note { get; set; }
    }

    public class DocumentComment
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        public string DocumentId { get; set; } = string.Empty;

        [Required]
        public string Body { get; set; } = string.Empty;

        public string? AuthorUserId { get; set; }
        public string? AuthorClientId { get; set; }
        public string AuthorType { get; set; } = "Staff";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
