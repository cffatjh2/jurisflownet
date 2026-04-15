# Trust Accounting Phase 3 RFC

Status: Draft
Owner: JurisFlow trust/accounting track
Last updated: 2026-04-13

---

## 1. Purpose

Phase 2 moved trust accounting to a journal-first runtime with cleared-funds controls, reconciliation packets, operational UI, and first-class earned-fee transfers.

Phase 3 is the production-hardening layer.

Goal: make the trust subsystem defensible for real operational use by introducing jurisdiction-aware policy, formal approval/signatory controls, monthly close discipline, and regulator-ready exports without regressing the low-latency read/write path established in Phase 2.

---

## 2. Problem Statement

The current trust stack now has the right accounting spine, but it is still missing the controls that make a real trust operation governable:

- responsible lawyer / signatory ownership is not explicit
- approval rules are role-based but not policy-threshold-based
- account type and jurisdiction rules are not first-class
- reconciliation is packetized, but not yet a full month-end operating workflow
- exception queues and compliance exports are still too thin for a real audit posture
- statement lifecycle is present, but matching and close discipline are not yet strong enough

This means the system is structurally correct, but not yet operationally complete.

---

## 3. Phase 3 Goals

- Make trust accounts policy-driven instead of mostly role-driven.
- Introduce designated responsible lawyer and signatory governance.
- Formalize disbursement classes and threshold-based approval policy.
- Turn reconciliation into a month-end close workflow, not only a packet generator.
- Produce regulator-ready exports for account journal, client ledger, approvals, reversals, and reconciliation sign-off.
- Surface exception operations clearly enough that accounting/compliance staff can run the system daily.
- Preserve Phase 2 performance characteristics: projections remain the main read path and journal remains the accounting source of truth.

---

## 4. Non-Goals

- Direct bank-core integration or money movement execution.
- ACH/check printing orchestration.
- Multi-bank reconciliation automation with third-party vendors.
- Replacing billing AR logic or general ledger accounting outside trust scope.

---

## 5. Core Invariants

- Journal remains append-only and canonical.
- Projections remain derived and rebuildable.
- Every trust disbursement decision remains explainable from policy + audit trail.
- Close workflow remains date-aware and reproducible for any as-of period.
- No Phase 3 feature may require expensive journal scans on routine dashboard reads.

---

## 6. Workstreams

### 6.1 P3A: Account Governance and Jurisdiction Policy

Introduce explicit account governance on `TrustBankAccount`:

- `AccountType`: `iolta | non_iolta`
- `ResponsibleLawyerUserId`
- `AllowedSignatoriesJson`
- `JurisdictionPolicyKey`
- `StatementCadence`
- `OverdraftNotificationEnabled`
- `BankReferenceMetadataJson`

Introduce jurisdiction policy entities/config:

- approval thresholds
- dual-approval thresholds
- cleared-funds waiting rules
- required monthly close cadence
- sign-off requirements
- overdraft/escalation rules
- exception aging SLA

Outcome:

- trust behavior becomes policy-configurable per jurisdiction/entity instead of hard-coded
- the system can distinguish “allowed by role” from “allowed by trust policy”

### 6.2 P3B: Disbursement Policy and Approval Hardening

Formalize trust disbursement classes:

- `client_disbursement`
- `settlement_payout`
- `third_party_payment`
- `earned_fee_transfer`
- `cost_reimbursement`
- `refund`

Add command policy checks before posting:

- creator cannot approve own disbursement where maker-checker is required
- threshold-based dual approval
- signatory requirement for certain disbursement classes
- mandatory reason on overrides
- high-risk disbursement auto-hold until release

Add explicit approval objects:

- `TrustApprovalRequirement`
- `TrustApprovalDecision`
- `TrustApprovalOverride`

Outcome:

- approvals become transaction-policy-driven instead of a single generic approve action
- the system can answer why a disbursement posted, who approved it, and under which policy

### 6.3 P3C: Monthly Close and Reconciliation Operations

Turn reconciliation into a full close workflow:

- statement import window
- outstanding check/deposit review
- exception queue resolution
- packet preparation
- reviewer sign-off
- responsible lawyer sign-off
- close lock for the period

Add period-close entities:

- `TrustMonthClose`
- `TrustMonthCloseStep`
- `TrustExceptionQueueItem`

Strengthen reconciliation data model:

- cleared/uncleared aging
- outstanding item age buckets
- statement match confidence
- packet completeness checks
- close status per account

Outcome:

- monthly trust operations become repeatable and visible
- close discipline stops being implicit in user behavior

### 6.4 P3D: Compliance Exports and Audit Packs

Add export surfaces for:

- account journal
- client ledger cards
- disbursement approvals
- reversal linkage chains
- outstanding item register
- monthly reconciliation packet
- hold/release timeline
- policy overrides

Exports should support:

- PDF packet
- CSV/Excel operational export
- machine-readable JSON for internal audit tooling

