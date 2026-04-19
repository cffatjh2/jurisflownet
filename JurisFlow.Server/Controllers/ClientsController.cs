using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using JurisFlow.Server.Contracts;
using JurisFlow.Server.Services;

namespace JurisFlow.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Policy = "StaffOnly")]
    public class ClientsController : ControllerBase
    {
        private readonly ClientApplicationService _service;

        public ClientsController(ClientApplicationService service)
        {
            _service = service;
        }

        [HttpGet]
        public async Task<IActionResult> GetClients()
        {
            return Ok(await _service.GetClientsAsync());
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetClient(string id)
        {
            var client = await _service.GetClientAsync(id);
            return client == null ? NotFound() : Ok(client);
        }

        [HttpPost]
        public async Task<IActionResult> PostClient([FromBody] ClientCreateRequest request)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            var result = await _service.CreateClientAsync(request);
            if (!result.Succeeded)
            {
                return ToProblem(result);
            }

            return CreatedAtAction(nameof(GetClient), new { id = result.Value!.Id }, result.Value);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> PutClient(string id, [FromBody] ClientReplaceRequest request)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            var result = await _service.ReplaceClientAsync(id, request);
            return result.Succeeded ? Ok(result.Value) : ToProblem(result);
        }

        [HttpPatch("{id}")]
        public async Task<IActionResult> PatchClient(string id, [FromBody] ClientPatchRequest request)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            var result = await _service.PatchClientAsync(id, request);
            return result.Succeeded ? Ok(result.Value) : ToProblem(result);
        }

        [HttpGet("{id}/status-history")]
        public async Task<IActionResult> GetStatusHistory(string id)
        {
            var history = await _service.GetStatusHistoryAsync(id);
            return history == null ? NotFound() : Ok(history);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteClient(string id)
        {
            var result = await _service.ArchiveClientAsync(id);
            return result.Succeeded ? Ok(result.Value) : ToProblem(result);
        }

        [HttpPost("{id}/set-password")]
        public async Task<IActionResult> SetClientPassword(string id, [FromBody] ClientSetPortalPasswordRequest request)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            var result = await _service.SetPortalPasswordAsync(id, request);
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
