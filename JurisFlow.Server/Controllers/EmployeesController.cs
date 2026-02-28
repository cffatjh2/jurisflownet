using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using JurisFlow.Server.Enums;
using JurisFlow.Server.DTOs;
using JurisFlow.Server.Services;

namespace JurisFlow.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Policy = "StaffOnly")]
    public class EmployeesController : ControllerBase
    {
        private readonly JurisFlowDbContext _context;
        private readonly IWebHostEnvironment _env;
        private readonly FirmStructureService _firmStructure;
        private readonly PasswordPolicyService _passwordPolicy;
        private readonly TenantContext _tenantContext;
        private readonly IConfiguration _configuration;

        public EmployeesController(JurisFlowDbContext context, IWebHostEnvironment env, FirmStructureService firmStructure, PasswordPolicyService passwordPolicy, TenantContext tenantContext, IConfiguration configuration)
        {
            _context = context;
            _env = env;
            _firmStructure = firmStructure;
            _passwordPolicy = passwordPolicy;
            _tenantContext = tenantContext;
            _configuration = configuration;
        }

        // GET: api/Employees
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Employee>>> GetEmployees([FromQuery] string? entityId, [FromQuery] string? officeId)
        {
            var query = _context.Employees.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(entityId))
            {
                query = query.Where(e => e.EntityId == entityId);
            }
            if (!string.IsNullOrWhiteSpace(officeId))
            {
                query = query.Where(e => e.OfficeId == officeId);
            }

            return await query
                .Include(e => e.User)
                .OrderBy(e => e.FirstName)
                .ToListAsync();
        }

        // GET: api/Employees/5
        [HttpGet("{id}")]
        public async Task<ActionResult<Employee>> GetEmployee(string id)
        {
            var employee = await _context.Employees.FindAsync(id);

            if (employee == null)
            {
                return NotFound();
            }

            return employee;
        }

        // POST: api/Employees
        [HttpPost]
        public async Task<ActionResult<Employee>> PostEmployee(EmployeeCreateDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            // Check for duplicate email and clean up orphan User records
            var normalizedEmail = EmailAddressNormalizer.Normalize(dto.Email);
            var trimmedEmail = dto.Email.Trim();

            var existingEmployee = await _context.Employees.FirstOrDefaultAsync(e => e.Email == trimmedEmail);
            if (existingEmployee != null)
            {
                return Conflict(new { message = "This email address is already assigned to a staff member." });
            }

            // Check for orphan User record (User exists but Employee doesn't)
            var orphanUser = await _context.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail);
            if (orphanUser != null)
            {
                // Orphan user found - remove it to allow re-registration
                _context.Users.Remove(orphanUser);
                await _context.SaveChangesAsync();
            }

            // First, create a User account for login
            if (string.IsNullOrWhiteSpace(dto.Password))
            {
                return BadRequest(new { message = "Password is required." });
            }

            var passwordResult = _passwordPolicy.Validate(dto.Password, trimmedEmail, $"{dto.FirstName} {dto.LastName}");
            if (!passwordResult.IsValid)
            {
                return BadRequest(new { message = passwordResult.Message });
            }

            var userId = Guid.NewGuid().ToString();
            var passwordHash = PasswordHashingHelper.HashPassword(dto.Password, _configuration);
            
            // Determine user role based on employee role
            var userRole = dto.Role switch
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

            var user = new User
            {
                Id = userId,
                Name = $"{dto.FirstName} {dto.LastName}",
                Email = trimmedEmail,
                NormalizedEmail = normalizedEmail,
                PasswordHash = passwordHash,
                Role = userRole,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.Users.Add(user);

            // Now create the Employee linked to this User
            var resolved = await _firmStructure.ResolveEntityOfficeAsync(dto.EntityId, dto.OfficeId);
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
                HireDate = dto.HireDate ?? DateTime.UtcNow,
                HourlyRate = dto.HourlyRate,
                Salary = dto.Salary,
                Notes = dto.Notes,
                Address = dto.Address,
                EmergencyContact = dto.EmergencyContact,
                EmergencyPhone = dto.EmergencyPhone,
                EntityId = resolved.entityId,
                OfficeId = resolved.officeId,

                
                // Bar License Mapping
                BarNumber = dto.BarLicense?.BarNumber,
                BarJurisdiction = dto.BarLicense?.Jurisdiction,
                BarAdmissionDate = dto.BarLicense?.AdmissionDate,
                BarStatus = dto.BarLicense?.Status,

                UserId = userId, // Link to User
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.Employees.Add(employee);
            
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                if (EmployeeExists(employee.Id))
                {
                    return Conflict();
                }
                else
                {
                    throw;
                }
            }

            return CreatedAtAction("GetEmployee", new { id = employee.Id }, employee);
        }

        // PUT: api/Employees/5
        [HttpPut("{id}")]
        public async Task<IActionResult> PutEmployee(string id, EmployeeCreateDto dto)
        {
            var employee = await _context.Employees.FindAsync(id);
            if (employee == null)
            {
                return NotFound();
            }

            // Update fields
            employee.FirstName = dto.FirstName;
            employee.LastName = dto.LastName;
            employee.Email = dto.Email;
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
            if (!string.IsNullOrWhiteSpace(dto.EntityId)) employee.EntityId = dto.EntityId;
            if (!string.IsNullOrWhiteSpace(dto.OfficeId)) employee.OfficeId = dto.OfficeId;
            
            // Update Bar License
            employee.BarNumber = dto.BarLicense?.BarNumber;
            employee.BarJurisdiction = dto.BarLicense?.Jurisdiction;
            employee.BarAdmissionDate = dto.BarLicense?.AdmissionDate;
            employee.BarStatus = dto.BarLicense?.Status;

            employee.UpdatedAt = DateTime.UtcNow;
            _context.Entry(employee).State = EntityState.Modified;

            // Also update the linked User name if changed
            if (!string.IsNullOrEmpty(employee.UserId))
            {
                var user = await _context.Users.FindAsync(employee.UserId);
                if (user != null)
                {
                    user.Name = $"{dto.FirstName} {dto.LastName}";
                    // We don't update email/password here automatically for security/complexity reasons 
                    // unless specifically requested, but name sync is good practice.
                    _context.Users.Update(user);
                }
            }

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!EmployeeExists(id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return NoContent();
        }

        // DELETE: api/Employees/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteEmployee(string id)
        {
            var employee = await _context.Employees.FindAsync(id);
            if (employee == null)
            {
                return NotFound();
            }

            // Also delete the linked User record to allow re-registration with same email
            if (!string.IsNullOrEmpty(employee.UserId))
            {
                var user = await _context.Users.FindAsync(employee.UserId);
                if (user != null)
                {
                    _context.Users.Remove(user);
                }
            }

            _context.Employees.Remove(employee);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private bool EmployeeExists(string id)
        {
            return _context.Employees.Any(e => e.Id == id);
        }

        [HttpPost("{id}/avatar")]
        public async Task<IActionResult> UploadAvatar(string id, IFormFile file)
        {
            var employee = await _context.Employees.Include(e => e.User).FirstOrDefaultAsync(e => e.Id == id);
            if (employee == null) return NotFound();
            if (file == null || file.Length == 0) return BadRequest("File is required.");

            var uploadsRoot = GetAvatarRoot();
            if (!Directory.Exists(uploadsRoot))
            {
                Directory.CreateDirectory(uploadsRoot);
            }

            var extension = Path.GetExtension(file.FileName);
            var fileName = $"{Guid.NewGuid()}{extension}";
            var savePath = Path.Combine(uploadsRoot, fileName);

            using (var stream = new FileStream(savePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            var relativePath = $"/api/files/avatars/{fileName}";
            if (employee.User != null)
            {
                employee.User.Avatar = relativePath;
                _context.Users.Update(employee.User);
            }
            await _context.SaveChangesAsync();

            return Ok(new { url = relativePath });
        }

        private string GetAvatarRoot()
        {
            if (string.IsNullOrWhiteSpace(_tenantContext.TenantId))
            {
                throw new InvalidOperationException("Tenant context is missing.");
            }

            return Path.Combine(_env.ContentRootPath, "uploads", _tenantContext.TenantId, "avatars");
        }
    }
}

