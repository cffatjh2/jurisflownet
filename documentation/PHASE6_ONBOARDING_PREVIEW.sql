-- Scenario B template
-- Replace {{...}} placeholders before any future execution.

insert into public.tenants (id, slug, name, status, created_at, updated_at, row_version)
values (
  '4608cd2d-eeaa-424d-bd09-fbd30ddd1ff4'::uuid,
  'demo',
  'Demo Legal',
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
  'bac841bd-87f1-4a89-b7a2-1a832e241abe'::uuid,
  '4608cd2d-eeaa-424d-bd09-fbd30ddd1ff4'::uuid,
  'main',
  'Demo Legal',
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
  '61a9fe94-b1f3-47fd-a7fd-0bd028c8d7de'::uuid,
  '4608cd2d-eeaa-424d-bd09-fbd30ddd1ff4'::uuid,
  'bac841bd-87f1-4a89-b7a2-1a832e241abe'::uuid,
  'hq',
  'Demo Legal HQ',
  'Europe/Istanbul',
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
  'a139c9b9-6ecf-4172-9e0c-818f586d2f55'::uuid,
  '4608cd2d-eeaa-424d-bd09-fbd30ddd1ff4'::uuid,
  lower('admin@example.com'),
  'admin@example.com',
  'Founding Admin',
  'active',
  '<replace-with-hash>',
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
  '8f8482c7-3391-4dfa-8ec3-42fd05d78dec'::uuid,
  '4608cd2d-eeaa-424d-bd09-fbd30ddd1ff4'::uuid,
  'a139c9b9-6ecf-4172-9e0c-818f586d2f55'::uuid,
  'Founding Admin',
  '61a9fe94-b1f3-47fd-a7fd-0bd028c8d7de'::uuid,
  'bac841bd-87f1-4a89-b7a2-1a832e241abe'::uuid,
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
  'a139c9b9-6ecf-4172-9e0c-818f586d2f55'::uuid,
  rd.id,
  timezone('utc', now())
from public.role_definitions rd
where rd.tenant_id is null
  and rd.key = 'admin'
on conflict do nothing;

