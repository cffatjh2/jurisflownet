using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using JurisFlow.Server.Data;
using JurisFlow.Server.DTOs.Admin;
using JurisFlow.Server.Models;
using System.Security.Claims;
using System.Globalization;
using System.Data;
using JurisFlow.Server.Services;

namespace JurisFlow.Server.Controllers
{
    [Route("api/admin")]
    [ApiController]
    [Authorize(Roles = "Admin,SecurityAdmin")]
    public class AdminController : ControllerBase
    {
        private readonly JurisFlowDbContext _context;
        private readonly AuditLogger _auditLogger;
        private readonly ILogger<AdminController> _logger;
        private readonly PasswordPolicyService _passwordPolicy;
        private readonly AuditLogIntegrityService _auditLogIntegrityService;
        private readonly IConfiguration _configuration;
        private readonly TenantContext _tenantContext;

        private static readonly HashSet<string> AllowedUserRoles = new(StringComparer.Ordinal)
        {
            "Admin",
            "SecurityAdmin",
            "Partner",
            "Associate",
            "Employee",
            "Attorney",
            "Staff",
            "Manager"
        };

        private static readonly HashSet<string> AllowedClientStatuses = new(StringComparer.Ordinal)
        {
            "Active",
            "Inactive"
        };

        private static readonly HashSet<string> AllowedClientTypes = new(StringComparer.Ordinal)
        {
            "Individual",
            "Corporate"
        };

        public AdminController(
            JurisFlowDbContext context,
            AuditLogger auditLogger,
            ILogger<AdminController> logger,
            PasswordPolicyService passwordPolicy,
            AuditLogIntegrityService auditLogIntegrityService,
            IConfiguration configuration,
            TenantContext tenantContext)
        {
            _context = context;
            _auditLogger = auditLogger;
            _logger = logger;
            _passwordPolicy = passwordPolicy;
            _auditLogIntegrityService = auditLogIntegrityService;
            _configuration = configuration;
            _tenantContext = tenantContext;
        }

        // GET: api/admin/users
        [Authorize(Roles = "Admin")]
        [HttpGet("users")]
        public async Task<IActionResult> GetUsers()
        {
            var users = await TenantScope(_context.Users)
                .Select(u => new
                {
                    u.Id,
                    u.Email,
                    u.Name,
                    u.Role
                })
                .ToListAsync();

            return Ok(users);
        }

