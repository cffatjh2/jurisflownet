using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using JurisFlow.Server.Contracts;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using TaskModel = JurisFlow.Server.Models.Task;

namespace JurisFlow.Server.Services
{
    public sealed class TaskApplicationService
    {
        private const string TaskOutboxProviderKey = "task_domain";
        private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

        private readonly JurisFlowDbContext _context;
        private readonly ILogger<TaskApplicationService> _logger;
        private readonly TaskRequestValidator _validator;
        private readonly TaskAccessService _taskAccess;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly TenantContext _tenantContext;

        public TaskApplicationService(
            JurisFlowDbContext context,
            ILogger<TaskApplicationService> logger,
            TaskRequestValidator validator,
            TaskAccessService taskAccess,
            IHttpContextAccessor httpContextAccessor,
            TenantContext tenantContext)
        {
            _context = context;
            _logger = logger;
            _validator = validator;
            _taskAccess = taskAccess;
            _httpContextAccessor = httpContextAccessor;
            _tenantContext = tenantContext;
        }

        public async Task<TaskReadModelCollectionResponse> GetTasksAsync(
            string? cursor = null,
            int limit = 50,
            string? status = null,
            string? matterId = null,
            string? assignedEmployeeId = null,
            DateTime? dueFrom = null,
            DateTime? dueTo = null,
            bool includeArchived = false,
            CancellationToken cancellationToken = default)
        {
            var user = GetCurrentUser();
            var normalizedLimit = Math.Clamp(limit, 1, 200);
            var filters = ParseStatusFilter(status);

            var query = _taskAccess.ApplyReadableScope(_context.Tasks.AsNoTracking(), user);

            if (!includeArchived && !filters.Contains("Archived", StringComparer.OrdinalIgnoreCase))
            {
                query = query.Where(t => t.Status != "Archived");
            }

            if (filters.Count > 0)
            {
                query = query.Where(t => filters.Contains(t.Status));
            }

            if (!string.IsNullOrWhiteSpace(matterId))
            {
                query = query.Where(t => t.MatterId == matterId);
            }

            if (!string.IsNullOrWhiteSpace(assignedEmployeeId))
            {
                query = query.Where(t => t.AssignedEmployeeId == assignedEmployeeId);
            }

            if (dueFrom.HasValue)
            {
                var normalizedDueFrom = NormalizeUtc(dueFrom.Value);
                query = query.Where(t => t.DueDate >= normalizedDueFrom);
            }

            if (dueTo.HasValue)
            {
                var normalizedDueTo = NormalizeUtc(dueTo.Value);
                query = query.Where(t => t.DueDate <= normalizedDueTo);
            }

            var totalCount = await query.CountAsync(cancellationToken);

            if (TryParseCursor(cursor, out var cursorUpdatedAt, out var cursorId))
            {
                query = query.Where(t =>
                    t.UpdatedAt < cursorUpdatedAt ||
                    (t.UpdatedAt == cursorUpdatedAt && string.CompareOrdinal(t.Id, cursorId) < 0));
            }

            var page = await query
                .OrderByDescending(t => t.UpdatedAt)
                .ThenByDescending(t => t.Id)
                .Select(t => new TaskProjection
                {
                    Id = t.Id,
                    Title = t.Title,
                    Description = t.Description,
                    DueDate = t.DueDate,
                    ReminderAt = t.ReminderAt,
                    Priority = t.Priority,
                    Status = t.Status,
                    Outcome = t.Outcome,
                    MatterId = t.MatterId,
                    MatterName = t.Matter != null ? t.Matter.Name : null,
                    AssignedEmployeeId = t.AssignedEmployeeId,
                    AssignedEmployeeFirstName = t.AssignedEmployee != null ? t.AssignedEmployee.FirstName : null,
                    AssignedEmployeeLastName = t.AssignedEmployee != null ? t.AssignedEmployee.LastName : null,
                    RowVersion = t.RowVersion,
                    ReminderSent = t.ReminderSent,
                    CreatedAt = t.CreatedAt,
                    UpdatedAt = t.UpdatedAt
                })
                .Take(normalizedLimit + 1)
                .ToListAsync(cancellationToken);

            var hasMore = page.Count > normalizedLimit;
            if (hasMore)
            {
                page.RemoveAt(page.Count - 1);
            }

            var items = page.Select(ToResponse).ToList();
            var nextCursor = hasMore && page.Count > 0
                ? BuildCursor(page[^1].UpdatedAt, page[^1].Id)
                : null;

            return new TaskReadModelCollectionResponse
            {
                Items = items,
                TotalCount = totalCount,
                Limit = normalizedLimit,
                HasMore = hasMore,
                NextCursor = nextCursor
            };
        }

