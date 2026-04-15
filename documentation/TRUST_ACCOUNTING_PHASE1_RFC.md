# Trust Accounting Phase 1 RFC
## Monetary Correctness, Journal Integrity, and Reconciliation Hardening

**Status:** Draft
**Owner:** Platform / Billing / Trust
**Last Updated:** 2026-04-13
**Scope:** Production-grade accounting integrity after Phase 0 command-layer hardening

---

## 1. Problem Statement

Phase 0 makes the trust write path structurally safer by removing controller-owned mutations, auto-approval, and same-user approval. That is necessary, but it is not enough for production trust accounting.

The remaining material risks are accounting and audit risks:

- trust money still uses `double` in core models
- posted balances are still mutable primary state instead of journal-derived state
- void behavior still behaves like in-place balance rewinding instead of explicit reversal accounting
- concurrency and retry safety are not yet strong enough for trust-grade financial posting
- "balance exists" is still too close to "disbursable balance"
- reconciliation is still a simplified balance comparison, not true monthly three-way reconciliation

If Phase 0 is the go-live blocker removal phase, Phase 1 is the accounting correctness phase.

---

## 2. Goals

- Standardize trust money to `decimal` end-to-end.
- Move trust posting toward an append-only journal model.
- Make account and client-ledger balances derived or cache-like, not the only source of truth.
- Introduce explicit reversal entries instead of balance-only rollback semantics.
- Add idempotency and concurrency protection for trust commands.
- Introduce cleared-funds and available-to-disburse semantics.
- Replace simple reconciliation with true three-way reconciliation support.
- Keep read performance strong by retaining optimized balance projections and snapshots.

---

## 3. Non-Goals

Out of scope for Phase 1:

- full bank-feed aggregation product
- ACH / wire orchestration
- cross-ledger bulk migration tooling for external accounting systems
- document OCR for bank statements
- jurisdiction-specific legal advice embedded in code
- UI polish unrelated to accounting correctness

Phase 1 is about ledger integrity, not product marketing surface.

---

## 4. What Phase 0 Unlocks

Phase 0 creates the service boundary needed for safe Phase 1 work:

- `TrustController` is no longer the mutation owner
- command orchestration now has a single home
- maker-checker and role rules are no longer hardcoded in HTTP handlers
- risk preflight is now attachable at business-command level

Without that refactor, Phase 1 would be much riskier and slower to deliver.

---

## 5. Current State Gaps

### 5.1 Monetary Type Risk

Trust models still store money in `double`:

- `TrustTransaction.Amount`
- `TrustTransaction.BalanceBefore`
- `TrustTransaction.BalanceAfter`
- `TrustBankAccount.CurrentBalance`
- `ClientTrustLedger.RunningBalance`
- `ReconciliationRecord` numeric fields

This is not acceptable for production trust accounting.

### 5.2 Journal Integrity Gap

Today the system mutates account and ledger balances directly, and the transaction record is part request record, part posting record. That creates ambiguity around:

- what is the accounting source of truth
- how reversals should be represented
- how audit exports should explain derived balances

### 5.3 Concurrency and Retry Gap

The system does not yet provide a complete trust-grade answer for:

- double-submit retries
- duplicate command replay after timeout
- optimistic concurrency failures
- racing approvals or voids

### 5.4 Cleared Funds Gap

Trust withdrawals should be limited by cleared funds, not only nominal balance.

### 5.5 Reconciliation Gap

Current reconciliation compares:

- bank statement balance
- trust account balance
- client ledger total

That is not enough for real three-way monthly reconciliation.

---

## 6. Proposed Architecture

Phase 1 should preserve the Phase 0 command layer and upgrade the posting model underneath it.

### 6.1 Source of Truth Split

Introduce a clear distinction between:

- **request / workflow record**
  - approval status
  - creator / approver / rejection / void workflow
- **accounting journal record**
  - immutable posted debit/credit-style money movement
  - explicit reversal linkage
  - posting batch correlation

Recommended direction:

- keep `TrustTransaction` as the workflow envelope
- add a journal entity as the accounting source of truth
- keep `TrustBankAccount.CurrentBalance` and `ClientTrustLedger.RunningBalance` as derived projection/cache fields for speed

### 6.2 New Accounting Entities

