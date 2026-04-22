using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using JurisFlow.Server.Services;
using System.Text.Json;
using System.Text.RegularExpressions;
using Task = System.Threading.Tasks.Task;

namespace JurisFlow.Server.Controllers
{
    [Route("api/settings")]
    [ApiController]
    [Authorize(Policy = "StaffOnly")]
    public class SettingsController : ControllerBase
    {
        private static readonly JsonSerializerOptions LegacyIntegrationJsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly JurisFlowDbContext _context;
        private readonly AuditLogger _auditLogger;
        private readonly IntegrationConnectorService _integrationConnectorService;
        private readonly IntegrationSyncRunner _integrationSyncRunner;
        private readonly IIntegrationSecretStore _integrationSecretStore;
        private readonly IIntegrationOperationsGuard _integrationOperationsGuard;

        public SettingsController(
            JurisFlowDbContext context,
            AuditLogger auditLogger,
            IntegrationConnectorService integrationConnectorService,
            IntegrationSyncRunner integrationSyncRunner,
            IIntegrationSecretStore integrationSecretStore,
            IIntegrationOperationsGuard integrationOperationsGuard)
        {
            _context = context;
            _auditLogger = auditLogger;
            _integrationConnectorService = integrationConnectorService;
            _integrationSyncRunner = integrationSyncRunner;
            _integrationSecretStore = integrationSecretStore;
            _integrationOperationsGuard = integrationOperationsGuard;
        }

        [HttpGet("billing")]
        [Authorize(Policy = "BillingRead")]
        public async Task<IActionResult> GetBillingSettings()
        {
            var settings = await GetBillingSettingsForReadAsync();
            return Ok(settings);
        }

        [HttpPut("billing")]
        [Authorize(Policy = "BillingSettingsWrite")]
        public async Task<IActionResult> UpdateBillingSettings([FromBody] BillingSettings dto)
        {
            var settings = await GetBillingSettingsForWriteAsync();

            settings.DefaultHourlyRate = dto.DefaultHourlyRate;
            settings.PartnerRate = dto.PartnerRate;
            settings.AssociateRate = dto.AssociateRate;
            settings.ParalegalRate = dto.ParalegalRate;
            settings.BillingIncrement = dto.BillingIncrement;
            settings.MinimumTimeEntry = dto.MinimumTimeEntry;
            settings.RoundingRule = dto.RoundingRule;
            settings.DefaultPaymentTerms = dto.DefaultPaymentTerms;
            settings.InvoicePrefix = dto.InvoicePrefix;
            settings.DefaultTaxRate = dto.DefaultTaxRate;
            settings.LedesEnabled = dto.LedesEnabled;
            settings.UtbmsCodesRequired = dto.UtbmsCodesRequired;
            settings.EvergreenRetainerMinimum = dto.EvergreenRetainerMinimum;
            settings.TrustBalanceAlerts = dto.TrustBalanceAlerts;
            settings.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await _auditLogger.LogAsync(HttpContext, "settings.billing.update", "BillingSettings", settings.Id, "Billing settings updated");

            return Ok(settings);
        }

        [HttpGet("firm")]
        [Authorize(Policy = "BillingRead")]
        public async Task<IActionResult> GetFirmSettings()
        {
            var settings = await GetFirmSettingsForReadAsync();
            return Ok(settings);
        }

        [HttpPut("firm")]
        [Authorize(Policy = "BillingSettingsWrite")]
        public async Task<IActionResult> UpdateFirmSettings([FromBody] FirmSettings dto)
        {
            var settings = await GetFirmSettingsForWriteAsync();

            settings.FirmName = dto.FirmName;
            settings.TaxId = dto.TaxId;
            settings.LedesFirmId = dto.LedesFirmId;
            settings.Address = dto.Address;
            settings.City = dto.City;
            settings.State = dto.State;
            settings.ZipCode = dto.ZipCode;
            settings.Phone = dto.Phone;
            settings.Website = dto.Website;
            settings.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await _auditLogger.LogAsync(HttpContext, "settings.firm.update", "FirmSettings", settings.Id, "Firm settings updated");

            return Ok(settings);
        }

        [HttpGet("integrations")]
        [Authorize(Policy = "BillingSettingsWrite")]
        public async Task<IActionResult> GetIntegrations()
        {
            var settings = await GetFirmSettingsForWriteAsync();
            await MigrateLegacyIntegrationsIfNeededAsync(settings);
            var items = await GetIntegrationsFromStoreAsync();
            return Ok(items);
        }

