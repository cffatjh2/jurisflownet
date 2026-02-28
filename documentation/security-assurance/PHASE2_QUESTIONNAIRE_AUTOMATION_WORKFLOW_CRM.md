# Phase 2 - Security Questionnaire Automation Workflow (CRM Integration)

Amac: Security questionnaire cevaplama surecini kisiden bagimsiz, izlenebilir ve KPI olculebilir hale getirmek.

Bu dokuman, CRM ile entegre "security review request -> response -> approval -> delivery" workflow'unu tanimlar.

## Phase 2 Deliverable
- CRM tabanli questionnaire request workflow
- Standard response library + ownership
- SLA tracking ve escalation kurallari
- Metrics extraction for P2 KPIs

## 1) Core Workflow (Target State)

1. Opportunity reaches security review stage in CRM.
2. Sales creates `Security Review Request`.
3. Request auto-populates:
   - account/opportunity metadata
   - NDA status
   - segment (SMB/Mid/Enterprise)
   - requested artifacts/questionnaire type
4. Security triages request and assigns package level.
5. Standard answers are auto-suggested from response library.
6. SMEs review exceptions/new questions.
7. Legal reviews contractual/security clauses if needed.
8. Final response package delivered and logged.
9. Turnaround metrics recorded automatically.

## 2) CRM Object Model (Minimum)

### `SecurityReviewRequest`
Fields:
- `RequestId`
- `AccountId`
- `OpportunityId`
- `StageAtRequest`
- `NdaStatus`
- `RequesterName`
- `RequesterEmail`
- `QuestionnaireType` (SIG Lite, CAIQ-lite, custom spreadsheet, portal form)
- `PackageLevel` (standard, expanded, regulated)
- `Status` (`new`, `triage`, `in_progress`, `waiting_sme`, `waiting_legal`, `delivered`, `blocked`, `closed`)
- `Priority`
- `DueDate`
- `SubmittedAt`
- `DeliveredAt`
- `OwnerSecurity`
- `OwnerSales`
- `EscalationLevel`
- `BlockerReason`
- `RiskFlagsJson`

### `SecurityReviewArtifactRequest` (optional child)
- `SecurityReviewRequestId`
- `ArtifactType` (DPA, pentest summary, diagram, policy summary, etc.)
- `Classification` (public / gated / internal)
- `Requested`
- `Approved`
- `Delivered`

### `SecurityQuestionItem` (optional child for custom questionnaires)
- `SecurityReviewRequestId`
- `QuestionHash`
- `QuestionText`
- `Category`
- `ResponseSource` (library / manual / legal)
- `ResponseVersion`
- `Status`

## 3) Response Library Model (Operational)

Response library must be versioned and owned.

Minimum metadata per answer:
- `AnswerId`
- `QuestionPattern` / canonical question
- `ApprovedAnswerText`
- `ScopeNotes`
- `AllowedUse` (public / nda-only / internal)
- `Owner`
- `LegalReviewRequired`
- `LastReviewedAt`
- `NextReviewDue`
- `SourceEvidenceIds`
- `RelatedClaims`

Rule:
- If no approved answer exists, workflow routes to SME and creates new draft answer item.

## 4) SLA and Escalation Rules

### Default SLA Targets
- Initial triage: `1 business day`
- Standard package turnaround: `2 business days`
- Expanded enterprise package: `5 business days`
- Legal clause redline response: `3 business days`

### Escalations
- Triage overdue -> Security manager
- Delivery overdue -> Sales owner + Security manager
- Legal blocked > 2 business days -> Legal lead escalation

## 5) Automation Points (What to Automate First)

### P2 Minimum Automation
1. Stage-triggered CRM task creation
2. NDA status validation check
3. Template package recommendation
4. SLA timers and reminders
5. Status transitions and timestamps
6. Delivery event logging

### P2+ (Later)
1. Auto-fill spreadsheet answers from response library
2. Similar-question matching / answer suggestion
3. Customer portal delivery + gated doc link generation
4. Contract clause analytics

## 6) KPI Instrumentation (Required Fields)

To support stated KPIs, log these timestamps:
- `RequestedAt`
- `TriagedAt`
- `FirstResponseAt`
- `DeliveredAt`
- `ClosedAt`
- `BlockedStartedAt` / `BlockedEndedAt`

Derived metrics:
- `Security questionnaire turnaround time` (median/p90)
- `Enterprise deal security-blocked days`
- `Evidence request fulfillment SLA`
- `Time-to-redline security clauses`

## 7) Exception Handling
- No NDA: only public docs + high-level answers; block gated docs
- Custom aggressive terms: route to legal, status `waiting_legal`
- Missing evidence for requested claim: route to `blocked`, update claim register if needed
- Outdated response library answer: route to `waiting_sme`

## 8) Auditability / Traceability

Every delivered questionnaire package should retain:
- who approved
- which answer versions were used
- which evidence IDs backed answers
- which gated docs were shared
- timestamps for SLA calculations

This materially reduces enterprise review rework and supports trust posture reporting.

## 9) Phase 2 Acceptance Criteria (Questionnaire Automation Track)
- [ ] CRM object/state model agreed
- [ ] Response library ownership and review cadence assigned
- [ ] SLA timers and escalation rules defined
- [ ] KPI timestamp fields defined
- [ ] Standard package workflow documented end-to-end
