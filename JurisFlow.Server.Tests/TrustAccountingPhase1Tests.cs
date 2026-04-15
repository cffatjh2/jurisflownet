using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JurisFlow.Server.Contracts;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using JurisFlow.Server.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Task = System.Threading.Tasks.Task;

namespace JurisFlow.Server.Tests;

[CollectionDefinition("TrustAccounting", DisableParallelization = true)]
public sealed class TrustAccountingCollectionDefinition : ICollectionFixture<TestApplicationFactory>
{
}

[Collection("TrustAccounting")]
public class TrustAccountingPhase1Tests
{
    private readonly TestApplicationFactory _factory;
    private readonly HttpClient _client;

    public TrustAccountingPhase1Tests(TestApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task DepositCreation_RemainsPending_AndDoesNotWriteJournalOrBalances()
    {
        var seed = await SeedTrustLedgerAsync();

        var createResponse = await SendTrustAsync(
            HttpMethod.Post,
            "/api/trust/deposit",
            new
            {
                trustAccountId = seed.AccountId,
                amount = 125.50,
                description = "Retainer deposit",
                payorPayee = "Client",
                checkNumber = "CHK-100",
                allocations = new[]
                {
                    new { ledgerId = seed.LedgerId, amount = 125.50, description = "Initial funding" }
                }
            },
            userId: "maker-admin",
            role: "Admin");

        var body = await createResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);

        var tx = JsonSerializer.Deserialize<TrustTransaction>(body, JsonOptions());
        Assert.NotNull(tx);
        Assert.Equal("PENDING", tx!.Status);
        Assert.Null(tx.ApprovedBy);

        using var scope = CreateTenantScope();
        var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var account = await db.TrustBankAccounts.AsNoTracking().SingleAsync(a => a.Id == seed.AccountId);
        var ledger = await db.ClientTrustLedgers.AsNoTracking().SingleAsync(l => l.Id == seed.LedgerId);
        var journalCount = await db.TrustJournalEntries.CountAsync(j => j.TrustTransactionId == tx.Id);

        Assert.Equal(0m, account.CurrentBalance);
        Assert.Equal(0m, ledger.RunningBalance);
        Assert.Equal(0, journalCount);
    }

    [Fact]
    public async Task ApproveTransaction_ByDifferentUser_WritesJournalAndUpdatesProjection()
    {
        var seed = await SeedTrustLedgerAsync();
        var tx = await CreatePendingDepositAsync(seed.AccountId, seed.LedgerId, "maker-1", "Associate");

        var approveResponse = await SendTrustAsync(
            HttpMethod.Post,
            $"/api/trust/transactions/{tx.Id}/approve",
            payload: null,
            userId: "approver-1",
            role: "Partner");

        var body = await approveResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);

        var approved = JsonSerializer.Deserialize<TrustTransaction>(body, JsonOptions());
        Assert.NotNull(approved);
        Assert.Equal("APPROVED", approved!.Status);
        Assert.Equal("approver-1", approved.ApprovedBy);

        using var scope = CreateTenantScope();
        var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var account = await db.TrustBankAccounts.AsNoTracking().SingleAsync(a => a.Id == seed.AccountId);
        var ledger = await db.ClientTrustLedgers.AsNoTracking().SingleAsync(l => l.Id == seed.LedgerId);
        var journal = await db.TrustJournalEntries.AsNoTracking().SingleAsync(j => j.TrustTransactionId == tx.Id);

