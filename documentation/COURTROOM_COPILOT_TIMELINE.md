# Courtroom Copilot Timeline

## Product Thesis (High-Impact Differentiator)

`Courtroom Copilot Timeline` is an explainable, citation-backed timeline and strategy engine that adapts to:
- case type
- state/federal jurisdiction
- court/division/venue
- procedural posture (filing, service, motion, hearing, trial, appeal stage)

This is not just a calendar helper. It is a `procedural intelligence layer` that generates:
1. deadline timeline
2. task/checklist recommendations
3. filing package readiness steps
4. strategy prompts (explainable, citation-backed)
5. risk/conflict flags

The key differentiator is explainability:
- every computed deadline or recommendation must show `why` (rule source + citation + rule-pack version + assumptions + confidence)

## Why This Matters (Competitive Positioning)

Most legal practice tools stop at:
- task templates
- court date calendar sync
- reminders

This feature creates a higher-value product layer:
- court-specific precheck + docket automation + filing workflow + timeline strategy in one surface
- explainable output that partners/associates can trust and audit
- review queue integration for low-confidence or conflicting rules

## Core Product Promise

Given a matter and court context, generate a living timeline that:
- is procedural-stage aware
- is court/jurisdiction aware
- is explainable with citations
- updates when facts/docket events change
- routes uncertain items to human review

## Inputs (Required / Optional)

### Required Inputs (MVP)
- `MatterId`
- `CaseType`
- `JurisdictionCode` (e.g., state/federal)
- `CourtSystem`
- `CourtId` or (`division` + `venue`)
- `TriggerEventType` (complaint filed, service completed, hearing set, order issued, etc.)
- `TriggerEventDate`

### Optional Inputs (MVP+)
- `Judge` / `department`
- `Service method`
- `Party count / party type`
- `Motion type`
- `Filing method` (e-filing / manual)
- `Local standing order flags`
- `Holiday calendar overrides`
- `Firm strategy profile`

### Dynamic Inputs (Continuous)
- Docket events (CourtListener/RECAP)
- E-filing submission outcomes (accepted/rejected/corrected)
- Matter tasks and completion state
- Human overrides / approved timeline edits

## Outputs

### 1) Timeline Milestones
Each milestone should include:
- title
- due date / due window
- computed date basis
- dependency chain (what this depends on)
- severity / criticality
- status (`draft`, `active`, `completed`, `blocked`, `needs_review`)
- confidence score
- explanation text

### 2) Citation Pack (Required for Explainability)
Each milestone/recommendation must reference one or more citations:
- source type (`statute`, `court_rule`, `local_rule`, `standing_order`, `partner_guide`, `internal_rule_pack`)
- citation text / identifier
- source URL or document reference
- rule pack version
- effective date range

### 3) Strategy Prompts (Explainable, not opaque)
Examples:
- "Service deadline approaching; consider alternative service motion if service attempts fail"
- "Motion filing window overlaps local courtesy-copy deadline"
- "Rejection repair likely due to PDF format rule mismatch; verify local naming/format requirement"

Each prompt must include:
- rationale
- citation(s)
- confidence
- review requirement flag

### 4) Review Queue Items
Auto-create review items when:
- rule conflict exists
- low confidence
- missing court coverage
- local rule override incomplete
- docket event ambiguity

## Explainability Contract (Non-Negotiable)

The engine must not produce "magic" dates without traceability.

Every generated item should be inspectable via:
- `source rules used`
- `calculation steps`
- `assumptions`
- `timezone/holiday treatment`
- `rule-pack version`
- `human override history`

If no citation is available:
- mark as `needs_review`
- do not present as authoritative deadline

## Architecture (Built on Existing Foundations)

This feature should reuse existing platform work:
- Jurisdiction Rules Platform (rule packs, coverage matrix, confidence, harness)
- E-filing workflow APIs and review queue
- Docket ingestion (CourtListener/RECAP)
- Docket-to-task/deadline automation
- Audit trace + review/conflict queue

### Proposed Engine Layers

