using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using JurisFlow.Server.Services;

namespace JurisFlow.Server.Controllers
{
    [Route("api/payment-plans")]
    [ApiController]
    [Authorize(Policy = "StaffOnly")]
    public class PaymentPlansController : ControllerBase
    {
        private readonly JurisFlowDbContext _context;
        private readonly PaymentPlanService _paymentPlanService;
        private readonly AuditLogger _auditLogger;

        public PaymentPlansController(JurisFlowDbContext context, PaymentPlanService paymentPlanService, AuditLogger auditLogger)
        {
            _context = context;
            _paymentPlanService = paymentPlanService;
            _auditLogger = auditLogger;
        }

        [HttpGet]
        public async Task<IActionResult> GetPlans([FromQuery] string? clientId, [FromQuery] string? invoiceId, [FromQuery] string? status)
        {
            var query = _context.PaymentPlans.AsQueryable();
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

            var plans = await query
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync();
            return Ok(plans);
        }

        [HttpPost]
        public async Task<IActionResult> CreatePlan([FromBody] PaymentPlanCreateDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.ClientId))
            {
                return BadRequest(new { message = "ClientId is required." });
            }

            if (dto.InstallmentAmount <= 0)
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
                ClientId = dto.ClientId,
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
            await _auditLogger.LogAsync(HttpContext, "payment.plan.create", "PaymentPlan", plan.Id, $"ClientId={plan.ClientId}, Total={plan.TotalAmount}");

            return Ok(plan);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdatePlan(string id, [FromBody] PaymentPlanUpdateDto dto)
        {
            var plan = await _context.PaymentPlans.FindAsync(id);
            if (plan == null) return NotFound();

            if (!string.IsNullOrWhiteSpace(dto.Name)) plan.Name = dto.Name.Trim();
            if (!string.IsNullOrWhiteSpace(dto.Frequency)) plan.Frequency = dto.Frequency.Trim();
            if (dto.InstallmentAmount.HasValue && dto.InstallmentAmount.Value > 0)
            {
                plan.InstallmentAmount = dto.InstallmentAmount.Value;
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
        public async Task<IActionResult> RunPlan(string id)
        {
            var plan = await _context.PaymentPlans.FindAsync(id);
            if (plan == null) return NotFound();

            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var transaction = await _paymentPlanService.RunPlanAsync(plan, userId, null, null);
            if (transaction == null)
            {
                return BadRequest(new { message = "Payment plan is not active or has no remaining balance." });
            }

            await _auditLogger.LogAsync(HttpContext, "payment.plan.run", "PaymentPlan", plan.Id, $"Amount={transaction.Amount}");
            return Ok(new { plan, transaction });
        }

        [HttpPost("run-due")]
        public async Task<IActionResult> RunDuePlans([FromQuery] int limit = 25)
        {
            var now = DateTime.UtcNow;
            var duePlans = await _context.PaymentPlans
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
    }

    public class PaymentPlanCreateDto
    {
        public string ClientId { get; set; } = string.Empty;
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

    public class PaymentPlanUpdateDto
    {
        public string? Name { get; set; }
        public double? InstallmentAmount { get; set; }
        public string? Frequency { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? NextRunDate { get; set; }
        public string? Status { get; set; }
        public bool? AutoPayEnabled { get; set; }
        public string? AutoPayMethod { get; set; }
        public string? AutoPayReference { get; set; }
    }
}
