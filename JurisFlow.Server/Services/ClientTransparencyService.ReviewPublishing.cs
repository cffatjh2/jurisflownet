using System.Text.Json;
using JurisFlow.Server.Models;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace JurisFlow.Server.Services
{
    public partial class ClientTransparencyService
    {
        private sealed class ClientTransparencyPublishPolicyConfig
        {
            public bool AutoPublishSafe { get; set; }
            public bool ReviewRequiredForDelayReason { get; set; }
            public bool ReviewRequiredForCostImpactChange { get; set; }
            public decimal CostImpactChangeThreshold { get; set; } = 1000m;
            public bool BlockOnLowConfidence { get; set; }
            public decimal LowConfidenceThreshold { get; set; } = 0.55m;
        }

        private sealed class ClientTransparencyPublishingEvaluation
        {
            public string PublishDecision { get; set; } = "warn_only_auto_publish";
            public bool RequiresReview { get; set; }
            public bool Blocked { get; set; }
            public bool ShouldAutoPublish { get; set; }
            public string Priority { get; set; } = "low";
            public IReadOnlyList<string> Reasons { get; set; } = Array.Empty<string>();
        }

        private sealed class ClientTransparencyPublishingWorkflowResult
        {
            public bool IsPublished { get; set; }
            public int ReviewItemsQueued { get; set; }
            public string PublishDecision { get; set; } = "none";
            public IReadOnlyList<string> Reasons { get; set; } = Array.Empty<string>();
        }
    }

    public class ClientTransparencyReviewWorkspaceResult
    {
        public string MatterId { get; set; } = string.Empty;
        public ClientTransparencyProfile? Profile { get; set; }
        public ClientTransparencySnapshotDetailResult? Draft { get; set; }
        public ClientTransparencySnapshotDetailResult? Published { get; set; }
        public IntegrationReviewQueueItem? PendingReviewItem { get; set; }
        public ClientTransparencyPublishPolicySummary? Policy { get; set; }
        public object? DraftVsPublished { get; set; }
        public object? DraftPolicyEvaluation { get; set; }
    }

    public class ClientTransparencyPublishPolicySummary
    {
        public string PublishPolicy { get; set; } = "warn_only";
        public bool AutoPublishSafe { get; set; }
        public bool ReviewRequiredForDelayReason { get; set; }
        public bool ReviewRequiredForCostImpactChange { get; set; }
        public decimal CostImpactChangeThreshold { get; set; }
        public bool BlockOnLowConfidence { get; set; }
        public decimal LowConfidenceThreshold { get; set; }
    }

    public class ClientTransparencyPolicyUpsertRequest
    {
        public string? PublishPolicy { get; set; }
        public bool? AutoPublishSafe { get; set; }
        public bool? ReviewRequiredForDelayReason { get; set; }
        public bool? ReviewRequiredForCostImpactChange { get; set; }
        public decimal? CostImpactChangeThreshold { get; set; }
        public bool? BlockOnLowConfidence { get; set; }
        public decimal? LowConfidenceThreshold { get; set; }
    }

    public class ClientTransparencySnapshotReviewRequest
    {
        public string? Action { get; set; } = "rewrite";
        public string? Reason { get; set; }
        public bool? PublishAfter { get; set; }
        public string? AssignedTo { get; set; }
        public string? SnapshotSummary { get; set; }
        public string? WhatChangedSummary { get; set; }
        public string? NextStepActionText { get; set; }
        public string? NextStepBlockedByText { get; set; }
        public List<ClientTransparencyDelayReasonTextUpdate>? DelayReasonTextUpdates { get; set; }
        public List<ClientTransparencyTimelineTextUpdate>? TimelineTextUpdates { get; set; }
    }

    public class ClientTransparencyDelayReasonTextUpdate
    {
        public string? Id { get; set; }
        public string? ClientSafeText { get; set; }
    }

    public class ClientTransparencyTimelineTextUpdate
    {
        public string? Id { get; set; }
        public string? ClientSafeText { get; set; }
    }

    public class ClientTransparencyPublishRequest
    {
        public string? Reason { get; set; }
        public bool OverridePolicy { get; set; }
        public string? ApproverReason { get; set; }
    }

    public class ClientTransparencyPublishResult
    {
        public string SnapshotId { get; set; } = string.Empty;
        public string MatterId { get; set; } = string.Empty;
        public bool Published { get; set; }
        public string PublishDecision { get; set; } = "none";
        public bool OverrideRequired { get; set; }
        public DateTime? PublishedAt { get; set; }
        public int ReviewItemsQueued { get; set; }
        public IReadOnlyList<string> Reasons { get; set; } = Array.Empty<string>();
    }
}
