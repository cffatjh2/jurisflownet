# Trust Risk Radar: Explainable Trust Risk Controls (One-Pager)

## What It Is
Trust Risk Radar is a tenant-scoped trust/IOLTA risk control layer that evaluates trust-related financial activity in near real time and produces:
- an explainable risk score
- concrete reasons and evidence references
- policy-driven actions (`warn`, `review`, `soft_hold`, `hard_hold`)

## Why It Matters
Law firm trust accounting workflows are high-risk and operationally sensitive. Teams need controls that:
- catch risky patterns early
- avoid unnecessary operational lockups
- produce audit-ready evidence for every decision

Trust Risk Radar is designed for exactly that tradeoff.

## What Makes It Different
### Explainable by Design
Each risk event includes:
- `riskScore`
- `severity`
- `riskReasons[]`
- `evidenceRefs[]`
- feature contributions (including behavioral signals)

This supports human review, auditability, and policy tuning.

### Human-in-the-Loop Enforcement
Radar supports staged control rollout:
- `warn`
- `review_required`
- `soft_hold`
- `hard_hold`

Policy controls support grace modes and strict mode to reduce false-positive operational disruption during rollout.

### Versioned Policy + Audit Trail
- Policy versions are tracked
- Hold actions and releases are logged
- Audit evidence exports include hash-chain-backed audit records and trace tags

## Example Use Cases
- Disbursement exceeds available trust balance
- Repeated reversals/adjustments in a short window
- Off-hours high-value trust activity
- Trust-to-operating allocation mismatch
- Missing supporting invoice/payment correlation

## Operational Safety
- Tenant-specific thresholds and policy templates
- Review disposition taxonomy (`true_positive`, `false_positive`, `acceptable_exception`)
- Dual-approval for critical hold release (policy configurable)
- SLA-based escalation

## Deployment Approach
Recommended rollout:
1. Behavioral shadow mode on
2. `warn` or `soft_hold` grace mode
3. Tune thresholds with impact simulation
4. Enable strict preflight for selected operations when signal quality is proven

## Evidence and Compliance Readiness
Trust Risk Radar supports enterprise review with:
- evidence export (policy versions, holds, releases, overrides, audit trail)
- quarterly tuning review documentation
- clear control narrative for Trust Center / security questionnaires

## Positioning Note
Trust Risk Radar is a decision-support and control orchestration layer. It does not replace firm accounting review, legal judgment, or regulatory obligations.
