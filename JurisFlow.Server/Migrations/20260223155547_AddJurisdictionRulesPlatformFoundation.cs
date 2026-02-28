using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JurisFlow.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddJurisdictionRulesPlatformFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "JurisdictionCoverageMatrixEntries",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    CoverageKey = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    JurisdictionCode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CourtSystem = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    CourtDivision = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Venue = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    CaseType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    FilingMethod = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    SupportLevel = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EffectiveTo = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ConfidenceLevel = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ConfidenceScore = table.Column<decimal>(type: "TEXT", nullable: false),
                    RulePackId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    CapabilitiesJson = table.Column<string>(type: "TEXT", nullable: true),
                    ConstraintsJson = table.Column<string>(type: "TEXT", nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    SourceCitation = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JurisdictionCoverageMatrixEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "JurisdictionDefinitions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    JurisdictionCode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Scope = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    CountryCode = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    StateCode = table.Column<string>(type: "TEXT", maxLength: 8, nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    ParentJurisdictionCode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    CourtSystem = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JurisdictionDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "JurisdictionRuleChangeRecords",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    RulePackId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    CoverageEntryId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    JurisdictionCode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CourtSystem = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    CaseType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    FilingMethod = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ChangeType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    Severity = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    SourceCitation = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    DiffJson = table.Column<string>(type: "TEXT", nullable: true),
                    SourcePayloadJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ReviewedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReviewNotes = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JurisdictionRuleChangeRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "JurisdictionRulePacks",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    ScopeKey = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    JurisdictionCode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CourtSystem = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    CourtDivision = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Venue = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    CaseType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    FilingMethod = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EffectiveTo = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ConfidenceLevel = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ConfidenceScore = table.Column<decimal>(type: "TEXT", nullable: false),
                    SourceCitation = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    SourceReferenceId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    DocumentRulesJson = table.Column<string>(type: "TEXT", nullable: true),
                    FeeRulesJson = table.Column<string>(type: "TEXT", nullable: true),
                    ServiceRulesJson = table.Column<string>(type: "TEXT", nullable: true),
                    DeadlineRulesJson = table.Column<string>(type: "TEXT", nullable: true),
                    LocalOverridesJson = table.Column<string>(type: "TEXT", nullable: true),
                    ValidationRulesJson = table.Column<string>(type: "TEXT", nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    SubmittedForReviewBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    SubmittedForReviewAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PublishedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    PublishedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SupersededByRulePackId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ReviewNotes = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JurisdictionRulePacks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "JurisdictionValidationTestCases",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    JurisdictionCode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CourtSystem = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    CourtDivision = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Venue = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    CaseType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    FilingMethod = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    RulePackId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ExpectedSupportLevel = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    ExpectedRequiresHumanReview = table.Column<bool>(type: "INTEGER", nullable: false),
                    PacketInputJson = table.Column<string>(type: "TEXT", nullable: true),
                    ExpectedOutputJson = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JurisdictionValidationTestCases", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "JurisdictionValidationTestRuns",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    JurisdictionCode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    CourtSystem = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    CaseType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    FilingMethod = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    RulePackId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    TotalCases = table.Column<int>(type: "INTEGER", nullable: false),
                    PassedCases = table.Column<int>(type: "INTEGER", nullable: false),
                    FailedCases = table.Column<int>(type: "INTEGER", nullable: false),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    ResultJson = table.Column<string>(type: "TEXT", nullable: true),
                    TriggeredBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JurisdictionValidationTestRuns", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_JurisdictionCoverageMatrixEntries_TenantId",
                table: "JurisdictionCoverageMatrixEntries",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_JurisdictionCoverageMatrixEntries_TenantId_CoverageKey_Version",
                table: "JurisdictionCoverageMatrixEntries",
                columns: new[] { "TenantId", "CoverageKey", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JurisdictionCoverageMatrixEntries_TenantId_JurisdictionCode_SupportLevel_Status",
                table: "JurisdictionCoverageMatrixEntries",
                columns: new[] { "TenantId", "JurisdictionCode", "SupportLevel", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_JurisdictionCoverageMatrixEntries_TenantId_RulePackId_Status",
                table: "JurisdictionCoverageMatrixEntries",
                columns: new[] { "TenantId", "RulePackId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_JurisdictionDefinitions_TenantId",
                table: "JurisdictionDefinitions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_JurisdictionDefinitions_TenantId_JurisdictionCode",
                table: "JurisdictionDefinitions",
                columns: new[] { "TenantId", "JurisdictionCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JurisdictionDefinitions_TenantId_Scope_IsActive",
                table: "JurisdictionDefinitions",
                columns: new[] { "TenantId", "Scope", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_JurisdictionRuleChangeRecords_TenantId",
                table: "JurisdictionRuleChangeRecords",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_JurisdictionRuleChangeRecords_TenantId_JurisdictionCode_Status_CreatedAt",
                table: "JurisdictionRuleChangeRecords",
                columns: new[] { "TenantId", "JurisdictionCode", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_JurisdictionRuleChangeRecords_TenantId_RulePackId_ChangeType",
                table: "JurisdictionRuleChangeRecords",
                columns: new[] { "TenantId", "RulePackId", "ChangeType" });

            migrationBuilder.CreateIndex(
                name: "IX_JurisdictionRulePacks_TenantId",
                table: "JurisdictionRulePacks",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_JurisdictionRulePacks_TenantId_CaseType_FilingMethod_Status",
                table: "JurisdictionRulePacks",
                columns: new[] { "TenantId", "CaseType", "FilingMethod", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_JurisdictionRulePacks_TenantId_JurisdictionCode_Status_EffectiveFrom",
                table: "JurisdictionRulePacks",
                columns: new[] { "TenantId", "JurisdictionCode", "Status", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_JurisdictionRulePacks_TenantId_ScopeKey_Version",
                table: "JurisdictionRulePacks",
                columns: new[] { "TenantId", "ScopeKey", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JurisdictionValidationTestCases_TenantId",
                table: "JurisdictionValidationTestCases",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_JurisdictionValidationTestCases_TenantId_JurisdictionCode_Status",
                table: "JurisdictionValidationTestCases",
                columns: new[] { "TenantId", "JurisdictionCode", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_JurisdictionValidationTestCases_TenantId_RulePackId_Status",
                table: "JurisdictionValidationTestCases",
                columns: new[] { "TenantId", "RulePackId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_JurisdictionValidationTestRuns_TenantId",
                table: "JurisdictionValidationTestRuns",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_JurisdictionValidationTestRuns_TenantId_CreatedAt",
                table: "JurisdictionValidationTestRuns",
                columns: new[] { "TenantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_JurisdictionValidationTestRuns_TenantId_JurisdictionCode_Status_CreatedAt",
                table: "JurisdictionValidationTestRuns",
                columns: new[] { "TenantId", "JurisdictionCode", "Status", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "JurisdictionCoverageMatrixEntries");

            migrationBuilder.DropTable(
                name: "JurisdictionDefinitions");

            migrationBuilder.DropTable(
                name: "JurisdictionRuleChangeRecords");

            migrationBuilder.DropTable(
                name: "JurisdictionRulePacks");

            migrationBuilder.DropTable(
                name: "JurisdictionValidationTestCases");

            migrationBuilder.DropTable(
                name: "JurisdictionValidationTestRuns");
        }
    }
}
