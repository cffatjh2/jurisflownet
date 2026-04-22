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

var inspection = InspectDatabase(context);
switch (ClassifyDatabaseState(inspection))
{
    case DatabaseState.CreateAndStamp:
        CreateTablesAndStampMigrationHistory(context);
        WriteGithubOutput(githubOutputPath, "bootstrapped", "true");
        WriteGithubOutput(githubOutputPath, "database_state", "create_and_stamp");
        Console.WriteLine("PostgreSQL schema created and EF migration history stamped.");
        break;
    case DatabaseState.StampOnly:
        StampMigrationHistory(context);
        WriteGithubOutput(githubOutputPath, "bootstrapped", "true");
        WriteGithubOutput(githubOutputPath, "database_state", "stamp_only");
        Console.WriteLine("Existing JurisFlow PostgreSQL schema detected; EF migration history stamped.");
        break;
    case DatabaseState.Ready:
        WriteGithubOutput(githubOutputPath, "bootstrapped", "false");
        WriteGithubOutput(githubOutputPath, "database_state", "ready");
        Console.WriteLine("Database already contains EF migration history; bootstrap not required.");
        break;
    case DatabaseState.PartialModelWithoutHistory:
        throw new InvalidOperationException(BuildPartialSchemaErrorMessage(inspection));
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

static DatabaseInspection InspectDatabase(JurisFlowDbContext context)
{
    var connection = context.Database.GetDbConnection();
    var shouldCloseConnection = connection.State != ConnectionState.Open;
    if (shouldCloseConnection)
    {
        connection.Open();
    }

    try
    {
        var currentSchema = ExecuteScalar<string>(connection, "select current_schema();");
        var historyExists = ExecuteScalar<bool>(connection, """
            select exists (
                select 1
                from information_schema.tables
                where table_schema = current_schema()
                  and table_name = '__EFMigrationsHistory'
            );
            """);

        var existingTables = ExecuteColumn(connection, """
            select table_name
            from information_schema.tables
            where table_schema = current_schema()
              and table_type = 'BASE TABLE'
              and table_name <> '__EFMigrationsHistory'
            order by table_name;
            """);

        var expectedModelTables = context.Model.GetRelationalModel().Tables
            .Where(table => string.Equals(table.Schema ?? currentSchema, currentSchema, StringComparison.Ordinal))
            .Select(table => table.Name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        var existingTableSet = new HashSet<string>(existingTables, StringComparer.Ordinal);
        var matchingModelTables = expectedModelTables
            .Where(existingTableSet.Contains)
            .ToArray();

        return new DatabaseInspection(
            currentSchema,
            historyExists,
            existingTables,
            expectedModelTables,
            matchingModelTables);
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

static string[] ExecuteColumn(DbConnection connection, string commandText)
{
    using var command = connection.CreateCommand();
    command.CommandText = commandText;
    using var reader = command.ExecuteReader();

    var values = new List<string>();
    while (reader.Read())
    {
        values.Add(Convert.ToString(reader.GetValue(0))!);
    }

    return values.ToArray();
}

static DatabaseState ClassifyDatabaseState(DatabaseInspection inspection)
{
    if (inspection.HistoryExists)
    {
        return DatabaseState.Ready;
    }

    if (inspection.MatchingModelTables.Count == 0)
    {
        return DatabaseState.CreateAndStamp;
    }

    return inspection.MatchingModelTables.Count == inspection.ExpectedModelTables.Count
        ? DatabaseState.StampOnly
        : DatabaseState.PartialModelWithoutHistory;
}

static string BuildPartialSchemaErrorMessage(DatabaseInspection inspection)
{
    var missingTables = inspection.ExpectedModelTables
        .Except(inspection.MatchingModelTables, StringComparer.Ordinal)
        .Take(10)
        .ToArray();

    var matchingTables = inspection.MatchingModelTables
        .Take(10)
        .ToArray();

    return
        $"Database schema '{inspection.CurrentSchema}' contains {inspection.MatchingModelTables.Count} of {inspection.ExpectedModelTables.Count} expected JurisFlow tables but no __EFMigrationsHistory table. " +
        $"Matching tables: {JoinSample(matchingTables)}. Missing tables: {JoinSample(missingTables)}.";
}

static string JoinSample(IEnumerable<string> values)
{
    var sample = values.ToArray();
    return sample.Length == 0
        ? "(none)"
        : string.Join(", ", sample);
}

static void CreateTablesAndStampMigrationHistory(JurisFlowDbContext context)
{
    var databaseCreator = context.GetService<IRelationalDatabaseCreator>();
    databaseCreator.CreateTables();

    StampMigrationHistory(context);
}

static void StampMigrationHistory(JurisFlowDbContext context)
{
    var connection = context.Database.GetDbConnection();
    var shouldCloseConnection = connection.State != ConnectionState.Open;
    if (shouldCloseConnection)
    {
        connection.Open();
    }

    bool historyExists;
    try
    {
        historyExists = ExecuteScalar<bool>(connection, """
            select exists (
                select 1
                from information_schema.tables
                where table_schema = current_schema()
                  and table_name = '__EFMigrationsHistory'
            );
            """);
    }
    finally
    {
        if (shouldCloseConnection)
        {
            connection.Close();
        }
    }

    var historyRepository = context.GetService<IHistoryRepository>();
    if (!historyExists)
    {
        context.Database.ExecuteSqlRaw(historyRepository.GetCreateIfNotExistsScript());
    }

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
    CreateAndStamp,
    StampOnly,
    Ready,
    PartialModelWithoutHistory
}

sealed record DatabaseInspection(
    string CurrentSchema,
    bool HistoryExists,
    IReadOnlyList<string> ExistingTables,
    IReadOnlyList<string> ExpectedModelTables,
    IReadOnlyList<string> MatchingModelTables);
