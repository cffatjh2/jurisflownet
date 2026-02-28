namespace JurisFlow.Server.Services
{
    public class TenantContext
    {
        public string? TenantId { get; private set; }
        public string? TenantSlug { get; private set; }
        public bool RequireTenant { get; set; } = true;

        public bool IsResolved => !string.IsNullOrWhiteSpace(TenantId);

        public void Set(string tenantId, string? tenantSlug = null)
        {
            TenantId = tenantId;
            if (!string.IsNullOrWhiteSpace(tenantSlug))
            {
                TenantSlug = tenantSlug;
            }
        }

        public void Clear()
        {
            TenantId = null;
            TenantSlug = null;
        }
    }
}
