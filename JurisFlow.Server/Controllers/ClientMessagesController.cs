using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using JurisFlow.Server.Data;
using JurisFlow.Server.DTOs;
using JurisFlow.Server.Models;
using JurisFlow.Server.Services;

namespace JurisFlow.Server.Controllers
{
    [Route("api/client/messages")]
    [ApiController]
    [Authorize(Policy = "StaffOrClient")]
    public class ClientMessagesController : ControllerBase
    {
        private readonly JurisFlowDbContext _context;
        private readonly MessageAttachmentIntakeService _attachmentIntake;
        private readonly MessageAttachmentIndexService _attachmentIndex;
        private readonly AuditLogger _auditLogger;
        private readonly TenantContext _tenantContext;
        private const int MaxMessageRequestBodyBytes = 40 * 1024 * 1024;

        public ClientMessagesController(
            JurisFlowDbContext context,
            MessageAttachmentIntakeService attachmentIntake,
            MessageAttachmentIndexService attachmentIndex,
            AuditLogger auditLogger,
            TenantContext tenantContext)
        {
            _context = context;
            _attachmentIntake = attachmentIntake;
            _attachmentIndex = attachmentIndex;
            _auditLogger = auditLogger;
            _tenantContext = tenantContext;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<object>>> GetForClient([FromQuery] string? clientId)
        {
            if (!IsClient()) return Forbid();
            var resolvedClientId = GetClientId();
            if (string.IsNullOrWhiteSpace(resolvedClientId)) return Unauthorized();
            if (!string.IsNullOrWhiteSpace(clientId) && !string.Equals(clientId, resolvedClientId, StringComparison.OrdinalIgnoreCase))
            {
                return Forbid();
            }

            var client = await TenantScope(_context.Clients)
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == resolvedClientId);
            if (client == null)
            {
                return Unauthorized();
            }

            var items = await TenantScope(_context.ClientMessages)
                .AsNoTracking()
                .Where(m => m.ClientId == resolvedClientId)
                .OrderByDescending(m => m.CreatedAt)
                .Take(100)
                .ToListAsync();
            var attachmentMap = await LoadAttachmentMapAsync(items.Select(m => m.Id));

            var matterIds = items.Where(m => !string.IsNullOrEmpty(m.MatterId)).Select(m => m.MatterId!).Distinct().ToList();
            var matters = await TenantScope(_context.Matters)
                .AsNoTracking()
                .Where(m => matterIds.Contains(m.Id))
                .Select(m => new { m.Id, m.Name, m.CaseNumber })
                .ToListAsync();
            var matterMap = matters.ToDictionary(m => m.Id, m => m);

            var senderUserIds = items
                .Where(m => !string.IsNullOrWhiteSpace(m.SenderUserId))
                .Select(m => m.SenderUserId!)
                .Distinct()
                .ToList();
            var users = await TenantScope(_context.Users)
                .AsNoTracking()
                .Where(u => senderUserIds.Contains(u.Id))
                .Select(u => new { u.Id, u.Name })
                .ToListAsync();
            var userMap = users.ToDictionary(u => u.Id, u => u.Name);

            var senderEmployeeIds = items
                .Where(m => !string.IsNullOrWhiteSpace(m.EmployeeId))
                .Select(m => m.EmployeeId!)
                .Distinct()
                .ToList();
            var employees = await TenantScope(_context.Employees)
                .AsNoTracking()
                .Where(e => senderEmployeeIds.Contains(e.Id))
                .Select(e => new { e.Id, Name = e.FirstName + " " + e.LastName })
                .ToListAsync();
            var employeeMap = employees.ToDictionary(e => e.Id, e => e.Name);

            var response = items.Select(m => new
            {
                id = m.Id,
                subject = m.Subject,
                message = m.Body,
                read = string.Equals(m.Status, "Read", StringComparison.OrdinalIgnoreCase),
                createdAt = m.CreatedAt,
                matterId = m.MatterId,
                matter = m.MatterId != null && matterMap.TryGetValue(m.MatterId, out var matter) ? matter : null,
                attachments = GetPublicAttachments(attachmentMap, m.Id),
                senderType = string.IsNullOrWhiteSpace(m.SenderType) ? "Client" : m.SenderType,
                senderName = ResolveSenderName(m, client?.Name, userMap, employeeMap)
            });

            return Ok(response);
        }

