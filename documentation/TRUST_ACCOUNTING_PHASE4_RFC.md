# Trust Accounting Phase 4 RFC

Status: Draft
Owner: JurisFlow trust/accounting track
Last updated: 2026-04-14

---

## 1. Purpose

Phase 3 made trust accounting policy-driven, approval-aware, and exportable.

Phase 4 makes reconciliation canonical.

Goal: leave exactly one reconciliation pipeline in the product, then harden that pipeline with statement import, line matching, cleared/uncleared tracking, outstanding item discipline, month-end packet assembly, and lawyer sign-off without regressing the low-latency write/read path already established.

---

## 2. Problem Statement

The trust subsystem now has most of the ingredients for reconciliation, but they still behave too much like adjacent features instead of one controlled operational line:

- statement imports exist, but import validation and line lifecycle are still thin
- cleared/uncleared state exists, but bank-statement evidence is not yet the single reconciliation driver
- outstanding checks/deposits exist, but auto-match and manual exception workflow are not yet strict enough
- packets and month-close exist, but the system still tolerates parallel ways of “being reconciled”
- lawyer sign-off exists, but it is not yet the explicit final gate of one canonical monthly close chain

Result: the accounting model is stronger than before, but reconciliation still needs one authoritative operating path.

---

## 3. Phase 4 Goals

- Collapse reconciliation onto one canonical packet-first/month-close-first pipeline.
- Treat statement import as first-class evidence, not optional metadata.
- Introduce deterministic statement line matching against trust journal / posted transactions.
- Formalize cleared vs uncleared behavior from statement evidence and outstanding-item resolution.
- Produce a regulator-ready month-end packet with supporting schedules.
- Make responsible-lawyer sign-off the explicit end-state for a closed reconciliation period.
- Preserve performance by keeping matching/packet assembly off the hot dashboard write path.

---

## 4. Non-Goals

- Direct bank feed sync in this phase.
- OCR/AI-led statement parsing beyond structured import validation.
- General-ledger accounting outside trust scope.
- Cross-bank treasury operations or payment execution.

---

## 5. Core Invariants

- Trust journal remains append-only and canonical for accounting truth.
- Reconciliation results remain derived, reproducible, and rebuildable.
- Only one packet per trust account + period can be canonical/current.
- Lawyer sign-off must never mutate journal truth; it only closes the reconciliation workflow.
- Matching, outstanding classification, and packet generation must be rerunnable without duplicate side effects.
- Heavy matching work must stay off routine trust dashboard reads.

---

## 6. Canonical Reconciliation Line

Phase 4 standardizes the lifecycle to:

1. Statement import opened for account + period
2. Statement lines validated and normalized
3. Matching run against posted trust activity
4. Unmatched items classified as outstanding deposit, outstanding check, or manual adjustment
5. Packet generated from statement + journal + client ledgers + outstanding items
6. Reviewer sign-off recorded
7. Responsible lawyer sign-off recorded
8. Period closed as the canonical reconciliation state

Any legacy/parallel reconciliation surface should be downgraded to read-only or redirected into this line.

---

## 7. Workstreams

### 7.1 P4A: Canonical Pipeline Consolidation

- mark one reconciliation packet as canonical per account/period
- route legacy reconciliation actions into packet/month-close commands
- remove ambiguous “reconciled” shortcuts outside packet close
- add packet state machine:
  - `draft`
  - `matching_in_progress`
  - `needs_review`
  - `ready_for_signoff`
  - `reviewer_signed`
  - `lawyer_signed`
  - `closed`

Outcome:

- the product stops having multiple competing reconciliation truths

### 7.2 P4B: Statement Import and Line Matching

Introduce a stronger statement import model:

- import header:
  - account id
  - period start / period end
  - source
  - ending balance
  - imported by / imported at
- line model:
  - posted date
  - amount
  - bank reference
  - memo / description
  - candidate transaction id
  - match status
  - confidence
  - cleared outcome

Matching rules:

