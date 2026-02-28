using Microsoft.AspNetCore.Http;

namespace JurisFlow.Server.Services
{
    public static class AuditTraceContextKeys
    {
        public const string CorrelationId = "audit.correlation_id";
        public const string IntegrationRunId = "audit.integration_run_id";
        public const string IntegrationConnectionId = "audit.integration_connection_id";
        public const string IntegrationProviderKey = "audit.integration_provider_key";
        public const string IntegrationEventId = "audit.integration_event_id";
    }

    public static class AuditTraceContext
    {
        public static void SetCorrelation(HttpContext httpContext, string? correlationId)
        {
            if (httpContext == null || string.IsNullOrWhiteSpace(correlationId))
            {
                return;
            }

            httpContext.Items[AuditTraceContextKeys.CorrelationId] = correlationId.Trim();
        }

        public static void SetIntegrationTrace(
            HttpContext httpContext,
            string? connectionId = null,
            string? providerKey = null,
            string? runId = null,
            string? eventId = null,
            string? correlationId = null)
        {
            if (httpContext == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(connectionId))
            {
                httpContext.Items[AuditTraceContextKeys.IntegrationConnectionId] = connectionId.Trim();
            }

            if (!string.IsNullOrWhiteSpace(providerKey))
            {
                httpContext.Items[AuditTraceContextKeys.IntegrationProviderKey] = providerKey.Trim().ToLowerInvariant();
            }

            if (!string.IsNullOrWhiteSpace(runId))
            {
                httpContext.Items[AuditTraceContextKeys.IntegrationRunId] = runId.Trim();
            }

            if (!string.IsNullOrWhiteSpace(eventId))
            {
                httpContext.Items[AuditTraceContextKeys.IntegrationEventId] = eventId.Trim();
            }

            SetCorrelation(httpContext, correlationId);
        }
    }
}
