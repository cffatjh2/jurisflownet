using System.Security.Claims;
using System.Text.Json;
using JurisFlow.Server.Contracts;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace JurisFlow.Server.Services
{
    public sealed class TrustComplianceExportService
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };

        private readonly JurisFlowDbContext _context;
        private readonly AuditLogger _auditLogger;
        private readonly TrustActionAuthorizationService _authorization;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public TrustComplianceExportService(
            JurisFlowDbContext context,
            AuditLogger auditLogger,
            TrustActionAuthorizationService authorization,
            IHttpContextAccessor httpContextAccessor)
        {
            _context = context;
            _auditLogger = auditLogger;
            _authorization = authorization;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task<IReadOnlyList<TrustComplianceExportListItemDto>> ListExportsAsync(string? trustAccountId = null, string? exportType = null, CancellationToken ct = default)
        {
            _authorization.EnsureAllowed(TrustActionKeys.ExportData, GetCurrentUser());

            var normalizedType = NormalizeExportTypeOrNull(exportType);
            var query = _context.TrustComplianceExports
                .AsNoTracking()
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(trustAccountId))
            {
                query = query.Where(x => x.TrustAccountId == trustAccountId);
            }

            if (!string.IsNullOrWhiteSpace(normalizedType))
            {
                query = query.Where(x => x.ExportType == normalizedType);
            }

            var rows = await query
                .OrderByDescending(x => x.GeneratedAt)
                .Take(50)
                .ToListAsync(ct);

            return rows.Select(ToListDto).ToList();
        }

        public async Task<TrustComplianceExportDto> GetExportAsync(string id, CancellationToken ct = default)
        {
            _authorization.EnsureAllowed(TrustActionKeys.ExportData, GetCurrentUser());

            var export = await _context.TrustComplianceExports
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id, ct);

            if (export == null)
            {
                throw new TrustCommandException(StatusCodes.Status404NotFound, "Trust export not found.");
            }

            return ToDetailDto(export);
        }

        public async Task<TrustComplianceExportDto> GenerateExportAsync(TrustComplianceExportRequest request, CancellationToken ct = default)
        {
            _authorization.EnsureAllowed(TrustActionKeys.ExportData, GetCurrentUser());

            var exportType = NormalizeExportType(request.ExportType);
            var format = NormalizeFormat(request.Format);
            var scope = await ResolveScopeAsync(request, exportType, ct);
            var contextEnvelope = await BuildExportContextAsync(scope, ct);
            var payload = exportType switch
            {
                "account_journal" => await BuildAccountJournalPayloadAsync(scope, contextEnvelope, ct),
                "client_ledger" => await BuildClientLedgerPayloadAsync(scope, contextEnvelope, ct),
                "approval_register" => await BuildApprovalRegisterPayloadAsync(scope, contextEnvelope, ct),
                "month_close_packet" => await BuildMonthClosePacketPayloadAsync(scope, contextEnvelope, ct),
                _ => throw new TrustCommandException(StatusCodes.Status400BadRequest, "Unsupported trust export type.")
            };

            var export = new TrustComplianceExport
            {
                Id = Guid.NewGuid().ToString(),
                ExportType = exportType,
                Format = format,
                Status = "completed",
                TrustAccountId = scope.Account?.Id,
                ClientTrustLedgerId = scope.Ledger?.Id,
                TrustMonthCloseId = scope.MonthClose?.Id,
                TrustReconciliationPacketId = scope.Packet?.Id,
                FileName = BuildFileName(exportType, format, scope),
                ContentType = ResolveContentType(format),
                SummaryJson = JsonSerializer.Serialize(payload.Summary, JsonOptions),
                PayloadJson = JsonSerializer.Serialize(payload.Payload, JsonOptions),
                GeneratedBy = GetCurrentUserId(),
                IntegrityStatus = "unsigned",
                GeneratedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.TrustComplianceExports.Add(export);
            await _context.SaveChangesAsync(ct);
            var httpContext = GetCurrentHttpContext();
            if (httpContext != null)
            {
                await _auditLogger.LogAsync(httpContext, "trust.export.generate", nameof(TrustComplianceExport), export.Id, $"Type={export.ExportType}, Format={export.Format}, Account={export.TrustAccountId}");
            }
            return ToDetailDto(export);
        }

        private async Task<ExportScope> ResolveScopeAsync(TrustComplianceExportRequest request, string exportType, CancellationToken ct)
        {
            var periodStart = request.PeriodStart?.Date;
            var periodEnd = request.PeriodEnd?.Date;
            if (periodStart.HasValue && periodEnd.HasValue && periodStart > periodEnd)
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Export period is invalid.");
            }

            ClientTrustLedger? ledger = null;
            TrustMonthClose? monthClose = null;
            TrustReconciliationPacket? packet = null;

            if (!string.IsNullOrWhiteSpace(request.ClientTrustLedgerId))
            {
                ledger = await _context.ClientTrustLedgers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.ClientTrustLedgerId, ct);
                if (ledger == null)
                {
                    throw new TrustCommandException(StatusCodes.Status404NotFound, "Trust ledger not found.");
                }
            }

            if (!string.IsNullOrWhiteSpace(request.TrustMonthCloseId))
            {
                monthClose = await _context.TrustMonthCloses.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.TrustMonthCloseId, ct);
                if (monthClose == null)
                {
                    throw new TrustCommandException(StatusCodes.Status404NotFound, "Month close not found.");
                }
            }

            if (!string.IsNullOrWhiteSpace(request.TrustReconciliationPacketId))
            {
                packet = await _context.TrustReconciliationPackets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.TrustReconciliationPacketId, ct);
                if (packet == null)
                {
                    throw new TrustCommandException(StatusCodes.Status404NotFound, "Reconciliation packet not found.");
                }
            }

            var accountId = request.TrustAccountId
                ?? ledger?.TrustAccountId
                ?? monthClose?.TrustAccountId
                ?? packet?.TrustAccountId;

            TrustBankAccount? account = null;
            if (!string.IsNullOrWhiteSpace(accountId))
            {
                account = await _context.TrustBankAccounts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == accountId, ct);
            }

            if (account == null && exportType != "approval_register")
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "A trust account scope is required for this export.");
            }

            if (exportType == "client_ledger" && ledger == null)
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Client ledger export requires a ledger id.");
            }

            if (exportType == "month_close_packet" && monthClose == null && packet == null)
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Month-close packet export requires a month-close id or packet id.");
            }

            if (monthClose == null && packet != null)
            {
                monthClose = await _context.TrustMonthCloses.AsNoTracking()
                    .Where(x => x.ReconciliationPacketId == packet.Id)
                    .OrderByDescending(x => x.PreparedAt)
                    .FirstOrDefaultAsync(ct);
            }

            if (packet == null && monthClose?.ReconciliationPacketId != null)
            {
                packet = await _context.TrustReconciliationPackets.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == monthClose.ReconciliationPacketId, ct);
            }

            if (!periodStart.HasValue)
            {
                periodStart = monthClose?.PeriodStart.Date ?? packet?.PeriodStart.Date;
            }

            if (!periodEnd.HasValue)
            {
                periodEnd = monthClose?.PeriodEnd.Date ?? packet?.PeriodEnd.Date;
            }

            return new ExportScope(account, ledger, monthClose, packet, periodStart, periodEnd);
        }

        private async Task<ExportContextEnvelope> BuildExportContextAsync(ExportScope scope, CancellationToken ct)
        {
            var firm = await _context.FirmSettings.AsNoTracking().OrderByDescending(x => x.UpdatedAt).FirstOrDefaultAsync(ct);
            FirmEntity? entity = null;
            Office? office = null;
            User? responsibleLawyer = null;
            User? generatedBy = null;

            if (!string.IsNullOrWhiteSpace(scope.Account?.EntityId))
            {
                entity = await _context.FirmEntities.AsNoTracking().FirstOrDefaultAsync(x => x.Id == scope.Account.EntityId, ct);
            }

            if (!string.IsNullOrWhiteSpace(scope.Account?.OfficeId))
            {
                office = await _context.Offices.AsNoTracking().FirstOrDefaultAsync(x => x.Id == scope.Account.OfficeId, ct);
            }

            if (!string.IsNullOrWhiteSpace(scope.Account?.ResponsibleLawyerUserId))
            {
                responsibleLawyer = await _context.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == scope.Account.ResponsibleLawyerUserId, ct);
            }

            var currentUserId = GetCurrentUserId();
            if (!string.IsNullOrWhiteSpace(currentUserId))
            {
                generatedBy = await _context.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == currentUserId, ct);
            }

            return new ExportContextEnvelope(firm, entity, office, responsibleLawyer, generatedBy);
        }

        private async Task<ExportBuildResult> BuildAccountJournalPayloadAsync(ExportScope scope, ExportContextEnvelope ctx, CancellationToken ct)
        {
            var account = scope.Account!;
            var effectiveTo = ToInclusiveCutoff(scope.PeriodEnd);
            var query = _context.TrustJournalEntries.AsNoTracking().Where(x => x.TrustAccountId == account.Id);

            if (scope.PeriodStart.HasValue)
            {
                query = query.Where(x => x.EffectiveAt >= scope.PeriodStart.Value);
            }

            if (effectiveTo.HasValue)
            {
                query = query.Where(x => x.EffectiveAt <= effectiveTo.Value);
            }

            var entries = await query.OrderBy(x => x.EffectiveAt).ThenBy(x => x.CreatedAt).ToListAsync(ct);
            var transactionIds = entries.Select(x => x.TrustTransactionId).Distinct().ToList();
            var transactions = await _context.TrustTransactions.AsNoTracking().Where(x => transactionIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
            var ledgerIds = entries.Where(x => x.ClientTrustLedgerId != null).Select(x => x.ClientTrustLedgerId!).Distinct().ToList();
            var ledgers = await _context.ClientTrustLedgers.AsNoTracking().Where(x => ledgerIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);

            var csvRows = entries.Select(entry =>
            {
                transactions.TryGetValue(entry.TrustTransactionId, out var tx);
                ledgers.TryGetValue(entry.ClientTrustLedgerId ?? string.Empty, out var ledger);
                return new Dictionary<string, object?>
                {
                    ["effectiveAt"] = entry.EffectiveAt,
                    ["createdAt"] = entry.CreatedAt,
                    ["operationType"] = entry.OperationType,
                    ["entryKind"] = entry.EntryKind,
                    ["availabilityClass"] = entry.AvailabilityClass,
                    ["amount"] = entry.Amount,
                    ["description"] = entry.Description,
                    ["matterId"] = entry.MatterId,
                    ["ledgerId"] = entry.ClientTrustLedgerId,
                    ["ledgerClientId"] = ledger?.ClientId,
                    ["trustTransactionId"] = entry.TrustTransactionId,
                    ["transactionStatus"] = tx?.Status,
                    ["payorPayee"] = tx?.PayorPayee,
                    ["correlationKey"] = entry.CorrelationKey
                };
            }).ToList();

            var payload = new
            {
                metadata = BuildMetadata("account_journal", "Trust Account Journal", scope, ctx),
                account = new
                {
                    account.Id,
                    account.Name,
                    account.BankName,
                    account.Jurisdiction,
                    account.AccountType,
                    account.CurrentBalance,
                    account.ClearedBalance,
                    account.UnclearedBalance,
                    account.AvailableDisbursementCapacity
                },
                period = BuildPeriod(scope),
                totals = new
                {
                    entryCount = entries.Count,
                    netAmount = entries.Sum(x => x.Amount),
                    clearedAmount = entries.Where(x => x.AvailabilityClass == "cleared").Sum(x => x.Amount),
                    unclearedAmount = entries.Where(x => x.AvailabilityClass == "uncleared").Sum(x => x.Amount)
                },
                csvRows
            };

            return new ExportBuildResult(new
            {
                title = "Trust Account Journal",
                subtitle = account.Name,
                rowCount = csvRows.Count,
                generatedAt = DateTime.UtcNow
            }, payload);
        }

        private async Task<ExportBuildResult> BuildClientLedgerPayloadAsync(ExportScope scope, ExportContextEnvelope ctx, CancellationToken ct)
        {
            var ledger = scope.Ledger!;
            var account = scope.Account!;
            var effectiveTo = ToInclusiveCutoff(scope.PeriodEnd);
            var query = _context.TrustJournalEntries.AsNoTracking().Where(x => x.ClientTrustLedgerId == ledger.Id);

            if (scope.PeriodStart.HasValue)
            {
                query = query.Where(x => x.EffectiveAt >= scope.PeriodStart.Value);
            }

            if (effectiveTo.HasValue)
            {
                query = query.Where(x => x.EffectiveAt <= effectiveTo.Value);
            }

            var entries = await query.OrderBy(x => x.EffectiveAt).ThenBy(x => x.CreatedAt).ToListAsync(ct);
            var transactionIds = entries.Select(x => x.TrustTransactionId).Distinct().ToList();
            var transactions = await _context.TrustTransactions.AsNoTracking().Where(x => transactionIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);

            var csvRows = entries.Select(entry =>
            {
                transactions.TryGetValue(entry.TrustTransactionId, out var tx);
                return new Dictionary<string, object?>
                {
                    ["effectiveAt"] = entry.EffectiveAt,
                    ["operationType"] = entry.OperationType,
                    ["availabilityClass"] = entry.AvailabilityClass,
                    ["amount"] = entry.Amount,
                    ["description"] = entry.Description,
                    ["trustTransactionId"] = entry.TrustTransactionId,
                    ["transactionStatus"] = tx?.Status,
                    ["payorPayee"] = tx?.PayorPayee,
                    ["matterId"] = entry.MatterId ?? tx?.MatterId
                };
            }).ToList();

            var payload = new
            {
                metadata = BuildMetadata("client_ledger", "Client Trust Ledger Card", scope, ctx),
                account = new
                {
                    account.Id,
                    account.Name,
                    account.BankName,
                    account.Jurisdiction
                },
                ledger = new
                {
                    ledger.Id,
                    ledger.ClientId,
                    ledger.MatterId,
                    ledger.RunningBalance,
                    ledger.ClearedBalance,
                    ledger.UnclearedBalance,
                    ledger.AvailableToDisburse,
                    ledger.HoldAmount,
                    ledger.Status
                },
                period = BuildPeriod(scope),
                totals = new
                {
                    entryCount = csvRows.Count,
                    netAmount = entries.Sum(x => x.Amount),
                    endingRunningBalance = ledger.RunningBalance,
                    endingAvailableToDisburse = ledger.AvailableToDisburse
                },
                csvRows
            };

            return new ExportBuildResult(new
            {
                title = "Client Trust Ledger Card",
                subtitle = ledger.ClientId,
                rowCount = csvRows.Count,
                generatedAt = DateTime.UtcNow
            }, payload);
        }

        private async Task<ExportBuildResult> BuildApprovalRegisterPayloadAsync(ExportScope scope, ExportContextEnvelope ctx, CancellationToken ct)
        {
            var accountId = scope.Account?.Id;
            var periodStart = scope.PeriodStart;
            var periodEnd = ToInclusiveCutoff(scope.PeriodEnd);

            var txQuery = _context.TrustTransactions.AsNoTracking()
                .Where(x => x.Status == "PENDING" || x.Status == "APPROVED" || x.Status == "REJECTED" || x.Status == "VOIDED");

            if (!string.IsNullOrWhiteSpace(accountId))
            {
                txQuery = txQuery.Where(x => x.TrustAccountId == accountId);
            }
            if (periodStart.HasValue)
            {
                txQuery = txQuery.Where(x => x.CreatedAt >= periodStart.Value);
            }
            if (periodEnd.HasValue)
            {
                txQuery = txQuery.Where(x => x.CreatedAt <= periodEnd.Value);
            }

            var transactions = await txQuery.OrderByDescending(x => x.CreatedAt).Take(300).ToListAsync(ct);
            var txIds = transactions.Select(x => x.Id).ToList();
            var requirements = await _context.TrustApprovalRequirements.AsNoTracking().Where(x => txIds.Contains(x.TrustTransactionId)).OrderBy(x => x.RequirementType).ToListAsync(ct);
            var decisions = await _context.TrustApprovalDecisions.AsNoTracking().Where(x => txIds.Contains(x.TrustTransactionId)).OrderBy(x => x.CreatedAt).ToListAsync(ct);
            var overrides = await _context.TrustApprovalOverrides.AsNoTracking().Where(x => txIds.Contains(x.TrustTransactionId)).OrderBy(x => x.CreatedAt).ToListAsync(ct);

            var requirementLookup = requirements.GroupBy(x => x.TrustTransactionId).ToDictionary(x => x.Key, x => x.ToList());
            var decisionLookup = decisions.GroupBy(x => x.TrustTransactionId).ToDictionary(x => x.Key, x => x.ToList());
            var overrideLookup = overrides.GroupBy(x => x.TrustTransactionId).ToDictionary(x => x.Key, x => x.ToList());

            var rows = transactions.Select(tx =>
            {
                requirementLookup.TryGetValue(tx.Id, out var txRequirements);
                decisionLookup.TryGetValue(tx.Id, out var txDecisions);
                overrideLookup.TryGetValue(tx.Id, out var txOverrides);
                return new
                {
                    tx.Id,
                    tx.TrustAccountId,
                    tx.Type,
                    tx.DisbursementClass,
                    tx.Amount,
                    tx.Status,
                    tx.CreatedBy,
                    tx.ApprovedBy,
                    tx.CreatedAt,
                    tx.ApprovedAt,
                    requirements = (txRequirements ?? []).Select(r => new { r.RequirementType, r.RequiredCount, r.SatisfiedCount, r.Status, r.Summary }).ToList(),
                    decisions = (txDecisions ?? []).Select(d => new { d.ActorUserId, d.ActorRole, d.DecisionType, d.Notes, d.Reason, d.CreatedAt }).ToList(),
                    overrides = (txOverrides ?? []).Select(o => new { o.ActorUserId, o.ActorRole, o.Reason, o.CreatedAt }).ToList()
                };
            }).ToList();

            var csvRows = rows.Select(row => new Dictionary<string, object?>
            {
                ["trustTransactionId"] = row.Id,
                ["trustAccountId"] = row.TrustAccountId,
                ["transactionType"] = row.Type,
                ["disbursementClass"] = row.DisbursementClass,
                ["amount"] = row.Amount,
                ["status"] = row.Status,
                ["createdBy"] = row.CreatedBy,
                ["approvedBy"] = row.ApprovedBy,
                ["createdAt"] = row.CreatedAt,
                ["approvedAt"] = row.ApprovedAt,
                ["requirementSummary"] = string.Join(" | ", row.requirements.Select(r => $"{r.RequirementType}:{r.SatisfiedCount}/{r.RequiredCount}:{r.Status}")),
                ["decisionCount"] = row.decisions.Count,
                ["overrideCount"] = row.overrides.Count
            }).ToList();

            var payload = new
            {
                metadata = BuildMetadata("approval_register", "Trust Approval Register", scope, ctx),
                period = BuildPeriod(scope),
                totals = new
                {
                    transactionCount = rows.Count,
                    pendingCount = rows.Count(x => x.Status == "PENDING"),
                    approvedCount = rows.Count(x => x.Status == "APPROVED"),
                    rejectedCount = rows.Count(x => x.Status == "REJECTED"),
                    overrideCount = rows.Sum(x => x.overrides.Count)
                },
                transactions = rows,
                csvRows
            };

            return new ExportBuildResult(new
            {
                title = "Trust Approval Register",
                subtitle = scope.Account?.Name ?? "All trust accounts",
                rowCount = rows.Count,
                generatedAt = DateTime.UtcNow
            }, payload);
        }

        private async Task<ExportBuildResult> BuildMonthClosePacketPayloadAsync(ExportScope scope, ExportContextEnvelope ctx, CancellationToken ct)
        {
            var monthClose = scope.MonthClose;
            var packet = scope.Packet ?? throw new TrustCommandException(StatusCodes.Status400BadRequest, "Month-close export requires a reconciliation packet.");
            var steps = monthClose == null
                ? new List<TrustMonthCloseStep>()
                : await _context.TrustMonthCloseSteps.AsNoTracking().Where(x => x.TrustMonthCloseId == monthClose.Id).OrderBy(x => x.StepKey).ToListAsync(ct);
            var packetSignoffs = await _context.TrustReconciliationSignoffs.AsNoTracking().Where(x => x.TrustReconciliationPacketId == packet.Id).OrderBy(x => x.SignedAt).ToListAsync(ct);
            var outstandingItems = await _context.TrustOutstandingItems.AsNoTracking().Where(x => x.TrustReconciliationPacketId == packet.Id).OrderBy(x => x.OccurredAt).ToListAsync(ct);
            var statementImport = string.IsNullOrWhiteSpace(packet.StatementImportId)
                ? null
                : await _context.TrustStatementImports.AsNoTracking().FirstOrDefaultAsync(x => x.Id == packet.StatementImportId, ct);
            var statementLines = statementImport == null
                ? new List<TrustStatementLine>()
                : await _context.TrustStatementLines.AsNoTracking()
                    .Where(x => x.TrustStatementImportId == statementImport.Id)
                    .OrderBy(x => x.PostedAt)
                    .ThenBy(x => x.Amount)
                    .ToListAsync(ct);

            var outstandingChecks = outstandingItems
                .Where(x => string.Equals(x.ItemType, "outstanding_check", StringComparison.OrdinalIgnoreCase))
                .Select(x => new { x.OccurredAt, x.Reference, x.Description, x.Amount, x.Status, x.Source })
                .ToList();
            var depositsInTransit = outstandingItems
                .Where(x => string.Equals(x.ItemType, "deposit_in_transit", StringComparison.OrdinalIgnoreCase))
                .Select(x => new { x.OccurredAt, x.Reference, x.Description, x.Amount, x.Status, x.Source })
                .ToList();
            var manualAdjustments = outstandingItems
                .Where(x => !string.Equals(x.ItemType, "outstanding_check", StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(x.ItemType, "deposit_in_transit", StringComparison.OrdinalIgnoreCase))
                .Select(x => new { x.OccurredAt, x.ItemType, x.ImpactDirection, x.Reference, x.Description, x.Amount, x.Status, x.Source })
                .ToList();
            var packetTemplateSummary = TryReadPacketTemplateSummary(monthClose?.SummaryJson);

            var payload = new
            {
                metadata = BuildMetadata("month_close_packet", "Trust Month-End Packet", scope, ctx),
                account = scope.Account == null ? null : new { scope.Account.Id, scope.Account.Name, scope.Account.BankName, scope.Account.Jurisdiction, scope.Account.AccountType },
                monthClose = monthClose == null ? null : new
                {
                    monthClose.Id,
                    monthClose.PolicyKey,
                    monthClose.Status,
                    monthClose.PreparedBy,
                    monthClose.PreparedAt,
                    monthClose.ReviewerSignedBy,
                    monthClose.ReviewerSignedAt,
                    monthClose.ResponsibleLawyerSignedBy,
                    monthClose.ResponsibleLawyerSignedAt,
                    monthClose.OpenExceptionCount
                },
                packetTemplate = packetTemplateSummary,
                statementSummary = statementImport == null ? null : new
                {
                    statementImport.Id,
                    statementImport.Source,
                    statementImport.PeriodStart,
                    statementImport.PeriodEnd,
                    statementImport.StatementEndingBalance,
                    statementImport.LineCount,
                    matchedLineCount = statementLines.Count(x => string.Equals(x.MatchStatus, "matched", StringComparison.OrdinalIgnoreCase)),
                    unmatchedLineCount = statementLines.Count(x => string.Equals(x.MatchStatus, "unmatched", StringComparison.OrdinalIgnoreCase)),
                    ignoredLineCount = statementLines.Count(x => string.Equals(x.MatchStatus, "ignored", StringComparison.OrdinalIgnoreCase))
                },
                packet = new
                {
                    packet.Id,
                    packet.PeriodStart,
                    packet.PeriodEnd,
                    packet.StatementEndingBalance,
                    packet.AdjustedBankBalance,
                    packet.JournalBalance,
                    packet.ClientLedgerBalance,
                    packet.OutstandingDepositsTotal,
                    packet.OutstandingChecksTotal,
                    packet.OtherAdjustmentsTotal,
                    packet.ExceptionCount,
                    packet.MatchedStatementLineCount,
                    packet.UnmatchedStatementLineCount,
                    packet.IsCanonical,
                    packet.Status,
                    packet.PreparedBy,
                    packet.PreparedAt,
                    packet.Notes
                },
                steps = steps.Select(x => new { x.StepKey, x.Status, x.Notes, x.CompletedBy, x.CompletedAt }).ToList(),
                signoffChain = new
                {
                    reviewer = monthClose == null ? null : new { monthClose.ReviewerSignedBy, monthClose.ReviewerSignedAt },
                    responsibleLawyer = monthClose == null ? null : new { monthClose.ResponsibleLawyerSignedBy, monthClose.ResponsibleLawyerSignedAt },
                    packetSignoffs = packetSignoffs.Select(x => new { x.SignedBy, x.SignerRole, x.Status, x.Notes, x.SignedAt }).ToList()
                },
                statementLines = statementLines.Select(x => new
                {
                    x.PostedAt,
                    x.EffectiveAt,
                    x.Amount,
                    x.BalanceAfter,
                    x.Reference,
                    x.CheckNumber,
                    x.Description,
                    x.Counterparty,
                    x.MatchStatus,
                    x.MatchMethod,
                    x.MatchConfidence,
                    x.MatchedTrustTransactionId
                }).ToList(),
                outstandingChecks,
                depositsInTransit,
                manualAdjustments,
                exceptionSummary = new
                {
                    openCount = outstandingItems.Count(x => string.Equals(x.Status, "open", StringComparison.OrdinalIgnoreCase)),
                    totalCount = outstandingItems.Count,
                    packet.ExceptionCount
                },
                outstandingItems = outstandingItems.Select(x => new { x.ItemType, x.ImpactDirection, x.Status, x.Amount, x.Reference, x.Description, x.Source, x.OccurredAt }).ToList(),
                csvRows = outstandingItems.Select(x => new Dictionary<string, object?>
                {
                    ["occurredAt"] = x.OccurredAt,
                    ["itemType"] = x.ItemType,
                    ["impactDirection"] = x.ImpactDirection,
                    ["status"] = x.Status,
                    ["amount"] = x.Amount,
                    ["reference"] = x.Reference,
                    ["description"] = x.Description,
                    ["source"] = x.Source
                }).ToList()
            };

            return new ExportBuildResult(new
            {
                title = "Trust Month-End Packet",
                subtitle = scope.Account?.Name ?? packet.TrustAccountId,
                rowCount = outstandingItems.Count,
                generatedAt = DateTime.UtcNow
            }, payload);
        }

        private object BuildMetadata(string exportType, string exportLabel, ExportScope scope, ExportContextEnvelope ctx)
        {
            return new
            {
                exportType,
                exportLabel,
                generatedAt = DateTime.UtcNow,
                generatedBy = ctx.GeneratedBy == null ? null : new { ctx.GeneratedBy.Id, ctx.GeneratedBy.Name, ctx.GeneratedBy.Email, ctx.GeneratedBy.Role },
                firm = ctx.Firm == null ? null : new { ctx.Firm.FirmName, ctx.Firm.Address, ctx.Firm.City, ctx.Firm.State, ctx.Firm.ZipCode, ctx.Firm.Phone, ctx.Firm.Website },
                entity = ctx.Entity == null ? null : new { ctx.Entity.Id, ctx.Entity.Name, ctx.Entity.LegalName, ctx.Entity.Email, ctx.Entity.Phone },
                office = ctx.Office == null ? null : new { ctx.Office.Id, ctx.Office.Name, ctx.Office.Code, ctx.Office.Email, ctx.Office.Phone },
                responsibleLawyer = ctx.ResponsibleLawyer == null ? null : new { ctx.ResponsibleLawyer.Id, ctx.ResponsibleLawyer.Name, ctx.ResponsibleLawyer.Email, ctx.ResponsibleLawyer.Role, ctx.ResponsibleLawyer.BarNumber },
                scope = new
                {
                    trustAccountId = scope.Account?.Id,
                    clientTrustLedgerId = scope.Ledger?.Id,
                    trustMonthCloseId = scope.MonthClose?.Id,
                    trustReconciliationPacketId = scope.Packet?.Id
                }
            };
        }

        private object BuildPeriod(ExportScope scope)
        {
            return new
            {
                periodStart = scope.PeriodStart,
                periodEnd = scope.PeriodEnd
            };
        }

        private static string BuildFileName(string exportType, string format, ExportScope scope)
        {
            var stamp = (scope.PeriodEnd ?? DateTime.UtcNow.Date).ToString("yyyy-MM-dd");
            var suffix = format switch
            {
                "csv" => "csv",
                "pdf" => "pdf",
                _ => "json"
            };

            var scopeKey = scope.Ledger?.Id ?? scope.MonthClose?.Id ?? scope.Packet?.Id ?? scope.Account?.Id ?? "all";
            return $"trust-{exportType}-{scopeKey}-{stamp}.{suffix}";
        }

        private static string ResolveContentType(string format)
        {
            return format switch
            {
                "csv" => "text/csv",
                "pdf" => "application/pdf",
                _ => "application/json"
            };
        }

        private static string NormalizeExportType(string? value)
        {
            return NormalizeExportTypeOrNull(value)
                ?? throw new TrustCommandException(StatusCodes.Status400BadRequest, "Export type is required.");
        }

        private static string? NormalizeExportTypeOrNull(string? value)
        {
            var normalized = value?.Trim().ToLowerInvariant();
            return normalized switch
            {
                "account_journal" or "client_ledger" or "approval_register" or "month_close_packet" or "compliance_bundle_manifest" => normalized,
                null or "" => null,
                _ => throw new TrustCommandException(StatusCodes.Status400BadRequest, "Unsupported trust export type.")
            };
        }

        private static string NormalizeFormat(string? value)
        {
            var normalized = value?.Trim().ToLowerInvariant();
            return normalized switch
            {
                "json" or "csv" or "pdf" => normalized,
                _ => throw new TrustCommandException(StatusCodes.Status400BadRequest, "Unsupported trust export format.")
            };
        }

        private static DateTime? ToInclusiveCutoff(DateTime? value)
        {
            return value?.Date.AddDays(1).AddTicks(-1);
        }

        private static object? TryReadPacketTemplateSummary(string? summaryJson)
        {
            if (string.IsNullOrWhiteSpace(summaryJson))
            {
                return null;
            }

            try
            {
                using var document = JsonDocument.Parse(summaryJson);
                if (!document.RootElement.TryGetProperty("packetTemplate", out var packetTemplateElement))
                {
                    return null;
                }

                return JsonSerializer.Deserialize<object>(packetTemplateElement.GetRawText(), JsonOptions);
            }
            catch
            {
                return null;
            }
        }

        private ClaimsPrincipal? GetCurrentUser()
        {
            return _httpContextAccessor.HttpContext?.User;
        }

        private string? GetCurrentUserId()
        {
            var user = GetCurrentUser();
            return user?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? user?.FindFirst("sub")?.Value
                ?? user?.FindFirst("userId")?.Value
                ?? user?.Identity?.Name;
        }

        private HttpContext? GetCurrentHttpContext()
        {
            return _httpContextAccessor.HttpContext;
        }

        private static TrustComplianceExportListItemDto ToListDto(TrustComplianceExport export)
        {
            return new TrustComplianceExportListItemDto
            {
                Id = export.Id,
                ExportType = export.ExportType,
                Format = export.Format,
                Status = export.Status,
                TrustAccountId = export.TrustAccountId,
                ClientTrustLedgerId = export.ClientTrustLedgerId,
                TrustMonthCloseId = export.TrustMonthCloseId,
                TrustReconciliationPacketId = export.TrustReconciliationPacketId,
                FileName = export.FileName,
                ContentType = export.ContentType,
                SummaryJson = export.SummaryJson,
                GeneratedBy = export.GeneratedBy,
                ParentExportId = export.ParentExportId,
                IntegrityStatus = export.IntegrityStatus,
                RetentionPolicyTag = export.RetentionPolicyTag,
                RedactionProfile = export.RedactionProfile,
                GeneratedAt = export.GeneratedAt
            };
        }

        private static TrustComplianceExportDto ToDetailDto(TrustComplianceExport export)
        {
            return new TrustComplianceExportDto
            {
                Id = export.Id,
                ExportType = export.ExportType,
                Format = export.Format,
                Status = export.Status,
                TrustAccountId = export.TrustAccountId,
                ClientTrustLedgerId = export.ClientTrustLedgerId,
                TrustMonthCloseId = export.TrustMonthCloseId,
                TrustReconciliationPacketId = export.TrustReconciliationPacketId,
                FileName = export.FileName,
                ContentType = export.ContentType,
                SummaryJson = export.SummaryJson,
                GeneratedBy = export.GeneratedBy,
                GeneratedAt = export.GeneratedAt,
                PayloadJson = export.PayloadJson,
                ParentExportId = export.ParentExportId,
                IntegrityStatus = export.IntegrityStatus,
                RetentionPolicyTag = export.RetentionPolicyTag,
                RedactionProfile = export.RedactionProfile,
                ProvenanceJson = export.ProvenanceJson
            };
        }

        private sealed record ExportScope(
            TrustBankAccount? Account,
            ClientTrustLedger? Ledger,
            TrustMonthClose? MonthClose,
            TrustReconciliationPacket? Packet,
            DateTime? PeriodStart,
            DateTime? PeriodEnd);

        private sealed record ExportContextEnvelope(
            FirmSettings? Firm,
            FirmEntity? Entity,
            Office? Office,
            User? ResponsibleLawyer,
            User? GeneratedBy);

        private sealed record ExportBuildResult(object Summary, object Payload);
    }
}
