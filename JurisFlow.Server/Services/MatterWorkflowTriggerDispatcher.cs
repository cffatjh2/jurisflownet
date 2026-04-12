namespace JurisFlow.Server.Services
{
    public class MatterWorkflowTriggerDispatcher
    {
        private readonly OutcomeFeePlannerTriggerQueue _queue;
        private readonly TenantContext _tenantContext;
        private readonly ILogger<MatterWorkflowTriggerDispatcher> _logger;

        public MatterWorkflowTriggerDispatcher(
            OutcomeFeePlannerTriggerQueue queue,
            TenantContext tenantContext,
            ILogger<MatterWorkflowTriggerDispatcher> logger)
        {
            _queue = queue;
            _tenantContext = tenantContext;
            _logger = logger;
        }

        public bool TryEnqueue(
            string? userId,
            OutcomeFeePlanTriggerRequest? plannerRequest = null,
            ClientTransparencyTriggerRequest? transparencyRequest = null)
        {
            if (plannerRequest == null && transparencyRequest == null)
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(_tenantContext.TenantId))
            {
                _logger.LogWarning(
                    "Workflow trigger enqueue skipped because tenant context is missing. MatterId={MatterId} TriggerType={TriggerType} EntityId={EntityId}",
                    plannerRequest?.MatterId ?? transparencyRequest?.MatterId,
                    plannerRequest?.TriggerType ?? transparencyRequest?.TriggerType,
                    plannerRequest?.TriggerEntityId ?? transparencyRequest?.TriggerEntityId);
                return false;
            }

            var enqueued = _queue.Enqueue(new OutcomeFeePlannerTriggerJob(
                _tenantContext.TenantId!,
                _tenantContext.TenantSlug ?? string.Empty,
                string.IsNullOrWhiteSpace(userId) ? "system" : userId.Trim(),
                plannerRequest,
                transparencyRequest));

            if (!enqueued)
            {
                _logger.LogWarning(
                    "Workflow trigger queue rejected job. MatterId={MatterId} TriggerType={TriggerType} EntityId={EntityId}",
                    plannerRequest?.MatterId ?? transparencyRequest?.MatterId,
                    plannerRequest?.TriggerType ?? transparencyRequest?.TriggerType,
                    plannerRequest?.TriggerEntityId ?? transparencyRequest?.TriggerEntityId);
            }

            return enqueued;
        }
    }
}
