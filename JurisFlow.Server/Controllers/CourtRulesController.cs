using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using Microsoft.AspNetCore.Authorization;
using JurisFlow.Server.Services;
using Microsoft.Extensions.Caching.Memory;
using System.ComponentModel.DataAnnotations;

namespace JurisFlow.Server.Controllers
{
    [Route("api/court-rules")]
    [ApiController]
    [Authorize(Policy = "StaffOnly")]
    public class CourtRulesController : ControllerBase
    {
        private readonly JurisFlowDbContext _context;
        private readonly IMemoryCache _cache;
        private readonly TenantContext _tenantContext;
        private readonly AuditLogger _auditLogger;
        private const string CacheVersionKey = "court-rules:version";
        private static readonly HashSet<string> AllowedRuleTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "Federal",
            "State",
            "Local"
        };
        private static readonly HashSet<string> AllowedDayTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "Calendar",
            "Court"
        };
        private static readonly HashSet<string> AllowedDirections = new(StringComparer.OrdinalIgnoreCase)
        {
            "Before",
            "After"
        };

        public CourtRulesController(JurisFlowDbContext context, IMemoryCache cache, TenantContext tenantContext, AuditLogger auditLogger)
        {
            _context = context;
            _cache = cache;
            _tenantContext = tenantContext;
            _auditLogger = auditLogger;
        }

        // GET: api/court-rules
        [HttpGet]
        public async Task<ActionResult<IEnumerable<CourtRuleListDto>>> GetCourtRules(
            [FromQuery] string? jurisdiction = null,
            [FromQuery] string? ruleType = null,
            [FromQuery] string? triggerEvent = null)
        {
            try
            {
                var tenantKey = RequireTenantId();
                var normalizedJurisdiction = NormalizeFilterValue(jurisdiction, 64);
                var normalizedRuleType = NormalizeAllowedFilter(ruleType, AllowedRuleTypes, "Invalid ruleType.");
                var normalizedTriggerEvent = NormalizeFilterValue(triggerEvent, 120);
                var cacheKey = $"court-rules:list:{tenantKey}:{GetCacheVersion(tenantKey)}:{NormalizeCacheKey(normalizedJurisdiction)}:{NormalizeCacheKey(normalizedRuleType)}:{NormalizeCacheKey(normalizedTriggerEvent)}";
                if (_cache.TryGetValue(cacheKey, out List<CourtRuleListDto>? cached) && cached != null)
                {
                    return Ok(cached);
                }

                var query = TenantScope(_context.CourtRules).AsNoTracking().Where(r => r.IsActive);

                if (!string.IsNullOrEmpty(normalizedJurisdiction))
                {
                    query = query.Where(r => r.Jurisdiction == normalizedJurisdiction);
                }

                if (!string.IsNullOrEmpty(normalizedRuleType))
                {
                    query = query.Where(r => r.RuleType == normalizedRuleType);
                }

                if (!string.IsNullOrEmpty(normalizedTriggerEvent))
                {
                    query = query.Where(r => EF.Functions.Like(r.TriggerEvent, EscapeLikePattern(normalizedTriggerEvent), "\\"));
                }

                var rules = await query
                    .OrderBy(r => r.Jurisdiction)
                    .ThenBy(r => r.TriggerEvent)
                    .Select(r => ToCourtRuleListDto(r))
                    .ToListAsync();

                _cache.Set(cacheKey, rules, new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
                });

                return Ok(rules);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // GET: api/court-rules/{id}
        [HttpGet("{id}")]
        public async Task<ActionResult<CourtRuleDetailDto>> GetCourtRule(string id)
        {
            var rule = await TenantScope(_context.CourtRules)
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == id && r.IsActive);
            if (rule == null)
            {
                return NotFound();
            }

            return Ok(ToCourtRuleDetailDto(rule));
        }

        // GET: api/court-rules/jurisdictions
        [HttpGet("jurisdictions")]
        public async Task<ActionResult<IEnumerable<string>>> GetJurisdictions()
        {
            var tenantKey = RequireTenantId();
            var cacheKey = $"court-rules:jurisdictions:{tenantKey}:{GetCacheVersion(tenantKey)}";
            if (_cache.TryGetValue(cacheKey, out List<string>? cached) && cached != null)
            {
                return Ok(cached);
            }

            var jurisdictions = await TenantScope(_context.CourtRules)
                .AsNoTracking()
                .Where(r => r.IsActive)
                .Select(r => r.Jurisdiction)
                .Distinct()
                .OrderBy(j => j)
                .ToListAsync();

            _cache.Set(cacheKey, jurisdictions, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
            });

            return Ok(jurisdictions);
        }

        // GET: api/court-rules/trigger-events
        [HttpGet("trigger-events")]
        public async Task<ActionResult<IEnumerable<string>>> GetTriggerEvents([FromQuery] string? jurisdiction = null)
        {
            try
            {
                var tenantKey = RequireTenantId();
                var normalizedJurisdiction = NormalizeFilterValue(jurisdiction, 64);
                var cacheKey = $"court-rules:trigger-events:{tenantKey}:{GetCacheVersion(tenantKey)}:{NormalizeCacheKey(normalizedJurisdiction)}";
                if (_cache.TryGetValue(cacheKey, out List<string>? cached) && cached != null)
                {
                    return Ok(cached);
                }

                var query = TenantScope(_context.CourtRules).AsNoTracking().Where(r => r.IsActive);

                if (!string.IsNullOrEmpty(normalizedJurisdiction))
                {
                    query = query.Where(r => r.Jurisdiction == normalizedJurisdiction);
                }

                var events = await query
                    .Select(r => r.TriggerEvent)
                    .Distinct()
                    .OrderBy(e => e)
                    .ToListAsync();

                _cache.Set(cacheKey, events, new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
                });

                return Ok(events);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // POST: api/court-rules
        [Authorize(Roles = "Admin,Partner")]
        [HttpPost]
        public async Task<ActionResult<CourtRuleDetailDto>> CreateCourtRule([FromBody] CourtRuleCreateDto dto)
        {
            try
            {
                if (dto == null)
                {
                    return BadRequest(new { message = "Request body is required." });
                }

                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var rule = CreateCourtRuleEntity(dto);
                rule.Id = Guid.NewGuid().ToString();
                rule.CreatedAt = DateTime.UtcNow;
                rule.UpdatedAt = DateTime.UtcNow;

                _context.CourtRules.Add(rule);
                await _context.SaveChangesAsync();
                BumpCacheVersion();
                await _auditLogger.LogAsync(HttpContext, "court_rule.create", "CourtRule", rule.Id, $"Jurisdiction={rule.Jurisdiction}, Trigger={rule.TriggerEvent}");

                return CreatedAtAction(nameof(GetCourtRule), new { id = rule.Id }, ToCourtRuleDetailDto(rule));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // PUT: api/court-rules/{id}
        [Authorize(Roles = "Admin,Partner")]
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateCourtRule(string id, [FromBody] CourtRuleUpdateDto updatedRule)
        {
            try
            {
                if (updatedRule == null)
                {
                    return BadRequest(new { message = "Request body is required." });
                }

                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var rule = await TenantScope(_context.CourtRules).FirstOrDefaultAsync(r => r.Id == id);
                if (rule == null)
                {
                    return NotFound();
                }

                ApplyCourtRuleUpdate(rule, updatedRule);
                rule.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();
                BumpCacheVersion();
                await _auditLogger.LogAsync(HttpContext, "court_rule.update", "CourtRule", rule.Id, $"Jurisdiction={rule.Jurisdiction}, Trigger={rule.TriggerEvent}, Active={rule.IsActive}");

                return Ok(ToCourtRuleDetailDto(rule));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // DELETE: api/court-rules/{id}
        [Authorize(Roles = "Admin,Partner")]
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteCourtRule(string id)
        {
            var rule = await TenantScope(_context.CourtRules).FirstOrDefaultAsync(r => r.Id == id);
            if (rule == null)
            {
                return NotFound();
            }

            // Soft delete
            rule.IsActive = false;
            rule.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            BumpCacheVersion();
            await _auditLogger.LogAsync(HttpContext, "court_rule.delete", "CourtRule", rule.Id, $"Jurisdiction={rule.Jurisdiction}, Trigger={rule.TriggerEvent}");

            return NoContent();
        }

        // POST: api/court-rules/seed
        [Authorize(Roles = "Admin,Partner")]
        [HttpPost("seed")]
        public async Task<IActionResult> SeedDefaultRules()
        {
            var tenantId = RequireTenantId();
            // Check if already seeded
            if (await TenantScope(_context.CourtRules).AnyAsync())
            {
                return BadRequest(new { message = "Rules already exist. Delete existing rules first." });
            }

            var defaultRules = GetDefaultRules();

            _context.CourtRules.AddRange(defaultRules);
            await _context.SaveChangesAsync();
            BumpCacheVersion();
            await _auditLogger.LogAsync(HttpContext, "court_rule.seed", "CourtRule", tenantId, $"Seeded {defaultRules.Count} rules");

            return Ok(new { message = $"Seeded {defaultRules.Count} court rules", count = defaultRules.Count });
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

        private int GetCacheVersion(string tenantKey)
        {
            var versionKey = $"{CacheVersionKey}:{tenantKey}";
            if (_cache.TryGetValue(versionKey, out int version))
            {
                return version;
            }
            _cache.Set(versionKey, 0);
            return 0;
        }

        private void BumpCacheVersion()
        {
            var tenantKey = RequireTenantId();
            var versionKey = $"{CacheVersionKey}:{tenantKey}";
            var next = GetCacheVersion(tenantKey) + 1;
            _cache.Set(versionKey, next);
        }

        private static string NormalizeCacheKey(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
        }

        private static string? NormalizeFilterValue(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var trimmed = value.Trim();
            if (trimmed.Length > maxLength)
            {
                throw new InvalidOperationException("Filter value is too long.");
            }

            return trimmed;
        }

        private static string? NormalizeAllowedFilter(string? value, HashSet<string> allowedValues, string errorMessage)
        {
            var normalized = NormalizeFilterValue(value, 32);
            if (normalized == null)
            {
                return null;
            }

            var allowed = allowedValues.FirstOrDefault(v => string.Equals(v, normalized, StringComparison.OrdinalIgnoreCase));
            if (allowed == null)
            {
                throw new InvalidOperationException(errorMessage);
            }

            return allowed;
        }

        private static string EscapeLikePattern(string value)
        {
            return value
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("%", "\\%", StringComparison.Ordinal)
                .Replace("_", "\\_", StringComparison.Ordinal);
        }

        private static CourtRule CreateCourtRuleEntity(CourtRuleCreateDto dto)
        {
            var rule = new CourtRule();
            ApplyCourtRuleUpdate(rule, dto);
            return rule;
        }

        private static void ApplyCourtRuleUpdate(CourtRule rule, CourtRuleWriteDto dto)
        {
            rule.Name = NormalizeRequired(dto.Name, 200, "Name is required.");
            rule.RuleType = NormalizeAllowedRequired(dto.RuleType, AllowedRuleTypes, "Invalid rule type.");
            rule.Jurisdiction = NormalizeRequired(dto.Jurisdiction, 64, "Jurisdiction is required.");
            rule.CourtType = NormalizeOptionalText(dto.CourtType, 64);
            rule.Citation = NormalizeOptionalText(dto.Citation, 200);
            rule.TriggerEvent = NormalizeRequired(dto.TriggerEvent, 200, "TriggerEvent is required.");
            if (dto.DaysCount < 0 || dto.DaysCount > 3650)
            {
                throw new InvalidOperationException("DaysCount must be between 0 and 3650.");
            }
            rule.DaysCount = dto.DaysCount;
            rule.DayType = NormalizeAllowedRequired(dto.DayType, AllowedDayTypes, "Invalid day type.");
            rule.Direction = NormalizeAllowedRequired(dto.Direction, AllowedDirections, "Invalid direction.");
            if (dto.ServiceDaysAdd < 0 || dto.ServiceDaysAdd > 365)
            {
                throw new InvalidOperationException("ServiceDaysAdd must be between 0 and 365.");
            }
            rule.ServiceDaysAdd = dto.ServiceDaysAdd;
            rule.Description = NormalizeOptionalText(dto.Description, 4000);
            rule.ExtendIfWeekend = dto.ExtendIfWeekend;
            rule.IsActive = dto.IsActive;
        }

        private static string NormalizeRequired(string? value, int maxLength, string errorMessage)
        {
            var normalized = NormalizeOptionalText(value, maxLength);
            return normalized ?? throw new InvalidOperationException(errorMessage);
        }

        private static string NormalizeAllowedRequired(string? value, HashSet<string> allowedValues, string errorMessage)
        {
            var normalized = NormalizeOptionalText(value, 32) ?? throw new InvalidOperationException(errorMessage);
            var allowed = allowedValues.FirstOrDefault(v => string.Equals(v, normalized, StringComparison.OrdinalIgnoreCase));
            return allowed ?? throw new InvalidOperationException(errorMessage);
        }

        private static string? NormalizeOptionalText(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var trimmed = value.Trim();
            if (trimmed.Length > maxLength)
            {
                throw new InvalidOperationException("Field value is too long.");
            }

            return trimmed;
        }

        private static CourtRuleListDto ToCourtRuleListDto(CourtRule rule)
        {
            return new CourtRuleListDto
            {
                Id = rule.Id,
                Name = rule.Name,
                RuleType = rule.RuleType,
                Jurisdiction = rule.Jurisdiction,
                CourtType = rule.CourtType,
                Citation = rule.Citation,
                TriggerEvent = rule.TriggerEvent,
                DaysCount = rule.DaysCount,
                DayType = rule.DayType,
                Direction = rule.Direction,
                ServiceDaysAdd = rule.ServiceDaysAdd,
                ExtendIfWeekend = rule.ExtendIfWeekend,
                IsActive = rule.IsActive,
                UpdatedAt = rule.UpdatedAt
            };
        }

        private static CourtRuleDetailDto ToCourtRuleDetailDto(CourtRule rule)
        {
            return new CourtRuleDetailDto
            {
                Id = rule.Id,
                Name = rule.Name,
                RuleType = rule.RuleType,
                Jurisdiction = rule.Jurisdiction,
                CourtType = rule.CourtType,
                Citation = rule.Citation,
                TriggerEvent = rule.TriggerEvent,
                DaysCount = rule.DaysCount,
                DayType = rule.DayType,
                Direction = rule.Direction,
                ServiceDaysAdd = rule.ServiceDaysAdd,
                Description = rule.Description,
                ExtendIfWeekend = rule.ExtendIfWeekend,
                IsActive = rule.IsActive,
                CreatedAt = rule.CreatedAt,
                UpdatedAt = rule.UpdatedAt
            };
        }

        private List<CourtRule> GetDefaultRules()
        {
            return new List<CourtRule>
            {
                // Federal Rules
                new CourtRule
                {
                    Name = "Answer to Complaint",
                    RuleType = "Federal",
                    Jurisdiction = "US-Federal",
                    Citation = "FRCP Rule 12(a)(1)(A)(i)",
                    TriggerEvent = "Service of Summons and Complaint",
                    DaysCount = 21,
                    DayType = "Calendar",
                    Direction = "After",
                    Description = "Defendant must answer within 21 days after service"
                },
                new CourtRule
                {
                    Name = "Motion Response",
                    RuleType = "Federal",
                    Jurisdiction = "US-Federal",
                    Citation = "Local Rules",
                    TriggerEvent = "Motion Filing",
                    DaysCount = 14,
                    DayType = "Calendar",
                    Direction = "After",
                    Description = "Opposition to motion due 14 days after filing"
                },
                new CourtRule
                {
                    Name = "Reply Brief",
                    RuleType = "Federal",
                    Jurisdiction = "US-Federal",
                    Citation = "Local Rules",
                    TriggerEvent = "Opposition Filing",
                    DaysCount = 7,
                    DayType = "Calendar",
                    Direction = "After",
                    Description = "Reply brief due 7 days after opposition"
                },

                // California State Rules
                new CourtRule
                {
                    Name = "Answer to Complaint",
                    RuleType = "State",
                    Jurisdiction = "US-CA",
                    CourtType = "Superior",
                    Citation = "CCP Sec. 412.20",
                    TriggerEvent = "Service of Summons and Complaint",
                    DaysCount = 30,
                    DayType = "Calendar",
                    Direction = "After",
                    Description = "30 days to file answer in California"
                },
                new CourtRule
                {
                    Name = "Motion Hearing Notice",
                    RuleType = "State",
                    Jurisdiction = "US-CA",
                    CourtType = "Superior",
                    Citation = "CCP Sec. 1005(b)",
                    TriggerEvent = "Motion Hearing Date",
                    DaysCount = 16,
                    DayType = "Court",
                    Direction = "Before",
                    ServiceDaysAdd = 5,
                    Description = "Motion must be filed 16 court days before hearing, +5 for mail service"
                },
                new CourtRule
                {
                    Name = "Opposition to Motion",
                    RuleType = "State",
                    Jurisdiction = "US-CA",
                    CourtType = "Superior",
                    Citation = "CCP Sec. 1005(b)",
                    TriggerEvent = "Motion Hearing Date",
                    DaysCount = 9,
                    DayType = "Court",
                    Direction = "Before",
                    Description = "Opposition due 9 court days before hearing"
                },
                new CourtRule
                {
                    Name = "Reply to Opposition",
                    RuleType = "State",
                    Jurisdiction = "US-CA",
                    CourtType = "Superior",
                    Citation = "CCP Sec. 1005(b)",
                    TriggerEvent = "Motion Hearing Date",
                    DaysCount = 5,
                    DayType = "Court",
                    Direction = "Before",
                    Description = "Reply due 5 court days before hearing"
                },

                // New York State Rules
                new CourtRule
                {
                    Name = "Answer to Complaint",
                    RuleType = "State",
                    Jurisdiction = "US-NY",
                    CourtType = "Supreme",
                    Citation = "CPLR Sec. 320(a)",
                    TriggerEvent = "Service of Summons and Complaint",
                    DaysCount = 20,
                    DayType = "Calendar",
                    Direction = "After",
                    ServiceDaysAdd = 5,
                    Description = "20 days to answer if personally served, +5 if by mail"
                },
                new CourtRule
                {
                    Name = "Motion Notice",
                    RuleType = "State",
                    Jurisdiction = "US-NY",
                    CourtType = "Supreme",
                    Citation = "CPLR Sec. 2214(b)",
                    TriggerEvent = "Motion Hearing Date",
                    DaysCount = 8,
                    DayType = "Calendar",
                    Direction = "Before",
                    Description = "Motion papers must be served at least 8 days before return date"
                },

                // Texas State Rules
                new CourtRule
                {
                    Name = "Answer to Petition",
                    RuleType = "State",
                    Jurisdiction = "US-TX",
                    CourtType = "District",
                    Citation = "TRCP Rule 99",
                    TriggerEvent = "Service of Citation",
                    DaysCount = 20,
                    DayType = "Calendar",
                    Direction = "After",
                    ExtendIfWeekend = true,
                    Description = "Answer due by 10:00 AM on the Monday following 20 days"
                },

                // Federal Appellate
                new CourtRule
                {
                    Name = "Notice of Appeal (Civil)",
                    RuleType = "Federal",
                    Jurisdiction = "US-Federal-Appellate",
                    CourtType = "Court of Appeals",
                    Citation = "FRAP 4(a)(1)(A)",
                    TriggerEvent = "Entry of Final Judgment",
                    DaysCount = 30,
                    DayType = "Calendar",
                    Direction = "After",
                    Description = "Baseline federal civil appeal deadline; verify district-specific exceptions."
                },
                new CourtRule
                {
                    Name = "Opening Brief",
                    RuleType = "Federal",
                    Jurisdiction = "US-Federal-Appellate",
                    CourtType = "Court of Appeals",
                    Citation = "FRAP 31(a)(1)",
                    TriggerEvent = "Record Filed",
                    DaysCount = 40,
                    DayType = "Calendar",
                    Direction = "After",
                    Description = "Default opening brief interval; always verify circuit scheduling orders."
                },

                // Florida
                new CourtRule
                {
                    Name = "Answer to Complaint",
                    RuleType = "State",
                    Jurisdiction = "US-FL",
                    CourtType = "Circuit",
                    Citation = "Fla. R. Civ. P. 1.140(a)(1)",
                    TriggerEvent = "Service of Summons and Complaint",
                    DaysCount = 20,
                    DayType = "Calendar",
                    Direction = "After",
                    Description = "Baseline Florida answer deadline; confirm service method impacts."
                },
                new CourtRule
                {
                    Name = "Response to Request for Production",
                    RuleType = "State",
                    Jurisdiction = "US-FL",
                    CourtType = "Circuit",
                    Citation = "Fla. R. Civ. P. 1.350(b)",
                    TriggerEvent = "Service of Request for Production",
                    DaysCount = 30,
                    DayType = "Calendar",
                    Direction = "After",
                    Description = "Default discovery response period."
                },

                // Illinois
                new CourtRule
                {
                    Name = "Appearance and Answer",
                    RuleType = "State",
                    Jurisdiction = "US-IL",
                    CourtType = "Circuit",
                    Citation = "Ill. Sup. Ct. R. 181(a)",
                    TriggerEvent = "Service of Summons and Complaint",
                    DaysCount = 30,
                    DayType = "Calendar",
                    Direction = "After",
                    Description = "Baseline Illinois appearance/answer deadline for civil matters."
                },
                new CourtRule
                {
                    Name = "Response to Interrogatories",
                    RuleType = "State",
                    Jurisdiction = "US-IL",
                    CourtType = "Circuit",
                    Citation = "Ill. Sup. Ct. R. 213(d)",
                    TriggerEvent = "Service of Interrogatories",
                    DaysCount = 28,
                    DayType = "Calendar",
                    Direction = "After",
                    Description = "Template interval; verify local scheduling order."
                },

                // New Jersey
                new CourtRule
                {
                    Name = "Answer to Complaint",
                    RuleType = "State",
                    Jurisdiction = "US-NJ",
                    CourtType = "Superior",
                    Citation = "N.J. Ct. R. 4:6-1(a)",
                    TriggerEvent = "Service of Summons and Complaint",
                    DaysCount = 35,
                    DayType = "Calendar",
                    Direction = "After",
                    Description = "Baseline New Jersey answer deadline."
                },
                new CourtRule
                {
                    Name = "Motion Return Date Notice",
                    RuleType = "State",
                    Jurisdiction = "US-NJ",
                    CourtType = "Superior",
                    Citation = "N.J. Ct. R. 1:6-3(a)",
                    TriggerEvent = "Motion Return Date",
                    DaysCount = 24,
                    DayType = "Calendar",
                    Direction = "Before",
                    Description = "Template motion notice lead time; verify practice part directives."
                },

                // Massachusetts
                new CourtRule
                {
                    Name = "Answer to Complaint",
                    RuleType = "State",
                    Jurisdiction = "US-MA",
                    CourtType = "Superior",
                    Citation = "Mass. R. Civ. P. 12(a)(1)",
                    TriggerEvent = "Service of Summons and Complaint",
                    DaysCount = 20,
                    DayType = "Calendar",
                    Direction = "After",
                    Description = "Baseline Massachusetts answer deadline."
                },
                new CourtRule
                {
                    Name = "Response to Request for Admissions",
                    RuleType = "State",
                    Jurisdiction = "US-MA",
                    CourtType = "Superior",
                    Citation = "Mass. R. Civ. P. 36(a)",
                    TriggerEvent = "Service of Requests for Admission",
                    DaysCount = 30,
                    DayType = "Calendar",
                    Direction = "After",
                    Description = "Default admissions response period."
                },

                // Washington
                new CourtRule
                {
                    Name = "Answer to Complaint",
                    RuleType = "State",
                    Jurisdiction = "US-WA",
                    CourtType = "Superior",
                    Citation = "Wash. CR 12(a)(1)",
                    TriggerEvent = "Service of Summons and Complaint",
                    DaysCount = 20,
                    DayType = "Calendar",
                    Direction = "After",
                    Description = "Baseline Washington answer deadline."
                },
                new CourtRule
                {
                    Name = "Response to Interrogatories",
                    RuleType = "State",
                    Jurisdiction = "US-WA",
                    CourtType = "Superior",
                    Citation = "Wash. CR 33(a)",
                    TriggerEvent = "Service of Interrogatories",
                    DaysCount = 30,
                    DayType = "Calendar",
                    Direction = "After",
                    Description = "Default discovery response period."
                },

                // Pennsylvania
                new CourtRule
                {
                    Name = "Answer to Complaint",
                    RuleType = "State",
                    Jurisdiction = "US-PA",
                    CourtType = "Court of Common Pleas",
                    Citation = "Pa. R.C.P. 1026(a)",
                    TriggerEvent = "Service of Complaint",
                    DaysCount = 20,
                    DayType = "Calendar",
                    Direction = "After",
                    Description = "Baseline Pennsylvania answer deadline."
                },
                new CourtRule
                {
                    Name = "Response to Requests for Admission",
                    RuleType = "State",
                    Jurisdiction = "US-PA",
                    CourtType = "Court of Common Pleas",
                    Citation = "Pa. R.C.P. 4014(b)",
                    TriggerEvent = "Service of Requests for Admission",
                    DaysCount = 30,
                    DayType = "Calendar",
                    Direction = "After",
                    Description = "Default response deadline for requests for admission."
                },

                // Georgia
                new CourtRule
                {
                    Name = "Answer to Complaint",
                    RuleType = "State",
                    Jurisdiction = "US-GA",
                    CourtType = "Superior",
                    Citation = "O.C.G.A. Sec. 9-11-12(a)",
                    TriggerEvent = "Service of Summons and Complaint",
                    DaysCount = 30,
                    DayType = "Calendar",
                    Direction = "After",
                    Description = "Baseline Georgia answer deadline."
                },
                new CourtRule
                {
                    Name = "Discovery Response",
                    RuleType = "State",
                    Jurisdiction = "US-GA",
                    CourtType = "Superior",
                    Citation = "O.C.G.A. Sec. 9-11-33(a)(2)",
                    TriggerEvent = "Service of Interrogatories",
                    DaysCount = 30,
                    DayType = "Calendar",
                    Direction = "After",
                    Description = "Default discovery response period."
                },

                // Ohio
                new CourtRule
                {
                    Name = "Answer to Complaint",
                    RuleType = "State",
                    Jurisdiction = "US-OH",
                    CourtType = "Court of Common Pleas",
                    Citation = "Ohio Civ. R. 12(A)(1)",
                    TriggerEvent = "Service of Summons and Complaint",
                    DaysCount = 28,
                    DayType = "Calendar",
                    Direction = "After",
                    Description = "Baseline Ohio answer deadline."
                },
                new CourtRule
                {
                    Name = "Response to Request for Production",
                    RuleType = "State",
                    Jurisdiction = "US-OH",
                    CourtType = "Court of Common Pleas",
                    Citation = "Ohio Civ. R. 34(B)(2)",
                    TriggerEvent = "Service of Request for Production",
                    DaysCount = 28,
                    DayType = "Calendar",
                    Direction = "After",
                    Description = "Default production response period."
                }
            };
        }
    }

    public abstract class CourtRuleWriteDto
    {
        [Required]
        public string Name { get; set; } = string.Empty;

        [Required]
        public string RuleType { get; set; } = "State";

        [Required]
        public string Jurisdiction { get; set; } = string.Empty;

        public string? CourtType { get; set; }

        public string? Citation { get; set; }

        [Required]
        public string TriggerEvent { get; set; } = string.Empty;

        public int DaysCount { get; set; }

        [Required]
        public string DayType { get; set; } = "Calendar";

        [Required]
        public string Direction { get; set; } = "After";

        public int ServiceDaysAdd { get; set; }

        public string? Description { get; set; }

        public bool ExtendIfWeekend { get; set; } = true;

        public bool IsActive { get; set; } = true;
    }

    public sealed class CourtRuleCreateDto : CourtRuleWriteDto
    {
    }

    public sealed class CourtRuleUpdateDto : CourtRuleWriteDto
    {
    }

    public class CourtRuleListDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string RuleType { get; set; } = string.Empty;
        public string Jurisdiction { get; set; } = string.Empty;
        public string? CourtType { get; set; }
        public string? Citation { get; set; }
        public string TriggerEvent { get; set; } = string.Empty;
        public int DaysCount { get; set; }
        public string DayType { get; set; } = string.Empty;
        public string Direction { get; set; } = string.Empty;
        public int ServiceDaysAdd { get; set; }
        public bool ExtendIfWeekend { get; set; }
        public bool IsActive { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public sealed class CourtRuleDetailDto : CourtRuleListDto
    {
        public string? Description { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
