# Supabase Database Setup

This project now supports running the backend against Supabase Postgres in addition to local SQLite.

## What Changed

- `Database:Provider` now supports `sqlite` and `postgres`.
- PostgreSQL connection strings can be supplied in standard key/value form or as a `postgresql://...` URI.
- PostgreSQL startup defaults to `Database:BootstrapMode=ensure-created` so a fresh Supabase database can be initialized from the current EF Core model without replaying the existing SQLite migration chain.

## Required Backend Variables

Use `supabase.backend.env.example` as the baseline. The database-specific values are:

```env
Database__Provider=postgres
Database__BootstrapMode=ensure-created
ConnectionStrings__DefaultConnection=Host=db.your-project-ref.supabase.co;Port=5432;Database=postgres;Username=postgres;Password=replace-me;SSL Mode=Require
```

You can also use the URI form Supabase shows in the dashboard:

```env
ConnectionStrings__DefaultConnection=postgresql://postgres:replace-me@db.your-project-ref.supabase.co:5432/postgres?sslmode=require
```

The app normalizes the URI into an Npgsql connection string automatically.

## Important Limitation

PostgreSQL is currently bootstrapped with `EnsureCreated()` by default. That is intentional for now because the checked-in EF migrations were generated for SQLite.

That means:

- use a **fresh** Supabase database when bootstrapping from this branch
- do not point this at an existing production PostgreSQL schema that needs incremental EF migrations
- if you want full PostgreSQL migration history later, create a PostgreSQL migration baseline before switching `Database__BootstrapMode` to `migrate`

## Storage Note

This change prepares the relational database layer only. Document binaries are still stored on the app filesystem today, so production-grade file storage should still be moved to Supabase Storage or another object store in a follow-up change.

## Supabase Console Steps

1. Create a new Supabase project.
2. In Supabase, open `Project Settings -> Database`.
3. Copy the direct connection string or URI.
4. Put that value into `ConnectionStrings__DefaultConnection`.
5. Add the remaining backend security variables from `supabase.backend.env.example`.
6. Deploy/redeploy the backend.

## Current References

- Supabase database overview: https://supabase.com/docs/guides/database/overview
- Supabase connecting to Postgres: https://supabase.com/docs/guides/database/connecting-to-postgres
- Supabase connection strings: https://supabase.com/docs/guides/database/connecting-to-postgres#connection-strings
