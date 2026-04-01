using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using JurisFlow.Server.Data;
using JurisFlow.Server.Services;

namespace JurisFlow.Server.Middleware
{
    public class TenantResolutionMiddleware
    {
        private readonly RequestDelegate _next;

        // Cache durations
        private static readonly TimeSpan SlugCacheDuration = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan ActiveCheckCacheDuration = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan NegativeCacheDuration = TimeSpan.FromSeconds(30);

        public TenantResolutionMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext context, TenantContext tenantContext, JurisFlowDbContext db, IMemoryCache cache, ILogger<TenantResolutionMiddleware> logger)
        {
            if (HttpMethods.IsOptions(context.Request.Method) || IsExemptPath(context.Request.Path))
            {
                tenantContext.RequireTenant = false;
                await _next(context);
                return;
            }

            string? claimTenantId = context.User.FindFirst("tenantId")?.Value
                                   ?? context.User.FindFirst("tid")?.Value;
            string? claimTenantSlug = context.User.FindFirst("tenantSlug")?.Value;

            if (!string.IsNullOrWhiteSpace(claimTenantId))
            {
                tenantContext.Set(claimTenantId, claimTenantSlug);
            }

            string? requestedTenantId = null;
            string? requestedTenantSlug = null;

            if (!tenantContext.IsResolved)
            {
                requestedTenantId = GetHeaderOrQuery(context, "X-Tenant-Id", "tenantId");
                requestedTenantSlug = GetHeaderOrQuery(context, "X-Tenant-Slug", "tenant", "tenantSlug");

                if (!string.IsNullOrWhiteSpace(requestedTenantId))
                {
                    tenantContext.Set(requestedTenantId, requestedTenantSlug);
                }
                else if (!string.IsNullOrWhiteSpace(requestedTenantSlug))
                {
                    var normalizedSlug = NormalizeSlug(requestedTenantSlug);
                    var cacheKey = $"tenant:slug:{normalizedSlug}";

                    var resolved = await cache.GetOrCreateAsync(cacheKey, async entry =>
                    {
                        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == normalizedSlug);
                        if (tenant != null)
                        {
                            entry.SlidingExpiration = SlugCacheDuration;
                            return (Id: tenant.Id, Slug: tenant.Slug, Found: true);
                        }

                        // Negative cache — prevent DB hammering for non-existent slugs
                        entry.AbsoluteExpirationRelativeToNow = NegativeCacheDuration;
                        return (Id: (string?)null, Slug: (string?)null, Found: false);
                    });

                    if (resolved.Found && !string.IsNullOrWhiteSpace(resolved.Id))
                    {
                        tenantContext.Set(resolved.Id, resolved.Slug);
                        requestedTenantId = resolved.Id;
                    }
                }
            }

            if (context.User?.Identity?.IsAuthenticated == true && !string.IsNullOrWhiteSpace(claimTenantId))
            {
                if (!string.IsNullOrWhiteSpace(requestedTenantId) && !string.Equals(claimTenantId, requestedTenantId, StringComparison.Ordinal))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await context.Response.WriteAsJsonAsync(new { message = "Tenant mismatch." });
                    return;
                }
            }

            if (!tenantContext.IsResolved)
            {
                if (context.User?.Identity?.IsAuthenticated == true)
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await context.Response.WriteAsJsonAsync(new { message = "Tenant context is missing." });
                    return;
                }

                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new { message = "Tenant is required." });
                return;
            }

            // Cache tenant active status check
            var activeCacheKey = $"tenant:active:{tenantContext.TenantId}";
            var tenantActive = await cache.GetOrCreateAsync(activeCacheKey, async entry =>
            {
                var isActive = await db.Tenants.AsNoTracking().AnyAsync(t => t.Id == tenantContext.TenantId && t.IsActive);
                entry.AbsoluteExpirationRelativeToNow = isActive ? ActiveCheckCacheDuration : NegativeCacheDuration;
                return isActive;
            });

            if (!tenantActive)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new { message = "Tenant is inactive." });
                return;
            }

            await _next(context);
        }

        private static bool IsExemptPath(PathString path)
        {
            return path.StartsWithSegments("/health")
                || path.StartsWithSegments("/swagger")
                || path.StartsWithSegments("/api/payments/webhook")
                || path.StartsWithSegments("/api/public/subscriptions")
                || path.StartsWithSegments("/api/tenants/provision");
        }

        private static string? GetHeaderOrQuery(HttpContext context, string headerName, params string[] queryNames)
        {
            if (context.Request.Headers.TryGetValue(headerName, out var headerValues))
            {
                var value = headerValues.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            foreach (var queryName in queryNames)
            {
                if (context.Request.Query.TryGetValue(queryName, out var queryValues))
                {
                    var value = queryValues.FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value;
                    }
                }
            }

            return null;
        }

        private static string NormalizeSlug(string slug)
        {
            return slug.Trim().ToLowerInvariant();
        }
    }
}