        [HttpGet("~/api/messages/client")]
        public async Task<ActionResult<IEnumerable<object>>> GetForStaff([FromQuery] string? clientId)
        {
            if (IsClient()) return Forbid();

            var query = TenantScope(_context.ClientMessages).AsNoTracking().AsQueryable();
            var currentUserId = GetUserId();
            var currentEmployeeId = await ResolveCurrentEmployeeIdAsync();

            if (!IsAdmin())
            {
                if (string.IsNullOrWhiteSpace(currentUserId) && string.IsNullOrWhiteSpace(currentEmployeeId))
                {
                    return Forbid();
                }

                query = query.Where(m =>
                    (!string.IsNullOrWhiteSpace(currentUserId) && m.SenderUserId == currentUserId) ||
                    (!string.IsNullOrWhiteSpace(currentEmployeeId) && m.EmployeeId == currentEmployeeId));
            }

            if (!string.IsNullOrWhiteSpace(clientId))
            {
                var clientExists = await TenantScope(_context.Clients)
                    .AsNoTracking()
                    .AnyAsync(c => c.Id == clientId);
                if (!clientExists)
                {
                    return NotFound(new { message = "Client not found for this tenant." });
                }

                query = query.Where(m => m.ClientId == clientId);
            }

            var items = await query.OrderByDescending(m => m.CreatedAt).Take(200).ToListAsync();
            var attachmentMap = await LoadAttachmentMapAsync(items.Select(m => m.Id));

            var clientIds = items.Select(m => m.ClientId).Distinct().ToList();
            var clients = await TenantScope(_context.Clients)
                .AsNoTracking()
                .Where(c => clientIds.Contains(c.Id))
                .Select(c => new { c.Id, c.Name, c.Email })
                .ToListAsync();
            var clientMap = clients.ToDictionary(c => c.Id, c => c);

            var matterIds = items.Where(m => !string.IsNullOrEmpty(m.MatterId)).Select(m => m.MatterId!).Distinct().ToList();
            var matters = await TenantScope(_context.Matters)
                .AsNoTracking()
                .Where(m => matterIds.Contains(m.Id))
                .Select(m => new { m.Id, m.Name, m.CaseNumber })
                .ToListAsync();
            var matterMap = matters.ToDictionary(m => m.Id, m => m);

            var senderUserIds = items
                .Where(m => !string.IsNullOrWhiteSpace(m.SenderUserId))
                .Select(m => m.SenderUserId!)
                .Distinct()
                .ToList();
            var users = await TenantScope(_context.Users)
                .AsNoTracking()
                .Where(u => senderUserIds.Contains(u.Id))
                .Select(u => new { u.Id, u.Name })
                .ToListAsync();
            var userMap = users.ToDictionary(u => u.Id, u => u.Name);

            var senderEmployeeIds = items
                .Where(m => !string.IsNullOrWhiteSpace(m.EmployeeId))
                .Select(m => m.EmployeeId!)
                .Distinct()
                .ToList();
            var employees = await TenantScope(_context.Employees)
                .AsNoTracking()
                .Where(e => senderEmployeeIds.Contains(e.Id))
                .Select(e => new { e.Id, Name = e.FirstName + " " + e.LastName })
                .ToListAsync();
            var employeeMap = employees.ToDictionary(e => e.Id, e => e.Name);

            var response = items.Select(m => new
            {
                id = m.Id,
                clientId = m.ClientId,
                client = clientMap.TryGetValue(m.ClientId, out var client) ? client : null,
                subject = m.Subject,
                message = m.Body,
                read = string.Equals(m.Status, "Read", StringComparison.OrdinalIgnoreCase),
                createdAt = m.CreatedAt,
                matterId = m.MatterId,
                matter = m.MatterId != null && matterMap.TryGetValue(m.MatterId, out var matter) ? matter : null,
                attachments = GetPublicAttachments(attachmentMap, m.Id),
                senderType = string.IsNullOrWhiteSpace(m.SenderType) ? "Client" : m.SenderType,
                senderName = ResolveSenderName(m, clientMap.TryGetValue(m.ClientId, out var clientName) ? clientName.Name : null, userMap, employeeMap)
            });

            return Ok(response);
        }

