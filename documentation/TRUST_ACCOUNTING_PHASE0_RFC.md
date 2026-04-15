# Trust Accounting Phase 0 RFC
## Service Layer, Maker-Checker, Role Matrix, and Risk Preflight

**Status:** Draft
**Owner:** Platform / Billing / Trust
**Last Updated:** 2026-04-13
**Scope:** Go-live blocker remediation for trust accounting write paths

---

## 1. Problem Statement

The current trust accounting module is a solid alpha foundation, but the write path is not production-safe for trust operations.

Current blockers:

- `TrustController` owns both HTTP and business mutation logic.
- Deposit and withdrawal can auto-approve when the caller is treated as an approver.
- The same user can create and approve the same trust transaction.
- Approval permissions are hardcoded in controller logic.
- Trust Risk Radar exists, but trust write endpoints are not integrated into a blocking preflight path.
- The current architecture makes future hardening harder because approval, posting, reversal, and audit concerns are mixed together.

This RFC defines the minimum structural change required before trust accounting can be treated as a production-grade subsystem.

---

## 2. Goals

- Move trust write behavior into a single command-oriented service layer.
- Make `TrustController` an HTTP orchestration layer only.
- Remove auto-approve for trust transaction creation.
- Enforce `creator != approver`.
- Move role-based approval behavior into configuration instead of controller hardcoding.
- Invoke Trust Risk Radar preflight before `withdrawal`, `void`, and future `earned-fee transfer` commands.
- Preserve or improve runtime performance.
- Avoid unnecessary schema churn in Phase 0 unless strictly required.

---

## 3. Non-Goals

These are explicitly out of scope for Phase 0:

- `double -> decimal` conversion for trust models
- append-only trust journal redesign
- cleared-funds model
- true bank statement line import and full outstanding-item reconciliation
- dual approval for all material thresholds
- UI redesign beyond what is needed to support the new workflow

Those belong to later phases and should not be mixed into this first hardening pass.

---

## 4. Current State Summary

Today the trust write path behaves like this:

- `POST /api/trust/deposit`
  - validates account/ledgers
  - if caller matches `IsApprover()`, creates `APPROVED` transaction and mutates balances immediately
- `POST /api/trust/withdrawal`
  - validates account/ledger
  - if caller matches `IsApprover()`, creates `APPROVED` transaction and mutates balances immediately
- `POST /api/trust/transactions/{id}/approve`
  - approves and mutates balances later
- `POST /api/trust/transactions/{id}/void`
  - reverses balances in-place

This is fast, but it is not sufficiently safe for production trust workflows.

---

## 5. Proposed Architecture

### 5.1 New Service

Introduce a single application service:

- `JurisFlow.Server/Services/TrustAccountingService.cs`

Responsibilities:

- business validation
- command authorization using configured role matrix
- trust risk preflight invocation
- trust entity loading
- balance mutation decisions
- transaction state transitions
- audit event payload construction

`TrustController` should stop mutating domain state directly and should delegate to this service.

### 5.2 New Command Surface

Phase 0 command methods:

- `CreateDepositAsync`
- `CreateWithdrawalAsync`
- `ApproveTransactionAsync`
- `RejectTransactionAsync`
- `VoidTransactionAsync`

Optional placeholder for forward compatibility:

- `CreateEarnedFeeTransferAsync`

Each command should:

- accept a dedicated request model
- return a dedicated result model
- avoid leaking HTTP concerns into service code

### 5.3 Controller Shape After Refactor

`TrustController` should only do:

- request binding
- HTTP response mapping
- cancellation token forwarding
- current user context extraction

It should not:

- decide approver roles
- mutate balances
- parse allocation JSON for business purposes
- call risk radar directly
- perform transaction lifecycle branching

---

## 6. Authorization and Role Matrix

### 6.1 New Configuration Section

Add a config-backed role matrix, for example:

```json
{
  "TrustAccounting": {
    "Roles": {
      "CreateDeposit": ["Admin", "Partner", "Accountant", "Associate"],
      "CreateWithdrawal": ["Admin", "Partner", "Accountant"],
      "ApproveTransaction": ["Admin", "Partner", "Accountant"],
      "RejectTransaction": ["Admin", "Partner", "Accountant"],
      "VoidTransaction": ["Admin", "Partner"],
      "EarnedFeeTransfer": ["Admin", "Partner", "Accountant"]
    }
  }
}
```

