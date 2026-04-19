# Matters / Notes / Trust Prod Faz Plani

Bu dokuman, mevcut stratejiyi su sekilde gunceller:

- uygulama `Render` uzerinde calisiyor
- canli veritabani `Supabase Postgres` uzerinde
- su anda canli DB uzerinde schema degisikligi yapilmiyor
- kalan buyuk refactorlar once kod ve domain seviyesinde tamamlanacak
- en sonda yeni bir `canonical` DB semasi uretilecek
- cutover yeni Supabase ortamina yapilacak

Bu artik klasik "mevcut prod DB'yi migration zinciriyle evrimlestir" plani degil. Bu plan:

- `legacy prod DB korunur`
- `uygulama ve domain yeniden duzenlenir`
- `final schema sifirdan tasarlanir`
- `yeni Supabase DB'ye cutover yapilir`

## Net Strateji

Mevcut canli Supabase DB:

- sistemin aktif veri kaynagi olarak calismaya devam eder
- bugunden itibaren "legacy/source DB" gibi ele alinir
- gelistirme ortami olarak kullanilmaz
- buyuk schema denemeleri bu DB uzerinde yapilmaz

Final hedef:

- yeni bir Supabase project veya branch DB
- `supabase/migrations` altinda canonical SQL migration seti
- uygulamada `Database__BootstrapMode=migrate`
- gerekirse eski DB'den yeni DB'ye data migration

## Ana Ilkeler

1. Mevcut prod Supabase DB uzerinde simdi DDL yok.
2. Once uygulama ve domain refactoru tamamlanir.
3. Final schema mevcut DB'den "patch" ederek degil, sifirdan tasarlanir.
4. Yeni DB once staging cutover ile dogrulanir.
5. Prod'a gecis "migration rollout" degil, "controlled cutover" olarak ele alinir.
6. Eski prod DB rollback icin bir sure korunur.

## Faz Durumu

### Faz 0
Durum:
- tamamlandi

Kapsam:
- Render + Supabase operasyon baseline
- dump / restore / storage backup runbook
- staging service ve health-check hazirligi

Referans:
- [RENDER_SUPABASE_PHASE0_RUNBOOK_TR.md](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/documentation/RENDER_SUPABASE_PHASE0_RUNBOOK_TR.md:1>)

### Faz 1
Durum:
- tamamlandi

Kapsam:
- matter DTO whitelist
- entity bind kaldirma
- note permission split
- trust read projection / clamp / pagination baslangici
- rate limit ve request size limitleri

Faz 2 operasyon detayi icin bkz.:
- [LEGACY_DB_FREEZE_PHASE2_RUNBOOK_TR.md](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/documentation/LEGACY_DB_FREEZE_PHASE2_RUNBOOK_TR.md:1>)

## Kalan Fazlar

## Faz 2: Legacy DB Freeze ve Snapshot Kurali
Amac:
- kalan refactorlar boyunca mevcut canli DB'yi degistirmeden korumak.

Bu fazda yapilacaklar:
1. Mevcut prod Supabase DB "legacy/source" olarak tanimlanir.
2. Bu DB icin "no schema change during refactor" kurali konur.
3. Her onemli sprint oncesi:
- logical dump
- storage backup
- gerekli ise schema snapshot
4. `ensure-created` davranisi sadece mevcut legacy bootstrapi bozmamak icin tolere edilir; yeni hedef olarak kabul edilmez.
5. Uygulama tarafinda yeni domain kararlarini eski schema ile uyumlu gecici adapterlarla ilerletme kurali benimsenir.

Bu fazda ozellikle yapilmaz:
- prod DB'de yeni kolon
- prod DB'de yeni constraint
- mevcut prod DB icin baseline/migration cutover

Prod deploy tipi:
- operasyon karari
- app-safe

Cikis kriteri:
1. Takim mevcut prod DB'ye dokunmama kararinda netlesmis olur.
2. Backup disiplini yazili hale gelir.
3. Kalan refactorlarin DB-freeze altinda ilerleyecegi kabul edilir.

