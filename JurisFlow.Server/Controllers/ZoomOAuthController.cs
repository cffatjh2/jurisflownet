using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using JurisFlow.Server.DTOs;
using JurisFlow.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JurisFlow.Server.Controllers
{
    [Route("api/zoom/oauth")]
    [ApiController]
    [Authorize(Policy = "StaffOrClient")]
    public class ZoomOAuthController : ControllerBase
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly OAuthStateService _stateService;

        public ZoomOAuthController(
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            OAuthStateService stateService)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _stateService = stateService;
        }

        [HttpPost]
        public async Task<IActionResult> ExchangeZoomCode([FromBody] OAuthCodeDto dto)
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

            if (!_stateService.TryValidate(dto.State, "zoom", principalId, out var statePayload, out var stateError))
            {
                return BadRequest(new { message = stateError ?? "OAuth state is invalid." });
            }

            var clientId = _configuration["Integrations:Zoom:ClientId"];
            var clientSecret = _configuration["Integrations:Zoom:ClientSecret"];
            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            {
                return StatusCode(500, new { message = "Zoom OAuth is not configured." });
            }

            var redirectUri = ResolveRedirectUri(
                _configuration["Integrations:Zoom:RedirectUri"],
                "/auth/zoom/callback",
                "http://localhost:3000");

            var request = new HttpRequestMessage(HttpMethod.Post, "https://zoom.us/oauth/token");
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}")));
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = dto.Code,
                ["redirect_uri"] = redirectUri
            });

            var client = _httpClientFactory.CreateClient();
            var response = await client.SendAsync(request, HttpContext.RequestAborted);
            var payload = await response.Content.ReadAsStringAsync(HttpContext.RequestAborted);
            if (!response.IsSuccessStatusCode)
            {
                return StatusCode((int)response.StatusCode, new { message = "Zoom token exchange failed.", detail = payload });
            }

            var token = OAuthTokenResponse.Parse(payload);
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

        private string? GetPrincipalId()
        {
            return User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                   ?? User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                   ?? User.FindFirst("clientId")?.Value;
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
