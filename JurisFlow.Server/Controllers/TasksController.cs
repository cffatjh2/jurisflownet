using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using JurisFlow.Server.Services;
using TaskModel = JurisFlow.Server.Models.Task;
using Task = System.Threading.Tasks.Task;

namespace JurisFlow.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Policy = "StaffOnly")]
    public class TasksController : ControllerBase
    {
        private readonly JurisFlowDbContext _context;
        private readonly AuditLogger _auditLogger;
        private readonly ClientTransparencyService _clientTransparencyService;
        private readonly ILogger<TasksController> _logger;

        public TasksController(
            JurisFlowDbContext context,
            AuditLogger auditLogger,
            ClientTransparencyService clientTransparencyService,
            ILogger<TasksController> logger)
        {
            _context = context;
            _auditLogger = auditLogger;
            _clientTransparencyService = clientTransparencyService;
            _logger = logger;
        }

        // GET: api/Tasks
        [HttpGet]
        public async Task<ActionResult<IEnumerable<object>>> GetTasks()
        {
            var tasks = await _context.Tasks
                .AsNoTracking()
                .Include(t => t.Matter)
                .Include(t => t.AssignedEmployee)
                .OrderByDescending(t => t.CreatedAt)
                .Select(t => new {
                    id = t.Id,
                    title = t.Title,
                    description = t.Description,
                    dueDate = t.DueDate,
                    reminderAt = t.ReminderAt,
                    priority = t.Priority,
                    status = t.Status,
                    outcome = t.Outcome,
                    matterId = t.MatterId,
                    assignedTo = t.AssignedEmployee != null ? t.AssignedEmployee.FirstName : null,
                    reminderSent = t.ReminderSent,
                    createdAt = t.CreatedAt,
                    updatedAt = t.UpdatedAt
                })
                .ToListAsync();
            
            Console.WriteLine($"[TasksController] GET /tasks - Returning {tasks.Count} tasks");
            return Ok(tasks);
        }

        // GET: api/Tasks/5
        [HttpGet("{id}")]
        public async Task<ActionResult<TaskModel>> GetTask(string id)
        {
            var task = await _context.Tasks.FindAsync(id);
            if (task == null)
            {
                return NotFound();
            }
            return task;
        }

        // POST: api/Tasks
        [HttpPost]
        public async Task<ActionResult<object>> PostTask([FromBody] TaskCreateDto dto)
        {
            Console.WriteLine($"[TasksController] POST /tasks - Creating task: {dto.Title}");
            
            // Convert empty strings to null for optional foreign key fields
            var matterId = string.IsNullOrWhiteSpace(dto.MatterId) ? null : dto.MatterId;
            var assignedEmployeeId = string.IsNullOrWhiteSpace(dto.AssignedEmployeeId) ? null : dto.AssignedEmployeeId;
            
            var task = new TaskModel
            {
                Id = Guid.NewGuid().ToString(),
                Title = dto.Title ?? "Untitled Task",
                Description = dto.Description,
                DueDate = dto.DueDate,
                ReminderAt = dto.ReminderAt,
                Priority = dto.Priority ?? "Medium",
                Status = dto.Status ?? "To Do",
                Outcome = dto.Outcome,
                MatterId = matterId,
                AssignedEmployeeId = assignedEmployeeId,
                ReminderSent = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.Tasks.Add(task);
            await _context.SaveChangesAsync();

            Console.WriteLine($"[TasksController] Task created successfully with ID: {task.Id}");

            await _auditLogger.LogAsync(HttpContext, "task.create", "Task", task.Id, $"Title={task.Title}, MatterId={task.MatterId}");
            await TryTriggerClientTransparencyAsync(task, "task_create");

            return CreatedAtAction("GetTask", new { id = task.Id }, new {
                id = task.Id,
                title = task.Title,
                description = task.Description,
                dueDate = task.DueDate,
                priority = task.Priority,
                status = task.Status,
                matterId = task.MatterId,
                createdAt = task.CreatedAt
            });
        }

        // PUT: api/Tasks/5
        [HttpPut("{id}")]
        public async Task<IActionResult> PutTask(string id, [FromBody] TaskUpdateDto dto)
        {
            var task = await _context.Tasks.FindAsync(id);
            if (task == null)
            {
                return NotFound();
            }

            if (dto.Title != null) task.Title = dto.Title;
            if (dto.Description != null) task.Description = dto.Description;
            if (dto.DueDate.HasValue) task.DueDate = dto.DueDate;
            if (dto.Priority != null) task.Priority = dto.Priority;
            if (dto.Status != null) task.Status = dto.Status;
            if (dto.Outcome != null) task.Outcome = dto.Outcome;
            if (dto.MatterId != null) task.MatterId = dto.MatterId;
            task.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await _auditLogger.LogAsync(HttpContext, "task.update", "Task", task.Id, $"Status={task.Status}");
            await TryTriggerClientTransparencyAsync(task, MapTaskTransparencyTriggerType(task.Status, "task_update"));
            return Ok(task);
        }

        // DELETE: api/Tasks/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteTask(string id)
        {
            Console.WriteLine($"[TasksController] DELETE /tasks/{id} - Attempting to delete");
            
            var task = await _context.Tasks.FindAsync(id);
            if (task == null)
            {
                Console.WriteLine($"[TasksController] Task {id} not found in database");
                // Return 204 (success) even if not found - idempotent delete
                // This prevents frontend from reverting when deleting already-deleted or mock tasks
                return NoContent();
            }

            _context.Tasks.Remove(task);
            await _context.SaveChangesAsync();
            Console.WriteLine($"[TasksController] Task {id} deleted successfully");

            await _auditLogger.LogAsync(HttpContext, "task.delete", "Task", id, $"Deleted task {task.Title}");

            return NoContent();
        }
        
        // PUT: api/Tasks/5/status
        [HttpPut("{id}/status")]
        public async Task<IActionResult> UpdateTaskStatus(string id, [FromBody] TaskStatusUpdateDto dto)
        {
            var task = await _context.Tasks.FindAsync(id);
            if (task == null) return NotFound();

            task.Status = dto.Status ?? task.Status;
            task.UpdatedAt = DateTime.UtcNow;
            
            await _context.SaveChangesAsync();
            await _auditLogger.LogAsync(HttpContext, "task.status", "Task", task.Id, $"Status={task.Status}");
            await TryTriggerClientTransparencyAsync(task, MapTaskTransparencyTriggerType(task.Status, "task_status_update"));
            return Ok(task);
        }

        private bool TaskExists(string id)
        {
            return _context.Tasks.Any(e => e.Id == id);
        }

        private async Task TryTriggerClientTransparencyAsync(TaskModel task, string triggerType)
        {
            if (task == null || string.IsNullOrWhiteSpace(task.MatterId))
            {
                return;
            }

            try
            {
                await _clientTransparencyService.TryProcessTriggerAsync(new ClientTransparencyTriggerRequest
                {
                    MatterId = task.MatterId,
                    TriggerType = triggerType,
                    TriggerEntityType = nameof(TaskModel),
                    TriggerEntityId = task.Id
                }, GetUserId(), HttpContext.RequestAborted);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Client transparency trigger failed for task {TaskId}", task.Id);
            }
        }

        private static string MapTaskTransparencyTriggerType(string? status, string fallback)
        {
            var normalized = (status ?? string.Empty).Trim().ToLowerInvariant();
            return normalized switch
            {
                "completed" or "done" => "task_completed",
                "blocked" => "task_blocked",
                _ => fallback
            };
        }

        private string GetUserId()
        {
            return User.FindFirst("sub")?.Value
                ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? "system";
        }
    }

    // DTOs
    public class TaskCreateDto 
    { 
        public string? Title { get; set; }
        public string? Description { get; set; }
        public DateTime? DueDate { get; set; }
        public DateTime? ReminderAt { get; set; }
        public string? Priority { get; set; }
        public string? Status { get; set; }
        public string? Outcome { get; set; }
        public string? MatterId { get; set; }
        public string? AssignedEmployeeId { get; set; }
        public string? AssignedTo { get; set; } // Frontend sends this
    }
    
    public class TaskUpdateDto 
    { 
        public string? Title { get; set; }
        public string? Description { get; set; }
        public DateTime? DueDate { get; set; }
        public string? Priority { get; set; }
        public string? Status { get; set; }
        public string? Outcome { get; set; }
        public string? MatterId { get; set; }
    }
    
    public class TaskStatusUpdateDto 
    { 
        public string? Status { get; set; } 
    }
}