Default Phase 0 posture:

- `Associate` may create requests
- `Associate` may not approve withdrawals or void transactions
- `Admin`, `Partner`, `Accountant` can approve where policy allows
- `Void` should default narrower than `Approve`

### 6.2 Service Abstraction

Introduce:

- `TrustAccountingRoleMatrixOptions`
- `ITrustAuthorizationService` or a private authorization helper within `TrustAccountingService`

This must support:

- hot-reload via config binding if practical
- case-insensitive role matching
- default-deny if config is missing or invalid

---

## 7. Maker-Checker Rules

Phase 0 hard rules:

- trust transaction creation never auto-approves
- newly created deposit and withdrawal are always `PENDING`
- approving user must not equal `CreatedBy`
- voiding an approved transaction requires an authorized user and should not be executable by the original creator when policy disallows it

This means:

- creation path becomes cheap and deterministic
- approval path becomes the only balance-posting path
- audit intent becomes cleaner

---

## 8. Risk Preflight Integration

### 8.1 Commands That Must Preflight

Phase 0 requirement:

- `withdrawal`
- `void`
- `earned-fee transfer` when implemented

### 8.2 Where Preflight Runs

Preflight should execute inside `TrustAccountingService`, before domain state mutation.

Recommended call:

- `_trustRiskRadarService.EnforceNoActiveHardHoldsAsync(...)`

Guard context must include:

- trust account id
- ledger id when available
- matter id when available
- trust transaction id for void operations
- operation type label such as `trust_withdrawal`, `trust_void`, `trust_earned_fee_transfer`

### 8.3 Phase 0 Policy Posture

To avoid hidden production drift, Phase 0 rollout should document and support:

- `preflightStrictModeEnabled`
- `preflightStrictRolloutMode`
- operation-specific fail mode

Default recommendation for initial rollout:

- withdrawal: strict preflight enabled
- void: strict preflight enabled
- earned fee transfer: strict preflight enabled
- fail mode: `fail_closed` for these operations once smoke testing passes

If rollout must begin softer:

- release behind a config flag
- instrument warnings
- upgrade to strict after validation window

---

## 9. Performance Guardrails

Phase 0 must not introduce a noticeable latency regression.

### 9.1 Rules

- exactly one business service call per endpoint
- one `SaveChangesAsync()` on happy-path create/approve/reject/void when possible
- no N+1 ledger/account reads
- use `AsNoTracking()` for pure authorization and preflight reads
- load only required columns for role/policy/preflight checks where possible
- no additional synchronous logging writes beyond the existing audit path
- no background queue requirement for Phase 0

### 9.2 Expected Query Shape

`CreateDepositAsync`

- read account
- read target ledgers in one query
- do not post balances
- insert one `TrustTransaction`

`CreateWithdrawalAsync`

- read account
- read target ledger
- run risk preflight
- insert one `TrustTransaction`

`ApproveTransactionAsync`

- read target transaction
- read account and ledger set required for posting
- enforce `creator != approver`
- mutate balances
- update transaction status

### 9.3 Caching

Role matrix and trust policy metadata should be config-backed and cached in-memory.

Do not:

- deserialize large JSON policy blobs repeatedly inside one request
- re-query static configuration tables multiple times per command

### 9.4 Performance Validation

For each command we should compare before vs after:

- median latency
- p95 latency
- SQL query count
- SQL total duration

Success criterion:

- create deposit/withdrawal should stay near current latency or improve
- approve should not exceed current p95 by more than a small constant factor

---

## 10. Detailed Service Design

### 10.1 Core Types

Suggested new types:

- `TrustCommandActor`
- `TrustCommandResult<T>`
- `CreateDepositCommand`
- `CreateWithdrawalCommand`
- `ApproveTrustTransactionCommand`
- `RejectTrustTransactionCommand`
- `VoidTrustTransactionCommand`

Suggested result envelope:

```csharp
public sealed class TrustCommandResult<T>
{
    public bool Success { get; init; }
    public string? ErrorCode { get; init; }
    public string? Message { get; init; }
    public T? Value { get; init; }
}
```

### 10.2 Service Flow Example: Create Withdrawal

1. Authorize actor against role matrix.
2. Load account and ledger.
3. Validate account/ledger relationship and active state.
4. Run billing lock check.
5. Run trust risk preflight.
6. Create `PENDING` transaction only.
7. Save once.
8. Emit audit log.

