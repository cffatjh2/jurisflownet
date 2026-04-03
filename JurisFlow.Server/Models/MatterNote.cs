using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace JurisFlow.Server.Models
{
    public class MatterNote
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        public string MatterId { get; set; } = string.Empty;

        [ForeignKey(nameof(MatterId))]
        [JsonIgnore]
        public Matter? Matter { get; set; }

        [MaxLength(250)]
        public string? Title { get; set; }

        [Required]
        public string Body { get; set; } = string.Empty;

        public string? CreatedByUserId { get; set; }
        public string? UpdatedByUserId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
