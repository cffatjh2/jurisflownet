using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace JurisFlow.Server.Services
{
    public sealed class LemonSqueezyCheckoutService
    {
        private const string CheckoutApiUrl = "https://api.lemonsqueezy.com/v1/checkouts";

        private static readonly IReadOnlyDictionary<string, PlanDefinition> Plans = new Dictionary<string, PlanDefinition>(StringComparer.Ordinal)
        {
            ["starter-39"] = new(
                "starter-39",
                "Starter",
                39m,
                false,
                "Gemini erisimi yok.",
                "LemonSqueezy:Starter39VariantId",
                "LemonSqueezy:Starter39CheckoutUrl"),
            ["all-inclusive-59"] = new(
                "all-inclusive-59",
                "All Inclusive",
                59m,
                true,
                "Gemini dahil tum ozellikler.",
                "LemonSqueezy:AllInclusive59VariantId",
                "LemonSqueezy:AllInclusive59CheckoutUrl")
        };

        private static readonly IReadOnlyDictionary<string, string> PlanAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["starter"] = "starter-39",
            ["39"] = "starter-39",
            ["starter-39"] = "starter-39",
            ["basic"] = "starter-39",
            ["all-inclusive"] = "all-inclusive-59",
            ["all-inclusive-59"] = "all-inclusive-59",
            ["pro"] = "all-inclusive-59",
            ["59"] = "all-inclusive-59",
            ["premium"] = "all-inclusive-59"
        };

        private readonly IConfiguration _configuration;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<LemonSqueezyCheckoutService> _logger;

        public LemonSqueezyCheckoutService(
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory,
            ILogger<LemonSqueezyCheckoutService> logger)
        {
            _configuration = configuration;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public IReadOnlyList<PublicSubscriptionPlan> GetPlans()
        {
            return Plans.Values
                .OrderBy(plan => plan.PriceUsd)
                .Select(plan => new PublicSubscriptionPlan(
                    plan.Id,
                    plan.Name,
                    plan.PriceUsd,
                    plan.GeminiEnabled,
                    plan.Description))
                .ToList();
        }

        public async Task<PublicSubscriptionCheckoutResult> CreateCheckoutAsync(
            string planId,
            string? email,
            string? fullName,
            string? firmCode,
            CancellationToken cancellationToken = default)
        {
            var plan = ResolvePlan(planId);
            if (plan == null)
            {
                throw new ArgumentException("PlanId is invalid. Allowed: starter-39, all-inclusive-59.");
            }

            var directCheckoutUrl = _configuration[plan.DirectCheckoutUrlConfigKey];
            if (!string.IsNullOrWhiteSpace(directCheckoutUrl))
            {
                return new PublicSubscriptionCheckoutResult(
                    plan.Id,
                    plan.Name,
                    plan.PriceUsd,
                    plan.GeminiEnabled,
                    directCheckoutUrl.Trim());
            }

            var apiKey = _configuration["LemonSqueezy:ApiKey"];
            var storeId = _configuration["LemonSqueezy:StoreId"];
            var variantId = _configuration[plan.VariantIdConfigKey];

            if (string.IsNullOrWhiteSpace(apiKey) ||
                string.IsNullOrWhiteSpace(storeId) ||
                string.IsNullOrWhiteSpace(variantId))
            {
                throw new InvalidOperationException(
                    $"LemonSqueezy is not configured for {plan.Id}. Configure either {plan.DirectCheckoutUrlConfigKey} or set LemonSqueezy:ApiKey, LemonSqueezy:StoreId and {plan.VariantIdConfigKey}.");
            }

            var checkoutUrl = await CreateCheckoutViaApiAsync(
                plan,
                apiKey.Trim(),
                storeId.Trim(),
                variantId.Trim(),
                email,
                fullName,
                firmCode,
                cancellationToken);

            return new PublicSubscriptionCheckoutResult(
                plan.Id,
                plan.Name,
                plan.PriceUsd,
                plan.GeminiEnabled,
                checkoutUrl);
        }

        private async Task<string> CreateCheckoutViaApiAsync(
            PlanDefinition plan,
            string apiKey,
            string storeId,
            string variantId,
            string? email,
            string? fullName,
            string? firmCode,
            CancellationToken cancellationToken)
        {
            var payload = BuildPayload(plan, storeId, variantId, email, fullName, firmCode);

            using var request = new HttpRequestMessage(HttpMethod.Post, CheckoutApiUrl)
            {
                Content = JsonContent.Create(payload)
            };

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.api+json"));
            request.Content!.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.api+json");

            var client = _httpClientFactory.CreateClient();
            using var response = await client.SendAsync(request, cancellationToken);
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "LemonSqueezy checkout creation failed. Status={StatusCode}, Response={Response}",
                    (int)response.StatusCode,
                    responseText);

                throw new InvalidOperationException("Failed to create LemonSqueezy checkout.");
            }

            try
            {
                using var document = JsonDocument.Parse(responseText);
                if (document.RootElement.TryGetProperty("data", out var dataElement) &&
                    dataElement.TryGetProperty("attributes", out var attributesElement) &&
                    attributesElement.TryGetProperty("url", out var urlElement) &&
                    urlElement.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(urlElement.GetString()))
                {
                    return urlElement.GetString()!;
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Unable to parse LemonSqueezy checkout response.");
            }

            throw new InvalidOperationException("LemonSqueezy response did not contain a checkout URL.");
        }

        private object BuildPayload(
            PlanDefinition plan,
            string storeId,
            string variantId,
            string? email,
            string? fullName,
            string? firmCode)
        {
            var normalizedEmail = NormalizeOptional(email);
            var normalizedFullName = NormalizeOptional(fullName);
            var normalizedFirmCode = NormalizeOptional(firmCode);

            var checkoutData = new Dictionary<string, object?>();
            if (!string.IsNullOrWhiteSpace(normalizedEmail))
            {
                checkoutData["email"] = normalizedEmail;
            }

            if (!string.IsNullOrWhiteSpace(normalizedFullName))
            {
                checkoutData["name"] = normalizedFullName;
            }

            var customData = new Dictionary<string, string>
            {
                ["plan"] = plan.Id,
                ["price_usd"] = plan.PriceUsd.ToString("0.##", CultureInfo.InvariantCulture)
            };

            if (!string.IsNullOrWhiteSpace(normalizedFirmCode))
            {
                customData["firm_code"] = normalizedFirmCode;
            }

            checkoutData["custom"] = customData;

            var attributes = new Dictionary<string, object?>
            {
                ["checkout_data"] = checkoutData,
                ["checkout_options"] = new
                {
                    embed = false,
                    media = true,
                    logo = true
                },
                ["test_mode"] = _configuration.GetValue("LemonSqueezy:TestMode", false)
            };

            var successUrl = NormalizeOptional(_configuration["LemonSqueezy:SuccessUrl"]);
            if (!string.IsNullOrWhiteSpace(successUrl))
            {
                attributes["product_options"] = new
                {
                    redirect_url = AppendPlanQuery(successUrl, plan.Id)
                };
            }

            return new
            {
                data = new
                {
                    type = "checkouts",
                    attributes,
                    relationships = new
                    {
                        store = new
                        {
                            data = new
                            {
                                type = "stores",
                                id = storeId
                            }
                        },
                        variant = new
                        {
                            data = new
                            {
                                type = "variants",
                                id = variantId
                            }
                        }
                    }
                }
            };
        }

        private static PlanDefinition? ResolvePlan(string? rawPlanId)
        {
            if (string.IsNullOrWhiteSpace(rawPlanId))
            {
                return null;
            }

            var normalized = rawPlanId.Trim().ToLowerInvariant();
            if (PlanAliases.TryGetValue(normalized, out var canonicalId) &&
                Plans.TryGetValue(canonicalId, out var aliasedPlan))
            {
                return aliasedPlan;
            }

            return Plans.TryGetValue(normalized, out var directPlan)
                ? directPlan
                : null;
        }

        private static string? NormalizeOptional(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var normalized = value.Trim();
            return normalized.Length == 0 ? null : normalized;
        }

        private static string AppendPlanQuery(string baseUrl, string planId)
        {
            var separator = baseUrl.Contains('?') ? "&" : "?";
            return $"{baseUrl}{separator}plan={Uri.EscapeDataString(planId)}";
        }

        private sealed record PlanDefinition(
            string Id,
            string Name,
            decimal PriceUsd,
            bool GeminiEnabled,
            string Description,
            string VariantIdConfigKey,
            string DirectCheckoutUrlConfigKey);
    }

    public sealed record PublicSubscriptionPlan(
        string Id,
        string Name,
        decimal PriceUsd,
        bool GeminiEnabled,
        string Description);

    public sealed record PublicSubscriptionCheckoutResult(
        string PlanId,
        string PlanName,
        decimal PriceUsd,
        bool GeminiEnabled,
        string CheckoutUrl);
}
