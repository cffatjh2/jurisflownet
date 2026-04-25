using Task = System.Threading.Tasks.Task;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using JurisFlow.Server.Services;

namespace JurisFlow.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Policy = "StaffOnly")]
    public class SignaturesController : ControllerBase
    {
        private readonly JurisFlowDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly AuditLogger _auditLogger;
        private readonly SignatureAuditTrailService _signatureAuditTrailService;
        private readonly OutboundEmailService _outboundEmailService;

        public SignaturesController(
            JurisFlowDbContext context,
            IConfiguration configuration,
            AuditLogger auditLogger,
            SignatureAuditTrailService signatureAuditTrailService,
            OutboundEmailService outboundEmailService)
        {
            _context = context;
            _configuration = configuration;
            _auditLogger = auditLogger;
            _signatureAuditTrailService = signatureAuditTrailService;
            _outboundEmailService = outboundEmailService;
        }

        // POST: api/signatures/request
        [HttpPost("request")]
        public async Task<ActionResult<SignatureRequest>> CreateSignatureRequest([FromBody] CreateSignatureRequestDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.DocumentId) || string.IsNullOrWhiteSpace(dto.SignerEmail))
            {
                return BadRequest(new { message = "DocumentId and signerEmail are required." });
            }

            var document = await _context.Documents.FirstOrDefaultAsync(d => d.Id == dto.DocumentId);
            if (document == null)
            {
                return NotFound(new { message = "Document not found" });
            }

            var userId = User.FindFirst("sub")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var verificationMethod = ResolveVerificationMethod(dto.VerificationMethod, dto.RequiresKba);
            var defaultExpiryDays = _configuration.GetValue("Signatures:DefaultExpiryDays", 30);

            var signatureRequest = new SignatureRequest
            {
                DocumentId = dto.DocumentId,
                SignerEmail = dto.SignerEmail,
                SignerName = dto.SignerName ?? "",
                MatterId = document.MatterId,
                ClientId = dto.ClientId,
                Status = "Pending",
                ExpiresAt = dto.ExpiresAt ?? DateTime.UtcNow.AddDays(defaultExpiryDays),
                RequestedBy = userId,
                RequiresKba = string.Equals(verificationMethod, "Kba", StringComparison.OrdinalIgnoreCase),
                VerificationMethod = verificationMethod,
                VerificationStatus = string.Equals(verificationMethod, "None", StringComparison.OrdinalIgnoreCase)
                    ? "NotRequired"
                    : "Pending"
            };

            var signingBaseUrl = _configuration["Signatures:SigningBaseUrl"];
            if (string.IsNullOrWhiteSpace(signingBaseUrl))
            {
                signingBaseUrl = $"{Request.Scheme}://{Request.Host}";
            }

            signatureRequest.SigningUrl = $"{signingBaseUrl.TrimEnd('/')}/sign/{signatureRequest.Id}";
            signatureRequest.Status = "Sent";
            signatureRequest.SentAt = DateTime.UtcNow;

            _context.SignatureRequests.Add(signatureRequest);
            await _context.SaveChangesAsync();

            await _outboundEmailService.QueueAsync(new OutboundEmail
            {
                ToAddress = signatureRequest.SignerEmail,
                Subject = $"Signature requested for {document.Name}",
                BodyText = $"Please review and sign \"{document.Name}\" at {signatureRequest.SigningUrl}. This request expires on {signatureRequest.ExpiresAt:yyyy-MM-dd}.",
                ScheduledFor = DateTime.UtcNow,
                RelatedEntityType = "SignatureRequest",
                RelatedEntityId = signatureRequest.Id
            });

            await _auditLogger.LogAsync(HttpContext, "esign.request.create", "SignatureRequest", signatureRequest.Id, $"Signer={signatureRequest.SignerEmail}, Document={signatureRequest.DocumentId}");
            if (dto.DisclosureProvided == true)
            {
                await _signatureAuditTrailService.LogAsync(
                    HttpContext,
                    signatureRequest,
                    "DisclosureProvided",
                    "User",
                    userId,
                    User.Identity?.Name,
                    new { dto.DisclosureVersion });
            }
            await _signatureAuditTrailService.LogAsync(HttpContext, signatureRequest, "Requested", "User", userId, User.Identity?.Name);
            await _signatureAuditTrailService.LogAsync(HttpContext, signatureRequest, "Sent", "System", null, signatureRequest.SignerEmail);

            return Ok(signatureRequest);
        }

        // GET: api/signatures/{id}
        [HttpGet("{id}")]
        public async Task<ActionResult<SignatureRequest>> GetSignatureRequest(string id)
        {
            var request = await _context.SignatureRequests.FindAsync(id);
            if (request == null)
            {
                return NotFound();
            }

            return Ok(request);
        }

        // GET: api/signatures/document/{documentId}
        [HttpGet("document/{documentId}")]
        public async Task<ActionResult<IEnumerable<SignatureRequest>>> GetDocumentSignatures(string documentId)
        {
            var requests = await _context.SignatureRequests
                .Where(r => r.DocumentId == documentId)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();

            return Ok(requests);
        }

        // GET: api/signatures/matter/{matterId}
        [HttpGet("matter/{matterId}")]
        public async Task<ActionResult<IEnumerable<SignatureRequest>>> GetMatterSignatures(string matterId)
        {
            var requests = await _context.SignatureRequests
                .Where(r => r.MatterId == matterId)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();

            return Ok(requests);
        }

        // POST: api/signatures/{id}/sign
        [HttpPost("{id}/sign")]
        public async Task<IActionResult> SignDocument(string id, [FromBody] SignRequestDto dto)
        {
            var request = await _context.SignatureRequests.FindAsync(id);
            if (request == null)
            {
                return NotFound();
            }

            if (IsExpired(request))
            {
                await MarkExpiredAsync(request);
                return BadRequest(new { message = "Signature request has expired." });
            }

            if (request.Status == "Signed")
            {
                return BadRequest(new { message = "Document already signed" });
            }

            if (RequiresVerification(request) && !IsVerificationPassed(request))
            {
                if (IsEmailLink(request))
                {
                    request.VerificationStatus = "Passed";
                    request.VerificationCompletedAt = DateTime.UtcNow;
                }
                else
                {
                    return BadRequest(new { message = "Signer verification is required before signing." });
                }
            }

            request.Status = "Signed";
            request.SignedAt = DateTime.UtcNow;
            request.UpdatedAt = DateTime.UtcNow;
            request.SignerIp = HttpContext.Connection.RemoteIpAddress?.ToString();
            request.SignerUserAgent = Request.Headers.UserAgent.ToString();
            request.SignerLocation = dto.SignerLocation;

            await _context.SaveChangesAsync();
            await _auditLogger.LogAsync(HttpContext, "esign.sign", "SignatureRequest", id, $"Signer={request.SignerEmail}, Ip={request.SignerIp}");
            if (dto.ConsentAccepted == true)
            {
                await _signatureAuditTrailService.LogAsync(HttpContext, request, "ConsentProvided", "Signer", null, request.SignerEmail, new { dto.ConsentVersion });
            }
            await _signatureAuditTrailService.LogAsync(HttpContext, request, "Signed", "Signer", null, request.SignerEmail);

            return Ok(new { message = "Document signed successfully", signedAt = request.SignedAt });
        }

        // POST: api/signatures/{id}/decline
        [HttpPost("{id}/decline")]
        public async Task<IActionResult> DeclineSignature(string id, [FromBody] DeclineSignatureDto dto)
        {
            var request = await _context.SignatureRequests.FindAsync(id);
            if (request == null)
            {
                return NotFound();
            }

            if (IsExpired(request))
            {
                await MarkExpiredAsync(request);
                return BadRequest(new { message = "Signature request has expired." });
            }

            request.Status = "Declined";
            request.DeclineReason = dto.Reason;
            request.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await _auditLogger.LogAsync(HttpContext, "esign.decline", "SignatureRequest", id, $"Reason={dto.Reason}");
            await _signatureAuditTrailService.LogAsync(HttpContext, request, "Declined", "Signer", null, request.SignerEmail, new { dto.Reason });

            return Ok(new { message = "Signature declined" });
        }

        // POST: api/signatures/{id}/remind
        [HttpPost("{id}/remind")]
        public async Task<IActionResult> SendReminder(string id)
        {
            var request = await _context.SignatureRequests.FindAsync(id);
            if (request == null)
            {
                return NotFound();
            }

            if (IsExpired(request))
            {
                await MarkExpiredAsync(request);
                return BadRequest(new { message = "Signature request has expired." });
            }

            if (request.Status != "Sent" && request.Status != "Viewed")
            {
                return BadRequest(new { message = "Cannot send reminder for this status" });
            }

            if (!string.IsNullOrWhiteSpace(request.SignerEmail))
            {
                await _outboundEmailService.QueueAsync(new OutboundEmail
                {
                    ToAddress = request.SignerEmail,
                    Subject = "Signature reminder",
                    BodyText = $"Please review and sign document request {request.Id}.",
                    ScheduledFor = DateTime.UtcNow,
                    RelatedEntityType = "SignatureRequest",
                    RelatedEntityId = request.Id
                });
            }

            request.ReminderCount += 1;
            request.LastReminderAt = DateTime.UtcNow;
            request.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            await _signatureAuditTrailService.LogAsync(HttpContext, request, "ReminderQueued", "User", null, User.Identity?.Name, new { request.ReminderCount });

            return Ok(new { message = "Reminder sent" });
        }

        // POST: api/signatures/{id}/void
        [HttpPost("{id}/void")]
        public async Task<IActionResult> VoidSignatureRequest(string id)
        {
            var request = await _context.SignatureRequests.FindAsync(id);
            if (request == null)
            {
                return NotFound();
            }

            if (IsExpired(request))
            {
                await MarkExpiredAsync(request);
                return BadRequest(new { message = "Signature request has expired." });
            }

            if (request.Status == "Signed")
            {
                return BadRequest(new { message = "Cannot void a signed document" });
            }

            request.Status = "Voided";
            request.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await _auditLogger.LogAsync(HttpContext, "esign.void", "SignatureRequest", id, $"RequestedBy={User.Identity?.Name}");
            await _signatureAuditTrailService.LogAsync(HttpContext, request, "Voided", "User", null, User.Identity?.Name);

            return Ok(new { message = "Signature request voided" });
        }

        // POST: api/signatures/{id}/view
        [HttpPost("{id}/view")]
        public async Task<IActionResult> MarkViewed(string id)
        {
            var request = await _context.SignatureRequests.FindAsync(id);
            if (request == null)
            {
                return NotFound();
            }

            if (IsExpired(request))
            {
                await MarkExpiredAsync(request);
                return BadRequest(new { message = "Signature request has expired." });
            }

            if (request.Status == "Sent")
            {
                request.Status = "Viewed";
            }

            request.ViewedAt = DateTime.UtcNow;
            request.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            await _signatureAuditTrailService.LogAsync(HttpContext, request, "Viewed", "Signer", null, request.SignerEmail);

            return Ok(new { message = "Signature request viewed." });
        }

        // POST: api/signatures/{id}/verify
        [HttpPost("{id}/verify")]
        public async Task<IActionResult> VerifySignature(string id, [FromBody] VerifySignatureDto dto)
        {
            var request = await _context.SignatureRequests.FindAsync(id);
            if (request == null)
            {
                return NotFound();
            }

            if (IsExpired(request))
            {
                await MarkExpiredAsync(request);
                return BadRequest(new { message = "Signature request has expired." });
            }

            if (!RequiresVerification(request))
            {
                return BadRequest(new { message = "Verification is not required for this request." });
            }

            var method = NormalizeVerificationMethod(dto.Method);
            if (!string.Equals(NormalizeVerificationMethod(request.VerificationMethod), method, StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { message = "Verification method does not match the request." });
            }

            request.VerificationStatus = dto.Passed ? "Passed" : "Failed";
            request.VerificationCompletedAt = DateTime.UtcNow;
            request.VerificationNotes = dto.Notes;
            request.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            await _signatureAuditTrailService.LogAsync(
                HttpContext,
                request,
                "Verified",
                "Signer",
                null,
                request.SignerEmail,
                new { dto.Passed, dto.Method });

            return Ok(new { message = "Verification recorded.", status = request.VerificationStatus });
        }

        // GET: api/signatures/{id}/audit-trail
        [HttpGet("{id}/audit-trail")]
        public async Task<IActionResult> GetAuditTrail(string id)
        {
            var exists = await _context.SignatureRequests.AnyAsync(r => r.Id == id);
            if (!exists)
            {
                return NotFound();
            }

            var entries = await _context.SignatureAuditEntries
                .Where(a => a.SignatureRequestId == id)
                .OrderBy(a => a.CreatedAt)
                .ToListAsync();

            return Ok(entries);
        }

        // POST: api/signatures/webhook (DocuSign webhook endpoint)
        [HttpPost("webhook")]
        [AllowAnonymous]
        public async Task<IActionResult> HandleWebhook([FromBody] SignatureWebhookDto dto)
        {
            if (dto == null)
            {
                return BadRequest(new { message = "Webhook payload is required." });
            }

            var request = await ResolveWebhookSignatureRequestAsync(dto);
            if (request == null)
            {
                return NotFound(new { message = "Signature request not found for webhook payload." });
            }

            var now = DateTime.UtcNow;
            var normalizedStatus = NormalizeWebhookStatus(dto.Status, dto.EventType);
            if (string.IsNullOrWhiteSpace(normalizedStatus))
            {
                return BadRequest(new { message = "Webhook status is missing or unsupported." });
            }

            switch (normalizedStatus)
            {
                case "Viewed":
                    if (request.Status == "Sent")
                    {
                        request.Status = "Viewed";
                    }
                    request.ViewedAt ??= dto.EventTimeUtc ?? now;
                    break;
                case "Signed":
                    request.Status = "Signed";
                    request.SignedAt = dto.EventTimeUtc ?? now;
                    break;
                case "Declined":
                    request.Status = "Declined";
                    request.DeclineReason = dto.DeclineReason;
                    break;
                case "Voided":
                    request.Status = "Voided";
                    break;
                case "Expired":
                    request.Status = "Expired";
                    request.ExpiredAt = dto.EventTimeUtc ?? now;
                    break;
                case "Sent":
                    request.Status = "Sent";
                    request.SentAt ??= dto.EventTimeUtc ?? now;
                    break;
            }

            if (!string.IsNullOrWhiteSpace(dto.ExternalEnvelopeId))
            {
                request.ExternalEnvelopeId = dto.ExternalEnvelopeId;
            }

            request.UpdatedAt = now;
            await _context.SaveChangesAsync();
            await _signatureAuditTrailService.LogAsync(
                HttpContext,
                request,
                $"Webhook:{normalizedStatus}",
                "System",
                null,
                dto.Provider ?? "external",
                new { dto.EventType, dto.Status, dto.ExternalEnvelopeId, dto.DeclineReason });

            return Ok(new { message = "Webhook processed.", status = request.Status });
        }

        private static bool RequiresVerification(SignatureRequest request)
        {
            var method = NormalizeVerificationMethod(request.VerificationMethod);
            return !string.Equals(method, "None", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsVerificationPassed(SignatureRequest request)
        {
            return string.Equals(request.VerificationStatus, "Passed", StringComparison.OrdinalIgnoreCase)
                || string.Equals(request.VerificationStatus, "NotRequired", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsEmailLink(SignatureRequest request)
        {
            return string.Equals(NormalizeVerificationMethod(request.VerificationMethod), "EmailLink", StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveVerificationMethod(string? method, bool? requiresKba)
        {
            if (requiresKba == true)
            {
                return "Kba";
            }

            return NormalizeVerificationMethod(method);
        }

        private static string NormalizeVerificationMethod(string? method)
        {
            if (string.IsNullOrWhiteSpace(method))
            {
                return "EmailLink";
            }

            var normalized = method.Trim().ToLowerInvariant();
            return normalized switch
            {
                "kba" => "Kba",
                "sms" => "SmsOtp",
                "smsotp" => "SmsOtp",
                "email" => "EmailLink",
                "emaillink" => "EmailLink",
                "none" => "None",
                _ => "EmailLink"
            };
        }

        private static bool IsExpired(SignatureRequest request)
        {
            return request.ExpiresAt.HasValue && request.ExpiresAt.Value <= DateTime.UtcNow;
        }

        private async Task<SignatureRequest?> ResolveWebhookSignatureRequestAsync(SignatureWebhookDto dto)
        {
            if (!string.IsNullOrWhiteSpace(dto.RequestId))
            {
                return await _context.SignatureRequests.FirstOrDefaultAsync(r => r.Id == dto.RequestId);
            }

            if (!string.IsNullOrWhiteSpace(dto.ExternalEnvelopeId))
            {
                return await _context.SignatureRequests
                    .FirstOrDefaultAsync(r => r.ExternalEnvelopeId == dto.ExternalEnvelopeId);
            }

            return null;
        }

        private static string? NormalizeWebhookStatus(string? status, string? eventType)
        {
            var value = status ?? eventType;
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return value.Trim().ToLowerInvariant() switch
            {
                "sent" => "Sent",
                "delivered" => "Sent",
                "created" => "Sent",
                "viewed" => "Viewed",
                "opened" => "Viewed",
                "completed" => "Signed",
                "signed" => "Signed",
                "declined" => "Declined",
                "voided" => "Voided",
                "expired" => "Expired",
                _ => null
            };
        }

        private async Task MarkExpiredAsync(SignatureRequest request)
        {
            if (request.Status == "Expired")
            {
                return;
            }

            request.Status = "Expired";
            request.ExpiredAt = DateTime.UtcNow;
            request.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            await _signatureAuditTrailService.LogAsync(HttpContext, request, "Expired", "System", null, request.SignerEmail);
        }
    }

    // DTOs
    public class CreateSignatureRequestDto
    {
        public string DocumentId { get; set; } = string.Empty;
        public string SignerEmail { get; set; } = string.Empty;
        public string? SignerName { get; set; }
        public string? ClientId { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public string? VerificationMethod { get; set; }
        public bool? RequiresKba { get; set; }
        public bool? DisclosureProvided { get; set; }
        public string? DisclosureVersion { get; set; }
    }

    public class DeclineSignatureDto
    {
        public string? Reason { get; set; }
    }

    public class SignRequestDto
    {
        public string? SignerLocation { get; set; }
        public bool? ConsentAccepted { get; set; }
        public string? ConsentVersion { get; set; }
    }

    public class VerifySignatureDto
    {
        public string? Method { get; set; }
        public bool Passed { get; set; }
        public string? Notes { get; set; }
    }

    public class SignatureWebhookDto
    {
        public string? RequestId { get; set; }
        public string? ExternalEnvelopeId { get; set; }
        public string? Status { get; set; }
        public string? EventType { get; set; }
        public DateTime? EventTimeUtc { get; set; }
        public string? DeclineReason { get; set; }
        public string? Provider { get; set; }
    }
}
