using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using JurisFlow.Server.Models;

namespace JurisFlow.Server.Contracts
{
    public abstract class MatterWriteRequestBase : IValidatableObject
    {
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (AdditionalProperties is { Count: > 0 })
            {
                var keys = string.Join(", ", AdditionalProperties.Keys.OrderBy(static key => key, StringComparer.Ordinal));
                yield return new ValidationResult(
                    $"Unsupported fields were supplied: {keys}.",
                    new[] { nameof(AdditionalProperties) });
            }
        }
    }

    public sealed class CreateMatterRequest : MatterWriteRequestBase
    {
        [Required]
        public string CaseNumber { get; set; } = string.Empty;

        [Required]
        public string Name { get; set; } = string.Empty;

        public string? PracticeArea { get; set; }
        public string? CourtType { get; set; }
        public string? Outcome { get; set; }

        [Required]
        public string Status { get; set; } = string.Empty;

        [Required]
        public string FeeStructure { get; set; } = string.Empty;

        [Required]
        public string ResponsibleAttorney { get; set; } = string.Empty;

        public double BillableRate { get; set; }

        public string? EntityId { get; set; }
        public string? OfficeId { get; set; }

        [Required]
        public string ClientId { get; set; } = string.Empty;

        public List<string>? RelatedClientIds { get; set; }
    }

    public sealed class UpdateMatterRequest : MatterWriteRequestBase
    {
        [Required]
        public string CaseNumber { get; set; } = string.Empty;

        [Required]
        public string Name { get; set; } = string.Empty;

        public string? PracticeArea { get; set; }
        public string? CourtType { get; set; }
        public string? Outcome { get; set; }

        [Required]
        public string Status { get; set; } = string.Empty;

        [Required]
        public string FeeStructure { get; set; } = string.Empty;

        [Required]
        public string ResponsibleAttorney { get; set; } = string.Empty;

        public double BillableRate { get; set; }

        public string? EntityId { get; set; }
        public string? OfficeId { get; set; }

        [Required]
        public string ClientId { get; set; } = string.Empty;

        public List<string>? RelatedClientIds { get; set; }
    }

    public sealed class MatterResponse
    {
        public string Id { get; set; } = string.Empty;
        public string CaseNumber { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? PracticeArea { get; set; }
        public string? CourtType { get; set; }
        public string? Outcome { get; set; }
        public string? CreatedByUserId { get; set; }
        public bool ShareWithFirm { get; set; }
        public bool ShareBillingWithFirm { get; set; }
        public bool ShareNotesWithFirm { get; set; }
        public string Status { get; set; } = string.Empty;
        public string FeeStructure { get; set; } = string.Empty;
        public DateTime OpenDate { get; set; }
        public string ResponsibleAttorney { get; set; } = string.Empty;
        public double BillableRate { get; set; }
        public double TrustBalance { get; set; }
        public string? CurrentOutcomeFeePlanId { get; set; }
        public string? EntityId { get; set; }
        public string? OfficeId { get; set; }
        public string ClientId { get; set; } = string.Empty;
        public List<string> RelatedClientIds { get; set; } = new();
        public List<MatterRelatedClientResponse> RelatedClients { get; set; } = new();
        public DateTime? ConflictCheckDate { get; set; }
        public bool ConflictCheckCleared { get; set; }
        public bool ConflictWaiverObtained { get; set; }

        public static MatterResponse FromModel(Matter matter)
        {
            return new MatterResponse
            {
                Id = matter.Id,
                CaseNumber = matter.CaseNumber,
                Name = matter.Name,
                PracticeArea = matter.PracticeArea,
                CourtType = matter.CourtType,
                Outcome = matter.Outcome,
                CreatedByUserId = matter.CreatedByUserId,
                ShareWithFirm = matter.ShareWithFirm,
                ShareBillingWithFirm = matter.ShareBillingWithFirm,
                ShareNotesWithFirm = matter.ShareNotesWithFirm,
                Status = matter.Status,
                FeeStructure = matter.FeeStructure,
                OpenDate = matter.OpenDate,
                ResponsibleAttorney = matter.ResponsibleAttorney,
                BillableRate = matter.BillableRate,
                TrustBalance = matter.TrustBalance,
                CurrentOutcomeFeePlanId = matter.CurrentOutcomeFeePlanId,
                EntityId = matter.EntityId,
                OfficeId = matter.OfficeId,
                ClientId = matter.ClientId,
                RelatedClientIds = matter.RelatedClientIds?.Where(static id => !string.IsNullOrWhiteSpace(id)).ToList() ?? new List<string>(),
                RelatedClients = matter.RelatedClients?.Select(MatterRelatedClientResponse.FromModel).ToList() ?? new List<MatterRelatedClientResponse>(),
                ConflictCheckDate = matter.ConflictCheckDate,
                ConflictCheckCleared = matter.ConflictCheckCleared,
                ConflictWaiverObtained = matter.ConflictWaiverObtained
            };
        }
    }

    public sealed class MatterRelatedClientResponse
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Email { get; set; }

        public static MatterRelatedClientResponse FromModel(Client client)
        {
            return new MatterRelatedClientResponse
            {
                Id = client.Id,
                Name = client.Name,
                Email = client.Email
            };
        }
    }
}
