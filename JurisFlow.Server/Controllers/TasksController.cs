using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using JurisFlow.Server.Contracts;
using JurisFlow.Server.Services;

namespace JurisFlow.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Policy = "StaffOnly")]
    public class TasksController : ControllerBase
    {
        private readonly TaskApplicationService _service;

        public TasksController(TaskApplicationService service)
        {
            _service = service;
        }

        [HttpGet]
        [EnableRateLimiting("TaskRead")]
        public async Task<ActionResult<TaskReadModelCollectionResponse>> GetTasks(
            [FromQuery] string? cursor = null,
            [FromQuery] int limit = 50,
            [FromQuery] string? status = null,
            [FromQuery] string? matterId = null,
            [FromQuery] string? assignedEmployeeId = null,
            [FromQuery] DateTime? dueFrom = null,
            [FromQuery] DateTime? dueTo = null,
            [FromQuery] bool includeArchived = false,
            CancellationToken cancellationToken = default)
        {
            return Ok(await _service.GetTasksAsync(
                cursor,
                limit,
                status,
                matterId,
                assignedEmployeeId,
                dueFrom,
                dueTo,
                includeArchived,
                cancellationToken));
        }

        [HttpGet("{id}")]
        [EnableRateLimiting("TaskRead")]
        public async Task<ActionResult<TaskResponse>> GetTask(string id, CancellationToken cancellationToken = default)
        {
            var task = await _service.GetTaskAsync(id, cancellationToken);
            if (task == null)
            {
                return NotFound();
            }

            SetTaskEtag(task);
            return Ok(task);
        }

        [HttpPost]
        [EnableRateLimiting("TaskMutation")]
        public async Task<IActionResult> PostTask([FromBody] TaskCreateRequest request, CancellationToken cancellationToken = default)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            var result = await _service.CreateTaskAsync(request, cancellationToken);
            if (!result.Succeeded)
            {
                return ToProblem(result);
            }

            SetTaskEtag(result.Value);
            return CreatedAtAction(nameof(GetTask), new { id = result.Value!.Id }, result.Value);
        }

        [HttpPut("{id}")]
        [EnableRateLimiting("TaskMutation")]
        public async Task<IActionResult> PutTask(string id, [FromBody] TaskUpdateRequest request, CancellationToken cancellationToken = default)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            var result = await _service.UpdateTaskAsync(id, request, ReadIfMatchHeader(), cancellationToken);
            if (!result.Succeeded)
            {
                return ToProblem(result);
            }

            SetTaskEtag(result.Value);
            return Ok(result.Value);
        }

        [HttpPatch("{id}")]
        [EnableRateLimiting("TaskMutation")]
        public async Task<IActionResult> PatchTask(string id, [FromBody] TaskUpdateRequest request, CancellationToken cancellationToken = default)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            var result = await _service.UpdateTaskAsync(id, request, ReadIfMatchHeader(), cancellationToken);
            if (!result.Succeeded)
            {
                return ToProblem(result);
            }

            SetTaskEtag(result.Value);
            return result.Succeeded ? Ok(result.Value) : ToProblem(result);
        }

        [HttpDelete("{id}")]
        [EnableRateLimiting("TaskMutation")]
        public async Task<IActionResult> DeleteTask(string id, CancellationToken cancellationToken = default)
        {
            var result = await _service.DeleteTaskAsync(id, cancellationToken);
            return result.Succeeded ? NoContent() : ToProblem(result);
        }

        [HttpPut("{id}/status")]
        [EnableRateLimiting("TaskMutation")]
        public async Task<IActionResult> UpdateTaskStatus(string id, [FromBody] TaskStatusUpdateRequest request, CancellationToken cancellationToken = default)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            var result = await _service.UpdateTaskStatusAsync(id, request, ReadIfMatchHeader(), cancellationToken);
            if (!result.Succeeded)
            {
                return ToProblem(result);
            }

            SetTaskEtag(result.Value);
            return Ok(result.Value);
        }

        [HttpPost("from-template")]
        [EnableRateLimiting("TaskMutation")]
        public async Task<IActionResult> CreateTasksFromTemplate([FromBody] CreateTasksFromTemplateRequest request, CancellationToken cancellationToken = default)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            var result = await _service.CreateTasksFromTemplateAsync(request, cancellationToken);
            return result.Succeeded ? Ok(result.Value) : ToProblem(result);
        }

        private ObjectResult ToProblem<T>(ApplicationServiceResult<T> result)
        {
            return Problem(
                title: result.Title,
                detail: result.Detail,
                statusCode: result.StatusCode);
        }

        private string? ReadIfMatchHeader()
        {
            var raw = Request.Headers.IfMatch.ToString();
            return string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
        }

        private void SetTaskEtag(TaskResponse? task)
        {
            if (task == null || string.IsNullOrWhiteSpace(task.RowVersion))
            {
                return;
            }

            Response.Headers.ETag = $"\"{task.RowVersion}\"";
        }
    }
}
