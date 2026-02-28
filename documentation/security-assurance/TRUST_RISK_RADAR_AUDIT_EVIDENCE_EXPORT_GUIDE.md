# Trust Risk Radar Audit Evidence Export Guide

## Purpose
Standardize how Trust Risk Radar evidence is exported and shared for:
- internal audit
- customer security/compliance review
- incident postmortems
- policy tuning governance

## Endpoint
- `GET /api/legal-billing/trust-risk/evidence-export`

## Access
- `Admin` or `SecurityAdmin` only
- Tenant-scoped by query filters / tenant context

## Recommended Default Export (Quarterly)
- `days=90`
- `includeAuditLogs=true`
- `includeEvents=true`

## What Is Included
- `policyVersions`
- `holds`
- `releases` (release actions incl. pending dual approval)
- `overrides` (release/escalate override actions)
- `actions`
- `reviewLinks`
- `events`
- `auditLogs` (trust-risk actions, hash chain fields, parsed trace tags)
- `summary`
- `dataQuality`

## Evidence Integrity Notes
- Audit rows include:
  - `Sequence`
  - `Hash`
  - `PreviousHash`
  - `HashAlgorithm`
- Export also includes parsed trace tags (when present in audit `Details`)

## Operational Procedure
1. Export from Trust Risk Radar panel (`Export Evidence JSON`) or call API.
2. Save to evidence vault with immutable filename stamp.
3. Record:
   - requester
   - purpose
   - date range
   - tenant
   - operator
4. Attach export hash/checksum (optional but recommended).
5. Reference related tickets / incidents / questionnaire requests.

## Redaction and Sharing Rules
- Internal exports may include full event and audit details.
- Customer-facing exports should be reviewed for:
  - internal user IDs
  - internal notes not needed for evidence
  - unrelated matter/client identifiers
- If redaction is required, preserve original export in internal evidence vault and produce a derived redacted copy.

## Suggested Evidence Bundle (Trust Risk Radar)
1. Latest evidence export JSON (90 days)
2. Quarterly tuning review report
3. Current active policy version snapshot
4. Trust Risk Radar runbook
5. Trust Center feature-control narrative

## API Query Parameters (practical)
- `days`
- `policyLimit`
- `eventLimit`
- `holdLimit`
- `actionLimit`
- `auditLimit`
- `includeAuditLogs`
- `includeEvents`

## Caveats
- Hold statuses may be SLA-updated at export time (export triggers SLA transition refresh)
- Export is evidence-oriented; not a complete accounting ledger export
- Trace tags depend on audit detail enrichment being present for the action path
