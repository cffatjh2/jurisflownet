using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JurisFlow.Server.Data;
using JurisFlow.Server.Enums;
using JurisFlow.Server.Models;
using JurisFlow.Server.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Task = System.Threading.Tasks.Task;

namespace JurisFlow.Server.Tests;

public class InvoiceWorkflowTests : IClassFixture<TestApplicationFactory>
{
    private readonly TestApplicationFactory _factory;
    private readonly HttpClient _client;

    public InvoiceWorkflowTests(TestApplicationFactory factory)
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
    public async Task NonPrivilegedInvoiceCreateWithoutMatterReturnsBadRequest()
    {
        var clientId = Guid.NewGuid().ToString();
        var email = $"invoice-create-{Guid.NewGuid():N}@example.com";

        await SeedAsync(async db =>
        {
            db.Clients.Add(new Client
            {
                Id = clientId,
                Name = "Portal Client",
                Email = email,
                NormalizedEmail = EmailAddressNormalizer.Normalize(email),
                Type = "Individual",
                Status = "Active"
            });
            await db.SaveChangesAsync();
        });

        var payload = JsonContent.Create(new
        {
            clientId,
            status = "Draft",
            dueDate = DateTime.UtcNow.AddDays(14),
            lineItems = new[]
            {
                new
                {
                    type = "time",
                    description = "Research",
                    quantity = 1m,
                    rate = 200m,
                    activityCode = "A101"
                }
            }
        });

        var request = CreateRequest(HttpMethod.Post, "/api/invoices", "attorney-1", "Attorney", payload);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SendInvoiceMarksInvoiceSentAndQueuesClientNotification()
    {
        var clientId = Guid.NewGuid().ToString();
        var matterId = Guid.NewGuid().ToString();
        var invoiceId = Guid.NewGuid().ToString();
        var email = $"invoice-send-{Guid.NewGuid():N}@example.com";

        await SeedAsync(async db =>
        {
            db.Clients.Add(new Client
            {
                Id = clientId,
                Name = "Invoice Client",
                Email = email,
                NormalizedEmail = EmailAddressNormalizer.Normalize(email),
                Type = "Individual",
                Status = "Active",
                PortalEnabled = true
            });

            db.Matters.Add(new Matter
            {
                Id = matterId,
                CaseNumber = "INV-CASE-1",
                Name = "Invoice Test Matter",
                PracticeArea = "CivilLitigation",
                Status = "Open",
                FeeStructure = "Hourly",
                ResponsibleAttorney = "Admin User",
                ClientId = clientId
            });

            db.Invoices.Add(new Invoice
            {
                Id = invoiceId,
                Number = "INV-TEST-0001",
                ClientId = clientId,
                MatterId = matterId,
                Status = InvoiceStatus.Draft,
                IssueDate = DateTime.UtcNow,
                DueDate = DateTime.UtcNow.AddDays(14),
                Subtotal = 150m,
                Total = 150m,
                Balance = 150m
            });

            await db.SaveChangesAsync();
        });

        var request = CreateRequest(HttpMethod.Post, $"/api/invoices/{invoiceId}/send", "admin-1", "Admin");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
        tenantContext.Set(TestApplicationFactory.TestTenantId, TestApplicationFactory.TestTenantSlug);

        var invoice = await db.Invoices.FindAsync(invoiceId);
        Assert.NotNull(invoice);
        Assert.Equal(InvoiceStatus.Sent, invoice!.Status);

        var notification = await db.Notifications.FirstOrDefaultAsync(n => n.ClientId == clientId && n.Link == "tab:invoices");
        Assert.NotNull(notification);
        Assert.Equal("New Invoice Available", notification!.Title);
    }

    [Fact]
    public async Task ClientPortalInvoicesReturnsOnlyClientVisibleInvoices()
    {
        var clientId = Guid.NewGuid().ToString();
        var email = $"invoice-portal-{Guid.NewGuid():N}@example.com";

        await SeedAsync(async db =>
        {
            db.Clients.Add(new Client
            {
                Id = clientId,
                Name = "Portal Invoice Client",
                Email = email,
                NormalizedEmail = EmailAddressNormalizer.Normalize(email),
                Type = "Individual",
                Status = "Active",
                PortalEnabled = true
            });

            db.Invoices.AddRange(
                new Invoice
                {
                    Id = Guid.NewGuid().ToString(),
                    Number = "INV-DRAFT-1",
                    ClientId = clientId,
                    Status = InvoiceStatus.Draft,
                    IssueDate = DateTime.UtcNow,
                    DueDate = DateTime.UtcNow.AddDays(7),
                    Subtotal = 100m,
                    Total = 100m,
                    Balance = 100m
                },
                new Invoice
                {
                    Id = Guid.NewGuid().ToString(),
                    Number = "INV-SENT-1",
                    ClientId = clientId,
                    Status = InvoiceStatus.Sent,
                    IssueDate = DateTime.UtcNow,
                    DueDate = DateTime.UtcNow.AddDays(7),
                    Subtotal = 150m,
                    Total = 150m,
                    Balance = 150m
                });

            await db.SaveChangesAsync();
        });

        var request = CreateRequest(HttpMethod.Get, "/api/client/invoices", clientId, "Client");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var invoices = document.RootElement.EnumerateArray().ToList();
        Assert.Single(invoices);
        Assert.Equal("INV-SENT-1", invoices[0].GetProperty("number").GetString());
    }
}
