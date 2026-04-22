using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JurisFlow.Server.DTOs;
using JurisFlow.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JurisFlow.Server.Controllers
{
    [Route("api/google/oauth")]
    [ApiController]
    [Authorize(Policy = "StaffOrClient")]
    public class GoogleOAuthController : ControllerBase
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly OAuthStateService _stateService;
        private readonly IIntegrationSecretStore _secretStore;

        public GoogleOAuthController(
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            OAuthStateService stateService,
            IIntegrationSecretStore secretStore)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _stateService = stateService;
            _secretStore = secretStore;
        }

        [HttpPost]
        public async Task<IActionResult> ExchangeGoogleCode([FromBody] OAuthCodeDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Code))
            {
                return BadRequest(new { message = "Authorization code is required." });
            }

            var principalId = GetPrincipalId();
            if (string.IsNullOrWhiteSpace(principalId))
            {
                return Unauthorized();
            }

            if (!_stateService.TryValidate(dto.State, "google", principalId, out var statePayload, out var stateError) || statePayload == null)
            {
                return BadRequest(new { message = stateError ?? "OAuth state is invalid." });
            }

            OAuthTokenResponse token;
            try
            {
                token = await ExchangeAuthorizationCodeAsync(dto.Code, HttpContext.RequestAborted);
            }
            catch (OAuthTokenExchangeException ex)
            {
                return StatusCode(ex.StatusCode, new { message = ex.Message, detail = ex.Detail });
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("OAuth is not configured", StringComparison.Ordinal))
            {
                return StatusCode(500, new { message = ex.Message });
            }

            if (string.IsNullOrWhiteSpace(token.AccessToken))
            {
                return StatusCode(502, new { message = "Google token exchange returned no access token." });
            }

            var connectionId = BuildConnectionId(statePayload.Target, principalId);
            var existingSecrets = await _secretStore.GetAsync(connectionId, IntegrationSecretScope.Connect, HttpContext.RequestAborted);
            await _secretStore.UpsertAsync(
                connectionId,
                $"google:{statePayload.Target}",
                new IntegrationSecretMaterial
                {
                    AccessToken = token.AccessToken,
                    RefreshToken = string.IsNullOrWhiteSpace(token.RefreshToken) ? existingSecrets?.RefreshToken : token.RefreshToken,
                    TokenType = token.TokenType,
                    Scope = token.Scope,
                    ExpiresAtUtc = token.ExpiresIn.HasValue ? DateTime.UtcNow.AddSeconds(token.ExpiresIn.Value) : null
                },
                IntegrationSecretScope.Connect,
                HttpContext.RequestAborted);

            return Ok(new
            {
                connectionId,
                accessToken = token.AccessToken,
                tokenType = token.TokenType,
                scope = token.Scope,
                expiresIn = token.ExpiresIn,
                target = statePayload.Target,
                returnPath = statePayload.ReturnPath,
                connected = true
            });
        }

        [HttpPost("refresh")]
        public async Task<IActionResult> RefreshGoogleToken([FromBody] OAuthRefreshDto dto)
        {
            var principalId = GetPrincipalId();
            if (string.IsNullOrWhiteSpace(principalId))
            {
                return Unauthorized();
            }

            var target = _stateService.NormalizeTarget("google", dto.Target);
            if (target == null)
            {
                return BadRequest(new { message = "Unsupported Google OAuth target." });
            }

            var connectionId = BuildConnectionId(target, principalId);
            var secrets = await _secretStore.GetAsync(connectionId, IntegrationSecretScope.Connect, HttpContext.RequestAborted);
            if (string.IsNullOrWhiteSpace(secrets?.RefreshToken))
            {
                return NotFound(new { message = "Google authorization requires reconnect." });
            }

            OAuthTokenResponse token;
            try
            {
                token = await RefreshTokenAsync(secrets.RefreshToken, HttpContext.RequestAborted);
            }
            catch (OAuthTokenExchangeException ex)
            {
                return StatusCode(ex.StatusCode, new { message = ex.Message, detail = ex.Detail });
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("OAuth is not configured", StringComparison.Ordinal))
            {
                return StatusCode(500, new { message = ex.Message });
            }

            if (string.IsNullOrWhiteSpace(token.AccessToken))
            {
                return StatusCode(502, new { message = "Google token refresh returned no access token." });
            }

            await _secretStore.UpsertAsync(
                connectionId,
                $"google:{target}",
                new IntegrationSecretMaterial
                {
                    AccessToken = token.AccessToken,
                    RefreshToken = string.IsNullOrWhiteSpace(token.RefreshToken) ? secrets.RefreshToken : token.RefreshToken,
                    TokenType = token.TokenType,
                    Scope = token.Scope,
                    ExpiresAtUtc = token.ExpiresIn.HasValue ? DateTime.UtcNow.AddSeconds(token.ExpiresIn.Value) : null
                },
                IntegrationSecretScope.Connect,
                HttpContext.RequestAborted);

            return Ok(new
            {
                connectionId,
                accessToken = token.AccessToken,
                tokenType = token.TokenType,
                scope = token.Scope,
                expiresIn = token.ExpiresIn,
                target
            });
        }

        private async Task<OAuthTokenResponse> ExchangeAuthorizationCodeAsync(string code, CancellationToken cancellationToken)
        {
            var (clientId, clientSecret) = GetGoogleCredentials();
            var redirectUri = ResolveRedirectUri(
                _configuration["Integrations:Google:RedirectUri"],
                "/auth/google/callback",
                "http://localhost:3000");

            var form = new Dictionary<string, string>
            {
                ["code"] = code,
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["redirect_uri"] = redirectUri,
                ["grant_type"] = "authorization_code"
            };

            return await PostGoogleTokenAsync(form, "Google token exchange failed.", cancellationToken);
        }

        private async Task<OAuthTokenResponse> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken)
        {
            var (clientId, clientSecret) = GetGoogleCredentials();
            var form = new Dictionary<string, string>
            {
                ["refresh_token"] = refreshToken,
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["grant_type"] = "refresh_token"
            };

            return await PostGoogleTokenAsync(form, "Google token refresh failed.", cancellationToken);
        }

        private async Task<OAuthTokenResponse> PostGoogleTokenAsync(
            Dictionary<string, string> form,
            string failureMessage,
            CancellationToken cancellationToken)
        {
            var client = _httpClientFactory.CreateClient();
            var response = await client.PostAsync(
                "https://oauth2.googleapis.com/token",
                new FormUrlEncodedContent(form),
                cancellationToken);

            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new OAuthTokenExchangeException((int)response.StatusCode, failureMessage, payload);
            }

            return OAuthTokenResponse.Parse(payload);
        }

        private (string ClientId, string ClientSecret) GetGoogleCredentials()
        {
            var clientId = _configuration["Integrations:Google:ClientId"];
            var clientSecret = _configuration["Integrations:Google:ClientSecret"];
            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            {
                throw new InvalidOperationException("Google OAuth is not configured.");
            }

            return (clientId, clientSecret);
        }

        private string? GetPrincipalId()
        {
            return User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                   ?? User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                   ?? User.FindFirst("clientId")?.Value;
        }

        private static string BuildConnectionId(string target, string principalId)
        {
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(principalId))).ToLowerInvariant()[..32];
            return $"google-oauth:{target}:{hash}";
        }

        private static string ResolveRedirectUri(string? configured, string fallbackPath, string fallbackBaseUrl)
        {
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured.Trim();
            }

            return $"{fallbackBaseUrl.TrimEnd('/')}{fallbackPath}";
        }

    }

    internal sealed class OAuthTokenResponse
    {
        public string? AccessToken { get; set; }
        public string? RefreshToken { get; set; }
        public string? TokenType { get; set; }
        public string? Scope { get; set; }
        public int? ExpiresIn { get; set; }
        public string? IdToken { get; set; }

        public static OAuthTokenResponse Parse(string payload)
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            return new OAuthTokenResponse
            {
                AccessToken = GetString(root, "access_token"),
                RefreshToken = GetString(root, "refresh_token"),
                TokenType = GetString(root, "token_type"),
                Scope = GetString(root, "scope"),
                ExpiresIn = GetInt(root, "expires_in"),
                IdToken = GetString(root, "id_token")
            };
        }

        private static string? GetString(JsonElement root, string name)
        {
            return root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }

        private static int? GetInt(JsonElement root, string name)
        {
            return root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
                ? value.GetInt32()
                : null;
        }
    }

    internal sealed class OAuthTokenExchangeException : Exception
    {
        public OAuthTokenExchangeException(int statusCode, string message, string detail)
            : base(message)
        {
            StatusCode = statusCode;
            Detail = detail;
        }

        public int StatusCode { get; }
        public string Detail { get; }
    }
}
