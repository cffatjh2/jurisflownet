using System.ComponentModel.DataAnnotations;
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
        public string? AssignedTo { get; set; }
    }

    public sealed class TaskUpdateRequest : RejectUnknownFieldsRequestBase
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
        public string? AssignedTo { get; set; }
    }

    public sealed class TaskStatusUpdateRequest : RejectUnknownFieldsRequestBase
    {
        [Required]
        public string Status { get; set; } = string.Empty;
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
                ReminderSent = task.ReminderSent,
                CreatedAt = task.CreatedAt,
                UpdatedAt = task.UpdatedAt
            };
        }
    }
}
