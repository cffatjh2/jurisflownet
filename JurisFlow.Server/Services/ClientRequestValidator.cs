using JurisFlow.Server.Contracts;

namespace JurisFlow.Server.Services
{
    public sealed class ClientRequestValidator
    {
        private static readonly HashSet<string> AllowedClientStatuses = new(StringComparer.OrdinalIgnoreCase)
        {
            "Active",
            "Inactive"
        };

        private static readonly HashSet<string> AllowedClientTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "Individual",
            "Corporate"
        };

        public ApplicationServiceResult<ClientWriteModel> ValidateForCreateOrReplace(ClientCreateRequest request)
        {
            return ValidateCore(
                request.ClientNumber,
                request.Name,
                request.Email,
                request.Phone,
                request.Mobile,
                request.Company,
                request.Type,
                request.Status,
                request.Address,
                request.City,
                request.State,
                request.ZipCode,
                request.Country,
                request.TaxId,
                request.IncorporationState,
                request.RegisteredAgent,
                request.AuthorizedRepresentatives,
                request.Notes,
                portalEnabled: null,
                statusChangeNote: null);
        }

        public ApplicationServiceResult<ClientWriteModel> ValidateForCreateOrReplace(ClientReplaceRequest request)
        {
            return ValidateCore(
                request.ClientNumber,
                request.Name,
                request.Email,
                request.Phone,
                request.Mobile,
                request.Company,
                request.Type,
                request.Status,
                request.Address,
                request.City,
                request.State,
                request.ZipCode,
                request.Country,
                request.TaxId,
                request.IncorporationState,
                request.RegisteredAgent,
                request.AuthorizedRepresentatives,
                request.Notes,
                portalEnabled: null,
                statusChangeNote: null);
        }

        public ApplicationServiceResult<ClientPatchModel> ValidateForPatch(ClientPatchRequest request)
        {
            string? normalizedType = null;
            string? normalizedStatus = null;

            if (!string.IsNullOrWhiteSpace(request.Type) && !TryNormalizeClientType(request.Type, out normalizedType))
            {
                return ApplicationServiceResult<ClientPatchModel>.Failure(StatusCodes.Status400BadRequest, "Invalid client update", "Invalid client type.");
            }

            if (!string.IsNullOrWhiteSpace(request.Status) && !TryNormalizeClientStatus(request.Status, out normalizedStatus))
            {
                return ApplicationServiceResult<ClientPatchModel>.Failure(StatusCodes.Status400BadRequest, "Invalid client update", "Invalid client status.");
            }

            var normalizedEmail = string.IsNullOrWhiteSpace(request.Email)
                ? null
                : EmailAddressNormalizer.Normalize(request.Email);

            if (request.Email != null && string.IsNullOrWhiteSpace(normalizedEmail))
            {
                return ApplicationServiceResult<ClientPatchModel>.Failure(StatusCodes.Status400BadRequest, "Invalid client update", "Email is required.");
            }

            return ApplicationServiceResult<ClientPatchModel>.Success(new ClientPatchModel
            {
                ClientNumber = NormalizeOptional(request.ClientNumber, 64),
                Name = NormalizeOptional(request.Name, 256) ?? string.Empty,
                Email = NormalizeOptional(request.Email, 320) ?? string.Empty,
                NormalizedEmail = normalizedEmail ?? string.Empty,
                Phone = NormalizeOptional(request.Phone, 64),
                Mobile = NormalizeOptional(request.Mobile, 64),
                Company = NormalizeOptional(request.Company, 256),
                Type = normalizedType ?? string.Empty,
                Status = normalizedStatus ?? string.Empty,
                Address = NormalizeOptional(request.Address, 512),
                City = NormalizeOptional(request.City, 128),
                State = NormalizeOptional(request.State, 128),
                ZipCode = NormalizeOptional(request.ZipCode, 32),
                Country = NormalizeOptional(request.Country, 128),
                TaxId = NormalizeOptional(request.TaxId, 64),
                IncorporationState = NormalizeOptional(request.IncorporationState, 128),
                RegisteredAgent = NormalizeOptional(request.RegisteredAgent, 256),
                AuthorizedRepresentatives = NormalizeOptional(request.AuthorizedRepresentatives, 2000),
                Notes = NormalizeOptional(request.Notes, 4000),
                PortalEnabled = request.PortalEnabled,
                StatusChangeNote = NormalizeOptional(request.StatusChangeNote, 512)
            });
        }

        public ApplicationServiceResult<string> ValidatePortalPassword(ClientSetPortalPasswordRequest request)
        {
            var password = string.IsNullOrWhiteSpace(request.Password) ? null : request.Password.Trim();
            if (string.IsNullOrWhiteSpace(password))
            {
                return ApplicationServiceResult<string>.Failure(StatusCodes.Status400BadRequest, "Invalid password", "Password is required.");
            }

            return ApplicationServiceResult<string>.Success(password);
        }

