using System.Net;
using System.Net.Http.Json;
using Task = System.Threading.Tasks.Task;
using JurisFlow.Server.Data;
using JurisFlow.Server.DTOs;
using JurisFlow.Server.Enums;
using JurisFlow.Server.Models;
using JurisFlow.Server.Services;
using Microsoft.Extensions.DependencyInjection;

namespace JurisFlow.Server.Tests;

public class StaffMessagingSecurityTests : IClassFixture<TestApplicationFactory>
{
    private readonly TestApplicationFactory _factory;
    private readonly HttpClient _client;

    public StaffMessagingSecurityTests(TestApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task SendMessageDerivesSenderFromAuthenticatedEmployeeAndWritesAudit()
    {
        var senderUserId = $"sender-user-{Guid.NewGuid():N}";
        var senderEmployeeId = $"sender-emp-{Guid.NewGuid():N}";
        var recipientUserId = $"recipient-user-{Guid.NewGuid():N}";
        var recipientEmployeeId = $"recipient-emp-{Guid.NewGuid():N}";

        await SeedEmployeesAsync(
            CreateEmployeeSeed(senderUserId, senderEmployeeId, "Sender", "User", "sender"),
            CreateEmployeeSeed(recipientUserId, recipientEmployeeId, "Recipient", "User", "recipient"));

        DrainAuditQueue();

        var request = CreateRequest(
            HttpMethod.Post,
            "/api/staffmessages",
            senderUserId,
            "Associate",
            new
            {
                senderId = recipientEmployeeId,
                recipientId = recipientEmployeeId,
                body = "Internal hello"
            });

        var response = await _client.SendAsync(request);
        var message = await response.Content.ReadFromJsonAsync<StaffMessage>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(message);
        Assert.Equal(senderEmployeeId, message!.SenderId);
        Assert.Equal(recipientEmployeeId, message.RecipientId);

        var auditJob = DequeueAuditJob("staff.message.send");
        Assert.NotNull(auditJob);
        Assert.Equal(message.Id, auditJob!.Audit.EntityId);
    }

    [Fact]
    public async Task ListIgnoresRequestedUserIdAndReturnsOnlyCurrentEmployeeMessages()
    {
        var userA = $"user-a-{Guid.NewGuid():N}";
        var userB = $"user-b-{Guid.NewGuid():N}";
        var userC = $"user-c-{Guid.NewGuid():N}";
        var employeeA = $"emp-a-{Guid.NewGuid():N}";
        var employeeB = $"emp-b-{Guid.NewGuid():N}";
        var employeeC = $"emp-c-{Guid.NewGuid():N}";

        await SeedEmployeesAsync(
            CreateEmployeeSeed(userA, employeeA, "Staff", "A", "a"),
            CreateEmployeeSeed(userB, employeeB, "Staff", "B", "b"),
            CreateEmployeeSeed(userC, employeeC, "Staff", "C", "c"));

        await SeedAsync(async db =>
        {
            db.StaffMessages.AddRange(
                new StaffMessage
                {
                    Id = $"msg-ab-{Guid.NewGuid():N}",
                    SenderId = employeeA,
                    RecipientId = employeeB,
                    Body = "A to B",
                    Status = "Unread",
                    CreatedAt = DateTime.UtcNow.AddMinutes(-2)
                },
                new StaffMessage
                {
                    Id = $"msg-bc-{Guid.NewGuid():N}",
                    SenderId = employeeB,
                    RecipientId = employeeC,
                    Body = "B to C",
                    Status = "Unread",
                    CreatedAt = DateTime.UtcNow.AddMinutes(-1)
                });
            await db.SaveChangesAsync();
        });

        var request = CreateRequest(HttpMethod.Get, $"/api/staffmessages?userId={employeeB}", userC, "Associate");
        var response = await _client.SendAsync(request);
        var messages = await response.Content.ReadFromJsonAsync<List<StaffMessage>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(messages);
        Assert.Single(messages!);
        Assert.Equal(employeeB, messages[0].SenderId);
        Assert.Equal(employeeC, messages[0].RecipientId);
    }

    [Fact]
    public async Task ThreadEndpointReturnsOnlyCurrentParticipantsMessages()
    {
        var userA = $"user-a-{Guid.NewGuid():N}";
        var userB = $"user-b-{Guid.NewGuid():N}";
        var userC = $"user-c-{Guid.NewGuid():N}";
        var employeeA = $"emp-a-{Guid.NewGuid():N}";
        var employeeB = $"emp-b-{Guid.NewGuid():N}";
        var employeeC = $"emp-c-{Guid.NewGuid():N}";

        await SeedEmployeesAsync(
            CreateEmployeeSeed(userA, employeeA, "Staff", "A", "a"),
            CreateEmployeeSeed(userB, employeeB, "Staff", "B", "b"),
            CreateEmployeeSeed(userC, employeeC, "Staff", "C", "c"));

        await SeedAsync(async db =>
        {
            db.StaffMessages.AddRange(
                new StaffMessage
                {
                    Id = $"msg-ab-{Guid.NewGuid():N}",
                    SenderId = employeeA,
                    RecipientId = employeeB,
                    Body = "A to B",
                    Status = "Unread",
                    CreatedAt = DateTime.UtcNow.AddMinutes(-2)
                },
                new StaffMessage
                {
                    Id = $"msg-cb-{Guid.NewGuid():N}",
                    SenderId = employeeC,
                    RecipientId = employeeB,
                    Body = "C to B",
                    Status = "Unread",
                    CreatedAt = DateTime.UtcNow.AddMinutes(-1)
                });
            await db.SaveChangesAsync();
        });

        var request = CreateRequest(HttpMethod.Get, $"/api/staffmessages/thread?participantId={employeeB}", userC, "Associate");
        var response = await _client.SendAsync(request);
        var thread = await response.Content.ReadFromJsonAsync<List<StaffMessage>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(thread);
        Assert.Single(thread!);
        Assert.Equal(employeeC, thread[0].SenderId);
        Assert.Equal(employeeB, thread[0].RecipientId);
    }

    [Fact]
    public async Task MarkReadRejectsNonRecipient()
    {
        var userA = $"user-a-{Guid.NewGuid():N}";
        var userB = $"user-b-{Guid.NewGuid():N}";
        var userC = $"user-c-{Guid.NewGuid():N}";
        var employeeA = $"emp-a-{Guid.NewGuid():N}";
        var employeeB = $"emp-b-{Guid.NewGuid():N}";
        var employeeC = $"emp-c-{Guid.NewGuid():N}";
        var messageId = $"msg-{Guid.NewGuid():N}";

        await SeedEmployeesAsync(
            CreateEmployeeSeed(userA, employeeA, "Staff", "A", "a"),
            CreateEmployeeSeed(userB, employeeB, "Staff", "B", "b"),
            CreateEmployeeSeed(userC, employeeC, "Staff", "C", "c"));

        await SeedAsync(async db =>
        {
            db.StaffMessages.Add(new StaffMessage
            {
                Id = messageId,
                SenderId = employeeA,
                RecipientId = employeeB,
                Body = "Read me",
                Status = "Unread",
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        });

        var outsiderResponse = await _client.SendAsync(CreateRequest(HttpMethod.Post, $"/api/staffmessages/{messageId}/read", userC, "Associate"));
        Assert.Equal(HttpStatusCode.Forbidden, outsiderResponse.StatusCode);

        var recipientResponse = await _client.SendAsync(CreateRequest(HttpMethod.Post, $"/api/staffmessages/{messageId}/read", userB, "Associate"));
        Assert.Equal(HttpStatusCode.NoContent, recipientResponse.StatusCode);
    }

    [Fact]
    public async Task SendRejectsAttachmentContentThatDoesNotMatchMimeType()
    {
        var senderUserId = $"sender-user-{Guid.NewGuid():N}";
        var senderEmployeeId = $"sender-emp-{Guid.NewGuid():N}";
        var recipientUserId = $"recipient-user-{Guid.NewGuid():N}";
        var recipientEmployeeId = $"recipient-emp-{Guid.NewGuid():N}";

        await SeedEmployeesAsync(
            CreateEmployeeSeed(senderUserId, senderEmployeeId, "Sender", "User", "sender"),
            CreateEmployeeSeed(recipientUserId, recipientEmployeeId, "Recipient", "User", "recipient"));

        var badPayload = Convert.ToBase64String("not-a-real-pdf"u8.ToArray());
        var request = CreateRequest(
            HttpMethod.Post,
            "/api/staffmessages",
            senderUserId,
            "Associate",
            new
            {
                recipientId = recipientEmployeeId,
                body = "Please review this file",
                attachments = new[]
                {
                    new AttachmentDto
                    {
                        FileName = "fake.pdf",
                        Type = "application/pdf",
                        Data = $"data:application/pdf;base64,{badPayload}"
                    }
                }
            });

        var response = await _client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("does not match the declared MIME type", body, StringComparison.OrdinalIgnoreCase);
    }

    private async Task SeedEmployeesAsync(params (User user, Employee employee)[] seeds)
    {
        await SeedAsync(async db =>
        {
            db.Users.AddRange(seeds.Select(seed => seed.user));
            db.Employees.AddRange(seeds.Select(seed => seed.employee));
            await db.SaveChangesAsync();
        });
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

    private HttpRequestMessage CreateRequest(HttpMethod method, string url, string userId, string role, object? payload = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-Test-UserId", userId);
        request.Headers.Add("X-Test-Role", role);
        if (payload != null)
        {
            request.Content = JsonContent.Create(payload);
        }

        return request;
    }

    private void DrainAuditQueue()
    {
        var queue = _factory.Services.GetRequiredService<AuditLogWriteQueue>();
        while (queue.TryDequeue(out _))
        {
        }
    }

    private AuditLogWriteJob? DequeueAuditJob(string action)
    {
        var queue = _factory.Services.GetRequiredService<AuditLogWriteQueue>();
        while (queue.TryDequeue(out var job))
        {
            if (job != null && string.Equals(job.Audit.Action, action, StringComparison.Ordinal))
            {
                return job;
            }
        }

        return null;
    }

    private static (User user, Employee employee) CreateEmployeeSeed(string userId, string employeeId, string firstName, string lastName, string emailSuffix)
    {
        return
        (
            new User
            {
                Id = userId,
                Email = $"{userId}@example.com",
                Name = $"{firstName} {lastName}",
                Role = "Associate",
                PasswordHash = "test-hash"
            },
            new Employee
            {
                Id = employeeId,
                FirstName = firstName,
                LastName = lastName,
                Email = $"staff-{emailSuffix}-{Guid.NewGuid():N}@example.com",
                Role = EmployeeRole.Associate,
                UserId = userId
            }
        );
    }
}
