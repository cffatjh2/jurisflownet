using System.Security.Claims;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using JurisFlow.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JurisFlow.Server.Controllers
{
    [Route("api/jurisdiction-rules-platform")]
    [ApiController]
    [Authorize(Policy = "StaffOnly")]
    public class JurisdictionRulesPlatformController : ControllerBase
    {
        private readonly JurisFlowDbContext _context;
        private readonly JurisdictionRulesPlatformService _platformService;
        private readonly AuditLogger _auditLogger;

        public JurisdictionRulesPlatformController(
            JurisFlowDbContext context,
            JurisdictionRulesPlatformService platformService,
            AuditLogger auditLogger)
        {
            _context = context;
            _platformService = platformService;
            _auditLogger = auditLogger;
        }

        [HttpGet("jurisdictions")]
        public async Task<IActionResult> GetJurisdictions([FromQuery] string? scope = null, [FromQuery] bool activeOnly = true, CancellationToken cancellationToken = default)
        {
            var query = _context.JurisdictionDefinitions.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(scope))
            {
                query = query.Where(j => j.Scope == scope.Trim().ToLowerInvariant());
            }
            if (activeOnly)
            {
                query = query.Where(j => j.IsActive);
            }

            var rows = await query
                .OrderBy(j => j.JurisdictionCode)
                .ThenBy(j => j.Name)
                .ToListAsync(cancellationToken);
            return Ok(rows);
        }

        [HttpPost("jurisdictions")]
        public async Task<IActionResult> UpsertJurisdiction([FromBody] UpsertJurisdictionDefinitionRequest? request, CancellationToken cancellationToken)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.JurisdictionCode) || string.IsNullOrWhiteSpace(request.Name))
            {
                return BadRequest(new { message = "JurisdictionCode and Name are required." });
            }

            var code = request.JurisdictionCode.Trim().ToUpperInvariant();
            var scope = string.IsNullOrWhiteSpace(request.Scope) ? "state" : request.Scope.Trim().ToLowerInvariant();
            var entity = string.IsNullOrWhiteSpace(request.Id)
                ? await _context.JurisdictionDefinitions.FirstOrDefaultAsync(j => j.JurisdictionCode == code, cancellationToken)
                : await _context.JurisdictionDefinitions.FirstOrDefaultAsync(j => j.Id == request.Id, cancellationToken);

            if (entity == null)
            {
                entity = new JurisdictionDefinition
                {
                    Id = Guid.NewGuid().ToString(),
                    JurisdictionCode = code,
                    CreatedAt = DateTime.UtcNow
                };
                _context.JurisdictionDefinitions.Add(entity);
            }

            entity.Scope = scope;
            entity.CountryCode = string.IsNullOrWhiteSpace(request.CountryCode) ? "US" : request.CountryCode.Trim().ToUpperInvariant();
            entity.StateCode = NormalizeNullable(request.StateCode)?.ToUpperInvariant();
            entity.Name = request.Name.Trim();
            entity.ParentJurisdictionCode = NormalizeNullable(request.ParentJurisdictionCode)?.ToUpperInvariant();
            entity.CourtSystem = NormalizeNullable(request.CourtSystem);
            entity.IsActive = request.IsActive ?? true;
            entity.MetadataJson = request.MetadataJson;
            entity.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(cancellationToken);
            await _auditLogger.LogAsync(HttpContext, "jurisdiction.upsert", nameof(JurisdictionDefinition), entity.Id, $"Code={entity.JurisdictionCode}, Scope={entity.Scope}");
            return Ok(entity);
        }

        [HttpGet("coverage")]
        public async Task<IActionResult> GetCoverageMatrix(
            [FromQuery] string? jurisdictionCode = null,
            [FromQuery] string? supportLevel = null,
            [FromQuery] string? caseType = null,
            [FromQuery] bool activeOnly = true,
            [FromQuery] int limit = 200,
            CancellationToken cancellationToken = default)
        {
            var query = _context.JurisdictionCoverageMatrixEntries.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(jurisdictionCode))
            {
                query = query.Where(c => c.JurisdictionCode == jurisdictionCode.Trim().ToUpperInvariant());
            }
            if (!string.IsNullOrWhiteSpace(supportLevel))
            {
                query = query.Where(c => c.SupportLevel == supportLevel.Trim().ToLowerInvariant());
            }
            if (!string.IsNullOrWhiteSpace(caseType))
            {
                query = query.Where(c => c.CaseType == caseType.Trim());
            }
            if (activeOnly)
            {
                query = query.Where(c => c.Status == "active");
            }

            var rows = await query
                .OrderBy(c => c.JurisdictionCode)
                .ThenBy(c => c.CourtSystem)
                .ThenBy(c => c.CaseType)
                .ThenByDescending(c => c.Version)
                .Take(Math.Clamp(limit, 1, 1000))
                .ToListAsync(cancellationToken);
            return Ok(rows);
        }

        [HttpPost("coverage")]
        public async Task<IActionResult> UpsertCoverageMatrix([FromBody] UpsertJurisdictionCoverageRequest? request, CancellationToken cancellationToken)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.JurisdictionCode))
            {
                return BadRequest(new { message = "JurisdictionCode is required." });
            }

            var jurisdictionCode = request.JurisdictionCode.Trim().ToUpperInvariant();
            var filingMethod = NormalizeNullable(request.FilingMethod) ?? "e_filing";
            var coverageKey = JurisdictionRulesPlatformService.BuildScopeKey(
                jurisdictionCode,
                request.CourtSystem,
                request.CourtDivision,
                request.Venue,
                request.CaseType,
                filingMethod);

            JurisdictionCoverageMatrixEntry? entity = null;
            if (!string.IsNullOrWhiteSpace(request.Id))
            {
                entity = await _context.JurisdictionCoverageMatrixEntries.FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken);
            }
            else if (request.Version.HasValue)
            {
                entity = await _context.JurisdictionCoverageMatrixEntries.FirstOrDefaultAsync(c =>
                    c.CoverageKey == coverageKey && c.Version == request.Version.Value, cancellationToken);
            }

            if (entity == null)
            {
                entity = new JurisdictionCoverageMatrixEntry
                {
                    Id = Guid.NewGuid().ToString(),
                    CoverageKey = coverageKey,
                    JurisdictionCode = jurisdictionCode,
                    Version = request.Version ?? 1,
                    CreatedAt = DateTime.UtcNow
                };
                _context.JurisdictionCoverageMatrixEntries.Add(entity);
            }

            entity.JurisdictionCode = jurisdictionCode;
            entity.CourtSystem = NormalizeNullable(request.CourtSystem);
            entity.CourtDivision = NormalizeNullable(request.CourtDivision);
            entity.Venue = NormalizeNullable(request.Venue);
            entity.CaseType = NormalizeNullable(request.CaseType);
            entity.FilingMethod = filingMethod;
            entity.CoverageKey = coverageKey;
            entity.SupportLevel = NormalizeNullable(request.SupportLevel)?.ToLowerInvariant() ?? "planned";
            entity.Status = NormalizeNullable(request.Status)?.ToLowerInvariant() ?? "active";
            entity.EffectiveFrom = (request.EffectiveFromUtc ?? DateTime.UtcNow).Date;
            entity.EffectiveTo = request.EffectiveToUtc?.Date;
            if (entity.EffectiveTo.HasValue && entity.EffectiveTo < entity.EffectiveFrom)
            {
                return BadRequest(new { message = "EffectiveToUtc must be on or after EffectiveFromUtc." });
            }

            entity.ConfidenceLevel = NormalizeNullable(request.ConfidenceLevel)?.ToLowerInvariant() ?? "medium";
            entity.ConfidenceScore = NormalizeConfidence(request.ConfidenceScore ?? entity.ConfidenceScore);
            entity.RulePackId = NormalizeNullable(request.RulePackId);
            entity.CapabilitiesJson = request.CapabilitiesJson;
            entity.ConstraintsJson = request.ConstraintsJson;
            entity.MetadataJson = request.MetadataJson;
            entity.SourceCitation = NormalizeNullable(request.SourceCitation);
            entity.UpdatedBy = GetActorId();
            entity.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(cancellationToken);
            await _auditLogger.LogAsync(HttpContext, "jurisdiction.coverage.upsert", nameof(JurisdictionCoverageMatrixEntry), entity.Id, $"Key={entity.CoverageKey}, Support={entity.SupportLevel}, Version={entity.Version}");
            return Ok(entity);
        }

        [HttpGet("coverage/resolve")]
        public async Task<IActionResult> ResolveCoverage(
            [FromQuery] string jurisdictionCode,
            [FromQuery] string? courtSystem = null,
            [FromQuery] string? courtDivision = null,
            [FromQuery] string? venue = null,
            [FromQuery] string? caseType = null,
            [FromQuery] string? filingMethod = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(jurisdictionCode))
            {
                return BadRequest(new { message = "jurisdictionCode is required." });
            }

            var resolution = await _platformService.ResolveCoverageAsync(new JurisdictionCoverageResolveRequest
            {
                JurisdictionCode = jurisdictionCode.Trim().ToUpperInvariant(),
                CourtSystem = NormalizeNullable(courtSystem),
                CourtDivision = NormalizeNullable(courtDivision),
                Venue = NormalizeNullable(venue),
                CaseType = NormalizeNullable(caseType),
                FilingMethod = NormalizeNullable(filingMethod) ?? "e_filing",
                AsOfUtc = DateTime.UtcNow
            }, cancellationToken);

            return Ok(resolution);
        }

        [HttpGet("rule-packs")]
        public async Task<IActionResult> GetRulePacks(
            [FromQuery] string? jurisdictionCode = null,
            [FromQuery] string? status = null,
            [FromQuery] string? caseType = null,
            [FromQuery] int limit = 200,
            CancellationToken cancellationToken = default)
        {
            var query = _context.JurisdictionRulePacks.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(jurisdictionCode))
            {
                query = query.Where(r => r.JurisdictionCode == jurisdictionCode.Trim().ToUpperInvariant());
            }
            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(r => r.Status == status.Trim().ToLowerInvariant());
            }
            if (!string.IsNullOrWhiteSpace(caseType))
            {
                query = query.Where(r => r.CaseType == caseType.Trim());
            }

            var rows = await query
                .OrderBy(r => r.JurisdictionCode)
                .ThenBy(r => r.CourtSystem)
                .ThenBy(r => r.CaseType)
                .ThenByDescending(r => r.Version)
                .Take(Math.Clamp(limit, 1, 1000))
                .ToListAsync(cancellationToken);
            return Ok(rows);
        }

        [HttpGet("rule-packs/{id}")]
        public async Task<IActionResult> GetRulePack(string id, CancellationToken cancellationToken)
        {
            var row = await _context.JurisdictionRulePacks.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
            return row == null ? NotFound() : Ok(row);
        }

        [HttpPost("rule-packs")]
        public async Task<IActionResult> UpsertRulePack([FromBody] UpsertJurisdictionRulePackRequest? request, CancellationToken cancellationToken)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.JurisdictionCode) || string.IsNullOrWhiteSpace(request.Name))
            {
                return BadRequest(new { message = "JurisdictionCode and Name are required." });
            }

            var jurisdictionCode = request.JurisdictionCode.Trim().ToUpperInvariant();
            var filingMethod = NormalizeNullable(request.FilingMethod) ?? "e_filing";
            var scopeKey = JurisdictionRulesPlatformService.BuildScopeKey(
                jurisdictionCode,
                request.CourtSystem,
                request.CourtDivision,
                request.Venue,
                request.CaseType,
                filingMethod);

            JurisdictionRulePack? entity = null;
            if (!string.IsNullOrWhiteSpace(request.Id))
            {
                entity = await _context.JurisdictionRulePacks.FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken);
            }
            else if (request.Version.HasValue)
            {
                entity = await _context.JurisdictionRulePacks.FirstOrDefaultAsync(r => r.ScopeKey == scopeKey && r.Version == request.Version.Value, cancellationToken);
            }

            if (entity == null)
            {
                entity = new JurisdictionRulePack
                {
                    Id = Guid.NewGuid().ToString(),
                    CreatedAt = DateTime.UtcNow,
                    Version = request.Version ?? 1
                };
                _context.JurisdictionRulePacks.Add(entity);
            }

            entity.ScopeKey = scopeKey;
            entity.JurisdictionCode = jurisdictionCode;
            entity.CourtSystem = NormalizeNullable(request.CourtSystem);
            entity.CourtDivision = NormalizeNullable(request.CourtDivision);
            entity.Venue = NormalizeNullable(request.Venue);
            entity.CaseType = NormalizeNullable(request.CaseType);
            entity.FilingMethod = filingMethod;
            entity.Name = request.Name.Trim();
            entity.Status = NormalizeNullable(request.Status)?.ToLowerInvariant() ?? entity.Status;
            entity.EffectiveFrom = (request.EffectiveFromUtc ?? entity.EffectiveFrom).Date;
            entity.EffectiveTo = request.EffectiveToUtc?.Date;
            if (entity.EffectiveTo.HasValue && entity.EffectiveTo < entity.EffectiveFrom)
            {
                return BadRequest(new { message = "EffectiveToUtc must be on or after EffectiveFromUtc." });
            }

            entity.ConfidenceLevel = NormalizeNullable(request.ConfidenceLevel)?.ToLowerInvariant() ?? entity.ConfidenceLevel;
            entity.ConfidenceScore = NormalizeConfidence(request.ConfidenceScore ?? entity.ConfidenceScore);
            entity.SourceCitation = NormalizeNullable(request.SourceCitation);
            entity.SourceReferenceId = NormalizeNullable(request.SourceReferenceId);
            entity.DocumentRulesJson = request.DocumentRulesJson;
            entity.FeeRulesJson = request.FeeRulesJson;
            entity.ServiceRulesJson = request.ServiceRulesJson;
            entity.DeadlineRulesJson = request.DeadlineRulesJson;
            entity.LocalOverridesJson = request.LocalOverridesJson;
            entity.ValidationRulesJson = request.ValidationRulesJson;
            entity.MetadataJson = request.MetadataJson;
            if (!string.IsNullOrWhiteSpace(request.ReviewNotes))
            {
                entity.ReviewNotes = request.ReviewNotes;
            }
            entity.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(cancellationToken);
            await _auditLogger.LogAsync(HttpContext, "jurisdiction.rule_pack.upsert", nameof(JurisdictionRulePack), entity.Id, $"ScopeKey={entity.ScopeKey}, Status={entity.Status}, Version={entity.Version}");
            return Ok(entity);
        }

        [HttpPost("rule-packs/{id}/submit-review")]
        public async Task<IActionResult> SubmitRulePackForReview(string id, [FromBody] RulePackReviewTransitionRequest? request, CancellationToken cancellationToken)
        {
            var pack = await _platformService.SubmitRulePackForReviewAsync(id, GetActorId(), request?.Notes, cancellationToken);
            if (pack == null) return NotFound(new { message = "Rule pack not found." });
            await _auditLogger.LogAsync(HttpContext, "jurisdiction.rule_pack.submit_review", nameof(JurisdictionRulePack), pack.Id, $"Status={pack.Status}");
            return Ok(pack);
        }

        [HttpPost("rule-packs/{id}/publish")]
        public async Task<IActionResult> PublishRulePack(string id, [FromBody] RulePackReviewTransitionRequest? request, CancellationToken cancellationToken)
        {
            var pack = await _platformService.PublishRulePackAsync(id, GetActorId(), request?.Notes, cancellationToken);
            if (pack == null) return NotFound(new { message = "Rule pack not found." });
            await _auditLogger.LogAsync(HttpContext, "jurisdiction.rule_pack.publish", nameof(JurisdictionRulePack), pack.Id, $"Status={pack.Status}, Version={pack.Version}");
            return Ok(pack);
        }

        [HttpGet("changes")]
        public async Task<IActionResult> GetRuleChanges(
            [FromQuery] string? jurisdictionCode = null,
            [FromQuery] string? status = null,
            [FromQuery] int limit = 200,
            CancellationToken cancellationToken = default)
        {
            var query = _context.JurisdictionRuleChangeRecords.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(jurisdictionCode))
            {
                query = query.Where(c => c.JurisdictionCode == jurisdictionCode.Trim().ToUpperInvariant());
            }
            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(c => c.Status == status.Trim().ToLowerInvariant());
            }

            var rows = await query
                .OrderByDescending(c => c.CreatedAt)
                .Take(Math.Clamp(limit, 1, 1000))
                .ToListAsync(cancellationToken);
            return Ok(rows);
        }

        [HttpPost("changes")]
        public async Task<IActionResult> CreateRuleChange([FromBody] CreateJurisdictionRuleChangeRequest? request, CancellationToken cancellationToken)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.JurisdictionCode) || string.IsNullOrWhiteSpace(request.ChangeType))
            {
                return BadRequest(new { message = "JurisdictionCode and ChangeType are required." });
            }

            var row = await _platformService.RecordRuleChangeAsync(new JurisdictionRuleChangeRecord
            {
                JurisdictionCode = request.JurisdictionCode.Trim().ToUpperInvariant(),
                CourtSystem = NormalizeNullable(request.CourtSystem),
                CaseType = NormalizeNullable(request.CaseType),
                FilingMethod = NormalizeNullable(request.FilingMethod),
                RulePackId = NormalizeNullable(request.RulePackId),
                CoverageEntryId = NormalizeNullable(request.CoverageEntryId),
                ChangeType = request.ChangeType.Trim().ToLowerInvariant(),
                Status = NormalizeNullable(request.Status)?.ToLowerInvariant() ?? "open",
                Severity = NormalizeNullable(request.Severity)?.ToLowerInvariant() ?? "medium",
                Summary = request.Summary,
                SourceCitation = request.SourceCitation,
                DiffJson = request.DiffJson,
                SourcePayloadJson = request.SourcePayloadJson,
                CreatedBy = GetActorId()
            }, cancellationToken);

            await _auditLogger.LogAsync(HttpContext, "jurisdiction.rule_change.create", nameof(JurisdictionRuleChangeRecord), row.Id, $"ChangeType={row.ChangeType}, Jurisdiction={row.JurisdictionCode}");
            return Ok(row);
        }

        [HttpGet("validation-cases")]
        public async Task<IActionResult> GetValidationCases([FromQuery] string? jurisdictionCode = null, [FromQuery] int limit = 200, CancellationToken cancellationToken = default)
        {
            var query = _context.JurisdictionValidationTestCases.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(jurisdictionCode))
            {
                query = query.Where(t => t.JurisdictionCode == jurisdictionCode.Trim().ToUpperInvariant());
            }
            var rows = await query
                .OrderBy(t => t.JurisdictionCode)
                .ThenBy(t => t.CaseType)
                .ThenBy(t => t.Name)
                .Take(Math.Clamp(limit, 1, 1000))
                .ToListAsync(cancellationToken);
            return Ok(rows);
        }

        [HttpPost("validation-cases")]
        public async Task<IActionResult> UpsertValidationCase([FromBody] UpsertJurisdictionValidationTestCaseRequest? request, CancellationToken cancellationToken)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.JurisdictionCode))
            {
                return BadRequest(new { message = "Name and JurisdictionCode are required." });
            }

            JurisdictionValidationTestCase? entity = null;
            if (!string.IsNullOrWhiteSpace(request.Id))
            {
                entity = await _context.JurisdictionValidationTestCases.FirstOrDefaultAsync(t => t.Id == request.Id, cancellationToken);
            }

            if (entity == null)
            {
                entity = new JurisdictionValidationTestCase
                {
                    Id = Guid.NewGuid().ToString(),
                    CreatedAt = DateTime.UtcNow
                };
                _context.JurisdictionValidationTestCases.Add(entity);
            }

            entity.Name = request.Name.Trim();
            entity.JurisdictionCode = request.JurisdictionCode.Trim().ToUpperInvariant();
            entity.CourtSystem = NormalizeNullable(request.CourtSystem);
            entity.CourtDivision = NormalizeNullable(request.CourtDivision);
            entity.Venue = NormalizeNullable(request.Venue);
            entity.CaseType = NormalizeNullable(request.CaseType);
            entity.FilingMethod = NormalizeNullable(request.FilingMethod) ?? "e_filing";
            entity.RulePackId = NormalizeNullable(request.RulePackId);
            entity.ExpectedSupportLevel = NormalizeNullable(request.ExpectedSupportLevel)?.ToLowerInvariant() ?? "partial";
            entity.ExpectedRequiresHumanReview = request.ExpectedRequiresHumanReview;
            entity.PacketInputJson = request.PacketInputJson;
            entity.ExpectedOutputJson = request.ExpectedOutputJson;
            entity.Status = NormalizeNullable(request.Status)?.ToLowerInvariant() ?? "active";
            entity.Notes = request.Notes;
            entity.UpdatedBy = GetActorId();
            entity.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(cancellationToken);
            await _auditLogger.LogAsync(HttpContext, "jurisdiction.validation_case.upsert", nameof(JurisdictionValidationTestCase), entity.Id, $"Jurisdiction={entity.JurisdictionCode}, Name={entity.Name}");
            return Ok(entity);
        }

        [HttpPost("validation-harness/run")]
        public async Task<IActionResult> RunValidationHarness([FromBody] RunJurisdictionValidationHarnessApiRequest? request, CancellationToken cancellationToken)
        {
            var result = await _platformService.RunValidationHarnessAsync(new JurisdictionValidationHarnessRunRequest
            {
                JurisdictionCode = NormalizeNullable(request?.JurisdictionCode)?.ToUpperInvariant(),
                CourtSystem = NormalizeNullable(request?.CourtSystem),
                CaseType = NormalizeNullable(request?.CaseType),
                FilingMethod = NormalizeNullable(request?.FilingMethod),
                RulePackId = NormalizeNullable(request?.RulePackId),
                Limit = request?.Limit
            }, GetActorId(), cancellationToken);

            await _auditLogger.LogAsync(HttpContext, "jurisdiction.validation_harness.run", nameof(JurisdictionValidationTestRun), result.RunId,
                $"Total={result.TotalCases}, Passed={result.PassedCases}, Failed={result.FailedCases}");
            return Ok(result);
        }

        [HttpGet("validation-runs")]
        public async Task<IActionResult> GetValidationRuns([FromQuery] int limit = 100, CancellationToken cancellationToken = default)
        {
            var rows = await _context.JurisdictionValidationTestRuns.AsNoTracking()
                .OrderByDescending(r => r.CreatedAt)
                .Take(Math.Clamp(limit, 1, 500))
                .ToListAsync(cancellationToken);
            return Ok(rows);
        }

        private string? GetActorId()
        {
            return User.FindFirst("sub")?.Value ??
                   User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        }

        private static string? NormalizeNullable(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static decimal NormalizeConfidence(decimal value)
        {
            if (value < 0m) return 0m;
            if (value > 1m) return 1m;
            return Math.Round(value, 3, MidpointRounding.AwayFromZero);
        }
    }

    public sealed class UpsertJurisdictionDefinitionRequest
    {
        public string? Id { get; set; }
        public string JurisdictionCode { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Scope { get; set; }
        public string? CountryCode { get; set; }
        public string? StateCode { get; set; }
        public string? ParentJurisdictionCode { get; set; }
        public string? CourtSystem { get; set; }
        public bool? IsActive { get; set; }
        public string? MetadataJson { get; set; }
    }

    public sealed class UpsertJurisdictionCoverageRequest
    {
        public string? Id { get; set; }
        public int? Version { get; set; }
        public string JurisdictionCode { get; set; } = string.Empty;
        public string? CourtSystem { get; set; }
        public string? CourtDivision { get; set; }
        public string? Venue { get; set; }
        public string? CaseType { get; set; }
        public string? FilingMethod { get; set; }
        public string? SupportLevel { get; set; }
        public string? Status { get; set; }
        public DateTime? EffectiveFromUtc { get; set; }
        public DateTime? EffectiveToUtc { get; set; }
        public string? ConfidenceLevel { get; set; }
        public decimal? ConfidenceScore { get; set; }
        public string? RulePackId { get; set; }
        public string? CapabilitiesJson { get; set; }
        public string? ConstraintsJson { get; set; }
        public string? MetadataJson { get; set; }
        public string? SourceCitation { get; set; }
    }

    public sealed class UpsertJurisdictionRulePackRequest
    {
        public string? Id { get; set; }
        public int? Version { get; set; }
        public string JurisdictionCode { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? CourtSystem { get; set; }
        public string? CourtDivision { get; set; }
        public string? Venue { get; set; }
        public string? CaseType { get; set; }
        public string? FilingMethod { get; set; }
        public string? Status { get; set; }
        public DateTime? EffectiveFromUtc { get; set; }
        public DateTime? EffectiveToUtc { get; set; }
        public string? ConfidenceLevel { get; set; }
        public decimal? ConfidenceScore { get; set; }
        public string? SourceCitation { get; set; }
        public string? SourceReferenceId { get; set; }
        public string? DocumentRulesJson { get; set; }
        public string? FeeRulesJson { get; set; }
        public string? ServiceRulesJson { get; set; }
        public string? DeadlineRulesJson { get; set; }
        public string? LocalOverridesJson { get; set; }
        public string? ValidationRulesJson { get; set; }
        public string? MetadataJson { get; set; }
        public string? ReviewNotes { get; set; }
    }

    public sealed class RulePackReviewTransitionRequest
    {
        public string? Notes { get; set; }
    }

    public sealed class CreateJurisdictionRuleChangeRequest
    {
        public string JurisdictionCode { get; set; } = string.Empty;
        public string ChangeType { get; set; } = string.Empty;
        public string? Status { get; set; }
        public string? Severity { get; set; }
        public string? RulePackId { get; set; }
        public string? CoverageEntryId { get; set; }
        public string? CourtSystem { get; set; }
        public string? CaseType { get; set; }
        public string? FilingMethod { get; set; }
        public string? Summary { get; set; }
        public string? SourceCitation { get; set; }
        public string? DiffJson { get; set; }
        public string? SourcePayloadJson { get; set; }
    }

    public sealed class UpsertJurisdictionValidationTestCaseRequest
    {
        public string? Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string JurisdictionCode { get; set; } = string.Empty;
        public string? CourtSystem { get; set; }
        public string? CourtDivision { get; set; }
        public string? Venue { get; set; }
        public string? CaseType { get; set; }
        public string? FilingMethod { get; set; }
        public string? RulePackId { get; set; }
        public string? ExpectedSupportLevel { get; set; }
        public bool ExpectedRequiresHumanReview { get; set; }
        public string? PacketInputJson { get; set; }
        public string? ExpectedOutputJson { get; set; }
        public string? Status { get; set; }
        public string? Notes { get; set; }
    }

    public sealed class RunJurisdictionValidationHarnessApiRequest
    {
        public string? JurisdictionCode { get; set; }
        public string? CourtSystem { get; set; }
        public string? CaseType { get; set; }
        public string? FilingMethod { get; set; }
        public string? RulePackId { get; set; }
        public int? Limit { get; set; }
    }
}

