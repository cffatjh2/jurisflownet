create table if not exists public.documents (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete restrict,
  matter_id uuid null references public.matters(id) on delete set null,
  uploaded_by_user_id uuid null references public.users(id) on delete set null,
  name text not null,
  category text null,
  status text not null default 'draft' check (status in ('draft', 'active', 'archived', 'legal_hold')),
  current_version_number integer not null default 1,
  tags_json jsonb null,
  description_text text null,
  legal_hold_reason text null,
  legal_hold_placed_at timestamptz null,
  legal_hold_placed_by_user_id uuid null references public.users(id) on delete set null,
  is_deleted boolean not null default false,
  deleted_at timestamptz null,
  deleted_by_user_id uuid null references public.users(id) on delete set null,
  created_at timestamptz not null default timezone('utc', now()),
  created_by_user_id uuid null references public.users(id) on delete set null,
  updated_at timestamptz not null default timezone('utc', now()),
  updated_by_user_id uuid null references public.users(id) on delete set null,
  row_version uuid not null default gen_random_uuid()
);

create index if not exists ix_documents_tenant_matter_created
  on public.documents (tenant_id, matter_id, created_at desc)
  where is_deleted = false;

create table if not exists public.document_versions (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete restrict,
  document_id uuid not null references public.documents(id) on delete cascade,
  version_number integer not null check (version_number > 0),
  file_name text not null,
  storage_object_key text not null,
  mime_type text not null,
  file_size_bytes bigint not null check (file_size_bytes >= 0),
  checksum_sha256 text null,
  encryption_metadata_json jsonb null,
  created_at timestamptz not null default timezone('utc', now()),
  created_by_user_id uuid null references public.users(id) on delete set null
);

create unique index if not exists ux_document_versions_document_version
  on public.document_versions (document_id, version_number);

create unique index if not exists ux_document_versions_storage_object_key
  on public.document_versions (storage_object_key);

create table if not exists public.document_shares (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete restrict,
  document_id uuid not null references public.documents(id) on delete cascade,
  client_id uuid not null references public.clients(id) on delete cascade,
  share_scope text not null check (share_scope in ('portal', 'download', 'view_only')),
  expires_at timestamptz null,
  revoked_at timestamptz null,
  revoked_by_user_id uuid null references public.users(id) on delete set null,
  is_deleted boolean not null default false,
  deleted_at timestamptz null,
  deleted_by_user_id uuid null references public.users(id) on delete set null,
  created_at timestamptz not null default timezone('utc', now()),
  created_by_user_id uuid null references public.users(id) on delete set null,
  updated_at timestamptz not null default timezone('utc', now()),
  updated_by_user_id uuid null references public.users(id) on delete set null,
  row_version uuid not null default gen_random_uuid()
);

create unique index if not exists ux_document_shares_unique
  on public.document_shares (tenant_id, document_id, client_id, share_scope)
  where is_deleted = false;

create table if not exists public.document_comments (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete restrict,
  document_id uuid not null references public.documents(id) on delete cascade,
  author_user_id uuid null references public.users(id) on delete set null,
  body_text text not null check (length(trim(body_text)) > 0),
  is_deleted boolean not null default false,
  deleted_at timestamptz null,
  deleted_by_user_id uuid null references public.users(id) on delete set null,
  created_at timestamptz not null default timezone('utc', now()),
  created_by_user_id uuid null references public.users(id) on delete set null,
  updated_at timestamptz not null default timezone('utc', now()),
  updated_by_user_id uuid null references public.users(id) on delete set null,
  row_version uuid not null default gen_random_uuid()
);

create table if not exists public.document_content_indexes (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete restrict,
  document_version_id uuid not null references public.document_versions(id) on delete cascade,
  content_tsv tsvector null,
  extracted_text text null,
  language_code text null,
  created_at timestamptz not null default timezone('utc', now()),
  created_by_user_id uuid null references public.users(id) on delete set null,
  updated_at timestamptz not null default timezone('utc', now()),
  updated_by_user_id uuid null references public.users(id) on delete set null,
  row_version uuid not null default gen_random_uuid()
);

