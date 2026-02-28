using System.Text.Json;
using JurisFlow.Server.Models;

namespace JurisFlow.Server.Services
{
    public static class IntegrationCanonicalActions
    {
        public const string Validate = "validate";
        public const string Pull = "pull";
        public const string Push = "push";
        public const string Webhook = "webhook";
        public const string Backfill = "backfill";
        public const string Reconcile = "reconcile";

        public static readonly IReadOnlyList<string> All = new[]
        {
            Validate,
            Pull,
            Push,
            Webhook,
            Backfill,
            Reconcile
        };
    }

    public static class IntegrationConflictPolicies
    {
        public const string ManualReview = "manual_review";
        public const string SourceWins = "source_wins";
        public const string DestinationWins = "destination_wins";
        public const string NewestWins = "newest_wins";

        public static readonly IReadOnlyList<string> All = new[]
        {
            ManualReview,
            SourceWins,
            DestinationWins,
            NewestWins
        };
    }

    public static class IntegrationReviewQueueStatuses
    {
        public const string Pending = "pending";
        public const string InReview = "in_review";
        public const string Resolved = "resolved";
        public const string Rejected = "rejected";

        public static readonly IReadOnlyList<string> OpenStatuses = new[]
        {
            Pending,
            InReview
        };
    }

    public static class IntegrationConflictStatuses
    {
        public const string Open = "open";
        public const string InReview = "in_review";
        public const string Resolved = "resolved";
        public const string Ignored = "ignored";

        public static readonly IReadOnlyList<string> OpenStatuses = new[]
        {
            Open,
            InReview
        };
    }

    public static class IntegrationEventStatuses
    {
        public const string Pending = "pending";
        public const string Validated = "validated";
        public const string Processed = "processed";
        public const string Rejected = "rejected";
        public const string Failed = "failed";
        public const string NoConnection = "no_connection";
        public const string Dispatched = "dispatched";
        public const string DeadLetter = "dead_letter";
    }

    public sealed class CanonicalIntegrationActionRequest
    {
        public string Action { get; set; } = IntegrationCanonicalActions.Pull;
        public string? ConnectionId { get; set; }
        public string ProviderKey { get; set; } = string.Empty;
        public string? EntityType { get; set; }
        public string? LocalEntityId { get; set; }
        public string? ExternalEntityId { get; set; }
        public string? RunId { get; set; }
        public string? CorrelationId { get; set; }
        public string? IdempotencyKey { get; set; }
        public string? Cursor { get; set; }
        public string? DeltaToken { get; set; }
        public string? PayloadJson { get; set; }
        public bool DryRun { get; set; }
        public bool RequiresReview { get; set; }
        public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    }

    public sealed class CanonicalIntegrationActionResult
    {
        public bool Success { get; set; }
        public bool Retryable { get; set; }
        public string Action { get; set; } = IntegrationCanonicalActions.Pull;
        public string Status { get; set; } = "pending";
        public string? Message { get; set; }
        public string? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
        public string? NextCursor { get; set; }
        public string? NextDeltaToken { get; set; }
        public int ReadCount { get; set; }
        public int WriteCount { get; set; }
        public int ConflictCount { get; set; }
        public int ReviewCount { get; set; }
        public string? ResultJson { get; set; }

        public static CanonicalIntegrationActionResult Unsupported(string action, string providerKey)
        {
            return new CanonicalIntegrationActionResult
            {
                Success = false,
                Retryable = false,
                Action = action,
                Status = "unsupported",
                ErrorCode = "action_not_supported",
                ErrorMessage = $"Provider '{providerKey}' does not support action '{action}'."
            };
        }
    }

    public interface IIntegrationActionConnector : IIntegrationConnector
    {
        IReadOnlyCollection<string> SupportedActions { get; }
        Task<CanonicalIntegrationActionResult> ExecuteAsync(
            IntegrationConnection connection,
            CanonicalIntegrationActionRequest request,
            CancellationToken cancellationToken);
    }

    public static class IntegrationCanonicalContract
    {
        public static object Describe()
        {
            return new
            {
                version = "1.0",
                actions = IntegrationCanonicalActions.All,
                conflictPolicies = IntegrationConflictPolicies.All,
                reviewQueueOpenStatuses = IntegrationReviewQueueStatuses.OpenStatuses,
                conflictQueueOpenStatuses = IntegrationConflictStatuses.OpenStatuses,
                eventStatuses = new
                {
                    inbox = new[]
                    {
                        IntegrationEventStatuses.Pending,
                        IntegrationEventStatuses.Validated,
                        IntegrationEventStatuses.Processed,
                        IntegrationEventStatuses.Rejected,
                        IntegrationEventStatuses.Failed,
                        IntegrationEventStatuses.NoConnection
                    },
                    outbox = new[]
                    {
                        IntegrationEventStatuses.Pending,
                        IntegrationEventStatuses.Dispatched,
                        IntegrationEventStatuses.Failed,
                        IntegrationEventStatuses.DeadLetter
                    }
                }
            };
        }

        public static string BuildRunOutboxPayloadJson(IntegrationConnection connection, IntegrationRun run)
        {
            return JsonSerializer.Serialize(new
            {
                connectionId = connection.Id,
                providerKey = connection.ProviderKey,
                runId = run.Id,
                trigger = run.Trigger,
                status = run.Status,
                attemptCount = run.AttemptCount,
                maxAttempts = run.MaxAttempts,
                errorCode = run.ErrorCode,
                errorMessage = run.ErrorMessage,
                createdAt = run.CreatedAt,
                completedAt = run.CompletedAt
            });
        }
    }
}
