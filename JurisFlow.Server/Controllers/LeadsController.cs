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
        private const int DefaultReadModelPageSize = 100;
        private const int MaxReadModelPageSize = 250;
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

        [HttpGet("read-model")]
        public async Task<ActionResult<LeadReadModelCollectionResponse>> GetLeadReadModel(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = DefaultReadModelPageSize,
            [FromQuery] string? search = null,
            [FromQuery] string? status = null)
        {
            var normalizedPage = Math.Max(1, page);
            var normalizedPageSize = NormalizeReadModelPageSize(pageSize);
            var result = await _service.GetLeadReadModelPageAsync(normalizedPage, normalizedPageSize, search, status);
            return Ok(result);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<LeadResponse>> GetLead(string id)
        {
            var lead = await _service.GetLeadAsync(id);
            return lead == null ? NotFound() : Ok(lead);
        }

        [HttpGet("{id}/status-history")]
        public async Task<ActionResult<IEnumerable<LeadStatusHistoryResponse>>> GetStatusHistory(string id)
        {
            var history = await _service.GetStatusHistoryAsync(id);
            return history == null ? NotFound() : Ok(history);
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

        private static int NormalizeReadModelPageSize(int pageSize)
        {
            if (pageSize <= 0)
            {
                return DefaultReadModelPageSize;
            }

            return Math.Clamp(pageSize, 1, MaxReadModelPageSize);
        }
    }
}
