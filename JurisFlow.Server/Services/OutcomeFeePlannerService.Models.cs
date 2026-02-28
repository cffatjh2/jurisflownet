namespace JurisFlow.Server.Services
{
    public class OutcomeFeePlanCreateRequest
    {
        public string MatterId { get; set; } = string.Empty;
        public string? Title { get; set; }
        public string? Complexity { get; set; } // low | medium | high
        public string? ClaimSizeBand { get; set; } // small | medium | large | enterprise
        public string? BillingArrangement { get; set; } // hourly | fixed | hybrid | contingency
        public string? PrimaryPayorProfile { get; set; } // client | corporate | third_party
        public string? JurisdictionCode { get; set; }
        public string? Currency { get; set; }
        public decimal? BaseBillableRateOverride { get; set; }
        public string? CorrelationId { get; set; }
        public string? Notes { get; set; }
        public object? PredictionSignal { get; set; }
        public object? RuleSignals { get; set; }
        public object? HistoricalCohortSignal { get; set; }
    }

    public class OutcomeFeePlanRecomputeRequest
    {
        public string? TriggerType { get; set; } = "manual_recompute";
        public string? TriggerEntityType { get; set; }
        public string? TriggerEntityId { get; set; }
        public string? Reason { get; set; }

        public string? Complexity { get; set; }
        public string? ClaimSizeBand { get; set; }
        public string? BillingArrangement { get; set; }
        public string? PrimaryPayorProfile { get; set; }
        public string? JurisdictionCode { get; set; }
        public string? Currency { get; set; }
        public decimal? BaseBillableRateOverride { get; set; }
        public string? CorrelationId { get; set; }
        public object? PredictionSignal { get; set; }
        public object? RuleSignals { get; set; }
        public object? HistoricalCohortSignal { get; set; }

        public OutcomeFeePlanCreateRequest ToCreateRequest()
        {
            return new OutcomeFeePlanCreateRequest
            {
                Complexity = Complexity,
                ClaimSizeBand = ClaimSizeBand,
                BillingArrangement = BillingArrangement,
                PrimaryPayorProfile = PrimaryPayorProfile,
                JurisdictionCode = JurisdictionCode,
                Currency = Currency,
                BaseBillableRateOverride = BaseBillableRateOverride,
                CorrelationId = CorrelationId,
                Notes = Reason,
                PredictionSignal = PredictionSignal,
                RuleSignals = RuleSignals,
                HistoricalCohortSignal = HistoricalCohortSignal
            };
        }
    }

    public class OutcomeFeePlanDetailResult
    {
        public JurisFlow.Server.Models.OutcomeFeePlan? Plan { get; set; }
        public JurisFlow.Server.Models.OutcomeFeePlanVersion? CurrentVersion { get; set; }
        public IReadOnlyList<JurisFlow.Server.Models.OutcomeFeePlanVersion> Versions { get; set; } = Array.Empty<JurisFlow.Server.Models.OutcomeFeePlanVersion>();
        public IReadOnlyList<JurisFlow.Server.Models.OutcomeFeeScenario> Scenarios { get; set; } = Array.Empty<JurisFlow.Server.Models.OutcomeFeeScenario>();
        public IReadOnlyList<JurisFlow.Server.Models.OutcomeFeePhaseForecast> PhaseForecasts { get; set; } = Array.Empty<JurisFlow.Server.Models.OutcomeFeePhaseForecast>();
        public IReadOnlyList<JurisFlow.Server.Models.OutcomeFeeStaffingLine> StaffingLines { get; set; } = Array.Empty<JurisFlow.Server.Models.OutcomeFeeStaffingLine>();
        public IReadOnlyList<JurisFlow.Server.Models.OutcomeFeeAssumption> Assumptions { get; set; } = Array.Empty<JurisFlow.Server.Models.OutcomeFeeAssumption>();
        public IReadOnlyList<JurisFlow.Server.Models.OutcomeFeeCollectionsForecast> CollectionsForecasts { get; set; } = Array.Empty<JurisFlow.Server.Models.OutcomeFeeCollectionsForecast>();
    }

    public class OutcomeFeePlanTriggerRequest
    {
        public string? MatterId { get; set; }
        public string? TriggerType { get; set; } = "manual_trigger";
        public string? TriggerEntityType { get; set; }
        public string? TriggerEntityId { get; set; }
        public string? Reason { get; set; }
        public string? SourceStatus { get; set; }
        public string? CorrelationId { get; set; }
        public bool AllowFullRecomputeFallback { get; set; } = true;
        public bool QueueReviewOnDrift { get; set; } = true;
        public bool QueueNotificationOnDrift { get; set; } = true;
        public decimal? HoursDriftThresholdRatio { get; set; }
        public decimal? CollectionsDriftThresholdRatio { get; set; }
        public decimal? MarginCompressionThresholdRatio { get; set; }
    }

    public class OutcomeFeePlanTriggerResult
    {
        public bool TriggerAccepted { get; set; }
        public bool Recomputed { get; set; }
        public bool DriftDetected { get; set; }
        public int ReviewItemsQueued { get; set; }
        public int NotificationsQueued { get; set; }
        public string? PlanId { get; set; }
        public string? MatterId { get; set; }
        public string? PreviousVersionId { get; set; }
        public string? CurrentVersionId { get; set; }
        public string? TriggerType { get; set; }
        public string? TriggerEntityType { get; set; }
        public string? TriggerEntityId { get; set; }
        public object? DriftSummary { get; set; }
        public OutcomeFeePlanVersionCompareResult? Compare { get; set; }
    }

    public class OutcomeFeePlanVersionCompareResult
    {
        public string PlanId { get; set; } = string.Empty;
        public string? MatterId { get; set; }
        public string? FromVersionId { get; set; }
        public string? ToVersionId { get; set; }
        public int? FromVersionNumber { get; set; }
        public int? ToVersionNumber { get; set; }
        public DateTime ComparedAtUtc { get; set; } = DateTime.UtcNow;
        public object? Actuals { get; set; }
        public object? DriftSummary { get; set; }
        public IReadOnlyList<object> ScenarioDeltas { get; set; } = Array.Empty<object>();
        public IReadOnlyList<object> PhaseDeltas { get; set; } = Array.Empty<object>();
    }

    public class OutcomeFeeOutcomeFeedbackRequest
    {
        public string? ActualOutcome { get; set; }
        public decimal? ActualFeesCollected { get; set; }
        public decimal? ActualCost { get; set; }
        public decimal? ActualMargin { get; set; }
        public DateTime? OutcomeDateUtc { get; set; }
        public string? Notes { get; set; }
        public string? CorrelationId { get; set; }
    }

    public class OutcomeFeeCalibrationJobRunRequest
    {
        public int Days { get; set; } = 365;
        public int MinSampleSize { get; set; } = 5;
        public bool ShadowMode { get; set; } = true;
        public bool AutoActivateHighConfidence { get; set; } = false;
        public decimal AutoActivateConfidenceThreshold { get; set; } = 0.80m;
        public string[]? CohortScopes { get; set; } // combined | practice_court_arrangement | practice_arrangement | attorney_arrangement | clienttype_arrangement
        public string? CorrelationId { get; set; }
        public string? Notes { get; set; }
    }

    public class OutcomeFeeCalibrationSnapshotActionRequest
    {
        public bool AsShadow { get; set; } = false;
        public string? Reason { get; set; }
        public string? CorrelationId { get; set; }
    }

    public class OutcomeFeeCalibrationRollbackRequest
    {
        public string? TargetSnapshotId { get; set; }
        public string? Reason { get; set; }
        public string? CorrelationId { get; set; }
    }
}
