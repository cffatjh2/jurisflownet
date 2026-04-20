using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using JurisFlow.Server.Services;
using System.Text.RegularExpressions;

namespace JurisFlow.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Policy = "StaffOnly")]
    public class ConflictsController : ControllerBase
    {
        private readonly JurisFlowDbContext _context;
        private readonly AuditLogger _auditLogger;
        private readonly TenantContext _tenantContext;
        private const int MaxConflictResults = 50;
        private static readonly HashSet<string> AllowedCheckTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "NewClient",
            "NewMatter",
            "OpposingParty",
            "IntakeSubmission",
            "Manual"
        };
        private static readonly HashSet<string> AllowedEntityTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "Client",
            "Matter",
            "OpposingParty"
        };

        private static class ConflictStatuses
        {
            public const string Pending = "Pending";
            public const string Clear = "Clear";
            public const string Conflict = "Conflict";
            public const string Waived = "Waived";
        }

        public ConflictsController(JurisFlowDbContext context, AuditLogger auditLogger, TenantContext tenantContext)
        {
            _context = context;
            _auditLogger = auditLogger;
            _tenantContext = tenantContext;
        }

        // POST: api/conflicts/check
        [HttpPost("check")]
        public async Task<ActionResult<ConflictCheckResultDto>> RunConflictCheck([FromBody] ConflictCheckRequestDto request)
        {
            if (request == null)
            {
                return BadRequest(new { message = "Request body is required." });
            }

            var tenantId = RequireTenantId();
            var userId = User.FindFirst("sub")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var normalizedSearchQuery = NormalizeSearchQuery(request.SearchQuery);
            if (string.IsNullOrWhiteSpace(normalizedSearchQuery))
            {
                return BadRequest(new { message = "SearchQuery is required." });
            }

            if (normalizedSearchQuery.Length < 3 || normalizedSearchQuery.Length > 120)
            {
                return BadRequest(new { message = "SearchQuery must be between 3 and 120 characters." });
            }

            if (!TryNormalizeOptionalValue(request.CheckType, AllowedCheckTypes, out var normalizedCheckType, out var checkTypeError))
            {
                return BadRequest(new { message = checkTypeError });
            }

            if (!TryNormalizeOptionalValue(request.EntityType, AllowedEntityTypes, out var normalizedEntityType, out var entityTypeError))
            {
                return BadRequest(new { message = entityTypeError });
            }

            var normalizedEntityId = NormalizeOptional(request.EntityId, 128);
            var escapedPattern = $"%{EscapeLikePattern(normalizedSearchQuery)}%";

            // Create conflict check record
            var conflictCheck = new ConflictCheck
            {
                SearchQuery = normalizedSearchQuery,
                CheckType = normalizedCheckType,
                EntityType = normalizedEntityType,
                EntityId = normalizedEntityId,
                CheckedBy = userId,
                Status = ConflictStatuses.Pending
            };

            _context.ConflictChecks.Add(conflictCheck);
            await _context.SaveChangesAsync();

            var results = new List<JurisFlow.Server.Models.ConflictResult>();

            // Search Clients
            var clients = await TenantScope(_context.Clients)
                .AsNoTracking()
                .Where(c => (normalizedEntityId == null || c.Id != normalizedEntityId) &&
                            ((c.Name != null && EF.Functions.Like(c.Name, escapedPattern, "\\")) ||
                             c.NormalizedEmail.Contains(normalizedSearchQuery)))
                .Select(c => new
                {
                    c.Id,
                    c.Name,
                    c.NormalizedEmail
                })
                .Take(MaxConflictResults)
                .ToListAsync();

            var clientMatterLookup = await BuildClientMatterLookupAsync(clients.Select(c => c.Id).ToList());
            foreach (var client in clients)
            {
                var evaluation = EvaluateClientMatch(normalizedSearchQuery, client.Name, client.NormalizedEmail);
                if (evaluation == null)
                {
                    continue;
                }

                clientMatterLookup.TryGetValue(client.Id, out var clientMatter);
                results.Add(new JurisFlow.Server.Models.ConflictResult
                {
                    ConflictCheckId = conflictCheck.Id,
                    MatchedEntityType = "Client",
                    MatchedEntityId = client.Id,
                    MatchedEntityName = client.Name,
                    MatchType = evaluation.MatchType,
                    MatchScore = evaluation.Score,
                    RiskLevel = evaluation.RiskLevel,
                    RelatedMatterId = clientMatter?.Id,
                    RelatedMatterName = clientMatter?.Name,
                    Details = evaluation.Details
                });
            }

            // Search Opposing Parties
            var parties = await TenantScope(_context.OpposingParties)
                .AsNoTracking()
                .Where(p => (normalizedEntityId == null || p.Id != normalizedEntityId) &&
                            p.Name != null &&
                            EF.Functions.Like(p.Name, escapedPattern, "\\"))
                .Select(p => new
                {
                    p.Id,
                    p.Name,
                    p.MatterId
                })
                .Take(MaxConflictResults)
                .ToListAsync();

            var partyMatterLookup = await BuildMatterLookupAsync(parties.Select(p => p.MatterId).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList()!);
            foreach (var party in parties)
            {
                var evaluation = EvaluateContainsMatch(normalizedSearchQuery, party.Name, "Contains");
                if (evaluation == null)
                {
                    continue;
                }

                partyMatterLookup.TryGetValue(party.MatterId, out var partyMatter);
                results.Add(new JurisFlow.Server.Models.ConflictResult
                {
                    ConflictCheckId = conflictCheck.Id,
                    MatchedEntityType = "OpposingParty",
                    MatchedEntityId = party.Id,
                    MatchedEntityName = party.Name,
                    MatchType = evaluation.MatchType,
                    MatchScore = evaluation.Score,
                    RiskLevel = evaluation.RiskLevel,
                    RelatedMatterId = party.MatterId,
                    RelatedMatterName = partyMatter?.Name,
                    Details = evaluation.Details
                });
            }

            results = results
                .OrderByDescending(r => r.MatchScore)
                .ThenBy(r => r.MatchedEntityName)
                .Take(MaxConflictResults)
                .ToList();

            // Save results
            if (results.Any())
            {
                _context.ConflictResults.AddRange(results);
                conflictCheck.Status = ConflictStatuses.Conflict;
                conflictCheck.MatchCount = results.Count;
            }
            else
            {
                conflictCheck.Status = ConflictStatuses.Clear;
            }

            await _context.SaveChangesAsync();
            await _auditLogger.LogAsync(HttpContext, "conflict.check.run", "ConflictCheck", conflictCheck.Id,
                $"Query={RedactForAudit(normalizedSearchQuery)}, Status={conflictCheck.Status}, MatchCount={results.Count}, Tenant={tenantId}");

            return Ok(new ConflictCheckResultDto
            {
                Id = conflictCheck.Id,
                Status = conflictCheck.Status,
                MatchCount = results.Count,
                Results = results.Select(r => new ConflictResultDto
                {
                    Id = r.Id,
                    MatchedEntityType = r.MatchedEntityType,
                    MatchedEntityId = r.MatchedEntityId,
                    MatchedEntityName = r.MatchedEntityName,
                    MatchType = r.MatchType,
                    MatchScore = r.MatchScore,
                    RiskLevel = r.RiskLevel,
                    RelatedMatterId = r.RelatedMatterId,
                    RelatedMatterName = r.RelatedMatterName
                }).ToList()
            });
        }

        // GET: api/conflicts/{id}
        [HttpGet("{id}")]
        public async Task<ActionResult<ConflictCheckResultDto>> GetConflictCheck(string id)
        {
            var check = await TenantScope(_context.ConflictChecks).FirstOrDefaultAsync(c => c.Id == id);
            if (check == null) return NotFound();

            var results = await TenantScope(_context.ConflictResults)
                .AsNoTracking()
                .Where(r => r.ConflictCheckId == id)
                .ToListAsync();

            return Ok(new ConflictCheckResultDto
            {
                Id = check.Id,
                Status = check.Status,
                MatchCount = check.MatchCount,
                SearchQuery = RedactForDisplay(check.SearchQuery),
                CheckType = check.CheckType,
                WaivedBy = check.WaivedBy,
                WaiverReason = check.WaiverReason,
                Results = results.Select(r => new ConflictResultDto
                {
                    Id = r.Id,
                    MatchedEntityType = r.MatchedEntityType,
                    MatchedEntityId = r.MatchedEntityId,
                    MatchedEntityName = r.MatchedEntityName,
                    MatchType = r.MatchType,
                    MatchScore = r.MatchScore,
                    RiskLevel = r.RiskLevel,
                    RelatedMatterId = r.RelatedMatterId,
                    RelatedMatterName = r.RelatedMatterName
                }).ToList()
            });
        }

        // POST: api/conflicts/{id}/waive
        [Authorize(Roles = "Admin,Partner")]
        [HttpPost("{id}/waive")]
        public async Task<IActionResult> WaiveConflict(string id, [FromBody] WaiveConflictDto dto)
        {
            if (dto == null)
            {
                return BadRequest(new { message = "Request body is required." });
            }

            var check = await TenantScope(_context.ConflictChecks).FirstOrDefaultAsync(c => c.Id == id);
            if (check == null) return NotFound();
            if (!string.Equals(check.Status, ConflictStatuses.Conflict, StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { message = "Only conflict checks in Conflict status can be waived." });
            }

            var waiverReason = NormalizeOptional(dto.Reason, 1000);
            if (string.IsNullOrWhiteSpace(waiverReason) || waiverReason.Length < 10)
            {
                return BadRequest(new { message = "Waiver reason must be at least 10 characters." });
            }

            var userId = User.FindFirst("sub")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var previousStatus = check.Status;

            check.Status = ConflictStatuses.Waived;
            check.WaivedBy = userId;
            check.WaiverReason = waiverReason;
            check.WaivedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await _auditLogger.LogAsync(HttpContext, "conflict.check.waive", "ConflictCheck", check.Id,
                $"PreviousStatus={previousStatus}, MatchCount={check.MatchCount}, Reason={RedactForAudit(waiverReason)}");

            return Ok(new { message = "Conflict waived successfully" });
        }

        // GET: api/conflicts/history
        [HttpGet("history")]
        public async Task<ActionResult<IEnumerable<ConflictCheckSummaryDto>>> GetConflictHistory([FromQuery] int limit = 50)
        {
            var normalizedLimit = Math.Clamp(limit <= 0 ? 50 : limit, 1, 200);
            var checks = await TenantScope(_context.ConflictChecks)
                .AsNoTracking()
                .OrderByDescending(c => c.CreatedAt)
                .Take(normalizedLimit)
                .Select(c => new ConflictCheckSummaryDto
                {
                    Id = c.Id,
                    SearchQuery = RedactForDisplay(c.SearchQuery),
                    CheckType = c.CheckType,
                    Status = c.Status,
                    MatchCount = c.MatchCount,
                    CreatedAt = c.CreatedAt
                })
                .ToListAsync();

            return Ok(checks);
        }

        private IQueryable<T> TenantScope<T>(IQueryable<T> query) where T : class
        {
            var tenantId = RequireTenantId();
            return query.Where(e => EF.Property<string>(e, "TenantId") == tenantId);
        }

        private string RequireTenantId()
        {
            if (string.IsNullOrWhiteSpace(_tenantContext.TenantId))
            {
                throw new InvalidOperationException("Tenant context is required.");
            }

            return _tenantContext.TenantId;
        }

        private static string? NormalizeOptional(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var normalized = Regex.Replace(value.Trim(), "\\s+", " ");
            return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
        }

        private static string? NormalizeSearchQuery(string? value)
        {
            var normalized = NormalizeOptional(value, 120);
            return normalized?.ToLowerInvariant();
        }

        private static bool TryNormalizeOptionalValue(string? input, HashSet<string> allowedValues, out string? normalizedValue, out string? error)
        {
            var candidate = NormalizeOptional(input, 64);
            if (candidate == null)
            {
                normalizedValue = null;
                error = null;
                return true;
            }

            normalizedValue = allowedValues.FirstOrDefault(v => string.Equals(v, candidate, StringComparison.OrdinalIgnoreCase));
            if (normalizedValue != null)
            {
                error = null;
                return true;
            }

            error = $"Unsupported value '{candidate}'.";
            return false;
        }

        private static string EscapeLikePattern(string value)
        {
            return value
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("%", "\\%", StringComparison.Ordinal)
                .Replace("_", "\\_", StringComparison.Ordinal);
        }

        private async Task<Dictionary<string, MatterLookup>> BuildClientMatterLookupAsync(List<string> clientIds)
        {
            if (clientIds.Count == 0)
            {
                return new Dictionary<string, MatterLookup>(StringComparer.Ordinal);
            }

            var matters = await TenantScope(_context.Matters)
                .AsNoTracking()
                .Where(m => clientIds.Contains(m.ClientId))
                .Select(m => new { m.ClientId, m.Id, m.Name, m.OpenDate })
                .ToListAsync();

            return matters
                .GroupBy(m => m.ClientId)
                .ToDictionary(
                    g => g.Key,
                    g =>
                    {
                        var selected = g.OrderByDescending(m => m.OpenDate).First();
                        return new MatterLookup(selected.Id, selected.Name);
                    },
                    StringComparer.Ordinal);
        }

        private async Task<Dictionary<string, MatterLookup>> BuildMatterLookupAsync(List<string> matterIds)
        {
            if (matterIds.Count == 0)
            {
                return new Dictionary<string, MatterLookup>(StringComparer.Ordinal);
            }

            var matters = await TenantScope(_context.Matters)
                .AsNoTracking()
                .Where(m => matterIds.Contains(m.Id))
                .Select(m => new { m.Id, m.Name })
                .ToListAsync();

            return matters.ToDictionary(m => m.Id, m => new MatterLookup(m.Id, m.Name), StringComparer.Ordinal);
        }

        private static MatchEvaluation? EvaluateClientMatch(string searchQuery, string? name, string normalizedEmail)
        {
            var nameMatch = EvaluateContainsMatch(searchQuery, name, "Contains");

            MatchEvaluation? emailMatch = null;
            if (!string.IsNullOrWhiteSpace(normalizedEmail) && normalizedEmail.Contains(searchQuery, StringComparison.Ordinal))
            {
                var isExact = string.Equals(normalizedEmail, searchQuery, StringComparison.Ordinal);
                emailMatch = new MatchEvaluation(
                    isExact ? "EmailExact" : "EmailContains",
                    isExact ? 100 : 92,
                    isExact ? "High" : "High",
                    "Matched against a protected client email.");
            }

            if (emailMatch == null) return nameMatch;
            if (nameMatch == null) return emailMatch;
            return emailMatch.Score >= nameMatch.Score ? emailMatch : nameMatch;
        }

        private static MatchEvaluation? EvaluateContainsMatch(string searchQuery, string? candidate, string defaultMatchType)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return null;
            }

            var normalizedCandidate = candidate.Trim();
            if (!normalizedCandidate.Contains(searchQuery, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (string.Equals(normalizedCandidate, searchQuery, StringComparison.OrdinalIgnoreCase))
            {
                return new MatchEvaluation("Exact", 100, "High", "Exact match on protected entity data.");
            }

            var tokenBoundaryMatch = Regex.IsMatch(normalizedCandidate, $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(searchQuery)}(?![\p{{L}}\p{{N}}])", RegexOptions.IgnoreCase);
            var coverageRatio = Math.Min(1d, (double)searchQuery.Length / Math.Max(1, normalizedCandidate.Length));
            var baseScore = tokenBoundaryMatch ? 85d : 70d;
            var score = Math.Min(99d, baseScore + Math.Round(coverageRatio * 15d, 1));
            var riskLevel = score >= 90d ? "High" : score >= 75d ? "Medium" : "Low";
            var matchType = tokenBoundaryMatch ? "WordMatch" : defaultMatchType;

            return new MatchEvaluation(matchType, score, riskLevel, $"Matched by {matchType} on protected entity data.");
        }

        private static string? RedactForDisplay(string? value)
        {
            var redacted = RedactForAudit(value);
            return string.IsNullOrWhiteSpace(redacted) ? null : redacted;
        }

        private static string RedactForAudit(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            if (value.Length <= 8)
            {
                return value.Length <= 2 ? value : $"{value[..2]}***";
            }

            return $"{value[..4]}***{value[^2..]}";
        }

        private sealed record MatterLookup(string Id, string Name);
        private sealed record MatchEvaluation(string MatchType, double Score, string RiskLevel, string Details);
    }

    // DTOs
    public class ConflictCheckRequestDto
    {
        public string SearchQuery { get; set; } = string.Empty;
        public string? CheckType { get; set; }
        public string? EntityType { get; set; }
        public string? EntityId { get; set; }
    }

    public class ConflictCheckResultDto
    {
        public string Id { get; set; } = "";
        public string Status { get; set; } = "";
        public int MatchCount { get; set; }
        public string? SearchQuery { get; set; }
        public string? CheckType { get; set; }
        public string? WaivedBy { get; set; }
        public string? WaiverReason { get; set; }
        public List<ConflictResultDto> Results { get; set; } = new();
    }

    public class ConflictResultDto
    {
        public string Id { get; set; } = "";
        public string MatchedEntityType { get; set; } = "";
        public string MatchedEntityId { get; set; } = "";
        public string MatchedEntityName { get; set; } = "";
        public string MatchType { get; set; } = "";
        public double MatchScore { get; set; }
        public string RiskLevel { get; set; } = "";
        public string? RelatedMatterId { get; set; }
        public string? RelatedMatterName { get; set; }
    }

    public class WaiveConflictDto
    {
        public string Reason { get; set; } = string.Empty;
    }

    public class ConflictCheckSummaryDto
    {
        public string Id { get; set; } = "";
        public string? SearchQuery { get; set; }
        public string? CheckType { get; set; }
        public string Status { get; set; } = "";
        public int MatchCount { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
