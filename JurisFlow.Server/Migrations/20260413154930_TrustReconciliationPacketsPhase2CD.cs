using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JurisFlow.Server.Migrations
{
    /// <inheritdoc />
    public partial class TrustReconciliationPacketsPhase2CD : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TrustOutstandingItems",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    TrustAccountId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    TrustTransactionId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ClientTrustLedgerId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    TrustStatementImportId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    TrustReconciliationPacketId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    PeriodStart = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PeriodEnd = table.Column<DateTime>(type: "TEXT", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ItemType = table.Column<string>(type: "TEXT", maxLength: 48, nullable: false),
                    ImpactDirection = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    CorrelationKey = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Reference = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ResolvedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrustOutstandingItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TrustReconciliationPackets",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    TrustAccountId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    StatementImportId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    PeriodStart = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PeriodEnd = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StatementEndingBalance = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    AdjustedBankBalance = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    JournalBalance = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    ClientLedgerBalance = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    OutstandingDepositsTotal = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    OutstandingChecksTotal = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    OtherAdjustmentsTotal = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    ExceptionCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    PreparedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    PreparedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrustReconciliationPackets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TrustReconciliationSignoffs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    TrustReconciliationPacketId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    SignedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    SignerRole = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    SignedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrustReconciliationSignoffs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TrustStatementImports",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    TrustAccountId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    PeriodStart = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PeriodEnd = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StatementEndingBalance = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    ImportedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    LineCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    ImportedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrustStatementImports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TrustStatementLines",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    TrustStatementImportId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    TrustAccountId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    PostedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EffectiveAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    BalanceAfter = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                    Reference = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    CheckNumber = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    Counterparty = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    MatchStatus = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    MatchedTrustTransactionId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ExternalLineId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrustStatementLines", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TrustOutstandingItems_TenantId",
                table: "TrustOutstandingItems",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TrustOutstandingItems_TenantId_CorrelationKey",
                table: "TrustOutstandingItems",
                columns: new[] { "TenantId", "CorrelationKey" });

            migrationBuilder.CreateIndex(
                name: "IX_TrustOutstandingItems_TenantId_TrustAccountId_PeriodEnd_Status",
                table: "TrustOutstandingItems",
                columns: new[] { "TenantId", "TrustAccountId", "PeriodEnd", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TrustReconciliationPackets_TenantId",
                table: "TrustReconciliationPackets",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TrustReconciliationPackets_TenantId_TrustAccountId_PeriodEnd",
                table: "TrustReconciliationPackets",
                columns: new[] { "TenantId", "TrustAccountId", "PeriodEnd" });

            migrationBuilder.CreateIndex(
                name: "IX_TrustReconciliationSignoffs_TenantId",
                table: "TrustReconciliationSignoffs",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TrustReconciliationSignoffs_TenantId_TrustReconciliationPacketId_SignedAt",
                table: "TrustReconciliationSignoffs",
                columns: new[] { "TenantId", "TrustReconciliationPacketId", "SignedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TrustStatementImports_TenantId",
                table: "TrustStatementImports",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TrustStatementImports_TenantId_TrustAccountId_PeriodEnd",
                table: "TrustStatementImports",
                columns: new[] { "TenantId", "TrustAccountId", "PeriodEnd" });

            migrationBuilder.CreateIndex(
                name: "IX_TrustStatementLines_TenantId",
                table: "TrustStatementLines",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TrustStatementLines_TenantId_TrustAccountId_CheckNumber_Amount",
                table: "TrustStatementLines",
                columns: new[] { "TenantId", "TrustAccountId", "CheckNumber", "Amount" });

            migrationBuilder.CreateIndex(
                name: "IX_TrustStatementLines_TenantId_TrustStatementImportId_PostedAt",
                table: "TrustStatementLines",
                columns: new[] { "TenantId", "TrustStatementImportId", "PostedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TrustOutstandingItems");

            migrationBuilder.DropTable(
                name: "TrustReconciliationPackets");

            migrationBuilder.DropTable(
                name: "TrustReconciliationSignoffs");

            migrationBuilder.DropTable(
                name: "TrustStatementImports");

            migrationBuilder.DropTable(
                name: "TrustStatementLines");
        }
    }
}