#### Layer 1: Deterministic Rule Engine (primary)
- Uses `JurisdictionRulePack` + `CoverageMatrix`
- Calculates deadlines/windows via explicit procedural rules
- Produces citations and confidence

#### Layer 2: Contextual Strategy Engine (secondary)
- Produces strategic prompts/checklists
- Can use AI assistance, but only with citation-constrained inputs
- Must never silently override deterministic deadlines

#### Layer 3: Timeline Orchestrator
- Merges:
  - docket events
  - filing events
  - rule outputs
  - task completion status
  - human overrides
- Emits timeline versions and review items

#### Layer 4: Audit / Explainability / Versioning
- snapshot each generated timeline version
- persist diff and source rules used
- allow replay/regeneration when rule packs change

## Proposed Domain Model (New)

### `CourtroomCopilotTimeline`
- `Id`
- `MatterId`
- `JurisdictionCode`
- `CourtSystem`
- `CourtId`
- `CaseType`
- `Status`
- `CurrentVersionId`
- `CreatedAt`, `UpdatedAt`

### `CourtroomCopilotTimelineVersion`
- `Id`
- `TimelineId`
- `VersionNumber`
- `GeneratedAt`
- `GeneratorType` (`rules_engine`, `rules_plus_ai`, `manual`)
- `RulePackIdsJson`
- `CoverageSnapshotJson`
- `InputSnapshotJson`
- `SummaryJson`
- `CorrelationId`

### `CourtroomCopilotTimelineItem`
- `Id`
- `TimelineVersionId`
- `Type` (`deadline`, `task`, `hearing`, `filing_prep`, `strategy_prompt`, `risk_flag`)
- `Title`
- `DueAtUtc` / `DueWindowStartUtc` / `DueWindowEndUtc`
- `Priority`
- `Status`
- `Confidence`
- `Explanation`
- `CalculationTraceJson`
- `NeedsHumanReview`

### `CourtroomCopilotTimelineItemCitation`
- `Id`
- `TimelineItemId`
- `SourceType`
- `CitationLabel`
- `CitationText`
- `SourceUrl`
- `RulePackId`
- `EffectiveFrom`, `EffectiveTo`

### `CourtroomCopilotTimelineOverride`
- `Id`
- `TimelineId`
- `TimelineItemId`
- `OverrideType` (`date`, `status`, `dismiss`, `add_manual_item`)
- `Reason`
- `ApprovedBy`
- `ApprovedAt`
- `CitationsJson` (optional manual references)

### `CourtroomCopilotScenarioSimulation` (MVP+)
- `Id`
- `TimelineId`
- `ScenarioType` (`service_delay`, `hearing_rescheduled`, `rejection_repair`, `extension_granted`)
- `InputDeltaJson`
- `ResultVersionId`

## APIs (Suggested)

### Timeline Generation / Retrieval
- `POST /api/courtroom-copilot/timelines/generate`
- `GET /api/courtroom-copilot/timelines/{timelineId}`
- `GET /api/courtroom-copilot/timelines/{timelineId}/versions`
- `GET /api/courtroom-copilot/timelines/{timelineId}/versions/{versionId}`

### Explainability
- `GET /api/courtroom-copilot/timeline-items/{itemId}/explanation`
- `GET /api/courtroom-copilot/timeline-items/{itemId}/citations`

### Overrides / Review
- `POST /api/courtroom-copilot/timeline-items/{itemId}/override`
- `POST /api/courtroom-copilot/timelines/{timelineId}/regenerate`
- `POST /api/courtroom-copilot/timelines/{timelineId}/simulate`

### Docket / Filing Hooks
- internal service hooks from:
  - CourtListener/RECAP ingestion
  - e-filing status changes
  - rejection repair workflow

## UX Concept (Phase E-ish Product Surface)

### 1) Timeline Board (main)
Views:
- chronological timeline
- grouped by stage (filing/service/discovery/motions/trial)
- grouped by risk (critical soon / blocked / needs review)

