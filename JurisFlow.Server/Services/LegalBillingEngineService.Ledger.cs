using System.Globalization;
using System.Text.Json;
using JurisFlow.Server.Enums;
using JurisFlow.Server.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Task = System.Threading.Tasks.Task;

namespace JurisFlow.Server.Services
{
    public sealed partial class LegalBillingEngineService
    {
        public async Task<IReadOnlyList<BillingLedgerEntry>> ListLedgerEntriesAsync(BillingLedgerQuery query, CancellationToken ct = default)
        {
            var q = _context.BillingLedgerEntries.AsQueryable();
            if (!string.IsNullOrWhiteSpace(query.LedgerDomain)) q = q.Where(e => e.LedgerDomain == query.LedgerDomain);
            if (!string.IsNullOrWhiteSpace(query.LedgerBucket)) q = q.Where(e => e.LedgerBucket == query.LedgerBucket);
            if (!string.IsNullOrWhiteSpace(query.InvoiceId)) q = q.Where(e => e.InvoiceId == query.InvoiceId);
            if (!string.IsNullOrWhiteSpace(query.MatterId)) q = q.Where(e => e.MatterId == query.MatterId);
            if (!string.IsNullOrWhiteSpace(query.PaymentTransactionId)) q = q.Where(e => e.PaymentTransactionId == query.PaymentTransactionId);
            if (query.FromUtc != null) q = q.Where(e => e.PostedAt >= query.FromUtc.Value);
            if (query.ToUtc != null) q = q.Where(e => e.PostedAt <= query.ToUtc.Value);

            return await q.OrderByDescending(e => e.PostedAt)
                .Take(Math.Clamp(query.Limit ?? 300, 1, 1000))
                .ToListAsync(ct);
        }

        public async Task<BillingLedgerEntry> PostManualLedgerEntryAsync(ManualLedgerEntryRequest request, string? userId, CancellationToken ct = default)
        {
            await EnsureNotLockedAsync(request.PostedAt ?? DateTime.UtcNow, ct, "manual_ledger_post");

            var entryType = NormalizeEnum(request.EntryType, "adjustment", ["credit_memo", "writeoff", "adjustment"]);
            var domain = NormalizeEnum(request.LedgerDomain, "billing", ["billing", "trust", "operating"]);
            if (string.IsNullOrWhiteSpace(request.LedgerBucket))
            {
                throw new InvalidOperationException("LedgerBucket is required.");
            }

            var amount = NormalizeMoney(request.Amount);
            if (amount == 0m)
            {
                throw new InvalidOperationException("Amount cannot be zero.");
            }

            var correlationKey = string.IsNullOrWhiteSpace(request.IdempotencyKey)
                ? $"manual:{domain}:{request.LedgerBucket}:{entryType}:{request.InvoiceId}:{request.PaymentTransactionId}:{amount.ToString(CultureInfo.InvariantCulture)}"
                : request.IdempotencyKey.Trim();

            var existing = await _context.BillingLedgerEntries.FirstOrDefaultAsync(e => e.CorrelationKey == correlationKey, ct);
            if (existing != null) return existing;

            Invoice? invoice = null;
            InvoicePayorAllocation? invoicePayorAllocation = null;
            if (!string.IsNullOrWhiteSpace(request.InvoiceId))
            {
                invoice = await _context.Invoices.AsNoTracking()
                    .Include(i => i.LineItems)
                    .FirstOrDefaultAsync(i => i.Id == request.InvoiceId, ct)
                    ?? throw new InvalidOperationException("Invoice not found.");

                if (!string.IsNullOrWhiteSpace(request.InvoiceLineItemId) &&
                    !invoice.LineItems.Any(li => li.Id == request.InvoiceLineItemId))
                {
                    throw new InvalidOperationException("Invoice line item not found.");
                }
            }

            if (!string.IsNullOrWhiteSpace(request.InvoicePayorAllocationId))
            {
                if (invoice == null)
                {
                    throw new InvalidOperationException("InvoiceId is required when InvoicePayorAllocationId is provided.");
                }

                invoicePayorAllocation = await _context.InvoicePayorAllocations
                    .FirstOrDefaultAsync(a => a.Id == request.InvoicePayorAllocationId && a.InvoiceId == invoice.Id, ct)
                    ?? throw new InvalidOperationException("Invoice payor allocation not found for invoice.");
            }

            var resolvedPayorClientId = ResolveManualLedgerPayorClientId(request, invoice, invoicePayorAllocation);

            await _trustRiskRadarService.EnforceNoActiveHardHoldsAsync(new TrustRiskRadarService.TrustRiskHoldGuardContext
            {
                OperationType = "manual_ledger_post",
                InvoiceId = request.InvoiceId,
                InvoicePayorAllocationId = invoicePayorAllocation?.Id ?? request.InvoicePayorAllocationId,
                PaymentTransactionId = request.PaymentTransactionId,
                TrustTransactionId = request.TrustTransactionId,
                MatterId = request.MatterId ?? invoice?.MatterId,
                ClientId = request.ClientId ?? invoice?.ClientId,
                PayorClientId = resolvedPayorClientId
            }, ct);

            var entry = new BillingLedgerEntry
            {
                LedgerDomain = domain,
                LedgerBucket = request.LedgerBucket.Trim().ToLowerInvariant(),
                EntryType = entryType,
                Currency = NormalizeCurrency(request.Currency, "USD"),
                Amount = amount,
                MatterId = NullIfEmpty(request.MatterId),
                ClientId = NullIfEmpty(request.ClientId),
                PayorClientId = resolvedPayorClientId,
                InvoicePayorAllocationId = invoicePayorAllocation?.Id,
                InvoiceId = NullIfEmpty(request.InvoiceId),
                InvoiceLineItemId = NullIfEmpty(request.InvoiceLineItemId),
                PaymentTransactionId = NullIfEmpty(request.PaymentTransactionId),
                TrustTransactionId = NullIfEmpty(request.TrustTransactionId),
                CorrelationKey = correlationKey,
                Description = Truncate(request.Description, 2048),
                MetadataJson = TruncateJson(request.MetadataJson),
                PostedBy = userId,
                PostedAt = request.PostedAt ?? DateTime.UtcNow
            };

            _context.BillingLedgerEntries.Add(entry);
            await _context.SaveChangesAsync(ct);
            await TryCreateManualLedgerPayorDistributionEntriesAsync(entry, request, userId, ct);
            await TryRecordTrustRiskLedgerAsync(entry, "manual_ledger_posted", ct);
            return entry;
        }

