using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using JurisFlow.Server.Data;
using JurisFlow.Server.DTOs;
using JurisFlow.Server.Models;
using JurisFlow.Server.Services;
using Task = System.Threading.Tasks.Task;

namespace JurisFlow.Server.Controllers
{
    [Route("api/time-entries")]
    [ApiController]
    [Authorize(Policy = "StaffOnly")]
    public class TimeEntriesController : ControllerBase
    {
        private const int DefaultPageSize = 100;
        private const int MaxPageSize = 100;
        private readonly JurisFlowDbContext _context;
        private readonly AuditLogger _auditLogger;
        private readonly ILogger<TimeEntriesController> _logger;
        private readonly OutcomeFeePlannerTriggerQueue _plannerTriggerQueue;
        private readonly MatterAccessService _matterAccess;

        public TimeEntriesController(JurisFlowDbContext context, AuditLogger auditLogger, ILogger<TimeEntriesController> logger, OutcomeFeePlannerTriggerQueue plannerTriggerQueue, MatterAccessService matterAccess)
        {
            _context = context;
            _auditLogger = auditLogger;
            _logger = logger;
            _plannerTriggerQueue = plannerTriggerQueue;
            _matterAccess = matterAccess;
        }

        [HttpGet]
        public async Task<IActionResult> GetTimeEntries(
            [FromQuery] string? matterId = null,
            [FromQuery] string? approvalStatus = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = DefaultPageSize)
        {
            var query = _context.TimeEntries.AsNoTracking().AsQueryable();
            var currentUserId = _matterAccess.GetCurrentUserId(User);
            var isPrivileged = _matterAccess.IsPrivileged(User);
            var readableMatterIds = _matterAccess.BuildReadableMatterIdsQuery(User);
            var normalizedPage = Math.Max(1, page);
            var normalizedPageSize = NormalizePageSize(pageSize);

            if (!string.IsNullOrWhiteSpace(matterId))
            {
                if (!await _matterAccess.CanReadMatterAsync(matterId, User, HttpContext.RequestAborted))
                {
                    return Ok(Array.Empty<TimeEntry>());
                }

                query = query.Where(t => t.MatterId == matterId);
            }
            else if (!isPrivileged)
            {
                query = query.Where(t =>
                    (!string.IsNullOrWhiteSpace(t.MatterId) && readableMatterIds.Contains(t.MatterId!)) ||
                    ((t.MatterId == null || t.MatterId == "") && t.SubmittedBy == currentUserId));
            }

            if (!string.IsNullOrWhiteSpace(approvalStatus))
            {
                query = query.Where(t => t.ApprovalStatus == approvalStatus);
            }

            var totalCount = await query.CountAsync(HttpContext.RequestAborted);
            var items = await query
                .OrderByDescending(t => t.Date)
                .ThenByDescending(t => t.CreatedAt)
                .Skip((normalizedPage - 1) * normalizedPageSize)
                .Take(normalizedPageSize)
                .Select(t => new TimeEntryListItemDto
                {
                    Id = t.Id,
                    MatterId = t.MatterId,
                    Description = t.Description,
                    Duration = t.Duration,
                    Rate = t.Rate,
                    Date = t.Date,
                    Billed = t.Billed,
                    IsBillable = t.IsBillable,
                    Type = t.Type,
                    ActivityCode = t.ActivityCode,
                    TaskCode = t.TaskCode,
                    ApprovalStatus = t.ApprovalStatus,
                    SubmittedAt = t.SubmittedAt,
                    ApprovedAt = t.ApprovedAt,
                    RejectedAt = t.RejectedAt,
                    RejectionReason = t.RejectionReason,
                    CreatedAt = t.CreatedAt,
                    UpdatedAt = t.UpdatedAt
                })
                .ToListAsync(HttpContext.RequestAborted);

            return Ok(new PagedCollectionResponse<TimeEntryListItemDto>
            {
                Items = items,
                TotalCount = totalCount,
                Page = normalizedPage,
                PageSize = normalizedPageSize,
                HasMore = normalizedPage * normalizedPageSize < totalCount
            });
        }