        Assert.Equal(100m, account.CurrentBalance);
        Assert.Equal(100m, ledger.RunningBalance);
        Assert.Equal(100m, journal.Amount);
        Assert.Equal("posting", journal.EntryKind);
        Assert.Equal("deposit", journal.OperationType);
        Assert.Equal(seed.LedgerId, journal.ClientTrustLedgerId);
    }

    [Fact]
    public async Task ApproveTransaction_ByCreator_IsForbidden()
    {
        var seed = await SeedTrustLedgerAsync();
        var tx = await CreatePendingDepositAsync(seed.AccountId, seed.LedgerId, "same-user", "Partner");

        var approveResponse = await SendTrustAsync(
            HttpMethod.Post,
            $"/api/trust/transactions/{tx.Id}/approve",
            payload: null,
            userId: "same-user",
            role: "Partner");

        var body = await approveResponse.Content.ReadAsStringAsync();
        Assert.True(
            approveResponse.StatusCode == HttpStatusCode.Forbidden,
            $"Expected 403 but received {(int)approveResponse.StatusCode}. Body: {body}");
        Assert.Contains("Maker-checker", body, StringComparison.OrdinalIgnoreCase);

        using var scope = CreateTenantScope();
        var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var persisted = await db.TrustTransactions.AsNoTracking().SingleAsync(t => t.Id == tx.Id);
        Assert.Equal("PENDING", persisted.Status);
    }

    [Fact]
    public async Task VoidApprovedDeposit_WritesReversalEntries_AndRestoresProjection()
    {
        var seed = await SeedTrustLedgerAsync();
        var tx = await CreatePendingDepositAsync(seed.AccountId, seed.LedgerId, "maker-void", "Associate");

        var approveResponse = await SendTrustAsync(
            HttpMethod.Post,
            $"/api/trust/transactions/{tx.Id}/approve",
            payload: null,
            userId: "approver-void",
            role: "Partner");
        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);

        var voidResponse = await SendTrustAsync(
            HttpMethod.Post,
            $"/api/trust/transactions/{tx.Id}/void",
            new { reason = "Client funding returned" },
            userId: "voider-1",
            role: "Partner");

        var voidBody = await voidResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, voidResponse.StatusCode);

        var voided = JsonSerializer.Deserialize<TrustTransaction>(voidBody, JsonOptions());
        Assert.NotNull(voided);
        Assert.Equal("VOIDED", voided!.Status);

        using var scope = CreateTenantScope();
        var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var account = await db.TrustBankAccounts.AsNoTracking().SingleAsync(a => a.Id == seed.AccountId);
        var ledger = await db.ClientTrustLedgers.AsNoTracking().SingleAsync(l => l.Id == seed.LedgerId);
        var journalEntries = await db.TrustJournalEntries.AsNoTracking()
            .Where(j => j.TrustTransactionId == tx.Id)
            .OrderBy(j => j.CreatedAt)
            .ToListAsync();

        Assert.Equal(0m, account.CurrentBalance);
        Assert.Equal(0m, ledger.RunningBalance);
        Assert.Equal(2, journalEntries.Count);
        Assert.Equal("posting", journalEntries[0].EntryKind);
        Assert.Equal("reversal", journalEntries[1].EntryKind);
        Assert.Equal(-journalEntries[0].Amount, journalEntries[1].Amount);
        Assert.Equal(journalEntries[0].Id, journalEntries[1].ReversalOfTrustJournalEntryId);
    }

    [Fact]
    public async Task ApproveCheckedDeposit_TracksPendingClearance_AndKeepsAvailableAtZero()
    {
        var seed = await SeedTrustLedgerAsync();
        var tx = await CreatePendingDepositAsync(seed.AccountId, seed.LedgerId, "maker-clearance", "Associate", amount: 175.00m, checkNumber: "CHK-450");

        var approveResponse = await SendTrustAsync(
            HttpMethod.Post,
            $"/api/trust/transactions/{tx.Id}/approve",
            payload: null,
            userId: "approver-clearance",
            role: "Partner");

        var approveBody = await approveResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);

        var approved = JsonSerializer.Deserialize<TrustTransaction>(approveBody, JsonOptions());
        Assert.NotNull(approved);
        Assert.Equal("pending_clearance", approved!.ClearingStatus);
        Assert.False(string.IsNullOrWhiteSpace(approved.PostingBatchId));

        using var scope = CreateTenantScope();
        var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var account = await db.TrustBankAccounts.AsNoTracking().SingleAsync(a => a.Id == seed.AccountId);
        var ledger = await db.ClientTrustLedgers.AsNoTracking().SingleAsync(l => l.Id == seed.LedgerId);
        var journal = await db.TrustJournalEntries.AsNoTracking().SingleAsync(j => j.TrustTransactionId == tx.Id && j.EntryKind == "posting");

        Assert.Equal(175.00m, account.CurrentBalance);
        Assert.Equal(0m, account.ClearedBalance);
        Assert.Equal(175.00m, account.UnclearedBalance);
        Assert.Equal(0m, account.AvailableDisbursementCapacity);
        Assert.Equal(175.00m, ledger.RunningBalance);
        Assert.Equal(0m, ledger.ClearedBalance);
        Assert.Equal(175.00m, ledger.UnclearedBalance);
        Assert.Equal(0m, ledger.AvailableToDisburse);
        Assert.Equal("uncleared", journal.AvailabilityClass);
    }

    [Fact]
    public async Task ClearDeposit_ReclassesFunds_ToClearedAvailable_WithoutChangingCurrentBalance()
    {
        var seed = await SeedTrustLedgerAsync();
        var tx = await CreatePendingDepositAsync(seed.AccountId, seed.LedgerId, "maker-clear", "Associate", amount: 220.00m, checkNumber: "CHK-500");

        var approveResponse = await SendTrustAsync(
            HttpMethod.Post,
            $"/api/trust/transactions/{tx.Id}/approve",
            payload: null,
            userId: "approver-clear",
            role: "Partner");
        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);

        var clearResponse = await SendTrustAsync(
            HttpMethod.Post,
            $"/api/trust/transactions/{tx.Id}/clear",
            new { notes = "Bank posted funds" },
            userId: "partner-clear",
            role: "Partner");

        var clearBody = await clearResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, clearResponse.StatusCode);

        var cleared = JsonSerializer.Deserialize<TrustTransaction>(clearBody, JsonOptions());
        Assert.NotNull(cleared);
        Assert.Equal("cleared", cleared!.ClearingStatus);
        Assert.NotNull(cleared.ClearedAt);

        using var scope = CreateTenantScope();
        var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var account = await db.TrustBankAccounts.AsNoTracking().SingleAsync(a => a.Id == seed.AccountId);
        var ledger = await db.ClientTrustLedgers.AsNoTracking().SingleAsync(l => l.Id == seed.LedgerId);
        var journals = await db.TrustJournalEntries.AsNoTracking()
            .Where(j => j.TrustTransactionId == tx.Id)
            .OrderBy(j => j.CreatedAt)
            .ToListAsync();

        Assert.Equal(220.00m, account.CurrentBalance);
        Assert.Equal(220.00m, account.ClearedBalance);
        Assert.Equal(0m, account.UnclearedBalance);
        Assert.Equal(220.00m, account.AvailableDisbursementCapacity);
        Assert.Equal(220.00m, ledger.RunningBalance);
        Assert.Equal(220.00m, ledger.ClearedBalance);
        Assert.Equal(0m, ledger.UnclearedBalance);
        Assert.Equal(220.00m, ledger.AvailableToDisburse);
        Assert.Equal(3, journals.Count);
        Assert.Contains(journals, j => j.EntryKind == "clearance" && j.AvailabilityClass == "cleared" && j.Amount == 220.00m);
        Assert.Contains(journals, j => j.EntryKind == "clearance" && j.AvailabilityClass == "uncleared" && j.Amount == -220.00m);
    }

    [Fact]
    public async Task WithdrawalAgainstUnclearedFunds_IsRejected()
    {
        var seed = await SeedTrustLedgerAsync();
        var deposit = await CreatePendingDepositAsync(seed.AccountId, seed.LedgerId, "maker-uncleared", "Associate", amount: 130.00m, checkNumber: "CHK-600");

        var approveDepositResponse = await SendTrustAsync(
            HttpMethod.Post,
            $"/api/trust/transactions/{deposit.Id}/approve",
            payload: null,
            userId: "approver-uncleared",
            role: "Partner");
        Assert.Equal(HttpStatusCode.OK, approveDepositResponse.StatusCode);

        var withdrawalResponse = await SendTrustAsync(
            HttpMethod.Post,
            "/api/trust/withdrawal",
            new
            {
                trustAccountId = seed.AccountId,
                ledgerId = seed.LedgerId,
                amount = 50.00m,
                description = "Early disbursement",
                payorPayee = "Vendor"
            },
            userId: "maker-withdrawal",
            role: "Associate");
        Assert.Equal(HttpStatusCode.OK, withdrawalResponse.StatusCode);

        var withdrawal = JsonSerializer.Deserialize<TrustTransaction>(await withdrawalResponse.Content.ReadAsStringAsync(), JsonOptions());
        Assert.NotNull(withdrawal);

        var approveWithdrawalResponse = await SendTrustAsync(
            HttpMethod.Post,
            $"/api/trust/transactions/{withdrawal!.Id}/approve",
            payload: null,
            userId: "approver-withdrawal",
            role: "Partner");

        var body = await approveWithdrawalResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.BadRequest, approveWithdrawalResponse.StatusCode);
        Assert.Contains("cleared funds", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReturnDeposit_ReversesOpenEntries_AndMarksReturned()
    {
        var seed = await SeedTrustLedgerAsync();
        var tx = await CreatePendingDepositAsync(seed.AccountId, seed.LedgerId, "maker-return", "Associate", amount: 145.00m, checkNumber: "CHK-700");

        var approveResponse = await SendTrustAsync(
            HttpMethod.Post,
            $"/api/trust/transactions/{tx.Id}/approve",
            payload: null,
            userId: "approver-return",
            role: "Partner");
        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);

        var returnResponse = await SendTrustAsync(
            HttpMethod.Post,
            $"/api/trust/transactions/{tx.Id}/return",
            new { reason = "NSF check" },
            userId: "partner-return",
            role: "Partner");

        var returnBody = await returnResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, returnResponse.StatusCode);

        var returned = JsonSerializer.Deserialize<TrustTransaction>(returnBody, JsonOptions());
        Assert.NotNull(returned);
        Assert.Equal("returned", returned!.ClearingStatus);
        Assert.Equal("APPROVED", returned.Status);
        Assert.Equal("NSF check", returned.ReturnReason);

        using var scope = CreateTenantScope();
        var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var account = await db.TrustBankAccounts.AsNoTracking().SingleAsync(a => a.Id == seed.AccountId);
        var ledger = await db.ClientTrustLedgers.AsNoTracking().SingleAsync(l => l.Id == seed.LedgerId);
        var journalEntries = await db.TrustJournalEntries.AsNoTracking()
            .Where(j => j.TrustTransactionId == tx.Id)
            .OrderBy(j => j.CreatedAt)
            .ToListAsync();

        Assert.Equal(0m, account.CurrentBalance);
        Assert.Equal(0m, account.ClearedBalance);
        Assert.Equal(0m, account.UnclearedBalance);
        Assert.Equal(0m, account.AvailableDisbursementCapacity);
        Assert.Equal(0m, ledger.RunningBalance);
        Assert.Equal(0m, ledger.ClearedBalance);
        Assert.Equal(0m, ledger.UnclearedBalance);
        Assert.Equal(0m, ledger.AvailableToDisburse);
        Assert.Equal(2, journalEntries.Count);
        Assert.Equal("posting", journalEntries[0].EntryKind);
        Assert.Equal("reversal", journalEntries[1].EntryKind);
        Assert.Equal(journalEntries[0].Id, journalEntries[1].ReversalOfTrustJournalEntryId);
    }

    [Fact]
    public async Task StatementImport_AndPacketGeneration_CreateReadyCanonicalPacket_WithAutoOutstandingItems()
    {
        var seed = await SeedTrustLedgerAsync();

        var clearedDeposit = await CreatePendingDepositAsync(seed.AccountId, seed.LedgerId, "maker-cleared", "Associate", amount: 300.00m, checkNumber: null);
        var approveClearedDeposit = await SendTrustAsync(
            HttpMethod.Post,
            $"/api/trust/transactions/{clearedDeposit.Id}/approve",
            payload: null,
            userId: "approver-cleared",
            role: "Partner");
        Assert.Equal(HttpStatusCode.OK, approveClearedDeposit.StatusCode);

        var unclearedDeposit = await CreatePendingDepositAsync(seed.AccountId, seed.LedgerId, "maker-uncleared-packet", "Associate", amount: 100.00m, checkNumber: "CHK-900");
        var approveUnclearedDeposit = await SendTrustAsync(
            HttpMethod.Post,
            $"/api/trust/transactions/{unclearedDeposit.Id}/approve",
            payload: null,
            userId: "approver-uncleared-packet",
            role: "Partner");
        Assert.Equal(HttpStatusCode.OK, approveUnclearedDeposit.StatusCode);

        var withdrawalResponse = await SendTrustAsync(
            HttpMethod.Post,
            "/api/trust/withdrawal",
            new
            {
                trustAccountId = seed.AccountId,
                ledgerId = seed.LedgerId,
                amount = 50.00m,
                description = "Settlement disbursement",
                payorPayee = "Vendor",
                checkNumber = "OUT-50"
            },
            userId: "maker-outstanding-check",
            role: "Associate");
        Assert.Equal(HttpStatusCode.OK, withdrawalResponse.StatusCode);

        var withdrawal = JsonSerializer.Deserialize<TrustTransaction>(await withdrawalResponse.Content.ReadAsStringAsync(), JsonOptions());
        Assert.NotNull(withdrawal);

        var approveWithdrawal = await SendTrustAsync(
            HttpMethod.Post,
            $"/api/trust/transactions/{withdrawal!.Id}/approve",
            payload: null,
            userId: "approver-outstanding-check",
            role: "Partner");
        Assert.Equal(HttpStatusCode.OK, approveWithdrawal.StatusCode);

        var importResponse = await SendTrustAsync(
            HttpMethod.Post,
            "/api/trust/statements/import",
            new
            {
                trustAccountId = seed.AccountId,
                periodStart = "2026-04-01T00:00:00Z",
                periodEnd = "2026-04-30T00:00:00Z",
                statementEndingBalance = 300.00m,
                source = "manual_test",
                lines = Array.Empty<object>()
            },
            userId: "statement-importer",
            role: "Partner");

        var importBody = await importResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, importResponse.StatusCode);
        var statementImport = JsonSerializer.Deserialize<TrustStatementImport>(importBody, JsonOptions());
        Assert.NotNull(statementImport);

        var packetResponse = await SendTrustAsync(
            HttpMethod.Post,
            "/api/trust/reconciliation-packets",
            new
            {
                trustAccountId = seed.AccountId,
                periodStart = "2026-04-01T00:00:00Z",
                periodEnd = "2026-04-30T00:00:00Z",
                statementImportId = statementImport!.Id,
                notes = "Month-end trust packet"
            },
            userId: "packet-preparer",
            role: "Partner");

        var packetBody = await packetResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, packetResponse.StatusCode);
        var packet = JsonSerializer.Deserialize<TrustReconciliationPacket>(packetBody, JsonOptions());
        Assert.NotNull(packet);
        Assert.Equal("ready_for_signoff", packet!.Status);
        Assert.Equal(0, packet.ExceptionCount);
        Assert.True(packet.IsCanonical);
        Assert.Equal(0, packet.UnmatchedStatementLineCount);
        Assert.Equal(300.00m, packet.StatementEndingBalance);
        Assert.Equal(350.00m, packet.AdjustedBankBalance);
        Assert.Equal(350.00m, packet.JournalBalance);
        Assert.Equal(350.00m, packet.ClientLedgerBalance);
        Assert.Equal(100.00m, packet.OutstandingDepositsTotal);
        Assert.Equal(50.00m, packet.OutstandingChecksTotal);

        using var scope = CreateTenantScope();
        var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var outstandingItems = await db.TrustOutstandingItems.AsNoTracking()
            .Where(i => i.TrustReconciliationPacketId == packet.Id)
            .OrderBy(i => i.ItemType)
            .ToListAsync();
        var reconRecord = await db.ReconciliationRecords.AsNoTracking()
            .SingleAsync(r => r.TrustAccountId == seed.AccountId && r.PeriodEnd == packet.PeriodEnd);

        Assert.Equal(2, outstandingItems.Count);
        Assert.Contains(outstandingItems, i => i.ItemType == "deposit_in_transit" && i.Amount == 100.00m);
        Assert.Contains(outstandingItems, i => i.ItemType == "outstanding_check" && i.Amount == 50.00m);
        Assert.True(reconRecord.IsReconciled);
        Assert.Equal(0m, reconRecord.DiscrepancyAmount);
    }

    [Fact]
    public async Task ReconciliationPacket_Signoff_RequiresDifferentUser()
    {
        var seed = await SeedTrustLedgerAsync();

        var importResponse = await SendTrustAsync(
            HttpMethod.Post,
            "/api/trust/statements/import",
            new
            {
                trustAccountId = seed.AccountId,
                periodStart = "2026-04-01T00:00:00Z",
                periodEnd = "2026-04-30T00:00:00Z",
                statementEndingBalance = 0.00m,
                source = "manual_test",
                lines = Array.Empty<object>()
            },
            userId: "packet-importer",
            role: "Partner");
        Assert.Equal(HttpStatusCode.OK, importResponse.StatusCode);
        var importDto = JsonSerializer.Deserialize<TrustStatementImport>(await importResponse.Content.ReadAsStringAsync(), JsonOptions());
        Assert.NotNull(importDto);

        var packetResponse = await SendTrustAsync(
            HttpMethod.Post,
            "/api/trust/reconciliation-packets",
            new
            {
                trustAccountId = seed.AccountId,
                periodStart = "2026-04-01T00:00:00Z",
                periodEnd = "2026-04-30T00:00:00Z",
                statementImportId = importDto!.Id,
                notes = "Clean packet"
            },
            userId: "packet-owner",
            role: "Partner");

        var packetBody = await packetResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, packetResponse.StatusCode);
        var packet = JsonSerializer.Deserialize<TrustReconciliationPacket>(packetBody, JsonOptions());
        Assert.NotNull(packet);

        var selfSignoff = await SendTrustAsync(
            HttpMethod.Post,
            $"/api/trust/reconciliation-packets/{packet!.Id}/signoff",
            new { notes = "Self signoff should fail" },
            userId: "packet-owner",
            role: "Partner");
        var selfBody = await selfSignoff.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.Forbidden, selfSignoff.StatusCode);
        Assert.Contains("Maker-checker", selfBody, StringComparison.OrdinalIgnoreCase);

        var signoffResponse = await SendTrustAsync(
            HttpMethod.Post,
            $"/api/trust/reconciliation-packets/{packet.Id}/signoff",
            new { notes = "Reviewed and signed." },
            userId: "signoff-partner",
            role: "Partner");
        var signoffBody = await signoffResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, signoffResponse.StatusCode);

        var signedPacket = JsonSerializer.Deserialize<TrustReconciliationPacket>(signoffBody, JsonOptions());
        Assert.NotNull(signedPacket);
        Assert.Equal("signed_off", signedPacket!.Status);

        using var scope = CreateTenantScope();
        var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var signoffs = await db.TrustReconciliationSignoffs.AsNoTracking()
            .Where(s => s.TrustReconciliationPacketId == packet.Id)
            .ToListAsync();

        Assert.Single(signoffs);
        Assert.Equal("signoff-partner", signoffs[0].SignedBy);
    }

    [Fact]
    public async Task ManualStatementMatch_RefreshesCanonicalPacket_AndClearsMatchingExceptions()
    {
        var seed = await SeedTrustLedgerAsync();
        var deposit = await CreatePendingDepositAsync(seed.AccountId, seed.LedgerId, "statement-maker", "Associate", amount: 120.00m, checkNumber: null);

        var approveResponse = await SendTrustAsync(
            HttpMethod.Post,
            $"/api/trust/transactions/{deposit.Id}/approve",
            payload: null,
            userId: "statement-approver",
            role: "Partner");
        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);

        var importResponse = await SendTrustAsync(
            HttpMethod.Post,
            "/api/trust/statements/import",
            new
            {
                trustAccountId = seed.AccountId,
                periodStart = "2026-04-01T00:00:00Z",
                periodEnd = "2026-04-30T00:00:00Z",
                statementEndingBalance = 120.00m,
                source = "manual_test",
                lines = new[]
                {
                    new
                    {
                        postedAt = "2026-04-30T00:00:00Z",
                        amount = 120.00m,
                        reference = "BANK-LINE-120",
                        description = "Wire received"
                    }
                }
            },
            userId: "statement-import-partner",
            role: "Partner");
        Assert.Equal(HttpStatusCode.OK, importResponse.StatusCode);
        var importDto = JsonSerializer.Deserialize<TrustStatementImport>(await importResponse.Content.ReadAsStringAsync(), JsonOptions());
        Assert.NotNull(importDto);

        var packetResponse = await SendTrustAsync(
            HttpMethod.Post,
            "/api/trust/reconciliation-packets",
            new
            {
                trustAccountId = seed.AccountId,
                periodStart = "2026-04-01T00:00:00Z",
                periodEnd = "2026-04-30T00:00:00Z",
                statementImportId = importDto!.Id
            },
            userId: "packet-builder",
            role: "Partner");
        Assert.Equal(HttpStatusCode.OK, packetResponse.StatusCode);

        var initialPacket = JsonSerializer.Deserialize<TrustReconciliationPacket>(await packetResponse.Content.ReadAsStringAsync(), JsonOptions());
        Assert.NotNull(initialPacket);
        Assert.Equal("matching_in_progress", initialPacket!.Status);
        Assert.Equal(1, initialPacket.UnmatchedStatementLineCount);

        var linesResponse = await SendTrustAsync(
            HttpMethod.Get,
            $"/api/trust/statements/{importDto.Id}/lines",
            payload: null,
            userId: "packet-builder",
            role: "Partner");
        Assert.Equal(HttpStatusCode.OK, linesResponse.StatusCode);
        var lines = JsonSerializer.Deserialize<List<TrustStatementLine>>(await linesResponse.Content.ReadAsStringAsync(), JsonOptions());
        Assert.NotNull(lines);
        var line = Assert.Single(lines!);

        var resolveResponse = await SendTrustAsync(
            HttpMethod.Post,
            $"/api/trust/statement-lines/{line.Id}/resolve",
            new
            {
                action = "match",
                trustTransactionId = deposit.Id,
                notes = "Manual tie-out."
            },
            userId: "statement-reviewer",
            role: "Partner");
        Assert.Equal(HttpStatusCode.OK, resolveResponse.StatusCode);

        using var scope = CreateTenantScope();
        var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var persistedLine = await db.TrustStatementLines.AsNoTracking().SingleAsync(l => l.Id == line.Id);
        var canonicalPacket = await db.TrustReconciliationPackets.AsNoTracking()
            .Where(p => p.TrustAccountId == seed.AccountId && p.PeriodEnd == new DateTime(2026, 4, 30, 0, 0, 0, DateTimeKind.Utc) && p.IsCanonical)
            .SingleAsync();
        var outstandingItems = await db.TrustOutstandingItems.AsNoTracking()
            .Where(i => i.TrustReconciliationPacketId == canonicalPacket.Id)
            .ToListAsync();

        Assert.Equal("matched", persistedLine.MatchStatus);
        Assert.Equal("manual", persistedLine.MatchMethod);
        Assert.Equal(deposit.Id, persistedLine.MatchedTrustTransactionId);
        Assert.Equal("ready_for_signoff", canonicalPacket.Status);
        Assert.Equal(0, canonicalPacket.UnmatchedStatementLineCount);
        Assert.Equal(1, canonicalPacket.MatchedStatementLineCount);
        Assert.Equal(0, canonicalPacket.ExceptionCount);
        Assert.Empty(outstandingItems);
    }

    [Fact]
    public async Task LegacyReconcile_WritesCompatibilityRecord_FromCanonicalPacketPipeline()
    {
        var seed = await SeedTrustLedgerAsync();
        var deposit = await CreatePendingDepositAsync(seed.AccountId, seed.LedgerId, "legacy-maker", "Associate", amount: 80.00m, checkNumber: null);

        var approveResponse = await SendTrustAsync(
            HttpMethod.Post,
            $"/api/trust/transactions/{deposit.Id}/approve",
            payload: null,
            userId: "legacy-approver",
            role: "Partner");
        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);

        var reconcileResponse = await SendTrustAsync(
            HttpMethod.Post,
            "/api/trust/reconcile",
            new
            {
                trustAccountId = seed.AccountId,
                periodEnd = "2026-04-30T00:00:00Z",
                bankStatementBalance = 80.00m,
                notes = "Legacy compatibility path"
            },
            userId: "legacy-reviewer",
            role: "Partner");
        var reconcileBody = await reconcileResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, reconcileResponse.StatusCode);

        var record = JsonSerializer.Deserialize<ReconciliationRecord>(reconcileBody, JsonOptions());
        Assert.NotNull(record);
        Assert.True(record!.IsReconciled);
        Assert.Equal(0m, record.DiscrepancyAmount);

        using var scope = CreateTenantScope();
        var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var packet = await db.TrustReconciliationPackets.AsNoTracking()
            .Where(p => p.TrustAccountId == seed.AccountId && p.PeriodEnd == new DateTime(2026, 4, 30, 0, 0, 0, DateTimeKind.Utc))
            .SingleAsync();

        Assert.True(packet.IsCanonical);
        Assert.Equal("draft", packet.Status);
        Assert.Equal(record.Id, (await db.ReconciliationRecords.AsNoTracking()
            .SingleAsync(r => r.TrustAccountId == seed.AccountId && r.PeriodEnd == packet.PeriodEnd)).Id);
    }

    [Fact]
    public async Task AccountJournalExport_GeneratesSnapshot_AndPersistsHistory()
    {
        var seed = await SeedTrustLedgerAsync();
        var tx = await CreatePendingDepositAsync(seed.AccountId, seed.LedgerId, "export-maker", "Associate", amount: 185.00m, checkNumber: null);

        var approveResponse = await SendTrustAsync(
            HttpMethod.Post,
            $"/api/trust/transactions/{tx.Id}/approve",
            payload: null,
            userId: "export-approver",
            role: "Partner");
        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);

        var exportResponse = await SendTrustAsync(
            HttpMethod.Post,
            "/api/trust/exports",
            new
            {
                exportType = "account_journal",
                format = "csv",
                trustAccountId = seed.AccountId
            },
            userId: "export-partner",
            role: "Partner");

        var exportBody = await exportResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, exportResponse.StatusCode);

        var export = JsonSerializer.Deserialize<TrustComplianceExportDto>(exportBody, JsonOptions());
        Assert.NotNull(export);
        Assert.Equal("account_journal", export!.ExportType);
        Assert.Equal("csv", export.Format);
        Assert.Contains("trust-account_journal", export.FileName, StringComparison.OrdinalIgnoreCase);

        using var payloadDoc = JsonDocument.Parse(export.PayloadJson ?? "{}");
        Assert.True(payloadDoc.RootElement.TryGetProperty("account", out var accountElement));
        Assert.Equal(seed.AccountId, accountElement.GetProperty("id").GetString());
        Assert.True(payloadDoc.RootElement.TryGetProperty("csvRows", out var rowsElement));
        Assert.True(rowsElement.GetArrayLength() >= 1);

        using var scope = CreateTenantScope();
        var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var persisted = await db.TrustComplianceExports.AsNoTracking().SingleAsync(x => x.Id == export.Id);
        Assert.Equal(seed.AccountId, persisted.TrustAccountId);
        Assert.Equal("completed", persisted.Status);
    }

    [Fact]
    public async Task MonthClosePacketExport_IncludesPacketAndCloseScope()
    {
        var seed = await SeedTrustLedgerAsync();

        var importResponse = await SendTrustAsync(
            HttpMethod.Post,
            "/api/trust/statements/import",
            new
            {
                trustAccountId = seed.AccountId,
                periodStart = "2026-04-01T00:00:00Z",
                periodEnd = "2026-04-30T00:00:00Z",
                statementEndingBalance = 0.00m,
                source = "manual",
                lines = new[]
                {
                    new
                    {
                        postedAt = "2026-04-28T00:00:00Z",
                        amount = -25.00m,
                        description = "Outstanding check",
                        reference = "CHK-404"
                    }
                }
            },
            userId: "packet-export-importer",
            role: "Partner");
        Assert.Equal(HttpStatusCode.OK, importResponse.StatusCode);
        var importDto = JsonSerializer.Deserialize<TrustStatementImport>(await importResponse.Content.ReadAsStringAsync(), JsonOptions());
        Assert.NotNull(importDto);

        var outstandingResponse = await SendTrustAsync(
            HttpMethod.Post,
            "/api/trust/outstanding-items",
            new
            {
                trustAccountId = seed.AccountId,
                periodStart = "2026-04-01T00:00:00Z",
                periodEnd = "2026-04-30T00:00:00Z",
                itemType = "outstanding_check",
                impactDirection = "decrease_bank",
                amount = 25.00m,
                reference = "CHK-404",
                description = "Manual outstanding check"
            },
            userId: "packet-export-reviewer",
            role: "Partner");
        Assert.Equal(HttpStatusCode.OK, outstandingResponse.StatusCode);

        var packetResponse = await SendTrustAsync(
            HttpMethod.Post,
            "/api/trust/reconciliation-packets",
            new
            {
                trustAccountId = seed.AccountId,
                periodStart = "2026-04-01T00:00:00Z",
                periodEnd = "2026-04-30T00:00:00Z",
                statementImportId = importDto!.Id,
                notes = "Packet for export"
            },
            userId: "packet-export-preparer",
            role: "Partner");
        Assert.Equal(HttpStatusCode.OK, packetResponse.StatusCode);

        var packet = JsonSerializer.Deserialize<TrustReconciliationPacket>(await packetResponse.Content.ReadAsStringAsync(), JsonOptions());
        Assert.NotNull(packet);

        var monthCloseResponse = await SendTrustAsync(
            HttpMethod.Post,
            "/api/trust/month-close/prepare",
            new
            {
                trustAccountId = seed.AccountId,
                periodStart = "2026-04-01T00:00:00Z",
                periodEnd = "2026-04-30T00:00:00Z",
                reconciliationPacketId = packet!.Id,
                autoGeneratePacket = false,
                notes = "Close for export"
            },
            userId: "month-close-export-preparer",
            role: "Partner");
        var monthCloseBody = await monthCloseResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, monthCloseResponse.StatusCode);

        var close = JsonSerializer.Deserialize<TrustMonthCloseDto>(monthCloseBody, JsonOptions());
        Assert.NotNull(close);

        var exportResponse = await SendTrustAsync(
            HttpMethod.Post,
            "/api/trust/exports",
            new
            {
                exportType = "month_close_packet",
                format = "pdf",
                trustAccountId = seed.AccountId,
                trustMonthCloseId = close!.Id,
                trustReconciliationPacketId = packet.Id
            },
            userId: "month-close-exporter",
            role: "Partner");

        var exportBody = await exportResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, exportResponse.StatusCode);

        var export = JsonSerializer.Deserialize<TrustComplianceExportDto>(exportBody, JsonOptions());
        Assert.NotNull(export);
        Assert.Equal("month_close_packet", export!.ExportType);
        Assert.Equal("pdf", export.Format);

        using var payloadDoc = JsonDocument.Parse(export.PayloadJson ?? "{}");
        Assert.Equal(packet.Id, payloadDoc.RootElement.GetProperty("packet").GetProperty("id").GetString());
        Assert.Equal(close.Id, payloadDoc.RootElement.GetProperty("monthClose").GetProperty("id").GetString());
        Assert.True(payloadDoc.RootElement.GetProperty("steps").GetArrayLength() >= 1);
        Assert.Equal(importDto.Id, payloadDoc.RootElement.GetProperty("statementSummary").GetProperty("id").GetString());
        Assert.Equal(1, payloadDoc.RootElement.GetProperty("statementLines").GetArrayLength());
        Assert.True(payloadDoc.RootElement.GetProperty("outstandingChecks").GetArrayLength() >= 1);
        Assert.True(payloadDoc.RootElement.TryGetProperty("signoffChain", out _));

        using var scope = CreateTenantScope();
        var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var persisted = await db.TrustComplianceExports.AsNoTracking().SingleAsync(x => x.Id == export.Id);
        Assert.Equal(close.Id, persisted.TrustMonthCloseId);
        Assert.Equal(packet.Id, persisted.TrustReconciliationPacketId);
    }

    [Fact]
    public async Task DepositReplay_WithSameIdempotencyKey_ReturnsSameTransaction_AndDoesNotDuplicate()
    {
        var seed = await SeedTrustLedgerAsync();
        const string idempotencyKey = "trust-deposit-replay-1";

        var firstResponse = await SendTrustAsync(
            HttpMethod.Post,
            "/api/trust/deposit",
            new
            {
                trustAccountId = seed.AccountId,
                amount = 240.00,
                description = "Replay-safe retainer",
                payorPayee = "Client",
                checkNumber = "CHK-300",
                allocations = new[]
                {
                    new { ledgerId = seed.LedgerId, amount = 240.00, description = "Replay funding" }
                }
            },
            userId: "maker-replay",
            role: "Admin",
            idempotencyKey: idempotencyKey);

        var secondResponse = await SendTrustAsync(
            HttpMethod.Post,
            "/api/trust/deposit",
            new
            {
                trustAccountId = seed.AccountId,
                amount = 240.00,
                description = "Replay-safe retainer",
                payorPayee = "Client",
                checkNumber = "CHK-300",
                allocations = new[]
                {
                    new { ledgerId = seed.LedgerId, amount = 240.00, description = "Replay funding" }
                }
            },
            userId: "maker-replay",
            role: "Admin",
            idempotencyKey: idempotencyKey);

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);

        var first = JsonSerializer.Deserialize<TrustTransaction>(await firstResponse.Content.ReadAsStringAsync(), JsonOptions());
        var second = JsonSerializer.Deserialize<TrustTransaction>(await secondResponse.Content.ReadAsStringAsync(), JsonOptions());

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first!.Id, second!.Id);

        using var scope = CreateTenantScope();
        var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var txCount = await db.TrustTransactions.CountAsync(t => t.CreatedBy == "maker-replay");
        var dedupCount = await db.TrustCommandDeduplications.CountAsync(d => d.ActorUserId == "maker-replay" && d.CommandName == "trust_deposit_create");

        Assert.Equal(1, txCount);
        Assert.Equal(1, dedupCount);
    }

    [Fact]
    public async Task DepositReplay_WithSameIdempotencyKeyAndDifferentPayload_ReturnsConflict()
    {
        var seed = await SeedTrustLedgerAsync();
        const string idempotencyKey = "trust-deposit-replay-2";

        var firstResponse = await SendTrustAsync(
            HttpMethod.Post,
            "/api/trust/deposit",
            new
            {
                trustAccountId = seed.AccountId,
                amount = 90.00,
                description = "Initial payload",
                payorPayee = "Client",
                allocations = new[]
                {
                    new { ledgerId = seed.LedgerId, amount = 90.00, description = "Initial" }
                }
            },
            userId: "maker-conflict",
            role: "Admin",
            idempotencyKey: idempotencyKey);
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        var secondResponse = await SendTrustAsync(
            HttpMethod.Post,
            "/api/trust/deposit",
            new
            {
                trustAccountId = seed.AccountId,
                amount = 95.00,
                description = "Changed payload",
                payorPayee = "Client",
                allocations = new[]
                {
                    new { ledgerId = seed.LedgerId, amount = 95.00, description = "Changed" }
                }
            },
            userId: "maker-conflict",
            role: "Admin",
            idempotencyKey: idempotencyKey);

        var body = await secondResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
        Assert.Contains("Idempotency key", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ApproveReplay_WithSameIdempotencyKey_ReturnsSameApprovedTransaction_AndDoesNotDuplicateJournal()
    {
        var seed = await SeedTrustLedgerAsync();
        var tx = await CreatePendingDepositAsync(seed.AccountId, seed.LedgerId, "maker-approve-replay", "Associate");
        const string idempotencyKey = "trust-approve-replay-1";

        var firstResponse = await SendTrustAsync(
            HttpMethod.Post,
            $"/api/trust/transactions/{tx.Id}/approve",
            payload: null,
            userId: "approver-replay",
            role: "Partner",
            idempotencyKey: idempotencyKey);

        var secondResponse = await SendTrustAsync(
            HttpMethod.Post,
            $"/api/trust/transactions/{tx.Id}/approve",
            payload: null,
            userId: "approver-replay",
            role: "Partner",
            idempotencyKey: idempotencyKey);

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);

        var first = JsonSerializer.Deserialize<TrustTransaction>(await firstResponse.Content.ReadAsStringAsync(), JsonOptions());
        var second = JsonSerializer.Deserialize<TrustTransaction>(await secondResponse.Content.ReadAsStringAsync(), JsonOptions());

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first!.Id, second!.Id);
        Assert.Equal("APPROVED", second.Status);

        using var scope = CreateTenantScope();
        var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var journalCount = await db.TrustJournalEntries.CountAsync(j => j.TrustTransactionId == tx.Id && j.EntryKind == "posting");
        var dedupCount = await db.TrustCommandDeduplications.CountAsync(d => d.ActorUserId == "approver-replay" && d.CommandName == "trust_transaction_approve");

        Assert.Equal(1, journalCount);
        Assert.Equal(1, dedupCount);
    }

    [Fact]
    public async Task BillingAllocationReplay_WithSameIdempotencyKey_ReturnsSameAllocation_AndDoesNotDuplicate()
    {
        var seed = await SeedBillingAllocationAsync();
        const string idempotencyKey = "billing-allocation-replay-1";

        var firstResponse = await SendBillingAsync(
            HttpMethod.Post,
            "/api/legal-billing/allocations",
            new
            {
                paymentTransactionId = seed.PaymentId,
                invoiceId = seed.InvoiceId,
                amount = 80.00m,
                fundSource = "operating",
                allocationType = "invoice_header",
                idempotencyKey
            },
            userId: "billing-operator",
            role: "Admin");

        var secondResponse = await SendBillingAsync(
            HttpMethod.Post,
            "/api/legal-billing/allocations",
            new
            {
                paymentTransactionId = seed.PaymentId,
                invoiceId = seed.InvoiceId,
                amount = 80.00m,
                fundSource = "operating",
                allocationType = "invoice_header",
                idempotencyKey
            },
            userId: "billing-operator",
            role: "Admin");

        var firstBody = await firstResponse.Content.ReadAsStringAsync();
        var secondBody = await secondResponse.Content.ReadAsStringAsync();

        Assert.True(firstResponse.StatusCode == HttpStatusCode.OK, firstBody);
        Assert.True(secondResponse.StatusCode == HttpStatusCode.OK, secondBody);

        var first = JsonSerializer.Deserialize<BillingPaymentAllocation>(firstBody, JsonOptions());
        var second = JsonSerializer.Deserialize<BillingPaymentAllocation>(secondBody, JsonOptions());

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first!.Id, second!.Id);

        using var scope = CreateTenantScope();
        var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var allocationCount = await db.BillingPaymentAllocations.CountAsync(a => a.PaymentTransactionId == seed.PaymentId);

        Assert.Equal(1, allocationCount);
    }

    [Fact]
    public async Task TrustFundedBillingAllocation_PostsLinkedEarnedFeeTransfer_AndUpdatesTrustBalances()
    {
        var seed = await SeedTrustFundedBillingAllocationAsync();

        var response = await SendBillingAsync(
            HttpMethod.Post,
            "/api/legal-billing/allocations",
            new
            {
                paymentTransactionId = seed.PaymentId,
                invoiceId = seed.InvoiceId,
                trustAccountId = seed.AccountId,
                trustLedgerId = seed.LedgerId,
                amount = 80.00m,
                fundSource = "trust",
                allocationType = "invoice_header",
                reference = "trust-allocation-1"
            },
            userId: "billing-trust-operator",
            role: "Partner");

        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, body);

        var allocation = JsonSerializer.Deserialize<BillingPaymentAllocation>(body, JsonOptions());
        Assert.NotNull(allocation);
        Assert.Equal("pending_trust_approval", allocation!.Status);

        var approveResponse = await SendTrustAsync(
            HttpMethod.Post,
            $"/api/trust/transactions/{allocation.TrustTransactionId}/approve",
            payload: null,
            userId: "billing-trust-approver",
            role: "Partner");
        var approveBody = await approveResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);
        Assert.Contains("\"status\":\"APPROVED\"", approveBody, StringComparison.OrdinalIgnoreCase);

        using var scope = CreateTenantScope();
        var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var account = await db.TrustBankAccounts.AsNoTracking().SingleAsync(a => a.Id == seed.AccountId);
        var ledger = await db.ClientTrustLedgers.AsNoTracking().SingleAsync(l => l.Id == seed.LedgerId);
        var trustTx = await db.TrustTransactions.AsNoTracking()
            .SingleAsync(t => t.Reference == "trust-allocation-1");
        var trustJournal = await db.TrustJournalEntries.AsNoTracking()
            .SingleAsync(j => j.TrustTransactionId == trustTx.Id && j.EntryKind == "posting");
        var billingLedgerEntries = await db.BillingLedgerEntries.AsNoTracking()
            .Where(e => e.CorrelationKey != null && e.CorrelationKey.StartsWith($"allocation:{allocation!.Id}:"))
            .OrderBy(e => e.CorrelationKey)
            .ToListAsync();

        Assert.Equal("EARNED_FEE_TRANSFER", trustTx.Type);
        Assert.Equal("APPROVED", trustTx.Status);
        Assert.True(trustTx.IsEarned);
        Assert.Equal(-80.00m, trustJournal.Amount);
        Assert.Equal("earned_fee_transfer", trustJournal.OperationType);
        Assert.All(billingLedgerEntries.Where(e => e.LedgerDomain != "billing"), entry => Assert.Equal(trustTx.Id, entry.TrustTransactionId));
        Assert.Equal(220.00m, account.CurrentBalance);
        Assert.Equal(220.00m, account.ClearedBalance);
        Assert.Equal(220.00m, account.AvailableDisbursementCapacity);
        Assert.Equal(220.00m, ledger.RunningBalance);
        Assert.Equal(220.00m, ledger.ClearedBalance);
        Assert.Equal(220.00m, ledger.AvailableToDisburse);
    }

    [Fact]
    public async Task ReverseTrustFundedBillingAllocation_VoidsLinkedEarnedFeeTransfer_AndRestoresTrustBalances()
    {
        var seed = await SeedTrustFundedBillingAllocationAsync();
        var applyResponse = await SendBillingAsync(
            HttpMethod.Post,
            "/api/legal-billing/allocations",
            new
            {
                paymentTransactionId = seed.PaymentId,
                invoiceId = seed.InvoiceId,
                trustAccountId = seed.AccountId,
                trustLedgerId = seed.LedgerId,
                amount = 55.00m,
                fundSource = "trust",
                allocationType = "invoice_header",
                reference = "trust-allocation-reversal"
            },
            userId: "billing-trust-reverser",
            role: "Partner");
        Assert.Equal(HttpStatusCode.OK, applyResponse.StatusCode);

        var allocation = JsonSerializer.Deserialize<BillingPaymentAllocation>(await applyResponse.Content.ReadAsStringAsync(), JsonOptions());
        Assert.NotNull(allocation);
        Assert.Equal("pending_trust_approval", allocation!.Status);

        var approveResponse = await SendTrustAsync(
            HttpMethod.Post,
            $"/api/trust/transactions/{allocation.TrustTransactionId}/approve",
            payload: null,
            userId: "billing-trust-reversal-approver",
            role: "Partner");
        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);

        var reverseResponse = await SendBillingAsync(
            HttpMethod.Post,
            $"/api/legal-billing/allocations/{allocation!.Id}/reverse",
            new { reason = "Allocation cancelled" },
            userId: "billing-trust-reverser",
            role: "Partner");

        var reverseBody = await reverseResponse.Content.ReadAsStringAsync();
        Assert.True(reverseResponse.StatusCode == HttpStatusCode.OK, reverseBody);

        using var scope = CreateTenantScope();
        var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var account = await db.TrustBankAccounts.AsNoTracking().SingleAsync(a => a.Id == seed.AccountId);
        var ledger = await db.ClientTrustLedgers.AsNoTracking().SingleAsync(l => l.Id == seed.LedgerId);
        var trustTx = await db.TrustTransactions.AsNoTracking()
            .SingleAsync(t => t.Reference == "trust-allocation-reversal");
        var trustJournalEntries = await db.TrustJournalEntries.AsNoTracking()
            .Where(j => j.TrustTransactionId == trustTx.Id)
            .OrderBy(j => j.CreatedAt)
            .ToListAsync();

        Assert.Equal("VOIDED", trustTx.Status);
        Assert.Equal(300.00m, account.CurrentBalance);
        Assert.Equal(300.00m, account.ClearedBalance);
        Assert.Equal(300.00m, account.AvailableDisbursementCapacity);
        Assert.Equal(300.00m, ledger.RunningBalance);
        Assert.Equal(300.00m, ledger.ClearedBalance);
        Assert.Equal(300.00m, ledger.AvailableToDisburse);
        Assert.Equal(2, trustJournalEntries.Count);
        Assert.Equal("posting", trustJournalEntries[0].EntryKind);
        Assert.Equal("reversal", trustJournalEntries[1].EntryKind);
        Assert.Equal(-trustJournalEntries[0].Amount, trustJournalEntries[1].Amount);
    }

    [Fact]
    public async Task ProjectionRebuild_RestoresTrustProjections_FromJournal()
    {
        var seed = await SeedTrustLedgerAsync();
        var tx = await CreatePendingDepositAsync(seed.AccountId, seed.LedgerId, "projection-maker", "Associate", amount: 140.00m, checkNumber: null);

        var approveResponse = await SendTrustAsync(
            HttpMethod.Post,
            $"/api/trust/transactions/{tx.Id}/approve",
            payload: null,
            userId: "projection-approver",
            role: "Partner");
        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);

        using (var scope = CreateTenantScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
            var account = await db.TrustBankAccounts.SingleAsync(a => a.Id == seed.AccountId);
            var ledger = await db.ClientTrustLedgers.SingleAsync(l => l.Id == seed.LedgerId);
            account.CurrentBalance = 0m;
            account.ClearedBalance = 0m;
            account.UnclearedBalance = 0m;
            account.AvailableDisbursementCapacity = 0m;
            ledger.RunningBalance = 0m;
            ledger.ClearedBalance = 0m;
            ledger.UnclearedBalance = 0m;
            ledger.AvailableToDisburse = 0m;
            await db.SaveChangesAsync();
        }

        var healthResponse = await SendTrustAsync(
            HttpMethod.Get,
            $"/api/trust/projection-health?trustAccountId={seed.AccountId}",
            payload: null,
            userId: "projection-admin",
            role: "Admin");
        var healthBody = await healthResponse.Content.ReadAsStringAsync();
        Assert.True(healthResponse.StatusCode == HttpStatusCode.OK, healthBody);

        var health = JsonSerializer.Deserialize<TrustProjectionHealthResponse>(healthBody, JsonOptions());
        Assert.NotNull(health);
        Assert.Single(health!.Accounts);
        Assert.True(health.Accounts[0].HasDrift);

        var rebuildResponse = await SendTrustAsync(
            HttpMethod.Post,
            "/api/trust/projection-rebuild",
            new { trustAccountId = seed.AccountId, onlyIfDrifted = true },
            userId: "projection-admin",
            role: "Admin");
        var rebuildBody = await rebuildResponse.Content.ReadAsStringAsync();
        Assert.True(rebuildResponse.StatusCode == HttpStatusCode.OK, rebuildBody);

        using var verifyScope = CreateTenantScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var rebuiltAccount = await verifyDb.TrustBankAccounts.AsNoTracking().SingleAsync(a => a.Id == seed.AccountId);
        var rebuiltLedger = await verifyDb.ClientTrustLedgers.AsNoTracking().SingleAsync(l => l.Id == seed.LedgerId);

        Assert.Equal(140.00m, rebuiltAccount.CurrentBalance);
        Assert.Equal(140.00m, rebuiltAccount.ClearedBalance);
        Assert.Equal(0m, rebuiltAccount.UnclearedBalance);
        Assert.Equal(140.00m, rebuiltAccount.AvailableDisbursementCapacity);
        Assert.Equal(140.00m, rebuiltLedger.RunningBalance);
        Assert.Equal(140.00m, rebuiltLedger.ClearedBalance);
        Assert.Equal(0m, rebuiltLedger.UnclearedBalance);
        Assert.Equal(140.00m, rebuiltLedger.AvailableToDisburse);
    }

    [Fact]
    public async Task AsOfRecovery_PreviewsHistoricalBalances_WithoutMutatingCurrentProjection()
    {
        var seed = await SeedTrustLedgerAsync();
        var tx = await CreatePendingDepositAsync(seed.AccountId, seed.LedgerId, "asof-maker", "Associate", amount: 90.00m, checkNumber: null);

        var approveResponse = await SendTrustAsync(
            HttpMethod.Post,
            $"/api/trust/transactions/{tx.Id}/approve",
            payload: null,
            userId: "asof-approver",
            role: "Partner");
        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);

        var asOfResponse = await SendTrustAsync(
            HttpMethod.Post,
            "/api/trust/recovery/as-of-rebuild",
            new
            {
                trustAccountId = seed.AccountId,
                asOfUtc = DateTime.UtcNow.AddDays(-2).ToString("O"),
                commitProjectionRepair = false,
                onlyIfDrifted = true
            },
            userId: "asof-admin",
            role: "Admin");
        var asOfBody = await asOfResponse.Content.ReadAsStringAsync();
        Assert.True(asOfResponse.IsSuccessStatusCode, asOfBody);

        var result = JsonSerializer.Deserialize<TrustAsOfProjectionRecoveryResult>(asOfBody, JsonOptions());
        Assert.NotNull(result);
        Assert.True(result!.HistoricalPreviewOnly);
        Assert.Single(result.Accounts);
        Assert.Equal(0m, result.Accounts[0].AsOfCurrentBalance);
        Assert.Equal(0m, result.Accounts[0].Ledgers[0].AsOfRunningBalance);

        using var verifyScope = CreateTenantScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var persistedAccount = await verifyDb.TrustBankAccounts.AsNoTracking().SingleAsync(a => a.Id == seed.AccountId);
        var persistedLedger = await verifyDb.ClientTrustLedgers.AsNoTracking().SingleAsync(l => l.Id == seed.LedgerId);
        Assert.Equal(90.00m, persistedAccount.CurrentBalance);
        Assert.Equal(90.00m, persistedLedger.RunningBalance);
    }

    [Fact]
    public async Task PacketRegeneration_SupersedesCanonicalPacket_AndPreparesReplacementMonthClose()
    {
        var seed = await SeedTrustLedgerAsync();
        var importResponse = await SendTrustAsync(
            HttpMethod.Post,
            "/api/trust/statements/import",
            new
            {
                trustAccountId = seed.AccountId,
                periodStart = "2026-04-01T00:00:00Z",
                periodEnd = "2026-04-30T00:00:00Z",
                statementEndingBalance = 0.00m,
                source = "manual",
                lines = Array.Empty<object>()
            },
            userId: "regen-importer",
            role: "Partner");
        Assert.Equal(HttpStatusCode.OK, importResponse.StatusCode);
        var importDto = JsonSerializer.Deserialize<TrustStatementImport>(await importResponse.Content.ReadAsStringAsync(), JsonOptions());
        Assert.NotNull(importDto);

        var packetResponse = await SendTrustAsync(
            HttpMethod.Post,
            "/api/trust/reconciliation-packets",
            new
            {
                trustAccountId = seed.AccountId,
                periodStart = "2026-04-01T00:00:00Z",
                periodEnd = "2026-04-30T00:00:00Z",
                statementImportId = importDto!.Id
            },
            userId: "regen-preparer",
            role: "Partner");
        var packetBody = await packetResponse.Content.ReadAsStringAsync();
        Assert.True(packetResponse.IsSuccessStatusCode, packetBody);
        var packet = JsonSerializer.Deserialize<TrustReconciliationPacket>(packetBody, JsonOptions());
        Assert.NotNull(packet);

        var regenerateResponse = await SendTrustAsync(
            HttpMethod.Post,
            "/api/trust/recovery/packet-regeneration",
            new
            {
                trustAccountId = seed.AccountId,
                trustReconciliationPacketId = packet!.Id,
                reason = "Recovery rerun after statement normalization",
                notes = "Phase 5E test",
                autoPrepareMonthClose = true
            },
            userId: "regen-admin",
            role: "Admin");
        var regenerateBody = await regenerateResponse.Content.ReadAsStringAsync();
        Assert.True(regenerateResponse.IsSuccessStatusCode, regenerateBody);

        var regenerateResult = JsonSerializer.Deserialize<TrustPacketRegenerationResult>(regenerateBody, JsonOptions());
        Assert.NotNull(regenerateResult);
        Assert.Equal(packet.Id, regenerateResult!.SourcePacketId);
        Assert.NotEqual(packet.Id, regenerateResult.PacketId);
        Assert.False(string.IsNullOrWhiteSpace(regenerateResult.TrustMonthCloseId));

        using var verifyScope = CreateTenantScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var oldPacket = await verifyDb.TrustReconciliationPackets.AsNoTracking().SingleAsync(x => x.Id == packet.Id);
        var replacementPacket = await verifyDb.TrustReconciliationPackets.AsNoTracking().SingleAsync(x => x.Id == regenerateResult.PacketId);
        var monthClose = await verifyDb.TrustMonthCloses.AsNoTracking().SingleAsync(x => x.Id == regenerateResult.TrustMonthCloseId);

        Assert.False(oldPacket.IsCanonical);
        Assert.Equal(replacementPacket.Id, oldPacket.SupersededByPacketId);
        Assert.True(replacementPacket.IsCanonical);
        Assert.Equal(replacementPacket.Id, monthClose.ReconciliationPacketId);
    }

    [Fact]
    public async Task ComplianceBundle_GeneratesManifestAndArtifactExports()
    {
        var seed = await SeedTrustLedgerAsync();
        var importResponse = await SendTrustAsync(
            HttpMethod.Post,
            "/api/trust/statements/import",
            new
            {
                trustAccountId = seed.AccountId,
                periodStart = "2026-05-01T00:00:00Z",
                periodEnd = "2026-05-31T00:00:00Z",
                statementEndingBalance = 0.00m,
                source = "manual",
                lines = Array.Empty<object>()
            },
            userId: "bundle-importer",
            role: "Partner");
        Assert.Equal(HttpStatusCode.OK, importResponse.StatusCode);
        var importDto = JsonSerializer.Deserialize<TrustStatementImport>(await importResponse.Content.ReadAsStringAsync(), JsonOptions());
        Assert.NotNull(importDto);

        var packetResponse = await SendTrustAsync(
            HttpMethod.Post,
            "/api/trust/reconciliation-packets",
            new
            {
                trustAccountId = seed.AccountId,
                periodStart = "2026-05-01T00:00:00Z",
                periodEnd = "2026-05-31T00:00:00Z",
                statementImportId = importDto!.Id
            },
            userId: "bundle-preparer",
            role: "Partner");
        var packetBody = await packetResponse.Content.ReadAsStringAsync();
        Assert.True(packetResponse.IsSuccessStatusCode, packetBody);
        var packet = JsonSerializer.Deserialize<TrustReconciliationPacket>(packetBody, JsonOptions());
        Assert.NotNull(packet);

        var closeResponse = await SendTrustAsync(
            HttpMethod.Post,
            "/api/trust/month-close/prepare",
            new
            {
                trustAccountId = seed.AccountId,
                periodStart = "2026-05-01T00:00:00Z",
                periodEnd = "2026-05-31T00:00:00Z",
                reconciliationPacketId = packet!.Id,
                autoGeneratePacket = false
            },
            userId: "bundle-closer",
            role: "Partner");
        var closeBody = await closeResponse.Content.ReadAsStringAsync();
        Assert.True(closeResponse.IsSuccessStatusCode, closeBody);
        var close = JsonSerializer.Deserialize<TrustMonthCloseDto>(closeBody, JsonOptions());
        Assert.NotNull(close);

        var bundleResponse = await SendTrustAsync(
            HttpMethod.Post,
            "/api/trust/recovery/compliance-bundle",
            new
            {
                trustAccountId = seed.AccountId,
                trustMonthCloseId = close!.Id,
                trustReconciliationPacketId = packet.Id,
                includeJsonPacket = true,
                includeAccountJournalCsv = true,
                includeApprovalRegisterCsv = true,
                includeClientLedgerCards = true,
                notes = "Phase 5E bundle"
            },
            userId: "bundle-admin",
            role: "Admin");
        var bundleBody = await bundleResponse.Content.ReadAsStringAsync();
        Assert.True(bundleResponse.IsSuccessStatusCode, bundleBody);

        var bundleResult = JsonSerializer.Deserialize<TrustComplianceBundleResult>(bundleBody, JsonOptions());
        Assert.NotNull(bundleResult);
        Assert.True(bundleResult!.ExportCount >= 4);
        Assert.Contains(bundleResult.Exports, export => export.ExportType == "month_close_packet" && export.Format == "pdf");
        Assert.False(string.IsNullOrWhiteSpace(bundleResult.ManifestExportId));

        using var verifyScope = CreateTenantScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var manifest = await verifyDb.TrustComplianceExports.AsNoTracking().SingleAsync(x => x.Id == bundleResult.ManifestExportId);
        Assert.Equal("compliance_bundle_manifest", manifest.ExportType);
        Assert.Equal(seed.AccountId, manifest.TrustAccountId);
    }

    [Fact]
    public async Task GovernanceUpdate_PersistsResponsibleLawyerSignatories_AndPolicyAssignment()
    {
        var seed = await SeedTrustLedgerAsync();

        var updateResponse = await SendTrustAsync(
            HttpMethod.Post,
            $"/api/trust/accounts/{seed.AccountId}/governance",
            new
            {
                accountType = "non_iolta",
                responsibleLawyerUserId = "partner-lawyer-1",
                allowedSignatories = new[] { "signer-a", "signer-b" },
                jurisdictionPolicyKey = "CA-premium",
                statementCadence = "monthly",
                overdraftNotificationEnabled = true
            },
            userId: "admin-gov",
            role: "Admin");

        var updateBody = await updateResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var governance = JsonSerializer.Deserialize<TrustAccountGovernanceDto>(updateBody, JsonOptions());
        Assert.NotNull(governance);
        Assert.Equal("non_iolta", governance!.AccountType);
        Assert.Equal("partner-lawyer-1", governance.ResponsibleLawyerUserId);
        Assert.Contains("signer-a", governance.AllowedSignatories);
        Assert.Equal("CA-premium", governance.JurisdictionPolicyKey);

        var getResponse = await SendTrustAsync(
            HttpMethod.Get,
            $"/api/trust/accounts/{seed.AccountId}/governance",
            payload: null,
            userId: "admin-gov",
            role: "Admin");

        var getBody = await getResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.Contains("partner-lawyer-1", getBody, StringComparison.Ordinal);
        Assert.Contains("signer-b", getBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithdrawalAboveThreshold_RequiresTwoOperationalApprovals()
    {
        var seed = await SeedTrustLedgerAsync();
        await FundTrustLedgerAsync(seed.AccountId, seed.LedgerId, 500m);
        await UpsertPolicyAsync("CA-dual", new
        {
            policyKey = "CA-dual",
            jurisdiction = "CA",
            dualApprovalThreshold = 100m,
            responsibleLawyerApprovalThreshold = 100000m,
            signatoryApprovalThreshold = 100000m,
            operationalApproverRoles = new[] { "Partner", "Accountant" },
            overrideApproverRoles = new[] { "Admin", "Partner" },
            disbursementClassesRequiringSignatory = Array.Empty<string>()
        });
        await UpdateGovernanceAsync(seed.AccountId, new
        {
            accountType = "iolta",
            jurisdictionPolicyKey = "CA-dual",
            statementCadence = "monthly",
            overdraftNotificationEnabled = true,
            allowedSignatories = Array.Empty<string>()
        });

        var tx = await CreatePendingWithdrawalAsync(seed.AccountId, seed.LedgerId, "maker-w1", "Associate", 150m);

        var firstApprove = await SendTrustAsync(
            HttpMethod.Post,
            $"/api/trust/transactions/{tx.Id}/approve-step",
            new { notes = "ops approval 1" },
            userId: "approver-w1",
            role: "Partner");

        var firstBody = await firstApprove.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, firstApprove.StatusCode);
        var firstTx = JsonSerializer.Deserialize<TrustTransaction>(firstBody, JsonOptions());
        Assert.NotNull(firstTx);
        Assert.Equal("PENDING", firstTx!.Status);
        Assert.Equal("partially_approved", firstTx.ApprovalStatus);

        var secondApprove = await SendTrustAsync(
            HttpMethod.Post,
            $"/api/trust/transactions/{tx.Id}/approve-step",
            new { notes = "ops approval 2" },
            userId: "approver-w2",
            role: "Partner");

        var secondBody = await secondApprove.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, secondApprove.StatusCode);
        var approvedTx = JsonSerializer.Deserialize<TrustTransaction>(secondBody, JsonOptions());
        Assert.NotNull(approvedTx);
        Assert.Equal("APPROVED", approvedTx!.Status);

        using var scope = CreateTenantScope();
        var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var account = await db.TrustBankAccounts.AsNoTracking().SingleAsync(a => a.Id == seed.AccountId);
        var ledger = await db.ClientTrustLedgers.AsNoTracking().SingleAsync(l => l.Id == seed.LedgerId);
        Assert.Equal(350m, account.CurrentBalance);
        Assert.Equal(350m, ledger.RunningBalance);
    }

    [Fact]
    public async Task WithdrawalRequiringSignatory_StaysPendingUntilConfiguredSignatoryApproves()
    {
        var seed = await SeedTrustLedgerAsync();
        await FundTrustLedgerAsync(seed.AccountId, seed.LedgerId, 500m);
        await UpsertPolicyAsync("CA-signatory", new
        {
            policyKey = "CA-signatory",
            jurisdiction = "CA",
            dualApprovalThreshold = 100000m,
            responsibleLawyerApprovalThreshold = 100000m,
            signatoryApprovalThreshold = 50m,
            operationalApproverRoles = new[] { "Partner" },
            overrideApproverRoles = new[] { "Admin", "Partner" },
            disbursementClassesRequiringSignatory = Array.Empty<string>()
        });
        await UpdateGovernanceAsync(seed.AccountId, new
        {
            accountType = "iolta",
            jurisdictionPolicyKey = "CA-signatory",
            statementCadence = "monthly",
            overdraftNotificationEnabled = true,
            allowedSignatories = new[] { "signer-1" }
        });

        var tx = await CreatePendingWithdrawalAsync(seed.AccountId, seed.LedgerId, "maker-sign", "Associate", 100m);

        var operationalApprove = await SendTrustAsync(
            HttpMethod.Post,
            $"/api/trust/transactions/{tx.Id}/approve-step",
            new { notes = "operational approval" },
            userId: "partner-sign",
            role: "Partner");
        Assert.Equal(HttpStatusCode.OK, operationalApprove.StatusCode);

        var nonSignatoryApprove = await SendTrustAsync(
            HttpMethod.Post,
            $"/api/trust/transactions/{tx.Id}/approve-step",
            new { requirementType = "signatory", notes = "not allowed" },
            userId: "partner-sign-2",
            role: "Partner");

        var nonSignatoryBody = await nonSignatoryApprove.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.Forbidden, nonSignatoryApprove.StatusCode);
        Assert.Contains("cannot satisfy", nonSignatoryBody, StringComparison.OrdinalIgnoreCase);

        var signatoryApprove = await SendTrustAsync(
            HttpMethod.Post,
            $"/api/trust/transactions/{tx.Id}/approve-step",
            new { requirementType = "signatory", notes = "signatory release" },
            userId: "signer-1",
            role: "Partner");

        var signatoryBody = await signatoryApprove.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, signatoryApprove.StatusCode);
        var approvedTx = JsonSerializer.Deserialize<TrustTransaction>(signatoryBody, JsonOptions());
        Assert.NotNull(approvedTx);
        Assert.Equal("APPROVED", approvedTx!.Status);
    }

    [Fact]
    public async Task OverrideTransaction_CanSatisfyPendingRequirement_WithReason()
    {
        var seed = await SeedTrustLedgerAsync();
        await FundTrustLedgerAsync(seed.AccountId, seed.LedgerId, 500m);
        await UpsertPolicyAsync("CA-override", new
        {
            policyKey = "CA-override",
            jurisdiction = "CA",
            dualApprovalThreshold = 100000m,
            responsibleLawyerApprovalThreshold = 100000m,
            signatoryApprovalThreshold = 50m,
            operationalApproverRoles = new[] { "Partner" },
            overrideApproverRoles = new[] { "Admin" },
            disbursementClassesRequiringSignatory = Array.Empty<string>()
        });
        await UpdateGovernanceAsync(seed.AccountId, new
        {
            accountType = "iolta",
            jurisdictionPolicyKey = "CA-override",
            statementCadence = "monthly",
            overdraftNotificationEnabled = true,
            allowedSignatories = new[] { "required-signer" }
        });

        var tx = await CreatePendingWithdrawalAsync(seed.AccountId, seed.LedgerId, "maker-override", "Associate", 120m);
        var operationalApprove = await SendTrustAsync(
            HttpMethod.Post,
            $"/api/trust/transactions/{tx.Id}/approve-step",
            new { notes = "ops approval" },
            userId: "partner-override",
            role: "Partner");
        Assert.Equal(HttpStatusCode.OK, operationalApprove.StatusCode);

        var overrideResponse = await SendTrustAsync(
            HttpMethod.Post,
            $"/api/trust/transactions/{tx.Id}/override",
            new { reason = "Emergency disbursement release." },
            userId: "admin-override",
            role: "Admin");

        var overrideBody = await overrideResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, overrideResponse.StatusCode);
        var approvedTx = JsonSerializer.Deserialize<TrustTransaction>(overrideBody, JsonOptions());
        Assert.NotNull(approvedTx);
        Assert.Equal("APPROVED", approvedTx!.Status);

        var stateResponse = await SendTrustAsync(
            HttpMethod.Get,
            $"/api/trust/transactions/{tx.Id}/approval-state",
            payload: null,
            userId: "admin-override",
            role: "Admin");
        var stateBody = await stateResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, stateResponse.StatusCode);
        Assert.Contains("overridden", stateBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TrustFundedBillingAllocation_RequiringApproval_FinalizesAfterTrustApprovalSync()
    {
        var seed = await SeedTrustFundedBillingAllocationAsync();
        await UpsertPolicyAsync("CA-earned-fee-dual", new
        {
            policyKey = "CA-earned-fee-dual",
            jurisdiction = "CA",
            dualApprovalThreshold = 10m,
            responsibleLawyerApprovalThreshold = 100000m,
            signatoryApprovalThreshold = 100000m,
            operationalApproverRoles = new[] { "Partner", "Accountant" },
            overrideApproverRoles = new[] { "Admin", "Partner" },
            disbursementClassesRequiringSignatory = Array.Empty<string>()
        });
        await UpdateGovernanceAsync(seed.AccountId, new
        {
            accountType = "iolta",
            jurisdictionPolicyKey = "CA-earned-fee-dual",
            statementCadence = "monthly",
            overdraftNotificationEnabled = true,
            allowedSignatories = Array.Empty<string>(),
            responsibleLawyerUserId = "responsible-lawyer-1"
        });

        var response = await SendBillingAsync(
            HttpMethod.Post,
            "/api/legal-billing/allocations",
            new
            {
                paymentTransactionId = seed.PaymentId,
                invoiceId = seed.InvoiceId,
                trustAccountId = seed.AccountId,
                trustLedgerId = seed.LedgerId,
                amount = 80.00m,
                fundSource = "trust",
                allocationType = "invoice_header",
                reference = "trust-allocation-pending"
            },
            userId: "billing-trust-maker",
            role: "Partner");

        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, body);

        var allocation = JsonSerializer.Deserialize<BillingPaymentAllocation>(body, JsonOptions());
        Assert.NotNull(allocation);
        Assert.Equal("pending_trust_approval", allocation!.Status);
        Assert.False(string.IsNullOrWhiteSpace(allocation.TrustTransactionId));

        using (var scope = CreateTenantScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
            var trustTx = await db.TrustTransactions.AsNoTracking().SingleAsync(t => t.Id == allocation.TrustTransactionId);
            var billingLedgerEntries = await db.BillingLedgerEntries.AsNoTracking()
                .Where(e => e.CorrelationKey != null && e.CorrelationKey.StartsWith($"allocation:{allocation.Id}:"))
                .ToListAsync();

            Assert.Equal("PENDING", trustTx.Status);
            Assert.Empty(billingLedgerEntries);
        }

        var queueResponse = await SendTrustAsync(
            HttpMethod.Get,
            "/api/trust/approvals",
            payload: null,
            userId: "queue-viewer",
            role: "Partner");
        var queueBody = await queueResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, queueResponse.StatusCode);
        Assert.Contains(allocation.TrustTransactionId!, queueBody, StringComparison.Ordinal);

        var firstApprove = await SendTrustAsync(
            HttpMethod.Post,
            $"/api/trust/transactions/{allocation.TrustTransactionId}/approve-step",
            new { notes = "ops approval 1" },
            userId: "trust-approver-1",
            role: "Partner");
        Assert.Equal(HttpStatusCode.OK, firstApprove.StatusCode);

        var secondApprove = await SendTrustAsync(
            HttpMethod.Post,
            $"/api/trust/transactions/{allocation.TrustTransactionId}/approve-step",
            new { notes = "ops approval 2" },
            userId: "trust-approver-2",
            role: "Partner");
        var secondBody = await secondApprove.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, secondApprove.StatusCode);
        Assert.Contains("\"status\":\"APPROVED\"", secondBody, StringComparison.OrdinalIgnoreCase);

        using var finalizedScope = CreateTenantScope();
        var finalizedDb = finalizedScope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var finalizedAllocation = await finalizedDb.BillingPaymentAllocations.AsNoTracking().SingleAsync(a => a.Id == allocation.Id);
        var account = await finalizedDb.TrustBankAccounts.AsNoTracking().SingleAsync(a => a.Id == seed.AccountId);
        var ledger = await finalizedDb.ClientTrustLedgers.AsNoTracking().SingleAsync(l => l.Id == seed.LedgerId);
        var billingEntries = await finalizedDb.BillingLedgerEntries.AsNoTracking()
            .Where(e => e.CorrelationKey != null && e.CorrelationKey.StartsWith($"allocation:{allocation.Id}:"))
            .OrderBy(e => e.CorrelationKey)
            .ToListAsync();

        Assert.Equal("applied", finalizedAllocation.Status);
        Assert.Equal(3, billingEntries.Count);
        Assert.Equal(220m, account.CurrentBalance);
        Assert.Equal(220m, account.ClearedBalance);
        Assert.Equal(220m, ledger.RunningBalance);
        Assert.Equal(220m, ledger.AvailableToDisburse);
    }

    [Fact]
    public async Task TrustFundedBillingAllocation_RejectedFromTrustQueue_ReversesPendingAllocation()
    {
        var seed = await SeedTrustFundedBillingAllocationAsync();
        await UpsertPolicyAsync("CA-earned-fee-reject", new
        {
            policyKey = "CA-earned-fee-reject",
            jurisdiction = "CA",
            dualApprovalThreshold = 10m,
            responsibleLawyerApprovalThreshold = 100000m,
            signatoryApprovalThreshold = 100000m,
            operationalApproverRoles = new[] { "Partner" },
            overrideApproverRoles = new[] { "Admin", "Partner" },
            disbursementClassesRequiringSignatory = Array.Empty<string>()
        });
        await UpdateGovernanceAsync(seed.AccountId, new
        {
            accountType = "iolta",
            jurisdictionPolicyKey = "CA-earned-fee-reject",
            statementCadence = "monthly",
            overdraftNotificationEnabled = true,
            allowedSignatories = Array.Empty<string>()
        });

        var response = await SendBillingAsync(
            HttpMethod.Post,
            "/api/legal-billing/allocations",
            new
            {
                paymentTransactionId = seed.PaymentId,
                invoiceId = seed.InvoiceId,
                trustAccountId = seed.AccountId,
                trustLedgerId = seed.LedgerId,
                amount = 40.00m,
                fundSource = "trust",
                allocationType = "invoice_header",
                reference = "trust-allocation-reject-sync"
            },
            userId: "billing-trust-maker-reject",
            role: "Partner");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var allocation = JsonSerializer.Deserialize<BillingPaymentAllocation>(await response.Content.ReadAsStringAsync(), JsonOptions());
        Assert.NotNull(allocation);
        Assert.Equal("pending_trust_approval", allocation!.Status);

        var rejectResponse = await SendTrustAsync(
            HttpMethod.Post,
            $"/api/trust/transactions/{allocation.TrustTransactionId}/reject",
            new { reason = "Insufficient supporting invoice review." },
            userId: "trust-rejector",
            role: "Partner");
        var rejectBody = await rejectResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, rejectResponse.StatusCode);
        Assert.Contains("\"status\":\"REJECTED\"", rejectBody, StringComparison.OrdinalIgnoreCase);

        using var scope = CreateTenantScope();
        var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var finalizedAllocation = await db.BillingPaymentAllocations.AsNoTracking().SingleAsync(a => a.Id == allocation.Id);
        var billingEntries = await db.BillingLedgerEntries.AsNoTracking()
            .Where(e => e.CorrelationKey != null && e.CorrelationKey.StartsWith($"allocation:{allocation.Id}:"))
            .ToListAsync();
        var trustTx = await db.TrustTransactions.AsNoTracking().SingleAsync(t => t.Id == allocation.TrustTransactionId);

        Assert.Equal("reversed", finalizedAllocation.Status);
        Assert.Equal("REJECTED", trustTx.Status);
        Assert.Empty(billingEntries);
    }

    [Fact]
    public async Task MonthClose_PrepareAndSignoff_TracksLifecycleAndResponsibleLawyerGate()
    {
        var seed = await SeedTrustLedgerAsync();
        await UpdateGovernanceAsync(seed.AccountId, new
        {
            accountType = "iolta",
            statementCadence = "monthly",
            overdraftNotificationEnabled = true,
            responsibleLawyerUserId = "monthclose-lawyer"
        });

        var deposit = await CreatePendingDepositAsync(seed.AccountId, seed.LedgerId, "monthclose-maker", "Associate", amount: 100m, checkNumber: null);
        var approveDeposit = await SendTrustAsync(
            HttpMethod.Post,
            $"/api/trust/transactions/{deposit.Id}/approve",
            payload: null,
            userId: "monthclose-approver",
            role: "Partner");
        Assert.Equal(HttpStatusCode.OK, approveDeposit.StatusCode);

        var periodStart = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodEnd = new DateTime(2026, 4, 30, 0, 0, 0, DateTimeKind.Utc);

        var importResponse = await SendTrustAsync(
            HttpMethod.Post,
            "/api/trust/statements/import",
            new
            {
                trustAccountId = seed.AccountId,
                periodStart,
                periodEnd,
                statementEndingBalance = 100m,
                source = "manual"
            },
            userId: "monthclose-partner",
            role: "Partner");
        var importBody = await importResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, importResponse.StatusCode);
        var importDto = JsonSerializer.Deserialize<TrustStatementImport>(importBody, JsonOptions());
        Assert.NotNull(importDto);

        var packetResponse = await SendTrustAsync(
            HttpMethod.Post,
            "/api/trust/reconciliation-packets",
            new
            {
                trustAccountId = seed.AccountId,
                periodStart,
                periodEnd,
                statementImportId = importDto!.Id
            },
            userId: "monthclose-partner",
            role: "Partner");
        var packetBody = await packetResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, packetResponse.StatusCode);
        var packet = JsonSerializer.Deserialize<TrustReconciliationPacket>(packetBody, JsonOptions());
        Assert.NotNull(packet);

        var prepareResponse = await SendTrustAsync(
            HttpMethod.Post,
            "/api/trust/month-close/prepare",
            new
            {
                trustAccountId = seed.AccountId,
                periodStart,
                periodEnd,
                reconciliationPacketId = packet!.Id,
                autoGeneratePacket = false
            },
            userId: "monthclose-partner",
            role: "Partner");
        var prepareBody = await prepareResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, prepareResponse.StatusCode);
        var monthClose = JsonSerializer.Deserialize<TrustMonthCloseDto>(prepareBody, JsonOptions());
        Assert.NotNull(monthClose);
        Assert.Equal("in_progress", monthClose!.Status);

        var selfReviewerResponse = await SendTrustAsync(
            HttpMethod.Post,
            $"/api/trust/month-close/{monthClose.Id}/signoff",
            new
            {
                role = "reviewer",
                notes = "Self signoff should fail",
                attestations = new[]
                {
                    new { key = "reviewed_three_way_reconciliation", accepted = true, notes = "Reviewer attempted self signoff." }
                }
            },
            userId: "monthclose-partner",
            role: "Partner");
        Assert.Equal(HttpStatusCode.Forbidden, selfReviewerResponse.StatusCode);

        var badResponsibleResponse = await SendTrustAsync(
            HttpMethod.Post,
            $"/api/trust/month-close/{monthClose.Id}/signoff",
            new
            {
                role = "responsible_lawyer",
                notes = "Should fail",
                attestations = new[]
                {
                    new { key = "responsible_lawyer_certification", accepted = true, notes = "Wrong lawyer attempted signoff." }
                }
            },
            userId: "wrong-lawyer",
            role: "Partner");
        Assert.Equal(HttpStatusCode.Forbidden, badResponsibleResponse.StatusCode);

        var reviewerResponse = await SendTrustAsync(
            HttpMethod.Post,
            $"/api/trust/month-close/{monthClose.Id}/signoff",
            new
            {
                role = "reviewer",
                notes = "Reviewed.",
                attestations = new[]
                {
                    new { key = "reviewed_three_way_reconciliation", accepted = true, notes = "Reviewer confirmed packet completeness." }
                }
            },
            userId: "monthclose-reviewer",
            role: "Partner");
        var reviewerBody = await reviewerResponse.Content.ReadAsStringAsync();
        Assert.True(reviewerResponse.StatusCode == HttpStatusCode.OK, reviewerBody);
        Assert.Contains("\"status\":\"partially_signed\"", reviewerBody, StringComparison.OrdinalIgnoreCase);

        var responsibleResponse = await SendTrustAsync(
            HttpMethod.Post,
            $"/api/trust/month-close/{monthClose.Id}/signoff",
            new
            {
                role = "responsible_lawyer",
                notes = "Final signoff.",
                attestations = new[]
                {
                    new { key = "responsible_lawyer_certification", accepted = true, notes = "Lawyer certified the packet." }
                }
            },
            userId: "monthclose-lawyer",
            role: "Partner");
        var responsibleBody = await responsibleResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, responsibleResponse.StatusCode);
        Assert.Contains("\"status\":\"closed\"", responsibleBody, StringComparison.OrdinalIgnoreCase);

        var listResponse = await SendTrustAsync(
            HttpMethod.Get,
            $"/api/trust/month-close?trustAccountId={seed.AccountId}",
            payload: null,
            userId: "monthclose-reviewer",
            role: "Partner");
        var listBody = await listResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.Contains(monthClose.Id, listBody, StringComparison.Ordinal);
        Assert.Contains("closed", listBody, StringComparison.OrdinalIgnoreCase);

        var reprepareResponse = await SendTrustAsync(
            HttpMethod.Post,
            "/api/trust/month-close/prepare",
            new
            {
                trustAccountId = seed.AccountId,
                periodStart,
                periodEnd,
                reconciliationPacketId = packet.Id,
                autoGeneratePacket = false
            },
            userId: "monthclose-reviewer",
            role: "Partner");
        Assert.Equal(HttpStatusCode.Conflict, reprepareResponse.StatusCode);

        using var scope = CreateTenantScope();
        var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var persistedPacket = await db.TrustReconciliationPackets.AsNoTracking().SingleAsync(x => x.Id == packet.Id);
        Assert.Equal("closed", persistedPacket.Status);
    }

    [Fact]
    public async Task JurisdictionPolicyResolver_PrefersAccountTypeSpecificLatestVersion()
    {
        var seed = await SeedTrustLedgerAsync();

        await UpsertPolicyAsync("CA-premium", new
        {
            policyKey = "CA-premium",
            jurisdiction = "CA",
            accountType = "all",
            versionNumber = 1,
            dualApprovalThreshold = 5000m,
            responsibleLawyerApprovalThreshold = 25000m,
            signatoryApprovalThreshold = 5000m,
            monthlyCloseCadenceDays = 30,
            exceptionAgingSlaHours = 48,
            retentionPeriodMonths = 60,
            requireMonthlyThreeWayReconciliation = true,
            requireResponsibleLawyerAssignment = true
        });

        await UpsertPolicyAsync("CA-premium", new
        {
            policyKey = "CA-premium",
            jurisdiction = "CA",
            accountType = "non_iolta",
            versionNumber = 2,
            dualApprovalThreshold = 15000m,
            responsibleLawyerApprovalThreshold = 50000m,
            signatoryApprovalThreshold = 7500m,
            monthlyCloseCadenceDays = 15,
            exceptionAgingSlaHours = 12,
            retentionPeriodMonths = 84,
            requireMonthlyThreeWayReconciliation = true,
            requireResponsibleLawyerAssignment = false
        });

        await UpdateGovernanceAsync(seed.AccountId, new
        {
            accountType = "non_iolta",
            jurisdictionPolicyKey = "CA-premium",
            statementCadence = "monthly",
            overdraftNotificationEnabled = true
        });

        using var scope = CreateTenantScope();
        var resolver = scope.ServiceProvider.GetRequiredService<TrustPolicyResolverService>();

        var resolved = await resolver.ResolveEffectivePolicyAsync(seed.AccountId);

        Assert.Equal("non_iolta", resolved.Account.AccountType);
        Assert.Equal("CA-premium", resolved.Policy.PolicyKey);
        Assert.Equal("non_iolta", resolved.Policy.AccountType);
        Assert.Equal(2, resolved.Policy.VersionNumber);
        Assert.Equal(15000m, resolved.Policy.DualApprovalThreshold);
        Assert.Equal(84, resolved.Policy.RetentionPeriodMonths);
        Assert.False(resolved.Policy.RequireResponsibleLawyerAssignment);
    }

    [Fact]
    public async Task ReconciliationPacket_Supersede_CreatesNewCanonicalVersionAndPreservesPriorRecord()
    {
        var seed = await SeedTrustLedgerAsync();
        var deposit = await CreatePendingDepositAsync(seed.AccountId, seed.LedgerId, "packet-maker", "Associate", amount: 100m, checkNumber: null);
        var approveDeposit = await SendTrustAsync(
            HttpMethod.Post,
            $"/api/trust/transactions/{deposit.Id}/approve",
            payload: null,
            userId: "packet-approver",
            role: "Partner");
        Assert.Equal(HttpStatusCode.OK, approveDeposit.StatusCode);

        var periodStart = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodEnd = new DateTime(2026, 4, 30, 0, 0, 0, DateTimeKind.Utc);

        var importResponse = await SendTrustAsync(
            HttpMethod.Post,
            "/api/trust/statements/import",
            new
            {
                trustAccountId = seed.AccountId,
                periodStart,
                periodEnd,
                statementEndingBalance = 100m,
                source = "manual"
            },
            userId: "packet-preparer",
            role: "Partner");
        var importDto = JsonSerializer.Deserialize<TrustStatementImport>(await importResponse.Content.ReadAsStringAsync(), JsonOptions());
        Assert.NotNull(importDto);

        var packetResponse = await SendTrustAsync(
            HttpMethod.Post,
            "/api/trust/reconciliation-packets",
            new
            {
                trustAccountId = seed.AccountId,
                periodStart,
                periodEnd,
                statementImportId = importDto!.Id
            },
            userId: "packet-preparer",
            role: "Partner");
        var initialPacket = JsonSerializer.Deserialize<TrustReconciliationPacket>(await packetResponse.Content.ReadAsStringAsync(), JsonOptions());
        Assert.NotNull(initialPacket);

        var supersedeResponse = await SendTrustAsync(
            HttpMethod.Post,
            $"/api/trust/reconciliation-packets/{initialPacket!.Id}/supersede",
            new
            {
                reason = "Statement import corrected",
                notes = "Regenerate canonical packet."
            },
            userId: "packet-reviewer",
            role: "Partner");
        var supersedeBody = await supersedeResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, supersedeResponse.StatusCode);
        var replacement = JsonSerializer.Deserialize<TrustReconciliationPacket>(supersedeBody, JsonOptions());
        Assert.NotNull(replacement);
        Assert.True(replacement!.IsCanonical);
        Assert.Equal(initialPacket.VersionNumber + 1, replacement.VersionNumber);

        var listResponse = await SendTrustAsync(
            HttpMethod.Get,
            $"/api/trust/reconciliation-packets?trustAccountId={seed.AccountId}",
            payload: null,
            userId: "packet-reviewer",
            role: "Partner");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var listedPackets = JsonSerializer.Deserialize<List<TrustReconciliationPacket>>(await listResponse.Content.ReadAsStringAsync(), JsonOptions());
        Assert.NotNull(listedPackets);
        Assert.Contains(listedPackets!, p => p.Id == replacement.Id);
        Assert.DoesNotContain(listedPackets!, p => p.Id == initialPacket.Id);

        using var scope = CreateTenantScope();
        var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var persistedInitial = await db.TrustReconciliationPackets.AsNoTracking().SingleAsync(x => x.Id == initialPacket.Id);
        var persistedReplacement = await db.TrustReconciliationPackets.AsNoTracking().SingleAsync(x => x.Id == replacement.Id);
        Assert.False(persistedInitial.IsCanonical);
        Assert.Equal(replacement.Id, persistedInitial.SupersededByPacketId);
        Assert.Equal("superseded", persistedInitial.Status);
        Assert.True(persistedReplacement.IsCanonical);
    }

    [Fact]
    public async Task MonthClose_Reopen_CreatesNewCanonicalVersion_AndPreservesHistory()
    {
        var seed = await SeedTrustLedgerAsync();
        await UpdateGovernanceAsync(seed.AccountId, new
        {
            accountType = "iolta",
            statementCadence = "monthly",
            overdraftNotificationEnabled = true,
            responsibleLawyerUserId = "monthclose-lawyer-2"
        });

        var deposit = await CreatePendingDepositAsync(seed.AccountId, seed.LedgerId, "monthclose-maker-2", "Associate", amount: 100m, checkNumber: null);
        var approveDeposit = await SendTrustAsync(
            HttpMethod.Post,
            $"/api/trust/transactions/{deposit.Id}/approve",
            payload: null,
            userId: "monthclose-approver-2",
            role: "Partner");
        Assert.Equal(HttpStatusCode.OK, approveDeposit.StatusCode);

        var periodStart = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodEnd = new DateTime(2026, 5, 31, 0, 0, 0, DateTimeKind.Utc);

        var importResponse = await SendTrustAsync(
            HttpMethod.Post,
            "/api/trust/statements/import",
            new
            {
                trustAccountId = seed.AccountId,
                periodStart,
                periodEnd,
                statementEndingBalance = 100m,
                source = "manual"
            },
            userId: "monthclose-preparer-2",
            role: "Partner");
        var importDto = JsonSerializer.Deserialize<TrustStatementImport>(await importResponse.Content.ReadAsStringAsync(), JsonOptions());
        Assert.NotNull(importDto);

        var packetResponse = await SendTrustAsync(
            HttpMethod.Post,
            "/api/trust/reconciliation-packets",
            new
            {
                trustAccountId = seed.AccountId,
                periodStart,
                periodEnd,
                statementImportId = importDto!.Id
            },
            userId: "monthclose-preparer-2",
            role: "Partner");
        var packet = JsonSerializer.Deserialize<TrustReconciliationPacket>(await packetResponse.Content.ReadAsStringAsync(), JsonOptions());
        Assert.NotNull(packet);

        var prepareResponse = await SendTrustAsync(
            HttpMethod.Post,
            "/api/trust/month-close/prepare",
            new
            {
                trustAccountId = seed.AccountId,
                periodStart,
                periodEnd,
                reconciliationPacketId = packet!.Id,
                autoGeneratePacket = false
            },
            userId: "monthclose-preparer-2",
            role: "Partner");
        var monthClose = JsonSerializer.Deserialize<TrustMonthCloseDto>(await prepareResponse.Content.ReadAsStringAsync(), JsonOptions());
        Assert.NotNull(monthClose);

        var reviewerResponse = await SendTrustAsync(
            HttpMethod.Post,
            $"/api/trust/month-close/{monthClose!.Id}/signoff",
            new
            {
                role = "reviewer",
                notes = "Reviewed.",
                attestations = new[]
                {
                    new { key = "reviewed_three_way_reconciliation", accepted = true, notes = "Reviewed." }
                }
            },
            userId: "monthclose-reviewer-2",
            role: "Partner");
        Assert.Equal(HttpStatusCode.OK, reviewerResponse.StatusCode);

        var responsibleResponse = await SendTrustAsync(
            HttpMethod.Post,
            $"/api/trust/month-close/{monthClose.Id}/signoff",
            new
            {
                role = "responsible_lawyer",
                notes = "Final signoff.",
                attestations = new[]
                {
                    new { key = "responsible_lawyer_certification", accepted = true, notes = "Final signoff." }
                }
            },
            userId: "monthclose-lawyer-2",
            role: "Partner");
        Assert.Equal(HttpStatusCode.OK, responsibleResponse.StatusCode);

        var reopenResponse = await SendTrustAsync(
            HttpMethod.Post,
            $"/api/trust/month-close/{monthClose.Id}/reopen",
            new { reason = "Add corrected month-close notes." },
            userId: "monthclose-admin-2",
            role: "Admin");
        var reopenBody = await reopenResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, reopenResponse.StatusCode);

        var reopened = JsonSerializer.Deserialize<TrustMonthCloseDto>(reopenBody, JsonOptions());
        Assert.NotNull(reopened);
        Assert.True(reopened!.IsCanonical);
        Assert.Equal(monthClose.VersionNumber + 1, reopened.VersionNumber);
        Assert.Equal(monthClose.Id, reopened.ReopenedFromMonthCloseId);

        var canonicalListResponse = await SendTrustAsync(
            HttpMethod.Get,
            $"/api/trust/month-close?trustAccountId={seed.AccountId}",
            payload: null,
            userId: "monthclose-admin-2",
            role: "Admin");
        Assert.Equal(HttpStatusCode.OK, canonicalListResponse.StatusCode);
        var canonicalCloses = JsonSerializer.Deserialize<List<TrustMonthCloseDto>>(await canonicalListResponse.Content.ReadAsStringAsync(), JsonOptions());
        Assert.NotNull(canonicalCloses);
        Assert.Contains(canonicalCloses!, c => c.Id == reopened.Id);
        Assert.DoesNotContain(canonicalCloses!, c => c.Id == monthClose.Id);

        var historyListResponse = await SendTrustAsync(
            HttpMethod.Get,
            $"/api/trust/month-close?trustAccountId={seed.AccountId}&includeHistory=true",
            payload: null,
            userId: "monthclose-admin-2",
            role: "Admin");
        Assert.Equal(HttpStatusCode.OK, historyListResponse.StatusCode);
        var historyCloses = JsonSerializer.Deserialize<List<TrustMonthCloseDto>>(await historyListResponse.Content.ReadAsStringAsync(), JsonOptions());
        Assert.NotNull(historyCloses);
        Assert.Contains(historyCloses!, c => c.Id == reopened.Id);
        Assert.Contains(historyCloses!, c => c.Id == monthClose.Id);

        using var scope = CreateTenantScope();
        var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var persistedOriginal = await db.TrustMonthCloses.AsNoTracking().SingleAsync(x => x.Id == monthClose.Id);
        var persistedReopened = await db.TrustMonthCloses.AsNoTracking().SingleAsync(x => x.Id == reopened.Id);
        Assert.False(persistedOriginal.IsCanonical);
        Assert.Equal(reopened.Id, persistedOriginal.SupersededByMonthCloseId);
        Assert.Equal("superseded", persistedOriginal.Status);
        Assert.True(persistedReopened.IsCanonical);
    }

    [Fact]
    public async Task StatementImport_DuplicateFingerprint_IsCapturedAsEvidenceOnly()
    {
        var seed = await SeedTrustLedgerAsync();
        var payload = new
        {
            trustAccountId = seed.AccountId,
            periodStart = "2026-04-01T00:00:00Z",
            periodEnd = "2026-04-30T00:00:00Z",
            statementEndingBalance = 245.25m,
            source = "csv_upload",
            sourceFileName = "april-statement.csv",
            sourceFileHash = "ABC123FF",
            lines = new[]
            {
                new
                {
                    postedAt = "2026-04-15T00:00:00Z",
                    amount = 245.25m,
                    reference = "DEP-APR-1",
                    description = "April deposit"
                }
            }
        };

        var firstImportResponse = await SendTrustAsync(
            HttpMethod.Post,
            "/api/trust/statements/import",
            payload,
            userId: "statement-importer-a",
            role: "Partner");
        Assert.Equal(HttpStatusCode.OK, firstImportResponse.StatusCode);
        var firstImport = JsonSerializer.Deserialize<TrustStatementImport>(await firstImportResponse.Content.ReadAsStringAsync(), JsonOptions());
        Assert.NotNull(firstImport);
        Assert.NotEqual("duplicate", firstImport!.Status);

        var duplicateImportResponse = await SendTrustAsync(
            HttpMethod.Post,
            "/api/trust/statements/import",
            payload,
            userId: "statement-importer-b",
            role: "Partner");
        Assert.Equal(HttpStatusCode.OK, duplicateImportResponse.StatusCode);
        var duplicateImport = JsonSerializer.Deserialize<TrustStatementImport>(await duplicateImportResponse.Content.ReadAsStringAsync(), JsonOptions());
        Assert.NotNull(duplicateImport);
        Assert.Equal("duplicate", duplicateImport!.Status);
        Assert.Equal(firstImport.Id, duplicateImport.DuplicateOfStatementImportId);
        Assert.Equal("abc123ff", duplicateImport.SourceFileHash);
        Assert.False(string.IsNullOrWhiteSpace(duplicateImport.ImportFingerprint));

        using var scope = CreateTenantScope();
        var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var duplicateLines = await db.TrustStatementLines.AsNoTracking()
            .Where(l => l.TrustStatementImportId == duplicateImport.Id)
            .ToListAsync();
        Assert.Empty(duplicateLines);
    }

    [Fact]
    public async Task StatementImport_NewVersion_SupersedesPriorCanonicalImport()
    {
        var seed = await SeedTrustLedgerAsync();
        var firstPayload = new
        {
            trustAccountId = seed.AccountId,
            periodStart = "2026-04-01T00:00:00Z",
            periodEnd = "2026-04-30T00:00:00Z",
            statementEndingBalance = 245.25m,
            source = "csv_upload",
            sourceFileName = "april-statement-v1.csv",
            sourceFileHash = "FIRSTHASH",
            lines = Array.Empty<object>()
        };

        var firstResponse = await SendTrustAsync(
            HttpMethod.Post,
            "/api/trust/statements/import",
            firstPayload,
            userId: "statement-version-a",
            role: "Partner");
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        var firstImport = JsonSerializer.Deserialize<TrustStatementImport>(await firstResponse.Content.ReadAsStringAsync(), JsonOptions());
        Assert.NotNull(firstImport);

        var secondResponse = await SendTrustAsync(
            HttpMethod.Post,
            "/api/trust/statements/import",
            new
            {
                trustAccountId = seed.AccountId,
                periodStart = "2026-04-01T00:00:00Z",
                periodEnd = "2026-04-30T00:00:00Z",
                statementEndingBalance = 250.00m,
                source = "csv_upload",
                sourceFileName = "april-statement-v2.csv",
                sourceFileHash = "SECONDHASH",
                lines = Array.Empty<object>()
            },
            userId: "statement-version-b",
            role: "Partner");
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        var secondImport = JsonSerializer.Deserialize<TrustStatementImport>(await secondResponse.Content.ReadAsStringAsync(), JsonOptions());
        Assert.NotNull(secondImport);

        using var scope = CreateTenantScope();
        var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var persistedFirst = await db.TrustStatementImports.AsNoTracking().SingleAsync(i => i.Id == firstImport!.Id);
        var persistedSecond = await db.TrustStatementImports.AsNoTracking().SingleAsync(i => i.Id == secondImport!.Id);

        Assert.Equal("superseded", persistedFirst.Status);
        Assert.Equal(secondImport.Id, persistedFirst.SupersededByStatementImportId);
        Assert.Equal("imported", persistedSecond.Status);
        Assert.Null(persistedSecond.SupersededByStatementImportId);
    }

    [Fact]
    public async Task EvidenceRegistry_AndParserRun_CreateStatementImportLineage()
    {
        var seed = await SeedTrustLedgerAsync();

        var evidenceResponse = await SendTrustAsync(
            HttpMethod.Post,
            "/api/trust/evidence-files/register",
            new
            {
                trustAccountId = seed.AccountId,
                periodStart = "2026-04-01T00:00:00Z",
                periodEnd = "2026-04-30T00:00:00Z",
                source = "bank_portal_upload",
                fileName = "apr-2026-statement.csv",
                contentType = "text/csv",
                fileHash = "sha256:abc123evidence",
                evidenceKey = "evidence://trust/apr-2026-statement",
                fileSizeBytes = 4096
            },
            userId: "evidence-registrar",
            role: "Partner");
        Assert.Equal(HttpStatusCode.OK, evidenceResponse.StatusCode);
        var evidence = JsonSerializer.Deserialize<TrustEvidenceFile>(await evidenceResponse.Content.ReadAsStringAsync(), JsonOptions());
        Assert.NotNull(evidence);
        Assert.Equal("registered", evidence!.Status);

        var parserRunResponse = await SendTrustAsync(
            HttpMethod.Post,
            "/api/trust/parser-runs",
            new
            {
                trustAccountId = seed.AccountId,
                trustEvidenceFileId = evidence.Id,
                statementEndingBalance = 150.00m,
                parserKey = "manual_manifest_v1",
                lines = new[]
                {
                    new
                    {
                        postedAt = "2026-04-10T00:00:00Z",
                        amount = 150.00m,
                        reference = "BANK-DEP-1",
                        description = "Trust deposit"
                    }
                }
            },
            userId: "evidence-parser",
            role: "Partner");
        var parserRunBody = await parserRunResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, parserRunResponse.StatusCode);
        var parserRun = JsonSerializer.Deserialize<TrustStatementParserRun>(parserRunBody, JsonOptions());
        Assert.NotNull(parserRun);
        Assert.Equal("completed", parserRun!.Status);
        Assert.False(string.IsNullOrWhiteSpace(parserRun.TrustStatementImportId));

        var evidenceListResponse = await SendTrustAsync(
            HttpMethod.Get,
            $"/api/trust/evidence-files?trustAccountId={seed.AccountId}",
            null,
            "evidence-parser",
            "Partner");
        Assert.Equal(HttpStatusCode.OK, evidenceListResponse.StatusCode);

        using var scope = CreateTenantScope();
        var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var persistedEvidence = await db.TrustEvidenceFiles.AsNoTracking().SingleAsync(x => x.Id == evidence.Id);
        var persistedParserRun = await db.TrustStatementParserRuns.AsNoTracking().SingleAsync(x => x.Id == parserRun.Id);
        var persistedImport = await db.TrustStatementImports.AsNoTracking().SingleAsync(x => x.Id == parserRun.TrustStatementImportId);

        Assert.Equal("parsed", persistedEvidence.Status);
        Assert.Equal(parserRun.Id, persistedEvidence.LatestParserRunId);
        Assert.Equal(persistedImport.Id, persistedEvidence.CanonicalStatementImportId);
        Assert.Equal(evidence.Id, persistedParserRun.TrustEvidenceFileId);
        Assert.Equal("parser_run", persistedImport.Source);
        Assert.Equal(persistedEvidence.EvidenceKey, persistedImport.SourceEvidenceKey);
    }

    [Fact]
    public async Task MonthClose_TemplateAttestations_BlockSignoffUntilAccepted()
    {
        var seed = await SeedTrustLedgerAsync();
        await UpdateGovernanceAsync(seed.AccountId, new
        {
            accountType = "iolta",
            statementCadence = "monthly",
            overdraftNotificationEnabled = true,
            responsibleLawyerUserId = "templated-lawyer"
        });

        var deposit = await CreatePendingDepositAsync(seed.AccountId, seed.LedgerId, "templated-maker", "Associate", amount: 100m, checkNumber: null);
        var approveDeposit = await SendTrustAsync(
            HttpMethod.Post,
            $"/api/trust/transactions/{deposit.Id}/approve",
            payload: null,
            userId: "templated-approver",
            role: "Partner");
        Assert.Equal(HttpStatusCode.OK, approveDeposit.StatusCode);

        var periodStart = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodEnd = new DateTime(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc);

        var importResponse = await SendTrustAsync(
            HttpMethod.Post,
            "/api/trust/statements/import",
            new
            {
                trustAccountId = seed.AccountId,
                periodStart,
                periodEnd,
                statementEndingBalance = 100m,
                source = "manual"
            },
            userId: "templated-preparer",
            role: "Partner");
        var importDto = JsonSerializer.Deserialize<TrustStatementImport>(await importResponse.Content.ReadAsStringAsync(), JsonOptions());
        Assert.NotNull(importDto);

        var packetResponse = await SendTrustAsync(
            HttpMethod.Post,
            "/api/trust/reconciliation-packets",
            new
            {
                trustAccountId = seed.AccountId,
                periodStart,
                periodEnd,
                statementImportId = importDto!.Id
            },
            userId: "templated-preparer",
            role: "Partner");
        var packet = JsonSerializer.Deserialize<TrustReconciliationPacket>(await packetResponse.Content.ReadAsStringAsync(), JsonOptions());
        Assert.NotNull(packet);

        var prepareResponse = await SendTrustAsync(
            HttpMethod.Post,
            "/api/trust/month-close/prepare",
            new
            {
                trustAccountId = seed.AccountId,
                periodStart,
                periodEnd,
                reconciliationPacketId = packet!.Id,
                autoGeneratePacket = false
            },
            userId: "templated-preparer",
            role: "Partner");
        var monthClose = JsonSerializer.Deserialize<TrustMonthCloseDto>(await prepareResponse.Content.ReadAsStringAsync(), JsonOptions());
        Assert.NotNull(monthClose);
        Assert.False(string.IsNullOrWhiteSpace(monthClose!.PacketTemplateKey));
        Assert.Contains(monthClose.RequiredAttestations, a => a.Role == "reviewer" && a.Required);
        Assert.Contains(monthClose.RequiredAttestations, a => a.Role == "responsible_lawyer" && a.Required);

        var reviewerMissingAttestation = await SendTrustAsync(
            HttpMethod.Post,
            $"/api/trust/month-close/{monthClose.Id}/signoff",
            new { role = "reviewer", notes = "Missing attestation should fail" },
            userId: "templated-reviewer",
            role: "Partner");
        var reviewerMissingBody = await reviewerMissingAttestation.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.Conflict, reviewerMissingAttestation.StatusCode);
        Assert.Contains("attestations", reviewerMissingBody, StringComparison.OrdinalIgnoreCase);

        var reviewerResponse = await SendTrustAsync(
            HttpMethod.Post,
            $"/api/trust/month-close/{monthClose.Id}/signoff",
            new
            {
                role = "reviewer",
                notes = "Reviewed with attestation.",
                attestations = new[]
                {
                    new { key = "reviewed_three_way_reconciliation", accepted = true, notes = "Reviewer confirms reconciliation." }
                }
            },
            userId: "templated-reviewer",
            role: "Partner");
        Assert.Equal(HttpStatusCode.OK, reviewerResponse.StatusCode);

        var lawyerResponse = await SendTrustAsync(
            HttpMethod.Post,
            $"/api/trust/month-close/{monthClose.Id}/signoff",
            new
            {
                role = "responsible_lawyer",
                notes = "Lawyer certified packet.",
                attestations = new[]
                {
                    new { key = "responsible_lawyer_certification", accepted = true, notes = "Certification complete." }
                }
            },
            userId: "templated-lawyer",
            role: "Partner");
        Assert.Equal(HttpStatusCode.OK, lawyerResponse.StatusCode);

        using var scope = CreateTenantScope();
        var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var attestations = await db.TrustMonthCloseAttestations.AsNoTracking()
            .Where(x => x.TrustMonthCloseId == monthClose.Id)
            .OrderBy(x => x.Role)
            .ToListAsync();
        Assert.Equal(2, attestations.Count);
        Assert.Contains(attestations, x => x.Role == "reviewer" && x.AttestationKey == "reviewed_three_way_reconciliation" && x.Accepted);
        Assert.Contains(attestations, x => x.Role == "responsible_lawyer" && x.AttestationKey == "responsible_lawyer_certification" && x.Accepted);
    }

    [Fact]
    public async Task StatementLine_Reject_MarksPacketReadyWithoutCreatingException()
    {
        var seed = await SeedTrustLedgerAsync();

        var importResponse = await SendTrustAsync(
            HttpMethod.Post,
            "/api/trust/statements/import",
            new
            {
                trustAccountId = seed.AccountId,
                periodStart = "2026-04-01T00:00:00Z",
                periodEnd = "2026-04-30T00:00:00Z",
                statementEndingBalance = 120.00m,
                source = "manual_test",
                lines = new[]
                {
                    new
                    {
                        postedAt = "2026-04-16T00:00:00Z",
                        amount = 120.00m,
                        reference = "ORPHAN-1",
                        description = "Unrecognized statement line"
                    }
                }
            },
            userId: "reject-importer",
            role: "Partner");
        Assert.Equal(HttpStatusCode.OK, importResponse.StatusCode);
        var importDto = JsonSerializer.Deserialize<TrustStatementImport>(await importResponse.Content.ReadAsStringAsync(), JsonOptions());
        Assert.NotNull(importDto);

        var packetResponse = await SendTrustAsync(
            HttpMethod.Post,
            "/api/trust/reconciliation-packets",
            new
            {
                trustAccountId = seed.AccountId,
                periodStart = "2026-04-01T00:00:00Z",
                periodEnd = "2026-04-30T00:00:00Z",
                statementImportId = importDto!.Id
            },
            userId: "reject-packet-builder",
            role: "Partner");
        Assert.Equal(HttpStatusCode.OK, packetResponse.StatusCode);

        var linesResponse = await SendTrustAsync(
            HttpMethod.Get,
            $"/api/trust/statements/{importDto.Id}/lines",
            payload: null,
            userId: "reject-packet-builder",
            role: "Partner");
        Assert.Equal(HttpStatusCode.OK, linesResponse.StatusCode);
        var lines = JsonSerializer.Deserialize<List<TrustStatementLine>>(await linesResponse.Content.ReadAsStringAsync(), JsonOptions());
        var line = Assert.Single(lines!);

        var rejectResponse = await SendTrustAsync(
            HttpMethod.Post,
            $"/api/trust/statement-lines/{line.Id}/resolve",
            new
            {
                action = "reject",
                notes = "Rejected after source evidence review."
            },
            userId: "reject-reviewer",
            role: "Partner");
        Assert.Equal(HttpStatusCode.OK, rejectResponse.StatusCode);

        using var scope = CreateTenantScope();
        var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var persistedLine = await db.TrustStatementLines.AsNoTracking().SingleAsync(l => l.Id == line.Id);
        var canonicalPacket = await db.TrustReconciliationPackets.AsNoTracking()
            .Where(p => p.TrustAccountId == seed.AccountId && p.PeriodEnd == new DateTime(2026, 4, 30, 0, 0, 0, DateTimeKind.Utc) && p.IsCanonical)
            .SingleAsync();
        var outstandingItems = await db.TrustOutstandingItems.AsNoTracking()
            .Where(i => i.TrustReconciliationPacketId == canonicalPacket.Id)
            .ToListAsync();

        Assert.Equal("rejected", persistedLine.MatchStatus);
        Assert.Equal("needs_review", canonicalPacket.Status);
        Assert.Equal(0, canonicalPacket.UnmatchedStatementLineCount);
        Assert.Equal(1, canonicalPacket.ExceptionCount);
        Assert.Empty(outstandingItems);
    }

    [Fact]
    public async Task ManualOutstandingItem_PersistsReasonCodeAndAttachmentEvidence()
    {
        var seed = await SeedTrustLedgerAsync();

        var response = await SendTrustAsync(
            HttpMethod.Post,
            "/api/trust/outstanding-items",
            new
            {
                trustAccountId = seed.AccountId,
                periodStart = "2026-04-01T00:00:00Z",
                periodEnd = "2026-04-30T00:00:00Z",
                itemType = "bank_fee",
                impactDirection = "increase_bank",
                amount = 18.25m,
                reference = "BANK-FEE-APR",
                description = "Manual bank adjustment",
                reasonCode = "BANK_FEE",
                attachmentEvidenceKey = "evidence://bank/apr-fee"
            },
            userId: "manual-outstanding-reviewer",
            role: "Partner");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var item = JsonSerializer.Deserialize<TrustOutstandingItem>(await response.Content.ReadAsStringAsync(), JsonOptions());
        Assert.NotNull(item);
        Assert.Equal("bank_fee", item!.ItemType);
        Assert.Equal("bank_fee", item.ReasonCode);
        Assert.Equal("evidence://bank/apr-fee", item.AttachmentEvidenceKey);
    }

    [Fact]
    public async Task OperationalAlerts_ReturnsMissingCloseAndAgedOutstandingSignals()
    {
        var seed = await SeedTrustLedgerAsync();

        using (var scope = CreateTenantScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
            db.TrustOutstandingItems.Add(new TrustOutstandingItem
            {
                Id = Guid.NewGuid().ToString(),
                TrustAccountId = seed.AccountId,
                PeriodStart = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                PeriodEnd = new DateTime(2026, 3, 31, 0, 0, 0, DateTimeKind.Utc),
                OccurredAt = new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc),
                ItemType = "outstanding_check",
                ImpactDirection = "decrease_bank",
                Status = "open",
                Source = "manual",
                Amount = 150m,
                Reference = "CHK-ALERT-1",
                CreatedBy = "ops-seed",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync();
        }

        var response = await SendTrustAsync(
            HttpMethod.Get,
            $"/api/trust/operational-alerts?trustAccountId={seed.AccountId}",
            payload: null,
            userId: "ops-reviewer",
            role: "Partner");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var summary = JsonSerializer.Deserialize<TrustOperationalAlertSummary>(await response.Content.ReadAsStringAsync(), JsonOptions());
        Assert.NotNull(summary);
        Assert.True(summary!.TotalCount >= 2);
        Assert.Contains(summary.Alerts, alert => alert.AlertType == "missing_month_close");
        Assert.Contains(summary.Alerts, alert => alert.AlertType == "outstanding_item_aging");
    }

    [Fact]
    public async Task OperationalAlerts_SyncCreatesPersistedRecord_AndAcknowledgeWritesHistory()
    {
        var seed = await SeedTrustLedgerAsync();
        await UpdateGovernanceAsync(seed.AccountId, new
        {
            accountType = "iolta",
            responsibleLawyerUserId = "responsible-lawyer-1",
            allowedSignatories = Array.Empty<string>(),
            jurisdictionPolicyKey = "ca-iolta-baseline",
            statementCadence = "monthly",
            overdraftNotificationEnabled = true
        });

        using (var scope = CreateTenantScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
            db.TrustOutstandingItems.Add(new TrustOutstandingItem
            {
                Id = Guid.NewGuid().ToString(),
                TrustAccountId = seed.AccountId,
                PeriodStart = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                PeriodEnd = new DateTime(2026, 3, 31, 0, 0, 0, DateTimeKind.Utc),
                OccurredAt = DateTime.UtcNow.AddDays(-20),
                ItemType = "outstanding_check",
                ImpactDirection = "decrease_bank",
                Status = "open",
                Source = "manual",
                Amount = 75m,
                Reference = "CHK-OPS-ACK",
                CreatedBy = "ops-seed",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync();
        }

        var syncResponse = await SendTrustAsync(HttpMethod.Post, "/api/trust/operational-alerts/sync", null, "ops-admin", "Admin");
        Assert.Equal(HttpStatusCode.OK, syncResponse.StatusCode);

        var summaryResponse = await SendTrustAsync(
            HttpMethod.Get,
            $"/api/trust/operational-alerts?trustAccountId={seed.AccountId}",
            null,
            "ops-admin",
            "Admin");
        var summary = JsonSerializer.Deserialize<TrustOperationalAlertSummary>(await summaryResponse.Content.ReadAsStringAsync(), JsonOptions());
        var alert = Assert.Single(summary!.Alerts, a => a.AlertType == "outstanding_item_aging");
        Assert.False(string.IsNullOrWhiteSpace(alert.AlertId));
        Assert.Equal("open", alert.WorkflowStatus);

        var ackResponse = await SendTrustAsync(
            HttpMethod.Post,
            $"/api/trust/operational-alerts/{alert.AlertId}/ack",
            new { notes = "Reviewed by trust ops" },
            "ops-reviewer",
            "Partner");
        Assert.Equal(HttpStatusCode.OK, ackResponse.StatusCode);

        var historyResponse = await SendTrustAsync(
            HttpMethod.Get,
            $"/api/trust/operational-alerts/{alert.AlertId}/history",
            null,
            "ops-reviewer",
            "Partner");
        var history = JsonSerializer.Deserialize<List<TrustOperationalAlertEventDto>>(await historyResponse.Content.ReadAsStringAsync(), JsonOptions());
        Assert.Contains(history!, h => h.EventType == "acknowledged");

        using var verifyScope = CreateTenantScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var notificationCount = await verifyDb.Notifications.CountAsync(n => n.UserId == "responsible-lawyer-1" && n.Link == "tab:trust");
        Assert.True(notificationCount > 0);
    }

    [Fact]
    public async Task OperationalAlerts_SyncAutoResolves_WhenUnderlyingConditionClears()
    {
        var seed = await SeedTrustLedgerAsync();

        using (var scope = CreateTenantScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
            db.TrustOutstandingItems.Add(new TrustOutstandingItem
            {
                Id = Guid.NewGuid().ToString(),
                TrustAccountId = seed.AccountId,
                PeriodStart = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                PeriodEnd = new DateTime(2026, 3, 31, 0, 0, 0, DateTimeKind.Utc),
                OccurredAt = DateTime.UtcNow.AddDays(-16),
                ItemType = "outstanding_check",
                ImpactDirection = "decrease_bank",
                Status = "open",
                Source = "manual",
                Amount = 22m,
                Reference = "CHK-OPS-RESOLVE",
                CreatedBy = "ops-seed",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync();
        }

        var firstSync = await SendTrustAsync(HttpMethod.Post, "/api/trust/operational-alerts/sync", null, "ops-admin-2", "Admin");
        Assert.Equal(HttpStatusCode.OK, firstSync.StatusCode);

        using (var scope = CreateTenantScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
            var outstanding = await db.TrustOutstandingItems.SingleAsync(i => i.Reference == "CHK-OPS-RESOLVE");
            outstanding.Status = "resolved";
            outstanding.ResolvedBy = "ops-admin-2";
            outstanding.ResolvedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        var secondSync = await SendTrustAsync(HttpMethod.Post, "/api/trust/operational-alerts/sync", null, "ops-admin-2", "Admin");
        Assert.Equal(HttpStatusCode.OK, secondSync.StatusCode);

        var recordsResponse = await SendTrustAsync(
            HttpMethod.Get,
            $"/api/trust/operational-alert-records?trustAccountId={seed.AccountId}&workflowStatus=resolved",
            null,
            "ops-admin-2",
            "Admin");
        var recordsBody = await recordsResponse.Content.ReadAsStringAsync();
        Assert.True(recordsResponse.IsSuccessStatusCode, recordsBody);
        var records = JsonSerializer.Deserialize<List<TrustOperationalAlertRecordDto>>(recordsBody, JsonOptions());
        Assert.Contains(records!, record => record.AlertType == "outstanding_item_aging" && record.WorkflowStatus == "resolved");
    }

    [Fact]
    public async Task OpsInbox_SyncClaimAndDefer_PersistsWorkflowAndHistory()
    {
        var seed = await SeedTrustLedgerAsync();

        using (var scope = CreateTenantScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
            db.TrustOutstandingItems.Add(new TrustOutstandingItem
            {
                Id = Guid.NewGuid().ToString(),
                TrustAccountId = seed.AccountId,
                ClientTrustLedgerId = seed.LedgerId,
                PeriodStart = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
                PeriodEnd = new DateTime(2026, 4, 30, 0, 0, 0, DateTimeKind.Utc),
                OccurredAt = DateTime.UtcNow.AddDays(-20),
                ItemType = "outstanding_check",
                ImpactDirection = "decrease_bank",
                Amount = 42.50m,
                Status = "open",
                Description = "Ops inbox seed"
            });
            await db.SaveChangesAsync();
        }

        var syncResponse = await SendTrustAsync(
            HttpMethod.Post,
            "/api/trust/ops-inbox/sync",
            payload: null,
            userId: "ops-sync-user",
            role: "Partner");
        Assert.Equal(HttpStatusCode.OK, syncResponse.StatusCode);

        using (var verifyScope = CreateTenantScope())
        {
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
            var item = await verifyDb.TrustOpsInboxItems.AsNoTracking()
                .Where(i => i.TrustAccountId == seed.AccountId && i.BlockerGroup == "exception_blocker")
                .OrderByDescending(i => i.CreatedAt)
                .FirstOrDefaultAsync();
            Assert.NotNull(item);
            Assert.Equal("exception_blocker", item!.BlockerGroup);

            var claimResponse = await SendTrustAsync(
                HttpMethod.Post,
                $"/api/trust/ops-inbox/{item.Id}/claim",
                new { notes = "Claiming inbox item" },
                userId: "ops-claimer",
                role: "Partner");
            Assert.Equal(HttpStatusCode.OK, claimResponse.StatusCode);
            var claimed = JsonSerializer.Deserialize<TrustOpsInboxItemDto>(await claimResponse.Content.ReadAsStringAsync(), JsonOptions());
            Assert.NotNull(claimed);
            Assert.Equal("claimed", claimed!.WorkflowStatus);
            Assert.Equal("ops-claimer", claimed.AssignedUserId);

            var deferredUntil = DateTime.UtcNow.AddHours(36);
            var deferResponse = await SendTrustAsync(
                HttpMethod.Post,
                $"/api/trust/ops-inbox/{item.Id}/defer",
                new { deferredUntilUtc = deferredUntil, notes = "Waiting for bank response" },
                userId: "ops-claimer",
                role: "Partner");
            Assert.Equal(HttpStatusCode.OK, deferResponse.StatusCode);
            var deferred = JsonSerializer.Deserialize<TrustOpsInboxItemDto>(await deferResponse.Content.ReadAsStringAsync(), JsonOptions());
            Assert.NotNull(deferred);
            Assert.Equal("deferred", deferred!.WorkflowStatus);
            Assert.True(deferred.DeferredUntil > DateTime.UtcNow);

            var historyResponse = await SendTrustAsync(
                HttpMethod.Get,
                $"/api/trust/ops-inbox/{item.Id}/history",
                payload: null,
                userId: "ops-claimer",
                role: "Partner");
            Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);
            var history = JsonSerializer.Deserialize<List<TrustOpsInboxEventDto>>(await historyResponse.Content.ReadAsStringAsync(), JsonOptions());
            Assert.NotNull(history);
            Assert.Contains(history!, e => e.EventType == "claimed");
            Assert.Contains(history!, e => e.EventType == "deferred");
        }
    }

    [Fact]
    public async Task ComplianceBundle_SignAndVerify_PersistsIntegrityMetadata()
    {
        var seed = await SeedTrustLedgerAsync();

        var importResponse = await SendTrustAsync(
            HttpMethod.Post,
            "/api/trust/statements/import",
            new
            {
                trustAccountId = seed.AccountId,
                periodStart = "2026-04-01T00:00:00Z",
                periodEnd = "2026-04-30T00:00:00Z",
                statementEndingBalance = 0.00m,
                source = "manual",
                sourceFileName = "bundle.csv",
                sourceFileHash = "bundle-hash-001",
                lines = Array.Empty<object>()
            },
            userId: "bundle-importer",
            role: "Partner");
        Assert.Equal(HttpStatusCode.OK, importResponse.StatusCode);
        var statementImport = JsonSerializer.Deserialize<TrustStatementImport>(await importResponse.Content.ReadAsStringAsync(), JsonOptions());
        Assert.NotNull(statementImport);

        var packetResponse = await SendTrustAsync(
            HttpMethod.Post,
            "/api/trust/reconciliation-packets",
            new
            {
                trustAccountId = seed.AccountId,
                periodStart = "2026-04-01T00:00:00Z",
                periodEnd = "2026-04-30T00:00:00Z",
                statementImportId = statementImport!.Id,
                notes = "Bundle packet"
            },
            userId: "bundle-packet-builder",
            role: "Partner");
        Assert.Equal(HttpStatusCode.OK, packetResponse.StatusCode);
        var packet = JsonSerializer.Deserialize<TrustReconciliationPacket>(await packetResponse.Content.ReadAsStringAsync(), JsonOptions());
        Assert.NotNull(packet);

        var bundleResponse = await SendTrustAsync(
            HttpMethod.Post,
            "/api/trust/recovery/compliance-bundle",
            new
            {
                trustAccountId = seed.AccountId,
                trustReconciliationPacketId = packet!.Id,
                periodStart = "2026-04-01T00:00:00Z",
                periodEnd = "2026-04-30T00:00:00Z",
                includeJsonPacket = true,
                includeAccountJournalCsv = true,
                includeApprovalRegisterCsv = true,
                includeClientLedgerCards = true,
                notes = "Bundle for signing"
            },
            userId: "bundle-generator",
            role: "Partner");
        Assert.Equal(HttpStatusCode.OK, bundleResponse.StatusCode);
        var bundle = JsonSerializer.Deserialize<TrustComplianceBundleResult>(await bundleResponse.Content.ReadAsStringAsync(), JsonOptions());
        Assert.NotNull(bundle);
        Assert.Equal("unsigned", bundle!.Integrity?.IntegrityStatus);

        var signResponse = await SendTrustAsync(
            HttpMethod.Post,
            $"/api/trust/recovery/compliance-bundle/{bundle.ManifestExportId}/sign",
            new
            {
                retentionPolicyTag = "regulator_7y",
                redactionProfile = "external_regulator",
                notes = "Signed for audit packet"
            },
            userId: "bundle-signer",
            role: "Partner");
        Assert.Equal(HttpStatusCode.OK, signResponse.StatusCode);
        var signed = JsonSerializer.Deserialize<TrustBundleIntegrityDto>(await signResponse.Content.ReadAsStringAsync(), JsonOptions());
        Assert.NotNull(signed);
        Assert.Equal("signed", signed!.IntegrityStatus);
        Assert.Equal("verified", signed.VerificationStatus);
        Assert.Equal("regulator_7y", signed.RetentionPolicyTag);
        Assert.Equal("external_regulator", signed.RedactionProfile);

        var verifyResponse = await SendTrustAsync(
            HttpMethod.Get,
            $"/api/trust/recovery/compliance-bundle/{bundle.ManifestExportId}/integrity",
            payload: null,
            userId: "bundle-auditor",
            role: "Partner");
        Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);
        var verified = JsonSerializer.Deserialize<TrustBundleIntegrityDto>(await verifyResponse.Content.ReadAsStringAsync(), JsonOptions());
        Assert.NotNull(verified);
        Assert.Equal("verified", verified!.VerificationStatus);

        using var verifyScope = CreateTenantScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var manifest = await verifyDb.TrustComplianceExports.AsNoTracking().SingleAsync(e => e.Id == bundle.ManifestExportId);
        var signature = await verifyDb.TrustBundleSignatures.AsNoTracking().SingleAsync(s => s.ManifestExportId == bundle.ManifestExportId);
        Assert.Equal("verified", manifest.IntegrityStatus);
        Assert.Equal("regulator_7y", manifest.RetentionPolicyTag);
        Assert.Equal("external_regulator", manifest.RedactionProfile);
        Assert.False(string.IsNullOrWhiteSpace(signature.SignatureDigest));
    }

    [Fact]
    public async Task CloseForecast_SyncCreatesBlockedSnapshot_InboxAndReminder_ForDailyCadence()
    {
        var seed = await SeedTrustLedgerAsync();
        await UpdateGovernanceAsync(seed.AccountId, new
        {
            accountType = "iolta",
            responsibleLawyerUserId = "close-forecast-lawyer",
            allowedSignatories = Array.Empty<string>(),
            jurisdictionPolicyKey = "ca-iolta-baseline",
            statementCadence = "daily",
            overdraftNotificationEnabled = true
        });

        var syncResponse = await SendTrustAsync(
            HttpMethod.Post,
            "/api/trust/close-forecast/sync?generateDraftBundles=false",
            payload: null,
            userId: "close-forecast-admin",
            role: "Admin");
        var syncBody = await syncResponse.Content.ReadAsStringAsync();
        Assert.True(syncResponse.IsSuccessStatusCode, syncBody);

        var secondSyncResponse = await SendTrustAsync(
            HttpMethod.Post,
            "/api/trust/close-forecast/sync?generateDraftBundles=false",
            payload: null,
            userId: "close-forecast-admin",
            role: "Admin");
        var secondSyncBody = await secondSyncResponse.Content.ReadAsStringAsync();
        Assert.True(secondSyncResponse.IsSuccessStatusCode, secondSyncBody);

        var summaryResponse = await SendTrustAsync(
            HttpMethod.Get,
            $"/api/trust/close-forecast?trustAccountId={seed.AccountId}",
            payload: null,
            userId: "close-forecast-admin",
            role: "Admin");
        var summaryBody = await summaryResponse.Content.ReadAsStringAsync();
        Assert.True(summaryResponse.IsSuccessStatusCode, summaryBody);

        var summary = JsonSerializer.Deserialize<TrustCloseForecastSummaryDto>(summaryBody, JsonOptions());
        Assert.NotNull(summary);
        var snapshot = Assert.Single(summary!.Snapshots);
        Assert.Equal("overdue", snapshot.ReadinessStatus);
        Assert.True(snapshot.MissingStatementImport);
        Assert.False(string.IsNullOrWhiteSpace(snapshot.RecommendedAction));

        using var scope = CreateTenantScope();
        var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var inboxItem = await db.TrustOpsInboxItems.AsNoTracking().SingleAsync(x => x.TrustCloseForecastSnapshotId == snapshot.Id);
        var notifications = await db.Notifications.AsNoTracking()
            .Where(n => n.UserId == "close-forecast-lawyer")
            .ToListAsync();

        Assert.Equal("close_forecast", inboxItem.ItemType);
        Assert.Equal("open", inboxItem.WorkflowStatus);
        Assert.Contains(notifications, n => n.Link == "tab:trust" && n.Title.Contains("Trust close", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CloseForecast_SyncCreatesDraftBundleManifest_ForEligiblePacket()
    {
        var seed = await SeedTrustLedgerAsync();
        await UpsertPolicyAsync("CA-phase6e-close", new
        {
            policyKey = "CA-phase6e-close",
            jurisdiction = "CA",
            accountType = "all",
            versionNumber = 1,
            dualApprovalThreshold = 5000m,
            responsibleLawyerApprovalThreshold = 25000m,
            signatoryApprovalThreshold = 5000m,
            monthlyCloseCadenceDays = 17,
            exceptionAgingSlaHours = 48,
            retentionPeriodMonths = 60,
            requireMonthlyThreeWayReconciliation = true,
            requireResponsibleLawyerAssignment = true
        });
        await UpdateGovernanceAsync(seed.AccountId, new
        {
            accountType = "iolta",
            responsibleLawyerUserId = "draft-bundle-lawyer",
            allowedSignatories = Array.Empty<string>(),
            jurisdictionPolicyKey = "CA-phase6e-close",
            statementCadence = "monthly",
            overdraftNotificationEnabled = true
        });

        var previousMonth = DateTime.UtcNow.AddMonths(-1);
        var periodStart = new DateTime(previousMonth.Year, previousMonth.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodEnd = new DateTime(previousMonth.Year, previousMonth.Month, DateTime.DaysInMonth(previousMonth.Year, previousMonth.Month), 0, 0, 0, DateTimeKind.Utc);

        var importResponse = await SendTrustAsync(
            HttpMethod.Post,
            "/api/trust/statements/import",
            new
            {
                trustAccountId = seed.AccountId,
                periodStart,
                periodEnd,
                statementEndingBalance = 0m,
                source = "manual",
                sourceFileName = "statement.csv",
                sourceFileHash = "hash-p6e-draft"
            },
            userId: "draft-bundle-importer",
            role: "Partner");
        var importBody = await importResponse.Content.ReadAsStringAsync();
        Assert.True(importResponse.IsSuccessStatusCode, importBody);
        var importDto = JsonSerializer.Deserialize<TrustStatementImport>(importBody, JsonOptions());
        Assert.NotNull(importDto);

        var packetResponse = await SendTrustAsync(
            HttpMethod.Post,
            "/api/trust/reconciliation-packets",
            new
            {
                trustAccountId = seed.AccountId,
                periodStart,
                periodEnd,
                statementImportId = importDto!.Id
            },
            userId: "draft-bundle-preparer",
            role: "Partner");
        var packetBody = await packetResponse.Content.ReadAsStringAsync();
        Assert.True(packetResponse.IsSuccessStatusCode, packetBody);

        var syncResponse = await SendTrustAsync(
            HttpMethod.Post,
            "/api/trust/close-forecast/sync?generateDraftBundles=true",
            payload: null,
            userId: "draft-bundle-admin",
            role: "Admin");
        var syncBody = await syncResponse.Content.ReadAsStringAsync();
        Assert.True(syncResponse.IsSuccessStatusCode, syncBody);

        var summaryResponse = await SendTrustAsync(
            HttpMethod.Get,
            $"/api/trust/close-forecast?trustAccountId={seed.AccountId}",
            payload: null,
            userId: "draft-bundle-admin",
            role: "Admin");
        var summaryBody = await summaryResponse.Content.ReadAsStringAsync();
        Assert.True(summaryResponse.IsSuccessStatusCode, summaryBody);

        var summary = JsonSerializer.Deserialize<TrustCloseForecastSummaryDto>(summaryBody, JsonOptions());
        Assert.NotNull(summary);
        var snapshot = Assert.Single(summary!.Snapshots);
        Assert.True(snapshot.HasCanonicalPacket, summaryBody);
        Assert.False(snapshot.MissingStatementImport, summaryBody);
        Assert.True(snapshot.DraftBundleEligible, summaryBody);
        Assert.False(string.IsNullOrWhiteSpace(snapshot.DraftBundleManifestExportId), summaryBody);

        using var scope = CreateTenantScope();
        var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var manifest = await db.TrustComplianceExports.AsNoTracking()
            .SingleAsync(x => x.Id == snapshot.DraftBundleManifestExportId);
        Assert.Equal("compliance_bundle_manifest", manifest.ExportType);
        Assert.Equal(seed.AccountId, manifest.TrustAccountId);
    }

    private async Task<(string AccountId, string LedgerId)> SeedTrustLedgerAsync()
    {
        var clientId = Guid.NewGuid().ToString();
        var accountId = Guid.NewGuid().ToString();
        var ledgerId = Guid.NewGuid().ToString();

        using var scope = CreateTenantScope();
        var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();

        db.Clients.Add(new Client
        {
            Id = clientId,
            Name = $"Client-{Guid.NewGuid():N}",
            Email = $"{Guid.NewGuid():N}@example.com",
            Type = "Individual",
            Status = "Active"
        });

        db.TrustBankAccounts.Add(new TrustBankAccount
        {
            Id = accountId,
            Name = "Main Trust",
            BankName = "Test Bank",
            AccountNumberEnc = "000123456789",
            RoutingNumber = "123456789",
            Jurisdiction = "CA",
            Status = TrustAccountStatus.ACTIVE
        });

        db.ClientTrustLedgers.Add(new ClientTrustLedger
        {
            Id = ledgerId,
            ClientId = clientId,
            TrustAccountId = accountId,
            Status = LedgerStatus.ACTIVE,
            Notes = "Test trust ledger"
        });

        await db.SaveChangesAsync();
        return (accountId, ledgerId);
    }

    private async Task<TrustTransaction> CreatePendingDepositAsync(
        string accountId,
        string ledgerId,
        string userId,
        string role,
        decimal amount = 100.00m,
        string? checkNumber = "CHK-200")
    {
        var response = await SendTrustAsync(
            HttpMethod.Post,
            "/api/trust/deposit",
            new
            {
                trustAccountId = accountId,
                amount,
                description = "Retainer deposit",
                payorPayee = "Client",
                checkNumber,
                allocations = new[]
                {
                    new { ledgerId, amount, description = "Funding" }
                }
            },
            userId,
            role);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonSerializer.Deserialize<TrustTransaction>(body, JsonOptions())!;
    }

    private async Task<TrustTransaction> CreatePendingWithdrawalAsync(
        string accountId,
        string ledgerId,
        string userId,
        string role,
        decimal amount)
    {
        var response = await SendTrustAsync(
            HttpMethod.Post,
            "/api/trust/withdrawal",
            new
            {
                trustAccountId = accountId,
                ledgerId,
                amount,
                description = "Trust disbursement",
                payorPayee = "Client"
            },
            userId,
            role);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonSerializer.Deserialize<TrustTransaction>(body, JsonOptions())!;
    }

    private async Task FundTrustLedgerAsync(string accountId, string ledgerId, decimal amount)
    {
        using var scope = CreateTenantScope();
        var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var account = await db.TrustBankAccounts.SingleAsync(a => a.Id == accountId);
        var ledger = await db.ClientTrustLedgers.SingleAsync(l => l.Id == ledgerId);
        account.CurrentBalance = amount;
        account.ClearedBalance = amount;
        account.UnclearedBalance = 0m;
        account.AvailableDisbursementCapacity = amount;
        ledger.RunningBalance = amount;
        ledger.ClearedBalance = amount;
        ledger.UnclearedBalance = 0m;
        ledger.AvailableToDisburse = amount;
        await db.SaveChangesAsync();
    }

    private async Task UpsertPolicyAsync(string policyKey, object payload)
    {
        var response = await SendTrustAsync(
            HttpMethod.Post,
            "/api/trust/policies",
            payload,
            userId: "policy-admin",
            role: "Admin");

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(policyKey, body, StringComparison.OrdinalIgnoreCase);
    }

    private async Task UpdateGovernanceAsync(string accountId, object payload)
    {
        var response = await SendTrustAsync(
            HttpMethod.Post,
            $"/api/trust/accounts/{accountId}/governance",
            payload,
            userId: "gov-admin",
            role: "Admin");

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(body));
    }

    private async Task<(string InvoiceId, string PaymentId)> SeedBillingAllocationAsync()
    {
        var clientId = Guid.NewGuid().ToString();
        var invoiceId = Guid.NewGuid().ToString();
        var paymentId = Guid.NewGuid().ToString();

        using var scope = CreateTenantScope();
        var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();

        db.Clients.Add(new Client
        {
            Id = clientId,
            Name = $"BillingClient-{Guid.NewGuid():N}",
            Email = $"{Guid.NewGuid():N}@example.com",
            Type = "Individual",
            Status = "Active"
        });

        db.Invoices.Add(new Invoice
        {
            Id = invoiceId,
            ClientId = clientId,
            Number = $"INV-{Guid.NewGuid().ToString("N")[..8]}",
            IssueDate = DateTime.UtcNow.Date,
            Total = 150m,
            AmountPaid = 0m,
            Balance = 150m,
            Status = JurisFlow.Server.Enums.InvoiceStatus.Sent
        });

        db.PaymentTransactions.Add(new PaymentTransaction
        {
            Id = paymentId,
            InvoiceId = invoiceId,
            ClientId = clientId,
            Amount = 150m,
            Currency = "USD",
            PaymentMethod = "Check",
            Status = "Succeeded",
            InvoiceAppliedAmount = 0m,
            InvoiceRefundAppliedAmount = 0m
        });

        await db.SaveChangesAsync();
        return (invoiceId, paymentId);
    }

    private async Task<(string AccountId, string LedgerId, string InvoiceId, string PaymentId)> SeedTrustFundedBillingAllocationAsync()
    {
        var clientId = Guid.NewGuid().ToString();
        var accountId = Guid.NewGuid().ToString();
        var ledgerId = Guid.NewGuid().ToString();
        var invoiceId = Guid.NewGuid().ToString();
        var paymentId = Guid.NewGuid().ToString();

        using var scope = CreateTenantScope();
        var db = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();

        db.Clients.Add(new Client
        {
            Id = clientId,
            Name = $"TrustBillingClient-{Guid.NewGuid():N}",
            Email = $"{Guid.NewGuid():N}@example.com",
            Type = "Individual",
            Status = "Active"
        });

        db.TrustBankAccounts.Add(new TrustBankAccount
        {
            Id = accountId,
            Name = "Trust Billing Account",
            BankName = "Test Bank",
            AccountNumberEnc = "000987654321",
            RoutingNumber = "123456789",
            Jurisdiction = "CA",
            Status = TrustAccountStatus.ACTIVE,
            CurrentBalance = 300m,
            ClearedBalance = 300m,
            UnclearedBalance = 0m,
            AvailableDisbursementCapacity = 300m
        });

        db.ClientTrustLedgers.Add(new ClientTrustLedger
        {
            Id = ledgerId,
            ClientId = clientId,
            TrustAccountId = accountId,
            Status = LedgerStatus.ACTIVE,
            RunningBalance = 300m,
            ClearedBalance = 300m,
            UnclearedBalance = 0m,
            AvailableToDisburse = 300m,
            Notes = "Trust-backed billing ledger"
        });

        db.Invoices.Add(new Invoice
        {
            Id = invoiceId,
            ClientId = clientId,
            Number = $"INV-{Guid.NewGuid().ToString("N")[..8]}",
            IssueDate = DateTime.UtcNow.Date,
            Total = 300m,
            AmountPaid = 0m,
            Balance = 300m,
            Status = JurisFlow.Server.Enums.InvoiceStatus.Sent
        });

        db.PaymentTransactions.Add(new PaymentTransaction
        {
            Id = paymentId,
            InvoiceId = invoiceId,
            ClientId = clientId,
            Amount = 300m,
            Currency = "USD",
            PaymentMethod = "Check",
            Status = "Succeeded",
            InvoiceAppliedAmount = 0m,
            InvoiceRefundAppliedAmount = 0m
        });

        await db.SaveChangesAsync();
        return (accountId, ledgerId, invoiceId, paymentId);
    }

    private async Task<HttpResponseMessage> SendTrustAsync(HttpMethod method, string url, object? payload, string userId, string role, string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-Test-UserId", userId);
        request.Headers.Add("X-Test-Role", role);
        request.Headers.Add("X-Test-TenantId", TestApplicationFactory.TestTenantId);
        request.Headers.Add("X-Test-TenantSlug", TestApplicationFactory.TestTenantSlug);
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        if (payload != null)
        {
            request.Content = JsonContent.Create(payload);
        }

        return await _client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> SendBillingAsync(HttpMethod method, string url, object? payload, string userId, string role)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-Test-UserId", userId);
        request.Headers.Add("X-Test-Role", role);
        request.Headers.Add("X-Test-TenantId", TestApplicationFactory.TestTenantId);
        request.Headers.Add("X-Test-TenantSlug", TestApplicationFactory.TestTenantSlug);

        if (payload != null)
        {
            request.Content = JsonContent.Create(payload);
        }

        return await _client.SendAsync(request);
    }

    private IServiceScope CreateTenantScope()
    {
        var scope = _factory.Services.CreateScope();
        var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
        tenantContext.Set(TestApplicationFactory.TestTenantId, TestApplicationFactory.TestTenantSlug);
        return scope;
    }

    private static JsonSerializerOptions JsonOptions()
    {
        return new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
    }
}