        public async Task<TaskResponse?> GetTaskAsync(string id, CancellationToken cancellationToken = default)
        {
            var user = GetCurrentUser();
            var task = await _taskAccess.ApplyReadableScope(_context.Tasks.AsNoTracking(), user)
                .Where(t => t.Id == id)
                .Select(t => new TaskProjection
                {
                    Id = t.Id,
                    Title = t.Title,
                    Description = t.Description,
                    DueDate = t.DueDate,
                    ReminderAt = t.ReminderAt,
                    Priority = t.Priority,
                    Status = t.Status,
                    Outcome = t.Outcome,
                    MatterId = t.MatterId,
                    MatterName = t.Matter != null ? t.Matter.Name : null,
                    AssignedEmployeeId = t.AssignedEmployeeId,
                    AssignedEmployeeFirstName = t.AssignedEmployee != null ? t.AssignedEmployee.FirstName : null,
                    AssignedEmployeeLastName = t.AssignedEmployee != null ? t.AssignedEmployee.LastName : null,
                    RowVersion = t.RowVersion,
                    ReminderSent = t.ReminderSent,
                    CreatedAt = t.CreatedAt,
                    UpdatedAt = t.UpdatedAt
                })
                .FirstOrDefaultAsync(cancellationToken);

            return task == null ? null : ToResponse(task);
        }

        public async Task<IReadOnlyList<TaskTemplateResponse>> GetTaskTemplatesAsync(CancellationToken cancellationToken = default)
        {
            return (await _context.Set<TaskTemplate>()
                .AsNoTracking()
                .Where(t => t.IsActive)
                .OrderBy(t => t.Category)
                .ThenBy(t => t.Name)
                .ToListAsync(cancellationToken))
                .Select(TaskTemplateResponse.FromModel)
                .ToList();
        }

        public async Task<ApplicationServiceResult<TaskResponse>> CreateTaskAsync(TaskCreateRequest request, CancellationToken cancellationToken = default)
        {
            var validation = _validator.ValidateForCreate(request);
            if (!validation.Succeeded || validation.Value == null)
            {
                return ApplicationServiceResult<TaskResponse>.Failure(validation.StatusCode, validation.Title!, validation.Detail!);
            }

            var user = GetCurrentUser();
            var actorUserId = GetUserId(user);
            if (string.IsNullOrWhiteSpace(actorUserId))
            {
                return ApplicationServiceResult<TaskResponse>.Failure(StatusCodes.Status401Unauthorized, "Unauthorized", "Authenticated user context is required.");
            }

            if (!await _taskAccess.CanCreateOrManageForMatterAsync(validation.Value.MatterId, user, cancellationToken))
            {
                return ApplicationServiceResult<TaskResponse>.Failure(StatusCodes.Status403Forbidden, "Forbidden", "You cannot create tasks for the selected matter.");
            }

            var matterNameResult = await ResolveMatterNameAsync(validation.Value.MatterId, cancellationToken);
            if (!matterNameResult.Succeeded)
            {
                return ApplicationServiceResult<TaskResponse>.Failure(matterNameResult.StatusCode, matterNameResult.Title!, matterNameResult.Detail!);
            }

            var assigneeResult = await ResolveAssignedEmployeeAsync(validation.Value.AssignedEmployeeId, cancellationToken);
            if (!assigneeResult.Succeeded)
            {
                return ApplicationServiceResult<TaskResponse>.Failure(assigneeResult.StatusCode, assigneeResult.Title!, assigneeResult.Detail!);
            }

            var now = DateTime.UtcNow;
            var task = new TaskModel
            {
                Id = Guid.NewGuid().ToString(),
                Title = validation.Value.Title,
                Description = validation.Value.Description,
                DueDate = NormalizeNullableUtc(validation.Value.DueDate),
                ReminderAt = NormalizeNullableUtc(validation.Value.ReminderAt),
                Priority = validation.Value.Priority,
                Status = validation.Value.Status,
                Outcome = validation.Value.Outcome,
                MatterId = validation.Value.MatterId,
                AssignedEmployeeId = assigneeResult.Value?.EmployeeId,
                CreatedByUserId = actorUserId,
                ReminderSent = false,
                CreatedAt = now,
                UpdatedAt = now,
                RowVersion = NewRowVersion()
            };

            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            _context.Tasks.Add(task);
            QueueTaskOutboxEvent(task, eventType: "task.created", auditAction: "task.create", auditDetails: $"Title={task.Title}, MatterId={task.MatterId}");
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return ApplicationServiceResult<TaskResponse>.Success(TaskResponse.FromModel(task, matterNameResult.Value, assigneeResult.Value?.DisplayName));
        }

