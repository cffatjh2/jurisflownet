using System.Security.Cryptography;
using System.Text;

namespace JurisFlow.Server.Services
{
    public enum IntegrationSecretScope
    {
        Connect,
        Validate,
        Sync,
        Disconnect,
        Rotation,
        SystemMigration
    }

    public enum IntegrationSecretOperation
    {
        Read,
        Write,
        Delete,
        Rotate
    }

    public interface IIntegrationSecretAccessPolicy
    {
        void EnsureAllowed(IntegrationSecretScope scope, IntegrationSecretOperation operation);
    }

    public sealed class IntegrationSecretAccessPolicy : IIntegrationSecretAccessPolicy
    {
        public void EnsureAllowed(IntegrationSecretScope scope, IntegrationSecretOperation operation)
        {
            var allowed = scope switch
            {
                IntegrationSecretScope.Connect => operation is IntegrationSecretOperation.Read or IntegrationSecretOperation.Write,
                IntegrationSecretScope.Validate => operation is IntegrationSecretOperation.Read or IntegrationSecretOperation.Write,
                IntegrationSecretScope.Sync => operation is IntegrationSecretOperation.Read or IntegrationSecretOperation.Write,
                IntegrationSecretScope.Disconnect => operation == IntegrationSecretOperation.Delete,
                IntegrationSecretScope.Rotation => operation is IntegrationSecretOperation.Read or IntegrationSecretOperation.Write or IntegrationSecretOperation.Rotate,
                IntegrationSecretScope.SystemMigration => operation is IntegrationSecretOperation.Read or IntegrationSecretOperation.Write,
                _ => false
            };

            if (!allowed)
            {
                throw new UnauthorizedAccessException($"Operation '{operation}' is not permitted for secret scope '{scope}'.");
            }
        }
    }

    public interface IIntegrationSecretKeyProvider
    {
        ValueTask<IntegrationSecretKeyRing> GetKeyRingAsync(CancellationToken cancellationToken);
    }

    public sealed class IntegrationSecretKeyRing
    {
        public IntegrationSecretKeyRing(
            string source,
            string activeKeyId,
            IReadOnlyDictionary<string, byte[]> keys)
        {
            Source = source;
            ActiveKeyId = activeKeyId;
            Keys = keys;
        }

        public string Source { get; }
        public string ActiveKeyId { get; }
        public IReadOnlyDictionary<string, byte[]> Keys { get; }
    }

    public sealed class IntegrationSecretKeyProvider : IIntegrationSecretKeyProvider
    {
        private const int RequiredKeyBytes = 32;
        private readonly IConfiguration _configuration;
        private readonly ILogger<IntegrationSecretKeyProvider> _logger;

        public IntegrationSecretKeyProvider(
            IConfiguration configuration,
            ILogger<IntegrationSecretKeyProvider> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public ValueTask<IntegrationSecretKeyRing> GetKeyRingAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var mode = (_configuration["Security:IntegrationSecrets:Provider"] ?? "config")
                .Trim()
                .ToLowerInvariant();

            return mode switch
            {
                "keyvault" => new ValueTask<IntegrationSecretKeyRing>(BuildKeyRing(
                    source: "keyvault",
                    rootPath: "Security:IntegrationSecrets:KeyVault",
                    keysSectionName: "DataKeys")),
                "kms" => new ValueTask<IntegrationSecretKeyRing>(BuildKeyRing(
                    source: "kms",
                    rootPath: "Security:IntegrationSecrets:Kms",
                    keysSectionName: "DataKeys")),
                _ => new ValueTask<IntegrationSecretKeyRing>(BuildKeyRing(
                    source: "config",
                    rootPath: "Security:IntegrationSecrets",
                    keysSectionName: "Keys"))
            };
        }

        private IntegrationSecretKeyRing BuildKeyRing(string source, string rootPath, string keysSectionName)
        {
            var keysPath = $"{rootPath}:{keysSectionName}";
            var activeKeyId = _configuration[$"{rootPath}:ActiveKeyId"]?.Trim();
            var keys = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

            foreach (var child in _configuration.GetSection(keysPath).GetChildren())
            {
                var keyId = child.Key?.Trim();
                var rawValue = child.Value?.Trim();
                if (string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(rawValue))
                {
                    continue;
                }

                try
                {
                    var decoded = Convert.FromBase64String(rawValue);
                    if (decoded.Length != RequiredKeyBytes)
                    {
                        throw new InvalidOperationException(
                            $"Integration secret key '{keyId}' under '{keysPath}' must be {RequiredKeyBytes} bytes.");
                    }

                    keys[keyId] = decoded;
                }
                catch (FormatException ex)
                {
                    _logger.LogError(ex, "Invalid base64 integration secret key. Source={Source} KeyId={KeyId}", source, keyId);
                    throw new InvalidOperationException(
                        $"Integration secret key '{keyId}' under '{keysPath}' is not valid base64.",
                        ex);
                }
            }

            if (keys.Count == 0)
            {
                throw new InvalidOperationException(
                    $"No integration secret keys configured for source '{source}'. Expected values under '{keysPath}'.");
            }

            if (string.IsNullOrWhiteSpace(activeKeyId))
            {
                activeKeyId = keys.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).Last();
            }
            else
            {
                activeKeyId = IntegrationSecretConfigurationResolver.FindEquivalentKeyId(keys.Keys, activeKeyId) ?? activeKeyId;
            }

