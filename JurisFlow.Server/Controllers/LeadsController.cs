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
    public class LeadsController : ControllerBase
    {
        private readonly JurisFlowDbContext _context;

        public LeadsController(JurisFlowDbContext context)
        {
            _context = context;
        }

        // GET: api/leads
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Lead>>> GetLeads()
        {
            return await _context.Leads.OrderByDescending(l => l.CreatedAt).ToListAsync();
        }

        // GET: api/leads/{id}
        [HttpGet("{id}")]
        public async Task<ActionResult<Lead>> GetLead(string id)
        {
            var lead = await _context.Leads.FindAsync(id);
            if (lead == null) return NotFound();
            return lead;
        }

        // POST: api/leads
        [HttpPost]
        public async Task<ActionResult<Lead>> CreateLead([FromBody] LeadDto dto)
        {
            var lead = new Lead
            {
                Id = string.IsNullOrEmpty(dto.Id) ? Guid.NewGuid().ToString() : dto.Id,
                Name = dto.Name ?? "New Lead",
                Email = dto.Email,
                Phone = dto.Phone,
                Source = dto.Source ?? "Referral",
                EstimatedValue = dto.EstimatedValue,
                Status = dto.Status ?? "New",
                PracticeArea = dto.PracticeArea,
                Notes = dto.Notes,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.Leads.Add(lead);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetLead), new { id = lead.Id }, lead);
        }

        // PUT: api/leads/{id}
        [HttpPut("{id}")]
        public async Task<ActionResult<Lead>> UpdateLead(string id, [FromBody] LeadUpdateDto dto)
        {
            var lead = await _context.Leads.FindAsync(id);
            if (lead == null) return NotFound();

            if (dto.Name != null) lead.Name = dto.Name;
            if (dto.Email != null) lead.Email = dto.Email;
            if (dto.Phone != null) lead.Phone = dto.Phone;
            if (dto.Source != null) lead.Source = dto.Source;
            if (dto.EstimatedValue.HasValue) lead.EstimatedValue = dto.EstimatedValue.Value;
            if (dto.Status != null) lead.Status = dto.Status;
            if (dto.PracticeArea != null) lead.PracticeArea = dto.PracticeArea;
            if (dto.Notes != null) lead.Notes = dto.Notes;

            lead.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return lead;
        }

        // DELETE: api/leads/{id}
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteLead(string id)
        {
            var lead = await _context.Leads.FindAsync(id);
            if (lead != null)
            {
                _context.Leads.Remove(lead);
                await _context.SaveChangesAsync();
            }
            // Return 204 even if not found (idempotent)
            return NoContent();
        }
    }

    public class LeadDto
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Source { get; set; }
        public decimal EstimatedValue { get; set; }
        public string? Status { get; set; }
        public string? PracticeArea { get; set; }
        public string? Notes { get; set; }
    }

    public class LeadUpdateDto
    {
        public string? Name { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Source { get; set; }
        public decimal? EstimatedValue { get; set; }
        public string? Status { get; set; }
        public string? PracticeArea { get; set; }
        public string? Notes { get; set; }
    }
}
