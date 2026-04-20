using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace JurisFlow.Server.Models
{
    public class LeadStatusHistory
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        public string LeadId { get; set; } = string.Empty;

        [ForeignKey(nameof(LeadId))]
        [JsonIgnore]
        public Lead? Lead { get; set; }

        [Required]
        public string PreviousStatus { get; set; } = string.Empty;

        [Required]
        public string NewStatus { get; set; } = string.Empty;

        public string? Notes { get; set; }

        public string? ChangedByUserId { get; set; }

        public string? ChangedByName { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
