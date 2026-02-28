using System.Security.Claims;
using System.Text.Json;
using System.Linq.Expressions;
using System.Data;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using JurisFlow.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace JurisFlow.Server.Controllers
{
    [Route("api/app-directory")]
    [ApiController]
    [Authorize(Policy = "StaffOrClient")]
    public class AppDirectoryController : ControllerBase
    {
        private readonly JurisFlowDbContext _context;
        private readonly AppDirectoryOnboardingService _onboardingService;
        private readonly AuditLogger _auditLogger;
        private readonly TenantContext _tenantContext;

        private const int MaxManifestJsonLength = 64_000;
        private const int MaxHarnessJsonLength = 64_000;
        private const int MaxValidationErrorsJsonLength = 32_000;

        public AppDirectoryController(
            JurisFlowDbContext context,
            AppDirectoryOnboardingService onboardingService,
            AuditLogger auditLogger,
            TenantContext tenantContext)
        {
            _context = context;
            _onboardingService = onboardingService;
            _auditLogger = auditLogger;
            _tenantContext = tenantContext;
        }

        [HttpGet("listings")]
        public async Task<IActionResult> GetListings([FromQuery] bool includeAll = false)
        {
            var canSeeAll = includeAll && IsStaff();
            var query = TenantScope(_context.AppDirectoryListings).AsNoTracking();
            if (!canSeeAll)
            {
                query = query.Where(l => l.Status == "published");
            }

            var listings = await query
                .OrderByDescending(l => l.IsFeatured)
                .ThenBy(l => l.Name)
                .Select(MapListingProjection())
                .ToListAsync();

            return Ok(listings);
        }

        [HttpGet("listings/{id}")]
        public async Task<IActionResult> GetListing(string id)
        {
            var query = TenantScope(_context.AppDirectoryListings)
                .AsNoTracking()
                .Where(l => l.Id == id);
            if (!IsStaff())
            {
                query = query.Where(l => l.Status == "published");
            }

            var listing = await query
                .Select(MapListingProjection())
                .FirstOrDefaultAsync();
            if (listing == null)
            {
                return NotFound(new { message = "Listing not found." });
            }

            return Ok(listing);
        }

        [HttpGet("review-queue")]
        [Authorize(Policy = "StaffOnly")]
        public async Task<IActionResult> GetReviewQueue()
        {
            var queue = await TenantScope(_context.AppDirectoryListings)
                .AsNoTracking()
                .Where(l => l.Status == "in_review" || l.Status == "submitted" || l.Status == "changes_requested")
                .OrderByDescending(l => l.LastSubmittedAt ?? l.UpdatedAt)
                .Select(MapListingProjection())
                .ToListAsync();

            return Ok(queue);
        }

        [HttpPost("onboarding/submit")]
        [Authorize(Policy = "StaffOnly")]
        public async Task<IActionResult> SubmitOnboarding([FromBody] OnboardingSubmissionRequest request)
        {
            if (request?.Manifest == null)
            {
                return BadRequest(new { message = "Manifest is required." });
            }

            var userId = ResolveUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Unauthorized();
            }

            var manifest = _onboardingService.NormalizeManifest(request.Manifest);
            var sla = _onboardingService.NormalizeSla(request.Sla);
            var validationErrors = ValidateOnboardingInput(manifest);
            if (validationErrors.Count > 0)
            {
                return BadRequest(new { message = "Manifest validation failed.", errors = validationErrors });
            }

            var harness = _onboardingService.RunHarness(manifest, sla);
            var now = DateTime.UtcNow;

            var manifestJson = _onboardingService.SerializeManifest(manifest);
            var failedChecksJson = _onboardingService.SerializeFailedChecks(harness);
            var harnessJson = _onboardingService.SerializeHarness(harness);
            var payloadLengthErrors = ValidateJsonPayloadSizes(manifestJson, failedChecksJson, harnessJson);
            if (payloadLengthErrors.Count > 0)
            {
                return BadRequest(new { message = "Manifest payload is too large.", errors = payloadLengthErrors });
            }

            await using var tx = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var listing = await TenantScope(_context.AppDirectoryListings)
                .FirstOrDefaultAsync(l => l.ProviderKey == manifest.ProviderKey);

            if (listing == null)
            {
                listing = new AppDirectoryListing
                {
                    Id = Guid.NewGuid().ToString(),
                    ProviderKey = manifest.ProviderKey,
                    CreatedBy = userId,
                    CreatedAt = now
                };
                _context.AppDirectoryListings.Add(listing);
            }

            ApplyManifestToListing(listing, manifest, sla, harness, manifestJson, harnessJson, now);
            listing.Status = _onboardingService.ResolveListingStatus(harness);
            listing.SubmissionCount += 1;
            listing.LastSubmittedAt = now;
            listing.UpdatedAt = now;

            _context.AppDirectorySubmissions.Add(new AppDirectorySubmission
            {
                Id = Guid.NewGuid().ToString(),
                ListingId = listing.Id,
                SubmittedBy = userId,
                Status = _onboardingService.ResolveSubmissionStatus(harness),
                ManifestJson = manifestJson,
                ValidationErrorsJson = failedChecksJson,
                TestReportJson = harnessJson,
                TestStatus = _onboardingService.ResolveTestStatus(harness),
                StartedAt = now,
                CompletedAt = now,
                CreatedAt = now
            });

            try
            {
                await _context.SaveChangesAsync();
                await tx.CommitAsync();
            }
            catch (DbUpdateException)
            {
                return Conflict(new { message = "ProviderKey already exists for this tenant. Please retry." });
            }

            await _auditLogger.LogAsync(
                HttpContext,
                "app_directory.onboarding.submit",
                nameof(AppDirectoryListing),
                listing.Id,
                $"App onboarding submitted for {listing.ProviderKey} with status {listing.Status}.");

            return Ok(new OnboardingSubmissionResponse
            {
                Listing = MapListing(listing),
                Harness = MapHarness(harness)
            });
        }

        [HttpPost("listings/{id}/retest")]
        [Authorize(Policy = "StaffOnly")]
        public async Task<IActionResult> RetestListing(string id)
        {
            var listing = await TenantScope(_context.AppDirectoryListings).FirstOrDefaultAsync(l => l.Id == id);
            if (listing == null)
            {
                return NotFound(new { message = "Listing not found." });
            }

            var userId = ResolveUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Unauthorized();
            }

            AppDirectoryManifest? manifest;
            try
            {
                manifest = JsonSerializer.Deserialize<AppDirectoryManifest>(listing.ManifestJson);
            }
            catch (JsonException)
            {
                manifest = null;
            }

            if (manifest == null)
            {
                return BadRequest(new { message = "Listing manifest is invalid and cannot be retested." });
            }

            var normalizedManifest = _onboardingService.NormalizeManifest(manifest);
            var sla = new AppDirectorySlaProfile
            {
                Tier = listing.SlaTier,
                ResponseHours = listing.SlaResponseHours,
                ResolutionHours = listing.SlaResolutionHours,
                UptimePercent = listing.SlaUptimePercent
            };
            var validationErrors = ValidateOnboardingInput(normalizedManifest);
            if (validationErrors.Count > 0)
            {
                return BadRequest(new { message = "Stored manifest validation failed.", errors = validationErrors });
            }
            var harness = _onboardingService.RunHarness(normalizedManifest, sla);
            var now = DateTime.UtcNow;
            var manifestJson = _onboardingService.SerializeManifest(normalizedManifest);
            var failedChecksJson = _onboardingService.SerializeFailedChecks(harness);
            var harnessJson = _onboardingService.SerializeHarness(harness);
            var payloadLengthErrors = ValidateJsonPayloadSizes(manifestJson, failedChecksJson, harnessJson);
            if (payloadLengthErrors.Count > 0)
            {
                return BadRequest(new { message = "Stored manifest payload is too large.", errors = payloadLengthErrors });
            }

            ApplyManifestToListing(listing, normalizedManifest, sla, harness, manifestJson, harnessJson, now);
            if (harness.Passed && (listing.Status == "changes_requested" || listing.Status == "rejected" || listing.Status == "draft"))
            {
                listing.Status = "in_review";
            }

            listing.UpdatedAt = now;

            _context.AppDirectorySubmissions.Add(new AppDirectorySubmission
            {
                Id = Guid.NewGuid().ToString(),
                ListingId = listing.Id,
                SubmittedBy = userId,
                Status = "retest",
                ManifestJson = manifestJson,
                ValidationErrorsJson = failedChecksJson,
                TestReportJson = harnessJson,
                TestStatus = _onboardingService.ResolveTestStatus(harness),
                StartedAt = now,
                CompletedAt = now,
                CreatedAt = now
            });

            await _context.SaveChangesAsync();
            await _auditLogger.LogAsync(
                HttpContext,
                "app_directory.onboarding.retest",
                nameof(AppDirectoryListing),
                listing.Id,
                $"App listing retested for {listing.ProviderKey} with test status {listing.LastTestStatus}.");

            return Ok(new OnboardingSubmissionResponse
            {
                Listing = MapListing(listing),
                Harness = MapHarness(harness)
            });
        }

        [HttpPost("listings/{id}/review")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> ReviewListing(string id, [FromBody] ListingReviewRequest request)
        {
            if (!IsAdmin())
            {
                return Forbid();
            }

            var listing = await TenantScope(_context.AppDirectoryListings).FirstOrDefaultAsync(l => l.Id == id);
            if (listing == null)
            {
                return NotFound(new { message = "Listing not found." });
            }

            var decision = (request.Decision ?? string.Empty).Trim().ToLowerInvariant();
            if (decision != "approve" && decision != "reject" && decision != "request_changes" && decision != "suspend")
            {
                return BadRequest(new { message = "Decision must be one of: approve, reject, request_changes, suspend." });
            }

            if (request.Publish && decision != "approve")
            {
                return BadRequest(new { message = "Only approve decision can publish a listing." });
            }

            if (!IsReviewTransitionAllowed(listing.Status, decision))
            {
                return BadRequest(new { message = $"Listing status transition is not allowed from '{listing.Status}' with decision '{decision}'." });
            }

            var now = DateTime.UtcNow;
            var reviewer = ResolveUserId() ?? "unknown";

            if (request.Sla != null)
            {
                var normalizedSla = _onboardingService.NormalizeSla(new AppDirectorySlaProfile
                {
                    Tier = request.Sla.Tier ?? listing.SlaTier,
                    ResponseHours = request.Sla.ResponseHours ?? listing.SlaResponseHours,
                    ResolutionHours = request.Sla.ResolutionHours ?? listing.SlaResolutionHours,
                    UptimePercent = request.Sla.UptimePercent ?? listing.SlaUptimePercent
                });
                listing.SlaTier = normalizedSla.Tier;
                listing.SlaResponseHours = normalizedSla.ResponseHours;
                listing.SlaResolutionHours = normalizedSla.ResolutionHours;
                listing.SlaUptimePercent = normalizedSla.UptimePercent;
            }

            listing.ReviewNotes = NormalizeOptional(request.Notes);
            listing.ReviewedBy = reviewer;
            listing.ReviewedAt = now;
            listing.IsFeatured = request.IsFeatured ?? listing.IsFeatured;

            switch (decision)
            {
                case "approve":
                    listing.Status = request.Publish ? "published" : "approved";
                    if (request.Publish)
                    {
                        listing.PublishedAt = now;
                    }
                    break;
                case "reject":
                    listing.Status = "rejected";
                    listing.PublishedAt = null;
                    listing.IsFeatured = false;
                    break;
                case "request_changes":
                    listing.Status = "changes_requested";
                    listing.PublishedAt = null;
                    listing.IsFeatured = false;
                    break;
                case "suspend":
                    listing.Status = "suspended";
                    listing.IsFeatured = false;
                    break;
            }

            listing.UpdatedAt = now;

            await _context.SaveChangesAsync();
            await _auditLogger.LogAsync(
                HttpContext,
                "app_directory.listing.review",
                nameof(AppDirectoryListing),
                listing.Id,
                $"Listing {listing.ProviderKey} reviewed with decision {decision}.");

            return Ok(MapListing(listing));
        }

        [HttpGet("listings/{id}/submissions")]
        [Authorize(Policy = "StaffOnly")]
        public async Task<IActionResult> GetListingSubmissions(string id)
        {
            var listingExists = await TenantScope(_context.AppDirectoryListings).AnyAsync(l => l.Id == id);
            if (!listingExists)
            {
                return NotFound(new { message = "Listing not found." });
            }

            var canViewSubmitter = IsAdmin() || User.IsInRole("SecurityAdmin");
            var submissions = await TenantScope(_context.AppDirectorySubmissions)
                .AsNoTracking()
                .Where(s => s.ListingId == id)
                .OrderByDescending(s => s.CreatedAt)
                .Take(50)
                .Select(s => new SubmissionDto
                {
                    Id = s.Id,
                    ListingId = s.ListingId,
                    SubmittedBy = canViewSubmitter ? s.SubmittedBy : string.Empty,
                    Status = s.Status,
                    TestStatus = s.TestStatus,
                    StartedAt = s.StartedAt,
                    CompletedAt = s.CompletedAt,
                    CreatedAt = s.CreatedAt
                })
                .ToListAsync();

            return Ok(submissions);
        }

        private static void ApplyManifestToListing(
            AppDirectoryListing listing,
            AppDirectoryManifest manifest,
            AppDirectorySlaProfile sla,
            AppDirectoryHarnessResult harness,
            string manifestJson,
            string harnessJson,
            DateTime now)
        {
            listing.ProviderKey = manifest.ProviderKey;
            listing.Name = manifest.Name;
            listing.Category = manifest.Category;
            listing.ConnectionMode = manifest.ConnectionMode;
            listing.Summary = manifest.Summary;
            listing.Description = manifest.Description;
            listing.ManifestVersion = manifest.ManifestVersion;
            listing.ManifestJson = manifestJson;
            listing.WebsiteUrl = manifest.WebsiteUrl;
            listing.DocumentationUrl = manifest.DocumentationUrl;
            listing.SupportEmail = manifest.SupportEmail;
            listing.SupportUrl = manifest.SupportUrl;
            listing.LogoUrl = manifest.LogoUrl;
            listing.SupportsWebhook = manifest.SupportsWebhook;
            listing.WebhookFirst = manifest.WebhookFirst;
            listing.FallbackPollingMinutes = manifest.FallbackPollingMinutes;
            listing.SlaTier = sla.Tier;
            listing.SlaResponseHours = sla.ResponseHours;
            listing.SlaResolutionHours = sla.ResolutionHours;
            listing.SlaUptimePercent = sla.UptimePercent;
            listing.LastTestStatus = harness.Passed ? "passed" : "failed";
            listing.LastTestedAt = now;
            listing.LastTestSummary = harness.Summary;
            listing.LastTestReportJson = harnessJson;
        }

        private static ListingDto MapListing(AppDirectoryListing listing)
        {
            return new ListingDto
            {
                Id = listing.Id,
                ProviderKey = listing.ProviderKey,
                Name = listing.Name,
                Category = listing.Category,
                ConnectionMode = listing.ConnectionMode,
                Summary = listing.Summary,
                Description = listing.Description,
                ManifestVersion = listing.ManifestVersion,
                WebsiteUrl = listing.WebsiteUrl,
                DocumentationUrl = listing.DocumentationUrl,
                SupportEmail = listing.SupportEmail,
                SupportUrl = listing.SupportUrl,
                LogoUrl = listing.LogoUrl,
                SupportsWebhook = listing.SupportsWebhook,
                WebhookFirst = listing.WebhookFirst,
                FallbackPollingMinutes = listing.FallbackPollingMinutes,
                SlaTier = listing.SlaTier,
                SlaResponseHours = listing.SlaResponseHours,
                SlaResolutionHours = listing.SlaResolutionHours,
                SlaUptimePercent = listing.SlaUptimePercent,
                Status = listing.Status,
                SubmissionCount = listing.SubmissionCount,
                LastSubmittedAt = listing.LastSubmittedAt,
                LastTestStatus = listing.LastTestStatus,
                LastTestedAt = listing.LastTestedAt,
                LastTestSummary = listing.LastTestSummary,
                ReviewNotes = listing.ReviewNotes,
                ReviewedBy = listing.ReviewedBy,
                ReviewedAt = listing.ReviewedAt,
                IsFeatured = listing.IsFeatured,
                PublishedAt = listing.PublishedAt,
                UpdatedAt = listing.UpdatedAt
            };
        }

        private static Expression<Func<AppDirectoryListing, ListingDto>> MapListingProjection()
        {
            return listing => new ListingDto
            {
                Id = listing.Id,
                ProviderKey = listing.ProviderKey,
                Name = listing.Name,
                Category = listing.Category,
                ConnectionMode = listing.ConnectionMode,
                Summary = listing.Summary,
                Description = listing.Description,
                ManifestVersion = listing.ManifestVersion,
                WebsiteUrl = listing.WebsiteUrl,
                DocumentationUrl = listing.DocumentationUrl,
                SupportEmail = listing.SupportEmail,
                SupportUrl = listing.SupportUrl,
                LogoUrl = listing.LogoUrl,
                SupportsWebhook = listing.SupportsWebhook,
                WebhookFirst = listing.WebhookFirst,
                FallbackPollingMinutes = listing.FallbackPollingMinutes,
                SlaTier = listing.SlaTier,
                SlaResponseHours = listing.SlaResponseHours,
                SlaResolutionHours = listing.SlaResolutionHours,
                SlaUptimePercent = listing.SlaUptimePercent,
                Status = listing.Status,
                SubmissionCount = listing.SubmissionCount,
                LastSubmittedAt = listing.LastSubmittedAt,
                LastTestStatus = listing.LastTestStatus,
                LastTestedAt = listing.LastTestedAt,
                LastTestSummary = listing.LastTestSummary,
                ReviewNotes = listing.ReviewNotes,
                ReviewedBy = listing.ReviewedBy,
                ReviewedAt = listing.ReviewedAt,
                IsFeatured = listing.IsFeatured,
                PublishedAt = listing.PublishedAt,
                UpdatedAt = listing.UpdatedAt
            };
        }

        private static HarnessDto MapHarness(AppDirectoryHarnessResult harness)
        {
            return new HarnessDto
            {
                Passed = harness.Passed,
                ErrorCount = harness.ErrorCount,
                WarningCount = harness.WarningCount,
                Summary = harness.Summary,
                Checks = harness.Checks.Select(c => new HarnessCheckDto
                {
                    Key = c.Key,
                    Severity = c.Severity,
                    Passed = c.Passed,
                    Message = c.Message
                }).ToList()
            };
        }

        private string? ResolveUserId()
        {
            return User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                   ?? User.FindFirst("sub")?.Value
                   ?? User.FindFirst("clientId")?.Value;
        }

        private bool IsStaff()
        {
            return User.IsInRole("Admin") ||
                   User.IsInRole("SecurityAdmin") ||
                   User.IsInRole("Partner") ||
                   User.IsInRole("Associate") ||
                   User.IsInRole("Employee") ||
                   User.IsInRole("Staff") ||
                   User.IsInRole("Manager") ||
                   User.IsInRole("Attorney");
        }

        private bool IsAdmin()
        {
            return User.IsInRole("Admin");
        }

        private static string? NormalizeOptional(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static bool IsReviewTransitionAllowed(string currentStatus, string decision)
        {
            var current = NormalizeOptional(currentStatus)?.ToLowerInvariant() ?? "draft";
            return decision switch
            {
                "approve" => current is "submitted" or "in_review" or "changes_requested" or "approved" or "suspended" or "rejected" or "draft",
                "reject" => current is "submitted" or "in_review" or "changes_requested" or "approved" or "suspended" or "draft",
                "request_changes" => current is "submitted" or "in_review" or "approved" or "suspended" or "draft",
                "suspend" => current is "published" or "approved" or "in_review" or "submitted" or "changes_requested",
                _ => false
            };
        }

        private List<string> ValidateOnboardingInput(AppDirectoryManifest manifest)
        {
            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(manifest.ProviderKey) || manifest.ProviderKey.Length > 128)
            {
                errors.Add("ProviderKey is required and must be <= 128 characters.");
            }

            if (string.IsNullOrWhiteSpace(manifest.Name) || manifest.Name.Length > 160)
            {
                errors.Add("Name is required and must be <= 160 characters.");
            }

            if (string.IsNullOrWhiteSpace(manifest.Category) || manifest.Category.Length > 64)
            {
                errors.Add("Category is required and must be <= 64 characters.");
            }

            if (string.IsNullOrWhiteSpace(manifest.ConnectionMode) || manifest.ConnectionMode.Length > 16)
            {
                errors.Add("ConnectionMode is required and must be <= 16 characters.");
            }

            if (string.IsNullOrWhiteSpace(manifest.Summary) || manifest.Summary.Length > 512)
            {
                errors.Add("Summary is required and must be <= 512 characters.");
            }

            if (!string.IsNullOrWhiteSpace(manifest.Description) && manifest.Description.Length > 4000)
            {
                errors.Add("Description must be <= 4000 characters.");
            }

            if (string.IsNullOrWhiteSpace(manifest.ManifestVersion) || manifest.ManifestVersion.Length > 32)
            {
                errors.Add("ManifestVersion is required and must be <= 32 characters.");
            }

            if (!string.IsNullOrWhiteSpace(manifest.WebsiteUrl) && manifest.WebsiteUrl.Length > 512)
            {
                errors.Add("WebsiteUrl must be <= 512 characters.");
            }

            if (!string.IsNullOrWhiteSpace(manifest.DocumentationUrl) && manifest.DocumentationUrl.Length > 512)
            {
                errors.Add("DocumentationUrl must be <= 512 characters.");
            }

            if (!string.IsNullOrWhiteSpace(manifest.SupportEmail) && manifest.SupportEmail.Length > 256)
            {
                errors.Add("SupportEmail must be <= 256 characters.");
            }

            if (!string.IsNullOrWhiteSpace(manifest.SupportUrl) && manifest.SupportUrl.Length > 512)
            {
                errors.Add("SupportUrl must be <= 512 characters.");
            }

            if (!string.IsNullOrWhiteSpace(manifest.LogoUrl) && manifest.LogoUrl.Length > 512)
            {
                errors.Add("LogoUrl must be <= 512 characters.");
            }

            if (manifest.Capabilities.Count > 128)
            {
                errors.Add("Capabilities cannot exceed 128 entries.");
            }

            if (manifest.Capabilities.Any(c => !string.IsNullOrWhiteSpace(c) && c.Length > 128))
            {
                errors.Add("Each capability must be <= 128 characters.");
            }

            if (manifest.ConfigurationHints != null)
            {
                if (manifest.ConfigurationHints.Count > 128)
                {
                    errors.Add("ConfigurationHints cannot exceed 128 entries.");
                }

                if (manifest.ConfigurationHints.Any(kv => kv.Key.Length > 128 || kv.Value.Length > 1024))
                {
                    errors.Add("ConfigurationHints key/value size is too large.");
                }
            }

            return errors;
        }

        private static List<string> ValidateJsonPayloadSizes(string manifestJson, string validationErrorsJson, string harnessJson)
        {
            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(manifestJson))
            {
                errors.Add("ManifestJson cannot be empty.");
            }
            else if (manifestJson.Length > MaxManifestJsonLength)
            {
                errors.Add($"ManifestJson exceeds max size ({MaxManifestJsonLength} chars).");
            }

            if (!string.IsNullOrWhiteSpace(validationErrorsJson) && validationErrorsJson.Length > MaxValidationErrorsJsonLength)
            {
                errors.Add($"ValidationErrorsJson exceeds max size ({MaxValidationErrorsJsonLength} chars).");
            }

            if (!string.IsNullOrWhiteSpace(harnessJson) && harnessJson.Length > MaxHarnessJsonLength)
            {
                errors.Add($"TestReportJson exceeds max size ({MaxHarnessJsonLength} chars).");
            }

            return errors;
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

        public sealed class OnboardingSubmissionRequest
        {
            public AppDirectoryManifest? Manifest { get; set; }
            public AppDirectorySlaProfile? Sla { get; set; }
        }

        public sealed class OnboardingSubmissionResponse
        {
            public ListingDto Listing { get; set; } = new();
            public HarnessDto Harness { get; set; } = new();
        }

        public sealed class ListingReviewRequest
        {
            public string Decision { get; set; } = string.Empty;
            public bool Publish { get; set; }
            public bool? IsFeatured { get; set; }
            public string? Notes { get; set; }
            public ReviewSlaDto? Sla { get; set; }
        }

        public sealed class ReviewSlaDto
        {
            public string? Tier { get; set; }
            public int? ResponseHours { get; set; }
            public int? ResolutionHours { get; set; }
            public double? UptimePercent { get; set; }
        }

        public sealed class ListingDto
        {
            public string Id { get; set; } = string.Empty;
            public string ProviderKey { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string Category { get; set; } = string.Empty;
            public string ConnectionMode { get; set; } = string.Empty;
            public string Summary { get; set; } = string.Empty;
            public string? Description { get; set; }
            public string ManifestVersion { get; set; } = string.Empty;
            public string? WebsiteUrl { get; set; }
            public string? DocumentationUrl { get; set; }
            public string? SupportEmail { get; set; }
            public string? SupportUrl { get; set; }
            public string? LogoUrl { get; set; }
            public bool SupportsWebhook { get; set; }
            public bool WebhookFirst { get; set; }
            public int? FallbackPollingMinutes { get; set; }
            public string SlaTier { get; set; } = string.Empty;
            public int? SlaResponseHours { get; set; }
            public int? SlaResolutionHours { get; set; }
            public double? SlaUptimePercent { get; set; }
            public string Status { get; set; } = string.Empty;
            public int SubmissionCount { get; set; }
            public DateTime? LastSubmittedAt { get; set; }
            public string LastTestStatus { get; set; } = string.Empty;
            public DateTime? LastTestedAt { get; set; }
            public string? LastTestSummary { get; set; }
            public string? ReviewNotes { get; set; }
            public string? ReviewedBy { get; set; }
            public DateTime? ReviewedAt { get; set; }
            public bool IsFeatured { get; set; }
            public DateTime? PublishedAt { get; set; }
            public DateTime UpdatedAt { get; set; }
        }

        public sealed class HarnessDto
        {
            public bool Passed { get; set; }
            public int ErrorCount { get; set; }
            public int WarningCount { get; set; }
            public string Summary { get; set; } = string.Empty;
            public List<HarnessCheckDto> Checks { get; set; } = new();
        }

        public sealed class HarnessCheckDto
        {
            public string Key { get; set; } = string.Empty;
            public string Severity { get; set; } = string.Empty;
            public bool Passed { get; set; }
            public string Message { get; set; } = string.Empty;
        }

        public sealed class SubmissionDto
        {
            public string Id { get; set; } = string.Empty;
            public string ListingId { get; set; } = string.Empty;
            public string SubmittedBy { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;
            public string TestStatus { get; set; } = string.Empty;
            public DateTime? StartedAt { get; set; }
            public DateTime? CompletedAt { get; set; }
            public DateTime CreatedAt { get; set; }
        }
    }
}
