using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using Task = System.Threading.Tasks.Task;

namespace JurisFlow.Server.Services
{
    public static class TenantSeedHelper
    {
        public static string NormalizeSlug(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? "default"
                : value.Trim().ToLowerInvariant();
        }

        public static async Task BackfillTenantIdsAsync(JurisFlowDbContext context, string tenantId)
        {
            var entityTypes = context.Model.GetEntityTypes()
                .Where(e => e.ClrType != typeof(Tenant) && !e.IsOwned())
                .Where(e => e.FindProperty("TenantId") != null);

            foreach (var entityType in entityTypes)
            {
                var tenantIdProperty = entityType.FindProperty("TenantId");
                var tableName = entityType.GetTableName();
                if (tenantIdProperty == null || string.IsNullOrWhiteSpace(tableName))
                {
                    continue;
                }

                var schema = entityType.GetSchema();
                var storeObject = StoreObjectIdentifier.Table(tableName, schema);
                var columnName = tenantIdProperty.GetColumnName(storeObject);
                if (string.IsNullOrWhiteSpace(columnName))
                {
                    continue;
                }

                var quotedTable = QuoteIdentifier(tableName, schema);
                var quotedColumn = QuoteIdentifier(columnName);
                var sql = $"UPDATE {quotedTable} SET {quotedColumn} = {{0}} WHERE {quotedColumn} IS NULL";
                await context.Database.ExecuteSqlRawAsync(sql, tenantId);
            }
        }

        private static string QuoteIdentifier(string identifier)
        {
            return $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
        }

        private static string QuoteIdentifier(string tableName, string? schema)
        {
            if (string.IsNullOrWhiteSpace(schema))
            {
                return QuoteIdentifier(tableName);
            }

            return $"{QuoteIdentifier(schema)}.{QuoteIdentifier(tableName)}";
        }
    }
}
