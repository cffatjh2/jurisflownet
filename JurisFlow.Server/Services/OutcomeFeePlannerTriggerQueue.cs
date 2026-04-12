using System.Threading.Channels;

namespace JurisFlow.Server.Services
{
    public sealed record OutcomeFeePlannerTriggerJob(
        string TenantId,
        string TenantSlug,
        string UserId,
        OutcomeFeePlanTriggerRequest? PlannerRequest,
        ClientTransparencyTriggerRequest? TransparencyRequest = null);

    public sealed class OutcomeFeePlannerTriggerQueue
    {
        private readonly Channel<OutcomeFeePlannerTriggerJob> _channel;

        public OutcomeFeePlannerTriggerQueue()
        {
            _channel = Channel.CreateUnbounded<OutcomeFeePlannerTriggerJob>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });
        }

        public bool Enqueue(OutcomeFeePlannerTriggerJob job) => _channel.Writer.TryWrite(job);

        public IAsyncEnumerable<OutcomeFeePlannerTriggerJob> DequeueAllAsync(CancellationToken cancellationToken) =>
            _channel.Reader.ReadAllAsync(cancellationToken);
    }

    public sealed class OutcomeFeePlannerTriggerHostedService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly OutcomeFeePlannerTriggerQueue _queue;
        private readonly ILogger<OutcomeFeePlannerTriggerHostedService> _logger;

        public OutcomeFeePlannerTriggerHostedService(
            IServiceProvider serviceProvider,
            OutcomeFeePlannerTriggerQueue queue,
            ILogger<OutcomeFeePlannerTriggerHostedService> logger)
        {
            _serviceProvider = serviceProvider;
            _queue = queue;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await foreach (var job in _queue.DequeueAllAsync(stoppingToken))
            {
                if (string.IsNullOrWhiteSpace(job.TenantId))
                {
                    _logger.LogWarning("Workflow trigger queue skipped a job because tenant context was empty.");
                    continue;
                }

                try
                {
                    if (job.PlannerRequest != null)
                    {
                        await ProcessPlannerAsync(job, stoppingToken);
                    }

                    if (job.TransparencyRequest != null)
                    {
                        await ProcessTransparencyAsync(job, stoppingToken);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Workflow trigger failed in background. MatterId={MatterId} TriggerType={TriggerType} EntityId={EntityId}",
                        job.PlannerRequest?.MatterId ?? job.TransparencyRequest?.MatterId,
                        job.PlannerRequest?.TriggerType ?? job.TransparencyRequest?.TriggerType,
                        job.PlannerRequest?.TriggerEntityId ?? job.TransparencyRequest?.TriggerEntityId);
                }
            }
        }

        private async Task ProcessPlannerAsync(OutcomeFeePlannerTriggerJob job, CancellationToken stoppingToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
            tenantContext.Set(job.TenantId, job.TenantSlug);

            var planner = scope.ServiceProvider.GetRequiredService<OutcomeFeePlannerService>();
            await planner.TryProcessTriggerAsync(job.PlannerRequest!, job.UserId, stoppingToken);
        }

        private async Task ProcessTransparencyAsync(OutcomeFeePlannerTriggerJob job, CancellationToken stoppingToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
            tenantContext.Set(job.TenantId, job.TenantSlug);

            var transparencyService = scope.ServiceProvider.GetRequiredService<ClientTransparencyService>();
            await transparencyService.TryProcessTriggerAsync(job.TransparencyRequest!, job.UserId, stoppingToken);
        }
    }
}
