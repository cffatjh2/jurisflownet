using System.ComponentModel.DataAnnotations;

namespace JurisFlow.Server.Models
{
    public class TaskTemplate
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(128)]
        public string? Category { get; set; }

        [MaxLength(1000)]
        public string? Description { get; set; }

        [Required]
        public string Definition { get; set; } = "[]";

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
