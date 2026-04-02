-- JurisFlow | Supabase SQL Editor template
-- Purpose:
-- 1) Create/update tenant (firm) by slug
-- 2) Create/update an attorney user inside that tenant
--
-- Important:
-- - This writes to JurisFlow app tables ("Tenants", "Users"), not Supabase Auth users.
-- - Script is idempotent: safe to rerun for the same tenant/email.
-- - Change only the values in the "INPUT" section below.

BEGIN;

CREATE EXTENSION IF NOT EXISTS pgcrypto;

DO $$
DECLARE
    -- ==========================
    -- INPUT (edit these values)
    -- ==========================
    v_tenant_slug          text := lower(trim('ahmet-hukuk'));
    v_tenant_name          text := trim('Ahmet Hukuk');
    v_user_email_raw       text := trim('ahmet@mail.com');
    v_user_name            text := trim('Ahmet');
    v_user_role            text := trim('Attorney');
    v_user_password_plain  text := trim('ChangeMe123!');
    -- ==========================

    v_user_email_normalized text;
    v_now                   timestamp without time zone := timezone('utc', now());
    v_tenant_id             text;
    v_user_id               text;
BEGIN
    v_user_email_normalized := lower(trim(v_user_email_raw));

    IF v_tenant_slug = '' THEN
        RAISE EXCEPTION 'tenant slug is required';
    END IF;

    IF v_tenant_name = '' THEN
        v_tenant_name := initcap(replace(v_tenant_slug, '-', ' '));
    END IF;

    IF v_user_email_normalized = '' THEN
        RAISE EXCEPTION 'user email is required';
    END IF;

    IF v_user_name = '' THEN
        v_user_name := split_part(v_user_email_raw, '@', 1);
    END IF;

    IF v_user_password_plain = '' THEN
        RAISE EXCEPTION 'user password is required';
    END IF;

    IF v_user_role NOT IN ('Admin', 'SecurityAdmin', 'Partner', 'Associate', 'Employee', 'Attorney', 'Staff', 'Manager') THEN
        RAISE EXCEPTION 'unsupported role: %', v_user_role;
    END IF;

    INSERT INTO "Tenants" ("Id", "Name", "Slug", "IsActive", "CreatedAt", "UpdatedAt")
    VALUES (gen_random_uuid()::text, v_tenant_name, v_tenant_slug, true, v_now, v_now)
    ON CONFLICT ("Slug")
    DO UPDATE
    SET
        "Name" = EXCLUDED."Name",
        "IsActive" = true,
        "UpdatedAt" = EXCLUDED."UpdatedAt"
    RETURNING "Id" INTO v_tenant_id;

    INSERT INTO "Users" (
        "Id",
        "Email",
        "NormalizedEmail",
        "Name",
        "Role",
        "PasswordHash",
        "CreatedAt",
        "UpdatedAt",
        "TenantId"
    )
    VALUES (
        gen_random_uuid()::text,
        v_user_email_raw,
        v_user_email_normalized,
        v_user_name,
        v_user_role,
        crypt(v_user_password_plain, gen_salt('bf', 12)),
        v_now,
        v_now,
        v_tenant_id
    )
    ON CONFLICT ("TenantId", "NormalizedEmail")
    DO UPDATE
    SET
        "Email" = EXCLUDED."Email",
        "Name" = EXCLUDED."Name",
        "Role" = EXCLUDED."Role",
        "PasswordHash" = EXCLUDED."PasswordHash",
        "UpdatedAt" = EXCLUDED."UpdatedAt"
    RETURNING "Id" INTO v_user_id;

    RAISE NOTICE 'Tenant provisioned: slug=% id=%', v_tenant_slug, v_tenant_id;
    RAISE NOTICE 'User provisioned: email=% id=% role=%', v_user_email_normalized, v_user_id, v_user_role;
END
$$;

-- Quick verification
SELECT
    t."Id"             AS tenant_id,
    t."Slug"           AS tenant_slug,
    t."Name"           AS tenant_name,
    u."Id"             AS user_id,
    u."Email"          AS user_email,
    u."Role"           AS user_role,
    u."CreatedAt"      AS user_created_at,
    u."UpdatedAt"      AS user_updated_at
FROM "Users" u
JOIN "Tenants" t ON t."Id" = u."TenantId"
WHERE t."Slug" = lower(trim('ahmet-hukuk'))
  AND u."NormalizedEmail" = lower(trim('ahmet@mail.com'));

COMMIT;
