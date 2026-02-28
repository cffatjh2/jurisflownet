using System;
using System.ComponentModel.DataAnnotations;

namespace JurisFlow.Server.Models
{
    public class SignatureAuditEntry
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        public string SignatureRequestId { get; set; } = string.Empty;

        [Required]
        public string Action { get; set; } = string.Empty;

        public string? ActorType { get; set; }
        public string? ActorId { get; set; }
        public string? ActorEmail { get; set; }
        public string? IpAddress { get; set; }
        public string? UserAgent { get; set; }
        public string? MetadataJson { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
