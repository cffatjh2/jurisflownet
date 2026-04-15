using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JurisFlow.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddTrustEvidencePipelineAndPacketTemplatesPhase6AB : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TrustEvidenceFiles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    TrustAccountId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    PeriodStart = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PeriodEnd = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    FileHash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    EvidenceKey = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    FileSizeBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    LatestParserRunId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    CanonicalStatementImportId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    DuplicateOfEvidenceFileId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    SupersededByEvidenceFileId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    SupersededBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    SupersededAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RegisteredBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    RegisteredAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrustEvidenceFiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TrustJurisdictionPacketTemplates",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    PolicyKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Jurisdiction = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    AccountType = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    TemplateKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    VersionNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    RequiredSectionsJson = table.Column<string>(type: "TEXT", nullable: true),
                    RequiredAttestationsJson = table.Column<string>(type: "TEXT", nullable: true),
                    DisclosureBlocksJson = table.Column<string>(type: "TEXT", nullable: true),
                    RenderingProfileJson = table.Column<string>(type: "TEXT", nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrustJurisdictionPacketTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TrustMonthCloseAttestations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    TrustMonthCloseId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 48, nullable: false),
                    AttestationKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Label = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Accepted = table.Column<bool>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    SignedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    SignedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrustMonthCloseAttestations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TrustStatementParserRuns",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    TrustAccountId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    TrustEvidenceFileId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    TrustStatementImportId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    PeriodStart = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PeriodEnd = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ParserKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    AttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    StartedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    SummaryJson = table.Column<string>(type: "TEXT", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrustStatementParserRuns", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TrustEvidenceFiles_TenantId",
                table: "TrustEvidenceFiles",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TrustEvidenceFiles_TenantId_DuplicateOfEvidenceFileId",
                table: "TrustEvidenceFiles",
                columns: new[] { "TenantId", "DuplicateOfEvidenceFileId" });

            migrationBuilder.CreateIndex(
                name: "IX_TrustEvidenceFiles_TenantId_EvidenceKey",
                table: "TrustEvidenceFiles",
                columns: new[] { "TenantId", "EvidenceKey" });

            migrationBuilder.CreateIndex(
                name: "IX_TrustEvidenceFiles_TenantId_SupersededByEvidenceFileId",
                table: "TrustEvidenceFiles",
                columns: new[] { "TenantId", "SupersededByEvidenceFileId" });

            migrationBuilder.CreateIndex(
                name: "IX_TrustEvidenceFiles_TenantId_TrustAccountId_PeriodEnd",
                table: "TrustEvidenceFiles",
                columns: new[] { "TenantId", "TrustAccountId", "PeriodEnd" });

            migrationBuilder.CreateIndex(
                name: "IX_TrustEvidenceFiles_TenantId_TrustAccountId_PeriodStart_PeriodEnd_FileHash",
                table: "TrustEvidenceFiles",
                columns: new[] { "TenantId", "TrustAccountId", "PeriodStart", "PeriodEnd", "FileHash" });

            migrationBuilder.CreateIndex(
                name: "IX_TrustJurisdictionPacketTemplates_TenantId",
                table: "TrustJurisdictionPacketTemplates",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TrustJurisdictionPacketTemplates_TenantId_Jurisdiction_AccountType_IsActive",
                table: "TrustJurisdictionPacketTemplates",
                columns: new[] { "TenantId", "Jurisdiction", "AccountType", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_TrustJurisdictionPacketTemplates_TenantId_PolicyKey_Jurisdiction_AccountType_TemplateKey_VersionNumber",
                table: "TrustJurisdictionPacketTemplates",
                columns: new[] { "TenantId", "PolicyKey", "Jurisdiction", "AccountType", "TemplateKey", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrustMonthCloseAttestations_TenantId",
                table: "TrustMonthCloseAttestations",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TrustMonthCloseAttestations_TenantId_TrustMonthCloseId_Role_AttestationKey",
                table: "TrustMonthCloseAttestations",
                columns: new[] { "TenantId", "TrustMonthCloseId", "Role", "AttestationKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrustMonthCloseAttestations_TenantId_TrustMonthCloseId_Role_SignedAt",
                table: "TrustMonthCloseAttestations",
                columns: new[] { "TenantId", "TrustMonthCloseId", "Role", "SignedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TrustStatementParserRuns_TenantId",
                table: "TrustStatementParserRuns",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TrustStatementParserRuns_TenantId_TrustAccountId_PeriodEnd_Status",
                table: "TrustStatementParserRuns",
                columns: new[] { "TenantId", "TrustAccountId", "PeriodEnd", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TrustStatementParserRuns_TenantId_TrustEvidenceFileId_StartedAt",
                table: "TrustStatementParserRuns",
                columns: new[] { "TenantId", "TrustEvidenceFileId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TrustStatementParserRuns_TenantId_TrustStatementImportId",
                table: "TrustStatementParserRuns",
                columns: new[] { "TenantId", "TrustStatementImportId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TrustEvidenceFiles");

            migrationBuilder.DropTable(
                name: "TrustJurisdictionPacketTemplates");

            migrationBuilder.DropTable(
                name: "TrustMonthCloseAttestations");

            migrationBuilder.DropTable(
                name: "TrustStatementParserRuns");
        }
    }
}
