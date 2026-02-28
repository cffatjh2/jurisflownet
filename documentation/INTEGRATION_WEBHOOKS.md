# Integration Webhooks (Webhook-First, Polling Fallback)

## Endpoint

- `POST /api/integrations/webhooks/{providerKey}`
- `GET /api/integrations/webhooks/{providerKey}` (Outlook validation challenge only)

`providerKey` examples:
- `quickbooks-online`
- `xero`
- `google-gmail`
- `microsoft-outlook-mail`

## Tenant Routing Requirement

Webhook requests are tenant-scoped. Include tenant in callback URL:

- Query: `?tenant={tenantSlug}`
- or header: `X-Tenant-Slug: {tenantSlug}`

Without tenant context, request is rejected before controller execution.

## Security Validation

Provider-specific validation:

- QuickBooks: `intuit-signature` with `Integrations:QuickBooks:WebhookVerifierToken`
- Xero: `x-xero-signature` with `Integrations:Xero:WebhookSigningKey`
- Outlook Mail: `clientState` check with `Integrations:Outlook:WebhookClientState`
- Gmail: `X-Goog-Channel-Token` with `Integrations:Google:GmailWebhookChannelToken`

Generic fallback:

- `x-integration-webhook-secret` with `Integrations:Webhooks:{providerKey}:SharedSecret`

Unsigned webhook acceptance is controlled by:

- `Integrations:Webhooks:AllowUnsigned`

## Runtime Behavior

- Webhook request triggers immediate `IntegrationSyncRunner` execution with `Trigger=webhook`.
- Idempotency key is derived from provider + event digest.
- If same event previously failed/dead-lettered, a new key variant is generated to allow reprocessing.

## Polling Fallback

Scheduler remains active as fallback:

- Non-webhook providers: normal polling interval (`Operations:IntegrationSyncIntervalMinutes`)
- Webhook-first providers: provider-specific fallback interval from catalog (`FallbackPollingMinutes`)

Config:

- `Operations:IntegrationSyncIntervalMinutes`
- `Operations:IntegrationSyncBatchSize`
- `Operations:IntegrationSyncCandidateBatchSize`

## Rate Limiting

Webhook route policy:

- `RateLimiting:IntegrationWebhook:PermitLimit`
- `RateLimiting:IntegrationWebhook:WindowSeconds`
