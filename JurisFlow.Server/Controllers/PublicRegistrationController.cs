using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using JurisFlow.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JurisFlow.Server.Controllers
{
    [Route("api/public")]
    [ApiController]
    public sealed class PublicRegistrationController : ControllerBase
    {
        private readonly JurisFlowDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly TenantContext _tenantContext;
        private readonly ILogger<PublicRegistrationController> _logger;

        public PublicRegistrationController(
            JurisFlowDbContext context,
            IConfiguration configuration,
            TenantContext tenantContext,
            ILogger<PublicRegistrationController> logger)
        {
            _context = context;
            _configuration = configuration;
            _tenantContext = tenantContext;
            _logger = logger;
        }

        [AllowAnonymous]
        [HttpPost("register-attorney")]
        public async Task<IActionResult> RegisterAttorney([FromBody] PublicAttorneyRegistrationRequest? request, CancellationToken cancellationToken)
        {
            if (request == null)
            {
                return BadRequest(new { message = "Request body is required." });
            }

            var firmName = NormalizeRequired(request.FirmName);
            var fullName = NormalizeRequired(request.FullName);
            var email = NormalizeRequired(request.Email);
            var password = request.Password?.Trim() ?? string.Empty;
            var firmCode = NormalizeSlug(request.FirmCode);

            if (string.IsNullOrWhiteSpace(firmName) ||
                string.IsNullOrWhiteSpace(fullName) ||
                string.IsNullOrWhiteSpace(email) ||
                string.IsNullOrWhiteSpace(firmCode))
            {
                return BadRequest(new { message = "Firm name, firm code, full name, and email are required." });
            }

            if (password.Length < 10)
            {
                return BadRequest(new { message = "Password must be at least 10 characters." });
            }

            var normalizedEmail = EmailAddressNormalizer.Normalize(email);
            if (string.IsNullOrWhiteSpace(normalizedEmail))
            {
                return BadRequest(new { message = "Email is invalid." });
            }

            var tenantExists = await _context.Tenants
                .AsNoTracking()
                .AnyAsync(t => t.Slug == firmCode, cancellationToken);
            if (tenantExists)
            {
                return Conflict(new { message = "Firm code is already in use." });
            }

            var tenant = new Tenant
            {
                Id = Guid.NewGuid().ToString(),
                Name = firmName,
                Slug = firmCode,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.Tenants.Add(tenant);
            await _context.SaveChangesAsync(cancellationToken);

            _tenantContext.Set(tenant.Id, tenant.Slug);

            var duplicateTenantUser = await _context.Users
                .AsNoTracking()
                .AnyAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken);
            if (duplicateTenantUser)
            {
                return Conflict(new { message = "Email is already in use for this firm." });
            }

            var now = DateTime.UtcNow;
            var user = new User
            {
                Id = Guid.NewGuid().ToString(),
                Name = fullName,
                Email = email,
                NormalizedEmail = normalizedEmail,
                Role = "Attorney",
                PasswordHash = PasswordHashingHelper.HashPassword(password, _configuration),
                MfaEnabled = false,
                CreatedAt = now,
                UpdatedAt = now
            };

            var firmSettings = new FirmSettings
            {
                Id = Guid.NewGuid().ToString(),
                FirmName = firmName,
                UpdatedAt = now
            };

            var billingSettings = new BillingSettings
            {
                Id = Guid.NewGuid().ToString(),
                UpdatedAt = now
            };

            var entity = new FirmEntity
            {
                Id = Guid.NewGuid().ToString(),
                Name = firmName,
                LegalName = firmName,
                Email = email,
                IsDefault = true,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            };

            var office = new Office
            {
                Id = Guid.NewGuid().ToString(),
                EntityId = entity.Id,
                Name = "Main Office",
                Email = email,
                IsDefault = true,
                IsActive = true,
                TimeZone = "America/New_York",
                CreatedAt = now,
                UpdatedAt = now
            };

            _context.Users.Add(user);
            _context.FirmSettings.Add(firmSettings);
            _context.BillingSettings.Add(billingSettings);
            _context.FirmEntities.Add(entity);
            _context.Offices.Add(office);

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Public attorney registration failed for tenant slug {TenantSlug}.", firmCode);
                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    message = "Registration could not be completed."
                });
            }

            return Ok(new
            {
                message = "Registration completed.",
                tenantSlug = tenant.Slug,
                email = user.Email,
                planId = NormalizeOptional(request.PlanId)
            });
        }

        private static string? NormalizeRequired(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static string? NormalizeOptional(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static string NormalizeSlug(string? value)
        {
            var normalized = TenantSeedHelper.NormalizeSlug(value ?? string.Empty);
            var sanitized = System.Text.RegularExpressions.Regex.Replace(normalized, @"[^a-z0-9\-]+", "-");
            sanitized = System.Text.RegularExpressions.Regex.Replace(sanitized, @"\-{2,}", "-").Trim('-');
            return string.IsNullOrWhiteSpace(sanitized) ? "firm" : sanitized;
        }
    }

    public sealed class PublicAttorneyRegistrationRequest
    {
        public string? FirmName { get; set; }
        public string? FirmCode { get; set; }
        public string? FullName { get; set; }
        public string? Email { get; set; }
        public string? Password { get; set; }
        public string? PlanId { get; set; }
    }
}
