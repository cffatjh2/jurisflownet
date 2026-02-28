using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text.Json;
using Task = System.Threading.Tasks.Task;

namespace JurisFlow.Server.Services
{
    public partial class OutcomeFeePlannerService
    {
        private static readonly (int Order, string Code, string Name, decimal Share, int Days)[] PhaseTemplate =
        {
            (1, "intake", "Intake & Assessment", 0.08m, 14),
            (2, "pleading", "Pleading", 0.12m, 21),
            (3, "discovery", "Discovery", 0.34m, 90),
            (4, "motion", "Motion Practice", 0.16m, 45),
            (5, "settlement", "Settlement", 0.10m, 30),
            (6, "trial_prep", "Trial Preparation", 0.12m, 35),
            (7, "trial", "Trial", 0.08m, 10)
        };

        private readonly JurisFlowDbContext _context;

        public OutcomeFeePlannerService(JurisFlowDbContext context)
        {
            _context = context;
        }

        public async Task<OutcomeFeePlanDetailResult?> GetLatestPlanForMatterAsync(string matterId, CancellationToken ct)
        {
            var normalizedMatterId = matterId?.Trim();
            if (string.IsNullOrWhiteSpace(normalizedMatterId)) return null;

            var plan = await _context.OutcomeFeePlans
                .OrderByDescending(p => p.UpdatedAt)
                .FirstOrDefaultAsync(p => p.MatterId == normalizedMatterId && p.Status == "active", ct);

            return plan == null ? null : await GetPlanDetailAsync(plan.Id, ct);
        }

        public async Task<OutcomeFeePlanDetailResult?> GetPlanDetailAsync(string planId, CancellationToken ct)
        {
            var plan = await _context.OutcomeFeePlans.FirstOrDefaultAsync(p => p.Id == planId, ct);
            if (plan == null) return null;

            var versions = await _context.OutcomeFeePlanVersions
                .Where(v => v.PlanId == plan.Id)
                .OrderByDescending(v => v.VersionNumber)
                .ThenByDescending(v => v.GeneratedAt)
                .ToListAsync(ct);

            var currentVersion = versions.FirstOrDefault(v => v.Id == plan.CurrentVersionId) ?? versions.FirstOrDefault();
            if (currentVersion == null)
            {
                return new OutcomeFeePlanDetailResult { Plan = plan, Versions = versions };
            }

            var scenarios = await _context.OutcomeFeeScenarios
                .Where(s => s.PlanVersionId == currentVersion.Id)
                .OrderBy(s => s.ScenarioKey)
                .ToListAsync(ct);
            var scenarioIds = scenarios.Select(s => s.Id).ToList();

            var phaseForecasts = scenarioIds.Count == 0
                ? new List<OutcomeFeePhaseForecast>()
                : await _context.OutcomeFeePhaseForecasts.Where(p => scenarioIds.Contains(p.ScenarioId))
                    .OrderBy(p => p.PhaseOrder)
                    .ThenBy(p => p.CreatedAt)
                    .ToListAsync(ct);

            var staffingLines = scenarioIds.Count == 0
                ? new List<OutcomeFeeStaffingLine>()
                : await _context.OutcomeFeeStaffingLines.Where(s => scenarioIds.Contains(s.ScenarioId))
                    .OrderBy(s => s.Role)
                    .ThenBy(s => s.CreatedAt)
                    .ToListAsync(ct);

            var collections = scenarioIds.Count == 0
                ? new List<OutcomeFeeCollectionsForecast>()
                : await _context.OutcomeFeeCollectionsForecasts.Where(c => scenarioIds.Contains(c.ScenarioId))
                    .OrderBy(c => c.BucketDays)
                    .ThenBy(c => c.PayorSegment)
                    .ToListAsync(ct);

            var assumptions = await _context.OutcomeFeeAssumptions
                .Where(a => a.PlanVersionId == currentVersion.Id)
                .OrderBy(a => a.Category)
                .ThenBy(a => a.Key)
                .ToListAsync(ct);

            return new OutcomeFeePlanDetailResult
            {
                Plan = plan,
                CurrentVersion = currentVersion,
                Versions = versions,
                Scenarios = scenarios,
                PhaseForecasts = phaseForecasts,
                StaffingLines = staffingLines,
                CollectionsForecasts = collections,
                Assumptions = assumptions
            };
        }

        public async Task<IReadOnlyList<OutcomeFeePlanVersion>> ListPlanVersionsAsync(string planId, CancellationToken ct)
        {
            return await _context.OutcomeFeePlanVersions
                .Where(v => v.PlanId == planId)
                .OrderByDescending(v => v.VersionNumber)
                .ThenByDescending(v => v.GeneratedAt)
                .ToListAsync(ct);
        }

        public async Task<OutcomeFeePlanDetailResult> CreatePlanAsync(OutcomeFeePlanCreateRequest request, string userId, CancellationToken ct)
        {
            if (request == null) throw new InvalidOperationException("Request body is required.");
            if (string.IsNullOrWhiteSpace(request.MatterId)) throw new InvalidOperationException("MatterId is required.");

            var now = DateTime.UtcNow;
            var matter = await _context.Matters.FirstOrDefaultAsync(m => m.Id == request.MatterId.Trim(), ct)
                ?? throw new InvalidOperationException("Matter not found.");
            var client = await _context.Clients.FirstOrDefaultAsync(c => c.Id == matter.ClientId, ct);
            var policy = await GetActiveMatterBillingPolicyAsync(matter.Id, now, ct);

            var plan = new OutcomeFeePlan
            {
                MatterId = matter.Id,
                ClientId = matter.ClientId,
                MatterBillingPolicyId = policy?.Id,
                PlannerMode = "hybrid_v1",
                Status = "active",
                CorrelationId = string.IsNullOrWhiteSpace(request.CorrelationId) ? $"ofp_{Guid.NewGuid():N}" : request.CorrelationId.Trim(),
                CreatedBy = userId,
                UpdatedBy = userId,
                MetadataJson = SerializeJson(new { request.Title, request.Notes, createdFrom = "manual_create" }),
                CreatedAt = now,
                UpdatedAt = now
            };
            _context.OutcomeFeePlans.Add(plan);

            await GenerateVersionAsync(plan, matter, client, policy, request, userId, now, "manual_create", ct);
            matter.CurrentOutcomeFeePlanId = plan.Id;

            await _context.SaveChangesAsync(ct);
            return (await GetPlanDetailAsync(plan.Id, ct))!;
        }

