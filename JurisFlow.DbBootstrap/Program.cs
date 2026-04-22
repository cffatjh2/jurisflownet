using System.Data;
using System.Data.Common;
using System.Reflection;
using JurisFlow.Server.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;

var githubOutputPath = ParseGithubOutputPath(args);

using var context = new JurisFlowDesignTimeDbContextFactory().CreateDbContext(args);

if (!string.Equals(context.Database.ProviderName, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
{
    throw new InvalidOperationException("JurisFlow.DbBootstrap only supports PostgreSQL targets.");
}

var state = DetectDatabaseState(context);
switch (state)
{
    case DatabaseState.Fresh:
        BootstrapFreshDatabase(context);
        WriteGithubOutput(githubOutputPath, "bootstrapped", "true");
        WriteGithubOutput(githubOutputPath, "database_state", "fresh");
        Console.WriteLine("Fresh PostgreSQL schema created and EF migration history stamped.");
        break;
    case DatabaseState.Ready:
        WriteGithubOutput(githubOutputPath, "bootstrapped", "false");
        WriteGithubOutput(githubOutputPath, "database_state", "ready");
        Console.WriteLine("Database already contains EF migration history; bootstrap not required.");
        break;
    case DatabaseState.NonEmptyWithoutHistory:
        throw new InvalidOperationException(
            "Database contains application tables but no __EFMigrationsHistory table. Refusing automatic bootstrap because the target is not empty.");
    case DatabaseState.HistoryWithoutTables:
        throw new InvalidOperationException(
            "Database contains __EFMigrationsHistory but no application tables. Manual intervention is required.");
    default:
        throw new ArgumentOutOfRangeException();
}

static string? ParseGithubOutputPath(string[] args)
{
    for (var index = 0; index < args.Length; index++)
    {
        if (!string.Equals(args[index], "--github-output", StringComparison.Ordinal))
        {
            continue;
        }

        if (index + 1 >= args.Length)
        {
            throw new InvalidOperationException("--github-output requires a path argument.");
        }

        return args[index + 1];
    }

    return null;
}

static void WriteGithubOutput(string? githubOutputPath, string key, string value)
{
    if (string.IsNullOrWhiteSpace(githubOutputPath))
    {
        return;
    }

    File.AppendAllText(githubOutputPath, $"{key}={value}{Environment.NewLine}");
}

static DatabaseState DetectDatabaseState(JurisFlowDbContext context)
{
    var connection = context.Database.GetDbConnection();
    var shouldCloseConnection = connection.State != ConnectionState.Open;
    if (shouldCloseConnection)
    {
        connection.Open();
    }

    try
    {
        var historyExists = ExecuteScalar<bool>(connection, """
            select exists (
                select 1
                from information_schema.tables
                where table_schema = current_schema()
                  and table_name = '__EFMigrationsHistory'
            );
            """);

        var applicationTableCount = ExecuteScalar<long>(connection, """
            select count(*)
            from information_schema.tables
            where table_schema = current_schema()
              and table_type = 'BASE TABLE'
              and table_name <> '__EFMigrationsHistory';
            """);

        if (applicationTableCount < 0)
        {
            throw new InvalidOperationException("Application table count cannot be negative.");
        }

        return (historyExists, applicationTableCount) switch
        {
            (false, 0) => DatabaseState.Fresh,
            (true, > 0) => DatabaseState.Ready,
            (false, > 0) => DatabaseState.NonEmptyWithoutHistory,
            (true, 0) => DatabaseState.HistoryWithoutTables,
            _ => throw new InvalidOperationException("Unexpected PostgreSQL schema state.")
        };
    }
    finally
    {
        if (shouldCloseConnection)
        {
            connection.Close();
        }
    }
}

static T ExecuteScalar<T>(DbConnection connection, string commandText)
{
    using var command = connection.CreateCommand();
    command.CommandText = commandText;
    var value = command.ExecuteScalar();
    return (T)Convert.ChangeType(value!, typeof(T));
}

static void BootstrapFreshDatabase(JurisFlowDbContext context)
{
    var databaseCreator = context.GetService<IRelationalDatabaseCreator>();
    databaseCreator.CreateTables();

    var historyRepository = context.GetService<IHistoryRepository>();
    context.Database.ExecuteSqlRaw(historyRepository.GetCreateIfNotExistsScript());

    var productVersion = ResolveEfProductVersion();
    foreach (var migrationId in context.Database.GetMigrations().OrderBy(id => id, StringComparer.Ordinal))
    {
        context.Database.ExecuteSqlRaw(historyRepository.GetInsertScript(new HistoryRow(migrationId, productVersion)));
    }
}

static string ResolveEfProductVersion()
{
    var informationalVersion = typeof(DbContext).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion;

    if (string.IsNullOrWhiteSpace(informationalVersion))
    {
        return "10.0.2";
    }

    var separatorIndex = informationalVersion.IndexOf('+');
    return separatorIndex >= 0
        ? informationalVersion[..separatorIndex]
        : informationalVersion;
}

enum DatabaseState
{
    Fresh,
    Ready,
    NonEmptyWithoutHistory,
    HistoryWithoutTables
}
