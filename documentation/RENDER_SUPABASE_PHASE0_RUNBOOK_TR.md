# Render + Supabase Faz 0 Runbook

Bu runbook, `Matters / Notes / Trust` remediation çalışması öncesinde platform ve operasyon baseline'ını kurmak içindir.

Bu repo için hedef:

1. `staging` ve `production` Render servislerini ayırmak
2. `staging` ve `production` Supabase ortamlarını ayırmak
3. deploy'u CI başarısından sonra kontrollü başlatmak
4. Supabase DB ve Supabase Storage için ayrı backup/restore prosedürü oluşturmak

İlgili dosyalar:

- [render.yaml](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/render.yaml:1>)
- [cd-render.yml](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/.github/workflows/cd-render.yml:1>)
- [supabase-db-dump.ps1](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/scripts/supabase-db-dump.ps1:1>)
- [supabase-db-restore.ps1](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/scripts/supabase-db-restore.ps1:1>)
- [supabase-storage-backup.ps1](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/scripts/supabase-storage-backup.ps1:1>)
- [supabase-storage-restore.ps1](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/scripts/supabase-storage-restore.ps1:1>)

## 1. Render Tarafı

### 1.1 Hedef Servisler

İki ayrı web service olmalı:

1. `jurisflow-staging`
2. `jurisflow-prod`

Repo içinde bunun için `render.yaml` blueprint eklendi.

Önemli kararlar:

1. `healthCheckPath: /health`
2. `autoDeploy: false`
3. runtime `docker`
4. deploy CI sonrası hook ile tetiklenecek

### 1.2 Render Kurulum Adımı

1. Render dashboard'da `New + -> Blueprint` veya `New + -> Web Service` ile kurulumu yap.
2. Repo kökündeki `render.yaml` dosyasını baz al.
3. Staging ve prod için environment variable'ları ayrı ayrı tanımla.
4. Her iki servis için de health check olarak `/health` doğrula.

### 1.3 Deploy Akışı

Repo içinde `cd-render.yml` workflow'u eklendi.

Davranış:

1. `CI` başarılı olduktan sonra `main` branch için staging deploy hook'u tetiklenir.
2. Production deploy yalnızca `workflow_dispatch` ile manuel tetiklenir.

GitHub secret'ları:

1. `RENDER_STAGING_DEPLOY_HOOK_URL`
2. `RENDER_PRODUCTION_DEPLOY_HOOK_URL`

Bu yapı ile prod deploy, dashboard auto-deploy yerine CI kapısından geçmiş olur.

## 2. Supabase Tarafı

### 2.1 Ortam Ayrımı

Tercih sırası:

1. Supabase Branching ile `staging` branch
2. Bu mümkün değilse ayrı bir `staging` Supabase project

Kural:

1. staging DB prod ile aynı şema ve aynı env yapısına yakın olacak
2. storage bucket staging için ayrı olacak
3. service role key staging ve prod arasında paylaşılmayacak

### 2.2 Connection Kuralı

Render tarafında Supabase için `Session Pooler` connection string kullanılmalı.

Kaynak:

- [SUPABASE_SETUP.md](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/documentation/SUPABASE_SETUP.md:1>)
- [supabase.backend.env.example](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/supabase.backend.env.example:1>)

### 2.3 Bootstrap Kuralı

Bu kod tabanında PostgreSQL için varsayılan bootstrap halen `ensure-created`.

Bu yüzden:

1. Faz 0 içinde `Database__BootstrapMode=migrate` prod'da açılmaz
2. önce staging'de baseline kurulacak
3. migration history hizası gelmeden prod migration otomasyonu açılmayacak

## 3. Backup ve Restore Politikası

### 3.1 Neden Uygulama İçi Backup Yeterli Değil

Mevcut `BackupService` şunlara göre tasarlanmış:

1. SQLite dosyası
2. local `uploads/` dizini

Supabase prod için yeterli değildir.

Sebep:

1. prod veritabanı dosya tabanlı değil, Postgres
2. prod storage local disk değil, Supabase Storage

Bu yüzden Faz 0 backup hattı iki parçalı olmalı:

1. Supabase Postgres logical dump
2. Supabase Storage object backup

## 4. Supabase Postgres Logical Dump

### 4.1 Hazırlık

Makinede `pg_dump`, `pg_restore`, `psql` bulunmalı.

### 4.2 Full Dump

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\supabase-db-dump.ps1 `
  -ConnectionString "Host=...;Port=5432;Database=postgres;Username=...;Password=...;SSL Mode=Require"
```

