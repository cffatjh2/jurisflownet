using System.Text.Json;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace JurisFlow.Server.Services
{
    public interface IIntegrationConnector
    {
        bool CanHandle(string providerKey);
        Task<IntegrationSyncResult> SyncAsync(IntegrationConnection connection, CancellationToken cancellationToken);
    }

    public sealed class LegacyIntegrationConnector : IIntegrationConnector
    {
        private readonly IntegrationConnectorService _integrationConnectorService;

        public LegacyIntegrationConnector(IntegrationConnectorService integrationConnectorService)
        {
            _integrationConnectorService = integrationConnectorService;
        }

        public bool CanHandle(string providerKey)
        {
            return !string.IsNullOrWhiteSpace(providerKey);
        }

        public Task<IntegrationSyncResult> SyncAsync(IntegrationConnection connection, CancellationToken cancellationToken)
        {
            return _integrationConnectorService.SyncAsync(
                connection.Id,
                connection.ProviderKey,
                connection.MetadataJson,
                cancellationToken);
        }
    }

    public sealed class IntegrationConnectorRegistry
    {
        private readonly IReadOnlyList<IIntegrationConnector> _connectors;

        public IntegrationConnectorRegistry(IEnumerable<IIntegrationConnector> connectors)
        {
            _connectors = connectors.ToList();
        }

        public IIntegrationConnector? Resolve(string providerKey)
        {
            var normalized = providerKey?.Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return null;
            }

            return _connectors.FirstOrDefault(c => c.CanHandle(normalized));
        }

        public IIntegrationActionConnector? ResolveActionConnector(string providerKey)
        {
            return Resolve(providerKey) as IIntegrationActionConnector;
        }
    }

    public sealed class IntegrationSyncRunRequest
    {
        public string Trigger { get; set; } = IntegrationRunTriggers.Scheduled;
        public string? IdempotencyKey { get; set; }
        public int? MaxAttempts { get; set; }
    }

    public sealed class IntegrationSyncRunResult
    {
        public string RunId { get; set; } = string.Empty;
        public bool Success { get; set; }
        public bool Deduplicated { get; set; }
        public bool IsDeadLetter { get; set; }
        public int SyncedCount { get; set; }
        public int AttemptCount { get; set; }
        public string Status { get; set; } = IntegrationRunStatuses.Pending;
        public string? Message { get; set; }
        public DateTime? LastSyncAt { get; set; }
    }

    public static class IntegrationRunStatuses
    {
        public const string Pending = "pending";
        public const string Running = "running";
        public const string Retrying = "retrying";
        public const string Succeeded = "succeeded";
        public const string Failed = "failed";
        public const string DeadLetter = "dead_letter";
    }

    public static class IntegrationRunTriggers
    {
        public const string Manual = "manual";
        public const string Scheduled = "scheduled";
        public const string Webhook = "webhook";
    }

    public sealed class IntegrationSyncRunner
    {
        private const int MaxIdempotencyKeyLength = 160;

        private readonly JurisFlowDbContext _context;
        private readonly IntegrationConnectorRegistry _connectorRegistry;
        private readonly IConfiguration _configuration;
        private readonly IIntegrationOperationsGuard _operationsGuard;
        private readonly ILogger<IntegrationSyncRunner> _logger;

        public IntegrationSyncRunner(
            JurisFlowDbContext context,
            IntegrationConnectorRegistry connectorRegistry,
            IConfiguration configuration,
            IIntegrationOperationsGuard operationsGuard,
            ILogger<IntegrationSyncRunner> logger)
        {
            _context = context;
            _connectorRegistry = connectorRegistry;
            _configuration = configuration;
            _operationsGuard = operationsGuard;
            _logger = logger;
        }

        public async Task<IntegrationSyncRunResult> RunAsync(
            IntegrationConnection connection,
            IntegrationSyncRunRequest request,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(connection.Id))
            {
                throw new InvalidOperationException("Integration connection id is required.");
            }

            if (string.IsNullOrWhiteSpace(connection.ProviderKey))
            {
                throw new InvalidOperationException("Integration provider key is required.");
            }

            var trigger = NormalizeTrigger(request.Trigger);
            var idempotencyKey = NormalizeIdempotencyKey(request.IdempotencyKey);
            if (string.IsNullOrWhiteSpace(idempotencyKey))
            {
                idempotencyKey = BuildGeneratedIdempotencyKey(connection.Id, trigger);
            }

            var existing = await _context.IntegrationRuns
                .AsNoTracking()
                .OrderByDescending(r => r.CreatedAt)
                .FirstOrDefaultAsync(
                    r => r.ConnectionId == connection.Id && r.IdempotencyKey == idempotencyKey,
                    cancellationToken);

            if (existing != null)
            {
                return MapRunToResult(existing, deduplicated: true, lastSyncAt: connection.LastSyncAt);
            }

            var providerRetryPolicy = _operationsGuard.ResolveRetryPolicy(connection.ProviderKey);
            var maxAttempts = Math.Clamp(
                request.MaxAttempts ?? providerRetryPolicy.MaxAttempts,
                1,
                providerRetryPolicy.MaxAttempts);

            var run = new IntegrationRun
            {
                Id = Guid.NewGuid().ToString(),
                ConnectionId = connection.Id,
                ProviderKey = connection.ProviderKey,
                Trigger = trigger,
                Status = IntegrationRunStatuses.Pending,
                IdempotencyKey = idempotencyKey,
                MaxAttempts = maxAttempts,
                CursorBefore = Truncate(connection.SyncCursor, 2048),
                DeltaTokenBefore = Truncate(connection.DeltaToken, 2048),
                CreatedAt = DateTime.UtcNow
            };

            _context.IntegrationRuns.Add(run);

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                var race = await _context.IntegrationRuns
                    .AsNoTracking()
                    .OrderByDescending(r => r.CreatedAt)
                    .FirstOrDefaultAsync(
                        r => r.ConnectionId == connection.Id && r.IdempotencyKey == idempotencyKey,
                        cancellationToken);

                if (race != null)
                {
                    return MapRunToResult(race, deduplicated: true, lastSyncAt: connection.LastSyncAt);
                }

                throw;
            }

            var gateDecision = await _operationsGuard.EvaluateForConnectionAsync(
                connection,
                trigger == IntegrationRunTriggers.Webhook ? IntegrationOperationKinds.Webhook : IntegrationOperationKinds.Sync,
                cancellationToken);

            if (!gateDecision.Allowed)
            {
                MarkRunAsBlocked(
                    run,
                    gateDecision.Code ?? "integration_operation_blocked",
                    gateDecision.Message ?? "Integration operation is blocked by operations guard.");
                connection.Status = "paused";
                connection.UpdatedAt = DateTime.UtcNow;
                EnqueueRunOutboxEvent(connection, run);
                await _context.SaveChangesAsync(cancellationToken);
                return MapRunToResult(run, deduplicated: false, lastSyncAt: connection.LastSyncAt);
            }

            if (providerRetryPolicy.MinSyncIntervalSeconds > 0 &&
                trigger != IntegrationRunTriggers.Manual &&
                connection.LastSyncAt.HasValue &&
                connection.LastSyncAt.Value > DateTime.UtcNow.AddSeconds(-providerRetryPolicy.MinSyncIntervalSeconds))
            {
                var waitSeconds = (int)Math.Ceiling(
                    providerRetryPolicy.MinSyncIntervalSeconds - (DateTime.UtcNow - connection.LastSyncAt.Value).TotalSeconds);
                MarkRunAsBlocked(
                    run,
                    "connector_local_rate_limited",
                    $"Sync is rate-limited for provider '{connection.ProviderKey}'. Retry after {Math.Max(1, waitSeconds)} seconds.");
                EnqueueRunOutboxEvent(connection, run);
                await _context.SaveChangesAsync(cancellationToken);
                return MapRunToResult(run, deduplicated: false, lastSyncAt: connection.LastSyncAt);
            }

            var connector = _connectorRegistry.Resolve(connection.ProviderKey);
            if (connector == null)
            {
                MarkRunAsDeadLetter(
                    run,
                    "provider_not_registered",
                    $"No connector is registered for provider '{connection.ProviderKey}'.");
                connection.Status = "error";
                connection.UpdatedAt = DateTime.UtcNow;
                EnqueueRunOutboxEvent(connection, run);
                await _context.SaveChangesAsync(cancellationToken);
                return MapRunToResult(run, deduplicated: false, lastSyncAt: connection.LastSyncAt);
            }

            IntegrationSyncResult? lastResult = null;
            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                run.AttemptCount = attempt;
                run.Status = attempt == 1 ? IntegrationRunStatuses.Running : IntegrationRunStatuses.Retrying;
                run.StartedAt ??= DateTime.UtcNow;
                await _context.SaveChangesAsync(cancellationToken);

                try
                {
                    lastResult = await connector.SyncAsync(connection, cancellationToken);
                }
                catch (Exception ex)
                {
                    var retryable = ex is HttpRequestException or TaskCanceledException or TimeoutException or IntegrationProviderRateLimitException;
                    var retryAfter = (ex as IntegrationProviderRateLimitException)?.RetryAfter;
                    _logger.LogError(
                        ex,
                        "Connector execution failed. ConnectionId={ConnectionId} ProviderKey={ProviderKey}",
                        connection.Id,
                        connection.ProviderKey);

                    lastResult = new IntegrationSyncResult
                    {
                        Success = false,
                        Retryable = retryable,
                        ErrorCode = ex is IntegrationProviderRateLimitException
                            ? "provider_rate_limited"
                            : (retryable ? "connector_transient_error" : "connector_terminal_error"),
                        ErrorMessage = ex.Message
                    };

                    if (retryAfter.HasValue)
                    {
                        lastResult.ErrorMessage = $"{lastResult.ErrorMessage} (retry_after={Math.Ceiling(retryAfter.Value.TotalSeconds)}s)";
                    }
                }

                if (lastResult.Success)
                {
                    var completedAt = DateTime.UtcNow;
                    connection.MetadataJson = lastResult.MetadataJson;
                    connection.Status = "connected";
                    connection.LastSyncAt = completedAt;
                    connection.SyncCursor = Truncate(lastResult.NextCursor ?? connection.SyncCursor ?? completedAt.ToString("O"), 2048);
                    connection.DeltaToken = Truncate(lastResult.NextDeltaToken ?? connection.DeltaToken, 2048);
                    connection.UpdatedAt = completedAt;

                    run.Status = IntegrationRunStatuses.Succeeded;
                    run.CompletedAt = completedAt;
                    run.IsDeadLetter = false;
                    run.ErrorCode = null;
                    run.ErrorMessage = null;
                    run.CursorAfter = Truncate(connection.SyncCursor, 2048);
                    run.DeltaTokenAfter = Truncate(connection.DeltaToken, 2048);
                    run.ResultJson = BuildResultJson(lastResult);
                    EnqueueRunOutboxEvent(connection, run);

                    await _context.SaveChangesAsync(cancellationToken);
                    return MapRunToResult(run, deduplicated: false, lastSyncAt: connection.LastSyncAt);
                }

                connection.MetadataJson = lastResult.MetadataJson;
                connection.Status = "error";
                connection.UpdatedAt = DateTime.UtcNow;
                run.ErrorCode = Truncate(lastResult.ErrorCode, 64);
                run.ErrorMessage = Truncate(lastResult.ErrorMessage ?? "Sync failed.", 2048);

                var finalAttempt = attempt >= maxAttempts;
                if (!lastResult.Retryable || finalAttempt)
                {
                    MarkRunAsDeadLetter(
                        run,
                        run.ErrorCode ?? "sync_failed",
                        run.ErrorMessage ?? "Sync failed.");
                    run.ResultJson = BuildResultJson(lastResult);
                    run.CursorAfter = Truncate(connection.SyncCursor, 2048);
                    run.DeltaTokenAfter = Truncate(connection.DeltaToken, 2048);
                    EnqueueRunOutboxEvent(connection, run);

                    await _context.SaveChangesAsync(cancellationToken);
                    return MapRunToResult(run, deduplicated: false, lastSyncAt: connection.LastSyncAt);
                }

                await _context.SaveChangesAsync(cancellationToken);
                TimeSpan? providerSuggestedRetryAfter = null;
                if (!string.IsNullOrWhiteSpace(lastResult.ErrorMessage))
                {
                    providerSuggestedRetryAfter = TryParseRetryAfterHint(lastResult.ErrorMessage);
                }
                var retryDelay = _operationsGuard.ComputeRetryDelay(
                    connection.ProviderKey,
                    attempt,
                    providerSuggestedRetryAfter);

                _logger.LogWarning(
                    "Integration sync retry scheduled. ConnectionId={ConnectionId} ProviderKey={ProviderKey} Attempt={Attempt} DelaySeconds={DelaySeconds}",
                    connection.Id,
                    connection.ProviderKey,
                    attempt,
                    retryDelay.TotalSeconds);

                await Task.Delay(retryDelay, cancellationToken);
            }

            run.Status = IntegrationRunStatuses.Failed;
            run.IsDeadLetter = true;
            run.CompletedAt = DateTime.UtcNow;
            run.ErrorCode = Truncate(lastResult?.ErrorCode ?? "sync_failed", 64);
            run.ErrorMessage = Truncate(lastResult?.ErrorMessage ?? "Sync failed.", 2048);
            run.ResultJson = BuildResultJson(lastResult);
            EnqueueRunOutboxEvent(connection, run);
            await _context.SaveChangesAsync(cancellationToken);

            return MapRunToResult(run, deduplicated: false, lastSyncAt: connection.LastSyncAt);
        }

        private static string BuildGeneratedIdempotencyKey(string connectionId, string trigger)
        {
            return $"{trigger}:{connectionId}:{Guid.NewGuid():N}";
        }

        private static string NormalizeTrigger(string trigger)
        {
            var normalized = trigger?.Trim().ToLowerInvariant();
            return normalized switch
            {
                IntegrationRunTriggers.Manual => IntegrationRunTriggers.Manual,
                IntegrationRunTriggers.Webhook => IntegrationRunTriggers.Webhook,
                _ => IntegrationRunTriggers.Scheduled
            };
        }

        private static string NormalizeIdempotencyKey(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var normalized = value.Trim();
            if (normalized.Length <= MaxIdempotencyKeyLength)
            {
                return normalized;
            }

            return normalized[..MaxIdempotencyKeyLength];
        }

        private static string? Truncate(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return value.Length <= maxLength ? value : value[..maxLength];
        }

        private static void MarkRunAsDeadLetter(IntegrationRun run, string errorCode, string errorMessage)
        {
            run.Status = IntegrationRunStatuses.DeadLetter;
            run.IsDeadLetter = true;
            run.CompletedAt = DateTime.UtcNow;
            run.ErrorCode = Truncate(errorCode, 64);
            run.ErrorMessage = Truncate(errorMessage, 2048);
        }

        private static void MarkRunAsBlocked(IntegrationRun run, string errorCode, string errorMessage)
        {
            run.Status = IntegrationRunStatuses.Failed;
            run.IsDeadLetter = false;
            run.CompletedAt = DateTime.UtcNow;
            run.ErrorCode = Truncate(errorCode, 64);
            run.ErrorMessage = Truncate(errorMessage, 2048);
        }

        private void EnqueueRunOutboxEvent(IntegrationConnection connection, IntegrationRun run)
        {
            var idempotencyKey = $"run:{run.Id}:{run.Status}";
            var exists = _context.IntegrationOutboxEvents.Local.Any(e =>
                string.Equals(e.IdempotencyKey, idempotencyKey, StringComparison.Ordinal));
            if (exists)
            {
                return;
            }

            _context.IntegrationOutboxEvents.Add(new IntegrationOutboxEvent
            {
                Id = Guid.NewGuid().ToString(),
                ConnectionId = connection.Id,
                RunId = run.Id,
                ProviderKey = connection.ProviderKey,
                EventType = "integration.run.completed",
                EntityType = nameof(IntegrationRun),
                EntityId = run.Id,
                IdempotencyKey = idempotencyKey,
                CorrelationId = $"run:{run.Id}",
                Status = IntegrationEventStatuses.Pending,
                PayloadJson = IntegrationCanonicalContract.BuildRunOutboxPayloadJson(connection, run),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        private static string? BuildResultJson(IntegrationSyncResult? result)
        {
            if (result == null)
            {
                return null;
            }

            return JsonSerializer.Serialize(new
            {
                result.Success,
                result.SyncedCount,
                result.Message,
                result.ErrorMessage,
                result.ErrorCode,
                result.Retryable
            });
        }

        private static IntegrationSyncRunResult MapRunToResult(IntegrationRun run, bool deduplicated, DateTime? lastSyncAt)
        {
            var success = string.Equals(run.Status, IntegrationRunStatuses.Succeeded, StringComparison.OrdinalIgnoreCase);
            return new IntegrationSyncRunResult
            {
                RunId = run.Id,
                Success = success,
                Deduplicated = deduplicated,
                IsDeadLetter = run.IsDeadLetter,
                SyncedCount = TryReadSyncedCount(run.ResultJson),
                AttemptCount = run.AttemptCount,
                Status = run.Status,
                Message = success ? (TryReadMessage(run.ResultJson) ?? "Sync completed.") : run.ErrorMessage,
                LastSyncAt = lastSyncAt
            };
        }

        private static int TryReadSyncedCount(string? resultJson)
        {
            if (string.IsNullOrWhiteSpace(resultJson))
            {
                return 0;
            }

            try
            {
                using var doc = JsonDocument.Parse(resultJson);
                if (doc.RootElement.TryGetProperty("SyncedCount", out var countElement) &&
                    countElement.TryGetInt32(out var count))
                {
                    return count;
                }
            }
            catch
            {
                return 0;
            }

            return 0;
        }

        private static string? TryReadMessage(string? resultJson)
        {
            if (string.IsNullOrWhiteSpace(resultJson))
            {
                return null;
            }

            try
            {
                using var doc = JsonDocument.Parse(resultJson);
                if (doc.RootElement.TryGetProperty("Message", out var messageElement) &&
                    messageElement.ValueKind == JsonValueKind.String)
                {
                    return messageElement.GetString();
                }
            }
            catch
            {
                return null;
            }

            return null;
        }

        private static TimeSpan? TryParseRetryAfterHint(string errorMessage)
        {
            if (string.IsNullOrWhiteSpace(errorMessage))
            {
                return null;
            }

            const string marker = "retry_after=";
            var index = errorMessage.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return null;
            }

            var suffix = errorMessage[(index + marker.Length)..];
            var digits = new string(suffix.TakeWhile(char.IsDigit).ToArray());
            if (!int.TryParse(digits, out var seconds) || seconds <= 0)
            {
                return null;
            }

            return TimeSpan.FromSeconds(Math.Min(seconds, 1800));
        }
    }
}
