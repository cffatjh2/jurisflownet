-- Scenario A template: Trust

insert into public.trust_accounts (id, tenant_id, firm_entity_id, office_id, display_name, bank_name, account_number_masked, currency_code, status, created_at, updated_at, row_version)
select
  a."Id"::uuid,
  a."TenantId"::uuid,
  nullif(a."EntityId", '')::uuid,
  nullif(a."OfficeId", '')::uuid,
  trim(a."Name"),
  trim(a."BankName"),
  trim(a."AccountNumberMasked"),
  coalesce(nullif(trim(a."CurrencyCode"), ''), 'USD'),
  case lower(trim(coalesce(a."Status", 'active')))
    when 'closed' then 'closed'
    when 'frozen' then 'frozen'
    else 'active'
  end,
  coalesce(a."CreatedAt", timezone('utc', now())),
  coalesce(a."UpdatedAt", coalesce(a."CreatedAt", timezone('utc', now()))),
  gen_random_uuid()
from legacy_public."TrustBankAccounts" a
on conflict (id) do update
set status = excluded.status,
    updated_at = excluded.updated_at,
    row_version = gen_random_uuid();

insert into public.trust_transactions (
  id, tenant_id, trust_account_id, matter_id, posting_batch_id, transaction_type, amount, currency_code,
  approval_status, posting_state, reference_number, payor_payee_name, check_number, description_text,
  occurred_at, approved_at, approved_by_user_id, voided_at, void_reason, created_at, updated_at, row_version
)
select
  t."Id"::uuid,
  t."TenantId"::uuid,
  t."TrustAccountId"::uuid,
  nullif(t."MatterId", '')::uuid,
  nullif(t."PostingBatchId", '')::uuid,
  case lower(trim(t."Type"))
    when 'deposit' then 'deposit'
    when 'withdrawal' then 'withdrawal'
    when 'transfer' then 'transfer'
    else 'reversal'
  end,
  t."Amount"::numeric(18,2),
  'USD',
  case lower(trim(coalesce(t."ApprovalStatus", 'not_required')))
    when 'approved' then 'approved'
    when 'rejected' then 'rejected'
    when 'pending' then 'pending'
    else 'not_required'
  end,
  case lower(trim(coalesce(t."Status", 'draft')))
    when 'posted' then 'posted'
    when 'voided' then 'voided'
    when 'failed' then 'failed'
    else 'draft'
  end,
  nullif(trim(t."Reference"), ''),
  nullif(trim(t."PayorPayee"), ''),
  nullif(trim(t."CheckNumber"), ''),
  nullif(trim(t."Description"), ''),
  coalesce(t."CreatedAt", timezone('utc', now())),
  t."ApprovedAt",
  nullif(t."ApprovedBy", '')::uuid,
  t."VoidedAt",
  nullif(trim(t."VoidReason"), ''),
  coalesce(t."CreatedAt", timezone('utc', now())),
  coalesce(t."UpdatedAt", coalesce(t."CreatedAt", timezone('utc', now()))),
  coalesce(nullif(t."RowVersion", '')::uuid, gen_random_uuid())
from legacy_public."TrustTransactions" t
on conflict (id) do update
set approval_status = excluded.approval_status,
    posting_state = excluded.posting_state,
    updated_at = excluded.updated_at,
    row_version = gen_random_uuid();

-- Allocation expansion: parse legacy AllocationsJson when present.
insert into public.trust_transaction_allocations (id, tenant_id, trust_transaction_id, trust_matter_ledger_id, allocation_type, amount, note_text, created_at)
select
  gen_random_uuid(),
  t."TenantId"::uuid,
  t."Id"::uuid,
  (alloc.value ->> 'ledgerId')::uuid,
  coalesce(nullif(lower(trim(alloc.value ->> 'type')), ''), 'principal'),
  ((alloc.value ->> 'amount')::numeric(18,2)),
  alloc.value ->> 'note',
  coalesce(t."CreatedAt", timezone('utc', now()))
from legacy_public."TrustTransactions" t
cross join lateral jsonb_array_elements(coalesce(nullif(t."AllocationsJson", ''), '[]')::jsonb) as alloc(value)
where jsonb_typeof(coalesce(nullif(t."AllocationsJson", ''), '[]')::jsonb) = 'array'
on conflict (tenant_id, trust_transaction_id, trust_matter_ledger_id, allocation_type) do update
set amount = excluded.amount,
    note_text = excluded.note_text;
