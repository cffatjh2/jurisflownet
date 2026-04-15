using System.Security.Claims;
using System.Text.Json;
using JurisFlow.Server.Contracts;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace JurisFlow.Server.Services
{
    public sealed class TrustRecoveryService
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };

        private readonly JurisFlowDbContext _context;
        private readonly TrustAccountingService _trustAccountingService;
        private readonly TrustComplianceExportService _trustExportService;
        private readonly TrustBundleIntegrityService _trustBundleIntegrityService;
        private readonly TrustActionAuthorizationService _authorization;
        private readonly AuditLogger _auditLogger;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public TrustRecoveryService(
            JurisFlowDbContext context,
            TrustAccountingService trustAccountingService,
            TrustComplianceExportService trustExportService,
            TrustBundleIntegrityService trustBundleIntegrityService,
            TrustActionAuthorizationService authorization,
            AuditLogger auditLogger,
            IHttpContextAccessor httpContextAccessor)
        {
            _context = context;
            _trustAccountingService = trustAccountingService;
            _trustExportService = trustExportService;
            _trustBundleIntegrityService = trustBundleIntegrityService;
            _authorization = authorization;
            _auditLogger = auditLogger;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task<TrustAsOfProjectionRecoveryResult> GenerateAsOfProjectionRecoveryAsync(TrustAsOfProjectionRecoveryRequest? request, CancellationToken ct = default)
        {
            _authorization.EnsureAllowed(TrustActionKeys.RebuildProjections, GetCurrentUser());

            var effectiveAsOfUtc = request?.AsOfUtc?.ToUniversalTime() ?? DateTime.UtcNow;
            var isHistoricalPreview = request?.AsOfUtc.HasValue == true;
            if (request?.CommitProjectionRepair == true && isHistoricalPreview)
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Historical as-of recovery can only preview. Current projection repair must run without an explicit as-of timestamp.");
            }

            TrustProjectionRebuildResult? repairResult = null;
            if (request?.CommitProjectionRepair == true)
            {
                repairResult = await _trustAccountingService.RebuildProjectionsAsync(new TrustProjectionRebuildRequest
                {
                    TrustAccountId = request.TrustAccountId,
                    OnlyIfDrifted = request.OnlyIfDrifted
                }, ct);
                effectiveAsOfUtc = repairResult.RebuiltAt;
            }

            var targetTrustAccountId = request?.TrustAccountId;
            var accounts = await _context.TrustBankAccounts
                .AsNoTracking()
                .Where(a => string.IsNullOrWhiteSpace(targetTrustAccountId) || a.Id == targetTrustAccountId)
                .OrderBy(a => a.Name)
                .ToListAsync(ct);

            var response = new TrustAsOfProjectionRecoveryResult
            {
                GeneratedAtUtc = DateTime.UtcNow,
                EffectiveAsOfUtc = effectiveAsOfUtc,
                CommitProjectionRepair = request?.CommitProjectionRepair == true,
                HistoricalPreviewOnly = isHistoricalPreview,
                RepairedTrustAccountIds = repairResult?.TrustAccountIds ?? []
            };

            if (accounts.Count == 0)
            {
                return response;
            }

            var accountIds = accounts.Select(a => a.Id).ToList();
            var ledgers = await _context.ClientTrustLedgers
                .AsNoTracking()
                .Where(l => accountIds.Contains(l.TrustAccountId))
                .OrderBy(l => l.ClientId)
                .ThenBy(l => l.MatterId)
                .ToListAsync(ct);

            var accountJournal = await _context.TrustJournalEntries
                .AsNoTracking()
                .Where(j => accountIds.Contains(j.TrustAccountId) && j.EffectiveAt <= effectiveAsOfUtc)
                .GroupBy(j => j.TrustAccountId)
                .Select(g => new
                {
                    TrustAccountId = g.Key,
                    CurrentBalance = g.Sum(x => x.Amount),
                    ClearedBalance = g.Where(x => x.AvailabilityClass == "cleared").Sum(x => x.Amount),
                    UnclearedBalance = g.Where(x => x.AvailabilityClass == "uncleared").Sum(x => x.Amount)
                })
                .ToListAsync(ct);

            var ledgerJournal = await _context.TrustJournalEntries
                .AsNoTracking()
                .Where(j => j.ClientTrustLedgerId != null && accountIds.Contains(j.TrustAccountId) && j.EffectiveAt <= effectiveAsOfUtc)
                .GroupBy(j => j.ClientTrustLedgerId!)
                .Select(g => new
                {
                    LedgerId = g.Key,
                    RunningBalance = g.Sum(x => x.Amount),
                    ClearedBalance = g.Where(x => x.AvailabilityClass == "cleared").Sum(x => x.Amount),
                    UnclearedBalance = g.Where(x => x.AvailabilityClass == "uncleared").Sum(x => x.Amount)
                })
                .ToListAsync(ct);

            var accountJournalMap = accountJournal.ToDictionary(
                item => item.TrustAccountId,
                item => (
                    NormalizeMoney(item.CurrentBalance),
                    NormalizeMoney(item.ClearedBalance),
                    NormalizeMoney(item.UnclearedBalance)),
                StringComparer.Ordinal);

            var ledgerJournalMap = ledgerJournal.ToDictionary(
                item => item.LedgerId,
                item => (
                    NormalizeMoney(item.RunningBalance),
                    NormalizeMoney(item.ClearedBalance),
                    NormalizeMoney(item.UnclearedBalance)),
                StringComparer.Ordinal);

            foreach (var account in accounts)
            {
                var expected = accountJournalMap.TryGetValue(account.Id, out var values)
                    ? values
                    : (0m, 0m, 0m);
                var expectedAvailable = NormalizeMoney(Math.Max(0m, expected.Item2));
                var accountLedgers = ledgers
                    .Where(l => string.Equals(l.TrustAccountId, account.Id, StringComparison.Ordinal))
                    .Select(ledger =>
                    {
                        var ledgerExpected = ledgerJournalMap.TryGetValue(ledger.Id, out var ledgerValues)
                            ? ledgerValues
                            : (0m, 0m, 0m);
                        var ledgerExpectedAvailable = NormalizeMoney(Math.Max(0m, ledgerExpected.Item2 - ledger.HoldAmount));
                        return new TrustAsOfProjectionRecoveryLedgerDto
                        {
                            LedgerId = ledger.Id,
                            ClientId = ledger.ClientId,
                            MatterId = ledger.MatterId,
                            PersistedRunningBalance = NormalizeMoney(ledger.RunningBalance),
                            PersistedClearedBalance = NormalizeMoney(ledger.ClearedBalance),
                            PersistedUnclearedBalance = NormalizeMoney(ledger.UnclearedBalance),
                            PersistedAvailableToDisburse = NormalizeMoney(ledger.AvailableToDisburse),
                            AsOfRunningBalance = ledgerExpected.Item1,
                            AsOfClearedBalance = ledgerExpected.Item2,
                            AsOfUnclearedBalance = ledgerExpected.Item3,
                            AsOfAvailableToDisburse = ledgerExpectedAvailable,
                            HasCurrentProjectionDrift =
                                HasProjectionDrift(ledger.RunningBalance, ledgerExpected.Item1) ||
                                HasProjectionDrift(ledger.ClearedBalance, ledgerExpected.Item2) ||
                                HasProjectionDrift(ledger.UnclearedBalance, ledgerExpected.Item3) ||
                                HasProjectionDrift(ledger.AvailableToDisburse, ledgerExpectedAvailable)
                        };
                    })
                    .ToList();

                response.Accounts.Add(new TrustAsOfProjectionRecoveryAccountDto
                {
                    TrustAccountId = account.Id,
                    TrustAccountName = account.Name,
                    PersistedCurrentBalance = NormalizeMoney(account.CurrentBalance),
                    PersistedClearedBalance = NormalizeMoney(account.ClearedBalance),
                    PersistedUnclearedBalance = NormalizeMoney(account.UnclearedBalance),
                    PersistedAvailableDisbursementCapacity = NormalizeMoney(account.AvailableDisbursementCapacity),
                    AsOfCurrentBalance = expected.Item1,
                    AsOfClearedBalance = expected.Item2,
                    AsOfUnclearedBalance = expected.Item3,
                    AsOfAvailableDisbursementCapacity = expectedAvailable,
                    HasCurrentProjectionDrift =
                        HasProjectionDrift(account.CurrentBalance, expected.Item1) ||
                        HasProjectionDrift(account.ClearedBalance, expected.Item2) ||
                        HasProjectionDrift(account.UnclearedBalance, expected.Item3) ||
                        HasProjectionDrift(account.AvailableDisbursementCapacity, expectedAvailable) ||
                        accountLedgers.Any(l => l.HasCurrentProjectionDrift),
                    Ledgers = accountLedgers
                });
            }

            response.AccountCount = response.Accounts.Count;
            response.LedgerCount = response.Accounts.Sum(a => a.Ledgers.Count);
            response.DriftedAccountCount = response.Accounts.Count(a => a.HasCurrentProjectionDrift);
            response.DriftedLedgerCount = response.Accounts.Sum(a => a.Ledgers.Count(l => l.HasCurrentProjectionDrift));

            await LogAsync(
                "trust.recovery.as_of",
                "TrustProjection",
                request?.TrustAccountId,
                $"AsOf={effectiveAsOfUtc:O}, Accounts={response.AccountCount}, Historical={response.HistoricalPreviewOnly}, Commit={response.CommitProjectionRepair}");

            return response;
        }

        public async Task<TrustPacketRegenerationResult> RegeneratePacketAsync(TrustPacketRegenerationRequest request, CancellationToken ct = default)
        {
            _authorization.EnsureAllowed(TrustActionKeys.PrepareReconciliationPacket, GetCurrentUser());

            if (string.IsNullOrWhiteSpace(request.Reason))
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Packet regeneration reason is required.");
            }

            var scope = await ResolveBundleScopeAsync(
                request.TrustAccountId,
                request.TrustMonthCloseId,
                request.TrustReconciliationPacketId,
                request.PeriodStart,
                request.PeriodEnd,
                ct);

            TrustReconciliationPacket packet;
            if (scope.Packet != null)
            {
                packet = await _trustAccountingService.SupersedeReconciliationPacketAsync(scope.Packet.Id, new TrustReconciliationPacketSupersedeDto
                {
                    Reason = request.Reason.Trim(),
                    Notes = request.Notes,
                    StatementImportId = string.IsNullOrWhiteSpace(request.StatementImportId) ? scope.Packet.StatementImportId : request.StatementImportId,
                    StatementEndingBalance = request.StatementEndingBalance ?? scope.Packet.StatementEndingBalance
                }, ct);
            }
            else
            {
                if (scope.Account == null || !scope.PeriodStart.HasValue || !scope.PeriodEnd.HasValue)
                {
                    throw new TrustCommandException(StatusCodes.Status400BadRequest, "Packet regeneration requires an account and period when no source packet exists.");
                }

                packet = await _trustAccountingService.GenerateReconciliationPacketAsync(new TrustReconciliationPacketCreateDto
                {
                    TrustAccountId = scope.Account.Id,
                    PeriodStart = scope.PeriodStart.Value,
                    PeriodEnd = scope.PeriodEnd.Value,
                    StatementImportId = request.StatementImportId,
                    StatementEndingBalance = request.StatementEndingBalance,
                    Notes = request.Notes,
                    ForceNewVersion = true,
                    SupersedeReason = request.Reason.Trim()
                }, ct);
            }

            TrustMonthCloseDto? close = null;
            if (request.AutoPrepareMonthClose)
            {
                close = await _trustAccountingService.PrepareMonthCloseAsync(new TrustMonthClosePrepareDto
                {
                    TrustAccountId = packet.TrustAccountId,
                    PeriodStart = packet.PeriodStart,
                    PeriodEnd = packet.PeriodEnd,
                    ReconciliationPacketId = packet.Id,
                    AutoGeneratePacket = false,
                    StatementEndingBalance = request.StatementEndingBalance ?? packet.StatementEndingBalance,
                    Notes = request.Notes
                }, ct);
            }

            await LogAsync(
                "trust.recovery.packet_regenerate",
                nameof(TrustReconciliationPacket),
                packet.Id,
                $"SourcePacket={scope.Packet?.Id}, Version={packet.VersionNumber}, MonthClose={close?.Id}");

            return new TrustPacketRegenerationResult
            {
                SourcePacketId = scope.Packet?.Id,
                PacketId = packet.Id,
                PacketVersionNumber = packet.VersionNumber,
                PacketStatus = packet.Status,
                TrustAccountId = packet.TrustAccountId,
                TrustMonthCloseId = close?.Id,
                TrustMonthCloseStatus = close?.Status,
                GeneratedAtUtc = DateTime.UtcNow
            };
        }

        public async Task<TrustComplianceBundleResult> GenerateComplianceBundleAsync(TrustComplianceBundleRequest request, CancellationToken ct = default)
        {
            _authorization.EnsureAllowed(TrustActionKeys.ExportData, GetCurrentUser());

            var scope = await ResolveBundleScopeAsync(
                request.TrustAccountId,
                request.TrustMonthCloseId,
                request.TrustReconciliationPacketId,
                request.PeriodStart,
                request.PeriodEnd,
                ct);

            if (scope.Account == null)
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Compliance bundle requires a trust account scope.");
            }

            if (scope.Packet == null && scope.MonthClose == null)
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Compliance bundle requires a month close or reconciliation packet scope.");
            }

            var effectivePeriodStart = scope.PeriodStart;
            var effectivePeriodEnd = scope.PeriodEnd;
            var exports = new List<TrustComplianceExportDto>();
            var evidenceReferences = await BuildBundleEvidenceReferencesAsync(scope, ct);
            var monthCloseId = scope.MonthClose?.Id;
            var packetId = scope.Packet?.Id;
            var parentManifestId = await _context.TrustComplianceExports
                .AsNoTracking()
                .Where(e =>
                    e.ExportType == "compliance_bundle_manifest" &&
                    e.TrustAccountId == scope.Account.Id &&
                    e.TrustMonthCloseId == monthCloseId &&
                    e.TrustReconciliationPacketId == packetId)
                .OrderByDescending(e => e.GeneratedAt)
                .Select(e => e.Id)
                .FirstOrDefaultAsync(ct);

            exports.Add(await _trustExportService.GenerateExportAsync(new TrustComplianceExportRequest
            {
                ExportType = "month_close_packet",
                Format = "pdf",
                TrustAccountId = scope.Account.Id,
                TrustMonthCloseId = scope.MonthClose?.Id,
                TrustReconciliationPacketId = scope.Packet?.Id,
                PeriodStart = effectivePeriodStart,
                PeriodEnd = effectivePeriodEnd
            }, ct));

            if (request.IncludeJsonPacket)
            {
                exports.Add(await _trustExportService.GenerateExportAsync(new TrustComplianceExportRequest
                {
                    ExportType = "month_close_packet",
                    Format = "json",
                    TrustAccountId = scope.Account.Id,
                    TrustMonthCloseId = scope.MonthClose?.Id,
                    TrustReconciliationPacketId = scope.Packet?.Id,
                    PeriodStart = effectivePeriodStart,
                    PeriodEnd = effectivePeriodEnd
                }, ct));
            }

            if (request.IncludeAccountJournalCsv)
            {
                exports.Add(await _trustExportService.GenerateExportAsync(new TrustComplianceExportRequest
                {
                    ExportType = "account_journal",
                    Format = "csv",
                    TrustAccountId = scope.Account.Id,
                    PeriodStart = effectivePeriodStart,
                    PeriodEnd = effectivePeriodEnd
                }, ct));
            }

            if (request.IncludeApprovalRegisterCsv)
            {
                exports.Add(await _trustExportService.GenerateExportAsync(new TrustComplianceExportRequest
                {
                    ExportType = "approval_register",
                    Format = "csv",
                    TrustAccountId = scope.Account.Id,
                    PeriodStart = effectivePeriodStart,
                    PeriodEnd = effectivePeriodEnd
                }, ct));
            }

            if (request.IncludeClientLedgerCards)
            {
                var ledgerIds = await ResolveBundleLedgerIdsAsync(scope.Account.Id, effectivePeriodStart, effectivePeriodEnd, ct);
                foreach (var ledgerId in ledgerIds)
                {
                    exports.Add(await _trustExportService.GenerateExportAsync(new TrustComplianceExportRequest
                    {
                        ExportType = "client_ledger",
                        Format = "csv",
                        TrustAccountId = scope.Account.Id,
                        ClientTrustLedgerId = ledgerId,
                        PeriodStart = effectivePeriodStart,
                        PeriodEnd = effectivePeriodEnd
                    }, ct));
                }
            }

            var manifest = new TrustComplianceExport
            {
                Id = Guid.NewGuid().ToString(),
                ExportType = "compliance_bundle_manifest",
                Format = "json",
                Status = "completed",
                TrustAccountId = scope.Account.Id,
                TrustMonthCloseId = scope.MonthClose?.Id,
                TrustReconciliationPacketId = scope.Packet?.Id,
                FileName = BuildManifestFileName(scope),
                ContentType = "application/json",
                IntegrityStatus = "unsigned",
                ParentExportId = parentManifestId,
                RetentionPolicyTag = "trust_default",
                RedactionProfile = "internal_unredacted",
                SummaryJson = JsonSerializer.Serialize(new
                {
                    title = "Trust Compliance Bundle",
                    exportCount = exports.Count,
                    accountName = scope.Account.Name,
                    periodStart = effectivePeriodStart,
                    periodEnd = effectivePeriodEnd
                }, JsonOptions),
                PayloadJson = JsonSerializer.Serialize(new
                {
                    generatedAtUtc = DateTime.UtcNow,
                    notes = request.Notes,
                    metadata = new
                    {
                        trustAccountId = scope.Account.Id,
                        trustAccountName = scope.Account.Name,
                        trustMonthCloseId = scope.MonthClose?.Id,
                        trustReconciliationPacketId = scope.Packet?.Id,
                        periodStart = effectivePeriodStart,
                        periodEnd = effectivePeriodEnd
                    },
                    evidenceReferences,
                    exports = exports.Select(ToBundleExportReference).ToList()
                }, JsonOptions),
                ProvenanceJson = JsonSerializer.Serialize(new
                {
                    generatedFrom = new
                    {
                        scope.Account.Id,
                        scope.Account.Name,
                        trustMonthCloseId = scope.MonthClose?.Id,
                        trustReconciliationPacketId = scope.Packet?.Id,
                        periodStart = effectivePeriodStart,
                        periodEnd = effectivePeriodEnd
                    },
                    evidenceReferences,
                    exportIds = exports.Select(e => e.Id).ToList()
                }, JsonOptions),
                GeneratedBy = GetCurrentUserId(),
                GeneratedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.TrustComplianceExports.Add(manifest);
            await _context.SaveChangesAsync(ct);

            await LogAsync(
                "trust.recovery.compliance_bundle",
                nameof(TrustComplianceExport),
                manifest.Id,
                $"Account={scope.Account.Id}, Exports={exports.Count}, Packet={scope.Packet?.Id}, Close={scope.MonthClose?.Id}");

            var integrity = await _trustBundleIntegrityService.GetBundleIntegrityAsync(manifest.Id, ct);

            return new TrustComplianceBundleResult
            {
                GeneratedAtUtc = manifest.GeneratedAt,
                ManifestExportId = manifest.Id,
                ManifestFileName = manifest.FileName,
                TrustAccountId = scope.Account.Id,
                TrustMonthCloseId = scope.MonthClose?.Id,
                TrustReconciliationPacketId = scope.Packet?.Id,
                ExportCount = exports.Count,
                Exports = exports.Select(ToListItemDto).ToList(),
                Integrity = integrity
            };
        }

        private async Task<IReadOnlyList<string>> ResolveBundleLedgerIdsAsync(string trustAccountId, DateTime? periodStart, DateTime? periodEnd, CancellationToken ct)
        {
            var effectiveTo = periodEnd?.Date.AddDays(1).AddTicks(-1);
            var query = _context.ClientTrustLedgers.AsNoTracking().Where(l => l.TrustAccountId == trustAccountId);
            var ledgers = await query.ToListAsync(ct);
            if (ledgers.Count == 0)
            {
                return [];
            }

            var ledgerIds = ledgers.Select(l => l.Id).ToList();
            var activeLedgerIds = await _context.TrustJournalEntries.AsNoTracking()
                .Where(j => j.ClientTrustLedgerId != null && ledgerIds.Contains(j.ClientTrustLedgerId))
                .Where(j => !periodStart.HasValue || j.EffectiveAt >= periodStart.Value)
                .Where(j => !effectiveTo.HasValue || j.EffectiveAt <= effectiveTo.Value)
                .Select(j => j.ClientTrustLedgerId!)
                .Distinct()
                .ToListAsync(ct);

            var activeSet = new HashSet<string>(activeLedgerIds, StringComparer.Ordinal);
            return ledgers
                .Where(l =>
                    activeSet.Contains(l.Id) ||
                    NormalizeMoney(l.RunningBalance) != 0m ||
                    NormalizeMoney(l.ClearedBalance) != 0m ||
                    NormalizeMoney(l.UnclearedBalance) != 0m)
                .OrderBy(l => l.ClientId, StringComparer.Ordinal)
                .ThenBy(l => l.MatterId, StringComparer.Ordinal)
                .Select(l => l.Id)
                .ToList();
        }

        private async Task<RecoveryScope> ResolveBundleScopeAsync(
            string? trustAccountId,
            string? trustMonthCloseId,
            string? trustReconciliationPacketId,
            DateTime? periodStart,
            DateTime? periodEnd,
            CancellationToken ct)
        {
            TrustMonthClose? monthClose = null;
            TrustReconciliationPacket? packet = null;

            if (!string.IsNullOrWhiteSpace(trustMonthCloseId))
            {
                monthClose = await _context.TrustMonthCloses.AsNoTracking().FirstOrDefaultAsync(x => x.Id == trustMonthCloseId, ct);
                if (monthClose == null)
                {
                    throw new TrustCommandException(StatusCodes.Status404NotFound, "Month close not found.");
                }
            }

            if (!string.IsNullOrWhiteSpace(trustReconciliationPacketId))
            {
                packet = await _context.TrustReconciliationPackets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == trustReconciliationPacketId, ct);
                if (packet == null)
                {
                    throw new TrustCommandException(StatusCodes.Status404NotFound, "Reconciliation packet not found.");
                }
            }

            if (packet == null && monthClose?.ReconciliationPacketId != null)
            {
                packet = await _context.TrustReconciliationPackets.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == monthClose.ReconciliationPacketId, ct);
            }

            var resolvedTrustAccountId = trustAccountId
                ?? monthClose?.TrustAccountId
                ?? packet?.TrustAccountId;

            var normalizedPeriodStart = periodStart?.Date
                ?? monthClose?.PeriodStart.Date
                ?? packet?.PeriodStart.Date;
            var normalizedPeriodEnd = periodEnd?.Date
                ?? monthClose?.PeriodEnd.Date
                ?? packet?.PeriodEnd.Date;

            if (packet == null &&
                !string.IsNullOrWhiteSpace(resolvedTrustAccountId) &&
                normalizedPeriodStart.HasValue &&
                normalizedPeriodEnd.HasValue)
            {
                packet = await _context.TrustReconciliationPackets.AsNoTracking()
                    .Where(x => x.TrustAccountId == resolvedTrustAccountId &&
                                x.PeriodStart == normalizedPeriodStart.Value &&
                                x.PeriodEnd == normalizedPeriodEnd.Value &&
                                x.IsCanonical)
                    .OrderByDescending(x => x.VersionNumber)
                    .FirstOrDefaultAsync(ct);
            }

            if (monthClose == null && packet != null)
            {
                monthClose = await _context.TrustMonthCloses.AsNoTracking()
                    .Where(x => x.ReconciliationPacketId == packet.Id && x.IsCanonical)
                    .OrderByDescending(x => x.VersionNumber)
                    .FirstOrDefaultAsync(ct);
            }

            TrustBankAccount? account = null;
            if (!string.IsNullOrWhiteSpace(resolvedTrustAccountId))
            {
                account = await _context.TrustBankAccounts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == resolvedTrustAccountId, ct);
            }

            return new RecoveryScope(account, monthClose, packet, normalizedPeriodStart, normalizedPeriodEnd);
        }

        private static object ToBundleExportReference(TrustComplianceExportDto export)
        {
            return new
            {
                export.Id,
                export.ExportType,
                export.Format,
                export.Status,
                export.TrustAccountId,
                export.ClientTrustLedgerId,
                export.TrustMonthCloseId,
                export.TrustReconciliationPacketId,
                export.FileName,
                export.ContentType,
                export.GeneratedBy,
                export.GeneratedAt
            };
        }

        private async Task<IReadOnlyList<object>> BuildBundleEvidenceReferencesAsync(RecoveryScope scope, CancellationToken ct)
        {
            var references = new List<object>();
            if (scope.Packet == null || string.IsNullOrWhiteSpace(scope.Packet.StatementImportId))
            {
                return references;
            }

            var statement = await _context.TrustStatementImports.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == scope.Packet.StatementImportId, ct);
            if (statement == null)
            {
                return references;
            }

            references.Add(new
            {
                statementImportId = statement.Id,
                statement.Source,
                statement.SourceFileName,
                statement.SourceFileHash,
                statement.SourceEvidenceKey,
                statement.SourceFileSizeBytes,
                statement.ImportedAt
            });

            if (!string.IsNullOrWhiteSpace(statement.SourceFileHash))
            {
                var evidence = await _context.TrustEvidenceFiles.AsNoTracking()
                    .Where(e => e.TrustAccountId == statement.TrustAccountId && e.FileHash == statement.SourceFileHash)
                    .OrderByDescending(e => e.RegisteredAt)
                    .FirstOrDefaultAsync(ct);
                if (evidence != null)
                {
                    references.Add(new
                    {
                        evidenceFileId = evidence.Id,
                        evidence.FileName,
                        evidence.FileHash,
                        evidence.EvidenceKey,
                        evidence.Status,
                        evidence.RegisteredAt
                    });
                }
            }

            return references;
        }

        private static TrustComplianceExportListItemDto ToListItemDto(TrustComplianceExportDto export)
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
                GeneratedAt = export.GeneratedAt
            };
        }

        private string BuildManifestFileName(RecoveryScope scope)
        {
            var stamp = (scope.PeriodEnd ?? DateTime.UtcNow.Date).ToString("yyyy-MM-dd");
            var scopeKey = scope.MonthClose?.Id ?? scope.Packet?.Id ?? scope.Account?.Id ?? "all";
            return $"trust-compliance_bundle_manifest-{scopeKey}-{stamp}.json";
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

        private async Task LogAsync(string action, string entityType, string? entityId, string detail)
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext == null)
            {
                return;
            }

            await _auditLogger.LogAsync(httpContext, action, entityType, entityId, detail);
        }

        private static bool HasProjectionDrift(decimal projected, decimal expected)
        {
            return NormalizeMoney(projected) != NormalizeMoney(expected);
        }

        private static decimal NormalizeMoney(decimal value)
        {
            return decimal.Round(value, 2, MidpointRounding.AwayFromZero);
        }

        private sealed record RecoveryScope(
            TrustBankAccount? Account,
            TrustMonthClose? MonthClose,
            TrustReconciliationPacket? Packet,
            DateTime? PeriodStart,
            DateTime? PeriodEnd);
    }
}
