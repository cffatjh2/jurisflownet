# Payments and Trust Operations

## Stripe Payments (PCI-Aware Flow)
JurisFlow uses Stripe Checkout or Payment Intents so card data never touches the application. This keeps the system in a low-scope PCI posture (typically SAQ A when using hosted checkout).

### Required Configuration
- `Stripe:SecretKey`
- `Stripe:WebhookSecret`
- `Stripe:SuccessUrl`
- `Stripe:CancelUrl`
- `Stripe:StatementDescriptor` (optional)

### Server Endpoints
- `POST /api/payments/create-checkout` → Stripe Checkout session URL.
- `POST /api/payments/create-intent` → PaymentIntent client secret.
- `POST /api/payments/confirm` → Confirm and sync PaymentIntent status.
- `POST /api/payments/webhook` → Stripe webhook handler.
- `POST /api/payments/autoPay/setup` → Attach a saved payment method for AutoPay.
- `POST /api/payments/{id}/refund` → Refund a payment.

### AutoPay Flow (Production)
1. Create a SetupIntent via `POST /api/payments/setup-intent`.
2. Collect card details in the client using Stripe Elements.
3. Submit `paymentMethodId` to `POST /api/payments/autopay/setup`.
4. Scheduled job runs due payment plans and charges via Stripe off-session.

### Webhook Events
Configure Stripe to send events to `/api/payments/webhook`:
- `checkout.session.completed`
- `payment_intent.succeeded`
- `payment_intent.payment_failed`
- `charge.refunded`

## Trust / IOLTA Compliance
IOLTA compliance depends on maintaining accurate ledgers and performing three-way reconciliation.

### Key Rules Enforced
- Deposits must be fully allocated to ledgers.
- Withdrawals cannot exceed ledger or account balances.
- Only approved transactions update balances.

### Compliance Endpoint
- `GET /api/trust/compliance?trustAccountId=...&bankStatementBalance=...`
  - Returns ledger totals, discrepancies, pending transactions, and negative ledger warnings.

### Three-Way Reconciliation Checklist
1. Confirm trust account ledger total equals trust account balance.
2. Confirm bank statement balance matches trust account balance.
3. Ensure no negative ledgers and no pending transactions for the period.
4. Record reconciliation using `POST /api/trust/reconcile`.

### Field Test (Recommended)
1. Post a deposit with multiple allocations.
2. Post a withdrawal against a single ledger.
3. Run `/api/trust/compliance` with the bank statement balance.
4. Verify the reconciliation record is marked `IsReconciled = true`.
