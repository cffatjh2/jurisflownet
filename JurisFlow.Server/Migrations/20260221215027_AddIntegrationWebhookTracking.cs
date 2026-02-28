using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JurisFlow.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddIntegrationWebhookTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastWebhookAt",
                table: "IntegrationConnections",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastWebhookEventId",
                table: "IntegrationConnections",
                type: "TEXT",
                maxLength: 160,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationConnections_TenantId_SyncEnabled_LastWebhookAt",
                table: "IntegrationConnections",
                columns: new[] { "TenantId", "SyncEnabled", "LastWebhookAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_IntegrationConnections_TenantId_SyncEnabled_LastWebhookAt",
                table: "IntegrationConnections");

            migrationBuilder.DropColumn(
                name: "LastWebhookAt",
                table: "IntegrationConnections");

            migrationBuilder.DropColumn(
                name: "LastWebhookEventId",
                table: "IntegrationConnections");
        }
    }
}
