# Render Canonical Production Env Matrix

Bu dokuman, canonical production cutover aninda Render production service ile yeni Supabase production ortam eslesmesini tanimlar.

Onemli:
- Bu bir hedef env matrisidir.
- Bu asamada Render production env degisikligi yapilmaz.
- Bu asamada mevcut prod Supabase baglantisi degistirilmez.
- Asagidaki ayarlar, Faz 8 production cutover basladiginda kullanilacaktir.

Onerilen Render service:
- `jurisflow-canonical-prod`

Onerilen Supabase project:
- `jurisflow-canonical-prod`

Hazir repo artefaktlari:
- [render.canonical.production.yaml](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/render.canonical.production.yaml:1>)
- [render-phase8-production-preflight.ps1](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/scripts/render-phase8-production-preflight.ps1:1>)
- [render-phase8-production-smoke.ps1](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/scripts/render-phase8-production-smoke.ps1:1>)

## Zorunlu Backend Env'leri

| Key | Deger / Not |
| --- | --- |
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `Database__Provider` | `postgres` |
| `Database__BootstrapMode` | `migrate` |
| `Database__ApplyMigrationsOnStartup` | `false` |
| `ConnectionStrings__DefaultConnection` | yeni canonical prod Supabase session pooler DSN |
| `Storage__Provider` | `supabase` |
| `Storage__Supabase__Url` | canonical prod project URL |
| `Storage__Supabase__ServiceRoleKey` | canonical prod service role key |
| `Storage__Supabase__Bucket` | production bucket |
| `Jwt__Key` | production secret |
| `Jwt__Issuer` | `JurisFlowServer` |
| `Jwt__Audience` | `JurisFlowClient` |
| `Tenancy__DefaultTenantSlug` | production tenant slug |
| `Tenancy__DefaultTenantName` | production tenant display name |
| `Seed__Enabled` | `false` |

## Guvenlik Env'leri

| Key | Not |
| --- | --- |
| `Security__MfaEnforced` | `true` |
| `Security__DocumentEncryptionEnabled` | `true` |
| `Security__DbEncryptionEnabled` | `true` |
| `Security__AuditLogImmutable` | `true` |
| `Security__AuditLogFailClosed` | `true` |
| `Security__DocumentEncryptionKey` | base64 32-byte key |
| `Security__DbEncryptionKey` | base64 32-byte key |
| `Security__AuditLogKey` | base64 key |

## Operasyon Akisi

1. prod freeze penceresini ac
2. son logical dump ve storage backup'i al
3. gerekiyorsa Faz 6 ETL/import senaryosunu sec
4. Render production env'lerini canonical production degerlerine yukle
5. `Database__BootstrapMode=migrate` ile deploy et
6. production smoke testlerini kos
7. eski DB'yi hemen silme; rollback penceresinde koru

Referans env ornegi:
- [supabase.backend.env.example](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/supabase.backend.env.example:1>)

Faz 8 runbook:
- [SUPABASE_RENDER_PHASE8_PRODUCTION_CUTOVER_RUNBOOK_TR.md](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/documentation/SUPABASE_RENDER_PHASE8_PRODUCTION_CUTOVER_RUNBOOK_TR.md:1>)
