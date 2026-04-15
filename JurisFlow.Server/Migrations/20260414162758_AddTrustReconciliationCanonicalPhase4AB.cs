using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JurisFlow.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddTrustReconciliationCanonicalPhase4AB : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "MatchConfidence",
                table: "TrustStatementLines",
                type: "TEXT",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MatchMethod",
                table: "TrustStatementLines",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "none");

            migrationBuilder.AddColumn<string>(
                name: "MatchNotes",
                table: "TrustStatementLines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "MatchedAt",
                table: "TrustStatementLines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MatchedBy",
                table: "TrustStatementLines",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "TrustStatementLines",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<bool>(
                name: "IsCanonical",
                table: "TrustReconciliationPackets",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "MatchedStatementLineCount",
                table: "TrustReconciliationPackets",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "UnmatchedStatementLineCount",
                table: "TrustReconciliationPackets",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "TrustStatementLineId",
                table: "TrustOutstandingItems",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrustStatementLines_TenantId_TrustStatementImportId_MatchStatus",
                table: "TrustStatementLines",
                columns: new[] { "TenantId", "TrustStatementImportId", "MatchStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_TrustReconciliationPackets_TenantId_TrustAccountId_PeriodEnd_IsCanonical",
                table: "TrustReconciliationPackets",
                columns: new[] { "TenantId", "TrustAccountId", "PeriodEnd", "IsCanonical" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TrustStatementLines_TenantId_TrustStatementImportId_MatchStatus",
                table: "TrustStatementLines");

            migrationBuilder.DropIndex(
                name: "IX_TrustReconciliationPackets_TenantId_TrustAccountId_PeriodEnd_IsCanonical",
                table: "TrustReconciliationPackets");

            migrationBuilder.DropColumn(
                name: "MatchConfidence",
                table: "TrustStatementLines");

            migrationBuilder.DropColumn(
                name: "MatchMethod",
                table: "TrustStatementLines");

            migrationBuilder.DropColumn(
                name: "MatchNotes",
                table: "TrustStatementLines");

            migrationBuilder.DropColumn(
                name: "MatchedAt",
                table: "TrustStatementLines");

            migrationBuilder.DropColumn(
                name: "MatchedBy",
                table: "TrustStatementLines");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "TrustStatementLines");

            migrationBuilder.DropColumn(
                name: "IsCanonical",
                table: "TrustReconciliationPackets");

            migrationBuilder.DropColumn(
                name: "MatchedStatementLineCount",
                table: "TrustReconciliationPackets");

            migrationBuilder.DropColumn(
                name: "UnmatchedStatementLineCount",
                table: "TrustReconciliationPackets");

            migrationBuilder.DropColumn(
                name: "TrustStatementLineId",
                table: "TrustOutstandingItems");
        }
    }
}
