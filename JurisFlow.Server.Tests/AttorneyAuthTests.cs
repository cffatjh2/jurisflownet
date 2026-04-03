using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using JurisFlow.Server.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JurisFlow.Server.Tests;

public class AttorneyAuthTests : IClassFixture<TestApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly TestApplicationFactory _factory;

    public AttorneyAuthTests(TestApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async System.Threading.Tasks.Task TenantScopedAttorneyCanLogIn()
    {
        var email = $"attorney-auth-{Guid.NewGuid():N}@example.com";
        const string password = "ChangeMe123.";

        using (var scope = _factory.Services.CreateScope())
        {
            var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
            tenantContext.Set(TestApplicationFactory.TestTenantId, TestApplicationFactory.TestTenantSlug);

            var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
            var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();

            db.Users.Add(new User
            {
                Id = Guid.NewGuid().ToString(),
                Email = email,
                NormalizedEmail = EmailAddressNormalizer.Normalize(email),
                Name = "Fatih Alpaslan",
                Role = "Attorney",
                MfaEnabled = false,
                PasswordHash = PasswordHashingHelper.HashPassword(password, configuration),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync();
        }

        var loginResponse = await _client.PostAsJsonAsync("/api/login", new
        {
            email,
            password
        });

        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        using var document = JsonDocument.Parse(await loginResponse.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.True(root.TryGetProperty("token", out var tokenElement));
        Assert.False(string.IsNullOrWhiteSpace(tokenElement.GetString()));
        Assert.Equal(email, root.GetProperty("user").GetProperty("email").GetString());
    }

    [Fact]
    public async System.Threading.Tasks.Task ExternalBcryptHashCanLogIn()
    {
        var email = $"attorney-external-hash-{Guid.NewGuid():N}@example.com";
        const string password = "ChangeMe123.";
        const string externalHash = "$2a$12$su2y5ZhJ/XmU2XdpeW3peuIZv35woZKxs3tsnYK9HQavNpauQnR66";

        using (var scope = _factory.Services.CreateScope())
        {
            var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
            tenantContext.Set(TestApplicationFactory.TestTenantId, TestApplicationFactory.TestTenantSlug);

            var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();

            db.Users.Add(new User
            {
                Id = Guid.NewGuid().ToString(),
                Email = email,
                NormalizedEmail = EmailAddressNormalizer.Normalize(email),
                Name = "External Hash User",
                Role = "Attorney",
                MfaEnabled = false,
                PasswordHash = externalHash,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync();
        }

        var loginResponse = await _client.PostAsJsonAsync("/api/login", new
        {
            email,
            password
        });

        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
    }

    [Fact]
    public async System.Threading.Tasks.Task RepeatedInvalidAttorneyLoginsReturnLockoutMessage()
    {
        var email = $"attorney-lockout-{Guid.NewGuid():N}@example.com";
        const string correctPassword = "ChangeMe123.";

        using (var scope = _factory.Services.CreateScope())
        {
            var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
            tenantContext.Set(TestApplicationFactory.TestTenantId, TestApplicationFactory.TestTenantSlug);

            var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
            var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();

            db.Users.Add(new User
            {
                Id = Guid.NewGuid().ToString(),
                Email = email,
                NormalizedEmail = EmailAddressNormalizer.Normalize(email),
                Name = "Lockout Test User",
                Role = "Attorney",
                MfaEnabled = false,
                PasswordHash = PasswordHashingHelper.HashPassword(correctPassword, configuration),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync();
        }

        HttpResponseMessage? lastResponse = null;
        for (var attempt = 0; attempt < 5; attempt += 1)
        {
            lastResponse = await _client.PostAsJsonAsync("/api/login", new
            {
                email,
                password = "WrongPassword123!"
            });
        }

        Assert.NotNull(lastResponse);
        Assert.Equal(HttpStatusCode.TooManyRequests, lastResponse!.StatusCode);

        using var document = JsonDocument.Parse(await lastResponse.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal("Too many failed login attempts. Please retry later.", root.GetProperty("message").GetString());
        Assert.True(root.GetProperty("retryAfterSeconds").GetInt32() > 0);
    }
}
