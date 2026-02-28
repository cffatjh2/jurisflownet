using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JurisFlow.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddLegalBillingEngineFoundationPhase2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BillingLedgerEntries",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    LedgerDomain = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    LedgerBucket = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    EntryType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", nullable: false),
                    MatterId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ClientId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    InvoiceId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    InvoiceLineItemId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    PaymentTransactionId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    PrebillBatchId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    PrebillLineId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    TrustTransactionId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ReversalOfLedgerEntryId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    CorrelationKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    PostedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    PostedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingLedgerEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BillingPaymentAllocations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    PaymentTransactionId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    InvoiceId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    InvoiceLineItemId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ClientId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    MatterId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Amount = table.Column<decimal>(type: "TEXT", nullable: false),
                    AllocationType = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    LedgerEntryId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ReversalOfAllocationId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    AppliedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    AppliedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingPaymentAllocations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BillingPrebillBatches",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    MatterId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ClientId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    PolicyId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    RateCardId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    InvoiceId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    ArrangementType = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    PeriodStart = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PeriodEnd = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Subtotal = table.Column<decimal>(type: "TEXT", nullable: false),
                    TaxTotal = table.Column<decimal>(type: "TEXT", nullable: false),
                    DiscountTotal = table.Column<decimal>(type: "TEXT", nullable: false),
                    WriteDownTotal = table.Column<decimal>(type: "TEXT", nullable: false),
                    Total = table.Column<decimal>(type: "TEXT", nullable: false),
                    TaxPolicyCode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    LedesFormat = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    GeneratedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    GeneratedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SubmittedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ApprovedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RejectedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    RejectedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReviewNotes = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingPrebillBatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BillingPrebillLines",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    PrebillBatchId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    MatterId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ClientId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    LineType = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    SourceType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SourceId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    TimekeeperId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    TimekeeperRole = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    ServiceDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Description = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    TaskCode = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    ActivityCode = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    ExpenseCode = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    Quantity = table.Column<decimal>(type: "TEXT", nullable: false),
                    Rate = table.Column<decimal>(type: "TEXT", nullable: false),
                    ProposedAmount = table.Column<decimal>(type: "TEXT", nullable: false),
                    ApprovedAmount = table.Column<decimal>(type: "TEXT", nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "TEXT", nullable: false),
                    WriteDownAmount = table.Column<decimal>(type: "TEXT", nullable: false),
                    TaxAmount = table.Column<decimal>(type: "TEXT", nullable: false),
                    TaxCode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    ThirdPartyPayorClientId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    ReviewerNotes = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    SplitAllocationJson = table.Column<string>(type: "TEXT", nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingPrebillLines", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BillingRateCardEntries",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    RateCardId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    EntryType = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    TimekeeperRole = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    EmployeeId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ClientId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    MatterId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    TaskCode = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    ActivityCode = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    ExpenseCode = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    Unit = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    Rate = table.Column<decimal>(type: "TEXT", nullable: false),
                    MinimumUnits = table.Column<decimal>(type: "TEXT", nullable: true),
                    MaximumUnits = table.Column<decimal>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingRateCardEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BillingRateCards",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    Scope = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    ClientId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    MatterId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EffectiveTo = table.Column<DateTime>(type: "TEXT", nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingRateCards", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MatterBillingPolicies",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    MatterId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ClientId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ThirdPartyPayorClientId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ArrangementType = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    BillingCycle = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    RateCardId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    TaxPolicyMode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    TrustHandlingMode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CollectionPolicy = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    EbillingFormat = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    EbillingStatus = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    RequirePrebillApproval = table.Column<bool>(type: "INTEGER", nullable: false),
                    EnforceUtbmsCodes = table.Column<bool>(type: "INTEGER", nullable: false),
                    EnforceTrustOperatingSplit = table.Column<bool>(type: "INTEGER", nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EffectiveTo = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    TaxPolicyJson = table.Column<string>(type: "TEXT", nullable: true),
                    SplitBillingJson = table.Column<string>(type: "TEXT", nullable: true),
                    EbillingProfileJson = table.Column<string>(type: "TEXT", nullable: true),
                    CollectionPolicyJson = table.Column<string>(type: "TEXT", nullable: true),
                    TrustPolicyJson = table.Column<string>(type: "TEXT", nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatterBillingPolicies", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BillingLedgerEntries_TenantId",
                table: "BillingLedgerEntries",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_BillingLedgerEntries_TenantId_CorrelationKey",
                table: "BillingLedgerEntries",
                columns: new[] { "TenantId", "CorrelationKey" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingLedgerEntries_TenantId_InvoiceId_PaymentTransactionId_PostedAt",
                table: "BillingLedgerEntries",
                columns: new[] { "TenantId", "InvoiceId", "PaymentTransactionId", "PostedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingLedgerEntries_TenantId_LedgerDomain_LedgerBucket_PostedAt",
                table: "BillingLedgerEntries",
                columns: new[] { "TenantId", "LedgerDomain", "LedgerBucket", "PostedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingLedgerEntries_TenantId_ReversalOfLedgerEntryId",
                table: "BillingLedgerEntries",
                columns: new[] { "TenantId", "ReversalOfLedgerEntryId" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingPaymentAllocations_TenantId",
                table: "BillingPaymentAllocations",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_BillingPaymentAllocations_TenantId_InvoiceId_InvoiceLineItemId_Status",
                table: "BillingPaymentAllocations",
                columns: new[] { "TenantId", "InvoiceId", "InvoiceLineItemId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingPaymentAllocations_TenantId_PaymentTransactionId_Status_AppliedAt",
                table: "BillingPaymentAllocations",
                columns: new[] { "TenantId", "PaymentTransactionId", "Status", "AppliedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingPrebillBatches_TenantId",
                table: "BillingPrebillBatches",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_BillingPrebillBatches_TenantId_InvoiceId",
                table: "BillingPrebillBatches",
                columns: new[] { "TenantId", "InvoiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingPrebillBatches_TenantId_MatterId_Status_GeneratedAt",
                table: "BillingPrebillBatches",
                columns: new[] { "TenantId", "MatterId", "Status", "GeneratedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingPrebillLines_TenantId",
                table: "BillingPrebillLines",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_BillingPrebillLines_TenantId_MatterId_ClientId_ServiceDate",
                table: "BillingPrebillLines",
                columns: new[] { "TenantId", "MatterId", "ClientId", "ServiceDate" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingPrebillLines_TenantId_PrebillBatchId_Status",
                table: "BillingPrebillLines",
                columns: new[] { "TenantId", "PrebillBatchId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingPrebillLines_TenantId_SourceType_SourceId",
                table: "BillingPrebillLines",
                columns: new[] { "TenantId", "SourceType", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingRateCardEntries_TenantId",
                table: "BillingRateCardEntries",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_BillingRateCardEntries_TenantId_RateCardId_EntryType_Priority",
                table: "BillingRateCardEntries",
                columns: new[] { "TenantId", "RateCardId", "EntryType", "Priority" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingRateCardEntries_TenantId_Status_TaskCode_ActivityCode_ExpenseCode",
                table: "BillingRateCardEntries",
                columns: new[] { "TenantId", "Status", "TaskCode", "ActivityCode", "ExpenseCode" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingRateCards_TenantId",
                table: "BillingRateCards",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_BillingRateCards_TenantId_Name_EffectiveFrom",
                table: "BillingRateCards",
                columns: new[] { "TenantId", "Name", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingRateCards_TenantId_Scope_ClientId_MatterId_Status",
                table: "BillingRateCards",
                columns: new[] { "TenantId", "Scope", "ClientId", "MatterId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_MatterBillingPolicies_TenantId",
                table: "MatterBillingPolicies",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_MatterBillingPolicies_TenantId_ClientId_ArrangementType_Status",
                table: "MatterBillingPolicies",
                columns: new[] { "TenantId", "ClientId", "ArrangementType", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_MatterBillingPolicies_TenantId_MatterId_Status",
                table: "MatterBillingPolicies",
                columns: new[] { "TenantId", "MatterId", "Status" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BillingLedgerEntries");

            migrationBuilder.DropTable(
                name: "BillingPaymentAllocations");

            migrationBuilder.DropTable(
                name: "BillingPrebillBatches");

            migrationBuilder.DropTable(
                name: "BillingPrebillLines");

            migrationBuilder.DropTable(
                name: "BillingRateCardEntries");

            migrationBuilder.DropTable(
                name: "BillingRateCards");

            migrationBuilder.DropTable(
                name: "MatterBillingPolicies");
        }
    }
}
