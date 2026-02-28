# Phase 2 - ISO 27001 / Secondary Standard Assessment (Enterprise Requirement Driven)

Amac: SOC 2 disinda ikinci bir standardin (genelde ISO 27001) gerekip gerekmedigini ve hangi kosullarda onceliklendirilecegini kararlandirmak.

Bu dokuman sertifikasyon plani degil; karar ve kapsam degerlendirme aracidir.

## Why this exists
Enterprise musteriler farkli standartlar isteyebilir:
- SOC 2 (US SaaS common)
- ISO 27001 (global/procurement heavy organizations)
- Sector/customer-specific frameworks

Yanlis strateji:
- Her standarda ayni anda kosmak

Dogru strateji:
- Pipeline ve musteri taleplerine gore ikinci standardi secmek

## Candidate Standards (initial)
- ISO 27001 (default candidate)
- ISO 27701 (privacy extension, later)
- Customer-specific controls mapping pack (instead of full certification)

## Decision Inputs (Required)
1. Enterprise pipeline demand (% opportunities asking for ISO 27001)
2. Lost deals due to missing certification/attestation
3. Geo mix (US vs EU/EMEA)
4. Procurement cycle friction type (policy docs vs formal cert)
5. Internal owner/budget capacity

## Assessment Matrix (Score 1-5)

| Criterion | Weight | ISO 27001 | Customer-Specific Mapping Pack | Notes |
|---|---:|---:|---:|---|
| Enterprise demand frequency | 20 |  |  | |
| Revenue impact of missing standard | 20 |  |  | |
| Reuse of existing SOC 2 controls/evidence | 15 |  |  | |
| Time to credible market signal | 15 |  |  | |
| Internal effort / operational overhead | 10 |  |  | lower score = higher cost |
| Ongoing maintenance burden | 10 |  |  | |
| International expansion support | 10 |  |  | |

Decision recommendation:
- Preferred path:
- Decision date:
- Approvers:

## ISO 27001 Readiness Snapshot (High-Level)

### Likely Reusable from current program
- Technical controls (access, logging, encryption, backups, ops safeguards)
- Evidence discipline (Phase 0/1 artifacts)
- Incident/DR documentation
- Vulnerability management process (Phase 1)
- Vendor/pentest governance foundations

### Likely Additional Work
- ISMS governance and scope statement
- Asset inventory formalization
- Risk treatment methodology
- Statement of Applicability (SoA)
- Internal audit and management review cadence
- Training and awareness evidence
- Supplier evaluation cadence formalization

## Enterprise Requirement Decision Tree (Pragmatic)

Use this before committing to ISO.

1. Is a formal certification explicitly required in >= 2 strategic opportunities?
   - No -> provide SOC2/trust package + mapping responses
   - Yes -> go to 2
2. Are deal sizes/time-to-close large enough to justify program cost?
   - No -> use customer-specific mapping pack first
   - Yes -> go to 3
3. Do we have owner/budget capacity for 9-12 month compliance program?
   - No -> defer with documented roadmap and date
   - Yes -> start ISO 27001 scoping

## Phase 2 Deliverable (Minimum)
- Formal written decision on second standard path (ISO 27001 now / later / not now)
- Evidence-backed rationale
- Budget/owner placeholder
- Decision review date

## If ISO 27001 is selected (Next Steps, not Phase 2 completion)
1. Define ISMS scope
2. Appoint ISMS owner
3. Run gap assessment (Annex A controls + clauses)
4. Create roadmap and milestone plan
5. Select auditor/certification body

## Phase 2 Acceptance Criteria (Secondary Standard Track)
- [ ] Demand data inputs captured (pipeline/lost deals)
- [ ] Assessment matrix scored
- [ ] Decision recorded with approvers
- [ ] Next review date set
