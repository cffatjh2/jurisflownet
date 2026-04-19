# Faz 7 Staging Smoke Matrix

Bu dokuman, canonical staging cutover aninda kosulacak smoke yuzeyini sabitler.

Onemli:
- bu dokuman bu asamada sadece plan ve repo artefaktidir
- gercek Supabase staging DB olusturma yok
- gercek Render env degisikligi yok
- execute ancak kullanici ayrica baslat dediginde yapilacak

## Probe Seti

| Surface | Endpoint | Auth | Not |
| --- | --- | --- | --- |
| health | `GET /health` | none | Render health check dogrulamasi |
| staff auth | `POST /api/login` | none | `X-Tenant-Slug` gerekli |
| matters | `GET /api/Matters?page=1&pageSize=10` | staff | listeden not probe icin matter id turetilir |
| notes | `GET /api/matters/{matterId}/notes?page=1&pageSize=10` | staff | ilk matter id bulunduysa calisir |
| trust | `GET /api/Trust/accounts?limit=5` | staff | trust read surface smoke |
| billing | `GET /api/Invoices` | staff | billing read smoke |
| documents | `GET /api/Documents` | staff | documents read smoke |
| integrations | `GET /api/integrations/ops/contract` | staff | canonical contract smoke |
| integrations | `GET /api/integrations/ops/capability-matrix` | staff | provider matrix smoke |
| client auth | `POST /api/client/login` | none | client portal credential gerekli |
| portal | `GET /api/client/profile` | client | portal identity smoke |
| portal | `GET /api/client/matters` | client | portal matters smoke |
| portal billing | `GET /api/client/invoices` | client | portal billing smoke |
| portal documents | `GET /api/client/documents` | client | portal documents smoke |

## Operasyon Kurallari

1. Staff smoke kullanicisi staging seed ile hazir olmali.
2. Staff smoke kullanicisi MFA-required donuyorsa staff probe seti bloklanir.
3. Client smoke kullanicisi opsiyoneldir; yoksa portal probe'lari skipped olarak raporlanir.
4. Notes probe, liste sonucunda en az bir matter varsa kosulur.
5. Smoke raporu basarisiz olsa bile bu script DB degistirmez; sadece HTTP dogrulama yapar.

## Repo Artefaktlari

- manifest: [PHASE7_STAGING_SMOKE_MANIFEST.json](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/documentation/PHASE7_STAGING_SMOKE_MANIFEST.json:1>)
- runner: [render-phase7-staging-smoke.ps1](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/scripts/render-phase7-staging-smoke.ps1:1>)
