using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using JurisFlow.Server.Services;

namespace JurisFlow.Server.Controllers
{
    [Route("api/mfa")]
    [ApiController]
    public class MfaController : ControllerBase
    {
        private readonly JurisFlowDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly AuditLogger _auditLogger;
        private readonly SessionTokenService _sessionTokenService;
        private readonly TenantContext _tenantContext;

        public MfaController(JurisFlowDbContext context, IConfiguration configuration, AuditLogger auditLogger, SessionTokenService sessionTokenService, TenantContext tenantContext)
        {
            _context = context;
            _configuration = configuration;
            _auditLogger = auditLogger;
            _sessionTokenService = sessionTokenService;
            _tenantContext = tenantContext;
        }

        [Authorize(Policy = "StaffOnly")]
        [HttpGet("status")]
        public async Task<IActionResult> GetStatus()
        {
            var userId = GetUserId();
            if (string.IsNullOrEmpty(userId)) return Unauthorized();
            if (string.IsNullOrWhiteSpace(_tenantContext.TenantId)) return BadRequest(new { message = "Tenant is required." });

            var user = await GetTenantUserByIdAsync(userId);
            if (user == null) return Unauthorized();

            var backupCount = GetBackupCodes(user).Count;

            return Ok(new
            {
                enabled = user.MfaEnabled,
                hasSecret = !string.IsNullOrEmpty(user.MfaSecret),
                backupCodesRemaining = backupCount
            });
        }

        [Authorize(Policy = "StaffOnly")]
        [HttpPost("setup")]
        public async Task<IActionResult> SetupMfa()
        {
            var userId = GetUserId();
            if (string.IsNullOrEmpty(userId)) return Unauthorized();
            if (string.IsNullOrWhiteSpace(_tenantContext.TenantId)) return BadRequest(new { message = "Tenant is required." });

            var user = await GetTenantUserByIdAsync(userId);
            if (user == null) return Unauthorized();

            var secret = TotpService.GenerateSecret();
            user.MfaSecret = secret;
            user.MfaEnabled = false;
            user.MfaVerifiedAt = null;
            user.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            var issuer = _configuration["Jwt:Issuer"] ?? "JurisFlow";
            var accountName = user.Email;
            var otpauthUri = TotpService.BuildOtpauthUri(issuer, accountName, secret);

            await _auditLogger.LogAsync(HttpContext, "auth.mfa.setup", "User", user.Id, "MFA setup initiated");

            return Ok(new
            {
                secret,
                otpauthUri,
                issuer,
                accountName
            });
        }

        [Authorize(Policy = "StaffOnly")]
        [HttpPost("enable")]
        public async Task<IActionResult> EnableMfa([FromBody] MfaCodeDto dto)
        {
            var userId = GetUserId();
            if (string.IsNullOrEmpty(userId)) return Unauthorized();
            if (string.IsNullOrWhiteSpace(_tenantContext.TenantId)) return BadRequest(new { message = "Tenant is required." });

            var user = await GetTenantUserByIdAsync(userId);
            if (user == null) return Unauthorized();
            if (string.IsNullOrEmpty(user.MfaSecret))
            {
                return BadRequest(new { message = "MFA secret not configured. Run setup first." });
            }

            if (!TotpService.VerifyCode(user.MfaSecret, dto.Code))
            {
                return BadRequest(new { message = "Invalid authentication code." });
            }

            var backupCodes = TotpService.GenerateBackupCodes();
            user.MfaBackupCodesJson = JsonSerializer.Serialize(HashBackupCodes(backupCodes));
            user.MfaEnabled = true;
            user.MfaVerifiedAt = DateTime.UtcNow;
            user.MfaLastUsedAt = DateTime.UtcNow;
            user.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await _auditLogger.LogAsync(HttpContext, "auth.mfa.enabled", "User", user.Id, "MFA enabled");

            return Ok(new { backupCodes });
        }

        [Authorize(Policy = "StaffOnly")]
        [HttpPost("disable")]
        public async Task<IActionResult> DisableMfa([FromBody] MfaCodeDto dto)
        {
            var userId = GetUserId();
            if (string.IsNullOrEmpty(userId)) return Unauthorized();
            if (string.IsNullOrWhiteSpace(_tenantContext.TenantId)) return BadRequest(new { message = "Tenant is required." });

            var user = await GetTenantUserByIdAsync(userId);
            if (user == null) return Unauthorized();

            if (!VerifyCodeOrBackup(user, dto.Code))
            {
                return BadRequest(new { message = "Invalid authentication code." });
            }

            user.MfaEnabled = false;
            user.MfaSecret = null;
            user.MfaBackupCodesJson = null;
            user.MfaVerifiedAt = null;
            user.MfaLastUsedAt = DateTime.UtcNow;
            user.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await _auditLogger.LogAsync(HttpContext, "auth.mfa.disabled", "User", user.Id, "MFA disabled");

            return Ok(new { message = "MFA disabled" });
        }

        [HttpPost("verify")]
        [AllowAnonymous]
        [EnableRateLimiting("AuthMfa")]
        public async Task<IActionResult> VerifyMfa([FromBody] MfaVerifyDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.ChallengeId))
            {
                return BadRequest(new { message = "Challenge ID is required." });
            }

            if (string.IsNullOrWhiteSpace(_tenantContext.TenantId))
            {
                return BadRequest(new { message = "Tenant is required." });
            }
            var tenantId = _tenantContext.TenantId!;

            var challenge = await _context.MfaChallenges.FirstOrDefaultAsync(c =>
                c.Id == dto.ChallengeId &&
                EF.Property<string>(c, "TenantId") == tenantId);
            if (challenge == null || challenge.IsUsed || challenge.ExpiresAt < DateTime.UtcNow)
            {
                return Unauthorized(new { message = "Invalid or expired MFA challenge." });
            }

