# Render Canonical Staging Env Matrix

Bu dokuman, yeni canonical Supabase staging veritabani ile Render staging service eslemesini tanimlar.

Onemli:
- Bu bir hedef env matrisidir.
- Bu asamada Render env degisikligi veya yeni Supabase baglantisi yapilmaz.
- Asagidaki ayarlar, Faz 7 staging cutover basladiginda kullanilacaktir.

Onerilen Render service:
- `jurisflow-canonical-staging`

Onerilen Supabase project:
- `jurisflow-canonical-staging`

Hazir repo artefaktlari:
- [render.canonical.staging.yaml](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/render.canonical.staging.yaml:1>)
- [render-phase7-canonical-preflight.ps1](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/scripts/render-phase7-canonical-preflight.ps1:1>)
- [render-phase7-staging-smoke.ps1](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/scripts/render-phase7-staging-smoke.ps1:1>)

## Zorunlu Backend Env'leri

| Key | Deger / Not |
| --- | --- |
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `Database__Provider` | `postgres` |
| `Database__BootstrapMode` | `migrate` |
| `ConnectionStrings__DefaultConnection` | Supabase session pooler DSN |
| `Storage__Provider` | `supabase` |
| `Storage__Supabase__Url` | canonical staging project URL |
| `Storage__Supabase__ServiceRoleKey` | canonical staging service role key |
| `Storage__Supabase__Bucket` | `jurisflow-files` veya staging varyanti |
| `Jwt__Key` | staging secret |
| `Jwt__Issuer` | `JurisFlowServer` |
| `Jwt__Audience` | `JurisFlowClient` |
| `Tenancy__DefaultTenantSlug` | staging tenant slug |
| `Tenancy__DefaultTenantName` | staging tenant display name |
| `Seed__Enabled` | `true` |
| `Seed__AdminEmail` | staging admin email |
| `Seed__AdminPassword` | staging admin password |

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

## Refactor Feature Flag'leri

| Key | Onerilen Deger |
| --- | --- |
| `Features__RefactorWave__UseLegacyTaskAssignmentAdapter` | `true` |
| `Features__RefactorWave__UseLegacyMatterResponsibilityAdapter` | `true` |
| `Features__RefactorWave__UseTenantCompanySynchronization` | `true` |
| `Features__RefactorWave__RestrictLeadDeleteToPrivilegedRoles` | `true` |

## Operasyon Akisi

1. canonical Supabase staging project hazirla
2. `db push --dry-run`
3. Render env'lerini yukle
4. `Database__BootstrapMode=migrate` ile deploy et
5. smoke test:
   - auth
   - clients
   - matters
   - notes
   - trust
   - billing
   - documents
   - integrations

Referans env ornegi:
- [supabase.backend.env.example](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/supabase.backend.env.example:1>)

Faz 7 runbook:
- [SUPABASE_RENDER_PHASE7_STAGING_CUTOVER_RUNBOOK_TR.md](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/documentation/SUPABASE_RENDER_PHASE7_STAGING_CUTOVER_RUNBOOK_TR.md:1>)
