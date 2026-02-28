using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;

namespace JurisFlow.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = "StaffOnly")]
    public class OpposingPartiesController : ControllerBase
    {
        private readonly JurisFlowDbContext _context;

        public OpposingPartiesController(JurisFlowDbContext context)
        {
            _context = context;
        }

        // GET: api/opposingparties
        [HttpGet]
        public async Task<ActionResult<IEnumerable<OpposingParty>>> GetAll()
        {
            return await _context.OpposingParties.OrderByDescending(o => o.CreatedAt).ToListAsync();
        }

        // GET: api/opposingparties/matter/{matterId}
        [HttpGet("matter/{matterId}")]
        public async Task<ActionResult<IEnumerable<OpposingParty>>> GetByMatter(string matterId)
        {
            return await _context.OpposingParties
                .Where(o => o.MatterId == matterId)
                .OrderBy(o => o.Name)
                .ToListAsync();
        }

        // GET: api/opposingparties/{id}
        [HttpGet("{id}")]
        public async Task<ActionResult<OpposingParty>> GetById(string id)
        {
            var op = await _context.OpposingParties.FindAsync(id);
            if (op == null) return NotFound();
            return op;
        }

        // POST: api/opposingparties
        [HttpPost]
        public async Task<ActionResult<OpposingParty>> Create([FromBody] OpposingPartyDto dto)
        {
            var op = new OpposingParty
            {
                Id = Guid.NewGuid().ToString(),
                MatterId = dto.MatterId,
                Name = dto.Name,
                Type = dto.Type ?? "Individual",
                Company = dto.Company,
                TaxId = dto.TaxId,
                IncorporationState = dto.IncorporationState,
                CounselName = dto.CounselName,
                CounselFirm = dto.CounselFirm,
                CounselEmail = dto.CounselEmail,
                CounselPhone = dto.CounselPhone,
                CounselAddress = dto.CounselAddress,
                Notes = dto.Notes,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.OpposingParties.Add(op);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = op.Id }, op);
        }

        // PUT: api/opposingparties/{id}
        [HttpPut("{id}")]
        public async Task<ActionResult<OpposingParty>> Update(string id, [FromBody] OpposingPartyDto dto)
        {
            var op = await _context.OpposingParties.FindAsync(id);
            if (op == null) return NotFound();

            if (dto.Name != null) op.Name = dto.Name;
            if (dto.Type != null) op.Type = dto.Type;
            if (dto.Company != null) op.Company = dto.Company;
            if (dto.TaxId != null) op.TaxId = dto.TaxId;
            if (dto.IncorporationState != null) op.IncorporationState = dto.IncorporationState;
            if (dto.CounselName != null) op.CounselName = dto.CounselName;
            if (dto.CounselFirm != null) op.CounselFirm = dto.CounselFirm;
            if (dto.CounselEmail != null) op.CounselEmail = dto.CounselEmail;
            if (dto.CounselPhone != null) op.CounselPhone = dto.CounselPhone;
            if (dto.CounselAddress != null) op.CounselAddress = dto.CounselAddress;
            if (dto.Notes != null) op.Notes = dto.Notes;
            
            op.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return op;
        }

        // DELETE: api/opposingparties/{id}
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id)
        {
            var op = await _context.OpposingParties.FindAsync(id);
            if (op != null)
            {
                _context.OpposingParties.Remove(op);
                await _context.SaveChangesAsync();
            }
            return NoContent();
        }
    }

    public class OpposingPartyDto
    {
        public string MatterId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Type { get; set; }
        public string? Company { get; set; }
        public string? TaxId { get; set; }
        public string? IncorporationState { get; set; }
        public string? CounselName { get; set; }
        public string? CounselFirm { get; set; }
        public string? CounselEmail { get; set; }
        public string? CounselPhone { get; set; }
        public string? CounselAddress { get; set; }
        public string? Notes { get; set; }
    }
}
