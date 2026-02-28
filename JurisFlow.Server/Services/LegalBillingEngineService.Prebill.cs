using System.Text.Json;
using JurisFlow.Server.Enums;
using JurisFlow.Server.Models;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace JurisFlow.Server.Services
{
    public sealed partial class LegalBillingEngineService
    {
        public async Task<PrebillGenerationResult> GeneratePrebillAsync(PrebillGenerateRequest request, string? userId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(request.MatterId))
            {
                throw new InvalidOperationException("MatterId is required.");
            }

            var matter = await _context.Matters.AsNoTracking().FirstOrDefaultAsync(m => m.Id == request.MatterId, ct)
                ?? throw new InvalidOperationException("Matter not found.");
            var client = await _context.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == matter.ClientId, ct)
                ?? throw new InvalidOperationException("Client not found.");

            var periodStart = request.PeriodStart.Date;
            var periodEnd = request.PeriodEnd.Date;
            if (periodEnd < periodStart)
            {
                throw new InvalidOperationException("PeriodEnd must be greater than or equal to PeriodStart.");
            }

            var policy = await GetActiveMatterPolicyAsync(matter.Id, request.AsOfUtc ?? DateTime.UtcNow, ct)
                ?? throw new InvalidOperationException("Active billing policy not found for matter.");
            var rateCard = await ResolveRateCardAsync(policy, matter, ct);
            if (policy.ArrangementType is "hourly" or "hybrid" && rateCard == null)
            {
                throw new InvalidOperationException("Rate card is required for hourly/hybrid billing policy.");
            }

            var rateEntries = rateCard == null
                ? new List<BillingRateCardEntry>()
                : await _context.BillingRateCardEntries
                    .Where(e => e.RateCardId == rateCard.Id && e.Status == "active")
                    .OrderBy(e => e.Priority)
                    .ToListAsync(ct);

            var timeQuery = _context.TimeEntries
                .Where(t =>
                    t.MatterId == matter.Id &&
                    !t.Billed &&
                    t.IsBillable &&
                    t.Date.Date >= periodStart &&
                    t.Date.Date <= periodEnd);

            var expenseQuery = _context.Expenses
                .Where(e =>
                    e.MatterId == matter.Id &&
                    !e.Billed &&
                    e.Date.Date >= periodStart &&
                    e.Date.Date <= periodEnd);

            if (request.IncludeUnapproved != true)
            {
                timeQuery = timeQuery.Where(t => ApprovedStatuses.Contains(t.ApprovalStatus) || string.IsNullOrWhiteSpace(t.ApprovalStatus));
                expenseQuery = expenseQuery.Where(e => ApprovedStatuses.Contains(e.ApprovalStatus) || string.IsNullOrWhiteSpace(e.ApprovalStatus));
            }

            var timeEntries = await timeQuery.OrderBy(t => t.Date).ToListAsync(ct);
            var expenses = await expenseQuery.OrderBy(e => e.Date).ToListAsync(ct);

            var batch = new BillingPrebillBatch
            {
                MatterId = matter.Id,
                ClientId = matter.ClientId,
                PolicyId = policy.Id,
                RateCardId = rateCard?.Id,
                Currency = policy.Currency,
                ArrangementType = policy.ArrangementType,
                Status = "draft",
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                TaxPolicyCode = policy.TaxPolicyMode,
                LedesFormat = string.Equals(policy.EbillingStatus, "enabled", StringComparison.OrdinalIgnoreCase) ? policy.EbillingFormat : null,
                GeneratedBy = userId,
                GeneratedAt = DateTime.UtcNow,
                MetadataJson = JsonSerializer.Serialize(new
                {
                    source = "legal_billing_engine",
                    includeUnapproved = request.IncludeUnapproved ?? false,
                    rateCardId = rateCard?.Id,
                    policyId = policy.Id
                })
            };
            _context.BillingPrebillBatches.Add(batch);
            await _context.SaveChangesAsync(ct);

            var warnings = new List<string>();
            var taxRatePercent = GetDefaultTaxRatePercent(policy);
            var newLines = new List<BillingPrebillLine>(timeEntries.Count + expenses.Count);

            foreach (var time in timeEntries)
            {
                var quantity = NormalizeMoney((decimal)time.Duration / 60m);
                if (quantity <= 0m) continue;

                var rate = ResolveTimeRate(time, matter, policy, rateEntries);
                var proposed = NormalizeMoney(quantity * rate.Rate);
                var lineWarnings = new List<string>();
                if (policy.EnforceUtbmsCodes)
                {
                    if (string.IsNullOrWhiteSpace(time.TaskCode)) lineWarnings.Add("Missing UTBMS task code.");
                    if (string.IsNullOrWhiteSpace(time.ActivityCode)) lineWarnings.Add("Missing UTBMS activity code.");
                }

                newLines.Add(new BillingPrebillLine
                {
                    PrebillBatchId = batch.Id,
                    MatterId = matter.Id,
                    ClientId = matter.ClientId,
                    LineType = "time",
                    SourceType = nameof(TimeEntry),
                    SourceId = time.Id,
                    TimekeeperId = NullIfEmpty(time.ApprovedBy ?? time.SubmittedBy),
                    ServiceDate = time.Date.Date,
                    Description = Truncate(time.Description, 255) ?? "Time entry",
                    TaskCode = NullIfEmpty(time.TaskCode),
                    ActivityCode = NullIfEmpty(time.ActivityCode),
                    Quantity = quantity,
                    Rate = rate.Rate,
                    ProposedAmount = proposed,
                    ApprovedAmount = proposed,
                    TaxAmount = NormalizeMoney(proposed * (taxRatePercent / 100m)),
                    TaxCode = GetDefaultTaxCode(policy),
                    ThirdPartyPayorClientId = policy.ThirdPartyPayorClientId,
                    Status = lineWarnings.Count == 0 ? "reviewed" : "draft",
                    ReviewerNotes = lineWarnings.Count == 0 ? null : Truncate(string.Join(" ", lineWarnings), 2048),
                    MetadataJson = BuildPrebillLineMetadataJson(new
                    {
                        sourceDate = time.Date,
                        sourceDurationMinutes = time.Duration,
                        approvalStatus = time.ApprovalStatus,
                        rateSource = rate.Source,
                        rateCardEntryId = rate.RateCardEntryId
                    })
                });

                if (lineWarnings.Count > 0)
                {
                    warnings.Add($"TimeEntry {time.Id}: {string.Join(" ", lineWarnings)}");
                }
            }

            foreach (var expense in expenses)
            {
                var proposed = NormalizeMoney((decimal)expense.Amount);
                if (proposed <= 0m) continue;

                var rate = ResolveExpenseRate(expense, policy, rateEntries, proposed);
                var lineWarnings = new List<string>();
                if (policy.EnforceUtbmsCodes && string.IsNullOrWhiteSpace(expense.ExpenseCode))
                {
                    lineWarnings.Add("Missing UTBMS expense code.");
                }

                newLines.Add(new BillingPrebillLine
                {
                    PrebillBatchId = batch.Id,
                    MatterId = matter.Id,
                    ClientId = matter.ClientId,
                    LineType = "expense",
                    SourceType = nameof(Expense),
                    SourceId = expense.Id,
                    ServiceDate = expense.Date.Date,
                    Description = Truncate(expense.Description, 255) ?? "Expense",
                    ExpenseCode = NullIfEmpty(expense.ExpenseCode),
                    Quantity = 1m,
                    Rate = rate.Rate,
                    ProposedAmount = proposed,
                    ApprovedAmount = rate.Rate,
                    TaxAmount = NormalizeMoney(rate.Rate * (taxRatePercent / 100m)),
                    TaxCode = GetDefaultTaxCode(policy),
                    ThirdPartyPayorClientId = policy.ThirdPartyPayorClientId,
                    Status = lineWarnings.Count == 0 ? "reviewed" : "draft",
                    ReviewerNotes = lineWarnings.Count == 0 ? null : Truncate(string.Join(" ", lineWarnings), 2048),
                    MetadataJson = BuildPrebillLineMetadataJson(new
                    {
                        sourceDate = expense.Date,
                        category = expense.Category,
                        approvalStatus = expense.ApprovalStatus,
                        rateSource = rate.Source,
                        rateCardEntryId = rate.RateCardEntryId
                    })
                });

                if (lineWarnings.Count > 0)
                {
                    warnings.Add($"Expense {expense.Id}: {string.Join(" ", lineWarnings)}");
                }
            }

            if (newLines.Count == 0)
            {
                batch.ReviewNotes = Truncate("No billable unbilled items found for selected period.", 2048);
                batch.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync(ct);
                return new PrebillGenerationResult { Batch = batch, Lines = [], Warnings = [] };
            }

            _context.BillingPrebillLines.AddRange(newLines);
            await _context.SaveChangesAsync(ct);
            await RecalculatePrebillTotalsAsync(batch.Id, ct);

            if (warnings.Count > 0)
            {
                var trackedBatch = await _context.BillingPrebillBatches.FirstAsync(b => b.Id == batch.Id, ct);
                trackedBatch.ReviewNotes = Truncate(string.Join(Environment.NewLine, warnings), 2048);
                trackedBatch.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync(ct);
            }

            var reviewItemId = await QueuePrebillReviewIfNeededAsync(batch.Id, warnings, matter, client, policy, ct);
            var lines = await _context.BillingPrebillLines
                .Where(l => l.PrebillBatchId == batch.Id)
                .OrderBy(l => l.ServiceDate)
                .ThenBy(l => l.CreatedAt)
                .ToListAsync(ct);

            return new PrebillGenerationResult
            {
                Batch = await _context.BillingPrebillBatches.FirstAsync(b => b.Id == batch.Id, ct),
                Lines = lines,
                ReviewItemId = reviewItemId,
                Warnings = warnings
            };
        }

        public async Task<IReadOnlyList<BillingPrebillBatch>> ListPrebillsAsync(BillingPrebillBatchQuery query, CancellationToken ct = default)
        {
            var q = _context.BillingPrebillBatches.AsQueryable();
            if (!string.IsNullOrWhiteSpace(query.MatterId)) q = q.Where(b => b.MatterId == query.MatterId);
            if (!string.IsNullOrWhiteSpace(query.ClientId)) q = q.Where(b => b.ClientId == query.ClientId);
            if (!string.IsNullOrWhiteSpace(query.Status)) q = q.Where(b => b.Status == query.Status);
            if (query.PeriodStart != null) q = q.Where(b => b.PeriodEnd >= query.PeriodStart.Value.Date);
            if (query.PeriodEnd != null) q = q.Where(b => b.PeriodStart <= query.PeriodEnd.Value.Date);

            return await q.OrderByDescending(b => b.GeneratedAt)
                .Take(Math.Clamp(query.Limit ?? 200, 1, 500))
                .ToListAsync(ct);
        }

        public async Task<PrebillDetailResult?> GetPrebillAsync(string prebillId, CancellationToken ct = default)
        {
            var batch = await _context.BillingPrebillBatches.FirstOrDefaultAsync(b => b.Id == prebillId, ct);
            if (batch == null) return null;

            var lines = await _context.BillingPrebillLines
                .Where(l => l.PrebillBatchId == prebillId)
                .OrderBy(l => l.ServiceDate)
                .ThenBy(l => l.CreatedAt)
                .ToListAsync(ct);

            var ledgerEntries = await _context.BillingLedgerEntries
                .Where(e => e.PrebillBatchId == prebillId)
                .OrderByDescending(e => e.PostedAt)
                .ToListAsync(ct);

            return new PrebillDetailResult
            {
                Batch = batch,
                Lines = lines,
                LedgerEntries = ledgerEntries
            };
        }

        public async Task<BillingPrebillLine?> AdjustPrebillLineAsync(string prebillLineId, PrebillLineAdjustmentRequest request, CancellationToken ct = default)
        {
            var line = await _context.BillingPrebillLines.FirstOrDefaultAsync(l => l.Id == prebillLineId, ct);
            if (line == null) return null;

            var batch = await _context.BillingPrebillBatches.FirstAsync(b => b.Id == line.PrebillBatchId, ct);
            EnsurePrebillMutable(batch);

            if (request.Exclude == true)
            {
                line.Status = "excluded";
                line.ApprovedAmount = 0m;
                line.TaxAmount = 0m;
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(request.Status))
                {
                    line.Status = NormalizeEnum(request.Status, line.Status, ["draft", "reviewed", "approved", "excluded"]);
                }

                if (request.Quantity.HasValue) line.Quantity = NormalizeMoney(request.Quantity.Value);
                if (request.Rate.HasValue) line.Rate = NormalizeMoney(request.Rate.Value);
                if (request.ProposedAmount.HasValue) line.ProposedAmount = NormalizeMoney(request.ProposedAmount.Value);
                if (request.DiscountAmount.HasValue) line.DiscountAmount = NormalizeMoney(request.DiscountAmount.Value);
                if (request.WriteDownAmount.HasValue) line.WriteDownAmount = NormalizeMoney(request.WriteDownAmount.Value);
                if (request.TaxAmount.HasValue) line.TaxAmount = NormalizeMoney(request.TaxAmount.Value);
                if (request.ApprovedAmount.HasValue) line.ApprovedAmount = NormalizeMoney(request.ApprovedAmount.Value);

                if (request.RecomputeApprovedFromProposed == true)
                {
                    line.ApprovedAmount = NormalizeMoney(Math.Max(0m, line.ProposedAmount - line.DiscountAmount - line.WriteDownAmount));
                }

                if (request.TaskCode != null) line.TaskCode = NullIfEmpty(request.TaskCode);
                if (request.ActivityCode != null) line.ActivityCode = NullIfEmpty(request.ActivityCode);
                if (request.ExpenseCode != null) line.ExpenseCode = NullIfEmpty(request.ExpenseCode);
                if (request.TaxCode != null) line.TaxCode = NullIfEmpty(request.TaxCode);
                if (request.Description != null) line.Description = Truncate(request.Description, 255) ?? line.Description;
                if (request.ThirdPartyPayorClientId != null) line.ThirdPartyPayorClientId = NullIfEmpty(request.ThirdPartyPayorClientId);
                if (request.SplitAllocations != null)
                {
                    line.SplitAllocationJson = SerializeValidatedSplitAllocationJson(request.SplitAllocations, line.ClientId, line.ThirdPartyPayorClientId);
                }
                else if (request.SplitAllocationJson != null)
                {
                    line.SplitAllocationJson = TruncateJson(request.SplitAllocationJson);
                }
            }

            if (request.ReviewerNotes != null)
            {
                line.ReviewerNotes = Truncate(request.ReviewerNotes, 2048);
            }

            line.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(ct);
            await RecalculatePrebillTotalsAsync(batch.Id, ct);
            return line;
        }

        public async Task<BillingPrebillBatch?> SubmitPrebillForReviewAsync(string prebillId, string? userId, string? notes, CancellationToken ct = default)
        {
            var batch = await _context.BillingPrebillBatches.FirstOrDefaultAsync(b => b.Id == prebillId, ct);
            if (batch == null) return null;
            EnsurePrebillMutable(batch);

            batch.Status = "in_review";
            batch.SubmittedBy = userId;
            batch.SubmittedAt = DateTime.UtcNow;
            if (!string.IsNullOrWhiteSpace(notes)) batch.ReviewNotes = Truncate(notes, 2048);
            batch.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(ct);
            return batch;
        }

        public async Task<BillingPrebillBatch?> ApprovePrebillAsync(string prebillId, string? userId, string? notes, CancellationToken ct = default)
        {
            var batch = await _context.BillingPrebillBatches.FirstOrDefaultAsync(b => b.Id == prebillId, ct);
            if (batch == null) return null;
            if (batch.Status == "approved") return batch;
            if (batch.Status != "draft" && batch.Status != "in_review")
            {
                throw new InvalidOperationException("Only draft or in-review prebills can be approved.");
            }

            await EnsureNotLockedAsync(DateTime.UtcNow, ct, "prebill_finalize");
            await RecalculatePrebillTotalsAsync(batch.Id, ct);

            var lines = await _context.BillingPrebillLines.Where(l => l.PrebillBatchId == batch.Id).ToListAsync(ct);
            if (lines.Count == 0)
            {
                throw new InvalidOperationException("Prebill has no lines.");
            }

            batch.Status = "approved";
            batch.ApprovedBy = userId;
            batch.ApprovedAt = DateTime.UtcNow;
            if (!string.IsNullOrWhiteSpace(notes)) batch.ReviewNotes = Truncate(notes, 2048);
            batch.UpdatedAt = DateTime.UtcNow;

            foreach (var line in lines.Where(l => l.Status != "excluded"))
            {
                if (line.Status is "draft" or "reviewed")
                {
                    line.Status = "approved";
                }
                line.UpdatedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync(ct);
            await PostPrebillApprovalLedgerEntriesAsync(batch, userId, ct);
            return batch;
        }

        public async Task<BillingPrebillBatch?> RejectPrebillAsync(string prebillId, string? userId, string? notes, CancellationToken ct = default)
        {
            var batch = await _context.BillingPrebillBatches.FirstOrDefaultAsync(b => b.Id == prebillId, ct);
            if (batch == null) return null;
            if (batch.Status == "finalized")
            {
                throw new InvalidOperationException("Finalized prebill cannot be rejected.");
            }

            batch.Status = "rejected";
            batch.RejectedBy = userId;
            batch.RejectedAt = DateTime.UtcNow;
            if (!string.IsNullOrWhiteSpace(notes)) batch.ReviewNotes = Truncate(notes, 2048);
            batch.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(ct);
            return batch;
        }

        public async Task<FinalizePrebillResult?> FinalizePrebillToInvoiceAsync(string prebillId, FinalizePrebillRequest request, string? userId, CancellationToken ct = default)
        {
            var batch = await _context.BillingPrebillBatches.FirstOrDefaultAsync(b => b.Id == prebillId, ct);
            if (batch == null) return null;

            if (batch.Status == "finalized" && !string.IsNullOrWhiteSpace(batch.InvoiceId))
            {
                var existingInvoice = await _context.Invoices.Include(i => i.LineItems).FirstOrDefaultAsync(i => i.Id == batch.InvoiceId, ct);
                return existingInvoice == null ? null : new FinalizePrebillResult { Batch = batch, Invoice = existingInvoice };
            }
            if (batch.Status != "approved")
            {
                throw new InvalidOperationException("Only approved prebills can be finalized.");
            }

            await EnsureNotLockedAsync(DateTime.UtcNow, ct, "prebill_approve");
            await RecalculatePrebillTotalsAsync(batch.Id, ct);

            var lines = await _context.BillingPrebillLines
                .Where(l => l.PrebillBatchId == batch.Id && l.Status != "excluded" && l.ApprovedAmount > 0m)
                .OrderBy(l => l.ServiceDate)
                .ToListAsync(ct);
            if (lines.Count == 0)
            {
                throw new InvalidOperationException("No approved prebill lines available.");
            }

            await using var tx = await _context.Database.BeginTransactionAsync(ct);

            var invoice = new Invoice
            {
                Number = string.IsNullOrWhiteSpace(request.InvoiceNumber)
                    ? $"INV-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}"
                    : request.InvoiceNumber.Trim(),
                ClientId = batch.ClientId,
                MatterId = batch.MatterId,
                Status = request.MarkAsSent == true ? InvoiceStatus.Sent : InvoiceStatus.Approved,
                IssueDate = (request.IssueDate ?? DateTime.UtcNow).Date,
                DueDate = request.DueDate?.Date,
                Subtotal = NormalizeMoney(lines.Sum(l => l.ApprovedAmount)),
                Tax = NormalizeMoney(lines.Sum(l => l.TaxAmount)),
                Discount = 0m,
                Total = 0m,
                AmountPaid = 0m,
                Balance = 0m,
                Notes = BuildInvoiceNotesFromPrebill(batch, request),
                Terms = Truncate(request.Terms, 500),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            foreach (var line in lines)
            {
                invoice.LineItems.Add(new InvoiceLineItem
                {
                    Type = NormalizeInvoiceLineType(line.LineType),
                    TaskCode = line.TaskCode,
                    ActivityCode = line.ActivityCode,
                    ExpenseCode = line.ExpenseCode,
                    Description = Truncate(line.Description, 255) ?? "Prebill line",
                    Quantity = line.Quantity <= 0m ? 1m : NormalizeMoney(line.Quantity),
                    Rate = NormalizeMoney(line.Rate),
                    Amount = NormalizeMoney(line.ApprovedAmount),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            }

            invoice.Total = NormalizeMoney(invoice.Subtotal + invoice.Tax - invoice.Discount);
            invoice.Balance = invoice.Total;
            _context.Invoices.Add(invoice);
            await _context.SaveChangesAsync(ct);

            var policy = !string.IsNullOrWhiteSpace(batch.PolicyId)
                ? await _context.MatterBillingPolicies.AsNoTracking().FirstOrDefaultAsync(p => p.Id == batch.PolicyId, ct)
                : await GetActiveMatterPolicyAsync(batch.MatterId, DateTime.UtcNow, ct);
            await CreateInvoicePayorAllocationsFromPrebillAsync(batch, invoice, lines, policy, ct);

            batch.InvoiceId = invoice.Id;
            batch.Status = "finalized";
            batch.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(ct);

            var timeIds = lines.Where(l => l.SourceType == nameof(TimeEntry) && !string.IsNullOrWhiteSpace(l.SourceId)).Select(l => l.SourceId!).Distinct().ToList();
            if (timeIds.Count > 0)
            {
                var times = await _context.TimeEntries.Where(t => timeIds.Contains(t.Id)).ToListAsync(ct);
                foreach (var t in times)
                {
                    t.Billed = true;
                    t.UpdatedAt = DateTime.UtcNow;
                }
            }

            var expenseIds = lines.Where(l => l.SourceType == nameof(Expense) && !string.IsNullOrWhiteSpace(l.SourceId)).Select(l => l.SourceId!).Distinct().ToList();
            if (expenseIds.Count > 0)
            {
                var exps = await _context.Expenses.Where(e => expenseIds.Contains(e.Id)).ToListAsync(ct);
                foreach (var e in exps)
                {
                    e.Billed = true;
                    e.UpdatedAt = DateTime.UtcNow;
                }
            }

            await _context.SaveChangesAsync(ct);

            await PostLedgerEntryIfAbsentAsync(new BillingLedgerEntry
            {
                LedgerDomain = "billing",
                LedgerBucket = "accounts_receivable",
                EntryType = "invoice_issued",
                Currency = batch.Currency,
                Amount = NormalizeMoney(-invoice.Total),
                MatterId = batch.MatterId,
                ClientId = batch.ClientId,
                InvoiceId = invoice.Id,
                PrebillBatchId = batch.Id,
                CorrelationKey = $"invoice:{invoice.Id}:issued:ar_offset",
                Description = $"Invoice {invoice.Number} issued from prebill {batch.Id}.",
                PostedBy = userId,
                PostedAt = DateTime.UtcNow,
                MetadataJson = BuildLedgerMetadataJson(new { source = "prebill_finalize", prebillId = batch.Id })
            }, ct);

            await tx.CommitAsync(ct);

            return new FinalizePrebillResult
            {
                Batch = batch,
                Invoice = await _context.Invoices.Include(i => i.LineItems).FirstAsync(i => i.Id == invoice.Id, ct)
            };
        }

        private async Task RecalculatePrebillTotalsAsync(string prebillBatchId, CancellationToken ct)
        {
            var batch = await _context.BillingPrebillBatches.FirstAsync(b => b.Id == prebillBatchId, ct);
            var lines = await _context.BillingPrebillLines
                .Where(l => l.PrebillBatchId == prebillBatchId && l.Status != "excluded")
                .ToListAsync(ct);

            batch.Subtotal = NormalizeMoney(lines.Sum(l => l.ApprovedAmount));
            batch.TaxTotal = NormalizeMoney(lines.Sum(l => l.TaxAmount));
            batch.DiscountTotal = NormalizeMoney(lines.Sum(l => l.DiscountAmount));
            batch.WriteDownTotal = NormalizeMoney(lines.Sum(l => l.WriteDownAmount));
            batch.Total = NormalizeMoney(batch.Subtotal + batch.TaxTotal);
            batch.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(ct);
        }

        private async Task PostPrebillApprovalLedgerEntriesAsync(BillingPrebillBatch batch, string? userId, CancellationToken ct)
        {
            await RecalculatePrebillTotalsAsync(batch.Id, ct);
            batch = await _context.BillingPrebillBatches.FirstAsync(b => b.Id == batch.Id, ct);

            var totalReceivable = NormalizeMoney(batch.Total);
            var revenue = NormalizeMoney(batch.Subtotal);
            var tax = NormalizeMoney(batch.TaxTotal);
            var writeoffs = NormalizeMoney(batch.DiscountTotal + batch.WriteDownTotal);

            await PostLedgerEntryIfAbsentAsync(new BillingLedgerEntry
            {
                LedgerDomain = "billing",
                LedgerBucket = "accounts_receivable",
                EntryType = "prebill_approved",
                Currency = batch.Currency,
                Amount = totalReceivable,
                MatterId = batch.MatterId,
                ClientId = batch.ClientId,
                PrebillBatchId = batch.Id,
                CorrelationKey = $"prebill:{batch.Id}:approve:ar",
                Description = $"Prebill {batch.Id} posted to A/R.",
                PostedBy = userId,
                PostedAt = batch.ApprovedAt ?? DateTime.UtcNow,
                MetadataJson = BuildLedgerMetadataJson(new { prebillBatchId = batch.Id })
            }, ct);

            if (revenue != 0m)
            {
                await PostLedgerEntryIfAbsentAsync(new BillingLedgerEntry
                {
                    LedgerDomain = "billing",
                    LedgerBucket = "revenue",
                    EntryType = "prebill_approved",
                    Currency = batch.Currency,
                    Amount = NormalizeMoney(-revenue),
                    MatterId = batch.MatterId,
                    ClientId = batch.ClientId,
                    PrebillBatchId = batch.Id,
                    CorrelationKey = $"prebill:{batch.Id}:approve:revenue",
                    Description = $"Revenue recognized for prebill {batch.Id}.",
                    PostedBy = userId,
                    PostedAt = batch.ApprovedAt ?? DateTime.UtcNow,
                    MetadataJson = BuildLedgerMetadataJson(new { prebillBatchId = batch.Id })
                }, ct);
            }

            if (tax != 0m)
            {
                await PostLedgerEntryIfAbsentAsync(new BillingLedgerEntry
                {
                    LedgerDomain = "billing",
                    LedgerBucket = "tax_liability",
                    EntryType = "prebill_approved",
                    Currency = batch.Currency,
                    Amount = NormalizeMoney(-tax),
                    MatterId = batch.MatterId,
                    ClientId = batch.ClientId,
                    PrebillBatchId = batch.Id,
                    CorrelationKey = $"prebill:{batch.Id}:approve:tax",
                    Description = $"Tax liability for prebill {batch.Id}.",
                    PostedBy = userId,
                    PostedAt = batch.ApprovedAt ?? DateTime.UtcNow,
                    MetadataJson = BuildLedgerMetadataJson(new { prebillBatchId = batch.Id })
                }, ct);
            }

            if (writeoffs != 0m)
            {
                await PostLedgerEntryIfAbsentAsync(new BillingLedgerEntry
                {
                    LedgerDomain = "billing",
                    LedgerBucket = "writeoff",
                    EntryType = "prebill_approved",
                    Currency = batch.Currency,
                    Amount = NormalizeMoney(-writeoffs),
                    MatterId = batch.MatterId,
                    ClientId = batch.ClientId,
                    PrebillBatchId = batch.Id,
                    CorrelationKey = $"prebill:{batch.Id}:approve:writeoff",
                    Description = $"Discount/write-down total for prebill {batch.Id}.",
                    PostedBy = userId,
                    PostedAt = batch.ApprovedAt ?? DateTime.UtcNow,
                    MetadataJson = BuildLedgerMetadataJson(new { prebillBatchId = batch.Id })
                }, ct);
            }
        }

        private static void EnsurePrebillMutable(BillingPrebillBatch batch)
        {
            if (batch.Status == "approved" || batch.Status == "finalized")
            {
                throw new InvalidOperationException("Approved/finalized prebills are immutable. Use adjustment/reversal entries.");
            }
        }

        private static string BuildInvoiceNotesFromPrebill(BillingPrebillBatch batch, FinalizePrebillRequest request)
        {
            var parts = new List<string> { $"Generated from prebill {batch.Id}" };
            if (!string.IsNullOrWhiteSpace(batch.ReviewNotes)) parts.Add($"Prebill review notes: {batch.ReviewNotes}");
            if (!string.IsNullOrWhiteSpace(request.Notes)) parts.Add(request.Notes.Trim());
            return Truncate(string.Join(" | ", parts), 500) ?? $"Generated from prebill {batch.Id}";
        }

        private static string BuildPrebillLineMetadataJson(object value) => JsonSerializer.Serialize(value);

        private string? SerializeValidatedSplitAllocationJson(
            IReadOnlyList<SplitAllocationComponentRequest> components,
            string primaryClientId,
            string? defaultThirdPartyPayorClientId)
        {
            if (components == null || components.Count == 0)
            {
                return null;
            }

            var normalized = new List<object>(components.Count);
            var hasPercent = false;
            var hasAmount = false;
            decimal percentTotal = 0m;

            for (var i = 0; i < components.Count; i++)
            {
                var c = components[i];
                var payorClientId = NullIfEmpty(c.PayorClientId)
                    ?? throw new InvalidOperationException($"Split allocation row {i + 1}: PayorClientId is required.");
                var responsibility = NormalizeSplitResponsibilityType(c.ResponsibilityType ??
                    (string.Equals(payorClientId, defaultThirdPartyPayorClientId, StringComparison.Ordinal) ? "third_party" : "primary"));

                decimal? percent = c.Percent.HasValue ? NormalizeMoney(c.Percent.Value) : null;
                decimal? amountCap = c.AmountCap.HasValue ? NormalizeMoney(c.AmountCap.Value) : null;
                if (percent.HasValue && percent.Value < 0m) throw new InvalidOperationException($"Split allocation row {i + 1}: Percent cannot be negative.");
                if (amountCap.HasValue && amountCap.Value < 0m) throw new InvalidOperationException($"Split allocation row {i + 1}: AmountCap cannot be negative.");

                if (percent.HasValue && percent.Value > 0m) hasPercent = true;
                if (amountCap.HasValue && amountCap.Value > 0m) hasAmount = true;
                if (percent.HasValue) percentTotal = NormalizeMoney(percentTotal + percent.Value);

                var isPrimary = c.IsPrimary ?? string.Equals(payorClientId, primaryClientId, StringComparison.Ordinal);
                var priority = c.Priority ?? ((i + 1) * 100);

                normalized.Add(new
                {
                    payorClientId,
                    responsibilityType = responsibility,
                    percent,
                    amountCap,
                    priority,
                    isPrimary,
                    status = NormalizeSplitStatus(c.Status),
                    terms = Truncate(c.Terms, 500),
                    reference = Truncate(c.Reference, 255),
                    purchaseOrder = Truncate(c.PurchaseOrder, 255),
                    ebillingProfileJson = TruncateJson(c.EbillingProfileJson),
                    metadataJson = TruncateJson(c.MetadataJson)
                });
            }

            if (hasPercent && hasAmount)
            {
                throw new InvalidOperationException("Split allocation cannot mix percent and amount-cap rows in the same definition.");
            }

            if (hasPercent && percentTotal > 100.01m)
            {
                throw new InvalidOperationException("Split allocation percent total cannot exceed 100%.");
            }

            return TruncateJson(JsonSerializer.Serialize(new { allocations = normalized }));
        }

        private async Task CreateInvoicePayorAllocationsFromPrebillAsync(
            BillingPrebillBatch batch,
            Invoice invoice,
            IReadOnlyList<BillingPrebillLine> prebillLines,
            MatterBillingPolicy? policy,
            CancellationToken ct)
        {
            if (invoice.LineItems.Count != prebillLines.Count)
            {
                throw new InvalidOperationException("Invoice line items and prebill lines are out of sync.");
            }

            var policySpecs = ParseSplitAllocationSpecs(policy?.SplitBillingJson, batch.ClientId, policy?.ThirdPartyPayorClientId);
            var lineAllocations = new List<PendingInvoiceLinePayorAllocation>(Math.Max(prebillLines.Count, 1));
            var invoiceAllocationAggregates = new Dictionary<InvoicePayorAllocationAggregateKey, InvoicePayorAllocationAggregate>();

            for (var i = 0; i < prebillLines.Count; i++)
            {
                var prebillLine = prebillLines[i];
                var invoiceLine = invoice.LineItems.ElementAt(i);
                var resolvedAllocations = ResolveLineSplitAllocations(prebillLine, invoiceLine, batch.ClientId, policy?.ThirdPartyPayorClientId, policySpecs);

                foreach (var resolved in resolvedAllocations)
                {
                    var lineAllocation = new InvoiceLinePayorAllocation
                    {
                        InvoiceId = invoice.Id,
                        InvoiceLineItemId = invoiceLine.Id,
                        PayorClientId = resolved.Spec.PayorClientId,
                        ResponsibilityType = resolved.Spec.ResponsibilityType,
                        Percent = resolved.Spec.Percent,
                        Amount = NormalizeMoney(resolved.Amount),
                        Status = resolved.Spec.Status,
                        TaskCode = invoiceLine.TaskCode,
                        ActivityCode = invoiceLine.ActivityCode,
                        ExpenseCode = invoiceLine.ExpenseCode,
                        EbillingProfileJson = resolved.Spec.EbillingProfileJson,
                        MetadataJson = resolved.Spec.MetadataJson,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    var key = InvoicePayorAllocationAggregateKey.From(resolved.Spec);
                    lineAllocations.Add(new PendingInvoiceLinePayorAllocation(lineAllocation, key));
                    if (!invoiceAllocationAggregates.TryGetValue(key, out var aggregate))
                    {
                        aggregate = new InvoicePayorAllocationAggregate(resolved.Spec);
                        invoiceAllocationAggregates[key] = aggregate;
                    }
                    aggregate.AllocatedAmount = NormalizeMoney(aggregate.AllocatedAmount + lineAllocation.Amount);
                    aggregate.LineCount++;
                    if (resolved.Spec.Percent.HasValue)
                    {
                        aggregate.Percent = resolved.Spec.Percent;
                    }
                    if (resolved.Spec.AmountCap.HasValue)
                    {
                        aggregate.AmountCap = resolved.Spec.AmountCap;
                    }
                }
            }

            if (invoiceAllocationAggregates.Count == 0)
            {
                throw new InvalidOperationException("No invoice payor allocations could be derived from prebill.");
            }

            var invoicePayorAllocations = invoiceAllocationAggregates.Values
                .OrderBy(a => a.Spec.Priority)
                .ThenByDescending(a => a.Spec.IsPrimary)
                .ThenBy(a => a.Spec.PayorClientId, StringComparer.Ordinal)
                .Select(a => new InvoicePayorAllocation
                {
                    InvoiceId = invoice.Id,
                    PayorClientId = a.Spec.PayorClientId,
                    ResponsibilityType = a.Spec.ResponsibilityType,
                    Percent = a.Percent,
                    AmountCap = a.AmountCap,
                    Priority = a.Spec.Priority,
                    Status = a.Spec.Status,
                    IsPrimary = a.Spec.IsPrimary,
                    AllocatedAmount = NormalizeMoney(a.AllocatedAmount),
                    Terms = a.Spec.Terms,
                    Reference = a.Spec.Reference,
                    PurchaseOrder = a.Spec.PurchaseOrder,
                    EbillingProfileJson = a.Spec.EbillingProfileJson,
                    MetadataJson = a.Spec.MetadataJson,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                })
                .ToList();

            _context.InvoicePayorAllocations.AddRange(invoicePayorAllocations);
            await _context.SaveChangesAsync(ct);

            var payorAllocationIds = invoicePayorAllocations.ToDictionary(
                a => InvoicePayorAllocationAggregateKey.From(a),
                a => a.Id);

            foreach (var pendingLineAllocation in lineAllocations)
            {
                if (payorAllocationIds.TryGetValue(pendingLineAllocation.Key, out var invoicePayorAllocationId))
                {
                    pendingLineAllocation.Entity.InvoicePayorAllocationId = invoicePayorAllocationId;
                }
            }

            _context.InvoiceLinePayorAllocations.AddRange(lineAllocations.Select(x => x.Entity));

            var primaryPayor = invoicePayorAllocations
                .OrderByDescending(a => a.IsPrimary)
                .ThenBy(a => a.Priority)
                .ThenByDescending(a => a.AllocatedAmount)
                .Select(a => a.PayorClientId)
                .FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(primaryPayor) &&
                !string.Equals(invoice.ClientId, primaryPayor, StringComparison.Ordinal))
            {
                invoice.ClientId = primaryPayor;
                invoice.UpdatedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync(ct);
        }

        private IReadOnlyList<ResolvedLineSplitAllocation> ResolveLineSplitAllocations(
            BillingPrebillLine prebillLine,
            InvoiceLineItem invoiceLine,
            string primaryClientId,
            string? defaultThirdPartyPayorClientId,
            IReadOnlyList<SplitAllocationSpec> policySpecs)
        {
            var lineAmount = NormalizeMoney(invoiceLine.Amount);
            if (lineAmount <= 0m)
            {
                return [new ResolvedLineSplitAllocation(DefaultPrimarySplitSpec(primaryClientId), 0m)];
            }

            var lineSpecs = ParseSplitAllocationSpecs(prebillLine.SplitAllocationJson, primaryClientId, prebillLine.ThirdPartyPayorClientId ?? defaultThirdPartyPayorClientId);
            var specs = lineSpecs.Count > 0 ? lineSpecs : policySpecs;

            if (specs.Count == 0)
            {
                specs = [DefaultPrimarySplitSpec(primaryClientId)];
            }

            var normalizedSpecs = specs
                .Select((s, idx) => s with { Priority = s.Priority == 0 ? (idx + 1) * 100 : s.Priority })
                .ToList();

            var fixedSpecs = normalizedSpecs.Where(s => s.AmountCap.HasValue && s.AmountCap.Value > 0m).ToList();
            var percentSpecs = normalizedSpecs.Where(s => s.Percent.HasValue && s.Percent.Value > 0m).ToList();

            if (fixedSpecs.Count > 0 && percentSpecs.Count > 0)
            {
                throw new InvalidOperationException($"Mixed split_amount and split_percent definitions are not supported yet for prebill line {prebillLine.Id}.");
            }

            if (percentSpecs.Count > 0)
            {
                return ResolvePercentLineSplits(normalizedSpecs, lineAmount, prebillLine.Id);
            }

            if (fixedSpecs.Count > 0)
            {
                return ResolveFixedAmountLineSplits(normalizedSpecs, lineAmount, primaryClientId);
            }

            if (normalizedSpecs.Count == 1)
            {
                return [new ResolvedLineSplitAllocation(normalizedSpecs[0], lineAmount)];
            }

            var explicitPrimary = normalizedSpecs.FirstOrDefault(s => s.IsPrimary) ?? normalizedSpecs.FirstOrDefault(s => string.Equals(s.PayorClientId, primaryClientId, StringComparison.Ordinal));
            if (explicitPrimary != null)
            {
                return [new ResolvedLineSplitAllocation(explicitPrimary, lineAmount)];
            }

            throw new InvalidOperationException($"Split allocation for prebill line {prebillLine.Id} is ambiguous. Provide percent/amount values or mark a primary payor.");
        }

        private IReadOnlyList<ResolvedLineSplitAllocation> ResolvePercentLineSplits(IReadOnlyList<SplitAllocationSpec> specs, decimal lineAmount, string prebillLineId)
        {
            var percentTotal = NormalizeMoney(specs.Sum(s => s.Percent ?? 0m));
            if (percentTotal <= 0m)
            {
                throw new InvalidOperationException($"Split percentage total must be greater than zero for prebill line {prebillLineId}.");
            }
            if (percentTotal > 100.01m)
            {
                throw new InvalidOperationException($"Split percentage total exceeds 100% for prebill line {prebillLineId}.");
            }

            var resolved = new List<ResolvedLineSplitAllocation>(specs.Count);
            decimal running = 0m;
            for (var i = 0; i < specs.Count; i++)
            {
                var spec = specs[i];
                var pct = Math.Max(0m, spec.Percent ?? 0m);
                var amount = i == specs.Count - 1
                    ? NormalizeMoney(lineAmount - running)
                    : NormalizeMoney(lineAmount * pct / 100m);
                if (amount < 0m) amount = 0m;
                running = NormalizeMoney(running + amount);
                resolved.Add(new ResolvedLineSplitAllocation(spec, amount));
            }

            var total = NormalizeMoney(resolved.Sum(r => r.Amount));
            var diff = NormalizeMoney(lineAmount - total);
            if (diff != 0m)
            {
                var idx = resolved.FindIndex(r => r.Spec.IsPrimary);
                if (idx < 0) idx = resolved.Count - 1;
                var target = resolved[idx];
                resolved[idx] = target with { Amount = NormalizeMoney(target.Amount + diff) };
            }

            return resolved.Where(r => r.Amount > 0m).ToList();
        }

        private IReadOnlyList<ResolvedLineSplitAllocation> ResolveFixedAmountLineSplits(IReadOnlyList<SplitAllocationSpec> specs, decimal lineAmount, string primaryClientId)
        {
            var ordered = specs.OrderBy(s => s.Priority).ToList();
            var resolved = new List<ResolvedLineSplitAllocation>(ordered.Count + 1);
            var remaining = lineAmount;
            foreach (var spec in ordered)
            {
                var cap = NormalizeMoney(spec.AmountCap ?? 0m);
                if (cap <= 0m) continue;
                var amount = NormalizeMoney(Math.Min(cap, remaining));
                if (amount <= 0m) continue;
                resolved.Add(new ResolvedLineSplitAllocation(spec, amount));
                remaining = NormalizeMoney(remaining - amount);
                if (remaining <= 0m) break;
            }

            if (remaining > 0m)
            {
                var fallback = ordered.FirstOrDefault(s => s.IsPrimary)
                    ?? ordered.FirstOrDefault(s => string.Equals(s.PayorClientId, primaryClientId, StringComparison.Ordinal))
                    ?? DefaultPrimarySplitSpec(primaryClientId);

                var existingIdx = resolved.FindIndex(r => SameSplitIdentity(r.Spec, fallback));
                if (existingIdx >= 0)
                {
                    var current = resolved[existingIdx];
                    resolved[existingIdx] = current with { Amount = NormalizeMoney(current.Amount + remaining) };
                }
                else
                {
                    resolved.Add(new ResolvedLineSplitAllocation(fallback, remaining));
                }
            }

            return resolved.Where(r => r.Amount > 0m).ToList();
        }

        private IReadOnlyList<SplitAllocationSpec> ParseSplitAllocationSpecs(string? json, string primaryClientId, string? defaultThirdPartyPayorClientId)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return [];
            }

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                JsonElement itemsElement;
                if (root.ValueKind == JsonValueKind.Array)
                {
                    itemsElement = root;
                }
                else if (root.ValueKind == JsonValueKind.Object && TryGetPropertyIgnoreCase(root, "allocations", out var allocations) && allocations.ValueKind == JsonValueKind.Array)
                {
                    itemsElement = allocations;
                }
                else
                {
                    return [];
                }

                var list = new List<SplitAllocationSpec>();
                foreach (var item in itemsElement.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object) continue;

                    var payorClientId = GetString(item, "payorClientId")
                        ?? GetString(item, "clientId")
                        ?? GetString(item, "thirdPartyPayorClientId");
                    if (string.IsNullOrWhiteSpace(payorClientId))
                    {
                        continue;
                    }

                    var responsibility = GetString(item, "responsibilityType")
                        ?? GetString(item, "type")
                        ?? (string.Equals(payorClientId, defaultThirdPartyPayorClientId, StringComparison.Ordinal) ? "third_party" : null)
                        ?? "primary";
                    responsibility = NormalizeSplitResponsibilityType(responsibility);

                    decimal? percent = TryGetDecimalValue(item, "percent") ?? TryGetDecimalValue(item, "pct");
                    decimal? amountCap = TryGetDecimalValue(item, "amountCap") ?? TryGetDecimalValue(item, "amount");
                    var priority = TryGetIntValue(item, "priority") ?? 0;
                    var isPrimary = TryGetBoolValue(item, "isPrimary")
                        ?? string.Equals(payorClientId, primaryClientId, StringComparison.Ordinal);
                    var status = NormalizeSplitStatus(GetString(item, "status"));

                    var ebillingProfileJson = GetRawJson(item, "ebillingProfile")
                        ?? TruncateJson(GetString(item, "ebillingProfileJson"));
                    var metadataJson = GetRawJson(item, "metadata")
                        ?? TruncateJson(GetString(item, "metadataJson"));

                    list.Add(new SplitAllocationSpec(
                        PayorClientId: payorClientId.Trim(),
                        ResponsibilityType: responsibility,
                        Percent: percent.HasValue ? NormalizeMoney(percent.Value) : null,
                        AmountCap: amountCap.HasValue ? NormalizeMoney(amountCap.Value) : null,
                        Priority: priority,
                        Status: status,
                        IsPrimary: isPrimary,
                        Terms: Truncate(GetString(item, "terms"), 500),
                        Reference: Truncate(GetString(item, "reference"), 255),
                        PurchaseOrder: Truncate(GetString(item, "purchaseOrder"), 255),
                        EbillingProfileJson: ebillingProfileJson,
                        MetadataJson: metadataJson));
                }

                return list;
            }
            catch (JsonException)
            {
                return [];
            }
        }

        private static SplitAllocationSpec DefaultPrimarySplitSpec(string primaryClientId)
        {
            return new SplitAllocationSpec(
                PayorClientId: primaryClientId,
                ResponsibilityType: "primary",
                Percent: 100m,
                AmountCap: null,
                Priority: 100,
                Status: "active",
                IsPrimary: true,
                Terms: null,
                Reference: null,
                PurchaseOrder: null,
                EbillingProfileJson: null,
                MetadataJson: null);
        }

        private static bool SameSplitIdentity(SplitAllocationSpec left, SplitAllocationSpec right)
        {
            return string.Equals(left.PayorClientId, right.PayorClientId, StringComparison.Ordinal) &&
                   string.Equals(left.ResponsibilityType, right.ResponsibilityType, StringComparison.Ordinal) &&
                   left.Priority == right.Priority &&
                   left.IsPrimary == right.IsPrimary;
        }

        private static bool TryGetPropertyIgnoreCase(JsonElement element, string name, out JsonElement value)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }

            value = default;
            return false;
        }

        private static string? GetString(JsonElement element, string name)
        {
            if (!TryGetPropertyIgnoreCase(element, name, out var value))
            {
                return null;
            }

            return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
        }

        private static string? GetRawJson(JsonElement element, string name)
        {
            if (!TryGetPropertyIgnoreCase(element, name, out var value))
            {
                return null;
            }

            return value.ValueKind is JsonValueKind.Object or JsonValueKind.Array
                ? TruncateJson(value.GetRawText())
                : null;
        }

        private static decimal? TryGetDecimalValue(JsonElement element, string name)
        {
            if (!TryGetPropertyIgnoreCase(element, name, out var value))
            {
                return null;
            }

            return TryReadDecimal(value, out var parsed) ? parsed : null;
        }

        private static int? TryGetIntValue(JsonElement element, string name)
        {
            if (!TryGetPropertyIgnoreCase(element, name, out var value))
            {
                return null;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var direct))
            {
                return direct;
            }

            if (TryReadDecimal(value, out var decimalValue))
            {
                return (int)decimalValue;
            }

            return null;
        }

        private static bool? TryGetBoolValue(JsonElement element, string name)
        {
            if (!TryGetPropertyIgnoreCase(element, name, out var value))
            {
                return null;
            }

            return value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
                _ => null
            };
        }

        private static string NormalizeSplitResponsibilityType(string value)
        {
            return NormalizeEnum(value, "primary", ["primary", "split_percent", "split_amount", "third_party"]);
        }

        private static string NormalizeSplitStatus(string? value)
        {
            return NormalizeEnum(value, "active", ["active", "inactive", "closed"]);
        }

        private sealed record SplitAllocationSpec(
            string PayorClientId,
            string ResponsibilityType,
            decimal? Percent,
            decimal? AmountCap,
            int Priority,
            string Status,
            bool IsPrimary,
            string? Terms,
            string? Reference,
            string? PurchaseOrder,
            string? EbillingProfileJson,
            string? MetadataJson);

        private sealed record ResolvedLineSplitAllocation(SplitAllocationSpec Spec, decimal Amount);

        private sealed class InvoicePayorAllocationAggregate
        {
            public InvoicePayorAllocationAggregate(SplitAllocationSpec spec)
            {
                Spec = spec;
            }

            public SplitAllocationSpec Spec { get; }
            public decimal AllocatedAmount { get; set; }
            public decimal? Percent { get; set; }
            public decimal? AmountCap { get; set; }
            public int LineCount { get; set; }
        }

        private readonly record struct InvoicePayorAllocationAggregateKey(
            string PayorClientId,
            string ResponsibilityType,
            int Priority,
            bool IsPrimary,
            string Status,
            string? Terms,
            string? Reference,
            string? PurchaseOrder,
            string? EbillingProfileJson)
        {
            public static InvoicePayorAllocationAggregateKey From(SplitAllocationSpec spec) =>
                new(
                    spec.PayorClientId,
                    spec.ResponsibilityType,
                    spec.Priority,
                    spec.IsPrimary,
                    spec.Status,
                    spec.Terms,
                    spec.Reference,
                    spec.PurchaseOrder,
                    spec.EbillingProfileJson);

            public static InvoicePayorAllocationAggregateKey From(InvoicePayorAllocation allocation) =>
                new(
                    allocation.PayorClientId,
                    allocation.ResponsibilityType,
                    allocation.Priority,
                    allocation.IsPrimary,
                    allocation.Status,
                    allocation.Terms,
                    allocation.Reference,
                    allocation.PurchaseOrder,
                    allocation.EbillingProfileJson);

        }

        private sealed record PendingInvoiceLinePayorAllocation(InvoiceLinePayorAllocation Entity, InvoicePayorAllocationAggregateKey Key);
    }
}
