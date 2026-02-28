using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JurisFlow.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddIntegrationOpsFoundationP0 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IntegrationConflictQueueItems",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    ConnectionId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    RunId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ProviderKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    EntityType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    LocalEntityId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ExternalEntityId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    ConflictType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Severity = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    MappingProfileId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Fingerprint = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    AssignedTo = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ResolutionType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    LocalSnapshotJson = table.Column<string>(type: "TEXT", nullable: true),
                    ExternalSnapshotJson = table.Column<string>(type: "TEXT", nullable: true),
                    SuggestedResolutionJson = table.Column<string>(type: "TEXT", nullable: true),
                    ResolutionJson = table.Column<string>(type: "TEXT", nullable: true),
                    ReviewNotes = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    ReviewedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntegrationConflictQueueItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IntegrationInboxEvents",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    ConnectionId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    RunId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ProviderKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ExternalEventId = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    CorrelationId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    EventType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    SignatureValidated = table.Column<bool>(type: "INTEGER", nullable: false),
                    PayloadHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    HeadersJson = table.Column<string>(type: "TEXT", nullable: true),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    ReplayCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntegrationInboxEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IntegrationMappingProfiles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    ConnectionId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ProviderKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ProfileKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    EntityType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Direction = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ConflictPolicy = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    FieldMappingsJson = table.Column<string>(type: "TEXT", nullable: true),
                    EnumMappingsJson = table.Column<string>(type: "TEXT", nullable: true),
                    TaxMappingsJson = table.Column<string>(type: "TEXT", nullable: true),
                    AccountMappingsJson = table.Column<string>(type: "TEXT", nullable: true),
                    DefaultsJson = table.Column<string>(type: "TEXT", nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    ValidationSummary = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    LastValidatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntegrationMappingProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IntegrationOutboxEvents",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    ConnectionId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    RunId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ProviderKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    EventType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    EntityType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    EntityId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    CorrelationId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    AttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DispatchedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    HeadersJson = table.Column<string>(type: "TEXT", nullable: true),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    ErrorCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    DeadLettered = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntegrationOutboxEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IntegrationReviewQueueItems",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    ConnectionId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    RunId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ProviderKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ItemType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SourceId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    SourceType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ConflictId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Priority = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 160, nullable: true),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    ContextJson = table.Column<string>(type: "TEXT", nullable: true),
                    SuggestedActionsJson = table.Column<string>(type: "TEXT", nullable: true),
                    Decision = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    DecisionNotes = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    AssignedTo = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ReviewedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DueAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntegrationReviewQueueItems", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationConflictQueueItems_TenantId",
                table: "IntegrationConflictQueueItems",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationConflictQueueItems_TenantId_ConnectionId_Status_CreatedAt",
                table: "IntegrationConflictQueueItems",
                columns: new[] { "TenantId", "ConnectionId", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationConflictQueueItems_TenantId_Fingerprint",
                table: "IntegrationConflictQueueItems",
                columns: new[] { "TenantId", "Fingerprint" });

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationConflictQueueItems_TenantId_ProviderKey_Status_CreatedAt",
                table: "IntegrationConflictQueueItems",
                columns: new[] { "TenantId", "ProviderKey", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationInboxEvents_TenantId",
                table: "IntegrationInboxEvents",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationInboxEvents_TenantId_ConnectionId_Status_ReceivedAt",
                table: "IntegrationInboxEvents",
                columns: new[] { "TenantId", "ConnectionId", "Status", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationInboxEvents_TenantId_ProviderKey_ExternalEventId",
                table: "IntegrationInboxEvents",
                columns: new[] { "TenantId", "ProviderKey", "ExternalEventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationInboxEvents_TenantId_RunId_ReceivedAt",
                table: "IntegrationInboxEvents",
                columns: new[] { "TenantId", "RunId", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationMappingProfiles_TenantId",
                table: "IntegrationMappingProfiles",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationMappingProfiles_TenantId_ConnectionId_ProfileKey",
                table: "IntegrationMappingProfiles",
                columns: new[] { "TenantId", "ConnectionId", "ProfileKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationMappingProfiles_TenantId_ProviderKey_EntityType_Direction",
                table: "IntegrationMappingProfiles",
                columns: new[] { "TenantId", "ProviderKey", "EntityType", "Direction" });

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationMappingProfiles_TenantId_Status_UpdatedAt",
                table: "IntegrationMappingProfiles",
                columns: new[] { "TenantId", "Status", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationOutboxEvents_TenantId",
                table: "IntegrationOutboxEvents",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationOutboxEvents_TenantId_ConnectionId_IdempotencyKey",
                table: "IntegrationOutboxEvents",
                columns: new[] { "TenantId", "ConnectionId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationOutboxEvents_TenantId_ProviderKey_Status_NextAttemptAt",
                table: "IntegrationOutboxEvents",
                columns: new[] { "TenantId", "ProviderKey", "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationOutboxEvents_TenantId_RunId_CreatedAt",
                table: "IntegrationOutboxEvents",
                columns: new[] { "TenantId", "RunId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationReviewQueueItems_TenantId",
                table: "IntegrationReviewQueueItems",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationReviewQueueItems_TenantId_ConnectionId_Status_Priority",
                table: "IntegrationReviewQueueItems",
                columns: new[] { "TenantId", "ConnectionId", "Status", "Priority" });

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationReviewQueueItems_TenantId_ProviderKey_Status_CreatedAt",
                table: "IntegrationReviewQueueItems",
                columns: new[] { "TenantId", "ProviderKey", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationReviewQueueItems_TenantId_SourceType_SourceId",
                table: "IntegrationReviewQueueItems",
                columns: new[] { "TenantId", "SourceType", "SourceId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IntegrationConflictQueueItems");

            migrationBuilder.DropTable(
                name: "IntegrationInboxEvents");

            migrationBuilder.DropTable(
                name: "IntegrationMappingProfiles");

            migrationBuilder.DropTable(
                name: "IntegrationOutboxEvents");

            migrationBuilder.DropTable(
                name: "IntegrationReviewQueueItems");
        }
    }
}
