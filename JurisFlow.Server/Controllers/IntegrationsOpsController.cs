using System.Security.Claims;
using System.Text.Json;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using JurisFlow.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JurisFlow.Server.Controllers
{
    [Route("api/integrations/ops")]
    [ApiController]
    [Authorize(Policy = "StaffOnly")]
    public class IntegrationsOpsController : ControllerBase
    {
        private readonly JurisFlowDbContext _context;
        private readonly AuditLogger _auditLogger;
        private readonly IntegrationSyncRunner _integrationSyncRunner;
        private readonly IntegrationCanonicalActionRunner _integrationCanonicalActionRunner;
        private readonly IConfiguration _configuration;
        private readonly IIntegrationOperationsGuard _integrationOperationsGuard;
        private readonly IIntegrationSecretStore _integrationSecretStore;
        private readonly IIntegrationSecretKeyProvider _integrationSecretKeyProvider;
        private readonly IIntegrationSecretCryptoService _integrationSecretCryptoService;
        private readonly IIntegrationSecretAccessPolicy _integrationSecretAccessPolicy;

        public IntegrationsOpsController(
            JurisFlowDbContext context,
            AuditLogger auditLogger,
            IntegrationSyncRunner integrationSyncRunner,
            IntegrationCanonicalActionRunner integrationCanonicalActionRunner,
            IConfiguration configuration,
            IIntegrationOperationsGuard integrationOperationsGuard,
            IIntegrationSecretStore integrationSecretStore,
            IIntegrationSecretKeyProvider integrationSecretKeyProvider,
            IIntegrationSecretCryptoService integrationSecretCryptoService,
            IIntegrationSecretAccessPolicy integrationSecretAccessPolicy)
        {
            _context = context;
            _auditLogger = auditLogger;
            _integrationSyncRunner = integrationSyncRunner;
            _integrationCanonicalActionRunner = integrationCanonicalActionRunner;
            _configuration = configuration;
            _integrationOperationsGuard = integrationOperationsGuard;
            _integrationSecretStore = integrationSecretStore;
            _integrationSecretKeyProvider = integrationSecretKeyProvider;
            _integrationSecretCryptoService = integrationSecretCryptoService;
            _integrationSecretAccessPolicy = integrationSecretAccessPolicy;
        }

        [HttpGet("contract")]
        public IActionResult GetCanonicalContract() => Ok(IntegrationCanonicalContract.Describe());

        [HttpGet("capability-matrix")]
        public async Task<IActionResult> GetCapabilityMatrix(CancellationToken cancellationToken)
        {
            var providers = IntegrationProviderCatalog.Items.OrderBy(p => p.Category).ThenBy(p => p.Provider).ToList();
            var connections = await _context.IntegrationConnections.AsNoTracking().ToListAsync(cancellationToken);
            var connectionIds = connections.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);

            var mappingCounts = await _context.IntegrationMappingProfiles.AsNoTracking()
                .GroupBy(m => new { m.ProviderKey, m.ConnectionId })
                .Select(g => new { g.Key.ProviderKey, g.Key.ConnectionId, Count = g.Count() })
                .ToListAsync(cancellationToken);
            var openConflictCounts = await _context.IntegrationConflictQueueItems.AsNoTracking()
                .Where(c => IntegrationConflictStatuses.OpenStatuses.Contains(c.Status))
                .GroupBy(c => c.ProviderKey)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken);
            var openReviewCounts = await _context.IntegrationReviewQueueItems.AsNoTracking()
                .Where(r => IntegrationReviewQueueStatuses.OpenStatuses.Contains(r.Status))
                .GroupBy(r => r.ProviderKey)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken);
            var inboxPendingCounts = await _context.IntegrationInboxEvents.AsNoTracking()
                .Where(e => e.Status == IntegrationEventStatuses.Pending || e.Status == IntegrationEventStatuses.Validated)
                .GroupBy(e => e.ProviderKey)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken);
            var inboxSeenCounts = await _context.IntegrationInboxEvents.AsNoTracking()
                .GroupBy(e => e.ProviderKey)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken);
            var outboxPendingCounts = await _context.IntegrationOutboxEvents.AsNoTracking()
                .Where(e => e.Status == IntegrationEventStatuses.Pending || e.Status == IntegrationEventStatuses.Failed)
                .GroupBy(e => e.ProviderKey)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken);

            var recentRuns = await _context.IntegrationRuns.AsNoTracking()
                .Where(r => connectionIds.Contains(r.ConnectionId))
                .OrderByDescending(r => r.CreatedAt)
                .Select(r => new { r.ConnectionId, r.Status, r.CreatedAt, r.CompletedAt, r.ErrorCode })
                .ToListAsync(cancellationToken);

            var latestRunByConnection = recentRuns
                .GroupBy(r => r.ConnectionId)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

            var mappingLookup = mappingCounts.ToDictionary(k => $"{k.ProviderKey}|{k.ConnectionId}", v => v.Count, StringComparer.OrdinalIgnoreCase);
            var conflictLookup = openConflictCounts.ToDictionary(k => k.Key, v => v.Count, StringComparer.OrdinalIgnoreCase);
            var reviewLookup = openReviewCounts.ToDictionary(k => k.Key, v => v.Count, StringComparer.OrdinalIgnoreCase);
            var inboxPendingLookup = inboxPendingCounts.ToDictionary(k => k.Key, v => v.Count, StringComparer.OrdinalIgnoreCase);
            var inboxSeenLookup = inboxSeenCounts.ToDictionary(k => k.Key, v => v.Count, StringComparer.OrdinalIgnoreCase);
            var outboxLookup = outboxPendingCounts.ToDictionary(k => k.Key, v => v.Count, StringComparer.OrdinalIgnoreCase);

            var rows = providers.Select(provider =>
            {
                var connection = connections.FirstOrDefault(c => c.ProviderKey == provider.ProviderKey);
                var mappingCount = connection == null ? 0 : mappingLookup.GetValueOrDefault($"{provider.ProviderKey}|{connection.Id}");
                var openConflicts = conflictLookup.GetValueOrDefault(provider.ProviderKey);
                var openReviews = reviewLookup.GetValueOrDefault(provider.ProviderKey);
                var pendingInbox = inboxPendingLookup.GetValueOrDefault(provider.ProviderKey);
                var inboxSeen = inboxSeenLookup.GetValueOrDefault(provider.ProviderKey);
                var pendingOutbox = outboxLookup.GetValueOrDefault(provider.ProviderKey);
                latestRunByConnection.TryGetValue(connection?.Id ?? string.Empty, out var latestRun);

                var gaps = new List<string>();
                if (connection == null) gaps.Add("not_connected");
                if (connection != null &&
                    provider.SupportedActions.Any(a => a == IntegrationCanonicalActions.Push || a == IntegrationCanonicalActions.Reconcile) &&
                    mappingCount == 0)
                {
                    gaps.Add("mapping_profile_missing");
                }
                if (provider.SupportsWebhook && connection != null && inboxSeen == 0) gaps.Add("webhook_not_observed");
                if (openConflicts > 0) gaps.Add("conflicts_pending_review");
                if (openReviews > 0) gaps.Add("review_queue_backlog");
                if (pendingOutbox > 0) gaps.Add("outbox_backlog");

                return new
                {
                    provider.ProviderKey,
                    provider.Provider,
                    provider.Category,
                    provider.ConnectionMode,
                    provider.SupportsWebhook,
                    provider.WebhookFirst,
                    provider.FallbackPollingMinutes,
                    supportedActions = provider.SupportedActions,
                    capabilities = provider.Capabilities,
                    connectionId = connection?.Id,
                    connectionStatus = connection?.Status,
                    syncEnabled = connection?.SyncEnabled ?? false,
                    connection?.LastSyncAt,
                    connection?.LastWebhookAt,
                    mappingProfileCount = mappingCount,
                    openConflictCount = openConflicts,
                    openReviewCount = openReviews,
                    pendingInboxEventCount = pendingInbox,
                    pendingOutboxEventCount = pendingOutbox,
                    lastRunStatus = latestRun?.Status,
                    lastRunAt = latestRun?.CompletedAt ?? latestRun?.CreatedAt,
                    lastRunErrorCode = latestRun?.ErrorCode,
                    gaps
                };
            }).ToList();

            return Ok(new { generatedAt = DateTime.UtcNow, rows });
        }

        [HttpGet("analytics/kpis")]
        public async Task<IActionResult> GetHistoricalKpiAnalytics(
            [FromQuery] int days = 90,
            [FromQuery] string? bucket = "week",
            CancellationToken cancellationToken = default)
        {
            days = Math.Clamp(days, 7, 365);
            var bucketKind = NormalizeKpiBucket(bucket);
            var now = DateTime.UtcNow;
            var fromUtc = now.Date.AddDays(-(days - 1));

            var validationRuns = await _context.JurisdictionValidationTestRuns.AsNoTracking()
                .Where(r => r.CreatedAt >= fromUtc)
                .Select(r => new
                {
                    r.Id,
                    r.CreatedAt,
                    r.TotalCases,
                    r.PassedCases,
                    r.FailedCases,
                    r.ResultJson
                })
                .ToListAsync(cancellationToken);

            var filingRows = await _context.EfilingSubmissions.AsNoTracking()
                .Where(s => (s.SubmittedAt ?? s.CreatedAt) >= fromUtc)
                .Select(s => new
                {
                    EventAt = s.SubmittedAt ?? s.CreatedAt,
                    s.Status,
                    s.RejectedAt,
                    s.AcceptedAt
                })
                .ToListAsync(cancellationToken);

            var ruleChangeRows = await _context.JurisdictionRuleChangeRecords.AsNoTracking()
                .Where(r => r.ReviewedAt != null && r.ReviewedAt >= fromUtc)
                .Select(r => new
                {
                    r.Id,
                    r.CreatedAt,
                    r.ReviewedAt,
                    r.Status,
                    r.ChangeType,
                    r.Severity,
                    r.JurisdictionCode
                })
                .ToListAsync(cancellationToken);

            var prebillLineRows = await _context.BillingPrebillLines.AsNoTracking()
                .Where(l => l.CreatedAt >= fromUtc)
                .Select(l => new
                {
                    l.Id,
                    l.CreatedAt,
                    l.ProposedAmount,
                    l.ApprovedAmount,
                    l.Status,
                    l.DiscountAmount,
                    l.WriteDownAmount
                })
                .ToListAsync(cancellationToken);

            var invoiceRows = await _context.Invoices.AsNoTracking()
                .Where(i => i.IssueDate >= fromUtc)
                .Select(i => new
                {
                    i.Id,
                    i.ClientId,
                    i.IssueDate,
                    i.Total,
                    i.AmountPaid,
                    i.Status
                })
                .ToListAsync(cancellationToken);

            var invoiceIds = invoiceRows.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
            var paymentRows = await _context.PaymentTransactions.AsNoTracking()
                .Where(p => p.InvoiceId != null &&
                            p.ProcessedAt != null &&
                            (p.Status == "Succeeded" || p.Status == "Refunded" || p.Status == "Partially Refunded"))
                .Select(p => new
                {
                    p.InvoiceId,
                    p.ProcessedAt,
                    p.Amount,
                    p.Status
                })
                .ToListAsync(cancellationToken);

            var filteredPaymentRows = paymentRows
                .Where(p => !string.IsNullOrWhiteSpace(p.InvoiceId) && invoiceIds.Contains(p.InvoiceId!))
                .ToList();

            var allocationRows = await _context.BillingPaymentAllocations.AsNoTracking()
                .Where(a => a.Status == "applied" && invoiceIds.Contains(a.InvoiceId))
                .Select(a => new
                {
                    a.InvoiceId,
                    a.PayorClientId,
                    a.ClientId,
                    a.Amount,
                    a.AppliedAt
                })
                .ToListAsync(cancellationToken);

            var payorClientIds = allocationRows
                .Select(a => a.PayorClientId)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v!)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var payorClientTypeMap = payorClientIds.Count == 0
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : (await _context.Clients.AsNoTracking()
                    .Where(c => payorClientIds.Contains(c.Id))
                    .Select(c => new { c.Id, c.Type })
                    .ToListAsync(cancellationToken))
                    .ToDictionary(x => x.Id, x => x.Type ?? string.Empty, StringComparer.Ordinal);

            var billingReviewRows = await _context.IntegrationReviewQueueItems.AsNoTracking()
                .Where(r => r.ProviderKey == "billing-engine" && r.CreatedAt >= fromUtc)
                .Select(r => new
                {
                    r.Id,
                    r.CreatedAt,
                    r.ItemType,
                    r.Status,
                    r.Decision,
                    r.ResolvedAt,
                    r.Summary,
                    r.Title
                })
                .ToListAsync(cancellationToken);

            var ebillingEventRows = await _context.BillingEbillingResultEvents.AsNoTracking()
                .Where(e => e.OccurredAt >= fromUtc)
                .Select(e => new
                {
                    e.Id,
                    e.ProviderKey,
                    e.TransmissionId,
                    e.EventType,
                    e.Status,
                    e.IsFinal,
                    e.IsRetryable,
                    e.ErrorCode,
                    e.OccurredAt,
                    e.RecordedAt
                })
                .ToListAsync(cancellationToken);

            var trustSnapshotRows = await _context.TrustReconciliationSnapshots.AsNoTracking()
                .Where(s => s.CreatedAt >= fromUtc && s.TrustAccountId == null)
                .Select(s => new
                {
                    s.Id,
                    s.AsOfUtc,
                    s.CreatedAt,
                    s.AccountCount,
                    s.MismatchedAccountCount,
                    s.BankBalance,
                    s.ClientLedgerTotal,
                    s.TrustTransactionsNet,
                    s.BillingTrustLedgerTotal,
                    s.BankVsClientLedgerDiff,
                    s.ClientLedgerVsTrustLedgerDiff,
                    s.BankVsTrustLedgerDiff,
                    s.DataQuality,
                    s.Source
                })
                .ToListAsync(cancellationToken);

            var validationSeries = BuildValidationKpiSeries(validationRuns, bucketKind);
            var filingRejectionSeries = BuildFilingRejectionSeries(filingRows, bucketKind);
            var ruleLeadTimeSeries = BuildRuleLeadTimeSeries(ruleChangeRows, bucketKind);
            var prebillAdjustmentSeries = BuildPrebillAdjustmentSeries(prebillLineRows, bucketKind);
            var collectionCycle = BuildCollectionCycleMetrics(invoiceRows, filteredPaymentRows, allocationRows, payorClientTypeMap, bucketKind);
            var ebillingMetrics = ebillingEventRows.Count > 0
                ? BuildEbillingExactMetrics(ebillingEventRows, bucketKind)
                : BuildEbillingProxyMetrics(billingReviewRows, bucketKind);

            return Ok(new
            {
                generatedAt = now,
                window = new
                {
                    fromUtc,
                    toUtc = now,
                    days,
                    bucket = bucketKind
                },
                courtValidation = validationSeries,
                filingRejection = filingRejectionSeries,
                ruleUpdateLeadTime = ruleLeadTimeSeries,
                prebillAdjustment = prebillAdjustmentSeries,
                collectionCycle = collectionCycle,
                ebillingRejection = ebillingMetrics,
                trustReconciliationHistory = BuildTrustReconciliationHistorySeries(trustSnapshotRows, bucketKind)
            });
        }

        [HttpGet("connections/{connectionId}/mapping-profiles")]
        public async Task<IActionResult> GetMappingProfiles(string connectionId, CancellationToken cancellationToken)
        {
            var exists = await _context.IntegrationConnections.AnyAsync(c => c.Id == connectionId, cancellationToken);
            if (!exists)
            {
                return NotFound(new { message = "Integration connection not found." });
            }

            var profiles = await _context.IntegrationMappingProfiles.AsNoTracking()
                .Where(p => p.ConnectionId == connectionId)
                .OrderByDescending(p => p.IsDefault)
                .ThenBy(p => p.EntityType)
                .ThenBy(p => p.Name)
                .ToListAsync(cancellationToken);

            return Ok(profiles);
        }

        [HttpPut("connections/{connectionId}/mapping-profiles/{profileKey}")]
        public async Task<IActionResult> UpsertMappingProfile(
            string connectionId,
            string profileKey,
            [FromBody] UpsertMappingProfileRequest? request,
            CancellationToken cancellationToken)
        {
            if (request == null)
            {
                return BadRequest(new { message = "Request body is required." });
            }

            var connection = await _context.IntegrationConnections.FirstOrDefaultAsync(c => c.Id == connectionId, cancellationToken);
            if (connection == null)
            {
                return NotFound(new { message = "Integration connection not found." });
            }

            var normalizedProfileKey = NormalizeRequired(profileKey, 128);
            var name = NormalizeRequired(request.Name, 160);
            var entityType = NormalizeRequired(request.EntityType, 64);
            if (normalizedProfileKey == null || name == null || entityType == null)
            {
                return BadRequest(new { message = "profileKey, name, and entityType are required." });
            }

            var conflictPolicy = NormalizeConflictPolicy(request.ConflictPolicy);
            if (conflictPolicy == null)
            {
                return BadRequest(new { message = $"ConflictPolicy must be one of: {string.Join(", ", IntegrationConflictPolicies.All)}" });
            }

            var profile = await _context.IntegrationMappingProfiles
                .FirstOrDefaultAsync(p => p.ConnectionId == connectionId && p.ProfileKey == normalizedProfileKey, cancellationToken);

            var isNew = profile == null;
            var now = DateTime.UtcNow;
            profile ??= new IntegrationMappingProfile
            {
                Id = Guid.NewGuid().ToString(),
                ConnectionId = connectionId,
                ProviderKey = connection.ProviderKey,
                ProfileKey = normalizedProfileKey,
                CreatedAt = now
            };

            profile.ProviderKey = connection.ProviderKey;
            profile.Name = name;
            profile.EntityType = entityType;
            profile.Direction = NormalizeDirection(request.Direction);
            profile.Status = NormalizeProfileStatus(request.Status);
            profile.ConflictPolicy = conflictPolicy;
            profile.IsDefault = request.IsDefault ?? profile.IsDefault;
            profile.FieldMappingsJson = NormalizeJson(request.FieldMappingsJson);
            profile.EnumMappingsJson = NormalizeJson(request.EnumMappingsJson);
            profile.TaxMappingsJson = NormalizeJson(request.TaxMappingsJson);
            profile.AccountMappingsJson = NormalizeJson(request.AccountMappingsJson);
            profile.DefaultsJson = NormalizeJson(request.DefaultsJson);
            profile.MetadataJson = NormalizeJson(request.MetadataJson);
            profile.ValidationSummary = Truncate(request.ValidationSummary, 2048);
            profile.LastValidatedAt = request.LastValidatedAt ?? profile.LastValidatedAt;
            profile.UpdatedBy = ResolveActorId();
            profile.UpdatedAt = now;
            profile.Version = isNew ? 1 : profile.Version + 1;

            if (isNew)
            {
                _context.IntegrationMappingProfiles.Add(profile);
            }

            if (profile.IsDefault)
            {
                var siblings = await _context.IntegrationMappingProfiles
                    .Where(p => p.ConnectionId == connectionId &&
                                p.EntityType == profile.EntityType &&
                                p.Direction == profile.Direction &&
                                p.Id != profile.Id &&
                                p.IsDefault)
                    .ToListAsync(cancellationToken);
                foreach (var sibling in siblings)
                {
                    sibling.IsDefault = false;
                    sibling.UpdatedAt = now;
                }
            }

            await _context.SaveChangesAsync(cancellationToken);
            await _auditLogger.LogAsync(HttpContext,
                isNew ? "integrations.mapping_profile.create" : "integrations.mapping_profile.update",
                nameof(IntegrationMappingProfile),
                profile.Id,
                $"ProfileKey={profile.ProfileKey}, ProviderKey={profile.ProviderKey}");

            return Ok(profile);
        }

        [HttpPost("connections/{connectionId}/actions/{action}")]
        public async Task<IActionResult> RunCanonicalAction(
            string connectionId,
            string action,
            [FromBody] RunCanonicalActionRequest? request,
            CancellationToken cancellationToken)
        {
            var connection = await _context.IntegrationConnections.FirstOrDefaultAsync(c => c.Id == connectionId, cancellationToken);
            if (connection == null)
            {
                return NotFound(new { message = "Integration connection not found." });
            }

            var provider = IntegrationProviderCatalog.Find(connection.ProviderKey);
            if (provider == null)
            {
                return BadRequest(new { message = "Unsupported integration provider." });
            }

            var normalizedAction = (action ?? string.Empty).Trim().ToLowerInvariant();
            if (!provider.SupportedActions.Contains(normalizedAction, StringComparer.Ordinal))
            {
                return BadRequest(new { message = $"Action '{normalizedAction}' is not supported by provider '{provider.ProviderKey}'." });
            }

            var guardDecision = await _integrationOperationsGuard.EvaluateForConnectionAsync(
                connection,
                IntegrationOperationKinds.CanonicalAction,
                cancellationToken);
            if (!guardDecision.Allowed)
            {
                return StatusCode(StatusCodes.Status423Locked, new { message = guardDecision.Message ?? "Canonical action is blocked." });
            }

            var correlationId = NormalizeRequired(request?.CorrelationId, 128)
                                ?? Request.Headers["X-Correlation-ID"].FirstOrDefault()
                                ?? Request.Headers["Idempotency-Key"].FirstOrDefault();

            var result = await _integrationCanonicalActionRunner.RunAsync(
                connection,
                new CanonicalIntegrationActionRequest
                {
                    Action = normalizedAction,
                    EntityType = NormalizeRequired(request?.EntityType, 64),
                    LocalEntityId = NormalizeRequired(request?.LocalEntityId, 128),
                    ExternalEntityId = NormalizeRequired(request?.ExternalEntityId, 256),
                    CorrelationId = correlationId,
                    IdempotencyKey = NormalizeRequired(request?.IdempotencyKey, 160),
                    Cursor = NormalizeRequired(request?.Cursor, 2048),
                    DeltaToken = NormalizeRequired(request?.DeltaToken, 2048),
                    PayloadJson = NormalizeJson(request?.PayloadJson),
                    DryRun = request?.DryRun ?? false,
                    RequiresReview = request?.RequiresReview ?? true
                },
                cancellationToken);

            AuditTraceContext.SetIntegrationTrace(
                HttpContext,
                connectionId: connection.Id,
                providerKey: connection.ProviderKey,
                correlationId: correlationId);

            await _auditLogger.LogAsync(
                HttpContext,
                "integrations.canonical_action.run",
                nameof(IntegrationConnection),
                connection.Id,
                $"ProviderKey={connection.ProviderKey}, Action={normalizedAction}, Success={result.Success}, Status={result.Status}");

            return Ok(result);
        }

        [HttpGet("conflicts")]
        public async Task<IActionResult> GetConflicts(
            [FromQuery] string? status = null,
            [FromQuery] string? providerKey = null,
            [FromQuery] int limit = 100,
            CancellationToken cancellationToken = default)
        {
            var query = _context.IntegrationConflictQueueItems.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(status)) query = query.Where(c => c.Status == status.Trim().ToLowerInvariant());
            if (!string.IsNullOrWhiteSpace(providerKey)) query = query.Where(c => c.ProviderKey == providerKey.Trim().ToLowerInvariant());
            return Ok(await query.OrderByDescending(c => c.CreatedAt).Take(Math.Clamp(limit, 1, 500)).ToListAsync(cancellationToken));
        }

        [HttpPost("conflicts/{id}/resolve")]
        public async Task<IActionResult> ResolveConflict(
            string id,
            [FromBody] ResolveConflictRequest? request,
            CancellationToken cancellationToken)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.ResolutionType))
            {
                return BadRequest(new { message = "ResolutionType is required." });
            }

            var conflict = await _context.IntegrationConflictQueueItems.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
            if (conflict == null)
            {
                return NotFound(new { message = "Conflict item not found." });
            }

            conflict.Status = string.IsNullOrWhiteSpace(request.Status)
                ? IntegrationConflictStatuses.Resolved
                : request.Status.Trim().ToLowerInvariant();
            conflict.ResolutionType = NormalizeRequired(request.ResolutionType, 32);
            conflict.ResolutionJson = NormalizeJson(request.ResolutionJson);
            conflict.ReviewNotes = Truncate(request.Notes, 2048);
            conflict.ReviewedBy = ResolveActorId();
            conflict.ReviewedAt = DateTime.UtcNow;
            if (conflict.Status == IntegrationConflictStatuses.Resolved)
            {
                conflict.ResolvedAt = DateTime.UtcNow;
            }
            conflict.UpdatedAt = DateTime.UtcNow;

            var relatedReviews = await _context.IntegrationReviewQueueItems
                .Where(r => r.ConflictId == conflict.Id && (r.Status == IntegrationReviewQueueStatuses.Pending || r.Status == IntegrationReviewQueueStatuses.InReview))
                .ToListAsync(cancellationToken);
            foreach (var review in relatedReviews)
            {
                review.Status = IntegrationReviewQueueStatuses.Resolved;
                review.Decision = "conflict_resolved";
                review.DecisionNotes = Truncate(request.Notes, 2048);
                review.ReviewedBy = conflict.ReviewedBy;
                review.ReviewedAt = DateTime.UtcNow;
                review.ResolvedAt = DateTime.UtcNow;
                review.UpdatedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync(cancellationToken);
            await _auditLogger.LogAsync(HttpContext, "integrations.conflict.resolve", nameof(IntegrationConflictQueueItem), conflict.Id,
                $"Status={conflict.Status}, ResolutionType={conflict.ResolutionType}");

            return Ok(conflict);
        }

        [HttpGet("review-queue")]
        public async Task<IActionResult> GetReviewQueue(
            [FromQuery] string? status = null,
            [FromQuery] string? providerKey = null,
            [FromQuery] int limit = 100,
            CancellationToken cancellationToken = default)
        {
            var query = _context.IntegrationReviewQueueItems.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(status)) query = query.Where(i => i.Status == status.Trim().ToLowerInvariant());
            if (!string.IsNullOrWhiteSpace(providerKey)) query = query.Where(i => i.ProviderKey == providerKey.Trim().ToLowerInvariant());
            return Ok(await query.OrderByDescending(i => i.CreatedAt).Take(Math.Clamp(limit, 1, 500)).ToListAsync(cancellationToken));
        }

        [HttpPost("review-queue/{id}/decision")]
        public async Task<IActionResult> DecideReviewQueueItem(
            string id,
            [FromBody] DecideReviewItemRequest? request,
            CancellationToken cancellationToken)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Decision))
            {
                return BadRequest(new { message = "Decision is required." });
            }

            var item = await _context.IntegrationReviewQueueItems.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
            if (item == null)
            {
                return NotFound(new { message = "Review queue item not found." });
            }

            item.Decision = NormalizeRequired(request.Decision, 32);
            item.DecisionNotes = Truncate(request.Notes, 2048);
            item.Status = string.IsNullOrWhiteSpace(request.Status)
                ? IntegrationReviewQueueStatuses.Resolved
                : request.Status.Trim().ToLowerInvariant();
            item.ReviewedBy = ResolveActorId();
            item.ReviewedAt = DateTime.UtcNow;
            if (item.Status == IntegrationReviewQueueStatuses.Resolved)
            {
                item.ResolvedAt = DateTime.UtcNow;
            }
            item.UpdatedAt = DateTime.UtcNow;

            if (!string.IsNullOrWhiteSpace(item.ConflictId))
            {
                var conflict = await _context.IntegrationConflictQueueItems.FirstOrDefaultAsync(c => c.Id == item.ConflictId, cancellationToken);
                if (conflict != null && item.Status == IntegrationReviewQueueStatuses.Resolved)
                {
                    if (string.Equals(item.Decision, "approve", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(item.Decision, "resolve", StringComparison.OrdinalIgnoreCase))
                    {
                        conflict.Status = IntegrationConflictStatuses.Resolved;
                        conflict.ResolutionType ??= "review_approved";
                        conflict.ReviewedBy = item.ReviewedBy;
                        conflict.ReviewedAt = item.ReviewedAt;
                        conflict.ResolvedAt = DateTime.UtcNow;
                        conflict.UpdatedAt = DateTime.UtcNow;
                    }
                }
            }

            await _context.SaveChangesAsync(cancellationToken);
            await _auditLogger.LogAsync(HttpContext, "integrations.review_queue.decision", nameof(IntegrationReviewQueueItem), item.Id,
                $"Decision={item.Decision}, Status={item.Status}");

            return Ok(item);
        }

        [HttpGet("events/inbox")]
        public async Task<IActionResult> GetInboxEvents(
            [FromQuery] string? providerKey = null,
            [FromQuery] string? status = null,
            [FromQuery] int limit = 100,
            CancellationToken cancellationToken = default)
        {
            var query = _context.IntegrationInboxEvents.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(providerKey)) query = query.Where(e => e.ProviderKey == providerKey.Trim().ToLowerInvariant());
            if (!string.IsNullOrWhiteSpace(status)) query = query.Where(e => e.Status == status.Trim().ToLowerInvariant());
            var rows = await query.OrderByDescending(e => e.ReceivedAt).Take(Math.Clamp(limit, 1, 500))
                .Select(e => new
                {
                    e.Id, e.ConnectionId, e.RunId, e.ProviderKey, e.ExternalEventId, e.Status,
                    e.SignatureValidated, e.PayloadHash, e.ReplayCount, e.ErrorMessage, e.ReceivedAt, e.ProcessedAt
                })
                .ToListAsync(cancellationToken);
            return Ok(rows);
        }

        [HttpPost("events/inbox/{id}/replay")]
        public async Task<IActionResult> ReplayInboxEvent(string id, CancellationToken cancellationToken)
        {
            var inboxEvent = await _context.IntegrationInboxEvents.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
            if (inboxEvent == null)
            {
                return NotFound(new { message = "Inbox event not found." });
            }

            if (string.IsNullOrWhiteSpace(inboxEvent.ConnectionId))
            {
                return BadRequest(new { message = "Inbox event is not linked to an integration connection." });
            }

            var connection = await _context.IntegrationConnections.FirstOrDefaultAsync(c => c.Id == inboxEvent.ConnectionId, cancellationToken);
            if (connection == null)
            {
                return NotFound(new { message = "Integration connection not found for inbox event." });
            }

            var guardDecision = await _integrationOperationsGuard.EvaluateForConnectionAsync(
                connection,
                IntegrationOperationKinds.Replay,
                cancellationToken);
            if (!guardDecision.Allowed)
            {
                return StatusCode(StatusCodes.Status423Locked, new { message = guardDecision.Message ?? "Inbox replay is blocked." });
            }

            var replaySequence = inboxEvent.ReplayCount + 1;
            var replayKey = $"webhook-replay:{connection.Id}:{(inboxEvent.ExternalEventId ?? inboxEvent.Id)}:{replaySequence}";
            var runResult = await _integrationSyncRunner.RunAsync(
                connection,
                new IntegrationSyncRunRequest
                {
                    Trigger = IntegrationRunTriggers.Webhook,
                    IdempotencyKey = replayKey
                },
                cancellationToken);

            AuditTraceContext.SetIntegrationTrace(
                HttpContext,
                connectionId: connection.Id,
                providerKey: connection.ProviderKey,
                runId: runResult.RunId,
                eventId: inboxEvent.ExternalEventId,
                correlationId: inboxEvent.CorrelationId ?? replayKey);

            inboxEvent.ReplayCount = replaySequence;
            inboxEvent.RunId = runResult.RunId;
            inboxEvent.Status = runResult.Success ? IntegrationEventStatuses.Processed : IntegrationEventStatuses.Failed;
            inboxEvent.ErrorMessage = runResult.Success ? null : Truncate(runResult.Message, 2048);
            inboxEvent.ProcessedAt = DateTime.UtcNow;
            inboxEvent.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);

            await _auditLogger.LogAsync(HttpContext, "integrations.inbox.replay", nameof(IntegrationInboxEvent), inboxEvent.Id,
                $"ProviderKey={inboxEvent.ProviderKey}, RunId={runResult.RunId}, Success={runResult.Success}");

            return Ok(new
            {
                inboxEvent.Id,
                inboxEvent.ProviderKey,
                replayCount = inboxEvent.ReplayCount,
                runResult.RunId,
                runResult.Success,
                runResult.Status,
                runResult.Message,
                runResult.Deduplicated
            });
        }

        [HttpGet("events/outbox")]
        public async Task<IActionResult> GetOutboxEvents(
            [FromQuery] string? providerKey = null,
            [FromQuery] string? status = null,
            [FromQuery] int limit = 100,
            CancellationToken cancellationToken = default)
        {
            var query = _context.IntegrationOutboxEvents.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(providerKey)) query = query.Where(e => e.ProviderKey == providerKey.Trim().ToLowerInvariant());
            if (!string.IsNullOrWhiteSpace(status)) query = query.Where(e => e.Status == status.Trim().ToLowerInvariant());
            var rows = await query.OrderByDescending(e => e.CreatedAt).Take(Math.Clamp(limit, 1, 500))
                .Select(e => new
                {
                    e.Id, e.ConnectionId, e.RunId, e.ProviderKey, e.EventType, e.EntityType, e.EntityId,
                    e.IdempotencyKey, e.Status, e.AttemptCount, e.NextAttemptAt, e.DispatchedAt, e.ErrorCode,
                    e.ErrorMessage, e.DeadLettered, e.CreatedAt
                })
                .ToListAsync(cancellationToken);
            return Ok(rows);
        }

        [HttpPost("events/outbox/{id}/replay")]
        public async Task<IActionResult> ReplayOutboxEvent(string id, CancellationToken cancellationToken)
        {
            var outboxEvent = await _context.IntegrationOutboxEvents.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
            if (outboxEvent == null)
            {
                return NotFound(new { message = "Outbox event not found." });
            }

            outboxEvent.Status = IntegrationEventStatuses.Pending;
            outboxEvent.DeadLettered = false;
            outboxEvent.NextAttemptAt = DateTime.UtcNow;
            outboxEvent.ErrorCode = null;
            outboxEvent.ErrorMessage = null;
            outboxEvent.AttemptCount += 1;
            outboxEvent.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);

            AuditTraceContext.SetIntegrationTrace(
                HttpContext,
                connectionId: outboxEvent.ConnectionId,
                providerKey: outboxEvent.ProviderKey,
                runId: outboxEvent.RunId,
                eventId: outboxEvent.Id,
                correlationId: outboxEvent.CorrelationId ?? outboxEvent.IdempotencyKey);

            await _auditLogger.LogAsync(HttpContext, "integrations.outbox.replay", nameof(IntegrationOutboxEvent), outboxEvent.Id,
                $"ProviderKey={outboxEvent.ProviderKey}, EventType={outboxEvent.EventType}, AttemptCount={outboxEvent.AttemptCount}");

            return Ok(new
            {
                outboxEvent.Id,
                outboxEvent.ProviderKey,
                outboxEvent.EventType,
                outboxEvent.Status,
                outboxEvent.AttemptCount,
                outboxEvent.NextAttemptAt
            });
        }

        [HttpGet("runs")]
        public async Task<IActionResult> GetRuns(
            [FromQuery] string? providerKey = null,
            [FromQuery] string? status = null,
            [FromQuery] string? connectionId = null,
            [FromQuery] int limit = 100,
            CancellationToken cancellationToken = default)
        {
            var query = _context.IntegrationRuns.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(providerKey)) query = query.Where(r => r.ProviderKey == providerKey.Trim().ToLowerInvariant());
            if (!string.IsNullOrWhiteSpace(status)) query = query.Where(r => r.Status == status.Trim().ToLowerInvariant());
            if (!string.IsNullOrWhiteSpace(connectionId)) query = query.Where(r => r.ConnectionId == connectionId.Trim());

            var rows = await query.OrderByDescending(r => r.CreatedAt)
                .Take(Math.Clamp(limit, 1, 500))
                .Select(r => new
                {
                    r.Id,
                    r.ConnectionId,
                    r.ProviderKey,
                    r.Trigger,
                    r.Status,
                    r.AttemptCount,
                    r.MaxAttempts,
                    r.IdempotencyKey,
                    r.ErrorCode,
                    r.ErrorMessage,
                    r.IsDeadLetter,
                    r.CreatedAt,
                    r.StartedAt,
                    r.CompletedAt
                })
                .ToListAsync(cancellationToken);

            return Ok(rows);
        }

        [HttpPost("runs/{id}/replay")]
        public async Task<IActionResult> ReplayRun(string id, CancellationToken cancellationToken)
        {
            var run = await _context.IntegrationRuns.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
            if (run == null)
            {
                return NotFound(new { message = "Integration run not found." });
            }

            var connection = await _context.IntegrationConnections.FirstOrDefaultAsync(c => c.Id == run.ConnectionId, cancellationToken);
            if (connection == null)
            {
                return NotFound(new { message = "Integration connection not found for run." });
            }

            var guardDecision = await _integrationOperationsGuard.EvaluateForConnectionAsync(
                connection,
                IntegrationOperationKinds.Replay,
                cancellationToken);
            if (!guardDecision.Allowed)
            {
                return StatusCode(StatusCodes.Status423Locked, new { message = guardDecision.Message ?? "Sync replay is blocked." });
            }

            var replayKey = $"run-replay:{connection.Id}:{run.Id}:{DateTime.UtcNow:yyyyMMddHHmmss}";
            var replayResult = await _integrationSyncRunner.RunAsync(
                connection,
                new IntegrationSyncRunRequest
                {
                    Trigger = IntegrationRunTriggers.Manual,
                    IdempotencyKey = replayKey,
                    MaxAttempts = run.MaxAttempts > 0 ? run.MaxAttempts : null
                },
                cancellationToken);

            AuditTraceContext.SetIntegrationTrace(
                HttpContext,
                connectionId: connection.Id,
                providerKey: connection.ProviderKey,
                runId: replayResult.RunId,
                correlationId: replayKey);

            await _auditLogger.LogAsync(
                HttpContext,
                "integrations.run.replay",
                nameof(IntegrationRun),
                run.Id,
                $"ReplayRunId={replayResult.RunId}, SourceRunId={run.Id}, ProviderKey={connection.ProviderKey}, Success={replayResult.Success}");

            return Ok(new
            {
                sourceRunId = run.Id,
                replayRunId = replayResult.RunId,
                replayResult.Success,
                replayResult.Status,
                replayResult.Message,
                replayResult.SyncedCount,
                replayResult.AttemptCount,
                replayResult.IsDeadLetter,
                replayResult.Deduplicated
            });
        }

        [HttpGet("security/secrets/status")]
        public async Task<IActionResult> GetSecretStoreStatus(CancellationToken cancellationToken)
        {
            var keyRing = await _integrationSecretKeyProvider.GetKeyRingAsync(cancellationToken);
            var providerMode = (_configuration["Security:IntegrationSecrets:Provider"] ?? "config").Trim().ToLowerInvariant();

            var keyCounts = await _context.IntegrationSecrets.AsNoTracking()
                .GroupBy(s => new { s.EncryptionProvider, s.EncryptionKeyId })
                .Select(g => new
                {
                    g.Key.EncryptionProvider,
                    g.Key.EncryptionKeyId,
                    Count = g.Count()
                })
                .ToListAsync(cancellationToken);

            var scopeMatrix = Enum.GetValues<IntegrationSecretScope>()
                .Select(scope => new
                {
                    scope = scope.ToString(),
                    read = Allows(scope, IntegrationSecretOperation.Read),
                    write = Allows(scope, IntegrationSecretOperation.Write),
                    delete = Allows(scope, IntegrationSecretOperation.Delete),
                    rotate = Allows(scope, IntegrationSecretOperation.Rotate)
                })
                .ToList();

            return Ok(new
            {
                providerMode,
                encryptionProviderId = _integrationSecretCryptoService.EncryptionProviderId,
                legacyPlaintextAllowed = _integrationSecretCryptoService.LegacyPlaintextAllowed,
                keyRingSource = keyRing.Source,
                activeKeyId = keyRing.ActiveKeyId,
                configuredKeyCount = keyRing.Keys.Count,
                rotationEnabled = _configuration.GetValue("Security:IntegrationSecrets:RotationEnabled", true),
                rotationIntervalMinutes = _configuration.GetValue("Security:IntegrationSecrets:RotationIntervalMinutes", 720),
                entries = new
                {
                    total = await _context.IntegrationSecrets.AsNoTracking().CountAsync(cancellationToken),
                    byKey = keyCounts
                },
                scopeMatrix
            });
        }

        [HttpPost("security/secrets/rotate")]
        public async Task<IActionResult> RotateSecretsNow(CancellationToken cancellationToken)
        {
            var rotated = await _integrationSecretStore.RotateOutdatedSecretsAsync(
                IntegrationSecretScope.Rotation,
                cancellationToken);
            if (rotated > 0)
            {
                await _context.SaveChangesAsync(cancellationToken);
            }

            await _auditLogger.LogAsync(
                HttpContext,
                "integrations.secrets.rotate",
                nameof(IntegrationSecret),
                "tenant",
                $"Rotated={rotated}");

            return Ok(new
            {
                rotated,
                executedAt = DateTime.UtcNow
            });
        }

        private static object BuildValidationKpiSeries(IEnumerable<dynamic> validationRuns, string bucketKind)
        {
            var buckets = new Dictionary<DateTime, ValidationKpiAccumulator>();

            foreach (var run in validationRuns)
            {
                var bucket = GetBucketStart((DateTime)run.CreatedAt, bucketKind);
                if (!buckets.TryGetValue(bucket, out var acc))
                {
                    acc = new ValidationKpiAccumulator();
                    buckets[bucket] = acc;
                }

                acc.Runs++;
                acc.TotalCases += (int)run.TotalCases;
                acc.PassedCases += (int)run.PassedCases;
                acc.FailedCases += (int)run.FailedCases;

                var details = ParseValidationRunDetailCounts(run.ResultJson as string);
                acc.HumanReviewFalsePositives += details.HumanReviewFalsePositives;
                acc.HumanReviewFalseNegatives += details.HumanReviewFalseNegatives;
                acc.SupportLevelMismatches += details.SupportLevelMismatches;
            }

            var ordered = buckets.OrderBy(k => k.Key).ToList();
            var totalCases = ordered.Sum(x => x.Value.TotalCases);
            var falsePos = ordered.Sum(x => x.Value.HumanReviewFalsePositives);
            var falseNeg = ordered.Sum(x => x.Value.HumanReviewFalseNegatives);
            var supportMismatch = ordered.Sum(x => x.Value.SupportLevelMismatches);

            return new
            {
                summary = new
                {
                    runs = ordered.Sum(x => x.Value.Runs),
                    totalCases,
                    passedCases = ordered.Sum(x => x.Value.PassedCases),
                    failedCases = ordered.Sum(x => x.Value.FailedCases),
                    humanReviewFalsePositiveCount = falsePos,
                    humanReviewFalseNegativeCount = falseNeg,
                    supportLevelMismatchCount = supportMismatch,
                    humanReviewFalsePositiveRatePct = totalCases > 0 ? Math.Round((double)falsePos * 100d / totalCases, 2) : 0d,
                    humanReviewFalseNegativeRatePct = totalCases > 0 ? Math.Round((double)falseNeg * 100d / totalCases, 2) : 0d,
                    passRatePct = totalCases > 0 ? Math.Round((double)ordered.Sum(x => x.Value.PassedCases) * 100d / totalCases, 2) : 0d
                },
                series = ordered.Select(x => new
                {
                    bucketStartUtc = x.Key,
                    runs = x.Value.Runs,
                    totalCases = x.Value.TotalCases,
                    passedCases = x.Value.PassedCases,
                    failedCases = x.Value.FailedCases,
                    humanReviewFalsePositiveCount = x.Value.HumanReviewFalsePositives,
                    humanReviewFalseNegativeCount = x.Value.HumanReviewFalseNegatives,
                    supportLevelMismatchCount = x.Value.SupportLevelMismatches,
                    passRatePct = x.Value.TotalCases > 0 ? Math.Round((double)x.Value.PassedCases * 100d / x.Value.TotalCases, 2) : 0d
                }).ToList()
            };
        }

        private static object BuildFilingRejectionSeries(IEnumerable<dynamic> filingRows, string bucketKind)
        {
            var buckets = new Dictionary<DateTime, FilingKpiAccumulator>();
            foreach (var row in filingRows)
            {
                var eventAt = (DateTime)row.EventAt;
                var bucket = GetBucketStart(eventAt, bucketKind);
                if (!buckets.TryGetValue(bucket, out var acc))
                {
                    acc = new FilingKpiAccumulator();
                    buckets[bucket] = acc;
                }

                acc.Submissions++;
                var status = ((string?)row.Status ?? string.Empty).Trim().ToLowerInvariant();
                if (status == "rejected" || row.RejectedAt != null) acc.Rejected++;
                if (status == "accepted" || row.AcceptedAt != null) acc.Accepted++;
            }

            var ordered = buckets.OrderBy(k => k.Key).ToList();
            var total = ordered.Sum(x => x.Value.Submissions);
            var rejected = ordered.Sum(x => x.Value.Rejected);
            var accepted = ordered.Sum(x => x.Value.Accepted);
            return new
            {
                summary = new
                {
                    submissions = total,
                    rejected,
                    accepted,
                    rejectionRatePct = total > 0 ? Math.Round((double)rejected * 100d / total, 2) : 0d
                },
                series = ordered.Select(x => new
                {
                    bucketStartUtc = x.Key,
                    submissions = x.Value.Submissions,
                    rejected = x.Value.Rejected,
                    accepted = x.Value.Accepted,
                    rejectionRatePct = x.Value.Submissions > 0 ? Math.Round((double)x.Value.Rejected * 100d / x.Value.Submissions, 2) : 0d
                }).ToList()
            };
        }

        private static object BuildRuleLeadTimeSeries(IEnumerable<dynamic> ruleChangeRows, string bucketKind)
        {
            var samples = new List<(DateTime Bucket, double Hours)>();
            foreach (var row in ruleChangeRows)
            {
                if (row.ReviewedAt is not DateTime reviewedAt) continue;
                var hours = Math.Max(0d, (reviewedAt - (DateTime)row.CreatedAt).TotalHours);
                samples.Add((GetBucketStart(reviewedAt, bucketKind), hours));
            }

            var grouped = samples.GroupBy(s => s.Bucket).OrderBy(g => g.Key).ToList();
            var allHours = samples.Select(s => s.Hours).ToList();

            return new
            {
                summary = new
                {
                    samples = allHours.Count,
                    avgHours = allHours.Count > 0 ? Math.Round(allHours.Average(), 2) : 0d,
                    p50Hours = Percentile(allHours, 0.50),
                    p90Hours = Percentile(allHours, 0.90)
                },
                series = grouped.Select(g =>
                {
                    var hours = g.Select(x => x.Hours).ToList();
                    return new
                    {
                        bucketStartUtc = g.Key,
                        samples = hours.Count,
                        avgHours = Math.Round(hours.Average(), 2),
                        p50Hours = Percentile(hours, 0.50),
                        p90Hours = Percentile(hours, 0.90)
                    };
                }).ToList()
            };
        }

        private static object BuildPrebillAdjustmentSeries(IEnumerable<dynamic> prebillLineRows, string bucketKind)
        {
            var buckets = new Dictionary<DateTime, PrebillAdjustmentAccumulator>();
            foreach (var row in prebillLineRows)
            {
                var bucket = GetBucketStart((DateTime)row.CreatedAt, bucketKind);
                if (!buckets.TryGetValue(bucket, out var acc))
                {
                    acc = new PrebillAdjustmentAccumulator();
                    buckets[bucket] = acc;
                }

                acc.TotalLines++;
                var adjusted = (decimal)row.ProposedAmount != (decimal)row.ApprovedAmount
                               || (decimal)row.DiscountAmount > 0m
                               || (decimal)row.WriteDownAmount > 0m
                               || string.Equals((string?)row.Status, "excluded", StringComparison.OrdinalIgnoreCase);
                if (adjusted) acc.AdjustedLines++;
            }

            var ordered = buckets.OrderBy(k => k.Key).ToList();
            var totalLines = ordered.Sum(x => x.Value.TotalLines);
            var adjustedLines = ordered.Sum(x => x.Value.AdjustedLines);

            return new
            {
                summary = new
                {
                    totalLines,
                    adjustedLines,
                    adjustmentRatePct = totalLines > 0 ? Math.Round((double)adjustedLines * 100d / totalLines, 2) : 0d
                },
                series = ordered.Select(x => new
                {
                    bucketStartUtc = x.Key,
                    totalLines = x.Value.TotalLines,
                    adjustedLines = x.Value.AdjustedLines,
                    adjustmentRatePct = x.Value.TotalLines > 0 ? Math.Round((double)x.Value.AdjustedLines * 100d / x.Value.TotalLines, 2) : 0d
                }).ToList()
            };
        }

        private static object BuildCollectionCycleMetrics(
            IEnumerable<dynamic> invoiceRows,
            IEnumerable<dynamic> paymentRows,
            IEnumerable<dynamic> allocationRows,
            IReadOnlyDictionary<string, string> payorClientTypeMap,
            string bucketKind)
        {
            var paymentsByInvoice = paymentRows
                .Where(p => p.InvoiceId is string)
                .GroupBy(p => (string)p.InvoiceId!)
                .ToDictionary(
                    g => g.Key,
                    g => g.Where(x => x.ProcessedAt is DateTime).Select(x => (DateTime)x.ProcessedAt!).OrderBy(x => x).ToList(),
                    StringComparer.Ordinal);

            var allocationsByInvoice = allocationRows
                .Where(a => a.InvoiceId is string && a.AppliedAt is DateTime)
                .GroupBy(a => (string)a.InvoiceId!)
                .ToDictionary(
                    g => g.Key,
                    g => g
                        .OrderBy(x => (DateTime)x.AppliedAt)
                        .Select(x => new
                        {
                            AppliedAt = (DateTime)x.AppliedAt,
                            PayorClientId = (string?)x.PayorClientId,
                            ClientId = (string?)x.ClientId,
                            Amount = (decimal)x.Amount
                        })
                        .ToList(),
                    StringComparer.Ordinal);

            var samples = new List<(DateTime Bucket, double FirstPaymentDays, double? PaidInFullDays)>();
            var segmentedSamples = new List<(DateTime Bucket, string Segment, double FirstPaymentDays, double? PaidInFullDays)>();
            foreach (var invoice in invoiceRows)
            {
                if (!paymentsByInvoice.TryGetValue((string)invoice.Id, out var paymentDates) || paymentDates.Count == 0)
                {
                    continue;
                }

                var issueDate = ((DateTime)invoice.IssueDate).ToUniversalTime();
                var firstPayment = paymentDates[0].ToUniversalTime();
                if (firstPayment < issueDate) continue;

                double? paidInFullDays = null;
                var statusString = invoice.Status?.ToString() ?? string.Empty;
                var paidInFull = string.Equals(statusString, "Paid", StringComparison.OrdinalIgnoreCase) ||
                                 ((decimal)invoice.AmountPaid >= (decimal)invoice.Total && (decimal)invoice.Total > 0m);
                if (paidInFull)
                {
                    var lastPayment = paymentDates[^1].ToUniversalTime();
                    if (lastPayment >= issueDate)
                    {
                        paidInFullDays = (lastPayment - issueDate).TotalDays;
                    }
                }

                samples.Add((GetBucketStart(firstPayment, bucketKind), (firstPayment - issueDate).TotalDays, paidInFullDays));

                var payorSegment = "client";
                if (allocationsByInvoice.TryGetValue((string)invoice.Id, out var invoiceAllocations) && invoiceAllocations.Count > 0)
                {
                    var firstAllocation = invoiceAllocations[0];
                    payorSegment = ClassifyPayorSegment((string?)invoice.ClientId, firstAllocation.PayorClientId, payorClientTypeMap);
                }

                segmentedSamples.Add((GetBucketStart(firstPayment, bucketKind), payorSegment, (firstPayment - issueDate).TotalDays, paidInFullDays));
            }

            var grouped = samples.GroupBy(s => s.Bucket).OrderBy(g => g.Key).ToList();
            var firstPaymentDaysAll = samples.Select(s => s.FirstPaymentDays).ToList();
            var paidInFullDaysAll = samples.Where(s => s.PaidInFullDays.HasValue).Select(s => s.PaidInFullDays!.Value).ToList();

            var segmentSummary = BuildCollectionCycleSegmentSummary(segmentedSamples);
            var segmentSeries = segmentedSamples
                .GroupBy(s => new { s.Bucket, s.Segment })
                .OrderBy(g => g.Key.Bucket)
                .ThenBy(g => g.Key.Segment, StringComparer.Ordinal)
                .Select(g =>
                {
                    var first = g.Select(x => x.FirstPaymentDays).ToList();
                    var full = g.Where(x => x.PaidInFullDays.HasValue).Select(x => x.PaidInFullDays!.Value).ToList();
                    return new
                    {
                        bucketStartUtc = g.Key.Bucket,
                        segment = g.Key.Segment,
                        invoiceCount = g.Count(),
                        avgFirstPaymentDays = first.Count > 0 ? Math.Round(first.Average(), 2) : 0d,
                        p50FirstPaymentDays = Percentile(first, 0.50),
                        p90FirstPaymentDays = Percentile(first, 0.90),
                        avgPaidInFullDays = full.Count > 0 ? Math.Round(full.Average(), 2) : 0d,
                        p50PaidInFullDays = Percentile(full, 0.50),
                        p90PaidInFullDays = Percentile(full, 0.90)
                    };
                })
                .ToList();

            return new
            {
                summary = new
                {
                    invoiceCount = invoiceRows.Count(),
                    collectedInvoiceCount = samples.Count,
                    avgFirstPaymentDays = firstPaymentDaysAll.Count > 0 ? Math.Round(firstPaymentDaysAll.Average(), 2) : 0d,
                    p50FirstPaymentDays = Percentile(firstPaymentDaysAll, 0.50),
                    p90FirstPaymentDays = Percentile(firstPaymentDaysAll, 0.90),
                    avgPaidInFullDays = paidInFullDaysAll.Count > 0 ? Math.Round(paidInFullDaysAll.Average(), 2) : 0d,
                    p50PaidInFullDays = Percentile(paidInFullDaysAll, 0.50),
                    p90PaidInFullDays = Percentile(paidInFullDaysAll, 0.90),
                    payorSegments = segmentSummary
                },
                series = grouped.Select(g =>
                {
                    var first = g.Select(x => x.FirstPaymentDays).ToList();
                    var full = g.Where(x => x.PaidInFullDays.HasValue).Select(x => x.PaidInFullDays!.Value).ToList();
                    return new
                    {
                        bucketStartUtc = g.Key,
                        invoiceCount = g.Count(),
                        avgFirstPaymentDays = Math.Round(first.Average(), 2),
                        p50FirstPaymentDays = Percentile(first, 0.50),
                        p90FirstPaymentDays = Percentile(first, 0.90),
                        avgPaidInFullDays = full.Count > 0 ? Math.Round(full.Average(), 2) : 0d
                    };
                }).ToList(),
                payorSegmentSeries = segmentSeries
            };
        }

        private static object BuildCollectionCycleSegmentSummary(IEnumerable<(DateTime Bucket, string Segment, double FirstPaymentDays, double? PaidInFullDays)> samples)
        {
            var grouped = samples
                .GroupBy(s => s.Segment, StringComparer.Ordinal)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .ToList();

            return grouped.Select(g =>
            {
                var first = g.Select(x => x.FirstPaymentDays).ToList();
                var full = g.Where(x => x.PaidInFullDays.HasValue).Select(x => x.PaidInFullDays!.Value).ToList();
                return new
                {
                    segment = g.Key,
                    invoiceCount = g.Count(),
                    avgFirstPaymentDays = first.Count > 0 ? Math.Round(first.Average(), 2) : 0d,
                    p50FirstPaymentDays = Percentile(first, 0.50),
                    p90FirstPaymentDays = Percentile(first, 0.90),
                    avgPaidInFullDays = full.Count > 0 ? Math.Round(full.Average(), 2) : 0d,
                    p50PaidInFullDays = Percentile(full, 0.50),
                    p90PaidInFullDays = Percentile(full, 0.90)
                };
            }).ToList();
        }

        private static string ClassifyPayorSegment(
            string? invoiceClientId,
            string? payorClientId,
            IReadOnlyDictionary<string, string> payorClientTypeMap)
        {
            if (string.IsNullOrWhiteSpace(payorClientId) ||
                string.Equals(payorClientId, invoiceClientId, StringComparison.Ordinal))
            {
                return "client";
            }

            if (payorClientTypeMap.TryGetValue(payorClientId, out var payorType) &&
                string.Equals(payorType?.Trim(), "Corporate", StringComparison.OrdinalIgnoreCase))
            {
                return "corporate";
            }

            return "third_party";
        }

        private static object BuildEbillingExactMetrics(IEnumerable<dynamic> ebillingEventRows, string bucketKind)
        {
            var rows = ebillingEventRows
                .Where(r =>
                    string.Equals((string?)r.EventType, "submission_result", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals((string?)r.EventType, "provider_error", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals((string?)r.EventType, "status_update", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var buckets = new Dictionary<DateTime, SimpleCountAccumulator>();
            foreach (var row in rows)
            {
                var bucket = GetBucketStart((DateTime)row.OccurredAt, bucketKind);
                if (!buckets.TryGetValue(bucket, out var acc))
                {
                    acc = new SimpleCountAccumulator();
                    buckets[bucket] = acc;
                }

                acc.Total++;
                var status = ((string?)row.Status ?? string.Empty).Trim().ToLowerInvariant();
                if (status is "rejected" or "error")
                {
                    acc.Flagged++;
                }
            }

            var ordered = buckets.OrderBy(k => k.Key).ToList();
            var total = ordered.Sum(x => x.Value.Total);
            var rejected = ordered.Sum(x => x.Value.Flagged);
            var providers = rows.Select(r => (string?)r.ProviderKey).Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            var finalEvents = rows.Count(r => (bool?)r.IsFinal == true);
            var hasProviderGradeEvents = rows.Any(r => !string.Equals((string?)r.ProviderKey, "billing-engine", StringComparison.OrdinalIgnoreCase));
            var dataQuality = hasProviderGradeEvents ? "exact_provider_event" : "partial_engine_event";
            var note = hasProviderGradeEvents
                ? "Provider-grade e-billing result events."
                : "Persisted e-billing events exist, but current rows are billing-engine/precheck events only. Provider adapter result events have not been observed yet.";

            return new
            {
                available = true,
                dataQuality,
                note,
                summary = new
                {
                    providers,
                    events = total,
                    finalEvents,
                    rejectedEvents = rejected,
                    rejectionRatePct = total > 0 ? Math.Round((double)rejected * 100d / total, 2) : 0d
                },
                series = ordered.Select(x => new
                {
                    bucketStartUtc = x.Key,
                    events = x.Value.Total,
                    rejectedEvents = x.Value.Flagged,
                    rejectionRatePct = x.Value.Total > 0 ? Math.Round((double)x.Value.Flagged * 100d / x.Value.Total, 2) : 0d
                }).ToList()
            };
        }

        private static object BuildEbillingProxyMetrics(IEnumerable<dynamic> billingReviewRows, string bucketKind)
        {
            var relevant = billingReviewRows
                .Where(r =>
                    string.Equals((string?)r.ItemType, "prebill_review", StringComparison.OrdinalIgnoreCase) &&
                    (ContainsIgnoreCase(r.Summary as string, "LEDES") ||
                     ContainsIgnoreCase(r.Summary as string, "UTBMS") ||
                     ContainsIgnoreCase(r.Title as string, "LEDES") ||
                     ContainsIgnoreCase(r.Title as string, "UTBMS")))
                .ToList();

            var buckets = new Dictionary<DateTime, SimpleCountAccumulator>();
            foreach (var row in relevant)
            {
                var bucket = GetBucketStart((DateTime)row.CreatedAt, bucketKind);
                if (!buckets.TryGetValue(bucket, out var acc))
                {
                    acc = new SimpleCountAccumulator();
                    buckets[bucket] = acc;
                }

                acc.Total++;
                if (string.Equals((string?)row.Decision, "reject", StringComparison.OrdinalIgnoreCase))
                {
                    acc.Flagged++;
                }
            }

            var ordered = buckets.OrderBy(k => k.Key).ToList();
            var total = ordered.Sum(x => x.Value.Total);
            var rejected = ordered.Sum(x => x.Value.Flagged);
            return new
            {
                available = true,
                dataQuality = "partial",
                note = "Proxy based on billing review items containing LEDES/UTBMS markers; provider-grade e-billing rejection tracking is not yet persisted.",
                summary = new
                {
                    reviewedItems = total,
                    rejectedItems = rejected,
                    rejectionRatePct = total > 0 ? Math.Round((double)rejected * 100d / total, 2) : 0d
                },
                series = ordered.Select(x => new
                {
                    bucketStartUtc = x.Key,
                    reviewedItems = x.Value.Total,
                    rejectedItems = x.Value.Flagged,
                    rejectionRatePct = x.Value.Total > 0 ? Math.Round((double)x.Value.Flagged * 100d / x.Value.Total, 2) : 0d
                }).ToList()
            };
        }

        private static object BuildTrustReconciliationHistorySeries(IEnumerable<dynamic> snapshotRows, string bucketKind)
        {
            var snapshots = snapshotRows.ToList();
            if (snapshots.Count == 0)
            {
                return new
                {
                    available = false,
                    dataQuality = "unavailable",
                    reason = "No trust reconciliation snapshots have been captured yet. Calling /api/legal-billing/trust/reconciliation will start snapshot history."
                };
            }

            var buckets = new Dictionary<DateTime, TrustReconciliationSnapshotAccumulator>();
            foreach (var row in snapshots)
            {
                var bucket = GetBucketStart((DateTime)row.AsOfUtc, bucketKind);
                if (!buckets.TryGetValue(bucket, out var acc))
                {
                    acc = new TrustReconciliationSnapshotAccumulator();
                    buckets[bucket] = acc;
                }

                acc.SnapshotCount++;
                acc.AccountCount = Math.Max(acc.AccountCount, (int)row.AccountCount);
                acc.MismatchedAccountCount = Math.Max(acc.MismatchedAccountCount, (int)row.MismatchedAccountCount);
                acc.LastAsOfUtc = row.AsOfUtc;
                acc.BankVsTrustLedgerDiff = (decimal)row.BankVsTrustLedgerDiff;
                acc.BankVsClientLedgerDiff = (decimal)row.BankVsClientLedgerDiff;
                acc.ClientLedgerVsTrustLedgerDiff = (decimal)row.ClientLedgerVsTrustLedgerDiff;
            }

            var ordered = buckets.OrderBy(k => k.Key).ToList();
            var latest = snapshots.OrderByDescending(s => (DateTime)s.AsOfUtc).First();
            return new
            {
                available = true,
                dataQuality = "computed_snapshot",
                summary = new
                {
                    snapshots = snapshots.Count,
                    latestAsOfUtc = (DateTime)latest.AsOfUtc,
                    latestAccountCount = (int)latest.AccountCount,
                    latestMismatchedAccountCount = (int)latest.MismatchedAccountCount,
                    latestBankVsTrustDiff = (decimal)latest.BankVsTrustLedgerDiff,
                    latestBankVsClientDiff = (decimal)latest.BankVsClientLedgerDiff,
                    latestClientVsTrustDiff = (decimal)latest.ClientLedgerVsTrustLedgerDiff
                },
                series = ordered.Select(x => new
                {
                    bucketStartUtc = x.Key,
                    snapshots = x.Value.SnapshotCount,
                    lastAsOfUtc = x.Value.LastAsOfUtc,
                    accountCount = x.Value.AccountCount,
                    mismatchedAccountCount = x.Value.MismatchedAccountCount,
                    bankVsTrustDiff = x.Value.BankVsTrustLedgerDiff,
                    bankVsClientDiff = x.Value.BankVsClientLedgerDiff,
                    clientVsTrustDiff = x.Value.ClientLedgerVsTrustLedgerDiff
                }).ToList()
            };
        }

        private static ValidationRunDetailCounts ParseValidationRunDetailCounts(string? resultJson)
        {
            if (string.IsNullOrWhiteSpace(resultJson))
            {
                return new ValidationRunDetailCounts();
            }

            try
            {
                using var doc = JsonDocument.Parse(resultJson);
                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                {
                    return new ValidationRunDetailCounts();
                }

                var counts = new ValidationRunDetailCounts();
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    counts.TotalRows++;
                    var expectedHr = GetJsonBool(item, "ExpectedRequiresHumanReview");
                    var actualHr = GetJsonBool(item, "ActualRequiresHumanReview");
                    if (expectedHr == true && actualHr == false) counts.HumanReviewFalseNegatives++;
                    if (expectedHr == false && actualHr == true) counts.HumanReviewFalsePositives++;

                    var expectedSupport = GetJsonString(item, "ExpectedSupportLevel");
                    var actualSupport = GetJsonString(item, "ActualSupportLevel");
                    if (!string.IsNullOrWhiteSpace(expectedSupport) &&
                        !string.IsNullOrWhiteSpace(actualSupport) &&
                        !string.Equals(expectedSupport, actualSupport, StringComparison.OrdinalIgnoreCase))
                    {
                        counts.SupportLevelMismatches++;
                    }
                }

                return counts;
            }
            catch (JsonException)
            {
                return new ValidationRunDetailCounts();
            }
        }

        private static bool? GetJsonBool(JsonElement element, string propertyName)
        {
            foreach (var prop in element.EnumerateObject())
            {
                if (!string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase)) continue;
                return prop.Value.ValueKind switch
                {
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.String when bool.TryParse(prop.Value.GetString(), out var parsed) => parsed,
                    _ => null
                };
            }
            return null;
        }

        private static string? GetJsonString(JsonElement element, string propertyName)
        {
            foreach (var prop in element.EnumerateObject())
            {
                if (!string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase)) continue;
                return prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() : prop.Value.GetRawText();
            }
            return null;
        }

        private static string NormalizeKpiBucket(string? bucket)
        {
            var normalized = bucket?.Trim().ToLowerInvariant();
            return normalized is "day" or "week" or "month" ? normalized : "week";
        }

        private static DateTime GetBucketStart(DateTime value, string bucketKind)
        {
            var utc = value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
            var date = utc.Date;
            return bucketKind switch
            {
                "day" => date,
                "month" => new DateTime(date.Year, date.Month, 1, 0, 0, 0, DateTimeKind.Utc),
                _ => date.AddDays(-(((int)date.DayOfWeek + 6) % 7)) // Monday-based week
            };
        }

        private static double Percentile(IReadOnlyCollection<double> values, double percentile)
        {
            if (values.Count == 0) return 0d;
            var sorted = values.OrderBy(v => v).ToList();
            var p = Math.Clamp(percentile, 0d, 1d);
            var index = (sorted.Count - 1) * p;
            var lower = (int)Math.Floor(index);
            var upper = (int)Math.Ceiling(index);
            if (lower == upper) return Math.Round(sorted[lower], 2);
            var fraction = index - lower;
            var interpolated = sorted[lower] + (sorted[upper] - sorted[lower]) * fraction;
            return Math.Round(interpolated, 2);
        }

        private static bool ContainsIgnoreCase(string? value, string needle) =>
            !string.IsNullOrWhiteSpace(value) && value.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

        private sealed class ValidationKpiAccumulator
        {
            public int Runs { get; set; }
            public int TotalCases { get; set; }
            public int PassedCases { get; set; }
            public int FailedCases { get; set; }
            public int HumanReviewFalsePositives { get; set; }
            public int HumanReviewFalseNegatives { get; set; }
            public int SupportLevelMismatches { get; set; }
        }

        private sealed class ValidationRunDetailCounts
        {
            public int TotalRows { get; set; }
            public int HumanReviewFalsePositives { get; set; }
            public int HumanReviewFalseNegatives { get; set; }
            public int SupportLevelMismatches { get; set; }
        }

        private sealed class FilingKpiAccumulator
        {
            public int Submissions { get; set; }
            public int Rejected { get; set; }
            public int Accepted { get; set; }
        }

        private sealed class PrebillAdjustmentAccumulator
        {
            public int TotalLines { get; set; }
            public int AdjustedLines { get; set; }
        }

        private sealed class SimpleCountAccumulator
        {
            public int Total { get; set; }
            public int Flagged { get; set; }
        }

        private sealed class TrustReconciliationSnapshotAccumulator
        {
            public int SnapshotCount { get; set; }
            public int AccountCount { get; set; }
            public int MismatchedAccountCount { get; set; }
            public DateTime? LastAsOfUtc { get; set; }
            public decimal BankVsTrustLedgerDiff { get; set; }
            public decimal BankVsClientLedgerDiff { get; set; }
            public decimal ClientLedgerVsTrustLedgerDiff { get; set; }
        }

        private string ResolveActorId()
        {
            return User.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
                   User.FindFirst("sub")?.Value ??
                   User.FindFirst(ClaimTypes.Email)?.Value ??
                   "unknown";
        }

        private static string? NormalizeRequired(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var trimmed = value.Trim();
            return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
        }

        private static string? Truncate(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var trimmed = value.Trim();
            return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
        }

        private static string NormalizeDirection(string? value)
        {
            var normalized = value?.Trim().ToLowerInvariant();
            return normalized is "inbound" or "outbound" ? normalized : "both";
        }

        private static string NormalizeProfileStatus(string? value)
        {
            var normalized = value?.Trim().ToLowerInvariant();
            return normalized is "draft" or "inactive" ? normalized : "active";
        }

        private static string? NormalizeConflictPolicy(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return IntegrationConflictPolicies.ManualReview;
            var normalized = value.Trim().ToLowerInvariant();
            return IntegrationConflictPolicies.All.Contains(normalized, StringComparer.Ordinal) ? normalized : null;
        }

        private static string? NormalizeJson(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            try
            {
                using var doc = JsonDocument.Parse(value);
                return doc.RootElement.GetRawText();
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private bool Allows(IntegrationSecretScope scope, IntegrationSecretOperation operation)
        {
            try
            {
                _integrationSecretAccessPolicy.EnsureAllowed(scope, operation);
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        public sealed class UpsertMappingProfileRequest
        {
            public string? Name { get; set; }
            public string? EntityType { get; set; }
            public string? Direction { get; set; }
            public string? Status { get; set; }
            public string? ConflictPolicy { get; set; }
            public bool? IsDefault { get; set; }
            public string? FieldMappingsJson { get; set; }
            public string? EnumMappingsJson { get; set; }
            public string? TaxMappingsJson { get; set; }
            public string? AccountMappingsJson { get; set; }
            public string? DefaultsJson { get; set; }
            public string? MetadataJson { get; set; }
            public string? ValidationSummary { get; set; }
            public DateTime? LastValidatedAt { get; set; }
        }

        public sealed class RunCanonicalActionRequest
        {
            public string? EntityType { get; set; }
            public string? LocalEntityId { get; set; }
            public string? ExternalEntityId { get; set; }
            public string? CorrelationId { get; set; }
            public string? IdempotencyKey { get; set; }
            public string? Cursor { get; set; }
            public string? DeltaToken { get; set; }
            public string? PayloadJson { get; set; }
            public bool DryRun { get; set; }
            public bool RequiresReview { get; set; } = true;
        }

        public sealed class ResolveConflictRequest
        {
            public string? Status { get; set; }
            public string? ResolutionType { get; set; }
            public string? ResolutionJson { get; set; }
            public string? Notes { get; set; }
        }

        public sealed class DecideReviewItemRequest
        {
            public string? Status { get; set; }
            public string? Decision { get; set; }
            public string? Notes { get; set; }
        }
    }
}
