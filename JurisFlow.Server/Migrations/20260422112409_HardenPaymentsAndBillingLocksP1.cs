using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JurisFlow.Server.Migrations
{
    /// <inheritdoc />
    public partial class HardenPaymentsAndBillingLocksP1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BillingLocks_PeriodStart_PeriodEnd",
                table: "BillingLocks");

            if (ActiveProvider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
            {
                migrationBuilder.Sql(
                    """
                    ALTER TABLE "BillingLocks"
                    ALTER COLUMN "PeriodStart" TYPE date
                    USING NULLIF(TRIM("PeriodStart"), '')::date;
                    """);

                migrationBuilder.Sql(
                    """
                    ALTER TABLE "BillingLocks"
                    ALTER COLUMN "PeriodEnd" TYPE date
                    USING NULLIF(TRIM("PeriodEnd"), '')::date;
                    """);
            }
            else
            {
                migrationBuilder.AlterColumn<DateTime>(
                    name: "PeriodStart",
                    table: "BillingLocks",
                    type: "date",
                    nullable: false,
                    oldClrType: typeof(string),
                    oldType: "TEXT");

                migrationBuilder.AlterColumn<DateTime>(
                    name: "PeriodEnd",
                    table: "BillingLocks",
                    type: "date",
                    nullable: false,
                    oldClrType: typeof(string),
                    oldType: "TEXT");
            }

            migrationBuilder.CreateTable(
                name: "PaymentCommandDeduplications",
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
                    CorrelationId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ErrorCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ResponsePayloadJson = table.Column<string>(type: "TEXT", nullable: true),
                    ResponseContentType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentCommandDeduplications", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BillingLocks_TenantId_PeriodStart_PeriodEnd",
                table: "BillingLocks",
                columns: new[] { "TenantId", "PeriodStart", "PeriodEnd" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentCommandDeduplications_TenantId",
                table: "PaymentCommandDeduplications",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentCommandDeduplications_TenantId_CommandName_IdempotencyKey",
                table: "PaymentCommandDeduplications",
                columns: new[] { "TenantId", "CommandName", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentCommandDeduplications_TenantId_ResultEntityType_ResultEntityId",
                table: "PaymentCommandDeduplications",
                columns: new[] { "TenantId", "ResultEntityType", "ResultEntityId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentCommandDeduplications");

            migrationBuilder.DropIndex(
                name: "IX_BillingLocks_TenantId_PeriodStart_PeriodEnd",
                table: "BillingLocks");

            if (ActiveProvider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
            {
                migrationBuilder.Sql(
                    """
                    ALTER TABLE "BillingLocks"
                    ALTER COLUMN "PeriodStart" TYPE text
                    USING TO_CHAR("PeriodStart", 'YYYY-MM-DD');
                    """);

                migrationBuilder.Sql(
                    """
                    ALTER TABLE "BillingLocks"
                    ALTER COLUMN "PeriodEnd" TYPE text
                    USING TO_CHAR("PeriodEnd", 'YYYY-MM-DD');
                    """);
            }
            else
            {
                migrationBuilder.AlterColumn<string>(
                    name: "PeriodStart",
                    table: "BillingLocks",
                    type: "TEXT",
                    nullable: false,
                    oldClrType: typeof(DateTime),
                    oldType: "date");

                migrationBuilder.AlterColumn<string>(
                    name: "PeriodEnd",
                    table: "BillingLocks",
                    type: "TEXT",
                    nullable: false,
                    oldClrType: typeof(DateTime),
                    oldType: "date");
            }

            migrationBuilder.CreateIndex(
                name: "IX_BillingLocks_PeriodStart_PeriodEnd",
                table: "BillingLocks",
                columns: new[] { "PeriodStart", "PeriodEnd" });
        }
    }
}
