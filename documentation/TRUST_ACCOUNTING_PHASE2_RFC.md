# Trust Accounting Phase 2 RFC
## Append-Only Posting Model, Cleared Funds, and Three-Way Reconciliation

**Status:** Draft
**Owner:** Platform / Billing / Trust
**Last Updated:** 2026-04-13
**Scope:** Production trust accounting model transition after Phase 1 monetary correctness and idempotency hardening

---

## 1. Problem Statement

Phase 0 and Phase 1 make the trust subsystem materially safer:

- write logic moved out of controllers
- maker-checker rules exist
- role matrix is configurable
- risk preflight is enforced on critical commands
- trust money is moving to `decimal`
- append-only journal entries exist
- idempotency and optimistic concurrency are in place for trust commands

That is enough to stabilize an alpha or controlled pilot. It is not yet enough for production-grade trust accounting.

The core remaining issue is model shape:

- `TrustTransaction` still behaves as both workflow record and accounting record
- cached balances are still too close to source-of-truth semantics
- reconciliation is still too simplified for real monthly trust operations
- nominal balance and disbursable balance are still not fully separated
- statement / outstanding item state is not first-class
- operational UI does not yet surface the controls compliance users actually need

Phase 2 formalizes the real accounting model.

---

## 2. Goals

- Make `TrustJournalEntry` the accounting source of truth.
- Make `TrustTransaction` a workflow/request envelope, not the posted accounting fact.
- Replace balance rewind semantics with linked reversal entries.
- Introduce explicit cleared-funds and available-to-disburse accounting state.
- Add first-class bank statement and outstanding item primitives required for real three-way reconciliation.
- Generate monthly reconciliation packets with sign-off support.
- Preserve strong runtime performance through projections, snapshots, and targeted indexes.
- Expose operationally necessary trust data in the UI instead of keeping it implicit in backend logic.

---

## 3. Non-Goals

Out of scope for Phase 2:

- direct bank API integrations for every financial institution
- OCR ingestion of arbitrary scanned statements
- ACH/wire execution orchestration
- multi-firm external accounting migration tooling
- full jurisdiction-specific expert system for every state bar rule
- cosmetic UI redesign not tied to trust operations

Phase 2 is an accounting model and compliance-surface phase, not a banking product rewrite.

---

## 4. Current State Summary

After Phase 1, the system has:

- `TrustAccountingService` as the write orchestration layer
- `TrustTransaction` approval workflow
- `TrustJournalEntry` posting and reversal support
- cached balances on:
  - `TrustBankAccount.CurrentBalance`
  - `ClientTrustLedger.RunningBalance`
- idempotent trust command handling
- trust risk preflight hooks

This is a strong base, but the boundaries are still too soft:

- a trust transaction can still be mentally interpreted as "the posting"
- balances are still user-visible without a fully explicit source-of-truth hierarchy
- deposits do not yet model `pending_clearance -> cleared -> returned`
- reconciliation lacks:
  - statement lines
  - cleared/uncleared item linkage
  - outstanding checks
  - outstanding deposits
  - lawyer sign-off packet

---

## 5. Proposed Phase 2 Architecture

Phase 2 formalizes a three-layer accounting model.

### 5.1 Layer A: Workflow / Request Layer

`TrustTransaction` becomes the workflow envelope.

Responsibilities:

- request metadata
- creator / approver / reject / void lifecycle
- command intent type
- reason / notes / policy references
- correlation to posted batch
- risk evaluation references

It should not be treated as the primary accounting source of truth.

### 5.2 Layer B: Accounting Source of Truth

`TrustJournalEntry` becomes the immutable posted record.

Every monetary effect must be represented as one or more journal rows.

Responsibilities:

- immutable posted amount
- posting direction or signed amount
- trust account reference
- client trust ledger reference
- matter reference
- posting batch / correlation key
- effective timestamp
- reversal linkage
- clearance classification where applicable

### 5.3 Layer C: Projections / Snapshots

Keep fast projections for runtime UX:

- `TrustBankAccount.CurrentBalance`
- `TrustBankAccount.ClearedBalance`
- `TrustBankAccount.UnclearedBalance`
- `ClientTrustLedger.RunningBalance`
- `ClientTrustLedger.AvailableToDisburse`
- reconciliation summary snapshots

