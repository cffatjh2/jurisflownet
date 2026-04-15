# Trust Accounting Phase 6 RFC

Status: Draft
Owner: JurisFlow trust/accounting track
Last updated: 2026-04-15

---

## 1. Purpose

Phase 5 made trust accounting operationally recoverable:

- jurisdiction policy packs exist
- canonical packet/month-close versioning exists
- statement evidence lifecycle is hardened
- operational alerts now have workflow and history
- recovery and compliance bundle commands are first-class

Phase 6 moves the system from internally-governed trust accounting to externally-integrated, operator-scalable trust operations.

Goal: connect trust accounting to real evidence sources, make compliance work routable and automatable, and produce stronger chain-of-custody guarantees without regressing the low-latency transaction/runtime path.

---

## 2. Problem Statement

The trust subsystem is now structurally and operationally strong, but it still depends too much on manual entry and local operator discipline:

- statement imports are structured, but not yet driven by real bank-feed/file pipelines
- compliance alerts exist, but still behave like dashboard state more than a routed inbox
- bundles are generator-based, but not yet signed, retained, or evidenced as immutable audit packets
- jurisdiction policy packs exist, but packet templates/attestations are not yet jurisdiction-complete
- close operations are explicit, but not yet scheduled/proactive with calendar/SLA automation

Result: the system is recoverable and defensible, but not yet fully production-scaled for sustained multi-office trust operations.

---

## 3. Phase 6 Goals

- Introduce real statement intake and evidence attachment flow as a first-class subsystem.
- Promote compliance alerts into a routable operations inbox with ownership, SLA, and escalation automation.
- Add jurisdiction-specific packet templates and attestation requirements on top of the canonical close chain.
- Strengthen chain-of-custody for trust evidence and generated bundles.
- Automate close readiness, exception forecasting, and overdue trust work.
- Preserve current runtime performance by keeping ingestion, automation, and bundle assembly off the hot write path.

---

## 4. Non-Goals

- Direct money movement or bank-core execution.
- General ledger close outside trust scope.
- OCR-heavy document understanding for arbitrary bank PDFs in this phase.
- Replacing document management or e-sign infrastructure platform-wide.

---

## 5. Core Invariants

- Trust journal remains append-only and canonical.
- Reconciliation and month-close remain packet-first and reproducible.
- Bank evidence never mutates accounting truth directly; it informs matching, close, and exception workflow.
- Generated compliance artifacts must remain reproducible from canonical trust state plus stored evidence references.
- Automation must enqueue operator work instead of silently bypassing approvals or sign-off controls.

---

## 6. Workstreams

### 6.1 P6A: External Statement Intake Pipeline

Introduce first-class intake channels for statement evidence:

- uploaded statement file registry with checksum and storage metadata
- optional bank-feed/connectors metadata layer
- parser runs with normalized line import batches
- duplicate-file and supersede detection at source-file level
- parser run status and operator retry surface

Outcome:

- statement intake becomes a managed pipeline, not just a manual JSON submit path

### 6.2 P6B: Jurisdiction Close Templates and Attestations

Build jurisdiction-aware close packet overlays:

- jurisdiction packet section requirements
- responsible-lawyer attestation templates
- office/entity-level disclosure blocks
- missing-required-schedule validation before sign-off
- packet rendering profiles per jurisdiction/account type

Outcome:

- the same canonical close flow can emit regulator-appropriate packets without custom code per office

### 6.3 P6C: Trust Operations Inbox

Promote alerts/exceptions/close items into one routed workspace:

- queue views by assignee, office, jurisdiction, severity, due date
- claim/reassign/defer/escalate actions
- SLA timers and breach markers
- grouped views for close blockers vs statement blockers vs approval blockers
- action-linked audit trail and export shortcuts

Outcome:

- trust work becomes manageable as an operations system instead of scattered cards and tables

### 6.4 P6D: Chain-of-Custody and Signed Bundles

Strengthen evidence and export integrity:

- immutable evidence reference policy
- generated bundle signature/verification metadata
- export regeneration lineage
- retention policy tagging
- redaction/access controls for privileged packet components

Outcome:

- trust packets become not only complete, but also provenance-aware and defensible under review

### 6.5 P6E: Close Automation and Forecasting

Add background automation around trust close health:

- upcoming close readiness checks
- missing statement import reminders
- uncleared funds aging forecasts
- unresolved-exception trend warnings
- pre-close bundle draft generation
- close calendar automation per jurisdiction cadence

Outcome:

- operators get ahead of trust problems before month-end bottlenecks form

---

## 7. Data Model / System Additions

Likely additions:

- `TrustEvidenceFile`
- `TrustStatementParserRun`
- `TrustOpsInboxItem`
- `TrustBundleSignature`
- `TrustJurisdictionPacketTemplate`
- `TrustCloseForecastSnapshot`

Likely expansions:

- statement imports linked to evidence file lineage
- operational alerts linked to inbox item routing
- compliance export records linked to signature/retention metadata
- jurisdiction policy linked to packet template profile

---

## 8. API / Service Shape

Likely service groups:

- `TrustStatementIngestionService`
- `TrustOpsInboxService`
- `TrustBundleIntegrityService`
- `TrustCloseAutomationService`

Likely endpoints:

- `POST /api/trust/evidence/upload-manifest`
- `POST /api/trust/statements/parser-runs`
- `GET /api/trust/ops-inbox`
- `POST /api/trust/ops-inbox/{id}/claim`
- `POST /api/trust/ops-inbox/{id}/defer`
- `POST /api/trust/recovery/compliance-bundle/{id}/sign`
- `GET /api/trust/close-forecast`

Controllers should remain thin.

---

## 9. Performance Constraints

- parser runs, inbox sync, bundle signing, and forecasting must remain command/background driven
- dashboard reads should use current projections, packet summaries, and alert/read-model tables
- heavy evidence/file metadata should stay out of routine trust account queries
- new indexes should cover:
  - evidence checksum + tenant
  - inbox workflow + assignee + due date
  - parser run status + account + period
  - forecast account + jurisdiction + period

---

## 10. Rollout Plan

### P6A

- statement evidence file registry
- parser run model
- source-file-backed import path

### P6B

- jurisdiction packet template registry
- attestation block validation
- close template rendering profile

### P6C

- ops inbox item model
- assignment/defer/breach workflow
- queue UI and bulk actions

### P6D

- bundle integrity metadata
- signed/export lineage
- retention/redaction controls

### P6E

- close forecast snapshots
- background readiness jobs
- reminder/escalation automation

---

## 11. Acceptance Criteria

- Statement evidence can be traced from source file to parser run to imported statement to packet.
- Trust operators can work from an inbox without hunting across tabs for blockers.
- Jurisdiction-required packet sections block sign-off when missing.
- Generated bundles can be tied to a provenance/signature record.
- Close readiness and breach conditions can be surfaced before the end-of-month sign-off window.