        [HttpPost]
        public async Task<IActionResult> CreateTimeEntry([FromBody] TimeEntryCreateDto dto)
        {
            if (dto.Duration <= 0)
            {
                return BadRequest(new { message = "Duration must be greater than zero." });
            }

            if (!string.IsNullOrWhiteSpace(dto.MatterId))
            {
                if (!await _matterAccess.CanReadMatterAsync(dto.MatterId, User, HttpContext.RequestAborted))
                {
                    return Forbid();
                }
            }

            var userId = GetUserId();

            var entry = new TimeEntry
            {
                MatterId = dto.MatterId,
                Description = dto.Description ?? string.Empty,
                Duration = dto.Duration,
                Rate = dto.Rate,
                Date = dto.Date ?? DateTime.UtcNow,
                Billed = dto.Billed,
                IsBillable = dto.IsBillable,
                Type = "time",
                ActivityCode = NormalizeUtbmsCode(dto.ActivityCode),
                TaskCode = NormalizeUtbmsCode(dto.TaskCode),
                ApprovalStatus = "Pending",
                SubmittedBy = userId,
                SubmittedAt = DateTime.UtcNow,
                ApprovedBy = null,
                ApprovedAt = null,
                UpdatedAt = DateTime.UtcNow
            };

            try
            {
                _context.TimeEntries.Add(entry);
                await _context.SaveChangesAsync();
                await _auditLogger.LogAsync(HttpContext, "time.create", "TimeEntry", entry.Id, $"MatterId={entry.MatterId}, Duration={entry.Duration}");
                await TryTriggerOutcomeFeePlannerAsync(entry.MatterId, "time_entry_create", entry.Id);
                return Ok(entry);
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Failed to create time entry.");
                return StatusCode(500, new { message = "Failed to create time entry. Please verify the matter selection." });
            }
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateTimeEntry(string id, [FromBody] TimeEntryUpdateDto dto)
        {
            var entry = await _context.TimeEntries.FindAsync(id);
            if (entry == null) return NotFound();
            if (!await CanAccessEntryAsync(entry))
            {
                return Forbid();
            }

            if (entry.ApprovalStatus == "Approved")
            {
                return BadRequest(new { message = "Approved time entries cannot be edited." });
            }

            if (dto.Description != null) entry.Description = dto.Description;
            if (dto.Duration.HasValue) entry.Duration = dto.Duration.Value;
            if (dto.Rate.HasValue) entry.Rate = dto.Rate.Value;
            if (dto.Date.HasValue) entry.Date = dto.Date.Value;
            if (dto.IsBillable.HasValue) entry.IsBillable = dto.IsBillable.Value;
            if (dto.ActivityCode != null) entry.ActivityCode = NormalizeUtbmsCode(dto.ActivityCode);
            if (dto.TaskCode != null) entry.TaskCode = NormalizeUtbmsCode(dto.TaskCode);
            entry.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await _auditLogger.LogAsync(HttpContext, "time.update", "TimeEntry", entry.Id, "Time entry updated");
            await TryTriggerOutcomeFeePlannerAsync(entry.MatterId, "time_entry_update", entry.Id);

            return Ok(entry);
        }

        [HttpPost("{id}/approve")]
        public async Task<IActionResult> ApproveTimeEntry(string id)
        {
            if (!IsApprover())
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "Only billing approvers can approve time entries." });
            }

            var entry = await _context.TimeEntries.FindAsync(id);
            if (entry == null) return NotFound();
            if (!await CanAccessEntryAsync(entry))
            {
                return Forbid();
            }

            entry.ApprovalStatus = "Approved";
            entry.ApprovedBy = GetUserId();
            entry.ApprovedAt = DateTime.UtcNow;
            entry.RejectedBy = null;
            entry.RejectedAt = null;
            entry.RejectionReason = null;
            entry.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await _auditLogger.LogAsync(HttpContext, "time.approve", "TimeEntry", entry.Id, "Time entry approved");
            await TryTriggerOutcomeFeePlannerAsync(entry.MatterId, "time_entry_approve", entry.Id);

