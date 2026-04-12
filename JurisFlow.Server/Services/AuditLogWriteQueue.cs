using System.Threading.Channels;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace JurisFlow.Server.Services
{
    public sealed record AuditLogWriteJob(
        string TenantId,
        string TenantSlug,
        AuditLog Audit);

    public sealed class AuditLogWriteQueue
    {
        private readonly Channel<AuditLogWriteJob> _channel;

        public AuditLogWriteQueue()
        {
            _channel = Channel.CreateBounded<AuditLogWriteJob>(new BoundedChannelOptions(4096)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait
            });
        }

        public bool TryEnqueue(AuditLogWriteJob job) => _channel.Writer.TryWrite(job);

        public ValueTask WriteAsync(AuditLogWriteJob job, CancellationToken cancellationToken = default) =>
            _channel.Writer.WriteAsync(job, cancellationToken);

        public IAsyncEnumerable<AuditLogWriteJob> DequeueAllAsync(CancellationToken cancellationToken) =>
            _channel.Reader.ReadAllAsync(cancellationToken);

        public bool TryDequeue(out AuditLogWriteJob? job) => _channel.Reader.TryRead(out job);
    }

    public sealed class AuditLogWriteHostedService : BackgroundService
    {
        private const int MaxBatchSize = 64;
        private readonly IServiceProvider _serviceProvider;
        private readonly AuditLogWriteQueue _queue;
        private readonly ILogger<AuditLogWriteHostedService> _logger;

        public AuditLogWriteHostedService(
            IServiceProvider serviceProvider,
            AuditLogWriteQueue queue,
            ILogger<AuditLogWriteHostedService> logger)
        {
            _serviceProvider = serviceProvider;
            _queue = queue;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await foreach (var firstJob in _queue.DequeueAllAsync(stoppingToken))
            {
                var batch = new List<AuditLogWriteJob>(MaxBatchSize) { firstJob };
                await DrainBatchAsync(batch, stoppingToken);

                foreach (var tenantBatch in batch
                             .Where(job => !string.IsNullOrWhiteSpace(job.TenantId))
                             .GroupBy(job => job.TenantId, StringComparer.Ordinal))
                {
                    await PersistBatchWithRetryAsync(tenantBatch.ToList(), stoppingToken);
                }

                foreach (var invalidJob in batch.Where(job => string.IsNullOrWhiteSpace(job.TenantId)))
                {
                    _logger.LogWarning(
                        "Audit write skipped because tenant context was missing. Action={Action} Entity={Entity} EntityId={EntityId}",
                        invalidJob.Audit.Action,
                        invalidJob.Audit.Entity,
                        invalidJob.Audit.EntityId);
                }
            }
        }

        private async Task DrainBatchAsync(List<AuditLogWriteJob> batch, CancellationToken stoppingToken)
        {
            while (batch.Count < MaxBatchSize && !stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(15, stoppingToken);

                while (batch.Count < MaxBatchSize && _queue.TryDequeue(out var nextJob))
                {
                    if (nextJob == null)
                    {
                        break;
                    }

                    batch.Add(nextJob);
                }

                if (batch.Count > 1)
                {
                    return;
                }
            }
        }

        private async Task PersistBatchWithRetryAsync(List<AuditLogWriteJob> jobs, CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await PersistBatchCoreAsync(jobs, stoppingToken);
                    return;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    var sample = jobs[0];
                    _logger.LogError(
                        ex,
                        "Audit batch write failed. TenantId={TenantId} Count={Count} FirstAction={Action}",
                        sample.TenantId,
                        jobs.Count,
                        sample.Audit.Action);

                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                }
            }
        }

        private async Task PersistBatchCoreAsync(List<AuditLogWriteJob> jobs, CancellationToken stoppingToken)
        {
            var tenantId = jobs[0].TenantId;
            var tenantSlug = jobs[0].TenantSlug;

            using var scope = _serviceProvider.CreateScope();
            var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
            tenantContext.Set(tenantId, tenantSlug);

            var context = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
            var integrityService = scope.ServiceProvider.GetRequiredService<AuditLogIntegrityService>();

            var audits = jobs.Select(job => job.Audit).ToList();
            if (integrityService.IsConfigured)
            {
                var state = await integrityService.GetLatestStateAsync(stoppingToken);
                integrityService.PrepareBatch(audits, state);
            }

            context.AuditLogs.AddRange(audits);
            await context.SaveChangesAsync(stoppingToken);
        }
    }
}
