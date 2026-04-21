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
            "Review",
            "Done",
            "Archived"
        };

        private static readonly HashSet<string> AllowedOutcomes = new(StringComparer.OrdinalIgnoreCase)
        {
            "success",
            "failed",
            "cancelled"
        };

        public ApplicationServiceResult<TaskCreateWriteModel> ValidateForCreate(TaskCreateRequest request)
        {
            var titleResult = NormalizeCreateTitle(request.Title);
            if (!titleResult.Succeeded || titleResult.Value == null)
            {
                return ApplicationServiceResult<TaskCreateWriteModel>.Failure(titleResult.StatusCode, titleResult.Title!, titleResult.Detail!);
            }

            var descriptionResult = NormalizeOptionalField(request.Description, 4000, "Description");
            if (!descriptionResult.Succeeded)
            {
                return ApplicationServiceResult<TaskCreateWriteModel>.Failure(descriptionResult.StatusCode, descriptionResult.Title!, descriptionResult.Detail!);
            }

            var priorityResult = NormalizePriority(request.Priority, defaultValue: "Medium");
            if (!priorityResult.Succeeded || priorityResult.Value == null)
            {
                return ApplicationServiceResult<TaskCreateWriteModel>.Failure(priorityResult.StatusCode, priorityResult.Title!, priorityResult.Detail!);
            }

            var statusResult = NormalizeStatus(request.Status, defaultValue: "To Do");
            if (!statusResult.Succeeded || statusResult.Value == null)
            {
                return ApplicationServiceResult<TaskCreateWriteModel>.Failure(statusResult.StatusCode, statusResult.Title!, statusResult.Detail!);
            }

            var outcomeResult = NormalizeOutcome(request.Outcome, allowClear: true);
            if (!outcomeResult.Succeeded)
            {
                return ApplicationServiceResult<TaskCreateWriteModel>.Failure(outcomeResult.StatusCode, outcomeResult.Title!, outcomeResult.Detail!);
            }

            var matterIdResult = NormalizeOptionalField(request.MatterId, 128, "MatterId");
            if (!matterIdResult.Succeeded)
            {
                return ApplicationServiceResult<TaskCreateWriteModel>.Failure(matterIdResult.StatusCode, matterIdResult.Title!, matterIdResult.Detail!);
            }

            var assignedEmployeeIdResult = NormalizeOptionalField(request.AssignedEmployeeId, 128, "AssignedEmployeeId");
            if (!assignedEmployeeIdResult.Succeeded)
            {
                return ApplicationServiceResult<TaskCreateWriteModel>.Failure(assignedEmployeeIdResult.StatusCode, assignedEmployeeIdResult.Title!, assignedEmployeeIdResult.Detail!);
            }

            var reminderValidation = ValidateReminderWindow(request.DueDate, request.ReminderAt);
            if (!reminderValidation.Succeeded)
            {
                return ApplicationServiceResult<TaskCreateWriteModel>.Failure(reminderValidation.StatusCode, reminderValidation.Title!, reminderValidation.Detail!);
            }

            return ApplicationServiceResult<TaskCreateWriteModel>.Success(new TaskCreateWriteModel
            {
                Title = titleResult.Value,
                Description = descriptionResult.Value,
                DueDate = request.DueDate,
                ReminderAt = request.ReminderAt,
                Priority = priorityResult.Value,
                Status = statusResult.Value,
                Outcome = outcomeResult.Value,
                MatterId = matterIdResult.Value,
                AssignedEmployeeId = assignedEmployeeIdResult.Value
            });
        }

        public ApplicationServiceResult<TaskPatchWriteModel> ValidateForUpdate(TaskUpdateRequest request)
        {
            if (request == null || !request.HasAnyField)
            {
                return ApplicationServiceResult<TaskPatchWriteModel>.Failure(
                    StatusCodes.Status400BadRequest,
                    "Invalid task update",
                    "At least one updatable field must be supplied.");
            }

            var model = new TaskPatchWriteModel();

            if (request.HasTitle)
            {
                if (string.IsNullOrWhiteSpace(request.Title))
                {
                    return ApplicationServiceResult<TaskPatchWriteModel>.Failure(
                        StatusCodes.Status400BadRequest,
                        "Invalid task update",
                        "Title cannot be empty.");
                }

                var titleResult = NormalizeOptionalField(request.Title, 256, "Title");
                if (!titleResult.Succeeded || string.IsNullOrWhiteSpace(titleResult.Value))
                {
                    return ApplicationServiceResult<TaskPatchWriteModel>.Failure(titleResult.StatusCode, titleResult.Title!, titleResult.Detail!);
                }

                model.HasTitle = true;
                model.Title = titleResult.Value;
            }

            if (request.HasDescription)
            {
                var descriptionResult = NormalizeOptionalField(request.Description, 4000, "Description");
                if (!descriptionResult.Succeeded)
                {
                    return ApplicationServiceResult<TaskPatchWriteModel>.Failure(descriptionResult.StatusCode, descriptionResult.Title!, descriptionResult.Detail!);
                }

                model.HasDescription = true;
                model.Description = descriptionResult.Value;
            }

            if (request.HasPriority)
            {
                var priorityResult = NormalizePriority(request.Priority, defaultValue: null);
                if (!priorityResult.Succeeded || priorityResult.Value == null)
                {
                    return ApplicationServiceResult<TaskPatchWriteModel>.Failure(priorityResult.StatusCode, priorityResult.Title!, priorityResult.Detail!);
                }

                model.HasPriority = true;
                model.Priority = priorityResult.Value;
            }

            if (request.HasStatus)
            {
                var statusResult = NormalizeStatus(request.Status, defaultValue: null);
                if (!statusResult.Succeeded || statusResult.Value == null)
                {
                    return ApplicationServiceResult<TaskPatchWriteModel>.Failure(statusResult.StatusCode, statusResult.Title!, statusResult.Detail!);
                }

                model.HasStatus = true;
                model.Status = statusResult.Value;
            }

            if (request.HasOutcome)
            {
                var outcomeResult = NormalizeOutcome(request.Outcome, allowClear: true);
                if (!outcomeResult.Succeeded)
                {
                    return ApplicationServiceResult<TaskPatchWriteModel>.Failure(outcomeResult.StatusCode, outcomeResult.Title!, outcomeResult.Detail!);
                }

                model.HasOutcome = true;
                model.Outcome = outcomeResult.Value;
            }

            if (request.HasMatterId)
            {
                var matterIdResult = NormalizeOptionalField(request.MatterId, 128, "MatterId");
                if (!matterIdResult.Succeeded)
                {
                    return ApplicationServiceResult<TaskPatchWriteModel>.Failure(matterIdResult.StatusCode, matterIdResult.Title!, matterIdResult.Detail!);
                }

                model.HasMatterId = true;
                model.MatterId = matterIdResult.Value;
            }

            if (request.HasAssignedEmployeeId)
            {
                var assignedEmployeeIdResult = NormalizeOptionalField(request.AssignedEmployeeId, 128, "AssignedEmployeeId");
                if (!assignedEmployeeIdResult.Succeeded)
                {
                    return ApplicationServiceResult<TaskPatchWriteModel>.Failure(assignedEmployeeIdResult.StatusCode, assignedEmployeeIdResult.Title!, assignedEmployeeIdResult.Detail!);
                }

                model.HasAssignedEmployeeId = true;
                model.AssignedEmployeeId = assignedEmployeeIdResult.Value;
            }

            if (request.HasDueDate)
            {
                model.HasDueDate = true;
                model.DueDate = request.DueDate;
            }

            if (request.HasReminderAt)
            {
                model.HasReminderAt = true;
                model.ReminderAt = request.ReminderAt;
            }

            var reminderValidation = ValidateReminderWindow(
                model.HasDueDate ? model.DueDate : null,
                model.HasReminderAt ? model.ReminderAt : null,
                allowPartialComparison: true);
            if (!reminderValidation.Succeeded)
            {
                return ApplicationServiceResult<TaskPatchWriteModel>.Failure(reminderValidation.StatusCode, reminderValidation.Title!, reminderValidation.Detail!);
            }

            return ApplicationServiceResult<TaskPatchWriteModel>.Success(model);
        }

        public ApplicationServiceResult<string> ValidateStatusUpdate(TaskStatusUpdateRequest request)
        {
            var normalizedStatus = NormalizeStatus(request.Status, defaultValue: null);
            if (!normalizedStatus.Succeeded || normalizedStatus.Value == null)
            {
                return ApplicationServiceResult<string>.Failure(
                    normalizedStatus.StatusCode,
                    normalizedStatus.Title!,
                    normalizedStatus.Detail!);
            }

            return ApplicationServiceResult<string>.Success(normalizedStatus.Value);
        }

        private static ApplicationServiceResult<string> NormalizeCreateTitle(string? title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return ApplicationServiceResult<string>.Success("Untitled Task");
            }

            return NormalizeRequiredField(title, 256, "Title");
        }

        private static ApplicationServiceResult<string> NormalizeRequiredField(string? value, int maxLength, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return ApplicationServiceResult<string>.Failure(
                    StatusCodes.Status400BadRequest,
                    "Invalid task",
                    $"{fieldName} is required.");
            }

            var trimmed = value.Trim();
            if (trimmed.Length > maxLength)
            {
                return ApplicationServiceResult<string>.Failure(
                    StatusCodes.Status400BadRequest,
                    "Invalid task",
                    $"{fieldName} exceeds the maximum length of {maxLength} characters.");
            }

            return ApplicationServiceResult<string>.Success(trimmed);
        }

        private static ApplicationServiceResult<string?> NormalizeOptionalField(string? value, int maxLength, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return ApplicationServiceResult<string?>.Success(null);
            }

            var trimmed = value.Trim();
            if (trimmed.Length > maxLength)
            {
                return ApplicationServiceResult<string?>.Failure(
                    StatusCodes.Status400BadRequest,
                    "Invalid task",
                    $"{fieldName} exceeds the maximum length of {maxLength} characters.");
            }

            return ApplicationServiceResult<string?>.Success(trimmed);
        }

        private static ApplicationServiceResult<string?> NormalizePriority(string? priority, string? defaultValue)
        {
            if (string.IsNullOrWhiteSpace(priority))
            {
                return defaultValue == null
                    ? ApplicationServiceResult<string?>.Failure(StatusCodes.Status400BadRequest, "Invalid task", "Task priority is required.")
                    : ApplicationServiceResult<string?>.Success(defaultValue);
            }

            var candidate = priority.Trim();
            var normalized = AllowedPriorities.FirstOrDefault(p => string.Equals(p, candidate, StringComparison.OrdinalIgnoreCase));
            return normalized == null
                ? ApplicationServiceResult<string?>.Failure(StatusCodes.Status400BadRequest, "Invalid task", "Task priority is invalid.")
                : ApplicationServiceResult<string?>.Success(normalized);
        }

        private static ApplicationServiceResult<string?> NormalizeStatus(string? status, string? defaultValue)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return defaultValue == null
                    ? ApplicationServiceResult<string?>.Failure(StatusCodes.Status400BadRequest, "Invalid task status", "Task status is invalid.")
                    : ApplicationServiceResult<string?>.Success(defaultValue);
            }

            var candidate = status.Trim();
            var normalized = AllowedStatuses.FirstOrDefault(s => string.Equals(s, candidate, StringComparison.OrdinalIgnoreCase));
            return normalized == null
                ? ApplicationServiceResult<string?>.Failure(StatusCodes.Status400BadRequest, "Invalid task status", "Task status is invalid.")
                : ApplicationServiceResult<string?>.Success(normalized);
        }

        private static ApplicationServiceResult<string?> NormalizeOutcome(string? outcome, bool allowClear)
        {
            if (string.IsNullOrWhiteSpace(outcome))
            {
                return allowClear
                    ? ApplicationServiceResult<string?>.Success(null)
                    : ApplicationServiceResult<string?>.Failure(StatusCodes.Status400BadRequest, "Invalid task", "Task outcome is invalid.");
            }

            var candidate = outcome.Trim();
            var normalized = AllowedOutcomes.FirstOrDefault(o => string.Equals(o, candidate, StringComparison.OrdinalIgnoreCase));
            return normalized == null
                ? ApplicationServiceResult<string?>.Failure(StatusCodes.Status400BadRequest, "Invalid task", "Task outcome is invalid.")
                : ApplicationServiceResult<string?>.Success(normalized);
        }

        private static ApplicationServiceResult<object> ValidateReminderWindow(DateTime? dueDate, DateTime? reminderAt, bool allowPartialComparison = false)
        {
            if (!dueDate.HasValue || !reminderAt.HasValue)
            {
                return allowPartialComparison || !reminderAt.HasValue || !dueDate.HasValue
                    ? ApplicationServiceResult<object>.Success(new object())
                    : ApplicationServiceResult<object>.Failure(StatusCodes.Status400BadRequest, "Invalid task", "ReminderAt cannot be after DueDate.");
            }

            return reminderAt.Value > dueDate.Value
                ? ApplicationServiceResult<object>.Failure(StatusCodes.Status400BadRequest, "Invalid task", "ReminderAt cannot be after DueDate.")
                : ApplicationServiceResult<object>.Success(new object());
        }
    }

    public sealed class TaskCreateWriteModel
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
    }

    public sealed class TaskPatchWriteModel
    {
        public bool HasTitle { get; set; }
        public string? Title { get; set; }
        public bool HasDescription { get; set; }
        public string? Description { get; set; }
        public bool HasDueDate { get; set; }
        public DateTime? DueDate { get; set; }
        public bool HasReminderAt { get; set; }
        public DateTime? ReminderAt { get; set; }
        public bool HasPriority { get; set; }
        public string? Priority { get; set; }
        public bool HasStatus { get; set; }
        public string? Status { get; set; }
        public bool HasOutcome { get; set; }
        public string? Outcome { get; set; }
        public bool HasMatterId { get; set; }
        public string? MatterId { get; set; }
        public bool HasAssignedEmployeeId { get; set; }
        public string? AssignedEmployeeId { get; set; }
    }
}
