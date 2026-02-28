# Phase 0 - Security Control Inventory

Durum amaci: mevcut kontrollerin varligini, kapsamını ve kanit ihtiyacini tek tabloda toplamak.

Status legend:
- `Implemented`
- `Partially Implemented`
- `Planned`
- `Needs Verification`

## 1. Identity, Access, Tenant Isolation

| Control | Status | Scope | Repo / Runtime Reference | Evidence Needed | Owner |
|---|---|---|---|---|---|
| Tenant resolution middleware | Implemented | API request tenant context | `JurisFlow.Server/Middleware/TenantResolutionMiddleware.cs`, `JurisFlow.Server/Program.cs` | middleware config screenshot + tenant-negative test | Backend |
| Tenant-scoped query patterns (payments/admin/integrations) | Implemented (selected critical paths) | Payment/admin/integration operations | `PaymentsController`, `AdminController`, integration controllers/services | cross-tenant access test results | Backend |
| Staff-only / role-based authorization policies | Implemented | Admin/staff APIs | `Program.cs` policies, controller attributes | policy mapping export + endpoint auth test | Backend |
| Security admin separation for sensitive audit endpoints | Implemented | Audit hash/integrity endpoints | `AdminController` security endpoints + policy | role test evidence | Backend/Sec |
| MFA support | Needs Verification | User auth flows | `JurisFlow.Server/Controllers/MfaController.cs` | MFA setup/login flow test + screenshots | Backend/Sec |
| Least-privilege role assignment whitelist | Implemented | Admin user management | `AdminController` role whitelist logic | create/update user negative role tests | Backend |

## 2. Data Protection / Encryption

| Control | Status | Scope | Repo / Runtime Reference | Evidence Needed | Owner |
|---|---|---|---|---|---|
| Document encryption (app-layer) | Implemented (config-gated) | Uploaded documents at rest | `DocumentEncryptionService`, `SECURITY_AND_BACKUP.md` | prod config proof + encrypted file sample + decrypt smoke test | Platform |
| DB field-level encryption (selected columns) | Implemented (config-gated) | PII/message/json fields | `DbEncryptionService`, `JurisFlowDbContext` converters | prod config proof + encrypted column dump sample | Platform |
| Audit log immutability (hash chain) | Implemented (config-gated) | Audit entries | `AuditLogIntegrityService`, `SECURITY_AND_BACKUP.md` | integrity verification API result | Backend/Sec |
| Integration payload PII minimization | Implemented | Webhook/integration metadata storage | `IntegrationPiiMinimizationService` | before/after sanitized payload examples | Backend |
| Integration secret store provider hardening (prod) | Implemented | Integration secrets | `Program.cs` prod validation, `IntegrationSecretProtection` | prod startup config + secret provider screenshot | Platform/Sec |

## 3. Availability, Backup, Recovery

| Control | Status | Scope | Repo / Runtime Reference | Evidence Needed | Owner |
|---|---|---|---|---|---|
| Health endpoint | Implemented | API liveness/readiness basic check | `/health`, `OPERATIONS_RUNBOOK.md` | monitor screenshot + health response | Platform |
| Backup create/download/restore workflows | Implemented | DB/uploads backup/restore | `BackupService`, admin backup endpoints, `SECURITY_AND_BACKUP.md` | successful backup + dry-run restore evidence | Platform |
| Backup encryption | Implemented (config-gated) | backup artifacts | `SECURITY_AND_BACKUP.md` | encrypted backup artifact sample + config proof | Platform |
| Incident response runbook | Implemented (documented) | Ops process | `documentation/INCIDENT_RUNBOOK.md` | tabletop exercise record (missing) | Sec/Ops |
| DR restore test cadence | Partially Implemented | Ops process | backup/restore endpoints exist | scheduled restore test reports (missing) | Platform/Ops |

## 4. Application Security / Secure Operations

