using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JurisFlow.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddFirmStructureDefaultUniqueIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EncryptionAlgorithm",
                table: "DocumentVersions",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Offices_TenantId_EntityId_IsDefault",
                table: "Offices",
                columns: new[] { "TenantId", "EntityId", "IsDefault" },
                unique: true,
                filter: "IsDefault = 1");

            migrationBuilder.CreateIndex(
                name: "IX_FirmEntities_TenantId_IsDefault",
                table: "FirmEntities",
                columns: new[] { "TenantId", "IsDefault" },
                unique: true,
                filter: "IsDefault = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Offices_TenantId_EntityId_IsDefault",
                table: "Offices");

            migrationBuilder.DropIndex(
                name: "IX_FirmEntities_TenantId_IsDefault",
                table: "FirmEntities");

            migrationBuilder.DropColumn(
                name: "EncryptionAlgorithm",
                table: "DocumentVersions");
        }
    }
}
