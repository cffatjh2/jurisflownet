using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace JurisFlow.Server.Services
{
    public sealed class TrustActionAuthorizationService
    {
        private readonly IOptionsMonitor<TrustAccountingOptions> _optionsMonitor;

        public TrustActionAuthorizationService(IOptionsMonitor<TrustAccountingOptions> optionsMonitor)
        {
            _optionsMonitor = optionsMonitor;
        }

        public void EnsureAllowed(string actionKey, ClaimsPrincipal? user)
        {
            var role = GetRole(user);
            if (string.IsNullOrWhiteSpace(role) || !IsAllowed(actionKey, role))
            {
                throw new TrustCommandException(StatusCodes.Status403Forbidden, $"Current role is not allowed to perform '{actionKey}'.");
            }
        }

        public bool IsAllowed(string actionKey, string? role)
        {
            if (string.IsNullOrWhiteSpace(role))
            {
                return false;
            }

            return GetAllowedRoles(actionKey)
                .Contains(role.Trim(), StringComparer.OrdinalIgnoreCase);
        }

        private IReadOnlyCollection<string> GetAllowedRoles(string actionKey)
        {
            var matrix = _optionsMonitor.CurrentValue.RoleMatrix ?? new TrustAccountingRoleMatrixOptions();
            return actionKey switch
            {
                TrustActionKeys.ApproveTransaction => matrix.ApproveTransaction,
                TrustActionKeys.RejectTransaction => matrix.RejectTransaction,
                TrustActionKeys.VoidTransaction => matrix.VoidTransaction,
                TrustActionKeys.ClearDeposit => matrix.ClearDeposit,
                TrustActionKeys.ReturnDeposit => matrix.ReturnDeposit,
                TrustActionKeys.EarnedFeeTransfer => matrix.EarnedFeeTransfer,
                TrustActionKeys.ImportStatement => matrix.ImportStatement,
                TrustActionKeys.ManageOutstandingItems => matrix.ManageOutstandingItems,
                TrustActionKeys.PrepareReconciliationPacket => matrix.PrepareReconciliationPacket,
                TrustActionKeys.SignoffReconciliationPacket => matrix.SignoffReconciliationPacket,
                TrustActionKeys.RebuildProjections => matrix.RebuildProjections,
                TrustActionKeys.ManageGovernance => matrix.ManageGovernance,
                TrustActionKeys.ManagePolicies => matrix.ManagePolicies,
                TrustActionKeys.OverrideTransaction => matrix.OverrideTransaction,
                TrustActionKeys.ExportData => matrix.ExportData,
                _ => Array.Empty<string>()
            };
        }

        public static string? GetRole(ClaimsPrincipal? user)
        {
            return user?.FindFirst(ClaimTypes.Role)?.Value ?? user?.FindFirst("role")?.Value;
        }
    }
}
