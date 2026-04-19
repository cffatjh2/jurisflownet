create table if not exists public.billing_rate_cards (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete restrict,
  firm_entity_id uuid null references public.firm_entities(id) on delete set null,
  code text not null,
  display_name text not null,
  status text not null default 'active' check (status in ('active', 'inactive')),
  effective_from date null,
  effective_to date null,
  is_deleted boolean not null default false,
  deleted_at timestamptz null,
  deleted_by_user_id uuid null references public.users(id) on delete set null,
  created_at timestamptz not null default timezone('utc', now()),
  created_by_user_id uuid null references public.users(id) on delete set null,
  updated_at timestamptz not null default timezone('utc', now()),
  updated_by_user_id uuid null references public.users(id) on delete set null,
  row_version uuid not null default gen_random_uuid()
);

create unique index if not exists ux_billing_rate_cards_tenant_code
  on public.billing_rate_cards (tenant_id, code)
  where is_deleted = false;

create table if not exists public.billing_rate_card_entries (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete restrict,
  rate_card_id uuid not null references public.billing_rate_cards(id) on delete cascade,
  staff_role text null,
  utbms_task_code text null,
  utbms_expense_code text null,
  hourly_rate numeric(18,4) null,
  flat_rate numeric(18,2) null,
  currency_code text not null default 'USD',
  created_at timestamptz not null default timezone('utc', now()),
  created_by_user_id uuid null references public.users(id) on delete set null,
  updated_at timestamptz not null default timezone('utc', now()),
  updated_by_user_id uuid null references public.users(id) on delete set null,
  row_version uuid not null default gen_random_uuid(),
  constraint ck_billing_rate_card_entries_any_rate check (hourly_rate is not null or flat_rate is not null)
);

create unique index if not exists ux_billing_rate_card_entries_unique
  on public.billing_rate_card_entries (
    tenant_id,
    rate_card_id,
    coalesce(staff_role, ''),
    coalesce(utbms_task_code, ''),
    coalesce(utbms_expense_code, '')
  );

create table if not exists public.matter_billing_policies (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete restrict,
  matter_id uuid not null references public.matters(id) on delete cascade,
  billing_mode text not null check (billing_mode in ('hourly', 'fixed', 'contingency', 'hybrid')),
  rate_card_id uuid null references public.billing_rate_cards(id) on delete set null,
  fixed_fee_amount numeric(18,2) null,
  contingency_percent numeric(18,4) null,
  trust_required boolean not null default false,
  created_at timestamptz not null default timezone('utc', now()),
  created_by_user_id uuid null references public.users(id) on delete set null,
  updated_at timestamptz not null default timezone('utc', now()),
  updated_by_user_id uuid null references public.users(id) on delete set null,
  row_version uuid not null default gen_random_uuid()
);

create unique index if not exists ux_matter_billing_policies_matter
  on public.matter_billing_policies (tenant_id, matter_id);

create table if not exists public.time_entries (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete restrict,
  matter_id uuid not null references public.matters(id) on delete restrict,
  user_id uuid not null references public.users(id) on delete restrict,
  work_date date not null,
  minutes integer not null check (minutes > 0),
  billing_status text not null default 'draft' check (billing_status in ('draft', 'submitted', 'approved', 'billed', 'written_off')),
  narrative text not null,
  hourly_rate numeric(18,4) null,
  amount numeric(18,2) null,
  utbms_task_code text null,
  is_deleted boolean not null default false,
  deleted_at timestamptz null,
  deleted_by_user_id uuid null references public.users(id) on delete set null,
  created_at timestamptz not null default timezone('utc', now()),
  created_by_user_id uuid null references public.users(id) on delete set null,
  updated_at timestamptz not null default timezone('utc', now()),
  updated_by_user_id uuid null references public.users(id) on delete set null,
  row_version uuid not null default gen_random_uuid()
);

create index if not exists ix_time_entries_tenant_matter_work_date
  on public.time_entries (tenant_id, matter_id, work_date desc)
  where is_deleted = false;

create table if not exists public.expenses (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete restrict,
  matter_id uuid not null references public.matters(id) on delete restrict,
  incurred_date date not null,
  category text not null,
  amount numeric(18,2) not null check (amount >= 0),
  billing_status text not null default 'draft' check (billing_status in ('draft', 'approved', 'billed', 'reimbursed', 'written_off')),
  vendor_name text null,
  description_text text null,
  receipt_document_id uuid null,
  is_deleted boolean not null default false,
  deleted_at timestamptz null,
  deleted_by_user_id uuid null references public.users(id) on delete set null,
  created_at timestamptz not null default timezone('utc', now()),
  created_by_user_id uuid null references public.users(id) on delete set null,
  updated_at timestamptz not null default timezone('utc', now()),
  updated_by_user_id uuid null references public.users(id) on delete set null,
  row_version uuid not null default gen_random_uuid()
);

