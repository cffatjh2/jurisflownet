using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using JurisFlow.Server.Services;
using JurisFlow.Server.Middleware;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.RateLimiting;
using System.Text;
using Serilog;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Data.Sqlite;
using Npgsql;
using System.IO;
using System.Globalization;
using System.Threading.RateLimiting;
using System.IO.Compression;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.Authentication;

var builder = WebApplication.CreateBuilder(args);
const string CorsPolicyName = "AppCors";

ConfigureRuntimePortBinding(builder);

// Add services to the container.

builder.Services.AddMemoryCache();

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase, allowIntegerValues: true));
    });
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<IAppFileStorage, AppFileStorage>();
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes;
});
builder.Services.Configure<BrotliCompressionProviderOptions>(options =>
{
    options.Level = CompressionLevel.Fastest;
});
builder.Services.Configure<GzipCompressionProviderOptions>(options =>
{
    options.Level = CompressionLevel.Fastest;
});

var authLoginPermitLimit = Math.Clamp(builder.Configuration.GetValue("RateLimiting:AuthLogin:PermitLimit", 8), 1, 200);
var authLoginWindowSeconds = Math.Clamp(builder.Configuration.GetValue("RateLimiting:AuthLogin:WindowSeconds", 60), 1, 3600);
var clientAuthLoginPermitLimit = Math.Clamp(builder.Configuration.GetValue("RateLimiting:ClientAuthLogin:PermitLimit", authLoginPermitLimit), 1, 200);
var clientAuthLoginWindowSeconds = Math.Clamp(builder.Configuration.GetValue("RateLimiting:ClientAuthLogin:WindowSeconds", authLoginWindowSeconds), 1, 3600);
var authMfaPermitLimit = Math.Clamp(builder.Configuration.GetValue("RateLimiting:AuthMfa:PermitLimit", 10), 1, 200);
var authMfaWindowSeconds = Math.Clamp(builder.Configuration.GetValue("RateLimiting:AuthMfa:WindowSeconds", 60), 1, 3600);
var authRefreshPermitLimit = Math.Clamp(builder.Configuration.GetValue("RateLimiting:AuthRefresh:PermitLimit", 30), 1, 500);
var authRefreshWindowSeconds = Math.Clamp(builder.Configuration.GetValue("RateLimiting:AuthRefresh:WindowSeconds", 60), 1, 3600);
var clientAuthRefreshPermitLimit = Math.Clamp(builder.Configuration.GetValue("RateLimiting:ClientAuthRefresh:PermitLimit", authRefreshPermitLimit), 1, 500);
var clientAuthRefreshWindowSeconds = Math.Clamp(builder.Configuration.GetValue("RateLimiting:ClientAuthRefresh:WindowSeconds", authRefreshWindowSeconds), 1, 3600);
var clientMessagingSendPermitLimit = Math.Clamp(builder.Configuration.GetValue("RateLimiting:ClientMessagingSend:PermitLimit", 12), 1, 200);
var clientMessagingSendWindowSeconds = Math.Clamp(builder.Configuration.GetValue("RateLimiting:ClientMessagingSend:WindowSeconds", 60), 1, 3600);
var crmConflictSearchPermitLimit = Math.Clamp(builder.Configuration.GetValue("RateLimiting:CrmConflictSearch:PermitLimit", 20), 1, 200);
var crmConflictSearchWindowSeconds = Math.Clamp(builder.Configuration.GetValue("RateLimiting:CrmConflictSearch:WindowSeconds", 60), 1, 3600);
var adminDangerousOpsPermitLimit = Math.Clamp(builder.Configuration.GetValue("RateLimiting:AdminDangerousOps:PermitLimit", 3), 1, 50);
var adminDangerousOpsWindowSeconds = Math.Clamp(builder.Configuration.GetValue("RateLimiting:AdminDangerousOps:WindowSeconds", 300), 1, 3600);
var integrationWebhookPermitLimit = Math.Clamp(builder.Configuration.GetValue("RateLimiting:IntegrationWebhook:PermitLimit", 120), 1, 5000);
var integrationWebhookWindowSeconds = Math.Clamp(builder.Configuration.GetValue("RateLimiting:IntegrationWebhook:WindowSeconds", 60), 1, 3600);

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, token) =>
    {
        var retryAfterSeconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
            ? Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds))
            : 60;

        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsJsonAsync(
            new
            {
                message = "Too many requests. Please retry later.",
                retryAfterSeconds
            },
            cancellationToken: token);
    };

    options.AddPolicy("AuthLogin", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            BuildRateLimitPartitionKey(httpContext, "auth-login"),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = authLoginPermitLimit,
                Window = TimeSpan.FromSeconds(authLoginWindowSeconds),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    options.AddPolicy("ClientAuthLogin", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            BuildRateLimitPartitionKey(httpContext, "client-auth-login"),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = clientAuthLoginPermitLimit,
                Window = TimeSpan.FromSeconds(clientAuthLoginWindowSeconds),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    options.AddPolicy("AuthMfa", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            BuildRateLimitPartitionKey(httpContext, "auth-mfa"),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = authMfaPermitLimit,
                Window = TimeSpan.FromSeconds(authMfaWindowSeconds),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    options.AddPolicy("AuthRefresh", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            BuildRateLimitPartitionKey(httpContext, "auth-refresh"),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = authRefreshPermitLimit,
                Window = TimeSpan.FromSeconds(authRefreshWindowSeconds),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    options.AddPolicy("ClientAuthRefresh", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            BuildRateLimitPartitionKey(httpContext, "client-auth-refresh"),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = clientAuthRefreshPermitLimit,
                Window = TimeSpan.FromSeconds(clientAuthRefreshWindowSeconds),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    options.AddPolicy("ClientMessagingSend", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            BuildRateLimitPartitionKey(httpContext, "client-messaging-send"),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = clientMessagingSendPermitLimit,
                Window = TimeSpan.FromSeconds(clientMessagingSendWindowSeconds),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    options.AddPolicy("CrmConflictSearch", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            BuildRateLimitPartitionKey(httpContext, "crm-conflict-search"),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = crmConflictSearchPermitLimit,
                Window = TimeSpan.FromSeconds(crmConflictSearchWindowSeconds),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    options.AddPolicy("AdminDangerousOps", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            BuildRateLimitPartitionKey(httpContext, "admin-dangerous-ops"),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = adminDangerousOpsPermitLimit,
                Window = TimeSpan.FromSeconds(adminDangerousOpsWindowSeconds),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    options.AddPolicy("IntegrationWebhook", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            BuildRateLimitPartitionKey(httpContext, "integration-webhook"),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = integrationWebhookPermitLimit,
                Window = TimeSpan.FromSeconds(integrationWebhookWindowSeconds),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
// builder.Services.AddOpenApi(); // Commented out to avoid dependency issues for now if package missing

builder.Host.UseSerilog((ctx, lc) =>
{
    lc.ReadFrom.Configuration(ctx.Configuration)
      .Enrich.FromLogContext();
});

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("DefaultConnection is missing.");
}

var databaseProvider = ResolveDatabaseProvider(builder.Configuration, connectionString);
var resolvedConnectionString = ResolveDatabaseConnectionString(databaseProvider, connectionString, builder.Environment.ContentRootPath);
var databaseBootstrapMode = ResolveDatabaseBootstrapMode(builder.Configuration, databaseProvider);

var (allowedCorsOrigins, invalidCorsOrigins) = ResolveCorsOrigins(builder.Configuration);
if (invalidCorsOrigins.Count > 0)
{
    throw new InvalidOperationException(
        $"Invalid CORS origins in configuration: {string.Join(", ", invalidCorsOrigins)}");
}
var allowedCorsOriginSet = new HashSet<string>(allowedCorsOrigins, StringComparer.OrdinalIgnoreCase);
var renderSiblingCorsRule = ResolveRenderSiblingCorsRule(builder.Configuration, allowedCorsOriginSet);

if (builder.Environment.IsProduction())
{
    EnsureProductionSecurityRequirements(builder.Configuration);

    if (allowedCorsOrigins.Count == 0)
    {
        throw new InvalidOperationException(
            "CORS origin whitelist is empty in production. Configure Cors:AllowedOrigins (or Cors:AllowedOriginsCsv).");
    }
}
else if (allowedCorsOrigins.Count == 0)
{
    allowedCorsOrigins.AddRange(new[]
    {
        "http://localhost:5173",
        "http://127.0.0.1:5173",
        "http://localhost:3000",
        "http://127.0.0.1:3000"
    });
}

builder.Services.AddDbContext<JurisFlowDbContext>(options =>
    ConfigureDatabaseProvider(options, databaseProvider, resolvedConnectionString));

builder.Services.AddSingleton<DbEncryptionService>();
builder.Services.AddSingleton<DocumentEncryptionService>();
builder.Services.AddSingleton<DocumentTextExtractor>();
builder.Services.AddScoped<TenantContext>();
builder.Services.AddSingleton<TenantJobRunner>();

builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicyName, policy =>
    {
        policy.SetIsOriginAllowed(origin => IsCorsOriginAllowed(origin, allowedCorsOriginSet, renderSiblingCorsRule))
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});


builder.Services.AddHealthChecks()
    .AddDbContextCheck<JurisFlowDbContext>("db");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var jwtKey = builder.Configuration["Jwt:Key"];
        if (string.IsNullOrWhiteSpace(jwtKey))
        {
            throw new InvalidOperationException("JWT Key is missing in configuration.");
        }
        if (!builder.Environment.IsDevelopment())
        {
            if (jwtKey.Contains("DevPurposeOnly", StringComparison.OrdinalIgnoreCase) ||
                jwtKey.Contains("ChangeInProduction", StringComparison.OrdinalIgnoreCase) ||
                jwtKey.Length < 32)
            {
                throw new InvalidOperationException("JWT Key is not production-safe. Set a strong secret in Jwt:Key.");
            }
        }

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("SecurityAdminOnly", policy =>
    {
        policy.RequireRole("SecurityAdmin");
    });

    options.AddPolicy("StaffOnly", policy =>
    {
        policy.RequireRole("Admin", "Partner", "Associate", "Employee", "Attorney", "Staff", "Manager");
    });

    options.AddPolicy("StaffOrClient", policy =>
    {
        policy.RequireRole("Admin", "Partner", "Associate", "Employee", "Attorney", "Staff", "Manager", "Client");
    });
});

builder.Services.AddHttpContextAccessor();
builder.Services.Configure<TrustAccountingOptions>(builder.Configuration.GetSection("TrustAccounting"));
builder.Services.AddTransient<IClaimsTransformation, RoleAliasClaimsTransformation>();
builder.Services.AddScoped<AuditLogger>();
builder.Services.AddScoped<AuditLogIntegrityService>();
builder.Services.AddSingleton<AuditLogWriteQueue>();
builder.Services.AddHostedService<AuditLogWriteHostedService>();
builder.Services.AddScoped<BillingPeriodLockService>();
builder.Services.AddScoped<PasswordVerificationService>();
builder.Services.AddScoped<MatterAccessService>();
builder.Services.AddScoped<MatterClientLinkService>();
builder.Services.AddScoped<MatterWorkflowTriggerDispatcher>();
builder.Services.AddSingleton<OutcomeFeePlannerTriggerQueue>();
builder.Services.AddHostedService<OutcomeFeePlannerTriggerHostedService>();
builder.Services.AddSingleton<BackupJobQueue>();
builder.Services.AddScoped<BackupService>();
builder.Services.AddHostedService<BackupJobHostedService>();
builder.Services.AddScoped<FirmStructureService>();
builder.Services.AddSingleton<StripePaymentService>();
builder.Services.AddSingleton<LemonSqueezyCheckoutService>();
builder.Services.AddScoped<PaymentPlanService>();
builder.Services.AddScoped<DocumentIndexService>();
builder.Services.AddScoped<RetentionService>();
builder.Services.AddSingleton<PasswordPolicyService>();
builder.Services.AddSingleton<SessionTokenService>();
builder.Services.AddSingleton<LoginAttemptService>();
builder.Services.AddScoped<TrustComplianceService>();
builder.Services.AddScoped<TrustActionAuthorizationService>();
builder.Services.AddScoped<TrustPolicyResolverService>();
builder.Services.AddScoped<TrustAccountingService>();
builder.Services.AddScoped<TrustStatementIngestionService>();
builder.Services.AddScoped<TrustOpsInboxService>();
builder.Services.AddScoped<TrustCloseAutomationService>();
builder.Services.AddScoped<TrustComplianceExportService>();
builder.Services.AddScoped<TrustBundleIntegrityService>();
builder.Services.AddScoped<TrustRecoveryService>();
builder.Services.AddScoped<TwilioSmsService>();
builder.Services.AddScoped<SmsReminderService>();
builder.Services.AddScoped<OutboundEmailService>();
builder.Services.AddScoped<HolidaySeedService>();
builder.Services.AddScoped<EfilingAutomationService>();
builder.Services.AddScoped<JurisdictionRulesPlatformService>();
builder.Services.AddScoped<TrustRiskRadarService>();
builder.Services.AddScoped<LegalBillingEngineService>();
builder.Services.AddScoped<OutcomeFeePlannerService>();
builder.Services.AddScoped<ClientTransparencyService>();
builder.Services.AddSingleton<IntegrationPiiMinimizationService>();
builder.Services.AddScoped<IIntegrationOperationsGuard, IntegrationOperationsGuard>();
builder.Services.AddScoped<IntegrationConnectorService>();
builder.Services.AddScoped<IntegrationWebhookService>();
builder.Services.AddScoped<AppDirectoryOnboardingService>();
builder.Services.AddSingleton<IIntegrationSecretAccessPolicy, IntegrationSecretAccessPolicy>();
builder.Services.AddSingleton<IIntegrationSecretKeyProvider, IntegrationSecretKeyProvider>();
builder.Services.AddSingleton<IIntegrationSecretCryptoService, IntegrationSecretCryptoService>();
builder.Services.AddScoped<IIntegrationSecretStore, IntegrationSecretStore>();
builder.Services.AddScoped<IIntegrationConnector, StripeIntegrationConnector>();
builder.Services.AddScoped<IIntegrationConnector, GoogleCalendarIntegrationConnector>();
builder.Services.AddScoped<IIntegrationConnector, QuickBooksIntegrationConnector>();
builder.Services.AddScoped<IIntegrationConnector, XeroIntegrationConnector>();
builder.Services.AddScoped<IIntegrationConnector, OutlookCalendarIntegrationConnector>();
builder.Services.AddScoped<IIntegrationConnector, GoogleGmailIntegrationConnector>();
builder.Services.AddScoped<IIntegrationConnector, OutlookMailIntegrationConnector>();
builder.Services.AddScoped<IIntegrationConnector, SharePointOneDriveIntegrationConnector>();
builder.Services.AddScoped<IIntegrationConnector, GoogleDriveIntegrationConnector>();
builder.Services.AddScoped<IIntegrationConnector, NetDocumentsIntegrationConnector>();
builder.Services.AddScoped<IIntegrationConnector, IManageIntegrationConnector>();
builder.Services.AddScoped<IIntegrationConnector, BusinessCentralIntegrationConnector>();
builder.Services.AddScoped<IIntegrationConnector, NetSuiteIntegrationConnector>();
builder.Services.AddScoped<IIntegrationConnector, CourtListenerDocketsIntegrationConnector>();
builder.Services.AddScoped<IIntegrationConnector, CourtListenerRecapIntegrationConnector>();
builder.Services.AddScoped<IIntegrationConnector, OneLegalEfileIntegrationConnector>();
builder.Services.AddScoped<IIntegrationConnector, FileAndServeXpressEfileIntegrationConnector>();
builder.Services.AddScoped<IIntegrationConnector, LegacyIntegrationConnector>();
builder.Services.AddScoped<IntegrationConnectorRegistry>();
builder.Services.AddScoped<IntegrationSyncRunner>();
builder.Services.AddScoped<IntegrationCanonicalActionRunner>();
builder.Services.AddHostedService<RetentionHostedService>();
builder.Services.AddScoped<DeadlineReminderService>();
builder.Services.AddHostedService<DeadlineReminderHostedService>();
builder.Services.AddHostedService<OperationsJobHostedService>();
builder.Services.AddHostedService<IntegrationSecretMaintenanceHostedService>();
builder.Services.AddScoped<SignatureAuditTrailService>();
builder.Services.AddScoped<SignatureLifecycleService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    // app.MapOpenApi();
}

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseResponseCompression();

app.UseSerilogRequestLogging();

app.UseMiddleware<SecurityHeadersMiddleware>();

app.UseCors(CorsPolicyName);
app.UseRateLimiter();

app.UseAuthentication();
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseMiddleware<SessionValidationMiddleware>();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

// Seeding
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<JurisFlowDbContext>();
        var startupLogger = services.GetRequiredService<ILogger<Program>>();
        InitializeDatabase(context, databaseProvider, databaseBootstrapMode);
        await PostgresSchemaCompatibility.EnsureCriticalColumnsAsync(context, startupLogger);
        await PostgresLegacyTrustSchemaCompatibility.EnsureAsync(context, startupLogger);

        var tenantContext = services.GetRequiredService<TenantContext>();
        var defaultTenantName = builder.Configuration["Tenancy:DefaultTenantName"] ?? "JurisFlow Legal";
        var defaultTenantSlug = TenantSeedHelper.NormalizeSlug(builder.Configuration["Tenancy:DefaultTenantSlug"] ?? "default");

        var tenant = context.Tenants.FirstOrDefault(t => t.Slug == defaultTenantSlug);
        if (tenant == null)
        {
            var existingTenants = context.Tenants
                .OrderBy(t => t.CreatedAt)
                .ToList();

            if (existingTenants.Count == 1)
            {
                tenant = existingTenants[0];
                tenant.Slug = defaultTenantSlug;
                tenant.Name = defaultTenantName;
                tenant.IsActive = true;
                tenant.UpdatedAt = DateTime.UtcNow;
                context.SaveChanges();
            }
            else
            {
                tenant = new Tenant
                {
                    Name = defaultTenantName,
                    Slug = defaultTenantSlug,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                context.Tenants.Add(tenant);
                context.SaveChanges();
            }
        }

        tenantContext.Set(tenant.Id, tenant.Slug);
        await TenantSeedHelper.BackfillTenantIdsAsync(context, tenant.Id);

        var seedEnabled = builder.Configuration.GetValue("Seed:Enabled", app.Environment.IsDevelopment());
        if (seedEnabled)
        {
            var passwordPolicy = services.GetRequiredService<PasswordPolicyService>();
            var resetSeedPasswords = builder.Configuration.GetValue("Seed:ResetPasswords", false);

            var adminEmail = builder.Configuration["Seed:AdminEmail"] ?? "admin@jurisflow.local";
            var adminPassword = builder.Configuration["Seed:AdminPassword"] ?? "ChangeMe123!";

            var adminPasswordResult = passwordPolicy.Validate(adminPassword, adminEmail, "Admin User");
            if (!adminPasswordResult.IsValid)
            {
                throw new InvalidOperationException("Seed admin password does not meet security requirements.");
            }

            var normalizedAdminEmail = EmailAddressNormalizer.Normalize(adminEmail);
            var adminUser = context.Users.FirstOrDefault(u => u.NormalizedEmail == normalizedAdminEmail);
            if (adminUser == null)
            {
                var passwordHash = PasswordHashingHelper.HashPassword(adminPassword, builder.Configuration);
                context.Users.Add(new JurisFlow.Server.Models.User
                {
                    Email = adminEmail,
                    NormalizedEmail = normalizedAdminEmail,
                    Name = "Admin User",
                    Role = "Admin",
                    PasswordHash = passwordHash
                });
            }
            else
            {
                adminUser.Email = adminEmail;
                adminUser.NormalizedEmail = normalizedAdminEmail;
                adminUser.Name = "Admin User";
                adminUser.Role = "Admin";

                if (resetSeedPasswords || string.IsNullOrWhiteSpace(adminUser.PasswordHash))
                {
                    adminUser.PasswordHash = PasswordHashingHelper.HashPassword(adminPassword, builder.Configuration);
                }
            }

            var portalSeedEnabled = builder.Configuration.GetValue("Seed:PortalClientEnabled", false);
            var demoClientEmail = builder.Configuration["Seed:PortalClientEmail"] ?? "client.demo@jurisflow.local";

            if (portalSeedEnabled)
            {
                // Seed a portal-enabled demo client so you can log in as a client user
                var demoClientPassword = builder.Configuration["Seed:PortalClientPassword"] ?? "ChangeMe123!";

                var demoPasswordResult = passwordPolicy.Validate(demoClientPassword, demoClientEmail, "Demo Client");
                if (!demoPasswordResult.IsValid)
                {
                    throw new InvalidOperationException("Seed client password does not meet security requirements.");
                }

                var normalizedDemoClientEmail = EmailAddressNormalizer.Normalize(demoClientEmail);
                var demoClient = context.Clients.FirstOrDefault(c => c.NormalizedEmail == normalizedDemoClientEmail);
                if (demoClient == null)
                {
                    var demoClientPasswordHash = PasswordHashingHelper.HashPassword(demoClientPassword, builder.Configuration);
                    demoClient = new Client
                    {
                        Name = "Demo Client",
                        Email = demoClientEmail,
                        NormalizedEmail = normalizedDemoClientEmail,
                        Phone = "555-0101",
                        Type = "Individual",
                        Status = "Active",
                        ClientNumber = "CLT-1001",
                        PortalEnabled = true,
                        PasswordHash = demoClientPasswordHash,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    context.Clients.Add(demoClient);
                }
                else
                {
                    if (resetSeedPasswords)
                    {
                        demoClient.PasswordHash = PasswordHashingHelper.HashPassword(demoClientPassword, builder.Configuration);
                    }
                    demoClient.NormalizedEmail = normalizedDemoClientEmail;
                    demoClient.PortalEnabled = true;
                    demoClient.Status = "Active";
                }
            }
            else
            {
                var normalizedDemoClientEmail = EmailAddressNormalizer.Normalize(demoClientEmail);
                var demoClient = context.Clients.FirstOrDefault(c => c.NormalizedEmail == normalizedDemoClientEmail);
                if (demoClient != null)
                {
                    demoClient.PortalEnabled = false;
                    demoClient.PasswordHash = null;
                    demoClient.Status = "Inactive";
                    demoClient.UpdatedAt = DateTime.UtcNow;
                }
            }

            context.SaveChanges();
        }

        if (!context.RetentionPolicies.Any())
        {
            context.RetentionPolicies.AddRange(new[]
            {
                new RetentionPolicy { EntityName = "AuditLog", RetentionDays = 365 },
                new RetentionPolicy { EntityName = "Notification", RetentionDays = 180 },
                new RetentionPolicy { EntityName = "ClientMessage", RetentionDays = 365 },
                new RetentionPolicy { EntityName = "StaffMessage", RetentionDays = 365 },
                new RetentionPolicy { EntityName = "ResearchSession", RetentionDays = 90 },
                new RetentionPolicy { EntityName = "SignatureRequest", RetentionDays = 365 },
                new RetentionPolicy { EntityName = "AuthSession", RetentionDays = 30 }
            });
            context.SaveChanges();
        }

        var holidaysSeedEnabled = builder.Configuration.GetValue("Holidays:SeedEnabled", seedEnabled);
        if (holidaysSeedEnabled)
        {
            var holidaySeedService = services.GetRequiredService<HolidaySeedService>();
            var startYear = builder.Configuration.GetValue("Holidays:SeedStartYear", DateTime.UtcNow.Year);
            var yearsAhead = builder.Configuration.GetValue("Holidays:SeedYearsAhead", 2);
            await holidaySeedService.EnsureSeededAsync(context, startYear, yearsAhead);
        }
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred seeding the DB.");
    }
}

app.Run();

static (List<string> validOrigins, List<string> invalidOrigins) ResolveCorsOrigins(IConfiguration configuration)
{
    var configuredOrigins = new List<string>();

    var sectionOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
    if (sectionOrigins != null)
    {
        configuredOrigins.AddRange(sectionOrigins);
    }

    var csvOrigins = configuration["Cors:AllowedOriginsCsv"];
    if (!string.IsNullOrWhiteSpace(csvOrigins))
    {
        configuredOrigins.AddRange(csvOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    var validOrigins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var invalidOrigins = new List<string>();

    foreach (var rawOrigin in configuredOrigins.Where(o => !string.IsNullOrWhiteSpace(o)))
    {
        if (TryNormalizeOrigin(rawOrigin, out var normalizedOrigin))
        {
            validOrigins.Add(normalizedOrigin);
        }
        else
        {
            invalidOrigins.Add(rawOrigin);
        }
    }

    return (validOrigins.OrderBy(o => o, StringComparer.OrdinalIgnoreCase).ToList(), invalidOrigins);
}

static (string scheme, string baseServiceName)? ResolveRenderSiblingCorsRule(
    IConfiguration configuration,
    IEnumerable<string> allowedCorsOrigins)
{
    var allowRenderSiblingOrigins = configuration.GetValue("Cors:AllowRenderSiblingOrigins", true);
    if (!allowRenderSiblingOrigins)
    {
        return null;
    }

    static bool TryBuildRuleFromOrigin(string origin, out (string scheme, string baseServiceName) rule)
    {
        rule = default;

        if (!TryNormalizeOrigin(origin, out var normalizedOrigin) ||
            !Uri.TryCreate(normalizedOrigin, UriKind.Absolute, out var uri))
        {
            return false;
        }

        const string OnRenderSuffix = ".onrender.com";
        if (!uri.Host.EndsWith(OnRenderSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var serviceName = uri.Host[..^OnRenderSuffix.Length].Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            return false;
        }

        var lastDashIndex = serviceName.LastIndexOf('-');
        if (lastDashIndex > 0 && lastDashIndex < serviceName.Length - 1)
        {
            var possibleNumericSuffix = serviceName[(lastDashIndex + 1)..];
            if (possibleNumericSuffix.All(char.IsDigit))
            {
                serviceName = serviceName[..lastDashIndex];
            }
        }

        if (string.IsNullOrWhiteSpace(serviceName))
        {
            return false;
        }

        rule = (uri.Scheme.ToLowerInvariant(), serviceName);
        return true;
    }

    var configuredRenderBaseUrl = configuration["Cors:RenderSiblingBaseUrl"];
    if (!string.IsNullOrWhiteSpace(configuredRenderBaseUrl) &&
        TryBuildRuleFromOrigin(configuredRenderBaseUrl, out var configuredRule))
    {
        return configuredRule;
    }

    var renderExternalUrl = configuration["RENDER_EXTERNAL_URL"]
        ?? Environment.GetEnvironmentVariable("RENDER_EXTERNAL_URL");

    if (!string.IsNullOrWhiteSpace(renderExternalUrl) &&
        TryBuildRuleFromOrigin(renderExternalUrl, out var renderRule))
    {
        return renderRule;
    }

    var renderServiceName = configuration["RENDER_SERVICE_NAME"]
        ?? Environment.GetEnvironmentVariable("RENDER_SERVICE_NAME");

    if (TryBuildRuleFromServiceName(renderServiceName, "https", out var renderServiceRule))
    {
        return renderServiceRule;
    }

    foreach (var origin in allowedCorsOrigins)
    {
        if (TryBuildRuleFromOrigin(origin, out var fallbackRule))
        {
            return fallbackRule;
        }
    }

    return null;

    static bool TryBuildRuleFromServiceName(string? rawServiceName, string scheme, out (string scheme, string baseServiceName) rule)
    {
        rule = default;

        if (string.IsNullOrWhiteSpace(rawServiceName))
        {
            return false;
        }

        var serviceName = rawServiceName.Trim().ToLowerInvariant();
        var lastDashIndex = serviceName.LastIndexOf('-');
        if (lastDashIndex > 0 && lastDashIndex < serviceName.Length - 1)
        {
            var possibleNumericSuffix = serviceName[(lastDashIndex + 1)..];
            if (possibleNumericSuffix.All(char.IsDigit))
            {
                serviceName = serviceName[..lastDashIndex];
            }
        }

        if (string.IsNullOrWhiteSpace(serviceName))
        {
            return false;
        }

        rule = (scheme.ToLowerInvariant(), serviceName);
        return true;
    }
}

static bool IsCorsOriginAllowed(
    string origin,
    ISet<string> allowedCorsOrigins,
    (string scheme, string baseServiceName)? renderSiblingCorsRule)
{
    if (!TryNormalizeOrigin(origin, out var normalizedOrigin))
    {
        return false;
    }

    if (allowedCorsOrigins.Contains(normalizedOrigin))
    {
        return true;
    }

    if (!renderSiblingCorsRule.HasValue ||
        !Uri.TryCreate(normalizedOrigin, UriKind.Absolute, out var uri))
    {
        return false;
    }

    var rule = renderSiblingCorsRule.Value;
    if (!string.Equals(uri.Scheme, rule.scheme, StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    const string OnRenderSuffix = ".onrender.com";
    if (!uri.Host.EndsWith(OnRenderSuffix, StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    var serviceName = uri.Host[..^OnRenderSuffix.Length].Trim().ToLowerInvariant();
    if (string.Equals(serviceName, rule.baseServiceName, StringComparison.OrdinalIgnoreCase))
    {
        return true;
    }

    var expectedPrefix = $"{rule.baseServiceName}-";
    if (!serviceName.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    var numericSuffix = serviceName[expectedPrefix.Length..];
    return numericSuffix.Length > 0 && numericSuffix.All(char.IsDigit);
}

static bool TryNormalizeOrigin(string rawOrigin, out string normalizedOrigin)
{
    normalizedOrigin = string.Empty;
    var value = rawOrigin.Trim();

    if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
    {
        return false;
    }

    if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    if ((!string.IsNullOrEmpty(uri.AbsolutePath) && uri.AbsolutePath != "/") ||
        !string.IsNullOrEmpty(uri.Query) ||
        !string.IsNullOrEmpty(uri.Fragment))
    {
        return false;
    }

    normalizedOrigin = uri.GetLeftPart(UriPartial.Authority);
    return true;
}

static string BuildRateLimitPartitionKey(HttpContext context, string policyName)
{
    var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    var tenantId = context.Request.Headers["X-Tenant-Id"].FirstOrDefault();
    var tenantSlug = context.Request.Headers["X-Tenant-Slug"].FirstOrDefault()
                     ?? context.Request.Query["tenantSlug"].FirstOrDefault()
                     ?? context.Request.Query["tenant"].FirstOrDefault();

    var tenant = !string.IsNullOrWhiteSpace(tenantId)
        ? tenantId
        : !string.IsNullOrWhiteSpace(tenantSlug)
            ? tenantSlug
            : "no-tenant";

    return $"{policyName}:{tenant}:{ip}";
}

static void EnsureProductionSecurityRequirements(IConfiguration configuration)
{
    var errors = new List<string>();

    RequireFlagEnabled(configuration, "Security:MfaEnforced", errors);
    RequireFlagEnabled(configuration, "Security:DocumentEncryptionEnabled", errors);
    RequireFlagEnabled(configuration, "Security:DbEncryptionEnabled", errors);
    RequireFlagEnabled(configuration, "Security:AuditLogImmutable", errors);
    RequireFlagEnabled(configuration, "Security:AuditLogFailClosed", errors);

    RequireBase64Key(configuration, "Security:DocumentEncryptionKey", exactBytes: 32, minBytes: null, errors);
    RequireBase64Key(configuration, "Security:DbEncryptionKey", exactBytes: 32, minBytes: null, errors);
    RequireBase64Key(configuration, "Security:AuditLogKey", exactBytes: null, minBytes: 32, errors);
    RequireIntegrationSecretProtection(configuration, errors);

    if (errors.Count > 0)
    {
        throw new InvalidOperationException(
            $"Production security requirements are not satisfied:{Environment.NewLine}- {string.Join(Environment.NewLine + "- ", errors)}");
    }
}

static void RequireIntegrationSecretProtection(IConfiguration configuration, List<string> errors)
{
    var provider = (configuration["Security:IntegrationSecrets:Provider"] ?? "config")
        .Trim()
        .ToLowerInvariant();

    if (configuration.GetValue("Security:IntegrationSecrets:LegacyPlaintextAllowed", false))
    {
        errors.Add("Security:IntegrationSecrets:LegacyPlaintextAllowed must be false in production.");
    }

    string activeKeyIdKey;
    string keysPrefix;
    switch (provider)
    {
        case "keyvault":
            activeKeyIdKey = "Security:IntegrationSecrets:KeyVault:ActiveKeyId";
            keysPrefix = "Security:IntegrationSecrets:KeyVault:DataKeys";
            break;
        case "kms":
            activeKeyIdKey = "Security:IntegrationSecrets:Kms:ActiveKeyId";
            keysPrefix = "Security:IntegrationSecrets:Kms:DataKeys";
            break;
        case "config":
            activeKeyIdKey = "Security:IntegrationSecrets:ActiveKeyId";
            keysPrefix = "Security:IntegrationSecrets:Keys";
            break;
        default:
            errors.Add("Security:IntegrationSecrets:Provider must be one of: config, kms, keyvault.");
            return;
    }

    var activeKeyId = configuration[activeKeyIdKey]?.Trim();
    if (string.IsNullOrWhiteSpace(activeKeyId))
    {
        errors.Add($"{activeKeyIdKey} is required.");
        return;
    }

    if (!IntegrationSecretConfigurationResolver.TryResolveConfiguredKeyValue(
        configuration,
        keysPrefix,
        activeKeyId,
        out var resolvedActiveKeyId,
        out var configuredKey))
    {
        errors.Add($"{keysPrefix}:{activeKeyId} is required.");
        return;
    }

    var activeKeyPath = $"{keysPrefix}:{activeKeyId}";
    RequireBase64KeyValue(configuredKey, activeKeyPath, exactBytes: 32, minBytes: null, errors);

    if (provider == "config")
    {
        if (string.Equals(configuredKey, "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=", StringComparison.Ordinal))
        {
            var resolvedPath = $"{keysPrefix}:{resolvedActiveKeyId}";
            errors.Add($"{resolvedPath} must be overridden in production.");
        }
    }
}

static void RequireFlagEnabled(IConfiguration configuration, string key, List<string> errors)
{
    if (!configuration.GetValue(key, false))
    {
        errors.Add($"{key} must be set to true.");
    }
}

static void RequireBase64Key(
    IConfiguration configuration,
    string key,
    int? exactBytes,
    int? minBytes,
    List<string> errors)
{
    RequireBase64KeyValue(configuration[key], key, exactBytes, minBytes, errors);
}

static void RequireBase64KeyValue(
    string? raw,
    string displayKey,
    int? exactBytes,
    int? minBytes,
    List<string> errors)
{
    if (string.IsNullOrWhiteSpace(raw))
    {
        errors.Add($"{displayKey} is required.");
        return;
    }

    try
    {
        var bytes = Convert.FromBase64String(raw);

        if (exactBytes.HasValue && bytes.Length != exactBytes.Value)
        {
            errors.Add($"{displayKey} must decode to exactly {exactBytes.Value} bytes.");
            return;
        }

        if (minBytes.HasValue && bytes.Length < minBytes.Value)
        {
            errors.Add($"{displayKey} must decode to at least {minBytes.Value.ToString(CultureInfo.InvariantCulture)} bytes.");
        }
    }
    catch (FormatException)
    {
        errors.Add($"{displayKey} must be valid base64.");
    }
}

static void ConfigureRuntimePortBinding(WebApplicationBuilder builder)
{
    var rawPort = Environment.GetEnvironmentVariable("PORT");
    if (!int.TryParse(rawPort, out var port) || port <= 0)
    {
        return;
    }

    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

static string ResolveDatabaseProvider(IConfiguration configuration, string connectionString)
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

static string ResolveDatabaseConnectionString(string provider, string connectionString, string contentRoot)
{
    return provider switch
    {
        "sqlite" => ResolveSqliteConnectionString(connectionString, contentRoot),
        "postgres" => NormalizePostgresConnectionString(connectionString),
        _ => throw new InvalidOperationException($"Unsupported database provider '{provider}'.")
    };
}

static void ConfigureDatabaseProvider(DbContextOptionsBuilder options, string provider, string connectionString)
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

static void InitializeDatabase(JurisFlowDbContext context, string provider, string bootstrapMode)
{
    if (string.Equals(bootstrapMode, "ensure-created", StringComparison.OrdinalIgnoreCase))
    {
        if (string.Equals(provider, "postgres", StringComparison.OrdinalIgnoreCase))
        {
            InitializePostgresSchema(context);
            return;
        }

        context.Database.EnsureCreated();
        return;
    }

    context.Database.Migrate();
}

static void InitializePostgresSchema(JurisFlowDbContext context)
{
    if (PostgresTableExists(context, "Tenants"))
    {
        return;
    }

    // Supabase pre-provisions system tables, so EnsureCreated() no-ops even when the app schema is empty.
    context.GetService<IRelationalDatabaseCreator>().CreateTables();
}

static bool PostgresTableExists(JurisFlowDbContext context, string tableName)
{
    var connection = context.Database.GetDbConnection();
    var shouldCloseConnection = connection.State != System.Data.ConnectionState.Open;
    if (shouldCloseConnection)
    {
        connection.Open();
    }

    try
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            select exists (
                select 1
                from information_schema.tables
                where table_schema = current_schema()
                  and table_name = @tableName
            );
            """;

        var tableNameParameter = command.CreateParameter();
        tableNameParameter.ParameterName = "@tableName";
        tableNameParameter.Value = tableName;
        command.Parameters.Add(tableNameParameter);

        var scalar = command.ExecuteScalar();
        return scalar is bool exists && exists;
    }
    finally
    {
        if (shouldCloseConnection)
        {
            connection.Close();
        }
    }
}

static string ResolveDatabaseBootstrapMode(IConfiguration configuration, string provider)
{
    var configuredMode = configuration["Database:BootstrapMode"]?.Trim().ToLowerInvariant();
    if (!string.IsNullOrWhiteSpace(configuredMode))
    {
        return configuredMode switch
        {
            "migrate" => "migrate",
            "ensure-created" => "ensure-created",
            "ensurecreated" => "ensure-created",
            _ => throw new InvalidOperationException("Database:BootstrapMode must be 'migrate' or 'ensure-created'.")
        };
    }

    return string.Equals(provider, "postgres", StringComparison.OrdinalIgnoreCase)
        ? "ensure-created"
        : "migrate";
}

static string ResolveSqliteConnectionString(string connectionString, string contentRoot)
{
    var sqliteBuilder = new SqliteConnectionStringBuilder(connectionString);
    if (string.IsNullOrWhiteSpace(sqliteBuilder.DataSource) ||
        sqliteBuilder.DataSource == ":memory:" ||
        Path.IsPathRooted(sqliteBuilder.DataSource))
    {
        return sqliteBuilder.ToString();
    }

    var serverRoot = contentRoot;
    if (!File.Exists(Path.Combine(contentRoot, "JurisFlow.Server.csproj")))
    {
        var candidate = Path.Combine(contentRoot, "JurisFlow.Server");
        if (File.Exists(Path.Combine(candidate, "JurisFlow.Server.csproj")))
        {
            serverRoot = candidate;
        }
    }

    var dataDir = ResolveWritableDataDirectory(serverRoot);
    var appDataPath = Path.Combine(dataDir, sqliteBuilder.DataSource);
    var legacyPath = Path.Combine(serverRoot, sqliteBuilder.DataSource);

    if (File.Exists(legacyPath) && !File.Exists(appDataPath))
    {
        File.Copy(legacyPath, appDataPath, true);
    }
    else if (File.Exists(legacyPath) && File.Exists(appDataPath))
    {
        var legacyInfo = new FileInfo(legacyPath);
        var appDataInfo = new FileInfo(appDataPath);
        if (appDataInfo.Length < 200 * 1024 && legacyInfo.Length > appDataInfo.Length)
        {
            var backupPath = $"{appDataPath}.bak-{DateTime.UtcNow:yyyyMMddHHmmss}";
            File.Copy(appDataPath, backupPath, true);
            File.Copy(legacyPath, appDataPath, true);
        }
    }

    sqliteBuilder.DataSource = appDataPath;
    return sqliteBuilder.ToString();
}

static string NormalizePostgresConnectionString(string connectionString)
{
    var normalized = connectionString.Trim();
    if (normalized.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
        normalized.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
    {
        return ConvertPostgresUriToConnectionString(normalized);
    }

    return normalized;
}

static string ConvertPostgresUriToConnectionString(string uriValue)
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

static string ResolveWritableDataDirectory(string serverRoot)
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

    var defaultPath = Path.Combine(serverRoot, "App_Data");
    Directory.CreateDirectory(defaultPath);
    return defaultPath;
}

public partial class Program { }
