using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using JurisFlow.Server.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Task = System.Threading.Tasks.Task;

namespace JurisFlow.Server.Tests;

public class TenantIsolationIntegrationTests : IClassFixture<TestApplicationFactory>
{
    private readonly TestApplicationFactory _factory;
    private readonly HttpClient _client;

    public TenantIsolationIntegrationTests(TestApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task BootstrapDeferredScope_ExcludesCrmReadModels_AndReadModelEndpointsRemainTenantScoped()
    {
        var primaryLeadId = Guid.NewGuid().ToString();
        var primaryClientId = Guid.NewGuid().ToString();
        var secondaryLeadId = Guid.NewGuid().ToString();
        var secondaryClientId = Guid.NewGuid().ToString();

        await SeedAsync(TestApplicationFactory.TestTenantId, TestApplicationFactory.TestTenantSlug, async db =>
        {
            db.Leads.Add(new Lead
            {
                Id = primaryLeadId,
                Name = "Primary Tenant Lead",
                Email = $"lead-{primaryLeadId}@example.com",
                Source = "Referral",
                Status = "New",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            db.Clients.Add(new Client
            {
                Id = primaryClientId,
                Name = "Primary Tenant Client",
                Email = $"client-{primaryClientId}@example.com",
                NormalizedEmail = $"client-{primaryClientId}@example.com",
                Type = "Individual",
                Status = "Active",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync();
        });

        await SeedAsync(TestApplicationFactory.SecondaryTenantId, TestApplicationFactory.SecondaryTenantSlug, async db =>
        {
            db.Leads.Add(new Lead
            {
                Id = secondaryLeadId,
                Name = "Secondary Tenant Lead",
                Email = $"lead-{secondaryLeadId}@example.com",
                Source = "Referral",
                Status = "New",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            db.Clients.Add(new Client
            {
                Id = secondaryClientId,
                Name = "Secondary Tenant Client",
                Email = $"client-{secondaryClientId}@example.com",
                NormalizedEmail = $"client-{secondaryClientId}@example.com",
                Type = "Individual",
                Status = "Active",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync();
        });

        var request = CreateAuthenticatedRequest(
            HttpMethod.Get,
            "/api/bootstrap?scope=deferred",
            "bootstrap-user",
            "Partner",
            TestApplicationFactory.TestTenantId,
            TestApplicationFactory.TestTenantSlug);

        var response = await _client.SendAsync(request);
        var payload = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(payload);
        Assert.True(
            !document.RootElement.TryGetProperty("leads", out var leadsElement) ||
            leadsElement.ValueKind == JsonValueKind.Null ||
            (leadsElement.ValueKind == JsonValueKind.Array && leadsElement.GetArrayLength() == 0));
        Assert.True(
            !document.RootElement.TryGetProperty("clients", out var clientsElement) ||
            clientsElement.ValueKind == JsonValueKind.Null ||
            (clientsElement.ValueKind == JsonValueKind.Array && clientsElement.GetArrayLength() == 0));

        var leadsReadModelRequest = CreateAuthenticatedRequest(
            HttpMethod.Get,
            "/api/leads/read-model?page=1&pageSize=20",
            "bootstrap-user",
            "Partner",
            TestApplicationFactory.TestTenantId,
            TestApplicationFactory.TestTenantSlug);

        var leadsReadModelResponse = await _client.SendAsync(leadsReadModelRequest);
        var leadsReadModelPayload = await leadsReadModelResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, leadsReadModelResponse.StatusCode);

        using var leadsReadModelDocument = JsonDocument.Parse(leadsReadModelPayload);
        var leadIds = leadsReadModelDocument.RootElement.GetProperty("items")
            .EnumerateArray()
            .Select(item => item.GetProperty("id").GetString())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);

        var clientsReadModelRequest = CreateAuthenticatedRequest(
            HttpMethod.Get,
            "/api/clients/read-model?page=1&pageSize=20",
            "bootstrap-user",
            "Partner",
            TestApplicationFactory.TestTenantId,
            TestApplicationFactory.TestTenantSlug);

        var clientsReadModelResponse = await _client.SendAsync(clientsReadModelRequest);
        var clientsReadModelPayload = await clientsReadModelResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, clientsReadModelResponse.StatusCode);

        using var clientsReadModelDocument = JsonDocument.Parse(clientsReadModelPayload);
        var clientIds = clientsReadModelDocument.RootElement.GetProperty("items")
            .EnumerateArray()
            .Select(item => item.GetProperty("id").GetString())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains(primaryLeadId, leadIds);
        Assert.DoesNotContain(secondaryLeadId, leadIds);
        Assert.Contains(primaryClientId, clientIds);
        Assert.DoesNotContain(secondaryClientId, clientIds);
    }

    [Fact]
    public async Task LeadEndpoints_RejectCrossTenantReadAndArchive()
    {
        var secondaryLeadId = Guid.NewGuid().ToString();

        await SeedAsync(TestApplicationFactory.SecondaryTenantId, TestApplicationFactory.SecondaryTenantSlug, async db =>
        {
            db.Leads.Add(new Lead
            {
                Id = secondaryLeadId,
                Name = "Hidden Secondary Lead",
                Email = $"lead-{secondaryLeadId}@example.com",
                Source = "Website",
                Status = "Contacted",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync();
        });

        var getRequest = CreateAuthenticatedRequest(
            HttpMethod.Get,
            $"/api/leads/{secondaryLeadId}",
            "primary-user",
            "Partner",
            TestApplicationFactory.TestTenantId,
            TestApplicationFactory.TestTenantSlug);

        var getResponse = await _client.SendAsync(getRequest);
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);

        var deleteRequest = CreateAuthenticatedRequest(
            HttpMethod.Delete,
            $"/api/leads/{secondaryLeadId}",
            "primary-user",
            "Partner",
            TestApplicationFactory.TestTenantId,
            TestApplicationFactory.TestTenantSlug);

        var deleteResponse = await _client.SendAsync(deleteRequest);
        Assert.Equal(HttpStatusCode.NotFound, deleteResponse.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
        tenantContext.Set(TestApplicationFactory.SecondaryTenantId, TestApplicationFactory.SecondaryTenantSlug);

        var lead = await db.Leads.IgnoreQueryFilters().SingleAsync(l => l.Id == secondaryLeadId);
        Assert.False(lead.IsArchived);
    }

    [Fact]
    public async Task ClientReadModelEndpoint_PaginatesAndReturnsProjectionMetadata()
    {
        await SeedAsync(TestApplicationFactory.TestTenantId, TestApplicationFactory.TestTenantSlug, async db =>
        {
            db.Clients.AddRange(
                new Client
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = "Paged Client A",
                    Email = $"client-a-{Guid.NewGuid():N}@example.com",
                    NormalizedEmail = $"client-a-{Guid.NewGuid():N}@example.com",
                    Type = "Individual",
                    Status = "Active",
                    CreatedAt = DateTime.UtcNow.AddMinutes(-2),
                    UpdatedAt = DateTime.UtcNow.AddMinutes(-2)
                },
                new Client
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = "Paged Client B",
                    Email = $"client-b-{Guid.NewGuid():N}@example.com",
                    NormalizedEmail = $"client-b-{Guid.NewGuid():N}@example.com",
                    Type = "Corporate",
                    Status = "Inactive",
                    CreatedAt = DateTime.UtcNow.AddMinutes(-1),
                    UpdatedAt = DateTime.UtcNow.AddMinutes(-1)
                });

            await db.SaveChangesAsync();
        });

        var request = CreateAuthenticatedRequest(
            HttpMethod.Get,
            "/api/clients/read-model?page=1&pageSize=1",
            "paged-user",
            "Partner",
            TestApplicationFactory.TestTenantId,
            TestApplicationFactory.TestTenantSlug);

        var response = await _client.SendAsync(request);
        var payload = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(payload);
        Assert.Equal(1, document.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(1, document.RootElement.GetProperty("pageSize").GetInt32());
        Assert.True(document.RootElement.GetProperty("totalCount").GetInt32() >= 2);
        Assert.True(document.RootElement.GetProperty("hasMore").GetBoolean());
        Assert.Single(document.RootElement.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task PublicIntakeSubmit_CreatesLeadWithinCurrentTenant_WhenAnotherTenantHasSameEmail()
    {
        var sharedEmail = $"shared-{Guid.NewGuid():N}@example.com";
        var secondaryLeadId = Guid.NewGuid().ToString();
        var primaryFormSlug = $"public-intake-{Guid.NewGuid():N}";

        await SeedAsync(TestApplicationFactory.SecondaryTenantId, TestApplicationFactory.SecondaryTenantSlug, async db =>
        {
            db.Leads.Add(new Lead
            {
                Id = secondaryLeadId,
                Name = "Secondary Existing Lead",
                Email = sharedEmail,
                Source = "Referral",
                CreatedBySource = "Manual",
                Status = "Contacted",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync();
        });

        await SeedAsync(TestApplicationFactory.TestTenantId, TestApplicationFactory.TestTenantSlug, async db =>
        {
            db.IntakeForms.Add(new IntakeForm
            {
                Id = Guid.NewGuid().ToString(),
                Name = "Primary Public Intake",
                Slug = primaryFormSlug,
                IsActive = true,
                IsPublic = true,
                PracticeArea = "Civil Litigation",
                FieldsJson = """
                [
                  { "name": "name", "label": "Name", "type": "text", "required": true, "order": 1 },
                  { "name": "email", "label": "Email", "type": "email", "required": true, "order": 2 }
                ]
                """
            });

            await db.SaveChangesAsync();
        });

        var submitPayload = JsonContent.Create(new
        {
            dataJson = JsonSerializer.Serialize(new
            {
                name = "Primary Tenant Intake",
                email = sharedEmail
            })
        });

        var request = CreateAnonymousRequest(
            HttpMethod.Post,
            $"/api/intake/public/{primaryFormSlug}/submit",
            TestApplicationFactory.TestTenantSlug,
            submitPayload);

        var response = await _client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();

        tenantContext.Set(TestApplicationFactory.TestTenantId, TestApplicationFactory.TestTenantSlug);
        var primaryLead = await db.Leads.SingleAsync(l => l.NormalizedEmail == sharedEmail);
        var primarySubmission = await db.IntakeSubmissions.SingleAsync(s => s.LeadId == primaryLead.Id);

        Assert.Equal("Intake", primaryLead.CreatedBySource);
        Assert.Equal("Converted", primarySubmission.Status);
        Assert.NotEqual(secondaryLeadId, primaryLead.Id);

        tenantContext.Set(TestApplicationFactory.SecondaryTenantId, TestApplicationFactory.SecondaryTenantSlug);
        var secondaryLead = await db.Leads.SingleAsync(l => l.Id == secondaryLeadId);

        Assert.Equal(sharedEmail, secondaryLead.NormalizedEmail);
        Assert.Equal("Contacted", secondaryLead.Status);
    }

    [Fact]
    public async Task ConflictEndpoints_RedactStoredSearchQueryInResponses()
    {
        var clientEmail = $"sensitive-{Guid.NewGuid():N}@example.com";

        await SeedAsync(TestApplicationFactory.TestTenantId, TestApplicationFactory.TestTenantSlug, async db =>
        {
            db.Clients.Add(new Client
            {
                Id = Guid.NewGuid().ToString(),
                Name = "Sensitive Conflict Client",
                Email = clientEmail,
                NormalizedEmail = clientEmail,
                Type = "Individual",
                Status = "Active",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync();
        });

        var runRequest = CreateAuthenticatedRequest(
            HttpMethod.Post,
            "/api/conflicts/check",
            "conflict-user",
            "Partner",
            TestApplicationFactory.TestTenantId,
            TestApplicationFactory.TestTenantSlug,
            JsonContent.Create(new
            {
                searchQuery = clientEmail,
                checkType = "Manual"
            }));

        var runResponse = await _client.SendAsync(runRequest);
        var runPayload = await runResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, runResponse.StatusCode);

        using var runDocument = JsonDocument.Parse(runPayload);
        var checkId = runDocument.RootElement.GetProperty("id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(checkId));

        var detailRequest = CreateAuthenticatedRequest(
            HttpMethod.Get,
            $"/api/conflicts/{checkId}",
            "conflict-user",
            "Partner",
            TestApplicationFactory.TestTenantId,
            TestApplicationFactory.TestTenantSlug);

        var detailResponse = await _client.SendAsync(detailRequest);
        var detailPayload = await detailResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);

        using var detailDocument = JsonDocument.Parse(detailPayload);
        var redactedQuery = detailDocument.RootElement.GetProperty("searchQuery").GetString();
        Assert.NotEqual(clientEmail, redactedQuery);
        Assert.Contains("***", redactedQuery ?? string.Empty);

        var historyRequest = CreateAuthenticatedRequest(
            HttpMethod.Get,
            "/api/conflicts/history?limit=10",
            "conflict-user",
            "Partner",
            TestApplicationFactory.TestTenantId,
            TestApplicationFactory.TestTenantSlug);

        var historyResponse = await _client.SendAsync(historyRequest);
        var historyPayload = await historyResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);

        using var historyDocument = JsonDocument.Parse(historyPayload);
        var historySearchQueries = historyDocument.RootElement
            .EnumerateArray()
            .Select(item => item.GetProperty("searchQuery").GetString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();

        Assert.Contains(historySearchQueries, value => value!.Contains("***", StringComparison.Ordinal));
        Assert.DoesNotContain(historySearchQueries, value => string.Equals(value, clientEmail, StringComparison.Ordinal));
    }

    [Fact]
    public async Task PublicIntakeSubmit_MinimizesStoredTelemetry()
    {
        var formSlug = $"minimized-intake-{Guid.NewGuid():N}";
        const string rawUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 CustomBrowser/124.1 ExtraToken";

        await SeedAsync(TestApplicationFactory.TestTenantId, TestApplicationFactory.TestTenantSlug, async db =>
        {
            db.IntakeForms.Add(new IntakeForm
            {
                Id = Guid.NewGuid().ToString(),
                Name = "Telemetry Minimization Form",
                Slug = formSlug,
                IsActive = true,
                IsPublic = true,
                FieldsJson = """
                [
                  { "name": "name", "label": "Name", "type": "text", "required": true, "order": 1 }
                ]
                """
            });

            await db.SaveChangesAsync();
        });

        var request = CreateAnonymousRequest(
            HttpMethod.Post,
            $"/api/intake/public/{formSlug}/submit",
            TestApplicationFactory.TestTenantSlug,
            JsonContent.Create(new
            {
                dataJson = JsonSerializer.Serialize(new
                {
                    name = "Telemetry Test"
                })
            }));
        request.Headers.UserAgent.ParseAdd(rawUserAgent);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
        tenantContext.Set(TestApplicationFactory.TestTenantId, TestApplicationFactory.TestTenantSlug);

        var submission = await db.IntakeSubmissions
            .OrderByDescending(s => s.CreatedAt)
            .FirstAsync();

        Assert.Equal("Mozilla/5.0 AppleWebKit/537.36 CustomBrowser/124.1", submission.UserAgent);
        Assert.NotEqual(rawUserAgent, submission.UserAgent);
    }

    [Fact]
    public async Task ArchivedLead_DoesNotBlockNewActiveLeadWithSameEmail()
    {
        var leadEmail = $"archived-lead-{Guid.NewGuid():N}@example.com";

        await SeedAsync(TestApplicationFactory.TestTenantId, TestApplicationFactory.TestTenantSlug, async db =>
        {
            db.Leads.Add(new Lead
            {
                Id = Guid.NewGuid().ToString(),
                Name = "Archived Lead",
                Email = leadEmail,
                NormalizedEmail = leadEmail,
                Source = "Referral",
                CreatedBySource = "Manual",
                Status = "Lost",
                IsArchived = true,
                ArchivedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow.AddDays(-2),
                UpdatedAt = DateTime.UtcNow.AddDays(-1)
            });

            await db.SaveChangesAsync();
        });

        var createRequest = CreateAuthenticatedRequest(
            HttpMethod.Post,
            "/api/leads",
            "lead-user",
            "Partner",
            TestApplicationFactory.TestTenantId,
            TestApplicationFactory.TestTenantSlug,
            JsonContent.Create(new
            {
                name = "Fresh Lead",
                email = leadEmail,
                status = "New",
                source = "Website"
            }));

        var createResponse = await _client.SendAsync(createRequest);
        var createPayload = await createResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
        tenantContext.Set(TestApplicationFactory.TestTenantId, TestApplicationFactory.TestTenantSlug);

        var matchingLeads = await db.Leads.IgnoreQueryFilters()
            .Where(l => l.NormalizedEmail == leadEmail)
            .OrderBy(l => l.CreatedAt)
            .ToListAsync();

        Assert.Equal(2, matchingLeads.Count);
        Assert.Single(matchingLeads, l => l.IsArchived);
        Assert.Single(matchingLeads, l => !l.IsArchived);
    }

    [Fact]
    public async Task IntakeFormSlug_IsUniquePerTenant()
    {
        var sharedSlug = $"shared-intake-{Guid.NewGuid():N}";

        await SeedAsync(TestApplicationFactory.TestTenantId, TestApplicationFactory.TestTenantSlug, async db =>
        {
            db.IntakeForms.Add(new IntakeForm
            {
                Id = Guid.NewGuid().ToString(),
                Name = "Primary Shared Slug",
                Slug = sharedSlug,
                FieldsJson = "[]",
                IsActive = true,
                IsPublic = true
            });

            await db.SaveChangesAsync();
        });

        await SeedAsync(TestApplicationFactory.SecondaryTenantId, TestApplicationFactory.SecondaryTenantSlug, async db =>
        {
            db.IntakeForms.Add(new IntakeForm
            {
                Id = Guid.NewGuid().ToString(),
                Name = "Secondary Shared Slug",
                Slug = sharedSlug,
                FieldsJson = "[]",
                IsActive = true,
                IsPublic = true
            });

            await db.SaveChangesAsync();
        });

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
        tenantContext.Set(TestApplicationFactory.TestTenantId, TestApplicationFactory.TestTenantSlug);

        db.IntakeForms.Add(new IntakeForm
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Duplicate Shared Slug",
            Slug = sharedSlug,
            FieldsJson = "[]",
            IsActive = true,
            IsPublic = true
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    private async Task SeedAsync(string tenantId, string tenantSlug, Func<JurisFlowDbContext, Task> seed)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
        tenantContext.Set(tenantId, tenantSlug);
        await db.Database.EnsureCreatedAsync();
        await seed(db);
    }

    private static HttpRequestMessage CreateAuthenticatedRequest(
        HttpMethod method,
        string url,
        string userId,
        string role,
        string tenantId,
        string tenantSlug,
        HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-Test-UserId", userId);
        request.Headers.Add("X-Test-Role", role);
        request.Headers.Add("X-Test-TenantId", tenantId);
        request.Headers.Add("X-Test-TenantSlug", tenantSlug);
        request.Headers.Add("X-Tenant-Slug", tenantSlug);
        if (content != null)
        {
            request.Content = content;
        }

        return request;
    }

    private static HttpRequestMessage CreateAnonymousRequest(
        HttpMethod method,
        string url,
        string tenantSlug,
        HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-Tenant-Slug", tenantSlug);
        if (content != null)
        {
            request.Content = content;
        }

        return request;
    }
}
