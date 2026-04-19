# Faz 7 Runbook

Bu runbook, canonical staging cutover icin gereken repo-side hazirlik paketini tanimlar.

Onemli:
- bu asamada gercek Supabase staging DB kurulmaz
- bu asamada `db push` calistirilmaz
- bu asamada Render env'leri yeni DB'ye cevrilmez
- bu fazin ciktisi cutover blueprint, preflight ve smoke paketidir

## Repo Kaynaklari

- future blueprint: [render.canonical.staging.yaml](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/render.canonical.staging.yaml:1>)
- env matrisi: [RENDER_CANONICAL_STAGING_ENV_MATRIX_TR.md](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/documentation/RENDER_CANONICAL_STAGING_ENV_MATRIX_TR.md:1>)
- preflight scripti: [render-phase7-canonical-preflight.ps1](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/scripts/render-phase7-canonical-preflight.ps1:1>)
- smoke matrix: [PHASE7_STAGING_SMOKE_MATRIX_TR.md](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/documentation/PHASE7_STAGING_SMOKE_MATRIX_TR.md:1>)
- smoke runner: [render-phase7-staging-smoke.ps1](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/scripts/render-phase7-staging-smoke.ps1:1>)

## Preflight

Repo-side preflight:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\render-phase7-canonical-preflight.ps1
```

Beklenen cikti:
- [PHASE7_PREFLIGHT_REPORT.md](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/documentation/PHASE7_PREFLIGHT_REPORT.md:1>)
- canonical migration set coverage
- ETL/seed dependency coverage
- env matrix coverage
- smoke manifest coverage

## Smoke Preview

Repo-side preview:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\render-phase7-staging-smoke.ps1
```

Beklenen cikti:
- [PHASE7_STAGING_SMOKE_PREVIEW.md](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/documentation/PHASE7_STAGING_SMOKE_PREVIEW.md:1>)

## Gelecekteki Gercek Aktivasyon Sirasi

Bu kisim sadece sonraki cutover gunu icindir:

1. canonical Supabase staging ortamini olustur
2. Faz 5 migration setini uygula
3. gerekiyorsa Faz 6 seed veya ETL senaryosunu sec
4. Render tarafinda `jurisflow-canonical-staging` servisini future blueprint ile ac
5. env matrisindeki secret ve baglanti degerlerini yukle
6. deploy et
7. smoke runner'i `-Execute` ile calistir
8. kritik modullerden manuel UI dogrulamasi yap

## Execute Ornegi

Bu komut bu asamada calistirilmadi. Gelecekte canonical staging hazir oldugunda kullanilacak:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\render-phase7-staging-smoke.ps1 `
  -Execute `
  -BaseUrl "https://jurisflow-canonical-staging.onrender.com" `
  -TenantSlug "demo" `
  -StaffEmail "admin@example.com" `
  -StaffPassword "<secret>" `
  -ClientEmail "client@example.com" `
  -ClientPassword "<secret>"
```

## Faz 7 Cikis Kriteri Karsiligi

Bu repo durumu ile:
1. canonical staging cutover icin blueprint vardir
2. preflight kontrol seti vardir
3. smoke matrix ve runner vardir
4. gercek DB ve Render degisikligi kullanici onayi gelene kadar pasif tutulur
