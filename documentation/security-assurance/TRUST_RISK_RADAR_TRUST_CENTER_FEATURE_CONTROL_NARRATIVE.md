# Trust Risk Radar Feature Control Narrative (Trust Center / Security Pack)

## Audience
- Enterprise buyers
- Security reviewers
- Compliance teams
- Procurement/legal stakeholders

## Purpose
Provide a concise, accurate control narrative for Trust Risk Radar without overstating guarantees.

## Suggested Public/Shared Wording (Review Before Publishing)
Trust Risk Radar helps firms monitor trust-account-related financial activity using explainable risk scoring, human review workflows, and configurable policy actions (including warnings, review requirements, and hold flows). The feature supports auditability through policy versioning, action logging, and evidence export.

## Control Design Summary
### 1. Explainable Risk Scoring
- Risk events include score, severity, reasons, and evidence references
- Behavioral signals can be used in shadow or active mode
- Threshold tuning is policy-driven and versioned

### 2. Human-in-the-Loop Workflow
- Review disposition taxonomy supported (`true_positive`, `false_positive`, `acceptable_exception`)
- Hold lifecycle includes review, escalation, and release
- Dual-approval release for critical holds is policy configurable

### 3. Gradual Enforcement
- Supports rollout/grace modes (`warn`, `soft_hold`, `strict`)
- High-confidence and severity filters can scope preflight enforcement
- Operation-level fail behavior can be configured (`fail_open`/`fail_closed`) by policy

### 4. Auditability
- Policy versions retained
- Hold actions and overrides logged
- Evidence export available for policy versions, events, holds, releases, overrides, and related trust-risk audit logs

## Important Qualification Language (Do Not Omit)
- Trust Risk Radar is a configurable control-support layer and does not replace firm accounting review, legal obligations, or financial reconciliation processes.
- Hold behavior and enforcement strictness are tenant-policy dependent.
- Same-transaction blocking may be intentionally conservative during rollout to reduce false-positive operational disruption.

## Security Questionnaire Mapping (Example Topics)
- Change management: policy versioning and tuning review process
- Access control: role-based release/escalate permissions
- Logging/monitoring: audit logs, hold lifecycle actions, trace correlation
- Incident response: runbook-based investigation and evidence export

## Internal Review Checklist Before Publishing
- Security approves wording
- Legal approves claims and qualifiers
- Product confirms current behavior matches wording
- Sales enablement receives approved short-form version
