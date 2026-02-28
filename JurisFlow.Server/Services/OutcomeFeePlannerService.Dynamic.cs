using System.Text.Json;
using JurisFlow.Server.Models;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace JurisFlow.Server.Services
{
    public partial class OutcomeFeePlannerService
    {
        public async Task<OutcomeFeePlanVersionCompareResult?> CompareVersionsAsync(
            string planId,
            string? fromVersionId,
            string? toVersionId,
            CancellationToken ct)
        {
            var plan = await _context.OutcomeFeePlans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == planId, ct);
            if (plan == null) return null;

            var versions = await _context.OutcomeFeePlanVersions.AsNoTracking()
                .Where(v => v.PlanId == plan.Id)
                .OrderByDescending(v => v.VersionNumber)
                .ThenByDescending(v => v.GeneratedAt)
                .ToListAsync(ct);
            if (versions.Count == 0) return null;

            var toVersion = !string.IsNullOrWhiteSpace(toVersionId)
                ? versions.FirstOrDefault(v => v.Id == toVersionId)
                : versions.FirstOrDefault(v => v.Id == plan.CurrentVersionId) ?? versions.FirstOrDefault();
            if (toVersion == null) return null;

            var fromVersion = !string.IsNullOrWhiteSpace(fromVersionId)
                ? versions.FirstOrDefault(v => v.Id == fromVersionId)
                : versions
                    .Where(v => v.Id != toVersion.Id)
                    .OrderByDescending(v => v.VersionNumber)
                    .ThenByDescending(v => v.GeneratedAt)
                    .FirstOrDefault();

            var toScenarios = await _context.OutcomeFeeScenarios.AsNoTracking()
                .Where(s => s.PlanVersionId == toVersion.Id)
                .ToListAsync(ct);
            var fromScenarios = fromVersion == null
                ? new List<OutcomeFeeScenario>()
                : await _context.OutcomeFeeScenarios.AsNoTracking()
                    .Where(s => s.PlanVersionId == fromVersion.Id)
                    .ToListAsync(ct);

            var toScenarioIds = toScenarios.Select(s => s.Id).ToList();
            var fromScenarioIds = fromScenarios.Select(s => s.Id).ToList();
            var phaseForecasts = await _context.OutcomeFeePhaseForecasts.AsNoTracking()
                .Where(p => toScenarioIds.Contains(p.ScenarioId) || fromScenarioIds.Contains(p.ScenarioId))
                .ToListAsync(ct);

            var scenarioDeltas = BuildScenarioDeltas(fromScenarios, toScenarios);
            var phaseDeltas = BuildPhaseDeltas(fromScenarios, toScenarios, phaseForecasts);
            var actuals = await BuildActualsAsync(plan.MatterId, ct);
            var driftSummary = BuildDriftSummary(fromScenarios, toScenarios, phaseForecasts, actuals);

            return new OutcomeFeePlanVersionCompareResult
            {
                PlanId = plan.Id,
                MatterId = plan.MatterId,
                FromVersionId = fromVersion?.Id,
                ToVersionId = toVersion.Id,
                FromVersionNumber = fromVersion?.VersionNumber,
                ToVersionNumber = toVersion.VersionNumber,
                ComparedAtUtc = DateTime.UtcNow,
                Actuals = actuals,
                DriftSummary = driftSummary,
                ScenarioDeltas = scenarioDeltas,
                PhaseDeltas = phaseDeltas
            };
        }

        public async Task<OutcomeFeePlanTriggerResult> TryProcessTriggerAsync(
            OutcomeFeePlanTriggerRequest request,
            string userId,
            CancellationToken ct)
        {
            if (request == null) throw new InvalidOperationException("Request body is required.");

            var normalizedTriggerType = string.IsNullOrWhiteSpace(request.TriggerType) ? "manual_trigger" : request.TriggerType!.Trim().ToLowerInvariant();
            var normalizedEntityType = string.IsNullOrWhiteSpace(request.TriggerEntityType) ? null : request.TriggerEntityType!.Trim();
            var normalizedEntityId = string.IsNullOrWhiteSpace(request.TriggerEntityId) ? null : request.TriggerEntityId!.Trim();
            var normalizedMatterId = string.IsNullOrWhiteSpace(request.MatterId) ? null : request.MatterId!.Trim();

            normalizedMatterId ??= await ResolveMatterIdForTriggerAsync(normalizedEntityType, normalizedEntityId, ct);
            if (string.IsNullOrWhiteSpace(normalizedMatterId))
            {
                return new OutcomeFeePlanTriggerResult
                {
                    TriggerAccepted = false,
                    Recomputed = false,
                    DriftDetected = false,
                    TriggerType = normalizedTriggerType,
                    TriggerEntityType = normalizedEntityType,
                    TriggerEntityId = normalizedEntityId
                };
            }

            var existingPlan = await _context.OutcomeFeePlans
                .AsNoTracking()
                .OrderByDescending(p => p.UpdatedAt)
                .FirstOrDefaultAsync(p => p.MatterId == normalizedMatterId && p.Status == "active", ct);
            if (existingPlan == null)
            {
                return new OutcomeFeePlanTriggerResult
                {
                    TriggerAccepted = true,
                    MatterId = normalizedMatterId,
                    Recomputed = false,
                    DriftDetected = false,
                    TriggerType = normalizedTriggerType,
                    TriggerEntityType = normalizedEntityType,
                    TriggerEntityId = normalizedEntityId
                };
            }

            var previousVersionId = existingPlan.CurrentVersionId;
            OutcomeFeePlanDetailResult? recomputed = null;
            if (request.AllowFullRecomputeFallback)
            {
                recomputed = await RecomputePlanAsync(existingPlan.Id, new OutcomeFeePlanRecomputeRequest
                {
                    TriggerType = normalizedTriggerType,
                    TriggerEntityType = normalizedEntityType,
                    TriggerEntityId = normalizedEntityId,
                    Reason = request.Reason,
                    CorrelationId = request.CorrelationId
                }, userId, ct);
            }

            var result = new OutcomeFeePlanTriggerResult
            {
                TriggerAccepted = true,
                Recomputed = recomputed != null,
                PlanId = existingPlan.Id,
                MatterId = normalizedMatterId,
                PreviousVersionId = previousVersionId,
                CurrentVersionId = recomputed?.CurrentVersion?.Id ?? existingPlan.CurrentVersionId,
                TriggerType = normalizedTriggerType,
                TriggerEntityType = normalizedEntityType,
                TriggerEntityId = normalizedEntityId
            };

            if (recomputed?.CurrentVersion != null)
            {
                var compare = await CompareVersionsAsync(existingPlan.Id, previousVersionId, recomputed.CurrentVersion.Id, ct);
                result.Compare = compare;
                result.DriftSummary = compare?.DriftSummary;

                var driftFlags = ParseDriftFlags(compare?.DriftSummary);
                var hoursThreshold = request.HoursDriftThresholdRatio ?? 0.20m;
                var collectionsThreshold = request.CollectionsDriftThresholdRatio ?? 0.20m;
                var marginThreshold = request.MarginCompressionThresholdRatio ?? 0.15m;

                result.DriftDetected =
                    driftFlags.hoursDriftRatio >= hoursThreshold ||
                    driftFlags.collectionsDriftRatio >= collectionsThreshold ||
                    driftFlags.marginCompressionRatio >= marginThreshold ||
                    driftFlags.collectionsRiskWorsened;

                var reviewItemsQueued = 0;
                var notificationsQueued = 0;
                if (result.DriftDetected && request.QueueReviewOnDrift)
                {
                    reviewItemsQueued += await QueueDriftReviewAsync(existingPlan, recomputed, result, driftFlags, ct);
                }
                if (result.DriftDetected && request.QueueNotificationOnDrift)
                {
                    notificationsQueued += await QueueDriftOutboxNotificationAsync(existingPlan, recomputed, result, driftFlags, ct);
                }

                result.ReviewItemsQueued = reviewItemsQueued;
                result.NotificationsQueued = notificationsQueued;

                await UpdateLatestOutcomeFeeUpdateEventAsync(
                    existingPlan.Id,
                    recomputed.CurrentVersion.Id,
                    normalizedTriggerType,
                    normalizedEntityType,
                    normalizedEntityId,
                    request,
                    result,
                    hoursThreshold,
                    collectionsThreshold,
                    marginThreshold,
                    ct);

                if (reviewItemsQueued > 0 || notificationsQueued > 0)
                {
                    await _context.SaveChangesAsync(ct);
                }
            }

            return result;
        }

        public async Task<object> GetPortfolioMetricsAsync(int days, CancellationToken ct)
        {
            var windowDays = Math.Clamp(days <= 0 ? 90 : days, 7, 365);
            var cutoff = DateTime.UtcNow.AddDays(-windowDays);

            var recentEvents = await _context.OutcomeFeeUpdateEvents
                .AsNoTracking()
                .Where(e => e.CreatedAt >= cutoff && e.ResultJson != null)
                .OrderByDescending(e => e.CreatedAt)
                .ToListAsync(ct);

            var activePlanIds = await _context.OutcomeFeePlans
                .AsNoTracking()
                .Where(p => p.Status == "active")
                .Select(p => p.Id)
                .ToListAsync(ct);

            var latestCompareByPlan = new Dictionary<string, (decimal hours, decimal collections, decimal margin, string severity, bool collectionsRiskWorsened)>(StringComparer.Ordinal);
            foreach (var evt in recentEvents)
            {
                if (string.IsNullOrWhiteSpace(evt.PlanId) || latestCompareByPlan.ContainsKey(evt.PlanId))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(evt.ResultJson))
                {
                    continue;
                }

                try
                {
                    using var doc = JsonDocument.Parse(evt.ResultJson);
                    var root = doc.RootElement;
                    if (!root.TryGetProperty("compare", out var compare) || compare.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var drift = compare.TryGetProperty("driftSummary", out var driftSummary) && driftSummary.ValueKind == JsonValueKind.Object
                        ? driftSummary
                        : (root.TryGetProperty("drift", out var resultDrift) && resultDrift.ValueKind == JsonValueKind.Object ? resultDrift : default);
                    if (drift.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    latestCompareByPlan[evt.PlanId] = (
                        hours: GetDecimal(drift, "hoursDriftRatio"),
                        collections: GetDecimal(drift, "collectionsDriftRatio"),
                        margin: GetDecimal(drift, "marginCompressionRatio"),
                        severity: (GetString(drift, "severity") ?? "low").ToLowerInvariant(),
                        collectionsRiskWorsened: GetBool(drift, "collectionsRiskWorsened"));
                }
                catch
                {
                    // ignore malformed result payloads; metrics endpoint is best-effort
                }
            }

            var rows = latestCompareByPlan.Values.ToList();
            var comparesUsed = rows.Count;
            var avgHours = comparesUsed == 0 ? 0m : Round2(rows.Average(r => r.hours));
            var avgCollections = comparesUsed == 0 ? 0m : Round2(rows.Average(r => r.collections));
            var avgMargin = comparesUsed == 0 ? 0m : Round2(rows.Average(r => r.margin));
            var weightedError = Round2((avgHours * 0.35m) + (avgCollections * 0.40m) + (avgMargin * 0.25m));
            var forecastAccuracy = Round2(Math.Clamp(1m - weightedError, 0m, 1m));

            return new
            {
                days = windowDays,
                plansObserved = activePlanIds.Count,
                comparesUsed,
                dataQuality = "event_compare_proxy_v1",
                metrics = new
                {
                    forecastAccuracy,
                    collectionsForecastError = avgCollections,
                    marginForecastError = avgMargin,
                    staffingVariance = avgHours,
                    avgHoursDriftRatio = avgHours,
                    avgCollectionsDriftRatio = avgCollections,
                    avgMarginCompressionRatio = avgMargin,
                    driftHighCount = rows.Count(r => r.severity == "high"),
                    driftMediumCount = rows.Count(r => r.severity == "medium"),
                    collectionsRiskWorsenedCount = rows.Count(r => r.collectionsRiskWorsened)
                }
            };
        }

        private async Task<string?> ResolveMatterIdForTriggerAsync(string? triggerEntityType, string? triggerEntityId, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(triggerEntityType) || string.IsNullOrWhiteSpace(triggerEntityId))
            {
                return null;
            }

            var type = triggerEntityType.Trim().ToLowerInvariant();
            var id = triggerEntityId.Trim();

            if (type is "matter" or "matters")
            {
                return await _context.Matters.AsNoTracking().Where(m => m.Id == id).Select(m => m.Id).FirstOrDefaultAsync(ct);
            }

            if (type is "timeentry" or "time_entry" or "time")
            {
                return await _context.TimeEntries.AsNoTracking().Where(t => t.Id == id).Select(t => t.MatterId).FirstOrDefaultAsync(ct);
            }

            if (type is "expense" or "expenses")
            {
                return await _context.Expenses.AsNoTracking().Where(e => e.Id == id).Select(e => e.MatterId).FirstOrDefaultAsync(ct);
            }

            if (type is "invoice" or "invoices")
            {
                return await _context.Invoices.AsNoTracking().Where(i => i.Id == id).Select(i => i.MatterId).FirstOrDefaultAsync(ct);
            }

            if (type is "paymenttransaction" or "payment_transaction" or "payment")
            {
                var tx = await _context.PaymentTransactions.AsNoTracking()
                    .Where(p => p.Id == id)
                    .Select(p => new { p.MatterId, p.InvoiceId })
                    .FirstOrDefaultAsync(ct);
                if (tx == null) return null;
                if (!string.IsNullOrWhiteSpace(tx.MatterId)) return tx.MatterId;
                if (string.IsNullOrWhiteSpace(tx.InvoiceId)) return null;
                return await _context.Invoices.AsNoTracking().Where(i => i.Id == tx.InvoiceId).Select(i => i.MatterId).FirstOrDefaultAsync(ct);
            }

            if (type is "efilingsubmission" or "efiling_submission" or "submission")
            {
                return await _context.EfilingSubmissions.AsNoTracking().Where(s => s.Id == id).Select(s => s.MatterId).FirstOrDefaultAsync(ct);
            }

            if (type is "courtdocketentry" or "court_docket_entry" or "docket")
            {
                return await _context.CourtDocketEntries.AsNoTracking().Where(d => d.Id == id).Select(d => d.MatterId).FirstOrDefaultAsync(ct);
            }

            return null;
        }

        private async Task<object> BuildActualsAsync(string matterId, CancellationToken ct)
        {
            var timeEntries = await _context.TimeEntries.AsNoTracking()
                .Where(t => t.MatterId == matterId)
                .ToListAsync(ct);
            var approvedTime = timeEntries.Where(t => string.Equals(t.ApprovalStatus, "Approved", StringComparison.OrdinalIgnoreCase) && t.IsBillable);
            var actualHours = Round2(approvedTime.Sum(t => Convert.ToDecimal(t.Duration) / 60m));
            var actualTimeFees = Round2(approvedTime.Sum(t => (Convert.ToDecimal(t.Duration) / 60m) * Convert.ToDecimal(t.Rate)));

            var expenses = await _context.Expenses.AsNoTracking()
                .Where(e => e.MatterId == matterId)
                .ToListAsync(ct);
            var approvedExpenses = expenses.Where(e => string.Equals(e.ApprovalStatus, "Approved", StringComparison.OrdinalIgnoreCase));
            var actualExpenses = Round2(approvedExpenses.Sum(e => Convert.ToDecimal(e.Amount)));

            var invoices = await _context.Invoices.AsNoTracking()
                .Where(i => i.MatterId == matterId)
                .ToListAsync(ct);
            var invoiceIds = invoices.Select(i => i.Id).ToList();

            decimal collectedNet;
            decimal appliedAllocations = 0m;
            if (invoiceIds.Count > 0)
            {
                var allocations = await _context.BillingPaymentAllocations.AsNoTracking()
                    .Where(a => invoiceIds.Contains(a.InvoiceId))
                    .ToListAsync(ct);
                appliedAllocations = Round2(allocations.Sum(a => string.Equals(a.Status, "Reversed", StringComparison.OrdinalIgnoreCase) ? -a.Amount : a.Amount));
            }

            if (appliedAllocations != 0m)
            {
                collectedNet = appliedAllocations;
            }
            else
            {
                var payments = await _context.PaymentTransactions.AsNoTracking()
                    .Where(p => p.MatterId == matterId)
                    .ToListAsync(ct);
                collectedNet = Round2(payments.Sum(p =>
                {
                    var status = p.Status ?? string.Empty;
                    if (string.Equals(status, "Succeeded", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(status, "Partially Refunded", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(status, "Refunded", StringComparison.OrdinalIgnoreCase))
                    {
                        var refunded = p.RefundAmount ?? 0m;
                        return p.Amount - refunded;
                    }
                    return 0m;
                }));
            }

            var invoicedTotal = Round2(invoices.Sum(i => i.Total));
            var openBalance = Round2(invoices.Sum(i => i.Balance));
            var actualMarginProxy = Round2(collectedNet - actualExpenses);

            return new
            {
                matterId,
                invoiceCount = invoices.Count,
                timeEntryCount = timeEntries.Count,
                expenseCount = expenses.Count,
                actualHours,
                actualTimeFees,
                actualExpenses,
                invoicedTotal,
                openBalance,
                collectedNet,
                actualMarginProxy
            };
        }

        private List<object> BuildScenarioDeltas(IReadOnlyList<OutcomeFeeScenario> fromScenarios, IReadOnlyList<OutcomeFeeScenario> toScenarios)
        {
            var fromByKey = fromScenarios.ToDictionary(s => s.ScenarioKey, StringComparer.OrdinalIgnoreCase);
            var deltas = new List<object>();

            foreach (var to in toScenarios.OrderBy(s => s.ScenarioKey, StringComparer.OrdinalIgnoreCase))
            {
                fromByKey.TryGetValue(to.ScenarioKey, out var from);
                var fromFlags = ParseRiskFlags(from?.MetadataJson);
                var toFlags = ParseRiskFlags(to.MetadataJson);
                var added = toFlags.Except(fromFlags, StringComparer.OrdinalIgnoreCase).ToArray();
                var removed = fromFlags.Except(toFlags, StringComparer.OrdinalIgnoreCase).ToArray();

                deltas.Add(new
                {
                    scenarioKey = to.ScenarioKey,
                    name = to.Name,
                    from = from == null ? null : new
                    {
                        versionScenarioId = from.Id,
                        probability = from.Probability,
                        budgetTotal = from.BudgetTotal,
                        expectedCollected = from.ExpectedCollected,
                        expectedMargin = from.ExpectedMargin,
                        confidenceScore = from.ConfidenceScore,
                        driverSummary = from.DriverSummary
                    },
                    to = new
                    {
                        versionScenarioId = to.Id,
                        probability = to.Probability,
                        budgetTotal = to.BudgetTotal,
                        expectedCollected = to.ExpectedCollected,
                        expectedMargin = to.ExpectedMargin,
                        confidenceScore = to.ConfidenceScore,
                        driverSummary = to.DriverSummary
                    },
                    delta = new
                    {
                        probability = Round2(to.Probability - (from?.Probability ?? 0m)),
                        budgetTotal = Round2(to.BudgetTotal - (from?.BudgetTotal ?? 0m)),
                        expectedCollected = Round2(to.ExpectedCollected - (from?.ExpectedCollected ?? 0m)),
                        expectedMargin = Round2(to.ExpectedMargin - (from?.ExpectedMargin ?? 0m)),
                        confidenceScore = Round2((to.ConfidenceScore ?? 0m) - (from?.ConfidenceScore ?? 0m))
                    },
                    riskFlags = new
                    {
                        from = fromFlags,
                        to = toFlags,
                        added,
                        removed
                    }
                });
            }

            return deltas;
        }

        private List<object> BuildPhaseDeltas(
            IReadOnlyList<OutcomeFeeScenario> fromScenarios,
            IReadOnlyList<OutcomeFeeScenario> toScenarios,
            IReadOnlyList<OutcomeFeePhaseForecast> phaseForecasts)
        {
            var toBase = toScenarios.FirstOrDefault(s => string.Equals(s.ScenarioKey, "base", StringComparison.OrdinalIgnoreCase))
                ?? toScenarios.OrderByDescending(s => s.Probability).FirstOrDefault();
            if (toBase == null) return new List<object>();

            var fromBase = fromScenarios.FirstOrDefault(s => string.Equals(s.ScenarioKey, "base", StringComparison.OrdinalIgnoreCase))
                ?? fromScenarios.OrderByDescending(s => s.Probability).FirstOrDefault();

            var toPhases = phaseForecasts.Where(p => p.ScenarioId == toBase.Id)
                .OrderBy(p => p.PhaseOrder)
                .ThenBy(p => p.CreatedAt)
                .ToList();
            var fromPhases = fromBase == null
                ? new List<OutcomeFeePhaseForecast>()
                : phaseForecasts.Where(p => p.ScenarioId == fromBase.Id)
                    .OrderBy(p => p.PhaseOrder)
                    .ThenBy(p => p.CreatedAt)
                    .ToList();

            var fromByCode = fromPhases.GroupBy(p => p.PhaseCode, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var rows = new List<object>();
            foreach (var to in toPhases)
            {
                fromByCode.TryGetValue(to.PhaseCode, out var from);
                var hoursDelta = Round2(to.HoursExpected - (from?.HoursExpected ?? 0m));
                var feeDelta = Round2(to.FeeExpected - (from?.FeeExpected ?? 0m));
                var expenseDelta = Round2(to.ExpenseExpected - (from?.ExpenseExpected ?? 0m));
                var durationDelta = to.DurationDaysExpected - (from?.DurationDaysExpected ?? 0);
                var feeDeltaRatio = (from?.FeeExpected ?? 0m) <= 0m ? 0m : Round2(Math.Abs(feeDelta) / from!.FeeExpected);
                rows.Add(new
                {
                    phaseCode = to.PhaseCode,
                    to.Name,
                    phaseOrder = to.PhaseOrder,
                    from = from == null ? null : new
                    {
                        hoursExpected = from.HoursExpected,
                        feeExpected = from.FeeExpected,
                        expenseExpected = from.ExpenseExpected,
                        durationDaysExpected = from.DurationDaysExpected
                    },
                    to = new
                    {
                        hoursExpected = to.HoursExpected,
                        feeExpected = to.FeeExpected,
                        expenseExpected = to.ExpenseExpected,
                        durationDaysExpected = to.DurationDaysExpected
                    },
                    delta = new
                    {
                        hoursExpected = hoursDelta,
                        feeExpected = feeDelta,
                        expenseExpected = expenseDelta,
                        durationDaysExpected = durationDelta,
                        feeDeltaRatio
                    }
                });
            }

            return rows;
        }

        private object BuildDriftSummary(
            IReadOnlyList<OutcomeFeeScenario> fromScenarios,
            IReadOnlyList<OutcomeFeeScenario> toScenarios,
            IReadOnlyList<OutcomeFeePhaseForecast> phaseForecasts,
            object actuals)
        {
            var toBase = toScenarios.FirstOrDefault(s => string.Equals(s.ScenarioKey, "base", StringComparison.OrdinalIgnoreCase))
                ?? toScenarios.OrderByDescending(s => s.Probability).FirstOrDefault();
            var fromBase = fromScenarios.FirstOrDefault(s => string.Equals(s.ScenarioKey, "base", StringComparison.OrdinalIgnoreCase))
                ?? fromScenarios.OrderByDescending(s => s.Probability).FirstOrDefault();

            var plannedHours = toBase == null
                ? 0m
                : Round2(phaseForecasts.Where(p => p.ScenarioId == toBase.Id).Sum(p => p.HoursExpected));
            var plannedCollections = Round2(toBase?.ExpectedCollected ?? 0m);
            var plannedMargin = Round2(toBase?.ExpectedMargin ?? 0m);

            var actualsJson = JsonSerializer.Serialize(actuals);
            using var actualDoc = JsonDocument.Parse(actualsJson);
            var root = actualDoc.RootElement;
            var actualHours = root.TryGetProperty("actualHours", out var ah) && ah.TryGetDecimal(out var ahd) ? ahd : 0m;
            var actualCollected = root.TryGetProperty("collectedNet", out var ac) && ac.TryGetDecimal(out var acd) ? acd : 0m;
            var actualMarginProxy = root.TryGetProperty("actualMarginProxy", out var am) && am.TryGetDecimal(out var amd) ? amd : 0m;

            var hoursDriftRatio = plannedHours <= 0m ? 0m : Round2(Math.Abs(actualHours - plannedHours) / plannedHours);
            var collectionsDriftRatio = plannedCollections <= 0m ? 0m : Round2(Math.Abs(actualCollected - plannedCollections) / plannedCollections);
            var marginCompressionRatio = plannedMargin <= 0m ? 0m : Round2(Math.Max(0m, (plannedMargin - actualMarginProxy) / plannedMargin));

            var fromFlags = ParseRiskFlags(fromBase?.MetadataJson);
            var toFlags = ParseRiskFlags(toBase?.MetadataJson);
            var collectionsRiskWorsened = !fromFlags.Contains("high_collections_risk", StringComparer.OrdinalIgnoreCase)
                && toFlags.Contains("high_collections_risk", StringComparer.OrdinalIgnoreCase);

            var severity = (hoursDriftRatio, collectionsDriftRatio, marginCompressionRatio) switch
            {
                var x when x.Item1 >= 0.40m || x.Item2 >= 0.40m || x.Item3 >= 0.30m => "high",
                var x when x.Item1 >= 0.20m || x.Item2 >= 0.20m || x.Item3 >= 0.15m => "medium",
                _ => "low"
            };

            return new
            {
                baseScenarioVersionFrom = fromBase?.Id,
                baseScenarioVersionTo = toBase?.Id,
                plannedHours,
                actualHours,
                hoursDriftRatio,
                plannedCollections,
                actualCollected,
                collectionsDriftRatio,
                plannedMargin,
                actualMarginProxy,
                marginCompressionRatio,
                collectionsRiskWorsened,
                severity,
                generatedAtUtc = DateTime.UtcNow
            };
        }

        private (decimal hoursDriftRatio, decimal collectionsDriftRatio, decimal marginCompressionRatio, bool collectionsRiskWorsened, string severity) ParseDriftFlags(object? driftSummary)
        {
            if (driftSummary == null) return (0m, 0m, 0m, false, "low");
            try
            {
                using var doc = JsonDocument.Parse(JsonSerializer.Serialize(driftSummary));
                var root = doc.RootElement;
                return (
                    GetDecimal(root, "hoursDriftRatio"),
                    GetDecimal(root, "collectionsDriftRatio"),
                    GetDecimal(root, "marginCompressionRatio"),
                    GetBool(root, "collectionsRiskWorsened"),
                    GetString(root, "severity") ?? "low"
                );
            }
            catch
            {
                return (0m, 0m, 0m, false, "low");
            }
        }

        private async Task<int> QueueDriftReviewAsync(
            OutcomeFeePlan plan,
            OutcomeFeePlanDetailResult recomputed,
            OutcomeFeePlanTriggerResult triggerResult,
            (decimal hoursDriftRatio, decimal collectionsDriftRatio, decimal marginCompressionRatio, bool collectionsRiskWorsened, string severity) driftFlags,
            CancellationToken ct)
        {
            var existing = await _context.IntegrationReviewQueueItems
                .FirstOrDefaultAsync(r =>
                    r.ProviderKey == "outcome_fee_planner" &&
                    r.ItemType == "outcome_fee_forecast_drift_review" &&
                    r.SourceType == nameof(OutcomeFeePlan) &&
                    r.SourceId == plan.Id &&
                    (r.Status == IntegrationReviewQueueStatuses.Pending || r.Status == IntegrationReviewQueueStatuses.InReview), ct);
            if (existing != null)
            {
                existing.UpdatedAt = DateTime.UtcNow;
                existing.ContextJson = SerializeJson(new
                {
                    trigger = new { triggerResult.TriggerType, triggerResult.TriggerEntityType, triggerResult.TriggerEntityId },
                    compare = triggerResult.Compare,
                    drift = triggerResult.DriftSummary
                });
                return 0;
            }

            _context.IntegrationReviewQueueItems.Add(new IntegrationReviewQueueItem
            {
                ProviderKey = "outcome_fee_planner",
                ItemType = "outcome_fee_forecast_drift_review",
                SourceType = nameof(OutcomeFeePlan),
                SourceId = plan.Id,
                Status = IntegrationReviewQueueStatuses.Pending,
                Priority = driftFlags.severity == "high" ? "high" : "medium",
                Title = "Outcome-to-Fee forecast drift detected",
                Summary = $"MatterId={plan.MatterId}, Trigger={triggerResult.TriggerType}, Severity={driftFlags.severity}",
                ContextJson = SerializeJson(new
                {
                    planId = plan.Id,
                    matterId = plan.MatterId,
                    currentVersionId = recomputed.CurrentVersion?.Id,
                    previousVersionId = triggerResult.PreviousVersionId,
                    trigger = new { triggerResult.TriggerType, triggerResult.TriggerEntityType, triggerResult.TriggerEntityId },
                    drift = triggerResult.DriftSummary,
                    compare = triggerResult.Compare
                }),
                SuggestedActionsJson = SerializeJson(new[] { "review_plan_version_delta", "recompute_with_override", "ack_drift" }),
                DueAt = DateTime.UtcNow.AddHours(24),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            return 1;
        }

        private async Task<int> QueueDriftOutboxNotificationAsync(
            OutcomeFeePlan plan,
            OutcomeFeePlanDetailResult recomputed,
            OutcomeFeePlanTriggerResult triggerResult,
            (decimal hoursDriftRatio, decimal collectionsDriftRatio, decimal marginCompressionRatio, bool collectionsRiskWorsened, string severity) driftFlags,
            CancellationToken ct)
        {
            var currentVersionId = recomputed.CurrentVersion?.Id;
            if (string.IsNullOrWhiteSpace(currentVersionId)) return 0;

            var idempotencyKey = $"outcome_fee_plan:drift:{plan.Id}:{currentVersionId}";
            var existing = await _context.IntegrationOutboxEvents
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.IdempotencyKey == idempotencyKey, ct);
            if (existing != null) return 0;

            _context.IntegrationOutboxEvents.Add(new IntegrationOutboxEvent
            {
                ProviderKey = "outcome_fee_planner",
                EventType = "outcome_fee_plan.drift_detected",
                EntityType = nameof(OutcomeFeePlan),
                EntityId = plan.Id,
                IdempotencyKey = idempotencyKey,
                CorrelationId = recomputed.CurrentVersion?.CorrelationId ?? plan.CorrelationId,
                Status = IntegrationEventStatuses.Pending,
                PayloadJson = SerializeJson(new
                {
                    planId = plan.Id,
                    matterId = plan.MatterId,
                    currentVersionId,
                    previousVersionId = triggerResult.PreviousVersionId,
                    trigger = new { triggerResult.TriggerType, triggerResult.TriggerEntityType, triggerResult.TriggerEntityId },
                    drift = triggerResult.DriftSummary,
                    compare = triggerResult.Compare
                }),
                MetadataJson = SerializeJson(new
                {
                    severity = driftFlags.severity,
                    collectionsRiskWorsened = driftFlags.collectionsRiskWorsened
                }),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            return 1;
        }

        private async Task UpdateLatestOutcomeFeeUpdateEventAsync(
            string planId,
            string currentVersionId,
            string triggerType,
            string? triggerEntityType,
            string? triggerEntityId,
            OutcomeFeePlanTriggerRequest request,
            OutcomeFeePlanTriggerResult triggerResult,
            decimal hoursThreshold,
            decimal collectionsThreshold,
            decimal marginThreshold,
            CancellationToken ct)
        {
            var evt = await _context.OutcomeFeeUpdateEvents
                .Where(e => e.PlanId == planId && e.AppliedVersionId == currentVersionId)
                .OrderByDescending(e => e.CreatedAt)
                .FirstOrDefaultAsync(ct);
            if (evt == null) return;

            evt.TriggerType = triggerType;
            evt.TriggerEntityType = triggerEntityType;
            evt.TriggerEntityId = triggerEntityId;
            evt.CorrelationId = string.IsNullOrWhiteSpace(request.CorrelationId) ? evt.CorrelationId : request.CorrelationId?.Trim();
            evt.MetadataJson = MergeJson(evt.MetadataJson, new
            {
                mode = "trigger_pipeline",
                recomputeStrategy = "full_recompute_fallback",
                driftThresholds = new { hours = hoursThreshold, collections = collectionsThreshold, marginCompression = marginThreshold },
                reviewQueued = triggerResult.ReviewItemsQueued,
                notificationsQueued = triggerResult.NotificationsQueued
            });
            evt.ResultJson = SerializeJson(new
            {
                versionId = currentVersionId,
                triggerAccepted = triggerResult.TriggerAccepted,
                recomputed = triggerResult.Recomputed,
                driftDetected = triggerResult.DriftDetected,
                drift = triggerResult.DriftSummary,
                compare = triggerResult.Compare
            });
            evt.AppliedAt ??= DateTime.UtcNow;
        }

        private static IReadOnlyList<string> ParseRiskFlags(string? metadataJson)
        {
            if (string.IsNullOrWhiteSpace(metadataJson)) return Array.Empty<string>();
            try
            {
                using var doc = JsonDocument.Parse(metadataJson);
                if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                    !doc.RootElement.TryGetProperty("riskFlags", out var flags) ||
                    flags.ValueKind != JsonValueKind.Array)
                {
                    return Array.Empty<string>();
                }

                return flags.EnumerateArray()
                    .Where(x => x.ValueKind == JsonValueKind.String)
                    .Select(x => x.GetString())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Cast<string>()
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        private static decimal GetDecimal(JsonElement root, string key)
        {
            if (!root.TryGetProperty(key, out var prop)) return 0m;
            try
            {
                if (prop.ValueKind == JsonValueKind.Number && prop.TryGetDecimal(out var value))
                {
                    return value;
                }
                if (prop.ValueKind == JsonValueKind.String &&
                    decimal.TryParse(prop.GetString(), out var parsed))
                {
                    return parsed;
                }
            }
            catch { }
            return 0m;
        }

        private static bool GetBool(JsonElement root, string key)
        {
            if (!root.TryGetProperty(key, out var prop)) return false;
            return prop.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String when bool.TryParse(prop.GetString(), out var parsed) => parsed,
                _ => false
            };
        }

        private static string? GetString(JsonElement root, string key)
        {
            return root.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.String
                ? prop.GetString()
                : null;
        }
    }
}
