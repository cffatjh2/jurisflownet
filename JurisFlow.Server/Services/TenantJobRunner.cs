using Microsoft.EntityFrameworkCore;
using JurisFlow.Server.Data;

namespace JurisFlow.Server.Services
{
    public class TenantJobRunner
    {
        private readonly IServiceProvider _serviceProvider;

        public TenantJobRunner(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public async Task RunAsync(Func<IServiceProvider, Task> action, CancellationToken cancellationToken = default)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
            var tenants = await db.Tenants
                .AsNoTracking()
                .Where(t => t.IsActive)
                .OrderBy(t => t.Slug)
                .ToListAsync(cancellationToken);

            foreach (var tenant in tenants)
            {
                using var tenantScope = _serviceProvider.CreateScope();
                var tenantContext = tenantScope.ServiceProvider.GetRequiredService<TenantContext>();
                tenantContext.Set(tenant.Id, tenant.Slug);
                await action(tenantScope.ServiceProvider);
            }
        }
    }
}
