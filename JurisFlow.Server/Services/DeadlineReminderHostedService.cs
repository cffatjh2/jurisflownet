using Microsoft.Extensions.Hosting;

namespace JurisFlow.Server.Services
{
    public class DeadlineReminderHostedService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<DeadlineReminderHostedService> _logger;
        private readonly IConfiguration _configuration;
        private readonly IHostEnvironment _environment;
        private readonly TenantJobRunner _tenantJobs;

        public DeadlineReminderHostedService(
            IServiceProvider serviceProvider,
            ILogger<DeadlineReminderHostedService> logger,
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
                configurationKey: "Deadlines:InitialDelaySeconds",
                productionDefaultSeconds: 30,
                serviceName: nameof(DeadlineReminderHostedService),
                cancellationToken: stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                var enabled = _configuration.GetValue("Deadlines:RemindersEnabled", true);
                var intervalMinutes = _configuration.GetValue("Deadlines:ReminderIntervalMinutes", 60);
                if (intervalMinutes < 5)
                {
                    intervalMinutes = 5;
                }

                if (enabled)
                {
                    try
                    {
                        await _tenantJobs.RunAsync(async sp =>
                        {
                            var service = sp.GetRequiredService<DeadlineReminderService>();
                            await service.ProcessAsync();
                        }, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Deadline reminder processing failed.");
                    }
                }

                await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), stoppingToken);
            }
        }
    }
}
