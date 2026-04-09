namespace JurisFlow.Server.Services
{
    internal static class HostedServiceStartupDelay
    {
        public static async Task WaitAsync(
            IConfiguration configuration,
            IHostEnvironment environment,
            ILogger logger,
            string configurationKey,
            int productionDefaultSeconds,
            string serviceName,
            CancellationToken cancellationToken)
        {
            var configuredDelaySeconds = configuration.GetValue<int?>(configurationKey);
            var delaySeconds = configuredDelaySeconds
                ?? (environment.IsDevelopment() ? 0 : productionDefaultSeconds);

            if (delaySeconds <= 0)
            {
                return;
            }

            logger.LogInformation(
                "{ServiceName} delaying initial execution by {DelaySeconds} seconds to prioritize startup traffic.",
                serviceName,
                delaySeconds);

            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
        }
    }
}
