using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Task = System.Threading.Tasks.Task;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JurisFlow.Server.Data;
using JurisFlow.Server.Enums;
using JurisFlow.Server.Models;
using JurisFlow.Server.Services;

namespace JurisFlow.Server.Controllers
{
    [Route("api/client")]
    [ApiController]
    [Authorize(Roles = "Client")]
    public class ClientPortalController : ControllerBase
    {
        private readonly JurisFlowDbContext _context;
        private readonly IAppFileStorage _fileStorage;
        private readonly AuditLogger _auditLogger;
        private readonly PaymentPlanService _paymentPlanService;
        private readonly DocumentIndexService _documentIndexService;
        private readonly DocumentEncryptionService _documentEncryptionService;
        private readonly SignatureAuditTrailService _signatureAuditTrailService;
        private readonly TenantContext _tenantContext;
        private readonly ClientTransparencyService _clientTransparencyService;
        private readonly MatterClientLinkService _matterClientLinks;
        private const long MaxClientDocumentUploadBodyBytes = 30L * 1024 * 1024;
        private static readonly IReadOnlyDictionary<string, string> AllowedClientDocumentMimeToExtension = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["application/pdf"] = ".pdf",
            ["image/png"] = ".png",
            ["image/jpeg"] = ".jpg",
            ["image/webp"] = ".webp",
            ["application/msword"] = ".doc",
            ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"] = ".docx",
            ["application/vnd.ms-excel"] = ".xls",
            ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"] = ".xlsx",
            ["application/vnd.ms-powerpoint"] = ".ppt",
            ["application/vnd.openxmlformats-officedocument.presentationml.presentation"] = ".pptx",
            ["text/plain"] = ".txt"
        };
        private static readonly HashSet<string> AllowedClientPaymentPlanStatuses = new(StringComparer.OrdinalIgnoreCase)
        {
            "Active",
            "Paused",
            "Cancelled"
        };

        public ClientPortalController(
            JurisFlowDbContext context,
            IAppFileStorage fileStorage,
            AuditLogger auditLogger,
            PaymentPlanService paymentPlanService,
            DocumentIndexService documentIndexService,
            DocumentEncryptionService documentEncryptionService,
            SignatureAuditTrailService signatureAuditTrailService,
            TenantContext tenantContext,
            ClientTransparencyService clientTransparencyService,
            MatterClientLinkService matterClientLinks)
        {
            _context = context;
            _fileStorage = fileStorage;
            _auditLogger = auditLogger;
            _paymentPlanService = paymentPlanService;
            _documentIndexService = documentIndexService;
            _documentEncryptionService = documentEncryptionService;
            _signatureAuditTrailService = signatureAuditTrailService;
            _tenantContext = tenantContext;
            _clientTransparencyService = clientTransparencyService;
            _matterClientLinks = matterClientLinks;
        }

        [HttpGet("matters")]
        public async Task<IActionResult> GetMatters()
        {
            if (!TryGetClientId(out var clientId)) return Unauthorized();
            var visibleMatterIdsQuery = _matterClientLinks.BuildVisibleMatterIdsForClientQuery(clientId);

            var matters = await TenantScope(_context.Matters)
                .Where(m => visibleMatterIdsQuery.Contains(m.Id))
                .OrderByDescending(m => m.OpenDate)
                .ToListAsync();

            var matterIds = matters.Select(m => m.Id).ToList();
            var events = await TenantScope(_context.CalendarEvents)
                .Where(e => e.MatterId != null && matterIds.Contains(e.MatterId))
                .OrderBy(e => e.Date)
                .ToListAsync();

            var eventsByMatter = events
                .GroupBy(e => e.MatterId)
                .ToDictionary(g => g.Key!, g => g.Select(e => (object)new
                {
                    id = e.Id,
                    title = e.Title,
                    date = e.Date,
                    type = e.Type,
                    matterId = e.MatterId
                }).ToList());

            var response = matters.Select(m => new
            {
                id = m.Id,
                caseNumber = m.CaseNumber,
                name = m.Name,
                practiceArea = m.PracticeArea,
                status = m.Status,
                feeStructure = m.FeeStructure,
                openDate = m.OpenDate,
                responsibleAttorney = m.ResponsibleAttorney,
                billableRate = m.BillableRate,
                trustBalance = m.TrustBalance,
                courtType = m.CourtType,
                outcome = m.Outcome,
                events = eventsByMatter.TryGetValue(m.Id, out var list) ? list : new List<object>(),
                timeEntries = Array.Empty<object>(),
                expenses = Array.Empty<object>()
            });

            return Ok(response);
        }

        [HttpGet("matters/{matterId}/transparency")]
        public async Task<IActionResult> GetMatterTransparency(string matterId, [FromQuery] string? lang, CancellationToken ct)
        {
            if (!TryGetClientId(out var clientId)) return Unauthorized();
            if (string.IsNullOrWhiteSpace(matterId)) return BadRequest(new { message = "MatterId is required." });
            var transparencyLanguage = NormalizeTransparencyLanguage(lang);

            var canAccessMatter = await _matterClientLinks.ClientCanAccessMatterAsync(clientId, matterId, ct);
            if (!canAccessMatter) return NotFound();

            var matter = await TenantScope(_context.Matters)
                .FirstOrDefaultAsync(m => m.Id == matterId, ct);
            if (matter == null) return NotFound();

            ClientTransparencySnapshotDetailResult? detail;
            ClientTransparencyTriggerResult? backgroundTrigger = null;
            object? evidenceBundle = null;
            try
            {
                detail = await _clientTransparencyService.GetPublishedSnapshotForMatterAsync(matterId, ct);
                if (detail?.Snapshot == null)
                {
                    // Faz 3: client sees only published snapshots. A background trigger may generate a draft/review item.
                    backgroundTrigger = await _clientTransparencyService.TryProcessTriggerAsync(
                        new ClientTransparencyTriggerRequest
                        {
                            MatterId = matterId,
                            TriggerType = "client_portal_view",
                            TriggerEntityType = "Matter",
                            TriggerEntityId = matterId,
                            Reason = "Client portal view refresh request",
                            ClientAudience = "portal",
                            ClientNotificationMode = "suppress",
                            QueueInternalReviewOnDelayThreshold = true,
                            QueueRetryOnFailure = true
                        },
                        clientId,
                        ct);
                    detail = await _clientTransparencyService.GetPublishedSnapshotForMatterAsync(matterId, ct);
                }
                if (detail?.Snapshot != null)
                {
                    evidenceBundle = await _clientTransparencyService.GetSnapshotEvidenceBundleAsync(detail.Snapshot.Id, ct);
                }
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }

            await _auditLogger.LogAsync(HttpContext, "client.transparency.view", "Matter", matterId, $"SnapshotId={detail?.Snapshot?.Id}, Version={detail?.Snapshot?.VersionNumber}");

            var localizedSummary = detail?.Snapshot?.SnapshotSummary;
            var localizedWhatChanged = detail?.Snapshot?.WhatChangedSummary;
            if (detail?.Snapshot != null)
            {
                localizedSummary = BuildLocalizedTransparencySummary(detail, transparencyLanguage);
                localizedWhatChanged = LocalizeTransparencyWhatChanged(detail.Snapshot.WhatChangedSummary, detail.Snapshot.VersionNumber, transparencyLanguage);
            }

            return Ok(new
            {
                snapshot = detail?.Snapshot == null ? null : new
                {
                    id = detail.Snapshot.Id,
                    matterId = detail.Snapshot.MatterId,
                    versionNumber = detail.Snapshot.VersionNumber,
                    status = detail.Snapshot.Status,
                    generatedAt = detail.Snapshot.GeneratedAt,
                    dataQuality = detail.Snapshot.DataQuality,
                    confidenceScore = detail.Snapshot.ConfidenceScore,
                    summary = localizedSummary,
                    whatChanged = localizedWhatChanged
                },
                pendingReview = detail?.Snapshot == null && backgroundTrigger?.TriggerAccepted == true,
                pendingReviewReason = detail?.Snapshot == null ? backgroundTrigger?.PublishDecision : null,
                riskFlags = detail?.RiskFlags ?? Array.Empty<string>(),
                timeline = (detail?.TimelineItems ?? Array.Empty<ClientTransparencyTimelineItem>()).Select(t => new
                {
                    id = t.Id,
                    orderIndex = t.OrderIndex,
                    phaseKey = t.PhaseKey,
                    label = LocalizeTransparencyTimelineLabel(t.PhaseKey, t.Label, transparencyLanguage),
                    status = t.Status,
                    text = LocalizeTransparencyTimelineText(t.ClientSafeText, transparencyLanguage),
                    startedAtUtc = t.StartedAtUtc,
                    etaAtUtc = t.EtaAtUtc,
                    completedAtUtc = t.CompletedAtUtc
                }),
                delayReasons = (detail?.DelayReasons ?? Array.Empty<ClientTransparencyDelayReason>()).Where(d => d.IsActive).Select(d => new
                {
                    id = d.Id,
                    code = d.ReasonCode,
                    severity = d.Severity,
                    expectedDelayDays = d.ExpectedDelayDays,
                    text = LocalizeTransparencyDelayText(d.ClientSafeText, d.ReasonCode, transparencyLanguage)
                }),
                nextStep = detail?.NextStep == null ? null : new
                {
                    id = detail.NextStep.Id,
                    ownerType = detail.NextStep.OwnerType,
                    status = detail.NextStep.Status,
                    actionText = LocalizeTransparencyNextStepAction(detail.NextStep.ActionText, transparencyLanguage),
                    etaAtUtc = detail.NextStep.EtaAtUtc,
                    blockedByText = LocalizeTransparencyBlockedByText(detail.NextStep.BlockedByText, transparencyLanguage)
                },
                costImpact = detail?.CostImpact == null ? null : new
                {
                    currency = detail.CostImpact.Currency,
                    currentExpectedRangeMin = detail.CostImpact.CurrentExpectedRangeMin,
                    currentExpectedRangeMax = detail.CostImpact.CurrentExpectedRangeMax,
                    deltaRangeMin = detail.CostImpact.DeltaRangeMin,
                    deltaRangeMax = detail.CostImpact.DeltaRangeMax,
                    confidenceBand = detail.CostImpact.ConfidenceBand,
                    driverSummary = LocalizeTransparencyCostDriverSummary(detail.CostImpact.DriverSummary, transparencyLanguage)
                },
                evidence = evidenceBundle
            });
        }

