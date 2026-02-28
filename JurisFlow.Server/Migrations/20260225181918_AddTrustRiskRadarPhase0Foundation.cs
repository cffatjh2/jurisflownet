using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JurisFlow.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddTrustRiskRadarPhase0Foundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TrustRiskActions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    TrustRiskEventId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ActionType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    ActorUserId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ActorType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    CorrelationId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrustRiskActions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TrustRiskEvents",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    PolicyId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    PolicyKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    PolicyVersionNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    SourceType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SourceId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    TriggerType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    TrustTransactionId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    BillingLedgerEntryId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    BillingPaymentAllocationId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    PaymentTransactionId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    InvoiceId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    MatterId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ClientId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    PayorClientId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    SourceCorrelationKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    CorrelationId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    TraceId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    EvaluationMode = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    RiskScore = table.Column<decimal>(type: "TEXT", nullable: false),
                    Severity = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Decision = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    ReviewQueueItemId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    IsRetryable = table.Column<bool>(type: "INTEGER", nullable: false),
                    RiskReasonsJson = table.Column<string>(type: "TEXT", nullable: true),
                    EvidenceJson = table.Column<string>(type: "TEXT", nullable: true),
                    FeaturesJson = table.Column<string>(type: "TEXT", nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    EvaluatedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EvaluatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrustRiskEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TrustRiskHolds",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    TrustRiskEventId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    TargetType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    TargetId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    HoldType = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    CorrelationId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    PlacedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    PlacedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ReleasedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ReleaseReason = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    ReleasedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrustRiskHolds", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TrustRiskPolicies",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    PolicyKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    VersionNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    WarnThreshold = table.Column<decimal>(type: "TEXT", nullable: false),
                    ReviewThreshold = table.Column<decimal>(type: "TEXT", nullable: false),
                    SoftHoldThreshold = table.Column<decimal>(type: "TEXT", nullable: false),
                    HardHoldThreshold = table.Column<decimal>(type: "TEXT", nullable: false),
                    FailMode = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    EnabledRulesJson = table.Column<string>(type: "TEXT", nullable: true),
                    RuleWeightsJson = table.Column<string>(type: "TEXT", nullable: true),
                    ActionMapJson = table.Column<string>(type: "TEXT", nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    PublishedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrustRiskPolicies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TrustRiskReviewLinks",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    TrustRiskEventId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ReviewQueueItemId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ReviewQueueType = table.Column<string>(type: "TEXT", maxLength: 48, nullable: false),
                    LinkReasonCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    CorrelationId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrustRiskReviewLinks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TrustRiskActions_TenantId",
                table: "TrustRiskActions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TrustRiskActions_TenantId_ActionType_Status_CreatedAt",
                table: "TrustRiskActions",
                columns: new[] { "TenantId", "ActionType", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TrustRiskActions_TenantId_TrustRiskEventId_CreatedAt",
                table: "TrustRiskActions",
                columns: new[] { "TenantId", "TrustRiskEventId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TrustRiskEvents_TenantId",
                table: "TrustRiskEvents",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TrustRiskEvents_TenantId_CorrelationId",
                table: "TrustRiskEvents",
                columns: new[] { "TenantId", "CorrelationId" });

            migrationBuilder.CreateIndex(
                name: "IX_TrustRiskEvents_TenantId_InvoiceId_MatterId_CreatedAt",
                table: "TrustRiskEvents",
                columns: new[] { "TenantId", "InvoiceId", "MatterId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TrustRiskEvents_TenantId_SourceType_SourceId_CreatedAt",
                table: "TrustRiskEvents",
                columns: new[] { "TenantId", "SourceType", "SourceId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TrustRiskEvents_TenantId_Status_Severity_CreatedAt",
                table: "TrustRiskEvents",
                columns: new[] { "TenantId", "Status", "Severity", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TrustRiskEvents_TenantId_TrustTransactionId_BillingLedgerEntryId_BillingPaymentAllocationId",
                table: "TrustRiskEvents",
                columns: new[] { "TenantId", "TrustTransactionId", "BillingLedgerEntryId", "BillingPaymentAllocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_TrustRiskHolds_TenantId",
                table: "TrustRiskHolds",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TrustRiskHolds_TenantId_Status_HoldType_PlacedAt",
                table: "TrustRiskHolds",
                columns: new[] { "TenantId", "Status", "HoldType", "PlacedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TrustRiskHolds_TenantId_TargetType_TargetId",
                table: "TrustRiskHolds",
                columns: new[] { "TenantId", "TargetType", "TargetId" });

            migrationBuilder.CreateIndex(
                name: "IX_TrustRiskHolds_TenantId_TrustRiskEventId_Status",
                table: "TrustRiskHolds",
                columns: new[] { "TenantId", "TrustRiskEventId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TrustRiskPolicies_TenantId",
                table: "TrustRiskPolicies",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TrustRiskPolicies_TenantId_IsActive_Status_UpdatedAt",
                table: "TrustRiskPolicies",
                columns: new[] { "TenantId", "IsActive", "Status", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TrustRiskPolicies_TenantId_PolicyKey_VersionNumber",
                table: "TrustRiskPolicies",
                columns: new[] { "TenantId", "PolicyKey", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrustRiskReviewLinks_TenantId",
                table: "TrustRiskReviewLinks",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TrustRiskReviewLinks_TenantId_ReviewQueueItemId_ReviewQueueType",
                table: "TrustRiskReviewLinks",
                columns: new[] { "TenantId", "ReviewQueueItemId", "ReviewQueueType" });

            migrationBuilder.CreateIndex(
                name: "IX_TrustRiskReviewLinks_TenantId_TrustRiskEventId_Status",
                table: "TrustRiskReviewLinks",
                columns: new[] { "TenantId", "TrustRiskEventId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TrustRiskActions");

            migrationBuilder.DropTable(
                name: "TrustRiskEvents");

            migrationBuilder.DropTable(
                name: "TrustRiskHolds");

            migrationBuilder.DropTable(
                name: "TrustRiskPolicies");

            migrationBuilder.DropTable(
                name: "TrustRiskReviewLinks");
        }
    }
}
