using System.Data;
using System.Security.Cryptography;
using System.Text;
using JurisFlow.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace JurisFlow.Server.Services
{
    internal static class StartupTaskStateStore
    {
        private const string EnsureTableSql = """
            CREATE TABLE IF NOT EXISTS "__StartupTaskState" (
                "TaskKey" text PRIMARY KEY,
                "Value" text NULL,
                "UpdatedAt" timestamp with time zone NOT NULL
            );
            """;

        public static async Task<string?> GetValueAsync(
            JurisFlowDbContext context,
            string taskKey,
            CancellationToken cancellationToken = default)
        {
            await EnsureTableAsync(context, cancellationToken);

            var connection = context.Database.GetDbConnection();
            var shouldClose = connection.State != ConnectionState.Open;
            if (shouldClose)
            {
                await connection.OpenAsync(cancellationToken);
            }

            try
            {
                using var command = connection.CreateCommand();
                command.CommandText = """
                    SELECT "Value"
                    FROM "__StartupTaskState"
                    WHERE "TaskKey" = @taskKey
                    LIMIT 1;
                    """;

                var taskKeyParameter = command.CreateParameter();
                taskKeyParameter.ParameterName = "@taskKey";
                taskKeyParameter.Value = taskKey;
                command.Parameters.Add(taskKeyParameter);

                var result = await command.ExecuteScalarAsync(cancellationToken);
                return result == null || result == DBNull.Value
                    ? null
                    : Convert.ToString(result);
            }
            finally
            {
                if (shouldClose)
                {
                    await connection.CloseAsync();
                }
            }
        }

        public static async Task SetValueAsync(
            JurisFlowDbContext context,
            string taskKey,
            string value,
            CancellationToken cancellationToken = default)
        {
            await EnsureTableAsync(context, cancellationToken);

            var connection = context.Database.GetDbConnection();
            var shouldClose = connection.State != ConnectionState.Open;
            if (shouldClose)
            {
                await connection.OpenAsync(cancellationToken);
            }

            try
            {
                using var command = connection.CreateCommand();
                command.CommandText = """
                    INSERT INTO "__StartupTaskState" ("TaskKey", "Value", "UpdatedAt")
                    VALUES (@taskKey, @value, @updatedAt)
                    ON CONFLICT ("TaskKey") DO UPDATE
                    SET "Value" = excluded."Value",
                        "UpdatedAt" = excluded."UpdatedAt";
                    """;

                var taskKeyParameter = command.CreateParameter();
                taskKeyParameter.ParameterName = "@taskKey";
                taskKeyParameter.Value = taskKey;
                command.Parameters.Add(taskKeyParameter);

                var valueParameter = command.CreateParameter();
                valueParameter.ParameterName = "@value";
                valueParameter.Value = value;
                command.Parameters.Add(valueParameter);

                var updatedAtParameter = command.CreateParameter();
                updatedAtParameter.ParameterName = "@updatedAt";
                updatedAtParameter.Value = DateTime.UtcNow;
                command.Parameters.Add(updatedAtParameter);

                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            finally
            {
                if (shouldClose)
                {
                    await connection.CloseAsync();
                }
            }
        }

        public static string ComputeStableHash(IEnumerable<string> parts)
        {
            using var sha256 = SHA256.Create();
            var payload = string.Join("\n--\n", parts);
            var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(payload));
            return Convert.ToHexString(hash);
        }

        private static Task EnsureTableAsync(JurisFlowDbContext context, CancellationToken cancellationToken)
        {
            return context.Database.ExecuteSqlRawAsync(EnsureTableSql, cancellationToken);
        }
    }
}
