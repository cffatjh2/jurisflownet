# Phase 0 - Evidence Inventory

Bu dosya, enterprise security review ve attestation hazirligi icin toplanacak kanitlari listeler.

## Evidence Status Legend
- `Available` = repo veya sistemden hemen alinabilir
- `Capture Required` = kontrol var ama kanit paketi hazir degil
- `External` = vendor / auditor / pentest firmasi gerekir
- `Planned` = sonraki fazda uretilecek

## Evidence Vault Standard (internal)

Onayli klasor formatini kullanin:

`/security-evidence/YYYY-Q#/CONTROL-ID/<artifact files>`

Dosya adlandirma:
- `YYYYMMDD-owner-artifact-type-short-title.ext`
- Ornek: `20260224-platform-config-prod-encryption-flags.png`

Minimum metadata (her artifact icin):
- Control ID
- Source system / endpoint
- Captured by
- Captured at (UTC)
- Environment (prod/stage/sandbox)
- Redaction status
- Expiry / refresh due date

## Evidence Catalog

| Evidence ID | Control Area | Artifact | Source | Status | Refresh | Owner | Notes |
|---|---|---|---|---|---|---|---|
| EV-IDA-001 | Tenant isolation | Tenant middleware config + request flow screenshot | API runtime + config | Capture Required | Quarterly | Backend | Include 400/401/tenant-mismatch example |
| EV-IDA-002 | RBAC | Policy list + endpoint authorization smoke tests | `Program.cs` + test run | Capture Required | Quarterly | Backend | Include `SecurityAdminOnly` proof |
| EV-IDA-003 | MFA | MFA enrollment/login screenshots + test result | app UI/API | Capture Required | Quarterly | Sec/QA | Verify current coverage before claiming |
| EV-ENC-001 | Document encryption | Prod config proof (`enabled`) + encrypted file sample | env/config + storage | Capture Required | Quarterly | Platform | Redact keys |
| EV-ENC-002 | DB field encryption | DB encrypted field sample + decryption app smoke test | DB + API | Capture Required | Quarterly | Platform | No plaintext PII in evidence |
| EV-AUD-001 | Audit immutability | Integrity verification API response | `/api/admin/audit-logs/integrity` | Capture Required | Monthly | Sec/Ops | Save response + timestamp |
| EV-OPS-001 | Health/monitoring | Health monitor screenshot / alert policy | monitor tool | Capture Required | Quarterly | Ops | Include uptime monitor ID |
| EV-OPS-002 | Rate limiting | Burst request test showing 429 | curl/k6 output | Capture Required | Quarterly | Platform | Auth endpoints + generic endpoint |
| EV-OPS-003 | Security headers | HTTP response header capture | curl/browser devtools | Capture Required | Quarterly | Platform | Include CSP if configured |
| EV-OPS-004 | Kill switch | Integration operation blocked by tenant/provider kill switch | Integrations Ops UI/API | Capture Required | Quarterly | Ops | Capture audit trace too |
| EV-OPS-005 | Replay tooling | Webhook replay + sync replay success logs | Integrations Ops UI/API | Capture Required | Quarterly | Ops | Include correlation IDs |
| EV-OPS-006 | Retry/backoff | 429/retry-after behavior test artifact | sandbox provider / mocked test | Capture Required | Quarterly | Backend/Ops | Can be sandbox evidence |
| EV-BKP-001 | Backup encryption | Encrypted backup file + config proof | admin backup + config | Capture Required | Quarterly | Platform | Do not expose keys |
| EV-BKP-002 | Restore dry run | Successful dry-run restore report | backup restore endpoint | Capture Required | Quarterly | Platform | Prefer stage env |
| EV-BKP-003 | Restore full test | Full restore validation report | stage restore test | Capture Required | Quarterly | Platform/Ops | Required for enterprise confidence |
| EV-IR-001 | Incident response | IR runbook + tabletop notes | docs + meeting record | Planned | Semiannual | Sec/Ops | Phase 2 readiness |
| EV-PAY-001 | Payment webhook idempotency | Duplicate event replay result | Stripe sandbox + logs | Capture Required | Quarterly | Backend/Finance | Show no duplicate apply |
| EV-PAY-002 | Period lock enforcement | Negative test for locked period mutation | API tests/manual | Capture Required | Quarterly | Finance/QA | Payments + legal billing |
| EV-TRU-001 | Trust reconciliation | Reconciliation result + mismatch scenario | legal billing API | Capture Required | Monthly | Finance | Include snapshot evidence |
| EV-EFL-001 | PACER/CAPA acknowledgement | Blocked CourtListener sync without acknowledgement | integration config + API result | Capture Required | Quarterly | Backend/Sec | |
| EV-RUL-001 | Jurisdiction rule harness | Validation harness run export | rules platform API | Capture Required | Quarterly | QA/Product | Basis for false pos/neg tracking |
| EV-EXT-001 | Pen test summary | External pentest executive summary | vendor report | External | Annual | Security | Phase 2 |
| EV-EXT-002 | SOC 2 report / bridge letter | Auditor deliverable | auditor | Planned | Annual | Security/Exec | Phase 2/3 |

## Evidence Collection Procedure (minimum)
1. Capture in target environment (`prod` preferred, `stage` acceptable if noted).
2. Redact secrets, tokens, customer PII.
3. Store in evidence vault path with naming standard.
4. Add entry/update status in this file.
5. Link artifact in questionnaire/trust package source bundle.

## Redaction Rules
- Never include encryption keys, API secrets, bearer tokens.
- Mask customer names/emails unless explicitly required and approved.
- Prefer synthetic or sandbox records for payment/efile evidence.
