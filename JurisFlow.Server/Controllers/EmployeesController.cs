using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using JurisFlow.Server.Data;
using JurisFlow.Server.DTOs;
using JurisFlow.Server.Enums;
using JurisFlow.Server.Models;
using JurisFlow.Server.Services;

namespace JurisFlow.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Policy = "EmployeeAdminOnly")]
    public class EmployeesController : ControllerBase
    {
        private const string DisabledUserRole = "Disabled";
        private const long MaxAvatarSizeBytes = 5 * 1024 * 1024;
        private const long MaxAvatarRequestBodyBytes = MaxAvatarSizeBytes + (1024 * 1024);
        private static readonly IReadOnlyDictionary<string, string> AvatarMimeToExtension =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["image/png"] = ".png",
                ["image/jpeg"] = ".jpg",
                ["image/webp"] = ".webp",
                ["image/gif"] = ".gif"
            };

        private readonly JurisFlowDbContext _context;
        private readonly IAppFileStorage _fileStorage;
        private readonly FirmStructureService _firmStructure;
        private readonly PasswordPolicyService _passwordPolicy;
        private readonly TenantContext _tenantContext;
        private readonly IConfiguration _configuration;

        public EmployeesController(
            JurisFlowDbContext context,
            IAppFileStorage fileStorage,
            FirmStructureService firmStructure,
            PasswordPolicyService passwordPolicy,
            TenantContext tenantContext,
            IConfiguration configuration)
        {
            _context = context;
            _fileStorage = fileStorage;
            _firmStructure = firmStructure;
            _passwordPolicy = passwordPolicy;
            _tenantContext = tenantContext;
            _configuration = configuration;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<EmployeeResponseDto>>> GetEmployees([FromQuery] string? entityId, [FromQuery] string? officeId)
        {
            var query = TenantScope(_context.Employees).AsNoTracking();
            if (!string.IsNullOrWhiteSpace(entityId))
            {
                query = query.Where(e => e.EntityId == entityId.Trim());
            }

            if (!string.IsNullOrWhiteSpace(officeId))
            {
                query = query.Where(e => e.OfficeId == officeId.Trim());
            }

            var items = await ProjectEmployeeResponses(query)
                .OrderBy(e => e.FirstName)
                .ThenBy(e => e.LastName)
                .ToListAsync();

            return Ok(items);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<EmployeeResponseDto>> GetEmployee(string id)
        {
            var employee = await ProjectEmployeeResponses(TenantScope(_context.Employees).AsNoTracking())
                .FirstOrDefaultAsync(e => e.Id == id);

            if (employee == null)
            {
                return NotFound();
            }

            return Ok(employee);
        }

        [HttpPost]
        [EnableRateLimiting("AdminDangerousOps")]
        public async Task<ActionResult<EmployeeResponseDto>> PostEmployee(EmployeeCreateDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var trimmedEmail = dto.Email.Trim();
            var normalizedEmail = EmailAddressNormalizer.Normalize(trimmedEmail);

            if (await TenantScope(_context.Employees).AnyAsync(e => e.Email == trimmedEmail))
            {
                return Conflict(new { message = "This email address is already assigned to a staff member." });
            }

            if (string.IsNullOrWhiteSpace(dto.Password))
            {
                return BadRequest(new { message = "Password is required." });
            }

            var passwordResult = _passwordPolicy.Validate(dto.Password, trimmedEmail, $"{dto.FirstName} {dto.LastName}");
            if (!passwordResult.IsValid)
            {
                return BadRequest(new { message = passwordResult.Message });
            }

            await using var transaction = await _context.Database.BeginTransactionAsync();

            var orphanUser = await TenantScope(_context.Users).FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail);
            if (orphanUser != null)
            {
                _context.Users.Remove(orphanUser);
                await _context.SaveChangesAsync();
            }

            var resolved = await _firmStructure.ResolveEntityOfficeAsync(dto.EntityId, dto.OfficeId);
            var userId = Guid.NewGuid().ToString();
            var now = DateTime.UtcNow;
            var user = new User
            {
                Id = userId,
                Name = $"{dto.FirstName} {dto.LastName}".Trim(),
                Email = trimmedEmail,
                NormalizedEmail = normalizedEmail,
                PasswordHash = PasswordHashingHelper.HashPassword(dto.Password, _configuration),
                Role = MapEmployeeRoleToUserRole(dto.Role),
                CreatedAt = now,
                UpdatedAt = now
            };

            var employee = new Employee
            {
                Id = Guid.NewGuid().ToString(),
                FirstName = dto.FirstName,
                LastName = dto.LastName,
                Email = trimmedEmail,
                Phone = dto.Phone,
                Mobile = dto.Mobile,
                Role = dto.Role,
                Status = dto.Status,
                HireDate = dto.HireDate ?? now,
                HourlyRate = dto.HourlyRate,
                Salary = dto.Salary,
                Notes = dto.Notes,
                Address = dto.Address,
                EmergencyContact = dto.EmergencyContact,
                EmergencyPhone = dto.EmergencyPhone,
                EntityId = resolved.entityId,
                OfficeId = resolved.officeId,
                BarNumber = dto.BarLicense?.BarNumber,
                BarJurisdiction = dto.BarLicense?.Jurisdiction,
                BarAdmissionDate = dto.BarLicense?.AdmissionDate,
                BarStatus = dto.BarLicense?.Status,
                UserId = userId,
                CreatedAt = now,
                UpdatedAt = now,
                User = user
            };

            _context.Users.Add(user);
            _context.Employees.Add(employee);

            try
            {
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch (DbUpdateException)
            {
                await transaction.RollbackAsync();
                if (await EmployeeExistsAsync(employee.Id))
                {
                    return Conflict();
                }

                throw;
            }

            return CreatedAtAction(nameof(GetEmployee), new { id = employee.Id }, ToEmployeeResponse(employee, user));
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> PutEmployee(string id, EmployeeCreateDto dto)
        {
            var employee = await TenantScope(_context.Employees)
                .Include(e => e.User)
                .FirstOrDefaultAsync(e => e.Id == id);
            if (employee == null)
            {
                return NotFound();
            }

            var trimmedEmail = dto.Email.Trim();
            if (await TenantScope(_context.Employees).AnyAsync(e => e.Id != id && e.Email == trimmedEmail))
            {
                return Conflict(new { message = "This email address is already assigned to a staff member." });
            }

            employee.FirstName = dto.FirstName;
            employee.LastName = dto.LastName;
            employee.Email = trimmedEmail;
            employee.Phone = dto.Phone;
            employee.Mobile = dto.Mobile;
            employee.Role = dto.Role;
            employee.Status = dto.Status;
            employee.HireDate = dto.HireDate ?? employee.HireDate;
            employee.HourlyRate = dto.HourlyRate;
            employee.Salary = dto.Salary;
            employee.Notes = dto.Notes;
            employee.Address = dto.Address;
            employee.EmergencyContact = dto.EmergencyContact;
            employee.EmergencyPhone = dto.EmergencyPhone;
            if (!string.IsNullOrWhiteSpace(dto.EntityId))
            {
                employee.EntityId = dto.EntityId;
            }

            if (!string.IsNullOrWhiteSpace(dto.OfficeId))
            {
                employee.OfficeId = dto.OfficeId;
            }

            employee.BarNumber = dto.BarLicense?.BarNumber;
            employee.BarJurisdiction = dto.BarLicense?.Jurisdiction;
            employee.BarAdmissionDate = dto.BarLicense?.AdmissionDate;
            employee.BarStatus = dto.BarLicense?.Status;
            employee.TerminationDate = dto.Status == EmployeeStatus.Terminated
                ? employee.TerminationDate ?? DateTime.UtcNow
                : null;
            employee.UpdatedAt = DateTime.UtcNow;

            if (employee.User != null)
            {
                employee.User.Name = $"{dto.FirstName} {dto.LastName}".Trim();
                employee.User.Email = trimmedEmail;
                employee.User.NormalizedEmail = EmailAddressNormalizer.Normalize(trimmedEmail);
                employee.User.UpdatedAt = DateTime.UtcNow;
                if (dto.Status == EmployeeStatus.Terminated)
                {
                    DeactivateUser(employee.User, employee.Email);
                    await RevokeUserSessionsAsync(employee.User.Id, "Employee deactivated");
                }
                else if (string.Equals(employee.User.Role, DisabledUserRole, StringComparison.Ordinal))
                {
                    employee.User.Role = MapEmployeeRoleToUserRole(dto.Role);
                }
            }

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!await EmployeeExistsAsync(id))
                {
                    return NotFound();
                }

                throw;
            }

            return NoContent();
        }

        [HttpDelete("{id}")]
        [EnableRateLimiting("AdminDangerousOps")]
        public async Task<IActionResult> DeleteEmployee(string id)
        {
            var employee = await TenantScope(_context.Employees)
                .Include(e => e.User)
                .FirstOrDefaultAsync(e => e.Id == id);
            if (employee == null)
            {
                return NotFound();
            }

            await using var transaction = await _context.Database.BeginTransactionAsync();

            employee.Status = EmployeeStatus.Terminated;
            employee.TerminationDate ??= DateTime.UtcNow;
            employee.UpdatedAt = DateTime.UtcNow;

            if (employee.User != null)
            {
                DeactivateUser(employee.User, employee.Email);
                await RevokeUserSessionsAsync(employee.User.Id, "Employee deactivated");
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return Ok(new
            {
                message = "Employee deactivated via delete endpoint. Hard delete is disabled.",
                employee = ToEmployeeResponse(employee, employee.User)
            });
        }

        [HttpPost("{id}/avatar")]
        [EnableRateLimiting("AdminDangerousOps")]
        [RequestSizeLimit(MaxAvatarRequestBodyBytes)]
        public async Task<IActionResult> UploadAvatar(string id, IFormFile file)
        {
            var employee = await TenantScope(_context.Employees)
                .Include(e => e.User)
                .FirstOrDefaultAsync(e => e.Id == id);
            if (employee == null)
            {
                return NotFound();
            }

            if (employee.User == null)
            {
                return Conflict(new { message = "Employee does not have a linked user account." });
            }

            if (file == null || file.Length == 0)
            {
                return BadRequest("File is required.");
            }

            if (file.Length > MaxAvatarSizeBytes)
            {
                return BadRequest(new { message = "Avatar exceeds the 5 MB limit." });
            }

            var declaredMimeType = file.ContentType?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(declaredMimeType) ||
                !AvatarMimeToExtension.TryGetValue(declaredMimeType, out var extension))
            {
                return BadRequest(new { message = "Avatar MIME type is not allowed." });
            }

            var fileName = $"{Guid.NewGuid()}{extension}";
            var savePath = GetAvatarPath(fileName);
            byte[] avatarBytes;
            await using (var stream = file.OpenReadStream())
            using (var buffer = new MemoryStream())
            {
                await stream.CopyToAsync(buffer);
                avatarBytes = buffer.ToArray();
            }

            if (!FileSignatureValidator.IsValidAvatarImage(declaredMimeType, avatarBytes))
            {
                return BadRequest(new { message = "Avatar content does not match the declared MIME type." });
            }

            await _fileStorage.SaveBytesAsync(savePath, avatarBytes, declaredMimeType);

            var relativePath = $"/api/files/avatars/{fileName}";
            employee.User.Avatar = relativePath;
            employee.User.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Ok(new { url = relativePath });
        }

        private IQueryable<Employee> TenantScope(IQueryable<Employee> query)
        {
            var tenantId = RequireTenantId();
            return query.Where(e => EF.Property<string>(e, "TenantId") == tenantId);
        }

        private IQueryable<User> TenantScope(IQueryable<User> query)
        {
            var tenantId = RequireTenantId();
            return query.Where(u => EF.Property<string>(u, "TenantId") == tenantId);
        }

        private IQueryable<EmployeeResponseDto> ProjectEmployeeResponses(IQueryable<Employee> query)
        {
            return query.Select(e => new EmployeeResponseDto
            {
                Id = e.Id,
                FirstName = e.FirstName,
                LastName = e.LastName,
                Email = e.Email,
                Avatar = e.User != null ? e.User.Avatar : null,
                Phone = e.Phone,
                Mobile = e.Mobile,
                Role = e.Role,
                Status = e.Status,
                HireDate = e.HireDate,
                TerminationDate = e.TerminationDate,
                HourlyRate = e.HourlyRate,
                Salary = e.Salary,
                UserId = e.UserId,
                Notes = e.Notes,
                Address = e.Address,
                EmergencyContact = e.EmergencyContact,
                EmergencyPhone = e.EmergencyPhone,
                CreatedAt = e.CreatedAt,
                UpdatedAt = e.UpdatedAt,
                BarNumber = e.BarNumber,
                BarJurisdiction = e.BarJurisdiction,
                BarAdmissionDate = e.BarAdmissionDate,
                BarStatus = e.BarStatus,
                EntityId = e.EntityId,
                OfficeId = e.OfficeId
            });
        }

        private static EmployeeResponseDto ToEmployeeResponse(Employee employee, User? user)
        {
            return new EmployeeResponseDto
            {
                Id = employee.Id,
                FirstName = employee.FirstName,
                LastName = employee.LastName,
                Email = employee.Email,
                Avatar = user?.Avatar,
                Phone = employee.Phone,
                Mobile = employee.Mobile,
                Role = employee.Role,
                Status = employee.Status,
                HireDate = employee.HireDate,
                TerminationDate = employee.TerminationDate,
                HourlyRate = employee.HourlyRate,
                Salary = employee.Salary,
                UserId = employee.UserId,
                Notes = employee.Notes,
                Address = employee.Address,
                EmergencyContact = employee.EmergencyContact,
                EmergencyPhone = employee.EmergencyPhone,
                CreatedAt = employee.CreatedAt,
                UpdatedAt = employee.UpdatedAt,
                BarNumber = employee.BarNumber,
                BarJurisdiction = employee.BarJurisdiction,
                BarAdmissionDate = employee.BarAdmissionDate,
                BarStatus = employee.BarStatus,
                EntityId = employee.EntityId,
                OfficeId = employee.OfficeId
            };
        }

        private async Task<bool> EmployeeExistsAsync(string id)
        {
            return await TenantScope(_context.Employees).AnyAsync(e => e.Id == id);
        }

        private void DeactivateUser(User user, string employeeEmail)
        {
            user.Role = DisabledUserRole;
            user.PasswordHash = PasswordHashingHelper.HashPassword($"disabled-{Guid.NewGuid():N}-Aa1!", _configuration);
            user.MfaEnabled = false;
            user.MfaSecret = null;
            user.MfaBackupCodesJson = null;
            user.MfaVerifiedAt = null;
            user.MfaLastUsedAt = null;
            user.UpdatedAt = DateTime.UtcNow;
            user.Name = string.IsNullOrWhiteSpace(user.Name) ? employeeEmail : user.Name;
        }

        private async System.Threading.Tasks.Task RevokeUserSessionsAsync(string userId, string reason)
        {
            var tenantId = RequireTenantId();
            var now = DateTime.UtcNow;
            var sessions = await _context.AuthSessions
                .Where(s => s.TenantId == tenantId && s.UserId == userId && s.SubjectType == "User" && s.RevokedAt == null)
                .ToListAsync();

            foreach (var session in sessions)
            {
                session.RevokedAt = now;
                session.RevokedReason = reason;
            }
        }

        private string GetAvatarPath(string fileName)
        {
            if (string.IsNullOrWhiteSpace(_tenantContext.TenantId))
            {
                throw new InvalidOperationException("Tenant context is missing.");
            }

            return $"uploads/{_tenantContext.TenantId}/avatars/{fileName}";
        }

        private string RequireTenantId()
        {
            if (string.IsNullOrWhiteSpace(_tenantContext.TenantId))
            {
                throw new InvalidOperationException("Tenant context is missing.");
            }

            return _tenantContext.TenantId;
        }

        private static string MapEmployeeRoleToUserRole(EmployeeRole role)
        {
            return role switch
            {
                EmployeeRole.Partner => "Partner",
                EmployeeRole.Associate => "Attorney",
                EmployeeRole.OfCounsel => "Attorney",
                EmployeeRole.Paralegal => "Staff",
                EmployeeRole.LegalSecretary => "Staff",
                EmployeeRole.LegalAssistant => "Staff",
                EmployeeRole.OfficeManager => "Manager",
                EmployeeRole.Receptionist => "Staff",
                EmployeeRole.Accountant => "Staff",
                _ => "Staff"
            };
        }
    }
}
