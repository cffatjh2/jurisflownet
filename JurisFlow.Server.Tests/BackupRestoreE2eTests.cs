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
            createBackupResponse.StatusCode == HttpStatusCode.Accepted,
            $"Create backup failed ({(int)createBackupResponse.StatusCode}): {createBody}");

        var createPayload = JsonSerializer.Deserialize<BackupJobApiResponse>(
            createBody,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(createPayload);
        Assert.False(string.IsNullOrWhiteSpace(createPayload!.JobId));

        var completedCreateJob = await WaitForBackupJobAsync(_client, createPayload.JobId);
        Assert.Equal("succeeded", completedCreateJob.Status);
        Assert.True(completedCreateJob.Result.HasValue);

        var backupFileName = completedCreateJob.Result.Value.GetProperty("fileName").GetString();
        Assert.False(string.IsNullOrWhiteSpace(backupFileName));

        await UpdateClientNameAsync(clientId, mutatedName);
        Assert.Equal(mutatedName, await GetClientNameAsync(clientId));

        var restoreRequest = CreateAdminRequest(
            HttpMethod.Post,
            "/api/admin/backups/restore",
            new
            {
                fileName = backupFileName,
                includeUploads = false,
                dryRun = false
            });
        restoreRequest.Headers.Add("X-Break-Glass-Confirm", "RESTORE");

        var restoreResponse = await _client.SendAsync(restoreRequest);
        var restoreBody = await restoreResponse.Content.ReadAsStringAsync();
        Assert.True(
            restoreResponse.StatusCode == HttpStatusCode.Accepted,
            $"Restore backup failed ({(int)restoreResponse.StatusCode}): {restoreBody}");

        var restorePayload = JsonSerializer.Deserialize<BackupJobApiResponse>(
            restoreBody,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(restorePayload);
        Assert.False(string.IsNullOrWhiteSpace(restorePayload!.JobId));

        var completedRestoreJob = await WaitForBackupJobAsync(_client, restorePayload.JobId);
        Assert.Equal("succeeded", completedRestoreJob.Status);
        Assert.True(completedRestoreJob.Result.HasValue);
        Assert.True(completedRestoreJob.Result.Value.GetProperty("success").GetBoolean());

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
        createBackupRequest.Headers.Add("X-Test-Role", "SecurityAdmin");

        var createBackupResponse = await encryptedClient.SendAsync(createBackupRequest);
        var createBody = await createBackupResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Accepted, createBackupResponse.StatusCode);

        var createPayload = JsonSerializer.Deserialize<BackupJobApiResponse>(
            createBody,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(createPayload);
        Assert.False(string.IsNullOrWhiteSpace(createPayload!.JobId));

        var completedCreateJob = await WaitForBackupJobAsync(encryptedClient, createPayload.JobId);
        Assert.Equal("failed", completedCreateJob.Status);
        Assert.Contains("could not be completed", completedCreateJob.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        var listBackupsRequest = new HttpRequestMessage(HttpMethod.Get, "/api/admin/backups");
        listBackupsRequest.Headers.Add("X-Test-UserId", "backup-admin");
        listBackupsRequest.Headers.Add("X-Test-Role", "SecurityAdmin");

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
        request.Headers.Add("X-Test-Role", "SecurityAdmin");
        if (payload != null)
        {
            request.Content = JsonContent.Create(payload);
        }

        return request;
    }

    private static async Task<BackupJobApiResponse> WaitForBackupJobAsync(HttpClient client, string jobId, int maxAttempts = 40)
    {
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/admin/backups/jobs/{jobId}");
            request.Headers.Add("X-Test-UserId", "backup-admin");
            request.Headers.Add("X-Test-Role", "SecurityAdmin");

            var response = await client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var payload = JsonSerializer.Deserialize<BackupJobApiResponse>(
                body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            Assert.NotNull(payload);

            if (string.Equals(payload!.Status, "succeeded", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(payload.Status, "failed", StringComparison.OrdinalIgnoreCase))
            {
                return payload;
            }

            await Task.Delay(250);
        }

        throw new TimeoutException($"Backup job '{jobId}' did not complete in time.");
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

    private sealed class BackupJobApiResponse
    {
        public string JobId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? Message { get; set; }
        public JsonElement? Result { get; set; }
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
                    || hostedDescriptor.ImplementationType == typeof(IntegrationSecretMaintenanceHostedService)
                    || hostedDescriptor.ImplementationType == typeof(TaskDomainOutboxHostedService))
                {
                    services.Remove(hostedDescriptor);
                }
            }

            var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
            db.Database.EnsureCreated();

            if (!db.Tenants.Any(t => t.Slug == _tenantSlug))
            {
                db.Tenants.Add(new Tenant
                {
                    Name = "Backup E2E Firm",
                    Slug = _tenantSlug,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
                db.SaveChanges();
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
