# Phase 2 - Trust Center Gated Docs and Access Model

Amac: Public trust center ile NDA-sonrasi paylasilan dokumanlari ayrik ve kontrollu hale getirmek.

Bu dokuman, "Trust Center canli" hedefi icin minimum bilgi mimarisi + erisim kurallari + operasyon akisini tanimlar.

## Phase 2 Deliverable
- Public trust center content set (ungated)
- NDA-gated dokuman katalogu ve erisim akisi
- Access logging / expiry / revocation kurallari
- Doc freshness owner/cadence modeli

## 1) Content Tiers (Required)

### Tier A - Public (No Login / No NDA)
Amaç: ilk guven bariyerini kaldirmak.

Minimum pages:
1. Security Overview (high-level controls)
2. Subprocessors page
3. Data handling and retention summary
4. Vulnerability disclosure / security contact
5. Status page link / uptime

Prohibited content (public):
- Detailed architecture diagrams with sensitive internals
- Pentest raw findings
- Internal-only policies
- Detailed incident records
- Customer-specific security exhibits

### Tier B - NDA Gated (Approved Prospects / Customers)
Amaç: procurement/security review hizlandirmak.

Minimum gated docs:
1. Security Overview PDF (detailed)
2. DPA template
3. Standard security questionnaire responses
4. Pentest executive summary (when available)
5. DR / IR posture summary (sanitized)
6. Data flow diagram (sanitized)

Optional gated docs:
- Policy summaries (not full internal policy unless approved)
- Architecture addendum
- Compliance roadmap / attestation timeline

### Tier C - Internal Only
- Full policies
- Detailed risk register
- Raw pentest report
- Restore logs with sensitive data
- Detailed incident postmortems

## 2) Access Control Model (Gated Docs)

### Access Preconditions
- NDA executed (or existing MSA/DPA covers confidentiality)
- Opportunity/account linked in CRM
- Requester email domain verified
- Internal approver (Security or Legal) approves package level

### Access Levels
- `Prospect-Standard` (default NDA pack)
- `Prospect-Expanded` (extra docs, security team review)
- `Customer-Active` (contracted customer)
- `Restricted` (single-doc share, time-limited)

### Access Controls (minimum)
- Time-limited links or portal session expiry
- Download and access event logging
- Revocation capability
- Version-aware document publishing
- Optional watermarking for sensitive PDF packs

## 3) Gated Access Workflow (Operational)

1. Sales/CS opens "Security Docs Request" in CRM.
2. CRM verifies NDA status.
3. Security reviews requested package level.
4. Trust center portal grants access (expiry + scope).
5. Access events logged and linked to opportunity/account.
6. Expiry reminder and auto-revoke.

## 4) Document Lifecycle / Freshness Rules

Each trust center document must have:
- Owner
- Reviewer
- Last reviewed date
- Next review due
- Classification (`public`, `nda-gated`, `internal`)
- Version / change summary

Suggested cadences:
- Subprocessors: monthly + ad hoc on change
- Security overview: quarterly
- Questionnaire answer library: monthly
- Pentest summary: annually or after retest
- DPA template: legal release cycle

## 5) Logging and Metrics (must-have)

Track at minimum:
- Trust center public page visits
- Gated doc requests
- Gated approvals/denials
- Time-to-approve
- Doc access/download counts
- Expired vs revoked links
- Stale docs (past review due)

These metrics feed P2 KPIs and quarterly assurance report.

## 6) Document Inventory (Seed List)

| Doc ID | Document | Tier | Owner | Review Cadence | Prereq |
|---|---|---|---|---|---|
| TC-PUB-001 | Security Overview (web) | Public | Security | Quarterly | Claim register approved |
| TC-PUB-002 | Subprocessors | Public | Legal/Security | Monthly | Subprocessor registry operational |
| TC-PUB-003 | Data Handling Summary | Public | Security/Legal | Quarterly | Data retention language approved |
| TC-NDA-001 | Security Overview PDF (detailed) | NDA | Security | Quarterly | Evidence baseline captured |
| TC-NDA-002 | DPA Template | NDA | Legal | On legal update | Legal approval |
| TC-NDA-003 | Standard Questionnaire Responses | NDA | Security + Sales | Monthly | Response library operational |
| TC-NDA-004 | Pentest Executive Summary | NDA | Security | Annual/ad hoc | Pentest complete |
| TC-NDA-005 | DR/IR Summary | NDA | Ops/Security | Semiannual | Tabletop + restore evidence |

## 7) Phase 2 Acceptance Criteria (Trust Center Track)
- [ ] Public trust center content set published
- [ ] Gated doc access workflow defined and owner-assigned
- [ ] NDA prerequisite enforcement documented
- [ ] Doc freshness metadata standard adopted
- [ ] Access logging fields defined (for KPI tracking)

## Risks / Failure Modes
- Stale docs published -> trust erosion
- Sales bypasses approval workflow -> over-disclosure
- Single approver bottleneck -> slowed deals
- No access logs -> cannot prove controlled sharing
