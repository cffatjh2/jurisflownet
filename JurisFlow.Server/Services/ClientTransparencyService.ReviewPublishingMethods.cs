using System.Text.Json;
using JurisFlow.Server.Models;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace JurisFlow.Server.Services
{
    public partial class ClientTransparencyService
    {
        public async Task<ClientTransparencySnapshotDetailResult?> GetPublishedSnapshotForMatterAsync(string matterId, CancellationToken ct)
        {
            var normalizedMatterId = NormalizeId(matterId);
            if (normalizedMatterId == null) return null;

            EnsureTenant();

            var snapshot = await _context.ClientTransparencySnapshots
                .Where(s => s.MatterId == normalizedMatterId && s.IsPublished)
                .OrderByDescending(s => s.PublishedAt ?? s.GeneratedAt)
                .ThenByDescending(s => s.VersionNumber)
                .FirstOrDefaultAsync(ct);

            return snapshot == null ? null : await LoadSnapshotDetailAsync(snapshot, ct);
        }

        public async Task<ClientTransparencyReviewWorkspaceResult> GetReviewWorkspaceForMatterAsync(string matterId, CancellationToken ct)
        {
            var normalizedMatterId = NormalizeId(matterId) ?? throw new InvalidOperationException("MatterId is required.");
            EnsureTenant();

            var matter = await _context.Matters.FirstOrDefaultAsync(m => m.Id == normalizedMatterId, ct)
                ?? throw new InvalidOperationException("Matter not found.");

            var draft = await GetCurrentSnapshotForMatterAsync(matter.Id, ct);
            var published = await GetPublishedSnapshotForMatterAsync(matter.Id, ct);
            var profile = draft?.Profile ?? published?.Profile ?? await GetOrCreateEffectiveProfileAsync(matter.Id, DateTime.UtcNow, ct);

            var pendingReview = draft?.Snapshot == null
                ? null
                : await _context.IntegrationReviewQueueItems
                    .Where(r =>
                        r.ProviderKey == "client_transparency" &&
                        r.ItemType == "client_transparency_review" &&
                        r.SourceType == nameof(ClientTransparencySnapshot) &&
                        r.SourceId == draft.Snapshot.Id &&
                        r.Status != "resolved" &&
                        r.Status != "dismissed")
                    .OrderByDescending(r => r.CreatedAt)
                    .FirstOrDefaultAsync(ct);

            ClientTransparencyPublishingEvaluation? evaluation = null;
            if (draft?.Snapshot != null && profile != null)
            {
                evaluation = EvaluatePublishingPolicy(profile, draft, published);
            }

            return new ClientTransparencyReviewWorkspaceResult
            {
                MatterId = matter.Id,
                Profile = profile,
                Draft = draft,
                Published = published,
                PendingReviewItem = pendingReview,
                Policy = BuildPublishPolicySummary(profile),
                DraftVsPublished = BuildDraftVsPublishedDiff(draft, published),
                DraftPolicyEvaluation = evaluation == null ? null : new
                {
                    evaluation.PublishDecision,
                    evaluation.RequiresReview,
                    evaluation.Blocked,
                    evaluation.ShouldAutoPublish,
                    evaluation.Priority,
                    evaluation.Reasons
                }
            };
        }

        public async Task<ClientTransparencyProfile> UpsertMatterPolicyAsync(string matterId, ClientTransparencyPolicyUpsertRequest request, string userId, CancellationToken ct)
        {
            var normalizedMatterId = NormalizeId(matterId) ?? throw new InvalidOperationException("MatterId is required.");
            EnsureTenant();

            _ = await _context.Matters.FirstOrDefaultAsync(m => m.Id == normalizedMatterId, ct)
                ?? throw new InvalidOperationException("Matter not found.");

            var now = DateTime.UtcNow;
            var actor = string.IsNullOrWhiteSpace(userId) ? "system" : userId.Trim();

            var profile = await _context.ClientTransparencyProfiles
                .Where(p => p.Scope == "matter_override" && p.MatterId == normalizedMatterId && p.Status == "active")
                .OrderByDescending(p => p.UpdatedAt)
                .FirstOrDefaultAsync(ct);

            if (profile == null)
            {
                var tenantDefault = await _context.ClientTransparencyProfiles
                    .Where(p => p.Scope == "tenant_default" && p.Status == "active")
                    .OrderByDescending(p => p.UpdatedAt)
                    .FirstOrDefaultAsync(ct);

                profile = new ClientTransparencyProfile
                {
                    Scope = "matter_override",
                    MatterId = normalizedMatterId,
                    ProfileKey = "matter_override",
                    Status = "active",
                    PublishPolicy = NormalizeTransparencyPublishPolicy(request.PublishPolicy ?? tenantDefault?.PublishPolicy ?? "warn_only"),
                    VisibilityRulesJson = tenantDefault?.VisibilityRulesJson,
                    RedactionRulesJson = tenantDefault?.RedactionRulesJson,
                    SourceWhitelistJson = tenantDefault?.SourceWhitelistJson,
                    DelayTaxonomyJson = tenantDefault?.DelayTaxonomyJson,
                    MetadataJson = tenantDefault?.MetadataJson,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                _context.ClientTransparencyProfiles.Add(profile);
            }

            profile.PublishPolicy = NormalizeTransparencyPublishPolicy(request.PublishPolicy ?? profile.PublishPolicy);
            profile.MetadataJson = MergePublishPolicyConfigIntoMetadata(profile.MetadataJson, request, actor, now);
            profile.UpdatedAt = now;

            await _context.SaveChangesAsync(ct);
            return profile;
        }

        public async Task<ClientTransparencyReviewWorkspaceResult> ReviewSnapshotAsync(string snapshotId, ClientTransparencySnapshotReviewRequest request, string reviewerUserId, CancellationToken ct)
        {
            var normalizedSnapshotId = NormalizeId(snapshotId) ?? throw new InvalidOperationException("SnapshotId is required.");
            EnsureTenant();

            var snapshot = await _context.ClientTransparencySnapshots.FirstOrDefaultAsync(s => s.Id == normalizedSnapshotId, ct)
                ?? throw new InvalidOperationException("Snapshot not found.");

            var actor = string.IsNullOrWhiteSpace(reviewerUserId) ? "system" : reviewerUserId.Trim();
            request ??= new ClientTransparencySnapshotReviewRequest();
            var action = NormalizeTransparencyReviewAction(request.Action);
            var now = DateTime.UtcNow;

            var before = await LoadSnapshotDetailAsync(snapshot, ct)
                ?? throw new InvalidOperationException("Unable to load snapshot.");

            if (action == "reject" && string.IsNullOrWhiteSpace(request.Reason))
            {
                throw new InvalidOperationException("Reason is required to reject a transparency snapshot.");
            }

            if (action == "rewrite" || HasRewritePayload(request))
            {
                await ApplySnapshotRewritesAsync(snapshot.Id, request, now, ct);
                snapshot.SnapshotSummary = SafeText(request.SnapshotSummary ?? snapshot.SnapshotSummary, 4000);
                snapshot.WhatChangedSummary = SafeText(request.WhatChangedSummary ?? snapshot.WhatChangedSummary, 4000);
                snapshot.Status = "review_pending";
            }

            if (!string.IsNullOrWhiteSpace(request.AssignedTo))
            {
                await AssignTransparencyReviewQueueAsync(snapshot.Id, request.AssignedTo!, actor, now, ct);
            }

            if (action == "approve")
            {
                snapshot.Status = "reviewed";
            }
            else if (action == "reject")
            {
                snapshot.Status = "rejected";
            }

            snapshot.UpdatedAt = now;

            var after = await LoadSnapshotDetailAsync(snapshot, ct)
                ?? throw new InvalidOperationException("Unable to load reviewed snapshot.");

            _context.ClientTransparencyReviewActions.Add(new ClientTransparencyReviewAction
            {
                SnapshotId = snapshot.Id,
                ActionType = action,
                ReviewerUserId = actor,
                Reason = SafeText(request.Reason, 2000),
                BeforeJson = SerializeJson(new
                {
                    summary = before.Snapshot?.SnapshotSummary,
                    whatChanged = before.Snapshot?.WhatChangedSummary,
                    nextStep = before.NextStep?.ActionText,
                    delayReasons = before.DelayReasons.Select(d => new { d.Id, d.ReasonCode, d.ClientSafeText }),
                    timeline = before.TimelineItems.Select(t => new { t.Id, t.PhaseKey, t.ClientSafeText })
                }),
                AfterJson = SerializeJson(new
                {
                    summary = after.Snapshot?.SnapshotSummary,
                    whatChanged = after.Snapshot?.WhatChangedSummary,
                    nextStep = after.NextStep?.ActionText,
                    delayReasons = after.DelayReasons.Select(d => new { d.Id, d.ReasonCode, d.ClientSafeText }),
                    timeline = after.TimelineItems.Select(t => new { t.Id, t.PhaseKey, t.ClientSafeText }),
                    publishAfter = request.PublishAfter ?? false
                }),
                MetadataJson = SerializeJson(new
                {
                    requestedAction = request.Action,
                    actor,
                    atUtc = now
                }),
                CreatedAt = now
            });

            if (action == "approve" || action == "reject")
            {
                await ResolveTransparencyReviewQueueItemsAsync(snapshot.Id, action == "approve" ? "approved" : "rejected", actor, request.Reason, now, ct);
            }

            await _context.SaveChangesAsync(ct);

            if (request.PublishAfter == true && action != "reject")
            {
                var publishResult = await PublishSnapshotAsync(snapshot.Id, new ClientTransparencyPublishRequest
                {
                    Reason = request.Reason ?? "Publish after reviewer action",
                    OverridePolicy = false
                }, actor, ct);
                if (!publishResult.Published)
                {
                    throw new InvalidOperationException($"Publish blocked by policy ({publishResult.PublishDecision}).");
                }
            }

            return await GetReviewWorkspaceForMatterAsync(snapshot.MatterId, ct);
        }

        public async Task<ClientTransparencyPublishResult> PublishSnapshotAsync(string snapshotId, ClientTransparencyPublishRequest? request, string reviewerUserId, CancellationToken ct)
        {
            var normalizedSnapshotId = NormalizeId(snapshotId) ?? throw new InvalidOperationException("SnapshotId is required.");
            EnsureTenant();

            var snapshot = await _context.ClientTransparencySnapshots.FirstOrDefaultAsync(s => s.Id == normalizedSnapshotId, ct)
                ?? throw new InvalidOperationException("Snapshot not found.");

            var actor = string.IsNullOrWhiteSpace(reviewerUserId) ? "system" : reviewerUserId.Trim();
            request ??= new ClientTransparencyPublishRequest();

            return await PublishSnapshotInternalAsync(snapshot, request, actor, saveChanges: true, ct);
        }

        private async Task<ClientTransparencyPublishingWorkflowResult> ApplyPublishingWorkflowAsync(
            Matter matter,
            ClientTransparencyProfile profile,
            ClientTransparencySnapshotDetailResult generated,
            string actor,
            string correlationId,
            CancellationToken ct)
        {
            var snapshot = generated.Snapshot;
            if (snapshot == null)
            {
                return new ClientTransparencyPublishingWorkflowResult { PublishDecision = "missing_snapshot" };
            }

            var latestPublished = await GetPublishedSnapshotForMatterAsync(matter.Id, ct);
            var evaluation = EvaluatePublishingPolicy(profile, generated, latestPublished);
            snapshot.MetadataJson = MergeSnapshotPublishingMetadata(snapshot.MetadataJson, evaluation, correlationId);
            snapshot.UpdatedAt = DateTime.UtcNow;

            if (evaluation.ShouldAutoPublish && !evaluation.Blocked)
            {
                var publishResult = await PublishSnapshotInternalAsync(snapshot, new ClientTransparencyPublishRequest
                {
                    Reason = "Auto-published by transparency policy"
                }, actor, saveChanges: false, ct);

                return new ClientTransparencyPublishingWorkflowResult
                {
                    IsPublished = publishResult.Published,
                    PublishDecision = evaluation.PublishDecision,
                    Reasons = evaluation.Reasons,
                    ReviewItemsQueued = publishResult.ReviewItemsQueued
                };
            }

            var reviewItemsQueued = 0;
            if (evaluation.RequiresReview || evaluation.Blocked)
            {
                reviewItemsQueued += await EnsureTransparencyReviewQueueItemAsync(
                    matter,
                    generated,
                    evaluation,
                    actor,
                    ct);
                snapshot.Status = evaluation.Blocked ? "review_blocked" : "review_required";
                snapshot.UpdatedAt = DateTime.UtcNow;
            }

            return new ClientTransparencyPublishingWorkflowResult
            {
                IsPublished = snapshot.IsPublished,
                ReviewItemsQueued = reviewItemsQueued,
                PublishDecision = evaluation.PublishDecision,
                Reasons = evaluation.Reasons
            };
        }

        private async Task<ClientTransparencyPublishResult> PublishSnapshotInternalAsync(
            ClientTransparencySnapshot snapshot,
            ClientTransparencyPublishRequest request,
            string actor,
            bool saveChanges,
            CancellationToken ct)
        {
            var detail = await LoadSnapshotDetailAsync(snapshot, ct)
                ?? throw new InvalidOperationException("Snapshot detail not found.");
            var profile = detail.Profile ?? await GetOrCreateEffectiveProfileAsync(snapshot.MatterId, DateTime.UtcNow, ct);
            var latestPublished = await GetPublishedSnapshotForMatterAsync(snapshot.MatterId, ct);
            var evaluation = EvaluatePublishingPolicy(profile, detail, latestPublished);

            var reasons = evaluation.Reasons.ToArray();
            var blockedByPolicy = evaluation.Blocked;
            var overrideRequired = blockedByPolicy && !request.OverridePolicy;

            if (overrideRequired)
            {
                var reviewItemsQueued = await EnsureTransparencyReviewQueueItemAsync(
                    await _context.Matters.FirstAsync(m => m.Id == snapshot.MatterId, ct),
                    detail,
                    evaluation,
                    actor,
                    ct);

                if (saveChanges)
                {
                    await _context.SaveChangesAsync(ct);
                }

                return new ClientTransparencyPublishResult
                {
                    SnapshotId = snapshot.Id,
                    MatterId = snapshot.MatterId,
                    Published = false,
                    PublishDecision = evaluation.PublishDecision,
                    OverrideRequired = true,
                    ReviewItemsQueued = reviewItemsQueued,
                    Reasons = reasons
                };
            }

            if (request.OverridePolicy && string.IsNullOrWhiteSpace(request.ApproverReason))
            {
                throw new InvalidOperationException("ApproverReason is required when overriding transparency publish policy.");
            }

            var now = DateTime.UtcNow;
            var otherPublished = await _context.ClientTransparencySnapshots
                .Where(s => s.MatterId == snapshot.MatterId && s.Id != snapshot.Id && s.IsPublished)
                .ToListAsync(ct);
            foreach (var row in otherPublished)
            {
                row.IsPublished = false;
                if (string.Equals(row.Status, "published", StringComparison.OrdinalIgnoreCase))
                {
                    row.Status = "superseded";
                }
                row.UpdatedAt = now;
            }

            snapshot.IsPublished = true;
            snapshot.PublishedAt = now;
            snapshot.Status = "published";
            snapshot.UpdatedAt = now;
            snapshot.MetadataJson = MergeSnapshotPublishActionMetadata(snapshot.MetadataJson, actor, request, evaluation, now);

            _context.ClientTransparencyReviewActions.Add(new ClientTransparencyReviewAction
            {
                SnapshotId = snapshot.Id,
                ActionType = "publish",
                ReviewerUserId = actor,
                Reason = SafeText(request.Reason, 2000),
                MetadataJson = SerializeJson(new
                {
                    request.OverridePolicy,
                    approverReason = SafeText(request.ApproverReason, 2000),
                    evaluation.PublishDecision,
                    evaluation.Reasons,
                    publishedAtUtc = now
                }),
                CreatedAt = now
            });

            await ResolveTransparencyReviewQueueItemsAsync(snapshot.Id, "published", actor, request.Reason, now, ct);

            if (saveChanges)
            {
                await _context.SaveChangesAsync(ct);
            }

            return new ClientTransparencyPublishResult
            {
                SnapshotId = snapshot.Id,
                MatterId = snapshot.MatterId,
                Published = true,
                PublishDecision = request.OverridePolicy ? "override_publish" : evaluation.PublishDecision,
                OverrideRequired = false,
                PublishedAt = now,
                Reasons = reasons
            };
        }

        private ClientTransparencyPublishPolicySummary BuildPublishPolicySummary(ClientTransparencyProfile? profile)
        {
            profile ??= new ClientTransparencyProfile();
            var config = ParsePublishPolicyConfig(profile);
            return new ClientTransparencyPublishPolicySummary
            {
                PublishPolicy = NormalizeTransparencyPublishPolicy(profile.PublishPolicy),
                AutoPublishSafe = config.AutoPublishSafe,
                ReviewRequiredForDelayReason = config.ReviewRequiredForDelayReason,
                ReviewRequiredForCostImpactChange = config.ReviewRequiredForCostImpactChange,
                CostImpactChangeThreshold = config.CostImpactChangeThreshold,
                BlockOnLowConfidence = config.BlockOnLowConfidence,
                LowConfidenceThreshold = config.LowConfidenceThreshold
            };
        }

        private ClientTransparencyPublishingEvaluation EvaluatePublishingPolicy(
            ClientTransparencyProfile profile,
            ClientTransparencySnapshotDetailResult draft,
            ClientTransparencySnapshotDetailResult? latestPublished)
        {
            var config = ParsePublishPolicyConfig(profile);
            var publishPolicy = NormalizeTransparencyPublishPolicy(profile.PublishPolicy);

            var reasons = new List<string>();
            var requiresReview = false;
            var blocked = false;
            var shouldAutoPublish = publishPolicy == "warn_only";
            var priority = "low";

            var activeDelays = (draft.DelayReasons ?? Array.Empty<ClientTransparencyDelayReason>()).Any(d => d.IsActive);
            if (activeDelays && (config.ReviewRequiredForDelayReason || publishPolicy == "review_required_for_delay_reason"))
            {
                reasons.Add("active_delay_reason_present");
                requiresReview = true;
                shouldAutoPublish = false;
                priority = "medium";
            }

            var confidence = draft.Snapshot?.ConfidenceScore ?? 0m;
            if ((config.BlockOnLowConfidence || publishPolicy == "block_on_low_confidence") && confidence > 0m && confidence < config.LowConfidenceThreshold)
            {
                reasons.Add("low_confidence_snapshot");
                blocked = true;
                requiresReview = true;
                shouldAutoPublish = false;
                priority = "high";
            }

            var costImpactDelta = CalculateMaxCostDeltaMagnitude(draft.CostImpact, latestPublished?.CostImpact);
            if (costImpactDelta.HasValue && (config.ReviewRequiredForCostImpactChange || publishPolicy == "review_required_for_cost_impact_change_gt_x") && costImpactDelta.Value >= config.CostImpactChangeThreshold)
            {
                reasons.Add("cost_impact_change_exceeds_threshold");
                requiresReview = true;
                shouldAutoPublish = false;
                priority = "high";
            }

            if (publishPolicy == "auto_publish_safe")
            {
                shouldAutoPublish = config.AutoPublishSafe && !requiresReview && !blocked;
            }
            else if (publishPolicy.StartsWith("review_required_", StringComparison.OrdinalIgnoreCase))
            {
                shouldAutoPublish = false;
            }
            else if (publishPolicy == "block_on_low_confidence" && !blocked && !requiresReview)
            {
                shouldAutoPublish = true;
            }

            if (!reasons.Any())
            {
                reasons.Add("no_sensitive_client_facing_change_detected");
            }

            return new ClientTransparencyPublishingEvaluation
            {
                PublishDecision = blocked
                    ? "blocked_review_required"
                    : requiresReview
                        ? "review_required"
                        : shouldAutoPublish
                            ? "auto_publish"
                            : "warn_only_auto_publish",
                RequiresReview = requiresReview,
                Blocked = blocked,
                ShouldAutoPublish = shouldAutoPublish,
                Priority = priority,
                Reasons = reasons
            };
        }

        private async Task<int> EnsureTransparencyReviewQueueItemAsync(
            Matter matter,
            ClientTransparencySnapshotDetailResult snapshotDetail,
            ClientTransparencyPublishingEvaluation evaluation,
            string actor,
            CancellationToken ct)
        {
            var snapshot = snapshotDetail.Snapshot;
            if (snapshot == null) return 0;

            var existing = await _context.IntegrationReviewQueueItems
                .FirstOrDefaultAsync(r =>
                    r.ProviderKey == "client_transparency" &&
                    r.ItemType == "client_transparency_review" &&
                    r.SourceType == nameof(ClientTransparencySnapshot) &&
                    r.SourceId == snapshot.Id &&
                    r.Status != "resolved" &&
                    r.Status != "dismissed", ct);

            var context = new
            {
                matterId = matter.Id,
                snapshotId = snapshot.Id,
                snapshotVersion = snapshot.VersionNumber,
                confidenceScore = snapshot.ConfidenceScore,
                dataQuality = snapshot.DataQuality,
                reasons = evaluation.Reasons,
                nextStep = snapshotDetail.NextStep?.ActionText,
                activeDelayReasons = snapshotDetail.DelayReasons.Where(d => d.IsActive).Select(d => new { d.Id, d.ReasonCode, d.Severity, d.ExpectedDelayDays }),
                costImpact = snapshotDetail.CostImpact == null ? null : new
                {
                    snapshotDetail.CostImpact.Currency,
                    snapshotDetail.CostImpact.CurrentExpectedRangeMin,
                    snapshotDetail.CostImpact.CurrentExpectedRangeMax,
                    snapshotDetail.CostImpact.DeltaRangeMin,
                    snapshotDetail.CostImpact.DeltaRangeMax,
                    snapshotDetail.CostImpact.ConfidenceBand
                }
            };

            if (existing != null)
            {
                existing.Priority = NormalizeQueuePriority(evaluation.Priority);
                existing.Status = existing.Status == "in_review" ? existing.Status : "pending";
                existing.Title = $"Client transparency review required for {matter.Name}";
                existing.Summary = SafeText(BuildTransparencyReviewSummary(snapshotDetail, evaluation), 2048);
                existing.ContextJson = SerializeJson(context);
                existing.UpdatedAt = DateTime.UtcNow;
                return 0;
            }

            _context.IntegrationReviewQueueItems.Add(new IntegrationReviewQueueItem
            {
                ProviderKey = "client_transparency",
                ItemType = "client_transparency_review",
                SourceType = nameof(ClientTransparencySnapshot),
                SourceId = snapshot.Id,
                Status = "pending",
                Priority = NormalizeQueuePriority(evaluation.Priority),
                Title = $"Client transparency review required for {matter.Name}",
                Summary = SafeText(BuildTransparencyReviewSummary(snapshotDetail, evaluation), 2048),
                ContextJson = SerializeJson(context),
                SuggestedActionsJson = SerializeJson(new[]
                {
                    new { action = "rewrite", label = "Rewrite client-safe wording" },
                    new { action = "publish", label = "Approve and publish" },
                    new { action = "reject", label = "Reject snapshot" }
                }),
                AssignedTo = null,
                ReviewedBy = null,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            _context.ClientTransparencyReviewActions.Add(new ClientTransparencyReviewAction
            {
                SnapshotId = snapshot.Id,
                ActionType = "review_queued",
                ReviewerUserId = actor,
                MetadataJson = SerializeJson(new { evaluation.PublishDecision, evaluation.Reasons, atUtc = DateTime.UtcNow }),
                CreatedAt = DateTime.UtcNow
            });

            return 1;
        }

        private async Task ResolveTransparencyReviewQueueItemsAsync(string snapshotId, string decision, string actor, string? notes, DateTime now, CancellationToken ct)
        {
            var openItems = await _context.IntegrationReviewQueueItems
                .Where(r =>
                    r.ProviderKey == "client_transparency" &&
                    r.ItemType == "client_transparency_review" &&
                    r.SourceType == nameof(ClientTransparencySnapshot) &&
                    r.SourceId == snapshotId &&
                    r.Status != "resolved" &&
                    r.Status != "dismissed")
                .ToListAsync(ct);

            foreach (var item in openItems)
            {
                item.Status = "resolved";
                item.Decision = SafeText(decision, 32);
                item.DecisionNotes = SafeText(notes, 2048);
                item.ReviewedBy = actor;
                item.ReviewedAt = now;
                item.ResolvedAt = now;
                item.UpdatedAt = now;
            }
        }

        private async Task AssignTransparencyReviewQueueAsync(string snapshotId, string assignedTo, string actor, DateTime now, CancellationToken ct)
        {
            var assignee = NormalizeId(assignedTo);
            if (assignee == null) return;

            var item = await _context.IntegrationReviewQueueItems
                .Where(r =>
                    r.ProviderKey == "client_transparency" &&
                    r.ItemType == "client_transparency_review" &&
                    r.SourceType == nameof(ClientTransparencySnapshot) &&
                    r.SourceId == snapshotId &&
                    r.Status != "resolved" &&
                    r.Status != "dismissed")
                .OrderByDescending(r => r.CreatedAt)
                .FirstOrDefaultAsync(ct);
            if (item == null) return;

            item.AssignedTo = assignee;
            item.Status = item.Status == "pending" ? "in_review" : item.Status;
            item.UpdatedAt = now;

            _context.ClientTransparencyReviewActions.Add(new ClientTransparencyReviewAction
            {
                SnapshotId = snapshotId,
                ActionType = "assign",
                ReviewerUserId = actor,
                Reason = $"Assigned to {assignee}",
                MetadataJson = SerializeJson(new { assignedTo = assignee, atUtc = now }),
                CreatedAt = now
            });
        }

        private async Task ApplySnapshotRewritesAsync(string snapshotId, ClientTransparencySnapshotReviewRequest request, DateTime now, CancellationToken ct)
        {
            var nextStep = await _context.ClientTransparencyNextSteps.FirstOrDefaultAsync(n => n.SnapshotId == snapshotId, ct);
            if (nextStep != null)
            {
                if (!string.IsNullOrWhiteSpace(request.NextStepActionText))
                {
                    nextStep.ActionText = request.NextStepActionText.Trim();
                }
                if (!string.IsNullOrWhiteSpace(request.NextStepBlockedByText))
                {
                    nextStep.BlockedByText = request.NextStepBlockedByText.Trim();
                }
                nextStep.UpdatedAt = now;
            }

            if (request.DelayReasonTextUpdates is { Count: > 0 })
            {
                var updates = request.DelayReasonTextUpdates
                    .Where(x => !string.IsNullOrWhiteSpace(x.Id))
                    .ToDictionary(x => x.Id!.Trim(), x => x.ClientSafeText, StringComparer.Ordinal);
                if (updates.Count > 0)
                {
                    var rows = await _context.ClientTransparencyDelayReasons
                        .Where(d => d.SnapshotId == snapshotId && updates.Keys.Contains(d.Id))
                        .ToListAsync(ct);
                    foreach (var row in rows)
                    {
                        row.ClientSafeText = SafeText(updates[row.Id], 4000);
                        row.UpdatedAt = now;
                    }
                }
            }

            if (request.TimelineTextUpdates is { Count: > 0 })
            {
                var updates = request.TimelineTextUpdates
                    .Where(x => !string.IsNullOrWhiteSpace(x.Id))
                    .ToDictionary(x => x.Id!.Trim(), x => x.ClientSafeText, StringComparer.Ordinal);
                if (updates.Count > 0)
                {
                    var rows = await _context.ClientTransparencyTimelineItems
                        .Where(t => t.SnapshotId == snapshotId && updates.Keys.Contains(t.Id))
                        .ToListAsync(ct);
                    foreach (var row in rows)
                    {
                        row.ClientSafeText = SafeText(updates[row.Id], 4000);
                        row.UpdatedAt = now;
                    }
                }
            }
        }

        private static bool HasRewritePayload(ClientTransparencySnapshotReviewRequest request)
        {
            return !string.IsNullOrWhiteSpace(request.SnapshotSummary)
                || !string.IsNullOrWhiteSpace(request.WhatChangedSummary)
                || !string.IsNullOrWhiteSpace(request.NextStepActionText)
                || !string.IsNullOrWhiteSpace(request.NextStepBlockedByText)
                || (request.DelayReasonTextUpdates?.Count > 0)
                || (request.TimelineTextUpdates?.Count > 0);
        }

        private static string NormalizeTransparencyReviewAction(string? action)
        {
            var normalized = string.IsNullOrWhiteSpace(action) ? "rewrite" : action.Trim().ToLowerInvariant();
            return normalized is "approve" or "reject" or "rewrite" ? normalized : "rewrite";
        }

        private static string NormalizeTransparencyPublishPolicy(string? value)
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? "warn_only" : value.Trim().ToLowerInvariant();
            return normalized switch
            {
                "warn_only" => "warn_only",
                "auto_publish_safe" => "auto_publish_safe",
                "review_required_for_delay_reason" => "review_required_for_delay_reason",
                "review_required_for_cost_impact_change_gt_x" => "review_required_for_cost_impact_change_gt_x",
                "block_on_low_confidence" => "block_on_low_confidence",
                _ => "warn_only"
            };
        }

        private static string NormalizeQueuePriority(string? value)
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? "medium" : value.Trim().ToLowerInvariant();
            return normalized is "low" or "medium" or "high" or "critical" ? normalized : "medium";
        }

        private ClientTransparencyPublishPolicyConfig ParsePublishPolicyConfig(ClientTransparencyProfile profile)
        {
            var config = new ClientTransparencyPublishPolicyConfig();
            if (string.IsNullOrWhiteSpace(profile.MetadataJson)) return config;

            try
            {
                using var doc = JsonDocument.Parse(profile.MetadataJson);
                if (!doc.RootElement.TryGetProperty("publishPolicyConfig", out var node) || node.ValueKind != JsonValueKind.Object)
                {
                    return config;
                }

                config.AutoPublishSafe = node.TryGetProperty("autoPublishSafe", out var autoNode) && GetBool(autoNode);
                config.ReviewRequiredForDelayReason = node.TryGetProperty("reviewRequiredForDelayReason", out var delayNode) && GetBool(delayNode);
                config.ReviewRequiredForCostImpactChange = node.TryGetProperty("reviewRequiredForCostImpactChange", out var costNode) && GetBool(costNode);
                config.BlockOnLowConfidence = node.TryGetProperty("blockOnLowConfidence", out var lowNode) && GetBool(lowNode);
                if (node.TryGetProperty("costImpactChangeThreshold", out var costThresholdNode))
                {
                    config.CostImpactChangeThreshold = NormalizeThreshold(GetDecimal(costThresholdNode), 1000m);
                }
                if (node.TryGetProperty("lowConfidenceThreshold", out var lowThresholdNode))
                {
                    var threshold = GetDecimal(lowThresholdNode);
                    config.LowConfidenceThreshold = threshold <= 0m || threshold > 1m ? 0.55m : threshold;
                }
            }
            catch
            {
                // defaults
            }

            return config;
        }

        private static string MergePublishPolicyConfigIntoMetadata(string? metadataJson, ClientTransparencyPolicyUpsertRequest request, string actor, DateTime now)
        {
            var root = ParseMutableMetadata(metadataJson);
            var existing = TryGetObject(root, "publishPolicyConfig");

            var next = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["autoPublishSafe"] = request.AutoPublishSafe ?? TryGetBool(existing, "autoPublishSafe"),
                ["reviewRequiredForDelayReason"] = request.ReviewRequiredForDelayReason ?? TryGetBool(existing, "reviewRequiredForDelayReason"),
                ["reviewRequiredForCostImpactChange"] = request.ReviewRequiredForCostImpactChange ?? TryGetBool(existing, "reviewRequiredForCostImpactChange"),
                ["costImpactChangeThreshold"] = request.CostImpactChangeThreshold ?? TryGetDecimal(existing, "costImpactChangeThreshold", 1000m),
                ["blockOnLowConfidence"] = request.BlockOnLowConfidence ?? TryGetBool(existing, "blockOnLowConfidence"),
                ["lowConfidenceThreshold"] = request.LowConfidenceThreshold ?? TryGetDecimal(existing, "lowConfidenceThreshold", 0.55m)
            };

            root["publishPolicyConfig"] = next;
            root["publishPolicyConfigMeta"] = new { updatedBy = actor, updatedAtUtc = now };
            return SerializeJson(root)!;
        }

        private static string? MergeSnapshotPublishingMetadata(string? metadataJson, ClientTransparencyPublishingEvaluation evaluation, string correlationId)
        {
            var root = ParseMutableMetadata(metadataJson);
            root["publishing"] = new
            {
                decision = evaluation.PublishDecision,
                requiresReview = evaluation.RequiresReview,
                blocked = evaluation.Blocked,
                shouldAutoPublish = evaluation.ShouldAutoPublish,
                priority = evaluation.Priority,
                reasons = evaluation.Reasons,
                evaluatedAtUtc = DateTime.UtcNow,
                correlationId
            };
            return SerializeJson(root);
        }

        private static string? MergeSnapshotPublishActionMetadata(string? metadataJson, string actor, ClientTransparencyPublishRequest request, ClientTransparencyPublishingEvaluation evaluation, DateTime now)
        {
            var root = ParseMutableMetadata(metadataJson);
            root["publishAction"] = new
            {
                publishedBy = actor,
                publishedAtUtc = now,
                overridePolicy = request.OverridePolicy,
                approverReason = SafeText(request.ApproverReason, 2000),
                reason = SafeText(request.Reason, 2000),
                evaluation.PublishDecision,
                evaluation.Reasons
            };
            return SerializeJson(root);
        }

        private static Dictionary<string, object?> ParseMutableMetadata(string? json)
        {
            var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(json)) return result;

            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) return result;
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    result[prop.Name] = prop.Value.Clone();
                }
            }
            catch
            {
                // ignore malformed metadata
            }

            return result;
        }

        private static Dictionary<string, object?>? TryGetObject(Dictionary<string, object?> root, string key)
        {
            if (!root.TryGetValue(key, out var value) || value == null) return null;
            if (value is Dictionary<string, object?> dict) return dict;

            if (value is JsonElement node && node.ValueKind == JsonValueKind.Object)
            {
                var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var prop in node.EnumerateObject())
                {
                    result[prop.Name] = prop.Value.Clone();
                }
                return result;
            }

            return null;
        }

        private static bool TryGetBool(Dictionary<string, object?>? root, string key)
        {
            if (root == null || !root.TryGetValue(key, out var value) || value == null) return false;
            if (value is bool b) return b;
            if (value is JsonElement node) return GetBool(node);
            if (value is string s && bool.TryParse(s, out var parsed)) return parsed;
            return false;
        }

        private static decimal TryGetDecimal(Dictionary<string, object?>? root, string key, decimal fallback)
        {
            if (root == null || !root.TryGetValue(key, out var value) || value == null) return fallback;
            if (value is decimal d) return d;
            if (value is double dbl) return (decimal)dbl;
            if (value is JsonElement node) return GetDecimal(node);
            if (decimal.TryParse(value.ToString(), out var parsed)) return parsed;
            return fallback;
        }

        private static bool GetBool(JsonElement node)
        {
            return node.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String when bool.TryParse(node.GetString(), out var parsed) => parsed,
                _ => false
            };
        }

        private static decimal GetDecimal(JsonElement node)
        {
            if (node.ValueKind == JsonValueKind.Number && node.TryGetDecimal(out var value)) return value;
            if (node.ValueKind == JsonValueKind.String && decimal.TryParse(node.GetString(), out var parsed)) return parsed;
            return 0m;
        }

        private static decimal NormalizeThreshold(decimal value, decimal fallback) => value <= 0m ? fallback : value;

        private static decimal? CalculateMaxCostDeltaMagnitude(ClientTransparencyCostImpact? draft, ClientTransparencyCostImpact? published)
        {
            if (draft == null) return null;
            var deltas = new[]
            {
                draft.DeltaRangeMin.HasValue ? Math.Abs(draft.DeltaRangeMin.Value) : 0m,
                draft.DeltaRangeMax.HasValue ? Math.Abs(draft.DeltaRangeMax.Value) : 0m
            };
            var direct = deltas.Max();
            if (direct > 0m) return direct;

            if (published != null &&
                draft.CurrentExpectedRangeMin.HasValue && draft.CurrentExpectedRangeMax.HasValue &&
                published.CurrentExpectedRangeMin.HasValue && published.CurrentExpectedRangeMax.HasValue)
            {
                var minDelta = Math.Abs(draft.CurrentExpectedRangeMin.Value - published.CurrentExpectedRangeMin.Value);
                var maxDelta = Math.Abs(draft.CurrentExpectedRangeMax.Value - published.CurrentExpectedRangeMax.Value);
                return Math.Max(minDelta, maxDelta);
            }
            return null;
        }

        private static object BuildDraftVsPublishedDiff(ClientTransparencySnapshotDetailResult? draft, ClientTransparencySnapshotDetailResult? published)
        {
            var draftSnapshot = draft?.Snapshot;
            var publishedSnapshot = published?.Snapshot;
            var draftDelay = draft?.DelayReasons.Where(d => d.IsActive).Select(d => d.ReasonCode).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToArray() ?? Array.Empty<string>();
            var pubDelay = published?.DelayReasons.Where(d => d.IsActive).Select(d => d.ReasonCode).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToArray() ?? Array.Empty<string>();
            return new
            {
                hasDraft = draftSnapshot != null,
                hasPublished = publishedSnapshot != null,
                draftSnapshotId = draftSnapshot?.Id,
                publishedSnapshotId = publishedSnapshot?.Id,
                summaryChanged = !string.Equals(draftSnapshot?.SnapshotSummary, publishedSnapshot?.SnapshotSummary, StringComparison.Ordinal),
                whatChangedUpdated = !string.Equals(draftSnapshot?.WhatChangedSummary, publishedSnapshot?.WhatChangedSummary, StringComparison.Ordinal),
                confidenceChanged = draftSnapshot?.ConfidenceScore != publishedSnapshot?.ConfidenceScore,
                delayAdded = draftDelay.Except(pubDelay, StringComparer.OrdinalIgnoreCase).ToArray(),
                delayRemoved = pubDelay.Except(draftDelay, StringComparer.OrdinalIgnoreCase).ToArray(),
                nextStepChanged = !string.Equals(draft?.NextStep?.ActionText, published?.NextStep?.ActionText, StringComparison.Ordinal),
                costImpactChanged = !string.Equals(draft?.CostImpact?.DriverSummary, published?.CostImpact?.DriverSummary, StringComparison.Ordinal)
                                  || draft?.CostImpact?.CurrentExpectedRangeMin != published?.CostImpact?.CurrentExpectedRangeMin
                                  || draft?.CostImpact?.CurrentExpectedRangeMax != published?.CostImpact?.CurrentExpectedRangeMax
            };
        }

        private static string BuildTransparencyReviewSummary(ClientTransparencySnapshotDetailResult draft, ClientTransparencyPublishingEvaluation evaluation)
        {
            var summary = draft.Snapshot?.SnapshotSummary ?? "Transparency snapshot requires review.";
            var reasons = evaluation.Reasons.Count == 0 ? string.Empty : $" Reasons: {string.Join(", ", evaluation.Reasons.Select(r => r.Replace('_', ' ')))}.";
            return $"{summary}{reasons}";
        }
    }
}
