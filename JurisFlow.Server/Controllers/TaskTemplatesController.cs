using JurisFlow.Server.Contracts;
using JurisFlow.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace JurisFlow.Server.Controllers
{
    [Route("api/task-templates")]
    [ApiController]
    [Authorize(Policy = "StaffOnly")]
    public class TaskTemplatesController : ControllerBase
    {
        private readonly TaskApplicationService _service;

        public TaskTemplatesController(TaskApplicationService service)
        {
            _service = service;
        }

        [HttpGet]
        [EnableRateLimiting("TaskRead")]
        public async Task<ActionResult<IReadOnlyList<TaskTemplateResponse>>> GetTaskTemplates(CancellationToken cancellationToken = default)
        {
            return Ok(await _service.GetTaskTemplatesAsync(cancellationToken));
        }
    }
}
