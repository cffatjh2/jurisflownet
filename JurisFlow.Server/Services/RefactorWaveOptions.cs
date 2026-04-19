namespace JurisFlow.Server.Services
{
    public sealed class RefactorWaveOptions
    {
        public bool UseLegacyTaskAssignmentAdapter { get; set; } = true;
        public bool UseLegacyMatterResponsibilityAdapter { get; set; } = true;
        public bool UseTenantCompanySynchronization { get; set; } = true;
        public bool RestrictLeadDeleteToPrivilegedRoles { get; set; } = true;
    }
}
