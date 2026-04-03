using System.Security.Claims;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using JurisFlow.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JurisFlow.Server.Controllers
{
    [Route("api/matters/{matterId}/notes")]
    [ApiController]
    [Authorize(Policy = "StaffOnly")]
    public class MatterNotesController : ControllerBase
    {
        private readonly JurisFlowDbContext _context;
        private readonly MatterAccessService _matterAccess;
        private readonly AuditLogger _auditLogger;

        public MatterNotesController(
            JurisFlowDbContext context,
            MatterAccessService matterAccess,
            AuditLogger auditLogger)
        {
            _context = context;
            _matterAccess = matterAccess;
            _auditLogger = auditLogger;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<MatterNoteResponseDto>>> GetNotes(string matterId, CancellationToken cancellationToken)
        {
            var matter = await _context.Matters
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == matterId, cancellationToken);
            if (matter == null)
            {
                return NotFound();
            }

            if (!_matterAccess.CanSeeMatterNotes(matter, User))
            {
                return Forbid();
            }

            var notes = await _context.MatterNotes
                .AsNoTracking()
                .Where(note => note.MatterId == matterId)
                .OrderByDescending(note => note.UpdatedAt)
                .ToListAsync(cancellationToken);

            return Ok(await MapNotesAsync(notes, cancellationToken));
        }

        [HttpPost]
        public async Task<ActionResult<MatterNoteResponseDto>> CreateNote(string matterId, [FromBody] MatterNoteUpsertDto dto, CancellationToken cancellationToken)
        {
            var matter = await _context.Matters
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == matterId, cancellationToken);
            if (matter == null)
            {
                return NotFound();
            }

            if (!_matterAccess.CanSeeMatterNotes(matter, User))
            {
                return Forbid();
            }

            var body = NormalizeBody(dto.Body);
            if (body == null)
            {
                return BadRequest(new { message = "Note body is required." });
            }

            var currentUserId = GetUserId();
            var now = DateTime.UtcNow;
            var note = new MatterNote
            {
                Id = Guid.NewGuid().ToString(),
                MatterId = matterId,
                Title = NormalizeTitle(dto.Title),
                Body = body,
                CreatedByUserId = currentUserId,
                UpdatedByUserId = currentUserId,
                CreatedAt = now,
                UpdatedAt = now
            };

            _context.MatterNotes.Add(note);
            await _context.SaveChangesAsync(cancellationToken);
            await _auditLogger.LogAsync(HttpContext, "matter.note.create", nameof(MatterNote), note.Id, $"MatterId={matterId}");

            var mapped = (await MapNotesAsync(new List<MatterNote> { note }, cancellationToken)).Single();
            return Created($"/api/matters/{matterId}/notes/{note.Id}", mapped);
        }

        [HttpPut("{noteId}")]
        public async Task<ActionResult<MatterNoteResponseDto>> UpdateNote(
            string matterId,
            string noteId,
            [FromBody] MatterNoteUpsertDto dto,
            CancellationToken cancellationToken)
        {
            var note = await _context.MatterNotes.FirstOrDefaultAsync(n => n.Id == noteId && n.MatterId == matterId, cancellationToken);
            if (note == null)
            {
                return NotFound();
            }

            var matter = await _context.Matters
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == matterId, cancellationToken);
            if (matter == null)
            {
                return NotFound();
            }

            if (!_matterAccess.CanSeeMatterNotes(matter, User))
            {
                return Forbid();
            }

            var body = NormalizeBody(dto.Body);
            if (body == null)
            {
                return BadRequest(new { message = "Note body is required." });
            }

            note.Title = NormalizeTitle(dto.Title);
            note.Body = body;
            note.UpdatedByUserId = GetUserId();
            note.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(cancellationToken);
            await _auditLogger.LogAsync(HttpContext, "matter.note.update", nameof(MatterNote), note.Id, $"MatterId={matterId}");

            var mapped = (await MapNotesAsync(new List<MatterNote> { note }, cancellationToken)).Single();
            return Ok(mapped);
        }

        [HttpDelete("{noteId}")]
        public async Task<IActionResult> DeleteNote(string matterId, string noteId, CancellationToken cancellationToken)
        {
            var note = await _context.MatterNotes.FirstOrDefaultAsync(n => n.Id == noteId && n.MatterId == matterId, cancellationToken);
            if (note == null)
            {
                return NotFound();
            }

            var matter = await _context.Matters
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == matterId, cancellationToken);
            if (matter == null)
            {
                return NotFound();
            }

            if (!await CanDeleteNoteAsync(matter, note, cancellationToken))
            {
                return Forbid();
            }

            _context.MatterNotes.Remove(note);
            await _context.SaveChangesAsync(cancellationToken);
            await _auditLogger.LogAsync(HttpContext, "matter.note.delete", nameof(MatterNote), note.Id, $"MatterId={matterId}");
            return NoContent();
        }

        private async Task<bool> CanDeleteNoteAsync(Matter matter, MatterNote note, CancellationToken cancellationToken)
        {
            if (_matterAccess.IsPrivileged(User))
            {
                return true;
            }

            var currentUserId = GetUserId();
            if (!string.IsNullOrWhiteSpace(currentUserId) &&
                string.Equals(currentUserId, note.CreatedByUserId, StringComparison.Ordinal))
            {
                return true;
            }

            return await _matterAccess.CanManageMatterAsync(matter.Id, User, cancellationToken: cancellationToken);
        }

        private async Task<List<MatterNoteResponseDto>> MapNotesAsync(IReadOnlyCollection<MatterNote> notes, CancellationToken cancellationToken)
        {
            if (notes.Count == 0)
            {
                return new List<MatterNoteResponseDto>();
            }

            var userIds = notes
                .SelectMany(note => new[] { note.CreatedByUserId, note.UpdatedByUserId })
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var userLookup = userIds.Count == 0
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : await _context.Users
                    .AsNoTracking()
                    .Where(user => userIds.Contains(user.Id))
                    .ToDictionaryAsync(user => user.Id, user => user.Name, StringComparer.Ordinal, cancellationToken);

            return notes.Select(note => new MatterNoteResponseDto
            {
                Id = note.Id,
                MatterId = note.MatterId,
                Title = note.Title,
                Body = note.Body,
                CreatedByUserId = note.CreatedByUserId,
                CreatedByName = ResolveUserName(note.CreatedByUserId, userLookup),
                UpdatedByUserId = note.UpdatedByUserId,
                UpdatedByName = ResolveUserName(note.UpdatedByUserId, userLookup),
                CreatedAt = note.CreatedAt,
                UpdatedAt = note.UpdatedAt
            }).ToList();
        }

        private static string? NormalizeTitle(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var trimmed = value.Trim();
            return trimmed.Length <= 250 ? trimmed : trimmed[..250];
        }

        private static string? NormalizeBody(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var trimmed = value.Trim();
            return trimmed.Length <= 10000 ? trimmed : trimmed[..10000];
        }

        private static string ResolveUserName(string? userId, IReadOnlyDictionary<string, string> userLookup)
        {
            if (!string.IsNullOrWhiteSpace(userId) && userLookup.TryGetValue(userId, out var name) && !string.IsNullOrWhiteSpace(name))
            {
                return name;
            }

            return "Staff";
        }

        private string? GetUserId()
        {
            return User.FindFirst("sub")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        }
    }

    public sealed class MatterNoteUpsertDto
    {
        public string? Title { get; set; }
        public string? Body { get; set; }
    }

    public sealed class MatterNoteResponseDto
    {
        public string Id { get; set; } = string.Empty;
        public string MatterId { get; set; } = string.Empty;
        public string? Title { get; set; }
        public string Body { get; set; } = string.Empty;
        public string? CreatedByUserId { get; set; }
        public string CreatedByName { get; set; } = "Staff";
        public string? UpdatedByUserId { get; set; }
        public string UpdatedByName { get; set; } = "Staff";
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
