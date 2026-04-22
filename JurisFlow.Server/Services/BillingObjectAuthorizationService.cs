using System.Security.Claims;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace JurisFlow.Server.Services
{
    public sealed class BillingObjectAuthorizationService
    {
        private readonly JurisFlowDbContext _context;
        private readonly MatterAccessService _matterAccess;

        public BillingObjectAuthorizationService(JurisFlowDbContext context, MatterAccessService matterAccess)
        {
            _context = context;
            _matterAccess = matterAccess;
        }

        public bool IsPrivileged(ClaimsPrincipal user) => _matterAccess.IsPrivileged(user);

        public IQueryable<Invoice> ApplyReadableInvoiceScope(IQueryable<Invoice> query, ClaimsPrincipal user)
        {
            if (IsPrivileged(user))
            {
                return query;
            }

            var readableMatterIds = _matterAccess.BuildBillingReadableMatterIdsQuery(user);
            return query.Where(i => i.MatterId != null && readableMatterIds.Contains(i.MatterId));
        }

        public IQueryable<PaymentTransaction> ApplyReadablePaymentTransactionScope(IQueryable<PaymentTransaction> query, ClaimsPrincipal user)
        {
            if (IsPrivileged(user))
            {
                return query;
            }

            var readableMatterIds = _matterAccess.BuildBillingReadableMatterIdsQuery(user);
            return query.Where(p =>
                (p.MatterId != null && readableMatterIds.Contains(p.MatterId)) ||
                (p.InvoiceId != null && _context.Invoices.Any(i => i.Id == p.InvoiceId && i.MatterId != null && readableMatterIds.Contains(i.MatterId))) ||
                (p.ClientId != null && _context.Matters.Any(m => m.ClientId == p.ClientId && readableMatterIds.Contains(m.Id))));
        }

        public IQueryable<PaymentPlan> ApplyReadablePaymentPlanScope(IQueryable<PaymentPlan> query, ClaimsPrincipal user)
        {
            if (IsPrivileged(user))
            {
                return query;
            }

            var readableMatterIds = _matterAccess.BuildBillingReadableMatterIdsQuery(user);
            return query.Where(p =>
                (p.InvoiceId != null && _context.Invoices.Any(i => i.Id == p.InvoiceId && i.MatterId != null && readableMatterIds.Contains(i.MatterId))) ||
                _context.Matters.Any(m => m.ClientId == p.ClientId && readableMatterIds.Contains(m.Id)));
        }

        public Task<bool> CanReadMatterAsync(string? matterId, ClaimsPrincipal user, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(matterId))
            {
                return Task.FromResult(false);
            }

            return _matterAccess.ApplyBillingReadableScope(_context.Matters.AsNoTracking(), user)
                .AnyAsync(m => m.Id == matterId, cancellationToken);
        }

        public Task<bool> CanManageMatterAsync(string? matterId, ClaimsPrincipal user, CancellationToken cancellationToken = default)
            => _matterAccess.CanManageMatterAsync(matterId, user, cancellationToken: cancellationToken);

        public async Task<bool> CanReadClientAsync(string? clientId, ClaimsPrincipal user, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(clientId))
            {
                return false;
            }

            if (IsPrivileged(user))
            {
                return await _context.Clients.AsNoTracking().AnyAsync(c => c.Id == clientId, cancellationToken);
            }

            var readableMatterIds = _matterAccess.BuildBillingReadableMatterIdsQuery(user);
            return await _context.Matters.AsNoTracking()
                .AnyAsync(m => m.ClientId == clientId && readableMatterIds.Contains(m.Id), cancellationToken);
        }

        public async Task<bool> CanManageClientAsync(string? clientId, ClaimsPrincipal user, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(clientId))
            {
                return false;
            }

            if (IsPrivileged(user))
            {
                return await _context.Clients.AsNoTracking().AnyAsync(c => c.Id == clientId, cancellationToken);
            }

            var userId = _matterAccess.GetCurrentUserId(user);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return false;
            }

            return await _context.Matters.AsNoTracking()
                .AnyAsync(m => m.ClientId == clientId && m.CreatedByUserId == userId, cancellationToken);
        }

        public async Task<bool> CanReadInvoiceAsync(Invoice invoice, ClaimsPrincipal user, CancellationToken cancellationToken = default)
        {
            if (!string.IsNullOrWhiteSpace(invoice.MatterId))
            {
                return await CanReadMatterAsync(invoice.MatterId, user, cancellationToken);
            }

            return await CanReadClientAsync(invoice.ClientId, user, cancellationToken);
        }

        public async Task<bool> CanManageInvoiceAsync(Invoice invoice, ClaimsPrincipal user, CancellationToken cancellationToken = default)
        {
            if (!string.IsNullOrWhiteSpace(invoice.MatterId))
            {
                return await CanManageMatterAsync(invoice.MatterId, user, cancellationToken);
            }

            return await CanManageClientAsync(invoice.ClientId, user, cancellationToken);
        }

        public async Task<bool> CanReadPaymentTransactionAsync(PaymentTransaction transaction, ClaimsPrincipal user, CancellationToken cancellationToken = default)
        {
            if (!string.IsNullOrWhiteSpace(transaction.InvoiceId))
            {
                var invoice = await _context.Invoices.AsNoTracking().FirstOrDefaultAsync(i => i.Id == transaction.InvoiceId, cancellationToken);
                return invoice != null && await CanReadInvoiceAsync(invoice, user, cancellationToken);
            }

            if (!string.IsNullOrWhiteSpace(transaction.MatterId))
            {
                return await CanReadMatterAsync(transaction.MatterId, user, cancellationToken);
            }

            return await CanReadClientAsync(transaction.ClientId, user, cancellationToken);
        }

        public async Task<bool> CanManagePaymentTransactionAsync(PaymentTransaction transaction, ClaimsPrincipal user, CancellationToken cancellationToken = default)
        {
            if (!string.IsNullOrWhiteSpace(transaction.InvoiceId))
            {
                var invoice = await _context.Invoices.AsNoTracking().FirstOrDefaultAsync(i => i.Id == transaction.InvoiceId, cancellationToken);
                return invoice != null && await CanManageInvoiceAsync(invoice, user, cancellationToken);
            }

            if (!string.IsNullOrWhiteSpace(transaction.MatterId))
            {
                return await CanManageMatterAsync(transaction.MatterId, user, cancellationToken);
            }

            return await CanManageClientAsync(transaction.ClientId, user, cancellationToken);
        }

        public async Task<bool> CanManagePaymentPlanAsync(PaymentPlan plan, ClaimsPrincipal user, CancellationToken cancellationToken = default)
        {
            if (!string.IsNullOrWhiteSpace(plan.InvoiceId))
            {
                var invoice = await _context.Invoices.AsNoTracking().FirstOrDefaultAsync(i => i.Id == plan.InvoiceId, cancellationToken);
                return invoice != null && await CanManageInvoiceAsync(invoice, user, cancellationToken);
            }

            return await CanManageClientAsync(plan.ClientId, user, cancellationToken);
        }
    }
}
