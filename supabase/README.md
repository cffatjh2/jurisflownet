# Supabase Fresh Build Workspace

This directory is the source of truth for the canonical Supabase/Postgres schema that will be deployed to a brand-new staging database first, then used for cutover later.

Rules:
- Do not apply these migrations to the legacy production database.
- Do not provision a new remote Supabase database from this directory until the user explicitly starts staging cutover work.
- Treat `supabase/migrations` as the canonical blank-database install set.
- Validate locally with `supabase db reset` before any remote push.
- Validate remote staging with `supabase db push --dry-run` before any real `db push`.
- Use `seed.sql` only for reference/bootstrap data that is safe on an empty canonical database.

Primary commands:
- `powershell -ExecutionPolicy Bypass -File .\scripts\supabase-phase5-validate.ps1 -EnvironmentName local`
- `powershell -ExecutionPolicy Bypass -File .\scripts\supabase-phase5-validate.ps1 -EnvironmentName staging`
- `powershell -ExecutionPolicy Bypass -File .\scripts\supabase-phase5-validate.ps1 -EnvironmentName staging -Apply`

Operational runbooks:
- `documentation/SUPABASE_PHASE5_FRESH_BUILD_RUNBOOK_TR.md`
- `documentation/RENDER_CANONICAL_STAGING_ENV_MATRIX_TR.md`

Phase 6 ETL workspace:
- `supabase/etl/README.md`
- `documentation/SUPABASE_PHASE6_DATA_MIGRATION_RUNBOOK_TR.md`

Phase 7 preflight and smoke:
- `powershell -ExecutionPolicy Bypass -File .\scripts\render-phase7-canonical-preflight.ps1`
- `powershell -ExecutionPolicy Bypass -File .\scripts\render-phase7-staging-smoke.ps1`
- `documentation/SUPABASE_RENDER_PHASE7_STAGING_CUTOVER_RUNBOOK_TR.md`

Phase 8 production cutover prep:
- `powershell -ExecutionPolicy Bypass -File .\scripts\render-phase8-production-preflight.ps1`
- `powershell -ExecutionPolicy Bypass -File .\scripts\render-phase8-production-smoke.ps1`
- `documentation/SUPABASE_RENDER_PHASE8_PRODUCTION_CUTOVER_RUNBOOK_TR.md`
