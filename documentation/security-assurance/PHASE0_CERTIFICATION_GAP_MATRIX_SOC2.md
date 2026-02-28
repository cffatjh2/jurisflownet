# Phase 0 - Certification Gap Matrix (SOC 2 Readiness Snapshot)

Bu dokuman resmi audit raporu degildir. Amac, mevcut teknik/operasyonel durumu SOC 2 Type I hazirlik bakisiyla siniflandirmaktir.

## Status Legend
- `Likely Covered (Technical)` = teknik kontrol mevcut, policy/evidence teyidi gerekli
- `Partially Covered` = teknik veya process parcali
- `Gap` = belirgin eksik
- `Not Evaluated` = Faz 0 kapsaminda incelenmedi

## Scope Assumption (Phase 0)
- Product: JurisFlow app + API + core integrations runtime
- Environments: production and staging (to be confirmed)
- Trust Services Criteria focus: Security (CC series)

## SOC 2 Security (CC) Readiness Matrix

| TSC / Control Area | Current Readiness | What Exists | Gaps / Evidence Needed | Suggested Owner |
|---|---|---|---|---|
| CC1 - Control Environment / governance | Partially Covered | Technical hardening and operational runbooks exist | Formal policy set, approval cadence, role accountability, training records | Security + Exec |
| CC2 - Communication and information | Partially Covered | Internal docs/runbooks, integration gates | Standard security response library, claim governance workflow, customer-facing trust docs | Security + Sales |
| CC3 - Risk assessment | Partially Covered | Risks identified across integrations/payments/efiling in engineering backlog | Formal risk register, periodic risk review cadence, acceptance criteria | Security + Eng |
| CC4 - Monitoring activities | Partially Covered | Health checks, logging, audit trace correlation, ops tooling | Alert effectiveness evidence, monitoring ownership matrix, review cadence | Ops + Security |
| CC5 - Control activities (change mgmt / SDLC) | Partially Covered | Code reviews/tests/build pipelines implied; phase gates for integrations documented | Formal change management policy, approval logs, deployment evidence set | Eng + Security |
| CC6 - Logical and physical access | Likely Covered (Technical) | Tenant isolation, RBAC policies, security-admin separation, auth controls | Access review process, user lifecycle evidence, MFA evidence completeness | Backend + Security + IT |
| CC7 - System operations | Likely Covered (Technical) | Rate limiting, kill switch, replay, retry/backoff, incident runbook, backup/restore flows | IR tabletop, DR restore cadence evidence, on-call procedures | Ops + Security |
| CC8 - Change management | Partially Covered | Engineering workflow exists; integration gate docs exist | Formal release approvals, segregation evidence, rollback policy records | Eng/Ops |
| CC9 - Risk mitigation (vendors/incidents/business continuity) | Partially Covered | Secret provider hardening, subprocessor concept not yet packaged, incident runbook | Vendor management policy, subprocessor registry process, pentest, BC/DR evidence | Security + Legal + Ops |

## Type I vs Type II Reality Check

### Type I (design of controls at a point in time)
Likely achievable after:
1. Policy set completion
2. Evidence pack assembly
3. Control owner assignment
4. External readiness assessment

### Type II (operating effectiveness over time)
Requires additional:
1. Recurring evidence collection cadence
2. Access review logs
3. Change approvals / deployment records
4. IR/DR exercises
5. Vulnerability management tracking

## Priority Remediation List (Phase 0 -> Phase 2 bridge)

### P0 (this quarter)
1. Build formal control owner matrix and security policy set (access control, change mgmt, IR, backup/DR, vendor mgmt).
2. Establish evidence vault and capture baseline evidence in `PHASE0_EVIDENCE_INVENTORY.md`.
3. Create claim governance / approved wording workflow (`PHASE0_CLAIM_REGISTER.md`).
4. Launch subprocessor registry and DPA baseline package (Phase 1 trust package prerequisite).

### P1
1. Run IR tabletop and store evidence.
2. Run full DR restore test and store evidence.
3. Commission third-party penetration test and retain summary letter.
4. Define quarterly access review and vendor review cadence.

### P2
1. Engage SOC 2 readiness/audit firm.
2. Freeze audit scope and system description.
3. Start Type I evidence collection window.

## Do Not Claim (until evidence exists)
- SOC 2 certified / attested
- Annual pentest completed
- Regular DR restore testing (unless recurring evidence captured)
- 24/7 security monitoring (unless staffed/contracted and evidenced)