Recommended additions:

- `TrustJournalEntry`
- `TrustPostingBatch`
- `TrustStatementLine`
- `TrustOutstandingItem`
- `TrustReconciliationPacket`
- `TrustCommandDeduplication` or equivalent idempotency storage

Minimal `TrustJournalEntry` shape:

- `Id`
- `TrustTransactionId`
- `PostingBatchId`
- `TrustAccountId`
- `ClientTrustLedgerId`
- `MatterId`
- `EntryKind` (`deposit`, `withdrawal`, `earned_fee_transfer`, `reversal`, `adjustment`)
- `Direction` (`debit`, `credit`) or signed `decimal Amount`
- `EffectiveAt`
- `ReversalOfJournalEntryId`
- `CorrelationKey`
- `CreatedBy`
- `CreatedAt`

---

## 7. Monetary Model

### 7.1 Decimal Standardization

All trust-facing money fields should move from `double` to `decimal`.

Phase 1 money conversion scope:

- trust entities
- trust DTOs
- trust command requests/responses
- trust compliance summaries
- reconciliation records and snapshots
- trust report/export builders

### 7.2 Precision Rules

Recommended baseline:

- database type equivalent to fixed-point money precision
- normalize to 2 decimal places for USD-style currencies at command boundary
- prohibit NaN / Infinity class anomalies by design

### 7.3 Migration Strategy

Migration must be explicit and reversible:

1. add parallel decimal columns or generate safe schema conversion migration
2. backfill from existing double fields
3. validate row-by-row tolerance
4. switch code reads to decimal fields
5. remove legacy double fields in a later cleanup migration

If direct in-place conversion is used, include a pre-migration numeric snapshot export for rollback confidence.

---

## 8. Journal-First Posting Model

### 8.1 Posting Rule

Approved trust money movement should create journal entries first, then update cached balances in the same database transaction.

### 8.2 Reversal Rule

Void should no longer mean "just mutate balances back."

Void should:

- mark workflow transaction as voided
- create one or more reversal journal entries linked to original journal entries
- update cached balances from those reversal entries

This preserves immutable accounting history and keeps exports audit-friendly.

### 8.3 Balance Projection Rule

For runtime performance, retain:

- `TrustBankAccount.CurrentBalance`
- `ClientTrustLedger.RunningBalance`

But treat them as projections that must always be derivable from journal history.

---

## 9. Concurrency and Idempotency

### 9.1 Required Protections

Phase 1 should add:

- row version / optimistic concurrency tokens on trust accounts, ledgers, and workflow transactions
- idempotency key support on every financial command
- duplicate suppression for retried approve/void/disburse operations
- deterministic command result replay when a known idempotency key is retried

### 9.2 Transaction Boundary

Every posting command must wrap these in one transaction:

- workflow state transition
- journal write
- cached balance update
- idempotency record write

### 9.3 Retry Behavior

On transient failure:

- safe retry must not duplicate money movement
- concurrency conflict should return a deterministic business error or retry-safe response

---

## 10. Cleared Funds and Available-to-Disburse

### 10.1 New States

Deposits should support at least:

- `received`
- `pending_clearance`
- `cleared`
- `returned`

### 10.2 New Balances

Each client trust ledger should expose:

- nominal balance
- uncleared funds
- available to disburse

Each trust account should expose:

- current total balance
- cleared balance
- uncleared balance
- available disbursement capacity

### 10.3 Withdrawal Rule

Withdrawal and earned-fee transfer should be blocked if:

- nominal balance is enough but cleared/available balance is not enough

This is where compliance safety materially improves.

---

## 11. Earned Fee Transfer Model

Phase 1 should formalize earned-fee transfer as its own trust posting type.

Requirements:

- invoice or billing basis must exist
- trust source must be identified
- available-to-disburse must cover the transfer
- risk preflight remains blocking
- accounting output should generate both trust-side reduction and operating-side increase journal evidence

This should stop being an incidental side effect inside broader billing allocation logic.

---

## 12. Three-Way Reconciliation

### 12.1 Target Model

Monthly reconciliation packet should include:

- bank statement ending balance
- cleared trust account journal balance as-of period end
- total of individual client ledgers as-of period end
- outstanding deposits
- outstanding checks / withdrawals
- adjusted bank balance
- discrepancy classification
- preparer / reviewer / sign-off metadata

