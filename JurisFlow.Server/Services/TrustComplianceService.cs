using JurisFlow.Server.Contracts;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;
using Task = System.Threading.Tasks.Task;

namespace JurisFlow.Server.Services
{
    public class TrustComplianceService
    {
        private readonly JurisFlowDbContext _context;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public TrustComplianceService(JurisFlowDbContext context, IHttpContextAccessor httpContextAccessor)
        {
            _context = context;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task<TrustComplianceSummary?> EvaluateAsync(string trustAccountId, decimal? bankStatementBalance = null, DateTime? asOfUtc = null)
        {
            var account = await _context.TrustBankAccounts.FindAsync(trustAccountId);
            if (account == null) return null;

            var cutoff = asOfUtc ?? DateTime.UtcNow;
            var pendingTransactions = await _context.TrustTransactions
                .Where(t => t.TrustAccountId == trustAccountId && t.Status == "PENDING" && t.CreatedAt <= cutoff)
                .ToListAsync();

            var journalLedgerBalances = await _context.TrustJournalEntries
                .AsNoTracking()
                .Where(j =>
                    j.TrustAccountId == trustAccountId &&
                    j.ClientTrustLedgerId != null &&
                    j.EffectiveAt <= cutoff)
                .GroupBy(j => j.ClientTrustLedgerId!)
                .Select(g => new
                {
                    LedgerId = g.Key,
                    Balance = g.Sum(x => x.Amount)
                })
                .ToListAsync();

            var hasJournalData = journalLedgerBalances.Count > 0 ||
                await _context.TrustJournalEntries.AsNoTracking()
                    .AnyAsync(j => j.TrustAccountId == trustAccountId && j.EffectiveAt <= cutoff);

            var ledgers = await _context.ClientTrustLedgers
                .AsNoTracking()
                .Where(l => l.TrustAccountId == trustAccountId)
                .ToListAsync();

            var negativeLedgers = hasJournalData
                ? ledgers
                    .GroupJoin(
                        journalLedgerBalances,
                        ledger => ledger.Id,
                        balance => balance.LedgerId,
                        (ledger, balances) => new { Ledger = ledger, Balance = balances.Select(x => x.Balance).FirstOrDefault() })
                    .Where(x => x.Balance < 0m)
                    .Select(x => new TrustComplianceLedgerIssue
                    {
                        LedgerId = x.Ledger.Id,
                        ClientId = x.Ledger.ClientId,
                        MatterId = x.Ledger.MatterId,
                        Balance = Math.Round(x.Balance, 2, MidpointRounding.AwayFromZero)
                    })
                    .ToList()
                : ledgers
                    .Where(l => l.RunningBalance < 0)
                    .Select(l => new TrustComplianceLedgerIssue
                    {
                        LedgerId = l.Id,
                        ClientId = l.ClientId,
                        MatterId = l.MatterId,
                        Balance = l.RunningBalance
                    })
                    .ToList();

            var ledgerTotal = hasJournalData
                ? Math.Round(journalLedgerBalances.Sum(l => l.Balance), 2, MidpointRounding.AwayFromZero)
                : ledgers.Sum(l => l.RunningBalance);

            var trustBalance = hasJournalData
                ? Math.Round(
                    await _context.TrustJournalEntries
                        .AsNoTracking()
                        .Where(j => j.TrustAccountId == trustAccountId && j.EffectiveAt <= cutoff)
                        .SumAsync(j => (decimal?)j.Amount) ?? 0m,
                    2,
                    MidpointRounding.AwayFromZero)
                : account.CurrentBalance;

            var ledgerDiscrepancy = Math.Round(trustBalance - ledgerTotal, 2, MidpointRounding.AwayFromZero);

            var bankDiscrepancy = bankStatementBalance.HasValue
                ? Math.Round(bankStatementBalance.Value - trustBalance, 2, MidpointRounding.AwayFromZero)
                : (decimal?)null;

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
                IsBalanced = Math.Abs(ledgerDiscrepancy) < 0.01m && (!bankDiscrepancy.HasValue || Math.Abs(bankDiscrepancy.Value) < 0.01m)
            };
        }

        public async Task<TrustOperationalAlertSummary> GetOperationalAlertsAsync(
            string? trustAccountId = null,
            string? severity = null,
            string? alertType = null,
            DateTime? asOfUtc = null,
            CancellationToken ct = default)
        {
            var cutoff = asOfUtc ?? DateTime.UtcNow;
            var alerts = await BuildOperationalAlertsAsync(trustAccountId, cutoff, ct);
            alerts = ApplyAlertFilters(alerts, severity, alertType);
            await OverlayOperationalAlertLifecycleAsync(alerts, ct);

            var distinctAccounts = alerts
                .Where(a => !string.IsNullOrWhiteSpace(a.TrustAccountId))
                .Select(a => a.TrustAccountId!)
                .Distinct(StringComparer.Ordinal)
                .Count();

            return new TrustOperationalAlertSummary
            {
                GeneratedAtUtc = cutoff,
                TotalCount = alerts.Count,
                CriticalCount = alerts.Count(a => string.Equals(a.Severity, "critical", StringComparison.OrdinalIgnoreCase)),
                WarningCount = alerts.Count(a => string.Equals(a.Severity, "warning", StringComparison.OrdinalIgnoreCase)),
                AccountsImpacted = distinctAccounts,
                Alerts = alerts
                    .OrderByDescending(a => GetSeverityRank(a.Severity))
                    .ThenByDescending(a => a.AgeDays)
                    .ThenByDescending(a => a.OpenedAt)
                    .ToList()
            };
        }

