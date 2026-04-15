using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JurisFlow.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddTrustCloseAutomationForecastsPhase6E : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TrustCloseForecastSnapshotId",
                table: "TrustOpsInboxItems",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TrustCloseForecastSnapshots",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    TrustAccountId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Jurisdiction = table.Column<string>(type: "TEXT", maxLength: 24, nullable: true),
                    OfficeId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    StatementCadence = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    PeriodStart = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PeriodEnd = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CloseDueAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ReadinessStatus = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    Severity = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    MissingStatementImport = table.Column<bool>(type: "INTEGER", nullable: false),
                    LatestStatementImportId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    StatementImportedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    HasCanonicalPacket = table.Column<bool>(type: "INTEGER", nullable: false),
                    CanonicalPacketId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    PacketStatus = table.Column<string>(type: "TEXT", maxLength: 24, nullable: true),
                    HasCanonicalMonthClose = table.Column<bool>(type: "INTEGER", nullable: false),
                    CanonicalMonthCloseId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    MonthCloseStatus = table.Column<string>(type: "TEXT", maxLength: 24, nullable: true),
                    OpenExceptionCount = table.Column<int>(type: "INTEGER", nullable: false),
                    OutstandingItemCount = table.Column<int>(type: "INTEGER", nullable: false),
                    MissingRequiredSectionCount = table.Column<int>(type: "INTEGER", nullable: false),
                    MissingAttestationCount = table.Column<int>(type: "INTEGER", nullable: false),
                    UnclearedBalance = table.Column<decimal>(type: "TEXT", nullable: false),
                    UnclearedEntryCount = table.Column<int>(type: "INTEGER", nullable: false),
                    OldestOutstandingAgeDays = table.Column<int>(type: "INTEGER", nullable: true),
                    OldestUnclearedAgeDays = table.Column<int>(type: "INTEGER", nullable: true),
                    DraftBundleEligible = table.Column<bool>(type: "INTEGER", nullable: false),
                    DraftBundleManifestExportId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    DraftBundleGeneratedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RecommendedAction = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    ReminderCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LastReminderAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    NextReminderAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    EscalatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastAutomationRunAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastAutomationRunBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    SummaryJson = table.Column<string>(type: "TEXT", nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrustCloseForecastSnapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TrustOpsInboxItems_TenantId_TrustCloseForecastSnapshotId",
                table: "TrustOpsInboxItems",
                columns: new[] { "TenantId", "TrustCloseForecastSnapshotId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrustCloseForecastSnapshots_TenantId",
                table: "TrustCloseForecastSnapshots",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TrustCloseForecastSnapshots_TenantId_Jurisdiction_OfficeId_CloseDueAt",
                table: "TrustCloseForecastSnapshots",
                columns: new[] { "TenantId", "Jurisdiction", "OfficeId", "CloseDueAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TrustCloseForecastSnapshots_TenantId_ReadinessStatus_CloseDueAt",
                table: "TrustCloseForecastSnapshots",
                columns: new[] { "TenantId", "ReadinessStatus", "CloseDueAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TrustCloseForecastSnapshots_TenantId_Severity_NextReminderAt",
                table: "TrustCloseForecastSnapshots",
                columns: new[] { "TenantId", "Severity", "NextReminderAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TrustCloseForecastSnapshots_TenantId_TrustAccountId_PeriodEnd",
                table: "TrustCloseForecastSnapshots",
                columns: new[] { "TenantId", "TrustAccountId", "PeriodEnd" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TrustCloseForecastSnapshots");

            migrationBuilder.DropIndex(
                name: "IX_TrustOpsInboxItems_TenantId_TrustCloseForecastSnapshotId",
                table: "TrustOpsInboxItems");

            migrationBuilder.DropColumn(
                name: "TrustCloseForecastSnapshotId",
                table: "TrustOpsInboxItems");
        }
    }
}
