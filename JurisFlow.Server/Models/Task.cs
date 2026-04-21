using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace JurisFlow.Server.Models
{
    public class Task
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        public string Title { get; set; }
        public string? Description { get; set; }
        public DateTime? DueDate { get; set; }
        public DateTime? ReminderAt { get; set; }

        [Required]
        public string Priority { get; set; } // High | Medium | Low

        [Required]
        public string Status { get; set; } // To Do | In Progress | ...

        public string? Outcome { get; set; } // Success | Failed

        // Relations
        [MaxLength(128)]
        public string? CreatedByUserId { get; set; }

        public string? MatterId { get; set; }

        [ForeignKey("MatterId")]
        [JsonIgnore]
        public Matter? Matter { get; set; }

        public string? AssignedEmployeeId { get; set; }

        [ForeignKey("AssignedEmployeeId")]
        [JsonIgnore]
        public Employee? AssignedEmployee { get; set; }

        [Required]
        [MaxLength(32)]
        [ConcurrencyCheck]
        public string RowVersion { get; set; } = Guid.NewGuid().ToString("N");

        public bool ReminderSent { get; set; } = false;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