        [HttpPost("~/api/messages/client/send")]
        [EnableRateLimiting("ClientMessagingSend")]
        [RequestSizeLimit(MaxMessageRequestBodyBytes)]
        public async Task<ActionResult<object>> SendFromStaff([FromBody] ClientMessageStaffCreateDto? dto)
        {
            if (IsClient()) return Forbid();
            if (dto == null) return BadRequest(new { message = "Request body is required." });
            if (!ModelState.IsValid) return BadRequest(ModelState);
            if (string.IsNullOrWhiteSpace(dto.ClientId))
            {
                return BadRequest(new { message = "Client is required." });
            }

            var client = await TenantScope(_context.Clients).FirstOrDefaultAsync(c => c.Id == dto.ClientId);
            if (client == null)
            {
                return BadRequest(new { message = "Client not found." });
            }

            Matter? matter = null;
            if (!string.IsNullOrWhiteSpace(dto.MatterId))
            {
                matter = await TenantScope(_context.Matters)
                    .FirstOrDefaultAsync(m => m.Id == dto.MatterId && m.ClientId == client.Id);
                if (matter == null)
                {
                    return BadRequest(new { message = "Matter not found for this client." });
                }
            }

            List<MessageAttachmentPayload> attachments;
            try
            {
                attachments = await _attachmentIntake.SaveAsync(dto.Attachments, HttpContext.RequestAborted);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            var userId = GetUserId();
            var currentEmployeeId = await ResolveCurrentEmployeeIdAsync();
            var employeeId = dto.EmployeeId;
            if (!string.IsNullOrWhiteSpace(employeeId))
            {
                var targetEmployee = await TenantScope(_context.Employees)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(e => e.Id == employeeId);
                if (targetEmployee == null)
                {
                    return BadRequest(new { message = "Employee not found for this tenant." });
                }

                if (!IsAdmin() && !string.Equals(employeeId, currentEmployeeId, StringComparison.Ordinal))
                {
                    return Forbid();
                }
            }
            else if (!string.IsNullOrWhiteSpace(currentEmployeeId))
            {
                employeeId = currentEmployeeId;
            }

            var msg = new ClientMessage
            {
                ClientId = client.Id,
                EmployeeId = employeeId,
                MatterId = dto.MatterId,
                Subject = dto.Subject,
                Body = dto.Message,
                Status = "Unread",
                CreatedAt = DateTime.UtcNow,
                AttachmentsJson = attachments.Count > 0 ? JsonSerializer.Serialize(attachments) : null,
                SenderType = "Staff",
                SenderUserId = userId
            };

            _context.ClientMessages.Add(msg);
            await _attachmentIndex.IndexClientMessageAsync(msg, attachments, HttpContext.RequestAborted);

            var contextLabel = matter != null ? $"{matter.CaseNumber} - {matter.Name}" : "your matter";
            _context.Notifications.Add(new Notification
            {
                ClientId = client.Id,
                Title = "New message from your legal team",
                Message = $"You received a new message regarding {contextLabel}.",
                Type = "info",
                Link = "tab:messages"
            });

            await _context.SaveChangesAsync();
            await _auditLogger.LogAsync(HttpContext, "staff.message.send", "ClientMessage", msg.Id, $"Client={client.Email}");

            var senderName = "Firm Staff";
            if (!string.IsNullOrWhiteSpace(userId))
            {
                var user = await TenantScope(_context.Users)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(u => u.Id == userId);
                if (user != null)
                {
                    senderName = user.Name;
                }
                else if (!string.IsNullOrWhiteSpace(employeeId))
                {
                    var employee = await TenantScope(_context.Employees)
                        .AsNoTracking()
                        .FirstOrDefaultAsync(e => e.Id == employeeId);
                    if (employee != null)
                    {
                        senderName = $"{employee.FirstName} {employee.LastName}";
                    }
                }
            }

            return Ok(new
            {
                id = msg.Id,
                subject = msg.Subject,
                message = msg.Body,
                read = false,
                createdAt = msg.CreatedAt,
                matterId = msg.MatterId,
                attachments = ToPublicAttachments(attachments),
                senderType = msg.SenderType,
                senderName
            });
        }

        [HttpPost]
        [EnableRateLimiting("ClientMessagingSend")]
        [RequestSizeLimit(MaxMessageRequestBodyBytes)]
        public async Task<ActionResult<object>> Send([FromBody] ClientMessageCreateDto? dto)
        {
            if (!IsClient()) return Forbid();
            if (dto == null) return BadRequest(new { message = "Request body is required." });
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var clientId = GetClientId();
            if (string.IsNullOrWhiteSpace(clientId)) return Unauthorized();

            var client = await TenantScope(_context.Clients).FirstOrDefaultAsync(c => c.Id == clientId);
            if (client == null) return BadRequest("Client not found");

            Matter? matter = null;
            if (!string.IsNullOrWhiteSpace(dto.MatterId))
            {
                matter = await TenantScope(_context.Matters)
                    .FirstOrDefaultAsync(m => m.Id == dto.MatterId && m.ClientId == clientId);
                if (matter == null) return Forbid();
            }

            List<MessageAttachmentPayload> attachments;
            try
            {
                attachments = await _attachmentIntake.SaveAsync(dto.Attachments, HttpContext.RequestAborted);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }

            string? targetEmployeeId = null;
            string? targetEmployeeUserId = null;
            if (!string.IsNullOrWhiteSpace(dto.EmployeeId))
            {
                if (matter == null)
                {
                    return BadRequest(new { message = "MatterId is required to send a message to a specific staff member." });
                }

                var targetEmployee = await TenantScope(_context.Employees)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(e => e.Id == dto.EmployeeId);
                if (targetEmployee == null)
                {
                    return BadRequest(new { message = "Employee not found for this tenant." });
                }

                if (!IsEmployeeAssignedToMatter(targetEmployee, matter))
                {
                    return Forbid();
                }

                targetEmployeeId = targetEmployee.Id;
                targetEmployeeUserId = targetEmployee.UserId;
            }

            var msg = new ClientMessage
            {
                ClientId = client.Id,
                EmployeeId = targetEmployeeId,
                MatterId = dto.MatterId,
                Subject = dto.Subject,
                Body = dto.Message,
                Status = "Unread",
                CreatedAt = DateTime.UtcNow,
                AttachmentsJson = attachments.Count > 0 ? JsonSerializer.Serialize(attachments) : null,
                SenderType = "Client",
                SenderUserId = null
            };

            _context.ClientMessages.Add(msg);
            await _attachmentIndex.IndexClientMessageAsync(msg, attachments, HttpContext.RequestAborted);

            if (!string.IsNullOrEmpty(targetEmployeeUserId))
            {
                _context.Notifications.Add(new Notification
                {
                    UserId = targetEmployeeUserId,
                    Title = "New client message",
                    Message = $"{client.Name} sent you a message.",
                    Type = "info",
                    Link = "tab:communications"
                });
            }

            await _context.SaveChangesAsync();
            await _auditLogger.LogAsync(HttpContext, "client.message.send", "ClientMessage", msg.Id, $"Client={client.Email}");

            return Ok(new
            {
                id = msg.Id,
                subject = msg.Subject,
                message = msg.Body,
                read = false,
                createdAt = msg.CreatedAt,
                matterId = msg.MatterId,
                attachments = ToPublicAttachments(attachments),
                senderType = msg.SenderType,
                senderName = client.Name
            });
        }

        [HttpPost("{id}/read")]
        public async Task<IActionResult> MarkRead(string id)
        {
            var msg = await TenantScope(_context.ClientMessages).FirstOrDefaultAsync(m => m.Id == id);
            if (msg == null) return NotFound();
            if (IsClient())
            {
                var clientId = GetClientId();
                if (string.IsNullOrWhiteSpace(clientId) || msg.ClientId != clientId) return Forbid();
            }
            else if (!await CanCurrentStaffAccessMessageAsync(msg))
            {
                return Forbid();
            }

            msg.Status = "Read";
            msg.ReadAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            await _auditLogger.LogAsync(HttpContext, "client.message.read", "ClientMessage", id, null);
            return NoContent();
        }

        private bool IsClient()
        {
            return User.IsInRole("Client") || string.Equals(User.FindFirst("role")?.Value, "Client", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsAdmin()
        {
            return User.IsInRole("Admin")
                || string.Equals(User.FindFirst("role")?.Value, "Admin", StringComparison.OrdinalIgnoreCase)
                || User.IsInRole("SecurityAdmin")
                || string.Equals(User.FindFirst("role")?.Value, "SecurityAdmin", StringComparison.OrdinalIgnoreCase);
        }

        private string? GetUserId()
        {
            return User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                   ?? User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        }

        private string? GetClientId()
        {
            return User.FindFirst("clientId")?.Value
                   ?? User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                   ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        }

        private async Task<string?> ResolveCurrentEmployeeIdAsync()
        {
            var userId = GetUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return null;
            }

            return await TenantScope(_context.Employees)
                .AsNoTracking()
                .Where(e => e.UserId == userId)
                .Select(e => e.Id)
                .FirstOrDefaultAsync();
        }

        private async Task<bool> CanCurrentStaffAccessMessageAsync(ClientMessage message)
        {
            if (IsAdmin())
            {
                return true;
            }

            var userId = GetUserId();
            if (!string.IsNullOrWhiteSpace(userId) && string.Equals(message.SenderUserId, userId, StringComparison.Ordinal))
            {
                return true;
            }

            var employeeId = await ResolveCurrentEmployeeIdAsync();
            return !string.IsNullOrWhiteSpace(employeeId) &&
                   string.Equals(message.EmployeeId, employeeId, StringComparison.Ordinal);
        }

        private static bool IsEmployeeAssignedToMatter(Employee employee, Matter matter)
        {
            if (!string.IsNullOrWhiteSpace(matter.ResponsibleEmployeeId))
            {
                return string.Equals(matter.ResponsibleEmployeeId, employee.Id, StringComparison.Ordinal);
            }

            return !string.IsNullOrWhiteSpace(matter.CreatedByUserId) &&
                   !string.IsNullOrWhiteSpace(employee.UserId) &&
                   string.Equals(matter.CreatedByUserId, employee.UserId, StringComparison.Ordinal);
        }

        private IQueryable<T> TenantScope<T>(IQueryable<T> query) where T : class
        {
            var tenantId = RequireTenantId();
            return query.Where(e => EF.Property<string>(e, "TenantId") == tenantId);
        }

        private string RequireTenantId()
        {
            if (string.IsNullOrWhiteSpace(_tenantContext.TenantId))
            {
                throw new InvalidOperationException("Tenant context is missing.");
            }

            return _tenantContext.TenantId;
        }

        private async Task<Dictionary<string, List<object>>> LoadAttachmentMapAsync(IEnumerable<string> messageIds)
        {
            var ids = messageIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct()
                .ToList();
            if (ids.Count == 0)
            {
                return new Dictionary<string, List<object>>();
            }

            var attachments = await TenantScope(_context.MessageAttachments)
                .AsNoTracking()
                .Where(a => a.MessageType == "client" && ids.Contains(a.MessageId))
                .OrderBy(a => a.CreatedAt)
                .ToListAsync();

            return attachments
                .GroupBy(a => a.MessageId)
                .ToDictionary(g => g.Key, g => g.Select(ToPublicAttachment).ToList());
        }

        private static IReadOnlyList<object> GetPublicAttachments(
            IReadOnlyDictionary<string, List<object>> attachmentMap,
            string messageId)
        {
            return attachmentMap.TryGetValue(messageId, out var attachments)
                ? attachments
                : Array.Empty<object>();
        }

        private static IReadOnlyList<object> ToPublicAttachments(IEnumerable<MessageAttachmentPayload> attachments)
        {
            return attachments.Select(ToPublicAttachment).ToList();
        }

        private static object ToPublicAttachment(MessageAttachmentPayload attachment)
        {
            return new
            {
                name = attachment.FileName,
                url = attachment.FilePath,
                mimeType = attachment.MimeType,
                size = attachment.Size
            };
        }

        private static object ToPublicAttachment(MessageAttachment attachment)
        {
            return new
            {
                name = attachment.FileName,
                url = attachment.FilePath,
                mimeType = attachment.MimeType,
                size = attachment.Size
            };
        }

        private static string ResolveSenderName(
            ClientMessage message,
            string? clientName,
            Dictionary<string, string> userMap,
            Dictionary<string, string> employeeMap)
        {
            var senderType = string.IsNullOrWhiteSpace(message.SenderType) ? "Client" : message.SenderType;
            if (senderType.Equals("Staff", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(message.SenderUserId) && userMap.TryGetValue(message.SenderUserId, out var userName))
                {
                    return userName;
                }
                if (!string.IsNullOrWhiteSpace(message.EmployeeId) && employeeMap.TryGetValue(message.EmployeeId, out var employeeName))
                {
                    return employeeName;
                }
                return "Firm Staff";
            }

            return string.IsNullOrWhiteSpace(clientName) ? "Client" : clientName;
        }
    }

    public class ClientMessageCreateDto
    {
        public string? ClientId { get; set; }
        public string? EmployeeId { get; set; }
        public string? MatterId { get; set; }

        [System.ComponentModel.DataAnnotations.Required]
        public string Subject { get; set; } = string.Empty;

        [System.ComponentModel.DataAnnotations.Required]
        public string Message { get; set; } = string.Empty;

        public List<AttachmentDto>? Attachments { get; set; }
    }

    public class ClientMessageStaffCreateDto
    {
        [System.ComponentModel.DataAnnotations.Required]
        public string ClientId { get; set; } = string.Empty;

        public string? EmployeeId { get; set; }
        public string? MatterId { get; set; }

        [System.ComponentModel.DataAnnotations.Required]
        public string Subject { get; set; } = string.Empty;

        [System.ComponentModel.DataAnnotations.Required]
        public string Message { get; set; } = string.Empty;

        public List<AttachmentDto>? Attachments { get; set; }
    }

}
