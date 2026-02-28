using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace JurisFlow.Server.Services
{
    public class SignatureLifecycleService
    {
        private readonly JurisFlowDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly SignatureAuditTrailService _auditTrailService;
        private readonly OutboundEmailService _outboundEmailService;

        public SignatureLifecycleService(
            JurisFlowDbContext context,
            IConfiguration configuration,
            SignatureAuditTrailService auditTrailService,
            OutboundEmailService outboundEmailService)
        {
            _context = context;
            _configuration = configuration;
            _auditTrailService = auditTrailService;
            _outboundEmailService = outboundEmailService;
        }

        public async Task<SignatureLifecycleResult> ProcessAsync(DateTime? nowUtc = null)
        {
            var now = nowUtc ?? DateTime.UtcNow;
            var reminderEnabled = _configuration.GetValue("Signatures:ReminderEnabled", true);
            var reminderCadenceDays = _configuration.GetValue("Signatures:ReminderCadenceDays", 3);
            var maxReminders = _configuration.GetValue("Signatures:MaxReminders", 3);
            var emailEnabled = _configuration.GetValue("Email:Enabled", false);

            var activeStatuses = new[] { "Pending", "Sent", "Viewed" };
            var activeRequests = await _context.SignatureRequests
                .Where(r => activeStatuses.Contains(r.Status))
                .ToListAsync();

            var expiredCount = 0;
            var reminderCount = 0;

            foreach (var request in activeRequests)
            {
                if (request.ExpiresAt.HasValue && request.ExpiresAt.Value <= now)
                {
                    request.Status = "Expired";
                    request.ExpiredAt = now;
                    request.UpdatedAt = now;
                    expiredCount++;
                    await _auditTrailService.LogSystemAsync(request, "Expired", new { request.ExpiresAt });
                    continue;
                }

                if (!reminderEnabled || reminderCadenceDays <= 0)
                {
                    continue;
                }

                if (request.ReminderCount >= maxReminders)
                {
                    continue;
                }

                if (request.Status != "Sent" && request.Status != "Viewed")
                {
                    continue;
                }

                var lastTouch = request.LastReminderAt ?? request.SentAt ?? request.CreatedAt;
                if (lastTouch.AddDays(reminderCadenceDays) > now)
                {
                    continue;
                }

                request.ReminderCount += 1;
                request.LastReminderAt = now;
                request.UpdatedAt = now;
                reminderCount++;

                await _auditTrailService.LogSystemAsync(request, "ReminderQueued", new { request.ReminderCount });

                if (emailEnabled && !string.IsNullOrWhiteSpace(request.SignerEmail))
                {
                    var subject = "Signature reminder";
                    var body = $"This is a reminder to sign the document request {request.Id}.";
                    await _outboundEmailService.QueueAsync(new OutboundEmail
                    {
                        ToAddress = request.SignerEmail,
                        Subject = subject,
                        BodyText = body,
                        ScheduledFor = now
                    });
                }

                if (!string.IsNullOrWhiteSpace(request.RequestedBy))
                {
                    _context.Notifications.Add(new Notification
                    {
                        UserId = request.RequestedBy,
                        Title = "Signature reminder queued",
                        Message = $"Reminder sent to {request.SignerEmail}.",
                        Type = "info",
                        CreatedAt = now
                    });
                }
            }

            if (expiredCount > 0 || reminderCount > 0)
            {
                await _context.SaveChangesAsync();
            }

            return new SignatureLifecycleResult
            {
                Expired = expiredCount,
                RemindersQueued = reminderCount
            };
        }
    }

    public class SignatureLifecycleResult
    {
        public int Expired { get; set; }
        public int RemindersQueued { get; set; }
    }
}
