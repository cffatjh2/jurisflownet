using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace JurisFlow.Server.Tests;

public class AppDirectoryIntegrationTests : IClassFixture<TestApplicationFactory>
{
    private readonly HttpClient _client;

    public AppDirectoryIntegrationTests(TestApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task SubmitOnboarding_WithValidManifest_CreatesReviewReadyListing()
    {
        var request = CreateRequest(
            HttpMethod.Post,
            "/api/app-directory/onboarding/submit",
            role: "Partner",
            userId: "partner-user",
            payload: BuildValidSubmissionPayload("acme-case-sync"));

        var response = await _client.SendAsync(request);
        var payload = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(payload);
        Assert.True(doc.RootElement.GetProperty("harness").GetProperty("passed").GetBoolean());
        Assert.Equal("in_review", doc.RootElement.GetProperty("listing").GetProperty("status").GetString());
        Assert.Equal("acme-case-sync", doc.RootElement.GetProperty("listing").GetProperty("providerKey").GetString());
    }

    [Fact]
    public async Task SubmitOnboarding_WithInvalidManifest_ReturnsHarnessFailures()
    {
        var request = CreateRequest(
            HttpMethod.Post,
            "/api/app-directory/onboarding/submit",
            role: "Partner",
            userId: "partner-user",
            payload: new
            {
                manifest = new
                {
                    providerKey = "InvalidProvider",
                    name = "Invalid Provider",
                    category = "Email",
                    connectionMode = "oauth",
                    summary = "Invalid webhook config",
                    manifestVersion = "1.0",
                    supportsWebhook = false,
                    webhookFirst = true,
                    fallbackPollingMinutes = 10,
                    capabilities = Array.Empty<string>()
                },
                sla = new
                {
                    tier = "gold",
                    responseHours = 2,
                    resolutionHours = 12,
                    uptimePercent = 99.9
                }
            });

        var response = await _client.SendAsync(request);
        var payload = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(payload);
        Assert.False(doc.RootElement.GetProperty("harness").GetProperty("passed").GetBoolean());
        Assert.Equal("changes_requested", doc.RootElement.GetProperty("listing").GetProperty("status").GetString());
        Assert.True(doc.RootElement.GetProperty("harness").GetProperty("errorCount").GetInt32() > 0);
    }

    [Fact]
    public async Task ReviewListing_RequiresAdminRole()
    {
        var listingId = await SubmitListingAndGetId("review-role-test");

        var reviewRequest = CreateRequest(
            HttpMethod.Post,
            $"/api/app-directory/listings/{listingId}/review",
            role: "Partner",
            userId: "partner-user",
            payload: new
            {
                decision = "approve",
                publish = true,
                notes = "partner cannot review"
            });

        var response = await _client.SendAsync(reviewRequest);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AdminCanPublishListing_AndClientSeesPublishedDirectory()
    {
        var providerKey = "publish-ready-app";
        var listingId = await SubmitListingAndGetId(providerKey);

        var reviewRequest = CreateRequest(
            HttpMethod.Post,
            $"/api/app-directory/listings/{listingId}/review",
            role: "Admin",
            userId: "admin-user",
            payload: new
            {
                decision = "approve",
                publish = true,
                isFeatured = true,
                notes = "approved for directory"
            });

        var reviewResponse = await _client.SendAsync(reviewRequest);
        Assert.Equal(HttpStatusCode.OK, reviewResponse.StatusCode);

        var listingsRequest = CreateRequest(
            HttpMethod.Get,
            "/api/app-directory/listings",
            role: "Client",
            userId: "client-user");

        var listingsResponse = await _client.SendAsync(listingsRequest);
        var listingsPayload = await listingsResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, listingsResponse.StatusCode);
        using var doc = JsonDocument.Parse(listingsPayload);
        Assert.True(doc.RootElement.ValueKind == JsonValueKind.Array);
        Assert.Contains(
            doc.RootElement.EnumerateArray().Select(i => i.GetProperty("providerKey").GetString()),
            key => string.Equals(key, providerKey, StringComparison.Ordinal));
    }

    private async Task<string> SubmitListingAndGetId(string providerKey)
    {
        var submitRequest = CreateRequest(
            HttpMethod.Post,
            "/api/app-directory/onboarding/submit",
            role: "Partner",
            userId: "partner-user",
            payload: BuildValidSubmissionPayload(providerKey));

        var submitResponse = await _client.SendAsync(submitRequest);
        var submitPayload = await submitResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);
        using var doc = JsonDocument.Parse(submitPayload);
        var id = doc.RootElement.GetProperty("listing").GetProperty("id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(id));
        return id!;
    }

    private static object BuildValidSubmissionPayload(string providerKey)
    {
        return new
        {
            manifest = new
            {
                providerKey,
                name = $"App {providerKey}",
                category = "Court Docket",
                connectionMode = "oauth",
                summary = "Bi-directional sync with webhook-first strategy",
                description = "Syncs dockets and filing updates into JurisFlow.",
                manifestVersion = "1.0",
                websiteUrl = "https://example.com",
                documentationUrl = "https://example.com/docs",
                supportEmail = "support@example.com",
                supportUrl = "https://example.com/support",
                logoUrl = "https://example.com/logo.png",
                supportsWebhook = true,
                webhookFirst = true,
                fallbackPollingMinutes = 360,
                capabilities = new[] { "docket_sync", "filing_status" }
            },
            sla = new
            {
                tier = "gold",
                responseHours = 4,
                resolutionHours = 24,
                uptimePercent = 99.9
            }
        };
    }

    private static HttpRequestMessage CreateRequest(
        HttpMethod method,
        string url,
        string role,
        string userId,
        object? payload = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-Test-UserId", userId);
        request.Headers.Add("X-Test-Role", role);

        if (payload != null)
        {
            request.Content = JsonContent.Create(payload);
        }

        return request;
    }
}