        public async Task<ApplicationServiceResult<TaskResponse>> UpdateTaskAsync(string id, TaskUpdateRequest request, string? ifMatch, CancellationToken cancellationToken = default)
        {
            var user = GetCurrentUser();
            var task = await _taskAccess.FindManageableTaskAsync(id, user, cancellationToken: cancellationToken);
            if (task == null)
            {
                return ApplicationServiceResult<TaskResponse>.Failure(StatusCodes.Status404NotFound, "Task not found", "Task was not found.");
            }

            var concurrency = ValidateIfMatch(task.RowVersion, ifMatch);
            if (!concurrency.Succeeded)
            {
                return ApplicationServiceResult<TaskResponse>.Failure(concurrency.StatusCode, concurrency.Title!, concurrency.Detail!);
            }

            var validation = _validator.ValidateForUpdate(request);
            if (!validation.Succeeded || validation.Value == null)
            {
                return ApplicationServiceResult<TaskResponse>.Failure(validation.StatusCode, validation.Title!, validation.Detail!);
            }

            var effectiveDueDate = validation.Value.HasDueDate ? NormalizeNullableUtc(validation.Value.DueDate) : task.DueDate;
            var effectiveReminderAt = validation.Value.HasReminderAt ? NormalizeNullableUtc(validation.Value.ReminderAt) : task.ReminderAt;
            if (effectiveDueDate.HasValue && effectiveReminderAt.HasValue && effectiveReminderAt.Value > effectiveDueDate.Value)
            {
                return ApplicationServiceResult<TaskResponse>.Failure(StatusCodes.Status400BadRequest, "Invalid task", "ReminderAt cannot be after DueDate.");
            }

            var targetMatterId = validation.Value.HasMatterId ? validation.Value.MatterId : task.MatterId;
            if (!await _taskAccess.CanCreateOrManageForMatterAsync(targetMatterId, user, cancellationToken))
            {
                return ApplicationServiceResult<TaskResponse>.Failure(StatusCodes.Status403Forbidden, "Forbidden", "You cannot move the task to the selected matter.");
            }

            var matterNameResult = await ResolveMatterNameAsync(targetMatterId, cancellationToken);
            if (!matterNameResult.Succeeded)
            {
                return ApplicationServiceResult<TaskResponse>.Failure(matterNameResult.StatusCode, matterNameResult.Title!, matterNameResult.Detail!);
            }

            var targetAssignedEmployeeId = validation.Value.HasAssignedEmployeeId ? validation.Value.AssignedEmployeeId : task.AssignedEmployeeId;
            var assigneeResult = await ResolveAssignedEmployeeAsync(targetAssignedEmployeeId, cancellationToken);
            if (!assigneeResult.Succeeded)
            {
                return ApplicationServiceResult<TaskResponse>.Failure(assigneeResult.StatusCode, assigneeResult.Title!, assigneeResult.Detail!);
            }

            var entry = _context.Entry(task);
            var originalRowVersion = task.RowVersion;

            if (validation.Value.HasTitle)
            {
                task.Title = validation.Value.Title!;
                entry.Property(t => t.Title).IsModified = true;
            }

            if (validation.Value.HasDescription)
            {
                task.Description = validation.Value.Description;
                entry.Property(t => t.Description).IsModified = true;
            }

            if (validation.Value.HasDueDate)
            {
                task.DueDate = effectiveDueDate;
                entry.Property(t => t.DueDate).IsModified = true;
            }

            if (validation.Value.HasReminderAt)
            {
                task.ReminderAt = effectiveReminderAt;
                entry.Property(t => t.ReminderAt).IsModified = true;
            }

            if (validation.Value.HasPriority)
            {
                task.Priority = validation.Value.Priority!;
                entry.Property(t => t.Priority).IsModified = true;
            }

            if (validation.Value.HasStatus)
            {
                task.Status = validation.Value.Status!;
                entry.Property(t => t.Status).IsModified = true;
            }

            if (validation.Value.HasOutcome)
            {
                task.Outcome = validation.Value.Outcome;
                entry.Property(t => t.Outcome).IsModified = true;
            }

            if (validation.Value.HasMatterId)
            {
                task.MatterId = validation.Value.MatterId;
                entry.Property(t => t.MatterId).IsModified = true;
            }

            if (validation.Value.HasAssignedEmployeeId)
            {
                task.AssignedEmployeeId = validation.Value.AssignedEmployeeId;
                entry.Property(t => t.AssignedEmployeeId).IsModified = true;
            }

            task.UpdatedAt = DateTime.UtcNow;
            task.RowVersion = NewRowVersion();
            entry.Property(t => t.UpdatedAt).IsModified = true;
            entry.Property(t => t.RowVersion).OriginalValue = originalRowVersion;
            entry.Property(t => t.RowVersion).IsModified = true;

            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            QueueTaskOutboxEvent(
                task,
                eventType: "task.updated",
                auditAction: "task.update",
                auditDetails: $"Status={task.Status}, MatterId={task.MatterId}");
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return ApplicationServiceResult<TaskResponse>.Success(TaskResponse.FromModel(task, matterNameResult.Value, assigneeResult.Value?.DisplayName));
        }

