using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Security.Claims;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using JurisFlow.Server.DTOs;
using JurisFlow.Server.Services;

namespace JurisFlow.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "Admin,Partner,Associate,Employee")]
    public class ClientsController : ControllerBase
    {
        private readonly JurisFlowDbContext _context;
        private readonly AuditLogger _auditLogger;
        private readonly PasswordPolicyService _passwordPolicy;
        private readonly IConfiguration _configuration;
        private readonly TenantContext _tenantContext;
        private static readonly HashSet<string> AllowedClientStatuses = new(StringComparer.OrdinalIgnoreCase)
        {
            "Active",
            "Inactive"
        };
        private static readonly HashSet<string> AllowedClientTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "Individual",
            "Corporate"
        };

        public ClientsController(
            JurisFlowDbContext context,
            AuditLogger auditLogger,
            PasswordPolicyService passwordPolicy,
            IConfiguration configuration,
            TenantContext tenantContext)
        {
            _context = context;
            _auditLogger = auditLogger;
            _passwordPolicy = passwordPolicy;
            _configuration = configuration;
            _tenantContext = tenantContext;
        }

        // GET: api/Clients
        [HttpGet]
        public async Task<IActionResult> GetClients()
        {
            IQueryable<Client> query = TenantScope(_context.Clients).AsNoTracking();
            if (ShouldHideSeedClient())
            {
                var demoEmail = NormalizeEmail(GetSeedClientEmail());
                query = query.Where(c => c.NormalizedEmail != demoEmail);
            }

            var clients = await query
                .OrderByDescending(c => c.CreatedAt)
                .ToListAsync();

            return Ok(clients.Select(ToClientListResponse));
        }

        // GET: api/Clients/5
        [HttpGet("{id}")]
        public async Task<IActionResult> GetClient(string id)
        {
            var client = await TenantScope(_context.Clients).FirstOrDefaultAsync(c => c.Id == id);

            if (client == null)
            {
                return NotFound();
            }

            if (IsSeedClientHidden(client))
            {
                return NotFound();
            }

            return Ok(ToClientDetailResponse(client));
        }

        // POST: api/Clients
        [Authorize(Roles = "Admin,Partner")]
        [HttpPost]
        public async Task<IActionResult> PostClient([FromBody] ClientCreateDto dto)
        {
            if (dto == null) return BadRequest(new { message = "Request body is required." });
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var normalizedEmail = NormalizeEmail(dto.Email);
            if (string.IsNullOrWhiteSpace(normalizedEmail))
            {
                return BadRequest(new { message = "Email is required." });
            }

            if (!TryNormalizeClientType(dto.Type, out var normalizedType, out var typeError))
            {
                return BadRequest(new { message = typeError });
            }

            if (!TryNormalizeClientStatus(dto.Status, out var normalizedStatus, out var statusError))
            {
                return BadRequest(new { message = statusError });
            }

            var duplicateEmail = await TenantScope(_context.Clients).AnyAsync(c => c.NormalizedEmail == normalizedEmail);
            if (duplicateEmail)
            {
                return BadRequest(new { message = "Email already exists." });
            }

            var client = new Client
            {
                Id = Guid.NewGuid().ToString(),
                ClientNumber = dto.ClientNumber,
                Name = dto.Name,
                Email = dto.Email.Trim(),
                NormalizedEmail = normalizedEmail,
                Phone = dto.Phone,
                Mobile = dto.Mobile,
                Company = dto.Company,
                Type = normalizedType!,
                Status = normalizedStatus!,
                Address = dto.Address,
                City = dto.City,
                State = dto.State,
                ZipCode = dto.ZipCode,
                Country = dto.Country,
                TaxId = dto.TaxId,
                IncorporationState = dto.IncorporationState,
                RegisteredAgent = dto.RegisteredAgent,
                AuthorizedRepresentatives = dto.AuthorizedRepresentatives,
                Notes = dto.Notes,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            // Password handling for portal access - only if explicitly provided
            if (!string.IsNullOrEmpty(dto.Password))
            {
                var passwordResult = _passwordPolicy.Validate(dto.Password, dto.Email, dto.Name);
                if (!passwordResult.IsValid)
                {
                    return BadRequest(new { message = passwordResult.Message });
                }
                client.PasswordHash = PasswordHashingHelper.HashPassword(dto.Password, _configuration);
                client.PortalEnabled = true;
            }

            _context.Clients.Add(client);
            _context.ClientStatusHistories.Add(new ClientStatusHistory
            {
                ClientId = client.Id,
                PreviousStatus = "New",
                NewStatus = client.Status,
                ChangedByUserId = GetUserId(),
                ChangedByName = GetUserEmail(),
                CreatedAt = DateTime.UtcNow
            });
            try
            {
                await _context.SaveChangesAsync();
            }
            catch(DbUpdateException)
            {
                if (ClientExists(client.Id)) return Conflict();
                else throw;
            }

            await _auditLogger.LogAsync(HttpContext, "client.create", "Client", client.Id, $"Created client {client.Email}");
            return CreatedAtAction("GetClient", new { id = client.Id }, ToClientDetailResponse(client));
        }

        // PUT: api/Clients/5
        [Authorize(Roles = "Admin,Partner")]
        [HttpPut("{id}")]
        public async Task<IActionResult> PutClient(string id, [FromBody] ClientCreateDto dto)
        {
            if (dto == null) return BadRequest(new { message = "Request body is required." });
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var existing = await TenantScope(_context.Clients).FirstOrDefaultAsync(c => c.Id == id);
            if (existing == null) return NotFound();
            if (IsSeedClientHidden(existing)) return NotFound();

            var normalizedEmail = NormalizeEmail(dto.Email);
            if (string.IsNullOrWhiteSpace(normalizedEmail))
            {
                return BadRequest(new { message = "Email is required." });
            }

            if (!TryNormalizeClientType(dto.Type, out var normalizedType, out var typeError))
            {
                return BadRequest(new { message = typeError });
            }

            if (!TryNormalizeClientStatus(dto.Status, out var normalizedStatus, out var statusError))
            {
                return BadRequest(new { message = statusError });
            }

            var duplicateEmail = await TenantScope(_context.Clients).AnyAsync(c => c.NormalizedEmail == normalizedEmail && c.Id != id);
            if (duplicateEmail)
            {
                return BadRequest(new { message = "Email already exists." });
            }

            var previousStatus = existing.Status;

            existing.ClientNumber = dto.ClientNumber;
            existing.Name = dto.Name.Trim();
            existing.Email = dto.Email.Trim();
            existing.NormalizedEmail = normalizedEmail;
            existing.Phone = dto.Phone;
            existing.Mobile = dto.Mobile;
            existing.Company = dto.Company;
            existing.Type = normalizedType!;
            existing.Status = normalizedStatus!;
            existing.Address = dto.Address;
            existing.City = dto.City;
            existing.State = dto.State;
            existing.ZipCode = dto.ZipCode;
            existing.Country = dto.Country;
            existing.TaxId = dto.TaxId;
            existing.IncorporationState = dto.IncorporationState;
            existing.RegisteredAgent = dto.RegisteredAgent;
            existing.AuthorizedRepresentatives = dto.AuthorizedRepresentatives;
            existing.Notes = dto.Notes;
            existing.UpdatedAt = DateTime.UtcNow;

            if (!string.IsNullOrWhiteSpace(dto.Password))
            {
                var passwordResult = _passwordPolicy.Validate(dto.Password, existing.Email, existing.Name);
                if (!passwordResult.IsValid)
                {
                    return BadRequest(new { message = passwordResult.Message });
                }
                existing.PasswordHash = PasswordHashingHelper.HashPassword(dto.Password, _configuration);
                existing.PortalEnabled = true;
            }

            if (!string.Equals(previousStatus, existing.Status, StringComparison.OrdinalIgnoreCase))
            {
                _context.ClientStatusHistories.Add(new ClientStatusHistory
                {
                    ClientId = existing.Id,
                    PreviousStatus = previousStatus ?? "Unknown",
                    NewStatus = existing.Status ?? "Unknown",
                    ChangedByUserId = GetUserId(),
                    ChangedByName = GetUserEmail(),
                    CreatedAt = DateTime.UtcNow
                });
            }

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!ClientExists(id)) return NotFound();
                else throw;
            }

            await _auditLogger.LogAsync(HttpContext, "client.update", "Client", existing.Id, $"Updated client {existing.Email}");
            return Ok(ToClientDetailResponse(existing));
        }

        // PATCH: api/Clients/5
        [Authorize(Roles = "Admin,Partner")]
        [HttpPatch("{id}")]
        public async Task<IActionResult> PatchClient(string id, [FromBody] ClientUpdateDto dto)
        {
            if (dto == null) return BadRequest(new { message = "Request body is required." });
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var client = await TenantScope(_context.Clients).FirstOrDefaultAsync(c => c.Id == id);
            if (client == null) return NotFound();
            if (IsSeedClientHidden(client)) return NotFound();

            var previousStatus = client.Status;

            if (dto.ClientNumber != null) client.ClientNumber = dto.ClientNumber;
            if (!string.IsNullOrWhiteSpace(dto.Name)) client.Name = dto.Name;
            if (!string.IsNullOrWhiteSpace(dto.Email))
            {
                var normalizedEmail = NormalizeEmail(dto.Email);
                if (string.IsNullOrWhiteSpace(normalizedEmail))
                {
                    return BadRequest(new { message = "Email is required." });
                }

                var duplicateEmail = await TenantScope(_context.Clients).AnyAsync(c => c.NormalizedEmail == normalizedEmail && c.Id != id);
                if (duplicateEmail)
                {
                    return BadRequest(new { message = "Email already exists." });
                }

                client.Email = dto.Email.Trim();
                client.NormalizedEmail = normalizedEmail;
            }
            if (dto.Phone != null) client.Phone = dto.Phone;
            if (dto.Mobile != null) client.Mobile = dto.Mobile;
            if (dto.Company != null) client.Company = dto.Company;
            if (!string.IsNullOrWhiteSpace(dto.Type))
            {
                if (!TryNormalizeClientType(dto.Type, out var normalizedType, out var typeError))
                {
                    return BadRequest(new { message = typeError });
                }
                client.Type = normalizedType!;
            }
            if (!string.IsNullOrWhiteSpace(dto.Status))
            {
                if (!TryNormalizeClientStatus(dto.Status, out var normalizedStatus, out var statusError))
                {
                    return BadRequest(new { message = statusError });
                }
                client.Status = normalizedStatus!;
            }
            if (dto.Address != null) client.Address = dto.Address;
            if (dto.City != null) client.City = dto.City;
            if (dto.State != null) client.State = dto.State;
            if (dto.ZipCode != null) client.ZipCode = dto.ZipCode;
            if (dto.Country != null) client.Country = dto.Country;
            if (dto.TaxId != null) client.TaxId = dto.TaxId;
            if (dto.IncorporationState != null) client.IncorporationState = dto.IncorporationState;
            if (dto.RegisteredAgent != null) client.RegisteredAgent = dto.RegisteredAgent;
            if (dto.AuthorizedRepresentatives != null) client.AuthorizedRepresentatives = dto.AuthorizedRepresentatives;
            if (dto.Notes != null) client.Notes = dto.Notes;

            if (!string.IsNullOrWhiteSpace(dto.Password))
            {
                var passwordResult = _passwordPolicy.Validate(dto.Password, dto.Email ?? client.Email, dto.Name ?? client.Name);
                if (!passwordResult.IsValid)
                {
                    return BadRequest(new { message = passwordResult.Message });
                }
                client.PasswordHash = PasswordHashingHelper.HashPassword(dto.Password, _configuration);
            }
            if (dto.PortalEnabled.HasValue)
            {
                if (dto.PortalEnabled.Value && string.IsNullOrWhiteSpace(dto.Password) && string.IsNullOrWhiteSpace(client.PasswordHash))
                {
                    return BadRequest(new { message = "Password is required before enabling portal access." });
                }
                client.PortalEnabled = dto.PortalEnabled.Value;
            }

            client.UpdatedAt = DateTime.UtcNow;

            if (!string.Equals(previousStatus, client.Status, StringComparison.OrdinalIgnoreCase))
            {
                _context.ClientStatusHistories.Add(new ClientStatusHistory
                {
                    ClientId = client.Id,
                    PreviousStatus = previousStatus ?? "Unknown",
                    NewStatus = client.Status ?? "Unknown",
                    Notes = string.IsNullOrWhiteSpace(dto.StatusChangeNote) ? null : dto.StatusChangeNote,
                    ChangedByUserId = GetUserId(),
                    ChangedByName = GetUserEmail(),
                    CreatedAt = DateTime.UtcNow
                });
            }

            await _context.SaveChangesAsync();

            await _auditLogger.LogAsync(HttpContext, "client.update", "Client", client.Id, $"Updated client {client.Email}");
            return Ok(ToClientDetailResponse(client));
        }

        [HttpGet("{id}/status-history")]
        public async Task<IActionResult> GetStatusHistory(string id)
        {
            IQueryable<Client> query = TenantScope(_context.Clients).AsNoTracking();
            if (ShouldHideSeedClient())
            {
                var demoEmail = NormalizeEmail(GetSeedClientEmail());
                query = query.Where(c => c.NormalizedEmail != demoEmail);
            }

            var exists = await query.AnyAsync(c => c.Id == id);
            if (!exists) return NotFound();

            var history = await TenantScope(_context.ClientStatusHistories)
                .AsNoTracking()
                .Where(h => h.ClientId == id)
                .OrderByDescending(h => h.CreatedAt)
                .Select(h => new
                {
                    h.Id,
                    h.ClientId,
                    h.PreviousStatus,
                    h.NewStatus,
                    h.Notes,
                    h.ChangedByUserId,
                    h.ChangedByName,
                    h.CreatedAt
                })
                .ToListAsync();

            return Ok(history);
        }

        // DELETE: api/Clients/5
        [Authorize(Roles = "Admin,Partner")]
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteClient(string id)
        {
            var client = await TenantScope(_context.Clients).FirstOrDefaultAsync(c => c.Id == id);
            if (client == null) return NotFound();
            if (IsSeedClientHidden(client)) return NotFound();

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
                    ChangedByUserId = GetUserId(),
                    ChangedByName = GetUserEmail(),
                    CreatedAt = DateTime.UtcNow
                });
            }

            await _context.SaveChangesAsync();

            await _auditLogger.LogAsync(HttpContext, "client.archive", "Client", id, $"Archived client {client.Email}; hard delete disabled.");
            return Ok(new { message = "Client archived. Hard delete is disabled." });
        }

        private bool ClientExists(string id)
        {
            return TenantScope(_context.Clients).Any(e => e.Id == id);
        }

        private string? GetUserId()
        {
            return User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
        }

        private string? GetUserEmail()
        {
            return User.FindFirst(ClaimTypes.Email)?.Value ?? User.FindFirst("email")?.Value;
        }

        // POST: api/Clients/{id}/set-password
        public class SetPasswordDto
        {
            public string Password { get; set; } = string.Empty;
        }

        [Authorize(Roles = "Admin,Partner")]
        [HttpPost("{id}/set-password")]
        public async Task<IActionResult> SetClientPassword(string id, [FromBody] SetPasswordDto dto)
        {
            if (dto == null)
            {
                return BadRequest(new { message = "Request body is required" });
            }

            var client = await TenantScope(_context.Clients).FirstOrDefaultAsync(c => c.Id == id);
            if (client == null) return NotFound();
            if (IsSeedClientHidden(client)) return NotFound();

            if (string.IsNullOrEmpty(dto.Password))
            {
                return BadRequest(new { message = "Password is required" });
            }

            var passwordResult = _passwordPolicy.Validate(dto.Password, client.Email, client.Name);
            if (!passwordResult.IsValid)
            {
                return BadRequest(new { message = passwordResult.Message });
            }

            client.PasswordHash = PasswordHashingHelper.HashPassword(dto.Password, _configuration);
            client.PortalEnabled = true;
            client.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await _auditLogger.LogAsync(HttpContext, "client.portal.password_set", "Client", client.Id, $"Portal password set for client {client.Email}");

            return Ok(new { message = "Password set successfully", portalEnabled = true });
        }

        private bool ShouldHideSeedClient()
        {
            var explicitHide = _configuration.GetValue<bool?>("Seed:HidePortalClient");
            if (explicitHide.HasValue)
            {
                return explicitHide.Value;
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

        private static string NormalizeEmail(string? email)
        {
            return EmailAddressNormalizer.Normalize(email);
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

        private static bool TryNormalizeClientStatus(string? status, out string? normalizedStatus, out string? error)
        {
            var candidate = string.IsNullOrWhiteSpace(status) ? null : status.Trim();
            normalizedStatus = candidate == null
                ? null
                : AllowedClientStatuses.FirstOrDefault(s => string.Equals(s, candidate, StringComparison.OrdinalIgnoreCase));
            if (normalizedStatus != null)
            {
                error = null;
                return true;
            }

            error = "Invalid client status.";
            return false;
        }

        private static bool TryNormalizeClientType(string? type, out string? normalizedType, out string? error)
        {
            var candidate = string.IsNullOrWhiteSpace(type) ? null : type.Trim();
            normalizedType = candidate == null
                ? null
                : AllowedClientTypes.FirstOrDefault(t => string.Equals(t, candidate, StringComparison.OrdinalIgnoreCase));
            if (normalizedType != null)
            {
                error = null;
                return true;
            }

            error = "Invalid client type.";
            return false;
        }

        private static object ToClientListResponse(Client client)
        {
            return new
            {
                client.Id,
                client.ClientNumber,
                client.Name,
                client.Email,
                client.Phone,
                client.Mobile,
                client.Company,
                client.Type,
                client.Status,
                client.PortalEnabled,
                client.CreatedAt,
                client.UpdatedAt
            };
        }

        private static object ToClientDetailResponse(Client client)
        {
            return new
            {
                client.Id,
                client.ClientNumber,
                client.Name,
                client.Email,
                client.Phone,
                client.Mobile,
                client.Company,
                client.Type,
                client.Status,
                client.Address,
                client.City,
                client.State,
                client.ZipCode,
                client.Country,
                client.IncorporationState,
                client.PortalEnabled,
                client.CreatedAt,
                client.UpdatedAt
            };
        }
    }
}
