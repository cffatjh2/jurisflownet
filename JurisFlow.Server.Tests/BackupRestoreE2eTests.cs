using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using JurisFlow.Server.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Task = System.Threading.Tasks.Task;

namespace JurisFlow.Server.Tests;

[CollectionDefinition("BackupE2E", DisableParallelization = true)]
public class BackupE2ECollection : ICollectionFixture<BackupE2eApplicationFactory>
{
}

[Collection("BackupE2E")]
public class BackupRestoreE2eTests
{
    private readonly BackupE2eApplicationFactory _factory;
    private readonly HttpClient _client;

    public BackupRestoreE2eTests(BackupE2eApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task BackupAndRestoreEndpointsRestoreDatabaseState()
    {
        var clientId = Guid.NewGuid().ToString();
        var baselineName = "Backup Baseline";
        var mutatedName = "Backup Mutated";
        var email = $"{Guid.NewGuid():N}@example.com";

        await SeedClientAsync(clientId, baselineName, email);

        var createBackupRequest = CreateAdminRequest(
            HttpMethod.Post,
            "/api/admin/backups",
            new { includeUploads = false });

        var createBackupResponse = await _client.SendAsync(createBackupRequest);
        var createBody = await createBackupResponse.Content.ReadAsStringAsync();
        Assert.True(
            createBackupResponse.StatusCode == HttpStatusCode.OK,
            $"Create backup failed ({(int)createBackupResponse.StatusCode}): {createBody}");

        var createPayload = JsonSerializer.Deserialize<BackupCreateApiResponse>(
            createBody,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(createPayload);
        Assert.False(string.IsNullOrWhiteSpace(createPayload!.FileName));

        await UpdateClientNameAsync(clientId, mutatedName);
        Assert.Equal(mutatedName, await GetClientNameAsync(clientId));

        var restoreRequest = CreateAdminRequest(
            HttpMethod.Post,
            "/api/admin/backups/restore",
            new
            {
                fileName = createPayload.FileName,
                includeUploads = false,
                dryRun = false
            });

        var restoreResponse = await _client.SendAsync(restoreRequest);
        var restoreBody = await restoreResponse.Content.ReadAsStringAsync();
        Assert.True(
            restoreResponse.StatusCode == HttpStatusCode.OK,
            $"Restore backup failed ({(int)restoreResponse.StatusCode}): {restoreBody}");

        var restorePayload = JsonSerializer.Deserialize<BackupRestoreApiResponse>(
            restoreBody,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(restorePayload);
        Assert.True(restorePayload!.Success);

        var restoredName = await GetClientNameAsync(clientId);
        Assert.Equal(baselineName, restoredName);
    }

    [Fact]
    public async Task CreateBackupFailsWhenEncryptionEnabledAndKeyMissing()
    {
        using var encryptedFactory = new BackupE2eApplicationFactory(encryptBackups: true, backupEncryptionKey: null, tenantSlug: null);
        using var encryptedClient = encryptedFactory.CreateClient();

        var createBackupRequest = new HttpRequestMessage(HttpMethod.Post, "/api/admin/backups")
        {
            Content = JsonContent.Create(new { includeUploads = false })
        };
        createBackupRequest.Headers.Add("X-Test-UserId", "backup-admin");
        createBackupRequest.Headers.Add("X-Test-Role", "Admin");

        var createBackupResponse = await encryptedClient.SendAsync(createBackupRequest);
        var createBody = await createBackupResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, createBackupResponse.StatusCode);
        Assert.Contains("encryption key", createBody, StringComparison.OrdinalIgnoreCase);

        var listBackupsRequest = new HttpRequestMessage(HttpMethod.Get, "/api/admin/backups");
        listBackupsRequest.Headers.Add("X-Test-UserId", "backup-admin");
        listBackupsRequest.Headers.Add("X-Test-Role", "Admin");

        var listBackupsResponse = await encryptedClient.SendAsync(listBackupsRequest);
        var listBody = await listBackupsResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, listBackupsResponse.StatusCode);

        var backups = JsonSerializer.Deserialize<List<BackupListApiResponse>>(
            listBody,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<BackupListApiResponse>();
        Assert.Empty(backups);
    }

    private HttpRequestMessage CreateAdminRequest(HttpMethod method, string url, object? payload = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-Test-UserId", "backup-admin");
        request.Headers.Add("X-Test-Role", "Admin");
        if (payload != null)
        {
            request.Content = JsonContent.Create(payload);
        }

        return request;
    }

    private async Task SeedClientAsync(string clientId, string name, string email)
    {
        var (tenantId, tenantSlug) = await GetTenantAsync();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
        tenantContext.Set(tenantId, tenantSlug);

        db.Clients.Add(new Client
        {
            Id = clientId,
            Name = name,
            Email = email,
            Type = "Individual",
            Status = "Active"
        });
        await db.SaveChangesAsync();
    }

    private async Task UpdateClientNameAsync(string clientId, string name)
    {
        var (tenantId, tenantSlug) = await GetTenantAsync();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
        tenantContext.Set(tenantId, tenantSlug);

        var client = await db.Clients.SingleAsync(c => c.Id == clientId);
        client.Name = name;
        client.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    private async Task<string> GetClientNameAsync(string clientId)
    {
        var (tenantId, tenantSlug) = await GetTenantAsync();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
        tenantContext.Set(tenantId, tenantSlug);

        var client = await db.Clients.AsNoTracking().SingleAsync(c => c.Id == clientId);
        return client.Name;
    }

    private async Task<(string TenantId, string TenantSlug)> GetTenantAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var tenant = await db.Tenants.AsNoTracking()
            .SingleAsync(t => t.Slug == BackupE2eApplicationFactory.TestTenantSlug);

        return (tenant.Id, tenant.Slug);
    }

    private sealed class BackupCreateApiResponse
    {
        public string FileName { get; set; } = string.Empty;
    }

    private sealed class BackupRestoreApiResponse
    {
        public bool Success { get; set; }
    }

    private sealed class BackupListApiResponse
    {
        public string FileName { get; set; } = string.Empty;
    }
}

public class BackupE2eApplicationFactory : WebApplicationFactory<Program>
{
    public const string TestTenantSlug = "backup-e2e";
    private readonly string _tenantSlug;
    private readonly string _tempContentRoot = Path.Combine(
        Path.GetTempPath(),
        "jurisflow-backup-e2e",
        Guid.NewGuid().ToString("N"));
    private readonly Dictionary<string, string?> _originalEnvironmentValues = new();

    public BackupE2eApplicationFactory()
        : this(false, null, null)
    {
    }

    internal BackupE2eApplicationFactory(
        bool encryptBackups,
        string? backupEncryptionKey,
        string? tenantSlug)
    {
        _tenantSlug = string.IsNullOrWhiteSpace(tenantSlug) ? TestTenantSlug : tenantSlug;
        Directory.CreateDirectory(_tempContentRoot);
        var dbFileName = $"jurisflow-backup-e2e-{Guid.NewGuid():N}.db";

        SetEnv("ConnectionStrings__DefaultConnection", $"Data Source={dbFileName}");
        SetEnv("Jwt__Key", "test-jwt-key-should-be-long-enough-32chars");
        SetEnv("Jwt__Issuer", "JurisFlowServer");
        SetEnv("Jwt__Audience", "JurisFlowClient");
        SetEnv("Security__DisableSessionValidation", "true");
        SetEnv("Seed__Enabled", "false");
        SetEnv("Backup__EncryptBackups", encryptBackups ? "true" : "false");
        SetEnv("Backup__EncryptionKey", backupEncryptionKey ?? string.Empty);
        SetEnv("Backup__AllowRestore", "true");
        SetEnv("Tenancy__DefaultTenantName", "Backup E2E Firm");
        SetEnv("Tenancy__DefaultTenantSlug", _tenantSlug);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseContentRoot(_tempContentRoot);

        builder.ConfigureServices(services =>
        {
            services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = "BackupTest";
                    options.DefaultChallengeScheme = "BackupTest";
                })
                .AddScheme<AuthenticationSchemeOptions, BackupTestAuthHandler>("BackupTest", _ => { });

            var hostedServices = services
                .Where(d => d.ServiceType == typeof(IHostedService))
                .ToList();

            foreach (var hostedDescriptor in hostedServices)
            {
                if (hostedDescriptor.ImplementationType == typeof(RetentionHostedService)
                    || hostedDescriptor.ImplementationType == typeof(DeadlineReminderHostedService)
                    || hostedDescriptor.ImplementationType == typeof(OperationsJobHostedService)
                    || hostedDescriptor.ImplementationType == typeof(IntegrationSecretMaintenanceHostedService))
                {
                    services.Remove(hostedDescriptor);
                }
            }
        });
    }