Important:

- no balance mutation on create
- no approval side effect during create

### 10.3 Service Flow Example: Approve Transaction

1. Authorize actor against role matrix.
2. Load transaction.
3. Enforce `transaction.Status == PENDING`.
4. Enforce `transaction.CreatedBy != actor.UserId`.
5. Load required account and ledgers.
6. Re-validate business invariants.
7. Apply posting.
8. Mark `APPROVED`.
9. Save once.
10. Emit audit log.

---

## 11. API Contract Changes

### 11.1 Behavior Changes

- `POST /api/trust/deposit` will now return a `PENDING` transaction even for privileged users.
- `POST /api/trust/withdrawal` will now return a `PENDING` transaction even for privileged users.
- `POST /api/trust/transactions/{id}/approve` will reject same-user approval.

### 11.2 Suggested Response Enhancements

Add fields that reduce client-side guesswork:

- `canApprove`
- `requiresApproval`
- `approvalBlockedReason`
- `riskPreflightEvaluated`

These are optional in Phase 0, but recommended.

---

## 12. Migration and Rollout

### 12.1 Schema Impact

Phase 0 should prefer no mandatory DB migration unless a minimal config-backed metadata table is required.

No-schema-change path is preferred for speed:

- reuse `CreatedBy`, `ApprovedBy`, `Status`
- keep existing transaction records intact

### 12.2 Backward Compatibility

Existing approved transactions remain valid.

After deployment:

- new create requests remain pending
- legacy approved records continue to display normally

### 12.3 Release Steps

1. Add service and config types.
2. Refactor controller to delegate to service.
3. Keep endpoints stable.
4. Deploy with strict metrics and logging.
5. Verify create/approve/void flows in staging.
6. Enable stricter fail-closed settings for withdrawal and void if rollout starts in warn mode.

---

## 13. Testing Plan

### 13.1 Unit Tests

- create deposit always produces `PENDING`
- create withdrawal always produces `PENDING`
- creator cannot approve own transaction
- unauthorized role cannot approve
- role matrix config is honored
- withdrawal preflight is called
- void preflight is called

### 13.2 Integration Tests

- deposit create then separate approve updates balances correctly
- withdrawal create then separate approve updates balances correctly
- failed preflight blocks withdrawal
- failed preflight blocks void
- approval does not double-post on repeat request attempt with same final status

### 13.3 Performance Tests

- compare controller-before vs service-after latency
- ensure SQL query count is bounded
- validate no extra `SaveChangesAsync()` calls per request

---

## 14. Observability

Add structured logs around:

- operation type
- actor user id
- actor role
- transaction id
- trust account id
- ledger id
- authorization outcome
- preflight outcome
- command duration ms

Add counters:

- `trust_command_create_deposit_total`
- `trust_command_create_withdrawal_total`
- `trust_command_approve_total`
- `trust_command_void_total`
- `trust_command_preflight_block_total`
- `trust_command_same_user_approval_block_total`

---

## 15. Risks

- Config mistakes could lock out valid operations if role defaults are too narrow.
- If risk preflight is set to fail-closed too early, operations may stop unexpectedly.
- If controller and service logic coexist too long, behavior drift can appear.

Mitigation:

- single service source of truth
- default-deny but with explicit staging validation
- endpoint behavior tests before production cutover

---

## 16. Phase 0 Implementation Checklist

1. Add `TrustAccountingService`.
2. Add config-bound role matrix options.
3. Refactor `TrustController` create/approve/reject/void endpoints to use service.
4. Remove auto-approve from create commands.
5. Enforce `creator != approver`.
6. Integrate risk preflight for withdrawal, void, earned-fee transfer path.
7. Add tests.
8. Capture latency/query-count before and after.

---

## 17. Follow-On Phases

Immediately after Phase 0:

- Phase 1: `decimal`, concurrency, idempotency
- Phase 2: append-only trust journal and reversal entries
- Phase 3: cleared funds and available-to-disburse model
- Phase 4: statement import and real reconciliation packet

---

## 18. Recommendation

Proceed with this RFC as the minimum production-hardening foundation.

Do not combine Phase 0 with:

- money type migration
- journal redesign
- reconciliation redesign

Those are necessary, but Phase 0 should stay small, high-signal, and low-regression so we can harden the write path quickly without introducing performance loss.
