using System.Globalization;
using System.Text.Json;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace JurisFlow.Server.Services
{
    public partial class ClientTransparencyService
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        private static readonly string[] DefaultSourceWhitelist =
        {
            "docket", "efiling", "task", "invoice", "payment", "planner"
        };

        private readonly JurisFlowDbContext _context;
        private readonly TenantContext _tenantContext;
        private readonly OutcomeFeePlannerService _outcomeFeePlannerService;

        public ClientTransparencyService(JurisFlowDbContext context, TenantContext tenantContext, OutcomeFeePlannerService outcomeFeePlannerService)
        {
            _context = context;
            _tenantContext = tenantContext;
            _outcomeFeePlannerService = outcomeFeePlannerService;
        }

        public async Task<ClientTransparencySnapshotDetailResult?> GetCurrentSnapshotForMatterAsync(string matterId, CancellationToken ct)
        {
            var normalizedMatterId = NormalizeId(matterId);
            if (normalizedMatterId == null) return null;

            EnsureTenant();

            var snapshot = await _context.ClientTransparencySnapshots
                .Where(s => s.MatterId == normalizedMatterId && s.IsCurrent)
                .OrderByDescending(s => s.VersionNumber)
                .ThenByDescending(s => s.GeneratedAt)
                .FirstOrDefaultAsync(ct);

            return snapshot == null ? null : await LoadSnapshotDetailAsync(snapshot, ct);
        }

        public async Task<ClientTransparencySnapshotDetailResult?> GetSnapshotByIdAsync(string snapshotId, CancellationToken ct)
        {
            var normalizedSnapshotId = NormalizeId(snapshotId);
            if (normalizedSnapshotId == null) return null;

            EnsureTenant();

            var snapshot = await _context.ClientTransparencySnapshots
                .FirstOrDefaultAsync(s => s.Id == normalizedSnapshotId, ct);
            return snapshot == null ? null : await LoadSnapshotDetailAsync(snapshot, ct);
        }

        public async Task<ClientTransparencyHistoryResult> GetUpdateHistoryForMatterAsync(string matterId, int limit, CancellationToken ct)
        {
            var normalizedMatterId = NormalizeId(matterId) ?? throw new InvalidOperationException("MatterId is required.");
            EnsureTenant();

            var boundedLimit = Math.Clamp(limit <= 0 ? 50 : limit, 1, 200);

            var events = await _context.ClientTransparencyUpdateEvents
                .Where(e => e.MatterId == normalizedMatterId)
                .OrderByDescending(e => e.CreatedAt)
                .Take(boundedLimit)
                .ToListAsync(ct);

            var snapshots = await _context.ClientTransparencySnapshots
                .Where(s => s.MatterId == normalizedMatterId)
                .OrderByDescending(s => s.VersionNumber)
                .ThenByDescending(s => s.GeneratedAt)
                .Take(Math.Min(boundedLimit, 100))
                .ToListAsync(ct);

            return new ClientTransparencyHistoryResult
            {
                MatterId = normalizedMatterId,
                Events = events,
                Snapshots = snapshots
            };
        }

        public async Task<ClientTransparencySnapshotDetailResult> RegenerateSnapshotAsync(string matterId, ClientTransparencyRegenerateRequest? request, string userId, CancellationToken ct)
        {
            var normalizedMatterId = NormalizeId(matterId) ?? throw new InvalidOperationException("MatterId is required.");
            EnsureTenant();

            var now = DateTime.UtcNow;
            var actor = string.IsNullOrWhiteSpace(userId) ? "system" : userId.Trim();
            request ??= new ClientTransparencyRegenerateRequest();

            var matter = await _context.Matters.FirstOrDefaultAsync(m => m.Id == normalizedMatterId, ct)
                ?? throw new InvalidOperationException("Matter not found.");
            var client = await _context.Clients.FirstOrDefaultAsync(c => c.Id == matter.ClientId, ct);

            var profile = await GetOrCreateEffectiveProfileAsync(matter.Id, now, ct);
            var sourceWhitelist = ParseSourceWhitelist(profile.SourceWhitelistJson);

            var previousCurrent = await _context.ClientTransparencySnapshots
                .Where(s => s.MatterId == matter.Id && s.IsCurrent)
                .OrderByDescending(s => s.VersionNumber)
                .ThenByDescending(s => s.GeneratedAt)
                .FirstOrDefaultAsync(ct);

            var nextVersion = ((await _context.ClientTransparencySnapshots
                .Where(s => s.MatterId == matter.Id)
                .MaxAsync(s => (int?)s.VersionNumber, ct)) ?? 0) + 1;

            var correlationId = !string.IsNullOrWhiteSpace(request.CorrelationId)
                ? request.CorrelationId.Trim()
                : $"ct_{Guid.NewGuid():N}";

            var sourceData = await LoadSourceDataAsync(matter, client, sourceWhitelist, ct);
            var built = BuildSkeletonSnapshot(matter, client, sourceData, request, profile, now, correlationId, nextVersion, previousCurrent, actor);

            if (previousCurrent != null)
            {
                previousCurrent.IsCurrent = false;
                previousCurrent.Status = "superseded";
                previousCurrent.UpdatedAt = now;
            }

            _context.ClientTransparencySnapshots.Add(built.Snapshot);
            if (built.TimelineItems.Count > 0) _context.ClientTransparencyTimelineItems.AddRange(built.TimelineItems);
            if (built.DelayReasons.Count > 0) _context.ClientTransparencyDelayReasons.AddRange(built.DelayReasons);
            if (built.NextStep != null) _context.ClientTransparencyNextSteps.Add(built.NextStep);
            if (built.CostImpact != null) _context.ClientTransparencyCostImpacts.Add(built.CostImpact);
            _context.ClientTransparencyUpdateEvents.Add(built.UpdateEvent);

            await _context.SaveChangesAsync(ct);

            return await LoadSnapshotDetailAsync(built.Snapshot, ct)
                ?? throw new InvalidOperationException("Failed to load generated transparency snapshot.");
        }

        private async Task<ClientTransparencySnapshotDetailResult?> LoadSnapshotDetailAsync(ClientTransparencySnapshot snapshot, CancellationToken ct)
        {
            var profile = !string.IsNullOrWhiteSpace(snapshot.ProfileId)
                ? await _context.ClientTransparencyProfiles.FirstOrDefaultAsync(p => p.Id == snapshot.ProfileId, ct)
                : null;

            profile ??= await _context.ClientTransparencyProfiles
                .Where(p => p.Scope == "matter_override" && p.MatterId == snapshot.MatterId && p.Status == "active")
                .OrderByDescending(p => p.UpdatedAt)
                .FirstOrDefaultAsync(ct);

            profile ??= await _context.ClientTransparencyProfiles
                .Where(p => p.Scope == "tenant_default" && p.Status == "active")
                .OrderByDescending(p => p.UpdatedAt)
                .FirstOrDefaultAsync(ct);

            var timeline = await _context.ClientTransparencyTimelineItems
                .Where(t => t.SnapshotId == snapshot.Id)
                .OrderBy(t => t.OrderIndex)
                .ThenBy(t => t.CreatedAt)
                .ToListAsync(ct);

            var delays = await _context.ClientTransparencyDelayReasons
                .Where(d => d.SnapshotId == snapshot.Id)
                .OrderByDescending(d => d.IsActive)
                .ThenBy(d => d.CreatedAt)
                .ToListAsync(ct);

            var nextStep = await _context.ClientTransparencyNextSteps
                .Where(n => n.SnapshotId == snapshot.Id)
                .OrderByDescending(n => n.CreatedAt)
                .FirstOrDefaultAsync(ct);

            var costImpact = await _context.ClientTransparencyCostImpacts
                .Where(c => c.SnapshotId == snapshot.Id)
                .OrderByDescending(c => c.CreatedAt)
                .FirstOrDefaultAsync(ct);

            return new ClientTransparencySnapshotDetailResult
            {
                Profile = profile,
                Snapshot = snapshot,
                TimelineItems = timeline,
                DelayReasons = delays,
                NextStep = nextStep,
                CostImpact = costImpact,
                RiskFlags = ParseRiskFlagsFromSnapshotMetadata(snapshot.MetadataJson)
            };
        }

        private async Task<ClientTransparencyProfile> GetOrCreateEffectiveProfileAsync(string matterId, DateTime now, CancellationToken ct)
        {
            var overrideProfile = await _context.ClientTransparencyProfiles
                .Where(p => p.Scope == "matter_override" && p.MatterId == matterId && p.Status == "active")
                .OrderByDescending(p => p.UpdatedAt)
                .FirstOrDefaultAsync(ct);
            if (overrideProfile != null) return overrideProfile;

            var tenantProfile = await _context.ClientTransparencyProfiles
                .Where(p => p.Scope == "tenant_default" && p.Status == "active")
                .OrderByDescending(p => p.UpdatedAt)
                .FirstOrDefaultAsync(ct);
            if (tenantProfile != null) return tenantProfile;

            var profile = new ClientTransparencyProfile
            {
                Scope = "tenant_default",
                ProfileKey = "default",
                Status = "active",
                PublishPolicy = "warn_only",
                VisibilityRulesJson = SerializeJson(new
                {
                    showTimeline = true,
                    showDelayReason = true,
                    showNextStep = true,
                    showCostImpact = true,
                    showConfidence = true,
                    showWhatChanged = true
                }),
                RedactionRulesJson = SerializeJson(new
                {
                    excludeInternalNotes = true,
                    excludePrivilegeTagged = true,
                    redactSpecificCourtInternalIdentifiers = true
                }),
                SourceWhitelistJson = SerializeJson(DefaultSourceWhitelist),
                DelayTaxonomyJson = SerializeJson(new[]
                {
                    new { code = "court_processing", label = "Court processing time" },
                    new { code = "filing_correction", label = "Filing correction needed" },
                    new { code = "pending_client_materials", label = "Waiting on client materials" },
                    new { code = "internal_task_backlog", label = "Firm task sequencing" },
                    new { code = "payment_timing", label = "Payment timing" }
                }),
                MetadataJson = SerializeJson(new { phase = 0, profileVersion = "client_transparency_default_v1" }),
                CreatedAt = now,
                UpdatedAt = now
            };

            _context.ClientTransparencyProfiles.Add(profile);
            await _context.SaveChangesAsync(ct);
            return profile;
        }

        private async Task<ClientTransparencySourceData> LoadSourceDataAsync(Matter matter, Client? client, HashSet<string> sourceWhitelist, CancellationToken ct)
        {
            var data = new ClientTransparencySourceData
            {
                Matter = matter,
                Client = client
            };

            if (sourceWhitelist.Contains("task"))
            {
                data.OpenTasks = await _context.Tasks
                    .Where(t => t.MatterId == matter.Id && t.Status != "Done" && t.Status != "Completed")
                    .OrderBy(t => t.DueDate ?? DateTime.MaxValue)
                    .ThenByDescending(t => t.UpdatedAt)
                    .Take(10)
                    .ToListAsync(ct);
            }

            if (sourceWhitelist.Contains("invoice"))
            {
                data.LatestInvoice = await _context.Invoices
                    .Where(i => i.MatterId == matter.Id)
                    .OrderByDescending(i => i.IssueDate)
                    .ThenByDescending(i => i.CreatedAt)
                    .FirstOrDefaultAsync(ct);
            }

            if (sourceWhitelist.Contains("payment"))
            {
                data.LatestPayment = await _context.PaymentTransactions
                    .Where(p => p.MatterId == matter.Id)
                    .OrderByDescending(p => p.ProcessedAt ?? p.CreatedAt)
                    .ThenByDescending(p => p.CreatedAt)
                    .FirstOrDefaultAsync(ct);
            }

            if (sourceWhitelist.Contains("efiling"))
            {
                data.LatestEfiling = await _context.EfilingSubmissions
                    .Where(e => e.MatterId == matter.Id)
                    .OrderByDescending(e => e.UpdatedAt)
                    .ThenByDescending(e => e.LastSeenAt)
                    .FirstOrDefaultAsync(ct);
            }

            if (sourceWhitelist.Contains("docket"))
            {
                data.LatestDocket = await _context.CourtDocketEntries
                    .Where(d => d.MatterId == matter.Id)
                    .OrderByDescending(d => d.ModifiedAt ?? d.FiledAt ?? d.UpdatedAt)
                    .ThenByDescending(d => d.LastSeenAt)
                    .FirstOrDefaultAsync(ct);
            }

            if (sourceWhitelist.Contains("planner"))
            {
                data.OutcomePlan = await _context.OutcomeFeePlans
                    .Where(p => p.MatterId == matter.Id && p.Status == "active")
                    .OrderByDescending(p => p.UpdatedAt)
                    .FirstOrDefaultAsync(ct);

                if (data.OutcomePlan != null)
                {
                    data.OutcomePlanVersion = !string.IsNullOrWhiteSpace(data.OutcomePlan.CurrentVersionId)
                        ? await _context.OutcomeFeePlanVersions.FirstOrDefaultAsync(v => v.Id == data.OutcomePlan.CurrentVersionId, ct)
                        : await _context.OutcomeFeePlanVersions
                            .Where(v => v.PlanId == data.OutcomePlan.Id)
                            .OrderByDescending(v => v.VersionNumber)
                            .FirstOrDefaultAsync(ct);

                    if (data.OutcomePlanVersion != null)
                    {
                        data.BaseScenario = await _context.OutcomeFeeScenarios
                            .Where(s => s.PlanVersionId == data.OutcomePlanVersion.Id)
                            .OrderBy(s => s.ScenarioKey == "base" ? 0 : 1)
                            .ThenByDescending(s => s.Probability)
                            .FirstOrDefaultAsync(ct);
                    }

                    try
                    {
                        data.PlannerCompare = await _outcomeFeePlannerService.CompareVersionsAsync(data.OutcomePlan.Id, null, data.OutcomePlan.CurrentVersionId, ct);
                    }
                    catch
                    {
                        // Fail-open for transparency snapshot generation.
                        data.PlannerCompare = null;
                    }
                }
            }

            return data;
        }

        private BuiltTransparencySnapshot BuildSkeletonSnapshot(
            Matter matter,
            Client? client,
            ClientTransparencySourceData sourceData,
            ClientTransparencyRegenerateRequest request,
            ClientTransparencyProfile profile,
            DateTime now,
            string correlationId,
            int versionNumber,
            ClientTransparencySnapshot? previousCurrent,
            string actor)
        {
            var timeline = BuildTimelineItems(matter, sourceData, now);
            var delays = BuildDelayReasons(sourceData, now);
            var nextStep = BuildNextStep(matter, sourceData, now);
            var costImpact = BuildCostImpact(sourceData, now);
            var riskFlags = BuildRiskFlags(matter, sourceData, delays, costImpact);

            var snapshot = new ClientTransparencySnapshot
            {
                MatterId = matter.Id,
                ProfileId = profile.Id,
                VersionNumber = versionNumber,
                Status = "generated",
                IsCurrent = true,
                IsPublished = false,
                DataQuality = DetermineDataQuality(sourceData, timeline.Count, costImpact != null),
                ConfidenceScore = DetermineConfidenceScore(sourceData, delays.Count),
                CorrelationId = correlationId,
                SnapshotSummary = BuildSnapshotSummary(matter, client, delays, nextStep, costImpact),
                WhatChangedSummary = BuildWhatChangedSummary(request, previousCurrent, versionNumber),
                MetadataJson = SerializeJson(new
                {
                    phase = 0,
                    generator = "client_transparency_skeleton_v1",
                    sourceWhitelist = ParseSourceWhitelist(profile.SourceWhitelistJson).OrderBy(x => x).ToArray(),
                    riskFlags,
                    actor,
                    request = new { request.TriggerType, request.TriggerEntityType, request.TriggerEntityId, request.Reason }
                }),
                GeneratedAt = now,
                CreatedAt = now,
                UpdatedAt = now
            };

            foreach (var item in timeline)
            {
                item.SnapshotId = snapshot.Id;
            }
            foreach (var delay in delays)
            {
                delay.SnapshotId = snapshot.Id;
            }
            if (nextStep != null) nextStep.SnapshotId = snapshot.Id;
            if (costImpact != null) costImpact.SnapshotId = snapshot.Id;

            var updateEvent = new ClientTransparencyUpdateEvent
            {
                MatterId = matter.Id,
                SnapshotId = snapshot.Id,
                TriggerType = string.IsNullOrWhiteSpace(request.TriggerType) ? "manual_regenerate" : request.TriggerType.Trim(),
                TriggerEntityType = request.TriggerEntityType?.Trim(),
                TriggerEntityId = request.TriggerEntityId?.Trim(),
                Status = "applied",
                CorrelationId = correlationId,
                TriggeredBy = string.IsNullOrWhiteSpace(request.TriggeredBy) ? actor : request.TriggeredBy.Trim(),
                PayloadJson = SerializeJson(new { request.Reason, request.VisibilityMode, request.ClientAudience }),
                DiffJson = SerializeJson(new
                {
                    previousSnapshotId = previousCurrent?.Id,
                    previousVersion = previousCurrent?.VersionNumber,
                    currentSnapshotId = snapshot.Id,
                    currentVersion = snapshot.VersionNumber
                }),
                MetadataJson = SerializeJson(new { generatedBy = "client_transparency_skeleton_v1" }),
                CreatedAt = now,
                AppliedAt = now
            };

            return new BuiltTransparencySnapshot(snapshot, timeline, delays, nextStep, costImpact, updateEvent);
        }

        private List<ClientTransparencyTimelineItem> BuildTimelineItems(Matter matter, ClientTransparencySourceData data, DateTime now)
        {
            var anyOpenTasks = data.OpenTasks.Any(t => !IsTaskComplete(t.Status));
            var efilingRejected = string.Equals(data.LatestEfiling?.Status, "rejected", StringComparison.OrdinalIgnoreCase);
            var efilingAccepted = string.Equals(data.LatestEfiling?.Status, "accepted", StringComparison.OrdinalIgnoreCase);
            var invoicePaid = data.LatestInvoice != null && data.LatestInvoice.Total > 0m && data.LatestInvoice.Balance <= 0m;
            var paymentSucceeded = data.LatestPayment != null && string.Equals(data.LatestPayment.Status, "Succeeded", StringComparison.OrdinalIgnoreCase);

            return new List<ClientTransparencyTimelineItem>
            {
                new()
                {
                    OrderIndex = 1,
                    PhaseKey = "matter_intake",
                    Label = "Case Intake & Review",
                    Status = string.Equals(matter.Status, "Closed", StringComparison.OrdinalIgnoreCase) ? "completed" : "in_progress",
                    ClientSafeText = "Your matter is active in our workflow and being tracked for the next milestones.",
                    StartedAtUtc = matter.OpenDate,
                    SourceRefsJson = SerializeJson(new[] { new { source = "matter", entityId = matter.Id } }),
                    MetadataJson = SerializeJson(new { matter.Status, matter.PracticeArea, matter.CourtType }),
                    CreatedAt = now,
                    UpdatedAt = now
                },
                new()
                {
                    OrderIndex = 2,
                    PhaseKey = "court_and_filing",
                    Label = "Court / Filing Activity",
                    Status = efilingRejected ? "blocked" : (efilingAccepted || data.LatestDocket != null ? "in_progress" : "pending"),
                    ClientSafeText = efilingRejected
                        ? "A filing update requires correction before the next court step can proceed."
                        : efilingAccepted
                            ? "A recent filing was accepted and court activity is moving forward."
                            : data.LatestDocket != null
                                ? "Recent court docket activity has been recorded and reviewed."
                                : "We are monitoring for the next court or filing update.",
                    StartedAtUtc = data.LatestDocket?.FiledAt ?? data.LatestEfiling?.SubmittedAt,
                    EtaAtUtc = efilingRejected ? now.AddDays(7) : null,
                    SourceRefsJson = SerializeJson(BuildSourceRefs(data.LatestDocket, data.LatestEfiling).ToArray()),
                    MetadataJson = SerializeJson(new { efilingStatus = data.LatestEfiling?.Status, court = data.LatestDocket?.Court }),
                    CreatedAt = now,
                    UpdatedAt = now
                },
                new()
                {
                    OrderIndex = 3,
                    PhaseKey = "firm_work",
                    Label = "Current Firm Work",
                    Status = anyOpenTasks ? "in_progress" : "pending",
                    ClientSafeText = anyOpenTasks
                        ? "Our team is actively working through scheduled tasks for your matter."
                        : "There is no currently scheduled internal task due immediately in this view.",
                    EtaAtUtc = data.OpenTasks.Where(t => !IsTaskComplete(t.Status)).Select(t => t.DueDate).Where(d => d.HasValue).OrderBy(d => d).FirstOrDefault(),
                    SourceRefsJson = SerializeJson(data.OpenTasks.Take(3).Select(t => new { source = "task", entityId = t.Id, title = t.Title }).ToArray()),
                    MetadataJson = SerializeJson(new { openTaskCount = data.OpenTasks.Count }),
                    CreatedAt = now,
                    UpdatedAt = now
                },
                new()
                {
                    OrderIndex = 4,
                    PhaseKey = "billing",
                    Label = "Billing & Payments",
                    Status = invoicePaid ? "completed" : (data.LatestInvoice != null ? "in_progress" : "pending"),
                    ClientSafeText = data.LatestInvoice == null
                        ? "No invoice has been issued yet for the tracked work shown in this summary."
                        : invoicePaid
                            ? "The latest invoice for this matter is paid."
                            : paymentSucceeded
                                ? "A payment has been received and any remaining balance is still being tracked."
                                : "Billing is active and payment timing may affect scheduling and cost timing.",
                    EtaAtUtc = data.LatestInvoice?.DueDate,
                    SourceRefsJson = SerializeJson(BuildSourceRefs(data.LatestInvoice, data.LatestPayment).ToArray()),
                    MetadataJson = SerializeJson(new { invoiceStatus = data.LatestInvoice?.Status.ToString(), balance = data.LatestInvoice?.Balance }),
                    CreatedAt = now,
                    UpdatedAt = now
                }
            };
        }

        private List<ClientTransparencyDelayReason> BuildDelayReasons(ClientTransparencySourceData data, DateTime now)
        {
            var delays = new List<ClientTransparencyDelayReason>();

            if (data.LatestEfiling != null && string.Equals(data.LatestEfiling.Status, "rejected", StringComparison.OrdinalIgnoreCase))
            {
                delays.Add(new ClientTransparencyDelayReason
                {
                    ReasonCode = "filing_correction",
                    Severity = "medium",
                    ExpectedDelayDays = 3,
                    ClientSafeText = "A filing is being corrected before resubmission.",
                    SourceRefsJson = SerializeJson(new[] { new { source = "efiling", entityId = data.LatestEfiling.Id, status = data.LatestEfiling.Status } }),
                    MetadataJson = SerializeJson(new { hasRejectionReason = !string.IsNullOrWhiteSpace(data.LatestEfiling.RejectionReason) }),
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }

            var overdueTask = data.OpenTasks
                .Where(t => t.DueDate.HasValue && t.DueDate.Value < now && !IsTaskComplete(t.Status))
                .OrderBy(t => t.DueDate)
                .FirstOrDefault();
            if (overdueTask != null)
            {
                delays.Add(new ClientTransparencyDelayReason
                {
                    ReasonCode = "internal_task_backlog",
                    Severity = "low",
                    ExpectedDelayDays = Math.Max(1, (int)Math.Ceiling((now - overdueTask.DueDate!.Value).TotalDays)),
                    ClientSafeText = "An internal task has been re-prioritized after missing its target date.",
                    SourceRefsJson = SerializeJson(new[] { new { source = "task", entityId = overdueTask.Id, dueDate = overdueTask.DueDate } }),
                    MetadataJson = SerializeJson(new { overdueTask.Priority, overdueTask.Status }),
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }

            if (data.LatestInvoice != null && data.LatestInvoice.Balance > 0m && data.LatestInvoice.DueDate.HasValue && data.LatestInvoice.DueDate.Value < now)
            {
                delays.Add(new ClientTransparencyDelayReason
                {
                    ReasonCode = "payment_timing",
                    Severity = "low",
                    ExpectedDelayDays = Math.Max(1, (int)Math.Ceiling((now - data.LatestInvoice.DueDate.Value).TotalDays)),
                    ClientSafeText = "Billing timing may affect when some follow-up work is scheduled.",
                    SourceRefsJson = SerializeJson(new[] { new { source = "invoice", entityId = data.LatestInvoice.Id, balance = data.LatestInvoice.Balance } }),
                    MetadataJson = SerializeJson(new { dueDate = data.LatestInvoice.DueDate, status = data.LatestInvoice.Status.ToString() }),
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }

            return delays;
        }

        private ClientTransparencyNextStep BuildNextStep(Matter matter, ClientTransparencySourceData data, DateTime now)
        {
            var task = data.OpenTasks
                .Where(t => !IsTaskComplete(t.Status))
                .OrderBy(t => t.DueDate ?? DateTime.MaxValue)
                .ThenBy(t => t.Title)
                .FirstOrDefault();

            if (task != null)
            {
                return new ClientTransparencyNextStep
                {
                    OwnerType = "firm",
                    Status = "pending",
                    ActionText = $"Our team will work on the next scheduled item: {SafeText(task.Title, 120)}.",
                    EtaAtUtc = task.DueDate,
                    SourceRefsJson = SerializeJson(new[] { new { source = "task", entityId = task.Id, status = task.Status } }),
                    MetadataJson = SerializeJson(new { task.Priority, task.Status }),
                    CreatedAt = now,
                    UpdatedAt = now
                };
            }

            if (data.LatestEfiling != null && string.Equals(data.LatestEfiling.Status, "rejected", StringComparison.OrdinalIgnoreCase))
            {
                return new ClientTransparencyNextStep
                {
                    OwnerType = "firm",
                    Status = "pending",
                    ActionText = "We will prepare a corrected filing package and resubmit it.",
                    EtaAtUtc = now.AddDays(3),
                    SourceRefsJson = SerializeJson(new[] { new { source = "efiling", entityId = data.LatestEfiling.Id } }),
                    MetadataJson = SerializeJson(new { status = data.LatestEfiling.Status }),
                    CreatedAt = now,
                    UpdatedAt = now
                };
            }

            if (data.LatestInvoice != null && data.LatestInvoice.Balance > 0m)
            {
                return new ClientTransparencyNextStep
                {
                    OwnerType = "client",
                    Status = "pending",
                    ActionText = "Please review the current billing status in the portal and contact us if you need billing support.",
                    EtaAtUtc = data.LatestInvoice.DueDate,
                    BlockedByText = data.LatestInvoice.DueDate.HasValue && data.LatestInvoice.DueDate.Value < now
                        ? "Open balance timing may affect scheduling."
                        : null,
                    SourceRefsJson = SerializeJson(new[] { new { source = "invoice", entityId = data.LatestInvoice.Id, balance = data.LatestInvoice.Balance } }),
                    MetadataJson = SerializeJson(new { status = data.LatestInvoice.Status.ToString() }),
                    CreatedAt = now,
                    UpdatedAt = now
                };
            }

            return new ClientTransparencyNextStep
            {
                OwnerType = string.Equals(matter.Status, "Closed", StringComparison.OrdinalIgnoreCase) ? "firm" : "court",
                Status = string.Equals(matter.Status, "Closed", StringComparison.OrdinalIgnoreCase) ? "completed" : "pending",
                ActionText = string.Equals(matter.Status, "Closed", StringComparison.OrdinalIgnoreCase)
                    ? "No immediate next step is required at this time."
                    : "We are monitoring for the next court or workflow milestone and will update this page when it changes.",
                SourceRefsJson = SerializeJson(new[] { new { source = "matter", entityId = matter.Id } }),
                MetadataJson = SerializeJson(new { matter.Status }),
                CreatedAt = now,
                UpdatedAt = now
            };
        }

        private ClientTransparencyCostImpact? BuildCostImpact(ClientTransparencySourceData data, DateTime now)
        {
            var scenario = data.BaseScenario;
            if (scenario == null) return null;

            var variance = Math.Max(500m, Math.Round(scenario.BudgetTotal * 0.10m, 2, MidpointRounding.AwayFromZero));
            var expectedMin = Math.Max(0m, scenario.BudgetTotal - variance);
            var expectedMax = scenario.BudgetTotal + variance;

            decimal deltaMin = 0m;
            decimal deltaMax = 0m;
            var driftSummary = ParsePlannerDriftSummary(data.PlannerCompare?.DriftSummary);
            if (driftSummary != null)
            {
                var collectionsDelta = Math.Round((driftSummary.ActualCollected - driftSummary.PlannedCollections), 2, MidpointRounding.AwayFromZero);
                var marginCompression = Math.Round(scenario.ExpectedMargin * driftSummary.MarginCompressionRatio, 2, MidpointRounding.AwayFromZero);
                deltaMin = Math.Round(collectionsDelta - marginCompression, 2, MidpointRounding.AwayFromZero);
                deltaMax = Math.Round(collectionsDelta, 2, MidpointRounding.AwayFromZero);
            }
            else if (data.LatestInvoice != null)
            {
                var deltaBase = data.LatestInvoice.Total - scenario.BudgetTotal;
                deltaMin = Math.Round(deltaBase * 0.20m, 2, MidpointRounding.AwayFromZero);
                deltaMax = Math.Round(deltaBase * 0.35m, 2, MidpointRounding.AwayFromZero);
            }

            var confidenceBand = (scenario.ConfidenceScore ?? 0.50m) switch
            {
                >= 0.80m => "high",
                >= 0.55m => "medium",
                _ => "low"
            };

            return new ClientTransparencyCostImpact
            {
                Currency = string.IsNullOrWhiteSpace(scenario.Currency) ? "USD" : scenario.Currency,
                CurrentExpectedRangeMin = expectedMin,
                CurrentExpectedRangeMax = expectedMax,
                DeltaRangeMin = deltaMin,
                DeltaRangeMax = deltaMax,
                ConfidenceBand = confidenceBand,
                DriverSummary = SafeText(scenario.DriverSummary, 512) ?? "Estimate is based on current case stage, work volume assumptions, and billing signals.",
                DriversJson = scenario.MetadataJson,
                SourceRefsJson = SerializeJson(new[]
                {
                    new { source = "planner", entityId = data.OutcomePlan?.Id, planVersionId = data.OutcomePlanVersion?.Id, scenarioId = scenario.Id }
                }),
                MetadataJson = SerializeJson(new
                {
                    scenario.ScenarioKey,
                    scenario.Probability,
                    scenario.ExpectedCollected,
                    scenario.ExpectedMargin,
                    plannerDrift = driftSummary == null ? null : new
                    {
                        driftSummary.HoursDriftRatio,
                        driftSummary.CollectionsDriftRatio,
                        driftSummary.MarginCompressionRatio,
                        driftSummary.CollectionsRiskWorsened,
                        driftSummary.Severity
                    }
                }),
                CreatedAt = now,
                UpdatedAt = now
            };
        }

        private static IReadOnlyList<string> BuildRiskFlags(
            Matter matter,
            ClientTransparencySourceData data,
            IReadOnlyList<ClientTransparencyDelayReason> delays,
            ClientTransparencyCostImpact? costImpact)
        {
            var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (delays.Any(d => string.Equals(d.ReasonCode, "filing_correction", StringComparison.OrdinalIgnoreCase)))
            {
                flags.Add("filing_correction_pending");
            }
            if (delays.Any(d => string.Equals(d.ReasonCode, "payment_timing", StringComparison.OrdinalIgnoreCase)))
            {
                flags.Add("billing_timing_risk");
            }

            if (data.PlannerCompare?.DriftSummary != null)
            {
                var drift = ParsePlannerDriftSummary(data.PlannerCompare.DriftSummary);
                if (drift != null)
                {
                    if (drift.CollectionsRiskWorsened) flags.Add("high_collections_risk");
                    if (drift.MarginCompressionRatio >= 0.15m) flags.Add("margin_compression_risk");
                    if (drift.HoursDriftRatio >= 0.20m) flags.Add("workload_drift");
                    if (drift.CollectionsDriftRatio >= 0.20m) flags.Add("collections_drift");
                }
            }

            if (string.Equals(matter.Status, "Closed", StringComparison.OrdinalIgnoreCase))
            {
                flags.Add("matter_closed");
            }
            if (data.BaseScenario == null || costImpact == null)
            {
                flags.Add("low_cost_visibility");
            }

            return flags.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private static string BuildSnapshotSummary(Matter matter, Client? client, IReadOnlyList<ClientTransparencyDelayReason> delays, ClientTransparencyNextStep nextStep, ClientTransparencyCostImpact? costImpact)
        {
            var clientName = string.IsNullOrWhiteSpace(client?.Name) ? "Client" : client!.Name!;
            var delayText = delays.Count > 0
                ? " A delay factor is currently being tracked."
                : " No active delay factor is currently flagged.";

            var costText = costImpact == null
                ? " Cost impact estimate is not yet available."
                : $" Expected cost impact is currently estimated at {costImpact.Currency} {costImpact.CurrentExpectedRangeMin?.ToString("N0", CultureInfo.InvariantCulture)}-{costImpact.CurrentExpectedRangeMax?.ToString("N0", CultureInfo.InvariantCulture)} ({costImpact.ConfidenceBand} confidence).";

            return SafeText($"{clientName}, this update summarizes where your matter stands and the next likely step. Next step: {nextStep.ActionText}.{delayText}{costText}", 2000)
                ?? "Client transparency snapshot generated.";
        }

        private static string BuildWhatChangedSummary(ClientTransparencyRegenerateRequest request, ClientTransparencySnapshot? previousCurrent, int versionNumber)
        {
            if (previousCurrent == null)
            {
                return $"Initial client transparency snapshot generated (version {versionNumber}).";
            }

            var trigger = string.IsNullOrWhiteSpace(request.TriggerType) ? "manual_regenerate" : request.TriggerType.Trim();
            return $"Snapshot refreshed from version {previousCurrent.VersionNumber} to {versionNumber} (trigger: {trigger}).";
        }

        private static string DetermineDataQuality(ClientTransparencySourceData data, int timelineCount, bool hasCostImpact)
        {
            var score = 0;
            if (data.LatestDocket != null) score++;
            if (data.LatestEfiling != null) score++;
            if (data.OpenTasks.Count > 0) score++;
            if (data.LatestInvoice != null) score++;
            if (data.LatestPayment != null) score++;
            if (hasCostImpact) score++;

            if (score >= 4 && timelineCount >= 4) return "high";
            if (score >= 2) return "medium";
            return "low";
        }

        private static decimal DetermineConfidenceScore(ClientTransparencySourceData data, int delayCount)
        {
            decimal confidence = 0.45m;

            if (data.BaseScenario?.ConfidenceScore is decimal plannerConfidence)
            {
                confidence = (confidence + plannerConfidence) / 2m;
            }

            if (data.LatestDocket != null) confidence += 0.08m;
            if (data.LatestEfiling != null) confidence += 0.08m;
            if (data.OpenTasks.Count > 0) confidence += 0.04m;
            if (data.LatestInvoice != null) confidence += 0.04m;
            if (delayCount > 0) confidence -= 0.05m;

            return Math.Clamp(Math.Round(confidence, 2, MidpointRounding.AwayFromZero), 0m, 1m);
        }

        private static IEnumerable<object> BuildSourceRefs(params object?[] sources)
        {
            foreach (var source in sources)
            {
                switch (source)
                {
                    case CourtDocketEntry docket:
                        yield return new { source = "docket", entityId = docket.Id, filedAt = docket.FiledAt, court = docket.Court };
                        break;
                    case EfilingSubmission filing:
                        yield return new { source = "efiling", entityId = filing.Id, status = filing.Status, provider = filing.ProviderKey };
                        break;
                    case Invoice invoice:
                        yield return new { source = "invoice", entityId = invoice.Id, status = invoice.Status.ToString(), balance = invoice.Balance };
                        break;
                    case PaymentTransaction payment:
                        yield return new { source = "payment", entityId = payment.Id, status = payment.Status, amount = payment.Amount };
                        break;
                }
            }
        }

        private static bool IsTaskComplete(string? status)
        {
            return string.Equals(status, "Done", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "Completed", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "Closed", StringComparison.OrdinalIgnoreCase);
        }

        private string EnsureTenant()
        {
            var tenantId = _tenantContext.TenantId;
            if (string.IsNullOrWhiteSpace(tenantId))
            {
                throw new InvalidOperationException("Tenant context is required.");
            }

            return tenantId;
        }

        private static string? NormalizeId(string? id)
        {
            return string.IsNullOrWhiteSpace(id) ? null : id.Trim();
        }

        private static HashSet<string> ParseSourceWhitelist(string? json)
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<string[]>(json ?? string.Empty, JsonOptions);
                if (parsed is { Length: > 0 })
                {
                    return new HashSet<string>(
                        parsed.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim().ToLowerInvariant()),
                        StringComparer.OrdinalIgnoreCase);
                }
            }
            catch
            {
                // default fallback below
            }

            return new HashSet<string>(DefaultSourceWhitelist, StringComparer.OrdinalIgnoreCase);
        }

        private static string? SafeText(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var trimmed = value.Trim();
            return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
        }

        private static string? SerializeJson(object? value)
        {
            return value == null ? null : JsonSerializer.Serialize(value, JsonOptions);
        }

        private static IReadOnlyList<string> ParseRiskFlagsFromSnapshotMetadata(string? metadataJson)
        {
            if (string.IsNullOrWhiteSpace(metadataJson)) return Array.Empty<string>();

            try
            {
                using var doc = JsonDocument.Parse(metadataJson);
                if (!doc.RootElement.TryGetProperty("riskFlags", out var flagsNode) || flagsNode.ValueKind != JsonValueKind.Array)
                {
                    return Array.Empty<string>();
                }

                return flagsNode.EnumerateArray()
                    .Where(x => x.ValueKind == JsonValueKind.String)
                    .Select(x => x.GetString())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x!.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        private static PlannerDriftSummary? ParsePlannerDriftSummary(object? driftSummary)
        {
            if (driftSummary == null) return null;

            try
            {
                using var doc = JsonDocument.Parse(JsonSerializer.Serialize(driftSummary, JsonOptions));
                var root = doc.RootElement;
                return new PlannerDriftSummary
                {
                    PlannedCollections = GetDecimal(root, "plannedCollections"),
                    ActualCollected = GetDecimal(root, "actualCollected"),
                    HoursDriftRatio = GetDecimal(root, "hoursDriftRatio"),
                    CollectionsDriftRatio = GetDecimal(root, "collectionsDriftRatio"),
                    MarginCompressionRatio = GetDecimal(root, "marginCompressionRatio"),
                    CollectionsRiskWorsened = GetBool(root, "collectionsRiskWorsened"),
                    Severity = GetString(root, "severity")
                };
            }
            catch
            {
                return null;
            }
        }

        private static decimal GetDecimal(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out var node)) return 0m;
            if (node.ValueKind == JsonValueKind.Number && node.TryGetDecimal(out var value)) return value;
            if (node.ValueKind == JsonValueKind.String && decimal.TryParse(node.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)) return parsed;
            return 0m;
        }

        private static bool GetBool(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out var node)) return false;
            return node.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String when bool.TryParse(node.GetString(), out var parsed) => parsed,
                _ => false
            };
        }

        private static string? GetString(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out var node) || node.ValueKind != JsonValueKind.String) return null;
            return node.GetString();
        }

        private sealed class ClientTransparencySourceData
        {
            public Matter Matter { get; set; } = null!;
            public Client? Client { get; set; }
            public List<JurisFlow.Server.Models.Task> OpenTasks { get; set; } = new();
            public Invoice? LatestInvoice { get; set; }
            public PaymentTransaction? LatestPayment { get; set; }
            public EfilingSubmission? LatestEfiling { get; set; }
            public CourtDocketEntry? LatestDocket { get; set; }
            public OutcomeFeePlan? OutcomePlan { get; set; }
            public OutcomeFeePlanVersion? OutcomePlanVersion { get; set; }
            public OutcomeFeeScenario? BaseScenario { get; set; }
            public OutcomeFeePlanVersionCompareResult? PlannerCompare { get; set; }
        }

        private sealed record BuiltTransparencySnapshot(
            ClientTransparencySnapshot Snapshot,
            List<ClientTransparencyTimelineItem> TimelineItems,
            List<ClientTransparencyDelayReason> DelayReasons,
            ClientTransparencyNextStep? NextStep,
            ClientTransparencyCostImpact? CostImpact,
            ClientTransparencyUpdateEvent UpdateEvent);

        private sealed class PlannerDriftSummary
        {
            public decimal PlannedCollections { get; set; }
            public decimal ActualCollected { get; set; }
            public decimal HoursDriftRatio { get; set; }
            public decimal CollectionsDriftRatio { get; set; }
            public decimal MarginCompressionRatio { get; set; }
            public bool CollectionsRiskWorsened { get; set; }
            public string? Severity { get; set; }
        }
    }

    public class ClientTransparencyRegenerateRequest
    {
        public string? TriggerType { get; set; } = "manual_regenerate";
        public string? TriggerEntityType { get; set; }
        public string? TriggerEntityId { get; set; }
        public string? Reason { get; set; }
        public string? VisibilityMode { get; set; }
        public string? ClientAudience { get; set; } = "portal";
        public string? CorrelationId { get; set; }
        public string? TriggeredBy { get; set; }
    }

    public class ClientTransparencySnapshotDetailResult
    {
        public ClientTransparencyProfile? Profile { get; set; }
        public ClientTransparencySnapshot? Snapshot { get; set; }
        public IReadOnlyList<ClientTransparencyTimelineItem> TimelineItems { get; set; } = Array.Empty<ClientTransparencyTimelineItem>();
        public IReadOnlyList<ClientTransparencyDelayReason> DelayReasons { get; set; } = Array.Empty<ClientTransparencyDelayReason>();
        public ClientTransparencyNextStep? NextStep { get; set; }
        public ClientTransparencyCostImpact? CostImpact { get; set; }
        public IReadOnlyList<string> RiskFlags { get; set; } = Array.Empty<string>();
    }

    public class ClientTransparencyHistoryResult
    {
        public string MatterId { get; set; } = string.Empty;
        public IReadOnlyList<ClientTransparencyUpdateEvent> Events { get; set; } = Array.Empty<ClientTransparencyUpdateEvent>();
        public IReadOnlyList<ClientTransparencySnapshot> Snapshots { get; set; } = Array.Empty<ClientTransparencySnapshot>();
    }
}