        public async Task<OutcomeFeePlanDetailResult?> RecomputePlanAsync(string planId, OutcomeFeePlanRecomputeRequest? request, string userId, CancellationToken ct)
        {
            var plan = await _context.OutcomeFeePlans.FirstOrDefaultAsync(p => p.Id == planId, ct);
            if (plan == null) return null;

            var now = DateTime.UtcNow;
            var matter = await _context.Matters.FirstOrDefaultAsync(m => m.Id == plan.MatterId, ct)
                ?? throw new InvalidOperationException("Matter not found for plan.");
            var client = await _context.Clients.FirstOrDefaultAsync(c => c.Id == matter.ClientId, ct);
            var policy = await GetActiveMatterBillingPolicyAsync(matter.Id, now, ct);

            var latest = await _context.OutcomeFeePlanVersions
                .Where(v => v.PlanId == plan.Id)
                .OrderByDescending(v => v.VersionNumber)
                .FirstOrDefaultAsync(ct);
            if (latest != null && latest.Status == "generated")
            {
                latest.Status = "superseded";
                latest.UpdatedAt = now;
            }

            var createRequest = request?.ToCreateRequest() ?? new OutcomeFeePlanCreateRequest();
            createRequest.MatterId = matter.Id;

            await GenerateVersionAsync(plan, matter, client, policy, createRequest, userId, now, request?.TriggerType ?? "manual_recompute", ct);

            plan.MatterBillingPolicyId = policy?.Id;
            plan.PlannerMode = "hybrid_v1";
            plan.UpdatedBy = userId;
            plan.UpdatedAt = now;
            plan.MetadataJson = MergeJson(plan.MetadataJson, new
            {
                lastRecompute = new { atUtc = now, by = userId, trigger = request?.TriggerType ?? "manual_recompute", request?.Reason }
            });

            await _context.SaveChangesAsync(ct);
            return await GetPlanDetailAsync(plan.Id, ct);
        }

