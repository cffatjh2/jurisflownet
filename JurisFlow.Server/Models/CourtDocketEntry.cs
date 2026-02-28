using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace JurisFlow.Server.Models
{
    public class CourtDocketEntry
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(128)]
        public string ProviderKey { get; set; } = string.Empty;

        [Required]
        [MaxLength(128)]
        public string ExternalDocketId { get; set; } = string.Empty;

        [MaxLength(128)]
        public string? ExternalCaseId { get; set; }

        [MaxLength(128)]
        public string? DocketNumber { get; set; }

        [MaxLength(256)]
        public string? CaseName { get; set; }

        [MaxLength(128)]
        public string? Court { get; set; }

        [MaxLength(2048)]
        public string? SourceUrl { get; set; }

        public DateTime? FiledAt { get; set; }

        public DateTime? ModifiedAt { get; set; }

        public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;

        public string? MatterId { get; set; }

        [ForeignKey("MatterId")]
        [JsonIgnore]
        public Matter? Matter { get; set; }

        public string? MetadataJson { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
