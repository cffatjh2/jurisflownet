using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;
using System.Text.RegularExpressions;
using JurisFlow.Server.Services;

namespace JurisFlow.Server.Controllers
{
    [Route("api/admin/backups")]
    [ApiController]
    [Authorize(Policy = "SecurityAdminOnly")]
    public class BackupsController : ControllerBase
    {
        private static readonly Regex SafeBackupFileNamePattern = new("^[a-zA-Z0-9._-]+$", RegexOptions.Compiled);
        private static readonly HashSet<string> AllowedBackupExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".zip",
            ".enc"
        };

        private readonly BackupService _backupService;
        private readonly BackupJobQueue _backupJobQueue;
        private readonly AuditLogger _auditLogger;
        private readonly ILogger<BackupsController> _logger;
        private readonly TenantContext _tenantContext;

        public BackupsController(
            BackupService backupService,
            BackupJobQueue backupJobQueue,
            AuditLogger auditLogger,
            ILogger<BackupsController> logger,
            TenantContext tenantContext)
        {
            _backupService = backupService;
            _backupJobQueue = backupJobQueue;
            _auditLogger = auditLogger;
            _logger = logger;
            _tenantContext = tenantContext;
        }

        [HttpGet]
        public IActionResult ListBackups()
        {
            try
            {
                var backups = _backupService.ListBackups();
                return Ok(backups);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Backup listing failed.");
                return BadRequest(new { message = "Backups are currently unavailable." });
            }
        }

        [EnableRateLimiting("AdminDangerousOps")]
        [HttpPost]
        public async Task<IActionResult> CreateBackup([FromBody] BackupCreateDto? dto)
        {
            if (string.IsNullOrWhiteSpace(_tenantContext.TenantId))
            {
                return BadRequest(new { message = "Tenant is required." });
            }

            try
            {
                var includeUploads = dto?.IncludeUploads ?? false;
                var job = _backupJobQueue.EnqueueCreate(BuildEnqueueContext(), includeUploads);
                await _auditLogger.LogAsync(HttpContext, "backup.create.queued", "BackupJob", job.JobId, $"Uploads={includeUploads}");
                return AcceptedAtAction(nameof(GetBackupJobStatus), new { jobId = job.JobId }, job);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Backup creation failed.");
                return BadRequest(new { message = "Backup could not be created." });
            }
        }

        [EnableRateLimiting("AdminDangerousOps")]
        [HttpGet("{fileName}")]
        public IActionResult DownloadBackup(string fileName)
        {
            if (!TryNormalizeBackupFileName(fileName, out var safeName))
            {
                return BadRequest(new { message = "Invalid backup file requested." });
            }

            try
            {
                if (!_backupService.ListBackups().Any(b => string.Equals(b.FileName, safeName, StringComparison.Ordinal)))
                {
                    return NotFound(new { message = "Backup file not found." });
                }

                var path = _backupService.ResolveBackupPath(safeName);
                if (!System.IO.File.Exists(path))
                {
                    return NotFound(new { message = "Backup file not found." });
                }

                Response.Headers.CacheControl = "no-store, no-cache, max-age=0";
                Response.Headers.Pragma = "no-cache";
                Response.Headers.Expires = "0";
                Response.Headers["X-Content-Type-Options"] = "nosniff";

                return PhysicalFile(path, "application/octet-stream", safeName, enableRangeProcessing: false);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Backup download failed for {FileName}.", safeName);
                return BadRequest(new { message = "Backup file could not be downloaded." });
            }
        }

        [EnableRateLimiting("AdminDangerousOps")]
        [HttpPost("restore")]
        public async Task<IActionResult> RestoreBackup([FromBody] BackupRestoreDto? dto)
        {
            if (string.IsNullOrWhiteSpace(_tenantContext.TenantId))
            {
                return BadRequest(new { message = "Tenant is required." });
            }

            if (dto == null)
            {
                return BadRequest(new { message = "Request body is required." });
            }

            if (string.IsNullOrWhiteSpace(dto.FileName))
            {
                return BadRequest(new { message = "FileName is required." });
            }

            if (!TryNormalizeBackupFileName(dto.FileName, out var safeName))
            {
                return BadRequest(new { message = "Invalid backup file requested." });
            }

            if (!dto.DryRun && !HasBreakGlassConfirmation())
            {
                return BadRequest(new { message = "Break-glass confirmation is required for restore execution." });
            }

            try
            {
                if (!_backupService.ListBackups().Any(b => string.Equals(b.FileName, safeName, StringComparison.Ordinal)))
                {
                    return NotFound(new { message = "Backup file not found." });
                }

                var job = _backupJobQueue.EnqueueRestore(BuildEnqueueContext(), safeName, dto.IncludeUploads, dto.DryRun);
                await _auditLogger.LogAsync(HttpContext, "backup.restore.queued", "BackupJob", job.JobId, $"DryRun={dto.DryRun}, Uploads={dto.IncludeUploads}; FileName={safeName}");
                return AcceptedAtAction(nameof(GetBackupJobStatus), new { jobId = job.JobId }, job);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Backup restore failed for {FileName}. DryRun={DryRun}", safeName, dto.DryRun);
                return BadRequest(new { message = "Backup restore could not be completed." });
            }
        }

        [HttpGet("jobs/{jobId}")]
        public IActionResult GetBackupJobStatus(string jobId)
        {
            if (string.IsNullOrWhiteSpace(_tenantContext.TenantId))
            {
                return BadRequest(new { message = "Tenant is required." });
            }

            if (string.IsNullOrWhiteSpace(jobId))
            {
                return BadRequest(new { message = "JobId is required." });
            }

            if (!_backupJobQueue.TryGet(jobId, _tenantContext.TenantId!, out var job))
            {
                return NotFound(new { message = "Backup job not found." });
            }

            return Ok(job);
        }

        private static bool TryNormalizeBackupFileName(string fileName, out string safeName)
        {
            safeName = Path.GetFileName(fileName);
            if (!string.Equals(fileName, safeName, StringComparison.Ordinal))
            {
                return false;
            }

            if (!SafeBackupFileNamePattern.IsMatch(safeName))
            {
                return false;
            }

            var extension = Path.GetExtension(safeName);
            return !string.IsNullOrWhiteSpace(extension) && AllowedBackupExtensions.Contains(extension);
        }

        private bool HasBreakGlassConfirmation()
        {
            var value = Request.Headers["X-Break-Glass-Confirm"].FirstOrDefault();
            return string.Equals(value, "RESTORE", StringComparison.Ordinal);
        }

        private BackupJobEnqueueContext BuildEnqueueContext()
        {
            return new BackupJobEnqueueContext
            {
                TenantId = _tenantContext.TenantId ?? string.Empty,
                TenantSlug = _tenantContext.TenantSlug,
                RequestedByUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value,
                RequestedByRole = User.FindFirst(ClaimTypes.Role)?.Value ?? User.FindFirst("role")?.Value,
                CorrelationId = HttpContext.TraceIdentifier
            };
        }
    }

    public class BackupCreateDto
    {
        public bool IncludeUploads { get; set; } = false;
    }

    public class BackupRestoreDto
    {
        public string FileName { get; set; } = string.Empty;
        public bool IncludeUploads { get; set; } = true;
        public bool DryRun { get; set; } = true;
    }
}
