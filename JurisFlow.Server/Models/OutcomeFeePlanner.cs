using System.ComponentModel.DataAnnotations;

namespace JurisFlow.Server.Models
{
    public class OutcomeFeePlan
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(128)]
        public string MatterId { get; set; } = string.Empty;

        [MaxLength(128)]
        public string? ClientId { get; set; }

        [MaxLength(128)]
        public string? MatterBillingPolicyId { get; set; }

        [MaxLength(128)]
        public string? CurrentVersionId { get; set; }

        [MaxLength(32)]
        public string PlannerMode { get; set; } = "deterministic_v1"; // deterministic_v1 | probabilistic_v1 | hybrid

        [MaxLength(24)]
        public string Status { get; set; } = "active"; // active | archived

        [MaxLength(128)]
        public string? CorrelationId { get; set; }

        [MaxLength(128)]
        public string? CreatedBy { get; set; }

        [MaxLength(128)]
        public string? UpdatedBy { get; set; }

        public string? MetadataJson { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class OutcomeFeePlanVersion
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(128)]
        public string PlanId { get; set; } = string.Empty;

        public int VersionNumber { get; set; } = 1;

        [MaxLength(24)]
        public string Status { get; set; } = "generated"; // generated | superseded | archived

        [MaxLength(32)]
        public string PlannerMode { get; set; } = "deterministic_v1";

        [MaxLength(64)]
        public string ModelVersion { get; set; } = "outcome-fee-deterministic-v1";

        [MaxLength(64)]
        public string AssumptionSetVersion { get; set; } = "default-v1";

        [MaxLength(128)]
        public string? CorrelationId { get; set; }

        [MaxLength(128)]
        public string? MatterBillingPolicyId { get; set; }

        [MaxLength(128)]
        public string? RateCardId { get; set; }

        [MaxLength(8)]
        public string Currency { get; set; } = "USD";

        [MaxLength(128)]
        public string? GeneratedBy { get; set; }

        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public string? SourceSignalsJson { get; set; }
        public string? InputSnapshotJson { get; set; }
        public string? SummaryJson { get; set; }
        public string? MetadataJson { get; set; }
    }

    public class OutcomeFeeScenario
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(128)]
        public string PlanVersionId { get; set; } = string.Empty;

        [Required]
        [MaxLength(24)]
        public string ScenarioKey { get; set; } = "base"; // conservative | base | aggressive | custom

        [MaxLength(96)]
        public string Name { get; set; } = "Base";

        public decimal Probability { get; set; } // 0..1

        [MaxLength(8)]
        public string Currency { get; set; } = "USD";

        public decimal BudgetTotal { get; set; }
        public decimal ExpectedCollected { get; set; }
        public decimal ExpectedCost { get; set; }
        public decimal ExpectedMargin { get; set; }

        public decimal? ConfidenceScore { get; set; }

        [MaxLength(24)]
        public string Status { get; set; } = "active";

        [MaxLength(512)]
        public string? DriverSummary { get; set; }

        public string? MetadataJson { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class OutcomeFeePhaseForecast
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(128)]
        public string ScenarioId { get; set; } = string.Empty;

        public int PhaseOrder { get; set; }

        [Required]
        [MaxLength(32)]
        public string PhaseCode { get; set; } = string.Empty; // intake | pleading | discovery | motion | settlement | trial_prep | trial

        [MaxLength(96)]
        public string Name { get; set; } = string.Empty;

        public decimal HoursExpected { get; set; }
        public decimal FeeExpected { get; set; }
        public decimal ExpenseExpected { get; set; }
        public int DurationDaysExpected { get; set; }

        [MaxLength(24)]
        public string Status { get; set; } = "planned";

        public string? MetadataJson { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class OutcomeFeeStaffingLine
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(128)]
        public string ScenarioId { get; set; } = string.Empty;

        [MaxLength(128)]
        public string? PhaseForecastId { get; set; }

        [Required]
        [MaxLength(32)]
        public string Role { get; set; } = string.Empty; // partner | associate | paralegal

        public decimal HoursExpected { get; set; }
        public decimal BillRate { get; set; }
        public decimal CostRate { get; set; }
        public decimal FeeExpected { get; set; }
        public decimal CostExpected { get; set; }

        public decimal? UtilizationRiskScore { get; set; }

        public string? MetadataJson { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class OutcomeFeeAssumption
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(128)]
        public string PlanVersionId { get; set; } = string.Empty;

        [Required]
        [MaxLength(48)]
        public string Category { get; set; } = string.Empty; // intake_input | phase_template | collections | staffing | margin | source_signal

        [Required]
        [MaxLength(96)]
        public string Key { get; set; } = string.Empty;

        [MaxLength(32)]
        public string ValueType { get; set; } = "json"; // json | decimal | string | bool | int

        public string? ValueJson { get; set; }

        [MaxLength(48)]
        public string SourceType { get; set; } = "engine_default"; // matter | billing_policy | case_prediction | engine_default | user_override

        [MaxLength(128)]
        public string? SourceRef { get; set; }

        [MaxLength(1024)]
        public string? Notes { get; set; }

        public string? MetadataJson { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class OutcomeFeeCollectionsForecast
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(128)]
        public string ScenarioId { get; set; } = string.Empty;

        [MaxLength(24)]
        public string PayorSegment { get; set; } = "client"; // client | corporate | third_party

        public int BucketDays { get; set; } // 30 / 60 / 90 / 120

        public decimal ExpectedAmount { get; set; }
        public decimal CollectionProbability { get; set; } // 0..1

        [MaxLength(24)]
        public string Status { get; set; } = "planned";

        public string? MetadataJson { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class OutcomeFeeUpdateEvent
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(128)]
        public string PlanId { get; set; } = string.Empty;

        [MaxLength(48)]
        public string TriggerType { get; set; } = "manual_recompute";

        [MaxLength(64)]
        public string? TriggerEntityType { get; set; }

        [MaxLength(128)]
        public string? TriggerEntityId { get; set; }

        [MaxLength(128)]
        public string? AppliedVersionId { get; set; }

        [MaxLength(24)]
        public string Status { get; set; } = "applied"; // pending | applied | skipped | failed

        [MaxLength(128)]
        public string? CorrelationId { get; set; }

        [MaxLength(128)]
        public string? TriggeredBy { get; set; }

        public string? PayloadJson { get; set; }
        public string? ResultJson { get; set; }
        public string? MetadataJson { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? AppliedAt { get; set; }
    }

    public class OutcomeFeeCalibrationSnapshot
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [MaxLength(96)]
        public string CohortKey { get; set; } = string.Empty; // practice:court:arrangement

        [MaxLength(96)]
        public string? JurisdictionCode { get; set; }

        [MaxLength(96)]
        public string? PracticeArea { get; set; }

        [MaxLength(32)]
        public string? ArrangementType { get; set; }

        public DateTime AsOfDate { get; set; } = DateTime.UtcNow.Date;

        [MaxLength(24)]
        public string Status { get; set; } = "draft";

        public int SampleSize { get; set; }
        public string? MetricsJson { get; set; }
        public string? PayloadJson { get; set; }
        public string? MetadataJson { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}

