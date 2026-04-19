# Faz 6 Runbook

Bu runbook, veri tasima veya minimal seed acilisi icin gereken repo artefaktlarini tanimlar.

Onemli:
- bu fazda gercek legacy DB extract calistirilmaz
- bu fazda gercek canonical DB import calistirilmaz
- bu fazin ciktisi script, mapping, transform ve verification setidir

## Repo Kaynaklari

- etl workspace: [supabase/etl](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/supabase/etl>)
- scenario preview scripti: [supabase-phase6-preview.ps1](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/scripts/supabase-phase6-preview.ps1:1>)
- onboarding preview scripti: [supabase-phase6-onboarding-preview.ps1](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/scripts/supabase-phase6-onboarding-preview.ps1:1>)

## Senaryo A

Amac:
- legacy -> canonical veri tasima mantigini sabitlemek

Artefaktlar:
- `manifest.phase6.json`
- `transforms.phase6.json`
- `id-maps.phase6.json`
- `verification.phase6.json`
- `sql/001_phase6_bootstrap.sql`
- `sql/010` .. `070` dalga scriptleri
- `sql/090_verification.sql`

Dry-run / preview:
```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\supabase-phase6-preview.ps1 -Scenario A
```

Beklenen cikti:
- [`PHASE6_PREVIEW_A.md`](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/documentation/PHASE6_PREVIEW_A.md:1>) benzeri bir rapor
- wave bazli script coverage
- transform rule sayisi
- id/fk map coverage
- verification profilleri

## Senaryo B

Amac:
- veri tasimadan minimum tenant/admin/lookup ile canonical acilis paketi hazirlamak

Template:
- [100_minimal_seed_and_onboarding.template.sql](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/supabase/etl/sql/100_minimal_seed_and_onboarding.template.sql:1>)

Preview uretimi:
```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\supabase-phase6-onboarding-preview.ps1 -TenantSlug demo -TenantName "Demo Legal" -AdminEmail admin@example.com
```

Beklenen cikti:
- [`PHASE6_ONBOARDING_PREVIEW.sql`](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/documentation/PHASE6_ONBOARDING_PREVIEW.sql:1>) benzeri review dosyasi

## Verification Seti

Verification mantigi uc katmandir:
1. row-count karsilastirma
2. orphan FK kontrolleri
3. sample business verification

Ana dosyalar:
- [verification.phase6.json](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/supabase/etl/verification.phase6.json:1>)
- [090_verification.sql](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/supabase/etl/sql/090_verification.sql:1>)

## Faz 6 Cikis Kriteri Karsiligi

Bu repo durumu ile:
1. yeni DB'yi veriyle veya seed ile ayaga kaldiran net mekanizma vardir
2. mekanizma idempotent SQL template + preview script + verification seti olarak repoda vardir
3. hicbir gercek DB islemine dokunulmadan ilerlenmistir
