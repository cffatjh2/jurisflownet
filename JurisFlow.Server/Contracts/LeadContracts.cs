using System.ComponentModel.DataAnnotations;
using JurisFlow.Server.Models;

namespace JurisFlow.Server.Contracts
{
    public sealed class LeadCreateRequest : RejectUnknownFieldsRequestBase
    {
        public string? Name { get; set; }
        [EmailAddress]
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Source { get; set; }
        public decimal EstimatedValue { get; set; }
        public string? Status { get; set; }
        public string? PracticeArea { get; set; }
        public string? Notes { get; set; }
    }

    public sealed class LeadUpdateRequest : RejectUnknownFieldsRequestBase
    {
        public string? Name { get; set; }
        [EmailAddress]
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Source { get; set; }
        public decimal? EstimatedValue { get; set; }
        public string? Status { get; set; }
        public string? PracticeArea { get; set; }
        public string? Notes { get; set; }
    }

    public sealed class LeadResponse
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Source { get; set; }
        public decimal EstimatedValue { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? PracticeArea { get; set; }
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public static LeadResponse FromModel(Lead lead)
        {
            return new LeadResponse
            {
                Id = lead.Id,
                Name = lead.Name,
                Email = lead.Email,
                Phone = lead.Phone,
                Source = lead.Source,
                EstimatedValue = lead.EstimatedValue,
                Status = lead.Status,
                PracticeArea = lead.PracticeArea,
                Notes = lead.Notes,
                CreatedAt = lead.CreatedAt,
                UpdatedAt = lead.UpdatedAt
            };
        }
    }
}
