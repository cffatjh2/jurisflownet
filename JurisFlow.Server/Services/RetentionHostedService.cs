using Microsoft.Extensions.Hosting;

namespace JurisFlow.Server.Services
{
    public class RetentionHostedService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<RetentionHostedService> _logger;
        private readonly TenantJobRunner _tenantJobs;

        public RetentionHostedService(IServiceProvider serviceProvider, ILogger<RetentionHostedService> logger, TenantJobRunner tenantJobs)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _tenantJobs = tenantJobs;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
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
