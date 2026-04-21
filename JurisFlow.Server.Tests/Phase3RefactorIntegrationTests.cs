using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using JurisFlow.Server.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Task = System.Threading.Tasks.Task;
using TaskModel = JurisFlow.Server.Models.Task;

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

    private HttpRequestMessage CreateRequest(
        HttpMethod method,
        string url,
        string userId,
        string role,
        HttpContent? content = null,
        string? ifMatch = null,
        string? tenantId = null,
        string? tenantSlug = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-Test-UserId", userId);
        request.Headers.Add("X-Test-Role", role);

        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            request.Headers.Add("X-Test-TenantId", tenantId);
        }

        if (!string.IsNullOrWhiteSpace(tenantSlug))
        {
            request.Headers.Add("X-Test-TenantSlug", tenantSlug);
        }

        if (!string.IsNullOrWhiteSpace(ifMatch))
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

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

    private async Task<TResult> QueryDbAsync<TResult>(Func<JurisFlowDbContext, Task<TResult>> query)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
        tenantContext.Set(TestApplicationFactory.TestTenantId, TestApplicationFactory.TestTenantSlug);
        return await query(db);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static string RequireEtag(HttpResponseMessage response)
    {
        var etag = response.Headers.ETag?.Tag;
        Assert.False(string.IsNullOrWhiteSpace(etag));
        return etag!;
    }

    private static HashSet<string> ReadTaskIds(JsonElement root)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        if (!root.TryGetProperty("items", out var items))
        {
            return ids;
        }

        foreach (var item in items.EnumerateArray())
        {
            var id = item.GetProperty("id").GetString();
            if (!string.IsNullOrWhiteSpace(id))
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    [Fact]
    public async Task ClientPortalPasswordManagementRequiresPrivilegedRole()
    {
        var clientId = Guid.NewGuid().ToString();
        var email = $"portal-{Guid.NewGuid():N}@example.com";

        await SeedAsync(async db =>
        {
            db.Clients.Add(new Client
            {
                Id = clientId,
                Name = "Portal Client",
                Email = email,
                NormalizedEmail = email.ToUpperInvariant(),
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
    public async Task TaskPatchOnlyUpdatesSpecifiedFields()
    {
        var taskId = Guid.NewGuid().ToString();
        var rowVersion = Guid.NewGuid().ToString("N");

        await SeedAsync(async db =>
        {
            db.Tasks.Add(new TaskModel
            {
                Id = taskId,
                Title = "Original task",
                Description = "Original description",
                Priority = "High",
                Status = "To Do",
                CreatedByUserId = "task-owner",
                RowVersion = rowVersion,
                CreatedAt = DateTime.UtcNow.AddDays(-1),
                UpdatedAt = DateTime.UtcNow.AddHours(-4)
            });

            await db.SaveChangesAsync();
        });

        var payload = JsonContent.Create(new
        {
            description = "Updated description only"
        });

        var request = CreateRequest(
            HttpMethod.Patch,
            $"/api/tasks/{taskId}",
            "task-owner",
            "Associate",
            payload,
            $"\"{rowVersion}\"");

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using (var document = await ReadJsonAsync(response))
        {
            Assert.Equal("Original task", document.RootElement.GetProperty("title").GetString());
            Assert.Equal("Updated description only", document.RootElement.GetProperty("description").GetString());
            Assert.Equal("High", document.RootElement.GetProperty("priority").GetString());
            Assert.Equal("To Do", document.RootElement.GetProperty("status").GetString());
        }

        var getResponse = await _client.SendAsync(CreateRequest(
            HttpMethod.Get,
            $"/api/tasks/{taskId}",
            "task-owner",
            "Associate"));

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        using (var getDocument = await ReadJsonAsync(getResponse))
        {
            Assert.Equal("Original task", getDocument.RootElement.GetProperty("title").GetString());
            Assert.Equal("Updated description only", getDocument.RootElement.GetProperty("description").GetString());
            Assert.Equal("High", getDocument.RootElement.GetProperty("priority").GetString());
            Assert.Equal("To Do", getDocument.RootElement.GetProperty("status").GetString());
            Assert.NotEqual(rowVersion, getDocument.RootElement.GetProperty("rowVersion").GetString());
        }
    }

    [Fact]
    public async Task TaskEndpointsEnforceObjectLevelAuthorization()
    {
        var clientId = Guid.NewGuid().ToString();
        var clientEmail = $"matter-client-{Guid.NewGuid():N}@example.com";
        var matterId = Guid.NewGuid().ToString();
        var matterTaskId = Guid.NewGuid().ToString();
        var matterTaskRowVersion = Guid.NewGuid().ToString("N");
        var ownTaskId = Guid.NewGuid().ToString();
        var otherTaskId = Guid.NewGuid().ToString();

        await SeedAsync(async db =>
        {
            db.Clients.Add(new Client
            {
                Id = clientId,
                Name = "Matter Client",
                Email = clientEmail,
                NormalizedEmail = clientEmail.ToUpperInvariant(),
                Type = "Individual",
                Status = "Active"
            });

            db.Matters.Add(new Matter
            {
                Id = matterId,
                CaseNumber = $"CASE-{Guid.NewGuid():N}",
                Name = "Restricted Matter",
                PracticeArea = "Litigation",
                Status = "Open",
                FeeStructure = "Hourly",
                ResponsibleAttorney = "matter-owner",
                ClientId = clientId,
                CreatedByUserId = "matter-owner",
                ShareWithFirm = false,
                ShareBillingWithFirm = false,
                ShareNotesWithFirm = false
            });

            db.Tasks.AddRange(
                new TaskModel
                {
                    Id = matterTaskId,
                    Title = "Restricted matter task",
                    Priority = "Medium",
                    Status = "To Do",
                    MatterId = matterId,
                    CreatedByUserId = "matter-owner",
                    RowVersion = matterTaskRowVersion
                },
                new TaskModel
                {
                    Id = ownTaskId,
                    Title = "My matterless task",
                    Priority = "Low",
                    Status = "To Do",
                    CreatedByUserId = "scope-user"
                },
                new TaskModel
                {
                    Id = otherTaskId,
                    Title = "Someone else's matterless task",
                    Priority = "Low",
                    Status = "To Do",
                    CreatedByUserId = "other-user"
                });

            await db.SaveChangesAsync();
        });

        var listResponse = await _client.SendAsync(CreateRequest(
            HttpMethod.Get,
            "/api/tasks?includeArchived=true",
            "scope-user",
            "Associate"));

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        using (var document = await ReadJsonAsync(listResponse))
        {
            var ids = ReadTaskIds(document.RootElement);
            Assert.Contains(ownTaskId, ids);
            Assert.DoesNotContain(matterTaskId, ids);
            Assert.DoesNotContain(otherTaskId, ids);
        }

        var getResponse = await _client.SendAsync(CreateRequest(
            HttpMethod.Get,
            $"/api/tasks/{matterTaskId}",
            "scope-user",
            "Associate"));

        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);

        var patchResponse = await _client.SendAsync(CreateRequest(
            HttpMethod.Patch,
            $"/api/tasks/{matterTaskId}",
            "scope-user",
            "Associate",
            JsonContent.Create(new { description = "Should not apply" }),
            $"\"{matterTaskRowVersion}\""));

        Assert.Equal(HttpStatusCode.NotFound, patchResponse.StatusCode);
    }

    [Fact]
    public async Task TaskStatusContractSupportsReviewAndArchivedAndRejectsBlocked()
    {
        var createResponse = await _client.SendAsync(CreateRequest(
            HttpMethod.Post,
            "/api/tasks",
            "status-user",
            "Associate",
            JsonContent.Create(new
            {
                title = "Review ready task",
                priority = "High",
                status = "Review"
            })));

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var createEtag = RequireEtag(createResponse);

        string taskId;
        using (var createDocument = await ReadJsonAsync(createResponse))
        {
            Assert.Equal("Review", createDocument.RootElement.GetProperty("status").GetString());
            taskId = createDocument.RootElement.GetProperty("id").GetString()!;
        }

        var archiveResponse = await _client.SendAsync(CreateRequest(
            HttpMethod.Put,
            $"/api/tasks/{taskId}/status",
            "status-user",
            "Associate",
            JsonContent.Create(new { status = "Archived" }),
            createEtag));

        Assert.Equal(HttpStatusCode.OK, archiveResponse.StatusCode);
        using (var archiveDocument = await ReadJsonAsync(archiveResponse))
        {
            Assert.Equal("Archived", archiveDocument.RootElement.GetProperty("status").GetString());
        }

        var refreshedTaskResponse = await _client.SendAsync(CreateRequest(
            HttpMethod.Get,
            $"/api/tasks/{taskId}",
            "status-user",
            "Associate"));

        Assert.Equal(HttpStatusCode.OK, refreshedTaskResponse.StatusCode);
        var archiveEtag = RequireEtag(refreshedTaskResponse);

        var blockedResponse = await _client.SendAsync(CreateRequest(
            HttpMethod.Put,
            $"/api/tasks/{taskId}/status",
            "status-user",
            "Associate",
            JsonContent.Create(new { status = "Blocked" }),
            archiveEtag));

        Assert.Equal(HttpStatusCode.BadRequest, blockedResponse.StatusCode);
        var blockedBody = await blockedResponse.Content.ReadAsStringAsync();
        Assert.Contains("status", blockedBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TaskPatchRequiresFreshIfMatchHeader()
    {
        var taskId = Guid.NewGuid().ToString();
        var rowVersion = Guid.NewGuid().ToString("N");

        await SeedAsync(async db =>
        {
            db.Tasks.Add(new TaskModel
            {
                Id = taskId,
                Title = "Concurrency task",
                Description = "Original",
                Priority = "Medium",
                Status = "To Do",
                CreatedByUserId = "etag-user",
                RowVersion = rowVersion
            });

            await db.SaveChangesAsync();
        });

        var missingResponse = await _client.SendAsync(CreateRequest(
            HttpMethod.Patch,
            $"/api/tasks/{taskId}",
            "etag-user",
            "Associate",
            JsonContent.Create(new { description = "Attempt without If-Match" })));

        Assert.Equal((HttpStatusCode)428, missingResponse.StatusCode);

        var staleResponse = await _client.SendAsync(CreateRequest(
            HttpMethod.Patch,
            $"/api/tasks/{taskId}",
            "etag-user",
            "Associate",
            JsonContent.Create(new { description = "Attempt with stale token" }),
            "\"stale-token\""));

        Assert.Equal(HttpStatusCode.PreconditionFailed, staleResponse.StatusCode);

        var currentTaskResponse = await _client.SendAsync(CreateRequest(
            HttpMethod.Get,
            $"/api/tasks/{taskId}",
            "etag-user",
            "Associate"));

        Assert.Equal(HttpStatusCode.OK, currentTaskResponse.StatusCode);
        using (var currentTaskDocument = await ReadJsonAsync(currentTaskResponse))
        {
            Assert.Equal("Original", currentTaskDocument.RootElement.GetProperty("description").GetString());
            Assert.Equal(rowVersion, currentTaskDocument.RootElement.GetProperty("rowVersion").GetString());
        }
    }

    [Fact]
    public async Task TaskTemplatesCanBeListedAndInstantiated()
    {
        var templateId = Guid.NewGuid().ToString();
        var baseDate = new DateTime(2026, 04, 20, 0, 0, 0, DateTimeKind.Utc);
        var definition = JsonSerializer.Serialize(new[]
        {
            new
            {
                title = "Open checklist",
                description = "Prepare initial packet",
                priority = "High",
                status = "To Do",
                dueOffsetDays = (int?)3,
                reminderOffsetDays = (int?)null
            },
            new
            {
                title = "Review filing",
                description = "Review submitted filing",
                priority = "Medium",
                status = "Review",
                dueOffsetDays = (int?)7,
                reminderOffsetDays = (int?)6
            }
        });

        await SeedAsync(async db =>
        {
            db.TaskTemplates.Add(new TaskTemplate
            {
                Id = templateId,
                Name = "Litigation kickoff",
                Category = "Litigation",
                Description = "Initial litigation checklist",
                Definition = definition,
                IsActive = true
            });

            await db.SaveChangesAsync();
        });

        var listResponse = await _client.SendAsync(CreateRequest(
            HttpMethod.Get,
            "/api/task-templates",
            "template-user",
            "Associate"));

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        using (var listDocument = await ReadJsonAsync(listResponse))
        {
            Assert.Contains(
                listDocument.RootElement.EnumerateArray().Select(item => item.GetProperty("id").GetString()),
                id => string.Equals(id, templateId, StringComparison.Ordinal));
        }

        var createResponse = await _client.SendAsync(CreateRequest(
            HttpMethod.Post,
            "/api/tasks/from-template",
            "template-user",
            "Associate",
            JsonContent.Create(new
            {
                templateId,
                baseDate
            })));

        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);

        string[] createdTaskIds;
        using (var createDocument = await ReadJsonAsync(createResponse))
        {
            var tasks = createDocument.RootElement.GetProperty("tasks").EnumerateArray().ToList();
            Assert.Equal(2, tasks.Count);
            Assert.Contains(tasks, item => item.GetProperty("title").GetString() == "Open checklist");
            Assert.Contains(tasks, item => item.GetProperty("title").GetString() == "Review filing");
            createdTaskIds = tasks.Select(item => item.GetProperty("id").GetString()!).ToArray();
        }

        var listTasksResponse = await _client.SendAsync(CreateRequest(
            HttpMethod.Get,
            "/api/tasks?includeArchived=true&limit=50",
            "template-user",
            "Associate"));

        Assert.Equal(HttpStatusCode.OK, listTasksResponse.StatusCode);
        using (var listTasksDocument = await ReadJsonAsync(listTasksResponse))
        {
            var items = listTasksDocument.RootElement.GetProperty("items")
                .EnumerateArray()
                .Where(item => createdTaskIds.Contains(item.GetProperty("id").GetString() ?? string.Empty))
                .OrderBy(item => item.GetProperty("title").GetString())
                .ToList();

            Assert.Equal(2, items.Count);
            Assert.Contains(items, task =>
                task.GetProperty("title").GetString() == "Open checklist" &&
                task.GetProperty("status").GetString() == "To Do" &&
                DateTime.Parse(task.GetProperty("dueDate").GetString()!).Date == baseDate.AddDays(3).Date);
            Assert.Contains(items, task =>
                task.GetProperty("title").GetString() == "Review filing" &&
                task.GetProperty("status").GetString() == "Review" &&
                DateTime.Parse(task.GetProperty("dueDate").GetString()!).Date == baseDate.AddDays(7).Date &&
                DateTime.Parse(task.GetProperty("reminderAt").GetString()!).Date == baseDate.AddDays(6).Date);
        }
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
