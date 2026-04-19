using System.Security.Claims;

namespace JurisFlow.Server.Services
{
    public sealed class BusinessSurfaceAuthorizationService
    {
        private static readonly string[] StaffRoles = { "Admin", "Partner", "Associate", "Employee", "Attorney", "Staff", "Manager" };
        private static readonly string[] PrivilegedRoles = { "Admin", "Partner", "Manager" };

        public bool CanManageClients(ClaimsPrincipal user) => HasAnyRole(user, StaffRoles);
        public bool CanManageClientPortal(ClaimsPrincipal user) => HasAnyRole(user, PrivilegedRoles);
        public bool CanArchiveClient(ClaimsPrincipal user) => HasAnyRole(user, PrivilegedRoles);
        public bool CanManageLeads(ClaimsPrincipal user) => HasAnyRole(user, StaffRoles);
        public bool CanDeleteLead(ClaimsPrincipal user) => HasAnyRole(user, PrivilegedRoles);
        public bool CanManageTasks(ClaimsPrincipal user) => HasAnyRole(user, StaffRoles);
        public bool CanManageEvents(ClaimsPrincipal user) => HasAnyRole(user, StaffRoles);

        private static bool HasAnyRole(ClaimsPrincipal user, IEnumerable<string> roles)
        {
            return roles.Any(user.IsInRole);
        }
    }
}
