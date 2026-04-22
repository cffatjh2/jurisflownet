using JurisFlow.Server.Contracts;
using JurisFlow.Server.Models;

namespace JurisFlow.Server.DTOs
{
    public class BootstrapResponse
    {
        public IReadOnlyList<Matter> Matters { get; set; } = Array.Empty<Matter>();
        public IReadOnlyList<TaskResponse> Tasks { get; set; } = Array.Empty<TaskResponse>();
        public IReadOnlyList<TimeEntryListItemDto> TimeEntries { get; set; } = Array.Empty<TimeEntryListItemDto>();
        public IReadOnlyList<ExpenseListItemDto> Expenses { get; set; } = Array.Empty<ExpenseListItemDto>();
        public IReadOnlyList<Client> Clients { get; set; } = Array.Empty<Client>();
        public IReadOnlyList<Lead> Leads { get; set; } = Array.Empty<Lead>();
        public IReadOnlyList<CalendarEvent> Events { get; set; } = Array.Empty<CalendarEvent>();
        public IReadOnlyList<InvoiceListItemDto> Invoices { get; set; } = Array.Empty<InvoiceListItemDto>();
        public IReadOnlyList<Notification> Notifications { get; set; } = Array.Empty<Notification>();
        public IReadOnlyList<BootstrapDocumentResponse> Documents { get; set; } = Array.Empty<BootstrapDocumentResponse>();
        public IReadOnlyList<TaskTemplateResponse> TaskTemplates { get; set; } = Array.Empty<TaskTemplateResponse>();
    }

    public sealed class BootstrapDocumentResponse
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string? MimeType { get; set; }
        public string? MatterId { get; set; }
        public int Version { get; set; }
        public string? Category { get; set; }
        public string? Description { get; set; }
        public string? Tags { get; set; }
        public string? Status { get; set; }
        public string? LegalHoldReason { get; set; }
        public DateTime? LegalHoldPlacedAt { get; set; }
        public DateTime? LegalHoldReleasedAt { get; set; }
        public string? LegalHoldPlacedBy { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public static BootstrapDocumentResponse FromModel(Document document)
        {
            return new BootstrapDocumentResponse
            {
                Id = document.Id,
                Name = document.Name,
                FileSize = document.FileSize,
                MimeType = document.MimeType,
                MatterId = document.MatterId,
                Version = document.Version,
                Category = document.Category,
                Description = document.Description,
                Tags = document.Tags,
                Status = document.Status,
                LegalHoldReason = document.LegalHoldReason,
                LegalHoldPlacedAt = document.LegalHoldPlacedAt,
                LegalHoldReleasedAt = document.LegalHoldReleasedAt,
                LegalHoldPlacedBy = document.LegalHoldPlacedBy,
                CreatedAt = document.CreatedAt,
                UpdatedAt = document.UpdatedAt
            };
        }
    }
}