        public async Task<BillingLedgerEntry?> ReverseLedgerEntryAsync(string ledgerEntryId, LedgerReversalRequest request, string? userId, CancellationToken ct = default)
        {
            var entry = await _context.BillingLedgerEntries.FirstOrDefaultAsync(e => e.Id == ledgerEntryId, ct);
            if (entry == null) return null;

            var existingReversal = await _context.BillingLedgerEntries
                .Where(e => e.ReversalOfLedgerEntryId == entry.Id)
                .OrderByDescending(e => e.PostedAt)
                .FirstOrDefaultAsync(ct);
            if (existingReversal != null)
            {
                return existingReversal;
            }

            await _trustRiskRadarService.EnforceNoActiveHardHoldsAsync(new TrustRiskRadarService.TrustRiskHoldGuardContext
            {
                OperationType = "ledger_reversal",
                BillingLedgerEntryId = entry.Id,
                InvoiceId = entry.InvoiceId,
                InvoicePayorAllocationId = entry.InvoicePayorAllocationId,
                PaymentTransactionId = entry.PaymentTransactionId,
                TrustTransactionId = entry.TrustTransactionId,
                MatterId = entry.MatterId,
                ClientId = entry.ClientId,
                PayorClientId = entry.PayorClientId
            }, ct);

            await EnsureNotLockedAsync(request.PostedAt ?? DateTime.UtcNow, ct, "ledger_reversal");

            await using var tx = await _context.Database.BeginTransactionAsync(ct);

            var reversal = new BillingLedgerEntry
            {
                LedgerDomain = entry.LedgerDomain,
                LedgerBucket = entry.LedgerBucket,
                EntryType = "reversal",
                Currency = entry.Currency,
                Amount = NormalizeMoney(-entry.Amount),
                MatterId = entry.MatterId,
                ClientId = entry.ClientId,
                PayorClientId = entry.PayorClientId,
                InvoicePayorAllocationId = entry.InvoicePayorAllocationId,
                InvoiceId = entry.InvoiceId,
                InvoiceLineItemId = entry.InvoiceLineItemId,
                PaymentTransactionId = entry.PaymentTransactionId,
                PrebillBatchId = entry.PrebillBatchId,
                PrebillLineId = entry.PrebillLineId,
                TrustTransactionId = entry.TrustTransactionId,
                ReversalOfLedgerEntryId = entry.Id,
                CorrelationKey = $"reverse:{entry.Id}",
                Description = Truncate($"Reversal of {entry.Id}. {request.Reason}", 2048),
                MetadataJson = BuildLedgerMetadataJson(new { reversalReason = request.Reason }),
                PostedBy = userId,
                PostedAt = request.PostedAt ?? DateTime.UtcNow
            };

            _context.BillingLedgerEntries.Add(reversal);
            entry.Status = "reversed";

            if (!string.IsNullOrWhiteSpace(entry.CorrelationKey) &&
                !entry.CorrelationKey.Contains(":payor:", StringComparison.Ordinal))
            {
                var companionEntries = await _context.BillingLedgerEntries
                    .Where(e =>
                        e.CorrelationKey != null &&
                        e.CorrelationKey.StartsWith($"{entry.CorrelationKey}:payor:"))
                    .ToListAsync(ct);

                foreach (var companion in companionEntries)
                {
                    var companionReversed = await _context.BillingLedgerEntries.AnyAsync(e => e.ReversalOfLedgerEntryId == companion.Id, ct);
                    if (companionReversed) continue;

                    _context.BillingLedgerEntries.Add(new BillingLedgerEntry
                    {
                        LedgerDomain = companion.LedgerDomain,
                        LedgerBucket = companion.LedgerBucket,
                        EntryType = "reversal",
                        Currency = companion.Currency,
                        Amount = NormalizeMoney(-companion.Amount),
                        MatterId = companion.MatterId,
                        ClientId = companion.ClientId,
                        PayorClientId = companion.PayorClientId,
                        InvoicePayorAllocationId = companion.InvoicePayorAllocationId,
                        InvoiceId = companion.InvoiceId,
                        InvoiceLineItemId = companion.InvoiceLineItemId,
                        PaymentTransactionId = companion.PaymentTransactionId,
                        PrebillBatchId = companion.PrebillBatchId,
                        PrebillLineId = companion.PrebillLineId,
                        TrustTransactionId = companion.TrustTransactionId,
                        ReversalOfLedgerEntryId = companion.Id,
                        CorrelationKey = $"reverse:{companion.Id}",
                        Description = Truncate($"Reversal of distributed ledger {companion.Id}. {request.Reason}", 2048),
                        MetadataJson = BuildLedgerMetadataJson(new { reversalReason = request.Reason, distributionOfLedgerEntryId = entry.Id }),
                        PostedBy = userId,
                        PostedAt = request.PostedAt ?? DateTime.UtcNow
                    });
                    companion.Status = "reversed";
                }
            }

            await _context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            await TryRecordTrustRiskLedgerAsync(reversal, "ledger_reversal_posted", ct);
            return reversal;
        }

        public async Task<BillingPaymentAllocation?> ApplyPaymentAllocationAsync(ApplyPaymentAllocationRequest request, string? userId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(request.PaymentTransactionId) || string.IsNullOrWhiteSpace(request.InvoiceId))
            {
                throw new InvalidOperationException("PaymentTransactionId and InvoiceId are required.");
            }

            var amount = NormalizeMoney(request.Amount);
            if (amount <= 0m)
            {
                throw new InvalidOperationException("Allocation amount must be greater than zero.");
            }

            await EnsureNotLockedAsync(request.AppliedAt ?? DateTime.UtcNow, ct, "payment_allocation_apply");

            var payment = await _context.PaymentTransactions.FirstOrDefaultAsync(p => p.Id == request.PaymentTransactionId, ct)
                ?? throw new InvalidOperationException("Payment transaction not found.");
            var invoice = await _context.Invoices.Include(i => i.LineItems).FirstOrDefaultAsync(i => i.Id == request.InvoiceId, ct)
                ?? throw new InvalidOperationException("Invoice not found.");

