using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using JurisFlow.Server.Data;
using JurisFlow.Server.DTOs;
using JurisFlow.Server.Models;
using JurisFlow.Server.Services;
using System.Text.Json;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;

namespace JurisFlow.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Policy = "StaffOnly")]
    public class StaffMessagesController : ControllerBase
    {
        private readonly JurisFlowDbContext _context;
        private readonly MessageAttachmentIntakeService _attachmentIntake;
        private readonly MessageAttachmentIndexService _attachmentIndex;
        private readonly AuditLogger _auditLogger;
        private readonly TenantContext _tenantContext;
        private const int MaxMessageRequestBodyBytes = 40 * 1024 * 1024;

        public StaffMessagesController(
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
        public async Task<ActionResult<IEnumerable<object>>> GetMessages()
        {
            var currentEmployeeId = await ResolveCurrentEmployeeIdAsync();
            if (string.IsNullOrWhiteSpace(currentEmployeeId))
            {
                return Forbid();
            }

            var items = await TenantScope(_context.StaffMessages)
                .AsNoTracking()
                .Where(m => m.SenderId == currentEmployeeId || m.RecipientId == currentEmployeeId)
                .OrderByDescending(m => m.CreatedAt)
                .Take(200)
                .ToListAsync();

            var attachmentMap = await LoadAttachmentMapAsync(items.Select(m => m.Id));
            return Ok(items.Select(m => ToResponse(m, GetPublicAttachments(attachmentMap, m.Id))));
        }

        [HttpGet("thread")]
        public async Task<ActionResult<IEnumerable<object>>> GetThread([FromQuery] string participantId)
        {
            if (string.IsNullOrWhiteSpace(participantId))
            {
                return BadRequest(new { message = "participantId is required." });
            }

            var currentEmployeeId = await ResolveCurrentEmployeeIdAsync();
            if (string.IsNullOrWhiteSpace(currentEmployeeId))
            {
                return Forbid();
            }

            var normalizedParticipantId = participantId.Trim();
            var participantExists = await TenantScope(_context.Employees)
                .AsNoTracking()
                .AnyAsync(e => e.Id == normalizedParticipantId);
            if (!participantExists)
            {
                return NotFound(new { message = "Participant not found for this tenant." });
            }

            var thread = await TenantScope(_context.StaffMessages)
                .AsNoTracking()
                .Where(m =>
                    (m.SenderId == currentEmployeeId && m.RecipientId == normalizedParticipantId) ||
                    (m.SenderId == normalizedParticipantId && m.RecipientId == currentEmployeeId))
                .OrderBy(m => m.CreatedAt)
                .ToListAsync();

            var attachmentMap = await LoadAttachmentMapAsync(thread.Select(m => m.Id));
            return Ok(thread.Select(m => ToResponse(m, GetPublicAttachments(attachmentMap, m.Id))));
        }

        [HttpPost]
        [EnableRateLimiting("StaffMessagingSend")]
        [RequestSizeLimit(MaxMessageRequestBodyBytes)]
        public async Task<ActionResult<object>> SendMessage([FromBody] StaffMessageCreateDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var sender = await ResolveCurrentEmployeeAsync();
            if (sender == null)
            {
                return Forbid();
            }

            var recipient = await TenantScope(_context.Employees)
                .Include(e => e.User)
                .FirstOrDefaultAsync(e => e.Id == dto.RecipientId);
            if (recipient == null)
            {
                return BadRequest(new { message = "Recipient not found." });
            }

            if (string.Equals(recipient.Id, sender.Id, StringComparison.Ordinal))
            {
                return BadRequest(new { message = "You cannot send a direct message to yourself." });
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

            var message = new StaffMessage
            {
                SenderId = sender.Id,
                RecipientId = dto.RecipientId,
                Body = dto.Body.Trim(),
                Status = "Unread",
                CreatedAt = DateTime.UtcNow,
                AttachmentsJson = attachments.Count > 0 ? JsonSerializer.Serialize(attachments) : null
            };

            _context.StaffMessages.Add(message);
            await _attachmentIndex.IndexStaffMessageAsync(message, attachments, HttpContext.RequestAborted);

            // Create notification for recipient if user exists
            if (recipient?.User != null)
            {
                _context.Notifications.Add(new Notification
                {
                    UserId = recipient.UserId,
                    Title = "New direct message",
                    Message = $"You have a message from {sender.FirstName} {sender.LastName}".Trim(),
                    Type = "info",
                    Link = "tab:communications"
                });
            }

            await _context.SaveChangesAsync();
            await _auditLogger.LogAsync(
                HttpContext,
                "staff.message.send",
                nameof(StaffMessage),
                message.Id,
                $"SenderEmployeeId={sender.Id}, RecipientEmployeeId={recipient.Id}");

            return CreatedAtAction(nameof(GetMessages), new { id = message.Id }, ToResponse(message, ToPublicAttachments(attachments)));
        }

        [HttpPost("{id}/read")]
        public async Task<IActionResult> MarkRead(string id)
        {
            var currentEmployeeId = await ResolveCurrentEmployeeIdAsync();
            if (string.IsNullOrWhiteSpace(currentEmployeeId))
            {
                return Forbid();
            }

            var message = await TenantScope(_context.StaffMessages).FirstOrDefaultAsync(m => m.Id == id);
            if (message == null) return NotFound();
            if (!string.Equals(message.RecipientId, currentEmployeeId, StringComparison.Ordinal))
            {
                return Forbid();
            }

            message.Status = "Read";
            message.ReadAt = DateTime.UtcNow;
            _context.StaffMessages.Update(message);
            await _context.SaveChangesAsync();
            await _auditLogger.LogAsync(HttpContext, "staff.message.read", nameof(StaffMessage), message.Id, null);

            return NoContent();
        }

        public class StaffMessageCreateDto
        {
            [System.ComponentModel.DataAnnotations.Required]
            public string RecipientId { get; set; } = string.Empty;

            [System.ComponentModel.DataAnnotations.Required]
            public string Body { get; set; } = string.Empty;

            public List<AttachmentDto>? Attachments { get; set; }
        }

        private async Task<Employee?> ResolveCurrentEmployeeAsync()
        {
            var userId = GetUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return null;
            }

            return await TenantScope(_context.Employees)
                .Include(e => e.User)
                .FirstOrDefaultAsync(e => e.UserId == userId);
        }

        private async Task<string?> ResolveCurrentEmployeeIdAsync()
        {
            var employee = await ResolveCurrentEmployeeAsync();
            return employee?.Id;
        }

        private IQueryable<T> TenantScope<T>(IQueryable<T> query) where T : class
        {
            var tenantId = RequireTenantId();
            return query.Where(entity => EF.Property<string>(entity, "TenantId") == tenantId);
        }

        private string RequireTenantId()
        {
            if (string.IsNullOrWhiteSpace(_tenantContext.TenantId))
            {
                throw new InvalidOperationException("Tenant context is missing.");
            }

            return _tenantContext.TenantId;
        }

        private string? GetUserId()
        {
            return User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                   ?? User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
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
                .Where(a => a.MessageType == "staff" && ids.Contains(a.MessageId))
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

        private static object ToResponse(StaffMessage message, IReadOnlyList<object> attachments)
        {
            return new
            {
                id = message.Id,
                senderId = message.SenderId,
                recipientId = message.RecipientId,
                body = message.Body,
                status = message.Status,
                createdAt = message.CreatedAt,
                readAt = message.ReadAt,
                attachments
            };
        }
    }
}
