using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using JurisFlow.Server.Services;

namespace JurisFlow.Server.Controllers
{
    [Route("api/public/subscriptions")]
    [ApiController]
    public class PublicSubscriptionsController : ControllerBase
    {
        private readonly LemonSqueezyCheckoutService _checkoutService;

        public PublicSubscriptionsController(LemonSqueezyCheckoutService checkoutService)
        {
            _checkoutService = checkoutService;
        }

        [AllowAnonymous]
        [EnableRateLimiting("AuthLogin")]
        [HttpGet("plans")]
        public ActionResult<IReadOnlyList<PublicSubscriptionPlan>> GetPlans()
        {
            return Ok(_checkoutService.GetPlans());
        }

        [AllowAnonymous]
        [EnableRateLimiting("AuthLogin")]
        [HttpPost("checkout")]
        public async Task<IActionResult> CreateCheckout(
            [FromBody] PublicSubscriptionCheckoutRequest? request,
            CancellationToken cancellationToken = default)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.PlanId))
            {
                return BadRequest(new { message = "PlanId is required." });
            }

            try
            {
                var checkout = await _checkoutService.CreateCheckoutAsync(
                    request.PlanId,
                    request.Email,
                    request.FullName,
                    request.FirmCode,
                    cancellationToken);

                return Ok(new
                {
                    planId = checkout.PlanId,
                    planName = checkout.PlanName,
                    priceUsd = checkout.PriceUsd,
                    geminiEnabled = checkout.GeminiEnabled,
                    checkoutUrl = checkout.CheckoutUrl
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = ex.Message });
            }
        }
    }

    public sealed class PublicSubscriptionCheckoutRequest
    {
        public string PlanId { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string? FullName { get; set; }
        public string? FirmCode { get; set; }
    }
}
