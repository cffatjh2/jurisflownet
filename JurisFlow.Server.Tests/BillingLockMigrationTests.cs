using System.Net;
using System.Net.Http.Json;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using JurisFlow.Server.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace JurisFlow.Server.Tests;

[Collection("SequentialDbTests")]
public class BillingLockMigrationTests : IClassFixture<TestApplicationFactory>
{
    private readonly TestApplicationFactory _factory;
    private readonly HttpClient _client;

    public BillingLockMigrationTests(TestApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task CreateBillingLock_PersistsDateRange_AndBillingPeriodServiceDetectsLock()
    {
        var seed = await SeedMatterWithUnbilledTimeEntryAsync("matter-owner", new DateTime(2026, 4, 25, 9, 0, 0, DateTimeKind.Utc));

        using var createLockResponse = await _client.SendAsync(CreateRequest(
            HttpMethod.Post,
            "/api/admin/billing-locks",
            "admin-user",
            "Admin",
            new
            {
                periodStart = "2026-04-20",
                periodEnd = "2026-04-30",
                notes = "month close"
            }));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
        var lockService = scope.ServiceProvider.GetRequiredService<BillingPeriodLockService>();
        tenantContext.Set(TestApplicationFactory.TestTenantId, TestApplicationFactory.TestTenantSlug);

        var persistedLock = await db.BillingLocks.AsNoTracking().SingleAsync();
        var isLocked = await lockService.IsLockedAsync(seed.ServiceDateUtc);

        Assert.Equal(HttpStatusCode.OK, createLockResponse.StatusCode);
        Assert.Equal(new DateTime(2026, 4, 20), persistedLock.PeriodStart);
        Assert.Equal(new DateTime(2026, 4, 30), persistedLock.PeriodEnd);
        Assert.True(isLocked);
    }

    [Fact]
    public void BillingLock_UsesDateColumns_AndSqlSideOverlapChecks()
    {
        var dbContextPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "JurisFlow.Server",
            "Data",
            "JurisFlowDbContext.cs"));
        var billingControllerPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "JurisFlow.Server",
            "Controllers",
            "BillingController.cs"));
        var adminControllerPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "JurisFlow.Server",
            "Controllers",
            "AdminController.cs"));

        var dbContextSource = File.ReadAllText(dbContextPath);
        var billingControllerSource = File.ReadAllText(billingControllerPath);
        var adminControllerSource = File.ReadAllText(adminControllerPath);

        Assert.Contains(".Property(b => b.PeriodStart)", dbContextSource, StringComparison.Ordinal);
        Assert.Contains(".HasColumnType(\"date\")", dbContextSource, StringComparison.Ordinal);
        Assert.Contains(".HasIndex(\"TenantId\", nameof(BillingLock.PeriodStart), nameof(BillingLock.PeriodEnd))", dbContextSource, StringComparison.Ordinal);

        Assert.DoesNotContain(".Select(b => new { b.PeriodStart, b.PeriodEnd })", billingControllerSource, StringComparison.Ordinal);
        Assert.Contains(".Distinct()", billingControllerSource, StringComparison.Ordinal);
        Assert.Contains("AnyAsync(b => b.PeriodStart <= day && b.PeriodEnd >= day)", billingControllerSource, StringComparison.Ordinal);
        Assert.Contains("AnyAsync(b =>", adminControllerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("string.Compare(b.PeriodStart", adminControllerSource, StringComparison.Ordinal);
    }

    private async Task<SeededBillingLockObjects> SeedMatterWithUnbilledTimeEntryAsync(string ownerUserId, DateTime serviceDateUtc)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
        tenantContext.Set(TestApplicationFactory.TestTenantId, TestApplicationFactory.TestTenantSlug);
        await db.Database.EnsureCreatedAsync();

        var suffix = Guid.NewGuid().ToString("N");
        var clientId = $"client-lock-{suffix}";
        var matterId = $"matter-lock-{suffix}";
        var timeEntryId = $"time-lock-{suffix}";
        var email = $"lock-{suffix}@example.com";

        db.Clients.Add(new Client
        {
            Id = clientId,
            Name = "Billing Lock Client",
            Email = email,
            NormalizedEmail = EmailAddressNormalizer.Normalize(email),
            Type = "Individual",
            Status = "Active"
        });

        db.Matters.Add(new Matter
        {
            Id = matterId,
            CaseNumber = $"LOCK-{suffix[..8]}",
            Name = "Billing Lock Matter",
            PracticeArea = "CivilLitigation",
            Status = "Open",
            FeeStructure = "Hourly",
            ResponsibleAttorney = ownerUserId,
            ClientId = clientId,
            CreatedByUserId = ownerUserId,
            ShareWithFirm = false,
            ShareBillingWithFirm = false
        });

        db.TimeEntries.Add(new TimeEntry
        {
            Id = timeEntryId,
            MatterId = matterId,
            Date = serviceDateUtc,
            Description = "Locked period work",
            Duration = 90,
            Rate = 100d,
            IsBillable = true,
            Billed = false,
            SubmittedBy = ownerUserId
        });

        await db.SaveChangesAsync();
        return new SeededBillingLockObjects(matterId, serviceDateUtc);
    }

    private static HttpRequestMessage CreateRequest(
        HttpMethod method,
        string url,
        string userId,
        string role,
        object? payload = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-Test-UserId", userId);
        request.Headers.Add("X-Test-Role", role);
        request.Headers.Add("X-Test-TenantId", TestApplicationFactory.TestTenantId);
        request.Headers.Add("X-Test-TenantSlug", TestApplicationFactory.TestTenantSlug);
        request.Headers.Add("X-Tenant-Slug", TestApplicationFactory.TestTenantSlug);

        if (payload != null)
        {
            request.Content = JsonContent.Create(payload);
        }

        return request;
    }

    private sealed record SeededBillingLockObjects(string MatterId, DateTime ServiceDateUtc);
}
