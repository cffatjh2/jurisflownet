using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;

namespace JurisFlow.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Policy = "StaffOnly")]
    public class NotificationsController : ControllerBase
    {
        private readonly JurisFlowDbContext _context;

        public NotificationsController(JurisFlowDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Notification>>> Get([FromQuery] string? userId, [FromQuery] string? clientId)
        {
            if (IsClient()) return Forbid();

            var currentUserId = GetUserId();
            if (string.IsNullOrWhiteSpace(currentUserId)) return Unauthorized();

            var targetUserId = string.IsNullOrWhiteSpace(userId) ? currentUserId : userId;
            if (!IsAdmin() && !string.Equals(targetUserId, currentUserId, StringComparison.OrdinalIgnoreCase))
            {
                return Forbid();
            }

            var query = _context.Notifications.AsNoTracking().AsQueryable();
            query = query.Where(n => n.UserId == targetUserId);

            if (IsAdmin() && !string.IsNullOrWhiteSpace(clientId))
            {
                query = query.Where(n => n.ClientId == clientId);
            }

            var items = await query.OrderByDescending(n => n.CreatedAt).Take(100).ToListAsync();
            return Ok(items);
        }

        [HttpPost("{id}/read")]
        public async Task<IActionResult> MarkRead(string id)
        {
            if (IsClient()) return Forbid();
            var currentUserId = GetUserId();
            if (string.IsNullOrWhiteSpace(currentUserId)) return Unauthorized();

            var notif = await _context.Notifications.FindAsync(id);
            if (notif == null) return NotFound();
            if (!IsAdmin() && notif.UserId != currentUserId) return Forbid();

            notif.Read = true;
            await _context.SaveChangesAsync();
            return NoContent();
        }

        [HttpPost("{id}/unread")]
        public async Task<IActionResult> MarkUnread(string id)
        {
            if (IsClient()) return Forbid();
            var currentUserId = GetUserId();
            if (string.IsNullOrWhiteSpace(currentUserId)) return Unauthorized();

            var notif = await _context.Notifications.FindAsync(id);
            if (notif == null) return NotFound();
            if (!IsAdmin() && notif.UserId != currentUserId) return Forbid();

            notif.Read = false;
            await _context.SaveChangesAsync();
            return NoContent();
        }

        [HttpPost("read-all")]
        public async Task<IActionResult> ReadAll([FromQuery] string? userId, [FromQuery] string? clientId)
        {
            if (IsClient()) return Forbid();
            var currentUserId = GetUserId();
            if (string.IsNullOrWhiteSpace(currentUserId)) return Unauthorized();

            var targetUserId = string.IsNullOrWhiteSpace(userId) ? currentUserId : userId;
            if (!IsAdmin() && !string.Equals(targetUserId, currentUserId, StringComparison.OrdinalIgnoreCase))
            {
                return Forbid();
            }

            var query = _context.Notifications.AsQueryable();
            query = query.Where(n => n.UserId == targetUserId);
            if (IsAdmin() && !string.IsNullOrWhiteSpace(clientId))
            {
                query = query.Where(n => n.ClientId == clientId);
            }

            var list = await query.ToListAsync();
            foreach (var n in list)
            {
                n.Read = true;
            }
            await _context.SaveChangesAsync();
            return NoContent();
        }

        private bool IsClient()
        {
            return User.IsInRole("Client") || string.Equals(User.FindFirst("role")?.Value, "Client", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsAdmin()
        {
            return User.IsInRole("Admin") || string.Equals(User.FindFirst("role")?.Value, "Admin", StringComparison.OrdinalIgnoreCase);
        }

        private string? GetUserId()
        {
            return User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                   ?? User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        }
    }
}