            if (!keys.ContainsKey(activeKeyId))
            {
                throw new InvalidOperationException(
                    $"Active integration secret key '{activeKeyId}' is missing in '{keysPath}'.");
            }

            return new IntegrationSecretKeyRing(source, activeKeyId, keys);
        }
    }

    public interface IIntegrationSecretCryptoService
    {
        string EncryptionProviderId { get; }
        bool LegacyPlaintextAllowed { get; }
        ValueTask<string> GetActiveKeyIdAsync(CancellationToken cancellationToken);
        ValueTask<IntegrationSecretEncryptedPayload> EncryptAsync(string plaintext, CancellationToken cancellationToken);
        ValueTask<IntegrationSecretDecryptedPayload> DecryptAsync(string payload, CancellationToken cancellationToken);
    }

    public sealed class IntegrationSecretCryptoService : IIntegrationSecretCryptoService
    {
        private const string Prefix = "isec:v1:";
        private const int IvSizeBytes = 12;
        private const int TagSizeBytes = 16;
        private readonly IIntegrationSecretKeyProvider _keyProvider;
        private readonly IConfiguration _configuration;

        public IntegrationSecretCryptoService(
            IIntegrationSecretKeyProvider keyProvider,
            IConfiguration configuration)
        {
            _keyProvider = keyProvider;
            _configuration = configuration;
        }

        public string EncryptionProviderId => "aead_aes256_gcm";

        public bool LegacyPlaintextAllowed => _configuration.GetValue("Security:IntegrationSecrets:LegacyPlaintextAllowed", false);

        public async ValueTask<string> GetActiveKeyIdAsync(CancellationToken cancellationToken)
        {
            var ring = await _keyProvider.GetKeyRingAsync(cancellationToken);
            return ring.ActiveKeyId;
        }

        public async ValueTask<IntegrationSecretEncryptedPayload> EncryptAsync(string plaintext, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(plaintext))
            {
                throw new InvalidOperationException("Secret payload cannot be empty.");
            }

            var ring = await _keyProvider.GetKeyRingAsync(cancellationToken);
            var key = ring.Keys[ring.ActiveKeyId];
            var iv = RandomNumberGenerator.GetBytes(IvSizeBytes);
            var tag = new byte[TagSizeBytes];
            var payloadBytes = Encoding.UTF8.GetBytes(plaintext);
            var cipher = new byte[payloadBytes.Length];

            using var aes = new AesGcm(key, TagSizeBytes);
            aes.Encrypt(iv, payloadBytes, cipher, tag);

            var protectedPayload =
                $"{Prefix}{ring.ActiveKeyId}:{Convert.ToBase64String(iv)}:{Convert.ToBase64String(tag)}:{Convert.ToBase64String(cipher)}";

            return new IntegrationSecretEncryptedPayload
            {
                Payload = protectedPayload,
                KeyId = ring.ActiveKeyId,
                Source = ring.Source
            };
        }

        public async ValueTask<IntegrationSecretDecryptedPayload> DecryptAsync(string payload, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(payload))
            {
                throw new InvalidOperationException("Secret payload is empty.");
            }

            var ring = await _keyProvider.GetKeyRingAsync(cancellationToken);

            if (!payload.StartsWith(Prefix, StringComparison.Ordinal))
            {
                if (!LegacyPlaintextAllowed)
                {
                    throw new InvalidOperationException("Legacy plaintext integration secret payload is not allowed.");
                }

                return new IntegrationSecretDecryptedPayload
                {
                    Plaintext = payload,
                    KeyId = string.Empty,
                    ShouldRotate = true
                };
            }

            var parts = payload[Prefix.Length..].Split(':', 4);
            if (parts.Length != 4)
            {
                throw new InvalidOperationException("Integration secret payload is malformed.");
            }

            var requestedKeyId = parts[0];
            var keyId = requestedKeyId;
            if (!ring.Keys.TryGetValue(keyId, out var key))
            {
                keyId = IntegrationSecretConfigurationResolver.FindEquivalentKeyId(ring.Keys.Keys, requestedKeyId) ?? keyId;
            }

            if (!ring.Keys.TryGetValue(keyId, out key))
            {
                throw new InvalidOperationException(
                    $"Integration secret key '{requestedKeyId}' is not available in current key ring.");
            }

            var iv = Convert.FromBase64String(parts[1]);
            var tag = Convert.FromBase64String(parts[2]);
            var cipher = Convert.FromBase64String(parts[3]);
            var plaintextBytes = new byte[cipher.Length];

            using var aes = new AesGcm(key, TagSizeBytes);
            aes.Decrypt(iv, cipher, tag, plaintextBytes);

            return new IntegrationSecretDecryptedPayload
            {
                Plaintext = Encoding.UTF8.GetString(plaintextBytes),
                KeyId = requestedKeyId,
                ShouldRotate = !IntegrationSecretConfigurationResolver.IsEquivalentKeyId(requestedKeyId, ring.ActiveKeyId)
            };
        }
    }

    public sealed class IntegrationSecretEncryptedPayload
    {
        public string Payload { get; set; } = string.Empty;
        public string KeyId { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
    }

    public sealed class IntegrationSecretDecryptedPayload
    {
        public string Plaintext { get; set; } = string.Empty;
        public string KeyId { get; set; } = string.Empty;
        public bool ShouldRotate { get; set; }
    }
}
