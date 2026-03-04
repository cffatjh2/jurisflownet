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
```

Notes:

- `user create` and `user create-admin` are upserts; they create the user if missing and update the existing row if it already exists in that tenant.
- `user list` without `--tenant` lists users across tenants.
- Prefer `--password-env SOME_ENV_VAR` in production so secrets do not end up in shell history.
