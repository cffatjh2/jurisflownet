# Operations Runbook

## Centralized Logging
JurisFlow uses Serilog with console output and optional Seq sink.

Configure in `appsettings.json` or environment variables:
- `Serilog:WriteTo:1:Args:serverUrl` (Seq URL)
- `Serilog:WriteTo:1:Args:apiKey` (Seq API key)

## Monitoring and Health Checks
Health endpoint:
- `GET /health`

Recommended monitors:
- API availability (`/health`).
- Error rate from logs.
- Background job success (payment plans, SMS, email queue).

## Integration Delivery Gates (DoR / DoD)
Before rolling out integration phases/providers (QBO, Xero, DMS, CourtListener, e-filing partners), use:
- `documentation/INTEGRATION_PHASE_GATES.md`
- `documentation/INTEGRATION_GATE_REVIEW_TEMPLATE.md`

These define mandatory readiness/completion checks, replay/kill-switch validation, and evidence collection.

## Rate Limiting
Global token bucket limiter is enabled. Auth endpoints use a stricter fixed window policy.

Auth endpoints:
- `/api/login`
- `/api/client/login`
- `/api/auth/refresh`
- `/api/client/refresh`

## Security Headers
The API sets baseline security headers:
- `X-Content-Type-Options: nosniff`
- `X-Frame-Options: DENY`
- `Referrer-Policy: no-referrer`
- `Permissions-Policy`

Optional CSP:
- Configure `Security:ContentSecurityPolicy`.

## Job Scheduler
The Operations job runner processes:
- Payment plan AutoPay runs.
- SMS reminders.
- Outbound email queue.

Config keys:
- `Operations:JobsEnabled`
- `Operations:JobIntervalMinutes`
- `Operations:PaymentPlanBatchSize`

## Email Delivery
Outbound email queue is sent via SMTP.
Required config:
- `Email:Enabled`
- `Email:FromAddress`
- `Email:Smtp:Host`
- `Email:Smtp:Port`
- `Email:Smtp:Username`
- `Email:Smtp:Password`
- `Email:Smtp:UseTls`
