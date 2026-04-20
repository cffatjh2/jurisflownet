using System;
using System.ComponentModel.DataAnnotations;

namespace JurisFlow.Server.Models
{
    public class ConflictCheck
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(120)]
        public string SearchQuery { get; set; } = string.Empty;

        [MaxLength(64)]
        public string? CheckType { get; set; } // NewClient, NewMatter, OpposingParty, Manual

        [MaxLength(64)]
        public string? EntityType { get; set; } // Client, Matter, OpposingParty

        [MaxLength(128)]
        public string? EntityId { get; set; } // ID of the entity being checked

        [MaxLength(64)]
        public string? CheckedBy { get; set; } // User ID who initiated the check

        [MaxLength(32)]
        public string Status { get; set; } = "Pending"; // Pending, Clear, Conflict, Waived

        public int MatchCount { get; set; } = 0;

        [MaxLength(64)]
        public string? WaivedBy { get; set; }

        public string? WaiverReason { get; set; }

        public DateTime? WaivedAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
