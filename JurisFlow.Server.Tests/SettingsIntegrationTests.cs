using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using JurisFlow.Server.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace JurisFlow.Server.Tests;

public class SettingsIntegrationTests : IClassFixture<TestApplicationFactory>
{
    private readonly TestApplicationFactory _factory;
    private readonly HttpClient _client;

    public SettingsIntegrationTests(TestApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetIntegrationCatalog_ReturnsBackendCatalog()
    {
        var request = CreateStaffRequest(HttpMethod.Get, "/api/settings/integrations/catalog");

        var response = await _client.SendAsync(request);
        var payload = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var items = JsonSerializer.Deserialize<List<IntegrationCatalogItemDto>>(
            payload,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<IntegrationCatalogItemDto>();

        Assert.NotEmpty(items);
        Assert.Contains(items, i => i.ProviderKey == "stripe" && i.Category == "Payments");
        Assert.Contains(items, i => i.ProviderKey == "courtlistener-dockets" && i.Category == "Court Docket");
        Assert.Contains(items, i => i.ProviderKey == "courtlistener-recap" && i.Category == "E-Filing");
        Assert.Contains(items, i => i.ProviderKey == "quickbooks-online" && i.WebhookFirst);
    }

    [Fact]
    public async Task ConnectStripe_WithoutApiKey_ReturnsBadRequest()
    {
        var request = CreateStaffRequest(
            HttpMethod.Post,
            "/api/settings/integrations/stripe/connect",
            new
            {
                accountLabel = "Ops",
                syncEnabled = true
            });

        var response = await _client.SendAsync(request);
        var payload = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("API key", payload, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdateIntegrations_PersistsToConnectionStore_AndClearsLegacyJson()
    {
        await SeedLegacyIntegrationsAsync();

        var request = CreateStaffRequest(
            HttpMethod.Put,
            "/api/settings/integrations",
            new
            {
                items = new[]
                {
                    new
                    {
                        providerKey = "stripe",
                        provider = "Stripe",
                        category = "Payments",
                        status = "connected",
                        accountLabel = "Operations",
                        syncEnabled = true
                    }
                }
            });

        var response = await _client.SendAsync(request);
        var payload = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var items = JsonSerializer.Deserialize<List<IntegrationItemDto>>(
            payload,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<IntegrationItemDto>();

        Assert.Single(items);
        Assert.Equal("stripe", items[0].ProviderKey);
        Assert.Equal("Stripe", items[0].Provider);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
        tenantContext.Set(TestApplicationFactory.TestTenantId, TestApplicationFactory.TestTenantSlug);

        var settings = await db.FirmSettings.AsNoTracking().SingleAsync();
        Assert.True(string.IsNullOrWhiteSpace(settings.IntegrationsJson));

        var connections = await db.IntegrationConnections.AsNoTracking().ToListAsync();
        Assert.Single(connections);
        Assert.Equal("stripe", connections[0].ProviderKey);
    }

    private async Task SeedLegacyIntegrationsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
        tenantContext.Set(TestApplicationFactory.TestTenantId, TestApplicationFactory.TestTenantSlug);

        db.IntegrationConnections.RemoveRange(db.IntegrationConnections);

        var settings = await db.FirmSettings.FirstOrDefaultAsync();
        if (settings == null)
        {
            settings = new FirmSettings();
            db.FirmSettings.Add(settings);
        }

        settings.IntegrationsJson = JsonSerializer.Serialize(new[]
        {
            new
            {
                id = "legacy-1",
                provider = "QuickBooks Online",
                category = "Accounting",
                status = "connected",
                accountLabel = "Legacy Account",
                syncEnabled = true
            }
        });
        settings.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
    }

    private static HttpRequestMessage CreateStaffRequest(HttpMethod method, string url, object? payload = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-Test-UserId", "staff-user");
        request.Headers.Add("X-Test-Role", "Partner");

        if (payload != null)
        {
            request.Content = JsonContent.Create(payload);
        }

        return request;
    }

    private sealed class IntegrationCatalogItemDto
    {
        public string ProviderKey { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public bool WebhookFirst { get; set; }
    }

    private sealed class IntegrationItemDto
    {
        public string ProviderKey { get; set; } = string.Empty;
        public string Provider { get; set; } = string.Empty;
    }
}
