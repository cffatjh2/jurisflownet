using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using JurisFlow.Server.Services;
using Task = System.Threading.Tasks.Task;

namespace JurisFlow.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Policy = "StaffOnly")]
    public class MattersController : ControllerBase
    {
        private readonly JurisFlowDbContext _context;
        private readonly AuditLogger _auditLogger;
        private readonly FirmStructureService _firmStructure;
        private readonly OutcomeFeePlannerService _outcomeFeePlanner;
        private readonly ClientTransparencyService _clientTransparencyService;
        private readonly ILogger<MattersController> _logger;

        public MattersController(
            JurisFlowDbContext context,
            AuditLogger auditLogger,
            FirmStructureService firmStructure,
            OutcomeFeePlannerService outcomeFeePlanner,
            ClientTransparencyService clientTransparencyService,
            ILogger<MattersController> logger)
        {
            _context = context;
            _auditLogger = auditLogger;
            _firmStructure = firmStructure;
            _outcomeFeePlanner = outcomeFeePlanner;
            _clientTransparencyService = clientTransparencyService;
            _logger = logger;
        }

        // GET: api/Matters
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Matter>>> GetMatters([FromQuery] string? status, [FromQuery] string? entityId, [FromQuery] string? officeId)
        {
            var query = _context.Matters
                .AsNoTracking()
                .Include(m => m.Client)
                .AsQueryable();

            if (!string.IsNullOrEmpty(status))
            {
                // Frontend might send "Archive" or "Open"
                // The Matter model has 'Status' field.
                // Assuming "Archived" is the status for archive.
                if (status.ToLower() == "archive" || status.ToLower() == "archived")
                {
                     query = query.Where(m => m.Status == "Archived");
                }
                else
                {
                     query = query.Where(m => m.Status == status || (status == "Open" && m.Status != "Archived" && m.Status != "Closed"));
                }
            }

            if (!string.IsNullOrWhiteSpace(entityId))
            {
                query = query.Where(m => m.EntityId == entityId);
            }

            if (!string.IsNullOrWhiteSpace(officeId))
            {
                query = query.Where(m => m.OfficeId == officeId);
            }

            return await query.OrderByDescending(m => m.OpenDate).ToListAsync();
        }

        // GET: api/Matters/5
        [HttpGet("{id}")]
        public async Task<ActionResult<Matter>> GetMatter(string id)
        {
            var matter = await _context.Matters
                .AsNoTracking()
                .Include(m => m.Client)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (matter == null)
            {
                return NotFound();
            }

            return matter;
        }

        // POST: api/Matters
        [HttpPost]
        public async Task<ActionResult<Matter>> PostMatter(Matter matter)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            if (string.IsNullOrWhiteSpace(matter.ClientId))
            {
                return BadRequest(new { message = "ClientId is required." });
            }

            var resolvedClientId = await ResolveValidClientIdAsync(matter.ClientId);
            if (resolvedClientId == null)
            {
                return BadRequest(new { message = "Selected client was not found." });
            }

            matter.Id = Guid.NewGuid().ToString();
            matter.OpenDate = DateTime.UtcNow;
            matter.ClientId = resolvedClientId;

            var resolved = await _firmStructure.ResolveEntityOfficeAsync(matter.EntityId, matter.OfficeId);
            matter.EntityId = resolved.entityId;
            matter.OfficeId = resolved.officeId;
            
            _context.Matters.Add(matter);
            await _context.SaveChangesAsync();

            await _auditLogger.LogAsync(HttpContext, "matter.create", "Matter", matter.Id, $"ClientId={matter.ClientId}, Name={matter.Name}");

            return CreatedAtAction("GetMatter", new { id = matter.Id }, matter);
        }

        // PUT: api/Matters/5
        [HttpPut("{id}")]
        public async Task<IActionResult> PutMatter(string id, Matter matter)
        {
            if (id != matter.Id) return BadRequest();
            if (!ModelState.IsValid) return BadRequest(ModelState);
            if (string.IsNullOrWhiteSpace(matter.ClientId))
            {
                return BadRequest(new { message = "ClientId is required." });
            }

            var existingMatter = await _context.Matters.FirstOrDefaultAsync(m => m.Id == id);
            if (existingMatter == null)
            {
                return NotFound();
            }

            var resolvedClientId = await ResolveValidClientIdAsync(matter.ClientId);
            if (resolvedClientId == null)
            {
                return BadRequest(new { message = "Selected client was not found." });
            }

            var resolved = await _firmStructure.ResolveEntityOfficeAsync(matter.EntityId, matter.OfficeId);

            existingMatter.CaseNumber = matter.CaseNumber;
            existingMatter.Name = matter.Name;
            existingMatter.PracticeArea = matter.PracticeArea;
            existingMatter.CourtType = matter.CourtType;
            existingMatter.Outcome = matter.Outcome;
            existingMatter.Status = matter.Status;
            existingMatter.FeeStructure = matter.FeeStructure;
            existingMatter.OpenDate = matter.OpenDate;
            existingMatter.ResponsibleAttorney = matter.ResponsibleAttorney;
            existingMatter.BillableRate = matter.BillableRate;
            existingMatter.TrustBalance = matter.TrustBalance;
            existingMatter.CurrentOutcomeFeePlanId = matter.CurrentOutcomeFeePlanId;
            existingMatter.EntityId = resolved.entityId;
            existingMatter.OfficeId = resolved.officeId;
            existingMatter.ClientId = resolvedClientId;
            existingMatter.ConflictCheckDate = matter.ConflictCheckDate;
            existingMatter.ConflictCheckCleared = matter.ConflictCheckCleared;
            existingMatter.ConflictWaiverObtained = matter.ConflictWaiverObtained;

            await _context.SaveChangesAsync();

            await _auditLogger.LogAsync(HttpContext, "matter.update", "Matter", existingMatter.Id, $"Status={existingMatter.Status}");
            await TryTriggerOutcomeFeePlannerAsync(existingMatter.Id, "matter_update");

            return NoContent();
        }

        // DELETE: api/Matters/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteMatter(string id)
        {
            var matter = await _context.Matters.FirstOrDefaultAsync(m => m.Id == id);
            if (matter == null) return NotFound();

            _context.Matters.Remove(matter);
            await _context.SaveChangesAsync();

            await _auditLogger.LogAsync(HttpContext, "matter.delete", "Matter", id, $"Deleted matter {matter.Name}");

            return NoContent();
        }
        
        // POST: api/Matters/5/archive
        [HttpPost("{id}/archive")]
        public async Task<IActionResult> ArchiveMatter(string id)
        {
            var matter = await _context.Matters.FirstOrDefaultAsync(m => m.Id == id);
            if (matter == null) return NotFound();

            matter.Status = "Archived";
            await _context.SaveChangesAsync();

            await _auditLogger.LogAsync(HttpContext, "matter.archive", "Matter", id, "Archived matter");
            await TryTriggerOutcomeFeePlannerAsync(id, "matter_status_archive");

            return Ok(matter);
        }

        // POST: api/Matters/5/restore
        [HttpPost("{id}/restore")]
        public async Task<IActionResult> RestoreMatter(string id)
        {
            var matter = await _context.Matters.FirstOrDefaultAsync(m => m.Id == id);
            if (matter == null) return NotFound();

            matter.Status = "Open"; // Restore to Open
            await _context.SaveChangesAsync();

            await _auditLogger.LogAsync(HttpContext, "matter.restore", "Matter", id, "Restored matter");
            await TryTriggerOutcomeFeePlannerAsync(id, "matter_status_restore");

            return Ok(matter);
        }

        private async Task<string?> ResolveValidClientIdAsync(string? clientId)
        {
            var normalizedClientId = clientId?.Trim();
            if (string.IsNullOrWhiteSpace(normalizedClientId))
            {
                return null;
            }

            var clientExists = await _context.Clients
                .AsNoTracking()
                .AnyAsync(c => c.Id == normalizedClientId);

            return clientExists ? normalizedClientId : null;
        }

        private async Task TryTriggerOutcomeFeePlannerAsync(string matterId, string triggerType)
        {
            if (string.IsNullOrWhiteSpace(matterId)) return;
            try
            {
                await _outcomeFeePlanner.TryProcessTriggerAsync(new OutcomeFeePlanTriggerRequest
                {
                    MatterId = matterId,
                    TriggerType = triggerType,
                    TriggerEntityType = nameof(Matter),
                    TriggerEntityId = matterId
                }, GetUserId() ?? "system", HttpContext.RequestAborted);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Outcome-to-Fee planner trigger failed for matter {MatterId}", matterId);
            }

            try
            {
                await _clientTransparencyService.TryProcessTriggerAsync(new ClientTransparencyTriggerRequest
                {
                    MatterId = matterId,
                    TriggerType = triggerType,
                    TriggerEntityType = nameof(Matter),
                    TriggerEntityId = matterId
                }, GetUserId() ?? "system", HttpContext.RequestAborted);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Client transparency trigger failed for matter {MatterId}", matterId);
            }
        }

        private string? GetUserId()
        {
            return User.FindFirst("sub")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        }
    }
}
