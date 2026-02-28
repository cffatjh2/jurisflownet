using JurisFlow.Server.Services;
using Microsoft.Extensions.Configuration;

namespace JurisFlow.Server.Tests;

public class IntegrationSecretProtectionTests
{
    [Fact]
    public async Task Crypto_RoundTrip_WorksWithActiveKey()
    {
        var provider = new StubKeyProvider(
            source: "config",
            activeKeyId: "v1",
            keys: new Dictionary<string, byte[]>
            {
                ["v1"] = Convert.FromBase64String("MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=")
            });
        var config = BuildConfig(legacyPlaintextAllowed: false);
        var crypto = new IntegrationSecretCryptoService(provider, config);

        var encrypted = await crypto.EncryptAsync("{\"token\":\"abc\"}", CancellationToken.None);
        var decrypted = await crypto.DecryptAsync(encrypted.Payload, CancellationToken.None);

        Assert.Equal("{\"token\":\"abc\"}", decrypted.Plaintext);
        Assert.False(decrypted.ShouldRotate);
        Assert.Equal("v1", encrypted.KeyId);
    }

    [Fact]
    public async Task Crypto_Decrypt_OldKeyFlagsRotation()
    {
        var oldProvider = new StubKeyProvider(
            source: "config",
            activeKeyId: "v1",
            keys: new Dictionary<string, byte[]>
            {
                ["v1"] = Convert.FromBase64String("MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=")
            });
        var newProvider = new StubKeyProvider(
            source: "config",
            activeKeyId: "v2",
            keys: new Dictionary<string, byte[]>
            {
                ["v1"] = Convert.FromBase64String("MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY="),
                ["v2"] = Convert.FromBase64String("ZmVkY2JhOTg3NjU0MzIxMGZlZGNiYTk4NzY1NDMyMTA=")
            });
        var config = BuildConfig(legacyPlaintextAllowed: false);

        var oldCrypto = new IntegrationSecretCryptoService(oldProvider, config);
        var encrypted = await oldCrypto.EncryptAsync("{\"k\":\"v\"}", CancellationToken.None);

        var newCrypto = new IntegrationSecretCryptoService(newProvider, config);
        var decrypted = await newCrypto.DecryptAsync(encrypted.Payload, CancellationToken.None);

        Assert.True(decrypted.ShouldRotate);
        Assert.Equal("v1", decrypted.KeyId);
    }

    [Fact]
    public void AccessPolicy_Disallows_DeleteInConnectScope()
    {
        var policy = new IntegrationSecretAccessPolicy();

        Assert.Throws<UnauthorizedAccessException>(() =>
            policy.EnsureAllowed(IntegrationSecretScope.Connect, IntegrationSecretOperation.Delete));
    }

    private static IConfiguration BuildConfig(bool legacyPlaintextAllowed)
    {
        var values = new Dictionary<string, string?>
        {
            ["Security:IntegrationSecrets:LegacyPlaintextAllowed"] = legacyPlaintextAllowed ? "true" : "false"
        };
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    private sealed class StubKeyProvider : IIntegrationSecretKeyProvider
    {
        private readonly IntegrationSecretKeyRing _ring;

        public StubKeyProvider(string source, string activeKeyId, IReadOnlyDictionary<string, byte[]> keys)
        {
            _ring = new IntegrationSecretKeyRing(source, activeKeyId, keys);
        }

        public ValueTask<IntegrationSecretKeyRing> GetKeyRingAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(_ring);
        }
    }
}
