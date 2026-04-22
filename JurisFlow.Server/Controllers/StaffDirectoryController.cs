using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JurisFlow.Server.Data;
using JurisFlow.Server.DTOs;
using JurisFlow.Server.Enums;
using JurisFlow.Server.Services;

namespace JurisFlow.Server.Controllers
{
    [Route("api/staff-directory")]
    [ApiController]
    [Authorize(Policy = "StaffOnly")]
    public class StaffDirectoryController : ControllerBase
    {
        private readonly JurisFlowDbContext _context;
        private readonly TenantContext _tenantContext;

        public StaffDirectoryController(JurisFlowDbContext context, TenantContext tenantContext)
        {
            _context = context;
            _tenantContext = tenantContext;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<EmployeeDirectoryItemDto>>> GetStaffDirectory([FromQuery] string? entityId, [FromQuery] string? officeId)
        {
            var query = TenantScope(_context.Employees)
                .AsNoTracking()
                .Where(e => e.Status != EmployeeStatus.Terminated);

            if (!string.IsNullOrWhiteSpace(entityId))
            {
                query = query.Where(e => e.EntityId == entityId.Trim());
            }

            if (!string.IsNullOrWhiteSpace(officeId))
            {
                query = query.Where(e => e.OfficeId == officeId.Trim());
            }

            var items = await query
                .OrderBy(e => e.FirstName)
                .ThenBy(e => e.LastName)
                .Select(e => new EmployeeDirectoryItemDto
                {
                    Id = e.Id,
                    FirstName = e.FirstName,
                    LastName = e.LastName,
                    Email = e.Email,
                    Avatar = e.User != null ? e.User.Avatar : null,
                    UserId = e.UserId,
                    Role = e.Role,
                    Status = e.Status,
                    EntityId = e.EntityId,
                    OfficeId = e.OfficeId
                })
                .ToListAsync();

            return Ok(items);
        }

        private IQueryable<Server.Models.Employee> TenantScope(IQueryable<Server.Models.Employee> query)
        {
            var tenantId = RequireTenantId();
            return query.Where(e => EF.Property<string>(e, "TenantId") == tenantId);
        }

        private string RequireTenantId()
        {
            if (string.IsNullOrWhiteSpace(_tenantContext.TenantId))
            {
                throw new InvalidOperationException("Tenant context is missing.");
            }

            return _tenantContext.TenantId;
        }
    }
}
