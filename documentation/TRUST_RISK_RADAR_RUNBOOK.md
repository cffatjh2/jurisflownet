# Trust Risk Radar Runbook (Ops + Finance)

## Purpose
Operational runbook for handling Trust Risk Radar alerts, holds, releases, escalations, tuning reviews, and audit evidence requests.

This runbook is for:
- Finance operations
- Security operations
- Billing operations
- Compliance/audit responders

## Scope
- Trust risk event triage
- Hold queue handling (`soft` / `hard`)
- Review disposition workflow
- Dual-approval release flow (critical hard holds)
- SLA escalation handling
- Evidence export for audit/customer review

## Roles and Responsibilities
- `FinanceAdmin`: primary operator for hold review/release in normal finance workflows
- `SecurityAdmin`: secondary approver / escalation path for critical cases and policy changes
- `Admin`: policy administration where allowed by tenant policy
- `Ops`: routing, monitoring, notification delivery, incident coordination

## Daily Operations (Pilot / Production)
1. Open `Settings > Trust Risk Radar`.
2. Review `New Since Refresh`, `Open Holds`, `Hard Holds`, and `False Positive %`.
3. Triage `Recent Alerts / Event Stream` by `severity`, `decision`, `matter`, `client`.
4. Process `Hold Queue`:
   - move to `Under Review`
   - `Escalate` if risk is unresolved or policy/scope issue exists
   - `Release` only with required reason + approver reason
5. Record mandatory review disposition for investigated events:
   - `true_positive`
   - `false_positive`
   - `acceptable_exception`
6. Export evidence JSON when requested by audit/compliance/customer review.

## Hold Handling Rules
### Soft Hold
- Goal: pause and review, avoid operational disruption
- Action:
  - set `Under Review`
  - collect supporting links (invoice, payment, matter, trust ledger)
  - release with reason if valid
  - escalate if policy ambiguity or repeated pattern exists

### Hard Hold
- Goal: stop risky follow-up actions until explicit release
- Action:
  - verify supporting evidence
  - confirm if risk is true positive
  - follow dual-approval if tenant policy requires it
  - release only with audit-quality reason

## Review Disposition Taxonomy (Required)
- `true_positive`: radar correctly identified real risk or control gap
- `false_positive`: radar triggered but underlying transaction was valid
- `acceptable_exception`: deviation is intentional and approved under policy

## Reason Quality Standard
Required for `release`, `escalate`, and review disposition:
- must describe what was checked
- must state why action is safe/unsafe
- must identify approver rationale for material holds
- avoid placeholders (`ok`, `done`, `checked`)

Good example:
- `Verified trust subledger and source invoice allocation. Disbursement amount is covered by client trust funds after same-day deposit posted late. Release approved after reconciliation review.`

## SLA and Escalation
- Monitor hold SLA metrics in panel
- If hold exceeds SLA:
  - system may auto-escalate (policy-driven)
  - operator documents current status and blocker
  - notify `SecurityAdmin` for critical holds

## Policy Change Control (Trust Risk)
- Threshold changes affecting critical metrics require documented reason
- Review queue item may be created automatically for critical threshold changes
- Capture:
  - change intent
  - expected impact
  - rollout mode (`warn`, `soft_hold`, `strict`)
  - fallback/rollback plan

## Evidence Export Procedure
1. In Trust Risk Radar panel, click `Export Evidence JSON`.
2. Default export includes:
   - policy versions
   - holds
   - releases / overrides
   - actions
   - review links
   - events
   - trust-risk audit logs (hash-chain fields + parsed trace tags)
3. Store exported file in evidence vault using naming convention:
   - `trust-risk-radar-evidence-export-YYYY-MM-DDTHH-mm-ssZ.json`
4. Attach to audit/customer request ticket with scope and timeframe.

See also: `documentation/security-assurance/TRUST_RISK_RADAR_AUDIT_EVIDENCE_EXPORT_GUIDE.md`

## Quarterly Tuning Review
- Run quarterly (or monthly during pilot)
- Review:
  - false positive rate by rule
  - burden score
  - hold release time by severity
  - threshold impact simulations (30/60 days)
  - policy version compare before/after
- Use template:
  - `documentation/security-assurance/TRUST_RISK_RADAR_QUARTERLY_TUNING_REVIEW_REPORT_TEMPLATE.md`

## Incident Handling (Radar-Specific)
### If radar appears too noisy (false positives spike)
1. Switch rollout mode to `warn` or `soft_hold`
2. Keep logging/review outcome collection active
3. Run impact simulation before threshold changes
4. Document policy version and rationale

### If radar misses a confirmed risky event
1. Label event outcome (`true_positive` if caught later, or issue record if missed)
2. Add evidence to tuning review
3. Consider rule weight/threshold/policy template adjustments
4. Track as control improvement item

## Audit / Customer Response Quick Answers (Operational)
- Radar decisions are explainable (`riskReasons`, `evidenceRefs`, features)
- Holds and releases are audit-logged with user/role/time and reason
- Policy versions are versioned and exportable
- Tenant-scoped policy and data separation apply
- Enforcement rollout supports gradual hardening (`warn` -> `soft_hold` -> `strict`)
