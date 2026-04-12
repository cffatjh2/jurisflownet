using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using Task = System.Threading.Tasks.Task;

namespace JurisFlow.Server.Services
{
    public record AuditLogChainState(long Sequence, string? Hash);

    public record AuditLogIntegrityResult(
        bool IsValid,
        long CheckedCount,
        long SkippedCount,
        long? FailedSequence,
        string? FailureReason);

    public class AuditLogIntegrityService
    {
        private const int MinKeySizeBytes = 32;
        private readonly JurisFlowDbContext _context;
        private readonly byte[]? _key;

        public bool Immutable { get; }
        public bool IsConfigured => _key != null && _key.Length >= MinKeySizeBytes;
        public string Algorithm => "HMACSHA256";

        public AuditLogIntegrityService(JurisFlowDbContext context, IConfiguration configuration, ILogger<AuditLogIntegrityService> logger)
        {
            _context = context;
            Immutable = configuration.GetValue("Security:AuditLogImmutable", false);

            var rawKey = configuration["Security:AuditLogKey"];
            if (!string.IsNullOrWhiteSpace(rawKey))
            {
                try
                {
                    _key = Convert.FromBase64String(rawKey);
                }
                catch (FormatException ex)
                {
                    logger.LogError(ex, "Audit log integrity key is not valid base64.");
                    _key = null;
                }
            }

            if (Immutable && (_key == null || _key.Length < MinKeySizeBytes))
            {
                throw new InvalidOperationException("Audit log immutability is enabled but the key is missing or invalid.");
            }
        }

        public async Task PrepareAsync(AuditLog audit)
        {
            if (!IsConfigured)
            {
                return;
            }

            PrepareBatch(new[] { audit }, await GetLatestStateAsync());
        }

        public async Task<AuditLogChainState> GetLatestStateAsync(CancellationToken ct = default)
        {
            if (!IsConfigured)
            {
                return new AuditLogChainState(0, null);
            }

            var last = await _context.AuditLogs
                .AsNoTracking()
                .OrderByDescending(a => a.Sequence)
                .Select(a => new
                {
                    a.Sequence,
                    a.Hash
                })
                .FirstOrDefaultAsync(ct);

            return new AuditLogChainState(last?.Sequence ?? 0, last?.Hash);
        }

        public void PrepareBatch(IEnumerable<AuditLog> audits, AuditLogChainState initialState)
        {
            if (!IsConfigured)
            {
                return;
            }

            var sequence = initialState.Sequence;
            var previousHash = initialState.Hash;

            foreach (var audit in audits)
            {
                sequence += 1;
                audit.Sequence = sequence;
                audit.PreviousHash = previousHash;
                audit.HashAlgorithm = Algorithm;
                audit.Hash = ComputeHash(audit);
                previousHash = audit.Hash;
            }
        }

        public string ComputeHash(AuditLog audit)
        {
            var key = RequireKey();
            var payload = string.Join("|",
                audit.Sequence,
                audit.CreatedAt.ToString("O"),
                Normalize(audit.UserId),
                Normalize(audit.ClientId),
                Normalize(audit.TenantId),
                Normalize(audit.Role),
                Normalize(audit.Action),
                Normalize(audit.Entity),
                Normalize(audit.EntityId),
                Normalize(audit.Details),
                Normalize(audit.IpAddress),
                Normalize(audit.UserAgent),
                Normalize(audit.PreviousHash));

            using var hmac = new HMACSHA256(key);
            var bytes = Encoding.UTF8.GetBytes(payload);
            return Convert.ToHexString(hmac.ComputeHash(bytes)).ToLowerInvariant();
        }

        public async Task<AuditLogIntegrityResult> VerifyAsync(int? limit = null)
        {
            if (!IsConfigured)
            {
                return new AuditLogIntegrityResult(false, 0, 0, null, "Integrity key is not configured.");
            }

            IQueryable<AuditLog> query = _context.AuditLogs
                .AsNoTracking()
                .OrderBy(a => a.Sequence);

            if (limit.HasValue && limit.Value > 0)
            {
                query = query.Take(limit.Value);
            }

            var logs = await query.ToListAsync();
            string? previousHash = null;
            long checkedCount = 0;
            long skippedCount = 0;

            foreach (var log in logs)
            {
                if (log.Sequence <= 0 || string.IsNullOrWhiteSpace(log.Hash))
                {
                    skippedCount += 1;
                    continue;
                }

                if (log.PreviousHash != previousHash)
                {
                    return new AuditLogIntegrityResult(false, checkedCount, skippedCount, log.Sequence, "Previous hash mismatch.");
                }

                var expected = ComputeHash(log);
                if (!string.Equals(expected, log.Hash, StringComparison.OrdinalIgnoreCase))
                {
                    return new AuditLogIntegrityResult(false, checkedCount, skippedCount, log.Sequence, "Hash mismatch.");
                }

                previousHash = log.Hash;
                checkedCount += 1;
            }

            return new AuditLogIntegrityResult(true, checkedCount, skippedCount, null, null);
        }

        private static string Normalize(string? value)
        {
            return value ?? string.Empty;
        }

        private byte[] RequireKey()
        {
            if (_key == null || _key.Length < MinKeySizeBytes)
            {
                throw new InvalidOperationException("Audit log integrity key is missing or invalid.");
            }
            return _key;
        }
    }
}
