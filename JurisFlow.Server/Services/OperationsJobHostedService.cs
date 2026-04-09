using JurisFlow.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace JurisFlow.Server.Services
{
    public class OperationsJobHostedService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IConfiguration _configuration;
        private readonly ILogger<OperationsJobHostedService> _logger;
        private readonly IHostEnvironment _environment;
        private readonly TenantJobRunner _tenantJobs;

        public OperationsJobHostedService(
            IServiceProvider serviceProvider,
            IConfiguration configuration,
            ILogger<OperationsJobHostedService> logger,
            IHostEnvironment environment,
            TenantJobRunner tenantJobs)
        {
            _serviceProvider = serviceProvider;
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
                configurationKey: "Operations:InitialDelaySeconds",
                productionDefaultSeconds: 45,
                serviceName: nameof(OperationsJobHostedService),
                cancellationToken: stoppingToken);

            var intervalMinutes = _configuration.GetValue("Operations:JobIntervalMinutes", 5);

            while (!stoppingToken.IsCancellationRequested)
            {
                var enabled = _configuration.GetValue("Operations:JobsEnabled", true);
                if (enabled)
                {
                    await RunJobsAsync(stoppingToken);
                }

                await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), stoppingToken);
            }
        }

        private async Task RunJobsAsync(CancellationToken stoppingToken)
        {
            await _tenantJobs.RunAsync(async sp =>
            {
                var context = sp.GetRequiredService<JurisFlowDbContext>();
                var paymentPlanService = sp.GetRequiredService<PaymentPlanService>();
                var smsReminderService = sp.GetRequiredService<SmsReminderService>();
                var outboundEmailService = sp.GetRequiredService<OutboundEmailService>();
                var signatureLifecycleService = sp.GetRequiredService<SignatureLifecycleService>();
                var integrationSyncRunner = sp.GetRequiredService<IntegrationSyncRunner>();
                var integrationWebhookService = sp.GetRequiredService<IntegrationWebhookService>();

                var now = DateTime.UtcNow;
                var planBatchSize = _configuration.GetValue("Operations:PaymentPlanBatchSize", 25);
                var duePlans = await context.PaymentPlans
                    .Where(p => p.Status == "Active" && p.NextRunDate <= now && p.RemainingAmount > 0)
                    .OrderBy(p => p.NextRunDate)
                    .Take(planBatchSize)
                    .ToListAsync(stoppingToken);

                var processedPlans = 0;
                foreach (var plan in duePlans)
                {
                    var client = await context.Clients.FindAsync(new object?[] { plan.ClientId }, cancellationToken: stoppingToken);
                    var transaction = await paymentPlanService.RunPlanAsync(plan, null, client?.Email, client?.Name, now);
                    if (transaction != null)
                    {
                        processedPlans++;
                    }
                }

                var smsSent = await smsReminderService.ProcessPendingAsync();
                var emailSent = await outboundEmailService.ProcessPendingAsync();
                var signatureResult = await signatureLifecycleService.ProcessAsync(now);
                var integrationSyncIntervalMinutes = Math.Clamp(
                    _configuration.GetValue("Operations:IntegrationSyncIntervalMinutes", 60),
                    5,
                    24 * 60);
                var integrationBatchSize = Math.Clamp(
                    _configuration.GetValue("Operations:IntegrationSyncBatchSize", 10),
                    1,
                    200);
                var integrationCandidateBatchSize = Math.Clamp(
                    _configuration.GetValue("Operations:IntegrationSyncCandidateBatchSize", integrationBatchSize * 5),
                    integrationBatchSize,
                    1000);
                var integrationCandidates = await context.IntegrationConnections
                    .Where(i =>
                        i.SyncEnabled &&
                        (i.Status == "connected" || i.Status == "error"))
                    .OrderBy(i => i.LastSyncAt ?? DateTime.MinValue)
                    .Take(integrationCandidateBatchSize)
                    .ToListAsync(stoppingToken);

                var integrationConnections = integrationCandidates
                    .Where(connection =>
                    {
                        var pollingInterval = integrationWebhookService.ResolveFallbackPollingMinutes(
                            connection.ProviderKey,
                            integrationSyncIntervalMinutes);
                        return IsScheduledSyncDue(connection, now, pollingInterval);
                    })
                    .Take(integrationBatchSize)
                    .ToList();

                var integrationSynced = 0;
                var integrationFailed = 0;
                var syncBucket = now.Ticks / TimeSpan.FromMinutes(integrationSyncIntervalMinutes).Ticks;
                foreach (var connection in integrationConnections)
                {
                    var runResult = await integrationSyncRunner.RunAsync(
                        connection,
                        new IntegrationSyncRunRequest
                        {
                            Trigger = IntegrationRunTriggers.Scheduled,
                            IdempotencyKey = $"scheduled:{connection.Id}:{syncBucket}"
                        },
                        stoppingToken);

                    if (runResult.Success)
                    {
                        integrationSynced++;
                    }
                    else
                    {
                        integrationFailed++;
                        _logger.LogWarning(
                            "Integration sync failed. Provider={Provider} ProviderKey={ProviderKey} Error={Error}",
                            connection.Provider,
                            connection.ProviderKey,
                            runResult.Message);
                    }
                }

                if (integrationConnections.Count > 0)
                {
                    await context.SaveChangesAsync(stoppingToken);
                }

                _logger.LogInformation(
                    "Operations jobs completed. PaymentPlans={ProcessedPlans} SmsSent={SmsSent} EmailSent={EmailSent} SignatureReminders={SignatureReminders} SignatureExpired={SignatureExpired} IntegrationSynced={IntegrationSynced} IntegrationFailed={IntegrationFailed}",
                    processedPlans,
                    smsSent,
                    emailSent,
                    signatureResult.RemindersQueued,
                    signatureResult.Expired,
                    integrationSynced,
                    integrationFailed);
            }, stoppingToken);
        }

        private static bool IsScheduledSyncDue(Models.IntegrationConnection connection, DateTime now, int pollingIntervalMinutes)
        {
            var dueBefore = now.AddMinutes(-Math.Clamp(pollingIntervalMinutes, 5, 24 * 60));
            return !connection.LastSyncAt.HasValue || connection.LastSyncAt <= dueBefore;
        }
    }
}
