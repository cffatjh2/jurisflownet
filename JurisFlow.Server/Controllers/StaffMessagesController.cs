using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using JurisFlow.Server.Services;
using System.Text.Json;

namespace JurisFlow.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Policy = "StaffOnly")]
    public class StaffMessagesController : ControllerBase
    {
        private readonly JurisFlowDbContext _context;
        private readonly IAppFileStorage _fileStorage;
        private readonly TenantContext _tenantContext;
        private const int MaxAttachmentCount = 10;
        private const int MaxAttachmentSizeBytes = 10 * 1024 * 1024;
        private const int MaxTotalAttachmentSizeBytes = 25 * 1024 * 1024;
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

        public StaffMessagesController(JurisFlowDbContext context, IAppFileStorage fileStorage, TenantContext tenantContext)
        {
            _context = context;
            _fileStorage = fileStorage;
            _tenantContext = tenantContext;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<StaffMessage>>> GetMessages([FromQuery] string? userId)
        {
            var query = _context.StaffMessages.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(userId))
            {
                query = query.Where(m => m.SenderId == userId || m.RecipientId == userId);
            }

            var items = await query
                .OrderByDescending(m => m.CreatedAt)
                .Take(200)
                .ToListAsync();

            return Ok(items);
        }

        [HttpGet("thread")]
        public async Task<ActionResult<IEnumerable<StaffMessage>>> GetThread([FromQuery] string userA, [FromQuery] string userB)
        {
            if (string.IsNullOrWhiteSpace(userA) || string.IsNullOrWhiteSpace(userB))
            {
                return BadRequest("Both userA and userB are required.");
            }

            var thread = await _context.StaffMessages
                .AsNoTracking()
                .Where(m => (m.SenderId == userA && m.RecipientId == userB) || (m.SenderId == userB && m.RecipientId == userA))
                .OrderBy(m => m.CreatedAt)
                .ToListAsync();

            return Ok(thread);
        }

        [HttpPost]
        public async Task<ActionResult<StaffMessage>> SendMessage([FromBody] StaffMessageCreateDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
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

            var message = new StaffMessage
            {
                SenderId = dto.SenderId,
                RecipientId = dto.RecipientId,
                Body = dto.Body.Trim(),
                Status = "Unread",
                CreatedAt = DateTime.UtcNow,
                AttachmentsJson = attachments.Count > 0 ? JsonSerializer.Serialize(attachments) : null
            };

            _context.StaffMessages.Add(message);
            await _context.SaveChangesAsync();

            // Create notification for recipient if user exists
            var recipient = await _context.Employees.Include(e => e.User).FirstOrDefaultAsync(e => e.Id == dto.RecipientId);
            if (recipient?.User != null)
            {
            _context.Notifications.Add(new Notification
            {
                UserId = recipient.UserId,
                Title = "New direct message",
                Message = $"You have a message from {dto.SenderId}",
                Type = "info",
                Link = "tab:communications"
            });
            await _context.SaveChangesAsync();
        }

        return CreatedAtAction(nameof(GetMessages), new { id = message.Id }, message);
    }

        [HttpPost("{id}/read")]
        public async Task<IActionResult> MarkRead(string id)
        {
            var message = await _context.StaffMessages.FindAsync(id);
            if (message == null) return NotFound();

            message.Status = "Read";
            message.ReadAt = DateTime.UtcNow;
            _context.StaffMessages.Update(message);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        public class StaffMessageCreateDto
        {
            [System.ComponentModel.DataAnnotations.Required]
            public string SenderId { get; set; } = string.Empty;

            [System.ComponentModel.DataAnnotations.Required]
            public string RecipientId { get; set; } = string.Empty;

            [System.ComponentModel.DataAnnotations.Required]
            public string Body { get; set; } = string.Empty;

            public List<AttachmentDto>? Attachments { get; set; }
        }

        private async Task<List<MessageAttachment>> SaveAttachments(List<AttachmentDto>? attachments)
        {
            var result = new List<MessageAttachment>();
            if (attachments == null || attachments.Count == 0) return result;

            if (attachments.Count > MaxAttachmentCount)
            {
                throw new InvalidOperationException($"A maximum of {MaxAttachmentCount} attachments is allowed per message.");
            }

            long totalBytes = 0;
            foreach (var att in attachments)
            {
                var (mimeType, base64Payload) = ParseAttachmentData(att);
                if (!AllowedMimeToExtension.TryGetValue(mimeType, out var ext))
                {
                    throw new InvalidOperationException($"Attachment MIME type '{mimeType}' is not allowed.");
                }

                var bytes = DecodeBase64(base64Payload, MaxAttachmentSizeBytes);
                totalBytes += bytes.Length;
                if (totalBytes > MaxTotalAttachmentSizeBytes)
                {
                    throw new InvalidOperationException(
                        $"Total attachment payload exceeds the {(MaxTotalAttachmentSizeBytes / (1024 * 1024)).ToString()} MB limit.");
                }

                var fileName = $"{Guid.NewGuid():N}{ext}";
                await _fileStorage.SaveBytesAsync(GetMessageAttachmentPath(fileName), bytes, mimeType);

                result.Add(new MessageAttachment
                {
                    FileName = NormalizeDisplayFileName(att.FileName, ext),
                    FilePath = $"/api/files/messages/{fileName}",
                    MimeType = mimeType,
                    Size = bytes.Length
                });
            }
            return result;
        }

        private string GetMessageAttachmentPath(string fileName)
        {
            if (string.IsNullOrWhiteSpace(_tenantContext.TenantId))
            {
                throw new InvalidOperationException("Tenant context is missing.");
            }

            return $"uploads/{_tenantContext.TenantId}/message-attachments/{fileName}";
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
    }
}
