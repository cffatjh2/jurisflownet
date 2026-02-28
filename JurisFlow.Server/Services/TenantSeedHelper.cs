using Microsoft.EntityFrameworkCore;
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
                var tableName = entityType.GetTableName();
                if (string.IsNullOrWhiteSpace(tableName))
                {
                    continue;
                }

                var sql = $"UPDATE \"{tableName}\" SET TenantId = {{0}} WHERE TenantId IS NULL";
                await context.Database.ExecuteSqlRawAsync(sql, tenantId);
            }
        }
    }
}
