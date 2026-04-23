using System.Net;
using System.Net.Mail;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace JurisFlow.Server.Services
{
    public class OutboundEmailService
    {
        private readonly JurisFlowDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly ILogger<OutboundEmailService> _logger;

        public OutboundEmailService(JurisFlowDbContext context, IConfiguration configuration, ILogger<OutboundEmailService> logger)
        {
            _context = context;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<OutboundEmail> QueueAsync(OutboundEmail email, CancellationToken cancellationToken = default)
        {
            email.Id = Guid.NewGuid().ToString();
            email.Status = "Queued";
            email.CreatedAt = DateTime.UtcNow;
            email.UpdatedAt = DateTime.UtcNow;
            _context.OutboundEmails.Add(email);
            await _context.SaveChangesAsync(cancellationToken);
            return email;
        }

        public async Task<OutboundEmail> DeliverQueuedAsync(OutboundEmail email, CancellationToken cancellationToken = default)
        {
            try
            {
                await SendAsync(email, cancellationToken);
                email.Status = "Sent";
                email.SentAt = DateTime.UtcNow;
                email.ErrorMessage = null;
            }
            catch (Exception ex)
            {
                email.Status = "Failed";
                email.ErrorMessage = ex.Message;
                _logger.LogWarning(ex, "Failed to send email {EmailId}", email.Id);
                throw;
            }
            finally
            {
                email.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync(cancellationToken);
            }

            return email;
        }

        public async Task<int> ProcessPendingAsync(int limit = 50)
        {
            var now = DateTime.UtcNow;
            var pending = await _context.OutboundEmails
                .Where(e => e.Status == "Queued" && e.ScheduledFor <= now)
                .OrderBy(e => e.ScheduledFor)
                .Take(limit)
                .ToListAsync();

            var sent = 0;
            foreach (var email in pending)
            {
                try
                {
                    await SendAsync(email, CancellationToken.None);
                    email.Status = "Sent";
                    email.SentAt = DateTime.UtcNow;
                    email.ErrorMessage = null;
                    sent++;
                }
                catch (Exception ex)
                {
                    email.Status = "Failed";
                    email.ErrorMessage = ex.Message;
                    _logger.LogWarning(ex, "Failed to send email {EmailId}", email.Id);
                }

                email.UpdatedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();
            return sent;
        }

        private async Task SendAsync(OutboundEmail email, CancellationToken cancellationToken)
        {
            var enabled = _configuration.GetValue("Email:Enabled", false);
            if (!enabled)
            {
                throw new InvalidOperationException("Email delivery is disabled.");
            }

            var host = _configuration["Email:Smtp:Host"];
            var port = _configuration.GetValue("Email:Smtp:Port", 587);
            var username = _configuration["Email:Smtp:Username"];
            var password = _configuration["Email:Smtp:Password"];
            var useTls = _configuration.GetValue("Email:Smtp:UseTls", true);
            var fromAddress = email.FromAddress ?? _configuration["Email:FromAddress"];

            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(fromAddress))
            {
                throw new InvalidOperationException("SMTP host or From address is not configured.");
            }

            using var message = new MailMessage
            {
                From = new MailAddress(fromAddress),
                Subject = email.Subject,
                Body = email.BodyHtml ?? email.BodyText ?? string.Empty,
                IsBodyHtml = !string.IsNullOrWhiteSpace(email.BodyHtml)
            };
            message.To.Add(new MailAddress(email.ToAddress));

            using var client = new SmtpClient(host, port)
            {
                EnableSsl = useTls
            };

            if (!string.IsNullOrWhiteSpace(username))
            {
                client.Credentials = new NetworkCredential(username, password);
            }

            await client.SendMailAsync(message, cancellationToken);
        }
    }
}
