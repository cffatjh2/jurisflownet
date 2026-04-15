using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JurisFlow.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddTrustStatementLifecycleAndEvidencePhase5C1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SupersededAt",
                table: "TrustStatementImports",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SupersededBy",
                table: "TrustStatementImports",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SupersededByStatementImportId",
                table: "TrustStatementImports",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AttachmentEvidenceKey",
                table: "TrustOutstandingItems",
                type: "TEXT",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReasonCode",
                table: "TrustOutstandingItems",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrustStatementImports_TenantId_SupersededByStatementImportId",
                table: "TrustStatementImports",
                columns: new[] { "TenantId", "SupersededByStatementImportId" });

            migrationBuilder.CreateIndex(
                name: "IX_TrustStatementImports_TenantId_TrustAccountId_PeriodStart_PeriodEnd_Status",
                table: "TrustStatementImports",
                columns: new[] { "TenantId", "TrustAccountId", "PeriodStart", "PeriodEnd", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TrustOutstandingItems_TenantId_TrustAccountId_ReasonCode_Status",
                table: "TrustOutstandingItems",
                columns: new[] { "TenantId", "TrustAccountId", "ReasonCode", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TrustStatementImports_TenantId_SupersededByStatementImportId",
                table: "TrustStatementImports");

            migrationBuilder.DropIndex(
                name: "IX_TrustStatementImports_TenantId_TrustAccountId_PeriodStart_PeriodEnd_Status",
                table: "TrustStatementImports");

            migrationBuilder.DropIndex(
                name: "IX_TrustOutstandingItems_TenantId_TrustAccountId_ReasonCode_Status",
                table: "TrustOutstandingItems");

            migrationBuilder.DropColumn(
                name: "SupersededAt",
                table: "TrustStatementImports");

            migrationBuilder.DropColumn(
                name: "SupersededBy",
                table: "TrustStatementImports");

            migrationBuilder.DropColumn(
                name: "SupersededByStatementImportId",
                table: "TrustStatementImports");

            migrationBuilder.DropColumn(
                name: "AttachmentEvidenceKey",
                table: "TrustOutstandingItems");

            migrationBuilder.DropColumn(
                name: "ReasonCode",
                table: "TrustOutstandingItems");
        }
    }
}
