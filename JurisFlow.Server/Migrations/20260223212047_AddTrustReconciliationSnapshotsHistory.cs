using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JurisFlow.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddTrustReconciliationSnapshotsHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TrustReconciliationSnapshots",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    TrustAccountId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    AsOfUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AccountCount = table.Column<int>(type: "INTEGER", nullable: false),
                    MismatchedAccountCount = table.Column<int>(type: "INTEGER", nullable: false),
                    BankBalance = table.Column<decimal>(type: "TEXT", nullable: false),
                    ClientLedgerTotal = table.Column<decimal>(type: "TEXT", nullable: false),
                    TrustTransactionsNet = table.Column<decimal>(type: "TEXT", nullable: false),
                    BillingTrustLedgerTotal = table.Column<decimal>(type: "TEXT", nullable: false),
                    BankVsClientLedgerDiff = table.Column<decimal>(type: "TEXT", nullable: false),
                    ClientLedgerVsTrustLedgerDiff = table.Column<decimal>(type: "TEXT", nullable: false),
                    BankVsTrustLedgerDiff = table.Column<decimal>(type: "TEXT", nullable: false),
                    DataQuality = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CapturedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrustReconciliationSnapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TrustReconciliationSnapshots_TenantId",
                table: "TrustReconciliationSnapshots",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TrustReconciliationSnapshots_TenantId_AsOfUtc",
                table: "TrustReconciliationSnapshots",
                columns: new[] { "TenantId", "AsOfUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TrustReconciliationSnapshots_TenantId_TrustAccountId_AsOfUtc",
                table: "TrustReconciliationSnapshots",
                columns: new[] { "TenantId", "TrustAccountId", "AsOfUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TrustReconciliationSnapshots");
        }
    }
}