        public async Task<TrustOperationalAlertSyncResultDto> SyncOperationalAlertsAsync(CancellationToken ct = default)
        {
            var cutoff = DateTime.UtcNow;
            var computedAlerts = await BuildOperationalAlertsAsync(null, cutoff, ct);
            var computedByKey = computedAlerts.ToDictionary(BuildAlertKey, StringComparer.Ordinal);
            var computedKeys = computedByKey.Keys.ToHashSet(StringComparer.Ordinal);
            var existingAlerts = await _context.TrustOperationalAlerts
                .Where(a => computedKeys.Contains(a.AlertKey) || a.WorkflowStatus != "resolved")
                .ToListAsync(ct);
            var existingByKey = existingAlerts.ToDictionary(a => a.AlertKey, StringComparer.Ordinal);
            var accountMap = await LoadAlertAccountMapAsync(computedAlerts.Select(a => a.TrustAccountId), ct);

            var createdCount = 0;
            var reopenedCount = 0;
            var updatedCount = 0;
            var autoResolvedCount = 0;
            var notificationCount = 0;
            var touchedKeys = new HashSet<string>(StringComparer.Ordinal);

            foreach (var computed in computedAlerts)
            {
                var alertKey = BuildAlertKey(computed);
                touchedKeys.Add(alertKey);
                accountMap.TryGetValue(computed.TrustAccountId ?? string.Empty, out var account);

                if (!existingByKey.TryGetValue(alertKey, out var record))
                {
                    record = new TrustOperationalAlert
                    {
                        Id = Guid.NewGuid().ToString(),
                        AlertKey = alertKey,
                        WorkflowStatus = "open",
                        FirstDetectedAt = cutoff,
                        CreatedAt = cutoff
                    };
                    ApplyAlertSnapshot(record, computed, cutoff);
                    _context.TrustOperationalAlerts.Add(record);
                    existingByKey[alertKey] = record;
                    createdCount++;
                    AddAlertEvent(record, "detected", null, "Alert detected during compliance sweep.", computed);
                    notificationCount += QueueAlertNotifications(record, account, "detected");
                    continue;
                }

                var wasResolved = string.Equals(record.WorkflowStatus, "resolved", StringComparison.OrdinalIgnoreCase);
                ApplyAlertSnapshot(record, computed, cutoff);
                if (wasResolved)
                {
                    record.WorkflowStatus = "open";
                    record.ResolvedAt = null;
                    record.ResolvedBy = null;
                    reopenedCount++;
                    AddAlertEvent(record, "reopened", null, "Alert condition is still present after a resolved state.", computed);
                    notificationCount += QueueAlertNotifications(record, account, "reopened");
                }
                else
                {
                    updatedCount++;
                }
            }

            foreach (var stale in existingAlerts.Where(a =>
                         !touchedKeys.Contains(a.AlertKey) &&
                         !string.Equals(a.WorkflowStatus, "resolved", StringComparison.OrdinalIgnoreCase)))
            {
                stale.WorkflowStatus = "resolved";
                stale.ResolvedAt = cutoff;
                stale.ResolvedBy = "system";
                stale.UpdatedAt = cutoff;
                autoResolvedCount++;
                AddAlertEvent(stale, "auto_resolved", null, "Alert auto-resolved because the compliance sweep no longer detects the condition.");
            }

            await _context.SaveChangesAsync(ct);

            return new TrustOperationalAlertSyncResultDto
            {
                GeneratedAtUtc = cutoff,
                ActiveAlertCount = computedAlerts.Count,
                CreatedCount = createdCount,
                ReopenedCount = reopenedCount,
                UpdatedCount = updatedCount,
                AutoResolvedCount = autoResolvedCount,
                NotificationCount = notificationCount
            };
        }

        public async Task<IReadOnlyList<TrustOperationalAlertRecordDto>> GetOperationalAlertRecordsAsync(
            string? trustAccountId = null,
            string? workflowStatus = null,
            string? severity = null,
            string? alertType = null,
            CancellationToken ct = default)
        {
            var query = _context.TrustOperationalAlerts.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(trustAccountId))
            {
                query = query.Where(a => a.TrustAccountId == trustAccountId);
            }

