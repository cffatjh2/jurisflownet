using System.Net;
using System.Net.Http.Json;
using Task = System.Threading.Tasks.Task;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using JurisFlow.Server.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JurisFlow.Server.Tests;

public class ApprovalFlowTests : IClassFixture<TestApplicationFactory>
{
    private readonly TestApplicationFactory _factory;
    private readonly HttpClient _client;

    public ApprovalFlowTests(TestApplicationFactory factory)
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
    public async Task NonApproverCannotApproveTimeEntry()
    {
        var entryId = Guid.NewGuid().ToString();
        await SeedAsync(async db =>
        {
            db.TimeEntries.Add(new TimeEntry
            {
                Id = entryId,
                Description = "Prep work",
                Duration = 30,
                Rate = 150,
                ApprovalStatus = "Pending"
            });
            await db.SaveChangesAsync();
        });

        var request = CreateRequest(HttpMethod.Post, $"/api/time-entries/{entryId}/approve", "user-1", "Paralegal");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AttorneyCreatedTimeEntryStartsAsPending()
    {
        var payload = JsonContent.Create(new
        {
            description = "Draft complaint",
            duration = 30,
            rate = 225,
            billed = false,
            isBillable = true
        });

        var request = CreateRequest(HttpMethod.Post, "/api/time-entries", "attorney-creator", "Attorney", payload);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
        tenantContext.Set(TestApplicationFactory.TestTenantId, TestApplicationFactory.TestTenantSlug);

        var entry = await db.TimeEntries
            .OrderByDescending(x => x.SubmittedAt)
            .FirstOrDefaultAsync(x => x.SubmittedBy == "attorney-creator");

        Assert.NotNull(entry);
        Assert.Equal("Pending", entry!.ApprovalStatus);
        Assert.Null(entry.ApprovedBy);
        Assert.Null(entry.ApprovedAt);
    }

    [Fact]
    public async Task ApprovingRejectedTimeEntryClearsRejection()
    {
        var entryId = Guid.NewGuid().ToString();
        await SeedAsync(async db =>
        {
            db.TimeEntries.Add(new TimeEntry
            {
                Id = entryId,
                Description = "Draft memo",
                Duration = 45,
                Rate = 200,
                ApprovalStatus = "Rejected",
                RejectedBy = "user-1",
                RejectedAt = DateTime.UtcNow,
                RejectionReason = "Need details"
            });
            await db.SaveChangesAsync();
        });

        var request = CreateRequest(HttpMethod.Post, $"/api/time-entries/{entryId}/approve", "admin-1", "Admin");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
        tenantContext.Set(TestApplicationFactory.TestTenantId, TestApplicationFactory.TestTenantSlug);
        var entry = await db.TimeEntries.FindAsync(entryId);

        Assert.NotNull(entry);
        Assert.Equal("Approved", entry!.ApprovalStatus);
        Assert.Null(entry.RejectedBy);
        Assert.Null(entry.RejectedAt);
        Assert.Null(entry.RejectionReason);
    }

    [Fact]
    public async Task AttorneyCanApproveOwnPendingTimeEntry()
    {
        var entryId = Guid.NewGuid().ToString();
        await SeedAsync(async db =>
        {
            db.TimeEntries.Add(new TimeEntry
            {
                Id = entryId,
                Description = "Review record",
                Duration = 20,
                Rate = 180,
                ApprovalStatus = "Pending",
                SubmittedBy = "attorney-1"
            });
            await db.SaveChangesAsync();
        });

        var request = CreateRequest(HttpMethod.Post, $"/api/time-entries/{entryId}/approve", "attorney-1", "Attorney");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ApprovedExpenseCannotBeEdited()
    {
        var expenseId = Guid.NewGuid().ToString();
        await SeedAsync(async db =>
        {
            db.Expenses.Add(new Expense
            {
                Id = expenseId,
                Description = "Court fees",
                Amount = 250,
                ApprovalStatus = "Approved"
            });
            await db.SaveChangesAsync();
        });

        var payload = JsonContent.Create(new { description = "Updated" });
        var request = CreateRequest(HttpMethod.Put, $"/api/expenses/{expenseId}", "partner-1", "Partner", payload);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task NonApproverCannotApproveExpense()
    {
        var expenseId = Guid.NewGuid().ToString();
        await SeedAsync(async db =>
        {
            db.Expenses.Add(new Expense
            {
                Id = expenseId,
                Description = "Filing fee",
                Amount = 100,
                ApprovalStatus = "Pending"
            });
            await db.SaveChangesAsync();
        });

        var request = CreateRequest(HttpMethod.Post, $"/api/expenses/{expenseId}/approve", "user-1", "Assistant");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AttorneyCreatedExpenseStartsAsPending()
    {
        var payload = JsonContent.Create(new
        {
            description = "Court runner",
            amount = 45,
            billed = false,
            category = "Other",
            expenseCode = "E118"
        });

        var request = CreateRequest(HttpMethod.Post, "/api/expenses", "attorney-expense", "Attorney", payload);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
        tenantContext.Set(TestApplicationFactory.TestTenantId, TestApplicationFactory.TestTenantSlug);

        var expense = await db.Expenses
            .OrderByDescending(x => x.SubmittedAt)
            .FirstOrDefaultAsync(x => x.SubmittedBy == "attorney-expense");

        Assert.NotNull(expense);
        Assert.Equal("Pending", expense!.ApprovalStatus);
        Assert.Null(expense.ApprovedBy);
        Assert.Null(expense.ApprovedAt);
    }

    [Fact]
    public async Task AttorneyCanApproveOwnPendingExpense()
    {
        var expenseId = Guid.NewGuid().ToString();
        await SeedAsync(async db =>
        {
            db.Expenses.Add(new Expense
            {
                Id = expenseId,
                Description = "Service fee",
                Amount = 125,
                ApprovalStatus = "Pending",
                SubmittedBy = "attorney-2"
            });
            await db.SaveChangesAsync();
        });

        var request = CreateRequest(HttpMethod.Post, $"/api/expenses/{expenseId}/approve", "attorney-2", "Attorney");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task RejectApprovedTrustTransactionFails()
    {
        var txId = Guid.NewGuid().ToString();
        await SeedAsync(async db =>
        {
            db.TrustTransactions.Add(new TrustTransaction
            {
                Id = txId,
                TrustAccountId = "trust-1",
                Type = "DEPOSIT",
                Amount = 500,
                Description = "Initial deposit",
                Status = "APPROVED"
            });
            await db.SaveChangesAsync();
        });

        var payload = JsonContent.Create(new { reason = "Invalid" });
        var request = CreateRequest(HttpMethod.Post, $"/api/trust/transactions/{txId}/reject", "admin-1", "Admin", payload);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ApproveRejectedTrustTransactionFails()
    {
        var txId = Guid.NewGuid().ToString();
        await SeedAsync(async db =>
        {
            db.TrustTransactions.Add(new TrustTransaction
            {
                Id = txId,
                TrustAccountId = "trust-2",
                Type = "DEPOSIT",
                Amount = 350,
                Description = "Rejected deposit",
                Status = "REJECTED"
            });
            await db.SaveChangesAsync();
        });

        var request = CreateRequest(HttpMethod.Post, $"/api/trust/transactions/{txId}/approve", "admin-2", "Admin");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