            var user = await _context.Users.FirstOrDefaultAsync(u =>
                u.Id == challenge.UserId &&
                EF.Property<string>(u, "TenantId") == tenantId);
            if (user == null || string.IsNullOrEmpty(user.MfaSecret) || !user.MfaEnabled)
            {
                return Unauthorized(new { message = "MFA not enabled for this account." });
            }

            if (!VerifyCodeOrBackup(user, dto.Code))
            {
                await _auditLogger.LogAsync(HttpContext, "auth.mfa.failed", "User", user.Id, "MFA verification failed");
                return Unauthorized(new { message = "Invalid authentication code." });
            }

            challenge.IsUsed = true;
            challenge.VerifiedAt = DateTime.UtcNow;
            user.MfaLastUsedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            var (session, refreshToken) = await CreateSessionAsync(user.Id);
            var accessTokenExpiresAt = GetAccessTokenExpiry();
            var token = GenerateJwtToken(user, session.Id, accessTokenExpiresAt);

            await _auditLogger.LogAsync(HttpContext, "auth.mfa.success", "User", user.Id, "MFA verified");

            return Ok(new
            {
                token,
                session = new { id = session.Id, expiresAt = session.ExpiresAt },
                refreshToken,
                refreshTokenExpiresAt = session.RefreshTokenExpiresAt,
                user = new
                {
                    id = user.Id,
                    name = user.Name,
                    email = user.Email,
                    role = user.Role
                }
            });
        }

        private string? GetUserId()
        {
            return User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
        }

        private Task<User?> GetTenantUserByIdAsync(string userId)
        {
            var tenantId = _tenantContext.TenantId!;
            return _context.Users.FirstOrDefaultAsync(u =>
                u.Id == userId &&
                EF.Property<string>(u, "TenantId") == tenantId);
        }

        private async Task<(AuthSession session, string refreshToken)> CreateSessionAsync(string userId)
        {
            var sessionMinutes = _configuration.GetValue("Security:SessionTimeoutMinutes", 480);
            var refreshTokenDays = _configuration.GetValue("Security:RefreshTokenDays", 30);
            var refreshToken = _sessionTokenService.GenerateRefreshToken();
            var session = new AuthSession
            {
                UserId = userId,
                TenantId = _tenantContext.TenantId,
                SubjectType = "User",
                CreatedAt = DateTime.UtcNow,
                LastSeenAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddMinutes(sessionMinutes),
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent = HttpContext.Request.Headers.UserAgent.ToString(),
                RefreshTokenHash = _sessionTokenService.HashToken(refreshToken),
                RefreshTokenIssuedAt = DateTime.UtcNow,
                RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(refreshTokenDays)
            };

            _context.AuthSessions.Add(session);
            await _context.SaveChangesAsync();
            return (session, refreshToken);
        }

        private string GenerateJwtToken(User user, string sessionId, DateTime expiresAt)
        {
            var jwtKey = RequireJwtSetting("Jwt:Key");
            var jwtIssuer = RequireJwtSetting("Jwt:Issuer");
            var jwtAudience = RequireJwtSetting("Jwt:Audience");

            var securityKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(jwtKey));
            var credentials = new Microsoft.IdentityModel.Tokens.SigningCredentials(securityKey, Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
                new Claim(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub, user.Id),
                new Claim(ClaimTypes.NameIdentifier, user.Id),
                new Claim(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Email, user.Email),
                new Claim(ClaimTypes.Role, user.Role),
                new Claim("sid", sessionId),
                new Claim("tenantId", _tenantContext.TenantId ?? string.Empty),
                new Claim("tenantSlug", _tenantContext.TenantSlug ?? string.Empty)
            };

            var token = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
                issuer: jwtIssuer,
                audience: jwtAudience,
                claims: claims,
                expires: expiresAt,
                signingCredentials: credentials);

            return new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(token);
        }

        private DateTime GetAccessTokenExpiry()
        {
            var accessMinutes = _configuration.GetValue("Security:AccessTokenMinutes", 30);
            return DateTime.UtcNow.AddMinutes(accessMinutes);
        }

        private string RequireJwtSetting(string key)
        {
            var value = _configuration[key];
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"{key} is missing in configuration.");
            }

            return value;
        }

        private List<string> HashBackupCodes(IEnumerable<string> codes)
        {
            return codes.Select(code => PasswordHashingHelper.HashPassword(code, _configuration)).ToList();
        }

        private static List<string> GetBackupCodes(User user)
        {
            if (string.IsNullOrWhiteSpace(user.MfaBackupCodesJson))
            {
                return new List<string>();
            }

            try
            {
                return JsonSerializer.Deserialize<List<string>>(user.MfaBackupCodesJson) ?? new List<string>();
            }
            catch
            {
                return new List<string>();
            }
        }

        private bool VerifyCodeOrBackup(User user, string? code)
        {
            if (string.IsNullOrWhiteSpace(code)) return false;

            if (!string.IsNullOrEmpty(user.MfaSecret) && TotpService.VerifyCode(user.MfaSecret, code))
            {
                return true;
            }

            var backupCodes = GetBackupCodes(user);
            if (backupCodes.Count == 0) return false;

            var matching = backupCodes.FirstOrDefault(hash => BCrypt.Net.BCrypt.Verify(code, hash));
            if (matching == null) return false;

            backupCodes.Remove(matching);
            user.MfaBackupCodesJson = JsonSerializer.Serialize(backupCodes);
            return true;
        }
    }

    public class MfaCodeDto
    {
        public string Code { get; set; } = string.Empty;
    }

    public class MfaVerifyDto
    {
        public string ChallengeId { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
    }
}
