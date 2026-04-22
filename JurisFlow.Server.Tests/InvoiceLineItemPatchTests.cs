using System.Net;
using System.Net.Http.Json;
using JurisFlow.Server.Data;
using JurisFlow.Server.Enums;
using JurisFlow.Server.Models;
using JurisFlow.Server.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace JurisFlow.Server.Tests;

public class InvoiceLineItemPatchTests : IClassFixture<TestApplicationFactory>
{
    private readonly TestApplicationFactory _factory;
    private readonly HttpClient _client;

    public InvoiceLineItemPatchTests(TestApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task UpdateInvoice_PreservesReferencedLineItemIdsWhenClientOmitsIds()
    {
        var seed = await SeedInvoiceWithReferencedLineAsync();

        var response = await _client.SendAsync(CreateRequest(
            HttpMethod.Put,
            $"/api/invoices/{seed.InvoiceId}",
            new
            {
                notes = "patched",
                lineItems = new object[]
                {
                    new
                    {
                        type = "time",
                        description = "Updated first line",
                        quantity = 2m,
                        rate = 75m,
                        activityCode = "A101"
                    },
                    new
                    {
                        type = "expense",
                        description = "Updated second line",
                        quantity = 1m,
                        rate = 40m,
                        expenseCode = "E101"
                    }
                }
            }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
        tenantContext.Set(TestApplicationFactory.TestTenantId, TestApplicationFactory.TestTenantSlug);

        var invoice = await db.Invoices
            .AsNoTracking()
            .Include(i => i.LineItems)
            .SingleAsync(i => i.Id == seed.InvoiceId);
        var allocation = await db.InvoiceLinePayorAllocations
            .AsNoTracking()
            .SingleAsync(a => a.Id == seed.InvoiceLinePayorAllocationId);

        Assert.Equal(2, invoice.LineItems.Count);
        Assert.Contains(invoice.LineItems, li => li.Id == seed.FirstLineItemId && li.Description == "Updated first line" && li.Amount == 150m);
        Assert.Contains(invoice.LineItems, li => li.Id == seed.SecondLineItemId && li.Description == "Updated second line" && li.Amount == 40m);
        Assert.Equal(seed.FirstLineItemId, allocation.InvoiceLineItemId);
    }

    [Fact]
    public async Task UpdateInvoice_RejectsDeletingReferencedLineItem()
    {
        var seed = await SeedInvoiceWithReferencedLineAsync();

        var response = await _client.SendAsync(CreateRequest(
            HttpMethod.Put,
            $"/api/invoices/{seed.InvoiceId}",
            new
            {
                lineItems = new object[]
                {
                    new
                    {
                        id = seed.SecondLineItemId,
                        type = "expense",
                        description = "Keep second line",
                        quantity = 1m,
                        rate = 40m,
                        expenseCode = "E101"
                    }
                }
            }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
        tenantContext.Set(TestApplicationFactory.TestTenantId, TestApplicationFactory.TestTenantSlug);

        var invoice = await db.Invoices
            .AsNoTracking()
            .Include(i => i.LineItems)
            .SingleAsync(i => i.Id == seed.InvoiceId);

        Assert.Equal(2, invoice.LineItems.Count);
        Assert.Contains(invoice.LineItems, li => li.Id == seed.FirstLineItemId);
        Assert.Contains(invoice.LineItems, li => li.Id == seed.SecondLineItemId);
    }

    private async Task<SeededInvoiceLinePatchObjects> SeedInvoiceWithReferencedLineAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
        tenantContext.Set(TestApplicationFactory.TestTenantId, TestApplicationFactory.TestTenantSlug);
        await db.Database.EnsureCreatedAsync();

        var suffix = Guid.NewGuid().ToString("N");
        var clientId = $"client-invoice-patch-{suffix}";
        var matterId = $"matter-invoice-patch-{suffix}";
        var invoiceId = $"invoice-invoice-patch-{suffix}";
        var firstLineItemId = $"line-one-{suffix}";
        var secondLineItemId = $"line-two-{suffix}";
        var payorClientId = $"payor-client-{suffix}";
        var allocationId = $"line-allocation-{suffix}";
        var email = $"invoice-patch-{suffix}@example.com";

        db.Clients.AddRange(
            new Client
            {
                Id = clientId,
                Name = "Invoice Patch Client",
                Email = email,
                NormalizedEmail = EmailAddressNormalizer.Normalize(email),
                Type = "Individual",
                Status = "Active"
            },
            new Client
            {
                Id = payorClientId,
                Name = "Invoice Patch Payor",
                Email = $"payor-{email}",
                NormalizedEmail = EmailAddressNormalizer.Normalize($"payor-{email}"),
                Type = "Organization",
                Status = "Active"
            });

        db.Matters.Add(new Matter
        {
            Id = matterId,
            CaseNumber = $"PATCH-{suffix[..8]}",
            Name = "Invoice Patch Matter",
            PracticeArea = "CivilLitigation",
            Status = "Open",
            FeeStructure = "Hourly",
            ResponsibleAttorney = "admin-user",
            ClientId = clientId,
            CreatedByUserId = "admin-user",
            ShareWithFirm = false,
            ShareBillingWithFirm = false
        });

        db.Invoices.Add(new Invoice
        {
            Id = invoiceId,
            Number = $"INV-PATCH-{suffix[..8]}",
            ClientId = clientId,
            MatterId = matterId,
            Status = InvoiceStatus.Sent,
            Subtotal = 140m,
            Total = 140m,
            Balance = 140m,
            LineItems = new List<InvoiceLineItem>
            {
                new()
                {
                    Id = firstLineItemId,
                    InvoiceId = invoiceId,
                    Type = "time",
                    Description = "Original first line",
                    Quantity = 2m,
                    Rate = 50m,
                    Amount = 100m
                },
                new()
                {
                    Id = secondLineItemId,
                    InvoiceId = invoiceId,
                    Type = "expense",
                    Description = "Original second line",
                    Quantity = 1m,
                    Rate = 40m,
                    Amount = 40m
                }
            }
        });

        db.InvoiceLinePayorAllocations.Add(new InvoiceLinePayorAllocation
        {
            Id = allocationId,
            InvoiceId = invoiceId,
            InvoiceLineItemId = firstLineItemId,
            PayorClientId = payorClientId,
            ResponsibilityType = "primary",
            Amount = 100m,
            Status = "active",
            ActivityCode = "A101",
            EbillingProfileJson = "{\"provider\":\"ledes98b\"}"
        });

        await db.SaveChangesAsync();

        return new SeededInvoiceLinePatchObjects(invoiceId, firstLineItemId, secondLineItemId, allocationId);
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string url, object? payload = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-Test-UserId", "admin-user");
        request.Headers.Add("X-Test-Role", "Admin");
        request.Headers.Add("X-Test-TenantId", TestApplicationFactory.TestTenantId);
        request.Headers.Add("X-Test-TenantSlug", TestApplicationFactory.TestTenantSlug);
        request.Headers.Add("X-Tenant-Slug", TestApplicationFactory.TestTenantSlug);

        if (payload != null)
        {
            request.Content = JsonContent.Create(payload);
        }

        return request;
    }

    private sealed record SeededInvoiceLinePatchObjects(
        string InvoiceId,
        string FirstLineItemId,
        string SecondLineItemId,
        string InvoiceLinePayorAllocationId);
}