        private async Task GenerateVersionAsync(
            OutcomeFeePlan plan,
            Matter matter,
            Client? client,
            MatterBillingPolicy? policy,
            OutcomeFeePlanCreateRequest request,
            string userId,
            DateTime now,
            string triggerType,
            CancellationToken ct)
        {
            var existingMax = await _context.OutcomeFeePlanVersions
                .Where(v => v.PlanId == plan.Id)
                .Select(v => (int?)v.VersionNumber)
                .MaxAsync(ct) ?? 0;

            var normalized = NormalizeInputs(matter, client, policy, request);
            var calibrationOverlay = await GetCalibrationOverlayForGenerationAsync(matter, client, policy, normalized, ct);
            var generated = BuildDeterministicForecast(normalized);

            var version = new OutcomeFeePlanVersion
            {
                PlanId = plan.Id,
                VersionNumber = existingMax + 1,
                Status = "generated",
                PlannerMode = "hybrid_v1",
                ModelVersion = "outcome-fee-hybrid-v1",
                AssumptionSetVersion = "probabilistic-overlay-v1",
                CorrelationId = string.IsNullOrWhiteSpace(request.CorrelationId) ? plan.CorrelationId : request.CorrelationId?.Trim(),
                MatterBillingPolicyId = policy?.Id,
                RateCardId = policy?.RateCardId,
                Currency = normalized.Currency,
                GeneratedBy = userId,
                GeneratedAt = now,
                CreatedAt = now,
                UpdatedAt = now,
                SourceSignalsJson = SerializeJson(new
                {
                    triggerType,
                    request.PredictionSignal,
                    request.RuleSignals,
                    request.HistoricalCohortSignal,
                    billingPolicy = new { policy?.Id, policy?.ArrangementType, policy?.RateCardId, policy?.CollectionPolicy }
                }),
                InputSnapshotJson = SerializeJson(normalized),
                SummaryJson = SerializeJson(new
                {
                    generated.baseHours,
                    generated.rate,
                    generated.warnings,
                    probabilisticOverlay = true,
                    scenarios = generated.scenarios.Select(s => new
                    {
                        s.key,
                        s.probability,
                        s.confidence,
                        confidenceBand = GetMetadataString(s.metadata, "confidenceBand"),
                        riskFlags = GetMetadataStringList(s.metadata, "riskFlags")
                    }).ToList()
                }),
                MetadataJson = SerializeJson(new
                {
                    generator = "outcome_fee_phase2_probabilistic_overlay",
                    plannerMode = "hybrid_v1"
                })
            };
            _context.OutcomeFeePlanVersions.Add(version);
            plan.CurrentVersionId = version.Id;

            foreach (var assumption in BuildAssumptions(normalized, generated, policy))
            {
                _context.OutcomeFeeAssumptions.Add(new OutcomeFeeAssumption
                {
                    PlanVersionId = version.Id,
                    Category = assumption.Category,
                    Key = assumption.Key,
                    ValueType = assumption.ValueType,
                    ValueJson = SerializeJson(assumption.Value),
                    SourceType = assumption.SourceType,
                    SourceRef = assumption.SourceRef,
                    Notes = assumption.Notes,
                    CreatedAt = now,
                    MetadataJson = null
                });
            }

            foreach (var scenario in generated.scenarios)
            {
                var activeCalibration = calibrationOverlay?.Active;
                var shadowCalibration = calibrationOverlay?.Shadow;
                var activeHoursMult = activeCalibration == null ? 1m : Round2(Math.Clamp(activeCalibration.GlobalHoursMultiplier, 0.65m, 1.75m));
                var activeCollectionsMult = activeCalibration == null ? 1m : Round2(Math.Clamp(activeCalibration.GlobalCollectionsMultiplier, 0.60m, 1.30m));
                var calibratedBudgetTotal = Round2(scenario.budgetTotal * activeHoursMult);
                var calibratedExpectedCost = Round2(scenario.expectedCost * (1m + ((activeHoursMult - 1m) * 0.85m)));
                var calibratedExpectedCollected = Round2(scenario.expectedCollected * activeCollectionsMult);
                var calibratedExpectedMargin = Round2(calibratedExpectedCollected - calibratedExpectedCost);

                var totalScenarioHours = Round2(scenario.phases.Sum(p => p.Hours));
                var staffingRowsFlat = scenario.phases
                    .SelectMany(p => p.Staffing.Select(s => new
                    {
                        phaseCode = p.Code,
                        phaseName = p.Name,
                        s.Role,
                        s.Hours,
                        s.BillRate,
                        s.CostRate,
                        s.Risk
                    }))
                    .ToList();
                var partnerHours = Round2(staffingRowsFlat.Where(s => s.Role == "partner").Sum(s => s.Hours));
                var associateHours = Round2(staffingRowsFlat.Where(s => s.Role == "associate").Sum(s => s.Hours));
                var paralegalHours = Round2(staffingRowsFlat.Where(s => s.Role == "paralegal").Sum(s => s.Hours));
                var blendedBillRate = totalScenarioHours <= 0m ? 0m : Round2(staffingRowsFlat.Sum(s => s.Hours * s.BillRate) / totalScenarioHours);
                var blendedCostRate = totalScenarioHours <= 0m ? 0m : Round2(staffingRowsFlat.Sum(s => s.Hours * s.CostRate) / totalScenarioHours);
                var avgUtilizationRisk = staffingRowsFlat.Count == 0 ? 0m : Round2(staffingRowsFlat.Average(s => s.Risk));

                var primarySegmentWeight = normalized.PrimaryPayorProfile switch
                {
                    "third_party" => 0.55m,
                    "corporate" => 0.75m,
                    _ => 0.85m
                };
                var splitRemainder = Round2(1m - primarySegmentWeight);
                var payorSegments = normalized.PrimaryPayorProfile switch
                {
                    "third_party" => new object[]
                    {
                        new { segment = "third_party", weight = Round2(primarySegmentWeight), collectionAdjustment = -0.05m },
                        new { segment = "client", weight = Round2(splitRemainder), collectionAdjustment = -0.01m }
                    },
                    "corporate" => new object[]
                    {
                        new { segment = "corporate", weight = Round2(primarySegmentWeight), collectionAdjustment = 0.03m },
                        new { segment = "client", weight = Round2(splitRemainder), collectionAdjustment = -0.01m }
                    },
                    _ => new object[]
                    {
                        new { segment = "client", weight = Round2(primarySegmentWeight), collectionAdjustment = 0.00m },
                        new { segment = "third_party", weight = Round2(splitRemainder), collectionAdjustment = -0.04m }
                    }
                };

                var paymentRailImpact = new
                {
                    card = new { collectionSpeedAdj = 0.03m, feeCostAdj = -0.018m, expectedUsageWeight = 0.40m },
                    ach = new { collectionSpeedAdj = 0.02m, feeCostAdj = -0.004m, expectedUsageWeight = 0.45m },
                    echeck = new { collectionSpeedAdj = 0.01m, feeCostAdj = -0.003m, expectedUsageWeight = 0.15m }
                };
                var trustFundedWeight = normalized.BillingArrangement switch
                {
                    "hourly" => 0.30m,
                    "hybrid" => 0.22m,
                    "fixed" => 0.12m,
                    "contingency" => 0.05m,
                    _ => 0.10m
                };
                var operatingFundedWeight = Round2(1m - trustFundedWeight);
                var handoffCostHours = Round2(totalScenarioHours * (scenario.key == "conservative" ? 0.05m : scenario.key == "aggressive" ? 0.025m : 0.035m));
                var handoffCostAmount = Round2(handoffCostHours * Math.Max(1m, blendedCostRate));

                var grossMargin = scenario.budgetTotal <= 0m ? 0m : Round2((scenario.budgetTotal - scenario.expectedCost) / scenario.budgetTotal);
                var collectedMarginRatio = scenario.expectedCollected <= 0m ? 0m : Round2(scenario.expectedMargin / scenario.expectedCollected);
                var blendedRateRealization = scenario.budgetTotal <= 0m ? 0m : Round2((scenario.expectedCollected - (scenario.budgetTotal * 0.08m)) / Math.Max(1m, scenario.budgetTotal));
                var writeOffRisk = Round2(Math.Clamp(1m - (scenario.expectedCollected / Math.Max(1m, scenario.budgetTotal)), 0m, 1m));
                var prebillAdjustmentRisk = Round2(Math.Clamp(avgUtilizationRisk * 0.35m + (normalized.BillingArrangement == "contingency" ? 0.20m : 0.05m), 0m, 1m));

                var stressTests = new object[]
                {
                    new { key = "delayed_payment", impactArea = "collections", deltaCollected = Round2(-scenario.expectedCollected * 0.08m), deltaMargin = Round2(-scenario.expectedMargin * 0.06m), note = "Primary receivables shift one bucket later." },
                    new { key = "extra_motion_cycle", impactArea = "hours", deltaHours = Round2(totalScenarioHours * 0.12m), deltaCost = Round2(scenario.expectedCost * 0.09m), deltaMargin = Round2(-scenario.expectedMargin * 0.11m), note = "Unexpected motion practice round." },
                    new { key = "discovery_overrun", impactArea = "hours", deltaHours = Round2(totalScenarioHours * 0.18m), deltaCost = Round2(scenario.expectedCost * 0.13m), deltaMargin = Round2(-scenario.expectedMargin * 0.15m), note = "Discovery volume exceeds expected range." },
                    new { key = "trial_date_shift", impactArea = "staffing", deltaDays = 21, deltaCost = Round2(scenario.expectedCost * 0.05m), deltaMargin = Round2(-scenario.expectedMargin * 0.04m), note = "Trial preparation idle/restart cost." }
                };

                var phase4Metadata = new
                {
                    collectionsIntelligence = new
                    {
                        payorSegments,
                        paymentRailImpact,
                        trustFundingBehavior = new
                        {
                            trustFundedWeight = Round2(trustFundedWeight),
                            operatingFundedWeight,
                            guidance = trustFundedWeight >= 0.25m ? "Elevated trust-funded dependency; monitor trust replenishment timing." : "Operating-funded collections dominate."
                        }
                    },
                    staffingIntelligence = new
                    {
                        utilizationAwareSuggestions = new object[]
                        {
                            new { role = "partner", suggestedHoursDelta = Round2(partnerHours > totalScenarioHours * 0.35m ? -partnerHours * 0.10m : 0m), reason = "Reduce partner-heavy execution to protect margin." },
                            new { role = "associate", suggestedHoursDelta = Round2(totalScenarioHours * 0.06m), reason = "Absorb drafting/research volume for blended realization." },
                            new { role = "paralegal", suggestedHoursDelta = Round2(totalScenarioHours * 0.03m), reason = "Shift process/admin work to lower-cost role." }
                        },
                        skillRoleConstraints = new object[]
                        {
                            new { role = "partner", minShare = 0.15m, maxShare = 0.40m, requiredFor = new[] { "motion", "trial_prep", "trial" } },
                            new { role = "associate", minShare = 0.35m, maxShare = 0.70m, requiredFor = new[] { "pleading", "discovery", "motion" } },
                            new { role = "paralegal", minShare = 0.10m, maxShare = 0.30m, requiredFor = new[] { "intake", "discovery", "settlement" } }
                        },
                        handoffCost = new
                        {
                            expectedHours = handoffCostHours,
                            expectedCost = handoffCostAmount,
                            risk = avgUtilizationRisk >= 0.55m ? "elevated" : "normal"
                        },
                        blendedRates = new { bill = blendedBillRate, cost = blendedCostRate }
                    },
                    marginIntelligence = new
                    {
                        blendedRateRealization,
                        grossMargin,
                        collectedMargin = collectedMarginRatio,
                        writeOffRisk,
                        prebillAdjustmentRisk,
                        payorMixEffect = normalized.PrimaryPayorProfile == "third_party" ? -0.04m : normalized.PrimaryPayorProfile == "corporate" ? 0.03m : 0m
                    },
                    stressTests
                };
                var scenarioMetadataJson = MergeJson(SerializeJson(scenario.metadata), phase4Metadata);

                var scenarioRow = new OutcomeFeeScenario
                {
                    PlanVersionId = version.Id,
                    ScenarioKey = scenario.key,
                    Name = scenario.name,
                    Probability = scenario.probability,
                    Currency = normalized.Currency,
                    BudgetTotal = calibratedBudgetTotal,
                    ExpectedCollected = calibratedExpectedCollected,
                    ExpectedCost = calibratedExpectedCost,
                    ExpectedMargin = calibratedExpectedMargin,
                    ConfidenceScore = scenario.confidence,
                    Status = "active",
                    DriverSummary = scenario.driverSummary,
                    MetadataJson = MergeJson(scenarioMetadataJson, new
                    {
                        calibration = new
                        {
                            activeApplied = calibrationOverlay?.ActiveSnapshot == null ? null : new
                            {
                                snapshotId = calibrationOverlay.ActiveSnapshot.Id,
                                cohortKey = calibrationOverlay.ActiveSnapshot.CohortKey,
                                status = calibrationOverlay.ActiveSnapshot.Status,
                                globalHoursMultiplier = activeHoursMult,
                                globalCollectionsMultiplier = activeCollectionsMult
                            },
                            shadowCandidate = calibrationOverlay?.ShadowSnapshot == null ? null : new
                            {
                                snapshotId = calibrationOverlay.ShadowSnapshot.Id,
                                cohortKey = calibrationOverlay.ShadowSnapshot.CohortKey,
                                status = calibrationOverlay.ShadowSnapshot.Status,
                                recommendedShadowMode = shadowCalibration?.RecommendedShadowMode ?? false
                            },
                            candidateCohorts = calibrationOverlay?.CandidateCohorts
                        }
                    }),
                    CreatedAt = now,
                    UpdatedAt = now
                };
                _context.OutcomeFeeScenarios.Add(scenarioRow);

                foreach (var phase in scenario.phases)
                {
                    var phaseHoursMult = activeCalibration != null && activeCalibration.PhaseHourMultipliers.TryGetValue(phase.Code, out var phaseCalMult)
                        ? Round2(Math.Clamp(phaseCalMult, 0.70m, 1.50m))
                        : activeHoursMult;
                    var phaseHoursExpected = Round2(phase.Hours * phaseHoursMult);
                    var phaseFeeExpected = Round2(phase.Fee * phaseHoursMult);
                    var phaseExpenseExpected = Round2(phase.Expenses * Math.Max(0.70m, Math.Min(1.35m, 1m + ((phaseHoursMult - 1m) * 0.60m))));
                    var phaseDurationExpected = Math.Max(1, (int)Math.Round(phase.Days * Math.Max(0.80m, Math.Min(1.35m, 1m + ((phaseHoursMult - 1m) * 0.50m)))));

                    var phaseRow = new OutcomeFeePhaseForecast
                    {
                        ScenarioId = scenarioRow.Id,
                        PhaseOrder = phase.Order,
                        PhaseCode = phase.Code,
                        Name = phase.Name,
                        HoursExpected = phaseHoursExpected,
                        FeeExpected = phaseFeeExpected,
                        ExpenseExpected = phaseExpenseExpected,
                        DurationDaysExpected = phaseDurationExpected,
                        Status = "planned",
                        MetadataJson = SerializeJson(new
                        {
                            phase.Share,
                            calibration = new
                            {
                                appliedMultiplier = phaseHoursMult,
                                activeSnapshotId = calibrationOverlay?.ActiveSnapshot?.Id,
                                shadowSnapshotId = calibrationOverlay?.ShadowSnapshot?.Id
                            },
                            hoursRange = BuildRangeMetadata(phase.Hours, scenario.key, 0.82m, 1.22m),
                            feeRange = BuildRangeMetadata(phase.Fee, scenario.key, 0.84m, 1.20m),
                            durationRangeDays = BuildDurationRangeMetadata(phase.Days, scenario.key)
                        }),
                        CreatedAt = now,
                        UpdatedAt = now
                    };
                    _context.OutcomeFeePhaseForecasts.Add(phaseRow);

                    foreach (var staffing in phase.Staffing)
                    {
                        var staffingHours = Round2(staffing.Hours * phaseHoursMult);
                        var staffingFee = Round2(staffing.Fee * phaseHoursMult);
                        var staffingCost = Round2(staffing.Cost * phaseHoursMult);
                        _context.OutcomeFeeStaffingLines.Add(new OutcomeFeeStaffingLine
                        {
                            ScenarioId = scenarioRow.Id,
                            PhaseForecastId = phaseRow.Id,
                            Role = staffing.Role,
                            HoursExpected = staffingHours,
                            BillRate = staffing.BillRate,
                            CostRate = staffing.CostRate,
                            FeeExpected = staffingFee,
                            CostExpected = staffingCost,
                            UtilizationRiskScore = staffing.Risk,
                            MetadataJson = SerializeJson(new { calibrationMultiplier = phaseHoursMult }),
                            CreatedAt = now,
                            UpdatedAt = now
                        });
                    }
                }

                foreach (var col in scenario.collections)
                {
                    var curveShift = activeCalibration != null && activeCalibration.CollectionCurveShifts.TryGetValue(col.BucketDays, out var shift)
                        ? shift
                        : 0m;
                    var adjustedProbability = Round2(Math.Clamp(col.Probability + curveShift, 0.01m, 0.99m));
                    var adjustedAmount = Round2(col.Amount * activeCollectionsMult);
                    _context.OutcomeFeeCollectionsForecasts.Add(new OutcomeFeeCollectionsForecast
                    {
                        ScenarioId = scenarioRow.Id,
                        PayorSegment = normalized.PrimaryPayorProfile,
                        BucketDays = col.BucketDays,
                        ExpectedAmount = adjustedAmount,
                        CollectionProbability = adjustedProbability,
                        Status = "planned",
                        MetadataJson = SerializeJson(new
                        {
                            source = "probabilistic_curve_v1",
                            scenarioKey = scenario.key,
                            payorSegments,
                            paymentRailImpact,
                            trustFundingBehavior = new { trustFundedWeight = Round2(trustFundedWeight), operatingFundedWeight },
                            calibration = new
                            {
                                curveShift,
                                activeSnapshotId = calibrationOverlay?.ActiveSnapshot?.Id,
                                shadowSnapshotId = calibrationOverlay?.ShadowSnapshot?.Id
                            },
                            curveType = scenario.key switch
                            {
                                "conservative" => "slow_collect",
                                "aggressive" => "fast_collect",
                                _ => "baseline_collect"
                            }
                        }),
                        CreatedAt = now,
                        UpdatedAt = now
                    });
                }
            }

            _context.OutcomeFeeUpdateEvents.Add(new OutcomeFeeUpdateEvent
            {
                PlanId = plan.Id,
                TriggerType = triggerType,
                TriggerEntityType = "Matter",
                TriggerEntityId = matter.Id,
                AppliedVersionId = version.Id,
                Status = "applied",
                CorrelationId = version.CorrelationId,
                TriggeredBy = userId,
                PayloadJson = SerializeJson(new { request.Complexity, request.ClaimSizeBand, request.BillingArrangement, request.PrimaryPayorProfile }),
                ResultJson = SerializeJson(new { versionId = version.Id, version.VersionNumber }),
                MetadataJson = SerializeJson(new { mode = "manual" }),
                CreatedAt = now,
                AppliedAt = now
            });
        }

