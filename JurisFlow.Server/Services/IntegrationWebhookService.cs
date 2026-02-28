using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace JurisFlow.Server.Services
{
    public sealed class IntegrationWebhookValidationResult
    {
        public bool Success { get; init; }
        public string EventId { get; init; } = string.Empty;
        public bool SignatureValidated { get; init; }
        public string? ErrorMessage { get; init; }
    }

    public sealed class IntegrationWebhookService
    {
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<IntegrationWebhookService> _logger;

        public IntegrationWebhookService(
            IConfiguration configuration,
            IWebHostEnvironment environment,
            ILogger<IntegrationWebhookService> logger)
        {
            _configuration = configuration;
            _environment = environment;
            _logger = logger;
        }

        public bool SupportsProvider(string providerKey)
        {
            return IntegrationProviderCatalog.SupportsWebhook(providerKey);
        }

        public bool IsWebhookFirstProvider(string providerKey)
        {
            return IntegrationProviderCatalog.IsWebhookFirst(providerKey);
        }

        public int ResolveFallbackPollingMinutes(string providerKey, int defaultPollingIntervalMinutes)
        {
            return IntegrationProviderCatalog.ResolvePollingIntervalMinutes(providerKey, defaultPollingIntervalMinutes);
        }

        public IntegrationWebhookValidationResult Validate(
            string providerKey,
            HttpRequest request,
            string payload)
        {
            var normalizedProviderKey = providerKey.Trim().ToLowerInvariant();
            if (!SupportsProvider(normalizedProviderKey))
            {
                return new IntegrationWebhookValidationResult
                {
                    Success = false,
                    EventId = BuildEventId(normalizedProviderKey, request, payload),
                    ErrorMessage = "Webhook is not supported for this provider."
                };
            }

            return normalizedProviderKey switch
            {
                IntegrationProviderKeys.QuickBooksOnline => ValidateQuickBooks(request, payload),
                IntegrationProviderKeys.Xero => ValidateXero(request, payload),
                IntegrationProviderKeys.MicrosoftOutlookMail => ValidateOutlookMail(request, payload),
                IntegrationProviderKeys.GoogleGmail => ValidateGoogleGmail(request, payload),
                _ => ValidateGenericSharedSecret(normalizedProviderKey, request, payload)
            };
        }

        private IntegrationWebhookValidationResult ValidateQuickBooks(HttpRequest request, string payload)
        {
            return ValidateHmacSignature(
                request,
                payload,
                providerKey: IntegrationProviderKeys.QuickBooksOnline,
                secretConfigKey: "Integrations:QuickBooks:WebhookVerifierToken",
                signatureHeader: "intuit-signature",
                eventIdHeaders: new[] { "intuit-delivery-id", "intuit-t-id", "x-request-id" });
        }

        private IntegrationWebhookValidationResult ValidateXero(HttpRequest request, string payload)
        {
            return ValidateHmacSignature(
                request,
                payload,
                providerKey: IntegrationProviderKeys.Xero,
                secretConfigKey: "Integrations:Xero:WebhookSigningKey",
                signatureHeader: "x-xero-signature",
                eventIdHeaders: new[] { "x-request-id", "x-xero-event-id" });
        }

        private IntegrationWebhookValidationResult ValidateOutlookMail(HttpRequest request, string payload)
        {
            var configuredClientState = _configuration["Integrations:Outlook:WebhookClientState"]?.Trim();
            var eventId = BuildEventId(
                IntegrationProviderKeys.MicrosoftOutlookMail,
                request,
                payload,
                preferredHeaderNames: new[] { "x-ms-client-request-id", "x-request-id" });

            if (string.IsNullOrWhiteSpace(configuredClientState))
            {
                if (UnsignedWebhookAllowed(IntegrationProviderKeys.MicrosoftOutlookMail))
                {
                    return new IntegrationWebhookValidationResult
                    {
                        Success = true,
                        EventId = eventId,
                        SignatureValidated = false
                    };
                }

                return new IntegrationWebhookValidationResult
                {
                    Success = false,
                    EventId = eventId,
                    ErrorMessage = "Outlook webhook client state is not configured."
                };
            }

            try
            {
                using var doc = JsonDocument.Parse(payload);
                if (!doc.RootElement.TryGetProperty("value", out var notifications) ||
                    notifications.ValueKind != JsonValueKind.Array)
                {
                    return new IntegrationWebhookValidationResult
                    {
                        Success = false,
                        EventId = eventId,
                        ErrorMessage = "Outlook webhook payload is malformed."
                    };
                }

                foreach (var notification in notifications.EnumerateArray())
                {
                    var clientState = notification.TryGetProperty("clientState", out var clientStateNode) &&
                                      clientStateNode.ValueKind == JsonValueKind.String
                        ? clientStateNode.GetString()
                        : null;

                    if (!string.Equals(clientState?.Trim(), configuredClientState, StringComparison.Ordinal))
                    {
                        return new IntegrationWebhookValidationResult
                        {
                            Success = false,
                            EventId = eventId,
                            ErrorMessage = "Outlook webhook client state mismatch."
                        };
                    }
                }

                return new IntegrationWebhookValidationResult
                {
                    Success = true,
                    EventId = eventId,
                    SignatureValidated = true
                };
            }
            catch (JsonException)
            {
                return new IntegrationWebhookValidationResult
                {
                    Success = false,
                    EventId = eventId,
                    ErrorMessage = "Outlook webhook payload is not valid JSON."
                };
            }
        }

        private IntegrationWebhookValidationResult ValidateGoogleGmail(HttpRequest request, string payload)
        {
            var configuredToken = _configuration["Integrations:Google:GmailWebhookChannelToken"]?.Trim();
            var eventId = BuildEventId(
                IntegrationProviderKeys.GoogleGmail,
                request,
                payload,
                preferredHeaderNames: new[] { "x-goog-message-number", "x-request-id" });
            var receivedToken = GetHeader(request, "x-goog-channel-token");

            if (string.IsNullOrWhiteSpace(configuredToken))
            {
                if (UnsignedWebhookAllowed(IntegrationProviderKeys.GoogleGmail))
                {
                    return new IntegrationWebhookValidationResult
                    {
                        Success = true,
                        EventId = eventId,
                        SignatureValidated = false
                    };
                }

                return new IntegrationWebhookValidationResult
                {
                    Success = false,
                    EventId = eventId,
                    ErrorMessage = "Google Gmail webhook channel token is not configured."
                };
            }

            if (!string.Equals(receivedToken?.Trim(), configuredToken, StringComparison.Ordinal))
            {
                return new IntegrationWebhookValidationResult
                {
                    Success = false,
                    EventId = eventId,
                    ErrorMessage = "Google Gmail webhook channel token mismatch."
                };
            }

            return new IntegrationWebhookValidationResult
            {
                Success = true,
                EventId = eventId,
                SignatureValidated = true
            };
        }

        private IntegrationWebhookValidationResult ValidateGenericSharedSecret(
            string providerKey,
            HttpRequest request,
            string payload)
        {
            var secretConfigKey = $"Integrations:Webhooks:{providerKey}:SharedSecret";
            var configuredSecret = _configuration[secretConfigKey]?.Trim();
            var eventId = BuildEventId(
                providerKey,
                request,
                payload,
                preferredHeaderNames: new[] { "x-request-id", "x-event-id" });
            var providedSecret = GetHeader(request, "x-integration-webhook-secret");

            if (string.IsNullOrWhiteSpace(configuredSecret))
            {
                if (UnsignedWebhookAllowed(providerKey))
                {
                    return new IntegrationWebhookValidationResult
                    {
                        Success = true,
                        EventId = eventId,
                        SignatureValidated = false
                    };
                }

                return new IntegrationWebhookValidationResult
                {
                    Success = false,
                    EventId = eventId,
                    ErrorMessage = $"Webhook shared secret is not configured for provider {providerKey}."
                };
            }

            if (!CryptographicEquals(configuredSecret, providedSecret))
            {
                return new IntegrationWebhookValidationResult
                {
                    Success = false,
                    EventId = eventId,
                    ErrorMessage = "Webhook shared secret mismatch."
                };
            }

            return new IntegrationWebhookValidationResult
            {
                Success = true,
                EventId = eventId,
                SignatureValidated = true
            };
        }

        private IntegrationWebhookValidationResult ValidateHmacSignature(
            HttpRequest request,
            string payload,
            string providerKey,
            string secretConfigKey,
            string signatureHeader,
            IReadOnlyCollection<string> eventIdHeaders)
        {
            var eventId = BuildEventId(providerKey, request, payload, eventIdHeaders);
            var configuredSecret = _configuration[secretConfigKey]?.Trim();
            var providedSignature = GetHeader(request, signatureHeader);

            if (string.IsNullOrWhiteSpace(configuredSecret))
            {
                if (UnsignedWebhookAllowed(providerKey))
                {
                    return new IntegrationWebhookValidationResult
                    {
                        Success = true,
                        EventId = eventId,
                        SignatureValidated = false
                    };
                }

                return new IntegrationWebhookValidationResult
                {
                    Success = false,
                    EventId = eventId,
                    ErrorMessage = $"Webhook secret is not configured for provider {providerKey}."
                };
            }

            if (string.IsNullOrWhiteSpace(providedSignature))
            {
                return new IntegrationWebhookValidationResult
                {
                    Success = false,
                    EventId = eventId,
                    ErrorMessage = "Webhook signature header is missing."
                };
            }

            var signatureValid = VerifyHmacSha256Base64(
                payload,
                configuredSecret,
                providedSignature);
            if (!signatureValid)
            {
                return new IntegrationWebhookValidationResult
                {
                    Success = false,
                    EventId = eventId,
                    ErrorMessage = "Webhook signature validation failed."
                };
            }

            return new IntegrationWebhookValidationResult
            {
                Success = true,
                EventId = eventId,
                SignatureValidated = true
            };
        }

        private bool UnsignedWebhookAllowed(string providerKey)
        {
            var allowUnsigned = _configuration.GetValue(
                "Integrations:Webhooks:AllowUnsigned",
                !_environment.IsProduction());

            if (allowUnsigned)
            {
                _logger.LogWarning(
                    "Unsigned integration webhook accepted. ProviderKey={ProviderKey}",
                    providerKey);
            }

            return allowUnsigned;
        }

        private static bool VerifyHmacSha256Base64(string payload, string secret, string providedSignature)
        {
            var payloadBytes = Encoding.UTF8.GetBytes(payload);
            var secretBytes = Encoding.UTF8.GetBytes(secret);
            using var hmac = new HMACSHA256(secretBytes);
            var computedSignature = hmac.ComputeHash(payloadBytes);

            try
            {
                var providedBytes = Convert.FromBase64String(providedSignature.Trim());
                return providedBytes.Length == computedSignature.Length &&
                       CryptographicOperations.FixedTimeEquals(providedBytes, computedSignature);
            }
            catch (FormatException)
            {
                return false;
            }
        }

        private static string BuildEventId(
            string providerKey,
            HttpRequest request,
            string payload,
            IReadOnlyCollection<string>? preferredHeaderNames = null)
        {
            if (preferredHeaderNames != null)
            {
                foreach (var headerName in preferredHeaderNames)
                {
                    var candidate = GetHeader(request, headerName);
                    if (!string.IsNullOrWhiteSpace(candidate))
                    {
                        return $"{providerKey}:{candidate.Trim()}";
                    }
                }
            }

            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload ?? string.Empty));
            var digest = Convert.ToHexString(hash).ToLowerInvariant();
            return $"{providerKey}:{digest[..32]}";
        }

        private static string? GetHeader(HttpRequest request, string headerName)
        {
            return request.Headers.TryGetValue(headerName, out var values)
                ? values.FirstOrDefault()
                : null;
        }

        private static bool CryptographicEquals(string expected, string? actual)
        {
            if (actual == null)
            {
                return false;
            }

            var expectedBytes = Encoding.UTF8.GetBytes(expected.Trim());
            var actualBytes = Encoding.UTF8.GetBytes(actual.Trim());
            return expectedBytes.Length == actualBytes.Length &&
                   CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
        }
    }
}
