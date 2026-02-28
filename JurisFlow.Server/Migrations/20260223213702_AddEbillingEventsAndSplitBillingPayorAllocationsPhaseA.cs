using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JurisFlow.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddEbillingEventsAndSplitBillingPayorAllocationsPhaseA : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InvoicePayorAllocationId",
                table: "BillingPaymentAllocations",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayorClientId",
                table: "BillingPaymentAllocations",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BillingEbillingResultEvents",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    TransmissionId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ProviderKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ExternalTransmissionId = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    ExternalEventId = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    EventType = table.Column<string>(type: "TEXT", maxLength: 48, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    InvoiceId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    MatterId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ClientId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    PayorClientId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ResultCode = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ResultMessage = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    ErrorCode = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ErrorCategory = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    IsFinal = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsRetryable = table.Column<bool>(type: "INTEGER", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    RecordedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingEbillingResultEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BillingEbillingTransmissions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    ProviderKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    InvoiceId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    MatterId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ClientId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    PayorClientId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    PrebillBatchId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Format = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ExternalTransmissionId = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    CorrelationId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Reference = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    ErrorCode = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RequestPayloadJson = table.Column<string>(type: "TEXT", nullable: true),
                    ResponsePayloadJson = table.Column<string>(type: "TEXT", nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingEbillingTransmissions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InvoiceLinePayorAllocations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    InvoiceId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    InvoiceLineItemId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    InvoicePayorAllocationId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    PayorClientId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ResponsibilityType = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    Percent = table.Column<decimal>(type: "TEXT", nullable: true),
                    Amount = table.Column<decimal>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    TaskCode = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    ActivityCode = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    ExpenseCode = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    EbillingProfileJson = table.Column<string>(type: "TEXT", nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceLinePayorAllocations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InvoicePayorAllocations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    InvoiceId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    PayorClientId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ResponsibilityType = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    Percent = table.Column<decimal>(type: "TEXT", nullable: true),
                    AmountCap = table.Column<decimal>(type: "TEXT", nullable: true),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    IsPrimary = table.Column<bool>(type: "INTEGER", nullable: false),
                    AllocatedAmount = table.Column<decimal>(type: "TEXT", nullable: false),
                    Terms = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Reference = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    PurchaseOrder = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    EbillingProfileJson = table.Column<string>(type: "TEXT", nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoicePayorAllocations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BillingPaymentAllocations_TenantId_InvoicePayorAllocationId_Status",
                table: "BillingPaymentAllocations",
                columns: new[] { "TenantId", "InvoicePayorAllocationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingPaymentAllocations_TenantId_PayorClientId_Status_AppliedAt",
                table: "BillingPaymentAllocations",
                columns: new[] { "TenantId", "PayorClientId", "Status", "AppliedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingEbillingResultEvents_TenantId",
                table: "BillingEbillingResultEvents",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_BillingEbillingResultEvents_TenantId_InvoiceId_OccurredAt",
                table: "BillingEbillingResultEvents",
                columns: new[] { "TenantId", "InvoiceId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingEbillingResultEvents_TenantId_ProviderKey_ExternalEventId",
                table: "BillingEbillingResultEvents",
                columns: new[] { "TenantId", "ProviderKey", "ExternalEventId" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingEbillingResultEvents_TenantId_ProviderKey_Status_OccurredAt",
                table: "BillingEbillingResultEvents",
                columns: new[] { "TenantId", "ProviderKey", "Status", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingEbillingResultEvents_TenantId_TransmissionId_OccurredAt",
                table: "BillingEbillingResultEvents",
                columns: new[] { "TenantId", "TransmissionId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingEbillingTransmissions_TenantId",
                table: "BillingEbillingTransmissions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_BillingEbillingTransmissions_TenantId_InvoiceId_CreatedAt",
                table: "BillingEbillingTransmissions",
                columns: new[] { "TenantId", "InvoiceId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingEbillingTransmissions_TenantId_ProviderKey_ExternalTransmissionId",
                table: "BillingEbillingTransmissions",
                columns: new[] { "TenantId", "ProviderKey", "ExternalTransmissionId" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingEbillingTransmissions_TenantId_ProviderKey_Status_SubmittedAt",
                table: "BillingEbillingTransmissions",
                columns: new[] { "TenantId", "ProviderKey", "Status", "SubmittedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceLinePayorAllocations_TenantId",
                table: "InvoiceLinePayorAllocations",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceLinePayorAllocations_TenantId_InvoiceId_InvoiceLineItemId_Status",
                table: "InvoiceLinePayorAllocations",
                columns: new[] { "TenantId", "InvoiceId", "InvoiceLineItemId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceLinePayorAllocations_TenantId_InvoicePayorAllocationId",
                table: "InvoiceLinePayorAllocations",
                columns: new[] { "TenantId", "InvoicePayorAllocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceLinePayorAllocations_TenantId_PayorClientId_Status",
                table: "InvoiceLinePayorAllocations",
                columns: new[] { "TenantId", "PayorClientId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoicePayorAllocations_TenantId",
                table: "InvoicePayorAllocations",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoicePayorAllocations_TenantId_InvoiceId_PayorClientId_IsPrimary",
                table: "InvoicePayorAllocations",
                columns: new[] { "TenantId", "InvoiceId", "PayorClientId", "IsPrimary" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoicePayorAllocations_TenantId_InvoiceId_Status_Priority",
                table: "InvoicePayorAllocations",
                columns: new[] { "TenantId", "InvoiceId", "Status", "Priority" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BillingEbillingResultEvents");

            migrationBuilder.DropTable(
                name: "BillingEbillingTransmissions");

            migrationBuilder.DropTable(
                name: "InvoiceLinePayorAllocations");

            migrationBuilder.DropTable(
                name: "InvoicePayorAllocations");

            migrationBuilder.DropIndex(
                name: "IX_BillingPaymentAllocations_TenantId_InvoicePayorAllocationId_Status",
                table: "BillingPaymentAllocations");

            migrationBuilder.DropIndex(
                name: "IX_BillingPaymentAllocations_TenantId_PayorClientId_Status_AppliedAt",
                table: "BillingPaymentAllocations");

            migrationBuilder.DropColumn(
                name: "InvoicePayorAllocationId",
                table: "BillingPaymentAllocations");

            migrationBuilder.DropColumn(
                name: "PayorClientId",
                table: "BillingPaymentAllocations");
        }
    }
}