        public async Task<ApplicationServiceResult<object>> DeleteTaskAsync(string id, CancellationToken cancellationToken = default)
        {
            var user = GetCurrentUser();
            var task = await _taskAccess.FindManageableTaskAsync(id, user, cancellationToken: cancellationToken);
            if (task == null)
            {
                return ApplicationServiceResult<object>.Success(new object());
            }

            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            QueueTaskOutboxEvent(
                task,
                eventType: "task.deleted",
                auditAction: "task.delete",
                auditDetails: $"Deleted task {task.Title}");
            _context.Tasks.Remove(task);
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return ApplicationServiceResult<object>.Success(new object());
        }

        public async Task<ApplicationServiceResult<TaskResponse>> UpdateTaskStatusAsync(string id, TaskStatusUpdateRequest request, string? ifMatch, CancellationToken cancellationToken = default)
        {
            var user = GetCurrentUser();
            var task = await _taskAccess.FindManageableTaskAsync(id, user, cancellationToken: cancellationToken);
            if (task == null)
            {
                return ApplicationServiceResult<TaskResponse>.Failure(StatusCodes.Status404NotFound, "Task not found", "Task was not found.");
            }

            var concurrency = ValidateIfMatch(task.RowVersion, ifMatch);
            if (!concurrency.Succeeded)
            {
                return ApplicationServiceResult<TaskResponse>.Failure(concurrency.StatusCode, concurrency.Title!, concurrency.Detail!);
            }

            var validation = _validator.ValidateStatusUpdate(request);
            if (!validation.Succeeded || validation.Value == null)
            {
                return ApplicationServiceResult<TaskResponse>.Failure(validation.StatusCode, validation.Title!, validation.Detail!);
            }

            var entry = _context.Entry(task);
            var originalRowVersion = task.RowVersion;
            task.Status = validation.Value;
            task.UpdatedAt = DateTime.UtcNow;
            task.RowVersion = NewRowVersion();
            entry.Property(t => t.Status).IsModified = true;
            entry.Property(t => t.UpdatedAt).IsModified = true;
            entry.Property(t => t.RowVersion).OriginalValue = originalRowVersion;
            entry.Property(t => t.RowVersion).IsModified = true;

            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            QueueTaskOutboxEvent(
                task,
                eventType: "task.status.changed",
                auditAction: "task.status",
                auditDetails: $"Status={task.Status}");
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return ApplicationServiceResult<TaskResponse>.Success(TaskResponse.FromModel(
                task,
                await ResolveMatterNameUnsafeAsync(task.MatterId, cancellationToken),
                (await ResolveAssignedEmployeeAsync(task.AssignedEmployeeId, cancellationToken)).Value?.DisplayName));
        }