These become derived/cache state only.

The recovery rule is:

- if projection and journal ever diverge, journal wins

---

## 6. Domain Model Changes

### 6.1 TrustTransaction Becomes Workflow-Only

Recommended semantics:

- `TrustTransaction.Type` = intent/request type
- `TrustTransaction.Status` = workflow state
- `TrustTransaction.Amount` = requested amount
- `TrustTransaction` may point to:
  - `PostingBatchId`
  - `PrimaryJournalEntryId`
  - `ClearingStatus`
  - `VoidOfTrustTransactionId` only for workflow traceability

But accounting truth is not read from this table.

### 6.2 TrustJournalEntry Becomes Canonical

Recommended minimum fields:

- `Id`
- `TrustTransactionId`
- `PostingBatchId`
- `TrustAccountId`
- `ClientTrustLedgerId`
- `MatterId`
- `EntryKind`
  - `posting`
  - `reversal`
  - `adjustment`
  - `clearance`
  - `return`
- `OperationType`
  - `deposit`
  - `withdrawal`
  - `earned_fee_transfer`
  - `refund`
  - `correction`
- `Amount`
- `Currency`
- `EffectiveAt`
- `AvailabilityClass`
  - `nominal`
  - `uncleared`
  - `cleared`
- `ReversalOfTrustJournalEntryId`
- `CorrelationKey`
- `CreatedBy`
- `CreatedAt`

### 6.3 New Supporting Entities

Recommended additions:

- `TrustPostingBatch`
  - groups all journal rows emitted by one command
- `TrustDepositClearance`
  - represents lifecycle from receipt to cleared/returned
- `TrustStatementImport`
  - import session metadata
- `TrustStatementLine`
  - raw bank statement line items
- `TrustOutstandingItem`
  - uncleared/outstanding checks and deposits
- `TrustReconciliationPacket`
  - monthly three-way rec packet
- `TrustReconciliationSignoff`
  - designated lawyer approval/sign-off

---

## 7. Posting Rules

### 7.1 Create vs Post

Commands should be split conceptually:

- create request
- approve request
- post accounting entries

Approval can trigger posting, but posting output is journal-only.

### 7.2 Deposit Rule

Deposit request lifecycle:

- `PENDING`
- `APPROVED`
- `POSTED_RECEIVED`
- `PENDING_CLEARANCE`
- `CLEARED`
- `RETURNED`

Initial approved deposit should create journal entries as `uncleared` unless policy says same-day cash-equivalent clearance applies.

### 7.3 Withdrawal Rule

Withdrawal approval must:

- run risk preflight
- validate maker-checker
- validate role policy
- validate available-to-disburse, not only nominal balance
- create journal entries as the only monetary record
- update projections in the same DB transaction

### 7.4 Earned Fee Transfer Rule

Earned fee transfer must become a first-class trust operation, not just a billing side-effect.

Requirements:

- invoice/billing basis exists
- trust source client ledger is explicit
- available cleared funds are sufficient
- trust risk preflight is blocking
- journal emits trust-side reduction rows
- operating/billing evidence rows are linked by correlation key

---

## 8. Reversal Model

### 8.1 Void Semantics

Void should no longer mean "move cached balance backwards and call it done."

New rule:

- original journal entries remain immutable
- void creates linked reversal journal entries
- workflow record changes state to `VOIDED`
- projection rows are updated from reversal entries

### 8.2 Reversal Guarantees

Each reversal must preserve:

- `ReversalOfTrustJournalEntryId`
- reason
- actor
- timestamp
- batch correlation

This gives an audit trail that can be explained without ambiguity.

### 8.3 No Hard Delete

Phase 2 should explicitly prohibit hard deletion of posted trust accounting rows outside tightly controlled maintenance tooling.

---

## 9. Cleared Funds Model

### 9.1 Why This Matters

Production trust accounting cannot treat "balance exists" as "money can leave the account."

We need separation between:

- nominal received balance
- uncleared balance
- cleared balance
- available-to-disburse

### 9.2 Recommended Ledger Fields

Per client trust ledger:

- `RunningBalance`
- `UnclearedBalance`
- `ClearedBalance`
- `AvailableToDisburse`
- `HoldAmount`

Per trust bank account:

- `CurrentBalance`
- `UnclearedBalance`
- `ClearedBalance`
- `AvailableDisbursementCapacity`

