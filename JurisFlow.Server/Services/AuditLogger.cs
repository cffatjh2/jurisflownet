using System.Security.Claims;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using Microsoft.Extensions.Hosting;
using Task = System.Threading.Tasks.Task;

namespace JurisFlow.Server.Services
{
    public class AuditLogger
    {
        private readonly JurisFlowDbContext _context;
        private readonly AuditLogIntegrityService _integrityService;
        private readonly TenantContext _tenantContext;
        private readonly ILogger<AuditLogger> _logger;
        private readonly bool _failClosed;

        public AuditLogger(
            JurisFlowDbContext context,
            AuditLogIntegrityService integrityService,
            TenantContext tenantContext,
            ILogger<AuditLogger> logger,
            IConfiguration configuration,
            IHostEnvironment hostEnvironment)
        {
            _context = context;
            _integrityService = integrityService;
            _tenantContext = tenantContext;
            _logger = logger;
            _failClosed = configuration.GetValue("Security:AuditLogFailClosed", hostEnvironment.IsProduction());
        }

        public async Task LogAsync(HttpContext httpContext, string action, string? entity = null, string? entityId = null, string? details = null)
        {
            try
            {
                var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var clientId = httpContext.User.FindFirst("clientId")?.Value;
                var role = httpContext.User.FindFirst(ClaimTypes.Role)?.Value ?? httpContext.User.FindFirst("role")?.Value;
                var enrichedDetails = BuildEnrichedDetails(httpContext, details);

                var audit = new AuditLog
                {
                    UserId = userId,
                    ClientId = clientId,
                    TenantId = _tenantContext.TenantId,
                    Role = role,
                    Action = action,
                    Entity = entity,
                    EntityId = entityId,
                    Details = enrichedDetails,
                    IpAddress = httpContext.Connection.RemoteIpAddress?.ToString(),
                    UserAgent = httpContext.Request.Headers.UserAgent.ToString(),
                    CreatedAt = DateTime.UtcNow
                };

                await _integrityService.PrepareAsync(audit);
                _context.AuditLogs.Add(audit);
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Audit logging failed. Action={Action} Entity={Entity} EntityId={EntityId} TenantId={TenantId}",
                    action,
                    entity,
                    entityId,
                    _tenantContext.TenantId);

                if (_failClosed)
                {
                    throw new InvalidOperationException("Audit logging failed.", ex);
                }
            }
        }

        private static string? BuildEnrichedDetails(HttpContext httpContext, string? details)
        {
            var traceId = httpContext.TraceIdentifier;
            var correlationId = httpContext.Items[AuditTraceContextKeys.CorrelationId]?.ToString()
                                ?? httpContext.Request.Headers["X-Correlation-ID"].FirstOrDefault();
            var integrationRunId = httpContext.Items[AuditTraceContextKeys.IntegrationRunId]?.ToString();
            var integrationConnectionId = httpContext.Items[AuditTraceContextKeys.IntegrationConnectionId]?.ToString();
            var integrationProviderKey = httpContext.Items[AuditTraceContextKeys.IntegrationProviderKey]?.ToString();
            var integrationEventId = httpContext.Items[AuditTraceContextKeys.IntegrationEventId]?.ToString();

            var tags = new List<string>();
            if (!string.IsNullOrWhiteSpace(traceId)) tags.Add($"traceId={traceId}");
            if (!string.IsNullOrWhiteSpace(correlationId)) tags.Add($"corr={correlationId}");
            if (!string.IsNullOrWhiteSpace(integrationRunId)) tags.Add($"integrationRunId={integrationRunId}");
            if (!string.IsNullOrWhiteSpace(integrationConnectionId)) tags.Add($"integrationConnectionId={integrationConnectionId}");
            if (!string.IsNullOrWhiteSpace(integrationProviderKey)) tags.Add($"integrationProviderKey={integrationProviderKey}");
            if (!string.IsNullOrWhiteSpace(integrationEventId)) tags.Add($"integrationEventId={integrationEventId}");

            if (tags.Count == 0)
            {
                return details;
            }

            var suffix = "[" + string.Join("; ", tags) + "]";
            if (string.IsNullOrWhiteSpace(details))
            {
                return suffix;
            }

            var trimmed = details.Trim();
            if (trimmed.Contains("traceId=", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains("integrationRunId=", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed;
            }

            return $"{trimmed} {suffix}";
        }
    }
}
