using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JurisFlow.Server.Data;
using JurisFlow.Server.DTOs;
using JurisFlow.Server.Services;

namespace JurisFlow.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Policy = "StaffOnly")]
    public class BootstrapController : ControllerBase
    {
        private static readonly HashSet<string> ClientReaderRoles = new(StringComparer.Ordinal)
        {
            "Admin", "Partner", "Associate", "Employee"
        };

        private readonly JurisFlowDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly ILogger<BootstrapController> _logger;

        public BootstrapController(
            JurisFlowDbContext context,
            IConfiguration configuration,
            ILogger<BootstrapController> logger)
        {
            _context = context;
            _configuration = configuration;
            _logger = logger;
        }

        [HttpGet]
        public async Task<ActionResult<BootstrapResponse>> GetBootstrap([FromQuery] string? scope = null)
        {
            var userId = GetUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Unauthorized();
            }

            if (!TryNormalizeScope(scope, out var normalizedScope))
            {
                return BadRequest(new { message = "scope must be one of: initial, deferred, full." });
            }

            var includeInitial = normalizedScope is "initial" or "full";
            var includeDeferred = normalizedScope is "deferred" or "full";

            try
            {
                var response = new BootstrapResponse();

                if (includeInitial)
                {
                    response.Matters = await _context.Matters
                        .AsNoTracking()
                        .OrderByDescending(m => m.OpenDate)
                        .ToListAsync();

                    response.Tasks = await _context.Tasks
                        .AsNoTracking()
                        .Where(t => t.MatterId == null || t.MatterId == "" || _context.Matters.Any(m => m.Id == t.MatterId))
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

                    response.TimeEntries = await _context.TimeEntries
                        .AsNoTracking()
                        .Where(t => t.MatterId == null || t.MatterId == "" || _context.Matters.Any(m => m.Id == t.MatterId))
                        .OrderByDescending(t => t.Date)
                        .ToListAsync();

                    response.Events = await _context.CalendarEvents
                        .AsNoTracking()
                        .Where(e => e.MatterId == null || e.MatterId == "" || _context.Matters.Any(m => m.Id == e.MatterId))
                        .OrderByDescending(e => e.Date)
                        .ToListAsync();

                    response.Notifications = await _context.Notifications
                        .AsNoTracking()
                        .Where(n => n.UserId == userId)
                        .OrderByDescending(n => n.CreatedAt)
                        .Take(100)
                        .ToListAsync();
                }

                if (includeDeferred)
                {
                    response.Expenses = await _context.Expenses
                        .AsNoTracking()
                        .Where(e => e.MatterId == null || e.MatterId == "" || _context.Matters.Any(m => m.Id == e.MatterId))
                        .OrderByDescending(e => e.Date)
                        .ToListAsync();

                    response.Leads = await _context.Leads
                        .AsNoTracking()
                        .OrderByDescending(l => l.CreatedAt)
                        .ToListAsync();

                    response.Invoices = await _context.Invoices
                        .AsNoTracking()
                        .Where(i => i.MatterId == null || i.MatterId == "" || _context.Matters.Any(m => m.Id == i.MatterId))
                        .Include(i => i.LineItems)
                        .OrderByDescending(i => i.CreatedAt)
                        .ToListAsync();

                    response.Documents = await _context.Documents
                        .AsNoTracking()
                        .Where(d => d.MatterId == null || d.MatterId == "" || _context.Matters.Any(m => m.Id == d.MatterId))
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

                    if (CanReadClients())
                    {
                        var clientsQuery = _context.Clients.AsNoTracking().AsQueryable();
                        if (ShouldHideSeedClient())
                        {
                            var seedClientEmail = EmailAddressNormalizer.Normalize(GetSeedClientEmail());
                            clientsQuery = clientsQuery.Where(c => c.NormalizedEmail != seedClientEmail);
                        }

                        response.Clients = await clientsQuery
                            .OrderByDescending(c => c.CreatedAt)
                            .Select(c => new
                            {
                                c.Id,
                                c.ClientNumber,
                                c.Name,
                                c.Email,
                                c.Phone,
                                c.Mobile,
                                c.Company,
                                c.Type,
                                c.Status,
                                c.PortalEnabled,
                                c.CreatedAt,
                                c.UpdatedAt
                            })
                            .ToListAsync();
                    }
                    else
                    {
                        response.Clients = Array.Empty<object>();
                    }

                    response.TaskTemplates = Array.Empty<object>();
                }

                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Bootstrap data load failed for user {UserId}", userId);
                return StatusCode(500, new { message = "Failed to load initial data." });
            }
        }

        private static bool TryNormalizeScope(string? scope, out string normalizedScope)
        {
            normalizedScope = string.IsNullOrWhiteSpace(scope)
                ? "full"
                : scope.Trim().ToLowerInvariant();

            return normalizedScope == "initial"
                || normalizedScope == "deferred"
                || normalizedScope == "full";
        }

        private bool CanReadClients()
        {
            foreach (var role in ClientReaderRoles)
            {
                if (User.IsInRole(role))
                {
                    return true;
                }
            }

            return false;
        }

        private bool ShouldHideSeedClient()
        {
            var explicitHide = _configuration.GetValue<bool?>("Seed:HidePortalClient");
            if (explicitHide.HasValue)
            {
                return explicitHide.Value;
            }

            return !_configuration.GetValue("Seed:PortalClientEnabled", false);
        }

        private string GetSeedClientEmail()
        {
            return _configuration["Seed:PortalClientEmail"] ?? "client.demo@jurisflow.local";
        }

        private string? GetUserId()
        {
            return User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                   ?? User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        }
    }
}
