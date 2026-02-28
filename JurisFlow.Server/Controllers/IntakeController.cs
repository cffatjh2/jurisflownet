using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using JurisFlow.Server.Services;
using System.Text.Json;
using Task = System.Threading.Tasks.Task;

namespace JurisFlow.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Policy = "StaffOnly")]
    public class IntakeController : ControllerBase
    {
        private readonly JurisFlowDbContext _context;
        private readonly OutboundEmailService _outboundEmailService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<IntakeController> _logger;

        public IntakeController(
            JurisFlowDbContext context,
            OutboundEmailService outboundEmailService,
            IConfiguration configuration,
            ILogger<IntakeController> logger)
        {
            _context = context;
            _outboundEmailService = outboundEmailService;
            _configuration = configuration;
            _logger = logger;
        }

        // ========== FORMS MANAGEMENT ==========

        // GET: api/intake/forms
        [HttpGet("forms")]
        public async Task<ActionResult<IEnumerable<IntakeForm>>> GetForms([FromQuery] bool activeOnly = true)
        {
            var query = _context.IntakeForms.AsQueryable();

            if (activeOnly)
            {
                query = query.Where(f => f.IsActive);
            }

            var forms = await query.OrderBy(f => f.Name).ToListAsync();
            return Ok(forms);
        }

        // GET: api/intake/forms/{id}
        [HttpGet("forms/{id}")]
        public async Task<ActionResult<IntakeForm>> GetForm(string id)
        {
            var form = await _context.IntakeForms.FindAsync(id);
            if (form == null)
            {
                return NotFound();
            }

            return Ok(form);
        }

        // POST: api/intake/forms
        [HttpPost("forms")]
        public async Task<ActionResult<IntakeForm>> CreateForm([FromBody] CreateIntakeFormDto dto)
        {
            // Generate unique slug
            var baseSlug = GenerateSlug(dto.Name);
            var slug = baseSlug;
            var counter = 1;

            while (await _context.IntakeForms.AnyAsync(f => f.Slug == slug))
            {
                slug = $"{baseSlug}-{counter}";
                counter++;
            }

            var form = new IntakeForm
            {
                Name = dto.Name,
                Description = dto.Description,
                PracticeArea = dto.PracticeArea,
                FieldsJson = dto.FieldsJson ?? "[]",
                StyleJson = dto.StyleJson,
                ThankYouMessage = dto.ThankYouMessage ?? "Thank you for your submission. We will contact you shortly.",
                RedirectUrl = dto.RedirectUrl,
                NotifyEmail = dto.NotifyEmail,
                AssignToEmployeeId = dto.AssignToEmployeeId,
                IsPublic = dto.IsPublic ?? true,
                Slug = slug
            };

            _context.IntakeForms.Add(form);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetForm), new { id = form.Id }, form);
        }

        // PUT: api/intake/forms/{id}
        [HttpPut("forms/{id}")]
        public async Task<IActionResult> UpdateForm(string id, [FromBody] UpdateIntakeFormDto dto)
        {
            var form = await _context.IntakeForms.FindAsync(id);
            if (form == null)
            {
                return NotFound();
            }

            if (dto.Name != null) form.Name = dto.Name;
            if (dto.Description != null) form.Description = dto.Description;
            if (dto.PracticeArea != null) form.PracticeArea = dto.PracticeArea;
            if (dto.FieldsJson != null) form.FieldsJson = dto.FieldsJson;
            if (dto.StyleJson != null) form.StyleJson = dto.StyleJson;
            if (dto.ThankYouMessage != null) form.ThankYouMessage = dto.ThankYouMessage;
            if (dto.RedirectUrl != null) form.RedirectUrl = dto.RedirectUrl;
            if (dto.NotifyEmail != null) form.NotifyEmail = dto.NotifyEmail;
            if (dto.AssignToEmployeeId != null) form.AssignToEmployeeId = dto.AssignToEmployeeId;
            if (dto.IsActive.HasValue) form.IsActive = dto.IsActive.Value;
            if (dto.IsPublic.HasValue) form.IsPublic = dto.IsPublic.Value;

            form.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return Ok(form);
        }

        // DELETE: api/intake/forms/{id}
        [HttpDelete("forms/{id}")]
        public async Task<IActionResult> DeleteForm(string id)
        {
            var form = await _context.IntakeForms.FindAsync(id);
            if (form == null)
            {
                return NotFound();
            }

            // Soft delete
            form.IsActive = false;
            form.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return NoContent();
        }

        // ========== PUBLIC FORM SUBMISSION ==========

        // GET: api/intake/public/{slug}
        [HttpGet("public/{slug}")]
        [AllowAnonymous]
        public async Task<ActionResult<IntakeForm>> GetPublicForm(string slug)
        {
            var form = await _context.IntakeForms
                .FirstOrDefaultAsync(f => f.Slug == slug && f.IsActive && f.IsPublic);

            if (form == null)
            {
                return NotFound(new { message = "Form not found or inactive" });
            }

            // Return only public-safe data
            return Ok(new
            {
                form.Id,
                form.Name,
                form.Description,
                form.FieldsJson,
                form.StyleJson,
                form.PracticeArea
            });
        }

        // POST: api/intake/public/{slug}/submit
        [HttpPost("public/{slug}/submit")]
        [AllowAnonymous]
        public async Task<IActionResult> SubmitForm(string slug, [FromBody] SubmitFormDto dto)
        {
            var form = await _context.IntakeForms
                .FirstOrDefaultAsync(f => f.Slug == slug && f.IsActive && f.IsPublic);

            if (form == null)
            {
                return NotFound(new { message = "Form not found or inactive" });
            }

            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            var userAgent = Request.Headers["User-Agent"].FirstOrDefault();

            var submission = new IntakeSubmission
            {
                IntakeFormId = form.Id,
                DataJson = dto.DataJson,
                IpAddress = ipAddress,
                UserAgent = userAgent,
                Status = "New"
            };

            _context.IntakeSubmissions.Add(submission);

            // Update form submission count
            form.SubmissionCount++;

            await _context.SaveChangesAsync();

            await QueueSubmissionNotificationAsync(form, submission, ipAddress, userAgent);
            await AutoCreateLeadIfConfiguredAsync(form, submission);

            return Ok(new
            {
                message = form.ThankYouMessage,
                redirectUrl = form.RedirectUrl,
                submissionId = submission.Id
            });
        }

        // ========== SUBMISSIONS MANAGEMENT ==========

        // GET: api/intake/submissions
        [HttpGet("submissions")]
        public async Task<ActionResult<IEnumerable<IntakeSubmission>>> GetSubmissions(
            [FromQuery] string? formId = null,
            [FromQuery] string? status = null,
            [FromQuery] int limit = 50)
        {
            var query = _context.IntakeSubmissions.AsQueryable();

            if (!string.IsNullOrEmpty(formId))
            {
                query = query.Where(s => s.IntakeFormId == formId);
            }

            if (!string.IsNullOrEmpty(status))
            {
                query = query.Where(s => s.Status == status);
            }

            var submissions = await query
                .OrderByDescending(s => s.CreatedAt)
                .Take(limit)
                .ToListAsync();

            return Ok(submissions);
        }

        // GET: api/intake/submissions/{id}
        [HttpGet("submissions/{id}")]
        public async Task<ActionResult<IntakeSubmission>> GetSubmission(string id)
        {
            var submission = await _context.IntakeSubmissions.FindAsync(id);
            if (submission == null)
            {
                return NotFound();
            }

            return Ok(submission);
        }

        // POST: api/intake/submissions/{id}/review
        [HttpPost("submissions/{id}/review")]
        public async Task<IActionResult> ReviewSubmission(string id, [FromBody] ReviewSubmissionDto dto)
        {
            var submission = await _context.IntakeSubmissions.FindAsync(id);
            if (submission == null)
            {
                return NotFound();
            }

            var userId = User.FindFirst("sub")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

            submission.Status = dto.Status;
            submission.ReviewNotes = dto.Notes;
            submission.ReviewedBy = userId;
            submission.ReviewedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return Ok(new { message = "Submission reviewed" });
        }

        // POST: api/intake/submissions/{id}/convert-to-lead
        [HttpPost("submissions/{id}/convert-to-lead")]
        public async Task<IActionResult> ConvertToLead(string id)
        {
            var submission = await _context.IntakeSubmissions.FindAsync(id);
            if (submission == null)
            {
                return NotFound();
            }

            try
            {
                // Parse submission data
                var data = JsonSerializer.Deserialize<Dictionary<string, object>>(submission.DataJson);

                // Create lead from submission
                var lead = new Lead
                {
                    Name = data?.GetValueOrDefault("name")?.ToString() ?? data?.GetValueOrDefault("fullName")?.ToString() ?? "Unknown",
                    Email = data?.GetValueOrDefault("email")?.ToString(),
                    Phone = data?.GetValueOrDefault("phone")?.ToString(),
                    Source = "Intake Form",
                    Status = "New",
                    Notes = $"Submitted via intake form. Original data: {submission.DataJson}"
                };

                // Try to get practice area from form
                var form = await _context.IntakeForms.FindAsync(submission.IntakeFormId);
                if (form != null)
                {
                    lead.PracticeArea = form.PracticeArea;
                }

                _context.Leads.Add(lead);

                submission.LeadId = lead.Id;
                submission.Status = "Converted";

                await _context.SaveChangesAsync();

                return Ok(new { message = "Converted to lead", leadId = lead.Id });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = $"Failed to convert: {ex.Message}" });
            }
        }

        // DELETE: api/intake/submissions/{id}
        [HttpDelete("submissions/{id}")]
        public async Task<IActionResult> DeleteSubmission(string id)
        {
            var submission = await _context.IntakeSubmissions.FindAsync(id);
            if (submission == null)
            {
                return NotFound();
            }

            _context.IntakeSubmissions.Remove(submission);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        // ========== HELPERS ==========

        private string GenerateSlug(string name)
        {
            return name
                .ToLowerInvariant()
                .Replace(" ", "-")
                .Replace("'", "")
                .Replace("\"", "")
                .Replace(".", "")
                .Replace(",", "");
        }

        private async Task QueueSubmissionNotificationAsync(IntakeForm form, IntakeSubmission submission, string? ipAddress, string? userAgent)
        {
            if (string.IsNullOrWhiteSpace(form.NotifyEmail))
            {
                return;
            }

            try
            {
                var subject = $"New intake submission: {form.Name}";
                var body = $"""
Submission ID: {submission.Id}
Form: {form.Name}
Created At (UTC): {submission.CreatedAt:O}
IP Address: {ipAddress ?? "Unknown"}
User Agent: {userAgent ?? "Unknown"}

Payload:
{submission.DataJson}
""";

                await _outboundEmailService.QueueAsync(new OutboundEmail
                {
                    ToAddress = form.NotifyEmail,
                    Subject = subject,
                    BodyText = body,
                    ScheduledFor = DateTime.UtcNow,
                    RelatedEntityType = "IntakeSubmission",
                    RelatedEntityId = submission.Id
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to queue intake notification email for submission {SubmissionId}", submission.Id);
            }
        }

        private async Task AutoCreateLeadIfConfiguredAsync(IntakeForm form, IntakeSubmission submission)
        {
            var autoCreateLead = _configuration.GetValue("Intake:AutoCreateLeadOnPublicSubmit", false);
            if (!autoCreateLead || !string.IsNullOrWhiteSpace(submission.LeadId))
            {
                return;
            }

            Dictionary<string, JsonElement>? payload;
            try
            {
                payload = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(submission.DataJson);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Submission payload is invalid JSON for auto lead conversion. Submission {SubmissionId}", submission.Id);
                return;
            }

            var name = GetSubmissionValue(payload, "name", "fullName", "clientName") ?? "Website Intake Lead";
            var email = GetSubmissionValue(payload, "email");
            var phone = GetSubmissionValue(payload, "phone", "mobile");

            Lead? lead = null;
            if (!string.IsNullOrWhiteSpace(email))
            {
                lead = await _context.Leads
                    .FirstOrDefaultAsync(l => l.Email != null && l.Email.ToLower() == email.ToLower());
            }

            if (lead == null)
            {
                lead = new Lead
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = name,
                    Email = email,
                    Phone = phone,
                    Source = $"Intake:{form.Slug}",
                    Status = "New",
                    PracticeArea = form.PracticeArea,
                    Notes = $"Auto-created from intake submission {submission.Id}."
                };

                _context.Leads.Add(lead);
            }
            else
            {
                lead.Name = string.IsNullOrWhiteSpace(lead.Name) ? name : lead.Name;
                lead.Phone = string.IsNullOrWhiteSpace(lead.Phone) ? phone : lead.Phone;
                lead.PracticeArea ??= form.PracticeArea;
                lead.UpdatedAt = DateTime.UtcNow;
            }

            submission.LeadId = lead.Id;
            submission.Status = "Converted";
            await _context.SaveChangesAsync();
        }

        private static string? GetSubmissionValue(Dictionary<string, JsonElement>? payload, params string[] keys)
        {
            if (payload == null)
            {
                return null;
            }

            foreach (var key in keys)
            {
                if (!payload.TryGetValue(key, out var value))
                {
                    continue;
                }

                if (value.ValueKind == JsonValueKind.String)
                {
                    var text = value.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text.Trim();
                    }
                }
                else if (value.ValueKind != JsonValueKind.Null && value.ValueKind != JsonValueKind.Undefined)
                {
                    var text = value.ToString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text.Trim();
                    }
                }
            }

            return null;
        }
    }

    // DTOs
    public class CreateIntakeFormDto
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? PracticeArea { get; set; }
        public string? FieldsJson { get; set; }
        public string? StyleJson { get; set; }
        public string? ThankYouMessage { get; set; }
        public string? RedirectUrl { get; set; }
        public string? NotifyEmail { get; set; }
        public string? AssignToEmployeeId { get; set; }
        public bool? IsPublic { get; set; }
    }

    public class UpdateIntakeFormDto
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? PracticeArea { get; set; }
        public string? FieldsJson { get; set; }
        public string? StyleJson { get; set; }
        public string? ThankYouMessage { get; set; }
        public string? RedirectUrl { get; set; }
        public string? NotifyEmail { get; set; }
        public string? AssignToEmployeeId { get; set; }
        public bool? IsActive { get; set; }
        public bool? IsPublic { get; set; }
    }

    public class SubmitFormDto
    {
        public string DataJson { get; set; } = "{}";
    }

    public class ReviewSubmissionDto
    {
        public string Status { get; set; } = "Reviewed";
        public string? Notes { get; set; }
    }
}
