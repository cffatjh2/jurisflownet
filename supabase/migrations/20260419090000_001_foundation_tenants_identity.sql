create extension if not exists pgcrypto;

create or replace function public.set_updated_at_and_row_version()
returns trigger
language plpgsql
as $$
begin
  new.updated_at = timezone('utc', now());
  new.row_version = gen_random_uuid();
  return new;
end;
$$;

create table if not exists public.tenants (
  id uuid primary key default gen_random_uuid(),
  slug text not null,
  name text not null,
  status text not null default 'active' check (status in ('active', 'suspended', 'disabled')),
  created_at timestamptz not null default timezone('utc', now()),
  created_by_user_id uuid null,
  updated_at timestamptz not null default timezone('utc', now()),
  updated_by_user_id uuid null,
  row_version uuid not null default gen_random_uuid()
);

create unique index if not exists ux_tenants_slug on public.tenants (slug);

create table if not exists public.firm_entities (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete restrict,
  code text not null,
  name text not null,
  status text not null default 'active' check (status in ('active', 'inactive')),
  metadata_json jsonb null,
  is_deleted boolean not null default false,
  deleted_at timestamptz null,
  deleted_by_user_id uuid null,
  created_at timestamptz not null default timezone('utc', now()),
  created_by_user_id uuid null,
  updated_at timestamptz not null default timezone('utc', now()),
  updated_by_user_id uuid null,
  row_version uuid not null default gen_random_uuid()
);

create unique index if not exists ux_firm_entities_tenant_code
  on public.firm_entities (tenant_id, code)
  where is_deleted = false;

create table if not exists public.offices (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete restrict,
  firm_entity_id uuid not null references public.firm_entities(id) on delete restrict,
  code text not null,
  name text not null,
  timezone text null,
  status text not null default 'active' check (status in ('active', 'inactive')),
  is_deleted boolean not null default false,
  deleted_at timestamptz null,
  deleted_by_user_id uuid null,
  created_at timestamptz not null default timezone('utc', now()),
  created_by_user_id uuid null,
  updated_at timestamptz not null default timezone('utc', now()),
  updated_by_user_id uuid null,
  row_version uuid not null default gen_random_uuid()
);

create unique index if not exists ux_offices_tenant_entity_code
  on public.offices (tenant_id, firm_entity_id, code)
  where is_deleted = false;

create table if not exists public.users (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete restrict,
  normalized_email text not null,
  email text not null,
  display_name text not null,
  status text not null default 'active' check (status in ('active', 'invited', 'disabled')),
  password_hash text null,
  phone text null,
  avatar_url text null,
  preferences_json jsonb null,
  notification_preferences_json jsonb null,
  mfa_enabled boolean not null default false,
  mfa_secret text null,
  last_login_at timestamptz null,
  is_deleted boolean not null default false,
  deleted_at timestamptz null,
  deleted_by_user_id uuid null,
  created_at timestamptz not null default timezone('utc', now()),
  created_by_user_id uuid null references public.users(id) on delete set null,
  updated_at timestamptz not null default timezone('utc', now()),
  updated_by_user_id uuid null references public.users(id) on delete set null,
  row_version uuid not null default gen_random_uuid()
);

create unique index if not exists ux_users_tenant_normalized_email
  on public.users (tenant_id, normalized_email)
  where is_deleted = false;

alter table public.tenants
  add constraint fk_tenants_created_by_user
  foreign key (created_by_user_id) references public.users(id) on delete set null;

alter table public.tenants
  add constraint fk_tenants_updated_by_user
  foreign key (updated_by_user_id) references public.users(id) on delete set null;

alter table public.firm_entities
  add constraint fk_firm_entities_created_by_user
  foreign key (created_by_user_id) references public.users(id) on delete set null;

alter table public.firm_entities
  add constraint fk_firm_entities_updated_by_user
  foreign key (updated_by_user_id) references public.users(id) on delete set null;

