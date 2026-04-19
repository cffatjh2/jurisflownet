using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using JurisFlow.Server.Data;

namespace JurisFlow.Server.Services
{
    public sealed class LegacyTaskAssignmentAdapter
    {
        private readonly JurisFlowDbContext _context;
        private readonly TenantContext _tenantContext;
        private readonly RefactorWaveOptions _options;

        public LegacyTaskAssignmentAdapter(
            JurisFlowDbContext context,
            TenantContext tenantContext,
            IOptions<RefactorWaveOptions> options)
        {
            _context = context;
            _tenantContext = tenantContext;
            _options = options.Value;
        }

        public async Task<ApplicationServiceResult<string?>> ResolveAssignedEmployeeIdAsync(string? assignedEmployeeId, string? assignedTo)
        {
            var tenantId = RequireTenantId();
            var normalizedId = NormalizeOptional(assignedEmployeeId);
            if (!string.IsNullOrWhiteSpace(normalizedId))
            {
                var exists = await _context.Employees
                    .AsNoTracking()
                    .AnyAsync(e => EF.Property<string>(e, "TenantId") == tenantId && e.Id == normalizedId);

                return exists
                    ? ApplicationServiceResult<string?>.Success(normalizedId)
                    : ApplicationServiceResult<string?>.Failure(StatusCodes.Status400BadRequest, "Invalid assignment", "Assigned employee was not found.");
            }

            if (!_options.UseLegacyTaskAssignmentAdapter)
            {
                return ApplicationServiceResult<string?>.Success(null);
            }

            var normalizedAssignedTo = NormalizeOptional(assignedTo);
            if (string.IsNullOrWhiteSpace(normalizedAssignedTo))
            {
                return ApplicationServiceResult<string?>.Success(null);
            }

            var employeeId = await _context.Employees
                .AsNoTracking()
                .Where(e => EF.Property<string>(e, "TenantId") == tenantId)
                .Where(e =>
                    e.Email == normalizedAssignedTo ||
                    ((e.FirstName + " " + e.LastName) == normalizedAssignedTo))
                .Select(e => e.Id)
                .FirstOrDefaultAsync();

            if (string.IsNullOrWhiteSpace(employeeId))
            {
                return ApplicationServiceResult<string?>.Failure(StatusCodes.Status400BadRequest, "Invalid assignment", "Assigned employee could not be resolved from the legacy assignment value.");
            }

            return ApplicationServiceResult<string?>.Success(employeeId);
        }

        private string RequireTenantId()
        {
            if (string.IsNullOrWhiteSpace(_tenantContext.TenantId))
            {
                throw new InvalidOperationException("Tenant context is required.");
            }

            return _tenantContext.TenantId;
        }

        private static string? NormalizeOptional(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
