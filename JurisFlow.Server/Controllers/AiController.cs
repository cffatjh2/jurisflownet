using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using JurisFlow.Server.Services;
using System.Text.Json;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Data;
using System.Security.Claims;
using System.Globalization;

namespace JurisFlow.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Policy = "StaffOnly")]
    public partial class AiController : ControllerBase
    {
        private readonly JurisFlowDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<AiController> _logger;
        private readonly JurisdictionRulesPlatformService _jurisdictionRulesPlatformService;
        private readonly AuditLogger _auditLogger;
        private readonly TenantContext _tenantContext;

        public AiController(
            JurisFlowDbContext context,
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory,
            ILogger<AiController> logger,
            JurisdictionRulesPlatformService jurisdictionRulesPlatformService,
            AuditLogger auditLogger,
            TenantContext tenantContext)
        {
            _context = context;
            _configuration = configuration;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _jurisdictionRulesPlatformService = jurisdictionRulesPlatformService;
            _auditLogger = auditLogger;
            _tenantContext = tenantContext;
        }

        [HttpPost("chat")]
        public async Task<ActionResult<object>> Chat([FromBody] AiChatRequestDto dto, CancellationToken cancellationToken)
        {
            if (dto == null)
            {
                return BadRequest(new { message = "Request body is required." });
            }

            var aiPolicy = ResolveAiPromptPolicy();
            var aiPolicyError = ValidateAiOperationPolicy(aiPolicy, "chat");
            if (aiPolicyError != null)
            {
                return aiPolicyError;
            }

            var sanitizedMessage = SanitizeForAi(dto.Message, aiPolicy, aiPolicy.ResearchQueryMaxChars);
            if (string.IsNullOrWhiteSpace(sanitizedMessage))
            {
                return BadRequest(new { message = "Message is required." });
            }

            var contextLimit = Math.Max(aiPolicy.ContractContentMaxChars, 6000);
            var sanitizedContext = SanitizeForAi(dto.ContextData, aiPolicy, contextLimit);
            var historyEntries = (dto.History ?? new List<AiChatMessageDto>())
                .Where(entry => entry != null)
                .Select(entry => new
                {
                    Role = string.Equals(entry.Role, "model", StringComparison.OrdinalIgnoreCase) ? "Assistant" : "User",
                    Text = SanitizeForAi(
                        string.Join(
                            "\n",
                            (entry.Parts ?? new List<AiChatPartDto>())
                                .Select(part => part?.Text?.Trim())
                                .Where(text => !string.IsNullOrWhiteSpace(text))
                        ),
                        aiPolicy,
                        1200)
                })
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Text))
                .TakeLast(12)
                .ToList();

            var prompt = new StringBuilder();
            prompt.AppendLine("You are Juris, an AI legal associate for a US law firm.");
            prompt.AppendLine("Respond professionally, precisely, and concisely.");
            prompt.AppendLine("Do not invent citations, facts, filings, or procedural history.");
            if (dto.EnableSearch)
            {
                prompt.AppendLine("If live research is requested but unavailable, say so plainly instead of fabricating sources.");
            }

            if (!string.IsNullOrWhiteSpace(sanitizedContext))
            {
                prompt.AppendLine();
                prompt.AppendLine("Document context:");
                prompt.AppendLine(sanitizedContext);
            }

            if (historyEntries.Count > 0)
            {
                prompt.AppendLine();
                prompt.AppendLine("Conversation so far:");
                foreach (var entry in historyEntries)
                {
                    prompt.Append(entry.Role);
                    prompt.Append(": ");
                    prompt.AppendLine(entry.Text);
                }
            }

            prompt.AppendLine();
            prompt.AppendLine("Latest user message:");
            prompt.AppendLine(sanitizedMessage);

            var generated = await GenerateGeminiTextAsync(prompt.ToString(), cancellationToken);
            if (string.IsNullOrWhiteSpace(generated))
            {
                return Ok(new
                {
                    text = GenerateDegradedChatResponse(sanitizedMessage, sanitizedContext, dto.EnableSearch),
                    sources = Array.Empty<object>(),
                    providerStatus = "degraded"
                });
            }

            return Ok(new
            {
                text = generated,
                sources = Array.Empty<object>(),
                providerStatus = "live"
            });
        }

        // ========== LEGAL RESEARCH ==========

        // POST: api/ai/research
        [HttpPost("research")]
        public async System.Threading.Tasks.Task<ActionResult<ResearchSession>> StartResearch([FromBody] ResearchRequestDto dto)
        {
            if (dto == null)
            {
                return BadRequest(new { message = "Request body is required." });
            }

            var queryText = dto.Query?.Trim();
            if (string.IsNullOrWhiteSpace(queryText))
            {
                return BadRequest(new { message = "Query is required." });
            }

            var userId = GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Unauthorized(new { message = "Authenticated user id is required." });
            }

            string? matterId = null;
            if (!string.IsNullOrWhiteSpace(dto.MatterId))
            {
                var matter = await GetMatterByIdAsync(dto.MatterId.Trim());
                if (matter == null)
                {
                    return NotFound(new { message = "Matter not found for this tenant." });
                }
                matterId = matter.Id;
            }

            var session = new ResearchSession
            {
                UserId = userId,
                MatterId = matterId,
                Title = string.IsNullOrWhiteSpace(dto.Title) ? $"Research: {queryText.Substring(0, Math.Min(50, queryText.Length))}" : dto.Title.Trim(),
                Query = queryText,
                Jurisdiction = string.IsNullOrWhiteSpace(dto.Jurisdiction) ? null : dto.Jurisdiction.Trim(),
                PracticeArea = string.IsNullOrWhiteSpace(dto.PracticeArea) ? null : dto.PracticeArea.Trim(),
                Status = "Processing"
            };

            _context.ResearchSessions.Add(session);
            await _context.SaveChangesAsync();

            // Process with Gemini AI
            var stopwatch = Stopwatch.StartNew();
            var aiPolicy = ResolveAiPromptPolicy();
            var aiPolicyError = ValidateAiOperationPolicy(aiPolicy, "research");
            if (aiPolicyError != null)
            {
                session.Status = "Failed";
                session.ErrorMessage = "AI policy blocked request.";
                session.CompletedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                return aiPolicyError;
            }
            try
            {
                var result = await ProcessLegalResearchWithGemini(session, aiPolicy);
                session.Response = result.Response;
                session.CitationsJson = JsonSerializer.Serialize(result.Citations);
                session.KeyPointsJson = JsonSerializer.Serialize(result.KeyPoints);
                session.RelatedCasesJson = JsonSerializer.Serialize(result.RelatedCases);
                session.Status = "Completed";
                session.CompletedAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                session.Status = "Failed";
                session.ErrorMessage = ex.Message;
            }
            finally
            {
                stopwatch.Stop();
                session.ProcessingTimeMs = (int)stopwatch.ElapsedMilliseconds;
            }

            await _context.SaveChangesAsync();
            await _auditLogger.LogAsync(HttpContext, "ai.research.start", nameof(ResearchSession), session.Id, $"MatterId={session.MatterId ?? "none"}");
            return Ok(session);
        }

        // GET: api/ai/research
        [HttpGet("research")]
        public async System.Threading.Tasks.Task<ActionResult<IEnumerable<ResearchSession>>> GetResearchHistory(
            [FromQuery] string? matterId = null,
            [FromQuery] int limit = 20)
        {
            var userId = GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Unauthorized(new { message = "Authenticated user id is required." });
            }

            var normalizedLimit = Math.Clamp(limit, 1, 200);
            var query = TenantScope(_context.ResearchSessions)
                .AsNoTracking()
                .Where(r => r.UserId == userId);

            if (!string.IsNullOrEmpty(matterId))
            {
                var normalizedMatterId = matterId.Trim();
                query = query.Where(r => r.MatterId == normalizedMatterId);
            }

            var sessions = await query
                .OrderByDescending(r => r.CreatedAt)
                .Take(normalizedLimit)
                .Select(r => new
                {
                    r.Id,
                    r.Title,
                    r.Query,
                    r.Status,
                    r.Jurisdiction,
                    r.PracticeArea,
                    r.ProcessingTimeMs,
                    r.CreatedAt
                })
                .ToListAsync();

            return Ok(sessions);
        }

        // GET: api/ai/research/{id}
        [HttpGet("research/{id}")]
        public async System.Threading.Tasks.Task<ActionResult<ResearchSession>> GetResearch(string id)
        {
            var userId = GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Unauthorized(new { message = "Authenticated user id is required." });
            }

            var session = await TenantScope(_context.ResearchSessions)
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId);
            if (session == null)
            {
                return NotFound();
            }

            await _auditLogger.LogAsync(HttpContext, "ai.research.read", nameof(ResearchSession), session.Id, null);
            return Ok(session);
        }

        // ========== CONTRACT ANALYSIS ==========

        // POST: api/ai/analyze-contract
        [HttpPost("analyze-contract")]
        public async System.Threading.Tasks.Task<ActionResult<ContractAnalysis>> AnalyzeContract([FromBody] ContractAnalysisDto dto)
        {
            if (dto == null)
            {
                return BadRequest(new { message = "Request body is required." });
            }

            if (string.IsNullOrWhiteSpace(dto.DocumentContent))
            {
                return BadRequest(new { message = "DocumentContent is required." });
            }

            var userId = GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Unauthorized(new { message = "Authenticated user id is required." });
            }

            var normalizedDocumentId = string.IsNullOrWhiteSpace(dto.DocumentId) ? null : dto.DocumentId.Trim();
            if (!string.IsNullOrWhiteSpace(normalizedDocumentId))
            {
                var documentExists = await TenantScope(_context.Documents).AsNoTracking().AnyAsync(d => d.Id == normalizedDocumentId);
                if (!documentExists)
                {
                    return NotFound(new { message = "Document not found for this tenant." });
                }
            }

            string? matterId = null;
            if (!string.IsNullOrWhiteSpace(dto.MatterId))
            {
                var matter = await GetMatterByIdAsync(dto.MatterId.Trim());
                if (matter == null)
                {
                    return NotFound(new { message = "Matter not found for this tenant." });
                }
                matterId = matter.Id;
            }

            var analysis = new ContractAnalysis
            {
                DocumentId = normalizedDocumentId ?? string.Empty,
                UserId = userId,
                MatterId = matterId,
                ContractType = string.IsNullOrWhiteSpace(dto.ContractType) ? "Unknown" : dto.ContractType.Trim(),
                Status = "Processing"
            };

            _context.ContractAnalyses.Add(analysis);
            await _context.SaveChangesAsync();

            var aiPolicy = ResolveAiPromptPolicy();
            var aiPolicyError = ValidateAiOperationPolicy(aiPolicy, "contract_analysis");
            if (aiPolicyError != null)
            {
                analysis.Status = "Failed";
                analysis.ErrorMessage = "AI policy blocked request.";
                analysis.CompletedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                return aiPolicyError;
            }

            try
            {
                var result = await AnalyzeContractWithGemini(dto.DocumentContent, dto.ContractType, aiPolicy);
                
                analysis.Summary = result.Summary;
                analysis.KeyTermsJson = JsonSerializer.Serialize(result.KeyTerms);
                analysis.KeyDatesJson = JsonSerializer.Serialize(result.KeyDates);
                analysis.PartiesJson = JsonSerializer.Serialize(result.Parties);
                analysis.RisksJson = JsonSerializer.Serialize(result.Risks);
                analysis.RiskScore = result.RiskScore;
                analysis.UnusualClausesJson = JsonSerializer.Serialize(result.UnusualClauses);
                analysis.RecommendationsJson = JsonSerializer.Serialize(result.Recommendations);
                analysis.ContractType = result.DetectedType ?? analysis.ContractType;
                analysis.Status = "Completed";
                analysis.CompletedAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                analysis.Status = "Failed";
                analysis.ErrorMessage = ex.Message;
            }

            await _context.SaveChangesAsync();
            await _auditLogger.LogAsync(HttpContext, "ai.contract.analyze", nameof(ContractAnalysis), analysis.Id, $"DocumentId={analysis.DocumentId}");
            return Ok(analysis);
        }

        // GET: api/ai/contract-analyses
        [HttpGet("contract-analyses")]
        public async System.Threading.Tasks.Task<ActionResult<IEnumerable<ContractAnalysis>>> GetContractAnalyses(
            [FromQuery] string? documentId = null,
            [FromQuery] string? matterId = null,
            [FromQuery] int limit = 20)
        {
            var userId = GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Unauthorized(new { message = "Authenticated user id is required." });
            }

            var normalizedLimit = Math.Clamp(limit, 1, 200);
            var query = TenantScope(_context.ContractAnalyses)
                .AsNoTracking()
                .Where(c => c.UserId == userId)
                .AsQueryable();

            if (!string.IsNullOrEmpty(documentId))
            {
                query = query.Where(c => c.DocumentId == documentId.Trim());
            }

            if (!string.IsNullOrEmpty(matterId))
            {
                query = query.Where(c => c.MatterId == matterId.Trim());
            }

            var analyses = await query
                .OrderByDescending(c => c.CreatedAt)
                .Take(normalizedLimit)
                .ToListAsync();

            return Ok(analyses);
        }

        // GET: api/ai/contract-analyses/{id}
        [HttpGet("contract-analyses/{id}")]
        public async System.Threading.Tasks.Task<ActionResult<ContractAnalysis>> GetContractAnalysis(string id)
        {
            var userId = GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Unauthorized(new { message = "Authenticated user id is required." });
            }

            var analysis = await TenantScope(_context.ContractAnalyses)
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);
            if (analysis == null)
            {
                return NotFound();
            }

            return Ok(analysis);
        }

        // ========== CASE PREDICTION ==========

        // POST: api/ai/predict-case
        [HttpPost("predict-case")]
        public async System.Threading.Tasks.Task<ActionResult<CasePrediction>> PredictCase([FromBody] CasePredictionDto dto)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.MatterId))
            {
                return BadRequest(new { message = "MatterId is required." });
            }

            var userId = GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Unauthorized(new { message = "Authenticated user id is required." });
            }

            // Get matter details
            var matter = await GetMatterByIdAsync(dto.MatterId.Trim());
            if (matter == null)
            {
                return NotFound(new { message = "Matter not found for this tenant." });
            }

            var prediction = new CasePrediction
            {
                MatterId = matter.Id,
                UserId = userId,
                Status = "Processing"
            };

            _context.CasePredictions.Add(prediction);
            await _context.SaveChangesAsync();

            var aiPolicy = ResolveAiPromptPolicy();
            var aiPolicyError = ValidateAiOperationPolicy(aiPolicy, "case_prediction");
            if (aiPolicyError != null)
            {
                prediction.Status = "Failed";
                prediction.ErrorMessage = "AI policy blocked request.";
                prediction.CompletedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                return aiPolicyError;
            }

            try
            {
                var result = await PredictCaseWithGemini(matter, dto.AdditionalContext, aiPolicy);
                
                prediction.PredictedOutcome = result.Outcome;
                prediction.Confidence = result.Confidence;
                prediction.FactorsJson = JsonSerializer.Serialize(result.Factors);
                prediction.SimilarCasesJson = JsonSerializer.Serialize(result.SimilarCases);
                prediction.SettlementMin = result.SettlementMin;
                prediction.SettlementMax = result.SettlementMax;
                prediction.EstimatedTimeline = result.Timeline;
                prediction.RecommendationsJson = JsonSerializer.Serialize(result.Recommendations);
                prediction.Status = "Completed";
                prediction.CompletedAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                prediction.Status = "Failed";
                prediction.ErrorMessage = ex.Message;
            }

            await _context.SaveChangesAsync();
            await _auditLogger.LogAsync(HttpContext, "ai.case.predict", nameof(CasePrediction), prediction.Id, $"MatterId={prediction.MatterId}");
            return Ok(prediction);
        }

        // GET: api/ai/predictions/{matterId}
        [HttpGet("predictions/{matterId}")]
        public async System.Threading.Tasks.Task<ActionResult<IEnumerable<CasePrediction>>> GetPredictions(string matterId)
        {
            var userId = GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Unauthorized(new { message = "Authenticated user id is required." });
            }

            var normalizedMatterId = matterId.Trim();
            var matter = await GetMatterByIdAsync(normalizedMatterId);
            if (matter == null)
            {
                return NotFound(new { message = "Matter not found for this tenant." });
            }

            var predictions = await TenantScope(_context.CasePredictions)
                .AsNoTracking()
                .Where(p => p.MatterId == normalizedMatterId && p.UserId == userId)
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync();

            return Ok(predictions);
        }

        // ========== EVIDENCE-LINKED AI DRAFTING (FOUNDATION) ==========

        // POST: api/ai/drafts/evidence-linked
        [HttpPost("drafts/evidence-linked")]
        public async System.Threading.Tasks.Task<ActionResult> CreateEvidenceLinkedDraft([FromBody] EvidenceLinkedDraftCreateDto dto)
        {
            if (dto == null)
            {
                return BadRequest(new { message = "Request body is required." });
            }

            if (string.IsNullOrWhiteSpace(dto.RenderedText))
            {
                return BadRequest(new { message = "RenderedText is required." });
            }

            var userId = GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Unauthorized(new { message = "Authenticated user id is required." });
            }

            var normalizedMatterId = string.IsNullOrWhiteSpace(dto.MatterId) ? null : dto.MatterId.Trim();
            if (!string.IsNullOrWhiteSpace(normalizedMatterId))
            {
                var matter = await GetMatterByIdAsync(normalizedMatterId);
                if (matter == null)
                {
                    return NotFound(new { message = "Matter not found for this tenant." });
                }
            }

            var now = DateTime.UtcNow;
            var session = new AiDraftSession
            {
                MatterId = normalizedMatterId,
                UserId = userId,
                Title = string.IsNullOrWhiteSpace(dto.Title) ? "Evidence-Linked AI Draft" : dto.Title.Trim(),
                Purpose = string.IsNullOrWhiteSpace(dto.Purpose) ? "other" : dto.Purpose.Trim(),
                Status = "generated",
                JurisdictionContextJson = CoerceJson(dto.JurisdictionContextJson, dto.JurisdictionContext),
                MetadataJson = CoerceJson(dto.SessionMetadataJson, dto.SessionMetadata),
                CreatedAt = now,
                UpdatedAt = now
            };

            var output = new AiDraftOutput
            {
                SessionId = session.Id,
                Status = string.IsNullOrWhiteSpace(dto.OutputStatus) ? "generated" : dto.OutputStatus.Trim(),
                RenderedText = dto.RenderedText,
                Model = dto.Model?.Trim(),
                PromptTemplateVersion = dto.PromptTemplateVersion?.Trim(),
                RetrievalBundleId = dto.RetrievalBundleId?.Trim(),
                CorrelationId = dto.CorrelationId?.Trim(),
                RetrievalBundleJson = CoerceJson(dto.RetrievalBundleJson, dto.RetrievalBundle),
                StructuredClaimsJson = CoerceJson(dto.StructuredClaimsJson, dto.StructuredClaims),
                MetadataJson = CoerceJson(dto.OutputMetadataJson, dto.OutputMetadata),
                GeneratedAt = dto.GeneratedAtUtc ?? now,
                CreatedAt = now,
                UpdatedAt = now
            };

            await using var tx = await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            _context.AiDraftSessions.Add(session);
            _context.AiDraftOutputs.Add(output);

            var claims = new List<AiDraftClaim>();
            var evidenceLinks = new List<AiDraftEvidenceLink>();
            var ruleCitations = new List<AiDraftRuleCitation>();

            var claimRows = dto.Claims ?? new List<EvidenceLinkedDraftClaimDto>();
            for (var i = 0; i < claimRows.Count; i++)
            {
                var row = claimRows[i];
                if (row == null || string.IsNullOrWhiteSpace(row.ClaimText))
                {
                    continue;
                }

                var claim = new AiDraftClaim
                {
                    DraftOutputId = output.Id,
                    OrderIndex = row.OrderIndex ?? i,
                    ClaimText = row.ClaimText,
                    ClaimType = row.ClaimType?.Trim(),
                    IsCritical = row.IsCritical,
                    Confidence = row.Confidence,
                    Status = string.IsNullOrWhiteSpace(row.Status) ? "needs_review" : row.Status.Trim(),
                    SupportSummary = row.SupportSummary?.Trim(),
                    MetadataJson = CoerceJson(row.MetadataJson, row.Metadata),
                    CreatedAt = now,
                    UpdatedAt = now
                };
                claims.Add(claim);

                foreach (var evidence in row.EvidenceLinks ?? Enumerable.Empty<EvidenceLinkedDraftEvidenceLinkDto>())
                {
                    if (evidence == null)
                    {
                        continue;
                    }

                    evidenceLinks.Add(new AiDraftEvidenceLink
                    {
                        ClaimId = claim.Id,
                        DocumentId = evidence.DocumentId?.Trim(),
                        DocumentVersionId = evidence.DocumentVersionId?.Trim(),
                        Sha256 = evidence.Sha256?.Trim(),
                        Page = evidence.Page,
                        ParagraphId = evidence.ParagraphId?.Trim(),
                        CharStart = evidence.CharStart,
                        CharEnd = evidence.CharEnd,
                        Excerpt = evidence.Excerpt,
                        SupportStrength = evidence.SupportStrength?.Trim(),
                        WhySupports = evidence.WhySupports?.Trim(),
                        MetadataJson = CoerceJson(evidence.MetadataJson, evidence.Metadata),
                        CreatedAt = now,
                        UpdatedAt = now
                    });
                }

                foreach (var citation in row.RuleCitations ?? Enumerable.Empty<EvidenceLinkedDraftRuleCitationDto>())
                {
                    if (citation == null)
                    {
                        continue;
                    }

                    ruleCitations.Add(new AiDraftRuleCitation
                    {
                        ClaimId = claim.Id,
                        JurisdictionRulePackId = citation.JurisdictionRulePackId?.Trim(),
                        RulePackVersion = citation.RulePackVersion,
                        RuleCode = citation.RuleCode?.Trim(),
                        SourceCitation = citation.SourceCitation,
                        CitationText = citation.CitationText,
                        EffectiveAt = citation.EffectiveAtUtc,
                        Confidence = citation.Confidence,
                        MetadataJson = CoerceJson(citation.MetadataJson, citation.Metadata),
                        CreatedAt = now,
                        UpdatedAt = now
                    });
                }
            }

            var verificationRuns = new List<AiDraftVerificationRun>();
            foreach (var verification in dto.VerificationRuns ?? Enumerable.Empty<EvidenceLinkedDraftVerificationRunDto>())
            {
                if (verification == null)
                {
                    continue;
                }

                verificationRuns.Add(new AiDraftVerificationRun
                {
                    DraftOutputId = output.Id,
                    VerifierVersion = verification.VerifierVersion?.Trim(),
                    Status = string.IsNullOrWhiteSpace(verification.Status) ? "completed" : verification.Status.Trim(),
                    CorrelationId = verification.CorrelationId?.Trim() ?? output.CorrelationId,
                    ResultJson = CoerceJson(verification.ResultJson, verification.Result),
                    MetadataJson = CoerceJson(verification.MetadataJson, verification.Metadata),
                    CreatedAt = verification.CreatedAtUtc ?? now
                });
            }

            if (claims.Count > 0) _context.AiDraftClaims.AddRange(claims);
            if (evidenceLinks.Count > 0) _context.AiDraftEvidenceLinks.AddRange(evidenceLinks);
            if (ruleCitations.Count > 0) _context.AiDraftRuleCitations.AddRange(ruleCitations);
            if (verificationRuns.Count > 0) _context.AiDraftVerificationRuns.AddRange(verificationRuns);

            await _context.SaveChangesAsync();
            await tx.CommitAsync();

            await _auditLogger.LogAsync(HttpContext, "ai.draft.create", nameof(AiDraftOutput), output.Id, $"MatterId={session.MatterId ?? "none"}");

            var graph = await LoadEvidenceLinkedDraftGraphAsync(output.Id);
            return CreatedAtAction(nameof(GetEvidenceLinkedDraft), new { id = output.Id }, graph);
        }

        // GET: api/ai/drafts/{id}
        [HttpGet("drafts/{id}")]
        public async System.Threading.Tasks.Task<ActionResult> GetEvidenceLinkedDraft(string id)
        {
            if (string.IsNullOrWhiteSpace(GetCurrentUserId()))
            {
                return Unauthorized(new { message = "Authenticated user id is required." });
            }

            var graph = await LoadEvidenceLinkedDraftGraphAsync(id);
            if (graph == null)
            {
                return NotFound();
            }

            await _auditLogger.LogAsync(HttpContext, "ai.draft.read", nameof(AiDraftOutput), id, null);
            return Ok(graph);
        }

        // ========== AI HELPERS ==========

        private async Task<object?> LoadEvidenceLinkedDraftGraphAsync(string outputId)
        {
            var access = await GetOwnedDraftOutputAccessAsync(outputId, asNoTracking: true);
            if (access == null)
            {
                return null;
            }

            var output = access.Output;
            var session = access.Session;
            var claims = await TenantScope(_context.AiDraftClaims).AsNoTracking()
                .Where(c => c.DraftOutputId == output.Id)
                .OrderBy(c => c.OrderIndex)
                .ThenBy(c => c.CreatedAt)
                .ToListAsync();

            var claimIds = claims.Select(c => c.Id).ToList();
            var evidenceLinks = claimIds.Count == 0
                ? new List<AiDraftEvidenceLink>()
                : await TenantScope(_context.AiDraftEvidenceLinks).AsNoTracking()
                    .Where(e => claimIds.Contains(e.ClaimId))
                    .OrderBy(e => e.CreatedAt)
                    .ToListAsync();

            var ruleCitations = claimIds.Count == 0
                ? new List<AiDraftRuleCitation>()
                : await TenantScope(_context.AiDraftRuleCitations).AsNoTracking()
                    .Where(c => claimIds.Contains(c.ClaimId))
                    .OrderBy(c => c.CreatedAt)
                    .ToListAsync();

            var verificationRuns = await TenantScope(_context.AiDraftVerificationRuns).AsNoTracking()
                .Where(v => v.DraftOutputId == output.Id)
                .OrderByDescending(v => v.CreatedAt)
                .ToListAsync();

            var evidenceByClaim = evidenceLinks.ToLookup(e => e.ClaimId, StringComparer.Ordinal);
            var citationsByClaim = ruleCitations.ToLookup(c => c.ClaimId, StringComparer.Ordinal);
            var claimGraph = claims.Select(claim => new
            {
                claim,
                evidenceLinks = evidenceByClaim[claim.Id].ToList(),
                ruleCitations = citationsByClaim[claim.Id].ToList()
            }).ToList();

            return new
            {
                session,
                output,
                claims = claimGraph,
                verificationRuns,
                summary = new
                {
                    claimCount = claims.Count,
                    criticalClaimCount = claims.Count(c => c.IsCritical),
                    evidenceLinkCount = evidenceLinks.Count,
                    ruleCitationCount = ruleCitations.Count,
                    unsupportedCriticalClaims = claims.Count(c => c.IsCritical && string.Equals(c.Status, "unsupported", StringComparison.OrdinalIgnoreCase))
                }
            };
        }

        private static string? CoerceJson(string? rawJson, object? payload)
        {
            if (!string.IsNullOrWhiteSpace(rawJson))
            {
                try
                {
                    using var _ = JsonDocument.Parse(rawJson);
                    return rawJson;
                }
                catch
                {
                    return null;
                }
            }

            if (payload == null)
            {
                return null;
            }

            try
            {
                return JsonSerializer.Serialize(payload);
            }
            catch
            {
                return null;
            }
        }

        private async System.Threading.Tasks.Task<LegalResearchResult> ProcessLegalResearchWithGemini(ResearchSession session, AiPromptPolicy aiPolicy)
        {
            var prompt = BuildResearchPrompt(session, aiPolicy);
            var generated = await GenerateGeminiTextAsync(
                prompt,
                HttpContext.RequestAborted,
                aiPolicy.StructuredJsonEnabled,
                aiPolicy.GeminiFunctionCallingEnabled ? BuildResearchFunctionSpec() : null);
            if (!string.IsNullOrWhiteSpace(generated))
            {
                if (TryParseResearchJsonResult(generated, out var structuredResearch))
                {
                    return structuredResearch!;
                }

                var extractedCitations = generated
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Where(line => line.Contains(" v. ", StringComparison.OrdinalIgnoreCase) || line.Contains("§", StringComparison.Ordinal))
                    .Take(5)
                    .Select(line => line.TrimStart('-', '*', ' ', '\t'))
                    .Distinct()
                    .ToList();

                var keyPoints = generated
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Where(line => line.TrimStart().StartsWith("-") || line.TrimStart().StartsWith("*"))
                    .Select(line => line.TrimStart('-', '*', ' ', '\t'))
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .Take(5)
                    .ToList();

                return new LegalResearchResult
                {
                    Response = generated,
                    Citations = extractedCitations,
                    KeyPoints = keyPoints,
                    RelatedCases = extractedCitations.Take(2).ToList()
                };
            }

            if (!IsAiSimulatedFallbackEnabled())
            {
                throw new InvalidOperationException("AI provider is unavailable or not configured.");
            }

            await System.Threading.Tasks.Task.Delay(200); // Explicit demo fallback only.
            return new LegalResearchResult
            {
                Response = GenerateSimulatedResearchResponse(session),
                Citations = new List<string>
                {
                    "Brown v. Board of Education, 347 U.S. 483 (1954)",
                    "Marbury v. Madison, 5 U.S. 137 (1803)",
                    "Gideon v. Wainwright, 372 U.S. 335 (1963)"
                },
                KeyPoints = new List<string>
                {
                    "The legal principle established in this area applies broadly",
                    "Courts have consistently held similar interpretations",
                    "Recent precedent supports this position"
                },
                RelatedCases = new List<string>
                {
                    "Smith v. Jones (2020) - Similar fact pattern",
                    "Johnson v. State (2019) - Relevant precedent"
                }
            };
        }

        private async Task<string?> GenerateGeminiTextAsync(
            string prompt,
            CancellationToken cancellationToken,
            bool requestJson = false,
            GeminiFunctionSpec? functionSpec = null)
        {
            var apiKey = _configuration["Integrations:Gemini:ApiKey"]
                ?? _configuration["Gemini:ApiKey"]
                ?? _configuration["GEMINI_API_KEY"];
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return null;
            }

            var model = _configuration["Integrations:Gemini:Model"]
                ?? _configuration["Gemini:Model"]
                ?? "gemini-1.5-flash";
            var endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent";
            var payload = new
            {
                contents = new[]
                {
                    new
                    {
                        role = "user",
                        parts = new[] { new { text = prompt } }
                    }
                },
                generationConfig = new
                {
                    temperature = 0.2,
                    topP = 0.9,
                    maxOutputTokens = 2048,
                    responseMimeType = requestJson ? "application/json" : null
                },
                tools = functionSpec == null ? null : new[]
                {
                    new
                    {
                        functionDeclarations = new[]
                        {
                            new
                            {
                                name = functionSpec.Name,
                                description = functionSpec.Description,
                                parameters = functionSpec.Parameters
                            }
                        }
                    }
                },
                toolConfig = functionSpec == null ? null : new
                {
                    functionCallingConfig = new
                    {
                        mode = "ANY",
                        allowedFunctionNames = new[] { functionSpec.Name }
                    }
                }
            };

            var client = _httpClientFactory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            request.Headers.TryAddWithoutValidation("x-goog-api-key", apiKey);

            HttpResponseMessage response;
            try
            {
                response = await client.SendAsync(request, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gemini request failed.");
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                var requestId = response.Headers.TryGetValues("x-request-id", out var ids) ? ids.FirstOrDefault() : null;
                _logger.LogWarning("Gemini returned {StatusCode}. RequestId={RequestId}", (int)response.StatusCode, requestId);
                return null;
            }

            try
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(body);
                if (!doc.RootElement.TryGetProperty("candidates", out var candidates) ||
                    candidates.ValueKind != JsonValueKind.Array ||
                    candidates.GetArrayLength() == 0)
                {
                    return null;
                }

                if (functionSpec != null)
                {
                    var functionResultJson = TryExtractGeminiFunctionCallArgsJson(candidates[0], functionSpec.Name);
                    if (!string.IsNullOrWhiteSpace(functionResultJson))
                    {
                        return functionResultJson;
                    }
                }

                var builder = new StringBuilder();
                foreach (var part in candidates[0].GetProperty("content").GetProperty("parts").EnumerateArray())
                {
                    if (part.TryGetProperty("text", out var textElement) && textElement.ValueKind == JsonValueKind.String)
                    {
                        builder.AppendLine(textElement.GetString());
                    }
                }

                var result = builder.ToString().Trim();
                return string.IsNullOrWhiteSpace(result) ? null : result;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gemini response parsing failed.");
                return null;
            }
        }

        private static string? TryExtractGeminiFunctionCallArgsJson(JsonElement candidate, string functionName)
        {
            if (candidate.ValueKind != JsonValueKind.Object ||
                !candidate.TryGetProperty("content", out var content) ||
                content.ValueKind != JsonValueKind.Object ||
                !content.TryGetProperty("parts", out var parts) ||
                parts.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var part in parts.EnumerateArray())
            {
                if (!part.TryGetProperty("functionCall", out var functionCall) || functionCall.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var calledName = GetString(functionCall, "name");
                if (!string.Equals(calledName, functionName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (functionCall.TryGetProperty("args", out var args) &&
                    (args.ValueKind == JsonValueKind.Object || args.ValueKind == JsonValueKind.Array))
                {
                    return args.GetRawText();
                }
            }

            return null;
        }

        private static GeminiFunctionSpec BuildResearchFunctionSpec()
        {
            return new GeminiFunctionSpec
            {
                Name = "return_research_result",
                Description = "Return structured legal research output.",
                Parameters = new
                {
                    type = "OBJECT",
                    properties = new
                    {
                        executiveSummary = new { type = "STRING" },
                        keyPoints = new
                        {
                            type = "ARRAY",
                            items = new { type = "STRING" }
                        },
                        citations = new
                        {
                            type = "ARRAY",
                            items = new { type = "STRING" }
                        },
                        relatedCases = new
                        {
                            type = "ARRAY",
                            items = new { type = "STRING" }
                        },
                        practicalRecommendations = new
                        {
                            type = "ARRAY",
                            items = new { type = "STRING" }
                        }
                    },
                    required = new[] { "executiveSummary", "keyPoints", "citations", "relatedCases", "practicalRecommendations" }
                }
            };
        }

        private static GeminiFunctionSpec BuildContractAnalysisFunctionSpec()
        {
            return new GeminiFunctionSpec
            {
                Name = "return_contract_analysis",
                Description = "Return structured contract analysis result.",
                Parameters = new
                {
                    type = "OBJECT",
                    properties = new
                    {
                        summary = new { type = "STRING" },
                        detectedType = new { type = "STRING" },
                        keyTerms = new
                        {
                            type = "ARRAY",
                            items = new
                            {
                                type = "OBJECT",
                                properties = new
                                {
                                    key = new { type = "STRING" },
                                    value = new { type = "STRING" }
                                },
                                required = new[] { "key", "value" }
                            }
                        },
                        keyDates = new
                        {
                            type = "ARRAY",
                            items = new
                            {
                                type = "OBJECT",
                                properties = new
                                {
                                    label = new { type = "STRING" },
                                    value = new { type = "STRING" }
                                },
                                required = new[] { "label", "value" }
                            }
                        },
                        parties = new
                        {
                            type = "ARRAY",
                            items = new { type = "STRING" }
                        },
                        risks = new
                        {
                            type = "ARRAY",
                            items = new
                            {
                                type = "OBJECT",
                                properties = new
                                {
                                    level = new { type = "STRING" },
                                    description = new { type = "STRING" }
                                },
                                required = new[] { "level", "description" }
                            }
                        },
                        riskScore = new { type = "INTEGER" },
                        unusualClauses = new
                        {
                            type = "ARRAY",
                            items = new { type = "STRING" }
                        },
                        recommendations = new
                        {
                            type = "ARRAY",
                            items = new { type = "STRING" }
                        }
                    },
                    required = new[] { "summary", "detectedType", "keyTerms", "keyDates", "parties", "risks", "riskScore", "unusualClauses", "recommendations" }
                }
            };
        }

        private static GeminiFunctionSpec BuildCasePredictionFunctionSpec()
        {
            return new GeminiFunctionSpec
            {
                Name = "return_case_prediction",
                Description = "Return structured legal case prediction result.",
                Parameters = new
                {
                    type = "OBJECT",
                    properties = new
                    {
                        outcome = new { type = "STRING" },
                        confidence = new { type = "NUMBER" },
                        factors = new
                        {
                            type = "ARRAY",
                            items = new { type = "STRING" }
                        },
                        similarCases = new
                        {
                            type = "ARRAY",
                            items = new { type = "STRING" }
                        },
                        settlementMin = new { type = "NUMBER" },
                        settlementMax = new { type = "NUMBER" },
                        timeline = new { type = "STRING" },
                        recommendations = new
                        {
                            type = "ARRAY",
                            items = new { type = "STRING" }
                        }
                    },
                    required = new[] { "outcome", "confidence", "factors", "similarCases", "timeline", "recommendations" }
                }
            };
        }

        private static string BuildResearchPrompt(ResearchSession session, AiPromptPolicy aiPolicy)
        {
            var query = SanitizeForAi(session.Query, aiPolicy, aiPolicy.ResearchQueryMaxChars);
            var jurisdiction = SanitizeForAi(session.Jurisdiction, aiPolicy, 200) ?? "General";
            var practiceArea = SanitizeForAi(session.PracticeArea, aiPolicy, 200) ?? "General";

            if (aiPolicy.StructuredJsonEnabled)
            {
                return $$"""
You are a legal research assistant.

Query: {{query}}
Jurisdiction: {{jurisdiction}}
Practice Area: {{practiceArea}}

Return ONLY valid JSON matching this schema:
{
  "executiveSummary": "string",
  "keyPoints": ["string"],
  "citations": ["string"],
  "relatedCases": ["string"],
  "practicalRecommendations": ["string"]
}

Rules:
- No markdown fences.
- Keep arrays concise (max 8 items each).
""";
            }

            return $"""
You are a legal research assistant.

Query: {query}
Jurisdiction: {jurisdiction}
Practice Area: {practiceArea}

Return:
1) Executive summary
2) Key legal principles
3) Potential citations or cases
4) Practical recommendations

Keep answer concise and structured with bullet points.
""";
        }

        private async System.Threading.Tasks.Task<ContractAnalysisResult> AnalyzeContractWithGemini(string content, string? contractType, AiPromptPolicy aiPolicy)
        {
            var prompt = BuildContractAnalysisPrompt(content, contractType, aiPolicy);
            var generated = await GenerateGeminiTextAsync(
                prompt,
                HttpContext.RequestAborted,
                aiPolicy.StructuredJsonEnabled,
                aiPolicy.GeminiFunctionCallingEnabled ? BuildContractAnalysisFunctionSpec() : null);

            if (!string.IsNullOrWhiteSpace(generated))
            {
                if (TryParseContractAnalysisJsonResult(generated, contractType, out var structuredContract))
                {
                    return structuredContract!;
                }

                var lines = generated
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Select(l => l.Trim().TrimStart('-', '*'))
                    .Where(l => !string.IsNullOrWhiteSpace(l))
                    .ToList();

                var keyTerms = lines
                    .Where(l => l.Contains(':'))
                    .Take(6)
                    .Select(l =>
                    {
                        var idx = l.IndexOf(':');
                        return new KeyValuePair<string, string>(l[..idx].Trim(), l[(idx + 1)..].Trim());
                    })
                    .ToList();

                var riskLines = lines
                    .Where(l => l.Contains("risk", StringComparison.OrdinalIgnoreCase))
                    .Take(4)
                    .ToList();

                var risks = riskLines
                    .Select(l => new RiskItem
                    {
                        Level = l.Contains("high", StringComparison.OrdinalIgnoreCase) ? "High" :
                                l.Contains("medium", StringComparison.OrdinalIgnoreCase) ? "Medium" : "Low",
                        Description = l
                    })
                    .ToList();

                var recommendations = lines
                    .Where(l =>
                        l.Contains("recommend", StringComparison.OrdinalIgnoreCase) ||
                        l.Contains("should", StringComparison.OrdinalIgnoreCase) ||
                        l.Contains("consider", StringComparison.OrdinalIgnoreCase))
                    .Take(5)
                    .ToList();

                var partyLines = lines
                    .Where(l => l.Contains("party", StringComparison.OrdinalIgnoreCase) || l.Contains("client", StringComparison.OrdinalIgnoreCase))
                    .Distinct()
                    .Take(3)
                    .ToList();

                var dateMatches = Regex.Matches(generated, @"\b\d{4}-\d{2}-\d{2}\b")
                    .Select(m => m.Value)
                    .Distinct()
                    .Take(4)
                    .Select(v => new KeyValuePair<string, string>("Date", v))
                    .ToList();

                var unusual = lines
                    .Where(l =>
                        l.Contains("unusual", StringComparison.OrdinalIgnoreCase) ||
                        l.Contains("non-standard", StringComparison.OrdinalIgnoreCase))
                    .Take(3)
                    .ToList();

                var summary = generated.Length > 2000 ? generated[..2000] : generated;

                return new ContractAnalysisResult
                {
                    Summary = summary,
                    DetectedType = contractType ?? "Service Agreement",
                    KeyTerms = keyTerms.Count > 0 ? keyTerms : new List<KeyValuePair<string, string>>
                    {
                        new("Term", "See AI summary"),
                        new("Payment", "See AI summary"),
                    },
                    KeyDates = dateMatches,
                    Parties = partyLines.Count > 0 ? partyLines : new List<string> { "Parties referenced in analysis" },
                    Risks = risks.Count > 0 ? risks : new List<RiskItem> { new() { Level = "Medium", Description = "Review AI summary for risks." } },
                    RiskScore = Math.Clamp(2 + risks.Count(r => r.Level == "High") * 2 + risks.Count(r => r.Level == "Medium"), 1, 10),
                    UnusualClauses = unusual,
                    Recommendations = recommendations.Count > 0 ? recommendations : new List<string> { "Review obligations and risk clauses with counsel." }
                };
            }

            if (!IsAiSimulatedFallbackEnabled())
            {
                throw new InvalidOperationException("AI provider is unavailable or not configured.");
            }

            return new ContractAnalysisResult
            {
                Summary = "This contract establishes a commercial relationship between the parties with defined terms, obligations, and termination conditions.",
                DetectedType = contractType ?? "Service Agreement",
                KeyTerms = new List<KeyValuePair<string, string>>
                {
                    new("Term", "12 months with automatic renewal"),
                    new("Payment", "Net 30 days"),
                    new("Liability Cap", "$100,000")
                },
                KeyDates = new List<KeyValuePair<string, string>>
                {
                    new("Effective Date", DateTime.Now.ToString("yyyy-MM-dd")),
                    new("Renewal Date", DateTime.Now.AddYears(1).ToString("yyyy-MM-dd"))
                },
                Parties = new List<string> { "Party A (Client)", "Party B (Service Provider)" },
                Risks = new List<RiskItem>
                {
                    new() { Level = "Medium", Description = "Broad indemnification clause" },
                    new() { Level = "Low", Description = "Standard limitation of liability" }
                },
                RiskScore = 4,
                UnusualClauses = new List<string>
                {
                    "Non-standard termination notice period (90 days)",
                    "Mandatory arbitration in specific jurisdiction"
                },
                Recommendations = new List<string>
                {
                    "Negotiate shorter termination notice period",
                    "Request cap on indemnification obligations",
                    "Add material breach cure period"
                }
            };
        }

        private async System.Threading.Tasks.Task<CasePredictionResult> PredictCaseWithGemini(Matter matter, string? additionalContext, AiPromptPolicy aiPolicy)
        {
            var prompt = BuildCasePredictionPrompt(matter, additionalContext, aiPolicy);
            var generated = await GenerateGeminiTextAsync(
                prompt,
                HttpContext.RequestAborted,
                aiPolicy.StructuredJsonEnabled,
                aiPolicy.GeminiFunctionCallingEnabled ? BuildCasePredictionFunctionSpec() : null);

            if (!string.IsNullOrWhiteSpace(generated))
            {
                if (TryParseCasePredictionJsonResult(generated, out var structuredPrediction))
                {
                    return structuredPrediction!;
                }

                var lines = generated
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Select(l => l.Trim().TrimStart('-', '*'))
                    .Where(l => !string.IsNullOrWhiteSpace(l))
                    .ToList();

                var outcome = lines.FirstOrDefault(l => l.Contains("outcome", StringComparison.OrdinalIgnoreCase) || l.Contains("settlement", StringComparison.OrdinalIgnoreCase))
                              ?? "Settlement";

                var confidenceMatch = Regex.Match(generated, @"(\d{1,3}(?:\.\d+)?)\s*%");
                var confidence = confidenceMatch.Success && double.TryParse(confidenceMatch.Groups[1].Value, out var parsedConfidence)
                    ? Math.Clamp(parsedConfidence, 1, 99)
                    : 70.0;

                var moneyMatches = Regex.Matches(generated.Replace(",", string.Empty), @"\$\s?(\d{3,9})")
                    .Select(m => decimal.TryParse(m.Groups[1].Value, out var amount) ? amount : (decimal?)null)
                    .Where(v => v.HasValue)
                    .Select(v => v!.Value)
                    .Take(2)
                    .ToList();

                var timeline = lines.FirstOrDefault(l => l.Contains("month", StringComparison.OrdinalIgnoreCase) || l.Contains("week", StringComparison.OrdinalIgnoreCase))
                    ?? "6-9 months to resolution";

                var recs = lines
                    .Where(l => l.Contains("recommend", StringComparison.OrdinalIgnoreCase) || l.Contains("consider", StringComparison.OrdinalIgnoreCase))
                    .Take(4)
                    .ToList();

                var factors = lines
                    .Where(l => l.Contains("factor", StringComparison.OrdinalIgnoreCase) || l.Contains("because", StringComparison.OrdinalIgnoreCase))
                    .Take(4)
                    .ToList();

                var similarCases = lines
                    .Where(l => l.Contains(" v. ", StringComparison.OrdinalIgnoreCase))
                    .Take(3)
                    .ToList();

                return new CasePredictionResult
                {
                    Outcome = outcome,
                    Confidence = confidence,
                    Factors = factors.Count > 0 ? factors : new List<string> { "Model-generated assessment available in narrative output." },
                    SimilarCases = similarCases,
                    SettlementMin = moneyMatches.Count > 0 ? moneyMatches.Min() : 35000,
                    SettlementMax = moneyMatches.Count > 1 ? moneyMatches.Max() : 75000,
                    Timeline = timeline,
                    Recommendations = recs.Count > 0 ? recs : new List<string> { "Prepare evidence package and consider early mediation." }
                };
            }

            if (!IsAiSimulatedFallbackEnabled())
            {
                throw new InvalidOperationException("AI provider is unavailable or not configured.");
            }

            return new CasePredictionResult
            {
                Outcome = "Settlement",
                Confidence = 72.5,
                Factors = new List<string>
                {
                    "Similar cases in this jurisdiction settled 68% of the time",
                    "Strong liability evidence supports favorable outcome",
                    "Defendant's prior settlement history"
                },
                SimilarCases = new List<string>
                {
                    "Johnson v. ABC Corp (2022) - Settled, $45,000",
                    "Williams v. XYZ Inc (2021) - Settled, $62,000"
                },
                SettlementMin = 35000,
                SettlementMax = 75000,
                Timeline = "6-9 months to resolution",
                Recommendations = new List<string>
                {
                    "Consider early mediation to reduce costs",
                    "Gather additional documentation on damages",
                    "Prepare for discovery requests"
                }
            };
        }

        private static string BuildContractAnalysisPrompt(string content, string? contractType, AiPromptPolicy aiPolicy)
        {
            var snippet = SanitizeForAi(content, aiPolicy, aiPolicy.ContractContentMaxChars) ?? string.Empty;
            var normalizedContractType = SanitizeForAi(contractType, aiPolicy, 120) ?? "Unknown";

            if (aiPolicy.StructuredJsonEnabled)
            {
                return $$"""
You are a legal contract analysis assistant.

Contract Type: {{normalizedContractType}}

Analyze this contract and return ONLY valid JSON matching this schema:
{
  "summary": "string",
  "detectedType": "string",
  "keyTerms": [{ "key": "string", "value": "string" }],
  "keyDates": [{ "label": "string", "value": "YYYY-MM-DD or text" }],
  "parties": ["string"],
  "risks": [{ "level": "Low|Medium|High", "description": "string" }],
  "riskScore": 1,
  "unusualClauses": ["string"],
  "recommendations": ["string"]
}

Rules:
- No markdown fences.
- riskScore must be integer 1..10.
- Keep arrays concise (max 8 items).

Contract text:
{{snippet}}
""";
            }

            return $"""
You are a legal contract analysis assistant.

Contract Type: {normalizedContractType}

Analyze this contract and return:
1) Summary
2) Key terms (term: explanation)
3) Risks with level (High/Medium/Low)
4) Unusual clauses
5) Recommendations
6) Any key dates as YYYY-MM-DD where possible

Contract text:
{snippet}
""";
        }

        private static string BuildCasePredictionPrompt(Matter matter, string? additionalContext, AiPromptPolicy aiPolicy)
        {
            var matterName = aiPolicy.IncludeMatterNameInPredictionPrompt ? (SanitizeForAi(matter.Name, aiPolicy, 250) ?? "Unknown Matter") : "[REDACTED_MATTER]";
            var caseNumber = aiPolicy.IncludeCaseNumberInPredictionPrompt ? (SanitizeForAi(matter.CaseNumber, aiPolicy, 120) ?? "Unknown") : "[REDACTED_CASE_NUMBER]";
            var practiceArea = SanitizeForAi(matter.PracticeArea, aiPolicy, 120) ?? "Unknown";
            var courtType = SanitizeForAi(matter.CourtType, aiPolicy, 120) ?? "Unknown";
            var status = SanitizeForAi(matter.Status, aiPolicy, 120) ?? "Unknown";
            var feeStructure = SanitizeForAi(matter.FeeStructure, aiPolicy, 120) ?? "Unknown";
            var normalizedAdditionalContext = SanitizeForAi(additionalContext, aiPolicy, aiPolicy.AdditionalContextMaxChars) ?? "None";

            if (aiPolicy.StructuredJsonEnabled)
            {
                return $$"""
You are a legal case outcome assistant.

Matter Name: {{matterName}}
Case Number: {{caseNumber}}
Practice Area: {{practiceArea}}
Court Type: {{courtType}}
Status: {{status}}
Fee Structure: {{feeStructure}}
Additional Context: {{normalizedAdditionalContext}}

Return ONLY valid JSON matching this schema:
{
  "outcome": "string",
  "confidence": 0,
  "factors": ["string"],
  "similarCases": ["string"],
  "settlementMin": 0,
  "settlementMax": 0,
  "timeline": "string",
  "recommendations": ["string"]
}

Rules:
- confidence is number 0..100
- settlementMin and settlementMax are numbers when applicable
- No markdown fences.
""";
            }

            return $"""
You are a legal case outcome assistant.

Matter Name: {matterName}
Case Number: {caseNumber}
Practice Area: {practiceArea}
Court Type: {courtType}
Status: {status}
Fee Structure: {feeStructure}
Additional Context: {normalizedAdditionalContext}

Return:
1) Likely outcome
2) Confidence %
3) Main factors
4) Similar case patterns
5) Settlement range (if applicable)
6) Estimated timeline
7) Recommendations
""";
        }

        private string GenerateSimulatedResearchResponse(ResearchSession session)
        {
            return $@"## Legal Research Summary

**Query:** {session.Query}

**Jurisdiction:** {session.Jurisdiction ?? "Federal/General"}

### Analysis

Based on established legal precedent and current statutory framework, the following key points emerge:

1. **Foundational Principles**: The legal doctrine in this area is well-established, with courts consistently applying similar standards.

2. **Relevant Case Law**: Multiple precedents support the interpretation that [specific legal principle applies].

3. **Statutory Framework**: The applicable statutes provide clear guidance on this matter.

### Recommendations

- Review the cited cases for specific factual similarities
- Consider jurisdictional variations in application
- Document all relevant evidence supporting the legal position

*Note: This research is AI-generated and should be verified by legal counsel.*";
        }

        private static string GenerateDegradedChatResponse(string message, string? contextData, bool enableSearch)
        {
            var trimmedMessage = (message ?? string.Empty).Trim();
            var normalized = trimmedMessage.ToLowerInvariant();
            var hasContext = !string.IsNullOrWhiteSpace(contextData);
            var builder = new StringBuilder();

            if (IsGreeting(normalized))
            {
                builder.AppendLine("Hello. Juris is available in limited mode right now.");
                builder.AppendLine("The external AI provider is temporarily unavailable, but I can still help you structure the next step.");
            }
            else if (normalized.Contains("summar", StringComparison.Ordinal))
            {
                builder.AppendLine("Juris is in limited mode right now, so I cannot generate a full AI summary.");
                builder.AppendLine("Attach the relevant document and tell me whether you want chronology, key admissions, obligations, damages, or action items.");
            }
            else if (normalized.Contains("draft", StringComparison.Ordinal) ||
                     normalized.Contains("motion", StringComparison.Ordinal) ||
                     normalized.Contains("letter", StringComparison.Ordinal) ||
                     normalized.Contains("email", StringComparison.Ordinal))
            {
                builder.AppendLine("Juris is in limited mode right now, so I cannot generate a polished draft from the provider.");
                builder.AppendLine("Send the document type, audience, jurisdiction, tone, and the core facts you want included.");
            }
            else if (normalized.Contains("research", StringComparison.Ordinal) ||
                     normalized.Contains("case law", StringComparison.Ordinal) ||
                     normalized.Contains("statute", StringComparison.Ordinal) ||
                     enableSearch)
            {
                builder.AppendLine("Juris is in limited mode right now, so live legal research is not available in this response.");
                builder.AppendLine("Provide the jurisdiction, issue, claim or defense, and the controlling facts so I can help frame the research request.");
            }
            else
            {
                builder.AppendLine("Juris is available in limited mode right now.");
                builder.AppendLine("The external AI provider is temporarily unavailable, but I can still help you turn this into a usable legal task.");
            }

            if (hasContext)
            {
                builder.AppendLine();
                builder.AppendLine("Linked document context is attached and will be useful once the provider is back.");
            }

            if (!string.IsNullOrWhiteSpace(trimmedMessage) && !IsGreeting(normalized))
            {
                builder.AppendLine();
                builder.AppendLine("Your last request:");
                builder.AppendLine($"\"{TruncateForUi(trimmedMessage, 220)}\"");
            }

            builder.AppendLine();
            builder.AppendLine("Best next input:");
            builder.AppendLine("1. State the exact task: summarize, draft, analyze, or research.");
            builder.AppendLine("2. Add the controlling facts, parties, and jurisdiction.");
            builder.AppendLine("3. Attach the relevant document or matter context if needed.");

            return builder.ToString().Trim();
        }

        private static bool IsGreeting(string value)
        {
            return value is "hi" or "hello" or "hey" or "selam" or "merhaba";
        }

        private static string TruncateForUi(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length <= maxLength)
            {
                return value;
            }

            return value[..maxLength].TrimEnd() + "...";
        }

        private AiPromptPolicy ResolveAiPromptPolicy()
        {
            var tenantId = _tenantContext.TenantId?.Trim();
            var tenantSlug = _tenantContext.TenantSlug?.Trim();

            string? ResolveRaw(string key)
            {
                if (!string.IsNullOrWhiteSpace(tenantId))
                {
                    var tenantValue = _configuration[$"AI:TenantPolicies:{tenantId}:{key}"];
                    if (!string.IsNullOrWhiteSpace(tenantValue))
                    {
                        return tenantValue;
                    }
                }

                if (!string.IsNullOrWhiteSpace(tenantSlug))
                {
                    var slugValue = _configuration[$"AI:TenantPoliciesBySlug:{tenantSlug}:{key}"];
                    if (!string.IsNullOrWhiteSpace(slugValue))
                    {
                        return slugValue;
                    }
                }

                return _configuration[$"AI:Policy:{key}"];
            }

            bool ResolveBool(string key, bool fallback)
            {
                var raw = ResolveRaw(key);
                return bool.TryParse(raw, out var value) ? value : fallback;
            }

            int ResolveInt(string key, int fallback, int min = 0, int max = int.MaxValue)
            {
                var raw = ResolveRaw(key);
                if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                {
                    return fallback;
                }

                return Math.Clamp(value, min, max);
            }

            return new AiPromptPolicy
            {
                TenantId = tenantId,
                RequireConsent = ResolveBool("RequireConsent", false),
                ConsentGranted = ResolveBool("ConsentGranted", true),
                AllowResearch = ResolveBool("AllowResearch", true),
                AllowContractAnalysis = ResolveBool("AllowContractAnalysis", true),
                AllowCasePrediction = ResolveBool("AllowCasePrediction", true),
                StructuredJsonEnabled = ResolveBool("StructuredJsonEnabled", true),
                GeminiFunctionCallingEnabled = ResolveBool("GeminiFunctionCallingEnabled", true),
                MinimumNecessaryEnabled = ResolveBool("MinimumNecessaryEnabled", true),
                RedactionEnabled = ResolveBool("RedactionEnabled", true),
                RedactEmails = ResolveBool("RedactEmails", true),
                RedactPhones = ResolveBool("RedactPhones", true),
                RedactTaxIds = ResolveBool("RedactTaxIds", true),
                RedactPostalAddresses = ResolveBool("RedactPostalAddresses", false),
                ResearchQueryMaxChars = ResolveInt("ResearchQueryMaxChars", 4000, 200, 20000),
                ContractContentMaxChars = ResolveInt("ContractContentMaxChars", 12000, 1000, 100000),
                AdditionalContextMaxChars = ResolveInt("AdditionalContextMaxChars", 3000, 100, 20000),
                IncludeMatterNameInPredictionPrompt = ResolveBool("IncludeMatterNameInPredictionPrompt", false),
                IncludeCaseNumberInPredictionPrompt = ResolveBool("IncludeCaseNumberInPredictionPrompt", false)
            };
        }

        private ActionResult? ValidateAiOperationPolicy(AiPromptPolicy policy, string operation)
        {
            var allowed = operation switch
            {
                "research" => policy.AllowResearch,
                "contract_analysis" => policy.AllowContractAnalysis,
                "case_prediction" => policy.AllowCasePrediction,
                _ => true
            };

            if (!allowed)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { message = $"AI operation '{operation}' is disabled for this tenant." });
            }

            if (policy.RequireConsent && !policy.ConsentGranted)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "Tenant AI consent is required before sending data to external AI providers." });
            }

            return null;
        }

        private static string? SanitizeForAi(string? value, AiPromptPolicy policy, int maxChars)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var result = value.Trim();

            if (policy.MinimumNecessaryEnabled && maxChars > 0 && result.Length > maxChars)
            {
                result = result[..maxChars];
            }

            if (!policy.RedactionEnabled)
            {
                return result;
            }

            if (policy.RedactEmails)
            {
                result = Regex.Replace(result, @"\b[A-Z0-9._%+\-]+@[A-Z0-9.\-]+\.[A-Z]{2,}\b", "[EMAIL]", RegexOptions.IgnoreCase);
            }

            if (policy.RedactPhones)
            {
                result = Regex.Replace(result, @"(?<!\w)(?:\+?1[\s.\-]?)?(?:\(?\d{3}\)?[\s.\-]?)\d{3}[\s.\-]?\d{4}(?!\w)", "[PHONE]");
            }

            if (policy.RedactTaxIds)
            {
                result = Regex.Replace(result, @"\b\d{3}-\d{2}-\d{4}\b", "[SSN]");
                result = Regex.Replace(result, @"\b\d{2}-\d{7}\b", "[TAX_ID]");
            }

            if (policy.RedactPostalAddresses)
            {
                result = Regex.Replace(result, @"\b\d{1,6}\s+[A-Za-z0-9.\- ]+\s(?:Street|St|Avenue|Ave|Road|Rd|Boulevard|Blvd|Drive|Dr|Lane|Ln|Court|Ct)\b", "[ADDRESS]", RegexOptions.IgnoreCase);
            }

            return result;
        }

        private static bool TryParseResearchJsonResult(string generated, out LegalResearchResult? result)
        {
            result = null;
            if (!TryParseGeminiJsonDocument(generated, out var doc))
            {
                return false;
            }

            using (doc)
            {
                var root = doc.RootElement;
                var executiveSummary = GetString(root, "executiveSummary") ?? GetString(root, "summary");
                var keyPoints = GetStringArray(root, "keyPoints");
                var citations = GetStringArray(root, "citations");
                var relatedCases = GetStringArray(root, "relatedCases");
                var recommendations = GetStringArray(root, "practicalRecommendations");

                if (string.IsNullOrWhiteSpace(executiveSummary) && keyPoints.Count == 0 && citations.Count == 0)
                {
                    return false;
                }

                var responseBuilder = new StringBuilder();
                if (!string.IsNullOrWhiteSpace(executiveSummary))
                {
                    responseBuilder.AppendLine("Executive Summary");
                    responseBuilder.AppendLine(executiveSummary);
                    responseBuilder.AppendLine();
                }

                if (keyPoints.Count > 0)
                {
                    responseBuilder.AppendLine("Key Points");
                    foreach (var point in keyPoints)
                    {
                        responseBuilder.Append("- ").AppendLine(point);
                    }
                    responseBuilder.AppendLine();
                }

                if (recommendations.Count > 0)
                {
                    responseBuilder.AppendLine("Practical Recommendations");
                    foreach (var item in recommendations)
                    {
                        responseBuilder.Append("- ").AppendLine(item);
                    }
                }

                result = new LegalResearchResult
                {
                    Response = responseBuilder.ToString().Trim(),
                    Citations = citations,
                    KeyPoints = keyPoints,
                    RelatedCases = relatedCases.Count > 0 ? relatedCases : citations.Take(2).ToList()
                };
                return true;
            }
        }

        private static bool TryParseContractAnalysisJsonResult(string generated, string? fallbackContractType, out ContractAnalysisResult? result)
        {
            result = null;
            if (!TryParseGeminiJsonDocument(generated, out var doc))
            {
                return false;
            }

            using (doc)
            {
                var root = doc.RootElement;
                var summary = GetString(root, "summary");
                var detectedType = GetString(root, "detectedType") ?? fallbackContractType;
                var keyTerms = GetLabeledPairs(root, "keyTerms");
                var keyDates = GetLabeledPairs(root, "keyDates");
                var parties = GetStringArray(root, "parties");
                var risks = GetRiskItems(root, "risks");
                var unusualClauses = GetStringArray(root, "unusualClauses");
                var recommendations = GetStringArray(root, "recommendations");
                var riskScore = GetInt(root, "riskScore") ?? 0;

                if (string.IsNullOrWhiteSpace(summary) && keyTerms.Count == 0 && risks.Count == 0)
                {
                    return false;
                }

                result = new ContractAnalysisResult
                {
                    Summary = string.IsNullOrWhiteSpace(summary) ? generated[..Math.Min(2000, generated.Length)] : summary!,
                    DetectedType = string.IsNullOrWhiteSpace(detectedType) ? "Service Agreement" : detectedType,
                    KeyTerms = keyTerms,
                    KeyDates = keyDates,
                    Parties = parties,
                    Risks = risks,
                    RiskScore = Math.Clamp(riskScore <= 0 ? (risks.Count == 0 ? 3 : risks.Count + 2) : riskScore, 1, 10),
                    UnusualClauses = unusualClauses,
                    Recommendations = recommendations
                };
                return true;
            }
        }

        private static bool TryParseCasePredictionJsonResult(string generated, out CasePredictionResult? result)
        {
            result = null;
            if (!TryParseGeminiJsonDocument(generated, out var doc))
            {
                return false;
            }

            using (doc)
            {
                var root = doc.RootElement;
                var outcome = GetString(root, "outcome");
                var confidence = GetDouble(root, "confidence");
                var factors = GetStringArray(root, "factors");
                var similarCases = GetStringArray(root, "similarCases");
                var settlementMin = GetDecimal(root, "settlementMin");
                var settlementMax = GetDecimal(root, "settlementMax");
                var timeline = GetString(root, "timeline");
                var recommendations = GetStringArray(root, "recommendations");

                if (string.IsNullOrWhiteSpace(outcome) && factors.Count == 0 && recommendations.Count == 0)
                {
                    return false;
                }

                result = new CasePredictionResult
                {
                    Outcome = string.IsNullOrWhiteSpace(outcome) ? "Settlement" : outcome!,
                    Confidence = Math.Clamp(confidence ?? 70d, 1d, 99d),
                    Factors = factors.Count > 0 ? factors : new List<string> { "Model-generated assessment available in structured response." },
                    SimilarCases = similarCases,
                    SettlementMin = settlementMin,
                    SettlementMax = settlementMax,
                    Timeline = string.IsNullOrWhiteSpace(timeline) ? "6-9 months to resolution" : timeline,
                    Recommendations = recommendations.Count > 0 ? recommendations : new List<string> { "Prepare evidence package and consider early mediation." }
                };
                return true;
            }
        }

        private static bool TryParseGeminiJsonDocument(string raw, out JsonDocument? document)
        {
            document = null;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            if (TryParseJsonDocument(raw, out document))
            {
                return true;
            }

            var fencedMatch = Regex.Match(raw, "```(?:json)?\\s*(\\{[\\s\\S]*\\}|\\[[\\s\\S]*\\])\\s*```", RegexOptions.IgnoreCase);
            if (fencedMatch.Success && TryParseJsonDocument(fencedMatch.Groups[1].Value, out document))
            {
                return true;
            }

            var firstBrace = raw.IndexOf('{');
            var lastBrace = raw.LastIndexOf('}');
            if (firstBrace >= 0 && lastBrace > firstBrace)
            {
                var candidate = raw[firstBrace..(lastBrace + 1)];
                if (TryParseJsonDocument(candidate, out document))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryParseJsonDocument(string raw, out JsonDocument? document)
        {
            document = null;
            try
            {
                document = JsonDocument.Parse(raw);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string? GetString(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out var element))
            {
                return null;
            }

            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString()?.Trim(),
                JsonValueKind.Number => element.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => null
            };
        }

        private static List<string> GetStringArray(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out var element) || element.ValueKind != JsonValueKind.Array)
            {
                return new List<string>();
            }

            return element.EnumerateArray()
                .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : item.GetRawText())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s!.Trim())
                .Distinct(StringComparer.Ordinal)
                .Take(8)
                .ToList();
        }

        private static List<KeyValuePair<string, string>> GetLabeledPairs(JsonElement root, string propertyName)
        {
            var result = new List<KeyValuePair<string, string>>();
            if (!root.TryGetProperty(propertyName, out var element) || element.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var key = GetString(item, "key") ?? GetString(item, "label");
                var value = GetString(item, "value");
                if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                result.Add(new KeyValuePair<string, string>(key!, value!));
                if (result.Count >= 8)
                {
                    break;
                }
            }

            return result;
        }

        private static List<RiskItem> GetRiskItems(JsonElement root, string propertyName)
        {
            var result = new List<RiskItem>();
            if (!root.TryGetProperty(propertyName, out var element) || element.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var text = item.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        result.Add(new RiskItem { Level = "Medium", Description = text! });
                    }
                }
                else if (item.ValueKind == JsonValueKind.Object)
                {
                    var description = GetString(item, "description");
                    if (string.IsNullOrWhiteSpace(description))
                    {
                        continue;
                    }

                    var level = GetString(item, "level");
                    result.Add(new RiskItem
                    {
                        Level = NormalizeRiskLevel(level),
                        Description = description!
                    });
                }

                if (result.Count >= 8)
                {
                    break;
                }
            }

            return result;
        }

        private static string NormalizeRiskLevel(string? level)
        {
            if (string.IsNullOrWhiteSpace(level))
            {
                return "Medium";
            }

            return level.Trim().ToLowerInvariant() switch
            {
                "high" => "High",
                "medium" => "Medium",
                "low" => "Low",
                _ => "Medium"
            };
        }

        private static int? GetInt(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out var element))
            {
                return null;
            }

            if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var value))
            {
                return value;
            }

            if (element.ValueKind == JsonValueKind.String &&
                int.TryParse(element.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                return value;
            }

            return null;
        }

        private static double? GetDouble(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out var element))
            {
                return null;
            }

            if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var value))
            {
                return value;
            }

            if (element.ValueKind == JsonValueKind.String &&
                double.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                return value;
            }

            return null;
        }

        private static decimal? GetDecimal(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out var element))
            {
                return null;
            }

            if (element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out var value))
            {
                return value;
            }

            if (element.ValueKind == JsonValueKind.String)
            {
                var raw = element.GetString();
                if (string.IsNullOrWhiteSpace(raw))
                {
                    return null;
                }

                raw = raw.Replace("$", string.Empty).Replace(",", string.Empty).Trim();
                if (decimal.TryParse(raw, NumberStyles.Number | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value))
                {
                    return value;
                }
            }

            return null;
        }

        private string? GetCurrentUserId()
        {
            return User.FindFirst("sub")?.Value ??
                   User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        }

        private IQueryable<T> TenantScope<T>(IQueryable<T> query) where T : class
        {
            var tenantId = RequireTenantId();
            return query.Where(e => EF.Property<string>(e, "TenantId") == tenantId);
        }

        private string RequireTenantId()
        {
            var tenantId = _tenantContext.TenantId;
            if (string.IsNullOrWhiteSpace(tenantId))
            {
                throw new InvalidOperationException("Tenant context is required.");
            }

            return tenantId;
        }

        private Task<Matter?> GetMatterByIdAsync(string id)
        {
            return TenantScope(_context.Matters).FirstOrDefaultAsync(m => m.Id == id);
        }

        private bool IsAiSimulatedFallbackEnabled()
        {
            return _configuration.GetValue("AI:EnableSimulatedFallback", false) ||
                   _configuration.GetValue("Integrations:Gemini:EnableSimulatedFallback", false);
        }

        private async Task<AiDraftOwnedOutputAccess?> GetOwnedDraftOutputAccessAsync(string outputId, bool asNoTracking)
        {
            return await GetDraftOutputAccessAsync(outputId, asNoTracking, requireOwnership: true);
        }

        private async Task<AiDraftOwnedOutputAccess?> GetDraftOutputAccessAsync(string outputId, bool asNoTracking, bool requireOwnership)
        {
            var userId = GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return null;
            }

            if (!requireOwnership && !(User.IsInRole("Admin") || User.IsInRole("SecurityAdmin")))
            {
                return null;
            }

            var outputs = TenantScope(_context.AiDraftOutputs).AsQueryable();
            var sessions = TenantScope(_context.AiDraftSessions).AsQueryable();

            if (asNoTracking)
            {
                outputs = outputs.AsNoTracking();
                sessions = sessions.AsNoTracking();
            }

            return await (from output in outputs
                          join session in sessions on output.SessionId equals session.Id
                          where output.Id == outputId &&
                                (!requireOwnership || session.UserId == userId)
                          select new AiDraftOwnedOutputAccess
                          {
                              Output = output,
                              Session = session
                          })
                .FirstOrDefaultAsync();
        }

        private sealed class AiPromptPolicy
        {
            public string? TenantId { get; init; }
            public bool RequireConsent { get; init; }
            public bool ConsentGranted { get; init; }
            public bool AllowResearch { get; init; }
            public bool AllowContractAnalysis { get; init; }
            public bool AllowCasePrediction { get; init; }
            public bool StructuredJsonEnabled { get; init; }
            public bool GeminiFunctionCallingEnabled { get; init; }
            public bool MinimumNecessaryEnabled { get; init; }
            public bool RedactionEnabled { get; init; }
            public bool RedactEmails { get; init; }
            public bool RedactPhones { get; init; }
            public bool RedactTaxIds { get; init; }
            public bool RedactPostalAddresses { get; init; }
            public int ResearchQueryMaxChars { get; init; }
            public int ContractContentMaxChars { get; init; }
            public int AdditionalContextMaxChars { get; init; }
            public bool IncludeMatterNameInPredictionPrompt { get; init; }
            public bool IncludeCaseNumberInPredictionPrompt { get; init; }
        }

        private sealed class AiDraftOwnedOutputAccess
        {
            public AiDraftOutput Output { get; set; } = default!;
            public AiDraftSession Session { get; set; } = default!;
        }

        private sealed class GeminiFunctionSpec
        {
            public string Name { get; init; } = string.Empty;
            public string Description { get; init; } = string.Empty;
            public object Parameters { get; init; } = new { };
        }
    }

    // DTOs and Result Classes
    public class ResearchRequestDto
    {
        public string Query { get; set; } = string.Empty;
        public string? Title { get; set; }
        public string? MatterId { get; set; }
        public string? Jurisdiction { get; set; }
        public string? PracticeArea { get; set; }
    }

    public class AiChatRequestDto
    {
        public List<AiChatMessageDto>? History { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? ContextData { get; set; }
        public bool EnableSearch { get; set; }
    }

    public class AiChatMessageDto
    {
        public string Role { get; set; } = string.Empty;
        public List<AiChatPartDto>? Parts { get; set; }
    }

    public class AiChatPartDto
    {
        public string? Text { get; set; }
    }

    public class ContractAnalysisDto
    {
        public string DocumentId { get; set; } = string.Empty;
        public string DocumentContent { get; set; } = string.Empty;
        public string? MatterId { get; set; }
        public string? ContractType { get; set; }
    }

    public class CasePredictionDto
    {
        public string MatterId { get; set; } = string.Empty;
        public string? AdditionalContext { get; set; }
    }

    public class EvidenceLinkedDraftCreateDto
    {
        public string? MatterId { get; set; }
        public string? Title { get; set; }
        public string? Purpose { get; set; }
        public string? JurisdictionContextJson { get; set; }
        public object? JurisdictionContext { get; set; }
        public string RenderedText { get; set; } = string.Empty;
        public string? OutputStatus { get; set; }
        public string? Model { get; set; }
        public string? PromptTemplateVersion { get; set; }
        public string? RetrievalBundleId { get; set; }
        public string? CorrelationId { get; set; }
        public string? RetrievalBundleJson { get; set; }
        public object? RetrievalBundle { get; set; }
        public string? StructuredClaimsJson { get; set; }
        public object? StructuredClaims { get; set; }
        public string? SessionMetadataJson { get; set; }
        public object? SessionMetadata { get; set; }
        public string? OutputMetadataJson { get; set; }
        public object? OutputMetadata { get; set; }
        public DateTime? GeneratedAtUtc { get; set; }
        public List<EvidenceLinkedDraftClaimDto>? Claims { get; set; }
        public List<EvidenceLinkedDraftVerificationRunDto>? VerificationRuns { get; set; }
    }

    public class EvidenceLinkedDraftClaimDto
    {
        public int? OrderIndex { get; set; }
        public string ClaimText { get; set; } = string.Empty;
        public string? ClaimType { get; set; }
        public bool IsCritical { get; set; }
        public decimal? Confidence { get; set; }
        public string? Status { get; set; }
        public string? SupportSummary { get; set; }
        public string? MetadataJson { get; set; }
        public object? Metadata { get; set; }
        public List<EvidenceLinkedDraftEvidenceLinkDto>? EvidenceLinks { get; set; }
        public List<EvidenceLinkedDraftRuleCitationDto>? RuleCitations { get; set; }
    }

    public class EvidenceLinkedDraftEvidenceLinkDto
    {
        public string? DocumentId { get; set; }
        public string? DocumentVersionId { get; set; }
        public string? Sha256 { get; set; }
        public int? Page { get; set; }
        public string? ParagraphId { get; set; }
        public int? CharStart { get; set; }
        public int? CharEnd { get; set; }
        public string? Excerpt { get; set; }
        public string? SupportStrength { get; set; }
        public string? WhySupports { get; set; }
        public string? MetadataJson { get; set; }
        public object? Metadata { get; set; }
    }

    public class EvidenceLinkedDraftRuleCitationDto
    {
        public string? JurisdictionRulePackId { get; set; }
        public int? RulePackVersion { get; set; }
        public string? RuleCode { get; set; }
        public string? SourceCitation { get; set; }
        public string? CitationText { get; set; }
        public DateTime? EffectiveAtUtc { get; set; }
        public decimal? Confidence { get; set; }
        public string? MetadataJson { get; set; }
        public object? Metadata { get; set; }
    }

    public class EvidenceLinkedDraftVerificationRunDto
    {
        public string? VerifierVersion { get; set; }
        public string? Status { get; set; }
        public string? CorrelationId { get; set; }
        public string? ResultJson { get; set; }
        public object? Result { get; set; }
        public string? MetadataJson { get; set; }
        public object? Metadata { get; set; }
        public DateTime? CreatedAtUtc { get; set; }
    }

    internal class LegalResearchResult
    {
        public string Response { get; set; } = string.Empty;
        public List<string> Citations { get; set; } = new();
        public List<string> KeyPoints { get; set; } = new();
        public List<string> RelatedCases { get; set; } = new();
    }

    internal class ContractAnalysisResult
    {
        public string Summary { get; set; } = string.Empty;
        public string? DetectedType { get; set; }
        public List<KeyValuePair<string, string>> KeyTerms { get; set; } = new();
        public List<KeyValuePair<string, string>> KeyDates { get; set; } = new();
        public List<string> Parties { get; set; } = new();
        public List<RiskItem> Risks { get; set; } = new();
        public int RiskScore { get; set; }
        public List<string> UnusualClauses { get; set; } = new();
        public List<string> Recommendations { get; set; } = new();
    }

    internal class RiskItem
    {
        public string Level { get; set; } = "Low";
        public string Description { get; set; } = string.Empty;
    }

    internal class CasePredictionResult
    {
        public string Outcome { get; set; } = string.Empty;
        public double Confidence { get; set; }
        public List<string> Factors { get; set; } = new();
        public List<string> SimilarCases { get; set; } = new();
        public decimal? SettlementMin { get; set; }
        public decimal? SettlementMax { get; set; }
        public string? Timeline { get; set; }
        public List<string> Recommendations { get; set; } = new();
    }
}
