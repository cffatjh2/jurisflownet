using System.Net;
using System.Text.Json;
using JurisFlow.Server.Data;
using JurisFlow.Server.Enums;
using JurisFlow.Server.Models;
using JurisFlow.Server.Services;
using Microsoft.Extensions.DependencyInjection;
using Task = System.Threading.Tasks.Task;

namespace JurisFlow.Server.Tests;

public class PaginationProjectionIntegrationTests : IClassFixture<TestApplicationFactory>
{
    private readonly TestApplicationFactory _factory;
    private readonly HttpClient _client;

    public PaginationProjectionIntegrationTests(TestApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ListEndpoints_RequirePagination_AndReturnProjectionPayloads()
    {
        var seed = await SeedPaginationDataAsync(invoiceCount: 105, timeEntryCount: 3, expenseCount: 3, paymentPlanCount: 3, clientCount: 3);

        await AssertPagedProjectionAsync($"/api/invoices?page=1&pageSize=250&entityId={seed.EntityId}&officeId={seed.OfficeId}", expectedPageSize: 100, expectedItemCount: 100, expectedHasMore: true, forbiddenFields: ["notes", "terms", "lineItems"]);
        await AssertPagedProjectionAsync($"/api/time-entries?page=1&pageSize=250&matterId={seed.MatterId}", expectedPageSize: 100, expectedItemCount: 3, expectedHasMore: false, forbiddenFields: ["submittedBy", "approvedBy"]);
        await AssertPagedProjectionAsync($"/api/expenses?page=1&pageSize=250&matterId={seed.MatterId}", expectedPageSize: 100, expectedItemCount: 3, expectedHasMore: false, forbiddenFields: ["submittedBy", "approvedBy"]);
        await AssertPagedProjectionAsync($"/api/payment-plans?page=1&pageSize=250&clientId={seed.PaymentPlanClientId}", expectedPageSize: 100, expectedItemCount: 3, expectedHasMore: false, forbiddenFields: ["autoPayReference"]);
        await AssertPagedProjectionAsync($"/api/clients?page=1&pageSize=250&search={seed.ClientSearchToken}", expectedPageSize: 100, expectedItemCount: 3, expectedHasMore: false, forbiddenFields: ["notes", "taxId", "passwordHash"]);
    }

    [Fact]
    public async Task BootstrapScopes_LimitBillingPayloads_ToRecentProjectionSlices()
    {
        await SeedPaginationDataAsync(invoiceCount: 60, timeEntryCount: 60, expenseCount: 60, paymentPlanCount: 0, clientCount: 1);

        var initialResponse = await _client.SendAsync(CreateRequest(HttpMethod.Get, "/api/bootstrap?scope=initial"));
        var deferredResponse = await _client.SendAsync(CreateRequest(HttpMethod.Get, "/api/bootstrap?scope=deferred"));

        Assert.Equal(HttpStatusCode.OK, initialResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, deferredResponse.StatusCode);

        using var initialDocument = JsonDocument.Parse(await initialResponse.Content.ReadAsStringAsync());
        using var deferredDocument = JsonDocument.Parse(await deferredResponse.Content.ReadAsStringAsync());

        var timeEntries = initialDocument.RootElement.GetProperty("timeEntries");
        Assert.Equal(50, timeEntries.GetArrayLength());
        var firstTimeEntry = timeEntries[0];
        Assert.False(firstTimeEntry.TryGetProperty("submittedBy", out _));
        Assert.False(firstTimeEntry.TryGetProperty("approvedBy", out _));

        var expenses = deferredDocument.RootElement.GetProperty("expenses");
        Assert.Equal(50, expenses.GetArrayLength());
        var firstExpense = expenses[0];
        Assert.False(firstExpense.TryGetProperty("submittedBy", out _));

        var invoices = deferredDocument.RootElement.GetProperty("invoices");
        Assert.Equal(50, invoices.GetArrayLength());
        var firstInvoice = invoices[0];
        Assert.False(firstInvoice.TryGetProperty("notes", out _));
        Assert.False(firstInvoice.TryGetProperty("terms", out _));
        Assert.False(firstInvoice.TryGetProperty("lineItems", out _));
    }

    private async Task AssertPagedProjectionAsync(string url, int expectedPageSize, int expectedItemCount, bool expectedHasMore, params string[] forbiddenFields)
    {
        var response = await _client.SendAsync(CreateRequest(HttpMethod.Get, url));
        var payload = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(payload);
        Assert.Equal(1, document.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(expectedPageSize, document.RootElement.GetProperty("pageSize").GetInt32());
        Assert.Equal(expectedHasMore, document.RootElement.GetProperty("hasMore").GetBoolean());

        var items = document.RootElement.GetProperty("items");
        Assert.Equal(expectedItemCount, items.GetArrayLength());
        Assert.True(document.RootElement.GetProperty("totalCount").GetInt32() >= expectedItemCount);

        if (items.GetArrayLength() == 0)
        {
            return;
        }

        var firstItem = items[0];
        foreach (var forbiddenField in forbiddenFields)
        {
            Assert.False(firstItem.TryGetProperty(forbiddenField, out _), $"Field '{forbiddenField}' should not be present in projection payload.");
        }
    }

    private async Task<PaginationSeedResult> SeedPaginationDataAsync(int invoiceCount, int timeEntryCount, int expenseCount, int paymentPlanCount, int clientCount)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
        tenantContext.Set(TestApplicationFactory.TestTenantId, TestApplicationFactory.TestTenantSlug);
        await db.Database.EnsureCreatedAsync();

        var suffix = Guid.NewGuid().ToString("N");
        var matterId = $"pagination-matter-{suffix}";
        var entityId = $"entity-{suffix}";
        var officeId = $"office-{suffix}";
        var clientSearchToken = $"pagination-{suffix}";
        var clients = new List<Client>();
        for (var i = 0; i < Math.Max(clientCount, 1); i++)
        {
            var clientId = $"pagination-client-{suffix}-{i}";
            var email = $"{clientSearchToken}-{i}@example.com";
            clients.Add(new Client
            {
                Id = clientId,
                Name = $"Pagination Client {clientSearchToken} {i}",
                Email = email,
                NormalizedEmail = EmailAddressNormalizer.Normalize(email),
                Type = "Individual",
                Status = "Active",
                CreatedAt = DateTime.UtcNow.AddMinutes(-i),
                UpdatedAt = DateTime.UtcNow.AddMinutes(-i)
            });
        }

        db.Clients.AddRange(clients);
        db.Matters.Add(new Matter
        {
            Id = matterId,
            CaseNumber = $"PAGE-{suffix[..8]}",
            Name = "Pagination Matter",
            PracticeArea = "CivilLitigation",
            Status = "Open",
            FeeStructure = "Hourly",
            ResponsibleAttorney = "pagination-user",
            ClientId = clients[0].Id,
            CreatedByUserId = "pagination-user",
            ShareWithFirm = false,
            ShareBillingWithFirm = false,
            OpenDate = DateTime.UtcNow.AddDays(-1)
        });

        for (var i = 0; i < invoiceCount; i++)
        {
            db.Invoices.Add(new Invoice
            {
                Id = $"pagination-invoice-{suffix}-{i}",
                Number = $"INV-PAGE-{i:D4}",
                ClientId = clients[i % clients.Count].Id,
                MatterId = matterId,
                EntityId = entityId,
                OfficeId = officeId,
                Status = InvoiceStatus.Sent,
                IssueDate = DateTime.UtcNow.AddMinutes(-i),
                DueDate = DateTime.UtcNow.AddDays(30).AddMinutes(-i),
                Subtotal = 100m + i,
                Total = 100m + i,
                Balance = 100m + i,
                Notes = "Sensitive invoice note",
                Terms = "Sensitive invoice terms",
                CreatedAt = DateTime.UtcNow.AddMinutes(-i),
                UpdatedAt = DateTime.UtcNow.AddMinutes(-i)
            });
        }

        for (var i = 0; i < timeEntryCount; i++)
        {
            db.TimeEntries.Add(new TimeEntry
            {
                Id = $"pagination-time-{suffix}-{i}",
                MatterId = matterId,
                Description = $"Time entry {i}",
                Duration = 30 + i,
                Rate = 100 + i,
                Date = DateTime.UtcNow.AddMinutes(-i),
                ApprovalStatus = "Pending",
                SubmittedBy = "pagination-user",
                SubmittedAt = DateTime.UtcNow.AddMinutes(-i),
                CreatedAt = DateTime.UtcNow.AddMinutes(-i),
                UpdatedAt = DateTime.UtcNow.AddMinutes(-i)
            });
        }

        for (var i = 0; i < expenseCount; i++)
        {
            db.Expenses.Add(new Expense
            {
                Id = $"pagination-expense-{suffix}-{i}",
                MatterId = matterId,
                Description = $"Expense {i}",
                Amount = 25 + i,
                Date = DateTime.UtcNow.AddMinutes(-i),
                Category = "Filing",
                ApprovalStatus = "Pending",
                SubmittedBy = "pagination-user",
                SubmittedAt = DateTime.UtcNow.AddMinutes(-i),
                CreatedAt = DateTime.UtcNow.AddMinutes(-i),
                UpdatedAt = DateTime.UtcNow.AddMinutes(-i)
            });
        }

        for (var i = 0; i < paymentPlanCount; i++)
        {
            db.PaymentPlans.Add(new PaymentPlan
            {
                Id = $"pagination-plan-{suffix}-{i}",
                ClientId = clients[0].Id,
                InvoiceId = invoiceCount > 0 ? $"pagination-invoice-{suffix}-{i}" : null,
                Name = $"Plan {i}",
                TotalAmount = 100m + i,
                InstallmentAmount = 25m,
                Frequency = "Monthly",
                StartDate = DateTime.UtcNow.AddDays(-i),
                NextRunDate = DateTime.UtcNow.AddDays(30 - i),
                RemainingAmount = 50m + i,
                Status = "Active",
                AutoPayEnabled = true,
                AutoPayMethod = "Stripe",
                AutoPayReference = "secret-reference",
                CreatedAt = DateTime.UtcNow.AddMinutes(-i),
                UpdatedAt = DateTime.UtcNow.AddMinutes(-i)
            });
        }

        await db.SaveChangesAsync();
        return new PaginationSeedResult(matterId, entityId, officeId, clients[0].Id, clientSearchToken);
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-Test-UserId", "pagination-user");
        request.Headers.Add("X-Test-Role", "Partner");
        request.Headers.Add("X-Test-TenantId", TestApplicationFactory.TestTenantId);
        request.Headers.Add("X-Test-TenantSlug", TestApplicationFactory.TestTenantSlug);
        request.Headers.Add("X-Tenant-Slug", TestApplicationFactory.TestTenantSlug);
        return request;
    }

    private sealed record PaginationSeedResult(
        string MatterId,
        string EntityId,
        string OfficeId,
        string PaymentPlanClientId,
        string ClientSearchToken);
}