            return Ok(entry);
        }

        [HttpPost("{id}/reject")]
        public async Task<IActionResult> RejectTimeEntry(string id, [FromBody] ApprovalRejectDto dto)
        {
            if (!IsApprover())
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "Only billing approvers can reject time entries." });
            }

            var entry = await _context.TimeEntries.FindAsync(id);
            if (entry == null) return NotFound();
            if (!await CanAccessEntryAsync(entry))
            {
                return Forbid();
            }

            entry.ApprovalStatus = "Rejected";
            entry.RejectedBy = GetUserId();
            entry.RejectedAt = DateTime.UtcNow;
            entry.RejectionReason = dto.Reason;
            entry.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await _auditLogger.LogAsync(HttpContext, "time.reject", "TimeEntry", entry.Id, $"Reason={dto.Reason}");

            return Ok(entry);
        }

        private string? GetUserId()
        {
            return User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
        }

        private bool IsApprover()
        {
            return HasAnyRole("Admin", "Partner", "Manager", "Associate", "Attorney", "Accountant");
        }

        private bool HasAnyRole(params string[] roles)
        {
            return roles.Any(role =>
                User.IsInRole(role) ||
                User.Claims.Any(claim =>
                    (claim.Type == ClaimTypes.Role || claim.Type == "role") &&
                    string.Equals(claim.Value, role, StringComparison.OrdinalIgnoreCase)));
        }

        private string? NormalizeUtbmsCode(string? code)
        {
            if (string.IsNullOrWhiteSpace(code)) return null;
            var trimmed = code.Trim();
            var split = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return split.Length > 0 ? split[0].Trim() : trimmed;
        }

        private async Task TryTriggerOutcomeFeePlannerAsync(string? matterId, string triggerType, string entityId)
        {
            if (string.IsNullOrWhiteSpace(matterId)) return;

            try
            {
                var enqueued = _plannerTriggerQueue.Enqueue(new OutcomeFeePlannerTriggerJob(
                    GetTenantId(),
                    GetTenantSlug(),
                    GetUserId() ?? "system",
                    new OutcomeFeePlanTriggerRequest
                    {
                        MatterId = matterId,
                        TriggerType = triggerType,
                        TriggerEntityType = nameof(TimeEntry),
                        TriggerEntityId = entityId,
                        QueueReviewOnDrift = true,
                        QueueNotificationOnDrift = true
                    }));

                if (!enqueued)
                {
                    _logger.LogWarning(
                        "Outcome-to-Fee planner trigger queue rejected time entry trigger. MatterId={MatterId} TriggerType={TriggerType} EntityId={EntityId}",
                        matterId,
                        triggerType,
                        entityId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Outcome-to-Fee planner trigger failed for time entry {TimeEntryId}", entityId);
            }
        }

        private string GetTenantId() =>
            User.FindFirst("tenantId")?.Value ?? string.Empty;

        private string GetTenantSlug() =>
            User.FindFirst("tenantSlug")?.Value
            ?? HttpContext.Request.Headers["X-Tenant-Slug"].FirstOrDefault()
            ?? string.Empty;

        private async Task<bool> CanAccessEntryAsync(TimeEntry entry)
        {
            if (_matterAccess.IsPrivileged(User))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(entry.MatterId))
            {
                return await _matterAccess.CanReadMatterAsync(entry.MatterId, User, HttpContext.RequestAborted);
            }

            var currentUserId = _matterAccess.GetCurrentUserId(User);
            return !string.IsNullOrWhiteSpace(currentUserId) &&
                string.Equals(entry.SubmittedBy, currentUserId, StringComparison.Ordinal);
        }

        private static int NormalizePageSize(int pageSize)
        {
            if (pageSize <= 0)
            {
                return DefaultPageSize;
            }

            return Math.Clamp(pageSize, 1, MaxPageSize);
        }
    }

    public class TimeEntryCreateDto
    {
        public string? MatterId { get; set; }
        public string? Description { get; set; }
        public int Duration { get; set; }
        public double Rate { get; set; }
        public DateTime? Date { get; set; }
        public bool Billed { get; set; }
        public bool IsBillable { get; set; } = true;
        public string? ActivityCode { get; set; }
        public string? TaskCode { get; set; }
    }

    public class TimeEntryUpdateDto
    {
        public string? Description { get; set; }
        public int? Duration { get; set; }
        public double? Rate { get; set; }
        public DateTime? Date { get; set; }
        public bool? IsBillable { get; set; }
        public string? ActivityCode { get; set; }
        public string? TaskCode { get; set; }
    }

    public class ApprovalRejectDto
    {
        public string? Reason { get; set; }
    }
}
