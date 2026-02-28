using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JurisFlow.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentPayorTargetAndRailsPhaseC : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InvoicePayorAllocationId",
                table: "PaymentTransactions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentRail",
                table: "PaymentTransactions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayorClientId",
                table: "PaymentTransactions",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InvoicePayorAllocationId",
                table: "PaymentTransactions");

            migrationBuilder.DropColumn(
                name: "PaymentRail",
                table: "PaymentTransactions");

            migrationBuilder.DropColumn(
                name: "PayorClientId",
                table: "PaymentTransactions");
        }
    }
}