create index if not exists ix_expenses_tenant_matter_incurred
  on public.expenses (tenant_id, matter_id, incurred_date desc)
  where is_deleted = false;

create table if not exists public.invoices (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete restrict,
  client_id uuid not null references public.clients(id) on delete restrict,
  matter_id uuid null references public.matters(id) on delete set null,
  invoice_number text not null,
  status text not null default 'draft' check (status in ('draft', 'issued', 'partially_paid', 'paid', 'void', 'written_off')),
  issue_date date not null,
  due_date date null,
  currency_code text not null default 'USD',
  subtotal numeric(18,2) not null default 0,
  tax_amount numeric(18,2) not null default 0,
  discount_amount numeric(18,2) not null default 0,
  total_amount numeric(18,2) not null default 0,
  amount_paid numeric(18,2) not null default 0,
  balance_amount numeric(18,2) not null default 0,
  notes_text text null,
  terms_text text null,
  created_at timestamptz not null default timezone('utc', now()),
  created_by_user_id uuid null references public.users(id) on delete set null,
  updated_at timestamptz not null default timezone('utc', now()),
  updated_by_user_id uuid null references public.users(id) on delete set null,
  row_version uuid not null default gen_random_uuid()
);

create unique index if not exists ux_invoices_tenant_invoice_number
  on public.invoices (tenant_id, invoice_number);

create index if not exists ix_invoices_tenant_client_status_issue
  on public.invoices (tenant_id, client_id, status, issue_date desc);

create table if not exists public.invoice_line_items (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete restrict,
  invoice_id uuid not null references public.invoices(id) on delete cascade,
  line_type text not null check (line_type in ('time', 'expense', 'adjustment', 'flat_fee')),
  description_text text not null,
  quantity numeric(18,2) not null check (quantity > 0),
  unit_price numeric(18,2) not null check (unit_price >= 0),
  line_total numeric(18,2) not null,
  time_entry_id uuid null references public.time_entries(id) on delete set null,
  expense_id uuid null references public.expenses(id) on delete set null,
  utbms_task_code text null,
  created_at timestamptz not null default timezone('utc', now()),
  created_by_user_id uuid null references public.users(id) on delete set null,
  updated_at timestamptz not null default timezone('utc', now()),
  updated_by_user_id uuid null references public.users(id) on delete set null,
  row_version uuid not null default gen_random_uuid()
);

create index if not exists ix_invoice_line_items_tenant_invoice
  on public.invoice_line_items (tenant_id, invoice_id);

create table if not exists public.invoice_payor_allocations (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete restrict,
  invoice_id uuid not null references public.invoices(id) on delete cascade,
  client_id uuid not null references public.clients(id) on delete restrict,
  allocation_scope text not null check (allocation_scope in ('invoice_total', 'fees', 'expenses', 'tax')),
  allocation_percent numeric(18,4) null,
  allocation_amount numeric(18,2) null,
  created_at timestamptz not null default timezone('utc', now()),
  created_by_user_id uuid null references public.users(id) on delete set null,
  updated_at timestamptz not null default timezone('utc', now()),
  updated_by_user_id uuid null references public.users(id) on delete set null,
  row_version uuid not null default gen_random_uuid(),
  constraint ck_invoice_payor_allocations_value check (allocation_percent is not null or allocation_amount is not null)
);

create unique index if not exists ux_invoice_payor_allocations_unique
  on public.invoice_payor_allocations (tenant_id, invoice_id, client_id, allocation_scope);

create table if not exists public.billing_payments (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete restrict,
  client_id uuid not null references public.clients(id) on delete restrict,
  matter_id uuid null references public.matters(id) on delete set null,
  payment_status text not null default 'pending' check (payment_status in ('pending', 'settled', 'failed', 'refunded', 'voided')),
  payment_method text not null,
  amount numeric(18,2) not null check (amount > 0),
  currency_code text not null default 'USD',
  received_at timestamptz not null default timezone('utc', now()),
  provider_reference text null,
  metadata_json jsonb null,
  created_at timestamptz not null default timezone('utc', now()),
  created_by_user_id uuid null references public.users(id) on delete set null,
  updated_at timestamptz not null default timezone('utc', now()),
  updated_by_user_id uuid null references public.users(id) on delete set null,
  row_version uuid not null default gen_random_uuid()
);

create index if not exists ix_billing_payments_tenant_client_received
  on public.billing_payments (tenant_id, client_id, received_at desc);

create table if not exists public.billing_payment_allocations (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete restrict,
  billing_payment_id uuid not null references public.billing_payments(id) on delete cascade,
  invoice_id uuid not null references public.invoices(id) on delete restrict,
  amount numeric(18,2) not null check (amount > 0),
  created_at timestamptz not null default timezone('utc', now()),
  created_by_user_id uuid null references public.users(id) on delete set null
);

