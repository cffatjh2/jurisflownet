using System.Globalization;
using System.Text.Json;
using JurisFlow.Server.Data;
using JurisFlow.Server.Enums;
using JurisFlow.Server.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Task = System.Threading.Tasks.Task;

namespace JurisFlow.Server.Services
{
    public sealed partial class LegalBillingEngineService
    {
        public const string BillingReviewProviderKey = "billing-engine";

        private static readonly string[] ApprovedStatuses = ["Approved", "APPROVED"];

        private readonly JurisFlowDbContext _context;
        private readonly IntegrationPiiMinimizationService _piiMinimizer;
        private readonly TrustRiskRadarService _trustRiskRadarService;
        private readonly ILogger<LegalBillingEngineService> _logger;

        public LegalBillingEngineService(
            JurisFlowDbContext context,
            IntegrationPiiMinimizationService piiMinimizer,
            TrustRiskRadarService trustRiskRadarService,
            ILogger<LegalBillingEngineService> logger)
        {
            _context = context;
            _piiMinimizer = piiMinimizer;
            _trustRiskRadarService = trustRiskRadarService;
            _logger = logger;
        }

        public Task<MatterBillingPolicy?> GetActiveMatterPolicyAsync(string matterId, DateTime? asOfUtc = null, CancellationToken ct = default)
        {
            var asOf = (asOfUtc ?? DateTime.UtcNow).Date;
            return _context.MatterBillingPolicies
                .Where(p =>
                    p.MatterId == matterId &&
                    p.Status == "active" &&
                    p.EffectiveFrom.Date <= asOf &&
                    (p.EffectiveTo == null || p.EffectiveTo.Value.Date >= asOf))
                .OrderByDescending(p => p.EffectiveFrom)
                .ThenByDescending(p => p.UpdatedAt)
                .FirstOrDefaultAsync(ct);
        }

        public async Task<MatterBillingPolicy> UpsertMatterPolicyAsync(MatterBillingPolicyUpsertRequest request, string? userId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(request.MatterId) || string.IsNullOrWhiteSpace(request.ClientId))
            {
                throw new InvalidOperationException("MatterId and ClientId are required.");
            }

