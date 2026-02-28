using Stripe;
using Stripe.Checkout;

namespace JurisFlow.Server.Services
{
    public class StripePaymentService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<StripePaymentService> _logger;
        private readonly string? _secretKey;
        private readonly string? _webhookSecret;
        private readonly string? _successUrl;
        private readonly string? _cancelUrl;
        private readonly string? _statementDescriptor;

        public StripePaymentService(IConfiguration configuration, ILogger<StripePaymentService> logger)
        {
            _configuration = configuration;
            _logger = logger;
            _secretKey = configuration["Stripe:SecretKey"];
            _webhookSecret = configuration["Stripe:WebhookSecret"];
            _successUrl = configuration["Stripe:SuccessUrl"];
            _cancelUrl = configuration["Stripe:CancelUrl"];
            _statementDescriptor = configuration["Stripe:StatementDescriptor"];

            if (!string.IsNullOrWhiteSpace(_secretKey))
            {
                StripeConfiguration.ApiKey = _secretKey;
                StripeConfiguration.AppInfo = new AppInfo
                {
                    Name = "JurisFlow",
                    Version = "1.0.0"
                };
            }
        }

        public bool IsConfigured => !string.IsNullOrWhiteSpace(_secretKey);
        public bool IsWebhookConfigured => !string.IsNullOrWhiteSpace(_webhookSecret);

        public string? WebhookSecret => _webhookSecret;

        public async Task<Session> CreateCheckoutSessionAsync(
            string transactionId,
            decimal amount,
            string currency,
            string? description,
            string? payerEmail,
            string? payerName,
            IDictionary<string, string?> metadata,
            IReadOnlyCollection<string>? paymentMethodTypes = null)
        {
            EnsureConfigured();

            if (string.IsNullOrWhiteSpace(_successUrl) || string.IsNullOrWhiteSpace(_cancelUrl))
            {
                throw new InvalidOperationException("Stripe success/cancel URLs are not configured.");
            }

            var amountCents = ToMinorUnits(amount);
            var sessionOptions = new SessionCreateOptions
            {
                Mode = "payment",
                SuccessUrl = AppendTransactionId(_successUrl, transactionId),
                CancelUrl = AppendTransactionId(_cancelUrl, transactionId),
                CustomerEmail = payerEmail,
                ClientReferenceId = transactionId,
                LineItems = new List<SessionLineItemOptions>
                {
                    new()
                    {
                        Quantity = 1,
                        PriceData = new SessionLineItemPriceDataOptions
                        {
                            Currency = currency.ToLowerInvariant(),
                            UnitAmount = amountCents,
                            ProductData = new SessionLineItemPriceDataProductDataOptions
                            {
                                Name = string.IsNullOrWhiteSpace(description) ? "Legal Services Payment" : description,
                                Metadata = CleanMetadata(metadata)
                            }
                        }
                    }
                },
                PaymentIntentData = new SessionPaymentIntentDataOptions
                {
                    ReceiptEmail = payerEmail,
                    StatementDescriptor = _statementDescriptor,
                    Metadata = CleanMetadata(metadata)
                },
                Metadata = CleanMetadata(metadata)
            };

            var normalizedCheckoutTypes = NormalizePaymentMethodTypes(paymentMethodTypes);
            if (normalizedCheckoutTypes.Count > 0)
            {
                sessionOptions.PaymentMethodTypes = normalizedCheckoutTypes;
            }

            var service = new SessionService();
            return await service.CreateAsync(sessionOptions);
        }

        public async Task<PaymentIntent> CreatePaymentIntentAsync(
            decimal amount,
            string currency,
            string? description,
            string? payerEmail,
            string? customerId,
            IDictionary<string, string?> metadata,
            IReadOnlyCollection<string>? paymentMethodTypes = null)
        {
            EnsureConfigured();

            var normalizedPaymentTypes = NormalizePaymentMethodTypes(paymentMethodTypes);
            var options = new PaymentIntentCreateOptions
            {
                Amount = ToMinorUnits(amount),
                Currency = currency.ToLowerInvariant(),
                Description = description,
                ReceiptEmail = payerEmail,
                Customer = customerId,
                Metadata = CleanMetadata(metadata)
            };

            if (normalizedPaymentTypes.Count > 0)
            {
                options.PaymentMethodTypes = normalizedPaymentTypes;
            }
            else
            {
                options.AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions
                {
                    Enabled = true
                };
            }

            if (!string.IsNullOrWhiteSpace(_statementDescriptor))
            {
                options.StatementDescriptor = _statementDescriptor;
            }

            var service = new PaymentIntentService();
            return await service.CreateAsync(options);
        }

        public async Task<PaymentIntent> GetPaymentIntentAsync(string paymentIntentId)
        {
            EnsureConfigured();
            var service = new PaymentIntentService();
            return await service.GetAsync(paymentIntentId);
        }

        public async Task<Charge?> GetChargeAsync(string? chargeId)
        {
            EnsureConfigured();
            if (string.IsNullOrWhiteSpace(chargeId))
            {
                return null;
            }

            var service = new ChargeService();
            return await service.GetAsync(chargeId);
        }

