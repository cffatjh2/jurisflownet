# Phase 1 - SOC 2 Readiness Gap Remediation List

Amac: `PHASE0_CERTIFICATION_GAP_MATRIX_SOC2.md` icindeki bosluklari sprint/owner bazli uygulanabilir backlog'a cevirmek.

Bu dokuman audit raporu degil; readiness execution planidir.

## Scope (Phase 1)
- SOC 2 Security criteria (CC series) readiness
- Design of controls + evidence readiness
- Type I audit oncesi minimum operasyonel olgunluk

## Execution Rules
1. Her remediation item owner + due date alir.
2. "Done" sayilmasi icin ilgili evidence ID en az bir artifact ile baglanir.
3. Policy yazmak tek basina yeterli degil; uygulanma kaniti gerekir.

## Priority Bands
- `P1-Now` = bu fazda baslatilacak/bitirilecek
- `P1-Next` = bu fazda planlanacak, sonraki faza devredebilir
- `Tracked` = takipte, ancak Faz 1 kapsam disi

## Remediation Backlog

| Item ID | TSC | Priority | Remediation | Deliverable | Evidence Link(s) | Owner | Target |
|---|---|---:|---|---|---|---|---|
| SOC2-RM-001 | CC1/CC3 | P1-Now | Security control owner matrix resmi hale getir | owner matrix doc + approvals | EV-* mapping table | Security | TBD |
| SOC2-RM-002 | CC1/CC5 | P1-Now | Core policy set tamamla (access, change mgmt, IR, backup/DR, vuln mgmt, vendor mgmt) | policy bundle v1 | policy approval records | Security + Legal + Eng | TBD |
| SOC2-RM-003 | CC2 | P1-Now | Claim governance + approved wording library operationalize et | signed claim register + usage process | `PHASE0_CLAIM_REGISTER.md` sign-off | Security + Sales + Legal | TBD |
| SOC2-RM-004 | CC4/CC7 | P1-Now | Monitoring ownership + alert review cadence tanimla | monitoring responsibility matrix | EV-OPS-001 + review logs | Ops | TBD |
| SOC2-RM-005 | CC5/CC8 | P1-Now | Change management evidence standardi | release approval template + change log retention rule | build/deploy approval artifacts | Eng/Ops | TBD |
| SOC2-RM-006 | CC6 | P1-Now | Quarterly access review process define et | access review SOP + schedule | first review record (Phase 2 if needed) | Security + IT | TBD |
| SOC2-RM-007 | CC6 | P1-Now | MFA coverage verification and claim scope finalize et | MFA capability/test evidence + claim update | EV-IDA-003 | Security + QA | TBD |
| SOC2-RM-008 | CC7 | P1-Now | Incident tabletop calistir ve kanitla | tabletop deck + minutes + action items | EV-IR-001 | Sec/Ops | TBD |
| SOC2-RM-009 | CC7/CC9 | P1-Now | Full DR restore test evidence pack uret | restore test report + checklist | EV-BKP-003 | Platform/Ops | TBD |
| SOC2-RM-010 | CC9 | P1-Now | Vulnerability management SLA ve reporting cadence yazili hale getir | SLA doc + reporting template | vuln SLA artifacts | Security + Eng | TBD |
| SOC2-RM-011 | CC9 | P1-Now | Pentest vendor secimi + scope finalize et | vendor scorecard + SOW-ready scope | EV-EXT-001 (future) | Security | TBD |
| SOC2-RM-012 | CC2/CC9 | P1-Next | Subprocessor registry + DPA package public/NDA workflow | subprocessor list + DPA template + process | trust package evidence | Legal + Security | TBD |
| SOC2-RM-013 | CC4 | P1-Next | Security review turnaround KPI tracking baslat | KPI definition + reporting sheet/dashboard | process metrics logs | Security Ops | TBD |
| SOC2-RM-014 | CC5 | Tracked | SDLC secure coding/training record standardi | training records + onboarding checklist | HR/LMS evidence | Eng Mgmt | TBD |

## Acceptance Criteria (Phase 1 Done for Readiness)
- [ ] En az `SOC2-RM-001..011` icin owner + date atanmis
- [ ] `SOC2-RM-002`, `008`, `009`, `010`, `011` deliverable'lari olusmus
- [ ] Her tamamlanan item ilgili evidence ID ile baglanmis
- [ ] "Blocked" claim listesi sales ekibiyle paylasilmis
- [ ] Type I readiness kickoff icin external auditor brief hazirlanabilir durumda

## Dependency Notes
- `SOC2-RM-008` (IR tabletop) ve `SOC2-RM-009` (DR restore) kanitlari olmadan CC7 claim'leri zayif kalir.
- `SOC2-RM-010` olmadan vulnerability response sorularina tutarli procurement cevabi verilemez.
- `SOC2-RM-011` olmadan "annual pentest" planned durumdan cikmaz.

## Weekly Review Cadence (Phase 1)
- Haftalik 30 dk: Security + Platform + Backend
- Iki haftada bir: Legal + Sales enablement checkpoint
- Aylik: Exec readiness review (red/yellow/green)