            var matter = await _context.Matters.AsNoTracking().FirstOrDefaultAsync(m => m.Id == request.MatterId, ct)
                ?? throw new InvalidOperationException("Matter not found.");
            if (!string.Equals(matter.ClientId, request.ClientId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("ClientId does not match matter.");
            }

            var clientExists = await _context.Clients.AsNoTracking().AnyAsync(c => c.Id == request.ClientId, ct);
            if (!clientExists)
            {
                throw new InvalidOperationException("Client not found.");
            }

            var normalizedArrangement = NormalizeEnum(request.ArrangementType, "hourly", ["hourly", "fixed", "contingency", "hybrid"]);
            var normalizedCycle = NormalizeEnum(request.BillingCycle, "monthly", ["monthly", "milestone", "ad_hoc"]);
            var normalizedTaxMode = NormalizeEnum(request.TaxPolicyMode, "matter", ["matter", "jurisdiction", "none"]);
            var normalizedTrustMode = NormalizeEnum(request.TrustHandlingMode, "separate", ["separate", "mixed"]);
            var normalizedEbillingFormat = NormalizeEnum(request.EbillingFormat, "none", ["none", "ledes98b", "ledes1998bi"]);
            var normalizedEbillingStatus = NormalizeEnum(request.EbillingStatus, "disabled", ["disabled", "enabled"]);

            if (normalizedArrangement is "hourly" or "hybrid" && string.IsNullOrWhiteSpace(request.RateCardId))
            {
                throw new InvalidOperationException("RateCardId is required for hourly/hybrid arrangements.");
            }

            if (!string.IsNullOrWhiteSpace(request.RateCardId))
            {
                var rateCardExists = await _context.BillingRateCards.AnyAsync(r => r.Id == request.RateCardId, ct);
                if (!rateCardExists)
                {
                    throw new InvalidOperationException("Rate card not found.");
                }
            }

            if (!string.IsNullOrWhiteSpace(request.ThirdPartyPayorClientId))
            {
                var payorExists = await _context.Clients.AnyAsync(c => c.Id == request.ThirdPartyPayorClientId, ct);
                if (!payorExists)
                {
                    throw new InvalidOperationException("Third-party payor client not found.");
                }
            }

            MatterBillingPolicy? policy = null;
            if (!string.IsNullOrWhiteSpace(request.Id))
            {
                policy = await _context.MatterBillingPolicies.FirstOrDefaultAsync(p => p.Id == request.Id, ct);
            }
            if (policy == null)
            {
                policy = new MatterBillingPolicy();
                _context.MatterBillingPolicies.Add(policy);
            }

            policy.MatterId = request.MatterId;
            policy.ClientId = request.ClientId;
            policy.ThirdPartyPayorClientId = NullIfEmpty(request.ThirdPartyPayorClientId);
            policy.ArrangementType = normalizedArrangement;
            policy.BillingCycle = normalizedCycle;
            policy.RateCardId = NullIfEmpty(request.RateCardId);
            policy.Currency = NormalizeCurrency(request.Currency, "USD");
            policy.TaxPolicyMode = normalizedTaxMode;
            policy.TrustHandlingMode = normalizedTrustMode;
            policy.CollectionPolicy = string.IsNullOrWhiteSpace(request.CollectionPolicy) ? "standard" : request.CollectionPolicy.Trim();
            policy.EbillingFormat = normalizedEbillingFormat;
            policy.EbillingStatus = normalizedEbillingStatus;
            policy.RequirePrebillApproval = request.RequirePrebillApproval ?? true;
            policy.EnforceUtbmsCodes = request.EnforceUtbmsCodes ?? false;
            policy.EnforceTrustOperatingSplit = request.EnforceTrustOperatingSplit ?? true;
            policy.EffectiveFrom = (request.EffectiveFrom ?? DateTime.UtcNow).Date;
            policy.EffectiveTo = request.EffectiveTo?.Date;
            policy.Status = NormalizeEnum(request.Status, "active", ["active", "inactive", "retired"]);
            policy.TaxPolicyJson = TruncateJson(request.TaxPolicyJson);
            if (request.SplitAllocations != null)
            {
                policy.SplitBillingJson = SerializeValidatedSplitAllocationJson(request.SplitAllocations, request.ClientId, request.ThirdPartyPayorClientId);
            }
            else
            {
                policy.SplitBillingJson = TruncateJson(request.SplitBillingJson);
            }
            policy.EbillingProfileJson = TruncateJson(request.EbillingProfileJson);
            policy.CollectionPolicyJson = TruncateJson(request.CollectionPolicyJson);
            policy.TrustPolicyJson = TruncateJson(request.TrustPolicyJson);
            policy.MetadataJson = TruncateJson(request.MetadataJson);
            policy.Notes = Truncate(request.Notes, 2048);
            policy.UpdatedBy = userId;
            policy.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(ct);
            return policy;
        }

        public async Task<IReadOnlyList<BillingRateCard>> ListRateCardsAsync(BillingRateCardQuery query, CancellationToken ct = default)
        {
            var q = _context.BillingRateCards.AsQueryable();
            if (!string.IsNullOrWhiteSpace(query.Scope)) q = q.Where(r => r.Scope == query.Scope);
            if (!string.IsNullOrWhiteSpace(query.ClientId)) q = q.Where(r => r.ClientId == query.ClientId);
            if (!string.IsNullOrWhiteSpace(query.MatterId)) q = q.Where(r => r.MatterId == query.MatterId);
            if (!string.IsNullOrWhiteSpace(query.Status)) q = q.Where(r => r.Status == query.Status);

            return await q.OrderByDescending(r => r.EffectiveFrom)
                .ThenBy(r => r.Name)
                .Take(Math.Clamp(query.Limit ?? 200, 1, 500))
                .ToListAsync(ct);
        }

        public async Task<BillingRateCard> UpsertRateCardAsync(BillingRateCardUpsertRequest request, string? userId, CancellationToken ct = default)
        {
            var scope = NormalizeEnum(request.Scope, "firm", ["firm", "client", "matter"]);
            if (scope == "client" && string.IsNullOrWhiteSpace(request.ClientId))
            {
                throw new InvalidOperationException("ClientId is required for client scope.");
            }
            if (scope == "matter" && string.IsNullOrWhiteSpace(request.MatterId))
            {
                throw new InvalidOperationException("MatterId is required for matter scope.");
            }

            BillingRateCard? card = null;
            if (!string.IsNullOrWhiteSpace(request.Id))
            {
                card = await _context.BillingRateCards.FirstOrDefaultAsync(r => r.Id == request.Id, ct);
            }
            if (card == null)
            {
                card = new BillingRateCard();
                _context.BillingRateCards.Add(card);
            }

            card.Name = string.IsNullOrWhiteSpace(request.Name) ? throw new InvalidOperationException("Name is required.") : request.Name.Trim();
            card.Currency = NormalizeCurrency(request.Currency, "USD");
            card.Scope = scope;
            card.ClientId = NullIfEmpty(request.ClientId);
            card.MatterId = NullIfEmpty(request.MatterId);
            card.Status = NormalizeEnum(request.Status, "active", ["active", "inactive", "retired"]);
            card.EffectiveFrom = (request.EffectiveFrom ?? DateTime.UtcNow).Date;
            card.EffectiveTo = request.EffectiveTo?.Date;
            card.MetadataJson = TruncateJson(request.MetadataJson);
            card.UpdatedBy = userId;
            card.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(ct);
            return card;
        }

        public async Task<IReadOnlyList<BillingRateCardEntry>> ListRateCardEntriesAsync(string rateCardId, CancellationToken ct = default)
        {
            return await _context.BillingRateCardEntries
                .Where(e => e.RateCardId == rateCardId)
                .OrderBy(e => e.Priority)
                .ThenBy(e => e.EntryType)
                .ToListAsync(ct);
        }

        public async Task<BillingRateCardEntry> UpsertRateCardEntryAsync(BillingRateCardEntryUpsertRequest request, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(request.RateCardId))
            {
                throw new InvalidOperationException("RateCardId is required.");
            }

            var cardExists = await _context.BillingRateCards.AnyAsync(r => r.Id == request.RateCardId, ct);
            if (!cardExists)
            {
                throw new InvalidOperationException("Rate card not found.");
            }

            BillingRateCardEntry? entry = null;
            if (!string.IsNullOrWhiteSpace(request.Id))
            {
                entry = await _context.BillingRateCardEntries.FirstOrDefaultAsync(e => e.Id == request.Id, ct);
            }
            if (entry == null)
            {
                entry = new BillingRateCardEntry();
                _context.BillingRateCardEntries.Add(entry);
            }

            entry.RateCardId = request.RateCardId;
            entry.EntryType = NormalizeEnum(request.EntryType, "time", ["time", "expense", "fixed"]);
            entry.TimekeeperRole = NullIfEmpty(request.TimekeeperRole);
            entry.EmployeeId = NullIfEmpty(request.EmployeeId);
            entry.ClientId = NullIfEmpty(request.ClientId);
            entry.MatterId = NullIfEmpty(request.MatterId);
            entry.TaskCode = NullIfEmpty(request.TaskCode);
            entry.ActivityCode = NullIfEmpty(request.ActivityCode);
            entry.ExpenseCode = NullIfEmpty(request.ExpenseCode);
            entry.Unit = NormalizeEnum(request.Unit, "hour", ["hour", "item", "amount"]);
            entry.Rate = NormalizeMoney(request.Rate);
            entry.MinimumUnits = request.MinimumUnits.HasValue ? NormalizeMoney(request.MinimumUnits.Value) : null;
            entry.MaximumUnits = request.MaximumUnits.HasValue ? NormalizeMoney(request.MaximumUnits.Value) : null;
            entry.Status = NormalizeEnum(request.Status, "active", ["active", "inactive"]);
            entry.Priority = request.Priority ?? 100;
            entry.MetadataJson = TruncateJson(request.MetadataJson);
            entry.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(ct);
            return entry;
        }

