using System.Security.Claims;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace JurisFlow.Server.Services
{
    public sealed class MatterAccessService
    {
        private static readonly string[] PrivilegedRoles = { "Admin", "Partner", "Manager" };
        private readonly JurisFlowDbContext _context;

        public MatterAccessService(JurisFlowDbContext context)
        {
            _context = context;
        }

        public string? GetCurrentUserId(ClaimsPrincipal user)
        {
            return user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? user.FindFirst("sub")?.Value;
        }

        public bool IsPrivileged(ClaimsPrincipal user)
        {
            return PrivilegedRoles.Any(role => HasRole(user, role));
        }

        public IQueryable<Matter> ApplyReadableScope(IQueryable<Matter> query, ClaimsPrincipal user)
        {
            if (IsPrivileged(user))
            {
                return query;
            }

            var userId = GetCurrentUserId(user);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return query.Where(_ => false);
            }

            return query.Where(m => m.CreatedByUserId == userId || m.ShareWithFirm);
        }

        public IQueryable<Matter> ApplyBillingReadableScope(IQueryable<Matter> query, ClaimsPrincipal user)
        {
            if (IsPrivileged(user))
            {
                return query;
            }

            var userId = GetCurrentUserId(user);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return query.Where(_ => false);
            }

            return query.Where(m =>
                m.CreatedByUserId == userId ||
                (m.ShareWithFirm && m.ShareBillingWithFirm));
        }

        public IQueryable<string> BuildReadableMatterIdsQuery(ClaimsPrincipal user)
        {
            return ApplyReadableScope(_context.Matters.AsNoTracking(), user)
                .Select(m => m.Id);
        }

        public IQueryable<string> BuildBillingReadableMatterIdsQuery(ClaimsPrincipal user)
        {
            return ApplyBillingReadableScope(_context.Matters.AsNoTracking(), user)
                .Select(m => m.Id);
        }

        public async Task<bool> CanReadMatterAsync(string? matterId, ClaimsPrincipal user, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(matterId))
            {
                return false;
            }

            return await ApplyReadableScope(_context.Matters.AsNoTracking(), user)
                .AnyAsync(m => m.Id == matterId, cancellationToken);
        }

        public async Task<bool> CanManageMatterAsync(string? matterId, ClaimsPrincipal user, bool ignoreQueryFilters = false, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(matterId))
            {
                return false;
            }

            var query = ignoreQueryFilters
                ? _context.Matters.IgnoreQueryFilters().AsQueryable()
                : _context.Matters.AsQueryable();

            if (IsPrivileged(user))
            {
                return await query.AsNoTracking().AnyAsync(m => m.Id == matterId, cancellationToken);
            }

            var userId = GetCurrentUserId(user);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return false;
            }

            return await query.AsNoTracking()
                .AnyAsync(m => m.Id == matterId && m.CreatedByUserId == userId, cancellationToken);
        }

        public bool CanSeeMatterNotes(Matter matter, ClaimsPrincipal user)
        {
            if (matter == null)
            {
                return false;
            }

            if (IsPrivileged(user))
            {
                return true;
            }

            var userId = GetCurrentUserId(user);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return false;
            }

            return matter.CreatedByUserId == userId ||
                (matter.ShareWithFirm && matter.ShareNotesWithFirm);
        }

        private static bool HasRole(ClaimsPrincipal user, string role)
        {
            if (user.IsInRole(role))
            {
                return true;
            }

            return user.Claims.Any(claim =>
                (claim.Type == ClaimTypes.Role || claim.Type == "role") &&
                string.Equals(claim.Value, role, StringComparison.OrdinalIgnoreCase));
        }
    }
}
