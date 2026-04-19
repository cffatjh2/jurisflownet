create table if not exists public.trust_accounts (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete restrict,
  firm_entity_id uuid null references public.firm_entities(id) on delete set null,
  office_id uuid null references public.offices(id) on delete set null,
  display_name text not null,
  bank_name text not null,
  account_number_masked text not null,
  currency_code text not null default 'USD',
  status text not null default 'active' check (status in ('active', 'frozen', 'closed')),
  metadata_json jsonb null,
  created_at timestamptz not null default timezone('utc', now()),
  created_by_user_id uuid null references public.users(id) on delete set null,
  updated_at timestamptz not null default timezone('utc', now()),
  updated_by_user_id uuid null references public.users(id) on delete set null,
  row_version uuid not null default gen_random_uuid()
);

create unique index if not exists ux_trust_accounts_tenant_account
  on public.trust_accounts (tenant_id, account_number_masked, bank_name);

create table if not exists public.trust_matter_ledgers (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete restrict,
  trust_account_id uuid not null references public.trust_accounts(id) on delete restrict,
  matter_id uuid not null references public.matters(id) on delete restrict,
  client_id uuid not null references public.clients(id) on delete restrict,
  status text not null default 'active' check (status in ('active', 'inactive', 'closed')),
  metadata_json jsonb null,
  created_at timestamptz not null default timezone('utc', now()),
  created_by_user_id uuid null references public.users(id) on delete set null,
  updated_at timestamptz not null default timezone('utc', now()),
  updated_by_user_id uuid null references public.users(id) on delete set null,
  row_version uuid not null default gen_random_uuid()
);

create unique index if not exists ux_trust_matter_ledgers_unique
  on public.trust_matter_ledgers (tenant_id, trust_account_id, matter_id, client_id);

create table if not exists public.trust_posting_batches (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete restrict,
  command_id uuid not null,
  status text not null default 'pending' check (status in ('pending', 'posted', 'voided', 'failed')),
  posted_at timestamptz null,
  failure_reason text null,
  metadata_json jsonb null,
  created_at timestamptz not null default timezone('utc', now()),
  created_by_user_id uuid null references public.users(id) on delete set null,
  updated_at timestamptz not null default timezone('utc', now()),
  updated_by_user_id uuid null references public.users(id) on delete set null,
  row_version uuid not null default gen_random_uuid()
);

create unique index if not exists ux_trust_posting_batches_tenant_command
  on public.trust_posting_batches (tenant_id, command_id);

create table if not exists public.trust_transactions (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete restrict,
  trust_account_id uuid not null references public.trust_accounts(id) on delete restrict,
  matter_id uuid null references public.matters(id) on delete set null,
  posting_batch_id uuid null references public.trust_posting_batches(id) on delete set null,
  transaction_type text not null check (transaction_type in ('deposit', 'withdrawal', 'transfer', 'reversal')),
  amount numeric(18,2) not null check (amount > 0),
  currency_code text not null default 'USD',
  approval_status text not null default 'not_required' check (approval_status in ('not_required', 'pending', 'approved', 'rejected')),
  posting_state text not null default 'draft' check (posting_state in ('draft', 'posted', 'voided', 'failed')),
  reference_number text null,
  payor_payee_name text null,
  check_number text null,
  description_text text null,
  occurred_at timestamptz not null default timezone('utc', now()),
  approved_at timestamptz null,
  approved_by_user_id uuid null references public.users(id) on delete set null,
  voided_at timestamptz null,
  voided_by_user_id uuid null references public.users(id) on delete set null,
  void_reason text null,
  metadata_json jsonb null,
  created_at timestamptz not null default timezone('utc', now()),
  created_by_user_id uuid null references public.users(id) on delete set null,
  updated_at timestamptz not null default timezone('utc', now()),
  updated_by_user_id uuid null references public.users(id) on delete set null,
  row_version uuid not null default gen_random_uuid()
);

create index if not exists ix_trust_transactions_tenant_account_occurred
  on public.trust_transactions (tenant_id, trust_account_id, occurred_at desc);

create index if not exists ix_trust_transactions_tenant_matter_occurred
  on public.trust_transactions (tenant_id, matter_id, occurred_at desc);

create table if not exists public.trust_transaction_allocations (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete restrict,
  trust_transaction_id uuid not null references public.trust_transactions(id) on delete cascade,
  trust_matter_ledger_id uuid not null references public.trust_matter_ledgers(id) on delete restrict,
  allocation_type text not null check (allocation_type in ('principal', 'fee', 'expense', 'transfer')),
  amount numeric(18,2) not null check (amount > 0),
  note_text text null,
  created_at timestamptz not null default timezone('utc', now()),
  created_by_user_id uuid null references public.users(id) on delete set null
);

create unique index if not exists ux_trust_transaction_allocations_unique
  on public.trust_transaction_allocations (tenant_id, trust_transaction_id, trust_matter_ledger_id, allocation_type);

create table if not exists public.trust_journal_entries (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete restrict,
  trust_transaction_id uuid not null references public.trust_transactions(id) on delete cascade,
  trust_matter_ledger_id uuid null references public.trust_matter_ledgers(id) on delete set null,
  entry_side text not null check (entry_side in ('debit', 'credit')),
  entry_order integer not null check (entry_order > 0),
  amount numeric(18,2) not null check (amount > 0),
  occurred_at timestamptz not null default timezone('utc', now()),
  account_code text null,
  narrative text null,
  metadata_json jsonb null,
  created_at timestamptz not null default timezone('utc', now()),
  created_by_user_id uuid null references public.users(id) on delete set null
);

create unique index if not exists ux_trust_journal_entries_unique
  on public.trust_journal_entries (tenant_id, trust_transaction_id, entry_side, entry_order);

drop trigger if exists trg_trust_accounts_set_updated_at on public.trust_accounts;
create trigger trg_trust_accounts_set_updated_at before update on public.trust_accounts
for each row execute function public.set_updated_at_and_row_version();

drop trigger if exists trg_trust_matter_ledgers_set_updated_at on public.trust_matter_ledgers;
create trigger trg_trust_matter_ledgers_set_updated_at before update on public.trust_matter_ledgers
for each row execute function public.set_updated_at_and_row_version();

drop trigger if exists trg_trust_posting_batches_set_updated_at on public.trust_posting_batches;
create trigger trg_trust_posting_batches_set_updated_at before update on public.trust_posting_batches
for each row execute function public.set_updated_at_and_row_version();

drop trigger if exists trg_trust_transactions_set_updated_at on public.trust_transactions;
create trigger trg_trust_transactions_set_updated_at before update on public.trust_transactions
for each row execute function public.set_updated_at_and_row_version();
