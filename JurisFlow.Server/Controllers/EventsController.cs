using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using JurisFlow.Server.Contracts;
using JurisFlow.Server.Services;

namespace JurisFlow.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Policy = "StaffOnly")]
    public class EventsController : ControllerBase
    {
        private readonly CalendarEventApplicationService _service;

        public EventsController(CalendarEventApplicationService service)
        {
            _service = service;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<CalendarEventResponse>>> GetEvents(
            [FromQuery] string? matterId = null,
            [FromQuery] DateTime? from = null,
            [FromQuery] DateTime? to = null,
            [FromQuery] int limit = 100)
        {
            return Ok(await _service.GetEventsAsync(matterId, from, to, limit));
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<CalendarEventResponse>> GetEvent(string id)
        {
            var evt = await _service.GetEventAsync(id);
            return evt == null ? NotFound() : Ok(evt);
        }

        [HttpPost]
        public async Task<IActionResult> CreateEvent([FromBody] CalendarEventRequest request)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            var result = await _service.CreateEventAsync(request);
            if (!result.Succeeded)
            {
                return ToProblem(result);
            }

            return CreatedAtAction(nameof(GetEvent), new { id = result.Value!.Id }, result.Value);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateEvent(string id, [FromBody] CalendarEventRequest request)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            var result = await _service.UpdateEventAsync(id, request);
            return result.Succeeded ? Ok(result.Value) : ToProblem(result);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteEvent(string id)
        {
            var result = await _service.DeleteEventAsync(id);
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
