using System.ComponentModel.DataAnnotations;

namespace JurisFlow.Server.DTOs
{
    public class ClientUpdateDto
    {
        public string? ClientNumber { get; set; }

        public string? Name { get; set; }

        [EmailAddress]
        public string? Email { get; set; }

        public string? Phone { get; set; }
        public string? Mobile { get; set; }
        public string? Company { get; set; }
        public string? Type { get; set; }
        public string? Status { get; set; }

        public string? Address { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public string? ZipCode { get; set; }
        public string? Country { get; set; }
        public string? TaxId { get; set; }

        public string? IncorporationState { get; set; }
        public string? RegisteredAgent { get; set; }
        public string? AuthorizedRepresentatives { get; set; }

        public string? Notes { get; set; }
        public bool? PortalEnabled { get; set; }
        public string? StatusChangeNote { get; set; }
        public string? Password { get; set; }
    }
}
