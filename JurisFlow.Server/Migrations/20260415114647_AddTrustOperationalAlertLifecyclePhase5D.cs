using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JurisFlow.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddTrustOperationalAlertLifecyclePhase5D : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TrustOperationalAlertEvents",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    TrustOperationalAlertId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    EventType = table.Column<string>(type: "TEXT", maxLength: 48, nullable: false),
                    ActorUserId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrustOperationalAlertEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TrustOperationalAlerts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    AlertKey = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    AlertType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Severity = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    TrustAccountId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    RelatedEntityType = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    RelatedEntityId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    PeriodEnd = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    ActionHint = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    SourceStatus = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    WorkflowStatus = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    AssignedUserId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    OpenedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FirstDetectedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastDetectedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AcknowledgedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    AcknowledgedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    EscalatedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    EscalatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ResolvedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastNotificationAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    NotificationCount = table.Column<int>(type: "INTEGER", nullable: false),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrustOperationalAlerts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TrustOperationalAlertEvents_TenantId",
                table: "TrustOperationalAlertEvents",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TrustOperationalAlertEvents_TenantId_TrustOperationalAlertId_CreatedAt",
                table: "TrustOperationalAlertEvents",
                columns: new[] { "TenantId", "TrustOperationalAlertId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TrustOperationalAlerts_TenantId",
                table: "TrustOperationalAlerts",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TrustOperationalAlerts_TenantId_AlertKey",
                table: "TrustOperationalAlerts",
                columns: new[] { "TenantId", "AlertKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrustOperationalAlerts_TenantId_AlertType_Severity_WorkflowStatus",
                table: "TrustOperationalAlerts",
                columns: new[] { "TenantId", "AlertType", "Severity", "WorkflowStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_TrustOperationalAlerts_TenantId_AssignedUserId_WorkflowStatus_LastDetectedAt",
                table: "TrustOperationalAlerts",
                columns: new[] { "TenantId", "AssignedUserId", "WorkflowStatus", "LastDetectedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TrustOperationalAlerts_TenantId_TrustAccountId_WorkflowStatus_LastDetectedAt",
                table: "TrustOperationalAlerts",
                columns: new[] { "TenantId", "TrustAccountId", "WorkflowStatus", "LastDetectedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TrustOperationalAlertEvents");

            migrationBuilder.DropTable(
                name: "TrustOperationalAlerts");
        }
    }
}
