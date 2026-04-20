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
        public string? StatusChangeNote { get; set; }
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
        public string CreatedBySource { get; set; } = string.Empty;
        public decimal EstimatedValue { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? PracticeArea { get; set; }
        public string? Notes { get; set; }
        public bool IsArchived { get; set; }
        public DateTime? ArchivedAt { get; set; }
        public string? ArchivedByName { get; set; }
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
                CreatedBySource = lead.CreatedBySource,
                EstimatedValue = lead.EstimatedValue,
                Status = lead.Status,
                PracticeArea = lead.PracticeArea,
                Notes = lead.Notes,
                IsArchived = lead.IsArchived,
                ArchivedAt = lead.ArchivedAt,
                ArchivedByName = lead.ArchivedByName,
                CreatedAt = lead.CreatedAt,
                UpdatedAt = lead.UpdatedAt
            };
        }
    }

    public sealed class LeadListItemResponse
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Source { get; set; }
        public string CreatedBySource { get; set; } = string.Empty;
        public decimal EstimatedValue { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? PracticeArea { get; set; }
        public bool IsArchived { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public static LeadListItemResponse FromModel(Lead lead)
        {
            return new LeadListItemResponse
            {
                Id = lead.Id,
                Name = lead.Name,
                Email = lead.Email,
                Phone = lead.Phone,
                Source = lead.Source,
                CreatedBySource = lead.CreatedBySource,
                EstimatedValue = lead.EstimatedValue,
                Status = lead.Status,
                PracticeArea = lead.PracticeArea,
                IsArchived = lead.IsArchived,
                CreatedAt = lead.CreatedAt,
                UpdatedAt = lead.UpdatedAt
            };
        }
    }

    public sealed class LeadReadModelCollectionResponse
    {
        public IReadOnlyList<LeadListItemResponse> Items { get; set; } = Array.Empty<LeadListItemResponse>();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public bool HasMore { get; set; }
    }

    public sealed class LeadStatusHistoryResponse
    {
        public string Id { get; set; } = string.Empty;
        public string LeadId { get; set; } = string.Empty;
        public string PreviousStatus { get; set; } = string.Empty;
        public string NewStatus { get; set; } = string.Empty;
        public string? Notes { get; set; }
        public string? ChangedByUserId { get; set; }
        public string? ChangedByName { get; set; }
        public DateTime CreatedAt { get; set; }

        public static LeadStatusHistoryResponse FromModel(LeadStatusHistory history)
        {
            return new LeadStatusHistoryResponse
            {
                Id = history.Id,
                LeadId = history.LeadId,
                PreviousStatus = history.PreviousStatus,
                NewStatus = history.NewStatus,
                Notes = history.Notes,
                ChangedByUserId = history.ChangedByUserId,
                ChangedByName = history.ChangedByName,
                CreatedAt = history.CreatedAt
            };
        }
    }
}
