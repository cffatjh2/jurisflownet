using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace JurisFlow.Server.Services
{
    public sealed record MatterRelatedClientResolution(
        IReadOnlyList<string> ClientIds,
        IReadOnlyList<string> InvalidClientIds);

    public sealed class MatterClientLinkService
    {
        private readonly JurisFlowDbContext _context;

        public MatterClientLinkService(JurisFlowDbContext context)
        {
            _context = context;
        }

        public IQueryable<string> BuildVisibleMatterIdsForClientQuery(string clientId)
        {
            var normalizedClientId = clientId?.Trim();
            if (string.IsNullOrWhiteSpace(normalizedClientId))
            {
                return Enumerable.Empty<string>().AsQueryable();
            }

            return _context.Matters
                .AsNoTracking()
                .Where(m => m.ClientId == normalizedClientId)
                .Select(m => m.Id)
                .Concat(
                    _context.MatterClientLinks
                        .AsNoTracking()
                        .Where(link => link.ClientId == normalizedClientId)
                        .Select(link => link.MatterId))
                .Distinct();
        }

        public async Task<HashSet<string>> GetVisibleMatterIdSetForClientAsync(string clientId, CancellationToken cancellationToken = default)
        {
            var matterIds = await BuildVisibleMatterIdsForClientQuery(clientId).ToListAsync(cancellationToken);
            return matterIds.ToHashSet(StringComparer.Ordinal);
        }

        public async Task<bool> ClientCanAccessMatterAsync(string clientId, string matterId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(matterId))
            {
                return false;
            }

            return await BuildVisibleMatterIdsForClientQuery(clientId.Trim())
                .AnyAsync(id => id == matterId.Trim(), cancellationToken);
        }

        public async Task<MatterRelatedClientResolution> ResolveRelatedClientIdsAsync(
            string primaryClientId,
            IEnumerable<string>? requestedClientIds,
            CancellationToken cancellationToken = default)
        {
            var normalizedPrimaryClientId = primaryClientId?.Trim();
            var normalizedRequestedIds = (requestedClientIds ?? Enumerable.Empty<string>())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .Where(id => !string.Equals(id, normalizedPrimaryClientId, StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (normalizedRequestedIds.Count == 0)
            {
                return new MatterRelatedClientResolution(Array.Empty<string>(), Array.Empty<string>());
            }

            var validClientIds = await _context.Clients
                .AsNoTracking()
                .Where(client => normalizedRequestedIds.Contains(client.Id))
                .Select(client => client.Id)
                .ToListAsync(cancellationToken);

            var validIdSet = validClientIds.ToHashSet(StringComparer.Ordinal);
            var resolvedIds = normalizedRequestedIds
                .Where(id => validIdSet.Contains(id))
                .ToList();
            var invalidIds = normalizedRequestedIds
                .Where(id => !validIdSet.Contains(id))
                .ToList();

            return new MatterRelatedClientResolution(resolvedIds, invalidIds);
        }

        public async Task SyncRelatedClientsAsync(
            string matterId,
            IReadOnlyCollection<string> relatedClientIds,
            CancellationToken cancellationToken = default)
        {
            var normalizedMatterId = matterId?.Trim();
            if (string.IsNullOrWhiteSpace(normalizedMatterId))
            {
                return;
            }

            var existingLinks = await _context.MatterClientLinks
                .Where(link => link.MatterId == normalizedMatterId)
                .ToListAsync(cancellationToken);

            var targetClientIds = relatedClientIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToHashSet(StringComparer.Ordinal);

            var linksToRemove = existingLinks
                .Where(link => !targetClientIds.Contains(link.ClientId))
                .ToList();

            if (linksToRemove.Count > 0)
            {
                _context.MatterClientLinks.RemoveRange(linksToRemove);
            }

            var existingClientIds = existingLinks
                .Select(link => link.ClientId)
                .ToHashSet(StringComparer.Ordinal);

            var now = DateTime.UtcNow;
            foreach (var clientId in targetClientIds)
            {
                if (existingClientIds.Contains(clientId))
                {
                    continue;
                }

                _context.MatterClientLinks.Add(new MatterClientLink
                {
                    Id = Guid.NewGuid().ToString(),
                    MatterId = normalizedMatterId,
                    ClientId = clientId,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }
        }

        public async Task RemovePrimaryClientDuplicatesAsync(string matterId, string primaryClientId, CancellationToken cancellationToken = default)
        {
            var normalizedMatterId = matterId?.Trim();
            var normalizedPrimaryClientId = primaryClientId?.Trim();
            if (string.IsNullOrWhiteSpace(normalizedMatterId) || string.IsNullOrWhiteSpace(normalizedPrimaryClientId))
            {
                return;
            }

            var duplicates = await _context.MatterClientLinks
                .Where(link => link.MatterId == normalizedMatterId && link.ClientId == normalizedPrimaryClientId)
                .ToListAsync(cancellationToken);

            if (duplicates.Count > 0)
            {
                _context.MatterClientLinks.RemoveRange(duplicates);
            }
        }

        public async Task PopulateRelatedClientsAsync(IList<Matter> matters, CancellationToken cancellationToken = default)
        {
            if (matters == null || matters.Count == 0)
            {
                return;
            }

            var matterIds = matters
                .Where(matter => !string.IsNullOrWhiteSpace(matter.Id))
                .Select(matter => matter.Id)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (matterIds.Count == 0)
            {
                return;
            }

            var links = await _context.MatterClientLinks
                .AsNoTracking()
                .Where(link => matterIds.Contains(link.MatterId))
                .OrderBy(link => link.CreatedAt)
                .ToListAsync(cancellationToken);

            var relatedClientIds = links
                .Select(link => link.ClientId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var relatedClients = relatedClientIds.Count == 0
                ? new Dictionary<string, Client>(StringComparer.Ordinal)
                : await _context.Clients
                    .AsNoTracking()
                    .Where(client => relatedClientIds.Contains(client.Id))
                    .ToDictionaryAsync(client => client.Id, client => client, StringComparer.Ordinal, cancellationToken);

            var linksByMatterId = links
                .GroupBy(link => link.MatterId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

            foreach (var matter in matters)
            {
                if (matter == null || string.IsNullOrWhiteSpace(matter.Id))
                {
                    continue;
                }

                if (!linksByMatterId.TryGetValue(matter.Id, out var matterLinks))
                {
                    matter.RelatedClientIds = new List<string>();
                    matter.RelatedClients = new List<Client>();
                    continue;
                }

                var secondaryIds = matterLinks
                    .Select(link => link.ClientId)
                    .Where(id => !string.IsNullOrWhiteSpace(id) && !string.Equals(id, matter.ClientId, StringComparison.Ordinal))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                matter.RelatedClientIds = secondaryIds;
                matter.RelatedClients = secondaryIds
                    .Where(relatedClients.ContainsKey)
                    .Select(id => relatedClients[id])
                    .ToList();
            }
        }

        public Task PopulateRelatedClientsAsync(Matter matter, CancellationToken cancellationToken = default)
        {
            if (matter == null)
            {
                return Task.CompletedTask;
            }

            return PopulateRelatedClientsAsync(new List<Matter> { matter }, cancellationToken);
        }
    }
}
