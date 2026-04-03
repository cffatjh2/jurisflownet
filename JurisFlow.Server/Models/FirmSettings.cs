using System;
using System.ComponentModel.DataAnnotations;

namespace JurisFlow.Server.Models
{
    public class FirmSettings
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public string FirmName { get; set; } = "Your Law Firm";
        public string? TaxId { get; set; }
        public string? LedesFirmId { get; set; }
        public string? Address { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public string? ZipCode { get; set; }
        public string? Phone { get; set; }
        public string? Website { get; set; }
        public string? IntegrationsJson { get; set; }

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