create unique index if not exists ux_document_content_indexes_document_version
  on public.document_content_indexes (tenant_id, document_version_id);

create table if not exists public.signature_requests (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete restrict,
  document_id uuid not null references public.documents(id) on delete cascade,
  client_id uuid null references public.clients(id) on delete set null,
  status text not null default 'draft' check (status in ('draft', 'sent', 'viewed', 'signed', 'expired', 'cancelled')),
  provider_key text null,
  provider_request_id text null,
  sent_at timestamptz null,
  expires_at timestamptz null,
  signed_at timestamptz null,
  metadata_json jsonb null,
  created_at timestamptz not null default timezone('utc', now()),
  created_by_user_id uuid null references public.users(id) on delete set null,
  updated_at timestamptz not null default timezone('utc', now()),
  updated_by_user_id uuid null references public.users(id) on delete set null,
  row_version uuid not null default gen_random_uuid()
);

create table if not exists public.signature_audit_entries (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete restrict,
  signature_request_id uuid not null references public.signature_requests(id) on delete cascade,
  action text not null,
  actor_user_id uuid null references public.users(id) on delete set null,
  actor_client_id uuid null references public.clients(id) on delete set null,
  metadata_json jsonb null,
  created_at timestamptz not null default timezone('utc', now())
);

create table if not exists public.client_portal_accounts (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete restrict,
  client_id uuid not null references public.clients(id) on delete cascade,
  normalized_email text not null,
  email text not null,
  password_hash text null,
  status text not null default 'pending' check (status in ('pending', 'active', 'locked', 'disabled')),
  portal_enabled boolean not null default false,
  last_login_at timestamptz null,
  failed_login_count integer not null default 0,
  locked_at timestamptz null,
  is_deleted boolean not null default false,
  deleted_at timestamptz null,
  deleted_by_user_id uuid null references public.users(id) on delete set null,
  created_at timestamptz not null default timezone('utc', now()),
  created_by_user_id uuid null references public.users(id) on delete set null,
  updated_at timestamptz not null default timezone('utc', now()),
  updated_by_user_id uuid null references public.users(id) on delete set null,
  row_version uuid not null default gen_random_uuid()
);

create unique index if not exists ux_client_portal_accounts_client
  on public.client_portal_accounts (tenant_id, client_id)
  where is_deleted = false;

create unique index if not exists ux_client_portal_accounts_email
  on public.client_portal_accounts (tenant_id, normalized_email)
  where is_deleted = false;

create table if not exists public.client_messages (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete restrict,
  client_id uuid not null references public.clients(id) on delete cascade,
  matter_id uuid null references public.matters(id) on delete set null,
  direction text not null check (direction in ('inbound', 'outbound')),
  subject text null,
  body_text text not null,
  sent_at timestamptz null,
  read_at timestamptz null,
  is_deleted boolean not null default false,
  deleted_at timestamptz null,
  deleted_by_user_id uuid null references public.users(id) on delete set null,
  created_at timestamptz not null default timezone('utc', now()),
  created_by_user_id uuid null references public.users(id) on delete set null,
  updated_at timestamptz not null default timezone('utc', now()),
  updated_by_user_id uuid null references public.users(id) on delete set null,
  row_version uuid not null default gen_random_uuid()
);

create table if not exists public.email_messages (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete restrict,
  matter_id uuid null references public.matters(id) on delete set null,
  provider_message_id text null,
  direction text not null check (direction in ('inbound', 'outbound')),
  from_address text not null,
  to_addresses jsonb not null default '[]'::jsonb,
  cc_addresses jsonb null,
  bcc_addresses jsonb null,
  subject text null,
  body_text text null,
  sent_at timestamptz null,
  metadata_json jsonb null,
  created_at timestamptz not null default timezone('utc', now()),
  created_by_user_id uuid null references public.users(id) on delete set null,
  updated_at timestamptz not null default timezone('utc', now()),
  updated_by_user_id uuid null references public.users(id) on delete set null,
  row_version uuid not null default gen_random_uuid()
);