Each export should carry:

- firm name / office / entity
- responsible lawyer
- prepared by / signed by
- generated timestamp
- as-of period
- source account + jurisdiction metadata

Outcome:

- downloadable output stops looking generic
- trust exports become presentation-complete and audit-usable

### 6.5 P3E: Operational Queue Surface

Add first-class trust operations queues:

- pending approvals
- pending clearance
- unresolved exceptions
- missing statement imports
- unsigned reconciliation packets
- overdue month close items
- policy violations / escalations

UI should support:

- ownership
- due dates
- severity
- quick actions
- export shortcuts

Outcome:

- operations staff do not have to infer work from scattered tabs
- trust becomes runnable as a process, not only inspectable as data

---

## 7. Data Model Additions

Likely additive entities:

- `TrustJurisdictionPolicy`
- `TrustAccountGovernance`
- `TrustApprovalRequirement`
- `TrustApprovalDecision`
- `TrustApprovalOverride`
- `TrustMonthClose`
- `TrustMonthCloseStep`
- `TrustExceptionQueueItem`
- `TrustComplianceExport`

Likely additive columns:

- `TrustBankAccount.AccountType`
- `TrustBankAccount.ResponsibleLawyerUserId`
- `TrustBankAccount.AllowedSignatoriesJson`
- `TrustTransaction.DisbursementClass`
- `TrustTransaction.PolicyDecisionJson`
- `TrustTransaction.MonthCloseId`
- `TrustReconciliationPacket.CloseStatus`

All additions should be backward-compatible and rollout-safe.

---

## 8. API Plan

New or expanded API groups:

- `GET/POST /api/trust/policies`
- `GET/POST /api/trust/accounts/{id}/governance`
- `GET /api/trust/approvals`
- `POST /api/trust/transactions/{id}/approve-step`
- `POST /api/trust/transactions/{id}/override`
- `GET /api/trust/month-close`
- `POST /api/trust/month-close/prepare`
- `POST /api/trust/month-close/{id}/signoff`
- `GET /api/trust/exceptions`
- `POST /api/trust/exceptions/{id}/resolve`
- `GET /api/trust/exports/*`

Controllers should remain thin.

All write paths should continue to terminate in `TrustAccountingService` or a tightly-related trust domain service, not in controller logic.

---

## 9. Performance Strategy

Phase 3 must not regress the runtime gains already achieved.

Rules:

- projections remain the primary dashboard read model
- queues read from indexed projection/exception tables, not raw journal scans
- close packet generation may scan journal/snapshots, but background or on-demand only
- exports should reuse packet snapshots where possible
- policy evaluation should be cached per account/policy version for request scope

Guardrails:

- no new per-row N+1 approval or export lookups in transaction lists
- no background job required for correctness of monetary posting
- no synchronous regulator packet build on hot dashboard route

---

## 10. Rollout Plan

### P3A rollout

- ship additive governance/policy schema
- default existing accounts into a baseline jurisdiction policy
- keep existing role matrix as fallback until policy assignment is complete

### P3B rollout

- shadow-evaluate approval policy first
- record what would have been blocked/escalated
- then enable blocking for selected disbursement classes

### P3C rollout

- introduce month-close workflow in parallel with existing reconciliation packet flow
- migrate packets into close objects after validation

### P3D/P3E rollout

- expose exports and queues after policy/close state is stable
- enable overdue alerts only after queue ownership is configured

---

## 11. Test Plan

Required coverage:

- jurisdiction policy selection
- dual approval threshold behavior
- creator/approver/signatory separation
- override + reason audit chain
- month-close progression and lock behavior
- packet sign-off gating
- exception aging and escalation
- export completeness and metadata
- rebuildability of projections after Phase 3 additions
- no p95 regression on trust dashboard/account list routes

---

## 12. Suggested Execution Order

1. P3A governance schema + policy resolution
2. P3B approval/disbursement policy engine
3. P3C month-close workflow
4. P3D compliance export packet layer
5. P3E operational queue UI

This order keeps correctness first, then operational discipline, then presentation/export polish.

---

## 13. Detailed Delivery Plan

### 13.1 P3A Delivery Slice: Governance Foundation

Scope:

- add account governance fields
- add jurisdiction policy entity/config
- resolve effective policy per trust account
- expose governance read/write endpoints

Backend deliverables:

- policy resolution service with request-scope caching
- account governance validation rules
- migration for additive governance columns/tables
- audit events for governance changes

UI deliverables:

- account governance drawer/modal
- responsible lawyer selector
- signatory list editor
- policy assignment/status chip

Acceptance:

- every active trust account can resolve one effective policy
- governance changes are audited
- existing accounts fall back to baseline policy without breaking current flows

### 13.2 P3B Delivery Slice: Approval and Disbursement Policy

Scope:

