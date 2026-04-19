using JurisFlow.Server.Contracts;

namespace JurisFlow.Server.Services
{
    public sealed class TaskRequestValidator
    {
        private static readonly HashSet<string> AllowedPriorities = new(StringComparer.OrdinalIgnoreCase)
        {
            "High",
            "Medium",
            "Low"
        };

        private static readonly HashSet<string> AllowedStatuses = new(StringComparer.OrdinalIgnoreCase)
        {
            "To Do",
            "In Progress",
            "Blocked",
            "Completed",
            "Done"
        };

        public ApplicationServiceResult<TaskWriteModel> ValidateForCreate(TaskCreateRequest request)
        {
            return ValidateCore(
                request.Title,
                request.Description,
                request.DueDate,
                request.ReminderAt,
                request.Priority,
                request.Status,
                request.Outcome,
                request.MatterId,
                request.AssignedEmployeeId,
                request.AssignedTo,
                requireTitle: false);
        }

        public ApplicationServiceResult<TaskWriteModel> ValidateForUpdate(TaskUpdateRequest request)
        {
            return ValidateCore(
                request.Title,
                request.Description,
                request.DueDate,
                request.ReminderAt,
                request.Priority,
                request.Status,
                request.Outcome,
                request.MatterId,
                request.AssignedEmployeeId,
                request.AssignedTo,
                requireTitle: false);
        }

        public ApplicationServiceResult<string> ValidateStatusUpdate(TaskStatusUpdateRequest request)
        {
            var normalizedStatus = NormalizeStatus(request.Status);
            if (normalizedStatus == null)
            {
                return ApplicationServiceResult<string>.Failure(StatusCodes.Status400BadRequest, "Invalid task status", "Task status is invalid.");
            }

            return ApplicationServiceResult<string>.Success(normalizedStatus);
        }

        private static ApplicationServiceResult<TaskWriteModel> ValidateCore(
            string? title,
            string? description,
            DateTime? dueDate,
            DateTime? reminderAt,
            string? priority,
            string? status,
            string? outcome,
            string? matterId,
            string? assignedEmployeeId,
            string? assignedTo,
            bool requireTitle)
        {
            var normalizedTitle = NormalizeOptional(title, 256) ?? "Untitled Task";
            if (requireTitle && string.IsNullOrWhiteSpace(normalizedTitle))
            {
                return ApplicationServiceResult<TaskWriteModel>.Failure(StatusCodes.Status400BadRequest, "Invalid task", "Title is required.");
            }

            var normalizedPriority = NormalizePriority(priority) ?? "Medium";
            if (NormalizePriority(priority) == null && priority != null)
            {
                return ApplicationServiceResult<TaskWriteModel>.Failure(StatusCodes.Status400BadRequest, "Invalid task", "Task priority is invalid.");
            }

            var normalizedStatus = NormalizeStatus(status) ?? "To Do";
            if (NormalizeStatus(status) == null && status != null)
            {
                return ApplicationServiceResult<TaskWriteModel>.Failure(StatusCodes.Status400BadRequest, "Invalid task", "Task status is invalid.");
            }

            return ApplicationServiceResult<TaskWriteModel>.Success(new TaskWriteModel
            {
                Title = normalizedTitle,
                Description = NormalizeOptional(description, 4000),
                DueDate = dueDate,
                ReminderAt = reminderAt,
                Priority = normalizedPriority,
                Status = normalizedStatus,
                Outcome = NormalizeOptional(outcome, 128),
                MatterId = NormalizeOptional(matterId, 128),
                AssignedEmployeeId = NormalizeOptional(assignedEmployeeId, 128),
                AssignedTo = NormalizeOptional(assignedTo, 256)
            });
        }

        private static string? NormalizePriority(string? priority)
        {
            var candidate = string.IsNullOrWhiteSpace(priority) ? "Medium" : priority.Trim();
            return AllowedPriorities.FirstOrDefault(p => string.Equals(p, candidate, StringComparison.OrdinalIgnoreCase));
        }

        private static string? NormalizeStatus(string? status)
        {
            var candidate = string.IsNullOrWhiteSpace(status) ? "To Do" : status.Trim();
            return AllowedStatuses.FirstOrDefault(s => string.Equals(s, candidate, StringComparison.OrdinalIgnoreCase));
        }

        private static string? NormalizeOptional(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var trimmed = value.Trim();
            return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
        }
    }

    public sealed class TaskWriteModel
    {
        public string Title { get; init; } = string.Empty;
        public string? Description { get; init; }
        public DateTime? DueDate { get; init; }
        public DateTime? ReminderAt { get; init; }
        public string Priority { get; init; } = "Medium";
        public string Status { get; init; } = "To Do";
        public string? Outcome { get; init; }
        public string? MatterId { get; init; }
        public string? AssignedEmployeeId { get; init; }
        public string? AssignedTo { get; init; }
    }
}
