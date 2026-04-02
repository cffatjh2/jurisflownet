using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using JurisFlow.Server.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JurisFlow.Server.Tests;

public class ClientPortalAuthTests : IClassFixture<TestApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly TestApplicationFactory _factory;

    public ClientPortalAuthTests(TestApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async System.Threading.Tasks.Task PortalEnabledClientCanLogInWithinSameTenant()
    {
        var email = $"portal-auth-{Guid.NewGuid():N}@example.com";
        const string password = "PortalTest123!";

        using (var scope = _factory.Services.CreateScope())
        {
            var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
            tenantContext.Set(TestApplicationFactory.TestTenantId, TestApplicationFactory.TestTenantSlug);

            var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
            var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();

            db.Clients.Add(new Client
            {
                Id = Guid.NewGuid().ToString(),
                Name = "Portal Auth Test",
                Email = email,
                NormalizedEmail = EmailAddressNormalizer.Normalize(email),
                Type = "Individual",
                Status = "Active",
                PortalEnabled = true,
                PasswordHash = PasswordHashingHelper.HashPassword(password, configuration),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync();
        }

        var loginResponse = await _client.PostAsJsonAsync("/api/client/login", new
        {
            email,
            password
        });

        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        using var document = JsonDocument.Parse(await loginResponse.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.True(root.TryGetProperty("token", out var tokenElement));
        Assert.False(string.IsNullOrWhiteSpace(tokenElement.GetString()));
        Assert.Equal(email, root.GetProperty("client").GetProperty("email").GetString());
        Assert.True(root.GetProperty("session").TryGetProperty("id", out var sessionId));
        Assert.False(string.IsNullOrWhiteSpace(sessionId.GetString()));
    }
}
