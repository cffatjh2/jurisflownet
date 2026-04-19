create table if not exists public.clients (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete restrict,
  client_number text null,
  normalized_email text null,
  email text null,
  display_name text not null,
  kind text not null check (kind in ('individual', 'organization')),
  status text not null default 'active' check (status in ('active', 'inactive', 'prospect', 'archived')),
  company_name text null,
  phone text null,
  mobile text null,
  address_line_1 text null,
  address_line_2 text null,
  city text null,
  state_region text null,
  postal_code text null,
  country_code text null,
  tax_id text null,
  notes_text text null,
  metadata_json jsonb null,
  is_deleted boolean not null default false,
  deleted_at timestamptz null,
  deleted_by_user_id uuid null references public.users(id) on delete set null,
  created_at timestamptz not null default timezone('utc', now()),
  created_by_user_id uuid null references public.users(id) on delete set null,
  updated_at timestamptz not null default timezone('utc', now()),
  updated_by_user_id uuid null references public.users(id) on delete set null,
  row_version uuid not null default gen_random_uuid()
);

create unique index if not exists ux_clients_tenant_client_number
  on public.clients (tenant_id, client_number)
  where client_number is not null and is_deleted = false;

create unique index if not exists ux_clients_tenant_normalized_email
  on public.clients (tenant_id, normalized_email)
  where normalized_email is not null and is_deleted = false;

create index if not exists ix_clients_tenant_status_created
  on public.clients (tenant_id, status, created_at desc);

create table if not exists public.client_contacts (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete restrict,
  client_id uuid not null references public.clients(id) on delete cascade,
  normalized_email text null,
  email text null,
  display_name text not null,
  contact_role text null,
  phone text null,
  mobile text null,
  is_primary boolean not null default false,
  is_deleted boolean not null default false,
  deleted_at timestamptz null,
  deleted_by_user_id uuid null references public.users(id) on delete set null,
  created_at timestamptz not null default timezone('utc', now()),
  created_by_user_id uuid null references public.users(id) on delete set null,
  updated_at timestamptz not null default timezone('utc', now()),
  updated_by_user_id uuid null references public.users(id) on delete set null,
  row_version uuid not null default gen_random_uuid()
);

create unique index if not exists ux_client_contacts_tenant_client_normalized_email
  on public.client_contacts (tenant_id, client_id, normalized_email)
  where normalized_email is not null and is_deleted = false;

alter table public.clients
  add column if not exists primary_contact_id uuid null references public.client_contacts(id) on delete set null;

create table if not exists public.client_status_history (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete restrict,
  client_id uuid not null references public.clients(id) on delete cascade,
  previous_status text not null,
  new_status text not null,
  change_note text null,
  changed_by_user_id uuid null references public.users(id) on delete set null,
  changed_at timestamptz not null default timezone('utc', now()),
  constraint ck_client_status_history_distinct check (previous_status <> new_status)
);

create index if not exists ix_client_status_history_tenant_client_changed
  on public.client_status_history (tenant_id, client_id, changed_at desc);

create table if not exists public.leads (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete restrict,
  normalized_email text null,
  email text null,
  display_name text not null,
  source text null,
  status text not null default 'new' check (status in ('new', 'qualified', 'unqualified', 'converted', 'archived')),
  phone text null,
  notes_text text null,
  converted_client_id uuid null references public.clients(id) on delete set null,
  converted_matter_id uuid null,
  is_deleted boolean not null default false,
  deleted_at timestamptz null,
  deleted_by_user_id uuid null references public.users(id) on delete set null,
  created_at timestamptz not null default timezone('utc', now()),
  created_by_user_id uuid null references public.users(id) on delete set null,
  updated_at timestamptz not null default timezone('utc', now()),
  updated_by_user_id uuid null references public.users(id) on delete set null,
  row_version uuid not null default gen_random_uuid()
);

create index if not exists ix_leads_tenant_status_created
  on public.leads (tenant_id, status, created_at desc);

