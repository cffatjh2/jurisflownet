using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JurisFlow.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddTrustPolicyPacksAndReopenVersioningPhase5AB : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TrustMonthCloses_TenantId_TrustAccountId_PeriodEnd",
                table: "TrustMonthCloses");

            migrationBuilder.DropIndex(
                name: "IX_TrustJurisdictionPolicies_TenantId_PolicyKey_Jurisdiction",
                table: "TrustJurisdictionPolicies");

            migrationBuilder.AddColumn<string>(
                name: "SupersedeReason",
                table: "TrustReconciliationPackets",
                type: "TEXT",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SupersededAt",
                table: "TrustReconciliationPackets",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SupersededBy",
                table: "TrustReconciliationPackets",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SupersededByPacketId",
                table: "TrustReconciliationPackets",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VersionNumber",
                table: "TrustReconciliationPackets",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsCanonical",
                table: "TrustMonthCloses",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ReopenReason",
                table: "TrustMonthCloses",
                type: "TEXT",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReopenedAt",
                table: "TrustMonthCloses",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReopenedBy",
                table: "TrustMonthCloses",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReopenedFromMonthCloseId",
                table: "TrustMonthCloses",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SupersedeReason",
                table: "TrustMonthCloses",
                type: "TEXT",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SupersededAt",
                table: "TrustMonthCloses",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SupersededBy",
                table: "TrustMonthCloses",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SupersededByMonthCloseId",
                table: "TrustMonthCloses",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VersionNumber",
                table: "TrustMonthCloses",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "AccountType",
                table: "TrustJurisdictionPolicies",
                type: "TEXT",
                maxLength: 24,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsSystemBaseline",
                table: "TrustJurisdictionPolicies",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RequireMonthlyThreeWayReconciliation",
                table: "TrustJurisdictionPolicies",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RequireResponsibleLawyerAssignment",
                table: "TrustJurisdictionPolicies",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "RetentionPeriodMonths",
                table: "TrustJurisdictionPolicies",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "VersionNumber",
                table: "TrustJurisdictionPolicies",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_TrustReconciliationPackets_TenantId_SupersededByPacketId",
                table: "TrustReconciliationPackets",
                columns: new[] { "TenantId", "SupersededByPacketId" });

            migrationBuilder.CreateIndex(
                name: "IX_TrustReconciliationPackets_TenantId_TrustAccountId_PeriodEnd_VersionNumber",
                table: "TrustReconciliationPackets",
                columns: new[] { "TenantId", "TrustAccountId", "PeriodEnd", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrustMonthCloses_TenantId_SupersededByMonthCloseId",
                table: "TrustMonthCloses",
                columns: new[] { "TenantId", "SupersededByMonthCloseId" });

            migrationBuilder.CreateIndex(
                name: "IX_TrustMonthCloses_TenantId_TrustAccountId_PeriodEnd_IsCanonical",
                table: "TrustMonthCloses",
                columns: new[] { "TenantId", "TrustAccountId", "PeriodEnd", "IsCanonical" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrustMonthCloses_TenantId_TrustAccountId_PeriodEnd_VersionNumber",
                table: "TrustMonthCloses",
                columns: new[] { "TenantId", "TrustAccountId", "PeriodEnd", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrustJurisdictionPolicies_TenantId_PolicyKey_Jurisdiction_AccountType_VersionNumber",
                table: "TrustJurisdictionPolicies",
                columns: new[] { "TenantId", "PolicyKey", "Jurisdiction", "AccountType", "VersionNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TrustReconciliationPackets_TenantId_SupersededByPacketId",
                table: "TrustReconciliationPackets");

            migrationBuilder.DropIndex(
                name: "IX_TrustReconciliationPackets_TenantId_TrustAccountId_PeriodEnd_VersionNumber",
                table: "TrustReconciliationPackets");

            migrationBuilder.DropIndex(
                name: "IX_TrustMonthCloses_TenantId_SupersededByMonthCloseId",
                table: "TrustMonthCloses");

            migrationBuilder.DropIndex(
                name: "IX_TrustMonthCloses_TenantId_TrustAccountId_PeriodEnd_IsCanonical",
                table: "TrustMonthCloses");

            migrationBuilder.DropIndex(
                name: "IX_TrustMonthCloses_TenantId_TrustAccountId_PeriodEnd_VersionNumber",
                table: "TrustMonthCloses");

            migrationBuilder.DropIndex(
                name: "IX_TrustJurisdictionPolicies_TenantId_PolicyKey_Jurisdiction_AccountType_VersionNumber",
                table: "TrustJurisdictionPolicies");

            migrationBuilder.DropColumn(
                name: "SupersedeReason",
                table: "TrustReconciliationPackets");

            migrationBuilder.DropColumn(
                name: "SupersededAt",
                table: "TrustReconciliationPackets");

            migrationBuilder.DropColumn(
                name: "SupersededBy",
                table: "TrustReconciliationPackets");

            migrationBuilder.DropColumn(
                name: "SupersededByPacketId",
                table: "TrustReconciliationPackets");

            migrationBuilder.DropColumn(
                name: "VersionNumber",
                table: "TrustReconciliationPackets");

            migrationBuilder.DropColumn(
                name: "IsCanonical",
                table: "TrustMonthCloses");

            migrationBuilder.DropColumn(
                name: "ReopenReason",
                table: "TrustMonthCloses");

            migrationBuilder.DropColumn(
                name: "ReopenedAt",
                table: "TrustMonthCloses");

            migrationBuilder.DropColumn(
                name: "ReopenedBy",
                table: "TrustMonthCloses");

            migrationBuilder.DropColumn(
                name: "ReopenedFromMonthCloseId",
                table: "TrustMonthCloses");

            migrationBuilder.DropColumn(
                name: "SupersedeReason",
                table: "TrustMonthCloses");

            migrationBuilder.DropColumn(
                name: "SupersededAt",
                table: "TrustMonthCloses");

            migrationBuilder.DropColumn(
                name: "SupersededBy",
                table: "TrustMonthCloses");

            migrationBuilder.DropColumn(
                name: "SupersededByMonthCloseId",
                table: "TrustMonthCloses");

            migrationBuilder.DropColumn(
                name: "VersionNumber",
                table: "TrustMonthCloses");

            migrationBuilder.DropColumn(
                name: "AccountType",
                table: "TrustJurisdictionPolicies");

            migrationBuilder.DropColumn(
                name: "IsSystemBaseline",
                table: "TrustJurisdictionPolicies");

            migrationBuilder.DropColumn(
                name: "RequireMonthlyThreeWayReconciliation",
                table: "TrustJurisdictionPolicies");

            migrationBuilder.DropColumn(
                name: "RequireResponsibleLawyerAssignment",
                table: "TrustJurisdictionPolicies");

            migrationBuilder.DropColumn(
                name: "RetentionPeriodMonths",
                table: "TrustJurisdictionPolicies");

            migrationBuilder.DropColumn(
                name: "VersionNumber",
                table: "TrustJurisdictionPolicies");

            migrationBuilder.CreateIndex(
                name: "IX_TrustMonthCloses_TenantId_TrustAccountId_PeriodEnd",
                table: "TrustMonthCloses",
                columns: new[] { "TenantId", "TrustAccountId", "PeriodEnd" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrustJurisdictionPolicies_TenantId_PolicyKey_Jurisdiction",
                table: "TrustJurisdictionPolicies",
                columns: new[] { "TenantId", "PolicyKey", "Jurisdiction" },
                unique: true);
        }
    }
}