create unique index if not exists ux_billing_payment_allocations_unique
  on public.billing_payment_allocations (tenant_id, billing_payment_id, invoice_id);

create table if not exists public.payment_plans (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete restrict,
  client_id uuid not null references public.clients(id) on delete restrict,
  matter_id uuid null references public.matters(id) on delete set null,
  status text not null default 'draft' check (status in ('draft', 'active', 'paused', 'completed', 'cancelled')),
  plan_name text not null,
  total_amount numeric(18,2) not null check (total_amount >= 0),
  schedule_json jsonb null,
  started_at timestamptz null,
  completed_at timestamptz null,
  created_at timestamptz not null default timezone('utc', now()),
  created_by_user_id uuid null references public.users(id) on delete set null,
  updated_at timestamptz not null default timezone('utc', now()),
  updated_by_user_id uuid null references public.users(id) on delete set null,
  row_version uuid not null default gen_random_uuid()
);

create table if not exists public.outcome_fee_plans (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete restrict,
  matter_id uuid not null references public.matters(id) on delete cascade,
  status text not null default 'draft' check (status in ('draft', 'active', 'retired')),
  display_name text not null,
  active_version_number integer not null default 1,
  created_at timestamptz not null default timezone('utc', now()),
  created_by_user_id uuid null references public.users(id) on delete set null,
  updated_at timestamptz not null default timezone('utc', now()),
  updated_by_user_id uuid null references public.users(id) on delete set null,
  row_version uuid not null default gen_random_uuid()
);

create unique index if not exists ux_outcome_fee_plans_active_matter
  on public.outcome_fee_plans (tenant_id, matter_id, status)
  where status = 'active';

create table if not exists public.outcome_fee_plan_versions (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete restrict,
  outcome_fee_plan_id uuid not null references public.outcome_fee_plans(id) on delete cascade,
  version_number integer not null check (version_number > 0),
  assumptions_json jsonb null,
  forecast_json jsonb null,
  created_at timestamptz not null default timezone('utc', now()),
  created_by_user_id uuid null references public.users(id) on delete set null
);

create unique index if not exists ux_outcome_fee_plan_versions_unique
  on public.outcome_fee_plan_versions (outcome_fee_plan_id, version_number);

drop trigger if exists trg_billing_rate_cards_set_updated_at on public.billing_rate_cards;
create trigger trg_billing_rate_cards_set_updated_at before update on public.billing_rate_cards
for each row execute function public.set_updated_at_and_row_version();

drop trigger if exists trg_billing_rate_card_entries_set_updated_at on public.billing_rate_card_entries;
create trigger trg_billing_rate_card_entries_set_updated_at before update on public.billing_rate_card_entries
for each row execute function public.set_updated_at_and_row_version();

drop trigger if exists trg_matter_billing_policies_set_updated_at on public.matter_billing_policies;
create trigger trg_matter_billing_policies_set_updated_at before update on public.matter_billing_policies
for each row execute function public.set_updated_at_and_row_version();

drop trigger if exists trg_time_entries_set_updated_at on public.time_entries;
create trigger trg_time_entries_set_updated_at before update on public.time_entries
for each row execute function public.set_updated_at_and_row_version();

drop trigger if exists trg_expenses_set_updated_at on public.expenses;
create trigger trg_expenses_set_updated_at before update on public.expenses
for each row execute function public.set_updated_at_and_row_version();

drop trigger if exists trg_invoices_set_updated_at on public.invoices;
create trigger trg_invoices_set_updated_at before update on public.invoices
for each row execute function public.set_updated_at_and_row_version();

drop trigger if exists trg_invoice_line_items_set_updated_at on public.invoice_line_items;
create trigger trg_invoice_line_items_set_updated_at before update on public.invoice_line_items
for each row execute function public.set_updated_at_and_row_version();

drop trigger if exists trg_invoice_payor_allocations_set_updated_at on public.invoice_payor_allocations;
create trigger trg_invoice_payor_allocations_set_updated_at before update on public.invoice_payor_allocations
for each row execute function public.set_updated_at_and_row_version();

drop trigger if exists trg_billing_payments_set_updated_at on public.billing_payments;
create trigger trg_billing_payments_set_updated_at before update on public.billing_payments
for each row execute function public.set_updated_at_and_row_version();

drop trigger if exists trg_payment_plans_set_updated_at on public.payment_plans;
create trigger trg_payment_plans_set_updated_at before update on public.payment_plans
for each row execute function public.set_updated_at_and_row_version();

drop trigger if exists trg_outcome_fee_plans_set_updated_at on public.outcome_fee_plans;
create trigger trg_outcome_fee_plans_set_updated_at before update on public.outcome_fee_plans
for each row execute function public.set_updated_at_and_row_version();
