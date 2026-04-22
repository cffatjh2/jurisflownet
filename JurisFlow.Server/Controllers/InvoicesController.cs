using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JurisFlow.Server.Data;
using JurisFlow.Server.DTOs;
using JurisFlow.Server.Enums;
using JurisFlow.Server.Models;
using JurisFlow.Server.Services;
using System.Globalization;
using System.Text;
using Task = System.Threading.Tasks.Task;

namespace JurisFlow.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Policy = "StaffOnly")]
    public class InvoicesController : ControllerBase
    {
        private const int DefaultPageSize = 100;
        private const int MaxPageSize = 100;
        private readonly JurisFlowDbContext _context;
        private readonly AuditLogger _auditLogger;
        private readonly BillingPeriodLockService _billingPeriodLockService;
        private readonly FirmStructureService _firmStructure;
        private readonly MatterWorkflowTriggerDispatcher _workflowTriggerDispatcher;
        private readonly MatterAccessService _matterAccess;
        private readonly ILogger<InvoicesController> _logger;

        public InvoicesController(
            JurisFlowDbContext context,
            AuditLogger auditLogger,
            BillingPeriodLockService billingPeriodLockService,
            FirmStructureService firmStructure,
            MatterWorkflowTriggerDispatcher workflowTriggerDispatcher,
            MatterAccessService matterAccess,
            ILogger<InvoicesController> logger)
        {
            _context = context;
            _auditLogger = auditLogger;
            _billingPeriodLockService = billingPeriodLockService;
            _firmStructure = firmStructure;
            _workflowTriggerDispatcher = workflowTriggerDispatcher;
            _matterAccess = matterAccess;
            _logger = logger;
        }

        private static void RecalculateTotals(Invoice invoice)
        {
            var subtotal = invoice.LineItems.Sum(li => li.Amount);
            var total = subtotal + invoice.Tax - invoice.Discount;
            invoice.Subtotal = subtotal;
            invoice.Total = total;
            invoice.Balance = total - invoice.AmountPaid;
        }

        private void ApplyLineItemValues(InvoiceLineItem target, InvoiceLineItemDto source, DateTime now, bool isNew)
        {
            target.Type = source.Type ?? "time";
            target.Description = source.Description ?? string.Empty;
            target.ServiceDate = source.ServiceDate;
            target.Quantity = source.Quantity ?? 1m;
            target.Rate = source.Rate ?? 0m;
            target.Amount = target.Quantity * target.Rate;
            target.TaskCode = NormalizeUtbmsCode(source.TaskCode);
            target.ExpenseCode = NormalizeUtbmsCode(source.ExpenseCode);
            target.ActivityCode = NormalizeUtbmsCode(source.ActivityCode);
            if (isNew)
            {
                target.CreatedAt = now;
            }

            target.UpdatedAt = now;
        }

        // GET: api/Invoices
        [HttpGet]
        public async Task<IActionResult> GetInvoices(
            [FromQuery] string? entityId,
            [FromQuery] string? officeId,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = DefaultPageSize)
        {
            var query = _context.Invoices.AsNoTracking().AsQueryable();
            var isPrivileged = _matterAccess.IsPrivileged(User);
            var normalizedPage = Math.Max(1, page);
            var normalizedPageSize = NormalizePageSize(pageSize);

            if (!string.IsNullOrWhiteSpace(entityId))
            {
                query = query.Where(i => i.EntityId == entityId);
            }

            if (!string.IsNullOrWhiteSpace(officeId))
            {
                query = query.Where(i => i.OfficeId == officeId);
            }

            if (!isPrivileged)
            {
                var readableBillingMatterIds = _matterAccess.BuildBillingReadableMatterIdsQuery(User);
                query = query.Where(i => !string.IsNullOrWhiteSpace(i.MatterId) && readableBillingMatterIds.Contains(i.MatterId!));
            }

            var totalCount = await query.CountAsync(HttpContext.RequestAborted);
            var invoices = await query
                .OrderByDescending(i => i.IssueDate)
                .ThenByDescending(i => i.CreatedAt)
                .Skip((normalizedPage - 1) * normalizedPageSize)
                .Take(normalizedPageSize)
                .Select(i => new InvoiceListItemDto
                {
                    Id = i.Id,
                    Number = i.Number,
                    ClientId = i.ClientId,
                    MatterId = i.MatterId,
                    EntityId = i.EntityId,
                    OfficeId = i.OfficeId,
                    Status = i.Status,
                    IssueDate = i.IssueDate,
                    DueDate = i.DueDate,
                    Subtotal = i.Subtotal,
                    Tax = i.Tax,
                    Discount = i.Discount,
                    Total = i.Total,
                    AmountPaid = i.AmountPaid,
                    Balance = i.Balance,
                    LineItemCount = i.LineItems.Count,
                    CreatedAt = i.CreatedAt,
                    UpdatedAt = i.UpdatedAt
                })
                .ToListAsync(HttpContext.RequestAborted);

            return Ok(new PagedCollectionResponse<InvoiceListItemDto>
            {
                Items = invoices,
                TotalCount = totalCount,
                Page = normalizedPage,
                PageSize = normalizedPageSize,
                HasMore = normalizedPage * normalizedPageSize < totalCount
            });
        }

        // GET: api/Invoices/{id}
        [HttpGet("{id}")]
        public async Task<IActionResult> GetInvoice(string id)
        {
            var query = _context.Invoices
                .AsNoTracking()
                .Include(i => i.LineItems)
                .AsQueryable();

            if (!_matterAccess.IsPrivileged(User))
            {
                var readableBillingMatterIds = _matterAccess.BuildBillingReadableMatterIdsQuery(User);
                query = query.Where(i => !string.IsNullOrWhiteSpace(i.MatterId) && readableBillingMatterIds.Contains(i.MatterId!));
            }

            var invoice = await query.FirstOrDefaultAsync(i => i.Id == id);
            if (invoice == null) return NotFound();
            await RedactSharedInvoiceNotesAsync(new List<Invoice> { invoice });
            return Ok(invoice);
        }

        // POST: api/Invoices
        [HttpPost]
        public async Task<IActionResult> CreateInvoice([FromBody] InvoiceCreateDto dto)
        {
            try
            {
                var requestedMatterId = string.IsNullOrWhiteSpace(dto.MatterId) ? null : dto.MatterId.Trim();
                var requestedClientId = string.IsNullOrWhiteSpace(dto.ClientId) ? null : dto.ClientId.Trim();

                Matter? selectedMatter = null;
                if (string.IsNullOrWhiteSpace(requestedMatterId))
                {
                    if (!_matterAccess.IsPrivileged(User))
                    {
                        return BadRequest(new { message = "MatterId is required for invoice creation." });
                    }
                }
                else if (!await _matterAccess.CanManageMatterAsync(requestedMatterId, User, cancellationToken: HttpContext.RequestAborted))
                {
                    return Forbid();
                }
                else
                {
                    selectedMatter = await _context.Matters
                        .AsNoTracking()
                        .FirstOrDefaultAsync(m => m.Id == requestedMatterId, HttpContext.RequestAborted);
                    if (selectedMatter == null)
                    {
                        return BadRequest(new { message = "Selected matter was not found." });
                    }
                }

                var resolvedClientId = ResolveInvoiceClientId(requestedClientId, selectedMatter);
                if (string.IsNullOrWhiteSpace(resolvedClientId))
                {
                    return BadRequest(new { message = "ClientId is required for invoice creation." });
                }

                if (!await _context.Clients.AsNoTracking().AnyAsync(c => c.Id == resolvedClientId, HttpContext.RequestAborted))
                {
                    return BadRequest(new { message = "Selected client was not found." });
                }

                if (await _billingPeriodLockService.IsLockedAsync(dto.IssueDate ?? DateTime.UtcNow, HttpContext.RequestAborted))
                {
                    return BadRequest(new { message = "Billing period is locked. Cannot create invoice." });
                }

                var billingSettings = await GetBillingSettingsAsync();
                var invoiceNumber = string.IsNullOrWhiteSpace(dto.Number)
                    ? await GenerateInvoiceNumberAsync(billingSettings.InvoicePrefix)
                    : dto.Number;

                if (billingSettings.UtbmsCodesRequired && dto.LineItems != null)
                {
                    var issues = GetUtbmsIssues(dto.LineItems);
                    if (issues.Count > 0)
                    {
                        return BadRequest(new { message = "UTBMS codes are required for this invoice.", issues });
                    }
                }

                var (resolvedEntityId, resolvedOfficeId) = await _firmStructure.ResolveEntityOfficeFromMatterAsync(dto.MatterId, dto.EntityId, dto.OfficeId);

                var invoice = new Invoice
                {
                    Id = Guid.NewGuid().ToString(),
                    Number = invoiceNumber,
                    ClientId = resolvedClientId,
                    MatterId = requestedMatterId,
                    EntityId = resolvedEntityId,
                    OfficeId = resolvedOfficeId,
                    Status = dto.Status ?? InvoiceStatus.Draft,
                    IssueDate = dto.IssueDate ?? DateTime.UtcNow,
                    DueDate = dto.DueDate,
                    Notes = dto.Notes,
                    Terms = dto.Terms,
                    Discount = dto.Discount ?? 0m,
                    Tax = dto.Tax ?? 0m,
                    AmountPaid = 0m
                };

                if (dto.LineItems != null)
                {
                    foreach (var li in dto.LineItems)
                    {
                        invoice.LineItems.Add(new InvoiceLineItem
                        {
                            Id = Guid.NewGuid().ToString(),
                            Type = li.Type ?? "time",
                            Description = li.Description ?? string.Empty,
                            ServiceDate = li.ServiceDate,
                            Quantity = li.Quantity ?? 1m,
                            Rate = li.Rate ?? 0m,
                            Amount = (li.Quantity ?? 1m) * (li.Rate ?? 0m),
                            TaskCode = NormalizeUtbmsCode(li.TaskCode),
                            ExpenseCode = NormalizeUtbmsCode(li.ExpenseCode),
                            ActivityCode = NormalizeUtbmsCode(li.ActivityCode),
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow
                        });
                    }
                }

                RecalculateTotals(invoice);

                _context.Invoices.Add(invoice);
                if (!await TryPersistInvoiceAsync(invoice, requestedMatterId, resolvedClientId))
                {
                    return BadRequest(new
                    {
                        message = "Invoice could not be created because this tenant still has legacy billing bindings. The system retried without entity and office bindings, but the save still failed."
                    });
                }
                await TryAuditAsync("invoice.create", "Invoice", invoice.Id, $"Client={invoice.ClientId}, Total={invoice.Total}");
                await TryTriggerOutcomeFeePlannerAsync(invoice, "invoice_create");

                return CreatedAtAction(nameof(GetInvoice), new { id = invoice.Id }, invoice);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled invoice creation failure for matter {MatterId} client {ClientId}.", dto.MatterId, dto.ClientId);
                return StatusCode(500, new
                {
                    message = "Invoice creation failed. Please verify billing settings, invoice line items, and firm structure configuration."
                });
            }
        }

        // PUT: api/Invoices/{id}
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateInvoice(string id, [FromBody] InvoiceUpdateDto dto)
        {
            var invoice = await _context.Invoices.Include(i => i.LineItems).FirstOrDefaultAsync(i => i.Id == id);
            if (invoice == null) return NotFound();
            if (!await CanManageInvoiceAsync(invoice))
            {
                return Forbid();
            }

            if (await _billingPeriodLockService.IsLockedAsync(DateTime.UtcNow, HttpContext.RequestAborted))
            {
                return BadRequest(new { message = "Billing period is locked. Cannot update invoice." });
            }

            var requestedMatterId = string.IsNullOrWhiteSpace(dto.MatterId) ? invoice.MatterId : dto.MatterId.Trim();
            var requestedClientId = string.IsNullOrWhiteSpace(dto.ClientId) ? invoice.ClientId : dto.ClientId.Trim();

            if (!string.IsNullOrWhiteSpace(requestedMatterId) &&
                !await _matterAccess.CanManageMatterAsync(requestedMatterId, User, cancellationToken: HttpContext.RequestAborted))
            {
                return Forbid();
            }

            Matter? selectedMatter = null;
            if (!string.IsNullOrWhiteSpace(requestedMatterId))
            {
                selectedMatter = await _context.Matters
                    .AsNoTracking()
                    .FirstOrDefaultAsync(m => m.Id == requestedMatterId, HttpContext.RequestAborted);
                if (selectedMatter == null)
                {
                    return BadRequest(new { message = "Selected matter was not found." });
                }
            }

            var resolvedClientId = ResolveInvoiceClientId(requestedClientId, selectedMatter);
            if (string.IsNullOrWhiteSpace(resolvedClientId))
            {
                return BadRequest(new { message = "ClientId is required for invoice update." });
            }

            if (!await _context.Clients.AsNoTracking().AnyAsync(c => c.Id == resolvedClientId, HttpContext.RequestAborted))
            {
                return BadRequest(new { message = "Selected client was not found." });
            }

            if (!string.IsNullOrWhiteSpace(dto.Number)) invoice.Number = dto.Number;
            invoice.ClientId = resolvedClientId;
            invoice.MatterId = requestedMatterId;
            if (!string.IsNullOrWhiteSpace(dto.EntityId)) invoice.EntityId = dto.EntityId;
            if (!string.IsNullOrWhiteSpace(dto.OfficeId)) invoice.OfficeId = dto.OfficeId;
            if (dto.Status.HasValue) invoice.Status = dto.Status.Value;
            if (dto.IssueDate.HasValue) invoice.IssueDate = dto.IssueDate.Value;
            if (dto.DueDate.HasValue) invoice.DueDate = dto.DueDate.Value;
            if (dto.Notes != null) invoice.Notes = dto.Notes;
            if (dto.Terms != null) invoice.Terms = dto.Terms;
            if (dto.Discount is not null) invoice.Discount = dto.Discount.Value;
            if (dto.Tax is not null) invoice.Tax = dto.Tax.Value;

            // Replace line items if provided
            if (dto.LineItems != null)
            {
                var billingSettings = await GetBillingSettingsAsync();
                if (billingSettings.UtbmsCodesRequired)
                {
                    var issues = GetUtbmsIssues(dto.LineItems);
                    if (issues.Count > 0)
                    {
                        return BadRequest(new { message = "UTBMS codes are required for this invoice.", issues });
                    }
                }
                var patchResult = await PatchInvoiceLineItemsAsync(invoice, dto.LineItems);
                if (patchResult != null)
                {
                    return patchResult;
                }
            }

            RecalculateTotals(invoice);
            invoice.UpdatedAt = DateTime.UtcNow;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Invoice update failed for invoice {InvoiceId}.", id);
                return BadRequest(new
                {
                    message = "Invoice could not be updated with the selected matter or billing structure. Please verify the matter, client, entity, and office assignments."
                });
            }
            await TryAuditAsync("invoice.update", "Invoice", invoice.Id, $"Status={invoice.Status}, Total={invoice.Total}");
            await TryTriggerOutcomeFeePlannerAsync(invoice, "invoice_update");

            return Ok(invoice);
        }

        // POST: api/Invoices/{id}/approve
        [HttpPost("{id}/approve")]
        public async Task<IActionResult> ApproveInvoice(string id)
        {
            var invoice = await _context.Invoices.FirstOrDefaultAsync(i => i.Id == id);
            if (invoice == null) return NotFound();
            if (!await CanManageInvoiceAsync(invoice))
            {
                return Forbid();
            }

            if (await _billingPeriodLockService.IsLockedAsync(DateTime.UtcNow, HttpContext.RequestAborted))
            {
                return BadRequest(new { message = "Billing period is locked. Cannot approve invoice." });
            }

            if (invoice.Status is InvoiceStatus.Cancelled or InvoiceStatus.WrittenOff)
            {
                return BadRequest(new { message = "Cancelled or written-off invoices cannot be approved." });
            }

            invoice.Status = InvoiceStatus.Approved;
            invoice.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await TryAuditAsync("invoice.approve", "Invoice", invoice.Id, $"Status={invoice.Status}");
            await TryTriggerOutcomeFeePlannerAsync(invoice, "invoice_approve");

            return Ok(invoice);
        }

        // POST: api/Invoices/{id}/send
        [HttpPost("{id}/send")]
        public async Task<IActionResult> SendInvoice(string id)
        {
            var invoice = await _context.Invoices.FirstOrDefaultAsync(i => i.Id == id);
            if (invoice == null) return NotFound();
            if (!await CanManageInvoiceAsync(invoice))
            {
                return Forbid();
            }

            if (await _billingPeriodLockService.IsLockedAsync(DateTime.UtcNow, HttpContext.RequestAborted))
            {
                return BadRequest(new { message = "Billing period is locked. Cannot send invoice." });
            }

            if (invoice.Status is InvoiceStatus.Cancelled or InvoiceStatus.WrittenOff)
            {
                return BadRequest(new { message = "Cancelled or written-off invoices cannot be sent." });
            }

            var shouldNotifyClient = invoice.Status is InvoiceStatus.Draft or InvoiceStatus.PendingApproval or InvoiceStatus.Approved;
            if (shouldNotifyClient)
            {
                invoice.Status = InvoiceStatus.Sent;
            }

            invoice.UpdatedAt = DateTime.UtcNow;

            if (shouldNotifyClient)
            {
                try
                {
                    await QueueClientInvoiceNotificationAsync(invoice);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Client invoice notification failed for invoice {InvoiceId}", invoice.Id);
                }
            }

            await _context.SaveChangesAsync();
            await TryAuditAsync("invoice.send", "Invoice", invoice.Id, $"Status={invoice.Status}");
            await TryTriggerOutcomeFeePlannerAsync(invoice, "invoice_send");

            return Ok(invoice);
        }

        // POST: api/Invoices/{id}/pay
        [HttpPost("{id}/pay")]
        public async Task<IActionResult> ApplyPayment(string id, [FromBody] InvoicePaymentDto dto)
        {
            var invoice = await _context.Invoices.FirstOrDefaultAsync(i => i.Id == id);
            if (invoice == null) return NotFound();
            if (!await CanManageInvoiceAsync(invoice))
            {
                return Forbid();
            }

            if (await _billingPeriodLockService.IsLockedAsync(DateTime.UtcNow, HttpContext.RequestAborted))
            {
                return BadRequest(new { message = "Billing period is locked. Cannot apply payment." });
            }

            var amount = dto.Amount;
            if (amount <= 0) return BadRequest(new { message = "Amount must be positive" });

            invoice.AmountPaid += amount;
            invoice.Balance -= amount;
            if (invoice.Balance < 0) invoice.Balance = 0;

            if (invoice.Balance == 0)
            {
                invoice.Status = InvoiceStatus.Paid;
            }
            else if (invoice.Status == InvoiceStatus.Sent || invoice.Status == InvoiceStatus.Approved)
            {
                invoice.Status = InvoiceStatus.PartiallyPaid;
            }

            invoice.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            await TryAuditAsync("invoice.payment.apply", "Invoice", invoice.Id, $"Amount={amount}, Balance={invoice.Balance}");
            await TryTriggerOutcomeFeePlannerAsync(invoice, "invoice_payment_apply");

            return Ok(invoice);
        }

        // GET: api/Invoices/{id}/ledes
        [HttpGet("{id}/ledes")]
        public async Task<IActionResult> ExportLedes(string id)
        {
            var invoice = await _context.Invoices.Include(i => i.LineItems).FirstOrDefaultAsync(i => i.Id == id);
            if (invoice == null) return NotFound();
            if (!await CanReadInvoiceAsync(invoice))
            {
                return Forbid();
            }

            var billingSettings = await GetBillingSettingsAsync();
            if (!billingSettings.LedesEnabled)
            {
                return BadRequest(new { message = "LEDES export is disabled in billing settings." });
            }

            if (billingSettings.UtbmsCodesRequired)
            {
                var issues = GetUtbmsIssues(invoice.LineItems.Select(li => new InvoiceLineItemDto
                {
                    Type = li.Type,
                    Description = li.Description,
                    Quantity = li.Quantity,
                    Rate = li.Rate,
                    TaskCode = li.TaskCode,
                    ExpenseCode = li.ExpenseCode,
                    ActivityCode = li.ActivityCode
                }).ToList());
                if (issues.Count > 0)
                {
                    return BadRequest(new { message = "UTBMS codes are required before exporting LEDES.", issues });
                }
            }

            var firmSettings = await GetFirmSettingsAsync();
            var lawFirmId = string.IsNullOrWhiteSpace(firmSettings.LedesFirmId) ? "JF-DEFAULT" : firmSettings.LedesFirmId;
            var firmName = string.IsNullOrWhiteSpace(firmSettings.FirmName) ? "JurisFlow" : firmSettings.FirmName;

            // LEDES 1998B export (pipe-delimited)
            var sb = new StringBuilder();
            sb.AppendLine("INVOICE_DATE|INVOICE_NUMBER|CLIENT_ID|LAW_FIRM_ID|LAW_FIRM_NAME|MATTER_ID|INVOICE_TOTAL");
            sb.AppendLine($"{invoice.IssueDate:yyyyMMdd}|{invoice.Number}|{invoice.ClientId}|{lawFirmId}|{firmName}|{invoice.MatterId}|{invoice.Total.ToString("F2", CultureInfo.InvariantCulture)}");
            sb.AppendLine("LINE_ITEM_NUMBER|LINE_ITEM_DATE|TASK_CODE|ACTIVITY_CODE|EXPENSE_CODE|LINE_ITEM_DESCRIPTION|LINE_ITEM_UNIT_COST|LINE_ITEM_UNITS|LINE_ITEM_TOTAL");

            int lineNo = 1;
            foreach (var li in invoice.LineItems)
            {
                var desc = (li.Description ?? string.Empty).Replace("|", "/");
                var units = li.Quantity;
                var unitCost = li.Rate;
                var fee = li.Amount;
                var serviceDate = li.ServiceDate ?? invoice.IssueDate;
                sb.AppendLine($"{lineNo}|{serviceDate:yyyyMMdd}|{NormalizeUtbmsCode(li.TaskCode)}|{NormalizeUtbmsCode(li.ActivityCode)}|{NormalizeUtbmsCode(li.ExpenseCode)}|{desc}|{unitCost.ToString("F2", CultureInfo.InvariantCulture)}|{units.ToString("F2", CultureInfo.InvariantCulture)}|{fee.ToString("F2", CultureInfo.InvariantCulture)}");
                lineNo++;
            }

            return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/plain", $"invoice_{invoice.Number ?? invoice.Id}_ledes.dat");
        }

        // POST: api/Invoices/{id}/write-off
        [HttpPost("{id}/write-off")]
        public async Task<IActionResult> WriteOff(string id, [FromBody] InvoiceWriteOffDto dto)
        {
            var invoice = await _context.Invoices.FirstOrDefaultAsync(i => i.Id == id);
            if (invoice == null) return NotFound();
            if (!await CanManageInvoiceAsync(invoice))
            {
                return Forbid();
            }

            if (await _billingPeriodLockService.IsLockedAsync(DateTime.UtcNow, HttpContext.RequestAborted))
            {
                return BadRequest(new { message = "Billing period is locked. Cannot write off invoice." });
            }

            invoice.Status = InvoiceStatus.WrittenOff;
            invoice.Balance = 0m;
            invoice.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            await TryAuditAsync("invoice.writeoff", "Invoice", invoice.Id, $"Reason={dto.Reason}");
            await TryTriggerOutcomeFeePlannerAsync(invoice, "invoice_writeoff");

            return Ok(invoice);
        }

        // POST: api/Invoices/{id}/cancel
        [HttpPost("{id}/cancel")]
        public async Task<IActionResult> Cancel(string id, [FromBody] InvoiceCancelDto dto)
        {
            var invoice = await _context.Invoices.FirstOrDefaultAsync(i => i.Id == id);
            if (invoice == null) return NotFound();
            if (!await CanManageInvoiceAsync(invoice))
            {
                return Forbid();
            }

            if (await _billingPeriodLockService.IsLockedAsync(DateTime.UtcNow, HttpContext.RequestAborted))
            {
                return BadRequest(new { message = "Billing period is locked. Cannot cancel invoice." });
            }

            invoice.Status = InvoiceStatus.Cancelled;
            invoice.Balance = 0m;
            invoice.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            await TryAuditAsync("invoice.cancel", "Invoice", invoice.Id, $"Reason={dto.Reason}");
            await TryTriggerOutcomeFeePlannerAsync(invoice, "invoice_cancel");

            return Ok(invoice);
        }

        // DELETE: api/Invoices/{id}
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteInvoice(string id)
        {
            var invoice = await _context.Invoices.Include(i => i.LineItems).FirstOrDefaultAsync(i => i.Id == id);
            if (invoice == null) return NoContent();
            if (!await CanManageInvoiceAsync(invoice))
            {
                return Forbid();
            }

            if (await _billingPeriodLockService.IsLockedAsync(DateTime.UtcNow, HttpContext.RequestAborted))
            {
                return BadRequest(new { message = "Billing period is locked. Cannot delete invoice." });
            }

            var invoiceLineIds = invoice.LineItems.Select(li => li.Id).ToList();
            if (invoiceLineIds.Count > 0)
            {
                var blockers = await GetInvoiceLineDeletionBlockersAsync(invoice.Id, invoiceLineIds);
                if (blockers.Count > 0)
                {
                    return BadRequest(new
                    {
                        message = "Invoice cannot be deleted because one or more line items have downstream billing allocations, ledger links, or e-billing bindings.",
                        blockedLineItemIds = blockers.Select(b => b.LineItemId).Distinct(StringComparer.Ordinal).ToList(),
                        blockers = blockers.Select(ToDeletionBlockerPayload).ToList()
                    });
                }
            }

            _context.InvoiceLineItems.RemoveRange(invoice.LineItems);
            _context.Invoices.Remove(invoice);
            await _context.SaveChangesAsync();
            await TryAuditAsync("invoice.delete", "Invoice", id, "Deleted invoice");

            return NoContent();
        }

        private async Task<IActionResult?> PatchInvoiceLineItemsAsync(Invoice invoice, IReadOnlyList<InvoiceLineItemDto> requestedLineItems)
        {
            var now = DateTime.UtcNow;
            var existingLineItems = invoice.LineItems
                .OrderBy(li => li.CreatedAt)
                .ThenBy(li => li.Id)
                .ToList();
            var existingById = existingLineItems.ToDictionary(li => li.Id, StringComparer.Ordinal);
            var matchedExistingIds = new HashSet<string>(StringComparer.Ordinal);
            var positionalCandidates = new Queue<InvoiceLineItem>(existingLineItems);

            foreach (var lineItemDto in requestedLineItems)
            {
                var requestedLineId = NormalizeLineItemId(lineItemDto.Id);
                if (requestedLineId != null)
                {
                    if (!existingById.TryGetValue(requestedLineId, out var existingLine))
                    {
                        return BadRequest(new { message = $"Invoice line item '{requestedLineId}' was not found on this invoice." });
                    }

                    ApplyLineItemValues(existingLine, lineItemDto, now, isNew: false);
                    matchedExistingIds.Add(existingLine.Id);
                    continue;
                }

                while (positionalCandidates.Count > 0 && matchedExistingIds.Contains(positionalCandidates.Peek().Id))
                {
                    positionalCandidates.Dequeue();
                }

                if (positionalCandidates.Count > 0)
                {
                    var existingLine = positionalCandidates.Dequeue();
                    ApplyLineItemValues(existingLine, lineItemDto, now, isNew: false);
                    matchedExistingIds.Add(existingLine.Id);
                    continue;
                }

                var newLine = new InvoiceLineItem
                {
                    Id = Guid.NewGuid().ToString(),
                    InvoiceId = invoice.Id
                };
                ApplyLineItemValues(newLine, lineItemDto, now, isNew: true);
                invoice.LineItems.Add(newLine);
            }

            var lineIdsToDelete = existingLineItems
                .Where(li => !matchedExistingIds.Contains(li.Id))
                .Select(li => li.Id)
                .ToList();

            if (lineIdsToDelete.Count > 0)
            {
                var blockers = await GetInvoiceLineDeletionBlockersAsync(invoice.Id, lineIdsToDelete);
                if (blockers.Count > 0)
                {
                    return BadRequest(new
                    {
                        message = "One or more invoice line items cannot be deleted because they have downstream billing allocations, ledger links, or e-billing bindings.",
                        blockedLineItemIds = blockers.Select(b => b.LineItemId).Distinct(StringComparer.Ordinal).ToList(),
                        blockers = blockers.Select(ToDeletionBlockerPayload).ToList()
                    });
                }

                var linesToDelete = existingLineItems
                    .Where(li => lineIdsToDelete.Contains(li.Id, StringComparer.Ordinal))
                    .ToList();

                _context.InvoiceLineItems.RemoveRange(linesToDelete);
                foreach (var lineToDelete in linesToDelete)
                {
                    invoice.LineItems.Remove(lineToDelete);
                }
            }

            return null;
        }

        private async Task<List<InvoiceLineDeletionBlocker>> GetInvoiceLineDeletionBlockersAsync(string invoiceId, IReadOnlyCollection<string> lineIds)
        {
            if (lineIds.Count == 0)
            {
                return new List<InvoiceLineDeletionBlocker>();
            }

            var payorAllocationRows = await _context.InvoiceLinePayorAllocations
                .AsNoTracking()
                .Where(a => a.InvoiceId == invoiceId && lineIds.Contains(a.InvoiceLineItemId))
                .Select(a => new { a.InvoiceLineItemId, HasEbillingProfile = !string.IsNullOrWhiteSpace(a.EbillingProfileJson) })
                .ToListAsync(HttpContext.RequestAborted);

            var paymentAllocationRows = await _context.BillingPaymentAllocations
                .AsNoTracking()
                .Where(a => a.InvoiceId == invoiceId && a.InvoiceLineItemId != null && lineIds.Contains(a.InvoiceLineItemId))
                .Select(a => a.InvoiceLineItemId!)
                .ToListAsync(HttpContext.RequestAborted);

            var ledgerRows = await _context.BillingLedgerEntries
                .AsNoTracking()
                .Where(a => a.InvoiceId == invoiceId && a.InvoiceLineItemId != null && lineIds.Contains(a.InvoiceLineItemId))
                .Select(a => a.InvoiceLineItemId!)
                .ToListAsync(HttpContext.RequestAborted);

            var blockers = new Dictionary<string, InvoiceLineDeletionBlocker>(StringComparer.Ordinal);

            foreach (var lineId in lineIds)
            {
                blockers[lineId] = new InvoiceLineDeletionBlocker(lineId);
            }

            foreach (var row in payorAllocationRows)
            {
                var blocker = blockers[row.InvoiceLineItemId];
                blocker.InvoiceLinePayorAllocationCount++;
                blocker.HasEbillingBinding |= row.HasEbillingProfile;
            }

            foreach (var lineId in paymentAllocationRows)
            {
                blockers[lineId].BillingPaymentAllocationCount++;
            }

            foreach (var lineId in ledgerRows)
            {
                blockers[lineId].BillingLedgerEntryCount++;
            }

            return blockers.Values
                .Where(b => b.InvoiceLinePayorAllocationCount > 0 || b.BillingPaymentAllocationCount > 0 || b.BillingLedgerEntryCount > 0 || b.HasEbillingBinding)
                .OrderBy(b => b.LineItemId, StringComparer.Ordinal)
                .ToList();
        }

        private static object ToDeletionBlockerPayload(InvoiceLineDeletionBlocker blocker)
        {
            return new
            {
                lineItemId = blocker.LineItemId,
                invoiceLinePayorAllocations = blocker.InvoiceLinePayorAllocationCount,
                billingPaymentAllocations = blocker.BillingPaymentAllocationCount,
                billingLedgerEntries = blocker.BillingLedgerEntryCount,
                hasEbillingBinding = blocker.HasEbillingBinding
            };
        }

        private static string? NormalizeLineItemId(string? lineItemId)
        {
            return string.IsNullOrWhiteSpace(lineItemId) ? null : lineItemId.Trim();
        }

        private static string? ResolveInvoiceClientId(string? requestedClientId, Matter? matter)
        {
            var normalizedClientId = string.IsNullOrWhiteSpace(requestedClientId) ? null : requestedClientId.Trim();
            if (matter == null)
            {
                return normalizedClientId;
            }

            if (string.IsNullOrWhiteSpace(matter.ClientId))
            {
                return null;
            }

            return matter.ClientId;
        }

        private static int NormalizePageSize(int pageSize)
        {
            if (pageSize <= 0)
            {
                return DefaultPageSize;
            }

            return Math.Clamp(pageSize, 1, MaxPageSize);
        }

        private async Task QueueClientInvoiceNotificationAsync(Invoice invoice)
        {
            var client = await _context.Clients
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == invoice.ClientId, HttpContext.RequestAborted);
            if (client == null)
            {
                return;
            }

            _context.Notifications.Add(new Notification
            {
                ClientId = client.Id,
                Title = "New Invoice Available",
                Message = $"Invoice {invoice.Number ?? invoice.Id} for ${invoice.Total:0.00} is now available in your client portal.",
                Type = "info",
                Link = "tab:invoices"
            });
        }

        private async Task TryAuditAsync(string action, string entity, string entityId, string? details = null)
        {
            try
            {
                await _auditLogger.LogAsync(HttpContext, action, entity, entityId, details);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Audit logging failed during invoice workflow. Action={Action} EntityId={EntityId}", action, entityId);
            }
        }

        private async Task<bool> TryPersistInvoiceAsync(Invoice invoice, string? requestedMatterId, string resolvedClientId)
        {
            try
            {
                await _context.SaveChangesAsync();
                return true;
            }
            catch (DbUpdateException ex) when (!string.IsNullOrWhiteSpace(invoice.EntityId) || !string.IsNullOrWhiteSpace(invoice.OfficeId))
            {
                _logger.LogWarning(ex, "Invoice create failed with entity/office binding for matter {MatterId} client {ClientId}. Retrying without entity/office.", requestedMatterId, resolvedClientId);
                invoice.EntityId = null;
                invoice.OfficeId = null;

                try
                {
                    await _context.SaveChangesAsync();
                    return true;
                }
                catch (DbUpdateException retryEx)
                {
                    _logger.LogError(retryEx, "Invoice create retry failed for matter {MatterId} client {ClientId}.", requestedMatterId, resolvedClientId);
                    return false;
                }
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Invoice create failed for matter {MatterId} client {ClientId}.", requestedMatterId, resolvedClientId);
                return false;
            }
        }

        private async Task<BillingSettings> GetBillingSettingsAsync()
        {
            try
            {
                var settings = await _context.BillingSettings.FirstOrDefaultAsync();
                return settings ?? new BillingSettings();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Billing settings lookup failed. Using in-memory defaults for current invoice operation.");
                return new BillingSettings();
            }
        }

        private async Task<FirmSettings> GetFirmSettingsAsync()
        {
            try
            {
                var settings = await _context.FirmSettings.FirstOrDefaultAsync();
                return settings ?? new FirmSettings();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Firm settings lookup failed. Using in-memory defaults for current invoice operation.");
                return new FirmSettings();
            }
        }

        private async Task<bool> CanReadInvoiceAsync(Invoice invoice)
        {
            if (_matterAccess.IsPrivileged(User))
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(invoice.MatterId))
            {
                return false;
            }

            var readableBillingMatterIds = _matterAccess.BuildBillingReadableMatterIdsQuery(User);
            return await readableBillingMatterIds.AnyAsync(id => id == invoice.MatterId, HttpContext.RequestAborted);
        }

        private async Task<bool> CanManageInvoiceAsync(Invoice invoice)
        {
            if (string.IsNullOrWhiteSpace(invoice.MatterId))
            {
                return _matterAccess.IsPrivileged(User);
            }

            return await _matterAccess.CanManageMatterAsync(invoice.MatterId, User, cancellationToken: HttpContext.RequestAborted);
        }

        private async Task RedactSharedInvoiceNotesAsync(List<Invoice> invoices)
        {
            if (_matterAccess.IsPrivileged(User) || invoices.Count == 0)
            {
                return;
            }

            var matterIds = invoices
                .Where(i => !string.IsNullOrWhiteSpace(i.MatterId))
                .Select(i => i.MatterId!)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (matterIds.Count == 0)
            {
                return;
            }

            var matterMap = await _context.Matters
                .AsNoTracking()
                .Where(m => matterIds.Contains(m.Id))
                .ToDictionaryAsync(m => m.Id, StringComparer.Ordinal);

            foreach (var invoice in invoices)
            {
                if (string.IsNullOrWhiteSpace(invoice.MatterId))
                {
                    continue;
                }

                if (matterMap.TryGetValue(invoice.MatterId, out var matter) && !_matterAccess.CanSeeMatterNotes(matter, User))
                {
                    invoice.Notes = null;
                    invoice.Terms = null;
                }
            }
        }

        private async Task<string> GenerateInvoiceNumberAsync(string prefix)
        {
            try
            {
                var normalized = string.IsNullOrWhiteSpace(prefix) ? "INV-" : prefix.Trim();
                if (!normalized.EndsWith("-", StringComparison.Ordinal))
                {
                    normalized += "-";
                }

                var year = DateTime.UtcNow.Year;
                var yearPrefix = $"{normalized}{year}-";
                var count = await _context.Invoices.CountAsync(i => i.Number != null && i.Number.StartsWith(yearPrefix));
                return $"{yearPrefix}{(count + 1).ToString("D4", CultureInfo.InvariantCulture)}";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Invoice number sequence lookup failed. Falling back to timestamp-based invoice number.");
                var normalized = string.IsNullOrWhiteSpace(prefix) ? "INV-" : prefix.Trim();
                if (!normalized.EndsWith("-", StringComparison.Ordinal))
                {
                    normalized += "-";
                }

                return $"{normalized}{DateTime.UtcNow:yyyy-MMddHHmmss}";
            }
        }

        private List<string> GetUtbmsIssues(IEnumerable<InvoiceLineItemDto> lineItems)
        {
            var issues = new List<string>();
            var lineNumber = 1;

            foreach (var item in lineItems)
            {
                var type = (item.Type ?? string.Empty).Trim().ToLowerInvariant();
                var taskCode = NormalizeUtbmsCode(item.TaskCode);
                var activityCode = NormalizeUtbmsCode(item.ActivityCode);
                var expenseCode = NormalizeUtbmsCode(item.ExpenseCode);

                if (type == "time")
                {
                    if (string.IsNullOrWhiteSpace(activityCode))
                    {
                        issues.Add($"Line {lineNumber}: Activity code is required for time entries.");
                    }
                }

                if (type == "expense")
                {
                    if (string.IsNullOrWhiteSpace(expenseCode))
                    {
                        issues.Add($"Line {lineNumber}: Expense code is required for expense entries.");
                    }
                }

                lineNumber++;
            }

            return issues;
        }

        private string? NormalizeUtbmsCode(string? code)
        {
            if (string.IsNullOrWhiteSpace(code)) return null;
            var trimmed = code.Trim();
            var split = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return split.Length > 0 ? split[0].Trim() : trimmed;
        }

        private Task TryTriggerOutcomeFeePlannerAsync(Invoice invoice, string triggerType)
        {
            if (invoice == null || (string.IsNullOrWhiteSpace(invoice.MatterId) && string.IsNullOrWhiteSpace(invoice.Id)))
            {
                return Task.CompletedTask;
            }

            try
            {
                _workflowTriggerDispatcher.TryEnqueue(
                    GetCurrentUserId(),
                    new OutcomeFeePlanTriggerRequest
                    {
                        MatterId = invoice.MatterId,
                        TriggerType = triggerType,
                        TriggerEntityType = nameof(Invoice),
                        TriggerEntityId = invoice.Id,
                        SourceStatus = invoice.Status.ToString()
                    },
                    new ClientTransparencyTriggerRequest
                    {
                        MatterId = invoice.MatterId,
                        TriggerType = triggerType,
                        TriggerEntityType = nameof(Invoice),
                        TriggerEntityId = invoice.Id
                    });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Workflow trigger enqueue failed for invoice {InvoiceId}", invoice.Id);
            }

            return Task.CompletedTask;
        }

        private string GetCurrentUserId()
        {
            return User.FindFirst("sub")?.Value
                ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? "system";
        }
    }

    // DTOs
    public class InvoiceLineItemDto
    {
        public string? Id { get; set; }
        public string? Type { get; set; }
        public string? Description { get; set; }
        public DateTime? ServiceDate { get; set; }
        public decimal? Quantity { get; set; }
        public decimal? Rate { get; set; }
        public string? TaskCode { get; set; }
        public string? ExpenseCode { get; set; }
        public string? ActivityCode { get; set; }
    }

    public class InvoiceCreateDto
    {
        public string? Number { get; set; }
        public string ClientId { get; set; } = string.Empty;
        public string? MatterId { get; set; }
        public string? EntityId { get; set; }
        public string? OfficeId { get; set; }
        public InvoiceStatus? Status { get; set; }
        public DateTime? IssueDate { get; set; }
        public DateTime? DueDate { get; set; }
        public decimal? Tax { get; set; }
        public decimal? Discount { get; set; }
        public string? Notes { get; set; }
        public string? Terms { get; set; }
        public List<InvoiceLineItemDto>? LineItems { get; set; }
    }

    public class InvoiceUpdateDto : InvoiceCreateDto
    {
    }

    public class InvoicePaymentDto
    {
        public decimal Amount { get; set; }
        public string? Reference { get; set; }
    }

    public class InvoiceWriteOffDto
    {
        public string? Reason { get; set; }
    }

    public class InvoiceCancelDto
    {
        public string? Reason { get; set; }
    }

    internal sealed class InvoiceLineDeletionBlocker
    {
        public InvoiceLineDeletionBlocker(string lineItemId)
        {
            LineItemId = lineItemId;
        }

        public string LineItemId { get; }
        public int InvoiceLinePayorAllocationCount { get; set; }
        public int BillingPaymentAllocationCount { get; set; }
        public int BillingLedgerEntryCount { get; set; }
        public bool HasEbillingBinding { get; set; }
    }
}