        public async Task<PaymentIntent> ChargeSavedPaymentMethodAsync(
            decimal amount,
            string currency,
            string customerId,
            string paymentMethodId,
            string? description,
            IDictionary<string, string?> metadata)
        {
            EnsureConfigured();

            var options = new PaymentIntentCreateOptions
            {
                Amount = ToMinorUnits(amount),
                Currency = currency.ToLowerInvariant(),
                Customer = customerId,
                PaymentMethod = paymentMethodId,
                Confirm = true,
                OffSession = true,
                Description = description,
                AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions
                {
                    Enabled = true
                },
                Metadata = CleanMetadata(metadata)
            };

            if (!string.IsNullOrWhiteSpace(_statementDescriptor))
            {
                options.StatementDescriptor = _statementDescriptor;
            }

            var service = new PaymentIntentService();
            return await service.CreateAsync(options);
        }

        public async Task<SetupIntent> CreateSetupIntentAsync(
            string? customerId,
            IDictionary<string, string?> metadata,
            IReadOnlyCollection<string>? paymentMethodTypes = null)
        {
            EnsureConfigured();
            var normalizedSetupTypes = NormalizePaymentMethodTypes(paymentMethodTypes);

            var options = new SetupIntentCreateOptions
            {
                Customer = customerId,
                PaymentMethodTypes = normalizedSetupTypes.Count > 0 ? normalizedSetupTypes : new List<string> { "card" },
                Usage = "off_session",
                Metadata = CleanMetadata(metadata)
            };

            var service = new SetupIntentService();
            return await service.CreateAsync(options);
        }

        public async Task<Customer> GetOrCreateCustomerAsync(string? customerId, string? email, string? name)
        {
            EnsureConfigured();

            var customerService = new CustomerService();

            if (!string.IsNullOrWhiteSpace(customerId))
            {
                try
                {
                    return await customerService.GetAsync(customerId);
                }
                catch (StripeException ex)
                {
                    _logger.LogWarning(ex, "Failed to retrieve Stripe customer {CustomerId}. Creating a new one.", customerId);
                }
            }

            var options = new CustomerCreateOptions
            {
                Email = email,
                Name = name
            };

            return await customerService.CreateAsync(options);
        }

        public async Task AttachPaymentMethodAsync(string paymentMethodId, string customerId)
        {
            EnsureConfigured();

            var service = new PaymentMethodService();
            await service.AttachAsync(paymentMethodId, new PaymentMethodAttachOptions { Customer = customerId });

            var customerService = new CustomerService();
            await customerService.UpdateAsync(customerId, new CustomerUpdateOptions
            {
                InvoiceSettings = new CustomerInvoiceSettingsOptions
                {
                    DefaultPaymentMethod = paymentMethodId
                }
            });
        }

        public async Task<Refund> RefundAsync(string? paymentIntentId, decimal? amount, string? reason)
        {
            EnsureConfigured();

            var options = new RefundCreateOptions
            {
                PaymentIntent = paymentIntentId,
                Amount = amount.HasValue ? ToMinorUnits(amount.Value) : null,
                Reason = MapRefundReason(reason)
            };

            var service = new RefundService();
            return await service.CreateAsync(options);
        }

        public Event? ConstructWebhookEvent(string payload, string? signatureHeader)
        {
            EnsureConfigured();

            if (string.IsNullOrWhiteSpace(_webhookSecret) || string.IsNullOrWhiteSpace(signatureHeader))
            {
                return null;
            }

            return EventUtility.ConstructEvent(payload, signatureHeader, _webhookSecret, throwOnApiVersionMismatch: false);
        }

        private void EnsureConfigured()
        {
            if (!IsConfigured)
            {
                throw new InvalidOperationException("Stripe is not configured.");
            }
        }

        private static string AppendTransactionId(string baseUrl, string transactionId)
        {
            var separator = baseUrl.Contains('?') ? "&" : "?";
            return $"{baseUrl}{separator}transactionId={Uri.EscapeDataString(transactionId)}";
        }

        private static long ToMinorUnits(decimal amount)
        {
            return (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);
        }

        private static Dictionary<string, string> CleanMetadata(IDictionary<string, string?> metadata)
        {
            var result = new Dictionary<string, string>();
            foreach (var entry in metadata)
            {
                if (string.IsNullOrWhiteSpace(entry.Key)) continue;
                if (string.IsNullOrWhiteSpace(entry.Value)) continue;
                result[entry.Key] = entry.Value.Trim();
            }
            return result;
        }

        private static List<string> NormalizePaymentMethodTypes(IReadOnlyCollection<string>? paymentMethodTypes)
        {
            if (paymentMethodTypes == null || paymentMethodTypes.Count == 0)
            {
                return new List<string>();
            }

            var result = new HashSet<string>(StringComparer.Ordinal);
            foreach (var raw in paymentMethodTypes)
            {
                if (string.IsNullOrWhiteSpace(raw))
                {
                    continue;
                }

                var normalized = raw.Trim().ToLowerInvariant();
                if (normalized is "card" or "us_bank_account")
                {
                    result.Add(normalized);
                }
            }

            return result.OrderBy(v => v, StringComparer.Ordinal).ToList();
        }

        private static string? MapRefundReason(string? reason)
        {
            if (string.IsNullOrWhiteSpace(reason)) return null;
            var normalized = reason.Trim().ToLowerInvariant();
            return normalized switch
            {
                "duplicate" => "duplicate",
                "fraudulent" => "fraudulent",
                "requested_by_customer" => "requested_by_customer",
                _ => null
            };
        }
    }
}
