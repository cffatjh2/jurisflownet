using System.Globalization;
using Microsoft.EntityFrameworkCore;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;

namespace JurisFlow.Server.Services
{
    public class DeadlineReminderService
    {
        private readonly JurisFlowDbContext _context;
        private readonly ILogger<DeadlineReminderService> _logger;

        public DeadlineReminderService(JurisFlowDbContext context, ILogger<DeadlineReminderService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public Task<DeadlineReminderResult> ProcessAsync(DateTime? nowUtc = null)
        {
            return ProcessAsync(null, nowUtc);
        }

        public async Task<DeadlineReminderResult> ProcessAsync(string? tenantId, DateTime? nowUtc = null)
        {
            var utcNow = nowUtc ?? DateTime.UtcNow;
            var today = utcNow.Date;

            var deadlineQuery = _context.Deadlines.AsQueryable();
            if (!string.IsNullOrWhiteSpace(tenantId))
            {
                deadlineQuery = deadlineQuery.Where(d => EF.Property<string>(d, "TenantId") == tenantId);
            }

            var pendingDeadlines = await deadlineQuery
                .Where(d => d.Status == "Pending")
                .ToListAsync();

            if (pendingDeadlines.Count == 0)
            {
                return new DeadlineReminderResult();
            }

            var matterIds = pendingDeadlines
                .Select(d => d.MatterId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct()
                .ToList();

            var matterMap = new Dictionary<string, Matter>();
            if (matterIds.Count > 0)
            {
                var matterQuery = _context.Matters
                    .Where(m => matterIds.Contains(m.Id));

                if (!string.IsNullOrWhiteSpace(tenantId))
                {
                    matterQuery = matterQuery.Where(m => EF.Property<string>(m, "TenantId") == tenantId);
                }

                matterMap = await matterQuery.ToDictionaryAsync(m => m.Id);
            }

            var notifications = new List<Notification>();
            var remindersSent = 0;
            var missedUpdated = 0;

            foreach (var deadline in pendingDeadlines)
            {
                if (deadline.DueDate.Date < today)
                {
                    deadline.Status = "Missed";
                    deadline.UpdatedAt = utcNow;
                    missedUpdated++;

                    if (!string.IsNullOrWhiteSpace(deadline.AssignedTo))
                    {
                        notifications.Add(BuildNotification(
                            deadline,
                            matterMap,
                            "Deadline Missed",
                            "error",
                            $"Deadline \"{deadline.Title}\" is overdue."
                        ));
                    }

                    continue;
                }

                if (deadline.ReminderSent)
                {
                    continue;
                }

                var reminderDays = Math.Max(0, deadline.ReminderDays);
                var reminderDate = deadline.DueDate.Date.AddDays(-reminderDays);
                if (today < reminderDate)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(deadline.AssignedTo))
                {
                    continue;
                }

                notifications.Add(BuildNotification(
                    deadline,
                    matterMap,
                    "Deadline Reminder",
                    "warning",
                    $"Deadline \"{deadline.Title}\" is due on {FormatDate(deadline.DueDate)}."
                ));

                deadline.ReminderSent = true;
                deadline.UpdatedAt = utcNow;
                remindersSent++;
            }

            if (notifications.Count > 0)
            {
                _context.Notifications.AddRange(notifications);
            }

            await _context.SaveChangesAsync();

            var result = new DeadlineReminderResult
            {
                TotalChecked = pendingDeadlines.Count,
                RemindersSent = remindersSent,
                MissedUpdated = missedUpdated,
                NotificationsCreated = notifications.Count
            };
            _logger.LogInformation(
                "Deadline reminders processed. Checked={Total} RemindersSent={RemindersSent} MissedUpdated={MissedUpdated} Notifications={Notifications}",
                result.TotalChecked,
                result.RemindersSent,
                result.MissedUpdated,
                result.NotificationsCreated);

            return result;
        }

        private Notification BuildNotification(
            Deadline deadline,
            Dictionary<string, Matter> matterMap,
            string title,
            string type,
            string message)
        {
            var matterLabel = ResolveMatterLabel(deadline, matterMap);
            var suffix = string.IsNullOrWhiteSpace(matterLabel) ? string.Empty : $" ({matterLabel})";

            return new Notification
            {
                UserId = deadline.AssignedTo,
                Title = $"{title}{suffix}",
                Message = message,
                Type = type,
                Link = "tab:calendar",
                CreatedAt = DateTime.UtcNow
            };
        }

        private string ResolveMatterLabel(Deadline deadline, Dictionary<string, Matter> matterMap)
        {
            if (string.IsNullOrWhiteSpace(deadline.MatterId))
            {
                return string.Empty;
            }

            if (!matterMap.TryGetValue(deadline.MatterId, out var matter))
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(matter.CaseNumber))
            {
                return $"{matter.CaseNumber} - {matter.Name}";
            }

            return matter.Name ?? string.Empty;
        }

        private string FormatDate(DateTime date)
        {
            return date.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
        }
    }

    public class DeadlineReminderResult
    {
        public int TotalChecked { get; set; }
        public int RemindersSent { get; set; }
        public int MissedUpdated { get; set; }
        public int NotificationsCreated { get; set; }
    }
}
