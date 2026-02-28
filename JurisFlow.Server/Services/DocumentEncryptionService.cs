using System.Security.Cryptography;

namespace JurisFlow.Server.Services
{
    public record DocumentEncryptionPayload(
        byte[] Ciphertext,
        string Iv,
        string Tag,
        string KeyId,
        string Algorithm);

    public class DocumentEncryptionService
    {
        private const int KeySizeBytes = 32;
        private const int IvSizeBytes = 12;
        private const int TagSizeBytes = 16;
        private readonly byte[]? _key;

        public bool Enabled { get; }
        public bool IsConfigured => _key != null && _key.Length == KeySizeBytes;
        public string KeyId { get; }
        public string Algorithm => "AES-256-GCM";

        public DocumentEncryptionService(IConfiguration configuration, ILogger<DocumentEncryptionService> logger)
        {
            Enabled = configuration.GetValue("Security:DocumentEncryptionEnabled", false);
            KeyId = configuration["Security:DocumentEncryptionKeyId"] ?? "primary";

            var rawKey = configuration["Security:DocumentEncryptionKey"];
            if (!string.IsNullOrWhiteSpace(rawKey))
            {
                try
                {
                    _key = Convert.FromBase64String(rawKey);
                }
                catch (FormatException ex)
                {
                    logger.LogError(ex, "Document encryption key is not valid base64.");
                    _key = null;
                }
            }

            if (Enabled && (_key == null || _key.Length != KeySizeBytes))
            {
                throw new InvalidOperationException("Document encryption is enabled but the key is missing or invalid.");
            }
        }

        public DocumentEncryptionPayload EncryptBytes(byte[] plaintext)
        {
            var key = RequireKey();
            var iv = RandomNumberGenerator.GetBytes(IvSizeBytes);
            var tag = new byte[TagSizeBytes];
            var cipher = new byte[plaintext.Length];

            using var aes = new AesGcm(key, TagSizeBytes);
            aes.Encrypt(iv, plaintext, cipher, tag);

            return new DocumentEncryptionPayload(
                cipher,
                Convert.ToBase64String(iv),
                Convert.ToBase64String(tag),
                KeyId,
                Algorithm);
        }

        public byte[] DecryptBytes(byte[] ciphertext, string ivBase64, string tagBase64)
        {
            var key = RequireKey();
            var iv = Convert.FromBase64String(ivBase64);
            var tag = Convert.FromBase64String(tagBase64);
            var plaintext = new byte[ciphertext.Length];

            using var aes = new AesGcm(key, TagSizeBytes);
            aes.Decrypt(iv, ciphertext, tag, plaintext);

            return plaintext;
        }

        public async Task<DocumentEncryptionPayload> EncryptFileAsync(Stream input, string outputPath, CancellationToken cancellationToken = default)
        {
            using var buffer = new MemoryStream();
            await input.CopyToAsync(buffer, cancellationToken);
            var payload = EncryptBytes(buffer.ToArray());
            await File.WriteAllBytesAsync(outputPath, payload.Ciphertext, cancellationToken);
            return payload;
        }

        public async Task<byte[]> DecryptFileAsync(string inputPath, string ivBase64, string tagBase64, CancellationToken cancellationToken = default)
        {
            var ciphertext = await File.ReadAllBytesAsync(inputPath, cancellationToken);
            return DecryptBytes(ciphertext, ivBase64, tagBase64);
        }

        private byte[] RequireKey()
        {
            if (_key == null || _key.Length != KeySizeBytes)
            {
                throw new InvalidOperationException("Document encryption key is missing or invalid.");
            }
            return _key;
        }
    }
}
