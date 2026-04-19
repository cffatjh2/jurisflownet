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
    [RequestSizeLimit(MaxMatterNoteRequestBodyBytes)]
    public class MatterNotesController : ControllerBase
    {
        private const int MaxMatterNoteRequestBodyBytes = 64 * 1024;
        private const int DefaultMatterNotePageSize = 50;
        private const int MaxMatterNotePageSize = 200;

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
        public async Task<ActionResult<IEnumerable<MatterNoteResponseDto>>> GetNotes(
            string matterId,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = DefaultMatterNotePageSize,
            CancellationToken cancellationToken = default)
        {
            var matter = await _context.Matters
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == matterId, cancellationToken);
            if (matter == null)
            {
                return NotFoundProblem("Matter not found.", $"Matter '{matterId}' was not found.");
            }

            if (!_matterAccess.CanReadMatterNotes(matter, User))
            {
                return Problem(statusCode: StatusCodes.Status403Forbidden, title: "Notes read is forbidden.", detail: $"You are not allowed to read notes for matter '{matterId}'.");
            }

            var normalizedPage = NormalizePage(page);
            var normalizedPageSize = NormalizePageSize(pageSize);

            var baseQuery = _context.MatterNotes
                .AsNoTracking()
                .Where(note => note.MatterId == matterId)
                .OrderByDescending(note => note.UpdatedAt);

            var totalCount = await baseQuery.CountAsync(cancellationToken);
            var notes = await baseQuery
                .Skip((normalizedPage - 1) * normalizedPageSize)
                .Take(normalizedPageSize)
                .ToListAsync(cancellationToken);

            AddPaginationHeaders(totalCount, normalizedPage, normalizedPageSize);
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
                return NotFoundProblem("Matter not found.", $"Matter '{matterId}' was not found.");
            }

            if (!await _matterAccess.CanCreateMatterNoteAsync(matter, User, cancellationToken))
            {
                return Problem(statusCode: StatusCodes.Status403Forbidden, title: "Note create is forbidden.", detail: $"You are not allowed to create notes for matter '{matterId}'.");
            }

            var body = NormalizeBody(dto.Body);
            if (body == null)
            {
                return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Note body is required.", detail: "Note body is required.");
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
                return NotFoundProblem("Note not found.", $"Note '{noteId}' was not found.");
            }

            var matter = await _context.Matters
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == matterId, cancellationToken);
            if (matter == null)
            {
                return NotFoundProblem("Matter not found.", $"Matter '{matterId}' was not found.");
            }

            if (!await _matterAccess.CanEditMatterNoteAsync(matter, note, User, cancellationToken))
            {
                return Problem(statusCode: StatusCodes.Status403Forbidden, title: "Note update is forbidden.", detail: $"You are not allowed to update note '{noteId}'.");
            }

            var body = NormalizeBody(dto.Body);
            if (body == null)
            {
                return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Note body is required.", detail: "Note body is required.");
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
                return NotFoundProblem("Note not found.", $"Note '{noteId}' was not found.");
            }

            var matter = await _context.Matters
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == matterId, cancellationToken);
            if (matter == null)
            {
                return NotFoundProblem("Matter not found.", $"Matter '{matterId}' was not found.");
            }

            if (!await _matterAccess.CanDeleteMatterNoteAsync(matter, note, User, cancellationToken))
            {
                return Problem(statusCode: StatusCodes.Status403Forbidden, title: "Note delete is forbidden.", detail: $"You are not allowed to delete note '{noteId}'.");
            }

            _context.MatterNotes.Remove(note);
            await _context.SaveChangesAsync(cancellationToken);
            await _auditLogger.LogAsync(HttpContext, "matter.note.delete", nameof(MatterNote), note.Id, $"MatterId={matterId}");
            return NoContent();
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

        private static int NormalizePage(int page) => page <= 0 ? 1 : page;

        private static int NormalizePageSize(int pageSize)
        {
            if (pageSize <= 0)
            {
                return DefaultMatterNotePageSize;
            }

            return Math.Clamp(pageSize, 1, MaxMatterNotePageSize);
        }

        private void AddPaginationHeaders(int totalCount, int page, int pageSize)
        {
            Response.Headers["X-Total-Count"] = totalCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Response.Headers["X-Page"] = page.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Response.Headers["X-Page-Size"] = pageSize.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private ActionResult NotFoundProblem(string title, string detail)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, title: title, detail: detail);
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