        private static ApplicationServiceResult<ClientWriteModel> ValidateCore(
            string? clientNumber,
            string? name,
            string? email,
            string? phone,
            string? mobile,
            string? company,
            string? type,
            string? status,
            string? address,
            string? city,
            string? state,
            string? zipCode,
            string? country,
            string? taxId,
            string? incorporationState,
            string? registeredAgent,
            string? authorizedRepresentatives,
            string? notes,
            bool? portalEnabled,
            string? statusChangeNote)
        {
            var normalizedName = NormalizeOptional(name, 256);
            if (string.IsNullOrWhiteSpace(normalizedName))
            {
                return ApplicationServiceResult<ClientWriteModel>.Failure(StatusCodes.Status400BadRequest, "Invalid client", "Name is required.");
            }

            var normalizedEmail = EmailAddressNormalizer.Normalize(email);
            if (string.IsNullOrWhiteSpace(normalizedEmail) || string.IsNullOrWhiteSpace(email))
            {
                return ApplicationServiceResult<ClientWriteModel>.Failure(StatusCodes.Status400BadRequest, "Invalid client", "Email is required.");
            }

            if (!TryNormalizeClientType(type, out var normalizedType))
            {
                return ApplicationServiceResult<ClientWriteModel>.Failure(StatusCodes.Status400BadRequest, "Invalid client", "Invalid client type.");
            }

            if (!TryNormalizeClientStatus(status, out var normalizedStatus))
            {
                return ApplicationServiceResult<ClientWriteModel>.Failure(StatusCodes.Status400BadRequest, "Invalid client", "Invalid client status.");
            }

            return ApplicationServiceResult<ClientWriteModel>.Success(new ClientWriteModel
            {
                ClientNumber = NormalizeOptional(clientNumber, 64),
                Name = normalizedName,
                Email = email!.Trim(),
                NormalizedEmail = normalizedEmail,
                Phone = NormalizeOptional(phone, 64),
                Mobile = NormalizeOptional(mobile, 64),
                Company = NormalizeOptional(company, 256),
                Type = normalizedType!,
                Status = normalizedStatus!,
                Address = NormalizeOptional(address, 512),
                City = NormalizeOptional(city, 128),
                State = NormalizeOptional(state, 128),
                ZipCode = NormalizeOptional(zipCode, 32),
                Country = NormalizeOptional(country, 128),
                TaxId = NormalizeOptional(taxId, 64),
                IncorporationState = NormalizeOptional(incorporationState, 128),
                RegisteredAgent = NormalizeOptional(registeredAgent, 256),
                AuthorizedRepresentatives = NormalizeOptional(authorizedRepresentatives, 2000),
                Notes = NormalizeOptional(notes, 4000),
                PortalEnabled = portalEnabled,
                StatusChangeNote = NormalizeOptional(statusChangeNote, 512)
            });
        }

        private static string? NormalizeOptional(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var trimmed = value.Trim();
            return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
        }

        private static bool TryNormalizeClientStatus(string? status, out string? normalizedStatus)
        {
            var candidate = string.IsNullOrWhiteSpace(status) ? null : status.Trim();
            normalizedStatus = candidate == null
                ? null
                : AllowedClientStatuses.FirstOrDefault(s => string.Equals(s, candidate, StringComparison.OrdinalIgnoreCase));
            return normalizedStatus != null;
        }

        private static bool TryNormalizeClientType(string? type, out string? normalizedType)
        {
            var candidate = string.IsNullOrWhiteSpace(type) ? null : type.Trim();
            normalizedType = candidate == null
                ? null
                : AllowedClientTypes.FirstOrDefault(t => string.Equals(t, candidate, StringComparison.OrdinalIgnoreCase));
            return normalizedType != null;
        }
    }

    public class ClientWriteModel
    {
        public string? ClientNumber { get; init; }
        public string Name { get; init; } = string.Empty;
        public string Email { get; init; } = string.Empty;
        public string NormalizedEmail { get; init; } = string.Empty;
        public string? Phone { get; init; }
        public string? Mobile { get; init; }
        public string? Company { get; init; }
        public string Type { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public string? Address { get; init; }
        public string? City { get; init; }
        public string? State { get; init; }
        public string? ZipCode { get; init; }
        public string? Country { get; init; }
        public string? TaxId { get; init; }
        public string? IncorporationState { get; init; }
        public string? RegisteredAgent { get; init; }
        public string? AuthorizedRepresentatives { get; init; }
        public string? Notes { get; init; }
        public bool? PortalEnabled { get; init; }
        public string? StatusChangeNote { get; init; }
    }

    public sealed class ClientPatchModel : ClientWriteModel
    {
    }
}
