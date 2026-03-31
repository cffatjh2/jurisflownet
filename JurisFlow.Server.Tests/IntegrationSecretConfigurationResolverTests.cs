using System.Security.Cryptography;
using System.Text;
using JurisFlow.Server.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace JurisFlow.Server.Tests;

public class IntegrationSecretConfigurationResolverTests
{
    [Fact]
    public async Task KeyProvider_ResolvesEquivalentConfiguredKeyId()
    {
        var keyBytes = Encoding.ASCII.GetBytes("0123456789abcdef0123456789abcdef");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:IntegrationSecrets:Provider"] = "config",
                ["Security:IntegrationSecrets:ActiveKeyId"] = "railway-v1",
                ["Security:IntegrationSecrets:Keys:railway_v1"] = Convert.ToBase64String(keyBytes)
            })
            .Build();

        var provider = new IntegrationSecretKeyProvider(
            configuration,
            NullLogger<IntegrationSecretKeyProvider>.Instance);

        var ring = await provider.GetKeyRingAsync(CancellationToken.None);

        Assert.Equal("railway_v1", ring.ActiveKeyId);
        Assert.True(ring.Keys.ContainsKey("railway_v1"));
    }

    [Fact]
    public async Task CryptoService_DecryptsEquivalentPayloadKeyIdWithoutRotation()
    {
        var keyBytes = Encoding.ASCII.GetBytes("0123456789abcdef0123456789abcdef");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:IntegrationSecrets:Provider"] = "config",
                ["Security:IntegrationSecrets:ActiveKeyId"] = "railway_v1",
                ["Security:IntegrationSecrets:Keys:railway_v1"] = Convert.ToBase64String(keyBytes)
            })
            .Build();

        var provider = new IntegrationSecretKeyProvider(
            configuration,
            NullLogger<IntegrationSecretKeyProvider>.Instance);
        var crypto = new IntegrationSecretCryptoService(provider, configuration);
        var payload = BuildPayload("railway-v1", keyBytes, "super-secret");

        var decrypted = await crypto.DecryptAsync(payload, CancellationToken.None);

        Assert.Equal("super-secret", decrypted.Plaintext);
        Assert.Equal("railway-v1", decrypted.KeyId);
        Assert.False(decrypted.ShouldRotate);
    }

    private static string BuildPayload(string keyId, byte[] key, string plaintext)
    {
        const int ivSizeBytes = 12;
        const int tagSizeBytes = 16;
        const string prefix = "isec:v1:";

        var iv = RandomNumberGenerator.GetBytes(ivSizeBytes);
        var tag = new byte[tagSizeBytes];
        var payloadBytes = Encoding.UTF8.GetBytes(plaintext);
        var cipher = new byte[payloadBytes.Length];

        using var aes = new AesGcm(key, tagSizeBytes);
        aes.Encrypt(iv, payloadBytes, cipher, tag);

        return $"{prefix}{keyId}:{Convert.ToBase64String(iv)}:{Convert.ToBase64String(tag)}:{Convert.ToBase64String(cipher)}";
    }
}
