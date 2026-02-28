# Integration Phase Gates (DoR / DoD)

This document defines the mandatory delivery gates for integration roadmap phases (QBO, email/DMS, CourtListener, e-filing partners, Xero/ERP, NetDocuments/iManage).

It is a delivery control mechanism, not a feature.

## Purpose

Use this checklist to prevent:
- Starting implementation before provider access/auth details are ready.
- Declaring a phase complete when only metadata/config exists (no operational sync).
- Shipping connectors without replay, observability, rate-limit handling, or kill switch validation.

## Definitions

### DoR (Definition of Ready)

Minimum requirements before implementation starts for a provider/phase.

### DoD (Definition of Done)

Minimum requirements before a provider/phase is considered operationally complete.

## Mandatory DoR (Start Gate)

All items must be satisfied before starting implementation.

- Provider sandbox access is available and credentials are tested.
- API documentation and required auth scopes are confirmed.
- Example payloads are available:
  - success
  - validation error
  - retry/rate-limit (`429` / transient `5xx`)
  - rejection/failure (if applicable, especially e-filing)
- Mapping requirements are defined:
  - field mappings
  - enum mappings
  - account/tax mappings (accounting)
  - conflict policy
- Pilot tenant is selected and documented.

## Mandatory DoD (Completion Gate)

All applicable items must be satisfied.

- `validate + pull + push + reconcile` actions work (or are explicitly marked not applicable with reason).
- Review/conflict queue produces real data (not mock/manual placeholders only).
- Replay works:
  - webhook replay (if webhook provider)
  - sync replay
- Rate-limit/backoff behavior tested (provider-specific retry policy).
- Audit trace correlation visible (run/webhook/replay actions linked in audit logs).
- Kill switch tested:
  - tenant-level
  - provider-level (if configured)
- At least:
  - `1` integration test
  - `1` retry/failure-path test
- Ops UI visibility confirmed (Settings / Integration Ops / filing workflow panels).

## Evidence Required (DoR + DoD)

Each gate review must include evidence links or artifacts.

### DoR evidence

- Provider sandbox account ID / tenant / org identifier
- Auth scope list (copied from provider docs)
- Example payload JSON files (or sanitized snippets)
- Mapping profile draft (JSON or screenshot)
- Pilot tenant ID / slug

### DoD evidence

- `validate`, `pull`, `push`, `reconcile` execution results (request ID / run ID / screenshots)
- Review queue and conflict queue screenshots/items
- Replay run result (`inbox`, `outbox`, or `sync replay`)
- Rate-limit/backoff test result (logs or run result)
- Kill switch test result (blocked action evidence)
- Audit log trace correlation sample (`traceId`, `integrationRunId`, `providerKey`, etc.)
- Test output (`dotnet test`, integration test logs)
- Ops UI screenshots (panels used by support/ops)

## Execution Workflow (How To Use)

For each roadmap phase/provider:

1. Open a gate review record from the template in `documentation/INTEGRATION_GATE_REVIEW_TEMPLATE.md`.
2. Fill DoR evidence before writing implementation code.
3. Implement provider/phase.
4. Run validation and collect DoD evidence.
5. Mark gate as `Pass`, `Conditional Pass`, or `Fail`.
6. If `Conditional Pass`, document explicit follow-up items and rollback constraints.

## Gate Status Policy

- `Pass`: all mandatory checks complete.
- `Conditional Pass`: limited pilot rollout allowed, non-critical gaps documented with owner/date.
- `Fail`: no rollout to pilot/production tenant.

## Applicability Matrix (Examples)

### Accounting connectors (QBO, Xero, Business Central, NetSuite)

Required:
- `validate`
- `pull`
- `push`
- `reconcile`
- conflict queue
- review queue
- period lock guard
- mapping coverage checks

### Email/DMS connectors (Gmail, Outlook, Drive, SharePoint, NetDocuments, iManage)

Required:
- `validate`
- `pull`
- `push` (review-planned push is acceptable if direct push is intentionally gated)
- `reconcile` (or documented equivalent inventory consistency check)
- webhook replay if webhook-first
- sync replay
- review queue for metadata/workspace/filing workflow

### E-filing partners

Required:
- `validate`
- `push` (submission)
- `reconcile` (status/timeline sync)
- webhook/sync replay
- rejection repair workflow
- notice/stamped copy ingestion (or review fallback)
- court-specific precheck validation

## Operational Commands (Recommended)

Use these during gate review and attach outputs/screenshots:

```powershell
dotnet build JurisFlow.Server/JurisFlow.Server.csproj
dotnet test JurisFlow.Server.Tests/JurisFlow.Server.Tests.csproj
```

UI checks (manual):
- `Settings > Integrations`
- `Settings > Integration Ops`
- `Settings > E-Filing Workspace` (if applicable)

## Rollout Rule

No provider/phase moves from engineering-complete to pilot rollout without a recorded DoD review.

