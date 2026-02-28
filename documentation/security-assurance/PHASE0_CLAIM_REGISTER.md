# Phase 0 - Claim Register (Public Security/Compliance Messaging)

Amaç: Marketing / Sales / Security ekiplerinin tek bir onayli guvenlik ifade listesi kullanmasi.

## Claim Status Legend
- `Approved` = Public use OK (current evidence exists)
- `Internal Only` = Sales/NDA context only
- `Conditional` = Public use only with qualifier
- `Blocked` = Kullanma
- `Planned` = Henüz iddia edilmez

## Public Claim Rules
1. Abarti yok: `certified`, `compliant`, `attested` ifadeleri yalnizca resmi kanit varsa kullanilir.
2. "In progress" ifadeleri tarih ve kapsam ile birlikte kullanilir.
3. Teknik capability ile operasyonel process ayrimi korunur (ornegin "IR runbook exists" != "24/7 SOC").

## Claim Register

| Claim ID | Candidate Wording | Status | Allowed Channel | Required Evidence | Owner | Notes |
|---|---|---|---|---|---|---|
| CLM-001 | "Tenant-aware access controls and scoped data isolation are implemented in core admin, payments, and integration workflows." | Conditional | Sales / Security FAQ / Trust Center (technical overview) | EV-IDA-001, EV-IDA-002 | Security + Backend | Use "core workflows" qualifier until full coverage attested |
| CLM-002 | "Audit logs support tamper-evident hash-chain integrity verification." | Conditional | Trust Center / Security Overview | EV-AUD-001 | Security | Must avoid saying "immutable" unless prod flag evidence captured |
| CLM-003 | "Encryption for documents and selected database fields is supported and can be enforced by deployment configuration." | Approved | Trust Center / Security Overview | EV-ENC-001, EV-ENC-002 | Platform | Phrase as capability + deployment configuration |
| CLM-004 | "Production deployments enforce hardened integration secret provider settings (KMS/Key Vault)." | Internal Only | Security questionnaire / enterprise review | config proof + startup validation evidence | Platform/Security | Avoid public wording until prod evidence pack exists |
| CLM-005 | "Integration operations include replay controls, kill switches, and audit trace correlation." | Conditional | Security overview / enterprise review | EV-OPS-004, EV-OPS-005 | Backend/Ops | Show as platform operations controls |
| CLM-006 | "Trust accounting workflows include three-way reconciliation support and period-lock enforcement." | Conditional | Product security + finance controls overview | EV-TRU-001, EV-PAY-002 | Finance/Backend | Product/financial control claim, not certification claim |
| CLM-007 | "Court filing precheck supports jurisdiction-specific rule packs with human review for low-confidence scenarios." | Conditional | Product security / enterprise demos | EV-RUL-001 | Product/Backend | Frame as workflow safeguard |
| CLM-008 | "We maintain documented incident response and backup/restore procedures." | Approved | Trust Center / questionnaire | runbooks + EV-BKP-002 | Security/Ops | Do not imply tested cadence unless evidence exists |
| CLM-009 | "We complete regular disaster recovery restore tests." | Blocked | None | EV-BKP-003 recurring evidence | Security/Ops | Enable only after cadence is established |
| CLM-010 | "SOC 2 certified" | Blocked | None | SOC 2 report | Exec/Security | Illegal/misleading until report exists |
| CLM-011 | "SOC 2 audit in progress" | Planned | Trust Center / sales | auditor engagement proof + target date | Exec/Security | Only after audit engagement signed |
| CLM-012 | "Independent third-party penetration testing is performed annually." | Planned | Trust Center / questionnaire | EV-EXT-001 | Security | Activate after first test completes |

## Disallowed / High-Risk Wording (Do Not Use)
- "Bank-grade security"
- "Fully compliant with all regulations"
- "SOC 2 certified" (until report received)
- "End-to-end encrypted" (unless exact scope defined and validated)
- "Zero trust architecture" (unless formally implemented and documented)

## Approved Qualifier Examples
- "Supported by configuration"
- "Implemented in core workflows"
- "Available for enterprise deployments"
- "Documented process"
- "In progress (target: YYYY-MM)"

## Sign-off Workflow (Phase 0)
1. Security proposes wording.
2. Backend/Platform validates technical accuracy.
3. Legal approves external wording.
4. Sales enablement receives approved version and usage notes.
