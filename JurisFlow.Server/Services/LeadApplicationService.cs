using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using JurisFlow.Server.Contracts;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using ThreadingTask = System.Threading.Tasks.Task;
using Npgsql;

namespace JurisFlow.Server.Services
{
    public sealed class LeadApplicationService
    {
        private const string LeadEmailIndexName = "IX_Leads_TenantId_NormalizedEmail";
        private readonly JurisFlowDbContext _context;
        private readonly AuditLogger _auditLogger;
        private readonly TenantContext _tenantContext;
        private readonly LeadRequestValidator _validator;
        private readonly BusinessSurfaceAuthorizationService _authorization;
        private readonly RefactorWaveOptions _options;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public LeadApplicationService(
            JurisFlowDbContext context,
            AuditLogger auditLogger,
            TenantContext tenantContext,
            LeadRequestValidator validator,
            BusinessSurfaceAuthorizationService authorization,
            IOptions<RefactorWaveOptions> options,
            IHttpContextAccessor httpContextAccessor)
        {
            _context = context;
            _auditLogger = auditLogger;
            _tenantContext = tenantContext;
            _validator = validator;
            _authorization = authorization;
            _options = options.Value;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task<IReadOnlyList<LeadResponse>> GetLeadsAsync()
        {
            var leads = await TenantScope(_context.Leads)
                .AsNoTracking()
                .OrderByDescending(l => l.CreatedAt)
                .ToListAsync();

            return leads.Select(LeadResponse.FromModel).ToList();
        }

        public async Task<LeadReadModelCollectionResponse> GetLeadReadModelPageAsync(
            int page,
            int pageSize,
            string? search = null,
            string? status = null)
        {
            var query = TenantScope(_context.Leads)
                .AsNoTracking()
                .AsQueryable();

            var normalizedSearch = search?.Trim().ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(normalizedSearch))
            {
                query = query.Where(l =>
                    l.Name.ToLower().Contains(normalizedSearch) ||
                    (l.Email != null && l.Email.ToLower().Contains(normalizedSearch)) ||
                    (l.Phone != null && l.Phone.ToLower().Contains(normalizedSearch)) ||
                    (l.Source != null && l.Source.ToLower().Contains(normalizedSearch)) ||
                    (l.PracticeArea != null && l.PracticeArea.ToLower().Contains(normalizedSearch)));
            }

            var normalizedStatus = status?.Trim();
            if (!string.IsNullOrWhiteSpace(normalizedStatus) &&
                !string.Equals(normalizedStatus, "all", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(l => l.Status == normalizedStatus);
            }

            var totalCount = await query.CountAsync();
            var items = await query
                .OrderByDescending(l => l.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(l => new LeadListItemResponse
                {
                    Id = l.Id,
                    Name = l.Name,
                    Email = l.Email,
                    Phone = l.Phone,
                    Source = l.Source,
                    CreatedBySource = l.CreatedBySource,
                    EstimatedValue = l.EstimatedValue,
                    Status = l.Status,
                    PracticeArea = l.PracticeArea,
                    IsArchived = l.IsArchived,
                    CreatedAt = l.CreatedAt,
                    UpdatedAt = l.UpdatedAt
                })
                .ToListAsync();

            return new LeadReadModelCollectionResponse
            {
                Items = items,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize,
                HasMore = (page * pageSize) < totalCount
            };
        }

        public async Task<LeadResponse?> GetLeadAsync(string id)
        {
            var lead = await TenantScope(_context.Leads)
                .AsNoTracking()
                .FirstOrDefaultAsync(l => l.Id == id);

            return lead == null ? null : LeadResponse.FromModel(lead);
        }

        public async Task<IReadOnlyList<LeadStatusHistoryResponse>?> GetStatusHistoryAsync(string id)
        {
            var exists = await TenantScope(_context.Leads)
                .AsNoTracking()
                .AnyAsync(l => l.Id == id);
            if (!exists)
            {
                return null;
            }

            var history = await TenantScope(_context.LeadStatusHistories)
                .AsNoTracking()
                .Where(h => h.LeadId == id)
                .OrderByDescending(h => h.CreatedAt)
                .ToListAsync();

            return history.Select(LeadStatusHistoryResponse.FromModel).ToList();
        }

        public async Task<ApplicationServiceResult<LeadResponse>> CreateLeadAsync(LeadCreateRequest request)
        {
            var validation = _validator.ValidateForCreate(request);
            if (!validation.Succeeded || validation.Value == null)
            {
                return ApplicationServiceResult<LeadResponse>.Failure(validation.StatusCode, validation.Title!, validation.Detail!);
            }

            if (!string.IsNullOrWhiteSpace(validation.Value.NormalizedEmail))
            {
                var duplicateEmail = await TenantScope(_context.Leads)
                    .AnyAsync(l => l.NormalizedEmail == validation.Value.NormalizedEmail);
                if (duplicateEmail)
                {
                    return ApplicationServiceResult<LeadResponse>.Failure(StatusCodes.Status400BadRequest, "Duplicate lead", "Email already exists.");
                }
            }

            var lead = new Lead
            {
                Id = Guid.NewGuid().ToString(),
                Name = validation.Value.Name,
                Email = validation.Value.Email,
                NormalizedEmail = validation.Value.NormalizedEmail,
                Phone = validation.Value.Phone,
                Source = validation.Value.Source,
                CreatedBySource = "Manual",
                EstimatedValue = validation.Value.EstimatedValue,
                Status = validation.Value.Status,
                PracticeArea = validation.Value.PracticeArea,
                Notes = validation.Value.Notes,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.Leads.Add(lead);
            AddStatusHistory(lead.Id, "New", lead.Status, null);

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (IsDuplicateLeadEmailConstraintViolation(ex))
            {
                return ApplicationServiceResult<LeadResponse>.Failure(StatusCodes.Status400BadRequest, "Duplicate lead", "Email already exists.");
            }

            await LogAuditAsync("lead.create", "Lead", lead.Id, $"Created lead {lead.Email ?? lead.Name}");

            return ApplicationServiceResult<LeadResponse>.Success(LeadResponse.FromModel(lead));
        }

        public async Task<ApplicationServiceResult<LeadResponse>> UpdateLeadAsync(string id, LeadUpdateRequest request)
        {
            var lead = await TenantScope(_context.Leads).FirstOrDefaultAsync(l => l.Id == id);
            if (lead == null)
            {
                return ApplicationServiceResult<LeadResponse>.Failure(StatusCodes.Status404NotFound, "Lead not found", "Lead was not found.");
            }

            var validation = _validator.ValidateForUpdate(request);
            if (!validation.Succeeded || validation.Value == null)
            {
                return ApplicationServiceResult<LeadResponse>.Failure(validation.StatusCode, validation.Title!, validation.Detail!);
            }

            if (!string.IsNullOrWhiteSpace(validation.Value.NormalizedEmail))
            {
                var duplicateEmail = await TenantScope(_context.Leads)
                    .AnyAsync(l => l.NormalizedEmail == validation.Value.NormalizedEmail && l.Id != id);
                if (duplicateEmail)
                {
                    return ApplicationServiceResult<LeadResponse>.Failure(StatusCodes.Status400BadRequest, "Duplicate lead", "Email already exists.");
                }
            }

            var previousStatus = lead.Status;

            if (validation.Value.Name != null) lead.Name = validation.Value.Name;
            if (request.Email != null)
            {
                lead.Email = validation.Value.Email;
                lead.NormalizedEmail = validation.Value.NormalizedEmail;
            }
            if (validation.Value.Phone != null) lead.Phone = validation.Value.Phone;
            if (validation.Value.Source != null) lead.Source = validation.Value.Source;
            if (validation.Value.EstimatedValue.HasValue) lead.EstimatedValue = validation.Value.EstimatedValue.Value;
            if (validation.Value.Status != null) lead.Status = validation.Value.Status;
            if (validation.Value.PracticeArea != null) lead.PracticeArea = validation.Value.PracticeArea;
            if (validation.Value.Notes != null) lead.Notes = validation.Value.Notes;

            lead.UpdatedAt = DateTime.UtcNow;

            if (!string.Equals(previousStatus, lead.Status, StringComparison.OrdinalIgnoreCase))
            {
                AddStatusHistory(lead.Id, previousStatus, lead.Status, validation.Value.StatusChangeNote);
            }

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (IsDuplicateLeadEmailConstraintViolation(ex))
            {
                return ApplicationServiceResult<LeadResponse>.Failure(StatusCodes.Status400BadRequest, "Duplicate lead", "Email already exists.");
            }

            await LogAuditAsync("lead.update", "Lead", lead.Id, $"Updated lead {lead.Email ?? lead.Name}");

            return ApplicationServiceResult<LeadResponse>.Success(LeadResponse.FromModel(lead));
        }

        public async Task<ApplicationServiceResult<object>> DeleteLeadAsync(string id)
        {
            if (_options.RestrictLeadDeleteToPrivilegedRoles && !_authorization.CanDeleteLead(CurrentUser))
            {
                return ApplicationServiceResult<object>.Failure(StatusCodes.Status403Forbidden, "Forbidden", "You do not have permission to archive leads.");
            }

            var lead = await TenantScope(_context.Leads.IgnoreQueryFilters())
                .FirstOrDefaultAsync(l => l.Id == id);
            if (lead == null)
            {
                return ApplicationServiceResult<object>.Failure(StatusCodes.Status404NotFound, "Lead not found", "Lead was not found.");
            }

            if (lead.IsArchived)
            {
                return ApplicationServiceResult<object>.Success(new { message = "Lead already archived." });
            }

            lead.IsArchived = true;
            lead.ArchivedAt = DateTime.UtcNow;
            lead.ArchivedByUserId = GetUserId();
            lead.ArchivedByName = GetUserEmail();
            lead.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await LogAuditAsync("lead.archive", "Lead", lead.Id, $"Archived lead {lead.Email ?? lead.Name}; hard delete disabled.");

            return ApplicationServiceResult<object>.Success(new { message = "Lead archived. Hard delete is disabled." });
        }

        private IQueryable<T> TenantScope<T>(IQueryable<T> query) where T : class
        {
            var tenantId = RequireTenantId();
            return query.Where(e => EF.Property<string>(e, "TenantId") == tenantId);
        }

        private string RequireTenantId()
        {
            if (string.IsNullOrWhiteSpace(_tenantContext.TenantId))
            {
                throw new InvalidOperationException("Tenant context is required.");
            }

            return _tenantContext.TenantId;
        }

        private ClaimsPrincipal CurrentUser => _httpContextAccessor.HttpContext?.User ?? new ClaimsPrincipal();

        private string? GetUserId()
        {
            return CurrentUser.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? CurrentUser.FindFirst("sub")?.Value;
        }

        private string? GetUserEmail()
        {
            return CurrentUser.FindFirst(ClaimTypes.Email)?.Value ?? CurrentUser.FindFirst("email")?.Value;
        }

        private void AddStatusHistory(string leadId, string? previousStatus, string? newStatus, string? notes)
        {
            _context.LeadStatusHistories.Add(new LeadStatusHistory
            {
                LeadId = leadId,
                PreviousStatus = previousStatus ?? "Unknown",
                NewStatus = newStatus ?? "Unknown",
                Notes = notes,
                ChangedByUserId = GetUserId(),
                ChangedByName = GetUserEmail(),
                CreatedAt = DateTime.UtcNow
            });
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

        private static bool IsDuplicateLeadEmailConstraintViolation(DbUpdateException exception)
        {
            return exception.InnerException switch
            {
                PostgresException postgresException => postgresException.SqlState == PostgresErrorCodes.UniqueViolation &&
                                                      string.Equals(postgresException.ConstraintName, LeadEmailIndexName, StringComparison.Ordinal),
                SqliteException sqliteException => sqliteException.SqliteErrorCode == 19 &&
                                                  sqliteException.Message.Contains("Leads.TenantId", StringComparison.Ordinal) &&
                                                  sqliteException.Message.Contains("Leads.NormalizedEmail", StringComparison.Ordinal),
                _ => false
            };
        }
    }
}
