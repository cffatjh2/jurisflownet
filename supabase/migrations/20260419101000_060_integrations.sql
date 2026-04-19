create table if not exists public.integration_connections (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete restrict,
  provider text not null,
  provider_key text not null,
  category text not null,
  external_account_id text null,
  account_label text null,
  account_email text null,
  status text not null default 'connected' check (status in ('connected', 'paused', 'error', 'revoked')),
  sync_enabled boolean not null default true,
  sync_cursor text null,
  delta_token text null,
  metadata_json jsonb null,
  last_sync_at timestamptz null,
  last_webhook_at timestamptz null,
  is_deleted boolean not null default false,
  deleted_at timestamptz null,
  deleted_by_user_id uuid null references public.users(id) on delete set null,
  created_at timestamptz not null default timezone('utc', now()),
  created_by_user_id uuid null references public.users(id) on delete set null,
  updated_at timestamptz not null default timezone('utc', now()),
  updated_by_user_id uuid null references public.users(id) on delete set null,
  row_version uuid not null default gen_random_uuid()
);

create unique index if not exists ux_integration_connections_unique
  on public.integration_connections (tenant_id, provider, provider_key, coalesce(external_account_id, ''))
  where is_deleted = false;

create table if not exists public.integration_secrets (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete restrict,
  connection_id uuid not null references public.integration_connections(id) on delete cascade,
  secret_type text not null,
  secret_reference text not null,
  rotated_at timestamptz null,
  created_at timestamptz not null default timezone('utc', now()),
  created_by_user_id uuid null references public.users(id) on delete set null,
  updated_at timestamptz not null default timezone('utc', now()),
  updated_by_user_id uuid null references public.users(id) on delete set null,
  row_version uuid not null default gen_random_uuid()
);

create unique index if not exists ux_integration_secrets_connection_type
  on public.integration_secrets (tenant_id, connection_id, secret_type);

create table if not exists public.integration_runs (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete restrict,
  connection_id uuid not null references public.integration_connections(id) on delete cascade,
  run_type text not null,
  status text not null default 'queued' check (status in ('queued', 'running', 'completed', 'failed', 'cancelled')),
  started_at timestamptz null,
  completed_at timestamptz null,
  failure_reason text null,
  metrics_json jsonb null,
  created_at timestamptz not null default timezone('utc', now()),
  created_by_user_id uuid null references public.users(id) on delete set null,
  updated_at timestamptz not null default timezone('utc', now()),
  updated_by_user_id uuid null references public.users(id) on delete set null,
  row_version uuid not null default gen_random_uuid()
);

create index if not exists ix_integration_runs_tenant_connection_created
  on public.integration_runs (tenant_id, connection_id, created_at desc);

create table if not exists public.integration_entity_links (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete restrict,
  connection_id uuid not null references public.integration_connections(id) on delete cascade,
  local_entity_type text not null,
  local_entity_id uuid not null,
  external_entity_type text not null,
  external_entity_id text not null,
  metadata_json jsonb null,
  created_at timestamptz not null default timezone('utc', now()),
  created_by_user_id uuid null references public.users(id) on delete set null,
  updated_at timestamptz not null default timezone('utc', now()),
  updated_by_user_id uuid null references public.users(id) on delete set null,
  row_version uuid not null default gen_random_uuid()
);

create unique index if not exists ux_integration_entity_links_unique
  on public.integration_entity_links (
    tenant_id,
    connection_id,
    local_entity_type,
    local_entity_id,
    external_entity_type,
    external_entity_id
  );

create table if not exists public.integration_mapping_profiles (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete restrict,
  connection_id uuid not null references public.integration_connections(id) on delete cascade,
  profile_key text not null,
  display_name text not null,
  mapping_payload jsonb not null default '{}'::jsonb,
  is_deleted boolean not null default false,
  deleted_at timestamptz null,
  deleted_by_user_id uuid null references public.users(id) on delete set null,
  created_at timestamptz not null default timezone('utc', now()),
  created_by_user_id uuid null references public.users(id) on delete set null,
  updated_at timestamptz not null default timezone('utc', now()),
  updated_by_user_id uuid null references public.users(id) on delete set null,
  row_version uuid not null default gen_random_uuid()
);

create unique index if not exists ux_integration_mapping_profiles_unique
  on public.integration_mapping_profiles (tenant_id, connection_id, profile_key)
  where is_deleted = false;

create table if not exists public.integration_inbox_events (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete restrict,
  connection_id uuid null references public.integration_connections(id) on delete set null,
  provider_event_id text null,
  event_type text not null,
  payload jsonb not null,
  received_at timestamptz not null default timezone('utc', now()),
  processed_at timestamptz null,
  processing_status text not null default 'pending' check (processing_status in ('pending', 'processed', 'failed', 'ignored')),
  failure_reason text null,
  created_at timestamptz not null default timezone('utc', now())
);

create unique index if not exists ux_integration_inbox_events_provider_id
  on public.integration_inbox_events (tenant_id, provider_event_id)
  where provider_event_id is not null;

