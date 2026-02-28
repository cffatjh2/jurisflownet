using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace JurisFlow.Server.Models
{
    public class Client
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public string? ClientNumber { get; set; }

        [Required]
        public string Name { get; set; }

        [Required]
        [EmailAddress]
        public string Email { get; set; }

        [Required]
        [MaxLength(320)]
        [JsonIgnore]
        public string NormalizedEmail { get; set; } = string.Empty;

        public string? Phone { get; set; }
        public string? Mobile { get; set; }
        public string? Company { get; set; }
        
        [Required]
        public string Type { get; set; } // Individual | Corporate
        
        [Required]
        public string Status { get; set; } // Active | Inactive

        public string? Address { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public string? ZipCode { get; set; }
        public string? Country { get; set; }
        public string? TaxId { get; set; }
        
        // Corporate-specific fields (US Legal)
        public string? IncorporationState { get; set; }  // State where incorporated
        public string? RegisteredAgent { get; set; }      // Registered agent name
        public string? AuthorizedRepresentatives { get; set; }  // JSON array of authorized persons
        
        public string? Notes { get; set; }

        // Portal Access
        [JsonIgnore]
        public string? PasswordHash { get; set; }
        public bool PortalEnabled { get; set; } = false;
        public DateTime? LastLogin { get; set; }

        public DateTime? CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation Properties
        [JsonIgnore]
        public ICollection<Matter> Matters { get; set; } = new List<Matter>();

        [JsonIgnore]
        public ICollection<ClientStatusHistory> StatusHistory { get; set; } = new List<ClientStatusHistory>();
        
        // Add other collections as Models are created: Invoices, etc.
    }
}
