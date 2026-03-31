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
using BCrypt.Net;
using JurisFlow.Server.Services;

namespace JurisFlow.Server.Controllers
{
    [Route("api/client")]
    [ApiController]
    public class ClientAuthController : ControllerBase
    {
        private const string InvalidCredentialsMessage = "Invalid email or password";

        private readonly JurisFlowDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly AuditLogger _auditLogger;
        private readonly SessionTokenService _sessionTokenService;
        private readonly TenantContext _tenantContext;
        private readonly LoginAttemptService _loginAttemptService;
        private readonly ILogger<ClientAuthController> _logger;

        public ClientAuthController(
            JurisFlowDbContext context,
            IConfiguration configuration,
            AuditLogger auditLogger,
            SessionTokenService sessionTokenService,
            TenantContext tenantContext,
            LoginAttemptService loginAttemptService,
            ILogger<ClientAuthController> logger)
        {
            _context = context;
            _configuration = configuration;
            _auditLogger = auditLogger;
            _sessionTokenService = sessionTokenService;
            _tenantContext = tenantContext;
            _loginAttemptService = loginAttemptService;
            _logger = logger;
        }

        public class ClientLoginDto
        {
            public string Email { get; set; } = string.Empty;
            public string Password { get; set; } = string.Empty;
        }

        [AllowAnonymous]
        [EnableRateLimiting("ClientAuthLogin")]
        [HttpPost("login")]
        public async Task<IActionResult> ClientLogin([FromBody] ClientLoginDto? loginDto)
        {
            if (loginDto == null)
            {
                return BadRequest(new { message = "Request body is required." });
            }

            if (string.IsNullOrWhiteSpace(loginDto.Email) || string.IsNullOrWhiteSpace(loginDto.Password))
            {
                return BadRequest(new { message = "Email and password are required" });
            }

            if (string.IsNullOrWhiteSpace(_tenantContext.TenantId))
            {
                return BadRequest(new { message = "Tenant is required." });
            }

            var tenantId = RequireTenantId();
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
                    "client.login.blocked",
                    "Client",
                    null,
                    $"Email={email}, Ip={ipAddress}, Reason=throttle");
                return TooManyLoginAttempts(throttleStatus);
            }

            var client = await TenantScope(_context.Clients)
                .FirstOrDefaultAsync(c => c.NormalizedEmail == email);

            if (client == null)
            {
                return await RejectInvalidLoginAsync(tenantId, email, ipAddress, null);
            }

            if (!IsClientPortalAccessAllowed(client))
            {
                return await RejectInvalidLoginAsync(tenantId, email, ipAddress, client.Id);
            }

            bool isPasswordValid = BCrypt.Net.BCrypt.Verify(loginDto.Password, client.PasswordHash);

            if (!isPasswordValid)
            {
                return await RejectInvalidLoginAsync(tenantId, email, ipAddress, client.Id);
            }

            _loginAttemptService.RegisterSuccess(tenantId, email, ipAddress);

