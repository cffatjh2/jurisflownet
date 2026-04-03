using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace JurisFlow.Server.Controllers
{
    [Route("api")]
    [ApiController]
    [Authorize(Policy = "StaffOrClient")]
    public class OAuthController : ControllerBase
    {
        private const int OAuthStateMaxAgeMinutes = 10;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

        public OAuthController(IHttpClientFactory httpClientFactory, IConfiguration configuration)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
        }

        public class OAuthCodeDto
        {
            public string Code { get; set; } = string.Empty;
            public string State { get; set; } = string.Empty;
        }

        public class OAuthStateRequestDto
        {
            public string Provider { get; set; } = string.Empty;
            public string? Target { get; set; }
            public string? ReturnPath { get; set; }
        }

        public class OAuthRefreshDto
        {
            public string RefreshToken { get; set; } = string.Empty;
            public string? Target { get; set; }
        }

        private sealed class OAuthStatePayload
        {
            public string Provider { get; set; } = string.Empty;
            public string UserId { get; set; } = string.Empty;
            public string Target { get; set; } = string.Empty;
            public string ReturnPath { get; set; } = "/";
            public long IssuedAtUnix { get; set; }
            public string Nonce { get; set; } = string.Empty;
        }

        private sealed class OAuthTokenResponse
        {
            public string? AccessToken { get; set; }
            public string? RefreshToken { get; set; }
            public string? TokenType { get; set; }
            public string? Scope { get; set; }
            public int? ExpiresIn { get; set; }
            public string? IdToken { get; set; }
        }

        [HttpPost("oauth/state")]
        public IActionResult CreateOAuthState([FromBody] OAuthStateRequestDto dto)
        {
            var userId = GetPrincipalId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Unauthorized();
            }

            var provider = NormalizeProvider(dto.Provider);
            if (provider == null)
            {
                return BadRequest(new { message = "Unsupported OAuth provider." });
            }

            var target = NormalizeTarget(provider, dto.Target);
            if (target == null)
            {
                return BadRequest(new { message = "Unsupported OAuth target." });
            }

            var returnPath = NormalizeReturnPath(dto.ReturnPath);
            var payload = new OAuthStatePayload
            {
                Provider = provider,
                UserId = userId,
                Target = target,
                ReturnPath = returnPath,
                IssuedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16))
            };

            var state = SignOAuthState(payload);
            return Ok(new
            {
                state
            });
        }

        [HttpPost("google/oauth")]
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

            if (!TryValidateOAuthState(dto.State, expectedProvider: "google", principalId, out var statePayload, out var stateError))
            {
                return BadRequest(new { message = stateError ?? "OAuth state is invalid." });
            }

            var clientId = _configuration["Integrations:Google:ClientId"];
            var clientSecret = _configuration["Integrations:Google:ClientSecret"];
            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            {
                return StatusCode(500, new { message = "Google OAuth is not configured." });
            }

            var redirectUri = ResolveRedirectUri(
                _configuration["Integrations:Google:RedirectUri"],
                "/auth/google/callback",
                "http://localhost:3000");

            var form = new Dictionary<string, string>
            {
                ["code"] = dto.Code,
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["redirect_uri"] = redirectUri,
                ["grant_type"] = "authorization_code"
            };

            var client = _httpClientFactory.CreateClient();
            var response = await client.PostAsync(
                "https://oauth2.googleapis.com/token",
                new FormUrlEncodedContent(form));

            var payload = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                return StatusCode((int)response.StatusCode, new { message = "Google token exchange failed.", detail = payload });
            }

            var token = ExtractTokenResponse(payload);
            return Ok(new
            {
                accessToken = token.AccessToken,
                refreshToken = token.RefreshToken,
                tokenType = token.TokenType,
                scope = token.Scope,
                expiresIn = token.ExpiresIn,
                idToken = token.IdToken,
                target = statePayload?.Target,
                returnPath = statePayload?.ReturnPath
            });
        }

        [HttpPost("google/oauth/refresh")]
        public async Task<IActionResult> RefreshGoogleToken([FromBody] OAuthRefreshDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.RefreshToken))
            {
                return BadRequest(new { message = "Refresh token is required." });
            }

            var principalId = GetPrincipalId();
            if (string.IsNullOrWhiteSpace(principalId))
            {
                return Unauthorized();
            }

            var target = NormalizeTarget("google", dto.Target);
            if (target == null)
            {
                return BadRequest(new { message = "Unsupported Google OAuth target." });
            }

            var clientId = _configuration["Integrations:Google:ClientId"];
            var clientSecret = _configuration["Integrations:Google:ClientSecret"];
            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            {
                return StatusCode(500, new { message = "Google OAuth is not configured." });
            }

            var form = new Dictionary<string, string>
            {
                ["refresh_token"] = dto.RefreshToken,
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["grant_type"] = "refresh_token"
            };

            var client = _httpClientFactory.CreateClient();
            var response = await client.PostAsync(
                "https://oauth2.googleapis.com/token",
                new FormUrlEncodedContent(form));

            var payload = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                return StatusCode((int)response.StatusCode, new { message = "Google token refresh failed.", detail = payload });
            }

            var token = ExtractTokenResponse(payload);
            if (string.IsNullOrWhiteSpace(token.AccessToken))
            {
                return StatusCode(502, new { message = "Google token refresh returned no access token." });
            }

            return Ok(new
            {
                accessToken = token.AccessToken,
                refreshToken = string.IsNullOrWhiteSpace(token.RefreshToken) ? dto.RefreshToken : token.RefreshToken,
                tokenType = token.TokenType,
                scope = token.Scope,
                expiresIn = token.ExpiresIn,
                idToken = token.IdToken,
                target
            });
        }

        [HttpPost("zoom/oauth")]
        public async Task<IActionResult> ExchangeZoomCode([FromBody] OAuthCodeDto dto)
        {
            var principalId = GetPrincipalId();
            if (string.IsNullOrWhiteSpace(principalId))
            {
                return Unauthorized();
            }

            if (!TryValidateOAuthState(dto.State, expectedProvider: "zoom", principalId, out var statePayload, out var stateError))
            {
                return BadRequest(new { message = stateError ?? "OAuth state is invalid." });
            }

            var clientId = _configuration["Integrations:Zoom:ClientId"];
            var clientSecret = _configuration["Integrations:Zoom:ClientSecret"];
            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            {
                return StatusCode(500, new { message = "Zoom OAuth is not configured." });
            }

            var accountId = _configuration["Integrations:Zoom:AccountId"];
            var redirectUri = ResolveRedirectUri(
                _configuration["Integrations:Zoom:RedirectUri"],
                "/auth/zoom/callback",
                "http://localhost:3000");

            var request = new HttpRequestMessage(HttpMethod.Post, "https://zoom.us/oauth/token");
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}")));

            if (!string.IsNullOrWhiteSpace(dto.Code))
            {
                request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "authorization_code",
                    ["code"] = dto.Code,
                    ["redirect_uri"] = redirectUri
                });
            }
            else if (!string.IsNullOrWhiteSpace(accountId))
            {
                request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "account_credentials",
                    ["account_id"] = accountId
                });
            }
            else
            {
                return BadRequest(new { message = "Authorization code is required." });
            }

            var client = _httpClientFactory.CreateClient();
            var response = await client.SendAsync(request);
            var payload = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                return StatusCode((int)response.StatusCode, new { message = "Zoom token exchange failed.", detail = payload });
            }

            var token = ExtractTokenResponse(payload);
            return Ok(new
            {
                accessToken = token.AccessToken,
                refreshToken = token.RefreshToken,
                tokenType = token.TokenType,
                scope = token.Scope,
                expiresIn = token.ExpiresIn,
                idToken = token.IdToken,
                target = statePayload?.Target,
                returnPath = statePayload?.ReturnPath
            });
        }

        private OAuthTokenResponse ExtractTokenResponse(string payload)
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

        private string? NormalizeProvider(string? provider)
        {
            if (string.IsNullOrWhiteSpace(provider))
            {
                return null;
            }

            var normalized = provider.Trim().ToLowerInvariant();
            return normalized switch
            {
                "google" => "google",
                "zoom" => "zoom",
                "microsoft" => "microsoft",
                _ => null
            };
        }

        private string? NormalizeTarget(string provider, string? target)
        {
            var normalized = (target ?? string.Empty).Trim().ToLowerInvariant();
            if (provider == "google")
            {
                return normalized switch
                {
                    "gmail" => "gmail",
                    "google-docs" => "google-docs",
                    "google-meet" => "google-meet",
                    _ => null
                };
            }

            if (provider == "zoom")
            {
                return normalized == "zoom" ? "zoom" : null;
            }

            if (provider == "microsoft")
            {
                return normalized == "microsoft-teams" ? "microsoft-teams" : null;
            }

            return null;
        }

        private static string NormalizeReturnPath(string? returnPath)
        {
            if (string.IsNullOrWhiteSpace(returnPath))
            {
                return "/";
            }

            var candidate = returnPath.Trim();
            if (candidate.Length > 200)
            {
                return "/";
            }

            if (!candidate.StartsWith("/", StringComparison.Ordinal) || candidate.StartsWith("//", StringComparison.Ordinal))
            {
                return "/";
            }

            return candidate;
        }

        private string SignOAuthState(OAuthStatePayload payload)
        {
            var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload, _jsonOptions);
            var encodedPayload = ToBase64Url(payloadBytes);
            var signature = ComputeSignature(encodedPayload);
            var encodedSignature = ToBase64Url(signature);
            return $"{encodedPayload}.{encodedSignature}";
        }

        private bool TryValidateOAuthState(
            string? state,
            string expectedProvider,
            string principalId,
            out OAuthStatePayload? payload,
            out string? error)
        {
            payload = null;
            error = null;

            if (string.IsNullOrWhiteSpace(state))
            {
                error = "OAuth state is required.";
                return false;
            }

            var parts = state.Split('.', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
            {
                error = "OAuth state is malformed.";
                return false;
            }

            byte[] signatureBytes;
            byte[] expectedSignatureBytes;
            try
            {
                signatureBytes = FromBase64Url(parts[1]);
                expectedSignatureBytes = ComputeSignature(parts[0]);
            }
            catch
            {
                error = "OAuth state signature is invalid.";
                return false;
            }

            if (signatureBytes.Length == 0 || signatureBytes.Length != expectedSignatureBytes.Length ||
                !CryptographicOperations.FixedTimeEquals(signatureBytes, expectedSignatureBytes))
            {
                error = "OAuth state signature mismatch.";
                return false;
            }

            OAuthStatePayload? decodedPayload;
            try
            {
                var payloadBytes = FromBase64Url(parts[0]);
                decodedPayload = JsonSerializer.Deserialize<OAuthStatePayload>(payloadBytes, _jsonOptions);
            }
            catch
            {
                error = "OAuth state payload is invalid.";
                return false;
            }

            if (decodedPayload == null)
            {
                error = "OAuth state payload is empty.";
                return false;
            }

            if (!string.Equals(decodedPayload.Provider, expectedProvider, StringComparison.Ordinal))
            {
                error = "OAuth provider does not match state.";
                return false;
            }

            if (!string.Equals(decodedPayload.UserId, principalId, StringComparison.Ordinal))
            {
                error = "OAuth state does not belong to current user.";
                return false;
            }

            var issuedAt = DateTimeOffset.FromUnixTimeSeconds(decodedPayload.IssuedAtUnix);
            var now = DateTimeOffset.UtcNow;
            if (issuedAt > now.AddMinutes(1) || now - issuedAt > TimeSpan.FromMinutes(OAuthStateMaxAgeMinutes))
            {
                error = "OAuth state has expired. Please retry authentication.";
                return false;
            }

            var normalizedTarget = NormalizeTarget(decodedPayload.Provider, decodedPayload.Target);
            if (normalizedTarget == null)
            {
                error = "OAuth state target is invalid.";
                return false;
            }

            decodedPayload.Target = normalizedTarget;
            decodedPayload.ReturnPath = NormalizeReturnPath(decodedPayload.ReturnPath);
            payload = decodedPayload;
            return true;
        }

        private byte[] ComputeSignature(string encodedPayload)
        {
            using var hmac = new HMACSHA256(GetOAuthStateKey());
            return hmac.ComputeHash(Encoding.UTF8.GetBytes(encodedPayload));
        }

        private byte[] GetOAuthStateKey()
        {
            var configured = _configuration["Security:OAuthStateKey"];
            if (!string.IsNullOrWhiteSpace(configured))
            {
                try
                {
                    var decoded = Convert.FromBase64String(configured.Trim());
                    if (decoded.Length >= 32)
                    {
                        return decoded;
                    }
                }
                catch
                {
                    // Fall back to deriving from configured string below.
                }

                return SHA256.HashData(Encoding.UTF8.GetBytes(configured.Trim()));
            }

            var jwtKey = _configuration["Jwt:Key"];
            if (string.IsNullOrWhiteSpace(jwtKey))
            {
                throw new InvalidOperationException("OAuth state signing key is not configured.");
            }

            return SHA256.HashData(Encoding.UTF8.GetBytes(jwtKey));
        }

        private static string ToBase64Url(byte[] value)
        {
            return Convert.ToBase64String(value)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private static byte[] FromBase64Url(string value)
        {
            var base64 = value.Replace('-', '+').Replace('_', '/');
            var padding = 4 - (base64.Length % 4);
            if (padding is > 0 and < 4)
            {
                base64 = base64 + new string('=', padding);
            }

            return Convert.FromBase64String(base64);
        }

        private string? GetPrincipalId()
        {
            return User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                   ?? User.FindFirst("sub")?.Value
                   ?? User.FindFirst("clientId")?.Value;
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

        private static string ResolveRedirectUri(string? configured, string fallbackPath, string fallbackBaseUrl)
        {
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured.Trim();
            }

            return $"{fallbackBaseUrl.TrimEnd('/')}{fallbackPath}";
        }
    }
}
