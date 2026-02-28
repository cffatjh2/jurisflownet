using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JurisFlow.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddDataProtection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EncryptionIv",
                table: "DocumentVersions",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EncryptionKeyId",
                table: "DocumentVersions",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EncryptionTag",
                table: "DocumentVersions",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsEncrypted",
                table: "DocumentVersions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "EncryptionAlgorithm",
                table: "Documents",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EncryptionIv",
                table: "Documents",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EncryptionKeyId",
                table: "Documents",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EncryptionTag",
                table: "Documents",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsEncrypted",
                table: "Documents",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Hash",
                table: "AuditLogs",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HashAlgorithm",
                table: "AuditLogs",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreviousHash",
                table: "AuditLogs",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "Sequence",
                table: "AuditLogs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Sequence",
                table: "AuditLogs",
                column: "Sequence");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_Sequence",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "EncryptionIv",
                table: "DocumentVersions");

            migrationBuilder.DropColumn(
                name: "EncryptionKeyId",
                table: "DocumentVersions");

            migrationBuilder.DropColumn(
                name: "EncryptionTag",
                table: "DocumentVersions");

            migrationBuilder.DropColumn(
                name: "IsEncrypted",
                table: "DocumentVersions");

            migrationBuilder.DropColumn(
                name: "EncryptionAlgorithm",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "EncryptionIv",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "EncryptionKeyId",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "EncryptionTag",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "IsEncrypted",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "Hash",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "HashAlgorithm",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "PreviousHash",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "Sequence",
                table: "AuditLogs");
        }
    }
}
