using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using JurisFlow.Server.Models;
using JurisFlow.Server.Services;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace JurisFlow.Server.Controllers
{
    public partial class AiController
    {
        private const string EvidenceDraftingProviderKey = "ai-drafting";
        private const string EvidenceDraftingReviewItemType = "ai_evidence_review";
        private const string EvidenceDraftingRuleDriftReviewItemType = "ai_rule_citation_drift_review";

        // POST: api/ai/drafts/evidence-linked/generate
        [HttpPost("drafts/evidence-linked/generate")]
        public async System.Threading.Tasks.Task<ActionResult> GenerateEvidenceLinkedDraft([FromBody] EvidenceLinkedDraftGenerateDto dto)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.Prompt))
            {
                return BadRequest(new { message = "Prompt is required." });
            }

            var selectedIds = (dto.SelectedDocumentIds ?? new List<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (selectedIds.Count == 0)
            {
                return BadRequest(new { message = "At least one selected document is required." });
            }

            var now = DateTime.UtcNow;
            var topChunksPerDocument = Math.Clamp(dto.TopChunksPerDocument ?? 4, 1, 8);
            var maxClaims = Math.Clamp(dto.MaxClaims ?? 6, 1, 12);
            var userId = GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Unauthorized(new { message = "Authenticated user id is required." });
            }

            var normalizedMatterId = string.IsNullOrWhiteSpace(dto.MatterId) ? null : dto.MatterId.Trim();
            Matter? matter = null;
            if (!string.IsNullOrWhiteSpace(normalizedMatterId))
            {
                matter = await GetMatterByIdAsync(normalizedMatterId);
                if (matter == null)
                {
                    return NotFound(new { message = "Matter not found for this tenant." });
                }
            }

            var docs = await TenantScope(_context.Documents).AsNoTracking().Where(d => selectedIds.Contains(d.Id)).ToListAsync();
            if (docs.Count == 0)
            {
                return BadRequest(new { message = "Selected documents were not found." });
            }
            if (docs.Count != selectedIds.Count)
            {
                return BadRequest(new { message = "One or more selected documents are invalid for this tenant." });
            }

            if (!string.IsNullOrWhiteSpace(normalizedMatterId))
            {
                var invalidMatterDoc = docs.Any(d => !string.Equals(d.MatterId, normalizedMatterId, StringComparison.Ordinal));
                if (invalidMatterDoc)
                {
                    return Forbid();
                }
            }
            else
            {
                // Without explicit matter scope, only allow user's own uploads.
                var unauthorizedDoc = docs.Any(d => !string.Equals(d.UploadedBy, userId, StringComparison.Ordinal));
                if (unauthorizedDoc)
                {
                    return Forbid();
                }
            }

            var indexes = await TenantScope(_context.DocumentContentIndexes).AsNoTracking().Where(i => selectedIds.Contains(i.DocumentId)).ToListAsync();
            var versions = await TenantScope(_context.DocumentVersions).AsNoTracking()
                .Where(v => selectedIds.Contains(v.DocumentId))
                .OrderByDescending(v => v.CreatedAt)
                .ToListAsync();
            var versionMap = versions.GroupBy(v => v.DocumentId).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            var indexMap = indexes.GroupBy(i => i.DocumentId).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            var promptTokens = TokenizeForEvidenceDrafting(dto.Prompt);
            var ruleAwareContext = await BuildRuleAwareContextAsync(normalizedMatterId, dto.JurisdictionContext, now);

            var chunks = new List<EvidenceBundleChunk>();
            foreach (var doc in docs)
            {
                if (!indexMap.TryGetValue(doc.Id, out var idx))
                {
                    continue;
                }
                if (idx == null || string.IsNullOrWhiteSpace(idx.Content)) continue;
                versionMap.TryGetValue(doc.Id, out var ver);
                var paragraphs = SplitIntoParagraphs(idx.Content);
                for (var i = 0; i < paragraphs.Count; i++)
                {
                    chunks.Add(new EvidenceBundleChunk
                    {
                        ChunkId = $"doc:{doc.Id}:p:{i + 1}",
                        DocumentId = doc.Id,
                        DocumentName = doc.Name,
                        DocumentVersionId = ver?.Id,
                        Sha256 = ver?.Sha256,
                        ParagraphId = $"p-{i + 1}",
                        Text = paragraphs[i],
                        Score = ScoreParagraph(paragraphs[i], promptTokens)
                    });
                }
            }

            var selectedChunks = chunks
                .GroupBy(c => c.DocumentId)
                .SelectMany(g => g.OrderByDescending(c => c.Score).ThenBy(c => c.ParagraphId).Take(topChunksPerDocument))
                .OrderByDescending(c => c.Score)
                .ThenBy(c => c.DocumentName)
                .ThenBy(c => c.ParagraphId)
                .Take(Math.Max(maxClaims * 2, 8))
                .ToList();

            var rulePacks = await ResolveApplicableRulePacksAsync(dto, now, ruleAwareContext);
            var bundleRules = rulePacks.Select((p, i) => new EvidenceBundleRulePack
            {
                BundleRuleId = $"rule-{i + 1}",
                JurisdictionRulePackId = p.Id,
                RulePackVersion = p.Version,
                RuleCode = $"{p.JurisdictionCode}:{p.CourtSystem ?? "generic"}:{p.CaseType ?? "general"}",
                Name = p.Name,
                JurisdictionCode = p.JurisdictionCode,
                CourtSystem = p.CourtSystem,
                CaseType = p.CaseType,
                FilingMethod = p.FilingMethod,
                SourceCitation = p.SourceCitation,
                EffectiveFrom = p.EffectiveFrom,
                EffectiveTo = p.EffectiveTo
            }).ToList();

            var claims = BuildStructuredClaims(dto.Prompt, selectedChunks, bundleRules, maxClaims);
            var contractErrors = ValidateStructuredClaimsContract(claims, selectedChunks, bundleRules);
            if (contractErrors.Count > 0)
            {
                return BadRequest(new { message = "Structured claims contract validation failed.", errors = contractErrors });
            }

            var bundleId = $"rb_{Guid.NewGuid():N}";
            var correlationId = $"aidraft_{Guid.NewGuid():N}";
            var renderedText = RenderEvidenceLinkedDraftProse(dto.Prompt, claims, bundleRules);
            string? coverageReviewQueueItemId = null;
            if (ruleAwareContext?.CoverageResolution?.RequiresHumanReview == true && ruleAwareContext.ResolveRequest != null)
            {
                coverageReviewQueueItemId = await _jurisdictionRulesPlatformService.QueuePrecheckReviewIfRequiredAsync(
                    ruleAwareContext.CoverageResolution,
                    new JurisdictionPrecheckReviewRequest
                    {
                        MatterId = dto.MatterId?.Trim(),
                        ProviderKey = EvidenceDraftingProviderKey,
                        PacketName = dto.Title ?? "Evidence-Linked AI Draft",
                        JurisdictionCode = ruleAwareContext.ResolveRequest.JurisdictionCode,
                        CourtSystem = ruleAwareContext.ResolveRequest.CourtSystem,
                        CourtDivision = ruleAwareContext.ResolveRequest.CourtDivision,
                        Venue = ruleAwareContext.ResolveRequest.Venue,
                        CaseType = ruleAwareContext.ResolveRequest.CaseType,
                        FilingMethod = ruleAwareContext.ResolveRequest.FilingMethod,
                        Metadata = new Dictionary<string, string?>
                        {
                            ["draftPurpose"] = dto.Purpose,
                            ["mode"] = "evidence_linked_drafting"
                        }.Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
                         .ToDictionary(kv => kv.Key, kv => kv.Value!, StringComparer.Ordinal)
                    });
            }

            var createDto = new EvidenceLinkedDraftCreateDto
            {
                MatterId = normalizedMatterId,
                Title = string.IsNullOrWhiteSpace(dto.Title) ? "Evidence-Linked Draft (MVP)" : dto.Title!.Trim(),
                Purpose = string.IsNullOrWhiteSpace(dto.Purpose) ? "drafting" : dto.Purpose!.Trim(),
                JurisdictionContext = dto.JurisdictionContext,
                RenderedText = renderedText,
                Model = "deterministic-evidence-drafting-mvp",
                PromptTemplateVersion = "evidence-linked-v1",
                RetrievalBundleId = bundleId,
                CorrelationId = correlationId,
                RetrievalBundle = new
                {
                    bundleId,
                    createdAtUtc = now,
                    promptMeta = new
                    {
                        tokenCount = promptTokens.Count,
                        charLength = dto.Prompt.Length
                    },
                    selectedDocuments = docs.Select(d => new
                    {
                        d.Id,
                        d.MatterId,
                        version = versionMap.TryGetValue(d.Id, out var v) ? new { v.Id, v.Sha256, v.CreatedAt } : null
                    }),
                    chunks = selectedChunks.Select(c => new
                    {
                        chunkId = c.ChunkId,
                        documentId = c.DocumentId,
                        documentVersionId = c.DocumentVersionId,
                        sha256 = c.Sha256,
                        paragraphId = c.ParagraphId,
                        page = c.Page,
                        score = c.Score,
                        charLength = c.Text?.Length ?? 0
                    }),
                    rulePacks = bundleRules.Select(r => new
                    {
                        bundleRuleId = r.BundleRuleId,
                        jurisdictionRulePackId = r.JurisdictionRulePackId,
                        rulePackVersion = r.RulePackVersion,
                        ruleCode = r.RuleCode,
                        jurisdictionCode = r.JurisdictionCode,
                        courtSystem = r.CourtSystem,
                        caseType = r.CaseType,
                        filingMethod = r.FilingMethod,
                        effectiveFrom = r.EffectiveFrom,
                        effectiveTo = r.EffectiveTo
                    }),
                    contract = new { version = "evidence-linked-claims-v1" },
                    ruleAwareness = ruleAwareContext == null ? null : new
                    {
                        contextSource = ruleAwareContext.ContextSource,
                        coverage = ruleAwareContext.CoverageResolution == null ? null : new
                        {
                            ruleAwareContext.CoverageResolution.CoverageFound,
                            ruleAwareContext.CoverageResolution.RulePackFound,
                            ruleAwareContext.CoverageResolution.SupportLevel,
                            ruleAwareContext.CoverageResolution.ConfidenceLevel,
                            ruleAwareContext.CoverageResolution.ConfidenceScore,
                            ruleAwareContext.CoverageResolution.RequiresHumanReview,
                            ruleAwareContext.CoverageResolution.RulePackId,
                            ruleAwareContext.CoverageResolution.CoverageEntryId,
                            reasonCodes = ruleAwareContext.CoverageResolution.ReasonCodes
                        },
                        resolveRequest = ruleAwareContext.ResolveRequest,
                        coverageReviewQueueItemId
                    }
                },
                StructuredClaims = new
                {
                    contractVersion = "evidence-linked-claims-v1",
                    generatedAtUtc = now,
                    claims = claims
                },
                Claims = claims.Select((claim, i) => new EvidenceLinkedDraftClaimDto
                {
                    OrderIndex = i,
                    ClaimText = claim.ClaimText,
                    ClaimType = claim.ClaimType,
                    IsCritical = claim.IsCritical,
                    Confidence = claim.Confidence,
                    Status = "needs_review",
                    SupportSummary = "Pending verifier run.",
                    Metadata = new { claimRef = claim.ClaimRef, evidenceIds = claim.SupportingEvidenceIds, ruleCitationIds = claim.RuleCitationIds },
                    EvidenceLinks = claim.SupportingEvidenceIds
                        .Select(id => selectedChunks.FirstOrDefault(c => c.ChunkId == id))
                        .Where(c => c != null)
                        .Select(c => new EvidenceLinkedDraftEvidenceLinkDto
                        {
                            DocumentId = c!.DocumentId,
                            DocumentVersionId = c.DocumentVersionId,
                            Sha256 = c.Sha256,
                            Page = c.Page,
                            ParagraphId = c.ParagraphId,
                            Excerpt = SafeTruncate(c.Text, 500),
                            SupportStrength = c.Score >= 3 ? "strong" : c.Score >= 1 ? "medium" : "weak",
                            WhySupports = $"Retrieved from paragraph {c.ParagraphId}.",
                            Metadata = new { bundleChunkId = c.ChunkId, c.Score }
                        }).ToList(),
                    RuleCitations = claim.RuleCitationIds
                        .Select(id => bundleRules.FirstOrDefault(r => r.BundleRuleId == id))
                        .Where(r => r != null)
                        .Select(r => new EvidenceLinkedDraftRuleCitationDto
                        {
                            JurisdictionRulePackId = r!.JurisdictionRulePackId,
                            RulePackVersion = r.RulePackVersion,
                            RuleCode = r.RuleCode,
                            SourceCitation = r.SourceCitation,
                            CitationText = r.Name,
                            EffectiveAtUtc = r.EffectiveFrom,
                            Confidence = claim.Confidence,
                            Metadata = new { bundleRuleId = r.BundleRuleId, r.JurisdictionCode, r.CourtSystem, r.CaseType }
                        }).ToList()
                }).ToList()
            };

            var created = await CreateEvidenceLinkedDraft(createDto);
            if (created is not CreatedAtActionResult createdAction ||
                createdAction.RouteValues == null ||
                !createdAction.RouteValues.TryGetValue("id", out var outputIdObj) ||
                outputIdObj == null)
            {
                return StatusCode(500, new { message = "Failed to persist draft output." });
            }

            var outputId = outputIdObj.ToString() ?? string.Empty;
            EvidenceDraftVerificationSummary? verificationSummary = null;
            string? verificationError = null;
            var verifierStage = dto.AutoVerify == false ? "skipped" : "completed";
            if (dto.AutoVerify != false)
            {
                try
                {
                    verificationSummary = await VerifyEvidenceLinkedDraftOutputAsync(outputId, true);
                }
                catch (Exception ex)
                {
                    verificationError = SafeTruncate(ex.Message, 400);
                    verifierStage = "failed";
                    _logger.LogWarning(ex, "Evidence draft auto-verify failed for output {OutputId}", outputId);
                    var failureAccess = await GetDraftOutputAccessAsync(outputId, asNoTracking: false, requireOwnership: true);
                    if (failureAccess != null)
                    {
                        failureAccess.Output.Status = "review_required";
                        failureAccess.Output.UpdatedAt = DateTime.UtcNow;
                        if (failureAccess.Session != null && !string.Equals(failureAccess.Session.Status, "published", StringComparison.OrdinalIgnoreCase))
                        {
                            failureAccess.Session.Status = "review_required";
                            failureAccess.Session.UpdatedAt = DateTime.UtcNow;
                        }
                        await _context.SaveChangesAsync();
                    }
                    await _auditLogger.LogAsync(
                        HttpContext,
                        "ai.evidence_draft.verify.failed",
                        nameof(AiDraftOutput),
                        outputId,
                        verificationError);
                }
            }

            var graph = await LoadEvidenceLinkedDraftGraphAsync(outputId);
            return Ok(new
            {
                graph,
                pipeline = new
                {
                    stageA = "structured_claims",
                    stageB = "prose_render_with_claim_refs",
                    verifier = verifierStage,
                    contractVersion = "evidence-linked-claims-v1",
                    contractErrors,
                    retrieval = new { documentCount = docs.Count, chunkCount = selectedChunks.Count, rulePackCount = bundleRules.Count },
                    verificationSummary,
                    verificationError,
                    ruleAwareness = ruleAwareContext == null ? null : new
                    {
                        contextSource = ruleAwareContext.ContextSource,
                        coverageFound = ruleAwareContext.CoverageResolution?.CoverageFound,
                        rulePackFound = ruleAwareContext.CoverageResolution?.RulePackFound,
                        supportLevel = ruleAwareContext.CoverageResolution?.SupportLevel,
                        confidenceLevel = ruleAwareContext.CoverageResolution?.ConfidenceLevel,
                        confidenceScore = ruleAwareContext.CoverageResolution?.ConfidenceScore,
                        requiresHumanReview = ruleAwareContext.CoverageResolution?.RequiresHumanReview,
                        reasonCodes = ruleAwareContext.CoverageResolution?.ReasonCodes,
                        coverageReviewQueueItemId
                    }
                }
            });
        }

        // POST: api/ai/drafts/{id}/verify
        [HttpPost("drafts/{id}/verify")]
        public async System.Threading.Tasks.Task<ActionResult> VerifyEvidenceLinkedDraft(string id, [FromBody] EvidenceLinkedDraftVerifyDto? dto = null)
        {
            var access = await GetOwnedDraftOutputAccessAsync(id, asNoTracking: true);
            if (access == null)
            {
                return NotFound(new { message = "Draft output not found." });
            }

            var summary = await VerifyEvidenceLinkedDraftOutputAsync(id, dto?.CreateReviewQueueItems ?? true);
            var graph = await LoadEvidenceLinkedDraftGraphAsync(id);
            return Ok(new { graph, verificationSummary = summary });
        }

        // POST: api/ai/drafts/{draftId}/claims/{claimId}/review
        [HttpPost("drafts/{draftId}/claims/{claimId}/review")]
        public async System.Threading.Tasks.Task<ActionResult> ReviewEvidenceDraftClaim(
            string draftId,
            string claimId,
            [FromBody] EvidenceDraftClaimReviewDto dto)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.Action))
            {
                return BadRequest(new { message = "Action is required." });
            }

            var action = dto.Action.Trim().ToLowerInvariant();
            if (action is not ("approve" or "reject" or "rewrite"))
            {
                return BadRequest(new { message = "Action must be one of: approve, reject, rewrite." });
            }

            var access = await GetOwnedDraftOutputAccessAsync(draftId, asNoTracking: false);
            if (access == null)
            {
                return NotFound(new { message = "Draft output not found." });
            }
            var output = access.Output;

            var claim = await TenantScope(_context.AiDraftClaims)
                .FirstOrDefaultAsync(c => c.Id == claimId && c.DraftOutputId == draftId);
            if (claim == null)
            {
                return NotFound(new { message = "Claim not found for draft." });
            }

            if (action == "rewrite" && string.IsNullOrWhiteSpace(dto.RewrittenText))
            {
                return BadRequest(new { message = "RewrittenText is required for rewrite action." });
            }

            var now = DateTime.UtcNow;
            var reviewerId = User.FindFirst("sub")?.Value ??
                             User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrWhiteSpace(reviewerId))
            {
                return Unauthorized(new { message = "Authenticated user id is required." });
            }

            var previousText = claim.ClaimText;
            var previousStatus = claim.Status;

            if (action == "approve")
            {
                claim.Status = string.IsNullOrWhiteSpace(dto.StatusOverride)
                    ? "supported"
                    : dto.StatusOverride.Trim().ToLowerInvariant() switch
                    {
                        "supported" => "supported",
                        "partially_supported" => "partially_supported",
                        _ => "supported"
                    };
                claim.SupportSummary = AppendReviewerNoteToSupportSummary(claim.SupportSummary, reviewerId, dto.ReviewerNotes, "approved");
            }
            else if (action == "reject")
            {
                claim.Status = "unsupported";
                claim.SupportSummary = AppendReviewerNoteToSupportSummary(claim.SupportSummary, reviewerId, dto.ReviewerNotes, "rejected");
            }
            else // rewrite
            {
                claim.ClaimText = dto.RewrittenText!.Trim();
                claim.Status = "needs_review";
                claim.SupportSummary = AppendReviewerNoteToSupportSummary(claim.SupportSummary, reviewerId, dto.ReviewerNotes, "rewritten");
            }

            claim.MetadataJson = UpdateClaimMetadataForReview(claim.MetadataJson, new
            {
                action,
                reviewerId,
                reviewerNotes = dto.ReviewerNotes?.Trim(),
                approverReason = dto.ApproverReason?.Trim(),
                rewrittenText = action == "rewrite" ? dto.RewrittenText?.Trim() : null,
                previousStatus,
                previousText = action == "rewrite" ? previousText : null,
                atUtc = now
            });
            claim.UpdatedAt = now;

            await UpdateAiEvidenceReviewQueueForClaimReviewAsync(output.Id, claim, action, dto, reviewerId, now);
            await RecomputeDraftStatusesAsync(output.Id, now);
            await _context.SaveChangesAsync();

            await _auditLogger.LogAsync(
                HttpContext,
                "ai.evidence_draft.claim_review",
                nameof(AiDraftClaim),
                claim.Id,
                $"DraftOutputId={output.Id}, Action={action}, PrevStatus={previousStatus}, NewStatus={claim.Status}");

            var graph = await LoadEvidenceLinkedDraftGraphAsync(output.Id);
            return Ok(new { graph, reviewedClaimId = claim.Id, action });
        }

        // POST: api/ai/drafts/{id}/publish
        [HttpPost("drafts/{id}/publish")]
        public async System.Threading.Tasks.Task<ActionResult> PublishEvidenceLinkedDraft(string id, [FromBody] EvidenceDraftPublishDto? dto = null)
        {
            var access = await GetOwnedDraftOutputAccessAsync(id, asNoTracking: false);
            if (access == null)
            {
                return NotFound(new { message = "Draft output not found." });
            }
            var output = access.Output;

            var session = access.Session;
            var claims = await TenantScope(_context.AiDraftClaims).Where(c => c.DraftOutputId == id).OrderBy(c => c.OrderIndex).ToListAsync();

            var policy = NormalizePublishPolicy(dto?.Policy);
            var lowConfidenceThreshold = dto?.LowConfidenceThreshold is > 0m and <= 1m ? dto.LowConfidenceThreshold.Value : 0.55m;

            var unsupportedCriticalClaims = claims.Where(c => c.IsCritical && string.Equals(c.Status, "unsupported", StringComparison.OrdinalIgnoreCase)).ToList();
            var lowConfidenceCriticalClaims = claims.Where(c =>
                    c.IsCritical &&
                    ((c.Confidence ?? 0m) < lowConfidenceThreshold ||
                     string.Equals(c.Status, "needs_review", StringComparison.OrdinalIgnoreCase)))
                .ToList();

            var blockingReasons = new List<object>();
            if (policy == "block_on_unsupported_critical" && unsupportedCriticalClaims.Count > 0)
            {
                blockingReasons.Add(new
                {
                    code = "unsupported_critical_claims",
                    count = unsupportedCriticalClaims.Count,
                    claimIds = unsupportedCriticalClaims.Select(c => c.Id).ToList()
                });
            }
            if (policy == "block_on_low_confidence" && lowConfidenceCriticalClaims.Count > 0)
            {
                blockingReasons.Add(new
                {
                    code = "low_confidence_critical_claims",
                    threshold = lowConfidenceThreshold,
                    count = lowConfidenceCriticalClaims.Count,
                    claimIds = lowConfidenceCriticalClaims.Select(c => c.Id).ToList()
                });
            }

            if (blockingReasons.Count > 0)
            {
                await _auditLogger.LogAsync(
                    HttpContext,
                    "ai.evidence_draft.publish_blocked",
                    nameof(AiDraftOutput),
                    output.Id,
                    $"Policy={policy}, Reasons={JsonSerializer.Serialize(blockingReasons)}");

                return Conflict(new
                {
                    message = "Publish blocked by evidence-linked drafting policy.",
                    policy,
                    blockingReasons
                });
            }

            var now = DateTime.UtcNow;
            output.Status = "published";
            output.UpdatedAt = now;
            output.MetadataJson = UpdateOutputMetadataForPublish(output.MetadataJson, new
            {
                policy,
                lowConfidenceThreshold,
                publishedAtUtc = now,
                warnings = new
                {
                    unsupportedCriticalCount = unsupportedCriticalClaims.Count,
                    lowConfidenceCriticalCount = lowConfidenceCriticalClaims.Count
                }
            });
            if (session != null)
            {
                session.Status = "published";
                session.UpdatedAt = now;
            }

            await _context.SaveChangesAsync();
            await _auditLogger.LogAsync(
                HttpContext,
                "ai.evidence_draft.publish",
                nameof(AiDraftOutput),
                output.Id,
                $"Policy={policy}, UnsupportedCritical={unsupportedCriticalClaims.Count}, LowConfidenceCritical={lowConfidenceCriticalClaims.Count}");

            var graph = await LoadEvidenceLinkedDraftGraphAsync(output.Id);
            return Ok(new
            {
                graph,
                publish = new
                {
                    policy,
                    lowConfidenceThreshold,
                    warnings = new
                    {
                        unsupportedCriticalCount = unsupportedCriticalClaims.Count,
                        lowConfidenceCriticalCount = lowConfidenceCriticalClaims.Count
                    }
                }
            });
        }

        // GET: api/ai/drafts/{id}/evidence-export
        [HttpGet("drafts/{id}/evidence-export")]
        public async System.Threading.Tasks.Task<ActionResult> ExportEvidenceLinkedDraftGraph(string id)
        {
            var graph = await LoadEvidenceLinkedDraftGraphAsync(id);
            if (graph == null)
            {
                return NotFound();
            }

            var access = await GetOwnedDraftOutputAccessAsync(id, asNoTracking: true);
            if (access == null)
            {
                return NotFound();
            }
            var output = access.Output;

            var latestVerification = await TenantScope(_context.AiDraftVerificationRuns).AsNoTracking()
                .Where(v => v.DraftOutputId == id)
                .OrderByDescending(v => v.CreatedAt)
                .FirstOrDefaultAsync();

            return Ok(new
            {
                exportedAtUtc = DateTime.UtcNow,
                draftOutputId = id,
                correlationId = output.CorrelationId,
                graph,
                latestVerification,
                exportFormat = "evidence_linked_ai_draft_v1"
            });
        }

        // POST: api/ai/drafts/evidence-linked/batch-reverify
        [Authorize(Roles = "Admin,SecurityAdmin")]
        [HttpPost("drafts/evidence-linked/batch-reverify")]
        public async System.Threading.Tasks.Task<ActionResult> BatchReverifyEvidenceLinkedDrafts([FromBody] EvidenceDraftBatchReverifyDto? dto = null)
        {
            var now = DateTime.UtcNow;
            var limit = Math.Clamp(dto?.Limit ?? 25, 1, 100);
            var createReviewQueueItems = dto?.CreateReviewQueueItems ?? true;
            var requestedIds = (dto?.DraftOutputIds ?? new List<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.Ordinal)
                .Take(500)
                .ToList();

            List<string> targetIds;
            if (requestedIds.Count > 0)
            {
                targetIds = await (from output in TenantScope(_context.AiDraftOutputs).AsNoTracking()
                                   join session in TenantScope(_context.AiDraftSessions).AsNoTracking() on output.SessionId equals session.Id
                                   where requestedIds.Contains(output.Id)
                                   select output.Id)
                    .ToListAsync();
            }
            else
            {
                var days = Math.Clamp(dto?.Days ?? 30, 1, 365);
                var since = now.AddDays(-days);
                targetIds = await (from output in TenantScope(_context.AiDraftOutputs).AsNoTracking()
                                   join session in TenantScope(_context.AiDraftSessions).AsNoTracking() on output.SessionId equals session.Id
                                   where (output.UpdatedAt >= since || output.CreatedAt >= since)
                                   orderby output.UpdatedAt descending, output.CreatedAt descending
                                   select output.Id)
                    .Take(limit)
                    .ToListAsync();
            }

            var results = new List<object>();
            var failures = new List<object>();
            foreach (var outputId in targetIds.Take(limit))
            {
                try
                {
                    var summary = await VerifyEvidenceLinkedDraftOutputAsync(
                        outputId,
                        createReviewQueueItems,
                        requireOwnership: false);
                    results.Add(new
                    {
                        draftOutputId = outputId,
                        status = "ok",
                        summary
                    });
                }
                catch (Exception ex)
                {
                    failures.Add(new
                    {
                        draftOutputId = outputId,
                        error = SafeTruncate(ex.Message, 600)
                    });
                }
            }

            return Ok(new
            {
                processedAtUtc = now,
                requestedCount = requestedIds.Count > 0 ? requestedIds.Count : (int?)null,
                selectedCount = targetIds.Count,
                processedCount = results.Count,
                failedCount = failures.Count,
                createReviewQueueItems,
                results,
                failures
            });
        }

        // GET: api/ai/drafts/evidence-linked/metrics
        [Authorize(Roles = "Admin,SecurityAdmin")]
        [HttpGet("drafts/evidence-linked/metrics")]
        public async System.Threading.Tasks.Task<ActionResult> GetEvidenceLinkedDraftingMetrics([FromQuery] int? days = null)
        {
            var windowDays = Math.Clamp(days ?? 30, 1, 365);
            var since = DateTime.UtcNow.AddDays(-windowDays);
            var outputWindowQuery = from output in TenantScope(_context.AiDraftOutputs).AsNoTracking()
                                    join session in TenantScope(_context.AiDraftSessions).AsNoTracking() on output.SessionId equals session.Id
                                    where output.CreatedAt >= since || output.UpdatedAt >= since
                                    select output;

            var claimsQuery = from claim in TenantScope(_context.AiDraftClaims).AsNoTracking()
                              join output in outputWindowQuery on claim.DraftOutputId equals output.Id
                              select claim;
            var totalClaims = await claimsQuery.CountAsync();
            var supportedClaims = await claimsQuery.CountAsync(c => c.Status != null && c.Status.ToLower() == "supported");
            var partiallySupportedClaims = await claimsQuery.CountAsync(c => c.Status != null && c.Status.ToLower() == "partially_supported");
            var unsupportedClaims = await claimsQuery.CountAsync(c => c.Status != null && c.Status.ToLower() == "unsupported");
            var needsReviewClaims = await claimsQuery.CountAsync(c => c.Status != null && c.Status.ToLower() == "needs_review");
            var criticalClaims = await claimsQuery.CountAsync(c => c.IsCritical);
            var unsupportedCriticalClaims = await claimsQuery.CountAsync(c => c.IsCritical && c.Status != null && c.Status.ToLower() == "unsupported");

            var coveragePct = totalClaims == 0
                ? 0m
                : Math.Round(((decimal)(supportedClaims + partiallySupportedClaims) / totalClaims) * 100m, 2, MidpointRounding.AwayFromZero);

            var unsupportedTrendRows = await claimsQuery
                .GroupBy(c => c.UpdatedAt.Date)
                .OrderBy(g => g.Key)
                .Select(g => new
                {
                    date = g.Key,
                    unsupported = g.Count(c => c.Status != null && c.Status.ToLower() == "unsupported"),
                    unsupportedCritical = g.Count(c => c.IsCritical && c.Status != null && c.Status.ToLower() == "unsupported"),
                    needsReview = g.Count(c => c.Status != null && c.Status.ToLower() == "needs_review")
                })
                .ToListAsync();
            var unsupportedTrend = unsupportedTrendRows.Select(g => new
            {
                date = g.date.ToString("yyyy-MM-dd"),
                g.unsupported,
                g.unsupportedCritical,
                g.needsReview
            }).ToList();

            var draftsInReview = await (from output in TenantScope(_context.AiDraftOutputs).AsNoTracking()
                                        join session in TenantScope(_context.AiDraftSessions).AsNoTracking() on output.SessionId equals session.Id
                                        where (output.CreatedAt >= since || output.UpdatedAt >= since) &&
                                              output.Status != null &&
                                              output.Status.ToLower() == "review_required"
                                        select output.Id).CountAsync();

            var openReviewItems = await (from i in TenantScope(_context.IntegrationReviewQueueItems).AsNoTracking()
                                         join c in claimsQuery on i.SourceId equals c.Id
                                         where i.ProviderKey == EvidenceDraftingProviderKey &&
                                               (i.ItemType == EvidenceDraftingReviewItemType || i.ItemType == EvidenceDraftingRuleDriftReviewItemType) &&
                                               (i.Status == null || (i.Status != "resolved" && i.Status != "closed"))
                                         select new
                                         {
                                             i.CreatedAt
                                         }).ToListAsync();
            var reviewerBurden = new
            {
                openReviewQueueItems = openReviewItems.Count,
                reviewQueueBacklogAgeHoursP50 = Percentile(
                    openReviewItems.Select(i => Math.Max(0d, (DateTime.UtcNow - i.CreatedAt).TotalHours)).ToList(),
                    0.50),
                reviewQueueBacklogAgeHoursP90 = Percentile(
                    openReviewItems.Select(i => Math.Max(0d, (DateTime.UtcNow - i.CreatedAt).TotalHours)).ToList(),
                    0.90),
                draftsInReview,
                needsReviewClaims,
                unsupportedCriticalClaims
            };

            var latestRuns = await (from run in TenantScope(_context.AiDraftVerificationRuns).AsNoTracking()
                                    join output in outputWindowQuery on run.DraftOutputId equals output.Id
                                    orderby run.CreatedAt descending
                                    select run)
                .Take(1000)
                .ToListAsync();
            latestRuns = latestRuns
                .GroupBy(v => v.DraftOutputId)
                .Select(g => g.First())
                .ToList();
            var latestVerificationStats = BuildEvidenceDraftingLatestVerificationMetrics(latestRuns);

            return Ok(new
            {
                window = new { days = windowDays, sinceUtc = since, untilUtc = DateTime.UtcNow },
                claimCoverage = new
                {
                    totalClaims,
                    criticalClaims,
                    supportedClaims,
                    partiallySupportedClaims,
                    unsupportedClaims,
                    needsReviewClaims,
                    unsupportedCriticalClaims,
                    coveragePct
                },
                unsupportedTrend,
                reviewerBurden,
                verifierQuality = latestVerificationStats,
                dataQuality = new
                {
                    unsupportedTrendBasis = "claim_status_by_claim_updatedAt",
                    reviewerBurdenBasis = "review_queue_plus_current_claim_status",
                    verifierQualityBasis = "latest_verification_run_per_output"
                }
            });
        }

        private async System.Threading.Tasks.Task<EvidenceDraftVerificationSummary> VerifyEvidenceLinkedDraftOutputAsync(
            string outputId,
            bool createReviewQueueItems,
            bool requireOwnership = true)
        {
            var access = await GetDraftOutputAccessAsync(outputId, asNoTracking: false, requireOwnership: requireOwnership);
            if (access == null)
            {
                throw new KeyNotFoundException("Draft output not found.");
            }

            var output = access.Output;
            var session = access.Session;
            var sessionJurisdictionContext = ParseJurisdictionContext(session?.JurisdictionContextJson);
            var ruleAwareContext = await BuildRuleAwareContextAsync(session?.MatterId, sessionJurisdictionContext, DateTime.UtcNow);
            var claims = await TenantScope(_context.AiDraftClaims).Where(c => c.DraftOutputId == outputId).OrderBy(c => c.OrderIndex).ToListAsync();
            var claimIds = claims.Select(c => c.Id).ToList();
            var evidenceLinks = claimIds.Count == 0 ? new List<AiDraftEvidenceLink>() : await TenantScope(_context.AiDraftEvidenceLinks).Where(e => claimIds.Contains(e.ClaimId)).ToListAsync();
            var ruleCitations = claimIds.Count == 0 ? new List<AiDraftRuleCitation>() : await TenantScope(_context.AiDraftRuleCitations).Where(r => claimIds.Contains(r.ClaimId)).ToListAsync();
            var evidenceLookup = evidenceLinks.ToLookup(e => e.ClaimId, StringComparer.Ordinal);
            var citationLookup = ruleCitations.ToLookup(r => r.ClaimId, StringComparer.Ordinal);
            var evidenceDocIds = evidenceLinks
                .Where(e => !string.IsNullOrWhiteSpace(e.DocumentId))
                .Select(e => e.DocumentId!)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            var latestDocumentVersionMap = evidenceDocIds.Count == 0
                ? new Dictionary<string, DocumentVersion>(StringComparer.Ordinal)
                : (await TenantScope(_context.DocumentVersions).AsNoTracking()
                    .Where(v => evidenceDocIds.Contains(v.DocumentId))
                    .OrderByDescending(v => v.CreatedAt)
                    .ToListAsync())
                    .GroupBy(v => v.DocumentId)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

            var bundle = ParseRetrievalBundle(output.RetrievalBundleJson);
            var chunkMap = bundle.Chunks.ToDictionary(c => c.ChunkId, StringComparer.Ordinal);
            var ruleMap = bundle.RulePacks.ToDictionary(r => r.BundleRuleId, StringComparer.Ordinal);
            var contradictionMap = DetectClaimContradictions(claims);

            var now = DateTime.UtcNow;
            var unsupportedCritical = new HashSet<string>(StringComparer.Ordinal);
            var staleCriticalClaims = new HashSet<string>(StringComparer.Ordinal);
            var claimResults = new List<object>();
            var staleCitationCount = 0;
            var contradictionCandidateCount = 0;
            var contradictionClaims = new HashSet<string>(StringComparer.Ordinal);
            var outdatedSourceLinkCount = 0;
            var outdatedSourceClaims = new HashSet<string>(StringComparer.Ordinal);
            var evidenceSuggestionCount = 0;
            var citationStabilityScores = new List<decimal>();
            var lowCitationStabilityClaims = 0;
            string? coverageReviewQueueItemId = null;
            if (createReviewQueueItems && ruleAwareContext?.CoverageResolution?.RequiresHumanReview == true && ruleAwareContext.ResolveRequest != null)
            {
                coverageReviewQueueItemId = await _jurisdictionRulesPlatformService.QueuePrecheckReviewIfRequiredAsync(
                    ruleAwareContext.CoverageResolution,
                    new JurisdictionPrecheckReviewRequest
                    {
                        MatterId = session?.MatterId,
                        ProviderKey = EvidenceDraftingProviderKey,
                        PacketName = session?.Title ?? "Evidence-Linked AI Draft",
                        JurisdictionCode = ruleAwareContext.ResolveRequest.JurisdictionCode,
                        CourtSystem = ruleAwareContext.ResolveRequest.CourtSystem,
                        CourtDivision = ruleAwareContext.ResolveRequest.CourtDivision,
                        Venue = ruleAwareContext.ResolveRequest.Venue,
                        CaseType = ruleAwareContext.ResolveRequest.CaseType,
                        FilingMethod = ruleAwareContext.ResolveRequest.FilingMethod,
                        Metadata = new Dictionary<string, string>
                        {
                            ["draftOutputId"] = output.Id,
                            ["verificationMode"] = "evidence_linked_verifier_v1"
                        }
                    });
            }

            foreach (var claim in claims)
            {
                var links = evidenceLookup[claim.Id].ToList();
                var cites = citationLookup[claim.Id].ToList();
                var validEvidence = 0;
                var validRules = 0;
                var staleCitationDetected = false;
                var evidenceMismatches = new List<object>();
                var ruleMismatches = new List<object>();
                var outdatedSourceMismatches = new List<object>();
                var crossCheckMismatches = contradictionMap.TryGetValue(claim.Id, out var contradictionRows)
                    ? contradictionRows.Cast<object>().ToList()
                    : new List<object>();
                var outdatedSourceDetected = false;
                if (crossCheckMismatches.Count > 0)
                {
                    contradictionClaims.Add(claim.Id);
                    contradictionCandidateCount += crossCheckMismatches.Count;
                }

                foreach (var link in links)
                {
                    var meta = TryParseJsonDictionary(link.MetadataJson);
                    var bundleChunkId = GetJsonString(meta, "bundleChunkId");
                    if (!string.IsNullOrWhiteSpace(bundleChunkId) && chunkMap.TryGetValue(bundleChunkId, out var bundleChunk))
                    {
                        var excerptOk = string.IsNullOrWhiteSpace(bundleChunk.Text) || HasExcerptOverlap(link.Excerpt, bundleChunk.Text);
                        var versionOk = string.IsNullOrWhiteSpace(link.DocumentVersionId) || string.Equals(link.DocumentVersionId, bundleChunk.DocumentVersionId, StringComparison.Ordinal);
                        var shaOk = string.IsNullOrWhiteSpace(link.Sha256) || string.IsNullOrWhiteSpace(bundleChunk.Sha256) || string.Equals(link.Sha256, bundleChunk.Sha256, StringComparison.OrdinalIgnoreCase);
                        var documentOk = string.IsNullOrWhiteSpace(link.DocumentId) || string.Equals(link.DocumentId, bundleChunk.DocumentId, StringComparison.Ordinal);
                        var paragraphOk = string.IsNullOrWhiteSpace(link.ParagraphId) || string.Equals(link.ParagraphId, bundleChunk.ParagraphId, StringComparison.Ordinal);
                        if (excerptOk && versionOk && shaOk && documentOk && paragraphOk)
                        {
                            validEvidence++;
                        }
                        else
                        {
                            if (!excerptOk) evidenceMismatches.Add(new { type = "excerpt_overlap_failed", linkId = link.Id, claimId = claim.Id, bundleChunkId });
                            if (!versionOk) evidenceMismatches.Add(new { type = "document_version_pin_mismatch", linkId = link.Id, claimId = claim.Id, bundleChunkId, expected = link.DocumentVersionId, actual = bundleChunk.DocumentVersionId });
                            if (!shaOk) evidenceMismatches.Add(new { type = "sha256_pin_mismatch", linkId = link.Id, claimId = claim.Id, bundleChunkId, expected = link.Sha256, actual = bundleChunk.Sha256 });
                            if (!documentOk) evidenceMismatches.Add(new { type = "document_id_mismatch", linkId = link.Id, claimId = claim.Id, bundleChunkId, expected = link.DocumentId, actual = bundleChunk.DocumentId });
                            if (!paragraphOk) evidenceMismatches.Add(new { type = "paragraph_id_mismatch", linkId = link.Id, claimId = claim.Id, bundleChunkId, expected = link.ParagraphId, actual = bundleChunk.ParagraphId });
                        }
                    }
                    else
                    {
                        evidenceMismatches.Add(new { type = "bundle_membership_missing", linkId = link.Id, claimId = claim.Id, bundleChunkId, link.DocumentId, link.ParagraphId });
                    }

                    if (!string.IsNullOrWhiteSpace(link.DocumentId) &&
                        latestDocumentVersionMap.TryGetValue(link.DocumentId!, out var latestVersion))
                    {
                        var versionMismatch = !string.IsNullOrWhiteSpace(link.DocumentVersionId) &&
                                              !string.Equals(link.DocumentVersionId, latestVersion.Id, StringComparison.Ordinal);
                        var shaMismatch = !string.IsNullOrWhiteSpace(link.Sha256) &&
                                          !string.IsNullOrWhiteSpace(latestVersion.Sha256) &&
                                          !string.Equals(link.Sha256, latestVersion.Sha256, StringComparison.OrdinalIgnoreCase);
                        if (versionMismatch || shaMismatch)
                        {
                            outdatedSourceDetected = true;
                            outdatedSourceLinkCount++;
                            outdatedSourceMismatches.Add(new
                            {
                                type = "outdated_source_detected",
                                linkId = link.Id,
                                claimId = claim.Id,
                                link.DocumentId,
                                linkedDocumentVersionId = link.DocumentVersionId,
                                latestDocumentVersionId = latestVersion.Id,
                                linkedSha256 = link.Sha256,
                                latestSha256 = latestVersion.Sha256,
                                latestVersionCreatedAtUtc = latestVersion.CreatedAt
                            });
                        }
                    }
                }

                foreach (var cite in cites)
                {
                    var meta = TryParseJsonDictionary(cite.MetadataJson);
                    var bundleRuleId = GetJsonString(meta, "bundleRuleId");
                    if (!string.IsNullOrWhiteSpace(bundleRuleId) && ruleMap.TryGetValue(bundleRuleId, out var bundleRule))
                    {
                        if (!cite.RulePackVersion.HasValue || cite.RulePackVersion.Value == bundleRule.RulePackVersion)
                        {
                            validRules++;
                        }
                        else
                        {
                            ruleMismatches.Add(new { type = "rule_pack_version_pin_mismatch", citationId = cite.Id, claimId = claim.Id, bundleRuleId, expected = cite.RulePackVersion, actual = bundleRule.RulePackVersion });
                        }
                    }
                    else
                    {
                        ruleMismatches.Add(new { type = "rule_bundle_membership_missing", citationId = cite.Id, claimId = claim.Id, bundleRuleId, cite.JurisdictionRulePackId, cite.RulePackVersion });
                    }

                    if (ruleAwareContext?.CoverageResolution?.RulePackFound == true && ruleAwareContext.CoverageResolution.RulePack != null)
                    {
                        var current = ruleAwareContext.CoverageResolution.RulePack;
                        if (!string.IsNullOrWhiteSpace(cite.JurisdictionRulePackId) &&
                            (!string.Equals(cite.JurisdictionRulePackId, current.Id, StringComparison.Ordinal) ||
                             (cite.RulePackVersion.HasValue && cite.RulePackVersion.Value != current.Version)))
                        {
                            staleCitationDetected = true;
                            ruleMismatches.Add(new
                            {
                                type = "stale_citation",
                                citationId = cite.Id,
                                claimId = claim.Id,
                                expectedRulePackId = current.Id,
                                expectedRulePackVersion = current.Version,
                                actualRulePackId = cite.JurisdictionRulePackId,
                                actualRulePackVersion = cite.RulePackVersion
                            });
                        }
                    }
                }

                var requiresRule = ClaimRequiresRuleCitation(claim, ruleAwareContext?.CoverageResolution != null);
                var citationStabilityScore = ComputeCitationStabilityScore(requiresRule, cites.Count, validRules, staleCitationDetected, ruleMismatches.Count);
                if (citationStabilityScore.HasValue)
                {
                    citationStabilityScores.Add(citationStabilityScore.Value);
                    if (citationStabilityScore.Value < 0.60m)
                    {
                        lowCitationStabilityClaims++;
                    }
                }
                var status = validEvidence == 0
                    ? "unsupported"
                    : (requiresRule && validRules == 0)
                        ? (ruleAwareContext?.CoverageResolution != null ? "unsupported" : "partially_supported")
                        : ((claim.Confidence ?? 0m) < 0.55m ? "needs_review" : "supported");

                if (staleCitationDetected && !string.Equals(status, "unsupported", StringComparison.OrdinalIgnoreCase))
                {
                    status = "needs_review";
                    staleCitationCount++;
                    if (claim.IsCritical)
                    {
                        staleCriticalClaims.Add(claim.Id);
                    }
                }

                if (outdatedSourceDetected && !string.Equals(status, "unsupported", StringComparison.OrdinalIgnoreCase))
                {
                    status = "needs_review";
                    outdatedSourceClaims.Add(claim.Id);
                }

                if (crossCheckMismatches.Count > 0 &&
                    string.Equals(status, "supported", StringComparison.OrdinalIgnoreCase))
                {
                    status = "needs_review";
                }

                var evidenceSuggestions = BuildEvidenceSuggestionsForClaim(claim, links, bundle.Chunks, 3);
                evidenceSuggestionCount += evidenceSuggestions.Count;

                claim.Status = status;
                claim.SupportSummary = $"evidence {validEvidence}/{links.Count}; rules {validRules}/{cites.Count}; confidence {(claim.Confidence ?? 0m):0.00}" +
                                       (staleCitationDetected ? "; stale_citation detected" : string.Empty) +
                                       (outdatedSourceDetected ? "; outdated_source detected" : string.Empty) +
                                       (crossCheckMismatches.Count > 0 ? "; contradiction candidate" : string.Empty) +
                                       ((ruleAwareContext?.CoverageResolution?.RequiresHumanReview == true) ? "; jurisdiction coverage review required" : string.Empty);
                claim.UpdatedAt = now;

                if (claim.IsCritical && string.Equals(status, "unsupported", StringComparison.OrdinalIgnoreCase))
                {
                    unsupportedCritical.Add(claim.Id);
                }

                claimResults.Add(new
                {
                    claimId = claim.Id,
                    status,
                    validEvidence,
                    validRules,
                    requiresRule,
                    staleCitationDetected,
                    citationStabilityScore,
                    evidenceMismatches,
                    ruleMismatches,
                    outdatedSourceMismatches,
                    crossCheckMismatches,
                    evidenceSuggestions
                });
            }

            output.Status = unsupportedCritical.Count > 0 ? "review_required" : "verified";
            output.UpdatedAt = now;
            if (session != null)
            {
                session.Status = unsupportedCritical.Count > 0 ? "review_required" : "generated";
                session.UpdatedAt = now;
            }

            var summary = new EvidenceDraftVerificationSummary
            {
                DraftOutputId = outputId,
                ClaimCount = claims.Count,
                CriticalClaimCount = claims.Count(c => c.IsCritical),
                SupportedCount = claims.Count(c => c.Status == "supported"),
                PartiallySupportedCount = claims.Count(c => c.Status == "partially_supported"),
                UnsupportedCount = claims.Count(c => c.Status == "unsupported"),
                NeedsReviewCount = claims.Count(c => c.Status == "needs_review"),
                UnsupportedCriticalClaimCount = unsupportedCritical.Count,
                RuleAwarenessApplied = ruleAwareContext?.CoverageResolution != null,
                CoverageFound = ruleAwareContext?.CoverageResolution?.CoverageFound,
                RulePackFound = ruleAwareContext?.CoverageResolution?.RulePackFound,
                CoverageRequiresHumanReview = ruleAwareContext?.CoverageResolution?.RequiresHumanReview,
                CoverageConfidenceScore = ruleAwareContext?.CoverageResolution?.ConfidenceScore,
                CoverageReviewQueueItemId = coverageReviewQueueItemId,
                StaleCitationCount = staleCitationCount,
                StaleCriticalClaimCount = staleCriticalClaims.Count,
                CurrentRulePackId = ruleAwareContext?.CoverageResolution?.RulePackId,
                CurrentRulePackVersion = ruleAwareContext?.CoverageResolution?.RulePack?.Version,
                ContradictionCandidateCount = contradictionCandidateCount,
                ContradictionClaimCount = contradictionClaims.Count,
                OutdatedSourceLinkCount = outdatedSourceLinkCount,
                OutdatedSourceClaimCount = outdatedSourceClaims.Count,
                AverageCitationStabilityScore = citationStabilityScores.Count == 0
                    ? null
                    : Math.Round(citationStabilityScores.Average(), 3, MidpointRounding.AwayFromZero),
                LowCitationStabilityClaimCount = lowCitationStabilityClaims,
                EvidenceSuggestionCount = evidenceSuggestionCount
            };

            _context.AiDraftVerificationRuns.Add(new AiDraftVerificationRun
            {
                DraftOutputId = outputId,
                VerifierVersion = summary.VerifierVersion,
                Status = "completed",
                CorrelationId = output.CorrelationId,
                ResultJson = JsonSerializer.Serialize(new { summary, claims = claimResults }),
                MetadataJson = JsonSerializer.Serialize(new
                {
                    dataQuality = new
                    {
                        bundleParsed = bundle.IsParsed,
                        advancedVerification = "phase4_mvp",
                        contradictionHeuristic = "negation_overlap_v1",
                        evidenceSuggestions = "bundle_chunk_similarity_v1"
                    }
                }),
                CreatedAt = now
            });

            if (createReviewQueueItems)
            {
                await UpsertAiEvidenceReviewItemsAsync(output, claims, unsupportedCritical, now);
                await UpsertAiRuleDriftReviewItemsAsync(output, claims, staleCriticalClaims, now);
            }

            await _context.SaveChangesAsync();
            return summary;
        }

        private async System.Threading.Tasks.Task<List<JurisdictionRulePack>> ResolveApplicableRulePacksAsync(
            EvidenceLinkedDraftGenerateDto dto,
            DateTime now,
            AiDraftRuleAwareContext? ruleAwareContext)
        {
            var query = TenantScope(_context.JurisdictionRulePacks).AsNoTracking().AsQueryable();
            query = query.Where(r =>
                r.Status == "published" &&
                r.EffectiveFrom <= now &&
                (!r.EffectiveTo.HasValue || r.EffectiveTo.Value >= now));

            var jc = dto.JurisdictionContext;
            if (!string.IsNullOrWhiteSpace(jc?.JurisdictionCode))
            {
                query = query.Where(r => r.JurisdictionCode == jc.JurisdictionCode);
            }
            if (!string.IsNullOrWhiteSpace(jc?.CourtSystem))
            {
                query = query.Where(r => r.CourtSystem == null || r.CourtSystem == jc.CourtSystem);
            }
            if (!string.IsNullOrWhiteSpace(jc?.CaseType))
            {
                query = query.Where(r => r.CaseType == null || r.CaseType == jc.CaseType);
            }
            if (!string.IsNullOrWhiteSpace(jc?.FilingMethod))
            {
                query = query.Where(r => r.FilingMethod == null || r.FilingMethod == jc.FilingMethod);
            }

            var packs = await query.OrderByDescending(r => r.EffectiveFrom).ThenByDescending(r => r.Version).Take(24).ToListAsync();

            var preferred = ruleAwareContext?.CoverageResolution?.RulePack;
            if (preferred != null)
            {
                packs = packs
                    .Where(p => !string.Equals(p.Id, preferred.Id, StringComparison.Ordinal))
                    .Prepend(preferred)
                    .ToList();
            }

            string? norm(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
            var ctx = ruleAwareContext?.ResolveRequest;
            var ranked = packs
                .Select(p =>
                {
                    var score = 0;
                    if (!string.IsNullOrWhiteSpace(ctx?.JurisdictionCode) &&
                        string.Equals(norm(p.JurisdictionCode), norm(ctx!.JurisdictionCode), StringComparison.OrdinalIgnoreCase))
                    {
                        score += 10;
                    }
                    if (!string.IsNullOrWhiteSpace(ctx?.CourtSystem))
                    {
                        score += string.Equals(norm(p.CourtSystem), norm(ctx!.CourtSystem), StringComparison.OrdinalIgnoreCase) ? 8 :
                                 string.IsNullOrWhiteSpace(p.CourtSystem) ? 2 : 0;
                    }
                    if (!string.IsNullOrWhiteSpace(ctx?.CaseType))
                    {
                        score += string.Equals(norm(p.CaseType), norm(ctx!.CaseType), StringComparison.OrdinalIgnoreCase) ? 6 :
                                 string.IsNullOrWhiteSpace(p.CaseType) ? 1 : 0;
                    }
                    if (!string.IsNullOrWhiteSpace(ctx?.FilingMethod))
                    {
                        score += string.Equals(norm(p.FilingMethod), norm(ctx!.FilingMethod), StringComparison.OrdinalIgnoreCase) ? 4 :
                                 string.IsNullOrWhiteSpace(p.FilingMethod) ? 1 : 0;
                    }
                    return new { pack = p, score };
                })
                .OrderByDescending(x => x.score)
                .ThenByDescending(x => x.pack.EffectiveFrom)
                .ThenByDescending(x => x.pack.Version)
                .Take(12)
                .Select(x => x.pack)
                .ToList();

            return ranked;
        }

        private static List<EvidenceStructuredClaim> BuildStructuredClaims(
            string prompt,
            IReadOnlyList<EvidenceBundleChunk> chunks,
            IReadOnlyList<EvidenceBundleRulePack> rulePacks,
            int maxClaims)
        {
            var claims = new List<EvidenceStructuredClaim>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var proceduralPrompt = Regex.IsMatch(prompt, "(court|rule|deadline|filing|motion|hearing)", RegexOptions.IgnoreCase);

            foreach (var chunk in chunks)
            {
                foreach (var sentence in ExtractCandidateSentences(chunk.Text))
                {
                    var normalized = NormalizeForEvidenceDrafting(sentence);
                    if (normalized.Length < 30 || !seen.Add(normalized))
                    {
                        continue;
                    }

                    var claimType = ClassifyClaimType(sentence, proceduralPrompt);
                    var needsRule = string.Equals(claimType, "rule_based", StringComparison.Ordinal) || string.Equals(claimType, "procedural", StringComparison.Ordinal);
                    var selectedRuleId = needsRule ? SelectBestRuleBundleId(sentence, rulePacks) : null;
                    claims.Add(new EvidenceStructuredClaim
                    {
                        ClaimRef = $"CLM-{claims.Count + 1}",
                        ClaimText = sentence.Trim(),
                        ClaimType = claimType,
                        IsCritical = IsCriticalClaim(sentence, claimType),
                        Confidence = chunk.Score >= 3 ? 0.86m : chunk.Score >= 1 ? 0.72m : 0.58m,
                        SupportingEvidenceIds = new List<string> { chunk.ChunkId },
                        RuleCitationIds = !string.IsNullOrWhiteSpace(selectedRuleId) ? new List<string> { selectedRuleId } : new List<string>()
                    });

                    if (claims.Count >= maxClaims)
                    {
                        return claims;
                    }
                }
            }

            if (claims.Count == 0)
            {
                claims.Add(new EvidenceStructuredClaim
                {
                    ClaimRef = "CLM-1",
                    ClaimText = $"Draft request: {prompt.Trim()}",
                    ClaimType = "other",
                    IsCritical = true,
                    Confidence = 0.40m,
                    SupportingEvidenceIds = chunks.Take(1).Select(c => c.ChunkId).ToList(),
                    RuleCitationIds = new List<string>()
                });
            }

            return claims;
        }

        private static List<string> ValidateStructuredClaimsContract(
            IReadOnlyList<EvidenceStructuredClaim> claims,
            IReadOnlyList<EvidenceBundleChunk> chunks,
            IReadOnlyList<EvidenceBundleRulePack> rulePacks)
        {
            var errors = new List<string>();
            var chunkIds = chunks.Select(c => c.ChunkId).ToHashSet(StringComparer.Ordinal);
            var ruleIds = rulePacks.Select(r => r.BundleRuleId).ToHashSet(StringComparer.Ordinal);

            if (claims.Count == 0)
            {
                errors.Add("claims[] must contain at least one claim.");
                return errors;
            }

            foreach (var claim in claims)
            {
                if (string.IsNullOrWhiteSpace(claim.ClaimText))
                {
                    errors.Add($"{claim.ClaimRef}: claimText is required.");
                }
                if (claim.SupportingEvidenceIds == null || claim.SupportingEvidenceIds.Count == 0)
                {
                    errors.Add($"{claim.ClaimRef}: at least one supportingEvidenceId is required.");
                }
                foreach (var eid in claim.SupportingEvidenceIds ?? new List<string>())
                {
                    if (!chunkIds.Contains(eid))
                    {
                        errors.Add($"{claim.ClaimRef}: unknown evidence id '{eid}'.");
                    }
                }
                foreach (var rid in claim.RuleCitationIds ?? new List<string>())
                {
                    if (!ruleIds.Contains(rid))
                    {
                        errors.Add($"{claim.ClaimRef}: unknown rule citation id '{rid}'.");
                    }
                }
            }

            return errors;
        }

        private static string? SelectBestRuleBundleId(string claimText, IReadOnlyList<EvidenceBundleRulePack> rulePacks)
        {
            if (rulePacks == null || rulePacks.Count == 0 || string.IsNullOrWhiteSpace(claimText))
            {
                return null;
            }

            var claimTokens = TokenizeForEvidenceDrafting(claimText);
            if (claimTokens.Count == 0)
            {
                return rulePacks[0].BundleRuleId;
            }

            var best = rulePacks
                .Select(r =>
                {
                    var corpus = $"{r.Name} {r.RuleCode} {r.JurisdictionCode} {r.CourtSystem} {r.CaseType} {r.FilingMethod}";
                    var tokens = TokenizeForEvidenceDrafting(corpus);
                    var overlap = claimTokens.Count(t => tokens.Contains(t));
                    return new { r.BundleRuleId, overlap };
                })
                .OrderByDescending(x => x.overlap)
                .FirstOrDefault();

            if (best == null)
            {
                return null;
            }

            return best.overlap > 0 ? best.BundleRuleId : rulePacks[0].BundleRuleId;
        }

        private static string RenderEvidenceLinkedDraftProse(
            string prompt,
            IReadOnlyList<EvidenceStructuredClaim> claims,
            IReadOnlyList<EvidenceBundleRulePack> rulePacks)
        {
            var lines = new List<string>
            {
                "Evidence-Linked Draft (MVP)",
                "",
                $"Draft objective: {prompt.Trim()}",
                "",
                "Key claims:"
            };

            lines.AddRange(claims.Select(c => $"- {c.ClaimText} [{c.ClaimRef}]"));

            if (rulePacks.Count > 0)
            {
                lines.Add("");
                lines.Add("Applicable rule pack context considered:");
                lines.AddRange(rulePacks.Take(3).Select(r => $"- {r.Name} ({r.JurisdictionCode}{(string.IsNullOrWhiteSpace(r.CourtSystem) ? "" : $" / {r.CourtSystem}")})"));
            }

            lines.Add("");
            lines.Add("Review note: Unsupported or low-confidence claims are flagged in the evidence drawer before use.");
            return string.Join(Environment.NewLine, lines);
        }

        private static List<string> SplitIntoParagraphs(string text)
        {
            var parts = Regex.Split(text ?? string.Empty, @"\r?\n\r?\n+|\r?\n+")
                .Select(p => Regex.Replace(p, @"\s+", " ").Trim())
                .Where(p => p.Length >= 20)
                .ToList();
            return parts.Count > 0 ? parts : ExtractCandidateSentences(text ?? string.Empty).ToList();
        }

        private static IEnumerable<string> ExtractCandidateSentences(string text)
        {
            foreach (var raw in Regex.Split(text ?? string.Empty, @"(?<=[\.\!\?])\s+|\r?\n+"))
            {
                var s = Regex.Replace(raw ?? string.Empty, @"\s+", " ").Trim();
                if (s.Length >= 20)
                {
                    yield return s;
                }
            }
        }

        private static int ScoreParagraph(string paragraph, ISet<string> promptTokens)
        {
            if (string.IsNullOrWhiteSpace(paragraph)) return 0;
            var tokens = TokenizeForEvidenceDrafting(paragraph);
            var overlap = tokens.Count(t => promptTokens.Contains(t));
            var normalizedOverlap = promptTokens.Count == 0 ? 0d : (double)overlap / Math.Max(1, promptTokens.Count);
            var legalBoost = Regex.IsMatch(paragraph, "(court|motion|hearing|deadline|rule|agreement|payment|invoice)", RegexOptions.IgnoreCase) ? 1 : 0;
            var numericBoost = Regex.IsMatch(paragraph, @"\d") ? 1 : 0;
            return (int)Math.Round(normalizedOverlap * 8d, MidpointRounding.AwayFromZero) + legalBoost + numericBoost;
        }

        private static HashSet<string> TokenizeForEvidenceDrafting(string text)
        {
            return Regex.Split((text ?? string.Empty).ToLowerInvariant(), @"[^\p{L}\p{Nd}]+")
                .Where(t => t.Length >= 3)
                .ToHashSet(StringComparer.Ordinal);
        }

        private static string ClassifyClaimType(string text, bool proceduralPrompt)
        {
            if (Regex.IsMatch(text, @"\b(rule|deadline|must|shall|file|court)\b", RegexOptions.IgnoreCase))
                return proceduralPrompt ? "rule_based" : "procedural";
            if (Regex.IsMatch(text, @"\$\s?\d+|\b\d{1,3}(,\d{3})*(\.\d{2})?\b")) return "amount";
            if (Regex.IsMatch(text, @"\b\d{1,2}/\d{1,2}/\d{2,4}\b|\b(jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec)\b", RegexOptions.IgnoreCase)) return "date";
            return "fact";
        }

        private static bool IsCriticalClaim(string text, string claimType)
        {
            if (claimType is "rule_based" or "procedural" or "date" or "amount") return true;
            return Regex.IsMatch(text, @"\b(deadline|filed|served|paid|breach|damages|termination)\b", RegexOptions.IgnoreCase);
        }

        private static bool ClaimRequiresRuleCitation(AiDraftClaim claim, bool ruleAwareMode)
        {
            if (string.Equals(claim.ClaimType, "rule_based", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(claim.ClaimType, "procedural", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (ruleAwareMode)
            {
                return Regex.IsMatch(claim.ClaimText, @"\b(rule|deadline|court|filing|service|motion|hearing)\b", RegexOptions.IgnoreCase);
            }

            return Regex.IsMatch(claim.ClaimText, @"\b(rule|deadline|court|filing|service)\b", RegexOptions.IgnoreCase);
        }

        private static string SafeTruncate(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var normalized = Regex.Replace(value, @"\s+", " ").Trim();
            return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
        }

        private async System.Threading.Tasks.Task<AiDraftRuleAwareContext?> BuildRuleAwareContextAsync(
            string? matterId,
            EvidenceLinkedDraftJurisdictionContextDto? dtoContext,
            DateTime asOfUtc)
        {
            var resolvedContext = dtoContext == null
                ? new EvidenceLinkedDraftJurisdictionContextDto()
                : new EvidenceLinkedDraftJurisdictionContextDto
                {
                    JurisdictionCode = dtoContext.JurisdictionCode?.Trim(),
                    CourtSystem = dtoContext.CourtSystem?.Trim(),
                    CourtDivision = dtoContext.CourtDivision?.Trim(),
                    Venue = dtoContext.Venue?.Trim(),
                    CaseType = dtoContext.CaseType?.Trim(),
                    FilingMethod = dtoContext.FilingMethod?.Trim()
                };

            string contextSource = "request";
            Matter? matter = null;
            if (!string.IsNullOrWhiteSpace(matterId))
            {
                matter = await TenantScope(_context.Matters).AsNoTracking().FirstOrDefaultAsync(m => m.Id == matterId);
                if (matter != null && string.IsNullOrWhiteSpace(resolvedContext.CourtSystem) && !string.IsNullOrWhiteSpace(matter.CourtType))
                {
                    resolvedContext.CourtSystem = matter.CourtType?.Trim();
                    contextSource = "matter_fallback";
                }
            }

            if (string.IsNullOrWhiteSpace(resolvedContext.JurisdictionCode))
            {
                return new AiDraftRuleAwareContext
                {
                    ContextSource = contextSource,
                    ResolvedJurisdictionContext = resolvedContext,
                    Matter = matter,
                    ResolveRequest = null,
                    CoverageResolution = null
                };
            }

            var request = new JurisdictionCoverageResolveRequest
            {
                JurisdictionCode = resolvedContext.JurisdictionCode!,
                CourtSystem = EmptyToNull(resolvedContext.CourtSystem),
                CourtDivision = EmptyToNull(resolvedContext.CourtDivision),
                Venue = EmptyToNull(resolvedContext.Venue),
                CaseType = EmptyToNull(resolvedContext.CaseType),
                FilingMethod = EmptyToNull(resolvedContext.FilingMethod),
                AsOfUtc = asOfUtc
            };

            var resolution = await _jurisdictionRulesPlatformService.ResolveCoverageAsync(request);
            return new AiDraftRuleAwareContext
            {
                ContextSource = contextSource,
                ResolvedJurisdictionContext = resolvedContext,
                Matter = matter,
                ResolveRequest = request,
                CoverageResolution = resolution
            };
        }

        private static EvidenceLinkedDraftJurisdictionContextDto? ParseJurisdictionContext(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                return new EvidenceLinkedDraftJurisdictionContextDto
                {
                    JurisdictionCode = GetPropString(doc.RootElement, "jurisdictionCode") ?? GetPropString(doc.RootElement, "JurisdictionCode"),
                    CourtSystem = GetPropString(doc.RootElement, "courtSystem") ?? GetPropString(doc.RootElement, "CourtSystem"),
                    CourtDivision = GetPropString(doc.RootElement, "courtDivision") ?? GetPropString(doc.RootElement, "CourtDivision"),
                    Venue = GetPropString(doc.RootElement, "venue") ?? GetPropString(doc.RootElement, "Venue"),
                    CaseType = GetPropString(doc.RootElement, "caseType") ?? GetPropString(doc.RootElement, "CaseType"),
                    FilingMethod = GetPropString(doc.RootElement, "filingMethod") ?? GetPropString(doc.RootElement, "FilingMethod")
                };
            }
            catch
            {
                return null;
            }
        }

        private static string? EmptyToNull(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private async System.Threading.Tasks.Task UpsertAiEvidenceReviewItemsAsync(
            AiDraftOutput output,
            IReadOnlyCollection<AiDraftClaim> claims,
            ISet<string> unsupportedCriticalClaimIds,
            DateTime now)
        {
            var claimIds = claims.Select(c => c.Id).ToList();
            var existing = claimIds.Count == 0
                ? new List<IntegrationReviewQueueItem>()
                : await TenantScope(_context.IntegrationReviewQueueItems)
                    .Where(i => i.ProviderKey == EvidenceDraftingProviderKey &&
                                i.ItemType == EvidenceDraftingReviewItemType &&
                                i.SourceType == nameof(AiDraftClaim) &&
                                i.SourceId != null &&
                                claimIds.Contains(i.SourceId))
                    .ToListAsync();
            foreach (var dup in existing
                .GroupBy(i => i.SourceId, StringComparer.Ordinal)
                .SelectMany(g => g.OrderByDescending(x => x.UpdatedAt).Skip(1)))
            {
                dup.Status = "resolved";
                dup.Decision = "duplicate_suppressed";
                dup.DecisionNotes = "Suppressed duplicate review item.";
                dup.ResolvedAt = now;
                dup.UpdatedAt = now;
            }

            foreach (var claim in claims)
            {
                var row = existing.FirstOrDefault(i => string.Equals(i.SourceId, claim.Id, StringComparison.Ordinal));
                var pending = unsupportedCriticalClaimIds.Contains(claim.Id);
                if (pending)
                {
                    if (row == null)
                    {
                        _context.IntegrationReviewQueueItems.Add(new IntegrationReviewQueueItem
                        {
                            ProviderKey = EvidenceDraftingProviderKey,
                            ItemType = EvidenceDraftingReviewItemType,
                            SourceType = nameof(AiDraftClaim),
                            SourceId = claim.Id,
                            Status = "pending",
                            Priority = "high",
                            Title = "Unsupported critical AI draft claim",
                            Summary = SafeTruncate(claim.ClaimText, 1000),
                            ContextJson = JsonSerializer.Serialize(new { draftOutputId = output.Id, claimId = claim.Id, claim.OrderIndex, claim.ClaimType, claim.Confidence, claim.Status }),
                            SuggestedActionsJson = JsonSerializer.Serialize(new[]
                            {
                                new { action = "review_claim_evidence", label = "Review claim evidence" },
                                new { action = "rewrite_claim", label = "Rewrite claim" },
                                new { action = "override_supported", label = "Override with reviewer note" }
                            }),
                            CreatedAt = now,
                            UpdatedAt = now
                        });
                    }
                    else
                    {
                        row.Status = "pending";
                        row.Priority = "high";
                        row.Title = "Unsupported critical AI draft claim";
                        row.Summary = SafeTruncate(claim.ClaimText, 1000);
                        row.ContextJson = JsonSerializer.Serialize(new { draftOutputId = output.Id, claimId = claim.Id, claim.OrderIndex, claim.ClaimType, claim.Confidence, claim.Status });
                        row.ResolvedAt = null;
                        row.Decision = null;
                        row.DecisionNotes = null;
                        row.UpdatedAt = now;
                    }
                }
                else if (row != null && !string.Equals(row.Status, "resolved", StringComparison.OrdinalIgnoreCase))
                {
                    row.Status = "resolved";
                    row.Decision = "system_verified";
                    row.DecisionNotes = "Claim no longer unsupported critical after verifier run.";
                    row.ReviewedAt ??= now;
                    row.ResolvedAt = now;
                    row.UpdatedAt = now;
                }
            }
        }

        private async System.Threading.Tasks.Task UpsertAiRuleDriftReviewItemsAsync(
            AiDraftOutput output,
            IReadOnlyCollection<AiDraftClaim> claims,
            ISet<string> staleCriticalClaimIds,
            DateTime now)
        {
            var claimIds = claims.Select(c => c.Id).ToList();
            var existing = claimIds.Count == 0
                ? new List<IntegrationReviewQueueItem>()
                : await TenantScope(_context.IntegrationReviewQueueItems)
                    .Where(i => i.ProviderKey == EvidenceDraftingProviderKey &&
                                i.ItemType == EvidenceDraftingRuleDriftReviewItemType &&
                                i.SourceType == nameof(AiDraftClaim) &&
                                i.SourceId != null &&
                                claimIds.Contains(i.SourceId))
                    .ToListAsync();
            foreach (var dup in existing
                .GroupBy(i => i.SourceId, StringComparer.Ordinal)
                .SelectMany(g => g.OrderByDescending(x => x.UpdatedAt).Skip(1)))
            {
                dup.Status = "resolved";
                dup.Decision = "duplicate_suppressed";
                dup.DecisionNotes = "Suppressed duplicate review item.";
                dup.ResolvedAt = now;
                dup.UpdatedAt = now;
            }

            foreach (var claim in claims)
            {
                var row = existing.FirstOrDefault(i => string.Equals(i.SourceId, claim.Id, StringComparison.Ordinal));
                var pending = staleCriticalClaimIds.Contains(claim.Id);
                if (pending)
                {
                    if (row == null)
                    {
                        _context.IntegrationReviewQueueItems.Add(new IntegrationReviewQueueItem
                        {
                            ProviderKey = EvidenceDraftingProviderKey,
                            ItemType = EvidenceDraftingRuleDriftReviewItemType,
                            SourceType = nameof(AiDraftClaim),
                            SourceId = claim.Id,
                            Status = "pending",
                            Priority = "high",
                            Title = "Stale rule citation detected in critical claim",
                            Summary = SafeTruncate(claim.ClaimText, 1000),
                            ContextJson = JsonSerializer.Serialize(new { draftOutputId = output.Id, claimId = claim.Id, claim.OrderIndex, claim.ClaimType, claim.Status }),
                            SuggestedActionsJson = JsonSerializer.Serialize(new[]
                            {
                                new { action = "refresh_rule_citations", label = "Refresh rule citations" },
                                new { action = "reverify_draft", label = "Re-verify draft" }
                            }),
                            CreatedAt = now,
                            UpdatedAt = now
                        });
                    }
                    else
                    {
                        row.Status = "pending";
                        row.Priority = "high";
                        row.Title = "Stale rule citation detected in critical claim";
                        row.Summary = SafeTruncate(claim.ClaimText, 1000);
                        row.ContextJson = JsonSerializer.Serialize(new { draftOutputId = output.Id, claimId = claim.Id, claim.OrderIndex, claim.ClaimType, claim.Status });
                        row.ResolvedAt = null;
                        row.Decision = null;
                        row.DecisionNotes = null;
                        row.UpdatedAt = now;
                    }
                }
                else if (row != null && !string.Equals(row.Status, "resolved", StringComparison.OrdinalIgnoreCase))
                {
                    row.Status = "resolved";
                    row.Decision = "citations_current";
                    row.DecisionNotes = "Rule citation drift no longer detected after verifier run.";
                    row.ReviewedAt ??= now;
                    row.ResolvedAt = now;
                    row.UpdatedAt = now;
                }
            }
        }

        private async System.Threading.Tasks.Task UpdateAiEvidenceReviewQueueForClaimReviewAsync(
            string draftOutputId,
            AiDraftClaim claim,
            string action,
            EvidenceDraftClaimReviewDto dto,
            string reviewerId,
            DateTime now)
        {
            var reviewItems = await TenantScope(_context.IntegrationReviewQueueItems)
                .Where(i => i.ProviderKey == EvidenceDraftingProviderKey &&
                            i.SourceType == nameof(AiDraftClaim) &&
                            i.SourceId == claim.Id &&
                            (i.ItemType == EvidenceDraftingReviewItemType || i.ItemType == EvidenceDraftingRuleDriftReviewItemType))
                .ToListAsync();

            foreach (var item in reviewItems)
            {
                var shouldResolve =
                    (item.ItemType == EvidenceDraftingReviewItemType && !string.Equals(claim.Status, "unsupported", StringComparison.OrdinalIgnoreCase)) ||
                    (item.ItemType == EvidenceDraftingRuleDriftReviewItemType && !string.Equals(claim.Status, "needs_review", StringComparison.OrdinalIgnoreCase));

                if (action == "reject")
                {
                    item.Status = "in_review";
                    item.Decision = "claim_rejected";
                    item.DecisionNotes = SafeTruncate(dto.ReviewerNotes ?? "Claim rejected by reviewer.", 2048);
                    item.ReviewedBy = reviewerId;
                    item.ReviewedAt = now;
                    item.UpdatedAt = now;
                    continue;
                }

                if (shouldResolve || action == "rewrite" || action == "approve")
                {
                    item.Status = action == "reject" ? "in_review" : "resolved";
                    item.Decision = action switch
                    {
                        "approve" => "reviewer_approved",
                        "rewrite" => "reviewer_rewritten",
                        "reject" => "claim_rejected",
                        _ => "reviewed"
                    };
                    item.DecisionNotes = SafeTruncate(dto.ReviewerNotes ?? string.Empty, 2048);
                    item.ReviewedBy = reviewerId;
                    item.ReviewedAt = now;
                    if (item.Status == "resolved")
                    {
                        item.ResolvedAt = now;
                    }
                    item.UpdatedAt = now;
                }
            }
        }

        private async System.Threading.Tasks.Task RecomputeDraftStatusesAsync(string outputId, DateTime now)
        {
            var access = await GetOwnedDraftOutputAccessAsync(outputId, asNoTracking: false);
            var output = access?.Output;
            if (output == null) return;

            var claims = await TenantScope(_context.AiDraftClaims).Where(c => c.DraftOutputId == outputId).ToListAsync();
            var session = access!.Session;

            var unsupportedCritical = claims.Count(c => c.IsCritical && string.Equals(c.Status, "unsupported", StringComparison.OrdinalIgnoreCase));
            var needsReviewCritical = claims.Count(c => c.IsCritical && string.Equals(c.Status, "needs_review", StringComparison.OrdinalIgnoreCase));

            output.Status = (unsupportedCritical > 0 || needsReviewCritical > 0) ? "review_required" : "verified";
            output.UpdatedAt = now;

            if (session != null && !string.Equals(session.Status, "published", StringComparison.OrdinalIgnoreCase))
            {
                session.Status = output.Status == "review_required" ? "review_required" : "generated";
                session.UpdatedAt = now;
            }
        }

        private static string NormalizePublishPolicy(string? policy)
        {
            return (policy ?? "warn_only").Trim().ToLowerInvariant() switch
            {
                "warn_only" => "warn_only",
                "block_on_unsupported_critical" => "block_on_unsupported_critical",
                "block_on_low_confidence" => "block_on_low_confidence",
                _ => "warn_only"
            };
        }

        private static string? UpdateClaimMetadataForReview(string? existingJson, object reviewEntry)
        {
            var history = new List<object>();
            var existing = TryParseJsonDictionary(existingJson);
            if (existing != null && existing.TryGetValue("reviewHistory", out var rh) && rh.ValueKind == JsonValueKind.Array)
            {
                foreach (var row in rh.EnumerateArray())
                {
                    history.Add(JsonSerializer.Deserialize<object>(row.GetRawText())!);
                }
            }
            history.Add(reviewEntry);
            const int maxHistoryRows = 50;
            if (history.Count > maxHistoryRows)
            {
                history = history.Skip(history.Count - maxHistoryRows).ToList();
            }

            var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["reviewHistory"] = history,
                ["lastReviewAction"] = reviewEntry
            };

            if (existing != null)
            {
                foreach (var kv in existing)
                {
                    if (kv.Key is "reviewHistory" or "lastReviewAction") continue;
                    payload[kv.Key] = JsonSerializer.Deserialize<object>(kv.Value.GetRawText());
                }
            }

            return JsonSerializer.Serialize(payload);
        }

        private static string? UpdateOutputMetadataForPublish(string? existingJson, object publishInfo)
        {
            var payload = new Dictionary<string, object?>(StringComparer.Ordinal);
            var existing = TryParseJsonDictionary(existingJson);
            if (existing != null)
            {
                foreach (var kv in existing)
                {
                    payload[kv.Key] = JsonSerializer.Deserialize<object>(kv.Value.GetRawText());
                }
            }
            payload["publish"] = publishInfo;
            return JsonSerializer.Serialize(payload);
        }

        private static string AppendReviewerNoteToSupportSummary(string? current, string reviewerId, string? notes, string action)
        {
            var note = $"review:{action} by {reviewerId}";
            if (!string.IsNullOrWhiteSpace(notes))
            {
                note += $" ({SafeTruncate(notes, 120)})";
            }

            if (string.IsNullOrWhiteSpace(current))
            {
                return note;
            }

            return $"{current}; {note}";
        }

        private static EvidenceBundleParseResult ParseRetrievalBundle(string? json)
        {
            var result = new EvidenceBundleParseResult();
            if (string.IsNullOrWhiteSpace(json))
            {
                return result;
            }

            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return result;
                }

                if (doc.RootElement.TryGetProperty("chunks", out var chunksEl) && chunksEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in chunksEl.EnumerateArray())
                    {
                        result.Chunks.Add(new EvidenceBundleChunk
                        {
                            ChunkId = GetPropString(el, "chunkId") ?? string.Empty,
                            DocumentId = GetPropString(el, "documentId") ?? string.Empty,
                            DocumentName = GetPropString(el, "documentName") ?? string.Empty,
                            DocumentVersionId = GetPropString(el, "documentVersionId"),
                            Sha256 = GetPropString(el, "sha256"),
                            ParagraphId = GetPropString(el, "paragraphId"),
                            Page = GetPropInt(el, "page"),
                            Text = GetPropString(el, "text") ?? string.Empty,
                            Score = GetPropInt(el, "score") ?? 0
                        });
                    }
                }

                if (doc.RootElement.TryGetProperty("rulePacks", out var rulesEl) && rulesEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in rulesEl.EnumerateArray())
                    {
                        result.RulePacks.Add(new EvidenceBundleRulePack
                        {
                            BundleRuleId = GetPropString(el, "bundleRuleId") ?? string.Empty,
                            JurisdictionRulePackId = GetPropString(el, "jurisdictionRulePackId") ?? string.Empty,
                            RulePackVersion = GetPropInt(el, "rulePackVersion") ?? 0,
                            RuleCode = GetPropString(el, "ruleCode"),
                            Name = GetPropString(el, "name") ?? string.Empty,
                            JurisdictionCode = GetPropString(el, "jurisdictionCode") ?? string.Empty,
                            CourtSystem = GetPropString(el, "courtSystem"),
                            CaseType = GetPropString(el, "caseType"),
                            FilingMethod = GetPropString(el, "filingMethod"),
                            SourceCitation = GetPropString(el, "sourceCitation")
                        });
                    }
                }

                result.IsParsed = true;
            }
            catch
            {
                result.IsParsed = false;
            }

            return result;
        }

        private static Dictionary<string, JsonElement>? TryParseJsonDictionary(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
                var dict = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                foreach (var p in doc.RootElement.EnumerateObject())
                {
                    dict[p.Name] = p.Value.Clone();
                }
                return dict;
            }
            catch
            {
                return null;
            }
        }

        private static string? GetJsonString(Dictionary<string, JsonElement>? dict, string key)
        {
            if (dict == null || !dict.TryGetValue(key, out var val)) return null;
            if (val.ValueKind == JsonValueKind.Null || val.ValueKind == JsonValueKind.Undefined) return null;
            return val.ValueKind == JsonValueKind.String ? val.GetString() : val.ToString();
        }

        private static bool HasExcerptOverlap(string? excerpt, string? sourceText)
        {
            if (string.IsNullOrWhiteSpace(excerpt) || string.IsNullOrWhiteSpace(sourceText)) return false;
            var left = NormalizeForEvidenceDrafting(excerpt);
            var right = NormalizeForEvidenceDrafting(sourceText);
            if (left.Length == 0 || right.Length == 0) return false;
            if (right.Contains(left, StringComparison.Ordinal) || left.Contains(right, StringComparison.Ordinal)) return true;
            var leftTokens = TokenizeForEvidenceDrafting(left);
            var rightTokens = TokenizeForEvidenceDrafting(right);
            if (leftTokens.Count == 0 || rightTokens.Count == 0) return false;
            var overlap = leftTokens.Count(t => rightTokens.Contains(t));
            return overlap >= Math.Max(2, Math.Min(leftTokens.Count, rightTokens.Count) / 2);
        }

        private static Dictionary<string, List<object>> DetectClaimContradictions(IReadOnlyCollection<AiDraftClaim> claims)
        {
            var result = new Dictionary<string, List<object>>(StringComparer.Ordinal);
            var list = claims.OrderBy(c => c.OrderIndex).Take(48).ToList();
            for (var i = 0; i < list.Count; i++)
            {
                for (var j = i + 1; j < list.Count; j++)
                {
                    var left = list[i];
                    var right = list[j];
                    if (string.Equals(left.Id, right.Id, StringComparison.Ordinal)) continue;
                    if (string.IsNullOrWhiteSpace(left.ClaimText) || string.IsNullOrWhiteSpace(right.ClaimText)) continue;

                    var leftTokens = TokenizeForEvidenceDrafting(left.ClaimText);
                    var rightTokens = TokenizeForEvidenceDrafting(right.ClaimText);
                    if (leftTokens.Count == 0 || rightTokens.Count == 0) continue;

                    var overlapTokens = leftTokens.Intersect(rightTokens, StringComparer.Ordinal).Take(8).ToList();
                    var overlapCount = overlapTokens.Count;
                    var minRequired = Math.Max(3, Math.Min(leftTokens.Count, rightTokens.Count) / 2);
                    if (overlapCount < minRequired) continue;

                    var leftNegated = IsNegatedClaimText(left.ClaimText);
                    var rightNegated = IsNegatedClaimText(right.ClaimText);
                    if (leftNegated == rightNegated) continue;

                    var mismatchForLeft = new
                    {
                        type = "claim_contradiction_candidate",
                        claimId = left.Id,
                        relatedClaimId = right.Id,
                        overlapTokens,
                        heuristic = "negation_overlap_v1"
                    };
                    var mismatchForRight = new
                    {
                        type = "claim_contradiction_candidate",
                        claimId = right.Id,
                        relatedClaimId = left.Id,
                        overlapTokens,
                        heuristic = "negation_overlap_v1"
                    };
                    if (!result.TryGetValue(left.Id, out var leftRows))
                    {
                        leftRows = new List<object>();
                        result[left.Id] = leftRows;
                    }
                    if (!result.TryGetValue(right.Id, out var rightRows))
                    {
                        rightRows = new List<object>();
                        result[right.Id] = rightRows;
                    }
                    leftRows.Add(mismatchForLeft);
                    rightRows.Add(mismatchForRight);
                }
            }

            return result;
        }

        private static bool IsNegatedClaimText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            return Regex.IsMatch(text, @"\b(no|not|never|without|none|cannot|can't|isn't|aren't|wasn't|weren't|didn't|doesn't|don't|lacks?|failed to)\b", RegexOptions.IgnoreCase);
        }

        private static decimal? ComputeCitationStabilityScore(bool requiresRule, int citationCount, int validRules, bool staleCitationDetected, int ruleMismatchCount)
        {
            if (!requiresRule && citationCount == 0)
            {
                return null;
            }

            decimal baseScore;
            if (citationCount <= 0)
            {
                baseScore = 0m;
            }
            else
            {
                baseScore = Math.Clamp((decimal)validRules / citationCount, 0m, 1m);
            }

            if (staleCitationDetected) baseScore -= 0.35m;
            if (ruleMismatchCount > 0) baseScore -= Math.Min(0.25m, ruleMismatchCount * 0.10m);
            return Math.Round(Math.Clamp(baseScore, 0m, 1m), 3, MidpointRounding.AwayFromZero);
        }

        private static List<object> BuildEvidenceSuggestionsForClaim(
            AiDraftClaim claim,
            IReadOnlyCollection<AiDraftEvidenceLink> currentLinks,
            IReadOnlyCollection<EvidenceBundleChunk> bundleChunks,
            int take)
        {
            if (string.IsNullOrWhiteSpace(claim.ClaimText) || bundleChunks.Count == 0 || take <= 0)
            {
                return new List<object>();
            }

            var claimTokens = TokenizeForEvidenceDrafting(claim.ClaimText);
            if (claimTokens.Count == 0)
            {
                return new List<object>();
            }

            var linkedChunkIds = new HashSet<string>(
                currentLinks.Select(l => GetJsonString(TryParseJsonDictionary(l.MetadataJson), "bundleChunkId"))
                    .Where(x => !string.IsNullOrWhiteSpace(x))!
                    .Select(x => x!),
                StringComparer.Ordinal);
            var linkedDocumentIds = new HashSet<string>(
                currentLinks.Where(l => !string.IsNullOrWhiteSpace(l.DocumentId)).Select(l => l.DocumentId!),
                StringComparer.Ordinal);

            var suggestions = bundleChunks
                .Where(c => !linkedChunkIds.Contains(c.ChunkId) && !string.IsNullOrWhiteSpace(c.Text))
                .Select(c =>
                {
                    var chunkTokens = TokenizeForEvidenceDrafting(c.Text);
                    var overlap = claimTokens.Count(t => chunkTokens.Contains(t));
                    var overlapRatio = claimTokens.Count == 0 ? 0m : (decimal)overlap / claimTokens.Count;
                    var sameDocBoost = linkedDocumentIds.Contains(c.DocumentId) ? 1 : 0;
                    var rank = overlap + (c.Score * 0.25m) + sameDocBoost;
                    return new
                    {
                        chunk = c,
                        overlap,
                        overlapRatio,
                        sameDocBoost,
                        rank
                    };
                })
                .Where(x => x.overlap >= 2 || x.sameDocBoost > 0)
                .OrderByDescending(x => x.rank)
                .ThenByDescending(x => x.overlap)
                .ThenByDescending(x => x.chunk.Score)
                .Take(take)
                .Select(x => (object)new
                {
                    type = "suggested_evidence_link",
                    bundleChunkId = x.chunk.ChunkId,
                    documentId = x.chunk.DocumentId,
                    documentVersionId = x.chunk.DocumentVersionId,
                    sha256 = x.chunk.Sha256,
                    paragraphId = x.chunk.ParagraphId,
                    page = x.chunk.Page,
                    excerpt = SafeTruncate(x.chunk.Text, 300),
                    supportStrength = x.chunk.Score >= 3 ? "strong" : x.chunk.Score >= 1 ? "medium" : "weak",
                    overlapTokenCount = x.overlap,
                    overlapRatio = Math.Round(x.overlapRatio, 3, MidpointRounding.AwayFromZero),
                    reason = x.sameDocBoost > 0 ? "same_document_high_similarity" : "high_similarity_bundle_chunk"
                })
                .ToList();

            return suggestions;
        }

        private static object BuildEvidenceDraftingLatestVerificationMetrics(IReadOnlyCollection<AiDraftVerificationRun> latestRuns)
        {
            var claimCoverage = 0;
            var unsupported = 0;
            var unsupportedCritical = 0;
            var needsReview = 0;
            var contradictionCandidates = 0;
            var outdatedSourceLinks = 0;
            var staleCitations = 0;
            var evidenceSuggestionCount = 0;
            var citationStabilityValues = new List<decimal>();
            var claimCoverageTrend = new Dictionary<string, (int total, int supportedLike, int unsupportedCritical, int needsReview)>(StringComparer.Ordinal);

            foreach (var run in latestRuns)
            {
                if (string.IsNullOrWhiteSpace(run.ResultJson)) continue;
                try
                {
                    using var doc = JsonDocument.Parse(run.ResultJson);
                    var root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object) continue;
                    if (root.TryGetProperty("summary", out var summary) && summary.ValueKind == JsonValueKind.Object)
                    {
                        var claimCount = GetPropInt(summary, "ClaimCount") ?? GetPropInt(summary, "claimCount") ?? 0;
                        var supportedCount = GetPropInt(summary, "SupportedCount") ?? GetPropInt(summary, "supportedCount") ?? 0;
                        var partiallySupportedCount = GetPropInt(summary, "PartiallySupportedCount") ?? GetPropInt(summary, "partiallySupportedCount") ?? 0;
                        var unsupportedCount = GetPropInt(summary, "UnsupportedCount") ?? GetPropInt(summary, "unsupportedCount") ?? 0;
                        var unsupportedCriticalCount = GetPropInt(summary, "UnsupportedCriticalClaimCount") ?? GetPropInt(summary, "unsupportedCriticalClaimCount") ?? 0;
                        var needsReviewCount = GetPropInt(summary, "NeedsReviewCount") ?? GetPropInt(summary, "needsReviewCount") ?? 0;
                        var contradictionCount = GetPropInt(summary, "ContradictionCandidateCount") ?? GetPropInt(summary, "contradictionCandidateCount") ?? 0;
                        var outdatedCount = GetPropInt(summary, "OutdatedSourceLinkCount") ?? GetPropInt(summary, "outdatedSourceLinkCount") ?? 0;
                        var staleCount = GetPropInt(summary, "StaleCitationCount") ?? GetPropInt(summary, "staleCitationCount") ?? 0;
                        var suggestionCount = GetPropInt(summary, "EvidenceSuggestionCount") ?? GetPropInt(summary, "evidenceSuggestionCount") ?? 0;

                        claimCoverage += (supportedCount + partiallySupportedCount);
                        unsupported += unsupportedCount;
                        unsupportedCritical += unsupportedCriticalCount;
                        needsReview += needsReviewCount;
                        contradictionCandidates += contradictionCount;
                        outdatedSourceLinks += outdatedCount;
                        staleCitations += staleCount;
                        evidenceSuggestionCount += suggestionCount;

                        if (TryGetDecimal(summary, "AverageCitationStabilityScore", out var avgStability) ||
                            TryGetDecimal(summary, "averageCitationStabilityScore", out avgStability))
                        {
                            citationStabilityValues.Add(avgStability);
                        }

                        var bucketKey = run.CreatedAt.Date.ToString("yyyy-MM-dd");
                        if (!claimCoverageTrend.TryGetValue(bucketKey, out var bucket))
                        {
                            bucket = (0, 0, 0, 0);
                        }
                        bucket.total += claimCount;
                        bucket.supportedLike += (supportedCount + partiallySupportedCount);
                        bucket.unsupportedCritical += unsupportedCriticalCount;
                        bucket.needsReview += needsReviewCount;
                        claimCoverageTrend[bucketKey] = bucket;
                    }
                }
                catch
                {
                    // Ignore malformed verifier payloads in metrics rollup.
                }
            }

            return new
            {
                verificationRunCount = latestRuns.Count,
                contradictionCandidateCount = contradictionCandidates,
                outdatedSourceLinkCount = outdatedSourceLinks,
                staleCitationCount = staleCitations,
                evidenceSuggestionCount,
                averageCitationStabilityScore = citationStabilityValues.Count == 0
                    ? (decimal?)null
                    : Math.Round(citationStabilityValues.Average(), 3, MidpointRounding.AwayFromZero),
                claimCoverageTrend = claimCoverageTrend
                    .OrderBy(kv => kv.Key)
                    .Select(kv => new
                    {
                        date = kv.Key,
                        claimCount = kv.Value.total,
                        coveragePct = kv.Value.total == 0 ? 0m : Math.Round(((decimal)kv.Value.supportedLike / kv.Value.total) * 100m, 2, MidpointRounding.AwayFromZero),
                        unsupportedCritical = kv.Value.unsupportedCritical,
                        needsReview = kv.Value.needsReview
                    })
                    .ToList()
            };
        }

        private static double? Percentile(List<double> values, double percentile)
        {
            if (values == null || values.Count == 0) return null;
            var sorted = values.Where(v => !double.IsNaN(v) && !double.IsInfinity(v)).OrderBy(v => v).ToList();
            if (sorted.Count == 0) return null;
            percentile = Math.Clamp(percentile, 0d, 1d);
            if (sorted.Count == 1) return Math.Round(sorted[0], 2, MidpointRounding.AwayFromZero);
            var position = (sorted.Count - 1) * percentile;
            var lower = (int)Math.Floor(position);
            var upper = (int)Math.Ceiling(position);
            if (lower == upper) return Math.Round(sorted[lower], 2, MidpointRounding.AwayFromZero);
            var fraction = position - lower;
            var interpolated = sorted[lower] + ((sorted[upper] - sorted[lower]) * fraction);
            return Math.Round(interpolated, 2, MidpointRounding.AwayFromZero);
        }

        private static bool TryGetDecimal(JsonElement obj, string name, out decimal value)
        {
            value = 0m;
            if (!obj.TryGetProperty(name, out var p) || p.ValueKind == JsonValueKind.Null || p.ValueKind == JsonValueKind.Undefined) return false;
            if (p.ValueKind == JsonValueKind.Number && p.TryGetDecimal(out value)) return true;
            if (p.ValueKind == JsonValueKind.String && decimal.TryParse(p.GetString(), out value)) return true;
            return false;
        }

        private static string NormalizeForEvidenceDrafting(string value)
        {
            return Regex.Replace((value ?? string.Empty).ToLowerInvariant(), @"\s+", " ").Trim();
        }

        private static string? GetPropString(JsonElement obj, string name)
        {
            if (!obj.TryGetProperty(name, out var p) || p.ValueKind == JsonValueKind.Null || p.ValueKind == JsonValueKind.Undefined) return null;
            return p.ValueKind == JsonValueKind.String ? p.GetString() : p.ToString();
        }

        private static int? GetPropInt(JsonElement obj, string name)
        {
            if (!obj.TryGetProperty(name, out var p) || p.ValueKind == JsonValueKind.Null || p.ValueKind == JsonValueKind.Undefined) return null;
            if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var n)) return n;
            if (p.ValueKind == JsonValueKind.String && int.TryParse(p.GetString(), out n)) return n;
            return null;
        }
    }

    public class EvidenceLinkedDraftGenerateDto
    {
        public string? MatterId { get; set; }
        public string? Title { get; set; }
        public string? Purpose { get; set; }
        public string Prompt { get; set; } = string.Empty;
        public List<string>? SelectedDocumentIds { get; set; }
        public EvidenceLinkedDraftJurisdictionContextDto? JurisdictionContext { get; set; }
        public int? TopChunksPerDocument { get; set; }
        public int? MaxClaims { get; set; }
        public bool? AutoVerify { get; set; }
    }

    public class EvidenceLinkedDraftVerifyDto
    {
        public bool CreateReviewQueueItems { get; set; } = true;
    }

    public class EvidenceDraftBatchReverifyDto
    {
        public List<string>? DraftOutputIds { get; set; }
        public bool CreateReviewQueueItems { get; set; } = true;
        public int? Days { get; set; }
        public int? Limit { get; set; }
    }

    public class EvidenceDraftClaimReviewDto
    {
        public string Action { get; set; } = string.Empty; // approve | reject | rewrite
        public string? StatusOverride { get; set; } // supported | partially_supported
        public string? ReviewerNotes { get; set; }
        public string? ApproverReason { get; set; }
        public string? RewrittenText { get; set; }
    }

    public class EvidenceDraftPublishDto
    {
        public string? Policy { get; set; } // warn_only | block_on_unsupported_critical | block_on_low_confidence
        public decimal? LowConfidenceThreshold { get; set; }
    }

    public class EvidenceLinkedDraftJurisdictionContextDto
    {
        public string? JurisdictionCode { get; set; }
        public string? CourtSystem { get; set; }
        public string? CourtDivision { get; set; }
        public string? Venue { get; set; }
        public string? CaseType { get; set; }
        public string? FilingMethod { get; set; }
    }

    internal class EvidenceStructuredClaim
    {
        public string ClaimRef { get; set; } = string.Empty;
        public string ClaimText { get; set; } = string.Empty;
        public string ClaimType { get; set; } = "fact";
        public bool IsCritical { get; set; }
        public decimal Confidence { get; set; }
        public List<string> SupportingEvidenceIds { get; set; } = new();
        public List<string> RuleCitationIds { get; set; } = new();
    }

    internal class EvidenceBundleChunk
    {
        public string ChunkId { get; set; } = string.Empty;
        public string DocumentId { get; set; } = string.Empty;
        public string DocumentName { get; set; } = string.Empty;
        public string? DocumentVersionId { get; set; }
        public string? Sha256 { get; set; }
        public string? ParagraphId { get; set; }
        public int? Page { get; set; }
        public string Text { get; set; } = string.Empty;
        public int Score { get; set; }
    }

    internal class EvidenceBundleRulePack
    {
        public string BundleRuleId { get; set; } = string.Empty;
        public string JurisdictionRulePackId { get; set; } = string.Empty;
        public int RulePackVersion { get; set; }
        public string? RuleCode { get; set; }
        public string Name { get; set; } = string.Empty;
        public string JurisdictionCode { get; set; } = string.Empty;
        public string? CourtSystem { get; set; }
        public string? CaseType { get; set; }
        public string? FilingMethod { get; set; }
        public string? SourceCitation { get; set; }
        public DateTime EffectiveFrom { get; set; }
        public DateTime? EffectiveTo { get; set; }
    }

    internal class EvidenceBundleParseResult
    {
        public bool IsParsed { get; set; }
        public List<EvidenceBundleChunk> Chunks { get; set; } = new();
        public List<EvidenceBundleRulePack> RulePacks { get; set; } = new();
    }

    internal class EvidenceDraftVerificationSummary
    {
        public string DraftOutputId { get; set; } = string.Empty;
        public string VerifierVersion { get; set; } = "evidence-linked-verifier-v1";
        public string Status { get; set; } = "completed";
        public int ClaimCount { get; set; }
        public int CriticalClaimCount { get; set; }
        public int SupportedCount { get; set; }
        public int PartiallySupportedCount { get; set; }
        public int UnsupportedCount { get; set; }
        public int NeedsReviewCount { get; set; }
        public int UnsupportedCriticalClaimCount { get; set; }
        public bool? RuleAwarenessApplied { get; set; }
        public bool? CoverageFound { get; set; }
        public bool? RulePackFound { get; set; }
        public bool? CoverageRequiresHumanReview { get; set; }
        public decimal? CoverageConfidenceScore { get; set; }
        public string? CoverageReviewQueueItemId { get; set; }
        public int StaleCitationCount { get; set; }
        public int StaleCriticalClaimCount { get; set; }
        public string? CurrentRulePackId { get; set; }
        public int? CurrentRulePackVersion { get; set; }
        public int ContradictionCandidateCount { get; set; }
        public int ContradictionClaimCount { get; set; }
        public int OutdatedSourceLinkCount { get; set; }
        public int OutdatedSourceClaimCount { get; set; }
        public decimal? AverageCitationStabilityScore { get; set; }
        public int LowCitationStabilityClaimCount { get; set; }
        public int EvidenceSuggestionCount { get; set; }
    }

    internal sealed class AiDraftRuleAwareContext
    {
        public string ContextSource { get; set; } = "request";
        public Matter? Matter { get; set; }
        public EvidenceLinkedDraftJurisdictionContextDto? ResolvedJurisdictionContext { get; set; }
        public JurisdictionCoverageResolveRequest? ResolveRequest { get; set; }
        public JurisdictionCoverageResolution? CoverageResolution { get; set; }
    }
}
