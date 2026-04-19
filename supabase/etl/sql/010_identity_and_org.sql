-- Scenario A template: Identity and organization
-- Source schema assumed: legacy_public

insert into public.tenants (id, slug, name, status, created_at, updated_at, row_version)
select
  l."Id"::uuid,
  lower(trim(l."Slug")),
  trim(l."Name"),
  case lower(coalesce(l."Status", 'active'))
    when 'active' then 'active'
    when 'suspended' then 'suspended'
    else 'disabled'
  end,
  coalesce(l."CreatedAt", timezone('utc', now())),
  coalesce(l."UpdatedAt", coalesce(l."CreatedAt", timezone('utc', now()))),
  gen_random_uuid()
from legacy_public."Tenants" l
on conflict (id) do update
set slug = excluded.slug,
    name = excluded.name,
    status = excluded.status,
    updated_at = excluded.updated_at,
    row_version = gen_random_uuid();

select migration_work.register_id_map('tenant', l."Id", l."Id"::uuid, lower(trim(l."Slug")))
from legacy_public."Tenants" l;

insert into public.users (id, tenant_id, normalized_email, email, display_name, status, password_hash, phone, preferences_json, notification_preferences_json, mfa_enabled, created_at, updated_at, row_version)
select
  u."Id"::uuid,
  u."TenantId"::uuid,
  lower(trim(u."NormalizedEmail")),
  trim(u."Email"),
  trim(u."Name"),
  case lower(coalesce(u."Status", 'active'))
    when 'active' then 'active'
    when 'invited' then 'invited'
    else 'disabled'
  end,
  u."PasswordHash",
  nullif(trim(u."Phone"), ''),
  case when nullif(trim(u."Preferences"), '') is null then null else u."Preferences"::jsonb end,
  case when nullif(trim(u."NotificationPreferences"), '') is null then null else u."NotificationPreferences"::jsonb end,
  coalesce(u."MfaEnabled", false),
  coalesce(u."CreatedAt", timezone('utc', now())),
  coalesce(u."UpdatedAt", coalesce(u."CreatedAt", timezone('utc', now()))),
  gen_random_uuid()
from legacy_public."Users" u
on conflict (id) do update
set normalized_email = excluded.normalized_email,
    email = excluded.email,
    display_name = excluded.display_name,
    status = excluded.status,
    phone = excluded.phone,
    preferences_json = excluded.preferences_json,
    notification_preferences_json = excluded.notification_preferences_json,
    mfa_enabled = excluded.mfa_enabled,
    updated_at = excluded.updated_at,
    row_version = gen_random_uuid();

select migration_work.register_id_map('user', u."Id", u."Id"::uuid, concat(u."TenantId", ':', lower(trim(u."NormalizedEmail"))))
from legacy_public."Users" u;

insert into public.staff_profiles (id, tenant_id, user_id, bar_number, job_title, employment_status, created_at, updated_at, row_version)
select
  gen_random_uuid(),
  u."TenantId"::uuid,
  u."Id"::uuid,
  nullif(trim(u."BarNumber"), ''),
  nullif(trim(u."EmployeeRole"), ''),
  'active',
  coalesce(u."CreatedAt", timezone('utc', now())),
  coalesce(u."UpdatedAt", coalesce(u."CreatedAt", timezone('utc', now()))),
  gen_random_uuid()
from legacy_public."Users" u
where nullif(trim(u."EmployeeRole"), '') is not null
on conflict do nothing;

-- Role split intentionally references system roles seeded by Phase 5 seed.sql.
insert into public.user_role_assignments (id, user_id, role_id, created_at)
select
  gen_random_uuid(),
  u."Id"::uuid,
  rd.id,
  timezone('utc', now())
from legacy_public."Users" u
join public.role_definitions rd
  on rd.tenant_id is null
 and rd.key = case lower(trim(u."Role"))
    when 'admin' then 'admin'
    when 'partner' then 'partner'
    when 'manager' then 'manager'
    when 'associate' then 'associate'
    else 'paralegal'
  end
on conflict do nothing;
