# Deployment Standard

## Environment Variables (Server)
Required:
- `Jwt__Key`
- `Jwt__Issuer`
- `Jwt__Audience`
- `Cors__AllowedOrigins__0` (and additional indexes for each allowed production origin)
- `Security__MfaEnforced=true`
- `Security__DocumentEncryptionEnabled=true`
- `Security__DocumentEncryptionKey` (base64, 32 bytes)
- `Security__DbEncryptionEnabled=true`
- `Security__DbEncryptionKey` (base64, 32 bytes)
- `Security__AuditLogImmutable=true`
- `Security__AuditLogKey` (base64, minimum 32 bytes)

Recommended:
- `Backup__EncryptBackups=true`
- `Backup__EncryptionKey` (base64, 32 bytes)

Payments:
- `Stripe__SecretKey`
- `Stripe__WebhookSecret`
- `Stripe__SuccessUrl`
- `Stripe__CancelUrl`

Operations:
- `Serilog__WriteTo__1__Args__serverUrl` (Seq URL)
- `Serilog__WriteTo__1__Args__apiKey` (Seq API key)
- `Email__Enabled`
- `Email__FromAddress`
- `Email__Smtp__Host`
- `Email__Smtp__Port`
- `Email__Smtp__Username`
- `Email__Smtp__Password`
- `Email__Smtp__UseTls`

## Environment Variables (Client)
- `VITE_STRIPE_PUBLISHABLE_KEY`
- `VITE_API_BASE_URL` (if not using proxy)
- `VITE_FORCE_CROSS_ORIGIN_API_BASE=true` only when the frontend must call a different origin directly in production

## Secrets Management
Use a secret manager (AWS Secrets Manager, Azure Key Vault, GCP Secret Manager, Vault).
Do not commit secrets to Git or place them in client-side bundles.

## Storage
Uploads are stored under `uploads/` by default. For production:
- Use external object storage (S3, GCS, Blob Storage).
- Mount persistent storage for local disk if required.

## Backups
Follow `documentation/SECURITY_AND_BACKUP.md` for backup/restore.

## Release Checklist
- Run database migrations.
- Verify webhook endpoints.
- Validate background jobs.
- Confirm logging/monitoring hooks.
- Verify encryption keys are set.
