using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Task = System.Threading.Tasks.Task;
using JurisFlow.Server.Data;
using JurisFlow.Server.Enums;
using JurisFlow.Server.Models;
using JurisFlow.Server.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JurisFlow.Server.Tests;

public class EmployeeSecurityTests : IClassFixture<TestApplicationFactory>
{
    private readonly TestApplicationFactory _factory;
    private readonly HttpClient _client;

    public EmployeeSecurityTests(TestApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task EmployeesEndpointRequiresAdminRole()
    {
        var request = CreateRequest(HttpMethod.Get, "/api/employees", "associate-user", "Associate");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task StaffDirectoryRemainsAvailableToStaffAndExcludesTerminatedEmployees()
    {
        var activeUserId = $"active-user-{Guid.NewGuid():N}";
        var terminatedUserId = $"terminated-user-{Guid.NewGuid():N}";
        var activeEmployeeId = $"active-emp-{Guid.NewGuid():N}";
        var terminatedEmployeeId = $"terminated-emp-{Guid.NewGuid():N}";

        await SeedAsync(async db =>
        {
            db.Users.AddRange(
                CreateUser(activeUserId, "Attorney"),
                CreateUser(terminatedUserId, "Disabled"));

            db.Employees.AddRange(
                new Employee
                {
                    Id = activeEmployeeId,
                    FirstName = "Active",
                    LastName = "Staff",
                    Email = $"{activeEmployeeId}@example.com",
                    Role = EmployeeRole.Associate,
                    Status = EmployeeStatus.Active,
                    UserId = activeUserId
                },
                new Employee
                {
                    Id = terminatedEmployeeId,
                    FirstName = "Terminated",
                    LastName = "Staff",
                    Email = $"{terminatedEmployeeId}@example.com",
                    Role = EmployeeRole.Associate,
                    Status = EmployeeStatus.Terminated,
                    TerminationDate = DateTime.UtcNow.AddDays(-1),
                    UserId = terminatedUserId
                });

            await db.SaveChangesAsync();
        });

        var request = CreateRequest(HttpMethod.Get, "/api/staff-directory", "associate-user", "Associate");
        var response = await _client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(activeEmployeeId, body, StringComparison.Ordinal);
        Assert.DoesNotContain(terminatedEmployeeId, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmployeeListReturnsDtoWithoutNestedUserSecrets()
    {
        var userId = $"user-{Guid.NewGuid():N}";
        var employeeId = $"emp-{Guid.NewGuid():N}";

        await SeedAsync(async db =>
        {
            db.Users.Add(new User
            {
                Id = userId,
                Email = $"{userId}@example.com",
                NormalizedEmail = $"{userId}@example.com".ToUpperInvariant(),
                Name = "Admin Employee",
                Role = "Admin",
                PasswordHash = "secret-hash",
                MfaEnabled = true,
                MfaSecret = "mfa-secret",
                MfaBackupCodesJson = "[\"backup\"]",
                Avatar = "/api/files/avatars/example.png"
            });

            db.Employees.Add(new Employee
            {
                Id = employeeId,
                FirstName = "Admin",
                LastName = "Employee",
                Email = $"{userId}@example.com",
                Role = EmployeeRole.Partner,
                Status = EmployeeStatus.Active,
                UserId = userId
            });

            await db.SaveChangesAsync();
        });

        var request = CreateRequest(HttpMethod.Get, "/api/employees", "admin-user", "Admin");
        var response = await _client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(employeeId, body, StringComparison.Ordinal);
        Assert.Contains("\"avatar\"", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"user\"", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("passwordHash", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mfaSecret", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mfaBackupCodesJson", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeleteEmployeeSoftDeletesUserAndRevokesSessions()
    {
        var userId = $"user-{Guid.NewGuid():N}";
        var employeeId = $"emp-{Guid.NewGuid():N}";
        var sessionId = $"session-{Guid.NewGuid():N}";

        await SeedAsync(async db =>
        {
            db.Users.Add(new User
            {
                Id = userId,
                Email = $"{userId}@example.com",
                NormalizedEmail = $"{userId}@example.com".ToUpperInvariant(),
                Name = "Employee User",
                Role = "Attorney",
                PasswordHash = "old-hash",
                MfaEnabled = true,
                MfaSecret = "secret",
                MfaBackupCodesJson = "[\"code\"]"
            });

            db.Employees.Add(new Employee
            {
                Id = employeeId,
                FirstName = "Delete",
                LastName = "Me",
                Email = $"{userId}@example.com",
                Role = EmployeeRole.Associate,
                Status = EmployeeStatus.Active,
                UserId = userId
            });

            db.AuthSessions.Add(new AuthSession
            {
                Id = sessionId,
                UserId = userId,
                TenantId = TestApplicationFactory.TestTenantId,
                SubjectType = "User",
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            });

            await db.SaveChangesAsync();
        });

        var request = CreateRequest(HttpMethod.Delete, $"/api/employees/{employeeId}", "admin-user", "Admin");
        var response = await _client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Hard delete is disabled", body, StringComparison.OrdinalIgnoreCase);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
        tenantContext.Set(TestApplicationFactory.TestTenantId, TestApplicationFactory.TestTenantSlug);

        var employee = await db.Employees.FirstAsync(e => e.Id == employeeId);
        var user = await db.Users.FirstAsync(u => u.Id == userId);
        var session = await db.AuthSessions.FirstAsync(s => s.Id == sessionId);

        Assert.Equal(EmployeeStatus.Terminated, employee.Status);
        Assert.NotNull(employee.TerminationDate);
        Assert.Equal("Disabled", user.Role);
        Assert.Null(user.MfaSecret);
        Assert.Null(user.MfaBackupCodesJson);
        Assert.False(user.MfaEnabled);
        Assert.NotEqual("old-hash", user.PasswordHash);
        Assert.NotNull(session.RevokedAt);
        Assert.Equal("Employee deactivated", session.RevokedReason);
    }

    [Fact]
    public async Task AvatarUploadRejectsMimeSignatureMismatch()
    {
        var userId = $"avatar-user-{Guid.NewGuid():N}";
        var employeeId = $"avatar-emp-{Guid.NewGuid():N}";

        await SeedAvatarEmployeeAsync(userId, employeeId);

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00 });
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("image/jpeg");
        content.Add(fileContent, "file", "avatar.jpg");

        var request = CreateRequest(HttpMethod.Post, $"/api/employees/{employeeId}/avatar", $"admin-{Guid.NewGuid():N}", "Admin");
        request.Content = content;

        var response = await _client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("does not match", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AvatarUploadRejectsUnsupportedMimeType()
    {
        var userId = $"avatar-user-{Guid.NewGuid():N}";
        var employeeId = $"avatar-emp-{Guid.NewGuid():N}";

        await SeedAvatarEmployeeAsync(userId, employeeId);

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent("<svg xmlns=\"http://www.w3.org/2000/svg\" />"u8.ToArray());
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("image/svg+xml");
        content.Add(fileContent, "file", "avatar.svg");

        var request = CreateRequest(HttpMethod.Post, $"/api/employees/{employeeId}/avatar", $"admin-{Guid.NewGuid():N}", "Admin");
        request.Content = content;

        var response = await _client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("MIME type is not allowed", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AvatarUploadRejectsOversizedFile()
    {
        var userId = $"avatar-user-{Guid.NewGuid():N}";
        var employeeId = $"avatar-emp-{Guid.NewGuid():N}";

        await SeedAvatarEmployeeAsync(userId, employeeId);

        var bytes = new byte[(5 * 1024 * 1024) + 1];
        bytes[0] = 0x89;
        bytes[1] = 0x50;
        bytes[2] = 0x4E;
        bytes[3] = 0x47;
        bytes[4] = 0x0D;
        bytes[5] = 0x0A;
        bytes[6] = 0x1A;
        bytes[7] = 0x0A;

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("image/png");
        content.Add(fileContent, "file", "avatar.png");

        var request = CreateRequest(HttpMethod.Post, $"/api/employees/{employeeId}/avatar", $"admin-{Guid.NewGuid():N}", "Admin");
        request.Content = content;

        var response = await _client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("5 MB limit", body, StringComparison.OrdinalIgnoreCase);
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

    private async Task SeedAvatarEmployeeAsync(string userId, string employeeId)
    {
        await SeedAsync(async db =>
        {
            db.Users.Add(CreateUser(userId, "Admin"));
            db.Employees.Add(new Employee
            {
                Id = employeeId,
                FirstName = "Avatar",
                LastName = "Upload",
                Email = $"{userId}@example.com",
                Role = EmployeeRole.Partner,
                Status = EmployeeStatus.Active,
                UserId = userId
            });

            await db.SaveChangesAsync();
        });
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string url, string userId, string role)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-Test-UserId", userId);
        request.Headers.Add("X-Test-Role", role);
        return request;
    }

    private static User CreateUser(string userId, string role)
    {
        var email = $"{userId}@example.com";
        return new User
        {
            Id = userId,
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            Name = userId,
            Role = role,
            PasswordHash = "test-hash"
        };
    }
}