create unique index if not exists ux_email_messages_provider_id
  on public.email_messages (tenant_id, provider_message_id)
  where provider_message_id is not null;

create table if not exists public.outbound_emails (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete restrict,
  email_message_id uuid not null references public.email_messages(id) on delete cascade,
  provider_send_id text null,
  status text not null default 'queued' check (status in ('queued', 'sent', 'delivered', 'failed', 'bounced')),
  sent_at timestamptz null,
  failure_reason text null,
  metadata_json jsonb null,
  created_at timestamptz not null default timezone('utc', now()),
  created_by_user_id uuid null references public.users(id) on delete set null,
  updated_at timestamptz not null default timezone('utc', now()),
  updated_by_user_id uuid null references public.users(id) on delete set null,
  row_version uuid not null default gen_random_uuid()
);

create unique index if not exists ux_outbound_emails_provider_send_id
  on public.outbound_emails (tenant_id, provider_send_id)
  where provider_send_id is not null;

create table if not exists public.sms_messages (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete restrict,
  client_id uuid null references public.clients(id) on delete set null,
  provider_message_id text null,
  direction text not null check (direction in ('inbound', 'outbound')),
  to_phone text not null,
  from_phone text null,
  body_text text not null,
  status text not null default 'queued' check (status in ('queued', 'sent', 'delivered', 'failed')),
  sent_at timestamptz null,
  metadata_json jsonb null,
  created_at timestamptz not null default timezone('utc', now()),
  created_by_user_id uuid null references public.users(id) on delete set null,
  updated_at timestamptz not null default timezone('utc', now()),
  updated_by_user_id uuid null references public.users(id) on delete set null,
  row_version uuid not null default gen_random_uuid()
);

create unique index if not exists ux_sms_messages_provider_message_id
  on public.sms_messages (tenant_id, provider_message_id)
  where provider_message_id is not null;

create table if not exists public.appointment_requests (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete restrict,
  client_id uuid not null references public.clients(id) on delete cascade,
  matter_id uuid null references public.matters(id) on delete set null,
  requested_start_at timestamptz not null,
  requested_end_at timestamptz null,
  status text not null default 'pending' check (status in ('pending', 'approved', 'declined', 'cancelled')),
  notes_text text null,
  decided_at timestamptz null,
  decided_by_user_id uuid null references public.users(id) on delete set null,
  created_at timestamptz not null default timezone('utc', now()),
  created_by_user_id uuid null references public.users(id) on delete set null,
  updated_at timestamptz not null default timezone('utc', now()),
  updated_by_user_id uuid null references public.users(id) on delete set null,
  row_version uuid not null default gen_random_uuid()
);

create table if not exists public.client_transparency_profiles (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete restrict,
  matter_id uuid not null references public.matters(id) on delete cascade,
  status text not null default 'active' check (status in ('active', 'paused', 'disabled')),
  audience_json jsonb null,
  preferences_json jsonb null,
  created_at timestamptz not null default timezone('utc', now()),
  created_by_user_id uuid null references public.users(id) on delete set null,
  updated_at timestamptz not null default timezone('utc', now()),
  updated_by_user_id uuid null references public.users(id) on delete set null,
  row_version uuid not null default gen_random_uuid()
);

create unique index if not exists ux_client_transparency_profiles_matter
  on public.client_transparency_profiles (tenant_id, matter_id);

