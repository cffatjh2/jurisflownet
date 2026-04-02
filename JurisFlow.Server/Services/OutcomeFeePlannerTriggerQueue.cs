using System.Threading.Channels;

namespace JurisFlow.Server.Services
{
    public sealed record OutcomeFeePlannerTriggerJob(
        string TenantId,
        string TenantSlug,
        string UserId,
        OutcomeFeePlanTriggerRequest Request);

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
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
                    tenantContext.Set(job.TenantId, job.TenantSlug);

                    var planner = scope.ServiceProvider.GetRequiredService<OutcomeFeePlannerService>();
                    await planner.TryProcessTriggerAsync(job.Request, job.UserId, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Outcome fee planner trigger failed in background. MatterId={MatterId} TriggerType={TriggerType} EntityId={EntityId}",
                        job.Request.MatterId,
                        job.Request.TriggerType,
                        job.Request.TriggerEntityId);
                }
            }
        }
    }
}
