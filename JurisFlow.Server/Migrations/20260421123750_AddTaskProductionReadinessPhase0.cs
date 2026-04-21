using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JurisFlow.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskProductionReadinessPhase0 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CreatedByUserId",
                table: "Tasks",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RowVersion",
                table: "Tasks",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "TaskTemplates",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Definition = table.Column<string>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskTemplates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_TenantId_AssignedEmployeeId_Status_UpdatedAt",
                table: "Tasks",
                columns: new[] { "TenantId", "AssignedEmployeeId", "Status", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_TenantId_CreatedByUserId_UpdatedAt",
                table: "Tasks",
                columns: new[] { "TenantId", "CreatedByUserId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_TenantId_ReminderSent_ReminderAt",
                table: "Tasks",
                columns: new[] { "TenantId", "ReminderSent", "ReminderAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_TenantId_Status_DueDate",
                table: "Tasks",
                columns: new[] { "TenantId", "Status", "DueDate" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Tasks_ReminderAt_OnOrBeforeDueDate",
                table: "Tasks",
                sql: "\"ReminderAt\" IS NULL OR \"DueDate\" IS NULL OR \"ReminderAt\" <= \"DueDate\"");

            migrationBuilder.CreateIndex(
                name: "IX_TaskTemplates_TenantId",
                table: "TaskTemplates",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskTemplates_TenantId_IsActive_Category_Name",
                table: "TaskTemplates",
                columns: new[] { "TenantId", "IsActive", "Category", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_TaskTemplates_TenantId_UpdatedAt",
                table: "TaskTemplates",
                columns: new[] { "TenantId", "UpdatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TaskTemplates");

            migrationBuilder.DropIndex(
                name: "IX_Tasks_TenantId_AssignedEmployeeId_Status_UpdatedAt",
                table: "Tasks");

            migrationBuilder.DropIndex(
                name: "IX_Tasks_TenantId_CreatedByUserId_UpdatedAt",
                table: "Tasks");

            migrationBuilder.DropIndex(
                name: "IX_Tasks_TenantId_ReminderSent_ReminderAt",
                table: "Tasks");

            migrationBuilder.DropIndex(
                name: "IX_Tasks_TenantId_Status_DueDate",
                table: "Tasks");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Tasks_ReminderAt_OnOrBeforeDueDate",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Tasks");
        }
    }
}
