# Railway Deployment

This repository is configured to deploy the backend API (`JurisFlow.Server`) to Railway with the root `Dockerfile` and `railway.json`.

The current Railway setup deploys the API service only. The React client should be deployed separately (for example as a second Railway service, Vercel, or Netlify) and pointed at the API URL.

Use `railway.backend.env.example` as the backend variable checklist and `JurisFlow.Client/frontend.env.production.example` as the frontend build-time template.

## What Changed For Railway

- The API now binds to Railway's `PORT` environment variable automatically.
- Relative SQLite paths now prefer `JURISFLOW_DATA_DIR`, then `RAILWAY_VOLUME_MOUNT_PATH`, before falling back to the local `App_Data` folder.
- Production boot validation now allows `Security:IntegrationSecrets:Provider=config` if you override the default sample key with a real 32-byte base64 key.

## Railway Service Setup

1. Create a new Railway service from this GitHub repository.
2. Keep the root `Dockerfile` builder.
3. Add a Railway volume and mount it (for example at `/data`).
4. Set `JURISFLOW_DATA_DIR=/data`.
5. Set the environment variables below before the first production boot.

## Required Environment Variables

These are required because the app enforces production security checks.

```env
ASPNETCORE_ENVIRONMENT=Production
Cors__AllowedOriginsCsv=https://your-frontend-domain.com

Jwt__Key=replace-with-a-random-secret-at-least-32-characters
Jwt__Issuer=JurisFlowServer
Jwt__Audience=JurisFlowClient

Security__MfaEnforced=true
Security__DocumentEncryptionEnabled=true
Security__DbEncryptionEnabled=true
Security__AuditLogImmutable=true
Security__AuditLogFailClosed=true

Security__DocumentEncryptionKey=<base64-encoded-32-byte-key>
Security__DbEncryptionKey=<base64-encoded-32-byte-key>
Security__AuditLogKey=<base64-encoded-key-at-least-32-bytes>

Security__IntegrationSecrets__Provider=config
Security__IntegrationSecrets__LegacyPlaintextAllowed=false
Security__IntegrationSecrets__ActiveKeyId=railway-v1
Security__IntegrationSecrets__Keys__railway-v1=<base64-encoded-32-byte-key>

JURISFLOW_DATA_DIR=/data
```

## Optional But Common Variables

```env
ConnectionStrings__DefaultConnection=Data Source=jurisflow.db
Seed__Enabled=false
Seed__PortalClientEnabled=false

Stripe__SecretKey=
Stripe__WebhookSecret=
Stripe__PublishableKey=

Integrations__Google__ClientId=
Integrations__Google__ClientSecret=
Integrations__Google__RedirectUri=https://your-frontend-domain.com/auth/google/callback

Integrations__Zoom__ClientId=
Integrations__Zoom__ClientSecret=
Integrations__Zoom__AccountId=
Integrations__Zoom__RedirectUri=https://your-frontend-domain.com/auth/zoom/callback

Integrations__Gemini__ApiKey=
```

## First Deploy Checks

After deploy, verify:

1. `GET /health` returns `200`.
2. The service logs do not contain `Production security requirements are not satisfied`.
3. The database file is created inside the mounted volume, not in the container root filesystem.
4. Your frontend origin exactly matches `Cors__AllowedOriginsCsv`.

## Frontend Note

The frontend needs a separate deploy because the current Railway root service only builds the backend API.

For a separate frontend deploy:

1. Deploy `JurisFlow.Client` as its own static site (Railway static service, Vercel, or Netlify).
2. Copy `JurisFlow.Client/frontend.env.production.example` to the host's environment variable UI.
3. Set `VITE_API_BASE_URL=https://your-api-service.up.railway.app`.
4. Build the frontend with those variables so browser calls to `/api` and `/uploads` are rewritten to the backend service.
5. Keep `Cors__AllowedOriginsCsv` aligned with the frontend domain and keep OAuth redirect URIs on the backend integration settings aligned with that same domain.