create table if not exists public.matters (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete restrict,
  client_id uuid not null references public.clients(id) on delete restrict,
  firm_entity_id uuid null references public.firm_entities(id) on delete set null,
  office_id uuid null references public.offices(id) on delete set null,
  responsible_user_id uuid not null references public.users(id) on delete restrict,
  case_number text not null,
  case_number_normalized text not null,
  display_name text not null,
  practice_area text null,
  court_type text null,
  status text not null check (status in ('intake', 'open', 'on_hold', 'closed', 'archived')),
  fee_model text not null check (fee_model in ('hourly', 'fixed', 'contingency', 'hybrid')),
  billable_rate numeric(18,4) null,
  open_date timestamptz not null default timezone('utc', now()),
  closed_at timestamptz null,
  archived_at timestamptz null,
  outcome_summary text null,
  metadata_json jsonb null,
  is_deleted boolean not null default false,
  deleted_at timestamptz null,
  deleted_by_user_id uuid null references public.users(id) on delete set null,
  created_at timestamptz not null default timezone('utc', now()),
  created_by_user_id uuid null references public.users(id) on delete set null,
  updated_at timestamptz not null default timezone('utc', now()),
  updated_by_user_id uuid null references public.users(id) on delete set null,
  row_version uuid not null default gen_random_uuid()
);

create unique index if not exists ux_matters_tenant_case_number
  on public.matters (tenant_id, case_number_normalized)
  where is_deleted = false;

create index if not exists ix_matters_tenant_status_open_date
  on public.matters (tenant_id, status, open_date desc);

create index if not exists ix_matters_tenant_client_status
  on public.matters (tenant_id, client_id, status);

create index if not exists ix_matters_tenant_responsible_status
  on public.matters (tenant_id, responsible_user_id, status);

create table if not exists public.matter_clients (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete restrict,
  matter_id uuid not null references public.matters(id) on delete cascade,
  client_id uuid not null references public.clients(id) on delete restrict,
  relationship_type text not null check (relationship_type in ('primary', 'co_client', 'guardian', 'billing_contact', 'organization_contact')),
  created_at timestamptz not null default timezone('utc', now()),
  created_by_user_id uuid null references public.users(id) on delete set null,
  row_version uuid not null default gen_random_uuid()
);

create unique index if not exists ux_matter_clients_tenant_matter_client_role
  on public.matter_clients (tenant_id, matter_id, client_id, relationship_type);

create index if not exists ix_matter_clients_tenant_client
  on public.matter_clients (tenant_id, client_id, created_at desc);

create table if not exists public.matter_staff_assignments (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete restrict,
  matter_id uuid not null references public.matters(id) on delete cascade,
  user_id uuid not null references public.users(id) on delete restrict,
  assignment_role text not null check (assignment_role in ('responsible_attorney', 'billing_attorney', 'support_attorney', 'paralegal', 'reviewer')),
  starts_at timestamptz null,
  ends_at timestamptz null,
  created_at timestamptz not null default timezone('utc', now()),
  created_by_user_id uuid null references public.users(id) on delete set null,
  updated_at timestamptz not null default timezone('utc', now()),
  updated_by_user_id uuid null references public.users(id) on delete set null,
  row_version uuid not null default gen_random_uuid()
);

create unique index if not exists ux_matter_staff_assignments_unique
  on public.matter_staff_assignments (tenant_id, matter_id, user_id, assignment_role);

create table if not exists public.matter_notes (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete restrict,
  matter_id uuid not null references public.matters(id) on delete cascade,
  title text null,
  body_text text not null,
  visibility text not null default 'internal' check (visibility in ('internal', 'private', 'client_visible')),
  note_type text not null default 'general' check (note_type in ('general', 'call', 'meeting', 'strategy', 'billing')),
  is_deleted boolean not null default false,
  deleted_at timestamptz null,
  deleted_by_user_id uuid null references public.users(id) on delete set null,
  created_at timestamptz not null default timezone('utc', now()),
  created_by_user_id uuid null references public.users(id) on delete set null,
  updated_at timestamptz not null default timezone('utc', now()),
  updated_by_user_id uuid null references public.users(id) on delete set null,
  row_version uuid not null default gen_random_uuid()
);

create index if not exists ix_matter_notes_tenant_matter_updated
  on public.matter_notes (tenant_id, matter_id, updated_at desc)
  where is_deleted = false;

create table if not exists public.matter_note_revisions (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete restrict,
  matter_note_id uuid not null references public.matter_notes(id) on delete cascade,
  revision_number integer not null check (revision_number > 0),
  title text null,
  body_text text not null,
  visibility text not null check (visibility in ('internal', 'private', 'client_visible')),
  note_type text not null check (note_type in ('general', 'call', 'meeting', 'strategy', 'billing')),
  revised_at timestamptz not null default timezone('utc', now()),
  revised_by_user_id uuid null references public.users(id) on delete set null
);

