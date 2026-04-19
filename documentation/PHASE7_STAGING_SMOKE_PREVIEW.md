# Phase 7 Staging Smoke Preview

- mode: preview
- execute-http: no
- real-render-change: no
- real-supabase-change: no

## Smoke Matrix

- `health` : GET /health [none] - Liveness and readiness probe.
- `staff_login` : POST /api/login [none] - Requires tenant header and seeded staging staff account.
- `matters_list` : GET /api/Matters?page=1&pageSize=10 [staff] - List matters and derive a matter id for note checks.
- `matter_notes_list` : GET /api/matters/{matterId}/notes?page=1&pageSize=10 [staff] - Executed only when at least one matter id is available.
- `trust_accounts` : GET /api/Trust/accounts?limit=5 [staff] - Trust read surface smoke probe.
- `invoices_list` : GET /api/Invoices [staff] - Billing read surface smoke probe.
- `documents_list` : GET /api/Documents [staff] - Documents read surface smoke probe.
- `integrations_contract` : GET /api/integrations/ops/contract [staff] - Canonical contract probe.
- `integrations_capability_matrix` : GET /api/integrations/ops/capability-matrix [staff] - Integration capability matrix smoke probe.
- `client_login` : POST /api/client/login [none] - Requires tenant header and seeded client credentials.
- `client_profile` : GET /api/client/profile [client] - Portal identity probe.
- `client_matters` : GET /api/client/matters [client] - Portal matters probe.
- `client_invoices` : GET /api/client/invoices [client] - Portal billing probe.
- `client_documents` : GET /api/client/documents [client] - Portal documents probe.

## Future Execute Parameters

- `BaseUrl` : canonical staging base url
- `TenantSlug` : staging tenant slug passed as `X-Tenant-Slug`
- `StaffEmail` / `StaffPassword` : seeded staff smoke account
- `ClientEmail` / `ClientPassword` : optional portal smoke account

## Notes

- staff login MFA required donerse staff probes atlanir
- note probe ilk matter kaydindan `matterId` turetir
- client credential verilmezse portal probelari skipped olarak raporlanir
