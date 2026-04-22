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

[Collection("SequentialDbTests")]
public class MoneyStandardizationTests : IClassFixture<TestApplicationFactory>
{
    private readonly TestApplicationFactory _factory;
    private readonly HttpClient _client;

    public MoneyStandardizationTests(TestApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task PaymentPlanService_NormalizesInstallmentsAndCompletesBalance()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
        var service = scope.ServiceProvider.GetRequiredService<PaymentPlanService>();

        tenantContext.Set(TestApplicationFactory.TestTenantId, TestApplicationFactory.TestTenantSlug);
        await db.Database.EnsureCreatedAsync();

        var seed = await SeedInvoiceAsync(
            db,
            ownerUserId: "money-owner",
            total: 100m,
            amountPaid: 0m,
            balance: 100m,
            status: InvoiceStatus.Sent);

        var runAt = new DateTime(2026, 4, 22, 10, 0, 0, DateTimeKind.Utc);
        var plan = new PaymentPlan
        {
            Id = $"plan-{Guid.NewGuid():N}",
            ClientId = seed.ClientId,
            InvoiceId = seed.InvoiceId,
            Name = "Standardized Plan",
            TotalAmount = 100m,
            InstallmentAmount = 33.335m,
            RemainingAmount = 100m,
            Frequency = "Monthly",
            StartDate = runAt,
            NextRunDate = runAt,
            Status = "Active",
            AutoPayEnabled = false
        };

        db.PaymentPlans.Add(plan);
        await db.SaveChangesAsync();

        var first = await service.RunPlanAsync(plan, "admin-user", "payer@example.com", "Payer", runAt);
        var second = await service.RunPlanAsync(plan, "admin-user", "payer@example.com", "Payer", runAt.AddMonths(1));
        var third = await service.RunPlanAsync(plan, "admin-user", "payer@example.com", "Payer", runAt.AddMonths(2));

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotNull(third);
        Assert.Equal(33.34m, first!.Amount);
        Assert.Equal(33.34m, second!.Amount);
        Assert.Equal(33.32m, third!.Amount);

        var storedPlan = await db.PaymentPlans.AsNoTracking().SingleAsync(p => p.Id == plan.Id);
        var invoice = await db.Invoices.AsNoTracking().SingleAsync(i => i.Id == seed.InvoiceId);

        Assert.Equal(0m, storedPlan.RemainingAmount);
        Assert.Equal("Completed", storedPlan.Status);
        Assert.False(storedPlan.AutoPayEnabled);

        Assert.Equal(100m, invoice.AmountPaid);
        Assert.Equal(0m, invoice.Balance);
        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
    }

    [Fact]
    public async Task RefundEndpoint_NormalizesPartialAndFullRefundsAndRestoresInvoiceBalance()
    {
        using var isolatedFactory = new TestApplicationFactory();
        using var isolatedClient = isolatedFactory.CreateClient();
        var seed = await SeedSucceededPaymentAsync(isolatedFactory, "refund-owner");

        using (var partialRefundResponse = await isolatedClient.SendAsync(CreateRequest(
            HttpMethod.Post,
            $"/api/payments/{seed.PaymentTransactionId}/refund",
            "admin-user",
            "Admin",
            new
            {
                amount = 33.335m,
                reason = "partial-refund",
                idempotencyKey = "money-refund-partial"
            })))
        {
            Assert.Equal(HttpStatusCode.OK, partialRefundResponse.StatusCode);
            await partialRefundResponse.Content.ReadAsStringAsync();
        }

        using (var scope = isolatedFactory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
            var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
            tenantContext.Set(TestApplicationFactory.TestTenantId, TestApplicationFactory.TestTenantSlug);

            var transaction = await db.PaymentTransactions.AsNoTracking().SingleAsync(t => t.Id == seed.PaymentTransactionId);
            var invoice = await db.Invoices.AsNoTracking().SingleAsync(i => i.Id == seed.InvoiceId);

            Assert.Equal("Partially Refunded", transaction.Status);
            Assert.Equal(33.34m, transaction.RefundAmount);
            Assert.Equal(33.34m, transaction.InvoiceRefundAppliedAmount);

            Assert.Equal(66.66m, invoice.AmountPaid);
            Assert.Equal(33.34m, invoice.Balance);
            Assert.Equal(InvoiceStatus.PartiallyPaid, invoice.Status);
        }

        using (var fullRefundResponse = await isolatedClient.SendAsync(CreateRequest(
            HttpMethod.Post,
            $"/api/payments/{seed.PaymentTransactionId}/refund",
            "admin-user",
            "Admin",
            new
            {
                reason = "full-refund",
                idempotencyKey = "money-refund-full"
            })))
        {
            var body = await fullRefundResponse.Content.ReadAsStringAsync();
            Assert.True(fullRefundResponse.StatusCode == HttpStatusCode.OK, body);
        }

        using (var scope = isolatedFactory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
            var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
            tenantContext.Set(TestApplicationFactory.TestTenantId, TestApplicationFactory.TestTenantSlug);

            var transaction = await db.PaymentTransactions.AsNoTracking().SingleAsync(t => t.Id == seed.PaymentTransactionId);
            var invoice = await db.Invoices.AsNoTracking().SingleAsync(i => i.Id == seed.InvoiceId);

            Assert.Equal("Refunded", transaction.Status);
            Assert.Equal(100m, transaction.RefundAmount);
            Assert.Equal(100m, transaction.InvoiceRefundAppliedAmount);

            Assert.Equal(0m, invoice.AmountPaid);
            Assert.Equal(100m, invoice.Balance);
            Assert.Equal(InvoiceStatus.Sent, invoice.Status);
        }
    }

