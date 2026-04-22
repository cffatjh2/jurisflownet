using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using JurisFlow.Server.Services;

namespace JurisFlow.Server.Data;

public sealed class JurisFlowDesignTimeDbContextFactory : IDesignTimeDbContextFactory<JurisFlowDbContext>
{
    public JurisFlowDbContext CreateDbContext(string[] args)
    {
        var contentRoot = ResolveContentRoot();
        var environmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";

        var configuration = new ConfigurationBuilder()
            .SetBasePath(contentRoot)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{environmentName}.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? configuration["ConnectionStrings:DefaultConnection"];

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required for design-time database operations.");
        }

        var provider = ResolveProvider(configuration, connectionString);
        var resolvedConnectionString = provider switch
        {
            "sqlite" => ResolveSqliteConnectionString(connectionString, contentRoot),
            "postgres" => NormalizePostgresConnectionString(connectionString),
            _ => throw new InvalidOperationException($"Unsupported database provider '{provider}'.")
        };

        var optionsBuilder = new DbContextOptionsBuilder<JurisFlowDbContext>();
        switch (provider)
        {
            case "sqlite":
                optionsBuilder.UseSqlite(resolvedConnectionString);
                break;
            case "postgres":
                optionsBuilder.UseNpgsql(resolvedConnectionString, npgsqlOptions =>
                {
                    npgsqlOptions.EnableRetryOnFailure(5, TimeSpan.FromSeconds(10), null);
                });
                break;
        }

        var dbEncryptionService = new DbEncryptionService(configuration, NullLogger<DbEncryptionService>.Instance);
        var tenantContext = new TenantContext
        {
            RequireTenant = false
        };

        return new JurisFlowDbContext(optionsBuilder.Options, dbEncryptionService, tenantContext);
    }

    private static string ResolveContentRoot()
    {
        var currentDirectory = Directory.GetCurrentDirectory();
        if (File.Exists(Path.Combine(currentDirectory, "appsettings.json")))
        {
            return currentDirectory;
        }

        var serverDirectory = Path.Combine(currentDirectory, "JurisFlow.Server");
        if (File.Exists(Path.Combine(serverDirectory, "appsettings.json")))
        {
            return serverDirectory;
        }

        return currentDirectory;
    }

    private static string ResolveProvider(IConfiguration configuration, string connectionString)
    {
        var configuredProvider = configuration["Database:Provider"]?.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(configuredProvider))
        {
            return configuredProvider switch
            {
                "sqlite" => "sqlite",
                "postgres" => "postgres",
                "postgresql" => "postgres",
                _ => throw new InvalidOperationException($"Unsupported database provider '{configuredProvider}'.")
            };
        }

        var normalizedConnection = connectionString.TrimStart();
        if (normalizedConnection.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
            normalizedConnection.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase) ||
            normalizedConnection.Contains("Host=", StringComparison.OrdinalIgnoreCase))
        {
            return "postgres";
        }

        return "sqlite";
    }

    private static string ResolveSqliteConnectionString(string connectionString, string contentRoot)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(builder.DataSource) ||
            builder.DataSource == ":memory:" ||
            Path.IsPathRooted(builder.DataSource))
        {
            return builder.ToString();
        }

        builder.DataSource = Path.GetFullPath(Path.Combine(contentRoot, builder.DataSource));
        return builder.ToString();
    }

    private static string NormalizePostgresConnectionString(string connectionString)
    {
        var normalized = connectionString.Trim();
        if (normalized.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            return ConvertPostgresUriToConnectionString(normalized);
        }

        return normalized;
    }

    private static string ConvertPostgresUriToConnectionString(string uriValue)
    {
        if (!Uri.TryCreate(uriValue, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("ConnectionStrings:DefaultConnection is not a valid PostgreSQL URI.");
        }

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort ? 5432 : uri.Port,
            Database = uri.AbsolutePath.Trim('/'),
            SslMode = SslMode.Require
        };

        var userInfo = uri.UserInfo.Split(':', 2);
        if (userInfo.Length > 0 && !string.IsNullOrWhiteSpace(userInfo[0]))
        {
            builder.Username = Uri.UnescapeDataString(userInfo[0]);
        }

        if (userInfo.Length > 1 && !string.IsNullOrWhiteSpace(userInfo[1]))
        {
            builder.Password = Uri.UnescapeDataString(userInfo[1]);
        }

        if (string.IsNullOrWhiteSpace(builder.Database))
        {
            throw new InvalidOperationException("ConnectionStrings:DefaultConnection PostgreSQL URI must include a database name.");
        }

        return builder.ToString();
    }
}