        public async Task<ApplicationServiceResult<CreateTasksFromTemplateResponse>> CreateTasksFromTemplateAsync(CreateTasksFromTemplateRequest request, CancellationToken cancellationToken = default)
        {
            var user = GetCurrentUser();
            var actorUserId = GetUserId(user);
            if (string.IsNullOrWhiteSpace(actorUserId))
            {
                return ApplicationServiceResult<CreateTasksFromTemplateResponse>.Failure(StatusCodes.Status401Unauthorized, "Unauthorized", "Authenticated user context is required.");
            }

            var templateId = request.TemplateId?.Trim();
            if (string.IsNullOrWhiteSpace(templateId))
            {
                return ApplicationServiceResult<CreateTasksFromTemplateResponse>.Failure(StatusCodes.Status400BadRequest, "Invalid template", "TemplateId is required.");
            }

            if (!await _taskAccess.CanCreateOrManageForMatterAsync(request.MatterId, user, cancellationToken))
            {
                return ApplicationServiceResult<CreateTasksFromTemplateResponse>.Failure(StatusCodes.Status403Forbidden, "Forbidden", "You cannot create tasks for the selected matter.");
            }

            var template = await _context.Set<TaskTemplate>()
                .FirstOrDefaultAsync(t => t.Id == templateId && t.IsActive, cancellationToken);
            if (template == null)
            {
                return ApplicationServiceResult<CreateTasksFromTemplateResponse>.Failure(StatusCodes.Status404NotFound, "Template not found", "Task template was not found.");
            }

            var matterNameResult = await ResolveMatterNameAsync(request.MatterId, cancellationToken);
            if (!matterNameResult.Succeeded)
            {
                return ApplicationServiceResult<CreateTasksFromTemplateResponse>.Failure(matterNameResult.StatusCode, matterNameResult.Title!, matterNameResult.Detail!);
            }

            var assigneeResult = await ResolveAssignedEmployeeAsync(request.AssignedEmployeeId, cancellationToken);
            if (!assigneeResult.Succeeded)
            {
                return ApplicationServiceResult<CreateTasksFromTemplateResponse>.Failure(assigneeResult.StatusCode, assigneeResult.Title!, assigneeResult.Detail!);
            }

            var definition = ParseTemplateDefinition(template.Definition);
            if (!definition.Succeeded || definition.Value == null)
            {
                return ApplicationServiceResult<CreateTasksFromTemplateResponse>.Failure(definition.StatusCode, definition.Title!, definition.Detail!);
            }

            var baseDate = request.BaseDate.HasValue ? NormalizeUtc(request.BaseDate.Value) : DateTime.UtcNow;
            var now = DateTime.UtcNow;
            var tasks = new List<TaskModel>(definition.Value.Count);

            foreach (var item in definition.Value)
            {
                var createValidation = _validator.ValidateForCreate(new TaskCreateRequest
                {
                    Title = item.Title,
                    Description = item.Description,
                    DueDate = item.DueOffsetDays.HasValue ? baseDate.AddDays(item.DueOffsetDays.Value) : null,
                    ReminderAt = item.ReminderOffsetDays.HasValue ? baseDate.AddDays(item.ReminderOffsetDays.Value) : null,
                    Priority = item.Priority,
                    Status = item.Status,
                    Outcome = null,
                    MatterId = request.MatterId,
                    AssignedEmployeeId = request.AssignedEmployeeId ?? item.AssignedEmployeeId
                });
                if (!createValidation.Succeeded || createValidation.Value == null)
                {
                    return ApplicationServiceResult<CreateTasksFromTemplateResponse>.Failure(createValidation.StatusCode, createValidation.Title!, createValidation.Detail!);
                }

                var task = new TaskModel
                {
                    Id = Guid.NewGuid().ToString(),
                    Title = createValidation.Value.Title,
                    Description = createValidation.Value.Description,
                    DueDate = NormalizeNullableUtc(createValidation.Value.DueDate),
                    ReminderAt = NormalizeNullableUtc(createValidation.Value.ReminderAt),
                    Priority = createValidation.Value.Priority,
                    Status = createValidation.Value.Status,
                    Outcome = createValidation.Value.Outcome,
                    MatterId = request.MatterId,
                    AssignedEmployeeId = createValidation.Value.AssignedEmployeeId,
                    CreatedByUserId = actorUserId,
                    ReminderSent = false,
                    CreatedAt = now,
                    UpdatedAt = now,
                    RowVersion = NewRowVersion()
                };

                tasks.Add(task);
            }

            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            _context.Tasks.AddRange(tasks);
            foreach (var task in tasks)
            {
                QueueTaskOutboxEvent(
                    task,
                    eventType: "task.created.from_template",
                    auditAction: "task.create",
                    auditDetails: $"Title={task.Title}, MatterId={task.MatterId}, TemplateId={template.Id}");
            }

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return ApplicationServiceResult<CreateTasksFromTemplateResponse>.Success(new CreateTasksFromTemplateResponse
            {
                Tasks = tasks.Select(task => TaskResponse.FromModel(task, matterNameResult.Value, assigneeResult.Value?.DisplayName)).ToList()
            });
        }

