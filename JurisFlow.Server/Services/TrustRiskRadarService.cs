using System.Globalization;
using System.Text.Json;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Task = System.Threading.Tasks.Task;

namespace JurisFlow.Server.Services
{
    public sealed class TrustRiskRadarService
    {
        private const string DefaultPolicyKey = "default";
        private const string ProviderKey = "trust-risk-radar";
        private const string ReviewItemType = "trust_risk_review";
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        private readonly JurisFlowDbContext _context;
        private readonly TenantContext _tenantContext;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILogger<TrustRiskRadarService> _logger;

        public TrustRiskRadarService(
            JurisFlowDbContext context,
            TenantContext tenantContext,
            IHttpContextAccessor httpContextAccessor,
            ILogger<TrustRiskRadarService> logger)
        {
            _context = context;
            _tenantContext = tenantContext;
            _httpContextAccessor = httpContextAccessor;
            _logger = logger;
        }

        public IReadOnlyList<TrustRiskPolicyTemplateDescriptor> GetPolicyTemplates()
        {
            return new[]
            {
                ToPolicyTemplateDescriptor(CreateBuiltInPolicyTemplate("conservative")),
                ToPolicyTemplateDescriptor(CreateBuiltInPolicyTemplate("balanced")),
                ToPolicyTemplateDescriptor(CreateBuiltInPolicyTemplate("aggressive"))
            };
        }

        public async Task<TrustRiskEvent?> RecordLedgerEntryRiskAsync(BillingLedgerEntry entry, string triggerType, CancellationToken ct = default)
        {
            if (entry == null)
            {
                return null;
            }

            return await TryRecordAsync(async () =>
            {
                var policy = await GetOrCreateActivePolicyAsync(ct);
                var evaluation = await EnrichLedgerEvaluationAsync(entry, triggerType, EvaluateLedgerEntry(entry), policy, ct);
                return await PersistAsync(
                    policy: policy,
                    sourceType: "billing_ledger_entry",
                    sourceId: entry.Id,
                    triggerType: string.IsNullOrWhiteSpace(triggerType) ? "ledger_entry_recorded" : triggerType,
                    occurredAt: entry.PostedAt,
                    billingLedgerEntryId: entry.Id,
                    billingPaymentAllocationId: null,
                    trustTransactionId: entry.TrustTransactionId,
                    paymentTransactionId: entry.PaymentTransactionId,
                    invoiceId: entry.InvoiceId,
                    matterId: entry.MatterId,
                    clientId: entry.ClientId,
                    payorClientId: entry.PayorClientId,
                    sourceCorrelationKey: entry.CorrelationKey,
                    evaluation: evaluation,
                    ct: ct);
            });
        }

        public async Task<TrustRiskEvent?> RecordPaymentAllocationRiskAsync(BillingPaymentAllocation allocation, string triggerType, CancellationToken ct = default)
        {
            if (allocation == null)
            {
                return null;
            }

            return await TryRecordAsync(async () =>
            {
                var policy = await GetOrCreateActivePolicyAsync(ct);
                var evaluation = await EnrichPaymentAllocationEvaluationAsync(allocation, triggerType, EvaluatePaymentAllocation(allocation), policy, ct);
                return await PersistAsync(
                    policy: policy,
                    sourceType: "billing_payment_allocation",
                    sourceId: allocation.Id,
                    triggerType: string.IsNullOrWhiteSpace(triggerType) ? "payment_allocation_recorded" : triggerType,
                    occurredAt: allocation.AppliedAt,
                    billingLedgerEntryId: allocation.LedgerEntryId,
                    billingPaymentAllocationId: allocation.Id,
                    trustTransactionId: null,
                    paymentTransactionId: allocation.PaymentTransactionId,
                    invoiceId: allocation.InvoiceId,
                    matterId: allocation.MatterId,
                    clientId: allocation.ClientId,
                    payorClientId: allocation.PayorClientId,
                    sourceCorrelationKey: BuildAllocationSourceCorrelationKey(allocation),
                    evaluation: evaluation,
                    ct: ct);
            });
        }

        public async Task<TrustRiskEvent?> RecordTrustTransactionRiskAsync(TrustTransaction transaction, string triggerType, CancellationToken ct = default)
        {
            if (transaction == null)
            {
                return null;
            }

            return await TryRecordAsync(async () =>
            {
                var policy = await GetOrCreateActivePolicyAsync(ct);
                var evaluation = await EnrichTrustTransactionEvaluationAsync(transaction, triggerType, EvaluateTrustTransaction(transaction), policy, ct);
                return await PersistAsync(
                    policy: policy,
                    sourceType: "trust_transaction",
                    sourceId: transaction.Id,
                    triggerType: string.IsNullOrWhiteSpace(triggerType) ? "trust_transaction_recorded" : triggerType,
                    occurredAt: transaction.UpdatedAt == default ? transaction.CreatedAt : transaction.UpdatedAt,
                    billingLedgerEntryId: transaction.LedgerId,
                    billingPaymentAllocationId: null,
                    trustTransactionId: transaction.Id,
                    paymentTransactionId: null,
                    invoiceId: null,
                    matterId: transaction.MatterId,
                    clientId: null,
                    payorClientId: null,
                    sourceCorrelationKey: transaction.Reference,
                    evaluation: evaluation,
                    ct: ct);
            });
        }

        public async Task<TrustRiskEvent?> RecordPeriodLockAttemptAsync(DateTime attemptedAtUtc, string? operationType = null, CancellationToken ct = default)
        {
            return await TryRecordAsync(async () =>
            {
                var policy = await GetOrCreateActivePolicyAsync(ct);
                var reasons = new List<object>();
                var evidence = new Dictionary<string, object?>
                {
                    ["attemptedAtUtc"] = attemptedAtUtc,
                    ["operationType"] = string.IsNullOrWhiteSpace(operationType) ? "billing_operation" : operationType,
                    ["evidenceRefs"] = Array.Empty<string>()
                };
                var features = new Dictionary<string, object?>
                {
                    ["isOffHours"] = IsOffHours(attemptedAtUtc),
                    ["isWeekend"] = IsWeekend(attemptedAtUtc)
                };

                var weights = ResolvePolicyWeights(policy);
                var score = 0m;
                score += Weight(weights, "periodLockAttempt", 65m);
                AddReason(reasons, "period_lock_attempt", "Billing operation attempted within a locked billing period.", Weight(weights, "periodLockAttempt", 65m));

                if (IsOffHours(attemptedAtUtc))
                {
                    var offHoursWeight = Weight(weights, "offHoursActivity", 12m);
                    score += offHoursWeight;
                    AddReason(reasons, "off_hours_activity", "Period-lock attempt occurred outside standard business hours.", offHoursWeight);
                }

                var evaluation = new TrustRiskEvaluation(
                    ClampScore(score),
                    MapSeverity(score),
                    reasons,
                    evidence,
                    features);

                return await PersistAsync(
                    policy: policy,
                    sourceType: "billing_operation_attempt",
                    sourceId: Guid.NewGuid().ToString("N"),
                    triggerType: "period_lock_attempt",
                    occurredAt: attemptedAtUtc,
                    billingLedgerEntryId: null,
                    billingPaymentAllocationId: null,
                    trustTransactionId: null,
                    paymentTransactionId: null,
                    invoiceId: null,
                    matterId: null,
                    clientId: null,
                    payorClientId: null,
                    sourceCorrelationKey: operationType,
                    evaluation: evaluation,
                    ct: ct);
            });
        }

        public Task<TrustRiskPolicy> GetActivePolicyAsync(CancellationToken ct = default) =>
            GetOrCreateActivePolicyAsync(ct);

        public async Task<TrustRiskBehavioralBaselineSummary> GetBehavioralBaselineSummaryAsync(int days = 60, int top = 8, CancellationToken ct = default)
        {
            var windowDays = Math.Clamp(days, 7, 365);
            var topN = Math.Clamp(top, 1, 25);
            var since = DateTime.UtcNow.AddDays(-windowDays);

            var ledgerRows = await _context.BillingLedgerEntries.AsNoTracking()
                .Where(e => e.PostedAt >= since)
                .Select(e => new { e.Id, e.Amount, e.PostedAt, e.EntryType, e.LedgerDomain, e.LedgerBucket, e.MatterId, e.ClientId })
                .ToListAsync(ct);

            var allocationRows = await _context.BillingPaymentAllocations.AsNoTracking()
                .Where(a => a.AppliedAt >= since)
                .Select(a => new { a.Id, a.Amount, a.AppliedAt, a.Status, a.AllocationType, a.MatterId, a.ClientId, a.PayorClientId })
                .ToListAsync(ct);

            var trustRows = await _context.TrustTransactions.AsNoTracking()
                .Where(t => t.CreatedAt >= since || t.UpdatedAt >= since)
                .Select(t => new { t.Id, t.TrustAccountId, t.MatterId, t.Type, t.Amount, t.IsVoided, t.CreatedAt, t.UpdatedAt })
                .ToListAsync(ct);

            var trustAccountBaselines = trustRows
                .Where(t => !string.IsNullOrWhiteSpace(t.TrustAccountId))
                .GroupBy(t => t.TrustAccountId)
                .Select(g =>
                {
                    var stats = BuildBehavioralBaselineStats(
                        g,
                        x => NormalizeMoney(Math.Abs((decimal)x.Amount)),
                        x => x.UpdatedAt == default ? x.CreatedAt : x.UpdatedAt,
                        x => IsTrustTransactionReversalLike(x.Type, x.IsVoided));
                    return new TrustRiskBehavioralBaselineBucket(
                        "trust_account",
                        g.Key ?? string.Empty,
                        stats.Count,
                        stats.AverageAbsoluteAmount,
                        stats.OffHoursRate,
                        stats.WeekendRate,
                        stats.ReversalRate,
                        stats.HourBuckets);
                })
                .OrderByDescending(x => x.SampleCount)
                .ThenByDescending(x => x.AverageAbsoluteAmount)
                .Take(topN)
                .ToList();

            var tenantSummary = new List<TrustRiskBehavioralBaselineBucket>
            {
                BuildSourceBaselineBucket(
                    "billing_ledger_entry",
                    "tenant",
                    ledgerRows,
                    x => NormalizeMoney(Math.Abs(x.Amount)),
                    x => x.PostedAt,
                    x => IsLedgerReversalLike(x.EntryType)),
                BuildSourceBaselineBucket(
                    "billing_payment_allocation",
                    "tenant",
                    allocationRows,
                    x => NormalizeMoney(Math.Abs(x.Amount)),
                    x => x.AppliedAt,
                    x => IsAllocationReversalLike(x.Status, x.AllocationType)),
                BuildSourceBaselineBucket(
                    "trust_transaction",
                    "tenant",
                    trustRows,
                    x => NormalizeMoney(Math.Abs((decimal)x.Amount)),
                    x => x.UpdatedAt == default ? x.CreatedAt : x.UpdatedAt,
                    x => IsTrustTransactionReversalLike(x.Type, x.IsVoided))
            };

            var byMatter = ledgerRows
                .Where(r => !string.IsNullOrWhiteSpace(r.MatterId))
                .GroupBy(r => r.MatterId)
                .Select(g =>
                {
                    var stats = BuildBehavioralBaselineStats(
                        g,
                        x => NormalizeMoney(Math.Abs(x.Amount)),
                        x => x.PostedAt,
                        x => IsLedgerReversalLike(x.EntryType));
                    return new TrustRiskBehavioralBaselineBucket("matter", g.Key ?? string.Empty, stats.Count, stats.AverageAbsoluteAmount, stats.OffHoursRate, stats.WeekendRate, stats.ReversalRate, stats.HourBuckets);
                })
                .OrderByDescending(x => x.SampleCount)
                .Take(topN)
                .ToList();

            return new TrustRiskBehavioralBaselineSummary(
                WindowDays: windowDays,
                GeneratedAtUtc: DateTime.UtcNow,
                TenantBaselines: tenantSummary,
                TrustAccountBaselines: trustAccountBaselines,
                MatterBaselines: byMatter,
                DataQuality: new Dictionary<string, string>
                {
                    ["hourPattern"] = "utc_hour_distribution",
                    ["amountRange"] = "average_absolute_amount_proxy",
                    ["reversalRatio"] = "source_type_specific_proxy"
                });
        }

        public async Task<TrustRiskBehavioralTuningSummary> GetBehavioralTuningSummaryAsync(int days = 45, CancellationToken ct = default)
        {
            var windowDays = Math.Clamp(days, 7, 365);
            var since = DateTime.UtcNow.AddDays(-windowDays);
            var policy = await GetOrCreateActivePolicyAsync(ct);

            var events = await _context.TrustRiskEvents.AsNoTracking()
                .Where(e => e.CreatedAt >= since)
                .Select(e => new { e.Id, e.RiskScore, e.Decision, e.Severity, e.FeaturesJson, e.MetadataJson, e.CreatedAt })
                .ToListAsync(ct);

            var scores = events.Select(e => e.RiskScore).OrderBy(v => v).ToList();
            var behavioralCandidates = new List<decimal>();
            var behavioralApplied = new List<decimal>();
            var shadowEvents = 0;
            var behavioralRuleCounters = new Dictionary<string, BehavioralRuleCounter>(StringComparer.OrdinalIgnoreCase);
            foreach (var ev in events)
            {
                if (string.IsNullOrWhiteSpace(ev.FeaturesJson)) continue;
                var metadata = TryParseJsonObject(ev.MetadataJson);
                metadata.TryGetValue("reviewDisposition", out var disposition);
                try
                {
                    using var doc = JsonDocument.Parse(ev.FeaturesJson);
                    if (!doc.RootElement.TryGetProperty("behavioral", out var behavioral) || behavioral.ValueKind != JsonValueKind.Object) continue;

                    if (behavioral.TryGetProperty("shadowMode", out var shadowProp) && shadowProp.ValueKind == JsonValueKind.True)
                    {
                        shadowEvents++;
                    }

                    if (behavioral.TryGetProperty("candidateContribution", out var candidateProp) &&
                        candidateProp.ValueKind == JsonValueKind.Number &&
                        candidateProp.TryGetDecimal(out var candidate))
                    {
                        behavioralCandidates.Add(NormalizeMoney(candidate));
                    }

                    if (behavioral.TryGetProperty("appliedContribution", out var appliedProp) &&
                        appliedProp.ValueKind == JsonValueKind.Number &&
                        appliedProp.TryGetDecimal(out var applied))
                    {
                        behavioralApplied.Add(NormalizeMoney(applied));
                    }

                    if (behavioral.TryGetProperty("components", out var components) && components.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var component in components.EnumerateArray())
                        {
                            if (component.ValueKind != JsonValueKind.Object) continue;
                            if (!component.TryGetProperty("code", out var codeProp) || codeProp.ValueKind != JsonValueKind.String) continue;

                            var code = codeProp.GetString()?.Trim();
                            if (string.IsNullOrWhiteSpace(code)) continue;

                            if (!behavioralRuleCounters.TryGetValue(code, out var counter))
                            {
                                counter = new BehavioralRuleCounter();
                                behavioralRuleCounters[code] = counter;
                            }

                            counter.Observations++;
                            if (behavioral.TryGetProperty("shadowMode", out var sm) && sm.ValueKind == JsonValueKind.True)
                            {
                                counter.ShadowObservations++;
                            }

                            if (component.TryGetProperty("candidateWeight", out var cw) &&
                                cw.ValueKind == JsonValueKind.Number &&
                                cw.TryGetDecimal(out var candidateWeight))
                            {
                                counter.CandidateWeightSum = NormalizeMoney(counter.CandidateWeightSum + NormalizeMoney(candidateWeight));
                            }
                            if (component.TryGetProperty("appliedWeight", out var aw) &&
                                aw.ValueKind == JsonValueKind.Number &&
                                aw.TryGetDecimal(out var appliedWeight))
                            {
                                counter.AppliedWeightSum = NormalizeMoney(counter.AppliedWeightSum + NormalizeMoney(appliedWeight));
                            }

                            var normalizedDisposition = (disposition ?? string.Empty).Trim().ToLowerInvariant();
                            if (normalizedDisposition == "true_positive") counter.TruePositive++;
                            else if (normalizedDisposition == "false_positive") counter.FalsePositive++;
                            else if (normalizedDisposition == "acceptable_exception") counter.AcceptableException++;
                        }
                    }
                }
                catch
                {
                    // ignore malformed historical features rows
                }
            }

            var ruleLevelSuggestions = behavioralRuleCounters
                .Select(kvp =>
                {
                    var row = kvp.Value;
                    var labeled = row.TruePositive + row.FalsePositive + row.AcceptableException;
                    var fpRate = labeled == 0 ? (double?)null : Math.Round((row.FalsePositive * 100d) / labeled, 2);
                    var precisionProxy = labeled == 0 ? (double?)null : Math.Round((row.TruePositive * 100d) / labeled, 2);
                    var avgCandidate = row.Observations == 0 ? 0m : NormalizeMoney(row.CandidateWeightSum / row.Observations);
                    var avgApplied = row.Observations == 0 ? 0m : NormalizeMoney(row.AppliedWeightSum / row.Observations);
                    var shadowRatePct = row.Observations == 0 ? 0d : Math.Round((row.ShadowObservations * 100d) / row.Observations, 2);

                    string suggestion;
                    if (labeled >= 5 && (fpRate ?? 0d) >= 60d)
                    {
                        suggestion = "decrease_behavioral_weight_or_raise_behavioral_threshold";
                    }
                    else if (row.TruePositive >= 3 && avgCandidate > 0m && avgApplied <= 0m)
                    {
                        suggestion = "consider_shadow_to_active_or_raise_contribution_cap";
                    }
                    else if (row.TruePositive >= 3 && row.FalsePositive == 0)
                    {
                        suggestion = "retain_or_slightly_increase_weight";
                    }
                    else
                    {
                        suggestion = "monitor";
                    }

                    var burdenScore = Math.Round(
                        row.Observations +
                        (row.FalsePositive * 1.0) +
                        (row.AcceptableException * 0.5),
                        2);

                    return new
                    {
                        ruleCode = kvp.Key,
                        observations = row.Observations,
                        outcomeLabeled = labeled,
                        truePositive = row.TruePositive,
                        falsePositive = row.FalsePositive,
                        acceptableException = row.AcceptableException,
                        falsePositiveRatePct = fpRate,
                        precisionProxyPct = precisionProxy,
                        shadowObservationRatePct = shadowRatePct,
                        avgCandidateWeight = avgCandidate,
                        avgAppliedWeight = avgApplied,
                        burdenScore,
                        suggestion
                    };
                })
                .OrderByDescending(x => x.burdenScore)
                .ThenByDescending(x => x.observations)
                .ThenBy(x => x.ruleCode)
                .Take(20)
                .Cast<object>()
                .ToList();

            var p60 = Percentile(scores, 0.60m);
            var p75 = Percentile(scores, 0.75m);
            var p90 = Percentile(scores, 0.90m);
            var p95 = Percentile(scores, 0.95m);

            var p60Value = p60 ?? policy.WarnThreshold;
            var p75Value = p75 ?? policy.ReviewThreshold;
            var p90Value = p90 ?? policy.SoftHoldThreshold;
            var p95Value = p95 ?? policy.HardHoldThreshold;

            var suggestedWarn = NormalizeMoney(Math.Clamp(Math.Max(policy.WarnThreshold, p60Value), 0m, 100m));
            var suggestedReview = NormalizeMoney(Math.Clamp(Math.Max(suggestedWarn, p75Value), 0m, 100m));
            var suggestedSoft = NormalizeMoney(Math.Clamp(Math.Max(suggestedReview, p90Value), 0m, 100m));
            var suggestedHard = NormalizeMoney(Math.Clamp(Math.Max(suggestedSoft, p95Value), 0m, 100m));

            return new TrustRiskBehavioralTuningSummary(
                WindowDays: windowDays,
                GeneratedAtUtc: DateTime.UtcNow,
                CurrentThresholds: new TrustRiskThresholdSnapshot(policy.WarnThreshold, policy.ReviewThreshold, policy.SoftHoldThreshold, policy.HardHoldThreshold),
                SuggestedThresholds: new TrustRiskThresholdSnapshot(suggestedWarn, suggestedReview, suggestedSoft, suggestedHard),
                BehavioralPolicy: new TrustRiskBehavioralPolicySnapshot(
                    ResolvePolicyFlag(policy, "behavioralSignalsEnabled", true),
                    ResolvePolicyFlag(policy, "behavioralShadowMode", true),
                    ResolvePolicyInt(policy, "behavioralLookbackDays", 45),
                    ResolvePolicyInt(policy, "behavioralMinSamples", 10),
                    ResolvePolicyDecimal(policy, "behavioralAmountRatioThreshold", 4m),
                    ResolvePolicyDecimal(policy, "behavioralContributionCap", 18m)),
                Distribution: new Dictionary<string, decimal?>
                {
                    ["p50"] = Percentile(scores, 0.50m),
                    ["p60"] = p60,
                    ["p75"] = p75,
                    ["p90"] = p90,
                    ["p95"] = p95
                },
                BehavioralSignalStats: new Dictionary<string, object?>
                {
                    ["shadowEventCount"] = shadowEvents,
                    ["candidateContributionAvg"] = behavioralCandidates.Count == 0 ? null : NormalizeMoney(behavioralCandidates.Average()),
                    ["appliedContributionAvg"] = behavioralApplied.Count == 0 ? null : NormalizeMoney(behavioralApplied.Average()),
                    ["candidateContributionP90"] = Percentile(behavioralCandidates.OrderBy(v => v).ToList(), 0.90m),
                    ["sampleEventCount"] = events.Count,
                    ["ruleLevelSuggestions"] = ruleLevelSuggestions
                },
                DataQuality: new Dictionary<string, string>
                {
                    ["thresholdSuggestion"] = "score_distribution_heuristic",
                    ["behavioralContribution"] = "features_json_derived",
                    ["ruleLevelSuggestions"] = "features_behavioral_components_plus_review_disposition_when_present"
                });
        }