## Faz 3: Uygulama ve Domain Refactor Dalgasi
Amac:
- tum sekmelerdeki buyuk uygulama degisikliklerini mevcut DB'yi zorlamadan tamamlamak.

Bu fazda yapilacaklar:
1. Matter / notes / trust disindaki modullerde de contract ve authorization sertlestirmesi.
2. Controller -> service -> validator -> mapper ayrimi standartlastirma.
3. Legacy entity alanlariyla yeni domain ihtiyaclari arasina adapter katmanlari koyma.
4. Final hedef modele uygun response/request DTO'lari olusturma.
5. Audit, lifecycle, workflow ve permission davranislarini kod tarafinda netlestirme.
6. Mümkün olan yerlerde feature flag ile yeni davranis hazirligi yapma.

Bu fazda yapilmaz:
- final DB constraint uygulama
- yeni canonical tablo kurulumu
- prod DB destructive migration

Prod deploy tipi:
- app-only deploylar

Rollback:
- Render rollback yeterli olmali

Cikis kriteri:
1. Uygulama davranisinin buyuk kismi yeni domain kararlarina tasinmis olur.
2. Final DB tasarimi artik net cikarilabilir hale gelir.

## Faz 4: Canonical Schema Design
Durum:
- tamamlandi

Amac:
- final veritabani modelini sifirdan tasarlamak.

Bu fazda yapilacaklar:
1. Final entity listesi cikarilir.
2. Her aggregate icin:
- tablo yapisi
- PK/FK
- unique index
- check constraint
- audit alanlari
- concurrency alanlari
- retention / soft-delete kurallari
3. Legacy alandan canonical alana mapping matrisi yazilir.
4. Hangi veriler tasinacak, hangileri tasinmayacak netlestirilir.
5. `Matter`, `MatterNote`, `Trust*`, user/role, billing, documents, portal, integration taraflari icin final SQL tasarim seti hazirlanir.

Repo ciktisi:
- tasarim dokumani
- tablo bazli mapping dokumani
- migration backlog

Bu fazda yapilmaz:
- yeni DB olusturma
- cutover

Cikis kriteri:
1. Yeni DB semasi karar seviyesinde tamamlanmis olur.
2. Data migration gerekip gerekmedigi tablo tablo bellidir.

Referans:
- [CANONICAL_SCHEMA_DESIGN_TR.md](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/documentation/CANONICAL_SCHEMA_DESIGN_TR.md:1>)
- [CANONICAL_SCHEMA_MAPPING_MATRIX_TR.md](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/documentation/CANONICAL_SCHEMA_MAPPING_MATRIX_TR.md:1>)
- [CANONICAL_SCHEMA_MIGRATION_BACKLOG_TR.md](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/documentation/CANONICAL_SCHEMA_MIGRATION_BACKLOG_TR.md:1>)

## Faz 5: Fresh Supabase Build Hazirligi
Durum:
- repo tarafi tamamlandi
- gercek DB provisioning beklemede

Amac:
- yeni Supabase ortaminda final schema'yi kurmaya hazir olmak.

Bu fazda yapilacaklar:
1. Yeni staging Supabase project veya branch icin hedef tanim netlestirilir.
2. `supabase/migrations` altinda final migration seti yazilir.
3. Bos DB'ye sifirdan kurulumu hedefleyen migration akisi repo seviyesinde hazirlanir.
4. Gerekirse:
- seed dosyalari
- bootstrap referans datasi
- lookup tablolari
hazirlanir.
5. Render staging service yeni DB ile eslenecek sekilde env matrisi hazirlanir.

Onemli not:
- Bu faz, yeni DB'yi tasarlar ve hazirlar.
- Kullanici yonlendirmesiyle acikca baslatilmadan:
  - yeni Supabase project olusturulmaz
  - `db push` calistirilmaz
  - Render env'leri yeni DB'ye cevrilmez

Cikis kriteri:
1. Yeni DB'yi kuran migration seti repoda vardir.
2. Staging cutover teknik olarak kagit ve repo seviyesinde hazirdir.