        private ApplicationServiceResult<object> ValidateIfMatch(string currentRowVersion, string? ifMatch)
        {
            if (string.IsNullOrWhiteSpace(ifMatch))
            {
                return ApplicationServiceResult<object>.Failure(
                    StatusCodes.Status428PreconditionRequired,
                    "Missing concurrency token",
                    "If-Match header is required for task mutations.");
            }

            var normalized = ifMatch.Trim();
            if (normalized.StartsWith('"') && normalized.EndsWith('"') && normalized.Length >= 2)
            {
                normalized = normalized[1..^1];
            }

            return string.Equals(currentRowVersion, normalized, StringComparison.Ordinal)
                ? ApplicationServiceResult<object>.Success(new object())
                : ApplicationServiceResult<object>.Failure(
                    StatusCodes.Status412PreconditionFailed,
                    "Task has changed",
                    "The task was modified by another request. Refresh and retry.");
        }

        private async Task<ApplicationServiceResult<string?>> ResolveMatterNameAsync(string? matterId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(matterId))
            {
                return ApplicationServiceResult<string?>.Success(null);
            }

            var matterName = await _context.Matters
                .AsNoTracking()
                .Where(m => m.Id == matterId)
                .Select(m => m.Name)
                .FirstOrDefaultAsync(cancellationToken);

            return string.IsNullOrWhiteSpace(matterName)
                ? ApplicationServiceResult<string?>.Failure(StatusCodes.Status400BadRequest, "Invalid task", "Matter was not found.")
                : ApplicationServiceResult<string?>.Success(matterName);
        }

        private async Task<string?> ResolveMatterNameUnsafeAsync(string? matterId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(matterId))
            {
                return null;
            }

