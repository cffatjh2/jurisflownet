# Incident Runbook

## Severity Levels
- SEV0: Total outage, data integrity risk.
- SEV1: Major feature outage, limited workaround.
- SEV2: Partial degradation, workaround available.
- SEV3: Minor defect.

## Initial Response
1. Acknowledge alert and assign an incident owner.
2. Capture start time and scope (who is impacted, which services).
3. Check `/health` and recent logs.

## Triage Checklist
- Recent deploys or migrations?
- Auth failures or rate limit spikes?
- Payment gateway failures?
- Background jobs failing?

## Mitigation Steps
- Roll back the last deployment if necessary.
- Disable scheduled jobs via `Operations:JobsEnabled=false`.
- Temporarily disable payment workflows if Stripe is degraded.
- Disable new sign-ins if auth is unstable.

## Recovery
- Validate services are stable.
- Run smoke tests on login, billing, and trust reconciliation.
- Confirm logs are flowing to the central sink.

## Post-Incident
- Write a short postmortem (root cause, timeline, action items).
- Rotate credentials if any exposure is suspected.
- Add monitoring for the new failure mode.
