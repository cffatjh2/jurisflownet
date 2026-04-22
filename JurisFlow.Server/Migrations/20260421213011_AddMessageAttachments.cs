using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JurisFlow.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddMessageAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MessageAttachments",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    MessageType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    MessageId = table.Column<string>(type: "TEXT", nullable: false),
                    StoredFileName = table.Column<string>(type: "TEXT", maxLength: 260, nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 260, nullable: false),
                    FilePath = table.Column<string>(type: "TEXT", nullable: false),
                    MimeType = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Size = table.Column<long>(type: "INTEGER", nullable: false),
                    ClientId = table.Column<string>(type: "TEXT", nullable: true),
                    MessageEmployeeId = table.Column<string>(type: "TEXT", nullable: true),
                    SenderUserId = table.Column<string>(type: "TEXT", nullable: true),
                    SenderEmployeeId = table.Column<string>(type: "TEXT", nullable: true),
                    RecipientEmployeeId = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MessageAttachments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MessageAttachments_TenantId",
                table: "MessageAttachments",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_MessageAttachments_TenantId_ClientId_StoredFileName",
                table: "MessageAttachments",
                columns: new[] { "TenantId", "ClientId", "StoredFileName" });

            migrationBuilder.CreateIndex(
                name: "IX_MessageAttachments_TenantId_MessageEmployeeId_StoredFileName",
                table: "MessageAttachments",
                columns: new[] { "TenantId", "MessageEmployeeId", "StoredFileName" });

            migrationBuilder.CreateIndex(
                name: "IX_MessageAttachments_TenantId_SenderEmployeeId_RecipientEmployeeId_StoredFileName",
                table: "MessageAttachments",
                columns: new[] { "TenantId", "SenderEmployeeId", "RecipientEmployeeId", "StoredFileName" });

            migrationBuilder.CreateIndex(
                name: "IX_MessageAttachments_TenantId_StoredFileName",
                table: "MessageAttachments",
                columns: new[] { "TenantId", "StoredFileName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MessageAttachments");
        }
    }
}