Each item card shows:
- due date
- status
- confidence
- "Why?" button -> explanation + citations

### 2) Strategy Pane
- top 5 procedural risks
- next best actions
- filing readiness checklist
- rejection repair suggestions (if e-filing event exists)

### 3) Rule Trace Drawer
- rule pack version(s)
- citations used
- calculation path
- assumptions
- local overrides
- timeline diff from previous version

### 4) Review Queue Integration
- low-confidence items open directly in review queue
- approved overrides regenerate timeline

## MVP Scope (Pragmatic)

Start with a narrow but high-value slice:

### MVP v1 (1-2 jurisdictions + 1-2 case types)
- Jurisdiction rule-pack backed deadline generation
- Citation-backed explainability
- Docket event -> timeline regeneration
- Review queue on low-confidence / rule gaps
- Basic UI timeline + explanation drawer

Candidate starter scope:
- State civil litigation (one high-volume state)
- Federal civil procedure (limited subset)

### MVP v1 Exclusions (explicit)
- Judge-specific behavioral recommendations
- Predictive outcome scoring
- Fully automated strategy advice without review
- Broad 50-state coverage at launch

## Risk & Safety Guardrails

### Legal / Professional Risk
- Position as procedural support, not legal advice
- Require human review for low-confidence or missing coverage
- Preserve citations for every authoritative date/recommendation
- Track user overrides and approvals

### Operational Risk
- Rule packs can drift -> require effective dates + versioning + review queue
- Timezone/holiday bugs -> explicit timezone and holiday source in calculation trace
- Court coverage gaps -> coverage matrix and confidence labels in UI

### Trust / Auditability
- Every generated version must be reproducible from:
  - input snapshot
  - rule pack versions
  - calendar assumptions

## Metrics (Feature Success)

### Product Metrics
- Timeline generation success rate
- % timeline items with citation coverage
- % low-confidence items routed to review
- Review resolution time for timeline items
- Timeline regeneration frequency per matter

### Outcome Metrics
- Deadline miss reduction (proxy and exact where possible)
- Filing rejection repair cycle time
- Time-to-prep for hearings/motions
- User adoption (timeline views / explanation clicks)

### Quality Metrics
- False positive / false negative procedural alerts
- Rule conflict incidence rate
- Human override rate (by jurisdiction/case type)

## Implementation Roadmap (Suggested)

### Phase A - Foundations (backend)
1. Domain models + migrations (`Timeline`, `Version`, `Item`, `Citation`, `Override`)
2. Deterministic timeline generation service using rule packs
3. Explainability payload contract (calculation trace + citations)
4. Review queue hooks for low-confidence/gap/conflict

### Phase B - Integration Hooks
1. CourtListener/RECAP triggers timeline regeneration
2. E-filing status/rejection hooks update timeline items
3. Matter task sync (timeline item -> task)

### Phase C - UI
1. Matter-level Timeline panel
2. Explainability drawer ("Why?")
3. Timeline diff view (version compare)
4. Override + regenerate actions

### Phase D - Scale / Coverage
1. More jurisdictions/case types
2. Scenario simulation
3. AI-assisted strategy prompts (citation constrained)
4. Regression harness for timeline outputs

## DoR / DoD (for MVP v1)

### DoR
- Jurisdiction rule pack and coverage matrix exist for selected starter scope
- Citation sources and effective dates available
- Review queue workflow owner assigned
- Product/legal wording for "copilot" approved

### DoD
- Generates timeline for starter scope matters
- Every authoritative deadline has at least one citation
- Low-confidence items route to review queue
- Timeline regenerates on docket or e-filing update
- UI shows explanation + citation trail
- Audit trace/versioning persists generated outputs

## Positioning Language (Safe + Strong)

Recommended:
- "Citation-backed procedural timeline copilot"
- "Court- and jurisdiction-aware timeline generation with explainable rule traces"
- "Human-in-the-loop deadline and filing strategy support"

Avoid:
- "Fully automated litigation strategy"
- "Guaranteed court deadline accuracy"
- "AI legal advice"
