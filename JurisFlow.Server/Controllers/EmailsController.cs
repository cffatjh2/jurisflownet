using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using JurisFlow.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace JurisFlow.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Policy = "StaffOnly")]
    public class EmailsController : ControllerBase
    {
        private readonly JurisFlowDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly AuditLogger _auditLogger;
        private readonly IIntegrationSecretStore _secretStore;
        private readonly TenantContext _tenantContext;
        private readonly IDataProtector _oauthStateProtector;
        private readonly ILogger<EmailsController> _logger;

        public EmailsController(
            JurisFlowDbContext context,
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory,
            AuditLogger auditLogger,
            IIntegrationSecretStore secretStore,
            TenantContext tenantContext,
            IDataProtectionProvider dataProtectionProvider,
            ILogger<EmailsController> logger)
        {
            _context = context;
            _configuration = configuration;
            _httpClientFactory = httpClientFactory;
            _auditLogger = auditLogger;
            _secretStore = secretStore;
            _tenantContext = tenantContext;
            _oauthStateProtector = dataProtectionProvider.CreateProtector("EmailsController.OAuthState.v1");
            _logger = logger;
        }

        // GET: api/emails
        [HttpGet]
        public async Task<ActionResult<IEnumerable<EmailMessage>>> GetEmails(
            [FromQuery] string? matterId = null,
            [FromQuery] string? clientId = null,
            [FromQuery] string? folder = null,
            [FromQuery] int limit = 50)
        {
            var userId = GetUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Unauthorized();
            }

            if (!string.IsNullOrWhiteSpace(matterId) && !await MatterExistsAsync(matterId))
            {
                return NotFound(new { message = "Matter not found." });
            }

            if (!string.IsNullOrWhiteSpace(clientId) && !await ClientExistsAsync(clientId))
            {
                return NotFound(new { message = "Client not found." });
            }

            var accountIds = await GetUserAccountIdsAsync(userId);
            var query = AuthorizedEmailMessages(accountIds);

            if (!string.IsNullOrEmpty(matterId))
            {
                query = query.Where(e => e.MatterId == matterId);
            }

            if (!string.IsNullOrEmpty(clientId))
            {
                query = query.Where(e => e.ClientId == clientId);
            }

            if (!string.IsNullOrEmpty(folder))
            {
                query = query.Where(e => e.Folder == folder);
            }

            var emails = await query.AsNoTracking()
                .OrderByDescending(e => e.ReceivedAt)
                .Take(Math.Clamp(limit, 1, 200))
                .Select(e => new
                {
                    e.Id,
                    e.Subject,
                    e.FromAddress,
                    e.FromName,
                    e.ToAddresses,
                    e.Folder,
                    e.IsRead,
                    e.HasAttachments,
                    e.Importance,
                    e.ReceivedAt,
                    e.MatterId,
                    e.ClientId
                })
                .ToListAsync();

            return Ok(emails);
        }

        // GET: api/emails/{id}
        [HttpGet("{id}")]
        public async Task<ActionResult<EmailMessage>> GetEmail(string id)
        {
            var userId = GetUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Unauthorized();
            }

            var accountIds = await GetUserAccountIdsAsync(userId);
            var email = await AuthorizedEmailMessages(accountIds)
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == id);
            if (email == null)
            {
                return NotFound();
            }

            return Ok(new
            {
                email.Id,
                email.Subject,
                email.FromAddress,
                email.FromName,
                email.ToAddresses,
                email.CcAddresses,
                email.BccAddresses,
                email.BodyText,
                email.BodyHtml,
                email.Folder,
                email.IsRead,
                email.HasAttachments,
                email.AttachmentCount,
                email.Importance,
                email.ReceivedAt,
                email.SentAt,
                email.MatterId,
                email.ClientId
            });
        }

        [HttpPost("{id}/read")]
        public async Task<IActionResult> MarkRead(string id)
        {
            var userId = GetUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Unauthorized();
            }

            var accountIds = await GetUserAccountIdsAsync(userId);
            var email = await AuthorizedEmailMessages(accountIds).FirstOrDefaultAsync(e => e.Id == id);
            if (email == null)
            {
                return NotFound();
            }

            if (!email.IsRead)
            {
                email.IsRead = true;
                await _context.SaveChangesAsync();
                await _auditLogger.LogAsync(HttpContext, "email.mark_read", nameof(EmailMessage), email.Id, $"AccountId={email.EmailAccountId}");
            }

            return Ok(new { message = "Email marked as read." });
        }

        // POST: api/emails/{id}/link
        [HttpPost("{id}/link")]
        public async Task<IActionResult> LinkEmail(string id, [FromBody] LinkEmailDto dto)
        {
            var userId = GetUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Unauthorized();
            }

            var accountIds = await GetUserAccountIdsAsync(userId);
            var email = await AuthorizedEmailMessages(accountIds).FirstOrDefaultAsync(e => e.Id == id);
            if (email == null)
            {
                return NotFound();
            }

            if (!string.IsNullOrEmpty(dto.MatterId))
            {
                if (!await MatterExistsAsync(dto.MatterId))
                {
                    return BadRequest(new { message = "Matter not found." });
                }

                email.MatterId = dto.MatterId.Trim();
            }

            if (!string.IsNullOrEmpty(dto.ClientId))
            {
                if (!await ClientExistsAsync(dto.ClientId))
                {
                    return BadRequest(new { message = "Client not found." });
                }

                email.ClientId = dto.ClientId.Trim();
            }

            await _context.SaveChangesAsync();
            await _auditLogger.LogAsync(HttpContext, "email.link", nameof(EmailMessage), email.Id, $"MatterId={email.MatterId}, ClientId={email.ClientId}");

            return Ok(new { message = "Email linked successfully" });
        }

        // POST: api/emails/{id}/unlink
        [HttpPost("{id}/unlink")]
        public async Task<IActionResult> UnlinkEmail(string id)
        {
            var userId = GetUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Unauthorized();
            }

            var accountIds = await GetUserAccountIdsAsync(userId);
            var email = await AuthorizedEmailMessages(accountIds).FirstOrDefaultAsync(e => e.Id == id);
            if (email == null)
            {
                return NotFound();
            }

            email.MatterId = null;
            email.ClientId = null;

            await _context.SaveChangesAsync();
            await _auditLogger.LogAsync(HttpContext, "email.unlink", nameof(EmailMessage), email.Id, null);

            return Ok(new { message = "Email unlinked" });
        }

        // POST: api/emails/send
        [HttpPost("send")]
        [EnableRateLimiting("StaffMessagingSend")]
        public async Task<IActionResult> SendEmail([FromBody] SendEmailDto dto)
        {
            var userId = GetUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Unauthorized();
            }

            var toAddress = dto.ToAddress?.Trim();
            var subject = dto.Subject?.Trim();
            var bodyText = dto.BodyText?.Trim();
            if (string.IsNullOrWhiteSpace(toAddress) || string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(bodyText))
            {
                return BadRequest(new { message = "ToAddress, Subject, and BodyText are required." });
            }

            if (!IsValidEmailAddress(toAddress))
            {
                return BadRequest(new { message = "Recipient email address is invalid." });
            }

            if (!string.IsNullOrWhiteSpace(dto.MatterId) && !await MatterExistsAsync(dto.MatterId))
            {
                return BadRequest(new { message = "Matter not found." });
            }

            if (!string.IsNullOrWhiteSpace(dto.ClientId) && !await ClientExistsAsync(dto.ClientId))
            {
                return BadRequest(new { message = "Client not found." });
            }

            var account = await ResolveSendAccountAsync(userId, dto.EmailAccountId);
            if (account == null)
            {
                var accountCount = await TenantScope(_context.EmailAccounts)
                    .CountAsync(a => a.UserId == userId && a.IsActive);

                return BadRequest(new
                {
                    message = accountCount == 0
                        ? "Connect a Gmail or Outlook mailbox before sending email."
                        : "Select a connected mailbox to send from.",
                    requiresAccountSelection = accountCount > 1
                });
            }

            if (!account.IsActive)
            {
                return BadRequest(new { message = "Selected mailbox is inactive. Reconnect this account." });
            }

            try
            {
                var tokens = await EnsureValidTokensAsync(account, HttpContext.RequestAborted);
                if (string.IsNullOrWhiteSpace(tokens.accessToken))
                {
                    return BadRequest(new { message = "Selected mailbox is missing an access token. Reconnect this account." });
                }

                var sendResult = await SendViaProviderAsync(
                    account,
                    tokens.accessToken,
                    toAddress,
                    subject,
                    bodyText,
                    HttpContext.RequestAborted);

                var sentAt = sendResult.sentAtUtc;
                var emailMessage = new EmailMessage
                {
                    Id = Guid.NewGuid().ToString(),
                    ExternalId = sendResult.externalId,
                    Provider = account.Provider,
                    EmailAccountId = account.Id,
                    MatterId = dto.MatterId?.Trim(),
                    ClientId = dto.ClientId?.Trim(),
                    Subject = subject,
                    FromAddress = account.EmailAddress,
                    FromName = account.DisplayName ?? account.EmailAddress,
                    ToAddresses = toAddress,
                    BodyText = bodyText,
                    BodyHtml = BuildHtmlBody(bodyText),
                    Folder = "Sent",
                    IsRead = true,
                    HasAttachments = false,
                    AttachmentCount = 0,
                    Importance = "Normal",
                    ReceivedAt = sentAt,
                    SentAt = sentAt,
                    SyncedAt = sentAt,
                    CreatedAt = sentAt
                };

                _context.EmailMessages.Add(emailMessage);
                await _context.SaveChangesAsync();

                await _auditLogger.LogAsync(
                    HttpContext,
                    "email.send",
                    nameof(EmailMessage),
                    emailMessage.Id,
                    $"To={toAddress}; AccountId={account.Id}; Provider={account.Provider}");

                return Ok(new
                {
                    message = "Email sent.",
                    emailId = emailMessage.Id,
                    status = "Sent",
                    accountId = account.Id,
                    fromAddress = account.EmailAddress
                });
            }
            catch (InvalidOperationException ex)
            {
                await _auditLogger.LogAsync(
                    HttpContext,
                    "email.send_failed",
                    nameof(EmailAccount),
                    account.Id,
                    $"To={toAddress}; Provider={account.Provider}; Reason={ex.Message}");

                return StatusCode(StatusCodes.Status502BadGateway, new
                {
                    message = ex.Message
                });
            }
        }

        // GET: api/emails/accounts
        [HttpGet("accounts")]
        public async Task<ActionResult<IEnumerable<EmailAccount>>> GetEmailAccounts()
        {
            var userId = GetUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Unauthorized();
            }

            var accounts = await TenantScope(_context.EmailAccounts)
                .AsNoTracking()
                .Where(a => a.UserId == userId)
                .Select(a => new
                {
                    a.Id,
                    a.Provider,
                    a.EmailAddress,
                    a.DisplayName,
                    a.IsActive,
                    a.SyncEnabled,
                    a.LastSyncAt,
                    a.SyncError
                })
                .ToListAsync();

            return Ok(accounts);
        }

        // GET: api/emails/accounts/connect/outlook/authorization
        [HttpGet("accounts/connect/outlook/authorization")]
        public IActionResult GetOutlookAuthorization()
        {
            var userId = GetUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Unauthorized();
            }

            var clientId = _configuration["Integrations:Outlook:ClientId"];
            if (string.IsNullOrWhiteSpace(clientId))
            {
                return BadRequest(new { message = "Outlook OAuth is not configured." });
            }

            var redirectUri = ResolveRedirectUri(_configuration["Integrations:Outlook:RedirectUri"], "/auth/outlook/callback");
            var scopes = _configuration["Integrations:Outlook:Scopes"] ?? "offline_access Mail.Read Mail.Send User.Read";
            var codeVerifier = GeneratePkceVerifier();
            var state = CreateOAuthState("outlook", userId, codeVerifier);
            var codeChallenge = BuildPkceChallenge(codeVerifier);

            var url = "https://login.microsoftonline.com/" +
                      $"{(_configuration["Integrations:Outlook:TenantId"] ?? "common")}/oauth2/v2.0/authorize" +
                      $"?client_id={Uri.EscapeDataString(clientId)}" +
                      $"&response_type=code" +
                      $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
                      $"&response_mode=query" +
                      $"&scope={Uri.EscapeDataString(scopes)}" +
                      $"&state={Uri.EscapeDataString(state)}" +
                      $"&code_challenge={Uri.EscapeDataString(codeChallenge)}" +
                      $"&code_challenge_method=S256";

            return Ok(new { authorizationUrl = url, state, codeVerifier, redirectUri });
        }

        // GET: api/emails/accounts/connect/gmail/authorization
        [HttpGet("accounts/connect/gmail/authorization")]
        public IActionResult GetGmailAuthorization()
        {
            var userId = GetUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Unauthorized();
            }

            var clientId = _configuration["Integrations:Google:ClientId"];
            if (string.IsNullOrWhiteSpace(clientId))
            {
                return BadRequest(new { message = "Google OAuth is not configured." });
            }

            var redirectUri = ResolveRedirectUri(_configuration["Integrations:Google:RedirectUri"], "/auth/google/callback");
            var scopes = _configuration["Integrations:Google:Scopes"] ?? "openid email profile https://www.googleapis.com/auth/gmail.readonly https://www.googleapis.com/auth/gmail.send";
            var codeVerifier = GeneratePkceVerifier();
            var state = CreateOAuthState("gmail", userId, codeVerifier);
            var codeChallenge = BuildPkceChallenge(codeVerifier);

            var url = "https://accounts.google.com/o/oauth2/v2/auth" +
                      $"?client_id={Uri.EscapeDataString(clientId)}" +
                      $"&response_type=code" +
                      $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
                      $"&scope={Uri.EscapeDataString(scopes)}" +
                      $"&access_type=offline" +
                      $"&prompt=consent" +
                      $"&state={Uri.EscapeDataString(state)}" +
                      $"&code_challenge={Uri.EscapeDataString(codeChallenge)}" +
                      $"&code_challenge_method=S256";

            return Ok(new { authorizationUrl = url, state, codeVerifier, redirectUri });
        }

        // POST: api/emails/accounts/connect/outlook
        [HttpPost("accounts/connect/outlook")]
        [EnableRateLimiting("AdminDangerousOps")]
        public async Task<IActionResult> ConnectOutlook([FromBody] ConnectOutlookDto dto)
        {
            var userId = GetUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Unauthorized();
            }

            if (!ValidateOAuthState("outlook", userId, dto.State, dto.CodeVerifier))
            {
                return BadRequest(new { message = "OAuth state validation failed." });
            }

            var token = await ResolveOutlookTokenAsync(dto);
            if (token == null)
            {
                return StatusCode(StatusCodes.Status502BadGateway, new { message = "Outlook token exchange failed." });
            }

            var profile = await ResolveOutlookMailboxIdentityAsync(token.Value.accessToken, HttpContext.RequestAborted);
            var emailAddress = string.IsNullOrWhiteSpace(dto.Email)
                ? profile.emailAddress
                : dto.Email.Trim();
            var displayName = string.IsNullOrWhiteSpace(dto.DisplayName)
                ? profile.displayName
                : dto.DisplayName.Trim();

            if (string.IsNullOrWhiteSpace(emailAddress))
            {
                return BadRequest(new { message = "Unable to determine Outlook mailbox identity." });
            }

            var account = await UpsertEmailAccountAsync(
                provider: "Outlook",
                userId,
                emailAddress,
                displayName,
                token.Value.accessToken,
                token.Value.refreshToken,
                token.Value.expiresAt);

            return Ok(new { message = "Outlook account connected", accountId = account.Id });
        }

        // POST: api/emails/accounts/connect/gmail
        [HttpPost("accounts/connect/gmail")]
        [EnableRateLimiting("AdminDangerousOps")]
        public async Task<IActionResult> ConnectGmail([FromBody] ConnectGmailDto dto)
        {
            var userId = GetUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Unauthorized();
            }

            if (!ValidateOAuthState("gmail", userId, dto.State, dto.CodeVerifier))
            {
                return BadRequest(new { message = "OAuth state validation failed." });
            }

            var token = await ResolveGmailTokenAsync(dto);
            if (token == null)
            {
                return StatusCode(StatusCodes.Status502BadGateway, new { message = "Gmail token exchange failed." });
            }

            var profile = await ResolveGmailMailboxIdentityAsync(token.Value.accessToken, HttpContext.RequestAborted);
            var emailAddress = string.IsNullOrWhiteSpace(dto.Email)
                ? profile.emailAddress
                : dto.Email.Trim();
            var displayName = string.IsNullOrWhiteSpace(dto.DisplayName)
                ? profile.displayName
                : dto.DisplayName.Trim();

            if (string.IsNullOrWhiteSpace(emailAddress))
            {
                return BadRequest(new { message = "Unable to determine Gmail mailbox identity." });
            }

            var account = await UpsertEmailAccountAsync(
                provider: "Gmail",
                userId,
                emailAddress,
                displayName,
                token.Value.accessToken,
                token.Value.refreshToken,
                token.Value.expiresAt);

            return Ok(new { message = "Gmail account connected", accountId = account.Id });
        }

        // POST: api/emails/accounts/{id}/sync
        [HttpPost("accounts/{id}/sync")]
        [EnableRateLimiting("AdminDangerousOps")]
        public async Task<IActionResult> SyncAccount(string id, [FromQuery] int limit = 100)
        {
            var userId = GetUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Unauthorized();
            }

            limit = Math.Clamp(limit, 1, 200);

            var account = await TenantScope(_context.EmailAccounts)
                .FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId);
            if (account == null)
            {
                return NotFound();
            }

            if (!account.IsActive)
            {
                return BadRequest(new { message = "Email account is inactive." });
            }

            try
            {
                var tokens = await EnsureValidTokensAsync(account, HttpContext.RequestAborted);
                if (string.IsNullOrWhiteSpace(tokens.accessToken))
                {
                    account.SyncError = "Account token is missing. Reconnect this account.";
                    account.UpdatedAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync();
                    return BadRequest(new { message = "Account token is missing. Reconnect this account." });
                }

                List<ProviderEmailMessage> fetched;
                if (string.Equals(account.Provider, "Outlook", StringComparison.OrdinalIgnoreCase))
                {
                    fetched = await FetchOutlookMessagesAsync(tokens.accessToken, limit, HttpContext.RequestAborted);
                }
                else if (string.Equals(account.Provider, "Gmail", StringComparison.OrdinalIgnoreCase))
                {
                    fetched = await FetchGmailMessagesAsync(tokens.accessToken, limit, HttpContext.RequestAborted);
                }
                else
                {
                    return BadRequest(new { message = $"Unsupported provider: {account.Provider}" });
                }

                var (created, updated) = await UpsertMessagesAsync(account, fetched);

                account.LastSyncAt = DateTime.UtcNow;
                account.SyncError = null;
                account.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                await _auditLogger.LogAsync(HttpContext, "email.account.sync", nameof(EmailAccount), account.Id, $"Provider={account.Provider}, Synced={fetched.Count}, Created={created}, Updated={updated}");

                return Ok(new
                {
                    message = "Sync completed.",
                    synced = fetched.Count,
                    created,
                    updated,
                    lastSyncAt = account.LastSyncAt
                });
            }
            catch (Exception ex)
            {
                account.SyncError = "Provider sync failed.";
                account.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                await _auditLogger.LogAsync(HttpContext, "email.account.sync_failed", nameof(EmailAccount), account.Id, $"Provider={account.Provider}");

                _logger.LogWarning(ex, "Email sync failed for account {AccountId}", account.Id);
                return StatusCode(StatusCodes.Status502BadGateway, new
                {
                    message = "Sync failed."
                });
            }
        }

        // DELETE: api/emails/accounts/{id}
        [HttpDelete("accounts/{id}")]
        public async Task<IActionResult> DisconnectAccount(string id)
        {
            var userId = GetUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Unauthorized();
            }

            var account = await TenantScope(_context.EmailAccounts)
                .FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId);
            if (account == null)
            {
                return NotFound();
            }

            // Delete associated emails
            var emails = await TenantScope(_context.EmailMessages)
                .Where(e => e.EmailAccountId == id)
                .ToListAsync();

            _context.EmailMessages.RemoveRange(emails);
            _context.EmailAccounts.Remove(account);
            await _secretStore.DeleteAsync(account.Id, IntegrationSecretScope.Disconnect, HttpContext.RequestAborted);
            await _context.SaveChangesAsync();
            await _auditLogger.LogAsync(HttpContext, "email.account.disconnect", nameof(EmailAccount), account.Id, $"Provider={account.Provider}");

            return NoContent();
        }

        // GET: api/emails/unlinked
        [HttpGet("unlinked")]
        public async Task<ActionResult<IEnumerable<EmailMessage>>> GetUnlinkedEmails([FromQuery] int limit = 50)
        {
            var userId = GetUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Unauthorized();
            }

            var accountIds = await GetUserAccountIdsAsync(userId);
            var emails = await AuthorizedEmailMessages(accountIds)
                .AsNoTracking()
                .Where(e => e.MatterId == null && e.ClientId == null)
                .OrderByDescending(e => e.ReceivedAt)
                .Take(Math.Clamp(limit, 1, 100))
                .Select(e => new
                {
                    e.Id,
                    e.Subject,
                    e.FromAddress,
                    e.FromName,
                    e.ToAddresses,
                    e.Folder,
                    e.IsRead,
                    e.HasAttachments,
                    e.Importance,
                    e.ReceivedAt,
                    e.EmailAccountId
                })
                .ToListAsync();

            return Ok(emails);
        }

        // POST: api/emails/auto-link
        [HttpPost("auto-link")]
        [EnableRateLimiting("AdminDangerousOps")]
        public async Task<IActionResult> AutoLinkEmails()
        {
            var userId = GetUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Unauthorized();
            }

            var accountIds = await GetUserAccountIdsAsync(userId);
            // Find unlinked emails
            var unlinkedEmails = await AuthorizedEmailMessages(accountIds)
                .Where(e => e.MatterId == null && e.ClientId == null)
                .ToListAsync();

            var clients = await TenantScope(_context.Clients)
                .Where(c => !string.IsNullOrWhiteSpace(c.Email))
                .ToListAsync();

            int linkedCount = 0;

            foreach (var email in unlinkedEmails)
            {
                if (string.IsNullOrWhiteSpace(email.FromAddress) && string.IsNullOrWhiteSpace(email.ToAddresses))
                {
                    continue;
                }

                var matchedClient = clients.FirstOrDefault(c =>
                    !string.IsNullOrWhiteSpace(c.Email) &&
                    (string.Equals(c.Email, email.FromAddress, StringComparison.OrdinalIgnoreCase) ||
                     RecipientContains(email.ToAddresses, c.Email)));

                if (matchedClient != null)
                {
                    email.ClientId = matchedClient.Id;

                    // Try to find an active matter for this client
                    var matter = await TenantScope(_context.Matters)
                        .Where(m => m.ClientId == matchedClient.Id && m.Status == "Active")
                        .OrderByDescending(m => m.OpenDate)
                        .FirstOrDefaultAsync();

                    if (matter != null)
                    {
                        email.MatterId = matter.Id;
                    }

                    linkedCount++;
                }
            }

            await _context.SaveChangesAsync();
            await _auditLogger.LogAsync(HttpContext, "email.auto_link", nameof(EmailMessage), null, $"LinkedCount={linkedCount}");

            return Ok(new { message = $"Auto-linked {linkedCount} emails", linkedCount });
        }

        private string? GetUserId()
        {
            return User.FindFirst("sub")?.Value ??
                   User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        }

        private static bool IsValidEmailAddress(string emailAddress)
        {
            try
            {
                _ = new MailAddress(emailAddress);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        private static string BuildHtmlBody(string bodyText)
        {
            var encoded = WebUtility.HtmlEncode(bodyText);
            return $"<div>{encoded.Replace(Environment.NewLine, "<br />", StringComparison.Ordinal).Replace("\n", "<br />", StringComparison.Ordinal)}</div>";
        }

        private IQueryable<T> TenantScope<T>(IQueryable<T> query) where T : class
        {
            var tenantId = RequireTenantId();
            return query.Where(entity => EF.Property<string>(entity, "TenantId") == tenantId);
        }

        private string RequireTenantId()
        {
            if (string.IsNullOrWhiteSpace(_tenantContext.TenantId))
            {
                throw new InvalidOperationException("Tenant context is required.");
            }

            return _tenantContext.TenantId;
        }

        private Task<List<string>> GetUserAccountIdsAsync(string userId)
        {
            return TenantScope(_context.EmailAccounts)
                .AsNoTracking()
                .Where(a => a.UserId == userId)
                .Select(a => a.Id)
                .ToListAsync();
        }

        private IQueryable<EmailMessage> AuthorizedEmailMessages(IReadOnlyCollection<string> accountIds)
        {
            if (accountIds.Count == 0)
            {
                return TenantScope(_context.EmailMessages).Where(_ => false);
            }

            return TenantScope(_context.EmailMessages).Where(e => e.EmailAccountId != null && accountIds.Contains(e.EmailAccountId));
        }

        private Task<bool> MatterExistsAsync(string matterId)
        {
            var normalized = matterId?.Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return Task.FromResult(false);
            }

            return TenantScope(_context.Matters).AnyAsync(m => m.Id == normalized);
        }

        private Task<bool> ClientExistsAsync(string clientId)
        {
            var normalized = clientId?.Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return Task.FromResult(false);
            }

            return TenantScope(_context.Clients).AnyAsync(c => c.Id == normalized);
        }

        private async Task<EmailAccount?> ResolveSendAccountAsync(string userId, string? requestedAccountId)
        {
            var query = TenantScope(_context.EmailAccounts)
                .Where(a => a.UserId == userId && a.IsActive);

            if (!string.IsNullOrWhiteSpace(requestedAccountId))
            {
                var normalizedAccountId = requestedAccountId.Trim();
                return await query.FirstOrDefaultAsync(a => a.Id == normalizedAccountId);
            }

            var accounts = await query
                .OrderByDescending(a => a.LastSyncAt ?? a.UpdatedAt)
                .Take(2)
                .ToListAsync();

            return accounts.Count == 1 ? accounts[0] : null;
        }

        private async Task<EmailAccount> UpsertEmailAccountAsync(
            string provider,
            string userId,
            string email,
            string? displayName,
            string accessToken,
            string? refreshToken,
            DateTime? tokenExpiresAt)
        {
            var normalizedEmail = email.Trim();
            var existing = await TenantScope(_context.EmailAccounts)
                .FirstOrDefaultAsync(a =>
                    a.UserId == userId &&
                    a.Provider == provider &&
                    a.EmailAddress.ToLower() == normalizedEmail.ToLower());

            if (existing == null)
            {
                existing = new EmailAccount
                {
                    UserId = userId,
                    Provider = provider,
                    EmailAddress = normalizedEmail,
                    DisplayName = displayName,
                    AccessToken = null,
                    RefreshToken = null,
                    TokenExpiresAt = tokenExpiresAt,
                    IsActive = true,
                    SyncEnabled = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _context.EmailAccounts.Add(existing);
            }
            else
            {
                existing.DisplayName = string.IsNullOrWhiteSpace(displayName) ? existing.DisplayName : displayName;
                existing.AccessToken = null;
                existing.RefreshToken = null;
                existing.TokenExpiresAt = tokenExpiresAt;
                existing.IsActive = true;
                existing.SyncEnabled = true;
                existing.SyncError = null;
                existing.UpdatedAt = DateTime.UtcNow;
            }

            await _secretStore.UpsertAsync(
                existing.Id,
                provider.Trim().ToLowerInvariant(),
                new IntegrationSecretMaterial
                {
                    AccessToken = accessToken,
                    RefreshToken = refreshToken
                },
                IntegrationSecretScope.Connect,
                HttpContext.RequestAborted);

            await _context.SaveChangesAsync();
            await _auditLogger.LogAsync(HttpContext, "email.account.connect", nameof(EmailAccount), existing.Id, $"Provider={provider}, Email={normalizedEmail}");
            return existing;
        }

        private async Task<(string accessToken, string? refreshToken, DateTime? expiresAt)?> ResolveOutlookTokenAsync(ConnectOutlookDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.AuthorizationCode))
            {
                return null;
            }

            var clientId = _configuration["Integrations:Outlook:ClientId"];
            var clientSecret = _configuration["Integrations:Outlook:ClientSecret"];
            var tenantId = _configuration["Integrations:Outlook:TenantId"] ?? "common";
            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            {
                _logger.LogWarning("Outlook OAuth is not configured. Missing Integrations:Outlook:ClientId/ClientSecret.");
                return null;
            }

            var redirectUri = ResolveRedirectUri(_configuration["Integrations:Outlook:RedirectUri"], "/auth/outlook/callback");
            var scopes = _configuration["Integrations:Outlook:Scopes"] ?? "offline_access Mail.Read Mail.Send User.Read";
            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = dto.AuthorizationCode,
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["redirect_uri"] = redirectUri,
                ["scope"] = scopes
            };
            if (!string.IsNullOrWhiteSpace(dto.CodeVerifier))
            {
                form["code_verifier"] = dto.CodeVerifier.Trim();
            }

            var tokenUrl = $"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token";
            return await ExchangeTokenAsync(tokenUrl, form, HttpContext.RequestAborted);
        }

        private async Task<(string accessToken, string? refreshToken, DateTime? expiresAt)?> ResolveGmailTokenAsync(ConnectGmailDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.AuthorizationCode))
            {
                return null;
            }

            var clientId = _configuration["Integrations:Google:ClientId"];
            var clientSecret = _configuration["Integrations:Google:ClientSecret"];
            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            {
                _logger.LogWarning("Google OAuth is not configured. Missing Integrations:Google:ClientId/ClientSecret.");
                return null;
            }

            var redirectUri = ResolveRedirectUri(_configuration["Integrations:Google:RedirectUri"], "/auth/google/callback");
            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = dto.AuthorizationCode,
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["redirect_uri"] = redirectUri
            };
            if (!string.IsNullOrWhiteSpace(dto.CodeVerifier))
            {
                form["code_verifier"] = dto.CodeVerifier.Trim();
            }

            return await ExchangeTokenAsync("https://oauth2.googleapis.com/token", form, HttpContext.RequestAborted);
        }

        private async Task<(string accessToken, string? refreshToken, DateTime? expiresAt)> EnsureValidTokensAsync(EmailAccount account, CancellationToken cancellationToken)
        {
            var secrets = await _secretStore.GetAsync(account.Id, IntegrationSecretScope.Sync, cancellationToken);
            var accessToken = secrets?.AccessToken ?? account.AccessToken;
            var refreshToken = secrets?.RefreshToken ?? account.RefreshToken;

            if (!string.IsNullOrWhiteSpace(accessToken) &&
                (!account.TokenExpiresAt.HasValue || account.TokenExpiresAt.Value > DateTime.UtcNow.AddMinutes(2)))
            {
                return (accessToken, refreshToken, account.TokenExpiresAt);
            }

            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                return (accessToken ?? string.Empty, refreshToken, account.TokenExpiresAt);
            }

            var refreshed = string.Equals(account.Provider, "Outlook", StringComparison.OrdinalIgnoreCase)
                ? await RefreshOutlookTokenAsync(refreshToken, cancellationToken)
                : string.Equals(account.Provider, "Gmail", StringComparison.OrdinalIgnoreCase)
                    ? await RefreshGmailTokenAsync(refreshToken, cancellationToken)
                    : null;

            if (refreshed == null)
            {
                account.IsActive = false;
                account.SyncEnabled = false;
                account.SyncError = "Email account requires reconnect.";
                account.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync(cancellationToken);
                throw new InvalidOperationException("Email account requires reconnect.");
            }

            account.TokenExpiresAt = refreshed.Value.expiresAt;
            account.SyncError = null;
            account.UpdatedAt = DateTime.UtcNow;

            await _secretStore.UpsertAsync(
                account.Id,
                account.Provider.Trim().ToLowerInvariant(),
                new IntegrationSecretMaterial
                {
                    AccessToken = refreshed.Value.accessToken,
                    RefreshToken = refreshed.Value.refreshToken ?? refreshToken
                },
                IntegrationSecretScope.Sync,
                cancellationToken);

            return (refreshed.Value.accessToken, refreshed.Value.refreshToken ?? refreshToken, refreshed.Value.expiresAt);
        }

        private async Task<(string accessToken, string? refreshToken, DateTime? expiresAt)?> RefreshOutlookTokenAsync(string refreshToken, CancellationToken cancellationToken)
        {
            var clientId = _configuration["Integrations:Outlook:ClientId"];
            var clientSecret = _configuration["Integrations:Outlook:ClientSecret"];
            var tenantId = _configuration["Integrations:Outlook:TenantId"] ?? "common";
            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            {
                return null;
            }

            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["redirect_uri"] = ResolveRedirectUri(_configuration["Integrations:Outlook:RedirectUri"], "/auth/outlook/callback"),
                ["scope"] = _configuration["Integrations:Outlook:Scopes"] ?? "offline_access Mail.Read Mail.Send User.Read"
            };

            var tokenUrl = $"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token";
            return await ExchangeTokenAsync(tokenUrl, form, cancellationToken);
        }

        private async Task<(string accessToken, string? refreshToken, DateTime? expiresAt)?> RefreshGmailTokenAsync(string refreshToken, CancellationToken cancellationToken)
        {
            var clientId = _configuration["Integrations:Google:ClientId"];
            var clientSecret = _configuration["Integrations:Google:ClientSecret"];
            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            {
                return null;
            }

            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret
            };

            return await ExchangeTokenAsync("https://oauth2.googleapis.com/token", form, cancellationToken);
        }

        private async Task<(string externalId, DateTime sentAtUtc)> SendViaProviderAsync(
            EmailAccount account,
            string accessToken,
            string toAddress,
            string subject,
            string bodyText,
            CancellationToken cancellationToken)
        {
            if (string.Equals(account.Provider, "Gmail", StringComparison.OrdinalIgnoreCase))
            {
                return await SendGmailMessageAsync(account, accessToken, toAddress, subject, bodyText, cancellationToken);
            }

            if (string.Equals(account.Provider, "Outlook", StringComparison.OrdinalIgnoreCase))
            {
                return await SendOutlookMessageAsync(accessToken, toAddress, subject, bodyText, cancellationToken);
            }

            throw new InvalidOperationException($"Unsupported provider: {account.Provider}");
        }

        private async Task<(string externalId, DateTime sentAtUtc)> SendGmailMessageAsync(
            EmailAccount account,
            string accessToken,
            string toAddress,
            string subject,
            string bodyText,
            CancellationToken cancellationToken)
        {
            var rawMessage = string.Join("\r\n", new[]
            {
                $"From: {FormatMailAddress(account.EmailAddress, account.DisplayName)}",
                $"To: {toAddress}",
                $"Subject: {subject}",
                "MIME-Version: 1.0",
                "Content-Type: text/html; charset=utf-8",
                string.Empty,
                BuildHtmlBody(bodyText)
            });

            var encodedMessage = Convert.ToBase64String(Encoding.UTF8.GetBytes(rawMessage))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');

            using var request = new HttpRequestMessage(HttpMethod.Post, "https://gmail.googleapis.com/gmail/v1/users/me/messages/send");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Content = new StringContent(
                JsonSerializer.Serialize(new { raw = encodedMessage }),
                Encoding.UTF8,
                "application/json");

            var client = _httpClientFactory.CreateClient();
            using var response = await client.SendAsync(request, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw CreateProviderSendException(response.StatusCode, "Gmail", payload);
            }

            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(payload) ? "{}" : payload);
            var externalId = GetString(doc.RootElement, "id") ?? Guid.NewGuid().ToString();
            var sentAtUtc = DateTime.UtcNow;
            return (externalId, sentAtUtc);
        }

        private async Task<(string externalId, DateTime sentAtUtc)> SendOutlookMessageAsync(
            string accessToken,
            string toAddress,
            string subject,
            string bodyText,
            CancellationToken cancellationToken)
        {
            var body = new
            {
                message = new
                {
                    subject,
                    body = new
                    {
                        contentType = "HTML",
                        content = BuildHtmlBody(bodyText)
                    },
                    toRecipients = new[]
                    {
                        new
                        {
                            emailAddress = new
                            {
                                address = toAddress
                            }
                        }
                    }
                },
                saveToSentItems = true
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, "https://graph.microsoft.com/v1.0/me/sendMail");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            var client = _httpClientFactory.CreateClient();
            using var response = await client.SendAsync(request, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw CreateProviderSendException(response.StatusCode, "Outlook", payload);
            }

            return (Guid.NewGuid().ToString(), DateTime.UtcNow);
        }

        private async Task<(string? emailAddress, string? displayName)> ResolveGmailMailboxIdentityAsync(string accessToken, CancellationToken cancellationToken)
        {
            var profileDoc = await GetJsonAsync("https://gmail.googleapis.com/gmail/v1/users/me/profile", accessToken, cancellationToken);
            var emailAddress = GetString(profileDoc.RootElement, "emailAddress");
            return (emailAddress, emailAddress);
        }

        private async Task<(string? emailAddress, string? displayName)> ResolveOutlookMailboxIdentityAsync(string accessToken, CancellationToken cancellationToken)
        {
            var profileDoc = await GetJsonAsync("https://graph.microsoft.com/v1.0/me?$select=mail,userPrincipalName,displayName", accessToken, cancellationToken);
            var emailAddress = GetString(profileDoc.RootElement, "mail") ?? GetString(profileDoc.RootElement, "userPrincipalName");
            var displayName = GetString(profileDoc.RootElement, "displayName");
            return (emailAddress, displayName);
        }

        private static InvalidOperationException CreateProviderSendException(HttpStatusCode statusCode, string provider, string? payload)
        {
            if (statusCode == HttpStatusCode.Forbidden || statusCode == HttpStatusCode.Unauthorized)
            {
                return new InvalidOperationException($"{provider} mailbox needs reconnect or send permission approval.");
            }

            return new InvalidOperationException($"{provider} rejected the send request.");
        }

        private async Task<(string accessToken, string? refreshToken, DateTime? expiresAt)?> ExchangeTokenAsync(
            string tokenUrl,
            Dictionary<string, string> form,
            CancellationToken cancellationToken)
        {
            var client = _httpClientFactory.CreateClient();
            using var response = await client.PostAsync(tokenUrl, new FormUrlEncodedContent(form), cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Token exchange failed for {TokenUrl}. Status={StatusCode}, PayloadLength={PayloadLength}", tokenUrl, (int)response.StatusCode, payload?.Length ?? 0);
                return null;
            }

            using var doc = JsonDocument.Parse(payload);
            if (!doc.RootElement.TryGetProperty("access_token", out var accessTokenElement))
            {
                return null;
            }

            var accessToken = accessTokenElement.GetString();
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return null;
            }

            var refreshToken = doc.RootElement.TryGetProperty("refresh_token", out var refreshTokenElement)
                ? refreshTokenElement.GetString()
                : null;

            DateTime? expiresAt = null;
            if (doc.RootElement.TryGetProperty("expires_in", out var expiresInElement) && expiresInElement.TryGetInt32(out var expiresIn))
            {
                expiresAt = DateTime.UtcNow.AddSeconds(expiresIn);
            }

            return (accessToken, refreshToken, expiresAt);
        }

        private string ResolveRedirectUri(string? configuredRedirectUri, string defaultPath)
        {
            if (!string.IsNullOrWhiteSpace(configuredRedirectUri))
            {
                return configuredRedirectUri.Trim();
            }

            return $"{Request.Scheme}://{Request.Host}{defaultPath}";
        }

        private string CreateOAuthState(string provider, string userId, string codeVerifier)
        {
            var payload = new OAuthStatePayload
            {
                Provider = provider,
                UserId = userId,
                TenantId = RequireTenantId(),
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(10),
                CodeVerifierHash = BuildPkceChallenge(codeVerifier)
            };

            var json = JsonSerializer.Serialize(payload);
            return _oauthStateProtector.Protect(json);
        }

        private bool ValidateOAuthState(string provider, string userId, string? protectedState, string? codeVerifier)
        {
            if (string.IsNullOrWhiteSpace(protectedState) || string.IsNullOrWhiteSpace(codeVerifier))
            {
                return false;
            }

            try
            {
                var json = _oauthStateProtector.Unprotect(protectedState.Trim());
                var payload = JsonSerializer.Deserialize<OAuthStatePayload>(json);
                if (payload == null)
                {
                    return false;
                }

                if (!string.Equals(payload.Provider, provider, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(payload.UserId, userId, StringComparison.Ordinal) ||
                    !string.Equals(payload.TenantId, RequireTenantId(), StringComparison.Ordinal) ||
                    payload.ExpiresAtUtc < DateTime.UtcNow)
                {
                    return false;
                }

                var computedHash = BuildPkceChallenge(codeVerifier.Trim());
                return string.Equals(payload.CodeVerifierHash, computedHash, StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        private static string GeneratePkceVerifier()
        {
            var bytes = RandomNumberGenerator.GetBytes(32);
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private static string BuildPkceChallenge(string codeVerifier)
        {
            var bytes = Encoding.UTF8.GetBytes(codeVerifier);
            var hash = SHA256.HashData(bytes);
            return Convert.ToBase64String(hash)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private async Task<List<ProviderEmailMessage>> FetchOutlookMessagesAsync(string accessToken, int limit, CancellationToken cancellationToken)
        {
            var url = $"https://graph.microsoft.com/v1.0/me/messages?$top={limit}&$orderby=receivedDateTime desc&$select=id,subject,from,toRecipients,ccRecipients,bccRecipients,bodyPreview,body,receivedDateTime,sentDateTime,isRead,hasAttachments,importance,parentFolderId";
            var doc = await GetJsonAsync(url, accessToken, cancellationToken);
            if (!doc.RootElement.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Array)
            {
                return new List<ProviderEmailMessage>();
            }

            var result = new List<ProviderEmailMessage>();
            foreach (var item in value.EnumerateArray())
            {
                var parsed = ParseOutlookMessage(item);
                if (parsed != null)
                {
                    result.Add(parsed);
                }
            }

            return result;
        }

        private async Task<List<ProviderEmailMessage>> FetchGmailMessagesAsync(string accessToken, int limit, CancellationToken cancellationToken)
        {
            var listUrl = $"https://gmail.googleapis.com/gmail/v1/users/me/messages?maxResults={limit}";
            var listDoc = await GetJsonAsync(listUrl, accessToken, cancellationToken);
            if (!listDoc.RootElement.TryGetProperty("messages", out var messagesElement) || messagesElement.ValueKind != JsonValueKind.Array)
            {
                return new List<ProviderEmailMessage>();
            }

            var result = new List<ProviderEmailMessage>();
            foreach (var item in messagesElement.EnumerateArray())
            {
                var id = GetString(item, "id");
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                var detailUrl = $"https://gmail.googleapis.com/gmail/v1/users/me/messages/{id}?format=full";
                var detailDoc = await GetJsonAsync(detailUrl, accessToken, cancellationToken);
                var parsed = ParseGmailMessage(detailDoc.RootElement);
                if (parsed != null)
                {
                    result.Add(parsed);
                }
            }

            return result;
        }

        private async Task<JsonDocument> GetJsonAsync(string url, string accessToken, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var client = _httpClientFactory.CreateClient();
            var response = await client.SendAsync(request, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Provider API request failed. Url={Url} Status={StatusCode} PayloadLength={PayloadLength}", url, (int)response.StatusCode, payload?.Length ?? 0);
                throw new InvalidOperationException("Provider API request failed.");
            }

            return JsonDocument.Parse(payload);
        }

        private async Task<(int created, int updated)> UpsertMessagesAsync(EmailAccount account, IReadOnlyCollection<ProviderEmailMessage> fetched)
        {
            var externalIds = fetched
                .Select(f => f.ExternalId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            var existingMap = externalIds.Length == 0
                ? new Dictionary<string, EmailMessage>(StringComparer.Ordinal)
                : await TenantScope(_context.EmailMessages)
                    .Where(e => e.EmailAccountId == account.Id && e.ExternalId != null && externalIds.Contains(e.ExternalId))
                    .ToDictionaryAsync(e => e.ExternalId!, StringComparer.Ordinal);

            var now = DateTime.UtcNow;
            var created = 0;
            var updated = 0;

            foreach (var incoming in fetched)
            {
                if (string.IsNullOrWhiteSpace(incoming.ExternalId))
                {
                    continue;
                }

                if (existingMap.TryGetValue(incoming.ExternalId, out var existing))
                {
                    existing.Subject = incoming.Subject;
                    existing.FromAddress = incoming.FromAddress;
                    existing.FromName = incoming.FromName;
                    existing.ToAddresses = incoming.ToAddresses;
                    existing.CcAddresses = incoming.CcAddresses;
                    existing.BccAddresses = incoming.BccAddresses;
                    existing.BodyText = incoming.BodyText;
                    existing.BodyHtml = incoming.BodyHtml;
                    existing.Folder = incoming.Folder;
                    existing.IsRead = incoming.IsRead;
                    existing.HasAttachments = incoming.HasAttachments;
                    existing.AttachmentCount = incoming.AttachmentCount;
                    existing.Importance = incoming.Importance;
                    existing.ReceivedAt = incoming.ReceivedAt;
                    existing.SentAt = incoming.SentAt;
                    existing.SyncedAt = now;
                    updated++;
                }
                else
                {
                    _context.EmailMessages.Add(new EmailMessage
                    {
                        Id = Guid.NewGuid().ToString(),
                        ExternalId = incoming.ExternalId,
                        Provider = account.Provider,
                        EmailAccountId = account.Id,
                        Subject = incoming.Subject,
                        FromAddress = incoming.FromAddress,
                        FromName = incoming.FromName,
                        ToAddresses = incoming.ToAddresses,
                        CcAddresses = incoming.CcAddresses,
                        BccAddresses = incoming.BccAddresses,
                        BodyText = incoming.BodyText,
                        BodyHtml = incoming.BodyHtml,
                        Folder = incoming.Folder,
                        IsRead = incoming.IsRead,
                        HasAttachments = incoming.HasAttachments,
                        AttachmentCount = incoming.AttachmentCount,
                        Importance = incoming.Importance,
                        ReceivedAt = incoming.ReceivedAt,
                        SentAt = incoming.SentAt,
                        SyncedAt = now,
                        CreatedAt = now
                    });
                    created++;
                }
            }

            return (created, updated);
        }

        private static ProviderEmailMessage? ParseOutlookMessage(JsonElement item)
        {
            var externalId = GetString(item, "id");
            if (string.IsNullOrWhiteSpace(externalId))
            {
                return null;
            }

            var fromAddress = GetNestedString(item, "from", "emailAddress", "address") ?? string.Empty;
            var fromName = GetNestedString(item, "from", "emailAddress", "name") ?? string.Empty;
            var toAddresses = JoinRecipientAddresses(item, "toRecipients");
            var ccAddresses = JoinRecipientAddresses(item, "ccRecipients");
            var bccAddresses = JoinRecipientAddresses(item, "bccRecipients");
            var subject = GetString(item, "subject") ?? "(No Subject)";
            var bodyText = GetString(item, "bodyPreview");
            var bodyHtml = GetNestedString(item, "body", "content");
            var folder = string.IsNullOrWhiteSpace(GetString(item, "parentFolderId")) ? "Inbox" : "Other";
            var importanceRaw = GetString(item, "importance");
            var importance = string.Equals(importanceRaw, "high", StringComparison.OrdinalIgnoreCase)
                ? "High"
                : string.Equals(importanceRaw, "low", StringComparison.OrdinalIgnoreCase)
                    ? "Low"
                    : "Normal";

            var receivedAt = ParseDateTime(GetString(item, "receivedDateTime"));
            var sentAt = ParseNullableDateTime(GetString(item, "sentDateTime"));

            return new ProviderEmailMessage
            {
                ExternalId = externalId,
                Subject = subject,
                FromAddress = fromAddress,
                FromName = fromName,
                ToAddresses = toAddresses,
                CcAddresses = ccAddresses,
                BccAddresses = bccAddresses,
                BodyText = bodyText,
                BodyHtml = bodyHtml,
                Folder = folder,
                IsRead = GetBoolean(item, "isRead"),
                HasAttachments = GetBoolean(item, "hasAttachments"),
                AttachmentCount = 0,
                Importance = importance,
                ReceivedAt = receivedAt ?? DateTime.UtcNow,
                SentAt = sentAt
            };
        }

        private static ProviderEmailMessage? ParseGmailMessage(JsonElement root)
        {
            var externalId = GetString(root, "id");
            if (string.IsNullOrWhiteSpace(externalId))
            {
                return null;
            }

            if (!root.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var headers = payload.TryGetProperty("headers", out var headersElement) && headersElement.ValueKind == JsonValueKind.Array
                ? headersElement
                : default;

            var fromRaw = GetHeaderValue(headers, "From") ?? string.Empty;
            var (fromAddress, fromName) = ParseMailAddress(fromRaw);
            var toAddresses = GetHeaderValue(headers, "To") ?? string.Empty;
            var ccAddresses = GetHeaderValue(headers, "Cc");
            var bccAddresses = GetHeaderValue(headers, "Bcc");
            var subject = GetHeaderValue(headers, "Subject") ?? "(No Subject)";
            var dateRaw = GetHeaderValue(headers, "Date");

            var bodyText = ExtractBody(payload, "text/plain");
            var bodyHtml = ExtractBody(payload, "text/html");
            if (string.IsNullOrWhiteSpace(bodyText))
            {
                bodyText = GetString(root, "snippet");
            }

            var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("labelIds", out var labelElement) && labelElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var label in labelElement.EnumerateArray())
                {
                    var value = label.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        labels.Add(value);
                    }
                }
            }

            var receivedAt = ParseDateTimeFromInternalDate(root, dateRaw) ?? DateTime.UtcNow;
            var sentAt = ParseNullableDateTime(dateRaw);
            var attachmentCount = CountAttachments(payload);

            return new ProviderEmailMessage
            {
                ExternalId = externalId,
                Subject = subject,
                FromAddress = fromAddress,
                FromName = fromName,
                ToAddresses = toAddresses,
                CcAddresses = ccAddresses,
                BccAddresses = bccAddresses,
                BodyText = bodyText,
                BodyHtml = bodyHtml,
                Folder = labels.Contains("SENT") ? "Sent" : "Inbox",
                IsRead = !labels.Contains("UNREAD"),
                HasAttachments = attachmentCount > 0,
                AttachmentCount = attachmentCount,
                Importance = labels.Contains("IMPORTANT") ? "High" : "Normal",
                ReceivedAt = receivedAt,
                SentAt = sentAt
            };
        }

        private static string? ExtractBody(JsonElement payload, string mimeType)
        {
            if (payload.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var currentMimeType = GetString(payload, "mimeType");
            if (string.Equals(currentMimeType, mimeType, StringComparison.OrdinalIgnoreCase))
            {
                if (payload.TryGetProperty("body", out var bodyElement) &&
                    bodyElement.TryGetProperty("data", out var dataElement) &&
                    dataElement.ValueKind == JsonValueKind.String)
                {
                    return DecodeBase64Url(dataElement.GetString());
                }
            }

            if (payload.TryGetProperty("parts", out var partsElement) && partsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var part in partsElement.EnumerateArray())
                {
                    var nested = ExtractBody(part, mimeType);
                    if (!string.IsNullOrWhiteSpace(nested))
                    {
                        return nested;
                    }
                }
            }

            return null;
        }

        private static int CountAttachments(JsonElement payload)
        {
            var count = 0;
            CountAttachmentsRecursive(payload, ref count);
            return count;
        }

        private static void CountAttachmentsRecursive(JsonElement node, ref int count)
        {
            if (node.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            if (node.TryGetProperty("filename", out var filenameElement) &&
                filenameElement.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(filenameElement.GetString()))
            {
                count++;
            }

            if (node.TryGetProperty("parts", out var partsElement) && partsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in partsElement.EnumerateArray())
                {
                    CountAttachmentsRecursive(child, ref count);
                }
            }
        }

        private static string DecodeBase64Url(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var normalized = value.Replace('-', '+').Replace('_', '/');
            switch (normalized.Length % 4)
            {
                case 2:
                    normalized += "==";
                    break;
                case 3:
                    normalized += "=";
                    break;
            }

            try
            {
                var bytes = Convert.FromBase64String(normalized);
                return Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string GetHeaderValue(JsonElement headers, string name)
        {
            if (headers.ValueKind != JsonValueKind.Array)
            {
                return string.Empty;
            }

            foreach (var header in headers.EnumerateArray())
            {
                var headerName = GetString(header, "name");
                if (!string.Equals(headerName, name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return GetString(header, "value") ?? string.Empty;
            }

            return string.Empty;
        }

        private static DateTime? ParseDateTime(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto))
            {
                return dto.UtcDateTime;
            }

            return null;
        }

        private static DateTime? ParseDateTimeFromInternalDate(JsonElement root, string? fallback)
        {
            if (root.TryGetProperty("internalDate", out var internalDateElement) &&
                internalDateElement.ValueKind == JsonValueKind.String &&
                long.TryParse(internalDateElement.GetString(), out var milliseconds))
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).UtcDateTime;
            }

            return ParseDateTime(fallback);
        }

        private static DateTime? ParseNullableDateTime(string? raw)
        {
            return ParseDateTime(raw);
        }

        private static string JoinRecipientAddresses(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out var recipients) || recipients.ValueKind != JsonValueKind.Array)
            {
                return string.Empty;
            }

            var addresses = new List<string>();
            foreach (var recipient in recipients.EnumerateArray())
            {
                var address = GetNestedString(recipient, "emailAddress", "address");
                if (!string.IsNullOrWhiteSpace(address))
                {
                    addresses.Add(address);
                }
            }

            return string.Join(", ", addresses);
        }

        private static bool RecipientContains(string? csv, string? email)
        {
            if (string.IsNullOrWhiteSpace(csv) || string.IsNullOrWhiteSpace(email))
            {
                return false;
            }

            var target = email.Trim();
            return csv
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(part => string.Equals(part, target, StringComparison.OrdinalIgnoreCase));
        }

        private static string? GetString(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out var value))
            {
                return null;
            }

            return value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
        }

        private static string? GetNestedString(JsonElement root, params string[] path)
        {
            var current = root;
            foreach (var segment in path)
            {
                if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out var next))
                {
                    return null;
                }

                current = next;
            }

            return current.ValueKind == JsonValueKind.String ? current.GetString() : null;
        }

        private static bool GetBoolean(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out var value))
            {
                return false;
            }

            return value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String => bool.TryParse(value.GetString(), out var parsed) && parsed,
                _ => false
            };
        }

        private static (string address, string displayName) ParseMailAddress(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return (string.Empty, string.Empty);
            }

            try
            {
                var parsed = new MailAddress(value.Trim());
                return (parsed.Address, parsed.DisplayName ?? string.Empty);
            }
            catch
            {
                return (value.Trim(), string.Empty);
            }
        }

        private static string FormatMailAddress(string address, string? displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName))
            {
                return address;
            }

            return new MailAddress(address, displayName.Trim()).ToString();
        }

        private sealed class ProviderEmailMessage
        {
            public string ExternalId { get; set; } = string.Empty;
            public string Subject { get; set; } = string.Empty;
            public string FromAddress { get; set; } = string.Empty;
            public string FromName { get; set; } = string.Empty;
            public string ToAddresses { get; set; } = string.Empty;
            public string? CcAddresses { get; set; }
            public string? BccAddresses { get; set; }
            public string? BodyText { get; set; }
            public string? BodyHtml { get; set; }
            public string Folder { get; set; } = "Inbox";
            public bool IsRead { get; set; }
            public bool HasAttachments { get; set; }
            public int AttachmentCount { get; set; }
            public string Importance { get; set; } = "Normal";
            public DateTime ReceivedAt { get; set; }
            public DateTime? SentAt { get; set; }
        }
    }

    // DTOs
    public class LinkEmailDto
    {
        public string? MatterId { get; set; }
        public string? ClientId { get; set; }
    }

    public class SendEmailDto
    {
        public string ToAddress { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string BodyText { get; set; } = string.Empty;
        public string? EmailAccountId { get; set; }
        public string? MatterId { get; set; }
        public string? ClientId { get; set; }
    }

    public class ConnectOutlookDto
    {
        public string? Email { get; set; }
        public string? DisplayName { get; set; }
        public string? AccessToken { get; set; }
        public string? RefreshToken { get; set; }
        public string? AuthorizationCode { get; set; }
        public string? RedirectUri { get; set; }
        public string? State { get; set; }
        public string? CodeVerifier { get; set; }
    }

    public class ConnectGmailDto
    {
        public string? Email { get; set; }
        public string? DisplayName { get; set; }
        public string? AccessToken { get; set; }
        public string? RefreshToken { get; set; }
        public string? AuthorizationCode { get; set; }
        public string? RedirectUri { get; set; }
        public string? State { get; set; }
        public string? CodeVerifier { get; set; }
    }

    public sealed class OAuthStatePayload
    {
        public string Provider { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string TenantId { get; set; } = string.Empty;
        public DateTime ExpiresAtUtc { get; set; }
        public string CodeVerifierHash { get; set; } = string.Empty;
    }
}
