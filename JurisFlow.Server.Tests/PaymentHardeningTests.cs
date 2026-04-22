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
public class PaymentHardeningTests : IClassFixture<TestApplicationFactory>
{
    private readonly TestApplicationFactory _factory;
    private readonly HttpClient _client;

    public PaymentHardeningTests(TestApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task RefundEndpoint_RequiresIdempotencyKey_AndReturnsProblemDetails()
    {
        var seed = await SeedSucceededPaymentAsync("refund-owner");

        using var response = await _client.SendAsync(CreateRequest(
            HttpMethod.Post,
            $"/api/payments/{seed.PaymentTransactionId}/refund",
            "admin-user",
            "Admin",
            new
            {
                amount = 10m,
                reason = "missing-key"
            }));

        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("\"code\":\"payment_refund_idempotency_required\"", body, StringComparison.Ordinal);
        Assert.Contains("\"correlationId\":", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefundEndpoint_ReplaysDuplicateIdempotencyKey_WithoutDoubleApplying()
    {
        var seed = await SeedSucceededPaymentAsync("refund-owner");
        const string idempotencyKey = "refund-replay-1";

        string firstBody;
        using (var first = await _client.SendAsync(CreateRequest(
            HttpMethod.Post,
            $"/api/payments/{seed.PaymentTransactionId}/refund",
            "admin-user",
            "Admin",
            new
            {
                amount = 10m,
                reason = "partial-replay",
                idempotencyKey
            },
            idempotencyKey)))
        {
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            firstBody = await first.Content.ReadAsStringAsync();
        }

        string secondBody;
        using (var second = await _client.SendAsync(CreateRequest(
            HttpMethod.Post,
            $"/api/payments/{seed.PaymentTransactionId}/refund",
            "admin-user",
            "Admin",
            new
            {
                amount = 10m,
                reason = "partial-replay",
                idempotencyKey
            },
            idempotencyKey)))
        {
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);
            secondBody = await second.Content.ReadAsStringAsync();
        }

        Assert.Equal(firstBody, secondBody);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
        tenantContext.Set(TestApplicationFactory.TestTenantId, TestApplicationFactory.TestTenantSlug);

        var transaction = await db.PaymentTransactions.AsNoTracking().SingleAsync(t => t.Id == seed.PaymentTransactionId);
        var invoice = await db.Invoices.AsNoTracking().SingleAsync(i => i.Id == seed.InvoiceId);
        var dedupCount = await db.PaymentCommandDeduplications.CountAsync(d => d.CommandName == "payment-refund" && d.IdempotencyKey == idempotencyKey);

        Assert.Equal("Partially Refunded", transaction.Status);
        Assert.Equal(10m, transaction.RefundAmount);
        Assert.Equal(10m, transaction.InvoiceRefundAppliedAmount);
        Assert.Equal(90m, invoice.AmountPaid);
        Assert.Equal(10m, invoice.Balance);
        Assert.Equal(1, dedupCount);
    }

    [Fact]
    public async Task RefundEndpoint_RejectsSameIdempotencyKeyWithDifferentPayload()
    {
        using var isolatedFactory = new TestApplicationFactory();
        using var isolatedClient = isolatedFactory.CreateClient();
        var seed = await SeedSucceededPaymentAsync(isolatedFactory, "refund-owner");
        const string idempotencyKey = "refund-conflict-1";

        using (var first = await isolatedClient.SendAsync(CreateRequest(
            HttpMethod.Post,
            $"/api/payments/{seed.PaymentTransactionId}/refund",
            "admin-user",
            "Admin",
            new
            {
                amount = 10m,
                reason = "first",
                idempotencyKey
            },
            idempotencyKey)))
        {
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            await first.Content.ReadAsStringAsync();
        }

        string body;
        using (var second = await isolatedClient.SendAsync(CreateRequest(
            HttpMethod.Post,
            $"/api/payments/{seed.PaymentTransactionId}/refund",
            "admin-user",
            "Admin",
            new
            {
                amount = 20m,
                reason = "second",
                idempotencyKey
            },
            idempotencyKey)))
        {
            body = await second.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        }

        Assert.Contains("\"code\":\"payment_refund_idempotency_conflict\"", body, StringComparison.Ordinal);
        Assert.Contains("\"correlationId\":", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PaymentPlanRun_ReplaysDuplicateIdempotencyKey_WithoutCreatingExtraTransactions()
    {
        var seed = await SeedPaymentPlanAsync("plan-owner");
        const string idempotencyKey = "plan-run-1";

        string firstBody;
        using (var first = await _client.SendAsync(CreateRequest(
            HttpMethod.Post,
            $"/api/payment-plans/{seed.PaymentPlanId}/run",
            "admin-user",
            "Admin",
            payload: null,
            idempotencyKey: idempotencyKey)))
        {
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            firstBody = await first.Content.ReadAsStringAsync();
        }

        string secondBody;
        using (var second = await _client.SendAsync(CreateRequest(
            HttpMethod.Post,
            $"/api/payment-plans/{seed.PaymentPlanId}/run",
            "admin-user",
            "Admin",
            payload: null,
            idempotencyKey: idempotencyKey)))
        {
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);
            secondBody = await second.Content.ReadAsStringAsync();
        }

        Assert.Equal(firstBody, secondBody);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
        tenantContext.Set(TestApplicationFactory.TestTenantId, TestApplicationFactory.TestTenantSlug);

        var transactionCount = await db.PaymentTransactions.CountAsync(t => t.PaymentPlanId == seed.PaymentPlanId);
        var plan = await db.PaymentPlans.AsNoTracking().SingleAsync(p => p.Id == seed.PaymentPlanId);
        var dedupCount = await db.PaymentCommandDeduplications.CountAsync(d => d.CommandName == "payment-plan-run" && d.IdempotencyKey == idempotencyKey);

        Assert.Equal(1, transactionCount);
        Assert.Equal(75m, plan.RemainingAmount);
        Assert.Equal(1, dedupCount);
    }

    [Fact]
    public void PaymentMutationRateLimit_IsConfiguredForRefundAndPlanRun()
    {
        var programPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "JurisFlow.Server",
            "Program.cs"));
        var paymentsControllerPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "JurisFlow.Server",
            "Controllers",
            "PaymentsController.cs"));
        var plansControllerPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "JurisFlow.Server",
            "Controllers",
            "PaymentPlansController.cs"));

        var programSource = File.ReadAllText(programPath);
        var paymentsSource = File.ReadAllText(paymentsControllerPath);
        var plansSource = File.ReadAllText(plansControllerPath);

        Assert.Contains("options.AddPolicy(\"PaymentMutation\"", programSource, StringComparison.Ordinal);
        Assert.Contains("[EnableRateLimiting(\"PaymentMutation\")]", paymentsSource, StringComparison.Ordinal);
        Assert.Contains("[EnableRateLimiting(\"PaymentMutation\")]", plansSource, StringComparison.Ordinal);
    }

    private async Task<SeededPaymentObjects> SeedSucceededPaymentAsync(string ownerUserId)
    {
        return await SeedSucceededPaymentAsync(_factory, ownerUserId);
    }

    private static async Task<SeededPaymentObjects> SeedSucceededPaymentAsync(TestApplicationFactory factory, string ownerUserId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
        tenantContext.Set(TestApplicationFactory.TestTenantId, TestApplicationFactory.TestTenantSlug);
        await db.Database.EnsureCreatedAsync();

        var invoiceSeed = await SeedInvoiceAsync(
            db,
            ownerUserId,
            total: 100m,
            amountPaid: 100m,
            balance: 0m,
            status: InvoiceStatus.Paid);

        db.PaymentTransactions.Add(new PaymentTransaction
        {
            Id = invoiceSeed.PaymentTransactionId,
            InvoiceId = invoiceSeed.InvoiceId,
            MatterId = invoiceSeed.MatterId,
            ClientId = invoiceSeed.ClientId,
            Amount = 100m,
            Currency = "USD",
            PaymentMethod = "Manual",
            Status = "Succeeded",
            ProcessedAt = DateTime.UtcNow,
            InvoiceAppliedAmount = 100m,
            InvoiceAppliedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
        return new SeededPaymentObjects(
            invoiceSeed.ClientId,
            invoiceSeed.MatterId,
            invoiceSeed.InvoiceId,
            invoiceSeed.PaymentTransactionId);
    }

    private async Task<SeededPaymentPlanObjects> SeedPaymentPlanAsync(string ownerUserId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
        tenantContext.Set(TestApplicationFactory.TestTenantId, TestApplicationFactory.TestTenantSlug);
        await db.Database.EnsureCreatedAsync();

        var seed = await SeedInvoiceAsync(
            db,
            ownerUserId,
            total: 100m,
            amountPaid: 0m,
            balance: 100m,
            status: InvoiceStatus.Sent);

        db.PaymentPlans.Add(new PaymentPlan
        {
            Id = seed.PaymentPlanId,
            ClientId = seed.ClientId,
            InvoiceId = seed.InvoiceId,
            Name = "Test Plan",
            TotalAmount = 100m,
            InstallmentAmount = 25m,
            RemainingAmount = 100m,
            Frequency = "Monthly",
            StartDate = DateTime.UtcNow,
            NextRunDate = DateTime.UtcNow,
            Status = "Active",
            AutoPayEnabled = false
        });

        await db.SaveChangesAsync();
        return seed;
    }

    private static async Task<SeededPaymentPlanObjects> SeedInvoiceAsync(
        JurisFlowDbContext db,
        string ownerUserId,
        decimal total,
        decimal amountPaid,
        decimal balance,
        InvoiceStatus status)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var clientId = $"client-hardening-{suffix}";
        var matterId = $"matter-hardening-{suffix}";
        var invoiceId = $"invoice-hardening-{suffix}";
        var paymentTransactionId = $"payment-hardening-{suffix}";
        var paymentPlanId = $"plan-hardening-{suffix}";
        var email = $"hardening-{suffix}@example.com";

        db.Clients.Add(new Client
        {
            Id = clientId,
            Name = "Payment Hardening Client",
            Email = email,
            NormalizedEmail = EmailAddressNormalizer.Normalize(email),
            Type = "Individual",
            Status = "Active"
        });

        db.Matters.Add(new Matter
        {
            Id = matterId,
            CaseNumber = $"HARD-{suffix[..8]}",
            Name = "Payment Hardening Matter",
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
            Number = $"INV-HARD-{suffix[..8]}",
            ClientId = clientId,
            MatterId = matterId,
            Status = status,
            Total = total,
            AmountPaid = amountPaid,
            Balance = balance
        });

        await db.SaveChangesAsync();
        return new SeededPaymentPlanObjects(clientId, matterId, invoiceId, paymentTransactionId, paymentPlanId);
    }

    private static HttpRequestMessage CreateRequest(
        HttpMethod method,
        string url,
        string userId,
        string role,
        object? payload = null,
        string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-Test-UserId", userId);
        request.Headers.Add("X-Test-Role", role);
        request.Headers.Add("X-Test-TenantId", TestApplicationFactory.TestTenantId);
        request.Headers.Add("X-Test-TenantSlug", TestApplicationFactory.TestTenantSlug);
        request.Headers.Add("X-Tenant-Slug", TestApplicationFactory.TestTenantSlug);

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        if (payload != null)
        {
            request.Content = JsonContent.Create(payload);
        }

        return request;
    }

    private sealed record SeededPaymentObjects(
        string ClientId,
        string MatterId,
        string InvoiceId,
        string PaymentTransactionId);

    private sealed record SeededPaymentPlanObjects(
        string ClientId,
        string MatterId,
        string InvoiceId,
        string PaymentTransactionId,
        string PaymentPlanId);
}
