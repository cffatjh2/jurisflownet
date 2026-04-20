using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JurisFlow.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddPhase5SchemaAndPrivacyHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var activeLeadEmailIndexFilter = ActiveProvider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase)
                ? "\"NormalizedEmail\" IS NOT NULL AND NOT \"IsArchived\""
                : "\"NormalizedEmail\" IS NOT NULL AND \"IsArchived\" = 0";

            migrationBuilder.DropIndex(
                name: "IX_Leads_TenantId_NormalizedEmail",
                table: "Leads");

            migrationBuilder.CreateIndex(
                name: "IX_Leads_TenantId_NormalizedEmail",
                table: "Leads",
                columns: new[] { "TenantId", "NormalizedEmail" },
                unique: true,
                filter: activeLeadEmailIndexFilter);

            migrationBuilder.CreateIndex(
                name: "IX_IntakeSubmissions_TenantId_IntakeFormId_CreatedAt",
                table: "IntakeSubmissions",
                columns: new[] { "TenantId", "IntakeFormId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_IntakeSubmissions_TenantId_Status_CreatedAt",
                table: "IntakeSubmissions",
                columns: new[] { "TenantId", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_IntakeForms_TenantId_CreatedAt",
                table: "IntakeForms",
                columns: new[] { "TenantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_IntakeForms_TenantId_Slug",
                table: "IntakeForms",
                columns: new[] { "TenantId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConflictResults_TenantId_ConflictCheckId_CreatedAt",
                table: "ConflictResults",
                columns: new[] { "TenantId", "ConflictCheckId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ConflictChecks_TenantId_CreatedAt",
                table: "ConflictChecks",
                columns: new[] { "TenantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ConflictChecks_TenantId_Status_CreatedAt",
                table: "ConflictChecks",
                columns: new[] { "TenantId", "Status", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Leads_TenantId_NormalizedEmail",
                table: "Leads");

            migrationBuilder.DropIndex(
                name: "IX_IntakeSubmissions_TenantId_IntakeFormId_CreatedAt",
                table: "IntakeSubmissions");

            migrationBuilder.DropIndex(
                name: "IX_IntakeSubmissions_TenantId_Status_CreatedAt",
                table: "IntakeSubmissions");

            migrationBuilder.DropIndex(
                name: "IX_IntakeForms_TenantId_CreatedAt",
                table: "IntakeForms");

            migrationBuilder.DropIndex(
                name: "IX_IntakeForms_TenantId_Slug",
                table: "IntakeForms");

            migrationBuilder.DropIndex(
                name: "IX_ConflictResults_TenantId_ConflictCheckId_CreatedAt",
                table: "ConflictResults");

            migrationBuilder.DropIndex(
                name: "IX_ConflictChecks_TenantId_CreatedAt",
                table: "ConflictChecks");

            migrationBuilder.DropIndex(
                name: "IX_ConflictChecks_TenantId_Status_CreatedAt",
                table: "ConflictChecks");

            migrationBuilder.CreateIndex(
                name: "IX_Leads_TenantId_NormalizedEmail",
                table: "Leads",
                columns: new[] { "TenantId", "NormalizedEmail" });
        }
    }
}
