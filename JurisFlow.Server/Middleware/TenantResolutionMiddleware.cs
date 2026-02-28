using Microsoft.EntityFrameworkCore;
using JurisFlow.Server.Data;
using JurisFlow.Server.Services;

namespace JurisFlow.Server.Middleware
{
    public class TenantResolutionMiddleware
    {
        private readonly RequestDelegate _next;

        public TenantResolutionMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext context, TenantContext tenantContext, JurisFlowDbContext db, ILogger<TenantResolutionMiddleware> logger)
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
                    var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == normalizedSlug);
                    if (tenant != null)
                    {
                        tenantContext.Set(tenant.Id, tenant.Slug);
                        requestedTenantId = tenant.Id;
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

            var tenantActive = await db.Tenants.AsNoTracking().AnyAsync(t => t.Id == tenantContext.TenantId && t.IsActive);
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
