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

public class Phase3RefactorIntegrationTests : IClassFixture<TestApplicationFactory>
{
    private readonly TestApplicationFactory _factory;
    private readonly HttpClient _client;

    public Phase3RefactorIntegrationTests(TestApplicationFactory factory)
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
    public async Task ClientPortalPasswordManagementRequiresPrivilegedRole()
    {
        var clientId = Guid.NewGuid().ToString();

        await SeedAsync(async db =>
        {
            db.Clients.Add(new Client
            {
                Id = clientId,
                Name = "Portal Client",
                Email = $"portal-{Guid.NewGuid():N}@example.com",
                NormalizedEmail = $"portal-{Guid.NewGuid():N}@example.com",
                Type = "Individual",
                Status = "Active"
            });

            await db.SaveChangesAsync();
        });

        var payload = JsonContent.Create(new { password = "ValidPass123!X" });
        var request = CreateRequest(HttpMethod.Post, $"/api/clients/{clientId}/set-password", "associate-user", "Associate", payload);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task LeadDeleteRequiresPrivilegedRole()
    {
        var leadId = Guid.NewGuid().ToString();

        await SeedAsync(async db =>
        {
            db.Leads.Add(new Lead
            {
                Id = leadId,
                Name = "Delete Lead",
                Status = "New",
                Source = "Referral"
            });

            await db.SaveChangesAsync();
        });

        var request = CreateRequest(HttpMethod.Delete, $"/api/leads/{leadId}", "associate-lead", "Associate");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task TaskCreateUsesLegacyAssignmentAdapter()
    {
        var employeeId = Guid.NewGuid().ToString();

        await SeedAsync(async db =>
        {
            db.Employees.Add(new Employee
            {
                Id = employeeId,
                FirstName = "Ada",
                LastName = "Lovelace",
                Email = $"ada-{Guid.NewGuid():N}@example.com",
                Role = JurisFlow.Server.Enums.EmployeeRole.Paralegal
            });

            await db.SaveChangesAsync();
        });

        var payload = JsonContent.Create(new
        {
            title = "Follow up on intake",
            assignedTo = "Ada Lovelace",
            status = "To Do",
            priority = "High"
        });

        var request = CreateRequest(HttpMethod.Post, "/api/tasks", "task-user", "Associate", payload);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(employeeId, document.RootElement.GetProperty("assignedEmployeeId").GetString());
        Assert.Equal("Ada Lovelace", document.RootElement.GetProperty("assignedTo").GetString());
    }

    [Fact]
    public async Task EventCreateRejectsUnsupportedFields()
    {
        var payload = JsonContent.Create(new
        {
            title = "Court appearance",
            date = DateTime.UtcNow.AddDays(2),
            type = "Court",
            unsupported = "should-fail"
        });

        var request = CreateRequest(HttpMethod.Post, "/api/events", "calendar-user", "Associate", payload);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Unsupported fields", body, StringComparison.OrdinalIgnoreCase);
    }
}
