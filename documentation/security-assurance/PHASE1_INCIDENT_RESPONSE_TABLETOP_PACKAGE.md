# Phase 1 - Incident Response Tabletop Package

Amac: `documentation/INCIDENT_RUNBOOK.md` varligini operasyonel kanita cevirmek.

Bu dokuman tabletop exercise plan + facilitation + evidence output beklentisini tanimlar.

## Deliverable (Phase 1)
- Tabletop exercise tamamlanmis
- Minutes + timeline + action items kaydedilmis
- `EV-IR-001` evidence artifact'i olusmus

## Exercise Format (Recommended)
- Duration: 60-90 min
- Participants:
  - Incident Commander (Ops)
  - Security lead
  - Backend lead
  - Platform/infra
  - Support/Sales liaison
  - Optional: Legal/Privacy
- Mode: discussion-based tabletop (no live production disruption)

## Scenario Set (Run at least 1 in Phase 1)

### Scenario A (Recommended First): Payment + Integration Incident
Trigger:
- Stripe webhook retries spike + duplicate delivery attempts
- Simultaneous integration replay activity and elevated error rates

Test objectives:
- Incident declaration and severity classification
- Kill switch decision and execution
- Customer impact assessment
- Audit trace/correlation usage
- Recovery validation and communication

### Scenario B: Suspected Tenant Isolation Regression
Trigger:
- QA/support reports cross-tenant data visibility in a critical flow

Test objectives:
- SEV classification
- Access restriction / feature disablement
- Evidence preservation
- Legal/customer notification decision tree

### Scenario C: Backup Restore Required
Trigger:
- Data corruption in billing/legal-billing tables discovered post-deploy

Test objectives:
- Dry-run restore decision
- Restore runbook use
- RTO/RPO communication
- Post-incident evidence capture

## Tabletop Agenda (90 min template)
1. (0-10) Scenario brief + assumptions
2. (10-25) Detection and triage
3. (25-45) Containment/mitigation decisions
4. (45-60) Recovery and validation
5. (60-75) Communications (internal/customer/legal)
6. (75-90) Gaps, action items, ownership

## Facilitation Script (minimum prompts)
- How is severity assigned and by whom?
- What immediate containment action is available? (kill switch, feature disable, rollback)
- Which logs/metrics prove impact scope?
- What data is needed before customer communication?
- What is the rollback/restore threshold?
- What evidence must be preserved for audit/postmortem?

## Evidence to Collect (for EV-IR-001)
- Meeting invite / attendee list
- Scenario doc used
- Time-stamped notes
- Decisions made (severity, containment, comms)
- Action items with owners and due dates
- Updated runbook change (if any)

Suggested artifact filenames:
- `YYYYMMDD-secops-ir-tabletop-attendance.png`
- `YYYYMMDD-secops-ir-tabletop-minutes.md`
- `YYYYMMDD-secops-ir-tabletop-actions.csv`

## Tabletop Minutes Template

### Metadata
- Date (UTC):
- Scenario:
- Facilitator:
- Scribe:
- Participants:

### Timeline
- T+00:
- T+10:
- T+20:

### Decisions
- Severity:
- Containment decision:
- Customer communication decision:
- Escalation decision:

### What Worked
- 

### Gaps Identified
- 

### Action Items
| ID | Action | Owner | Due Date | Severity |
|---|---|---|---|---|
| IR-ACT-001 |  |  |  |  |

## Exit Criteria (Phase 1)
- [ ] En az bir tabletop tamamlandi
- [ ] Evidence vault'a artifact yüklendi
- [ ] Action items owner+date ile kaydedildi
- [ ] `INCIDENT_RUNBOOK.md` gerekiyorsa guncellendi