    [Fact]
    public void PaymentPlanMoneyFields_UseDecimal182ColumnType()
    {
        var dbContextPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "JurisFlow.Server",
            "Data",
            "JurisFlowDbContext.cs"));

        var dbContextSource = File.ReadAllText(dbContextPath);

        Assert.Contains(".Property(p => p.TotalAmount)", dbContextSource, StringComparison.Ordinal);
        Assert.Contains(".Property(p => p.InstallmentAmount)", dbContextSource, StringComparison.Ordinal);
        Assert.Contains(".Property(p => p.RemainingAmount)", dbContextSource, StringComparison.Ordinal);
        Assert.Equal(3, dbContextSource.Split(".HasColumnType(\"decimal(18,2)\")", StringSplitOptions.None).Length - 1);
    }

    private async Task<SeededMoneyObjects> SeedSucceededPaymentAsync(string ownerUserId)
    {
        return await SeedSucceededPaymentAsync(_factory, ownerUserId);
    }

    private static async Task<SeededMoneyObjects> SeedSucceededPaymentAsync(TestApplicationFactory factory, string ownerUserId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
        tenantContext.Set(TestApplicationFactory.TestTenantId, TestApplicationFactory.TestTenantSlug);
        await db.Database.EnsureCreatedAsync();

        var seed = await SeedInvoiceAsync(
            db,
            ownerUserId,
            total: 100m,
            amountPaid: 100m,
            balance: 0m,
            status: InvoiceStatus.Paid);

        db.PaymentTransactions.Add(new PaymentTransaction
        {
            Id = seed.PaymentTransactionId,
            InvoiceId = seed.InvoiceId,
            MatterId = seed.MatterId,
            ClientId = seed.ClientId,
            Amount = 100m,
            Currency = "USD",
            PaymentMethod = "Manual",
            Status = "Succeeded",
            ProcessedAt = DateTime.UtcNow,
            InvoiceAppliedAmount = 100m,
            InvoiceAppliedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
        return seed;
    }

    private static async Task<SeededMoneyObjects> SeedInvoiceAsync(
        JurisFlowDbContext db,
        string ownerUserId,
        decimal total,
        decimal amountPaid,
        decimal balance,
        InvoiceStatus status)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var clientId = $"client-money-{suffix}";
        var matterId = $"matter-money-{suffix}";
        var invoiceId = $"invoice-money-{suffix}";
        var paymentTransactionId = $"payment-money-{suffix}";
        var email = $"money-{suffix}@example.com";

        db.Clients.Add(new Client
        {
            Id = clientId,
            Name = "Money Test Client",
            Email = email,
            NormalizedEmail = EmailAddressNormalizer.Normalize(email),
            Type = "Individual",
            Status = "Active"
        });

        db.Matters.Add(new Matter
        {
            Id = matterId,
            CaseNumber = $"MONEY-{suffix[..8]}",
            Name = "Money Test Matter",
            PracticeArea = "CivilLitigation",
            Status = "Open",
            FeeStructure = "Hourly",
            ResponsibleAttorney = ownerUserId,
            ClientId = clientId,
            CreatedByUserId = ownerUserId,
            ShareWithFirm = false,
            ShareBillingWithFirm = false
        });

        db.Invoices.Add(new Invoice
        {
            Id = invoiceId,
            Number = $"INV-MONEY-{suffix[..8]}",
            ClientId = clientId,
            MatterId = matterId,
            Status = status,
            Total = total,
            AmountPaid = amountPaid,
            Balance = balance
        });

        await db.SaveChangesAsync();
        return new SeededMoneyObjects(clientId, matterId, invoiceId, paymentTransactionId);
    }

    private static HttpRequestMessage CreateRequest(
        HttpMethod method,
        string url,
        string userId,
        string role,
        object? payload = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-Test-UserId", userId);
        request.Headers.Add("X-Test-Role", role);
        request.Headers.Add("X-Test-TenantId", TestApplicationFactory.TestTenantId);
        request.Headers.Add("X-Test-TenantSlug", TestApplicationFactory.TestTenantSlug);
        request.Headers.Add("X-Tenant-Slug", TestApplicationFactory.TestTenantSlug);

        if (payload != null)
        {
            request.Content = JsonContent.Create(payload);
        }

        return request;
    }

    private sealed record SeededMoneyObjects(
        string ClientId,
        string MatterId,
        string InvoiceId,
        string PaymentTransactionId);
}
