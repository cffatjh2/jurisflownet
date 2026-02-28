using System.Text.Json;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace JurisFlow.Server.Services
{
    public sealed class JurisdictionRulesPlatformService
    {
        public const string ReviewProviderKey = "jurisdiction-rules";

        private readonly JurisFlowDbContext _context;
        private readonly IntegrationPiiMinimizationService _piiMinimizer;

        public JurisdictionRulesPlatformService(
            JurisFlowDbContext context,
            IntegrationPiiMinimizationService piiMinimizer)
        {
            _context = context;
            _piiMinimizer = piiMinimizer;
        }

        public async Task<JurisdictionCoverageResolution> ResolveCoverageAsync(
            JurisdictionCoverageResolveRequest request,
            CancellationToken cancellationToken = default)
        {
            var now = (request.AsOfUtc ?? DateTime.UtcNow).Date;
            var key = BuildScopeKey(
                request.JurisdictionCode,
                request.CourtSystem,
                request.CourtDivision,
                request.Venue,
                request.CaseType,
                request.FilingMethod);

            var coverageCandidates = await _context.JurisdictionCoverageMatrixEntries
                .AsNoTracking()
                .Where(c =>
                    c.Status == "active" &&
                    c.JurisdictionCode == request.JurisdictionCode &&
                    c.EffectiveFrom <= now &&
                    (c.EffectiveTo == null || c.EffectiveTo >= now))
                .OrderByDescending(c => c.Version)
                .ThenByDescending(c => c.EffectiveFrom)
                .ToListAsync(cancellationToken);

            var coverage = coverageCandidates
                .OrderByDescending(c => ScoreCoverageCandidate(c, request))
                .ThenByDescending(c => c.Version)
                .ThenByDescending(c => c.EffectiveFrom)
                .FirstOrDefault();

            JurisdictionRulePack? rulePack = null;
            if (coverage != null && !string.IsNullOrWhiteSpace(coverage.RulePackId))
            {
                rulePack = await _context.JurisdictionRulePacks
                    .AsNoTracking()
                    .FirstOrDefaultAsync(r =>
                        r.Id == coverage.RulePackId &&
                        r.Status == "published" &&
                        r.EffectiveFrom <= now &&
                        (r.EffectiveTo == null || r.EffectiveTo >= now),
                        cancellationToken);
            }

            if (rulePack == null)
            {
                var packCandidates = await _context.JurisdictionRulePacks
                    .AsNoTracking()
                    .Where(r =>
                        r.Status == "published" &&
                        r.JurisdictionCode == request.JurisdictionCode &&
                        r.EffectiveFrom <= now &&
                        (r.EffectiveTo == null || r.EffectiveTo >= now))
                    .OrderByDescending(r => r.Version)
                    .ThenByDescending(r => r.EffectiveFrom)
                    .ToListAsync(cancellationToken);

                rulePack = packCandidates
                    .OrderByDescending(r => ScoreRulePackCandidate(r, request, key))
                    .ThenByDescending(r => r.Version)
                    .ThenByDescending(r => r.EffectiveFrom)
                    .FirstOrDefault();
            }

            var supportLevel = coverage?.SupportLevel ?? "none";
            var confidenceScore = coverage?.ConfidenceScore ?? rulePack?.ConfidenceScore ?? 0m;
            var confidenceLevel = coverage?.ConfidenceLevel ?? rulePack?.ConfidenceLevel ?? "low";

            var requiresHumanReview =
                coverage == null ||
                rulePack == null ||
                supportLevel is "none" or "planned" ||
                string.Equals(confidenceLevel, "low", StringComparison.OrdinalIgnoreCase) ||
                confidenceScore < 0.75m;

            var reasonCodes = new List<string>();
            if (coverage == null) reasonCodes.Add("coverage_missing");
            if (rulePack == null) reasonCodes.Add("rule_pack_missing");
            if (supportLevel is "none" or "planned") reasonCodes.Add("support_level_insufficient");
            if (string.Equals(confidenceLevel, "low", StringComparison.OrdinalIgnoreCase) || confidenceScore < 0.75m) reasonCodes.Add("low_confidence");

            return new JurisdictionCoverageResolution
            {
                ScopeKey = key,
                CoverageEntryId = coverage?.Id,
                RulePackId = rulePack?.Id,
                CoverageFound = coverage != null,
                RulePackFound = rulePack != null,
                SupportLevel = supportLevel,
                ConfidenceLevel = confidenceLevel,
                ConfidenceScore = confidenceScore,
                RequiresHumanReview = requiresHumanReview,
                Coverage = coverage,
                RulePack = rulePack,
                ReasonCodes = reasonCodes
            };
        }

        public async Task<string?> QueuePrecheckReviewIfRequiredAsync(
            JurisdictionCoverageResolution resolution,
            JurisdictionPrecheckReviewRequest request,
            CancellationToken cancellationToken = default)
        {
            if (!resolution.RequiresHumanReview)
            {
                return null;
            }

            var sourceId = request.MatterId ?? resolution.ScopeKey;
            var existing = await _context.IntegrationReviewQueueItems
                .FirstOrDefaultAsync(r =>
                    r.ProviderKey == ReviewProviderKey &&
                    r.ItemType == "jurisdiction_precheck_review" &&
                    r.SourceType == (string.IsNullOrWhiteSpace(request.MatterId) ? "jurisdiction_scope" : nameof(Matter)) &&
                    r.SourceId == sourceId &&
                    (r.Status == IntegrationReviewQueueStatuses.Pending || r.Status == IntegrationReviewQueueStatuses.InReview),
                    cancellationToken);

            var summary = BuildPrecheckReviewSummary(resolution, request);
            var contextJson = _piiMinimizer.SanitizeObjectForStorage(new
            {
                request.MatterId,
                request.ProviderKey,
                request.PacketName,
                request.JurisdictionCode,
                request.CourtSystem,
                request.CourtDivision,
                request.Venue,
                request.CaseType,
                request.FilingMethod,
                request.Metadata,
                resolution.SupportLevel,
                resolution.ConfidenceLevel,
                resolution.ConfidenceScore,
                resolution.ReasonCodes,
                coverage = resolution.Coverage == null ? null : new
                {
                    resolution.Coverage.Id,
                    resolution.Coverage.SupportLevel,
                    resolution.Coverage.ConfidenceLevel,
                    resolution.Coverage.ConfidenceScore,
                    resolution.Coverage.SourceCitation
                },
                rulePack = resolution.RulePack == null ? null : new
                {
                    resolution.RulePack.Id,
                    resolution.RulePack.Name,
                    resolution.RulePack.Version,
                    resolution.RulePack.ConfidenceLevel,
                    resolution.RulePack.ConfidenceScore,
                    resolution.RulePack.SourceCitation
                }
            }, "jurisdiction_rules:precheck_review");

            if (existing != null)
            {
                existing.Summary = Truncate(summary, 2048);
                existing.ContextJson = contextJson;
                existing.Priority = ResolveReviewPriority(resolution);
                existing.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync(cancellationToken);
                return existing.Id;
            }

            var item = new IntegrationReviewQueueItem
            {
                Id = Guid.NewGuid().ToString(),
                ProviderKey = ReviewProviderKey,
                ItemType = "jurisdiction_precheck_review",
                SourceType = string.IsNullOrWhiteSpace(request.MatterId) ? "jurisdiction_scope" : nameof(Matter),
                SourceId = sourceId,
                Status = IntegrationReviewQueueStatuses.Pending,
                Priority = ResolveReviewPriority(resolution),
                Title = Truncate("Court-specific rules coverage review required", 160),
                Summary = Truncate(summary, 2048),
                ContextJson = contextJson,
                SuggestedActionsJson = JsonSerializer.Serialize(new object[]
                {
                    new { action = "review_coverage_matrix", jurisdictionCode = request.JurisdictionCode, caseType = request.CaseType, filingMethod = request.FilingMethod },
                    new { action = "review_rule_pack", rulePackId = resolution.RulePackId, scopeKey = resolution.ScopeKey },
                    new { action = "override_with_manual_review" }
                }),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.IntegrationReviewQueueItems.Add(item);
            await _context.SaveChangesAsync(cancellationToken);
            return item.Id;
        }

        public async Task<JurisdictionRulePack?> SubmitRulePackForReviewAsync(
            string rulePackId,
            string? userId,
            string? notes,
            CancellationToken cancellationToken = default)
        {
            var rulePack = await _context.JurisdictionRulePacks.FirstOrDefaultAsync(r => r.Id == rulePackId, cancellationToken);
            if (rulePack == null)
            {
                return null;
            }

            rulePack.Status = "in_review";
            rulePack.SubmittedForReviewAt = DateTime.UtcNow;
            rulePack.SubmittedForReviewBy = userId;
            if (!string.IsNullOrWhiteSpace(notes))
            {
                rulePack.ReviewNotes = Truncate(notes, 2048);
            }
            rulePack.UpdatedAt = DateTime.UtcNow;

            _context.JurisdictionRuleChangeRecords.Add(new JurisdictionRuleChangeRecord
            {
                Id = Guid.NewGuid().ToString(),
                RulePackId = rulePack.Id,
                JurisdictionCode = rulePack.JurisdictionCode,
                CourtSystem = rulePack.CourtSystem,
                CaseType = rulePack.CaseType,
                FilingMethod = rulePack.FilingMethod,
                ChangeType = "review_submitted",
                Status = "in_review",
                Severity = "medium",
                Summary = $"Rule pack '{rulePack.Name}' v{rulePack.Version} submitted for review.",
                SourceCitation = rulePack.SourceCitation,
                ReviewNotes = rulePack.ReviewNotes,
                CreatedBy = userId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync(cancellationToken);
            return rulePack;
        }

        public async Task<JurisdictionRulePack?> PublishRulePackAsync(
            string rulePackId,
            string? userId,
            string? notes,
            CancellationToken cancellationToken = default)
        {
            var rulePack = await _context.JurisdictionRulePacks.FirstOrDefaultAsync(r => r.Id == rulePackId, cancellationToken);
            if (rulePack == null)
            {
                return null;
            }

            var now = DateTime.UtcNow;
            var activePublished = await _context.JurisdictionRulePacks
                .Where(r =>
                    r.Id != rulePack.Id &&
                    r.ScopeKey == rulePack.ScopeKey &&
                    r.Status == "published" &&
                    (r.EffectiveTo == null || r.EffectiveTo >= rulePack.EffectiveFrom))
                .ToListAsync(cancellationToken);

            foreach (var existing in activePublished)
            {
                existing.Status = "retired";
                existing.EffectiveTo = rulePack.EffectiveFrom.AddDays(-1);
                existing.SupersededByRulePackId = rulePack.Id;
                existing.UpdatedAt = now;
            }

            rulePack.Status = "published";
            rulePack.PublishedAt = now;
            rulePack.PublishedBy = userId;
            if (!string.IsNullOrWhiteSpace(notes))
            {
                rulePack.ReviewNotes = Truncate(notes, 2048);
            }
            rulePack.UpdatedAt = now;

            _context.JurisdictionRuleChangeRecords.Add(new JurisdictionRuleChangeRecord
            {
                Id = Guid.NewGuid().ToString(),
                RulePackId = rulePack.Id,
                JurisdictionCode = rulePack.JurisdictionCode,
                CourtSystem = rulePack.CourtSystem,
                CaseType = rulePack.CaseType,
                FilingMethod = rulePack.FilingMethod,
                ChangeType = "published",
                Status = "published",
                Severity = "medium",
                Summary = $"Rule pack '{rulePack.Name}' v{rulePack.Version} published.",
                SourceCitation = rulePack.SourceCitation,
                ReviewNotes = rulePack.ReviewNotes,
                CreatedBy = userId,
                ReviewedBy = userId,
                ReviewedAt = now,
                CreatedAt = now,
                UpdatedAt = now
            });

            await _context.SaveChangesAsync(cancellationToken);
            return rulePack;
        }

        public async Task<JurisdictionRuleChangeRecord> RecordRuleChangeAsync(
            JurisdictionRuleChangeRecord record,
            CancellationToken cancellationToken = default)
        {
            record.Id = string.IsNullOrWhiteSpace(record.Id) ? Guid.NewGuid().ToString() : record.Id;
            record.CreatedAt = DateTime.UtcNow;
            record.UpdatedAt = DateTime.UtcNow;
            _context.JurisdictionRuleChangeRecords.Add(record);
            await _context.SaveChangesAsync(cancellationToken);
            return record;
        }

        public async Task<JurisdictionValidationHarnessRunResult> RunValidationHarnessAsync(
            JurisdictionValidationHarnessRunRequest request,
            string? triggeredBy,
            CancellationToken cancellationToken = default)
        {
            var query = _context.JurisdictionValidationTestCases.AsNoTracking()
                .Where(t => t.Status == "active");

            if (!string.IsNullOrWhiteSpace(request.JurisdictionCode))
            {
                query = query.Where(t => t.JurisdictionCode == request.JurisdictionCode);
            }
            if (!string.IsNullOrWhiteSpace(request.CaseType))
            {
                query = query.Where(t => t.CaseType == request.CaseType);
            }
            if (!string.IsNullOrWhiteSpace(request.FilingMethod))
            {
                query = query.Where(t => t.FilingMethod == request.FilingMethod);
            }
            if (!string.IsNullOrWhiteSpace(request.RulePackId))
            {
                query = query.Where(t => t.RulePackId == request.RulePackId);
            }

            var testCases = await query
                .OrderBy(t => t.JurisdictionCode)
                .ThenBy(t => t.CaseType)
                .ThenBy(t => t.Name)
                .Take(Math.Clamp(request.Limit ?? 100, 1, 500))
                .ToListAsync(cancellationToken);

            var results = new List<JurisdictionValidationHarnessCaseResult>(testCases.Count);
            foreach (var testCase in testCases)
            {
                var resolution = await ResolveCoverageAsync(new JurisdictionCoverageResolveRequest
                {
                    JurisdictionCode = testCase.JurisdictionCode,
                    CourtSystem = testCase.CourtSystem,
                    CourtDivision = testCase.CourtDivision,
                    Venue = testCase.Venue,
                    CaseType = testCase.CaseType,
                    FilingMethod = testCase.FilingMethod,
                    AsOfUtc = DateTime.UtcNow
                }, cancellationToken);

                var pass = string.Equals(resolution.SupportLevel, testCase.ExpectedSupportLevel, StringComparison.OrdinalIgnoreCase) &&
                           resolution.RequiresHumanReview == testCase.ExpectedRequiresHumanReview;

                results.Add(new JurisdictionValidationHarnessCaseResult
                {
                    TestCaseId = testCase.Id,
                    Name = testCase.Name,
                    Passed = pass,
                    ExpectedSupportLevel = testCase.ExpectedSupportLevel,
                    ActualSupportLevel = resolution.SupportLevel,
                    ExpectedRequiresHumanReview = testCase.ExpectedRequiresHumanReview,
                    ActualRequiresHumanReview = resolution.RequiresHumanReview,
                    RulePackId = resolution.RulePackId,
                    CoverageEntryId = resolution.CoverageEntryId,
                    ReasonCodes = resolution.ReasonCodes
                });
            }

            var passed = results.Count(r => r.Passed);
            var failed = results.Count - passed;
            var runId = Guid.NewGuid().ToString();
            var run = new JurisdictionValidationTestRun
            {
                Id = runId,
                JurisdictionCode = request.JurisdictionCode,
                CourtSystem = request.CourtSystem,
                CaseType = request.CaseType,
                FilingMethod = request.FilingMethod,
                RulePackId = request.RulePackId,
                Status = "completed",
                TotalCases = results.Count,
                PassedCases = passed,
                FailedCases = failed,
                Summary = $"Jurisdiction validation harness completed. Total={results.Count}, Passed={passed}, Failed={failed}.",
                ResultJson = JsonSerializer.Serialize(results),
                TriggeredBy = triggeredBy,
                CreatedAt = DateTime.UtcNow
            };
            _context.JurisdictionValidationTestRuns.Add(run);
            await _context.SaveChangesAsync(cancellationToken);

            return new JurisdictionValidationHarnessRunResult
            {
                RunId = runId,
                TotalCases = results.Count,
                PassedCases = passed,
                FailedCases = failed,
                Results = results
            };
        }

        public static string BuildScopeKey(
            string jurisdictionCode,
            string? courtSystem,
            string? courtDivision,
            string? venue,
            string? caseType,
            string? filingMethod)
        {
            return string.Join("|", new[]
            {
                NormalizeKeyPart(jurisdictionCode),
                NormalizeKeyPart(courtSystem),
                NormalizeKeyPart(courtDivision),
                NormalizeKeyPart(venue),
                NormalizeKeyPart(caseType),
                NormalizeKeyPart(filingMethod)
            });
        }

        private static int ScoreCoverageCandidate(JurisdictionCoverageMatrixEntry c, JurisdictionCoverageResolveRequest request)
        {
            var score = 0;
            score += MatchScore(c.JurisdictionCode, request.JurisdictionCode, 100);
            score += MatchScore(c.CourtSystem, request.CourtSystem, 20, wildcardWeight: 5);
            score += MatchScore(c.CourtDivision, request.CourtDivision, 15, wildcardWeight: 4);
            score += MatchScore(c.Venue, request.Venue, 10, wildcardWeight: 3);
            score += MatchScore(c.CaseType, request.CaseType, 20, wildcardWeight: 4);
            score += MatchScore(c.FilingMethod, request.FilingMethod, 10, wildcardWeight: 2);
            return score;
        }

        private static int ScoreRulePackCandidate(JurisdictionRulePack r, JurisdictionCoverageResolveRequest request, string scopeKey)
        {
            var score = 0;
            if (string.Equals(r.ScopeKey, scopeKey, StringComparison.Ordinal)) score += 200;
            score += MatchScore(r.JurisdictionCode, request.JurisdictionCode, 100);
            score += MatchScore(r.CourtSystem, request.CourtSystem, 20, wildcardWeight: 5);
            score += MatchScore(r.CourtDivision, request.CourtDivision, 15, wildcardWeight: 4);
            score += MatchScore(r.Venue, request.Venue, 10, wildcardWeight: 3);
            score += MatchScore(r.CaseType, request.CaseType, 20, wildcardWeight: 4);
            score += MatchScore(r.FilingMethod, request.FilingMethod, 10, wildcardWeight: 2);
            return score;
        }

        private static int MatchScore(string? actual, string? requested, int exactWeight, int wildcardWeight = 0)
        {
            if (string.IsNullOrWhiteSpace(requested))
            {
                return string.IsNullOrWhiteSpace(actual) ? wildcardWeight : 0;
            }

            if (string.Equals(actual?.Trim(), requested.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return exactWeight;
            }

            return string.IsNullOrWhiteSpace(actual) ? wildcardWeight : 0;
        }

        private static string NormalizeKeyPart(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? "*" : value.Trim().ToLowerInvariant();
        }

        private static string ResolveReviewPriority(JurisdictionCoverageResolution resolution)
        {
            if (!resolution.CoverageFound || !resolution.RulePackFound)
            {
                return "high";
            }

            if (string.Equals(resolution.ConfidenceLevel, "low", StringComparison.OrdinalIgnoreCase) ||
                resolution.ConfidenceScore < 0.6m)
            {
                return "high";
            }

            return "medium";
        }

        private static string BuildPrecheckReviewSummary(JurisdictionCoverageResolution resolution, JurisdictionPrecheckReviewRequest request)
        {
            var reasons = resolution.ReasonCodes.Count == 0 ? "none" : string.Join(", ", resolution.ReasonCodes);
            return $"Court-specific rules review required for precheck. Jurisdiction={request.JurisdictionCode}, CourtSystem={request.CourtSystem}, CaseType={request.CaseType}, FilingMethod={request.FilingMethod}, Support={resolution.SupportLevel}, Confidence={resolution.ConfidenceLevel}/{resolution.ConfidenceScore:0.00}, Reasons={reasons}.";
        }

        private static string? Truncate(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var trimmed = value.Trim();
            return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
        }
    }

    public sealed class JurisdictionCoverageResolveRequest
    {
        public string JurisdictionCode { get; set; } = string.Empty;
        public string? CourtSystem { get; set; }
        public string? CourtDivision { get; set; }
        public string? Venue { get; set; }
        public string? CaseType { get; set; }
        public string? FilingMethod { get; set; }
        public DateTime? AsOfUtc { get; set; }
    }

    public sealed class JurisdictionCoverageResolution
    {
        public string ScopeKey { get; set; } = string.Empty;
        public bool CoverageFound { get; set; }
        public bool RulePackFound { get; set; }
        public string? CoverageEntryId { get; set; }
        public string? RulePackId { get; set; }
        public string SupportLevel { get; set; } = "none";
        public string ConfidenceLevel { get; set; } = "low";
        public decimal ConfidenceScore { get; set; }
        public bool RequiresHumanReview { get; set; }
        public JurisdictionCoverageMatrixEntry? Coverage { get; set; }
        public JurisdictionRulePack? RulePack { get; set; }
        public List<string> ReasonCodes { get; set; } = new();
    }

    public sealed class JurisdictionPrecheckReviewRequest
    {
        public string? MatterId { get; set; }
        public string? ProviderKey { get; set; }
        public string? PacketName { get; set; }
        public string JurisdictionCode { get; set; } = string.Empty;
        public string? CourtSystem { get; set; }
        public string? CourtDivision { get; set; }
        public string? Venue { get; set; }
        public string? CaseType { get; set; }
        public string? FilingMethod { get; set; }
        public IDictionary<string, string>? Metadata { get; set; }
    }

    public sealed class JurisdictionValidationHarnessRunRequest
    {
        public string? JurisdictionCode { get; set; }
        public string? CourtSystem { get; set; }
        public string? CaseType { get; set; }
        public string? FilingMethod { get; set; }
        public string? RulePackId { get; set; }
        public int? Limit { get; set; }
    }

    public sealed class JurisdictionValidationHarnessRunResult
    {
        public string RunId { get; set; } = string.Empty;
        public int TotalCases { get; set; }
        public int PassedCases { get; set; }
        public int FailedCases { get; set; }
        public IReadOnlyCollection<JurisdictionValidationHarnessCaseResult> Results { get; set; } = Array.Empty<JurisdictionValidationHarnessCaseResult>();
    }

    public sealed class JurisdictionValidationHarnessCaseResult
    {
        public string TestCaseId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool Passed { get; set; }
        public string ExpectedSupportLevel { get; set; } = string.Empty;
        public string ActualSupportLevel { get; set; } = string.Empty;
        public bool ExpectedRequiresHumanReview { get; set; }
        public bool ActualRequiresHumanReview { get; set; }
        public string? CoverageEntryId { get; set; }
        public string? RulePackId { get; set; }
        public IReadOnlyCollection<string> ReasonCodes { get; set; } = Array.Empty<string>();
    }
}

