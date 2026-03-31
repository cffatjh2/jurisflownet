using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using JurisFlow.Server.Services;
using Task = System.Threading.Tasks.Task;

namespace JurisFlow.Server.Middleware
{
    public class SessionValidationMiddleware
    {
        private readonly RequestDelegate _next;

        // Cache session reads for 30 seconds max — a revoked session
        // will be honored within 30s at most. This is well within the
        // existing 5-minute LastSeenAt update threshold.
        private static readonly TimeSpan SessionCacheDuration = TimeSpan.FromSeconds(30);

        public SessionValidationMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext context, JurisFlowDbContext db, IMemoryCache cache, IConfiguration config, AuditLogger auditLogger)
        {
            if (config.GetValue("Security:DisableSessionValidation", false))
            {
                await _next(context);
                return;
            }

            if (context.User?.Identity?.IsAuthenticated != true)
            {
                await _next(context);
                return;
            }

            var sessionId = context.User.FindFirst("sid")?.Value;
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                await _next(context);
                return;
            }

            var now = DateTime.UtcNow;
            var idleMinutes = config.GetValue("Security:IdleTimeoutMinutes", 60);
            var sessionTimeoutMinutes = config.GetValue("Security:SessionTimeoutMinutes", 480);
            var refreshThresholdMinutes = config.GetValue("Security:SessionRefreshThresholdMinutes", 30);

            // Try to get cached session status first to avoid DB read
            var cacheKey = $"session:{sessionId}";
            var cachedSession = cache.Get<CachedSessionInfo>(cacheKey);

            AuthSession? session = null;

            if (cachedSession != null)
            {
                // Check cached status — if revoked or expired, reject immediately
                if (cachedSession.RevokedAt != null || cachedSession.ExpiresAt <= now)
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }

                // Check idle timeout using cached LastSeenAt
                if (idleMinutes > 0 && cachedSession.LastSeenAt.AddMinutes(idleMinutes) <= now)
                {
                    // Must hit DB to perform the actual revocation
                    session = await db.AuthSessions.FirstOrDefaultAsync(s => s.Id == sessionId);
                    if (session != null && session.RevokedAt == null)
                    {
                        session.RevokedAt = now;
                        session.RevokedReason = "idle_timeout";
                        await db.SaveChangesAsync();
                        await auditLogger.LogAsync(context, "auth.session.expired", "AuthSession", session.Id, "Idle timeout");
                        cache.Remove(cacheKey); // Invalidate cache
                    }
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }

                // Session is valid from cache — handle LastSeenAt and ExpiresAt refresh
                var needsDbWrite = false;

                if (cachedSession.LastSeenAt.AddMinutes(5) <= now)
                {
                    // Only hit DB when LastSeenAt needs updating (once per 5 min)
                    session = await db.AuthSessions.FirstOrDefaultAsync(s => s.Id == sessionId);
                    if (session == null || session.RevokedAt != null || session.ExpiresAt <= now)
                    {
                        cache.Remove(cacheKey);
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        return;
                    }
                    session.LastSeenAt = now;
                    needsDbWrite = true;
                }

                if (sessionTimeoutMinutes > 0 && refreshThresholdMinutes > 0)
                {
                    var refreshWindow = TimeSpan.FromMinutes(refreshThresholdMinutes);
                    if (cachedSession.ExpiresAt - now <= refreshWindow)
                    {
                        if (session == null)
                        {
                            session = await db.AuthSessions.FirstOrDefaultAsync(s => s.Id == sessionId);
                        }
                        if (session != null && session.RevokedAt == null)
                        {
                            session.ExpiresAt = now.AddMinutes(sessionTimeoutMinutes);
                            needsDbWrite = true;
                        }
                    }
                }

                if (needsDbWrite && db.ChangeTracker.HasChanges())
                {
                    await db.SaveChangesAsync();
                    // Update cache with fresh values
                    if (session != null)
                    {
                        cache.Set(cacheKey, new CachedSessionInfo
                        {
                            LastSeenAt = session.LastSeenAt,
                            ExpiresAt = session.ExpiresAt,
                            RevokedAt = session.RevokedAt
                        }, SessionCacheDuration);
                    }
                }

                await _next(context);
                return;
            }

            // --- Cache miss: read from DB (first request or after cache expiry) ---
            session = await db.AuthSessions.FirstOrDefaultAsync(s => s.Id == sessionId);
            if (session == null || session.RevokedAt != null || session.ExpiresAt <= now)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            if (idleMinutes > 0 && session.LastSeenAt.AddMinutes(idleMinutes) <= now)
            {
                session.RevokedAt = now;
                session.RevokedReason = "idle_timeout";
                await db.SaveChangesAsync();
                await auditLogger.LogAsync(context, "auth.session.expired", "AuthSession", session.Id, "Idle timeout");
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            if (session.LastSeenAt.AddMinutes(5) <= now)
            {
                session.LastSeenAt = now;
            }

            if (sessionTimeoutMinutes > 0 && refreshThresholdMinutes > 0)
            {
                var refreshWindow = TimeSpan.FromMinutes(refreshThresholdMinutes);
                if (session.ExpiresAt - now <= refreshWindow)
                {
                    session.ExpiresAt = now.AddMinutes(sessionTimeoutMinutes);
                }
            }

            if (db.ChangeTracker.HasChanges())
            {
                await db.SaveChangesAsync();
            }

            // Cache session info for subsequent requests
            cache.Set(cacheKey, new CachedSessionInfo
            {
                LastSeenAt = session.LastSeenAt,
                ExpiresAt = session.ExpiresAt,
                RevokedAt = session.RevokedAt
            }, SessionCacheDuration);

            await _next(context);
        }

        /// <summary>
        /// Lightweight cached representation of session status.
        /// Only captures the fields needed for validation decisions.
        /// </summary>
        private sealed class CachedSessionInfo
        {
            public DateTime LastSeenAt { get; set; }
            public DateTime ExpiresAt { get; set; }
            public DateTime? RevokedAt { get; set; }
        }
    }
}
