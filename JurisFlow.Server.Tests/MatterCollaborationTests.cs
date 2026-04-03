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

public class MatterCollaborationTests : IClassFixture<TestApplicationFactory>
{
    private readonly TestApplicationFactory _factory;
    private readonly HttpClient _client;

    public MatterCollaborationTests(TestApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string url, string userId, string role, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-Test-UserId", userId);
        request.Headers.Add("X-Test-Role", role);
        if (content != null)
        {
            request.Content = content;
        }

        return request;
    }

    private async Task SeedAsync(Func<JurisFlowDbContext, Task> seed)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
        tenantContext.Set(TestApplicationFactory.TestTenantId, TestApplicationFactory.TestTenantSlug);
        await db.Database.EnsureCreatedAsync();
        await seed(db);
    }

    [Fact]
    public async Task SecondaryLinkedClientCanSeeMatterAndMatterEvents()
    {
        var primaryClientId = Guid.NewGuid().ToString();
        var secondaryClientId = Guid.NewGuid().ToString();
        var matterId = Guid.NewGuid().ToString();
        var eventId = Guid.NewGuid().ToString();

        await SeedAsync(async db =>
        {
            db.Clients.AddRange(
                new Client
                {
                    Id = primaryClientId,
                    Name = "Primary Client",
                    Email = $"primary-{Guid.NewGuid():N}@example.com",
                    NormalizedEmail = $"primary-{Guid.NewGuid():N}@example.com",
                    Type = "Individual",
                    Status = "Active",
                    PortalEnabled = true
                },
                new Client
                {
                    Id = secondaryClientId,
                    Name = "Secondary Client",
                    Email = $"secondary-{Guid.NewGuid():N}@example.com",
                    NormalizedEmail = $"secondary-{Guid.NewGuid():N}@example.com",
                    Type = "Individual",
                    Status = "Active",
                    PortalEnabled = true
                });

            db.Matters.Add(new Matter
            {
                Id = matterId,
                CaseNumber = "CASE-2001",
                Name = "Shared Matter",
                PracticeArea = "Civil Litigation",
                Status = "Open",
                FeeStructure = "Hourly",
                ResponsibleAttorney = "Attorney User",
                ClientId = primaryClientId,
                CreatedByUserId = "attorney-1"
            });

            db.MatterClientLinks.Add(new MatterClientLink
            {
                Id = Guid.NewGuid().ToString(),
                MatterId = matterId,
                ClientId = secondaryClientId
            });

            db.CalendarEvents.Add(new CalendarEvent
            {
                Id = eventId,
                Title = "Case Strategy Meeting",
                Date = DateTime.UtcNow.AddDays(2),
                Type = "Meeting",
                MatterId = matterId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync();
        });

        var request = CreateRequest(HttpMethod.Get, "/api/client/matters", secondaryClientId, "Client");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var matters = document.RootElement.EnumerateArray().ToList();
        Assert.Single(matters);
        Assert.Equal(matterId, matters[0].GetProperty("id").GetString());

        var events = matters[0].GetProperty("events").EnumerateArray().ToList();
        Assert.Single(events);
        Assert.Equal(eventId, events[0].GetProperty("id").GetString());
        Assert.Equal("Case Strategy Meeting", events[0].GetProperty("title").GetString());
    }

    [Fact]
    public async Task AttorneyCanCreateMatterWithoutSecondaryClients()
    {
        var clientId = Guid.NewGuid().ToString();

        await SeedAsync(async db =>
        {
            db.Clients.Add(new Client
            {
                Id = clientId,
                Name = "Matter Create Client",
                Email = $"matter-create-{Guid.NewGuid():N}@example.com",
                NormalizedEmail = $"matter-create-{Guid.NewGuid():N}@example.com",
                Type = "Individual",
                Status = "Active"
            });

            await db.SaveChangesAsync();
        });

        var payload = JsonContent.Create(new
        {
            caseNumber = "CASE-NEW-1001",
            name = "New Matter",
            practiceArea = "Civil Litigation",
            status = "Open",
            feeStructure = "Hourly",
            responsibleAttorney = "Fatih Alpaslan",
            clientId,
            billableRate = 400
        });

        var request = CreateRequest(HttpMethod.Post, "/api/matters", "attorney-create-1", "Attorney", payload);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
        tenantContext.Set(TestApplicationFactory.TestTenantId, TestApplicationFactory.TestTenantSlug);

        var matter = await db.Matters.AsNoTracking().FirstOrDefaultAsync(m => m.CaseNumber == "CASE-NEW-1001");
        Assert.NotNull(matter);
        Assert.Equal(clientId, matter!.ClientId);
        Assert.Equal("attorney-create-1", matter.CreatedByUserId);
    }

    [Fact]
    public async Task SecondaryLinkedClientCanCreateAppointmentForSharedMatter()
    {
        var primaryClientId = Guid.NewGuid().ToString();
        var secondaryClientId = Guid.NewGuid().ToString();
        var matterId = Guid.NewGuid().ToString();

        await SeedAsync(async db =>
        {
            db.Clients.AddRange(
                new Client
                {
                    Id = primaryClientId,
                    Name = "Primary Client",
                    Email = $"appt-primary-{Guid.NewGuid():N}@example.com",
                    NormalizedEmail = $"appt-primary-{Guid.NewGuid():N}@example.com",
                    Type = "Individual",
                    Status = "Active",
                    PortalEnabled = true
                },
                new Client
                {
                    Id = secondaryClientId,
                    Name = "Secondary Client",
                    Email = $"appt-secondary-{Guid.NewGuid():N}@example.com",
                    NormalizedEmail = $"appt-secondary-{Guid.NewGuid():N}@example.com",
                    Type = "Individual",
                    Status = "Active",
                    PortalEnabled = true
                });

            db.Matters.Add(new Matter
            {
                Id = matterId,
                CaseNumber = "CASE-2002",
                Name = "Appointment Matter",
                PracticeArea = "Civil Litigation",
                Status = "Open",
                FeeStructure = "Hourly",
                ResponsibleAttorney = "Attorney User",
                ClientId = primaryClientId,
                CreatedByUserId = "attorney-1"
            });

            db.MatterClientLinks.Add(new MatterClientLink
            {
                Id = Guid.NewGuid().ToString(),
                MatterId = matterId,
                ClientId = secondaryClientId
            });

            await db.SaveChangesAsync();
        });

        var payload = JsonContent.Create(new
        {
            matterId,
            requestedDate = DateTime.UtcNow.AddDays(1),
            duration = 30,
            type = "meeting",
            notes = "Secondary client appointment"
        });

        var request = CreateRequest(HttpMethod.Post, "/api/client/appointments", secondaryClientId, "Client", payload);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
        tenantContext.Set(TestApplicationFactory.TestTenantId, TestApplicationFactory.TestTenantSlug);

        var appointment = await db.AppointmentRequests.AsNoTracking().FirstOrDefaultAsync(a => a.ClientId == secondaryClientId && a.MatterId == matterId);
        Assert.NotNull(appointment);
        Assert.Equal("pending", appointment!.Status);
    }

    [Fact]
    public async Task MatterNotesCrudWorksForMatterOwner()
    {
        var matterId = Guid.NewGuid().ToString();
        const string attorneyUserId = "attorney-notes-1";

        await SeedAsync(async db =>
        {
            var clientId = Guid.NewGuid().ToString();
            db.Clients.Add(new Client
            {
                Id = clientId,
                Name = "Notes Client",
                Email = $"notes-{Guid.NewGuid():N}@example.com",
                NormalizedEmail = $"notes-{Guid.NewGuid():N}@example.com",
                Type = "Individual",
                Status = "Active"
            });

            db.Matters.Add(new Matter
            {
                Id = matterId,
                CaseNumber = "CASE-2003",
                Name = "Notes Matter",
                PracticeArea = "Civil Litigation",
                Status = "Open",
                FeeStructure = "Hourly",
                ResponsibleAttorney = "Attorney User",
                ClientId = clientId,
                CreatedByUserId = attorneyUserId
            });

            await db.SaveChangesAsync();
        });

        var createPayload = JsonContent.Create(new
        {
            title = "Hearing prep",
            body = "Outline witness questions and exhibits."
        });
        var createRequest = CreateRequest(HttpMethod.Post, $"/api/matters/{matterId}/notes", attorneyUserId, "Attorney", createPayload);
        var createResponse = await _client.SendAsync(createRequest);

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        using var createDocument = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var noteId = createDocument.RootElement.GetProperty("id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(noteId));

        var updatePayload = JsonContent.Create(new
        {
            title = "Hearing prep",
            body = "Outline witness questions, exhibits, and settlement posture."
        });
        var updateRequest = CreateRequest(HttpMethod.Put, $"/api/matters/{matterId}/notes/{noteId}", attorneyUserId, "Attorney", updatePayload);
        var updateResponse = await _client.SendAsync(updateRequest);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var listRequest = CreateRequest(HttpMethod.Get, $"/api/matters/{matterId}/notes", attorneyUserId, "Attorney");
        var listResponse = await _client.SendAsync(listRequest);
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        using var listDocument = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
        var notes = listDocument.RootElement.EnumerateArray().ToList();
        Assert.Single(notes);
        Assert.Equal("Hearing prep", notes[0].GetProperty("title").GetString());
        Assert.Contains("settlement posture", notes[0].GetProperty("body").GetString(), StringComparison.Ordinal);

        var deleteRequest = CreateRequest(HttpMethod.Delete, $"/api/matters/{matterId}/notes/{noteId}", attorneyUserId, "Attorney");
        var deleteResponse = await _client.SendAsync(deleteRequest);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
        tenantContext.Set(TestApplicationFactory.TestTenantId, TestApplicationFactory.TestTenantSlug);

        var remainingNotes = await db.MatterNotes.AsNoTracking().Where(n => n.MatterId == matterId).ToListAsync();
        Assert.Empty(remainingNotes);
    }
}
