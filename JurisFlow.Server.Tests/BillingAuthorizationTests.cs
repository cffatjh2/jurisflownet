using System.Net;
using System.Net.Http.Json;
using JurisFlow.Server.Data;
using JurisFlow.Server.Enums;
using JurisFlow.Server.Models;
using JurisFlow.Server.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace JurisFlow.Server.Tests;

public class BillingAuthorizationTests : IClassFixture<TestApplicationFactory>
{
    private readonly TestApplicationFactory _factory;
    private readonly HttpClient _client;

    public BillingAuthorizationTests(TestApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task BillingMutationRejectsSameTenantDifferentMatter()
    {
        var seed = await SeedBillingObjectsAsync(TestApplicationFactory.TestTenantId, TestApplicationFactory.TestTenantSlug, "matter-owner");

        var request = CreateRequest(
            HttpMethod.Post,
            "/api/billing/mark-billed",
            "other-associate",
            "Associate",
            TestApplicationFactory.TestTenantId,
            TestApplicationFactory.TestTenantSlug,
            new { matterId = seed.MatterId });

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task BillingMutationHidesDifferentTenantMatter()
    {
        var seed = await SeedBillingObjectsAsync(TestApplicationFactory.SecondaryTenantId, TestApplicationFactory.SecondaryTenantSlug, "matter-owner");

        var request = CreateRequest(
            HttpMethod.Post,
            "/api/billing/mark-billed",
            "matter-owner",
            "Associate",
            TestApplicationFactory.TestTenantId,
            TestApplicationFactory.TestTenantSlug,
            new { matterId = seed.MatterId });

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task BillingWriteRejectsLowRoleBeforeObjectMutation()
    {
        var seed = await SeedBillingObjectsAsync(TestApplicationFactory.TestTenantId, TestApplicationFactory.TestTenantSlug, "employee-owner");

        var request = CreateRequest(
            HttpMethod.Post,
            "/api/billing/mark-billed",
            "employee-owner",
            "Employee",
            TestApplicationFactory.TestTenantId,
            TestApplicationFactory.TestTenantSlug,
            new { matterId = seed.MatterId });

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task BillingReadRejectsClientRole()
    {
        var request = CreateRequest(
            HttpMethod.Get,
            "/api/payments/stats",
            "client-user",
            "Client",
            TestApplicationFactory.TestTenantId,
            TestApplicationFactory.TestTenantSlug);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PaymentPlanCreateRequiresInvoiceObjectAuthorization()
    {
        var seed = await SeedBillingObjectsAsync(TestApplicationFactory.TestTenantId, TestApplicationFactory.TestTenantSlug, "matter-owner");

        var request = CreateRequest(
            HttpMethod.Post,
            "/api/payment-plans",
            "other-associate",
            "Associate",
            TestApplicationFactory.TestTenantId,
            TestApplicationFactory.TestTenantSlug,
            new
            {
                clientId = seed.ClientId,
                invoiceId = seed.InvoiceId,
                installmentAmount = 25m,
                totalAmount = 100m
            });

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task LegalBillingPrebillGenerationRequiresMatterObjectAuthorization()
    {
        var seed = await SeedBillingObjectsAsync(TestApplicationFactory.TestTenantId, TestApplicationFactory.TestTenantSlug, "matter-owner");

        var request = CreateRequest(
            HttpMethod.Post,
            "/api/legal-billing/prebills/generate",
            "other-associate",
            "Associate",
            TestApplicationFactory.TestTenantId,
            TestApplicationFactory.TestTenantSlug,
            new
            {
                matterId = seed.MatterId,
                periodStart = DateTime.UtcNow.Date.AddDays(-7),
                periodEnd = DateTime.UtcNow.Date
            });

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task BillingSettingsWriteRejectsLowRole()
    {
        var request = CreateRequest(
            HttpMethod.Put,
            "/api/settings/billing",
            "employee-user",
            "Employee",
            TestApplicationFactory.TestTenantId,
            TestApplicationFactory.TestTenantSlug,
            new { defaultHourlyRate = 300m });

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RefundRequiresRefundPolicyEvenWhenMatterOwner()
    {
        var seed = await SeedBillingObjectsAsync(TestApplicationFactory.TestTenantId, TestApplicationFactory.TestTenantSlug, "matter-owner");

        var request = CreateRequest(
            HttpMethod.Post,
            $"/api/payments/{seed.PaymentTransactionId}/refund",
            "matter-owner",
            "Associate",
            TestApplicationFactory.TestTenantId,
            TestApplicationFactory.TestTenantSlug,
            new { amount = 10m, reason = "test" });

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<SeededBillingObjects> SeedBillingObjectsAsync(string tenantId, string tenantSlug, string matterOwnerUserId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
        tenantContext.Set(tenantId, tenantSlug);
        await db.Database.EnsureCreatedAsync();

        var suffix = Guid.NewGuid().ToString("N");
        var clientId = $"client-{suffix}";
        var matterId = $"matter-{suffix}";
        var invoiceId = $"invoice-{suffix}";
        var paymentTransactionId = $"payment-{suffix}";
        var email = $"billing-auth-{suffix}@example.com";

        db.Clients.Add(new Client
        {
            Id = clientId,
            Name = "Billing Auth Client",
            Email = email,
            NormalizedEmail = EmailAddressNormalizer.Normalize(email),
            Type = "Individual",
            Status = "Active"
        });

        db.Matters.Add(new Matter
        {
            Id = matterId,
            CaseNumber = $"AUTH-{suffix[..8]}",
            Name = "Billing Auth Matter",
            PracticeArea = "CivilLitigation",
            Status = "Open",
            FeeStructure = "Hourly",
            ResponsibleAttorney = matterOwnerUserId,
            ClientId = clientId,
            CreatedByUserId = matterOwnerUserId,
            ShareWithFirm = false,
            ShareBillingWithFirm = false
        });

        db.Invoices.Add(new Invoice
        {
            Id = invoiceId,
            Number = $"INV-{suffix[..8]}",
            ClientId = clientId,
            MatterId = matterId,
            Status = InvoiceStatus.Sent,
            Total = 100m,
            Balance = 100m
        });

        db.PaymentTransactions.Add(new PaymentTransaction
        {
            Id = paymentTransactionId,
            InvoiceId = invoiceId,
            MatterId = matterId,
            ClientId = clientId,
            Amount = 100m,
            Currency = "USD",
            PaymentMethod = "Manual",
            Status = "Succeeded",
            ProcessedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();

        return new SeededBillingObjects(clientId, matterId, invoiceId, paymentTransactionId);
    }

    private static HttpRequestMessage CreateRequest(
        HttpMethod method,
        string url,
        string userId,
        string role,
        string tenantId,
        string tenantSlug,
        object? payload = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-Test-UserId", userId);
        request.Headers.Add("X-Test-Role", role);
        request.Headers.Add("X-Test-TenantId", tenantId);
        request.Headers.Add("X-Test-TenantSlug", tenantSlug);
        request.Headers.Add("X-Tenant-Slug", tenantSlug);
        if (payload != null)
        {
            request.Content = JsonContent.Create(payload);
        }

        return request;
    }

    private sealed record SeededBillingObjects(
        string ClientId,
        string MatterId,
        string InvoiceId,
        string PaymentTransactionId);
}
