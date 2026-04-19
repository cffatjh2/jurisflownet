using System.ComponentModel.DataAnnotations;
using JurisFlow.Server.Models;

namespace JurisFlow.Server.Contracts
{
    public sealed class ClientCreateRequest : RejectUnknownFieldsRequestBase
    {
        public string? ClientNumber { get; set; }

        [Required]
        public string Name { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        public string? Phone { get; set; }
        public string? Mobile { get; set; }
        public string? Company { get; set; }

        [Required]
        public string Type { get; set; } = "Individual";

        [Required]
        public string Status { get; set; } = "Active";

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
        public string? Password { get; set; }
    }

    public sealed class ClientReplaceRequest : RejectUnknownFieldsRequestBase
    {
        public string? ClientNumber { get; set; }

        [Required]
        public string Name { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        public string? Phone { get; set; }
        public string? Mobile { get; set; }
        public string? Company { get; set; }

        [Required]
        public string Type { get; set; } = "Individual";

        [Required]
        public string Status { get; set; } = "Active";

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
        public string? Password { get; set; }
    }

    public sealed class ClientPatchRequest : RejectUnknownFieldsRequestBase
    {
        public string? ClientNumber { get; set; }

        [EmailAddress]
        public string? Email { get; set; }

        public string? Name { get; set; }
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

    public sealed class ClientSetPortalPasswordRequest : RejectUnknownFieldsRequestBase
    {
        [Required]
        public string Password { get; set; } = string.Empty;
    }

    public sealed class ClientListItemResponse
    {
        public string Id { get; set; } = string.Empty;
        public string? ClientNumber { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? Phone { get; set; }
        public string? Mobile { get; set; }
        public string? Company { get; set; }
        public string Type { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public bool PortalEnabled { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        public static ClientListItemResponse FromModel(Client client)
        {
            return new ClientListItemResponse
            {
                Id = client.Id,
                ClientNumber = client.ClientNumber,
                Name = client.Name,
                Email = client.Email,
                Phone = client.Phone,
                Mobile = client.Mobile,
                Company = client.Company,
                Type = client.Type,
                Status = client.Status,
                PortalEnabled = client.PortalEnabled,
                CreatedAt = client.CreatedAt,
                UpdatedAt = client.UpdatedAt
            };
        }
    }

    public sealed class ClientDetailResponse
    {
        public string Id { get; set; } = string.Empty;
        public string? ClientNumber { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? Phone { get; set; }
        public string? Mobile { get; set; }
        public string? Company { get; set; }
        public string Type { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? Address { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public string? ZipCode { get; set; }
        public string? Country { get; set; }
        public string? IncorporationState { get; set; }
        public bool PortalEnabled { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }

        public static ClientDetailResponse FromModel(Client client)
        {
            return new ClientDetailResponse
            {
                Id = client.Id,
                ClientNumber = client.ClientNumber,
                Name = client.Name,
                Email = client.Email,
                Phone = client.Phone,
                Mobile = client.Mobile,
                Company = client.Company,
                Type = client.Type,
                Status = client.Status,
                Address = client.Address,
                City = client.City,
                State = client.State,
                ZipCode = client.ZipCode,
                Country = client.Country,
                IncorporationState = client.IncorporationState,
                PortalEnabled = client.PortalEnabled,
                CreatedAt = client.CreatedAt,
                UpdatedAt = client.UpdatedAt
            };
        }
    }

    public sealed class ClientStatusHistoryResponse
    {
        public string Id { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public string? PreviousStatus { get; set; }
        public string? NewStatus { get; set; }
        public string? Notes { get; set; }
        public string? ChangedByUserId { get; set; }
        public string? ChangedByName { get; set; }
        public DateTime CreatedAt { get; set; }

        public static ClientStatusHistoryResponse FromModel(ClientStatusHistory history)
        {
            return new ClientStatusHistoryResponse
            {
                Id = history.Id,
                ClientId = history.ClientId,
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
