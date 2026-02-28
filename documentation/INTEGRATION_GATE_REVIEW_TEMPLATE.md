# Integration Gate Review Template

Use this template for each provider/phase gate review.

---

## Metadata

- Phase:
- Provider:
- Environment: `sandbox` / `pilot` / `production`
- Owner:
- Reviewer:
- Date:
- Status: `Pass` / `Conditional Pass` / `Fail`

## Scope

- In scope:
- Out of scope:
- Canonical actions expected: `validate`, `pull`, `push`, `reconcile`, `webhook`, `backfill`

## DoR Checklist (Before Implementation)

- [ ] Provider sandbox access available
- [ ] API docs + auth scopes confirmed
- [ ] Example payloads collected (success/error/retry/rejection)
- [ ] Mapping requirements defined (field/enum/account/tax/conflict policy)
- [ ] Pilot tenant selected

### DoR Evidence

- Sandbox account/org ID:
- Auth scopes:
- Payload samples:
- Mapping profile reference:
- Pilot tenant:

## DoD Checklist (Completion)

- [ ] `validate` works
- [ ] `pull` works
- [ ] `push` works (or N/A documented)
- [ ] `reconcile` works (or N/A documented)
- [ ] Review/conflict queue produces real data
- [ ] Replay works (webhook and/or sync replay)
- [ ] Rate-limit/backoff tested
- [ ] Audit trace correlation visible
- [ ] Kill switch tested
- [ ] Integration test added/passed
- [ ] Retry/failure-path test added/passed
- [ ] Ops UI visibility verified

### DoD Evidence

- Validate run IDs / screenshots:
- Pull run IDs / screenshots:
- Push run IDs / screenshots:
- Reconcile run IDs / screenshots:
- Review/conflict queue examples:
- Replay evidence:
- Rate-limit/backoff evidence:
- Audit trace correlation example:
- Kill switch evidence:
- Test outputs:
- Ops UI screenshots:

## Risks / Exceptions

- Open risks:
- Temporary limitations:
- Follow-up tickets:

## Rollout Decision

- Pilot rollout allowed: `Yes` / `No`
- Feature flags / tenant allowlist:
- Rollback conditions:
- Kill switch owner:

---

