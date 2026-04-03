using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using JurisFlow.Server.DTOs;
using JurisFlow.Server.Services;

namespace JurisFlow.Server.Controllers
{
    [Route("api")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly JurisFlowDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly AuditLogger _auditLogger;
        private readonly SessionTokenService _sessionTokenService;
        private readonly TenantContext _tenantContext;
        private readonly LoginAttemptService _loginAttemptService;
        private readonly PasswordVerificationService _passwordVerificationService;

        public AuthController(
            JurisFlowDbContext context,
            IConfiguration configuration,
            AuditLogger auditLogger,
            SessionTokenService sessionTokenService,
            TenantContext tenantContext,
            LoginAttemptService loginAttemptService,
            PasswordVerificationService passwordVerificationService)
        {
            _context = context;
            _configuration = configuration;
            _auditLogger = auditLogger;
            _sessionTokenService = sessionTokenService;
            _tenantContext = tenantContext;
            _loginAttemptService = loginAttemptService;
            _passwordVerificationService = passwordVerificationService;
        }

        [AllowAnonymous]
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginDto loginDto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            if (string.IsNullOrWhiteSpace(_tenantContext.TenantId))
            {
                return BadRequest(new { message = "Tenant is required." });
            }

            var tenantId = _tenantContext.TenantId!;
            var email = NormalizeEmail(loginDto.Email);
            if (string.IsNullOrWhiteSpace(email))
            {
                return BadRequest(new { message = "Email is required." });
            }
            var ipAddress = GetRemoteIpAddress();

            var throttleStatus = _loginAttemptService.GetStatus(tenantId, email, ipAddress);
            if (throttleStatus.IsLockedOut)
            {
                await _auditLogger.LogAsync(
                    HttpContext,
                    "auth.login.blocked",
                    "User",
                    null,
                    $"Email={email}, Ip={ipAddress}, Reason=throttle");
                return TooManyLoginAttempts(throttleStatus);
            }

            var user = await _context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u =>
                EF.Property<string>(u, "TenantId") == tenantId &&
                u.NormalizedEmail == email);

            if (user == null)
            {
                var failedStatus = _loginAttemptService.RegisterFailure(tenantId, email, ipAddress);
                if (failedStatus.IsLockedOut)
                {
                    await _auditLogger.LogAsync(
                        HttpContext,
                        "auth.login.blocked",
                        "User",
                        null,
                        $"Email={email}, Ip={ipAddress}, Reason=max_attempts");
                    return TooManyLoginAttempts(failedStatus);
                }

                return Unauthorized(new { message = "Invalid credentials" });
            }
           
            bool isPasswordValid = await _passwordVerificationService.VerifyAsync(
                loginDto.Password,
                user.PasswordHash,
                HttpContext.RequestAborted);

            if (!isPasswordValid)
            {
                var failedStatus = _loginAttemptService.RegisterFailure(tenantId, email, ipAddress);
                if (failedStatus.IsLockedOut)
                {
                    await _auditLogger.LogAsync(
                        HttpContext,
                        "auth.login.blocked",
                        "User",
                        user.Id,
                        $"Email={email}, Ip={ipAddress}, Reason=max_attempts");
                    return TooManyLoginAttempts(failedStatus);
                }

                return Unauthorized(new { message = "Invalid credentials" });
            }

            _loginAttemptService.RegisterSuccess(tenantId, email, ipAddress);

            var mfaEnforced = _configuration.GetValue("Security:MfaEnforced", true);
            if (mfaEnforced && user.MfaEnabled && !string.IsNullOrEmpty(user.MfaSecret))
            {
                var challengeMinutes = _configuration.GetValue("Security:MfaChallengeMinutes", 10);
                var challenge = new MfaChallenge
                {
                    UserId = user.Id,
                    ExpiresAt = DateTime.UtcNow.AddMinutes(challengeMinutes),
                    IpAddress = GetRemoteIpAddress(),
                    UserAgent = HttpContext.Request.Headers.UserAgent.ToString()
                };

                _context.MfaChallenges.Add(challenge);
                await _context.SaveChangesAsync();

                await _auditLogger.LogAsync(HttpContext, "auth.login.mfa_required", "User", user.Id, $"Email: {user.Email}");

                return Ok(new
                {
                    mfaRequired = true,
                    challengeId = challenge.Id,
                    challengeExpiresAt = challenge.ExpiresAt
                });
            }