        private async Task EnsureNotLockedAsync(DateTime dateUtc, CancellationToken ct, string operationType = "billing_operation")
        {
            var day = DateOnly.FromDateTime(dateUtc);
            var locks = await _context.BillingLocks.AsNoTracking()
                .Select(l => new { l.PeriodStart, l.PeriodEnd })
                .ToListAsync(ct);

            foreach (var item in locks)
            {
                if (!TryParseDateOnly(item.PeriodStart, out var start) || !TryParseDateOnly(item.PeriodEnd, out var end))
                {
                    continue;
                }
                if (day >= start && day <= end)
                {
                    try
                    {
                        await _trustRiskRadarService.RecordPeriodLockAttemptAsync(dateUtc, operationType, ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Trust risk radar failed to record period lock attempt for {OperationType}.", operationType);
                    }
                    throw new InvalidOperationException("Billing period is locked.");
                }
            }
        }

        private static bool TryParseDateOnly(string? value, out DateOnly date)
        {
            return DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
        }

        private async Task<BillingRateCard?> ResolveRateCardAsync(MatterBillingPolicy policy, Matter matter, CancellationToken ct)
        {
            if (!string.IsNullOrWhiteSpace(policy.RateCardId))
            {
                return await _context.BillingRateCards.FirstOrDefaultAsync(r => r.Id == policy.RateCardId && r.Status == "active", ct);
            }

            return await _context.BillingRateCards
                .Where(r =>
                    r.Status == "active" &&
                    r.EffectiveFrom <= DateTime.UtcNow.Date &&
                    (r.EffectiveTo == null || r.EffectiveTo >= DateTime.UtcNow.Date) &&
                    ((r.Scope == "matter" && r.MatterId == matter.Id) ||
                     (r.Scope == "client" && r.ClientId == matter.ClientId) ||
                     r.Scope == "firm"))
                .OrderBy(r => r.Scope == "matter" ? 0 : r.Scope == "client" ? 1 : 2)
                .ThenByDescending(r => r.EffectiveFrom)
                .FirstOrDefaultAsync(ct);
        }

        private async Task<string?> QueuePrebillReviewIfNeededAsync(
            string prebillBatchId,
            IReadOnlyList<string> warnings,
            Matter matter,
            Client client,
            MatterBillingPolicy policy,
            CancellationToken ct)
        {
            if (warnings.Count == 0)
            {
                return null;
            }

            var existing = await _context.IntegrationReviewQueueItems
                .FirstOrDefaultAsync(r =>
                    r.ProviderKey == BillingReviewProviderKey &&
                    r.ItemType == "prebill_review" &&
                    r.SourceType == nameof(BillingPrebillBatch) &&
                    r.SourceId == prebillBatchId &&
                    (r.Status == IntegrationReviewQueueStatuses.Pending || r.Status == IntegrationReviewQueueStatuses.InReview),
                    ct);

            var summary = Truncate(string.Join(" ", warnings.Take(10)), 2048);
            var contextJson = _piiMinimizer.SanitizeObjectForStorage(new
            {
                prebillBatchId,
                matter = new { matter.Id, matter.Name, matter.ClientId, matter.FeeStructure },
                client = new { client.Id, client.Name, client.Email },
                policy = new { policy.Id, policy.ArrangementType, policy.EbillingStatus, policy.EbillingFormat, policy.EnforceUtbmsCodes },
                warnings
            }, "billing_engine:prebill_review");

            if (existing != null)
            {
                existing.Summary = summary;
                existing.ContextJson = contextJson;
                existing.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync(ct);
                return existing.Id;
            }

            var review = new IntegrationReviewQueueItem
            {
                ProviderKey = BillingReviewProviderKey,
                ItemType = "prebill_review",
                SourceType = nameof(BillingPrebillBatch),
                SourceId = prebillBatchId,
                Status = IntegrationReviewQueueStatuses.Pending,
                Priority = "high",
                Title = "Prebill review required (UTBMS/LEDES readiness)",
                Summary = summary,
                ContextJson = contextJson,
                SuggestedActionsJson = JsonSerializer.Serialize(new object[]
                {
                    new { action = "review_prebill_lines", prebillBatchId },
                    new { action = "fix_utbms_codes", prebillBatchId },
                    new { action = "approve_with_manual_override", prebillBatchId }
                }),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.IntegrationReviewQueueItems.Add(review);
            await _context.SaveChangesAsync(ct);
            return review.Id;
        }

        private static ResolvedRate ResolveTimeRate(TimeEntry time, Matter matter, MatterBillingPolicy policy, IReadOnlyList<BillingRateCardEntry> entries)
        {
            var match = entries
                .Where(e => e.EntryType == "time" && e.Status == "active")
                .Select(e => new { Entry = e, Score = ScoreRateEntry(e, matter.Id, matter.ClientId, null, time.TaskCode, time.ActivityCode, null) })
                .OrderBy(x => x.Score)
                .ThenBy(x => x.Entry.Priority)
                .FirstOrDefault(x => x.Score < int.MaxValue);

            if (match != null)
            {
                return new ResolvedRate(NormalizeMoney(match.Entry.Rate), "rate_card", match.Entry.Id);
            }

            _ = policy;
            var fallback = NormalizeMoney((decimal)(time.Rate > 0 ? time.Rate : matter.BillableRate));
            return new ResolvedRate(fallback, "source_or_matter_fallback", null);
        }

        private static ResolvedRate ResolveExpenseRate(Expense expense, MatterBillingPolicy policy, IReadOnlyList<BillingRateCardEntry> entries, decimal proposedAmount)
        {
            var match = entries
                .Where(e => e.EntryType == "expense" && e.Status == "active")
                .Select(e => new { Entry = e, Score = ScoreRateEntry(e, null, null, null, null, null, expense.ExpenseCode) })
                .OrderBy(x => x.Score)
                .ThenBy(x => x.Entry.Priority)
                .FirstOrDefault(x => x.Score < int.MaxValue);

            if (match != null)
            {
                return new ResolvedRate(NormalizeMoney(match.Entry.Rate), "rate_card", match.Entry.Id);
            }

            _ = policy;
            return new ResolvedRate(NormalizeMoney(proposedAmount), "expense_amount_fallback", null);
        }

        private static int ScoreRateEntry(BillingRateCardEntry entry, string? matterId, string? clientId, string? employeeId, string? taskCode, string? activityCode, string? expenseCode)
        {
            var score = 0;
            if (!string.IsNullOrWhiteSpace(entry.MatterId))
            {
                if (!string.Equals(entry.MatterId, matterId, StringComparison.Ordinal)) return int.MaxValue;
                score -= 100;
            }
            if (!string.IsNullOrWhiteSpace(entry.ClientId))
            {
                if (!string.Equals(entry.ClientId, clientId, StringComparison.Ordinal)) return int.MaxValue;
                score -= 50;
            }
            if (!string.IsNullOrWhiteSpace(entry.EmployeeId))
            {
                if (!string.Equals(entry.EmployeeId, employeeId, StringComparison.Ordinal)) return int.MaxValue;
                score -= 40;
            }
            if (!string.IsNullOrWhiteSpace(entry.TaskCode))
            {
                if (!string.Equals(entry.TaskCode, taskCode, StringComparison.OrdinalIgnoreCase)) return int.MaxValue;
                score -= 20;
            }
            if (!string.IsNullOrWhiteSpace(entry.ActivityCode))
            {
                if (!string.Equals(entry.ActivityCode, activityCode, StringComparison.OrdinalIgnoreCase)) return int.MaxValue;
                score -= 10;
            }
            if (!string.IsNullOrWhiteSpace(entry.ExpenseCode))
            {
                if (!string.Equals(entry.ExpenseCode, expenseCode, StringComparison.OrdinalIgnoreCase)) return int.MaxValue;
                score -= 20;
            }
            score += Math.Max(0, entry.Priority);
            return score;
        }

        private static decimal GetDefaultTaxRatePercent(MatterBillingPolicy policy)
        {
            if (string.IsNullOrWhiteSpace(policy.TaxPolicyJson))
            {
                return 0m;
            }

            try
            {
                using var doc = JsonDocument.Parse(policy.TaxPolicyJson);
                if (doc.RootElement.TryGetProperty("defaultRatePercent", out var a) && TryReadDecimal(a, out var v))
                {
                    return NormalizeMoney(v);
                }
                if (doc.RootElement.TryGetProperty("ratePercent", out var b) && TryReadDecimal(b, out var v2))
                {
                    return NormalizeMoney(v2);
                }
            }
            catch
            {
            }

            return 0m;
        }

        private static string? GetDefaultTaxCode(MatterBillingPolicy policy)
        {
            if (string.IsNullOrWhiteSpace(policy.TaxPolicyJson))
            {
                return null;
            }

            try
            {
                using var doc = JsonDocument.Parse(policy.TaxPolicyJson);
                if (doc.RootElement.TryGetProperty("defaultTaxCode", out var code) && code.ValueKind == JsonValueKind.String)
                {
                    return NullIfEmpty(code.GetString());
                }
            }
            catch
            {
            }

            return null;
        }

        private static bool TryReadDecimal(JsonElement element, out decimal value)
        {
            if (element.ValueKind == JsonValueKind.Number)
            {
                return element.TryGetDecimal(out value);
            }
            if (element.ValueKind == JsonValueKind.String)
            {
                return decimal.TryParse(element.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out value);
            }
            value = 0m;
            return false;
        }

        private static string NormalizeInvoiceLineType(string lineType)
        {
            var normalized = (lineType ?? string.Empty).Trim().ToLowerInvariant();
            return normalized is "time" or "expense" or "fixed" ? normalized : "fixed";
        }

        private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        private static decimal NormalizeMoney(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

        private static string NormalizeCurrency(string? currency, string fallback)
        {
            var normalized = string.IsNullOrWhiteSpace(currency) ? fallback : currency.Trim().ToUpperInvariant();
            if (normalized.Length != 3 || normalized.Any(c => !char.IsLetter(c)))
            {
                throw new InvalidOperationException("Currency must be an ISO alpha-3 code.");
            }
            return normalized;
        }

        private static string NormalizeEnum(string? value, string fallback, IReadOnlyCollection<string> allowed)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }
            var normalized = value.Trim().ToLowerInvariant();
            return allowed.Contains(normalized) ? normalized : throw new InvalidOperationException($"Unsupported value '{value}'.");
        }

        private static string? Truncate(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var trimmed = value.Trim();
            return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
        }

        private static string? TruncateJson(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var trimmed = value.Trim();
            return trimmed.Length <= 64000 ? trimmed : trimmed[..64000];
        }

        private readonly record struct ResolvedRate(decimal Rate, string Source, string? RateCardEntryId);
    }
}