        // POST: api/admin/users
        [Authorize(Roles = "Admin")]
        [HttpPost("users")]
        public async Task<IActionResult> CreateUser([FromBody] CreateUserDto dto)
        {
            if (dto == null)
            {
                return BadRequest(new { message = "Request body is required." });
            }

            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var normalizedEmail = NormalizeEmail(dto.Email);
            if (string.IsNullOrWhiteSpace(normalizedEmail))
            {
                return BadRequest(new { message = "Email is required." });
            }

            if (await TenantScope(_context.Users).AnyAsync(u => u.NormalizedEmail == normalizedEmail))
            {
                return BadRequest(new { message = "Email already exists" });
            }

            if (!TryNormalizeRole(dto.Role, out var normalizedRole, out var roleError))
            {
                return BadRequest(new { message = roleError });
            }

            if (string.IsNullOrWhiteSpace(dto.Password))
            {
                return BadRequest(new { message = "Password is required." });
            }

            var trimmedEmail = dto.Email.Trim();
            var passwordResult = _passwordPolicy.Validate(dto.Password, trimmedEmail, dto.Name);
            if (!passwordResult.IsValid)
            {
                return BadRequest(new { message = passwordResult.Message });
            }

            var trimmedName = string.IsNullOrWhiteSpace(dto.Name) ? dto.Name : dto.Name.Trim();

            var user = new User
            {
                Id = Guid.NewGuid().ToString(),
                Email = trimmedEmail,
                NormalizedEmail = normalizedEmail,
                Name = trimmedName,
                Role = normalizedRole!,
                PasswordHash = PasswordHashingHelper.HashPassword(dto.Password, _configuration),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            await _auditLogger.LogAsync(HttpContext, "admin.user.create", "User", user.Id, $"Created user {user.Email}");

            return Ok(new
            {
                user.Id,
                user.Email,
                user.Name,
                user.Role
            });
        }

        // PUT: api/admin/users/{id}
        [Authorize(Roles = "Admin")]
        [HttpPut("users/{id}")]
        public async Task<IActionResult> UpdateUser(string id, [FromBody] UpdateUserDto dto)
        {
            if (dto == null)
            {
                return BadRequest(new { message = "Request body is required." });
            }

            var user = await TenantScope(_context.Users).FirstOrDefaultAsync(u => u.Id == id);
            if (user == null)
            {
                return NotFound(new { message = "User not found" });
            }

            string? candidateEmail = null;
            if (!string.IsNullOrWhiteSpace(dto.Name))
                user.Name = dto.Name.Trim();
            if (!string.IsNullOrEmpty(dto.Email))
            {
                candidateEmail = dto.Email.Trim();
                var normalizedEmail = NormalizeEmail(candidateEmail);
                if (string.IsNullOrWhiteSpace(normalizedEmail))
                {
                    return BadRequest(new { message = "Email is required." });
                }

                var emailInUse = await TenantScope(_context.Users).AnyAsync(u => u.NormalizedEmail == normalizedEmail && u.Id != id);
                if (emailInUse)
                {
                    return BadRequest(new { message = "Email already exists" });
                }

                user.Email = candidateEmail;
                user.NormalizedEmail = normalizedEmail;
            }
            if (!string.IsNullOrEmpty(dto.Role))
            {
                if (!TryNormalizeRole(dto.Role, out var normalizedRole, out var roleError))
                {
                    return BadRequest(new { message = roleError });
                }
                user.Role = normalizedRole!;
            }
            if (!string.IsNullOrEmpty(dto.Password))
            {
                var effectiveEmail = candidateEmail ?? user.Email;
                var effectiveName = !string.IsNullOrWhiteSpace(dto.Name) ? dto.Name.Trim() : user.Name;
                var passwordResult = _passwordPolicy.Validate(dto.Password, effectiveEmail, effectiveName);
                if (!passwordResult.IsValid)
                {
                    return BadRequest(new { message = passwordResult.Message });
                }
                user.PasswordHash = PasswordHashingHelper.HashPassword(dto.Password, _configuration);
            }

            user.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            await _auditLogger.LogAsync(HttpContext, "admin.user.update", "User", user.Id, $"Updated user {user.Email}");

            return Ok(new
            {
                user.Id,
                user.Email,
                user.Name,
                user.Role
            });
        }

        // DELETE: api/admin/users/{id}
        [Authorize(Roles = "Admin")]
        [HttpDelete("users/{id}")]
        public async Task<IActionResult> DeleteUser(string id)
        {
            var user = await TenantScope(_context.Users).FirstOrDefaultAsync(u => u.Id == id);
            if (user == null)
            {
                return NotFound(new { message = "User not found" });
            }

            // Prevent deleting self
            var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (user.Id == currentUserId)
            {
                return BadRequest(new { message = "Cannot delete your own account" });
            }

            _context.Users.Remove(user);
            await _context.SaveChangesAsync();

            await _auditLogger.LogAsync(HttpContext, "admin.user.delete", "User", id, $"Deleted user {user.Email}");

            return NoContent();
        }

        // GET: api/admin/clients
        [Authorize(Roles = "Admin")]
        [HttpGet("clients")]
        public async Task<IActionResult> GetClients()
        {
            var query = TenantScope(_context.Clients).AsNoTracking().AsQueryable();
            if (ShouldHideSeedClient())
            {
                var demoEmail = NormalizeEmail(GetSeedClientEmail());
                query = query.Where(c => c.NormalizedEmail != demoEmail);
            }

            var clients = await query
                .OrderByDescending(c => c.CreatedAt)
                .Select(c => new AdminClientListItemDto
                {
                    Id = c.Id,
                    Name = c.Name,
                    Email = c.Email,
                    Company = c.Company,
                    Type = c.Type,
                    Status = c.Status,
                    PortalEnabled = c.PortalEnabled,
                    CreatedAt = c.CreatedAt,
                    UpdatedAt = c.UpdatedAt
                })
                .ToListAsync();

            return Ok(clients);
        }

        // PUT: api/admin/clients/{id}
        [Authorize(Roles = "Admin")]
        [HttpPut("clients/{id}")]
        public async Task<IActionResult> UpdateClient(string id, [FromBody] UpdateClientDto dto)
        {
            if (dto == null)
            {
                return BadRequest(new { message = "Request body is required." });
            }

            var client = await TenantScope(_context.Clients).FirstOrDefaultAsync(c => c.Id == id);
            if (client == null)
            {
                return NotFound(new { message = "Client not found" });
            }
            if (IsSeedClientHidden(client))
            {
                return NotFound(new { message = "Client not found" });
            }

            var previousStatus = client.Status;
            string? candidateEmail = null;

            if (!string.IsNullOrWhiteSpace(dto.Name))
                client.Name = dto.Name.Trim();
            if (!string.IsNullOrEmpty(dto.Email))
            {
                candidateEmail = dto.Email.Trim();
                var normalizedEmail = NormalizeEmail(candidateEmail);
                if (string.IsNullOrWhiteSpace(normalizedEmail))
                {
                    return BadRequest(new { message = "Email is required." });
                }

                var duplicateEmail = await TenantScope(_context.Clients)
                    .AnyAsync(c => c.NormalizedEmail == normalizedEmail && c.Id != id);
                if (duplicateEmail)
                {
                    return BadRequest(new { message = "Email already exists" });
                }

                client.Email = candidateEmail;
                client.NormalizedEmail = normalizedEmail;
            }
            if (dto.Phone != null)
                client.Phone = string.IsNullOrWhiteSpace(dto.Phone) ? null : dto.Phone.Trim();
            if (dto.PortalEnabled.HasValue)
                client.PortalEnabled = dto.PortalEnabled.Value;
            if (!string.IsNullOrWhiteSpace(dto.Status))
            {
                if (!TryNormalizeClientStatus(dto.Status, out var normalizedStatus, out var statusError))
                {
                    return BadRequest(new { message = statusError });
                }
                client.Status = normalizedStatus!;
            }
            if (!string.IsNullOrWhiteSpace(dto.Company))
                client.Company = dto.Company.Trim();
            if (!string.IsNullOrWhiteSpace(dto.Type))
            {
                if (!TryNormalizeClientType(dto.Type, out var normalizedType, out var typeError))
                {
                    return BadRequest(new { message = typeError });
                }
                client.Type = normalizedType!;
            }
            if (dto.Mobile != null)
                client.Mobile = string.IsNullOrWhiteSpace(dto.Mobile) ? null : dto.Mobile.Trim();
            if (!string.IsNullOrWhiteSpace(dto.Password))
            {
                var effectiveEmail = candidateEmail ?? client.Email;
                var effectiveName = !string.IsNullOrWhiteSpace(dto.Name) ? dto.Name.Trim() : client.Name;
                var passwordResult = _passwordPolicy.Validate(dto.Password, effectiveEmail, effectiveName);
                if (!passwordResult.IsValid)
                {
                    return BadRequest(new { message = passwordResult.Message });
                }
                client.PasswordHash = PasswordHashingHelper.HashPassword(dto.Password, _configuration);
            }

            client.Company = await ResolveTenantCompanyNameAsync(client.Company);

            client.UpdatedAt = DateTime.UtcNow;

            if (!string.Equals(previousStatus, client.Status, StringComparison.OrdinalIgnoreCase))
            {
                var changedById = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
                var changedByName = User.FindFirst(ClaimTypes.Email)?.Value ?? User.FindFirst("email")?.Value;
                _context.ClientStatusHistories.Add(new ClientStatusHistory
                {
                    ClientId = client.Id,
                    PreviousStatus = previousStatus ?? "Unknown",
                    NewStatus = client.Status ?? "Unknown",
                    Notes = string.IsNullOrWhiteSpace(dto.StatusChangeNote) ? null : dto.StatusChangeNote,
                    ChangedByUserId = changedById,
                    ChangedByName = changedByName,
                    CreatedAt = DateTime.UtcNow
                });
            }

            await _context.SaveChangesAsync();

            await _auditLogger.LogAsync(HttpContext, "admin.client.update", "Client", client.Id, $"Updated client {client.Email}");

            return Ok(ToAdminClientResponse(client));
        }

        // DELETE: api/admin/clients/{id}
        [Authorize(Roles = "Admin")]
        [HttpDelete("clients/{id}")]
        public async Task<IActionResult> DeleteClient(string id)
        {
            return await ArchiveClientInternalAsync(id, requestedViaDeleteEndpoint: true);
        }

        // POST: api/admin/clients/{id}/archive
        [Authorize(Roles = "Admin")]
        [HttpPost("clients/{id}/archive")]
        public async Task<IActionResult> ArchiveClient(string id)
        {
            return await ArchiveClientInternalAsync(id, requestedViaDeleteEndpoint: false);
        }

        private async Task<IActionResult> ArchiveClientInternalAsync(string id, bool requestedViaDeleteEndpoint)
        {
            var client = await TenantScope(_context.Clients).FirstOrDefaultAsync(c => c.Id == id);
            if (client == null)
            {
                return NotFound(new { message = "Client not found" });
            }
            if (IsSeedClientHidden(client))
            {
                return NotFound(new { message = "Client not found" });
            }

            var previousStatus = client.Status;
            client.Status = "Inactive";
            client.PortalEnabled = false;
            client.UpdatedAt = DateTime.UtcNow;

            if (!string.Equals(previousStatus, client.Status, StringComparison.OrdinalIgnoreCase))
            {
                _context.ClientStatusHistories.Add(new ClientStatusHistory
                {
                    ClientId = client.Id,
                    PreviousStatus = previousStatus ?? "Unknown",
                    NewStatus = client.Status ?? "Inactive",
                    Notes = "Archived via delete endpoint (hard delete disabled).",
                    ChangedByUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value,
                    ChangedByName = User.FindFirst(ClaimTypes.Email)?.Value ?? User.FindFirst("email")?.Value,
                    CreatedAt = DateTime.UtcNow
                });
            }

            await _context.SaveChangesAsync();

            await _auditLogger.LogAsync(HttpContext, "admin.client.archive", "Client", id, $"Archived client {client.Email}; hard delete disabled.");

            return Ok(new
            {
                message = requestedViaDeleteEndpoint
                    ? "Client archived via delete endpoint. Hard delete is disabled."
                    : "Client archived.",
                client = ToAdminClientResponse(client)
            });
        }

        // POST: api/admin/billing-locks
        [Authorize(Roles = "Admin")]
        [HttpPost("billing-locks")]
        public async Task<IActionResult> CreateBillingLock([FromBody] CreateBillingLockDto dto)
        {
            if (dto == null)
            {
                return BadRequest(new { message = "Request body is required." });
            }

            if (string.IsNullOrWhiteSpace(dto.PeriodStart) || string.IsNullOrWhiteSpace(dto.PeriodEnd))
            {
                return BadRequest(new { message = "PeriodStart and PeriodEnd are required (yyyy-MM-dd)." });
            }

            if (!TryParseDateOnly(dto.PeriodStart, out var periodStart) ||
                !TryParseDateOnly(dto.PeriodEnd, out var periodEnd))
            {
                return BadRequest(new { message = "PeriodStart and PeriodEnd must be valid dates in yyyy-MM-dd format." });
            }

            if (periodEnd < periodStart)
            {
                return BadRequest(new { message = "PeriodEnd must be greater than or equal to PeriodStart." });
            }

            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var periodStartKey = periodStart.ToString("yyyy-MM-dd");
            var periodEndKey = periodEnd.ToString("yyyy-MM-dd");

            await using var tx = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var lockExists = await TenantScope(_context.BillingLocks)
                .AsNoTracking()
                .AnyAsync(b =>
                    string.Compare(b.PeriodStart, periodEndKey) <= 0 &&
                    string.Compare(b.PeriodEnd, periodStartKey) >= 0);
            if (lockExists)
            {
                return BadRequest(new { message = "An overlapping billing lock already exists." });
            }

            var record = new BillingLock
            {
                PeriodStart = periodStartKey,
                PeriodEnd = periodEndKey,
                LockedByUserId = userId,
                LockedAt = DateTime.UtcNow,
                Notes = dto.Notes
            };

            _context.BillingLocks.Add(record);
            await _context.SaveChangesAsync();
            await tx.CommitAsync();
            await _auditLogger.LogAsync(HttpContext, "billing.lock.create", "BillingLock", record.Id, $"Period {record.PeriodStart} to {record.PeriodEnd}");

            return Ok(record);
        }

        // GET: api/admin/billing-locks
        [Authorize(Roles = "Admin")]
        [HttpGet("billing-locks")]
        public async Task<IActionResult> GetBillingLocks()
        {
            var locks = await TenantScope(_context.BillingLocks)
                .AsNoTracking()
                .OrderByDescending(b => b.LockedAt)
                .ToListAsync();
            return Ok(locks);
        }

        // GET: api/admin/audit-logs
        [Authorize(Roles = "Admin")]
        [HttpGet("audit-logs")]
        public async Task<IActionResult> GetAuditLogs(
            [FromQuery] int page = 1,
            [FromQuery] int limit = 50,
            [FromQuery] string? action = null,
            [FromQuery] string? entity = null,
            [FromQuery] string? userId = null,
            [FromQuery] string? clientId = null,
            [FromQuery] string? from = null,
            [FromQuery] string? to = null)
        {
            (int normalizedPage, int normalizedLimit, IQueryable<AuditLog> query) result;
            try
            {
                result = BuildAuditLogQuery(page, limit, action, entity, userId, clientId, from, to);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            var (normalizedPage, normalizedLimit, query) = result;

            var total = await query.CountAsync();
            var logs = await query
                .OrderByDescending(a => a.CreatedAt)
                .Skip((normalizedPage - 1) * normalizedLimit)
                .Take(normalizedLimit)
                .Select(a => new
                {
                    a.Id,
                    a.Action,
                    a.Entity,
                    a.EntityId,
                    a.UserId,
                    a.ClientId,
                    a.Role,
                    a.IpAddress,
                    a.UserAgent,
                    a.Details,
                    a.CreatedAt
                })
                .ToListAsync();

            return Ok(new
            {
                logs,
                total,
                page = normalizedPage,
                limit = normalizedLimit
            });
        }

        [Authorize(Policy = "SecurityAdminOnly")]
        [HttpGet("audit-logs/security")]
        public async Task<IActionResult> GetAuditLogsSecurity(
            [FromQuery] int page = 1,
            [FromQuery] int limit = 50,
            [FromQuery] string? action = null,
            [FromQuery] string? entity = null,
            [FromQuery] string? userId = null,
            [FromQuery] string? clientId = null,
            [FromQuery] string? from = null,
            [FromQuery] string? to = null)
        {
            (int normalizedPage, int normalizedLimit, IQueryable<AuditLog> query) result;
            try
            {
                result = BuildAuditLogQuery(page, limit, action, entity, userId, clientId, from, to);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            var (normalizedPage, normalizedLimit, query) = result;

            var total = await query.CountAsync();
            var logs = await query
                .OrderByDescending(a => a.CreatedAt)
                .Skip((normalizedPage - 1) * normalizedLimit)
                .Take(normalizedLimit)
                .Select(a => new
                {
                    a.Id,
                    a.Action,
                    a.Entity,
                    a.EntityId,
                    a.UserId,
                    a.ClientId,
                    a.Role,
                    a.IpAddress,
                    a.UserAgent,
                    a.Details,
                    a.Sequence,
                    a.PreviousHash,
                    a.Hash,
                    a.HashAlgorithm,
                    a.CreatedAt
                })
                .ToListAsync();

            return Ok(new
            {
                logs,
                total,
                page = normalizedPage,
                limit = normalizedLimit
            });
        }

        [Authorize(Policy = "SecurityAdminOnly")]
        [HttpGet("audit-logs/integrity")]
        public async Task<IActionResult> VerifyAuditLogIntegrity([FromQuery] int? limit = null)
        {
            var result = await _auditLogIntegrityService.VerifyAsync(limit);
            return Ok(result);
        }

        private (int page, int limit, IQueryable<AuditLog> query) BuildAuditLogQuery(
            int page,
            int limit,
            string? action,
            string? entity,
            string? userId,
            string? clientId,
            string? from,
            string? to)
        {
            var normalizedPage = Math.Max(1, page);
            var normalizedLimit = Math.Clamp(limit, 1, GetMaxAuditLogPageSize());

            var query = TenantScope(_context.AuditLogs).AsNoTracking().AsQueryable();

            var (normalizedAction, actionIsPrefix) = NormalizeAuditFilter(action, nameof(action));
            if (normalizedAction != null)
            {
                query = actionIsPrefix
                    ? query.Where(a => a.Action.StartsWith(normalizedAction))
                    : query.Where(a => a.Action == normalizedAction);
            }

            var (normalizedEntity, entityIsPrefix) = NormalizeAuditFilter(entity, nameof(entity));
            if (normalizedEntity != null)
            {
                query = entityIsPrefix
                    ? query.Where(a => a.Entity.StartsWith(normalizedEntity))
                    : query.Where(a => a.Entity == normalizedEntity);
            }

            if (!string.IsNullOrWhiteSpace(userId))
            {
                query = query.Where(a => a.UserId == userId);
            }

            if (!string.IsNullOrWhiteSpace(clientId))
            {
                query = query.Where(a => a.ClientId == clientId);
            }

            if (TryParseUtcDateTime(from, out var fromDate))
            {
                query = query.Where(a => a.CreatedAt >= fromDate);
            }

            if (TryParseUtcDateTime(to, out var toDate))
            {
                query = query.Where(a => a.CreatedAt <= toDate);
            }

            return (normalizedPage, normalizedLimit, query);
        }

        private IQueryable<T> TenantScope<T>(IQueryable<T> query) where T : class
        {
            var tenantId = RequireTenantId();
            var entityType = _context.Model.FindEntityType(typeof(T));
            if (entityType?.FindProperty("TenantId") == null)
            {
                throw new InvalidOperationException($"TenantScope cannot be applied to entity '{typeof(T).Name}' because TenantId is not configured.");
            }
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

        private async Task<string?> ResolveTenantCompanyNameAsync(string? fallbackCompany)
        {
            var tenantId = RequireTenantId();
            var tenantName = await _context.Tenants
                .AsNoTracking()
                .Where(t => t.Id == tenantId)
                .Select(t => t.Name)
                .FirstOrDefaultAsync();

            if (!string.IsNullOrWhiteSpace(tenantName))
            {
                return tenantName.Trim();
            }

            return string.IsNullOrWhiteSpace(fallbackCompany)
                ? null
                : fallbackCompany.Trim();
        }

        private static string NormalizeEmail(string? email)
        {
            return EmailAddressNormalizer.Normalize(email);
        }

        private static bool TryNormalizeRole(string? role, out string? normalizedRole, out string? error)
        {
            var candidate = string.IsNullOrWhiteSpace(role) ? "Associate" : role.Trim();

            normalizedRole = AllowedUserRoles.FirstOrDefault(r => string.Equals(r, candidate, StringComparison.OrdinalIgnoreCase));
            if (normalizedRole != null)
            {
                error = null;
                return true;
            }

            error = $"Unsupported role '{candidate}'.";
            return false;
        }

        private static bool TryNormalizeClientStatus(string? status, out string? normalizedStatus, out string? error)
        {
            var candidate = string.IsNullOrWhiteSpace(status) ? "Active" : status.Trim();
            normalizedStatus = AllowedClientStatuses.FirstOrDefault(s => string.Equals(s, candidate, StringComparison.OrdinalIgnoreCase));
            if (normalizedStatus != null)
            {
                error = null;
                return true;
            }

            error = $"Unsupported client status '{candidate}'.";
            return false;
        }

        private static bool TryNormalizeClientType(string? type, out string? normalizedType, out string? error)
        {
            var candidate = string.IsNullOrWhiteSpace(type) ? "Individual" : type.Trim();
            normalizedType = AllowedClientTypes.FirstOrDefault(s => string.Equals(s, candidate, StringComparison.OrdinalIgnoreCase));
            if (normalizedType != null)
            {
                error = null;
                return true;
            }

            error = $"Unsupported client type '{candidate}'.";
            return false;
        }

        private static AdminClientResponseDto ToAdminClientResponse(Client client)
        {
            return new AdminClientResponseDto
            {
                Id = client.Id,
                Name = client.Name,
                Email = client.Email,
                Phone = client.Phone,
                Mobile = client.Mobile,
                Company = client.Company,
                Type = client.Type,
                Status = client.Status,
                PortalEnabled = client.PortalEnabled,
                UpdatedAt = client.UpdatedAt
            };
        }

        private static bool TryParseDateOnly(string? input, out DateOnly date)
        {
            date = default;
            if (string.IsNullOrWhiteSpace(input))
            {
                return false;
            }

            return DateOnly.TryParseExact(input, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
        }

        private static bool TryParseUtcDateTime(string? input, out DateTime utcDateTime)
        {
            utcDateTime = default;
            if (string.IsNullOrWhiteSpace(input))
            {
                return false;
            }

            if (!DateTimeOffset.TryParse(
                    input,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var parsed))
            {
                return false;
            }

            utcDateTime = parsed.UtcDateTime;
            return true;
        }

        private bool ShouldHideSeedClient()
        {
            // Preferred explicit flag (no inverse semantics). Keep legacy fallback for compatibility.
            var explicitHide = _configuration["Seed:HideClient"];
            if (!string.IsNullOrWhiteSpace(explicitHide) && bool.TryParse(explicitHide, out var hideSeedClient))
            {
                return hideSeedClient;
            }

            return !_configuration.GetValue("Seed:PortalClientEnabled", false);
        }

        private string GetSeedClientEmail()
        {
            return _configuration["Seed:PortalClientEmail"] ?? "client.demo@jurisflow.local";
        }

        private bool IsSeedClientHidden(Client client)
        {
            if (!ShouldHideSeedClient()) return false;
            return string.Equals(client.NormalizedEmail, NormalizeEmail(GetSeedClientEmail()), StringComparison.Ordinal);
        }

        private int GetMaxAuditLogPageSize()
        {
            var configured = _configuration.GetValue<int?>("Admin:MaxAuditLogPageSize") ?? 200;
            return Math.Clamp(configured, 50, 1000);
        }

        private static (string? value, bool isPrefix) NormalizeAuditFilter(string? raw, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return (null, false);
            }

            var trimmed = raw.Trim();
            var isPrefix = trimmed.EndsWith('*');
            var value = isPrefix ? trimmed[..^1].Trim() : trimmed;
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException($"Invalid {parameterName} filter.");
            }

            if (value.Length > 128)
            {
                throw new ArgumentException($"{parameterName} filter is too long.");
            }

            foreach (var ch in value)
            {
                var isAllowed =
                    char.IsLetterOrDigit(ch) ||
                    ch is '.' or '_' or '-' or ':' or '/';
                if (!isAllowed)
                {
                    throw new ArgumentException($"{parameterName} filter contains unsupported characters.");
                }
            }

            return (value, isPrefix);
        }
    }
}