create table if not exists public.integration_outbox_events (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete restrict,
  connection_id uuid null references public.integration_connections(id) on delete set null,
  event_key text not null,
  event_type text not null,
  payload jsonb not null,
  delivery_status text not null default 'pending' check (delivery_status in ('pending', 'delivered', 'failed', 'cancelled')),
  next_attempt_at timestamptz null,
  attempt_count integer not null default 0,
  delivered_at timestamptz null,
  failure_reason text null,
  created_at timestamptz not null default timezone('utc', now()),
  created_by_user_id uuid null references public.users(id) on delete set null,
  updated_at timestamptz not null default timezone('utc', now()),
  updated_by_user_id uuid null references public.users(id) on delete set null,
  row_version uuid not null default gen_random_uuid()
);

create unique index if not exists ux_integration_outbox_events_event_key
  on public.integration_outbox_events (tenant_id, event_key);

create table if not exists public.integration_review_queue_items (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete restrict,
  connection_id uuid null references public.integration_connections(id) on delete set null,
  status text not null default 'open' check (status in ('open', 'assigned', 'resolved', 'dismissed')),
  summary text not null,
  payload jsonb null,
  assigned_to_user_id uuid null references public.users(id) on delete set null,
  resolved_at timestamptz null,
  resolved_by_user_id uuid null references public.users(id) on delete set null,
  created_at timestamptz not null default timezone('utc', now()),
  created_by_user_id uuid null references public.users(id) on delete set null,
  updated_at timestamptz not null default timezone('utc', now()),
  updated_by_user_id uuid null references public.users(id) on delete set null,
  row_version uuid not null default gen_random_uuid()
);

create index if not exists ix_integration_review_queue_status_created
  on public.integration_review_queue_items (tenant_id, status, created_at desc);

create table if not exists public.integration_conflict_queue_items (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete restrict,
  connection_id uuid null references public.integration_connections(id) on delete set null,
  status text not null default 'open' check (status in ('open', 'resolved', 'ignored')),
  conflict_key text not null,
  summary text not null,
  payload jsonb null,
  resolved_at timestamptz null,
  resolved_by_user_id uuid null references public.users(id) on delete set null,
  created_at timestamptz not null default timezone('utc', now()),
  created_by_user_id uuid null references public.users(id) on delete set null,
  updated_at timestamptz not null default timezone('utc', now()),
  updated_by_user_id uuid null references public.users(id) on delete set null,
  row_version uuid not null default gen_random_uuid()
);

create unique index if not exists ux_integration_conflict_queue_conflict_key
  on public.integration_conflict_queue_items (tenant_id, conflict_key);

create table if not exists public.stripe_webhook_events (
  id uuid primary key default gen_random_uuid(),
  provider_event_id text not null,
  event_type text not null,
  payload jsonb not null,
  received_at timestamptz not null default timezone('utc', now()),
  processed_at timestamptz null,
  processing_status text not null default 'pending' check (processing_status in ('pending', 'processed', 'failed', 'ignored')),
  failure_reason text null
);

create unique index if not exists ux_stripe_webhook_events_provider_event_id
  on public.stripe_webhook_events (provider_event_id);

drop trigger if exists trg_integration_connections_set_updated_at on public.integration_connections;
create trigger trg_integration_connections_set_updated_at before update on public.integration_connections
for each row execute function public.set_updated_at_and_row_version();

drop trigger if exists trg_integration_secrets_set_updated_at on public.integration_secrets;
create trigger trg_integration_secrets_set_updated_at before update on public.integration_secrets
for each row execute function public.set_updated_at_and_row_version();

drop trigger if exists trg_integration_runs_set_updated_at on public.integration_runs;
create trigger trg_integration_runs_set_updated_at before update on public.integration_runs
for each row execute function public.set_updated_at_and_row_version();

drop trigger if exists trg_integration_entity_links_set_updated_at on public.integration_entity_links;
create trigger trg_integration_entity_links_set_updated_at before update on public.integration_entity_links
for each row execute function public.set_updated_at_and_row_version();

drop trigger if exists trg_integration_mapping_profiles_set_updated_at on public.integration_mapping_profiles;
create trigger trg_integration_mapping_profiles_set_updated_at before update on public.integration_mapping_profiles
for each row execute function public.set_updated_at_and_row_version();

drop trigger if exists trg_integration_outbox_events_set_updated_at on public.integration_outbox_events;
create trigger trg_integration_outbox_events_set_updated_at before update on public.integration_outbox_events
for each row execute function public.set_updated_at_and_row_version();

drop trigger if exists trg_integration_review_queue_items_set_updated_at on public.integration_review_queue_items;
create trigger trg_integration_review_queue_items_set_updated_at before update on public.integration_review_queue_items
for each row execute function public.set_updated_at_and_row_version();

drop trigger if exists trg_integration_conflict_queue_items_set_updated_at on public.integration_conflict_queue_items;
create trigger trg_integration_conflict_queue_items_set_updated_at before update on public.integration_conflict_queue_items
for each row execute function public.set_updated_at_and_row_version();
