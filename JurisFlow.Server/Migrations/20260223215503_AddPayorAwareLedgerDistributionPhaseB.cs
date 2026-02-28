using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JurisFlow.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddPayorAwareLedgerDistributionPhaseB : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InvoicePayorAllocationId",
                table: "BillingLedgerEntries",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayorClientId",
                table: "BillingLedgerEntries",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillingLedgerEntries_TenantId_InvoiceId_PayorClientId_PostedAt",
                table: "BillingLedgerEntries",
                columns: new[] { "TenantId", "InvoiceId", "PayorClientId", "PostedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingLedgerEntries_TenantId_InvoicePayorAllocationId_PostedAt",
                table: "BillingLedgerEntries",
                columns: new[] { "TenantId", "InvoicePayorAllocationId", "PostedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BillingLedgerEntries_TenantId_InvoiceId_PayorClientId_PostedAt",
                table: "BillingLedgerEntries");

            migrationBuilder.DropIndex(
                name: "IX_BillingLedgerEntries_TenantId_InvoicePayorAllocationId_PostedAt",
                table: "BillingLedgerEntries");

            migrationBuilder.DropColumn(
                name: "InvoicePayorAllocationId",
                table: "BillingLedgerEntries");

            migrationBuilder.DropColumn(
                name: "PayorClientId",
                table: "BillingLedgerEntries");
        }
    }
}
