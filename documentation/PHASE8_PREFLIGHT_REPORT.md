# Phase 8 Production Preflight Report

- generated-at: 2026-04-19T18:04:16
- mode: repo-only preview
- real-supabase-change: no
- real-render-change: no

## Core Checks

- healthcheck_path : ok (/health maplenmis olmali)
- canonical_bootstrap_mode : ok (Canonical production blueprint migrate kullanmali)
- canonical_seed_disabled : ok (Production blueprint seed kapali olmali)
- canonical_healthcheck_path : ok (Canonical production blueprint /health kullanmali)

## Required Repo Files

- render.canonical.production.yaml : ok
- documentation\RENDER_CANONICAL_PRODUCTION_ENV_MATRIX_TR.md : ok
- documentation\SUPABASE_RENDER_PHASE8_PRODUCTION_CUTOVER_RUNBOOK_TR.md : ok
- documentation\PHASE8_PRODUCTION_CUTOVER_CHECKLIST_TR.md : ok
- documentation\PHASE8_ROLLBACK_CHECKLIST_TR.md : ok
- documentation\PHASE7_PREFLIGHT_REPORT.md : ok
- documentation\PHASE7_STAGING_SMOKE_PREVIEW.md : ok
- scripts\render-phase8-production-smoke.ps1 : ok
- scripts\supabase-db-dump.ps1 : ok
- scripts\supabase-db-restore.ps1 : ok
- scripts\supabase-storage-backup.ps1 : ok
- scripts\supabase-storage-restore.ps1 : ok

## Env Matrix Coverage

- expected-env-key-count: 13
- missing-env-key-count: 0
- result: ok

## Verdict

- phase8-preflight: ready-for-future-cutover
