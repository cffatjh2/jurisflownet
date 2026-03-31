using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JurisFlow.Server.Data;
using JurisFlow.Server.DTOs;
using JurisFlow.Server.Services;

namespace JurisFlow.Server.Controllers
{
    /// <summary>
    /// Consolidates the initial data load into a single request.
    /// Replaces 11 parallel API calls that the frontend fires after login,
    /// removing 10×(tenant-resolution + session-validation) middleware DB hits.
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Policy = "StaffOnly")]
    public class BootstrapController : ControllerBase
    {
        private readonly JurisFlowDbContext _context;
        private readonly ILogger<BootstrapController> _logger;

        public BootstrapController(JurisFlowDbContext context, ILogger<BootstrapController> logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// GET /api/bootstrap
        /// Returns all collections the frontend needs on initial load.
        /// </summary>
        [HttpGet]
        public async Task<ActionResult<BootstrapResponse>> GetBootstrap()
        {
            var userId = GetUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Unauthorized();
            }

            try
            {
                // Run all queries concurrently within a single DB context scope.
                // Each query uses AsNoTracking for read-only performance.
                var mattersTask = _context.Matters
                    .AsNoTracking()
                    .Include(m => m.Client)
                    .OrderByDescending(m => m.OpenDate)
                    .ToListAsync();

                var tasksTask = _context.Tasks
                    .AsNoTracking()
                    .Include(t => t.AssignedEmployee)
                    .OrderByDescending(t => t.CreatedAt)
                    .Select(t => new
                    {
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

                var timeEntriesTask = _context.TimeEntries
                    .AsNoTracking()
                    .ToListAsync();

                var expensesTask = _context.Expenses
                    .AsNoTracking()
                    .ToListAsync();

                var clientsTask = _context.Clients
                    .AsNoTracking()
                    .ToListAsync();

                var leadsTask = _context.Leads
                    .AsNoTracking()
                    .ToListAsync();

                var eventsTask = _context.CalendarEvents
                    .AsNoTracking()
                    .ToListAsync();

                var invoicesTask = _context.Invoices
                    .AsNoTracking()
                    .Include(i => i.LineItems)
                    .ToListAsync();

                var notificationsTask = _context.Notifications
                    .AsNoTracking()
                    .Where(n => n.UserId == userId)
                    .OrderByDescending(n => n.CreatedAt)
                    .Take(100)
                    .ToListAsync();

                var documentsTask = _context.Documents
                    .AsNoTracking()
                    .OrderByDescending(d => d.CreatedAt)
                    .Take(200)
                    .Select(d => new
                    {
                        d.Id,
                        d.Name,
                        d.FileSize,
                        d.MimeType,
                        d.MatterId,
                        d.Version,
                        d.Category,
                        d.Description,
                        d.Tags,
                        d.Status,
                        d.LegalHoldReason,
                        d.LegalHoldPlacedAt,
                        d.LegalHoldReleasedAt,
                        d.LegalHoldPlacedBy,
                        d.CreatedAt,
                        d.UpdatedAt
                    })
                    .ToListAsync();

                // Wait for all queries
                await Task.WhenAll(
                    mattersTask, tasksTask, timeEntriesTask, expensesTask,
                    clientsTask, leadsTask, eventsTask, invoicesTask,
                    notificationsTask, documentsTask);

                var response = new BootstrapResponse
                {
                    Matters = mattersTask.Result,
                    Tasks = tasksTask.Result,
                    TimeEntries = timeEntriesTask.Result,
                    Expenses = expensesTask.Result,
                    Clients = clientsTask.Result,
                    Leads = leadsTask.Result,
                    Events = eventsTask.Result,
                    Invoices = invoicesTask.Result,
                    Notifications = notificationsTask.Result,
                    Documents = documentsTask.Result,
                    TaskTemplates = Array.Empty<object>() // TaskTemplates are not DB-backed
                };

                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Bootstrap data load failed for user {UserId}", userId);
                return StatusCode(500, new { message = "Failed to load initial data." });
            }
        }

        private string? GetUserId()
        {
            return User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                   ?? User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        }
    }
}
