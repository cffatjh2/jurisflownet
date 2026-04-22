using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JurisFlow.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddMatterResponsibleEmployee : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ResponsibleEmployeeId",
                table: "Matters",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Matters_TenantId_ResponsibleEmployeeId_OpenDate",
                table: "Matters",
                columns: new[] { "TenantId", "ResponsibleEmployeeId", "OpenDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Matters_TenantId_ResponsibleEmployeeId_OpenDate",
                table: "Matters");

            migrationBuilder.DropColumn(
                name: "ResponsibleEmployeeId",
                table: "Matters");
        }
    }
}
