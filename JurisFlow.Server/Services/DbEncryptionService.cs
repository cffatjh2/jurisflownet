using System.Security.Cryptography;
using System.Text;

namespace JurisFlow.Server.Services
{
    public class DbEncryptionService
    {
        private const string Prefix = "enc:v1:";
        private const int KeySizeBytes = 32;
        private const int IvSizeBytes = 12;
        private const int TagSizeBytes = 16;
        private readonly byte[]? _key;

        public bool Enabled { get; }
        public bool IsConfigured => _key != null && _key.Length == KeySizeBytes;

        public DbEncryptionService(IConfiguration configuration, ILogger<DbEncryptionService> logger)
        {
            Enabled = configuration.GetValue("Security:DbEncryptionEnabled", false);

            var rawKey = configuration["Security:DbEncryptionKey"];
            if (!string.IsNullOrWhiteSpace(rawKey))
            {
                try
                {
                    _key = Convert.FromBase64String(rawKey);
                }
                catch (FormatException ex)
                {
                    logger.LogError(ex, "Database encryption key is not valid base64.");
                    _key = null;
                }
            }

            if (Enabled && (_key == null || _key.Length != KeySizeBytes))
            {
                throw new InvalidOperationException("Database encryption is enabled but the key is missing or invalid.");
            }
        }

        public string? EncryptString(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            if (!Enabled)
            {
                return value;
            }

            if (IsEncrypted(value))
            {
                return value;
            }

            var key = RequireKey();
            var iv = RandomNumberGenerator.GetBytes(IvSizeBytes);
            var tag = new byte[TagSizeBytes];
            var plaintext = Encoding.UTF8.GetBytes(value);
            var ciphertext = new byte[plaintext.Length];

            using var aes = new AesGcm(key, TagSizeBytes);
            aes.Encrypt(iv, plaintext, ciphertext, tag);

            return $"{Prefix}{Convert.ToBase64String(iv)}:{Convert.ToBase64String(tag)}:{Convert.ToBase64String(ciphertext)}";
        }

        public string? DecryptString(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            if (!IsEncrypted(value))
            {
                return value;
            }

            try
            {
                var key = RequireKey();
                var parts = value.Substring(Prefix.Length).Split(':', 3);
                if (parts.Length != 3)
                {
                    return value;
                }

                var iv = Convert.FromBase64String(parts[0]);
                var tag = Convert.FromBase64String(parts[1]);
                var ciphertext = Convert.FromBase64String(parts[2]);
                var plaintext = new byte[ciphertext.Length];

                using var aes = new AesGcm(key, TagSizeBytes);
                aes.Decrypt(iv, ciphertext, tag, plaintext);

                return Encoding.UTF8.GetString(plaintext);
            }
            catch
            {
                return value;
            }
        }

        public bool IsEncrypted(string value)
        {
            return value.StartsWith(Prefix, StringComparison.Ordinal);
        }

        private byte[] RequireKey()
        {
            if (_key == null || _key.Length != KeySizeBytes)
            {
                throw new InvalidOperationException("Database encryption key is missing or invalid.");
            }
            return _key;
        }
    }
}
