using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JurisFlow.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddAiEvidenceLinkedDraftingFoundationPhase0 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiDraftClaims",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    DraftOutputId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    OrderIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    ClaimText = table.Column<string>(type: "TEXT", nullable: false),
                    ClaimType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    IsCritical = table.Column<bool>(type: "INTEGER", nullable: false),
                    Confidence = table.Column<decimal>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    SupportSummary = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiDraftClaims", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AiDraftEvidenceLinks",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    ClaimId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    DocumentId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    DocumentVersionId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Sha256 = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Page = table.Column<int>(type: "INTEGER", nullable: true),
                    ParagraphId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    CharStart = table.Column<int>(type: "INTEGER", nullable: true),
                    CharEnd = table.Column<int>(type: "INTEGER", nullable: true),
                    Excerpt = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    SupportStrength = table.Column<string>(type: "TEXT", maxLength: 24, nullable: true),
                    WhySupports = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiDraftEvidenceLinks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AiDraftOutputs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    SessionId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    RenderedText = table.Column<string>(type: "TEXT", nullable: false),
                    Model = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    PromptTemplateVersion = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    RetrievalBundleId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    CorrelationId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    RetrievalBundleJson = table.Column<string>(type: "TEXT", nullable: true),
                    StructuredClaimsJson = table.Column<string>(type: "TEXT", nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    GeneratedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiDraftOutputs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AiDraftRuleCitations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    ClaimId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    JurisdictionRulePackId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    RulePackVersion = table.Column<int>(type: "INTEGER", nullable: true),
                    RuleCode = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    SourceCitation = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    CitationText = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    EffectiveAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Confidence = table.Column<decimal>(type: "TEXT", nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiDraftRuleCitations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AiDraftSessions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    MatterId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    Purpose = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    JurisdictionContextJson = table.Column<string>(type: "TEXT", nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiDraftSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AiDraftVerificationRuns",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    DraftOutputId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    VerifierVersion = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    CorrelationId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ResultJson = table.Column<string>(type: "TEXT", nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiDraftVerificationRuns", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiDraftClaims_TenantId",
                table: "AiDraftClaims",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AiDraftClaims_TenantId_DraftOutputId_OrderIndex",
                table: "AiDraftClaims",
                columns: new[] { "TenantId", "DraftOutputId", "OrderIndex" });

            migrationBuilder.CreateIndex(
                name: "IX_AiDraftClaims_TenantId_IsCritical_Status",
                table: "AiDraftClaims",
                columns: new[] { "TenantId", "IsCritical", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AiDraftEvidenceLinks_TenantId",
                table: "AiDraftEvidenceLinks",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AiDraftEvidenceLinks_TenantId_ClaimId_DocumentVersionId",
                table: "AiDraftEvidenceLinks",
                columns: new[] { "TenantId", "ClaimId", "DocumentVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_AiDraftEvidenceLinks_TenantId_DocumentId_Sha256",
                table: "AiDraftEvidenceLinks",
                columns: new[] { "TenantId", "DocumentId", "Sha256" });

            migrationBuilder.CreateIndex(
                name: "IX_AiDraftOutputs_TenantId",
                table: "AiDraftOutputs",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AiDraftOutputs_TenantId_CorrelationId",
                table: "AiDraftOutputs",
                columns: new[] { "TenantId", "CorrelationId" });

            migrationBuilder.CreateIndex(
                name: "IX_AiDraftOutputs_TenantId_SessionId_GeneratedAt",
                table: "AiDraftOutputs",
                columns: new[] { "TenantId", "SessionId", "GeneratedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AiDraftRuleCitations_TenantId",
                table: "AiDraftRuleCitations",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AiDraftRuleCitations_TenantId_ClaimId_RuleCode",
                table: "AiDraftRuleCitations",
                columns: new[] { "TenantId", "ClaimId", "RuleCode" });

            migrationBuilder.CreateIndex(
                name: "IX_AiDraftRuleCitations_TenantId_JurisdictionRulePackId_RulePackVersion",
                table: "AiDraftRuleCitations",
                columns: new[] { "TenantId", "JurisdictionRulePackId", "RulePackVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_AiDraftSessions_TenantId",
                table: "AiDraftSessions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AiDraftSessions_TenantId_MatterId_Status_CreatedAt",
                table: "AiDraftSessions",
                columns: new[] { "TenantId", "MatterId", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AiDraftSessions_TenantId_UserId_CreatedAt",
                table: "AiDraftSessions",
                columns: new[] { "TenantId", "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AiDraftVerificationRuns_TenantId",
                table: "AiDraftVerificationRuns",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AiDraftVerificationRuns_TenantId_DraftOutputId_CreatedAt",
                table: "AiDraftVerificationRuns",
                columns: new[] { "TenantId", "DraftOutputId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AiDraftVerificationRuns_TenantId_Status_CreatedAt",
                table: "AiDraftVerificationRuns",
                columns: new[] { "TenantId", "Status", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiDraftClaims");

            migrationBuilder.DropTable(
                name: "AiDraftEvidenceLinks");

            migrationBuilder.DropTable(
                name: "AiDraftOutputs");

            migrationBuilder.DropTable(
                name: "AiDraftRuleCitations");

            migrationBuilder.DropTable(
                name: "AiDraftSessions");

            migrationBuilder.DropTable(
                name: "AiDraftVerificationRuns");
        }
    }
}
