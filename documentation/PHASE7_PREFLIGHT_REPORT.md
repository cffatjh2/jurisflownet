# Phase 7 Canonical Staging Preflight Report

- generated-at: 2026-04-19T17:54:55
- mode: repo-only preview
- real-supabase-change: no
- real-render-change: no

## Core Checks

- healthcheck_path : ok (/health maplenmis olmali)
- tenant_middleware : ok (TenantResolutionMiddleware aktif olmali)
- canonical_bootstrap_mode : ok (Canonical staging blueprint migrate kullanmali)
- canonical_healthcheck_path : ok (Canonical staging blueprint /health kullanmali)
- smoke_manifest_present : ok (Smoke manifest yeterli coverage icermeli)

## Required Repo Files

- render.canonical.staging.yaml : ok
- documentation\RENDER_CANONICAL_STAGING_ENV_MATRIX_TR.md : ok
- documentation\SUPABASE_PHASE5_FRESH_BUILD_RUNBOOK_TR.md : ok
- documentation\SUPABASE_PHASE6_DATA_MIGRATION_RUNBOOK_TR.md : ok
- documentation\PHASE7_STAGING_SMOKE_MANIFEST.json : ok
- supabase\seed.sql : ok
- scripts\supabase-phase5-validate.ps1 : ok
- scripts\supabase-phase6-preview.ps1 : ok
- scripts\supabase-phase6-onboarding-preview.ps1 : ok
- scripts\render-phase7-staging-smoke.ps1 : ok

## Canonical Migration Set

- supabase\migrations\20260419090000_001_foundation_tenants_identity.sql : ok
- supabase\migrations\20260419091000_010_clients_matters_core.sql : ok
- supabase\migrations\20260419092000_020_workflow_tasks_calendar.sql : ok
- supabase\migrations\20260419093000_030_trust_accounting_core.sql : ok
- supabase\migrations\20260419094000_031_trust_governance.sql : ok
- supabase\migrations\20260419095000_040_billing_payments.sql : ok
- supabase\migrations\20260419100000_050_documents_signatures_portal.sql : ok
- supabase\migrations\20260419101000_060_integrations.sql : ok
- supabase\migrations\20260419102000_070_audit_retention_views.sql : ok

## Phase 6 ETL/Seed Dependencies

- supabase\etl\manifest.phase6.json : ok
- supabase\etl\transforms.phase6.json : ok
- supabase\etl\id-maps.phase6.json : ok
- supabase\etl\verification.phase6.json : ok
- supabase\etl\sql\001_phase6_bootstrap.sql : ok
- supabase\etl\sql\090_verification.sql : ok
- supabase\etl\sql\100_minimal_seed_and_onboarding.template.sql : ok

## Env Matrix Coverage

- expected-env-key-count: 15
- missing-env-key-count: 0
- result: ok

## Smoke Coverage

- health : GET /health [none]
- staff_login : POST /api/login [none]
- matters_list : GET /api/Matters?page=1&pageSize=10 [staff]
- matter_notes_list : GET /api/matters/{matterId}/notes?page=1&pageSize=10 [staff]
- trust_accounts : GET /api/Trust/accounts?limit=5 [staff]
- invoices_list : GET /api/Invoices [staff]
- documents_list : GET /api/Documents [staff]
- integrations_contract : GET /api/integrations/ops/contract [staff]
- integrations_capability_matrix : GET /api/integrations/ops/capability-matrix [staff]
- client_login : POST /api/client/login [none]
- client_profile : GET /api/client/profile [client]
- client_matters : GET /api/client/matters [client]
- client_invoices : GET /api/client/invoices [client]
- client_documents : GET /api/client/documents [client]

## Verdict

- phase7-preflight: ready-for-future-cutover
