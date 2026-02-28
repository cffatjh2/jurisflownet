using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JurisFlow.Server.Migrations
{
    /// <inheritdoc />
    public partial class HardenPaymentsIdempotencyAndWebhookEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "InvoiceAppliedAmount",
                table: "PaymentTransactions",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<DateTime>(
                name: "InvoiceAppliedAt",
                table: "PaymentTransactions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "InvoiceRefundAppliedAmount",
                table: "PaymentTransactions",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<DateTime>(
                name: "InvoiceRefundAppliedAt",
                table: "PaymentTransactions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "StripeWebhookEvents",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    EventId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    EventType = table.Column<string>(type: "TEXT", maxLength: 96, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    ProcessedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StripeWebhookEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StripeWebhookEvents_TenantId",
                table: "StripeWebhookEvents",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_StripeWebhookEvents_TenantId_CreatedAt",
                table: "StripeWebhookEvents",
                columns: new[] { "TenantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_StripeWebhookEvents_TenantId_EventId",
                table: "StripeWebhookEvents",
                columns: new[] { "TenantId", "EventId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StripeWebhookEvents");

            migrationBuilder.DropColumn(
                name: "InvoiceAppliedAmount",
                table: "PaymentTransactions");

            migrationBuilder.DropColumn(
                name: "InvoiceAppliedAt",
                table: "PaymentTransactions");

            migrationBuilder.DropColumn(
                name: "InvoiceRefundAppliedAmount",
                table: "PaymentTransactions");

            migrationBuilder.DropColumn(
                name: "InvoiceRefundAppliedAt",
                table: "PaymentTransactions");
        }
    }
}
