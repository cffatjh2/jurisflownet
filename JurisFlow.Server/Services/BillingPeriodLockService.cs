using JurisFlow.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace JurisFlow.Server.Services
{
    public class BillingPeriodLockService
    {
        private readonly JurisFlowDbContext _context;

        public BillingPeriodLockService(JurisFlowDbContext context)
        {
            _context = context;
        }

        public Task<bool> IsLockedAsync(DateTime date, CancellationToken cancellationToken = default)
        {
            var day = date.Date;

            return _context.BillingLocks
                .AsNoTracking()
                .AnyAsync(
                    billingLock => billingLock.PeriodStart <= day &&
                                   billingLock.PeriodEnd >= day,
                    cancellationToken);
        }
    }
}
