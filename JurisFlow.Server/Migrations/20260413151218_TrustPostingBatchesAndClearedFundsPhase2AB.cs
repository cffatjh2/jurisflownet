using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JurisFlow.Server.Migrations
{
    /// <inheritdoc />
    public partial class TrustPostingBatchesAndClearedFundsPhase2AB : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ClearedAt",
                table: "TrustTransactions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClearingStatus",
                table: "TrustTransactions",
                type: "TEXT",
                nullable: false,
                defaultValue: "not_applicable");

            migrationBuilder.AddColumn<string>(
                name: "PostingBatchId",
                table: "TrustTransactions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrimaryJournalEntryId",
                table: "TrustTransactions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReturnReason",
                table: "TrustTransactions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReturnedAt",
                table: "TrustTransactions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AvailabilityClass",
                table: "TrustJournalEntries",
                type: "TEXT",
                maxLength: 24,
                nullable: false,
                defaultValue: "cleared");

            migrationBuilder.AddColumn<string>(
                name: "PostingBatchId",
                table: "TrustJournalEntries",
                type: "TEXT",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "AvailableDisbursementCapacity",
                table: "TrustBankAccounts",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ClearedBalance",
                table: "TrustBankAccounts",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "UnclearedBalance",
                table: "TrustBankAccounts",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "AvailableToDisburse",
                table: "ClientTrustLedgers",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ClearedBalance",
                table: "ClientTrustLedgers",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "HoldAmount",
                table: "ClientTrustLedgers",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "UnclearedBalance",
                table: "ClientTrustLedgers",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "TrustPostingBatches",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    TrustTransactionId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    TrustAccountId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    BatchType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ParentPostingBatchId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    JournalEntryCount = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    EffectiveAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrustPostingBatches", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TrustJournalEntries_TenantId_ClientTrustLedgerId_AvailabilityClass_EffectiveAt",
                table: "TrustJournalEntries",
                columns: new[] { "TenantId", "ClientTrustLedgerId", "AvailabilityClass", "EffectiveAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TrustJournalEntries_TenantId_PostingBatchId_EffectiveAt",
                table: "TrustJournalEntries",
                columns: new[] { "TenantId", "PostingBatchId", "EffectiveAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TrustJournalEntries_TenantId_TrustAccountId_AvailabilityClass_EffectiveAt",
                table: "TrustJournalEntries",
                columns: new[] { "TenantId", "TrustAccountId", "AvailabilityClass", "EffectiveAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TrustJournalEntries_TenantId_TrustTransactionId_EntryKind_EffectiveAt",
                table: "TrustJournalEntries",
                columns: new[] { "TenantId", "TrustTransactionId", "EntryKind", "EffectiveAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TrustPostingBatches_TenantId",
                table: "TrustPostingBatches",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TrustPostingBatches_TenantId_ParentPostingBatchId",
                table: "TrustPostingBatches",
                columns: new[] { "TenantId", "ParentPostingBatchId" });

            migrationBuilder.CreateIndex(
                name: "IX_TrustPostingBatches_TenantId_TrustAccountId_EffectiveAt",
                table: "TrustPostingBatches",
                columns: new[] { "TenantId", "TrustAccountId", "EffectiveAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TrustPostingBatches_TenantId_TrustTransactionId_EffectiveAt",
                table: "TrustPostingBatches",
                columns: new[] { "TenantId", "TrustTransactionId", "EffectiveAt" });

            migrationBuilder.Sql("""
                UPDATE "TrustBankAccounts"
                SET "ClearedBalance" = ROUND(COALESCE("CurrentBalance", 0), 2),
                    "AvailableDisbursementCapacity" = ROUND(COALESCE("CurrentBalance", 0), 2)
                WHERE ROUND(COALESCE("CurrentBalance", 0), 2) <> 0
                  AND ROUND(COALESCE("ClearedBalance", 0), 2) = 0;
                """);

            migrationBuilder.Sql("""
                UPDATE "ClientTrustLedgers"
                SET "ClearedBalance" = ROUND(COALESCE("RunningBalance", 0), 2),
                    "AvailableToDisburse" = ROUND(COALESCE("RunningBalance", 0), 2)
                WHERE ROUND(COALESCE("RunningBalance", 0), 2) <> 0
                  AND ROUND(COALESCE("ClearedBalance", 0), 2) = 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TrustPostingBatches");

            migrationBuilder.DropIndex(
                name: "IX_TrustJournalEntries_TenantId_ClientTrustLedgerId_AvailabilityClass_EffectiveAt",
                table: "TrustJournalEntries");

            migrationBuilder.DropIndex(
                name: "IX_TrustJournalEntries_TenantId_PostingBatchId_EffectiveAt",
                table: "TrustJournalEntries");

            migrationBuilder.DropIndex(
                name: "IX_TrustJournalEntries_TenantId_TrustAccountId_AvailabilityClass_EffectiveAt",
                table: "TrustJournalEntries");

            migrationBuilder.DropIndex(
                name: "IX_TrustJournalEntries_TenantId_TrustTransactionId_EntryKind_EffectiveAt",
                table: "TrustJournalEntries");

            migrationBuilder.DropColumn(
                name: "ClearedAt",
                table: "TrustTransactions");

            migrationBuilder.DropColumn(
                name: "ClearingStatus",
                table: "TrustTransactions");

            migrationBuilder.DropColumn(
                name: "PostingBatchId",
                table: "TrustTransactions");

            migrationBuilder.DropColumn(
                name: "PrimaryJournalEntryId",
                table: "TrustTransactions");

            migrationBuilder.DropColumn(
                name: "ReturnReason",
                table: "TrustTransactions");

            migrationBuilder.DropColumn(
                name: "ReturnedAt",
                table: "TrustTransactions");

            migrationBuilder.DropColumn(
                name: "AvailabilityClass",
                table: "TrustJournalEntries");

            migrationBuilder.DropColumn(
                name: "PostingBatchId",
                table: "TrustJournalEntries");

            migrationBuilder.DropColumn(
                name: "AvailableDisbursementCapacity",
                table: "TrustBankAccounts");

            migrationBuilder.DropColumn(
                name: "ClearedBalance",
                table: "TrustBankAccounts");

            migrationBuilder.DropColumn(
                name: "UnclearedBalance",
                table: "TrustBankAccounts");

            migrationBuilder.DropColumn(
                name: "AvailableToDisburse",
                table: "ClientTrustLedgers");

            migrationBuilder.DropColumn(
                name: "ClearedBalance",
                table: "ClientTrustLedgers");

            migrationBuilder.DropColumn(
                name: "HoldAmount",
                table: "ClientTrustLedgers");

            migrationBuilder.DropColumn(
                name: "UnclearedBalance",
                table: "ClientTrustLedgers");
        }
    }
}