### 4.3 Schema-Only Dump

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\supabase-db-dump.ps1 `
  -ConnectionString "Host=...;Port=5432;Database=postgres;Username=...;Password=...;SSL Mode=Require" `
  -SchemaOnly
```

### 4.4 Restore Dry Run

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\supabase-db-restore.ps1 `
  -ConnectionString "Host=...;Port=5432;Database=postgres;Username=...;Password=...;SSL Mode=Require" `
  -InputPath ".\out\supabase-dumps\supabase-full-YYYYMMDD-HHmmss.dump"
```

Bu mod restore uygulamaz.

Yaptığı şey:

1. custom dump ise restore inventory üretir
2. plain SQL ise SQL preview kopyası bırakır

### 4.5 Restore Apply

Bu adım prod'da yalnızca break-glass prosedürüyle yapılmalı.

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\supabase-db-restore.ps1 `
  -ConnectionString "Host=...;Port=5432;Database=postgres;Username=...;Password=...;SSL Mode=Require" `
  -InputPath ".\out\supabase-dumps\supabase-full-YYYYMMDD-HHmmss.dump" `
  -Apply
```

## 5. Supabase Storage Backup

### 5.1 Backup

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\supabase-storage-backup.ps1 `
  -SupabaseUrl "https://your-project-ref.supabase.co" `
  -ServiceRoleKey "service-role-key" `
  -Bucket "jurisflow-files"
```

Çıktı:

1. indirilen obje dosyaları
2. `manifest.json`

### 5.2 Restore Dry Run

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\supabase-storage-restore.ps1 `
  -SupabaseUrl "https://your-project-ref.supabase.co" `
  -ServiceRoleKey "service-role-key" `
  -Bucket "jurisflow-files" `
  -BackupDirectory ".\out\supabase-storage-backups\storage-YYYYMMDD-HHmmss"
```

Bu mod:

1. restore etmez
2. yüklenecek obje sayısını ve toplam byte miktarını raporlar

### 5.3 Restore Apply

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\supabase-storage-restore.ps1 `
  -SupabaseUrl "https://your-project-ref.supabase.co" `
  -ServiceRoleKey "service-role-key" `
  -Bucket "jurisflow-files" `
  -BackupDirectory ".\out\supabase-storage-backups\storage-YYYYMMDD-HHmmss" `
  -Apply
```

## 6. Staging Rehearsal Prosedürü

Faz 0 çıkış kriterini karşılamak için staging üzerinde en az bir kez şu tatbikat yapılmalı:

1. staging Render deploy başarılı olmalı
2. `/health` yeşil dönmeli
3. Supabase DB full dump alınmalı
4. Supabase Storage backup alınmalı
5. DB restore dry run çalışmalı
6. Storage restore dry run çalışmalı
7. gerekiyorsa disposable staging DB üzerinde gerçek restore uygulanmalı
8. staging uygulaması tekrar ayağa kalkmalı

## 7. Faz 0 Check List

### Tamamlandı Saymak İçin

1. `jurisflow-staging` Render service hazır
2. `jurisflow-prod` Render service hazır veya mevcut prod buna hizalanmış
3. `/health` Render tarafında tanımlı
4. `autoDeploy` kapalı
5. staging deploy hook secret'ı tanımlı
6. prod deploy hook secret'ı tanımlı
7. staging Supabase ortamı hazır
8. DB dump script'i staging üzerinde en az bir kez çalışmış
9. DB restore dry run staging üzerinde en az bir kez çalışmış
10. storage backup script'i staging üzerinde en az bir kez çalışmış
11. storage restore dry run staging üzerinde en az bir kez çalışmış

### Hâlâ Manuel Kalanlar

Bu repo içinde otomatikleştirilmedi, operasyonda yapılacak:

1. gerçek Render service oluşturma
2. Supabase branch veya project açma
3. deploy hook URL'lerini GitHub secret olarak ekleme
4. staging rehearsal sonucunu operasyon kaydına yazma

## 8. Faz 0 Sonrasi

Faz 0 tamamlandiktan sonra gelen aktif sira:

1. Faz 1 uygulama sertlestirmesi
2. Faz 2 legacy DB freeze ve snapshot kurali

Aktif Faz 2 runbook:
- [LEGACY_DB_FREEZE_PHASE2_RUNBOOK_TR.md](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/documentation/LEGACY_DB_FREEZE_PHASE2_RUNBOOK_TR.md:1>)
