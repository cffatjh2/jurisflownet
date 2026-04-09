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
            var backfillableTargets = context.Model.GetEntityTypes()
                .Where(e => e.ClrType != typeof(Tenant) && !e.IsOwned())
                .Where(e => e.FindProperty("TenantId") != null);

            var updateTargets = new List<(string TableName, string? Schema, string ColumnName)>();
            foreach (var entityType in backfillableTargets)
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

                updateTargets.Add((tableName, schema, columnName));
            }

            if (updateTargets.Count == 0)
            {
                return;
            }

            var signature = StartupTaskStateStore.ComputeStableHash(updateTargets
                .Select(target => $"{target.Schema ?? "dbo"}.{target.TableName}:{target.ColumnName}:{tenantId}")
                .OrderBy(value => value, StringComparer.Ordinal));

            var taskKey = $"tenant-id-backfill:{tenantId}";
            var appliedSignature = await StartupTaskStateStore.GetValueAsync(context, taskKey);
            if (string.Equals(appliedSignature, signature, StringComparison.Ordinal))
            {
                return;
            }

            foreach (var target in updateTargets)
            {
                var quotedTable = QuoteIdentifier(target.TableName, target.Schema);
                var quotedColumn = QuoteIdentifier(target.ColumnName);
                var sql = $"UPDATE {quotedTable} SET {quotedColumn} = {{0}} WHERE {quotedColumn} IS NULL";
                await context.Database.ExecuteSqlRawAsync(sql, tenantId);
            }

            await StartupTaskStateStore.SetValueAsync(context, taskKey, signature);
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