- exact amount + reference + date window
- exact amount + date window + account scope
- manual override for disputed matches
- idempotent rerun support

Outcome:

- statement evidence becomes structured and matchable instead of passive

### 7.3 P4C: Cleared / Uncleared and Outstanding Discipline

Formalize reconciliation-side state:

- `matched_cleared`
- `unmatched_outstanding_check`
- `unmatched_deposit_in_transit`
- `manual_adjustment`
- `rejected_match`

Rules:

- only matched statement evidence can move an item to cleared in reconciliation context
- unresolved unmatched withdrawals become outstanding checks
- unresolved unmatched deposits become deposits in transit
- manual adjustments require notes and actor identity

Outcome:

- cleared/uncleared status is tied to bank evidence, not inferred loosely

### 7.4 P4D: Month-End Packet and Lawyer Sign-Off

Build one final packet object containing:

- packet header
- statement summary
- journal summary
- client ledger total
- outstanding checks schedule
- deposits in transit schedule
- manual adjustments schedule
- exception summary
- reviewer sign-off
- responsible lawyer sign-off
- generated export metadata

Lawyer sign-off rules:

- creator cannot self-sign final packet where maker-checker applies
- lawyer sign-off requires reviewer step completion
- closed packet becomes immutable except for explicit supersede/reopen flow

Outcome:

- reconciliation produces one defensible month-end artifact

### 7.5 P4E: Performance and Rollout Safety

- matching runs behind explicit command endpoints, not every dashboard read
- packet summaries cached/derived onto packet records
- add indexes for:
  - statement line account/date
  - statement line match status
  - packet account/period/status
  - outstanding item account/period/status
- add idempotency keys to import/match/generate/sign-off commands where missing
- keep existing projection reads intact

Outcome:

- reconciliation gets deeper without slowing trust operations generally

---

## 8. Data Model Additions / Changes

Likely additions:

- `TrustStatementImportLine`
- `TrustStatementMatchDecision`
- `TrustReconciliationMatchRun`
- `TrustReconciliationPacketVersion` or supersede metadata

Likely column additions:

- `TrustReconciliationPacket.IsCanonical`
- `TrustReconciliationPacket.ReviewerSignedAt`
- `TrustReconciliationPacket.ResponsibleLawyerSignedAt`
- `TrustOutstandingItem.StatementLineId`
- `TrustOutstandingItem.ResolutionStatus`
- `TrustTransaction.ClearedSource`

Likely constraints:

- unique `(TenantId, TrustAccountId, PeriodStart, PeriodEnd, IsCanonical=true)`
- packet close cannot bypass lawyer sign-off

---

## 9. API / Service Shape

Trust controller should remain thin. Command/read services should expose:

- `ImportStatementAsync`
- `RunStatementMatchingAsync`
- `ApplyMatchDecisionAsync`
- `GenerateCanonicalPacketAsync`
- `SignoffPacketAsync`
- `CloseReconciliationPeriodAsync`
- `GetReconciliationWorkspaceAsync`

Read models should serve:

- packet summary
- matching queue
- outstanding schedules
- sign-off state
- export history

---

## 10. Rollout Plan

### P4A

- canonical packet state machine
- redirect legacy reconciliation entry points

### P4B

- statement line model
- matching engine
- manual match override

### P4C

- outstanding classification
- cleared-source enforcement

### P4D

- month-end packet assembly
- reviewer + lawyer sign-off hardening
- final PDF/JSON packet export refinements

### P4E

- migration cleanup
- indexes
- backfill
- operational metrics

---

## 11. Acceptance Criteria

- For any trust account/period, the system can point to exactly one canonical reconciliation packet.
- A packet can be regenerated and re-matched without duplicating accounting effects.
- Cleared status can be explained from statement evidence or explicit outstanding classification.
- Lawyer sign-off is recorded, queryable, and required before final close.
- Operators can export one complete month-end packet with schedules and sign-off chain.
- Dashboard latency stays on projection/cached reads; matching work remains command-driven.
