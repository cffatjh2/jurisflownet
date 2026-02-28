using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JurisFlow.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddSignatureExpiryAndCalendarUpdates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiredAt",
                table: "SignatureRequests",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastReminderAt",
                table: "SignatureRequests",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReminderCount",
                table: "SignatureRequests",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "VerificationCompletedAt",
                table: "SignatureRequests",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerificationMethod",
                table: "SignatureRequests",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerificationNotes",
                table: "SignatureRequests",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerificationStatus",
                table: "SignatureRequests",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SenderType",
                table: "ClientMessages",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SenderUserId",
                table: "ClientMessages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DocumentComments",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    DocumentId = table.Column<string>(type: "TEXT", nullable: false),
                    Body = table.Column<string>(type: "TEXT", nullable: false),
                    AuthorUserId = table.Column<string>(type: "TEXT", nullable: true),
                    AuthorClientId = table.Column<string>(type: "TEXT", nullable: true),
                    AuthorType = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentComments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DocumentShares",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    DocumentId = table.Column<string>(type: "TEXT", nullable: false),
                    ClientId = table.Column<string>(type: "TEXT", nullable: false),
                    SharedByUserId = table.Column<string>(type: "TEXT", nullable: true),
                    SharedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CanView = table.Column<bool>(type: "INTEGER", nullable: false),
                    CanDownload = table.Column<bool>(type: "INTEGER", nullable: false),
                    CanComment = table.Column<bool>(type: "INTEGER", nullable: false),
                    CanUpload = table.Column<bool>(type: "INTEGER", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Note = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentShares", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SignatureAuditEntries",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    SignatureRequestId = table.Column<string>(type: "TEXT", nullable: false),
                    Action = table.Column<string>(type: "TEXT", nullable: false),
                    ActorType = table.Column<string>(type: "TEXT", nullable: true),
                    ActorId = table.Column<string>(type: "TEXT", nullable: true),
                    ActorEmail = table.Column<string>(type: "TEXT", nullable: true),
                    IpAddress = table.Column<string>(type: "TEXT", nullable: true),
                    UserAgent = table.Column<string>(type: "TEXT", nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SignatureAuditEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentComments_DocumentId_CreatedAt",
                table: "DocumentComments",
                columns: new[] { "DocumentId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentShares_ClientId_ExpiresAt",
                table: "DocumentShares",
                columns: new[] { "ClientId", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentShares_DocumentId_ClientId",
                table: "DocumentShares",
                columns: new[] { "DocumentId", "ClientId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SignatureAuditEntries_SignatureRequestId_CreatedAt",
                table: "SignatureAuditEntries",
                columns: new[] { "SignatureRequestId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentComments");

            migrationBuilder.DropTable(
                name: "DocumentShares");

            migrationBuilder.DropTable(
                name: "SignatureAuditEntries");

            migrationBuilder.DropColumn(
                name: "ExpiredAt",
                table: "SignatureRequests");

            migrationBuilder.DropColumn(
                name: "LastReminderAt",
                table: "SignatureRequests");

            migrationBuilder.DropColumn(
                name: "ReminderCount",
                table: "SignatureRequests");

            migrationBuilder.DropColumn(
                name: "VerificationCompletedAt",
                table: "SignatureRequests");

            migrationBuilder.DropColumn(
                name: "VerificationMethod",
                table: "SignatureRequests");

            migrationBuilder.DropColumn(
                name: "VerificationNotes",
                table: "SignatureRequests");

            migrationBuilder.DropColumn(
                name: "VerificationStatus",
                table: "SignatureRequests");

            migrationBuilder.DropColumn(
                name: "SenderType",
                table: "ClientMessages");

            migrationBuilder.DropColumn(
                name: "SenderUserId",
                table: "ClientMessages");
        }
    }
}
