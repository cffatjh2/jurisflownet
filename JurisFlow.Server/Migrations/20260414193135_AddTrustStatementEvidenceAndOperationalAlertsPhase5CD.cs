using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JurisFlow.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddTrustStatementEvidenceAndOperationalAlertsPhase5CD : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DuplicateOfStatementImportId",
                table: "TrustStatementImports",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImportFingerprint",
                table: "TrustStatementImports",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceEvidenceKey",
                table: "TrustStatementImports",
                type: "TEXT",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceFileHash",
                table: "TrustStatementImports",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceFileName",
                table: "TrustStatementImports",
                type: "TEXT",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SourceFileSizeBytes",
                table: "TrustStatementImports",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrustStatementImports_TenantId_DuplicateOfStatementImportId",
                table: "TrustStatementImports",
                columns: new[] { "TenantId", "DuplicateOfStatementImportId" });

            migrationBuilder.CreateIndex(
                name: "IX_TrustStatementImports_TenantId_SourceFileHash",
                table: "TrustStatementImports",
                columns: new[] { "TenantId", "SourceFileHash" });

            migrationBuilder.CreateIndex(
                name: "IX_TrustStatementImports_TenantId_TrustAccountId_PeriodStart_PeriodEnd_ImportFingerprint",
                table: "TrustStatementImports",
                columns: new[] { "TenantId", "TrustAccountId", "PeriodStart", "PeriodEnd", "ImportFingerprint" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TrustStatementImports_TenantId_DuplicateOfStatementImportId",
                table: "TrustStatementImports");

            migrationBuilder.DropIndex(
                name: "IX_TrustStatementImports_TenantId_SourceFileHash",
                table: "TrustStatementImports");

            migrationBuilder.DropIndex(
                name: "IX_TrustStatementImports_TenantId_TrustAccountId_PeriodStart_PeriodEnd_ImportFingerprint",
                table: "TrustStatementImports");

            migrationBuilder.DropColumn(
                name: "DuplicateOfStatementImportId",
                table: "TrustStatementImports");

            migrationBuilder.DropColumn(
                name: "ImportFingerprint",
                table: "TrustStatementImports");

            migrationBuilder.DropColumn(
                name: "SourceEvidenceKey",
                table: "TrustStatementImports");

            migrationBuilder.DropColumn(
                name: "SourceFileHash",
                table: "TrustStatementImports");

            migrationBuilder.DropColumn(
                name: "SourceFileName",
                table: "TrustStatementImports");

            migrationBuilder.DropColumn(
                name: "SourceFileSizeBytes",
                table: "TrustStatementImports");
        }
    }
}