Referans:
- [SUPABASE_PHASE5_FRESH_BUILD_RUNBOOK_TR.md](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/documentation/SUPABASE_PHASE5_FRESH_BUILD_RUNBOOK_TR.md:1>)
- [RENDER_CANONICAL_STAGING_ENV_MATRIX_TR.md](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/documentation/RENDER_CANONICAL_STAGING_ENV_MATRIX_TR.md:1>)

## Faz 6: Data Migration / Seed Akisi
Durum:
- repo tarafi tamamlandi
- gercek extract/import beklemede

Amac:
- eski DB'den yeni DB'ye neyin nasil gececegini tamamlamak.

Onemli not:
- Bu fazda da gercek legacy DB extract veya yeni canonical DB import calistirilmaz.
- Bu fazin ciktisi script, mapping, transform ve verification setidir.
- Gercek veri tasima ancak kullanici ayrica baslat dediginde uygulanir.

Iki senaryo:

### Senaryo A: Veri tasinacak
Yapilacaklar:
1. Legacy -> canonical tablo mapping scriptleri
2. transform kurallari
3. id map / foreign key map
4. dogrulama raporlari:
- row count
- orphan check
- sample verification

### Senaryo B: Veri tasinmayacak veya minimal tasinacak
Yapilacaklar:
1. seed admin / tenant / lookup setup
2. sifir-baslangic onboarding akisi

Her iki durumda da:
- migration scriptleri idempotent olmali
- dry-run veya preview raporu uretebilmeli

Cikis kriteri:
1. Yeni DB'yi veriyle veya seed ile ayağa kaldiran net bir mekanizma vardir.

Referans:
- [SUPABASE_PHASE6_DATA_MIGRATION_RUNBOOK_TR.md](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/documentation/SUPABASE_PHASE6_DATA_MIGRATION_RUNBOOK_TR.md:1>)
- [supabase/etl](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/supabase/etl>)

## Faz 7: Staging Cutover
Durum:
- repo tarafi tamamlandi
- gercek staging cutover beklemede

Amac:
- uygulamayi yeni canonical DB ile staging uzerinde uca uca calistirmak.

Bu fazda yapilacaklar:
1. Yeni Supabase staging DB kurulur.
2. Final migration seti uygulanir.
3. Gerekli ise data migration / seed uygulanir.
4. Render staging yeni DB'ye baglanir.
5. Uca uca dogrulama yapilir:
- auth
- matters
- notes
- trust
- billing
- documents
- portal
- integrations

Cikis kriteri:
1. Staging tam olarak yeni DB ile calisir.
2. Kritik smoke ve integration testleri gecer.

Onemli not:
- bu asamada gercek Supabase staging DB kurulmaz
- bu asamada gercek `db push` calistirilmaz
- bu asamada Render staging env yeni DB'ye cevrilmez
- repo ciktisi future blueprint + preflight + smoke paketidir

Referans:
- [SUPABASE_RENDER_PHASE7_STAGING_CUTOVER_RUNBOOK_TR.md](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/documentation/SUPABASE_RENDER_PHASE7_STAGING_CUTOVER_RUNBOOK_TR.md:1>)
- [PHASE7_STAGING_SMOKE_MATRIX_TR.md](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/documentation/PHASE7_STAGING_SMOKE_MATRIX_TR.md:1>)
- [PHASE7_PREFLIGHT_REPORT.md](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/documentation/PHASE7_PREFLIGHT_REPORT.md:1>)

## Faz 8: Production Cutover
Durum:
- repo tarafi tamamlandi
- gercek production cutover beklemede

Amac:
- prod'u eski DB'den yeni DB'ye gecirmek.

Bu fazda yapilacaklar:
1. Prod freeze penceresi belirlenir.
2. Son logical dump + storage backup alinir.
3. Gerekliyse final data migration calistirilir.
4. Render production env yeni Supabase DB'ye cevrilir.
5. Health, auth, matter, trust ve billing smoke testleri aninda calistirilir.
6. Eski DB hemen silinmez; rollback icin bir sure korunur.