            return await _context.Matters
                .AsNoTracking()
                .Where(m => m.Id == matterId)
                .Select(m => m.Name)
                .FirstOrDefaultAsync(cancellationToken);
        }

        private async Task<ApplicationServiceResult<TaskAssigneeResolution?>> ResolveAssignedEmployeeAsync(string? assignedEmployeeId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(assignedEmployeeId))
            {
                return ApplicationServiceResult<TaskAssigneeResolution?>.Success(null);
            }

            var employee = await _context.Employees
                .AsNoTracking()
                .Where(e => e.Id == assignedEmployeeId)
                .Select(e => new TaskAssigneeResolution
                {
                    EmployeeId = e.Id,
                    DisplayName = (e.FirstName + " " + e.LastName).Trim()
                })
                .FirstOrDefaultAsync(cancellationToken);

            return employee == null
                ? ApplicationServiceResult<TaskAssigneeResolution?>.Failure(StatusCodes.Status400BadRequest, "Invalid task", "Assigned employee was not found.")
                : ApplicationServiceResult<TaskAssigneeResolution?>.Success(employee);
        }

        private void QueueTaskOutboxEvent(TaskModel task, string eventType, string auditAction, string auditDetails)
        {
            var httpContext = _httpContextAccessor.HttpContext;
            var user = httpContext?.User ?? GetCurrentUser();
            var audit = new TaskDomainAuditPayload
            {
                UserId = GetUserId(user),
                ClientId = user.FindFirst("clientId")?.Value,
                Role = user.FindFirst(ClaimTypes.Role)?.Value ?? user.FindFirst("role")?.Value,
                Action = auditAction,
                Entity = "Task",
                EntityId = task.Id,
                Details = auditDetails,
                IpAddress = httpContext?.Connection.RemoteIpAddress?.ToString(),
                UserAgent = httpContext?.Request.Headers.UserAgent.ToString(),
                CreatedAtUtc = DateTime.UtcNow
            };

            ClientTransparencyTriggerRequest? transparencyTrigger = null;
            if (!string.IsNullOrWhiteSpace(task.MatterId))
            {
                transparencyTrigger = new ClientTransparencyTriggerRequest
                {
                    MatterId = task.MatterId,
                    TriggerType = MapTaskTransparencyTriggerType(task.Status, eventType),
                    TriggerEntityType = nameof(TaskModel),
                    TriggerEntityId = task.Id,
                    CorrelationId = $"task:{task.Id}:{task.RowVersion}"
                };
            }

            var payload = new TaskDomainOutboxPayload
            {
                TenantSlug = _tenantContext.TenantSlug ?? string.Empty,
                ActorUserId = audit.UserId,
                Audit = audit,
                TransparencyTrigger = transparencyTrigger
            };

            _context.IntegrationOutboxEvents.Add(new IntegrationOutboxEvent
            {
                Id = Guid.NewGuid().ToString(),
                ProviderKey = TaskOutboxProviderKey,
                EventType = eventType,
                EntityType = nameof(TaskModel),
                EntityId = task.Id,
                IdempotencyKey = $"task:{task.Id}:{task.RowVersion}:{eventType}",
                CorrelationId = $"task:{task.Id}:{task.RowVersion}",
                Status = "pending",
                PayloadJson = JsonSerializer.Serialize(payload, SerializerOptions),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        private static string MapTaskTransparencyTriggerType(string? status, string fallback)
        {
            var normalized = (status ?? string.Empty).Trim().ToLowerInvariant();
            return normalized switch
            {
                "done" => "task_completed",
                "review" => "task_in_review",
                "archived" => "task_archived",
                _ => fallback
            };
        }

        private static HashSet<string> ParseStatusFilter(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            return raw
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        private static bool TryParseCursor(string? cursor, out DateTime updatedAt, out string id)
        {
            updatedAt = default;
            id = string.Empty;
            if (string.IsNullOrWhiteSpace(cursor))
            {
                return false;
            }

            try
            {
                var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
                var separatorIndex = decoded.IndexOf('|');
                if (separatorIndex <= 0 || separatorIndex >= decoded.Length - 1)
                {
                    return false;
                }

                var timestamp = decoded[..separatorIndex];
                id = decoded[(separatorIndex + 1)..];
                if (!DateTime.TryParse(timestamp, null, System.Globalization.DateTimeStyles.RoundtripKind, out updatedAt))
                {
                    return false;
                }

                updatedAt = NormalizeUtc(updatedAt);
                return !string.IsNullOrWhiteSpace(id);
            }
            catch
            {
                return false;
            }
        }

        private static string BuildCursor(DateTime updatedAt, string id)
        {
            var value = $"{NormalizeUtc(updatedAt):O}|{id}";
            return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(value));
        }

        private static TaskResponse ToResponse(TaskProjection task)
        {
            var assignedTo = $"{task.AssignedEmployeeFirstName} {task.AssignedEmployeeLastName}".Trim();
            return new TaskResponse
            {
                Id = task.Id,
                Title = task.Title,
                Description = task.Description,
                DueDate = task.DueDate,
                ReminderAt = task.ReminderAt,
                Priority = task.Priority,
                Status = task.Status,
                Outcome = task.Outcome,
                MatterId = task.MatterId,
                MatterName = task.MatterName,
                AssignedEmployeeId = task.AssignedEmployeeId,
                AssignedTo = string.IsNullOrWhiteSpace(assignedTo) ? null : assignedTo,
                RowVersion = task.RowVersion,
                ReminderSent = task.ReminderSent,
                CreatedAt = task.CreatedAt,
                UpdatedAt = task.UpdatedAt
            };
        }

        private static ApplicationServiceResult<List<TaskTemplateDefinitionItem>> ParseTemplateDefinition(string definition)
        {
            if (string.IsNullOrWhiteSpace(definition))
            {
                return ApplicationServiceResult<List<TaskTemplateDefinitionItem>>.Failure(
                    StatusCodes.Status400BadRequest,
                    "Invalid template",
                    "Template definition is empty.");
            }

            try
            {
                var items = JsonSerializer.Deserialize<List<TaskTemplateDefinitionItem>>(definition, SerializerOptions);
                if (items == null || items.Count == 0)
                {
                    return ApplicationServiceResult<List<TaskTemplateDefinitionItem>>.Failure(
                        StatusCodes.Status400BadRequest,
                        "Invalid template",
                        "Template definition must contain at least one task.");
                }

                if (items.Any(item => string.IsNullOrWhiteSpace(item.Title)))
                {
                    return ApplicationServiceResult<List<TaskTemplateDefinitionItem>>.Failure(
                        StatusCodes.Status400BadRequest,
                        "Invalid template",
                        "Template definition contains a task without a title.");
                }

                return ApplicationServiceResult<List<TaskTemplateDefinitionItem>>.Success(items);
            }
            catch (JsonException ex)
            {
                return ApplicationServiceResult<List<TaskTemplateDefinitionItem>>.Failure(
                    StatusCodes.Status400BadRequest,
                    "Invalid template",
                    $"Template definition is not valid JSON: {ex.Message}");
            }
        }

        private ClaimsPrincipal GetCurrentUser()
        {
            return _httpContextAccessor.HttpContext?.User ?? new ClaimsPrincipal(new ClaimsIdentity());
        }

        private static string? GetUserId(ClaimsPrincipal user)
        {
            return user.FindFirst("sub")?.Value
                ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        }

        private static DateTime NormalizeUtc(DateTime value)
        {
            return value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Local => value.ToUniversalTime(),
                _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
            };
        }

        private static DateTime? NormalizeNullableUtc(DateTime? value)
        {
            return value.HasValue ? NormalizeUtc(value.Value) : null;
        }

        private static string NewRowVersion() => Guid.NewGuid().ToString("N");

        private sealed class TaskProjection
        {
            public string Id { get; init; } = string.Empty;
            public string Title { get; init; } = string.Empty;
            public string? Description { get; init; }
            public DateTime? DueDate { get; init; }
            public DateTime? ReminderAt { get; init; }
            public string Priority { get; init; } = string.Empty;
            public string Status { get; init; } = string.Empty;
            public string? Outcome { get; init; }
            public string? MatterId { get; init; }
            public string? MatterName { get; init; }
            public string? AssignedEmployeeId { get; init; }
            public string? AssignedEmployeeFirstName { get; init; }
            public string? AssignedEmployeeLastName { get; init; }
            public string RowVersion { get; init; } = string.Empty;
            public bool ReminderSent { get; init; }
            public DateTime CreatedAt { get; init; }
            public DateTime UpdatedAt { get; init; }
        }
    }

    public sealed class TaskTemplateDefinitionItem
    {
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? Priority { get; set; }
        public string? Status { get; set; }
        public int? DueOffsetDays { get; set; }
        public int? ReminderOffsetDays { get; set; }
        public string? AssignedEmployeeId { get; set; }
    }

    public sealed class TaskDomainOutboxPayload
    {
        public string TenantSlug { get; set; } = string.Empty;
        public string? ActorUserId { get; set; }
        public TaskDomainAuditPayload? Audit { get; set; }
        public ClientTransparencyTriggerRequest? TransparencyTrigger { get; set; }
    }

    public sealed class TaskDomainAuditPayload
    {
        public string? UserId { get; set; }
        public string? ClientId { get; set; }
        public string? Role { get; set; }
        public string Action { get; set; } = string.Empty;
        public string? Entity { get; set; }
        public string? EntityId { get; set; }
        public string? Details { get; set; }
        public string? IpAddress { get; set; }
        public string? UserAgent { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }

    public sealed class TaskAssigneeResolution
    {
        public string EmployeeId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
    }
}
