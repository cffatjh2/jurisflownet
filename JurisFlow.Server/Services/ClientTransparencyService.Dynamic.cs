using System.Text.Json;
using JurisFlow.Server.Models;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace JurisFlow.Server.Services
{
    public partial class ClientTransparencyService
    {
        public async Task<ClientTransparencyTriggerResult> TryProcessTriggerAsync(
            ClientTransparencyTriggerRequest request,
            string userId,
            CancellationToken ct)
        {
            if (request == null) throw new InvalidOperationException("Request body is required.");

            EnsureTenant();

            var normalizedTriggerType = string.IsNullOrWhiteSpace(request.TriggerType) ? "manual_trigger" : request.TriggerType.Trim().ToLowerInvariant();
            var normalizedEntityType = string.IsNullOrWhiteSpace(request.TriggerEntityType) ? null : request.TriggerEntityType.Trim();
            var normalizedEntityId = string.IsNullOrWhiteSpace(request.TriggerEntityId) ? null : request.TriggerEntityId.Trim();
            var normalizedMatterId = string.IsNullOrWhiteSpace(request.MatterId) ? null : request.MatterId.Trim();
            var actor = string.IsNullOrWhiteSpace(userId) ? "system" : userId.Trim();
            var correlationId = string.IsNullOrWhiteSpace(request.CorrelationId) ? $"ctr_{Guid.NewGuid():N}" : request.CorrelationId!.Trim();
            var notificationMode = string.IsNullOrWhiteSpace(request.ClientNotificationMode) ? "auto" : request.ClientNotificationMode.Trim().ToLowerInvariant();

            normalizedMatterId ??= await ResolveMatterIdForTriggerAsync(normalizedEntityType, normalizedEntityId, ct);
            if (string.IsNullOrWhiteSpace(normalizedMatterId))
            {
                return new ClientTransparencyTriggerResult
                {
                    TriggerAccepted = false,
                    Recomputed = false,
                    TriggerType = normalizedTriggerType,
                    TriggerEntityType = normalizedEntityType,
                    TriggerEntityId = normalizedEntityId
                };
            }

            var previous = await GetCurrentSnapshotForMatterAsync(normalizedMatterId, ct);
            var previousSnapshot = previous?.Snapshot;

            try
            {
                var generated = await RegenerateSnapshotAsync(normalizedMatterId, new ClientTransparencyRegenerateRequest
                {
                    TriggerType = normalizedTriggerType,
                    TriggerEntityType = normalizedEntityType,
                    TriggerEntityId = normalizedEntityId,
                    Reason = request.Reason,
                    CorrelationId = correlationId,
                    TriggeredBy = actor,
                    ClientAudience = request.ClientAudience ?? "portal"
                }, actor, ct);

                var currentSnapshot = generated.Snapshot ?? throw new InvalidOperationException("Generated transparency snapshot is missing.");
                var currentRiskFlags = generated.RiskFlags ?? Array.Empty<string>();
                var diff = BuildSnapshotDiff(previous, generated);

                currentSnapshot.WhatChangedSummary = diff.WhatChangedSummary;
                currentSnapshot.UpdatedAt = DateTime.UtcNow;

                var updateEvent = await _context.ClientTransparencyUpdateEvents
                    .Where(e => e.SnapshotId == currentSnapshot.Id)
                    .OrderByDescending(e => e.CreatedAt)
                    .FirstOrDefaultAsync(ct);
                if (updateEvent != null)
                {
                    updateEvent.DiffJson = SerializeJson(diff);
                    updateEvent.MetadataJson = SerializeJson(new
                    {
                        generatedBy = "client_transparency_dynamic_v1",
                        trigger = normalizedTriggerType,
                        request.QueueRetryOnFailure,
                        request.QueueInternalReviewOnDelayThreshold,
                        request.ClientNotificationMode
                    });
                }

                var clientNotificationsQueued = 0;
                var outboxNotificationsQueued = 0;
                var reviewItemsQueued = 0;
                var publishingWorkflow = new ClientTransparencyPublishingWorkflowResult
                {
                    IsPublished = currentSnapshot.IsPublished,
                    PublishDecision = currentSnapshot.IsPublished ? "already_published" : "none",
                    Reasons = Array.Empty<string>()
                };

                var matter = await _context.Matters.FirstOrDefaultAsync(m => m.Id == normalizedMatterId, ct);
                if (matter != null)
                {
                    var profile = generated.Profile ?? await GetOrCreateEffectiveProfileAsync(matter.Id, DateTime.UtcNow, ct);
                    publishingWorkflow = await ApplyPublishingWorkflowAsync(matter, profile, generated, actor, correlationId, ct);
                    reviewItemsQueued += publishingWorkflow.ReviewItemsQueued;

                    var policy = ParseNotificationPolicy(profile.MetadataJson);
                    var delayFlags = generated.DelayReasons.Where(d => d.IsActive).ToList();
                    var effectiveNotificationMode = !publishingWorkflow.IsPublished ? "suppress" : notificationMode;

                    if (!string.Equals(effectiveNotificationMode, "suppress", StringComparison.OrdinalIgnoreCase) &&
                        ShouldNotifyClient(policy, normalizedTriggerType, diff, delayFlags))
                    {
                        clientNotificationsQueued += await QueueClientInAppNotificationAsync(matter, generated, diff, ct);
                        if (ShouldQueueEmailOutbox(policy))
                        {
                            outboxNotificationsQueued += await QueueClientTransparencyOutboxAsync(matter, generated, diff, correlationId, ct);
                        }
                    }

                    if (request.QueueInternalReviewOnDelayThreshold && ShouldQueueDelayThresholdReview(policy, delayFlags))
                    {
                        reviewItemsQueued += await QueueDelayThresholdReviewAsync(matter, generated, diff, ct);
                    }
                }

                await _context.SaveChangesAsync(ct);

                return new ClientTransparencyTriggerResult
                {
                    TriggerAccepted = true,
                    Recomputed = true,
                    MatterId = normalizedMatterId,
                    TriggerType = normalizedTriggerType,
                    TriggerEntityType = normalizedEntityType,
                    TriggerEntityId = normalizedEntityId,
                    PreviousSnapshotId = previousSnapshot?.Id,
                    CurrentSnapshotId = currentSnapshot.Id,
                    PreviousVersionNumber = previousSnapshot?.VersionNumber,
                    CurrentVersionNumber = currentSnapshot.VersionNumber,
                    WhatChangedSummary = currentSnapshot.WhatChangedSummary,
                    DataQuality = currentSnapshot.DataQuality,
                    ConfidenceScore = currentSnapshot.ConfidenceScore,
                    RiskFlags = currentRiskFlags,
                    ClientNotificationsQueued = clientNotificationsQueued,
                    OutboxNotificationsQueued = outboxNotificationsQueued,
                    ReviewItemsQueued = reviewItemsQueued,
                    SnapshotPublished = publishingWorkflow.IsPublished,
                    PublishDecision = publishingWorkflow.PublishDecision,
                    PublishDecisionReasons = publishingWorkflow.Reasons,
                    Diff = diff
                };
            }
            catch (Exception ex)
            {
                if (request.QueueRetryOnFailure)
                {
                    await TryQueueTriggerFailureAsync(normalizedMatterId, normalizedTriggerType, normalizedEntityType, normalizedEntityId, correlationId, actor, ex, ct);
                }

                return new ClientTransparencyTriggerResult
                {
                    TriggerAccepted = true,
                    Recomputed = false,
                    MatterId = normalizedMatterId,
                    TriggerType = normalizedTriggerType,
                    TriggerEntityType = normalizedEntityType,
                    TriggerEntityId = normalizedEntityId,
                    Error = ex.Message
                };
            }
        }

        private async Task<string?> ResolveMatterIdForTriggerAsync(string? entityType, string? entityId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(entityId)) return null;

            if (string.Equals(entityType, nameof(Matter), StringComparison.OrdinalIgnoreCase))
            {
                return await _context.Matters.Where(m => m.Id == entityId).Select(m => m.Id).FirstOrDefaultAsync(ct);
            }
            if (string.Equals(entityType, nameof(JurisFlow.Server.Models.Task), StringComparison.OrdinalIgnoreCase) || string.Equals(entityType, "Task", StringComparison.OrdinalIgnoreCase))
            {
                return await _context.Tasks.Where(t => t.Id == entityId).Select(t => t.MatterId).FirstOrDefaultAsync(ct);
            }
            if (string.Equals(entityType, nameof(EfilingSubmission), StringComparison.OrdinalIgnoreCase))
            {
                return await _context.EfilingSubmissions.Where(e => e.Id == entityId).Select(e => e.MatterId).FirstOrDefaultAsync(ct);
            }
            if (string.Equals(entityType, nameof(CourtDocketEntry), StringComparison.OrdinalIgnoreCase))
            {
                return await _context.CourtDocketEntries.Where(d => d.Id == entityId).Select(d => d.MatterId).FirstOrDefaultAsync(ct);
            }
            if (string.Equals(entityType, nameof(Invoice), StringComparison.OrdinalIgnoreCase))
            {
                return await _context.Invoices.Where(i => i.Id == entityId).Select(i => i.MatterId).FirstOrDefaultAsync(ct);
            }
            if (string.Equals(entityType, nameof(PaymentTransaction), StringComparison.OrdinalIgnoreCase))
            {
                return await _context.PaymentTransactions.Where(p => p.Id == entityId).Select(p => p.MatterId).FirstOrDefaultAsync(ct);
            }
            if (string.Equals(entityType, nameof(OutcomeFeePlan), StringComparison.OrdinalIgnoreCase))
            {
                return await _context.OutcomeFeePlans.Where(p => p.Id == entityId).Select(p => p.MatterId).FirstOrDefaultAsync(ct);
            }
            if (string.Equals(entityType, nameof(OutcomeFeePlanVersion), StringComparison.OrdinalIgnoreCase))
            {
                return await (from v in _context.OutcomeFeePlanVersions
                              join p in _context.OutcomeFeePlans on v.PlanId equals p.Id
                              where v.Id == entityId
                              select p.MatterId).FirstOrDefaultAsync(ct);
            }
            if (string.Equals(entityType, nameof(Document), StringComparison.OrdinalIgnoreCase))
            {
                return await _context.Documents.Where(d => d.Id == entityId).Select(d => d.MatterId).FirstOrDefaultAsync(ct);
            }

            return null;
        }

        private ClientTransparencySnapshotDiff BuildSnapshotDiff(ClientTransparencySnapshotDetailResult? previous, ClientTransparencySnapshotDetailResult current)
        {
            var previousRiskFlags = previous?.RiskFlags ?? Array.Empty<string>();
            var currentRiskFlags = current.RiskFlags ?? Array.Empty<string>();

            var previousDelayCodes = previous?.DelayReasons.Where(d => d.IsActive).Select(d => d.ReasonCode).Distinct(StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase)
                ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var currentDelayCodes = current.DelayReasons.Where(d => d.IsActive).Select(d => d.ReasonCode).Distinct(StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var addedDelayCodes = currentDelayCodes.Except(previousDelayCodes, StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToArray();
            var removedDelayCodes = previousDelayCodes.Except(currentDelayCodes, StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToArray();

            var addedRiskFlags = currentRiskFlags.Except(previousRiskFlags, StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToArray();
            var removedRiskFlags = previousRiskFlags.Except(currentRiskFlags, StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToArray();

            var previousNextStep = previous?.NextStep?.ActionText?.Trim();
            var currentNextStep = current.NextStep?.ActionText?.Trim();
            var nextStepChanged = !string.Equals(previousNextStep, currentNextStep, StringComparison.Ordinal);

            var previousCost = previous?.CostImpact;
            var currentCost = current.CostImpact;
            var costRangeChanged = previousCost == null || currentCost == null
                ? previousCost != currentCost
                : previousCost.CurrentExpectedRangeMin != currentCost.CurrentExpectedRangeMin
                    || previousCost.CurrentExpectedRangeMax != currentCost.CurrentExpectedRangeMax
                    || previousCost.DeltaRangeMin != currentCost.DeltaRangeMin
                    || previousCost.DeltaRangeMax != currentCost.DeltaRangeMax
                    || !string.Equals(previousCost.ConfidenceBand, currentCost.ConfidenceBand, StringComparison.OrdinalIgnoreCase);

            var previousTimelineStatuses = previous?.TimelineItems.ToDictionary(t => t.PhaseKey, t => t.Status, StringComparer.OrdinalIgnoreCase)
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var currentTimelineStatuses = current.TimelineItems.ToDictionary(t => t.PhaseKey, t => t.Status, StringComparer.OrdinalIgnoreCase);
            var timelineStatusChanges = currentTimelineStatuses
                .Where(kvp => !previousTimelineStatuses.TryGetValue(kvp.Key, out var prevStatus) || !string.Equals(prevStatus, kvp.Value, StringComparison.OrdinalIgnoreCase))
                .Select(kvp => new { phaseKey = kvp.Key, status = kvp.Value, previous = previousTimelineStatuses.TryGetValue(kvp.Key, out var prev) ? prev : null })
                .ToArray();

            var summaryParts = new List<string>();
            if (addedDelayCodes.Length > 0) summaryParts.Add($"New delay factor: {string.Join(", ", addedDelayCodes.Select(x => x.Replace('_', ' ')))}");
            if (removedDelayCodes.Length > 0) summaryParts.Add($"Delay factor resolved: {string.Join(", ", removedDelayCodes.Select(x => x.Replace('_', ' ')))}");
            if (nextStepChanged && !string.IsNullOrWhiteSpace(currentNextStep)) summaryParts.Add("Next step updated");
            if (costRangeChanged && currentCost != null) summaryParts.Add("Expected cost impact updated");
            if (timelineStatusChanges.Length > 0) summaryParts.Add("Process map status changed");
            if (addedRiskFlags.Length > 0) summaryParts.Add($"Risk flags added: {string.Join(", ", addedRiskFlags.Select(x => x.Replace('_', ' ')))}");

            return new ClientTransparencySnapshotDiff
            {
                AddedDelayCodes = addedDelayCodes,
                RemovedDelayCodes = removedDelayCodes,
                AddedRiskFlags = addedRiskFlags,
                RemovedRiskFlags = removedRiskFlags,
                NextStepChanged = nextStepChanged,
                CostRangeChanged = costRangeChanged,
                TimelineStatusChanges = timelineStatusChanges,
                WhatChangedSummary = summaryParts.Count == 0 ? "No major client-facing change detected in the latest refresh." : string.Join(". ", summaryParts) + "."
            };
        }

        private async Task<int> QueueClientInAppNotificationAsync(Matter matter, ClientTransparencySnapshotDetailResult snapshot, ClientTransparencySnapshotDiff diff, CancellationToken ct)
        {
            var key = $"client-transparency:{snapshot.Snapshot?.Id}";
            var existing = await _context.Notifications.FirstOrDefaultAsync(n => n.ClientId == matter.ClientId && n.Link == key, ct);
            if (existing != null) return 0;

            _context.Notifications.Add(new Notification
            {
                ClientId = matter.ClientId,
                Title = "Case update available",
                Message = SafeText(snapshot.Snapshot?.WhatChangedSummary, 500) ?? "A new case transparency update is available in your portal.",
                Type = diff.AddedDelayCodes.Count > 0 ? "warning" : "info",
                Link = key,
                Read = false,
                CreatedAt = DateTime.UtcNow
            });
            return 1;
        }

        private async Task<int> QueueClientTransparencyOutboxAsync(Matter matter, ClientTransparencySnapshotDetailResult snapshot, ClientTransparencySnapshotDiff diff, string correlationId, CancellationToken ct)
        {
            var snapshotId = snapshot.Snapshot?.Id;
            if (string.IsNullOrWhiteSpace(snapshotId)) return 0;
            var idempotencyKey = $"client_transparency_email:{snapshotId}";

            var existing = await _context.IntegrationOutboxEvents.FirstOrDefaultAsync(e =>
                e.ProviderKey == "client_transparency" &&
                e.EventType == "client_transparency.snapshot.updated" &&
                e.IdempotencyKey == idempotencyKey, ct);
            if (existing != null) return 0;

            _context.IntegrationOutboxEvents.Add(new IntegrationOutboxEvent
            {
                ProviderKey = "client_transparency",
                EventType = "client_transparency.snapshot.updated",
                EntityType = nameof(ClientTransparencySnapshot),
                EntityId = snapshotId,
                IdempotencyKey = idempotencyKey,
                CorrelationId = correlationId,
                Status = "pending",
                PayloadJson = SerializeJson(new
                {
                    matterId = matter.Id,
                    clientId = matter.ClientId,
                    snapshotId,
                    summary = snapshot.Snapshot?.SnapshotSummary,
                    whatChanged = snapshot.Snapshot?.WhatChangedSummary,
                    delayReasons = snapshot.DelayReasons.Where(d => d.IsActive).Select(d => new { d.ReasonCode, d.Severity, d.ClientSafeText }),
                    nextStep = snapshot.NextStep == null ? null : new { snapshot.NextStep.ActionText, snapshot.NextStep.OwnerType, snapshot.NextStep.EtaAtUtc },
                    costImpact = snapshot.CostImpact == null ? null : new
                    {
                        snapshot.CostImpact.Currency,
                        snapshot.CostImpact.CurrentExpectedRangeMin,
                        snapshot.CostImpact.CurrentExpectedRangeMax,
                        snapshot.CostImpact.ConfidenceBand
                    },
                    riskFlags = snapshot.RiskFlags
                }),
                MetadataJson = SerializeJson(new { route = "email", diff }),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            return 1;
        }

        private async Task<int> QueueDelayThresholdReviewAsync(Matter matter, ClientTransparencySnapshotDetailResult snapshot, ClientTransparencySnapshotDiff diff, CancellationToken ct)
        {
            var snapshotId = snapshot.Snapshot?.Id;
            if (string.IsNullOrWhiteSpace(snapshotId)) return 0;

            var existing = await _context.IntegrationReviewQueueItems.FirstOrDefaultAsync(r =>
                r.ProviderKey == "client_transparency" &&
                r.ItemType == "client_transparency_delay_review" &&
                r.SourceType == nameof(ClientTransparencySnapshot) &&
                r.SourceId == snapshotId &&
                (r.Status == "pending" || r.Status == "in_review"), ct);
            if (existing != null)
            {
                existing.UpdatedAt = DateTime.UtcNow;
                existing.ContextJson = SerializeJson(new { diff, delayReasons = snapshot.DelayReasons.Where(d => d.IsActive) });
                return 0;
            }

            _context.IntegrationReviewQueueItems.Add(new IntegrationReviewQueueItem
            {
                ProviderKey = "client_transparency",
                ItemType = "client_transparency_delay_review",
                SourceType = nameof(ClientTransparencySnapshot),
                SourceId = snapshotId,
                Status = "pending",
                Priority = "medium",
                Title = "Client transparency delay threshold review",
                Summary = SafeText(snapshot.Snapshot?.WhatChangedSummary, 400),
                ContextJson = SerializeJson(new
                {
                    matterId = matter.Id,
                    snapshotId,
                    delays = snapshot.DelayReasons.Where(d => d.IsActive),
                    diff
                }),
                SuggestedActionsJson = SerializeJson(new[] { "review_client_wording", "confirm_delay_reason", "approve_publish" }),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            return 1;
        }

        private async Task TryQueueTriggerFailureAsync(string matterId, string triggerType, string? entityType, string? entityId, string correlationId, string actor, Exception ex, CancellationToken ct)
        {
            try
            {
                _context.ClientTransparencyUpdateEvents.Add(new ClientTransparencyUpdateEvent
                {
                    MatterId = matterId,
                    TriggerType = triggerType,
                    TriggerEntityType = entityType,
                    TriggerEntityId = entityId,
                    Status = "failed",
                    CorrelationId = correlationId,
                    TriggeredBy = actor,
                    PayloadJson = SerializeJson(new { retrySuggested = true }),
                    DiffJson = null,
                    MetadataJson = SerializeJson(new { error = SafeText(ex.Message, 800), queuedBy = "client_transparency_dynamic_v1" }),
                    CreatedAt = DateTime.UtcNow
                });

                var sourceKey = $"{matterId}:{triggerType}:{entityType}:{entityId}";
                var existingReview = await _context.IntegrationReviewQueueItems.FirstOrDefaultAsync(r =>
                    r.ProviderKey == "client_transparency" &&
                    r.ItemType == "client_transparency_retry" &&
                    r.SourceId == sourceKey &&
                    (r.Status == "pending" || r.Status == "in_review"), ct);

                if (existingReview == null)
                {
                    _context.IntegrationReviewQueueItems.Add(new IntegrationReviewQueueItem
                    {
                        ProviderKey = "client_transparency",
                        ItemType = "client_transparency_retry",
                        SourceType = "ClientTransparencyTrigger",
                        SourceId = sourceKey,
                        Status = "pending",
                        Priority = "medium",
                        Title = "Client transparency snapshot regeneration failed",
                        Summary = SafeText(ex.Message, 300),
                        ContextJson = SerializeJson(new { matterId, triggerType, entityType, entityId, correlationId }),
                        SuggestedActionsJson = SerializeJson(new[] { "retry_snapshot_generation", "inspect_source_data" }),
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    });
                }

                await _context.SaveChangesAsync(ct);
            }
            catch
            {
                // fail-open by design
            }
        }

        private static bool ShouldNotifyClient(ClientTransparencyNotificationPolicy policy, string triggerType, ClientTransparencySnapshotDiff diff, IReadOnlyList<ClientTransparencyDelayReason> activeDelays)
        {
            if (!policy.InAppEnabled && !policy.EmailEnabled) return false;
            if (policy.NotifyOnTriggerTypes.Count > 0 && !policy.NotifyOnTriggerTypes.Contains(triggerType, StringComparer.OrdinalIgnoreCase)) return false;

            if (diff.AddedDelayCodes.Count > 0 || diff.NextStepChanged || diff.CostRangeChanged || diff.TimelineStatusChanges.Length > 0)
            {
                return true;
            }

            if (activeDelays.Any(d => (d.ExpectedDelayDays ?? 0) >= policy.DelayThresholdDays)) return true;

            return false;
        }

        private static bool ShouldQueueEmailOutbox(ClientTransparencyNotificationPolicy policy) => policy.EmailEnabled;

        private static bool ShouldQueueDelayThresholdReview(ClientTransparencyNotificationPolicy policy, IReadOnlyList<ClientTransparencyDelayReason> activeDelays)
        {
            if (!policy.InternalDelayReviewEnabled) return false;
            return activeDelays.Any(d => (d.ExpectedDelayDays ?? 0) >= policy.DelayThresholdDays);
        }

        private static ClientTransparencyNotificationPolicy ParseNotificationPolicy(string? profileMetadataJson)
        {
            var policy = new ClientTransparencyNotificationPolicy();
            if (string.IsNullOrWhiteSpace(profileMetadataJson)) return policy;

            try
            {
                using var doc = JsonDocument.Parse(profileMetadataJson);
                if (!doc.RootElement.TryGetProperty("notificationPolicy", out var node) || node.ValueKind != JsonValueKind.Object)
                {
                    return policy;
                }

                if (node.TryGetProperty("inAppEnabled", out var inAppNode) && inAppNode.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    policy.InAppEnabled = inAppNode.GetBoolean();
                if (node.TryGetProperty("emailEnabled", out var emailNode) && emailNode.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    policy.EmailEnabled = emailNode.GetBoolean();
                if (node.TryGetProperty("internalDelayReviewEnabled", out var reviewNode) && reviewNode.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    policy.InternalDelayReviewEnabled = reviewNode.GetBoolean();
                if (node.TryGetProperty("delayThresholdDays", out var thresholdNode) && thresholdNode.TryGetInt32(out var threshold))
                    policy.DelayThresholdDays = Math.Clamp(threshold, 1, 90);

                if (node.TryGetProperty("notifyOnTriggerTypes", out var triggersNode) && triggersNode.ValueKind == JsonValueKind.Array)
                {
                    policy.NotifyOnTriggerTypes = triggersNode.EnumerateArray()
                        .Where(x => x.ValueKind == JsonValueKind.String)
                        .Select(x => x.GetString())
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Select(x => x!.Trim().ToLowerInvariant())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                }
            }
            catch
            {
                return policy;
            }

            return policy;
        }

        private sealed class ClientTransparencyNotificationPolicy
        {
            public bool InAppEnabled { get; set; } = true;
            public bool EmailEnabled { get; set; } = false;
            public bool InternalDelayReviewEnabled { get; set; } = true;
            public int DelayThresholdDays { get; set; } = 3;
            public IReadOnlyList<string> NotifyOnTriggerTypes { get; set; } = Array.Empty<string>();
        }

        private sealed class ClientTransparencySnapshotDiff
        {
            public IReadOnlyList<string> AddedDelayCodes { get; set; } = Array.Empty<string>();
            public IReadOnlyList<string> RemovedDelayCodes { get; set; } = Array.Empty<string>();
            public IReadOnlyList<string> AddedRiskFlags { get; set; } = Array.Empty<string>();
            public IReadOnlyList<string> RemovedRiskFlags { get; set; } = Array.Empty<string>();
            public bool NextStepChanged { get; set; }
            public bool CostRangeChanged { get; set; }
            public object[] TimelineStatusChanges { get; set; } = Array.Empty<object>();
            public string WhatChangedSummary { get; set; } = "Snapshot refreshed.";
        }
    }

    public class ClientTransparencyTriggerRequest
    {
        public string? MatterId { get; set; }
        public string? TriggerType { get; set; } = "manual_trigger";
        public string? TriggerEntityType { get; set; }
        public string? TriggerEntityId { get; set; }
        public string? Reason { get; set; }
        public string? CorrelationId { get; set; }
        public string? ClientAudience { get; set; } = "portal";
        public bool QueueRetryOnFailure { get; set; } = true;
        public bool QueueInternalReviewOnDelayThreshold { get; set; } = true;
        public string? ClientNotificationMode { get; set; } // auto | suppress
    }

    public class ClientTransparencyTriggerResult
    {
        public bool TriggerAccepted { get; set; }
        public bool Recomputed { get; set; }
        public string? MatterId { get; set; }
        public string? TriggerType { get; set; }
        public string? TriggerEntityType { get; set; }
        public string? TriggerEntityId { get; set; }
        public string? PreviousSnapshotId { get; set; }
        public string? CurrentSnapshotId { get; set; }
        public int? PreviousVersionNumber { get; set; }
        public int? CurrentVersionNumber { get; set; }
        public string? WhatChangedSummary { get; set; }
        public string? DataQuality { get; set; }
        public decimal? ConfidenceScore { get; set; }
        public IReadOnlyList<string> RiskFlags { get; set; } = Array.Empty<string>();
        public int ClientNotificationsQueued { get; set; }
        public int OutboxNotificationsQueued { get; set; }
        public int ReviewItemsQueued { get; set; }
        public bool SnapshotPublished { get; set; }
        public string? PublishDecision { get; set; }
        public IReadOnlyList<string> PublishDecisionReasons { get; set; } = Array.Empty<string>();
        public object? Diff { get; set; }
        public string? Error { get; set; }
    }
}
