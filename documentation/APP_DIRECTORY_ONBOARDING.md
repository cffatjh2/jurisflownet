# JurisFlow App Directory And Partner Onboarding

## Overview

The App Directory workflow supports:
- Manifest-based partner submissions
- Built-in test harness validation
- Staff/Admin review pipeline
- SLA profile tracking per listing

## API Surface

- `GET /api/app-directory/listings`
- `GET /api/app-directory/listings/{id}`
- `GET /api/app-directory/review-queue` (staff)
- `POST /api/app-directory/onboarding/submit` (staff; partner onboarding)
- `POST /api/app-directory/listings/{id}/retest` (staff)
- `POST /api/app-directory/listings/{id}/review` (admin)
- `GET /api/app-directory/listings/{id}/submissions` (staff)

## Manifest Payload

`POST /api/app-directory/onboarding/submit`

```json
{
  "manifest": {
    "providerKey": "acme-case-sync",
    "name": "Acme Case Sync",
    "category": "Court Docket",
    "connectionMode": "oauth",
    "summary": "Bi-directional sync",
    "description": "Syncs dockets and filing statuses",
    "manifestVersion": "1.0",
    "websiteUrl": "https://partner.example",
    "documentationUrl": "https://partner.example/docs",
    "supportEmail": "support@partner.example",
    "supportUrl": "https://partner.example/support",
    "logoUrl": "https://partner.example/logo.png",
    "supportsWebhook": true,
    "webhookFirst": true,
    "fallbackPollingMinutes": 360,
    "capabilities": [
      "docket_sync",
      "filing_status"
    ]
  },
  "sla": {
    "tier": "gold",
    "responseHours": 4,
    "resolutionHours": 24,
    "uptimePercent": 99.9
  }
}
```

## Test Harness Rules

The harness checks:
- Provider key format (`lowercase-kebab-case`)
- Required fields (`name`, `summary`, `category`, `manifestVersion`)
- Connection mode (`oauth`, `api_key`, `hybrid`)
- Webhook-first consistency:
  - `webhookFirst=true` requires `supportsWebhook=true`
  - `fallbackPollingMinutes` in `[60, 1440]`
- URL and support email sanity
- SLA sanity:
  - `resolutionHours >= responseHours`
  - uptime range `[95, 100]`

Listings failing harness errors are marked `changes_requested`.
Listings passing harness move to `in_review`.

## Review Decisions

`POST /api/app-directory/listings/{id}/review` accepts:
- `approve`
- `reject`
- `request_changes`
- `suspend`

If `decision=approve` and `publish=true`, listing status becomes `published`.

## Status Lifecycle

- `draft`
- `changes_requested`
- `in_review`
- `approved`
- `published`
- `rejected`
- `suspended`

## Notes

- Listings and submissions are tenant-scoped.
- Review actions are audit-logged.
- SLA values are stored on listing and can be adjusted during review.
