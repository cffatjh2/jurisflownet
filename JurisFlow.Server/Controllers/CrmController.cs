using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using JurisFlow.Server.Services;
using System.Security.Cryptography;
using System.Text;

namespace JurisFlow.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = "StaffOnly")]
    public class CrmController : ControllerBase
    {
        private readonly JurisFlowDbContext _context;
        private readonly TenantContext _tenantContext;
        private readonly AuditLogger _auditLogger;
        private const int MaxPerCategory = 15;
        private const int MaxTotalResults = 50;

        public CrmController(JurisFlowDbContext context, TenantContext tenantContext, AuditLogger auditLogger)
        {
            _context = context;
            _tenantContext = tenantContext;
            _auditLogger = auditLogger;
        }

        /// <summary>
        /// Conflict of Interest Check - ABA Model Rules 1.7, 1.9, 1.10
        /// Searches across Clients, Leads, and Matters to identify potential conflicts
        /// </summary>
        [EnableRateLimiting("CrmConflictSearch")]
        [HttpPost("conflict-check")]
        public async Task<ActionResult<IEnumerable<ConflictCheckResult>>> ConflictCheck([FromBody] CrmConflictCheckRequest request)
        {
            var tenantId = RequireTenantId();
            var normalizedQuery = NormalizeQuery(request.SearchQuery);
            if (string.IsNullOrWhiteSpace(normalizedQuery) || normalizedQuery.Length < 3)
            {
                return BadRequest(new { message = "Search query must be at least 3 characters" });
            }

            if (normalizedQuery.Length > 120)
            {
                return BadRequest(new { message = "Search query must be at most 120 characters" });
            }

            var query = normalizedQuery.ToLowerInvariant();
            var escapedPattern = $"%{EscapeLikePattern(normalizedQuery)}%";
            var canSearchSensitive = User.IsInRole("Admin") || User.IsInRole("SecurityAdmin");
            var results = new List<ConflictCheckResult>();

            // 1. Search Clients (Current clients = HIGH risk if opposing)
            var clientsQuery = TenantScope(_context.Clients).AsNoTracking()
                .Where(c => 
                    (c.Name != null && EF.Functions.Like(c.Name, escapedPattern, "\\")) ||
                    c.NormalizedEmail.Contains(query) ||
                    (c.Company != null && EF.Functions.Like(c.Company, escapedPattern, "\\")) ||
                    (c.ClientNumber != null && EF.Functions.Like(c.ClientNumber, escapedPattern, "\\")));
            if (canSearchSensitive)
            {
                clientsQuery = clientsQuery.Where(c =>
                    (c.Name != null && EF.Functions.Like(c.Name, escapedPattern, "\\")) ||
                    c.NormalizedEmail.Contains(query) ||
                    (c.Company != null && EF.Functions.Like(c.Company, escapedPattern, "\\")) ||
                    (c.ClientNumber != null && EF.Functions.Like(c.ClientNumber, escapedPattern, "\\")) ||
                    (c.TaxId != null && EF.Functions.Like(c.TaxId, escapedPattern, "\\")));
            }

            var clients = await clientsQuery
                .Select(c => new { c.Id, c.Name, c.Email, c.ClientNumber, c.Company, c.Status })
                .Take(MaxPerCategory)
                .ToListAsync();

            foreach (var client in clients)
            {
                var isActive = string.Equals(client.Status, "Active", StringComparison.OrdinalIgnoreCase);
                results.Add(new ConflictCheckResult
                {
                    Id = client.Id,
                    Name = client.Name,
                    Type = "Client",
                    Detail = client.ClientNumber ?? client.Company,
                    Status = client.Status ?? "Active",
                    RiskLevel = isActive ? "high" : "medium",
                    ConflictReason = isActive
                        ? "Potential conflict with an existing client record. Current client relationship review is required."
                        : "Potential conflict with a prior or inactive client record. Former-client review may be required."
                });
            }

            // 2. Search Leads (Potential clients = MEDIUM risk)
            var leads = await TenantScope(_context.Leads)
                .AsNoTracking()
                .Where(l =>
                    (l.Name != null && EF.Functions.Like(l.Name, escapedPattern, "\\")) ||
                    (l.Email != null && EF.Functions.Like(l.Email, escapedPattern, "\\")))
                .Select(l => new { l.Id, l.Name, l.Status, l.PracticeArea })
                .Take(MaxPerCategory)
                .ToListAsync();

            foreach (var lead in leads)
            {
                results.Add(new ConflictCheckResult
                {
                    Id = lead.Id,
                    Name = lead.Name,
                    Type = "Lead",
                    Detail = lead.PracticeArea,
                    Status = lead.Status,
                    RiskLevel = "low",
                    ConflictReason = "Potential related CRM lead. Review whether any consultation or confidential intake occurred."
                });
            }

            // 3. Search Matters (Case names & numbers - for reference)
            var matters = await TenantScope(_context.Matters)
                .AsNoTracking()
                .Where(m => 
                    (m.Name != null && EF.Functions.Like(m.Name, escapedPattern, "\\")) ||
                    (m.CaseNumber != null && EF.Functions.Like(m.CaseNumber, escapedPattern, "\\")))
                .Select(m => new { m.Id, m.Name, m.CaseNumber, m.Status })
                .Take(MaxPerCategory)
                .ToListAsync();

            foreach (var matter in matters)
            {
                results.Add(new ConflictCheckResult
                {
                    Id = matter.Id,
                    Name = matter.Name,
                    Type = "Matter",
                    Detail = matter.CaseNumber,
                    Status = matter.Status.ToString(),
                    RiskLevel = "low",
                    ConflictReason = "Related matter found. Review for potential overlap, prior representation, or confidentiality concerns."
                });
            }

            // 4. Search Opposing Parties (Previous opponents = HIGH risk)
            var opposingQuery = TenantScope(_context.OpposingParties).AsNoTracking()
                .Where(op => 
                    (op.Name != null && EF.Functions.Like(op.Name, escapedPattern, "\\")) ||
                    (op.Company != null && EF.Functions.Like(op.Company, escapedPattern, "\\")) ||
                    (op.CounselName != null && EF.Functions.Like(op.CounselName, escapedPattern, "\\")) ||
                    (op.CounselFirm != null && EF.Functions.Like(op.CounselFirm, escapedPattern, "\\")));
            if (canSearchSensitive)
            {
                opposingQuery = opposingQuery.Where(op =>
                    (op.Name != null && EF.Functions.Like(op.Name, escapedPattern, "\\")) ||
                    (op.Company != null && EF.Functions.Like(op.Company, escapedPattern, "\\")) ||
                    (op.CounselName != null && EF.Functions.Like(op.CounselName, escapedPattern, "\\")) ||
                    (op.CounselFirm != null && EF.Functions.Like(op.CounselFirm, escapedPattern, "\\")) ||
                    (op.TaxId != null && EF.Functions.Like(op.TaxId, escapedPattern, "\\")));
            }

            var opposingParties = await opposingQuery
                .Select(op => new { op.Id, op.Name, op.Company, op.Type, op.MatterId })
                .Take(MaxPerCategory)
                .ToListAsync();

            foreach (var op in opposingParties)
            {
                results.Add(new ConflictCheckResult
                {
                    Id = op.Id,
                    Name = op.Name,
                    Type = "OpposingParty",
                    Detail = op.Company ?? op.Type,
                    Status = "Related Opposing Party",
                    RiskLevel = "medium",
                    ConflictReason = "Potential related opposing-party record. Review for former-client, related-party, and screening considerations."
                });
            }

            var finalResults = results
                .OrderByDescending(r => RiskRank(r.RiskLevel))
                .ThenBy(r => r.Type)
                .ThenBy(r => r.Name)
                .Take(MaxTotalResults)
                .ToList();

            await _auditLogger.LogAsync(HttpContext, "crm.conflict_check", "Crm", "conflict-check",
                $"Tenant={tenantId}, QueryHash={ComputeQueryHash(normalizedQuery)}, QueryLength={normalizedQuery.Length}, Sensitive={canSearchSensitive}, Results={finalResults.Count}");

            return Ok(finalResults);
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

        private static string NormalizeQuery(string? query)
        {
            return string.IsNullOrWhiteSpace(query)
                ? string.Empty
                : string.Join(" ", query.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }

        private static string EscapeLikePattern(string value)
        {
            return value
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("%", "\\%", StringComparison.Ordinal)
                .Replace("_", "\\_", StringComparison.Ordinal);
        }

        private static int RiskRank(string? riskLevel)
        {
            return riskLevel?.ToLowerInvariant() switch
            {
                "high" => 3,
                "medium" => 2,
                _ => 1
            };
        }

        private static string ComputeQueryHash(string query)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(query));
            return Convert.ToHexString(bytes[..8]);
        }
    }

    /// <summary>
    /// Result object for conflict check search
    /// </summary>
    public class ConflictCheckResult
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty; // Client, Lead, Matter, OpposingParty
        public string? Detail { get; set; }
        public string Status { get; set; } = string.Empty;
        public string RiskLevel { get; set; } = "low"; // high, medium, low
        public string? ConflictReason { get; set; }
    }

    public sealed class CrmConflictCheckRequest
    {
        public string SearchQuery { get; set; } = string.Empty;
    }
}