            if (!string.IsNullOrWhiteSpace(workflowStatus))
            {
                var normalizedWorkflowStatus = workflowStatus.Trim().ToLowerInvariant();
                query = query.Where(a => a.WorkflowStatus == normalizedWorkflowStatus);
            }

            if (!string.IsNullOrWhiteSpace(severity))
            {
                var normalizedSeverity = severity.Trim().ToLowerInvariant();
                query = query.Where(a => a.Severity == normalizedSeverity);
            }

            if (!string.IsNullOrWhiteSpace(alertType))
            {
                var normalizedAlertType = alertType.Trim().ToLowerInvariant();
                query = query.Where(a => a.AlertType == normalizedAlertType);
            }

            var records = await query
                .OrderByDescending(a => a.LastDetectedAt)
                .Take(400)
                .ToListAsync(ct);
            records = records
                .OrderByDescending(a => GetSeverityRank(a.Severity))
                .ThenByDescending(a => a.LastDetectedAt)
                .Take(200)
                .ToList();
            var accountNameMap = records.Count == 0
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : await _context.TrustBankAccounts.AsNoTracking()
                    .Where(a => records.Select(r => r.TrustAccountId).Where(id => !string.IsNullOrWhiteSpace(id)).Contains(a.Id))
                    .ToDictionaryAsync(a => a.Id, a => a.Name, ct);
            return records.Select(record => ToOperationalAlertRecordDto(record, accountNameMap)).ToList();
        }

        public async Task<IReadOnlyList<TrustOperationalAlertEventDto>> GetOperationalAlertHistoryAsync(string alertId, CancellationToken ct = default)
        {
            var alert = await _context.TrustOperationalAlerts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == alertId, ct);
            if (alert == null)
            {
                throw new TrustCommandException(StatusCodes.Status404NotFound, "Operational alert not found.");
            }

            var events = await _context.TrustOperationalAlertEvents.AsNoTracking()
                .Where(e => e.TrustOperationalAlertId == alertId)
                .OrderByDescending(e => e.CreatedAt)
                .Take(100)
                .ToListAsync(ct);