alter table public.firm_entities
  add constraint fk_firm_entities_deleted_by_user
  foreign key (deleted_by_user_id) references public.users(id) on delete set null;

alter table public.offices
  add constraint fk_offices_created_by_user
  foreign key (created_by_user_id) references public.users(id) on delete set null;

alter table public.offices
  add constraint fk_offices_updated_by_user
  foreign key (updated_by_user_id) references public.users(id) on delete set null;

alter table public.offices
  add constraint fk_offices_deleted_by_user
  foreign key (deleted_by_user_id) references public.users(id) on delete set null;

create table if not exists public.role_definitions (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid null references public.tenants(id) on delete cascade,
  key text not null,
  display_name text not null,
  description text null,
  permissions_json jsonb not null default '{}'::jsonb,
  is_system boolean not null default false,
  created_at timestamptz not null default timezone('utc', now()),
  created_by_user_id uuid null references public.users(id) on delete set null,
  updated_at timestamptz not null default timezone('utc', now()),
  updated_by_user_id uuid null references public.users(id) on delete set null,
  row_version uuid not null default gen_random_uuid()
);

create unique index if not exists ux_role_definitions_global_key
  on public.role_definitions (key)
  where tenant_id is null;

create unique index if not exists ux_role_definitions_tenant_key
  on public.role_definitions (tenant_id, key)
  where tenant_id is not null;

create table if not exists public.user_role_assignments (
  id uuid primary key default gen_random_uuid(),
  user_id uuid not null references public.users(id) on delete cascade,
  role_id uuid not null references public.role_definitions(id) on delete cascade,
  created_at timestamptz not null default timezone('utc', now()),
  created_by_user_id uuid null references public.users(id) on delete set null
);

create unique index if not exists ux_user_role_assignments_user_role
  on public.user_role_assignments (user_id, role_id);

create table if not exists public.staff_profiles (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete restrict,
  user_id uuid not null references public.users(id) on delete cascade,
  bar_number text null,
  job_title text null,
  department text null,
  office_id uuid null references public.offices(id) on delete set null,
  firm_entity_id uuid null references public.firm_entities(id) on delete set null,
  hire_date date null,
  employment_status text not null default 'active' check (employment_status in ('active', 'inactive', 'leave')),
  is_deleted boolean not null default false,
  deleted_at timestamptz null,
  deleted_by_user_id uuid null references public.users(id) on delete set null,
  created_at timestamptz not null default timezone('utc', now()),
  created_by_user_id uuid null references public.users(id) on delete set null,
  updated_at timestamptz not null default timezone('utc', now()),
  updated_by_user_id uuid null references public.users(id) on delete set null,
  row_version uuid not null default gen_random_uuid()
);

create unique index if not exists ux_staff_profiles_tenant_user
  on public.staff_profiles (tenant_id, user_id)
  where is_deleted = false;

create table if not exists public.auth_sessions (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete cascade,
  user_id uuid not null references public.users(id) on delete cascade,
  session_key text not null,
  refresh_token_hash text not null,
  expires_at timestamptz not null,
  revoked_at timestamptz null,
  metadata_json jsonb null,
  created_at timestamptz not null default timezone('utc', now()),
  created_by_user_id uuid null references public.users(id) on delete set null,
  updated_at timestamptz not null default timezone('utc', now()),
  updated_by_user_id uuid null references public.users(id) on delete set null,
  row_version uuid not null default gen_random_uuid()
);

create unique index if not exists ux_auth_sessions_session_key
  on public.auth_sessions (session_key);

create index if not exists ix_auth_sessions_tenant_user_expires
  on public.auth_sessions (tenant_id, user_id, expires_at desc);

