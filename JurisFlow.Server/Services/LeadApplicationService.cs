using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using JurisFlow.Server.Contracts;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;

namespace JurisFlow.Server.Services
{
    public sealed class LeadApplicationService
    {
        private readonly JurisFlowDbContext _context;
        private readonly LeadRequestValidator _validator;
        private readonly BusinessSurfaceAuthorizationService _authorization;
        private readonly RefactorWaveOptions _options;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public LeadApplicationService(
            JurisFlowDbContext context,
            LeadRequestValidator validator,
            BusinessSurfaceAuthorizationService authorization,
            IOptions<RefactorWaveOptions> options,
            IHttpContextAccessor httpContextAccessor)
        {
            _context = context;
            _validator = validator;
            _authorization = authorization;
            _options = options.Value;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task<IReadOnlyList<LeadResponse>> GetLeadsAsync()
        {
            var leads = await _context.Leads
                .AsNoTracking()
                .OrderByDescending(l => l.CreatedAt)
                .ToListAsync();

            return leads.Select(LeadResponse.FromModel).ToList();
        }

        public async Task<LeadResponse?> GetLeadAsync(string id)
        {
            var lead = await _context.Leads
                .AsNoTracking()
                .FirstOrDefaultAsync(l => l.Id == id);

            return lead == null ? null : LeadResponse.FromModel(lead);
        }

        public async Task<ApplicationServiceResult<LeadResponse>> CreateLeadAsync(LeadCreateRequest request)
        {
            var validation = _validator.ValidateForCreate(request);
            if (!validation.Succeeded || validation.Value == null)
            {
                return ApplicationServiceResult<LeadResponse>.Failure(validation.StatusCode, validation.Title!, validation.Detail!);
            }

            var lead = new Lead
            {
                Id = Guid.NewGuid().ToString(),
                Name = validation.Value.Name,
                Email = validation.Value.Email,
                Phone = validation.Value.Phone,
                Source = validation.Value.Source,
                EstimatedValue = validation.Value.EstimatedValue,
                Status = validation.Value.Status,
                PracticeArea = validation.Value.PracticeArea,
                Notes = validation.Value.Notes,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.Leads.Add(lead);
            await _context.SaveChangesAsync();

            return ApplicationServiceResult<LeadResponse>.Success(LeadResponse.FromModel(lead));
        }

        public async Task<ApplicationServiceResult<LeadResponse>> UpdateLeadAsync(string id, LeadUpdateRequest request)
        {
            var lead = await _context.Leads.FirstOrDefaultAsync(l => l.Id == id);
            if (lead == null)
            {
                return ApplicationServiceResult<LeadResponse>.Failure(StatusCodes.Status404NotFound, "Lead not found", "Lead was not found.");
            }

            var validation = _validator.ValidateForUpdate(request);
            if (!validation.Succeeded || validation.Value == null)
            {
                return ApplicationServiceResult<LeadResponse>.Failure(validation.StatusCode, validation.Title!, validation.Detail!);
            }

            if (validation.Value.Name != null) lead.Name = validation.Value.Name;
            if (validation.Value.Email != null) lead.Email = validation.Value.Email;
            if (validation.Value.Phone != null) lead.Phone = validation.Value.Phone;
            if (validation.Value.Source != null) lead.Source = validation.Value.Source;
            if (validation.Value.EstimatedValue.HasValue) lead.EstimatedValue = validation.Value.EstimatedValue.Value;
            if (validation.Value.Status != null) lead.Status = validation.Value.Status;
            if (validation.Value.PracticeArea != null) lead.PracticeArea = validation.Value.PracticeArea;
            if (validation.Value.Notes != null) lead.Notes = validation.Value.Notes;

            lead.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return ApplicationServiceResult<LeadResponse>.Success(LeadResponse.FromModel(lead));
        }

        public async Task<ApplicationServiceResult<object>> DeleteLeadAsync(string id)
        {
            if (_options.RestrictLeadDeleteToPrivilegedRoles && !_authorization.CanDeleteLead(CurrentUser))
            {
                return ApplicationServiceResult<object>.Failure(StatusCodes.Status403Forbidden, "Forbidden", "You do not have permission to delete leads.");
            }

            var lead = await _context.Leads.FirstOrDefaultAsync(l => l.Id == id);
            if (lead != null)
            {
                _context.Leads.Remove(lead);
                await _context.SaveChangesAsync();
            }

            return ApplicationServiceResult<object>.Success(new object());
        }

        private System.Security.Claims.ClaimsPrincipal CurrentUser => _httpContextAccessor.HttpContext?.User ?? new System.Security.Claims.ClaimsPrincipal();
    }
}