        private static (decimal baseHours, decimal rate, List<string> warnings, List<(string key, string name, decimal probability, decimal budgetTotal, decimal expectedCollected, decimal expectedCost, decimal expectedMargin, decimal confidence, string driverSummary, object metadata, List<(int Order, string Code, string Name, decimal Share, decimal Hours, decimal Fee, decimal Expenses, int Days, List<(string Role, decimal Hours, decimal BillRate, decimal CostRate, decimal Fee, decimal Cost, decimal Risk)> Staffing)> phases, List<(int BucketDays, decimal Probability, decimal Amount)> collections)> scenarios)
            BuildDeterministicForecast(OutcomeFeeInputs input)
        {
            var warnings = new List<string>();
            var complexityMultiplier = input.Complexity switch { "low" => 0.75m, "high" => 1.35m, _ => 1.00m };
            var claimSizeMultiplier = input.ClaimSizeBand switch { "small" => 0.70m, "large" => 1.25m, "enterprise" => 1.60m, _ => 1.00m };
            var courtMultiplier = !string.IsNullOrWhiteSpace(input.CourtType) && input.CourtType!.Contains("federal", StringComparison.OrdinalIgnoreCase) ? 1.20m : 1.00m;
            var arrangementMultiplier = input.BillingArrangement switch { "fixed" => 0.90m, "hybrid" => 1.05m, "contingency" => 1.10m, _ => 1.00m };

            var rate = input.BaseBillableRate > 0m ? input.BaseBillableRate : 275m;
            if (input.BaseBillableRate <= 0m) warnings.Add("Matter.BillableRate missing; default billable rate applied.");
            rate = Round2(rate);
            var baseHours = Round2(120m * complexityMultiplier * claimSizeMultiplier * courtMultiplier * arrangementMultiplier);

            var scenarios = new List<(string, string, decimal, decimal, decimal, decimal, decimal, decimal, string, object, List<(int, string, string, decimal, decimal, decimal, decimal, int, List<(string, decimal, decimal, decimal, decimal, decimal, decimal)>)>, List<(int, decimal, decimal)>)>();
            var defs = new[]
            {
                (key:"conservative", name:"Conservative", probability:0.25m, hoursMult:1.15m, collectMult:0.84m, confidence:0.60m),
                (key:"base", name:"Base", probability:0.50m, hoursMult:1.00m, collectMult:0.90m, confidence:0.72m),
                (key:"aggressive", name:"Aggressive", probability:0.25m, hoursMult:0.85m, collectMult:0.95m, confidence:0.66m)
            };

            foreach (var def in defs)
            {
                var totalHours = Round2(baseHours * def.hoursMult);
                var phaseRows = new List<(int, string, string, decimal, decimal, decimal, decimal, int, List<(string, decimal, decimal, decimal, decimal, decimal, decimal)>)>();
                decimal totalFees = 0m;
                decimal totalExpenses = 0m;
                decimal totalCosts = 0m;

                foreach (var phase in PhaseTemplate)
                {
                    var phaseHours = Round2(totalHours * phase.Share);
                    var blendedRate = Round2(rate);
                    var fee = Round2(phaseHours * blendedRate);
                    var expenses = Round2(fee * 0.08m);
                    totalFees += fee;
                    totalExpenses += expenses;

                    var staffing = BuildStaffingRows(phaseHours, rate);
                    totalCosts += staffing.Sum(s => s.Cost);
                    phaseRows.Add((phase.Order, phase.Code, phase.Name, phase.Share, phaseHours, fee, expenses, phase.Days, staffing));
                }

                var budgetTotal = Round2(totalFees + totalExpenses);
                var payorAdj = input.PrimaryPayorProfile switch { "corporate" => 0.03m, "third_party" => -0.04m, _ => 0m };
                var collectedRatio = Math.Clamp(def.collectMult + payorAdj, 0.50m, 0.99m);
                var expectedCollected = Round2(budgetTotal * collectedRatio);
                var expectedCost = Round2(totalCosts + (totalExpenses * 0.55m));
                var expectedMargin = Round2(expectedCollected - expectedCost);
                var curve = BuildCollectionCurve(expectedCollected, def.key);
                var grossMargin = Round2(totalFees - totalCosts);
                var dataCoverageScore = CalculateDataCoverageScore(input);
                var confidenceScore = Round2(Math.Clamp(def.confidence * 0.75m + dataCoverageScore * 0.25m, 0m, 1m));
                var confidenceBand = MapConfidenceBand(confidenceScore);
                var outcomeProbabilities = BuildOutcomeProbabilities(input, def.key);
                var topDrivers = BuildTopDrivers(input, complexityMultiplier, claimSizeMultiplier, courtMultiplier, arrangementMultiplier, payorAdj);
                var sensitivity = BuildSensitivitySummary(input, complexityMultiplier, claimSizeMultiplier, courtMultiplier, arrangementMultiplier, payorAdj);
                var riskFlags = BuildRiskFlags(input, collectedRatio, dataCoverageScore, confidenceScore, payorAdj, def.key);

                scenarios.Add((def.key, def.name, def.probability, budgetTotal, expectedCollected, expectedCost, expectedMargin, confidenceScore,
                    $"{input.Complexity} complexity, {input.ClaimSizeBand} claim size, {input.BillingArrangement} arrangement, {input.PrimaryPayorProfile} payor",
                    new
                    {
                        totalHours,
                        collectedRatio,
                        complexityMultiplier,
                        claimSizeMultiplier,
                        courtMultiplier,
                        arrangementMultiplier,
                        outcomeProbabilities,
                        confidenceBand,
                        dataCoverageScore,
                        topDrivers,
                        inputSensitivitySummary = sensitivity,
                        riskFlags,
                        margin = new
                        {
                            grossMargin,
                            collectedMargin = expectedMargin,
                            payorMixEffect = Round2(payorAdj),
                            expectedCost
                        },
                        explainability = new
                        {
                            whyThisScenario = $"{def.name} scenario uses {def.hoursMult:0.##}x hours and {def.collectMult:0.##}x collections baseline with {input.PrimaryPayorProfile} payor mix adjustment.",
                            driverOrder = topDrivers.Select(d => d.Key).ToArray()
                        }
                    },
                    phaseRows,
                    curve));
            }

            return (baseHours, rate, warnings, scenarios);
        }

