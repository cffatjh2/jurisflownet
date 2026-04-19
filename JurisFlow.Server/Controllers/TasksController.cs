using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
        public async Task<ActionResult<IEnumerable<TaskResponse>>> GetTasks()
        {
            return Ok(await _service.GetTasksAsync());
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<TaskResponse>> GetTask(string id)
        {
            var task = await _service.GetTaskAsync(id);
            return task == null ? NotFound() : Ok(task);
        }

        [HttpPost]
        public async Task<IActionResult> PostTask([FromBody] TaskCreateRequest request)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            var result = await _service.CreateTaskAsync(request);
            if (!result.Succeeded)
            {
                return ToProblem(result);
            }

            return CreatedAtAction(nameof(GetTask), new { id = result.Value!.Id }, result.Value);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> PutTask(string id, [FromBody] TaskUpdateRequest request)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            var result = await _service.UpdateTaskAsync(id, request);
            return result.Succeeded ? Ok(result.Value) : ToProblem(result);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteTask(string id)
        {
            var result = await _service.DeleteTaskAsync(id);
            return result.Succeeded ? NoContent() : ToProblem(result);
        }

        [HttpPut("{id}/status")]
        public async Task<IActionResult> UpdateTaskStatus(string id, [FromBody] TaskStatusUpdateRequest request)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            var result = await _service.UpdateTaskStatusAsync(id, request);
            return result.Succeeded ? Ok(result.Value) : ToProblem(result);
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