            var (session, refreshToken) = await CreateSessionAsync(user.Id, "User");
            var accessTokenExpiresAt = GetAccessTokenExpiry();
            var token = GenerateJwtToken(user, session.Id, accessTokenExpiresAt);

            var response = new
            {
                token,
                session = new
                {
                    id = session.Id,
                    expiresAt = session.ExpiresAt
                },
                refreshToken,
                refreshTokenExpiresAt = session.RefreshTokenExpiresAt,
                user = new
                {
                    id = user.Id,
                    name = user.Name,
                    email = user.Email,
                    role = user.Role
                }
            };

            await _auditLogger.LogAsync(HttpContext, "auth.login.success", "User", user.Id, $"Email: {user.Email}");

            return Ok(response);
        }

        private async Task<(AuthSession session, string refreshToken)> CreateSessionAsync(string userId, string subjectType)
        {
            if (string.IsNullOrWhiteSpace(_tenantContext.TenantId))
            {
                throw new InvalidOperationException("Tenant context is required for session creation.");
            }

            var sessionMinutes = _configuration.GetValue("Security:SessionTimeoutMinutes", 480);
            var refreshTokenDays = _configuration.GetValue("Security:RefreshTokenDays", 30);
            var refreshToken = _sessionTokenService.GenerateRefreshToken();

            var session = new AuthSession
            {
                UserId = userId,
                TenantId = _tenantContext.TenantId,
                SubjectType = subjectType,
                CreatedAt = DateTime.UtcNow,
                LastSeenAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddMinutes(sessionMinutes),
                IpAddress = GetRemoteIpAddress(),
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

            var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
            var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id),
                new Claim(ClaimTypes.NameIdentifier, user.Id),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim(ClaimTypes.Role, user.Role),
                new Claim("sid", sessionId),
                new Claim("tenantId", _tenantContext.TenantId ?? string.Empty),
                new Claim("tenantSlug", _tenantContext.TenantSlug ?? string.Empty)
            };

            var token = new JwtSecurityToken(
                issuer: jwtIssuer,
                audience: jwtAudience,
                claims: claims,
                expires: expiresAt,
                signingCredentials: credentials);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        private DateTime GetAccessTokenExpiry()
        {
            var accessMinutes = _configuration.GetValue("Security:AccessTokenMinutes", 30);
            return DateTime.UtcNow.AddMinutes(accessMinutes);
        }

        [AllowAnonymous]
        [EnableRateLimiting("AuthRefresh")]
        [HttpPost("auth/refresh")]
        public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequest dto)
        {
            if (dto == null)
            {
                return BadRequest(new { message = "Request body is required." });
            }

            if (string.IsNullOrWhiteSpace(dto.SessionId) || string.IsNullOrWhiteSpace(dto.RefreshToken))
            {
                return BadRequest(new { message = "SessionId and refresh token are required." });
            }

            if (string.IsNullOrWhiteSpace(_tenantContext.TenantId))
            {
                return BadRequest(new { message = "Tenant is required." });
            }

            var tenantId = _tenantContext.TenantId!;
            var session = await _context.AuthSessions.FirstOrDefaultAsync(s =>
                s.Id == dto.SessionId &&
                s.UserId != null &&
                s.SubjectType == "User" &&
                s.TenantId == tenantId);
            if (session == null || session.RevokedAt != null)
            {
                return Unauthorized(new { message = "Session is invalid." });
            }

            var now = DateTime.UtcNow;
            if (string.IsNullOrWhiteSpace(session.RefreshTokenHash) ||
                session.RefreshTokenExpiresAt == null ||
                session.ExpiresAt <= now ||
                session.RefreshTokenExpiresAt <= now)
            {
                return Unauthorized(new { message = "Session has expired." });
            }

            if (!_sessionTokenService.VerifyToken(dto.RefreshToken, session.RefreshTokenHash))
            {
                await RevokeSessionForRefreshMismatchAsync(session, tenantId, now);
                return Unauthorized(new { message = "Refresh token is invalid." });
            }

            var absoluteSessionDays = Math.Max(1, _configuration.GetValue("Security:SessionAbsoluteMaxDays", 30));
            if (session.CreatedAt.AddDays(absoluteSessionDays) <= now)
            {
                session.RevokedAt = now;
                session.RevokedReason = "session_max_age_reached";
                await _context.SaveChangesAsync();
                await _auditLogger.LogAsync(HttpContext, "auth.session.expired", "AuthSession", session.Id, "Absolute session age reached.");
                return Unauthorized(new { message = "Session has expired." });
            }

            var user = await _context.Users.FirstOrDefaultAsync(u =>
                u.Id == session.UserId &&
                EF.Property<string>(u, "TenantId") == tenantId);
            if (user == null)
            {
                return Unauthorized(new { message = "User not found." });
            }

            var sessionMinutes = _configuration.GetValue("Security:SessionTimeoutMinutes", 480);
            var refreshTokenDays = _configuration.GetValue("Security:RefreshTokenDays", 30);
            var newRefreshToken = _sessionTokenService.GenerateRefreshToken();

            session.LastSeenAt = now;
            session.ExpiresAt = now.AddMinutes(sessionMinutes);
            session.RefreshTokenHash = _sessionTokenService.HashToken(newRefreshToken);
            session.RefreshTokenIssuedAt = now;
            session.RefreshTokenExpiresAt = now.AddDays(refreshTokenDays);
            session.RefreshTokenRotatedAt = now;

            await _context.SaveChangesAsync();

            var accessTokenExpiresAt = GetAccessTokenExpiry();
            var token = GenerateJwtToken(user, session.Id, accessTokenExpiresAt);

            return Ok(new
            {
                token,
                refreshToken = newRefreshToken,
                refreshTokenExpiresAt = session.RefreshTokenExpiresAt,
                session = new { id = session.Id, expiresAt = session.ExpiresAt },
                user = new { id = user.Id, name = user.Name, email = user.Email, role = user.Role }
            });
        }

        [Authorize]
        [HttpPut("user/profile")]
        public async Task<IActionResult> UpdateOwnProfile([FromBody] UpdateOwnProfileRequest dto)
        {
            if (dto == null)
            {
                return BadRequest(new { message = "Request body is required." });
            }

            if (string.IsNullOrWhiteSpace(_tenantContext.TenantId))
            {
                return BadRequest(new { message = "Tenant is required." });
            }

            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                         ?? User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Unauthorized(new { message = "User context is missing." });
            }

            var tenantId = _tenantContext.TenantId!;
            var claimTenantId = User.FindFirst("tenantId")?.Value ?? User.FindFirst("tid")?.Value;
            if (!string.IsNullOrWhiteSpace(claimTenantId) && !string.Equals(claimTenantId, tenantId, StringComparison.Ordinal))
            {
                return Forbid();
            }

            var user = await _context.Users.FirstOrDefaultAsync(u =>
                u.Id == userId &&
                EF.Property<string>(u, "TenantId") == tenantId);
            if (user == null)
            {
                return NotFound(new { message = "User not found." });
            }

            var nextName = (dto.Name ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(nextName))
            {
                return BadRequest(new { message = "Name is required." });
            }

            var nextEmail = string.IsNullOrWhiteSpace(dto.Email) ? user.Email : dto.Email.Trim();
            if (string.IsNullOrWhiteSpace(nextEmail))
            {
                return BadRequest(new { message = "Email is required." });
            }

            var normalizedEmail = NormalizeEmail(nextEmail);
            if (string.IsNullOrWhiteSpace(normalizedEmail))
            {
                return BadRequest(new { message = "Email is required." });
            }
            var emailChanged = !string.Equals(user.NormalizedEmail ?? NormalizeEmail(user.Email), normalizedEmail, StringComparison.Ordinal);
            if (emailChanged)
            {
                var duplicate = await _context.Users.AnyAsync(u =>
                    u.Id != user.Id &&
                    EF.Property<string>(u, "TenantId") == tenantId &&
                    u.NormalizedEmail == normalizedEmail);
                if (duplicate)
                {
                    return BadRequest(new { message = "Email is already in use." });
                }
            }

            var changed = false;
            if (!string.Equals(user.Name, nextName, StringComparison.Ordinal))
            {
                user.Name = nextName;
                changed = true;
            }

            if (!string.Equals(user.Email, nextEmail, StringComparison.Ordinal))
            {
                user.Email = nextEmail;
                user.NormalizedEmail = normalizedEmail;
                changed = true;
            }

            changed |= SetIfDifferent(user.Phone, NormalizeOptionalString(dto.Phone), value => user.Phone = value);
            changed |= SetIfDifferent(user.Mobile, NormalizeOptionalString(dto.Mobile), value => user.Mobile = value);
            changed |= SetIfDifferent(user.Address, NormalizeOptionalString(dto.Address), value => user.Address = value);
            changed |= SetIfDifferent(user.City, NormalizeOptionalString(dto.City), value => user.City = value);
            changed |= SetIfDifferent(user.State, NormalizeOptionalString(dto.State), value => user.State = value);
            changed |= SetIfDifferent(user.ZipCode, NormalizeOptionalString(dto.ZipCode), value => user.ZipCode = value);
            changed |= SetIfDifferent(user.Country, NormalizeOptionalString(dto.Country), value => user.Country = value);
            changed |= SetIfDifferent(user.BarNumber, NormalizeOptionalString(dto.BarNumber), value => user.BarNumber = value);
            changed |= SetIfDifferent(user.Bio, NormalizeOptionalString(dto.Bio), value => user.Bio = value);

            if (changed)
            {
                user.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                await _auditLogger.LogAsync(HttpContext, "user.profile.update", nameof(User), user.Id, $"Email={user.Email}");
            }

            return Ok(new
            {
                user = new
                {
                    id = user.Id,
                    name = user.Name,
                    email = user.Email,
                    role = user.Role
                }
            });
        }

        public class RefreshTokenRequest
        {
            public string SessionId { get; set; } = string.Empty;
            public string RefreshToken { get; set; } = string.Empty;
        }

        public class UpdateOwnProfileRequest
        {
            public string? Name { get; set; }
            public string? Email { get; set; }
            public string? Phone { get; set; }
            public string? Mobile { get; set; }
            public string? Address { get; set; }
            public string? City { get; set; }
            public string? State { get; set; }
            public string? ZipCode { get; set; }
            public string? Country { get; set; }
            public string? BarNumber { get; set; }
            public string? Bio { get; set; }
        }

        private IActionResult TooManyLoginAttempts(LoginThrottleStatus status)
        {
            var retryAfterSeconds = status.RetryAfter.HasValue
                ? Math.Max(1, (int)Math.Ceiling(status.RetryAfter.Value.TotalSeconds))
                : 60;

            return StatusCode(StatusCodes.Status429TooManyRequests, new
            {
                message = "Too many failed login attempts. Please retry later.",
                retryAfterSeconds
            });
        }

        private async System.Threading.Tasks.Task RevokeSessionForRefreshMismatchAsync(AuthSession session, string tenantId, DateTime now)
        {
            if (session.RevokedAt == null)
            {
                session.RevokedAt = now;
                session.RevokedReason = "refresh_token_reuse_detected";
            }

            if (!string.IsNullOrWhiteSpace(session.UserId))
            {
                var siblingSessions = await _context.AuthSessions
                    .Where(s =>
                        s.UserId == session.UserId &&
                        s.SubjectType == "User" &&
                        s.TenantId == tenantId &&
                        s.RevokedAt == null &&
                        s.Id != session.Id)
                    .ToListAsync();

                foreach (var sibling in siblingSessions)
                {
                    sibling.RevokedAt = now;
                    sibling.RevokedReason = "refresh_token_reuse_detected";
                }
            }

            await _context.SaveChangesAsync();
            await _auditLogger.LogAsync(
                HttpContext,
                "auth.refresh.reuse_detected",
                nameof(AuthSession),
                session.Id,
                "Refresh token mismatch detected; active sessions revoked.");
        }

        private static bool SetIfDifferent(string? currentValue, string? nextValue, Action<string?> assign)
        {
            if (string.Equals(currentValue, nextValue, StringComparison.Ordinal))
            {
                return false;
            }

            assign(nextValue);
            return true;
        }

        private static string? NormalizeOptionalString(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static string? NormalizeEmail(string? email)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                return null;
            }

            return EmailAddressNormalizer.Normalize(email);
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

        private string GetRemoteIpAddress()
        {
            return HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        }
    }
}
