using Microsoft.EntityFrameworkCore;
using JurisFlow.Server.Data;

namespace JurisFlow.Server.Services
{
    public class FirmStructureService
    {
        private readonly JurisFlowDbContext _context;

        public FirmStructureService(JurisFlowDbContext context)
        {
            _context = context;
        }

        public async Task<string?> GetDefaultEntityIdAsync()
        {
            return await _context.FirmEntities
                .OrderByDescending(e => e.IsDefault)
                .ThenByDescending(e => e.IsActive)
                .ThenBy(e => e.Name)
                .Select(e => e.Id)
                .FirstOrDefaultAsync();
        }

        public async Task<string?> GetDefaultOfficeIdAsync(string? entityId)
        {
            if (string.IsNullOrWhiteSpace(entityId))
            {
                return await _context.Offices
                    .OrderByDescending(o => o.IsDefault)
                    .ThenByDescending(o => o.IsActive)
                    .ThenBy(o => o.Name)
                    .Select(o => o.Id)
                    .FirstOrDefaultAsync();
            }

            return await _context.Offices
                .Where(o => o.EntityId == entityId)
                .OrderByDescending(o => o.IsDefault)
                .ThenByDescending(o => o.IsActive)
                .ThenBy(o => o.Name)
                .Select(o => o.Id)
                .FirstOrDefaultAsync();
        }

        public async Task<(string? entityId, string? officeId)> ResolveEntityOfficeAsync(string? entityId, string? officeId)
        {
            var resolvedEntityId = NormalizeId(entityId);
            var resolvedOfficeId = NormalizeId(officeId);

            if (!string.IsNullOrWhiteSpace(resolvedEntityId))
            {
                var entityExists = await _context.FirmEntities
                    .AsNoTracking()
                    .AnyAsync(e => e.Id == resolvedEntityId && e.IsActive);
                if (!entityExists)
                {
                    resolvedEntityId = null;
                }
            }

            if (!string.IsNullOrWhiteSpace(resolvedOfficeId))
            {
                var office = await _context.Offices
                    .AsNoTracking()
                    .Where(o => o.Id == resolvedOfficeId && o.IsActive)
                    .Select(o => new { o.Id, o.EntityId })
                    .FirstOrDefaultAsync();

                if (office == null)
                {
                    resolvedOfficeId = null;
                }
                else if (string.IsNullOrWhiteSpace(resolvedEntityId))
                {
                    resolvedEntityId = office.EntityId;
                }
                else if (!string.Equals(office.EntityId, resolvedEntityId, StringComparison.Ordinal))
                {
                    resolvedOfficeId = null;
                }
            }

            resolvedEntityId ??= await GetDefaultEntityIdAsync();

            if (!string.IsNullOrWhiteSpace(resolvedOfficeId))
            {
                var officeMatchesEntity = await _context.Offices
                    .AsNoTracking()
                    .AnyAsync(o => o.Id == resolvedOfficeId
                        && o.IsActive
                        && (string.IsNullOrWhiteSpace(resolvedEntityId) || o.EntityId == resolvedEntityId));
                if (!officeMatchesEntity)
                {
                    resolvedOfficeId = null;
                }
            }

            resolvedOfficeId ??= await GetDefaultOfficeIdAsync(resolvedEntityId);

            return (resolvedEntityId, resolvedOfficeId);
        }

        public async Task<(string? entityId, string? officeId)> ResolveEntityOfficeFromMatterAsync(string? matterId, string? entityId, string? officeId)
        {
            if (!string.IsNullOrWhiteSpace(matterId))
            {
                var matter = await _context.Matters
                    .Where(m => m.Id == matterId)
                    .Select(m => new { m.EntityId, m.OfficeId })
                    .FirstOrDefaultAsync();
                if (matter != null)
                {
                    entityId ??= matter.EntityId;
                    officeId ??= matter.OfficeId;
                }
            }

            return await ResolveEntityOfficeAsync(entityId, officeId);
        }

        private static string? NormalizeId(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
