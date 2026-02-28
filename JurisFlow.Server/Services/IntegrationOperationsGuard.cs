using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace JurisFlow.Server.Services
{
    public static class IntegrationOperationKinds
    {
        public const string Connect = "connect";
        public const string Validate = "validate";
        public const string Sync = "sync";
        public const string Webhook = "webhook";
        public const string Replay = "replay";
        public const string CanonicalAction = "canonical_action";
    }

    public sealed class IntegrationProviderRateLimitException : Exception
    {
        public IntegrationProviderRateLimitException(string message, TimeSpan? retryAfter = null, Exception? innerException = null)
            : base(message, innerException)
        {
            RetryAfter = retryAfter;
        }

        public TimeSpan? RetryAfter { get; }
    }

    public sealed class IntegrationProviderRetryPolicy
    {
        public int MaxAttempts { get; init; } = 3;
        public int BaseDelaySeconds { get; init; } = 5;
        public int MaxDelaySeconds { get; init; } = 120;
        public double BackoffMultiplier { get; init; } = 2.0;
        public int JitterPercent { get; init; } = 20;
        public int MinSyncIntervalSeconds { get; init; }
    }

    public sealed class IntegrationOperationGuardDecision
    {
        public bool Allowed { get; init; } = true;
        public string? Code { get; init; }
        public string? Message { get; init; }
        public string? TenantId { get; init; }
    }

    public interface IIntegrationOperationsGuard
    {
        Task<IntegrationOperationGuardDecision> EvaluateForConnectionAsync(
            IntegrationConnection connection,
            string operation,
            CancellationToken cancellationToken);

        Task<IntegrationOperationGuardDecision> EvaluateForCurrentTenantAsync(
            string? providerKey,
            string operation,
            CancellationToken cancellationToken);

        IntegrationProviderRetryPolicy ResolveRetryPolicy(string providerKey);

        TimeSpan ComputeRetryDelay(
            string providerKey,
            int attempt,
            TimeSpan? providerSuggestedRetryAfter = null);
    }

    public sealed class IntegrationOperationsGuard : IIntegrationOperationsGuard
    {
        private readonly JurisFlowDbContext _context;
        private readonly TenantContext _tenantContext;
        private readonly IConfiguration _configuration;
        private readonly Random _random = new();

        public IntegrationOperationsGuard(
            JurisFlowDbContext context,
            TenantContext tenantContext,
            IConfiguration configuration)
        {
            _context = context;
            _tenantContext = tenantContext;
            _configuration = configuration;
        }

        public async Task<IntegrationOperationGuardDecision> EvaluateForConnectionAsync(
            IntegrationConnection connection,
            string operation,
            CancellationToken cancellationToken)
        {
            var tenantId = await ResolveTenantIdForConnectionAsync(connection, cancellationToken);
            if (!string.IsNullOrWhiteSpace(connection.ProviderKey) &&
                RequiresActiveSync(connection, operation) &&
                !connection.SyncEnabled)
            {
                return new IntegrationOperationGuardDecision
                {
                    Allowed = false,
                    Code = "connection_sync_disabled",
                    Message = $"Integration connection '{connection.ProviderKey}' is disabled for sync operations.",
                    TenantId = tenantId
                };
            }

            return BuildKillSwitchDecision(tenantId, connection.ProviderKey, operation);
        }

        public Task<IntegrationOperationGuardDecision> EvaluateForCurrentTenantAsync(
            string? providerKey,
            string operation,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var decision = BuildKillSwitchDecision(_tenantContext.TenantId, providerKey, operation);
            return Task.FromResult(decision);
        }

        public IntegrationProviderRetryPolicy ResolveRetryPolicy(string providerKey)
        {
            var normalizedProviderKey = (providerKey ?? string.Empty).Trim().ToLowerInvariant();

            var defaults = _configuration.GetSection("Integrations:Operations:RetryPolicy");
            var provider = string.IsNullOrWhiteSpace(normalizedProviderKey)
                ? null
                : _configuration.GetSection($"Integrations:Operations:RetryPolicy:Providers:{normalizedProviderKey}");

            return new IntegrationProviderRetryPolicy
            {
                MaxAttempts = Math.Clamp(
                    provider?.GetValue<int?>("MaxAttempts") ?? defaults.GetValue("MaxAttempts", 3),
                    1,
                    10),
                BaseDelaySeconds = Math.Clamp(
                    provider?.GetValue<int?>("BaseDelaySeconds") ?? defaults.GetValue("BaseDelaySeconds", 5),
                    1,
                    300),
                MaxDelaySeconds = Math.Clamp(
                    provider?.GetValue<int?>("MaxDelaySeconds") ?? defaults.GetValue("MaxDelaySeconds", 120),
                    1,
                    1800),
                BackoffMultiplier = Math.Clamp(
                    provider?.GetValue<double?>("BackoffMultiplier") ?? defaults.GetValue("BackoffMultiplier", 2.0),
                    1.0,
                    10.0),
                JitterPercent = Math.Clamp(
                    provider?.GetValue<int?>("JitterPercent") ?? defaults.GetValue("JitterPercent", 20),
                    0,
                    100),
                MinSyncIntervalSeconds = Math.Clamp(
                    provider?.GetValue<int?>("MinSyncIntervalSeconds") ?? defaults.GetValue("MinSyncIntervalSeconds", 0),
                    0,
                    3600)
            };
        }

        public TimeSpan ComputeRetryDelay(
            string providerKey,
            int attempt,
            TimeSpan? providerSuggestedRetryAfter = null)
        {
            var policy = ResolveRetryPolicy(providerKey);
            var boundedAttempt = Math.Max(1, attempt);
            var baseDelay = TimeSpan.FromSeconds(policy.BaseDelaySeconds);

            double multiplier = 1d;
            for (var i = 1; i < boundedAttempt; i++)
            {
                multiplier *= policy.BackoffMultiplier;
            }

            var computedSeconds = Math.Min(
                policy.MaxDelaySeconds,
                Math.Max(1d, baseDelay.TotalSeconds * multiplier));

            var jitterFactor = policy.JitterPercent <= 0
                ? 0d
                : ((_random.NextDouble() * 2d) - 1d) * (policy.JitterPercent / 100d);

            var jitteredSeconds = Math.Max(1d, computedSeconds * (1d + jitterFactor));
            var delay = TimeSpan.FromSeconds(jitteredSeconds);

            if (providerSuggestedRetryAfter.HasValue && providerSuggestedRetryAfter.Value > delay)
            {
                delay = providerSuggestedRetryAfter.Value;
            }

            var maxDelay = TimeSpan.FromSeconds(policy.MaxDelaySeconds);
            return delay > maxDelay ? maxDelay : delay;
        }

        private async Task<string?> ResolveTenantIdForConnectionAsync(IntegrationConnection connection, CancellationToken cancellationToken)
        {
            if (!string.IsNullOrWhiteSpace(_tenantContext.TenantId))
            {
                return _tenantContext.TenantId;
            }

            try
            {
                var trackedEntry = _context.Entry(connection);
                var tenantProperty = trackedEntry.Metadata.FindProperty("TenantId");
                if (tenantProperty != null)
                {
                    var trackedTenantId = trackedEntry.Property("TenantId").CurrentValue?.ToString();
                    if (!string.IsNullOrWhiteSpace(trackedTenantId))
                    {
                        return trackedTenantId;
                    }
                }
            }
            catch
            {
                // Fall back to direct query.
            }

            if (string.IsNullOrWhiteSpace(connection.Id))
            {
                return null;
            }

            return await _context.IntegrationConnections
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(c => c.Id == connection.Id)
                .Select(c => EF.Property<string>(c, "TenantId"))
                .FirstOrDefaultAsync(cancellationToken);
        }

        private IntegrationOperationGuardDecision BuildKillSwitchDecision(string? tenantId, string? providerKey, string operation)
        {
            var normalizedProviderKey = providerKey?.Trim().ToLowerInvariant();
            var killSwitchSection = _configuration.GetSection("Integrations:Operations:KillSwitch");

            if (killSwitchSection.GetValue("GlobalEnabled", false))
            {
                return Deny("global_kill_switch", killSwitchSection["GlobalReason"] ?? "Integration operations are temporarily disabled.", tenantId);
            }

            var disabledProviders = new HashSet<string>(
                killSwitchSection.GetSection("DisabledProviders").Get<string[]>() ?? Array.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(normalizedProviderKey) && disabledProviders.Contains(normalizedProviderKey))
            {
                return Deny("provider_kill_switch", $"Integration provider '{normalizedProviderKey}' is disabled.", tenantId);
            }

            if (!string.IsNullOrWhiteSpace(tenantId))
            {
                var disabledTenants = new HashSet<string>(
                    killSwitchSection.GetSection("DisabledTenants").Get<string[]>() ?? Array.Empty<string>(),
                    StringComparer.Ordinal);
                if (disabledTenants.Contains(tenantId))
                {
                    return Deny("tenant_kill_switch", $"Integration operations are disabled for tenant '{tenantId}'.", tenantId);
                }

                var tenantSection = killSwitchSection.GetSection($"TenantOverrides:{tenantId}");
                var tenantEnabled = tenantSection.GetValue<bool?>("Enabled");
                if (tenantEnabled == true)
                {
                    var untilUtc = tenantSection.GetValue<DateTime?>("UntilUtc");
                    if (!untilUtc.HasValue || untilUtc.Value > DateTime.UtcNow)
                    {
                        var reason = tenantSection["Reason"] ?? $"Integration operations are disabled for tenant '{tenantId}'.";
                        return Deny("tenant_kill_switch", reason, tenantId);
                    }
                }

                if (!string.IsNullOrWhiteSpace(normalizedProviderKey))
                {
                    var tenantProviders = new HashSet<string>(
                        tenantSection.GetSection("DisabledProviders").Get<string[]>() ?? Array.Empty<string>(),
                        StringComparer.OrdinalIgnoreCase);
                    if (tenantProviders.Contains(normalizedProviderKey))
                    {
                        return Deny("tenant_provider_kill_switch", $"Provider '{normalizedProviderKey}' is disabled for tenant '{tenantId}'.", tenantId);
                    }
                }
            }

            _ = operation; // reserved for operation-specific rules.
            return new IntegrationOperationGuardDecision
            {
                Allowed = true,
                TenantId = tenantId
            };
        }

        private static bool RequiresActiveSync(IntegrationConnection connection, string operation)
        {
            if (connection == null)
            {
                return false;
            }

            var normalized = (operation ?? string.Empty).Trim().ToLowerInvariant();
            return normalized is IntegrationOperationKinds.Sync
                or IntegrationOperationKinds.Webhook
                or IntegrationOperationKinds.Replay
                or IntegrationOperationKinds.CanonicalAction;
        }

        private static IntegrationOperationGuardDecision Deny(string code, string message, string? tenantId)
        {
            return new IntegrationOperationGuardDecision
            {
                Allowed = false,
                Code = code,
                Message = message,
                TenantId = tenantId
            };
        }
    }
}
