using System.Globalization;
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
            var key = date.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            return _context.BillingLocks
                .AsNoTracking()
                .AnyAsync(
                    billingLock => string.Compare(key, billingLock.PeriodStart) >= 0 &&
                                   string.Compare(key, billingLock.PeriodEnd) <= 0,
                    cancellationToken);
        }
    }
}
