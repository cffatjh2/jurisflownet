using Microsoft.Extensions.Hosting;

namespace JurisFlow.Server.Services
{
    public class RetentionHostedService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<RetentionHostedService> _logger;
        private readonly IConfiguration _configuration;
        private readonly IHostEnvironment _environment;
        private readonly TenantJobRunner _tenantJobs;

        public RetentionHostedService(
            IServiceProvider serviceProvider,
            ILogger<RetentionHostedService> logger,
            IConfiguration configuration,
            IHostEnvironment environment,
            TenantJobRunner tenantJobs)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _configuration = configuration;
            _environment = environment;
            _tenantJobs = tenantJobs;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await HostedServiceStartupDelay.WaitAsync(
                _configuration,
                _environment,
                _logger,
                configurationKey: "Retention:InitialDelaySeconds",
                productionDefaultSeconds: 120,
                serviceName: nameof(RetentionHostedService),
                cancellationToken: stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await _tenantJobs.RunAsync(async sp =>
                    {
                        var retention = sp.GetRequiredService<RetentionService>();
                        await retention.ApplyRetentionAsync();
                    }, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Retention cleanup failed");
                }

                await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
            }
        }
    }
}
