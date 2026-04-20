using JurisFlow.Server.Contracts;

namespace JurisFlow.Server.Services
{
    public sealed class LeadRequestValidator
    {
        private static readonly HashSet<string> AllowedLeadStatuses = new(StringComparer.OrdinalIgnoreCase)
        {
            "New",
            "Contacted",
            "Scheduled",
            "Consulted",
            "Proposal",
            "Retained",
            "Lost"
        };

        public ApplicationServiceResult<LeadWriteModel> ValidateForCreate(LeadCreateRequest request)
        {
            var name = NormalizeOptional(request.Name, 256) ?? "New Lead";
            var normalizedStatus = NormalizeStatus(request.Status);
            if (normalizedStatus == null)
            {
                return ApplicationServiceResult<LeadWriteModel>.Failure(StatusCodes.Status400BadRequest, "Invalid lead", "Lead status is invalid.");
            }

            return ApplicationServiceResult<LeadWriteModel>.Success(new LeadWriteModel
            {
                Name = name,
                Email = NormalizeOptional(request.Email, 320),
                NormalizedEmail = NormalizeEmail(request.Email),
                Phone = NormalizeOptional(request.Phone, 64),
                Source = NormalizeOptional(request.Source, 128) ?? "Referral",
                EstimatedValue = request.EstimatedValue,
                Status = normalizedStatus,
                PracticeArea = NormalizeOptional(request.PracticeArea, 128),
                Notes = NormalizeOptional(request.Notes, 4000)
            });
        }

        public ApplicationServiceResult<LeadUpdateModel> ValidateForUpdate(LeadUpdateRequest request)
        {
            var normalizedStatus = request.Status == null ? null : NormalizeStatus(request.Status);
            if (request.Status != null && normalizedStatus == null)
            {
                return ApplicationServiceResult<LeadUpdateModel>.Failure(StatusCodes.Status400BadRequest, "Invalid lead", "Lead status is invalid.");
            }

            return ApplicationServiceResult<LeadUpdateModel>.Success(new LeadUpdateModel
            {
                Name = NormalizeOptional(request.Name, 256),
                Email = NormalizeOptional(request.Email, 320),
                NormalizedEmail = request.Email == null ? null : NormalizeEmail(request.Email),
                Phone = NormalizeOptional(request.Phone, 64),
                Source = NormalizeOptional(request.Source, 128),
                EstimatedValue = request.EstimatedValue,
                Status = normalizedStatus,
                StatusChangeNote = NormalizeOptional(request.StatusChangeNote, 512),
                PracticeArea = NormalizeOptional(request.PracticeArea, 128),
                Notes = NormalizeOptional(request.Notes, 4000)
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

        private static string? NormalizeStatus(string? status)
        {
            var candidate = string.IsNullOrWhiteSpace(status) ? "New" : status.Trim();
            return AllowedLeadStatuses.FirstOrDefault(s => string.Equals(s, candidate, StringComparison.OrdinalIgnoreCase));
        }

        private static string? NormalizeEmail(string? email)
        {
            var normalized = EmailAddressNormalizer.Normalize(email);
            return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
        }
    }

    public sealed class LeadWriteModel
    {
        public string Name { get; init; } = string.Empty;
        public string? Email { get; init; }
        public string? NormalizedEmail { get; init; }
        public string? Phone { get; init; }
        public string Source { get; init; } = "Referral";
        public decimal EstimatedValue { get; init; }
        public string Status { get; init; } = "New";
        public string? PracticeArea { get; init; }
        public string? Notes { get; init; }
    }

    public sealed class LeadUpdateModel
    {
        public string? Name { get; init; }
        public string? Email { get; init; }
        public string? NormalizedEmail { get; init; }
        public string? Phone { get; init; }
        public string? Source { get; init; }
        public decimal? EstimatedValue { get; init; }
        public string? Status { get; init; }
        public string? StatusChangeNote { get; init; }
        public string? PracticeArea { get; init; }
        public string? Notes { get; init; }
    }
}
