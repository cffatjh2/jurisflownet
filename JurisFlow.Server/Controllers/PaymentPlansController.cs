using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using JurisFlow.Server.Data;
using JurisFlow.Server.DTOs;
using JurisFlow.Server.Models;
using JurisFlow.Server.Services;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace JurisFlow.Server.Controllers
{
    [Route("api/payment-plans")]
    [ApiController]
    [Authorize(Policy = "BillingRead")]
    public class PaymentPlansController : ControllerBase
    {
        private const int DefaultPageSize = 100;
        private const int MaxPageSize = 100;
        private static readonly JsonSerializerOptions StoredResponseJsonOptions = new(JsonSerializerDefaults.Web);
        private readonly JurisFlowDbContext _context;
        private readonly PaymentPlanService _paymentPlanService;
        private readonly PaymentCommandIdempotencyService _paymentCommandIdempotency;
        private readonly AuditLogger _auditLogger;
        private readonly BillingObjectAuthorizationService _billingAuthorization;

        public PaymentPlansController(
            JurisFlowDbContext context,
            PaymentPlanService paymentPlanService,
            PaymentCommandIdempotencyService paymentCommandIdempotency,
            AuditLogger auditLogger,
            BillingObjectAuthorizationService billingAuthorization)
        {
            _context = context;
            _paymentPlanService = paymentPlanService;
            _paymentCommandIdempotency = paymentCommandIdempotency;
            _auditLogger = auditLogger;
            _billingAuthorization = billingAuthorization;
        }

        [HttpGet]
        public async Task<IActionResult> GetPlans(
            [FromQuery] string? clientId,
            [FromQuery] string? invoiceId,
            [FromQuery] string? status,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = DefaultPageSize)
        {
            var query = _billingAuthorization.ApplyReadablePaymentPlanScope(_context.PaymentPlans.AsQueryable(), User);
            var normalizedPage = Math.Max(1, page);
            var normalizedPageSize = NormalizePageSize(pageSize);
            if (!string.IsNullOrWhiteSpace(clientId))
            {
                query = query.Where(p => p.ClientId == clientId);
            }
            if (!string.IsNullOrWhiteSpace(invoiceId))
            {
                query = query.Where(p => p.InvoiceId == invoiceId);
            }
            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(p => p.Status == status);
            }

            var totalCount = await query.CountAsync(HttpContext.RequestAborted);
            var plans = await query
                .OrderByDescending(p => p.CreatedAt)
                .Skip((normalizedPage - 1) * normalizedPageSize)
                .Take(normalizedPageSize)
                .Select(p => new PaymentPlanListItemDto
                {
                    Id = p.Id,
                    ClientId = p.ClientId,
                    InvoiceId = p.InvoiceId,
                    Name = p.Name,
                    TotalAmount = p.TotalAmount,
                    InstallmentAmount = p.InstallmentAmount,
                    Frequency = p.Frequency,
                    StartDate = p.StartDate,
                    NextRunDate = p.NextRunDate,
                    EndDate = p.EndDate,
                    RemainingAmount = p.RemainingAmount,
                    Status = p.Status,
                    AutoPayEnabled = p.AutoPayEnabled,
                    AutoPayMethod = p.AutoPayMethod,
                    CreatedAt = p.CreatedAt,
                    UpdatedAt = p.UpdatedAt
                })
                .ToListAsync(HttpContext.RequestAborted);

            return Ok(new PagedCollectionResponse<PaymentPlanListItemDto>
            {
                Items = plans,
                TotalCount = totalCount,
                Page = normalizedPage,
                PageSize = normalizedPageSize,
                HasMore = normalizedPage * normalizedPageSize < totalCount
            });
        }

        [HttpPost]
        [Authorize(Policy = "BillingWrite")]
        public async Task<IActionResult> CreatePlan([FromBody] PaymentPlanCreateDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.ClientId))
            {
                return BadRequest(new { message = "ClientId is required." });
            }

            var installmentAmount = MoneyMath.Normalize(dto.InstallmentAmount);
            if (installmentAmount <= 0m)
            {
                return BadRequest(new { message = "Installment amount must be positive." });
            }

            var invoice = !string.IsNullOrWhiteSpace(dto.InvoiceId)
                ? await _context.Invoices.FirstOrDefaultAsync(i => i.Id == dto.InvoiceId)
                : null;

            if (!string.IsNullOrWhiteSpace(dto.InvoiceId) && invoice == null)
            {
                return BadRequest(new { message = "Invoice not found." });
            }

            if (invoice != null)
            {
                if (!string.Equals(invoice.ClientId, dto.ClientId, StringComparison.Ordinal))
                {
                    return BadRequest(new { message = "ClientId does not match invoice." });
                }

                if (!await _billingAuthorization.CanManageInvoiceAsync(invoice, User, HttpContext.RequestAborted))
                {
                    return Forbid();
                }
            }
            else if (!await _billingAuthorization.CanManageClientAsync(dto.ClientId, User, HttpContext.RequestAborted))
            {
                return Forbid();
            }

            var total = MoneyMath.Normalize(dto.TotalAmount ?? invoice?.Balance ?? 0m);
            if (total <= 0m)
            {
                return BadRequest(new { message = "Total amount must be greater than 0." });
            }

            if (installmentAmount > total)
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
                ClientId = dto.ClientId,
                InvoiceId = dto.InvoiceId,
                Name = string.IsNullOrWhiteSpace(dto.Name) ? $"Payment Plan {DateTime.UtcNow:yyyyMMdd}" : dto.Name.Trim(),
                TotalAmount = total,
                InstallmentAmount = installmentAmount,
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
            await _auditLogger.LogAsync(HttpContext, "payment.plan.create", "PaymentPlan", plan.Id, $"ClientId={plan.ClientId}, Total={plan.TotalAmount}");

            return Ok(plan);
        }

        [HttpPut("{id}")]
        [Authorize(Policy = "BillingWrite")]
        public async Task<IActionResult> UpdatePlan(string id, [FromBody] PaymentPlanUpdateDto dto)
        {
            var plan = await _context.PaymentPlans.FindAsync(id);
            if (plan == null) return NotFound();
            if (!await _billingAuthorization.CanManagePaymentPlanAsync(plan, User, HttpContext.RequestAborted))
            {
                return Forbid();
            }

            if (!string.IsNullOrWhiteSpace(dto.Name)) plan.Name = dto.Name.Trim();
            if (!string.IsNullOrWhiteSpace(dto.Frequency)) plan.Frequency = dto.Frequency.Trim();
            if (dto.InstallmentAmount.HasValue && MoneyMath.Normalize(dto.InstallmentAmount.Value) > 0m)
            {
                plan.InstallmentAmount = MoneyMath.Normalize(dto.InstallmentAmount.Value);
            }
            if (dto.StartDate.HasValue) plan.StartDate = dto.StartDate.Value;
            if (dto.NextRunDate.HasValue) plan.NextRunDate = dto.NextRunDate.Value;
            if (!string.IsNullOrWhiteSpace(dto.Status)) plan.Status = dto.Status;
            if (dto.AutoPayEnabled.HasValue) plan.AutoPayEnabled = dto.AutoPayEnabled.Value;
            if (dto.AutoPayMethod != null) plan.AutoPayMethod = dto.AutoPayMethod;
            if (dto.AutoPayReference != null) plan.AutoPayReference = dto.AutoPayReference;

            plan.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            await _auditLogger.LogAsync(HttpContext, "payment.plan.update", "PaymentPlan", plan.Id, $"Status={plan.Status}, AutoPay={plan.AutoPayEnabled}");

            return Ok(plan);
        }

        [HttpPost("{id}/run")]
        [Authorize(Policy = "BillingWrite")]
        [EnableRateLimiting("PaymentMutation")]
        public async Task<IActionResult> RunPlan(string id)
        {
            var correlationId = EnsureCorrelationId();
            var plan = await _context.PaymentPlans.FindAsync(id);
            if (plan == null) return NotFound();
            if (!await _billingAuthorization.CanManagePaymentPlanAsync(plan, User, HttpContext.RequestAborted))
            {
                return Forbid();
            }

            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrWhiteSpace(userId))
            {
                return CreateProblemResponse(
                    StatusCodes.Status401Unauthorized,
                    "Authentication required.",
                    "An authenticated user id is required to run a payment plan.",
                    "payment_plan_run_auth_required",
                    correlationId);
            }

            var idempotencyKey = ResolveRequiredIdempotencyKey();
            if (idempotencyKey == null)
            {
                return CreateProblemResponse(
                    StatusCodes.Status400BadRequest,
                    "Idempotency key is required.",
                    "Provide an Idempotency-Key header when running a payment plan.",
                    "payment_plan_run_idempotency_required",
                    correlationId);
            }

            var idempotency = await _paymentCommandIdempotency.BeginAsync(
                "payment-plan-run",
                userId,
                idempotencyKey,
                ComputeRequestFingerprint("payment-plan-run", plan.Id),
                correlationId,
                HttpContext.RequestAborted);

            var replay = ReplayStoredResponse(idempotency, correlationId);
            if (replay != null)
            {
                return replay;
            }

            var transaction = await _paymentPlanService.RunPlanAsync(plan, userId, null, null);
            if (transaction == null)
            {
                return await CompleteProblemResponseAsync(
                    idempotency.Record,
                    StatusCodes.Status400BadRequest,
                    "Payment plan cannot run.",
                    "Payment plan is not active or has no remaining balance.",
                    "payment_plan_not_runnable",
                    correlationId);
            }

            await _auditLogger.LogAsync(HttpContext, "payment.plan.run", "PaymentPlan", plan.Id, $"Amount={transaction.Amount}");
            var response = PaymentPlanRunResponseDto.Create(plan, transaction);
            await _paymentCommandIdempotency.CompleteAsync(
                idempotency.Record,
                StatusCodes.Status200OK,
                nameof(PaymentTransaction),
                transaction.Id,
                null,
                JsonSerializer.Serialize(response, StoredResponseJsonOptions),
                "application/json",
                HttpContext.RequestAborted);
            return Ok(response);
        }

        [HttpPost("run-due")]
        [Authorize(Policy = "BillingFinalize")]
        [EnableRateLimiting("PaymentMutation")]
        public async Task<IActionResult> RunDuePlans([FromQuery] int limit = 25)
        {
            var now = DateTime.UtcNow;
            var duePlans = await _billingAuthorization.ApplyReadablePaymentPlanScope(_context.PaymentPlans, User)
                .Where(p => p.Status == "Active" && p.NextRunDate <= now && p.RemainingAmount > 0)
                .OrderBy(p => p.NextRunDate)
                .Take(limit)
                .ToListAsync();

            var processed = new List<string>();
            foreach (var plan in duePlans)
            {
                var transaction = await _paymentPlanService.RunPlanAsync(plan, null, null, null, now);
                if (transaction != null)
                {
                    processed.Add(plan.Id);
                }
            }

            await _auditLogger.LogAsync(HttpContext, "payment.plan.run_due", "PaymentPlan", null, $"Count={processed.Count}");
            return Ok(new { processedCount = processed.Count, planIds = processed });
        }

        private static int NormalizePageSize(int pageSize)
        {
            if (pageSize <= 0)
            {
                return DefaultPageSize;
            }

            return Math.Clamp(pageSize, 1, MaxPageSize);
        }

        private IActionResult? ReplayStoredResponse(PaymentCommandIdempotencyResult idempotency, string fallbackCorrelationId)
        {
            var correlationId = idempotency.Record.CorrelationId ?? fallbackCorrelationId;

            return idempotency.State switch
            {
                PaymentCommandIdempotencyState.Started => null,
                PaymentCommandIdempotencyState.Conflict => CreateProblemResponse(
                    StatusCodes.Status409Conflict,
                    "Idempotency key conflict.",
                    "The same idempotency key was reused with a different payment plan run request.",
                    "payment_plan_run_idempotency_conflict",
                    correlationId),
                PaymentCommandIdempotencyState.InProgressDuplicate => CreateProblemResponse(
                    StatusCodes.Status409Conflict,
                    "Payment plan run already in progress.",
                    "A payment plan run with the same idempotency key is already being processed.",
                    "payment_plan_run_in_progress",
                    correlationId),
                PaymentCommandIdempotencyState.CompletedDuplicate => BuildStoredResponse(idempotency.Record, correlationId),
                _ => null
            };
        }

        private async Task<ObjectResult> CompleteProblemResponseAsync(
            PaymentCommandDeduplication record,
            int statusCode,
            string title,
            string detail,
            string code,
            string correlationId)
        {
            var response = CreateProblemResponse(statusCode, title, detail, code, correlationId);
            await _paymentCommandIdempotency.FailAsync(
                record,
                statusCode,
                code,
                JsonSerializer.Serialize(response.Value, StoredResponseJsonOptions),
                "application/problem+json",
                HttpContext.RequestAborted);
            return response;
        }

        private ObjectResult CreateProblemResponse(int statusCode, string title, string detail, string code, string correlationId)
        {
            AuditTraceContext.SetCorrelation(HttpContext, correlationId);
            Response.Headers["X-Correlation-Id"] = correlationId;

            var problem = new ProblemDetails
            {
                Status = statusCode,
                Title = title,
                Detail = detail
            };
            problem.Extensions["code"] = code;
            problem.Extensions["correlationId"] = correlationId;

            return StatusCode(statusCode, problem);
        }

        private ContentResult BuildStoredResponse(PaymentCommandDeduplication record, string correlationId)
        {
            AuditTraceContext.SetCorrelation(HttpContext, correlationId);
            Response.Headers["X-Correlation-Id"] = correlationId;

            return new ContentResult
            {
                StatusCode = record.ResultStatusCode ?? StatusCodes.Status200OK,
                ContentType = record.ResponseContentType ?? "application/json",
                Content = record.ResponsePayloadJson ?? "{}"
            };
        }

        private string EnsureCorrelationId()
        {
            var correlationId = Request.Headers["X-Correlation-Id"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(correlationId))
            {
                correlationId = string.IsNullOrWhiteSpace(HttpContext.TraceIdentifier)
                    ? Guid.NewGuid().ToString("N")
                    : HttpContext.TraceIdentifier;
            }

            AuditTraceContext.SetCorrelation(HttpContext, correlationId);
            Response.Headers["X-Correlation-Id"] = correlationId;
            return correlationId;
        }

        private string? ResolveRequiredIdempotencyKey()
        {
            var headerValue = Request.Headers["Idempotency-Key"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(headerValue))
            {
                return null;
            }

            var trimmed = headerValue.Trim();
            return trimmed.Length <= 160 ? trimmed : trimmed[..160];
        }

        private static string ComputeRequestFingerprint(params string?[] parts)
        {
            var normalized = string.Join("|", parts.Select(part => part?.Trim() ?? string.Empty));
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
        }
    }

    public class PaymentPlanCreateDto
    {
        public string ClientId { get; set; } = string.Empty;
        public string? InvoiceId { get; set; }
        public string? Name { get; set; }
        public decimal? TotalAmount { get; set; }
        public decimal InstallmentAmount { get; set; }
        public string? Frequency { get; set; }
        public DateTime? StartDate { get; set; }
        public bool AutoPayEnabled { get; set; } = false;
        public string? AutoPayMethod { get; set; }
        public string? AutoPayReference { get; set; }
    }

    public class PaymentPlanUpdateDto
    {
        public string? Name { get; set; }
        public decimal? InstallmentAmount { get; set; }
        public string? Frequency { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? NextRunDate { get; set; }
        public string? Status { get; set; }
        public bool? AutoPayEnabled { get; set; }
        public string? AutoPayMethod { get; set; }
        public string? AutoPayReference { get; set; }
    }

    public class PaymentPlanRunResponseDto
    {
        public PaymentPlanRunPlanDto Plan { get; set; } = new();
        public PaymentPlanRunTransactionDto Transaction { get; set; } = new();

        public static PaymentPlanRunResponseDto Create(PaymentPlan plan, PaymentTransaction transaction)
        {
            return new PaymentPlanRunResponseDto
            {
                Plan = new PaymentPlanRunPlanDto
                {
                    Id = plan.Id,
                    ClientId = plan.ClientId,
                    InvoiceId = plan.InvoiceId,
                    Name = plan.Name,
                    TotalAmount = plan.TotalAmount,
                    InstallmentAmount = plan.InstallmentAmount,
                    Frequency = plan.Frequency,
                    StartDate = plan.StartDate,
                    NextRunDate = plan.NextRunDate,
                    EndDate = plan.EndDate,
                    RemainingAmount = plan.RemainingAmount,
                    Status = plan.Status,
                    AutoPayEnabled = plan.AutoPayEnabled,
                    AutoPayMethod = plan.AutoPayMethod,
                    AutoPayReference = plan.AutoPayReference,
                    CreatedAt = plan.CreatedAt,
                    UpdatedAt = plan.UpdatedAt
                },
                Transaction = new PaymentPlanRunTransactionDto
                {
                    Id = transaction.Id,
                    InvoiceId = transaction.InvoiceId,
                    MatterId = transaction.MatterId,
                    ClientId = transaction.ClientId,
                    PayorClientId = transaction.PayorClientId,
                    InvoicePayorAllocationId = transaction.InvoicePayorAllocationId,
                    Amount = transaction.Amount,
                    TaskCode = transaction.TaskCode,
                    ExpenseCode = transaction.ExpenseCode,
                    ActivityCode = transaction.ActivityCode,
                    Currency = transaction.Currency,
                    PaymentMethod = transaction.PaymentMethod,
                    PaymentRail = transaction.PaymentRail,
                    ProviderSessionId = transaction.ProviderSessionId,
                    ProviderPaymentIntentId = transaction.ProviderPaymentIntentId,
                    ProviderChargeId = transaction.ProviderChargeId,
                    ProviderRefundId = transaction.ProviderRefundId,
                    ProviderCustomerId = transaction.ProviderCustomerId,
                    ExternalTransactionId = transaction.ExternalTransactionId,
                    Status = transaction.Status,
                    FailureReason = transaction.FailureReason,
                    RefundAmount = transaction.RefundAmount,
                    RefundReason = transaction.RefundReason,
                    RefundedAt = transaction.RefundedAt,
                    ReceiptUrl = transaction.ReceiptUrl,
                    PayerEmail = transaction.PayerEmail,
                    PayerName = transaction.PayerName,
                    CardLast4 = transaction.CardLast4,
                    CardBrand = transaction.CardBrand,
                    ProcessedBy = transaction.ProcessedBy,
                    PaymentPlanId = transaction.PaymentPlanId,
                    ScheduledFor = transaction.ScheduledFor,
                    Source = transaction.Source,
                    ProcessedAt = transaction.ProcessedAt,
                    InvoiceAppliedAmount = transaction.InvoiceAppliedAmount,
                    InvoiceRefundAppliedAmount = transaction.InvoiceRefundAppliedAmount,
                    InvoiceAppliedAt = transaction.InvoiceAppliedAt,
                    InvoiceRefundAppliedAt = transaction.InvoiceRefundAppliedAt,
                    CreatedAt = transaction.CreatedAt,
                    UpdatedAt = transaction.UpdatedAt
                }
            };
        }
    }

    public class PaymentPlanRunPlanDto
    {
        public string Id { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public string? InvoiceId { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal TotalAmount { get; set; }
        public decimal InstallmentAmount { get; set; }
        public string Frequency { get; set; } = string.Empty;
        public DateTime StartDate { get; set; }
        public DateTime NextRunDate { get; set; }
        public DateTime? EndDate { get; set; }
        public decimal RemainingAmount { get; set; }
        public string Status { get; set; } = string.Empty;
        public bool AutoPayEnabled { get; set; }
        public string? AutoPayMethod { get; set; }
        public string? AutoPayReference { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class PaymentPlanRunTransactionDto
    {
        public string Id { get; set; } = string.Empty;
        public string? InvoiceId { get; set; }
        public string? MatterId { get; set; }
        public string? ClientId { get; set; }
        public string? PayorClientId { get; set; }
        public string? InvoicePayorAllocationId { get; set; }
        public decimal Amount { get; set; }
        public string? TaskCode { get; set; }
        public string? ExpenseCode { get; set; }
        public string? ActivityCode { get; set; }
        public string Currency { get; set; } = "USD";
        public string PaymentMethod { get; set; } = string.Empty;
        public string? PaymentRail { get; set; }
        public string? ProviderSessionId { get; set; }
        public string? ProviderPaymentIntentId { get; set; }
        public string? ProviderChargeId { get; set; }
        public string? ProviderRefundId { get; set; }
        public string? ProviderCustomerId { get; set; }
        public string? ExternalTransactionId { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? FailureReason { get; set; }
        public decimal? RefundAmount { get; set; }
        public string? RefundReason { get; set; }
        public DateTime? RefundedAt { get; set; }
        public string? ReceiptUrl { get; set; }
        public string? PayerEmail { get; set; }
        public string? PayerName { get; set; }
        public string? CardLast4 { get; set; }
        public string? CardBrand { get; set; }
        public string? ProcessedBy { get; set; }
        public string? PaymentPlanId { get; set; }
        public DateTime? ScheduledFor { get; set; }
        public string? Source { get; set; }
        public DateTime? ProcessedAt { get; set; }
        public decimal InvoiceAppliedAmount { get; set; }
        public decimal InvoiceRefundAppliedAmount { get; set; }
        public DateTime? InvoiceAppliedAt { get; set; }
        public DateTime? InvoiceRefundAppliedAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