create unique index if not exists ux_matter_note_revisions_note_revision
  on public.matter_note_revisions (matter_note_id, revision_number);

create table if not exists public.opposing_parties (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete restrict,
  matter_id uuid not null references public.matters(id) on delete cascade,
  display_name text not null,
  normalized_name text not null,
  party_type text null,
  notes_text text null,
  is_deleted boolean not null default false,
  deleted_at timestamptz null,
  deleted_by_user_id uuid null references public.users(id) on delete set null,
  created_at timestamptz not null default timezone('utc', now()),
  created_by_user_id uuid null references public.users(id) on delete set null,
  updated_at timestamptz not null default timezone('utc', now()),
  updated_by_user_id uuid null references public.users(id) on delete set null,
  row_version uuid not null default gen_random_uuid()
);

create unique index if not exists ux_opposing_parties_tenant_matter_normalized_name
  on public.opposing_parties (tenant_id, matter_id, normalized_name)
  where is_deleted = false;

create table if not exists public.conflict_checks (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete restrict,
  matter_id uuid not null references public.matters(id) on delete cascade,
  status text not null check (status in ('pending', 'cleared', 'waiver_required', 'blocked')),
  checked_at timestamptz not null default timezone('utc', now()),
  checked_by_user_id uuid null references public.users(id) on delete set null,
  notes_text text null,
  created_at timestamptz not null default timezone('utc', now()),
  created_by_user_id uuid null references public.users(id) on delete set null,
  updated_at timestamptz not null default timezone('utc', now()),
  updated_by_user_id uuid null references public.users(id) on delete set null,
  row_version uuid not null default gen_random_uuid()
);

create index if not exists ix_conflict_checks_tenant_matter_checked
  on public.conflict_checks (tenant_id, matter_id, checked_at desc);

create table if not exists public.conflict_results (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete restrict,
  conflict_check_id uuid not null references public.conflict_checks(id) on delete cascade,
  matched_entity_type text not null,
  matched_entity_id uuid null,
  match_summary text not null,
  severity text not null check (severity in ('low', 'medium', 'high', 'blocker')),
  created_at timestamptz not null default timezone('utc', now()),
  created_by_user_id uuid null references public.users(id) on delete set null
);

create index if not exists ix_conflict_results_tenant_check_created
  on public.conflict_results (tenant_id, conflict_check_id, created_at desc);

alter table public.leads
  add constraint fk_leads_converted_matter
  foreign key (converted_matter_id) references public.matters(id) on delete set null;

drop trigger if exists trg_clients_set_updated_at on public.clients;
create trigger trg_clients_set_updated_at before update on public.clients
for each row execute function public.set_updated_at_and_row_version();

drop trigger if exists trg_client_contacts_set_updated_at on public.client_contacts;
create trigger trg_client_contacts_set_updated_at before update on public.client_contacts
for each row execute function public.set_updated_at_and_row_version();

drop trigger if exists trg_leads_set_updated_at on public.leads;
create trigger trg_leads_set_updated_at before update on public.leads
for each row execute function public.set_updated_at_and_row_version();

drop trigger if exists trg_matters_set_updated_at on public.matters;
create trigger trg_matters_set_updated_at before update on public.matters
for each row execute function public.set_updated_at_and_row_version();

drop trigger if exists trg_matter_staff_assignments_set_updated_at on public.matter_staff_assignments;
create trigger trg_matter_staff_assignments_set_updated_at before update on public.matter_staff_assignments
for each row execute function public.set_updated_at_and_row_version();

drop trigger if exists trg_matter_notes_set_updated_at on public.matter_notes;
create trigger trg_matter_notes_set_updated_at before update on public.matter_notes
for each row execute function public.set_updated_at_and_row_version();

drop trigger if exists trg_opposing_parties_set_updated_at on public.opposing_parties;
create trigger trg_opposing_parties_set_updated_at before update on public.opposing_parties
for each row execute function public.set_updated_at_and_row_version();

drop trigger if exists trg_conflict_checks_set_updated_at on public.conflict_checks;
create trigger trg_conflict_checks_set_updated_at before update on public.conflict_checks
for each row execute function public.set_updated_at_and_row_version();
