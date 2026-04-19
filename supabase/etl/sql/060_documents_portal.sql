-- Scenario A template: Documents / portal / communications

insert into public.documents (
  id, tenant_id, matter_id, uploaded_by_user_id, name, category, status, current_version_number,
  description_text, created_at, updated_at, row_version
)
select
  d."Id"::uuid,
  d."TenantId"::uuid,
  nullif(d."MatterId", '')::uuid,
  nullif(d."UploadedBy", '')::uuid,
  trim(d."Name"),
  nullif(trim(d."Category"), ''),
  case lower(trim(coalesce(d."Status", 'draft')))
    when 'active' then 'active'
    when 'archived' then 'archived'
    when 'legalhold' then 'legal_hold'
    else 'draft'
  end,
  greatest(coalesce(d."Version", 1), 1),
  nullif(trim(d."Description"), ''),
  coalesce(d."CreatedAt", timezone('utc', now())),
  coalesce(d."UpdatedAt", coalesce(d."CreatedAt", timezone('utc', now()))),
  gen_random_uuid()
from legacy_public."Documents" d
on conflict (id) do update
set status = excluded.status,
    current_version_number = excluded.current_version_number,
    updated_at = excluded.updated_at,
    row_version = gen_random_uuid();

insert into public.document_versions (
  id, tenant_id, document_id, version_number, file_name, storage_object_key, mime_type, file_size_bytes, created_at, created_by_user_id
)
select
  gen_random_uuid(),
  d."TenantId"::uuid,
  d."Id"::uuid,
  greatest(coalesce(d."Version", 1), 1),
  trim(d."FileName"),
  replace(trim(d."FilePath"), '\', '/'),
  coalesce(nullif(trim(d."MimeType"), ''), 'application/octet-stream'),
  coalesce(d."FileSize", 0),
  coalesce(d."CreatedAt", timezone('utc', now())),
  nullif(d."UploadedBy", '')::uuid
from legacy_public."Documents" d
on conflict (document_id, version_number) do update
set file_name = excluded.file_name,
    storage_object_key = excluded.storage_object_key,
    mime_type = excluded.mime_type,
    file_size_bytes = excluded.file_size_bytes;

insert into public.client_portal_accounts (
  id, tenant_id, client_id, normalized_email, email, password_hash, status, portal_enabled,
  last_login_at, created_at, updated_at, row_version
)
select
  gen_random_uuid(),
  c."TenantId"::uuid,
  c."Id"::uuid,
  lower(trim(coalesce(c."NormalizedEmail", c."Email"))),
  trim(c."Email"),
  c."PasswordHash",
  case when coalesce(c."PortalEnabled", false) then 'active' else 'pending' end,
  coalesce(c."PortalEnabled", false),
  c."LastLogin",
  coalesce(c."CreatedAt", timezone('utc', now())),
  coalesce(c."UpdatedAt", coalesce(c."CreatedAt", timezone('utc', now()))),
  gen_random_uuid()
from legacy_public."Clients" c
where nullif(trim(coalesce(c."Email", '')), '') is not null
on conflict (tenant_id, client_id) do update
set normalized_email = excluded.normalized_email,
    email = excluded.email,
    password_hash = excluded.password_hash,
    status = excluded.status,
    portal_enabled = excluded.portal_enabled,
    last_login_at = excluded.last_login_at,
    updated_at = excluded.updated_at,
    row_version = gen_random_uuid();
