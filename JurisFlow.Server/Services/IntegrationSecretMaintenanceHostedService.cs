using JurisFlow.Server.Data;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace JurisFlow.Server.Services
{
    public sealed class IntegrationSecretMaintenanceHostedService : BackgroundService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<IntegrationSecretMaintenanceHostedService> _logger;
        private readonly IHostEnvironment _environment;
        private readonly TenantJobRunner _tenantJobs;

        public IntegrationSecretMaintenanceHostedService(
            IConfiguration configuration,
            ILogger<IntegrationSecretMaintenanceHostedService> logger,
            IHostEnvironment environment,
            TenantJobRunner tenantJobs)
        {
            _configuration = configuration;
            _logger = logger;
            _environment = environment;
            _tenantJobs = tenantJobs;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await HostedServiceStartupDelay.WaitAsync(
                _configuration,
                _environment,
                _logger,
                configurationKey: "Security:IntegrationSecrets:InitialDelaySeconds",
                productionDefaultSeconds: 120,
                serviceName: nameof(IntegrationSecretMaintenanceHostedService),
                cancellationToken: stoppingToken);

            var backfillOnStartup = _configuration.GetValue("Security:IntegrationSecrets:BackfillOnStartup", true);
            if (backfillOnStartup)
            {
                await RunBackfillAsync(stoppingToken);
            }

            var rotationEnabled = _configuration.GetValue("Security:IntegrationSecrets:RotationEnabled", true);
            var rotationIntervalMinutes = Math.Clamp(
                _configuration.GetValue("Security:IntegrationSecrets:RotationIntervalMinutes", 12 * 60),
                15,
                7 * 24 * 60);

            while (!stoppingToken.IsCancellationRequested)
            {
                if (rotationEnabled)
                {
                    await RunRotationAsync(stoppingToken);
                }

                await Task.Delay(TimeSpan.FromMinutes(rotationIntervalMinutes), stoppingToken);
            }
        }

        private async Task RunBackfillAsync(CancellationToken cancellationToken)
        {
            var totalMigrated = 0;
            await _tenantJobs.RunAsync(async sp =>
            {
                var db = sp.GetRequiredService<JurisFlowDbContext>();
                var connectorService = sp.GetRequiredService<IntegrationConnectorService>();
                var batchSize = Math.Clamp(
                    _configuration.GetValue("Security:IntegrationSecrets:BackfillBatchSize", 500),
                    10,
                    5000);

                var connections = await db.IntegrationConnections
                    .Where(c => !string.IsNullOrWhiteSpace(c.MetadataJson))
                    .OrderBy(c => c.UpdatedAt)
                    .Take(batchSize)
                    .ToListAsync(cancellationToken);

                var migrated = 0;
                foreach (var connection in connections)
                {
                    var result = await connectorService.MigrateMetadataSecretsAsync(
                        connection.Id,
                        connection.ProviderKey,
                        connection.MetadataJson,
                        cancellationToken);

                    if (!result.Migrated)
                    {
                        continue;
                    }

                    connection.MetadataJson = result.MetadataJson;
                    connection.UpdatedAt = DateTime.UtcNow;
                    migrated++;
                }

                if (migrated > 0)
                {
                    await db.SaveChangesAsync(cancellationToken);
                }

                totalMigrated += migrated;
            }, cancellationToken);

            if (totalMigrated > 0)
            {
                _logger.LogInformation(
                    "Integration secret metadata backfill completed. MigratedConnections={MigratedConnections}",
                    totalMigrated);
            }
        }

        private async Task RunRotationAsync(CancellationToken cancellationToken)
        {
            var totalRotated = 0;
            await _tenantJobs.RunAsync(async sp =>
            {
                var db = sp.GetRequiredService<JurisFlowDbContext>();
                var secretStore = sp.GetRequiredService<IIntegrationSecretStore>();

                var rotated = await secretStore.RotateOutdatedSecretsAsync(
                    IntegrationSecretScope.Rotation,
                    cancellationToken);

                if (rotated > 0)
                {
                    await db.SaveChangesAsync(cancellationToken);
                }

                totalRotated += rotated;
            }, cancellationToken);

            if (totalRotated > 0)
            {
                _logger.LogInformation(
                    "Integration secret rotation completed. RotatedEntries={RotatedEntries}",
                    totalRotated);
            }
        }
    }
}
