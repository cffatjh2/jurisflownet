using System;
using System.ComponentModel.DataAnnotations;

namespace JurisFlow.Server.Models
{
    public class ConflictResult
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(64)]
        public string ConflictCheckId { get; set; } = string.Empty;

        [Required]
        [MaxLength(64)]
        public string MatchedEntityType { get; set; } = string.Empty; // Client, Matter, OpposingParty

        [Required]
        [MaxLength(64)]
        public string MatchedEntityId { get; set; } = string.Empty;

        [MaxLength(200)]
        public string MatchedEntityName { get; set; } = string.Empty;

        [MaxLength(64)]
        public string MatchType { get; set; } = "Exact"; // Exact, Fuzzy, Phonetic, Email, Phone

        public double MatchScore { get; set; } = 100.0; // 0-100 confidence score

        [MaxLength(32)]
        public string RiskLevel { get; set; } = "Medium"; // Low, Medium, High

        public string? Details { get; set; } // Additional match details

        [MaxLength(64)]
        public string? RelatedMatterId { get; set; } // If client match, show related matter

        [MaxLength(200)]
        public string? RelatedMatterName { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
