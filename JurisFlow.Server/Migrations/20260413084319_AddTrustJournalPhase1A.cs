using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JurisFlow.Server.Migrations
{
    public partial class AddTrustJournalPhase1A : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RowVersion",
                table: "TrustTransactions",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RowVersion",
                table: "TrustBankAccounts",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RowVersion",
                table: "ClientTrustLedgers",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "TrustJournalEntries",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    TrustTransactionId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    TrustAccountId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ClientTrustLedgerId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    MatterId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    EntryKind = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    OperationType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", nullable: false),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    ReversalOfTrustJournalEntryId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    CorrelationKey = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    EffectiveAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrustJournalEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TrustJournalEntries_TenantId",
                table: "TrustJournalEntries",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TrustJournalEntries_TenantId_ClientTrustLedgerId_CreatedAt",
                table: "TrustJournalEntries",
                columns: new[] { "TenantId", "ClientTrustLedgerId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TrustJournalEntries_TenantId_CorrelationKey",
                table: "TrustJournalEntries",
                columns: new[] { "TenantId", "CorrelationKey" });

            migrationBuilder.CreateIndex(
                name: "IX_TrustJournalEntries_TenantId_ReversalOfTrustJournalEntryId",
                table: "TrustJournalEntries",
                columns: new[] { "TenantId", "ReversalOfTrustJournalEntryId" });

            migrationBuilder.CreateIndex(
                name: "IX_TrustJournalEntries_TenantId_TrustAccountId_CreatedAt",
                table: "TrustJournalEntries",
                columns: new[] { "TenantId", "TrustAccountId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TrustJournalEntries_TenantId_TrustTransactionId_CreatedAt",
                table: "TrustJournalEntries",
                columns: new[] { "TenantId", "TrustTransactionId", "CreatedAt" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TrustJournalEntries");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "TrustTransactions");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "TrustBankAccounts");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "ClientTrustLedgers");
        }
    }
}
