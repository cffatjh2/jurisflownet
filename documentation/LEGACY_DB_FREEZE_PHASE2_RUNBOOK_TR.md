# Legacy DB Freeze Faz 2 Runbook

Bu runbook, mevcut canli Supabase prod DB'yi kalan refactorlar boyunca degistirmeden korumak icindir.

Aktif ilke:
- mevcut prod DB calismaya devam eder
- ama yeni schema denemeleri, constraint eklemeleri ve migration zinciri burada yapilmaz

Ana plan referansi:
- [MATTERS_NOTES_TRUST_PROD_PHASE_PLAN_TR.md](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/documentation/MATTERS_NOTES_TRUST_PROD_PHASE_PLAN_TR.md:1>)

## Faz 2 Karari

Bu fazda ekip su konuda netlesir:

1. mevcut canli Supabase DB `legacy/source DB` olarak kabul edilir
2. refactor boyunca bu DB'ye yeni kolon, yeni constraint veya migration uygulanmaz
3. buyuk domain ve uygulama degisiklikleri kod seviyesinde ilerler
4. final schema daha sonra yeni bir Supabase DB icin sifirdan kurulacaktir

## Bu Fazda Yapilmayacaklar

Yasaklar:

1. prod DB'de yeni kolon
2. prod DB'de yeni constraint
3. prod DB icin baseline / migration cutover
4. `supabase/migrations` altina aktif rollout migration'i eklemek
5. `JurisFlow.Server/Migrations` altina yeni EF migration'i eklemek

## Repo Icinde Eklenen Kontroller

### 1. Sprint oncesi schema snapshot komutu

- [supabase-schema-snapshot.ps1](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/scripts/supabase-schema-snapshot.ps1:1>)

Kullanim:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\supabase-schema-snapshot.ps1 `
  -EnvironmentName production
```

Alternatif:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\supabase-schema-snapshot.ps1 `
  -ConnectionString "Host=...;Port=5432;Database=postgres;Username=...;Password=...;SSL Mode=Require"
```

Bu komut:
- `pg_dump --schema-only` mantigiyla snapshot alir
- ciktiyi `out/supabase-schema-snapshots` altina yazar

### 2. CI DB freeze guard

- [check-phase2-db-freeze.ps1](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/scripts/check-phase2-db-freeze.ps1:1>)
- [phase2-legacy-db-freeze.yml](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/.github/workflows/phase2-legacy-db-freeze.yml:1>)

Bu guard su path'lerde degisiklik gorurse build'i fail eder:

1. `JurisFlow.Server/Migrations/`
2. `supabase/migrations/`

Amac:
- ekip refactor sirasinda yanlislikla migration commit etmesin

## Sprint Oncesi Operasyon Rutini

Her onemli sprintten once:

1. logical dump al
2. storage backup al
3. gerekiyorsa schema snapshot al
4. operasyon kaydina tarih ve cikti path'ini not dus

Kullanilacak scriptler:

- [supabase-db-dump.ps1](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/scripts/supabase-db-dump.ps1:1>)
- [supabase-storage-backup.ps1](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/scripts/supabase-storage-backup.ps1:1>)
- [supabase-schema-snapshot.ps1](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/scripts/supabase-schema-snapshot.ps1:1>)

## `ensure-created` Karari

Bu fazda:

- `ensure-created` mevcut legacy davranisi bozmamak icin tolere edilir
- ama hedef mimari olarak kabul edilmez

Hedef:

```env
Database__BootstrapMode=migrate
```

Bu gecis ise ancak final canonical DB kuruldugunda yapilacaktir.

## Gecici Uygulama Kurali

Kod degisikliklerinde su kural gecerlidir:

1. yeni domain kararlari once uygulama/service/DTO katmaninda ilerletilir
2. eski schema ile uyumlu gecici adapter veya mapping kullanilir
3. DB degisimi son fazlara birakilir

Bu ne demek:

- yeni davranis hazirlanabilir
- ama onu zorunlu kilan DB degisikligi simdi yapilmaz

## Faz 2 Cikis Checklist

Faz 2 tamamlandi saymak icin:

1. "legacy/source DB" karari yazili
2. "no schema change during refactor" kurali yazili
3. backup/snapshot rutini yazili
4. sprint oncesi schema snapshot komutu mevcut
5. accidental migration degisikligini bloke eden CI guard mevcut

## Not

Bu faz, teknik olarak "DB yapmiyoruz" demekten daha fazlasidir.

Amaç:
- ekibin kalan refactorlari DB baskisi olmadan bitirebilmesi
- ama bunu operasyon disiplini ve accidental-change guard ile yapmak
