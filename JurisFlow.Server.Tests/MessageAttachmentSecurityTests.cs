using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using JurisFlow.Server.Data;
using JurisFlow.Server.Enums;
using JurisFlow.Server.Models;
using JurisFlow.Server.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Task = System.Threading.Tasks.Task;

namespace JurisFlow.Server.Tests;

public class MessageAttachmentSecurityTests : IClassFixture<TestApplicationFactory>
{
    private readonly TestApplicationFactory _factory;
    private readonly HttpClient _client;

    public MessageAttachmentSecurityTests(TestApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ClientMessageUploadRejectsDisallowedMimeType()
    {
        var clientId = $"client-{Guid.NewGuid():N}";
        await SeedAsync(async db =>
        {
            db.Clients.Add(new Client
            {
                Id = clientId,
                Name = "Portal Client",
                Email = $"{clientId}@example.com",
                Type = "Individual",
                Status = "Active"
            });
            await db.SaveChangesAsync();
        });

        var htmlPayload = Convert.ToBase64String(Encoding.UTF8.GetBytes("<html>malicious</html>"));
        var request = CreateRequest(
            HttpMethod.Post,
            "/api/client/messages",
            clientId,
            "Client",
            new
            {
                subject = "Need help",
                message = "Please review this attachment.",
                attachments = new[]
                {
                    new
                    {
                        fileName = "payload.html",
                        type = "text/html",
                        data = $"data:text/html;base64,{htmlPayload}"
                    }
                }
            });

        var response = await _client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("not allowed", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StaffMessageUploadRejectsOversizedAttachment()
    {
        var oversizedBytes = new byte[(10 * 1024 * 1024) + 1];
        var oversizedPayload = Convert.ToBase64String(oversizedBytes);

        var request = CreateRequest(
            HttpMethod.Post,
            "/api/staffmessages",
            $"staff-{Guid.NewGuid():N}",
            "Associate",
            new
            {
                senderId = "emp-sender",
                recipientId = "emp-recipient",
                body = "Please check this file.",
                attachments = new[]
                {
                    new
                    {
                        fileName = "large.pdf",
                        type = "application/pdf",
                        data = $"data:application/pdf;base64,{oversizedPayload}"
                    }
                }
            });

        var response = await _client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("per-file limit", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ClientCannotDownloadAnotherClientsAttachment()
    {
        var ownerClientId = $"client-owner-{Guid.NewGuid():N}";
        var otherClientId = $"client-other-{Guid.NewGuid():N}";
        var fileName = $"{Guid.NewGuid():N}.pdf";
        var attachmentJson = JsonSerializer.Serialize(new[]
        {
            new MessageAttachment
            {
                FileName = "statement.pdf",
                FilePath = $"/api/files/messages/{fileName}",
                MimeType = "application/pdf",
                Size = 3
            }
        });

        await SeedAsync(async db =>
        {
            db.Clients.AddRange(
                new Client
                {
                    Id = ownerClientId,
                    Name = "Owner Client",
                    Email = $"{ownerClientId}@example.com",
                    Type = "Individual",
                    Status = "Active"
                },
                new Client
                {
                    Id = otherClientId,
                    Name = "Other Client",
                    Email = $"{otherClientId}@example.com",
                    Type = "Individual",
                    Status = "Active"
                });

            db.ClientMessages.Add(new ClientMessage
            {
                Id = $"msg-{Guid.NewGuid():N}",
                ClientId = ownerClientId,
                Subject = "Confidential",
                Body = "Owner-only attachment",
                SenderType = "Client",
                Status = "Unread",
                AttachmentsJson = attachmentJson,
                CreatedAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync();
        });

        var physicalPath = CreateAttachmentFile(fileName, Encoding.UTF8.GetBytes("pdf"));
        try
        {
            var unauthorizedRequest = CreateRequest(
                HttpMethod.Get,
                $"/api/files/messages/{fileName}",
                otherClientId,
                "Client");
            var unauthorizedResponse = await _client.SendAsync(unauthorizedRequest);
            Assert.Equal(HttpStatusCode.Forbidden, unauthorizedResponse.StatusCode);

            var ownerRequest = CreateRequest(
                HttpMethod.Get,
                $"/api/files/messages/{fileName}",
                ownerClientId,
                "Client");
            var ownerResponse = await _client.SendAsync(ownerRequest);
            Assert.Equal(HttpStatusCode.OK, ownerResponse.StatusCode);
        }
        finally
        {
            if (File.Exists(physicalPath))
            {
                File.Delete(physicalPath);
            }
        }
    }

    [Fact]
    public async Task StaffNonParticipantCannotDownloadStaffThreadAttachment()
    {
        var userA = $"user-a-{Guid.NewGuid():N}";
        var userB = $"user-b-{Guid.NewGuid():N}";
        var userC = $"user-c-{Guid.NewGuid():N}";
        var employeeA = $"emp-a-{Guid.NewGuid():N}";
        var employeeB = $"emp-b-{Guid.NewGuid():N}";
        var employeeC = $"emp-c-{Guid.NewGuid():N}";
        var fileName = $"{Guid.NewGuid():N}.pdf";
        var attachmentJson = JsonSerializer.Serialize(new[]
        {
            new MessageAttachment
            {
                FileName = "team-note.pdf",
                FilePath = $"/api/files/messages/{fileName}",
                MimeType = "application/pdf",
                Size = 3
            }
        });

        await SeedAsync(async db =>
        {
            db.Users.AddRange(
                CreateUser(userA, "Associate"),
                CreateUser(userB, "Associate"),
                CreateUser(userC, "Associate"));

            db.Employees.AddRange(
                CreateEmployee(employeeA, userA, "a"),
                CreateEmployee(employeeB, userB, "b"),
                CreateEmployee(employeeC, userC, "c"));

            db.StaffMessages.Add(new StaffMessage
            {
                Id = $"staff-msg-{Guid.NewGuid():N}",
                SenderId = employeeA,
                RecipientId = employeeB,
                Body = "Internal thread attachment",
                Status = "Unread",
                AttachmentsJson = attachmentJson,
                CreatedAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync();
        });

        var physicalPath = CreateAttachmentFile(fileName, Encoding.UTF8.GetBytes("pdf"));
        try
        {
            var outsiderRequest = CreateRequest(
                HttpMethod.Get,
                $"/api/files/messages/{fileName}",
                userC,
                "Associate");
            var outsiderResponse = await _client.SendAsync(outsiderRequest);
            Assert.Equal(HttpStatusCode.Forbidden, outsiderResponse.StatusCode);

            var participantRequest = CreateRequest(
                HttpMethod.Get,
                $"/api/files/messages/{fileName}",
                userA,
                "Associate");
            var participantResponse = await _client.SendAsync(participantRequest);
            Assert.Equal(HttpStatusCode.OK, participantResponse.StatusCode);
        }
        finally
        {
            if (File.Exists(physicalPath))
            {
                File.Delete(physicalPath);
            }
        }
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

    private string CreateAttachmentFile(string fileName, byte[] content)
    {
        var root = GetMessageAttachmentRoot();
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, fileName);
        File.WriteAllBytes(path, content);
        return path;
    }

    private string GetMessageAttachmentRoot()
    {
        using var scope = _factory.Services.CreateScope();
        var env = scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>();
        return Path.Combine(
            env.ContentRootPath,
            "uploads",
            TestApplicationFactory.TestTenantId,
            "message-attachments");
    }

    private static User CreateUser(string userId, string role)
    {
        return new User
        {
            Id = userId,
            Email = $"{userId}@example.com",
            Name = userId,
            Role = role,
            PasswordHash = "test-hash"
        };
    }

    private static Employee CreateEmployee(string employeeId, string userId, string suffix)
    {
        return new Employee
        {
            Id = employeeId,
            FirstName = "Staff",
            LastName = suffix.ToUpperInvariant(),
            Email = $"staff-{suffix}-{Guid.NewGuid():N}@example.com",
            Role = EmployeeRole.Associate,
            UserId = userId
        };
    }
}
