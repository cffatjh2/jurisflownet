using JurisFlow.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace JurisFlow.Server.Services
{
    public class TrustComplianceService
    {
        private readonly JurisFlowDbContext _context;

        public TrustComplianceService(JurisFlowDbContext context)
        {
            _context = context;
        }

        public async Task<TrustComplianceSummary?> EvaluateAsync(string trustAccountId, double? bankStatementBalance = null, DateTime? asOfUtc = null)
        {
            var account = await _context.TrustBankAccounts.FindAsync(trustAccountId);
            if (account == null) return null;

            var cutoff = asOfUtc ?? DateTime.UtcNow;

            var ledgers = await _context.ClientTrustLedgers
                .Where(l => l.TrustAccountId == trustAccountId)
                .ToListAsync();

            var pendingTransactions = await _context.TrustTransactions
                .Where(t => t.TrustAccountId == trustAccountId && t.Status == "PENDING" && t.CreatedAt <= cutoff)
                .ToListAsync();

            var negativeLedgers = ledgers
                .Where(l => l.RunningBalance < 0)
                .Select(l => new TrustComplianceLedgerIssue
                {
                    LedgerId = l.Id,
                    ClientId = l.ClientId,
                    MatterId = l.MatterId,
                    Balance = l.RunningBalance
                })
                .ToList();

            var ledgerTotal = ledgers.Sum(l => l.RunningBalance);
            var trustBalance = account.CurrentBalance;
            var ledgerDiscrepancy = Math.Round(trustBalance - ledgerTotal, 2);

            var bankDiscrepancy = bankStatementBalance.HasValue
                ? Math.Round(bankStatementBalance.Value - trustBalance, 2)
                : (double?)null;

            return new TrustComplianceSummary
            {
                TrustAccountId = trustAccountId,
                AsOfUtc = cutoff,
                TrustBalance = trustBalance,
                LedgerTotal = ledgerTotal,
                LedgerDiscrepancy = ledgerDiscrepancy,
                BankStatementBalance = bankStatementBalance,
                BankDiscrepancy = bankDiscrepancy,
                PendingTransactions = pendingTransactions.Count,
                NegativeLedgerCount = negativeLedgers.Count,
                NegativeLedgers = negativeLedgers,
                IsBalanced = Math.Abs(ledgerDiscrepancy) < 0.01 && (!bankDiscrepancy.HasValue || Math.Abs(bankDiscrepancy.Value) < 0.01)
            };
        }
    }

    public class TrustComplianceSummary
    {
        public string TrustAccountId { get; set; } = string.Empty;
        public DateTime AsOfUtc { get; set; }
        public double TrustBalance { get; set; }
        public double LedgerTotal { get; set; }
        public double LedgerDiscrepancy { get; set; }
        public double? BankStatementBalance { get; set; }
        public double? BankDiscrepancy { get; set; }
        public int PendingTransactions { get; set; }
        public int NegativeLedgerCount { get; set; }
        public List<TrustComplianceLedgerIssue> NegativeLedgers { get; set; } = new();
        public bool IsBalanced { get; set; }
    }

    public class TrustComplianceLedgerIssue
    {
        public string? LedgerId { get; set; }
        public string? ClientId { get; set; }
        public string? MatterId { get; set; }
        public double Balance { get; set; }
    }
}
