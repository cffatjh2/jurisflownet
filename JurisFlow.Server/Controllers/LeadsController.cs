using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using JurisFlow.Server.Contracts;
using JurisFlow.Server.Services;

namespace JurisFlow.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = "StaffOnly")]
    public class LeadsController : ControllerBase
    {
        private readonly LeadApplicationService _service;

        public LeadsController(LeadApplicationService service)
        {
            _service = service;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<LeadResponse>>> GetLeads()
        {
            return Ok(await _service.GetLeadsAsync());
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<LeadResponse>> GetLead(string id)
        {
            var lead = await _service.GetLeadAsync(id);
            return lead == null ? NotFound() : Ok(lead);
        }

        [HttpPost]
        public async Task<IActionResult> CreateLead([FromBody] LeadCreateRequest request)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            var result = await _service.CreateLeadAsync(request);
            if (!result.Succeeded)
            {
                return ToProblem(result);
            }

            return CreatedAtAction(nameof(GetLead), new { id = result.Value!.Id }, result.Value);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateLead(string id, [FromBody] LeadUpdateRequest request)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            var result = await _service.UpdateLeadAsync(id, request);
            return result.Succeeded ? Ok(result.Value) : ToProblem(result);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteLead(string id)
        {
            var result = await _service.DeleteLeadAsync(id);
            return result.Succeeded ? NoContent() : ToProblem(result);
        }

        private ObjectResult ToProblem<T>(ApplicationServiceResult<T> result)
        {
            return Problem(
                title: result.Title,
                detail: result.Detail,
                statusCode: result.StatusCode);
        }
    }
}
