using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JurisFlow.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddAppDirectoryOnboarding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppDirectoryListings",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    ProviderKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ConnectionMode = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    ManifestVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ManifestJson = table.Column<string>(type: "TEXT", nullable: false),
                    WebsiteUrl = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    DocumentationUrl = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    SupportEmail = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    SupportUrl = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    LogoUrl = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    SupportsWebhook = table.Column<bool>(type: "INTEGER", nullable: false),
                    WebhookFirst = table.Column<bool>(type: "INTEGER", nullable: false),
                    FallbackPollingMinutes = table.Column<int>(type: "INTEGER", nullable: true),
                    SlaTier = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    SlaResponseHours = table.Column<int>(type: "INTEGER", nullable: true),
                    SlaResolutionHours = table.Column<int>(type: "INTEGER", nullable: true),
                    SlaUptimePercent = table.Column<double>(type: "REAL", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    SubmissionCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LastSubmittedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastTestStatus = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    LastTestedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastTestSummary = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    LastTestReportJson = table.Column<string>(type: "TEXT", nullable: true),
                    ReviewNotes = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    ReviewedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsFeatured = table.Column<bool>(type: "INTEGER", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppDirectoryListings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppDirectorySubmissions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    ListingId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    SubmittedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ManifestJson = table.Column<string>(type: "TEXT", nullable: false),
                    ValidationErrorsJson = table.Column<string>(type: "TEXT", nullable: true),
                    TestReportJson = table.Column<string>(type: "TEXT", nullable: true),
                    TestStatus = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppDirectorySubmissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppDirectorySubmissions_AppDirectoryListings_ListingId",
                        column: x => x.ListingId,
                        principalTable: "AppDirectoryListings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppDirectoryListings_TenantId",
                table: "AppDirectoryListings",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AppDirectoryListings_TenantId_ProviderKey",
                table: "AppDirectoryListings",
                columns: new[] { "TenantId", "ProviderKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppDirectoryListings_TenantId_Status_UpdatedAt",
                table: "AppDirectoryListings",
                columns: new[] { "TenantId", "Status", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AppDirectorySubmissions_ListingId",
                table: "AppDirectorySubmissions",
                column: "ListingId");

            migrationBuilder.CreateIndex(
                name: "IX_AppDirectorySubmissions_TenantId",
                table: "AppDirectorySubmissions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AppDirectorySubmissions_TenantId_ListingId_CreatedAt",
                table: "AppDirectorySubmissions",
                columns: new[] { "TenantId", "ListingId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AppDirectorySubmissions_TenantId_Status_CreatedAt",
                table: "AppDirectorySubmissions",
                columns: new[] { "TenantId", "Status", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppDirectorySubmissions");

            migrationBuilder.DropTable(
                name: "AppDirectoryListings");
        }
    }
}
