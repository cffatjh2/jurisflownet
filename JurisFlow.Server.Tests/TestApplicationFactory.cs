using System.Net.Http;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Task = System.Threading.Tasks.Task;
using System.Linq;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using JurisFlow.Server.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JurisFlow.Server.Tests;

public class TestApplicationFactory : WebApplicationFactory<Program>
{
    public const string TestTenantId = "tenant-test-1";
    public const string TestTenantSlug = "test-firm";
    public const string SecondaryTenantId = "tenant-test-2";
    public const string SecondaryTenantSlug = "other-firm";
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _connection.Open();

        builder.ConfigureAppConfiguration((context, config) =>
        {
            var settings = new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "test-jwt-key-should-be-long-enough-32chars",
                ["Jwt:Issuer"] = "JurisFlowServer",
                ["Jwt:Audience"] = "JurisFlowClient",
                ["Security:DisableSessionValidation"] = "true",
                ["Seed:Enabled"] = "false",
                ["Intake:AutoCreateLeadOnPublicSubmit"] = "true",
                ["Email:Enabled"] = "true",
                ["Email:FromAddress"] = "no-reply@test.example"
            };
            config.AddInMemoryCollection(settings);
        });

        builder.ConfigureServices(services =>
        {
            services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = "Test";
                    options.DefaultChallengeScheme = "Test";
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });

            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<JurisFlowDbContext>));
            if (descriptor != null)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<JurisFlowDbContext>(options =>
                options.UseSqlite(_connection));

            var hostedServices = services
                .Where(d => d.ServiceType == typeof(IHostedService))
                .ToList();

            foreach (var hostedDescriptor in hostedServices)
            {
                if (hostedDescriptor.ImplementationType == typeof(RetentionHostedService)
                    || hostedDescriptor.ImplementationType == typeof(DeadlineReminderHostedService)
                    || hostedDescriptor.ImplementationType == typeof(OperationsJobHostedService)
                    || hostedDescriptor.ImplementationType == typeof(IntegrationSecretMaintenanceHostedService)
                    || hostedDescriptor.ImplementationType == typeof(AuditLogWriteHostedService)
                    || hostedDescriptor.ImplementationType == typeof(TaskDomainOutboxHostedService))
                {
                    services.Remove(hostedDescriptor);
                }
            }

            var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
            db.Database.EnsureCreated();

            if (!db.Tenants.Any(t => t.Id == TestTenantId))
            {
                db.Tenants.Add(new Tenant
                {
                    Id = TestTenantId,
                    Name = "Test Firm",
                    Slug = TestTenantSlug,
                    IsActive = true
                });
            }

            if (!db.Tenants.Any(t => t.Id == SecondaryTenantId))
            {
                db.Tenants.Add(new Tenant
                {
                    Id = SecondaryTenantId,
                    Name = "Other Firm",
                    Slug = SecondaryTenantSlug,
                    IsActive = true
                });
            }

            db.SaveChanges();
        });
    }

    protected override void ConfigureClient(HttpClient client)
    {
        base.ConfigureClient(client);
        if (!client.DefaultRequestHeaders.Contains("X-Tenant-Slug"))
        {
            client.DefaultRequestHeaders.Add("X-Tenant-Slug", TestTenantSlug);
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection.Close();
        }
    }
}

public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    #pragma warning disable CS0618
    public TestAuthHandler(
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
            : "Associate";

        var tenantId = Request.Headers.TryGetValue("X-Test-TenantId", out var tenantHeader)
            ? tenantHeader.ToString()
            : TestApplicationFactory.TestTenantId;

        var tenantSlug = Request.Headers.TryGetValue("X-Test-TenantSlug", out var tenantSlugHeader)
            ? tenantSlugHeader.ToString()
            : TestApplicationFactory.TestTenantSlug;

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Email, $"{userId}@example.com"),
            new Claim(ClaimTypes.Role, role),
            new Claim("role", role),
            new Claim("tenantId", tenantId),
            new Claim("tenantSlug", tenantSlug)
        };

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
