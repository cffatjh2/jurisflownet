using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using JurisFlow.Server.Data;
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
        private readonly IWebHostEnvironment _env;
        private readonly AuditLogger _auditLogger;
        private readonly TenantContext _tenantContext;
        private const int MaxAttachmentCount = 10;
        private const int MaxAttachmentSizeBytes = 10 * 1024 * 1024;
        private const int MaxTotalAttachmentSizeBytes = 25 * 1024 * 1024;
        private const int MaxMessageRequestBodyBytes = 40 * 1024 * 1024;
        private static readonly IReadOnlyDictionary<string, string> AllowedMimeToExtension = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["application/pdf"] = ".pdf",
            ["image/png"] = ".png",
            ["image/jpeg"] = ".jpg",
            ["image/webp"] = ".webp",
            ["application/msword"] = ".doc",
            ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"] = ".docx",
            ["application/vnd.ms-excel"] = ".xls",
            ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"] = ".xlsx",
            ["application/vnd.ms-powerpoint"] = ".ppt",
            ["application/vnd.openxmlformats-officedocument.presentationml.presentation"] = ".pptx",
            ["text/plain"] = ".txt"
        };

        public ClientMessagesController(JurisFlowDbContext context, IWebHostEnvironment env, AuditLogger auditLogger, TenantContext tenantContext)
        {
            _context = context;
            _env = env;
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
                attachments = ParsePublicAttachments(m.AttachmentsJson),
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
                attachments = ParsePublicAttachments(m.AttachmentsJson),
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

            List<MessageAttachment> attachments;
            try
            {
                attachments = await SaveAttachments(dto.Attachments);
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
                attachments = ParsePublicAttachments(msg.AttachmentsJson),
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

            List<MessageAttachment> attachments;
            try
            {
                attachments = await SaveAttachments(dto.Attachments);
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

                if (!IsEmployeeAllowedForClientMatter(targetEmployee, matter))
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
                attachments = ParsePublicAttachments(msg.AttachmentsJson),
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

        private async Task<List<MessageAttachment>> SaveAttachments(List<AttachmentDto>? attachments)
        {
            var result = new List<MessageAttachment>();
            if (attachments == null || attachments.Count == 0) return result;

            if (attachments.Count > MaxAttachmentCount)
            {
                throw new InvalidOperationException($"A maximum of {MaxAttachmentCount} attachments is allowed per message.");
            }

            var root = GetMessageAttachmentRoot();
            if (!Directory.Exists(root)) Directory.CreateDirectory(root);

            long totalBytes = 0;
            foreach (var att in attachments)
            {
                var (mimeType, base64Payload) = ParseAttachmentData(att);
                if (!AllowedMimeToExtension.TryGetValue(mimeType, out var ext))
                {
                    throw new InvalidOperationException($"Attachment MIME type '{mimeType}' is not allowed.");
                }

                var bytes = DecodeBase64(base64Payload, MaxAttachmentSizeBytes);
                ValidateAttachmentSignature(mimeType, bytes);
                totalBytes += bytes.Length;
                if (totalBytes > MaxTotalAttachmentSizeBytes)
                {
                    throw new InvalidOperationException(
                        $"Total attachment payload exceeds the {(MaxTotalAttachmentSizeBytes / (1024 * 1024)).ToString()} MB limit.");
                }

                var fileName = $"{Guid.NewGuid():N}{ext}";
                var savePath = Path.Combine(root, fileName);
                await System.IO.File.WriteAllBytesAsync(savePath, bytes);

                result.Add(new MessageAttachment
                {
                    FileName = NormalizeDisplayFileName(att.FileName ?? att.Name, ext),
                    FilePath = $"/api/files/messages/{fileName}",
                    MimeType = mimeType,
                    Size = bytes.Length
                });
            }
            return result;
        }

        private string GetMessageAttachmentRoot()
        {
            if (string.IsNullOrWhiteSpace(_tenantContext.TenantId))
            {
                throw new InvalidOperationException("Tenant context is missing.");
            }
            return Path.Combine(_env.ContentRootPath, "uploads", _tenantContext.TenantId, "message-attachments");
        }

        private static (string MimeType, string Base64Payload) ParseAttachmentData(AttachmentDto attachment)
        {
            if (string.IsNullOrWhiteSpace(attachment.Data))
            {
                throw new InvalidOperationException("Attachment data is required.");
            }

            var rawData = attachment.Data.Trim();
            if (!rawData.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Attachment payload must be a base64 data URL.");
            }

            var commaIndex = rawData.IndexOf(',');
            if (commaIndex <= 5 || commaIndex >= rawData.Length - 1)
            {
                throw new InvalidOperationException("Attachment payload is malformed.");
            }

            var header = rawData.Substring(5, commaIndex - 5);
            var base64Payload = rawData[(commaIndex + 1)..].Trim();
            if (string.IsNullOrWhiteSpace(base64Payload))
            {
                throw new InvalidOperationException("Attachment payload is empty.");
            }

            var headerParts = header.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (headerParts.Length == 0 || string.IsNullOrWhiteSpace(headerParts[0]))
            {
                throw new InvalidOperationException("Attachment MIME type is required.");
            }

            if (!headerParts.Any(p => string.Equals(p, "base64", StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("Attachment payload must use base64 encoding.");
            }

            var mimeType = headerParts[0].Trim().ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(attachment.Type) &&
                !string.Equals(attachment.Type.Trim(), mimeType, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Attachment type metadata does not match payload MIME type.");
            }

            return (mimeType, base64Payload);
        }

        private static byte[] DecodeBase64(string base64Payload, int maxBytes)
        {
            if (base64Payload.Length % 4 != 0)
            {
                throw new InvalidOperationException("Attachment payload is not valid base64.");
            }

            var padding = base64Payload.EndsWith("==", StringComparison.Ordinal)
                ? 2
                : base64Payload.EndsWith("=", StringComparison.Ordinal) ? 1 : 0;

            var expectedBytes = ((long)base64Payload.Length / 4L) * 3L - padding;
            if (expectedBytes <= 0 || expectedBytes > int.MaxValue)
            {
                throw new InvalidOperationException("Attachment payload is not valid base64.");
            }

            if (expectedBytes > maxBytes)
            {
                throw new InvalidOperationException(
                    $"Attachment exceeds the {(maxBytes / (1024 * 1024)).ToString()} MB per-file limit.");
            }

            var buffer = new byte[(int)expectedBytes];
            if (!Convert.TryFromBase64String(base64Payload, buffer, out var bytesWritten))
            {
                throw new InvalidOperationException("Attachment payload is not valid base64.");
            }

            if (bytesWritten <= 0 || bytesWritten > maxBytes)
            {
                throw new InvalidOperationException(
                    $"Attachment exceeds the {(maxBytes / (1024 * 1024)).ToString()} MB per-file limit.");
            }

            return bytesWritten == buffer.Length ? buffer : buffer[..bytesWritten];
        }

        private static string NormalizeDisplayFileName(string? fileName, string extension)
        {
            var candidate = Path.GetFileName(fileName ?? string.Empty);
            var baseName = Path.GetFileNameWithoutExtension(candidate);
            if (string.IsNullOrWhiteSpace(baseName))
            {
                return $"attachment{extension}";
            }

            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = new string(baseName.Where(ch => !invalidChars.Contains(ch)).ToArray());
            if (string.IsNullOrWhiteSpace(sanitized))
            {
                sanitized = "attachment";
            }

            if (sanitized.Length > 80)
            {
                sanitized = sanitized[..80];
            }

            return $"{sanitized}{extension}";
        }

        private static void ValidateAttachmentSignature(string mimeType, byte[] bytes)
        {
            if (bytes.Length == 0)
            {
                throw new InvalidOperationException("Attachment payload is empty.");
            }

            bool isValid = mimeType.ToLowerInvariant() switch
            {
                "application/pdf" => StartsWith(bytes, "%PDF"u8),
                "image/png" => StartsWith(bytes, new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
                "image/jpeg" => StartsWith(bytes, new byte[] { 0xFF, 0xD8, 0xFF }),
                "image/webp" => StartsWith(bytes, "RIFF"u8) && bytes.Length > 12 && StartsWith(bytes.AsSpan(8).ToArray(), "WEBP"u8),
                "application/msword" or "application/vnd.ms-excel" or "application/vnd.ms-powerpoint" =>
                    StartsWith(bytes, new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 }),
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
                    or "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
                    or "application/vnd.openxmlformats-officedocument.presentationml.presentation" =>
                    StartsWith(bytes, new byte[] { 0x50, 0x4B, 0x03, 0x04 }) || StartsWith(bytes, new byte[] { 0x50, 0x4B, 0x05, 0x06 }),
                "text/plain" => !bytes.Contains((byte)0),
                _ => false
            };

            if (!isValid)
            {
                throw new InvalidOperationException("Attachment content does not match the declared MIME type.");
            }
        }

        private static bool StartsWith(byte[] bytes, ReadOnlySpan<byte> signature)
        {
            return bytes.AsSpan().StartsWith(signature);
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

        private bool IsEmployeeAllowedForClientMatter(Employee employee, Matter matter)
        {
            if (string.IsNullOrWhiteSpace(matter.ResponsibleAttorney))
            {
                return false;
            }

            var responsibleAttorney = matter.ResponsibleAttorney.Trim();
            var employeeName = $"{employee.FirstName} {employee.LastName}".Trim();
            return string.Equals(responsibleAttorney, employeeName, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(responsibleAttorney, employee.Email?.Trim(), StringComparison.OrdinalIgnoreCase);
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

        private static IReadOnlyList<object> ParsePublicAttachments(string? attachmentsJson)
        {
            if (string.IsNullOrWhiteSpace(attachmentsJson))
            {
                return Array.Empty<object>();
            }

            try
            {
                var attachments = JsonSerializer.Deserialize<List<MessageAttachment>>(attachmentsJson);
                if (attachments == null || attachments.Count == 0)
                {
                    return Array.Empty<object>();
                }

                return attachments.Select(a => (object)new
                {
                    name = a.FileName,
                    url = a.FilePath,
                    mimeType = a.MimeType,
                    size = a.Size
                }).ToList();
            }
            catch (JsonException)
            {
                return Array.Empty<object>();
            }
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

    public class AttachmentDto
    {
        public string? FileName { get; set; }
        public string? Name { get; set; }
        public long Size { get; set; }
        public string? Type { get; set; }
        public string Data { get; set; } = string.Empty; // data URL base64
    }
}
