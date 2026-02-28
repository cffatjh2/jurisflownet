using System.Text.Json;
using JurisFlow.Server.Models;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace JurisFlow.Server.Services
{
    public partial class ClientTransparencyService
    {
        public async Task<object?> GetSnapshotEvidenceBundleAsync(string snapshotId, CancellationToken ct)
        {
            var detail = await GetSnapshotByIdAsync(snapshotId, ct);
            if (detail?.Snapshot == null) return null;
            return await BuildSnapshotEvidenceBundleInternalAsync(detail, ct);
        }

        public async Task<object> GetEvidenceMetricsAsync(int days, string? matterId, CancellationToken ct)
        {
            EnsureTenant();
            var now = DateTime.UtcNow;
            var boundedDays = Math.Clamp(days <= 0 ? 90 : days, 1, 365);
            var since = now.AddDays(-boundedDays);
            var normalizedMatterId = NormalizeId(matterId);

            var snapshotsQuery = _context.ClientTransparencySnapshots.Where(s => s.GeneratedAt >= since);
            if (normalizedMatterId != null)
            {
                snapshotsQuery = snapshotsQuery.Where(s => s.MatterId == normalizedMatterId);
            }

            var snapshots = await snapshotsQuery.OrderByDescending(s => s.GeneratedAt).Take(120).ToListAsync(ct);

            var rows = new List<(ClientTransparencySnapshot s, decimal coverage, decimal stale, int staleSources, int reviewBurden)>();
            foreach (var snapshot in snapshots)
            {
                var detail = await GetSnapshotByIdAsync(snapshot.Id, ct);
                if (detail?.Snapshot == null) continue;
                var bundle = await BuildSnapshotEvidenceBundleInternalAsync(detail, ct);
                var quality = ExtractQuality(bundle);
                rows.Add((snapshot, quality.coverage, quality.stale, quality.staleSources, quality.reviewBurden));
            }

            var pendingReviewQuery = _context.IntegrationReviewQueueItems
                .Where(r => r.ProviderKey == "client_transparency" && (r.Status == "pending" || r.Status == "in_review"));
            if (normalizedMatterId != null)
            {
                pendingReviewQuery = pendingReviewQuery.Where(r =>
                    (r.SourceId != null && r.SourceId.Contains(normalizedMatterId)) ||
                    (r.ContextJson != null && r.ContextJson.Contains(normalizedMatterId)));
            }

            var pendingReviewCount = await pendingReviewQuery.CountAsync(ct);

            var reviewActionQuery = from a in _context.ClientTransparencyReviewActions
                                    join s in _context.ClientTransparencySnapshots on a.SnapshotId equals s.Id
                                    where a.CreatedAt >= since
                                    select new { a.CreatedAt, s.GeneratedAt, s.MatterId };
            if (normalizedMatterId != null)
            {
                reviewActionQuery = reviewActionQuery.Where(x => x.MatterId == normalizedMatterId);
            }
            var reviewActions = await reviewActionQuery.ToListAsync(ct);
            var reviewHours = reviewActions.Select(x => (x.CreatedAt - x.GeneratedAt).TotalHours).Where(x => x >= 0).ToArray();

            var buckets = rows
                .GroupBy(x => x.s.GeneratedAt.Date)
                .OrderBy(g => g.Key)
                .Select(g => new
                {
                    date = g.Key,
                    snapshotCount = g.Count(),
                    coverageRate = Math.Round(g.Average(x => (double)x.coverage), 4),
                    staleRate = Math.Round(g.Average(x => (double)x.stale), 4),
                    reviewBurden = Math.Round(g.Average(x => x.reviewBurden), 2)
                })
                .ToArray();

            return new
            {
                days = boundedDays,
                matterId = normalizedMatterId,
                snapshotCount = rows.Count,
                publishedSnapshotCount = rows.Count(x => x.s.IsPublished),
                coverageRate = rows.Count == 0 ? 0m : Math.Round(rows.Average(x => x.coverage), 4),
                staleRate = rows.Count == 0 ? 0m : Math.Round(rows.Average(x => x.stale), 4),
                snapshotsWithStaleSources = rows.Count(x => x.staleSources > 0),
                pendingReviewCount,
                reviewBurdenAverage = rows.Count == 0 ? 0m : Math.Round((decimal)rows.Average(x => (double)x.reviewBurden), 2),
                meanReviewTurnaroundHours = reviewHours.Length == 0 ? (decimal?)null : Math.Round((decimal)reviewHours.Average(), 2),
                buckets,
                dataQuality = rows.Count < 5 ? "low_sample" : "measured"
            };
        }

        public async Task<object> BatchReverifyEvidenceAsync(ClientTransparencyEvidenceBatchReverifyRequest? request, string userId, CancellationToken ct)
        {
            EnsureTenant();
            request ??= new ClientTransparencyEvidenceBatchReverifyRequest();
            var now = DateTime.UtcNow;
            var days = Math.Clamp(request.Days <= 0 ? 90 : request.Days, 1, 365);
            var limit = Math.Clamp(request.Limit <= 0 ? 50 : request.Limit, 1, 250);
            var normalizedMatterId = NormalizeId(request.MatterId);
            var sourceFilter = string.IsNullOrWhiteSpace(request.SourceFilter) ? null : request.SourceFilter.Trim().ToLowerInvariant();
            var actor = string.IsNullOrWhiteSpace(userId) ? "system" : userId.Trim();

            var query = _context.ClientTransparencySnapshots.Where(s => s.GeneratedAt >= now.AddDays(-days));
            if (normalizedMatterId != null) query = query.Where(s => s.MatterId == normalizedMatterId);
            if (request.OnlyPublished == true) query = query.Where(s => s.IsPublished);
            if (request.OnlyCurrent == true) query = query.Where(s => s.IsCurrent);

            var snapshots = await query.OrderByDescending(s => s.GeneratedAt).Take(limit).ToListAsync(ct);
            var items = new List<object>();
            var reviewItemsQueued = 0;

            foreach (var snapshot in snapshots)
            {
                try
                {
                    var detail = await GetSnapshotByIdAsync(snapshot.Id, ct);
                    if (detail?.Snapshot == null)
                    {
                        items.Add(new { snapshotId = snapshot.Id, matterId = snapshot.MatterId, versionNumber = snapshot.VersionNumber, reverified = false, error = "snapshot_not_loaded" });
                        continue;
                    }

                    var bundle = await BuildSnapshotEvidenceBundleInternalAsync(detail, ct);
                    var quality = ExtractQuality(bundle);
                    var allSources = ExtractSourceList(bundle, "allSources");
                    if (sourceFilter != null && !allSources.Any(x => string.Equals(x.Source, sourceFilter, StringComparison.OrdinalIgnoreCase)))
                    {
                        items.Add(new { snapshotId = snapshot.Id, matterId = snapshot.MatterId, versionNumber = snapshot.VersionNumber, reverified = false, skipped = true, skipReason = "source_filter_no_match" });
                        continue;
                    }

                    _context.ClientTransparencyUpdateEvents.Add(new ClientTransparencyUpdateEvent
                    {
                        MatterId = snapshot.MatterId,
                        SnapshotId = snapshot.Id,
                        TriggerType = "evidence_reverify",
                        TriggerEntityType = nameof(ClientTransparencySnapshot),
                        TriggerEntityId = snapshot.Id,
                        Status = "applied",
                        CorrelationId = $"ctr_ev_{Guid.NewGuid():N}",
                        TriggeredBy = actor,
                        MetadataJson = SerializeJson(new { evidenceVersion = "client_transparency_evidence_v1", quality }),
                        CreatedAt = now,
                        AppliedAt = now
                    });

                    if (snapshot.IsPublished && quality.staleSources > 0)
                    {
                        reviewItemsQueued += await QueueTransparencyEvidenceReviewAsync(snapshot, bundle, ct);
                    }

                    items.Add(new
                    {
                        snapshotId = snapshot.Id,
                        matterId = snapshot.MatterId,
                        versionNumber = snapshot.VersionNumber,
                        reverified = true,
                        coverageRate = quality.coverage,
                        staleRate = quality.stale,
                        staleSources = quality.staleSources,
                        totalSources = quality.totalSources
                    });
                }
                catch (Exception ex)
                {
                    items.Add(new { snapshotId = snapshot.Id, matterId = snapshot.MatterId, versionNumber = snapshot.VersionNumber, reverified = false, error = SafeText(ex.Message, 300) });
                }
            }

            await _context.SaveChangesAsync(ct);

            return new
            {
                requested = snapshots.Count,
                reverified = items.Count(i => GetBool(i, "reverified")),
                skipped = items.Count(i => GetBool(i, "skipped")),
                failed = items.Count(i => !GetBool(i, "reverified") && !GetBool(i, "skipped")),
                reviewItemsQueued,
                items
            };
        }

        private sealed class EvidenceSourceRefState
        {
            public string Source { get; set; } = "unknown";
            public string? EntityId { get; set; }
            public string? PlanVersionId { get; set; }
            public string? RulePackId { get; set; }
            public string? Label { get; set; }
            public string? MetadataJson { get; set; }
            public DateTime? LastChangedAtUtc { get; set; }
            public bool IsStale { get; set; }
            public string? StaleReason { get; set; }
        }

        private async Task<object> BuildSnapshotEvidenceBundleInternalAsync(ClientTransparencySnapshotDetailResult detail, CancellationToken ct)
        {
            var snapshot = detail.Snapshot ?? throw new InvalidOperationException("Snapshot is required.");
            var now = DateTime.UtcNow;

            var timelineLinks = detail.TimelineItems.Select(t => new
            {
                itemId = t.Id,
                itemType = "timeline",
                label = t.Label,
                text = t.ClientSafeText,
                sourceRefs = ParseEvidenceSourceRefs(t.SourceRefsJson)
            }).ToList();

            var delayLinks = detail.DelayReasons.Where(d => d.IsActive).Select(d => new
            {
                itemId = d.Id,
                itemType = "delay_reason",
                label = d.ReasonCode,
                text = d.ClientSafeText,
                sourceRefs = ParseEvidenceSourceRefs(d.SourceRefsJson)
            }).ToList();

            var nextStepLink = detail.NextStep == null ? null : new
            {
                itemId = detail.NextStep.Id,
                itemType = "next_step",
                label = detail.NextStep.OwnerType,
                text = detail.NextStep.ActionText,
                sourceRefs = ParseEvidenceSourceRefs(detail.NextStep.SourceRefsJson)
            };

            var costImpactLink = detail.CostImpact == null ? null : new
            {
                itemId = detail.CostImpact.Id,
                itemType = "cost_impact",
                label = detail.CostImpact.Currency,
                text = detail.CostImpact.DriverSummary,
                sourceRefs = ParseEvidenceSourceRefs(detail.CostImpact.SourceRefsJson)
            };

            var latestUpdateEvent = await _context.ClientTransparencyUpdateEvents
                .Where(e => e.SnapshotId == snapshot.Id)
                .OrderByDescending(e => e.CreatedAt)
                .FirstOrDefaultAsync(ct);

            var unionRefs = timelineLinks.SelectMany(x => x.sourceRefs.Cast<EvidenceSourceRefState>())
                .Concat(delayLinks.SelectMany(x => x.sourceRefs.Cast<EvidenceSourceRefState>()))
                .Concat(nextStepLink?.sourceRefs.Cast<EvidenceSourceRefState>() ?? Array.Empty<EvidenceSourceRefState>())
                .Concat(costImpactLink?.sourceRefs.Cast<EvidenceSourceRefState>() ?? Array.Empty<EvidenceSourceRefState>())
                .ToList();

            var summarySentences = BuildSentenceEvidence(snapshot.SnapshotSummary, "summary", unionRefs);
            var whatChangedSentences = BuildSentenceEvidence(snapshot.WhatChangedSummary, "what_changed", BuildWhatChangedRefs(latestUpdateEvent, unionRefs));

            var allRefs = unionRefs
                .Concat(BuildWhatChangedRefs(latestUpdateEvent, unionRefs))
                .ToList();

            await ApplyStaleDetectionAsync(allRefs, snapshot.GeneratedAt, ct);

            var uniqueSources = allRefs
                .GroupBy(x => $"{x.Source}:{x.EntityId}:{x.PlanVersionId}:{x.RulePackId}", StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
            var staleSources = uniqueSources.Where(x => x.IsStale).ToList();

            var totalSegments = summarySentences.Count + whatChangedSentences.Count + timelineLinks.Count + delayLinks.Count + (nextStepLink == null ? 0 : 1) + (costImpactLink == null ? 0 : 1);
            var coveredSegments = summarySentences.Count(x => CountSourceRefs(x) > 0)
                + whatChangedSentences.Count(x => CountSourceRefs(x) > 0)
                + timelineLinks.Count(x => CountSourceRefs(x) > 0)
                + delayLinks.Count(x => CountSourceRefs(x) > 0)
                + ((nextStepLink != null && CountSourceRefs(nextStepLink) > 0) ? 1 : 0)
                + ((costImpactLink != null && CountSourceRefs(costImpactLink) > 0) ? 1 : 0);
            var totalSourceCount = uniqueSources.Count;
            var reviewBurden = await _context.IntegrationReviewQueueItems.CountAsync(r =>
                r.ProviderKey == "client_transparency" &&
                r.SourceType == nameof(ClientTransparencySnapshot) &&
                r.SourceId == snapshot.Id &&
                (r.Status == "pending" || r.Status == "in_review"), ct);

            var quality = new
            {
                coverage = totalSegments == 0 ? 0m : Math.Round((decimal)coveredSegments / totalSegments, 4),
                stale = totalSourceCount == 0 ? 0m : Math.Round((decimal)staleSources.Count / totalSourceCount, 4),
                coveredSegments,
                totalSegments,
                totalSources = totalSourceCount,
                staleSources = staleSources.Count,
                reviewBurden,
                lastVerifiedAtUtc = now,
                dataQuality = totalSegments < 3 ? "low_sample" : "measured"
            };

            return new
            {
                snapshotId = snapshot.Id,
                versionNumber = snapshot.VersionNumber,
                summarySentences,
                whatChangedSentences,
                timelineLinks,
                delayReasonLinks = delayLinks,
                nextStepLink,
                costImpactLink,
                staleSources = staleSources.Select(ToSourceRefObject).ToArray(),
                allSources = uniqueSources.Select(ToSourceRefObject).ToArray(),
                quality
            };
        }

        private async Task ApplyStaleDetectionAsync(List<EvidenceSourceRefState> refs, DateTime snapshotGeneratedAt, CancellationToken ct)
        {
            if (refs.Count == 0) return;
            var distinct = refs.GroupBy(x => $"{x.Source}:{x.EntityId}:{x.PlanVersionId}:{x.RulePackId}", StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();

            var matterIds = distinct.Where(x => x.Source.Equals("matter", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(x.EntityId)).Select(x => x.EntityId!).Distinct().ToArray();
            var taskIds = distinct.Where(x => x.Source.Equals("task", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(x.EntityId)).Select(x => x.EntityId!).Distinct().ToArray();
            var docketIds = distinct.Where(x => x.Source.Equals("docket", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(x.EntityId)).Select(x => x.EntityId!).Distinct().ToArray();
            var efilingIds = distinct.Where(x => x.Source.Equals("efiling", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(x.EntityId)).Select(x => x.EntityId!).Distinct().ToArray();
            var invoiceIds = distinct.Where(x => x.Source.Equals("invoice", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(x.EntityId)).Select(x => x.EntityId!).Distinct().ToArray();
            var paymentIds = distinct.Where(x => x.Source.Equals("payment", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(x.EntityId)).Select(x => x.EntityId!).Distinct().ToArray();
            var plannerIds = distinct.Where(x => x.Source.Equals("planner", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(x.EntityId)).Select(x => x.EntityId!).Distinct().ToArray();
            var plannerVersionIds = distinct.Where(x => !string.IsNullOrWhiteSpace(x.PlanVersionId)).Select(x => x.PlanVersionId!).Distinct().ToArray();

            var matterTs = matterIds.Length == 0 ? new Dictionary<string, DateTime>() : await _context.Matters.Where(x => matterIds.Contains(x.Id)).Select(x => new { x.Id, x.OpenDate, x.ConflictCheckDate }).ToDictionaryAsync(x => x.Id, x => MaxTs(x.ConflictCheckDate, x.OpenDate), ct);
            var taskTs = taskIds.Length == 0 ? new Dictionary<string, DateTime>() : await _context.Tasks.Where(x => taskIds.Contains(x.Id)).Select(x => new { x.Id, x.UpdatedAt, x.CreatedAt }).ToDictionaryAsync(x => x.Id, x => MaxTs(x.UpdatedAt, x.CreatedAt), ct);
            var docketTs = docketIds.Length == 0 ? new Dictionary<string, DateTime>() : await _context.CourtDocketEntries.Where(x => docketIds.Contains(x.Id)).Select(x => new { x.Id, x.ModifiedAt, x.UpdatedAt, x.LastSeenAt, x.FiledAt, x.CreatedAt }).ToDictionaryAsync(x => x.Id, x => MaxTs(x.ModifiedAt, x.UpdatedAt, x.LastSeenAt, x.FiledAt, x.CreatedAt), ct);
            var efilingTs = efilingIds.Length == 0 ? new Dictionary<string, DateTime>() : await _context.EfilingSubmissions.Where(x => efilingIds.Contains(x.Id)).Select(x => new { x.Id, x.UpdatedAt, x.LastSeenAt, x.RejectedAt, x.AcceptedAt, x.SubmittedAt, x.CreatedAt }).ToDictionaryAsync(x => x.Id, x => MaxTs(x.UpdatedAt, x.LastSeenAt, x.RejectedAt, x.AcceptedAt, x.SubmittedAt, x.CreatedAt), ct);
            var invoiceTs = invoiceIds.Length == 0 ? new Dictionary<string, DateTime>() : await _context.Invoices.Where(x => invoiceIds.Contains(x.Id)).Select(x => new { x.Id, x.UpdatedAt, x.CreatedAt }).ToDictionaryAsync(x => x.Id, x => MaxTs(x.UpdatedAt, x.CreatedAt), ct);
            var paymentTs = paymentIds.Length == 0 ? new Dictionary<string, DateTime>() : await _context.PaymentTransactions.Where(x => paymentIds.Contains(x.Id)).Select(x => new { x.Id, x.UpdatedAt, x.ProcessedAt, x.RefundedAt, x.CreatedAt }).ToDictionaryAsync(x => x.Id, x => MaxTs(x.UpdatedAt, x.ProcessedAt, x.RefundedAt, x.CreatedAt), ct);
            var plannerState = plannerIds.Length == 0 ? new Dictionary<string, (DateTime ts, string? currentVersionId)>() : await _context.OutcomeFeePlans.Where(x => plannerIds.Contains(x.Id)).Select(x => new { x.Id, x.UpdatedAt, x.CreatedAt, x.CurrentVersionId }).ToDictionaryAsync(x => x.Id, x => (MaxTs(x.UpdatedAt, x.CreatedAt), x.CurrentVersionId), ct);
            var plannerVersionTs = plannerVersionIds.Length == 0 ? new Dictionary<string, DateTime>() : await _context.OutcomeFeePlanVersions.Where(x => plannerVersionIds.Contains(x.Id)).Select(x => new { x.Id, x.UpdatedAt, x.GeneratedAt, x.CreatedAt }).ToDictionaryAsync(x => x.Id, x => MaxTs(x.UpdatedAt, x.GeneratedAt, x.CreatedAt), ct);

            foreach (var r in refs)
            {
                var source = r.Source.Trim().ToLowerInvariant();
                DateTime? ts = source switch
                {
                    "matter" when r.EntityId != null && matterTs.TryGetValue(r.EntityId, out var t) => t,
                    "task" when r.EntityId != null && taskTs.TryGetValue(r.EntityId, out var t) => t,
                    "docket" when r.EntityId != null && docketTs.TryGetValue(r.EntityId, out var t) => t,
                    "efiling" when r.EntityId != null && efilingTs.TryGetValue(r.EntityId, out var t) => t,
                    "invoice" when r.EntityId != null && invoiceTs.TryGetValue(r.EntityId, out var t) => t,
                    "payment" when r.EntityId != null && paymentTs.TryGetValue(r.EntityId, out var t) => t,
                    "planner" when r.PlanVersionId != null && plannerVersionTs.TryGetValue(r.PlanVersionId, out var t) => t,
                    "planner" when r.EntityId != null && plannerState.TryGetValue(r.EntityId, out var ps) => ps.Item1,
                    _ => null
                };
                r.LastChangedAtUtc = ts;
                if (!ts.HasValue)
                {
                    r.IsStale = true;
                    r.StaleReason = "source_not_found";
                    continue;
                }
                if (ts.Value > snapshotGeneratedAt)
                {
                    r.IsStale = true;
                    r.StaleReason = "source_updated_after_snapshot";
                }
                if (source == "planner" && r.EntityId != null && r.PlanVersionId != null && plannerState.TryGetValue(r.EntityId, out var planner) && !string.IsNullOrWhiteSpace(planner.Item2) && !string.Equals(planner.Item2, r.PlanVersionId, StringComparison.Ordinal))
                {
                    r.IsStale = true;
                    r.StaleReason = "planner_version_superseded";
                }
            }
        }

        private async Task<int> QueueTransparencyEvidenceReviewAsync(ClientTransparencySnapshot snapshot, object bundle, CancellationToken ct)
        {
            var staleSources = ExtractSourceList(bundle, "staleSources");
            var existing = await _context.IntegrationReviewQueueItems.FirstOrDefaultAsync(r =>
                r.ProviderKey == "client_transparency" &&
                r.ItemType == "client_transparency_evidence_review" &&
                r.SourceType == nameof(ClientTransparencySnapshot) &&
                r.SourceId == snapshot.Id &&
                (r.Status == "pending" || r.Status == "in_review"), ct);

            var contextJson = SerializeJson(new { snapshotId = snapshot.Id, matterId = snapshot.MatterId, staleSources = staleSources.Select(ToSourceRefObject) });
            if (existing != null)
            {
                existing.ContextJson = contextJson;
                existing.UpdatedAt = DateTime.UtcNow;
                return 0;
            }

            _context.IntegrationReviewQueueItems.Add(new IntegrationReviewQueueItem
            {
                ProviderKey = "client_transparency",
                ItemType = "client_transparency_evidence_review",
                SourceType = nameof(ClientTransparencySnapshot),
                SourceId = snapshot.Id,
                Status = "pending",
                Priority = "medium",
                Title = "Client transparency evidence review",
                Summary = SafeText(snapshot.WhatChangedSummary ?? snapshot.SnapshotSummary, 300),
                ContextJson = contextJson,
                SuggestedActionsJson = SerializeJson(new[] { "review_stale_sources", "reverify_snapshot", "rewrite_client_text" }),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            return 1;
        }

        private static List<object> BuildSentenceEvidence(string? text, string keyPrefix, List<EvidenceSourceRefState> refs)
        {
            var result = new List<object>();
            if (string.IsNullOrWhiteSpace(text)) return result;

            var parts = text.Trim().Split(new[] { ". ", "! ", "? " }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) parts = new[] { text.Trim() };

            for (var i = 0; i < parts.Length; i++)
            {
                var sentence = parts[i].Trim();
                if (!sentence.EndsWith(".") && i < parts.Length - 1) sentence += ".";
                result.Add(new { sentenceId = $"{keyPrefix}_{i + 1}", text = sentence, sourceRefs = refs.Select(ToSourceRefObject).ToArray() });
            }

            return result;
        }

        private static List<EvidenceSourceRefState> BuildWhatChangedRefs(ClientTransparencyUpdateEvent? updateEvent, List<EvidenceSourceRefState> fallbackRefs)
        {
            var refs = new List<EvidenceSourceRefState>();
            if (updateEvent != null && !string.IsNullOrWhiteSpace(updateEvent.TriggerEntityType) && !string.IsNullOrWhiteSpace(updateEvent.TriggerEntityId))
            {
                var source = MapTriggerEntityTypeToSource(updateEvent.TriggerEntityType);
                if (source != null)
                {
                    refs.Add(new EvidenceSourceRefState
                    {
                        Source = source,
                        EntityId = updateEvent.TriggerEntityId,
                        Label = updateEvent.TriggerType,
                        MetadataJson = SerializeJson(new { updateEvent.TriggerType, updateEvent.TriggerEntityType })
                    });
                }
            }
            if (refs.Count == 0) refs.AddRange(fallbackRefs.Take(6).Select(CloneState));
            return refs;
        }

        private static string? MapTriggerEntityTypeToSource(string? entityType)
        {
            if (string.IsNullOrWhiteSpace(entityType)) return null;
            return entityType.Trim().ToLowerInvariant() switch
            {
                "task" => "task",
                "efilingsubmission" => "efiling",
                "courtdocketentry" => "docket",
                "invoice" => "invoice",
                "paymenttransaction" => "payment",
                "outcomefeeplan" => "planner",
                "outcomefeeplanversion" => "planner",
                "matter" => "matter",
                _ => null
            };
        }

        private static List<EvidenceSourceRefState> ParseEvidenceSourceRefs(string? sourceRefsJson)
        {
            var refs = new List<EvidenceSourceRefState>();
            if (string.IsNullOrWhiteSpace(sourceRefsJson)) return refs;

            try
            {
                using var doc = JsonDocument.Parse(sourceRefsJson);
                if (doc.RootElement.ValueKind != JsonValueKind.Array) return refs;
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object) continue;
                    string? source = item.TryGetProperty("source", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
                    string? entityId = item.TryGetProperty("entityId", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
                    string? planVersionId = item.TryGetProperty("planVersionId", out var pv) && pv.ValueKind == JsonValueKind.String ? pv.GetString() : null;
                    string? rulePackId = item.TryGetProperty("rulePackId", out var rp) && rp.ValueKind == JsonValueKind.String ? rp.GetString() : null;
                    refs.Add(new EvidenceSourceRefState
                    {
                        Source = string.IsNullOrWhiteSpace(source) ? "unknown" : source!,
                        EntityId = entityId,
                        PlanVersionId = planVersionId,
                        RulePackId = rulePackId,
                        Label = BuildSourceRefLabel(item, source),
                        MetadataJson = item.GetRawText()
                    });
                }
            }
            catch
            {
                return new List<EvidenceSourceRefState>();
            }

            return refs;
        }

        private static string? BuildSourceRefLabel(JsonElement item, string? source)
        {
            if (source == null) return null;
            if (source.Equals("task", StringComparison.OrdinalIgnoreCase) && item.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String) return title.GetString();
            if (source.Equals("docket", StringComparison.OrdinalIgnoreCase) && item.TryGetProperty("court", out var court) && court.ValueKind == JsonValueKind.String) return court.GetString();
            if (source.Equals("planner", StringComparison.OrdinalIgnoreCase) && item.TryGetProperty("scenarioId", out var scenario) && scenario.ValueKind == JsonValueKind.String) return $"Planner scenario {scenario.GetString()}";
            return null;
        }

        private static object ToSourceRefObject(EvidenceSourceRefState r) => new
        {
            source = r.Source,
            entityId = r.EntityId,
            planVersionId = r.PlanVersionId,
            rulePackId = r.RulePackId,
            label = r.Label,
            lastChangedAtUtc = r.LastChangedAtUtc == DateTime.MinValue ? (DateTime?)null : r.LastChangedAtUtc,
            isStale = r.IsStale,
            staleReason = r.StaleReason,
            metadata = ParseJsonLoose(r.MetadataJson)
        };

        private static object? ParseJsonLoose(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { return JsonSerializer.Deserialize<object>(json, JsonOptions); } catch { return json; }
        }

        private static EvidenceSourceRefState CloneState(EvidenceSourceRefState source)
        {
            return new EvidenceSourceRefState
            {
                Source = source.Source,
                EntityId = source.EntityId,
                PlanVersionId = source.PlanVersionId,
                RulePackId = source.RulePackId,
                Label = source.Label,
                MetadataJson = source.MetadataJson,
                LastChangedAtUtc = source.LastChangedAtUtc,
                IsStale = source.IsStale,
                StaleReason = source.StaleReason
            };
        }

        private static DateTime MaxTs(params DateTime?[] values)
            => values.Where(v => v.HasValue).Select(v => v!.Value).DefaultIfEmpty(DateTime.MinValue).Max();

        private static int CountSourceRefs(object obj)
        {
            var prop = obj.GetType().GetProperty("sourceRefs");
            var value = prop?.GetValue(obj);
            return value is System.Collections.ICollection c ? c.Count : 0;
        }

        private static bool GetBool(object obj, string propertyName)
        {
            var prop = obj.GetType().GetProperty(propertyName);
            var value = prop?.GetValue(obj);
            return value is bool b && b;
        }

        private static (decimal coverage, decimal stale, int staleSources, int totalSources, int reviewBurden) ExtractQuality(object bundle)
        {
            try
            {
                var q = bundle.GetType().GetProperty("quality")?.GetValue(bundle);
                if (q == null) return (0m, 0m, 0, 0, 0);
                return (
                    ToDecimal(q, "coverage"),
                    ToDecimal(q, "stale"),
                    Convert.ToInt32(q.GetType().GetProperty("staleSources")?.GetValue(q) ?? 0),
                    Convert.ToInt32(q.GetType().GetProperty("totalSources")?.GetValue(q) ?? 0),
                    Convert.ToInt32(q.GetType().GetProperty("reviewBurden")?.GetValue(q) ?? 0)
                );
            }
            catch
            {
                return (0m, 0m, 0, 0, 0);
            }
        }

        private static decimal ToDecimal(object obj, string propertyName)
        {
            var val = obj.GetType().GetProperty(propertyName)?.GetValue(obj);
            return val switch
            {
                decimal d => d,
                double d => (decimal)d,
                float f => (decimal)f,
                int i => i,
                long l => l,
                _ => 0m
            };
        }

        private static List<EvidenceSourceRefState> ExtractSourceList(object bundle, string propertyName)
        {
            var list = new List<EvidenceSourceRefState>();
            try
            {
                var prop = bundle.GetType().GetProperty(propertyName)?.GetValue(bundle);
                if (prop is not System.Collections.IEnumerable items) return list;
                foreach (var item in items)
                {
                    if (item == null) continue;
                    list.Add(new EvidenceSourceRefState
                    {
                        Source = item.GetType().GetProperty("source")?.GetValue(item)?.ToString() ?? "unknown",
                        EntityId = item.GetType().GetProperty("entityId")?.GetValue(item)?.ToString(),
                        PlanVersionId = item.GetType().GetProperty("planVersionId")?.GetValue(item)?.ToString(),
                        RulePackId = item.GetType().GetProperty("rulePackId")?.GetValue(item)?.ToString(),
                        Label = item.GetType().GetProperty("label")?.GetValue(item)?.ToString(),
                        IsStale = item.GetType().GetProperty("isStale")?.GetValue(item) is bool b && b,
                        StaleReason = item.GetType().GetProperty("staleReason")?.GetValue(item)?.ToString()
                    });
                }
            }
            catch { }
            return list;
        }
    }

    public class ClientTransparencyEvidenceBatchReverifyRequest
    {
        public string? MatterId { get; set; }
        public int Days { get; set; } = 90;
        public int Limit { get; set; } = 50;
        public bool? OnlyPublished { get; set; } = true;
        public bool? OnlyCurrent { get; set; }
        public string? SourceFilter { get; set; }
    }
}
