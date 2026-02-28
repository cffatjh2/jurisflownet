using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JurisFlow.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddClientTransparencyFoundationPhase0 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClientTransparencyCostImpacts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    SnapshotId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    CurrentExpectedRangeMin = table.Column<decimal>(type: "TEXT", nullable: true),
                    CurrentExpectedRangeMax = table.Column<decimal>(type: "TEXT", nullable: true),
                    DeltaRangeMin = table.Column<decimal>(type: "TEXT", nullable: true),
                    DeltaRangeMax = table.Column<decimal>(type: "TEXT", nullable: true),
                    ConfidenceBand = table.Column<string>(type: "TEXT", maxLength: 24, nullable: true),
                    DriverSummary = table.Column<string>(type: "TEXT", nullable: true),
                    DriversJson = table.Column<string>(type: "TEXT", nullable: true),
                    SourceRefsJson = table.Column<string>(type: "TEXT", nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientTransparencyCostImpacts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ClientTransparencyDelayReasons",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    SnapshotId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ReasonCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Severity = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    ExpectedDelayDays = table.Column<int>(type: "INTEGER", nullable: true),
                    ClientSafeText = table.Column<string>(type: "TEXT", nullable: true),
                    SourceRefsJson = table.Column<string>(type: "TEXT", nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientTransparencyDelayReasons", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ClientTransparencyNextSteps",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    SnapshotId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    OwnerType = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    ActionText = table.Column<string>(type: "TEXT", nullable: false),
                    EtaAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    BlockedByText = table.Column<string>(type: "TEXT", nullable: true),
                    SourceRefsJson = table.Column<string>(type: "TEXT", nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientTransparencyNextSteps", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ClientTransparencyProfiles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Scope = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    MatterId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ProfileKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    PublishPolicy = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    VisibilityRulesJson = table.Column<string>(type: "TEXT", nullable: true),
                    RedactionRulesJson = table.Column<string>(type: "TEXT", nullable: true),
                    SourceWhitelistJson = table.Column<string>(type: "TEXT", nullable: true),
                    DelayTaxonomyJson = table.Column<string>(type: "TEXT", nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientTransparencyProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ClientTransparencyReviewActions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    SnapshotId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ActionType = table.Column<string>(type: "TEXT", maxLength: 48, nullable: false),
                    ReviewerUserId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Reason = table.Column<string>(type: "TEXT", nullable: true),
                    BeforeJson = table.Column<string>(type: "TEXT", nullable: true),
                    AfterJson = table.Column<string>(type: "TEXT", nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientTransparencyReviewActions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ClientTransparencySnapshots",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    MatterId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ProfileId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    VersionNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    IsCurrent = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsPublished = table.Column<bool>(type: "INTEGER", nullable: false),
                    DataQuality = table.Column<string>(type: "TEXT", maxLength: 24, nullable: true),
                    ConfidenceScore = table.Column<decimal>(type: "TEXT", nullable: true),
                    CorrelationId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    SnapshotSummary = table.Column<string>(type: "TEXT", nullable: true),
                    WhatChangedSummary = table.Column<string>(type: "TEXT", nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    GeneratedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientTransparencySnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ClientTransparencyTimelineItems",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    SnapshotId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    OrderIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    PhaseKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Label = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    ClientSafeText = table.Column<string>(type: "TEXT", nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    EtaAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SourceRefsJson = table.Column<string>(type: "TEXT", nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientTransparencyTimelineItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ClientTransparencyUpdateEvents",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    MatterId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    SnapshotId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    TriggerType = table.Column<string>(type: "TEXT", maxLength: 48, nullable: false),
                    TriggerEntityType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    TriggerEntityId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    CorrelationId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    TriggeredBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: true),
                    DiffJson = table.Column<string>(type: "TEXT", nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AppliedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientTransparencyUpdateEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClientTransparencyCostImpacts_TenantId",
                table: "ClientTransparencyCostImpacts",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientTransparencyCostImpacts_TenantId_SnapshotId",
                table: "ClientTransparencyCostImpacts",
                columns: new[] { "TenantId", "SnapshotId" });

            migrationBuilder.CreateIndex(
                name: "IX_ClientTransparencyDelayReasons_TenantId",
                table: "ClientTransparencyDelayReasons",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientTransparencyDelayReasons_TenantId_SnapshotId_IsActive_Severity",
                table: "ClientTransparencyDelayReasons",
                columns: new[] { "TenantId", "SnapshotId", "IsActive", "Severity" });

            migrationBuilder.CreateIndex(
                name: "IX_ClientTransparencyNextSteps_TenantId",
                table: "ClientTransparencyNextSteps",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientTransparencyNextSteps_TenantId_SnapshotId_Status_EtaAtUtc",
                table: "ClientTransparencyNextSteps",
                columns: new[] { "TenantId", "SnapshotId", "Status", "EtaAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ClientTransparencyProfiles_TenantId",
                table: "ClientTransparencyProfiles",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientTransparencyProfiles_TenantId_ProfileKey_Status",
                table: "ClientTransparencyProfiles",
                columns: new[] { "TenantId", "ProfileKey", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ClientTransparencyProfiles_TenantId_Scope_MatterId_Status",
                table: "ClientTransparencyProfiles",
                columns: new[] { "TenantId", "Scope", "MatterId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ClientTransparencyReviewActions_TenantId",
                table: "ClientTransparencyReviewActions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientTransparencyReviewActions_TenantId_SnapshotId_CreatedAt",
                table: "ClientTransparencyReviewActions",
                columns: new[] { "TenantId", "SnapshotId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ClientTransparencySnapshots_TenantId",
                table: "ClientTransparencySnapshots",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientTransparencySnapshots_TenantId_IsPublished_PublishedAt",
                table: "ClientTransparencySnapshots",
                columns: new[] { "TenantId", "IsPublished", "PublishedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ClientTransparencySnapshots_TenantId_MatterId_IsCurrent_GeneratedAt",
                table: "ClientTransparencySnapshots",
                columns: new[] { "TenantId", "MatterId", "IsCurrent", "GeneratedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ClientTransparencySnapshots_TenantId_MatterId_VersionNumber",
                table: "ClientTransparencySnapshots",
                columns: new[] { "TenantId", "MatterId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClientTransparencyTimelineItems_TenantId",
                table: "ClientTransparencyTimelineItems",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientTransparencyTimelineItems_TenantId_SnapshotId_OrderIndex",
                table: "ClientTransparencyTimelineItems",
                columns: new[] { "TenantId", "SnapshotId", "OrderIndex" });

            migrationBuilder.CreateIndex(
                name: "IX_ClientTransparencyUpdateEvents_TenantId",
                table: "ClientTransparencyUpdateEvents",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientTransparencyUpdateEvents_TenantId_MatterId_CreatedAt",
                table: "ClientTransparencyUpdateEvents",
                columns: new[] { "TenantId", "MatterId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ClientTransparencyUpdateEvents_TenantId_SnapshotId_CreatedAt",
                table: "ClientTransparencyUpdateEvents",
                columns: new[] { "TenantId", "SnapshotId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClientTransparencyCostImpacts");

            migrationBuilder.DropTable(
                name: "ClientTransparencyDelayReasons");

            migrationBuilder.DropTable(
                name: "ClientTransparencyNextSteps");

            migrationBuilder.DropTable(
                name: "ClientTransparencyProfiles");

            migrationBuilder.DropTable(
                name: "ClientTransparencyReviewActions");

            migrationBuilder.DropTable(
                name: "ClientTransparencySnapshots");

            migrationBuilder.DropTable(
                name: "ClientTransparencyTimelineItems");

            migrationBuilder.DropTable(
                name: "ClientTransparencyUpdateEvents");
        }
    }
}
