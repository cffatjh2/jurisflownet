using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JurisFlow.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddIntegrationReviewQueueUniqueSourceKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_IntegrationReviewQueueItems_TenantId_ProviderKey_ItemType_SourceType_SourceId",
                table: "IntegrationReviewQueueItems",
                columns: new[] { "TenantId", "ProviderKey", "ItemType", "SourceType", "SourceId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_IntegrationReviewQueueItems_TenantId_ProviderKey_ItemType_SourceType_SourceId",
                table: "IntegrationReviewQueueItems");
        }
    }
}
