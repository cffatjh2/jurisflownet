-- Scenario B template
-- Replace {{...}} placeholders before any future execution.

insert into public.tenants (id, slug, name, status, created_at, updated_at, row_version)
values (
  '{{TENANT_ID}}'::uuid,
  '{{TENANT_SLUG}}',
  '{{TENANT_NAME}}',
  'active',
  timezone('utc', now()),
  timezone('utc', now()),
  gen_random_uuid()
)
on conflict (id) do update
set slug = excluded.slug,
    name = excluded.name,
    status = excluded.status,
    updated_at = timezone('utc', now()),
    row_version = gen_random_uuid();

insert into public.firm_entities (
  id, tenant_id, code, name, status, created_at, updated_at, row_version
)
values (
  '{{FIRM_ENTITY_ID}}'::uuid,
  '{{TENANT_ID}}'::uuid,
  'main',
  '{{TENANT_NAME}}',
  'active',
  timezone('utc', now()),
  timezone('utc', now()),
  gen_random_uuid()
)
on conflict (id) do update
set name = excluded.name,
    updated_at = timezone('utc', now()),
    row_version = gen_random_uuid();

insert into public.offices (
  id, tenant_id, firm_entity_id, code, name, timezone, status, created_at, updated_at, row_version
)
values (
  '{{OFFICE_ID}}'::uuid,
  '{{TENANT_ID}}'::uuid,
  '{{FIRM_ENTITY_ID}}'::uuid,
  'hq',
  '{{TENANT_NAME}} HQ',
  '{{TIMEZONE}}',
  'active',
  timezone('utc', now()),
  timezone('utc', now()),
  gen_random_uuid()
)
on conflict (id) do update
set name = excluded.name,
    timezone = excluded.timezone,
    updated_at = timezone('utc', now()),
    row_version = gen_random_uuid();

insert into public.users (
  id, tenant_id, normalized_email, email, display_name, status, password_hash, created_at, updated_at, row_version
)
values (
  '{{ADMIN_USER_ID}}'::uuid,
  '{{TENANT_ID}}'::uuid,
  lower('{{ADMIN_EMAIL}}'),
  '{{ADMIN_EMAIL}}',
  '{{ADMIN_NAME}}',
  'active',
  '{{ADMIN_PASSWORD_HASH}}',
  timezone('utc', now()),
  timezone('utc', now()),
  gen_random_uuid()
)
on conflict (id) do update
set normalized_email = excluded.normalized_email,
    email = excluded.email,
    display_name = excluded.display_name,
    password_hash = excluded.password_hash,
    updated_at = timezone('utc', now()),
    row_version = gen_random_uuid();

insert into public.staff_profiles (
  id, tenant_id, user_id, job_title, office_id, firm_entity_id, employment_status, created_at, updated_at, row_version
)
values (
  '{{STAFF_PROFILE_ID}}'::uuid,
  '{{TENANT_ID}}'::uuid,
  '{{ADMIN_USER_ID}}'::uuid,
  'Founding Admin',
  '{{OFFICE_ID}}'::uuid,
  '{{FIRM_ENTITY_ID}}'::uuid,
  'active',
  timezone('utc', now()),
  timezone('utc', now()),
  gen_random_uuid()
)
on conflict (id) do update
set updated_at = timezone('utc', now()),
    row_version = gen_random_uuid();

insert into public.user_role_assignments (id, user_id, role_id, created_at)
select
  gen_random_uuid(),
  '{{ADMIN_USER_ID}}'::uuid,
  rd.id,
  timezone('utc', now())
from public.role_definitions rd
where rd.tenant_id is null
  and rd.key = 'admin'
on conflict do nothing;
