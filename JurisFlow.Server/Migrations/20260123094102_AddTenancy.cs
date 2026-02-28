using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JurisFlow.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddTenancy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_Email",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_RetentionPolicies_EntityName",
                table: "RetentionPolicies");

            migrationBuilder.DropIndex(
                name: "IX_FirmSettings_Id",
                table: "FirmSettings");

            migrationBuilder.DropIndex(
                name: "IX_Employees_Email",
                table: "Employees");

            migrationBuilder.DropIndex(
                name: "IX_Clients_Email",
                table: "Clients");

            migrationBuilder.DropIndex(
                name: "IX_BillingSettings_Id",
                table: "BillingSettings");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "Users",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "TrustTransactions",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "TrustBankAccounts",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "TimeEntries",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "Tasks",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "StaffMessages",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "SmsTemplates",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "SmsReminders",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "SmsMessages",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "SignatureRequests",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "SignatureAuditEntries",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "RetentionPolicies",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "ResearchSessions",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "ReconciliationRecords",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "PaymentTransactions",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "PaymentPlans",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "OutboundEmails",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "OpposingParties",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "Offices",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "Notifications",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "MfaChallenges",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "Matters",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "Leads",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "Invoices",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "InvoiceLineItems",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "IntakeSubmissions",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "IntakeForms",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "Holidays",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "FirmSettings",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "FirmEntities",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "Expenses",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "Employees",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "EmailMessages",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "EmailAccounts",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "DocumentVersions",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "DocumentShares",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "Documents",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "DocumentContentTokens",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "DocumentContentIndexes",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "DocumentComments",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "Deadlines",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "CourtRules",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "ContractAnalyses",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "ConflictResults",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "ConflictChecks",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "ClientTrustLedgers",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "ClientStatusHistories",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "Clients",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "ClientMessages",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "CasePredictions",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "CalendarEvents",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "BillingSettings",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "BillingLocks",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "AuthSessions",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "AuditLogs",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "AppointmentRequests",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Tenants",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenants", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Users_TenantId",
                table: "Users",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_TenantId_Email",
                table: "Users",
                columns: new[] { "TenantId", "Email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrustTransactions_TenantId",
                table: "TrustTransactions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TrustBankAccounts_TenantId",
                table: "TrustBankAccounts",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TimeEntries_TenantId",
                table: "TimeEntries",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_TenantId",
                table: "Tasks",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_StaffMessages_TenantId",
                table: "StaffMessages",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_SmsTemplates_TenantId",
                table: "SmsTemplates",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_SmsReminders_TenantId",
                table: "SmsReminders",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_SmsMessages_TenantId",
                table: "SmsMessages",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_SignatureRequests_TenantId",
                table: "SignatureRequests",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_SignatureAuditEntries_TenantId",
                table: "SignatureAuditEntries",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_RetentionPolicies_TenantId",
                table: "RetentionPolicies",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_RetentionPolicies_TenantId_EntityName",
                table: "RetentionPolicies",
                columns: new[] { "TenantId", "EntityName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ResearchSessions_TenantId",
                table: "ResearchSessions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationRecords_TenantId",
                table: "ReconciliationRecords",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTransactions_TenantId",
                table: "PaymentTransactions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentPlans_TenantId",
                table: "PaymentPlans",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_OutboundEmails_TenantId",
                table: "OutboundEmails",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_OpposingParties_TenantId",
                table: "OpposingParties",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Offices_TenantId",
                table: "Offices",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_TenantId",
                table: "Notifications",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_MfaChallenges_TenantId",
                table: "MfaChallenges",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Matters_TenantId",
                table: "Matters",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Leads_TenantId",
                table: "Leads",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_TenantId",
                table: "Invoices",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceLineItems_TenantId",
                table: "InvoiceLineItems",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_IntakeSubmissions_TenantId",
                table: "IntakeSubmissions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_IntakeForms_TenantId",
                table: "IntakeForms",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Holidays_TenantId",
                table: "Holidays",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_FirmSettings_TenantId",
                table: "FirmSettings",
                column: "TenantId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FirmEntities_TenantId",
                table: "FirmEntities",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_TenantId",
                table: "Expenses",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_TenantId",
                table: "Employees",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_TenantId_Email",
                table: "Employees",
                columns: new[] { "TenantId", "Email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmailMessages_TenantId",
                table: "EmailMessages",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailAccounts_TenantId",
                table: "EmailAccounts",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentVersions_TenantId",
                table: "DocumentVersions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentShares_TenantId",
                table: "DocumentShares",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_TenantId",
                table: "Documents",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentContentTokens_TenantId",
                table: "DocumentContentTokens",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentContentIndexes_TenantId",
                table: "DocumentContentIndexes",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentComments_TenantId",
                table: "DocumentComments",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Deadlines_TenantId",
                table: "Deadlines",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_CourtRules_TenantId",
                table: "CourtRules",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractAnalyses_TenantId",
                table: "ContractAnalyses",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ConflictResults_TenantId",
                table: "ConflictResults",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ConflictChecks_TenantId",
                table: "ConflictChecks",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientTrustLedgers_TenantId",
                table: "ClientTrustLedgers",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientStatusHistories_TenantId",
                table: "ClientStatusHistories",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Clients_TenantId",
                table: "Clients",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Clients_TenantId_Email",
                table: "Clients",
                columns: new[] { "TenantId", "Email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClientMessages_TenantId",
                table: "ClientMessages",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_CasePredictions_TenantId",
                table: "CasePredictions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_CalendarEvents_TenantId",
                table: "CalendarEvents",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_BillingSettings_TenantId",
                table: "BillingSettings",
                column: "TenantId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillingLocks_TenantId",
                table: "BillingLocks",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AuthSessions_TenantId",
                table: "AuthSessions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_TenantId",
                table: "AuditLogs",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentRequests_TenantId",
                table: "AppointmentRequests",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_Slug",
                table: "Tenants",
                column: "Slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Tenants");

            migrationBuilder.DropIndex(
                name: "IX_Users_TenantId",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_TenantId_Email",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_TrustTransactions_TenantId",
                table: "TrustTransactions");

            migrationBuilder.DropIndex(
                name: "IX_TrustBankAccounts_TenantId",
                table: "TrustBankAccounts");

            migrationBuilder.DropIndex(
                name: "IX_TimeEntries_TenantId",
                table: "TimeEntries");

            migrationBuilder.DropIndex(
                name: "IX_Tasks_TenantId",
                table: "Tasks");

            migrationBuilder.DropIndex(
                name: "IX_StaffMessages_TenantId",
                table: "StaffMessages");

            migrationBuilder.DropIndex(
                name: "IX_SmsTemplates_TenantId",
                table: "SmsTemplates");

            migrationBuilder.DropIndex(
                name: "IX_SmsReminders_TenantId",
                table: "SmsReminders");

            migrationBuilder.DropIndex(
                name: "IX_SmsMessages_TenantId",
                table: "SmsMessages");

            migrationBuilder.DropIndex(
                name: "IX_SignatureRequests_TenantId",
                table: "SignatureRequests");

            migrationBuilder.DropIndex(
                name: "IX_SignatureAuditEntries_TenantId",
                table: "SignatureAuditEntries");

            migrationBuilder.DropIndex(
                name: "IX_RetentionPolicies_TenantId",
                table: "RetentionPolicies");

            migrationBuilder.DropIndex(
                name: "IX_RetentionPolicies_TenantId_EntityName",
                table: "RetentionPolicies");

            migrationBuilder.DropIndex(
                name: "IX_ResearchSessions_TenantId",
                table: "ResearchSessions");

            migrationBuilder.DropIndex(
                name: "IX_ReconciliationRecords_TenantId",
                table: "ReconciliationRecords");

            migrationBuilder.DropIndex(
                name: "IX_PaymentTransactions_TenantId",
                table: "PaymentTransactions");

            migrationBuilder.DropIndex(
                name: "IX_PaymentPlans_TenantId",
                table: "PaymentPlans");

            migrationBuilder.DropIndex(
                name: "IX_OutboundEmails_TenantId",
                table: "OutboundEmails");

            migrationBuilder.DropIndex(
                name: "IX_OpposingParties_TenantId",
                table: "OpposingParties");

            migrationBuilder.DropIndex(
                name: "IX_Offices_TenantId",
                table: "Offices");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_TenantId",
                table: "Notifications");

            migrationBuilder.DropIndex(
                name: "IX_MfaChallenges_TenantId",
                table: "MfaChallenges");

            migrationBuilder.DropIndex(
                name: "IX_Matters_TenantId",
                table: "Matters");

            migrationBuilder.DropIndex(
                name: "IX_Leads_TenantId",
                table: "Leads");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_TenantId",
                table: "Invoices");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceLineItems_TenantId",
                table: "InvoiceLineItems");

            migrationBuilder.DropIndex(
                name: "IX_IntakeSubmissions_TenantId",
                table: "IntakeSubmissions");

            migrationBuilder.DropIndex(
                name: "IX_IntakeForms_TenantId",
                table: "IntakeForms");

            migrationBuilder.DropIndex(
                name: "IX_Holidays_TenantId",
                table: "Holidays");

            migrationBuilder.DropIndex(
                name: "IX_FirmSettings_TenantId",
                table: "FirmSettings");

            migrationBuilder.DropIndex(
                name: "IX_FirmEntities_TenantId",
                table: "FirmEntities");

            migrationBuilder.DropIndex(
                name: "IX_Expenses_TenantId",
                table: "Expenses");

            migrationBuilder.DropIndex(
                name: "IX_Employees_TenantId",
                table: "Employees");

            migrationBuilder.DropIndex(
                name: "IX_Employees_TenantId_Email",
                table: "Employees");

            migrationBuilder.DropIndex(
                name: "IX_EmailMessages_TenantId",
                table: "EmailMessages");

            migrationBuilder.DropIndex(
                name: "IX_EmailAccounts_TenantId",
                table: "EmailAccounts");

            migrationBuilder.DropIndex(
                name: "IX_DocumentVersions_TenantId",
                table: "DocumentVersions");

            migrationBuilder.DropIndex(
                name: "IX_DocumentShares_TenantId",
                table: "DocumentShares");

            migrationBuilder.DropIndex(
                name: "IX_Documents_TenantId",
                table: "Documents");

            migrationBuilder.DropIndex(
                name: "IX_DocumentContentTokens_TenantId",
                table: "DocumentContentTokens");

            migrationBuilder.DropIndex(
                name: "IX_DocumentContentIndexes_TenantId",
                table: "DocumentContentIndexes");

            migrationBuilder.DropIndex(
                name: "IX_DocumentComments_TenantId",
                table: "DocumentComments");

            migrationBuilder.DropIndex(
                name: "IX_Deadlines_TenantId",
                table: "Deadlines");

            migrationBuilder.DropIndex(
                name: "IX_CourtRules_TenantId",
                table: "CourtRules");

            migrationBuilder.DropIndex(
                name: "IX_ContractAnalyses_TenantId",
                table: "ContractAnalyses");

            migrationBuilder.DropIndex(
                name: "IX_ConflictResults_TenantId",
                table: "ConflictResults");

            migrationBuilder.DropIndex(
                name: "IX_ConflictChecks_TenantId",
                table: "ConflictChecks");

            migrationBuilder.DropIndex(
                name: "IX_ClientTrustLedgers_TenantId",
                table: "ClientTrustLedgers");

            migrationBuilder.DropIndex(
                name: "IX_ClientStatusHistories_TenantId",
                table: "ClientStatusHistories");

            migrationBuilder.DropIndex(
                name: "IX_Clients_TenantId",
                table: "Clients");

            migrationBuilder.DropIndex(
                name: "IX_Clients_TenantId_Email",
                table: "Clients");

            migrationBuilder.DropIndex(
                name: "IX_ClientMessages_TenantId",
                table: "ClientMessages");

            migrationBuilder.DropIndex(
                name: "IX_CasePredictions_TenantId",
                table: "CasePredictions");

            migrationBuilder.DropIndex(
                name: "IX_CalendarEvents_TenantId",
                table: "CalendarEvents");

            migrationBuilder.DropIndex(
                name: "IX_BillingSettings_TenantId",
                table: "BillingSettings");

            migrationBuilder.DropIndex(
                name: "IX_BillingLocks_TenantId",
                table: "BillingLocks");

            migrationBuilder.DropIndex(
                name: "IX_AuthSessions_TenantId",
                table: "AuthSessions");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_TenantId",
                table: "AuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_AppointmentRequests_TenantId",
                table: "AppointmentRequests");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "TrustTransactions");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "TrustBankAccounts");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "TimeEntries");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "StaffMessages");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "SmsTemplates");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "SmsReminders");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "SmsMessages");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "SignatureRequests");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "SignatureAuditEntries");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "RetentionPolicies");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ResearchSessions");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ReconciliationRecords");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "PaymentTransactions");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "PaymentPlans");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "OutboundEmails");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "OpposingParties");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Offices");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "MfaChallenges");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Matters");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "InvoiceLineItems");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "IntakeSubmissions");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "IntakeForms");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Holidays");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "FirmSettings");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "FirmEntities");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "EmailMessages");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "EmailAccounts");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "DocumentVersions");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "DocumentShares");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "DocumentContentTokens");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "DocumentContentIndexes");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "DocumentComments");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Deadlines");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "CourtRules");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ContractAnalyses");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ConflictResults");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ConflictChecks");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ClientTrustLedgers");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ClientStatusHistories");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ClientMessages");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "CasePredictions");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "CalendarEvents");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "BillingSettings");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "BillingLocks");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "AuthSessions");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "AppointmentRequests");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RetentionPolicies_EntityName",
                table: "RetentionPolicies",
                column: "EntityName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FirmSettings_Id",
                table: "FirmSettings",
                column: "Id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Employees_Email",
                table: "Employees",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Clients_Email",
                table: "Clients",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillingSettings_Id",
                table: "BillingSettings",
                column: "Id",
                unique: true);
        }
    }
}
