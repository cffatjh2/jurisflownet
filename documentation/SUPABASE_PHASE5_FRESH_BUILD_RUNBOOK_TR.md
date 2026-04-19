# Faz 5 Runbook

Bu runbook, canonical schema icin yeni Supabase staging ortamini hazirlama adimlarini tanimlar.

Onemli:
- bu faz legacy prod DB'ye uygulanmaz
- bu faz yeni canonical staging/prod hedefleri icindir
- bu dokuman su an icin sadece hazirlik referansidir
- gercek remote `db push` ancak kullanici yonlendirmesiyle yapilir
- bu asamada yeni Supabase project olusturma zorunlulugu yoktur

## Hedef Ortam Karari

Tercih sirasi:
1. ayrik Supabase project: `jurisflow-canonical-staging`
2. gerekiyorsa ayrik Supabase branch

Not:
- Bu bir hedef ortam tarifidir.
- Bu turda gercek ortam acilmasi beklenmez.

Onerilen isimler:
- staging: `jurisflow-canonical-staging`
- production future target: `jurisflow-canonical-prod`

## Repo Kaynaklari

- migration seti: [supabase/migrations](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/supabase/migrations>)
- seed/reference data: [supabase/seed.sql](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/supabase/seed.sql:1>)
- validation scripti: [supabase-phase5-validate.ps1](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/scripts/supabase-phase5-validate.ps1:1>)
- env ornegi: [supabase.backend.env.example](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/supabase.backend.env.example:1>)

## Lokal Dogrulama

Komut:
```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\supabase-phase5-validate.ps1 -EnvironmentName local
```

Beklenen sonuc:
- Supabase CLI migration dosyalarini sirayla uygular
- `seed.sql` reference role kayitlarini yukler
- bos canonical DB ayaga kalkar

Not:
- bu adim Docker gerektirebilir
- Docker yoksa lokal reset calismaz; bu durumda sadece dosya varligi dogrulanmis olur

## Remote Staging Dry Run

Bu bolum gelecekteki aktivasyon adimidir.
Su an calistirilmasi gerekmez.

Gerekli env:
```powershell
$env:SUPABASE_CANONICAL_STAGING_PROJECT_REF="your-project-ref"
$env:SUPABASE_CANONICAL_STAGING_DB_PASSWORD="your-db-password"
```

Komut:
```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\supabase-phase5-validate.ps1 -EnvironmentName staging
```

Bu komut:
- `supabase link`
- `supabase db push --dry-run`
calistirir.

## Remote Staging Apply

Yalnizca acik yonlendirme ile:
```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\supabase-phase5-validate.ps1 -EnvironmentName staging -Apply
```

## Faz 5 Cikis Kriteri Karsiligi

Bu repo durumu ile:
1. yeni DB'yi kuran migration seti repoda vardir
2. staging cutover icin env matrisi ve validation akisi tanimlidir

Gercek staging apply bu runbook kapsaminda ileride hazirdir, ancak bu asamada calistirilmaz.
