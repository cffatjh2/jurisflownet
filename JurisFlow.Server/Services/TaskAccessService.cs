using System.Security.Claims;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using Microsoft.EntityFrameworkCore;
using TaskModel = JurisFlow.Server.Models.Task;

namespace JurisFlow.Server.Services
{
    public sealed class TaskAccessService
    {
        private readonly JurisFlowDbContext _context;
        private readonly MatterAccessService _matterAccess;

        public TaskAccessService(JurisFlowDbContext context, MatterAccessService matterAccess)
        {
            _context = context;
            _matterAccess = matterAccess;
        }

        public IQueryable<TaskModel> ApplyReadableScope(IQueryable<TaskModel> query, ClaimsPrincipal user)
        {
            if (_matterAccess.IsPrivileged(user))
            {
                return query;
            }

            var userId = _matterAccess.GetCurrentUserId(user);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return query.Where(_ => false);
            }

            var readableMatterIds = _matterAccess
                .ApplyReadableScope(_context.Matters.AsNoTracking(), user)
                .Select(m => m.Id);

            return query.Where(task =>
                (!string.IsNullOrWhiteSpace(task.MatterId) && readableMatterIds.Contains(task.MatterId!)) ||
                (string.IsNullOrWhiteSpace(task.MatterId) && task.CreatedByUserId == userId));
        }

        public IQueryable<TaskModel> ApplyManageableScope(IQueryable<TaskModel> query, ClaimsPrincipal user)
        {
            if (_matterAccess.IsPrivileged(user))
            {
                return query;
            }

            var userId = _matterAccess.GetCurrentUserId(user);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return query.Where(_ => false);
            }

            var manageableMatterIds = _context.Matters
                .AsNoTracking()
                .Where(m => m.CreatedByUserId == userId)
                .Select(m => m.Id);

            return query.Where(task =>
                (!string.IsNullOrWhiteSpace(task.MatterId) && manageableMatterIds.Contains(task.MatterId!)) ||
                (string.IsNullOrWhiteSpace(task.MatterId) && task.CreatedByUserId == userId));
        }

        public async Task<bool> CanCreateOrManageForMatterAsync(string? matterId, ClaimsPrincipal user, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(matterId))
            {
                return !string.IsNullOrWhiteSpace(_matterAccess.GetCurrentUserId(user));
            }

            return await _matterAccess.CanManageMatterAsync(matterId, user, cancellationToken: cancellationToken);
        }

        public async Task<TaskModel?> FindReadableTaskAsync(string id, ClaimsPrincipal user, bool asNoTracking = false, CancellationToken cancellationToken = default)
        {
            var query = ApplyReadableScope(_context.Tasks.AsQueryable(), user);
            if (asNoTracking)
            {
                query = query.AsNoTracking();
            }

            return await query.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        }

        public async Task<TaskModel?> FindManageableTaskAsync(string id, ClaimsPrincipal user, bool asNoTracking = false, CancellationToken cancellationToken = default)
        {
            var query = ApplyManageableScope(_context.Tasks.AsQueryable(), user);
            if (asNoTracking)
            {
                query = query.AsNoTracking();
            }

            return await query.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        }
    }
}