        [HttpGet("integrations/catalog")]
        [Authorize(Policy = "BillingRead")]
        public IActionResult GetIntegrationsCatalog()
        {
            var catalog = IntegrationProviderCatalog.Items
                .Select(item => new IntegrationCatalogItemDto
                {
                    ProviderKey = item.ProviderKey,
                    Provider = item.Provider,
                    Category = item.Category,
                    Description = item.Description,
                    ConnectionMode = item.ConnectionMode,
                    SupportsSync = item.SupportsSync,
                    SupportsWebhook = item.SupportsWebhook,
                    WebhookFirst = item.WebhookFirst,
                    FallbackPollingMinutes = item.FallbackPollingMinutes,
                    SupportedActions = item.SupportedActions.ToList(),
                    Capabilities = item.Capabilities.ToList()
                })
                .ToList();

            return Ok(catalog);
        }

        [HttpPut("integrations")]
        [Authorize(Policy = "BillingSettingsWrite")]
        public async Task<IActionResult> UpdateIntegrations([FromBody] IntegrationsUpdateDto dto)
        {
            var settings = await GetFirmSettingsForWriteAsync();
            await MigrateLegacyIntegrationsIfNeededAsync(settings);

            var items = dto?.Items ?? new List<IntegrationItemDto>();

            var normalized = items
                .Select(NormalizeIncomingIntegration)
                .Where(i => i != null)
                .Select(i => i!)
                .ToList();

            var incomingKeys = normalized
                .Select(i => BuildConnectionKey(i.ProviderKey ?? ResolveProviderKey(i.Provider, i.Category), i.Category))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var existing = await _context.IntegrationConnections.ToListAsync();

            var staleConnections = existing
                .Where(c => !incomingKeys.Contains(BuildConnectionKey(c.ProviderKey, c.Category)))
                .ToList();

            if (staleConnections.Count > 0)
            {
                var staleIds = staleConnections.Select(c => c.Id).ToList();
                if (staleIds.Count > 0)
                {
                    var staleSecrets = await _context.IntegrationSecrets
                        .Where(s => staleIds.Contains(s.ConnectionId))
                        .ToListAsync();
                    if (staleSecrets.Count > 0)
                    {
                        _context.IntegrationSecrets.RemoveRange(staleSecrets);
                    }
                }
                _context.IntegrationConnections.RemoveRange(staleConnections);
            }

            var now = DateTime.UtcNow;
            foreach (var item in normalized)
            {
                var providerKey = item.ProviderKey ?? ResolveProviderKey(item.Provider, item.Category);
                var connection = existing.FirstOrDefault(c =>
                    string.Equals(c.ProviderKey, providerKey, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(c.Category, item.Category, StringComparison.OrdinalIgnoreCase));

                if (connection == null)
                {
                    connection = new IntegrationConnection
                    {
                        Id = string.IsNullOrWhiteSpace(item.Id) ? Guid.NewGuid().ToString() : item.Id!,
                        ConnectedAt = now
                    };
                    _context.IntegrationConnections.Add(connection);
                    existing.Add(connection);
                }

                connection.ProviderKey = providerKey;
                connection.Provider = item.Provider;
                connection.Category = item.Category;
                connection.Status = NormalizeStatus(item.Status);
                connection.AccountLabel = NormalizeOptional(item.AccountLabel);
                connection.AccountEmail = NormalizeOptional(item.AccountEmail);
                connection.SyncEnabled = item.SyncEnabled;
                connection.LastSyncAt = item.LastSyncAt;
                connection.UpdatedAt = now;
            }

            settings.IntegrationsJson = null;
            settings.UpdatedAt = now;

            await _context.SaveChangesAsync();
            await _auditLogger.LogAsync(HttpContext, "settings.integrations.update", "IntegrationConnection", "bulk", "Integration settings updated");

            var stored = await GetIntegrationsFromStoreAsync();
            return Ok(stored);
        }

        [HttpPost("integrations/{providerKey}/connect")]
        [Authorize(Policy = "BillingSettingsWrite")]
        public async Task<IActionResult> ConnectIntegration(
            string providerKey,
            [FromBody] IntegrationConnectPayload? payload,
            CancellationToken cancellationToken)
        {
            var provider = IntegrationProviderCatalog.Find(providerKey);
            if (provider == null)
            {
                return BadRequest(new { message = "Unsupported integration provider." });
            }

            var settings = await GetFirmSettingsForWriteAsync();
            await MigrateLegacyIntegrationsIfNeededAsync(settings);

            var normalizedProviderKey = provider.ProviderKey;
            var connectDecision = await _integrationOperationsGuard.EvaluateForCurrentTenantAsync(
                normalizedProviderKey,
                IntegrationOperationKinds.Connect,
                cancellationToken);
            if (!connectDecision.Allowed)
            {
                return StatusCode(StatusCodes.Status423Locked, new { message = connectDecision.Message ?? "Integration connect is blocked." });
            }

            var connection = await _context.IntegrationConnections
                .FirstOrDefaultAsync(c => c.ProviderKey == normalizedProviderKey, cancellationToken);

            var connectionId = connection?.Id ?? Guid.NewGuid().ToString();
            var requestPayload = payload ?? new IntegrationConnectPayload();
            var result = await _integrationConnectorService.ConnectAsync(
                connectionId,
                normalizedProviderKey,
                requestPayload,
                connection?.MetadataJson,
                cancellationToken);

            if (!result.Success)
            {
                return BadRequest(new { message = result.ErrorMessage ?? "Integration connection failed." });
            }

            var now = DateTime.UtcNow;
            connection ??= new IntegrationConnection
            {
                Id = connectionId,
                ConnectedAt = now
            };

            connection.ProviderKey = normalizedProviderKey;
            connection.Provider = provider.Provider;
            connection.Category = provider.Category;
            connection.Status = NormalizeStatus(result.Status);
            connection.AccountLabel = NormalizeOptional(result.AccountLabel) ?? NormalizeOptional(requestPayload.AccountLabel);
            connection.AccountEmail = NormalizeOptional(result.AccountEmail) ?? NormalizeOptional(requestPayload.AccountEmail);
            connection.ExternalAccountId = NormalizeOptional(result.ExternalAccountId);
            connection.MetadataJson = result.MetadataJson;
            connection.SyncEnabled = requestPayload.SyncEnabled ?? connection.SyncEnabled;
            connection.UpdatedAt = now;

            if (_context.Entry(connection).State == EntityState.Detached)
            {
                _context.IntegrationConnections.Add(connection);
            }

            settings.IntegrationsJson = null;
            settings.UpdatedAt = now;

            await _context.SaveChangesAsync(cancellationToken);
            await _auditLogger.LogAsync(
                HttpContext,
                "settings.integrations.connect",
                "IntegrationConnection",
                connection.Id,
                $"Connected integration {connection.Provider} ({connection.ProviderKey}).");

            return Ok(MapConnectionToDto(connection, result.Notes ?? result.Message));
        }

        [HttpPost("integrations/{providerKey}/validate")]
        [Authorize(Policy = "BillingSettingsWrite")]
        public async Task<IActionResult> ValidateIntegration(
            string providerKey,
            [FromBody] IntegrationConnectPayload? payload,
            CancellationToken cancellationToken)
        {
            var provider = IntegrationProviderCatalog.Find(providerKey);
            if (provider == null)
            {
                return BadRequest(new { message = "Unsupported integration provider." });
            }

            var normalizedProviderKey = provider.ProviderKey;
            var validateDecision = await _integrationOperationsGuard.EvaluateForCurrentTenantAsync(
                normalizedProviderKey,
                IntegrationOperationKinds.Validate,
                cancellationToken);
            if (!validateDecision.Allowed)
            {
                return StatusCode(StatusCodes.Status423Locked, new { message = validateDecision.Message ?? "Integration validation is blocked." });
            }

            var connection = await _context.IntegrationConnections
                .FirstOrDefaultAsync(c => c.ProviderKey == normalizedProviderKey, cancellationToken);

            var requestPayload = payload ?? new IntegrationConnectPayload();
            var result = await _integrationConnectorService.ValidateAsync(
                connection?.Id,
                normalizedProviderKey,
                requestPayload,
                connection?.MetadataJson,
                cancellationToken);

            if (connection != null && !string.IsNullOrWhiteSpace(result.MetadataJson))
            {
                connection.MetadataJson = result.MetadataJson;
                connection.Status = result.Success ? "connected" : "error";
                connection.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync(cancellationToken);
            }

            return Ok(new IntegrationValidationResultDto
            {
                Success = result.Success,
                Message = result.Success ? result.Message : result.ErrorMessage,
                Status = result.Status,
                AccountLabel = result.AccountLabel,
                AccountEmail = result.AccountEmail,
                ExternalAccountId = result.ExternalAccountId
            });
        }

        [HttpPost("integrations/{providerKey}/sync")]
        [Authorize(Policy = "BillingSettingsWrite")]
        public async Task<IActionResult> SyncIntegration(string providerKey, CancellationToken cancellationToken)
        {
            var provider = IntegrationProviderCatalog.Find(providerKey);
            if (provider == null)
            {
                return BadRequest(new { message = "Unsupported integration provider." });
            }

            var normalizedProviderKey = provider.ProviderKey;
            var connection = await _context.IntegrationConnections
                .FirstOrDefaultAsync(c => c.ProviderKey == normalizedProviderKey, cancellationToken);

            if (connection == null)
            {
                return NotFound(new { message = "Integration connection was not found." });
            }

            var syncDecision = await _integrationOperationsGuard.EvaluateForConnectionAsync(
                connection,
                IntegrationOperationKinds.Sync,
                cancellationToken);
            if (!syncDecision.Allowed)
            {
                return StatusCode(StatusCodes.Status423Locked, new { message = syncDecision.Message ?? "Integration sync is blocked." });
            }

            var runResult = await _integrationSyncRunner.RunAsync(
                connection,
                new IntegrationSyncRunRequest
                {
                    Trigger = IntegrationRunTriggers.Manual,
                    IdempotencyKey = Request.Headers["Idempotency-Key"].FirstOrDefault()
                },
                cancellationToken);

            AuditTraceContext.SetIntegrationTrace(
                HttpContext,
                connectionId: connection.Id,
                providerKey: connection.ProviderKey,
                runId: runResult.RunId,
                correlationId: Request.Headers["X-Correlation-ID"].FirstOrDefault() ?? Request.Headers["Idempotency-Key"].FirstOrDefault());

            await _auditLogger.LogAsync(
                HttpContext,
                "settings.integrations.sync",
                "IntegrationConnection",
                connection.Id,
                runResult.Success
                    ? $"Sync succeeded for {connection.Provider} ({connection.ProviderKey})."
                    : $"Sync failed for {connection.Provider} ({connection.ProviderKey}): {runResult.Message}");

            return Ok(new IntegrationSyncResultDto
            {
                Success = runResult.Success,
                Message = runResult.Message,
                SyncedCount = runResult.SyncedCount,
                LastSyncAt = runResult.LastSyncAt,
                RunId = runResult.RunId,
                AttemptCount = runResult.AttemptCount,
                IsDeadLetter = runResult.IsDeadLetter,
                Deduplicated = runResult.Deduplicated
            });
        }

        [HttpDelete("integrations/{providerKey}")]
        [Authorize(Policy = "BillingSettingsWrite")]
        public async Task<IActionResult> DisconnectIntegration(string providerKey, CancellationToken cancellationToken)
        {
            var provider = IntegrationProviderCatalog.Find(providerKey);
            var normalizedProviderKey = provider?.ProviderKey ?? providerKey.Trim().ToLowerInvariant();

            var connection = await _context.IntegrationConnections
                .FirstOrDefaultAsync(c => c.ProviderKey == normalizedProviderKey, cancellationToken);
            if (connection == null)
            {
                return NoContent();
            }

            await _integrationSecretStore.DeleteAsync(connection.Id, IntegrationSecretScope.Disconnect, cancellationToken);
            _context.IntegrationConnections.Remove(connection);
            await _context.SaveChangesAsync(cancellationToken);

            await _auditLogger.LogAsync(
                HttpContext,
                "settings.integrations.disconnect",
                "IntegrationConnection",
                connection.Id,
                $"Disconnected integration {connection.Provider} ({connection.ProviderKey}).");

            return NoContent();
        }

        private async Task<BillingSettings> GetBillingSettingsForReadAsync()
        {
            return await _context.BillingSettings.FirstOrDefaultAsync() ?? new BillingSettings();
        }

        private async Task<BillingSettings> GetBillingSettingsForWriteAsync()
        {
            var settings = await _context.BillingSettings.FirstOrDefaultAsync();
            if (settings == null)
            {
                settings = new BillingSettings
                {
                    Id = Guid.NewGuid().ToString()
                };
                _context.BillingSettings.Add(settings);
                await _context.SaveChangesAsync();
            }
            return settings;
        }

        private async Task<FirmSettings> GetFirmSettingsForReadAsync()
        {
            return await _context.FirmSettings.FirstOrDefaultAsync() ?? new FirmSettings();
        }

        private async Task<FirmSettings> GetFirmSettingsForWriteAsync()
        {
            var settings = await _context.FirmSettings.FirstOrDefaultAsync();
            if (settings == null)
            {
                settings = new FirmSettings
                {
                    Id = Guid.NewGuid().ToString()
                };
                _context.FirmSettings.Add(settings);
                await _context.SaveChangesAsync();
            }
            return settings;
        }

        private async Task<List<IntegrationItemDto>> GetIntegrationsFromStoreAsync()
        {
            return await _context.IntegrationConnections
                .AsNoTracking()
                .OrderBy(i => i.Category)
                .ThenBy(i => i.Provider)
                .Select(i => new IntegrationItemDto
                {
                    Id = i.Id,
                    ProviderKey = i.ProviderKey,
                    Provider = i.Provider,
                    Category = i.Category,
                    Status = i.Status,
                    AccountLabel = i.AccountLabel,
                    AccountEmail = i.AccountEmail,
                    SyncEnabled = i.SyncEnabled,
                    LastSyncAt = i.LastSyncAt,
                    LastWebhookAt = i.LastWebhookAt,
                    LastWebhookEventId = i.LastWebhookEventId
                })
                .ToListAsync();
        }

        private static IntegrationItemDto MapConnectionToDto(IntegrationConnection connection, string? notes = null)
        {
            return new IntegrationItemDto
            {
                Id = connection.Id,
                ProviderKey = connection.ProviderKey,
                Provider = connection.Provider,
                Category = connection.Category,
                Status = connection.Status,
                AccountLabel = connection.AccountLabel,
                AccountEmail = connection.AccountEmail,
                SyncEnabled = connection.SyncEnabled,
                LastSyncAt = connection.LastSyncAt,
                LastWebhookAt = connection.LastWebhookAt,
                LastWebhookEventId = connection.LastWebhookEventId,
                Notes = NormalizeOptional(notes)
            };
        }

        private async Task MigrateLegacyIntegrationsIfNeededAsync(FirmSettings settings)
        {
            var hasConnections = await _context.IntegrationConnections.AnyAsync();
            if (hasConnections)
            {
                if (!string.IsNullOrWhiteSpace(settings.IntegrationsJson))
                {
                    settings.IntegrationsJson = null;
                    settings.UpdatedAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync();
                }

                return;
            }

            if (string.IsNullOrWhiteSpace(settings.IntegrationsJson))
            {
                return;
            }

            List<IntegrationItemDto>? legacyItems;
            try
            {
                legacyItems = JsonSerializer.Deserialize<List<IntegrationItemDto>>(
                    settings.IntegrationsJson,
                    LegacyIntegrationJsonOptions);
            }
            catch
            {
                legacyItems = null;
            }

            if (legacyItems == null || legacyItems.Count == 0)
            {
                settings.IntegrationsJson = null;
                settings.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                return;
            }

            var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var now = DateTime.UtcNow;
            foreach (var item in legacyItems.Select(NormalizeIncomingIntegration).Where(i => i != null).Select(i => i!))
            {
                var providerKey = item.ProviderKey ?? ResolveProviderKey(item.Provider, item.Category);
                var key = BuildConnectionKey(providerKey, item.Category);
                if (!seenKeys.Add(key))
                {
                    continue;
                }

                _context.IntegrationConnections.Add(new IntegrationConnection
                {
                    Id = string.IsNullOrWhiteSpace(item.Id) ? Guid.NewGuid().ToString() : item.Id!,
                    ProviderKey = providerKey,
                    Provider = item.Provider,
                    Category = item.Category,
                    Status = NormalizeStatus(item.Status),
                    AccountLabel = NormalizeOptional(item.AccountLabel),
                    AccountEmail = NormalizeOptional(item.AccountEmail),
                    SyncEnabled = item.SyncEnabled,
                    LastSyncAt = item.LastSyncAt,
                    ConnectedAt = now,
                    UpdatedAt = now
                });
            }

            settings.IntegrationsJson = null;
            settings.UpdatedAt = now;
            await _context.SaveChangesAsync();
        }

        private static IntegrationItemDto? NormalizeIncomingIntegration(IntegrationItemDto? input)
        {
            if (input == null ||
                string.IsNullOrWhiteSpace(input.Provider) ||
                string.IsNullOrWhiteSpace(input.Category))
            {
                return null;
            }

            var provider = input.Provider.Trim();
            var category = input.Category.Trim();
            var providerKey = string.IsNullOrWhiteSpace(input.ProviderKey)
                ? ResolveProviderKey(provider, category)
                : input.ProviderKey.Trim().ToLowerInvariant();

            return new IntegrationItemDto
            {
                Id = string.IsNullOrWhiteSpace(input.Id) ? Guid.NewGuid().ToString() : input.Id,
                ProviderKey = providerKey,
                Provider = provider,
                Category = category,
                Status = NormalizeStatus(input.Status),
                AccountLabel = NormalizeOptional(input.AccountLabel),
                AccountEmail = NormalizeOptional(input.AccountEmail),
                SyncEnabled = input.SyncEnabled,
                LastSyncAt = input.LastSyncAt,
                LastWebhookAt = input.LastWebhookAt,
                LastWebhookEventId = NormalizeOptional(input.LastWebhookEventId),
                Notes = NormalizeOptional(input.Notes)
            };
        }

        private static string NormalizeStatus(string? status)
        {
            var normalized = status?.Trim().ToLowerInvariant();
            return normalized switch
            {
                "connected" => "connected",
                "pending" => "pending",
                "disabled" => "disabled",
                "error" => "error",
                _ => "connected"
            };
        }

        private static string ResolveProviderKey(string provider, string category)
        {
            var fromCatalog = IntegrationProviderCatalog.Items.FirstOrDefault(item =>
                string.Equals(item.Provider, provider, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.Category, category, StringComparison.OrdinalIgnoreCase));

            if (fromCatalog != null)
            {
                return fromCatalog.ProviderKey;
            }

            var slug = $"{category}-{provider}".ToLowerInvariant();
            return Regex.Replace(slug, @"[^a-z0-9]+", "-").Trim('-');
        }

        private static string BuildConnectionKey(string providerKey, string category)
        {
            return $"{providerKey.Trim().ToLowerInvariant()}::{category.Trim().ToLowerInvariant()}";
        }

        private static string? NormalizeOptional(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        public class IntegrationItemDto
        {
            public string? Id { get; set; }
            public string? ProviderKey { get; set; }
            public string Provider { get; set; } = string.Empty;
            public string Category { get; set; } = string.Empty;
            public string Status { get; set; } = "connected";
            public string? AccountLabel { get; set; }
            public string? AccountEmail { get; set; }
            public bool SyncEnabled { get; set; } = true;
            public DateTime? LastSyncAt { get; set; }
            public DateTime? LastWebhookAt { get; set; }
            public string? LastWebhookEventId { get; set; }
            public string? Notes { get; set; }
        }

        public class IntegrationsUpdateDto
        {
            public List<IntegrationItemDto> Items { get; set; } = new();
        }

        public class IntegrationCatalogItemDto
        {
            public string ProviderKey { get; set; } = string.Empty;
            public string Provider { get; set; } = string.Empty;
            public string Category { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public string ConnectionMode { get; set; } = "oauth";
            public bool SupportsSync { get; set; } = true;
            public bool SupportsWebhook { get; set; }
            public bool WebhookFirst { get; set; }
            public int FallbackPollingMinutes { get; set; } = 360;
            public List<string> SupportedActions { get; set; } = new();
            public List<string> Capabilities { get; set; } = new();
        }

        public class IntegrationValidationResultDto
        {
            public bool Success { get; set; }
            public string Status { get; set; } = "error";
            public string? Message { get; set; }
            public string? AccountLabel { get; set; }
            public string? AccountEmail { get; set; }
            public string? ExternalAccountId { get; set; }
        }

        public class IntegrationSyncResultDto
        {
            public bool Success { get; set; }
            public int SyncedCount { get; set; }
            public string? Message { get; set; }
            public DateTime? LastSyncAt { get; set; }
            public string? RunId { get; set; }
            public int AttemptCount { get; set; }
            public bool IsDeadLetter { get; set; }
            public bool Deduplicated { get; set; }
        }
    }
}
