# Phase 6 ETL Workspace

Bu klasor Faz 6 ciktisidir.

Amac:
- legacy schema -> canonical schema veri tasima mantigini repo seviyesinde sabitlemek
- gercek extract/import calistirmadan once script, mapping, transform ve verification setini hazir etmek

Kurallar:
- buradaki SQL dosyalari legacy veya canonical DB'ye bu asamada uygulanmaz
- `supabase/etl/sql` altindaki dosyalar idempotent migration/seed tasarim setidir
- `manifest.phase6.json`, `transforms.phase6.json`, `id-maps.phase6.json`, `verification.phase6.json` birlikte Phase 6 truth setidir

Senaryolar:
- `A`: legacy verisi canonical DB'ye tasinacak
- `B`: veri tasinmayacak veya minimum seed ile sifirdan acilis yapilacak

Temel komutlar:
- `powershell -ExecutionPolicy Bypass -File .\scripts\supabase-phase6-preview.ps1 -Scenario A`
- `powershell -ExecutionPolicy Bypass -File .\scripts\supabase-phase6-preview.ps1 -Scenario B`
- `powershell -ExecutionPolicy Bypass -File .\scripts\supabase-phase6-onboarding-preview.ps1 -TenantSlug demo -TenantName "Demo Legal" -AdminEmail admin@example.com`
