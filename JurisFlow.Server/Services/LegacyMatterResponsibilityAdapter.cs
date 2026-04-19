using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;

namespace JurisFlow.Server.Services
{
    public sealed class LegacyMatterResponsibilityAdapter
    {
        private readonly JurisFlowDbContext _context;
        private readonly TenantContext _tenantContext;
        private readonly RefactorWaveOptions _options;

        public LegacyMatterResponsibilityAdapter(
            JurisFlowDbContext context,
            TenantContext tenantContext,
            IOptions<RefactorWaveOptions> options)
        {
            _context = context;
            _tenantContext = tenantContext;
            _options = options.Value;
        }

        public async Task<HashSet<string>> ResolveNotificationTargetsAsync(Matter? matter, string? currentUserId)
        {
            var userIds = new HashSet<string>(StringComparer.Ordinal);
            if (!string.IsNullOrWhiteSpace(currentUserId))
            {
                userIds.Add(currentUserId);
            }

            if (!_options.UseLegacyMatterResponsibilityAdapter || matter == null || string.IsNullOrWhiteSpace(matter.ResponsibleAttorney))
            {
                return userIds;
            }

            var tenantId = RequireTenantId();
            var responsibleAttorney = matter.ResponsibleAttorney.Trim();

            var directUserIds = await _context.Users
                .AsNoTracking()
                .Where(u => EF.Property<string>(u, "TenantId") == tenantId)
                .Where(u => u.Id == responsibleAttorney || u.Name == responsibleAttorney)
                .Select(u => u.Id)
                .ToListAsync();

            foreach (var userId in directUserIds)
            {
                userIds.Add(userId);
            }

            var employeeUserIds = await _context.Employees
                .AsNoTracking()
                .Where(e => EF.Property<string>(e, "TenantId") == tenantId)
                .Where(e => e.Id == responsibleAttorney || ((e.FirstName + " " + e.LastName) == responsibleAttorney))
                .Where(e => e.UserId != null)
                .Select(e => e.UserId!)
                .ToListAsync();

            foreach (var userId in employeeUserIds)
            {
                userIds.Add(userId);
            }

            return userIds;
        }

        private string RequireTenantId()
        {
            if (string.IsNullOrWhiteSpace(_tenantContext.TenantId))
            {
                throw new InvalidOperationException("Tenant context is required.");
            }

            return _tenantContext.TenantId;
        }
    }
}
