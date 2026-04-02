using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;

namespace JurisFlow.Server.Services
{
    public sealed class RoleAliasClaimsTransformation : IClaimsTransformation
    {
        public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
        {
            var identity = principal.Identities.FirstOrDefault(i => i.IsAuthenticated);
            if (identity == null)
            {
                return Task.FromResult(principal);
            }

            var existingRoles = principal.Claims
                .Where(claim => claim.Type == ClaimTypes.Role || claim.Type == "role")
                .Select(claim => claim.Value)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (!existingRoles.Contains("Attorney") || existingRoles.Contains("Associate"))
            {
                return Task.FromResult(principal);
            }

            var aliasIdentity = new ClaimsIdentity();
            aliasIdentity.AddClaim(new Claim(ClaimTypes.Role, "Associate"));
            aliasIdentity.AddClaim(new Claim("role", "Associate"));
            principal.AddIdentity(aliasIdentity);

            return Task.FromResult(principal);
        }
    }
}
