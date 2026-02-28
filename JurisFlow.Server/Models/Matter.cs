using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace JurisFlow.Server.Models
{
    public class Matter
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        public string CaseNumber { get; set; }

        [Required]
        public string Name { get; set; }

        public string PracticeArea { get; set; }
        public string? CourtType { get; set; }

        public string? Outcome { get; set; }
        
        [Required]
        public string Status { get; set; } // Open | Closed

        [Required]
        public string FeeStructure { get; set; } // Hourly | Fixed

        public DateTime OpenDate { get; set; } = DateTime.UtcNow;

        [Required]
        public string ResponsibleAttorney { get; set; }

        public double BillableRate { get; set; }
        public double TrustBalance { get; set; } = 0;

        public string? CurrentOutcomeFeePlanId { get; set; }

        public string? EntityId { get; set; }

        [ForeignKey("EntityId")]
        [JsonIgnore]
        public FirmEntity? Entity { get; set; }

        public string? OfficeId { get; set; }

        [ForeignKey("OfficeId")]
        [JsonIgnore]
        public Office? Office { get; set; }

        // Foreign Key
        [Required]
        public string ClientId { get; set; }

        [ForeignKey("ClientId")]
        [JsonIgnore] // Prevent cycles
        public Client? Client { get; set; }

        // Navigation Properties
        // public ICollection<Task> Tasks { get; set; }
        // public ICollection<Document> Documents { get; set; }
        
        // Opposing Parties (for conflict check)
        public ICollection<OpposingParty> OpposingParties { get; set; } = new List<OpposingParty>();
        
        // Conflict Check Tracking
        public DateTime? ConflictCheckDate { get; set; }
        public bool ConflictCheckCleared { get; set; } = false;
        public bool ConflictWaiverObtained { get; set; } = false;
    }
}
