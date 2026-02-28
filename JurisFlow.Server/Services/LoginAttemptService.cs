using Microsoft.Extensions.Caching.Memory;

namespace JurisFlow.Server.Services
{
    public record LoginThrottleStatus(bool IsLockedOut, int RemainingAttempts, TimeSpan? RetryAfter);

    public class LoginAttemptService
    {
        private readonly IMemoryCache _cache;
        private readonly int _accountFailureLimit;
        private readonly int _ipFailureLimit;
        private readonly TimeSpan _failureWindow;
        private readonly TimeSpan _lockoutDuration;
        private readonly ILogger<LoginAttemptService> _logger;

        public LoginAttemptService(IMemoryCache cache, IConfiguration configuration, ILogger<LoginAttemptService> logger)
        {
            _cache = cache;
            _logger = logger;

            _accountFailureLimit = Math.Clamp(configuration.GetValue("Security:LoginFailureLimitPerAccount", 5), 2, 20);
            _ipFailureLimit = Math.Clamp(configuration.GetValue("Security:LoginFailureLimitPerIp", 20), 5, 200);

            var windowMinutes = Math.Clamp(configuration.GetValue("Security:LoginFailureWindowMinutes", 15), 1, 240);
            var lockoutMinutes = Math.Clamp(configuration.GetValue("Security:LoginLockoutMinutes", 15), 1, 240);
            _failureWindow = TimeSpan.FromMinutes(windowMinutes);
            _lockoutDuration = TimeSpan.FromMinutes(lockoutMinutes);
        }

        public LoginThrottleStatus GetStatus(string tenantId, string subject, string ipAddress)
        {
            var now = DateTimeOffset.UtcNow;

            var accountKey = BuildKey(tenantId, "account", NormalizeSubject(subject));
            var ipKey = BuildKey(tenantId, "ip", NormalizeIp(ipAddress));

            var accountStatus = GetBucketStatus(accountKey, _accountFailureLimit, now);
            var ipStatus = GetBucketStatus(ipKey, _ipFailureLimit, now);

            return MergeStatus(accountStatus, ipStatus);
        }

        public LoginThrottleStatus RegisterFailure(string tenantId, string subject, string ipAddress)
        {
            var now = DateTimeOffset.UtcNow;

            var accountKey = BuildKey(tenantId, "account", NormalizeSubject(subject));
            var ipKey = BuildKey(tenantId, "ip", NormalizeIp(ipAddress));

            var accountStatus = RegisterBucketFailure(accountKey, _accountFailureLimit, now);
            var ipStatus = RegisterBucketFailure(ipKey, _ipFailureLimit, now);

            var merged = MergeStatus(accountStatus, ipStatus);
            if (merged.IsLockedOut)
            {
                _logger.LogWarning(
                    "Login lockout triggered for tenant {TenantId}. subject={Subject} ip={IpAddress}",
                    tenantId,
                    NormalizeSubject(subject),
                    NormalizeIp(ipAddress));
            }

            return merged;
        }

        public void RegisterSuccess(string tenantId, string subject, string ipAddress)
        {
            var accountKey = BuildKey(tenantId, "account", NormalizeSubject(subject));
            var ipKey = BuildKey(tenantId, "ip", NormalizeIp(ipAddress));

            _cache.Remove(accountKey);
            _cache.Remove(ipKey);
        }

        private LoginThrottleStatus RegisterBucketFailure(string key, int maxFailures, DateTimeOffset now)
        {
            var state = GetOrCreateState(key, now);
            lock (state.SyncRoot)
            {
                ResetIfWindowExpired(state, now);

                if (state.LockedUntil.HasValue && state.LockedUntil.Value > now)
                {
                    var retry = state.LockedUntil.Value - now;
                    return new LoginThrottleStatus(true, 0, retry);
                }

                state.FailureCount += 1;
                if (state.FailureCount >= maxFailures)
                {
                    state.LockedUntil = now.Add(_lockoutDuration);
                    var retry = state.LockedUntil.Value - now;
                    _cache.Set(key, state, BuildCacheOptions(now));
                    return new LoginThrottleStatus(true, 0, retry);
                }

                var remaining = Math.Max(0, maxFailures - state.FailureCount);
                _cache.Set(key, state, BuildCacheOptions(now));
                return new LoginThrottleStatus(false, remaining, null);
            }
        }

