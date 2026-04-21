using System.Text.Json;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using Microsoft.EntityFrameworkCore;
using SystemTask = System.Threading.Tasks.Task;

namespace JurisFlow.Server.Services
{
    public sealed class TaskDomainOutboxHostedService : BackgroundService
    {
        private const string ProviderKey = "task_domain";
        private const int BatchSize = 25;
        private const int MaxAttempts = 5;
        private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

        private readonly IServiceProvider _serviceProvider;
        private readonly AuditLogWriteQueue _auditQueue;
        private readonly OutcomeFeePlannerTriggerQueue _workflowQueue;
        private readonly ILogger<TaskDomainOutboxHostedService> _logger;

        public TaskDomainOutboxHostedService(
            IServiceProvider serviceProvider,
            AuditLogWriteQueue auditQueue,
            OutcomeFeePlannerTriggerQueue workflowQueue,
            ILogger<TaskDomainOutboxHostedService> logger)
        {
            _serviceProvider = serviceProvider;
            _auditQueue = auditQueue;
            _workflowQueue = workflowQueue;
            _logger = logger;
        }

        protected override async SystemTask ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var pending = await LoadPendingEventsAsync(stoppingToken);
                    if (pending.Count == 0)
                    {
                        await SystemTask.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                        continue;
                    }

                    foreach (var pendingEvent in pending)
                    {
                        await ProcessEventAsync(pendingEvent, stoppingToken);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Task domain outbox processor failed.");
                    await SystemTask.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
            }
        }

        private async System.Threading.Tasks.Task<List<PendingTaskOutboxEvent>> LoadPendingEventsAsync(CancellationToken cancellationToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();

            return await context.IntegrationOutboxEvents
                .IgnoreQueryFilters()
                .Where(e =>
                    e.ProviderKey == ProviderKey &&
                    !e.DeadLettered &&
                    (e.Status == "pending" || (e.Status == "failed" && (e.NextAttemptAt == null || e.NextAttemptAt <= DateTime.UtcNow))))
                .OrderBy(e => e.CreatedAt)
                .Take(BatchSize)
                .Select(e => new PendingTaskOutboxEvent
                {
                    EventId = e.Id,
                    TenantId = EF.Property<string>(e, "TenantId")
                })
                .ToListAsync(cancellationToken);
        }

        private async SystemTask ProcessEventAsync(PendingTaskOutboxEvent pendingEvent, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(pendingEvent.TenantId))
            {
                _logger.LogWarning("Skipping task domain outbox event {EventId} because tenant context was empty.", pendingEvent.EventId);
                return;
            }

            using var scope = _serviceProvider.CreateScope();
            var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
            tenantContext.Set(pendingEvent.TenantId);

            var context = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
            var outboxEvent = await context.IntegrationOutboxEvents.FirstOrDefaultAsync(e => e.Id == pendingEvent.EventId, cancellationToken);
            if (outboxEvent == null)
            {
                return;
            }

            try
            {
                var payload = JsonSerializer.Deserialize<TaskDomainOutboxPayload>(outboxEvent.PayloadJson ?? string.Empty, SerializerOptions)
                    ?? throw new InvalidOperationException("Task outbox payload was empty.");

                if (payload.Audit != null)
                {
                    var audit = new AuditLog
                    {
                        UserId = payload.Audit.UserId,
                        ClientId = payload.Audit.ClientId,
                        TenantId = pendingEvent.TenantId,
                        Role = payload.Audit.Role,
                        Action = payload.Audit.Action,
                        Entity = payload.Audit.Entity,
                        EntityId = payload.Audit.EntityId,
                        Details = AppendOutboxMarker(payload.Audit.Details, outboxEvent.Id),
                        IpAddress = payload.Audit.IpAddress,
                        UserAgent = payload.Audit.UserAgent,
                        CreatedAt = payload.Audit.CreatedAtUtc
                    };

                    var tenantSlug = string.IsNullOrWhiteSpace(payload.TenantSlug) ? string.Empty : payload.TenantSlug;
                    if (!_auditQueue.TryEnqueue(new AuditLogWriteJob(pendingEvent.TenantId, tenantSlug, audit)))
                    {
                        await _auditQueue.WriteAsync(new AuditLogWriteJob(pendingEvent.TenantId, tenantSlug, audit), cancellationToken);
                    }
                }

                if (payload.TransparencyTrigger != null)
                {
                    var actorUserId = string.IsNullOrWhiteSpace(payload.ActorUserId) ? "system" : payload.ActorUserId;
                    var tenantSlug = string.IsNullOrWhiteSpace(payload.TenantSlug) ? string.Empty : payload.TenantSlug;
                    var accepted = _workflowQueue.Enqueue(new OutcomeFeePlannerTriggerJob(
                        pendingEvent.TenantId,
                        tenantSlug,
                        actorUserId,
                        PlannerRequest: null,
                        TransparencyRequest: payload.TransparencyTrigger));

                    if (!accepted)
                    {
                        throw new InvalidOperationException("Workflow trigger queue rejected the task domain outbox event.");
                    }
                }

                outboxEvent.Status = "completed";
                outboxEvent.DispatchedAt = DateTime.UtcNow;
                outboxEvent.UpdatedAt = DateTime.UtcNow;
                outboxEvent.NextAttemptAt = null;
                outboxEvent.ErrorCode = null;
                outboxEvent.ErrorMessage = null;

                await context.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                outboxEvent.AttemptCount += 1;
                outboxEvent.Status = outboxEvent.AttemptCount >= MaxAttempts ? "dead_letter" : "failed";
                outboxEvent.DeadLettered = outboxEvent.AttemptCount >= MaxAttempts;
                outboxEvent.ErrorCode = "task_domain_outbox_failed";
                outboxEvent.ErrorMessage = Truncate(ex.Message, 2048);
                outboxEvent.NextAttemptAt = outboxEvent.DeadLettered
                    ? null
                    : DateTime.UtcNow.AddSeconds(Math.Min(300, 30 * outboxEvent.AttemptCount));
                outboxEvent.UpdatedAt = DateTime.UtcNow;

                await context.SaveChangesAsync(cancellationToken);
                _logger.LogWarning(ex, "Task domain outbox event {EventId} failed on attempt {AttemptCount}.", outboxEvent.Id, outboxEvent.AttemptCount);
            }
        }

        private static string? AppendOutboxMarker(string? details, string outboxEventId)
        {
            var marker = $"[taskOutboxEventId={outboxEventId}]";
            if (string.IsNullOrWhiteSpace(details))
            {
                return marker;
            }

            return details.Contains("taskOutboxEventId=", StringComparison.OrdinalIgnoreCase)
                ? details
                : $"{details.Trim()} {marker}";
        }

        private static string? Truncate(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            var trimmed = value.Trim();
            return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
        }

        private sealed class PendingTaskOutboxEvent
        {
            public string EventId { get; init; } = string.Empty;
            public string? TenantId { get; init; }
        }
    }
}
