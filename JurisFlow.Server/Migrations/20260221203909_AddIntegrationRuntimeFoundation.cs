using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JurisFlow.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddIntegrationRuntimeFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeltaToken",
                table: "IntegrationConnections",
                type: "TEXT",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SyncCursor",
                table: "IntegrationConnections",
                type: "TEXT",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "IntegrationRuns",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    ConnectionId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ProviderKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Trigger = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    AttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxAttempts = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CursorBefore = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    CursorAfter = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    DeltaTokenBefore = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    DeltaTokenAfter = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    ErrorCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    IsDeadLetter = table.Column<bool>(type: "INTEGER", nullable: false),
                    ResultJson = table.Column<string>(type: "TEXT", nullable: true),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntegrationRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IntegrationSecrets",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    ConnectionId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ProviderKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    SecretJson = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntegrationSecrets", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationRuns_TenantId",
                table: "IntegrationRuns",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationRuns_TenantId_ConnectionId_CreatedAt",
                table: "IntegrationRuns",
                columns: new[] { "TenantId", "ConnectionId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationRuns_TenantId_ConnectionId_IdempotencyKey",
                table: "IntegrationRuns",
                columns: new[] { "TenantId", "ConnectionId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationRuns_TenantId_Status_CreatedAt",
                table: "IntegrationRuns",
                columns: new[] { "TenantId", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationSecrets_ProviderKey",
                table: "IntegrationSecrets",
                column: "ProviderKey");

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationSecrets_TenantId",
                table: "IntegrationSecrets",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationSecrets_TenantId_ConnectionId",
                table: "IntegrationSecrets",
                columns: new[] { "TenantId", "ConnectionId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IntegrationRuns");

            migrationBuilder.DropTable(
                name: "IntegrationSecrets");

            migrationBuilder.DropColumn(
                name: "DeltaToken",
                table: "IntegrationConnections");

            migrationBuilder.DropColumn(
                name: "SyncCursor",
                table: "IntegrationConnections");
        }
    }
}
