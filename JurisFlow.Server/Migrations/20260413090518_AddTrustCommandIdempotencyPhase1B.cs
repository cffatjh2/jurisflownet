using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JurisFlow.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddTrustCommandIdempotencyPhase1B : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TrustCommandDeduplications",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    CommandName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ActorUserId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    RequestFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    ResultEntityType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ResultEntityId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ResultStatusCode = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrustCommandDeduplications", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TrustCommandDeduplications_TenantId",
                table: "TrustCommandDeduplications",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TrustCommandDeduplications_TenantId_CommandName_ActorUserId_IdempotencyKey",
                table: "TrustCommandDeduplications",
                columns: new[] { "TenantId", "CommandName", "ActorUserId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrustCommandDeduplications_TenantId_ResultEntityType_ResultEntityId",
                table: "TrustCommandDeduplications",
                columns: new[] { "TenantId", "ResultEntityType", "ResultEntityId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TrustCommandDeduplications");
        }
    }
}