            if (!string.Equals(payment.Status, "Succeeded", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(payment.Status, "Partially Refunded", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Only succeeded payments can be allocated.");
            }

            InvoiceLineItem? invoiceLine = null;
            if (!string.IsNullOrWhiteSpace(request.InvoiceLineItemId))
            {
                invoiceLine = invoice.LineItems.FirstOrDefault(li => li.Id == request.InvoiceLineItemId)
                    ?? throw new InvalidOperationException("Invoice line item not found.");
            }

            InvoicePayorAllocation? invoicePayorAllocation = null;
            if (!string.IsNullOrWhiteSpace(request.InvoicePayorAllocationId))
            {
                invoicePayorAllocation = await _context.InvoicePayorAllocations
                    .FirstOrDefaultAsync(a => a.Id == request.InvoicePayorAllocationId && a.InvoiceId == invoice.Id, ct)
                    ?? throw new InvalidOperationException("Invoice payor allocation not found for invoice.");

                if (!string.Equals(invoicePayorAllocation.Status, "active", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Invoice payor allocation is not active.");
                }
            }

            var activeInvoicePayorAllocations = await _context.InvoicePayorAllocations.AsNoTracking()
                .Where(a => a.InvoiceId == invoice.Id && a.Status == "active")
                .OrderBy(a => a.Priority)
                .ThenByDescending(a => a.IsPrimary)
                .ToListAsync(ct);

            if (invoicePayorAllocation == null &&
                string.IsNullOrWhiteSpace(request.InvoicePayorAllocationId) &&
                string.IsNullOrWhiteSpace(request.PayorClientId))
            {
                if (!string.IsNullOrWhiteSpace(payment.InvoicePayorAllocationId))
                {
                    invoicePayorAllocation = activeInvoicePayorAllocations
                        .FirstOrDefault(a => a.Id == payment.InvoicePayorAllocationId);
                }

                if (invoicePayorAllocation == null && activeInvoicePayorAllocations.Count == 1)
                {
                    invoicePayorAllocation = activeInvoicePayorAllocations[0];
                }
                else if (invoicePayorAllocation == null && activeInvoicePayorAllocations.Count > 1)
                {
                    throw new InvalidOperationException("Payor target is required for split-billed invoices. Provide PayorClientId or InvoicePayorAllocationId.");
                }
            }

            var resolvedPayorClientId = ResolveAllocationPayorClientId(request, invoice, invoicePayorAllocation, payment);
            if (!string.IsNullOrWhiteSpace(resolvedPayorClientId))
            {
                var payorExists = await _context.Clients.AsNoTracking().AnyAsync(c => c.Id == resolvedPayorClientId, ct);
                if (!payorExists)
                {
                    throw new InvalidOperationException("Payor client not found.");
                }
            }

            if (invoicePayorAllocation != null &&
                !string.IsNullOrWhiteSpace(resolvedPayorClientId) &&
                !string.Equals(invoicePayorAllocation.PayorClientId, resolvedPayorClientId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("PayorClientId does not match InvoicePayorAllocation.");
            }

            await _trustRiskRadarService.EnforceNoActiveHardHoldsAsync(new TrustRiskRadarService.TrustRiskHoldGuardContext
            {
                OperationType = "payment_allocation_apply",
                PaymentTransactionId = payment.Id,
                InvoiceId = invoice.Id,
                InvoicePayorAllocationId = invoicePayorAllocation?.Id,
                MatterId = request.MatterId ?? invoice.MatterId ?? payment.MatterId,
                ClientId = request.ClientId ?? invoice.ClientId ?? payment.ClientId,
                PayorClientId = resolvedPayorClientId
            }, ct);

            var appliedTotal = await _context.BillingPaymentAllocations
                .Where(a => a.PaymentTransactionId == payment.Id && a.Status == "applied")
                .SumAsync(a => (decimal?)a.Amount, ct) ?? 0m;
            var refundedTotal = NormalizeMoney(payment.RefundAmount ?? 0m);
            var remainingPayment = NormalizeMoney((payment.Amount - refundedTotal) - appliedTotal);
            if (amount > remainingPayment)
            {
                throw new InvalidOperationException("Allocation amount exceeds remaining allocatable payment amount.");
            }

            if (string.Equals(request.FundSource, "trust", StringComparison.OrdinalIgnoreCase))
            {
                await EnforceTrustFundingGuardrailAsync(request, invoice, amount, ct);
            }

            var idempotencyKey = string.IsNullOrWhiteSpace(request.IdempotencyKey)
                ? $"alloc:{payment.Id}:{invoice.Id}:{request.InvoiceLineItemId ?? "header"}:{(invoicePayorAllocation?.Id ?? "no_payor_alloc")}:{(resolvedPayorClientId ?? "no_payor")}:{amount.ToString(CultureInfo.InvariantCulture)}:{(request.FundSource ?? "operating").ToLowerInvariant()}"
                : request.IdempotencyKey.Trim();

            var existing = await _context.BillingPaymentAllocations
                .FirstOrDefaultAsync(a => a.MetadataJson != null && a.MetadataJson.Contains(idempotencyKey) && a.Status == "applied", ct);
            if (existing != null)
            {
                return existing;
            }

            await using var tx = await _context.Database.BeginTransactionAsync(ct);

            var allocation = new BillingPaymentAllocation
            {
                PaymentTransactionId = payment.Id,
                InvoiceId = invoice.Id,
                InvoiceLineItemId = invoiceLine?.Id,
                ClientId = request.ClientId ?? invoice.ClientId ?? payment.ClientId,
                PayorClientId = resolvedPayorClientId,
                InvoicePayorAllocationId = invoicePayorAllocation?.Id,
                MatterId = request.MatterId ?? invoice.MatterId ?? payment.MatterId,
                Amount = amount,
                AllocationType = NormalizeEnum(request.AllocationType, invoiceLine == null ? "invoice_header" : "invoice_line", ["invoice_line", "invoice_header", "tax", "fee"]),
                Status = "applied",
                Notes = Truncate(request.Notes, 2048),
                MetadataJson = BuildPaymentAllocationMetadataJson(new
                {
                    idempotencyKey,
                    fundSource = (request.FundSource ?? "operating").Trim().ToLowerInvariant(),
                    payorClientId = resolvedPayorClientId,
                    invoicePayorAllocationId = invoicePayorAllocation?.Id,
                    trustSourceClientId = request.TrustSourceClientId,
                    request.Reference,
                    request.ApplyInvoiceHeaderIfNotAlreadyApplied
                }),
                AppliedBy = userId,
                AppliedAt = request.AppliedAt ?? DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _context.BillingPaymentAllocations.Add(allocation);
            await _context.SaveChangesAsync(ct);

            var fundSource = (request.FundSource ?? "operating").Trim().ToLowerInvariant();
            var ledgerEntries = new List<BillingLedgerEntry>();
            if (fundSource == "trust")
            {
                ledgerEntries.Add(new BillingLedgerEntry
                {
                    LedgerDomain = "trust",
                    LedgerBucket = "trust_liability",
                    EntryType = "payment_allocation",
                    Currency = payment.Currency,
                    Amount = NormalizeMoney(-amount),
                    MatterId = allocation.MatterId,
                    ClientId = allocation.ClientId,
                    PayorClientId = allocation.PayorClientId,
                    InvoicePayorAllocationId = allocation.InvoicePayorAllocationId,
                    InvoiceId = invoice.Id,
                    InvoiceLineItemId = invoiceLine?.Id,
                    PaymentTransactionId = payment.Id,
                    CorrelationKey = $"allocation:{allocation.Id}:trust_liability",
                    Description = $"Trust liability reduced for invoice {invoice.Id} allocation.",
                    PostedBy = userId,
                    PostedAt = allocation.AppliedAt,
                    MetadataJson = BuildLedgerMetadataJson(new { allocationId = allocation.Id, fundSource, trustAccountId = request.TrustAccountId })
                });
                ledgerEntries.Add(new BillingLedgerEntry
                {
                    LedgerDomain = "operating",
                    LedgerBucket = "cash",
                    EntryType = "payment_allocation",
                    Currency = payment.Currency,
                    Amount = amount,
                    MatterId = allocation.MatterId,
                    ClientId = allocation.ClientId,
                    PayorClientId = allocation.PayorClientId,
                    InvoicePayorAllocationId = allocation.InvoicePayorAllocationId,
                    InvoiceId = invoice.Id,
                    InvoiceLineItemId = invoiceLine?.Id,
                    PaymentTransactionId = payment.Id,
                    CorrelationKey = $"allocation:{allocation.Id}:operating_cash",
                    Description = $"Earned fees transfer from trust for invoice {invoice.Id}.",
                    PostedBy = userId,
                    PostedAt = allocation.AppliedAt,
                    MetadataJson = BuildLedgerMetadataJson(new { allocationId = allocation.Id, fundSource, trustAccountId = request.TrustAccountId })
                });
            }
            else
            {
                ledgerEntries.Add(new BillingLedgerEntry
                {
                    LedgerDomain = "operating",
                    LedgerBucket = "cash",
                    EntryType = "payment_allocation",
                    Currency = payment.Currency,
                    Amount = amount,
                    MatterId = allocation.MatterId,
                    ClientId = allocation.ClientId,
                    PayorClientId = allocation.PayorClientId,
                    InvoicePayorAllocationId = allocation.InvoicePayorAllocationId,
                    InvoiceId = invoice.Id,
                    InvoiceLineItemId = invoiceLine?.Id,
                    PaymentTransactionId = payment.Id,
                    CorrelationKey = $"allocation:{allocation.Id}:operating_cash",
                    Description = $"Operating cash allocation for invoice {invoice.Id}.",
                    PostedBy = userId,
                    PostedAt = allocation.AppliedAt,
                    MetadataJson = BuildLedgerMetadataJson(new { allocationId = allocation.Id, fundSource })
                });
            }

            ledgerEntries.Add(new BillingLedgerEntry
            {
                LedgerDomain = "billing",
                LedgerBucket = "accounts_receivable",
                EntryType = "payment_allocation",
                Currency = payment.Currency,
                Amount = NormalizeMoney(-amount),
                MatterId = allocation.MatterId,
                ClientId = allocation.ClientId,
                PayorClientId = allocation.PayorClientId,
                InvoicePayorAllocationId = allocation.InvoicePayorAllocationId,
                InvoiceId = invoice.Id,
                InvoiceLineItemId = invoiceLine?.Id,
                PaymentTransactionId = payment.Id,
                CorrelationKey = $"allocation:{allocation.Id}:ar",
                Description = $"A/R reduced by payment allocation on invoice {invoice.Id}.",
                PostedBy = userId,
                PostedAt = allocation.AppliedAt,
                MetadataJson = BuildLedgerMetadataJson(new { allocationId = allocation.Id, fundSource })
            });

            _context.BillingLedgerEntries.AddRange(ledgerEntries);
            await _context.SaveChangesAsync(ct);

            allocation.LedgerEntryId = ledgerEntries.Last().Id;
            allocation.UpdatedAt = DateTime.UtcNow;

            if (request.ApplyInvoiceHeaderIfNotAlreadyApplied == true)
            {
                await ApplyInvoiceHeaderAllocationIfNeededAsync(invoice, payment, amount, ct);
            }

            await _context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            foreach (var ledgerEntry in ledgerEntries)
            {
                await TryRecordTrustRiskLedgerAsync(ledgerEntry, "payment_allocation_ledger_posted", ct);
            }
            await TryRecordTrustRiskAllocationAsync(allocation, "payment_allocation_applied", ct);
            return allocation;
        }

        public async Task<BillingPaymentAllocation?> ReversePaymentAllocationAsync(string allocationId, ReversePaymentAllocationRequest request, string? userId, CancellationToken ct = default)
        {
            var allocation = await _context.BillingPaymentAllocations.FirstOrDefaultAsync(a => a.Id == allocationId, ct);
            if (allocation == null) return null;
            if (allocation.Status == "reversed") return allocation;

            await _trustRiskRadarService.EnforceNoActiveHardHoldsAsync(new TrustRiskRadarService.TrustRiskHoldGuardContext
            {
                OperationType = "payment_allocation_reverse",
                BillingPaymentAllocationId = allocation.Id,
                PaymentTransactionId = allocation.PaymentTransactionId,
                InvoiceId = allocation.InvoiceId,
                InvoicePayorAllocationId = allocation.InvoicePayorAllocationId,
                MatterId = allocation.MatterId,
                ClientId = allocation.ClientId,
                PayorClientId = allocation.PayorClientId
            }, ct);

            await EnsureNotLockedAsync(request.ReversedAt ?? DateTime.UtcNow, ct, "payment_allocation_reverse");

            var ledgers = await _context.BillingLedgerEntries
                .Where(e => e.CorrelationKey != null && e.CorrelationKey.StartsWith($"allocation:{allocation.Id}:"))
                .ToListAsync(ct);
            var createdReversals = new List<BillingLedgerEntry>();

            await using var tx = await _context.Database.BeginTransactionAsync(ct);

            foreach (var ledger in ledgers)
            {
                var existsReversal = await _context.BillingLedgerEntries.AnyAsync(e => e.ReversalOfLedgerEntryId == ledger.Id, ct);
                if (existsReversal) continue;

                var reversalLedger = new BillingLedgerEntry
                {
                    LedgerDomain = ledger.LedgerDomain,
                    LedgerBucket = ledger.LedgerBucket,
                    EntryType = "reversal",
                    Currency = ledger.Currency,
                    Amount = NormalizeMoney(-ledger.Amount),
                    MatterId = ledger.MatterId,
                    ClientId = ledger.ClientId,
                    PayorClientId = ledger.PayorClientId,
                    InvoicePayorAllocationId = ledger.InvoicePayorAllocationId,
                    InvoiceId = ledger.InvoiceId,
                    InvoiceLineItemId = ledger.InvoiceLineItemId,
                    PaymentTransactionId = ledger.PaymentTransactionId,
                    ReversalOfLedgerEntryId = ledger.Id,
                    CorrelationKey = $"reverse:allocation:{allocation.Id}:{ledger.Id}",
                    Description = Truncate($"Reversal of allocation {allocation.Id}. {request.Reason}", 2048),
                    MetadataJson = BuildLedgerMetadataJson(new { allocationId, reversalReason = request.Reason }),
                    PostedBy = userId,
                    PostedAt = request.ReversedAt ?? DateTime.UtcNow
                };
                _context.BillingLedgerEntries.Add(reversalLedger);
                createdReversals.Add(reversalLedger);
                ledger.Status = "reversed";
            }

            allocation.Status = "reversed";
            allocation.ReversalOfAllocationId = allocation.ReversalOfAllocationId ?? allocation.Id;
            allocation.Notes = Truncate(string.Join(" ", new[] { allocation.Notes, request.Reason }.Where(v => !string.IsNullOrWhiteSpace(v))), 2048);
            allocation.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            foreach (var reversalLedger in createdReversals)
            {
                await TryRecordTrustRiskLedgerAsync(reversalLedger, "payment_allocation_reversal_ledger_posted", ct);
            }
            await TryRecordTrustRiskAllocationAsync(allocation, "payment_allocation_reversed", ct);
            return allocation;
        }

        private async Task TryCreateManualLedgerPayorDistributionEntriesAsync(BillingLedgerEntry entry, ManualLedgerEntryRequest request, string? userId, CancellationToken ct)
        {
            if (!ShouldCreateManualLedgerPayorDistribution(entry, request))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(entry.CorrelationKey))
            {
                return;
            }

            var existingCompanion = await _context.BillingLedgerEntries
                .AsNoTracking()
                .AnyAsync(e => e.CorrelationKey != null && e.CorrelationKey.StartsWith($"{entry.CorrelationKey}:payor:"), ct);
            if (existingCompanion)
            {
                return;
            }

            var slices = await BuildManualLedgerPayorDistributionSlicesAsync(entry, ct);
            if (slices.Count == 0)
            {
                return;
            }

            var companions = new List<BillingLedgerEntry>(slices.Count);
            for (var i = 0; i < slices.Count; i++)
            {
                var slice = slices[i];
                if (slice.Amount == 0m) continue;

                companions.Add(new BillingLedgerEntry
                {
                    LedgerDomain = entry.LedgerDomain,
                    LedgerBucket = entry.LedgerBucket,
                    EntryType = entry.EntryType,
                    Currency = entry.Currency,
                    Amount = NormalizeMoney(slice.Amount),
                    MatterId = entry.MatterId,
                    ClientId = entry.ClientId,
                    PayorClientId = slice.PayorClientId,
                    InvoicePayorAllocationId = slice.InvoicePayorAllocationId,
                    InvoiceId = entry.InvoiceId,
                    InvoiceLineItemId = entry.InvoiceLineItemId,
                    PaymentTransactionId = entry.PaymentTransactionId,
                    PrebillBatchId = entry.PrebillBatchId,
                    PrebillLineId = entry.PrebillLineId,
                    TrustTransactionId = entry.TrustTransactionId,
                    CorrelationKey = $"{entry.CorrelationKey}:payor:{(slice.InvoicePayorAllocationId ?? slice.PayorClientId)}:{i + 1}",
                    Status = "posted",
                    Description = Truncate($"{entry.Description ?? entry.EntryType} [payor split]", 2048),
                    MetadataJson = BuildLedgerMetadataJson(new
                    {
                        distributionOfLedgerEntryId = entry.Id,
                        source = "payor_distribution",
                        payorClientId = slice.PayorClientId,
                        invoicePayorAllocationId = slice.InvoicePayorAllocationId,
                        slice.Basis,
                        slice.Weight
                    }),
                    PostedBy = userId ?? entry.PostedBy,
                    PostedAt = entry.PostedAt,
                    CreatedAt = DateTime.UtcNow
                });
            }

            if (companions.Count == 0)
            {
                return;
            }

            _context.BillingLedgerEntries.AddRange(companions);
            await _context.SaveChangesAsync(ct);
        }

        private bool ShouldCreateManualLedgerPayorDistribution(BillingLedgerEntry entry, ManualLedgerEntryRequest request)
        {
            if (request.DistributeByPayor == false)
            {
                return false;
            }

            if (!string.Equals(entry.LedgerDomain, "billing", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(entry.InvoiceId))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(entry.PayorClientId) || !string.IsNullOrWhiteSpace(entry.InvoicePayorAllocationId))
            {
                return false;
            }

            return entry.EntryType is "credit_memo" or "writeoff" or "adjustment";
        }

        private async Task<IReadOnlyList<PayorDistributionSlice>> BuildManualLedgerPayorDistributionSlicesAsync(BillingLedgerEntry entry, CancellationToken ct)
        {
            var invoiceId = entry.InvoiceId;
            if (string.IsNullOrWhiteSpace(invoiceId))
            {
                return [];
            }

            var amount = NormalizeMoney(entry.Amount);
            if (amount == 0m)
            {
                return [];
            }

            var sign = amount < 0m ? -1m : 1m;
            var magnitude = Math.Abs(amount);

            if (!string.IsNullOrWhiteSpace(entry.InvoiceLineItemId))
            {
                var lineAllocations = await _context.InvoiceLinePayorAllocations.AsNoTracking()
                    .Where(a => a.InvoiceId == invoiceId && a.InvoiceLineItemId == entry.InvoiceLineItemId && a.Status == "active")
                    .OrderBy(a => a.PayorClientId)
                    .ToListAsync(ct);

                if (lineAllocations.Count > 0)
                {
                    var lineSlices = AllocateAmountAcrossWeights(
                        magnitude,
                        lineAllocations.Select(a => new WeightedPayorTarget(
                            a.PayorClientId,
                            a.InvoicePayorAllocationId,
                            a.Amount > 0m ? a.Amount : (a.Percent ?? 0m),
                            a.Amount > 0m ? "line_amount" : "line_percent")).ToList());
                    return lineSlices.Select(s => s with { Amount = NormalizeMoney(s.Amount * sign) }).ToList();
                }
            }

            var invoiceAllocations = await _context.InvoicePayorAllocations.AsNoTracking()
                .Where(a => a.InvoiceId == invoiceId && a.Status == "active")
                .OrderBy(a => a.Priority)
                .ThenByDescending(a => a.IsPrimary)
                .ToListAsync(ct);

            if (invoiceAllocations.Count == 0)
            {
                return [];
            }

            var invoiceTargets = invoiceAllocations.Select(a =>
            {
                var weight = a.AllocatedAmount > 0m
                    ? a.AllocatedAmount
                    : (a.Percent.HasValue && a.Percent.Value > 0m ? a.Percent.Value : 0m);
                var basis = a.AllocatedAmount > 0m ? "invoice_allocated_amount" :
                    (a.Percent.HasValue && a.Percent.Value > 0m ? "invoice_percent" : "equal");
                return new WeightedPayorTarget(a.PayorClientId, a.Id, weight, basis);
            }).ToList();

            var slices = AllocateAmountAcrossWeights(magnitude, invoiceTargets);
            return slices.Select(s => s with { Amount = NormalizeMoney(s.Amount * sign) }).ToList();
        }

        private static IReadOnlyList<PayorDistributionSlice> AllocateAmountAcrossWeights(decimal magnitude, IReadOnlyList<WeightedPayorTarget> targets)
        {
            var normalizedMagnitude = NormalizeMoney(magnitude);
            if (normalizedMagnitude <= 0m || targets.Count == 0)
            {
                return [];
            }

            var positiveWeightTargets = targets.Where(t => t.Weight > 0m).ToList();
            var useEqual = positiveWeightTargets.Count == 0;
            var basisTargets = useEqual ? targets.ToList() : positiveWeightTargets;
            if (basisTargets.Count == 0)
            {
                return [];
            }

            var totalWeight = useEqual ? basisTargets.Count : basisTargets.Sum(t => t.Weight);
            var slices = new List<PayorDistributionSlice>(basisTargets.Count);
            decimal running = 0m;
            for (var i = 0; i < basisTargets.Count; i++)
            {
                var target = basisTargets[i];
                var amount = i == basisTargets.Count - 1
                    ? NormalizeMoney(normalizedMagnitude - running)
                    : NormalizeMoney(normalizedMagnitude * (useEqual ? 1m : target.Weight) / totalWeight);
                if (amount < 0m) amount = 0m;
                running = NormalizeMoney(running + amount);
                slices.Add(new PayorDistributionSlice(
                    target.PayorClientId,
                    target.InvoicePayorAllocationId,
                    amount,
                    useEqual ? "equal" : target.Basis,
                    useEqual ? 1m : target.Weight));
            }

            var diff = NormalizeMoney(normalizedMagnitude - slices.Sum(s => s.Amount));
            if (diff != 0m && slices.Count > 0)
            {
                var last = slices[^1];
                slices[^1] = last with { Amount = NormalizeMoney(last.Amount + diff) };
            }

            return slices.Where(s => s.Amount != 0m).ToList();
        }

        private string? ResolveManualLedgerPayorClientId(ManualLedgerEntryRequest request, Invoice? invoice, InvoicePayorAllocation? invoicePayorAllocation)
        {
            if (!string.IsNullOrWhiteSpace(request.PayorClientId))
            {
                return request.PayorClientId.Trim();
            }

            if (!string.IsNullOrWhiteSpace(invoicePayorAllocation?.PayorClientId))
            {
                return invoicePayorAllocation.PayorClientId;
            }

            return null;
        }

        private string? ResolveAllocationPayorClientId(
            ApplyPaymentAllocationRequest request,
            Invoice invoice,
            InvoicePayorAllocation? invoicePayorAllocation,
            PaymentTransaction? payment = null)
        {
            if (!string.IsNullOrWhiteSpace(request.PayorClientId))
            {
                return request.PayorClientId.Trim();
            }

            if (!string.IsNullOrWhiteSpace(invoicePayorAllocation?.PayorClientId))
            {
                return invoicePayorAllocation.PayorClientId;
            }

            if (!string.IsNullOrWhiteSpace(payment?.PayorClientId))
            {
                return payment.PayorClientId;
            }

            return string.IsNullOrWhiteSpace(invoice.ClientId) ? null : invoice.ClientId;
        }

        public async Task<TrustThreeWayReconciliationResult> GetTrustThreeWayReconciliationAsync(TrustReconciliationRequest request, CancellationToken ct = default)
        {
            var asOf = request.AsOfUtc ?? DateTime.UtcNow;
            var accountsQuery = _context.TrustBankAccounts.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(request.TrustAccountId))
            {
                accountsQuery = accountsQuery.Where(a => a.Id == request.TrustAccountId);
            }

            var accounts = await accountsQuery.OrderBy(a => a.Name).ToListAsync(ct);
            var accountIds = accounts.Select(a => a.Id).ToList();

            var clientLedgers = await _context.ClientTrustLedgers.AsNoTracking()
                .Where(l => accountIds.Contains(l.TrustAccountId))
                .ToListAsync(ct);

            var trustTransactions = await _context.TrustTransactions.AsNoTracking()
                .Where(t => accountIds.Contains(t.TrustAccountId) && !t.IsVoided && t.CreatedAt <= asOf)
                .ToListAsync(ct);

            var billingTrustLedgerEntries = await _context.BillingLedgerEntries.AsNoTracking()
                .Where(e => e.LedgerDomain == "trust" && e.PostedAt <= asOf)
                .ToListAsync(ct);

            var items = new List<TrustThreeWayReconciliationAccountItem>(accounts.Count);
            foreach (var account in accounts)
            {
                var bankBalance = NormalizeMoney((decimal)account.CurrentBalance);
                var clientLedgerTotal = NormalizeMoney(clientLedgers
                    .Where(l => l.TrustAccountId == account.Id && l.Status != LedgerStatus.CLOSED)
                    .Sum(l => (decimal)l.RunningBalance));

                var trustTransactionsNet = NormalizeMoney(trustTransactions
                    .Where(t => t.TrustAccountId == account.Id)
                    .Sum(t => SignedTrustTransactionAmount(t)));

                var billingTrustLedgerTotal = NormalizeMoney(billingTrustLedgerEntries
                    .Where(e => e.MetadataJson != null && e.MetadataJson.Contains(account.Id))
                    .Sum(e => e.Amount));

                items.Add(new TrustThreeWayReconciliationAccountItem
                {
                    TrustAccountId = account.Id,
                    TrustAccountName = account.Name,
                    BankBalance = bankBalance,
                    ClientLedgerTotal = clientLedgerTotal,
                    TrustTransactionsNet = trustTransactionsNet,
                    BillingTrustLedgerTotal = billingTrustLedgerTotal,
                    BankVsClientLedgerDiff = NormalizeMoney(bankBalance - clientLedgerTotal),
                    ClientLedgerVsTrustLedgerDiff = NormalizeMoney(clientLedgerTotal - billingTrustLedgerTotal),
                    BankVsTrustLedgerDiff = NormalizeMoney(bankBalance - billingTrustLedgerTotal)
                });
            }

            return new TrustThreeWayReconciliationResult
            {
                AsOfUtc = asOf,
                Accounts = items,
                Totals = new TrustThreeWayReconciliationTotals
                {
                    BankBalance = NormalizeMoney(items.Sum(i => i.BankBalance)),
                    ClientLedgerTotal = NormalizeMoney(items.Sum(i => i.ClientLedgerTotal)),
                    TrustTransactionsNet = NormalizeMoney(items.Sum(i => i.TrustTransactionsNet)),
                    BillingTrustLedgerTotal = NormalizeMoney(items.Sum(i => i.BillingTrustLedgerTotal)),
                    BankVsClientLedgerDiff = NormalizeMoney(items.Sum(i => i.BankVsClientLedgerDiff)),
                    ClientLedgerVsTrustLedgerDiff = NormalizeMoney(items.Sum(i => i.ClientLedgerVsTrustLedgerDiff)),
                    BankVsTrustLedgerDiff = NormalizeMoney(items.Sum(i => i.BankVsTrustLedgerDiff))
                }
            };
        }

        public async Task<LedesPreviewResult?> GenerateLedesPreviewAsync(string prebillId, CancellationToken ct = default)
        {
            var batch = await _context.BillingPrebillBatches.FirstOrDefaultAsync(b => b.Id == prebillId, ct);
            if (batch == null) return null;

            var policy = !string.IsNullOrWhiteSpace(batch.PolicyId)
                ? await _context.MatterBillingPolicies.AsNoTracking().FirstOrDefaultAsync(p => p.Id == batch.PolicyId, ct)
                : await GetActiveMatterPolicyAsync(batch.MatterId, DateTime.UtcNow, ct);

            var lines = await _context.BillingPrebillLines
                .Where(l => l.PrebillBatchId == prebillId && l.Status != "excluded")
                .OrderBy(l => l.ServiceDate)
                .ThenBy(l => l.CreatedAt)
                .ToListAsync(ct);

            var warnings = new List<string>();
            if (policy == null)
            {
                warnings.Add("Billing policy not found; LEDES preview is using fallback defaults.");
            }
            else
            {
                if (!string.Equals(policy.EbillingStatus, "enabled", StringComparison.OrdinalIgnoreCase))
                {
                    warnings.Add("E-billing is disabled in matter billing policy.");
                }

                if (policy.EnforceUtbmsCodes)
                {
                    var missing = lines.Count(l =>
                        (l.LineType == "time" && (string.IsNullOrWhiteSpace(l.TaskCode) || string.IsNullOrWhiteSpace(l.ActivityCode))) ||
                        (l.LineType == "expense" && string.IsNullOrWhiteSpace(l.ExpenseCode)));
                    if (missing > 0)
                    {
                        warnings.Add($"UTBMS/LEDES required codes missing for {missing} line(s).");
                    }
                }
            }

            var records = new List<LedesPreviewLineRecord>(lines.Count);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("INVOICE_NUMBER|CLIENT_ID|MATTER_ID|LINE_NO|LINE_TYPE|SERVICE_DATE|TASK_CODE|ACTIVITY_CODE|EXPENSE_CODE|QTY|RATE|LINE_AMOUNT|TAX_AMOUNT|THIRD_PARTY_PAYOR|DESCRIPTION");

            var lineNo = 1;
            foreach (var line in lines)
            {
                var record = new LedesPreviewLineRecord
                {
                    LineNo = lineNo,
                    LineId = line.Id,
                    LineType = line.LineType,
                    ServiceDate = line.ServiceDate,
                    TaskCode = line.TaskCode,
                    ActivityCode = line.ActivityCode,
                    ExpenseCode = line.ExpenseCode,
                    Quantity = NormalizeMoney(line.Quantity),
                    Rate = NormalizeMoney(line.Rate),
                    LineAmount = NormalizeMoney(line.ApprovedAmount),
                    TaxAmount = NormalizeMoney(line.TaxAmount),
                    ThirdPartyPayorClientId = line.ThirdPartyPayorClientId,
                    Description = line.Description
                };
                records.Add(record);

                sb.Append(Esc(batch.InvoiceId ?? $"PREBILL-{batch.Id}")).Append('|')
                  .Append(Esc(batch.ClientId)).Append('|')
                  .Append(Esc(batch.MatterId)).Append('|')
                  .Append(record.LineNo.ToString(CultureInfo.InvariantCulture)).Append('|')
                  .Append(Esc(record.LineType)).Append('|')
                  .Append(record.ServiceDate?.ToString("yyyy-MM-dd") ?? string.Empty).Append('|')
                  .Append(Esc(record.TaskCode)).Append('|')
                  .Append(Esc(record.ActivityCode)).Append('|')
                  .Append(Esc(record.ExpenseCode)).Append('|')
                  .Append(record.Quantity.ToString("0.00", CultureInfo.InvariantCulture)).Append('|')
                  .Append(record.Rate.ToString("0.00", CultureInfo.InvariantCulture)).Append('|')
                  .Append(record.LineAmount.ToString("0.00", CultureInfo.InvariantCulture)).Append('|')
                  .Append(record.TaxAmount.ToString("0.00", CultureInfo.InvariantCulture)).Append('|')
                  .Append(Esc(record.ThirdPartyPayorClientId)).Append('|')
                  .Append(Esc(record.Description))
                  .AppendLine();

                lineNo++;
            }

            return new LedesPreviewResult
            {
                PrebillId = batch.Id,
                Format = policy?.EbillingFormat ?? batch.LedesFormat ?? "none",
                Currency = batch.Currency,
                Warnings = warnings,
                Lines = records,
                PreviewText = sb.ToString()
            };
        }

        private async Task ApplyInvoiceHeaderAllocationIfNeededAsync(Invoice invoice, PaymentTransaction payment, decimal amount, CancellationToken ct)
        {
            if (!string.Equals(payment.InvoiceId, invoice.Id, StringComparison.Ordinal))
            {
                return;
            }

            var targetApplied = NormalizeMoney(Math.Min(payment.Amount, payment.InvoiceAppliedAmount + amount));
            var delta = NormalizeMoney(targetApplied - payment.InvoiceAppliedAmount);
            if (delta <= 0m)
            {
                return;
            }

            invoice.AmountPaid = NormalizeMoney(invoice.AmountPaid + delta);
            invoice.Balance = NormalizeMoney(invoice.Balance - delta);
            if (invoice.Balance < 0m) invoice.Balance = 0m;
            if (invoice.Balance == 0m)
            {
                invoice.Status = InvoiceStatus.Paid;
            }
            else if (invoice.AmountPaid > 0m && (invoice.Status == InvoiceStatus.Draft || invoice.Status == InvoiceStatus.Approved || invoice.Status == InvoiceStatus.Sent))
            {
                invoice.Status = InvoiceStatus.PartiallyPaid;
            }
            invoice.UpdatedAt = DateTime.UtcNow;

            payment.InvoiceAppliedAmount = targetApplied;
            payment.InvoiceAppliedAt ??= DateTime.UtcNow;
            payment.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(ct);
        }

        private async Task EnforceTrustFundingGuardrailAsync(ApplyPaymentAllocationRequest request, Invoice invoice, decimal amount, CancellationToken ct)
        {
            var targetMatterId = request.MatterId ?? invoice.MatterId;
            var policy = !string.IsNullOrWhiteSpace(targetMatterId)
                ? await GetActiveMatterPolicyAsync(targetMatterId!, DateTime.UtcNow, ct)
                : null;

            if (policy?.EnforceTrustOperatingSplit != true)
            {
                return;
            }

            var clientId = request.TrustSourceClientId ?? request.ClientId ?? invoice.ClientId;
            if (string.IsNullOrWhiteSpace(clientId))
            {
                throw new InvalidOperationException("ClientId is required for trust-funded allocation.");
            }

            var payorClientId = request.PayorClientId;
            if (!string.IsNullOrWhiteSpace(payorClientId) &&
                !string.Equals(payorClientId, clientId, StringComparison.Ordinal) &&
                string.IsNullOrWhiteSpace(request.TrustSourceClientId))
            {
                throw new InvalidOperationException("Trust-funded allocations for third-party payors require TrustSourceClientId to be specified explicitly.");
            }

            var trustLedgerBalance = await _context.ClientTrustLedgers
                .Where(l =>
                    l.ClientId == clientId &&
                    (string.IsNullOrWhiteSpace(targetMatterId) || l.MatterId == targetMatterId) &&
                    l.Status == LedgerStatus.ACTIVE &&
                    (string.IsNullOrWhiteSpace(request.TrustAccountId) || l.TrustAccountId == request.TrustAccountId))
                .SumAsync(l => (double?)l.RunningBalance, ct) ?? 0d;

            var available = NormalizeMoney((decimal)trustLedgerBalance);
            if (amount > available)
            {
                throw new InvalidOperationException("Trust-funded allocation exceeds available client trust balance (IOLTA guardrail).");
            }
        }

        private static decimal SignedTrustTransactionAmount(TrustTransaction tx)
        {
            var amount = NormalizeMoney((decimal)tx.Amount);
            var type = (tx.Type ?? string.Empty).Trim().ToLowerInvariant();
            return type switch
            {
                "deposit" => amount,
                "withdrawal" => -amount,
                "earnedfees" => -amount,
                "earned_fees" => -amount,
                "refundtoclient" => -amount,
                "refund_to_client" => -amount,
                "transfer" => 0m,
                _ => amount
            };
        }

        private static string Esc(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            return value.Replace("|", "/").Replace(Environment.NewLine, " ").Trim();
        }

        private async Task PostLedgerEntryIfAbsentAsync(BillingLedgerEntry draft, CancellationToken ct)
        {
            if (!string.IsNullOrWhiteSpace(draft.CorrelationKey))
            {
                var existing = await _context.BillingLedgerEntries
                    .FirstOrDefaultAsync(e => e.CorrelationKey == draft.CorrelationKey, ct);
                if (existing != null)
                {
                    return;
                }
            }

            draft.Amount = NormalizeMoney(draft.Amount);
            draft.Currency = NormalizeCurrency(draft.Currency, "USD");
            draft.Description = Truncate(draft.Description, 2048);
            _context.BillingLedgerEntries.Add(draft);
            await _context.SaveChangesAsync(ct);
        }

        private static string BuildLedgerMetadataJson(object value) => JsonSerializer.Serialize(value);
        private static string BuildPaymentAllocationMetadataJson(object value) => JsonSerializer.Serialize(value);

        private async Task TryRecordTrustRiskLedgerAsync(BillingLedgerEntry entry, string triggerType, CancellationToken ct)
        {
            try
            {
                await _trustRiskRadarService.RecordLedgerEntryRiskAsync(entry, triggerType, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Trust risk radar hook failed for ledger entry {LedgerEntryId} ({TriggerType}).", entry.Id, triggerType);
            }
        }

        private async Task TryRecordTrustRiskAllocationAsync(BillingPaymentAllocation allocation, string triggerType, CancellationToken ct)
        {
            try
            {
                await _trustRiskRadarService.RecordPaymentAllocationRiskAsync(allocation, triggerType, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Trust risk radar hook failed for payment allocation {AllocationId} ({TriggerType}).", allocation.Id, triggerType);
            }
        }

        private sealed record WeightedPayorTarget(string PayorClientId, string? InvoicePayorAllocationId, decimal Weight, string Basis);
        private sealed record PayorDistributionSlice(string PayorClientId, string? InvoicePayorAllocationId, decimal Amount, string Basis, decimal Weight);
    }
}