        private LoginThrottleStatus GetBucketStatus(string key, int maxFailures, DateTimeOffset now)
        {
            if (!_cache.TryGetValue<LoginFailureState>(key, out var state) || state == null)
            {
                return new LoginThrottleStatus(false, maxFailures, null);
            }

            lock (state.SyncRoot)
            {
                ResetIfWindowExpired(state, now);

                if (state.LockedUntil.HasValue && state.LockedUntil.Value > now)
                {
                    var retry = state.LockedUntil.Value - now;
                    _cache.Set(key, state, BuildCacheOptions(now));
                    return new LoginThrottleStatus(true, 0, retry);
                }

                var remaining = Math.Max(0, maxFailures - state.FailureCount);
                _cache.Set(key, state, BuildCacheOptions(now));
                return new LoginThrottleStatus(false, remaining, null);
            }
        }

        private LoginFailureState GetOrCreateState(string key, DateTimeOffset now)
        {
            if (_cache.TryGetValue<LoginFailureState>(key, out var existing) && existing != null)
            {
                return existing;
            }

            var created = new LoginFailureState
            {
                WindowStartedAt = now
            };

            _cache.Set(key, created, BuildCacheOptions(now));
            return created;
        }

        private void ResetIfWindowExpired(LoginFailureState state, DateTimeOffset now)
        {
            if (state.LockedUntil.HasValue && state.LockedUntil.Value <= now)
            {
                state.LockedUntil = null;
                state.FailureCount = 0;
                state.WindowStartedAt = now;
                return;
            }

            if (now - state.WindowStartedAt >= _failureWindow)
            {
                state.FailureCount = 0;
                state.LockedUntil = null;
                state.WindowStartedAt = now;
            }
        }

        private MemoryCacheEntryOptions BuildCacheOptions(DateTimeOffset now)
        {
            var ttl = _failureWindow + _lockoutDuration + TimeSpan.FromMinutes(5);
            return new MemoryCacheEntryOptions
            {
                AbsoluteExpiration = now.Add(ttl)
            };
        }

        private static LoginThrottleStatus MergeStatus(LoginThrottleStatus accountStatus, LoginThrottleStatus ipStatus)
        {
            var isLockedOut = accountStatus.IsLockedOut || ipStatus.IsLockedOut;
            var remainingAttempts = Math.Min(accountStatus.RemainingAttempts, ipStatus.RemainingAttempts);

            TimeSpan? retryAfter = null;
            if (accountStatus.RetryAfter.HasValue || ipStatus.RetryAfter.HasValue)
            {
                var accountSeconds = accountStatus.RetryAfter?.TotalSeconds ?? 0;
                var ipSeconds = ipStatus.RetryAfter?.TotalSeconds ?? 0;
                retryAfter = TimeSpan.FromSeconds(Math.Max(accountSeconds, ipSeconds));
            }

            return new LoginThrottleStatus(isLockedOut, remainingAttempts, retryAfter);
        }

        private static string BuildKey(string tenantId, string bucket, string identifier)
        {
            return $"login-throttle:{tenantId}:{bucket}:{identifier}";
        }

        private static string NormalizeSubject(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim().ToLowerInvariant();
        }

        private static string NormalizeIp(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim();
        }

        private sealed class LoginFailureState
        {
            public object SyncRoot { get; } = new();
            public int FailureCount { get; set; }
            public DateTimeOffset WindowStartedAt { get; set; }
            public DateTimeOffset? LockedUntil { get; set; }
        }
    }
}