### 9.3 Clearance Events

Clearance should be represented as journal or journal-linked events:

- deposit received
- deposit cleared
- deposit returned / bounced

This allows historical replay and accurate as-of reporting.

---

## 10. Three-Way Reconciliation Model

### 10.1 Required Packet

A real monthly trust reconciliation packet should compare:

- bank statement ending balance
- trust account journal balance as of statement date
- sum of individual client trust ledgers as of statement date

### 10.2 Required Supporting Items

We also need:

- cleared statement lines
- matched journal rows
- outstanding checks
- outstanding deposits
- unexplained variances
- month-end exception notes
- designated responsible lawyer sign-off

### 10.3 As-Of Date Rules

Every reconciliation query must be date-aware.

Do not rely on present-day cached balances for month-end compliance reporting.

Required support:

- journal replay or snapshot-as-of
- cleared/uncleared state as of period end
- outstanding item aging

### 10.4 Recommended Packet Structure

`TrustReconciliationPacket` should include:

- packet id
- trust account id
- statement period start/end
- imported bank statement balance
- adjusted bank balance
- journal balance as-of period end
- client ledger total as-of period end
- outstanding checks summary
- outstanding deposits summary
- discrepancies
- prepared by
- prepared at
- signed off by
- signed off at
- packet payload JSON for export reproducibility

---

## 11. UI / Operational Surface Requirements

The trust UI should stop being purely summary-oriented and start surfacing operational control boxes.

### 11.1 Overview Additions

Add these first-class widgets:

- Available to Disburse
- Uncleared Funds
- Pending Approval Queue
- Open Holds / Compliance Alerts
- Outstanding Checks
- Outstanding Deposits
- Last Three-Way Reconciliation Date
- Designated Responsible Lawyer
- Statement Import Status

### 11.2 Tab / Screen Additions

Existing tabs remain directionally correct:

- Overview
- Accounts
- Client Ledgers
- Transactions
- Reconciliation
- Audit Log

Add or expand:

- Statement Imports
- Outstanding Items
- Approval Queue
- Exception Queue
- Sign-Off History

### 11.3 Workflow UX

Users should be able to distinguish:

- request created
- approved but not yet cleared
- posted and cleared
- reversed / voided
- held for review

This distinction must exist both in backend state and in UI language.

---

## 12. Performance Guardrails

Phase 2 changes the accounting model, but it must not make the product feel slower.

### 12.1 Core Rule

Journal is source of truth. Projections are performance optimization.

We keep both.

### 12.2 Read Path Strategy

Use:

- indexed projection tables/columns for dashboard reads
- reconciliation snapshots for historical packet retrieval
- narrow covering indexes on:
  - `TrustJournalEntry (TenantId, TrustAccountId, EffectiveAt)`
  - `TrustJournalEntry (TenantId, ClientTrustLedgerId, EffectiveAt)`
  - `TrustJournalEntry (TenantId, CorrelationKey)`
  - `TrustStatementLine (TenantId, TrustAccountId, StatementDate)`
  - `TrustOutstandingItem (TenantId, TrustAccountId, Status, ItemDate)`

### 12.3 Write Path Strategy

Each posting command should do one DB transaction containing:

- workflow update
- journal insert(s)
- projection update(s)
- idempotency mark
- audit enqueue/write

No post-commit balance correction jobs should be required for correctness.

### 12.4 Recovery Strategy

Provide internal repair tooling:

- rebuild trust projections from journal
- rebuild reconciliation snapshots for a date range
- validate projection vs journal drift

This is a trust system requirement, not a nice-to-have.

---

## 13. Risk Radar Integration

Phase 2 should deepen, not relax, risk behavior.

### 13.1 Preflight Must Stay Blocking

Still required before:

- withdrawal approval
- void/reversal
- earned fee transfer
- manual adjustment

### 13.2 New Signals

Risk radar should gain awareness of:

- uncleared-funds disbursement attempts
- unusual reversal frequency
- repeated returned deposits
- approval immediately after creation at unusual times
- frequent outstanding item aging breaches
- repeated lawyer sign-off delays

### 13.3 Hold Semantics

Holds should attach to workflow commands before posting and optionally to posted items for investigation, but they must never mutate posted journal history silently.

