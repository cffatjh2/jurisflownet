using System.Text.Json;
using JurisFlow.Server.Data;
using JurisFlow.Server.Enums;
using JurisFlow.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace JurisFlow.Server.Services
{
    public class PaymentPlanService
    {
        private readonly JurisFlowDbContext _context;
        private readonly StripePaymentService _stripePaymentService;
        private readonly ILogger<PaymentPlanService> _logger;

        public PaymentPlanService(JurisFlowDbContext context, StripePaymentService stripePaymentService, ILogger<PaymentPlanService> logger)
        {
            _context = context;
            _stripePaymentService = stripePaymentService;
            _logger = logger;
        }

        public DateTime GetNextRunDate(DateTime from, string frequency)
        {
            var normalized = (frequency ?? string.Empty).Trim().ToLowerInvariant();
            return normalized switch
            {
                "weekly" => from.AddDays(7),
                "biweekly" => from.AddDays(14),
                "monthly" => from.AddMonths(1),
                "quarterly" => from.AddMonths(3),
                _ => from.AddMonths(1)
            };
        }

        public async Task<PaymentTransaction?> RunPlanAsync(PaymentPlan plan, string? processedBy, string? payerEmail, string? payerName, DateTime? runAt = null)
        {
            if (!string.Equals(plan.Status, "Active", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (plan.RemainingAmount <= 0)
            {
                plan.Status = "Completed";
                plan.AutoPayEnabled = false;
                plan.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                return null;
            }

            var amount = Math.Min((decimal)plan.InstallmentAmount, (decimal)plan.RemainingAmount);
            if (amount <= 0)
            {
                return null;
            }

            Invoice? invoice = null;
            if (!string.IsNullOrWhiteSpace(plan.InvoiceId))
            {
                invoice = await _context.Invoices.FirstOrDefaultAsync(i => i.Id == plan.InvoiceId);
            }

            var now = runAt ?? DateTime.UtcNow;
            if (plan.AutoPayEnabled)
            {
                if (string.Equals(plan.AutoPayMethod, "Stripe", StringComparison.OrdinalIgnoreCase))
                {
                    return await RunStripeAutoPayAsync(plan, invoice, amount, processedBy, payerEmail, payerName, now);
                }

                var failed = CreateFailedAutoPayTransaction(
                    plan,
                    amount,
                    processedBy,
                    payerEmail,
                    payerName,
                    now,
                    $"Unsupported AutoPay method: {plan.AutoPayMethod ?? "unknown"}");

                _context.PaymentTransactions.Add(failed);
                plan.Status = "Past Due";
                plan.NextRunDate = now.AddDays(1);
                plan.UpdatedAt = now;
                await _context.SaveChangesAsync();
                return failed;
            }

            var manualTransaction = new PaymentTransaction
            {
                Id = Guid.NewGuid().ToString(),
                InvoiceId = plan.InvoiceId,
                ClientId = plan.ClientId,
                Amount = amount,
                Currency = "USD",
                PaymentMethod = "Payment Plan",
                Status = "Succeeded",
                PayerEmail = payerEmail,
                PayerName = payerName,
                ProcessedBy = processedBy,
                ProcessedAt = now,
                ScheduledFor = plan.NextRunDate,
                PaymentPlanId = plan.Id,
                Source = "Plan",
                CreatedAt = now,
                UpdatedAt = now
            };

            _context.PaymentTransactions.Add(manualTransaction);
            ApplyPaymentToInvoice(invoice, amount, now);
            UpdatePlanAfterSuccess(plan, amount, now);

            await _context.SaveChangesAsync();
            return manualTransaction;
        }

        private async Task<PaymentTransaction> RunStripeAutoPayAsync(
            PaymentPlan plan,
            Invoice? invoice,
            decimal amount,
            string? processedBy,
            string? payerEmail,
            string? payerName,
            DateTime now)
        {
            var reference = ParseAutoPayReference(plan.AutoPayReference);
            if (reference == null || string.IsNullOrWhiteSpace(reference.CustomerId) || string.IsNullOrWhiteSpace(reference.PaymentMethodId))
            {
                var failed = CreateFailedAutoPayTransaction(plan, amount, processedBy, payerEmail, payerName, now, "AutoPay payment method is not configured.");
                _context.PaymentTransactions.Add(failed);
                plan.Status = "Past Due";
                plan.NextRunDate = now.AddDays(1);
                plan.UpdatedAt = now;
                await _context.SaveChangesAsync();
                return failed;
            }

            var transaction = new PaymentTransaction
            {
                Id = Guid.NewGuid().ToString(),
                InvoiceId = plan.InvoiceId,
                ClientId = plan.ClientId,
                Amount = amount,
                Currency = "USD",
                PaymentMethod = "Stripe",
                Status = "Processing",
                PayerEmail = payerEmail,
                PayerName = payerName,
                ProcessedBy = processedBy,
                ScheduledFor = plan.NextRunDate,
                PaymentPlanId = plan.Id,
                Source = "AutoPay",
                ProviderCustomerId = reference.CustomerId,
                CreatedAt = now,
                UpdatedAt = now
            };

            _context.PaymentTransactions.Add(transaction);
            await _context.SaveChangesAsync();

            try
            {
                var intent = await _stripePaymentService.ChargeSavedPaymentMethodAsync(
                    amount,
                    "USD",
                    reference.CustomerId!,
                    reference.PaymentMethodId!,
                    $"AutoPay for {plan.Name}",
                    new Dictionary<string, string?>
                    {
                        ["paymentPlanId"] = plan.Id,
                        ["clientId"] = plan.ClientId,
                        ["invoiceId"] = plan.InvoiceId
                    });

                transaction.ProviderPaymentIntentId = intent.Id;
                transaction.ExternalTransactionId = intent.Id;
                transaction.ProviderCustomerId = intent.CustomerId;
                transaction.Status = MapStripeStatus(intent.Status);
                transaction.ProcessedAt = intent.Status == "succeeded" ? now : null;
                transaction.UpdatedAt = now;

                if (string.Equals(intent.Status, "succeeded", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyPaymentToInvoice(invoice, amount, now);
                    UpdatePlanAfterSuccess(plan, amount, now);
                }
                else
                {
                    plan.Status = "Past Due";
                    plan.NextRunDate = now.AddDays(1);
                    plan.UpdatedAt = now;
                    transaction.FailureReason = $"Stripe status: {intent.Status}";
                }

                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Stripe AutoPay failed for plan {PlanId}", plan.Id);
                transaction.Status = "Failed";
                transaction.FailureReason = "AutoPay charge failed.";
                transaction.UpdatedAt = now;
                plan.Status = "Past Due";
                plan.NextRunDate = now.AddDays(1);
                plan.UpdatedAt = now;
                await _context.SaveChangesAsync();
            }

            return transaction;
        }

        private static PaymentTransaction CreateFailedAutoPayTransaction(
            PaymentPlan plan,
            decimal amount,
            string? processedBy,
            string? payerEmail,
            string? payerName,
            DateTime now,
            string reason)
        {
            return new PaymentTransaction
            {
                Id = Guid.NewGuid().ToString(),
                InvoiceId = plan.InvoiceId,
                ClientId = plan.ClientId,
                Amount = amount,
                Currency = "USD",
                PaymentMethod = "Stripe",
                Status = "Failed",
                FailureReason = reason,
                PayerEmail = payerEmail,
                PayerName = payerName,
                ProcessedBy = processedBy,
                ScheduledFor = plan.NextRunDate,
                PaymentPlanId = plan.Id,
                Source = "AutoPay",
                CreatedAt = now,
                UpdatedAt = now
            };
        }

        private static void ApplyPaymentToInvoice(Invoice? invoice, decimal amount, DateTime now)
        {
            if (invoice == null) return;
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

            invoice.UpdatedAt = now;
        }

        private void UpdatePlanAfterSuccess(PaymentPlan plan, decimal amount, DateTime now)
        {
            plan.RemainingAmount -= (double)amount;
            plan.UpdatedAt = now;
            if (plan.RemainingAmount <= 0)
            {
                plan.RemainingAmount = 0;
                plan.Status = "Completed";
                plan.AutoPayEnabled = false;
            }
            else
            {
                plan.NextRunDate = GetNextRunDate(plan.NextRunDate, plan.Frequency);
            }
        }

        private static string MapStripeStatus(string? status)
        {
            if (string.IsNullOrWhiteSpace(status)) return "Processing";
            var normalized = status.ToLowerInvariant();
            return normalized switch
            {
                "succeeded" => "Succeeded",
                "requires_action" => "Requires Action",
                "processing" => "Processing",
                "requires_payment_method" => "Failed",
                "canceled" => "Failed",
                _ => "Processing"
            };
        }

        private static AutoPayReferenceData? ParseAutoPayReference(string? reference)
        {
            if (string.IsNullOrWhiteSpace(reference)) return null;
            try
            {
                return JsonSerializer.Deserialize<AutoPayReferenceData>(reference);
            }
            catch
            {
                return null;
            }
        }

        public static string BuildAutoPayReference(AutoPayReferenceData data)
        {
            return JsonSerializer.Serialize(data);
        }
    }

    public class AutoPayReferenceData
    {
        public string Provider { get; set; } = "stripe";
        public string? CustomerId { get; set; }
        public string? PaymentMethodId { get; set; }
        public string? SetupIntentId { get; set; }
    }
}
