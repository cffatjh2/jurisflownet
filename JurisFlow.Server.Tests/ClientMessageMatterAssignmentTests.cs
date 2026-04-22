using System.Net;
using System.Net.Http.Json;
using JurisFlow.Server.Data;
using JurisFlow.Server.Enums;
using JurisFlow.Server.Models;
using JurisFlow.Server.Services;
using Microsoft.Extensions.DependencyInjection;
using Task = System.Threading.Tasks.Task;

namespace JurisFlow.Server.Tests;

public class ClientMessageMatterAssignmentTests : IClassFixture<TestApplicationFactory>
{
    private readonly TestApplicationFactory _factory;
    private readonly HttpClient _client;

    public ClientMessageMatterAssignmentTests(TestApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ClientCannotTargetEmployeeByResponsibleAttorneyStringOnly()
    {
        var clientId = $"client-{Guid.NewGuid():N}";
        var employeeUserId = $"employee-user-{Guid.NewGuid():N}";
        var employeeId = $"employee-{Guid.NewGuid():N}";
        var matterId = $"matter-{Guid.NewGuid():N}";

        await SeedAsync(async db =>
        {
            db.Clients.Add(CreateClient(clientId));
            db.Users.Add(CreateUser(employeeUserId));
            db.Employees.Add(CreateEmployee(employeeId, employeeUserId, "Target", "Attorney"));
            db.Matters.Add(CreateMatter(matterId, clientId, createdByUserId: "different-owner", responsibleEmployeeId: null, responsibleAttorney: "Target Attorney"));
            await db.SaveChangesAsync();
        });

        var response = await _client.SendAsync(CreateClientMessageRequest(clientId, matterId, employeeId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ClientCanTargetResponsibleEmployeeByRelationship()
    {
        var clientId = $"client-{Guid.NewGuid():N}";
        var employeeUserId = $"employee-user-{Guid.NewGuid():N}";
        var employeeId = $"employee-{Guid.NewGuid():N}";
        var matterId = $"matter-{Guid.NewGuid():N}";

        await SeedAsync(async db =>
        {
            db.Clients.Add(CreateClient(clientId));
            db.Users.Add(CreateUser(employeeUserId));
            db.Employees.Add(CreateEmployee(employeeId, employeeUserId, "Target", "Attorney"));
            db.Matters.Add(CreateMatter(matterId, clientId, createdByUserId: "different-owner", responsibleEmployeeId: employeeId, responsibleAttorney: "Legacy Display Name"));
            await db.SaveChangesAsync();
        });

        var response = await _client.SendAsync(CreateClientMessageRequest(clientId, matterId, employeeId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
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

    private static HttpRequestMessage CreateClientMessageRequest(string clientId, string matterId, string employeeId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/client/messages");
        request.Headers.Add("X-Test-UserId", clientId);
        request.Headers.Add("X-Test-Role", "Client");
        request.Content = JsonContent.Create(new
        {
            matterId,
            employeeId,
            subject = "Matter question",
            message = "Can we discuss this matter?"
        });
        return request;
    }

    private static Client CreateClient(string clientId)
    {
        return new Client
        {
            Id = clientId,
            Name = "Portal Client",
            Email = $"{clientId}@example.com",
            Type = "Individual",
            Status = "Active"
        };
    }

    private static User CreateUser(string userId)
    {
        return new User
        {
            Id = userId,
            Email = $"{userId}@example.com",
            Name = "Target Attorney",
            Role = "Attorney",
            PasswordHash = "test-hash"
        };
    }

    private static Employee CreateEmployee(string employeeId, string userId, string firstName, string lastName)
    {
        return new Employee
        {
            Id = employeeId,
            FirstName = firstName,
            LastName = lastName,
            Email = $"{employeeId}@example.com",
            Role = EmployeeRole.Associate,
            Status = EmployeeStatus.Active,
            UserId = userId
        };
    }

    private static Matter CreateMatter(
        string matterId,
        string clientId,
        string createdByUserId,
        string? responsibleEmployeeId,
        string responsibleAttorney)
    {
        return new Matter
        {
            Id = matterId,
            CaseNumber = $"CASE-{Guid.NewGuid():N}",
            Name = "Client Matter",
            PracticeArea = "General",
            Status = "Open",
            FeeStructure = "Hourly",
            ResponsibleAttorney = responsibleAttorney,
            ResponsibleEmployeeId = responsibleEmployeeId,
            ClientId = clientId,
            CreatedByUserId = createdByUserId,
            OpenDate = DateTime.UtcNow
        };
    }
}
