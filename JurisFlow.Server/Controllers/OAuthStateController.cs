using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using JurisFlow.Server.DTOs;
using JurisFlow.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JurisFlow.Server.Controllers
{
    [Route("api/oauth")]
    [ApiController]
    [Authorize(Policy = "StaffOrClient")]
    public class OAuthStateController : ControllerBase
    {
        private readonly OAuthStateService _stateService;

        public OAuthStateController(OAuthStateService stateService)
        {
            _stateService = stateService;
        }

        [HttpPost("state")]
        public IActionResult CreateOAuthState([FromBody] OAuthStateRequestDto dto)
        {
            var userId = GetPrincipalId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Unauthorized();
            }

            var provider = _stateService.NormalizeProvider(dto.Provider);
            if (provider == null)
            {
                return BadRequest(new { message = "Unsupported OAuth provider." });
            }

            var target = _stateService.NormalizeTarget(provider, dto.Target);
            if (target == null)
            {
                return BadRequest(new { message = "Unsupported OAuth target." });
            }

            var payload = new JurisFlow.Server.Services.OAuthStatePayload
            {
                Provider = provider,
                UserId = userId,
                Target = target,
                ReturnPath = _stateService.NormalizeReturnPath(dto.ReturnPath),
                IssuedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16))
            };

            return Ok(new { state = _stateService.Sign(payload) });
        }

        private string? GetPrincipalId()
        {
            return User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                   ?? User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                   ?? User.FindFirst("clientId")?.Value;
        }
    }
}