| Control | Status | Scope | Repo / Runtime Reference | Evidence Needed | Owner |
|---|---|---|---|---|---|
| Rate limiting (global + auth stricter) | Implemented | Public/auth endpoints | `Program.cs` rate limiter config, `OPERATIONS_RUNBOOK.md` | load test or curl burst evidence | Platform |
| Security headers baseline | Implemented | HTTP responses | `OPERATIONS_RUNBOOK.md`, security header middleware/config | response header capture | Backend |
| Audit trace correlation for integration ops | Implemented | integration runs/replays/webhooks | `AuditTraceContext`, `AuditLogger`, `IntegrationsOpsController` | sample correlated audit entries | Backend |
| Per-tenant/provider kill switch | Implemented | Integration operations | `IntegrationOperationsGuard` | kill switch block test evidence | Backend/Ops |
| Replay tooling (webhook/sync) | Implemented | Integration operations | `IntegrationsOpsController` replay endpoints | replay success audit/log evidence | Backend/Ops |
| Retry/backoff policies per connector | Implemented | Integration runtime | `IntegrationOperationsGuard`, `IntegrationRuntime`, `IntegrationCanonicalActionRunner` | throttling simulation evidence | Backend/Ops |

## 5. Financial Controls (Payments / Billing / Trust)

| Control | Status | Scope | Repo / Runtime Reference | Evidence Needed | Owner |
|---|---|---|---|---|---|
| Decimal money handling in payments | Implemented | Payment/invoice/refund flows | `PaymentsController`, `StripePaymentService` | regression tests + sample transactions | Backend |
| Payment webhook idempotency | Implemented | Stripe webhook processing | `StripeWebhookEvents`, `PaymentsController` | duplicate webhook replay evidence | Backend |
| Tenant-scoped payment confirmation hardening | Implemented | Confirm payment endpoint | `PaymentsController` confirm metadata checks | negative test evidence | Backend |
| Period lock enforcement (payments/legal billing/integrations) | Implemented | Accounting-affecting writes | `PaymentsController`, `LegalBillingEngineService`, integration service | locked-period negative tests | Backend/Finance |
| Ledger-first billing adjustments/reversals | Implemented | Legal billing engine | `LegalBillingEngineService.*` | ledger/reversal test evidence | Backend/Finance |
| Trust 3-way reconciliation engine | Implemented | Legal trust accounting | `LegalBillingController`, `LegalBillingEngineService` | reconciliation output + mismatch test | Finance/Backend |
| Trust reconciliation history snapshots | Implemented | KPI historical tracking | `TrustReconciliationSnapshot` + analytics | snapshot accumulation evidence | Backend/Ops |

## 6. E-filing / Docket / Rule Platform

| Control | Status | Scope | Repo / Runtime Reference | Evidence Needed | Owner |
|---|---|---|---|---|---|
| PACER/CAPA policy acknowledgement enforcement | Implemented | CourtListener/RECAP sync | integration connector service | config + blocked sync test | Backend/Sec |
| Jurisdiction rule pack versioning and coverage matrix | Implemented (foundation) | e-filing precheck/rules | `Jurisdiction*` models/services/controllers | seeded rule packs + precheck evidence | Product/Backend |
| Low-confidence -> human review gate | Implemented | E-filing precheck workflow | `EfilingController` + review queue | sample low-confidence review item | Backend/Ops |
| Court validation harness (regression foundation) | Implemented (foundation) | rule regression testing | `JurisdictionRulesPlatformController` harness endpoints | harness run artifact | Backend/QA |

## 7. Known Gaps (Phase 0 outcome candidates)

These should remain visible until moved to Phase 1/2 deliverables.

- Public Trust Center and customer-facing security assurance page (`Planned`)
- Standardized security questionnaire response library (`Planned`)
- Pen test executive summary (external evidence missing)
- Formal BC/DR tested cadence evidence (process evidence missing)
- SOC 2 Type I readiness package / policy set completeness (`Partially Implemented`)
- Exact provider-grade e-billing result ingestion coverage by all provider adapters (`Partially Implemented`)
