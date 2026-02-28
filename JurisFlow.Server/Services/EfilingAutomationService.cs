using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace JurisFlow.Server.Services
{
    public sealed class EfilingAutomationService
    {
        private readonly JurisFlowDbContext _context;
        private readonly OutcomeFeePlannerService _outcomeFeePlanner;
        private readonly ClientTransparencyService _clientTransparencyService;
        private readonly TenantContext _tenantContext;
        private readonly ILogger<EfilingAutomationService> _logger;

        public EfilingAutomationService(
            JurisFlowDbContext context,
            OutcomeFeePlannerService outcomeFeePlanner,
            ClientTransparencyService clientTransparencyService,
            TenantContext tenantContext,
            ILogger<EfilingAutomationService> logger)
        {
            _context = context;
            _outcomeFeePlanner = outcomeFeePlanner;
            _clientTransparencyService = clientTransparencyService;
            _tenantContext = tenantContext;
            _logger = logger;
        }

        public async Task<EfilingDocketAutomationResult> ProcessDocketSyncAutomationAsync(
            string connectionId,
            IReadOnlyCollection<string> docketEntryIds,
            CancellationToken cancellationToken)
        {
            if (docketEntryIds.Count == 0)
            {
                return new EfilingDocketAutomationResult();
            }

            var ids = docketEntryIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToArray();
            if (ids.Length == 0)
            {
                return new EfilingDocketAutomationResult();
            }

            var entries = await TenantScopeIfAvailable(_context.CourtDocketEntries)
                .Where(d => ids.Contains(d.Id))
                .ToListAsync(cancellationToken);

            return await RunDocketAutomationAsync(new EfilingDocketAutomationRequest
            {
                ConnectionId = connectionId,
                Limit = ids.Length,
                DocketEntryIds = ids.ToList()
            }, cancellationToken);
        }

        public async Task<EfilingDocketAutomationResult> RunDocketAutomationAsync(
            EfilingDocketAutomationRequest request,
            CancellationToken cancellationToken)
        {
            var query = TenantScopeIfAvailable(_context.CourtDocketEntries).AsQueryable();
            if (request.DocketEntryIds is { Count: > 0 })
            {
                var ids = request.DocketEntryIds.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.Ordinal).ToArray();
                query = query.Where(d => ids.Contains(d.Id));
            }
            if (!string.IsNullOrWhiteSpace(request.MatterId))
            {
                query = query.Where(d => d.MatterId == request.MatterId);
            }
            if (!string.IsNullOrWhiteSpace(request.ProviderKey))
            {
                var providerKey = request.ProviderKey.Trim().ToLowerInvariant();
                query = query.Where(d => d.ProviderKey == providerKey);
            }

            var limit = Math.Clamp(request.Limit ?? 25, 1, 200);
            var entries = await query
                .OrderByDescending(d => d.ModifiedAt ?? d.LastSeenAt)
                .Take(limit)
                .ToListAsync(cancellationToken);

            if (entries.Count == 0)
            {
                return new EfilingDocketAutomationResult();
            }

            var matterIds = entries.Where(e => !string.IsNullOrWhiteSpace(e.MatterId)).Select(e => e.MatterId!).Distinct(StringComparer.Ordinal).ToArray();
            var matters = matterIds.Length == 0
                ? new Dictionary<string, Matter>(StringComparer.Ordinal)
                : await TenantScopeIfAvailable(_context.Matters).Where(m => matterIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, StringComparer.Ordinal, cancellationToken);

            var connectionId = string.IsNullOrWhiteSpace(request.ConnectionId) ? "manual-efiling" : request.ConnectionId!;
            var externalIds = entries.Where(e => !string.IsNullOrWhiteSpace(e.ExternalDocketId)).Select(e => e.ExternalDocketId!).Distinct(StringComparer.Ordinal).ToArray();
            var existingLinks = externalIds.Length == 0
                ? new List<IntegrationEntityLink>()
                : await TenantScopeIfAvailable(_context.IntegrationEntityLinks)
                    .Where(l => l.ConnectionId == connectionId &&
                                l.ProviderKey == IntegrationProviderKeys.CourtListenerDockets &&
                                externalIds.Contains(l.ExternalEntityId))
                    .ToListAsync(cancellationToken);
            var linkMap = existingLinks.ToDictionary(l => $"{l.ExternalEntityId}|{l.ExternalEntityType}", StringComparer.Ordinal);

            var createdTasks = 0;
            var createdDeadlines = 0;
            var createdReviews = 0;
            foreach (var entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry.MatterId) || !matters.TryGetValue(entry.MatterId, out var matter))
                {
                    createdReviews += await QueueDocketReviewAsync(entry, "docket_matter_link_review", "high", "Docket update requires matter link", cancellationToken);
                    continue;
                }

                createdTasks += await EnsureDocketTaskAsync(connectionId, entry, matter, linkMap, cancellationToken);
                createdDeadlines += await EnsureDocketDeadlinesAsync(connectionId, entry, matter, linkMap, cancellationToken);
            }

            if (_context.ChangeTracker.HasChanges())
            {
                await _context.SaveChangesAsync(cancellationToken);
            }

            await TryTriggerOutcomeFeePlannerForDocketSyncAsync(entries, request.ConnectionId, cancellationToken);

            return new EfilingDocketAutomationResult
            {
                Processed = entries.Count,
                TasksCreated = createdTasks,
                DeadlinesCreated = createdDeadlines,
                ReviewsQueued = createdReviews
            };
        }

        public async Task<EfilingSubmissionSignalResult> ProcessSubmissionSyncSignalsAsync(
            string connectionId,
            IReadOnlyCollection<EfilingSubmissionSyncSignal> signals,
            CancellationToken cancellationToken)
        {
            if (signals.Count == 0)
            {
                return new EfilingSubmissionSignalResult();
            }

            var ids = signals.Where(s => !string.IsNullOrWhiteSpace(s.SubmissionId)).Select(s => s.SubmissionId!).Distinct(StringComparer.Ordinal).ToArray();
            var submissions = ids.Length == 0
                ? new Dictionary<string, EfilingSubmission>(StringComparer.Ordinal)
                : await TenantScopeIfAvailable(_context.EfilingSubmissions).Where(s => ids.Contains(s.Id)).ToDictionaryAsync(s => s.Id, StringComparer.Ordinal, cancellationToken);

            var reviews = 0;
            var tasks = 0;
            var outbox = 0;
            foreach (var signal in signals)
            {
                if (!submissions.TryGetValue(signal.SubmissionId, out var submission))
                {
                    continue;
                }

                var prev = NormalizeSubmissionStatus(signal.PreviousStatus);
                var curr = NormalizeSubmissionStatus(signal.CurrentStatus ?? submission.Status);
                if (!string.Equals(prev, curr, StringComparison.Ordinal))
                {
                    outbox += await QueueSubmissionStatusOutboxAsync(connectionId, submission, curr, cancellationToken);
                }

                if (curr == "rejected" || !string.IsNullOrWhiteSpace(signal.CurrentRejectionReason))
                {
                    reviews += await QueueSubmissionReviewAsync(connectionId, submission, "efile_rejection_repair", "high", "Rejected filing requires repair", signal.CurrentRejectionReason ?? submission.RejectionReason, cancellationToken);
                    tasks += await EnsureSubmissionTaskAsync(connectionId, submission, "efiling_rejection_task", "Repair rejected filing", cancellationToken);
                }

                if (curr == "accepted")
                {
                    reviews += await QueueSubmissionReviewAsync(connectionId, submission, "efile_notice_return_review", "medium", "Accepted filing: review notice/stamped copies", "Review returned notices/stamped copies and link artifacts to matter.", cancellationToken);
                }
            }

            if (_context.ChangeTracker.HasChanges())
            {
                await _context.SaveChangesAsync(cancellationToken);
            }

            return new EfilingSubmissionSignalResult { Processed = signals.Count, ReviewsQueued = reviews, TasksQueued = tasks, OutboxQueued = outbox };
        }

        private async Task<int> EnsureDocketTaskAsync(string connectionId, CourtDocketEntry entry, Matter matter, IDictionary<string, IntegrationEntityLink> linkMap, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(entry.ExternalDocketId)) return 0;
            var version = (entry.ModifiedAt ?? entry.LastSeenAt).Ticks.ToString();
            var key = $"{entry.ExternalDocketId}|docket_review_task";
            if (linkMap.TryGetValue(key, out var existing) && string.Equals(existing.ExternalVersion, version, StringComparison.Ordinal)) return 0;

            JurisFlow.Server.Models.Task? task = null;
            if (existing != null) task = await TenantScopeIfAvailable(_context.Tasks).FirstOrDefaultAsync(t => t.Id == existing.LocalEntityId, ct);
            if (task == null)
            {
                task = new JurisFlow.Server.Models.Task
                {
                    Id = Guid.NewGuid().ToString(),
                    MatterId = matter.Id,
                    Title = $"Review docket update: {entry.DocketNumber ?? matter.CaseNumber}",
                    Description = $"Docket sync update. ExternalDocketId={entry.ExternalDocketId}; Case={entry.CaseName}; Court={entry.Court}; Source={entry.SourceUrl}",
                    Priority = "High",
                    Status = "To Do",
                    DueDate = DateTime.UtcNow.AddDays(1),
                    ReminderAt = DateTime.UtcNow.AddHours(4),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _context.Tasks.Add(task);
            }
            else
            {
                task.Description = $"Docket sync update. ExternalDocketId={entry.ExternalDocketId}; Case={entry.CaseName}; Court={entry.Court}; Source={entry.SourceUrl}";
                task.UpdatedAt = DateTime.UtcNow;
                task.Status = "To Do";
            }

            UpsertLink(linkMap, connectionId, IntegrationProviderKeys.CourtListenerDockets, "task", task.Id, "docket_review_task", entry.ExternalDocketId, version);
            return existing == null ? 1 : 0;
        }

        private async Task<int> EnsureDocketDeadlinesAsync(string connectionId, CourtDocketEntry entry, Matter matter, IDictionary<string, IntegrationEntityLink> linkMap, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(entry.ExternalDocketId) || string.IsNullOrWhiteSpace(matter.Id)) return 0;
            var triggerDate = entry.FiledAt ?? entry.ModifiedAt ?? entry.LastSeenAt;
            var rules = await TenantScopeIfAvailable(_context.CourtRules)
                .Where(r => r.IsActive &&
                            (r.CourtType == null || r.CourtType == matter.CourtType) &&
                            (EF.Functions.Like(r.TriggerEvent, "%Filing%") || EF.Functions.Like(r.TriggerEvent, "%Docket%") || EF.Functions.Like(r.TriggerEvent, "%Response%")))
                .OrderBy(r => r.Jurisdiction)
                .ThenBy(r => r.TriggerEvent)
                .Take(2)
                .ToListAsync(ct);

            if (rules.Count == 0)
            {
                return await QueueDocketReviewAsync(entry, "docket_deadline_rule_review", "medium", "No court rule match for docket deadline automation", ct);
            }

            var created = 0;
            foreach (var rule in rules)
            {
                var externalId = $"{entry.ExternalDocketId}:{rule.Id}";
                var version = (entry.ModifiedAt ?? entry.LastSeenAt).Ticks.ToString();
                var key = $"{externalId}|docket_deadline";
                if (linkMap.TryGetValue(key, out var existing) && string.Equals(existing.ExternalVersion, version, StringComparison.Ordinal)) continue;

                var dueDate = CalculateDeadline(triggerDate, rule);
                var deadline = existing == null ? null : await TenantScopeIfAvailable(_context.Deadlines).FirstOrDefaultAsync(d => d.Id == existing.LocalEntityId, ct);
                if (deadline == null)
                {
                    deadline = new Deadline
                    {
                        Id = Guid.NewGuid().ToString(),
                        MatterId = matter.Id,
                        CourtRuleId = rule.Id,
                        Title = $"{rule.TriggerEvent}: {entry.DocketNumber ?? matter.CaseNumber}",
                        Description = $"Auto-drafted from docket sync via {entry.ProviderKey}",
                        DueDate = dueDate,
                        TriggerDate = triggerDate,
                        Status = "Pending",
                        Priority = "High",
                        DeadlineType = "Filing",
                        Notes = $"DocketEntryId={entry.Id}; ExternalDocketId={entry.ExternalDocketId}; SourceUrl={entry.SourceUrl}",
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    _context.Deadlines.Add(deadline);
                }
                else
                {
                    deadline.DueDate = dueDate;
                    deadline.TriggerDate = triggerDate;
                    deadline.UpdatedAt = DateTime.UtcNow;
                }

                UpsertLink(linkMap, connectionId, IntegrationProviderKeys.CourtListenerDockets, "deadline", deadline.Id, "docket_deadline", externalId, version);
                if (existing == null) created++;
            }

            return created;
        }

        private async Task<int> QueueDocketReviewAsync(CourtDocketEntry entry, string itemType, string priority, string title, CancellationToken ct)
        {
            var existing = await TenantScopeIfAvailable(_context.IntegrationReviewQueueItems).FirstOrDefaultAsync(r =>
                r.ProviderKey == entry.ProviderKey && r.SourceType == nameof(CourtDocketEntry) && r.SourceId == entry.Id && r.ItemType == itemType &&
                (r.Status == IntegrationReviewQueueStatuses.Pending || r.Status == IntegrationReviewQueueStatuses.InReview), ct);
            if (existing != null)
            {
                existing.UpdatedAt = DateTime.UtcNow;
                return 0;
            }

            _context.IntegrationReviewQueueItems.Add(new IntegrationReviewQueueItem
            {
                Id = Guid.NewGuid().ToString(),
                ProviderKey = entry.ProviderKey,
                ItemType = itemType,
                SourceType = nameof(CourtDocketEntry),
                SourceId = entry.Id,
                Status = IntegrationReviewQueueStatuses.Pending,
                Priority = priority,
                Title = title,
                Summary = $"Case={entry.CaseName}; Docket={entry.DocketNumber}; Court={entry.Court}; Source={entry.SourceUrl}",
                ContextJson = entry.MetadataJson,
                SuggestedActionsJson = JsonSerializer.Serialize(new object[] { new { action = "review_docket" }, new { action = "link_matter" }, new { action = "draft_deadline" } }),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            return 1;
        }

        private async Task<int> QueueSubmissionReviewAsync(string? connectionId, EfilingSubmission submission, string itemType, string priority, string title, string? summary, CancellationToken ct)
        {
            var existing = await TenantScopeIfAvailable(_context.IntegrationReviewQueueItems).FirstOrDefaultAsync(r =>
                r.ProviderKey == submission.ProviderKey && r.SourceType == nameof(EfilingSubmission) && r.SourceId == submission.Id && r.ItemType == itemType &&
                (r.Status == IntegrationReviewQueueStatuses.Pending || r.Status == IntegrationReviewQueueStatuses.InReview), ct);
            if (existing != null)
            {
                existing.Summary = summary ?? existing.Summary;
                existing.UpdatedAt = DateTime.UtcNow;
                return 0;
            }

            _context.IntegrationReviewQueueItems.Add(new IntegrationReviewQueueItem
            {
                Id = Guid.NewGuid().ToString(),
                ConnectionId = connectionId,
                ProviderKey = submission.ProviderKey,
                ItemType = itemType,
                SourceType = nameof(EfilingSubmission),
                SourceId = submission.Id,
                Status = IntegrationReviewQueueStatuses.Pending,
                Priority = priority,
                Title = title,
                Summary = summary,
                ContextJson = submission.MetadataJson,
                SuggestedActionsJson = JsonSerializer.Serialize(new object[] { new { action = "open_submission" }, new { action = "open_packet_builder" }, new { action = "review_notice_or_repair" } }),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            return 1;
        }

        private async Task<int> EnsureSubmissionTaskAsync(string connectionId, EfilingSubmission submission, string externalEntityType, string title, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(submission.MatterId)) return 0;
            var version = NormalizeSubmissionStatus(submission.Status);
            var existingLink = await TenantScopeIfAvailable(_context.IntegrationEntityLinks).FirstOrDefaultAsync(l =>
                l.ConnectionId == connectionId && l.ProviderKey == submission.ProviderKey && l.ExternalEntityType == externalEntityType && l.ExternalEntityId == submission.ExternalSubmissionId, ct);
            if (existingLink != null && string.Equals(existingLink.ExternalVersion, version, StringComparison.Ordinal)) return 0;

            JurisFlow.Server.Models.Task? task = null;
            if (existingLink != null) task = await TenantScopeIfAvailable(_context.Tasks).FirstOrDefaultAsync(t => t.Id == existingLink.LocalEntityId, ct);
            if (task == null)
            {
                task = new JurisFlow.Server.Models.Task
                {
                    Id = Guid.NewGuid().ToString(),
                    MatterId = submission.MatterId,
                    Title = $"{title} ({submission.ReferenceNumber ?? submission.ExternalSubmissionId})",
                    Description = $"SubmissionId={submission.Id}; ExternalSubmissionId={submission.ExternalSubmissionId}; Status={submission.Status}; RejectionReason={submission.RejectionReason}",
                    Priority = "High",
                    Status = "To Do",
                    DueDate = DateTime.UtcNow.AddDays(1),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _context.Tasks.Add(task);
            }

            var map = new Dictionary<string, IntegrationEntityLink>(StringComparer.Ordinal);
            if (existingLink != null) map[$"{submission.ExternalSubmissionId}|{externalEntityType}"] = existingLink;
            UpsertLink(map, connectionId, submission.ProviderKey, "task", task.Id, externalEntityType, submission.ExternalSubmissionId, version);
            return existingLink == null ? 1 : 0;
        }

        private async Task<int> QueueSubmissionStatusOutboxAsync(string? connectionId, EfilingSubmission submission, string status, CancellationToken ct)
        {
            var key = BuildHashKey($"{submission.ProviderKey}|efiling.submission.status|{submission.ExternalSubmissionId}|{status}|{submission.UpdatedAt.Ticks}");
            var existing = await TenantScopeIfAvailable(_context.IntegrationOutboxEvents).FirstOrDefaultAsync(e => e.IdempotencyKey == key, ct);
            if (existing != null) return 0;

            _context.IntegrationOutboxEvents.Add(new IntegrationOutboxEvent
            {
                Id = Guid.NewGuid().ToString(),
                ConnectionId = connectionId,
                ProviderKey = submission.ProviderKey,
                EventType = "efiling.submission.status_changed",
                EntityType = nameof(EfilingSubmission),
                EntityId = submission.Id,
                CorrelationId = submission.ExternalSubmissionId,
                IdempotencyKey = key,
                Status = IntegrationEventStatuses.Pending,
                PayloadJson = JsonSerializer.Serialize(new { submission.Id, submission.ExternalSubmissionId, submission.Status, status, submission.RejectionReason, submission.MatterId }),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            return 1;
        }

        private void UpsertLink(IDictionary<string, IntegrationEntityLink> linkMap, string connectionId, string providerKey, string localEntityType, string localEntityId, string externalEntityType, string externalEntityId, string? version)
        {
            var key = $"{externalEntityId}|{externalEntityType}";
            if (linkMap.TryGetValue(key, out var existing))
            {
                existing.LocalEntityType = localEntityType;
                existing.LocalEntityId = localEntityId;
                existing.ExternalVersion = version;
                existing.LastDirection = "inbound";
                existing.LastSyncedAt = DateTime.UtcNow;
                existing.IdempotencyKey = BuildHashKey($"{providerKey}|{externalEntityType}|{externalEntityId}|{version}");
                return;
            }

            var link = new IntegrationEntityLink
            {
                Id = Guid.NewGuid().ToString(),
                ConnectionId = connectionId,
                ProviderKey = providerKey,
                LocalEntityType = localEntityType,
                LocalEntityId = localEntityId,
                ExternalEntityType = externalEntityType,
                ExternalEntityId = externalEntityId,
                ExternalVersion = version,
                LastDirection = "inbound",
                LastSyncedAt = DateTime.UtcNow,
                IdempotencyKey = BuildHashKey($"{providerKey}|{externalEntityType}|{externalEntityId}|{version}")
            };
            _context.IntegrationEntityLinks.Add(link);
            linkMap[key] = link;
        }

        private static DateTime CalculateDeadline(DateTime trigger, CourtRule rule)
        {
            var current = trigger.Date;
            var days = Math.Max(0, rule.DaysCount + Math.Max(0, rule.ServiceDaysAdd));
            var before = string.Equals(rule.Direction, "Before", StringComparison.OrdinalIgnoreCase);
            if (string.Equals(rule.DayType, "Court", StringComparison.OrdinalIgnoreCase))
            {
                var remaining = days;
                while (remaining > 0)
                {
                    current = current.AddDays(before ? -1 : 1);
                    if (current.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
                    remaining--;
                }
            }
            else
            {
                current = current.AddDays(before ? -days : days);
            }

            if (rule.ExtendIfWeekend)
            {
                while (current.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) current = current.AddDays(1);
            }
            return DateTime.SpecifyKind(current, DateTimeKind.Utc);
        }

        private static string NormalizeSubmissionStatus(string? value)
        {
            var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
            return string.IsNullOrWhiteSpace(normalized) ? "pending" : normalized;
        }

        private static string BuildHashKey(string raw)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        private async Task TryTriggerOutcomeFeePlannerForDocketSyncAsync(
            IReadOnlyCollection<CourtDocketEntry> entries,
            string? connectionId,
            CancellationToken ct)
        {
            if (entries.Count == 0) return;

            var docketMatterGroups = entries
                .Where(e => !string.IsNullOrWhiteSpace(e.MatterId))
                .GroupBy(e => e.MatterId!, StringComparer.Ordinal)
                .ToArray();
            if (docketMatterGroups.Length == 0) return;

            foreach (var group in docketMatterGroups)
            {
                var firstEntry = group
                    .OrderByDescending(e => e.ModifiedAt ?? e.LastSeenAt)
                    .ThenByDescending(e => e.CreatedAt)
                    .FirstOrDefault();
                if (firstEntry == null) continue;

                try
                {
                    await _outcomeFeePlanner.TryProcessTriggerAsync(new OutcomeFeePlanTriggerRequest
                    {
                        MatterId = group.Key,
                        TriggerType = "court_docket_milestone_sync",
                        TriggerEntityType = nameof(CourtDocketEntry),
                        TriggerEntityId = firstEntry.Id,
                        SourceStatus = "synced",
                        Reason = "Automated docket sync milestone update",
                        CorrelationId = string.IsNullOrWhiteSpace(connectionId) ? null : $"docket-sync:{connectionId}:{group.Key}"
                    }, "system:efiling_automation", ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Outcome-to-Fee planner trigger failed for docket sync matter {MatterId}", group.Key);
                }

                try
                {
                    await _clientTransparencyService.TryProcessTriggerAsync(new ClientTransparencyTriggerRequest
                    {
                        MatterId = group.Key,
                        TriggerType = "court_docket_milestone_sync",
                        TriggerEntityType = nameof(CourtDocketEntry),
                        TriggerEntityId = firstEntry.Id,
                        Reason = "Automated docket sync milestone update",
                        CorrelationId = string.IsNullOrWhiteSpace(connectionId) ? null : $"docket-sync:{connectionId}:{group.Key}"
                    }, "system:efiling_automation", ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Client transparency trigger failed for docket sync matter {MatterId}", group.Key);
                }
            }
        }

        private IQueryable<T> TenantScopeIfAvailable<T>(IQueryable<T> query) where T : class
        {
            if (string.IsNullOrWhiteSpace(_tenantContext.TenantId))
            {
                return query;
            }

            var tenantId = _tenantContext.TenantId;
            return query.Where(entity => EF.Property<string>(entity, "TenantId") == tenantId);
        }
    }

    public sealed class EfilingDocketAutomationRequest
    {
        public string? ConnectionId { get; set; }
        public string? ProviderKey { get; set; }
        public string? MatterId { get; set; }
        public int? Limit { get; set; }
        public List<string>? DocketEntryIds { get; set; }
    }

    public sealed class EfilingDocketAutomationResult
    {
        public int Processed { get; set; }
        public int TasksCreated { get; set; }
        public int DeadlinesCreated { get; set; }
        public int ReviewsQueued { get; set; }
    }

    public sealed class EfilingSubmissionSyncSignal
    {
        public string SubmissionId { get; set; } = string.Empty;
        public string? PreviousStatus { get; set; }
        public string? CurrentStatus { get; set; }
        public string? CurrentRejectionReason { get; set; }
    }

    public sealed class EfilingSubmissionSignalResult
    {
        public int Processed { get; set; }
        public int ReviewsQueued { get; set; }
        public int TasksQueued { get; set; }
        public int OutboxQueued { get; set; }
    }
}
