-- Scenario A template: Billing

insert into public.invoices (
  id, tenant_id, client_id, matter_id, invoice_number, status, issue_date, due_date,
  currency_code, subtotal, tax_amount, discount_amount, total_amount, amount_paid, balance_amount,
  notes_text, terms_text, created_at, updated_at, row_version
)
select
  i."Id"::uuid,
  i."TenantId"::uuid,
  i."ClientId"::uuid,
  nullif(i."MatterId", '')::uuid,
  trim(i."Number"),
  case lower(trim(i."Status"))
    when 'issued' then 'issued'
    when 'paid' then 'paid'
    when 'partiallypaid' then 'partially_paid'
    when 'void' then 'void'
    else 'draft'
  end,
  coalesce(i."IssueDate"::date, current_date),
  i."DueDate"::date,
  'USD',
  coalesce(i."Subtotal", 0)::numeric(18,2),
  coalesce(i."Tax", 0)::numeric(18,2),
  coalesce(i."Discount", 0)::numeric(18,2),
  coalesce(i."Total", 0)::numeric(18,2),
  coalesce(i."AmountPaid", 0)::numeric(18,2),
  coalesce(i."Balance", 0)::numeric(18,2),
  nullif(trim(i."Notes"), ''),
  nullif(trim(i."Terms"), ''),
  coalesce(i."CreatedAt", timezone('utc', now())),
  coalesce(i."UpdatedAt", coalesce(i."CreatedAt", timezone('utc', now()))),
  gen_random_uuid()
from legacy_public."Invoices" i
on conflict (id) do update
set invoice_number = excluded.invoice_number,
    status = excluded.status,
    total_amount = excluded.total_amount,
    amount_paid = excluded.amount_paid,
    balance_amount = excluded.balance_amount,
    updated_at = excluded.updated_at,
    row_version = gen_random_uuid();

insert into public.billing_payments (
  id, tenant_id, client_id, matter_id, payment_status, payment_method, amount,
  currency_code, received_at, provider_reference, created_at, updated_at, row_version
)
select
  p."Id"::uuid,
  p."TenantId"::uuid,
  p."ClientId"::uuid,
  nullif(p."MatterId", '')::uuid,
  case lower(trim(coalesce(p."Status", 'pending')))
    when 'settled' then 'settled'
    when 'failed' then 'failed'
    when 'refunded' then 'refunded'
    when 'voided' then 'voided'
    else 'pending'
  end,
  coalesce(nullif(trim(p."Method"), ''), 'unknown'),
  p."Amount"::numeric(18,2),
  'USD',
  coalesce(p."CreatedAt", timezone('utc', now())),
  nullif(trim(p."ProviderReference"), ''),
  coalesce(p."CreatedAt", timezone('utc', now())),
  coalesce(p."UpdatedAt", coalesce(p."CreatedAt", timezone('utc', now()))),
  gen_random_uuid()
from legacy_public."PaymentTransactions" p
on conflict (id) do update
set payment_status = excluded.payment_status,
    amount = excluded.amount,
    updated_at = excluded.updated_at,
    row_version = gen_random_uuid();
