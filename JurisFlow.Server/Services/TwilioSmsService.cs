using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace JurisFlow.Server.Services
{
    public record TwilioSendResult(
        bool Success,
        string? MessageSid,
        string Status,
        string? ErrorCode,
        string? ErrorMessage);

    public class TwilioSmsService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<TwilioSmsService> _logger;

        public TwilioSmsService(
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ILogger<TwilioSmsService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _logger = logger;
        }

        public bool IsEnabled => _configuration.GetValue("Twilio:Enabled", false);
        public string? FromNumber => _configuration["Twilio:FromNumber"];

        public async Task<TwilioSendResult> SendAsync(string toNumber, string body, CancellationToken cancellationToken = default)
        {
            if (!IsEnabled)
            {
                return new TwilioSendResult(
                    Success: true,
                    MessageSid: null,
                    Status: "Sent",
                    ErrorCode: null,
                    ErrorMessage: null);
            }

            var accountSid = _configuration["Twilio:AccountSid"];
            var authToken = _configuration["Twilio:AuthToken"];
            var fromNumber = _configuration["Twilio:FromNumber"];

            if (string.IsNullOrWhiteSpace(accountSid) ||
                string.IsNullOrWhiteSpace(authToken) ||
                string.IsNullOrWhiteSpace(fromNumber))
            {
                return new TwilioSendResult(
                    Success: false,
                    MessageSid: null,
                    Status: "Failed",
                    ErrorCode: "TwilioNotConfigured",
                    ErrorMessage: "Twilio is enabled but AccountSid/AuthToken/FromNumber is missing.");
            }

            var endpoint = $"https://api.twilio.com/2010-04-01/Accounts/{accountSid}/Messages.json";
            var form = new Dictionary<string, string>
            {
                ["To"] = toNumber,
                ["From"] = fromNumber,
                ["Body"] = body
            };

            var statusCallbackUrl = _configuration["Twilio:StatusCallbackUrl"];
            if (!string.IsNullOrWhiteSpace(statusCallbackUrl))
            {
                form["StatusCallback"] = statusCallbackUrl;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new FormUrlEncodedContent(form)
            };
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.ASCII.GetBytes($"{accountSid}:{authToken}")));

            var client = _httpClientFactory.CreateClient();
            var response = await client.SendAsync(request, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var (errorCode, errorMessage) = ParseTwilioError(payload);
                return new TwilioSendResult(
                    Success: false,
                    MessageSid: null,
                    Status: "Failed",
                    ErrorCode: errorCode,
                    ErrorMessage: errorMessage ?? $"Twilio send failed with {(int)response.StatusCode}.");
            }

            try
            {
                using var doc = JsonDocument.Parse(payload);
                var root = doc.RootElement;
                var status = NormalizeProviderStatus(GetString(root, "status"));
                return new TwilioSendResult(
                    Success: true,
                    MessageSid: GetString(root, "sid"),
                    Status: status,
                    ErrorCode: GetString(root, "error_code"),
                    ErrorMessage: GetString(root, "error_message"));
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Twilio response could not be parsed. Payload: {Payload}", payload);
                return new TwilioSendResult(
                    Success: true,
                    MessageSid: null,
                    Status: "Sent",
                    ErrorCode: null,
                    ErrorMessage: null);
            }
        }

        public async Task<bool> IsWebhookSignatureValidAsync(HttpRequest request)
        {
            if (!IsEnabled)
            {
                return true;
            }

            var validateSignature = _configuration.GetValue("Twilio:ValidateWebhookSignature", false);
            if (!validateSignature)
            {
                return true;
            }

            var authToken = _configuration["Twilio:AuthToken"];
            if (string.IsNullOrWhiteSpace(authToken))
            {
                return false;
            }

            if (!request.Headers.TryGetValue("X-Twilio-Signature", out var signatureValues))
            {
                return false;
            }

            var providedSignature = signatureValues.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(providedSignature))
            {
                return false;
            }

            var requestUrl = _configuration["Twilio:WebhookUrl"];
            if (string.IsNullOrWhiteSpace(requestUrl))
            {
                requestUrl = $"{request.Scheme}://{request.Host}{request.PathBase}{request.Path}{request.QueryString}";
            }

            if (!request.HasFormContentType)
            {
                return false;
            }

            var form = await request.ReadFormAsync();
            var sortedKeys = form.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
            var signatureBase = new StringBuilder(requestUrl);
            foreach (var key in sortedKeys)
            {
                signatureBase.Append(key);
                signatureBase.Append(form[key].ToString());
            }

            using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(authToken));
            var computedBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(signatureBase.ToString()));
            var computedSignature = Convert.ToBase64String(computedBytes);

            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(providedSignature),
                Encoding.UTF8.GetBytes(computedSignature));
        }

        public static string NormalizeProviderStatus(string? status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return "Sent";
            }

            return status.Trim().ToLowerInvariant() switch
            {
                "queued" => "Queued",
                "accepted" => "Queued",
                "sending" => "Queued",
                "sent" => "Sent",
                "delivered" => "Delivered",
                "read" => "Delivered",
                "received" => "Received",
                "inbound" => "Received",
                "undelivered" => "Failed",
                "failed" => "Failed",
                _ => "Sent"
            };
        }

        private static string? GetString(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out var value))
            {
                return null;
            }

            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                _ => null
            };
        }

        private static (string? errorCode, string? errorMessage) ParseTwilioError(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
            {
                return (null, null);
            }

            try
            {
                using var doc = JsonDocument.Parse(payload);
                var root = doc.RootElement;
                var code = GetString(root, "code");
                var message = GetString(root, "message") ?? GetString(root, "detail");
                return (code, message);
            }
            catch
            {
                return (null, payload);
            }
        }
    }
}
