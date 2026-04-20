using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace JurisFlow.Server.Models
{
    public class Lead
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        
        [Required]
        public string Name { get; set; } = string.Empty;
        
        [EmailAddress]
        public string? Email { get; set; }

        [MaxLength(320)]
        [JsonIgnore]
        public string? NormalizedEmail { get; set; }
        
        public string? Phone { get; set; }
        
        public string? Source { get; set; } = "Referral";

        [Required]
        [MaxLength(64)]
        public string CreatedBySource { get; set; } = "Manual";
        
        [Column(TypeName = "decimal(18,2)")]
        public decimal EstimatedValue { get; set; }
        
        public string Status { get; set; } = "New"; // New, Contacted, Scheduled, Consulted, Proposal, Retained, Lost
        
        public string? PracticeArea { get; set; }
        
        public string? Notes { get; set; }

        public bool IsArchived { get; set; }

        public DateTime? ArchivedAt { get; set; }

        public string? ArchivedByUserId { get; set; }

        public string? ArchivedByName { get; set; }
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        [JsonIgnore]
        public ICollection<LeadStatusHistory> StatusHistory { get; set; } = new List<LeadStatusHistory>();
    }
}
