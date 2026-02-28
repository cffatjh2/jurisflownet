using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using JurisFlow.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Task = System.Threading.Tasks.Task;

namespace JurisFlow.Server.Controllers
{
    [Route("api/efiling")]
    [ApiController]
    [Authorize(Policy = "StaffOnly")]
    public class EfilingController : ControllerBase
    {
        private static readonly HashSet<string> AllowedPacketExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf", ".doc", ".docx", ".txt", ".jpg", ".jpeg", ".png"
        };

        private static readonly HashSet<string> AllowedMetadataKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            "caseNumber",
            "partyRole",
            "filingType",
            "jurisdictionCode",
            "jurisdiction",
            "state",
            "caseType",
            "filingMethod",
            "courtSystem",
            "courtDivision",
            "venue"
        };

        private static readonly HashSet<string> AllowedSubmissionStatuses = new(StringComparer.OrdinalIgnoreCase)
        {
            "draft",
            "pending",
            "processing",
            "submitted",
            "accepted",
            "rejected",
            "corrected",
            "failed"
        };

        private readonly JurisFlowDbContext _context;
        private readonly EfilingAutomationService _efilingAutomationService;
        private readonly IntegrationConnectorService _integrationConnectorService;
        private readonly JurisdictionRulesPlatformService _jurisdictionRulesPlatformService;
        private readonly AuditLogger _auditLogger;
        private readonly IConfiguration _configuration;
        private readonly OutcomeFeePlannerService _outcomeFeePlanner;
        private readonly ClientTransparencyService _clientTransparencyService;
        private readonly TenantContext _tenantContext;
        private readonly ILogger<EfilingController> _logger;

        public EfilingController(
            JurisFlowDbContext context,
            EfilingAutomationService efilingAutomationService,
            IntegrationConnectorService integrationConnectorService,
            JurisdictionRulesPlatformService jurisdictionRulesPlatformService,
            AuditLogger auditLogger,
            IConfiguration configuration,
            OutcomeFeePlannerService outcomeFeePlanner,
            ClientTransparencyService clientTransparencyService,
            TenantContext tenantContext,
            ILogger<EfilingController> logger)
        {
            _context = context;
            _efilingAutomationService = efilingAutomationService;
            _integrationConnectorService = integrationConnectorService;
            _jurisdictionRulesPlatformService = jurisdictionRulesPlatformService;
            _auditLogger = auditLogger;
            _configuration = configuration;
            _outcomeFeePlanner = outcomeFeePlanner;
            _clientTransparencyService = clientTransparencyService;
            _tenantContext = tenantContext;
            _logger = logger;
        }

        [HttpGet("workspace/{matterId}")]
        public async Task<IActionResult> GetWorkspace(string matterId, [FromQuery] string? providerKey = null, CancellationToken cancellationToken = default)
        {
            var matter = await RequireMatterAsync(matterId, cancellationToken, asNoTracking: true);
            if (matter == null) return NotFound(new { message = "Matter not found." });

            var docs = await TenantScope(_context.Documents).AsNoTracking()
                .Where(d => d.MatterId == matterId)
                .OrderByDescending(d => d.UpdatedAt)
                .Take(100)
                .Select(d => new { d.Id, d.Name, d.FileName, d.FileSize, d.MimeType, d.Category, d.Tags, d.Status, d.UpdatedAt })
                .ToListAsync(cancellationToken);

            var dockets = await TenantScope(_context.CourtDocketEntries).AsNoTracking()
                .Where(d => d.MatterId == matterId)
                .OrderByDescending(d => d.ModifiedAt ?? d.LastSeenAt)
                .Take(25)
                .Select(d => new { d.Id, d.ProviderKey, d.ExternalDocketId, d.DocketNumber, d.CaseName, d.Court, d.SourceUrl, d.FiledAt, d.ModifiedAt, d.LastSeenAt })
                .ToListAsync(cancellationToken);

            var submissions = await TenantScope(_context.EfilingSubmissions).AsNoTracking()
                .Where(s => s.MatterId == matterId)
                .OrderByDescending(s => s.UpdatedAt)
                .Take(25)
                .Select(s => new { s.Id, s.ProviderKey, s.ExternalSubmissionId, s.ExternalDocketId, s.ReferenceNumber, s.Status, s.SubmittedAt, s.AcceptedAt, s.RejectedAt, s.RejectionReason, s.UpdatedAt })
                .ToListAsync(cancellationToken);

            var efilingProviderKeys = new[]
            {
                IntegrationProviderKeys.CourtListenerRecap,
                IntegrationProviderKeys.OneLegalEfile,
                IntegrationProviderKeys.FileAndServeXpressEfile
            };
            var connections = await TenantScope(_context.IntegrationConnections).AsNoTracking()
                .Where(c => efilingProviderKeys.Contains(c.ProviderKey) && c.SyncEnabled)
                .OrderBy(c => c.Provider)
                .Select(c => new { c.Id, c.ProviderKey, c.Provider, c.Status, c.AccountLabel, c.LastSyncAt, c.LastWebhookAt })
                .ToListAsync(cancellationToken);

            var rules = await TenantScope(_context.CourtRules).AsNoTracking()
                .Where(r => r.IsActive && (matter.CourtType == null || r.CourtType == null || r.CourtType == matter.CourtType))
                .OrderBy(r => r.Jurisdiction).ThenBy(r => r.TriggerEvent)
                .Take(25)
                .Select(r => new { r.Id, r.Name, r.Jurisdiction, r.CourtType, r.TriggerEvent, r.DaysCount, r.DayType, r.Direction, r.ServiceDaysAdd, r.ExtendIfWeekend, r.Citation })
                .ToListAsync(cancellationToken);

            return Ok(new
            {
                matter = new { matter.Id, matter.Name, matter.CaseNumber, matter.Status, matter.CourtType },
                providerKey = string.IsNullOrWhiteSpace(providerKey) ? null : providerKey.Trim().ToLowerInvariant(),
                documents = docs,
                dockets,
                submissions,
                connections,
                courtRules = rules,
                suggestedPacket = new
                {
                    packetName = $"{matter.CaseNumber} Filing Packet",
                    suggestedFilingType = InferFilingType(docs.Select(d => $"{d.Name} {d.FileName}")),
                    suggestedDocumentIds = docs
                        .OrderByDescending(d => ScoreDocumentForPacket(d.Name, d.FileName, d.Category, d.Tags))
                        .ThenByDescending(d => d.UpdatedAt)
                        .Take(10)
                        .Select(d => d.Id)
                        .ToList()
                }
            });
        }

        [HttpPost("precheck")]
        [EnableRateLimiting("AdminDangerousOps")]
        public async Task<IActionResult> PrecheckPacket([FromBody] EfilingPrecheckRequest? request, CancellationToken cancellationToken)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.MatterId))
            {
                return BadRequest(new { message = "MatterId is required." });
            }

            var errors = new List<object>();
            var warnings = new List<object>();
            var matter = await RequireMatterAsync(request.MatterId, cancellationToken, asNoTracking: true);
            if (matter == null)
            {
                return NotFound(new { message = "Matter not found." });
            }

            var documentIds = (request.DocumentIds ?? new List<string>())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (documentIds.Count == 0)
            {
                errors.Add(new { code = "missing_documents", message = "At least one filing document is required." });
            }

            var metadata = NormalizeMetadata(request.Metadata);
            var documents = await LoadPacketDocumentsAsync(matter.Id, documentIds, cancellationToken);
            var documentMap = documents.ToDictionary(d => d.Id, StringComparer.Ordinal);
            if (documentIds.Count > 0 && documents.Count != documentIds.Count)
            {
                errors.Add(new { code = "invalid_document_selection", message = "One or more selected documents are unavailable for the target matter." });
            }

            var maxDocumentMb = Math.Clamp(_configuration.GetValue<int?>("Efiling:PacketPrecheck:MaxDocumentMb") ?? 25, 1, 500);
            var maxDocumentBytes = maxDocumentMb * 1024L * 1024L;

            foreach (var id in documentIds)
            {
                if (!documentMap.TryGetValue(id, out var doc))
                {
                    continue;
                }

                var ext = Path.GetExtension(doc.FileName ?? string.Empty);
                if (string.IsNullOrWhiteSpace(ext) || !AllowedPacketExtensions.Contains(ext))
                {
                    errors.Add(new { code = "unsupported_extension", message = $"Document '{doc.FileName}' extension '{ext}' is not allowed." });
                }
                if (doc.FileSize > maxDocumentBytes)
                {
                    errors.Add(new { code = "document_too_large", message = $"Document '{doc.FileName}' exceeds {maxDocumentMb}MB limit." });
                }
                if (!string.Equals(ext, ".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    warnings.Add(new { code = "non_pdf", message = $"Document '{doc.FileName}' is not PDF; many courts require PDF." });
                }
            }

            RequireMetadata(metadata, "caseNumber", errors, "Case number metadata is required.");
            RequireMetadata(metadata, "partyRole", errors, "Party role metadata is required.");
            RequireMetadata(metadata, "filingType", errors, "Filing type metadata is required.");
            if (!string.Equals(metadata.GetValueOrDefault("caseNumber"), matter.CaseNumber, StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add(new { code = "case_number_mismatch", message = "Provided case number does not match matter case number." });
            }

            var jurisdictionCode = ResolveJurisdictionCode(metadata, matter);
            var resolvedCaseType = metadata.GetValueOrDefault("caseType") ?? request.FilingType;
            var resolvedFilingMethod = metadata.GetValueOrDefault("filingMethod") ?? "e_filing";
            var resolvedCourtSystem = metadata.GetValueOrDefault("courtSystem") ?? request.CourtType ?? matter.CourtType;
            JurisdictionCoverageResolution? jurisdictionResolution = null;
            string? jurisdictionReviewItemId = null;
            var requireHumanReviewForLowConfidence = _configuration.GetValue<bool?>("Efiling:JurisdictionRules:RequireHumanReviewForLowConfidence") ?? true;

            if (string.IsNullOrWhiteSpace(jurisdictionCode))
            {
                warnings.Add(new
                {
                    code = "jurisdiction_context_missing",
                    message = "Jurisdiction code is missing; court-specific rules coverage could not be resolved."
                });
            }
            else
            {
                jurisdictionResolution = await _jurisdictionRulesPlatformService.ResolveCoverageAsync(
                    new JurisdictionCoverageResolveRequest
                    {
                        JurisdictionCode = jurisdictionCode,
                        CourtSystem = resolvedCourtSystem,
                        CourtDivision = metadata.GetValueOrDefault("courtDivision"),
                        Venue = metadata.GetValueOrDefault("venue"),
                        CaseType = resolvedCaseType,
                        FilingMethod = resolvedFilingMethod,
                        AsOfUtc = DateTime.UtcNow
                    },
                    cancellationToken);

                ApplyJurisdictionRulePackPrecheckRules(
                    jurisdictionResolution.RulePack,
                    documents,
                    metadata,
                    errors,
                    warnings);

                if (jurisdictionResolution.RequiresHumanReview)
                {
                    var payload = new
                    {
                        code = "jurisdiction_rules_review_required",
                        message = "Court-specific rules coverage is missing or low-confidence. Human review is required before submission.",
                        supportLevel = jurisdictionResolution.SupportLevel,
                        confidenceLevel = jurisdictionResolution.ConfidenceLevel,
                        confidenceScore = jurisdictionResolution.ConfidenceScore,
                        reasons = jurisdictionResolution.ReasonCodes
                    };

                    if (requireHumanReviewForLowConfidence)
                    {
                        errors.Add(payload);
                    }
                    else
                    {
                        warnings.Add(payload);
                    }

                    jurisdictionReviewItemId = await _jurisdictionRulesPlatformService.QueuePrecheckReviewIfRequiredAsync(
                        jurisdictionResolution,
                        new JurisdictionPrecheckReviewRequest
                        {
                            MatterId = matter.Id,
                            ProviderKey = request.ProviderKey,
                            PacketName = request.PacketName,
                            JurisdictionCode = jurisdictionCode,
                            CourtSystem = resolvedCourtSystem,
                            CourtDivision = metadata.GetValueOrDefault("courtDivision"),
                            Venue = metadata.GetValueOrDefault("venue"),
                            CaseType = resolvedCaseType,
                            FilingMethod = resolvedFilingMethod,
                            Metadata = metadata
                        },
                        cancellationToken);
                }
            }

            ApplyPartnerSpecificPrecheckRules(request.ProviderKey, matter, documents, metadata, errors, warnings);

            var rules = await TenantScope(_context.CourtRules).AsNoTracking()
                .Where(r => r.IsActive &&
                            (jurisdictionCode == null || r.Jurisdiction == null || r.Jurisdiction == jurisdictionCode) &&
                            (request.CourtType == null || r.CourtType == null || r.CourtType == request.CourtType || r.CourtType == matter.CourtType))
                .OrderBy(r => r.Jurisdiction).ThenBy(r => r.TriggerEvent)
                .Take(20)
                .ToListAsync(cancellationToken);

            if (rules.Count == 0)
            {
                var strict = _configuration.GetValue<bool?>("Efiling:PacketPrecheck:RequireCourtRuleCoverage") ?? false;
                var payload = new { code = "missing_court_rule_coverage", message = "No matching court rule coverage found for automatic precheck." };
                if (strict) errors.Add(payload); else warnings.Add(payload);
            }

            var triggerDate = request.TriggerDateUtc ?? DateTime.UtcNow;
            var holidayDates = await LoadCourtHolidayDatesAsync(jurisdictionCode, cancellationToken);
            var deadlineSuggestions = rules.Take(5).Select(r => new
            {
                ruleId = r.Id,
                ruleName = r.Name,
                triggerEvent = r.TriggerEvent,
                dueDateUtc = CalculateDeadline(triggerDate, r, holidayDates),
                priority = "Medium"
            }).ToList();

            return Ok(new
            {
                canSubmit = errors.Count == 0,
                matter = new { matter.Id, matter.Name, matter.CaseNumber, matter.CourtType, matter.Status },
                providerKey = request.ProviderKey,
                packetName = request.PacketName,
                filingType = request.FilingType,
                documents = documents.Select(d => new { d.Id, d.Name, d.FileName, d.FileSize, d.MimeType, d.Category, d.Tags }),
                matchedRules = rules.Select(r => new { r.Id, r.Name, r.Jurisdiction, r.CourtType, r.TriggerEvent, r.Citation, r.DaysCount, r.DayType, r.Direction, r.ServiceDaysAdd, r.ExtendIfWeekend }),
                suggestedDeadlines = deadlineSuggestions,
                jurisdictionCoverage = jurisdictionResolution == null ? null : new
                {
                    jurisdictionResolution.ScopeKey,
                    jurisdictionResolution.CoverageFound,
                    jurisdictionResolution.RulePackFound,
                    jurisdictionResolution.CoverageEntryId,
                    jurisdictionResolution.RulePackId,
                    jurisdictionResolution.SupportLevel,
                    jurisdictionResolution.ConfidenceLevel,
                    jurisdictionResolution.ConfidenceScore,
                    jurisdictionResolution.RequiresHumanReview,
                    jurisdictionResolution.ReasonCodes
                },
                jurisdictionReviewItemId,
                errors,
                warnings
            });
        }

        [HttpPost("submissions/submit")]
        [EnableRateLimiting("AdminDangerousOps")]
        public async Task<IActionResult> SubmitToPartner([FromBody] EfilingPartnerSubmitApiRequest? request, CancellationToken cancellationToken)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.MatterId) || string.IsNullOrWhiteSpace(request.ProviderKey))
            {
                return BadRequest(new { message = "MatterId and ProviderKey are required." });
            }

            var providerKey = request.ProviderKey.Trim().ToLowerInvariant();
            if (providerKey != IntegrationProviderKeys.OneLegalEfile &&
                providerKey != IntegrationProviderKeys.FileAndServeXpressEfile)
            {
                return BadRequest(new { message = "ProviderKey must be a supported e-filing partner provider." });
            }

            if (string.IsNullOrWhiteSpace(request.ConnectionId))
            {
                return BadRequest(new { message = "ConnectionId is required." });
            }

            var matter = await RequireMatterAsync(request.MatterId, cancellationToken, asNoTracking: true);
            if (matter == null)
            {
                return NotFound(new { message = "Matter not found." });
            }

            var documentIds = (request.DocumentIds ?? new List<string>())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (documentIds.Count == 0)
            {
                return BadRequest(new { message = "At least one document is required for e-filing submission." });
            }

            var connection = await RequireActiveConnectionAsync(request.ConnectionId, providerKey, cancellationToken);
            if (connection == null)
            {
                return BadRequest(new { message = "Selected integration connection is not available for this tenant/provider." });
            }

            var documents = await LoadPacketDocumentsAsync(matter.Id, documentIds, cancellationToken);
            if (documents.Count != documentIds.Count)
            {
                return BadRequest(new { message = "One or more selected documents are unavailable for the target matter." });
            }

            var metadata = NormalizeMetadata(request.Metadata);
            var precheckErrors = new List<object>();
            var precheckWarnings = new List<object>();
            ApplyPartnerSpecificPrecheckRules(
                providerKey,
                matter,
                documents,
                metadata,
                precheckErrors,
                precheckWarnings);

            if (precheckErrors.Count > 0)
            {
                return BadRequest(new
                {
                    message = "Provider-specific precheck failed.",
                    errors = precheckErrors,
                    warnings = precheckWarnings
                });
            }

            var submitResult = await _integrationConnectorService.SubmitEfilingPartnerPacketAsync(
                new EfilingPartnerSubmitRequest
                {
                    ProviderKey = providerKey,
                    ConnectionId = connection.Id,
                    MatterId = matter.Id,
                    ExistingSubmissionId = request.ExistingSubmissionId,
                    PacketName = request.PacketName,
                    FilingType = request.FilingType,
                    DocumentIds = documentIds,
                    Metadata = metadata
                },
                cancellationToken);

            if (!submitResult.Success)
            {
                return StatusCode(502, new
                {
                    message = submitResult.ErrorMessage ?? "Partner submission failed.",
                    errorCode = submitResult.ErrorCode,
                    warnings = precheckWarnings
                });
            }

            var internalSubmissionId = submitResult.Submissions.FirstOrDefault()?.SubmissionId;
            var externalSubmissionId = submitResult.Submissions.FirstOrDefault()?.ExternalSubmissionId;
            await _auditLogger.LogAsync(
                HttpContext,
                "efiling.submission.partner_submit",
                nameof(EfilingSubmission),
                internalSubmissionId,
                $"Provider={providerKey}, ConnectionId={connection.Id}, MatterId={matter.Id}, Packet={request.PacketName}, Synced={submitResult.SyncedCount}, ExternalSubmissionId={externalSubmissionId}");

            return Ok(new
            {
                success = true,
                providerKey,
                matterId = matter.Id,
                warnings = precheckWarnings,
                result = submitResult
            });
        }

        [HttpGet("submissions")]
        public async Task<IActionResult> GetSubmissions([FromQuery] string? matterId = null, [FromQuery] string? providerKey = null, [FromQuery] int limit = 100, CancellationToken cancellationToken = default)
        {
            if (!string.IsNullOrWhiteSpace(matterId))
            {
                var matter = await RequireMatterAsync(matterId, cancellationToken, asNoTracking: true);
                if (matter == null)
                {
                    return NotFound(new { message = "Matter not found." });
                }
            }

            var query = TenantScope(_context.EfilingSubmissions).AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(matterId)) query = query.Where(s => s.MatterId == matterId);
            if (!string.IsNullOrWhiteSpace(providerKey)) query = query.Where(s => s.ProviderKey == providerKey.Trim().ToLowerInvariant());
            var rows = await query
                .OrderByDescending(s => s.UpdatedAt)
                .Take(Math.Clamp(limit, 1, 200))
                .Select(s => new
                {
                    s.Id,
                    s.ProviderKey,
                    s.ExternalSubmissionId,
                    s.ExternalDocketId,
                    s.ReferenceNumber,
                    s.Status,
                    s.MatterId,
                    s.SubmittedAt,
                    s.AcceptedAt,
                    s.RejectedAt,
                    s.RejectionReason,
                    s.LastSeenAt,
                    s.CreatedAt,
                    s.UpdatedAt
                })
                .ToListAsync(cancellationToken);
            return Ok(rows);
        }

        [HttpGet("submissions/{id}/timeline")]
        public async Task<IActionResult> GetSubmissionTimeline(string id, CancellationToken cancellationToken)
        {
            var submission = await RequireSubmissionAsync(id, cancellationToken, asNoTracking: true);
            if (submission == null) return NotFound(new { message = "Submission not found." });

            var timeline = new List<object>();
            AddTimeline(timeline, submission.CreatedAt, "submission_record_created", "system", "Submission record created.");
            AddTimeline(timeline, submission.SubmittedAt, "submission_submitted", "provider", "Submission submitted.");
            AddTimeline(timeline, submission.AcceptedAt, "submission_accepted", "provider", "Submission accepted.");
            if (submission.RejectedAt.HasValue || !string.IsNullOrWhiteSpace(submission.RejectionReason))
            {
                timeline.Add(new { timestampUtc = submission.RejectedAt ?? submission.UpdatedAt, eventType = "submission_rejected", source = "provider", title = "Submission rejected", summary = submission.RejectionReason });
            }

            var reviewItems = await TenantScope(_context.IntegrationReviewQueueItems).AsNoTracking()
                .Where(r => r.SourceType == nameof(EfilingSubmission) && r.SourceId == submission.Id)
                .OrderBy(r => r.CreatedAt)
                .Select(r => new { timestampUtc = r.CreatedAt, eventType = "review_item", source = "review_queue", title = r.Title ?? r.ItemType, summary = r.Summary, status = r.Status, reviewItemId = r.Id, decision = r.Decision })
                .ToListAsync(cancellationToken);
            timeline.AddRange(reviewItems);

            var outbox = await TenantScope(_context.IntegrationOutboxEvents).AsNoTracking()
                .Where(e => e.EntityType == nameof(EfilingSubmission) && e.EntityId == submission.Id)
                .OrderBy(e => e.CreatedAt)
                .Select(e => new { timestampUtc = e.CreatedAt, eventType = e.EventType, source = "outbox", title = $"Outbox: {e.EventType}", summary = e.ErrorMessage, status = e.Status, outboxEventId = e.Id })
                .ToListAsync(cancellationToken);
            timeline.AddRange(outbox);

            var correlationIds = new[] { submission.ExternalSubmissionId, submission.Id }
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (correlationIds.Length > 0)
            {
                var inbox = await TenantScope(_context.IntegrationInboxEvents).AsNoTracking()
                    .Where(e => e.ProviderKey == submission.ProviderKey &&
                                e.CorrelationId != null &&
                                correlationIds.Contains(e.CorrelationId))
                    .OrderBy(e => e.ReceivedAt)
                    .Select(e => new { timestampUtc = e.ReceivedAt, eventType = e.EventType ?? "provider_event", source = "inbox", title = $"Inbox: {e.EventType ?? e.ExternalEventId}", summary = e.ErrorMessage, status = e.Status, inboxEventId = e.Id })
                    .ToListAsync(cancellationToken);
                timeline.AddRange(inbox);
            }

            return Ok(new
            {
                submission = new
                {
                    submission.Id,
                    submission.ProviderKey,
                    submission.ExternalSubmissionId,
                    submission.ExternalDocketId,
                    submission.ReferenceNumber,
                    submission.Status,
                    submission.MatterId,
                    submission.SubmittedAt,
                    submission.AcceptedAt,
                    submission.RejectedAt,
                    submission.RejectionReason,
                    submission.LastSeenAt,
                    submission.CreatedAt,
                    submission.UpdatedAt
                },
                timeline = timeline.OrderBy(x => GetTimelineTimestamp(x)).ToList()
            });
        }

        [HttpPost("submissions/{id}/transition")]
        [EnableRateLimiting("AdminDangerousOps")]
        public async Task<IActionResult> TransitionSubmission(string id, [FromBody] EfilingTransitionRequest? request, CancellationToken cancellationToken)
        {
            if (!CanManageEfilingWorkflow())
            {
                return Forbid();
            }

            if (request == null || string.IsNullOrWhiteSpace(request.TargetStatus))
            {
                return BadRequest(new { message = "TargetStatus is required." });
            }

            var submission = await RequireSubmissionAsync(id, cancellationToken);
            if (submission == null) return NotFound(new { message = "Submission not found." });

            if (!TryNormalizeSubmissionStatus(submission.Status, out var current))
            {
                return BadRequest(new { message = "Current submission status is not supported for manual transition." });
            }

            if (!TryNormalizeSubmissionStatus(request.TargetStatus, out var target))
            {
                return BadRequest(new { message = "TargetStatus is not recognized." });
            }

            if (!IsAllowedTransition(current, target))
            {
                return BadRequest(new { message = $"Transition '{current}' -> '{target}' is not allowed." });
            }

            var previous = submission.Status;
            var now = DateTime.UtcNow;
            submission.Status = target;
            submission.UpdatedAt = now;
            submission.LastSeenAt = now;
            if (target == "submitted") submission.SubmittedAt ??= now;
            if (target == "accepted") submission.AcceptedAt ??= now;
            if (target == "rejected")
            {
                submission.RejectedAt ??= now;
                if (!string.IsNullOrWhiteSpace(request.RejectionReason))
                {
                    submission.RejectionReason = request.RejectionReason.Trim()[..Math.Min(1024, request.RejectionReason.Trim().Length)];
                }
            }

            await _context.SaveChangesAsync(cancellationToken);
            await _auditLogger.LogAsync(HttpContext, "efiling.submission.transition", nameof(EfilingSubmission), submission.Id, $"From={previous}, To={submission.Status}");
            await TryTriggerOutcomeFeePlannerAsync(submission, "efiling_status_transition", cancellationToken);

            return Ok(new { submissionId = submission.Id, previousStatus = previous, currentStatus = submission.Status });
        }

        [HttpPost("submissions/{id}/repair")]
        [EnableRateLimiting("AdminDangerousOps")]
        public async Task<IActionResult> StartRejectionRepair(string id, [FromBody] EfilingRepairRequest? request, CancellationToken cancellationToken)
        {
            if (!CanManageEfilingWorkflow())
            {
                return Forbid();
            }

            var submission = await RequireSubmissionAsync(id, cancellationToken);
            if (submission == null) return NotFound(new { message = "Submission not found." });

            if (!TryNormalizeSubmissionStatus(submission.Status, out var current))
            {
                return BadRequest(new { message = "Current submission status is not supported for repair workflow." });
            }

            if (current != "rejected" && current != "failed")
            {
                return BadRequest(new { message = "Repair workflow can only start from rejected or failed submissions." });
            }

            submission.Status = "corrected";
            submission.UpdatedAt = DateTime.UtcNow;
            submission.LastSeenAt = DateTime.UtcNow;
            var repairNotes = NormalizeOptionalText(request?.Notes, 1024);

            var existingReview = await TenantScope(_context.IntegrationReviewQueueItems).FirstOrDefaultAsync(r =>
                r.ProviderKey == submission.ProviderKey &&
                r.SourceType == nameof(EfilingSubmission) &&
                r.SourceId == submission.Id &&
                r.ItemType == "efile_resubmission_review" &&
                (r.Status == IntegrationReviewQueueStatuses.Pending || r.Status == IntegrationReviewQueueStatuses.InReview),
                cancellationToken);

            if (existingReview == null)
            {
                _context.IntegrationReviewQueueItems.Add(new IntegrationReviewQueueItem
                {
                    Id = Guid.NewGuid().ToString(),
                    ProviderKey = submission.ProviderKey,
                    ItemType = "efile_resubmission_review",
                    SourceType = nameof(EfilingSubmission),
                    SourceId = submission.Id,
                    Status = IntegrationReviewQueueStatuses.Pending,
                    Priority = "high",
                    Title = "Corrected filing ready for resubmission review",
                    Summary = repairNotes ?? "Verify corrected packet and resubmit.",
                    ContextJson = submission.MetadataJson,
                    SuggestedActionsJson = JsonSerializer.Serialize(new object[]
                    {
                        new { action = "run_precheck" },
                        new { action = "approve_resubmission" },
                        new { action = "mark_submitted" }
                    }),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            }

            await _context.SaveChangesAsync(cancellationToken);
            await _auditLogger.LogAsync(HttpContext, "efiling.submission.repair.start", nameof(EfilingSubmission), submission.Id, "Status=corrected");
            return Ok(new { submissionId = submission.Id, currentStatus = submission.Status, message = "Rejection repair workflow started." });
        }

        [HttpGet("dockets")]
        public async Task<IActionResult> GetDockets([FromQuery] string? matterId = null, [FromQuery] int limit = 100, CancellationToken cancellationToken = default)
        {
            if (!string.IsNullOrWhiteSpace(matterId))
            {
                var matter = await RequireMatterAsync(matterId, cancellationToken, asNoTracking: true);
                if (matter == null)
                {
                    return NotFound(new { message = "Matter not found." });
                }
            }

            var query = TenantScope(_context.CourtDocketEntries).AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(matterId)) query = query.Where(d => d.MatterId == matterId);
            var rows = await query
                .OrderByDescending(d => d.ModifiedAt ?? d.LastSeenAt)
                .Take(Math.Clamp(limit, 1, 200))
                .Select(d => new
                {
                    d.Id,
                    d.ProviderKey,
                    d.ExternalDocketId,
                    d.ExternalCaseId,
                    d.DocketNumber,
                    d.CaseName,
                    d.Court,
                    d.SourceUrl,
                    d.FiledAt,
                    d.ModifiedAt,
                    d.LastSeenAt,
                    d.MatterId,
                    d.CreatedAt,
                    d.UpdatedAt
                })
                .ToListAsync(cancellationToken);
            return Ok(rows);
        }

        [HttpPost("dockets/automation")]
        [EnableRateLimiting("AdminDangerousOps")]
        public async Task<IActionResult> RunDocketAutomation([FromBody] EfilingDocketAutomationApiRequest? request, CancellationToken cancellationToken)
        {
            if (!CanManageEfilingWorkflow())
            {
                return Forbid();
            }

            if (request == null || (string.IsNullOrWhiteSpace(request.MatterId) && (request.DocketEntryIds == null || request.DocketEntryIds.Count == 0)))
            {
                return BadRequest(new { message = "MatterId or DocketEntryIds is required." });
            }

            var providerKey = string.IsNullOrWhiteSpace(request.ProviderKey)
                ? IntegrationProviderKeys.CourtListenerDockets
                : request.ProviderKey.Trim().ToLowerInvariant();

            if (!string.IsNullOrWhiteSpace(request.ConnectionId))
            {
                var connection = await RequireActiveConnectionAsync(request.ConnectionId, providerKey, cancellationToken);
                if (connection == null)
                {
                    return BadRequest(new { message = "Selected integration connection is not available for this tenant/provider." });
                }
            }

            string? matterId = null;
            if (!string.IsNullOrWhiteSpace(request.MatterId))
            {
                var matter = await RequireMatterAsync(request.MatterId, cancellationToken, asNoTracking: true);
                if (matter == null)
                {
                    return NotFound(new { message = "Matter not found." });
                }

                matterId = matter.Id;
            }

            var requestedDocketIds = (request.DocketEntryIds ?? new List<string>())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (requestedDocketIds.Count > 0)
            {
                var scopedDocketIds = await TenantScope(_context.CourtDocketEntries)
                    .AsNoTracking()
                    .Where(d => requestedDocketIds.Contains(d.Id) &&
                                (matterId == null || d.MatterId == matterId) &&
                                d.ProviderKey == providerKey)
                    .Select(d => d.Id)
                    .ToListAsync(cancellationToken);

                if (scopedDocketIds.Count != requestedDocketIds.Count)
                {
                    return BadRequest(new { message = "One or more docket entries are unavailable for this tenant context." });
                }

                request.DocketEntryIds = scopedDocketIds;
            }

            var result = await _efilingAutomationService.RunDocketAutomationAsync(new EfilingDocketAutomationRequest
            {
                ConnectionId = request?.ConnectionId,
                ProviderKey = providerKey,
                MatterId = matterId,
                Limit = request?.Limit,
                DocketEntryIds = request?.DocketEntryIds
            }, cancellationToken);

            await _auditLogger.LogAsync(HttpContext, "efiling.dockets.automation.run", nameof(CourtDocketEntry), null,
                $"Processed={result.Processed}, Tasks={result.TasksCreated}, Deadlines={result.DeadlinesCreated}, Reviews={result.ReviewsQueued}");

            return Ok(result);
        }

        private IQueryable<T> TenantScope<T>(IQueryable<T> query) where T : class
        {
            var tenantId = RequireTenantId();
            return query.Where(entity => EF.Property<string>(entity, "TenantId") == tenantId);
        }

        private string RequireTenantId()
        {
            if (string.IsNullOrWhiteSpace(_tenantContext.TenantId))
            {
                throw new InvalidOperationException("Tenant context is required.");
            }

            return _tenantContext.TenantId;
        }

        private async Task<Matter?> RequireMatterAsync(string? matterId, CancellationToken cancellationToken, bool asNoTracking = false)
        {
            if (string.IsNullOrWhiteSpace(matterId))
            {
                return null;
            }

            IQueryable<Matter> query = TenantScope(_context.Matters).Where(m => m.Id == matterId.Trim());
            if (asNoTracking)
            {
                query = query.AsNoTracking();
            }

            return await query.FirstOrDefaultAsync(cancellationToken);
        }

        private async Task<EfilingSubmission?> RequireSubmissionAsync(string? submissionId, CancellationToken cancellationToken, bool asNoTracking = false)
        {
            if (string.IsNullOrWhiteSpace(submissionId))
            {
                return null;
            }

            IQueryable<EfilingSubmission> query = TenantScope(_context.EfilingSubmissions).Where(s => s.Id == submissionId.Trim());
            if (asNoTracking)
            {
                query = query.AsNoTracking();
            }

            return await query.FirstOrDefaultAsync(cancellationToken);
        }

        private async Task<IntegrationConnection?> RequireActiveConnectionAsync(string? connectionId, string providerKey, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(connectionId))
            {
                return null;
            }

            var normalizedConnectionId = connectionId.Trim();
            var normalizedProviderKey = providerKey.Trim().ToLowerInvariant();

            return await TenantScope(_context.IntegrationConnections)
                .AsNoTracking()
                .Where(c => c.Id == normalizedConnectionId &&
                            c.ProviderKey == normalizedProviderKey &&
                            c.SyncEnabled &&
                            c.Status == "connected")
                .FirstOrDefaultAsync(cancellationToken);
        }

        private async Task<List<EfilingPacketDocumentInfo>> LoadPacketDocumentsAsync(string matterId, IReadOnlyCollection<string> documentIds, CancellationToken cancellationToken)
        {
            if (documentIds.Count == 0)
            {
                return new List<EfilingPacketDocumentInfo>();
            }

            return await TenantScope(_context.Documents)
                .AsNoTracking()
                .Where(d => d.MatterId == matterId && documentIds.Contains(d.Id))
                .Select(d => new EfilingPacketDocumentInfo
                {
                    Id = d.Id,
                    MatterId = d.MatterId,
                    Name = d.Name,
                    FileName = d.FileName,
                    FileSize = d.FileSize,
                    MimeType = d.MimeType,
                    Category = d.Category,
                    Tags = d.Tags
                })
                .ToListAsync(cancellationToken);
        }

        private async Task<HashSet<DateTime>> LoadCourtHolidayDatesAsync(string? jurisdictionCode, CancellationToken cancellationToken)
        {
            var query = TenantScope(_context.Holidays)
                .AsNoTracking()
                .Where(h => h.IsCourtHoliday);

            if (!string.IsNullOrWhiteSpace(jurisdictionCode))
            {
                var normalizedJurisdiction = jurisdictionCode.Trim().ToUpperInvariant();
                query = query.Where(h => h.Jurisdiction == null || h.Jurisdiction == normalizedJurisdiction);
            }

            var dates = await query
                .Select(h => h.Date.Date)
                .Distinct()
                .ToListAsync(cancellationToken);

            return dates.ToHashSet();
        }

        private static Dictionary<string, string> NormalizeMetadata(Dictionary<string, string>? metadata)
        {
            var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (metadata == null)
            {
                return normalized;
            }

            foreach (var pair in metadata)
            {
                var key = pair.Key?.Trim();
                if (string.IsNullOrWhiteSpace(key) || !AllowedMetadataKeys.Contains(key))
                {
                    continue;
                }

                var value = NormalizeOptionalText(pair.Value, 512);
                if (value == null)
                {
                    continue;
                }

                normalized[key] = value;
            }

            return normalized;
        }

        private static string? NormalizeOptionalText(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var trimmed = value.Trim();
            return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
        }

        private bool CanManageEfilingWorkflow()
        {
            return User.IsInRole("Admin") || User.IsInRole("Partner") || User.IsInRole("SecurityAdmin");
        }

        private static void AddTimeline(List<object> list, DateTime? ts, string eventType, string source, string title)
        {
            if (!ts.HasValue) return;
            list.Add(new Dictionary<string, object?>
            {
                ["timestampUtc"] = ts.Value,
                ["eventType"] = eventType,
                ["source"] = source,
                ["title"] = title
            });
        }

        private static DateTime GetTimelineTimestamp(object obj)
        {
            if (obj is IDictionary<string, object?> dict && dict.TryGetValue("timestampUtc", out var value) && value is DateTime dt)
            {
                return dt;
            }

            var prop = obj.GetType().GetProperty("timestampUtc");
            if (prop?.GetValue(obj) is DateTime propDt) return propDt;
            return DateTime.UtcNow;
        }

        private static string InferFilingType(IEnumerable<string> textParts)
        {
            var text = string.Join(" ", textParts).ToLowerInvariant();
            if (text.Contains("motion")) return "motion";
            if (text.Contains("complaint")) return "complaint";
            if (text.Contains("petition")) return "petition";
            if (text.Contains("notice")) return "notice";
            return "general_filing";
        }

        private static int ScoreDocumentForPacket(string? name, string? fileName, string? category, string? tags)
        {
            var text = $"{name} {fileName} {category} {tags}".ToLowerInvariant();
            var score = 0;
            if (text.Contains("motion") || text.Contains("complaint") || text.Contains("petition") || text.Contains("notice")) score += 5;
            if ((fileName ?? string.Empty).EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) score += 2;
            return score;
        }

        private static void RequireMetadata(IDictionary<string, string> metadata, string key, IList<object> errors, string message)
        {
            if (!metadata.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
            {
                errors.Add(new { code = $"metadata_{key}_missing", message });
            }
        }

        private void ApplyPartnerSpecificPrecheckRules(
            string? providerKey,
            Matter matter,
            IReadOnlyCollection<EfilingPacketDocumentInfo> documents,
            IDictionary<string, string> metadata,
            IList<object> errors,
            IList<object> warnings)
        {
            if (string.IsNullOrWhiteSpace(providerKey))
            {
                return;
            }

            var normalizedProviderKey = providerKey.Trim().ToLowerInvariant();
            var configPrefix = normalizedProviderKey switch
            {
                var p when p == IntegrationProviderKeys.OneLegalEfile => "Integrations:EfilingPartners:OneLegal",
                var p when p == IntegrationProviderKeys.FileAndServeXpressEfile => "Integrations:EfilingPartners:FileAndServeXpress",
                _ => null
            };

            if (string.IsNullOrWhiteSpace(configPrefix))
            {
                warnings.Add(new { code = "partner_validator_unavailable", message = $"No provider-specific validator profile found for '{providerKey}'." });
                return;
            }

            var supportedCourtTypes = _configuration.GetSection($"{configPrefix}:SupportedCourtTypes").Get<string[]>() ?? Array.Empty<string>();
            if (supportedCourtTypes.Length > 0 &&
                !string.IsNullOrWhiteSpace(matter.CourtType) &&
                !supportedCourtTypes.Contains(matter.CourtType, StringComparer.OrdinalIgnoreCase))
            {
                var strictCourtCoverage = _configuration.GetValue<bool?>($"{configPrefix}:StrictCourtCoverage") ?? false;
                var payload = new
                {
                    code = "unsupported_court_type",
                    message = $"{providerKey} court coverage does not list '{matter.CourtType}'.",
                    supportedCourtTypes
                };
                if (strictCourtCoverage) errors.Add(payload); else warnings.Add(payload);
            }

            var maxPacketDocuments = _configuration.GetValue<int?>($"{configPrefix}:MaxPacketDocuments");
            if (maxPacketDocuments.HasValue && documents.Count > Math.Max(1, maxPacketDocuments.Value))
            {
                errors.Add(new
                {
                    code = "too_many_documents",
                    message = $"Packet contains {documents.Count} documents; provider limit is {maxPacketDocuments.Value}."
                });
            }

            var requirePdfOnly = _configuration.GetValue<bool?>($"{configPrefix}:RequirePdfOnly") ?? false;
            if (requirePdfOnly)
            {
                foreach (var document in documents)
                {
                    if (!string.Equals(Path.GetExtension(document.FileName ?? string.Empty), ".pdf", StringComparison.OrdinalIgnoreCase))
                    {
                        errors.Add(new
                        {
                            code = "provider_pdf_only",
                            message = $"{providerKey} requires PDF-only packets. '{document.FileName}' is not PDF."
                        });
                    }
                }
            }

            var requiredMetadataKeys = _configuration.GetSection($"{configPrefix}:RequiredMetadataKeys").Get<string[]>() ?? Array.Empty<string>();
            foreach (var key in requiredMetadataKeys.Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k.Trim()))
            {
                if (!metadata.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
                {
                    errors.Add(new
                    {
                        code = $"provider_metadata_{key}_missing",
                        message = $"{providerKey} requires metadata field '{key}'."
                    });
                }
            }
        }

        private static string? ResolveJurisdictionCode(IDictionary<string, string> metadata, Matter matter)
        {
            if (metadata.TryGetValue("jurisdictionCode", out var code) && !string.IsNullOrWhiteSpace(code))
            {
                return code.Trim().ToUpperInvariant();
            }

            if (metadata.TryGetValue("jurisdiction", out var jurisdiction) && !string.IsNullOrWhiteSpace(jurisdiction))
            {
                return jurisdiction.Trim().ToUpperInvariant();
            }

            if (metadata.TryGetValue("state", out var state) && !string.IsNullOrWhiteSpace(state))
            {
                return $"US-{state.Trim().ToUpperInvariant()}";
            }

            if (!string.IsNullOrWhiteSpace(matter.CourtType) &&
                matter.CourtType.Contains("Federal", StringComparison.OrdinalIgnoreCase))
            {
                return "US-Federal";
            }

            return null;
        }

        private static void ApplyJurisdictionRulePackPrecheckRules(
            JurisdictionRulePack? rulePack,
            IReadOnlyCollection<EfilingPacketDocumentInfo> documents,
            IDictionary<string, string> metadata,
            IList<object> errors,
            IList<object> warnings)
        {
            if (rulePack == null)
            {
                return;
            }

            if (TryGetRulePackDocumentRules(rulePack.DocumentRulesJson, out var maxDocumentMb, out var requirePdfOnly, out var allowedExtensions))
            {
                if (maxDocumentMb.HasValue && maxDocumentMb.Value > 0)
                {
                    var maxBytes = maxDocumentMb.Value * 1024L * 1024L;
                    foreach (var document in documents)
                    {
                        if (document.FileSize > maxBytes)
                        {
                            errors.Add(new
                            {
                                code = "jurisdiction_document_too_large",
                                message = $"Rule pack '{rulePack.Name}' limits documents to {maxDocumentMb.Value}MB. '{document.FileName}' exceeds the limit."
                            });
                        }
                    }
                }

                if (allowedExtensions.Count > 0)
                {
                    foreach (var document in documents)
                    {
                        var ext = Path.GetExtension(document.FileName ?? string.Empty);
                        if (string.IsNullOrWhiteSpace(ext))
                        {
                            continue;
                        }

                        if (!allowedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
                        {
                            errors.Add(new
                            {
                                code = "jurisdiction_extension_not_allowed",
                                message = $"Rule pack '{rulePack.Name}' does not allow extension '{ext}' for '{document.FileName}'."
                            });
                        }
                    }
                }

                if (requirePdfOnly == true)
                {
                    foreach (var document in documents)
                    {
                        if (!string.Equals(Path.GetExtension(document.FileName ?? string.Empty), ".pdf", StringComparison.OrdinalIgnoreCase))
                        {
                            warnings.Add(new
                            {
                                code = "jurisdiction_pdf_only_warning",
                                message = $"Rule pack '{rulePack.Name}' indicates PDF-only filing. '{document.FileName}' is not PDF."
                            });
                        }
                    }
                }
            }

            if (TryGetRulePackRequiredMetadataKeys(rulePack.ValidationRulesJson, out var requiredMetadataKeys))
            {
                foreach (var key in requiredMetadataKeys)
                {
                    if (!metadata.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
                    {
                        errors.Add(new
                        {
                            code = $"jurisdiction_metadata_{key}_missing",
                            message = $"Rule pack '{rulePack.Name}' requires metadata field '{key}'."
                        });
                    }
                }
            }
        }

        private static bool TryGetRulePackDocumentRules(
            string? json,
            out int? maxDocumentMb,
            out bool? requirePdfOnly,
            out HashSet<string> allowedExtensions)
        {
            maxDocumentMb = null;
            requirePdfOnly = null;
            allowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }

                if (doc.RootElement.TryGetProperty("maxDocumentMb", out var maxProp))
                {
                    if (maxProp.ValueKind == JsonValueKind.Number && maxProp.TryGetInt32(out var parsed))
                    {
                        maxDocumentMb = parsed;
                    }
                    else if (maxProp.ValueKind == JsonValueKind.String && int.TryParse(maxProp.GetString(), out var parsedStr))
                    {
                        maxDocumentMb = parsedStr;
                    }
                }

                if (doc.RootElement.TryGetProperty("requirePdfOnly", out var pdfProp))
                {
                    if (pdfProp.ValueKind == JsonValueKind.True) requirePdfOnly = true;
                    if (pdfProp.ValueKind == JsonValueKind.False) requirePdfOnly = false;
                    if (pdfProp.ValueKind == JsonValueKind.String && bool.TryParse(pdfProp.GetString(), out var parsedBool))
                    {
                        requirePdfOnly = parsedBool;
                    }
                }

                if (doc.RootElement.TryGetProperty("allowedExtensions", out var extProp) && extProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in extProp.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.String) continue;
                        var raw = item.GetString();
                        if (string.IsNullOrWhiteSpace(raw)) continue;
                        var normalized = raw.StartsWith('.') ? raw : $".{raw}";
                        allowedExtensions.Add(normalized.Trim());
                    }
                }

                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static bool TryGetRulePackRequiredMetadataKeys(string? json, out List<string> keys)
        {
            keys = new List<string>();
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }

                if (!doc.RootElement.TryGetProperty("requiredMetadataKeys", out var keysNode) || keysNode.ValueKind != JsonValueKind.Array)
                {
                    return false;
                }

                foreach (var item in keysNode.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String) continue;
                    var value = item.GetString();
                    if (string.IsNullOrWhiteSpace(value)) continue;
                    keys.Add(value.Trim());
                }

                return keys.Count > 0;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private async Task TryTriggerOutcomeFeePlannerAsync(EfilingSubmission submission, string triggerType, CancellationToken ct)
        {
            var userId = GetUserId();
            if (submission == null ||
                string.IsNullOrWhiteSpace(userId) ||
                (string.IsNullOrWhiteSpace(submission.MatterId) && string.IsNullOrWhiteSpace(submission.Id)))
            {
                return;
            }

            try
            {
                await _outcomeFeePlanner.TryProcessTriggerAsync(new OutcomeFeePlanTriggerRequest
                {
                    MatterId = submission.MatterId,
                    TriggerType = triggerType,
                    TriggerEntityType = nameof(EfilingSubmission),
                    TriggerEntityId = submission.Id,
                    SourceStatus = submission.Status
                }, userId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Outcome-to-Fee planner trigger failed for e-filing submission {SubmissionId}", submission.Id);
            }

            try
            {
                await _clientTransparencyService.TryProcessTriggerAsync(new ClientTransparencyTriggerRequest
                {
                    MatterId = submission.MatterId,
                    TriggerType = triggerType,
                    TriggerEntityType = nameof(EfilingSubmission),
                    TriggerEntityId = submission.Id
                }, userId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Client transparency trigger failed for e-filing submission {SubmissionId}", submission.Id);
            }
        }

        private string? GetUserId()
        {
            return User.FindFirst("sub")?.Value
                ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        }

        private static bool TryNormalizeSubmissionStatus(string? value, out string normalized)
        {
            normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
            normalized = normalized switch
            {
                "draft" => "draft",
                "pending" => "pending",
                "processing" => "processing",
                "submitted" => "submitted",
                "accepted" => "accepted",
                "rejected" => "rejected",
                "corrected" => "corrected",
                "failed" => "failed",
                _ => string.Empty
            };

            return AllowedSubmissionStatuses.Contains(normalized);
        }

        private static bool IsAllowedTransition(string current, string target)
        {
            if (current == target) return true;
            return current switch
            {
                "pending" => target is "draft" or "submitted" or "processing",
                "draft" => target == "submitted",
                "processing" => target is "submitted" or "accepted" or "rejected" or "failed",
                "submitted" => target is "processing" or "accepted" or "rejected" or "failed",
                "rejected" => target == "corrected",
                "failed" => target is "corrected" or "submitted",
                "corrected" => target is "submitted" or "processing",
                "accepted" => false,
                _ => false
            };
        }

        private static DateTime CalculateDeadline(DateTime triggerDateUtc, CourtRule rule, HashSet<DateTime> holidayDates)
        {
            var current = triggerDateUtc.Date;
            var ruleDays = Math.Max(0, rule.DaysCount);
            var serviceDays = Math.Max(0, rule.ServiceDaysAdd);
            var before = string.Equals(rule.Direction, "Before", StringComparison.OrdinalIgnoreCase);
            if (string.Equals(rule.DayType, "Court", StringComparison.OrdinalIgnoreCase))
            {
                var remaining = ruleDays;
                while (remaining > 0)
                {
                    current = current.AddDays(before ? -1 : 1);
                    if (!IsBusinessDay(current, holidayDates)) continue;
                    remaining--;
                }
            }
            else
            {
                current = current.AddDays(before ? -ruleDays : ruleDays);
            }

            if (serviceDays > 0)
            {
                current = current.AddDays(before ? -serviceDays : serviceDays);
            }

            if (rule.ExtendIfWeekend)
            {
                while (!IsBusinessDay(current, holidayDates))
                {
                    current = current.AddDays(before ? -1 : 1);
                }
            }

            return DateTime.SpecifyKind(current, DateTimeKind.Utc);
        }

        private static bool IsBusinessDay(DateTime date, HashSet<DateTime> holidayDates)
        {
            if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                return false;
            }

            return !holidayDates.Contains(date.Date);
        }
    }

    public sealed class EfilingPrecheckRequest
    {
        public string MatterId { get; set; } = string.Empty;
        public string? ProviderKey { get; set; }
        public string? PacketName { get; set; }
        public string? FilingType { get; set; }
        public string? CourtType { get; set; }
        public DateTime? TriggerDateUtc { get; set; }
        public List<string>? DocumentIds { get; set; }
        public Dictionary<string, string>? Metadata { get; set; }
    }

    public sealed class EfilingTransitionRequest
    {
        public string TargetStatus { get; set; } = string.Empty;
        public string? RejectionReason { get; set; }
    }

    public sealed class EfilingPartnerSubmitApiRequest
    {
        public string ProviderKey { get; set; } = string.Empty;
        public string? ConnectionId { get; set; }
        public string MatterId { get; set; } = string.Empty;
        public string? ExistingSubmissionId { get; set; }
        public string? PacketName { get; set; }
        public string? FilingType { get; set; }
        public List<string>? DocumentIds { get; set; }
        public Dictionary<string, string>? Metadata { get; set; }
    }

    public sealed class EfilingRepairRequest
    {
        public string? Notes { get; set; }
    }

    public sealed class EfilingDocketAutomationApiRequest
    {
        public string? ConnectionId { get; set; }
        public string? ProviderKey { get; set; }
        public string? MatterId { get; set; }
        public int? Limit { get; set; }
        public List<string>? DocketEntryIds { get; set; }
    }

    public sealed class EfilingPacketDocumentInfo
    {
        public string Id { get; set; } = string.Empty;
        public string? MatterId { get; set; }
        public string? Name { get; set; }
        public string? FileName { get; set; }
        public long FileSize { get; set; }
        public string? MimeType { get; set; }
        public string? Category { get; set; }
        public string? Tags { get; set; }
    }
}
