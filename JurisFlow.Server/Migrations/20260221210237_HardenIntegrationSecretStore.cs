using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JurisFlow.Server.Migrations
{
    /// <inheritdoc />
    public partial class HardenIntegrationSecretStore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EncryptionKeyId",
                table: "IntegrationSecrets",
                type: "TEXT",
                maxLength: 128,
                nullable: false,
                defaultValue: "legacy");

            migrationBuilder.AddColumn<string>(
                name: "EncryptionProvider",
                table: "IntegrationSecrets",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                defaultValue: "legacy");

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationSecrets_EncryptionKeyId",
                table: "IntegrationSecrets",
                column: "EncryptionKeyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_IntegrationSecrets_EncryptionKeyId",
                table: "IntegrationSecrets");

            migrationBuilder.DropColumn(
                name: "EncryptionKeyId",
                table: "IntegrationSecrets");

            migrationBuilder.DropColumn(
                name: "EncryptionProvider",
                table: "IntegrationSecrets");
        }
    }
}
