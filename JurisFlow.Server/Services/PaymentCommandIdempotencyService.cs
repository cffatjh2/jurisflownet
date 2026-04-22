using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace JurisFlow.Server.Services
{
    public enum PaymentCommandIdempotencyState
    {
        Started,
        CompletedDuplicate,
        InProgressDuplicate,
        Conflict
    }

    public sealed class PaymentCommandIdempotencyResult
    {
        public PaymentCommandIdempotencyState State { get; init; }
        public PaymentCommandDeduplication Record { get; init; } = null!;
    }

    public class PaymentCommandIdempotencyService
    {
        private readonly JurisFlowDbContext _context;

        public PaymentCommandIdempotencyService(JurisFlowDbContext context)
        {
            _context = context;
        }

        public async Task<PaymentCommandIdempotencyResult> BeginAsync(
            string commandName,
            string actorUserId,
            string idempotencyKey,
            string requestFingerprint,
            string correlationId,
            CancellationToken cancellationToken)
        {
            var normalizedCommandName = commandName.Trim();
            var normalizedActorUserId = actorUserId.Trim();
            var normalizedIdempotencyKey = idempotencyKey.Trim();
            var normalizedRequestFingerprint = requestFingerprint.Trim();
            var normalizedCorrelationId = correlationId.Trim();

            var existing = await _context.PaymentCommandDeduplications
                .FirstOrDefaultAsync(
                    r => r.CommandName == normalizedCommandName && r.IdempotencyKey == normalizedIdempotencyKey,
                    cancellationToken);

            if (existing != null)
            {
                return BuildResult(existing, normalizedRequestFingerprint);
            }

            var record = new PaymentCommandDeduplication
            {
                CommandName = normalizedCommandName,
                ActorUserId = normalizedActorUserId,
                IdempotencyKey = normalizedIdempotencyKey,
                RequestFingerprint = normalizedRequestFingerprint,
                CorrelationId = normalizedCorrelationId,
                Status = "in_progress",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.PaymentCommandDeduplications.Add(record);

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
                return new PaymentCommandIdempotencyResult
                {
                    State = PaymentCommandIdempotencyState.Started,
                    Record = record
                };
            }
            catch (DbUpdateException)
            {
                _context.Entry(record).State = EntityState.Detached;
                existing = await _context.PaymentCommandDeduplications
                    .FirstOrDefaultAsync(
                        r => r.CommandName == normalizedCommandName && r.IdempotencyKey == normalizedIdempotencyKey,
                        cancellationToken);
                if (existing != null)
                {
                    return BuildResult(existing, normalizedRequestFingerprint);
                }

                throw;
            }
        }

        public async Task CompleteAsync(
            PaymentCommandDeduplication record,
            int statusCode,
            string? resultEntityType,
            string? resultEntityId,
            string? errorCode,
            string? responsePayloadJson,
            string? responseContentType,
            CancellationToken cancellationToken)
        {
            await PersistCompletionAsync(record, statusCode, resultEntityType, resultEntityId, errorCode, responsePayloadJson, responseContentType, cancellationToken);
        }

        public async Task FailAsync(
            PaymentCommandDeduplication record,
            int statusCode,
            string errorCode,
            string? responsePayloadJson,
            string? responseContentType,
            CancellationToken cancellationToken)
        {
            await PersistCompletionAsync(record, statusCode, null, null, errorCode, responsePayloadJson, responseContentType, cancellationToken);
        }

        private async Task PersistCompletionAsync(
            PaymentCommandDeduplication record,
            int statusCode,
            string? resultEntityType,
            string? resultEntityId,
            string? errorCode,
            string? responsePayloadJson,
            string? responseContentType,
            CancellationToken cancellationToken)
        {
            if (_context.Entry(record).State == EntityState.Detached)
            {
                _context.PaymentCommandDeduplications.Attach(record);
            }

            record.Status = "completed";
            record.ResultStatusCode = statusCode;
            record.ResultEntityType = resultEntityType;
            record.ResultEntityId = resultEntityId;
            record.ErrorCode = errorCode;
            record.ResponsePayloadJson = responsePayloadJson;
            record.ResponseContentType = responseContentType;
            record.CompletedAt = DateTime.UtcNow;
            record.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(cancellationToken);
        }

        private static PaymentCommandIdempotencyResult BuildResult(
            PaymentCommandDeduplication existing,
            string requestFingerprint)
        {
            if (!string.Equals(existing.RequestFingerprint, requestFingerprint, StringComparison.Ordinal))
            {
                return new PaymentCommandIdempotencyResult
                {
                    State = PaymentCommandIdempotencyState.Conflict,
                    Record = existing
                };
            }

            return new PaymentCommandIdempotencyResult
            {
                State = string.Equals(existing.Status, "completed", StringComparison.OrdinalIgnoreCase)
                    ? PaymentCommandIdempotencyState.CompletedDuplicate
                    : PaymentCommandIdempotencyState.InProgressDuplicate,
                Record = existing
            };
        }
    }
}
