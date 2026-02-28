using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace JurisFlow.Server.Models
{
    public class ClientMessage
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        public string ClientId { get; set; } = string.Empty;

        public string? EmployeeId { get; set; }
        public string? MatterId { get; set; }

        [Required]
        public string Subject { get; set; } = string.Empty;

        [Required]
        public string Body { get; set; } = string.Empty;

        public string SenderType { get; set; } = "Client";
        public string? SenderUserId { get; set; }

        public string Status { get; set; } = "Unread";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ReadAt { get; set; }

        // JSON string of attachments
        public string? AttachmentsJson { get; set; }
    }

    public class MessageAttachment
    {
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string MimeType { get; set; } = string.Empty;
        public long Size { get; set; }
    }
}
