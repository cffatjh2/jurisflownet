# Phase 2 - Quarterly Assurance Report (Template + Operating Model)

Amac: Guvenlik ve guvence calismalarini executive/sales/legal ile duzenli ve kontrollu raporlamak.

Bu dokuman hem rapor template'i hem de raporlama cadence/isletim kurallarini icerir.

## Audience
- Executive leadership
- Security / Platform / Engineering leadership
- Legal
- Sales enablement / RevOps

## Reporting Goals
1. Trust posture trendini gostermek
2. GTM blocker'lari erken gormek
3. Claim risklerini ve stale docs riskini yonetmek
4. Attestation/certification readiness takibini netlestirmek

## Cadence
- Quarterly (required)
- Optional monthly mini-review for internal ops

## Data Inputs (minimum)
- `PHASE0_EVIDENCE_INVENTORY.md` status updates
- P1/P2 execution workstream statuses
- Security questionnaire workflow metrics (CRM)
- Vulnerability management metrics
- Trust center access / doc freshness metrics
- Pen test / DR / IR status

## Report Structure (Template)

### 1. Executive Summary (1 page)
- Overall trust posture: `Green / Yellow / Red`
- Key improvements this quarter
- Key risks requiring executive decision
- Certification/attestation status snapshot

### 2. GTM Assurance Readiness Snapshot
- Trust Center status (public + gated)
- Security overview PDF status
- Subprocessor page status
- DPA + standard responses status
- Sales enablement adoption status

Suggested summary table:

| Item | Status | Owner | Last Updated | Risk |
|---|---|---|---|---|
| Trust Center Public |  |  |  |  |
| Gated Docs Workflow |  |  |  |  |
| Security Overview PDF |  |  |  |  |
| Subprocessors Page |  |  |  |  |
| DPA + Responses |  |  |  |  |

### 3. KPI Dashboard (Required)
Track quarter value + trend vs previous quarter.

| KPI | Current | Prior | Trend | Target | Notes |
|---|---:|---:|---|---:|---|
| Security questionnaire turnaround (median) |  |  |  |  | |
| Security questionnaire turnaround (p90) |  |  |  |  | |
| Enterprise deal security-blocked days |  |  |  |  | |
| Security review win-rate impact |  |  |  |  | attribution assumptions |
| Evidence request fulfillment SLA |  |  |  |  | |
| Time-to-redline security clauses |  |  |  |  | |
| Trust Center doc access / usage |  |  |  |  | |
| Open critical security remediation count |  |  |  |  | |

### 4. Security Operations and Evidence Status
- New evidence captured this quarter
- Evidence gaps still open
- Stale evidence/documents past refresh due
- IR tabletop / DR restore / pentest status

### 5. Vulnerability Program Summary
- Open Critical/High counts
- SLA attainment %
- MTTR by severity
- Repeated finding themes
- Major remediation highlights

### 6. Risk Register (Top 5)
Include explicit risk owner and mitigation ETA.

| Risk | Impact | Likelihood | Owner | Mitigation | ETA |
|---|---|---|---|---|---|
| Claim overstatement risk |  |  |  |  |  |
| Document staleness |  |  |  |  |  |
| Single-person bottleneck |  |  |  |  |  |
| Evidence sprawl |  |  |  |  |  |

### 7. Decisions Needed (Exec)
- Budget/vendor approval (pentest/audit tooling)
- Hiring/ownership gaps
- Certification timeline decisions

## Data Quality Notes (Required)
For each KPI, indicate if:
- `exact`
- `derived`
- `proxy`

This prevents misleading trend interpretation.

## Ownership and Workflow
1. Security compiles draft
2. Ops/Platform validates operational metrics
3. Sales/RevOps validates GTM metrics
4. Legal reviews external-claim implications
5. Exec receives final report and decisions list

## Phase 2 Acceptance Criteria (Quarterly Report Track)
- [ ] Template approved and owners assigned
- [ ] KPI sources mapped
- [ ] First quarterly report generated
- [ ] Exec review completed with documented decisions
