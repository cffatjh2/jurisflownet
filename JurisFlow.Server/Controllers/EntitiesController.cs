using System.ComponentModel.DataAnnotations;
using System.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using JurisFlow.Server.Services;

namespace JurisFlow.Server.Controllers
{
    [Route("api/entities")]
    [ApiController]
    [Authorize(Policy = "StaffOnly")]
    public class EntitiesController : ControllerBase
    {
        private const int DefaultListLimit = 100;
        private const int MaxListLimit = 200;

        private static readonly EmailAddressAttribute EmailValidator = new();

        private readonly JurisFlowDbContext _context;
        private readonly AuditLogger _auditLogger;
        private readonly TenantContext _tenantContext;

        public EntitiesController(JurisFlowDbContext context, AuditLogger auditLogger, TenantContext tenantContext)
        {
            _context = context;
            _auditLogger = auditLogger;
            _tenantContext = tenantContext;
        }

        [HttpGet]
        public async Task<IActionResult> GetEntities([FromQuery] int limit = DefaultListLimit)
        {
            RequireTenantId();
            var normalizedLimit = NormalizeLimit(limit);

            var entities = await TenantScope(_context.FirmEntities)
                .AsNoTracking()
                .OrderByDescending(e => e.IsDefault)
                .ThenByDescending(e => e.IsActive)
                .ThenBy(e => e.Name)
                .Take(normalizedLimit)
                .Select(e => new EntityResponseDto
                {
                    Id = e.Id,
                    Name = e.Name,
                    LegalName = e.LegalName,
                    TaxId = e.TaxId,
                    Email = e.Email,
                    Phone = e.Phone,
                    Website = e.Website,
                    Address = e.Address,
                    City = e.City,
                    State = e.State,
                    ZipCode = e.ZipCode,
                    Country = e.Country,
                    IsDefault = e.IsDefault,
                    IsActive = e.IsActive,
                    CreatedAt = e.CreatedAt,
                    UpdatedAt = e.UpdatedAt,
                    OfficeCount = e.Offices.Count
                })
                .ToListAsync();

            return Ok(entities);
        }

