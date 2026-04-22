using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace JurisFlow.Server.Services
{
    public sealed class OAuthStateService
    {
        private const int OAuthStateMaxAgeMinutes = 10;
        private readonly IConfiguration _configuration;
        private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

        public OAuthStateService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public string? NormalizeProvider(string? provider)
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

        public string? NormalizeTarget(string provider, string? target)
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

        public string NormalizeReturnPath(string? returnPath)
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

        public string Sign(OAuthStatePayload payload)
        {
            var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload, _jsonOptions);
            var encodedPayload = ToBase64Url(payloadBytes);
            var signature = ComputeSignature(encodedPayload);
            var encodedSignature = ToBase64Url(signature);
            return $"{encodedPayload}.{encodedSignature}";
        }

        public bool TryValidate(
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
    }

    public sealed class OAuthStatePayload
    {
        public string Provider { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string Target { get; set; } = string.Empty;
        public string ReturnPath { get; set; } = "/";
        public long IssuedAtUnix { get; set; }
        public string Nonce { get; set; } = string.Empty;
    }
}