- add disbursement class
- add threshold-based approval requirements
- add dual approval and override chain
- separate signatory approval from operational approval when required

Backend deliverables:

- approval requirement generator
- approval decision persistence
- override command path with reason enforcement
- policy evaluation hooks before posting

UI deliverables:

- approval queue
- step-by-step approval timeline
- override modal with mandatory rationale
- signatory state badges on transaction rows

Acceptance:

- transactions above threshold cannot post without required approvals
- creator cannot satisfy approver requirement when maker-checker applies
- override path is fully audit-linked

### 13.3 P3C Delivery Slice: Month-End Close

Scope:

- formalize monthly close object
- attach reconciliation packet, outstanding review, and sign-off progression
- add close status and close lock

Backend deliverables:

- month-close aggregate root
- close progression command set
- missing-step validation
- period close / reopen rules

UI deliverables:

- month-close workspace per account
- checklist progress UI
- unresolved exception blockers
- close sign-off panel

Acceptance:

- a close cannot sign off while required artifacts are incomplete
- close state is reproducible for a prior period
- close lock prevents post-close operational drift unless reopened by policy

### 13.4 P3D Delivery Slice: Compliance Exports

Scope:

- generate operational and regulator-facing exports
- package close artifacts into reusable audit bundles

Backend deliverables:

- export builder service
- reusable packet templates
- export metadata/watermarking
- stored export history

UI deliverables:

- export actions from account, ledger, close, and packet screens
- branded headers / attribution fields
- export history list with generated timestamps

Acceptance:

- exported trust packet includes firm/account/responsible-lawyer attribution
- exports are reproducible for the same period and source packet

### 13.5 P3E Delivery Slice: Operations Queue

Scope:

- make trust exceptions runnable as a queue
- surface aging and overdue work

Backend deliverables:

- indexed queue projection
- ownership fields
- SLA/aging calculator
- queue summary endpoint

UI deliverables:

- approvals queue
- exceptions queue
- missing-signoff queue
- overdue month-close queue

Acceptance:

- staff can identify the next blocking trust action from one screen
- queue reads stay projection-based and do not require full journal scans

---

## 14. Dependency Rules

- P3B depends on P3A effective policy resolution.
- P3C depends on P3A governance because responsible lawyer/signatory rules must already exist.
- P3D depends on P3C packet/close state for final export composition.
- P3E can start partially in parallel with P3C, but final queue semantics depend on close state and approval state.

Recommended overlap:

- build P3A schema + service first
- begin P3B UI only after policy read path stabilizes
- begin P3D export formatting while P3C backend close aggregate is stabilizing

---

## 15. Performance Guardrails

Target budgets:

- trust dashboard/account list reads must remain projection-backed
- approval queue reads should stay under current trust list latency envelope
- month-close preparation may be heavier, but should be explicit user-triggered work
- exports should snapshot once and reuse packet state where possible

Required instrumentation:

- policy resolution timing
- approval preflight timing
- queue query timing
- close packet generation timing
- export generation timing

Failure policy:

- if a Phase 3 read path requires journal scan on the hot UI route, it is not production-ready
- if approval evaluation causes material regression on trust write endpoints, cache or precompute policy inputs

---

## 16. Rollout and Feature Flags

Use separate rollout flags:

- `trust.phase3.governance`
- `trust.phase3.approvals`
- `trust.phase3.monthClose`
- `trust.phase3.exports`
- `trust.phase3.opsQueues`

Rollout sequence:

1. dark-launch schema + write-side population
2. shadow-read / shadow-policy evaluation
3. enable UI visibility for internal admins
4. enable blocking behavior for selected tenants/accounts
5. generalize after telemetry review

---

## 17. Test Matrix

Unit/integration:

- policy resolution precedence
- approval rule generation
- signatory enforcement
- override audit linkage
- month-close state transitions
- export packet completeness
- queue aging calculations

Scenario/end-to-end:

- high-value settlement payout requiring dual approval
- earned-fee transfer with policy threshold
- missing statement import blocking close
- packet signed by reviewer but not responsible lawyer
- reopened close after exception discovery

Performance:

- no meaningful p95 regression on trust list and queue routes
- month-close and export operations complete within acceptable operator workflow bounds

---

## 18. Open Questions

- Which jurisdictions need first-class policy packs on day one?
- Should signatory policy live per account, per entity, or both?
- Do earned-fee transfers require dual approval everywhere or only above thresholds?
- Should month-close locks be trust-only or integrated with billing period locks?
- Which export formats are mandatory at launch versus nice-to-have?

---

## 19. Exit Criteria

Phase 3 is complete when:

- every trust disbursement can be explained by policy, approval trail, and journal lineage
- every trust account has explicit governance metadata
- monthly close can be prepared, signed, and locked in-system
- exception queues and overdue close items are visible in the UI
- audit/compliance exports are presentation-complete and attributable
- read/write latency remains within current acceptable trust accounting bounds
