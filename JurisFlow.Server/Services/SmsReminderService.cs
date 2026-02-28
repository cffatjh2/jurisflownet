using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace JurisFlow.Server.Services
{
    public class SmsReminderService
    {
        private readonly JurisFlowDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly ILogger<SmsReminderService> _logger;
        private readonly TwilioSmsService _twilioSmsService;

        public SmsReminderService(
            JurisFlowDbContext context,
            IConfiguration configuration,
            ILogger<SmsReminderService> logger,
            TwilioSmsService twilioSmsService)
        {
            _context = context;
            _configuration = configuration;
            _logger = logger;
            _twilioSmsService = twilioSmsService;
        }

        public async Task<int> ProcessPendingAsync()
        {
            var now = DateTime.UtcNow;
            var pendingReminders = await _context.SmsReminders
                .Where(r => r.Status == "Pending" && r.ScheduledFor <= now)
                .ToListAsync();

            var sentCount = 0;

            foreach (var reminder in pendingReminders)
            {
                try
                {
                    var message = new SmsMessage
                    {
                        FromNumber = _configuration["Twilio:FromNumber"] ?? "+15551234567",
                        ToNumber = reminder.ToNumber,
                        Body = reminder.Message,
                        Direction = "Outbound",
                        Status = "Queued",
                        ClientId = reminder.ClientId,
                    };

                    _context.SmsMessages.Add(message);

                    var sendResult = await _twilioSmsService.SendAsync(reminder.ToNumber, reminder.Message);
                    message.ExternalId = sendResult.MessageSid;
                    message.Status = sendResult.Status;
                    message.ErrorCode = sendResult.ErrorCode;
                    message.ErrorMessage = sendResult.ErrorMessage;
                    if (sendResult.Success)
                    {
                        message.SentAt = DateTime.UtcNow;
                    }

                    reminder.Status = sendResult.Success ? "Sent" : "Failed";
                    reminder.SmsMessageId = message.Id;
                    if (sendResult.Success)
                    {
                        sentCount++;
                    }
                }
                catch (Exception ex)
                {
                    reminder.Status = "Failed";
                    _logger.LogWarning(ex, "Failed to send SMS reminder {ReminderId}", reminder.Id);
                }
            }

            await _context.SaveChangesAsync();
            return sentCount;
        }
    }
}
