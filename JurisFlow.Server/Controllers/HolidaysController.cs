using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using JurisFlow.Server.Services;
using Microsoft.Extensions.Caching.Memory;

namespace JurisFlow.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Policy = "StaffOnly")]
    public class HolidaysController : ControllerBase
    {
        private readonly JurisFlowDbContext _context;
        private readonly IMemoryCache _cache;
        private readonly TenantContext _tenantContext;
        private const string CacheVersionKey = "holidays:version";

        public HolidaysController(JurisFlowDbContext context, IMemoryCache cache, TenantContext tenantContext)
        {
            _context = context;
            _cache = cache;
            _tenantContext = tenantContext;
        }

        [HttpGet]
        public async Task<IActionResult> GetHolidays([FromQuery] string? jurisdiction = null)
        {
            var normalized = JurisdictionCalendar.Normalize(jurisdiction);
            var tenantKey = GetTenantKey();
            var cacheKey = $"holidays:list:{tenantKey}:{GetCacheVersion(tenantKey)}:{NormalizeCacheKey(normalized)}";
            if (_cache.TryGetValue(cacheKey, out List<Holiday>? cached) && cached != null)
            {
                return Ok(cached);
            }

            var query = _context.Holidays.AsNoTracking().AsQueryable();
            if (!string.IsNullOrEmpty(normalized))
            {
                query = query.Where(h => h.Jurisdiction == normalized || h.Jurisdiction == "US-Federal");
            }
            else
            {
                query = query.Where(h => h.Jurisdiction == "US-Federal");
            }

            var items = await query.OrderBy(h => h.Date).ToListAsync();
            _cache.Set(cacheKey, items, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(6)
            });
            return Ok(items);
        }

        [HttpPost]
        public async Task<IActionResult> CreateHoliday([FromBody] Holiday dto)
        {
            dto.Id = Guid.NewGuid().ToString();
            dto.CreatedAt = DateTime.UtcNow;
            dto.UpdatedAt = DateTime.UtcNow;

            _context.Holidays.Add(dto);
            await _context.SaveChangesAsync();
            BumpCacheVersion();
            return Ok(dto);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteHoliday(string id)
        {
            var holiday = await _context.Holidays.FindAsync(id);
            if (holiday == null) return NotFound();

            _context.Holidays.Remove(holiday);
            await _context.SaveChangesAsync();
            BumpCacheVersion();
            return NoContent();
        }

        private string GetTenantKey()
        {
            return string.IsNullOrWhiteSpace(_tenantContext.TenantId) ? "default" : _tenantContext.TenantId;
        }

        private int GetCacheVersion(string tenantKey)
        {
            var versionKey = $"{CacheVersionKey}:{tenantKey}";
            if (_cache.TryGetValue(versionKey, out int version))
            {
                return version;
            }
            _cache.Set(versionKey, 0);
            return 0;
        }

        private void BumpCacheVersion()
        {
            var tenantKey = GetTenantKey();
            var versionKey = $"{CacheVersionKey}:{tenantKey}";
            var next = GetCacheVersion(tenantKey) + 1;
            _cache.Set(versionKey, next);
        }

        private static string NormalizeCacheKey(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
        }
    }
}
