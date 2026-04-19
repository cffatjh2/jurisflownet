# Faz 8 Production Smoke Matrix

Bu dokuman, production cutover aninda kosulacak minimum kritik probe setini sabitler.

Onemli:
- bu asamada sadece plan ve repo artefaktidir
- gercek prod smoke execute edilmez
- gercek Render veya Supabase degisikligi yapilmaz

## Kritik Probe Seti

| Surface | Endpoint | Auth | Not |
| --- | --- | --- | --- |
| health | `GET /health` | none | Render health check |
| staff auth | `POST /api/login` | none | `X-Tenant-Slug` gerekli |
| matters | `GET /api/Matters?page=1&pageSize=10` | staff | ana matter read smoke |
| trust | `GET /api/Trust/accounts?limit=5` | staff | trust read smoke |
| billing | `GET /api/Invoices` | staff | billing read smoke |
| notes | `GET /api/matters/{matterId}/notes?page=1&pageSize=10` | staff | ilk matter id uzerinden |
| client auth | `POST /api/client/login` | none | client portal credential varsa |
| client matters | `GET /api/client/matters` | client | portal matter smoke |
| client invoices | `GET /api/client/invoices` | client | portal billing smoke |

## Operasyon Kurallari

1. Smoke, env switch ve deploy tamamlanir tamamlanmaz kosulur.
2. Staff smoke hesabi production-safe ve auditlenebilir olmalidir.
3. Client smoke hesabi sadece read-safe portal kontrolu icin kullanilmalidir.
4. Staff login MFA-required donerse bu durum blocker olarak kayda gecer.
5. Health yesil olmadan diger probe setine gecilmez.

## Repo Artefaktlari

- runner: [render-phase8-production-smoke.ps1](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/scripts/render-phase8-production-smoke.ps1:1>)
- runbook: [SUPABASE_RENDER_PHASE8_PRODUCTION_CUTOVER_RUNBOOK_TR.md](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/documentation/SUPABASE_RENDER_PHASE8_PRODUCTION_CUTOVER_RUNBOOK_TR.md:1>)
