using System.Threading.Channels;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Task = System.Threading.Tasks.Task;

namespace JurisFlow.Server.Services
{
    public sealed record AuditLogWriteJob(
        string TenantId,
        string TenantSlug,
        AuditLog Audit);

    public sealed record AuditLogDeadLetterJob(
        AuditLogWriteJob Job,
        string Reason,
        string? ExceptionType,
        string? ExceptionMessage,
        int AttemptCount,
        DateTime DeadLetteredAt);

    public sealed record AuditLogQueueSnapshot(
        int Capacity,
        int DeadLetterCapacity,
        long EnqueuedCount,
        long DeadLetteredCount,
        long DroppedDeadLetterCount);

    public sealed class AuditLogWriteQueue
    {
        private readonly Channel<AuditLogWriteJob> _channel;
        private readonly Channel<AuditLogDeadLetterJob> _deadLetterChannel;
        private readonly int _capacity;
        private readonly int _deadLetterCapacity;
        private long _enqueuedCount;
        private long _deadLetteredCount;
        private long _droppedDeadLetterCount;

        public AuditLogWriteQueue(IConfiguration configuration)
        {
            _capacity = Math.Clamp(configuration.GetValue("Security:AuditLogQueue:Capacity", 4096), 1, 100_000);
            _deadLetterCapacity = Math.Clamp(configuration.GetValue("Security:AuditLogQueue:DeadLetterCapacity", 1024), 1, 100_000);

            _channel = Channel.CreateBounded<AuditLogWriteJob>(new BoundedChannelOptions(_capacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait
            });

            _deadLetterChannel = Channel.CreateBounded<AuditLogDeadLetterJob>(new BoundedChannelOptions(_deadLetterCapacity)
            {
                SingleReader = false,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait
            });
        }

        public bool TryEnqueue(AuditLogWriteJob job)
        {
            if (!_channel.Writer.TryWrite(job))
            {
                return false;
            }

            Interlocked.Increment(ref _enqueuedCount);
            return true;
        }

        public async ValueTask WriteAsync(AuditLogWriteJob job, CancellationToken cancellationToken = default)
        {
            await _channel.Writer.WriteAsync(job, cancellationToken);
            Interlocked.Increment(ref _enqueuedCount);
        }

        public IAsyncEnumerable<AuditLogWriteJob> DequeueAllAsync(CancellationToken cancellationToken) =>
            _channel.Reader.ReadAllAsync(cancellationToken);

        public bool TryDequeue(out AuditLogWriteJob? job) => _channel.Reader.TryRead(out job);

        public bool TryEnqueueDeadLetter(AuditLogDeadLetterJob job)
        {
            if (!_deadLetterChannel.Writer.TryWrite(job))
            {
                Interlocked.Increment(ref _droppedDeadLetterCount);
                return false;
            }

            Interlocked.Increment(ref _deadLetteredCount);
            return true;
        }

        public bool TryDequeueDeadLetter(out AuditLogDeadLetterJob? job) => _deadLetterChannel.Reader.TryRead(out job);

        public IReadOnlyList<AuditLogDeadLetterJob> DrainDeadLetters(int maxItems = 100)
        {
            var limit = Math.Clamp(maxItems, 1, _deadLetterCapacity);
            var items = new List<AuditLogDeadLetterJob>(limit);
            while (items.Count < limit && _deadLetterChannel.Reader.TryRead(out var job))
            {
                items.Add(job);
            }

            return items;
        }

        public AuditLogQueueSnapshot GetSnapshot()
        {
            return new AuditLogQueueSnapshot(
                _capacity,
                _deadLetterCapacity,
                Interlocked.Read(ref _enqueuedCount),
                Interlocked.Read(ref _deadLetteredCount),
                Interlocked.Read(ref _droppedDeadLetterCount));
        }
    }

    public sealed class AuditLogWriteHostedService : BackgroundService
    {
        private const int MaxBatchSize = 64;
        private readonly IServiceProvider _serviceProvider;
        private readonly AuditLogWriteQueue _queue;
        private readonly ILogger<AuditLogWriteHostedService> _logger;
        private readonly int _maxRetryAttempts;
        private readonly TimeSpan _retryDelay;

        public AuditLogWriteHostedService(
            IServiceProvider serviceProvider,
            AuditLogWriteQueue queue,
            ILogger<AuditLogWriteHostedService> logger,
            IConfiguration configuration)
        {
            _serviceProvider = serviceProvider;
            _queue = queue;
            _logger = logger;
            _maxRetryAttempts = Math.Clamp(configuration.GetValue("Security:AuditLogQueue:MaxRetryAttempts", 5), 1, 100);
            _retryDelay = TimeSpan.FromSeconds(Math.Clamp(configuration.GetValue("Security:AuditLogQueue:RetryDelaySeconds", 2), 1, 300));
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
                    DeadLetterJob(invalidJob, "MissingTenantContext", null, 0);
                    _logger.LogWarning(
                        "Audit write dead-lettered because tenant context was missing. Action={Action} Entity={Entity} EntityId={EntityId}",
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
            var attempt = 0;
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
                    attempt++;
                    var sample = jobs[0];
                    _logger.LogError(
                        ex,
                        "Audit batch write failed. TenantId={TenantId} Count={Count} FirstAction={Action} Attempt={Attempt}/{MaxAttempts}",
                        sample.TenantId,
                        jobs.Count,
                        sample.Audit.Action,
                        attempt,
                        _maxRetryAttempts);

                    if (attempt >= _maxRetryAttempts)
                    {
                        DeadLetterBatch(jobs, "PersistFailed", ex, attempt);
                        _logger.LogCritical(
                            ex,
                            "Audit batch moved to dead-letter queue. TenantId={TenantId} Count={Count} FirstAction={Action} Attempts={Attempts}",
                            sample.TenantId,
                            jobs.Count,
                            sample.Audit.Action,
                            attempt);
                        return;
                    }

                    await Task.Delay(_retryDelay, stoppingToken);
                }
            }
        }

        private void DeadLetterBatch(List<AuditLogWriteJob> jobs, string reason, Exception? exception, int attemptCount)
        {
            foreach (var job in jobs)
            {
                DeadLetterJob(job, reason, exception, attemptCount);
            }
        }

        private void DeadLetterJob(AuditLogWriteJob job, string reason, Exception? exception, int attemptCount)
        {
            if (_queue.TryEnqueueDeadLetter(new AuditLogDeadLetterJob(
                    job,
                    reason,
                    exception?.GetType().FullName,
                    exception?.Message,
                    attemptCount,
                    DateTime.UtcNow)))
            {
                return;
            }

            _logger.LogCritical(
                "Audit dead-letter queue is full. Dead-letter entry dropped. Reason={Reason} TenantId={TenantId} Action={Action} Entity={Entity} EntityId={EntityId}",
                reason,
                job.TenantId,
                job.Audit.Action,
                job.Audit.Entity,
                job.Audit.EntityId);
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
