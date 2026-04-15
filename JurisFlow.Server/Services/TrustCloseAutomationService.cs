using System.Text.Json;
using JurisFlow.Server.Contracts;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace JurisFlow.Server.Services
{
    public sealed partial class TrustCloseAutomationService
    {
        private const string SystemActor = "system:trust-close-automation";
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = false };

        private readonly JurisFlowDbContext _context;
        private readonly TrustPolicyResolverService _policyResolver;
        private readonly IConfiguration _configuration;

        public TrustCloseAutomationService(
            JurisFlowDbContext context,
            TrustPolicyResolverService policyResolver,
            IConfiguration configuration)
        {
            _context = context;
            _policyResolver = policyResolver;
            _configuration = configuration;
        }

        public async Task<TrustCloseForecastSummaryDto> GetCloseForecastsAsync(
            string? trustAccountId = null,
            string? readinessStatus = null,
            bool actionableOnly = false,
            CancellationToken ct = default)
        {
            var query = _context.TrustCloseForecastSnapshots.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(trustAccountId))
            {
                query = query.Where(x => x.TrustAccountId == trustAccountId);
            }

            if (!string.IsNullOrWhiteSpace(readinessStatus))
            {
                var normalizedReadiness = readinessStatus.Trim().ToLowerInvariant();
                query = query.Where(x => x.ReadinessStatus == normalizedReadiness);
            }

            if (actionableOnly)
            {
                query = query.Where(x => x.ReadinessStatus != "ready" && x.ReadinessStatus != "closed");
            }

            var rows = await query
                .OrderBy(x => x.CloseDueAt)
                .ThenByDescending(x => x.PeriodEnd)
                .Take(200)
                .ToListAsync(ct);
            var accountNameMap = rows.Count == 0
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : await _context.TrustBankAccounts.AsNoTracking()
                    .Where(a => rows.Select(r => r.TrustAccountId).Contains(a.Id))
                    .ToDictionaryAsync(a => a.Id, a => a.Name, ct);

            return BuildSummary(rows, accountNameMap);
        }

        public async Task<TrustCloseForecastSyncResultDto> SyncCloseForecastsAsync(bool generateDraftBundles = true, CancellationToken ct = default)
        {
            var now = DateTime.UtcNow;
            var reminderLeadDays = Math.Clamp(_configuration.GetValue("Trust:CloseAutomation:ReminderLeadDays", 3), 1, 14);
            var draftLeadDays = Math.Clamp(_configuration.GetValue("Trust:CloseAutomation:DraftBundleLeadDays", 2), 0, 14);
            var escalationDelayHours = Math.Clamp(_configuration.GetValue("Trust:CloseAutomation:EscalationDelayHours", 24), 1, 24 * 14);

            var accounts = await _context.TrustBankAccounts
                .Where(a => a.Status == TrustAccountStatus.ACTIVE)
                .OrderBy(a => a.Name)
                .ToListAsync(ct);
            if (accounts.Count == 0)
            {
                return new TrustCloseForecastSyncResultDto { GeneratedAtUtc = now };
            }

            var accountIds = accounts.Select(a => a.Id).ToList();
            var expectedPeriods = accounts
                .Select(a => new { a.Id, PeriodEnd = GetExpectedCompletedPeriodEnd(now, a.StatementCadence).Date })
                .DistinctBy(x => $"{x.Id}:{x.PeriodEnd:yyyyMMdd}")
                .ToList();
            var minPeriodEnd = expectedPeriods.Min(x => x.PeriodEnd).AddMonths(-1);

            var statements = await _context.TrustStatementImports.AsNoTracking()
                .Where(x => accountIds.Contains(x.TrustAccountId) &&
                            x.PeriodEnd >= minPeriodEnd &&
                            x.Status != "duplicate" &&
                            x.Status != "superseded")
                .ToListAsync(ct);
            var packets = await _context.TrustReconciliationPackets.AsNoTracking()
                .Where(x => accountIds.Contains(x.TrustAccountId) && x.IsCanonical && x.PeriodEnd >= minPeriodEnd)
                .ToListAsync(ct);
            var closes = await _context.TrustMonthCloses.AsNoTracking()
                .Where(x => accountIds.Contains(x.TrustAccountId) && x.IsCanonical && x.PeriodEnd >= minPeriodEnd)
                .ToListAsync(ct);
            var outstandingItems = await _context.TrustOutstandingItems.AsNoTracking()
                .Where(x => accountIds.Contains(x.TrustAccountId) && x.Status == "open")
                .ToListAsync(ct);
            var unclearedFunds = await _context.TrustJournalEntries.AsNoTracking()
                .Where(j => accountIds.Contains(j.TrustAccountId) &&
                            j.AvailabilityClass == "uncleared" &&
                            j.EffectiveAt <= now &&
                            j.Amount > 0m)
                .GroupBy(j => j.TrustAccountId)
                .Select(g => new UnclearedAggregate(g.Key, g.Sum(x => x.Amount), g.Count(), g.Min(x => x.EffectiveAt)))
                .ToListAsync(ct);
            var existingSnapshots = await _context.TrustCloseForecastSnapshots
                .Where(x => accountIds.Contains(x.TrustAccountId))
                .ToListAsync(ct);
            var existingSnapshotMap = existingSnapshots.ToDictionary(x => BuildSnapshotKey(x.TrustAccountId, x.PeriodEnd), StringComparer.Ordinal);
            var existingExports = await _context.TrustComplianceExports
                .Where(x => x.TrustAccountId != null && accountIds.Contains(x.TrustAccountId) && x.ExportType == "compliance_bundle_manifest")
                .OrderByDescending(x => x.GeneratedAt)
                .ToListAsync(ct);

            var createdCount = 0;
            var updatedCount = 0;
            var reminderCount = 0;
            var escalatedCount = 0;
            var draftBundleCount = 0;
            var resolvedInboxCount = 0;

            foreach (var account in accounts)
            {
                var resolvedPolicy = await _policyResolver.ResolveEffectivePolicyAsync(account.Id, ct);
                var periodEnd = GetExpectedCompletedPeriodEnd(now, account.StatementCadence).Date;
                var periodStart = GetPeriodStart(periodEnd, account.StatementCadence);
                var dueAt = ResolveCloseDueAt(periodEnd, account.StatementCadence, resolvedPolicy.Policy.MonthlyCloseCadenceDays);

                var statement = statements.Where(x => x.TrustAccountId == account.Id && x.PeriodEnd.Date == periodEnd)
                    .OrderByDescending(x => x.ImportedAt)
                    .FirstOrDefault();
                var packet = packets.Where(x => x.TrustAccountId == account.Id && x.PeriodEnd.Date == periodEnd)
                    .OrderByDescending(x => x.VersionNumber)
                    .ThenByDescending(x => x.PreparedAt)
                    .FirstOrDefault();
                var close = closes.Where(x => x.TrustAccountId == account.Id && x.PeriodEnd.Date == periodEnd)
                    .OrderByDescending(x => x.VersionNumber)
                    .ThenByDescending(x => x.PreparedAt)
                    .FirstOrDefault();
                var accountOutstanding = outstandingItems.Where(x => x.TrustAccountId == account.Id).ToList();
                var oldestOutstandingAgeDays = accountOutstanding.Count == 0
                    ? (int?)null
                    : Math.Max(0, (now.Date - accountOutstanding.Min(x => x.OccurredAt.Date)).Days);
                var outstandingCount = accountOutstanding.Count;
                var openExceptionCount = close?.OpenExceptionCount ?? packet?.ExceptionCount ?? outstandingCount;
                var uncleared = unclearedFunds.FirstOrDefault(x => x.TrustAccountId == account.Id);
                var oldestUnclearedAgeDays = uncleared == null
                    ? (int?)null
                    : Math.Max(0, (now.Date - uncleared.OldestEffectiveAt.Date).Days);
                var templateState = ReadTemplateState(close?.SummaryJson);
                var missingSectionCount = templateState.MissingRequiredSections.Count;
                var missingAttestationCount = templateState.RequiredAttestations.Count(a => a.Required) -
                                              templateState.CompletedAttestations.Count(a => a.Accepted);
                var readiness = DetermineReadiness(now, dueAt, statement, packet, close, openExceptionCount, missingSectionCount, missingAttestationCount, reminderLeadDays);
                var recommendedAction = BuildRecommendedAction(readiness, statement, packet, close, openExceptionCount, missingSectionCount, missingAttestationCount, oldestUnclearedAgeDays);
                var draftEligible = DetermineDraftBundleEligibility(readiness, statement, packet, close, openExceptionCount, missingSectionCount, missingAttestationCount);

                var snapshotKey = BuildSnapshotKey(account.Id, periodEnd);
                if (!existingSnapshotMap.TryGetValue(snapshotKey, out var snapshot))
                {
                    snapshot = new TrustCloseForecastSnapshot
                    {
                        Id = Guid.NewGuid().ToString(),
                        TrustAccountId = account.Id,
                        CreatedAt = now
                    };
                    _context.TrustCloseForecastSnapshots.Add(snapshot);
                    existingSnapshotMap[snapshotKey] = snapshot;
                    createdCount++;
                }
                else
                {
                    updatedCount++;
                }

                ApplySnapshot(
                    snapshot,
                    account,
                    now,
                    periodStart,
                    periodEnd,
                    dueAt,
                    statement,
                    packet,
                    close,
                    openExceptionCount,
                    outstandingCount,
                    missingSectionCount,
                    Math.Max(0, missingAttestationCount),
                    uncleared?.Amount ?? 0m,
                    uncleared?.EntryCount ?? 0,
                    oldestOutstandingAgeDays,
                    oldestUnclearedAgeDays,
                    readiness,
                    draftEligible,
                    recommendedAction);

                var matchingExports = existingExports
                    .Where(x => x.TrustAccountId == account.Id &&
                                x.TrustMonthCloseId == close?.Id &&
                                x.TrustReconciliationPacketId == packet?.Id)
                    .ToList();
                var existingDraft = matchingExports.FirstOrDefault(x => x.Status == "draft");
                snapshot.DraftBundleManifestExportId = existingDraft?.Id;
                snapshot.DraftBundleGeneratedAt = existingDraft?.GeneratedAt;

                if (generateDraftBundles &&
                    draftEligible &&
                    existingDraft == null &&
                    (dueAt.Date - now.Date).Days <= draftLeadDays)
                {
                    var draft = BuildDraftBundleManifest(account, snapshot, packet, close, statement, now);
                    _context.TrustComplianceExports.Add(draft);
                    existingExports.Insert(0, draft);
                    snapshot.DraftBundleManifestExportId = draft.Id;
                    snapshot.DraftBundleGeneratedAt = draft.GeneratedAt;
                    draftBundleCount++;
                }

                if (ShouldSendReminder(snapshot, now, reminderLeadDays))
                {
                    reminderCount += QueueReminderNotifications(snapshot, account, now);
                    snapshot.ReminderCount += 1;
                    snapshot.LastReminderAt = now;
                    snapshot.NextReminderAt = now.AddHours(snapshot.Severity == "critical" ? 24 : 72);
                }
                else if (!snapshot.NextReminderAt.HasValue && snapshot.ReadinessStatus is "at_risk" or "blocked" or "overdue")
                {
                    snapshot.NextReminderAt = dueAt <= now ? now : dueAt.AddDays(-Math.Max(1, reminderLeadDays));
                }

                if (ShouldEscalate(snapshot, now, escalationDelayHours))
                {
                    escalatedCount += await EscalateForecastInboxAsync(snapshot, account, recommendedAction, dueAt, ct);
                    snapshot.EscalatedAt ??= now;
                }
                else
                {
                    resolvedInboxCount += await SyncForecastInboxAsync(snapshot, account, recommendedAction, dueAt, ct);
                }
            }

            await _context.SaveChangesAsync(ct);

            return new TrustCloseForecastSyncResultDto
            {
                GeneratedAtUtc = now,
                SnapshotCount = existingSnapshotMap.Count,
                CreatedCount = createdCount,
                UpdatedCount = updatedCount,
                ReminderCount = reminderCount,
                EscalatedCount = escalatedCount,
                DraftBundleCount = draftBundleCount,
                ResolvedInboxCount = resolvedInboxCount
            };
        }

        private void ApplySnapshot(
            TrustCloseForecastSnapshot snapshot,
            TrustBankAccount account,
            DateTime now,
            DateTime periodStart,
            DateTime periodEnd,
            DateTime dueAt,
            TrustStatementImport? statement,
            TrustReconciliationPacket? packet,
            TrustMonthClose? close,
            int openExceptionCount,
            int outstandingCount,
            int missingSectionCount,
            int missingAttestationCount,
            decimal unclearedBalance,
            int unclearedEntryCount,
            int? oldestOutstandingAgeDays,
            int? oldestUnclearedAgeDays,
            ForecastReadiness readiness,
            bool draftEligible,
            string recommendedAction)
        {
            snapshot.Jurisdiction = account.Jurisdiction;
            snapshot.OfficeId = account.OfficeId;
            snapshot.StatementCadence = string.IsNullOrWhiteSpace(account.StatementCadence) ? "monthly" : account.StatementCadence.Trim().ToLowerInvariant();
            snapshot.PeriodStart = periodStart;
            snapshot.PeriodEnd = periodEnd;
            snapshot.CloseDueAt = dueAt;
            snapshot.ReadinessStatus = readiness.Status;
            snapshot.Severity = readiness.Severity;
            snapshot.MissingStatementImport = statement == null;
            snapshot.LatestStatementImportId = statement?.Id;
            snapshot.StatementImportedAt = statement?.ImportedAt;
            snapshot.HasCanonicalPacket = packet != null;
            snapshot.CanonicalPacketId = packet?.Id;
            snapshot.PacketStatus = packet?.Status;
            snapshot.HasCanonicalMonthClose = close != null;
            snapshot.CanonicalMonthCloseId = close?.Id;
            snapshot.MonthCloseStatus = close?.Status;
            snapshot.OpenExceptionCount = openExceptionCount;
            snapshot.OutstandingItemCount = outstandingCount;
            snapshot.MissingRequiredSectionCount = missingSectionCount;
            snapshot.MissingAttestationCount = missingAttestationCount;
            snapshot.UnclearedBalance = unclearedBalance;
            snapshot.UnclearedEntryCount = unclearedEntryCount;
            snapshot.OldestOutstandingAgeDays = oldestOutstandingAgeDays;
            snapshot.OldestUnclearedAgeDays = oldestUnclearedAgeDays;
            snapshot.DraftBundleEligible = draftEligible;
            snapshot.RecommendedAction = recommendedAction;
            snapshot.LastAutomationRunAt = now;
            snapshot.LastAutomationRunBy = CurrentActor();
            snapshot.SummaryJson = JsonSerializer.Serialize(new
            {
                dueAt,
                readiness = readiness.Status,
                severity = readiness.Severity,
                daysUntilDue = (dueAt.Date - now.Date).Days,
                missingStatementImport = statement == null,
                openExceptionCount,
                outstandingItemCount = outstandingCount,
                missingRequiredSectionCount = missingSectionCount,
                missingAttestationCount,
                unclearedBalance,
                oldestUnclearedAgeDays
            }, JsonOptions);
            snapshot.MetadataJson = JsonSerializer.Serialize(new
            {
                closeId = close?.Id,
                packetId = packet?.Id,
                statementImportId = statement?.Id,
                draftBundleEligible = draftEligible
            }, JsonOptions);
            snapshot.UpdatedAt = now;
        }

        private async Task<int> SyncForecastInboxAsync(
            TrustCloseForecastSnapshot snapshot,
            TrustBankAccount account,
            string? recommendedAction,
            DateTime dueAt,
            CancellationToken ct)
        {
            var existing = await _context.TrustOpsInboxItems
                .FirstOrDefaultAsync(x => x.TrustCloseForecastSnapshotId == snapshot.Id, ct);
            if (snapshot.ReadinessStatus is "ready" or "closed")
            {
                if (existing != null && !string.Equals(existing.WorkflowStatus, "resolved", StringComparison.OrdinalIgnoreCase))
                {
                    existing.WorkflowStatus = "resolved";
                    existing.LastActionAt = DateTime.UtcNow;
                    existing.UpdatedAt = DateTime.UtcNow;
                    _context.TrustOpsInboxEvents.Add(new TrustOpsInboxEvent
                    {
                        Id = Guid.NewGuid().ToString(),
                        TrustOpsInboxItemId = existing.Id,
                        EventType = "auto_resolved",
                        ActorUserId = SystemActor,
                        Notes = "Close forecast no longer requires routed action.",
                        CreatedAt = DateTime.UtcNow
                    });
                    return 1;
                }

                return 0;
            }

            var now = DateTime.UtcNow;
            if (existing == null)
            {
                existing = new TrustOpsInboxItem
                {
                    Id = Guid.NewGuid().ToString(),
                    TrustCloseForecastSnapshotId = snapshot.Id,
                    ItemType = "close_forecast",
                    BlockerGroup = "close_blocker",
                    CreatedAt = now,
                    OpenedAt = now
                };
                _context.TrustOpsInboxItems.Add(existing);
                _context.TrustOpsInboxEvents.Add(new TrustOpsInboxEvent
                {
                    Id = Guid.NewGuid().ToString(),
                    TrustOpsInboxItemId = existing.Id,
                    EventType = "detected",
                    ActorUserId = SystemActor,
                    Notes = "Ops inbox item created from close forecast automation.",
                    CreatedAt = now
                });
            }

            existing.Severity = snapshot.Severity;
            existing.TrustAccountId = account.Id;
            existing.Jurisdiction = account.Jurisdiction;
            existing.OfficeId = account.OfficeId;
            existing.DueAt = dueAt;
            existing.LastDetectedAt = now;
            existing.Title = snapshot.ReadinessStatus switch
            {
                "overdue" => "Trust month-close is overdue",
                "blocked" => "Trust month-close is blocked",
                _ => "Trust month-close needs readiness review"
            };
            existing.Summary = recommendedAction ?? "Review trust close readiness.";
            existing.ActionHint = snapshot.DraftBundleManifestExportId == null
                ? "Review statement import, packet, exceptions, and month-close prerequisites."
                : "Review the pre-close draft bundle and finish signoff.";
            existing.SuggestedExportType = snapshot.DraftBundleManifestExportId == null ? "month_close_packet" : "compliance_bundle_manifest";
            existing.SuggestedRoute = $"trust/close-forecast/{snapshot.Id}";
            existing.MetadataJson = JsonSerializer.Serialize(new
            {
                periodStart = snapshot.PeriodStart,
                periodEnd = snapshot.PeriodEnd,
                readinessStatus = snapshot.ReadinessStatus,
                monthCloseId = snapshot.CanonicalMonthCloseId,
                packetId = snapshot.CanonicalPacketId,
                draftBundleManifestExportId = snapshot.DraftBundleManifestExportId
            }, JsonOptions);
            existing.UpdatedAt = now;
            if (existing.WorkflowStatus == "resolved")
            {
                existing.WorkflowStatus = "open";
            }

            return 0;
        }

        private async Task<int> EscalateForecastInboxAsync(
            TrustCloseForecastSnapshot snapshot,
            TrustBankAccount account,
            string? recommendedAction,
            DateTime dueAt,
            CancellationToken ct)
        {
            await SyncForecastInboxAsync(snapshot, account, recommendedAction, dueAt, ct);
            var existing = await _context.TrustOpsInboxItems
                .FirstOrDefaultAsync(x => x.TrustCloseForecastSnapshotId == snapshot.Id, ct);
            if (existing == null || string.Equals(existing.WorkflowStatus, "escalated", StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            existing.WorkflowStatus = "escalated";
            existing.AssignedUserId ??= ResolvePrimaryRecipient(account);
            existing.LastActionAt = DateTime.UtcNow;
            existing.UpdatedAt = DateTime.UtcNow;
            _context.TrustOpsInboxEvents.Add(new TrustOpsInboxEvent
            {
                Id = Guid.NewGuid().ToString(),
                TrustOpsInboxItemId = existing.Id,
                EventType = "automation_escalated",
                ActorUserId = SystemActor,
                Notes = "Close forecast exceeded automation escalation threshold.",
                MetadataJson = JsonSerializer.Serialize(new { dueAt, readinessStatus = snapshot.ReadinessStatus }, JsonOptions),
                CreatedAt = DateTime.UtcNow
            });
            return 1;
        }

        private int QueueReminderNotifications(TrustCloseForecastSnapshot snapshot, TrustBankAccount account, DateTime now)
        {
            var recipients = ResolveRecipients(account);
            if (recipients.Count == 0)
            {
                return 0;
            }

            foreach (var recipient in recipients)
            {
                _context.Notifications.Add(new Notification
                {
                    UserId = recipient,
                    Title = snapshot.ReadinessStatus == "overdue" ? "Trust close overdue" : "Trust close readiness reminder",
                    Message = $"{account.Name}: {snapshot.RecommendedAction}",
                    Type = snapshot.Severity == "critical" ? "error" : "warning",
                    Link = "tab:trust",
                    CreatedAt = now
                });
            }

            return recipients.Count;
        }

        private static bool ShouldSendReminder(TrustCloseForecastSnapshot snapshot, DateTime now, int reminderLeadDays)
        {
            if (snapshot.ReadinessStatus is "ready" or "closed")
            {
                return false;
            }

            if (!snapshot.NextReminderAt.HasValue)
            {
                return snapshot.CloseDueAt <= now || (snapshot.CloseDueAt.Date - now.Date).Days <= reminderLeadDays;
            }

            return snapshot.NextReminderAt.Value <= now;
        }

        private static bool ShouldEscalate(TrustCloseForecastSnapshot snapshot, DateTime now, int escalationDelayHours)
        {
            if (snapshot.ReadinessStatus != "overdue" || snapshot.EscalatedAt.HasValue)
            {
                return false;
            }

            return snapshot.CloseDueAt.AddHours(escalationDelayHours) <= now;
        }

        private static bool DetermineDraftBundleEligibility(
            ForecastReadiness readiness,
            TrustStatementImport? statement,
            TrustReconciliationPacket? packet,
            TrustMonthClose? close,
            int openExceptionCount,
            int missingSectionCount,
            int missingAttestationCount)
        {
            if (readiness.Status == "closed")
            {
                return false;
            }

            return statement != null &&
                   (packet != null || close != null) &&
                   openExceptionCount <= 0 &&
                   missingSectionCount <= 0 &&
                   missingAttestationCount <= 0;
        }

        private static ForecastReadiness DetermineReadiness(
            DateTime now,
            DateTime dueAt,
            TrustStatementImport? statement,
            TrustReconciliationPacket? packet,
            TrustMonthClose? close,
            int openExceptionCount,
            int missingSectionCount,
            int missingAttestationCount,
            int reminderLeadDays)
        {
            if (close != null && string.Equals(close.Status, "closed", StringComparison.OrdinalIgnoreCase))
            {
                return new ForecastReadiness("closed", "info");
            }

            if (dueAt < now)
            {
                return new ForecastReadiness("overdue", "critical");
            }

            var blocked = statement == null || packet == null || openExceptionCount > 0 || missingSectionCount > 0 || missingAttestationCount > 0;
            if (blocked)
            {
                return new ForecastReadiness("blocked", dueAt.Date <= now.Date.AddDays(reminderLeadDays) ? "critical" : "warning");
            }

            if (close != null && close.Status is "ready_for_signoff" or "partially_signed")
            {
                return new ForecastReadiness("ready", "info");
            }

            return dueAt.Date <= now.Date.AddDays(reminderLeadDays)
                ? new ForecastReadiness("at_risk", "warning")
                : new ForecastReadiness("ready", "info");
        }

        private static string BuildRecommendedAction(
            ForecastReadiness readiness,
            TrustStatementImport? statement,
            TrustReconciliationPacket? packet,
            TrustMonthClose? close,
            int openExceptionCount,
            int missingSectionCount,
            int missingAttestationCount,
            int? oldestUnclearedAgeDays)
        {
            if (readiness.Status == "closed")
            {
                return "Canonical month-close is already signed off and closed.";
            }

            var actions = new List<string>();
            if (statement == null)
            {
                actions.Add("register and import the bank statement");
            }

            if (packet == null)
            {
                actions.Add("generate the canonical reconciliation packet");
            }

            if (openExceptionCount > 0)
            {
                actions.Add($"resolve {openExceptionCount} open reconciliation exception(s)");
            }

            if (missingSectionCount > 0)
            {
                actions.Add($"complete {missingSectionCount} missing required packet section(s)");
            }

            if (missingAttestationCount > 0)
            {
                actions.Add($"capture {missingAttestationCount} required close attestation(s)");
            }

            if (close == null && packet != null)
            {
                actions.Add("prepare the month-close from the canonical packet");
            }

            if (close != null && close.Status != "closed")
            {
                actions.Add("complete reviewer and responsible-lawyer signoff");
            }

            if (oldestUnclearedAgeDays.GetValueOrDefault() >= 7)
            {
                actions.Add($"review uncleared funds aging ({oldestUnclearedAgeDays} day old items)");
            }

            return actions.Count == 0
                ? "Review trust close readiness and finalize the pre-close bundle."
                : $"Close readiness requires action: {string.Join("; ", actions)}.";
        }

        private static TrustComplianceExport BuildDraftBundleManifest(
            TrustBankAccount account,
            TrustCloseForecastSnapshot snapshot,
            TrustReconciliationPacket? packet,
            TrustMonthClose? close,
            TrustStatementImport? statement,
            DateTime now)
        {
            var scopeKey = close?.Id ?? packet?.Id ?? account.Id;
            return new TrustComplianceExport
            {
                Id = Guid.NewGuid().ToString(),
                ExportType = "compliance_bundle_manifest",
                Format = "json",
                Status = "draft",
                TrustAccountId = account.Id,
                TrustMonthCloseId = close?.Id,
                TrustReconciliationPacketId = packet?.Id,
                FileName = $"trust-close-draft-bundle-{scopeKey}-{snapshot.PeriodEnd:yyyy-MM-dd}.json",
                ContentType = "application/json",
                SummaryJson = JsonSerializer.Serialize(new
                {
                    scope = "pre_close_bundle_draft",
                    readinessStatus = snapshot.ReadinessStatus,
                    dueAt = snapshot.CloseDueAt,
                    periodStart = snapshot.PeriodStart,
                    periodEnd = snapshot.PeriodEnd
                }, JsonOptions),
                PayloadJson = JsonSerializer.Serialize(new
                {
                    trustAccountId = account.Id,
                    trustAccountName = account.Name,
                    trustMonthCloseId = close?.Id,
                    trustReconciliationPacketId = packet?.Id,
                    statementImportId = statement?.Id,
                    readinessStatus = snapshot.ReadinessStatus,
                    recommendedAction = snapshot.RecommendedAction
                }, JsonOptions),
                GeneratedBy = SystemActor,
                IntegrityStatus = "unsigned",
                RetentionPolicyTag = "trust_default",
                RedactionProfile = "internal_unredacted",
                ProvenanceJson = JsonSerializer.Serialize(new
                {
                    source = "trust_close_automation",
                    snapshotId = snapshot.Id,
                    generatedAt = now
                }, JsonOptions),
                GeneratedAt = now,
                CreatedAt = now,
                UpdatedAt = now
            };
        }

        private static TrustCloseForecastSummaryDto BuildSummary(
            IReadOnlyCollection<TrustCloseForecastSnapshot> rows,
            IReadOnlyDictionary<string, string> accountNameMap)
        {
            var dtos = rows
                .OrderBy(x => x.CloseDueAt)
                .ThenByDescending(x => x.PeriodEnd)
                .Select(x =>
                {
                    var daysUntilDue = (x.CloseDueAt.Date - DateTime.UtcNow.Date).Days;
                    return new TrustCloseForecastSnapshotDto
                    {
                        Id = x.Id,
                        TrustAccountId = x.TrustAccountId,
                        TrustAccountName = accountNameMap.TryGetValue(x.TrustAccountId, out var name) ? name : null,
                        Jurisdiction = x.Jurisdiction,
                        OfficeId = x.OfficeId,
                        StatementCadence = x.StatementCadence,
                        PeriodStart = x.PeriodStart,
                        PeriodEnd = x.PeriodEnd,
                        CloseDueAt = x.CloseDueAt,
                        ReadinessStatus = x.ReadinessStatus,
                        Severity = x.Severity,
                        MissingStatementImport = x.MissingStatementImport,
                        LatestStatementImportId = x.LatestStatementImportId,
                        StatementImportedAt = x.StatementImportedAt,
                        HasCanonicalPacket = x.HasCanonicalPacket,
                        CanonicalPacketId = x.CanonicalPacketId,
                        PacketStatus = x.PacketStatus,
                        HasCanonicalMonthClose = x.HasCanonicalMonthClose,
                        CanonicalMonthCloseId = x.CanonicalMonthCloseId,
                        MonthCloseStatus = x.MonthCloseStatus,
                        OpenExceptionCount = x.OpenExceptionCount,
                        OutstandingItemCount = x.OutstandingItemCount,
                        MissingRequiredSectionCount = x.MissingRequiredSectionCount,
                        MissingAttestationCount = x.MissingAttestationCount,
                        UnclearedBalance = x.UnclearedBalance,
                        UnclearedEntryCount = x.UnclearedEntryCount,
                        OldestOutstandingAgeDays = x.OldestOutstandingAgeDays,
                        OldestUnclearedAgeDays = x.OldestUnclearedAgeDays,
                        DraftBundleEligible = x.DraftBundleEligible,
                        DraftBundleManifestExportId = x.DraftBundleManifestExportId,
                        DraftBundleGeneratedAt = x.DraftBundleGeneratedAt,
                        RecommendedAction = x.RecommendedAction,
                        ReminderCount = x.ReminderCount,
                        LastReminderAt = x.LastReminderAt,
                        NextReminderAt = x.NextReminderAt,
                        EscalatedAt = x.EscalatedAt,
                        LastAutomationRunAt = x.LastAutomationRunAt,
                        DaysUntilDue = daysUntilDue,
                        IsOverdue = daysUntilDue < 0 || string.Equals(x.ReadinessStatus, "overdue", StringComparison.OrdinalIgnoreCase)
                    };
                })
                .ToList();

            return new TrustCloseForecastSummaryDto
            {
                GeneratedAtUtc = DateTime.UtcNow,
                TotalCount = dtos.Count,
                ReadyCount = dtos.Count(x => x.ReadinessStatus == "ready"),
                AtRiskCount = dtos.Count(x => x.ReadinessStatus == "at_risk"),
                BlockedCount = dtos.Count(x => x.ReadinessStatus == "blocked"),
                OverdueCount = dtos.Count(x => x.ReadinessStatus == "overdue"),
                DraftBundleEligibleCount = dtos.Count(x => x.DraftBundleEligible),
                ReminderDueCount = dtos.Count(x => x.NextReminderAt.HasValue && x.NextReminderAt.Value <= DateTime.UtcNow && !x.IsOverdue),
                Snapshots = dtos
            };
        }

        private static string BuildSnapshotKey(string trustAccountId, DateTime periodEnd)
            => $"{trustAccountId}:{periodEnd:yyyyMMdd}";

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

        private static DateTime GetPeriodStart(DateTime periodEnd, string? cadence)
        {
            var normalizedCadence = (cadence ?? "monthly").Trim().ToLowerInvariant();
            return normalizedCadence switch
            {
                "daily" => periodEnd.Date,
                "weekly" => periodEnd.Date.AddDays(-6),
                "quarterly" => new DateTime(periodEnd.Year, (((periodEnd.Month - 1) / 3) * 3) + 1, 1, 0, 0, 0, DateTimeKind.Utc),
                _ => new DateTime(periodEnd.Year, periodEnd.Month, 1, 0, 0, 0, DateTimeKind.Utc)
            };
        }

        private static DateTime ResolveCloseDueAt(DateTime periodEnd, string? cadence, int monthlyCloseCadenceDays)
        {
            var normalizedCadence = (cadence ?? "monthly").Trim().ToLowerInvariant();
            var graceDays = normalizedCadence switch
            {
                "daily" => 1,
                "weekly" => 2,
                "quarterly" => 10,
                _ => Math.Clamp(monthlyCloseCadenceDays <= 0 ? 5 : monthlyCloseCadenceDays, 1, 45)
            };

            return periodEnd.Date.AddDays(graceDays);
        }

        private static DateTime EndOfMonth(DateTime value)
            => new(value.Year, value.Month, DateTime.DaysInMonth(value.Year, value.Month), 0, 0, 0, DateTimeKind.Utc);

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

        private static TemplateState ReadTemplateState(string? summaryJson)
        {
            if (string.IsNullOrWhiteSpace(summaryJson))
            {
                return TemplateState.Empty;
            }

            try
            {
                using var document = JsonDocument.Parse(summaryJson);
                if (!document.RootElement.TryGetProperty("packetTemplate", out var packetTemplate))
                {
                    return TemplateState.Empty;
                }

                var missingRequiredSections = packetTemplate.TryGetProperty("missingRequiredSections", out var missingElement)
                    ? JsonSerializer.Deserialize<List<string>>(missingElement.GetRawText(), JsonOptions) ?? new List<string>()
                    : new List<string>();
                var requiredAttestations = packetTemplate.TryGetProperty("requiredAttestations", out var requiredElement)
                    ? JsonSerializer.Deserialize<List<TrustPacketTemplateAttestationDto>>(requiredElement.GetRawText(), JsonOptions) ?? new List<TrustPacketTemplateAttestationDto>()
                    : new List<TrustPacketTemplateAttestationDto>();
                var completedAttestations = packetTemplate.TryGetProperty("completedAttestations", out var completedElement)
                    ? JsonSerializer.Deserialize<List<TrustMonthCloseAttestationDto>>(completedElement.GetRawText(), JsonOptions) ?? new List<TrustMonthCloseAttestationDto>()
                    : new List<TrustMonthCloseAttestationDto>();
                return new TemplateState(missingRequiredSections, requiredAttestations, completedAttestations);
            }
            catch
            {
                return TemplateState.Empty;
            }
        }

        private static List<string> ResolveRecipients(TrustBankAccount account)
        {
            var recipients = new List<string>();
            if (!string.IsNullOrWhiteSpace(account.ResponsibleLawyerUserId))
            {
                recipients.Add(account.ResponsibleLawyerUserId.Trim());
            }

            if (!string.IsNullOrWhiteSpace(account.AllowedSignatoriesJson))
            {
                try
                {
                    var signatories = JsonSerializer.Deserialize<List<string>>(account.AllowedSignatoriesJson, JsonOptions) ?? new List<string>();
                    recipients.AddRange(signatories.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()));
                }
                catch
                {
                    // ignore malformed governance data
                }
            }

            return recipients.Distinct(StringComparer.Ordinal).ToList();
        }

        private static string? ResolvePrimaryRecipient(TrustBankAccount account)
            => ResolveRecipients(account).FirstOrDefault();

        private string CurrentActor() => SystemActor;

        private sealed record ForecastReadiness(string Status, string Severity);
        private sealed record UnclearedAggregate(string TrustAccountId, decimal Amount, int EntryCount, DateTime OldestEffectiveAt);
        private sealed record TemplateState(
            List<string> MissingRequiredSections,
            List<TrustPacketTemplateAttestationDto> RequiredAttestations,
            List<TrustMonthCloseAttestationDto> CompletedAttestations)
        {
            public static TemplateState Empty => new(new List<string>(), new List<TrustPacketTemplateAttestationDto>(), new List<TrustMonthCloseAttestationDto>());
        }
    }
}
