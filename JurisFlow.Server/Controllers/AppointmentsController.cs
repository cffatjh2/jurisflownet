using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using JurisFlow.Server.Services;

namespace JurisFlow.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Policy = "StaffOnly")]
    public class AppointmentsController : ControllerBase
    {
        private readonly JurisFlowDbContext _context;
        private readonly AuditLogger _auditLogger;
        private readonly TenantContext _tenantContext;
        private static readonly HashSet<string> AllowedStatuses = new(StringComparer.OrdinalIgnoreCase)
        {
            "pending",
            "approved",
            "rejected",
            "cancelled"
        };

        public AppointmentsController(JurisFlowDbContext context, AuditLogger auditLogger, TenantContext tenantContext)
        {
            _context = context;
            _auditLogger = auditLogger;
            _tenantContext = tenantContext;
        }

        [HttpGet]
        public async Task<IActionResult> GetAppointments(
            [FromQuery] string? status,
            [FromQuery] string? clientId,
            [FromQuery] string? matterId,
            [FromQuery] int page = 1,
            [FromQuery] int limit = 100)
        {
            var normalizedPage = Math.Max(1, page);
            var normalizedLimit = Math.Clamp(limit, 1, 200);
            var normalizedStatus = string.IsNullOrWhiteSpace(status) ? null : status.Trim().ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(normalizedStatus) && !AllowedStatuses.Contains(normalizedStatus))
            {
                return BadRequest(new { message = "Invalid appointment status." });
            }

            var query = TenantScope(_context.AppointmentRequests).AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(normalizedStatus))
            {
                query = query.Where(a => a.Status == normalizedStatus);
            }
            if (!string.IsNullOrWhiteSpace(clientId))
            {
                query = query.Where(a => a.ClientId == clientId.Trim());
            }
            if (!string.IsNullOrWhiteSpace(matterId))
            {
                query = query.Where(a => a.MatterId == matterId.Trim());
            }

            var items = await query
                .OrderByDescending(a => a.RequestedDate)
                .Skip((normalizedPage - 1) * normalizedLimit)
                .Take(normalizedLimit)
                .ToListAsync();

            var clientIds = items.Select(a => a.ClientId).Distinct().ToList();
            var clients = await TenantScope(_context.Clients)
                .AsNoTracking()
                .Where(c => clientIds.Contains(c.Id))
                .Select(c => new { c.Id, c.Name, c.Email })
                .ToListAsync();
            var clientMap = clients.ToDictionary(c => c.Id, c => c);

            var response = items
                .Select(a => ToAppointmentResponse(a, clientMap.TryGetValue(a.ClientId, out var c) ? c : null))
                .ToList();

            return Ok(response);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateAppointment(string id, [FromBody] AppointmentUpdateDto dto)
        {
            if (dto == null)
            {
                return BadRequest(new { message = "Request body is required." });
            }

            var appointment = await TenantScope(_context.AppointmentRequests).FirstOrDefaultAsync(a => a.Id == id);
            if (appointment == null) return NotFound();

            var previousStatus = appointment.Status;
            var previousApprovedDate = appointment.ApprovedDate;
            var normalizedStatus = string.IsNullOrWhiteSpace(appointment.Status)
                ? "pending"
                : appointment.Status.Trim().ToLowerInvariant();

            if (!string.IsNullOrWhiteSpace(dto.Status))
            {
                var requestedStatus = dto.Status.Trim().ToLowerInvariant();
                if (!AllowedStatuses.Contains(requestedStatus))
                {
                    return BadRequest(new { message = "Invalid appointment status." });
                }
                normalizedStatus = requestedStatus;
                appointment.Status = requestedStatus;
            }

            if (dto.ApprovedDate.HasValue && !string.Equals(normalizedStatus, "approved", StringComparison.Ordinal))
            {
                return BadRequest(new { message = "ApprovedDate can only be set when status is approved." });
            }

            if (!string.IsNullOrWhiteSpace(dto.AssignedTo))
            {
                var assignee = dto.AssignedTo.Trim();
                var assignedUser = await TenantScope(_context.Users)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(u => u.Id == assignee || u.Email == assignee);
                if (assignedUser == null)
                {
                    return BadRequest(new { message = "AssignedTo user was not found for this tenant." });
                }

                appointment.AssignedTo = assignedUser.Id;
            }
            if (dto.Duration.HasValue)
            {
                if (dto.Duration.Value < 15 || dto.Duration.Value > 240)
                {
                    return BadRequest(new { message = "Duration must be between 15 and 240 minutes." });
                }
                appointment.Duration = dto.Duration.Value;
            }

            appointment.UpdatedAt = DateTime.UtcNow;

            if (string.Equals(normalizedStatus, "approved", StringComparison.Ordinal))
            {
                if (dto.ApprovedDate.HasValue)
                {
                    appointment.ApprovedDate = dto.ApprovedDate.Value;
                }
                else if (!appointment.ApprovedDate.HasValue)
                {
                    appointment.ApprovedDate = appointment.RequestedDate;
                }
            }
            else
            {
                appointment.ApprovedDate = null;
            }

            var notificationPlanned = false;
            if (!string.Equals(previousStatus, appointment.Status, StringComparison.OrdinalIgnoreCase))
            {
                notificationPlanned = true;
                await CreateClientNotificationAsync(appointment, appointment.Status switch
                {
                    "approved" => "Appointment Approved",
                    "rejected" => "Appointment Rejected",
                    "cancelled" => "Appointment Cancelled",
                    _ => "Appointment Updated"
                }, appointment.Status switch
                {
                    "approved" => $"Your appointment request for {appointment.RequestedDate:g} has been approved.",
                    "rejected" => $"Your appointment request for {appointment.RequestedDate:g} has been rejected.",
                    "cancelled" => $"Your appointment request for {appointment.RequestedDate:g} has been cancelled.",
                    _ => $"Your appointment request for {appointment.RequestedDate:g} has been updated."
                }, appointment.Status == "approved" ? "success" : "warning");
            }
            else if (appointment.Status == "approved" && appointment.ApprovedDate.HasValue && appointment.ApprovedDate != previousApprovedDate)
            {
                notificationPlanned = true;
                await CreateClientNotificationAsync(appointment, "Appointment Rescheduled",
                    $"Your appointment has been rescheduled to {appointment.ApprovedDate:MMM d, yyyy h:mm tt}.",
                    "info");
            }

            if (dto.NotifyClient == true && !notificationPlanned)
            {
                var title = appointment.Status == "approved" ? "Appointment Reminder" : "Appointment Update";
                var message = appointment.Status == "approved" && appointment.ApprovedDate.HasValue
                    ? $"Reminder: your appointment is scheduled for {appointment.ApprovedDate:MMM d, yyyy h:mm tt}."
                    : $"Your appointment request status is {appointment.Status}.";
                await CreateClientNotificationAsync(appointment, title, message, "info");
            }

            await _context.SaveChangesAsync();
            await _auditLogger.LogAsync(HttpContext, "appointment.update", "AppointmentRequest", appointment.Id, $"Status={appointment.Status}");

            var client = await TenantScope(_context.Clients)
                .AsNoTracking()
                .Where(c => c.Id == appointment.ClientId)
                .Select(c => new { c.Id, c.Name, c.Email })
                .FirstOrDefaultAsync();
            return Ok(ToAppointmentResponse(appointment, client));
        }

        [HttpPost("{id}/notify")]
        public async Task<IActionResult> NotifyAppointment(string id)
        {
            var appointment = await TenantScope(_context.AppointmentRequests).FirstOrDefaultAsync(a => a.Id == id);
            if (appointment == null) return NotFound();

            var title = appointment.Status == "approved" ? "Appointment Reminder" : "Appointment Update";
            var message = appointment.Status == "approved" && appointment.ApprovedDate.HasValue
                ? $"Reminder: your appointment is scheduled for {appointment.ApprovedDate:MMM d, yyyy h:mm tt}."
                : $"Your appointment request status is {appointment.Status}.";

            await CreateClientNotificationAsync(appointment, title, message, "info");
            await _context.SaveChangesAsync();
            await _auditLogger.LogAsync(HttpContext, "appointment.notify", "AppointmentRequest", appointment.Id, "Client notified");

            return Ok(new { message = "Notification sent." });
        }

        private async System.Threading.Tasks.Task CreateClientNotificationAsync(AppointmentRequest appointment, string title, string message, string type)
        {
            var client = await TenantScope(_context.Clients).FirstOrDefaultAsync(c => c.Id == appointment.ClientId);
            if (client == null) return;

            _context.Notifications.Add(new Notification
            {
                ClientId = client.Id,
                Title = title,
                Message = message,
                Type = type,
                Link = "tab:appointments"
            });
        }

        private static object ToAppointmentResponse(AppointmentRequest appointment, object? client)
        {
            return new
            {
                id = appointment.Id,
                clientId = appointment.ClientId,
                client,
                matterId = appointment.MatterId,
                requestedDate = appointment.RequestedDate,
                duration = appointment.Duration,
                type = appointment.Type,
                notes = appointment.Notes,
                status = appointment.Status,
                assignedTo = appointment.AssignedTo,
                approvedDate = appointment.ApprovedDate,
                createdAt = appointment.CreatedAt,
                updatedAt = appointment.UpdatedAt
            };
        }

        private IQueryable<T> TenantScope<T>(IQueryable<T> query) where T : class
        {
            var tenantId = RequireTenantId();
            return query.Where(e => EF.Property<string>(e, "TenantId") == tenantId);
        }

        private string RequireTenantId()
        {
            var tenantId = _tenantContext.TenantId;
            if (string.IsNullOrWhiteSpace(tenantId))
            {
                throw new InvalidOperationException("Tenant context is required.");
            }

            return tenantId;
        }
    }

    public class AppointmentUpdateDto
    {
        public string? Status { get; set; }
        public DateTime? ApprovedDate { get; set; }
        public string? AssignedTo { get; set; }
        public bool? NotifyClient { get; set; }
        public int? Duration { get; set; }
    }
}