---

## 14. Authorization / Responsibility Model

Phase 2 should introduce the real operational role split.

Recommended default posture:

- `Attorney / Partner`
  - approve disbursements
  - release holds
  - sign off reconciliation packets
- `Bookkeeper / Accountant`
  - create requests
  - import statements
  - prepare reconciliation packets
- `Associate`
  - view
  - create requests where policy allows
  - no direct disbursement approval by default
- `Admin / Compliance`
  - configure policy
  - monitor exceptions
  - audit/report access

Also add:

- designated responsible lawyer at trust account level
- allowed signatories per trust account
- jurisdiction/account-type policy hooks

---

## 15. Migration Strategy

Phase 2 should not be delivered as one giant cutover.

### 15.1 Phase 2A: Canonical Posting Boundary

- treat `TrustJournalEntry` as canonical source of truth
- keep current projections
- add posting batch and reversal linkage
- stop reading accounting truth from `TrustTransaction`

### 15.2 Phase 2B: Cleared Funds

- add deposit clearance lifecycle
- add uncleared/cleared/available balances
- block disbursement on uncleared funds

### 15.3 Phase 2C: Reconciliation Backbone

- add statement import entities
- add outstanding item matching
- add three-way reconciliation packet generation
- add sign-off workflow

### 15.4 Phase 2D: Operational Surface

- surface exception queue
- surface pending approvals
- surface statement and sign-off status
- expose responsible lawyer and account operational metadata

---

## 16. Rollout Plan

### 16.1 Feature Flags

Recommended flags:

- `TrustAccounting:JournalIsCanonical`
- `TrustAccounting:ClearedFundsEnabled`
- `TrustAccounting:ThreeWayReconciliationEnabled`
- `TrustAccounting:TrustOpsUiEnabled`

### 16.2 Deployment Order

1. ship additive schema
2. dual-write journal + projections
3. verify projection rebuild correctness
4. switch reads for trust accounting reports to journal/snapshots
5. enable cleared-funds blocking
6. enable reconciliation packet/sign-off UX

### 16.3 Success Metrics

- zero duplicate posted trust movements under retry
- zero projection/journal drift in validation jobs
- withdrawal attempts on uncleared funds correctly blocked
- reconciliation packet generation under acceptable latency
- no material p95 regression on trust dashboard read paths

---

## 17. Testing Strategy

Required test coverage:

- journal-only posting correctness
- reversal linkage correctness
- projection rebuild from journal
- idempotent replay for all monetary commands
- cleared-funds blocking behavior
- returned deposit behavior
- as-of reconciliation correctness
- statement line matching and outstanding item aging
- sign-off workflow and permission checks

Also add deterministic fixtures for:

- uncleared deposit then withdrawal attempt
- returned deposit after prior receipt
- month-end outstanding check
- split-ledger deposit allocations
- earned-fee transfer with insufficient cleared funds

---

## 18. Open Questions

- Should clearance transitions be stored as separate journal events, separate clearance table rows, or both?
- Should reconciliation packets be regenerated on demand, snapshotted at close, or both?
- Do we require dual approval thresholds in Phase 2 or leave them to Phase 3 policy hardening?
- Should attorney sign-off be one-step or preparer + reviewer + lawyer final sign-off?
- Do we need jurisdiction-specific trust account types now, or can we start with generic `IOLTA` / `non-IOLTA`?

---

## 19. Recommended Implementation Order

Recommended execution order:

1. make journal canonical and formalize workflow vs posting boundary
2. finish linked reversal-only void semantics
3. add posting batch and projection rebuild tooling
4. add cleared/uncleared/available balances
5. formalize earned-fee transfer as first-class trust operation
6. add statement import and outstanding item matching
7. add reconciliation packet and sign-off
8. expose trust operations UI boxes and exception queues

This order keeps the hardest correctness work first and the user-facing surfacing work later.

---

## 20. Recommendation

Phase 2 should be approved as the model-transition phase that turns trust accounting from "safe workflow with better posting discipline" into "production trust ledger architecture."

The critical posture for Phase 2 is:

- immutable journal first
- projections second
- workflow separate from accounting
- cleared funds explicit
- reconciliation date-aware and packetized
- operational responsibility visible in both code and UI

That is the line between a promising trust module and a production-trust subsystem.
