# JurisFlow Admin Tool

`JurisFlow.AdminTool` is a console-based provisioning utility for production-safe tenant and user management without relying on the web UI.

It reads the same database configuration as `JurisFlow.Server`:

- `ConnectionStrings__DefaultConnection`
- `Database__Provider`
- `JURISFLOW_DATA_DIR` / `RAILWAY_VOLUME_MOUNT_PATH` for SQLite
- `Security__DbEncryptionEnabled` and `Security__DbEncryptionKey` when DB encryption is enabled

Use it from the repo root:

```powershell
dotnet run --project JurisFlow.AdminTool -p:UseAppHost=false -- tenant list
dotnet run --project JurisFlow.AdminTool -p:UseAppHost=false -- tenant create --name "Juris Flow" --slug juris-flow
dotnet run --project JurisFlow.AdminTool -p:UseAppHost=false -- user create-admin --tenant juris-flow --email admin@jurisflow.local --password-env ADMIN_PASSWORD
dotnet run --project JurisFlow.AdminTool -p:UseAppHost=false -- user reset-password --tenant juris-flow --email admin@jurisflow.local --password-env ADMIN_PASSWORD
dotnet run --project JurisFlow.AdminTool -p:UseAppHost=false -- user disable --tenant juris-flow --email former-admin@jurisflow.local
dotnet run --project JurisFlow.AdminTool -p:UseAppHost=false -- user delete --tenant juris-flow --email former-admin@jurisflow.local
```

Notes:

- `user create` and `user create-admin` are upserts; they create the user if missing and update the existing row if it already exists in that tenant.
- `user list` without `--tenant` lists users across tenants.
- `user disable` performs a password-based lock because the current schema does not include a dedicated disabled flag yet.
- `user delete` is a hard delete; if the user is referenced by other records, the database may reject the delete.
- Prefer `--password-env SOME_ENV_VAR` in production so secrets do not end up in shell history.

For the common production bootstrap path, use the wrapper script:

```powershell
.\scripts\provision-admin.ps1 -Tenant juris-flow -Email admin@jurisflow.local
```

You can also pass the DB settings directly:

```powershell
.\scripts\provision-admin.ps1 `
  -Tenant juris-flow `
  -Email admin@jurisflow.local `
  -ConnectionString "Host=...;Port=5432;Database=postgres;Username=...;Password=...;SSL Mode=Require" `
  -DbEncryptionEnabled false
```
