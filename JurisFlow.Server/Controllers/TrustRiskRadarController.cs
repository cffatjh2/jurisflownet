using System.Globalization;
using System.Security.Claims;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using JurisFlow.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace JurisFlow.Server.Controllers
{
    [Route("api/legal-billing/trust-risk")]
    [ApiController]
    [Authorize(Policy = "StaffOnly")]
    public class TrustRiskRadarController : ControllerBase
    {
        private readonly JurisFlowDbContext _context;
        private readonly TrustRiskRadarService _trustRiskRadar;
        private readonly AuditLogger _auditLogger;

        public TrustRiskRadarController(
            JurisFlowDbContext context,
            TrustRiskRadarService trustRiskRadar,
            AuditLogger auditLogger)
        {
            _context = context;
            _trustRiskRadar = trustRiskRadar;
            _auditLogger = auditLogger;
        }

        [HttpGet("policy")]
        public async Task<ActionResult<TrustRiskPolicy>> GetActivePolicy(CancellationToken ct)
        {
            return Ok(await _trustRiskRadar.GetActivePolicyAsync(ct));
        }

        [HttpGet("policy/versions")]
        public async Task<ActionResult<IEnumerable<TrustRiskPolicy>>> GetPolicyVersions([FromQuery] int limit = 20, CancellationToken ct = default)
        {
            var policies = await _context.TrustRiskPolicies
                .OrderByDescending(p => p.IsActive)
                .ThenByDescending(p => p.VersionNumber)
                .ThenByDescending(p => p.CreatedAt)
                .Take(Math.Clamp(limit, 1, 100))
                .ToListAsync(ct);
            return Ok(policies);
        }

        [HttpGet("policy/templates")]
        public ActionResult<IEnumerable<TrustRiskRadarService.TrustRiskPolicyTemplateDescriptor>> GetPolicyTemplates()
        {
            return Ok(_trustRiskRadar.GetPolicyTemplates());
        }

        [HttpPost("policy")]
        public async Task<ActionResult<TrustRiskPolicy>> UpsertPolicy([FromBody] TrustRiskPolicyUpsertDto dto, CancellationToken ct)
        {
            if (dto == null)
            {
                return BadRequest(new { message = "Request body is required." });
            }

            if (!UserHasAnyRole("Admin", "SecurityAdmin"))
            {
                return Forbid();
            }

            try
            {
                var policy = await _trustRiskRadar.UpsertPolicyAsync(new TrustRiskRadarService.TrustRiskPolicyUpsertRequest
                {
                    PolicyKey = dto.PolicyKey,
                    TemplateKey = dto.TemplateKey,
                    PolicyTemplate = dto.PolicyTemplate,
                    Name = dto.Name,
                    Description = dto.Description,
                    WarnThreshold = dto.WarnThreshold,
                    ReviewThreshold = dto.ReviewThreshold,
                    SoftHoldThreshold = dto.SoftHoldThreshold,
                    HardHoldThreshold = dto.HardHoldThreshold,
                    FailMode = dto.FailMode,
                    EnabledRules = dto.EnabledRules,
                    RuleWeights = dto.RuleWeights,
                    ActionMap = dto.ActionMap,
                    OverrideRoles = dto.OverrideRoles,
                    ReleaseRoles = dto.ReleaseRoles,
                    CriticalDualApprovalSecondaryRoles = dto.CriticalDualApprovalSecondaryRoles,
                    CriticalDualApprovalEnabled = dto.CriticalDualApprovalEnabled,
                    HoldEscalationSlaMinutes = dto.HoldEscalationSlaMinutes,
                    SoftHoldExpiryHours = dto.SoftHoldExpiryHours,
                    HardHoldExpiryHours = dto.HardHoldExpiryHours,
                    RequireCriticalThresholdChangeReview = dto.RequireCriticalThresholdChangeReview,
                    CriticalThresholdChangeReason = dto.CriticalThresholdChangeReason,
                    OpsAlertsEnabled = dto.OpsAlertsEnabled,
                    OpsAlertChannels = dto.OpsAlertChannels,
                    TrustAccountOverrides = MapTrustAccountOverrides(dto.TrustAccountOverrides),
                    AdditionalMetadata = BuildAdditionalPolicyMetadata(dto)
                }, GetUserId(), ct);

                await _auditLogger.LogAsync(
                    HttpContext,
                    "trust_risk.policy.upsert",
                    nameof(TrustRiskPolicy),
                    policy.Id,
                    $"PolicyKey={policy.PolicyKey}, Version={policy.VersionNumber}, Warn={policy.WarnThreshold}, Review={policy.ReviewThreshold}, Soft={policy.SoftHoldThreshold}, Hard={policy.HardHoldThreshold}");

                return Ok(policy);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("events")]
        public async Task<ActionResult<IEnumerable<TrustRiskEvent>>> GetEvents([FromQuery] TrustRiskEventQueryDto query, CancellationToken ct)
        {
            var q = ApplyEventFilters(_context.TrustRiskEvents.AsQueryable(), query);

            var items = await q
                .OrderByDescending(e => e.CreatedAt)
                .Take(Math.Clamp(query.Limit ?? 100, 1, 500))
                .ToListAsync(ct);

            return Ok(items);
        }

        [HttpGet("events/{id}")]
        public async Task<ActionResult> GetEventDetail(string id, CancellationToken ct)
        {
            var riskEvent = await _context.TrustRiskEvents.FirstOrDefaultAsync(e => e.Id == id, ct);
            if (riskEvent == null)
            {
                return NotFound();
            }

            var actions = await _context.TrustRiskActions
                .Where(a => a.TrustRiskEventId == id)
                .OrderByDescending(a => a.CreatedAt)
                .ToListAsync(ct);
            var holds = await _context.TrustRiskHolds
                .Where(h => h.TrustRiskEventId == id)
                .OrderByDescending(h => h.PlacedAt)
                .ToListAsync(ct);
            var links = await _context.TrustRiskReviewLinks
                .Where(l => l.TrustRiskEventId == id)
                .OrderByDescending(l => l.CreatedAt)
                .ToListAsync(ct);

            IntegrationReviewQueueItem? reviewItem = null;
            if (!string.IsNullOrWhiteSpace(riskEvent.ReviewQueueItemId))
            {
                reviewItem = await _context.IntegrationReviewQueueItems.FirstOrDefaultAsync(r => r.Id == riskEvent.ReviewQueueItemId, ct);
            }

            BillingLedgerEntry? billingLedgerEntry = null;
            BillingPaymentAllocation? billingPaymentAllocation = null;
            TrustTransaction? trustTransaction = null;
            PaymentTransaction? paymentTransaction = null;
            Invoice? invoice = null;
            Matter? matter = null;
            Client? client = null;
            Client? payorClient = null;

            if (!string.IsNullOrWhiteSpace(riskEvent.BillingLedgerEntryId))
            {
                billingLedgerEntry = await _context.BillingLedgerEntries.FirstOrDefaultAsync(e => e.Id == riskEvent.BillingLedgerEntryId, ct);
            }
            if (!string.IsNullOrWhiteSpace(riskEvent.BillingPaymentAllocationId))
            {
                billingPaymentAllocation = await _context.BillingPaymentAllocations.FirstOrDefaultAsync(a => a.Id == riskEvent.BillingPaymentAllocationId, ct);
            }
            if (!string.IsNullOrWhiteSpace(riskEvent.TrustTransactionId))
            {
                trustTransaction = await _context.TrustTransactions.FirstOrDefaultAsync(t => t.Id == riskEvent.TrustTransactionId, ct);
            }
            if (!string.IsNullOrWhiteSpace(riskEvent.PaymentTransactionId))
            {
                paymentTransaction = await _context.PaymentTransactions.FirstOrDefaultAsync(p => p.Id == riskEvent.PaymentTransactionId, ct);
            }
            if (!string.IsNullOrWhiteSpace(riskEvent.InvoiceId))
            {
                invoice = await _context.Invoices.FirstOrDefaultAsync(i => i.Id == riskEvent.InvoiceId, ct);
            }
            if (!string.IsNullOrWhiteSpace(riskEvent.MatterId))
            {
                matter = await _context.Matters.FirstOrDefaultAsync(m => m.Id == riskEvent.MatterId, ct);
            }
            if (!string.IsNullOrWhiteSpace(riskEvent.ClientId))
            {
                client = await _context.Clients.FirstOrDefaultAsync(c => c.Id == riskEvent.ClientId, ct);
            }
            if (!string.IsNullOrWhiteSpace(riskEvent.PayorClientId))
            {
                payorClient = await _context.Clients.FirstOrDefaultAsync(c => c.Id == riskEvent.PayorClientId, ct);
            }

            return Ok(new
            {
                riskEvent,
                actions,
                holds,
                reviewLinks = links,
                reviewItem,
                related = new
                {
                    billingLedgerEntry,
                    billingPaymentAllocation,
                    trustTransaction,
                    paymentTransaction,
                    invoice,
                    matter,
                    client,
                    payorClient
                }
            });
        }

        [HttpPost("events/{id}/rescore")]
        public async Task<ActionResult> RescoreEvent(string id, [FromBody] TrustRiskRescoreDto? dto, CancellationToken ct)
        {
            var riskEvent = await _context.TrustRiskEvents.FirstOrDefaultAsync(e => e.Id == id, ct);
            if (riskEvent == null)
            {
                return NotFound();
            }

            TrustRiskEvent? rescored = riskEvent.SourceType switch
            {
                "billing_ledger_entry" => await RescoreLedgerAsync(riskEvent, ct),
                "billing_payment_allocation" => await RescoreAllocationAsync(riskEvent, ct),
                "trust_transaction" => await RescoreTrustTransactionAsync(riskEvent, ct),
                _ => null
            };

            if (rescored == null)
            {
                return BadRequest(new { message = "Source entity not found or event type is not rescore-supported." });
            }

            await _auditLogger.LogAsync(
                HttpContext,
                "trust_risk.event.rescore",
                nameof(TrustRiskEvent),
                riskEvent.Id,
                $"NewEventId={rescored.Id}, Reason={dto?.Reason}");

            return Ok(new
            {
                originalEventId = riskEvent.Id,
                rescoredEventId = rescored.Id,
                rescoredDecision = rescored.Decision,
                rescoredSeverity = rescored.Severity,
                rescoredScore = rescored.RiskScore
            });
        }

        [HttpPost("events/rescore-batch")]
        public async Task<ActionResult> RescoreEventsBatch([FromBody] TrustRiskBatchRescoreDto? dto, CancellationToken ct)
        {
            dto ??= new TrustRiskBatchRescoreDto();
            var q = ApplyEventFilters(_context.TrustRiskEvents.AsQueryable(), new TrustRiskEventQueryDto
            {
                Status = dto.Status,
                Decision = dto.Decision,
                Severity = dto.Severity,
                SourceType = dto.SourceType,
                InvoiceId = dto.InvoiceId,
                MatterId = dto.MatterId,
                ClientId = dto.ClientId,
                FromUtc = dto.FromUtc,
                ToUtc = dto.ToUtc,
                Limit = dto.Limit
            });

            if (dto.EventIds != null && dto.EventIds.Count > 0)
            {
                var ids = dto.EventIds.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).Distinct().ToList();
                q = q.Where(e => ids.Contains(e.Id));
            }

            var sourceEvents = await q
                .OrderByDescending(e => e.CreatedAt)
                .Take(Math.Clamp(dto.Limit ?? 50, 1, 200))
                .ToListAsync(ct);

            var rescored = new List<object>();
            var skipped = 0;
            foreach (var ev in sourceEvents)
            {
                TrustRiskEvent? newEvent = ev.SourceType switch
                {
                    "billing_ledger_entry" => await RescoreLedgerAsync(ev, ct),
                    "billing_payment_allocation" => await RescoreAllocationAsync(ev, ct),
                    "trust_transaction" => await RescoreTrustTransactionAsync(ev, ct),
                    _ => null
                };

                if (newEvent == null)
                {
                    skipped++;
                    continue;
                }

                rescored.Add(new
                {
                    originalEventId = ev.Id,
                    rescoredEventId = newEvent.Id,
                    decision = newEvent.Decision,
                    severity = newEvent.Severity,
                    score = newEvent.RiskScore
                });
            }

            await _auditLogger.LogAsync(HttpContext, "trust_risk.event.rescore_batch", nameof(TrustRiskEvent), null, $"Count={rescored.Count}, Skipped={skipped}, Reason={dto.Reason}");
            return Ok(new
            {
                requested = sourceEvents.Count,
                rescoredCount = rescored.Count,
                skippedCount = skipped,
                items = rescored
            });
        }

        [HttpPost("events/{id}/ack")]
        public async Task<ActionResult> AcknowledgeEvent(string id, [FromBody] TrustRiskEventAcknowledgeDto? dto, CancellationToken ct)
        {
            var updated = await _trustRiskRadar.AcknowledgeEventAsync(id, GetUserId(), dto?.Note, ct);
            if (updated == null) return NotFound();

            await _auditLogger.LogAsync(HttpContext, "trust_risk.event.ack", nameof(TrustRiskEvent), id, $"Note={dto?.Note}");
            return Ok(updated);
        }

        [HttpPost("events/{id}/assign")]
        public async Task<ActionResult> AssignEvent(string id, [FromBody] TrustRiskEventAssignDto? dto, CancellationToken ct)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.AssigneeUserId))
            {
                return BadRequest(new { message = "AssigneeUserId is required." });
            }

            try
            {
                var updated = await _trustRiskRadar.AssignEventAsync(id, dto.AssigneeUserId, GetUserId(), dto.Note, ct);
                if (updated == null) return NotFound();
                await _auditLogger.LogAsync(HttpContext, "trust_risk.event.assign", nameof(TrustRiskEvent), id, $"Assignee={dto.AssigneeUserId}, Note={dto.Note}");
                return Ok(updated);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("events/{id}/review-disposition")]
        public async Task<ActionResult> SetReviewDisposition(string id, [FromBody] TrustRiskReviewDispositionDto? dto, CancellationToken ct)
        {
            if (dto == null)
            {
                return BadRequest(new { message = "Request body is required." });
            }

            try
            {
                var updated = await _trustRiskRadar.SetReviewDispositionAsync(id, dto.Disposition ?? string.Empty, dto.Reason ?? string.Empty, dto.ApproverReason, GetUserId(), GetUserRole(), ct);
                if (updated == null) return NotFound();
                await _auditLogger.LogAsync(HttpContext, "trust_risk.review.disposition", nameof(TrustRiskEvent), id, $"Disposition={dto.Disposition}");
                return Ok(updated);
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("metrics")]
        public async Task<ActionResult> GetMetrics([FromQuery] int days = 30, CancellationToken ct = default)
        {
            await _trustRiskRadar.ApplyHoldSlaTransitionsAsync(ct);
            var since = DateTime.UtcNow.AddDays(-Math.Clamp(days, 1, 365));

            var events = await _context.TrustRiskEvents.AsNoTracking()
                .Where(e => e.CreatedAt >= since)
                .Select(e => new
                {
                    e.Id,
                    e.CreatedAt,
                    e.Status,
                    e.Decision,
                    e.Severity,
                    e.ReviewQueueItemId
                })
                .ToListAsync(ct);

            var eventIds = events.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);

            var reviewLinks = await _context.TrustRiskReviewLinks.AsNoTracking()
                .Where(l => eventIds.Contains(l.TrustRiskEventId))
                .Select(l => new { l.TrustRiskEventId, l.ReviewQueueItemId, l.Status, l.UpdatedAt, l.CreatedAt, l.MetadataJson })
                .ToListAsync(ct);

            var reviewIds = reviewLinks.Select(l => l.ReviewQueueItemId).Where(v => !string.IsNullOrWhiteSpace(v)).Distinct().ToList();
            var reviewItems = reviewIds.Count == 0
                ? []
                : await _context.IntegrationReviewQueueItems.AsNoTracking()
                    .Where(r => reviewIds.Contains(r.Id))
                    .Select(r => new { r.Id, r.Status, r.CreatedAt, r.UpdatedAt })
                    .ToListAsync(ct);

            var reviewById = reviewItems.ToDictionary(r => r.Id, StringComparer.Ordinal);

            var holds = await _context.TrustRiskHolds.AsNoTracking()
                .Where(h => h.CreatedAt >= since)
                .Select(h => new
                {
                    h.Id,
                    h.TrustRiskEventId,
                    h.HoldType,
                    h.Status,
                    h.PlacedAt,
                    h.ReleasedAt
                })
                .ToListAsync(ct);

            var reviewCompletedDurationsProxy = new List<double>();
            var reviewCompletedDurationsExact = new List<double>();
            var falsePositiveCount = 0;
            var reviewResolvedCount = 0;
            var dispositionsByEventId = new Dictionary<string, string>(StringComparer.Ordinal);
            var dispositionAtByEventId = new Dictionary<string, DateTime>(StringComparer.Ordinal);
            var ruleCounters = new Dictionary<string, RuleCounter>(StringComparer.OrdinalIgnoreCase);

            foreach (var link in reviewLinks)
            {
                var disposition = TryReadJsonString(link.MetadataJson, "reviewDisposition");
                if (!string.IsNullOrWhiteSpace(disposition))
                {
                    dispositionsByEventId[link.TrustRiskEventId] = disposition!;
                }
            }

            // Prefer event metadata disposition; fall back to review-link metadata.
            var eventMetaDispositions = await _context.TrustRiskEvents.AsNoTracking()
                .Where(e => eventIds.Contains(e.Id))
                .Select(e => new { e.Id, e.MetadataJson, e.RiskReasonsJson })
                .ToListAsync(ct);

            foreach (var ev in events)
            {
                if (string.IsNullOrWhiteSpace(ev.ReviewQueueItemId)) continue;
                if (!reviewById.TryGetValue(ev.ReviewQueueItemId, out var review)) continue;

                var status = (review.Status ?? string.Empty).ToLowerInvariant();
                if (status is "in_review" or "resolved" or "rejected")
                {
                    reviewCompletedDurationsProxy.Add((review.UpdatedAt - ev.CreatedAt).TotalMinutes);
                }

                if (status == "rejected")
                {
                    falsePositiveCount++;
                }
                if (status is "resolved" or "rejected")
                {
                    reviewResolvedCount++;
                }
            }

            foreach (var ev in eventMetaDispositions)
            {
                var disposition = TryReadJsonString(ev.MetadataJson, "reviewDisposition");
                if (!string.IsNullOrWhiteSpace(disposition))
                {
                    dispositionsByEventId[ev.Id] = disposition!;
                }

                if (TryReadJsonUtc(ev.MetadataJson, "reviewDispositionAtUtc", out var dispositionAtUtc))
                {
                    dispositionAtByEventId[ev.Id] = dispositionAtUtc;
                }

                foreach (var ruleCode in ExtractReasonCodes(ev.RiskReasonsJson))
                {
                    if (!ruleCounters.TryGetValue(ruleCode, out var counter))
                    {
                        counter = new RuleCounter();
                        ruleCounters[ruleCode] = counter;
                    }

                    counter.Total++;
                    if (dispositionsByEventId.TryGetValue(ev.Id, out var d))
                    {
                        var normalized = d.ToLowerInvariant();
                        if (normalized == "false_positive") counter.FalsePositive++;
                        else if (normalized == "true_positive") counter.TruePositive++;
                        else if (normalized == "acceptable_exception") counter.AcceptableException++;
                    }
                }
            }

            var eventsById = events.ToDictionary(e => e.Id, StringComparer.Ordinal);
            foreach (var kvp in dispositionAtByEventId)
            {
                if (!eventsById.TryGetValue(kvp.Key, out var ev)) continue;
                var minutes = (kvp.Value - ev.CreatedAt).TotalMinutes;
                if (minutes >= 0) reviewCompletedDurationsExact.Add(minutes);
            }

            var releasedHolds = holds.Where(h => h.ReleasedAt != null).ToList();
            var holdReleaseDurations = releasedHolds
                .Select(h => (h.ReleasedAt!.Value - h.PlacedAt).TotalMinutes)
                .Where(v => v >= 0)
                .ToList();
            var holdReleaseBySeverity = releasedHolds
                .Where(h => h.ReleasedAt != null && !string.IsNullOrWhiteSpace(h.TrustRiskEventId))
                .Select(h =>
                {
                    var severity = eventsById.TryGetValue(h.TrustRiskEventId, out var ev) && !string.IsNullOrWhiteSpace(ev.Severity)
                        ? ev.Severity!.ToLowerInvariant()
                        : "unknown";
                    return new
                    {
                        Severity = severity,
                        Minutes = (h.ReleasedAt!.Value - h.PlacedAt).TotalMinutes
                    };
                })
                .Where(x => x.Minutes >= 0)
                .GroupBy(x => x.Severity)
                .ToDictionary(
                    g => g.Key,
                    g =>
                    {
                        var values = g.Select(x => x.Minutes).OrderBy(v => v).ToList();
                        return new
                        {
                            count = values.Count,
                            meanMinutes = values.Count == 0 ? (double?)null : Math.Round(values.Average(), 2),
                            p90Minutes = values.Count == 0 ? (double?)null : Math.Round(PercentileDouble(values, 0.90), 2)
                        };
                    });

            var exactOutcomeLabeledCount = dispositionsByEventId.Values.Count(v =>
                string.Equals(v, "true_positive", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(v, "false_positive", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(v, "acceptable_exception", StringComparison.OrdinalIgnoreCase));
            var exactFalsePositiveCount = dispositionsByEventId.Values.Count(v =>
                string.Equals(v, "false_positive", StringComparison.OrdinalIgnoreCase));
            var falsePositiveRatePct = exactOutcomeLabeledCount > 0
                ? Math.Round((exactFalsePositiveCount * 100d) / exactOutcomeLabeledCount, 2)
                : (reviewResolvedCount == 0 ? (double?)null : Math.Round((falsePositiveCount * 100d) / reviewResolvedCount, 2));

            var alerts = events.Count(e =>
                string.Equals(e.Decision, "warn", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(e.Decision, "review_required", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(e.Decision, "soft_hold", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(e.Decision, "hard_hold", StringComparison.OrdinalIgnoreCase));

            return Ok(new
            {
                windowDays = Math.Clamp(days, 1, 365),
                generatedAtUtc = DateTime.UtcNow,
                summary = new
                {
                    eventCount = events.Count,
                    alertCount = alerts,
                    openHolds = holds.Count(h => h.Status == "placed" || h.Status == "under_review" || h.Status == "escalated"),
                    hardHoldsOpen = holds.Count(h => h.HoldType == "hard" && (h.Status == "placed" || h.Status == "under_review" || h.Status == "escalated")),
                    softHoldsOpen = holds.Count(h => h.HoldType == "soft" && (h.Status == "placed" || h.Status == "under_review" || h.Status == "escalated")),
                    meanTimeToReviewMinutes = reviewCompletedDurationsExact.Count == 0
                        ? (reviewCompletedDurationsProxy.Count == 0 ? (double?)null : Math.Round(reviewCompletedDurationsProxy.Average(), 2))
                        : Math.Round(reviewCompletedDurationsExact.Average(), 2),
                    meanHoldReleaseMinutes = holdReleaseDurations.Count == 0 ? (double?)null : Math.Round(holdReleaseDurations.Average(), 2),
                    falsePositiveRatePct = falsePositiveRatePct
                },
                severityCounts = events
                    .GroupBy(e => (e.Severity ?? "unknown").ToLowerInvariant())
                    .ToDictionary(g => g.Key, g => g.Count()),
                decisionCounts = events
                    .GroupBy(e => (e.Decision ?? "unknown").ToLowerInvariant())
                    .ToDictionary(g => g.Key, g => g.Count()),
                holdStatusCounts = holds
                    .GroupBy(h => $"{(h.HoldType ?? "unknown").ToLowerInvariant()}:{(h.Status ?? "unknown").ToLowerInvariant()}")
                    .ToDictionary(g => g.Key, g => g.Count()),
                reviewDispositionCounts = dispositionsByEventId.Values
                    .GroupBy(v => v.ToLowerInvariant())
                    .ToDictionary(g => g.Key, g => g.Count()),
                ruleCounters = ruleCounters
                    .OrderByDescending(kvp => kvp.Value.Total)
                    .ThenBy(kvp => kvp.Key)
                    .Take(25)
                    .ToDictionary(
                        kvp => kvp.Key,
                        kvp =>
                        {
                            var outcomeLabeled = kvp.Value.TruePositive + kvp.Value.FalsePositive + kvp.Value.AcceptableException;
                            var falsePositiveRate = outcomeLabeled == 0
                                ? (double?)null
                                : Math.Round((kvp.Value.FalsePositive * 100d) / outcomeLabeled, 2);
                            var precisionProxyPct = outcomeLabeled == 0
                                ? (double?)null
                                : Math.Round((kvp.Value.TruePositive * 100d) / outcomeLabeled, 2);
                            var unlabeled = Math.Max(0, kvp.Value.Total - outcomeLabeled);
                            var burdenScore = Math.Round(
                                kvp.Value.Total +
                                (kvp.Value.FalsePositive * 1.0) +
                                (kvp.Value.AcceptableException * 0.5) +
                                (unlabeled * 0.25),
                                2);
                            return new
                            {
                                total = kvp.Value.Total,
                                truePositive = kvp.Value.TruePositive,
                                falsePositive = kvp.Value.FalsePositive,
                                acceptableException = kvp.Value.AcceptableException,
                                outcomeLabeled,
                                unlabeled,
                                falsePositiveRatePct = falsePositiveRate,
                                precisionProxyPct,
                                burdenScore
                            };
                        }),
                x3 = new
                {
                    meanTimeToReview = new
                    {
                        exactMinutes = reviewCompletedDurationsExact.Count == 0 ? (double?)null : Math.Round(reviewCompletedDurationsExact.Average(), 2),
                        proxyMinutes = reviewCompletedDurationsProxy.Count == 0 ? (double?)null : Math.Round(reviewCompletedDurationsProxy.Average(), 2),
                        exactSampleCount = reviewCompletedDurationsExact.Count,
                        proxySampleCount = reviewCompletedDurationsProxy.Count
                    },
                    holdReleaseTimeBySeverity = holdReleaseBySeverity
                },
                dataQuality = new
                {
                    falsePositiveRate = exactOutcomeLabeledCount > 0 ? "exact_review_disposition" : "proxy_review_outcome",
                    meanTimeToReview = reviewCompletedDurationsExact.Count > 0 ? "exact_reviewDispositionAtUtc" : "review_queue_updatedAt_proxy",
                    holdPlacementEnforcement = "post_commit_subsequent_operation_enforcement",
                    holdReleaseTimeBySeverity = "hold_release_joined_to_event_severity",
                    ruleCounters = "exact_if_reviewDisposition_present_else_partial"
                }
            });
        }

        [HttpGet("tuning/impact")]
        public async Task<ActionResult> GetThresholdImpactSimulation(
            [FromQuery] int days = 30,
            [FromQuery] decimal? warn = null,
            [FromQuery] decimal? review = null,
            [FromQuery] decimal? softHold = null,
            [FromQuery] decimal? hardHold = null,
            CancellationToken ct = default)
        {
            var windowDays = Math.Clamp(days, 7, 365);
            var since = DateTime.UtcNow.AddDays(-windowDays);
            var policy = await _trustRiskRadar.GetActivePolicyAsync(ct);

            var currentThresholds = new
            {
                warn = policy.WarnThreshold,
                review = policy.ReviewThreshold,
                softHold = policy.SoftHoldThreshold,
                hardHold = policy.HardHoldThreshold
            };
            var candidate = new
            {
                warn = warn ?? policy.WarnThreshold,
                review = review ?? policy.ReviewThreshold,
                softHold = softHold ?? policy.SoftHoldThreshold,
                hardHold = hardHold ?? policy.HardHoldThreshold
            };

            if (!(candidate.warn <= candidate.review && candidate.review <= candidate.softHold && candidate.softHold <= candidate.hardHold))
            {
                return BadRequest(new { message = "Thresholds must satisfy warn <= review <= softHold <= hardHold." });
            }

            var events = await _context.TrustRiskEvents.AsNoTracking()
                .Where(e => e.CreatedAt >= since)
                .Select(e => new { e.Id, e.RiskScore, e.Decision, e.Severity })
                .ToListAsync(ct);

            var beforeCounts = SimulateDecisionCounts(events.Select(e => e.RiskScore), currentThresholds.warn, currentThresholds.review, currentThresholds.softHold, currentThresholds.hardHold);
            var afterCounts = SimulateDecisionCounts(events.Select(e => e.RiskScore), candidate.warn, candidate.review, candidate.softHold, candidate.hardHold);
            var actualCounts = events
                .GroupBy(e => (e.Decision ?? "unknown").ToLowerInvariant())
                .ToDictionary(g => g.Key, g => g.Count());

            var impacted = events.Select(e =>
                new
                {
                    before = SimulateDecision(e.RiskScore, currentThresholds.warn, currentThresholds.review, currentThresholds.softHold, currentThresholds.hardHold),
                    after = SimulateDecision(e.RiskScore, candidate.warn, candidate.review, candidate.softHold, candidate.hardHold)
                })
                .ToList();

            var changedCount = impacted.Count(x => !string.Equals(x.before, x.after, StringComparison.OrdinalIgnoreCase));
            var escalatedCount = impacted.Count(x => CompareDecisionSeverity(x.after) > CompareDecisionSeverity(x.before));
            var relaxedCount = impacted.Count(x => CompareDecisionSeverity(x.after) < CompareDecisionSeverity(x.before));

            return Ok(new
            {
                windowDays,
                generatedAtUtc = DateTime.UtcNow,
                currentThresholds,
                candidateThresholds = candidate,
                totalEvents = events.Count,
                actualDecisionCounts = actualCounts,
                simulatedBefore = beforeCounts,
                simulatedAfter = afterCounts,
                impact = new
                {
                    changedCount,
                    changedPct = events.Count == 0 ? 0d : Math.Round((changedCount * 100d) / events.Count, 2),
                    escalatedCount,
                    relaxedCount
                },
                dataQuality = new
                {
                    mode = "historical_score_replay",
                    note = "Uses recorded riskScore and re-simulates threshold-based decisions."
                }
            });
        }

        [HttpGet("policy/compare-impact")]
        public async Task<ActionResult> ComparePolicyVersionImpact(
            [FromQuery] int days = 60,
            [FromQuery] string? fromPolicyId = null,
            [FromQuery] int? fromVersion = null,
            [FromQuery] string? toPolicyId = null,
            [FromQuery] int? toVersion = null,
            CancellationToken ct = default)
        {
            var windowDays = Math.Clamp(days, 7, 365);
            var fromPolicy = await ResolvePolicyVersionAsync(fromPolicyId, fromVersion, ct, fallbackNewest: false);
            var toPolicyResolved = await ResolvePolicyVersionAsync(toPolicyId, toVersion, ct, fallbackNewest: true);

            if (toPolicyResolved == null)
            {
                return NotFound(new { message = "Target policy version not found." });
            }

            if (fromPolicy == null)
            {
                fromPolicy = await _context.TrustRiskPolicies.AsNoTracking()
                    .Where(p => p.PolicyKey == toPolicyResolved.PolicyKey && p.VersionNumber != toPolicyResolved.VersionNumber)
                    .OrderByDescending(p => p.VersionNumber)
                    .FirstOrDefaultAsync(ct);
            }

            if (fromPolicy == null)
            {
                return BadRequest(new { message = "Baseline (from) policy version could not be resolved." });
            }

            var since = DateTime.UtcNow.AddDays(-windowDays);
            var events = await _context.TrustRiskEvents.AsNoTracking()
                .Where(e => e.CreatedAt >= since)
                .Select(e => new { e.Id, e.RiskScore })
                .ToListAsync(ct);

            var before = SimulateDecisionCounts(events.Select(e => e.RiskScore), fromPolicy.WarnThreshold, fromPolicy.ReviewThreshold, fromPolicy.SoftHoldThreshold, fromPolicy.HardHoldThreshold);
            var after = SimulateDecisionCounts(events.Select(e => e.RiskScore), toPolicyResolved.WarnThreshold, toPolicyResolved.ReviewThreshold, toPolicyResolved.SoftHoldThreshold, toPolicyResolved.HardHoldThreshold);

            var changed = events.Select(e => new
            {
                before = SimulateDecision(e.RiskScore, fromPolicy.WarnThreshold, fromPolicy.ReviewThreshold, fromPolicy.SoftHoldThreshold, fromPolicy.HardHoldThreshold),
                after = SimulateDecision(e.RiskScore, toPolicyResolved.WarnThreshold, toPolicyResolved.ReviewThreshold, toPolicyResolved.SoftHoldThreshold, toPolicyResolved.HardHoldThreshold)
            }).ToList();

            return Ok(new
            {
                windowDays,
                generatedAtUtc = DateTime.UtcNow,
                fromPolicy = new
                {
                    fromPolicy.Id,
                    fromPolicy.PolicyKey,
                    fromPolicy.VersionNumber,
                    thresholds = new { warn = fromPolicy.WarnThreshold, review = fromPolicy.ReviewThreshold, softHold = fromPolicy.SoftHoldThreshold, hardHold = fromPolicy.HardHoldThreshold }
                },
                toPolicy = new
                {
                    toPolicyResolved.Id,
                    toPolicyResolved.PolicyKey,
                    toPolicyResolved.VersionNumber,
                    thresholds = new { warn = toPolicyResolved.WarnThreshold, review = toPolicyResolved.ReviewThreshold, softHold = toPolicyResolved.SoftHoldThreshold, hardHold = toPolicyResolved.HardHoldThreshold }
                },
                totalEvents = events.Count,
                beforeDecisionCounts = before,
                afterDecisionCounts = after,
                impact = new
                {
                    changedCount = changed.Count(x => !string.Equals(x.before, x.after, StringComparison.OrdinalIgnoreCase)),
                    escalatedCount = changed.Count(x => CompareDecisionSeverity(x.after) > CompareDecisionSeverity(x.before)),
                    relaxedCount = changed.Count(x => CompareDecisionSeverity(x.after) < CompareDecisionSeverity(x.before))
                },
                dataQuality = new
                {
                    mode = "historical_score_replay_policy_version_compare"
                }
            });
        }

        [HttpGet("baselines")]
        public async Task<ActionResult> GetBehavioralBaselines([FromQuery] int days = 60, [FromQuery] int top = 8, CancellationToken ct = default)
        {
            return Ok(await _trustRiskRadar.GetBehavioralBaselineSummaryAsync(days, top, ct));
        }

        [HttpGet("tuning")]
        public async Task<ActionResult> GetBehavioralTuning([FromQuery] int days = 45, CancellationToken ct = default)
        {
            return Ok(await _trustRiskRadar.GetBehavioralTuningSummaryAsync(days, ct));
        }

        [HttpGet("evidence-export")]
        public async Task<ActionResult> ExportAuditEvidence(
            [FromQuery] int days = 90,
            [FromQuery] int policyLimit = 25,
            [FromQuery] int eventLimit = 1000,
            [FromQuery] int holdLimit = 1000,
            [FromQuery] int actionLimit = 2500,
            [FromQuery] int auditLimit = 2500,
            [FromQuery] bool includeAuditLogs = true,
            [FromQuery] bool includeEvents = true,
            CancellationToken ct = default)
        {
            if (!UserHasAnyRole("Admin", "SecurityAdmin"))
            {
                return Forbid();
            }

            await _trustRiskRadar.ApplyHoldSlaTransitionsAsync(ct);

            var windowDays = Math.Clamp(days, 1, 3650);
            var sinceUtc = DateTime.UtcNow.AddDays(-windowDays);

            var policies = await _context.TrustRiskPolicies.AsNoTracking()
                .OrderByDescending(p => p.VersionNumber)
                .ThenByDescending(p => p.CreatedAt)
                .Take(Math.Clamp(policyLimit, 1, 250))
                .ToListAsync(ct);

            var holds = await _context.TrustRiskHolds.AsNoTracking()
                .Where(h => h.CreatedAt >= sinceUtc || (h.UpdatedAt >= sinceUtc))
                .OrderByDescending(h => h.UpdatedAt)
                .ThenByDescending(h => h.CreatedAt)
                .Take(Math.Clamp(holdLimit, 1, 5000))
                .ToListAsync(ct);

            List<TrustRiskEvent> events;
            if (includeEvents)
            {
                events = await _context.TrustRiskEvents.AsNoTracking()
                    .Where(e => e.CreatedAt >= sinceUtc || e.UpdatedAt >= sinceUtc)
                    .OrderByDescending(e => e.CreatedAt)
                    .Take(Math.Clamp(eventLimit, 1, 5000))
                    .ToListAsync(ct);
            }
            else
            {
                events = new List<TrustRiskEvent>();
            }

            var eventIds = new HashSet<string>(
                events.Select(e => e.Id)
                    .Concat(holds.Where(h => !string.IsNullOrWhiteSpace(h.TrustRiskEventId)).Select(h => h.TrustRiskEventId)),
                StringComparer.Ordinal);

            var actionsQuery = _context.TrustRiskActions.AsNoTracking()
                .Where(a => a.CreatedAt >= sinceUtc);
            if (eventIds.Count > 0)
            {
                actionsQuery = actionsQuery.Where(a => eventIds.Contains(a.TrustRiskEventId));
            }

            var actions = await actionsQuery
                .OrderByDescending(a => a.CreatedAt)
                .Take(Math.Clamp(actionLimit, 1, 10000))
                .ToListAsync(ct);

            var reviewLinks = eventIds.Count == 0
                ? new List<TrustRiskReviewLink>()
                : await _context.TrustRiskReviewLinks.AsNoTracking()
                    .Where(l => eventIds.Contains(l.TrustRiskEventId))
                    .OrderByDescending(l => l.CreatedAt)
                    .Take(Math.Clamp(eventLimit, 1, 5000))
                    .ToListAsync(ct);

            var releaseActions = actions
                .Where(a => string.Equals(a.ActionType, "release", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(a.ActionType, "release_pending_dual_approval", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var overrideActions = actions
                .Where(a => string.Equals(a.ActionType, "release", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(a.ActionType, "release_pending_dual_approval", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(a.ActionType, "escalate", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var dualApprovalPendingCount = actions.Count(a =>
                string.Equals(a.ActionType, "release_pending_dual_approval", StringComparison.OrdinalIgnoreCase));

            List<object> auditLogs = new();
            if (includeAuditLogs)
            {
                var auditRows = await _context.AuditLogs.AsNoTracking()
                    .Where(a => a.CreatedAt >= sinceUtc)
                    .Where(a => a.Action.StartsWith("trust_risk."))
                    .OrderByDescending(a => a.CreatedAt)
                    .ThenByDescending(a => a.Sequence)
                    .Take(Math.Clamp(auditLimit, 1, 10000))
                    .ToListAsync(ct);

                auditLogs = auditRows.Select(a =>
                {
                    var tags = ParseAuditTraceTags(a.Details);
                    return (object)new
                    {
                        a.Id,
                        a.Sequence,
                        a.Action,
                        a.Entity,
                        a.EntityId,
                        a.UserId,
                        a.Role,
                        a.TenantId,
                        a.Hash,
                        a.PreviousHash,
                        a.HashAlgorithm,
                        a.CreatedAt,
                        details = StripAuditTraceTags(a.Details),
                        traceTags = tags.Count == 0 ? null : tags
                    };
                }).ToList();
            }

            var summary = new
            {
                generatedAtUtc = DateTime.UtcNow,
                windowDays,
                policyVersions = policies.Count,
                includedEvents = events.Count,
                includedHolds = holds.Count,
                includedActions = actions.Count,
                includedReviewLinks = reviewLinks.Count,
                releaseActions = releaseActions.Count,
                overrideActions = overrideActions.Count,
                dualApprovalPendingReleases = dualApprovalPendingCount,
                openHolds = holds.Count(h => string.Equals(h.Status, "placed", StringComparison.OrdinalIgnoreCase) ||
                                            string.Equals(h.Status, "under_review", StringComparison.OrdinalIgnoreCase) ||
                                            string.Equals(h.Status, "escalated", StringComparison.OrdinalIgnoreCase)),
                releasedHolds = holds.Count(h => string.Equals(h.Status, "released", StringComparison.OrdinalIgnoreCase)),
                auditLogRows = auditLogs.Count
            };

            await _auditLogger.LogAsync(
                HttpContext,
                "trust_risk.evidence.export",
                "TrustRiskEvidenceExport",
                null,
                $"Days={windowDays}, Policies={policies.Count}, Events={events.Count}, Holds={holds.Count}, Actions={actions.Count}, Audit={auditLogs.Count}");

            return Ok(new
            {
                exportMetadata = new
                {
                    generatedAtUtc = DateTime.UtcNow,
                    window = new { days = windowDays, fromUtc = sinceUtc },
                    scope = new
                    {
                        includeAuditLogs,
                        includeEvents,
                        policyLimit = Math.Clamp(policyLimit, 1, 250),
                        eventLimit = Math.Clamp(eventLimit, 1, 5000),
                        holdLimit = Math.Clamp(holdLimit, 1, 5000),
                        actionLimit = Math.Clamp(actionLimit, 1, 10000),
                        auditLimit = Math.Clamp(auditLimit, 1, 10000)
                    }
                },
                summary,
                policyVersions = policies,
                holds,
                releases = releaseActions,
                overrides = overrideActions,
                actions,
                reviewLinks,
                events,
                auditLogs,
                dataQuality = new
                {
                    auditLogs = includeAuditLogs ? "hash_chain_export_with_trace_tags" : "excluded",
                    events = includeEvents ? "included" : "excluded",
                    notes = new[]
                    {
                        "Export is tenant-scoped via tenant query filters.",
                        "Hold lifecycle statuses may be SLA-updated at export time.",
                        "Trace tags are parsed from audit details suffix when present."
                    }
                }
            });
        }

        [HttpGet("holds")]
        public async Task<ActionResult<IEnumerable<TrustRiskHold>>> GetHolds([FromQuery] TrustRiskHoldQueryDto query, CancellationToken ct)
        {
            await _trustRiskRadar.ApplyHoldSlaTransitionsAsync(ct);
            var q = _context.TrustRiskHolds.AsQueryable();
            if (!string.IsNullOrWhiteSpace(query.Status)) q = q.Where(h => h.Status == query.Status);
            if (!string.IsNullOrWhiteSpace(query.HoldType)) q = q.Where(h => h.HoldType == query.HoldType);
            if (!string.IsNullOrWhiteSpace(query.TargetType)) q = q.Where(h => h.TargetType == query.TargetType);
            if (!string.IsNullOrWhiteSpace(query.TargetId)) q = q.Where(h => h.TargetId == query.TargetId);
            if (!string.IsNullOrWhiteSpace(query.TrustRiskEventId)) q = q.Where(h => h.TrustRiskEventId == query.TrustRiskEventId);

            if (TryParseUtc(query.FromUtc, out var fromUtc))
            {
                q = q.Where(h => h.CreatedAt >= fromUtc);
            }
            if (TryParseUtc(query.ToUtc, out var toUtc))
            {
                q = q.Where(h => h.CreatedAt <= toUtc);
            }

            var holds = await q
                .OrderByDescending(h => h.PlacedAt)
                .Take(Math.Clamp(query.Limit ?? 100, 1, 500))
                .ToListAsync(ct);

            return Ok(holds);
        }

        [HttpGet("holds/{id}")]
        public async Task<ActionResult> GetHoldDetail(string id, CancellationToken ct)
        {
            await _trustRiskRadar.ApplyHoldSlaTransitionsAsync(ct);
            var hold = await _context.TrustRiskHolds.FirstOrDefaultAsync(h => h.Id == id, ct);
            if (hold == null)
            {
                return NotFound();
            }

            var riskEvent = await _context.TrustRiskEvents.FirstOrDefaultAsync(e => e.Id == hold.TrustRiskEventId, ct);
            var actions = await _context.TrustRiskActions
                .Where(a => a.TrustRiskEventId == hold.TrustRiskEventId)
                .OrderByDescending(a => a.CreatedAt)
                .ToListAsync(ct);

            return Ok(new { hold, riskEvent, actions });
        }

        [HttpPost("holds/{id}/under-review")]
        public async Task<ActionResult<TrustRiskHold>> MarkHoldUnderReview(string id, [FromBody] TrustRiskHoldActionDto dto, CancellationToken ct)
        {
            try
            {
                var hold = await _trustRiskRadar.MarkHoldUnderReviewAsync(id, GetUserId(), dto?.Reason, ct);
                if (hold == null) return NotFound();

                await _auditLogger.LogAsync(HttpContext, "trust_risk.hold.under_review", nameof(TrustRiskHold), hold.Id, $"Reason={dto?.Reason}");
                return Ok(hold);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("holds/{id}/release")]
        public async Task<ActionResult<TrustRiskHold>> ReleaseHold(string id, [FromBody] TrustRiskHoldActionDto dto, CancellationToken ct)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.Reason))
            {
                return BadRequest(new { message = "Reason is required." });
            }

            try
            {
                var hold = await _trustRiskRadar.ReleaseHoldAsync(id, GetUserId(), GetUserRole(), dto.Reason, dto.ApproverReason, ct);
                if (hold == null) return NotFound();

                await _auditLogger.LogAsync(HttpContext, "trust_risk.hold.release", nameof(TrustRiskHold), hold.Id, $"Reason={dto.Reason}");
                return Ok(hold);
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("holds/{id}/escalate")]
        public async Task<ActionResult<TrustRiskHold>> EscalateHold(string id, [FromBody] TrustRiskHoldActionDto dto, CancellationToken ct)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.Reason))
            {
                return BadRequest(new { message = "Reason is required." });
            }

            try
            {
                var hold = await _trustRiskRadar.EscalateHoldAsync(id, GetUserId(), GetUserRole(), dto.Reason, dto.ApproverReason, ct);
                if (hold == null) return NotFound();

                await _auditLogger.LogAsync(HttpContext, "trust_risk.hold.escalate", nameof(TrustRiskHold), hold.Id, $"Reason={dto.Reason}");
                return Ok(hold);
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        private string? GetUserId() =>
            User.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
            User.FindFirst("sub")?.Value;

        private string? GetUserRole() =>
            User.FindFirst(ClaimTypes.Role)?.Value ??
            User.FindFirst("role")?.Value;

        private bool UserHasAnyRole(params string[] roles)
        {
            var current = GetUserRole();
            return !string.IsNullOrWhiteSpace(current) &&
                   roles.Any(r => string.Equals(r, current, StringComparison.OrdinalIgnoreCase));
        }

        private static IQueryable<TrustRiskEvent> ApplyEventFilters(IQueryable<TrustRiskEvent> q, TrustRiskEventQueryDto query)
        {
            if (!string.IsNullOrWhiteSpace(query.Status)) q = q.Where(e => e.Status == query.Status);
            if (!string.IsNullOrWhiteSpace(query.Decision)) q = q.Where(e => e.Decision == query.Decision);
            if (!string.IsNullOrWhiteSpace(query.Severity)) q = q.Where(e => e.Severity == query.Severity);
            if (!string.IsNullOrWhiteSpace(query.SourceType)) q = q.Where(e => e.SourceType == query.SourceType);
            if (!string.IsNullOrWhiteSpace(query.InvoiceId)) q = q.Where(e => e.InvoiceId == query.InvoiceId);
            if (!string.IsNullOrWhiteSpace(query.MatterId)) q = q.Where(e => e.MatterId == query.MatterId);
            if (!string.IsNullOrWhiteSpace(query.ClientId)) q = q.Where(e => e.ClientId == query.ClientId);

            if (TryParseUtc(query.FromUtc, out var fromUtc))
            {
                q = q.Where(e => e.CreatedAt >= fromUtc);
            }
            if (TryParseUtc(query.ToUtc, out var toUtc))
            {
                q = q.Where(e => e.CreatedAt <= toUtc);
            }

            return q;
        }

        private static Dictionary<string, object?>? BuildAdditionalPolicyMetadata(TrustRiskPolicyUpsertDto dto)
        {
            var metadata = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            if (dto.BehavioralSignalsEnabled.HasValue) metadata["behavioralSignalsEnabled"] = dto.BehavioralSignalsEnabled.Value;
            if (dto.BehavioralShadowMode.HasValue) metadata["behavioralShadowMode"] = dto.BehavioralShadowMode.Value;
            if (dto.BehavioralLookbackDays.HasValue) metadata["behavioralLookbackDays"] = dto.BehavioralLookbackDays.Value;
            if (dto.BehavioralMinSamples.HasValue) metadata["behavioralMinSamples"] = dto.BehavioralMinSamples.Value;
            if (dto.BehavioralAmountRatioThreshold.HasValue) metadata["behavioralAmountRatioThreshold"] = dto.BehavioralAmountRatioThreshold.Value;
            if (dto.BehavioralTimePatternDeltaThreshold.HasValue) metadata["behavioralTimePatternDeltaThreshold"] = dto.BehavioralTimePatternDeltaThreshold.Value;
            if (dto.BehavioralReversalRateDeltaThreshold.HasValue) metadata["behavioralReversalRateDeltaThreshold"] = dto.BehavioralReversalRateDeltaThreshold.Value;
            if (dto.BehavioralContributionCap.HasValue) metadata["behavioralContributionCap"] = dto.BehavioralContributionCap.Value;
            if (dto.PreflightStrictModeEnabled.HasValue) metadata["preflightStrictModeEnabled"] = dto.PreflightStrictModeEnabled.Value;
            if (!string.IsNullOrWhiteSpace(dto.PreflightStrictRolloutMode)) metadata["preflightStrictRolloutMode"] = dto.PreflightStrictRolloutMode;
            if (dto.PreflightHighConfidenceOnly.HasValue) metadata["preflightHighConfidenceOnly"] = dto.PreflightHighConfidenceOnly.Value;
            if (!string.IsNullOrWhiteSpace(dto.PreflightMinSeverity)) metadata["preflightMinSeverity"] = dto.PreflightMinSeverity;
            if (dto.PreflightRecentEventWindowMinutes.HasValue) metadata["preflightRecentEventWindowMinutes"] = dto.PreflightRecentEventWindowMinutes.Value;
            if (dto.PreflightDuplicateSuppressionEnabled.HasValue) metadata["preflightDuplicateSuppressionEnabled"] = dto.PreflightDuplicateSuppressionEnabled.Value;
            if (dto.OperationFailModes != null && dto.OperationFailModes.Count > 0) metadata["operationFailModes"] = dto.OperationFailModes;
            return metadata.Count == 0 ? null : metadata;
        }

        private static IReadOnlyCollection<TrustRiskRadarService.TrustAccountPolicyOverrideRequest>? MapTrustAccountOverrides(IEnumerable<TrustRiskTrustAccountOverrideDto>? rows)
        {
            if (rows == null)
            {
                return null;
            }

            var mapped = rows
                .Where(r => r != null && !string.IsNullOrWhiteSpace(r.TrustAccountId))
                .Select(r => new TrustRiskRadarService.TrustAccountPolicyOverrideRequest
                {
                    TrustAccountId = r.TrustAccountId!.Trim(),
                    WarnThreshold = r.WarnThreshold,
                    ReviewThreshold = r.ReviewThreshold,
                    SoftHoldThreshold = r.SoftHoldThreshold,
                    HardHoldThreshold = r.HardHoldThreshold,
                    ActionMap = r.ActionMap,
                    OverrideRoles = r.OverrideRoles,
                    ReleaseRoles = r.ReleaseRoles,
                    CriticalDualApprovalSecondaryRoles = r.CriticalDualApprovalSecondaryRoles,
                    CriticalDualApprovalEnabled = r.CriticalDualApprovalEnabled,
                    HoldEscalationSlaMinutes = r.HoldEscalationSlaMinutes,
                    SoftHoldExpiryHours = r.SoftHoldExpiryHours,
                    HardHoldExpiryHours = r.HardHoldExpiryHours,
                    OpsAlertsEnabled = r.OpsAlertsEnabled,
                    OpsAlertChannels = r.OpsAlertChannels,
                    Note = r.Note
                })
                .ToList();

            return mapped.Count == 0 ? null : mapped;
        }

        private static string? StripAuditTraceTags(string? details)
        {
            if (string.IsNullOrWhiteSpace(details))
            {
                return details;
            }

            var trimmed = details.Trim();
            var suffixStart = trimmed.LastIndexOf('[');
            if (suffixStart <= 0 || !trimmed.EndsWith("]", StringComparison.Ordinal))
            {
                return trimmed;
            }

            var suffix = trimmed[suffixStart..];
            if (!suffix.Contains("traceId=", StringComparison.OrdinalIgnoreCase) &&
                !suffix.Contains("corr=", StringComparison.OrdinalIgnoreCase) &&
                !suffix.Contains("integrationRunId=", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed;
            }

            return trimmed[..suffixStart].TrimEnd();
        }

        private static Dictionary<string, string> ParseAuditTraceTags(string? details)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(details))
            {
                return result;
            }

            var trimmed = details.Trim();
            var suffixStart = trimmed.LastIndexOf('[');
            if (suffixStart < 0 || !trimmed.EndsWith("]", StringComparison.Ordinal))
            {
                return result;
            }

            var suffix = trimmed[(suffixStart + 1)..^1];
            if (!suffix.Contains("traceId=", StringComparison.OrdinalIgnoreCase) &&
                !suffix.Contains("corr=", StringComparison.OrdinalIgnoreCase) &&
                !suffix.Contains("integrationRunId=", StringComparison.OrdinalIgnoreCase))
            {
                return result;
            }

            foreach (var part in suffix.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var idx = part.IndexOf('=');
                if (idx <= 0 || idx >= part.Length - 1)
                {
                    continue;
                }

                var key = part[..idx].Trim();
                var value = part[(idx + 1)..].Trim();
                if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                result[key] = value;
            }

            return result;
        }

        private static bool TryParseUtc(string? raw, out DateTime utc)
        {
            utc = default;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            if (!DateTime.TryParse(
                    raw,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out utc))
            {
                return false;
            }

            return true;
        }

        private async Task<TrustRiskPolicy?> ResolvePolicyVersionAsync(string? policyId, int? version, CancellationToken ct, bool fallbackNewest)
        {
            IQueryable<TrustRiskPolicy> q = _context.TrustRiskPolicies.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(policyId))
            {
                q = q.Where(p => p.Id == policyId);
            }

            if (version.HasValue)
            {
                q = q.Where(p => p.VersionNumber == version.Value);
            }

            var match = await q
                .OrderByDescending(p => p.IsActive)
                .ThenByDescending(p => p.VersionNumber)
                .ThenByDescending(p => p.CreatedAt)
                .FirstOrDefaultAsync(ct);

            if (match != null || !fallbackNewest)
            {
                return match;
            }

            return await _context.TrustRiskPolicies.AsNoTracking()
                .OrderByDescending(p => p.IsActive)
                .ThenByDescending(p => p.VersionNumber)
                .ThenByDescending(p => p.CreatedAt)
                .FirstOrDefaultAsync(ct);
        }

        private static Dictionary<string, int> SimulateDecisionCounts(IEnumerable<decimal> scores, decimal warn, decimal review, decimal softHold, decimal hardHold)
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var score in scores)
            {
                var decision = SimulateDecision(score, warn, review, softHold, hardHold);
                counts[decision] = counts.TryGetValue(decision, out var c) ? c + 1 : 1;
            }

            return counts;
        }

        private static string SimulateDecision(decimal score, decimal warn, decimal review, decimal softHold, decimal hardHold)
        {
            if (score >= hardHold) return "hard_hold";
            if (score >= softHold) return "soft_hold";
            if (score >= review) return "review_required";
            if (score >= warn) return "warn";
            return "record";
        }

        private static int CompareDecisionSeverity(string? decision)
        {
            var d = (decision ?? string.Empty).Trim().ToLowerInvariant();
            return d switch
            {
                "hard_hold" => 4,
                "soft_hold" => 3,
                "review_required" => 2,
                "warn" => 1,
                _ => 0
            };
        }

        private static string? TryReadJsonString(string? json, string propertyName)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) return null;
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (!string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase)) continue;
                    return prop.Value.ValueKind == System.Text.Json.JsonValueKind.String ? prop.Value.GetString() : prop.Value.GetRawText();
                }
            }
            catch
            {
                return null;
            }
            return null;
        }

        private static bool TryReadJsonUtc(string? json, string propertyName, out DateTime utc)
        {
            utc = default;
            var raw = TryReadJsonString(json, propertyName);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            return DateTime.TryParse(
                raw,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out utc);
        }

        private static IEnumerable<string> ExtractReasonCodes(string? riskReasonsJson)
        {
            if (string.IsNullOrWhiteSpace(riskReasonsJson))
            {
                yield break;
            }

            System.Text.Json.JsonDocument? doc = null;
            try
            {
                doc = System.Text.Json.JsonDocument.Parse(riskReasonsJson);
                if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array)
                {
                    yield break;
                }

                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    if (item.ValueKind != System.Text.Json.JsonValueKind.Object) continue;
                    if (item.TryGetProperty("code", out var codeProp) && codeProp.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        var code = codeProp.GetString();
                        if (!string.IsNullOrWhiteSpace(code))
                        {
                            yield return code.Trim();
                        }
                    }
                }
            }
            finally
            {
                doc?.Dispose();
            }
        }

        private static double PercentileDouble(IReadOnlyList<double> sortedValues, double percentile)
        {
            if (sortedValues == null || sortedValues.Count == 0)
            {
                return 0d;
            }

            if (sortedValues.Count == 1)
            {
                return sortedValues[0];
            }

            var p = Math.Clamp(percentile, 0d, 1d);
            var position = (sortedValues.Count - 1) * p;
            var lower = (int)Math.Floor(position);
            var upper = (int)Math.Ceiling(position);
            if (lower == upper)
            {
                return sortedValues[lower];
            }

            var fraction = position - lower;
            return sortedValues[lower] + ((sortedValues[upper] - sortedValues[lower]) * fraction);
        }

        public sealed class TrustRiskPolicyUpsertDto
        {
            public string? PolicyKey { get; set; }
            public string? TemplateKey { get; set; }
            public string? PolicyTemplate { get; set; }
            public string? Name { get; set; }
            public string? Description { get; set; }
            public decimal? WarnThreshold { get; set; }
            public decimal? ReviewThreshold { get; set; }
            public decimal? SoftHoldThreshold { get; set; }
            public decimal? HardHoldThreshold { get; set; }
            public string? FailMode { get; set; }
            public List<string>? EnabledRules { get; set; }
            public Dictionary<string, decimal>? RuleWeights { get; set; }
            public Dictionary<string, string>? ActionMap { get; set; }
            public List<string>? OverrideRoles { get; set; }
            public List<string>? ReleaseRoles { get; set; }
            public List<string>? CriticalDualApprovalSecondaryRoles { get; set; }
            public bool? CriticalDualApprovalEnabled { get; set; }
            public int? HoldEscalationSlaMinutes { get; set; }
            public int? SoftHoldExpiryHours { get; set; }
            public int? HardHoldExpiryHours { get; set; }
            public bool? RequireCriticalThresholdChangeReview { get; set; }
            public string? CriticalThresholdChangeReason { get; set; }
            public bool? OpsAlertsEnabled { get; set; }
            public List<string>? OpsAlertChannels { get; set; }
            public List<TrustRiskTrustAccountOverrideDto>? TrustAccountOverrides { get; set; }
            public bool? BehavioralSignalsEnabled { get; set; }
            public bool? BehavioralShadowMode { get; set; }
            public int? BehavioralLookbackDays { get; set; }
            public int? BehavioralMinSamples { get; set; }
            public decimal? BehavioralAmountRatioThreshold { get; set; }
            public decimal? BehavioralTimePatternDeltaThreshold { get; set; }
            public decimal? BehavioralReversalRateDeltaThreshold { get; set; }
            public decimal? BehavioralContributionCap { get; set; }
            public bool? PreflightStrictModeEnabled { get; set; }
            public string? PreflightStrictRolloutMode { get; set; }
            public bool? PreflightHighConfidenceOnly { get; set; }
            public string? PreflightMinSeverity { get; set; }
            public int? PreflightRecentEventWindowMinutes { get; set; }
            public bool? PreflightDuplicateSuppressionEnabled { get; set; }
            public Dictionary<string, string>? OperationFailModes { get; set; }
        }

        public sealed class TrustRiskTrustAccountOverrideDto
        {
            public string? TrustAccountId { get; set; }
            public decimal? WarnThreshold { get; set; }
            public decimal? ReviewThreshold { get; set; }
            public decimal? SoftHoldThreshold { get; set; }
            public decimal? HardHoldThreshold { get; set; }
            public Dictionary<string, string>? ActionMap { get; set; }
            public List<string>? OverrideRoles { get; set; }
            public List<string>? ReleaseRoles { get; set; }
            public List<string>? CriticalDualApprovalSecondaryRoles { get; set; }
            public bool? CriticalDualApprovalEnabled { get; set; }
            public int? HoldEscalationSlaMinutes { get; set; }
            public int? SoftHoldExpiryHours { get; set; }
            public int? HardHoldExpiryHours { get; set; }
            public bool? OpsAlertsEnabled { get; set; }
            public List<string>? OpsAlertChannels { get; set; }
            public string? Note { get; set; }
        }

        public sealed class TrustRiskEventQueryDto
        {
            public string? Status { get; set; }
            public string? Decision { get; set; }
            public string? Severity { get; set; }
            public string? SourceType { get; set; }
            public string? InvoiceId { get; set; }
            public string? MatterId { get; set; }
            public string? ClientId { get; set; }
            public string? FromUtc { get; set; }
            public string? ToUtc { get; set; }
            public int? Limit { get; set; }
        }

        public sealed class TrustRiskHoldQueryDto
        {
            public string? Status { get; set; }
            public string? HoldType { get; set; }
            public string? TargetType { get; set; }
            public string? TargetId { get; set; }
            public string? TrustRiskEventId { get; set; }
            public string? FromUtc { get; set; }
            public string? ToUtc { get; set; }
            public int? Limit { get; set; }
        }

        public sealed class TrustRiskHoldActionDto
        {
            public string? Reason { get; set; }
            public string? ApproverReason { get; set; }
        }

        public sealed class TrustRiskRescoreDto
        {
            public string? Reason { get; set; }
        }

        public sealed class TrustRiskBatchRescoreDto
        {
            public string? Status { get; set; }
            public string? Decision { get; set; }
            public string? Severity { get; set; }
            public string? SourceType { get; set; }
            public string? InvoiceId { get; set; }
            public string? MatterId { get; set; }
            public string? ClientId { get; set; }
            public string? FromUtc { get; set; }
            public string? ToUtc { get; set; }
            public int? Limit { get; set; }
            public List<string>? EventIds { get; set; }
            public string? Reason { get; set; }
        }

        public sealed class TrustRiskEventAcknowledgeDto
        {
            public string? Note { get; set; }
        }

        public sealed class TrustRiskEventAssignDto
        {
            public string? AssigneeUserId { get; set; }
            public string? Note { get; set; }
        }

        public sealed class TrustRiskReviewDispositionDto
        {
            public string? Disposition { get; set; }
            public string? Reason { get; set; }
            public string? ApproverReason { get; set; }
        }

        private sealed class RuleCounter
        {
            public int Total { get; set; }
            public int TruePositive { get; set; }
            public int FalsePositive { get; set; }
            public int AcceptableException { get; set; }
        }

        private async Task<TrustRiskEvent?> RescoreLedgerAsync(TrustRiskEvent riskEvent, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(riskEvent.BillingLedgerEntryId))
            {
                return null;
            }

            var entry = await _context.BillingLedgerEntries.FirstOrDefaultAsync(e => e.Id == riskEvent.BillingLedgerEntryId, ct);
            if (entry == null) return null;
            return await _trustRiskRadar.RecordLedgerEntryRiskAsync(entry, "manual_rescore", ct);
        }

        private async Task<TrustRiskEvent?> RescoreAllocationAsync(TrustRiskEvent riskEvent, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(riskEvent.BillingPaymentAllocationId))
            {
                return null;
            }

            var allocation = await _context.BillingPaymentAllocations.FirstOrDefaultAsync(a => a.Id == riskEvent.BillingPaymentAllocationId, ct);
            if (allocation == null) return null;
            return await _trustRiskRadar.RecordPaymentAllocationRiskAsync(allocation, "manual_rescore", ct);
        }

        private async Task<TrustRiskEvent?> RescoreTrustTransactionAsync(TrustRiskEvent riskEvent, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(riskEvent.TrustTransactionId))
            {
                return null;
            }

            var transaction = await _context.TrustTransactions.FirstOrDefaultAsync(t => t.Id == riskEvent.TrustTransactionId, ct);
            if (transaction == null) return null;
            return await _trustRiskRadar.RecordTrustTransactionRiskAsync(transaction, "manual_rescore", ct);
        }
    }
}
