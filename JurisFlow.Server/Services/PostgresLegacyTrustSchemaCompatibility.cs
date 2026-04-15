using JurisFlow.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace JurisFlow.Server.Services
{
    internal static class PostgresLegacyTrustSchemaCompatibility
    {
        private static readonly string[] Commands = BuildCommands();

        public static async Task EnsureAsync(JurisFlowDbContext context, ILogger logger, CancellationToken cancellationToken = default)
        {
            if (!context.Database.IsNpgsql())
            {
                return;
            }

            var commandsHash = StartupTaskStateStore.ComputeStableHash(Commands);
            var appliedHash = await StartupTaskStateStore.GetValueAsync(
                context,
                taskKey: "postgres-legacy-trust-schema-compatibility",
                cancellationToken);

            if (string.Equals(appliedHash, commandsHash, StringComparison.Ordinal))
            {
                logger.LogDebug("Skipping PostgreSQL legacy trust schema compatibility checks because the current command set is already applied.");
                return;
            }

            logger.LogInformation("Applying PostgreSQL legacy trust schema compatibility checks for Supabase deployments.");

            foreach (var command in Commands)
            {
                await context.Database.ExecuteSqlRawAsync(command, cancellationToken);
            }

            await StartupTaskStateStore.SetValueAsync(
                context,
                taskKey: "postgres-legacy-trust-schema-compatibility",
                value: commandsHash,
                cancellationToken);
        }

        private static string[] BuildCommands()
        {
            return
            [
                .. BuildAccountAndApprovalCommands(),
                .. BuildComplianceAndPolicyCommands(),
                .. BuildLifecycleAndOperationsCommands(),
                .. BuildIndexes()
            ];
        }

        private static string[] BuildAccountAndApprovalCommands()
        {
            return
            [
                """ALTER TABLE IF EXISTS "TrustBankAccounts" ADD COLUMN IF NOT EXISTS "TenantId" text NULL;""",
                """ALTER TABLE IF EXISTS "TrustBankAccounts" ADD COLUMN IF NOT EXISTS "EntityId" text NULL;""",
                """ALTER TABLE IF EXISTS "TrustBankAccounts" ADD COLUMN IF NOT EXISTS "OfficeId" text NULL;""",
                """ALTER TABLE IF EXISTS "TrustBankAccounts" ADD COLUMN IF NOT EXISTS "AccountType" character varying(24) NOT NULL DEFAULT 'iolta';""",
                """ALTER TABLE IF EXISTS "TrustBankAccounts" ADD COLUMN IF NOT EXISTS "ResponsibleLawyerUserId" character varying(128) NULL;""",
                """ALTER TABLE IF EXISTS "TrustBankAccounts" ADD COLUMN IF NOT EXISTS "AllowedSignatoriesJson" text NULL;""",
                """ALTER TABLE IF EXISTS "TrustBankAccounts" ADD COLUMN IF NOT EXISTS "JurisdictionPolicyKey" character varying(64) NULL;""",
                """ALTER TABLE IF EXISTS "TrustBankAccounts" ADD COLUMN IF NOT EXISTS "StatementCadence" character varying(24) NOT NULL DEFAULT 'monthly';""",
                """ALTER TABLE IF EXISTS "TrustBankAccounts" ADD COLUMN IF NOT EXISTS "OverdraftNotificationEnabled" boolean NOT NULL DEFAULT TRUE;""",
                """ALTER TABLE IF EXISTS "TrustBankAccounts" ADD COLUMN IF NOT EXISTS "BankReferenceMetadataJson" text NULL;""",
                """ALTER TABLE IF EXISTS "TrustTransactions" ADD COLUMN IF NOT EXISTS "ApprovalStatus" character varying(24) NULL;""",
                """ALTER TABLE IF EXISTS "TrustTransactions" ADD COLUMN IF NOT EXISTS "DisbursementClass" character varying(32) NULL;""",
                """ALTER TABLE IF EXISTS "TrustTransactions" ADD COLUMN IF NOT EXISTS "PolicyDecisionJson" text NULL;""",
                """
                CREATE TABLE IF NOT EXISTS "TrustApprovalRequirements" (
                    "Id" text PRIMARY KEY,
                    "TrustTransactionId" character varying(128) NOT NULL,
                    "RequirementType" character varying(48) NOT NULL DEFAULT 'operational_approval',
                    "RequiredCount" integer NOT NULL DEFAULT 1,
                    "SatisfiedCount" integer NOT NULL DEFAULT 0,
                    "Status" character varying(24) NOT NULL DEFAULT 'pending',
                    "PolicyKey" character varying(64) NULL,
                    "Summary" character varying(128) NULL,
                    "MetadataJson" text NULL,
                    "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                    "UpdatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                    "TenantId" text NULL
                );
                """,
                """
                CREATE TABLE IF NOT EXISTS "TrustApprovalDecisions" (
                    "Id" text PRIMARY KEY,
                    "TrustTransactionId" character varying(128) NOT NULL,
                    "TrustApprovalRequirementId" character varying(128) NOT NULL,
                    "ActorUserId" character varying(128) NOT NULL,
                    "ActorRole" character varying(64) NULL,
                    "DecisionType" character varying(24) NOT NULL DEFAULT 'approve',
                    "Notes" character varying(2048) NULL,
                    "Reason" character varying(2048) NULL,
                    "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                    "TenantId" text NULL
                );
                """,
                """
                CREATE TABLE IF NOT EXISTS "TrustApprovalOverrides" (
                    "Id" text PRIMARY KEY,
                    "TrustTransactionId" character varying(128) NOT NULL,
                    "TrustApprovalRequirementId" character varying(128) NULL,
                    "ActorUserId" character varying(128) NOT NULL,
                    "ActorRole" character varying(64) NULL,
                    "Reason" character varying(2048) NOT NULL,
                    "MetadataJson" text NULL,
                    "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                    "TenantId" text NULL
                );
                """
            ];
        }

        private static string[] BuildComplianceAndPolicyCommands()
        {
            return
            [
                """
                CREATE TABLE IF NOT EXISTS "TrustComplianceExports" (
                    "Id" text PRIMARY KEY,
                    "ExportType" character varying(48) NOT NULL,
                    "Format" character varying(16) NOT NULL DEFAULT 'json',
                    "Status" character varying(24) NOT NULL DEFAULT 'completed',
                    "TrustAccountId" character varying(128) NULL,
                    "ClientTrustLedgerId" character varying(128) NULL,
                    "TrustMonthCloseId" character varying(128) NULL,
                    "TrustReconciliationPacketId" character varying(128) NULL,
                    "FileName" character varying(256) NOT NULL,
                    "ContentType" character varying(128) NOT NULL DEFAULT 'application/json',
                    "SummaryJson" text NULL,
                    "PayloadJson" text NULL,
                    "GeneratedBy" character varying(128) NULL,
                    "ParentExportId" character varying(128) NULL,
                    "IntegrityStatus" character varying(32) NOT NULL DEFAULT 'unsigned',
                    "RetentionPolicyTag" character varying(64) NULL,
                    "RedactionProfile" character varying(64) NULL,
                    "ProvenanceJson" text NULL,
                    "GeneratedAt" timestamp with time zone NOT NULL DEFAULT now(),
                    "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                    "UpdatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                    "TenantId" text NULL
                );
                """,
                """ALTER TABLE IF EXISTS "TrustComplianceExports" ADD COLUMN IF NOT EXISTS "ParentExportId" character varying(128) NULL;""",
                """ALTER TABLE IF EXISTS "TrustComplianceExports" ADD COLUMN IF NOT EXISTS "IntegrityStatus" character varying(32) NOT NULL DEFAULT 'unsigned';""",
                """ALTER TABLE IF EXISTS "TrustComplianceExports" ADD COLUMN IF NOT EXISTS "RetentionPolicyTag" character varying(64) NULL;""",
                """ALTER TABLE IF EXISTS "TrustComplianceExports" ADD COLUMN IF NOT EXISTS "RedactionProfile" character varying(64) NULL;""",
                """ALTER TABLE IF EXISTS "TrustComplianceExports" ADD COLUMN IF NOT EXISTS "ProvenanceJson" text NULL;""",
                """
                CREATE TABLE IF NOT EXISTS "TrustJurisdictionPolicies" (
                    "Id" text PRIMARY KEY,
                    "PolicyKey" character varying(64) NOT NULL,
                    "Jurisdiction" character varying(24) NOT NULL,
                    "Name" character varying(128) NULL,
                    "AccountType" character varying(24) NOT NULL DEFAULT 'all',
                    "VersionNumber" integer NOT NULL DEFAULT 1,
                    "IsActive" boolean NOT NULL DEFAULT TRUE,
                    "IsSystemBaseline" boolean NOT NULL DEFAULT FALSE,
                    "RequireMakerChecker" boolean NOT NULL DEFAULT TRUE,
                    "RequireOverrideReason" boolean NOT NULL DEFAULT TRUE,
                    "DualApprovalThreshold" numeric(18,2) NOT NULL DEFAULT 10000,
                    "ResponsibleLawyerApprovalThreshold" numeric(18,2) NOT NULL DEFAULT 25000,
                    "SignatoryApprovalThreshold" numeric(18,2) NOT NULL DEFAULT 5000,
                    "MonthlyCloseCadenceDays" integer NOT NULL DEFAULT 30,
                    "ExceptionAgingSlaHours" integer NOT NULL DEFAULT 48,
                    "RetentionPeriodMonths" integer NOT NULL DEFAULT 60,
                    "RequireMonthlyThreeWayReconciliation" boolean NOT NULL DEFAULT TRUE,
                    "RequireResponsibleLawyerAssignment" boolean NOT NULL DEFAULT TRUE,
                    "DisbursementClassesRequiringSignatoryJson" text NULL,
                    "OperationalApproverRolesJson" text NULL,
                    "OverrideApproverRolesJson" text NULL,
                    "MetadataJson" text NULL,
                    "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                    "UpdatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                    "TenantId" text NULL
                );
                """,
                """ALTER TABLE IF EXISTS "TrustJurisdictionPolicies" ADD COLUMN IF NOT EXISTS "AccountType" character varying(24) NOT NULL DEFAULT 'all';""",
                """ALTER TABLE IF EXISTS "TrustJurisdictionPolicies" ADD COLUMN IF NOT EXISTS "VersionNumber" integer NOT NULL DEFAULT 1;""",
                """ALTER TABLE IF EXISTS "TrustJurisdictionPolicies" ADD COLUMN IF NOT EXISTS "IsSystemBaseline" boolean NOT NULL DEFAULT FALSE;""",
                """ALTER TABLE IF EXISTS "TrustJurisdictionPolicies" ADD COLUMN IF NOT EXISTS "RetentionPeriodMonths" integer NOT NULL DEFAULT 60;""",
                """ALTER TABLE IF EXISTS "TrustJurisdictionPolicies" ADD COLUMN IF NOT EXISTS "RequireMonthlyThreeWayReconciliation" boolean NOT NULL DEFAULT TRUE;""",
                """ALTER TABLE IF EXISTS "TrustJurisdictionPolicies" ADD COLUMN IF NOT EXISTS "RequireResponsibleLawyerAssignment" boolean NOT NULL DEFAULT TRUE;""",
                """
                CREATE TABLE IF NOT EXISTS "TrustMonthCloses" (
                    "Id" text PRIMARY KEY,
                    "TrustAccountId" character varying(128) NOT NULL,
                    "PolicyKey" character varying(64) NOT NULL,
                    "PeriodStart" timestamp with time zone NOT NULL,
                    "PeriodEnd" timestamp with time zone NOT NULL,
                    "ReconciliationPacketId" character varying(128) NULL,
                    "VersionNumber" integer NOT NULL DEFAULT 1,
                    "IsCanonical" boolean NOT NULL DEFAULT TRUE,
                    "Status" character varying(24) NOT NULL DEFAULT 'draft',
                    "OpenExceptionCount" integer NOT NULL DEFAULT 0,
                    "PreparedBy" character varying(128) NULL,
                    "PreparedAt" timestamp with time zone NOT NULL DEFAULT now(),
                    "ReviewerSignedBy" character varying(128) NULL,
                    "ReviewerSignedAt" timestamp with time zone NULL,
                    "ResponsibleLawyerSignedBy" character varying(128) NULL,
                    "ResponsibleLawyerSignedAt" timestamp with time zone NULL,
                    "ReopenedFromMonthCloseId" character varying(128) NULL,
                    "SupersededByMonthCloseId" character varying(128) NULL,
                    "ReopenedBy" character varying(128) NULL,
                    "ReopenedAt" timestamp with time zone NULL,
                    "ReopenReason" character varying(2048) NULL,
                    "SupersededBy" character varying(128) NULL,
                    "SupersedeReason" character varying(2048) NULL,
                    "SupersededAt" timestamp with time zone NULL,
                    "SummaryJson" text NULL,
                    "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                    "UpdatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                    "TenantId" text NULL
                );
                """,
                """ALTER TABLE IF EXISTS "TrustMonthCloses" ADD COLUMN IF NOT EXISTS "VersionNumber" integer NOT NULL DEFAULT 1;""",
                """ALTER TABLE IF EXISTS "TrustMonthCloses" ADD COLUMN IF NOT EXISTS "IsCanonical" boolean NOT NULL DEFAULT TRUE;""",
                """ALTER TABLE IF EXISTS "TrustMonthCloses" ADD COLUMN IF NOT EXISTS "ReopenedFromMonthCloseId" character varying(128) NULL;""",
                """ALTER TABLE IF EXISTS "TrustMonthCloses" ADD COLUMN IF NOT EXISTS "SupersededByMonthCloseId" character varying(128) NULL;""",
                """ALTER TABLE IF EXISTS "TrustMonthCloses" ADD COLUMN IF NOT EXISTS "ReopenedBy" character varying(128) NULL;""",
                """ALTER TABLE IF EXISTS "TrustMonthCloses" ADD COLUMN IF NOT EXISTS "ReopenedAt" timestamp with time zone NULL;""",
                """ALTER TABLE IF EXISTS "TrustMonthCloses" ADD COLUMN IF NOT EXISTS "ReopenReason" character varying(2048) NULL;""",
                """ALTER TABLE IF EXISTS "TrustMonthCloses" ADD COLUMN IF NOT EXISTS "SupersededBy" character varying(128) NULL;""",
                """ALTER TABLE IF EXISTS "TrustMonthCloses" ADD COLUMN IF NOT EXISTS "SupersedeReason" character varying(2048) NULL;""",
                """ALTER TABLE IF EXISTS "TrustMonthCloses" ADD COLUMN IF NOT EXISTS "SupersededAt" timestamp with time zone NULL;""",
                """
                CREATE TABLE IF NOT EXISTS "TrustMonthCloseSteps" (
                    "Id" text PRIMARY KEY,
                    "TrustMonthCloseId" character varying(128) NOT NULL,
                    "StepKey" character varying(48) NOT NULL,
                    "Status" character varying(24) NOT NULL DEFAULT 'pending',
                    "Notes" character varying(2048) NULL,
                    "CompletedBy" character varying(128) NULL,
                    "CompletedAt" timestamp with time zone NULL,
                    "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                    "UpdatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                    "TenantId" text NULL
                );
                """,
                """
                CREATE TABLE IF NOT EXISTS "TrustMonthCloseAttestations" (
                    "Id" text PRIMARY KEY,
                    "TrustMonthCloseId" character varying(128) NOT NULL,
                    "Role" character varying(48) NOT NULL,
                    "AttestationKey" character varying(64) NOT NULL,
                    "Label" character varying(256) NULL,
                    "Accepted" boolean NOT NULL DEFAULT FALSE,
                    "Notes" character varying(2048) NULL,
                    "SignedBy" character varying(128) NULL,
                    "SignedAt" timestamp with time zone NULL,
                    "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                    "UpdatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                    "TenantId" text NULL
                );
                """
            ];
        }

        private static string[] BuildLifecycleAndOperationsCommands()
        {
            return
            [
                """ALTER TABLE IF EXISTS "TrustStatementImports" ADD COLUMN IF NOT EXISTS "SourceFileName" character varying(256) NULL;""",
                """ALTER TABLE IF EXISTS "TrustStatementImports" ADD COLUMN IF NOT EXISTS "SourceFileHash" character varying(128) NULL;""",
                """ALTER TABLE IF EXISTS "TrustStatementImports" ADD COLUMN IF NOT EXISTS "SourceEvidenceKey" character varying(256) NULL;""",
                """ALTER TABLE IF EXISTS "TrustStatementImports" ADD COLUMN IF NOT EXISTS "ImportFingerprint" character varying(128) NULL;""",
                """ALTER TABLE IF EXISTS "TrustStatementImports" ADD COLUMN IF NOT EXISTS "DuplicateOfStatementImportId" character varying(128) NULL;""",
                """ALTER TABLE IF EXISTS "TrustStatementImports" ADD COLUMN IF NOT EXISTS "SupersededByStatementImportId" character varying(128) NULL;""",
                """ALTER TABLE IF EXISTS "TrustStatementImports" ADD COLUMN IF NOT EXISTS "SupersededBy" character varying(128) NULL;""",
                """ALTER TABLE IF EXISTS "TrustStatementImports" ADD COLUMN IF NOT EXISTS "SupersededAt" timestamp with time zone NULL;""",
                """ALTER TABLE IF EXISTS "TrustStatementImports" ADD COLUMN IF NOT EXISTS "SourceFileSizeBytes" bigint NULL;""",
                """ALTER TABLE IF EXISTS "TrustStatementLines" ADD COLUMN IF NOT EXISTS "MatchMethod" character varying(32) NOT NULL DEFAULT 'none';""",
                """ALTER TABLE IF EXISTS "TrustStatementLines" ADD COLUMN IF NOT EXISTS "MatchConfidence" numeric(5,2) NULL;""",
                """ALTER TABLE IF EXISTS "TrustStatementLines" ADD COLUMN IF NOT EXISTS "MatchedBy" character varying(128) NULL;""",
                """ALTER TABLE IF EXISTS "TrustStatementLines" ADD COLUMN IF NOT EXISTS "MatchedAt" timestamp with time zone NULL;""",
                """ALTER TABLE IF EXISTS "TrustStatementLines" ADD COLUMN IF NOT EXISTS "MatchNotes" text NULL;""",
                """ALTER TABLE IF EXISTS "TrustStatementLines" ADD COLUMN IF NOT EXISTS "UpdatedAt" timestamp with time zone NOT NULL DEFAULT now();""",
                """ALTER TABLE IF EXISTS "TrustOutstandingItems" ADD COLUMN IF NOT EXISTS "TrustStatementLineId" character varying(128) NULL;""",
                """ALTER TABLE IF EXISTS "TrustOutstandingItems" ADD COLUMN IF NOT EXISTS "ReasonCode" character varying(64) NULL;""",
                """ALTER TABLE IF EXISTS "TrustOutstandingItems" ADD COLUMN IF NOT EXISTS "AttachmentEvidenceKey" character varying(256) NULL;""",
                """ALTER TABLE IF EXISTS "TrustReconciliationPackets" ADD COLUMN IF NOT EXISTS "MatchedStatementLineCount" integer NOT NULL DEFAULT 0;""",
                """ALTER TABLE IF EXISTS "TrustReconciliationPackets" ADD COLUMN IF NOT EXISTS "UnmatchedStatementLineCount" integer NOT NULL DEFAULT 0;""",
                """ALTER TABLE IF EXISTS "TrustReconciliationPackets" ADD COLUMN IF NOT EXISTS "IsCanonical" boolean NOT NULL DEFAULT TRUE;""",
                """ALTER TABLE IF EXISTS "TrustReconciliationPackets" ADD COLUMN IF NOT EXISTS "VersionNumber" integer NOT NULL DEFAULT 1;""",
                """ALTER TABLE IF EXISTS "TrustReconciliationPackets" ADD COLUMN IF NOT EXISTS "SupersededByPacketId" character varying(128) NULL;""",
                """ALTER TABLE IF EXISTS "TrustReconciliationPackets" ADD COLUMN IF NOT EXISTS "SupersededBy" character varying(128) NULL;""",
                """ALTER TABLE IF EXISTS "TrustReconciliationPackets" ADD COLUMN IF NOT EXISTS "SupersedeReason" character varying(2048) NULL;""",
                """ALTER TABLE IF EXISTS "TrustReconciliationPackets" ADD COLUMN IF NOT EXISTS "SupersededAt" timestamp with time zone NULL;""",
                """
                CREATE TABLE IF NOT EXISTS "TrustOperationalAlerts" (
                    "Id" text PRIMARY KEY,
                    "AlertKey" character varying(256) NOT NULL,
                    "AlertType" character varying(64) NOT NULL,
                    "Severity" character varying(24) NOT NULL DEFAULT 'warning',
                    "TrustAccountId" character varying(128) NULL,
                    "RelatedEntityType" character varying(128) NULL,
                    "RelatedEntityId" character varying(128) NULL,
                    "PeriodEnd" timestamp with time zone NULL,
                    "Title" character varying(256) NOT NULL,
                    "Summary" character varying(2048) NOT NULL,
                    "ActionHint" character varying(2048) NULL,
                    "SourceStatus" character varying(32) NOT NULL DEFAULT 'open',
                    "WorkflowStatus" character varying(32) NOT NULL DEFAULT 'open',
                    "AssignedUserId" character varying(128) NULL,
                    "OpenedAt" timestamp with time zone NOT NULL,
                    "FirstDetectedAt" timestamp with time zone NOT NULL,
                    "LastDetectedAt" timestamp with time zone NOT NULL,
                    "AcknowledgedBy" character varying(128) NULL,
                    "AcknowledgedAt" timestamp with time zone NULL,
                    "EscalatedBy" character varying(128) NULL,
                    "EscalatedAt" timestamp with time zone NULL,
                    "ResolvedBy" character varying(128) NULL,
                    "ResolvedAt" timestamp with time zone NULL,
                    "LastNotificationAt" timestamp with time zone NULL,
                    "NotificationCount" integer NOT NULL DEFAULT 0,
                    "MetadataJson" text NULL,
                    "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                    "UpdatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                    "TenantId" text NULL
                );
                """,
                """
                CREATE TABLE IF NOT EXISTS "TrustOperationalAlertEvents" (
                    "Id" text PRIMARY KEY,
                    "TrustOperationalAlertId" character varying(128) NOT NULL,
                    "EventType" character varying(48) NOT NULL,
                    "ActorUserId" character varying(128) NULL,
                    "Notes" character varying(2048) NULL,
                    "MetadataJson" text NULL,
                    "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                    "TenantId" text NULL
                );
                """,
                """
                CREATE TABLE IF NOT EXISTS "TrustEvidenceFiles" (
                    "Id" text PRIMARY KEY,
                    "TrustAccountId" character varying(128) NOT NULL,
                    "PeriodStart" timestamp with time zone NOT NULL,
                    "PeriodEnd" timestamp with time zone NOT NULL,
                    "Source" character varying(64) NOT NULL DEFAULT 'manual_manifest',
                    "FileName" character varying(256) NOT NULL,
                    "ContentType" character varying(128) NULL,
                    "FileHash" character varying(128) NOT NULL,
                    "EvidenceKey" character varying(256) NULL,
                    "FileSizeBytes" bigint NULL,
                    "Status" character varying(24) NOT NULL DEFAULT 'registered',
                    "LatestParserRunId" character varying(128) NULL,
                    "CanonicalStatementImportId" character varying(128) NULL,
                    "DuplicateOfEvidenceFileId" character varying(128) NULL,
                    "SupersededByEvidenceFileId" character varying(128) NULL,
                    "SupersededBy" character varying(128) NULL,
                    "SupersededAt" timestamp with time zone NULL,
                    "RegisteredBy" character varying(128) NULL,
                    "Notes" character varying(2048) NULL,
                    "MetadataJson" text NULL,
                    "RegisteredAt" timestamp with time zone NOT NULL DEFAULT now(),
                    "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                    "UpdatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                    "TenantId" text NULL
                );
                """,
                """
                CREATE TABLE IF NOT EXISTS "TrustStatementParserRuns" (
                    "Id" text PRIMARY KEY,
                    "TrustAccountId" character varying(128) NOT NULL,
                    "TrustEvidenceFileId" character varying(128) NOT NULL,
                    "TrustStatementImportId" character varying(128) NULL,
                    "PeriodStart" timestamp with time zone NOT NULL,
                    "PeriodEnd" timestamp with time zone NOT NULL,
                    "ParserKey" character varying(64) NOT NULL DEFAULT 'manual_manifest_v1',
                    "Status" character varying(24) NOT NULL DEFAULT 'pending',
                    "AttemptCount" integer NOT NULL DEFAULT 1,
                    "Source" character varying(64) NOT NULL DEFAULT 'evidence_registry',
                    "StartedBy" character varying(128) NULL,
                    "Notes" character varying(2048) NULL,
                    "ErrorMessage" character varying(2048) NULL,
                    "SummaryJson" text NULL,
                    "StartedAt" timestamp with time zone NOT NULL DEFAULT now(),
                    "CompletedAt" timestamp with time zone NULL,
                    "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                    "UpdatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                    "TenantId" text NULL
                );
                """,
                """
                CREATE TABLE IF NOT EXISTS "TrustJurisdictionPacketTemplates" (
                    "Id" text PRIMARY KEY,
                    "PolicyKey" character varying(64) NOT NULL,
                    "Jurisdiction" character varying(24) NOT NULL,
                    "AccountType" character varying(24) NOT NULL DEFAULT 'all',
                    "TemplateKey" character varying(64) NOT NULL,
                    "Name" character varying(128) NULL,
                    "VersionNumber" integer NOT NULL DEFAULT 1,
                    "IsActive" boolean NOT NULL DEFAULT TRUE,
                    "RequiredSectionsJson" text NULL,
                    "RequiredAttestationsJson" text NULL,
                    "DisclosureBlocksJson" text NULL,
                    "RenderingProfileJson" text NULL,
                    "MetadataJson" text NULL,
                    "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                    "UpdatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                    "TenantId" text NULL
                );
                """,
                """
                CREATE TABLE IF NOT EXISTS "TrustOpsInboxItems" (
                    "Id" text PRIMARY KEY,
                    "TrustOperationalAlertId" character varying(128) NULL,
                    "TrustCloseForecastSnapshotId" character varying(128) NULL,
                    "ItemType" character varying(48) NOT NULL,
                    "BlockerGroup" character varying(48) NOT NULL,
                    "Severity" character varying(24) NOT NULL DEFAULT 'warning',
                    "TrustAccountId" character varying(128) NULL,
                    "Jurisdiction" character varying(24) NULL,
                    "OfficeId" character varying(128) NULL,
                    "AssignedUserId" character varying(128) NULL,
                    "WorkflowStatus" character varying(32) NOT NULL DEFAULT 'open',
                    "DueAt" timestamp with time zone NULL,
                    "DeferredUntil" timestamp with time zone NULL,
                    "LastActionAt" timestamp with time zone NULL,
                    "Title" character varying(256) NOT NULL,
                    "Summary" character varying(2048) NOT NULL,
                    "ActionHint" character varying(2048) NULL,
                    "SuggestedExportType" character varying(48) NULL,
                    "SuggestedRoute" character varying(256) NULL,
                    "MetadataJson" text NULL,
                    "OpenedAt" timestamp with time zone NOT NULL DEFAULT now(),
                    "LastDetectedAt" timestamp with time zone NOT NULL DEFAULT now(),
                    "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                    "UpdatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                    "TenantId" text NULL
                );
                """,
                """ALTER TABLE IF EXISTS "TrustOpsInboxItems" ADD COLUMN IF NOT EXISTS "TrustCloseForecastSnapshotId" character varying(128) NULL;""",
                """
                CREATE TABLE IF NOT EXISTS "TrustOpsInboxEvents" (
                    "Id" text PRIMARY KEY,
                    "TrustOpsInboxItemId" character varying(128) NOT NULL,
                    "EventType" character varying(48) NOT NULL,
                    "ActorUserId" character varying(128) NULL,
                    "Notes" character varying(2048) NULL,
                    "MetadataJson" text NULL,
                    "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                    "TenantId" text NULL
                );
                """,
                """
                CREATE TABLE IF NOT EXISTS "TrustBundleSignatures" (
                    "Id" text PRIMARY KEY,
                    "ManifestExportId" character varying(128) NOT NULL,
                    "SignatureAlgorithm" character varying(64) NOT NULL DEFAULT 'hmac-sha256',
                    "SignatureDigest" character varying(256) NOT NULL,
                    "IntegrityStatus" character varying(32) NOT NULL DEFAULT 'signed',
                    "VerificationStatus" character varying(64) NOT NULL DEFAULT 'verified',
                    "SignedBy" character varying(128) NULL,
                    "SignedAt" timestamp with time zone NOT NULL DEFAULT now(),
                    "VerifiedAt" timestamp with time zone NULL,
                    "RetentionPolicyTag" character varying(64) NULL,
                    "RedactionProfile" character varying(64) NULL,
                    "ParentManifestExportId" character varying(128) NULL,
                    "EvidenceManifestJson" text NULL,
                    "MetadataJson" text NULL,
                    "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                    "UpdatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                    "TenantId" text NULL
                );
                """,
                """
                CREATE TABLE IF NOT EXISTS "TrustCloseForecastSnapshots" (
                    "Id" text PRIMARY KEY,
                    "TrustAccountId" character varying(128) NOT NULL,
                    "Jurisdiction" character varying(24) NULL,
                    "OfficeId" character varying(128) NULL,
                    "StatementCadence" character varying(24) NOT NULL DEFAULT 'monthly',
                    "PeriodStart" timestamp with time zone NOT NULL,
                    "PeriodEnd" timestamp with time zone NOT NULL,
                    "CloseDueAt" timestamp with time zone NOT NULL,
                    "ReadinessStatus" character varying(24) NOT NULL DEFAULT 'blocked',
                    "Severity" character varying(24) NOT NULL DEFAULT 'warning',
                    "MissingStatementImport" boolean NOT NULL DEFAULT FALSE,
                    "LatestStatementImportId" character varying(128) NULL,
                    "StatementImportedAt" timestamp with time zone NULL,
                    "HasCanonicalPacket" boolean NOT NULL DEFAULT FALSE,
                    "CanonicalPacketId" character varying(128) NULL,
                    "PacketStatus" character varying(24) NULL,
                    "HasCanonicalMonthClose" boolean NOT NULL DEFAULT FALSE,
                    "CanonicalMonthCloseId" character varying(128) NULL,
                    "MonthCloseStatus" character varying(24) NULL,
                    "OpenExceptionCount" integer NOT NULL DEFAULT 0,
                    "OutstandingItemCount" integer NOT NULL DEFAULT 0,
                    "MissingRequiredSectionCount" integer NOT NULL DEFAULT 0,
                    "MissingAttestationCount" integer NOT NULL DEFAULT 0,
                    "UnclearedBalance" numeric(18,2) NOT NULL DEFAULT 0,
                    "UnclearedEntryCount" integer NOT NULL DEFAULT 0,
                    "OldestOutstandingAgeDays" integer NULL,
                    "OldestUnclearedAgeDays" integer NULL,
                    "DraftBundleEligible" boolean NOT NULL DEFAULT FALSE,
                    "DraftBundleManifestExportId" character varying(128) NULL,
                    "DraftBundleGeneratedAt" timestamp with time zone NULL,
                    "RecommendedAction" character varying(2048) NULL,
                    "ReminderCount" integer NOT NULL DEFAULT 0,
                    "LastReminderAt" timestamp with time zone NULL,
                    "NextReminderAt" timestamp with time zone NULL,
                    "EscalatedAt" timestamp with time zone NULL,
                    "LastAutomationRunAt" timestamp with time zone NULL,
                    "LastAutomationRunBy" character varying(128) NULL,
                    "SummaryJson" text NULL,
                    "MetadataJson" text NULL,
                    "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                    "UpdatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                    "TenantId" text NULL
                );
                """
            ];
        }

        private static string[] BuildIndexes()
        {
            return
            [
                """CREATE UNIQUE INDEX IF NOT EXISTS "IX_TrustOperationalAlerts_TenantId_AlertKey" ON "TrustOperationalAlerts" ("TenantId", "AlertKey");""",
                """CREATE UNIQUE INDEX IF NOT EXISTS "IX_TrustJurisdictionPolicies_TenantId_PolicyKey_Jurisdiction_AccountType_VersionNumber" ON "TrustJurisdictionPolicies" ("TenantId", "PolicyKey", "Jurisdiction", "AccountType", "VersionNumber");""",
                """CREATE UNIQUE INDEX IF NOT EXISTS "IX_TrustJurisdictionPacketTemplates_TenantId_PolicyKey_Jurisdiction_AccountType_TemplateKey_VersionNumber" ON "TrustJurisdictionPacketTemplates" ("TenantId", "PolicyKey", "Jurisdiction", "AccountType", "TemplateKey", "VersionNumber");""",
                """CREATE UNIQUE INDEX IF NOT EXISTS "IX_TrustOpsInboxItems_TenantId_TrustOperationalAlertId" ON "TrustOpsInboxItems" ("TenantId", "TrustOperationalAlertId") WHERE "TrustOperationalAlertId" IS NOT NULL;""",
                """CREATE UNIQUE INDEX IF NOT EXISTS "IX_TrustOpsInboxItems_TenantId_TrustCloseForecastSnapshotId" ON "TrustOpsInboxItems" ("TenantId", "TrustCloseForecastSnapshotId") WHERE "TrustCloseForecastSnapshotId" IS NOT NULL;""",
                """CREATE UNIQUE INDEX IF NOT EXISTS "IX_TrustCloseForecastSnapshots_TenantId_TrustAccountId_PeriodEnd" ON "TrustCloseForecastSnapshots" ("TenantId", "TrustAccountId", "PeriodEnd");"""
            ];
        }
    }
}
