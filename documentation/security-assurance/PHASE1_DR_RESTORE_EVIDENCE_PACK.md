# Phase 1 - DR Restore Test Evidence Pack

Amac: Backup/restore capability'yi "feature exists" seviyesinden "tested and evidenced" seviyesine cikarmak.

Bu paket, `EV-BKP-003` icin gerekli restore test kanitlarinin toplanma standardidir.

## Test Objective
- Full restore (DB + optional uploads) surecinin calistigini gostermek
- Dry-run ve actual restore akisini belgelemek
- RTO/RPO olcumunu kaydetmek
- Restore sonrasi smoke test sonucunu kaydetmek

## Environment Strategy
- Preferred: isolated staging restore environment
- Dataset: representative tenant/matter/invoice/payment/trust/integration records
- Production backup kullaniliyorsa:
  - PII redaction policy uygulanmali veya access tightly controlled olmali

## Test Types

### Test 1 (Required): Dry Run Restore
- Endpoint: `POST /api/admin/backups/restore` with `dryRun=true`
- Expected: no mutation, validation passes, summary returned

### Test 2 (Required): Full Restore Apply
- Endpoint: `POST /api/admin/backups/restore` with `dryRun=false`
- Expected: successful restore and smoke test pass

### Test 3 (Optional but Recommended): Uploads Included Restore
- `includeUploads=true`
- Verify encrypted backups if enabled

## Preconditions Checklist
- [ ] `Backup:AllowRestore=true` (test environment)
- [ ] Backup artifact available (prefer encrypted)
- [ ] Environment snapshot/rollback plan prepared
- [ ] Test window approved
- [ ] Observer/scribe assigned
- [ ] Smoke test checklist prepared

## Execution Checklist (operator runbook)

### A. Pre-test Capture
- [ ] Record environment and app version
- [ ] Record backup file name and timestamp
- [ ] Record encryption settings state:
  - `Backup:EncryptBackups`
  - `Security:DocumentEncryptionEnabled`
  - `Security:DbEncryptionEnabled`

### B. Dry Run
- [ ] Execute dry-run restore
- [ ] Save request/response payload (redacted)
- [ ] Record duration
- [ ] Record validation warnings/errors

### C. Full Restore
- [ ] Execute restore apply
- [ ] Record start/end time
- [ ] Save logs
- [ ] Record any retries/errors

### D. Post-restore Smoke Test (minimum)
- [ ] `/health` OK
- [ ] Login flow works
- [ ] Tenant resolution works
- [ ] Billing page loads / invoice list accessible
- [ ] Trust reconciliation endpoint responds
- [ ] Payments stats endpoint responds
- [ ] Integrations ops runs/review queue endpoints respond

### E. Evidence Packaging
- [ ] Store logs/screenshots/payloads in evidence vault
- [ ] Complete summary report section below
- [ ] Update `PHASE0_EVIDENCE_INVENTORY.md` (`EV-BKP-003`)

## Evidence Artifacts (Required)
- Restore test plan/checklist used
- Dry-run response (redacted)
- Full restore response/logs
- Smoke test checklist with results
- Timing summary (RTO observed)
- Issues and remediation actions

## DR Restore Test Report Template

### Metadata
- Test date (UTC):
- Environment:
- Operator:
- Observer:
- Backup artifact:
- Include uploads:
- Encryption enabled (backup/docs/db):

### Timings
- Dry-run duration:
- Restore apply duration:
- App recovery validation duration:
- Observed RTO:
- Estimated RPO (based on backup timestamp):

### Results
- Dry-run: Pass / Fail
- Full restore: Pass / Fail
- Smoke test: Pass / Fail

### Smoke Test Results
| Check | Result | Notes |
|---|---|---|
| /health |  |  |
| login |  |  |
| tenant resolution |  |  |
| billing invoice list |  |  |
| trust reconciliation |  |  |
| payments stats |  |  |
| integrations ops |  |  |

### Issues / Follow-ups
- 

### Sign-off
- Platform/Ops:
- Security:

## Exit Criteria (Phase 1)
- [ ] Dry-run + full restore completed
- [ ] Evidence pack stored
- [ ] RTO/RPO documented
- [ ] Gaps tracked with owners
