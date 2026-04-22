using System.ComponentModel.DataAnnotations;

namespace JurisFlow.Server.Models
{
    public class MessageAttachment
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(32)]
        public string MessageType { get; set; } = string.Empty;

        [Required]
        public string MessageId { get; set; } = string.Empty;

        [Required]
        [MaxLength(260)]
        public string StoredFileName { get; set; } = string.Empty;

        [Required]
        [MaxLength(260)]
        public string FileName { get; set; } = string.Empty;

        [Required]
        public string FilePath { get; set; } = string.Empty;

        [Required]
        [MaxLength(128)]
        public string MimeType { get; set; } = string.Empty;

        public long Size { get; set; }

        public string? ClientId { get; set; }
        public string? MessageEmployeeId { get; set; }
        public string? SenderUserId { get; set; }
        public string? SenderEmployeeId { get; set; }
        public string? RecipientEmployeeId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
