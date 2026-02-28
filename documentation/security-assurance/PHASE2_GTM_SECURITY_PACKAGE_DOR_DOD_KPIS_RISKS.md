# Phase 2 - GTM Security Package (DoR / DoD / KPIs / Risks)

Amac: Faz 2'nin "trust center + gated docs + questionnaire ops" teslimini net kabul kriterleriyle yonetmek.

Bu dokuman, engineering/security/legal/sales arasinda ortak kabul kapisidir.

## Phase 2 Scope (GTM security packaging)
- Trust Center gated docs (NDA sonrasi erisim)
- Questionnaire automation workflow (CRM integration)
- Quarterly assurance report
- ISO 27001 / second standard evaluation

## DoR (Before Starting Phase 2)

Faz 2 baslamis sayilmaz unless all are true:

- [ ] Mevcut kontrollerin teknik sahipleri belli
- [ ] Security / Legal / Sales owner atanmis
- [ ] Public claim onay mekanizmasi tanimli

Operational proof references:
- `PHASE0_CONTROL_INVENTORY.md`
- `PHASE0_CLAIM_REGISTER.md`
- `PHASE1_SOC2_READINESS_REMEDIATION_LIST.md`

## DoD (Minimum GTM Güven Paketi)

Faz 2 "minimum complete" sayilmasi icin:

- [ ] Trust Center canli
- [ ] Security Overview PDF hazir
- [ ] Subprocessor page canli
- [ ] DPA + standard security responses hazir
- [ ] Pen test / DR / IR status net (var/yok + plan tarihi)
- [ ] Sales ekibi standart guvenlik yanit setini kullaniyor
- [ ] Security review turnaround KPI olculuyor

## KPI Set (Required)

Olculecek KPI'lar (target + owner atanacak):

| KPI | Definition | Source | Cadence | Owner | Data Quality |
|---|---|---|---|---|---|
| Security questionnaire turnaround time (median) | `DeliveredAt - RequestedAt` median | CRM workflow | Weekly/Quarterly | Security Ops | exact (if timestamps instrumented) |
| Security questionnaire turnaround time (p90) | same metric p90 | CRM workflow | Weekly/Quarterly | Security Ops | exact |
| Enterprise deal security-blocked days | total blocked duration on security review requests | CRM workflow | Quarterly | RevOps + Security | derived |
| Security review win-rate impact | win rate comparison with/without security friction | CRM + RevOps | Quarterly | RevOps | derived/proxy (document assumptions) |
| Evidence request fulfillment SLA | % evidence requests fulfilled within SLA | CRM / request log | Weekly/Quarterly | Security | exact/derived |
| Time-to-redline security clauses | contract/security addendum redline cycle time | CRM + legal tracker | Monthly/Quarterly | Legal Ops | derived |
| Trust Center doc access / usage | visits/downloads/gated access events | Trust center logs | Monthly/Quarterly | Security + Web | exact |
| Open critical security remediation count | count of open critical issues | vuln tracker | Weekly/Quarterly | Security + Eng | exact |

## KPI Guardrails (interpretation)
- `proxy` or `derived` KPI'lar raporda etiketlenmeli.
- KPI target yoksa "green/yellow/red" raporu anlamsiz olur -> target zorunlu.
- Win-rate impact attribution varsayimlari raporda acik yazilmali.

## Risk Register (Phase 2 specific)

| Risk ID | Risk | Impact | Likelihood | Early Signal | Mitigation | Owner |
|---|---|---|---|---|---|---|
| P2-RSK-001 | Abartili claim riski (`SOC2-ready` gibi muğlak ifade) | High | Medium | Sales wording drift / unapproved decks | Claim register enforcement + approval wording library | Security + Sales |
| P2-RSK-002 | Dokuman bayatlama (Trust Center stale docs) | High | High | review due dates passed | doc freshness owner + cadence + stale alerts | Security |
| P2-RSK-003 | Tek kisi bottleneck (questionnaire answers) | Medium | High | backlog aging / SLA misses | response library + SME rotation + CRM workflow | Security Ops |
| P2-RSK-004 | Kanit daginikligi | High | Medium | evidence requests slow / missing artifacts | evidence vault standard + evidence inventory updates | Security + Ops |
| P2-RSK-005 | NDA-gated docs over-sharing | High | Medium | manual email shares outside process | gated access workflow + access logging + revocation | Security + Legal |

## Phase 2 Governance Cadence
- Weekly: Security ops + Sales enablement (questionnaire SLA, blockers)
- Bi-weekly: Legal + Security (DPA/clauses/subprocessors)
- Monthly: Trust Center content freshness review
- Quarterly: Assurance report executive review

## Evidence Expectations for Phase 2 Completion
- Trust center screenshots / URLs
- Gated access workflow proof (approved + denied sample)
- Security overview PDF version artifact
- Subprocessor page publication artifact
- DPA + standard response library versions
- KPI report snapshots
- Sales enablement communication / training confirmation

## Decision Log (fill during execution)
- YYYY-MM-DD:
- YYYY-MM-DD:
