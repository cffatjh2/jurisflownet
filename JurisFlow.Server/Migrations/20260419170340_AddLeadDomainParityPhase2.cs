using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JurisFlow.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddLeadDomainParityPhase2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var dateTimeType = ActiveProvider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase)
                ? "timestamp with time zone"
                : "TEXT";
            var stringType = ActiveProvider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase)
                ? "character varying"
                : "TEXT";
            var string64Type = ActiveProvider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase)
                ? "character varying(64)"
                : "TEXT";
            var string320Type = ActiveProvider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase)
                ? "character varying(320)"
                : "TEXT";
            var boolType = ActiveProvider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase)
                ? "boolean"
                : "INTEGER";

            migrationBuilder.AddColumn<DateTime>(
                name: "ArchivedAt",
                table: "Leads",
                type: dateTimeType,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ArchivedByName",
                table: "Leads",
                type: stringType,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ArchivedByUserId",
                table: "Leads",
                type: stringType,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedBySource",
                table: "Leads",
                type: string64Type,
                maxLength: 64,
                nullable: false,
                defaultValue: "Manual");

            migrationBuilder.AddColumn<bool>(
                name: "IsArchived",
                table: "Leads",
                type: boolType,
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedEmail",
                table: "Leads",
                type: string320Type,
                maxLength: 320,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "LeadStatusHistories",
                columns: table => new
                {
                    Id = table.Column<string>(type: stringType, nullable: false),
                    LeadId = table.Column<string>(type: stringType, nullable: false),
                    PreviousStatus = table.Column<string>(type: stringType, nullable: false),
                    NewStatus = table.Column<string>(type: stringType, nullable: false),
                    Notes = table.Column<string>(type: stringType, nullable: true),
                    ChangedByUserId = table.Column<string>(type: stringType, nullable: true),
                    ChangedByName = table.Column<string>(type: stringType, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: dateTimeType, nullable: false),
                    TenantId = table.Column<string>(type: string64Type, maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeadStatusHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LeadStatusHistories_Leads_LeadId",
                        column: x => x.LeadId,
                        principalTable: "Leads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Leads_TenantId_NormalizedEmail",
                table: "Leads",
                columns: new[] { "TenantId", "NormalizedEmail" });

            migrationBuilder.CreateIndex(
                name: "IX_Leads_TenantId_Status_CreatedAt",
                table: "Leads",
                columns: new[] { "TenantId", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LeadStatusHistories_LeadId_CreatedAt",
                table: "LeadStatusHistories",
                columns: new[] { "LeadId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LeadStatusHistories_TenantId",
                table: "LeadStatusHistories",
                column: "TenantId");

            migrationBuilder.Sql(
                """
                UPDATE "Leads"
                SET
                    "NormalizedEmail" = NULLIF(lower(trim(coalesce("Email", ''))), ''),
                    "CreatedBySource" = CASE
                        WHEN EXISTS (
                            SELECT 1
                            FROM "IntakeSubmissions" AS s
                            WHERE s."LeadId" = "Leads"."Id")
                        THEN 'Intake'
                        ELSE 'Manual'
                    END,
                    "Status" = CASE
                        WHEN "Status" IS NULL OR trim("Status") = '' THEN 'New'
                        WHEN lower(trim("Status")) IN ('new', 'new inquiry') THEN 'New'
                        WHEN lower(trim("Status")) IN ('contacted', 'initial contact', 'qualified') THEN 'Contacted'
                        WHEN lower(trim("Status")) IN ('scheduled', 'consultation scheduled') THEN 'Scheduled'
                        WHEN lower(trim("Status")) IN ('consulted', 'consultation', 'consultation completed') THEN 'Consulted'
                        WHEN lower(trim("Status")) IN ('proposal', 'proposal sent') THEN 'Proposal'
                        WHEN lower(trim("Status")) = 'retained' THEN 'Retained'
                        WHEN lower(trim("Status")) IN ('declined', 'lost') THEN 'Lost'
                        ELSE 'New'
                    END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LeadStatusHistories");

            migrationBuilder.DropIndex(
                name: "IX_Leads_TenantId_NormalizedEmail",
                table: "Leads");

            migrationBuilder.DropIndex(
                name: "IX_Leads_TenantId_Status_CreatedAt",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "ArchivedByName",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "ArchivedByUserId",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "CreatedBySource",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "IsArchived",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "NormalizedEmail",
                table: "Leads");
        }
    }
}