Rollback:
- Render env eski DB'ye dondurulebilir
- eski DB read-only veya korunmus halde bekletilir
- storage ve app versiyonu birlikte degerlendirilir

Cikis kriteri:
1. Production yeni DB ile stabil calisiyor olur.
2. Eski DB aktif write kaynagi olmaktan cikar.

Onemli not:
- bu asamada gercek production freeze acilmaz
- bu asamada gercek logical dump, storage backup veya env switch calistirilmaz
- bu asamada Render production yeni DB'ye cevrilmez
- repo ciktisi future blueprint + preflight + smoke + rollback paketidir

Referans:
- [SUPABASE_RENDER_PHASE8_PRODUCTION_CUTOVER_RUNBOOK_TR.md](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/documentation/SUPABASE_RENDER_PHASE8_PRODUCTION_CUTOVER_RUNBOOK_TR.md:1>)
- [RENDER_CANONICAL_PRODUCTION_ENV_MATRIX_TR.md](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/documentation/RENDER_CANONICAL_PRODUCTION_ENV_MATRIX_TR.md:1>)
- [PHASE8_PRODUCTION_CUTOVER_CHECKLIST_TR.md](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/documentation/PHASE8_PRODUCTION_CUTOVER_CHECKLIST_TR.md:1>)
- [PHASE8_ROLLBACK_CHECKLIST_TR.md](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/documentation/PHASE8_ROLLBACK_CHECKLIST_TR.md:1>)
- [PHASE8_PREFLIGHT_REPORT.md](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/documentation/PHASE8_PREFLIGHT_REPORT.md:1>)

## Yeni Sira

1. Faz 0
2. Faz 1
3. Faz 2
4. Faz 3
5. Faz 4
6. Faz 5
7. Faz 6
8. Faz 7
9. Faz 8

Kisa karsilik:
- once operasyon guvenligi
- sonra uygulama sertlestirme
- sonra legacy DB freeze
- sonra tum kod refactorlari
- sonra final schema design
- sonra yeni DB build
- sonra data migration
- sonra staging cutover
- en son prod cutover

## Artik Aktif Olmayan Eski Yaklasim

Asagidaki yaklasim artik aktif yol degildir:

- mevcut prod Supabase DB'den hemen baseline alip
- ayni DB uzerinde additive migration zinciriyle devam etmek

Bu repo icindeki Supabase baseline altyapisi yine korunabilir, ancak mevcut karara gore:

- simdi uygulanmayacak
- final cutover kararinda yeniden degerlendirilecek

## Bu Plana Gore "Prod Ready" Esigi

Bu yeni stratejide prod-ready esigi su siradadir:

1. Faz 3 tamamlanmis olmali
2. Faz 4 canonical schema tasarimi tamamlanmis olmali
3. Faz 5 migration seti hazir olmali
4. Faz 6 data migration / seed akisi hazir olmali
5. Faz 7 staging cutover gecmis olmali
6. Faz 8 production cutover kontrollu sekilde tamamlanmis olmali

## Operasyon Kararlari

### Render
1. `/health` health check olarak kalir.
2. Staging ve prod service ayrimi korunur.
3. Cutover gunu env degisikligi kontrollu yapilir.

### Supabase
1. Mevcut prod DB simdilik korunur.
2. Yeni canonical DB ayri ortamda kurulur.
3. Final hedef migration-only modeldir.
4. `ensure-created`, sadece eski gecis donemi davranisi olarak kalir.

## Referanslar

- Render health checks: [render.com/docs/health-checks](https://render.com/docs/health-checks)
- Render deploys: [render.com/docs/deploys](https://render.com/docs/deploys)
- Supabase Postgres connection yontemleri: [supabase.com/docs/guides/database/connecting-to-postgres](https://supabase.com/docs/guides/database/connecting-to-postgres)
- Supabase backup ve restore: [supabase.com/docs/guides/platform/backups](https://supabase.com/docs/guides/platform/backups)
- Supabase project clone / cutover mantigi: [supabase.com/docs/guides/platform/clone-project](https://supabase.com/docs/guides/platform/clone-project)
