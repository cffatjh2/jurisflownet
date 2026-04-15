using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JurisFlow.Server.Migrations
{
    /// <inheritdoc />
    public partial class TrustMoneyDecimalAndBillingAllocationIdempotencyPhase1C : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (ActiveProvider.Contains("Sqlite"))
            {
                migrationBuilder.AlterColumn<decimal>(
                    name: "BalanceBefore",
                    table: "TrustTransactions",
                    type: "TEXT",
                    precision: 18,
                    scale: 2,
                    nullable: false,
                    oldClrType: typeof(double),
                    oldType: "REAL");

                migrationBuilder.AlterColumn<decimal>(
                    name: "BalanceAfter",
                    table: "TrustTransactions",
                    type: "TEXT",
                    precision: 18,
                    scale: 2,
                    nullable: false,
                    oldClrType: typeof(double),
                    oldType: "REAL");

                migrationBuilder.AlterColumn<decimal>(
                    name: "Amount",
                    table: "TrustTransactions",
                    type: "TEXT",
                    precision: 18,
                    scale: 2,
                    nullable: false,
                    oldClrType: typeof(double),
                    oldType: "REAL");

                migrationBuilder.AlterColumn<decimal>(
                    name: "CurrentBalance",
                    table: "TrustBankAccounts",
                    type: "TEXT",
                    precision: 18,
                    scale: 2,
                    nullable: false,
                    oldClrType: typeof(double),
                    oldType: "REAL");

                migrationBuilder.AlterColumn<decimal>(
                    name: "TrustLedgerBalance",
                    table: "ReconciliationRecords",
                    type: "TEXT",
                    precision: 18,
                    scale: 2,
                    nullable: false,
                    oldClrType: typeof(double),
                    oldType: "REAL");

                migrationBuilder.AlterColumn<decimal>(
                    name: "DiscrepancyAmount",
                    table: "ReconciliationRecords",
                    type: "TEXT",
                    precision: 18,
                    scale: 2,
                    nullable: false,
                    oldClrType: typeof(double),
                    oldType: "REAL");

                migrationBuilder.AlterColumn<decimal>(
                    name: "ClientLedgerSumBalance",
                    table: "ReconciliationRecords",
                    type: "TEXT",
                    precision: 18,
                    scale: 2,
                    nullable: false,
                    oldClrType: typeof(double),
                    oldType: "REAL");

                migrationBuilder.AlterColumn<decimal>(
                    name: "BankStatementBalance",
                    table: "ReconciliationRecords",
                    type: "TEXT",
                    precision: 18,
                    scale: 2,
                    nullable: false,
                    oldClrType: typeof(double),
                    oldType: "REAL");

                migrationBuilder.AlterColumn<decimal>(
                    name: "RunningBalance",
                    table: "ClientTrustLedgers",
                    type: "TEXT",
                    precision: 18,
                    scale: 2,
                    nullable: false,
                    oldClrType: typeof(double),
                    oldType: "REAL");
            }
            else if (ActiveProvider.Contains("Npgsql"))
            {
                migrationBuilder.Sql("""ALTER TABLE "TrustTransactions" ALTER COLUMN "BalanceBefore" TYPE numeric(18,2) USING round("BalanceBefore"::numeric, 2);""");
                migrationBuilder.Sql("""ALTER TABLE "TrustTransactions" ALTER COLUMN "BalanceAfter" TYPE numeric(18,2) USING round("BalanceAfter"::numeric, 2);""");
                migrationBuilder.Sql("""ALTER TABLE "TrustTransactions" ALTER COLUMN "Amount" TYPE numeric(18,2) USING round("Amount"::numeric, 2);""");
                migrationBuilder.Sql("""ALTER TABLE "TrustBankAccounts" ALTER COLUMN "CurrentBalance" TYPE numeric(18,2) USING round("CurrentBalance"::numeric, 2);""");
                migrationBuilder.Sql("""ALTER TABLE "ReconciliationRecords" ALTER COLUMN "TrustLedgerBalance" TYPE numeric(18,2) USING round("TrustLedgerBalance"::numeric, 2);""");
                migrationBuilder.Sql("""ALTER TABLE "ReconciliationRecords" ALTER COLUMN "DiscrepancyAmount" TYPE numeric(18,2) USING round("DiscrepancyAmount"::numeric, 2);""");
                migrationBuilder.Sql("""ALTER TABLE "ReconciliationRecords" ALTER COLUMN "ClientLedgerSumBalance" TYPE numeric(18,2) USING round("ClientLedgerSumBalance"::numeric, 2);""");
                migrationBuilder.Sql("""ALTER TABLE "ReconciliationRecords" ALTER COLUMN "BankStatementBalance" TYPE numeric(18,2) USING round("BankStatementBalance"::numeric, 2);""");
                migrationBuilder.Sql("""ALTER TABLE "ClientTrustLedgers" ALTER COLUMN "RunningBalance" TYPE numeric(18,2) USING round("RunningBalance"::numeric, 2);""");
            }
            else
            {
                migrationBuilder.AlterColumn<decimal>(
                    name: "BalanceBefore",
                    table: "TrustTransactions",
                    type: "decimal(18,2)",
                    precision: 18,
                    scale: 2,
                    nullable: false);

                migrationBuilder.AlterColumn<decimal>(
                    name: "BalanceAfter",
                    table: "TrustTransactions",
                    type: "decimal(18,2)",
                    precision: 18,
                    scale: 2,
                    nullable: false);

                migrationBuilder.AlterColumn<decimal>(
                    name: "Amount",
                    table: "TrustTransactions",
                    type: "decimal(18,2)",
                    precision: 18,
                    scale: 2,
                    nullable: false);

                migrationBuilder.AlterColumn<decimal>(
                    name: "CurrentBalance",
                    table: "TrustBankAccounts",
                    type: "decimal(18,2)",
                    precision: 18,
                    scale: 2,
                    nullable: false);

                migrationBuilder.AlterColumn<decimal>(
                    name: "TrustLedgerBalance",
                    table: "ReconciliationRecords",
                    type: "decimal(18,2)",
                    precision: 18,
                    scale: 2,
                    nullable: false);

                migrationBuilder.AlterColumn<decimal>(
                    name: "DiscrepancyAmount",
                    table: "ReconciliationRecords",
                    type: "decimal(18,2)",
                    precision: 18,
                    scale: 2,
                    nullable: false);

                migrationBuilder.AlterColumn<decimal>(
                    name: "ClientLedgerSumBalance",
                    table: "ReconciliationRecords",
                    type: "decimal(18,2)",
                    precision: 18,
                    scale: 2,
                    nullable: false);

                migrationBuilder.AlterColumn<decimal>(
                    name: "BankStatementBalance",
                    table: "ReconciliationRecords",
                    type: "decimal(18,2)",
                    precision: 18,
                    scale: 2,
                    nullable: false);

                migrationBuilder.AlterColumn<decimal>(
                    name: "RunningBalance",
                    table: "ClientTrustLedgers",
                    type: "decimal(18,2)",
                    precision: 18,
                    scale: 2,
                    nullable: false);
            }

            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                table: "BillingPaymentAllocations",
                type: "TEXT",
                maxLength: 160,
                nullable: false,
                defaultValue: "");

            if (ActiveProvider.Contains("Sqlite"))
            {
                migrationBuilder.Sql("""UPDATE "BillingPaymentAllocations" SET "IdempotencyKey" = substr('legacy:' || "Id", 1, 160) WHERE "IdempotencyKey" = '';""");
            }
            else if (ActiveProvider.Contains("Npgsql"))
            {
                migrationBuilder.Sql("""UPDATE "BillingPaymentAllocations" SET "IdempotencyKey" = left('legacy:' || "Id", 160) WHERE "IdempotencyKey" = '';""");
            }
            else
            {
                migrationBuilder.Sql("""UPDATE [BillingPaymentAllocations] SET [IdempotencyKey] = LEFT(CONCAT('legacy:', [Id]), 160) WHERE [IdempotencyKey] = N'';""");
            }

            migrationBuilder.CreateIndex(
                name: "IX_BillingPaymentAllocations_TenantId_IdempotencyKey",
                table: "BillingPaymentAllocations",
                columns: new[] { "TenantId", "IdempotencyKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BillingPaymentAllocations_TenantId_IdempotencyKey",
                table: "BillingPaymentAllocations");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                table: "BillingPaymentAllocations");

            if (ActiveProvider.Contains("Sqlite"))
            {
                migrationBuilder.AlterColumn<double>(
                    name: "BalanceBefore",
                    table: "TrustTransactions",
                    type: "REAL",
                    nullable: false,
                    oldClrType: typeof(decimal),
                    oldType: "TEXT",
                    oldPrecision: 18,
                    oldScale: 2);

                migrationBuilder.AlterColumn<double>(
                    name: "BalanceAfter",
                    table: "TrustTransactions",
                    type: "REAL",
                    nullable: false,
                    oldClrType: typeof(decimal),
                    oldType: "TEXT",
                    oldPrecision: 18,
                    oldScale: 2);

                migrationBuilder.AlterColumn<double>(
                    name: "Amount",
                    table: "TrustTransactions",
                    type: "REAL",
                    nullable: false,
                    oldClrType: typeof(decimal),
                    oldType: "TEXT",
                    oldPrecision: 18,
                    oldScale: 2);

                migrationBuilder.AlterColumn<double>(
                    name: "CurrentBalance",
                    table: "TrustBankAccounts",
                    type: "REAL",
                    nullable: false,
                    oldClrType: typeof(decimal),
                    oldType: "TEXT",
                    oldPrecision: 18,
                    oldScale: 2);

                migrationBuilder.AlterColumn<double>(
                    name: "TrustLedgerBalance",
                    table: "ReconciliationRecords",
                    type: "REAL",
                    nullable: false,
                    oldClrType: typeof(decimal),
                    oldType: "TEXT",
                    oldPrecision: 18,
                    oldScale: 2);

                migrationBuilder.AlterColumn<double>(
                    name: "DiscrepancyAmount",
                    table: "ReconciliationRecords",
                    type: "REAL",
                    nullable: false,
                    oldClrType: typeof(decimal),
                    oldType: "TEXT",
                    oldPrecision: 18,
                    oldScale: 2);

                migrationBuilder.AlterColumn<double>(
                    name: "ClientLedgerSumBalance",
                    table: "ReconciliationRecords",
                    type: "REAL",
                    nullable: false,
                    oldClrType: typeof(decimal),
                    oldType: "TEXT",
                    oldPrecision: 18,
                    oldScale: 2);

                migrationBuilder.AlterColumn<double>(
                    name: "BankStatementBalance",
                    table: "ReconciliationRecords",
                    type: "REAL",
                    nullable: false,
                    oldClrType: typeof(decimal),
                    oldType: "TEXT",
                    oldPrecision: 18,
                    oldScale: 2);

                migrationBuilder.AlterColumn<double>(
                    name: "RunningBalance",
                    table: "ClientTrustLedgers",
                    type: "REAL",
                    nullable: false,
                    oldClrType: typeof(decimal),
                    oldType: "TEXT",
                    oldPrecision: 18,
                    oldScale: 2);
            }
            else if (ActiveProvider.Contains("Npgsql"))
            {
                migrationBuilder.Sql("""ALTER TABLE "TrustTransactions" ALTER COLUMN "BalanceBefore" TYPE double precision USING "BalanceBefore"::double precision;""");
                migrationBuilder.Sql("""ALTER TABLE "TrustTransactions" ALTER COLUMN "BalanceAfter" TYPE double precision USING "BalanceAfter"::double precision;""");
                migrationBuilder.Sql("""ALTER TABLE "TrustTransactions" ALTER COLUMN "Amount" TYPE double precision USING "Amount"::double precision;""");
                migrationBuilder.Sql("""ALTER TABLE "TrustBankAccounts" ALTER COLUMN "CurrentBalance" TYPE double precision USING "CurrentBalance"::double precision;""");
                migrationBuilder.Sql("""ALTER TABLE "ReconciliationRecords" ALTER COLUMN "TrustLedgerBalance" TYPE double precision USING "TrustLedgerBalance"::double precision;""");
                migrationBuilder.Sql("""ALTER TABLE "ReconciliationRecords" ALTER COLUMN "DiscrepancyAmount" TYPE double precision USING "DiscrepancyAmount"::double precision;""");
                migrationBuilder.Sql("""ALTER TABLE "ReconciliationRecords" ALTER COLUMN "ClientLedgerSumBalance" TYPE double precision USING "ClientLedgerSumBalance"::double precision;""");
                migrationBuilder.Sql("""ALTER TABLE "ReconciliationRecords" ALTER COLUMN "BankStatementBalance" TYPE double precision USING "BankStatementBalance"::double precision;""");
                migrationBuilder.Sql("""ALTER TABLE "ClientTrustLedgers" ALTER COLUMN "RunningBalance" TYPE double precision USING "RunningBalance"::double precision;""");
            }
            else
            {
                migrationBuilder.AlterColumn<double>(
                    name: "BalanceBefore",
                    table: "TrustTransactions",
                    type: "float",
                    nullable: false);

                migrationBuilder.AlterColumn<double>(
                    name: "BalanceAfter",
                    table: "TrustTransactions",
                    type: "float",
                    nullable: false);

                migrationBuilder.AlterColumn<double>(
                    name: "Amount",
                    table: "TrustTransactions",
                    type: "float",
                    nullable: false);

                migrationBuilder.AlterColumn<double>(
                    name: "CurrentBalance",
                    table: "TrustBankAccounts",
                    type: "float",
                    nullable: false);

                migrationBuilder.AlterColumn<double>(
                    name: "TrustLedgerBalance",
                    table: "ReconciliationRecords",
                    type: "float",
                    nullable: false);

                migrationBuilder.AlterColumn<double>(
                    name: "DiscrepancyAmount",
                    table: "ReconciliationRecords",
                    type: "float",
                    nullable: false);

                migrationBuilder.AlterColumn<double>(
                    name: "ClientLedgerSumBalance",
                    table: "ReconciliationRecords",
                    type: "float",
                    nullable: false);

                migrationBuilder.AlterColumn<double>(
                    name: "BankStatementBalance",
                    table: "ReconciliationRecords",
                    type: "float",
                    nullable: false);

                migrationBuilder.AlterColumn<double>(
                    name: "RunningBalance",
                    table: "ClientTrustLedgers",
                    type: "float",
                    nullable: false);
            }
        }
    }
}
