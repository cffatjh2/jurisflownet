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
    public async Task CreateInvoiceFallsBackToValidEntityOfficeAndPersistsServiceDates()
    {
        var clientId = Guid.NewGuid().ToString();
        var matterId = Guid.NewGuid().ToString();
        var entityId = Guid.NewGuid().ToString();
        var officeId = Guid.NewGuid().ToString();
        var email = $"invoice-create-success-{Guid.NewGuid():N}@example.com";
        var serviceDate = new DateTime(2026, 4, 3, 0, 0, 0, DateTimeKind.Utc);

        await SeedAsync(async db =>
        {
            db.FirmEntities.Add(new FirmEntity
            {
                Id = entityId,
                Name = "Main Entity",
                IsDefault = true,
                IsActive = true
            });

            db.Offices.Add(new Office
            {
                Id = officeId,
                EntityId = entityId,
                Name = "Main Office",
                IsDefault = true,
                IsActive = true
            });

            db.Clients.Add(new Client
            {
                Id = clientId,
                Name = "Portal Client",
                Email = email,
                NormalizedEmail = EmailAddressNormalizer.Normalize(email),
                Type = "Individual",
                Status = "Active"
            });

            db.Matters.Add(new Matter
            {
                Id = matterId,
                CaseNumber = "INV-CASE-2",
                Name = "Invoice Create Matter",
                PracticeArea = "CivilLitigation",
                Status = "Open",
                FeeStructure = "Hourly",
                ResponsibleAttorney = "Admin User",
                ClientId = clientId
            });

            var firmSettings = await db.FirmSettings.FirstOrDefaultAsync();
            if (firmSettings == null)
            {
                db.FirmSettings.Add(new FirmSettings
                {
                    FirmName = "Test Firm"
                });
            }
            else
            {
                firmSettings.FirmName = "Test Firm";
            }

            await db.SaveChangesAsync();
        });

        var payload = JsonContent.Create(new
        {
            clientId,
            matterId,
            entityId = "stale-entity-id",
            officeId = "stale-office-id",
            status = "Draft",
            dueDate = DateTime.UtcNow.AddDays(14),
            terms = "Net 14",
            notes = "Thank you for your business.",
            lineItems = new[]
            {
                new
                {
                    type = "time",
                    description = "Draft motion to compel",
                    serviceDate,
                    quantity = 1.5m,
                    rate = 400m,
                    activityCode = "A101"
                }
            }
        });

        var request = CreateRequest(HttpMethod.Post, "/api/invoices", "admin-1", "Admin", payload);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
        tenantContext.Set(TestApplicationFactory.TestTenantId, TestApplicationFactory.TestTenantSlug);

        var invoice = await db.Invoices.Include(i => i.LineItems).FirstOrDefaultAsync(i => i.MatterId == matterId);
        Assert.NotNull(invoice);
        Assert.Equal(entityId, invoice!.EntityId);
        Assert.Equal(officeId, invoice.OfficeId);
        var lineItem = Assert.Single(invoice.LineItems);
        Assert.Equal(serviceDate, lineItem.ServiceDate);
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

    [Fact]
    public async Task ClientPortalInvoiceDetailsReturnsLineItemsAndFirmSummary()
    {
        var clientId = Guid.NewGuid().ToString();
        var matterId = Guid.NewGuid().ToString();
        var invoiceId = Guid.NewGuid().ToString();
        var email = $"invoice-detail-{Guid.NewGuid():N}@example.com";
        var serviceDate = new DateTime(2026, 4, 3, 0, 0, 0, DateTimeKind.Utc);

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
                CaseNumber = "INV-CASE-3",
                Name = "Invoice Detail Matter",
                PracticeArea = "CivilLitigation",
                Status = "Open",
                FeeStructure = "Hourly",
                ResponsibleAttorney = "Admin User",
                ClientId = clientId
            });

            db.Invoices.Add(new Invoice
            {
                Id = invoiceId,
                Number = "INV-SENT-2",
                ClientId = clientId,
                MatterId = matterId,
                Status = InvoiceStatus.Sent,
                IssueDate = DateTime.UtcNow,
                DueDate = DateTime.UtcNow.AddDays(14),
                Subtotal = 600m,
                Total = 600m,
                Balance = 600m,
                Terms = "Net 14"
            });

            db.InvoiceLineItems.Add(new InvoiceLineItem
            {
                Id = Guid.NewGuid().ToString(),
                InvoiceId = invoiceId,
                Type = "time",
                Description = "Prepare client status report",
                ServiceDate = serviceDate,
                Quantity = 1.5m,
                Rate = 400m,
                Amount = 600m,
                ActivityCode = "A105"
            });

            var firmSettings = await db.FirmSettings.FirstOrDefaultAsync();
            if (firmSettings == null)
            {
                db.FirmSettings.Add(new FirmSettings
                {
                    FirmName = "Test Firm",
                    Address = "100 Main St",
                    City = "New York",
                    State = "NY",
                    ZipCode = "10001",
                    Phone = "212-555-0100"
                });
            }
            else
            {
                firmSettings.FirmName = "Test Firm";
                firmSettings.Address = "100 Main St";
                firmSettings.City = "New York";
                firmSettings.State = "NY";
                firmSettings.ZipCode = "10001";
                firmSettings.Phone = "212-555-0100";
            }

            await db.SaveChangesAsync();
        });

        var request = CreateRequest(HttpMethod.Get, $"/api/client/invoices/{invoiceId}", clientId, "Client");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("INV-SENT-2", document.RootElement.GetProperty("number").GetString());
        Assert.Equal("Test Firm", document.RootElement.GetProperty("firm").GetProperty("name").GetString());
        var lineItems = document.RootElement.GetProperty("lineItems").EnumerateArray().ToList();
        Assert.Single(lineItems);
        Assert.Equal("Prepare client status report", lineItems[0].GetProperty("description").GetString());
    }
}
