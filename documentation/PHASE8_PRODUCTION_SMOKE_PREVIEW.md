# Phase 8 Production Smoke Preview

- mode: preview
- execute-http: no
- real-render-change: no
- real-supabase-change: no

## Critical Probes

- `GET /health`
- `POST /api/login`
- `GET /api/Matters?page=1&pageSize=10`
- `GET /api/Trust/accounts?limit=5`
- `GET /api/Invoices`
- `GET /api/matters/{matterId}/notes?page=1&pageSize=10`
- `POST /api/client/login`
- `GET /api/client/matters`
- `GET /api/client/invoices`

## Future Execute Parameters

- `BaseUrl` : canonical production base url
- `TenantSlug` : production tenant slug passed as `X-Tenant-Slug`
- `StaffEmail` / `StaffPassword` : production-safe smoke account
- `ClientEmail` / `ClientPassword` : optional portal smoke account

## Notes

- health yesil olmadan diger probelara gecilmez
- staff login MFA required donerse blocker olarak kaydedilir
- client credential verilmezse portal probelari skipped olarak raporlanir
