using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
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
        private readonly IAppFileStorage _fileStorage;
        private readonly JurisFlowDbContext _context;
        private readonly TenantContext _tenantContext;
        private readonly FileExtensionContentTypeProvider _contentTypeProvider = new();

        public FilesController(IAppFileStorage fileStorage, JurisFlowDbContext context, TenantContext tenantContext)
        {
            _fileStorage = fileStorage;
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

            var path = $"uploads/{tenantId}/message-attachments/{safeName}";
            if (!await _fileStorage.ExistsAsync(path))
            {
                return NotFound(new { message = "File not found." });
            }

            var bytes = await _fileStorage.ReadBytesAsync(path);
            return File(bytes, GetContentType(safeName), safeName);
        }

        [HttpGet("avatars/{fileName}")]
        public async Task<IActionResult> GetAvatar(string fileName)
        {
            var tenantId = RequireTenantId();
            var safeName = Path.GetFileName(fileName);
            if (!string.Equals(fileName, safeName, StringComparison.Ordinal))
            {
                return BadRequest(new { message = "Invalid file name." });
            }

            var path = $"uploads/{tenantId}/avatars/{safeName}";
            if (!await _fileStorage.ExistsAsync(path))
            {
                return NotFound(new { message = "File not found." });
            }

            var bytes = await _fileStorage.ReadBytesAsync(path);
            return File(bytes, GetContentType(safeName), safeName);
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

                return await TenantScope(_context.MessageAttachments)
                        .AsNoTracking()
                        .AnyAsync(a =>
                            a.MessageType == "client" &&
                            a.StoredFileName == fileName &&
                            a.ClientId == clientId);
            }

            var currentUserId = GetUserId();
            var currentEmployeeId = await ResolveEmployeeIdAsync();

            if (await TenantScope(_context.MessageAttachments)
                    .AsNoTracking()
                    .AnyAsync(a =>
                        a.StoredFileName == fileName &&
                        a.MessageType == "client" &&
                        (IsAdmin() ||
                         (!string.IsNullOrWhiteSpace(currentUserId) && a.SenderUserId == currentUserId) ||
                         (!string.IsNullOrWhiteSpace(currentEmployeeId) && a.MessageEmployeeId == currentEmployeeId))))
            {
                return true;
            }

            return await TenantScope(_context.MessageAttachments)
                    .AsNoTracking()
                    .AnyAsync(a =>
                        a.StoredFileName == fileName &&
                        a.MessageType == "staff" &&
                        (IsAdmin() ||
                         (!string.IsNullOrWhiteSpace(currentEmployeeId) &&
                          (a.SenderEmployeeId == currentEmployeeId || a.RecipientEmployeeId == currentEmployeeId))));
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
