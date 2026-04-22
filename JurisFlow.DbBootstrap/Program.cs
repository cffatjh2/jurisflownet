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
var orderedMigrationIds = context.Database.GetMigrations().OrderBy(id => id, StringComparer.Ordinal).ToArray();
var decision = DecideBootstrapPlan(inspection, orderedMigrationIds);
switch (decision.Action)
{
    case BootstrapAction.CreateAndStampAll:
        CreateTables(context);
        StampMigrationHistory(context, orderedMigrationIds);
        WriteGithubOutput(githubOutputPath, "bootstrapped", "true");
        WriteGithubOutput(githubOutputPath, "database_state", "create_and_stamp");
        WriteGithubOutput(githubOutputPath, "requires_update", "false");
        Console.WriteLine("PostgreSQL schema created and EF migration history stamped.");
        break;
    case BootstrapAction.StampAll:
        StampMigrationHistory(context, orderedMigrationIds);
        WriteGithubOutput(githubOutputPath, "bootstrapped", "true");
        WriteGithubOutput(githubOutputPath, "database_state", "stamp_only");
        WriteGithubOutput(githubOutputPath, "requires_update", "false");
        Console.WriteLine("Existing JurisFlow PostgreSQL schema detected; EF migration history stamped.");
        break;
    case BootstrapAction.StampBaselineThenUpdate:
        StampMigrationHistory(context, decision.MigrationsToStamp);
        WriteGithubOutput(githubOutputPath, "bootstrapped", "true");
        WriteGithubOutput(githubOutputPath, "database_state", "baseline_stamped");
        WriteGithubOutput(githubOutputPath, "requires_update", "true");
        Console.WriteLine($"Stamped EF migration history through {decision.MigrationsToStamp.LastOrDefault() ?? "(none)"}; remaining migrations will be applied normally.");
        break;
    case BootstrapAction.Ready:
        WriteGithubOutput(githubOutputPath, "bootstrapped", "false");
        WriteGithubOutput(githubOutputPath, "database_state", "ready");
        WriteGithubOutput(githubOutputPath, "requires_update", "true");
        Console.WriteLine("Database already contains EF migration history; bootstrap not required.");
        break;
    case BootstrapAction.UnsupportedPartialSchema:
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

static BootstrapDecision DecideBootstrapPlan(DatabaseInspection inspection, IReadOnlyList<string> orderedMigrationIds)
{
    if (inspection.HistoryExists)
    {
        return new BootstrapDecision(BootstrapAction.Ready, Array.Empty<string>());
    }

    if (inspection.MatchingModelTables.Count == 0)
    {
        return new BootstrapDecision(BootstrapAction.CreateAndStampAll, Array.Empty<string>());
    }

    if (inspection.MatchingModelTables.Count == inspection.ExpectedModelTables.Count)
    {
        return new BootstrapDecision(BootstrapAction.StampAll, Array.Empty<string>());
    }

    var missingTables = inspection.ExpectedModelTables
        .Except(inspection.MatchingModelTables, StringComparer.Ordinal)
        .ToArray();

    if (!TryResolveBaselineStamp(orderedMigrationIds, missingTables, out var migrationsToStamp))
    {
        return new BootstrapDecision(BootstrapAction.UnsupportedPartialSchema, Array.Empty<string>());
    }

    return new BootstrapDecision(BootstrapAction.StampBaselineThenUpdate, migrationsToStamp);
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

static bool TryResolveBaselineStamp(
    IReadOnlyList<string> orderedMigrationIds,
    IReadOnlyList<string> missingTables,
    out string[] migrationsToStamp)
{
    migrationsToStamp = Array.Empty<string>();

    var tableToMigration = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["LeadStatusHistories"] = "20260419170340_AddLeadDomainParityPhase2",
        ["TaskTemplates"] = "20260421123750_AddTaskProductionReadinessPhase0",
        ["MessageAttachments"] = "20260421213011_AddMessageAttachments",
        ["PaymentCommandDeduplications"] = "20260422112409_HardenPaymentsAndBillingLocksP1"
    };

    if (missingTables.Any(table => !tableToMigration.ContainsKey(table)))
    {
        return false;
    }

    var earliestMissingMigration = missingTables
        .Select(table => tableToMigration[table])
        .Distinct(StringComparer.Ordinal)
        .OrderBy(id => id, StringComparer.Ordinal)
        .FirstOrDefault();

    if (string.IsNullOrWhiteSpace(earliestMissingMigration))
    {
        return false;
    }

    var stopIndex = Array.IndexOf(orderedMigrationIds.ToArray(), earliestMissingMigration);
    if (stopIndex < 0)
    {
        return false;
    }

    migrationsToStamp = orderedMigrationIds.Take(stopIndex).ToArray();
    return true;
}

static void CreateTables(JurisFlowDbContext context)
{
    var databaseCreator = context.GetService<IRelationalDatabaseCreator>();
    databaseCreator.CreateTables();
}

static void StampMigrationHistory(JurisFlowDbContext context, IEnumerable<string> migrationIds)
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
    foreach (var migrationId in migrationIds.OrderBy(id => id, StringComparer.Ordinal))
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

enum BootstrapAction
{
    CreateAndStampAll,
    StampAll,
    StampBaselineThenUpdate,
    Ready,
    UnsupportedPartialSchema
}

sealed record DatabaseInspection(
    string CurrentSchema,
    bool HistoryExists,
    IReadOnlyList<string> ExistingTables,
    IReadOnlyList<string> ExpectedModelTables,
    IReadOnlyList<string> MatchingModelTables);

sealed record BootstrapDecision(
    BootstrapAction Action,
    IReadOnlyList<string> MigrationsToStamp);
