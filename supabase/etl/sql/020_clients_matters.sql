-- Scenario A template: Clients and matters

insert into public.clients (
  id, tenant_id, client_number, normalized_email, email, display_name, kind, status,
  company_name, phone, mobile, address_line_1, city, state_region, postal_code, country_code,
  tax_id, notes_text, created_at, updated_at, row_version
)
select
  c."Id"::uuid,
  c."TenantId"::uuid,
  nullif(trim(c."ClientNumber"), ''),
  nullif(lower(trim(c."NormalizedEmail")), ''),
  nullif(trim(c."Email"), ''),
  trim(c."Name"),
  case lower(trim(c."Type")) when 'corporate' then 'organization' else 'individual' end,
  case lower(trim(c."Status")) when 'inactive' then 'inactive' else 'active' end,
  nullif(trim(c."Company"), ''),
  nullif(trim(c."Phone"), ''),
  nullif(trim(c."Mobile"), ''),
  nullif(trim(c."Address"), ''),
  nullif(trim(c."City"), ''),
  nullif(trim(c."State"), ''),
  nullif(trim(c."ZipCode"), ''),
  nullif(trim(c."Country"), ''),
  nullif(trim(c."TaxId"), ''),
  nullif(trim(c."Notes"), ''),
  coalesce(c."CreatedAt", timezone('utc', now())),
  coalesce(c."UpdatedAt", coalesce(c."CreatedAt", timezone('utc', now()))),
  gen_random_uuid()
from legacy_public."Clients" c
on conflict (id) do update
set client_number = excluded.client_number,
    normalized_email = excluded.normalized_email,
    email = excluded.email,
    display_name = excluded.display_name,
    kind = excluded.kind,
    status = excluded.status,
    updated_at = excluded.updated_at,
    row_version = gen_random_uuid();

select migration_work.register_id_map('client', c."Id", c."Id"::uuid, concat(c."TenantId", ':', lower(trim(coalesce(c."NormalizedEmail", c."Email")))))
from legacy_public."Clients" c;

insert into public.matters (
  id, tenant_id, client_id, responsible_user_id, firm_entity_id, office_id, case_number, case_number_normalized,
  display_name, practice_area, court_type, status, fee_model, billable_rate, open_date, outcome_summary, created_at, updated_at, row_version
)
select
  m."Id"::uuid,
  m."TenantId"::uuid,
  m."ClientId"::uuid,
  coalesce(resp.target_id, m."CreatedByUserId"::uuid),
  nullif(m."EntityId", '')::uuid,
  nullif(m."OfficeId", '')::uuid,
  trim(m."CaseNumber"),
  lower(trim(m."CaseNumber")),
  trim(m."Name"),
  nullif(trim(m."PracticeArea"), ''),
  nullif(trim(m."CourtType"), ''),
  case lower(trim(m."Status"))
    when 'closed' then 'closed'
    when 'archived' then 'archived'
    else 'open'
  end,
  case lower(trim(m."FeeStructure"))
    when 'hourly' then 'hourly'
    when 'fixed' then 'fixed'
    else 'hybrid'
  end,
  nullif(m."BillableRate"::text, '')::numeric(18,4),
  coalesce(m."OpenDate", timezone('utc', now())),
  nullif(trim(m."Outcome"), ''),
  timezone('utc', now()),
  timezone('utc', now()),
  gen_random_uuid()
from legacy_public."Matters" m
left join migration_work.id_map resp
  on resp.map_key = 'user'
 and resp.natural_key = concat(m."TenantId", ':', lower(trim(coalesce(m."ResponsibleAttorney", ''))))
on conflict (id) do update
set client_id = excluded.client_id,
    responsible_user_id = excluded.responsible_user_id,
    status = excluded.status,
    fee_model = excluded.fee_model,
    billable_rate = excluded.billable_rate,
    updated_at = excluded.updated_at,
    row_version = gen_random_uuid();

insert into public.matter_clients (id, tenant_id, matter_id, client_id, relationship_type, created_at, row_version)
select
  gen_random_uuid(),
  l."TenantId"::uuid,
  l."MatterId"::uuid,
  l."ClientId"::uuid,
  'co_client',
  coalesce(l."CreatedAt", timezone('utc', now())),
  gen_random_uuid()
from legacy_public."MatterClientLinks" l
on conflict (tenant_id, matter_id, client_id, relationship_type) do nothing;

insert into public.matter_notes (id, tenant_id, matter_id, title, body_text, visibility, note_type, created_at, created_by_user_id, updated_at, updated_by_user_id, row_version)
select
  n."Id"::uuid,
  n."TenantId"::uuid,
  n."MatterId"::uuid,
  nullif(trim(n."Title"), ''),
  regexp_replace(coalesce(n."Body", ''), E'\\r\\n?', E'\\n', 'g'),
  'internal',
  'general',
  coalesce(n."CreatedAt", timezone('utc', now())),
  nullif(n."CreatedByUserId", '')::uuid,
  coalesce(n."UpdatedAt", coalesce(n."CreatedAt", timezone('utc', now()))),
  nullif(n."UpdatedByUserId", '')::uuid,
  gen_random_uuid()
from legacy_public."MatterNotes" n
on conflict (id) do update
set title = excluded.title,
    body_text = excluded.body_text,
    updated_at = excluded.updated_at,
    updated_by_user_id = excluded.updated_by_user_id,
    row_version = gen_random_uuid();

insert into public.matter_note_revisions (id, tenant_id, matter_note_id, revision_number, title, body_text, visibility, note_type, revised_at, revised_by_user_id)
select
  gen_random_uuid(),
  n."TenantId"::uuid,
  n."Id"::uuid,
  1,
  nullif(trim(n."Title"), ''),
  regexp_replace(coalesce(n."Body", ''), E'\\r\\n?', E'\\n', 'g'),
  'internal',
  'general',
  coalesce(n."UpdatedAt", coalesce(n."CreatedAt", timezone('utc', now()))),
  nullif(n."UpdatedByUserId", '')::uuid
from legacy_public."MatterNotes" n
on conflict do nothing;