create table if not exists public.mfa_challenges (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete cascade,
  user_id uuid not null references public.users(id) on delete cascade,
  challenge_key text not null,
  challenge_type text not null check (challenge_type in ('totp', 'backup_code', 'email_otp')),
  status text not null default 'pending' check (status in ('pending', 'completed', 'expired', 'cancelled')),
  expires_at timestamptz not null,
  completed_at timestamptz null,
  created_at timestamptz not null default timezone('utc', now()),
  created_by_user_id uuid null references public.users(id) on delete set null,
  updated_at timestamptz not null default timezone('utc', now()),
  updated_by_user_id uuid null references public.users(id) on delete set null,
  row_version uuid not null default gen_random_uuid()
);

create unique index if not exists ux_mfa_challenges_key
  on public.mfa_challenges (challenge_key);

create table if not exists public.retention_policies (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete cascade,
  policy_key text not null,
  display_name text not null,
  retention_days integer not null check (retention_days > 0),
  archive_after_days integer null check (archive_after_days is null or archive_after_days > 0),
  is_legal_hold_aware boolean not null default true,
  created_at timestamptz not null default timezone('utc', now()),
  created_by_user_id uuid null references public.users(id) on delete set null,
  updated_at timestamptz not null default timezone('utc', now()),
  updated_by_user_id uuid null references public.users(id) on delete set null,
  row_version uuid not null default gen_random_uuid()
);

create unique index if not exists ux_retention_policies_tenant_key
  on public.retention_policies (tenant_id, policy_key);

create table if not exists public.audit_logs (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete cascade,
  sequence bigint generated always as identity,
  actor_user_id uuid null references public.users(id) on delete set null,
  action text not null,
  entity_type text not null,
  entity_id uuid null,
  request_id text null,
  ip_address inet null,
  old_values jsonb null,
  new_values jsonb null,
  metadata_json jsonb null,
  created_at timestamptz not null default timezone('utc', now())
);

create unique index if not exists ux_audit_logs_tenant_sequence
  on public.audit_logs (tenant_id, sequence);

create index if not exists ix_audit_logs_tenant_entity_created
  on public.audit_logs (tenant_id, entity_type, created_at desc);

drop trigger if exists trg_tenants_set_updated_at on public.tenants;
create trigger trg_tenants_set_updated_at before update on public.tenants
for each row execute function public.set_updated_at_and_row_version();

drop trigger if exists trg_firm_entities_set_updated_at on public.firm_entities;
create trigger trg_firm_entities_set_updated_at before update on public.firm_entities
for each row execute function public.set_updated_at_and_row_version();

drop trigger if exists trg_offices_set_updated_at on public.offices;
create trigger trg_offices_set_updated_at before update on public.offices
for each row execute function public.set_updated_at_and_row_version();

drop trigger if exists trg_users_set_updated_at on public.users;
create trigger trg_users_set_updated_at before update on public.users
for each row execute function public.set_updated_at_and_row_version();

drop trigger if exists trg_role_definitions_set_updated_at on public.role_definitions;
create trigger trg_role_definitions_set_updated_at before update on public.role_definitions
for each row execute function public.set_updated_at_and_row_version();

drop trigger if exists trg_staff_profiles_set_updated_at on public.staff_profiles;
create trigger trg_staff_profiles_set_updated_at before update on public.staff_profiles
for each row execute function public.set_updated_at_and_row_version();

drop trigger if exists trg_auth_sessions_set_updated_at on public.auth_sessions;
create trigger trg_auth_sessions_set_updated_at before update on public.auth_sessions
for each row execute function public.set_updated_at_and_row_version();

drop trigger if exists trg_mfa_challenges_set_updated_at on public.mfa_challenges;
create trigger trg_mfa_challenges_set_updated_at before update on public.mfa_challenges
for each row execute function public.set_updated_at_and_row_version();

drop trigger if exists trg_retention_policies_set_updated_at on public.retention_policies;
create trigger trg_retention_policies_set_updated_at before update on public.retention_policies
for each row execute function public.set_updated_at_and_row_version();
