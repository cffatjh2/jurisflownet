using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using JurisFlow.Server.Services;

namespace JurisFlow.Server.Controllers
{
    [Route("api/files")]
    [ApiController]
    [Authorize(Policy = "StaffOrClient")]
    public class FilesController : ControllerBase
    {
        private readonly IWebHostEnvironment _env;
        private readonly JurisFlowDbContext _context;
        private readonly TenantContext _tenantContext;
        private readonly FileExtensionContentTypeProvider _contentTypeProvider = new();

        public FilesController(IWebHostEnvironment env, JurisFlowDbContext context, TenantContext tenantContext)
        {
            _env = env;
            _context = context;
            _tenantContext = tenantContext;
        }

        [HttpGet("messages/{fileName}")]
        public async Task<IActionResult> GetMessageAttachment(string fileName)
        {
            var tenantId = RequireTenantId();
            var safeName = Path.GetFileName(fileName);
            if (!string.Equals(fileName, safeName, StringComparison.Ordinal))
            {
                return BadRequest(new { message = "Invalid file name." });
            }

            if (!await CanCurrentUserAccessMessageAttachmentAsync(safeName))
            {
                return Forbid();
            }

            var path = Path.Combine(_env.ContentRootPath, "uploads", tenantId, "message-attachments", safeName);
            if (!System.IO.File.Exists(path))
            {
                return NotFound(new { message = "File not found." });
            }

            return PhysicalFile(path, GetContentType(path), safeName);
        }

        [HttpGet("avatars/{fileName}")]
        public IActionResult GetAvatar(string fileName)
        {
            var tenantId = RequireTenantId();
            var safeName = Path.GetFileName(fileName);
            if (!string.Equals(fileName, safeName, StringComparison.Ordinal))
            {
                return BadRequest(new { message = "Invalid file name." });
            }

            var path = Path.Combine(_env.ContentRootPath, "uploads", tenantId, "avatars", safeName);
            if (!System.IO.File.Exists(path))
            {
                return NotFound(new { message = "File not found." });
            }

            return PhysicalFile(path, GetContentType(path), safeName);
        }

        private string RequireTenantId()
        {
            if (string.IsNullOrWhiteSpace(_tenantContext.TenantId))
            {
                throw new InvalidOperationException("Tenant context is missing.");
            }
            return _tenantContext.TenantId;
        }

        private async Task<bool> CanCurrentUserAccessMessageAttachmentAsync(string fileName)
        {
            if (IsClient())
            {
                var clientId = GetClientId();
                if (string.IsNullOrWhiteSpace(clientId))
                {
                    return false;
                }

                return await QueryContainsAttachmentAsync(
                    TenantScope(_context.ClientMessages)
                        .AsNoTracking()
                        .Where(m => m.ClientId == clientId)
                        .Select(m => m.AttachmentsJson),
                    fileName);
            }

            var currentUserId = GetUserId();
            var currentEmployeeId = await ResolveEmployeeIdAsync();

            if (await QueryContainsAttachmentAsync(
                    TenantScope(_context.ClientMessages)
                        .AsNoTracking()
                        .Where(m =>
                            IsAdmin() ||
                            (!string.IsNullOrWhiteSpace(currentUserId) && m.SenderUserId == currentUserId) ||
                            (!string.IsNullOrWhiteSpace(currentEmployeeId) && m.EmployeeId == currentEmployeeId))
                        .Select(m => m.AttachmentsJson),
                    fileName))
            {
                return true;
            }

            if (IsAdmin())
            {
                return await QueryContainsAttachmentAsync(
                    TenantScope(_context.StaffMessages)
                        .AsNoTracking()
                        .Select(m => m.AttachmentsJson),
                    fileName);
            }

            if (string.IsNullOrWhiteSpace(currentEmployeeId))
            {
                return false;
            }

            return await QueryContainsAttachmentAsync(
                TenantScope(_context.StaffMessages)
                    .AsNoTracking()
                    .Where(m => m.SenderId == currentEmployeeId || m.RecipientId == currentEmployeeId)
                    .Select(m => m.AttachmentsJson),
                fileName);
        }

        private static async Task<bool> QueryContainsAttachmentAsync(IQueryable<string?> attachmentsQuery, string fileName)
        {
            var attachmentsJsonRows = await attachmentsQuery.ToListAsync();
            foreach (var attachmentsJson in attachmentsJsonRows)
            {
                if (ContainsAttachment(attachmentsJson, fileName))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsAttachment(string? attachmentsJson, string fileName)
        {
            if (string.IsNullOrWhiteSpace(attachmentsJson))
            {
                return false;
            }

            try
            {
                var attachments = JsonSerializer.Deserialize<List<MessageAttachment>>(attachmentsJson);
                if (attachments == null || attachments.Count == 0)
                {
                    return false;
                }

                foreach (var attachment in attachments)
                {
                    var referencedFileName = ExtractStoredFileName(attachment.FilePath);
                    if (string.Equals(referencedFileName, fileName, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            catch (JsonException)
            {
                return false;
            }

            return false;
        }

        private static string? ExtractStoredFileName(string? filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return null;
            }

            var value = filePath.Trim();
            var queryIndex = value.IndexOfAny(new[] { '?', '#' });
            if (queryIndex >= 0)
            {
                value = value[..queryIndex];
            }

            value = value.Replace('\\', '/');
            return Path.GetFileName(value);
        }

        private bool IsClient()
        {
            return User.IsInRole("Client")
                   || string.Equals(User.FindFirst("role")?.Value, "Client", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsAdmin()
        {
            return User.IsInRole("Admin")
                   || string.Equals(User.FindFirst("role")?.Value, "Admin", StringComparison.OrdinalIgnoreCase);
        }

        private async Task<string?> ResolveEmployeeIdAsync()
        {
            var userId = GetUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return null;
            }

            return await _context.Employees
                .Where(e => EF.Property<string>(e, "TenantId") == RequireTenantId())
                .AsNoTracking()
                .Where(e => e.UserId == userId)
                .Select(e => e.Id)
                .FirstOrDefaultAsync();
        }

        private IQueryable<T> TenantScope<T>(IQueryable<T> query) where T : class
        {
            var tenantId = RequireTenantId();
            return query.Where(e => EF.Property<string>(e, "TenantId") == tenantId);
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

        private string GetContentType(string path)
        {
            if (_contentTypeProvider.TryGetContentType(path, out var contentType))
            {
                return contentType;
            }
            return "application/octet-stream";
        }
    }
}
