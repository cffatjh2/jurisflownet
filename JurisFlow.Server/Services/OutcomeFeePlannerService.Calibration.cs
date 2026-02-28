using System.Text.Json;
using JurisFlow.Server.Models;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace JurisFlow.Server.Services
{
    public partial class OutcomeFeePlannerService
    {
        public async Task<object> ListCalibrationSnapshotsAsync(string? status, string? cohortKey, int limit, CancellationToken ct)
        {
            var query = _context.OutcomeFeeCalibrationSnapshots.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(s => s.Status == status.Trim().ToLowerInvariant());
            }
            if (!string.IsNullOrWhiteSpace(cohortKey))
            {
                query = query.Where(s => s.CohortKey == cohortKey.Trim());
            }

            var rows = await query.OrderByDescending(s => s.AsOfDate).ThenByDescending(s => s.CreatedAt)
                .Take(Math.Clamp(limit <= 0 ? 50 : limit, 1, 200))
                .ToListAsync(ct);

            return new { count = rows.Count, items = rows.Select(BuildCalibrationSnapshotDto).ToList() };
        }

        public async Task<object?> RecordOutcomeFeedbackAsync(string planId, OutcomeFeeOutcomeFeedbackRequest request, string userId, CancellationToken ct)
        {
            var plan = await _context.OutcomeFeePlans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == planId, ct);
            if (plan == null) return null;

            var latestVersionId = await _context.OutcomeFeePlanVersions.AsNoTracking()
                .Where(v => v.PlanId == planId)
                .OrderByDescending(v => v.VersionNumber)
                .Select(v => v.Id)
                .FirstOrDefaultAsync(ct);

            var now = DateTime.UtcNow;
            var evt = new OutcomeFeeUpdateEvent
            {
                PlanId = planId,
                TriggerType = "outcome_label_feedback",
                TriggerEntityType = nameof(OutcomeFeePlan),
                TriggerEntityId = planId,
                AppliedVersionId = latestVersionId,
                Status = "applied",
                CorrelationId = string.IsNullOrWhiteSpace(request.CorrelationId) ? plan.CorrelationId : request.CorrelationId?.Trim(),
                TriggeredBy = userId,
                PayloadJson = SerializeJson(new
                {
                    request.ActualOutcome,
                    request.ActualFeesCollected,
                    request.ActualCost,
                    request.ActualMargin,
                    OutcomeDateUtc = request.OutcomeDateUtc?.ToUniversalTime(),
                    request.Notes
                }),
                ResultJson = SerializeJson(new { stored = true }),
                MetadataJson = SerializeJson(new { source = "manual_feedback", schemaVersion = "v1" }),
                CreatedAt = now,
                AppliedAt = now
            };

            _context.OutcomeFeeUpdateEvents.Add(evt);
            await _context.SaveChangesAsync(ct);

            return new { eventId = evt.Id, planId, latestVersionId, createdAtUtc = evt.CreatedAt };
        }

        public async Task<object?> GetEffectiveCalibrationForMatterAsync(string matterId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(matterId)) return null;
            var matter = await _context.Matters.AsNoTracking().FirstOrDefaultAsync(m => m.Id == matterId.Trim(), ct);
            if (matter == null) return null;
            var client = await _context.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == matter.ClientId, ct);
            var policy = await GetActiveMatterBillingPolicyAsync(matter.Id, DateTime.UtcNow, ct);

            var normalized = NormalizeInputs(matter, client, policy, new OutcomeFeePlanCreateRequest
            {
                MatterId = matter.Id,
                BillingArrangement = policy?.ArrangementType ?? matter.FeeStructure,
                PrimaryPayorProfile = string.Equals(client?.Type, "Corporate", StringComparison.OrdinalIgnoreCase) ? "corporate" : "client"
            });

            var overlay = await GetCalibrationOverlayForGenerationAsync(matter, client, policy, normalized, ct);
            return new
            {
                matterId = matter.Id,
                hasCalibration = overlay != null,
                active = overlay?.ActiveSnapshot == null ? null : BuildCalibrationSnapshotDto(overlay.ActiveSnapshot),
                shadow = overlay?.ShadowSnapshot == null ? null : BuildCalibrationSnapshotDto(overlay.ShadowSnapshot),
                candidateCohorts = overlay?.CandidateCohorts ?? new List<object>()
            };
        }

        public async Task<object> RunCalibrationJobAsync(OutcomeFeeCalibrationJobRunRequest? request, string userId, CancellationToken ct)
        {
            var req = request ?? new OutcomeFeeCalibrationJobRunRequest();
            var days = Math.Clamp(req.Days <= 0 ? 365 : req.Days, 30, 1095);
            var minSampleSize = Math.Clamp(req.MinSampleSize <= 0 ? 5 : req.MinSampleSize, 2, 500);
            var cutoff = DateTime.UtcNow.AddDays(-days);
            var allowedScopes = new HashSet<string>(
                (req.CohortScopes is { Length: > 0 } ? req.CohortScopes : new[] { "combined", "practice_court_arrangement", "practice_arrangement" })
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s.Trim().ToLowerInvariant()),
                StringComparer.Ordinal);

            var plans = await _context.OutcomeFeePlans.AsNoTracking().Where(p => p.Status == "active").ToListAsync(ct);
            if (plans.Count == 0)
            {
                return new { days, created = 0, skipped = 0, shadowMode = req.ShadowMode, notes = "No active plans." };
            }

            var matterIds = plans.Select(p => p.MatterId).Distinct(StringComparer.Ordinal).ToArray();
            var clientIds = plans.Where(p => !string.IsNullOrWhiteSpace(p.ClientId)).Select(p => p.ClientId!).Distinct(StringComparer.Ordinal).ToArray();
            var planIds = plans.Select(p => p.Id).Distinct(StringComparer.Ordinal).ToArray();

            var matters = await _context.Matters.AsNoTracking().Where(m => matterIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, StringComparer.Ordinal, ct);
            var clients = clientIds.Length == 0
                ? new Dictionary<string, Client>(StringComparer.Ordinal)
                : await _context.Clients.AsNoTracking().Where(c => clientIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, StringComparer.Ordinal, ct);
            var events = await _context.OutcomeFeeUpdateEvents.AsNoTracking()
                .Where(e => planIds.Contains(e.PlanId) && e.CreatedAt >= cutoff)
                .OrderByDescending(e => e.CreatedAt)
                .ToListAsync(ct);

            var compareByPlan = events.Where(e => e.TriggerType != "outcome_label_feedback")
                .GroupBy(e => e.PlanId, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            var feedbackByPlan = events.Where(e => e.TriggerType == "outcome_label_feedback")
                .GroupBy(e => e.PlanId, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

            var observations = new List<CalibrationObservation>();
            foreach (var plan in plans)
            {
                if (!matters.TryGetValue(plan.MatterId, out var matter)) continue;
                clients.TryGetValue(plan.ClientId ?? string.Empty, out var client);
                var compare = compareByPlan.TryGetValue(plan.Id, out var cmp) ? ParseCompareObservation(cmp.ResultJson) : null;
                var feedback = feedbackByPlan.TryGetValue(plan.Id, out var f) ? ParseFeedbackObservation(f.PayloadJson) : (ActualOutcome: (string?)null, ActualFeesCollected: (decimal?)null, ActualMargin: (decimal?)null);
                var arrangement = ParseArrangementFromPlanMetadata(plan.MetadataJson) ?? "hourly";
                var candidates = BuildCalibrationCohortCandidates(matter, client, new OutcomeFeeInputs
                {
                    MatterId = matter.Id,
                    ClientId = matter.ClientId,
                    PracticeArea = matter.PracticeArea,
                    CourtType = matter.CourtType,
                    BillingArrangement = arrangement,
                    PrimaryPayorProfile = string.Equals(client?.Type, "Corporate", StringComparison.OrdinalIgnoreCase) ? "corporate" : "client",
                    Currency = "USD"
                }).Where(c => allowedScopes.Contains(c.Scope)).ToList();
                if (candidates.Count == 0) continue;

                foreach (var candidate in candidates)
                {
                    observations.Add(new CalibrationObservation
                    {
                        CohortKey = candidate.CohortKey,
                        PracticeArea = matter.PracticeArea,
                        CourtType = matter.CourtType,
                        ResponsibleAttorney = matter.ResponsibleAttorney,
                        ClientType = client?.Type,
                        ArrangementType = arrangement,
                        HoursSignedRatio = compare?.HoursSignedRatio ?? 0m,
                        CollectionsSignedRatio = compare?.CollectionsSignedRatio ?? 0m,
                        MarginCompressionRatio = compare?.MarginCompressionRatio ?? 0m,
                        LowDataCoverage = compare?.LowDataCoverage ?? false,
                        PhaseVersionDeltaHours = compare?.PhaseDeltaHours ?? new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase),
                        ActualOutcome = feedback.ActualOutcome,
                        ActualFeesCollected = feedback.ActualFeesCollected,
                        ActualMargin = feedback.ActualMargin
                    });
                }
            }

            var created = 0;
            var skipped = 0;
            var autoActivated = 0;
            var snapshots = new List<object>();
            var now = DateTime.UtcNow;
            foreach (var group in observations.GroupBy(o => o.CohortKey, StringComparer.Ordinal))
            {
                var sample = group.ToList();
                if (sample.Count < minSampleSize)
                {
                    skipped++;
                    continue;
                }

                var avgHoursSigned = Round2(sample.Average(s => s.HoursSignedRatio));
                var avgCollectionsSigned = Round2(sample.Average(s => s.CollectionsSignedRatio));
                var avgMarginCompression = Round2(sample.Average(s => s.MarginCompressionRatio));
                var phaseHourMultipliers = BuildSimplePhaseMultipliers(sample);
                var collectionCurveShifts = BuildSimpleCollectionCurveShifts(avgCollectionsSigned);
                var confidence = Round2(Math.Clamp(0.40m + Math.Min(0.35m, sample.Count / 40m) - Math.Min(0.12m, sample.Count(s => s.LowDataCoverage) / (decimal)sample.Count * 0.20m), 0m, 1m));
                var activateAsActive = !req.ShadowMode &&
                                       req.AutoActivateHighConfidence &&
                                       confidence >= Math.Clamp(req.AutoActivateConfidenceThreshold <= 0m ? 0.80m : req.AutoActivateConfidenceThreshold, 0m, 1m);

                var first = sample[0];
                var snapshot = new OutcomeFeeCalibrationSnapshot
                {
                    CohortKey = group.Key,
                    PracticeArea = first.PracticeArea,
                    ArrangementType = first.ArrangementType,
                    JurisdictionCode = null,
                    AsOfDate = now.Date,
                    Status = req.ShadowMode ? "shadow" : (activateAsActive ? "active" : "draft"),
                    SampleSize = sample.Count,
                    MetricsJson = SerializeJson(new
                    {
                        confidenceScore = confidence,
                        sampleSize = sample.Count,
                        avgHoursSignedRatio = avgHoursSigned,
                        avgCollectionsSignedRatio = avgCollectionsSigned,
                        avgMarginCompressionRatio = avgMarginCompression,
                        lowDataCoverageRate = Round2(sample.Count(s => s.LowDataCoverage) / (decimal)sample.Count)
                    }),
                    PayloadJson = SerializeJson(new
                    {
                        tuningSuggestions = new
                        {
                            globalHoursMultiplier = Round2(Math.Clamp(1m + avgHoursSigned, 0.65m, 1.75m)),
                            globalCollectionsMultiplier = Round2(Math.Clamp(1m + avgCollectionsSigned, 0.60m, 1.30m)),
                            phaseHourMultipliers,
                            collectionCurveShifts,
                            recommendedShadowMode = req.ShadowMode
                        },
                        cohort = new
                        {
                            key = group.Key,
                            practiceArea = first.PracticeArea,
                            courtType = first.CourtType,
                            responsibleAttorney = first.ResponsibleAttorney,
                            clientType = first.ClientType,
                            arrangementType = first.ArrangementType
                        },
                        suggestions = new
                        {
                            phaseHourMultiplierSuggestion = phaseHourMultipliers,
                            collectionsCurveShiftSuggestion = collectionCurveShifts
                        }
                    }),
                    MetadataJson = SerializeJson(new
                    {
                        engine = "outcome_fee_calibration_v1",
                        generatedBy = userId,
                        generatedAtUtc = now,
                        correlationId = req.CorrelationId,
                        notes = req.Notes,
                        sourceWindowDays = days,
                        shadowMode = req.ShadowMode,
                        autoActivateHighConfidence = req.AutoActivateHighConfidence,
                        autoActivateConfidenceThreshold = req.AutoActivateConfidenceThreshold
                    }),
                    CreatedAt = now,
                    UpdatedAt = now
                };

                if (snapshot.Status is "active" or "shadow")
                {
                    var existingSameStatus = await _context.OutcomeFeeCalibrationSnapshots
                        .Where(s => s.CohortKey == snapshot.CohortKey && s.Status == snapshot.Status)
                        .ToListAsync(ct);
                    foreach (var existing in existingSameStatus)
                    {
                        existing.Status = "superseded";
                        existing.UpdatedAt = now;
                    }
                }

                _context.OutcomeFeeCalibrationSnapshots.Add(snapshot);
                created++;
                if (activateAsActive) autoActivated++;
                snapshots.Add(new { snapshot.Id, snapshot.CohortKey, snapshot.Status, snapshot.SampleSize, confidenceScore = confidence, autoActivated = activateAsActive });
            }

            if (_context.ChangeTracker.HasChanges())
            {
                await _context.SaveChangesAsync(ct);
            }

            return new
            {
                days,
                minSampleSize,
                shadowMode = req.ShadowMode,
                autoActivateHighConfidence = req.AutoActivateHighConfidence,
                autoActivateConfidenceThreshold = req.AutoActivateConfidenceThreshold,
                cohortScopes = allowedScopes.OrderBy(x => x).ToArray(),
                created,
                skipped,
                autoActivated,
                snapshots
            };
        }

        public async Task<object?> ActivateCalibrationSnapshotAsync(string snapshotId, OutcomeFeeCalibrationSnapshotActionRequest request, string userId, CancellationToken ct)
        {
            var snapshot = await _context.OutcomeFeeCalibrationSnapshots.FirstOrDefaultAsync(s => s.Id == snapshotId, ct);
            if (snapshot == null) return null;

            var targetStatus = request.AsShadow ? "shadow" : "active";
            var currentSameStatus = await _context.OutcomeFeeCalibrationSnapshots
                .Where(s => s.CohortKey == snapshot.CohortKey && s.Id != snapshot.Id && s.Status == targetStatus)
                .ToListAsync(ct);
            foreach (var item in currentSameStatus)
            {
                item.Status = "superseded";
                item.UpdatedAt = DateTime.UtcNow;
            }

            snapshot.Status = targetStatus;
            snapshot.UpdatedAt = DateTime.UtcNow;
            snapshot.MetadataJson = MergeJson(snapshot.MetadataJson, new
            {
                activatedAtUtc = DateTime.UtcNow,
                activatedBy = userId,
                activationReason = request.Reason,
                activationCorrelationId = request.CorrelationId
            });

            await _context.SaveChangesAsync(ct);
            return BuildCalibrationSnapshotDto(snapshot);
        }

        public async Task<object?> RollbackCalibrationSnapshotAsync(string snapshotId, OutcomeFeeCalibrationRollbackRequest request, string userId, CancellationToken ct)
        {
            var source = await _context.OutcomeFeeCalibrationSnapshots.FirstOrDefaultAsync(s => s.Id == snapshotId, ct);
            if (source == null) return null;

            OutcomeFeeCalibrationSnapshot? target;
            if (!string.IsNullOrWhiteSpace(request.TargetSnapshotId))
            {
                target = await _context.OutcomeFeeCalibrationSnapshots.FirstOrDefaultAsync(s => s.Id == request.TargetSnapshotId && s.CohortKey == source.CohortKey, ct);
            }
            else
            {
                target = await _context.OutcomeFeeCalibrationSnapshots
                    .Where(s => s.CohortKey == source.CohortKey && s.Id != source.Id && (s.Status == "superseded" || s.Status == "rolled_back"))
                    .OrderByDescending(s => s.AsOfDate)
                    .ThenByDescending(s => s.UpdatedAt)
                    .FirstOrDefaultAsync(ct);
            }
            if (target == null) return null;

            var active = await _context.OutcomeFeeCalibrationSnapshots
                .Where(s => s.CohortKey == source.CohortKey && s.Status == "active")
                .ToListAsync(ct);
            foreach (var item in active)
            {
                item.Status = "rolled_back";
                item.UpdatedAt = DateTime.UtcNow;
            }

            target.Status = "active";
            target.UpdatedAt = DateTime.UtcNow;
            target.MetadataJson = MergeJson(target.MetadataJson, new
            {
                rollbackRestoredAtUtc = DateTime.UtcNow,
                rollbackRestoredBy = userId,
                rollbackSourceSnapshotId = source.Id,
                rollbackReason = request.Reason,
                rollbackCorrelationId = request.CorrelationId
            });

            await _context.SaveChangesAsync(ct);
            return new { source = BuildCalibrationSnapshotDto(source), restored = BuildCalibrationSnapshotDto(target) };
        }

        private async Task<OutcomeFeeCalibrationOverlay?> GetCalibrationOverlayForGenerationAsync(
            Matter matter,
            Client? client,
            MatterBillingPolicy? policy,
            OutcomeFeeInputs input,
            CancellationToken ct)
        {
            var candidates = BuildCalibrationCohortCandidates(matter, client, input);
            if (candidates.Count == 0) return null;
            var candidateKeys = candidates.Select(c => c.CohortKey).Distinct(StringComparer.Ordinal).ToArray();

            var snapshots = await _context.OutcomeFeeCalibrationSnapshots.AsNoTracking()
                .Where(s => candidateKeys.Contains(s.CohortKey) && (s.Status == "active" || s.Status == "shadow"))
                .OrderByDescending(s => s.AsOfDate)
                .ThenByDescending(s => s.UpdatedAt)
                .ToListAsync(ct);
            if (snapshots.Count == 0) return null;

            OutcomeFeeCalibrationSnapshot? Pick(string status)
            {
                foreach (var candidate in candidates)
                {
                    var match = snapshots
                        .Where(s => s.CohortKey == candidate.CohortKey && s.Status == status)
                        .OrderByDescending(s => s.SampleSize)
                        .ThenByDescending(s => s.AsOfDate)
                        .FirstOrDefault();
                    if (match != null) return match;
                }
                return null;
            }

            var active = Pick("active");
            var shadow = Pick("shadow");
            if (active == null && shadow == null) return null;

            return new OutcomeFeeCalibrationOverlay
            {
                ActiveSnapshot = active,
                ShadowSnapshot = shadow,
                Active = ParseCalibrationSnapshotPayload(active),
                Shadow = ParseCalibrationSnapshotPayload(shadow),
                CandidateCohorts = candidates.Select(c => new { c.Scope, c.CohortKey }).Cast<object>().ToList()
            };
        }

        private List<CalibrationCohortCandidate> BuildCalibrationCohortCandidates(Matter matter, Client? client, OutcomeFeeInputs input)
        {
            static string Norm(string? v, string fallback = "unknown")
                => string.IsNullOrWhiteSpace(v) ? fallback : v.Trim().ToLowerInvariant().Replace(' ', '_');

            var practice = Norm(matter.PracticeArea ?? input.PracticeArea, "general");
            var court = Norm(matter.CourtType ?? input.CourtType, "unspecified");
            var attorney = Norm(matter.ResponsibleAttorney, "unassigned");
            var clientType = Norm(client?.Type ?? (input.PrimaryPayorProfile == "corporate" ? "corporate" : "individual"));
            var arrangement = Norm(input.BillingArrangement, "hourly");

            return new List<CalibrationCohortCandidate>
            {
                new("combined", $"practice:{practice}|court:{court}|attorney:{attorney}|client_type:{clientType}|arrangement:{arrangement}"),
                new("practice_court_arrangement", $"practice:{practice}|court:{court}|arrangement:{arrangement}"),
                new("practice_arrangement", $"practice:{practice}|arrangement:{arrangement}")
            };
        }

        private string? BuildCombinedCalibrationCohortKey(Matter matter, Client? client, OutcomeFeePlan plan)
        {
            static string Norm(string? v, string fallback = "unknown")
                => string.IsNullOrWhiteSpace(v) ? fallback : v.Trim().ToLowerInvariant().Replace(' ', '_');

            var arrangement = Norm(ParseArrangementFromPlanMetadata(plan.MetadataJson) ?? "hourly", "hourly");
            return $"practice:{Norm(matter.PracticeArea, "general")}|court:{Norm(matter.CourtType, "unspecified")}|attorney:{Norm(matter.ResponsibleAttorney, "unassigned")}|client_type:{Norm(client?.Type, "individual")}|arrangement:{arrangement}";
        }

        private static string? ParseArrangementFromPlanMetadata(string? metadataJson)
        {
            if (string.IsNullOrWhiteSpace(metadataJson)) return null;
            try
            {
                using var doc = JsonDocument.Parse(metadataJson);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
                return doc.RootElement.TryGetProperty("billingArrangement", out var prop) && prop.ValueKind == JsonValueKind.String
                    ? prop.GetString()
                    : null;
            }
            catch { return null; }
        }

        private (decimal HoursSignedRatio, decimal CollectionsSignedRatio, decimal MarginCompressionRatio, bool LowDataCoverage, Dictionary<string, decimal> PhaseDeltaHours)? ParseCompareObservation(string? resultJson)
        {
            if (string.IsNullOrWhiteSpace(resultJson)) return null;
            try
            {
                using var doc = JsonDocument.Parse(resultJson);
                if (!doc.RootElement.TryGetProperty("compare", out var compare) || compare.ValueKind != JsonValueKind.Object) return null;
                if (!compare.TryGetProperty("driftSummary", out var drift) || drift.ValueKind != JsonValueKind.Object) return null;

                var plannedHours = GetDecimal(drift, "plannedHours");
                var actualHours = GetDecimal(drift, "actualHours");
                var plannedCollections = GetDecimal(drift, "plannedCollections");
                var actualCollections = GetDecimal(drift, "actualCollected");
                var marginCompression = GetDecimal(drift, "marginCompressionRatio");

                var lowDataCoverage = false;
                if (compare.TryGetProperty("scenarioDeltas", out var scenarioDeltas) && scenarioDeltas.ValueKind == JsonValueKind.Array)
                {
                    foreach (var row in scenarioDeltas.EnumerateArray())
                    {
                        if (row.ValueKind != JsonValueKind.Object) continue;
                        var key = row.TryGetProperty("scenarioKey", out var keyProp) && keyProp.ValueKind == JsonValueKind.String ? keyProp.GetString() : null;
                        if (!string.Equals(key, "base", StringComparison.OrdinalIgnoreCase)) continue;
                        if (!row.TryGetProperty("riskFlags", out var rf) || rf.ValueKind != JsonValueKind.Object) continue;
                        if (!rf.TryGetProperty("to", out var toFlags) || toFlags.ValueKind != JsonValueKind.Array) continue;
                        lowDataCoverage = toFlags.EnumerateArray().Any(v => v.ValueKind == JsonValueKind.String && string.Equals(v.GetString(), "low_data_coverage", StringComparison.OrdinalIgnoreCase));
                        break;
                    }
                }

                var phaseDeltas = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
                if (compare.TryGetProperty("phaseDeltas", out var phaseRows) && phaseRows.ValueKind == JsonValueKind.Array)
                {
                    foreach (var row in phaseRows.EnumerateArray())
                    {
                        if (row.ValueKind != JsonValueKind.Object) continue;
                        var phaseCode = row.TryGetProperty("phaseCode", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
                        if (string.IsNullOrWhiteSpace(phaseCode)) continue;
                        if (!row.TryGetProperty("delta", out var d) || d.ValueKind != JsonValueKind.Object) continue;
                        phaseDeltas[phaseCode] = GetDecimal(d, "hoursExpected");
                    }
                }

                return (
                    plannedHours <= 0m ? 0m : Round2((actualHours - plannedHours) / plannedHours),
                    plannedCollections <= 0m ? 0m : Round2((actualCollections - plannedCollections) / plannedCollections),
                    marginCompression,
                    lowDataCoverage,
                    phaseDeltas
                );
            }
            catch { return null; }
        }

        private (string? ActualOutcome, decimal? ActualFeesCollected, decimal? ActualMargin) ParseFeedbackObservation(string? payloadJson)
        {
            if (string.IsNullOrWhiteSpace(payloadJson)) return (null, null, null);
            try
            {
                using var doc = JsonDocument.Parse(payloadJson);
                var root = doc.RootElement;
                decimal? fees = root.TryGetProperty("ActualFeesCollected", out var f1) && f1.TryGetDecimal(out var d1) ? d1 : null;
                decimal? margin = root.TryGetProperty("ActualMargin", out var f2) && f2.TryGetDecimal(out var d2) ? d2 : null;
                return (GetString(root, "ActualOutcome") ?? GetString(root, "actualOutcome"), fees, margin);
            }
            catch { return (null, null, null); }
        }

        private static Dictionary<string, decimal> BuildSimplePhaseMultipliers(IReadOnlyList<CalibrationObservation> sample)
        {
            var avgHoursSigned = sample.Count == 0 ? 0m : sample.Average(s => s.HoursSignedRatio);
            var phaseAgg = new Dictionary<string, (decimal sum, int count)>(StringComparer.OrdinalIgnoreCase);
            foreach (var obs in sample)
            {
                foreach (var kvp in obs.PhaseVersionDeltaHours)
                {
                    var current = phaseAgg.TryGetValue(kvp.Key, out var v) ? v : (sum: 0m, count: 0);
                    phaseAgg[kvp.Key] = (current.sum + kvp.Value, current.count + 1);
                }
            }

            decimal BaseMultiplier(string phase)
            {
                var cohortBias = phaseAgg.TryGetValue(phase, out var agg) && agg.count > 0
                    ? Math.Clamp((agg.sum / agg.count) / 20m, -0.25m, 0.35m)
                    : avgHoursSigned * 0.5m;
                return Round2(Math.Clamp(1m + cohortBias, 0.70m, 1.50m));
            }

            return new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["intake"] = BaseMultiplier("intake"),
                ["pleading"] = BaseMultiplier("pleading"),
                ["discovery"] = BaseMultiplier("discovery"),
                ["motion"] = BaseMultiplier("motion"),
                ["settlement"] = BaseMultiplier("settlement"),
                ["trial_prep"] = BaseMultiplier("trial_prep"),
                ["trial"] = BaseMultiplier("trial")
            };
        }

        private static Dictionary<int, decimal> BuildSimpleCollectionCurveShifts(decimal avgCollectionsSigned)
        {
            return new Dictionary<int, decimal>
            {
                [30] = Round2(Math.Clamp(avgCollectionsSigned * -0.20m, -0.15m, 0.15m)),
                [60] = Round2(Math.Clamp(avgCollectionsSigned * -0.05m, -0.10m, 0.10m)),
                [90] = Round2(Math.Clamp(avgCollectionsSigned * 0.10m, -0.10m, 0.10m)),
                [120] = Round2(Math.Clamp(avgCollectionsSigned * 0.15m, -0.10m, 0.10m))
            };
        }

        private object BuildCalibrationSnapshotDto(OutcomeFeeCalibrationSnapshot snapshot)
        {
            object? metrics = null;
            object? payload = null;
            object? metadata = null;
            try { if (!string.IsNullOrWhiteSpace(snapshot.MetricsJson)) metrics = JsonSerializer.Deserialize<object>(snapshot.MetricsJson); } catch { }
            try { if (!string.IsNullOrWhiteSpace(snapshot.PayloadJson)) payload = JsonSerializer.Deserialize<object>(snapshot.PayloadJson); } catch { }
            try { if (!string.IsNullOrWhiteSpace(snapshot.MetadataJson)) metadata = JsonSerializer.Deserialize<object>(snapshot.MetadataJson); } catch { }
            return new { snapshot, metrics, payload, metadata };
        }

        private OutcomeFeeParsedCalibration? ParseCalibrationSnapshotPayload(OutcomeFeeCalibrationSnapshot? snapshot)
        {
            if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.PayloadJson)) return null;
            try
            {
                using var doc = JsonDocument.Parse(snapshot.PayloadJson);
                if (!doc.RootElement.TryGetProperty("tuningSuggestions", out var tuning) || tuning.ValueKind != JsonValueKind.Object) return null;
                var phaseMap = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
                if (tuning.TryGetProperty("phaseHourMultipliers", out var phases) && phases.ValueKind == JsonValueKind.Object)
                {
                    foreach (var p in phases.EnumerateObject())
                    {
                        if (p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetDecimal(out var d)) phaseMap[p.Name] = d;
                    }
                }
                var shifts = new Dictionary<int, decimal>();
                if (tuning.TryGetProperty("collectionCurveShifts", out var buckets) && buckets.ValueKind == JsonValueKind.Object)
                {
                    foreach (var b in buckets.EnumerateObject())
                    {
                        if (int.TryParse(b.Name, out var bucket) && b.Value.ValueKind == JsonValueKind.Number && b.Value.TryGetDecimal(out var d)) shifts[bucket] = d;
                    }
                }
                return new OutcomeFeeParsedCalibration
                {
                    GlobalHoursMultiplier = Math.Max(0.01m, GetDecimal(tuning, "globalHoursMultiplier")),
                    GlobalCollectionsMultiplier = Math.Max(0.01m, GetDecimal(tuning, "globalCollectionsMultiplier")),
                    PhaseHourMultipliers = phaseMap,
                    CollectionCurveShifts = shifts,
                    RecommendedShadowMode = GetBool(tuning, "recommendedShadowMode")
                };
            }
            catch { return null; }
        }

        private sealed class OutcomeFeeCalibrationOverlay
        {
            public OutcomeFeeCalibrationSnapshot? ActiveSnapshot { get; set; }
            public OutcomeFeeCalibrationSnapshot? ShadowSnapshot { get; set; }
            public OutcomeFeeParsedCalibration? Active { get; set; }
            public OutcomeFeeParsedCalibration? Shadow { get; set; }
            public List<object> CandidateCohorts { get; set; } = new();
        }

        private sealed class OutcomeFeeParsedCalibration
        {
            public decimal GlobalHoursMultiplier { get; set; } = 1m;
            public decimal GlobalCollectionsMultiplier { get; set; } = 1m;
            public Dictionary<string, decimal> PhaseHourMultipliers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
            public Dictionary<int, decimal> CollectionCurveShifts { get; set; } = new();
            public bool RecommendedShadowMode { get; set; }
        }

        private sealed record CalibrationCohortCandidate(string Scope, string CohortKey);

        private sealed class CalibrationObservation
        {
            public string CohortKey { get; set; } = string.Empty;
            public string? PracticeArea { get; set; }
            public string? CourtType { get; set; }
            public string? ResponsibleAttorney { get; set; }
            public string? ClientType { get; set; }
            public string? ArrangementType { get; set; }
            public decimal HoursSignedRatio { get; set; }
            public decimal CollectionsSignedRatio { get; set; }
            public decimal MarginCompressionRatio { get; set; }
            public bool LowDataCoverage { get; set; }
            public Dictionary<string, decimal> PhaseVersionDeltaHours { get; set; } = new(StringComparer.OrdinalIgnoreCase);
            public string? ActualOutcome { get; set; }
            public decimal? ActualFeesCollected { get; set; }
            public decimal? ActualMargin { get; set; }
        }
    }
}
