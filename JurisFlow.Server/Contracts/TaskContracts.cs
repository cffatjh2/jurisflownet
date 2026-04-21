using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using JurisFlow.Server.Models;
using TaskModel = JurisFlow.Server.Models.Task;

namespace JurisFlow.Server.Contracts
{
    public sealed class TaskCreateRequest : RejectUnknownFieldsRequestBase
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
    }

    public sealed class TaskUpdateRequest : RejectUnknownFieldsRequestBase
    {
        private string? _title;
        private string? _description;
        private DateTime? _dueDate;
        private DateTime? _reminderAt;
        private string? _priority;
        private string? _status;
        private string? _outcome;
        private string? _matterId;
        private string? _assignedEmployeeId;

        public string? Title
        {
            get => _title;
            set
            {
                _title = value;
                HasTitle = true;
            }
        }

        public string? Description
        {
            get => _description;
            set
            {
                _description = value;
                HasDescription = true;
            }
        }

        public DateTime? DueDate
        {
            get => _dueDate;
            set
            {
                _dueDate = value;
                HasDueDate = true;
            }
        }

        public DateTime? ReminderAt
        {
            get => _reminderAt;
            set
            {
                _reminderAt = value;
                HasReminderAt = true;
            }
        }

        public string? Priority
        {
            get => _priority;
            set
            {
                _priority = value;
                HasPriority = true;
            }
        }

        public string? Status
        {
            get => _status;
            set
            {
                _status = value;
                HasStatus = true;
            }
        }

        public string? Outcome
        {
            get => _outcome;
            set
            {
                _outcome = value;
                HasOutcome = true;
            }
        }

        public string? MatterId
        {
            get => _matterId;
            set
            {
                _matterId = value;
                HasMatterId = true;
            }
        }

        public string? AssignedEmployeeId
        {
            get => _assignedEmployeeId;
            set
            {
                _assignedEmployeeId = value;
                HasAssignedEmployeeId = true;
            }
        }

        [JsonIgnore]
        public bool HasTitle { get; private set; }

        [JsonIgnore]
        public bool HasDescription { get; private set; }

        [JsonIgnore]
        public bool HasDueDate { get; private set; }

        [JsonIgnore]
        public bool HasReminderAt { get; private set; }

        [JsonIgnore]
        public bool HasPriority { get; private set; }

        [JsonIgnore]
        public bool HasStatus { get; private set; }

        [JsonIgnore]
        public bool HasOutcome { get; private set; }

        [JsonIgnore]
        public bool HasMatterId { get; private set; }

        [JsonIgnore]
        public bool HasAssignedEmployeeId { get; private set; }

        [JsonIgnore]
        public bool HasAnyField =>
            HasTitle ||
            HasDescription ||
            HasDueDate ||
            HasReminderAt ||
            HasPriority ||
            HasStatus ||
            HasOutcome ||
            HasMatterId ||
            HasAssignedEmployeeId;
    }

    public sealed class TaskStatusUpdateRequest : RejectUnknownFieldsRequestBase
    {
        [Required]
        public string Status { get; set; } = string.Empty;
    }

    public sealed class CreateTasksFromTemplateRequest : RejectUnknownFieldsRequestBase
    {
        [Required]
        public string TemplateId { get; set; } = string.Empty;

        public string? MatterId { get; set; }
        public string? AssignedEmployeeId { get; set; }
        public DateTime? BaseDate { get; set; }
    }

    public sealed class CreateTasksFromTemplateResponse
    {
        public IReadOnlyList<TaskResponse> Tasks { get; set; } = Array.Empty<TaskResponse>();
    }

    public sealed class TaskReadModelCollectionResponse
    {
        public IReadOnlyList<TaskResponse> Items { get; set; } = Array.Empty<TaskResponse>();
        public int TotalCount { get; set; }
        public int Limit { get; set; }
        public bool HasMore { get; set; }
        public string? NextCursor { get; set; }
    }

    public sealed class TaskTemplateResponse
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Category { get; set; }
        public string? Description { get; set; }
        public string Definition { get; set; } = "[]";
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public static TaskTemplateResponse FromModel(TaskTemplate template)
        {
            return new TaskTemplateResponse
            {
                Id = template.Id,
                Name = template.Name,
                Category = template.Category,
                Description = template.Description,
                Definition = template.Definition,
                IsActive = template.IsActive,
                CreatedAt = template.CreatedAt,
                UpdatedAt = template.UpdatedAt
            };
        }
    }

    public sealed class TaskResponse
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public DateTime? DueDate { get; set; }
        public DateTime? ReminderAt { get; set; }
        public string Priority { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? Outcome { get; set; }
        public string? MatterId { get; set; }
        public string? MatterName { get; set; }
        public string? AssignedEmployeeId { get; set; }
        public string? AssignedTo { get; set; }
        public string RowVersion { get; set; } = string.Empty;
        public bool ReminderSent { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public static TaskResponse FromModel(TaskModel task, string? matterName = null, string? assignedTo = null)
        {
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
                MatterName = matterName,
                AssignedEmployeeId = task.AssignedEmployeeId,
                AssignedTo = assignedTo,
                RowVersion = task.RowVersion,
                ReminderSent = task.ReminderSent,
                CreatedAt = task.CreatedAt,
                UpdatedAt = task.UpdatedAt
            };
        }
    }
}
