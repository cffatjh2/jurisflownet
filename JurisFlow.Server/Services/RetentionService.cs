using Microsoft.EntityFrameworkCore;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;

namespace JurisFlow.Server.Services
{
    public class RetentionService
    {
        private readonly JurisFlowDbContext _context;
        private readonly ILogger<RetentionService> _logger;
        private readonly IConfiguration _configuration;

        public RetentionService(JurisFlowDbContext context, ILogger<RetentionService> logger, IConfiguration configuration)
        {
            _context = context;
            _logger = logger;
            _configuration = configuration;
        }

        public async Task<Dictionary<string, int>> ApplyRetentionAsync()
        {
            var results = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var policies = await _context.RetentionPolicies.Where(p => p.IsActive).ToListAsync();

            foreach (var policy in policies)
            {
                var cutoff = DateTime.UtcNow.AddDays(-Math.Abs(policy.RetentionDays));
                var deleted = 0;
                var auditImmutable = _configuration.GetValue("Security:AuditLogImmutable", false);

                switch (policy.EntityName)
                {
                    case "AuditLog":
                        if (!auditImmutable)
                        {
                            deleted = await _context.AuditLogs.Where(x => x.CreatedAt < cutoff).ExecuteDeleteAsync();
                        }
                        else
                        {
                            _logger.LogInformation("Audit log retention skipped because immutability is enabled.");
                        }
                        break;
                    case "Notification":
                        deleted = await _context.Notifications.Where(x => x.CreatedAt < cutoff).ExecuteDeleteAsync();
                        break;
                    case "ClientMessage":
                        deleted = await _context.ClientMessages.Where(x => x.CreatedAt < cutoff).ExecuteDeleteAsync();
                        break;
                    case "StaffMessage":
                        deleted = await _context.StaffMessages.Where(x => x.CreatedAt < cutoff).ExecuteDeleteAsync();
                        break;
                    case "ResearchSession":
                        deleted = await _context.ResearchSessions.Where(x => x.CreatedAt < cutoff).ExecuteDeleteAsync();
                        break;
                    case "SignatureRequest":
                        deleted = await _context.SignatureRequests.Where(x => x.CreatedAt < cutoff).ExecuteDeleteAsync();
                        break;
                    case "AuthSession":
                        deleted = await _context.AuthSessions
                            .Where(x => (x.ExpiresAt < cutoff) || (x.RevokedAt != null && x.RevokedAt < cutoff))
                            .ExecuteDeleteAsync();
                        break;
                    default:
                        _logger.LogWarning("Unknown retention entity: {Entity}", policy.EntityName);
                        break;
                }

                policy.LastAppliedAt = DateTime.UtcNow;
                policy.UpdatedAt = DateTime.UtcNow;
                results[policy.EntityName] = deleted;
            }

            await _context.SaveChangesAsync();
            return results;
        }
    }
}
