using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JurisFlow.Server.Contracts;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace JurisFlow.Server.Services
{
    public sealed class TrustStatementIngestionService
    {
        private readonly JurisFlowDbContext _context;
        private readonly AuditLogger _auditLogger;
        private readonly TrustActionAuthorizationService _authorization;
        private readonly TrustAccountingService _trustAccountingService;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public TrustStatementIngestionService(
            JurisFlowDbContext context,
            AuditLogger auditLogger,
            TrustActionAuthorizationService authorization,
            TrustAccountingService trustAccountingService,
            IHttpContextAccessor httpContextAccessor)
        {
            _context = context;
            _auditLogger = auditLogger;
            _authorization = authorization;
            _trustAccountingService = trustAccountingService;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task<IReadOnlyList<TrustEvidenceFile>> GetEvidenceFilesAsync(string? trustAccountId = null, bool includeHistory = false, CancellationToken ct = default)
        {
            _authorization.EnsureAllowed(TrustActionKeys.ImportStatement, GetCurrentUser());

            var query = _context.TrustEvidenceFiles.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(trustAccountId))
            {
                query = query.Where(x => x.TrustAccountId == trustAccountId);
            }

            if (!includeHistory)
            {
                query = query.Where(x => x.Status != "superseded");
            }

            return await query
                .OrderByDescending(x => x.PeriodEnd)
                .ThenByDescending(x => x.RegisteredAt)
                .Take(120)
                .ToListAsync(ct);
        }

        public async Task<IReadOnlyList<TrustStatementParserRun>> GetParserRunsAsync(string? trustAccountId = null, CancellationToken ct = default)
        {
            _authorization.EnsureAllowed(TrustActionKeys.ImportStatement, GetCurrentUser());

            var query = _context.TrustStatementParserRuns.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(trustAccountId))
            {
                query = query.Where(x => x.TrustAccountId == trustAccountId);
            }

            return await query
                .OrderByDescending(x => x.StartedAt)
                .Take(120)
                .ToListAsync(ct);
        }

        public async Task<TrustEvidenceFile> RegisterEvidenceFileAsync(TrustEvidenceFileRegisterRequest request, CancellationToken ct = default)
        {
            _authorization.EnsureAllowed(TrustActionKeys.ImportStatement, GetCurrentUser());

            if (string.IsNullOrWhiteSpace(request.TrustAccountId))
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Trust account is required.");
            }

            if (string.IsNullOrWhiteSpace(request.FileName))
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Evidence file name is required.");
            }

            if (string.IsNullOrWhiteSpace(request.FileHash))
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Evidence file hash is required.");
            }

            if (request.PeriodEnd.Date < request.PeriodStart.Date)
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Evidence period end must be on or after period start.");
            }

            var account = await _context.TrustBankAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == request.TrustAccountId, ct);
            if (account == null)
            {
                throw new TrustCommandException(StatusCodes.Status404NotFound, "Trust account not found.");
            }

            var actorUserId = RequireCurrentUserId();
            var periodStart = request.PeriodStart.Date;
            var periodEnd = request.PeriodEnd.Date;
            var normalizedHash = NormalizeHashToken(request.FileHash)
                ?? throw new TrustCommandException(StatusCodes.Status400BadRequest, "Evidence file hash is invalid.");
            var now = DateTime.UtcNow;
            var duplicateCandidate = await _context.TrustEvidenceFiles.AsNoTracking()
                .Where(x => x.TrustAccountId == request.TrustAccountId &&
                            x.PeriodStart == periodStart &&
                            x.PeriodEnd == periodEnd &&
                            x.FileHash == normalizedHash)
                .OrderByDescending(x => x.RegisteredAt)
                .FirstOrDefaultAsync(ct);
            var duplicateOfEvidenceFileId = duplicateCandidate == null
                ? null
                : string.IsNullOrWhiteSpace(duplicateCandidate.DuplicateOfEvidenceFileId)
                    ? duplicateCandidate.Id
                    : duplicateCandidate.DuplicateOfEvidenceFileId;
            var isDuplicate = duplicateOfEvidenceFileId != null && !request.AllowDuplicateRegistration;

            var evidence = new TrustEvidenceFile
            {
                Id = Guid.NewGuid().ToString(),
                TrustAccountId = request.TrustAccountId,
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                Source = string.IsNullOrWhiteSpace(request.Source) ? "manual_manifest" : request.Source.Trim(),
                FileName = request.FileName.Trim(),
                ContentType = request.ContentType?.Trim(),
                FileHash = normalizedHash,
                EvidenceKey = request.EvidenceKey?.Trim(),
                FileSizeBytes = request.FileSizeBytes,
                Status = isDuplicate ? "duplicate" : "registered",
                DuplicateOfEvidenceFileId = isDuplicate ? duplicateOfEvidenceFileId : null,
                RegisteredBy = actorUserId,
                Notes = request.Notes?.Trim(),
                MetadataJson = JsonSerializer.Serialize(new
                {
                    duplicateOfEvidenceFileId,
                    allowDuplicateRegistration = request.AllowDuplicateRegistration,
                    source = string.IsNullOrWhiteSpace(request.Source) ? "manual_manifest" : request.Source.Trim()
                }),
                RegisteredAt = now,
                CreatedAt = now,
                UpdatedAt = now
            };

            _context.TrustEvidenceFiles.Add(evidence);
            await _context.SaveChangesAsync(ct);

            if (!isDuplicate)
            {
                var priorEvidence = await _context.TrustEvidenceFiles
                    .Where(x =>
                        x.Id != evidence.Id &&
                        x.TrustAccountId == evidence.TrustAccountId &&
                        x.PeriodStart == evidence.PeriodStart &&
                        x.PeriodEnd == evidence.PeriodEnd &&
                        x.Status != "duplicate" &&
                        x.Status != "superseded")
                    .ToListAsync(ct);

                foreach (var prior in priorEvidence)
                {
                    prior.Status = "superseded";
                    prior.SupersededByEvidenceFileId = evidence.Id;
                    prior.SupersededBy = actorUserId;
                    prior.SupersededAt = now;
                    prior.UpdatedAt = now;
                }

                if (priorEvidence.Count > 0)
                {
                    await _context.SaveChangesAsync(ct);
                }
            }

            await LogAsync(
                isDuplicate ? "trust.evidence.register_duplicate" : "trust.evidence.register",
                nameof(TrustEvidenceFile),
                evidence.Id,
                $"Account={evidence.TrustAccountId}, PeriodEnd={evidence.PeriodEnd:yyyy-MM-dd}, Status={evidence.Status}");

            return evidence;
        }

        public async Task<TrustStatementParserRun> CreateParserRunAsync(TrustStatementParserRunCreateDto request, CancellationToken ct = default)
        {
            _authorization.EnsureAllowed(TrustActionKeys.ImportStatement, GetCurrentUser());

            if (string.IsNullOrWhiteSpace(request.TrustAccountId) || string.IsNullOrWhiteSpace(request.TrustEvidenceFileId))
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Trust account and evidence file are required.");
            }

            var evidence = await _context.TrustEvidenceFiles.FirstOrDefaultAsync(x => x.Id == request.TrustEvidenceFileId, ct);
            if (evidence == null)
            {
                throw new TrustCommandException(StatusCodes.Status404NotFound, "Trust evidence file not found.");
            }

            if (!string.Equals(evidence.TrustAccountId, request.TrustAccountId, StringComparison.Ordinal))
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Evidence file does not belong to the selected trust account.");
            }

            if (string.Equals(evidence.Status, "duplicate", StringComparison.OrdinalIgnoreCase))
            {
                throw new TrustCommandException(StatusCodes.Status409Conflict, "Duplicate evidence files are frozen and cannot create parser runs.");
            }

            if (string.Equals(evidence.Status, "superseded", StringComparison.OrdinalIgnoreCase))
            {
                throw new TrustCommandException(StatusCodes.Status409Conflict, "Superseded evidence files cannot create parser runs.");
            }

            var actorUserId = RequireCurrentUserId();
            var now = DateTime.UtcNow;
            var periodStart = request.PeriodStart?.Date ?? evidence.PeriodStart;
            var periodEnd = request.PeriodEnd?.Date ?? evidence.PeriodEnd;
            var parserRun = new TrustStatementParserRun
            {
                Id = Guid.NewGuid().ToString(),
                TrustAccountId = request.TrustAccountId,
                TrustEvidenceFileId = evidence.Id,
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                ParserKey = string.IsNullOrWhiteSpace(request.ParserKey) ? "manual_manifest_v1" : request.ParserKey.Trim(),
                Status = "running",
                AttemptCount = 1,
                Source = string.IsNullOrWhiteSpace(request.Source) ? "evidence_registry" : request.Source.Trim(),
                StartedBy = actorUserId,
                Notes = request.Notes?.Trim(),
                StartedAt = now,
                CreatedAt = now,
                UpdatedAt = now
            };

            _context.TrustStatementParserRuns.Add(parserRun);
            evidence.Status = "parsing";
            evidence.LatestParserRunId = parserRun.Id;
            evidence.UpdatedAt = now;
            await _context.SaveChangesAsync(ct);

            try
            {
                var import = await _trustAccountingService.ImportStatementAsync(new TrustStatementImportRequest
                {
                    TrustAccountId = request.TrustAccountId,
                    PeriodStart = periodStart,
                    PeriodEnd = periodEnd,
                    StatementEndingBalance = request.StatementEndingBalance,
                    Source = string.IsNullOrWhiteSpace(request.Source) ? "parser_run" : request.Source.Trim(),
                    SourceFileName = evidence.FileName,
                    SourceFileHash = evidence.FileHash,
                    SourceEvidenceKey = evidence.EvidenceKey,
                    SourceFileSizeBytes = evidence.FileSizeBytes,
                    AllowDuplicateImport = request.AllowDuplicateImport,
                    Notes = request.Notes,
                    Lines = request.Lines ?? new List<TrustStatementLineDto>()
                }, ct);

                parserRun.TrustStatementImportId = import.Id;
                parserRun.Status = string.Equals(import.Status, "duplicate", StringComparison.OrdinalIgnoreCase)
                    ? "completed_duplicate"
                    : "completed";
                parserRun.CompletedAt = DateTime.UtcNow;
                parserRun.UpdatedAt = parserRun.CompletedAt.Value;
                parserRun.SummaryJson = JsonSerializer.Serialize(new
                {
                    statementImportId = import.Id,
                    statementImportStatus = import.Status,
                    lineCount = import.LineCount,
                    matchedFingerprint = import.ImportFingerprint
                });

                evidence.Status = string.Equals(import.Status, "duplicate", StringComparison.OrdinalIgnoreCase) ? "duplicate_import" : "parsed";
                evidence.CanonicalStatementImportId = import.Id;
                evidence.UpdatedAt = parserRun.CompletedAt.Value;
                await _context.SaveChangesAsync(ct);
                await LogAsync("trust.parser_run.complete", nameof(TrustStatementParserRun), parserRun.Id, $"ImportId={import.Id}, Status={parserRun.Status}");
                return parserRun;
            }
            catch (Exception ex)
            {
                parserRun.Status = "failed";
                parserRun.ErrorMessage = ex.Message;
                parserRun.CompletedAt = DateTime.UtcNow;
                parserRun.UpdatedAt = parserRun.CompletedAt.Value;
                evidence.Status = "parse_failed";
                evidence.UpdatedAt = parserRun.CompletedAt.Value;
                await _context.SaveChangesAsync(ct);
                await LogAsync("trust.parser_run.failed", nameof(TrustStatementParserRun), parserRun.Id, ex.Message);
                throw;
            }
        }

        private ClaimsPrincipal? GetCurrentUser()
        {
            return _httpContextAccessor.HttpContext?.User;
        }

        private string RequireCurrentUserId()
        {
            var user = GetCurrentUser();
            return user?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? user?.FindFirst("sub")?.Value
                ?? user?.FindFirst("userId")?.Value
                ?? user?.Identity?.Name
                ?? throw new TrustCommandException(StatusCodes.Status401Unauthorized, "Current user could not be resolved.");
        }

        private async Task LogAsync(string action, string entity, string entityId, string details)
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext == null)
            {
                return;
            }

            await _auditLogger.LogAsync(httpContext, action, entity, entityId, details);
        }

        private static string? NormalizeHashToken(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var token = value.Trim();
            if (token.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            {
                token = token["sha256:".Length..];
            }

            token = token.Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
            if (token.Length == 64 && token.All(Uri.IsHexDigit))
            {
                return token;
            }

            var digest = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            return Convert.ToHexString(digest).ToLowerInvariant();
        }
    }
}
