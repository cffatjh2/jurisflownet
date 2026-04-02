-- JurisFlow | Supabase SQL Editor template
-- Purpose:
-- 1) Resolve tenant by firm code (slug)
-- 2) Create/update client portal account safely
-- 3) Keep client Company synced to tenant (firm) name
--
-- Login requirements for /api/client/login:
-- - Clients.NormalizedEmail must match lower(email)
-- - Clients.Status must be 'Active'
-- - Clients.PortalEnabled must be true
-- - Clients.PasswordHash must be a BCrypt hash

BEGIN;

CREATE EXTENSION IF NOT EXISTS pgcrypto;

DO $$
DECLARE
    -- ==========================
    -- INPUT (BURAYI DOLDUR)
    -- ==========================
    v_tenant_slug          text := lower(trim('FIRM_CODE_GIR'));          -- örn: juris-flow
    v_client_email_raw     text := trim('MUV_EKKIL_EMAIL_GIR');           -- örn: mahmut@mail.com
    v_client_name          text := trim('MUV_EKKIL_ADI_GIR');             -- örn: Mahmut Demir
    v_client_type          text := trim('Individual');                    -- Individual | Corporate
    v_client_password      text := trim('MUV_EKKIL_SIFRE_GIR');           -- örn: ChangeMe123!

    -- Opsiyonel iletişim bilgileri
    v_client_phone         text := NULL;                                  -- örn: '+90...'
    v_client_mobile        text := NULL;                                  -- örn: '+90...'
    -- ==========================

    v_now                   timestamp without time zone := timezone('utc', now());
    v_tenant_id             text;
    v_tenant_name           text;
    v_client_email_norm     text;
BEGIN
    v_client_email_norm := lower(trim(v_client_email_raw));

    IF v_tenant_slug = '' THEN
        RAISE EXCEPTION 'tenant slug (firm code) is required';
    END IF;

    IF v_client_email_norm = '' THEN
        RAISE EXCEPTION 'client email is required';
    END IF;

    IF v_client_name = '' THEN
        v_client_name := split_part(v_client_email_raw, '@', 1);
    END IF;

    IF v_client_password = '' THEN
        RAISE EXCEPTION 'client password is required';
    END IF;

    IF v_client_type NOT IN ('Individual', 'Corporate') THEN
        RAISE EXCEPTION 'unsupported client type: %', v_client_type;
    END IF;

    SELECT t."Id", t."Name"
    INTO v_tenant_id, v_tenant_name
    FROM "Tenants" t
    WHERE t."Slug" = v_tenant_slug
    LIMIT 1;

    IF v_tenant_id IS NULL THEN
        RAISE EXCEPTION 'tenant not found for slug: %', v_tenant_slug;
    END IF;

    INSERT INTO "Clients" (
        "Id",
        "Name",
        "Email",
        "NormalizedEmail",
        "Phone",
        "Mobile",
        "Company",
        "Type",
        "Status",
        "PasswordHash",
        "PortalEnabled",
        "CreatedAt",
        "UpdatedAt",
        "TenantId"
    )
    VALUES (
        gen_random_uuid()::text,
        v_client_name,
        v_client_email_raw,
        v_client_email_norm,
        v_client_phone,
        v_client_mobile,
        v_tenant_name,
        v_client_type,
        'Active',
        crypt(v_client_password, gen_salt('bf', 12)),
        true,
        v_now,
        v_now,
        v_tenant_id
    )
    ON CONFLICT ("TenantId", "NormalizedEmail")
    DO UPDATE
    SET
        "Name" = EXCLUDED."Name",
        "Email" = EXCLUDED."Email",
        "Phone" = EXCLUDED."Phone",
        "Mobile" = EXCLUDED."Mobile",
        "Company" = v_tenant_name,
        "Type" = EXCLUDED."Type",
        "Status" = 'Active',
        "PasswordHash" = EXCLUDED."PasswordHash",
        "PortalEnabled" = true,
        "UpdatedAt" = EXCLUDED."UpdatedAt";

    RAISE NOTICE 'Client portal account ready: tenant=% email=%', v_tenant_slug, v_client_email_norm;
END
$$;

-- Quick verification
SELECT
    t."Slug"          AS tenant_slug,
    t."Name"          AS tenant_name,
    c."Id"            AS client_id,
    c."Name"          AS client_name,
    c."Email"         AS client_email,
    c."Company"       AS client_company,
    c."Type"          AS client_type,
    c."Status"        AS client_status,
    c."PortalEnabled" AS portal_enabled,
    c."UpdatedAt"     AS updated_at
FROM "Clients" c
JOIN "Tenants" t ON t."Id" = c."TenantId"
WHERE t."Slug" = lower(trim('FIRM_CODE_GIR'))
  AND c."NormalizedEmail" = lower(trim('MUV_EKKIL_EMAIL_GIR'));

COMMIT;