        private static List<(string Role, decimal Hours, decimal BillRate, decimal CostRate, decimal Fee, decimal Cost, decimal Risk)> BuildStaffingRows(decimal phaseHours, decimal baseRate)
        {
            var rows = new List<(string, decimal, decimal, decimal, decimal, decimal, decimal)>();
            var defs = new[]
            {
                (role:"partner", share:0.25m, billMult:1.70m, costMult:0.55m, risk:0.70m),
                (role:"associate", share:0.55m, billMult:1.00m, costMult:0.40m, risk:0.40m),
                (role:"paralegal", share:0.20m, billMult:0.45m, costMult:0.28m, risk:0.25m)
            };
            foreach (var def in defs)
            {
                var hours = Round2(phaseHours * def.share);
                var billRate = Round2(baseRate * def.billMult);
                var costRate = Round2(baseRate * def.costMult);
                rows.Add((def.role, hours, billRate, costRate, Round2(hours * billRate), Round2(hours * costRate), def.risk));
            }
            return rows;
        }

        private static List<(int BucketDays, decimal Probability, decimal Amount)> BuildCollectionCurve(decimal expectedCollected, string scenarioKey)
        {
            var curve = scenarioKey switch
            {
                "conservative" => new[] { (30, 0.35m), (60, 0.25m), (90, 0.20m), (120, 0.20m) },
                "aggressive" => new[] { (30, 0.55m), (60, 0.25m), (90, 0.15m), (120, 0.05m) },
                _ => new[] { (30, 0.45m), (60, 0.28m), (90, 0.17m), (120, 0.10m) }
            };
            return curve.Select(c => (c.Item1, c.Item2, Round2(expectedCollected * c.Item2))).ToList();
        }