        [HttpGet("invoices")]
        public async Task<IActionResult> GetInvoices()
        {
            if (!TryGetClientId(out var clientId)) return Unauthorized();
            var client = await TenantScope(_context.Clients).FirstOrDefaultAsync(c => c.Id == clientId);

            var invoices = await TenantScope(_context.Invoices)
                .Where(i => i.ClientId == clientId
                    && (i.Status == InvoiceStatus.Sent
                        || i.Status == InvoiceStatus.PartiallyPaid
                        || i.Status == InvoiceStatus.Paid
                        || i.Status == InvoiceStatus.Overdue
                        || i.Status == InvoiceStatus.Cancelled
                        || i.Status == InvoiceStatus.WrittenOff))
                .OrderByDescending(i => i.IssueDate)
                .ToListAsync();

            var response = invoices.Select(i => new
            {
                id = i.Id,
                number = i.Number,
                clientId = i.ClientId,
                client = client == null ? null : new { id = client.Id, name = client.Name },
                status = NormalizeInvoiceStatus(i.Status),
                issueDate = i.IssueDate,
                dueDate = i.DueDate,
                amount = i.Total,
                amountPaid = i.AmountPaid,
                balance = i.Balance,
                notes = i.Notes,
                terms = i.Terms,
                createdAt = i.CreatedAt,
                updatedAt = i.UpdatedAt
            });

            return Ok(response);
        }

        [HttpGet("invoices/{id}")]
        public async Task<IActionResult> GetInvoiceDetails(string id)
        {
            if (!TryGetClientId(out var clientId)) return Unauthorized();

            var invoice = await TenantScope(_context.Invoices)
                .Include(i => i.LineItems)
                .FirstOrDefaultAsync(i => i.Id == id
                    && i.ClientId == clientId
                    && (i.Status == InvoiceStatus.Sent
                        || i.Status == InvoiceStatus.PartiallyPaid
                        || i.Status == InvoiceStatus.Paid
                        || i.Status == InvoiceStatus.Overdue
                        || i.Status == InvoiceStatus.Cancelled
                        || i.Status == InvoiceStatus.WrittenOff));

            if (invoice == null)
            {
                return NotFound();
            }

            var client = await TenantScope(_context.Clients)
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == clientId);

            Matter? matter = null;
            if (!string.IsNullOrWhiteSpace(invoice.MatterId))
            {
                matter = await TenantScope(_context.Matters)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(m => m.Id == invoice.MatterId);
            }

            var firm = await TenantScope(_context.FirmSettings)
                .AsNoTracking()
                .FirstOrDefaultAsync()
                ?? new FirmSettings();