create table if not exists public.client_transparency_snapshots (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete restrict,
  matter_id uuid not null references public.matters(id) on delete cascade,
  snapshot_at timestamptz not null default timezone('utc', now()),
  snapshot_payload jsonb not null default '{}'::jsonb,
  created_at timestamptz not null default timezone('utc', now()),
  created_by_user_id uuid null references public.users(id) on delete set null
);

create unique index if not exists ux_client_transparency_snapshots_unique
  on public.client_transparency_snapshots (tenant_id, matter_id, snapshot_at);

create table if not exists public.client_transparency_timeline_items (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references public.tenants(id) on delete restrict,
  matter_id uuid not null references public.matters(id) on delete cascade,
  visibility text not null check (visibility in ('internal', 'client_visible')),
  item_type text not null,
  title text not null,
  body_text text null,
  occurred_at timestamptz not null default timezone('utc', now()),
  created_at timestamptz not null default timezone('utc', now()),
  created_by_user_id uuid null references public.users(id) on delete set null,
  updated_at timestamptz not null default timezone('utc', now()),
  updated_by_user_id uuid null references public.users(id) on delete set null,
  row_version uuid not null default gen_random_uuid()
);

drop trigger if exists trg_documents_set_updated_at on public.documents;
create trigger trg_documents_set_updated_at before update on public.documents
for each row execute function public.set_updated_at_and_row_version();

drop trigger if exists trg_document_shares_set_updated_at on public.document_shares;
create trigger trg_document_shares_set_updated_at before update on public.document_shares
for each row execute function public.set_updated_at_and_row_version();

drop trigger if exists trg_document_comments_set_updated_at on public.document_comments;
create trigger trg_document_comments_set_updated_at before update on public.document_comments
for each row execute function public.set_updated_at_and_row_version();

drop trigger if exists trg_document_content_indexes_set_updated_at on public.document_content_indexes;
create trigger trg_document_content_indexes_set_updated_at before update on public.document_content_indexes
for each row execute function public.set_updated_at_and_row_version();

drop trigger if exists trg_signature_requests_set_updated_at on public.signature_requests;
create trigger trg_signature_requests_set_updated_at before update on public.signature_requests
for each row execute function public.set_updated_at_and_row_version();

drop trigger if exists trg_client_portal_accounts_set_updated_at on public.client_portal_accounts;
create trigger trg_client_portal_accounts_set_updated_at before update on public.client_portal_accounts
for each row execute function public.set_updated_at_and_row_version();

drop trigger if exists trg_client_messages_set_updated_at on public.client_messages;
create trigger trg_client_messages_set_updated_at before update on public.client_messages
for each row execute function public.set_updated_at_and_row_version();

drop trigger if exists trg_email_messages_set_updated_at on public.email_messages;
create trigger trg_email_messages_set_updated_at before update on public.email_messages
for each row execute function public.set_updated_at_and_row_version();

drop trigger if exists trg_outbound_emails_set_updated_at on public.outbound_emails;
create trigger trg_outbound_emails_set_updated_at before update on public.outbound_emails
for each row execute function public.set_updated_at_and_row_version();

drop trigger if exists trg_sms_messages_set_updated_at on public.sms_messages;
create trigger trg_sms_messages_set_updated_at before update on public.sms_messages
for each row execute function public.set_updated_at_and_row_version();

drop trigger if exists trg_appointment_requests_set_updated_at on public.appointment_requests;
create trigger trg_appointment_requests_set_updated_at before update on public.appointment_requests
for each row execute function public.set_updated_at_and_row_version();

drop trigger if exists trg_client_transparency_profiles_set_updated_at on public.client_transparency_profiles;
create trigger trg_client_transparency_profiles_set_updated_at before update on public.client_transparency_profiles
for each row execute function public.set_updated_at_and_row_version();

drop trigger if exists trg_client_transparency_timeline_items_set_updated_at on public.client_transparency_timeline_items;
create trigger trg_client_transparency_timeline_items_set_updated_at before update on public.client_transparency_timeline_items
for each row execute function public.set_updated_at_and_row_version();