            return events.Select(e => new TrustOperationalAlertEventDto
            {
                Id = e.Id,
                EventType = e.EventType,
                ActorUserId = e.ActorUserId,
                Notes = e.Notes,
                MetadataJson = e.MetadataJson,
                CreatedAt = e.CreatedAt
            }).ToList();
        }

        public Task<TrustOperationalAlertRecordDto> AcknowledgeOperationalAlertAsync(string alertId, Contracts.TrustOperationalAlertActionDto? dto, CancellationToken ct = default)
            => UpdateOperationalAlertAsync(alertId, "acknowledged", dto?.Notes, null, ct);

        public Task<TrustOperationalAlertRecordDto> EscalateOperationalAlertAsync(string alertId, Contracts.TrustOperationalAlertActionDto? dto, CancellationToken ct = default)
            => UpdateOperationalAlertAsync(alertId, "escalated", dto?.Notes, null, ct);

        public Task<TrustOperationalAlertRecordDto> ResolveOperationalAlertAsync(string alertId, Contracts.TrustOperationalAlertActionDto? dto, CancellationToken ct = default)
            => UpdateOperationalAlertAsync(alertId, "resolved", dto?.Notes, null, ct);

        public Task<TrustOperationalAlertRecordDto> AssignOperationalAlertAsync(string alertId, Contracts.TrustOperationalAlertAssignDto dto, CancellationToken ct = default)
            => UpdateOperationalAlertAsync(alertId, "assigned", dto.Notes, dto.AssigneeUserId, ct);

        private async Task<List<TrustOperationalAlertDto>> BuildOperationalAlertsAsync(string? trustAccountId, DateTime cutoff, CancellationToken ct)
        {
            var accountsQuery = _context.TrustBankAccounts
                .AsNoTracking()
                .Where(a => a.Status == TrustAccountStatus.ACTIVE);

            if (!string.IsNullOrWhiteSpace(trustAccountId))
            {
                accountsQuery = accountsQuery.Where(a => a.Id == trustAccountId);
            }

            var accounts = await accountsQuery.ToListAsync(ct);
            if (accounts.Count == 0)
            {
                return [];
            }

            var accountIds = accounts.Select(a => a.Id).ToList();
            var openOutstandingItems = await _context.TrustOutstandingItems
                .AsNoTracking()
                .Where(i => accountIds.Contains(i.TrustAccountId) && i.Status == "open")
                .ToListAsync(ct);
            var canonicalMonthCloses = await _context.TrustMonthCloses
                .AsNoTracking()
                .Where(c => accountIds.Contains(c.TrustAccountId) && c.IsCanonical)
                .ToListAsync(ct);
            var duplicateImports = await _context.TrustStatementImports
                .AsNoTracking()
                .Where(i => accountIds.Contains(i.TrustAccountId) && i.Status == "duplicate")
                .ToListAsync(ct);
            var unclearedFunds = await _context.TrustJournalEntries
                .AsNoTracking()
                .Where(j => accountIds.Contains(j.TrustAccountId) &&
                            j.AvailabilityClass == "uncleared" &&
                            j.EffectiveAt <= cutoff &&
                            j.Amount > 0m)
                .GroupBy(j => j.TrustAccountId)
                .Select(g => new
                {
                    TrustAccountId = g.Key,
                    Amount = g.Sum(x => x.Amount),
                    OldestEffectiveAt = g.Min(x => x.EffectiveAt)
                })
                .ToListAsync(ct);

            var alerts = new List<TrustOperationalAlertDto>();
            foreach (var account in accounts)
            {
                var expectedPeriodEnd = GetExpectedCompletedPeriodEnd(cutoff, account.StatementCadence);
                var closeGraceDate = GetCloseGraceDate(expectedPeriodEnd, account.StatementCadence);
                var accountCloses = canonicalMonthCloses
                    .Where(c => c.TrustAccountId == account.Id)
                    .OrderByDescending(c => c.PeriodEnd)
                    .ThenByDescending(c => c.VersionNumber)
                    .ToList();
                var expectedClose = accountCloses.FirstOrDefault(c => c.PeriodEnd.Date == expectedPeriodEnd.Date);

                if (cutoff.Date > closeGraceDate.Date)
                {
                    if (expectedClose == null)
                    {
                        alerts.Add(new TrustOperationalAlertDto
                        {
                            AlertType = "missing_month_close",
                            Severity = "critical",
                            TrustAccountId = account.Id,
                            TrustAccountName = account.Name,
                            RelatedEntityType = "TrustBankAccount",
                            RelatedEntityId = account.Id,
                            PeriodEnd = expectedPeriodEnd,
                            OpenedAt = closeGraceDate,
                            AgeDays = Math.Max(0, (cutoff.Date - closeGraceDate.Date).Days),
                            Title = "Required month-close is missing",
                            Summary = $"No canonical {account.StatementCadence} close exists for the period ending {expectedPeriodEnd:yyyy-MM-dd}.",
                            Status = "open",
                            ActionHint = "Prepare and sign the canonical reconciliation packet for the missing period."
                        });
                    }
                    else if (!string.Equals(expectedClose.Status, "closed", StringComparison.OrdinalIgnoreCase))
                    {
                        var unsignedCloseSeverity = (cutoff.Date - closeGraceDate.Date).Days >= 10 ? "critical" : "warning";
                        alerts.Add(new TrustOperationalAlertDto
                        {
                            AlertType = "unsigned_month_close",
                            Severity = unsignedCloseSeverity,
                            TrustAccountId = account.Id,
                            TrustAccountName = account.Name,
                            RelatedEntityType = "TrustMonthClose",
                            RelatedEntityId = expectedClose.Id,
                            PeriodEnd = expectedClose.PeriodEnd,
                            OpenedAt = expectedClose.PreparedAt,
                            AgeDays = Math.Max(0, (cutoff.Date - expectedClose.PreparedAt.Date).Days),
                            Title = "Month-close is not fully signed",
                            Summary = $"Canonical close for {expectedClose.PeriodEnd:yyyy-MM-dd} is still {expectedClose.Status.Replace('_', ' ')}.",
                            Status = expectedClose.Status,
                            ActionHint = "Complete reviewer and responsible lawyer sign-off."
                        });
                    }
                }

                var accountOutstanding = openOutstandingItems
                    .Where(i => i.TrustAccountId == account.Id)
                    .OrderBy(i => i.OccurredAt)
                    .ToList();
                if (accountOutstanding.Count > 0)
                {
                    var oldestOutstanding = accountOutstanding[0];
                    var oldestAge = Math.Max(0, (cutoff.Date - oldestOutstanding.OccurredAt.Date).Days);
                    if (oldestAge >= 14)
                    {
                        alerts.Add(new TrustOperationalAlertDto
                        {
                            AlertType = "outstanding_item_aging",
                            Severity = oldestAge >= 30 ? "critical" : "warning",
                            TrustAccountId = account.Id,
                            TrustAccountName = account.Name,
                            RelatedEntityType = "TrustOutstandingItem",
                            RelatedEntityId = oldestOutstanding.Id,
                            PeriodEnd = oldestOutstanding.PeriodEnd,
                            OpenedAt = oldestOutstanding.OccurredAt,
                            AgeDays = oldestAge,
                            Title = "Outstanding exceptions are aging",
                            Summary = $"{accountOutstanding.Count} open outstanding item(s), oldest from {oldestOutstanding.OccurredAt:yyyy-MM-dd}, total impact {accountOutstanding.Sum(i => i.Amount):0.00}.",
                            Status = "open",
                            ActionHint = "Resolve, match, or document aged outstanding items before next sign-off."
                        });
                    }
                }

                var uncleared = unclearedFunds.FirstOrDefault(u => u.TrustAccountId == account.Id);
                if (uncleared != null)
                {
                    var unclearedAge = Math.Max(0, (cutoff.Date - uncleared.OldestEffectiveAt.Date).Days);
                    if (unclearedAge >= 7 && uncleared.Amount > 0m)
                    {
                        alerts.Add(new TrustOperationalAlertDto
                        {
                            AlertType = "uncleared_funds_aging",
                            Severity = unclearedAge >= 21 ? "critical" : "warning",
                            TrustAccountId = account.Id,
                            TrustAccountName = account.Name,
                            RelatedEntityType = "TrustBankAccount",
                            RelatedEntityId = account.Id,
                            OpenedAt = uncleared.OldestEffectiveAt,
                            AgeDays = unclearedAge,
                            Title = "Uncleared funds are still aging",
                            Summary = $"{uncleared.Amount:0.00} remains uncleared; oldest uncleared journal entry is from {uncleared.OldestEffectiveAt:yyyy-MM-dd}.",
                            Status = "open",
                            ActionHint = "Confirm bank clearance or keep the funds out of disbursement capacity."
                        });
                    }
                }

                var accountDuplicates = duplicateImports
                    .Where(i => i.TrustAccountId == account.Id)
                    .OrderByDescending(i => i.ImportedAt)
                    .ToList();
                if (accountDuplicates.Count > 0)
                {
                    var latestDuplicate = accountDuplicates[0];
                    alerts.Add(new TrustOperationalAlertDto
                    {
                        AlertType = "duplicate_statement_import",
                        Severity = "warning",
                        TrustAccountId = account.Id,
                        TrustAccountName = account.Name,
                        RelatedEntityType = "TrustStatementImport",
                        RelatedEntityId = latestDuplicate.Id,
                        PeriodEnd = latestDuplicate.PeriodEnd,
                        OpenedAt = latestDuplicate.ImportedAt,
                        AgeDays = Math.Max(0, (cutoff.Date - latestDuplicate.ImportedAt.Date).Days),
                        Title = "Duplicate statement evidence detected",
                        Summary = $"{accountDuplicates.Count} duplicate import(s) captured for this account. Latest duplicate references {latestDuplicate.DuplicateOfStatementImportId ?? "an earlier statement"}",
                        Status = "duplicate",
                        ActionHint = "Review source hashes and keep only the canonical statement packet in active workflows."
                    });
                }
            }

            return alerts;
        }

        private static List<TrustOperationalAlertDto> ApplyAlertFilters(List<TrustOperationalAlertDto> alerts, string? severity, string? alertType)
        {
            if (!string.IsNullOrWhiteSpace(severity))
            {
                var normalizedSeverity = severity.Trim().ToLowerInvariant();
                alerts = alerts.Where(a => string.Equals(a.Severity, normalizedSeverity, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            if (!string.IsNullOrWhiteSpace(alertType))
            {
                var normalizedAlertType = alertType.Trim().ToLowerInvariant();
                alerts = alerts.Where(a => string.Equals(a.AlertType, normalizedAlertType, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            return alerts;
        }

        private async Task OverlayOperationalAlertLifecycleAsync(List<TrustOperationalAlertDto> alerts, CancellationToken ct)
        {
            if (alerts.Count == 0)
            {
                return;
            }

            var alertKeys = alerts.Select(BuildAlertKey).Distinct(StringComparer.Ordinal).ToList();
            var records = await _context.TrustOperationalAlerts.AsNoTracking()
                .Where(a => alertKeys.Contains(a.AlertKey))
                .ToListAsync(ct);
            var recordByKey = records.ToDictionary(r => r.AlertKey, StringComparer.Ordinal);

            foreach (var alert in alerts)
            {
                if (!recordByKey.TryGetValue(BuildAlertKey(alert), out var record))
                {
                    continue;
                }

                alert.AlertId = record.Id;
                alert.AlertKey = record.AlertKey;
                alert.WorkflowStatus = record.WorkflowStatus;
                alert.AssignedUserId = record.AssignedUserId;
                alert.FirstDetectedAt = record.FirstDetectedAt;
                alert.LastDetectedAt = record.LastDetectedAt;
                alert.AcknowledgedBy = record.AcknowledgedBy;
                alert.AcknowledgedAt = record.AcknowledgedAt;
                alert.EscalatedBy = record.EscalatedBy;
                alert.EscalatedAt = record.EscalatedAt;
                alert.ResolvedBy = record.ResolvedBy;
                alert.ResolvedAt = record.ResolvedAt;
                alert.NotificationCount = record.NotificationCount;
            }
        }

        private async Task<TrustOperationalAlertRecordDto> UpdateOperationalAlertAsync(string alertId, string action, string? notes, string? assigneeUserId, CancellationToken ct)
        {
            var alert = await _context.TrustOperationalAlerts.FirstOrDefaultAsync(a => a.Id == alertId, ct);
            if (alert == null)
            {
                throw new TrustCommandException(StatusCodes.Status404NotFound, "Operational alert not found.");
            }

            var actorUserId = RequireCurrentUserId();
            var now = DateTime.UtcNow;
            if (string.Equals(alert.WorkflowStatus, "resolved", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(action, "resolved", StringComparison.OrdinalIgnoreCase))
            {
                throw new TrustCommandException(StatusCodes.Status409Conflict, "Resolved alerts cannot be modified.");
            }

            TrustBankAccount? account = null;
            if (!string.IsNullOrWhiteSpace(alert.TrustAccountId))
            {
                account = await _context.TrustBankAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == alert.TrustAccountId, ct);
            }

            switch (action)
            {
                case "acknowledged":
                    alert.WorkflowStatus = "acknowledged";
                    alert.AcknowledgedBy = actorUserId;
                    alert.AcknowledgedAt = now;
                    if (string.IsNullOrWhiteSpace(alert.AssignedUserId))
                    {
                        alert.AssignedUserId = actorUserId;
                    }
                    break;
                case "assigned":
                    if (string.IsNullOrWhiteSpace(assigneeUserId))
                    {
                        throw new TrustCommandException(StatusCodes.Status400BadRequest, "Assignee user id is required.");
                    }

                    alert.AssignedUserId = assigneeUserId.Trim();
                    if (string.Equals(alert.WorkflowStatus, "open", StringComparison.OrdinalIgnoreCase))
                    {
                        alert.WorkflowStatus = "assigned";
                    }
                    break;
                case "escalated":
                    alert.WorkflowStatus = "escalated";
                    alert.EscalatedBy = actorUserId;
                    alert.EscalatedAt = now;
                    alert.AssignedUserId ??= ResolveEscalationAssignee(account);
                    break;
                case "resolved":
                    alert.WorkflowStatus = "resolved";
                    alert.ResolvedBy = actorUserId;
                    alert.ResolvedAt = now;
                    break;
                default:
                    throw new TrustCommandException(StatusCodes.Status400BadRequest, "Unsupported alert action.");
            }

            alert.UpdatedAt = now;
            AddAlertEvent(alert, action, actorUserId, notes, string.Equals(action, "assigned", StringComparison.OrdinalIgnoreCase)
                ? new { assignedUserId = alert.AssignedUserId }
                : null);
            if (string.Equals(action, "assigned", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(action, "escalated", StringComparison.OrdinalIgnoreCase))
            {
                QueueAlertNotifications(alert, account, action);
            }

            await _context.SaveChangesAsync(ct);
            var accountNameMap = await LoadAlertAccountNameMapAsync([alert.TrustAccountId], ct);
            return ToOperationalAlertRecordDto(alert, accountNameMap);
        }

        private void ApplyAlertSnapshot(TrustOperationalAlert record, TrustOperationalAlertDto computed, DateTime detectedAt)
        {
            record.AlertType = computed.AlertType;
            record.Severity = computed.Severity;
            record.TrustAccountId = computed.TrustAccountId;
            record.RelatedEntityType = computed.RelatedEntityType;
            record.RelatedEntityId = computed.RelatedEntityId;
            record.PeriodEnd = computed.PeriodEnd;
            record.Title = computed.Title;
            record.Summary = computed.Summary;
            record.ActionHint = computed.ActionHint;
            record.SourceStatus = computed.Status;
            record.OpenedAt = computed.OpenedAt;
            record.LastDetectedAt = detectedAt;
            record.UpdatedAt = detectedAt;
        }

        private static string BuildAlertKey(TrustOperationalAlertDto alert)
        {
            return string.Join("|", new[]
            {
                alert.AlertType.Trim().ToLowerInvariant(),
                alert.TrustAccountId?.Trim() ?? string.Empty,
                alert.RelatedEntityType?.Trim().ToLowerInvariant() ?? string.Empty,
                alert.RelatedEntityId?.Trim() ?? string.Empty,
                alert.PeriodEnd?.ToString("yyyyMMdd") ?? string.Empty
            });
        }

        private void AddAlertEvent(TrustOperationalAlert alert, string eventType, string? actorUserId, string? notes, object? metadata = null)
        {
            _context.TrustOperationalAlertEvents.Add(new TrustOperationalAlertEvent
            {
                Id = Guid.NewGuid().ToString(),
                TrustOperationalAlertId = alert.Id,
                EventType = eventType,
                ActorUserId = actorUserId,
                Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
                MetadataJson = metadata == null ? null : JsonSerializer.Serialize(metadata),
                CreatedAt = DateTime.UtcNow
            });
        }

        private int QueueAlertNotifications(TrustOperationalAlert alert, TrustBankAccount? account, string reason)
        {
            var now = DateTime.UtcNow;
            if ((reason == "detected" || reason == "reopened") &&
                alert.LastNotificationAt.HasValue &&
                alert.LastNotificationAt.Value >= now.AddHours(-12))
            {
                return 0;
            }

            var recipients = ResolveAlertRecipients(alert, account);
            if (recipients.Count == 0)
            {
                return 0;
            }

            foreach (var recipient in recipients)
            {
                _context.Notifications.Add(new Notification
                {
                    UserId = recipient,
                    Title = $"{(alert.Severity.Equals("critical", StringComparison.OrdinalIgnoreCase) ? "Critical" : "Trust")} compliance alert",
                    Message = $"{alert.Title}: {alert.Summary}",
                    Type = alert.Severity.Equals("critical", StringComparison.OrdinalIgnoreCase) ? "error" : "warning",
                    Link = "tab:trust",
                    CreatedAt = now
                });
            }

            alert.LastNotificationAt = now;
            alert.NotificationCount += recipients.Count;
            alert.UpdatedAt = now;
            AddAlertEvent(alert, "notification_sent", null, $"Sent to {recipients.Count} recipient(s).", new { reason, recipients });
            return recipients.Count;
        }

        private static List<string> ResolveAlertRecipients(TrustOperationalAlert alert, TrustBankAccount? account)
        {
            var recipients = new List<string>();
            if (!string.IsNullOrWhiteSpace(alert.AssignedUserId))
            {
                recipients.Add(alert.AssignedUserId.Trim());
            }

            if (!string.IsNullOrWhiteSpace(account?.ResponsibleLawyerUserId))
            {
                recipients.Add(account.ResponsibleLawyerUserId.Trim());
            }

            if (!string.IsNullOrWhiteSpace(account?.AllowedSignatoriesJson))
            {
                try
                {
                    var signatories = JsonSerializer.Deserialize<List<string>>(account.AllowedSignatoriesJson);
                    if (signatories != null)
                    {
                        recipients.AddRange(signatories.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()));
                    }
                }
                catch
                {
                    // ignore malformed signatory metadata
                }
            }

            return recipients.Distinct(StringComparer.Ordinal).ToList();
        }

        private static string? ResolveEscalationAssignee(TrustBankAccount? account)
        {
            if (!string.IsNullOrWhiteSpace(account?.ResponsibleLawyerUserId))
            {
                return account.ResponsibleLawyerUserId.Trim();
            }

            if (!string.IsNullOrWhiteSpace(account?.AllowedSignatoriesJson))
            {
                try
                {
                    var signatories = JsonSerializer.Deserialize<List<string>>(account.AllowedSignatoriesJson);
                    return signatories?.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim();
                }
                catch
                {
                    return null;
                }
            }

            return null;
        }

        private async Task<Dictionary<string, TrustBankAccount>> LoadAlertAccountMapAsync(IEnumerable<string?> accountIds, CancellationToken ct)
        {
            var ids = accountIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id!)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (ids.Count == 0)
            {
                return new Dictionary<string, TrustBankAccount>(StringComparer.Ordinal);
            }

            return await _context.TrustBankAccounts.AsNoTracking()
                .Where(a => ids.Contains(a.Id))
                .ToDictionaryAsync(a => a.Id, ct);
        }

        private async Task<Dictionary<string, string>> LoadAlertAccountNameMapAsync(IEnumerable<string?> accountIds, CancellationToken ct)
        {
            var ids = accountIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id!)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (ids.Count == 0)
            {
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }

            return await _context.TrustBankAccounts.AsNoTracking()
                .Where(a => ids.Contains(a.Id))
                .ToDictionaryAsync(a => a.Id, a => a.Name, ct);
        }

        private static TrustOperationalAlertRecordDto ToOperationalAlertRecordDto(TrustOperationalAlert record, IReadOnlyDictionary<string, string> accountNameMap)
        {
            return new TrustOperationalAlertRecordDto
            {
                Id = record.Id,
                AlertKey = record.AlertKey,
                AlertType = record.AlertType,
                Severity = record.Severity,
                TrustAccountId = record.TrustAccountId,
                TrustAccountName = record.TrustAccountId != null && accountNameMap.TryGetValue(record.TrustAccountId, out var name) ? name : null,
                RelatedEntityType = record.RelatedEntityType,
                RelatedEntityId = record.RelatedEntityId,
                PeriodEnd = record.PeriodEnd,
                Title = record.Title,
                Summary = record.Summary,
                SourceStatus = record.SourceStatus,
                WorkflowStatus = record.WorkflowStatus,
                AssignedUserId = record.AssignedUserId,
                ActionHint = record.ActionHint,
                OpenedAt = record.OpenedAt,
                AgeDays = Math.Max(0, (DateTime.UtcNow.Date - record.OpenedAt.Date).Days),
                FirstDetectedAt = record.FirstDetectedAt,
                LastDetectedAt = record.LastDetectedAt,
                AcknowledgedBy = record.AcknowledgedBy,
                AcknowledgedAt = record.AcknowledgedAt,
                EscalatedBy = record.EscalatedBy,
                EscalatedAt = record.EscalatedAt,
                ResolvedBy = record.ResolvedBy,
                ResolvedAt = record.ResolvedAt,
                NotificationCount = record.NotificationCount
            };
        }

        private string RequireCurrentUserId()
        {
            var userId = _httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? _httpContextAccessor.HttpContext?.User.FindFirstValue("sub");
            if (string.IsNullOrWhiteSpace(userId))
            {
                throw new TrustCommandException(StatusCodes.Status401Unauthorized, "Current user could not be resolved.");
            }

            return userId;
        }

        private static int GetSeverityRank(string? severity)
        {
            return (severity ?? string.Empty).ToLowerInvariant() switch
            {
                "critical" => 3,
                "warning" => 2,
                "info" => 1,
                _ => 0
            };
        }

        private static DateTime GetExpectedCompletedPeriodEnd(DateTime cutoff, string? cadence)
        {
            var normalizedCadence = (cadence ?? "monthly").Trim().ToLowerInvariant();
            var today = cutoff.Date;

            return normalizedCadence switch
            {
                "daily" => today.AddDays(-1),
                "weekly" => EndOfWeek(today.AddDays(-7)),
                "quarterly" => EndOfQuarter(today.AddMonths(-3)),
                _ => EndOfMonth(today.AddMonths(-1))
            };
        }

        private static DateTime GetCloseGraceDate(DateTime periodEnd, string? cadence)
        {
            var normalizedCadence = (cadence ?? "monthly").Trim().ToLowerInvariant();
            var graceDays = normalizedCadence switch
            {
                "daily" => 1,
                "weekly" => 2,
                "quarterly" => 10,
                _ => 5
            };

            return periodEnd.Date.AddDays(graceDays);
        }

        private static DateTime EndOfMonth(DateTime value)
        {
            return new DateTime(value.Year, value.Month, DateTime.DaysInMonth(value.Year, value.Month), 0, 0, 0, DateTimeKind.Utc);
        }

        private static DateTime EndOfQuarter(DateTime value)
        {
            var quarter = ((value.Month - 1) / 3) + 1;
            var endMonth = quarter * 3;
            return new DateTime(value.Year, endMonth, DateTime.DaysInMonth(value.Year, endMonth), 0, 0, 0, DateTimeKind.Utc);
        }

        private static DateTime EndOfWeek(DateTime value)
        {
            var diff = DayOfWeek.Saturday - value.DayOfWeek;
            if (diff < 0)
            {
                diff += 7;
            }

            return value.AddDays(diff).Date;
        }
    }

    public class TrustComplianceSummary
    {
        public string TrustAccountId { get; set; } = string.Empty;
        public DateTime AsOfUtc { get; set; }
        public decimal TrustBalance { get; set; }
        public decimal LedgerTotal { get; set; }
        public decimal LedgerDiscrepancy { get; set; }
        public decimal? BankStatementBalance { get; set; }
        public decimal? BankDiscrepancy { get; set; }
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
        public decimal Balance { get; set; }
    }

    public class TrustOperationalAlertSummary
    {
        public DateTime GeneratedAtUtc { get; set; }
        public int TotalCount { get; set; }
        public int CriticalCount { get; set; }
        public int WarningCount { get; set; }
        public int AccountsImpacted { get; set; }
        public List<TrustOperationalAlertDto> Alerts { get; set; } = new();
    }

    public class TrustOperationalAlertDto
    {
        public string? AlertId { get; set; }
        public string? AlertKey { get; set; }
        public string AlertType { get; set; } = string.Empty;
        public string Severity { get; set; } = "warning";
        public string? TrustAccountId { get; set; }
        public string? TrustAccountName { get; set; }
        public string? RelatedEntityType { get; set; }
        public string? RelatedEntityId { get; set; }
        public DateTime? PeriodEnd { get; set; }
        public DateTime OpenedAt { get; set; }
        public int AgeDays { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string Status { get; set; } = "open";
        public string WorkflowStatus { get; set; } = "open";
        public string? AssignedUserId { get; set; }
        public DateTime? FirstDetectedAt { get; set; }
        public DateTime? LastDetectedAt { get; set; }
        public string? AcknowledgedBy { get; set; }
        public DateTime? AcknowledgedAt { get; set; }
        public string? EscalatedBy { get; set; }
        public DateTime? EscalatedAt { get; set; }
        public string? ResolvedBy { get; set; }
        public DateTime? ResolvedAt { get; set; }
        public int NotificationCount { get; set; }
        public string? ActionHint { get; set; }
    }
}
