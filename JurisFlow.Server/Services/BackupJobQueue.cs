using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace JurisFlow.Server.Services
{
    public sealed class BackupJobQueue
    {
        private readonly Channel<BackupJobRequest> _channel;
        private readonly ConcurrentDictionary<string, BackupJobState> _jobs = new(StringComparer.Ordinal);
        private readonly TimeSpan _completedRetention;

        public BackupJobQueue(IConfiguration configuration)
        {
            _channel = Channel.CreateUnbounded<BackupJobRequest>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });
            _completedRetention = TimeSpan.FromHours(Math.Clamp(
                configuration.GetValue("Backup:JobStatusRetentionHours", 24),
                1,
                24 * 7));
        }

        public BackupJobSnapshot EnqueueCreate(BackupJobEnqueueContext context, bool includeUploads)
        {
            CleanupExpired();

            var request = BuildRequest(context, "create", includeUploads, fileName: null, dryRun: null);
            _jobs[request.JobId] = BackupJobState.FromRequest(request);
            _channel.Writer.TryWrite(request);
            return _jobs[request.JobId].ToSnapshot();
        }

        public BackupJobSnapshot EnqueueRestore(BackupJobEnqueueContext context, string fileName, bool includeUploads, bool dryRun)
        {
            CleanupExpired();

            var request = BuildRequest(context, "restore", includeUploads, fileName, dryRun);
            _jobs[request.JobId] = BackupJobState.FromRequest(request);
            _channel.Writer.TryWrite(request);
            return _jobs[request.JobId].ToSnapshot();
        }

        public IAsyncEnumerable<BackupJobRequest> DequeueAllAsync(CancellationToken cancellationToken)
        {
            return _channel.Reader.ReadAllAsync(cancellationToken);
        }

        public bool TryGet(string jobId, string tenantId, out BackupJobSnapshot? snapshot)
        {
            snapshot = null;
            if (!_jobs.TryGetValue(jobId, out var state))
            {
                return false;
            }

            if (!string.Equals(state.TenantId, tenantId, StringComparison.Ordinal))
            {
                return false;
            }

            snapshot = state.ToSnapshot();
            return true;
        }

        public void MarkRunning(string jobId)
        {
            if (_jobs.TryGetValue(jobId, out var state))
            {
                state.Status = "running";
                state.StartedAt = DateTime.UtcNow;
                state.CompletedAt = null;
                state.Message = "Backup job is running.";
            }
        }

        public void MarkSucceeded(string jobId, object result, string message)
        {
            if (_jobs.TryGetValue(jobId, out var state))
            {
                state.Status = "succeeded";
                state.CompletedAt = DateTime.UtcNow;
                state.Result = result;
                state.Message = message;
                CleanupExpired();
            }
        }

        public void MarkFailed(string jobId, string message)
        {
            if (_jobs.TryGetValue(jobId, out var state))
            {
                state.Status = "failed";
                state.CompletedAt = DateTime.UtcNow;
                state.Result = null;
                state.Message = message;
                CleanupExpired();
            }
        }

        private BackupJobRequest BuildRequest(BackupJobEnqueueContext context, string operation, bool includeUploads, string? fileName, bool? dryRun)
        {
            return new BackupJobRequest
            {
                JobId = Guid.NewGuid().ToString(),
                Operation = operation,
                TenantId = context.TenantId,
                TenantSlug = context.TenantSlug,
                RequestedByUserId = context.RequestedByUserId,
                RequestedByRole = context.RequestedByRole,
                CorrelationId = context.CorrelationId,
                IncludeUploads = includeUploads,
                FileName = fileName,
                DryRun = dryRun,
                RequestedAt = DateTime.UtcNow
            };
        }

        private void CleanupExpired()
        {
            var threshold = DateTime.UtcNow - _completedRetention;
            foreach (var entry in _jobs)
            {
                var completedAt = entry.Value.CompletedAt;
                if (completedAt.HasValue && completedAt.Value < threshold)
                {
                    _jobs.TryRemove(entry.Key, out _);
                }
            }
        }
    }

    public sealed class BackupJobHostedService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly BackupJobQueue _queue;
        private readonly ILogger<BackupJobHostedService> _logger;

        public BackupJobHostedService(IServiceProvider serviceProvider, BackupJobQueue queue, ILogger<BackupJobHostedService> logger)
        {
            _serviceProvider = serviceProvider;
            _queue = queue;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await foreach (var job in _queue.DequeueAllAsync(stoppingToken))
            {
                _queue.MarkRunning(job.JobId);

                using var scope = _serviceProvider.CreateScope();
                var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
                tenantContext.Set(job.TenantId, job.TenantSlug);

                var backupService = scope.ServiceProvider.GetRequiredService<BackupService>();
                var auditLogger = scope.ServiceProvider.GetRequiredService<AuditLogger>();

                try
                {
                    if (string.Equals(job.Operation, "create", StringComparison.Ordinal))
                    {
                        var result = await backupService.CreateBackupAsync(job.IncludeUploads);
                        _queue.MarkSucceeded(job.JobId, result, "Backup completed.");
                        await auditLogger.LogAsync(
                            BuildAuditContext(job),
                            "backup.create.completed",
                            "BackupJob",
                            job.JobId,
                            $"Uploads={job.IncludeUploads}; FileName={result.FileName}");
                    }
                    else
                    {
                        var result = await backupService.RestoreBackupAsync(job.FileName ?? string.Empty, job.IncludeUploads, job.DryRun ?? true);
                        _queue.MarkSucceeded(job.JobId, result, result.Message);
                        await auditLogger.LogAsync(
                            BuildAuditContext(job),
                            "backup.restore.completed",
                            "BackupJob",
                            job.JobId,
                            $"DryRun={job.DryRun}; Uploads={job.IncludeUploads}; FileName={job.FileName}");
                    }
                }
                catch (InvalidOperationException ex)
                {
                    _logger.LogWarning(ex, "Backup job failed. JobId={JobId} Operation={Operation}", job.JobId, job.Operation);
                    _queue.MarkFailed(job.JobId, "Backup job could not be completed.");
                    await TryAuditFailureAsync(auditLogger, job);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Backup job crashed. JobId={JobId} Operation={Operation}", job.JobId, job.Operation);
                    _queue.MarkFailed(job.JobId, "Backup job failed unexpectedly.");
                    await TryAuditFailureAsync(auditLogger, job);
                }
            }
        }

        private static DefaultHttpContext BuildAuditContext(BackupJobRequest job)
        {
            var context = new DefaultHttpContext();
            context.TraceIdentifier = job.CorrelationId ?? Guid.NewGuid().ToString();

            if (!string.IsNullOrWhiteSpace(job.RequestedByUserId))
            {
                var claims = new List<Claim>
                {
                    new(ClaimTypes.NameIdentifier, job.RequestedByUserId)
                };

                if (!string.IsNullOrWhiteSpace(job.RequestedByRole))
                {
                    claims.Add(new Claim(ClaimTypes.Role, job.RequestedByRole));
                }

                context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "backup-job"));
            }

            return context;
        }

        private static async Task TryAuditFailureAsync(AuditLogger auditLogger, BackupJobRequest job)
        {
            try
            {
                await auditLogger.LogAsync(
                    BuildAuditContext(job),
                    $"{(job.Operation == "create" ? "backup.create" : "backup.restore")}.failed",
                    "BackupJob",
                    job.JobId,
                    $"DryRun={job.DryRun}; Uploads={job.IncludeUploads}; FileName={job.FileName}");
            }
            catch
            {
                // Best-effort failure audit.
            }
        }
    }

    public sealed class BackupJobEnqueueContext
    {
        public string TenantId { get; init; } = string.Empty;
        public string? TenantSlug { get; init; }
        public string? RequestedByUserId { get; init; }
        public string? RequestedByRole { get; init; }
        public string? CorrelationId { get; init; }
    }

    public sealed class BackupJobRequest
    {
        public string JobId { get; init; } = string.Empty;
        public string Operation { get; init; } = string.Empty;
        public string TenantId { get; init; } = string.Empty;
        public string? TenantSlug { get; init; }
        public string? RequestedByUserId { get; init; }
        public string? RequestedByRole { get; init; }
        public string? CorrelationId { get; init; }
        public bool IncludeUploads { get; init; }
        public string? FileName { get; init; }
        public bool? DryRun { get; init; }
        public DateTime RequestedAt { get; init; }
    }

    public sealed class BackupJobSnapshot
    {
        public string JobId { get; init; } = string.Empty;
        public string Operation { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public bool IncludeUploads { get; init; }
        public string? FileName { get; init; }
        public bool? DryRun { get; init; }
        public DateTime RequestedAt { get; init; }
        public DateTime? StartedAt { get; init; }
        public DateTime? CompletedAt { get; init; }
        public string? Message { get; init; }
        public object? Result { get; init; }
    }

    internal sealed class BackupJobState
    {
        public string JobId { get; set; } = string.Empty;
        public string TenantId { get; set; } = string.Empty;
        public string Operation { get; set; } = string.Empty;
        public string Status { get; set; } = "queued";
        public bool IncludeUploads { get; set; }
        public string? FileName { get; set; }
        public bool? DryRun { get; set; }
        public DateTime RequestedAt { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public string? Message { get; set; }
        public object? Result { get; set; }

        public static BackupJobState FromRequest(BackupJobRequest request)
        {
            return new BackupJobState
            {
                JobId = request.JobId,
                TenantId = request.TenantId,
                Operation = request.Operation,
                Status = "queued",
                IncludeUploads = request.IncludeUploads,
                FileName = request.FileName,
                DryRun = request.DryRun,
                RequestedAt = request.RequestedAt,
                Message = "Backup job is queued."
            };
        }

        public BackupJobSnapshot ToSnapshot()
        {
            return new BackupJobSnapshot
            {
                JobId = JobId,
                Operation = Operation,
                Status = Status,
                IncludeUploads = IncludeUploads,
                FileName = FileName,
                DryRun = DryRun,
                RequestedAt = RequestedAt,
                StartedAt = StartedAt,
                CompletedAt = CompletedAt,
                Message = Message,
                Result = Result
            };
        }
    }
}