        [HttpPost]
        [Authorize(Roles = "Admin,Partner")]
        public async Task<IActionResult> CreateEntity([FromBody] EntityDto dto)
        {
            RequireTenantId();
            if (dto == null)
            {
                return BadRequest(new { message = "Request body is required." });
            }

            var validationError = ValidateEntityDto(dto, requireName: true);
            if (validationError != null)
            {
                return validationError;
            }

            var normalizedName = dto.Name!.Trim();
            if (await HasEntityNameConflictAsync(normalizedName))
            {
                return Conflict(new { message = "An entity with this name already exists." });
            }

            if (dto.IsDefault == true && dto.IsActive == false)
            {
                return BadRequest(new { message = "An inactive entity cannot be the default entity." });
            }

            await using var tx = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

            var hasActiveDefault = await TenantScope(_context.FirmEntities).AnyAsync(e => e.IsDefault && e.IsActive);
            var entity = new FirmEntity
            {
                Name = normalizedName,
                LegalName = NormalizeOptional(dto.LegalName),
                TaxId = NormalizeOptional(dto.TaxId),
                Email = NormalizeEmail(dto.Email),
                Phone = NormalizeOptional(dto.Phone),
                Website = NormalizeWebsite(dto.Website),
                Address = NormalizeOptional(dto.Address),
                City = NormalizeOptional(dto.City),
                State = NormalizeOptional(dto.State),
                ZipCode = NormalizeOptional(dto.ZipCode),
                Country = NormalizeOptional(dto.Country),
                IsActive = dto.IsActive ?? true,
                IsDefault = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            entity.IsDefault = entity.IsActive && (dto.IsDefault ?? !hasActiveDefault);

            var defaultsCleared = 0;
            if (entity.IsDefault)
            {
                defaultsCleared = await ClearEntityDefaultsAsync(null);
            }

            _context.FirmEntities.Add(entity);
            await _context.SaveChangesAsync();
            await tx.CommitAsync();

            await _auditLogger.LogAsync(
                HttpContext,
                "entity.create",
                "FirmEntity",
                entity.Id,
                $"Created entity {entity.Name}; Default={entity.IsDefault}; DefaultsCleared={defaultsCleared}");

            return Created($"/api/entities/{entity.Id}", await MapEntityAsync(entity));
        }

        [HttpPut("{id}")]
        [Authorize(Roles = "Admin,Partner")]
        public async Task<IActionResult> UpdateEntity(string id, [FromBody] EntityDto dto)
        {
            RequireTenantId();
            if (dto == null)
            {
                return BadRequest(new { message = "Request body is required." });
            }

            var entity = await FindEntityAsync(id);
            if (entity == null)
            {
                return NotFound();
            }

            var validationError = ValidateEntityDto(dto, requireName: false);
            if (validationError != null)
            {
                return validationError;
            }

            if (dto.Name != null)
            {
                var normalizedName = dto.Name.Trim();
                if (!string.Equals(entity.Name, normalizedName, StringComparison.OrdinalIgnoreCase)
                    && await HasEntityNameConflictAsync(normalizedName, entity.Id))
                {
                    return Conflict(new { message = "An entity with this name already exists." });
                }
                entity.Name = normalizedName;
            }

            await using var tx = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

            var wantsActive = dto.IsActive ?? entity.IsActive;
            if (dto.IsDefault == true && !wantsActive)
            {
                return BadRequest(new { message = "An inactive entity cannot be the default entity." });
            }

            if (!wantsActive && entity.IsActive)
            {
                var activeEntityCount = await TenantScope(_context.FirmEntities).CountAsync(e => e.IsActive);
                if (activeEntityCount <= 1)
                {
                    return Conflict(new { message = "At least one active entity must remain." });
                }
            }

            entity.LegalName = dto.LegalName != null ? NormalizeOptional(dto.LegalName) : entity.LegalName;
            entity.TaxId = dto.TaxId != null ? NormalizeOptional(dto.TaxId) : entity.TaxId;
            entity.Email = dto.Email != null ? NormalizeEmail(dto.Email) : entity.Email;
            entity.Phone = dto.Phone != null ? NormalizeOptional(dto.Phone) : entity.Phone;
            entity.Website = dto.Website != null ? NormalizeWebsite(dto.Website) : entity.Website;
            entity.Address = dto.Address != null ? NormalizeOptional(dto.Address) : entity.Address;
            entity.City = dto.City != null ? NormalizeOptional(dto.City) : entity.City;
            entity.State = dto.State != null ? NormalizeOptional(dto.State) : entity.State;
            entity.ZipCode = dto.ZipCode != null ? NormalizeOptional(dto.ZipCode) : entity.ZipCode;
            entity.Country = dto.Country != null ? NormalizeOptional(dto.Country) : entity.Country;

            var defaultsCleared = 0;
            var promotedEntityId = string.Empty;

            if (dto.IsDefault == true)
            {
                defaultsCleared = await ClearEntityDefaultsAsync(entity.Id);
                entity.IsDefault = true;
            }
            else if (entity.IsDefault && (dto.IsDefault == false || !wantsActive))
            {
                var fallback = await FindActiveEntityFallbackAsync(entity.Id);
                if (fallback == null)
                {
                    return Conflict(new { message = "At least one active default entity must remain." });
                }

                fallback.IsDefault = true;
                fallback.UpdatedAt = DateTime.UtcNow;
                entity.IsDefault = false;
                promotedEntityId = fallback.Id;
            }

            entity.IsActive = wantsActive;

            if (entity.IsActive && !entity.IsDefault)
            {
                var hasOtherActiveDefault = await TenantScope(_context.FirmEntities)
                    .AnyAsync(e => e.Id != entity.Id && e.IsDefault && e.IsActive);
                if (!hasOtherActiveDefault)
                {
                    entity.IsDefault = true;
                }
            }

            entity.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            await tx.CommitAsync();

            await _auditLogger.LogAsync(
                HttpContext,
                "entity.update",
                "FirmEntity",
                entity.Id,
                $"Updated entity {entity.Name}; Default={entity.IsDefault}; Active={entity.IsActive}; DefaultsCleared={defaultsCleared}; Promoted={promotedEntityId}");

            return Ok(await MapEntityAsync(entity));
        }

        [HttpPost("{id}/default")]
        [Authorize(Roles = "Admin,Partner")]
        public async Task<IActionResult> SetDefaultEntity(string id)
        {
            RequireTenantId();
            var entity = await FindEntityAsync(id);
            if (entity == null)
            {
                return NotFound();
            }

            if (!entity.IsActive)
            {
                return BadRequest(new { message = "Inactive entities cannot be set as default." });
            }

            await using var tx = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var defaultsCleared = await ClearEntityDefaultsAsync(entity.Id);

            entity.IsDefault = true;
            entity.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await tx.CommitAsync();

            await _auditLogger.LogAsync(
                HttpContext,
                "entity.default",
                "FirmEntity",
                entity.Id,
                $"Set default entity {entity.Name}; DefaultsCleared={defaultsCleared}");

            return Ok(await MapEntityAsync(entity));
        }

        [HttpDelete("{id}")]
        [Authorize(Roles = "Admin,Partner")]
        public async Task<IActionResult> DeleteEntity(string id)
        {
            RequireTenantId();
            var entity = await FindEntityAsync(id);
            if (entity == null)
            {
                return NotFound();
            }

            await using var tx = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

            if (entity.IsActive)
            {
                var activeEntityCount = await TenantScope(_context.FirmEntities).CountAsync(e => e.IsActive);
                if (activeEntityCount <= 1)
                {
                    return Conflict(new { message = "Cannot archive the last active entity." });
                }
            }

            var promotedEntityId = string.Empty;
            if (entity.IsDefault)
            {
                var fallback = await FindActiveEntityFallbackAsync(entity.Id);
                if (fallback == null)
                {
                    return Conflict(new { message = "Cannot archive the last active default entity." });
                }

                fallback.IsDefault = true;
                fallback.UpdatedAt = DateTime.UtcNow;
                promotedEntityId = fallback.Id;
            }

            var linkedOffices = await TenantScope(_context.Offices)
                .Where(o => o.EntityId == id)
                .ToListAsync();

            foreach (var office in linkedOffices)
            {
                office.IsActive = false;
                office.IsDefault = false;
                office.UpdatedAt = DateTime.UtcNow;
            }

            entity.IsActive = false;
            entity.IsDefault = false;
            entity.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await tx.CommitAsync();

            await _auditLogger.LogAsync(
                HttpContext,
                "entity.archive",
                "FirmEntity",
                entity.Id,
                $"Archived entity {entity.Name}; Promoted={promotedEntityId}; OfficesArchived={linkedOffices.Count}");

            return NoContent();
        }

        [HttpGet("{id}/offices")]
        public async Task<IActionResult> GetOffices(string id, [FromQuery] int limit = DefaultListLimit)
        {
            RequireTenantId();
            var normalizedLimit = NormalizeLimit(limit);

            var entityExists = await TenantScope(_context.FirmEntities).AnyAsync(e => e.Id == id);
            if (!entityExists)
            {
                return NotFound();
            }

            var offices = await TenantScope(_context.Offices)
                .AsNoTracking()
                .Where(o => o.EntityId == id)
                .OrderByDescending(o => o.IsDefault)
                .ThenByDescending(o => o.IsActive)
                .ThenBy(o => o.Name)
                .Take(normalizedLimit)
                .Select(o => new OfficeResponseDto
                {
                    Id = o.Id,
                    EntityId = o.EntityId,
                    Name = o.Name,
                    Code = o.Code,
                    Email = o.Email,
                    Phone = o.Phone,
                    Address = o.Address,
                    City = o.City,
                    State = o.State,
                    ZipCode = o.ZipCode,
                    Country = o.Country,
                    TimeZone = o.TimeZone,
                    IsDefault = o.IsDefault,
                    IsActive = o.IsActive,
                    CreatedAt = o.CreatedAt,
                    UpdatedAt = o.UpdatedAt
                })
                .ToListAsync();

            return Ok(offices);
        }

        [HttpPost("{id}/offices")]
        [Authorize(Roles = "Admin,Partner")]
        public async Task<IActionResult> CreateOffice(string id, [FromBody] OfficeDto dto)
        {
            RequireTenantId();
            if (dto == null)
            {
                return BadRequest(new { message = "Request body is required." });
            }

            var entity = await FindEntityAsync(id);
            if (entity == null)
            {
                return NotFound();
            }

            if (!entity.IsActive)
            {
                return BadRequest(new { message = "Cannot create an office under an inactive entity." });
            }

            var validationError = ValidateOfficeDto(dto, requireName: true);
            if (validationError != null)
            {
                return validationError;
            }

            var normalizedName = dto.Name!.Trim();
            if (await HasOfficeNameConflictAsync(id, normalizedName))
            {
                return Conflict(new { message = "An office with this name already exists for this entity." });
            }

            var normalizedCode = NormalizeOptional(dto.Code);
            if (!string.IsNullOrWhiteSpace(normalizedCode) && await HasOfficeCodeConflictAsync(id, normalizedCode))
            {
                return Conflict(new { message = "An office with this code already exists for this entity." });
            }

            if (dto.IsDefault == true && dto.IsActive == false)
            {
                return BadRequest(new { message = "An inactive office cannot be the default office." });
            }

            await using var tx = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

            var hasActiveDefault = await TenantScope(_context.Offices).AnyAsync(o => o.EntityId == id && o.IsDefault && o.IsActive);
            var office = new Office
            {
                EntityId = id,
                Name = normalizedName,
                Code = normalizedCode,
                Email = NormalizeEmail(dto.Email),
                Phone = NormalizeOptional(dto.Phone),
                Address = NormalizeOptional(dto.Address),
                City = NormalizeOptional(dto.City),
                State = NormalizeOptional(dto.State),
                ZipCode = NormalizeOptional(dto.ZipCode),
                Country = NormalizeOptional(dto.Country),
                TimeZone = NormalizeOptional(dto.TimeZone),
                IsActive = dto.IsActive ?? true,
                IsDefault = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            office.IsDefault = office.IsActive && (dto.IsDefault ?? !hasActiveDefault);

            var defaultsCleared = 0;
            if (office.IsDefault)
            {
                defaultsCleared = await ClearOfficeDefaultsAsync(id, null);
            }

            _context.Offices.Add(office);
            await _context.SaveChangesAsync();
            await tx.CommitAsync();

            await _auditLogger.LogAsync(
                HttpContext,
                "office.create",
                "Office",
                office.Id,
                $"Created office {office.Name} ({entity.Name}); Default={office.IsDefault}; DefaultsCleared={defaultsCleared}");

            return Created($"/api/entities/{id}/offices/{office.Id}", MapOffice(office));
        }

        [HttpPut("{id}/offices/{officeId}")]
        [Authorize(Roles = "Admin,Partner")]
        public async Task<IActionResult> UpdateOffice(string id, string officeId, [FromBody] OfficeDto dto)
        {
            RequireTenantId();
            if (dto == null)
            {
                return BadRequest(new { message = "Request body is required." });
            }

            var entity = await FindEntityAsync(id, track: false);
            if (entity == null)
            {
                return NotFound();
            }

            var office = await FindOfficeAsync(id, officeId);
            if (office == null)
            {
                return NotFound();
            }

            var validationError = ValidateOfficeDto(dto, requireName: false);
            if (validationError != null)
            {
                return validationError;
            }

            if (dto.Name != null)
            {
                var normalizedName = dto.Name.Trim();
                if (!string.Equals(office.Name, normalizedName, StringComparison.OrdinalIgnoreCase)
                    && await HasOfficeNameConflictAsync(id, normalizedName, office.Id))
                {
                    return Conflict(new { message = "An office with this name already exists for this entity." });
                }
                office.Name = normalizedName;
            }

            var normalizedCode = dto.Code != null ? NormalizeOptional(dto.Code) : office.Code;
            if (!string.IsNullOrWhiteSpace(normalizedCode)
                && !string.Equals(office.Code, normalizedCode, StringComparison.OrdinalIgnoreCase)
                && await HasOfficeCodeConflictAsync(id, normalizedCode, office.Id))
            {
                return Conflict(new { message = "An office with this code already exists for this entity." });
            }

            await using var tx = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

            var wantsActive = dto.IsActive ?? office.IsActive;
            if (dto.IsDefault == true && !wantsActive)
            {
                return BadRequest(new { message = "An inactive office cannot be the default office." });
            }

            if (!wantsActive && office.IsActive)
            {
                var activeOfficeCount = await TenantScope(_context.Offices)
                    .CountAsync(o => o.EntityId == id && o.IsActive);
                if (activeOfficeCount <= 1)
                {
                    return Conflict(new { message = "At least one active office must remain for this entity." });
                }
            }

            office.Code = normalizedCode;
            office.Email = dto.Email != null ? NormalizeEmail(dto.Email) : office.Email;
            office.Phone = dto.Phone != null ? NormalizeOptional(dto.Phone) : office.Phone;
            office.Address = dto.Address != null ? NormalizeOptional(dto.Address) : office.Address;
            office.City = dto.City != null ? NormalizeOptional(dto.City) : office.City;
            office.State = dto.State != null ? NormalizeOptional(dto.State) : office.State;
            office.ZipCode = dto.ZipCode != null ? NormalizeOptional(dto.ZipCode) : office.ZipCode;
            office.Country = dto.Country != null ? NormalizeOptional(dto.Country) : office.Country;
            office.TimeZone = dto.TimeZone != null ? NormalizeOptional(dto.TimeZone) : office.TimeZone;

            var defaultsCleared = 0;
            var promotedOfficeId = string.Empty;

            if (dto.IsDefault == true)
            {
                defaultsCleared = await ClearOfficeDefaultsAsync(id, office.Id);
                office.IsDefault = true;
            }
            else if (office.IsDefault && (dto.IsDefault == false || !wantsActive))
            {
                var fallback = await FindActiveOfficeFallbackAsync(id, office.Id);
                if (fallback == null)
                {
                    return Conflict(new { message = "At least one active default office must remain." });
                }

                fallback.IsDefault = true;
                fallback.UpdatedAt = DateTime.UtcNow;
                office.IsDefault = false;
                promotedOfficeId = fallback.Id;
            }

            office.IsActive = wantsActive;

            if (office.IsActive && !office.IsDefault)
            {
                var hasOtherActiveDefault = await TenantScope(_context.Offices)
                    .AnyAsync(o => o.EntityId == id && o.Id != office.Id && o.IsDefault && o.IsActive);
                if (!hasOtherActiveDefault && entity.IsActive)
                {
                    office.IsDefault = true;
                }
            }

            office.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            await tx.CommitAsync();

            await _auditLogger.LogAsync(
                HttpContext,
                "office.update",
                "Office",
                office.Id,
                $"Updated office {office.Name}; Default={office.IsDefault}; Active={office.IsActive}; DefaultsCleared={defaultsCleared}; Promoted={promotedOfficeId}");

            return Ok(MapOffice(office));
        }

        [HttpPost("{id}/offices/{officeId}/default")]
        [Authorize(Roles = "Admin,Partner")]
        public async Task<IActionResult> SetDefaultOffice(string id, string officeId)
        {
            RequireTenantId();
            var office = await FindOfficeAsync(id, officeId);
            if (office == null)
            {
                return NotFound();
            }

            if (!office.IsActive)
            {
                return BadRequest(new { message = "Inactive offices cannot be set as default." });
            }

            await using var tx = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var defaultsCleared = await ClearOfficeDefaultsAsync(id, office.Id);

            office.IsDefault = true;
            office.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await tx.CommitAsync();

            await _auditLogger.LogAsync(
                HttpContext,
                "office.default",
                "Office",
                office.Id,
                $"Set default office {office.Name}; DefaultsCleared={defaultsCleared}");

            return Ok(MapOffice(office));
        }

        [HttpDelete("{id}/offices/{officeId}")]
        [Authorize(Roles = "Admin,Partner")]
        public async Task<IActionResult> DeleteOffice(string id, string officeId)
        {
            RequireTenantId();
            var office = await FindOfficeAsync(id, officeId);
            if (office == null)
            {
                return NotFound();
            }

            await using var tx = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

            if (office.IsActive)
            {
                var activeOfficeCount = await TenantScope(_context.Offices)
                    .CountAsync(o => o.EntityId == id && o.IsActive);
                if (activeOfficeCount <= 1)
                {
                    return Conflict(new { message = "Cannot archive the last active office for this entity." });
                }
            }

            var promotedOfficeId = string.Empty;
            if (office.IsDefault)
            {
                var fallback = await FindActiveOfficeFallbackAsync(id, office.Id);
                if (fallback == null)
                {
                    return Conflict(new { message = "Cannot archive the last active default office." });
                }

                fallback.IsDefault = true;
                fallback.UpdatedAt = DateTime.UtcNow;
                promotedOfficeId = fallback.Id;
            }

            office.IsActive = false;
            office.IsDefault = false;
            office.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await tx.CommitAsync();

            await _auditLogger.LogAsync(
                HttpContext,
                "office.archive",
                "Office",
                office.Id,
                $"Archived office {office.Name}; Promoted={promotedOfficeId}");

            return NoContent();
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

        private async Task<FirmEntity?> FindEntityAsync(string id, bool track = true)
        {
            var query = TenantScope(_context.FirmEntities);
            if (!track)
            {
                query = query.AsNoTracking();
            }

            return await query.FirstOrDefaultAsync(e => e.Id == id);
        }

        private async Task<Office?> FindOfficeAsync(string entityId, string officeId)
        {
            return await TenantScope(_context.Offices)
                .FirstOrDefaultAsync(o => o.Id == officeId && o.EntityId == entityId);
        }

        private async Task<bool> HasEntityNameConflictAsync(string name, string? excludeId = null)
        {
            var normalized = name.Trim().ToUpperInvariant();
            return await TenantScope(_context.FirmEntities)
                .AnyAsync(e => e.Id != excludeId && e.Name.ToUpper() == normalized);
        }

        private async Task<bool> HasOfficeNameConflictAsync(string entityId, string name, string? excludeId = null)
        {
            var normalized = name.Trim().ToUpperInvariant();
            return await TenantScope(_context.Offices)
                .AnyAsync(o => o.EntityId == entityId && o.Id != excludeId && o.Name.ToUpper() == normalized);
        }

        private async Task<bool> HasOfficeCodeConflictAsync(string entityId, string code, string? excludeId = null)
        {
            var normalized = code.Trim().ToUpperInvariant();
            return await TenantScope(_context.Offices)
                .AnyAsync(o => o.EntityId == entityId
                    && o.Id != excludeId
                    && o.Code != null
                    && o.Code.ToUpper() == normalized);
        }

        private async Task<int> ClearEntityDefaultsAsync(string? exceptId)
        {
            var defaults = await TenantScope(_context.FirmEntities)
                .Where(e => e.IsDefault && e.Id != exceptId)
                .ToListAsync();

            foreach (var existing in defaults)
            {
                existing.IsDefault = false;
                existing.UpdatedAt = DateTime.UtcNow;
            }

            return defaults.Count;
        }

        private async Task<int> ClearOfficeDefaultsAsync(string entityId, string? exceptId)
        {
            var defaults = await TenantScope(_context.Offices)
                .Where(o => o.EntityId == entityId && o.IsDefault && o.Id != exceptId)
                .ToListAsync();

            foreach (var existing in defaults)
            {
                existing.IsDefault = false;
                existing.UpdatedAt = DateTime.UtcNow;
            }

            return defaults.Count;
        }

        private async Task<FirmEntity?> FindActiveEntityFallbackAsync(string excludedId)
        {
            return await TenantScope(_context.FirmEntities)
                .Where(e => e.Id != excludedId && e.IsActive)
                .OrderByDescending(e => e.IsDefault)
                .ThenBy(e => e.Name)
                .FirstOrDefaultAsync();
        }

        private async Task<Office?> FindActiveOfficeFallbackAsync(string entityId, string excludedId)
        {
            return await TenantScope(_context.Offices)
                .Where(o => o.EntityId == entityId && o.Id != excludedId && o.IsActive)
                .OrderByDescending(o => o.IsDefault)
                .ThenBy(o => o.Name)
                .FirstOrDefaultAsync();
        }

        private async Task<EntityResponseDto> MapEntityAsync(FirmEntity entity)
        {
            var officeCount = await TenantScope(_context.Offices).CountAsync(o => o.EntityId == entity.Id);
            return new EntityResponseDto
            {
                Id = entity.Id,
                Name = entity.Name,
                LegalName = entity.LegalName,
                TaxId = entity.TaxId,
                Email = entity.Email,
                Phone = entity.Phone,
                Website = entity.Website,
                Address = entity.Address,
                City = entity.City,
                State = entity.State,
                ZipCode = entity.ZipCode,
                Country = entity.Country,
                IsDefault = entity.IsDefault,
                IsActive = entity.IsActive,
                CreatedAt = entity.CreatedAt,
                UpdatedAt = entity.UpdatedAt,
                OfficeCount = officeCount
            };
        }

        private static OfficeResponseDto MapOffice(Office office)
        {
            return new OfficeResponseDto
            {
                Id = office.Id,
                EntityId = office.EntityId,
                Name = office.Name,
                Code = office.Code,
                Email = office.Email,
                Phone = office.Phone,
                Address = office.Address,
                City = office.City,
                State = office.State,
                ZipCode = office.ZipCode,
                Country = office.Country,
                TimeZone = office.TimeZone,
                IsDefault = office.IsDefault,
                IsActive = office.IsActive,
                CreatedAt = office.CreatedAt,
                UpdatedAt = office.UpdatedAt
            };
        }

        private IActionResult? ValidateEntityDto(EntityDto dto, bool requireName)
        {
            if (requireName && string.IsNullOrWhiteSpace(dto.Name))
            {
                return BadRequest(new { message = "Entity name is required." });
            }

            if (dto.Name != null && string.IsNullOrWhiteSpace(dto.Name))
            {
                return BadRequest(new { message = "Entity name is required." });
            }

            if (!IsValidOptionalEmail(dto.Email))
            {
                return BadRequest(new { message = "A valid email address is required." });
            }

            if (!IsValidOptionalWebsite(dto.Website))
            {
                return BadRequest(new { message = "A valid website URL is required." });
            }

            if (!IsValidOptionalTaxId(dto.TaxId))
            {
                return BadRequest(new { message = "Tax ID format is invalid." });
            }

            return null;
        }

        private IActionResult? ValidateOfficeDto(OfficeDto dto, bool requireName)
        {
            if (requireName && string.IsNullOrWhiteSpace(dto.Name))
            {
                return BadRequest(new { message = "Office name is required." });
            }

            if (dto.Name != null && string.IsNullOrWhiteSpace(dto.Name))
            {
                return BadRequest(new { message = "Office name is required." });
            }

            if (!IsValidOptionalEmail(dto.Email))
            {
                return BadRequest(new { message = "A valid email address is required." });
            }

            if (!string.IsNullOrWhiteSpace(dto.Code) && dto.Code.Trim().Length > 32)
            {
                return BadRequest(new { message = "Office code must be 32 characters or fewer." });
            }

            return null;
        }

        private static int NormalizeLimit(int limit)
        {
            if (limit <= 0)
            {
                return DefaultListLimit;
            }

            return Math.Clamp(limit, 1, MaxListLimit);
        }

        private static string? NormalizeOptional(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static string? NormalizeEmail(string? value)
        {
            var trimmed = NormalizeOptional(value);
            return trimmed?.ToLowerInvariant();
        }

        private static string? NormalizeWebsite(string? value)
        {
            return NormalizeOptional(value);
        }

        private static bool IsValidOptionalEmail(string? value)
        {
            var normalized = NormalizeOptional(value);
            return normalized == null || EmailValidator.IsValid(normalized);
        }

        private static bool IsValidOptionalWebsite(string? value)
        {
            var normalized = NormalizeOptional(value);
            if (normalized == null)
            {
                return true;
            }

            return Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        }

        private static bool IsValidOptionalTaxId(string? value)
        {
            var normalized = NormalizeOptional(value);
            if (normalized == null)
            {
                return true;
            }

            if (normalized.Length < 5 || normalized.Length > 32)
            {
                return false;
            }

            return normalized.All(ch => char.IsLetterOrDigit(ch) || ch == '-' || ch == '/' || ch == ' ');
        }
    }

    public class EntityDto
    {
        public string? Name { get; set; }
        public string? LegalName { get; set; }
        public string? TaxId { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Website { get; set; }
        public string? Address { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public string? ZipCode { get; set; }
        public string? Country { get; set; }
        public bool? IsDefault { get; set; }
        public bool? IsActive { get; set; }
    }

    public class OfficeDto
    {
        public string? Name { get; set; }
        public string? Code { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Address { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public string? ZipCode { get; set; }
        public string? Country { get; set; }
        public string? TimeZone { get; set; }
        public bool? IsDefault { get; set; }
        public bool? IsActive { get; set; }
    }

    public class EntityResponseDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? LegalName { get; set; }
        public string? TaxId { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Website { get; set; }
        public string? Address { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public string? ZipCode { get; set; }
        public string? Country { get; set; }
        public bool IsDefault { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public int OfficeCount { get; set; }
    }

    public class OfficeResponseDto
    {
        public string Id { get; set; } = string.Empty;
        public string EntityId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Code { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Address { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public string? ZipCode { get; set; }
        public string? Country { get; set; }
        public string? TimeZone { get; set; }
        public bool IsDefault { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