            string token;
            string refreshToken;
            AuthSession session;
            try
            {
                var now = DateTime.UtcNow;
                await using var transaction = await _context.Database.BeginTransactionAsync();
                client.LastLogin = now;
                (session, refreshToken) = BuildSession(client.Id, "Client", now);
                _context.AuthSessions.Add(session);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                var accessTokenExpiresAt = GetAccessTokenExpiry();
                token = GenerateClientJwtToken(client, session.Id, accessTokenExpiresAt);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Client login failed after credential validation. TenantId={TenantId} ClientId={ClientId} Email={Email}",
                    tenantId,
                    client.Id,
                    email);
                throw;
            }

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
                client = new
                {
                    id = client.Id,
                    name = client.Name,
                    email = client.Email,
                    phone = client.Phone,
                    mobile = client.Mobile,
                    company = client.Company,
                    type = client.Type,
                    status = client.Status
                }
            };

            await _auditLogger.LogAsync(HttpContext, "client.login.success", "Client", client.Id, $"Email: {client.Email}");

            return Ok(response);
        }

        private (AuthSession session, string refreshToken) BuildSession(string clientId, string subjectType, DateTime now)
        {
            var sessionMinutes = _configuration.GetValue("Security:SessionTimeoutMinutes", 480);
            var refreshTokenDays = _configuration.GetValue("Security:RefreshTokenDays", 30);
            var refreshToken = _sessionTokenService.GenerateRefreshToken();

            var session = new AuthSession
            {
                ClientId = clientId,
                TenantId = _tenantContext.TenantId,
                SubjectType = subjectType,
                CreatedAt = now,
                LastSeenAt = now,
                ExpiresAt = now.AddMinutes(sessionMinutes),
                IpAddress = GetRemoteIpAddress(),
                UserAgent = HttpContext.Request.Headers.UserAgent.ToString(),
                RefreshTokenHash = _sessionTokenService.HashToken(refreshToken),
                RefreshTokenIssuedAt = now,
                RefreshTokenExpiresAt = now.AddDays(refreshTokenDays)
            };

            return (session, refreshToken);
        }

        private string GenerateClientJwtToken(Client client, string sessionId, DateTime expiresAt)
        {
            var jwtKey = RequireJwtSetting("Jwt:Key");
            var jwtIssuer = RequireJwtSetting("Jwt:Issuer");
            var jwtAudience = RequireJwtSetting("Jwt:Audience");

            var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
            var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, client.Id),
                new Claim(ClaimTypes.NameIdentifier, client.Id),
                new Claim(JwtRegisteredClaimNames.Email, client.Email),
                new Claim(ClaimTypes.Role, "Client"),
                new Claim("clientId", client.Id),
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
        [EnableRateLimiting("ClientAuthRefresh")]
        [HttpPost("refresh")]
        public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequest? dto)
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

            var tenantId = RequireTenantId();
            var session = await _context.AuthSessions.FirstOrDefaultAsync(s =>
                s.Id == dto.SessionId &&
                s.ClientId != null &&
                s.SubjectType == "Client" &&
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
                await _auditLogger.LogAsync(HttpContext, "client.session.expired", "AuthSession", session.Id, "Absolute session age reached.");
                return Unauthorized(new { message = "Session has expired." });
            }

            var client = await TenantScope(_context.Clients)
                .FirstOrDefaultAsync(c => c.Id == session.ClientId);
            if (client == null)
            {
                return Unauthorized(new { message = "Client not found." });
            }

            if (!IsClientPortalAccessAllowed(client))
            {
                session.RevokedAt = now;
                session.RevokedReason = "client_access_revoked";
                await _context.SaveChangesAsync();
                return Unauthorized(new { message = "Session is invalid." });
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
            var token = GenerateClientJwtToken(client, session.Id, accessTokenExpiresAt);

            return Ok(new
            {
                token,
                refreshToken = newRefreshToken,
                refreshTokenExpiresAt = session.RefreshTokenExpiresAt,
                session = new { id = session.Id, expiresAt = session.ExpiresAt },
                client = new
                {
                    id = client.Id,
                    name = client.Name,
                    email = client.Email,
                    phone = client.Phone,
                    mobile = client.Mobile,
                    company = client.Company,
                    type = client.Type,
                    status = client.Status
                }
            });
        }

        public class RefreshTokenRequest
        {
            public string SessionId { get; set; } = string.Empty;
            public string RefreshToken { get; set; } = string.Empty;
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

        private async Task<IActionResult> RejectInvalidLoginAsync(string tenantId, string email, string ipAddress, string? clientId)
        {
            var failedStatus = _loginAttemptService.RegisterFailure(tenantId, email, ipAddress);
            if (failedStatus.IsLockedOut)
            {
                await _auditLogger.LogAsync(
                    HttpContext,
                    "client.login.blocked",
                    "Client",
                    clientId,
                    $"Email={email}, Ip={ipAddress}, Reason=max_attempts");
                return TooManyLoginAttempts(failedStatus);
            }

            return Unauthorized(new { message = InvalidCredentialsMessage });
        }

        private async System.Threading.Tasks.Task RevokeSessionForRefreshMismatchAsync(AuthSession session, string tenantId, DateTime now)
        {
            if (session.RevokedAt == null)
            {
                session.RevokedAt = now;
                session.RevokedReason = "refresh_token_reuse_detected";
            }

            if (!string.IsNullOrWhiteSpace(session.ClientId))
            {
                var siblingSessions = await _context.AuthSessions
                    .Where(s =>
                        s.ClientId == session.ClientId &&
                        s.SubjectType == "Client" &&
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
                "client.refresh.reuse_detected",
                nameof(AuthSession),
                session.Id,
                "Refresh token mismatch detected; active client sessions revoked.");
        }

        private IQueryable<T> TenantScope<T>(IQueryable<T> query) where T : class
        {
            var tenantId = RequireTenantId();
            return query.Where(e => EF.Property<string>(e, "TenantId") == tenantId);
        }

        private string RequireTenantId()
        {
            var tenantId = _tenantContext.TenantId;
            if (string.IsNullOrWhiteSpace(tenantId))
            {
                throw new InvalidOperationException("Tenant is required.");
            }

            return tenantId;
        }

        private static bool IsClientPortalAccessAllowed(Client client)
        {
            return client.PortalEnabled &&
                   !string.IsNullOrWhiteSpace(client.PasswordHash) &&
                   string.Equals(client.Status?.Trim(), "Active", StringComparison.OrdinalIgnoreCase);
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