        private static decimal CalculateDataCoverageScore(OutcomeFeeInputs input)
        {
            decimal score = 0.35m; // baseline
            if (!string.IsNullOrWhiteSpace(input.JurisdictionCode)) score += 0.15m;
            if (!string.IsNullOrWhiteSpace(input.CourtType)) score += 0.15m;
            if (!string.IsNullOrWhiteSpace(input.PracticeArea)) score += 0.10m;
            if (input.BaseBillableRate > 0m) score += 0.10m;
            if (!string.IsNullOrWhiteSpace(input.ClientId)) score += 0.05m;
            if (!string.IsNullOrWhiteSpace(input.MatterName)) score += 0.05m;
            if (input.BillingArrangement is "hourly" or "fixed" or "hybrid" or "contingency") score += 0.05m;
            return Round2(Math.Clamp(score, 0m, 1m));
        }

        private static string MapConfidenceBand(decimal confidenceScore)
        {
            if (confidenceScore >= 0.80m) return "high";
            if (confidenceScore >= 0.60m) return "medium";
            return "low";
        }

        private static object BuildOutcomeProbabilities(OutcomeFeeInputs input, string scenarioKey)
        {
            var settle = scenarioKey switch { "conservative" => 0.42m, "aggressive" => 0.62m, _ => 0.54m };
            var dismiss = scenarioKey switch { "conservative" => 0.12m, "aggressive" => 0.10m, _ => 0.11m };
            var trial = scenarioKey switch { "conservative" => 0.28m, "aggressive" => 0.18m, _ => 0.22m };
            var adverse = scenarioKey switch { "conservative" => 0.18m, "aggressive" => 0.10m, _ => 0.13m };

            if (!string.IsNullOrWhiteSpace(input.CourtType) && input.CourtType.Contains("Federal", StringComparison.OrdinalIgnoreCase))
            {
                trial = Round2(trial + 0.04m);
                settle = Round2(Math.Max(0m, settle - 0.03m));
                adverse = Round2(adverse + 0.01m);
            }

            if (input.ClaimSizeBand == "enterprise")
            {
                trial = Round2(trial + 0.03m);
                settle = Round2(Math.Max(0m, settle - 0.02m));
            }

            var total = settle + dismiss + trial + adverse;
            if (total <= 0m) total = 1m;

            return new
            {
                settle = Round2(settle / total),
                dismiss = Round2(dismiss / total),
                trial = Round2(trial / total),
                adverse = Round2(adverse / total)
            };
        }

