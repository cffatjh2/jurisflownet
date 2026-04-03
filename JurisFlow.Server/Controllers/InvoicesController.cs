using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JurisFlow.Server.Data;
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
        private readonly JurisFlowDbContext _context;
        private readonly AuditLogger _auditLogger;
        private readonly FirmStructureService _firmStructure;
        private readonly OutcomeFeePlannerService _outcomeFeePlanner;
        private readonly ClientTransparencyService _clientTransparencyService;
        private readonly MatterAccessService _matterAccess;
        private readonly ILogger<InvoicesController> _logger;

        public InvoicesController(
            JurisFlowDbContext context,
            AuditLogger auditLogger,
            FirmStructureService firmStructure,
            OutcomeFeePlannerService outcomeFeePlanner,
            ClientTransparencyService clientTransparencyService,
            MatterAccessService matterAccess,
            ILogger<InvoicesController> logger)
        {
            _context = context;
            _auditLogger = auditLogger;
            _firmStructure = firmStructure;
            _outcomeFeePlanner = outcomeFeePlanner;
            _clientTransparencyService = clientTransparencyService;
            _matterAccess = matterAccess;
            _logger = logger;
        }

        private async Task<bool> IsPeriodLocked(DateTime date)
        {
            var key = date.ToString("yyyy-MM-dd");
            return await _context.BillingLocks.AnyAsync(b => string.Compare(key, b.PeriodStart) >= 0 && string.Compare(key, b.PeriodEnd) <= 0);
        }

        private static void RecalculateTotals(Invoice invoice)
        {
            var subtotal = invoice.LineItems.Sum(li => li.Amount);
            var total = subtotal + invoice.Tax - invoice.Discount;
            invoice.Subtotal = subtotal;
            invoice.Total = total;
            invoice.Balance = total - invoice.AmountPaid;
        }

        // GET: api/Invoices
        [HttpGet]
        public async Task<IActionResult> GetInvoices([FromQuery] string? entityId, [FromQuery] string? officeId)
        {
            var query = _context.Invoices.AsNoTracking().AsQueryable();
            var isPrivileged = _matterAccess.IsPrivileged(User);

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

            var invoices = await query
                .OrderByDescending(i => i.IssueDate)
                .ToListAsync();
            await RedactSharedInvoiceNotesAsync(invoices);
            return Ok(invoices);
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

            if (await IsPeriodLocked(dto.IssueDate ?? DateTime.UtcNow))
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
            await _context.SaveChangesAsync();
            await _auditLogger.LogAsync(HttpContext, "invoice.create", "Invoice", invoice.Id, $"Client={invoice.ClientId}, Total={invoice.Total}");
            await TryTriggerOutcomeFeePlannerAsync(invoice, "invoice_create");

            return CreatedAtAction(nameof(GetInvoice), new { id = invoice.Id }, invoice);
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

            if (await IsPeriodLocked(DateTime.UtcNow))
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

                _context.InvoiceLineItems.RemoveRange(invoice.LineItems);
                invoice.LineItems.Clear();
                foreach (var li in dto.LineItems)
                {
                    invoice.LineItems.Add(new InvoiceLineItem
                    {
                        Id = Guid.NewGuid().ToString(),
                        InvoiceId = invoice.Id,
                        Type = li.Type ?? "time",
                        Description = li.Description ?? string.Empty,
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
            invoice.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await _auditLogger.LogAsync(HttpContext, "invoice.update", "Invoice", invoice.Id, $"Status={invoice.Status}, Total={invoice.Total}");
            await TryTriggerOutcomeFeePlannerAsync(invoice, "invoice_update");

            return Ok(invoice);
        }

        // POST: api/Invoices/{id}/approve
        [HttpPost("{id}/approve")]
        public async Task<IActionResult> ApproveInvoice(string id)
        {
            var invoice = await _context.Invoices.Include(i => i.LineItems).FirstOrDefaultAsync(i => i.Id == id);
            if (invoice == null) return NotFound();
            if (!await CanManageInvoiceAsync(invoice))
            {
                return Forbid();
            }

            if (await IsPeriodLocked(DateTime.UtcNow))
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
            await _auditLogger.LogAsync(HttpContext, "invoice.approve", "Invoice", invoice.Id, $"Status={invoice.Status}");
            await TryTriggerOutcomeFeePlannerAsync(invoice, "invoice_approve");

            return Ok(invoice);
        }

        // POST: api/Invoices/{id}/send
        [HttpPost("{id}/send")]
        public async Task<IActionResult> SendInvoice(string id)
        {
            var invoice = await _context.Invoices.Include(i => i.LineItems).FirstOrDefaultAsync(i => i.Id == id);
            if (invoice == null) return NotFound();
            if (!await CanManageInvoiceAsync(invoice))
            {
                return Forbid();
            }

            if (await IsPeriodLocked(DateTime.UtcNow))
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
                await QueueClientInvoiceNotificationAsync(invoice);
            }
            await _context.SaveChangesAsync();
            await _auditLogger.LogAsync(HttpContext, "invoice.send", "Invoice", invoice.Id, $"Status={invoice.Status}");
            await TryTriggerOutcomeFeePlannerAsync(invoice, "invoice_send");

            return Ok(invoice);
        }

        // POST: api/Invoices/{id}/pay
        [HttpPost("{id}/pay")]
        public async Task<IActionResult> ApplyPayment(string id, [FromBody] InvoicePaymentDto dto)
        {
            var invoice = await _context.Invoices.Include(i => i.LineItems).FirstOrDefaultAsync(i => i.Id == id);
            if (invoice == null) return NotFound();
            if (!await CanManageInvoiceAsync(invoice))
            {
                return Forbid();
            }

            if (await IsPeriodLocked(DateTime.UtcNow))
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
            await _auditLogger.LogAsync(HttpContext, "invoice.payment.apply", "Invoice", invoice.Id, $"Amount={amount}, Balance={invoice.Balance}");
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
                sb.AppendLine($"{lineNo}|{invoice.IssueDate:yyyyMMdd}|{NormalizeUtbmsCode(li.TaskCode)}|{NormalizeUtbmsCode(li.ActivityCode)}|{NormalizeUtbmsCode(li.ExpenseCode)}|{desc}|{unitCost.ToString("F2", CultureInfo.InvariantCulture)}|{units.ToString("F2", CultureInfo.InvariantCulture)}|{fee.ToString("F2", CultureInfo.InvariantCulture)}");
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

            if (await IsPeriodLocked(DateTime.UtcNow))
            {
                return BadRequest(new { message = "Billing period is locked. Cannot write off invoice." });
            }

            invoice.Status = InvoiceStatus.WrittenOff;
            invoice.Balance = 0m;
            invoice.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            await _auditLogger.LogAsync(HttpContext, "invoice.writeoff", "Invoice", invoice.Id, $"Reason={dto.Reason}");
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

            if (await IsPeriodLocked(DateTime.UtcNow))
            {
                return BadRequest(new { message = "Billing period is locked. Cannot cancel invoice." });
            }

            invoice.Status = InvoiceStatus.Cancelled;
            invoice.Balance = 0m;
            invoice.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            await _auditLogger.LogAsync(HttpContext, "invoice.cancel", "Invoice", invoice.Id, $"Reason={dto.Reason}");
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

            if (await IsPeriodLocked(DateTime.UtcNow))
            {
                return BadRequest(new { message = "Billing period is locked. Cannot delete invoice." });
            }

            _context.InvoiceLineItems.RemoveRange(invoice.LineItems);
            _context.Invoices.Remove(invoice);
            await _context.SaveChangesAsync();
            await _auditLogger.LogAsync(HttpContext, "invoice.delete", "Invoice", id, "Deleted invoice");

            return NoContent();
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

        private async Task<BillingSettings> GetBillingSettingsAsync()
        {
            var settings = await _context.BillingSettings.FirstOrDefaultAsync();
            if (settings == null)
            {
                settings = new BillingSettings();
                _context.BillingSettings.Add(settings);
                await _context.SaveChangesAsync();
            }
            return settings;
        }

        private async Task<FirmSettings> GetFirmSettingsAsync()
        {
            var settings = await _context.FirmSettings.FirstOrDefaultAsync();
            if (settings == null)
            {
                settings = new FirmSettings();
                _context.FirmSettings.Add(settings);
                await _context.SaveChangesAsync();
            }
            return settings;
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

        private async Task TryTriggerOutcomeFeePlannerAsync(Invoice invoice, string triggerType)
        {
            if (invoice == null || (string.IsNullOrWhiteSpace(invoice.MatterId) && string.IsNullOrWhiteSpace(invoice.Id)))
            {
                return;
            }

            try
            {
                await _outcomeFeePlanner.TryProcessTriggerAsync(new OutcomeFeePlanTriggerRequest
                {
                    MatterId = invoice.MatterId,
                    TriggerType = triggerType,
                    TriggerEntityType = nameof(Invoice),
                    TriggerEntityId = invoice.Id,
                    SourceStatus = invoice.Status.ToString()
                }, GetCurrentUserId(), HttpContext.RequestAborted);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Outcome-to-Fee planner trigger failed for invoice {InvoiceId}", invoice.Id);
            }

            try
            {
                await _clientTransparencyService.TryProcessTriggerAsync(new ClientTransparencyTriggerRequest
                {
                    MatterId = invoice.MatterId,
                    TriggerType = triggerType,
                    TriggerEntityType = nameof(Invoice),
                    TriggerEntityId = invoice.Id
                }, GetCurrentUserId(), HttpContext.RequestAborted);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Client transparency trigger failed for invoice {InvoiceId}", invoice.Id);
            }
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
        public string? Type { get; set; }
        public string? Description { get; set; }
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
}
