using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JurisFlow.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddTrustComplianceExportsPhase3D : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ApprovalStatus",
                table: "TrustTransactions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DisbursementClass",
                table: "TrustTransactions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PolicyDecisionJson",
                table: "TrustTransactions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AccountType",
                table: "TrustBankAccounts",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AllowedSignatoriesJson",
                table: "TrustBankAccounts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankReferenceMetadataJson",
                table: "TrustBankAccounts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "JurisdictionPolicyKey",
                table: "TrustBankAccounts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "OverdraftNotificationEnabled",
                table: "TrustBankAccounts",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ResponsibleLawyerUserId",
                table: "TrustBankAccounts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StatementCadence",
                table: "TrustBankAccounts",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TrustTransactionId",
                table: "BillingPaymentAllocations",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TrustApprovalDecisions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    TrustTransactionId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    TrustApprovalRequirementId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ActorUserId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ActorRole = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    DecisionType = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrustApprovalDecisions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TrustApprovalOverrides",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    TrustTransactionId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    TrustApprovalRequirementId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ActorUserId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ActorRole = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrustApprovalOverrides", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TrustApprovalRequirements",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    TrustTransactionId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    RequirementType = table.Column<string>(type: "TEXT", maxLength: 48, nullable: false),
                    RequiredCount = table.Column<int>(type: "INTEGER", nullable: false),
                    SatisfiedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    PolicyKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrustApprovalRequirements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TrustComplianceExports",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    ExportType = table.Column<string>(type: "TEXT", maxLength: 48, nullable: false),
                    Format = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    TrustAccountId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ClientTrustLedgerId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    TrustMonthCloseId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    TrustReconciliationPacketId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    SummaryJson = table.Column<string>(type: "TEXT", nullable: true),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: true),
                    GeneratedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    GeneratedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrustComplianceExports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TrustJurisdictionPolicies",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    PolicyKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Jurisdiction = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    RequireMakerChecker = table.Column<bool>(type: "INTEGER", nullable: false),
                    RequireOverrideReason = table.Column<bool>(type: "INTEGER", nullable: false),
                    DualApprovalThreshold = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    ResponsibleLawyerApprovalThreshold = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    SignatoryApprovalThreshold = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    MonthlyCloseCadenceDays = table.Column<int>(type: "INTEGER", nullable: false),
                    ExceptionAgingSlaHours = table.Column<int>(type: "INTEGER", nullable: false),
                    DisbursementClassesRequiringSignatoryJson = table.Column<string>(type: "TEXT", nullable: true),
                    OperationalApproverRolesJson = table.Column<string>(type: "TEXT", nullable: true),
                    OverrideApproverRolesJson = table.Column<string>(type: "TEXT", nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrustJurisdictionPolicies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TrustMonthCloses",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    TrustAccountId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    PolicyKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PeriodStart = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PeriodEnd = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ReconciliationPacketId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    OpenExceptionCount = table.Column<int>(type: "INTEGER", nullable: false),
                    PreparedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    PreparedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ReviewerSignedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ReviewerSignedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ResponsibleLawyerSignedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ResponsibleLawyerSignedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SummaryJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrustMonthCloses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TrustMonthCloseSteps",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    TrustMonthCloseId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    StepKey = table.Column<string>(type: "TEXT", maxLength: 48, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    CompletedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrustMonthCloseSteps", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BillingPaymentAllocations_TenantId_TrustTransactionId_Status",
                table: "BillingPaymentAllocations",
                columns: new[] { "TenantId", "TrustTransactionId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TrustApprovalDecisions_TenantId",
                table: "TrustApprovalDecisions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TrustApprovalDecisions_TenantId_TrustApprovalRequirementId_ActorUserId",
                table: "TrustApprovalDecisions",
                columns: new[] { "TenantId", "TrustApprovalRequirementId", "ActorUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrustApprovalOverrides_TenantId",
                table: "TrustApprovalOverrides",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TrustApprovalOverrides_TenantId_TrustTransactionId_CreatedAt",
                table: "TrustApprovalOverrides",
                columns: new[] { "TenantId", "TrustTransactionId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TrustApprovalRequirements_TenantId",
                table: "TrustApprovalRequirements",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TrustApprovalRequirements_TenantId_TrustTransactionId_RequirementType_Status",
                table: "TrustApprovalRequirements",
                columns: new[] { "TenantId", "TrustTransactionId", "RequirementType", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TrustComplianceExports_TenantId",
                table: "TrustComplianceExports",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TrustComplianceExports_TenantId_ExportType_GeneratedAt",
                table: "TrustComplianceExports",
                columns: new[] { "TenantId", "ExportType", "GeneratedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TrustComplianceExports_TenantId_GeneratedAt",
                table: "TrustComplianceExports",
                columns: new[] { "TenantId", "GeneratedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TrustComplianceExports_TenantId_TrustAccountId_GeneratedAt",
                table: "TrustComplianceExports",
                columns: new[] { "TenantId", "TrustAccountId", "GeneratedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TrustJurisdictionPolicies_TenantId",
                table: "TrustJurisdictionPolicies",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TrustJurisdictionPolicies_TenantId_PolicyKey_Jurisdiction",
                table: "TrustJurisdictionPolicies",
                columns: new[] { "TenantId", "PolicyKey", "Jurisdiction" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrustMonthCloses_TenantId",
                table: "TrustMonthCloses",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TrustMonthCloses_TenantId_TrustAccountId_PeriodEnd",
                table: "TrustMonthCloses",
                columns: new[] { "TenantId", "TrustAccountId", "PeriodEnd" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrustMonthCloseSteps_TenantId",
                table: "TrustMonthCloseSteps",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TrustMonthCloseSteps_TenantId_TrustMonthCloseId_StepKey",
                table: "TrustMonthCloseSteps",
                columns: new[] { "TenantId", "TrustMonthCloseId", "StepKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TrustApprovalDecisions");

            migrationBuilder.DropTable(
                name: "TrustApprovalOverrides");

            migrationBuilder.DropTable(
                name: "TrustApprovalRequirements");

            migrationBuilder.DropTable(
                name: "TrustComplianceExports");

            migrationBuilder.DropTable(
                name: "TrustJurisdictionPolicies");

            migrationBuilder.DropTable(
                name: "TrustMonthCloses");

            migrationBuilder.DropTable(
                name: "TrustMonthCloseSteps");

            migrationBuilder.DropIndex(
                name: "IX_BillingPaymentAllocations_TenantId_TrustTransactionId_Status",
                table: "BillingPaymentAllocations");

            migrationBuilder.DropColumn(
                name: "ApprovalStatus",
                table: "TrustTransactions");

            migrationBuilder.DropColumn(
                name: "DisbursementClass",
                table: "TrustTransactions");

            migrationBuilder.DropColumn(
                name: "PolicyDecisionJson",
                table: "TrustTransactions");

            migrationBuilder.DropColumn(
                name: "AccountType",
                table: "TrustBankAccounts");

            migrationBuilder.DropColumn(
                name: "AllowedSignatoriesJson",
                table: "TrustBankAccounts");

            migrationBuilder.DropColumn(
                name: "BankReferenceMetadataJson",
                table: "TrustBankAccounts");

            migrationBuilder.DropColumn(
                name: "JurisdictionPolicyKey",
                table: "TrustBankAccounts");

            migrationBuilder.DropColumn(
                name: "OverdraftNotificationEnabled",
                table: "TrustBankAccounts");

            migrationBuilder.DropColumn(
                name: "ResponsibleLawyerUserId",
                table: "TrustBankAccounts");

            migrationBuilder.DropColumn(
                name: "StatementCadence",
                table: "TrustBankAccounts");

            migrationBuilder.DropColumn(
                name: "TrustTransactionId",
                table: "BillingPaymentAllocations");
        }
    }
}
