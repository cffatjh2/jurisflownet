using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace JurisFlow.Server.Services
{
    public sealed class IntegrationPiiMinimizationService
    {
        private static readonly HashSet<string> SensitiveHeaderTokens = new(StringComparer.OrdinalIgnoreCase)
        {
            "authorization",
            "cookie",
            "set-cookie",
            "signature",
            "token",
            "secret"
        };

        private static readonly HashSet<string> SensitiveFieldTokens = new(StringComparer.OrdinalIgnoreCase)
        {
            "email",
            "phone",
            "mobile",
            "ssn",
            "taxid",
            "tin",
            "ein",
            "dob",
            "birth",
            "address",
            "street",
            "city",
            "zip",
            "postal",
            "partyname",
            "clientname",
            "fullname",
            "firstname",
            "lastname",
            "middlename",
            "displayname",
            "name",
            "body",
            "html",
            "text",
            "content",
            "description",
            "notes",
            "token",
            "secret",
            "apikey",
            "apisecret",
            "accesstoken",
            "refreshtoken",
            "password"
        };

        private readonly IConfiguration _configuration;

        public IntegrationPiiMinimizationService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public string SanitizeWebhookHeadersForStorage(HttpRequest request)
        {
            var normalized = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var header in request.Headers)
            {
                var key = header.Key;
                var value = header.Value.ToString();
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                if (LooksSensitiveHeader(key))
                {
                    normalized[key] = BuildRedactionDescriptor(value, "header");
                    continue;
                }

                normalized[key] = value.Length <= 512 ? value : value[..512];
            }

            return JsonSerializer.Serialize(normalized);
        }

        public string SanitizeWebhookPayloadForStorage(string providerKey, string payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
            {
                return string.Empty;
            }

            var maxChars = Math.Clamp(
                _configuration.GetValue("Integrations:Operations:PiiMinimization:WebhookPayloadMaxChars", 8192),
                512,
                256 * 1024);

            if (payload.Length > maxChars)
            {
                payload = payload[..maxChars];
            }

            return SanitizeJsonForStorage(payload, $"webhook:{providerKey}");
        }

        public string SanitizeProviderMetadataJsonForStorage(string providerKey, string entityKind, string? json)
        {
            return SanitizeJsonForStorage(json, $"{providerKey}:{entityKind}");
        }

        public string SanitizeJsonForStorage(string? json, string contextTag)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return string.Empty;
            }

            try
            {
                using var document = JsonDocument.Parse(json);
                return SanitizeJsonElement(document.RootElement, contextTag);
            }
            catch (JsonException)
            {
                return JsonSerializer.Serialize(new
                {
                    context = contextTag,
                    rawTruncated = Truncate(json, 512),
                    sha256 = ComputeSha256(json)
                });
            }
        }

        public string SanitizeObjectForStorage(object value, string contextTag)
        {
            var raw = JsonSerializer.Serialize(value);
            return SanitizeJsonForStorage(raw, contextTag);
        }

        private string SanitizeJsonElement(JsonElement element, string contextTag)
        {
            var maxArrayItems = Math.Clamp(
                _configuration.GetValue("Integrations:Operations:PiiMinimization:MaxArrayItems", 50),
                5,
                500);
            var maxStringLength = Math.Clamp(
                _configuration.GetValue("Integrations:Operations:PiiMinimization:MaxStringLength", 512),
                64,
                16 * 1024);

            var buffer = new ArrayBufferWriter<byte>();
            using var writer = new Utf8JsonWriter(buffer);
            WriteSanitizedElement(writer, element, parentKey: null, maxArrayItems, maxStringLength);
            writer.Flush();
            return Encoding.UTF8.GetString(buffer.WrittenSpan);
        }

        private static void WriteSanitizedElement(
            Utf8JsonWriter writer,
            JsonElement element,
            string? parentKey,
            int maxArrayItems,
            int maxStringLength)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    writer.WriteStartObject();
                    foreach (var property in element.EnumerateObject())
                    {
                        writer.WritePropertyName(property.Name);
                        var normalizedKey = NormalizeKey(property.Name);
                        if (LooksSensitiveField(normalizedKey))
                        {
                            WriteRedactionObject(writer, property.Value, normalizedKey);
                            continue;
                        }

                        WriteSanitizedElement(writer, property.Value, normalizedKey, maxArrayItems, maxStringLength);
                    }
                    writer.WriteEndObject();
                    break;

                case JsonValueKind.Array:
                    writer.WriteStartArray();
                    var index = 0;
                    var truncated = false;
                    foreach (var item in element.EnumerateArray())
                    {
                        index++;
                        if (index > maxArrayItems)
                        {
                            truncated = true;
                            break;
                        }

                        WriteSanitizedElement(writer, item, parentKey, maxArrayItems, maxStringLength);
                    }

                    if (truncated)
                    {
                        writer.WriteStartObject();
                        writer.WriteBoolean("__truncated", true);
                        writer.WriteNumber("__maxItems", maxArrayItems);
                        writer.WriteEndObject();
                    }
                    writer.WriteEndArray();
                    break;

                case JsonValueKind.String:
                    var stringValue = element.GetString() ?? string.Empty;
                    if (LooksSensitiveField(parentKey) || LooksLikeDirectPii(parentKey, stringValue))
                    {
                        WriteRedactionObject(writer, stringValue, parentKey ?? "string");
                        break;
                    }

                    if (stringValue.Length > maxStringLength)
                    {
                        writer.WriteStringValue(stringValue[..maxStringLength]);
                        break;
                    }

                    writer.WriteStringValue(stringValue);
                    break;

                default:
                    element.WriteTo(writer);
                    break;
            }
        }

        private static void WriteRedactionObject(Utf8JsonWriter writer, JsonElement element, string? key)
        {
            WriteRedactionObject(writer, element.GetRawText(), key ?? "json");
        }

        private static void WriteRedactionObject(Utf8JsonWriter writer, string rawValue, string context)
        {
            writer.WriteStartObject();
            writer.WriteBoolean("__redacted", true);
            writer.WriteString("context", context);
            writer.WriteString("sha256", ComputeSha256(rawValue));
            if (!string.IsNullOrWhiteSpace(rawValue))
            {
                var normalized = rawValue.Trim();
                var last4 = normalized.Length >= 4 ? normalized[^4..] : normalized;
                writer.WriteString("last4", last4);
                writer.WriteNumber("length", rawValue.Length);
            }
            writer.WriteEndObject();
        }

        private static object BuildRedactionDescriptor(string rawValue, string context)
        {
            var trimmed = rawValue?.Trim() ?? string.Empty;
            return new
            {
                redacted = true,
                context,
                sha256 = ComputeSha256(rawValue ?? string.Empty),
                last4 = trimmed.Length >= 4 ? trimmed[^4..] : trimmed,
                length = rawValue?.Length ?? 0
            };
        }

        private static bool LooksSensitiveHeader(string headerName)
        {
            var normalized = NormalizeKey(headerName);
            return SensitiveHeaderTokens.Any(token => normalized.Contains(token, StringComparison.OrdinalIgnoreCase));
        }

        private static bool LooksSensitiveField(string? normalizedFieldName)
        {
            if (string.IsNullOrWhiteSpace(normalizedFieldName))
            {
                return false;
            }

            return SensitiveFieldTokens.Any(token =>
                normalizedFieldName.Contains(token, StringComparison.OrdinalIgnoreCase));
        }

        private static bool LooksLikeDirectPii(string? key, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            if (LooksSensitiveField(key))
            {
                return true;
            }

            if (value.Contains('@') && value.Contains('.'))
            {
                return true;
            }

            var digits = value.Count(char.IsDigit);
            return digits >= 9 && value.Length <= 32;
        }

        private static string NormalizeKey(string? key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(key.Length);
            foreach (var ch in key)
            {
                if (char.IsLetterOrDigit(ch))
                {
                    builder.Append(char.ToLowerInvariant(ch));
                }
            }

            return builder.ToString();
        }

        private static string ComputeSha256(string value)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private static string? Truncate(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return value.Length <= maxLength ? value : value[..maxLength];
        }
    }
}