### 12.2 New Data Structures

Recommended additions:

- `TrustStatementLine`
- `TrustOutstandingItem`
- `TrustReconciliationPacket`
- `TrustReconciliationException`

### 12.3 Output

System should generate:

- internal review view
- exportable reconciliation packet
- month-end attorney sign-off trail

---

## 13. Jurisdiction and Policy Layer

Phase 1 should start moving trust rules from generic code assumptions into policy/config:

- IOLTA vs non-IOLTA account type
- designated responsible lawyer
- signatory rules
- jurisdiction-specific retention/export defaults
- fee-transfer policy requirements
- reconciliation cadence and sign-off requirements

This should be config-driven, not hardcoded around one firm assumption.

---

## 14. Performance Guardrails

Phase 1 must not regress the product into visibly slower trust operations.

### 14.1 Performance Principles

- journal write and projection update happen in one transaction
- read screens should prefer precomputed balances and snapshots
- statement and reconciliation heavy computation should use snapshot/materialization strategy
- exports should read from prepared packet/snapshot tables where practical
- avoid recomputing full journal balances on every dashboard request

### 14.2 Projection Strategy

Recommended:

- journal = source of truth
- projection tables/columns = hot read path
- reconciliation packet = period snapshot

This gives both correctness and speed.

---

## 15. Migration Plan

### 15.1 Suggested Delivery Order

**Phase 1A**

- decimal conversion plan and schema migration
- journal entity introduction
- projection updater inside transaction

**Phase 1B**

- reversal-entry model
- row version / optimistic concurrency
- idempotency keys

**Phase 1C**

- cleared funds model
- earned-fee transfer hardening

**Phase 1D**

- statement lines
- outstanding items
- reconciliation packet generation

### 15.2 Rollout Safety

- deploy schema first where needed
- dual-write old/new accounting state for one validation window if necessary
- compare projection totals against journal-derived totals
- ship drift alerts before removing fallback logic

---

## 16. Testing Strategy

Phase 1 needs materially stronger tests than today.

### 16.1 Required Test Types

- unit tests for posting math
- integration tests for approve/void/reversal chains
- idempotency retry tests
- optimistic concurrency race tests
- cleared-funds withdrawal blocking tests
- reconciliation packet generation tests
- migration/backfill verification tests

### 16.2 Invariants to Assert

- trust account projection equals journal-derived balance
- client ledger projection equals journal-derived balance
- reversal net effect equals original in opposite direction
- same idempotency key does not double-post
- available-to-disburse never exceeds cleared funds
- three-way reconciliation packet computes consistent totals

---

## 17. Observability

Phase 1 should add operational visibility for accounting integrity:

- journal/projection drift metric
- duplicate-command suppression metric
- concurrency-conflict metric
- failed reconciliation packet generation metric
- uncleared funds aging metric
- outstanding-items aging metric

These should show up in admin operations dashboards and logs.

---

## 18. Acceptance Criteria

Phase 1 is complete when:

- trust core money fields are decimal-based
- approved transactions create immutable journal entries
- void creates linked reversal entries
- approve/void/earned-fee transfer are idempotent and concurrency-safe
- withdrawals are blocked by available-to-disburse, not just nominal balance
- reconciliation produces a real three-way packet with outstanding items
- cached balances match journal-derived balances within zero tolerance

---

## 19. Open Decisions

Items to settle before implementation:

- whether `TrustTransaction` remains the permanent workflow envelope or is split further
- whether idempotency uses a dedicated table or embeds in workflow records
- whether statement import lands in Phase 1 or Phase 2
- whether projection updates remain synchronous or move to verified append-and-project with drift monitor
- whether earned-fee transfer becomes its own command immediately or first remains inside billing with a new trust posting primitive underneath

---

## 20. Recommendation

Proceed with Phase 1 as the next trust hardening milestone after Phase 0 is merged.

Recommended implementation order:

1. decimal conversion design and migration
2. journal + projection dual model
3. reversal entries + concurrency/idempotency
4. cleared funds
5. three-way reconciliation packet

This sequence gives the highest compliance and correctness gain without sacrificing performance discipline.