        private static List<OutcomeFeeDriverSignal> BuildTopDrivers(
            OutcomeFeeInputs input,
            decimal complexityMultiplier,
            decimal claimSizeMultiplier,
            decimal courtMultiplier,
            decimal arrangementMultiplier,
            decimal payorAdj)
        {
            return new List<OutcomeFeeDriverSignal>
            {
                new OutcomeFeeDriverSignal { Key = "complexity", Label = "Complexity", Impact = Round2(complexityMultiplier - 1m), Value = input.Complexity },
                new OutcomeFeeDriverSignal { Key = "claim_size_band", Label = "Claim Size", Impact = Round2(claimSizeMultiplier - 1m), Value = input.ClaimSizeBand },
                new OutcomeFeeDriverSignal { Key = "court_type", Label = "Court Type", Impact = Round2(courtMultiplier - 1m), Value = input.CourtType ?? "unspecified" },
                new OutcomeFeeDriverSignal { Key = "billing_arrangement", Label = "Billing Arrangement", Impact = Round2(arrangementMultiplier - 1m), Value = input.BillingArrangement },
                new OutcomeFeeDriverSignal { Key = "payor_mix", Label = "Payor Mix", Impact = Round2(payorAdj), Value = input.PrimaryPayorProfile }
            }
            .OrderByDescending(x => Math.Abs(x.Impact))
            .ToList();
        }

        private static object BuildSensitivitySummary(
            OutcomeFeeInputs input,
            decimal complexityMultiplier,
            decimal claimSizeMultiplier,
            decimal courtMultiplier,
            decimal arrangementMultiplier,
            decimal payorAdj)
        {
            var strongest = new[]
            {
                new { key = "complexity", delta = Math.Abs(complexityMultiplier - 1m) },
                new { key = "claim_size_band", delta = Math.Abs(claimSizeMultiplier - 1m) },
                new { key = "court_type", delta = Math.Abs(courtMultiplier - 1m) },
                new { key = "billing_arrangement", delta = Math.Abs(arrangementMultiplier - 1m) },
                new { key = "payor_mix", delta = Math.Abs(payorAdj) }
            }
            .OrderByDescending(x => x.delta)
            .Take(3)
            .Select(x => x.key)
            .ToArray();

            return new
            {
                strongestDrivers = strongest,
                notes = $"{input.BillingArrangement} arrangement and {input.PrimaryPayorProfile} payor mix drive collections and margin sensitivity."
            };
        }

        private static List<string> BuildRiskFlags(
            OutcomeFeeInputs input,
            decimal collectedRatio,
            decimal dataCoverageScore,
            decimal confidenceScore,
            decimal payorAdj,
            string scenarioKey)
        {
            var flags = new List<string>();

            if (dataCoverageScore < 0.60m) flags.Add("low_data_coverage");
            if (confidenceScore < 0.60m) flags.Add("low_confidence");
            if (string.IsNullOrWhiteSpace(input.JurisdictionCode)) flags.Add("jurisdiction_gap");
            if (input.PrimaryPayorProfile == "third_party" || collectedRatio < 0.86m) flags.Add("high_collections_risk");
            if (input.ClaimSizeBand == "enterprise" || (input.Complexity == "high" && scenarioKey == "conservative")) flags.Add("atypical_matter");
            if (input.BillingArrangement == "contingency") flags.Add("realization_variability");
            if (payorAdj < 0m) flags.Add("payor_mix_margin_pressure");

            return flags.Distinct(StringComparer.Ordinal).ToList();
        }

        private static IEnumerable<(string Category, string Key, string ValueType, object? Value, string SourceType, string? SourceRef, string? Notes)> BuildAssumptions(
            OutcomeFeeInputs input,
            (decimal baseHours, decimal rate, List<string> warnings, List<(string key, string name, decimal probability, decimal budgetTotal, decimal expectedCollected, decimal expectedCost, decimal expectedMargin, decimal confidence, string driverSummary, object metadata, List<(int Order, string Code, string Name, decimal Share, decimal Hours, decimal Fee, decimal Expenses, int Days, List<(string Role, decimal Hours, decimal BillRate, decimal CostRate, decimal Fee, decimal Cost, decimal Risk)> Staffing)> phases, List<(int BucketDays, decimal Probability, decimal Amount)> collections)> scenarios) generated,
            MatterBillingPolicy? policy)
        {
            yield return ("intake_input", "complexity", "string", input.Complexity, "user_override", input.MatterId, null);
            yield return ("intake_input", "claim_size_band", "string", input.ClaimSizeBand, "user_override", input.MatterId, null);
            yield return ("intake_input", "billing_arrangement", "string", input.BillingArrangement, "matter_or_policy", policy?.Id ?? input.MatterId, null);
            yield return ("intake_input", "primary_payor_profile", "string", input.PrimaryPayorProfile, "client", input.ClientId, null);
            yield return ("staffing", "base_billable_rate", "decimal", generated.rate, "matter", input.MatterId, null);
            yield return ("phase_template", "phase_template_v1", "json", PhaseTemplate.Select(p => new { p.Order, p.Code, p.Share, p.Days }).ToList(), "engine_default", null, null);
            yield return ("engine", "base_hours", "decimal", generated.baseHours, "engine_default", null, null);
            yield return ("probabilistic", "scenario_probabilities_v1", "json",
                generated.scenarios.Select(s => new { s.key, s.probability }).ToList(),
                "engine_default", null, "Conservative/Base/Aggressive scenario prior probabilities");
            yield return ("probabilistic", "confidence_model_v1", "json",
                new { coverageWeight = 0.25m, scenarioWeight = 0.75m, bands = new { low = "<0.60", medium = "0.60-0.79", high = ">=0.80" } },
                "engine_default", null, "Confidence band and weighting assumptions");
            yield return ("collections_intelligence", "payor_rail_trust_behavior_v1", "json",
                new
                {
                    payorSegments = new[] { "client", "corporate", "third_party" },
                    paymentRails = new[] { "card", "ach", "echeck" },
                    trustFundingBehavior = "heuristic_v1"
                },
                "engine_default", null, "Phase 4 collections intelligence overlay assumptions");
            yield return ("staffing_intelligence", "utilization_handoff_v1", "json",
                new { utilizationAwareSuggestions = true, handoffCostModel = "heuristic_v1", skillRoleConstraints = "template_v1" },
                "engine_default", null, "Phase 4 staffing intelligence overlay assumptions");
            yield return ("margin_intelligence", "realization_writeoff_prebill_risk_v1", "json",
                new { blendedRateRealization = "heuristic_v1", writeOffRisk = "heuristic_v1", prebillAdjustmentRisk = "heuristic_v1" },
                "engine_default", null, "Phase 4 margin intelligence overlay assumptions");
            if (generated.warnings.Count > 0)
            {
                yield return ("engine", "warnings", "json", generated.warnings, "engine_default", null, "Generation warnings");
            }
        }

