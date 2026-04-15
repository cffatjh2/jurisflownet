using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JurisFlow.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddTrustOpsInboxAndBundleIntegrityPhase6CD : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IntegrityStatus",
                table: "TrustComplianceExports",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ParentExportId",
                table: "TrustComplianceExports",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProvenanceJson",
                table: "TrustComplianceExports",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RedactionProfile",
                table: "TrustComplianceExports",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RetentionPolicyTag",
                table: "TrustComplianceExports",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TrustBundleSignatures",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    ManifestExportId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    SignatureAlgorithm = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SignatureDigest = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    IntegrityStatus = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    VerificationStatus = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SignedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    SignedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    VerifiedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RetentionPolicyTag = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    RedactionProfile = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ParentManifestExportId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    EvidenceManifestJson = table.Column<string>(type: "TEXT", nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrustBundleSignatures", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TrustOpsInboxEvents",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    TrustOpsInboxItemId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    EventType = table.Column<string>(type: "TEXT", maxLength: 48, nullable: false),
                    ActorUserId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrustOpsInboxEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TrustOpsInboxItems",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    TrustOperationalAlertId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ItemType = table.Column<string>(type: "TEXT", maxLength: 48, nullable: false),
                    BlockerGroup = table.Column<string>(type: "TEXT", maxLength: 48, nullable: false),
                    Severity = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    TrustAccountId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Jurisdiction = table.Column<string>(type: "TEXT", maxLength: 24, nullable: true),
                    OfficeId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    AssignedUserId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    WorkflowStatus = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    DueAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DeferredUntil = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastActionAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    ActionHint = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    SuggestedExportType = table.Column<string>(type: "TEXT", maxLength: 48, nullable: true),
                    SuggestedRoute = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    OpenedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastDetectedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrustOpsInboxItems", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TrustComplianceExports_TenantId_IntegrityStatus_GeneratedAt",
                table: "TrustComplianceExports",
                columns: new[] { "TenantId", "IntegrityStatus", "GeneratedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TrustComplianceExports_TenantId_ParentExportId_GeneratedAt",
                table: "TrustComplianceExports",
                columns: new[] { "TenantId", "ParentExportId", "GeneratedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TrustBundleSignatures_TenantId",
                table: "TrustBundleSignatures",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TrustBundleSignatures_TenantId_ManifestExportId_SignedAt",
                table: "TrustBundleSignatures",
                columns: new[] { "TenantId", "ManifestExportId", "SignedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TrustOpsInboxEvents_TenantId",
                table: "TrustOpsInboxEvents",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TrustOpsInboxEvents_TenantId_TrustOpsInboxItemId_CreatedAt",
                table: "TrustOpsInboxEvents",
                columns: new[] { "TenantId", "TrustOpsInboxItemId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TrustOpsInboxItems_TenantId",
                table: "TrustOpsInboxItems",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TrustOpsInboxItems_TenantId_AssignedUserId_WorkflowStatus_DueAt",
                table: "TrustOpsInboxItems",
                columns: new[] { "TenantId", "AssignedUserId", "WorkflowStatus", "DueAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TrustOpsInboxItems_TenantId_Jurisdiction_OfficeId_Severity_DueAt",
                table: "TrustOpsInboxItems",
                columns: new[] { "TenantId", "Jurisdiction", "OfficeId", "Severity", "DueAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TrustOpsInboxItems_TenantId_TrustAccountId_BlockerGroup_WorkflowStatus",
                table: "TrustOpsInboxItems",
                columns: new[] { "TenantId", "TrustAccountId", "BlockerGroup", "WorkflowStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_TrustOpsInboxItems_TenantId_TrustOperationalAlertId",
                table: "TrustOpsInboxItems",
                columns: new[] { "TenantId", "TrustOperationalAlertId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TrustBundleSignatures");

            migrationBuilder.DropTable(
                name: "TrustOpsInboxEvents");

            migrationBuilder.DropTable(
                name: "TrustOpsInboxItems");

            migrationBuilder.DropIndex(
                name: "IX_TrustComplianceExports_TenantId_IntegrityStatus_GeneratedAt",
                table: "TrustComplianceExports");

            migrationBuilder.DropIndex(
                name: "IX_TrustComplianceExports_TenantId_ParentExportId_GeneratedAt",
                table: "TrustComplianceExports");

            migrationBuilder.DropColumn(
                name: "IntegrityStatus",
                table: "TrustComplianceExports");

            migrationBuilder.DropColumn(
                name: "ParentExportId",
                table: "TrustComplianceExports");

            migrationBuilder.DropColumn(
                name: "ProvenanceJson",
                table: "TrustComplianceExports");

            migrationBuilder.DropColumn(
                name: "RedactionProfile",
                table: "TrustComplianceExports");

            migrationBuilder.DropColumn(
                name: "RetentionPolicyTag",
                table: "TrustComplianceExports");
        }
    }
}