    protected override void ConfigureClient(HttpClient client)
    {
        base.ConfigureClient(client);
        if (!client.DefaultRequestHeaders.Contains("X-Tenant-Slug"))
        {
            client.DefaultRequestHeaders.Add("X-Tenant-Slug", _tenantSlug);
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            try
            {
                if (Directory.Exists(_tempContentRoot))
                {
                    Directory.Delete(_tempContentRoot, true);
                }
            }
            catch
            {
                // Best-effort cleanup.
            }

            foreach (var item in _originalEnvironmentValues)
            {
                Environment.SetEnvironmentVariable(item.Key, item.Value);
            }
        }
    }

    private void SetEnv(string key, string value)
    {
        if (!_originalEnvironmentValues.ContainsKey(key))
        {
            _originalEnvironmentValues[key] = Environment.GetEnvironmentVariable(key);
        }

        Environment.SetEnvironmentVariable(key, value);
    }
}

public class BackupTestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
#pragma warning disable CS0618
    public BackupTestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ISystemClock clock)
        : base(options, logger, encoder, clock)
    {
    }
#pragma warning restore CS0618

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Test-UserId", out var userId))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var role = Request.Headers.TryGetValue("X-Test-Role", out var roleHeader)
            ? roleHeader.ToString()
            : "Admin";

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Email, $"{userId}@example.com"),
            new Claim(ClaimTypes.Role, role),
            new Claim("role", role)
        };

        if (Request.Headers.TryGetValue("X-Test-TenantId", out var tenantId) &&
            !string.IsNullOrWhiteSpace(tenantId.ToString()))
        {
            claims.Add(new Claim("tenantId", tenantId.ToString()));
        }

        if (Request.Headers.TryGetValue("X-Test-TenantSlug", out var tenantSlug) &&
            !string.IsNullOrWhiteSpace(tenantSlug.ToString()))
        {
            claims.Add(new Claim("tenantSlug", tenantSlug.ToString()));
        }

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
