using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using JurisFlow.Server.Services;
using System.IO;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Linq;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using Task = System.Threading.Tasks.Task;

namespace JurisFlow.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Policy = "StaffOnly")]
    public class DocumentsController : ControllerBase
    {
        private const int MaxDocumentResults = 200;
        private static readonly HashSet<string> AllowedDocumentStatuses = new(StringComparer.OrdinalIgnoreCase)
        {
            "Draft",
            "Final",
            "Archived",
            "Legal Hold"
        };

        private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf", ".doc", ".docx", ".txt", ".rtf", ".png", ".jpg", ".jpeg", ".xlsx", ".xls", ".ppt", ".pptx", ".csv"
        };

        private readonly JurisFlowDbContext _context;
        private readonly IWebHostEnvironment _env;
        private readonly AuditLogger _auditLogger;
        private readonly DocumentIndexService _documentIndexService;
        private readonly DocumentEncryptionService _documentEncryptionService;
        private readonly DocumentTextExtractor _textExtractor;
        private readonly TenantContext _tenantContext;
        private readonly ILogger<DocumentsController> _logger;

        public DocumentsController(
            JurisFlowDbContext context,
            IWebHostEnvironment env,
            AuditLogger auditLogger,
            DocumentIndexService documentIndexService,
            DocumentEncryptionService documentEncryptionService,
            DocumentTextExtractor textExtractor,
            TenantContext tenantContext,
            ILogger<DocumentsController> logger)
        {
            _context = context;
            _env = env;
            _auditLogger = auditLogger;
            _documentIndexService = documentIndexService;
            _documentEncryptionService = documentEncryptionService;
            _textExtractor = textExtractor;
            _tenantContext = tenantContext;
            _logger = logger;
        }

        private static string ComputeSha256(string filePath)
        {
            using var sha = SHA256.Create();
            using var stream = System.IO.File.OpenRead(filePath);
            var hash = sha.ComputeHash(stream);
            var sb = new StringBuilder();
            foreach (var b in hash)
            {
                sb.Append(b.ToString("x2"));
            }
            return sb.ToString();
        }

        private static string ComputeSha256(Stream stream)
        {
            stream.Position = 0;
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(stream);
            var sb = new StringBuilder();
            foreach (var b in hash)
            {
                sb.Append(b.ToString("x2"));
            }
            stream.Position = 0;
            return sb.ToString();
        }

        private static string? SerializeTags(List<string>? tags)
        {
            if (tags == null || tags.Count == 0) return null;
            return JsonSerializer.Serialize(tags);
        }

        private static bool IsLegalHoldStatus(string? status)
        {
            return string.Equals(status, "Legal Hold", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasRestrictedUpdates(DocumentUpdateDto dto)
        {
            return dto.MatterId.HasValue || dto.Description != null || dto.Category != null || dto.Tags.HasValue;
        }

        private string RequireTenantId()
        {
            if (string.IsNullOrWhiteSpace(_tenantContext.TenantId))
            {
                throw new InvalidOperationException("Tenant context is missing.");
            }
            return _tenantContext.TenantId;
        }

        private string GetTenantUploadsFolder()
        {
            var tenantId = RequireTenantId();
            var uploadsFolder = Path.Combine(_env.ContentRootPath, "uploads", tenantId);
            if (!Directory.Exists(uploadsFolder))
            {
                Directory.CreateDirectory(uploadsFolder);
            }
            return uploadsFolder;
        }

        private string GetTenantRelativePath(string fileName)
        {
            var tenantId = RequireTenantId();
            return $"uploads/{tenantId}/{fileName}";
        }

        private string GetTenantRelativePrefix()
        {
            return $"uploads/{RequireTenantId()}/";
        }

        private string GetTenantUploadsRootFullPath()
        {
            return Path.GetFullPath(GetTenantUploadsFolder());
        }

        private string ResolveTenantStoredFilePath(string relativePath)
        {
            var normalizedRelativePath = relativePath
                .Replace(Path.DirectorySeparatorChar, '/')
                .Replace(Path.AltDirectorySeparatorChar, '/')
                .TrimStart('/');

            var expectedPrefix = GetTenantRelativePrefix();
            if (!normalizedRelativePath.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Stored file path is outside the tenant upload root.");
            }

            var fullPath = Path.GetFullPath(Path.Combine(_env.ContentRootPath, normalizedRelativePath));
            var tenantRoot = GetTenantUploadsRootFullPath();
            var tenantBoundary = tenantRoot.EndsWith(Path.DirectorySeparatorChar)
                ? tenantRoot
                : tenantRoot + Path.DirectorySeparatorChar;

            if (!fullPath.StartsWith(tenantBoundary, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(fullPath, tenantRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Resolved file path is outside the tenant upload root.");
            }

            return fullPath;
        }

        private IQueryable<T> TenantScope<T>(IQueryable<T> query) where T : class
        {
            var tenantId = RequireTenantId();
            return query.Where(e => EF.Property<string>(e, "TenantId") == tenantId);
        }

        private async Task<Document?> FindDocumentAsync(string id, bool asNoTracking = false)
        {
            var query = TenantScope(_context.Documents);
            if (asNoTracking)
            {
                query = query.AsNoTracking();
            }

            return await query.FirstOrDefaultAsync(d => d.Id == id);
        }

        private async Task<DocumentVersionLookup?> FindDocumentVersionAsync(string versionId)
        {
            var version = await _context.DocumentVersions.FirstOrDefaultAsync(v => v.Id == versionId);
            if (version == null)
            {
                return null;
            }

            var document = await FindDocumentAsync(version.DocumentId);
            if (document == null)
            {
                return null;
            }

            return new DocumentVersionLookup(document, version);
        }

        private async Task EnsureMatterExistsAsync(string? matterId)
        {
            var normalizedMatterId = NormalizeOptionalText(matterId, 100);
            if (string.IsNullOrWhiteSpace(normalizedMatterId))
            {
                return;
            }

            var exists = await TenantScope(_context.Matters)
                .AsNoTracking()
                .AnyAsync(m => m.Id == normalizedMatterId);

            if (!exists)
            {
                throw new InvalidOperationException("Matter not found.");
            }
        }

        private static string? NormalizeOptionalText(string? value, int maxLength)
        {
            if (value == null)
            {
                return null;
            }

            var normalized = value.Trim();
            if (normalized.Length == 0)
            {
                return null;
            }

            if (normalized.Length > maxLength)
            {
                throw new InvalidOperationException($"Value exceeds maximum length of {maxLength}.");
            }

            return normalized;
        }

        private static string NormalizeRequiredText(string? value, string fieldName, int maxLength)
        {
            return NormalizeOptionalText(value, maxLength)
                ?? throw new InvalidOperationException($"{fieldName} is required.");
        }

        private static string? NormalizeAllowedOptional(string? value, HashSet<string> allowed, string fieldName)
        {
            var normalized = NormalizeOptionalText(value, 100);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return null;
            }

            var match = allowed.FirstOrDefault(item => string.Equals(item, normalized, StringComparison.OrdinalIgnoreCase));
            if (match == null)
            {
                throw new InvalidOperationException($"Unsupported {fieldName} value '{normalized}'.");
            }

            return match;
        }

        private static string EscapeLikePattern(string input)
        {
            return input
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("%", "\\%", StringComparison.Ordinal)
                .Replace("_", "\\_", StringComparison.Ordinal)
                .Replace("[", "\\[", StringComparison.Ordinal);
        }

        private static string ComputeQueryHash(string input)
        {
            using var sha = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(input);
            var hash = sha.ComputeHash(bytes);
            return Convert.ToHexString(hash);
        }

        private static IReadOnlyList<string> DeserializeTags(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return Array.Empty<string>();
            }

            try
            {
                var tags = JsonSerializer.Deserialize<List<string>>(raw);
                return tags?
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Select(t => t.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
                    ?? new List<string>();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        private string BuildDownloadUrl(string documentId)
        {
            return $"/api/Documents/{documentId}/download";
        }

        private string BuildVersionDownloadUrl(string versionId)
        {
            return $"/api/Documents/versions/{versionId}/download";
        }

        private DocumentResponseDto ToDocumentResponse(Document document)
        {
            return new DocumentResponseDto
            {
                Id = document.Id,
                Name = document.Name,
                FileName = document.FileName,
                DownloadUrl = BuildDownloadUrl(document.Id),
                FileSize = document.FileSize,
                MimeType = document.MimeType,
                MatterId = document.MatterId,
                UploadedBy = document.UploadedBy,
                Version = document.Version,
                Category = document.Category,
                Description = document.Description,
                Tags = DeserializeTags(document.Tags),
                Status = document.Status,
                LegalHoldReason = document.LegalHoldReason,
                LegalHoldPlacedAt = document.LegalHoldPlacedAt,
                LegalHoldPlacedBy = document.LegalHoldPlacedBy,
                LegalHoldReleasedAt = document.LegalHoldReleasedAt,
                LegalHoldReleasedBy = document.LegalHoldReleasedBy,
                CreatedAt = document.CreatedAt,
                UpdatedAt = document.UpdatedAt
            };
        }

        private DocumentVersionResponseDto ToDocumentVersionResponse(DocumentVersion version)
        {
            return new DocumentVersionResponseDto
            {
                Id = version.Id,
                DocumentId = version.DocumentId,
                FileName = version.FileName,
                DownloadUrl = BuildVersionDownloadUrl(version.Id),
                FileSize = version.FileSize,
                IsEncrypted = version.IsEncrypted,
                EncryptionAlgorithm = version.EncryptionAlgorithm,
                Sha256 = version.Sha256,
                UploadedByUserId = version.UploadedByUserId,
                CreatedAt = version.CreatedAt
            };
        }

        private async Task<IActionResult> DownloadDocumentFileAsync(
            string relativePath,
            string fileName,
            string? mimeType,
            bool isEncrypted,
            string? encryptionIv,
            string? encryptionTag)
        {
            var fullPath = ResolveTenantStoredFilePath(relativePath);
            if (!System.IO.File.Exists(fullPath))
            {
                return NotFound(new { message = "File not found" });
            }

            Response.Headers["Cache-Control"] = "no-store";
            Response.Headers["X-Content-Type-Options"] = "nosniff";

            if (isEncrypted)
            {
                if (string.IsNullOrWhiteSpace(encryptionIv) || string.IsNullOrWhiteSpace(encryptionTag))
                {
                    return BadRequest(new { message = "Encrypted file metadata is missing." });
                }

                var bytes = await _documentEncryptionService.DecryptFileAsync(fullPath, encryptionIv, encryptionTag);
                return File(bytes, mimeType ?? "application/octet-stream", fileName);
            }

            return PhysicalFile(fullPath, mimeType ?? "application/octet-stream", fileName);
        }

        private async Task<string> LoadTextAsync(DocumentVersion version)
        {
            var fullPath = ResolveTenantStoredFilePath(version.FilePath);
            if (!System.IO.File.Exists(fullPath))
            {
                return string.Empty;
            }

            if (version.IsEncrypted)
            {
                if (string.IsNullOrWhiteSpace(version.EncryptionIv) || string.IsNullOrWhiteSpace(version.EncryptionTag))
                {
                    return string.Empty;
                }

                var plaintext = await _documentEncryptionService.DecryptFileAsync(fullPath, version.EncryptionIv, version.EncryptionTag);
                return await _textExtractor.ExtractTextAsync(plaintext, version.FileName);
            }

            return await _textExtractor.ExtractTextAsync(fullPath);
        }

        private async Task<SanitizedUploadFile> ValidateAndPrepareUploadAsync(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                throw new InvalidOperationException("No file uploaded.");
            }

            var prepared = PrepareStoredFileName(file.FileName);

            var header = await ReadFileHeaderAsync(file);
            if (!MatchesExpectedSignature(prepared.Extension, header))
            {
                throw new InvalidOperationException("Uploaded file content does not match the expected file type.");
            }

            return prepared;
        }

        private static SanitizedUploadFile PrepareStoredFileName(string? rawFileName)
        {
            var displayName = Path.GetFileName(rawFileName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(displayName))
            {
                throw new InvalidOperationException("A valid file name is required.");
            }

            if (displayName.Length > 255)
            {
                throw new InvalidOperationException("File name exceeds maximum length of 255.");
            }

            var extension = Path.GetExtension(displayName);
            if (string.IsNullOrWhiteSpace(extension) || !AllowedExtensions.Contains(extension))
            {
                throw new InvalidOperationException("Unsupported file type.");
            }

            return new SanitizedUploadFile(displayName, $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}", extension.ToLowerInvariant());
        }

        private static async Task<byte[]> ReadFileHeaderAsync(IFormFile file, int maxBytes = 16)
        {
            await using var stream = file.OpenReadStream();
            var buffer = new byte[maxBytes];
            var read = await stream.ReadAsync(buffer.AsMemory(0, maxBytes));
            return buffer[..read];
        }

        private static bool MatchesExpectedSignature(string extension, byte[] header)
        {
            if (extension.Equals(".txt", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".csv", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                return header.Length >= 5 &&
                    header[0] == 0x25 && header[1] == 0x50 && header[2] == 0x44 && header[3] == 0x46 && header[4] == 0x2D;
            }

            if (extension.Equals(".png", StringComparison.OrdinalIgnoreCase))
            {
                return header.Length >= 8 &&
                    header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47 &&
                    header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A;
            }

            if (extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
            {
                return header.Length >= 3 &&
                    header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF;
            }

            if (extension.Equals(".rtf", StringComparison.OrdinalIgnoreCase))
            {
                return header.Length >= 5 &&
                    header[0] == 0x7B && header[1] == 0x5C && header[2] == 0x72 && header[3] == 0x74 && header[4] == 0x66;
            }

            var isOle = header.Length >= 8 &&
                header[0] == 0xD0 && header[1] == 0xCF && header[2] == 0x11 && header[3] == 0xE0 &&
                header[4] == 0xA1 && header[5] == 0xB1 && header[6] == 0x1A && header[7] == 0xE1;
            if (extension.Equals(".doc", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".xls", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".ppt", StringComparison.OrdinalIgnoreCase))
            {
                return isOle;
            }

            var isZipContainer = header.Length >= 4 &&
                header[0] == 0x50 && header[1] == 0x4B &&
                (header[2] == 0x03 || header[2] == 0x05 || header[2] == 0x07) &&
                (header[3] == 0x04 || header[3] == 0x06 || header[3] == 0x08);
            if (extension.Equals(".docx", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".pptx", StringComparison.OrdinalIgnoreCase))
            {
                return isZipContainer;
            }

            return false;
        }

        // GET: api/Documents
        [HttpGet]
        public async Task<ActionResult<IEnumerable<DocumentResponseDto>>> GetDocuments([FromQuery] string? matterId)
        {
            try
            {
                var normalizedMatterId = NormalizeOptionalText(matterId, 100);
                if (!string.IsNullOrWhiteSpace(normalizedMatterId))
                {
                    await EnsureMatterExistsAsync(normalizedMatterId);
                }

                var query = TenantScope(_context.Documents).AsNoTracking().AsQueryable();

                if (!string.IsNullOrEmpty(normalizedMatterId))
                {
                    query = query.Where(d => d.MatterId == normalizedMatterId);
                }

                var documents = await query
                    .OrderByDescending(d => d.CreatedAt)
                    .Take(MaxDocumentResults)
                    .ToListAsync();

                return Ok(documents.Select(ToDocumentResponse));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // GET: api/Documents/{id}/download
        [HttpGet("{id}/download")]
        public async Task<IActionResult> DownloadDocument(string id)
        {
            var document = await FindDocumentAsync(id, asNoTracking: true);
            if (document == null) return NotFound(new { message = "Document not found" });

            try
            {
                return await DownloadDocumentFileAsync(
                    document.FilePath,
                    document.FileName,
                    document.MimeType,
                    document.IsEncrypted,
                    document.EncryptionIv,
                    document.EncryptionTag);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // PUT: api/Documents/{id}
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateDocument(string id, [FromBody] DocumentUpdateDto dto)
        {
            if (dto == null)
            {
                return BadRequest(new { message = "Request body is required." });
            }

            var document = await FindDocumentAsync(id);
            if (document == null) return NotFound();

            try
            {
                var isCurrentlyOnHold = IsLegalHoldStatus(document.Status);
                var normalizedRequestedStatus = dto.Status != null
                    ? NormalizeAllowedOptional(dto.Status, AllowedDocumentStatuses, nameof(dto.Status))
                    : null;
                var isRequestedLegalHold = normalizedRequestedStatus != null && IsLegalHoldStatus(normalizedRequestedStatus);
                var isStayingOnHold = isCurrentlyOnHold && (normalizedRequestedStatus == null || isRequestedLegalHold);

                var legalHoldTransition = isCurrentlyOnHold != isRequestedLegalHold;
                if ((legalHoldTransition || dto.LegalHoldReason != null) && !User.IsInRole("SecurityAdmin"))
                {
                    return Forbid();
                }

                if (isStayingOnHold && HasRestrictedUpdates(dto))
                {
                    return BadRequest(new { message = "Document is on legal hold and cannot be modified." });
                }

                if (dto.MatterId.HasValue)
                {
                    var matterValue = dto.MatterId.Value;
                    var nextMatterId = matterValue.ValueKind == JsonValueKind.Null ? null : NormalizeOptionalText(matterValue.GetString(), 100);
                    await EnsureMatterExistsAsync(nextMatterId);
                    document.MatterId = nextMatterId;
                }
                if (dto.Description != null)
                {
                    document.Description = NormalizeOptionalText(dto.Description, 4000);
                }
                if (dto.Category != null)
                {
                    document.Category = NormalizeOptionalText(dto.Category, 250);
                }
                if (dto.Tags.HasValue)
                {
                    var tagsValue = dto.Tags.Value;
                    if (tagsValue.ValueKind == JsonValueKind.Null)
                    {
                        document.Tags = null;
                    }
                    else if (tagsValue.ValueKind == JsonValueKind.Array)
                    {
                        var tags = new List<string>();
                        foreach (var item in tagsValue.EnumerateArray())
                        {
                            if (item.ValueKind == JsonValueKind.String)
                            {
                                var tag = NormalizeOptionalText(item.GetString(), 100);
                                if (!string.IsNullOrWhiteSpace(tag))
                                {
                                    tags.Add(tag);
                                }
                            }
                        }
                        document.Tags = SerializeTags(tags.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
                    }
                    else if (tagsValue.ValueKind == JsonValueKind.String)
                    {
                        var raw = tagsValue.GetString() ?? string.Empty;
                        var tags = raw.Split(',', StringSplitOptions.RemoveEmptyEntries)
                            .Select(t => NormalizeOptionalText(t, 100))
                            .Where(t => !string.IsNullOrWhiteSpace(t))
                            .Cast<string>()
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();
                        document.Tags = SerializeTags(tags);
                    }
                    else
                    {
                        return BadRequest(new { message = "Tags must be null, string, or array." });
                    }
                }

                var previousStatus = document.Status;
                if (dto.Status != null)
                {
                    document.Status = normalizedRequestedStatus;
                }

                if (dto.LegalHoldReason != null)
                {
                    document.LegalHoldReason = NormalizeOptionalText(dto.LegalHoldReason, 1000);
                }

                var isLegalHold = IsLegalHoldStatus(document.Status);
                if (isLegalHold)
                {
                    if (!document.LegalHoldPlacedAt.HasValue)
                    {
                        document.LegalHoldPlacedAt = DateTime.UtcNow;
                    }
                    if (string.IsNullOrEmpty(document.LegalHoldPlacedBy))
                    {
                        document.LegalHoldPlacedBy = GetUserId();
                    }
                    document.LegalHoldReleasedAt = null;
                    document.LegalHoldReleasedBy = null;
                }
                else if (IsLegalHoldStatus(previousStatus))
                {
                    document.LegalHoldReleasedAt = DateTime.UtcNow;
                    document.LegalHoldReleasedBy = GetUserId();
                }

                document.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();
                await _auditLogger.LogAsync(HttpContext, "document.update", "Document", document.Id, $"Status={document.Status}, MatterId={document.MatterId}");

                return Ok(ToDocumentResponse(document));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // PUT: api/Documents/bulk-assign
        [HttpPut("bulk-assign")]
        public async Task<IActionResult> BulkAssign([FromBody] DocumentBulkAssignDto dto)
        {
            if (dto == null || dto.Ids == null || dto.Ids.Count == 0)
            {
                return BadRequest(new { message = "Document ids are required." });
            }

            try
            {
                var matterId = NormalizeOptionalText(dto.MatterId, 100);
                await EnsureMatterExistsAsync(matterId);

                var distinctIds = dto.Ids
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Select(id => id.Trim())
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                if (distinctIds.Count == 0)
                {
                    return BadRequest(new { message = "Document ids are required." });
                }

                var docs = await TenantScope(_context.Documents)
                    .Where(d => distinctIds.Contains(d.Id))
                    .ToListAsync();

                if (docs.Count != distinctIds.Count)
                {
                    return NotFound(new { message = "One or more documents were not found." });
                }

                if (docs.Any(d => IsLegalHoldStatus(d.Status)))
                {
                    return BadRequest(new { message = "One or more documents are on legal hold and cannot be modified." });
                }
                foreach (var doc in docs)
                {
                    doc.MatterId = matterId;
                    doc.UpdatedAt = DateTime.UtcNow;
                }

                await _context.SaveChangesAsync();
                await _auditLogger.LogAsync(HttpContext, "document.bulk_assign", "Document", null, $"Count={docs.Count}, MatterId={matterId}");

                return Ok(new { updated = docs.Count });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // GET: api/Documents/{id}/versions
        [HttpGet("{id}/versions")]
        public async Task<IActionResult> GetVersions(string id)
        {
            var document = await FindDocumentAsync(id, asNoTracking: true);
            if (document == null)
            {
                return NotFound(new { message = "Document not found" });
            }

            var versions = await _context.DocumentVersions
                .AsNoTracking()
                .Where(v => v.DocumentId == id)
                .OrderByDescending(v => v.CreatedAt)
                .ToListAsync();
            return Ok(versions.Select(ToDocumentVersionResponse));
        }

        // GET: api/Documents/versions/{versionId}/download
        [HttpGet("versions/{versionId}/download")]
        public async Task<IActionResult> DownloadVersion(string versionId)
        {
            var lookup = await FindDocumentVersionAsync(versionId);
            if (lookup == null) return NotFound(new { message = "Version not found" });
            try
            {
                return await DownloadDocumentFileAsync(
                    lookup.Version.FilePath,
                    lookup.Version.FileName,
                    "application/octet-stream",
                    lookup.Version.IsEncrypted,
                    lookup.Version.EncryptionIv,
                    lookup.Version.EncryptionTag);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // POST: api/Documents/versions/{versionId}/restore
        [HttpPost("versions/{versionId}/restore")]
        public async Task<IActionResult> RestoreVersion(string versionId)
        {
            try
            {
                var lookup = await FindDocumentVersionAsync(versionId);
                if (lookup == null) return NotFound(new { message = "Version not found" });

                var version = lookup.Version;
                var document = lookup.Document;
                if (IsLegalHoldStatus(document.Status))
                {
                    return BadRequest(new { message = "Document is on legal hold and cannot be restored." });
                }

                var sourcePath = ResolveTenantStoredFilePath(version.FilePath);
                if (!System.IO.File.Exists(sourcePath))
                {
                    return BadRequest(new { message = "Source file missing" });
                }

                var storedFileName = PrepareStoredFileName(version.FileName).StorageFileName;
                var destPath = Path.Combine(GetTenantUploadsFolder(), storedFileName);

                var isEncrypted = version.IsEncrypted;
                var encryptionKeyId = version.EncryptionKeyId;
                var encryptionIv = version.EncryptionIv;
                var encryptionTag = version.EncryptionTag;
                var encryptionAlgorithm = version.EncryptionAlgorithm ?? document.EncryptionAlgorithm;

                if (version.IsEncrypted)
                {
                    System.IO.File.Copy(sourcePath, destPath, true);
                }
                else if (_documentEncryptionService.Enabled)
                {
                    await using var sourceStream = System.IO.File.OpenRead(sourcePath);
                    var encrypted = await _documentEncryptionService.EncryptFileAsync(sourceStream, destPath);
                    isEncrypted = true;
                    encryptionKeyId = encrypted.KeyId;
                    encryptionIv = encrypted.Iv;
                    encryptionTag = encrypted.Tag;
                    encryptionAlgorithm = encrypted.Algorithm;
                }
                else
                {
                    System.IO.File.Copy(sourcePath, destPath, true);
                }

                document.FileName = version.FileName;
                document.Name = version.FileName;
                document.FilePath = GetTenantRelativePath(storedFileName);
                document.FileSize = version.FileSize;
                document.IsEncrypted = isEncrypted;
                document.EncryptionKeyId = encryptionKeyId;
                document.EncryptionIv = encryptionIv;
                document.EncryptionTag = encryptionTag;
                document.EncryptionAlgorithm = encryptionAlgorithm;
                document.Version += 1;
                document.UpdatedAt = DateTime.UtcNow;

                var restoredVersion = new DocumentVersion
                {
                    DocumentId = document.Id,
                    FileName = document.FileName,
                    FilePath = document.FilePath,
                    FileSize = document.FileSize,
                    IsEncrypted = document.IsEncrypted,
                    EncryptionKeyId = document.EncryptionKeyId,
                    EncryptionIv = document.EncryptionIv,
                    EncryptionTag = document.EncryptionTag,
                    EncryptionAlgorithm = document.EncryptionAlgorithm,
                    Sha256 = version.Sha256 ?? ComputeSha256(destPath),
                    UploadedByUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
                    CreatedAt = DateTime.UtcNow
                };
                _context.DocumentVersions.Add(restoredVersion);

                await _context.SaveChangesAsync();
                await _auditLogger.LogAsync(HttpContext, "document.version.restore", "Document", document.Id, $"VersionId={versionId}");

                return Ok(ToDocumentResponse(document));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // GET: api/Documents/versions/diff?leftVersionId=...&rightVersionId=...
        [EnableRateLimiting("AdminDangerousOps")]
        [HttpGet("versions/diff")]
        public async Task<IActionResult> DiffVersions([FromQuery] string leftVersionId, [FromQuery] string rightVersionId)
        {
            try
            {
                var leftLookup = await FindDocumentVersionAsync(leftVersionId);
                var rightLookup = await FindDocumentVersionAsync(rightVersionId);
                if (leftLookup == null || rightLookup == null) return NotFound(new { message = "Version not found" });

                var left = leftLookup.Version;
                var right = rightLookup.Version;

                var leftPath = ResolveTenantStoredFilePath(left.FilePath);
                var rightPath = ResolveTenantStoredFilePath(right.FilePath);

                if (!System.IO.File.Exists(leftPath) || !System.IO.File.Exists(rightPath))
                    return BadRequest(new { message = "File(s) missing for diff" });

                const long maxDiffFileSizeBytes = 10 * 1024 * 1024;
                if (left.FileSize > maxDiffFileSizeBytes || right.FileSize > maxDiffFileSizeBytes)
                {
                    return BadRequest(new { message = "Document versions are too large to diff inline." });
                }

                const int maxDiffTextLength = 200000;
                var leftText = await LoadTextAsync(left);
                var rightText = await LoadTextAsync(right);
                if (leftText.Length > maxDiffTextLength)
                {
                    leftText = leftText[..maxDiffTextLength];
                }
                if (rightText.Length > maxDiffTextLength)
                {
                    rightText = rightText[..maxDiffTextLength];
                }

                var diff = BuildSimpleDiff(leftText, rightText);

                await _auditLogger.LogAsync(HttpContext, "document.diff", "DocumentVersion", $"{leftVersionId}->{rightVersionId}", $"Diff length={diff.Length}");

                return Ok(new
                {
                    leftVersionId,
                    rightVersionId,
                    diff
                });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // GET: api/Documents/search?q=...
        [EnableRateLimiting("CrmConflictSearch")]
        [HttpGet("search")]
        public async Task<IActionResult> SearchDocuments([FromQuery] string q, [FromQuery] string? matterId = null, [FromQuery] bool includeContent = false)
        {
            if (string.IsNullOrWhiteSpace(q))
            {
                return BadRequest(new { message = "Query is required." });
            }

            try
            {
                var searchText = NormalizeRequiredText(q, nameof(q), 200);
                var normalized = searchText.ToLowerInvariant();
                var normalizedMatterId = NormalizeOptionalText(matterId, 100);
                if (!string.IsNullOrWhiteSpace(normalizedMatterId))
                {
                    await EnsureMatterExistsAsync(normalizedMatterId);
                }

                var docsQuery = TenantScope(_context.Documents).AsNoTracking().AsQueryable();
                if (!string.IsNullOrEmpty(normalizedMatterId))
                {
                    docsQuery = docsQuery.Where(d => d.MatterId == normalizedMatterId);
                }

                var likePattern = $"%{EscapeLikePattern(searchText)}%";
                var metadataMatches = await docsQuery
                    .Where(doc =>
                        (!string.IsNullOrEmpty(doc.Name) && EF.Functions.Like(doc.Name, likePattern, "\\")) ||
                        (!string.IsNullOrEmpty(doc.FileName) && EF.Functions.Like(doc.FileName, likePattern, "\\")) ||
                        (!string.IsNullOrEmpty(doc.Description) && EF.Functions.Like(doc.Description, likePattern, "\\")) ||
                        (!string.IsNullOrEmpty(doc.Tags) && EF.Functions.Like(doc.Tags, likePattern, "\\")))
                    .OrderByDescending(d => d.CreatedAt)
                    .Take(MaxDocumentResults)
                    .ToListAsync();

                var matches = new List<Document>(metadataMatches);
                var scopedDocIdsQuery = docsQuery.Select(d => d.Id);

                if (includeContent)
                {
                    var contentMatchIds = new List<string>();
                    var tokens = DocumentIndexService.TokenizeQuery(normalized);

                    if (tokens.Count > 0)
                    {
                        var tokenMatches = _context.DocumentContentTokens
                            .AsNoTracking()
                            .Where(t => tokens.Contains(t.Token))
                            .Join(scopedDocIdsQuery, t => t.DocumentId, id => id, (t, _) => t);

                        contentMatchIds = await tokenMatches
                            .GroupBy(t => t.DocumentId)
                            .Where(g => g.Select(x => x.Token).Distinct().Count() >= tokens.Count)
                            .Select(g => g.Key)
                            .Take(MaxDocumentResults)
                            .ToListAsync();

                        if (contentMatchIds.Count > 0 && normalized.Length >= 4 && normalized.Contains(' '))
                        {
                            var phraseCandidates = await _context.DocumentContentIndexes
                                .AsNoTracking()
                                .Where(i => contentMatchIds.Contains(i.DocumentId))
                                .Join(scopedDocIdsQuery, i => i.DocumentId, id => id, (i, _) => new { i.DocumentId, i.NormalizedContent })
                                .Take(MaxDocumentResults)
                                .ToListAsync();

                            contentMatchIds = phraseCandidates
                                .Where(i => i.NormalizedContent != null && i.NormalizedContent.Contains(normalized))
                                .Select(i => i.DocumentId)
                                .Distinct(StringComparer.Ordinal)
                                .ToList();
                        }
                    }
                    else if (normalized.Length >= 3)
                    {
                        var candidates = await _context.DocumentContentIndexes
                            .AsNoTracking()
                            .Join(scopedDocIdsQuery, i => i.DocumentId, id => id, (i, _) => new { i.DocumentId, i.NormalizedContent, i.IndexedAt })
                            .OrderByDescending(i => i.IndexedAt)
                            .Take(MaxDocumentResults)
                            .ToListAsync();

                        contentMatchIds = candidates
                            .Where(i => i.NormalizedContent != null && i.NormalizedContent.Contains(normalized))
                            .Select(i => i.DocumentId)
                            .Distinct(StringComparer.Ordinal)
                            .ToList();
                    }

                    if (contentMatchIds.Count > 0)
                    {
                        var contentMatches = await docsQuery
                            .Where(d => contentMatchIds.Contains(d.Id))
                            .OrderByDescending(d => d.CreatedAt)
                            .Take(MaxDocumentResults)
                            .ToListAsync();

                        foreach (var doc in contentMatches)
                        {
                            if (matches.All(m => m.Id != doc.Id))
                            {
                                matches.Add(doc);
                            }
                        }
                    }
                }

                if (matches.Count > MaxDocumentResults)
                {
                    matches = matches
                        .OrderByDescending(d => d.CreatedAt)
                        .Take(MaxDocumentResults)
                        .ToList();
                }

                await _auditLogger.LogAsync(
                    HttpContext,
                    "document.search",
                    "Document",
                    null,
                    $"QueryHash={ComputeQueryHash(searchText)}, QueryLength={searchText.Length}, IncludeContent={includeContent}, Results={matches.Count}");

                return Ok(matches.Select(ToDocumentResponse));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        private static string BuildSimpleDiff(string left, string right)
        {
            var builder = new InlineDiffBuilder(new Differ());
            var diff = builder.BuildDiffModel(left, right);
            var sb = new StringBuilder();
            sb.AppendLine("--- LEFT");
            sb.AppendLine("+++ RIGHT");
            foreach (var line in diff.Lines)
            {
                var prefix = line.Type switch
                {
                    ChangeType.Inserted => "+ ",
                    ChangeType.Deleted => "- ",
                    ChangeType.Modified => "~ ",
                    _ => "  "
                };
                sb.AppendLine(prefix + (line.Text ?? string.Empty));
            }
            return sb.ToString();
        }

        // POST: api/Documents/upload
        [RequestSizeLimit(50 * 1024 * 1024)]
        [RequestFormLimits(MultipartBodyLengthLimit = 50 * 1024 * 1024)]
        [HttpPost("upload")]
        public async Task<ActionResult<DocumentResponseDto>> UploadDocument([FromForm] IFormFile file, [FromForm] string? matterId, [FromForm] string? description)
        {
            try
            {
                var prepared = await ValidateAndPrepareUploadAsync(file);
                var normalizedMatterId = NormalizeOptionalText(matterId, 100);
                await EnsureMatterExistsAsync(normalizedMatterId);
                var normalizedDescription = NormalizeOptionalText(description, 4000);
                var storagePath = Path.Combine(GetTenantUploadsFolder(), prepared.StorageFileName);
                var uploadedBy = GetUserId();

                string plaintextSha;
                await using (var hashStream = file.OpenReadStream())
                {
                    plaintextSha = ComputeSha256(hashStream);
                }

                DocumentEncryptionPayload? encryptionPayload = null;
                if (_documentEncryptionService.Enabled)
                {
                    await using var stream = file.OpenReadStream();
                    encryptionPayload = await _documentEncryptionService.EncryptFileAsync(stream, storagePath);
                }
                else
                {
                    await using var stream = new FileStream(storagePath, FileMode.Create);
                    await file.CopyToAsync(stream);
                }

                var document = new Document
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = prepared.DisplayName,
                    FileName = prepared.DisplayName,
                    FilePath = GetTenantRelativePath(prepared.StorageFileName),
                    FileSize = file.Length,
                    MimeType = file.ContentType,
                    IsEncrypted = encryptionPayload != null,
                    EncryptionKeyId = encryptionPayload?.KeyId,
                    EncryptionIv = encryptionPayload?.Iv,
                    EncryptionTag = encryptionPayload?.Tag,
                    EncryptionAlgorithm = encryptionPayload?.Algorithm,
                    MatterId = normalizedMatterId,
                    Description = normalizedDescription,
                    Status = "Draft",
                    UploadedBy = uploadedBy,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                var version = new DocumentVersion
                {
                    DocumentId = document.Id,
                    FileName = document.FileName,
                    FilePath = document.FilePath,
                    FileSize = file.Length,
                    IsEncrypted = document.IsEncrypted,
                    EncryptionKeyId = document.EncryptionKeyId,
                    EncryptionIv = document.EncryptionIv,
                    EncryptionTag = document.EncryptionTag,
                    EncryptionAlgorithm = document.EncryptionAlgorithm,
                    Sha256 = plaintextSha,
                    UploadedByUserId = uploadedBy,
                    CreatedAt = DateTime.UtcNow
                };

                await using (var tx = await _context.Database.BeginTransactionAsync())
                {
                    _context.Documents.Add(document);
                    _context.DocumentVersions.Add(version);
                    await _context.SaveChangesAsync();
                    await tx.CommitAsync();
                }

                try
                {
                    await _documentIndexService.UpsertIndexAsync(document, storagePath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Document index update failed for uploaded document {DocumentId}.", document.Id);
                }

                await _auditLogger.LogAsync(HttpContext, "document.upload", "Document", document.Id, $"MatterId={normalizedMatterId}, Name={prepared.DisplayName}, Size={file.Length}");

                return Ok(ToDocumentResponse(document));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // POST: api/Documents/{id}/versions
        [RequestSizeLimit(50 * 1024 * 1024)]
        [RequestFormLimits(MultipartBodyLengthLimit = 50 * 1024 * 1024)]
        [HttpPost("{id}/versions")]
        public async Task<IActionResult> UploadNewVersion(string id, [FromForm] IFormFile file)
        {
            try
            {
                var prepared = await ValidateAndPrepareUploadAsync(file);
                var document = await FindDocumentAsync(id);
                if (document == null) return NotFound();
                if (IsLegalHoldStatus(document.Status))
                {
                    return BadRequest(new { message = "Document is on legal hold and cannot be modified." });
                }

                var storagePath = Path.Combine(GetTenantUploadsFolder(), prepared.StorageFileName);
                var uploadedBy = GetUserId();
                string plaintextSha;
                await using (var hashStream = file.OpenReadStream())
                {
                    plaintextSha = ComputeSha256(hashStream);
                }

                DocumentEncryptionPayload? encryptionPayload = null;

                if (_documentEncryptionService.Enabled)
                {
                    await using var stream = file.OpenReadStream();
                    encryptionPayload = await _documentEncryptionService.EncryptFileAsync(stream, storagePath);
                }
                else
                {
                    await using var stream = new FileStream(storagePath, FileMode.Create);
                    await file.CopyToAsync(stream);
                }

                document.FileName = prepared.DisplayName;
                document.Name = prepared.DisplayName;
                document.FilePath = GetTenantRelativePath(prepared.StorageFileName);
                document.FileSize = file.Length;
                document.MimeType = file.ContentType;
                document.IsEncrypted = encryptionPayload != null;
                document.EncryptionKeyId = encryptionPayload?.KeyId;
                document.EncryptionIv = encryptionPayload?.Iv;
                document.EncryptionTag = encryptionPayload?.Tag;
                document.EncryptionAlgorithm = encryptionPayload?.Algorithm;
                document.Version += 1;
                document.UpdatedAt = DateTime.UtcNow;

                var version = new DocumentVersion
                {
                    DocumentId = document.Id,
                    FileName = document.FileName,
                    FilePath = document.FilePath,
                    FileSize = file.Length,
                    IsEncrypted = document.IsEncrypted,
                    EncryptionKeyId = document.EncryptionKeyId,
                    EncryptionIv = document.EncryptionIv,
                    EncryptionTag = document.EncryptionTag,
                    EncryptionAlgorithm = document.EncryptionAlgorithm,
                    Sha256 = plaintextSha,
                    UploadedByUserId = uploadedBy,
                    CreatedAt = DateTime.UtcNow
                };
                _context.DocumentVersions.Add(version);

                await _context.SaveChangesAsync();
                try
                {
                    await _documentIndexService.UpsertIndexAsync(document, storagePath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Document index update failed for version upload {DocumentId}.", document.Id);
                }

                await _auditLogger.LogAsync(HttpContext, "document.version.upload", "Document", document.Id, $"Version={document.Version}");

                return Ok(ToDocumentResponse(document));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // POST: api/Documents/reindex?limit=200&force=true
        [Authorize(Roles = "Admin,SecurityAdmin")]
        [EnableRateLimiting("AdminDangerousOps")]
        [HttpPost("reindex")]
        public async Task<IActionResult> ReindexDocuments([FromQuery] int limit = 200, [FromQuery] bool force = false)
        {
            try
            {
                var normalizedLimit = Math.Clamp(limit <= 0 ? 200 : limit, 1, 500);
                var indexed = await _documentIndexService.ReindexAllAsync(RequireTenantId(), normalizedLimit, force);
                await _auditLogger.LogAsync(HttpContext, "document.reindex", "DocumentContentIndex", null, $"Count={indexed}, Force={force}");
                return Ok(new { indexedCount = indexed });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // GET: api/Documents/{id}/shares
        [HttpGet("{id}/shares")]
        public async Task<IActionResult> GetShares(string id)
        {
            if (IsClient()) return Forbid();

            var document = await FindDocumentAsync(id, asNoTracking: true);
            if (document == null) return NotFound(new { message = "Document not found" });

            var shares = await _context.DocumentShares
                .AsNoTracking()
                .Where(s => s.DocumentId == id)
                .OrderByDescending(s => s.SharedAt)
                .ToListAsync();

            var clientIds = shares.Select(s => s.ClientId).Distinct().ToList();
            var clients = await TenantScope(_context.Clients)
                .AsNoTracking()
                .Where(c => clientIds.Contains(c.Id))
                .Select(c => new { c.Id, c.Name })
                .ToListAsync();
            var clientMap = clients.ToDictionary(c => c.Id, c => c);

            var response = shares.Select(s => new
            {
                id = s.Id,
                documentId = s.DocumentId,
                clientId = s.ClientId,
                client = clientMap.TryGetValue(s.ClientId, out var client) ? client : null,
                canView = s.CanView,
                canDownload = s.CanDownload,
                canComment = s.CanComment,
                canUpload = s.CanUpload,
                sharedAt = s.SharedAt,
                updatedAt = s.UpdatedAt,
                expiresAt = s.ExpiresAt,
                note = s.Note,
                sharedByUserId = s.SharedByUserId
            });

            return Ok(response);
        }

        // POST: api/Documents/{id}/shares
        [HttpPost("{id}/shares")]
        public async Task<IActionResult> UpsertShare(string id, [FromBody] DocumentShareUpsertDto dto)
        {
            if (IsClient()) return Forbid();
            if (dto == null || string.IsNullOrWhiteSpace(dto.ClientId))
            {
                return BadRequest(new { message = "Client is required." });
            }

            var document = await FindDocumentAsync(id);
            if (document == null) return NotFound(new { message = "Document not found" });

            var normalizedClientId = NormalizeRequiredText(dto.ClientId, nameof(dto.ClientId), 100);
            var client = await TenantScope(_context.Clients).FirstOrDefaultAsync(c => c.Id == normalizedClientId);
            if (client == null) return BadRequest(new { message = "Client not found." });

            var share = await _context.DocumentShares.FirstOrDefaultAsync(s => s.DocumentId == id && s.ClientId == normalizedClientId);
            if (share == null)
            {
                share = new DocumentShare
                {
                    DocumentId = id,
                    ClientId = normalizedClientId,
                    SharedByUserId = GetUserId(),
                    SharedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _context.DocumentShares.Add(share);
            }

            share.CanView = dto.CanView ?? share.CanView;
            share.CanDownload = dto.CanDownload ?? share.CanDownload;
            share.CanComment = dto.CanComment ?? share.CanComment;
            share.CanUpload = dto.CanUpload ?? share.CanUpload;
            share.ExpiresAt = dto.ExpiresAt;
            share.Note = NormalizeOptionalText(dto.Note, 1000);
            share.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await _auditLogger.LogAsync(HttpContext, "document.share.upsert", "DocumentShare", share.Id, $"Document={id}, Client={normalizedClientId}");

            return Ok(new
            {
                id = share.Id,
                documentId = share.DocumentId,
                clientId = share.ClientId,
                canView = share.CanView,
                canDownload = share.CanDownload,
                canComment = share.CanComment,
                canUpload = share.CanUpload,
                sharedAt = share.SharedAt,
                updatedAt = share.UpdatedAt,
                expiresAt = share.ExpiresAt,
                note = share.Note,
                sharedByUserId = share.SharedByUserId
            });
        }

        // DELETE: api/Documents/{id}/shares/{clientId}
        [HttpDelete("{id}/shares/{clientId}")]
        public async Task<IActionResult> RemoveShare(string id, string clientId)
        {
            if (IsClient()) return Forbid();

            var document = await FindDocumentAsync(id, asNoTracking: true);
            if (document == null) return NotFound(new { message = "Document not found" });

            var share = await _context.DocumentShares.FirstOrDefaultAsync(s => s.DocumentId == id && s.ClientId == clientId);
            if (share == null) return NotFound(new { message = "Share not found." });

            _context.DocumentShares.Remove(share);
            await _context.SaveChangesAsync();

            await _auditLogger.LogAsync(HttpContext, "document.share.remove", "DocumentShare", share.Id, $"Document={id}, Client={clientId}");
            return NoContent();
        }

        // GET: api/Documents/{id}/comments
        [HttpGet("{id}/comments")]
        public async Task<IActionResult> GetComments(string id)
        {
            if (IsClient()) return Forbid();

            var document = await FindDocumentAsync(id, asNoTracking: true);
            if (document == null) return NotFound(new { message = "Document not found" });

            var comments = await _context.DocumentComments
                .AsNoTracking()
                .Where(c => c.DocumentId == id)
                .OrderBy(c => c.CreatedAt)
                .ToListAsync();

            var userIds = comments.Where(c => !string.IsNullOrWhiteSpace(c.AuthorUserId)).Select(c => c.AuthorUserId!).Distinct().ToList();
            var clientIds = comments.Where(c => !string.IsNullOrWhiteSpace(c.AuthorClientId)).Select(c => c.AuthorClientId!).Distinct().ToList();

            var users = await TenantScope(_context.Users)
                .AsNoTracking()
                .Where(u => userIds.Contains(u.Id))
                .Select(u => new { u.Id, u.Name })
                .ToListAsync();
            var clients = await TenantScope(_context.Clients)
                .AsNoTracking()
                .Where(c => clientIds.Contains(c.Id))
                .Select(c => new { c.Id, c.Name })
                .ToListAsync();

            var userMap = users.ToDictionary(u => u.Id, u => u.Name);
            var clientMap = clients.ToDictionary(c => c.Id, c => c.Name);

            var response = comments.Select(c => new
            {
                id = c.Id,
                documentId = c.DocumentId,
                body = c.Body,
                createdAt = c.CreatedAt,
                authorType = c.AuthorType,
                author = new
                {
                    id = c.AuthorType == "Client" ? c.AuthorClientId : c.AuthorUserId,
                    name = c.AuthorType == "Client"
                        ? (c.AuthorClientId != null && clientMap.TryGetValue(c.AuthorClientId, out var clientName) ? clientName : "Client")
                        : (c.AuthorUserId != null && userMap.TryGetValue(c.AuthorUserId, out var userName) ? userName : "Staff")
                }
            });

            return Ok(response);
        }

        // POST: api/Documents/{id}/comments
        [HttpPost("{id}/comments")]
        public async Task<IActionResult> AddComment(string id, [FromBody] DocumentCommentCreateDto dto)
        {
            if (IsClient()) return Forbid();
            if (dto == null || string.IsNullOrWhiteSpace(dto.Body))
            {
                return BadRequest(new { message = "Comment body is required." });
            }

            var document = await FindDocumentAsync(id);
            if (document == null) return NotFound(new { message = "Document not found" });

            var userId = GetUserId();
            if (string.IsNullOrWhiteSpace(userId)) return Unauthorized();

            var comment = new DocumentComment
            {
                DocumentId = id,
                Body = NormalizeRequiredText(dto.Body, nameof(dto.Body), 4000),
                AuthorUserId = userId,
                AuthorType = "Staff",
                CreatedAt = DateTime.UtcNow
            };

            _context.DocumentComments.Add(comment);

            var targetClientId = await ResolveClientIdAsync(document);
            if (!string.IsNullOrWhiteSpace(targetClientId))
            {
                _context.Notifications.Add(new Notification
                {
                    ClientId = targetClientId,
                    Title = "New document comment",
                    Message = $"A new comment was added to {document.Name}.",
                    Type = "info",
                    Link = "tab:documents"
                });
            }

            await _context.SaveChangesAsync();
            await _auditLogger.LogAsync(HttpContext, "document.comment.add", "DocumentComment", comment.Id, $"Document={id}");

            return Ok(new
            {
                id = comment.Id,
                documentId = comment.DocumentId,
                body = comment.Body,
                createdAt = comment.CreatedAt,
                authorType = comment.AuthorType
            });
        }

        // DELETE: api/Documents/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteDocument(string id)
        {
            try
            {
                 var document = await FindDocumentAsync(id);
                if (document == null)
                {
                    return NotFound();
                }

                if (string.Equals(document.Status, "Legal Hold", StringComparison.OrdinalIgnoreCase))
                {
                    return BadRequest(new { message = "Document is on legal hold and cannot be deleted." });
                }

                var fullPath = ResolveTenantStoredFilePath(document.FilePath);
                var sha256 = System.IO.File.Exists(fullPath) ? ComputeSha256(fullPath) : null;

                var version = new DocumentVersion
                {
                    DocumentId = document.Id,
                    FileName = document.FileName,
                    FilePath = document.FilePath,
                    FileSize = document.FileSize,
                    IsEncrypted = document.IsEncrypted,
                    EncryptionKeyId = document.EncryptionKeyId,
                    EncryptionIv = document.EncryptionIv,
                    EncryptionTag = document.EncryptionTag,
                    EncryptionAlgorithm = document.EncryptionAlgorithm,
                    Sha256 = sha256,
                    UploadedByUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
                    CreatedAt = DateTime.UtcNow
                };
                await using (var tx = await _context.Database.BeginTransactionAsync())
                {
                    _context.DocumentVersions.Add(version);
                    _context.Documents.Remove(document);
                    await _context.SaveChangesAsync();
                    await tx.CommitAsync();
                }

                if (System.IO.File.Exists(fullPath))
                {
                    System.IO.File.Delete(fullPath);
                }

                await _auditLogger.LogAsync(HttpContext, "document.delete", "Document", id, $"Deleted document {document.FileName}");

                return NoContent();
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        private bool IsClient()
        {
            return User.IsInRole("Client") || string.Equals(User.FindFirst("role")?.Value, "Client", StringComparison.OrdinalIgnoreCase);
        }

        private string? GetUserId()
        {
            return User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                   ?? User.FindFirst("sub")?.Value;
        }

        private async Task<string?> ResolveClientIdAsync(Document document)
        {
            if (!string.IsNullOrWhiteSpace(document.MatterId))
            {
                var matter = await TenantScope(_context.Matters).FirstOrDefaultAsync(m => m.Id == document.MatterId);
                if (matter != null) return matter.ClientId;
            }

            if (!string.IsNullOrWhiteSpace(document.UploadedBy))
            {
                var isClient = await TenantScope(_context.Clients).AnyAsync(c => c.Id == document.UploadedBy);
                if (isClient) return document.UploadedBy;
            }

            var share = await _context.DocumentShares
                .Where(s => s.DocumentId == document.Id)
                .OrderByDescending(s => s.SharedAt)
                .FirstOrDefaultAsync();
            return share?.ClientId;
        }

        private sealed record SanitizedUploadFile(string DisplayName, string StorageFileName, string Extension);
        private sealed record DocumentVersionLookup(Document Document, DocumentVersion Version);
    }

    public class DocumentUpdateDto
    {
        public JsonElement? MatterId { get; set; }
        public string? Description { get; set; }
        public JsonElement? Tags { get; set; }
        public string? Category { get; set; }
        public string? Status { get; set; }
        public string? LegalHoldReason { get; set; }
    }

    public class DocumentBulkAssignDto
    {
        public List<string> Ids { get; set; } = new();
        public string? MatterId { get; set; }
    }

    public class DocumentShareUpsertDto
    {
        public string ClientId { get; set; } = string.Empty;
        public bool? CanView { get; set; }
        public bool? CanDownload { get; set; }
        public bool? CanComment { get; set; }
        public bool? CanUpload { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public string? Note { get; set; }
    }

    public class DocumentCommentCreateDto
    {
        public string Body { get; set; } = string.Empty;
    }

    public class DocumentResponseDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string DownloadUrl { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string? MimeType { get; set; }
        public string? MatterId { get; set; }
        public string? UploadedBy { get; set; }
        public int Version { get; set; }
        public string? Category { get; set; }
        public string? Description { get; set; }
        public IReadOnlyList<string> Tags { get; set; } = Array.Empty<string>();
        public string? Status { get; set; }
        public string? LegalHoldReason { get; set; }
        public DateTime? LegalHoldPlacedAt { get; set; }
        public string? LegalHoldPlacedBy { get; set; }
        public DateTime? LegalHoldReleasedAt { get; set; }
        public string? LegalHoldReleasedBy { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class DocumentVersionResponseDto
    {
        public string Id { get; set; } = string.Empty;
        public string DocumentId { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string DownloadUrl { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public bool IsEncrypted { get; set; }
        public string? EncryptionAlgorithm { get; set; }
        public string? Sha256 { get; set; }
        public string? UploadedByUserId { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