        public async Task<TrustRiskPolicy> UpsertPolicyAsync(TrustRiskPolicyUpsertRequest request, string? userId, CancellationToken ct = default)
        {
            var current = await GetOrCreateActivePolicyAsync(ct);
            var now = DateTime.UtcNow;
            var template = ResolveRequestedTemplate(request.TemplateKey);

            var warn = request.WarnThreshold ?? template?.WarnThreshold ?? current.WarnThreshold;
            var review = request.ReviewThreshold ?? template?.ReviewThreshold ?? current.ReviewThreshold;
            var softHold = request.SoftHoldThreshold ?? template?.SoftHoldThreshold ?? current.SoftHoldThreshold;
            var hardHold = request.HardHoldThreshold ?? template?.HardHoldThreshold ?? current.HardHoldThreshold;
            ValidateThresholds(warn, review, softHold, hardHold);

            var failMode = NormalizeFailMode(request.FailMode ?? current.FailMode);
            var enabledRules = request.EnabledRules?.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                               ?? ResolveEnabledRuleKeys(current).ToArray();
            var weights = request.RuleWeights != null
                ? request.RuleWeights
                    .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Key))
                    .ToDictionary(kvp => kvp.Key.Trim(), kvp => NormalizeMoney(Math.Max(0m, kvp.Value)), StringComparer.OrdinalIgnoreCase)
                : ResolvePolicyWeights(current).ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);
            var actionMap = request.ActionMap != null
                ? NormalizeActionMap(request.ActionMap)
                : ResolvePolicyActionMap(current);

            var criticalThresholdChange = DetectCriticalThresholdChange(current, warn, review, softHold, hardHold);
            if (criticalThresholdChange.IsCritical &&
                ResolveCriticalThresholdChangeReviewRequired(current, request.RequireCriticalThresholdChangeReview))
            {
                ValidateActionReasonQuality("critical threshold change", request.CriticalThresholdChangeReason, required: true);
            }

            var metadata = BuildPolicyMetadataForPhase2(
                current,
                template,
                request.OverrideRoles,
                request.ReleaseRoles,
                request.CriticalDualApprovalSecondaryRoles,
                request.OpsAlertsEnabled,
                request.OpsAlertChannels,
                request.PolicyTemplate,
                request.TrustAccountOverrides,
                request.CriticalDualApprovalEnabled,
                request.HoldEscalationSlaMinutes,
                request.SoftHoldExpiryHours,
                request.HardHoldExpiryHours,
                request.RequireCriticalThresholdChangeReview,
                request.CriticalThresholdChangeReason,
                criticalThresholdChange,
                request.AdditionalMetadata);

            current.IsActive = false;
            current.Status = "retired";
            current.UpdatedAt = now;
            current.UpdatedBy = userId ?? "system";

            var next = new TrustRiskPolicy
            {
                PolicyKey = string.IsNullOrWhiteSpace(request.PolicyKey) ? current.PolicyKey : request.PolicyKey.Trim(),
                VersionNumber = current.VersionNumber + 1,
                IsActive = true,
                Status = "active",
                Name = Truncate(request.Name ?? current.Name, 255),
                Description = Truncate(request.Description ?? current.Description, 2048),
                WarnThreshold = warn,
                ReviewThreshold = review,
                SoftHoldThreshold = softHold,
                HardHoldThreshold = hardHold,
                FailMode = failMode,
                EnabledRulesJson = Serialize(enabledRules),
                RuleWeightsJson = Serialize(weights),
                ActionMapJson = Serialize(actionMap),
                MetadataJson = Serialize(metadata),
                CreatedBy = userId ?? "system",
                UpdatedBy = userId ?? "system",
                PublishedAt = now,
                CreatedAt = now,
                UpdatedAt = now
            };

            _context.TrustRiskPolicies.Add(next);

            if (criticalThresholdChange.IsCritical &&
                ResolveCriticalThresholdChangeReviewRequired(current, request.RequireCriticalThresholdChangeReview))
            {
                var reviewItem = BuildPolicyThresholdChangeReviewItem(current, next, criticalThresholdChange, request.CriticalThresholdChangeReason, userId);
                _context.IntegrationReviewQueueItems.Add(reviewItem);
                next.MetadataJson = MergeJsonMetadata(next.MetadataJson, new Dictionary<string, object?>
                {
                    ["criticalThresholdChangeReviewRequired"] = true,
                    ["criticalThresholdChangeReviewQueueItemId"] = reviewItem.Id,
                    ["criticalThresholdChangeReason"] = Truncate(request.CriticalThresholdChangeReason, 2048),
                    ["criticalThresholdChangeSummary"] = criticalThresholdChange.Summary,
                    ["criticalThresholdChangeDetectedAtUtc"] = now
                });
            }

            await _context.SaveChangesAsync(ct);
            return next;
        }

        public async Task<TrustRiskHold?> MarkHoldUnderReviewAsync(string holdId, string? userId, string? notes, CancellationToken ct = default)
        {
            await ApplyHoldSlaTransitionsAsync(ct);
            var hold = await _context.TrustRiskHolds.FirstOrDefaultAsync(h => h.Id == holdId, ct);
            if (hold == null) return null;
            if (string.Equals(hold.Status, "released", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Released hold cannot be moved to under_review.");
            }

            hold.Status = "under_review";
            hold.UpdatedAt = DateTime.UtcNow;
            hold.MetadataJson = MergeJsonMetadata(hold.MetadataJson, new Dictionary<string, object?>
            {
                ["lastAction"] = "under_review",
                ["notes"] = Truncate(notes, 2048),
                ["updatedBy"] = userId
            });

            await SetReviewQueueStatusForEventAsync(hold.TrustRiskEventId, IntegrationReviewQueueStatuses.InReview, ct);
            await AppendHoldActionAsync(hold, "under_review", "completed", userId, "user", notes ?? "Hold moved to under review.", ct);
            await TryQueueOpsAlertOutboxEventAsync(
                policy: await GetOrCreateActivePolicyAsync(ct),
                eventType: "trust_risk.hold.under_review",
                riskEventId: hold.TrustRiskEventId,
                decision: null,
                holdId: hold.Id,
                reviewQueueItemId: await TryGetReviewQueueItemIdForEventAsync(hold.TrustRiskEventId, ct),
                payload: new { holdId = hold.Id, holdType = hold.HoldType, hold.Status, hold.TargetType, hold.TargetId, notes },
                ct: ct);

            await _context.SaveChangesAsync(ct);
            return hold;
        }

        public async Task<TrustRiskHold?> EscalateHoldAsync(string holdId, string? userId, string? userRole, string? reason, string? approverReason, CancellationToken ct = default)
        {
            ValidateActionReasonQuality("escalation", reason, required: true);
            ValidateActionReasonQuality("approver", approverReason, required: true);
            await ApplyHoldSlaTransitionsAsync(ct);

            var hold = await _context.TrustRiskHolds.FirstOrDefaultAsync(h => h.Id == holdId, ct);
            if (hold == null) return null;
            if (string.Equals(hold.Status, "released", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Released hold cannot be escalated.");
            }

            var policy = await GetOrCreateActivePolicyAsync(ct);
            var riskEvent = await _context.TrustRiskEvents.AsNoTracking().FirstOrDefaultAsync(e => e.Id == hold.TrustRiskEventId, ct);
            var trustAccountId = ResolveTrustAccountIdFromHoldOrEvent(hold, riskEvent);
            EnsureOverrideRoleAuthorized(policy, userRole, "overrideRoles", trustAccountId);

            hold.Status = "escalated";
            hold.UpdatedAt = DateTime.UtcNow;
            hold.MetadataJson = MergeJsonMetadata(hold.MetadataJson, new Dictionary<string, object?>
            {
                ["lastAction"] = "escalated",
                ["escalatedBy"] = userId,
                ["escalationReason"] = Truncate(reason, 2048),
                ["approverReason"] = Truncate(approverReason, 2048)
            });

            await SetReviewQueueStatusForEventAsync(hold.TrustRiskEventId, IntegrationReviewQueueStatuses.InReview, ct);
            await AppendHoldActionAsync(hold, "escalate", "completed", userId, "user", $"{reason} | approver: {approverReason}", ct);
            await TryQueueOpsAlertOutboxEventAsync(
                policy,
                "trust_risk.hold.escalated",
                hold.TrustRiskEventId,
                null,
                hold.Id,
                await TryGetReviewQueueItemIdForEventAsync(hold.TrustRiskEventId, ct),
                new { holdId = hold.Id, hold.TargetType, hold.TargetId, reason, approverReason },
                ct);

            await _context.SaveChangesAsync(ct);
            return hold;
        }

        public async Task<TrustRiskHold?> ReleaseHoldAsync(string holdId, string? userId, string? userRole, string reason, string? approverReason, CancellationToken ct = default)
        {
            ValidateActionReasonQuality("release", reason, required: true);
            ValidateActionReasonQuality("approver", approverReason, required: true);
            await ApplyHoldSlaTransitionsAsync(ct);

            var hold = await _context.TrustRiskHolds.FirstOrDefaultAsync(h => h.Id == holdId, ct);
            if (hold == null) return null;
            if (string.Equals(hold.Status, "released", StringComparison.OrdinalIgnoreCase))
            {
                return hold;
            }

            var policy = await GetOrCreateActivePolicyAsync(ct);
            var riskEvent = await _context.TrustRiskEvents.FirstOrDefaultAsync(e => e.Id == hold.TrustRiskEventId, ct);
            var trustAccountId = ResolveTrustAccountIdFromHoldOrEvent(hold, riskEvent);
            EnsureOverrideRoleAuthorized(policy, userRole, "releaseRoles", trustAccountId);

            if (RequiresCriticalDualApproval(policy, hold, riskEvent, trustAccountId))
            {
                var pending = GetPendingDualApprovalState(hold);
                var secondaryRoles = ResolvePolicyRoleSet(policy, "criticalDualApprovalSecondaryRoles", trustAccountId, "SecurityAdmin", "Admin");

                if (pending == null || !pending.Pending)
                {
                    hold.Status = "under_review";
                    hold.UpdatedAt = DateTime.UtcNow;
                    hold.MetadataJson = MergeJsonMetadata(hold.MetadataJson, new Dictionary<string, object?>
                    {
                        ["dualApprovalPending"] = true,
                        ["dualApprovalRequired"] = true,
                        ["dualApprovalPrimaryUserId"] = Truncate(userId, 128),
                        ["dualApprovalPrimaryRole"] = Truncate(userRole, 64),
                        ["dualApprovalPrimaryReason"] = Truncate(reason, 2048),
                        ["dualApprovalPrimaryApproverReason"] = Truncate(approverReason, 2048),
                        ["dualApprovalRequestedAtUtc"] = DateTime.UtcNow,
                        ["dualApprovalSecondaryRoles"] = secondaryRoles.ToArray(),
                        ["trustAccountId"] = trustAccountId
                    });

                    await SetReviewQueueStatusForEventAsync(hold.TrustRiskEventId, IntegrationReviewQueueStatuses.InReview, ct);
                    await AppendHoldActionAsync(hold, "release_pending_dual_approval", "completed", userId, "user", $"{reason} | approver: {approverReason}", ct);
                    await TryQueueOpsAlertOutboxEventAsync(
                        policy,
                        "trust_risk.hold.release_pending_dual_approval",
                        hold.TrustRiskEventId,
                        null,
                        hold.Id,
                        await TryGetReviewQueueItemIdForEventAsync(hold.TrustRiskEventId, ct),
                        new { holdId = hold.Id, hold.TargetType, hold.TargetId, reason, approverReason, requiredSecondaryRoles = secondaryRoles.ToArray() },
                        ct);

                    await _context.SaveChangesAsync(ct);
                    return hold;
                }

                if (!string.IsNullOrWhiteSpace(pending.PrimaryUserId) &&
                    !string.IsNullOrWhiteSpace(userId) &&
                    string.Equals(pending.PrimaryUserId, userId, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Critical hold dual approval requires a second approver.");
                }

                if (string.IsNullOrWhiteSpace(userRole) || !secondaryRoles.Contains(userRole))
                {
                    throw new UnauthorizedAccessException($"Role '{userRole ?? "unknown"}' is not authorized as secondary approver for critical hold release.");
                }
            }

            hold.Status = "released";
            hold.ReleasedBy = userId ?? "system";
            hold.ReleaseReason = Truncate(reason, 2048);
            hold.ReleasedAt = DateTime.UtcNow;
            hold.UpdatedAt = DateTime.UtcNow;
            hold.MetadataJson = MergeJsonMetadata(hold.MetadataJson, new Dictionary<string, object?>
            {
                ["approverReason"] = Truncate(approverReason, 2048),
                ["releasedBy"] = userId ?? "system",
                ["dualApprovalPending"] = false,
                ["dualApprovalReleasedBy"] = userId ?? "system",
                ["trustAccountId"] = trustAccountId
            });

            if (riskEvent != null)
            {
                var anyOpenHolds = await _context.TrustRiskHolds.AnyAsync(h =>
                    h.TrustRiskEventId == riskEvent.Id &&
                    h.Id != hold.Id &&
                    (h.Status == "placed" || h.Status == "under_review" || h.Status == "escalated"), ct);
                if (!anyOpenHolds)
                {
                    riskEvent.Status = "closed";
                    riskEvent.UpdatedAt = DateTime.UtcNow;
                }
            }

            await SetReviewQueueStatusForEventAsync(hold.TrustRiskEventId, IntegrationReviewQueueStatuses.Resolved, ct);
            await AppendHoldActionAsync(hold, "release", "completed", userId, "user", $"{reason} | approver: {approverReason}", ct);
            await TryQueueOpsAlertOutboxEventAsync(
                policy,
                "trust_risk.hold.released",
                hold.TrustRiskEventId,
                null,
                hold.Id,
                await TryGetReviewQueueItemIdForEventAsync(hold.TrustRiskEventId, ct),
                new { holdId = hold.Id, hold.TargetType, hold.TargetId, releasedBy = userId, reason, approverReason },
                ct);

            await _context.SaveChangesAsync(ct);
            return hold;
        }

        public async Task<TrustRiskEvent?> AcknowledgeEventAsync(string eventId, string? userId, string? note, CancellationToken ct = default)
        {
            var riskEvent = await _context.TrustRiskEvents.FirstOrDefaultAsync(e => e.Id == eventId, ct);
            if (riskEvent == null) return null;

            riskEvent.MetadataJson = MergeJsonMetadata(riskEvent.MetadataJson, new Dictionary<string, object?>
            {
                ["acknowledged"] = true,
                ["acknowledgedBy"] = userId ?? "system",
                ["acknowledgedAtUtc"] = DateTime.UtcNow,
                ["ackNote"] = Truncate(note, 1024)
            });
            riskEvent.UpdatedAt = DateTime.UtcNow;

            _context.TrustRiskActions.Add(new TrustRiskAction
            {
                TrustRiskEventId = riskEvent.Id,
                ActionType = "acknowledge",
                Status = "completed",
                ActorUserId = Truncate(userId, 128),
                ActorType = "user",
                CorrelationId = Truncate(GetHttpContextItem(AuditTraceContextKeys.CorrelationId), 128),
                Notes = Truncate(note, 1024) ?? "Trust risk event acknowledged.",
                MetadataJson = Serialize(new { phase = "trust-risk-radar.phaseX1" }),
                CreatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync(ct);
            return riskEvent;
        }

        public async Task<TrustRiskEvent?> AssignEventAsync(string eventId, string assigneeUserId, string? assignedByUserId, string? note, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(assigneeUserId))
            {
                throw new InvalidOperationException("Assignee is required.");
            }

            var riskEvent = await _context.TrustRiskEvents.FirstOrDefaultAsync(e => e.Id == eventId, ct);
            if (riskEvent == null) return null;

            riskEvent.MetadataJson = MergeJsonMetadata(riskEvent.MetadataJson, new Dictionary<string, object?>
            {
                ["assignedToUserId"] = Truncate(assigneeUserId, 128),
                ["assignedByUserId"] = Truncate(assignedByUserId, 128),
                ["assignedAtUtc"] = DateTime.UtcNow,
                ["assignmentNote"] = Truncate(note, 1024)
            });
            riskEvent.UpdatedAt = DateTime.UtcNow;

            _context.TrustRiskActions.Add(new TrustRiskAction
            {
                TrustRiskEventId = riskEvent.Id,
                ActionType = "assign",
                Status = "completed",
                ActorUserId = Truncate(assignedByUserId, 128),
                ActorType = "user",
                CorrelationId = Truncate(GetHttpContextItem(AuditTraceContextKeys.CorrelationId), 128),
                Notes = Truncate($"Assigned to {assigneeUserId}. {note}", 2048),
                MetadataJson = Serialize(new { phase = "trust-risk-radar.phaseX1", assigneeUserId = Truncate(assigneeUserId, 128) }),
                CreatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync(ct);
            return riskEvent;
        }

        public async Task<TrustRiskEvent?> SetReviewDispositionAsync(
            string eventId,
            string disposition,
            string reason,
            string? approverReason,
            string? userId,
            string? userRole,
            CancellationToken ct = default)
        {
            ValidateReviewDisposition(disposition);
            ValidateActionReasonQuality("review disposition", reason, required: true);
            ValidateActionReasonQuality("approver", approverReason, required: true);

            var riskEvent = await _context.TrustRiskEvents.FirstOrDefaultAsync(e => e.Id == eventId, ct);
            if (riskEvent == null) return null;

            var policy = await GetOrCreateActivePolicyAsync(ct);
            EnsureOverrideRoleAuthorized(policy, userRole, "releaseRoles");

            var normalizedDisposition = disposition.Trim().ToLowerInvariant();
            var reviewQueueStatus = normalizedDisposition switch
            {
                "false_positive" => IntegrationReviewQueueStatuses.Rejected,
                "true_positive" => IntegrationReviewQueueStatuses.Resolved,
                "acceptable_exception" => IntegrationReviewQueueStatuses.Resolved,
                _ => IntegrationReviewQueueStatuses.InReview
            };

            riskEvent.MetadataJson = MergeJsonMetadata(riskEvent.MetadataJson, new Dictionary<string, object?>
            {
                ["reviewDisposition"] = normalizedDisposition,
                ["reviewDispositionReason"] = Truncate(reason, 2048),
                ["reviewDispositionApproverReason"] = Truncate(approverReason, 2048),
                ["reviewDispositionBy"] = Truncate(userId, 128),
                ["reviewDispositionAtUtc"] = DateTime.UtcNow
            });
            riskEvent.Status = normalizedDisposition == "false_positive" ? "closed" : riskEvent.Status;
            riskEvent.UpdatedAt = DateTime.UtcNow;

            if (!string.IsNullOrWhiteSpace(riskEvent.ReviewQueueItemId))
            {
                await SetReviewQueueStatusForEventAsync(riskEvent.Id, reviewQueueStatus, ct);
            }

            var links = await _context.TrustRiskReviewLinks
                .Where(l => l.TrustRiskEventId == riskEvent.Id)
                .ToListAsync(ct);
            foreach (var link in links)
            {
                link.Status = "resolved";
                link.UpdatedAt = DateTime.UtcNow;
                link.MetadataJson = MergeJsonMetadata(link.MetadataJson, new Dictionary<string, object?>
                {
                    ["reviewDisposition"] = normalizedDisposition,
                    ["reviewDispositionReason"] = Truncate(reason, 2048),
                    ["reviewDispositionApproverReason"] = Truncate(approverReason, 2048),
                    ["reviewDispositionBy"] = Truncate(userId, 128),
                    ["reviewDispositionAtUtc"] = DateTime.UtcNow
                });
            }

            _context.TrustRiskActions.Add(new TrustRiskAction
            {
                TrustRiskEventId = riskEvent.Id,
                ActionType = "review_disposition_set",
                Status = "completed",
                ActorUserId = Truncate(userId, 128),
                ActorType = "user",
                CorrelationId = Truncate(GetHttpContextItem(AuditTraceContextKeys.CorrelationId), 128),
                Notes = Truncate($"{normalizedDisposition}: {reason} | approver: {approverReason}", 2048),
                MetadataJson = Serialize(new
                {
                    phase = "trust-risk-radar.phaseX1",
                    disposition = normalizedDisposition,
                    reviewQueueStatus
                }),
                CreatedAt = DateTime.UtcNow
            });

            await TryQueueOpsAlertOutboxEventAsync(
                policy,
                "trust_risk.review.disposition",
                riskEvent.Id,
                riskEvent.Decision,
                null,
                riskEvent.ReviewQueueItemId,
                new
                {
                    riskEventId = riskEvent.Id,
                    disposition = normalizedDisposition,
                    reason,
                    approverReason,
                    reviewedBy = userId
                },
                ct);

            await _context.SaveChangesAsync(ct);
            return riskEvent;
        }

        public async Task EnforceNoActiveHardHoldsAsync(TrustRiskHoldGuardContext context, CancellationToken ct = default)
        {
            if (context == null)
            {
                return;
            }

            TrustRiskPolicy? policy = null;
            var operationType = (context.OperationType ?? string.Empty).Trim();
            var failMode = "fail_open";
            try
            {
                policy = await GetOrCreateActivePolicyAsync(ct);
                failMode = ResolveOperationFailMode(policy, operationType);

                await ApplyHoldSlaTransitionsAsync(ct);

                var candidates = BuildHoldTargetCandidates(context)
                    .Where(c => !string.IsNullOrWhiteSpace(c.TargetId))
                    .Distinct()
                    .ToList();
                if (candidates.Count == 0)
                {
                    return;
                }

                var targetTypes = candidates.Select(c => c.TargetType).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var activeStatuses = new[] { "placed", "under_review", "escalated" };

                var holds = await _context.TrustRiskHolds.AsNoTracking()
                    .Where(h =>
                        activeStatuses.Contains(h.Status) &&
                        targetTypes.Contains(h.TargetType))
                    .Select(h => new
                    {
                        h.Id,
                        h.TargetType,
                        h.TargetId,
                        h.HoldType,
                        h.Status,
                        h.TrustRiskEventId,
                        h.Reason,
                        h.PlacedAt
                    })
                    .ToListAsync(ct);

                var matchingHardHold = holds.FirstOrDefault(h =>
                    string.Equals(h.HoldType, "hard", StringComparison.OrdinalIgnoreCase) &&
                    candidates.Any(c =>
                        string.Equals(c.TargetType, h.TargetType, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(c.TargetId, h.TargetId, StringComparison.Ordinal)));

                if (matchingHardHold != null)
                {
                    throw new TrustRiskPreflightBlockException(
                        $"Operation blocked by active trust risk hard hold. HoldId={matchingHardHold.Id}, EventId={matchingHardHold.TrustRiskEventId}, Target={matchingHardHold.TargetType}:{matchingHardHold.TargetId}, Status={matchingHardHold.Status}.");
                }

                var strictOptions = ResolvePreflightOptions(policy);
                if (!strictOptions.Enabled)
                {
                    return;
                }

                var matchedSoftHold = holds.FirstOrDefault(h =>
                    string.Equals(h.HoldType, "soft", StringComparison.OrdinalIgnoreCase) &&
                    candidates.Any(c =>
                        string.Equals(c.TargetType, h.TargetType, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(c.TargetId, h.TargetId, StringComparison.Ordinal)));

                var recentSince = DateTime.UtcNow.AddMinutes(-strictOptions.RecentEventWindowMinutes);
                var recentEvents = await _context.TrustRiskEvents.AsNoTracking()
                    .Where(e => e.CreatedAt >= recentSince &&
                                (e.Status == "review_queued" || e.Status == "hold_placed" || e.Status == "under_review") &&
                                (e.Decision == "review_required" || e.Decision == "soft_hold" || e.Decision == "hard_hold"))
                    .Select(e => new
                    {
                        e.Id,
                        e.Decision,
                        e.Severity,
                        e.SourceType,
                        e.SourceId,
                        e.BillingLedgerEntryId,
                        e.BillingPaymentAllocationId,
                        e.TrustTransactionId,
                        e.PaymentTransactionId,
                        e.InvoiceId,
                        e.MatterId,
                        e.ClientId,
                        e.PayorClientId,
                        e.RiskReasonsJson,
                        e.FeaturesJson,
                        e.CreatedAt
                    })
                    .ToListAsync(ct);

                var strictCandidate = recentEvents
                    .Where(e => EventMatchesGuardCandidates(e, candidates))
                    .Where(e => SeverityAtLeast(e.Severity, strictOptions.MinSeverity))
                    .Where(e => !strictOptions.HighConfidenceOnly || IsHighConfidenceEvent(e.RiskReasonsJson, e.FeaturesJson))
                    .OrderByDescending(e => DecisionRank(e.Decision))
                    .ThenByDescending(e => e.CreatedAt)
                    .FirstOrDefault();

                if (strictCandidate == null && matchedSoftHold == null)
                {
                    return;
                }

                var rollout = strictOptions.RolloutMode;
                var summary = strictCandidate != null
                    ? $"candidateEvent={strictCandidate.Id}, decision={strictCandidate.Decision}, severity={strictCandidate.Severity}"
                    : $"candidateSoftHold={matchedSoftHold?.Id}";

                if (string.Equals(rollout, "strict", StringComparison.OrdinalIgnoreCase))
                {
                    throw new TrustRiskPreflightBlockException(
                        $"Trust risk strict preflight blocked operation '{operationType}'. {summary}");
                }

                _logger.LogWarning(
                    "Trust risk preflight grace mode triggered for operation {OperationType}. Rollout={RolloutMode}. {Summary}",
                    operationType,
                    rollout,
                    summary);
            }
            catch (TrustRiskPreflightBlockException ex)
            {
                throw new InvalidOperationException(ex.Message);
            }
            catch (Exception ex)
            {
                if (string.Equals(failMode, "fail_closed", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Trust risk preflight unavailable and policy requires fail-closed for operation '{operationType}'.");
                }

                _logger.LogWarning(ex, "Trust risk preflight failed for operation {OperationType}; fail-open applied.", operationType);
            }
        }

        private async Task<TrustRiskEvent?> TryRecordAsync(Func<Task<TrustRiskEvent>> action)
        {
            if (!_tenantContext.IsResolved || string.IsNullOrWhiteSpace(_tenantContext.TenantId))
            {
                _logger.LogDebug("Trust risk evaluation skipped because tenant context is not resolved.");
                return null;
            }

            try
            {
                return await action();
            }
            catch (Exception ex)
            {
                // Phase 0 requirement: do not block billing/trust operations on radar failures.
                _logger.LogWarning(ex, "Trust risk radar record-only evaluation failed.");
                return null;
            }
        }

        private async Task<TrustRiskEvent> PersistAsync(
            TrustRiskPolicy policy,
            string sourceType,
            string sourceId,
            string triggerType,
            DateTime occurredAt,
            string? billingLedgerEntryId,
            string? billingPaymentAllocationId,
            string? trustTransactionId,
            string? paymentTransactionId,
            string? invoiceId,
            string? matterId,
            string? clientId,
            string? payorClientId,
            string? sourceCorrelationKey,
            TrustRiskEvaluation evaluation,
            CancellationToken ct)
        {
            var correlationId = GetHttpContextItem(AuditTraceContextKeys.CorrelationId);
            var traceId = _httpContextAccessor.HttpContext?.TraceIdentifier;
            var filteredEvaluation = ApplyEnabledRuleFilter(policy, evaluation);
            var score = ClampScore(filteredEvaluation.Score);
            var trustAccountId = TryResolveTrustAccountId(filteredEvaluation.Evidence, filteredEvaluation.Features);
            var effectiveThresholds = ResolveEffectiveThresholds(policy, trustAccountId);
            var thresholdDecision = ResolveThresholdDecision(score, effectiveThresholds);
            var decision = ResolvePolicyDecision(thresholdDecision, policy, trustAccountId);
            var preflightOptions = ResolvePreflightOptions(policy, trustAccountId);
            var graceAdjustedDecision = ApplyPreflightGraceDecision(decision, preflightOptions);
            if (!string.Equals(graceAdjustedDecision, decision, StringComparison.Ordinal))
            {
                decision = graceAdjustedDecision;
            }
            var holdEnforcementEnabled = ResolvePolicyFlag(policy, "holdEnforcementEnabled", true);
            var status = ResolveRiskEventStatus(decision);
            var reviewRequired = RequiresReviewQueue(decision);
            var holdDecision = holdEnforcementEnabled && (decision is "soft_hold" or "hard_hold");

            var riskEvent = new TrustRiskEvent
            {
                PolicyId = policy.Id,
                PolicyKey = policy.PolicyKey,
                PolicyVersionNumber = policy.VersionNumber,
                SourceType = sourceType,
                SourceId = sourceId,
                TriggerType = triggerType,
                TrustTransactionId = NullIfEmpty(trustTransactionId),
                BillingLedgerEntryId = NullIfEmpty(billingLedgerEntryId),
                BillingPaymentAllocationId = NullIfEmpty(billingPaymentAllocationId),
                PaymentTransactionId = NullIfEmpty(paymentTransactionId),
                InvoiceId = NullIfEmpty(invoiceId),
                MatterId = NullIfEmpty(matterId),
                ClientId = NullIfEmpty(clientId),
                PayorClientId = NullIfEmpty(payorClientId),
                SourceCorrelationKey = Truncate(NullIfEmpty(sourceCorrelationKey), 128),
                CorrelationId = Truncate(correlationId, 128),
                TraceId = Truncate(traceId, 128),
                EvaluationMode = holdEnforcementEnabled ? "policy_actions_phase2" : "warn_review_only",
                RiskScore = score,
                Severity = MapSeverity(score),
                Decision = decision,
                Status = status,
                RiskReasonsJson = Serialize(filteredEvaluation.Reasons),
                EvidenceJson = Serialize(filteredEvaluation.Evidence),
                FeaturesJson = Serialize(filteredEvaluation.Features),
                MetadataJson = Serialize(new
                {
                    phase = "trust-risk-radar.phase2",
                    recordOnly = false,
                    holdEnforcementEnabled,
                    thresholdDecision,
                    effectiveDecision = decision,
                    preflightStrictRolloutMode = preflightOptions.RolloutMode,
                    preflightGraceAdjusted = !string.Equals(graceAdjustedDecision, thresholdDecision, StringComparison.OrdinalIgnoreCase) &&
                                            !string.Equals(graceAdjustedDecision, ResolvePolicyDecision(thresholdDecision, policy, trustAccountId), StringComparison.Ordinal),
                    warnThreshold = effectiveThresholds.Warn,
                    reviewThreshold = effectiveThresholds.Review,
                    softHoldThreshold = effectiveThresholds.SoftHold,
                    hardHoldThreshold = effectiveThresholds.Hard,
                    trustAccountId,
                    trustAccountOverrideApplied = !string.IsNullOrWhiteSpace(trustAccountId) && HasTrustAccountOverride(policy, trustAccountId),
                    tenantId = _tenantContext.TenantId
                }),
                EvaluatedBy = "system",
                OccurredAt = occurredAt == default ? DateTime.UtcNow : occurredAt,
                EvaluatedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.TrustRiskEvents.Add(riskEvent);

            IntegrationReviewQueueItem? reviewItem = null;
            if (reviewRequired)
            {
                reviewItem = BuildReviewQueueItem(riskEvent, filteredEvaluation, thresholdDecision);
                _context.IntegrationReviewQueueItems.Add(reviewItem);
                _context.TrustRiskReviewLinks.Add(new TrustRiskReviewLink
                {
                    TrustRiskEventId = riskEvent.Id,
                    ReviewQueueItemId = reviewItem.Id,
                    ReviewQueueType = "integration_review",
                    LinkReasonCode = thresholdDecision is "soft_hold" or "hard_hold" ? "hold_candidate_phase1" : "risk_threshold_review",
                    Status = "active",
                    CorrelationId = Truncate(correlationId, 128),
                    MetadataJson = Serialize(new
                    {
                        phase = "trust-risk-radar.phase2",
                        thresholdDecision,
                        enforcedHold = holdDecision
                    }),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
                riskEvent.ReviewQueueItemId = reviewItem.Id;
            }

            TrustRiskHold? hold = null;
            if (holdDecision)
            {
                var (targetType, targetId) = ResolveHoldTarget(
                    sourceType,
                    sourceId,
                    billingLedgerEntryId,
                    billingPaymentAllocationId,
                    trustTransactionId,
                    paymentTransactionId,
                    invoiceId,
                    matterId,
                    clientId,
                    payorClientId);

                var holdType = string.Equals(decision, "hard_hold", StringComparison.Ordinal) ? "hard" : "soft";
                var duplicateSuppressionEnabled = ResolvePolicyFlag(policy, "preflightDuplicateSuppressionEnabled", true, trustAccountId);
                if (duplicateSuppressionEnabled)
                {
                    hold = await _context.TrustRiskHolds
                        .Where(h => h.TargetType == targetType &&
                                    h.TargetId == targetId &&
                                    (h.Status == "placed" || h.Status == "under_review" || h.Status == "escalated"))
                        .OrderByDescending(h => h.HoldType == "hard")
                        .ThenByDescending(h => h.PlacedAt)
                        .FirstOrDefaultAsync(ct);
                    if (hold != null)
                    {
                        riskEvent.MetadataJson = MergeJsonMetadata(riskEvent.MetadataJson, new Dictionary<string, object?>
                        {
                            ["duplicateHoldSuppressed"] = true,
                            ["duplicateHoldId"] = hold.Id,
                            ["duplicateHoldType"] = hold.HoldType
                        });
                    }
                }

                if (hold == null)
                {
                    hold = new TrustRiskHold
                    {
                        TrustRiskEventId = riskEvent.Id,
                        TargetType = targetType,
                        TargetId = targetId,
                        HoldType = holdType,
                        Status = "placed",
                        CorrelationId = Truncate(correlationId, 128),
                        Reason = Truncate(BuildActionReason(filteredEvaluation, decision), 2048),
                        PlacedBy = "system",
                        PlacedAt = DateTime.UtcNow,
                        MetadataJson = Serialize(new
                        {
                            phase = "trust-risk-radar.phase2",
                            thresholdDecision,
                            effectiveDecision = decision,
                            reviewQueueItemId = reviewItem?.Id,
                            trustAccountId,
                            sourceType,
                            sourceId,
                            triggerType
                        }),
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    _context.TrustRiskHolds.Add(hold);
                }
            }

            _context.TrustRiskActions.Add(new TrustRiskAction
            {
                TrustRiskEventId = riskEvent.Id,
                ActionType = decision switch
                {
                    "soft_hold" => "soft_hold",
                    "hard_hold" => "hard_hold",
                    "review_required" => "review_created",
                    "warn" => "warn",
                    _ => "recorded"
                },
                Status = "completed",
                ActorType = "system",
                CorrelationId = Truncate(correlationId, 128),
                Notes = decision switch
                {
                    "hard_hold" => $"Phase 2 hard hold placed (threshold decision: {thresholdDecision}).",
                    "soft_hold" => $"Phase 2 soft hold placed (threshold decision: {thresholdDecision}).",
                    "review_required" => $"Phase 2 trust risk review queued (threshold decision: {thresholdDecision}).",
                    "warn" => "Phase 2 trust risk warning recorded.",
                    _ => "Phase 2 trust risk event recorded."
                },
                MetadataJson = Serialize(new
                {
                    phase = "trust-risk-radar.phase2",
                    thresholdDecision,
                    effectiveDecision = decision,
                    trustAccountId,
                    reviewQueueItemId = reviewItem?.Id,
                    holdId = hold?.Id,
                    holdEnforced = holdDecision
                }),
                CreatedAt = DateTime.UtcNow
            });

            if (reviewItem != null && decision is "soft_hold" or "hard_hold")
            {
                _context.TrustRiskActions.Add(new TrustRiskAction
                {
                    TrustRiskEventId = riskEvent.Id,
                    ActionType = "review_created",
                    Status = "completed",
                    ActorType = "system",
                    CorrelationId = Truncate(correlationId, 128),
                    Notes = "Trust risk hold decision also queued review for human-in-the-loop handling.",
                    MetadataJson = Serialize(new { phase = "trust-risk-radar.phase2", reviewQueueItemId = reviewItem.Id, holdId = hold?.Id }),
                    CreatedAt = DateTime.UtcNow
                });
            }

            await TryQueueOpsAlertOutboxEventAsync(
                policy,
                ResolveOpsAlertEventType(decision),
                riskEvent.Id,
                decision,
                hold?.Id,
                reviewItem?.Id,
                new
                {
                    riskEventId = riskEvent.Id,
                    riskEvent.SourceType,
                    riskEvent.SourceId,
                    riskEvent.TriggerType,
                    riskEvent.RiskScore,
                    riskEvent.Severity,
                    thresholdDecision,
                    effectiveDecision = decision,
                    trustAccountId,
                    holdId = hold?.Id,
                    reviewQueueItemId = reviewItem?.Id
                },
                ct);

            await _context.SaveChangesAsync(ct);
            return riskEvent;
        }

        private async Task<TrustRiskPolicy> GetOrCreateActivePolicyAsync(CancellationToken ct)
        {
            var policy = await _context.TrustRiskPolicies
                .OrderByDescending(p => p.IsActive)
                .ThenByDescending(p => p.VersionNumber)
                .FirstOrDefaultAsync(p => p.PolicyKey == DefaultPolicyKey && p.IsActive, ct);
            if (policy != null)
            {
                return policy;
            }

            policy = new TrustRiskPolicy
            {
                PolicyKey = DefaultPolicyKey,
                VersionNumber = 1,
                IsActive = true,
                Status = "active",
                Name = "Default Trust Risk Radar Policy",
                Description = "Phase 2 deterministic trust risk radar policy with review + hold actions.",
                WarnThreshold = 35m,
                ReviewThreshold = 60m,
                SoftHoldThreshold = 80m,
                HardHoldThreshold = 95m,
                FailMode = "fail_open",
                EnabledRulesJson = Serialize(new[]
                {
                    "trust_domain_activity",
                    "reversal_activity",
                    "off_hours_activity",
                    "high_amount_threshold",
                    "missing_linkage",
                    "client_trust_subledger_negative",
                    "disbursement_exceeds_available_trust_balance",
                    "reversal_adjustment_burst",
                    "round_dollar_pattern",
                    "off_hours_high_value",
                    "matter_client_spike",
                    "behavioral_amount_outlier",
                    "behavioral_time_pattern_outlier",
                    "behavioral_reversal_pattern_outlier",
                    "trust_operating_mapping_mismatch",
                    "closed_or_inactive_matter_trust_activity",
                    "period_lock_interaction",
                    "period_lock_attempt"
                }),
                RuleWeightsJson = Serialize(new
                {
                    trustDomainActivity = 10,
                    reversalActivity = 30,
                    offHoursActivity = 12,
                    offHoursHighValue = 18,
                    highAmountThresholdMedium = 15,
                    highAmountThresholdHigh = 30,
                    missingLinkage = 10,
                    clientTrustNegativeSubledger = 35,
                    disbursementExceedsAvailableTrust = 40,
                    reversalAdjustmentBurst = 18,
                    reversalAdjustmentBurstHigh = 30,
                    allocationReversalBurst = 14,
                    allocationReversalBurstHigh = 24,
                    roundDollarPattern = 8,
                    matterClientSpike = 16,
                    matterClientSpikeHigh = 26,
                    trustOperatingMappingMismatch = 28,
                    closedMatterTrustMovement = 25,
                    periodLockInteraction = 35,
                    periodLockAttempt = 65,
                    trustFundedAllocation = 20,
                    missingPayorTarget = 15,
                    missingTrustSourceClient = 20,
                    behavioralAmountOutlier = 8,
                    behavioralTimePatternOutlier = 6,
                    behavioralReversalPatternOutlier = 7
                }),
                ActionMapJson = Serialize(new
                {
                    mode = "policy_actions_phase2",
                    warn = "persist_event_and_warn",
                    review = "create_review_queue_item",
                    softHold = "create_soft_hold",
                    hardHold = "create_hard_hold"
                }),
                MetadataJson = Serialize(new
                {
                    phase = "trust-risk-radar.phaseX2",
                    createdBy = "system",
                    policyTemplate = "balanced",
                    note = "Auto-created default policy for trust risk radar (Phase X2 role split + SLA + behavioral-ready).",
                    holdEnforcementEnabled = true,
                    decisionStrategy = "deterministic_thresholds_policy_actions",
                    overrideRoles = new[] { "SecurityAdmin", "Admin" },
                    releaseRoles = new[] { "FinanceAdmin", "Admin" },
                    criticalDualApprovalEnabled = false,
                    criticalDualApprovalSecondaryRoles = new[] { "SecurityAdmin", "Admin" },
                    holdEscalationSlaMinutes = 120,
                    softHoldExpiryHours = 24,
                    hardHoldExpiryHours = 72,
                    criticalThresholdChangeReviewRequired = true,
                    opsAlertsEnabled = false,
                    opsAlertChannels = new[] { "outbox" },
                    behavioralSignalsEnabled = true,
                    behavioralShadowMode = true,
                    behavioralLookbackDays = 45,
                    behavioralMinSamples = 10,
                    behavioralAmountRatioThreshold = 4,
                    behavioralTimePatternDeltaThreshold = 0.35,
                    behavioralReversalRateDeltaThreshold = 0.20,
                    behavioralContributionCap = 18
                }),
                CreatedBy = "system",
                UpdatedBy = "system",
                PublishedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.TrustRiskPolicies.Add(policy);
            try
            {
                await _context.SaveChangesAsync(ct);
                return policy;
            }
            catch (DbUpdateException)
            {
                // Concurrent first-write race; reload the tenant-scoped active default.
                _context.Entry(policy).State = EntityState.Detached;
                return await _context.TrustRiskPolicies
                    .OrderByDescending(p => p.VersionNumber)
                    .FirstAsync(p => p.PolicyKey == DefaultPolicyKey && p.IsActive, ct);
            }
        }

        private async Task<TrustRiskEvaluation> EnrichLedgerEvaluationAsync(
            BillingLedgerEntry entry,
            string triggerType,
            TrustRiskEvaluation baseEvaluation,
            TrustRiskPolicy policy,
            CancellationToken ct)
        {
            var weights = ResolvePolicyWeights(policy);
            var reasons = baseEvaluation.Reasons.ToList();
            var evidence = new Dictionary<string, object?>(baseEvaluation.Evidence, StringComparer.OrdinalIgnoreCase);
            var features = new Dictionary<string, object?>(baseEvaluation.Features, StringComparer.OrdinalIgnoreCase);
            var evidenceRefs = ExtractEvidenceRefs(evidence);
            AddEvidenceRef(evidenceRefs, "billing_ledger_entry", entry.Id);
            AddEvidenceRef(evidenceRefs, "invoice", entry.InvoiceId);
            AddEvidenceRef(evidenceRefs, "payment_transaction", entry.PaymentTransactionId);
            AddEvidenceRef(evidenceRefs, "trust_transaction", entry.TrustTransactionId);

            var score = baseEvaluation.Score;
            var absoluteAmount = NormalizeMoney(Math.Abs(entry.Amount));
            var isTrustRelated = string.Equals(entry.LedgerDomain, "trust", StringComparison.OrdinalIgnoreCase) ||
                                 !string.IsNullOrWhiteSpace(entry.TrustTransactionId) ||
                                 (entry.MetadataJson?.Contains("\"fundSource\":\"trust\"", StringComparison.OrdinalIgnoreCase) ?? false);

            score += ApplyRoundDollarRule(absoluteAmount, reasons, weights);
            if (IsOffHours(entry.PostedAt) && absoluteAmount >= 2500m)
            {
                var weight = Weight(weights, "offHoursHighValue", 18m);
                score += weight;
                AddReason(reasons, "off_hours_high_value", "High-value movement occurred off-hours/weekend.", weight, new { amount = absoluteAmount, postedAt = entry.PostedAt });
            }

            score += await ApplyReversalAdjustmentBurstRuleForLedgerAsync(entry, triggerType, reasons, evidence, weights, ct);
            score += await ApplyMatterStatusRuleAsync(entry.MatterId, isTrustRelated, reasons, evidence, weights, ct);
            score += await ApplySpikeRuleForLedgerAsync(entry, absoluteAmount, reasons, evidence, weights, ct);
            score += await ApplyPeriodLockPostedFlagRuleAsync(entry.PostedAt, reasons, evidence, weights, ct);
            score += await ApplyTrustBalanceRulesForLedgerAsync(entry, reasons, evidence, weights, ct);
            score += await ApplyBehavioralSignalsForLedgerAsync(entry, absoluteAmount, reasons, evidence, features, weights, policy, ct);

            if (string.IsNullOrWhiteSpace(entry.CorrelationKey) &&
                string.Equals(entry.EntryType, "payment_allocation", StringComparison.OrdinalIgnoreCase))
            {
                var weight = Weight(weights, "missingLinkage", 10m);
                score += weight;
                AddReason(reasons, "missing_correlation_key", "Payment allocation ledger entry is missing CorrelationKey.", weight);
            }

            evidence["evidenceRefs"] = evidenceRefs;
            features["phase"] = "trust-risk-radar.phase4";
            features["isTrustRelated"] = isTrustRelated;
            features["triggerType"] = triggerType;

            return new TrustRiskEvaluation(ClampScore(score), MapSeverity(score), reasons, evidence, features);
        }

        private async Task<TrustRiskEvaluation> EnrichPaymentAllocationEvaluationAsync(
            BillingPaymentAllocation allocation,
            string triggerType,
            TrustRiskEvaluation baseEvaluation,
            TrustRiskPolicy policy,
            CancellationToken ct)
        {
            var weights = ResolvePolicyWeights(policy);
            var reasons = baseEvaluation.Reasons.ToList();
            var evidence = new Dictionary<string, object?>(baseEvaluation.Evidence, StringComparer.OrdinalIgnoreCase);
            var features = new Dictionary<string, object?>(baseEvaluation.Features, StringComparer.OrdinalIgnoreCase);
            var evidenceRefs = ExtractEvidenceRefs(evidence);
            AddEvidenceRef(evidenceRefs, "billing_payment_allocation", allocation.Id);
            AddEvidenceRef(evidenceRefs, "payment_transaction", allocation.PaymentTransactionId);
            AddEvidenceRef(evidenceRefs, "invoice", allocation.InvoiceId);
            AddEvidenceRef(evidenceRefs, "invoice_payor_allocation", allocation.InvoicePayorAllocationId);

            var score = baseEvaluation.Score;
            var absoluteAmount = NormalizeMoney(Math.Abs(allocation.Amount));
            score += ApplyRoundDollarRule(absoluteAmount, reasons, weights);
            if (IsOffHours(allocation.AppliedAt) && absoluteAmount >= 2500m)
            {
                var weight = Weight(weights, "offHoursHighValue", 18m);
                score += weight;
                AddReason(reasons, "off_hours_high_value", "High-value payment allocation occurred off-hours/weekend.", weight, new { amount = absoluteAmount, appliedAt = allocation.AppliedAt });
            }

            score += await ApplyMatterStatusRuleAsync(allocation.MatterId, true, reasons, evidence, weights, ct);
            score += await ApplySpikeRuleForAllocationAsync(allocation, absoluteAmount, reasons, evidence, weights, ct);
            score += await ApplyReversalBurstRuleForAllocationAsync(allocation, triggerType, reasons, evidence, weights, ct);
            score += await ApplyPeriodLockPostedFlagRuleAsync(allocation.AppliedAt, reasons, evidence, weights, ct);
            score += await ApplyTrustAllocationRulesAsync(allocation, reasons, evidence, weights, ct);
            score += await ApplyMissingSupportingLinkRuleForAllocationAsync(allocation, reasons, evidence, weights, ct);
            score += await ApplyBehavioralSignalsForAllocationAsync(allocation, absoluteAmount, reasons, evidence, features, weights, policy, ct);

            evidence["evidenceRefs"] = evidenceRefs;
            features["phase"] = "trust-risk-radar.phase4";
            features["triggerType"] = triggerType;

            return new TrustRiskEvaluation(ClampScore(score), MapSeverity(score), reasons, evidence, features);
        }

        private async Task<TrustRiskEvaluation> EnrichTrustTransactionEvaluationAsync(
            TrustTransaction transaction,
            string triggerType,
            TrustRiskEvaluation baseEvaluation,
            TrustRiskPolicy policy,
            CancellationToken ct)
        {
            var weights = ResolvePolicyWeights(policy);
            var reasons = baseEvaluation.Reasons.ToList();
            var evidence = new Dictionary<string, object?>(baseEvaluation.Evidence, StringComparer.OrdinalIgnoreCase);
            var features = new Dictionary<string, object?>(baseEvaluation.Features, StringComparer.OrdinalIgnoreCase);
            var evidenceRefs = ExtractEvidenceRefs(evidence);
            AddEvidenceRef(evidenceRefs, "trust_transaction", transaction.Id);
            AddEvidenceRef(evidenceRefs, "billing_ledger_entry", transaction.LedgerId);

            var ts = transaction.UpdatedAt == default ? transaction.CreatedAt : transaction.UpdatedAt;
            var score = baseEvaluation.Score;
            var absoluteAmount = NormalizeMoney(Math.Abs(transaction.Amount));
            score += ApplyRoundDollarRule(absoluteAmount, reasons, weights);
            if (IsOffHours(ts) && absoluteAmount >= 2500m)
            {
                var weight = Weight(weights, "offHoursHighValue", 18m);
                score += weight;
                AddReason(reasons, "off_hours_high_value", "High-value trust transaction occurred off-hours/weekend.", weight, new { amount = absoluteAmount, occurredAt = ts });
            }

            score += await ApplyMatterStatusRuleAsync(transaction.MatterId, true, reasons, evidence, weights, ct);
            score += await ApplyPeriodLockPostedFlagRuleAsync(ts, reasons, evidence, weights, ct);
            score += await ApplyTrustBalanceRulesForTrustTransactionAsync(transaction, absoluteAmount, reasons, evidence, weights, ct);
            score += await ApplyBehavioralSignalsForTrustTransactionAsync(transaction, ts, absoluteAmount, reasons, evidence, features, weights, policy, ct);

            evidence["evidenceRefs"] = evidenceRefs;
            features["phase"] = "trust-risk-radar.phase4";
            features["triggerType"] = triggerType;

            return new TrustRiskEvaluation(ClampScore(score), MapSeverity(score), reasons, evidence, features);
        }

        private TrustRiskEvaluation EvaluateLedgerEntry(BillingLedgerEntry entry)
        {
            var score = 0m;
            var reasons = new List<object>();
            var evidence = new Dictionary<string, object?>
            {
                ["ledgerDomain"] = entry.LedgerDomain,
                ["ledgerBucket"] = entry.LedgerBucket,
                ["entryType"] = entry.EntryType,
                ["amount"] = entry.Amount,
                ["invoiceId"] = entry.InvoiceId,
                ["paymentTransactionId"] = entry.PaymentTransactionId,
                ["trustTransactionId"] = entry.TrustTransactionId,
                ["payorClientId"] = entry.PayorClientId,
                ["postedAt"] = entry.PostedAt
            };

            if (string.Equals(entry.LedgerDomain, "trust", StringComparison.OrdinalIgnoreCase))
            {
                score += 10m;
                AddReason(reasons, "trust_domain_activity", "Trust domain ledger activity recorded.", 10m);
            }

            if (string.Equals(entry.EntryType, "reversal", StringComparison.OrdinalIgnoreCase))
            {
                score += 30m;
                AddReason(reasons, "reversal_activity", "Ledger reversal activity increases trust risk scrutiny.", 30m);
            }

            score += ScoreAmountBand(NormalizeMoney(Math.Abs(entry.Amount)), reasons);

            if (IsOffHours(entry.PostedAt))
            {
                score += 12m;
                AddReason(reasons, "off_hours_activity", "Ledger activity occurred outside standard business hours.", 12m);
            }

            if (string.Equals(entry.LedgerDomain, "trust", StringComparison.OrdinalIgnoreCase) &&
                entry.Amount < 0m &&
                string.IsNullOrWhiteSpace(entry.TrustTransactionId))
            {
                score += 15m;
                AddReason(reasons, "missing_trust_transaction_link", "Trust reduction ledger entry is missing TrustTransactionId correlation.", 15m);
            }

            if (string.IsNullOrWhiteSpace(entry.InvoiceId) &&
                string.IsNullOrWhiteSpace(entry.PaymentTransactionId) &&
                string.IsNullOrWhiteSpace(entry.TrustTransactionId))
            {
                score += 10m;
                AddReason(reasons, "missing_linkage", "Ledger entry has no invoice/payment/trust linkage.", 10m);
            }

            var features = new Dictionary<string, object?>
            {
                ["signedAmount"] = entry.Amount,
                ["absoluteAmount"] = NormalizeMoney(Math.Abs(entry.Amount)),
                ["hasInvoiceLink"] = !string.IsNullOrWhiteSpace(entry.InvoiceId),
                ["hasPaymentLink"] = !string.IsNullOrWhiteSpace(entry.PaymentTransactionId),
                ["hasTrustLink"] = !string.IsNullOrWhiteSpace(entry.TrustTransactionId),
                ["isOffHours"] = IsOffHours(entry.PostedAt),
                ["isWeekend"] = IsWeekend(entry.PostedAt)
            };

            return new TrustRiskEvaluation(ClampScore(score), MapSeverity(score), reasons, evidence, features);
        }

        private TrustRiskEvaluation EvaluatePaymentAllocation(BillingPaymentAllocation allocation)
        {
            var score = 0m;
            var reasons = new List<object>();
            var evidence = new Dictionary<string, object?>
            {
                ["paymentTransactionId"] = allocation.PaymentTransactionId,
                ["invoiceId"] = allocation.InvoiceId,
                ["invoiceLineItemId"] = allocation.InvoiceLineItemId,
                ["allocationType"] = allocation.AllocationType,
                ["amount"] = allocation.Amount,
                ["payorClientId"] = allocation.PayorClientId,
                ["invoicePayorAllocationId"] = allocation.InvoicePayorAllocationId,
                ["appliedAt"] = allocation.AppliedAt
            };

            score += ScoreAmountBand(NormalizeMoney(Math.Abs(allocation.Amount)), reasons);

            if (IsOffHours(allocation.AppliedAt))
            {
                score += 10m;
                AddReason(reasons, "off_hours_activity", "Payment allocation applied outside standard business hours.", 10m);
            }

            if (string.IsNullOrWhiteSpace(allocation.PayorClientId) && string.IsNullOrWhiteSpace(allocation.InvoicePayorAllocationId))
            {
                score += 15m;
                AddReason(reasons, "missing_payor_target", "Allocation is missing payor target correlation.", 15m);
            }

            var metadata = TryParseJsonObject(allocation.MetadataJson);
            var fundSource = metadata.TryGetValue("fundSource", out var fundSourceValue) ? fundSourceValue : null;
            var trustSourceClientId = metadata.TryGetValue("trustSourceClientId", out var trustSourceValue) ? trustSourceValue : null;
            if (string.Equals(fundSource, "trust", StringComparison.OrdinalIgnoreCase))
            {
                score += 20m;
                AddReason(reasons, "trust_funded_allocation", "Trust-funded allocation requires elevated scrutiny.", 20m);

                if (string.IsNullOrWhiteSpace(trustSourceClientId) &&
                    !string.IsNullOrWhiteSpace(allocation.PayorClientId) &&
                    !string.Equals(allocation.PayorClientId, allocation.ClientId, StringComparison.Ordinal))
                {
                    score += 20m;
                    AddReason(reasons, "missing_trust_source_client", "Third-party trust-funded allocation is missing explicit TrustSourceClientId.", 20m);
                }
            }

            var features = new Dictionary<string, object?>
            {
                ["absoluteAmount"] = NormalizeMoney(Math.Abs(allocation.Amount)),
                ["allocationType"] = allocation.AllocationType,
                ["fundSource"] = fundSource,
                ["hasPayorClientId"] = !string.IsNullOrWhiteSpace(allocation.PayorClientId),
                ["hasInvoicePayorAllocationId"] = !string.IsNullOrWhiteSpace(allocation.InvoicePayorAllocationId),
                ["hasLedgerEntryId"] = !string.IsNullOrWhiteSpace(allocation.LedgerEntryId),
                ["isOffHours"] = IsOffHours(allocation.AppliedAt),
                ["isWeekend"] = IsWeekend(allocation.AppliedAt)
            };

            evidence["fundSource"] = fundSource;
            evidence["trustSourceClientId"] = trustSourceClientId;

            return new TrustRiskEvaluation(ClampScore(score), MapSeverity(score), reasons, evidence, features);
        }

        private TrustRiskEvaluation EvaluateTrustTransaction(TrustTransaction transaction)
        {
            var amount = NormalizeMoney(Math.Abs(transaction.Amount));
            var score = 0m;
            var reasons = new List<object>();
            var evidence = new Dictionary<string, object?>
            {
                ["trustAccountId"] = transaction.TrustAccountId,
                ["matterId"] = transaction.MatterId,
                ["type"] = transaction.Type,
                ["amount"] = amount,
                ["status"] = transaction.Status,
                ["isVoided"] = transaction.IsVoided,
                ["ledgerId"] = transaction.LedgerId,
                ["createdAt"] = transaction.CreatedAt,
                ["updatedAt"] = transaction.UpdatedAt
            };

            score += ScoreAmountBand(amount, reasons);

            var ts = transaction.UpdatedAt == default ? transaction.CreatedAt : transaction.UpdatedAt;
            if (IsOffHours(ts))
            {
                score += 10m;
                AddReason(reasons, "off_hours_activity", "Trust transaction activity occurred outside standard business hours.", 10m);
            }

            if (transaction.IsVoided)
            {
                score += 20m;
                AddReason(reasons, "voided_trust_transaction", "Voided trust transaction requires reviewable audit trail scrutiny.", 20m);
            }

            if ((transaction.Type?.Contains("withdraw", StringComparison.OrdinalIgnoreCase) == true ||
                 transaction.Type?.Contains("earned", StringComparison.OrdinalIgnoreCase) == true) &&
                string.IsNullOrWhiteSpace(transaction.LedgerId))
            {
                score += 15m;
                AddReason(reasons, "missing_ledger_link", "Trust transaction is missing linked ledger record correlation.", 15m);
            }

            var features = new Dictionary<string, object?>
            {
                ["absoluteAmount"] = amount,
                ["isOffHours"] = IsOffHours(ts),
                ["isWeekend"] = IsWeekend(ts),
                ["isVoided"] = transaction.IsVoided,
                ["hasLedgerId"] = !string.IsNullOrWhiteSpace(transaction.LedgerId)
            };

            return new TrustRiskEvaluation(ClampScore(score), MapSeverity(score), reasons, evidence, features);
        }

        private async Task<decimal> ApplyMatterStatusRuleAsync(
            string? matterId,
            bool trustRelated,
            List<object> reasons,
            Dictionary<string, object?> evidence,
            IReadOnlyDictionary<string, decimal> weights,
            CancellationToken ct)
        {
            if (!trustRelated || string.IsNullOrWhiteSpace(matterId))
            {
                return 0m;
            }

            var matter = await _context.Matters.AsNoTracking()
                .Select(m => new { m.Id, m.Status })
                .FirstOrDefaultAsync(m => m.Id == matterId, ct);
            if (matter == null)
            {
                return 0m;
            }

            evidence["matterStatus"] = matter.Status;
            if (string.Equals(matter.Status, "Closed", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(matter.Status, "Inactive", StringComparison.OrdinalIgnoreCase))
            {
                var weight = Weight(weights, "closedMatterTrustMovement", 25m);
                AddReason(reasons, "closed_or_inactive_matter_trust_activity", "Trust-related activity references a closed/inactive matter.", weight, new { matterId, matter.Status });
                return weight;
            }

            return 0m;
        }

        private async Task<decimal> ApplyPeriodLockPostedFlagRuleAsync(
            DateTime occurredAtUtc,
            List<object> reasons,
            Dictionary<string, object?> evidence,
            IReadOnlyDictionary<string, decimal> weights,
            CancellationToken ct)
        {
            var day = DateOnly.FromDateTime(occurredAtUtc);
            var isLocked = await IsDateInLockedPeriodAsync(day, ct);
            evidence["isBillingPeriodLockedAtOccurrence"] = isLocked;
            if (!isLocked)
            {
                return 0m;
            }

            var weight = Weight(weights, "periodLockInteraction", 35m);
            AddReason(reasons, "period_lock_interaction", "Activity timestamp falls within a locked billing period.", weight, new { occurredAtUtc = occurredAtUtc.ToString("O") });
            return weight;
        }

        private async Task<decimal> ApplyReversalAdjustmentBurstRuleForLedgerAsync(
            BillingLedgerEntry entry,
            string triggerType,
            List<object> reasons,
            Dictionary<string, object?> evidence,
            IReadOnlyDictionary<string, decimal> weights,
            CancellationToken ct)
        {
            var type = (entry.EntryType ?? string.Empty).Trim().ToLowerInvariant();
            var trigger = (triggerType ?? string.Empty).Trim().ToLowerInvariant();
            var isTarget = type is "reversal" or "adjustment" or "writeoff" or "credit_memo" ||
                           trigger.Contains("reversal", StringComparison.Ordinal);
            if (!isTarget)
            {
                return 0m;
            }

            var since = entry.PostedAt.AddHours(-24);
            var query = _context.BillingLedgerEntries.AsNoTracking()
                .Where(e => e.PostedAt >= since && e.PostedAt <= entry.PostedAt);
            if (!string.IsNullOrWhiteSpace(entry.MatterId))
            {
                query = query.Where(e => e.MatterId == entry.MatterId);
            }
            else if (!string.IsNullOrWhiteSpace(entry.ClientId))
            {
                query = query.Where(e => e.ClientId == entry.ClientId);
            }
            else
            {
                return 0m;
            }

            var count = await query.CountAsync(e =>
                e.EntryType == "reversal" ||
                e.EntryType == "adjustment" ||
                e.EntryType == "writeoff" ||
                e.EntryType == "credit_memo", ct);

            evidence["recentReversalAdjustmentCount24h"] = count;
            if (count < 3)
            {
                return 0m;
            }

            var weight = count >= 5 ? Weight(weights, "reversalAdjustmentBurstHigh", 30m) : Weight(weights, "reversalAdjustmentBurst", 18m);
            AddReason(reasons, "reversal_adjustment_burst", "Multiple reversals/adjustments detected in a short window.", weight, new { count24h = count });
            return weight;
        }

        private async Task<decimal> ApplyReversalBurstRuleForAllocationAsync(
            BillingPaymentAllocation allocation,
            string triggerType,
            List<object> reasons,
            Dictionary<string, object?> evidence,
            IReadOnlyDictionary<string, decimal> weights,
            CancellationToken ct)
        {
            if (!string.Equals(allocation.Status, "reversed", StringComparison.OrdinalIgnoreCase) &&
                !(triggerType?.Contains("reversed", StringComparison.OrdinalIgnoreCase) ?? false))
            {
                return 0m;
            }

            var since = allocation.AppliedAt.AddHours(-24);
            var q = _context.BillingPaymentAllocations.AsNoTracking()
                .Where(a => a.AppliedAt >= since && a.AppliedAt <= allocation.AppliedAt && a.Status == "reversed");
            if (!string.IsNullOrWhiteSpace(allocation.MatterId))
            {
                q = q.Where(a => a.MatterId == allocation.MatterId);
            }
            else if (!string.IsNullOrWhiteSpace(allocation.ClientId))
            {
                q = q.Where(a => a.ClientId == allocation.ClientId);
            }
            else
            {
                return 0m;
            }

            var count = await q.CountAsync(ct);
            evidence["recentAllocationReversalCount24h"] = count;
            if (count < 2)
            {
                return 0m;
            }

            var weight = count >= 4 ? Weight(weights, "allocationReversalBurstHigh", 24m) : Weight(weights, "allocationReversalBurst", 14m);
            AddReason(reasons, "allocation_reversal_burst", "Repeated payment allocation reversals detected.", weight, new { count24h = count });
            return weight;
        }

        private async Task<decimal> ApplySpikeRuleForLedgerAsync(
            BillingLedgerEntry entry,
            decimal absoluteAmount,
            List<object> reasons,
            Dictionary<string, object?> evidence,
            IReadOnlyDictionary<string, decimal> weights,
            CancellationToken ct)
        {
            if (absoluteAmount <= 0m)
            {
                return 0m;
            }

            var since = entry.PostedAt.AddDays(-30);
            var q = _context.BillingLedgerEntries.AsNoTracking()
                .Where(e => e.Id != entry.Id && e.PostedAt >= since && e.PostedAt < entry.PostedAt && e.Amount != 0m);

            if (!string.IsNullOrWhiteSpace(entry.MatterId))
            {
                q = q.Where(e => e.MatterId == entry.MatterId);
            }
            else if (!string.IsNullOrWhiteSpace(entry.ClientId))
            {
                q = q.Where(e => e.ClientId == entry.ClientId);
            }
            else
            {
                return 0m;
            }

            q = q.Where(e => e.LedgerDomain == entry.LedgerDomain);
            var baseline = await q.Select(e => e.Amount).ToListAsync(ct);
            if (baseline.Count < 5)
            {
                evidence["spikeBaselineCount"] = baseline.Count;
                return 0m;
            }

            var avgAbs = NormalizeMoney((decimal)baseline.Average(v => Math.Abs(v)));
            evidence["spikeBaselineCount"] = baseline.Count;
            evidence["spikeBaselineAverageAmount"] = avgAbs;
            if (avgAbs <= 0m)
            {
                return 0m;
            }

            var ratio = NormalizeMoney(absoluteAmount / avgAbs);
            evidence["spikeRatio"] = ratio;
            if (ratio < 4m || absoluteAmount < 500m)
            {
                return 0m;
            }

            var weight = ratio >= 8m ? Weight(weights, "matterClientSpikeHigh", 26m) : Weight(weights, "matterClientSpike", 16m);
            AddReason(reasons, "matter_client_spike", "Current amount materially exceeds recent matter/client baseline.", weight, new { baselineAvg = avgAbs, ratio });
            return weight;
        }

        private async Task<decimal> ApplySpikeRuleForAllocationAsync(
            BillingPaymentAllocation allocation,
            decimal absoluteAmount,
            List<object> reasons,
            Dictionary<string, object?> evidence,
            IReadOnlyDictionary<string, decimal> weights,
            CancellationToken ct)
        {
            if (absoluteAmount <= 0m)
            {
                return 0m;
            }

            var since = allocation.AppliedAt.AddDays(-30);
            var q = _context.BillingPaymentAllocations.AsNoTracking()
                .Where(a => a.Id != allocation.Id && a.AppliedAt >= since && a.AppliedAt < allocation.AppliedAt && a.Status == "applied");

            if (!string.IsNullOrWhiteSpace(allocation.MatterId))
            {
                q = q.Where(a => a.MatterId == allocation.MatterId);
            }
            else if (!string.IsNullOrWhiteSpace(allocation.ClientId))
            {
                q = q.Where(a => a.ClientId == allocation.ClientId);
            }
            else
            {
                return 0m;
            }

            var baseline = await q.Select(a => a.Amount).ToListAsync(ct);
            if (baseline.Count < 5)
            {
                evidence["allocationSpikeBaselineCount"] = baseline.Count;
                return 0m;
            }

            var avgAbs = NormalizeMoney((decimal)baseline.Average(v => Math.Abs(v)));
            evidence["allocationSpikeBaselineCount"] = baseline.Count;
            evidence["allocationSpikeBaselineAverageAmount"] = avgAbs;
            if (avgAbs <= 0m)
            {
                return 0m;
            }

            var ratio = NormalizeMoney(absoluteAmount / avgAbs);
            evidence["allocationSpikeRatio"] = ratio;
            if (ratio < 4m || absoluteAmount < 500m)
            {
                return 0m;
            }

            var weight = ratio >= 8m ? Weight(weights, "matterClientSpikeHigh", 26m) : Weight(weights, "matterClientSpike", 16m);
            AddReason(reasons, "matter_client_spike", "Payment allocation amount materially exceeds recent matter/client baseline.", weight, new { baselineAvg = avgAbs, ratio });
            return weight;
        }

        private async Task<decimal> ApplyTrustBalanceRulesForLedgerAsync(
            BillingLedgerEntry entry,
            List<object> reasons,
            Dictionary<string, object?> evidence,
            IReadOnlyDictionary<string, decimal> weights,
            CancellationToken ct)
        {
            if (!string.Equals(entry.LedgerDomain, "trust", StringComparison.OrdinalIgnoreCase) || entry.Amount >= 0m)
            {
                return 0m;
            }

            var clientId = entry.ClientId;
            if (string.IsNullOrWhiteSpace(clientId) && !string.IsNullOrWhiteSpace(entry.MatterId))
            {
                clientId = await _context.Matters.AsNoTracking()
                    .Where(m => m.Id == entry.MatterId)
                    .Select(m => m.ClientId)
                    .FirstOrDefaultAsync(ct);
            }
            if (string.IsNullOrWhiteSpace(clientId))
            {
                return 0m;
            }

            var available = await GetClientTrustAvailableBalanceAsync(clientId, entry.MatterId, null, ct);
            evidence["clientTrustAvailableBalance"] = available;

            var score = 0m;
            if (available < 0m)
            {
                var weight = Weight(weights, "clientTrustNegativeSubledger", 35m);
                score += weight;
                AddReason(reasons, "client_trust_subledger_negative", "Client trust subledger balance is negative.", weight, new { availableBalance = available, clientId, entry.MatterId });
            }

            var disbursementAmount = NormalizeMoney(Math.Abs(entry.Amount));
            if (disbursementAmount > available && available >= 0m)
            {
                var weight = Weight(weights, "disbursementExceedsAvailableTrust", 40m);
                score += weight;
                AddReason(reasons, "disbursement_exceeds_available_trust_balance", "Trust disbursement exceeds available client trust balance.", weight, new { requested = disbursementAmount, availableBalance = available });
            }

            return score;
        }

        private async Task<decimal> ApplyTrustBalanceRulesForTrustTransactionAsync(
            TrustTransaction transaction,
            decimal absoluteAmount,
            List<object> reasons,
            Dictionary<string, object?> evidence,
            IReadOnlyDictionary<string, decimal> weights,
            CancellationToken ct)
        {
            var type = (transaction.Type ?? string.Empty).Trim().ToLowerInvariant();
            var isDisbursement = type.Contains("withdraw", StringComparison.Ordinal) ||
                                 type.Contains("earned", StringComparison.Ordinal) ||
                                 type.Contains("refund", StringComparison.Ordinal);
            if (!isDisbursement)
            {
                return 0m;
            }

            var clientId = !string.IsNullOrWhiteSpace(transaction.MatterId)
                ? await _context.Matters.AsNoTracking().Where(m => m.Id == transaction.MatterId).Select(m => m.ClientId).FirstOrDefaultAsync(ct)
                : null;
            if (string.IsNullOrWhiteSpace(clientId))
            {
                return 0m;
            }

            var available = await GetClientTrustAvailableBalanceAsync(clientId, transaction.MatterId, transaction.TrustAccountId, ct);
            evidence["clientTrustAvailableBalance"] = available;

            var score = 0m;
            if (available < 0m)
            {
                var w = Weight(weights, "clientTrustNegativeSubledger", 35m);
                score += w;
                AddReason(reasons, "client_trust_subledger_negative", "Client trust subledger balance is negative.", w, new { availableBalance = available, clientId, transaction.MatterId });
            }

            if (absoluteAmount > available && available >= 0m)
            {
                var w = Weight(weights, "disbursementExceedsAvailableTrust", 40m);
                score += w;
                AddReason(reasons, "disbursement_exceeds_available_trust_balance", "Trust transaction amount exceeds available client trust balance.", w, new { requested = absoluteAmount, availableBalance = available });
            }

            return score;
        }

        private async Task<decimal> ApplyTrustAllocationRulesAsync(
            BillingPaymentAllocation allocation,
            List<object> reasons,
            Dictionary<string, object?> evidence,
            IReadOnlyDictionary<string, decimal> weights,
            CancellationToken ct)
        {
            var metadata = TryParseJsonObject(allocation.MetadataJson);
            var fundSource = metadata.TryGetValue("fundSource", out var v) ? v : null;
            var trustSourceClientId = metadata.TryGetValue("trustSourceClientId", out var ts) ? ts : null;
            var trustAccountId = metadata.TryGetValue("trustAccountId", out var ta) ? ta : null;
            if (!string.Equals(fundSource, "trust", StringComparison.OrdinalIgnoreCase))
            {
                return 0m;
            }

            var score = 0m;
            var clientId = trustSourceClientId ?? allocation.ClientId ?? allocation.PayorClientId;
            if (!string.IsNullOrWhiteSpace(clientId))
            {
                var available = await GetClientTrustAvailableBalanceAsync(clientId, allocation.MatterId, trustAccountId, ct);
                evidence["clientTrustAvailableBalance"] = available;
                if (available < 0m)
                {
                    var w = Weight(weights, "clientTrustNegativeSubledger", 35m);
                    score += w;
                    AddReason(reasons, "client_trust_subledger_negative", "Client trust subledger balance is negative during trust-funded allocation.", w, new { availableBalance = available, clientId, allocation.MatterId });
                }
                if (allocation.Amount > available && available >= 0m)
                {
                    var w = Weight(weights, "disbursementExceedsAvailableTrust", 40m);
                    score += w;
                    AddReason(reasons, "disbursement_exceeds_available_trust_balance", "Trust-funded allocation exceeds available client trust balance.", w, new { requested = allocation.Amount, availableBalance = available });
                }
            }

            var ledgers = await _context.BillingLedgerEntries.AsNoTracking()
                .Where(e => e.CorrelationKey != null && e.CorrelationKey.StartsWith($"allocation:{allocation.Id}:"))
                .Select(e => new { e.Id, e.LedgerDomain, e.LedgerBucket, e.PayorClientId, e.InvoicePayorAllocationId })
                .ToListAsync(ct);
            evidence["allocationLedgerCount"] = ledgers.Count;
            var evidenceRefs = ExtractEvidenceRefs(evidence);
            foreach (var ledger in ledgers)
            {
                AddEvidenceRef(evidenceRefs, "billing_ledger_entry", ledger.Id);
            }

            var hasTrustLiability = ledgers.Any(l => string.Equals(l.LedgerDomain, "trust", StringComparison.OrdinalIgnoreCase) &&
                                                    string.Equals(l.LedgerBucket, "trust_liability", StringComparison.OrdinalIgnoreCase));
            var hasOperatingCash = ledgers.Any(l => string.Equals(l.LedgerDomain, "operating", StringComparison.OrdinalIgnoreCase) &&
                                                   string.Equals(l.LedgerBucket, "cash", StringComparison.OrdinalIgnoreCase));
            var hasBillingAr = ledgers.Any(l => string.Equals(l.LedgerDomain, "billing", StringComparison.OrdinalIgnoreCase) &&
                                               string.Equals(l.LedgerBucket, "accounts_receivable", StringComparison.OrdinalIgnoreCase));
            if (!(hasTrustLiability && hasOperatingCash && hasBillingAr))
            {
                var w = Weight(weights, "trustOperatingMappingMismatch", 28m);
                score += w;
                AddReason(reasons, "trust_operating_mapping_mismatch", "Trust-funded allocation is missing expected trust/operating/A/R ledger postings.", w, new { hasTrustLiability, hasOperatingCash, hasBillingAr });
            }

            if (!string.IsNullOrWhiteSpace(allocation.PayorClientId) &&
                ledgers.Any(l => !string.IsNullOrWhiteSpace(l.PayorClientId) && !string.Equals(l.PayorClientId, allocation.PayorClientId, StringComparison.Ordinal)))
            {
                var w = Weight(weights, "trustOperatingMappingMismatch", 28m);
                score += w;
                AddReason(reasons, "allocation_payor_mapping_mismatch", "Allocation ledger entries contain mismatched payor mapping.", w, new { allocation.PayorClientId });
            }

            return score;
        }

        private async Task<decimal> ApplyMissingSupportingLinkRuleForAllocationAsync(
            BillingPaymentAllocation allocation,
            List<object> reasons,
            Dictionary<string, object?> evidence,
            IReadOnlyDictionary<string, decimal> weights,
            CancellationToken ct)
        {
            var score = 0m;
            if (string.IsNullOrWhiteSpace(allocation.PaymentTransactionId) || string.IsNullOrWhiteSpace(allocation.InvoiceId))
            {
                var w = Weight(weights, "missingLinkage", 10m);
                score += w;
                AddReason(reasons, "missing_linkage", "Payment allocation is missing invoice/payment correlation.", w);
            }

            var ledgerCount = await _context.BillingLedgerEntries.AsNoTracking()
                .CountAsync(e => e.CorrelationKey != null && e.CorrelationKey.StartsWith($"allocation:{allocation.Id}:"), ct);
            evidence["allocationLedgerCount"] = ledgerCount;
            if (ledgerCount == 0)
            {
                var w = Weight(weights, "missingLinkage", 10m);
                score += w;
                AddReason(reasons, "missing_allocation_ledger_entries", "Payment allocation has no supporting ledger entries.", w);
            }

            return score;
        }

        private async Task<decimal> ApplyBehavioralSignalsForLedgerAsync(
            BillingLedgerEntry entry,
            decimal absoluteAmount,
            List<object> reasons,
            Dictionary<string, object?> evidence,
            Dictionary<string, object?> features,
            IReadOnlyDictionary<string, decimal> weights,
            TrustRiskPolicy policy,
            CancellationToken ct)
        {
            var options = ResolveBehavioralOptions(policy);
            if (!options.Enabled || absoluteAmount <= 0m)
            {
                features["behavioral"] = new { enabled = false, reason = "disabled_or_zero_amount" };
                return 0m;
            }

            var occurredAt = entry.PostedAt;
            var since = occurredAt.AddDays(-options.LookbackDays);
            var tenantRows = await _context.BillingLedgerEntries.AsNoTracking()
                .Where(e => e.Id != entry.Id && e.PostedAt >= since && e.PostedAt < occurredAt)
                .Select(e => new { e.Amount, e.PostedAt, e.EntryType, e.LedgerDomain, e.LedgerBucket, e.MatterId, e.ClientId })
                .ToListAsync(ct);

            var scopedRows = tenantRows.Where(e =>
                    (string.IsNullOrWhiteSpace(entry.MatterId) || string.Equals(e.MatterId, entry.MatterId, StringComparison.Ordinal)) &&
                    (string.IsNullOrWhiteSpace(entry.ClientId) || string.Equals(e.ClientId, entry.ClientId, StringComparison.Ordinal)) &&
                    string.Equals(e.LedgerDomain ?? string.Empty, entry.LedgerDomain ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var local = BuildBehavioralBaselineStats(
                scopedRows,
                r => NormalizeMoney(Math.Abs(r.Amount)),
                r => r.PostedAt,
                r => IsLedgerReversalLike(r.EntryType));

            var tenant = BuildBehavioralBaselineStats(
                tenantRows.Where(e => string.Equals(e.LedgerDomain ?? string.Empty, entry.LedgerDomain ?? string.Empty, StringComparison.OrdinalIgnoreCase)),
                r => NormalizeMoney(Math.Abs(r.Amount)),
                r => r.PostedAt,
                r => IsLedgerReversalLike(r.EntryType));

            return ApplyBehavioralContribution(
                currentTimestampUtc: occurredAt,
                absoluteAmount: absoluteAmount,
                currentIsReversalLike: IsLedgerReversalLike(entry.EntryType),
                currentOffHours: IsOffHours(occurredAt),
                currentWeekend: IsWeekend(occurredAt),
                localBaseline: local,
                tenantBaseline: tenant,
                reasons: reasons,
                evidence: evidence,
                features: features,
                weights: weights,
                options: options,
                sourceLabel: "billing_ledger_entry");
        }

        private async Task<decimal> ApplyBehavioralSignalsForAllocationAsync(
            BillingPaymentAllocation allocation,
            decimal absoluteAmount,
            List<object> reasons,
            Dictionary<string, object?> evidence,
            Dictionary<string, object?> features,
            IReadOnlyDictionary<string, decimal> weights,
            TrustRiskPolicy policy,
            CancellationToken ct)
        {
            var options = ResolveBehavioralOptions(policy);
            if (!options.Enabled || absoluteAmount <= 0m)
            {
                features["behavioral"] = new { enabled = false, reason = "disabled_or_zero_amount" };
                return 0m;
            }

            var occurredAt = allocation.AppliedAt;
            var since = occurredAt.AddDays(-options.LookbackDays);
            var tenantRows = await _context.BillingPaymentAllocations.AsNoTracking()
                .Where(a => a.Id != allocation.Id && a.AppliedAt >= since && a.AppliedAt < occurredAt)
                .Select(a => new { a.Amount, a.AppliedAt, a.Status, a.AllocationType, a.MatterId, a.ClientId, a.PayorClientId })
                .ToListAsync(ct);

            var scopedRows = tenantRows.Where(a =>
                    (!string.IsNullOrWhiteSpace(allocation.MatterId) && string.Equals(a.MatterId, allocation.MatterId, StringComparison.Ordinal)) ||
                    (!string.IsNullOrWhiteSpace(allocation.ClientId) && string.Equals(a.ClientId, allocation.ClientId, StringComparison.Ordinal)) ||
                    (!string.IsNullOrWhiteSpace(allocation.PayorClientId) && string.Equals(a.PayorClientId, allocation.PayorClientId, StringComparison.Ordinal)))
                .ToList();

            var local = BuildBehavioralBaselineStats(
                scopedRows,
                r => NormalizeMoney(Math.Abs(r.Amount)),
                r => r.AppliedAt,
                r => IsAllocationReversalLike(r.Status, r.AllocationType));

            var tenant = BuildBehavioralBaselineStats(
                tenantRows,
                r => NormalizeMoney(Math.Abs(r.Amount)),
                r => r.AppliedAt,
                r => IsAllocationReversalLike(r.Status, r.AllocationType));

            return ApplyBehavioralContribution(
                currentTimestampUtc: occurredAt,
                absoluteAmount: absoluteAmount,
                currentIsReversalLike: IsAllocationReversalLike(allocation.Status, allocation.AllocationType),
                currentOffHours: IsOffHours(occurredAt),
                currentWeekend: IsWeekend(occurredAt),
                localBaseline: local,
                tenantBaseline: tenant,
                reasons: reasons,
                evidence: evidence,
                features: features,
                weights: weights,
                options: options,
                sourceLabel: "billing_payment_allocation");
        }

        private async Task<decimal> ApplyBehavioralSignalsForTrustTransactionAsync(
            TrustTransaction transaction,
            DateTime occurredAt,
            decimal absoluteAmount,
            List<object> reasons,
            Dictionary<string, object?> evidence,
            Dictionary<string, object?> features,
            IReadOnlyDictionary<string, decimal> weights,
            TrustRiskPolicy policy,
            CancellationToken ct)
        {
            var options = ResolveBehavioralOptions(policy);
            if (!options.Enabled || absoluteAmount <= 0m)
            {
                features["behavioral"] = new { enabled = false, reason = "disabled_or_zero_amount" };
                return 0m;
            }

            var since = occurredAt.AddDays(-options.LookbackDays);
            var tenantRows = await _context.TrustTransactions.AsNoTracking()
                .Where(t => t.Id != transaction.Id && (t.CreatedAt >= since || t.UpdatedAt >= since))
                .Select(t => new { t.Id, t.TrustAccountId, t.MatterId, t.Type, t.Amount, t.IsVoided, t.CreatedAt, t.UpdatedAt })
                .ToListAsync(ct);
            tenantRows = tenantRows
                .Where(t =>
                {
                    var ts = t.UpdatedAt == default ? t.CreatedAt : t.UpdatedAt;
                    return ts >= since && ts < occurredAt;
                })
                .ToList();

            var scopedRows = tenantRows.Where(t =>
                    (!string.IsNullOrWhiteSpace(transaction.TrustAccountId) && string.Equals(t.TrustAccountId, transaction.TrustAccountId, StringComparison.Ordinal)) ||
                    (!string.IsNullOrWhiteSpace(transaction.MatterId) && string.Equals(t.MatterId, transaction.MatterId, StringComparison.Ordinal)))
                .ToList();

            var local = BuildBehavioralBaselineStats(
                scopedRows,
                r => NormalizeMoney(Math.Abs((decimal)r.Amount)),
                r => r.UpdatedAt == default ? r.CreatedAt : r.UpdatedAt,
                r => IsTrustTransactionReversalLike(r.Type, r.IsVoided));

            var tenant = BuildBehavioralBaselineStats(
                tenantRows,
                r => NormalizeMoney(Math.Abs((decimal)r.Amount)),
                r => r.UpdatedAt == default ? r.CreatedAt : r.UpdatedAt,
                r => IsTrustTransactionReversalLike(r.Type, r.IsVoided));

            evidence["trustAccountId"] ??= transaction.TrustAccountId;

            return ApplyBehavioralContribution(
                currentTimestampUtc: occurredAt,
                absoluteAmount: absoluteAmount,
                currentIsReversalLike: IsTrustTransactionReversalLike(transaction.Type, transaction.IsVoided),
                currentOffHours: IsOffHours(occurredAt),
                currentWeekend: IsWeekend(occurredAt),
                localBaseline: local,
                tenantBaseline: tenant,
                reasons: reasons,
                evidence: evidence,
                features: features,
                weights: weights,
                options: options,
                sourceLabel: "trust_transaction");
        }

        private decimal ApplyBehavioralContribution(
            DateTime currentTimestampUtc,
            decimal absoluteAmount,
            bool currentIsReversalLike,
            bool currentOffHours,
            bool currentWeekend,
            BehavioralBaselineStats localBaseline,
            BehavioralBaselineStats tenantBaseline,
            List<object> reasons,
            Dictionary<string, object?> evidence,
            Dictionary<string, object?> features,
            IReadOnlyDictionary<string, decimal> weights,
            BehavioralPolicyOptions options,
            string sourceLabel)
        {
            var chosen = localBaseline.Count >= options.MinSamples ? localBaseline : tenantBaseline;
            var scope = localBaseline.Count >= options.MinSamples ? "local" : "tenant";

            var componentRecords = new List<Dictionary<string, object?>>();
            var candidateContribution = 0m;
            var appliedContribution = 0m;
            var appliedReasonCount = 0;

            if (chosen.Count < options.MinSamples)
            {
                features["behavioral"] = new
                {
                    enabled = true,
                    shadowMode = options.ShadowMode,
                    applied = false,
                    insufficientBaseline = true,
                    minSamples = options.MinSamples,
                    localSamples = localBaseline.Count,
                    tenantSamples = tenantBaseline.Count,
                    lookbackDays = options.LookbackDays,
                    source = sourceLabel
                };
                evidence["behavioralBaseline"] = new
                {
                    source = sourceLabel,
                    scope = scope,
                    local = localBaseline,
                    tenant = tenantBaseline
                };
                return 0m;
            }

            var ratio = chosen.AverageAbsoluteAmount <= 0m ? 0m : NormalizeMoney(absoluteAmount / chosen.AverageAbsoluteAmount);
            if (ratio >= options.AmountRatioThreshold && absoluteAmount >= 500m)
            {
                var severityFactor = ratio >= (options.AmountRatioThreshold * 2m) ? 1.4m : 1m;
                var w = NormalizeMoney(Weight(weights, "behavioralAmountOutlier", 8m) * (decimal)severityFactor);
                componentRecords.Add(new Dictionary<string, object?>
                {
                    ["code"] = "behavioral_amount_outlier",
                    ["candidateWeight"] = w,
                    ["appliedWeight"] = 0m,
                    ["ratio"] = ratio,
                    ["baselineAverage"] = chosen.AverageAbsoluteAmount,
                    ["scope"] = scope
                });
                candidateContribution += w;
            }

            if (currentOffHours)
            {
                var offHoursDelta = NormalizeMoney((decimal)(1d - chosen.OffHoursRate));
                if (offHoursDelta >= (decimal)options.TimePatternDeltaThreshold)
                {
                    var w = Weight(weights, "behavioralTimePatternOutlier", 6m);
                    componentRecords.Add(new Dictionary<string, object?>
                    {
                        ["code"] = "behavioral_time_pattern_outlier",
                        ["candidateWeight"] = w,
                        ["appliedWeight"] = 0m,
                        ["currentOffHours"] = true,
                        ["currentWeekend"] = currentWeekend,
                        ["baselineOffHoursRate"] = chosen.OffHoursRate,
                        ["baselineWeekendRate"] = chosen.WeekendRate,
                        ["scope"] = scope
                    });
                    candidateContribution += w;
                }
            }

            if (currentIsReversalLike)
            {
                var reversalDelta = NormalizeMoney((decimal)(1d - chosen.ReversalRate));
                if (reversalDelta >= (decimal)options.ReversalRateDeltaThreshold)
                {
                    var w = Weight(weights, "behavioralReversalPatternOutlier", 7m);
                    componentRecords.Add(new Dictionary<string, object?>
                    {
                        ["code"] = "behavioral_reversal_pattern_outlier",
                        ["candidateWeight"] = w,
                        ["appliedWeight"] = 0m,
                        ["baselineReversalRate"] = chosen.ReversalRate,
                        ["scope"] = scope
                    });
                    candidateContribution += w;
                }
            }

            if (candidateContribution > 0m)
            {
                var cappedCandidate = Math.Min(options.ContributionCap, NormalizeMoney(candidateContribution));
                if (!options.ShadowMode)
                {
                    var remaining = cappedCandidate;
                    foreach (var component in componentRecords)
                    {
                        var code = component["code"]?.ToString() ?? "behavioral_signal";
                        var candidateWeight = component.TryGetValue("candidateWeight", out var candidateObj)
                            ? NormalizeMoney(Convert.ToDecimal(candidateObj, CultureInfo.InvariantCulture))
                            : 0m;
                        var appliedWeight = Math.Min(candidateWeight, remaining);
                        component["appliedWeight"] = appliedWeight;
                        if (appliedWeight > 0m)
                        {
                            remaining = NormalizeMoney(remaining - appliedWeight);
                            appliedContribution += appliedWeight;
                            appliedReasonCount++;
                            if (code == "behavioral_amount_outlier")
                            {
                                AddReason(reasons, code, "Behavioral baseline flags amount as atypically high for this scope.", appliedWeight, new
                                {
                                    scope,
                                    ratio,
                                    baselineAverage = chosen.AverageAbsoluteAmount,
                                    lookbackDays = options.LookbackDays
                                });
                            }
                            else if (code == "behavioral_time_pattern_outlier")
                            {
                                AddReason(reasons, code, "Behavioral baseline flags time-of-day/week pattern as atypical.", appliedWeight, new
                                {
                                    scope,
                                    currentOffHours,
                                    currentWeekend,
                                    baselineOffHoursRate = chosen.OffHoursRate,
                                    baselineWeekendRate = chosen.WeekendRate,
                                    lookbackDays = options.LookbackDays
                                });
                            }
                            else if (code == "behavioral_reversal_pattern_outlier")
                            {
                                AddReason(reasons, code, "Behavioral baseline flags reversal-like activity as atypical for this scope.", appliedWeight, new
                                {
                                    scope,
                                    baselineReversalRate = chosen.ReversalRate,
                                    lookbackDays = options.LookbackDays
                                });
                            }
                        }
                    }
                }

                evidence["behavioralBaseline"] = new
                {
                    source = sourceLabel,
                    scope,
                    selected = chosen,
                    local = localBaseline,
                    tenant = tenantBaseline
                };
            }

            features["behavioral"] = new
            {
                enabled = true,
                shadowMode = options.ShadowMode,
                applied = !options.ShadowMode,
                lookbackDays = options.LookbackDays,
                minSamples = options.MinSamples,
                source = sourceLabel,
                baselineScope = scope,
                baselineSampleCount = chosen.Count,
                current = new
                {
                    absoluteAmount,
                    isOffHours = currentOffHours,
                    isWeekend = currentWeekend,
                    isReversalLike = currentIsReversalLike,
                    hourUtc = currentTimestampUtc.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(currentTimestampUtc, DateTimeKind.Utc).Hour : currentTimestampUtc.ToUniversalTime().Hour
                },
                candidateContribution = NormalizeMoney(Math.Min(options.ContributionCap, Math.Max(0m, candidateContribution))),
                appliedContribution = NormalizeMoney(appliedContribution),
                contributionCap = options.ContributionCap,
                components = componentRecords
            };

            if (candidateContribution <= 0m)
            {
                return 0m;
            }

            if (options.ShadowMode)
            {
                evidence["behavioralShadowMode"] = true;
                evidence["behavioralShadowCandidateContribution"] = NormalizeMoney(Math.Min(options.ContributionCap, candidateContribution));
                return 0m;
            }

            evidence["behavioralShadowMode"] = false;
            evidence["behavioralAppliedReasonCount"] = appliedReasonCount;
            return NormalizeMoney(appliedContribution);
        }

        private static BehavioralBaselineStats BuildBehavioralBaselineStats<T>(
            IEnumerable<T> rows,
            Func<T, decimal> amountSelector,
            Func<T, DateTime> timestampSelector,
            Func<T, bool> reversalSelector)
        {
            var list = rows?.ToList() ?? new List<T>();
            if (list.Count == 0)
            {
                return new BehavioralBaselineStats(0, 0m, 0d, 0d, 0d, Array.Empty<int>());
            }

            var absAmounts = list.Select(amountSelector).Select(v => NormalizeMoney(Math.Abs(v))).ToList();
            var timestamps = list.Select(timestampSelector).ToList();
            var count = list.Count;
            var avgAbs = count == 0 ? 0m : NormalizeMoney(absAmounts.Average());
            var offHours = timestamps.Count(t => IsOffHours(t));
            var weekends = timestamps.Count(t => IsWeekend(t));
            var reversals = list.Count(reversalSelector);
            var hourBuckets = new int[24];
            foreach (var ts in timestamps)
            {
                var utc = ts.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(ts, DateTimeKind.Utc) : ts.ToUniversalTime();
                hourBuckets[utc.Hour]++;
            }

            return new BehavioralBaselineStats(
                count,
                avgAbs,
                count == 0 ? 0d : Math.Round(offHours / (double)count, 4),
                count == 0 ? 0d : Math.Round(weekends / (double)count, 4),
                count == 0 ? 0d : Math.Round(reversals / (double)count, 4),
                hourBuckets);
        }

        private static bool IsLedgerReversalLike(string? entryType)
        {
            var normalized = (entryType ?? string.Empty).Trim().ToLowerInvariant();
            return normalized is "reversal" or "adjustment" or "writeoff" or "credit_memo";
        }

        private static bool IsAllocationReversalLike(string? status, string? allocationType)
        {
            if (string.Equals(status, "reversed", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            var type = (allocationType ?? string.Empty).Trim().ToLowerInvariant();
            return type.Contains("revers", StringComparison.Ordinal);
        }

        private static bool IsTrustTransactionReversalLike(string? type, bool isVoided)
        {
            if (isVoided) return true;
            var normalized = (type ?? string.Empty).Trim().ToLowerInvariant();
            return normalized.Contains("revers", StringComparison.Ordinal) ||
                   normalized.Contains("void", StringComparison.Ordinal);
        }

        private static BehavioralPolicyOptions ResolveBehavioralOptions(TrustRiskPolicy policy)
        {
            return new BehavioralPolicyOptions(
                Enabled: ResolvePolicyFlag(policy, "behavioralSignalsEnabled", true),
                ShadowMode: ResolvePolicyFlag(policy, "behavioralShadowMode", true),
                LookbackDays: Math.Clamp(ResolvePolicyInt(policy, "behavioralLookbackDays", 45), 7, 365),
                MinSamples: Math.Clamp(ResolvePolicyInt(policy, "behavioralMinSamples", 10), 3, 200),
                AmountRatioThreshold: Math.Clamp(ResolvePolicyDecimal(policy, "behavioralAmountRatioThreshold", 4m), 1.5m, 25m),
                TimePatternDeltaThreshold: Math.Clamp((double)ResolvePolicyDecimal(policy, "behavioralTimePatternDeltaThreshold", 0.35m), 0.05d, 1d),
                ReversalRateDeltaThreshold: Math.Clamp((double)ResolvePolicyDecimal(policy, "behavioralReversalRateDeltaThreshold", 0.20m), 0.05d, 1d),
                ContributionCap: Math.Clamp(ResolvePolicyDecimal(policy, "behavioralContributionCap", 18m), 1m, 40m));
        }

        private static TrustRiskBehavioralBaselineBucket BuildSourceBaselineBucket<T>(
            string scopeType,
            string scopeId,
            IEnumerable<T> rows,
            Func<T, decimal> amountSelector,
            Func<T, DateTime> timestampSelector,
            Func<T, bool> reversalSelector)
        {
            var stats = BuildBehavioralBaselineStats(rows, amountSelector, timestampSelector, reversalSelector);
            return new TrustRiskBehavioralBaselineBucket(
                scopeType,
                scopeId,
                stats.Count,
                stats.AverageAbsoluteAmount,
                stats.OffHoursRate,
                stats.WeekendRate,
                stats.ReversalRate,
                stats.HourBuckets);
        }

        private static decimal? Percentile(IReadOnlyList<decimal> sortedValues, decimal percentile)
        {
            if (sortedValues == null || sortedValues.Count == 0)
            {
                return null;
            }

            percentile = Math.Clamp(percentile, 0m, 1m);
            if (sortedValues.Count == 1)
            {
                return NormalizeMoney(sortedValues[0]);
            }

            var rank = (double)percentile * (sortedValues.Count - 1);
            var lower = (int)Math.Floor(rank);
            var upper = (int)Math.Ceiling(rank);
            if (lower == upper)
            {
                return NormalizeMoney(sortedValues[lower]);
            }

            var weight = (decimal)(rank - lower);
            var value = sortedValues[lower] + ((sortedValues[upper] - sortedValues[lower]) * weight);
            return NormalizeMoney(value);
        }

        private async Task<decimal> GetClientTrustAvailableBalanceAsync(string clientId, string? matterId, string? trustAccountId, CancellationToken ct)
        {
            var q = _context.ClientTrustLedgers.AsNoTracking()
                .Where(l => l.ClientId == clientId && l.Status == LedgerStatus.ACTIVE);
            if (!string.IsNullOrWhiteSpace(matterId))
            {
                q = q.Where(l => l.MatterId == matterId);
            }
            if (!string.IsNullOrWhiteSpace(trustAccountId))
            {
                q = q.Where(l => l.TrustAccountId == trustAccountId);
            }

            var balance = await q.SumAsync(l => (decimal?)l.AvailableToDisburse, ct) ?? 0m;
            return NormalizeMoney(balance);
        }

        private async Task<bool> IsDateInLockedPeriodAsync(DateOnly day, CancellationToken ct)
        {
            var normalizedDay = day.ToDateTime(TimeOnly.MinValue);
            return await _context.BillingLocks.AsNoTracking()
                .AnyAsync(l => l.PeriodStart <= normalizedDay && l.PeriodEnd >= normalizedDay, ct);
        }

        private static decimal ApplyRoundDollarRule(decimal absoluteAmount, List<object> reasons, IReadOnlyDictionary<string, decimal> weights)
        {
            if (absoluteAmount < 1000m)
            {
                return 0m;
            }

            var fractional = absoluteAmount - Math.Truncate(absoluteAmount);
            if (fractional != 0m && fractional != 0.50m)
            {
                return 0m;
            }

            var weight = Weight(weights, "roundDollarPattern", 8m);
            AddReason(reasons, "round_dollar_pattern", "Round-dollar (or .50) high-value amount pattern detected.", weight, new { amount = absoluteAmount });
            return weight;
        }

        private static IReadOnlyDictionary<string, decimal> ResolvePolicyWeights(TrustRiskPolicy policy)
        {
            var result = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(policy.RuleWeightsJson))
            {
                return result;
            }

            try
            {
                using var doc = JsonDocument.Parse(policy.RuleWeightsJson);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return result;
                }

                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetDecimal(out var d))
                    {
                        result[prop.Name] = NormalizeMoney(d);
                    }
                }
            }
            catch
            {
                // malformed policy weights -> fallback defaults
            }

            return result;
        }

        private static decimal Weight(IReadOnlyDictionary<string, decimal> weights, string key, decimal fallback) =>
            weights.TryGetValue(key, out var value) ? NormalizeMoney(value) : NormalizeMoney(fallback);

        private IntegrationReviewQueueItem BuildReviewQueueItem(TrustRiskEvent riskEvent, TrustRiskEvaluation evaluation, string thresholdDecision)
        {
            var summary = Truncate(string.Join(" ", evaluation.Reasons.Take(5).Select(ExtractReasonMessage)), 2048)
                          ?? "Trust risk radar flagged activity for manual review.";

            return new IntegrationReviewQueueItem
            {
                ProviderKey = ProviderKey,
                ItemType = ReviewItemType,
                SourceType = nameof(TrustRiskEvent),
                SourceId = riskEvent.Id,
                Status = IntegrationReviewQueueStatuses.Pending,
                Priority = riskEvent.Severity is "critical" or "high" ? "high" : "medium",
                Title = $"Trust Risk Review ({riskEvent.Severity.ToUpperInvariant()})",
                Summary = summary,
                ContextJson = Serialize(new
                {
                    riskEventId = riskEvent.Id,
                    riskEvent.SourceType,
                    riskEvent.SourceId,
                    riskEvent.TriggerType,
                    riskEvent.RiskScore,
                    riskEvent.Severity,
                    thresholdDecision,
                    riskEvent.InvoiceId,
                    riskEvent.MatterId,
                    riskEvent.ClientId,
                    riskEvent.PayorClientId,
                    riskEvent.BillingLedgerEntryId,
                    riskEvent.BillingPaymentAllocationId,
                    riskEvent.TrustTransactionId,
                    reasons = evaluation.Reasons,
                    evidence = evaluation.Evidence,
                    features = evaluation.Features
                }),
                SuggestedActionsJson = Serialize(new object[]
                {
                    new { action = "review_trust_risk_event", trustRiskEventId = riskEvent.Id },
                    new { action = "inspect_billing_ledger_entry", billingLedgerEntryId = riskEvent.BillingLedgerEntryId },
                    new { action = "inspect_payment_allocation", billingPaymentAllocationId = riskEvent.BillingPaymentAllocationId },
                    new { action = "inspect_trust_transaction", trustTransactionId = riskEvent.TrustTransactionId }
                }),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
        }

        private static string ResolveThresholdDecision(decimal score, TrustRiskPolicy policy) =>
            ResolveThresholdDecision(score, new EffectiveTrustRiskThresholds(policy.WarnThreshold, policy.ReviewThreshold, policy.SoftHoldThreshold, policy.HardHoldThreshold));

        private static string ResolveThresholdDecision(decimal score, EffectiveTrustRiskThresholds thresholds)
        {
            if (score >= thresholds.Hard) return "hard_hold";
            if (score >= thresholds.SoftHold) return "soft_hold";
            if (score >= thresholds.Review) return "review_required";
            if (score >= thresholds.Warn) return "warn";
            return "record";
        }

        private static TrustRiskEvaluation ApplyEnabledRuleFilter(TrustRiskPolicy policy, TrustRiskEvaluation evaluation)
        {
            var enabled = ResolveEnabledRuleKeys(policy);
            if (enabled.Count == 0)
            {
                return evaluation;
            }

            var filteredReasons = new List<object>(evaluation.Reasons.Count);
            var disabledCodes = new List<string>();
            var recomputedScore = 0m;

            foreach (var reason in evaluation.Reasons)
            {
                if (!TryExtractReasonCodeAndWeight(reason, out var code, out var weight))
                {
                    filteredReasons.Add(reason);
                    continue;
                }

                var ruleKey = MapReasonCodeToPolicyRuleKey(code);
                if (enabled.Contains(ruleKey) || enabled.Contains(code))
                {
                    filteredReasons.Add(reason);
                    recomputedScore += weight ?? 0m;
                }
                else
                {
                    disabledCodes.Add(code);
                }
            }

            if (disabledCodes.Count == 0)
            {
                return evaluation;
            }

            var features = new Dictionary<string, object?>(evaluation.Features, StringComparer.OrdinalIgnoreCase)
            {
                ["enabledRuleFilterApplied"] = true,
                ["disabledRuleReasonCodes"] = disabledCodes,
                ["enabledRuleCount"] = enabled.Count
            };

            var evidence = new Dictionary<string, object?>(evaluation.Evidence, StringComparer.OrdinalIgnoreCase)
            {
                ["disabledRuleReasonCodes"] = disabledCodes
            };

            var score = ClampScore(recomputedScore);
            return new TrustRiskEvaluation(score, MapSeverity(score), filteredReasons, evidence, features);
        }

        private static bool TryExtractReasonCodeAndWeight(object reason, out string code, out decimal? weight)
        {
            code = string.Empty;
            weight = null;

            try
            {
                using var doc = JsonDocument.Parse(JsonSerializer.Serialize(reason, JsonOptions));
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }

                if (doc.RootElement.TryGetProperty("code", out var codeProp) && codeProp.ValueKind == JsonValueKind.String)
                {
                    code = codeProp.GetString() ?? string.Empty;
                }

                if (doc.RootElement.TryGetProperty("weight", out var weightProp) &&
                    weightProp.ValueKind == JsonValueKind.Number &&
                    weightProp.TryGetDecimal(out var d))
                {
                    weight = NormalizeMoney(d);
                }

                return !string.IsNullOrWhiteSpace(code);
            }
            catch
            {
                return false;
            }
        }

        private static string MapReasonCodeToPolicyRuleKey(string code)
        {
            var normalized = (code ?? string.Empty).Trim();
            return normalized switch
            {
                "trust_domain_activity" => "trust_domain_activity",
                "reversal_activity" => "reversal_activity",
                "off_hours_activity" => "off_hours_activity",
                "high_amount_threshold" => "high_amount_threshold",
                "medium_amount_threshold" => "high_amount_threshold",
                "missing_linkage" => "missing_supporting_link",
                "missing_correlation_key" => "missing_supporting_link",
                "missing_trust_transaction_link" => "missing_supporting_link",
                "missing_ledger_link" => "missing_supporting_link",
                "missing_allocation_ledger_entries" => "missing_supporting_link",
                "missing_payor_target" => "missing_payor_target",
                "missing_trust_source_client" => "missing_trust_source_client",
                "trust_funded_allocation" => "trust_funded_allocation",
                "client_trust_subledger_negative" => "client_trust_subledger_negative",
                "disbursement_exceeds_available_trust_balance" => "disbursement_exceeds_available_trust_balance",
                "reversal_adjustment_burst" => "reversal_adjustment_burst",
                "allocation_reversal_burst" => "reversal_adjustment_burst",
                "round_dollar_pattern" => "round_dollar_pattern",
                "matter_client_spike" => "matter_client_spike",
                "behavioral_amount_outlier" => "behavioral_amount_outlier",
                "behavioral_time_pattern_outlier" => "behavioral_time_pattern_outlier",
                "behavioral_reversal_pattern_outlier" => "behavioral_reversal_pattern_outlier",
                "trust_operating_mapping_mismatch" => "trust_operating_mapping_mismatch",
                "allocation_payor_mapping_mismatch" => "trust_operating_mapping_mismatch",
                "closed_or_inactive_matter_trust_activity" => "closed_or_inactive_matter_trust_activity",
                "period_lock_interaction" => "period_lock_interaction",
                "period_lock_attempt" => "period_lock_attempt",
                _ => normalized
            };
        }

        private static string ResolvePolicyDecision(string thresholdDecision, TrustRiskPolicy policy) =>
            ResolvePolicyDecision(thresholdDecision, policy, trustAccountId: null);

        private static string ResolvePolicyDecision(string thresholdDecision, TrustRiskPolicy policy, string? trustAccountId)
        {
            var actionMap = ResolvePolicyActionMap(policy, trustAccountId);
            return thresholdDecision switch
            {
                "warn" => ResolveDecisionFromAction(thresholdDecision, actionMap.TryGetValue("warn", out var warnAction) ? warnAction : null, "warn"),
                "review_required" => ResolveDecisionFromAction(thresholdDecision, actionMap.TryGetValue("review", out var reviewAction) ? reviewAction : null, "review_required"),
                "soft_hold" => ResolveDecisionFromAction(thresholdDecision, actionMap.TryGetValue("softHold", out var softAction) ? softAction : null, "soft_hold"),
                "hard_hold" => ResolveDecisionFromAction(thresholdDecision, actionMap.TryGetValue("hardHold", out var hardAction) ? hardAction : null, "hard_hold"),
                _ => "record"
            };
        }

        private static string ResolveDecisionFromAction(string thresholdDecision, string? action, string fallback)
        {
            var normalized = (action ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return fallback;
            }

            if (normalized.Contains("review_only", StringComparison.Ordinal) || normalized.Contains("create_review", StringComparison.Ordinal))
            {
                return thresholdDecision is "warn" ? "warn" : "review_required";
            }

            if (normalized.Contains("hard_hold", StringComparison.Ordinal))
            {
                return "hard_hold";
            }

            if (normalized.Contains("soft_hold", StringComparison.Ordinal))
            {
                return "soft_hold";
            }

            if (normalized.Contains("warn", StringComparison.Ordinal))
            {
                return "warn";
            }

            if (normalized.Contains("record", StringComparison.Ordinal))
            {
                return "record";
            }

            return fallback;
        }

        private static string ResolveRiskEventStatus(string decision) => decision switch
        {
            "hard_hold" or "soft_hold" => "hold_placed",
            "review_required" => "review_queued",
            "warn" => "warned",
            _ => "recorded"
        };

        private static bool RequiresReviewQueue(string decision) =>
            decision is "review_required" or "soft_hold" or "hard_hold";

        private static (string TargetType, string TargetId) ResolveHoldTarget(
            string sourceType,
            string sourceId,
            string? billingLedgerEntryId,
            string? billingPaymentAllocationId,
            string? trustTransactionId,
            string? paymentTransactionId,
            string? invoiceId,
            string? matterId,
            string? clientId,
            string? payorClientId)
        {
            if (!string.IsNullOrWhiteSpace(billingPaymentAllocationId)) return ("billing_payment_allocation", billingPaymentAllocationId);
            if (!string.IsNullOrWhiteSpace(billingLedgerEntryId)) return ("billing_ledger_entry", billingLedgerEntryId);
            if (!string.IsNullOrWhiteSpace(trustTransactionId)) return ("trust_transaction", trustTransactionId);
            if (!string.IsNullOrWhiteSpace(invoiceId)) return ("invoice", invoiceId);
            if (!string.IsNullOrWhiteSpace(paymentTransactionId)) return ("payment_transaction", paymentTransactionId);
            if (!string.IsNullOrWhiteSpace(matterId)) return ("matter", matterId);
            if (!string.IsNullOrWhiteSpace(clientId)) return ("client", clientId);
            if (!string.IsNullOrWhiteSpace(payorClientId)) return ("payor_client", payorClientId);
            return (string.IsNullOrWhiteSpace(sourceType) ? "unknown" : sourceType, sourceId);
        }

        private static string BuildActionReason(TrustRiskEvaluation evaluation, string decision)
        {
            var topReasons = evaluation.Reasons
                .Take(3)
                .Select(ExtractReasonMessage)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToArray();

            var prefix = decision switch
            {
                "hard_hold" => "Hard hold placed.",
                "soft_hold" => "Soft hold placed.",
                "review_required" => "Review required.",
                "warn" => "Warning generated.",
                _ => "Risk event recorded."
            };

            if (topReasons.Length == 0)
            {
                return prefix;
            }

            return Truncate($"{prefix} {string.Join(" ", topReasons)}", 2048) ?? prefix;
        }

        private async Task SetReviewQueueStatusForEventAsync(string trustRiskEventId, string status, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(trustRiskEventId))
            {
                return;
            }

            var eventRow = await _context.TrustRiskEvents.FirstOrDefaultAsync(e => e.Id == trustRiskEventId, ct);
            if (eventRow == null || string.IsNullOrWhiteSpace(eventRow.ReviewQueueItemId))
            {
                return;
            }

            var reviewItem = await _context.IntegrationReviewQueueItems
                .FirstOrDefaultAsync(r => r.Id == eventRow.ReviewQueueItemId, ct);
            if (reviewItem == null)
            {
                return;
            }

            reviewItem.Status = status;
            reviewItem.UpdatedAt = DateTime.UtcNow;
            eventRow.Status = status switch
            {
                IntegrationReviewQueueStatuses.Resolved => "closed",
                IntegrationReviewQueueStatuses.InReview => "under_review",
                _ => eventRow.Status
            };
            eventRow.UpdatedAt = DateTime.UtcNow;

            var link = await _context.TrustRiskReviewLinks
                .OrderByDescending(l => l.UpdatedAt)
                .FirstOrDefaultAsync(l => l.TrustRiskEventId == trustRiskEventId && l.ReviewQueueItemId == reviewItem.Id, ct);
            if (link != null)
            {
                link.Status = status switch
                {
                    IntegrationReviewQueueStatuses.Resolved => "resolved",
                    _ => "active"
                };
                link.UpdatedAt = DateTime.UtcNow;
            }
        }

        private async Task<string?> TryGetReviewQueueItemIdForEventAsync(string trustRiskEventId, CancellationToken ct)
        {
            return await _context.TrustRiskEvents.AsNoTracking()
                .Where(e => e.Id == trustRiskEventId)
                .Select(e => e.ReviewQueueItemId)
                .FirstOrDefaultAsync(ct);
        }

        private Task AppendHoldActionAsync(TrustRiskHold hold, string actionType, string status, string? actorUserId, string actorType, string? notes, CancellationToken ct)
        {
            _context.TrustRiskActions.Add(new TrustRiskAction
            {
                TrustRiskEventId = hold.TrustRiskEventId,
                ActionType = actionType,
                Status = status,
                ActorUserId = Truncate(actorUserId, 128),
                ActorType = Truncate(actorType, 64),
                CorrelationId = Truncate(GetHttpContextItem(AuditTraceContextKeys.CorrelationId), 128),
                Notes = Truncate(notes, 2048),
                MetadataJson = Serialize(new
                {
                    holdId = hold.Id,
                    holdType = hold.HoldType,
                    holdStatus = hold.Status,
                    hold.TargetType,
                    hold.TargetId,
                    phase = "trust-risk-radar.phase2"
                }),
                CreatedAt = DateTime.UtcNow
            });
            return Task.CompletedTask;
        }

        private async Task TryQueueOpsAlertOutboxEventAsync(
            TrustRiskPolicy policy,
            string? eventType,
            string riskEventId,
            string? decision,
            string? holdId,
            string? reviewQueueItemId,
            object payload,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(eventType) || !ResolveOpsAlertsEnabled(policy))
            {
                return;
            }

            var channels = ResolveOpsAlertChannels(policy)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => c.Trim().ToLowerInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (channels.Length == 0)
            {
                channels = new[] { "outbox" };
            }

            // in_app routing is satisfied by review queue + radar panel; no outbox row needed.
            foreach (var channel in channels.Where(c => c is "outbox" or "webhook" or "email"))
            {
                var idempotencyKey = $"trust-risk:{riskEventId}:{eventType}:{channel}";
                var exists = await _context.IntegrationOutboxEvents.AsNoTracking()
                    .AnyAsync(e => e.ProviderKey == ProviderKey && e.IdempotencyKey == idempotencyKey, ct);
                if (exists)
                {
                    continue;
                }

                _context.IntegrationOutboxEvents.Add(new IntegrationOutboxEvent
                {
                    ProviderKey = ProviderKey,
                    EventType = eventType,
                    EntityType = nameof(TrustRiskEvent),
                    EntityId = riskEventId,
                    IdempotencyKey = idempotencyKey,
                    CorrelationId = Truncate(GetHttpContextItem(AuditTraceContextKeys.CorrelationId), 128),
                    Status = "pending",
                    PayloadJson = Serialize(payload),
                    MetadataJson = Serialize(new
                    {
                        phase = "trust-risk-radar.phaseX1",
                        decision,
                        holdId,
                        reviewQueueItemId,
                        channel,
                        configuredChannels = channels
                    }),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            }
        }

        public async Task<int> ApplyHoldSlaTransitionsAsync(CancellationToken ct = default)
        {
            var policy = await GetOrCreateActivePolicyAsync(ct);
            var defaultEscalationMinutes = ResolvePolicyInt(policy, "holdEscalationSlaMinutes", 120);
            var defaultSoftExpiryHours = ResolvePolicyInt(policy, "softHoldExpiryHours", 24);
            var defaultHardExpiryHours = ResolvePolicyInt(policy, "hardHoldExpiryHours", 72);
            if (defaultEscalationMinutes <= 0 && defaultSoftExpiryHours <= 0 && defaultHardExpiryHours <= 0)
            {
                return 0;
            }

            var now = DateTime.UtcNow;
            var activeStatuses = new[] { "placed", "under_review", "escalated" };
            var holds = await _context.TrustRiskHolds
                .Where(h => activeStatuses.Contains(h.Status))
                .OrderBy(h => h.PlacedAt)
                .Take(200)
                .ToListAsync(ct);

            var changed = 0;
            foreach (var hold in holds)
            {
                var holdTrustAccountId = TryParseJsonObject(hold.MetadataJson).TryGetValue("trustAccountId", out var ta) ? ta : null;
                var escalationMinutes = ResolvePolicyInt(policy, "holdEscalationSlaMinutes", defaultEscalationMinutes, holdTrustAccountId);
                var softExpiryHours = ResolvePolicyInt(policy, "softHoldExpiryHours", defaultSoftExpiryHours, holdTrustAccountId);
                var hardExpiryHours = ResolvePolicyInt(policy, "hardHoldExpiryHours", defaultHardExpiryHours, holdTrustAccountId);
                var age = now - (hold.PlacedAt == default ? hold.CreatedAt : hold.PlacedAt);
                var expiryHours = string.Equals(hold.HoldType, "hard", StringComparison.OrdinalIgnoreCase) ? hardExpiryHours : softExpiryHours;
                if (expiryHours > 0 && age >= TimeSpan.FromHours(expiryHours))
                {
                    if (!string.Equals(hold.Status, "expired", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(hold.Status, "released", StringComparison.OrdinalIgnoreCase))
                    {
                        hold.Status = "expired";
                        hold.UpdatedAt = now;
                        hold.MetadataJson = MergeJsonMetadata(hold.MetadataJson, new Dictionary<string, object?>
                        {
                            ["lastAction"] = "expired",
                            ["slaExpiredAtUtc"] = now,
                            ["slaExpiryHours"] = expiryHours,
                            ["slaTransition"] = true
                        });
                        await AppendHoldActionAsync(hold, "expire", "completed", "system", "system", $"SLA expiry reached ({expiryHours}h).", ct);
                        await SetReviewQueueStatusForEventAsync(hold.TrustRiskEventId, IntegrationReviewQueueStatuses.InReview, ct);
                        changed++;
                    }
                    continue;
                }

                if (escalationMinutes > 0 &&
                    string.Equals(hold.Status, "placed", StringComparison.OrdinalIgnoreCase) &&
                    age >= TimeSpan.FromMinutes(escalationMinutes))
                {
                    hold.Status = "escalated";
                    hold.UpdatedAt = now;
                    hold.MetadataJson = MergeJsonMetadata(hold.MetadataJson, new Dictionary<string, object?>
                    {
                        ["lastAction"] = "escalated",
                        ["slaAutoEscalatedAtUtc"] = now,
                        ["slaEscalationMinutes"] = escalationMinutes,
                        ["slaTransition"] = true
                    });
                    await AppendHoldActionAsync(hold, "escalate_auto_sla", "completed", "system", "system", $"SLA escalation reached ({escalationMinutes}m).", ct);
                    await SetReviewQueueStatusForEventAsync(hold.TrustRiskEventId, IntegrationReviewQueueStatuses.InReview, ct);
                    changed++;
                }
            }

            if (changed > 0)
            {
                await _context.SaveChangesAsync(ct);
            }

            return changed;
        }

        private static bool ResolveOpsAlertsEnabled(TrustRiskPolicy policy) =>
            ResolvePolicyFlag(policy, "opsAlertsEnabled", false);

        private static string[] ResolveOpsAlertChannels(TrustRiskPolicy policy)
        {
            var values = ResolvePolicyStringArray(policy, "opsAlertChannels");
            return values.Count == 0 ? new[] { "outbox" } : values.ToArray();
        }

        private static string ResolveOpsAlertEventType(string decision) => decision switch
        {
            "hard_hold" => "trust_risk.hold.placed",
            "soft_hold" => "trust_risk.hold.placed",
            "review_required" => "trust_risk.review_required",
            "warn" => "trust_risk.warned",
            _ => "trust_risk.recorded"
        };

        private static TrustRiskPreflightOptions ResolvePreflightOptions(TrustRiskPolicy policy) =>
            ResolvePreflightOptions(policy, trustAccountId: null);

        private static TrustRiskPreflightOptions ResolvePreflightOptions(TrustRiskPolicy policy, string? trustAccountId)
        {
            var rolloutRaw = ResolvePolicyString(policy, "preflightStrictRolloutMode", "warn", trustAccountId);
            var rollout = NormalizePreflightRolloutMode(rolloutRaw);
            return new TrustRiskPreflightOptions(
                Enabled: ResolvePolicyFlag(policy, "preflightStrictModeEnabled", false, trustAccountId),
                RolloutMode: rollout,
                HighConfidenceOnly: ResolvePolicyFlag(policy, "preflightHighConfidenceOnly", true, trustAccountId),
                MinSeverity: NormalizeSeverityLabel(ResolvePolicyString(policy, "preflightMinSeverity", "critical", trustAccountId)),
                RecentEventWindowMinutes: Math.Clamp(ResolvePolicyInt(policy, "preflightRecentEventWindowMinutes", 30, trustAccountId), 1, 1440),
                DuplicateSuppressionEnabled: ResolvePolicyFlag(policy, "preflightDuplicateSuppressionEnabled", true, trustAccountId));
        }

        private static string ApplyPreflightGraceDecision(string decision, TrustRiskPreflightOptions options)
        {
            if (!options.Enabled)
            {
                return decision;
            }

            if (!string.Equals(decision, "soft_hold", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(decision, "hard_hold", StringComparison.OrdinalIgnoreCase))
            {
                return decision;
            }

            return options.RolloutMode switch
            {
                "warn" => "warn",
                "soft_hold" when string.Equals(decision, "hard_hold", StringComparison.OrdinalIgnoreCase) => "soft_hold",
                _ => decision
            };
        }

        private static string NormalizePreflightRolloutMode(string? value)
        {
            var normalized = (value ?? "warn").Trim().ToLowerInvariant();
            return normalized is "warn" or "soft_hold" or "strict" ? normalized : "warn";
        }

        private static string NormalizeSeverityLabel(string? value)
        {
            var normalized = (value ?? "critical").Trim().ToLowerInvariant();
            return normalized is "low" or "medium" or "high" or "critical" ? normalized : "critical";
        }

        private static bool SeverityAtLeast(string? severity, string minSeverity)
        {
            return SeverityRank(severity) >= SeverityRank(minSeverity);
        }

        private static int SeverityRank(string? severity)
        {
            var normalized = NormalizeSeverityLabel(severity);
            return normalized switch
            {
                "critical" => 4,
                "high" => 3,
                "medium" => 2,
                "low" => 1,
                _ => 0
            };
        }

        private static int DecisionRank(string? decision)
        {
            var normalized = (decision ?? string.Empty).Trim().ToLowerInvariant();
            return normalized switch
            {
                "hard_hold" => 4,
                "soft_hold" => 3,
                "review_required" => 2,
                "warn" => 1,
                _ => 0
            };
        }

        private static bool EventMatchesGuardCandidates(dynamic e, IReadOnlyCollection<TrustRiskHoldTargetCandidate> candidates)
        {
            bool Match(string targetType, string? targetId) =>
                !string.IsNullOrWhiteSpace(targetId) &&
                candidates.Any(c => string.Equals(c.TargetType, targetType, StringComparison.OrdinalIgnoreCase) &&
                                    string.Equals(c.TargetId, targetId, StringComparison.Ordinal));

            return Match("billing_ledger_entry", e.BillingLedgerEntryId) ||
                   Match("billing_payment_allocation", e.BillingPaymentAllocationId) ||
                   Match("trust_transaction", e.TrustTransactionId) ||
                   Match("payment_transaction", e.PaymentTransactionId) ||
                   Match("invoice", e.InvoiceId) ||
                   Match("matter", e.MatterId) ||
                   Match("client", e.ClientId) ||
                   Match("payor_client", e.PayorClientId) ||
                   (string.Equals(e.SourceType as string, "billing_ledger_entry", StringComparison.OrdinalIgnoreCase) && Match("billing_ledger_entry", e.SourceId)) ||
                   (string.Equals(e.SourceType as string, "billing_payment_allocation", StringComparison.OrdinalIgnoreCase) && Match("billing_payment_allocation", e.SourceId)) ||
                   (string.Equals(e.SourceType as string, "trust_transaction", StringComparison.OrdinalIgnoreCase) && Match("trust_transaction", e.SourceId));
        }

        private static bool IsHighConfidenceEvent(string? riskReasonsJson, string? featuresJson)
        {
            var highConfidenceReasonCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "client_trust_subledger_negative",
                "disbursement_exceeds_available_trust_balance",
                "trust_operating_mapping_mismatch",
                "period_lock_attempt",
                "period_lock_interaction"
            };

            try
            {
                if (!string.IsNullOrWhiteSpace(featuresJson))
                {
                    using var fdoc = JsonDocument.Parse(featuresJson);
                    if (fdoc.RootElement.ValueKind == JsonValueKind.Object &&
                        fdoc.RootElement.TryGetProperty("behavioral", out var behavioral) &&
                        behavioral.ValueKind == JsonValueKind.Object &&
                        behavioral.TryGetProperty("shadowMode", out var shadowProp) &&
                        shadowProp.ValueKind == JsonValueKind.True)
                    {
                        // Shadow-mode-only behavioral signal shouldn't satisfy high-confidence by itself.
                    }
                }
            }
            catch
            {
                // ignore malformed features
            }

            if (string.IsNullOrWhiteSpace(riskReasonsJson))
            {
                return false;
            }

            try
            {
                using var doc = JsonDocument.Parse(riskReasonsJson);
                if (doc.RootElement.ValueKind != JsonValueKind.Array) return false;
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object) continue;
                    if (!item.TryGetProperty("code", out var codeProp) || codeProp.ValueKind != JsonValueKind.String) continue;
                    var code = codeProp.GetString();
                    if (!string.IsNullOrWhiteSpace(code) && highConfidenceReasonCodes.Contains(code))
                    {
                        return true;
                    }
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        private static string ResolveOperationFailMode(TrustRiskPolicy policy, string? operationType)
        {
            if (!string.IsNullOrWhiteSpace(operationType))
            {
                var opMode = ResolvePolicyObjectString(policy, "operationFailModes", operationType.Trim());
                if (!string.IsNullOrWhiteSpace(opMode))
                {
                    return NormalizeFailMode(opMode);
                }
            }

            return NormalizeFailMode(policy.FailMode);
        }

        private static bool ResolvePolicyFlag(TrustRiskPolicy policy, string key, bool fallback) =>
            ResolvePolicyFlag(policy, key, fallback, trustAccountId: null);

        private static bool ResolvePolicyFlag(TrustRiskPolicy policy, string key, bool fallback, string? trustAccountId)
        {
            if (!string.IsNullOrWhiteSpace(trustAccountId))
            {
                var overrideValue = ResolveTrustAccountOverrideProperty(policy, trustAccountId, key);
                if (overrideValue.HasValue)
                {
                    return overrideValue.Value.ValueKind switch
                    {
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        JsonValueKind.String when bool.TryParse(overrideValue.Value.GetString(), out var b) => b,
                        _ => fallback
                    };
                }
            }

            if (string.IsNullOrWhiteSpace(policy.MetadataJson))
            {
                return fallback;
            }

            try
            {
                using var doc = JsonDocument.Parse(policy.MetadataJson);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return fallback;
                }

                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (!string.Equals(prop.Name, key, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    return prop.Value.ValueKind switch
                    {
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        JsonValueKind.String when bool.TryParse(prop.Value.GetString(), out var b) => b,
                        _ => fallback
                    };
                }
            }
            catch
            {
                return fallback;
            }

            return fallback;
        }

        private static int ResolvePolicyInt(TrustRiskPolicy policy, string key, int fallback) =>
            ResolvePolicyInt(policy, key, fallback, trustAccountId: null);

        private static int ResolvePolicyInt(TrustRiskPolicy policy, string key, int fallback, string? trustAccountId)
        {
            if (!string.IsNullOrWhiteSpace(trustAccountId))
            {
                var overrideValue = ResolveTrustAccountOverrideProperty(policy, trustAccountId, key);
                if (overrideValue.HasValue)
                {
                    if (overrideValue.Value.ValueKind == JsonValueKind.Number && overrideValue.Value.TryGetInt32(out var i))
                    {
                        return i;
                    }
                    if (overrideValue.Value.ValueKind == JsonValueKind.String &&
                        int.TryParse(overrideValue.Value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                    {
                        return parsed;
                    }
                    return fallback;
                }
            }

            if (string.IsNullOrWhiteSpace(policy.MetadataJson))
            {
                return fallback;
            }

            try
            {
                using var doc = JsonDocument.Parse(policy.MetadataJson);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return fallback;
                }

                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (!string.Equals(prop.Name, key, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetInt32(out var i))
                    {
                        return i;
                    }

                    if (prop.Value.ValueKind == JsonValueKind.String &&
                        int.TryParse(prop.Value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                    {
                        return parsed;
                    }

                    return fallback;
                }
            }
            catch
            {
                return fallback;
            }

            return fallback;
        }

        private static decimal ResolvePolicyDecimal(TrustRiskPolicy policy, string key, decimal fallback) =>
            ResolvePolicyDecimal(policy, key, fallback, trustAccountId: null);

        private static decimal ResolvePolicyDecimal(TrustRiskPolicy policy, string key, decimal fallback, string? trustAccountId)
        {
            if (!string.IsNullOrWhiteSpace(trustAccountId))
            {
                var overrideValue = ResolveTrustAccountOverrideProperty(policy, trustAccountId, key);
                if (overrideValue.HasValue)
                {
                    if (overrideValue.Value.ValueKind == JsonValueKind.Number && overrideValue.Value.TryGetDecimal(out var d))
                    {
                        return NormalizeMoney(d);
                    }
                    if (overrideValue.Value.ValueKind == JsonValueKind.String &&
                        decimal.TryParse(overrideValue.Value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
                    {
                        return NormalizeMoney(parsed);
                    }
                    return fallback;
                }
            }

            if (string.IsNullOrWhiteSpace(policy.MetadataJson))
            {
                return fallback;
            }

            try
            {
                using var doc = JsonDocument.Parse(policy.MetadataJson);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return fallback;
                }

                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (!string.Equals(prop.Name, key, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetDecimal(out var d))
                    {
                        return NormalizeMoney(d);
                    }

                    if (prop.Value.ValueKind == JsonValueKind.String &&
                        decimal.TryParse(prop.Value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
                    {
                        return NormalizeMoney(parsed);
                    }

                    return fallback;
                }
            }
            catch
            {
                return fallback;
            }

            return fallback;
        }

        private static HashSet<string> ResolvePolicyRoleSet(TrustRiskPolicy policy, string metadataKey, params string[] fallback) =>
            ResolvePolicyRoleSet(policy, metadataKey, trustAccountId: null, fallback);

        private static HashSet<string> ResolvePolicyRoleSet(TrustRiskPolicy policy, string metadataKey, string? trustAccountId, params string[] fallback)
        {
            var values = ResolvePolicyStringArray(policy, metadataKey, trustAccountId);
            if (values.Count == 0)
            {
                values = new HashSet<string>(
                    fallback.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()),
                    StringComparer.OrdinalIgnoreCase);
            }

            return new HashSet<string>(values, StringComparer.OrdinalIgnoreCase);
        }

        private static HashSet<string> ResolvePolicyStringArray(TrustRiskPolicy policy, string metadataKey) =>
            ResolvePolicyStringArray(policy, metadataKey, trustAccountId: null);

        private static HashSet<string> ResolvePolicyStringArray(TrustRiskPolicy policy, string metadataKey, string? trustAccountId)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(trustAccountId))
            {
                var overrideValue = ResolveTrustAccountOverrideProperty(policy, trustAccountId, metadataKey);
                if (overrideValue.HasValue)
                {
                    if (overrideValue.Value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in overrideValue.Value.EnumerateArray())
                        {
                            if (item.ValueKind == JsonValueKind.String)
                            {
                                var value = item.GetString();
                                if (!string.IsNullOrWhiteSpace(value))
                                {
                                    result.Add(value.Trim());
                                }
                            }
                        }
                    }
                    else if (overrideValue.Value.ValueKind == JsonValueKind.String)
                    {
                        var raw = overrideValue.Value.GetString();
                        if (!string.IsNullOrWhiteSpace(raw))
                        {
                            foreach (var token in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                            {
                                result.Add(token);
                            }
                        }
                    }
                    return result;
                }
            }

            if (string.IsNullOrWhiteSpace(policy.MetadataJson))
            {
                return result;
            }

            try
            {
                using var doc = JsonDocument.Parse(policy.MetadataJson);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return result;
                }

                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (!string.Equals(prop.Name, metadataKey, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (prop.Value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in prop.Value.EnumerateArray())
                        {
                            if (item.ValueKind == JsonValueKind.String)
                            {
                                var value = item.GetString();
                                if (!string.IsNullOrWhiteSpace(value))
                                {
                                    result.Add(value.Trim());
                                }
                            }
                        }
                    }
                    else if (prop.Value.ValueKind == JsonValueKind.String)
                    {
                        var raw = prop.Value.GetString();
                        if (!string.IsNullOrWhiteSpace(raw))
                        {
                            foreach (var token in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                            {
                                result.Add(token);
                            }
                        }
                    }
                    break;
                }
            }
            catch
            {
                // malformed metadata -> fallback
            }

            return result;
        }

        private static void EnsureOverrideRoleAuthorized(TrustRiskPolicy policy, string? userRole, string metadataKey) =>
            EnsureOverrideRoleAuthorized(policy, userRole, metadataKey, trustAccountId: null);

        private static void EnsureOverrideRoleAuthorized(TrustRiskPolicy policy, string? userRole, string metadataKey, string? trustAccountId)
        {
            var fallback = string.Equals(metadataKey, "releaseRoles", StringComparison.OrdinalIgnoreCase)
                ? new[] { "FinanceAdmin", "Admin" }
                : new[] { "SecurityAdmin", "Admin" };
            var allowed = ResolvePolicyRoleSet(policy, metadataKey, trustAccountId, fallback);
            if (string.IsNullOrWhiteSpace(userRole) || !allowed.Contains(userRole))
            {
                throw new UnauthorizedAccessException($"Role '{userRole ?? "unknown"}' is not authorized for trust risk hold override action.");
            }
        }

        private static Dictionary<string, string> ResolvePolicyActionMap(TrustRiskPolicy policy) =>
            ResolvePolicyActionMap(policy, trustAccountId: null);

        private static Dictionary<string, string> ResolvePolicyActionMap(TrustRiskPolicy policy, string? trustAccountId)
        {
            if (!string.IsNullOrWhiteSpace(trustAccountId))
            {
                var overrideActionMap = ResolveTrustAccountOverrideActionMap(policy, trustAccountId);
                if (overrideActionMap != null && overrideActionMap.Count > 0)
                {
                    return overrideActionMap;
                }
            }

            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(policy.ActionMapJson))
            {
                return DefaultPhase2ActionMap();
            }

            try
            {
                using var doc = JsonDocument.Parse(policy.ActionMapJson);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return DefaultPhase2ActionMap();
                }

                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.Value.ValueKind != JsonValueKind.String) continue;
                    result[prop.Name] = prop.Value.GetString() ?? string.Empty;
                }
            }
            catch
            {
                return DefaultPhase2ActionMap();
            }

            if (result.Count == 0)
            {
                return DefaultPhase2ActionMap();
            }

            return result;
        }

        private static string? ResolvePolicyString(TrustRiskPolicy policy, string key, string? fallback)
            => ResolvePolicyString(policy, key, fallback, trustAccountId: null);

        private static string? ResolvePolicyString(TrustRiskPolicy policy, string key, string? fallback, string? trustAccountId)
        {
            if (!string.IsNullOrWhiteSpace(trustAccountId))
            {
                var overrideValue = ResolveTrustAccountOverrideProperty(policy, trustAccountId, key);
                if (overrideValue.HasValue)
                {
                    return overrideValue.Value.ValueKind switch
                    {
                        JsonValueKind.String => overrideValue.Value.GetString(),
                        JsonValueKind.Number => overrideValue.Value.GetRawText(),
                        JsonValueKind.True => "true",
                        JsonValueKind.False => "false",
                        _ => fallback
                    };
                }
            }

            if (string.IsNullOrWhiteSpace(policy.MetadataJson))
            {
                return fallback;
            }

            try
            {
                using var doc = JsonDocument.Parse(policy.MetadataJson);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return fallback;
                }

                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (!string.Equals(prop.Name, key, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    return prop.Value.ValueKind switch
                    {
                        JsonValueKind.String => prop.Value.GetString(),
                        JsonValueKind.Number => prop.Value.GetRawText(),
                        JsonValueKind.True => "true",
                        JsonValueKind.False => "false",
                        _ => fallback
                    };
                }
            }
            catch
            {
                return fallback;
            }

            return fallback;
        }

        private static string? ResolvePolicyObjectString(TrustRiskPolicy policy, string objectKey, string childKey)
        {
            if (string.IsNullOrWhiteSpace(policy.MetadataJson) ||
                string.IsNullOrWhiteSpace(objectKey) ||
                string.IsNullOrWhiteSpace(childKey))
            {
                return null;
            }

            try
            {
                using var doc = JsonDocument.Parse(policy.MetadataJson);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (!string.Equals(prop.Name, objectKey, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (prop.Value.ValueKind != JsonValueKind.Object)
                    {
                        return null;
                    }

                    foreach (var child in prop.Value.EnumerateObject())
                    {
                        if (!string.Equals(child.Name, childKey, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        return child.Value.ValueKind switch
                        {
                            JsonValueKind.String => child.Value.GetString(),
                            JsonValueKind.Number => child.Value.GetRawText(),
                            JsonValueKind.True => "true",
                            JsonValueKind.False => "false",
                            _ => null
                        };
                    }

                    return null;
                }
            }
            catch
            {
                return null;
            }

            return null;
        }

        private static Dictionary<string, string> NormalizeActionMap(IReadOnlyDictionary<string, string> request)
        {
            var normalized = DefaultPhase2ActionMap();
            foreach (var kvp in request)
            {
                if (string.IsNullOrWhiteSpace(kvp.Key)) continue;
                normalized[kvp.Key.Trim()] = (kvp.Value ?? string.Empty).Trim();
            }

            return normalized;
        }

        private static Dictionary<string, string> DefaultPhase2ActionMap() => new(StringComparer.OrdinalIgnoreCase)
        {
            ["mode"] = "policy_actions_phase2",
            ["warn"] = "persist_event_and_warn",
            ["review"] = "create_review_queue_item",
            ["softHold"] = "create_soft_hold",
            ["hardHold"] = "create_hard_hold"
        };

        private static HashSet<string> ResolveEnabledRuleKeys(TrustRiskPolicy policy)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(policy.EnabledRulesJson))
            {
                return result;
            }

            try
            {
                using var doc = JsonDocument.Parse(policy.EnabledRulesJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in doc.RootElement.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String)
                        {
                            var value = item.GetString();
                            if (!string.IsNullOrWhiteSpace(value))
                            {
                                result.Add(value.Trim());
                            }
                        }
                    }
                }
                else if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        var enabled = prop.Value.ValueKind switch
                        {
                            JsonValueKind.True => true,
                            JsonValueKind.False => false,
                            JsonValueKind.String when bool.TryParse(prop.Value.GetString(), out var b) => b,
                            _ => false
                        };
                        if (enabled)
                        {
                            result.Add(prop.Name);
                        }
                    }
                }
            }
            catch
            {
                // malformed enabled rules -> fallback empty
            }

            return result;
        }

        private static Dictionary<string, object?> BuildPolicyMetadataForPhase2(
            TrustRiskPolicy current,
            BuiltInTrustRiskPolicyTemplate? template,
            IReadOnlyCollection<string>? overrideRoles,
            IReadOnlyCollection<string>? releaseRoles,
            IReadOnlyCollection<string>? criticalDualApprovalSecondaryRoles,
            bool? opsAlertsEnabled,
            IReadOnlyCollection<string>? opsAlertChannels,
            string? policyTemplate,
            IReadOnlyCollection<TrustAccountPolicyOverrideRequest>? trustAccountOverrides,
            bool? criticalDualApprovalEnabled,
            int? holdEscalationSlaMinutes,
            int? softHoldExpiryHours,
            int? hardHoldExpiryHours,
            bool? requireCriticalThresholdChangeReview,
            string? criticalThresholdChangeReason,
            CriticalThresholdChangeSummary criticalThresholdChange,
            IReadOnlyDictionary<string, object?>? additionalMetadata)
        {
            var metadata = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["phase"] = "trust-risk-radar.phaseX2",
                ["holdEnforcementEnabled"] = true,
                ["decisionStrategy"] = "deterministic_thresholds_policy_actions",
                ["policyTemplate"] = (policyTemplate ?? template?.TemplateKey ?? ResolvePolicyString(current, "policyTemplate", "balanced"))?.Trim().ToLowerInvariant(),
                ["overrideRoles"] = (overrideRoles?.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                    ?? ResolvePolicyRoleSet(current, "overrideRoles", "SecurityAdmin", "Admin").ToArray()),
                ["releaseRoles"] = (releaseRoles?.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                    ?? (template?.ReleaseRoles?.ToArray() ?? ResolvePolicyRoleSet(current, "releaseRoles", "FinanceAdmin", "Admin").ToArray())),
                ["criticalDualApprovalEnabled"] = criticalDualApprovalEnabled
                    ?? template?.CriticalDualApprovalEnabled
                    ?? ResolvePolicyFlag(current, "criticalDualApprovalEnabled", false),
                ["criticalDualApprovalSecondaryRoles"] = (criticalDualApprovalSecondaryRoles?.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                    ?? (template?.CriticalDualApprovalSecondaryRoles?.ToArray()
                        ?? ResolvePolicyRoleSet(current, "criticalDualApprovalSecondaryRoles", "SecurityAdmin", "Admin").ToArray())),
                ["holdEscalationSlaMinutes"] = Math.Clamp(holdEscalationSlaMinutes ?? template?.HoldEscalationSlaMinutes ?? ResolvePolicyInt(current, "holdEscalationSlaMinutes", 120), 0, 10080),
                ["softHoldExpiryHours"] = Math.Clamp(softHoldExpiryHours ?? template?.SoftHoldExpiryHours ?? ResolvePolicyInt(current, "softHoldExpiryHours", 24), 0, 8760),
                ["hardHoldExpiryHours"] = Math.Clamp(hardHoldExpiryHours ?? template?.HardHoldExpiryHours ?? ResolvePolicyInt(current, "hardHoldExpiryHours", 72), 0, 8760),
                ["criticalThresholdChangeReviewRequired"] = requireCriticalThresholdChangeReview
                    ?? ResolvePolicyFlag(current, "criticalThresholdChangeReviewRequired", true),
                ["opsAlertsEnabled"] = opsAlertsEnabled ?? ResolvePolicyFlag(current, "opsAlertsEnabled", false),
                ["opsAlertChannels"] = (opsAlertChannels?.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                    ?? ResolveOpsAlertChannels(current)),
                ["behavioralSignalsEnabled"] = ResolvePolicyFlag(current, "behavioralSignalsEnabled", true),
                ["behavioralShadowMode"] = ResolvePolicyFlag(current, "behavioralShadowMode", true),
                ["behavioralLookbackDays"] = ResolvePolicyInt(current, "behavioralLookbackDays", 45),
                ["behavioralMinSamples"] = ResolvePolicyInt(current, "behavioralMinSamples", 10),
                ["behavioralAmountRatioThreshold"] = ResolvePolicyDecimal(current, "behavioralAmountRatioThreshold", 4m),
                ["behavioralTimePatternDeltaThreshold"] = ResolvePolicyDecimal(current, "behavioralTimePatternDeltaThreshold", 0.35m),
                ["behavioralReversalRateDeltaThreshold"] = ResolvePolicyDecimal(current, "behavioralReversalRateDeltaThreshold", 0.20m),
                ["behavioralContributionCap"] = ResolvePolicyDecimal(current, "behavioralContributionCap", 18m),
                ["preflightStrictModeEnabled"] = ResolvePolicyFlag(current, "preflightStrictModeEnabled", false),
                ["preflightStrictRolloutMode"] = NormalizePreflightRolloutMode(ResolvePolicyString(current, "preflightStrictRolloutMode", "warn")),
                ["preflightHighConfidenceOnly"] = ResolvePolicyFlag(current, "preflightHighConfidenceOnly", true),
                ["preflightMinSeverity"] = NormalizeSeverityLabel(ResolvePolicyString(current, "preflightMinSeverity", "critical")),
                ["preflightRecentEventWindowMinutes"] = Math.Clamp(ResolvePolicyInt(current, "preflightRecentEventWindowMinutes", 30), 1, 1440),
                ["preflightDuplicateSuppressionEnabled"] = ResolvePolicyFlag(current, "preflightDuplicateSuppressionEnabled", true),
                ["operationFailModes"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["manual_ledger_post"] = NormalizeFailMode(ResolvePolicyObjectString(current, "operationFailModes", "manual_ledger_post") ?? current.FailMode),
                    ["ledger_reversal"] = NormalizeFailMode(ResolvePolicyObjectString(current, "operationFailModes", "ledger_reversal") ?? current.FailMode),
                    ["payment_allocation_apply"] = NormalizeFailMode(ResolvePolicyObjectString(current, "operationFailModes", "payment_allocation_apply") ?? current.FailMode),
                    ["payment_allocation_reverse"] = NormalizeFailMode(ResolvePolicyObjectString(current, "operationFailModes", "payment_allocation_reverse") ?? current.FailMode)
                }
            };

            if (trustAccountOverrides != null)
            {
                metadata["trustAccountPolicyOverrides"] = NormalizeTrustAccountOverrides(trustAccountOverrides);
            }
            else
            {
                var existingOverrides = ResolveTrustAccountOverrideMapRaw(current);
                if (existingOverrides != null)
                {
                    metadata["trustAccountPolicyOverrides"] = existingOverrides;
                }
            }

            if (criticalThresholdChange.IsCritical)
            {
                metadata["criticalThresholdChangeDetected"] = true;
                metadata["criticalThresholdChangeSummary"] = criticalThresholdChange.Summary;
                if (!string.IsNullOrWhiteSpace(criticalThresholdChangeReason))
                {
                    metadata["criticalThresholdChangeReason"] = Truncate(criticalThresholdChangeReason, 2048);
                }
            }

            if (additionalMetadata != null)
            {
                foreach (var kvp in additionalMetadata)
                {
                    if (string.IsNullOrWhiteSpace(kvp.Key)) continue;
                    metadata[kvp.Key] = kvp.Value;
                }
            }

            return metadata;
        }

        private static BuiltInTrustRiskPolicyTemplate? ResolveRequestedTemplate(string? templateKey)
        {
            if (string.IsNullOrWhiteSpace(templateKey))
            {
                return null;
            }

            var normalized = templateKey.Trim().ToLowerInvariant();
            return normalized is "conservative" or "balanced" or "aggressive"
                ? CreateBuiltInPolicyTemplate(normalized)
                : null;
        }

        private static TrustRiskPolicyTemplateDescriptor ToPolicyTemplateDescriptor(BuiltInTrustRiskPolicyTemplate t) =>
            new(
                t.TemplateKey,
                t.Name,
                t.Description,
                new TrustRiskThresholdSnapshot(t.WarnThreshold, t.ReviewThreshold, t.SoftHoldThreshold, t.HardHoldThreshold),
                t.CriticalDualApprovalEnabled,
                t.HoldEscalationSlaMinutes,
                t.SoftHoldExpiryHours,
                t.HardHoldExpiryHours,
                t.OverrideRoles.ToArray(),
                t.ReleaseRoles.ToArray(),
                t.CriticalDualApprovalSecondaryRoles.ToArray());

        private static BuiltInTrustRiskPolicyTemplate CreateBuiltInPolicyTemplate(string templateKey)
        {
            var normalized = (templateKey ?? "balanced").Trim().ToLowerInvariant();
            return normalized switch
            {
                "conservative" => new BuiltInTrustRiskPolicyTemplate(
                    "conservative", "Conservative", "Lower thresholds, higher review/hold sensitivity.",
                    25m, 45m, 65m, 85m,
                    new[] { "SecurityAdmin", "Admin" },
                    new[] { "FinanceAdmin", "Admin" },
                    new[] { "SecurityAdmin", "Admin" },
                    true, 60, 24, 48),
                "aggressive" => new BuiltInTrustRiskPolicyTemplate(
                    "aggressive", "Aggressive", "Higher thresholds to reduce review burden; behavioral signals still active.",
                    45m, 70m, 88m, 97m,
                    new[] { "SecurityAdmin", "Admin" },
                    new[] { "FinanceAdmin", "Admin" },
                    new[] { "SecurityAdmin", "Admin" },
                    false, 180, 36, 96),
                _ => new BuiltInTrustRiskPolicyTemplate(
                    "balanced", "Balanced", "Default production-ready mix of review burden and risk sensitivity.",
                    35m, 60m, 80m, 95m,
                    new[] { "SecurityAdmin", "Admin" },
                    new[] { "FinanceAdmin", "Admin" },
                    new[] { "SecurityAdmin", "Admin" },
                    false, 120, 24, 72)
            };
        }

        private static EffectiveTrustRiskThresholds ResolveEffectiveThresholds(TrustRiskPolicy policy, string? trustAccountId)
        {
            var warn = ResolvePolicyDecimal(policy, "warnThreshold", policy.WarnThreshold, trustAccountId);
            var review = ResolvePolicyDecimal(policy, "reviewThreshold", policy.ReviewThreshold, trustAccountId);
            var soft = ResolvePolicyDecimal(policy, "softHoldThreshold", policy.SoftHoldThreshold, trustAccountId);
            var hard = ResolvePolicyDecimal(policy, "hardHoldThreshold", policy.HardHoldThreshold, trustAccountId);
            if (!(warn <= review && review <= soft && soft <= hard))
            {
                return new EffectiveTrustRiskThresholds(policy.WarnThreshold, policy.ReviewThreshold, policy.SoftHoldThreshold, policy.HardHoldThreshold);
            }

            return new EffectiveTrustRiskThresholds(warn, review, soft, hard);
        }

        private static string? TryResolveTrustAccountId(
            IReadOnlyDictionary<string, object?> evidence,
            IReadOnlyDictionary<string, object?> features)
        {
            if (evidence.TryGetValue("trustAccountId", out var ev) && !string.IsNullOrWhiteSpace(ev?.ToString()))
            {
                return Truncate(ev.ToString(), 128);
            }
            if (features.TryGetValue("trustAccountId", out var fv) && !string.IsNullOrWhiteSpace(fv?.ToString()))
            {
                return Truncate(fv.ToString(), 128);
            }
            return null;
        }

        private static string? ResolveTrustAccountIdFromHoldOrEvent(TrustRiskHold hold, TrustRiskEvent? riskEvent)
        {
            var holdMetadata = TryParseJsonObject(hold.MetadataJson);
            if (holdMetadata.TryGetValue("trustAccountId", out var holdTa) && !string.IsNullOrWhiteSpace(holdTa))
            {
                return Truncate(holdTa, 128);
            }

            var eventEvidence = TryParseJsonObject(riskEvent?.EvidenceJson);
            if (eventEvidence.TryGetValue("trustAccountId", out var evTa) && !string.IsNullOrWhiteSpace(evTa))
            {
                return Truncate(evTa, 128);
            }

            return null;
        }

        private static bool HasTrustAccountOverride(TrustRiskPolicy policy, string trustAccountId) =>
            ResolveTrustAccountOverrideProperty(policy, trustAccountId, "warnThreshold").HasValue ||
            ResolveTrustAccountOverrideProperty(policy, trustAccountId, "reviewThreshold").HasValue ||
            ResolveTrustAccountOverrideProperty(policy, trustAccountId, "softHoldThreshold").HasValue ||
            ResolveTrustAccountOverrideProperty(policy, trustAccountId, "hardHoldThreshold").HasValue ||
            ResolveTrustAccountOverrideProperty(policy, trustAccountId, "actionMap").HasValue;

        private static bool ResolveCriticalThresholdChangeReviewRequired(TrustRiskPolicy current, bool? requested) =>
            requested ?? ResolvePolicyFlag(current, "criticalThresholdChangeReviewRequired", true);

        private static CriticalThresholdChangeSummary DetectCriticalThresholdChange(TrustRiskPolicy current, decimal warn, decimal review, decimal softHold, decimal hardHold)
        {
            var changes = new List<string>();
            var loweredCritical = false;

            void Track(string key, decimal oldValue, decimal newValue, bool criticalMetric)
            {
                var delta = NormalizeMoney(newValue - oldValue);
                if (delta == 0m) return;
                changes.Add($"{key}:{oldValue}->{newValue} ({(delta > 0 ? "+" : string.Empty)}{delta})");
                if (criticalMetric && newValue < oldValue)
                {
                    loweredCritical = true;
                }
                if (criticalMetric && Math.Abs(delta) >= 5m)
                {
                    loweredCritical = true;
                }
            }

            Track("warn", current.WarnThreshold, warn, criticalMetric: false);
            Track("review", current.ReviewThreshold, review, criticalMetric: true);
            Track("softHold", current.SoftHoldThreshold, softHold, criticalMetric: true);
            Track("hardHold", current.HardHoldThreshold, hardHold, criticalMetric: true);

            return new CriticalThresholdChangeSummary(
                IsCritical: loweredCritical,
                Changed: changes.Count > 0,
                Summary: changes.Count == 0 ? null : string.Join("; ", changes),
                ChangedKeys: changes);
        }

        private IntegrationReviewQueueItem BuildPolicyThresholdChangeReviewItem(
            TrustRiskPolicy previous,
            TrustRiskPolicy next,
            CriticalThresholdChangeSummary change,
            string? reason,
            string? userId)
        {
            return new IntegrationReviewQueueItem
            {
                ProviderKey = ProviderKey,
                ItemType = "trust_risk_policy_threshold_change_review",
                SourceType = nameof(TrustRiskPolicy),
                SourceId = next.Id,
                Status = IntegrationReviewQueueStatuses.Pending,
                Priority = "high",
                Title = "Trust Risk Policy Critical Threshold Change Review",
                Summary = Truncate(change.Summary ?? "Critical trust risk threshold change requires review.", 2048),
                ContextJson = Serialize(new
                {
                    previousPolicyId = previous.Id,
                    previousVersion = previous.VersionNumber,
                    nextPolicyId = next.Id,
                    nextVersion = next.VersionNumber,
                    previousThresholds = new { previous.WarnThreshold, previous.ReviewThreshold, previous.SoftHoldThreshold, previous.HardHoldThreshold },
                    nextThresholds = new { next.WarnThreshold, next.ReviewThreshold, next.SoftHoldThreshold, next.HardHoldThreshold },
                    changeSummary = change.Summary,
                    changedKeys = change.ChangedKeys,
                    reason,
                    requestedBy = userId
                }),
                SuggestedActionsJson = Serialize(new object[]
                {
                    new { action = "review_trust_risk_policy_threshold_change", trustRiskPolicyId = next.Id },
                    new { action = "compare_policy_versions", previousPolicyId = previous.Id, nextPolicyId = next.Id }
                }),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
        }

        private static object NormalizeTrustAccountOverrides(IReadOnlyCollection<TrustAccountPolicyOverrideRequest> overrides)
        {
            var result = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var item in overrides)
            {
                if (string.IsNullOrWhiteSpace(item.TrustAccountId))
                {
                    continue;
                }

                var entry = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                if (item.WarnThreshold.HasValue) entry["warnThreshold"] = NormalizeMoney(item.WarnThreshold.Value);
                if (item.ReviewThreshold.HasValue) entry["reviewThreshold"] = NormalizeMoney(item.ReviewThreshold.Value);
                if (item.SoftHoldThreshold.HasValue) entry["softHoldThreshold"] = NormalizeMoney(item.SoftHoldThreshold.Value);
                if (item.HardHoldThreshold.HasValue) entry["hardHoldThreshold"] = NormalizeMoney(item.HardHoldThreshold.Value);
                if (item.OverrideRoles != null) entry["overrideRoles"] = item.OverrideRoles.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                if (item.ReleaseRoles != null) entry["releaseRoles"] = item.ReleaseRoles.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                if (item.CriticalDualApprovalSecondaryRoles != null) entry["criticalDualApprovalSecondaryRoles"] = item.CriticalDualApprovalSecondaryRoles.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                if (item.CriticalDualApprovalEnabled.HasValue) entry["criticalDualApprovalEnabled"] = item.CriticalDualApprovalEnabled.Value;
                if (item.HoldEscalationSlaMinutes.HasValue) entry["holdEscalationSlaMinutes"] = Math.Clamp(item.HoldEscalationSlaMinutes.Value, 0, 10080);
                if (item.SoftHoldExpiryHours.HasValue) entry["softHoldExpiryHours"] = Math.Clamp(item.SoftHoldExpiryHours.Value, 0, 8760);
                if (item.HardHoldExpiryHours.HasValue) entry["hardHoldExpiryHours"] = Math.Clamp(item.HardHoldExpiryHours.Value, 0, 8760);
                if (item.OpsAlertsEnabled.HasValue) entry["opsAlertsEnabled"] = item.OpsAlertsEnabled.Value;
                if (item.OpsAlertChannels != null) entry["opsAlertChannels"] = item.OpsAlertChannels.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                if (item.ActionMap != null && item.ActionMap.Count > 0) entry["actionMap"] = NormalizeActionMap(item.ActionMap);
                if (!string.IsNullOrWhiteSpace(item.Note)) entry["note"] = Truncate(item.Note, 512);

                if (entry.Count > 0)
                {
                    result[item.TrustAccountId.Trim()] = entry;
                }
            }
            return result;
        }

        private static object? ResolveTrustAccountOverrideMapRaw(TrustRiskPolicy policy)
        {
            if (string.IsNullOrWhiteSpace(policy.MetadataJson))
            {
                return null;
            }

            try
            {
                using var doc = JsonDocument.Parse(policy.MetadataJson);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (!string.Equals(prop.Name, "trustAccountPolicyOverrides", StringComparison.OrdinalIgnoreCase)) continue;
                    return JsonSerializer.Deserialize<object>(prop.Value.GetRawText(), JsonOptions);
                }
            }
            catch
            {
                return null;
            }

            return null;
        }

        private static JsonElement? ResolveTrustAccountOverrideProperty(TrustRiskPolicy policy, string trustAccountId, string propertyKey)
        {
            if (string.IsNullOrWhiteSpace(trustAccountId) || string.IsNullOrWhiteSpace(policy.MetadataJson))
            {
                return null;
            }

            try
            {
                using var doc = JsonDocument.Parse(policy.MetadataJson);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                JsonElement overridesRoot = default;
                var foundOverrides = false;
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (string.Equals(prop.Name, "trustAccountPolicyOverrides", StringComparison.OrdinalIgnoreCase) &&
                        prop.Value.ValueKind == JsonValueKind.Object)
                    {
                        overridesRoot = prop.Value.Clone();
                        foundOverrides = true;
                        break;
                    }
                }
                if (!foundOverrides) return null;

                JsonElement? accountNode = null;
                foreach (var accountProp in overridesRoot.EnumerateObject())
                {
                    if (string.Equals(accountProp.Name, trustAccountId, StringComparison.OrdinalIgnoreCase))
                    {
                        accountNode = accountProp.Value.Clone();
                        break;
                    }
                }
                if (accountNode == null || accountNode.Value.ValueKind != JsonValueKind.Object) return null;

                foreach (var prop in accountNode.Value.EnumerateObject())
                {
                    if (string.Equals(prop.Name, propertyKey, StringComparison.OrdinalIgnoreCase))
                    {
                        return prop.Value.Clone();
                    }
                }
            }
            catch
            {
                return null;
            }

            return null;
        }

        private static Dictionary<string, string>? ResolveTrustAccountOverrideActionMap(TrustRiskPolicy policy, string trustAccountId)
        {
            var prop = ResolveTrustAccountOverrideProperty(policy, trustAccountId, "actionMap");
            if (!prop.HasValue || prop.Value.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var result = DefaultPhase2ActionMap();
            foreach (var item in prop.Value.EnumerateObject())
            {
                if (item.Value.ValueKind != JsonValueKind.String) continue;
                result[item.Name] = (item.Value.GetString() ?? string.Empty).Trim();
            }
            return result;
        }

        private static bool RequiresCriticalDualApproval(TrustRiskPolicy policy, TrustRiskHold hold, TrustRiskEvent? riskEvent, string? trustAccountId)
        {
            if (!string.Equals(hold.HoldType, "hard", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var severity = (riskEvent?.Severity ?? string.Empty).Trim().ToLowerInvariant();
            if (severity != "critical")
            {
                return false;
            }

            return ResolvePolicyFlag(policy, "criticalDualApprovalEnabled", false, trustAccountId);
        }

        private static PendingDualApprovalState? GetPendingDualApprovalState(TrustRiskHold hold)
        {
            var metadata = TryParseJsonObject(hold.MetadataJson);
            if (!metadata.TryGetValue("dualApprovalPending", out var pendingRaw))
            {
                return null;
            }

            var pending = string.Equals(pendingRaw, "true", StringComparison.OrdinalIgnoreCase);
            return new PendingDualApprovalState(
                pending,
                metadata.TryGetValue("dualApprovalPrimaryUserId", out var u) ? u : null,
                metadata.TryGetValue("dualApprovalPrimaryRole", out var r) ? r : null);
        }

        private static string NormalizeFailMode(string value)
        {
            var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
            return normalized is "fail_closed" ? "fail_closed" : "fail_open";
        }

        private static void ValidateThresholds(decimal warn, decimal review, decimal softHold, decimal hardHold)
        {
            static bool IsValid(decimal v) => v >= 0m && v <= 100m;
            if (!IsValid(warn) || !IsValid(review) || !IsValid(softHold) || !IsValid(hardHold))
            {
                throw new InvalidOperationException("Thresholds must be between 0 and 100.");
            }

            if (!(warn <= review && review <= softHold && softHold <= hardHold))
            {
                throw new InvalidOperationException("Thresholds must satisfy Warn <= Review <= SoftHold <= HardHold.");
            }
        }

        private static string? MergeJsonMetadata(string? existingJson, IReadOnlyDictionary<string, object?> updates)
        {
            var merged = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(existingJson))
            {
                try
                {
                    using var doc = JsonDocument.Parse(existingJson);
                    if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var prop in doc.RootElement.EnumerateObject())
                        {
                            merged[prop.Name] = prop.Value.ValueKind switch
                            {
                                JsonValueKind.String => prop.Value.GetString(),
                                JsonValueKind.Number => prop.Value.GetRawText(),
                                JsonValueKind.True => true,
                                JsonValueKind.False => false,
                                JsonValueKind.Null => null,
                                _ => prop.Value.GetRawText()
                            };
                        }
                    }
                }
                catch
                {
                    // ignore malformed existing metadata
                }
            }

            foreach (var kvp in updates)
            {
                if (string.IsNullOrWhiteSpace(kvp.Key)) continue;
                merged[kvp.Key] = kvp.Value;
            }

            return Serialize(merged);
        }

        private static IEnumerable<TrustRiskHoldTargetCandidate> BuildHoldTargetCandidates(TrustRiskHoldGuardContext context)
        {
            yield return new("billing_ledger_entry", context.BillingLedgerEntryId);
            yield return new("billing_payment_allocation", context.BillingPaymentAllocationId);
            yield return new("trust_transaction", context.TrustTransactionId);
            yield return new("payment_transaction", context.PaymentTransactionId);
            yield return new("invoice", context.InvoiceId);
            yield return new("matter", context.MatterId);
            yield return new("client", context.ClientId);
            yield return new("payor_client", context.PayorClientId);
            yield return new("invoice_payor_allocation", context.InvoicePayorAllocationId);
        }

        private static List<string> ExtractEvidenceRefs(Dictionary<string, object?> evidence)
        {
            if (evidence.TryGetValue("evidenceRefs", out var existing) && existing is List<string> list)
            {
                return list;
            }

            var created = new List<string>();
            evidence["evidenceRefs"] = created;
            return created;
        }

        private static void AddEvidenceRef(List<string> refs, string prefix, string? id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return;
            }

            refs.Add($"{prefix}:{id}");
        }

        private static string ExtractReasonMessage(object reason)
        {
            try
            {
                using var doc = JsonDocument.Parse(JsonSerializer.Serialize(reason, JsonOptions));
                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty("message", out var messageProp) &&
                    messageProp.ValueKind == JsonValueKind.String)
                {
                    return messageProp.GetString() ?? string.Empty;
                }
            }
            catch
            {
                // fall through
            }

            return reason?.ToString() ?? string.Empty;
        }

        private static decimal ScoreAmountBand(decimal absoluteAmount, List<object> reasons)
        {
            if (absoluteAmount >= 10000m)
            {
                AddReason(reasons, "high_amount_threshold", "High-value trust/billing amount threshold reached.", 30m, new { threshold = 10000m, amount = absoluteAmount });
                return 30m;
            }

            if (absoluteAmount >= 2500m)
            {
                AddReason(reasons, "medium_amount_threshold", "Medium-value trust/billing amount threshold reached.", 15m, new { threshold = 2500m, amount = absoluteAmount });
                return 15m;
            }

            return 0m;
        }

        private static void AddReason(List<object> reasons, string code, string message, decimal weight, object? data = null)
        {
            reasons.Add(new
            {
                code,
                message,
                weight = NormalizeMoney(weight),
                data
            });
        }

        private static string MapSeverity(decimal score)
        {
            score = ClampScore(score);
            if (score >= 90m) return "critical";
            if (score >= 70m) return "high";
            if (score >= 35m) return "medium";
            return "low";
        }

        private static decimal ClampScore(decimal score) => Math.Clamp(NormalizeMoney(score), 0m, 100m);

        private static bool IsWeekend(DateTime utc)
        {
            var dt = utc.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(utc, DateTimeKind.Utc) : utc.ToUniversalTime();
            return dt.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
        }

        private static bool IsOffHours(DateTime utc)
        {
            var dt = utc.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(utc, DateTimeKind.Utc) : utc.ToUniversalTime();
            var hour = dt.Hour;
            return IsWeekend(dt) || hour < 6 || hour >= 21;
        }

        private string? GetHttpContextItem(string key)
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext?.Items == null)
            {
                return null;
            }

            return httpContext.Items.TryGetValue(key, out var value) ? value?.ToString() : null;
        }

        private static string? BuildAllocationSourceCorrelationKey(BillingPaymentAllocation allocation)
        {
            if (string.IsNullOrWhiteSpace(allocation.MetadataJson))
            {
                return null;
            }

            var metadata = TryParseJsonObject(allocation.MetadataJson);
            if (metadata.TryGetValue("idempotencyKey", out var idempotencyKey) && !string.IsNullOrWhiteSpace(idempotencyKey))
            {
                return idempotencyKey;
            }

            return null;
        }

        private static Dictionary<string, string?> TryParseJsonObject(string? json)
        {
            var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(json))
            {
                return result;
            }

            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return result;
                }

                foreach (var property in doc.RootElement.EnumerateObject())
                {
                    result[property.Name] = property.Value.ValueKind switch
                    {
                        JsonValueKind.String => property.Value.GetString(),
                        JsonValueKind.Number => property.Value.GetRawText(),
                        JsonValueKind.True => "true",
                        JsonValueKind.False => "false",
                        JsonValueKind.Null => null,
                        _ => property.Value.GetRawText()
                    };
                }
            }
            catch
            {
                // Ignore malformed metadata JSON in Phase 0; risk event persistence should continue.
            }

            return result;
        }

        private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static string? Truncate(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var trimmed = value.Trim();
            return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
        }

        private static decimal NormalizeMoney(decimal value) =>
            Math.Round(value, 2, MidpointRounding.AwayFromZero);

        private static void ValidateActionReasonQuality(string label, string? value, bool required)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                if (required)
                {
                    throw new InvalidOperationException($"{label} reason is required.");
                }
                return;
            }

            var trimmed = value.Trim();
            if (trimmed.Length < 12)
            {
                throw new InvalidOperationException($"{label} reason must be at least 12 characters.");
            }

            var tokenCount = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
            if (tokenCount < 2)
            {
                throw new InvalidOperationException($"{label} reason must include at least 2 words.");
            }
        }

        private static void ValidateReviewDisposition(string? disposition)
        {
            var normalized = (disposition ?? string.Empty).Trim().ToLowerInvariant();
            if (normalized is not ("true_positive" or "false_positive" or "acceptable_exception"))
            {
                throw new InvalidOperationException("Disposition must be one of: true_positive, false_positive, acceptable_exception.");
            }
        }

        private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);

        public sealed record TrustRiskPolicyUpsertRequest
        {
            public string? PolicyKey { get; init; }
            public string? TemplateKey { get; init; }
            public string? PolicyTemplate { get; init; }
            public string? Name { get; init; }
            public string? Description { get; init; }
            public decimal? WarnThreshold { get; init; }
            public decimal? ReviewThreshold { get; init; }
            public decimal? SoftHoldThreshold { get; init; }
            public decimal? HardHoldThreshold { get; init; }
            public string? FailMode { get; init; }
            public IReadOnlyCollection<string>? EnabledRules { get; init; }
            public IReadOnlyDictionary<string, decimal>? RuleWeights { get; init; }
            public IReadOnlyDictionary<string, string>? ActionMap { get; init; }
            public IReadOnlyCollection<string>? OverrideRoles { get; init; }
            public IReadOnlyCollection<string>? ReleaseRoles { get; init; }
            public IReadOnlyCollection<string>? CriticalDualApprovalSecondaryRoles { get; init; }
            public bool? CriticalDualApprovalEnabled { get; init; }
            public int? HoldEscalationSlaMinutes { get; init; }
            public int? SoftHoldExpiryHours { get; init; }
            public int? HardHoldExpiryHours { get; init; }
            public bool? RequireCriticalThresholdChangeReview { get; init; }
            public string? CriticalThresholdChangeReason { get; init; }
            public bool? OpsAlertsEnabled { get; init; }
            public IReadOnlyCollection<string>? OpsAlertChannels { get; init; }
            public IReadOnlyCollection<TrustAccountPolicyOverrideRequest>? TrustAccountOverrides { get; init; }
            public IReadOnlyDictionary<string, object?>? AdditionalMetadata { get; init; }
        }

        public sealed record TrustAccountPolicyOverrideRequest
        {
            public string TrustAccountId { get; init; } = string.Empty;
            public decimal? WarnThreshold { get; init; }
            public decimal? ReviewThreshold { get; init; }
            public decimal? SoftHoldThreshold { get; init; }
            public decimal? HardHoldThreshold { get; init; }
            public IReadOnlyDictionary<string, string>? ActionMap { get; init; }
            public IReadOnlyCollection<string>? OverrideRoles { get; init; }
            public IReadOnlyCollection<string>? ReleaseRoles { get; init; }
            public IReadOnlyCollection<string>? CriticalDualApprovalSecondaryRoles { get; init; }
            public bool? CriticalDualApprovalEnabled { get; init; }
            public int? HoldEscalationSlaMinutes { get; init; }
            public int? SoftHoldExpiryHours { get; init; }
            public int? HardHoldExpiryHours { get; init; }
            public bool? OpsAlertsEnabled { get; init; }
            public IReadOnlyCollection<string>? OpsAlertChannels { get; init; }
            public string? Note { get; init; }
        }

        public sealed record TrustRiskHoldGuardContext
        {
            public string? OperationType { get; init; }
            public string? BillingLedgerEntryId { get; init; }
            public string? BillingPaymentAllocationId { get; init; }
            public string? TrustTransactionId { get; init; }
            public string? PaymentTransactionId { get; init; }
            public string? InvoiceId { get; init; }
            public string? InvoicePayorAllocationId { get; init; }
            public string? MatterId { get; init; }
            public string? ClientId { get; init; }
            public string? PayorClientId { get; init; }
        }

        public sealed record TrustRiskBehavioralBaselineSummary(
            int WindowDays,
            DateTime GeneratedAtUtc,
            IReadOnlyList<TrustRiskBehavioralBaselineBucket> TenantBaselines,
            IReadOnlyList<TrustRiskBehavioralBaselineBucket> TrustAccountBaselines,
            IReadOnlyList<TrustRiskBehavioralBaselineBucket> MatterBaselines,
            IReadOnlyDictionary<string, string> DataQuality);

        public sealed record TrustRiskBehavioralBaselineBucket(
            string ScopeType,
            string ScopeId,
            int SampleCount,
            decimal AverageAbsoluteAmount,
            double OffHoursRate,
            double WeekendRate,
            double ReversalRate,
            IReadOnlyList<int> HourBuckets);

        public sealed record TrustRiskBehavioralTuningSummary(
            int WindowDays,
            DateTime GeneratedAtUtc,
            TrustRiskThresholdSnapshot CurrentThresholds,
            TrustRiskThresholdSnapshot SuggestedThresholds,
            TrustRiskBehavioralPolicySnapshot BehavioralPolicy,
            IReadOnlyDictionary<string, decimal?> Distribution,
            IReadOnlyDictionary<string, object?> BehavioralSignalStats,
            IReadOnlyDictionary<string, string> DataQuality);

        public sealed record TrustRiskThresholdSnapshot(decimal Warn, decimal Review, decimal SoftHold, decimal HardHold);

        public sealed record TrustRiskPolicyTemplateDescriptor(
            string TemplateKey,
            string Name,
            string Description,
            TrustRiskThresholdSnapshot Thresholds,
            bool CriticalDualApprovalEnabled,
            int HoldEscalationSlaMinutes,
            int SoftHoldExpiryHours,
            int HardHoldExpiryHours,
            IReadOnlyCollection<string> OverrideRoles,
            IReadOnlyCollection<string> ReleaseRoles,
            IReadOnlyCollection<string> CriticalDualApprovalSecondaryRoles);

        public sealed record TrustRiskBehavioralPolicySnapshot(
            bool Enabled,
            bool ShadowMode,
            int LookbackDays,
            int MinSamples,
            decimal AmountRatioThreshold,
            decimal ContributionCap);

        private sealed record TrustRiskHoldTargetCandidate(string TargetType, string? TargetId);
        private sealed record EffectiveTrustRiskThresholds(decimal Warn, decimal Review, decimal SoftHold, decimal Hard);
        private sealed record PendingDualApprovalState(bool Pending, string? PrimaryUserId, string? PrimaryRole);
        private sealed record CriticalThresholdChangeSummary(bool IsCritical, bool Changed, string? Summary, IReadOnlyList<string> ChangedKeys);
        private sealed record BuiltInTrustRiskPolicyTemplate(
            string TemplateKey,
            string Name,
            string Description,
            decimal WarnThreshold,
            decimal ReviewThreshold,
            decimal SoftHoldThreshold,
            decimal HardHoldThreshold,
            IReadOnlyCollection<string> OverrideRoles,
            IReadOnlyCollection<string> ReleaseRoles,
            IReadOnlyCollection<string> CriticalDualApprovalSecondaryRoles,
            bool CriticalDualApprovalEnabled,
            int HoldEscalationSlaMinutes,
            int SoftHoldExpiryHours,
            int HardHoldExpiryHours);

        private sealed record BehavioralPolicyOptions(
            bool Enabled,
            bool ShadowMode,
            int LookbackDays,
            int MinSamples,
            decimal AmountRatioThreshold,
            double TimePatternDeltaThreshold,
            double ReversalRateDeltaThreshold,
            decimal ContributionCap);

        private sealed record BehavioralBaselineStats(
            int Count,
            decimal AverageAbsoluteAmount,
            double OffHoursRate,
            double WeekendRate,
            double ReversalRate,
            IReadOnlyList<int> HourBuckets);

        private sealed class BehavioralRuleCounter
        {
            public int Observations { get; set; }
            public int ShadowObservations { get; set; }
            public int TruePositive { get; set; }
            public int FalsePositive { get; set; }
            public int AcceptableException { get; set; }
            public decimal CandidateWeightSum { get; set; }
            public decimal AppliedWeightSum { get; set; }
        }

        private sealed record TrustRiskPreflightOptions(
            bool Enabled,
            string RolloutMode,
            bool HighConfidenceOnly,
            string MinSeverity,
            int RecentEventWindowMinutes,
            bool DuplicateSuppressionEnabled);

        private sealed class TrustRiskPreflightBlockException : Exception
        {
            public TrustRiskPreflightBlockException(string message) : base(message)
            {
            }
        }

        private sealed record TrustRiskEvaluation(
            decimal Score,
            string Severity,
            IReadOnlyList<object> Reasons,
            IReadOnlyDictionary<string, object?> Evidence,
            IReadOnlyDictionary<string, object?> Features);
    }
}
