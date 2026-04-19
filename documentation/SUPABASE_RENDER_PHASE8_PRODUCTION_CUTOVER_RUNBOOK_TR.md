# Faz 8 Runbook

Bu runbook, canonical production cutover icin gereken repo-side hazirlik paketini tanimlar.

Onemli:
- bu asamada gercek production cutover yapilmaz
- bu asamada gercek Supabase production DB degisikligi yapilmaz
- bu asamada Render production env yeni DB'ye cevrilmez
- bu fazin ciktisi freeze, backup, smoke ve rollback paketidir

## Repo Kaynaklari

- future blueprint: [render.canonical.production.yaml](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/render.canonical.production.yaml:1>)
- env matrisi: [RENDER_CANONICAL_PRODUCTION_ENV_MATRIX_TR.md](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/documentation/RENDER_CANONICAL_PRODUCTION_ENV_MATRIX_TR.md:1>)
- preflight scripti: [render-phase8-production-preflight.ps1](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/scripts/render-phase8-production-preflight.ps1:1>)
- smoke matrix: [PHASE8_PRODUCTION_SMOKE_MATRIX_TR.md](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/documentation/PHASE8_PRODUCTION_SMOKE_MATRIX_TR.md:1>)
- smoke runner: [render-phase8-production-smoke.ps1](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/scripts/render-phase8-production-smoke.ps1:1>)
- cutover checklist: [PHASE8_PRODUCTION_CUTOVER_CHECKLIST_TR.md](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/documentation/PHASE8_PRODUCTION_CUTOVER_CHECKLIST_TR.md:1>)
- rollback checklist: [PHASE8_ROLLBACK_CHECKLIST_TR.md](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/documentation/PHASE8_ROLLBACK_CHECKLIST_TR.md:1>)

## Preflight

Repo-side preflight:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\render-phase8-production-preflight.ps1
```

Beklenen cikti:
- [PHASE8_PREFLIGHT_REPORT.md](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/documentation/PHASE8_PREFLIGHT_REPORT.md:1>)
- backup/restore artefakt coverage
- phase 7 readiness dependency coverage
- env matrix coverage

## Smoke Preview

Repo-side preview:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\render-phase8-production-smoke.ps1
```

Beklenen cikti:
- [PHASE8_PRODUCTION_SMOKE_PREVIEW.md](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/documentation/PHASE8_PRODUCTION_SMOKE_PREVIEW.md:1>)

## Gelecekteki Gercek Aktivasyon Sirasi

Bu kisim sadece cutover gunu icindir:

1. prod freeze penceresini ac
2. son logical dump al
3. son storage backup al
4. gerekiyorsa final ETL/import akisini sec
5. Render production env'lerini canonical production degerlerine cevir
6. deploy et
7. production smoke runner'i `-Execute` ile calistir
8. blocker varsa rollback checklist'e gec
9. eski DB'yi rollback penceresi boyunca koru

## Execute Ornegi

Bu komut bu asamada calistirilmadi. Gelecekte canonical production cutover gununde kullanilacak:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\render-phase8-production-smoke.ps1 `
  -Execute `
  -BaseUrl "https://jurisflow-prod.onrender.com" `
  -TenantSlug "prod" `
  -StaffEmail "ops@example.com" `
  -StaffPassword "<secret>" `
  -ClientEmail "client-smoke@example.com" `
  -ClientPassword "<secret>"
```

## Faz 8 Cikis Kriteri Karsiligi

Bu repo durumu ile:
1. production cutover icin future blueprint vardir
2. freeze ve rollback checklistleri vardir
3. preflight ve smoke paketleri vardir
4. gercek DB ve Render degisikligi kullanici onayi gelene kadar pasif tutulur
