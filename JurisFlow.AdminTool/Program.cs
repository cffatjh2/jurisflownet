using System.Text.RegularExpressions;
using JurisFlow.Server.Data;
using JurisFlow.Server.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Tenant = JurisFlow.Server.Models.Tenant;
using User = JurisFlow.Server.Models.User;

var exitCode = await ProvisioningCli.RunAsync(args);
return exitCode;

internal static class ProvisioningCli
{
    private const string DefaultConnectionString = "Data Source=jurisflow.db";

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0)
        {
            PrintHelp();
            return 1;
        }

        if (IsHelp(args[0]))
        {
            PrintHelp();
            return 0;
        }

        IConfiguration configuration;

        try
        {
            configuration = BuildConfiguration();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Configuration error: {ex.Message}");
            return 1;
        }

        var services = new ServiceCollection();
        ConfigureServices(services, configuration);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

        try
        {
            return await ExecuteAsync(provider, args);
        }
        catch (Exception ex)
        {
            var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("JurisFlow.AdminTool");
            logger.LogError(ex, "Provisioning command failed.");
            return 1;
        }
    }

    private static async Task<int> ExecuteAsync(ServiceProvider provider, string[] args)
    {
        var area = args[0].Trim().ToLowerInvariant();
        var remaining = args.Skip(1).ToArray();

        return area switch
        {
            "tenant" => await RunTenantCommandAsync(provider, remaining),
            "user" => await RunUserCommandAsync(provider, remaining),
            _ => FailUnknownCommand($"Unknown command group '{args[0]}'.")
        };
    }

    private static async Task<int> RunTenantCommandAsync(ServiceProvider provider, string[] args)
    {
        if (args.Length == 0)
        {
            PrintTenantHelp();
            return 1;
        }

        if (IsHelp(args[0]))
        {
            PrintTenantHelp();
            return 0;
        }

        var action = args[0].Trim().ToLowerInvariant();
        var options = ParseOptions(args.Skip(1).ToArray());

        return action switch
        {
            "list" => await RunTenantListAsync(provider),
            "create" => await RunTenantCreateAsync(provider, options),
            _ => FailUnknownCommand($"Unknown tenant command '{args[0]}'.")
        };
    }

    private static async Task<int> RunUserCommandAsync(ServiceProvider provider, string[] args)
    {
        if (args.Length == 0)
        {
            PrintUserHelp();
            return 1;
        }

        if (IsHelp(args[0]))
        {
            PrintUserHelp();
            return 0;
        }

        var action = args[0].Trim().ToLowerInvariant();
        var options = ParseOptions(args.Skip(1).ToArray());

        return action switch
        {
            "list" => await RunUserListAsync(provider, options),
            "create" => await RunUserCreateAsync(provider, options, forceAdmin: false),
            "create-admin" => await RunUserCreateAsync(provider, options, forceAdmin: true),
            "reset-password" => await RunResetPasswordAsync(provider, options),
            _ => FailUnknownCommand($"Unknown user command '{args[0]}'.")
        };
    }

    private static async Task<int> RunTenantListAsync(ServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();

        await EnsureDatabaseConnectivityAsync(context);

        var tenants = await context.Tenants
            .AsNoTracking()
            .OrderBy(t => t.Slug)
            .ToListAsync();

        if (tenants.Count == 0)
        {
            Console.WriteLine("No tenants found.");
            return 0;
        }

        foreach (var tenant in tenants)
        {
            Console.WriteLine($"{tenant.Slug}\t{tenant.Id}\t{tenant.Name}\tactive={tenant.IsActive}");
        }

        return 0;
    }

    private static async Task<int> RunTenantCreateAsync(ServiceProvider provider, IReadOnlyDictionary<string, string?> options)
    {
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();

        await EnsureDatabaseConnectivityAsync(context);

        var name = RequireOption(options, "name");
        var slug = options.TryGetValue("slug", out var slugValue) && !string.IsNullOrWhiteSpace(slugValue)
            ? NormalizeTenantSlug(slugValue)
            : NormalizeTenantSlug(name);

        var tenant = await context.Tenants.FirstOrDefaultAsync(t => t.Slug == slug);
        var isNew = tenant is null;

        if (tenant is null)
        {
            tenant = new Tenant
            {
                Name = name.Trim(),
                Slug = slug,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            context.Tenants.Add(tenant);
        }
        else
        {
            tenant.Name = name.Trim();
            tenant.IsActive = true;
            tenant.UpdatedAt = DateTime.UtcNow;
        }

        await context.SaveChangesAsync();

        Console.WriteLine(isNew
            ? $"Created tenant '{tenant.Slug}' ({tenant.Id})."
            : $"Updated tenant '{tenant.Slug}' ({tenant.Id}).");

        return 0;
    }

    private static async Task<int> RunUserListAsync(ServiceProvider provider, IReadOnlyDictionary<string, string?> options)
    {
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();

        await EnsureDatabaseConnectivityAsync(context);

        if (options.TryGetValue("tenant", out var tenantValue) && !string.IsNullOrWhiteSpace(tenantValue))
        {
            var tenant = await RequireTenantAsync(context, NormalizeTenantSlug(tenantValue));
            tenantContext.Set(tenant.Id, tenant.Slug);
        }
        else
        {
            tenantContext.RequireTenant = false;
        }

        var users = await context.Users
            .AsNoTracking()
            .OrderBy(u => u.Email)
            .Select(u => new
            {
                u.Email,
                u.Name,
                u.Role,
                TenantId = EF.Property<string>(u, "TenantId")
            })
            .ToListAsync();

        if (users.Count == 0)
        {
            Console.WriteLine("No users found.");
            return 0;
        }

        foreach (var user in users)
        {
            Console.WriteLine($"{user.Email}\t{user.Role}\t{user.TenantId}\t{user.Name}");
        }

        return 0;
    }

    private static async Task<int> RunUserCreateAsync(
        ServiceProvider provider,
        IReadOnlyDictionary<string, string?> options,
        bool forceAdmin)
    {
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        await EnsureDatabaseConnectivityAsync(context);

        var tenant = await RequireTenantAsync(context, NormalizeTenantSlug(RequireOption(options, "tenant")));
        tenantContext.Set(tenant.Id, tenant.Slug);

        var email = RequireOption(options, "email");
        var normalizedEmail = EmailAddressNormalizer.Normalize(email);
        if (string.IsNullOrWhiteSpace(normalizedEmail))
        {
            throw new InvalidOperationException("Email is required.");
        }

        var password = ResolvePassword(options);
        var role = forceAdmin ? "Admin" : RequireOption(options, "role").Trim();
        var defaultName = forceAdmin ? "Admin User" : email.Trim();
        var name = options.TryGetValue("name", out var nameValue) && !string.IsNullOrWhiteSpace(nameValue)
            ? nameValue.Trim()
            : defaultName;

        var user = await context.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail);
        var isNew = user is null;

        if (user is null)
        {
            user = new User
            {
                Email = email.Trim(),
                NormalizedEmail = normalizedEmail,
                Name = name,
                Role = role,
                PasswordHash = PasswordHashingHelper.HashPassword(password, configuration),
                MfaEnabled = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            context.Users.Add(user);
        }
        else
        {
            user.Email = email.Trim();
            user.NormalizedEmail = normalizedEmail;
            user.Name = name;
            user.Role = role;
            user.PasswordHash = PasswordHashingHelper.HashPassword(password, configuration);
            user.UpdatedAt = DateTime.UtcNow;
        }

        await context.SaveChangesAsync();

        Console.WriteLine(isNew
            ? $"Created user '{user.Email}' in tenant '{tenant.Slug}' with role '{user.Role}'."
            : $"Updated user '{user.Email}' in tenant '{tenant.Slug}' with role '{user.Role}'.");

        return 0;
    }

    private static async Task<int> RunResetPasswordAsync(ServiceProvider provider, IReadOnlyDictionary<string, string?> options)
    {
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<JurisFlowDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<TenantContext>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        await EnsureDatabaseConnectivityAsync(context);

        var tenant = await RequireTenantAsync(context, NormalizeTenantSlug(RequireOption(options, "tenant")));
        tenantContext.Set(tenant.Id, tenant.Slug);

        var normalizedEmail = EmailAddressNormalizer.Normalize(RequireOption(options, "email"));
        var password = ResolvePassword(options);

        var user = await context.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail);
        if (user is null)
        {
            throw new InvalidOperationException($"User '{normalizedEmail}' was not found in tenant '{tenant.Slug}'.");
        }

        user.PasswordHash = PasswordHashingHelper.HashPassword(password, configuration);
        user.UpdatedAt = DateTime.UtcNow;

        await context.SaveChangesAsync();

        Console.WriteLine($"Reset password for '{user.Email}' in tenant '{tenant.Slug}'.");
        return 0;
    }

    private static async Task<Tenant> RequireTenantAsync(JurisFlowDbContext context, string slug)
    {
        var tenant = await context.Tenants.FirstOrDefaultAsync(t => t.Slug == slug);
        if (tenant is null)
        {
            throw new InvalidOperationException($"Tenant '{slug}' was not found.");
        }

        return tenant;
    }

    private static async Task EnsureDatabaseConnectivityAsync(JurisFlowDbContext context)
    {
        if (!await context.Database.CanConnectAsync())
        {
            throw new InvalidOperationException("Database connection failed. Check ConnectionStrings__DefaultConnection and Database__Provider.");
        }
    }

    private static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        var rawConnectionString = configuration.GetConnectionString("DefaultConnection") ?? DefaultConnectionString;
        var databaseProvider = ResolveDatabaseProvider(configuration, rawConnectionString);
        var resolvedConnectionString = ResolveDatabaseConnectionString(
            databaseProvider,
            rawConnectionString,
            Directory.GetCurrentDirectory());

        services.AddSingleton(configuration);
        services.AddLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddSimpleConsole(options =>
            {
                options.SingleLine = true;
                options.TimestampFormat = "HH:mm:ss ";
            });
            logging.SetMinimumLevel(LogLevel.Information);
        });

        services.AddScoped<TenantContext>();
        services.AddSingleton<DbEncryptionService>();
        services.AddDbContext<JurisFlowDbContext>(options =>
            ConfigureDatabaseProvider(options, databaseProvider, resolvedConnectionString));
    }

    private static IConfiguration BuildConfiguration()
    {
        var builder = new ConfigurationBuilder();

        foreach (var path in DiscoverServerAppSettingsPaths())
        {
            builder.AddJsonFile(path, optional: false, reloadOnChange: false);
        }

        builder.AddEnvironmentVariables();
        return builder.Build();
    }

    private static IEnumerable<string> DiscoverServerAppSettingsPaths()
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddCandidates(Directory.GetCurrentDirectory());
        AddCandidates(AppContext.BaseDirectory);

        return candidates;

        void AddCandidates(string startPath)
        {
            if (string.IsNullOrWhiteSpace(startPath))
            {
                return;
            }

            var current = Path.GetFullPath(startPath);

            for (var i = 0; i < 8; i++)
            {
                var directServerProject = Path.Combine(current, "JurisFlow.Server.csproj");
                if (File.Exists(directServerProject))
                {
                    AddIfExists(Path.Combine(current, "appsettings.json"));
                }

                AddIfExists(Path.Combine(current, "JurisFlow.Server", "appsettings.json"));

                var parent = Directory.GetParent(current);
                if (parent is null)
                {
                    break;
                }

                current = parent.FullName;
            }
        }

        void AddIfExists(string path)
        {
            if (File.Exists(path))
            {
                candidates.Add(path);
            }
        }
    }

    private static IReadOnlyDictionary<string, string?> ParseOptions(string[] args)
    {
        var options = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < args.Length; i++)
        {
            var token = args[i];
            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Unexpected argument '{token}'. Use --name value format.");
            }

            var trimmed = token[2..];
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                throw new InvalidOperationException("Empty option name.");
            }

            string key;
            string? value;
            var equalsIndex = trimmed.IndexOf('=');
            if (equalsIndex >= 0)
            {
                key = trimmed[..equalsIndex];
                value = trimmed[(equalsIndex + 1)..];
            }
            else
            {
                key = trimmed;
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    value = args[++i];
                }
                else
                {
                    value = "true";
                }
            }

            options[key] = value;
        }

        return options;
    }

    private static string ResolvePassword(IReadOnlyDictionary<string, string?> options)
    {
        if (options.TryGetValue("password-env", out var envVarName) && !string.IsNullOrWhiteSpace(envVarName))
        {
            var envValue = Environment.GetEnvironmentVariable(envVarName);
            if (string.IsNullOrWhiteSpace(envValue))
            {
                throw new InvalidOperationException($"Environment variable '{envVarName}' is empty or missing.");
            }

            return envValue;
        }

        return RequireOption(options, "password");
    }

    private static string RequireOption(IReadOnlyDictionary<string, string?> options, string name)
    {
        if (!options.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Missing required option '--{name}'.");
        }

        return value.Trim();
    }

    private static string NormalizeTenantSlug(string value)
    {
        var normalized = Regex.Replace(value.Trim().ToLowerInvariant(), "[^a-z0-9]+", "-");
        normalized = normalized.Trim('-');

        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException("Tenant slug must contain at least one alphanumeric character.");
        }

        return normalized;
    }

    private static bool IsHelp(string value)
    {
        return string.Equals(value, "help", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "--help", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "-h", StringComparison.OrdinalIgnoreCase);
    }

    private static int FailUnknownCommand(string message)
    {
        Console.Error.WriteLine(message);
        PrintHelp();
        return 1;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("JurisFlow.AdminTool");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run --project JurisFlow.AdminTool -p:UseAppHost=false -- tenant list");
        Console.WriteLine("  dotnet run --project JurisFlow.AdminTool -p:UseAppHost=false -- tenant create --name \"Juris Flow\" [--slug juris-flow]");
        Console.WriteLine("  dotnet run --project JurisFlow.AdminTool -p:UseAppHost=false -- user list [--tenant juris-flow]");
        Console.WriteLine("  dotnet run --project JurisFlow.AdminTool -p:UseAppHost=false -- user create --tenant juris-flow --email admin@jurisflow.local --name \"Admin User\" --role Admin --password \"...\"");
        Console.WriteLine("  dotnet run --project JurisFlow.AdminTool -p:UseAppHost=false -- user create-admin --tenant juris-flow --email admin@jurisflow.local --password \"...\" [--name \"Admin User\"]");
        Console.WriteLine("  dotnet run --project JurisFlow.AdminTool -p:UseAppHost=false -- user reset-password --tenant juris-flow --email admin@jurisflow.local --password \"...\"");
        Console.WriteLine();
        Console.WriteLine("Production note:");
        Console.WriteLine("  The tool reads the same environment variables as the server. Prefer --password-env ADMIN_PASSWORD to avoid shell history exposure.");
    }

    private static void PrintTenantHelp()
    {
        Console.WriteLine("Tenant commands:");
        Console.WriteLine("  tenant list");
        Console.WriteLine("  tenant create --name \"Juris Flow\" [--slug juris-flow]");
    }

    private static void PrintUserHelp()
    {
        Console.WriteLine("User commands:");
        Console.WriteLine("  user list [--tenant juris-flow]");
        Console.WriteLine("  user create --tenant juris-flow --email user@example.com --name \"Display Name\" --role Admin --password \"...\"");
        Console.WriteLine("  user create-admin --tenant juris-flow --email admin@jurisflow.local --password \"...\" [--name \"Admin User\"]");
        Console.WriteLine("  user reset-password --tenant juris-flow --email user@example.com --password \"...\"");
        Console.WriteLine();
        Console.WriteLine("You can replace --password with --password-env YOUR_ENV_VAR.");
    }

    private static string ResolveDatabaseProvider(IConfiguration configuration, string connectionString)
    {
        var configuredProvider = configuration["Database:Provider"]?.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(configuredProvider))
        {
            return configuredProvider switch
            {
                "sqlite" => "sqlite",
                "postgres" => "postgres",
                "postgresql" => "postgres",
                _ => throw new InvalidOperationException("Database:Provider must be 'sqlite' or 'postgres'.")
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

    private static string ResolveDatabaseConnectionString(string provider, string connectionString, string contentRoot)
    {
        return provider switch
        {
            "sqlite" => ResolveSqliteConnectionString(connectionString, contentRoot),
            "postgres" => NormalizePostgresConnectionString(connectionString),
            _ => throw new InvalidOperationException($"Unsupported database provider '{provider}'.")
        };
    }

    private static void ConfigureDatabaseProvider(DbContextOptionsBuilder options, string provider, string connectionString)
    {
        switch (provider)
        {
            case "sqlite":
                options.UseSqlite(connectionString);
                break;
            case "postgres":
                options.UseNpgsql(connectionString, npgsqlOptions =>
                {
                    npgsqlOptions.EnableRetryOnFailure(5, TimeSpan.FromSeconds(10), null);
                });
                break;
            default:
                throw new InvalidOperationException($"Unsupported database provider '{provider}'.");
        }
    }

    private static string ResolveSqliteConnectionString(string connectionString, string contentRoot)
    {
        var sqliteBuilder = new SqliteConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(sqliteBuilder.DataSource) ||
            sqliteBuilder.DataSource == ":memory:" ||
            Path.IsPathRooted(sqliteBuilder.DataSource))
        {
            return sqliteBuilder.ToString();
        }

        var dataDir = ResolveWritableDataDirectory(contentRoot);
        sqliteBuilder.DataSource = Path.Combine(dataDir, sqliteBuilder.DataSource);
        return sqliteBuilder.ToString();
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

        if (!string.IsNullOrWhiteSpace(uri.UserInfo))
        {
            var userInfoParts = uri.UserInfo.Split(':', 2);
            builder.Username = Uri.UnescapeDataString(userInfoParts[0]);
            if (userInfoParts.Length > 1)
            {
                builder.Password = Uri.UnescapeDataString(userInfoParts[1]);
            }
        }

        if (!string.IsNullOrWhiteSpace(uri.Query))
        {
            var query = uri.Query.TrimStart('?')
                .Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (var pair in query)
            {
                var parts = pair.Split('=', 2);
                var key = Uri.UnescapeDataString(parts[0]);
                var value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                builder[key] = value;
            }
        }

        if (string.IsNullOrWhiteSpace(builder.Database))
        {
            throw new InvalidOperationException("ConnectionStrings:DefaultConnection PostgreSQL URI must include a database name.");
        }

        return builder.ToString();
    }

    private static string ResolveWritableDataDirectory(string contentRoot)
    {
        var explicitDataDir = Environment.GetEnvironmentVariable("JURISFLOW_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(explicitDataDir))
        {
            var fullPath = Path.GetFullPath(explicitDataDir);
            Directory.CreateDirectory(fullPath);
            return fullPath;
        }

        var railwayVolumePath = Environment.GetEnvironmentVariable("RAILWAY_VOLUME_MOUNT_PATH");
        if (!string.IsNullOrWhiteSpace(railwayVolumePath))
        {
            var fullPath = Path.Combine(Path.GetFullPath(railwayVolumePath), "App_Data");
            Directory.CreateDirectory(fullPath);
            return fullPath;
        }

        var defaultPath = Path.Combine(contentRoot, "App_Data");
        Directory.CreateDirectory(defaultPath);
        return defaultPath;
    }
}
