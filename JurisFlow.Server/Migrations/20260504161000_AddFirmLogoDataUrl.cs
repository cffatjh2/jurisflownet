using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JurisFlow.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddFirmLogoDataUrl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LogoDataUrl",
                table: "FirmSettings",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LogoDataUrl",
                table: "FirmSettings");
        }
    }
}
