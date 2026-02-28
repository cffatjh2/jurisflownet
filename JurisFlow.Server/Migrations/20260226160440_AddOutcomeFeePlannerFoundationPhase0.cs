using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JurisFlow.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddOutcomeFeePlannerFoundationPhase0 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CurrentOutcomeFeePlanId",
                table: "Matters",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OutcomeFeeAssumptions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    PlanVersionId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 48, nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 96, nullable: false),
                    ValueType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ValueJson = table.Column<string>(type: "TEXT", nullable: true),
                    SourceType = table.Column<string>(type: "TEXT", maxLength: 48, nullable: false),
                    SourceRef = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutcomeFeeAssumptions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OutcomeFeeCalibrationSnapshots",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    CohortKey = table.Column<string>(type: "TEXT", maxLength: 96, nullable: false),
                    JurisdictionCode = table.Column<string>(type: "TEXT", maxLength: 96, nullable: true),
                    PracticeArea = table.Column<string>(type: "TEXT", maxLength: 96, nullable: true),
                    ArrangementType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    AsOfDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    SampleSize = table.Column<int>(type: "INTEGER", nullable: false),
                    MetricsJson = table.Column<string>(type: "TEXT", nullable: true),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutcomeFeeCalibrationSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OutcomeFeeCollectionsForecasts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    ScenarioId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    PayorSegment = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    BucketDays = table.Column<int>(type: "INTEGER", nullable: false),
                    ExpectedAmount = table.Column<decimal>(type: "TEXT", nullable: false),
                    CollectionProbability = table.Column<decimal>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutcomeFeeCollectionsForecasts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OutcomeFeePhaseForecasts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    ScenarioId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    PhaseOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    PhaseCode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 96, nullable: false),
                    HoursExpected = table.Column<decimal>(type: "TEXT", nullable: false),
                    FeeExpected = table.Column<decimal>(type: "TEXT", nullable: false),
                    ExpenseExpected = table.Column<decimal>(type: "TEXT", nullable: false),
                    DurationDaysExpected = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutcomeFeePhaseForecasts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OutcomeFeePlans",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    MatterId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ClientId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    MatterBillingPolicyId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    CurrentVersionId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    PlannerMode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    CorrelationId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutcomeFeePlans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OutcomeFeePlanVersions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    PlanId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    VersionNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    PlannerMode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ModelVersion = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    AssumptionSetVersion = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CorrelationId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    MatterBillingPolicyId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    RateCardId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    GeneratedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    GeneratedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SourceSignalsJson = table.Column<string>(type: "TEXT", nullable: true),
                    InputSnapshotJson = table.Column<string>(type: "TEXT", nullable: true),
                    SummaryJson = table.Column<string>(type: "TEXT", nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutcomeFeePlanVersions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OutcomeFeeScenarios",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    PlanVersionId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ScenarioKey = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 96, nullable: false),
                    Probability = table.Column<decimal>(type: "TEXT", nullable: false),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    BudgetTotal = table.Column<decimal>(type: "TEXT", nullable: false),
                    ExpectedCollected = table.Column<decimal>(type: "TEXT", nullable: false),
                    ExpectedCost = table.Column<decimal>(type: "TEXT", nullable: false),
                    ExpectedMargin = table.Column<decimal>(type: "TEXT", nullable: false),
                    ConfidenceScore = table.Column<decimal>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    DriverSummary = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutcomeFeeScenarios", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OutcomeFeeStaffingLines",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    ScenarioId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    PhaseForecastId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Role = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    HoursExpected = table.Column<decimal>(type: "TEXT", nullable: false),
                    BillRate = table.Column<decimal>(type: "TEXT", nullable: false),
                    CostRate = table.Column<decimal>(type: "TEXT", nullable: false),
                    FeeExpected = table.Column<decimal>(type: "TEXT", nullable: false),
                    CostExpected = table.Column<decimal>(type: "TEXT", nullable: false),
                    UtilizationRiskScore = table.Column<decimal>(type: "TEXT", nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutcomeFeeStaffingLines", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OutcomeFeeUpdateEvents",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    PlanId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    TriggerType = table.Column<string>(type: "TEXT", maxLength: 48, nullable: false),
                    TriggerEntityType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    TriggerEntityId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    AppliedVersionId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    CorrelationId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    TriggeredBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: true),
                    ResultJson = table.Column<string>(type: "TEXT", nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AppliedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutcomeFeeUpdateEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutcomeFeeAssumptions_TenantId",
                table: "OutcomeFeeAssumptions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_OutcomeFeeAssumptions_TenantId_PlanVersionId_Category_Key",
                table: "OutcomeFeeAssumptions",
                columns: new[] { "TenantId", "PlanVersionId", "Category", "Key" });

            migrationBuilder.CreateIndex(
                name: "IX_OutcomeFeeCalibrationSnapshots_TenantId",
                table: "OutcomeFeeCalibrationSnapshots",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_OutcomeFeeCalibrationSnapshots_TenantId_CohortKey_AsOfDate",
                table: "OutcomeFeeCalibrationSnapshots",
                columns: new[] { "TenantId", "CohortKey", "AsOfDate" });

            migrationBuilder.CreateIndex(
                name: "IX_OutcomeFeeCalibrationSnapshots_TenantId_Status_CreatedAt",
                table: "OutcomeFeeCalibrationSnapshots",
                columns: new[] { "TenantId", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OutcomeFeeCollectionsForecasts_TenantId",
                table: "OutcomeFeeCollectionsForecasts",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_OutcomeFeeCollectionsForecasts_TenantId_ScenarioId_PayorSegment_BucketDays",
                table: "OutcomeFeeCollectionsForecasts",
                columns: new[] { "TenantId", "ScenarioId", "PayorSegment", "BucketDays" });

            migrationBuilder.CreateIndex(
                name: "IX_OutcomeFeePhaseForecasts_TenantId",
                table: "OutcomeFeePhaseForecasts",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_OutcomeFeePhaseForecasts_TenantId_ScenarioId_PhaseOrder",
                table: "OutcomeFeePhaseForecasts",
                columns: new[] { "TenantId", "ScenarioId", "PhaseOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_OutcomeFeePlans_TenantId",
                table: "OutcomeFeePlans",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_OutcomeFeePlans_TenantId_CorrelationId",
                table: "OutcomeFeePlans",
                columns: new[] { "TenantId", "CorrelationId" });

            migrationBuilder.CreateIndex(
                name: "IX_OutcomeFeePlans_TenantId_CurrentVersionId",
                table: "OutcomeFeePlans",
                columns: new[] { "TenantId", "CurrentVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_OutcomeFeePlans_TenantId_MatterId_Status_UpdatedAt",
                table: "OutcomeFeePlans",
                columns: new[] { "TenantId", "MatterId", "Status", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OutcomeFeePlanVersions_TenantId",
                table: "OutcomeFeePlanVersions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_OutcomeFeePlanVersions_TenantId_CorrelationId",
                table: "OutcomeFeePlanVersions",
                columns: new[] { "TenantId", "CorrelationId" });

            migrationBuilder.CreateIndex(
                name: "IX_OutcomeFeePlanVersions_TenantId_GeneratedAt",
                table: "OutcomeFeePlanVersions",
                columns: new[] { "TenantId", "GeneratedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OutcomeFeePlanVersions_TenantId_PlanId_VersionNumber",
                table: "OutcomeFeePlanVersions",
                columns: new[] { "TenantId", "PlanId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutcomeFeeScenarios_TenantId",
                table: "OutcomeFeeScenarios",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_OutcomeFeeScenarios_TenantId_PlanVersionId_ScenarioKey",
                table: "OutcomeFeeScenarios",
                columns: new[] { "TenantId", "PlanVersionId", "ScenarioKey" });

            migrationBuilder.CreateIndex(
                name: "IX_OutcomeFeeScenarios_TenantId_Status_CreatedAt",
                table: "OutcomeFeeScenarios",
                columns: new[] { "TenantId", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OutcomeFeeStaffingLines_TenantId",
                table: "OutcomeFeeStaffingLines",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_OutcomeFeeStaffingLines_TenantId_Role_CreatedAt",
                table: "OutcomeFeeStaffingLines",
                columns: new[] { "TenantId", "Role", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OutcomeFeeStaffingLines_TenantId_ScenarioId_PhaseForecastId_Role",
                table: "OutcomeFeeStaffingLines",
                columns: new[] { "TenantId", "ScenarioId", "PhaseForecastId", "Role" });

            migrationBuilder.CreateIndex(
                name: "IX_OutcomeFeeUpdateEvents_TenantId",
                table: "OutcomeFeeUpdateEvents",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_OutcomeFeeUpdateEvents_TenantId_AppliedVersionId_CreatedAt",
                table: "OutcomeFeeUpdateEvents",
                columns: new[] { "TenantId", "AppliedVersionId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OutcomeFeeUpdateEvents_TenantId_PlanId_TriggerType_CreatedAt",
                table: "OutcomeFeeUpdateEvents",
                columns: new[] { "TenantId", "PlanId", "TriggerType", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OutcomeFeeAssumptions");

            migrationBuilder.DropTable(
                name: "OutcomeFeeCalibrationSnapshots");

            migrationBuilder.DropTable(
                name: "OutcomeFeeCollectionsForecasts");

            migrationBuilder.DropTable(
                name: "OutcomeFeePhaseForecasts");

            migrationBuilder.DropTable(
                name: "OutcomeFeePlans");

            migrationBuilder.DropTable(
                name: "OutcomeFeePlanVersions");

            migrationBuilder.DropTable(
                name: "OutcomeFeeScenarios");

            migrationBuilder.DropTable(
                name: "OutcomeFeeStaffingLines");

            migrationBuilder.DropTable(
                name: "OutcomeFeeUpdateEvents");

            migrationBuilder.DropColumn(
                name: "CurrentOutcomeFeePlanId",
                table: "Matters");
        }
    }
}