            return Ok(new
            {
                id = invoice.Id,
                number = invoice.Number,
                clientId = invoice.ClientId,
                client = client == null ? null : new
                {
                    id = client.Id,
                    name = client.Name,
                    email = client.Email,
                    company = client.Company
                },
                matterId = invoice.MatterId,
                matter = matter == null ? null : new
                {
                    id = matter.Id,
                    name = matter.Name,
                    caseNumber = matter.CaseNumber,
                    responsibleAttorney = matter.ResponsibleAttorney
                },
                status = NormalizeInvoiceStatus(invoice.Status),
                issueDate = invoice.IssueDate,
                dueDate = invoice.DueDate,
                subtotal = invoice.Subtotal,
                tax = invoice.Tax,
                discount = invoice.Discount,
                amount = invoice.Total,
                total = invoice.Total,
                amountPaid = invoice.AmountPaid,
                balance = invoice.Balance,
                notes = invoice.Notes,
                terms = invoice.Terms,
                createdAt = invoice.CreatedAt,
                updatedAt = invoice.UpdatedAt,
                lineItems = invoice.LineItems
                    .OrderBy(li => li.ServiceDate ?? li.CreatedAt)
                    .ThenBy(li => li.CreatedAt)
                    .Select(li => new
                    {
                        id = li.Id,
                        invoiceId = li.InvoiceId,
                        type = li.Type,
                        description = li.Description,
                        date = li.ServiceDate,
                        quantity = li.Quantity,
                        rate = li.Rate,
                        amount = li.Amount,
                        taskCode = li.TaskCode,
                        expenseCode = li.ExpenseCode,
                        activityCode = li.ActivityCode
                    }),
                firm = new
                {
                    name = firm.FirmName,
                    taxId = firm.TaxId,
                    address = firm.Address,
                    city = firm.City,
                    state = firm.State,
                    zipCode = firm.ZipCode,
                    phone = firm.Phone,
                    website = firm.Website
                }
            });
        }

        [HttpGet("documents")]
        public async Task<IActionResult> GetDocuments()
        {
            if (!TryGetClientId(out var clientId)) return Unauthorized();
            var matterIds = await _matterClientLinks.GetVisibleMatterIdSetForClientAsync(clientId);

            var activeShares = await TenantScope(_context.DocumentShares)
                .Where(s => s.ClientId == clientId && (!s.ExpiresAt.HasValue || s.ExpiresAt > DateTime.UtcNow))
                .ToListAsync();
            var shareMap = activeShares
                .GroupBy(s => s.DocumentId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.SharedAt).First());
            var shareDocIds = shareMap.Keys.ToList();
            var matterIdSet = matterIds;

            var documents = await TenantScope(_context.Documents)
                .Where(d => (d.MatterId != null && matterIdSet.Contains(d.MatterId))
                    || d.UploadedBy == clientId
                    || shareDocIds.Contains(d.Id))
                .OrderByDescending(d => d.CreatedAt)
                .ToListAsync();

            var response = documents.Select(d =>
            {
                shareMap.TryGetValue(d.Id, out var share);
                var permissions = ResolveClientDocumentPermissions(d, clientId, matterIdSet, share);
                if (!permissions.CanView) return null;

                return new
                {
                    id = d.Id,
                    name = d.Name,
                    fileName = d.FileName,
                    filePath = NormalizeFilePath(d.FilePath),
                    fileSize = d.FileSize,
                    mimeType = d.MimeType,
                    matterId = d.MatterId,
                    description = d.Description,
                    tags = d.Tags,
                    category = d.Category,
                    uploadedBy = d.UploadedBy,
                    version = d.Version,
                    createdAt = d.CreatedAt,
                    updatedAt = d.UpdatedAt,
                    permissions = new
                    {
                        canView = permissions.CanView,
                        canDownload = permissions.CanDownload,
                        canComment = permissions.CanComment,
                        canUpload = permissions.CanUpload,
                        sharedAt = share?.SharedAt,
                        expiresAt = share?.ExpiresAt
                    }
                };
            }).Where(d => d != null);

            return Ok(response);
        }

        [HttpPost("documents/upload")]
        [RequestSizeLimit(MaxClientDocumentUploadBodyBytes)]
        [RequestFormLimits(MultipartBodyLengthLimit = MaxClientDocumentUploadBodyBytes)]
        public async Task<IActionResult> UploadDocument([FromForm] IFormFile file, [FromForm] string? matterId, [FromForm] string? description)
        {
            if (!TryGetClientId(out var clientId)) return Unauthorized();
            if (file == null || file.Length == 0)
            {
                return BadRequest(new { message = "File is required." });
            }

            if (file.Length > 25 * 1024 * 1024)
            {
                return BadRequest(new { message = "File size exceeds 25 MB limit." });
            }

            if (!string.IsNullOrEmpty(matterId))
            {
                var canAccessMatter = await _matterClientLinks.ClientCanAccessMatterAsync(clientId, matterId, HttpContext.RequestAborted);
                if (!canAccessMatter)
                {
                    return Forbid();
                }
            }

            var originalFileName = Path.GetFileName(file.FileName);
            if (string.IsNullOrWhiteSpace(originalFileName))
            {
                return BadRequest(new { message = "File name is invalid." });
            }

            byte[] fileBytes;
            await using (var stream = file.OpenReadStream())
            using (var buffer = new MemoryStream())
            {
                await stream.CopyToAsync(buffer);
                fileBytes = buffer.ToArray();
            }

            if (!TryValidateClientDocumentUpload(originalFileName, file.ContentType, fileBytes, out var normalizedMimeType, out var storageExtension, out var validationError))
            {
                return BadRequest(new { message = validationError });
            }

            var displayFileName = NormalizeUploadDisplayFileName(originalFileName, storageExtension);
            var uniqueFileName = $"{Guid.NewGuid():N}{storageExtension}";
            var filePath = GetTenantRelativePath(uniqueFileName);
            DocumentEncryptionPayload? encryptionPayload = null;

            if (_documentEncryptionService.Enabled)
            {
                encryptionPayload = _documentEncryptionService.EncryptBytes(fileBytes);
                await _fileStorage.SaveBytesAsync(filePath, encryptionPayload.Ciphertext, normalizedMimeType);
            }
            else
            {
                await _fileStorage.SaveBytesAsync(filePath, fileBytes, normalizedMimeType);
            }

            var now = DateTime.UtcNow;
            var document = new Document
            {
                Id = Guid.NewGuid().ToString(),
                Name = displayFileName,
                FileName = displayFileName,
                FilePath = GetTenantRelativePath(uniqueFileName),
                FileSize = fileBytes.LongLength,
                MimeType = normalizedMimeType,
                IsEncrypted = encryptionPayload != null,
                EncryptionKeyId = encryptionPayload?.KeyId,
                EncryptionIv = encryptionPayload?.Iv,
                EncryptionTag = encryptionPayload?.Tag,
                EncryptionAlgorithm = encryptionPayload?.Algorithm,
                MatterId = matterId,
                Description = description,
                UploadedBy = clientId,
                CreatedAt = now,
                UpdatedAt = now
            };

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
                Sha256 = ComputeSha256(fileBytes),
                UploadedByUserId = clientId,
                CreatedAt = now
            };

            await using (var transaction = await _context.Database.BeginTransactionAsync())
            {
                _context.Documents.Add(document);
                _context.DocumentVersions.Add(version);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
            }

            bool indexingQueuedForRetry = false;
            try
            {
                await _documentIndexService.UpsertIndexAsync(document, fileBytes);
            }
            catch (Exception ex)
            {
                indexingQueuedForRetry = true;
                await _auditLogger.LogAsync(HttpContext, "client.document.index_failed", "Document", document.Id, $"Indexing failed: {ex.GetType().Name}");
            }
            await _auditLogger.LogAsync(HttpContext, "client.document.upload", "Document", document.Id, $"MatterId={matterId}, Name={file.FileName}");

            return Ok(new
            {
                id = document.Id,
                name = document.Name,
                fileName = document.FileName,
                filePath = NormalizeFilePath(document.FilePath),
                fileSize = document.FileSize,
                mimeType = document.MimeType,
                matterId = document.MatterId,
                description = document.Description,
                tags = document.Tags,
                category = document.Category,
                uploadedBy = document.UploadedBy,
                version = document.Version,
                createdAt = document.CreatedAt,
                updatedAt = document.UpdatedAt,
                indexingStatus = indexingQueuedForRetry ? "pending_retry" : "indexed"
            });
        }

        [HttpGet("documents/{id}/download")]
        public async Task<IActionResult> DownloadDocument(string id)
        {
            if (!TryGetClientId(out var clientId)) return Unauthorized();

            var matterIds = await _matterClientLinks.GetVisibleMatterIdSetForClientAsync(clientId);

            var document = await TenantScope(_context.Documents).FirstOrDefaultAsync(d => d.Id == id);

            if (document == null)
            {
                return NotFound(new { message = "Document not found" });
            }

            var share = await TenantScope(_context.DocumentShares)
                .FirstOrDefaultAsync(s => s.DocumentId == id && s.ClientId == clientId && (!s.ExpiresAt.HasValue || s.ExpiresAt > DateTime.UtcNow));
            var permissions = ResolveClientDocumentPermissions(document, clientId, matterIds, share);
            if (!permissions.CanView || !permissions.CanDownload)
            {
                return Forbid();
            }

            return await DownloadDocumentFileAsync(
                document.FilePath,
                document.FileName,
                document.MimeType,
                document.IsEncrypted,
                document.EncryptionIv,
                document.EncryptionTag);
        }

        [HttpGet("documents/{id}/comments")]
        public async Task<IActionResult> GetDocumentComments(string id)
        {
            if (!TryGetClientId(out var clientId)) return Unauthorized();
            var document = await TenantScope(_context.Documents).FirstOrDefaultAsync(d => d.Id == id);
            if (document == null) return NotFound(new { message = "Document not found" });

            var matterIds = await _matterClientLinks.GetVisibleMatterIdSetForClientAsync(clientId);
            var share = await TenantScope(_context.DocumentShares)
                .FirstOrDefaultAsync(s => s.DocumentId == id && s.ClientId == clientId && (!s.ExpiresAt.HasValue || s.ExpiresAt > DateTime.UtcNow));
            var permissions = ResolveClientDocumentPermissions(document, clientId, matterIds, share);
            if (!permissions.CanView) return Forbid();

            var comments = await TenantScope(_context.DocumentComments)
                .Where(c => c.DocumentId == id)
                .OrderBy(c => c.CreatedAt)
                .ToListAsync();

            var userIds = comments.Where(c => !string.IsNullOrWhiteSpace(c.AuthorUserId)).Select(c => c.AuthorUserId!).Distinct().ToList();
            var clientIds = comments.Where(c => !string.IsNullOrWhiteSpace(c.AuthorClientId)).Select(c => c.AuthorClientId!).Distinct().ToList();

            var users = await TenantScope(_context.Users)
                .Where(u => userIds.Contains(u.Id))
                .Select(u => new { u.Id, u.Name })
                .ToListAsync();
            var clients = await TenantScope(_context.Clients)
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

        [HttpPost("documents/{id}/comments")]
        public async Task<IActionResult> AddDocumentComment(string id, [FromBody] DocumentCommentCreateDto dto)
        {
            if (!TryGetClientId(out var clientId)) return Unauthorized();
            if (string.IsNullOrWhiteSpace(dto.Body))
            {
                return BadRequest(new { message = "Comment body is required." });
            }

            var document = await TenantScope(_context.Documents).FirstOrDefaultAsync(d => d.Id == id);
            if (document == null) return NotFound(new { message = "Document not found" });

            var matterIds = await _matterClientLinks.GetVisibleMatterIdSetForClientAsync(clientId);
            var share = await TenantScope(_context.DocumentShares)
                .FirstOrDefaultAsync(s => s.DocumentId == id && s.ClientId == clientId && (!s.ExpiresAt.HasValue || s.ExpiresAt > DateTime.UtcNow));
            var permissions = ResolveClientDocumentPermissions(document, clientId, matterIds, share);
            if (!permissions.CanComment) return Forbid();

            var comment = new DocumentComment
            {
                DocumentId = id,
                Body = dto.Body.Trim(),
                AuthorClientId = clientId,
                AuthorType = "Client",
                CreatedAt = DateTime.UtcNow
            };

            _context.DocumentComments.Add(comment);
            await _context.SaveChangesAsync();

            await _auditLogger.LogAsync(HttpContext, "client.document.comment", "DocumentComment", comment.Id, $"Document={id}");

            return Ok(new
            {
                id = comment.Id,
                documentId = comment.DocumentId,
                body = comment.Body,
                createdAt = comment.CreatedAt,
                authorType = comment.AuthorType
            });
        }

        [HttpGet("notifications")]
        public async Task<IActionResult> GetNotifications()
        {
            if (!TryGetClientId(out var clientId)) return Unauthorized();
            var items = await TenantScope(_context.Notifications)
                .Where(n => n.ClientId == clientId)
                .OrderByDescending(n => n.CreatedAt)
                .Take(100)
                .ToListAsync();

            return Ok(items.Select(n => new
            {
                id = n.Id,
                title = n.Title,
                message = n.Message,
                type = n.Type,
                link = n.Link,
                read = n.Read,
                createdAt = n.CreatedAt
            }));
        }

        [HttpPost("notifications/{id}/read")]
        public async Task<IActionResult> MarkNotificationRead(string id)
        {
            if (!TryGetClientId(out var clientId)) return Unauthorized();
            var notif = await TenantScope(_context.Notifications).FirstOrDefaultAsync(n => n.Id == id && n.ClientId == clientId);
            if (notif == null) return NotFound();
            notif.Read = true;
            await _context.SaveChangesAsync();
            return NoContent();
        }

        [HttpPost("notifications/read-all")]
        public async Task<IActionResult> MarkAllNotificationsRead()
        {
            if (!TryGetClientId(out var clientId)) return Unauthorized();
            await TenantScope(_context.Notifications)
                .Where(n => n.ClientId == clientId && !n.Read)
                .ExecuteUpdateAsync(setters => setters.SetProperty(n => n.Read, true));
            return NoContent();
        }

        [HttpGet("profile")]
        public async Task<IActionResult> GetProfile()
        {
            if (!TryGetClientId(out var clientId)) return Unauthorized();
            var client = await TenantScope(_context.Clients).FirstOrDefaultAsync(c => c.Id == clientId);
            if (client == null) return NotFound();

            return Ok(new
            {
                id = client.Id,
                name = client.Name,
                email = client.Email,
                phone = client.Phone,
                mobile = client.Mobile,
                company = client.Company,
                type = client.Type,
                status = client.Status,
                address = client.Address,
                city = client.City,
                state = client.State,
                zipCode = client.ZipCode,
                country = client.Country
            });
        }

        [HttpPut("profile")]
        public async Task<IActionResult> UpdateProfile([FromBody] ClientProfileUpdateDto dto)
        {
            if (!TryGetClientId(out var clientId)) return Unauthorized();
            var client = await TenantScope(_context.Clients).FirstOrDefaultAsync(c => c.Id == clientId);
            if (client == null) return NotFound();

            client.Name = dto.Name ?? client.Name;
            client.Phone = dto.Phone;
            client.Mobile = dto.Mobile;
            client.Address = dto.Address;
            client.City = dto.City;
            client.State = dto.State;
            client.ZipCode = dto.ZipCode;
            client.Country = dto.Country;
            client.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await _auditLogger.LogAsync(HttpContext, "client.profile.update", "Client", client.Id, "Client profile updated.");

            return Ok(new
            {
                id = client.Id,
                name = client.Name,
                email = client.Email,
                phone = client.Phone,
                mobile = client.Mobile,
                company = client.Company,
                type = client.Type,
                status = client.Status,
                address = client.Address,
                city = client.City,
                state = client.State,
                zipCode = client.ZipCode,
                country = client.Country
            });
        }

        [HttpGet("payment-plans")]
        public async Task<IActionResult> GetPaymentPlans()
        {
            if (!TryGetClientId(out var clientId)) return Unauthorized();

            var plans = await TenantScope(_context.PaymentPlans)
                .Where(p => p.ClientId == clientId)
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync();

            return Ok(plans);
        }

        [HttpPost("payment-plans")]
        public async Task<IActionResult> CreatePaymentPlan([FromBody] ClientPaymentPlanCreateDto dto)
        {
            if (!TryGetClientId(out var clientId)) return Unauthorized();

            if (dto.InstallmentAmount <= 0)
            {
                return BadRequest(new { message = "Installment amount must be positive." });
            }

            Invoice? invoice = null;
            if (!string.IsNullOrWhiteSpace(dto.InvoiceId))
            {
                invoice = await TenantScope(_context.Invoices).FirstOrDefaultAsync(i => i.Id == dto.InvoiceId && i.ClientId == clientId);
                if (invoice == null)
                {
                    return BadRequest(new { message = "Invoice not found." });
                }
            }

            var total = dto.TotalAmount ?? ((double?)invoice?.Balance ?? 0d);
            if (total <= 0)
            {
                return BadRequest(new { message = "Total amount must be greater than 0." });
            }

            if (dto.InstallmentAmount > total + 0.01)
            {
                return BadRequest(new { message = "Installment amount cannot exceed total amount." });
            }

            if (dto.AutoPayEnabled && string.IsNullOrWhiteSpace(dto.AutoPayMethod))
            {
                return BadRequest(new { message = "AutoPay method is required when AutoPay is enabled." });
            }

            var startDate = dto.StartDate ?? DateTime.UtcNow;
            var plan = new PaymentPlan
            {
                Id = Guid.NewGuid().ToString(),
                ClientId = clientId,
                InvoiceId = dto.InvoiceId,
                Name = string.IsNullOrWhiteSpace(dto.Name) ? $"Payment Plan {DateTime.UtcNow:yyyyMMdd}" : dto.Name.Trim(),
                TotalAmount = total,
                InstallmentAmount = dto.InstallmentAmount,
                Frequency = string.IsNullOrWhiteSpace(dto.Frequency) ? "Monthly" : dto.Frequency.Trim(),
                StartDate = startDate,
                NextRunDate = startDate,
                RemainingAmount = total,
                Status = "Active",
                AutoPayEnabled = dto.AutoPayEnabled,
                AutoPayMethod = dto.AutoPayMethod,
                AutoPayReference = dto.AutoPayReference,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.PaymentPlans.Add(plan);
            await _context.SaveChangesAsync();

            await _auditLogger.LogAsync(HttpContext, "client.payment_plan.create", "PaymentPlan", plan.Id, $"ClientId={clientId}, Total={plan.TotalAmount}");

            return Ok(plan);
        }

        [HttpPut("payment-plans/{id}")]
        public async Task<IActionResult> UpdatePaymentPlan(string id, [FromBody] ClientPaymentPlanUpdateDto dto)
        {
            if (!TryGetClientId(out var clientId)) return Unauthorized();

            var plan = await TenantScope(_context.PaymentPlans).FirstOrDefaultAsync(p => p.Id == id && p.ClientId == clientId);
            if (plan == null) return NotFound();

            if (!string.IsNullOrWhiteSpace(dto.Name)) plan.Name = dto.Name.Trim();
            if (!string.IsNullOrWhiteSpace(dto.Status))
            {
                var normalizedStatus = dto.Status.Trim();
                if (!AllowedClientPaymentPlanStatuses.Contains(normalizedStatus))
                {
                    return BadRequest(new { message = "Invalid payment plan status." });
                }

                plan.Status = AllowedClientPaymentPlanStatuses
                    .First(s => string.Equals(s, normalizedStatus, StringComparison.OrdinalIgnoreCase));
            }
            if (dto.AutoPayEnabled.HasValue) plan.AutoPayEnabled = dto.AutoPayEnabled.Value;
            if (dto.AutoPayMethod != null) plan.AutoPayMethod = dto.AutoPayMethod;
            if (dto.AutoPayReference != null) plan.AutoPayReference = dto.AutoPayReference;

            if (plan.AutoPayEnabled && string.IsNullOrWhiteSpace(plan.AutoPayMethod))
            {
                return BadRequest(new { message = "AutoPay method is required when AutoPay is enabled." });
            }

            plan.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            await _auditLogger.LogAsync(HttpContext, "client.payment_plan.update", "PaymentPlan", plan.Id, $"Status={plan.Status}, AutoPay={plan.AutoPayEnabled}");

            return Ok(plan);
        }

        [HttpPost("payment-plans/{id}/run")]
        public async Task<IActionResult> RunPaymentPlan(string id)
        {
            if (!TryGetClientId(out var clientId)) return Unauthorized();

            var plan = await TenantScope(_context.PaymentPlans).FirstOrDefaultAsync(p => p.Id == id && p.ClientId == clientId);
            if (plan == null) return NotFound();

            var now = DateTime.UtcNow;
            if (!plan.AutoPayEnabled)
            {
                return BadRequest(new { message = "Manual payment plan execution is not available in the client portal." });
            }

            if (!string.Equals(plan.Status, "Active", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { message = "Payment plan is not active." });
            }

            if (plan.RemainingAmount <= 0)
            {
                return BadRequest(new { message = "Payment plan has no remaining balance." });
            }

            if (plan.NextRunDate > now.AddMinutes(1))
            {
                return BadRequest(new { message = "Payment plan is not due yet." });
            }

            var client = await TenantScope(_context.Clients).FirstOrDefaultAsync(c => c.Id == clientId);
            var transaction = await _paymentPlanService.RunPlanAsync(plan, clientId, client?.Email, client?.Name);
            if (transaction == null)
            {
                return BadRequest(new { message = "Payment plan is not active or has no remaining balance." });
            }

            await _auditLogger.LogAsync(HttpContext, "client.payment_plan.run", "PaymentPlan", plan.Id, $"Amount={transaction.Amount}");
            return Ok(new { plan, transaction });
        }

        [HttpGet("signatures")]
        public async Task<IActionResult> GetSignatureRequests()
        {
            if (!TryGetClientId(out var clientId)) return Unauthorized();
            var client = await TenantScope(_context.Clients).FirstOrDefaultAsync(c => c.Id == clientId);

            var requests = await TenantScope(_context.SignatureRequests)
                .Where(r => r.ClientId == clientId || (client != null && r.SignerEmail == client.Email))
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();

            var docIds = requests.Select(r => r.DocumentId).Distinct().ToList();
            var docs = await TenantScope(_context.Documents)
                .Where(d => docIds.Contains(d.Id))
                .Select(d => new { d.Id, d.Name })
                .ToListAsync();
            var docMap = docs.ToDictionary(d => d.Id, d => d.Name);

            var response = requests.Select(r => new
            {
                id = r.Id,
                documentId = r.DocumentId,
                document = docMap.TryGetValue(r.DocumentId, out var name) ? new { id = r.DocumentId, name } : null,
                status = NormalizeSignatureStatus(r.Status),
                signedAt = r.SignedAt,
                createdAt = r.CreatedAt,
                expiresAt = r.ExpiresAt,
                verificationStatus = r.VerificationStatus,
                verificationMethod = r.VerificationMethod,
                reminderCount = r.ReminderCount
            });

            return Ok(response);
        }

        [HttpPost("sign/{id}")]
        public async Task<IActionResult> SignRequest(string id, [FromBody] ClientSignatureDto dto)
        {
            if (!TryGetClientId(out var clientId)) return Unauthorized();
            var client = await TenantScope(_context.Clients).FirstOrDefaultAsync(c => c.Id == clientId);

            var request = await TenantScope(_context.SignatureRequests)
                .FirstOrDefaultAsync(r => r.Id == id);
            if (request == null) return NotFound();

            var isSigner = client != null && string.Equals(request.SignerEmail, client.Email, StringComparison.OrdinalIgnoreCase);
            if (!isSigner)
            {
                return Forbid();
            }

            if (request.Status == "Signed")
            {
                return BadRequest(new { message = "Document already signed." });
            }

            if (IsSignatureExpired(request))
            {
                await MarkSignatureExpiredAsync(request);
                return BadRequest(new { message = "Signature request has expired." });
            }

            if (dto.ConsentAccepted != true)
            {
                return BadRequest(new { message = "Electronic consent is required to sign." });
            }

            if (RequiresVerification(request) && !IsVerificationPassed(request))
            {
                return BadRequest(new { message = "Signer verification is required before signing." });
            }

            request.Status = "Signed";
            request.SignedAt = DateTime.UtcNow;
            request.UpdatedAt = DateTime.UtcNow;
            request.SignerIp = HttpContext.Connection.RemoteIpAddress?.ToString();
            request.SignerUserAgent = Request.Headers.UserAgent.ToString();
            request.SignerLocation = dto.SignerLocation;

            await _context.SaveChangesAsync();
            await _auditLogger.LogAsync(HttpContext, "client.signature.sign", "SignatureRequest", id, $"Signer={request.SignerEmail}");
            await _signatureAuditTrailService.LogAsync(HttpContext, request, "ConsentProvided", "Client", clientId, client?.Email, new { dto.ConsentVersion });
            await _signatureAuditTrailService.LogAsync(HttpContext, request, "Signed", "Client", clientId, client?.Email);

            return Ok(new { message = "Document signed.", signedAt = request.SignedAt });
        }

        [HttpGet("appointments")]
        public async Task<IActionResult> GetAppointments()
        {
            if (!TryGetClientId(out var clientId)) return Unauthorized();
            var appointments = await TenantScope(_context.AppointmentRequests)
                .Where(a => a.ClientId == clientId)
                .OrderByDescending(a => a.RequestedDate)
                .ToListAsync();

            return Ok(appointments);
        }

        [HttpPost("appointments")]
        public async Task<IActionResult> CreateAppointment([FromBody] ClientAppointmentCreateDto dto)
        {
            if (!TryGetClientId(out var clientId)) return Unauthorized();

            if (dto.RequestedDate == default)
            {
                return BadRequest(new { message = "Requested date is required." });
            }

            if (dto.RequestedDate < DateTime.UtcNow.AddMinutes(-5))
            {
                return BadRequest(new { message = "Requested date must be in the future." });
            }

            if (dto.Duration < 15 || dto.Duration > 240)
            {
                return BadRequest(new { message = "Duration must be between 15 and 240 minutes." });
            }

            var allowedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "consultation",
                "meeting",
                "call",
                "court"
            };

            if (!string.IsNullOrWhiteSpace(dto.Type) && !allowedTypes.Contains(dto.Type))
            {
                return BadRequest(new { message = "Invalid appointment type." });
            }

            if (!string.IsNullOrEmpty(dto.MatterId))
            {
                var canAccessMatter = await _matterClientLinks.ClientCanAccessMatterAsync(clientId, dto.MatterId, HttpContext.RequestAborted);
                if (!canAccessMatter)
                {
                    return Forbid();
                }
            }

            var appointment = new AppointmentRequest
            {
                Id = Guid.NewGuid().ToString(),
                ClientId = clientId,
                MatterId = dto.MatterId,
                RequestedDate = dto.RequestedDate,
                Duration = dto.Duration,
                Type = string.IsNullOrWhiteSpace(dto.Type) ? "consultation" : dto.Type.ToLowerInvariant(),
                Notes = dto.Notes,
                Status = "pending",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.AppointmentRequests.Add(appointment);
            await _context.SaveChangesAsync();

            await _auditLogger.LogAsync(HttpContext, "client.appointment.create", "AppointmentRequest", appointment.Id, $"RequestedDate={dto.RequestedDate:o}");

            return Ok(appointment);
        }

        private bool TryGetClientId(out string clientId)
        {
            clientId = GetClientId() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(clientId);
        }

        private string RequireTenantId()
        {
            if (string.IsNullOrWhiteSpace(_tenantContext.TenantId))
            {
                throw new InvalidOperationException("Tenant context is missing.");
            }
            return _tenantContext.TenantId;
        }

        private IQueryable<T> TenantScope<T>(IQueryable<T> query) where T : class
        {
            var tenantId = RequireTenantId();
            return query.Where(e => EF.Property<string>(e, "TenantId") == tenantId);
        }

        private string GetTenantRelativePath(string fileName)
        {
            var tenantId = RequireTenantId();
            return $"uploads/{tenantId}/{fileName}";
        }

        private async Task<IActionResult> DownloadDocumentFileAsync(
            string relativePath,
            string fileName,
            string? mimeType,
            bool isEncrypted,
            string? encryptionIv,
            string? encryptionTag)
        {
            var tenantId = RequireTenantId();
            var normalizedRelativePath = _fileStorage.NormalizeRelativePath(relativePath);
            var expectedRelativePrefix = $"uploads/{tenantId}/";
            if (!normalizedRelativePath.StartsWith(expectedRelativePrefix, StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { message = "Invalid file path." });
            }

            if (!await _fileStorage.ExistsAsync(normalizedRelativePath))
            {
                return NotFound(new { message = "File not found" });
            }

            Response.Headers["X-Content-Type-Options"] = "nosniff";

            var storedBytes = await _fileStorage.ReadBytesAsync(normalizedRelativePath);
            if (isEncrypted)
            {
                if (string.IsNullOrWhiteSpace(encryptionIv) || string.IsNullOrWhiteSpace(encryptionTag))
                {
                    return BadRequest(new { message = "Encrypted file metadata is missing." });
                }

                var bytes = _documentEncryptionService.DecryptBytes(storedBytes, encryptionIv, encryptionTag);
                return File(bytes, mimeType ?? "application/octet-stream", fileName);
            }

            return File(storedBytes, mimeType ?? "application/octet-stream", fileName);
        }

        private string? GetClientId()
        {
            return User.FindFirst("clientId")?.Value
                   ?? User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                   ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        }

        private static string NormalizeInvoiceStatus(InvoiceStatus status)
        {
            return status switch
            {
                InvoiceStatus.Draft => "Draft",
                InvoiceStatus.PendingApproval => "Pending Approval",
                InvoiceStatus.Approved => "Approved",
                InvoiceStatus.Sent => "Sent",
                InvoiceStatus.PartiallyPaid => "Partially Paid",
                InvoiceStatus.Paid => "Paid",
                InvoiceStatus.Overdue => "Overdue",
                InvoiceStatus.WrittenOff => "Written Off",
                InvoiceStatus.Cancelled => "Cancelled",
                _ => status.ToString()
            };
        }

        private static string NormalizeSignatureStatus(string? status)
        {
            if (string.IsNullOrWhiteSpace(status)) return "pending";
            var normalized = status.ToLowerInvariant();
            return normalized switch
            {
                "sent" => "pending",
                "pending" => "pending",
                "viewed" => "pending",
                "signed" => "signed",
                "declined" => "declined",
                "voided" => "declined",
                "expired" => "expired",
                _ => normalized
            };
        }

        private static string NormalizeFilePath(string? filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return string.Empty;
            return filePath.StartsWith("/") ? filePath : "/" + filePath;
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

        private static bool IsSignatureExpired(SignatureRequest request)
        {
            return request.ExpiresAt.HasValue && request.ExpiresAt.Value <= DateTime.UtcNow;
        }

        private static string NormalizeTransparencyLanguage(string? lang)
        {
            if (string.IsNullOrWhiteSpace(lang)) return "en";
            var normalized = lang.Trim().ToLowerInvariant();
            if (normalized.StartsWith("tr")) return "tr";
            return "en";
        }

        private static bool IsTurkishTransparencyLanguage(string lang)
        {
            return string.Equals(lang, "tr", StringComparison.OrdinalIgnoreCase);
        }

        private static string LocalizeTransparencyTimelineLabel(string? phaseKey, string? fallback, string lang)
        {
            if (!IsTurkishTransparencyLanguage(lang) || string.IsNullOrWhiteSpace(fallback)) return fallback ?? string.Empty;
            return (phaseKey ?? string.Empty).ToLowerInvariant() switch
            {
                "matter_intake" => "Dosya Açılışı ve İnceleme",
                "court_and_filing" => "Mahkeme / Dosyalama Süreci",
                "firm_work" => "Mevcut Ofis Çalışması",
                "billing" => "Faturalama ve Ödemeler",
                _ => fallback
            };
        }

        private static string? LocalizeTransparencyTimelineText(string? text, string lang)
        {
            if (!IsTurkishTransparencyLanguage(lang) || string.IsNullOrWhiteSpace(text)) return text;

            return text switch
            {
                "Your matter is active in our workflow and being tracked for the next milestones." =>
                    "Dosyanız iş akışımızda aktif ve bir sonraki kilometre taşları için takip ediliyor.",
                "A filing update requires correction before the next court step can proceed." =>
                    "Bir dosyalama güncellemesi düzeltme gerektiriyor; bir sonraki mahkeme adımı öncesinde düzeltme yapılacak.",
                "A recent filing was accepted and court activity is moving forward." =>
                    "Yakın tarihli bir dosyalama kabul edildi ve mahkeme süreci ilerliyor.",
                "Recent court docket activity has been recorded and reviewed." =>
                    "Yakın tarihli mahkeme kayıt hareketleri kaydedildi ve incelendi.",
                "We are monitoring for the next court or filing update." =>
                    "Bir sonraki mahkeme veya dosyalama güncellemesini takip ediyoruz.",
                "Our team is actively working through scheduled tasks for your matter." =>
                    "Ekibimiz dosyanız için planlanan görevler üzerinde aktif olarak çalışıyor.",
                "There is no currently scheduled internal task due immediately in this view." =>
                    "Bu görünümde şu anda acil vadeli planlanmış iç görev görünmüyor.",
                "No invoice has been issued yet for the tracked work shown in this summary." =>
                    "Bu özette görünen takip edilen işler için henüz fatura düzenlenmedi.",
                "The latest invoice for this matter is paid." =>
                    "Bu dosya için en son fatura ödendi.",
                "A payment has been received and any remaining balance is still being tracked." =>
                    "Bir ödeme alındı ve kalan bakiye varsa takip edilmeye devam ediyor.",
                "Billing is active and payment timing may affect scheduling and cost timing." =>
                    "Faturalama aktif; ödeme zamanlaması planlama ve maliyet zamanlamasını etkileyebilir.",
                _ => text
            };
        }

        private static string? LocalizeTransparencyDelayText(string? text, string? reasonCode, string lang)
        {
            if (!IsTurkishTransparencyLanguage(lang) || string.IsNullOrWhiteSpace(text)) return text;

            return (reasonCode ?? string.Empty).ToLowerInvariant() switch
            {
                "filing_correction" => "Yeniden gönderimden önce bir dosyalama düzeltmesi yapılıyor.",
                "internal_task_backlog" => "Hedef tarihi kaçıran bir iç görev yeniden önceliklendirildi.",
                "payment_timing" => "Faturalama zamanlaması bazı takip işlerinin ne zaman planlanacağını etkileyebilir.",
                _ => text
            };
        }

        private static string LocalizeTransparencyNextStepAction(string actionText, string lang)
        {
            if (!IsTurkishTransparencyLanguage(lang) || string.IsNullOrWhiteSpace(actionText)) return actionText;

            const string taskPrefix = "Our team will work on the next scheduled item: ";
            if (actionText.StartsWith(taskPrefix, StringComparison.Ordinal))
            {
                var title = actionText[taskPrefix.Length..].Trim();
                if (title.EndsWith(".")) title = title[..^1];
                return $"Ekibimiz sıradaki planlanmış iş üzerinde çalışacak: {title}.";
            }

            return actionText switch
            {
                "We will prepare a corrected filing package and resubmit it." =>
                    "Düzeltilmiş bir dosyalama paketi hazırlayıp yeniden göndereceğiz.",
                "Please review the current billing status in the portal and contact us if you need billing support." =>
                    "Lütfen portalda mevcut fatura durumunu inceleyin; faturalama desteğine ihtiyacınız varsa bizimle iletişime geçin.",
                "No immediate next step is required at this time." =>
                    "Şu anda acil bir sonraki adım gerekmiyor.",
                "We are monitoring for the next court or workflow milestone and will update this page when it changes." =>
                    "Bir sonraki mahkeme veya iş akışı kilometre taşını takip ediyoruz; değiştiğinde bu sayfayı güncelleyeceğiz.",
                _ => actionText
            };
        }

        private static string? LocalizeTransparencyBlockedByText(string? text, string lang)
        {
            if (!IsTurkishTransparencyLanguage(lang) || string.IsNullOrWhiteSpace(text)) return text;
            return text switch
            {
                "Open balance timing may affect scheduling." => "Açık bakiye zamanlaması planlamayı etkileyebilir.",
                _ => text
            };
        }

        private static string? LocalizeTransparencyCostDriverSummary(string? text, string lang)
        {
            if (!IsTurkishTransparencyLanguage(lang) || string.IsNullOrWhiteSpace(text)) return text;
            return text switch
            {
                "Estimate is based on current case stage, work volume assumptions, and billing signals." =>
                    "Tahmin; mevcut dosya aşaması, iş hacmi varsayımları ve faturalama sinyallerine dayanır.",
                _ => text
            };
        }

        private static string? LocalizeTransparencyWhatChanged(string? text, int versionNumber, string lang)
        {
            if (!IsTurkishTransparencyLanguage(lang) || string.IsNullOrWhiteSpace(text)) return text;

            const string initialPrefix = "Initial client transparency snapshot generated (version ";
            if (text.StartsWith(initialPrefix, StringComparison.Ordinal))
            {
                return $"İlk müvekkil şeffaflık özeti oluşturuldu (sürüm {versionNumber}).";
            }

            const string refreshPrefix = "Snapshot refreshed from version ";
            if (text.StartsWith(refreshPrefix, StringComparison.Ordinal))
            {
                var trigger = ExtractBetween(text, "(trigger: ", ")");
                var versionPart = text[refreshPrefix.Length..];
                var splitIdx = versionPart.IndexOf(" to ", StringComparison.Ordinal);
                if (splitIdx > 0)
                {
                    var previous = versionPart[..splitIdx].Trim();
                    var rest = versionPart[(splitIdx + 4)..];
                    var current = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? versionNumber.ToString();
                    return trigger == null
                        ? $"Özet {previous}. sürümden {current}. sürüme yenilendi."
                        : $"Özet {previous}. sürümden {current}. sürüme yenilendi (tetikleyici: {trigger}).";
                }
            }

            return text;
        }

        private static string? BuildLocalizedTransparencySummary(ClientTransparencySnapshotDetailResult detail, string lang)
        {
            if (!IsTurkishTransparencyLanguage(lang))
            {
                return detail.Snapshot?.SnapshotSummary;
            }

            var snapshot = detail.Snapshot;
            if (snapshot == null) return null;

            var clientName = "Müvekkil";
            var rawSummary = snapshot.SnapshotSummary;
            if (!string.IsNullOrWhiteSpace(rawSummary))
            {
                var commaIndex = rawSummary.IndexOf(',');
                if (commaIndex > 0)
                {
                    clientName = rawSummary[..commaIndex].Trim();
                }
            }

            var nextStepText = detail.NextStep == null
                ? "Bir sonraki adım şu anda beklemede."
                : LocalizeTransparencyNextStepAction(detail.NextStep.ActionText, lang);
            if (!string.IsNullOrWhiteSpace(nextStepText) && !nextStepText.EndsWith("."))
            {
                nextStepText += ".";
            }

            var hasDelay = detail.DelayReasons.Any(d => d.IsActive);
            var delayText = hasDelay
                ? " Şu anda takip edilen bir gecikme faktörü bulunuyor."
                : " Şu anda aktif bir gecikme faktörü işaretlenmiyor.";

            string costText;
            if (detail.CostImpact == null)
            {
                costText = " Maliyet etkisi tahmini henüz mevcut değil.";
            }
            else
            {
                var min = detail.CostImpact.CurrentExpectedRangeMin?.ToString("N0") ?? "0";
                var max = detail.CostImpact.CurrentExpectedRangeMax?.ToString("N0") ?? "0";
                var confidence = detail.CostImpact.ConfidenceBand switch
                {
                    "high" => "yüksek",
                    "medium" => "orta",
                    "low" => "düşük",
                    _ => detail.CostImpact.ConfidenceBand ?? "bilinmiyor"
                };
                costText = $" Beklenen maliyet etkisi şu anda {detail.CostImpact.Currency} {min}-{max} aralığında tahmin ediliyor ({confidence} güven).";
            }

            return $"{clientName}, bu güncelleme dosyanızın mevcut durumunu ve olası bir sonraki adımı özetler. Bir sonraki adım: {nextStepText}{delayText}{costText}";
        }

        private static string? ExtractBetween(string input, string start, string end)
        {
            var startIdx = input.IndexOf(start, StringComparison.Ordinal);
            if (startIdx < 0) return null;
            startIdx += start.Length;
            var endIdx = input.IndexOf(end, startIdx, StringComparison.Ordinal);
            if (endIdx < 0 || endIdx <= startIdx) return null;
            return input[startIdx..endIdx];
        }

        private class DocumentPermission
        {
            public bool CanView { get; set; }
            public bool CanDownload { get; set; }
            public bool CanComment { get; set; }
            public bool CanUpload { get; set; }
        }

        private static DocumentPermission ResolveClientDocumentPermissions(
            Document document,
            string clientId,
            HashSet<string> matterIds,
            DocumentShare? share)
        {
            var isClientUpload = !string.IsNullOrWhiteSpace(document.UploadedBy)
                && string.Equals(document.UploadedBy, clientId, StringComparison.OrdinalIgnoreCase);
            var hasMatterAccess = !string.IsNullOrWhiteSpace(document.MatterId)
                && matterIds.Contains(document.MatterId);

            var permission = new DocumentPermission
            {
                CanView = isClientUpload || hasMatterAccess,
                CanDownload = isClientUpload || hasMatterAccess,
                CanComment = isClientUpload || hasMatterAccess,
                CanUpload = false
            };

            if (!isClientUpload && share != null)
            {
                permission.CanView = share.CanView;
                permission.CanDownload = share.CanDownload;
                permission.CanComment = share.CanComment;
                permission.CanUpload = share.CanUpload;
            }

            return permission;
        }

        private async Task MarkSignatureExpiredAsync(SignatureRequest request)
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

        private static bool TryValidateClientDocumentUpload(
            string originalFileName,
            string? contentType,
            byte[] fileBytes,
            out string normalizedMimeType,
            out string storageExtension,
            out string error)
        {
            normalizedMimeType = string.Empty;
            storageExtension = string.Empty;
            error = string.Empty;

            var candidateMimeType = string.IsNullOrWhiteSpace(contentType)
                ? string.Empty
                : contentType.Trim().ToLowerInvariant();
            if (!AllowedClientDocumentMimeToExtension.TryGetValue(candidateMimeType, out var mappedExtension))
            {
                error = "File type is not allowed.";
                return false;
            }

            var originalExtension = Path.GetExtension(Path.GetFileName(originalFileName));
            if (!string.IsNullOrWhiteSpace(originalExtension) &&
                !string.Equals(originalExtension, mappedExtension, StringComparison.OrdinalIgnoreCase))
            {
                error = "File extension does not match the declared content type.";
                return false;
            }

            if (!MatchesDocumentSignature(candidateMimeType, fileBytes))
            {
                error = "File content does not match the declared content type.";
                return false;
            }

            normalizedMimeType = candidateMimeType;
            storageExtension = mappedExtension;
            return true;
        }

        private static bool MatchesDocumentSignature(string mimeType, byte[] bytes)
        {
            if (bytes.Length == 0)
            {
                return false;
            }

            return mimeType switch
            {
                "application/pdf" => StartsWith(bytes, "%PDF"u8),
                "image/png" => StartsWith(bytes, new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
                "image/jpeg" => StartsWith(bytes, new byte[] { 0xFF, 0xD8, 0xFF }),
                "image/webp" => StartsWith(bytes, "RIFF"u8) && bytes.Length > 12 && bytes.AsSpan(8).StartsWith("WEBP"u8),
                "application/msword" or "application/vnd.ms-excel" or "application/vnd.ms-powerpoint" =>
                    StartsWith(bytes, new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 }),
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
                    or "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
                    or "application/vnd.openxmlformats-officedocument.presentationml.presentation" =>
                    StartsWith(bytes, new byte[] { 0x50, 0x4B, 0x03, 0x04 }) || StartsWith(bytes, new byte[] { 0x50, 0x4B, 0x05, 0x06 }),
                "text/plain" => !bytes.Contains((byte)0),
                _ => false
            };
        }

        private static bool StartsWith(byte[] bytes, ReadOnlySpan<byte> signature)
        {
            return bytes.AsSpan().StartsWith(signature);
        }

        private static string NormalizeUploadDisplayFileName(string fileName, string requiredExtension)
        {
            var candidate = Path.GetFileName(fileName);
            var baseName = Path.GetFileNameWithoutExtension(candidate);
            if (string.IsNullOrWhiteSpace(baseName))
            {
                baseName = "document";
            }

            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = new string(baseName.Where(ch => !invalidChars.Contains(ch)).ToArray()).Trim();
            if (string.IsNullOrWhiteSpace(sanitized))
            {
                sanitized = "document";
            }

            if (sanitized.Length > 80)
            {
                sanitized = sanitized[..80];
            }

            return $"{sanitized}{requiredExtension}";
        }

        private static string ComputeSha256(byte[] data)
        {
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(data);
            var sb = new StringBuilder();
            foreach (var b in hash)
            {
                sb.Append(b.ToString("x2"));
            }
            return sb.ToString();
        }
    }

    public class ClientProfileUpdateDto
    {
        public string? Name { get; set; }
        public string? Phone { get; set; }
        public string? Mobile { get; set; }
        public string? Address { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public string? ZipCode { get; set; }
        public string? Country { get; set; }
    }

    public class ClientSignatureDto
    {
        public string? SignatureData { get; set; }
        public string? SignerLocation { get; set; }
        public bool? ConsentAccepted { get; set; }
        public string? ConsentVersion { get; set; }
    }

    public class ClientAppointmentCreateDto
    {
        public string? MatterId { get; set; }
        public DateTime RequestedDate { get; set; }
        public int Duration { get; set; } = 30;
        public string? Type { get; set; }
        public string? Notes { get; set; }
    }

    public class ClientPaymentPlanCreateDto
    {
        public string? InvoiceId { get; set; }
        public string? Name { get; set; }
        public double? TotalAmount { get; set; }
        public double InstallmentAmount { get; set; }
        public string? Frequency { get; set; }
        public DateTime? StartDate { get; set; }
        public bool AutoPayEnabled { get; set; } = false;
        public string? AutoPayMethod { get; set; }
        public string? AutoPayReference { get; set; }
    }

    public class ClientPaymentPlanUpdateDto
    {
        public string? Name { get; set; }
        public string? Status { get; set; }
        public bool? AutoPayEnabled { get; set; }
        public string? AutoPayMethod { get; set; }
        public string? AutoPayReference { get; set; }
    }
}

