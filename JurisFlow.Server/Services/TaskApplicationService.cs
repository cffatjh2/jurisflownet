using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using JurisFlow.Server.Contracts;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using TaskModel = JurisFlow.Server.Models.Task;
using ThreadingTask = System.Threading.Tasks.Task;

namespace JurisFlow.Server.Services
{
    public sealed class TaskApplicationService
    {
        private readonly JurisFlowDbContext _context;
        private readonly AuditLogger _auditLogger;
        private readonly MatterWorkflowTriggerDispatcher _workflowTriggerDispatcher;
        private readonly ILogger<TaskApplicationService> _logger;
        private readonly TaskRequestValidator _validator;
        private readonly LegacyTaskAssignmentAdapter _assignmentAdapter;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public TaskApplicationService(
            JurisFlowDbContext context,
            AuditLogger auditLogger,
            MatterWorkflowTriggerDispatcher workflowTriggerDispatcher,
            ILogger<TaskApplicationService> logger,
            TaskRequestValidator validator,
            LegacyTaskAssignmentAdapter assignmentAdapter,
            IHttpContextAccessor httpContextAccessor)
        {
            _context = context;
            _auditLogger = auditLogger;
            _workflowTriggerDispatcher = workflowTriggerDispatcher;
            _logger = logger;
            _validator = validator;
            _assignmentAdapter = assignmentAdapter;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task<IReadOnlyList<TaskResponse>> GetTasksAsync()
        {
            var tasks = await _context.Tasks
                .AsNoTracking()
                .Include(t => t.Matter)
                .Include(t => t.AssignedEmployee)
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync();

            return tasks.Select(t => TaskResponse.FromModel(
                t,
                t.Matter?.Name,
                t.AssignedEmployee != null ? $"{t.AssignedEmployee.FirstName} {t.AssignedEmployee.LastName}".Trim() : null)).ToList();
        }

        public async Task<TaskResponse?> GetTaskAsync(string id)
        {
            var task = await _context.Tasks
                .AsNoTracking()
                .Include(t => t.Matter)
                .Include(t => t.AssignedEmployee)
                .FirstOrDefaultAsync(t => t.Id == id);

            return task == null
                ? null
                : TaskResponse.FromModel(
                    task,
                    task.Matter?.Name,
                    task.AssignedEmployee != null ? $"{task.AssignedEmployee.FirstName} {task.AssignedEmployee.LastName}".Trim() : null);
        }

        public async Task<ApplicationServiceResult<TaskResponse>> CreateTaskAsync(TaskCreateRequest request)
        {
            var validation = _validator.ValidateForCreate(request);
            if (!validation.Succeeded || validation.Value == null)
            {
                return ApplicationServiceResult<TaskResponse>.Failure(validation.StatusCode, validation.Title!, validation.Detail!);
            }

            var matterResult = await ResolveMatterNameAsync(validation.Value.MatterId);
            if (!matterResult.Succeeded)
            {
                return ApplicationServiceResult<TaskResponse>.Failure(matterResult.StatusCode, matterResult.Title!, matterResult.Detail!);
            }

            var assignmentResult = await _assignmentAdapter.ResolveAssignedEmployeeIdAsync(validation.Value.AssignedEmployeeId, validation.Value.AssignedTo);
            if (!assignmentResult.Succeeded)
            {
                return ApplicationServiceResult<TaskResponse>.Failure(assignmentResult.StatusCode, assignmentResult.Title!, assignmentResult.Detail!);
            }

            var task = new TaskModel
            {
                Id = Guid.NewGuid().ToString(),
                Title = validation.Value.Title,
                Description = validation.Value.Description,
                DueDate = validation.Value.DueDate,
                ReminderAt = validation.Value.ReminderAt,
                Priority = validation.Value.Priority,
                Status = validation.Value.Status,
                Outcome = validation.Value.Outcome,
                MatterId = validation.Value.MatterId,
                AssignedEmployeeId = assignmentResult.Value,
                ReminderSent = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.Tasks.Add(task);
            await _context.SaveChangesAsync();
            await LogAuditAsync("task.create", "Task", task.Id, $"Title={task.Title}, MatterId={task.MatterId}");
            await TryTriggerClientTransparencyAsync(task, "task_create");

            var assignedTo = await ResolveAssignedEmployeeNameAsync(task.AssignedEmployeeId);
            return ApplicationServiceResult<TaskResponse>.Success(TaskResponse.FromModel(task, matterResult.Value, assignedTo));
        }

        public async Task<ApplicationServiceResult<TaskResponse>> UpdateTaskAsync(string id, TaskUpdateRequest request)
        {
            var task = await _context.Tasks.FirstOrDefaultAsync(t => t.Id == id);
            if (task == null)
            {
                return ApplicationServiceResult<TaskResponse>.Failure(StatusCodes.Status404NotFound, "Task not found", "Task was not found.");
            }

            var validation = _validator.ValidateForUpdate(request);
            if (!validation.Succeeded || validation.Value == null)
            {
                return ApplicationServiceResult<TaskResponse>.Failure(validation.StatusCode, validation.Title!, validation.Detail!);
            }

            string? matterName = null;
            if (validation.Value.MatterId != null)
            {
                var matterResult = await ResolveMatterNameAsync(validation.Value.MatterId);
                if (!matterResult.Succeeded)
                {
                    return ApplicationServiceResult<TaskResponse>.Failure(matterResult.StatusCode, matterResult.Title!, matterResult.Detail!);
                }

                matterName = matterResult.Value;
                task.MatterId = validation.Value.MatterId;
            }

            string? assignedTo = null;
            if (validation.Value.AssignedEmployeeId != null || validation.Value.AssignedTo != null)
            {
                var assignmentResult = await _assignmentAdapter.ResolveAssignedEmployeeIdAsync(validation.Value.AssignedEmployeeId, validation.Value.AssignedTo);
                if (!assignmentResult.Succeeded)
                {
                    return ApplicationServiceResult<TaskResponse>.Failure(assignmentResult.StatusCode, assignmentResult.Title!, assignmentResult.Detail!);
                }

                task.AssignedEmployeeId = assignmentResult.Value;
                assignedTo = await ResolveAssignedEmployeeNameAsync(task.AssignedEmployeeId);
            }
            else
            {
                assignedTo = await ResolveAssignedEmployeeNameAsync(task.AssignedEmployeeId);
                matterName ??= await ResolveMatterNameUnsafeAsync(task.MatterId);
            }

            if (validation.Value.Title != null) task.Title = validation.Value.Title;
            if (validation.Value.Description != null) task.Description = validation.Value.Description;
            if (validation.Value.DueDate.HasValue) task.DueDate = validation.Value.DueDate;
            if (validation.Value.ReminderAt.HasValue) task.ReminderAt = validation.Value.ReminderAt;
            if (validation.Value.Priority != null) task.Priority = validation.Value.Priority;
            if (validation.Value.Status != null) task.Status = validation.Value.Status;
            if (validation.Value.Outcome != null) task.Outcome = validation.Value.Outcome;
            task.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await LogAuditAsync("task.update", "Task", task.Id, $"Status={task.Status}");
            await TryTriggerClientTransparencyAsync(task, MapTaskTransparencyTriggerType(task.Status, "task_update"));

            return ApplicationServiceResult<TaskResponse>.Success(TaskResponse.FromModel(task, matterName, assignedTo));
        }

        public async Task<ApplicationServiceResult<object>> DeleteTaskAsync(string id)
        {
            var task = await _context.Tasks.FirstOrDefaultAsync(t => t.Id == id);
            if (task == null)
            {
                return ApplicationServiceResult<object>.Success(new object());
            }

            _context.Tasks.Remove(task);
            await _context.SaveChangesAsync();
            await LogAuditAsync("task.delete", "Task", id, $"Deleted task {task.Title}");

            return ApplicationServiceResult<object>.Success(new object());
        }

        public async Task<ApplicationServiceResult<TaskResponse>> UpdateTaskStatusAsync(string id, TaskStatusUpdateRequest request)
        {
            var task = await _context.Tasks.FirstOrDefaultAsync(t => t.Id == id);
            if (task == null)
            {
                return ApplicationServiceResult<TaskResponse>.Failure(StatusCodes.Status404NotFound, "Task not found", "Task was not found.");
            }

            var validation = _validator.ValidateStatusUpdate(request);
            if (!validation.Succeeded || validation.Value == null)
            {
                return ApplicationServiceResult<TaskResponse>.Failure(validation.StatusCode, validation.Title!, validation.Detail!);
            }

            task.Status = validation.Value;
            task.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await LogAuditAsync("task.status", "Task", task.Id, $"Status={task.Status}");
            await TryTriggerClientTransparencyAsync(task, MapTaskTransparencyTriggerType(task.Status, "task_status_update"));

            return ApplicationServiceResult<TaskResponse>.Success(TaskResponse.FromModel(
                task,
                await ResolveMatterNameUnsafeAsync(task.MatterId),
                await ResolveAssignedEmployeeNameAsync(task.AssignedEmployeeId)));
        }

        private async Task<ApplicationServiceResult<string?>> ResolveMatterNameAsync(string? matterId)
        {
            if (string.IsNullOrWhiteSpace(matterId))
            {
                return ApplicationServiceResult<string?>.Success(null);
            }

            var matterName = await _context.Matters
                .AsNoTracking()
                .Where(m => m.Id == matterId)
                .Select(m => m.Name)
                .FirstOrDefaultAsync();

            return string.IsNullOrWhiteSpace(matterName)
                ? ApplicationServiceResult<string?>.Failure(StatusCodes.Status400BadRequest, "Invalid task", "Matter was not found.")
                : ApplicationServiceResult<string?>.Success(matterName);
        }

        private async Task<string?> ResolveMatterNameUnsafeAsync(string? matterId)
        {
            if (string.IsNullOrWhiteSpace(matterId))
            {
                return null;
            }

            return await _context.Matters
                .AsNoTracking()
                .Where(m => m.Id == matterId)
                .Select(m => m.Name)
                .FirstOrDefaultAsync();
        }

        private async Task<string?> ResolveAssignedEmployeeNameAsync(string? assignedEmployeeId)
        {
            if (string.IsNullOrWhiteSpace(assignedEmployeeId))
            {
                return null;
            }

            var employee = await _context.Employees
                .AsNoTracking()
                .Where(e => e.Id == assignedEmployeeId)
                .Select(e => new { e.FirstName, e.LastName })
                .FirstOrDefaultAsync();

            return employee == null ? null : $"{employee.FirstName} {employee.LastName}".Trim();
        }

        private ThreadingTask TryTriggerClientTransparencyAsync(TaskModel task, string triggerType)
        {
            if (task == null || string.IsNullOrWhiteSpace(task.MatterId))
            {
                return ThreadingTask.CompletedTask;
            }

            try
            {
                _workflowTriggerDispatcher.TryEnqueue(
                    GetUserId(),
                    transparencyRequest: new ClientTransparencyTriggerRequest
                    {
                        MatterId = task.MatterId,
                        TriggerType = triggerType,
                        TriggerEntityType = nameof(TaskModel),
                        TriggerEntityId = task.Id
                    });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Workflow trigger enqueue failed for task {TaskId}", task.Id);
            }

            return ThreadingTask.CompletedTask;
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
            var user = _httpContextAccessor.HttpContext?.User;
            return user?.FindFirst("sub")?.Value
                ?? user?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? "system";
        }

        private ThreadingTask LogAuditAsync(string action, string entityType, string entityId, string details)
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext == null)
            {
                return ThreadingTask.CompletedTask;
            }

            return _auditLogger.LogAsync(httpContext, action, entityType, entityId, details);
        }
    }
}
