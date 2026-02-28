using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JurisFlow.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddConnectorDomainTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CourtDocketEntries",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    ProviderKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ExternalDocketId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ExternalCaseId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    DocketNumber = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    CaseName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Court = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    SourceUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    FiledAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ModifiedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastSeenAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    MatterId = table.Column<string>(type: "TEXT", nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourtDocketEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CourtDocketEntries_Matters_MatterId",
                        column: x => x.MatterId,
                        principalTable: "Matters",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "EfilingSubmissions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    ProviderKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ExternalSubmissionId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ExternalDocketId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ReferenceNumber = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    MatterId = table.Column<string>(type: "TEXT", nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    AcceptedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RejectedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RejectionReason = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    LastSeenAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EfilingSubmissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EfilingSubmissions_Matters_MatterId",
                        column: x => x.MatterId,
                        principalTable: "Matters",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "IntegrationEntityLinks",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    ConnectionId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ProviderKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    LocalEntityType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    LocalEntityId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ExternalEntityType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ExternalEntityId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ExternalVersion = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    LastDirection = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    LastSyncedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntegrationEntityLinks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CourtDocketEntries_MatterId",
                table: "CourtDocketEntries",
                column: "MatterId");

            migrationBuilder.CreateIndex(
                name: "IX_CourtDocketEntries_TenantId",
                table: "CourtDocketEntries",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_CourtDocketEntries_TenantId_MatterId_ModifiedAt",
                table: "CourtDocketEntries",
                columns: new[] { "TenantId", "MatterId", "ModifiedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CourtDocketEntries_TenantId_ProviderKey_ExternalDocketId",
                table: "CourtDocketEntries",
                columns: new[] { "TenantId", "ProviderKey", "ExternalDocketId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EfilingSubmissions_MatterId",
                table: "EfilingSubmissions",
                column: "MatterId");

            migrationBuilder.CreateIndex(
                name: "IX_EfilingSubmissions_TenantId",
                table: "EfilingSubmissions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_EfilingSubmissions_TenantId_MatterId_Status_LastSeenAt",
                table: "EfilingSubmissions",
                columns: new[] { "TenantId", "MatterId", "Status", "LastSeenAt" });

            migrationBuilder.CreateIndex(
                name: "IX_EfilingSubmissions_TenantId_ProviderKey_ExternalSubmissionId",
                table: "EfilingSubmissions",
                columns: new[] { "TenantId", "ProviderKey", "ExternalSubmissionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationEntityLinks_TenantId",
                table: "IntegrationEntityLinks",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationEntityLinks_TenantId_ConnectionId_ExternalEntityType_ExternalEntityId",
                table: "IntegrationEntityLinks",
                columns: new[] { "TenantId", "ConnectionId", "ExternalEntityType", "ExternalEntityId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationEntityLinks_TenantId_ConnectionId_LocalEntityType_LocalEntityId",
                table: "IntegrationEntityLinks",
                columns: new[] { "TenantId", "ConnectionId", "LocalEntityType", "LocalEntityId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationEntityLinks_TenantId_ProviderKey_LastSyncedAt",
                table: "IntegrationEntityLinks",
                columns: new[] { "TenantId", "ProviderKey", "LastSyncedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CourtDocketEntries");

            migrationBuilder.DropTable(
                name: "EfilingSubmissions");

            migrationBuilder.DropTable(
                name: "IntegrationEntityLinks");
        }
    }
}