        private async Task<MatterBillingPolicy?> GetActiveMatterBillingPolicyAsync(string matterId, DateTime asOfUtc, CancellationToken ct)
        {
            return await _context.MatterBillingPolicies
                .Where(p => p.MatterId == matterId &&
                            p.Status == "active" &&
                            p.EffectiveFrom <= asOfUtc &&
                            (!p.EffectiveTo.HasValue || p.EffectiveTo.Value >= asOfUtc))
                .OrderByDescending(p => p.EffectiveFrom)
                .ThenByDescending(p => p.UpdatedAt)
                .FirstOrDefaultAsync(ct);
        }

        private static OutcomeFeeInputs NormalizeInputs(Matter matter, Client? client, MatterBillingPolicy? policy, OutcomeFeePlanCreateRequest request)
        {
            var arrangement = (request.BillingArrangement ?? policy?.ArrangementType ?? matter.FeeStructure ?? "hourly").Trim().ToLowerInvariant();
            if (arrangement is not ("hourly" or "fixed" or "hybrid" or "contingency")) arrangement = "hourly";

            var complexity = NormalizeEnum(request.Complexity, "medium", "low", "medium", "high");
            var claimSizeBand = NormalizeEnum(request.ClaimSizeBand, "medium", "small", "medium", "large", "enterprise");
            var payor = NormalizeEnum(request.PrimaryPayorProfile,
                string.Equals(client?.Type, "Corporate", StringComparison.OrdinalIgnoreCase) ? "corporate" : "client",
                "client", "corporate", "third_party");

            var currency = string.IsNullOrWhiteSpace(request.Currency) ? (policy?.Currency ?? "USD") : request.Currency!.Trim().ToUpperInvariant();
            var rate = request.BaseBillableRateOverride;
            if (!rate.HasValue || rate.Value <= 0m)
            {
                rate = matter.BillableRate > 0 ? Convert.ToDecimal(matter.BillableRate, CultureInfo.InvariantCulture) : 0m;
            }

            return new OutcomeFeeInputs
            {
                MatterId = matter.Id,
                ClientId = matter.ClientId,
                MatterName = matter.Name,
                PracticeArea = matter.PracticeArea,
                CourtType = matter.CourtType,
                JurisdictionCode = request.JurisdictionCode?.Trim(),
                Complexity = complexity,
                ClaimSizeBand = claimSizeBand,
                BillingArrangement = arrangement,
                PrimaryPayorProfile = payor,
                Currency = currency,
                BaseBillableRate = rate.GetValueOrDefault()
            };
        }

        private static string NormalizeEnum(string? raw, string fallback, params string[] allowed)
        {
            var normalized = string.IsNullOrWhiteSpace(raw) ? fallback : raw.Trim().ToLowerInvariant();
            return allowed.Contains(normalized, StringComparer.Ordinal) ? normalized : fallback;
        }

        private static object BuildRangeMetadata(decimal expected, string scenarioKey, decimal minFactor, decimal maxFactor)
        {
            var scenarioSpreadAdj = scenarioKey switch
            {
                "conservative" => 0.04m,
                "aggressive" => 0.02m,
                _ => 0.00m
            };
            var min = Round2(expected * Math.Max(0.10m, minFactor - scenarioSpreadAdj));
            var max = Round2(expected * (maxFactor + scenarioSpreadAdj));
            return new { min, expected = Round2(expected), max };
        }

        private static object BuildDurationRangeMetadata(int expectedDays, string scenarioKey)
        {
            var min = scenarioKey == "aggressive" ? (int)Math.Max(1, Math.Round(expectedDays * 0.80m)) : (int)Math.Max(1, Math.Round(expectedDays * 0.90m));
            var max = scenarioKey == "conservative" ? (int)Math.Max(min, Math.Round(expectedDays * 1.25m)) : (int)Math.Max(min, Math.Round(expectedDays * 1.15m));
            return new { minDays = min, expectedDays, maxDays = max };
        }

        private static string? GetMetadataString(object metadata, string key)
        {
            try
            {
                using var doc = JsonDocument.Parse(JsonSerializer.Serialize(metadata));
                if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
                return doc.RootElement.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.String
                    ? prop.GetString()
                    : null;
            }
            catch
            {
                return null;
            }
        }

        private static IReadOnlyList<string> GetMetadataStringList(object metadata, string key)
        {
            try
            {
                using var doc = JsonDocument.Parse(JsonSerializer.Serialize(metadata));
                if (doc.RootElement.ValueKind != JsonValueKind.Object) return Array.Empty<string>();
                if (!doc.RootElement.TryGetProperty(key, out var prop) || prop.ValueKind != JsonValueKind.Array)
                {
                    return Array.Empty<string>();
                }

                return prop.EnumerateArray()
                    .Where(v => v.ValueKind == JsonValueKind.String)
                    .Select(v => v.GetString())
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .Cast<string>()
                    .ToArray();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        private static decimal Round2(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

        private static string? SerializeJson(object? payload) => payload == null ? null : JsonSerializer.Serialize(payload);

        private static string? MergeJson(string? existingJson, object patch)
        {
            var payload = new Dictionary<string, object?>(StringComparer.Ordinal);
            if (!string.IsNullOrWhiteSpace(existingJson))
            {
                try
                {
                    using var doc = JsonDocument.Parse(existingJson);
                    if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var prop in doc.RootElement.EnumerateObject())
                        {
                            payload[prop.Name] = JsonSerializer.Deserialize<object>(prop.Value.GetRawText());
                        }
                    }
                }
                catch { }
            }

            using var patchDoc = JsonDocument.Parse(JsonSerializer.Serialize(patch));
            foreach (var prop in patchDoc.RootElement.EnumerateObject())
            {
                payload[prop.Name] = JsonSerializer.Deserialize<object>(prop.Value.GetRawText());
            }

            return JsonSerializer.Serialize(payload);
        }

        private sealed class OutcomeFeeInputs
        {
            public string MatterId { get; set; } = string.Empty;
            public string ClientId { get; set; } = string.Empty;
            public string? MatterName { get; set; }
            public string? PracticeArea { get; set; }
            public string? CourtType { get; set; }
            public string? JurisdictionCode { get; set; }
            public string Complexity { get; set; } = "medium";
            public string ClaimSizeBand { get; set; } = "medium";
            public string BillingArrangement { get; set; } = "hourly";
            public string PrimaryPayorProfile { get; set; } = "client";
            public string Currency { get; set; } = "USD";
            public decimal BaseBillableRate { get; set; }
        }

        private sealed class OutcomeFeeDriverSignal
        {
            public string Key { get; set; } = string.Empty;
            public string Label { get; set; } = string.Empty;
            public decimal Impact { get; set; }
            public string? Value { get; set; }
        }
    }
}
