using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using JurisFlow.Server.Data;
using JurisFlow.Server.Enums;
using JurisFlow.Server.Models;
using JurisFlow.Server.Services;
using Stripe;
using System.Data;
using System.Globalization;
using System.Linq;
using Microsoft.Extensions.Logging;
using Task = System.Threading.Tasks.Task;

namespace JurisFlow.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Policy = "StaffOnly")]
    public class PaymentsController : ControllerBase
    {
        private readonly JurisFlowDbContext _context;
        private readonly AuditLogger _auditLogger;
        private readonly StripePaymentService _stripePaymentService;
        private readonly TenantContext _tenantContext;
        private readonly HashSet<string> _allowedCurrencies;
        private readonly OutcomeFeePlannerService _outcomeFeePlanner;
        private readonly ClientTransparencyService _clientTransparencyService;
        private readonly ILogger<PaymentsController> _logger;

        public PaymentsController(
            JurisFlowDbContext context,
            IConfiguration configuration,
            AuditLogger auditLogger,
            StripePaymentService stripePaymentService,
            TenantContext tenantContext,
            OutcomeFeePlannerService outcomeFeePlanner,
            ClientTransparencyService clientTransparencyService,
            ILogger<PaymentsController> logger)
        {
            _context = context;
            _auditLogger = auditLogger;
            _stripePaymentService = stripePaymentService;
            _tenantContext = tenantContext;
            _allowedCurrencies = LoadAllowedCurrencies(configuration);
            _outcomeFeePlanner = outcomeFeePlanner;
            _clientTransparencyService = clientTransparencyService;
            _logger = logger;
        }

        private static class PaymentStatuses
        {
            public const string Pending = "Pending";
            public const string Processing = "Processing";
            public const string Succeeded = "Succeeded";
            public const string Failed = "Failed";
            public const string Refunded = "Refunded";
            public const string PartiallyRefunded = "Partially Refunded";
            public const string RequiresAction = "Requires Action";
        }

        private async Task<bool> IsPeriodLocked(DateTime date)
        {
            var targetDate = DateOnly.FromDateTime(date.ToUniversalTime());
            var billingLocks = await TenantScope(_context.BillingLocks)
                .AsNoTracking()
                .Select(b => new { b.PeriodStart, b.PeriodEnd })
                .ToListAsync();

            foreach (var billingLock in billingLocks)
            {
                if (!TryParseDateOnly(billingLock.PeriodStart, out var periodStart) ||
                    !TryParseDateOnly(billingLock.PeriodEnd, out var periodEnd))
                {
                    continue;
                }

                if (targetDate >= periodStart && targetDate <= periodEnd)
                {
                    return true;
                }
            }

            return false;
        }

        // POST: api/payments/create-checkout
        [HttpPost("create-checkout")]
        public async Task<ActionResult> CreateCheckoutSession([FromBody] CreateCheckoutDto dto)
        {
            if (dto == null)
            {
                return BadRequest(new { message = "Request body is required." });
            }

            var userId = User.FindFirst("sub")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var normalizedAmount = NormalizeMoney(dto.Amount);
            if (normalizedAmount <= 0m)
            {
                return BadRequest(new { message = "Amount must be greater than zero." });
            }

            var normalizedCurrency = NormalizeCurrency(dto.Currency, _allowedCurrencies);
            if (normalizedCurrency == null)
            {
                return BadRequest(new { message = "Currency is not allowed for this environment." });
            }

            var paymentRail = NormalizePaymentRail(dto.PaymentRail);
            if (paymentRail == null)
            {
                return BadRequest(new { message = "PaymentRail is invalid. Allowed values: card, ach, echeck, card_or_ach." });
            }

            // Locked period guard
            var txnDate = DateTime.UtcNow;
            if (await IsPeriodLocked(txnDate))
            {
                return BadRequest(new { message = "Billing period is locked. Cannot create checkout in a locked period." });
            }

            // Surcharge guard: do not allow charging above invoice balance if invoiceId provided
            JurisFlow.Server.Models.Invoice? invoice = null;
            if (!string.IsNullOrEmpty(dto.InvoiceId))
            {
                invoice = await GetInvoiceByIdAsync(dto.InvoiceId);
                if (invoice == null)
                {
                    return BadRequest(new { message = "Invoice not found for this tenant." });
                }

                if (normalizedAmount > NormalizeMoney(invoice.Balance))
                {
                    return BadRequest(new { message = "Charge amount exceeds invoice balance. Surcharges are not allowed." });
                }
            }

            var payorTarget = await ResolvePaymentPayorTargetAsync(invoice, dto.PayorClientId, dto.InvoicePayorAllocationId);
            if (!string.IsNullOrWhiteSpace(payorTarget.ErrorMessage))
            {
                return BadRequest(new { message = payorTarget.ErrorMessage });
            }

            // Create payment transaction record
            var transaction = new PaymentTransaction
            {
                InvoiceId = dto.InvoiceId,
                MatterId = dto.MatterId,
                ClientId = dto.ClientId,
                PayorClientId = payorTarget.PayorClientId,
                InvoicePayorAllocationId = payorTarget.InvoicePayorAllocationId,
                Amount = normalizedAmount,
                Currency = normalizedCurrency,
                TaskCode = dto.TaskCode,
                ExpenseCode = dto.ExpenseCode,
                ActivityCode = dto.ActivityCode,
                PaymentMethod = "Stripe",
                PaymentRail = paymentRail,
                Status = PaymentStatuses.Pending,
                PayerEmail = dto.PayerEmail,
                PayerName = dto.PayerName,
                ProcessedBy = userId
            };

            _context.PaymentTransactions.Add(transaction);
            await _context.SaveChangesAsync();

            await _auditLogger.LogAsync(HttpContext, "payment.create_checkout", "PaymentTransaction", transaction.Id, $"Invoice={dto.InvoiceId}, Amount={dto.Amount} {transaction.Currency}");

            if (!_stripePaymentService.IsConfigured)
            {
                transaction.Status = PaymentStatuses.Failed;
                transaction.FailureReason = "Stripe is not configured.";
                transaction.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                return StatusCode(501, new { message = "Stripe is not configured." });
            }

            try
            {
                var metadata = BuildMetadata(
                    transaction.Id,
                    dto.InvoiceId,
                    dto.MatterId,
                    dto.ClientId,
                    transaction.PayorClientId,
                    transaction.InvoicePayorAllocationId,
                    transaction.PaymentRail);
                var stripePaymentMethodTypes = ResolveStripePaymentMethodTypes(paymentRail);
                var session = await _stripePaymentService.CreateCheckoutSessionAsync(
                    transaction.Id,
                    normalizedAmount,
                    transaction.Currency,
                    dto.Description,
                    dto.PayerEmail,
                    dto.PayerName,
                    metadata,
                    stripePaymentMethodTypes);

                transaction.ProviderSessionId = session.Id;
                transaction.ProviderPaymentIntentId = session.PaymentIntentId;
                transaction.ProviderCustomerId = session.CustomerId;
                transaction.ExternalTransactionId = session.PaymentIntentId ?? session.Id;
                transaction.Status = PaymentStatuses.Processing;
                transaction.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    transactionId = transaction.Id,
                    checkoutUrl = session.Url,
                    amount = normalizedAmount,
                    currency = transaction.Currency,
                    paymentRail = transaction.PaymentRail
                });
            }
            catch (Exception ex)
            {
                transaction.Status = PaymentStatuses.Failed;
                transaction.FailureReason = Truncate(ex.Message, 1000);
                transaction.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                await _auditLogger.LogAsync(HttpContext, "payment.create_checkout.failed", "PaymentTransaction", transaction.Id, transaction.FailureReason ?? "Stripe session create failed.");
                return StatusCode(502, new { message = "Failed to create checkout session." });
            }
        }

        // POST: api/payments/create-intent
        [HttpPost("create-intent")]
        public async Task<ActionResult> CreatePaymentIntent([FromBody] CreatePaymentIntentDto dto)
        {
            if (dto == null)
            {
                return BadRequest(new { message = "Request body is required." });
            }

            var userId = User.FindFirst("sub")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var normalizedAmount = NormalizeMoney(dto.Amount);
            if (normalizedAmount <= 0m)
            {
                return BadRequest(new { message = "Amount must be greater than zero." });
            }

            var normalizedCurrency = NormalizeCurrency(dto.Currency, _allowedCurrencies);
            if (normalizedCurrency == null)
            {
                return BadRequest(new { message = "Currency is not allowed for this environment." });
            }

            var paymentRail = NormalizePaymentRail(dto.PaymentRail);
            if (paymentRail == null)
            {
                return BadRequest(new { message = "PaymentRail is invalid. Allowed values: card, ach, echeck, card_or_ach." });
            }

            if (await IsPeriodLocked(DateTime.UtcNow))
            {
                return BadRequest(new { message = "Billing period is locked. Cannot create payment intent." });
            }

            JurisFlow.Server.Models.Invoice? invoice = null;
            if (!string.IsNullOrEmpty(dto.InvoiceId))
            {
                invoice = await GetInvoiceByIdAsync(dto.InvoiceId);
                if (invoice == null)
                {
                    return BadRequest(new { message = "Invoice not found for this tenant." });
                }

                if (normalizedAmount > NormalizeMoney(invoice.Balance))
                {
                    return BadRequest(new { message = "Charge amount exceeds invoice balance. Surcharges are not allowed." });
                }
            }

            var payorTarget = await ResolvePaymentPayorTargetAsync(invoice, dto.PayorClientId, dto.InvoicePayorAllocationId);
            if (!string.IsNullOrWhiteSpace(payorTarget.ErrorMessage))
            {
                return BadRequest(new { message = payorTarget.ErrorMessage });
            }

            if (!_stripePaymentService.IsConfigured)
            {
                return StatusCode(501, new { message = "Stripe is not configured." });
            }

            var transaction = new PaymentTransaction
            {
                InvoiceId = dto.InvoiceId,
                MatterId = dto.MatterId,
                ClientId = dto.ClientId,
                PayorClientId = payorTarget.PayorClientId,
                InvoicePayorAllocationId = payorTarget.InvoicePayorAllocationId,
                Amount = normalizedAmount,
                Currency = normalizedCurrency,
                TaskCode = dto.TaskCode,
                ExpenseCode = dto.ExpenseCode,
                ActivityCode = dto.ActivityCode,
                PaymentMethod = "Stripe",
                PaymentRail = paymentRail,
                Status = PaymentStatuses.Processing,
                PayerEmail = dto.PayerEmail,
                PayerName = dto.PayerName,
                ProcessedBy = userId
            };

            _context.PaymentTransactions.Add(transaction);
            await _context.SaveChangesAsync();

            try
            {
                var metadata = BuildMetadata(
                    transaction.Id,
                    dto.InvoiceId,
                    dto.MatterId,
                    dto.ClientId,
                    transaction.PayorClientId,
                    transaction.InvoicePayorAllocationId,
                    transaction.PaymentRail);
                var stripePaymentMethodTypes = ResolveStripePaymentMethodTypes(paymentRail);
                var intent = await _stripePaymentService.CreatePaymentIntentAsync(
                    normalizedAmount,
                    transaction.Currency,
                    dto.Description,
                    dto.PayerEmail,
                    dto.CustomerId,
                    metadata,
                    stripePaymentMethodTypes);

                transaction.ProviderPaymentIntentId = intent.Id;
                transaction.ProviderCustomerId = intent.CustomerId;
                transaction.ExternalTransactionId = intent.Id;
                transaction.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                await _auditLogger.LogAsync(HttpContext, "payment.create_intent", "PaymentTransaction", transaction.Id, $"Invoice={dto.InvoiceId}, Amount={normalizedAmount} {transaction.Currency}");

                return Ok(new
                {
                    transactionId = transaction.Id,
                    paymentIntentId = intent.Id,
                    clientSecret = intent.ClientSecret,
                    amount = normalizedAmount,
                    currency = transaction.Currency,
                    paymentRail = transaction.PaymentRail
                });
            }
            catch (Exception ex)
            {
                transaction.Status = PaymentStatuses.Failed;
                transaction.FailureReason = Truncate(ex.Message, 1000);
                transaction.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                await _auditLogger.LogAsync(HttpContext, "payment.create_intent.failed", "PaymentTransaction", transaction.Id, transaction.FailureReason ?? "Stripe intent create failed.");
                return StatusCode(502, new { message = "Failed to create payment intent." });
            }
        }

        // POST: api/payments/confirm
        [HttpPost("confirm")]
        public async Task<ActionResult> ConfirmPayment([FromBody] ConfirmPaymentIntentDto dto)
        {
            if (dto == null)
            {
                return BadRequest(new { message = "Request body is required." });
            }

            if (string.IsNullOrWhiteSpace(dto.PaymentIntentId))
            {
                return BadRequest(new { message = "PaymentIntentId is required." });
            }

            if (string.IsNullOrWhiteSpace(dto.TransactionId))
            {
                return BadRequest(new { message = "TransactionId is required." });
            }

            if (!_stripePaymentService.IsConfigured)
            {
                return StatusCode(501, new { message = "Stripe is not configured." });
            }

            var transaction = await GetTransactionByIdAsync(dto.TransactionId);
            if (transaction == null)
            {
                return NotFound(new { message = "Payment transaction not found." });
            }

            if (IsRefundedStatus(transaction.Status))
            {
                return BadRequest(new { message = "Refunded transactions cannot be confirmed." });
            }

            if (!string.IsNullOrWhiteSpace(transaction.ProviderPaymentIntentId) &&
                !string.Equals(transaction.ProviderPaymentIntentId, dto.PaymentIntentId, StringComparison.Ordinal))
            {
                return BadRequest(new { message = "PaymentIntentId does not match transaction." });
            }

            var intent = await _stripePaymentService.GetPaymentIntentAsync(dto.PaymentIntentId);
            if (!string.Equals(intent.Id, dto.PaymentIntentId, StringComparison.Ordinal))
            {
                return BadRequest(new { message = "Payment intent mismatch." });
            }

            var intentCurrency = intent.Currency?.Trim().ToUpperInvariant();
            var transactionCurrency = transaction.Currency?.Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(intentCurrency) ||
                !string.Equals(intentCurrency, transactionCurrency, StringComparison.Ordinal))
            {
                return BadRequest(new { message = "Payment intent currency does not match transaction currency." });
            }

            var metadataTransactionId = intent.Metadata != null && intent.Metadata.TryGetValue("transactionId", out var txMeta)
                ? txMeta
                : null;
            if (string.IsNullOrWhiteSpace(metadataTransactionId) || !string.Equals(metadataTransactionId, transaction.Id, StringComparison.Ordinal))
            {
                return BadRequest(new { message = "Payment intent metadata does not match transaction." });
            }

            var metadataTenantId = intent.Metadata != null && intent.Metadata.TryGetValue("tenantId", out var tenantMeta)
                ? tenantMeta
                : null;
            if (!string.IsNullOrWhiteSpace(metadataTenantId) &&
                !string.IsNullOrWhiteSpace(_tenantContext.TenantId) &&
                !string.Equals(metadataTenantId, _tenantContext.TenantId, StringComparison.Ordinal))
            {
                return Forbid();
            }

            if (!string.IsNullOrWhiteSpace(transaction.PayorClientId) &&
                intent.Metadata != null &&
                intent.Metadata.TryGetValue("payorClientId", out var metadataPayorClientId) &&
                !string.IsNullOrWhiteSpace(metadataPayorClientId) &&
                !string.Equals(metadataPayorClientId, transaction.PayorClientId, StringComparison.Ordinal))
            {
                return BadRequest(new { message = "Payment intent payor metadata does not match transaction." });
            }

            if (!string.IsNullOrWhiteSpace(transaction.InvoicePayorAllocationId) &&
                intent.Metadata != null &&
                intent.Metadata.TryGetValue("invoicePayorAllocationId", out var metadataPayorAllocationId) &&
                !string.IsNullOrWhiteSpace(metadataPayorAllocationId) &&
                !string.Equals(metadataPayorAllocationId, transaction.InvoicePayorAllocationId, StringComparison.Ordinal))
            {
                return BadRequest(new { message = "Payment intent payor allocation metadata does not match transaction." });
            }

            transaction.ProviderPaymentIntentId = intent.Id;
            transaction.ExternalTransactionId = intent.Id;
            transaction.ProviderCustomerId = intent.CustomerId;
            transaction.Status = MapStripeStatus(intent.Status);

            var charge = await _stripePaymentService.GetChargeAsync(intent.LatestChargeId);
            if (charge != null)
            {
                transaction.ProviderChargeId = charge.Id;
                transaction.ReceiptUrl = charge.ReceiptUrl;
                transaction.CardBrand = charge.PaymentMethodDetails?.Card?.Brand;
                transaction.CardLast4 = charge.PaymentMethodDetails?.Card?.Last4;
                transaction.PaymentRail = transaction.PaymentRail ?? MapStripePaymentRail(charge);
            }

            if (string.Equals(intent.Status, "succeeded", StringComparison.OrdinalIgnoreCase))
            {
                transaction.ProcessedAt ??= DateTime.UtcNow;
                await ApplyInvoicePaymentAsync(transaction, NormalizeMoney(transaction.Amount));
            }

            transaction.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            if (string.Equals(intent.Status, "succeeded", StringComparison.OrdinalIgnoreCase))
            {
                await TryTriggerOutcomeFeePlannerAsync(transaction, "payment_confirm_succeeded");
            }

            return Ok(new
            {
                success = string.Equals(transaction.Status, PaymentStatuses.Succeeded, StringComparison.OrdinalIgnoreCase),
                status = transaction.Status,
                paymentId = transaction.Id,
                receiptUrl = transaction.ReceiptUrl
            });
        }

        // POST: api/payments/setup-intent
        [HttpPost("setup-intent")]
        public async Task<ActionResult> CreateSetupIntent([FromBody] CreateSetupIntentDto dto)
        {
            if (dto == null)
            {
                return BadRequest(new { message = "Request body is required." });
            }

            if (!_stripePaymentService.IsConfigured)
            {
                return StatusCode(501, new { message = "Stripe is not configured." });
            }

            var customer = await _stripePaymentService.GetOrCreateCustomerAsync(dto.CustomerId, dto.Email, dto.Name);
            var setupPaymentRail = NormalizePaymentRail(dto.PaymentRail);
            if (setupPaymentRail == null)
            {
                return BadRequest(new { message = "PaymentRail is invalid. Allowed values: card, ach, echeck, card_or_ach." });
            }

            var intent = await _stripePaymentService.CreateSetupIntentAsync(
                customer.Id,
                new Dictionary<string, string?>
                {
                    ["clientId"] = dto.ClientId
                },
                ResolveStripePaymentMethodTypes(setupPaymentRail));

            return Ok(new
            {
                setupIntentId = intent.Id,
                clientSecret = intent.ClientSecret,
                customerId = customer.Id,
                paymentRail = setupPaymentRail
            });
        }

        // POST: api/payments/autopay/setup
        [HttpPost("autopay/setup")]
        public async Task<ActionResult> SetupAutoPay([FromBody] SetupAutoPayDto dto)
        {
            if (dto == null)
            {
                return BadRequest(new { message = "Request body is required." });
            }

            if (string.IsNullOrWhiteSpace(dto.PaymentPlanId) || string.IsNullOrWhiteSpace(dto.PaymentMethodId))
            {
                return BadRequest(new { message = "PaymentPlanId and PaymentMethodId are required." });
            }

            if (!_stripePaymentService.IsConfigured)
            {
                return StatusCode(501, new { message = "Stripe is not configured." });
            }

            var plan = await GetPaymentPlanByIdAsync(dto.PaymentPlanId);
            if (plan == null) return NotFound(new { message = "Payment plan not found." });

            var customer = await _stripePaymentService.GetOrCreateCustomerAsync(dto.CustomerId, dto.Email, dto.Name);
            await _stripePaymentService.AttachPaymentMethodAsync(dto.PaymentMethodId, customer.Id);

            var reference = new AutoPayReferenceData
            {
                Provider = "stripe",
                CustomerId = customer.Id,
                PaymentMethodId = dto.PaymentMethodId,
                SetupIntentId = dto.SetupIntentId
            };

            plan.AutoPayEnabled = true;
            plan.AutoPayMethod = "Stripe";
            plan.AutoPayReference = PaymentPlanService.BuildAutoPayReference(reference);
            plan.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await _auditLogger.LogAsync(HttpContext, "payment.autopay.setup", "PaymentPlan", plan.Id, $"CustomerId={customer.Id}");

            return Ok(new { message = "AutoPay enabled.", planId = plan.Id });
        }

        // GET: api/payments/{id}
        [HttpGet("{id}")]
        public async Task<ActionResult<PaymentTransaction>> GetPayment(string id)
        {
            var transaction = await GetTransactionByIdAsync(id);
            if (transaction == null)
            {
                return NotFound();
            }

            return Ok(transaction);
        }

        // GET: api/payments/invoice/{invoiceId}
        [HttpGet("invoice/{invoiceId}")]
        public async Task<ActionResult<IEnumerable<PaymentTransaction>>> GetInvoicePayments(string invoiceId)
        {
            var payments = await TenantScope(_context.PaymentTransactions)
                .Where(p => p.InvoiceId == invoiceId)
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync();

            return Ok(payments);
        }

        // GET: api/payments/matter/{matterId}
        [HttpGet("matter/{matterId}")]
        public async Task<ActionResult<IEnumerable<PaymentTransaction>>> GetMatterPayments(string matterId)
        {
            var payments = await TenantScope(_context.PaymentTransactions)
                .Where(p => p.MatterId == matterId)
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync();

            return Ok(payments);
        }

        // GET: api/payments/client/{clientId}
        [HttpGet("client/{clientId}")]
        public async Task<ActionResult<IEnumerable<PaymentTransaction>>> GetClientPayments(string clientId)
        {
            var payments = await TenantScope(_context.PaymentTransactions)
                .Where(p => p.ClientId == clientId)
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync();

            return Ok(payments);
        }

        // POST: api/payments/{id}/complete
        [HttpPost("{id}/complete")]
        public async Task<IActionResult> CompletePayment(string id, [FromBody] CompletePaymentDto dto)
        {
            if (dto == null)
            {
                return BadRequest(new { message = "Request body is required." });
            }

            var transaction = await GetTransactionByIdAsync(id);
            if (transaction == null)
            {
                return NotFound();
            }

            if (await IsPeriodLocked(DateTime.UtcNow))
            {
                return BadRequest(new { message = "Billing period is locked. Cannot modify payment." });
            }

            if (string.Equals(transaction.Status, PaymentStatuses.Succeeded, StringComparison.OrdinalIgnoreCase))
            {
                return Ok(new { message = "Payment already completed", transactionId = transaction.Id });
            }

            if (IsRefundedStatus(transaction.Status))
            {
                return BadRequest(new { message = "Refunded payments cannot be completed." });
            }

            if (string.Equals(transaction.Status, PaymentStatuses.Failed, StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { message = "Failed payments cannot be completed." });
            }

            transaction.Status = PaymentStatuses.Succeeded;
            transaction.ExternalTransactionId = dto.ExternalTransactionId;
            transaction.CardLast4 = dto.CardLast4;
            transaction.CardBrand = dto.CardBrand;
            transaction.ReceiptUrl = dto.ReceiptUrl;
            transaction.ProcessedAt = DateTime.UtcNow;
            transaction.UpdatedAt = DateTime.UtcNow;

            await ApplyInvoicePaymentAsync(transaction, NormalizeMoney(transaction.Amount));
            await _context.SaveChangesAsync();

            await _auditLogger.LogAsync(HttpContext, "payment.complete", "PaymentTransaction", transaction.Id, $"ExternalId={dto.ExternalTransactionId}, Amount={transaction.Amount}");
            await TryTriggerOutcomeFeePlannerAsync(transaction, "payment_complete");

            return Ok(new { message = "Payment completed", transactionId = transaction.Id });
        }

        // POST: api/payments/{id}/fail
        [HttpPost("{id}/fail")]
        public async Task<IActionResult> FailPayment(string id, [FromBody] FailPaymentDto dto)
        {
            if (dto == null)
            {
                return BadRequest(new { message = "Request body is required." });
            }

            var transaction = await GetTransactionByIdAsync(id);
            if (transaction == null)
            {
                return NotFound();
            }

            if (await IsPeriodLocked(DateTime.UtcNow))
            {
                return BadRequest(new { message = "Billing period is locked. Cannot modify payment." });
            }

            if (string.Equals(transaction.Status, PaymentStatuses.Failed, StringComparison.OrdinalIgnoreCase))
            {
                return Ok(new { message = "Payment already failed", reason = transaction.FailureReason });
            }

            if (string.Equals(transaction.Status, PaymentStatuses.Succeeded, StringComparison.OrdinalIgnoreCase) || IsRefundedStatus(transaction.Status))
            {
                return BadRequest(new { message = "Completed or refunded payments cannot be marked failed." });
            }

            transaction.Status = PaymentStatuses.Failed;
            transaction.FailureReason = dto.Reason;
            transaction.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            await _auditLogger.LogAsync(HttpContext, "payment.fail", "PaymentTransaction", transaction.Id, $"Reason={dto.Reason}");

            return Ok(new { message = "Payment failed", reason = dto.Reason });
        }

        // POST: api/payments/{id}/refund
        [HttpPost("{id}/refund")]
        public async Task<IActionResult> RefundPayment(string id, [FromBody] RefundPaymentDto dto)
        {
            if (dto == null)
            {
                return BadRequest(new { message = "Request body is required." });
            }

            var transaction = await GetTransactionByIdAsync(id);
            if (transaction == null)
            {
                return NotFound();
            }

            if (string.Equals(transaction.Status, PaymentStatuses.Refunded, StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { message = "Payment is already fully refunded." });
            }

            if (!string.Equals(transaction.Status, PaymentStatuses.Succeeded, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(transaction.Status, PaymentStatuses.PartiallyRefunded, StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { message = "Can only refund completed payments" });
            }

            if (await IsPeriodLocked(DateTime.UtcNow))
            {
                return BadRequest(new { message = "Billing period is locked. Cannot refund in locked period." });
            }

            var totalPaid = NormalizeMoney(transaction.Amount);
            var alreadyRefunded = NormalizeMoney(transaction.RefundAmount ?? 0m);
            var remainingRefundable = totalPaid - alreadyRefunded;
            if (remainingRefundable <= 0m)
            {
                return BadRequest(new { message = "No refundable amount remaining." });
            }

            var requestedRefund = dto.Amount.HasValue
                ? NormalizeMoney(dto.Amount.Value)
                : remainingRefundable;

            if (requestedRefund <= 0m)
            {
                return BadRequest(new { message = "Refund amount must be greater than zero." });
            }

            if (requestedRefund > remainingRefundable)
            {
                return BadRequest(new { message = "Refund amount exceeds remaining refundable amount." });
            }

            if (_stripePaymentService.IsConfigured && string.Equals(transaction.PaymentMethod, "Stripe", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var refund = await _stripePaymentService.RefundAsync(transaction.ProviderPaymentIntentId ?? transaction.ExternalTransactionId, requestedRefund, dto.Reason);
                    transaction.ProviderRefundId = refund.Id;
                }
                catch (Exception ex)
                {
                    return BadRequest(new { message = "Refund failed.", details = ex.Message });
                }
            }

            var cumulativeRefunded = alreadyRefunded + requestedRefund;
            transaction.Status = cumulativeRefunded >= totalPaid ? PaymentStatuses.Refunded : PaymentStatuses.PartiallyRefunded;
            transaction.RefundAmount = cumulativeRefunded;
            transaction.RefundReason = dto.Reason;
            transaction.RefundedAt = DateTime.UtcNow;
            transaction.UpdatedAt = DateTime.UtcNow;

            await ApplyInvoiceRefundAsync(transaction, cumulativeRefunded);
            await _context.SaveChangesAsync();

            await _auditLogger.LogAsync(HttpContext, "payment.refund", "PaymentTransaction", transaction.Id, $"RefundAmount={requestedRefund}, TotalRefunded={cumulativeRefunded}, Reason={dto.Reason}");
            await TryTriggerOutcomeFeePlannerAsync(transaction, "payment_refund");

            return Ok(new { message = "Refund processed", refundAmount = requestedRefund, totalRefunded = cumulativeRefunded });
        }

        // POST: api/payments/webhook (Stripe webhook endpoint)
        [HttpPost("webhook")]
        [AllowAnonymous]
        public async Task<IActionResult> HandleStripeWebhook()
        {
            if (!_stripePaymentService.IsConfigured || !_stripePaymentService.IsWebhookConfigured)
            {
                return StatusCode(501, new { message = "Stripe webhook is not configured." });
            }

            var json = await new StreamReader(Request.Body).ReadToEndAsync();
            var signatureHeader = Request.Headers["Stripe-Signature"].FirstOrDefault();
            Event? stripeEvent;

            try
            {
                stripeEvent = _stripePaymentService.ConstructWebhookEvent(json, signatureHeader);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = "Invalid webhook signature.", details = ex.Message });
            }

            if (stripeEvent == null)
            {
                return BadRequest(new { message = "Webhook signature missing." });
            }

            if (string.IsNullOrWhiteSpace(stripeEvent.Id))
            {
                return BadRequest(new { message = "Webhook event id is missing." });
            }

            if (!await TryResolveTenantForWebhookAsync(stripeEvent))
            {
                return BadRequest(new { message = "Unable to resolve tenant context for webhook." });
            }

            try
            {
                await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

                var existing = await TenantScope(_context.StripeWebhookEvents)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(e => e.EventId == stripeEvent.Id);
                if (existing != null)
                {
                    return Ok(new { received = true, duplicate = true });
                }

                switch (stripeEvent.Type)
                {
                    case "checkout.session.completed":
                    {
                        var session = stripeEvent.Data.Object as Stripe.Checkout.Session;
                        if (session != null)
                        {
                            await HandleCheckoutCompletedAsync(session);
                        }
                        break;
                    }
                    case "payment_intent.succeeded":
                    {
                        var intent = stripeEvent.Data.Object as PaymentIntent;
                        if (intent != null)
                        {
                            await HandlePaymentIntentSucceededAsync(intent);
                        }
                        break;
                    }
                    case "payment_intent.payment_failed":
                    {
                        var intent = stripeEvent.Data.Object as PaymentIntent;
                        if (intent != null)
                        {
                            await HandlePaymentIntentFailedAsync(intent);
                        }
                        break;
                    }
                    case "charge.refunded":
                    {
                        var charge = stripeEvent.Data.Object as Charge;
                        if (charge != null)
                        {
                            await HandleChargeRefundedAsync(charge);
                        }
                        break;
                    }
                    default:
                        await _auditLogger.LogAsync(HttpContext, "payment.webhook.unhandled", nameof(StripeWebhookEvent), stripeEvent.Id, $"Unhandled stripe event type: {stripeEvent.Type}");
                        break;
                }

                _context.StripeWebhookEvents.Add(new StripeWebhookEvent
                {
                    EventId = stripeEvent.Id,
                    EventType = stripeEvent.Type ?? "unknown",
                    Status = "processed",
                    ProcessedAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow
                });
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch (DbUpdateException)
            {
                // Unique index race on webhook retries in-flight.
                return Ok(new { received = true, duplicate = true });
            }
            catch (Exception ex)
            {
                await _auditLogger.LogAsync(HttpContext, "payment.webhook.failed", nameof(StripeWebhookEvent), stripeEvent.Id, Truncate(ex.Message, 1000) ?? "Webhook processing failed.");
                return StatusCode(500, new { message = "Webhook processing failed." });
            }

            return Ok(new { received = true });
        }

        // GET: api/payments/stats
        [HttpGet("stats")]
        public async Task<ActionResult> GetPaymentStats([FromQuery] string? from = null, [FromQuery] string? to = null)
        {
            var query = TenantScope(_context.PaymentTransactions).AsQueryable();

            if (TryParseUtcDateTime(from, out var fromDate))
            {
                query = query.Where(p => p.CreatedAt >= fromDate);
            }

            if (TryParseUtcDateTime(to, out var toDate))
            {
                query = query.Where(p => p.CreatedAt <= toDate);
            }

            var stats = await query
                .GroupBy(p => p.Status)
                .Select(g => new
                {
                    Status = g.Key,
                    Count = g.Count(),
                    TotalAmount = g.Sum(p => p.Amount)
                })
                .ToListAsync();

            var totalSucceeded = stats.Where(s => s.Status == PaymentStatuses.Succeeded).Sum(s => s.TotalAmount);
            var totalRefunded = await query.SumAsync(p => p.RefundAmount ?? 0m);

            return Ok(new
            {
                stats,
                totalSucceeded,
                totalRefunded,
                netRevenue = totalSucceeded - totalRefunded
            });
        }

        private async Task HandleCheckoutCompletedAsync(Stripe.Checkout.Session session)
        {
            var transaction = await FindTransactionAsync(session.ClientReferenceId, session.Id, session.PaymentIntentId);
            if (transaction == null) return;

            if (string.Equals(transaction.Status, PaymentStatuses.Succeeded, StringComparison.OrdinalIgnoreCase) && transaction.ProcessedAt != null)
            {
                return;
            }

            transaction.ProviderSessionId = session.Id;
            transaction.ProviderPaymentIntentId = session.PaymentIntentId;
            transaction.ProviderCustomerId = session.CustomerId;
            transaction.ExternalTransactionId = session.PaymentIntentId ?? session.Id;

            if (string.Equals(session.PaymentStatus, "paid", StringComparison.OrdinalIgnoreCase))
            {
                transaction.Status = PaymentStatuses.Succeeded;
                transaction.ProcessedAt ??= DateTime.UtcNow;
                await ApplyInvoicePaymentAsync(transaction, NormalizeMoney(transaction.Amount));
            }
            else
            {
                transaction.Status = PaymentStatuses.Processing;
            }

            transaction.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        private async Task HandlePaymentIntentSucceededAsync(PaymentIntent intent)
        {
            var transaction = await FindTransactionAsync(intent.Metadata?["transactionId"], null, intent.Id);
            if (transaction == null) return;

            if (string.Equals(transaction.Status, PaymentStatuses.Succeeded, StringComparison.OrdinalIgnoreCase) && transaction.ProcessedAt != null)
            {
                return;
            }

            transaction.ProviderPaymentIntentId = intent.Id;
            transaction.ExternalTransactionId = intent.Id;
            transaction.ProviderCustomerId = intent.CustomerId;
            transaction.Status = PaymentStatuses.Succeeded;
            transaction.ProcessedAt ??= DateTime.UtcNow;

            var charge = await _stripePaymentService.GetChargeAsync(intent.LatestChargeId);
            if (charge != null)
            {
                transaction.ProviderChargeId = charge.Id;
                transaction.ReceiptUrl = charge.ReceiptUrl;
                transaction.CardBrand = charge.PaymentMethodDetails?.Card?.Brand;
                transaction.CardLast4 = charge.PaymentMethodDetails?.Card?.Last4;
                transaction.PaymentRail = transaction.PaymentRail ?? MapStripePaymentRail(charge);
            }

            transaction.UpdatedAt = DateTime.UtcNow;
            await ApplyInvoicePaymentAsync(transaction, NormalizeMoney(transaction.Amount));
            await _context.SaveChangesAsync();
        }

        private async Task HandlePaymentIntentFailedAsync(PaymentIntent intent)
        {
            var transaction = await FindTransactionAsync(intent.Metadata?["transactionId"], null, intent.Id);
            if (transaction == null) return;

            if (string.Equals(transaction.Status, PaymentStatuses.Succeeded, StringComparison.OrdinalIgnoreCase) || IsRefundedStatus(transaction.Status))
            {
                return;
            }

            transaction.ProviderPaymentIntentId = intent.Id;
            transaction.ExternalTransactionId = intent.Id;
            transaction.Status = PaymentStatuses.Failed;
            transaction.FailureReason = intent.LastPaymentError?.Message ?? "Payment failed.";
            transaction.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        private async Task HandleChargeRefundedAsync(Charge charge)
        {
            var transaction = await TenantScope(_context.PaymentTransactions).FirstOrDefaultAsync(t =>
                t.ProviderChargeId == charge.Id || t.ProviderPaymentIntentId == charge.PaymentIntentId);
            if (transaction == null) return;

            var cumulativeRefunded = NormalizeMoney((decimal)charge.AmountRefunded / 100m);
            transaction.Status = charge.Refunded ? PaymentStatuses.Refunded : PaymentStatuses.PartiallyRefunded;
            transaction.RefundAmount = cumulativeRefunded;
            transaction.ProviderRefundId = charge.Refunds?.Data?.FirstOrDefault()?.Id;
            transaction.RefundedAt = DateTime.UtcNow;
            transaction.UpdatedAt = DateTime.UtcNow;
            transaction.PaymentRail = transaction.PaymentRail ?? MapStripePaymentRail(charge);

            await ApplyInvoiceRefundAsync(transaction, cumulativeRefunded);
            await _context.SaveChangesAsync();
        }

        private async Task<PaymentTransaction?> FindTransactionAsync(string? referenceId, string? sessionId, string? paymentIntentId)
        {
            if (!string.IsNullOrWhiteSpace(referenceId))
            {
                var tx = await TenantScope(_context.PaymentTransactions)
                    .FirstOrDefaultAsync(t => t.Id == referenceId);
                if (tx != null) return tx;
            }

            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                var tx = await TenantScope(_context.PaymentTransactions)
                    .FirstOrDefaultAsync(t => t.ProviderSessionId == sessionId);
                if (tx != null) return tx;
            }

            if (!string.IsNullOrWhiteSpace(paymentIntentId))
            {
                return await TenantScope(_context.PaymentTransactions).FirstOrDefaultAsync(t =>
                    t.ProviderPaymentIntentId == paymentIntentId || t.ExternalTransactionId == paymentIntentId);
            }

            return null;
        }

        private async Task ApplyInvoicePaymentAsync(PaymentTransaction transaction, decimal amount)
        {
            if (string.IsNullOrWhiteSpace(transaction.InvoiceId))
            {
                return;
            }

            var invoice = await GetInvoiceByIdAsync(transaction.InvoiceId);
            if (invoice == null) return;

            var targetApplied = NormalizeMoney(amount);
            if (targetApplied <= 0m)
            {
                return;
            }

            var alreadyApplied = NormalizeMoney(transaction.InvoiceAppliedAmount);
            if (targetApplied <= alreadyApplied)
            {
                return;
            }

            var delta = targetApplied - alreadyApplied;
            var nextAmountPaid = NormalizeMoney(invoice.AmountPaid) + delta;
            var nextBalance = NormalizeMoney(invoice.Balance) - delta;

            if (nextBalance < 0m) nextBalance = 0m;
            if (nextAmountPaid < 0m) nextAmountPaid = 0m;

            invoice.AmountPaid = NormalizeMoney(nextAmountPaid);
            invoice.Balance = NormalizeMoney(nextBalance);

            if (NormalizeMoney(invoice.Balance) == 0m)
            {
                invoice.Status = InvoiceStatus.Paid;
            }
            else if (NormalizeMoney(invoice.AmountPaid) > 0m)
            {
                invoice.Status = InvoiceStatus.PartiallyPaid;
            }

            invoice.UpdatedAt = DateTime.UtcNow;
            transaction.InvoiceAppliedAmount = targetApplied;
            transaction.InvoiceAppliedAt ??= DateTime.UtcNow;
        }

        private async Task ApplyInvoiceRefundAsync(PaymentTransaction transaction, decimal cumulativeRefundAmount)
        {
            if (string.IsNullOrWhiteSpace(transaction.InvoiceId))
            {
                return;
            }

            var invoice = await GetInvoiceByIdAsync(transaction.InvoiceId);
            if (invoice == null)
            {
                return;
            }

            var totalAppliedPayment = NormalizeMoney(transaction.InvoiceAppliedAmount);
            if (totalAppliedPayment <= 0m)
            {
                return;
            }

            var targetRefundApplied = Math.Min(totalAppliedPayment, NormalizeMoney(cumulativeRefundAmount));
            var alreadyRefundApplied = NormalizeMoney(transaction.InvoiceRefundAppliedAmount);
            if (targetRefundApplied <= alreadyRefundApplied)
            {
                return;
            }

            var delta = targetRefundApplied - alreadyRefundApplied;
            var nextAmountPaid = NormalizeMoney(invoice.AmountPaid) - delta;
            var nextBalance = NormalizeMoney(invoice.Balance) + delta;
            var maxBalance = NormalizeMoney(invoice.Total);

            if (nextAmountPaid < 0m) nextAmountPaid = 0m;
            if (nextBalance < 0m) nextBalance = 0m;
            if (nextBalance > maxBalance) nextBalance = maxBalance;

            invoice.AmountPaid = NormalizeMoney(nextAmountPaid);
            invoice.Balance = NormalizeMoney(nextBalance);

            if (NormalizeMoney(invoice.Balance) == 0m)
            {
                invoice.Status = InvoiceStatus.Paid;
            }
            else if (NormalizeMoney(invoice.AmountPaid) > 0m)
            {
                invoice.Status = InvoiceStatus.PartiallyPaid;
            }
            else if (invoice.Status == InvoiceStatus.Paid || invoice.Status == InvoiceStatus.PartiallyPaid)
            {
                invoice.Status = InvoiceStatus.Sent;
            }

            invoice.UpdatedAt = DateTime.UtcNow;
            transaction.InvoiceRefundAppliedAmount = targetRefundApplied;
            transaction.InvoiceRefundAppliedAt = DateTime.UtcNow;
        }

        private Dictionary<string, string?> BuildMetadata(
            string transactionId,
            string? invoiceId,
            string? matterId,
            string? clientId,
            string? payorClientId = null,
            string? invoicePayorAllocationId = null,
            string? paymentRail = null)
        {
            var metadata = new Dictionary<string, string?>
            {
                ["transactionId"] = transactionId
            };
            if (!string.IsNullOrWhiteSpace(invoiceId))
            {
                metadata["invoiceId"] = invoiceId;
            }
            if (!string.IsNullOrWhiteSpace(matterId))
            {
                metadata["matterId"] = matterId;
            }
            if (!string.IsNullOrWhiteSpace(clientId))
            {
                metadata["clientId"] = clientId;
            }
            if (!string.IsNullOrWhiteSpace(payorClientId))
            {
                metadata["payorClientId"] = payorClientId;
            }
            if (!string.IsNullOrWhiteSpace(invoicePayorAllocationId))
            {
                metadata["invoicePayorAllocationId"] = invoicePayorAllocationId;
            }
            if (!string.IsNullOrWhiteSpace(paymentRail))
            {
                metadata["paymentRail"] = paymentRail;
            }
            if (!string.IsNullOrWhiteSpace(_tenantContext.TenantId))
            {
                metadata["tenantId"] = _tenantContext.TenantId;
            }
            return metadata;
        }

        private async Task<(string? PayorClientId, string? InvoicePayorAllocationId, string? ErrorMessage)> ResolvePaymentPayorTargetAsync(
            JurisFlow.Server.Models.Invoice? invoice,
            string? requestedPayorClientId,
            string? requestedInvoicePayorAllocationId)
        {
            var normalizedPayorClientId = NullIfEmpty(requestedPayorClientId);
            var normalizedInvoicePayorAllocationId = NullIfEmpty(requestedInvoicePayorAllocationId);

            if (invoice == null)
            {
                if (!string.IsNullOrWhiteSpace(normalizedInvoicePayorAllocationId))
                {
                    return (null, null, "InvoiceId is required when InvoicePayorAllocationId is provided.");
                }

                if (!string.IsNullOrWhiteSpace(normalizedPayorClientId))
                {
                    var clientExists = await TenantScope(_context.Clients).AsNoTracking()
                        .AnyAsync(c => c.Id == normalizedPayorClientId);
                    if (!clientExists)
                    {
                        return (null, null, "Payor client not found for this tenant.");
                    }
                }

                return (normalizedPayorClientId, null, null);
            }

            var activeAllocations = await TenantScope(_context.InvoicePayorAllocations)
                .Where(a => a.InvoiceId == invoice.Id && a.Status == "active")
                .OrderBy(a => a.Priority)
                .ThenByDescending(a => a.IsPrimary)
                .ToListAsync();

            InvoicePayorAllocation? resolvedAllocation = null;
            if (!string.IsNullOrWhiteSpace(normalizedInvoicePayorAllocationId))
            {
                resolvedAllocation = activeAllocations.FirstOrDefault(a => a.Id == normalizedInvoicePayorAllocationId);
                if (resolvedAllocation == null)
                {
                    return (null, null, "Invoice payor allocation not found or inactive for this invoice.");
                }
            }

            if (resolvedAllocation != null && string.IsNullOrWhiteSpace(normalizedPayorClientId))
            {
                normalizedPayorClientId = resolvedAllocation.PayorClientId;
            }

            if (!string.IsNullOrWhiteSpace(normalizedPayorClientId))
            {
                var clientExists = await TenantScope(_context.Clients).AsNoTracking()
                    .AnyAsync(c => c.Id == normalizedPayorClientId);
                if (!clientExists)
                {
                    return (null, null, "Payor client not found for this tenant.");
                }
            }

            if (resolvedAllocation != null &&
                !string.IsNullOrWhiteSpace(normalizedPayorClientId) &&
                !string.Equals(resolvedAllocation.PayorClientId, normalizedPayorClientId, StringComparison.Ordinal))
            {
                return (null, null, "PayorClientId does not match InvoicePayorAllocation.");
            }

            if (resolvedAllocation == null && !string.IsNullOrWhiteSpace(normalizedPayorClientId))
            {
                var matchingAllocations = activeAllocations
                    .Where(a => string.Equals(a.PayorClientId, normalizedPayorClientId, StringComparison.Ordinal))
                    .ToList();
                if (matchingAllocations.Count == 1)
                {
                    resolvedAllocation = matchingAllocations[0];
                }
            }

            if (resolvedAllocation == null &&
                string.IsNullOrWhiteSpace(normalizedPayorClientId) &&
                activeAllocations.Count > 1)
            {
                return (null, null, "Payor target is required for split-billed invoices. Provide PayorClientId or InvoicePayorAllocationId.");
            }

            if (resolvedAllocation == null &&
                string.IsNullOrWhiteSpace(normalizedPayorClientId) &&
                activeAllocations.Count == 1)
            {
                resolvedAllocation = activeAllocations[0];
                normalizedPayorClientId = resolvedAllocation.PayorClientId;
            }

            return (normalizedPayorClientId, resolvedAllocation?.Id, null);
        }

        private async Task<bool> TryResolveTenantForWebhookAsync(Event stripeEvent)
        {
            if (_tenantContext.IsResolved)
            {
                return true;
            }

            var metadata = await GetStripeEventMetadataAsync(stripeEvent);
            if (metadata == null || !metadata.TryGetValue("tenantId", out var tenantId) || string.IsNullOrWhiteSpace(tenantId))
            {
                return false;
            }

            var normalizedTenantId = tenantId.Trim();
            var tenant = await _context.Tenants
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == normalizedTenantId && t.IsActive);
            if (tenant != null)
            {
                _tenantContext.Set(tenant.Id, tenant.Slug);
                return true;
            }

            return false;
        }

        private async Task<IDictionary<string, string>?> GetStripeEventMetadataAsync(Event stripeEvent)
        {
            switch (stripeEvent.Type)
            {
                case "checkout.session.completed":
                    return (stripeEvent.Data.Object as Stripe.Checkout.Session)?.Metadata;
                case "payment_intent.succeeded":
                case "payment_intent.payment_failed":
                    return (stripeEvent.Data.Object as PaymentIntent)?.Metadata;
                case "charge.refunded":
                {
                    var charge = stripeEvent.Data.Object as Charge;
                    if (charge?.Metadata != null && charge.Metadata.Count > 0)
                    {
                        return charge.Metadata;
                    }

                    if (!string.IsNullOrWhiteSpace(charge?.PaymentIntentId))
                    {
                        var intent = await _stripePaymentService.GetPaymentIntentAsync(charge.PaymentIntentId);
                        return intent.Metadata;
                    }

                    break;
                }
            }

            return null;
        }

        private IQueryable<T> TenantScope<T>(IQueryable<T> query) where T : class
        {
            var tenantId = RequireTenantId();

            return query.Where(e => EF.Property<string>(e, "TenantId") == tenantId);
        }

        private string RequireTenantId()
        {
            var tenantId = _tenantContext.TenantId;
            if (string.IsNullOrWhiteSpace(tenantId))
            {
                throw new InvalidOperationException("Tenant context is required.");
            }

            return tenantId;
        }

        private Task<PaymentTransaction?> GetTransactionByIdAsync(string id)
        {
            return TenantScope(_context.PaymentTransactions).FirstOrDefaultAsync(t => t.Id == id);
        }

        private Task<JurisFlow.Server.Models.Invoice?> GetInvoiceByIdAsync(string id)
        {
            return TenantScope(_context.Invoices).FirstOrDefaultAsync(i => i.Id == id);
        }

        private Task<PaymentPlan?> GetPaymentPlanByIdAsync(string id)
        {
            return TenantScope(_context.PaymentPlans).FirstOrDefaultAsync(p => p.Id == id);
        }

        private async Task TryTriggerOutcomeFeePlannerAsync(PaymentTransaction transaction, string triggerType)
        {
            if (transaction == null || (string.IsNullOrWhiteSpace(transaction.MatterId) && string.IsNullOrWhiteSpace(transaction.Id)))
            {
                return;
            }

            try
            {
                await _outcomeFeePlanner.TryProcessTriggerAsync(new OutcomeFeePlanTriggerRequest
                {
                    MatterId = transaction.MatterId,
                    TriggerType = triggerType,
                    TriggerEntityType = nameof(PaymentTransaction),
                    TriggerEntityId = transaction.Id,
                    SourceStatus = transaction.Status
                }, GetCurrentUserId(), HttpContext.RequestAborted);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Outcome-to-Fee planner trigger failed for payment transaction {PaymentTransactionId}", transaction.Id);
            }

            try
            {
                await _clientTransparencyService.TryProcessTriggerAsync(new ClientTransparencyTriggerRequest
                {
                    MatterId = transaction.MatterId,
                    TriggerType = triggerType,
                    TriggerEntityType = nameof(PaymentTransaction),
                    TriggerEntityId = transaction.Id
                }, GetCurrentUserId(), HttpContext.RequestAborted);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Client transparency trigger failed for payment transaction {PaymentTransactionId}", transaction.Id);
            }
        }

        private string GetCurrentUserId()
        {
            return User.FindFirst("sub")?.Value
                ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? "system";
        }

        private static decimal NormalizeMoney(decimal value)
        {
            return Math.Round(value, 2, MidpointRounding.AwayFromZero);
        }

        private static string? NormalizePaymentRail(string? paymentRail)
        {
            var normalized = string.IsNullOrWhiteSpace(paymentRail) ? "card" : paymentRail.Trim().ToLowerInvariant();
            return normalized switch
            {
                "card" => "card",
                "ach" => "ach",
                "echeck" => "echeck",
                "card_or_ach" => "card_or_ach",
                "card+ach" => "card_or_ach",
                "card+echeck" => "card_or_ach",
                _ => null
            };
        }

        private static IReadOnlyCollection<string>? ResolveStripePaymentMethodTypes(string paymentRail)
        {
            return paymentRail switch
            {
                "ach" or "echeck" => new[] { "us_bank_account" },
                "card_or_ach" => new[] { "card", "us_bank_account" },
                _ => null
            };
        }

        private static bool TryParseUtcDateTime(string? input, out DateTime utcDateTime)
        {
            utcDateTime = default;
            if (string.IsNullOrWhiteSpace(input))
            {
                return false;
            }

            if (!DateTimeOffset.TryParse(
                    input,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var parsed))
            {
                return false;
            }

            utcDateTime = parsed.UtcDateTime;
            return true;
        }

        private static bool TryParseDateOnly(string? input, out DateOnly date)
        {
            date = default;
            if (string.IsNullOrWhiteSpace(input))
            {
                return false;
            }

            if (DateOnly.TryParseExact(input, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var exactDate))
            {
                date = exactDate;
                return true;
            }

            return DateOnly.TryParse(input, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
        }

        private static bool IsRefundedStatus(string? status)
        {
            return string.Equals(status, PaymentStatuses.Refunded, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(status, PaymentStatuses.PartiallyRefunded, StringComparison.OrdinalIgnoreCase);
        }

        private static string? MapStripePaymentRail(Charge? charge)
        {
            var methodType = charge?.PaymentMethodDetails?.Type?.Trim().ToLowerInvariant();
            return methodType switch
            {
                "us_bank_account" => "ach",
                "card" => "card",
                _ => null
            };
        }

        private static string? NormalizeCurrency(string? currency, ISet<string> allowedCurrencies)
        {
            var normalized = string.IsNullOrWhiteSpace(currency) ? "USD" : currency.Trim().ToUpperInvariant();
            if (normalized.Length != 3 || normalized.Any(c => !char.IsLetter(c)))
            {
                return null;
            }

            if (allowedCurrencies.Count > 0 && !allowedCurrencies.Contains(normalized))
            {
                return null;
            }

            return normalized;
        }

        private static HashSet<string> LoadAllowedCurrencies(IConfiguration configuration)
        {
            var configured = configuration.GetSection("Payments:AllowedCurrencies").Get<string[]>();
            var defaultValues = configured is { Length: > 0 } ? configured : new[] { "USD" };
            return new HashSet<string>(
                defaultValues
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .Select(v => v.Trim().ToUpperInvariant()),
                StringComparer.Ordinal);
        }

        private static string? Truncate(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var trimmed = value.Trim();
            return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
        }

        private static string? NullIfEmpty(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var trimmed = value.Trim();
            return trimmed.Length == 0 ? null : trimmed;
        }

        private static string MapStripeStatus(string? status)
        {
            if (string.IsNullOrWhiteSpace(status)) return PaymentStatuses.Processing;
            var normalized = status.ToLowerInvariant();
            return normalized switch
            {
                "succeeded" => PaymentStatuses.Succeeded,
                "requires_action" => PaymentStatuses.RequiresAction,
                "processing" => PaymentStatuses.Processing,
                "requires_payment_method" => PaymentStatuses.Failed,
                "canceled" => PaymentStatuses.Failed,
                _ => PaymentStatuses.Processing
            };
        }
    }

    // DTOs
    public class CreateCheckoutDto
    {
        public string? InvoiceId { get; set; }
        public string? MatterId { get; set; }
        public string? ClientId { get; set; }
        public string? PayorClientId { get; set; }
        public string? InvoicePayorAllocationId { get; set; }
        public decimal Amount { get; set; }
        public string? Currency { get; set; }
        public string? PaymentRail { get; set; } // card | ach | echeck | card_or_ach
        public string? PayerEmail { get; set; }
        public string? PayerName { get; set; }
        public string? Description { get; set; }
        public string? TaskCode { get; set; }
        public string? ExpenseCode { get; set; }
        public string? ActivityCode { get; set; }
    }

    public class CreatePaymentIntentDto
    {
        public string? InvoiceId { get; set; }
        public string? MatterId { get; set; }
        public string? ClientId { get; set; }
        public string? PayorClientId { get; set; }
        public string? InvoicePayorAllocationId { get; set; }
        public string? CustomerId { get; set; }
        public decimal Amount { get; set; }
        public string? Currency { get; set; }
        public string? PaymentRail { get; set; } // card | ach | echeck | card_or_ach
        public string? PayerEmail { get; set; }
        public string? PayerName { get; set; }
        public string? Description { get; set; }
        public string? TaskCode { get; set; }
        public string? ExpenseCode { get; set; }
        public string? ActivityCode { get; set; }
    }

    public class CreateSetupIntentDto
    {
        public string? ClientId { get; set; }
        public string? CustomerId { get; set; }
        public string? PaymentRail { get; set; } // card | ach | echeck | card_or_ach
        public string? Email { get; set; }
        public string? Name { get; set; }
    }

    public class SetupAutoPayDto
    {
        public string PaymentPlanId { get; set; } = string.Empty;
        public string PaymentMethodId { get; set; } = string.Empty;
        public string? CustomerId { get; set; }
        public string? SetupIntentId { get; set; }
        public string? Email { get; set; }
        public string? Name { get; set; }
    }

    public class ConfirmPaymentIntentDto
    {
        public string TransactionId { get; set; } = string.Empty;
        public string PaymentIntentId { get; set; } = string.Empty;
    }

    public class CompletePaymentDto
    {
        public string? ExternalTransactionId { get; set; }
        public string? CardLast4 { get; set; }
        public string? CardBrand { get; set; }
        public string? ReceiptUrl { get; set; }
    }

    public class FailPaymentDto
    {
        public string? Reason { get; set; }
    }

    public class RefundPaymentDto
    {
        public decimal? Amount { get; set; }
        public string? Reason { get; set; }
    }
}
