# Supabase Database and Storage Setup

This project now supports running the backend against Supabase Postgres and Supabase Storage in addition to local SQLite + local disk.

## What Changed

- `Database:Provider` now supports `sqlite` and `postgres`.
- PostgreSQL connection strings can be supplied in standard key/value form or as a `postgresql://...` URI.
- PostgreSQL startup defaults to `Database:BootstrapMode=ensure-created` so a fresh Supabase database can be initialized from the current EF Core model without replaying the existing SQLite migration chain.
- `Storage:Provider` now supports `local` and `supabase`.
- When `Storage:Provider=supabase`, document files, client portal uploads, message attachments, avatars, and imported integration artifacts are stored in a private Supabase Storage bucket instead of the app filesystem.

## Required Backend Variables

Use `supabase.backend.env.example` as the baseline. The database-specific values are:

```env
Database__Provider=postgres
Database__BootstrapMode=ensure-created
ConnectionStrings__DefaultConnection=Host=aws-0-us-east-1.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.your-project-ref;Password=replace-me;SSL Mode=Require
Storage__Provider=supabase
Storage__Supabase__Url=https://your-project-ref.supabase.co
Storage__Supabase__ServiceRoleKey=replace-with-your-supabase-service-role-key
Storage__Supabase__Bucket=jurisflow-files
```

You can also use the URI form Supabase shows in the dashboard:

```env
ConnectionStrings__DefaultConnection=postgresql://postgres:replace-me@db.your-project-ref.supabase.co:5432/postgres?sslmode=require
```

The app normalizes the URI into an Npgsql connection string automatically.

For Railway and similar IPv4-only platforms, prefer the Supabase `Session Pooler` connection instead of `Direct connection`.

## Migration Baseline Status

This repository now includes a dedicated `supabase/` workspace for PostgreSQL migration management.

Current operational stance:

- `Database__BootstrapMode=ensure-created` is still acceptable only for fresh throwaway bootstraps
- staging and production Supabase projects should move to `Database__BootstrapMode=migrate` after the remote baseline is captured
- the PostgreSQL schema baseline must come from `supabase db pull`, not from replaying the existing SQLite-oriented EF migration chain

For the step-by-step baseline and history alignment procedure, use:

- [SUPABASE_PHASE2_BASELINE_RUNBOOK_TR.md](</C:/Users/cffat/OneDrive/Masaüstü/jurisflownet/documentation/SUPABASE_PHASE2_BASELINE_RUNBOOK_TR.md:1>)

## Storage Notes

- Use a private bucket and provide the backend with the Supabase `service_role` key via `Storage__Supabase__ServiceRoleKey`.
- The backend auto-creates the configured bucket if it does not exist yet.
- Existing `FilePath` values continue to use logical app-relative keys such as `uploads/{tenantId}/...`; the storage provider now resolves those keys to local disk or Supabase Storage.
- Backup ZIP files are still generated locally by the application when you trigger backups. The runtime file store for documents and attachments is what moved to Supabase Storage.

## Supabase Console Steps

1. Create a new Supabase project.
2. In Supabase, open `Project Settings -> Database`.
3. Open the connection dialog and switch the database connection method to `Session Pooler`.
4. Copy the pooler host/port/username/password into `ConnectionStrings__DefaultConnection`.
5. Open `Project Settings -> API` and copy the `service_role` key.
6. Put that key into `Storage__Supabase__ServiceRoleKey`.
7. Add the remaining backend security variables from `supabase.backend.env.example`.
8. Deploy/redeploy the backend.

## Current References

- Supabase database overview: https://supabase.com/docs/guides/database/overview
- Supabase connecting to Postgres: https://supabase.com/docs/guides/database/connecting-to-postgres
- Supabase connection strings: https://supabase.com/docs/guides/database/connecting-to-postgres#connection-strings
- Supabase Storage overview: https://supabase.com/docs/guides/storage
- Supabase API keys: https://supabase.com/docs/guides/api/api-keys
