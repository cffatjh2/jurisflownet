using JurisFlow.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace JurisFlow.Server.Services
{
    internal static class PostgresSchemaCompatibility
    {
        private static readonly string[] Commands =
        {
            """
            CREATE TABLE IF NOT EXISTS "AuthSessions" (
                "Id" text PRIMARY KEY,
                "UserId" text NULL,
                "ClientId" text NULL,
                "TenantId" text NULL,
                "SubjectType" text NOT NULL DEFAULT 'User',
                "CreatedAt" timestamp with time zone NOT NULL,
                "LastSeenAt" timestamp with time zone NOT NULL,
                "ExpiresAt" timestamp with time zone NOT NULL,
                "IpAddress" text NULL,
                "UserAgent" text NULL,
                "RevokedAt" timestamp with time zone NULL,
                "RevokedReason" text NULL,
                "RefreshTokenHash" text NULL,
                "RefreshTokenIssuedAt" timestamp with time zone NULL,
                "RefreshTokenExpiresAt" timestamp with time zone NULL,
                "RefreshTokenRotatedAt" timestamp with time zone NULL
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS "AuditLogs" (
                "Id" text PRIMARY KEY,
                "UserId" text NULL,
                "ClientId" text NULL,
                "TenantId" text NULL,
                "Role" text NULL,
                "Action" character varying(128) NOT NULL,
                "Entity" character varying(128) NULL,
                "EntityId" character varying(128) NULL,
                "Details" text NULL,
                "IpAddress" text NULL,
                "UserAgent" text NULL,
                "Sequence" bigint NOT NULL DEFAULT 0,
                "PreviousHash" character varying(128) NULL,
                "Hash" character varying(128) NULL,
                "HashAlgorithm" character varying(32) NULL,
                "CreatedAt" timestamp with time zone NOT NULL
            );
            """,
            """ALTER TABLE IF EXISTS "Clients" ADD COLUMN IF NOT EXISTS "LastLogin" timestamp with time zone NULL;""",
            """ALTER TABLE IF EXISTS "Clients" ADD COLUMN IF NOT EXISTS "NormalizedEmail" character varying(320) NULL;""",
            """ALTER TABLE IF EXISTS "Clients" ADD COLUMN IF NOT EXISTS "PortalEnabled" boolean NOT NULL DEFAULT FALSE;""",
            """ALTER TABLE IF EXISTS "Clients" ADD COLUMN IF NOT EXISTS "PasswordHash" text NULL;""",
            """ALTER TABLE IF EXISTS "Clients" ADD COLUMN IF NOT EXISTS "Status" text NOT NULL DEFAULT 'Active';""",
            """ALTER TABLE IF EXISTS "Clients" ADD COLUMN IF NOT EXISTS "Company" text NULL;""",
            """UPDATE "Clients" SET "NormalizedEmail" = lower(trim(coalesce("Email", ''))) WHERE "NormalizedEmail" IS NULL OR btrim("NormalizedEmail") = '';""",
            """UPDATE "Clients" SET "Status" = 'Active' WHERE "Status" IS NULL OR btrim("Status") = '';""",
            """
            UPDATE "Clients" AS c
            SET "Company" = t."Name"
            FROM "Tenants" AS t
            WHERE c."TenantId" = t."Id"
              AND c."Company" IS DISTINCT FROM t."Name";
            """,
            """ALTER TABLE IF EXISTS "Matters" ADD COLUMN IF NOT EXISTS "CreatedByUserId" text NULL;""",
            """ALTER TABLE IF EXISTS "Matters" ADD COLUMN IF NOT EXISTS "ShareWithFirm" boolean NOT NULL DEFAULT FALSE;""",
            """ALTER TABLE IF EXISTS "Matters" ADD COLUMN IF NOT EXISTS "ShareBillingWithFirm" boolean NOT NULL DEFAULT FALSE;""",
            """ALTER TABLE IF EXISTS "Matters" ADD COLUMN IF NOT EXISTS "ShareNotesWithFirm" boolean NOT NULL DEFAULT FALSE;""",
            """
            CREATE TABLE IF NOT EXISTS "MatterClientLinks" (
                "Id" text PRIMARY KEY,
                "MatterId" text NOT NULL,
                "ClientId" text NOT NULL,
                "TenantId" text NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NOT NULL
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS "MatterNotes" (
                "Id" text PRIMARY KEY,
                "MatterId" text NOT NULL,
                "Title" text NULL,
                "Body" text NOT NULL,
                "CreatedByUserId" text NULL,
                "UpdatedByUserId" text NULL,
                "TenantId" text NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NOT NULL
            );
            """,
            """ALTER TABLE IF EXISTS "AuthSessions" ADD COLUMN IF NOT EXISTS "ClientId" text NULL;""",
            """ALTER TABLE IF EXISTS "AuthSessions" ADD COLUMN IF NOT EXISTS "TenantId" text NULL;""",
            """ALTER TABLE IF EXISTS "AuthSessions" ADD COLUMN IF NOT EXISTS "SubjectType" text NOT NULL DEFAULT 'User';""",
            """ALTER TABLE IF EXISTS "AuthSessions" ADD COLUMN IF NOT EXISTS "RefreshTokenHash" text NULL;""",
            """ALTER TABLE IF EXISTS "AuthSessions" ADD COLUMN IF NOT EXISTS "RefreshTokenIssuedAt" timestamp with time zone NULL;""",
            """ALTER TABLE IF EXISTS "AuthSessions" ADD COLUMN IF NOT EXISTS "RefreshTokenExpiresAt" timestamp with time zone NULL;""",
            """ALTER TABLE IF EXISTS "AuthSessions" ADD COLUMN IF NOT EXISTS "RefreshTokenRotatedAt" timestamp with time zone NULL;""",
            """ALTER TABLE IF EXISTS "AuthSessions" ADD COLUMN IF NOT EXISTS "RevokedReason" text NULL;""",
            """ALTER TABLE IF EXISTS "AuditLogs" ADD COLUMN IF NOT EXISTS "Sequence" bigint NOT NULL DEFAULT 0;""",
            """ALTER TABLE IF EXISTS "AuditLogs" ADD COLUMN IF NOT EXISTS "PreviousHash" character varying(128) NULL;""",
            """ALTER TABLE IF EXISTS "AuditLogs" ADD COLUMN IF NOT EXISTS "Hash" character varying(128) NULL;""",
            """ALTER TABLE IF EXISTS "AuditLogs" ADD COLUMN IF NOT EXISTS "HashAlgorithm" character varying(32) NULL;""",
            """CREATE INDEX IF NOT EXISTS "IX_Matters_TenantId_OpenDate" ON "Matters" ("TenantId", "OpenDate" DESC);""",
            """CREATE INDEX IF NOT EXISTS "IX_Tasks_TenantId_CreatedAt" ON "Tasks" ("TenantId", "CreatedAt" DESC);""",
            """CREATE INDEX IF NOT EXISTS "IX_TimeEntries_TenantId_Date" ON "TimeEntries" ("TenantId", "Date" DESC);""",
            """CREATE INDEX IF NOT EXISTS "IX_CalendarEvents_TenantId_Date" ON "CalendarEvents" ("TenantId", "Date" DESC);""",
            """CREATE INDEX IF NOT EXISTS "IX_Expenses_TenantId_Date" ON "Expenses" ("TenantId", "Date" DESC);""",
            """CREATE INDEX IF NOT EXISTS "IX_Leads_TenantId_CreatedAt" ON "Leads" ("TenantId", "CreatedAt" DESC);""",
            """CREATE INDEX IF NOT EXISTS "IX_Invoices_TenantId_CreatedAt" ON "Invoices" ("TenantId", "CreatedAt" DESC);""",
            """CREATE INDEX IF NOT EXISTS "IX_Documents_TenantId_CreatedAt" ON "Documents" ("TenantId", "CreatedAt" DESC);""",
            """CREATE INDEX IF NOT EXISTS "IX_Clients_TenantId_CreatedAt" ON "Clients" ("TenantId", "CreatedAt" DESC);""",
            """CREATE INDEX IF NOT EXISTS "IX_Clients_TenantId_NormalizedEmail_lookup" ON "Clients" ("TenantId", "NormalizedEmail");""",
            """CREATE UNIQUE INDEX IF NOT EXISTS "IX_MatterClientLinks_TenantId_MatterId_ClientId" ON "MatterClientLinks" ("TenantId", "MatterId", "ClientId");""",
            """CREATE INDEX IF NOT EXISTS "IX_MatterClientLinks_TenantId_ClientId_CreatedAt" ON "MatterClientLinks" ("TenantId", "ClientId", "CreatedAt" DESC);""",
            """CREATE INDEX IF NOT EXISTS "IX_MatterNotes_TenantId_MatterId_CreatedAt" ON "MatterNotes" ("TenantId", "MatterId", "CreatedAt" DESC);""",
            """CREATE INDEX IF NOT EXISTS "IX_MatterNotes_TenantId_MatterId_UpdatedAt" ON "MatterNotes" ("TenantId", "MatterId", "UpdatedAt" DESC);""",
            """CREATE INDEX IF NOT EXISTS "IX_Matters_TenantId_CreatedByUserId" ON "Matters" ("TenantId", "CreatedByUserId");""",
            """CREATE INDEX IF NOT EXISTS "IX_Matters_TenantId_ShareWithFirm" ON "Matters" ("TenantId", "ShareWithFirm");""",
            """CREATE INDEX IF NOT EXISTS "IX_Invoices_TenantId_MatterId" ON "Invoices" ("TenantId", "MatterId");""",
            """CREATE INDEX IF NOT EXISTS "IX_Documents_TenantId_MatterId" ON "Documents" ("TenantId", "MatterId");""",
            """CREATE INDEX IF NOT EXISTS "IX_TimeEntries_TenantId_MatterId" ON "TimeEntries" ("TenantId", "MatterId");""",
            """CREATE INDEX IF NOT EXISTS "IX_Expenses_TenantId_MatterId" ON "Expenses" ("TenantId", "MatterId");"""
        };

        public static async Task EnsureCriticalColumnsAsync(JurisFlowDbContext context, ILogger logger, CancellationToken cancellationToken = default)
        {
            if (!context.Database.IsNpgsql())
            {
                return;
            }

            logger.LogInformation("Applying PostgreSQL schema compatibility checks for legacy deployments.");

            foreach (var command in Commands)
            {
                await context.Database.ExecuteSqlRawAsync(command, cancellationToken);
            }
        }
    }
}
