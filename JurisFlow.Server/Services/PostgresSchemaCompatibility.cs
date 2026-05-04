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
            """ALTER TABLE IF EXISTS "MatterClientLinks" ADD COLUMN IF NOT EXISTS "TenantId" text NULL;""",
            """ALTER TABLE IF EXISTS "MatterClientLinks" ADD COLUMN IF NOT EXISTS "CreatedAt" timestamp with time zone NOT NULL DEFAULT now();""",
            """ALTER TABLE IF EXISTS "MatterClientLinks" ADD COLUMN IF NOT EXISTS "UpdatedAt" timestamp with time zone NOT NULL DEFAULT now();""",
            """ALTER TABLE IF EXISTS "MatterNotes" ADD COLUMN IF NOT EXISTS "TenantId" text NULL;""",
            """ALTER TABLE IF EXISTS "MatterNotes" ADD COLUMN IF NOT EXISTS "CreatedAt" timestamp with time zone NOT NULL DEFAULT now();""",
            """ALTER TABLE IF EXISTS "MatterNotes" ADD COLUMN IF NOT EXISTS "UpdatedAt" timestamp with time zone NOT NULL DEFAULT now();""",
            """
            CREATE TABLE IF NOT EXISTS "BillingSettings" (
                "Id" text PRIMARY KEY,
                "DefaultHourlyRate" double precision NOT NULL DEFAULT 350,
                "PartnerRate" double precision NOT NULL DEFAULT 500,
                "AssociateRate" double precision NOT NULL DEFAULT 300,
                "ParalegalRate" double precision NOT NULL DEFAULT 150,
                "BillingIncrement" integer NOT NULL DEFAULT 6,
                "MinimumTimeEntry" integer NOT NULL DEFAULT 6,
                "RoundingRule" text NOT NULL DEFAULT 'up',
                "DefaultPaymentTerms" integer NOT NULL DEFAULT 30,
                "InvoicePrefix" text NOT NULL DEFAULT 'INV-',
                "DefaultTaxRate" double precision NOT NULL DEFAULT 0,
                "LedesEnabled" boolean NOT NULL DEFAULT FALSE,
                "UtbmsCodesRequired" boolean NOT NULL DEFAULT FALSE,
                "EvergreenRetainerMinimum" double precision NOT NULL DEFAULT 5000,
                "TrustBalanceAlerts" boolean NOT NULL DEFAULT TRUE,
                "UpdatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                "TenantId" text NULL
            );
            """,
            """ALTER TABLE IF EXISTS "BillingSettings" ADD COLUMN IF NOT EXISTS "TenantId" text NULL;""",
            """
            CREATE TABLE IF NOT EXISTS "FirmSettings" (
                "Id" text PRIMARY KEY,
                "FirmName" text NOT NULL DEFAULT 'Your Law Firm',
                "TaxId" text NULL,
                "LedesFirmId" text NULL,
                "Address" text NULL,
                "City" text NULL,
                "State" text NULL,
                "ZipCode" text NULL,
                "Phone" text NULL,
                "Website" text NULL,
                "LogoDataUrl" text NULL,
                "IntegrationsJson" text NULL,
                "UpdatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                "TenantId" text NULL
            );
            """,
            """ALTER TABLE IF EXISTS "FirmSettings" ADD COLUMN IF NOT EXISTS "TenantId" text NULL;""",
            """ALTER TABLE IF EXISTS "FirmSettings" ADD COLUMN IF NOT EXISTS "LogoDataUrl" text NULL;""",
            """
            CREATE TABLE IF NOT EXISTS "Invoices" (
                "Id" text PRIMARY KEY,
                "Number" character varying(50) NULL,
                "ClientId" text NULL,
                "MatterId" text NULL,
                "EntityId" text NULL,
                "OfficeId" text NULL,
                "Status" integer NOT NULL DEFAULT 0,
                "IssueDate" timestamp with time zone NOT NULL DEFAULT now(),
                "DueDate" timestamp with time zone NULL,
                "Subtotal" numeric NOT NULL DEFAULT 0,
                "Tax" numeric NOT NULL DEFAULT 0,
                "Discount" numeric NOT NULL DEFAULT 0,
                "Total" numeric NOT NULL DEFAULT 0,
                "AmountPaid" numeric NOT NULL DEFAULT 0,
                "Balance" numeric NOT NULL DEFAULT 0,
                "Notes" text NULL,
                "Terms" text NULL,
                "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                "UpdatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                "TenantId" text NULL
            );
            """,
            """ALTER TABLE IF EXISTS "Invoices" ADD COLUMN IF NOT EXISTS "TenantId" text NULL;""",
            """
            CREATE TABLE IF NOT EXISTS "InvoiceLineItems" (
                "Id" text PRIMARY KEY,
                "InvoiceId" text NOT NULL,
                "Type" character varying(20) NOT NULL DEFAULT 'time',
                "TaskCode" character varying(20) NULL,
                "ExpenseCode" character varying(20) NULL,
                "ActivityCode" character varying(20) NULL,
                "Description" character varying(255) NOT NULL DEFAULT '',
                "ServiceDate" timestamp with time zone NULL,
                "Quantity" numeric NOT NULL DEFAULT 1,
                "Rate" numeric NOT NULL DEFAULT 0,
                "Amount" numeric NOT NULL DEFAULT 0,
                "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                "UpdatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                "TenantId" text NULL
            );
            """,
            """ALTER TABLE IF EXISTS "InvoiceLineItems" ADD COLUMN IF NOT EXISTS "TenantId" text NULL;""",
            """ALTER TABLE IF EXISTS "Invoices" ADD COLUMN IF NOT EXISTS "Number" character varying(50) NULL;""",
            """ALTER TABLE IF EXISTS "Invoices" ADD COLUMN IF NOT EXISTS "ClientId" text NULL;""",
            """ALTER TABLE IF EXISTS "Invoices" ADD COLUMN IF NOT EXISTS "MatterId" text NULL;""",
            """ALTER TABLE IF EXISTS "Invoices" ADD COLUMN IF NOT EXISTS "EntityId" text NULL;""",
            """ALTER TABLE IF EXISTS "Invoices" ADD COLUMN IF NOT EXISTS "OfficeId" text NULL;""",
            """ALTER TABLE IF EXISTS "Invoices" ADD COLUMN IF NOT EXISTS "Status" integer NOT NULL DEFAULT 0;""",
            """ALTER TABLE IF EXISTS "Invoices" ADD COLUMN IF NOT EXISTS "IssueDate" timestamp with time zone NOT NULL DEFAULT now();""",
            """ALTER TABLE IF EXISTS "Invoices" ADD COLUMN IF NOT EXISTS "DueDate" timestamp with time zone NULL;""",
            """ALTER TABLE IF EXISTS "Invoices" ADD COLUMN IF NOT EXISTS "Subtotal" numeric NOT NULL DEFAULT 0;""",
            """ALTER TABLE IF EXISTS "Invoices" ADD COLUMN IF NOT EXISTS "Tax" numeric NOT NULL DEFAULT 0;""",
            """ALTER TABLE IF EXISTS "Invoices" ADD COLUMN IF NOT EXISTS "Discount" numeric NOT NULL DEFAULT 0;""",
            """ALTER TABLE IF EXISTS "Invoices" ADD COLUMN IF NOT EXISTS "Total" numeric NOT NULL DEFAULT 0;""",
            """ALTER TABLE IF EXISTS "Invoices" ADD COLUMN IF NOT EXISTS "AmountPaid" numeric NOT NULL DEFAULT 0;""",
            """ALTER TABLE IF EXISTS "Invoices" ADD COLUMN IF NOT EXISTS "Balance" numeric NOT NULL DEFAULT 0;""",
            """ALTER TABLE IF EXISTS "Invoices" ADD COLUMN IF NOT EXISTS "Notes" text NULL;""",
            """ALTER TABLE IF EXISTS "Invoices" ADD COLUMN IF NOT EXISTS "Terms" text NULL;""",
            """ALTER TABLE IF EXISTS "Invoices" ADD COLUMN IF NOT EXISTS "CreatedAt" timestamp with time zone NOT NULL DEFAULT now();""",
            """ALTER TABLE IF EXISTS "Invoices" ADD COLUMN IF NOT EXISTS "UpdatedAt" timestamp with time zone NOT NULL DEFAULT now();""",
            """ALTER TABLE IF EXISTS "InvoiceLineItems" ADD COLUMN IF NOT EXISTS "InvoiceId" text NULL;""",
            """ALTER TABLE IF EXISTS "InvoiceLineItems" ADD COLUMN IF NOT EXISTS "Type" character varying(20) NOT NULL DEFAULT 'time';""",
            """ALTER TABLE IF EXISTS "InvoiceLineItems" ADD COLUMN IF NOT EXISTS "TaskCode" character varying(20) NULL;""",
            """ALTER TABLE IF EXISTS "InvoiceLineItems" ADD COLUMN IF NOT EXISTS "ExpenseCode" character varying(20) NULL;""",
            """ALTER TABLE IF EXISTS "InvoiceLineItems" ADD COLUMN IF NOT EXISTS "ActivityCode" character varying(20) NULL;""",
            """ALTER TABLE IF EXISTS "InvoiceLineItems" ADD COLUMN IF NOT EXISTS "Description" character varying(255) NOT NULL DEFAULT '';""",
            """ALTER TABLE IF EXISTS "InvoiceLineItems" ADD COLUMN IF NOT EXISTS "ServiceDate" timestamp with time zone NULL;""",
            """ALTER TABLE IF EXISTS "InvoiceLineItems" ADD COLUMN IF NOT EXISTS "Quantity" numeric NOT NULL DEFAULT 1;""",
            """ALTER TABLE IF EXISTS "InvoiceLineItems" ADD COLUMN IF NOT EXISTS "Rate" numeric NOT NULL DEFAULT 0;""",
            """ALTER TABLE IF EXISTS "InvoiceLineItems" ADD COLUMN IF NOT EXISTS "Amount" numeric NOT NULL DEFAULT 0;""",
            """ALTER TABLE IF EXISTS "InvoiceLineItems" ADD COLUMN IF NOT EXISTS "CreatedAt" timestamp with time zone NOT NULL DEFAULT now();""",
            """ALTER TABLE IF EXISTS "InvoiceLineItems" ADD COLUMN IF NOT EXISTS "UpdatedAt" timestamp with time zone NOT NULL DEFAULT now();""",
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
            """
            CREATE TABLE IF NOT EXISTS "TrustJournalEntries" (
                "Id" text PRIMARY KEY,
                "TrustTransactionId" character varying(128) NOT NULL,
                "PostingBatchId" character varying(128) NOT NULL DEFAULT '',
                "TrustAccountId" character varying(128) NOT NULL,
                "ClientTrustLedgerId" character varying(128) NULL,
                "MatterId" character varying(128) NULL,
                "EntryKind" character varying(24) NOT NULL DEFAULT 'posting',
                "OperationType" character varying(32) NOT NULL DEFAULT 'deposit',
                "Amount" numeric NOT NULL DEFAULT 0,
                "Currency" character varying(3) NOT NULL DEFAULT 'USD',
                "AvailabilityClass" character varying(24) NOT NULL DEFAULT 'cleared',
                "ReversalOfTrustJournalEntryId" character varying(128) NULL,
                "CorrelationKey" character varying(256) NULL,
                "Description" text NULL,
                "MetadataJson" text NULL,
                "CreatedBy" character varying(128) NULL,
                "EffectiveAt" timestamp with time zone NOT NULL DEFAULT now(),
                "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                "TenantId" text NULL
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS "TrustPostingBatches" (
                "Id" text PRIMARY KEY,
                "TrustTransactionId" character varying(128) NOT NULL,
                "TrustAccountId" character varying(128) NOT NULL,
                "BatchType" character varying(24) NOT NULL DEFAULT 'posting',
                "ParentPostingBatchId" character varying(128) NULL,
                "CreatedBy" character varying(128) NULL,
                "JournalEntryCount" integer NOT NULL DEFAULT 0,
                "TotalAmount" numeric NOT NULL DEFAULT 0,
                "EffectiveAt" timestamp with time zone NOT NULL DEFAULT now(),
                "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                "TenantId" text NULL
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS "TrustStatementImports" (
                "Id" text PRIMARY KEY,
                "TrustAccountId" character varying(128) NOT NULL,
                "PeriodStart" timestamp with time zone NOT NULL,
                "PeriodEnd" timestamp with time zone NOT NULL,
                "StatementEndingBalance" numeric(18,2) NOT NULL DEFAULT 0,
                "Status" character varying(24) NOT NULL DEFAULT 'imported',
                "Source" character varying(64) NOT NULL DEFAULT 'manual',
                "Currency" character varying(3) NOT NULL DEFAULT 'USD',
                "ImportedBy" character varying(128) NULL,
                "LineCount" integer NOT NULL DEFAULT 0,
                "Notes" text NULL,
                "MetadataJson" text NULL,
                "ImportedAt" timestamp with time zone NOT NULL DEFAULT now(),
                "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                "UpdatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                "TenantId" text NULL
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS "TrustStatementLines" (
                "Id" text PRIMARY KEY,
                "TrustStatementImportId" character varying(128) NOT NULL,
                "TrustAccountId" character varying(128) NOT NULL,
                "PostedAt" timestamp with time zone NOT NULL,
                "EffectiveAt" timestamp with time zone NULL,
                "Amount" numeric(18,2) NOT NULL DEFAULT 0,
                "BalanceAfter" numeric(18,2) NULL,
                "Reference" character varying(128) NULL,
                "CheckNumber" character varying(128) NULL,
                "Description" text NULL,
                "Counterparty" character varying(256) NULL,
                "MatchStatus" character varying(24) NOT NULL DEFAULT 'unmatched',
                "MatchedTrustTransactionId" character varying(128) NULL,
                "ExternalLineId" character varying(128) NULL,
                "MetadataJson" text NULL,
                "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                "TenantId" text NULL
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS "TrustOutstandingItems" (
                "Id" text PRIMARY KEY,
                "TrustAccountId" character varying(128) NOT NULL,
                "TrustTransactionId" character varying(128) NULL,
                "ClientTrustLedgerId" character varying(128) NULL,
                "TrustStatementImportId" character varying(128) NULL,
                "TrustReconciliationPacketId" character varying(128) NULL,
                "PeriodStart" timestamp with time zone NOT NULL,
                "PeriodEnd" timestamp with time zone NOT NULL,
                "OccurredAt" timestamp with time zone NOT NULL,
                "ItemType" character varying(48) NOT NULL DEFAULT 'other_adjustment',
                "ImpactDirection" character varying(24) NOT NULL DEFAULT 'decrease_bank',
                "Status" character varying(24) NOT NULL DEFAULT 'open',
                "Source" character varying(24) NOT NULL DEFAULT 'manual',
                "Amount" numeric(18,2) NOT NULL DEFAULT 0,
                "CorrelationKey" character varying(256) NULL,
                "Reference" character varying(128) NULL,
                "Description" text NULL,
                "CreatedBy" character varying(128) NULL,
                "ResolvedBy" character varying(128) NULL,
                "ResolvedAt" timestamp with time zone NULL,
                "MetadataJson" text NULL,
                "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                "UpdatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                "TenantId" text NULL
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS "TrustReconciliationPackets" (
                "Id" text PRIMARY KEY,
                "TrustAccountId" character varying(128) NOT NULL,
                "StatementImportId" character varying(128) NULL,
                "PeriodStart" timestamp with time zone NOT NULL,
                "PeriodEnd" timestamp with time zone NOT NULL,
                "StatementEndingBalance" numeric(18,2) NOT NULL DEFAULT 0,
                "AdjustedBankBalance" numeric(18,2) NOT NULL DEFAULT 0,
                "JournalBalance" numeric(18,2) NOT NULL DEFAULT 0,
                "ClientLedgerBalance" numeric(18,2) NOT NULL DEFAULT 0,
                "OutstandingDepositsTotal" numeric(18,2) NOT NULL DEFAULT 0,
                "OutstandingChecksTotal" numeric(18,2) NOT NULL DEFAULT 0,
                "OtherAdjustmentsTotal" numeric(18,2) NOT NULL DEFAULT 0,
                "ExceptionCount" integer NOT NULL DEFAULT 0,
                "Status" character varying(24) NOT NULL DEFAULT 'prepared',
                "PreparedBy" character varying(128) NULL,
                "PreparedAt" timestamp with time zone NOT NULL DEFAULT now(),
                "Notes" text NULL,
                "PayloadJson" text NULL,
                "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                "UpdatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                "TenantId" text NULL
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS "TrustReconciliationSignoffs" (
                "Id" text PRIMARY KEY,
                "TrustReconciliationPacketId" character varying(128) NOT NULL,
                "SignedBy" character varying(128) NOT NULL,
                "SignerRole" character varying(64) NULL,
                "Status" character varying(24) NOT NULL DEFAULT 'signed_off',
                "Notes" text NULL,
                "SignedAt" timestamp with time zone NOT NULL DEFAULT now(),
                "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                "TenantId" text NULL
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS "TrustCommandDeduplications" (
                "Id" text PRIMARY KEY,
                "CommandName" character varying(64) NOT NULL,
                "ActorUserId" character varying(128) NOT NULL,
                "IdempotencyKey" character varying(160) NOT NULL,
                "RequestFingerprint" character varying(64) NOT NULL,
                "Status" character varying(24) NOT NULL DEFAULT 'in_progress',
                "ResultEntityType" character varying(64) NULL,
                "ResultEntityId" character varying(128) NULL,
                "ResultStatusCode" integer NULL,
                "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                "UpdatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                "CompletedAt" timestamp with time zone NULL,
                "TenantId" text NULL
            );
            """,
            """ALTER TABLE IF EXISTS "TrustTransactions" ADD COLUMN IF NOT EXISTS "RowVersion" character varying(32) NOT NULL DEFAULT md5(random()::text || clock_timestamp()::text);""",
            """ALTER TABLE IF EXISTS "TrustBankAccounts" ADD COLUMN IF NOT EXISTS "RowVersion" character varying(32) NOT NULL DEFAULT md5(random()::text || clock_timestamp()::text);""",
            """ALTER TABLE IF EXISTS "ClientTrustLedgers" ADD COLUMN IF NOT EXISTS "RowVersion" character varying(32) NOT NULL DEFAULT md5(random()::text || clock_timestamp()::text);""",
            """ALTER TABLE IF EXISTS "TrustTransactions" ADD COLUMN IF NOT EXISTS "PostingBatchId" character varying(128) NULL;""",
            """ALTER TABLE IF EXISTS "TrustTransactions" ADD COLUMN IF NOT EXISTS "PrimaryJournalEntryId" character varying(128) NULL;""",
            """ALTER TABLE IF EXISTS "TrustTransactions" ADD COLUMN IF NOT EXISTS "ClearingStatus" character varying(32) NOT NULL DEFAULT 'not_applicable';""",
            """ALTER TABLE IF EXISTS "TrustTransactions" ADD COLUMN IF NOT EXISTS "ClearedAt" timestamp with time zone NULL;""",
            """ALTER TABLE IF EXISTS "TrustTransactions" ADD COLUMN IF NOT EXISTS "ReturnedAt" timestamp with time zone NULL;""",
            """ALTER TABLE IF EXISTS "TrustTransactions" ADD COLUMN IF NOT EXISTS "ReturnReason" character varying(512) NULL;""",
            """ALTER TABLE IF EXISTS "TrustJournalEntries" ADD COLUMN IF NOT EXISTS "PostingBatchId" character varying(128) NOT NULL DEFAULT '';""",
            """ALTER TABLE IF EXISTS "TrustJournalEntries" ADD COLUMN IF NOT EXISTS "AvailabilityClass" character varying(24) NOT NULL DEFAULT 'cleared';""",
            """ALTER TABLE IF EXISTS "TrustBankAccounts" ADD COLUMN IF NOT EXISTS "ClearedBalance" numeric(18,2) NOT NULL DEFAULT 0;""",
            """ALTER TABLE IF EXISTS "TrustBankAccounts" ADD COLUMN IF NOT EXISTS "UnclearedBalance" numeric(18,2) NOT NULL DEFAULT 0;""",
            """ALTER TABLE IF EXISTS "TrustBankAccounts" ADD COLUMN IF NOT EXISTS "AvailableDisbursementCapacity" numeric(18,2) NOT NULL DEFAULT 0;""",
            """ALTER TABLE IF EXISTS "ClientTrustLedgers" ADD COLUMN IF NOT EXISTS "ClearedBalance" numeric(18,2) NOT NULL DEFAULT 0;""",
            """ALTER TABLE IF EXISTS "ClientTrustLedgers" ADD COLUMN IF NOT EXISTS "UnclearedBalance" numeric(18,2) NOT NULL DEFAULT 0;""",
            """ALTER TABLE IF EXISTS "ClientTrustLedgers" ADD COLUMN IF NOT EXISTS "AvailableToDisburse" numeric(18,2) NOT NULL DEFAULT 0;""",
            """ALTER TABLE IF EXISTS "ClientTrustLedgers" ADD COLUMN IF NOT EXISTS "HoldAmount" numeric(18,2) NOT NULL DEFAULT 0;""",
            """UPDATE "TrustBankAccounts" SET "ClearedBalance" = round(coalesce("CurrentBalance", 0)::numeric, 2) WHERE round(coalesce("ClearedBalance", 0)::numeric, 2) = 0 AND round(coalesce("CurrentBalance", 0)::numeric, 2) <> 0;""",
            """UPDATE "TrustBankAccounts" SET "AvailableDisbursementCapacity" = round(coalesce("ClearedBalance", 0)::numeric, 2) WHERE round(coalesce("AvailableDisbursementCapacity", 0)::numeric, 2) = 0 AND round(coalesce("ClearedBalance", 0)::numeric, 2) <> 0;""",
            """UPDATE "ClientTrustLedgers" SET "ClearedBalance" = round(coalesce("RunningBalance", 0)::numeric, 2) WHERE round(coalesce("ClearedBalance", 0)::numeric, 2) = 0 AND round(coalesce("RunningBalance", 0)::numeric, 2) <> 0;""",
            """UPDATE "ClientTrustLedgers" SET "AvailableToDisburse" = round(coalesce("ClearedBalance", 0)::numeric - coalesce("HoldAmount", 0)::numeric, 2) WHERE round(coalesce("AvailableToDisburse", 0)::numeric, 2) = 0 AND round(coalesce("ClearedBalance", 0)::numeric, 2) <> 0;""",
            """ALTER TABLE IF EXISTS "TrustTransactions" ALTER COLUMN "BalanceBefore" TYPE numeric(18,2) USING round(coalesce("BalanceBefore", 0)::numeric, 2);""",
            """ALTER TABLE IF EXISTS "TrustTransactions" ALTER COLUMN "BalanceAfter" TYPE numeric(18,2) USING round(coalesce("BalanceAfter", 0)::numeric, 2);""",
            """ALTER TABLE IF EXISTS "TrustTransactions" ALTER COLUMN "Amount" TYPE numeric(18,2) USING round(coalesce("Amount", 0)::numeric, 2);""",
            """ALTER TABLE IF EXISTS "TrustBankAccounts" ALTER COLUMN "CurrentBalance" TYPE numeric(18,2) USING round(coalesce("CurrentBalance", 0)::numeric, 2);""",
            """ALTER TABLE IF EXISTS "TrustBankAccounts" ALTER COLUMN "ClearedBalance" TYPE numeric(18,2) USING round(coalesce("ClearedBalance", 0)::numeric, 2);""",
            """ALTER TABLE IF EXISTS "TrustBankAccounts" ALTER COLUMN "UnclearedBalance" TYPE numeric(18,2) USING round(coalesce("UnclearedBalance", 0)::numeric, 2);""",
            """ALTER TABLE IF EXISTS "TrustBankAccounts" ALTER COLUMN "AvailableDisbursementCapacity" TYPE numeric(18,2) USING round(coalesce("AvailableDisbursementCapacity", 0)::numeric, 2);""",
            """ALTER TABLE IF EXISTS "ReconciliationRecords" ALTER COLUMN "TrustLedgerBalance" TYPE numeric(18,2) USING round(coalesce("TrustLedgerBalance", 0)::numeric, 2);""",
            """ALTER TABLE IF EXISTS "ReconciliationRecords" ALTER COLUMN "DiscrepancyAmount" TYPE numeric(18,2) USING round(coalesce("DiscrepancyAmount", 0)::numeric, 2);""",
            """ALTER TABLE IF EXISTS "ReconciliationRecords" ALTER COLUMN "ClientLedgerSumBalance" TYPE numeric(18,2) USING round(coalesce("ClientLedgerSumBalance", 0)::numeric, 2);""",
            """ALTER TABLE IF EXISTS "ReconciliationRecords" ALTER COLUMN "BankStatementBalance" TYPE numeric(18,2) USING round(coalesce("BankStatementBalance", 0)::numeric, 2);""",
            """ALTER TABLE IF EXISTS "ClientTrustLedgers" ALTER COLUMN "RunningBalance" TYPE numeric(18,2) USING round(coalesce("RunningBalance", 0)::numeric, 2);""",
            """ALTER TABLE IF EXISTS "ClientTrustLedgers" ALTER COLUMN "ClearedBalance" TYPE numeric(18,2) USING round(coalesce("ClearedBalance", 0)::numeric, 2);""",
            """ALTER TABLE IF EXISTS "ClientTrustLedgers" ALTER COLUMN "UnclearedBalance" TYPE numeric(18,2) USING round(coalesce("UnclearedBalance", 0)::numeric, 2);""",
            """ALTER TABLE IF EXISTS "ClientTrustLedgers" ALTER COLUMN "AvailableToDisburse" TYPE numeric(18,2) USING round(coalesce("AvailableToDisburse", 0)::numeric, 2);""",
            """ALTER TABLE IF EXISTS "ClientTrustLedgers" ALTER COLUMN "HoldAmount" TYPE numeric(18,2) USING round(coalesce("HoldAmount", 0)::numeric, 2);""",
            """ALTER TABLE IF EXISTS "BillingPaymentAllocations" ADD COLUMN IF NOT EXISTS "IdempotencyKey" character varying(160) NULL;""",
            """UPDATE "BillingPaymentAllocations" SET "IdempotencyKey" = left('legacy:' || "Id", 160) WHERE coalesce("IdempotencyKey", '') = '';""",
            """ALTER TABLE IF EXISTS "BillingPaymentAllocations" ALTER COLUMN "IdempotencyKey" SET NOT NULL;""",
            """CREATE INDEX IF NOT EXISTS "IX_TrustJournalEntries_TenantId_TrustTransactionId_CreatedAt" ON "TrustJournalEntries" ("TenantId", "TrustTransactionId", "CreatedAt" DESC);""",
            """CREATE INDEX IF NOT EXISTS "IX_TrustJournalEntries_TenantId_TrustAccountId_CreatedAt" ON "TrustJournalEntries" ("TenantId", "TrustAccountId", "CreatedAt" DESC);""",
            """CREATE INDEX IF NOT EXISTS "IX_TrustJournalEntries_TenantId_ClientTrustLedgerId_CreatedAt" ON "TrustJournalEntries" ("TenantId", "ClientTrustLedgerId", "CreatedAt" DESC);""",
            """CREATE INDEX IF NOT EXISTS "IX_TrustJournalEntries_TenantId_ReversalOfTrustJournalEntryId" ON "TrustJournalEntries" ("TenantId", "ReversalOfTrustJournalEntryId");""",
            """CREATE INDEX IF NOT EXISTS "IX_TrustJournalEntries_TenantId_CorrelationKey" ON "TrustJournalEntries" ("TenantId", "CorrelationKey");""",
            """CREATE INDEX IF NOT EXISTS "IX_TrustJournalEntries_TenantId_PostingBatchId_EffectiveAt" ON "TrustJournalEntries" ("TenantId", "PostingBatchId", "EffectiveAt" DESC);""",
            """CREATE INDEX IF NOT EXISTS "IX_TrustJournalEntries_TenantId_TrustTransactionId_EntryKind_EffectiveAt" ON "TrustJournalEntries" ("TenantId", "TrustTransactionId", "EntryKind", "EffectiveAt" DESC);""",
            """CREATE INDEX IF NOT EXISTS "IX_TrustJournalEntries_TenantId_TrustAccountId_AvailabilityClass_EffectiveAt" ON "TrustJournalEntries" ("TenantId", "TrustAccountId", "AvailabilityClass", "EffectiveAt" DESC);""",
            """CREATE INDEX IF NOT EXISTS "IX_TrustJournalEntries_TenantId_ClientTrustLedgerId_AvailabilityClass_EffectiveAt" ON "TrustJournalEntries" ("TenantId", "ClientTrustLedgerId", "AvailabilityClass", "EffectiveAt" DESC);""",
            """CREATE INDEX IF NOT EXISTS "IX_TrustPostingBatches_TenantId_TrustTransactionId_EffectiveAt" ON "TrustPostingBatches" ("TenantId", "TrustTransactionId", "EffectiveAt" DESC);""",
            """CREATE INDEX IF NOT EXISTS "IX_TrustPostingBatches_TenantId_TrustAccountId_EffectiveAt" ON "TrustPostingBatches" ("TenantId", "TrustAccountId", "EffectiveAt" DESC);""",
            """CREATE INDEX IF NOT EXISTS "IX_TrustPostingBatches_TenantId_ParentPostingBatchId" ON "TrustPostingBatches" ("TenantId", "ParentPostingBatchId");""",
            """CREATE INDEX IF NOT EXISTS "IX_TrustStatementImports_TenantId_TrustAccountId_PeriodEnd" ON "TrustStatementImports" ("TenantId", "TrustAccountId", "PeriodEnd" DESC);""",
            """CREATE INDEX IF NOT EXISTS "IX_TrustStatementLines_TenantId_TrustStatementImportId_PostedAt" ON "TrustStatementLines" ("TenantId", "TrustStatementImportId", "PostedAt" DESC);""",
            """CREATE INDEX IF NOT EXISTS "IX_TrustStatementLines_TenantId_TrustAccountId_CheckNumber_Amount" ON "TrustStatementLines" ("TenantId", "TrustAccountId", "CheckNumber", "Amount");""",
            """CREATE INDEX IF NOT EXISTS "IX_TrustOutstandingItems_TenantId_TrustAccountId_PeriodEnd_Status" ON "TrustOutstandingItems" ("TenantId", "TrustAccountId", "PeriodEnd" DESC, "Status");""",
            """CREATE INDEX IF NOT EXISTS "IX_TrustOutstandingItems_TenantId_CorrelationKey" ON "TrustOutstandingItems" ("TenantId", "CorrelationKey");""",
            """CREATE INDEX IF NOT EXISTS "IX_TrustReconciliationPackets_TenantId_TrustAccountId_PeriodEnd" ON "TrustReconciliationPackets" ("TenantId", "TrustAccountId", "PeriodEnd" DESC);""",
            """CREATE INDEX IF NOT EXISTS "IX_TrustReconciliationSignoffs_TenantId_TrustReconciliationPacketId_SignedAt" ON "TrustReconciliationSignoffs" ("TenantId", "TrustReconciliationPacketId", "SignedAt" DESC);""",
            """CREATE UNIQUE INDEX IF NOT EXISTS "IX_TrustCommandDeduplications_TenantId_CommandName_ActorUserId_IdempotencyKey" ON "TrustCommandDeduplications" ("TenantId", "CommandName", "ActorUserId", "IdempotencyKey");""",
            """CREATE INDEX IF NOT EXISTS "IX_TrustCommandDeduplications_TenantId_ResultEntityType_ResultEntityId" ON "TrustCommandDeduplications" ("TenantId", "ResultEntityType", "ResultEntityId");""",
            """CREATE UNIQUE INDEX IF NOT EXISTS "IX_BillingPaymentAllocations_TenantId_IdempotencyKey" ON "BillingPaymentAllocations" ("TenantId", "IdempotencyKey");""",
            """CREATE INDEX IF NOT EXISTS "IX_Matters_TenantId_OpenDate" ON "Matters" ("TenantId", "OpenDate" DESC);""",
            """CREATE INDEX IF NOT EXISTS "IX_Matters_TenantId_CreatedByUserId_OpenDate" ON "Matters" ("TenantId", "CreatedByUserId", "OpenDate" DESC);""",
            """CREATE INDEX IF NOT EXISTS "IX_Matters_TenantId_ShareWithFirm_OpenDate" ON "Matters" ("TenantId", "ShareWithFirm", "OpenDate" DESC);""",
            """CREATE INDEX IF NOT EXISTS "IX_Tasks_TenantId_CreatedAt" ON "Tasks" ("TenantId", "CreatedAt" DESC);""",
            """CREATE INDEX IF NOT EXISTS "IX_Tasks_TenantId_MatterId_CreatedAt" ON "Tasks" ("TenantId", "MatterId", "CreatedAt" DESC);""",
            """CREATE INDEX IF NOT EXISTS "IX_TimeEntries_TenantId_Date" ON "TimeEntries" ("TenantId", "Date" DESC);""",
            """CREATE INDEX IF NOT EXISTS "IX_TimeEntries_TenantId_SubmittedBy_Date" ON "TimeEntries" ("TenantId", "SubmittedBy", "Date" DESC);""",
            """CREATE INDEX IF NOT EXISTS "IX_CalendarEvents_TenantId_Date" ON "CalendarEvents" ("TenantId", "Date" DESC);""",
            """CREATE INDEX IF NOT EXISTS "IX_CalendarEvents_TenantId_MatterId_Date" ON "CalendarEvents" ("TenantId", "MatterId", "Date" DESC);""",
            """CREATE INDEX IF NOT EXISTS "IX_Expenses_TenantId_Date" ON "Expenses" ("TenantId", "Date" DESC);""",
            """CREATE INDEX IF NOT EXISTS "IX_Expenses_TenantId_SubmittedBy_Date" ON "Expenses" ("TenantId", "SubmittedBy", "Date" DESC);""",
            """CREATE INDEX IF NOT EXISTS "IX_Leads_TenantId_CreatedAt" ON "Leads" ("TenantId", "CreatedAt" DESC);""",
            """CREATE INDEX IF NOT EXISTS "IX_Invoices_TenantId_CreatedAt" ON "Invoices" ("TenantId", "CreatedAt" DESC);""",
            """CREATE INDEX IF NOT EXISTS "IX_Invoices_TenantId_MatterId_CreatedAt" ON "Invoices" ("TenantId", "MatterId", "CreatedAt" DESC);""",
            """CREATE INDEX IF NOT EXISTS "IX_Documents_TenantId_CreatedAt" ON "Documents" ("TenantId", "CreatedAt" DESC);""",
            """CREATE INDEX IF NOT EXISTS "IX_Documents_TenantId_UploadedBy_CreatedAt" ON "Documents" ("TenantId", "UploadedBy", "CreatedAt" DESC);""",
            """CREATE INDEX IF NOT EXISTS "IX_Clients_TenantId_CreatedAt" ON "Clients" ("TenantId", "CreatedAt" DESC);""",
            """CREATE INDEX IF NOT EXISTS "IX_Clients_TenantId_NormalizedEmail_lookup" ON "Clients" ("TenantId", "NormalizedEmail");""",
            """CREATE INDEX IF NOT EXISTS "IX_Notifications_TenantId_UserId_CreatedAt" ON "Notifications" ("TenantId", "UserId", "CreatedAt" DESC);""",
            """CREATE INDEX IF NOT EXISTS "IX_Notifications_TenantId_ClientId_CreatedAt" ON "Notifications" ("TenantId", "ClientId", "CreatedAt" DESC);""",
            """CREATE UNIQUE INDEX IF NOT EXISTS "IX_MatterClientLinks_TenantId_MatterId_ClientId" ON "MatterClientLinks" ("TenantId", "MatterId", "ClientId");""",
            """CREATE INDEX IF NOT EXISTS "IX_MatterClientLinks_TenantId_ClientId_CreatedAt" ON "MatterClientLinks" ("TenantId", "ClientId", "CreatedAt" DESC);""",
            """CREATE INDEX IF NOT EXISTS "IX_MatterNotes_TenantId_MatterId_CreatedAt" ON "MatterNotes" ("TenantId", "MatterId", "CreatedAt" DESC);""",
            """CREATE INDEX IF NOT EXISTS "IX_MatterNotes_TenantId_MatterId_UpdatedAt" ON "MatterNotes" ("TenantId", "MatterId", "UpdatedAt" DESC);""",
            """CREATE INDEX IF NOT EXISTS "IX_Matters_TenantId_CreatedByUserId" ON "Matters" ("TenantId", "CreatedByUserId");""",
            """CREATE INDEX IF NOT EXISTS "IX_Matters_TenantId_ShareWithFirm" ON "Matters" ("TenantId", "ShareWithFirm");""",
            """ALTER TABLE IF EXISTS "Tasks" ADD COLUMN IF NOT EXISTS "CreatedByUserId" text NULL;""",
            """ALTER TABLE IF EXISTS "Tasks" ADD COLUMN IF NOT EXISTS "RowVersion" character varying(32) NOT NULL DEFAULT md5(random()::text || clock_timestamp()::text);""",
            """
            CREATE TABLE IF NOT EXISTS "TaskTemplates" (
                "Id" text PRIMARY KEY,
                "Name" character varying(200) NOT NULL,
                "Category" character varying(128) NULL,
                "Description" character varying(1000) NULL,
                "Definition" text NOT NULL,
                "IsActive" boolean NOT NULL DEFAULT TRUE,
                "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                "UpdatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                "TenantId" text NULL
            );
            """,
            """CREATE INDEX IF NOT EXISTS "IX_TaskTemplates_TenantId_IsActive_Category_Name" ON "TaskTemplates" ("TenantId", "IsActive", "Category", "Name");""",
            """CREATE INDEX IF NOT EXISTS "IX_TaskTemplates_TenantId_UpdatedAt" ON "TaskTemplates" ("TenantId", "UpdatedAt" DESC);""",
            """CREATE INDEX IF NOT EXISTS "IX_Tasks_TenantId_CreatedByUserId_UpdatedAt" ON "Tasks" ("TenantId", "CreatedByUserId", "UpdatedAt" DESC);""",
            """CREATE INDEX IF NOT EXISTS "IX_Tasks_TenantId_AssignedEmployeeId_Status_UpdatedAt" ON "Tasks" ("TenantId", "AssignedEmployeeId", "Status", "UpdatedAt" DESC);""",
            """CREATE INDEX IF NOT EXISTS "IX_Tasks_TenantId_Status_DueDate" ON "Tasks" ("TenantId", "Status", "DueDate");""",
            """CREATE INDEX IF NOT EXISTS "IX_Tasks_TenantId_ReminderSent_ReminderAt" ON "Tasks" ("TenantId", "ReminderSent", "ReminderAt");""",
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

            var commandsHash = StartupTaskStateStore.ComputeStableHash(Commands);
            var appliedHash = await StartupTaskStateStore.GetValueAsync(
                context,
                taskKey: "postgres-schema-compatibility",
                cancellationToken);

            if (string.Equals(appliedHash, commandsHash, StringComparison.Ordinal))
            {
                logger.LogDebug("Skipping PostgreSQL schema compatibility checks because the current command set is already applied.");
                return;
            }

            logger.LogInformation("Applying PostgreSQL schema compatibility checks for legacy deployments.");

            foreach (var command in Commands)
            {
                await context.Database.ExecuteSqlRawAsync(command, cancellationToken);
            }

            await StartupTaskStateStore.SetValueAsync(
                context,
                taskKey: "postgres-schema-compatibility",
                value: commandsHash,
                cancellationToken);
        }
    }
}
