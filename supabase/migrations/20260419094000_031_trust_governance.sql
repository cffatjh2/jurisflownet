create table if not exists public.trust_approval_requests (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete restrict,
  trust_transaction_id uuid not null references public.trust_transactions(id) on delete cascade,
  status text not null default 'pending' check (status in ('pending', 'approved', 'rejected', 'expired')),
  requested_at timestamptz not null default timezone('utc', now()),
  requested_by_user_id uuid null references public.users(id) on delete set null,
  expires_at timestamptz null,
  policy_snapshot_json jsonb null,
  created_at timestamptz not null default timezone('utc', now()),
  created_by_user_id uuid null references public.users(id) on delete set null,
  updated_at timestamptz not null default timezone('utc', now()),
  updated_by_user_id uuid null references public.users(id) on delete set null,
  row_version uuid not null default gen_random_uuid()
);

create unique index if not exists ux_trust_approval_requests_transaction
  on public.trust_approval_requests (tenant_id, trust_transaction_id);

create table if not exists public.trust_approval_decisions (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete restrict,
  approval_request_id uuid not null references public.trust_approval_requests(id) on delete cascade,
  decided_by_user_id uuid null references public.users(id) on delete set null,
  decision text not null check (decision in ('approve', 'reject', 'override')),
  reason_text text null,
  decided_at timestamptz not null default timezone('utc', now()),
  metadata_json jsonb null,
  created_at timestamptz not null default timezone('utc', now())
);

create index if not exists ix_trust_approval_decisions_request_decided
  on public.trust_approval_decisions (tenant_id, approval_request_id, decided_at desc);

create table if not exists public.trust_month_closes (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete restrict,
  trust_account_id uuid not null references public.trust_accounts(id) on delete restrict,
  close_month date not null,
  status text not null default 'open' check (status in ('open', 'in_progress', 'closed', 'reopened')),
  closed_at timestamptz null,
  closed_by_user_id uuid null references public.users(id) on delete set null,
  summary_json jsonb null,
  created_at timestamptz not null default timezone('utc', now()),
  created_by_user_id uuid null references public.users(id) on delete set null,
  updated_at timestamptz not null default timezone('utc', now()),
  updated_by_user_id uuid null references public.users(id) on delete set null,
  row_version uuid not null default gen_random_uuid()
);

create unique index if not exists ux_trust_month_closes_account_month
  on public.trust_month_closes (tenant_id, trust_account_id, close_month);

create table if not exists public.trust_reconciliation_snapshots (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete restrict,
  trust_account_id uuid not null references public.trust_accounts(id) on delete restrict,
  snapshot_date date not null,
  bank_balance numeric(18,2) not null default 0,
  book_balance numeric(18,2) not null default 0,
  difference_amount numeric(18,2) not null default 0,
  metadata_json jsonb null,
  created_at timestamptz not null default timezone('utc', now()),
  created_by_user_id uuid null references public.users(id) on delete set null,
  updated_at timestamptz not null default timezone('utc', now()),
  updated_by_user_id uuid null references public.users(id) on delete set null,
  row_version uuid not null default gen_random_uuid()
);

create unique index if not exists ux_trust_reconciliation_snapshots_account_date
  on public.trust_reconciliation_snapshots (tenant_id, trust_account_id, snapshot_date);

create table if not exists public.trust_reconciliation_packets (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete restrict,
  snapshot_id uuid not null references public.trust_reconciliation_snapshots(id) on delete cascade,
  packet_number text not null,
  status text not null default 'draft' check (status in ('draft', 'sealed', 'superseded')),
  sealed_at timestamptz null,
  sealed_by_user_id uuid null references public.users(id) on delete set null,
  document_bundle_json jsonb null,
  created_at timestamptz not null default timezone('utc', now()),
  created_by_user_id uuid null references public.users(id) on delete set null,
  updated_at timestamptz not null default timezone('utc', now()),
  updated_by_user_id uuid null references public.users(id) on delete set null,
  row_version uuid not null default gen_random_uuid()
);

create unique index if not exists ux_trust_reconciliation_packets_number
  on public.trust_reconciliation_packets (tenant_id, packet_number);

create table if not exists public.trust_compliance_exports (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete restrict,
  trust_account_id uuid not null references public.trust_accounts(id) on delete restrict,
  export_type text not null,
  export_period_start date not null,
  export_period_end date not null,
  status text not null default 'generated' check (status in ('generated', 'delivered', 'failed')),
  file_path text null,
  generated_at timestamptz not null default timezone('utc', now()),
  created_at timestamptz not null default timezone('utc', now()),
  created_by_user_id uuid null references public.users(id) on delete set null,
  updated_at timestamptz not null default timezone('utc', now()),
  updated_by_user_id uuid null references public.users(id) on delete set null,
  row_version uuid not null default gen_random_uuid()
);

create unique index if not exists ux_trust_compliance_exports_unique
  on public.trust_compliance_exports (tenant_id, export_type, export_period_start, export_period_end, trust_account_id);

create table if not exists public.trust_operational_alerts (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete restrict,
  trust_account_id uuid null references public.trust_accounts(id) on delete set null,
  severity text not null check (severity in ('info', 'warning', 'high', 'critical')),
  status text not null default 'open' check (status in ('open', 'acknowledged', 'resolved')),
  title text not null,
  description_text text not null,
  acknowledged_at timestamptz null,
  acknowledged_by_user_id uuid null references public.users(id) on delete set null,
  resolved_at timestamptz null,
  resolved_by_user_id uuid null references public.users(id) on delete set null,
  metadata_json jsonb null,
  created_at timestamptz not null default timezone('utc', now()),
  created_by_user_id uuid null references public.users(id) on delete set null,
  updated_at timestamptz not null default timezone('utc', now()),
  updated_by_user_id uuid null references public.users(id) on delete set null,
  row_version uuid not null default gen_random_uuid()
);

create index if not exists ix_trust_operational_alerts_tenant_status_created
  on public.trust_operational_alerts (tenant_id, status, created_at desc);

drop trigger if exists trg_trust_approval_requests_set_updated_at on public.trust_approval_requests;
create trigger trg_trust_approval_requests_set_updated_at before update on public.trust_approval_requests
for each row execute function public.set_updated_at_and_row_version();

drop trigger if exists trg_trust_month_closes_set_updated_at on public.trust_month_closes;
create trigger trg_trust_month_closes_set_updated_at before update on public.trust_month_closes
for each row execute function public.set_updated_at_and_row_version();

drop trigger if exists trg_trust_reconciliation_snapshots_set_updated_at on public.trust_reconciliation_snapshots;
create trigger trg_trust_reconciliation_snapshots_set_updated_at before update on public.trust_reconciliation_snapshots
for each row execute function public.set_updated_at_and_row_version();

drop trigger if exists trg_trust_reconciliation_packets_set_updated_at on public.trust_reconciliation_packets;
create trigger trg_trust_reconciliation_packets_set_updated_at before update on public.trust_reconciliation_packets
for each row execute function public.set_updated_at_and_row_version();

drop trigger if exists trg_trust_compliance_exports_set_updated_at on public.trust_compliance_exports;
create trigger trg_trust_compliance_exports_set_updated_at before update on public.trust_compliance_exports
for each row execute function public.set_updated_at_and_row_version();

drop trigger if exists trg_trust_operational_alerts_set_updated_at on public.trust_operational_alerts;
create trigger trg_trust_operational_alerts_set_updated_at before update on public.trust_operational_alerts
for each row execute function public.set_updated_at_and_row_version();
