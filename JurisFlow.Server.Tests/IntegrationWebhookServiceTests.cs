using System.Security.Cryptography;
using System.Text;
using JurisFlow.Server.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;

namespace JurisFlow.Server.Tests;

public class IntegrationWebhookServiceTests
{
    [Fact]
    public void Validate_QuickBooksWebhook_WithValidSignature_Succeeds()
    {
        const string payload = "{\"eventNotifications\":[]}";
        const string secret = "quickbooks-webhook-secret";
        var signature = ComputeBase64HmacSha256(payload, secret);

        var config = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Integrations:QuickBooks:WebhookVerifierToken"] = secret,
            ["Integrations:Webhooks:AllowUnsigned"] = "false"
        });
        var service = new IntegrationWebhookService(
            config,
            new StubHostEnvironment("Production"),
            NullLogger<IntegrationWebhookService>.Instance);
        var request = new DefaultHttpContext().Request;
        request.Headers["intuit-signature"] = signature;
        request.Headers["intuit-delivery-id"] = "evt-1";

        var result = service.Validate(IntegrationProviderKeys.QuickBooksOnline, request, payload);

        Assert.True(result.Success);
        Assert.True(result.SignatureValidated);
        Assert.Contains("evt-1", result.EventId, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_QuickBooksWebhook_WithInvalidSignature_Fails()
    {
        const string payload = "{\"eventNotifications\":[]}";
        const string secret = "quickbooks-webhook-secret";

        var config = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Integrations:QuickBooks:WebhookVerifierToken"] = secret,
            ["Integrations:Webhooks:AllowUnsigned"] = "false"
        });
        var service = new IntegrationWebhookService(
            config,
            new StubHostEnvironment("Production"),
            NullLogger<IntegrationWebhookService>.Instance);
        var request = new DefaultHttpContext().Request;
        request.Headers["intuit-signature"] = Convert.ToBase64String(Guid.NewGuid().ToByteArray());

        var result = service.Validate(IntegrationProviderKeys.QuickBooksOnline, request, payload);

        Assert.False(result.Success);
        Assert.False(result.SignatureValidated);
    }

    [Fact]
    public void ResolveFallbackPollingMinutes_UsesWebhookFallbackForWebhookFirstProviders()
    {
        var config = BuildConfiguration(new Dictionary<string, string?>());
        var service = new IntegrationWebhookService(
            config,
            new StubHostEnvironment("Production"),
            NullLogger<IntegrationWebhookService>.Instance);

        var quickBooksPolling = service.ResolveFallbackPollingMinutes(IntegrationProviderKeys.QuickBooksOnline, 60);
        var stripePolling = service.ResolveFallbackPollingMinutes(IntegrationProviderKeys.Stripe, 60);

        Assert.True(quickBooksPolling > 60);
        Assert.Equal(60, stripePolling);
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    private static string ComputeBase64HmacSha256(string payload, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToBase64String(hash);
    }

    private sealed class StubHostEnvironment : IWebHostEnvironment
    {
        public StubHostEnvironment(string environmentName)
        {
            EnvironmentName = environmentName;
        }

        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "JurisFlow.Server.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
