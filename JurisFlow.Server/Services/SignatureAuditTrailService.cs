using System.Text.Json;
using Task = System.Threading.Tasks.Task;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;

namespace JurisFlow.Server.Services
{
    public class SignatureAuditTrailService
    {
        private readonly JurisFlowDbContext _context;

        public SignatureAuditTrailService(JurisFlowDbContext context)
        {
            _context = context;
        }

        public async Task LogAsync(
            HttpContext? httpContext,
            SignatureRequest request,
            string action,
            string? actorType,
            string? actorId,
            string? actorEmail,
            object? metadata = null)
        {
            var entry = new SignatureAuditEntry
            {
                SignatureRequestId = request.Id,
                Action = action,
                ActorType = actorType,
                ActorId = actorId,
                ActorEmail = actorEmail,
                IpAddress = httpContext?.Connection.RemoteIpAddress?.ToString(),
                UserAgent = httpContext?.Request.Headers.UserAgent.ToString(),
                MetadataJson = metadata == null ? null : JsonSerializer.Serialize(metadata),
                CreatedAt = DateTime.UtcNow
            };

            _context.SignatureAuditEntries.Add(entry);
            await _context.SaveChangesAsync();
        }

        public async Task LogSystemAsync(SignatureRequest request, string action, object? metadata = null)
        {
            await LogAsync(null, request, action, "System", null, null, metadata);
        }
    }
}
