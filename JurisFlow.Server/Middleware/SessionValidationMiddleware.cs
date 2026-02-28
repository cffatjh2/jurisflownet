using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using JurisFlow.Server.Data;
using JurisFlow.Server.Services;

namespace JurisFlow.Server.Middleware
{
    public class SessionValidationMiddleware
    {
        private readonly RequestDelegate _next;

        public SessionValidationMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext context, JurisFlowDbContext db, IConfiguration config, AuditLogger auditLogger)
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

            var session = await db.AuthSessions.FirstOrDefaultAsync(s => s.Id == sessionId);
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

            await _next(context);
        }
    }
}
