using System.Net.Http.Headers;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using JurisFlow.Server.Data;
using JurisFlow.Server.Enums;
using JurisFlow.Server.Models;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace JurisFlow.Server.Services
{
    public sealed class IntegrationConnectorService
    {
        private static readonly Regex CaseNumberTokenRegex = new("[A-Za-z0-9][A-Za-z0-9\\-:/]{2,}", RegexOptions.Compiled);

        private readonly JurisFlowDbContext _context;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<IntegrationConnectorService> _logger;
        private readonly IIntegrationSecretStore _secretStore;
        private readonly IAppFileStorage _fileStorage;
        private readonly TenantContext _tenantContext;
        private readonly DocumentEncryptionService _documentEncryptionService;
        private readonly DocumentIndexService _documentIndexService;
        private readonly EfilingAutomationService _efilingAutomationService;
        private readonly IntegrationPiiMinimizationService _piiMinimizer;
        private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

        public IntegrationConnectorService(
            JurisFlowDbContext context,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ILogger<IntegrationConnectorService> logger,
            IIntegrationSecretStore secretStore,
            IAppFileStorage fileStorage,
            TenantContext tenantContext,
            DocumentEncryptionService documentEncryptionService,
            DocumentIndexService documentIndexService,
            EfilingAutomationService efilingAutomationService,
            IntegrationPiiMinimizationService piiMinimizer)
        {
            _context = context;
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _logger = logger;
            _secretStore = secretStore;
            _fileStorage = fileStorage;
            _tenantContext = tenantContext;
            _documentEncryptionService = documentEncryptionService;
            _documentIndexService = documentIndexService;
            _efilingAutomationService = efilingAutomationService;
            _piiMinimizer = piiMinimizer;
        }

        public async Task<IntegrationConnectResult> ConnectAsync(
            string connectionId,
            string providerKey,
            IntegrationConnectPayload payload,
            string? existingMetadataJson,
            CancellationToken cancellationToken)
        {
            var metadata = DeserializeMetadata(existingMetadataJson);
            await HydrateCredentialsAsync(connectionId, metadata, IntegrationSecretScope.Connect, cancellationToken);
            MergeIncomingCredentials(metadata.Credentials, payload);
            ApplyComplianceFlags(metadata, payload);

            var validation = await ValidateProviderAsync(providerKey, payload, metadata, cancellationToken);
            if (!validation.Success)
            {
                return validation;
            }

            metadata.LastValidation = new IntegrationValidationSnapshot
            {
                ValidAtUtc = DateTime.UtcNow,
                Message = validation.Message
            };

            await PersistCredentialsAsync(connectionId, providerKey, metadata, IntegrationSecretScope.Connect, cancellationToken);

            return new IntegrationConnectResult
            {
                Success = true,
                Status = "connected",
                AccountLabel = validation.AccountLabel,
                AccountEmail = validation.AccountEmail,
                ExternalAccountId = validation.ExternalAccountId,
                Notes = validation.Message,
                MetadataJson = SerializeMetadata(metadata)
            };
        }

        public async Task<IntegrationConnectResult> ValidateAsync(
            string? connectionId,
            string providerKey,
            IntegrationConnectPayload payload,
            string? existingMetadataJson,
            CancellationToken cancellationToken)
        {
            var metadata = DeserializeMetadata(existingMetadataJson);
            await HydrateCredentialsAsync(connectionId, metadata, IntegrationSecretScope.Validate, cancellationToken);
            MergeIncomingCredentials(metadata.Credentials, payload);
            ApplyComplianceFlags(metadata, payload);

            var validation = await ValidateProviderAsync(providerKey, payload, metadata, cancellationToken);
            if (!validation.Success)
            {
                metadata.LastValidation = new IntegrationValidationSnapshot
                {
                    ValidAtUtc = DateTime.UtcNow,
                    Message = validation.ErrorMessage
                };
                if (!string.IsNullOrWhiteSpace(connectionId))
                {
                    await PersistCredentialsAsync(connectionId, providerKey, metadata, IntegrationSecretScope.Validate, cancellationToken);
                }
                validation.MetadataJson = SerializeMetadata(metadata);
                return validation;
            }

            metadata.LastValidation = new IntegrationValidationSnapshot
            {
                ValidAtUtc = DateTime.UtcNow,
                Message = validation.Message
            };
            if (!string.IsNullOrWhiteSpace(connectionId))
            {
                await PersistCredentialsAsync(connectionId, providerKey, metadata, IntegrationSecretScope.Validate, cancellationToken);
            }

            validation.MetadataJson = SerializeMetadata(metadata);
            return validation;
        }

        public async Task<IntegrationSyncResult> SyncAsync(
            string connectionId,
            string providerKey,
            string? metadataJson,
            CancellationToken cancellationToken)
        {
            var metadata = DeserializeMetadata(metadataJson);
            await HydrateCredentialsAsync(connectionId, metadata, IntegrationSecretScope.Sync, cancellationToken);
            var syncResult = await SyncProviderAsync(connectionId, providerKey, metadata, cancellationToken);
            return await CompleteSyncResultAsync(connectionId, providerKey, metadata, syncResult, cancellationToken);
        }

        public Task<IntegrationSyncResult> SyncStripeConnectionAsync(
            string connectionId,
            string? metadataJson,
            CancellationToken cancellationToken)
        {
            return SyncProviderConnectionAsync(
                connectionId,
                metadataJson,
                IntegrationProviderKeys.Stripe,
                (id, envelope, ct) => SyncStripeAsync(id, envelope, ct),
                cancellationToken);
        }

        public Task<IntegrationSyncResult> SyncGoogleCalendarConnectionAsync(
            string connectionId,
            string? metadataJson,
            CancellationToken cancellationToken)
        {
            return SyncProviderConnectionAsync(
                connectionId,
                metadataJson,
                IntegrationProviderKeys.GoogleCalendar,
                (id, envelope, ct) => SyncGoogleCalendarAsync(id, envelope, ct),
                cancellationToken);
        }

        public Task<IntegrationSyncResult> SyncGoogleGmailConnectionAsync(
            string connectionId,
            string? metadataJson,
            CancellationToken cancellationToken)
        {
            return SyncProviderConnectionAsync(
                connectionId,
                metadataJson,
                IntegrationProviderKeys.GoogleGmail,
                (id, envelope, ct) => SyncGoogleGmailAsync(id, envelope, ct),
                cancellationToken);
        }

        public Task<IntegrationSyncResult> SyncQuickBooksConnectionAsync(
            string connectionId,
            string? metadataJson,
            CancellationToken cancellationToken)
            => SyncProviderConnectionAsync(
                connectionId,
                metadataJson,
                IntegrationProviderKeys.QuickBooksOnline,
                (id, envelope, ct) => SyncQuickBooksAsync(id, envelope, ct),
                cancellationToken);

        public Task<IntegrationSyncResult> SyncXeroConnectionAsync(
            string connectionId,
            string? metadataJson,
            CancellationToken cancellationToken)
            => SyncProviderConnectionAsync(
                connectionId,
                metadataJson,
                IntegrationProviderKeys.Xero,
                (id, envelope, ct) => SyncXeroAsync(id, envelope, ct),
                cancellationToken);

        public Task<IntegrationSyncResult> SyncOutlookCalendarConnectionAsync(
            string connectionId,
            string? metadataJson,
            CancellationToken cancellationToken)
            => SyncProviderConnectionAsync(
                connectionId,
                metadataJson,
                IntegrationProviderKeys.MicrosoftOutlookCalendar,
                (id, envelope, ct) => SyncOutlookCalendarAsync(id, envelope, ct),
                cancellationToken);

        public Task<IntegrationSyncResult> SyncOutlookMailConnectionAsync(
            string connectionId,
            string? metadataJson,
            CancellationToken cancellationToken)
            => SyncProviderConnectionAsync(
                connectionId,
                metadataJson,
                IntegrationProviderKeys.MicrosoftOutlookMail,
                (id, envelope, ct) => SyncOutlookMailAsync(id, envelope, ct),
                cancellationToken);

        public Task<IntegrationSyncResult> SyncSharePointOneDriveConnectionAsync(
            string connectionId,
            string? metadataJson,
            CancellationToken cancellationToken)
            => SyncProviderConnectionAsync(
                connectionId,
                metadataJson,
                IntegrationProviderKeys.MicrosoftSharePointOneDrive,
                (id, envelope, ct) => SyncMicrosoftSharePointOneDriveAsync(id, envelope, ct),
                cancellationToken);

        public Task<IntegrationSyncResult> SyncGoogleDriveConnectionAsync(
            string connectionId,
            string? metadataJson,
            CancellationToken cancellationToken)
            => SyncProviderConnectionAsync(
                connectionId,
                metadataJson,
                IntegrationProviderKeys.GoogleDrive,
                (id, envelope, ct) => SyncGoogleDriveAsync(id, envelope, ct),
                cancellationToken);

        public Task<IntegrationSyncResult> SyncNetDocumentsConnectionAsync(
            string connectionId,
            string? metadataJson,
            CancellationToken cancellationToken)
            => SyncProviderConnectionAsync(
                connectionId,
                metadataJson,
                IntegrationProviderKeys.NetDocuments,
                (id, envelope, ct) => SyncGenericDocumentProviderAsync(id, IntegrationProviderKeys.NetDocuments, "Integrations:NetDocuments", envelope, ct),
                cancellationToken);

        public Task<IntegrationSyncResult> SyncIManageConnectionAsync(
            string connectionId,
            string? metadataJson,
            CancellationToken cancellationToken)
            => SyncProviderConnectionAsync(
                connectionId,
                metadataJson,
                IntegrationProviderKeys.IManage,
                (id, envelope, ct) => SyncGenericDocumentProviderAsync(id, IntegrationProviderKeys.IManage, "Integrations:IManage", envelope, ct),
                cancellationToken);

        public Task<IntegrationSyncResult> SyncBusinessCentralConnectionAsync(
            string connectionId,
            string? metadataJson,
            CancellationToken cancellationToken)
            => SyncProviderConnectionAsync(
                connectionId,
                metadataJson,
                IntegrationProviderKeys.MicrosoftBusinessCentral,
                (id, envelope, ct) => SyncGenericErpProviderAsync(id, IntegrationProviderKeys.MicrosoftBusinessCentral, "Integrations:BusinessCentral", envelope, ct),
                cancellationToken);

        public Task<IntegrationSyncResult> SyncNetSuiteConnectionAsync(
            string connectionId,
            string? metadataJson,
            CancellationToken cancellationToken)
            => SyncProviderConnectionAsync(
                connectionId,
                metadataJson,
                IntegrationProviderKeys.NetSuite,
                (id, envelope, ct) => SyncGenericErpProviderAsync(id, IntegrationProviderKeys.NetSuite, "Integrations:NetSuite", envelope, ct),
                cancellationToken);

        public Task<IntegrationSyncResult> SyncCourtListenerDocketsConnectionAsync(
            string connectionId,
            string? metadataJson,
            CancellationToken cancellationToken)
        {
            return SyncProviderConnectionAsync(
                connectionId,
                metadataJson,
                IntegrationProviderKeys.CourtListenerDockets,
                (id, envelope, ct) => SyncCourtListenerDocketsAsync(id, envelope, ct),
                cancellationToken);
        }

        public Task<IntegrationSyncResult> SyncCourtListenerRecapConnectionAsync(
            string connectionId,
            string? metadataJson,
            CancellationToken cancellationToken)
        {
            return SyncProviderConnectionAsync(
                connectionId,
                metadataJson,
                IntegrationProviderKeys.CourtListenerRecap,
                (id, envelope, ct) => SyncCourtListenerRecapAsync(id, envelope, ct),
                cancellationToken);
        }

        public Task<IntegrationSyncResult> SyncOneLegalEfileConnectionAsync(
            string connectionId,
            string? metadataJson,
            CancellationToken cancellationToken)
            => SyncProviderConnectionAsync(
                connectionId,
                metadataJson,
                IntegrationProviderKeys.OneLegalEfile,
                (id, envelope, ct) => SyncGenericEfilingPartnerAsync(
                    id,
                    IntegrationProviderKeys.OneLegalEfile,
                    "Integrations:EfilingPartners:OneLegal",
                    envelope,
                    ct),
                cancellationToken);

        public Task<IntegrationSyncResult> SyncFileAndServeXpressEfileConnectionAsync(
            string connectionId,
            string? metadataJson,
            CancellationToken cancellationToken)
            => SyncProviderConnectionAsync(
                connectionId,
                metadataJson,
                IntegrationProviderKeys.FileAndServeXpressEfile,
                (id, envelope, ct) => SyncGenericEfilingPartnerAsync(
                    id,
                    IntegrationProviderKeys.FileAndServeXpressEfile,
                    "Integrations:EfilingPartners:FileAndServeXpress",
                    envelope,
                    ct),
                cancellationToken);

        public async Task<EfilingPartnerSubmitResult> SubmitEfilingPartnerPacketAsync(
            EfilingPartnerSubmitRequest request,
            CancellationToken cancellationToken)
        {
            if (request == null ||
                string.IsNullOrWhiteSpace(request.ProviderKey) ||
                string.IsNullOrWhiteSpace(request.MatterId))
            {
                return new EfilingPartnerSubmitResult
                {
                    Success = false,
                    ErrorCode = "invalid_request",
                    ErrorMessage = "ProviderKey and MatterId are required."
                };
            }

            var providerKey = request.ProviderKey.Trim().ToLowerInvariant();
            var configPrefix = providerKey switch
            {
                var p when p == IntegrationProviderKeys.OneLegalEfile => "Integrations:EfilingPartners:OneLegal",
                var p when p == IntegrationProviderKeys.FileAndServeXpressEfile => "Integrations:EfilingPartners:FileAndServeXpress",
                _ => null
            };

            if (string.IsNullOrWhiteSpace(configPrefix))
            {
                return new EfilingPartnerSubmitResult
                {
                    Success = false,
                    ErrorCode = "unsupported_provider",
                    ErrorMessage = $"Provider '{request.ProviderKey}' is not supported for e-filing packet submission."
                };
            }

            if (string.IsNullOrWhiteSpace(_tenantContext.TenantId))
            {
                return new EfilingPartnerSubmitResult
                {
                    Success = false,
                    ErrorCode = "missing_tenant",
                    ErrorMessage = "Tenant context is required."
                };
            }

            var tenantId = _tenantContext.TenantId;

            var connectionQuery = _context.IntegrationConnections
                .Where(c => EF.Property<string>(c, "TenantId") == tenantId)
                .Where(c => c.ProviderKey == providerKey &&
                            (c.Status == "connected" || c.Status == "error"));
            if (!string.IsNullOrWhiteSpace(request.ConnectionId))
            {
                var normalizedConnectionId = request.ConnectionId.Trim();
                connectionQuery = connectionQuery.Where(c => c.Id == normalizedConnectionId);
            }
            else
            {
                connectionQuery = connectionQuery.Where(c => c.SyncEnabled);
            }

            var connection = await connectionQuery
                .OrderByDescending(c => c.UpdatedAt)
                .FirstOrDefaultAsync(cancellationToken);
            if (connection == null)
            {
                return new EfilingPartnerSubmitResult
                {
                    Success = false,
                    ErrorCode = "no_connection",
                    ErrorMessage = $"No active integration connection found for provider '{providerKey}'."
                };
            }

            var metadata = DeserializeMetadata(connection.MetadataJson);
            await HydrateCredentialsAsync(connection.Id, metadata, IntegrationSecretScope.Sync, cancellationToken);

            var baseUrl = _configuration[$"{configPrefix}:ApiBaseUrl"]?.TrimEnd('/');
            var submitPath = _configuration[$"{configPrefix}:SubmitPath"] ?? _configuration[$"{configPrefix}:SyncPath"] ?? "/submissions";
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                return new EfilingPartnerSubmitResult
                {
                    Success = false,
                    ErrorCode = "missing_configuration",
                    ErrorMessage = $"{providerKey} submit endpoint is not configured."
                };
            }

            var auth = ResolveGenericProviderAuth(configPrefix, metadata);
            if (string.IsNullOrWhiteSpace(auth.authToken) && (auth.additionalHeaders == null || auth.additionalHeaders.Count == 0))
            {
                return new EfilingPartnerSubmitResult
                {
                    Success = false,
                    ErrorCode = "missing_credentials",
                    ErrorMessage = $"{providerKey} credentials are incomplete."
                };
            }

            var documents = await _context.Documents
                .AsNoTracking()
                .Where(d => EF.Property<string>(d, "TenantId") == tenantId)
                .Where(d => d.MatterId == request.MatterId &&
                            request.DocumentIds.Contains(d.Id))
                .Select(d => new
                {
                    d.Id,
                    d.Name,
                    d.FileName,
                    d.FilePath,
                    d.FileSize,
                    d.MimeType,
                    d.Category,
                    d.Tags,
                    d.UpdatedAt
                })
                .ToListAsync(cancellationToken);

            var payload = new
            {
                matterId = request.MatterId,
                packetName = request.PacketName,
                filingType = request.FilingType,
                existingSubmissionId = request.ExistingSubmissionId,
                metadata = request.Metadata ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                documents,
                submittedAtUtc = DateTime.UtcNow,
                mode = "sandbox_submit"
            };

            using var responseDoc = await PostJsonAsync(
                $"{baseUrl}{NormalizeApiPath(submitPath)}",
                payload,
                auth.authScheme,
                auth.authToken,
                auth.additionalHeaders,
                cancellationToken);

            var rows = ResolveSubmissionRows(responseDoc.RootElement);
            if (rows.Count == 0 && responseDoc.RootElement.ValueKind == JsonValueKind.Object)
            {
                rows = new List<JsonElement> { responseDoc.RootElement.Clone() };
            }

            if (rows.Count == 0)
            {
                return new EfilingPartnerSubmitResult
                {
                    Success = false,
                    ErrorCode = "invalid_partner_response",
                    ErrorMessage = $"{providerKey} submit response did not contain a submission record."
                };
            }

            var syncResult = await UpsertEfilingSubmissionsAsync(
                connection.Id,
                providerKey,
                rows,
                cancellationToken);

            var externalSubmissionIds = rows
                .Select(GetSubmissionId)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Cast<string>()
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            var localSubmissions = externalSubmissionIds.Length == 0
                ? new List<EfilingSubmission>()
                : await _context.EfilingSubmissions
                    .AsNoTracking()
                    .Where(s => s.ProviderKey == providerKey && externalSubmissionIds.Contains(s.ExternalSubmissionId))
                    .OrderByDescending(s => s.UpdatedAt)
                    .Take(10)
                    .ToListAsync(cancellationToken);

            return new EfilingPartnerSubmitResult
            {
                Success = syncResult.Success,
                ProviderKey = providerKey,
                ConnectionId = connection.Id,
                SyncedCount = syncResult.SyncedCount,
                Message = syncResult.Message ?? "Submission processed.",
                ErrorCode = syncResult.ErrorCode,
                ErrorMessage = syncResult.ErrorMessage,
                Submissions = localSubmissions
                    .Select(s => new EfilingPartnerSubmitSubmissionItem
                    {
                        SubmissionId = s.Id,
                        ExternalSubmissionId = s.ExternalSubmissionId,
                        Status = s.Status,
                        ReferenceNumber = s.ReferenceNumber,
                        SubmittedAt = s.SubmittedAt,
                        AcceptedAt = s.AcceptedAt,
                        RejectedAt = s.RejectedAt,
                        RejectionReason = s.RejectionReason
                    })
                    .ToList()
            };
        }

        public async Task<CanonicalIntegrationActionResult> ExecuteCanonicalActionAsync(
            IntegrationConnection connection,
            CanonicalIntegrationActionRequest request,
            CancellationToken cancellationToken)
        {
            var action = (request.Action ?? string.Empty).Trim().ToLowerInvariant();
            if (action == IntegrationCanonicalActions.Validate)
            {
                var validation = await ValidateAsync(
                    connection.Id,
                    connection.ProviderKey,
                    new IntegrationConnectPayload
                    {
                        AccountLabel = connection.AccountLabel,
                        AccountEmail = connection.AccountEmail,
                        SyncEnabled = connection.SyncEnabled
                    },
                    connection.MetadataJson,
                    cancellationToken);

                if (!string.IsNullOrWhiteSpace(validation.MetadataJson))
                {
                    connection.MetadataJson = validation.MetadataJson;
                    connection.UpdatedAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync(cancellationToken);
                }

                return new CanonicalIntegrationActionResult
                {
                    Success = validation.Success,
                    Retryable = !validation.Success && validation.ErrorMessage != null,
                    Action = IntegrationCanonicalActions.Validate,
                    Status = validation.Success ? "validated" : "failed",
                    Message = validation.Success ? (validation.Message ?? "Validation completed.") : null,
                    ErrorCode = validation.Success ? null : "validation_failed",
                    ErrorMessage = validation.Success ? null : validation.ErrorMessage,
                    ResultJson = JsonSerializer.Serialize(new
                    {
                        validation.Success,
                        validation.Status,
                        validation.Message,
                        validation.ErrorMessage,
                        validation.AccountLabel,
                        validation.AccountEmail,
                        validation.ExternalAccountId
                    })
                };
            }

            return connection.ProviderKey.Trim().ToLowerInvariant() switch
            {
                "stripe" => await ExecuteStripeCanonicalActionAsync(connection, request, cancellationToken),
                "google-gmail" => await ExecuteGoogleGmailCanonicalActionAsync(connection, request, cancellationToken),
                "microsoft-outlook-mail" => await ExecuteOutlookMailCanonicalActionAsync(connection, request, cancellationToken),
                "quickbooks-online" => await ExecuteQuickBooksCanonicalActionAsync(connection, request, cancellationToken),
                "xero" => await ExecuteXeroCanonicalActionAsync(connection, request, cancellationToken),
                "google-drive" => await ExecuteDocumentProviderCanonicalActionAsync(connection, request, "Integrations:GoogleDrive", cancellationToken),
                "microsoft-sharepoint-onedrive" => await ExecuteDocumentProviderCanonicalActionAsync(connection, request, "Integrations:SharePointOneDrive", cancellationToken),
                "netdocuments" => await ExecuteDocumentProviderCanonicalActionAsync(connection, request, "Integrations:NetDocuments", cancellationToken),
                "imanage" => await ExecuteDocumentProviderCanonicalActionAsync(connection, request, "Integrations:IManage", cancellationToken),
                "microsoft-business-central" => await ExecuteErpProviderCanonicalActionAsync(connection, request, "Integrations:BusinessCentral", cancellationToken),
                "netsuite" => await ExecuteErpProviderCanonicalActionAsync(connection, request, "Integrations:NetSuite", cancellationToken),
                _ => CanonicalIntegrationActionResult.Unsupported(action, connection.ProviderKey)
            };
        }

        private async Task<IntegrationSyncResult> SyncProviderConnectionAsync(
            string connectionId,
            string? metadataJson,
            string providerKey,
            Func<string, IntegrationMetadataEnvelope, CancellationToken, Task<IntegrationSyncResult>> syncFn,
            CancellationToken cancellationToken)
        {
            var metadata = DeserializeMetadata(metadataJson);
            await HydrateCredentialsAsync(connectionId, metadata, IntegrationSecretScope.Sync, cancellationToken);
            IntegrationSyncResult syncResult;
            try
            {
                syncResult = await syncFn(connectionId, metadata, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Integration sync failed for provider {ProviderKey}", providerKey);
                syncResult = BuildSyncFailureFromException(ex);
            }

            return await CompleteSyncResultAsync(connectionId, providerKey, metadata, syncResult, cancellationToken);
        }

        private async Task<IntegrationSyncResult> CompleteSyncResultAsync(
            string connectionId,
            string providerKey,
            IntegrationMetadataEnvelope metadata,
            IntegrationSyncResult syncResult,
            CancellationToken cancellationToken)
        {
            metadata.LastSync = new IntegrationSyncSnapshot
            {
                SyncedAtUtc = DateTime.UtcNow,
                Success = syncResult.Success,
                Message = syncResult.Message ?? syncResult.ErrorMessage,
                SyncedCount = syncResult.SyncedCount
            };
            await PersistCredentialsAsync(connectionId, providerKey, metadata, IntegrationSecretScope.Sync, cancellationToken);
            syncResult.NextCursor ??= DateTime.UtcNow.ToString("O");
            syncResult.MetadataJson = SerializeMetadata(metadata);
            return syncResult;
        }

        public async Task<MetadataSecretMigrationResult> MigrateMetadataSecretsAsync(
            string connectionId,
            string providerKey,
            string? metadataJson,
            CancellationToken cancellationToken)
        {
            var metadata = DeserializeMetadata(metadataJson);
            if (!ContainsSensitiveCredentialData(metadata.Credentials))
            {
                return new MetadataSecretMigrationResult
                {
                    Migrated = false,
                    MetadataJson = metadataJson
                };
            }

            await PersistCredentialsAsync(
                connectionId,
                providerKey,
                metadata,
                IntegrationSecretScope.SystemMigration,
                cancellationToken);

            return new MetadataSecretMigrationResult
            {
                Migrated = true,
                MetadataJson = SerializeMetadata(metadata)
            };
        }

        private async Task<IntegrationConnectResult> ValidateProviderAsync(
            string providerKey,
            IntegrationConnectPayload payload,
            IntegrationMetadataEnvelope metadata,
            CancellationToken cancellationToken)
        {
            try
            {
                return providerKey.Trim().ToLowerInvariant() switch
                {
                    "stripe" => await ValidateStripeAsync(metadata, cancellationToken),
                    "google-calendar" => await ValidateGoogleCalendarAsync(payload, metadata, cancellationToken),
                    "google-gmail" => await ValidateGoogleGmailAsync(payload, metadata, cancellationToken),
                    "google-drive" => await ValidateGoogleDriveAsync(payload, metadata, cancellationToken),
                    "microsoft-outlook-calendar" => await ValidateOutlookCalendarAsync(payload, metadata, cancellationToken),
                    "microsoft-outlook-mail" => await ValidateOutlookMailAsync(payload, metadata, cancellationToken),
                    "microsoft-sharepoint-onedrive" => await ValidateMicrosoftSharePointOneDriveAsync(payload, metadata, cancellationToken),
                    "quickbooks-online" => await ValidateQuickBooksAsync(payload, metadata, cancellationToken),
                    "xero" => await ValidateXeroAsync(payload, metadata, cancellationToken),
                    "netdocuments" => await ValidateGenericDocumentProviderAsync("NetDocuments", "Integrations:NetDocuments", metadata, cancellationToken),
                    "imanage" => await ValidateGenericDocumentProviderAsync("iManage", "Integrations:IManage", metadata, cancellationToken),
                    "microsoft-business-central" => await ValidateGenericErpProviderAsync("Microsoft Business Central", "Integrations:BusinessCentral", metadata, cancellationToken),
                    "netsuite" => await ValidateGenericErpProviderAsync("NetSuite", "Integrations:NetSuite", metadata, cancellationToken),
                    "courtlistener-dockets" => await ValidateCourtListenerDocketsAsync(metadata, cancellationToken),
                    "courtlistener-recap" => await ValidateCourtListenerRecapAsync(metadata, cancellationToken),
                    "one-legal-efile" => await ValidateGenericEfilingPartnerAsync(
                        "One Legal",
                        "Integrations:EfilingPartners:OneLegal",
                        metadata,
                        cancellationToken),
                    "fileandservexpress-efile" => await ValidateGenericEfilingPartnerAsync(
                        "File & ServeXpress",
                        "Integrations:EfilingPartners:FileAndServeXpress",
                        metadata,
                        cancellationToken),
                    _ => new IntegrationConnectResult
                    {
                        Success = false,
                        Status = "error",
                        ErrorMessage = "Unsupported integration provider."
                    }
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Integration validation failed for provider {ProviderKey}", providerKey);
                return new IntegrationConnectResult
                {
                    Success = false,
                    Status = "error",
                    ErrorMessage = ex.Message
                };
            }
        }

        private async Task<IntegrationSyncResult> SyncProviderAsync(
            string connectionId,
            string providerKey,
            IntegrationMetadataEnvelope metadata,
            CancellationToken cancellationToken)
        {
            try
            {
                return providerKey.Trim().ToLowerInvariant() switch
                {
                    "stripe" => await SyncStripeAsync(connectionId, metadata, cancellationToken),
                    "google-calendar" => await SyncGoogleCalendarAsync(connectionId, metadata, cancellationToken),
                    "google-gmail" => await SyncGoogleGmailAsync(connectionId, metadata, cancellationToken),
                    "google-drive" => await SyncGoogleDriveAsync(connectionId, metadata, cancellationToken),
                    "microsoft-outlook-calendar" => await SyncOutlookCalendarAsync(connectionId, metadata, cancellationToken),
                    "microsoft-outlook-mail" => await SyncOutlookMailAsync(connectionId, metadata, cancellationToken),
                    "microsoft-sharepoint-onedrive" => await SyncMicrosoftSharePointOneDriveAsync(connectionId, metadata, cancellationToken),
                    "quickbooks-online" => await SyncQuickBooksAsync(connectionId, metadata, cancellationToken),
                    "xero" => await SyncXeroAsync(connectionId, metadata, cancellationToken),
                    "netdocuments" => await SyncGenericDocumentProviderAsync(connectionId, IntegrationProviderKeys.NetDocuments, "Integrations:NetDocuments", metadata, cancellationToken),
                    "imanage" => await SyncGenericDocumentProviderAsync(connectionId, IntegrationProviderKeys.IManage, "Integrations:IManage", metadata, cancellationToken),
                    "microsoft-business-central" => await SyncGenericErpProviderAsync(connectionId, IntegrationProviderKeys.MicrosoftBusinessCentral, "Integrations:BusinessCentral", metadata, cancellationToken),
                    "netsuite" => await SyncGenericErpProviderAsync(connectionId, IntegrationProviderKeys.NetSuite, "Integrations:NetSuite", metadata, cancellationToken),
                    "courtlistener-dockets" => await SyncCourtListenerDocketsAsync(connectionId, metadata, cancellationToken),
                    "courtlistener-recap" => await SyncCourtListenerRecapAsync(connectionId, metadata, cancellationToken),
                    "one-legal-efile" => await SyncGenericEfilingPartnerAsync(
                        connectionId,
                        IntegrationProviderKeys.OneLegalEfile,
                        "Integrations:EfilingPartners:OneLegal",
                        metadata,
                        cancellationToken),
                    "fileandservexpress-efile" => await SyncGenericEfilingPartnerAsync(
                        connectionId,
                        IntegrationProviderKeys.FileAndServeXpressEfile,
                        "Integrations:EfilingPartners:FileAndServeXpress",
                        metadata,
                        cancellationToken),
                    _ => new IntegrationSyncResult
                    {
                        Success = false,
                        Retryable = false,
                        ErrorCode = "unsupported_provider",
                        ErrorMessage = "Unsupported integration provider."
                    }
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Integration sync failed for provider {ProviderKey}", providerKey);
                return new IntegrationSyncResult
                {
                    Success = false,
                    Retryable = IsRetryableException(ex),
                    ErrorCode = IsRetryableException(ex) ? "sync_transient_error" : "sync_terminal_error",
                    ErrorMessage = ex.Message
                };
            }
        }

        private static bool IsRetryableException(Exception ex)
        {
            return ex is HttpRequestException or TaskCanceledException or TimeoutException;
        }

        private static IntegrationSyncResult BuildSyncFailureFromException(Exception ex)
        {
            return new IntegrationSyncResult
            {
                Success = false,
                Retryable = IsRetryableException(ex),
                ErrorCode = IsRetryableException(ex) ? "sync_transient_error" : "sync_terminal_error",
                ErrorMessage = ex.Message
            };
        }

        private async Task<IntegrationConnectResult> ValidateStripeAsync(
            IntegrationMetadataEnvelope metadata,
            CancellationToken cancellationToken)
        {
            var apiKey = metadata.Credentials.ApiKey;
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return new IntegrationConnectResult
                {
                    Success = false,
                    Status = "error",
                    ErrorMessage = "Stripe API key is required."
                };
            }

            using var doc = await GetJsonAsync(
                "https://api.stripe.com/v1/account",
                authScheme: "Bearer",
                authToken: apiKey,
                additionalHeaders: null,
                cancellationToken: cancellationToken);
            var root = doc.RootElement;

            var accountId = GetString(root, "id");
            var businessName = GetNestedString(root, "business_profile", "name");
            var email = GetString(root, "email");
            var label = !string.IsNullOrWhiteSpace(businessName) ? businessName : "Stripe Account";

            metadata.Credentials.ExternalAccountId = accountId;
            metadata.Credentials.AccountEmail = email;

            return new IntegrationConnectResult
            {
                Success = true,
                Status = "connected",
                AccountLabel = label,
                AccountEmail = email,
                ExternalAccountId = accountId,
                Message = "Stripe credentials validated."
            };
        }

        private async Task<IntegrationConnectResult> ValidateGoogleCalendarAsync(
            IntegrationConnectPayload payload,
            IntegrationMetadataEnvelope metadata,
            CancellationToken cancellationToken)
        {
            await EnsureGoogleTokenAsync(payload, metadata, cancellationToken);
            if (string.IsNullOrWhiteSpace(metadata.Credentials.AccessToken))
            {
                return new IntegrationConnectResult
                {
                    Success = false,
                    Status = "error",
                    ErrorMessage = "Google access token is missing."
                };
            }

            using var calendarDoc = await GetJsonAsync(
                "https://www.googleapis.com/calendar/v3/users/me/calendarList?maxResults=10",
                authScheme: "Bearer",
                authToken: metadata.Credentials.AccessToken!,
                additionalHeaders: null,
                cancellationToken: cancellationToken);
            var calendarRoot = calendarDoc.RootElement;

            string? selectedCalendarId = null;
            string? selectedCalendarName = null;
            if (calendarRoot.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in items.EnumerateArray())
                {
                    var id = GetString(item, "id");
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        continue;
                    }

                    selectedCalendarId = id;
                    selectedCalendarName = GetString(item, "summary");
                    if (item.TryGetProperty("primary", out var primary) && primary.ValueKind == JsonValueKind.True)
                    {
                        break;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(selectedCalendarId))
            {
                return new IntegrationConnectResult
                {
                    Success = false,
                    Status = "error",
                    ErrorMessage = "No accessible Google calendars were found."
                };
            }

            using var userDoc = await GetJsonAsync(
                "https://www.googleapis.com/oauth2/v2/userinfo",
                authScheme: "Bearer",
                authToken: metadata.Credentials.AccessToken!,
                additionalHeaders: null,
                cancellationToken: cancellationToken);
            var userEmail = GetString(userDoc.RootElement, "email");

            metadata.Credentials.CalendarId = selectedCalendarId;
            metadata.Credentials.ExternalAccountId = selectedCalendarId;
            metadata.Credentials.AccountEmail = userEmail;

            return new IntegrationConnectResult
            {
                Success = true,
                Status = "connected",
                AccountLabel = string.IsNullOrWhiteSpace(selectedCalendarName) ? "Google Calendar" : selectedCalendarName,
                AccountEmail = userEmail,
                ExternalAccountId = selectedCalendarId,
                Message = "Google Calendar credentials validated."
            };
        }

        private async Task<IntegrationConnectResult> ValidateOutlookCalendarAsync(
            IntegrationConnectPayload payload,
            IntegrationMetadataEnvelope metadata,
            CancellationToken cancellationToken)
        {
            await EnsureOutlookTokenAsync(payload, metadata, cancellationToken);
            if (string.IsNullOrWhiteSpace(metadata.Credentials.AccessToken))
            {
                return new IntegrationConnectResult
                {
                    Success = false,
                    Status = "error",
                    ErrorMessage = "Outlook access token is missing."
                };
            }

            using var meDoc = await GetJsonAsync(
                "https://graph.microsoft.com/v1.0/me?$select=id,displayName,mail,userPrincipalName",
                authScheme: "Bearer",
                authToken: metadata.Credentials.AccessToken!,
                additionalHeaders: null,
                cancellationToken: cancellationToken);
            var meRoot = meDoc.RootElement;

            var userId = GetString(meRoot, "id");
            var displayName = GetString(meRoot, "displayName");
            var email = GetString(meRoot, "mail") ?? GetString(meRoot, "userPrincipalName");

            using var calendarsDoc = await GetJsonAsync(
                "https://graph.microsoft.com/v1.0/me/calendars?$top=10",
                authScheme: "Bearer",
                authToken: metadata.Credentials.AccessToken!,
                additionalHeaders: null,
                cancellationToken: cancellationToken);

            string? selectedCalendarId = null;
            string? selectedCalendarName = null;
            if (calendarsDoc.RootElement.TryGetProperty("value", out var calendars) && calendars.ValueKind == JsonValueKind.Array)
            {
                foreach (var calendar in calendars.EnumerateArray())
                {
                    var id = GetString(calendar, "id");
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        continue;
                    }

                    selectedCalendarId = id;
                    selectedCalendarName = GetString(calendar, "name");
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(selectedCalendarId))
            {
                return new IntegrationConnectResult
                {
                    Success = false,
                    Status = "error",
                    ErrorMessage = "No accessible Outlook calendars were found."
                };
            }

            metadata.Credentials.ExternalAccountId = userId ?? selectedCalendarId;
            metadata.Credentials.AccountEmail = email;
            metadata.Credentials.CalendarId = selectedCalendarId;

            return new IntegrationConnectResult
            {
                Success = true,
                Status = "connected",
                AccountLabel = string.IsNullOrWhiteSpace(displayName)
                    ? (selectedCalendarName ?? "Outlook Calendar")
                    : displayName,
                AccountEmail = email,
                ExternalAccountId = userId ?? selectedCalendarId,
                Message = "Outlook Calendar credentials validated."
            };
        }

        private async Task<IntegrationConnectResult> ValidateGoogleGmailAsync(
            IntegrationConnectPayload payload,
            IntegrationMetadataEnvelope metadata,
            CancellationToken cancellationToken)
        {
            await EnsureGoogleTokenAsync(payload, metadata, cancellationToken);
            var accessToken = metadata.Credentials.AccessToken;
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return new IntegrationConnectResult
                {
                    Success = false,
                    Status = "error",
                    ErrorMessage = "Google access token is missing."
                };
            }

            using var profileDoc = await GetJsonAsync(
                "https://gmail.googleapis.com/gmail/v1/users/me/profile",
                authScheme: "Bearer",
                authToken: accessToken,
                additionalHeaders: null,
                cancellationToken: cancellationToken);

            using var userDoc = await GetJsonAsync(
                "https://www.googleapis.com/oauth2/v2/userinfo",
                authScheme: "Bearer",
                authToken: accessToken,
                additionalHeaders: null,
                cancellationToken: cancellationToken);

            var historyId = GetString(profileDoc.RootElement, "historyId");
            var email = GetString(profileDoc.RootElement, "emailAddress")
                        ?? GetString(userDoc.RootElement, "email");

            metadata.Credentials.ExternalAccountId = historyId;
            metadata.Credentials.AccountEmail = email;

            return new IntegrationConnectResult
            {
                Success = true,
                Status = "connected",
                AccountLabel = "Google Gmail",
                AccountEmail = email,
                ExternalAccountId = historyId,
                Message = "Google Gmail credentials validated."
            };
        }

        private async Task<IntegrationConnectResult> ValidateOutlookMailAsync(
            IntegrationConnectPayload payload,
            IntegrationMetadataEnvelope metadata,
            CancellationToken cancellationToken)
        {
            await EnsureOutlookTokenAsync(payload, metadata, cancellationToken);
            var accessToken = metadata.Credentials.AccessToken;
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return new IntegrationConnectResult
                {
                    Success = false,
                    Status = "error",
                    ErrorMessage = "Outlook access token is missing."
                };
            }

            using var meDoc = await GetJsonAsync(
                "https://graph.microsoft.com/v1.0/me?$select=id,displayName,mail,userPrincipalName",
                authScheme: "Bearer",
                authToken: accessToken,
                additionalHeaders: null,
                cancellationToken: cancellationToken);

            var userId = GetString(meDoc.RootElement, "id");
            var displayName = GetString(meDoc.RootElement, "displayName");
            var email = GetString(meDoc.RootElement, "mail") ?? GetString(meDoc.RootElement, "userPrincipalName");

            metadata.Credentials.ExternalAccountId = userId;
            metadata.Credentials.AccountEmail = email;

            return new IntegrationConnectResult
            {
                Success = true,
                Status = "connected",
                AccountLabel = string.IsNullOrWhiteSpace(displayName) ? "Microsoft Outlook Mail" : displayName,
                AccountEmail = email,
                ExternalAccountId = userId,
                Message = "Outlook Mail credentials validated."
            };
        }

        private async Task<IntegrationConnectResult> ValidateGoogleDriveAsync(
            IntegrationConnectPayload payload,
            IntegrationMetadataEnvelope metadata,
            CancellationToken cancellationToken)
        {
            await EnsureGoogleTokenAsync(payload, metadata, cancellationToken);
            var accessToken = metadata.Credentials.AccessToken;
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return new IntegrationConnectResult
                {
                    Success = false,
                    Status = "error",
                    ErrorMessage = "Google access token is missing."
                };
            }

            using var aboutDoc = await GetJsonAsync(
                "https://www.googleapis.com/drive/v3/about?fields=user(displayName,emailAddress,permissionId),storageQuota(limit,usage)",
                authScheme: "Bearer",
                authToken: accessToken,
                additionalHeaders: null,
                cancellationToken: cancellationToken);

            var user = aboutDoc.RootElement.TryGetProperty("user", out var userElement) ? userElement : aboutDoc.RootElement;
            var displayName = GetString(user, "displayName") ?? "Google Drive";
            var email = GetString(user, "emailAddress");
            var permissionId = GetString(user, "permissionId");

            metadata.Credentials.ExternalAccountId = permissionId ?? email;
            metadata.Credentials.AccountEmail = email;

            return new IntegrationConnectResult
            {
                Success = true,
                Status = "connected",
                AccountLabel = displayName,
                AccountEmail = email,
                ExternalAccountId = permissionId ?? email,
                Message = "Google Drive credentials validated."
            };
        }

        private async Task<IntegrationConnectResult> ValidateMicrosoftSharePointOneDriveAsync(
            IntegrationConnectPayload payload,
            IntegrationMetadataEnvelope metadata,
            CancellationToken cancellationToken)
        {
            await EnsureOutlookTokenAsync(payload, metadata, cancellationToken);
            var accessToken = metadata.Credentials.AccessToken;
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return new IntegrationConnectResult
                {
                    Success = false,
                    Status = "error",
                    ErrorMessage = "Microsoft access token is missing."
                };
            }

            using var meDoc = await GetJsonAsync(
                "https://graph.microsoft.com/v1.0/me?$select=id,displayName,mail,userPrincipalName",
                authScheme: "Bearer",
                authToken: accessToken,
                additionalHeaders: null,
                cancellationToken: cancellationToken);
            using var driveDoc = await GetJsonAsync(
                "https://graph.microsoft.com/v1.0/me/drive?$select=id,driveType,webUrl",
                authScheme: "Bearer",
                authToken: accessToken,
                additionalHeaders: null,
                cancellationToken: cancellationToken);

            var me = meDoc.RootElement;
            var drive = driveDoc.RootElement;
            var displayName = GetString(me, "displayName") ?? "Microsoft Drive";
            var email = GetString(me, "mail") ?? GetString(me, "userPrincipalName");
            var driveId = GetString(drive, "id");

            metadata.Credentials.ExternalAccountId = driveId ?? GetString(me, "id");
            metadata.Credentials.AccountEmail = email;

            return new IntegrationConnectResult
            {
                Success = true,
                Status = "connected",
                AccountLabel = displayName,
                AccountEmail = email,
                ExternalAccountId = driveId ?? GetString(me, "id"),
                Message = "Microsoft SharePoint/OneDrive credentials validated."
            };
        }

        private Task<IntegrationConnectResult> ValidateGenericDocumentProviderAsync(
            string providerDisplayName,
            string configPrefix,
            IntegrationMetadataEnvelope metadata,
            CancellationToken cancellationToken)
            => ValidateGenericRestProviderAsync(providerDisplayName, configPrefix, metadata, category: "document", cancellationToken);

        private Task<IntegrationConnectResult> ValidateGenericErpProviderAsync(
            string providerDisplayName,
            string configPrefix,
            IntegrationMetadataEnvelope metadata,
            CancellationToken cancellationToken)
            => ValidateGenericRestProviderAsync(providerDisplayName, configPrefix, metadata, category: "erp", cancellationToken);

        private async Task<IntegrationConnectResult> ValidateQuickBooksAsync(
            IntegrationConnectPayload payload,
            IntegrationMetadataEnvelope metadata,
            CancellationToken cancellationToken)
        {
            await EnsureQuickBooksTokenAsync(payload, metadata, cancellationToken);

            if (string.IsNullOrWhiteSpace(metadata.Credentials.AccessToken))
            {
                return new IntegrationConnectResult
                {
                    Success = false,
                    Status = "error",
                    ErrorMessage = "QuickBooks access token is missing."
                };
            }

            var realmId = !string.IsNullOrWhiteSpace(payload.RealmId)
                ? payload.RealmId.Trim()
                : metadata.Credentials.RealmId;
            if (string.IsNullOrWhiteSpace(realmId))
            {
                return new IntegrationConnectResult
                {
                    Success = false,
                    Status = "error",
                    ErrorMessage = "QuickBooks Realm ID is required."
                };
            }

            var apiBaseUrl = _configuration["Integrations:QuickBooks:ApiBaseUrl"]?.TrimEnd('/')
                             ?? "https://quickbooks.api.intuit.com";
            var companyInfoUrl = $"{apiBaseUrl}/v3/company/{Uri.EscapeDataString(realmId)}/companyinfo/{Uri.EscapeDataString(realmId)}";
            using var doc = await GetJsonAsync(
                companyInfoUrl,
                authScheme: "Bearer",
                authToken: metadata.Credentials.AccessToken!,
                additionalHeaders: new Dictionary<string, string> { ["Accept"] = "application/json" },
                cancellationToken: cancellationToken);

            var company = doc.RootElement.TryGetProperty("CompanyInfo", out var info)
                ? info
                : doc.RootElement;
            var companyName = GetString(company, "CompanyName") ?? GetString(company, "LegalName") ?? "QuickBooks Company";
            var email = GetString(company, "Email") ?? GetNestedString(company, "Email", "Address");

            metadata.Credentials.RealmId = realmId;
            metadata.Credentials.ExternalAccountId = realmId;
            metadata.Credentials.AccountEmail = email;

            return new IntegrationConnectResult
            {
                Success = true,
                Status = "connected",
                AccountLabel = companyName,
                AccountEmail = email,
                ExternalAccountId = realmId,
                Message = "QuickBooks credentials validated."
            };
        }

        private async Task<IntegrationConnectResult> ValidateXeroAsync(
            IntegrationConnectPayload payload,
            IntegrationMetadataEnvelope metadata,
            CancellationToken cancellationToken)
        {
            await EnsureXeroTokenAsync(payload, metadata, cancellationToken);
            if (string.IsNullOrWhiteSpace(metadata.Credentials.AccessToken))
            {
                return new IntegrationConnectResult
                {
                    Success = false,
                    Status = "error",
                    ErrorMessage = "Xero access token is missing."
                };
            }

            using var connectionsDoc = await GetJsonAsync(
                "https://api.xero.com/connections",
                authScheme: "Bearer",
                authToken: metadata.Credentials.AccessToken!,
                additionalHeaders: null,
                cancellationToken: cancellationToken);

            if (connectionsDoc.RootElement.ValueKind != JsonValueKind.Array || connectionsDoc.RootElement.GetArrayLength() == 0)
            {
                return new IntegrationConnectResult
                {
                    Success = false,
                    Status = "error",
                    ErrorMessage = "No Xero tenant connection was found."
                };
            }

            string? selectedTenantId = null;
            string? selectedTenantName = null;
            foreach (var item in connectionsDoc.RootElement.EnumerateArray())
            {
                var tenantId = GetString(item, "tenantId");
                var tenantName = GetString(item, "tenantName");
                if (string.IsNullOrWhiteSpace(tenantId))
                {
                    continue;
                }

                selectedTenantId = tenantId;
                selectedTenantName = tenantName;
                break;
            }

            if (string.IsNullOrWhiteSpace(selectedTenantId))
            {
                return new IntegrationConnectResult
                {
                    Success = false,
                    Status = "error",
                    ErrorMessage = "No valid Xero tenant identifier was returned."
                };
            }

            metadata.Credentials.TenantId = selectedTenantId;
            metadata.Credentials.TenantName = selectedTenantName;
            metadata.Credentials.ExternalAccountId = selectedTenantId;

            return new IntegrationConnectResult
            {
                Success = true,
                Status = "connected",
                AccountLabel = string.IsNullOrWhiteSpace(selectedTenantName) ? "Xero Tenant" : selectedTenantName,
                AccountEmail = metadata.Credentials.AccountEmail,
                ExternalAccountId = selectedTenantId,
                Message = "Xero credentials validated."
            };
        }

        private async Task<IntegrationConnectResult> ValidateCourtListenerDocketsAsync(
            IntegrationMetadataEnvelope metadata,
            CancellationToken cancellationToken)
        {
            var apiToken = metadata.Credentials.ApiKey;
            if (string.IsNullOrWhiteSpace(apiToken))
            {
                return new IntegrationConnectResult
                {
                    Success = false,
                    Status = "error",
                    ErrorMessage = "CourtListener API token is required."
                };
            }

            if (!HasCourtListenerPolicyAcknowledgement(metadata))
            {
                return new IntegrationConnectResult
                {
                    Success = false,
                    Status = "error",
                    ErrorMessage = "Court data usage policy acknowledgement is required for CourtListener connections."
                };
            }

            var baseUrl = _configuration["Integrations:CourtListener:ApiBaseUrl"]?.TrimEnd('/')
                          ?? "https://www.courtlistener.com/api/rest/v4";
            using var doc = await GetJsonAsync(
                $"{baseUrl}/dockets/?page_size=1&ordering=-date_created",
                authScheme: "Token",
                authToken: apiToken,
                additionalHeaders: null,
                cancellationToken: cancellationToken);

            var root = doc.RootElement;
            var results = root.TryGetProperty("results", out var resultArray) && resultArray.ValueKind == JsonValueKind.Array
                ? resultArray
                : default;
            var count = results.ValueKind == JsonValueKind.Array ? results.GetArrayLength() : 0;

            string? docketId = null;
            string? caseName = null;
            if (count > 0)
            {
                var first = results.EnumerateArray().FirstOrDefault();
                if (first.ValueKind == JsonValueKind.Object)
                {
                    docketId = GetString(first, "id")
                        ?? (first.TryGetProperty("id", out var idProperty) ? idProperty.ToString() : null);
                    caseName = GetString(first, "case_name");
                }
            }

            metadata.Credentials.ExternalAccountId = docketId;
            metadata.Credentials.AccountLabel = string.IsNullOrWhiteSpace(caseName) ? "CourtListener Dockets" : caseName;

            return new IntegrationConnectResult
            {
                Success = true,
                Status = "connected",
                AccountLabel = metadata.Credentials.AccountLabel,
                ExternalAccountId = docketId,
                Message = "CourtListener docket credentials validated."
            };
        }

        private async Task<IntegrationConnectResult> ValidateCourtListenerRecapAsync(
            IntegrationMetadataEnvelope metadata,
            CancellationToken cancellationToken)
        {
            var apiToken = metadata.Credentials.ApiKey;
            if (string.IsNullOrWhiteSpace(apiToken))
            {
                return new IntegrationConnectResult
                {
                    Success = false,
                    Status = "error",
                    ErrorMessage = "CourtListener API token is required."
                };
            }

            if (!HasCourtListenerPolicyAcknowledgement(metadata))
            {
                return new IntegrationConnectResult
                {
                    Success = false,
                    Status = "error",
                    ErrorMessage = "Court data usage policy acknowledgement is required for CourtListener connections."
                };
            }

            var baseUrl = _configuration["Integrations:CourtListener:ApiBaseUrl"]?.TrimEnd('/')
                          ?? "https://www.courtlistener.com/api/rest/v4";
            using var doc = await GetJsonAsync(
                $"{baseUrl}/recap-fetch/?page_size=1&ordering=-date_created",
                authScheme: "Token",
                authToken: apiToken,
                additionalHeaders: null,
                cancellationToken: cancellationToken);

            var root = doc.RootElement;
            var results = root.TryGetProperty("results", out var resultArray) && resultArray.ValueKind == JsonValueKind.Array
                ? resultArray
                : default;
            var first = results.ValueKind == JsonValueKind.Array ? results.EnumerateArray().FirstOrDefault() : default;
            var requestId = first.ValueKind == JsonValueKind.Object
                ? GetString(first, "id") ?? (first.TryGetProperty("id", out var idProperty) ? idProperty.ToString() : null)
                : null;

            metadata.Credentials.ExternalAccountId = requestId;
            metadata.Credentials.AccountLabel = "CourtListener RECAP";

            return new IntegrationConnectResult
            {
                Success = true,
                Status = "connected",
                AccountLabel = "CourtListener RECAP",
                ExternalAccountId = requestId,
                Message = "CourtListener RECAP credentials validated."
            };
        }

        private async Task<IntegrationConnectResult> ValidateGenericEfilingPartnerAsync(
            string displayName,
            string configurationPrefix,
            IntegrationMetadataEnvelope metadata,
            CancellationToken cancellationToken)
        {
            var apiToken = metadata.Credentials.ApiKey;
            if (string.IsNullOrWhiteSpace(apiToken))
            {
                return new IntegrationConnectResult
                {
                    Success = false,
                    Status = "error",
                    ErrorMessage = $"{displayName} API token is required."
                };
            }

            var baseUrl = _configuration[$"{configurationPrefix}:ApiBaseUrl"]?.TrimEnd('/');
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                return new IntegrationConnectResult
                {
                    Success = false,
                    Status = "error",
                    ErrorMessage = $"{displayName} API base URL is not configured."
                };
            }

            var validatePath = _configuration[$"{configurationPrefix}:ValidatePath"] ?? "/health";
            using var doc = await GetJsonAsync(
                $"{baseUrl}{NormalizeApiPath(validatePath)}",
                authScheme: "Bearer",
                authToken: apiToken,
                additionalHeaders: null,
                cancellationToken: cancellationToken);

            metadata.Credentials.AccountLabel = displayName;
            metadata.Credentials.ExternalAccountId = GetString(doc.RootElement, "accountId")
                                                    ?? GetString(doc.RootElement, "tenantId")
                                                    ?? metadata.Credentials.ExternalAccountId;

            return new IntegrationConnectResult
            {
                Success = true,
                Status = "connected",
                AccountLabel = displayName,
                AccountEmail = metadata.Credentials.AccountEmail,
                ExternalAccountId = metadata.Credentials.ExternalAccountId,
                Message = $"{displayName} credentials validated."
            };
        }

        private async Task<IntegrationSyncResult> SyncStripeAsync(
            string connectionId,
            IntegrationMetadataEnvelope metadata,
            CancellationToken cancellationToken)
        {
            var apiKey = metadata.Credentials.ApiKey;
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return new IntegrationSyncResult
                {
                    Success = false,
                    Retryable = false,
                    ErrorCode = "missing_credentials",
                    ErrorMessage = "Stripe API key is missing."
                };
            }

            using var doc = await GetJsonAsync(
                "https://api.stripe.com/v1/balance_transactions?limit=25",
                authScheme: "Bearer",
                authToken: apiKey,
                additionalHeaders: null,
                cancellationToken: cancellationToken);

            var count = doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array
                ? data.GetArrayLength()
                : 0;
            var bridgeConflicts = await GenerateStripeAccountingBridgeConflictsAsync(connectionId, cancellationToken);

            return new IntegrationSyncResult
            {
                Success = true,
                SyncedCount = count,
                Message = $"Stripe transactions synced. Records={count}, BridgeConflicts={bridgeConflicts}."
            };
        }

        private async Task<IntegrationSyncResult> SyncGoogleCalendarAsync(
            string connectionId,
            IntegrationMetadataEnvelope metadata,
            CancellationToken cancellationToken)
        {
            await RefreshGoogleTokenIfNeededAsync(metadata, cancellationToken);
            var accessToken = metadata.Credentials.AccessToken;
            var calendarId = metadata.Credentials.CalendarId;
            if (string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(calendarId))
            {
                return new IntegrationSyncResult
                {
                    Success = false,
                    Retryable = false,
                    ErrorCode = "missing_credentials",
                    ErrorMessage = "Google calendar credentials are incomplete."
                };
            }

            var now = DateTime.UtcNow;
            var timeMin = Uri.EscapeDataString(now.ToString("o"));
            var timeMax = Uri.EscapeDataString(now.AddDays(30).ToString("o"));
            var url = $"https://www.googleapis.com/calendar/v3/calendars/{Uri.EscapeDataString(calendarId)}/events?singleEvents=true&orderBy=startTime&timeMin={timeMin}&timeMax={timeMax}&maxResults=100";
            using var doc = await GetJsonAsync(
                url,
                authScheme: "Bearer",
                authToken: accessToken,
                additionalHeaders: null,
                cancellationToken: cancellationToken);

            var count = doc.RootElement.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array
                ? items.GetArrayLength()
                : 0;

            return new IntegrationSyncResult
            {
                Success = true,
                SyncedCount = count,
                Message = "Google Calendar events synced."
            };
        }

        private async Task<IntegrationSyncResult> SyncGoogleGmailAsync(
            string connectionId,
            IntegrationMetadataEnvelope metadata,
            CancellationToken cancellationToken)
        {
            await RefreshGoogleTokenIfNeededAsync(metadata, cancellationToken);
            var accessToken = metadata.Credentials.AccessToken;
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return new IntegrationSyncResult
                {
                    Success = false,
                    Retryable = false,
                    ErrorCode = "missing_credentials",
                    ErrorMessage = "Google Gmail credentials are incomplete."
                };
            }

            using var listDoc = await GetJsonAsync(
                "https://gmail.googleapis.com/gmail/v1/users/me/messages?maxResults=40",
                authScheme: "Bearer",
                authToken: accessToken,
                additionalHeaders: null,
                cancellationToken: cancellationToken);

            if (!listDoc.RootElement.TryGetProperty("messages", out var listElement) || listElement.ValueKind != JsonValueKind.Array)
            {
                return new IntegrationSyncResult
                {
                    Success = true,
                    SyncedCount = 0,
                    Message = "No Gmail messages returned."
                };
            }

            var messages = new List<ProviderEmailEnvelope>();
            foreach (var item in listElement.EnumerateArray())
            {
                var id = GetString(item, "id");
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                using var detailDoc = await GetJsonAsync(
                    $"https://gmail.googleapis.com/gmail/v1/users/me/messages/{Uri.EscapeDataString(id)}?format=full",
                    authScheme: "Bearer",
                    authToken: accessToken,
                    additionalHeaders: null,
                    cancellationToken: cancellationToken);

                var parsed = ParseGmailEnvelope(detailDoc.RootElement);
                if (parsed != null)
                {
                    if (parsed.HasAttachments)
                    {
                        try
                        {
                            parsed.Attachments = await LoadGmailAttachmentsAsync(
                                detailDoc.RootElement,
                                accessToken,
                                cancellationToken);
                            parsed.AttachmentCount = parsed.Attachments.Count;
                            parsed.HasAttachments = parsed.AttachmentCount > 0;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Gmail attachment fetch failed for message {ExternalId}", parsed.ExternalId);
                            parsed.Attachments = new List<ProviderEmailAttachment>();
                        }
                    }
                    messages.Add(parsed);
                }
            }

            var syncSummary = await UpsertMatterLinkedEmailsAsync(
                connectionId,
                "Gmail",
                IntegrationProviderKeys.GoogleGmail,
                messages,
                cancellationToken);
            return new IntegrationSyncResult
            {
                Success = true,
                SyncedCount = syncSummary.created + syncSummary.updated + syncSummary.relinked + syncSummary.attachmentsImported + syncSummary.reviewSignalsQueued,
                Message = $"Gmail sync completed. Created={syncSummary.created}, Updated={syncSummary.updated}, Linked={syncSummary.relinked}, Attachments={syncSummary.attachmentsImported}, Deduped={syncSummary.attachmentsDeduped}, Reviews={syncSummary.reviewSignalsQueued}."
            };
        }

        private async Task<IntegrationSyncResult> SyncOutlookCalendarAsync(
            string connectionId,
            IntegrationMetadataEnvelope metadata,
            CancellationToken cancellationToken)
        {
            await RefreshOutlookTokenIfNeededAsync(metadata, cancellationToken);
            var accessToken = metadata.Credentials.AccessToken;
            var calendarId = metadata.Credentials.CalendarId;
            if (string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(calendarId))
            {
                return new IntegrationSyncResult
                {
                    Success = false,
                    Retryable = false,
                    ErrorCode = "missing_credentials",
                    ErrorMessage = "Outlook calendar credentials are incomplete."
                };
            }

            var now = DateTime.UtcNow;
            var start = Uri.EscapeDataString(now.ToString("o"));
            var end = Uri.EscapeDataString(now.AddDays(30).ToString("o"));
            var url = $"https://graph.microsoft.com/v1.0/me/calendars/{Uri.EscapeDataString(calendarId)}/calendarView?startDateTime={start}&endDateTime={end}&$top=100";
            using var doc = await GetJsonAsync(
                url,
                authScheme: "Bearer",
                authToken: accessToken,
                additionalHeaders: null,
                cancellationToken: cancellationToken);

            var count = doc.RootElement.TryGetProperty("value", out var items) && items.ValueKind == JsonValueKind.Array
                ? items.GetArrayLength()
                : 0;

            return new IntegrationSyncResult
            {
                Success = true,
                SyncedCount = count,
                Message = "Outlook Calendar events synced."
            };
        }

        private async Task<IntegrationSyncResult> SyncOutlookMailAsync(
            string connectionId,
            IntegrationMetadataEnvelope metadata,
            CancellationToken cancellationToken)
        {
            await RefreshOutlookTokenIfNeededAsync(metadata, cancellationToken);
            var accessToken = metadata.Credentials.AccessToken;
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return new IntegrationSyncResult
                {
                    Success = false,
                    Retryable = false,
                    ErrorCode = "missing_credentials",
                    ErrorMessage = "Outlook mail credentials are incomplete."
                };
            }

            using var doc = await GetJsonAsync(
                "https://graph.microsoft.com/v1.0/me/messages?$top=40&$orderby=receivedDateTime desc&$select=id,subject,from,toRecipients,ccRecipients,bccRecipients,bodyPreview,body,receivedDateTime,sentDateTime,isRead,hasAttachments,importance",
                authScheme: "Bearer",
                authToken: accessToken,
                additionalHeaders: null,
                cancellationToken: cancellationToken);

            var messages = new List<ProviderEmailEnvelope>();
            if (doc.RootElement.TryGetProperty("value", out var values) && values.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in values.EnumerateArray())
                {
                    var parsed = ParseOutlookEnvelope(item);
                    if (parsed != null)
                    {
                        if (parsed.HasAttachments)
                        {
                            try
                            {
                                parsed.Attachments = await LoadOutlookAttachmentsAsync(
                                    parsed.ExternalId,
                                    accessToken,
                                    cancellationToken);
                                parsed.AttachmentCount = parsed.Attachments.Count;
                                parsed.HasAttachments = parsed.AttachmentCount > 0;
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Outlook attachment fetch failed for message {ExternalId}", parsed.ExternalId);
                                parsed.Attachments = new List<ProviderEmailAttachment>();
                            }
                        }
                        messages.Add(parsed);
                    }
                }
            }

            var syncSummary = await UpsertMatterLinkedEmailsAsync(
                connectionId,
                "Outlook",
                IntegrationProviderKeys.MicrosoftOutlookMail,
                messages,
                cancellationToken);
            return new IntegrationSyncResult
            {
                Success = true,
                SyncedCount = syncSummary.created + syncSummary.updated + syncSummary.relinked + syncSummary.attachmentsImported + syncSummary.reviewSignalsQueued,
                Message = $"Outlook mail sync completed. Created={syncSummary.created}, Updated={syncSummary.updated}, Linked={syncSummary.relinked}, Attachments={syncSummary.attachmentsImported}, Deduped={syncSummary.attachmentsDeduped}, Reviews={syncSummary.reviewSignalsQueued}."
            };
        }

        private async Task<IntegrationSyncResult> SyncGoogleDriveAsync(
            string connectionId,
            IntegrationMetadataEnvelope metadata,
            CancellationToken cancellationToken)
        {
            await RefreshGoogleTokenIfNeededAsync(metadata, cancellationToken);
            var accessToken = metadata.Credentials.AccessToken;
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return new IntegrationSyncResult
                {
                    Success = false,
                    Retryable = false,
                    ErrorCode = "missing_credentials",
                    ErrorMessage = "Google Drive credentials are incomplete."
                };
            }

            var syncState = await _context.IntegrationConnections
                .AsNoTracking()
                .Where(c => c.Id == connectionId)
                .Select(c => new { c.SyncCursor })
                .FirstOrDefaultAsync(cancellationToken);

            var sinceUtc = ParseProviderDateTime(syncState?.SyncCursor)
                           ?? (metadata.LastSync != null ? metadata.LastSync.SyncedAtUtc.AddMinutes(-5) : null);

            var pageLimit = Math.Clamp(_configuration.GetValue<int?>("Integrations:GoogleDrive:PollingMaxPages") ?? 4, 1, 20);
            var pageToken = (string?)null;
            var pages = 0;
            var moreAvailable = false;
            var newestModifiedAt = sinceUtc;
            var entries = new List<ProviderDocumentCatalogEntry>();

            do
            {
                var qs = new List<string>
                {
                    "pageSize=100",
                    "orderBy=modifiedTime desc",
                    "fields=nextPageToken,files(id,name,mimeType,size,modifiedTime,parents,webViewLink,trashed)"
                };

                if (!string.IsNullOrWhiteSpace(pageToken))
                {
                    qs.Add($"pageToken={Uri.EscapeDataString(pageToken)}");
                }

                if (sinceUtc.HasValue)
                {
                    var q = $"modifiedTime > '{sinceUtc.Value.ToUniversalTime():yyyy-MM-ddTHH:mm:ssZ}'";
                    qs.Add($"q={Uri.EscapeDataString(q)}");
                }

                using var doc = await GetJsonAsync(
                    $"https://www.googleapis.com/drive/v3/files?{string.Join("&", qs)}",
                    authScheme: "Bearer",
                    authToken: accessToken,
                    additionalHeaders: null,
                    cancellationToken: cancellationToken);

                if (doc.RootElement.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in files.EnumerateArray())
                    {
                        var id = GetString(item, "id");
                        var name = GetString(item, "name");
                        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
                        {
                            continue;
                        }

                        var modifiedAt = ParseProviderDateTime(GetString(item, "modifiedTime"));
                        if (modifiedAt.HasValue && (!newestModifiedAt.HasValue || modifiedAt.Value > newestModifiedAt.Value))
                        {
                            newestModifiedAt = modifiedAt.Value;
                        }

                        var mimeType = GetString(item, "mimeType");
                        var isFolder = string.Equals(mimeType, "application/vnd.google-apps.folder", StringComparison.OrdinalIgnoreCase);
                        var isDeleted = item.TryGetProperty("trashed", out var trashed) && trashed.ValueKind == JsonValueKind.True;
                        entries.Add(new ProviderDocumentCatalogEntry
                        {
                            ExternalId = id!,
                            Name = name!,
                            MimeType = mimeType,
                            IsFolder = isFolder,
                            SizeBytes = GetLong(item, "size") ?? 0,
                            ModifiedAt = modifiedAt,
                            WebUrl = GetString(item, "webViewLink"),
                            ParentReference = ExtractFirstString(item, "parents"),
                            IsDeleted = isDeleted
                        });
                    }
                }

                pageToken = GetString(doc.RootElement, "nextPageToken");
                pages++;
                if (!string.IsNullOrWhiteSpace(pageToken) && pages >= pageLimit)
                {
                    moreAvailable = true;
                    break;
                }
            } while (!string.IsNullOrWhiteSpace(pageToken));

            var summary = await UpsertExternalDocumentCatalogAsync(connectionId, IntegrationProviderKeys.GoogleDrive, entries, cancellationToken);
            return new IntegrationSyncResult
            {
                Success = true,
                SyncedCount = summary.upserted,
                NextCursor = (newestModifiedAt ?? DateTime.UtcNow).ToString("O"),
                Message = $"Google Drive sync completed. Upserts={summary.upserted}, Files={summary.files}, Folders={summary.folders}, Deleted={summary.deleted}, Reviews={summary.reviews}, Pages={pages}{(moreAvailable ? ", TruncatedByPageLimit=true" : string.Empty)}."
            };
        }

        private async Task<IntegrationSyncResult> SyncMicrosoftSharePointOneDriveAsync(
            string connectionId,
            IntegrationMetadataEnvelope metadata,
            CancellationToken cancellationToken)
        {
            await RefreshOutlookTokenIfNeededAsync(metadata, cancellationToken);
            var accessToken = metadata.Credentials.AccessToken;
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return new IntegrationSyncResult
                {
                    Success = false,
                    Retryable = false,
                    ErrorCode = "missing_credentials",
                    ErrorMessage = "Microsoft SharePoint/OneDrive credentials are incomplete."
                };
            }

            var syncState = await _context.IntegrationConnections
                .AsNoTracking()
                .Where(c => c.Id == connectionId)
                .Select(c => new { c.DeltaToken, c.SyncCursor })
                .FirstOrDefaultAsync(cancellationToken);

            var pageLimit = Math.Clamp(_configuration.GetValue<int?>("Integrations:SharePointOneDrive:PollingMaxPages") ?? 4, 1, 20);
            var selectClause = Uri.EscapeDataString("id,name,size,webUrl,lastModifiedDateTime,parentReference,file,folder,deleted");
            var requestUrl = !string.IsNullOrWhiteSpace(syncState?.DeltaToken)
                ? syncState!.DeltaToken!
                : $"https://graph.microsoft.com/v1.0/me/drive/root/delta?$top=100&$select={selectClause}";

            var pages = 0;
            var moreAvailable = false;
            var nextDeltaToken = syncState?.DeltaToken;
            var newestModifiedAt = ParseProviderDateTime(syncState?.SyncCursor)
                                   ?? (metadata.LastSync != null ? metadata.LastSync.SyncedAtUtc.AddMinutes(-5) : null);
            var entries = new List<ProviderDocumentCatalogEntry>();

            while (!string.IsNullOrWhiteSpace(requestUrl))
            {
                using var doc = await GetJsonAsync(
                    requestUrl,
                    authScheme: "Bearer",
                    authToken: accessToken,
                    additionalHeaders: null,
                    cancellationToken: cancellationToken);

                if (doc.RootElement.TryGetProperty("value", out var values) && values.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in values.EnumerateArray())
                    {
                        var isDeleted = item.TryGetProperty("deleted", out var deletedElement) && deletedElement.ValueKind == JsonValueKind.Object;
                        var id = GetString(item, "id");
                        var name = GetString(item, "name");
                        if (string.IsNullOrWhiteSpace(id))
                        {
                            continue;
                        }

                        if (string.IsNullOrWhiteSpace(name) && !isDeleted)
                        {
                            continue;
                        }

                        var modifiedAt = ParseProviderDateTime(GetString(item, "lastModifiedDateTime"));
                        if (modifiedAt.HasValue && (!newestModifiedAt.HasValue || modifiedAt.Value > newestModifiedAt.Value))
                        {
                            newestModifiedAt = modifiedAt.Value;
                        }

                        var mimeType = GetNestedString(item, "file", "mimeType");
                        var isFolder = item.TryGetProperty("folder", out var folderElement) && folderElement.ValueKind == JsonValueKind.Object;
                        entries.Add(new ProviderDocumentCatalogEntry
                        {
                            ExternalId = id!,
                            Name = string.IsNullOrWhiteSpace(name) ? "(deleted item)" : name!,
                            MimeType = mimeType,
                            IsFolder = isFolder,
                            SizeBytes = GetLong(item, "size") ?? 0,
                            ModifiedAt = modifiedAt,
                            WebUrl = GetString(item, "webUrl"),
                            ParentReference = GetNestedString(item, "parentReference", "path"),
                            IsDeleted = isDeleted
                        });
                    }
                }

                nextDeltaToken = GetString(doc.RootElement, "@odata.deltaLink") ?? nextDeltaToken;
                var nextLink = GetString(doc.RootElement, "@odata.nextLink");
                pages++;
                if (!string.IsNullOrWhiteSpace(nextLink) && pages >= pageLimit)
                {
                    moreAvailable = true;
                    break;
                }

                requestUrl = nextLink;
                if (string.IsNullOrWhiteSpace(requestUrl))
                {
                    break;
                }
            }

            var summary = await UpsertExternalDocumentCatalogAsync(connectionId, IntegrationProviderKeys.MicrosoftSharePointOneDrive, entries, cancellationToken);
            return new IntegrationSyncResult
            {
                Success = true,
                SyncedCount = summary.upserted,
                NextCursor = (newestModifiedAt ?? DateTime.UtcNow).ToString("O"),
                NextDeltaToken = nextDeltaToken,
                Message = $"SharePoint/OneDrive sync completed. Upserts={summary.upserted}, Files={summary.files}, Folders={summary.folders}, Deleted={summary.deleted}, Reviews={summary.reviews}, Pages={pages}{(moreAvailable ? ", TruncatedByPageLimit=true" : string.Empty)}."
            };
        }

        private async Task<IntegrationSyncResult> SyncGenericDocumentProviderAsync(
            string connectionId,
            string providerKey,
            string configPrefix,
            IntegrationMetadataEnvelope metadata,
            CancellationToken cancellationToken)
        {
            var (baseUrl, listPath) = ResolveGenericProviderEndpoints(configPrefix, defaultListPath: null);
            if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(listPath))
            {
                return new IntegrationSyncResult
                {
                    Success = false,
                    Retryable = false,
                    ErrorCode = "missing_configuration",
                    ErrorMessage = $"{providerKey} list endpoint is not configured."
                };
            }

            var auth = ResolveGenericProviderAuth(configPrefix, metadata);
            using var doc = await GetJsonAsync(
                $"{baseUrl}{NormalizeApiPath(listPath)}",
                auth.authScheme,
                auth.authToken,
                auth.additionalHeaders,
                cancellationToken);

            var entries = ResolveGenericDocumentCatalogEntries(doc.RootElement);
            var summary = await UpsertExternalDocumentCatalogAsync(connectionId, providerKey, entries, cancellationToken);
            return new IntegrationSyncResult
            {
                Success = true,
                SyncedCount = summary.upserted,
                Message = $"{providerKey} document sync completed. Upserts={summary.upserted}, Files={summary.files}, Folders={summary.folders}, Deleted={summary.deleted}, Reviews={summary.reviews}."
            };
        }

        private async Task<IntegrationSyncResult> SyncGenericErpProviderAsync(
            string connectionId,
            string providerKey,
            string configPrefix,
            IntegrationMetadataEnvelope metadata,
            CancellationToken cancellationToken)
        {
            var (baseUrl, listPath) = ResolveGenericProviderEndpoints(configPrefix, defaultListPath: null);
            if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(listPath))
            {
                return new IntegrationSyncResult
                {
                    Success = false,
                    Retryable = false,
                    ErrorCode = "missing_configuration",
                    ErrorMessage = $"{providerKey} ERP list endpoint is not configured."
                };
            }

            await EnsureAccountingMappingCoverageConflictsAsync(connectionId, providerKey, cancellationToken);

            var auth = ResolveGenericProviderAuth(configPrefix, metadata);
            using var doc = await GetJsonAsync(
                $"{baseUrl}{NormalizeApiPath(listPath)}",
                auth.authScheme,
                auth.authToken,
                auth.additionalHeaders,
                cancellationToken);

            var rows = ResolveSubmissionRows(doc.RootElement);
            var reviewCount = await QueueGenericErpReviewSignalsAsync(connectionId, providerKey, rows, cancellationToken);
            return new IntegrationSyncResult
            {
                Success = true,
                SyncedCount = rows.Count,
                Message = $"{providerKey} ERP sync completed. Rows={rows.Count}, Reviews={reviewCount}."
            };
        }

        private async Task<CanonicalIntegrationActionResult> ExecuteGoogleGmailCanonicalActionAsync(
            IntegrationConnection connection,
            CanonicalIntegrationActionRequest request,
            CancellationToken cancellationToken)
        {
            var action = (request.Action ?? string.Empty).Trim().ToLowerInvariant();
            if (action == IntegrationCanonicalActions.Push)
            {
                var result = await PushGmailMessageStateAsync(connection.Id, connection.MetadataJson, cancellationToken);
                connection.MetadataJson = result.metadataJson ?? connection.MetadataJson;
                connection.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync(cancellationToken);
                return new CanonicalIntegrationActionResult
                {
                    Success = result.success,
                    Retryable = result.retryable,
                    Action = action,
                    Status = result.success ? "completed" : "failed",
                    Message = result.message,
                    ErrorCode = result.errorCode,
                    ErrorMessage = result.errorMessage,
                    WriteCount = result.writeCount,
                    ResultJson = JsonSerializer.Serialize(new { action, result.writeCount, result.skippedCount })
                };
            }

            if (action == IntegrationCanonicalActions.Webhook)
            {
                var reviewCount = await CountOpenReviewsForConnectionProviderAsync(connection.Id, connection.ProviderKey, cancellationToken);
                return new CanonicalIntegrationActionResult
                {
                    Success = true,
                    Retryable = false,
                    Action = action,
                    Status = "completed",
                    Message = "Gmail webhook-first ingest is enabled. Review queue refreshed via normal sync/webhook handlers.",
                    ReviewCount = reviewCount
                };
            }

            if (action is IntegrationCanonicalActions.Pull or IntegrationCanonicalActions.Backfill or IntegrationCanonicalActions.Reconcile)
            {
                return await ExecuteSyncBackedCanonicalActionAsync(
                    connection,
                    action,
                    (id, metadataJson, ct) => SyncGoogleGmailConnectionAsync(id, metadataJson, ct),
                    cancellationToken);
            }

            return CanonicalIntegrationActionResult.Unsupported(action, connection.ProviderKey);
        }

        private async Task<CanonicalIntegrationActionResult> ExecuteOutlookMailCanonicalActionAsync(
            IntegrationConnection connection,
            CanonicalIntegrationActionRequest request,
            CancellationToken cancellationToken)
        {
            var action = (request.Action ?? string.Empty).Trim().ToLowerInvariant();
            if (action == IntegrationCanonicalActions.Push)
            {
                var result = await PushOutlookMessageStateAsync(connection.Id, connection.MetadataJson, cancellationToken);
                connection.MetadataJson = result.metadataJson ?? connection.MetadataJson;
                connection.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync(cancellationToken);
                return new CanonicalIntegrationActionResult
                {
                    Success = result.success,
                    Retryable = result.retryable,
                    Action = action,
                    Status = result.success ? "completed" : "failed",
                    Message = result.message,
                    ErrorCode = result.errorCode,
                    ErrorMessage = result.errorMessage,
                    WriteCount = result.writeCount,
                    ResultJson = JsonSerializer.Serialize(new { action, result.writeCount, result.skippedCount, result.folderMoves })
                };
            }

            if (action == IntegrationCanonicalActions.Webhook)
            {
                var reviewCount = await CountOpenReviewsForConnectionProviderAsync(connection.Id, connection.ProviderKey, cancellationToken);
                return new CanonicalIntegrationActionResult
                {
                    Success = true,
                    Retryable = false,
                    Action = action,
                    Status = "completed",
                    Message = "Outlook webhook-first ingest is enabled. Review queue refreshed via normal sync/webhook handlers.",
                    ReviewCount = reviewCount
                };
            }

            if (action is IntegrationCanonicalActions.Pull or IntegrationCanonicalActions.Backfill or IntegrationCanonicalActions.Reconcile)
            {
                return await ExecuteSyncBackedCanonicalActionAsync(
                    connection,
                    action,
                    (id, metadataJson, ct) => SyncOutlookMailConnectionAsync(id, metadataJson, ct),
                    cancellationToken);
            }

            return CanonicalIntegrationActionResult.Unsupported(action, connection.ProviderKey);
        }

        private async Task<CanonicalIntegrationActionResult> ExecuteDocumentProviderCanonicalActionAsync(
            IntegrationConnection connection,
            CanonicalIntegrationActionRequest request,
            string configPrefix,
            CancellationToken cancellationToken)
        {
            var action = (request.Action ?? string.Empty).Trim().ToLowerInvariant();
            if (action == IntegrationCanonicalActions.Push)
            {
                var metadataQueued = await QueueDocumentProviderMetadataPushReviewsAsync(connection.Id, connection.ProviderKey, cancellationToken);
                var workflowQueued = 0;
                if (string.Equals(connection.ProviderKey, IntegrationProviderKeys.NetDocuments, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(connection.ProviderKey, IntegrationProviderKeys.IManage, StringComparison.OrdinalIgnoreCase))
                {
                    workflowQueued = await QueueDmsWorkspaceAndFilingWorkflowPushReviewsAsync(connection.Id, connection.ProviderKey, cancellationToken);
                }

                var queued = metadataQueued + workflowQueued;
                return new CanonicalIntegrationActionResult
                {
                    Success = true,
                    Retryable = false,
                    Action = action,
                    Status = "completed",
                    Message = workflowQueued > 0
                        ? $"Document metadata/workspace sync push plan queued. MetadataReviews={metadataQueued}, DmsWorkflowReviews={workflowQueued}, Total={queued}."
                        : $"Document metadata/workspace sync push plan queued. Reviews={queued}.",
                    ReviewCount = queued,
                    ResultJson = JsonSerializer.Serialize(new { action, queued, metadataQueued, workflowQueued, mode = "review_planned_push" })
                };
            }

            if (action is IntegrationCanonicalActions.Pull or IntegrationCanonicalActions.Backfill or IntegrationCanonicalActions.Reconcile)
            {
                return await ExecuteSyncBackedCanonicalActionAsync(
                    connection,
                    action,
                    (id, metadataJson, ct) => SyncAsync(id, connection.ProviderKey, metadataJson, ct),
                    cancellationToken);
            }

            if (action == IntegrationCanonicalActions.Webhook)
            {
                var reviewCount = await CountOpenReviewsForConnectionProviderAsync(connection.Id, connection.ProviderKey, cancellationToken);
                return new CanonicalIntegrationActionResult
                {
                    Success = true,
                    Retryable = false,
                    Action = action,
                    Status = "completed",
                    Message = "Document provider webhook mode acknowledged. Polling fallback remains available.",
                    ReviewCount = reviewCount
                };
            }

            return CanonicalIntegrationActionResult.Unsupported(action, connection.ProviderKey);
        }

        private async Task<CanonicalIntegrationActionResult> ExecuteErpProviderCanonicalActionAsync(
            IntegrationConnection connection,
            CanonicalIntegrationActionRequest request,
            string configPrefix,
            CancellationToken cancellationToken)
        {
            var action = (request.Action ?? string.Empty).Trim().ToLowerInvariant();
            if (action == IntegrationCanonicalActions.Push)
            {
                await EnsureAccountingMappingCoverageConflictsAsync(connection.Id, connection.ProviderKey, cancellationToken);
                var reviewCount = await QueueErpPushReviewPlanAsync(connection.Id, connection.ProviderKey, cancellationToken);
                return new CanonicalIntegrationActionResult
                {
                    Success = true,
                    Retryable = false,
                    Action = action,
                    Status = "completed",
                    Message = $"{connection.ProviderKey} ERP push plan generated for review. Reviews={reviewCount}.",
                    ReviewCount = reviewCount,
                    ConflictCount = await CountOpenConflictsForConnectionAsync(connection.Id, cancellationToken),
                    ResultJson = JsonSerializer.Serialize(new { action, reviewCount, mode = "review_planned_push" })
                };
            }

            if (action is IntegrationCanonicalActions.Pull or IntegrationCanonicalActions.Backfill or IntegrationCanonicalActions.Reconcile)
            {
                return await ExecuteSyncBackedCanonicalActionAsync(
                    connection,
                    action,
                    (id, metadataJson, ct) => SyncAsync(id, connection.ProviderKey, metadataJson, ct),
                    cancellationToken);
            }

            if (action == IntegrationCanonicalActions.Webhook)
            {
                return new CanonicalIntegrationActionResult
                {
                    Success = true,
                    Retryable = false,
                    Action = action,
                    Status = "completed",
                    Message = $"{connection.ProviderKey} webhook-first mode acknowledged."
                };
            }

            return CanonicalIntegrationActionResult.Unsupported(action, connection.ProviderKey);
        }

        private async Task<CanonicalIntegrationActionResult> ExecuteSyncBackedCanonicalActionAsync(
            IntegrationConnection connection,
            string action,
            Func<string, string?, CancellationToken, Task<IntegrationSyncResult>> syncFn,
            CancellationToken cancellationToken)
        {
            var syncResult = await syncFn(connection.Id, connection.MetadataJson, cancellationToken);
            connection.MetadataJson = syncResult.MetadataJson ?? connection.MetadataJson;
            connection.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);

            var conflictCount = syncResult.Success ? await CountOpenConflictsForConnectionAsync(connection.Id, cancellationToken) : 0;
            var reviewCount = syncResult.Success ? await CountOpenReviewsForConnectionProviderAsync(connection.Id, connection.ProviderKey, cancellationToken) : 0;

            return new CanonicalIntegrationActionResult
            {
                Success = syncResult.Success,
                Retryable = syncResult.Retryable,
                Action = action,
                Status = syncResult.Success ? "completed" : "failed",
                Message = syncResult.Message,
                ErrorCode = syncResult.ErrorCode,
                ErrorMessage = syncResult.ErrorMessage,
                ReadCount = action == IntegrationCanonicalActions.Push ? 0 : Math.Max(0, syncResult.SyncedCount),
                WriteCount = action == IntegrationCanonicalActions.Push ? Math.Max(0, syncResult.SyncedCount) : 0,
                ConflictCount = conflictCount,
                ReviewCount = reviewCount,
                ResultJson = JsonSerializer.Serialize(new
                {
                    action,
                    syncResult.Success,
                    syncResult.SyncedCount,
                    syncResult.Message,
                    conflicts = conflictCount,
                    reviews = reviewCount
                })
            };
        }

        private async Task<(bool success, bool retryable, string? message, string? errorCode, string? errorMessage, int writeCount, int skippedCount, string? metadataJson)> PushGmailMessageStateAsync(
            string connectionId,
            string? metadataJson,
            CancellationToken cancellationToken)
        {
            var metadata = DeserializeMetadata(metadataJson);
            await HydrateCredentialsAsync(connectionId, metadata, IntegrationSecretScope.Sync, cancellationToken);
            await RefreshGoogleTokenIfNeededAsync(metadata, cancellationToken);
            var accessToken = metadata.Credentials.AccessToken;
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return (false, false, null, "missing_credentials", "Google Gmail credentials are incomplete.", 0, 0, metadataJson);
            }

            var messages = await _context.EmailMessages
                .Where(m => m.EmailAccountId == connectionId && m.Provider == "Gmail" && m.ExternalId != null)
                .OrderByDescending(m => m.SyncedAt)
                .Take(25)
                .ToListAsync(cancellationToken);

            var pushed = 0;
            var skipped = 0;
            foreach (var message in messages)
            {
                var addLabelIds = new HashSet<string>(StringComparer.Ordinal);
                var removeLabelIds = new HashSet<string>(StringComparer.Ordinal);

                if (message.IsRead)
                {
                    removeLabelIds.Add("UNREAD");
                }
                else
                {
                    addLabelIds.Add("UNREAD");
                }

                var folder = (message.Folder ?? string.Empty).Trim().ToLowerInvariant();
                if (folder == "inbox")
                {
                    addLabelIds.Add("INBOX");
                }
                else if (folder is "archive" or "archived")
                {
                    removeLabelIds.Add("INBOX");
                }
                else if (folder is "needs review" or "needs_review")
                {
                    addLabelIds.Add("INBOX");
                    addLabelIds.Add("STARRED");
                    addLabelIds.Add("IMPORTANT");
                }
                else if (folder == "filed")
                {
                    removeLabelIds.Add("INBOX");
                    removeLabelIds.Add("UNREAD");
                }

                if (addLabelIds.Count == 0 && removeLabelIds.Count == 0)
                {
                    skipped++;
                    continue;
                }

                await PostJsonAsync(
                    $"https://gmail.googleapis.com/gmail/v1/users/me/messages/{Uri.EscapeDataString(message.ExternalId!)}/modify",
                    new { addLabelIds = addLabelIds.ToArray(), removeLabelIds = removeLabelIds.ToArray() },
                    authScheme: "Bearer",
                    authToken: accessToken,
                    additionalHeaders: null,
                    cancellationToken: cancellationToken);
                pushed++;
            }

            var completed = await CompleteSyncResultAsync(
                connectionId,
                IntegrationProviderKeys.GoogleGmail,
                metadata,
                new IntegrationSyncResult
                {
                    Success = true,
                    SyncedCount = pushed,
                    Message = $"Gmail state push completed. Updated={pushed}, Skipped={skipped}."
                },
                cancellationToken);

            return (true, false, completed.Message, null, null, pushed, skipped, completed.MetadataJson);
        }

        private async Task<(bool success, bool retryable, string? message, string? errorCode, string? errorMessage, int writeCount, int skippedCount, int folderMoves, string? metadataJson)> PushOutlookMessageStateAsync(
            string connectionId,
            string? metadataJson,
            CancellationToken cancellationToken)
        {
            var metadata = DeserializeMetadata(metadataJson);
            await HydrateCredentialsAsync(connectionId, metadata, IntegrationSecretScope.Sync, cancellationToken);
            await RefreshOutlookTokenIfNeededAsync(metadata, cancellationToken);
            var accessToken = metadata.Credentials.AccessToken;
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return (false, false, null, "missing_credentials", "Outlook Mail credentials are incomplete.", 0, 0, 0, metadataJson);
            }

            var messages = await _context.EmailMessages
                .Where(m => m.EmailAccountId == connectionId && m.Provider == "Outlook" && m.ExternalId != null)
                .OrderByDescending(m => m.SyncedAt)
                .Take(25)
                .ToListAsync(cancellationToken);

            var pushed = 0;
            var skipped = 0;
            var folderMoves = 0;
            Dictionary<string, string>? folderMap = null;

            foreach (var message in messages)
            {
                var changed = false;

                await SendJsonAsync(
                    HttpMethod.Patch,
                    $"https://graph.microsoft.com/v1.0/me/messages/{Uri.EscapeDataString(message.ExternalId!)}",
                    new { isRead = message.IsRead },
                    authScheme: "Bearer",
                    authToken: accessToken,
                    additionalHeaders: null,
                    cancellationToken: cancellationToken,
                    allowEmptyBody: true);
                changed = true;

                var targetFolderName = (message.Folder ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(targetFolderName) &&
                    !targetFolderName.Equals("Inbox", StringComparison.OrdinalIgnoreCase) &&
                    !targetFolderName.Equals("Sent", StringComparison.OrdinalIgnoreCase))
                {
                    folderMap ??= await GetOutlookFolderMapAsync(accessToken, cancellationToken);
                    if (folderMap.TryGetValue(targetFolderName, out var destinationId) && !string.IsNullOrWhiteSpace(destinationId))
                    {
                        await PostJsonAsync(
                            $"https://graph.microsoft.com/v1.0/me/messages/{Uri.EscapeDataString(message.ExternalId!)}/move",
                            new { destinationId },
                            authScheme: "Bearer",
                            authToken: accessToken,
                            additionalHeaders: null,
                            cancellationToken: cancellationToken);
                        folderMoves++;
                    }
                }

                if (changed)
                {
                    pushed++;
                }
                else
                {
                    skipped++;
                }
            }

            var completed = await CompleteSyncResultAsync(
                connectionId,
                IntegrationProviderKeys.MicrosoftOutlookMail,
                metadata,
                new IntegrationSyncResult
                {
                    Success = true,
                    SyncedCount = pushed,
                    Message = $"Outlook state push completed. Updated={pushed}, FolderMoves={folderMoves}, Skipped={skipped}."
                },
                cancellationToken);

            return (true, false, completed.Message, null, null, pushed, skipped, folderMoves, completed.MetadataJson);
        }

        private async Task<IntegrationSyncResult> SyncQuickBooksAsync(
            string connectionId,
            IntegrationMetadataEnvelope metadata,
            CancellationToken cancellationToken)
        {
            await RefreshQuickBooksTokenIfNeededAsync(metadata, cancellationToken);
            var accessToken = metadata.Credentials.AccessToken;
            var realmId = metadata.Credentials.RealmId;
            if (string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(realmId))
            {
                return new IntegrationSyncResult
                {
                    Success = false,
                    Retryable = false,
                    ErrorCode = "missing_credentials",
                    ErrorMessage = "QuickBooks credentials are incomplete."
                };
            }

            await EnsureAccountingMappingCoverageConflictsAsync(connectionId, IntegrationProviderKeys.QuickBooksOnline, cancellationToken);

            var apiBaseUrl = _configuration["Integrations:QuickBooks:ApiBaseUrl"]?.TrimEnd('/')
                             ?? "https://quickbooks.api.intuit.com";

            var outboundInvoices = await SyncQuickBooksOutboundInvoicesAsync(
                connectionId,
                apiBaseUrl,
                realmId,
                accessToken,
                cancellationToken);
            var outboundPayments = await SyncQuickBooksOutboundPaymentsAsync(
                connectionId,
                apiBaseUrl,
                realmId,
                accessToken,
                cancellationToken);
            var inboundInvoices = await SyncQuickBooksInboundInvoicesAsync(
                connectionId,
                apiBaseUrl,
                realmId,
                accessToken,
                cancellationToken);
            var inboundPayments = await SyncQuickBooksInboundPaymentsAsync(
                connectionId,
                apiBaseUrl,
                realmId,
                accessToken,
                cancellationToken);
            var conflictCount = await GenerateQuickBooksAccountingConflictsAsync(connectionId, cancellationToken);

            var count = outboundInvoices + outboundPayments + inboundInvoices + inboundPayments;
            return new IntegrationSyncResult
            {
                Success = true,
                SyncedCount = count,
                Message = $"QuickBooks bi-directional sync completed. InvoicePush={outboundInvoices}, PaymentPush={outboundPayments}, InvoicePull={inboundInvoices}, PaymentPull={inboundPayments}, Conflicts={conflictCount}."
            };
        }

        private async Task<IntegrationSyncResult> SyncXeroAsync(
            string connectionId,
            IntegrationMetadataEnvelope metadata,
            CancellationToken cancellationToken)
        {
            await RefreshXeroTokenIfNeededAsync(metadata, cancellationToken);
            var accessToken = metadata.Credentials.AccessToken;
            var tenantId = metadata.Credentials.TenantId;
            if (string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(tenantId))
            {
                return new IntegrationSyncResult
                {
                    Success = false,
                    Retryable = false,
                    ErrorCode = "missing_credentials",
                    ErrorMessage = "Xero credentials are incomplete."
                };
            }

            await EnsureAccountingMappingCoverageConflictsAsync(connectionId, IntegrationProviderKeys.Xero, cancellationToken);
            var invoicePageLimit = Math.Clamp(_configuration.GetValue<int?>("Integrations:Xero:InvoicesPollingMaxPages") ?? 4, 1, 20);
            var paymentPageLimit = Math.Clamp(_configuration.GetValue<int?>("Integrations:Xero:PaymentsPollingMaxPages") ?? 4, 1, 20);
            var contactPageLimit = Math.Clamp(_configuration.GetValue<int?>("Integrations:Xero:ContactsPollingMaxPages") ?? 2, 1, 10);

            var invoicesSnapshot = await GetXeroPagedCollectionAsync("Invoices", "Invoices", accessToken, tenantId, invoicePageLimit, cancellationToken);
            var paymentsSnapshot = await GetXeroPagedCollectionAsync("Payments", "Payments", accessToken, tenantId, paymentPageLimit, cancellationToken);
            var contactsSnapshot = await GetXeroPagedCollectionAsync("Contacts", "Contacts", accessToken, tenantId, contactPageLimit, cancellationToken);

            await HydrateXeroClientContactLinksAsync(connectionId, contactsSnapshot.rows, cancellationToken);
            var invoiceConflicts = await GenerateXeroAccountingCoverageConflictsAsync(connectionId, invoicesSnapshot.rows, cancellationToken);
            var paymentConflicts = await GenerateXeroPaymentCoverageConflictsAsync(connectionId, paymentsSnapshot.rows, cancellationToken);

            var count = invoicesSnapshot.rows.Count + paymentsSnapshot.rows.Count + contactsSnapshot.rows.Count;
            var pages = invoicesSnapshot.pages + paymentsSnapshot.pages + contactsSnapshot.pages;
            var truncated = invoicesSnapshot.truncated || paymentsSnapshot.truncated || contactsSnapshot.truncated;

            return new IntegrationSyncResult
            {
                Success = true,
                SyncedCount = count,
                Message = $"Xero sync completed. Invoices={invoicesSnapshot.rows.Count}, Payments={paymentsSnapshot.rows.Count}, Contacts={contactsSnapshot.rows.Count}, Conflicts={invoiceConflicts + paymentConflicts}, Pages={pages}{(truncated ? ", TruncatedByPageLimit=true" : string.Empty)}."
            };
        }

        private async Task<int> SyncXeroOutboundInvoicesAsync(
            string connectionId,
            string accessToken,
            string tenantId,
            CancellationToken cancellationToken)
        {
            var since = DateTime.UtcNow.AddDays(-30);
            var invoices = await _context.Invoices
                .Include(i => i.LineItems)
                .Where(i => i.UpdatedAt >= since && i.Total > 0)
                .OrderBy(i => i.UpdatedAt)
                .Take(100)
                .ToListAsync(cancellationToken);
            if (invoices.Count == 0)
            {
                return 0;
            }

            var localInvoiceIds = invoices.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
            var localClientIds = invoices.Select(i => i.ClientId).Where(id => !string.IsNullOrWhiteSpace(id)).ToHashSet(StringComparer.Ordinal);
            var invoiceLinks = await _context.IntegrationEntityLinks
                .Where(l => l.ConnectionId == connectionId &&
                            l.ProviderKey == IntegrationProviderKeys.Xero &&
                            l.LocalEntityType == "invoice" &&
                            localInvoiceIds.Contains(l.LocalEntityId))
                .ToDictionaryAsync(l => l.LocalEntityId, StringComparer.Ordinal, cancellationToken);
            var clientLinks = await _context.IntegrationEntityLinks
                .Where(l => l.ConnectionId == connectionId &&
                            l.ProviderKey == IntegrationProviderKeys.Xero &&
                            l.LocalEntityType == "client" &&
                            localClientIds.Contains(l.LocalEntityId))
                .ToDictionaryAsync(l => l.LocalEntityId, StringComparer.Ordinal, cancellationToken);
            var clients = await _context.Clients
                .Where(c => localClientIds.Contains(c.Id))
                .Select(c => new { c.Id, c.Name, c.Email })
                .ToDictionaryAsync(c => c.Id, StringComparer.Ordinal, cancellationToken);

            var existingOpenConflicts = await _context.IntegrationConflictQueueItems
                .Where(c => c.ConnectionId == connectionId &&
                            c.ProviderKey == IntegrationProviderKeys.Xero &&
                            (c.Status == IntegrationConflictStatuses.Open || c.Status == IntegrationConflictStatuses.InReview))
                .ToDictionaryAsync(c => c.Fingerprint ?? c.Id, StringComparer.OrdinalIgnoreCase, cancellationToken);

            var incomeAccountCode = await GetDefaultMappingValueAsync(connectionId, IntegrationProviderKeys.Xero, "invoice", "income", cancellationToken);
            var taxType = await GetDefaultMappingValueAsync(connectionId, IntegrationProviderKeys.Xero, "invoice", "defaultTaxType", cancellationToken)
                          ?? await GetDefaultTaxMappingValueAsync(connectionId, IntegrationProviderKeys.Xero, "invoice", cancellationToken);
            if (string.IsNullOrWhiteSpace(incomeAccountCode))
            {
                return 0;
            }

            var writes = 0;
            foreach (var invoice in invoices)
            {
                var splitPolicy = await ResolveAccountingSplitReceivablePolicyAsync(
                    connectionId,
                    IntegrationProviderKeys.Xero,
                    invoice,
                    existingOpenConflicts,
                    cancellationToken);
                if (splitPolicy.BlockOutbound)
                {
                    continue;
                }

                var billingClientId = splitPolicy.BillToClientId ?? invoice.ClientId;
                if (string.IsNullOrWhiteSpace(billingClientId))
                {
                    continue;
                }

                if (!clients.TryGetValue(billingClientId, out var client))
                {
                    var payorClient = await _context.Clients
                        .Where(c => c.Id == billingClientId)
                        .Select(c => new { c.Id, c.Name, c.Email })
                        .FirstOrDefaultAsync(cancellationToken);
                    if (payorClient == null)
                    {
                        continue;
                    }

                    clients[billingClientId] = payorClient;
                    client = payorClient;
                }

                var contactId = await EnsureXeroContactRefAsync(connectionId, accessToken, tenantId, client.Id, client.Name, client.Email, clientLinks, cancellationToken);
                if (string.IsNullOrWhiteSpace(contactId))
                {
                    continue;
                }

                if (invoice.Status == InvoiceStatus.Cancelled)
                {
                    if (!invoiceLinks.TryGetValue(invoice.Id, out var cancelledLink) || string.IsNullOrWhiteSpace(cancelledLink.ExternalEntityId))
                    {
                        continue;
                    }

                    using var voidDoc = await PostJsonAsync(
                        "https://api.xero.com/api.xro/2.0/Invoices",
                        new
                        {
                            Invoices = new object[]
                            {
                                new
                                {
                                    InvoiceID = cancelledLink.ExternalEntityId,
                                    Status = "VOIDED"
                                }
                            }
                        },
                        authScheme: "Bearer",
                        authToken: accessToken,
                        additionalHeaders: new Dictionary<string, string> { ["xero-tenant-id"] = tenantId },
                        cancellationToken: cancellationToken);

                    if (TryGetFirstXeroRow(voidDoc.RootElement, "Invoices", out var voidInvoice))
                    {
                        UpsertIntegrationEntityLink(
                            invoiceLinks,
                            connectionId,
                            IntegrationProviderKeys.Xero,
                            "invoice",
                            invoice.Id,
                            "invoice",
                            GetString(voidInvoice, "InvoiceID") ?? cancelledLink.ExternalEntityId!,
                            GetString(voidInvoice, "UpdatedDateUTC"),
                            "outbound",
                            voidInvoice.GetRawText());
                        writes++;
                    }

                    continue;
                }

                var lineItems = (invoice.LineItems?.Count ?? 0) == 0
                    ? new object[]
                    {
                        new
                        {
                            Description = string.IsNullOrWhiteSpace(invoice.Notes) ? $"Invoice {invoice.Number ?? invoice.Id}" : invoice.Notes,
                            Quantity = 1m,
                            UnitAmount = NormalizeConnectorMoney(invoice.Total),
                            AccountCode = incomeAccountCode,
                            TaxType = string.IsNullOrWhiteSpace(taxType) ? null : taxType
                        }
                    }
                    : (invoice.LineItems ?? Array.Empty<InvoiceLineItem>()).Select(li => new
                    {
                        Description = string.IsNullOrWhiteSpace(li.Description) ? li.Type : li.Description,
                        Quantity = li.Quantity <= 0 ? 1m : NormalizeConnectorMoney(li.Quantity),
                        UnitAmount = NormalizeConnectorMoney(li.Rate <= 0 ? (li.Amount <= 0 ? invoice.Total : li.Amount) : li.Rate),
                        AccountCode = incomeAccountCode,
                        TaxType = string.IsNullOrWhiteSpace(taxType) ? null : taxType
                    }).Cast<object>().ToArray();

                var existingLink = invoiceLinks.TryGetValue(invoice.Id, out var linkedInvoice) ? linkedInvoice : null;
                using var invoiceDoc = await PostJsonAsync(
                    "https://api.xero.com/api.xro/2.0/Invoices",
                    new
                    {
                        Invoices = new object[]
                        {
                            new
                            {
                                InvoiceID = existingLink?.ExternalEntityId,
                                Type = "ACCREC",
                                Contact = new { ContactID = contactId },
                                Date = invoice.IssueDate.ToString("yyyy-MM-dd"),
                                DueDate = invoice.DueDate?.ToString("yyyy-MM-dd"),
                                InvoiceNumber = string.IsNullOrWhiteSpace(invoice.Number) ? invoice.Id[..Math.Min(12, invoice.Id.Length)] : invoice.Number,
                                Reference = invoice.Id,
                                Status = invoice.Status == InvoiceStatus.Draft ? "DRAFT" : "AUTHORISED",
                                LineAmountTypes = invoice.Tax > 0 ? "Exclusive" : "NoTax",
                                LineItems = lineItems
                            }
                        }
                    },
                    authScheme: "Bearer",
                    authToken: accessToken,
                    additionalHeaders: new Dictionary<string, string> { ["xero-tenant-id"] = tenantId },
                    cancellationToken: cancellationToken);

                if (!TryGetFirstXeroRow(invoiceDoc.RootElement, "Invoices", out var remoteInvoice))
                {
                    continue;
                }

                var remoteInvoiceId = GetString(remoteInvoice, "InvoiceID");
                if (string.IsNullOrWhiteSpace(remoteInvoiceId))
                {
                    continue;
                }

                UpsertIntegrationEntityLink(
                    invoiceLinks,
                    connectionId,
                    IntegrationProviderKeys.Xero,
                    "invoice",
                    invoice.Id,
                    "invoice",
                    remoteInvoiceId!,
                    GetString(remoteInvoice, "UpdatedDateUTC"),
                    "outbound",
                    remoteInvoice.GetRawText());
                writes++;
            }

            if (_context.ChangeTracker.HasChanges())
            {
                await _context.SaveChangesAsync(cancellationToken);
            }

            return writes;
        }

        private async Task<int> SyncXeroOutboundPaymentsAsync(
            string connectionId,
            string accessToken,
            string tenantId,
            CancellationToken cancellationToken)
        {
            var since = DateTime.UtcNow.AddDays(-30);
            var payments = await _context.PaymentTransactions
                .Where(p =>
                    (p.Status == "Succeeded" || p.Status == "Partially Refunded") &&
                    p.InvoiceId != null &&
                    p.UpdatedAt >= since)
                .OrderBy(p => p.UpdatedAt)
                .Take(100)
                .ToListAsync(cancellationToken);
            if (payments.Count == 0)
            {
                return 0;
            }

            var paymentIds = payments.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
            var invoiceIds = payments.Select(p => p.InvoiceId!).Distinct(StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
            var paymentLinks = await _context.IntegrationEntityLinks
                .Where(l => l.ConnectionId == connectionId &&
                            l.ProviderKey == IntegrationProviderKeys.Xero &&
                            l.LocalEntityType == "payment" &&
                            paymentIds.Contains(l.LocalEntityId))
                .ToDictionaryAsync(l => l.LocalEntityId, StringComparer.Ordinal, cancellationToken);
            var invoiceLinks = await _context.IntegrationEntityLinks
                .Where(l => l.ConnectionId == connectionId &&
                            l.ProviderKey == IntegrationProviderKeys.Xero &&
                            l.LocalEntityType == "invoice" &&
                            invoiceIds.Contains(l.LocalEntityId))
                .ToDictionaryAsync(l => l.LocalEntityId, StringComparer.Ordinal, cancellationToken);

            var localInvoices = await _context.Invoices
                .Where(i => invoiceIds.Contains(i.Id))
                .Select(i => new { i.Id, i.Number, i.ClientId, i.MatterId })
                .ToDictionaryAsync(i => i.Id, StringComparer.Ordinal, cancellationToken);

            var existingOpenConflicts = await _context.IntegrationConflictQueueItems
                .Where(c => c.ConnectionId == connectionId &&
                            c.ProviderKey == IntegrationProviderKeys.Xero &&
                            (c.Status == IntegrationConflictStatuses.Open || c.Status == IntegrationConflictStatuses.InReview))
                .ToDictionaryAsync(c => c.Fingerprint ?? c.Id, StringComparer.OrdinalIgnoreCase, cancellationToken);

            var accountCode = await GetDefaultMappingValueAsync(connectionId, IntegrationProviderKeys.Xero, "payment", "operatingClearing", cancellationToken)
                             ?? await GetDefaultMappingValueAsync(connectionId, IntegrationProviderKeys.Xero, "payment", "operatingBank", cancellationToken)
                             ?? await GetDefaultMappingValueAsync(connectionId, IntegrationProviderKeys.Xero, "payment", "operating", cancellationToken);
            if (string.IsNullOrWhiteSpace(accountCode))
            {
                return 0;
            }

            var writes = 0;
            foreach (var payment in payments)
            {
                if (IsProviderAuthoredAccountingPaymentSource(payment.Source, IntegrationProviderKeys.Xero))
                {
                    continue;
                }

                if (!invoiceLinks.TryGetValue(payment.InvoiceId!, out var invoiceLink) || string.IsNullOrWhiteSpace(invoiceLink.ExternalEntityId))
                {
                    continue;
                }

                if (!localInvoices.TryGetValue(payment.InvoiceId!, out var localInvoice))
                {
                    continue;
                }

                var splitPolicy = await ResolveAccountingSplitReceivablePolicyAsync(
                    connectionId,
                    IntegrationProviderKeys.Xero,
                    new Invoice
                    {
                        Id = localInvoice.Id,
                        Number = localInvoice.Number,
                        ClientId = localInvoice.ClientId,
                        MatterId = localInvoice.MatterId
                    },
                    existingOpenConflicts,
                    cancellationToken);
                if (splitPolicy.BlockOutbound)
                {
                    continue;
                }

                if (splitPolicy.InvoicePayorAllocationId != null && string.IsNullOrWhiteSpace(payment.PayorClientId))
                {
                    QueueOrRefreshConflict(
                        existingOpenConflicts,
                        connectionId,
                        IntegrationProviderKeys.Xero,
                        runId: null,
                        entityType: "payment",
                        localEntityId: payment.Id,
                        externalEntityId: null,
                        conflictType: "payment_payor_target_required",
                        severity: "high",
                        summary: $"Payment {payment.Id} belongs to a split-billed invoice and requires explicit payor targeting before Xero push.",
                        localSnapshotJson: JsonSerializer.Serialize(new { payment.Id, payment.InvoiceId, payment.ClientId, payment.PayorClientId, splitPolicy.Mode }),
                        externalSnapshotJson: null,
                        suggestedResolutionJson: JsonSerializer.Serialize(new { action = "set_payment_payor_target", paymentId = payment.Id, providerKey = IntegrationProviderKeys.Xero }),
                        sourceHint: $"xero_payment_payor_target_required:{payment.Id}");
                    continue;
                }

                var netAmount = NormalizeConnectorMoney(payment.Amount - (payment.RefundAmount ?? 0m));
                if (netAmount <= 0m)
                {
                    continue;
                }

                using var paymentDoc = await PostJsonAsync(
                    "https://api.xero.com/api.xro/2.0/Payments",
                    new
                    {
                        Payments = new object[]
                        {
                            new
                            {
                                PaymentID = paymentLinks.TryGetValue(payment.Id, out var existingLink) ? existingLink.ExternalEntityId : null,
                                Invoice = new { InvoiceID = invoiceLink.ExternalEntityId },
                                Account = new { Code = accountCode },
                                Date = (payment.ProcessedAt ?? payment.CreatedAt).ToString("yyyy-MM-dd"),
                                Amount = netAmount,
                                Reference = payment.Id
                            }
                        }
                    },
                    authScheme: "Bearer",
                    authToken: accessToken,
                    additionalHeaders: new Dictionary<string, string> { ["xero-tenant-id"] = tenantId },
                    cancellationToken: cancellationToken);

                if (!TryGetFirstXeroRow(paymentDoc.RootElement, "Payments", out var remotePayment))
                {
                    continue;
                }

                var remotePaymentId = GetString(remotePayment, "PaymentID");
                if (string.IsNullOrWhiteSpace(remotePaymentId))
                {
                    continue;
                }

                UpsertIntegrationEntityLink(
                    paymentLinks,
                    connectionId,
                    IntegrationProviderKeys.Xero,
                    "payment",
                    payment.Id,
                    "payment",
                    remotePaymentId!,
                    GetString(remotePayment, "UpdatedDateUTC"),
                    "outbound",
                    remotePayment.GetRawText());
                writes++;
            }

            if (_context.ChangeTracker.HasChanges())
            {
                await _context.SaveChangesAsync(cancellationToken);
            }

            return writes;
        }

        private async Task<string?> EnsureXeroContactRefAsync(
            string connectionId,
            string accessToken,
            string tenantId,
            string clientId,
            string clientName,
            string clientEmail,
            IDictionary<string, IntegrationEntityLink> clientLinks,
            CancellationToken cancellationToken)
        {
            if (clientLinks.TryGetValue(clientId, out var existing) && !string.IsNullOrWhiteSpace(existing.ExternalEntityId))
            {
                return existing.ExternalEntityId;
            }

            using var contactDoc = await PostJsonAsync(
                "https://api.xero.com/api.xro/2.0/Contacts",
                new
                {
                    Contacts = new object[]
                    {
                        new
                        {
                            Name = string.IsNullOrWhiteSpace(clientName) ? clientId : clientName,
                            EmailAddress = string.IsNullOrWhiteSpace(clientEmail) ? null : clientEmail,
                            ContactNumber = clientId
                        }
                    }
                },
                authScheme: "Bearer",
                authToken: accessToken,
                additionalHeaders: new Dictionary<string, string> { ["xero-tenant-id"] = tenantId },
                cancellationToken: cancellationToken);

            if (!TryGetFirstXeroRow(contactDoc.RootElement, "Contacts", out var contact))
            {
                return null;
            }

            var contactId = GetString(contact, "ContactID");
            if (string.IsNullOrWhiteSpace(contactId))
            {
                return null;
            }

            UpsertIntegrationEntityLink(
                clientLinks,
                connectionId,
                IntegrationProviderKeys.Xero,
                "client",
                clientId,
                "customer",
                contactId!,
                GetString(contact, "UpdatedDateUTC"),
                "outbound",
                contact.GetRawText());
            return contactId;
        }

        private async Task<(List<JsonElement> rows, int pages, bool truncated)> GetXeroPagedCollectionAsync(
            string resourcePath,
            string collectionProperty,
            string accessToken,
            string tenantId,
            int pageLimit,
            CancellationToken cancellationToken)
        {
            var rows = new List<JsonElement>();
            var pages = 0;
            var truncated = false;
            var lastCount = 0;

            for (var page = 1; page <= pageLimit; page++)
            {
                var separator = resourcePath.Contains('?', StringComparison.Ordinal) ? '&' : '?';
                var url = $"https://api.xero.com/api.xro/2.0/{resourcePath}{separator}page={page}";
                using var doc = await GetJsonAsync(
                    url,
                    authScheme: "Bearer",
                    authToken: accessToken,
                    additionalHeaders: new Dictionary<string, string> { ["xero-tenant-id"] = tenantId },
                    cancellationToken: cancellationToken);

                pages++;
                if (!doc.RootElement.TryGetProperty(collectionProperty, out var collection) || collection.ValueKind != JsonValueKind.Array)
                {
                    break;
                }

                var pageRows = collection.EnumerateArray().Select(e => e.Clone()).ToList();
                lastCount = pageRows.Count;
                if (pageRows.Count == 0)
                {
                    break;
                }

                rows.AddRange(pageRows);
                if (pageRows.Count < 100)
                {
                    break;
                }
            }

            if (pages >= pageLimit && lastCount >= 100)
            {
                truncated = true;
            }

            return (rows, pages, truncated);
        }

        private async Task HydrateXeroClientContactLinksAsync(
            string connectionId,
            IReadOnlyCollection<JsonElement> remoteContacts,
            CancellationToken cancellationToken)
        {
            if (remoteContacts.Count == 0)
            {
                return;
            }

            var contactNumbers = remoteContacts
                .Select(c => GetString(c, "ContactNumber"))
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Cast<string>()
                .ToHashSet(StringComparer.Ordinal);
            var normalizedEmails = remoteContacts
                .Select(c => NormalizeEmailForMatch(GetString(c, "EmailAddress")))
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Cast<string>()
                .ToHashSet(StringComparer.Ordinal);

            var candidates = await _context.Clients
                .AsNoTracking()
                .Where(c =>
                    contactNumbers.Contains(c.Id) ||
                    (!string.IsNullOrWhiteSpace(c.NormalizedEmail) && normalizedEmails.Contains(c.NormalizedEmail)))
                .Select(c => new { c.Id, c.Name, c.Email, c.NormalizedEmail })
                .ToListAsync(cancellationToken);

            var clientsById = candidates.ToDictionary(c => c.Id, StringComparer.Ordinal);
            var clientsByEmail = candidates
                .Where(c => !string.IsNullOrWhiteSpace(c.NormalizedEmail))
                .GroupBy(c => c.NormalizedEmail!, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

            var clientLinks = await _context.IntegrationEntityLinks
                .Where(l => l.ConnectionId == connectionId &&
                            l.ProviderKey == IntegrationProviderKeys.Xero &&
                            l.LocalEntityType == "client")
                .ToDictionaryAsync(l => l.LocalEntityId, StringComparer.Ordinal, cancellationToken);

            var existingOpen = await _context.IntegrationConflictQueueItems
                .Where(c => c.ConnectionId == connectionId &&
                            c.ProviderKey == IntegrationProviderKeys.Xero &&
                            (c.Status == IntegrationConflictStatuses.Open || c.Status == IntegrationConflictStatuses.InReview))
                .ToDictionaryAsync(c => c.Fingerprint ?? c.Id, StringComparer.OrdinalIgnoreCase, cancellationToken);

            var createdOrUpdatedConflicts = 0;
            foreach (var remote in remoteContacts)
            {
                var remoteContactId = GetString(remote, "ContactID");
                if (string.IsNullOrWhiteSpace(remoteContactId))
                {
                    continue;
                }

                var contactNumber = GetString(remote, "ContactNumber");
                if (!string.IsNullOrWhiteSpace(contactNumber) && clientsById.ContainsKey(contactNumber))
                {
                    UpsertIntegrationEntityLink(
                        clientLinks,
                        connectionId,
                        IntegrationProviderKeys.Xero,
                        "client",
                        contactNumber,
                        "customer",
                        remoteContactId,
                        GetString(remote, "UpdatedDateUTC"),
                        "inbound",
                        remote.GetRawText());
                    continue;
                }

                var normalizedEmail = NormalizeEmailForMatch(GetString(remote, "EmailAddress"));
                if (string.IsNullOrWhiteSpace(normalizedEmail) || !clientsByEmail.TryGetValue(normalizedEmail, out var matches))
                {
                    continue;
                }

                if (matches.Count == 1)
                {
                    UpsertIntegrationEntityLink(
                        clientLinks,
                        connectionId,
                        IntegrationProviderKeys.Xero,
                        "client",
                        matches[0].Id,
                        "customer",
                        remoteContactId,
                        GetString(remote, "UpdatedDateUTC"),
                        "inbound",
                        remote.GetRawText());
                    continue;
                }

                createdOrUpdatedConflicts += QueueOrRefreshConflict(
                    existingOpen,
                    connectionId,
                    IntegrationProviderKeys.Xero,
                    runId: null,
                    entityType: "customer",
                    localEntityId: null,
                    externalEntityId: remoteContactId,
                    conflictType: "dedupe_collision",
                    severity: "medium",
                    summary: $"Xero contact email matches multiple local clients ({normalizedEmail}); manual mapping required.",
                    localSnapshotJson: JsonSerializer.Serialize(matches.Select(m => new { m.Id, m.Name, m.Email })),
                    externalSnapshotJson: remote.GetRawText(),
                    suggestedResolutionJson: JsonSerializer.Serialize(new { action = "map_customer", externalEntityId = remoteContactId }),
                    sourceHint: "xero_contact_email_collision");
            }

            if (_context.ChangeTracker.HasChanges() || createdOrUpdatedConflicts > 0)
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
        }

        private async Task<int> GenerateXeroPaymentCoverageConflictsAsync(
            string connectionId,
            IReadOnlyCollection<JsonElement> remotePayments,
            CancellationToken cancellationToken)
        {
            var providerKey = IntegrationProviderKeys.Xero;
            var since = DateTime.UtcNow.AddDays(-90);

            var localPayments = await _context.PaymentTransactions
                .Where(p => p.CreatedAt >= since &&
                            p.InvoiceId != null &&
                            (p.Status == "Succeeded" || p.Status == "Partially Refunded" || p.Status == "Refunded"))
                .ToListAsync(cancellationToken);

            var paymentLinks = await _context.IntegrationEntityLinks
                .Where(l => l.ConnectionId == connectionId &&
                            l.ProviderKey == providerKey &&
                            l.LocalEntityType == "payment")
                .ToDictionaryAsync(l => l.LocalEntityId, StringComparer.Ordinal, cancellationToken);

            var existingOpen = await _context.IntegrationConflictQueueItems
                .Where(c => c.ConnectionId == connectionId &&
                            c.ProviderKey == providerKey &&
                            (c.Status == IntegrationConflictStatuses.Open || c.Status == IntegrationConflictStatuses.InReview))
                .ToDictionaryAsync(c => c.Fingerprint ?? c.Id, StringComparer.OrdinalIgnoreCase, cancellationToken);

            var remoteByReference = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            foreach (var remote in remotePayments)
            {
                var reference = GetString(remote, "Reference");
                if (!string.IsNullOrWhiteSpace(reference) && !remoteByReference.ContainsKey(reference))
                {
                    remoteByReference[reference] = remote;
                }
            }

            var createdOrUpdated = 0;
            foreach (var payment in localPayments)
            {
                if (remoteByReference.TryGetValue(payment.Id, out var remote))
                {
                    UpsertIntegrationEntityLink(
                        paymentLinks,
                        connectionId,
                        providerKey,
                        "payment",
                        payment.Id,
                        "payment",
                        GetString(remote, "PaymentID") ?? payment.Id,
                        GetString(remote, "UpdatedDateUTC"),
                        "inbound",
                        remote.GetRawText());

                    var remoteAmount = GetDecimal(remote, "Amount");
                    var localNet = NormalizeConnectorMoney(payment.Amount - (payment.RefundAmount ?? 0m));
                    if (remoteAmount.HasValue && Math.Abs(remoteAmount.Value - localNet) > 0.01m)
                    {
                        createdOrUpdated += QueueOrRefreshConflict(
                            existingOpen,
                            connectionId,
                            providerKey,
                            runId: null,
                            entityType: "payment",
                            localEntityId: payment.Id,
                            externalEntityId: GetString(remote, "PaymentID"),
                            conflictType: "field_mismatch",
                            severity: "high",
                            summary: $"Xero payment amount mismatch for payment {payment.Id}.",
                            localSnapshotJson: JsonSerializer.Serialize(new { payment.Id, payment.Amount, payment.RefundAmount, netAmount = localNet, payment.Status }),
                            externalSnapshotJson: remote.GetRawText(),
                            suggestedResolutionJson: JsonSerializer.Serialize(new { action = "reconcile", entityType = "payment", localEntityId = payment.Id }),
                            sourceHint: "xero_payment_field_mismatch");
                    }

                    continue;
                }

                if (IsProviderAuthoredAccountingPaymentSource(payment.Source, providerKey))
                {
                    continue;
                }

                if (payment.Status != "Succeeded" && payment.Status != "Partially Refunded")
                {
                    continue;
                }

                createdOrUpdated += QueueOrRefreshConflict(
                    existingOpen,
                    connectionId,
                    providerKey,
                    runId: null,
                    entityType: "payment",
                    localEntityId: payment.Id,
                    externalEntityId: paymentLinks.TryGetValue(payment.Id, out var link) ? link.ExternalEntityId : null,
                    conflictType: "missing_remote_record",
                    severity: "medium",
                    summary: $"Xero payment not found for local payment {payment.Id}.",
                    localSnapshotJson: JsonSerializer.Serialize(new { payment.Id, payment.Amount, payment.RefundAmount, payment.Status, payment.InvoiceId }),
                    externalSnapshotJson: null,
                    suggestedResolutionJson: JsonSerializer.Serialize(new { action = "push", entityType = "payment", localEntityId = payment.Id }),
                    sourceHint: "xero_missing_remote_payment");
            }

            if (_context.ChangeTracker.HasChanges() || createdOrUpdated > 0)
            {
                await _context.SaveChangesAsync(cancellationToken);
            }

            return await CountOpenConflictsForConnectionAsync(connectionId, cancellationToken);
        }

        private async Task<string?> GetDefaultMappingValueAsync(
            string connectionId,
            string providerKey,
            string entityType,
            string key,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return null;
            }

            var profiles = await _context.IntegrationMappingProfiles
                .AsNoTracking()
                .Where(p => p.ConnectionId == connectionId &&
                            p.ProviderKey == providerKey &&
                            p.EntityType == entityType &&
                            p.Status == "active")
                .OrderByDescending(p => p.IsDefault)
                .ThenBy(p => p.ProfileKey)
                .Select(p => new { p.AccountMappingsJson, p.DefaultsJson, p.MetadataJson })
                .ToListAsync(cancellationToken);

            foreach (var profile in profiles)
            {
                var value = TryExtractJsonString(profile.AccountMappingsJson, key)
                            ?? TryExtractJsonString(profile.DefaultsJson, key)
                            ?? TryExtractJsonString(profile.MetadataJson, key);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return null;
        }

        private async Task<string?> GetDefaultTaxMappingValueAsync(
            string connectionId,
            string providerKey,
            string entityType,
            CancellationToken cancellationToken)
        {
            var profiles = await _context.IntegrationMappingProfiles
                .AsNoTracking()
                .Where(p => p.ConnectionId == connectionId &&
                            p.ProviderKey == providerKey &&
                            p.EntityType == entityType &&
                            p.Status == "active")
                .OrderByDescending(p => p.IsDefault)
                .ThenBy(p => p.ProfileKey)
                .Select(p => new { p.TaxMappingsJson, p.DefaultsJson })
                .ToListAsync(cancellationToken);

            foreach (var profile in profiles)
            {
                var direct = TryExtractJsonString(profile.TaxMappingsJson, "defaultTaxType")
                             ?? TryExtractJsonString(profile.DefaultsJson, "defaultTaxType");
                if (!string.IsNullOrWhiteSpace(direct))
                {
                    return direct;
                }

                if (TryParseJson(profile.TaxMappingsJson, out var taxMappings))
                {
                    var first = TryExtractFirstJsonString(taxMappings);
                    if (!string.IsNullOrWhiteSpace(first))
                    {
                        return first;
                    }
                }
            }

            return null;
        }

        private static bool TryGetFirstXeroRow(JsonElement root, string collectionProperty, out JsonElement row)
        {
            row = default;
            if (!root.TryGetProperty(collectionProperty, out var collection) || collection.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var item in collection.EnumerateArray())
            {
                row = item.Clone();
                return true;
            }

            return false;
        }

        private static string? NormalizeEmailForMatch(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
        }

        private static string? TryExtractJsonString(string? json, string key)
        {
            if (!TryParseJson(json, out var root))
            {
                return null;
            }

            return TryExtractJsonString(root, key);
        }

        private static string? TryExtractJsonString(JsonElement root, string key)
        {
            if (root.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in root.EnumerateObject())
                {
                    if (string.Equals(property.Name, key, StringComparison.OrdinalIgnoreCase))
                    {
                        return property.Value.ValueKind switch
                        {
                            JsonValueKind.String => property.Value.GetString(),
                            JsonValueKind.Number => property.Value.GetRawText(),
                            JsonValueKind.True => "true",
                            JsonValueKind.False => "false",
                            _ => null
                        };
                    }

                    var nested = TryExtractJsonString(property.Value, key);
                    if (!string.IsNullOrWhiteSpace(nested))
                    {
                        return nested;
                    }
                }
            }
            else if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in root.EnumerateArray())
                {
                    var nested = TryExtractJsonString(item, key);
                    if (!string.IsNullOrWhiteSpace(nested))
                    {
                        return nested;
                    }
                }
            }

            return null;
        }

        private static string? TryExtractFirstJsonString(JsonElement root)
        {
            return root.ValueKind switch
            {
                JsonValueKind.String => root.GetString(),
                JsonValueKind.Object => root.EnumerateObject().Select(p => TryExtractFirstJsonString(p.Value)).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)),
                JsonValueKind.Array => root.EnumerateArray().Select(TryExtractFirstJsonString).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)),
                _ => null
            };
        }

        private async Task<CanonicalIntegrationActionResult> ExecuteStripeCanonicalActionAsync(
            IntegrationConnection connection,
            CanonicalIntegrationActionRequest request,
            CancellationToken cancellationToken)
        {
            var action = (request.Action ?? string.Empty).Trim().ToLowerInvariant();

            if (action == IntegrationCanonicalActions.Push)
            {
                return CanonicalIntegrationActionResult.Unsupported(action, connection.ProviderKey);
            }

            if (action == IntegrationCanonicalActions.Webhook)
            {
                var conflictCount = await GenerateStripeAccountingBridgeConflictsAsync(connection.Id, cancellationToken);
                return new CanonicalIntegrationActionResult
                {
                    Success = true,
                    Retryable = false,
                    Action = action,
                    Status = "completed",
                    Message = $"Stripe webhook-first reconciliation refresh completed. Conflicts={conflictCount}.",
                    ConflictCount = conflictCount,
                    ResultJson = JsonSerializer.Serialize(new { action, conflictCount, sourceOfTruth = "provider" })
                };
            }

            if (action == IntegrationCanonicalActions.Reconcile)
            {
                var conflictCount = await GenerateStripeAccountingBridgeConflictsAsync(connection.Id, cancellationToken);
                return new CanonicalIntegrationActionResult
                {
                    Success = true,
                    Retryable = false,
                    Action = action,
                    Status = "completed",
                    Message = $"Stripe to accounting reconciliation bridge completed. Conflicts={conflictCount}.",
                    ConflictCount = conflictCount,
                    ResultJson = JsonSerializer.Serialize(new
                    {
                        action,
                        conflictCount,
                        entitySourceOfTruth = new
                        {
                            invoice = "jurisflow",
                            payment = "provider",
                            customer = "jurisflow"
                        }
                    })
                };
            }

            if (action is IntegrationCanonicalActions.Pull or IntegrationCanonicalActions.Backfill)
            {
                var syncResult = await SyncStripeConnectionAsync(connection.Id, connection.MetadataJson, cancellationToken);
                connection.MetadataJson = syncResult.MetadataJson ?? connection.MetadataJson;
                connection.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync(cancellationToken);

                var conflictCount = syncResult.Success
                    ? await GenerateStripeAccountingBridgeConflictsAsync(connection.Id, cancellationToken)
                    : 0;

                return new CanonicalIntegrationActionResult
                {
                    Success = syncResult.Success,
                    Retryable = syncResult.Retryable,
                    Action = action,
                    Status = syncResult.Success ? "completed" : "failed",
                    Message = syncResult.Message,
                    ErrorCode = syncResult.ErrorCode,
                    ErrorMessage = syncResult.ErrorMessage,
                    ReadCount = syncResult.Success ? syncResult.SyncedCount : 0,
                    ConflictCount = conflictCount,
                    ResultJson = JsonSerializer.Serialize(new
                    {
                        action,
                        syncResult.Success,
                        syncResult.SyncedCount,
                        syncResult.Message,
                        conflicts = conflictCount
                    })
                };
            }

            return CanonicalIntegrationActionResult.Unsupported(action, connection.ProviderKey);
        }

        private async Task<CanonicalIntegrationActionResult> ExecuteQuickBooksCanonicalActionAsync(
            IntegrationConnection connection,
            CanonicalIntegrationActionRequest request,
            CancellationToken cancellationToken)
        {
            var metadata = DeserializeMetadata(connection.MetadataJson);
            await HydrateCredentialsAsync(connection.Id, metadata, IntegrationSecretScope.Sync, cancellationToken);

            var action = (request.Action ?? string.Empty).Trim().ToLowerInvariant();
            if (action == IntegrationCanonicalActions.Reconcile)
            {
                await EnsureAccountingMappingCoverageConflictsAsync(connection.Id, connection.ProviderKey, cancellationToken);
                await RefreshQuickBooksTokenIfNeededAsync(metadata, cancellationToken);
                var conflictCount = await GenerateQuickBooksAccountingConflictsAsync(connection.Id, cancellationToken);
                var syncResult = await CompleteSyncResultAsync(
                    connection.Id,
                    connection.ProviderKey,
                    metadata,
                    new IntegrationSyncResult
                    {
                        Success = true,
                        SyncedCount = 0,
                        Message = $"QuickBooks reconciliation completed. Conflicts={conflictCount}."
                    },
                    cancellationToken);

                connection.MetadataJson = syncResult.MetadataJson ?? connection.MetadataJson;
                connection.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync(cancellationToken);

                return new CanonicalIntegrationActionResult
                {
                    Success = true,
                    Retryable = false,
                    Action = IntegrationCanonicalActions.Reconcile,
                    Status = "completed",
                    Message = syncResult.Message,
                    ConflictCount = conflictCount,
                    ResultJson = JsonSerializer.Serialize(new { conflictCount, action = "reconcile" })
                };
            }

            IntegrationSyncResult syncLike;
            if (action == IntegrationCanonicalActions.Push || action == IntegrationCanonicalActions.Pull)
            {
                await RefreshQuickBooksTokenIfNeededAsync(metadata, cancellationToken);
                var accessToken = metadata.Credentials.AccessToken;
                var realmId = metadata.Credentials.RealmId;
                if (string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(realmId))
                {
                    syncLike = new IntegrationSyncResult
                    {
                        Success = false,
                        Retryable = false,
                        ErrorCode = "missing_credentials",
                        ErrorMessage = "QuickBooks credentials are incomplete."
                    };
                }
                else
                {
                    var apiBaseUrl = _configuration["Integrations:QuickBooks:ApiBaseUrl"]?.TrimEnd('/') ?? "https://quickbooks.api.intuit.com";
                    var writes = 0;
                    var reads = 0;
                    if (action == IntegrationCanonicalActions.Push)
                    {
                        var pushLock = await EvaluateAccountingPushPeriodLockAsync(connection.Id, cancellationToken);
                        if (pushLock.hasLockedRecords)
                        {
                            return new CanonicalIntegrationActionResult
                            {
                                Success = false,
                                Retryable = false,
                                Action = action,
                                Status = "blocked",
                                ErrorCode = "billing_period_locked",
                                ErrorMessage = $"Accounting push blocked by billing lock. LockedInvoices={pushLock.lockedInvoices}, LockedPayments={pushLock.lockedPayments}.",
                                ResultJson = JsonSerializer.Serialize(new
                                {
                                    action,
                                    pushLock.lockedInvoices,
                                    pushLock.lockedPayments,
                                    policy = "period_lock_blocks_push"
                                })
                            };
                        }

                        await EnsureAccountingMappingCoverageConflictsAsync(connection.Id, connection.ProviderKey, cancellationToken);
                        writes += await SyncQuickBooksOutboundInvoicesAsync(connection.Id, apiBaseUrl, realmId, accessToken, cancellationToken);
                        writes += await SyncQuickBooksOutboundPaymentsAsync(connection.Id, apiBaseUrl, realmId, accessToken, cancellationToken);
                    }
                    else
                    {
                        reads += await SyncQuickBooksInboundInvoicesAsync(connection.Id, apiBaseUrl, realmId, accessToken, cancellationToken);
                        reads += await SyncQuickBooksInboundPaymentsAsync(connection.Id, apiBaseUrl, realmId, accessToken, cancellationToken);
                    }

                    var conflictCount = await GenerateQuickBooksAccountingConflictsAsync(connection.Id, cancellationToken);
                    syncLike = new IntegrationSyncResult
                    {
                        Success = true,
                        SyncedCount = writes + reads,
                        Message = action == IntegrationCanonicalActions.Push
                            ? $"QuickBooks push completed. Writes={writes}, Conflicts={conflictCount}."
                            : $"QuickBooks pull completed. Reads={reads}, Conflicts={conflictCount}."
                    };
                }
            }
            else if (action == IntegrationCanonicalActions.Backfill)
            {
                await EnsureAccountingMappingCoverageConflictsAsync(connection.Id, connection.ProviderKey, cancellationToken);
                syncLike = await SyncQuickBooksAsync(connection.Id, metadata, cancellationToken);
            }
            else
            {
                return CanonicalIntegrationActionResult.Unsupported(action, connection.ProviderKey);
            }

            var completed = await CompleteSyncResultAsync(connection.Id, connection.ProviderKey, metadata, syncLike, cancellationToken);
            connection.MetadataJson = completed.MetadataJson ?? connection.MetadataJson;
            connection.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);

            var quickBooksConflicts = completed.Success
                ? await CountOpenConflictsForConnectionAsync(connection.Id, cancellationToken)
                : 0;

            return new CanonicalIntegrationActionResult
            {
                Success = completed.Success,
                Retryable = completed.Retryable,
                Action = action,
                Status = completed.Success ? "completed" : "failed",
                Message = completed.Message,
                ErrorCode = completed.ErrorCode,
                ErrorMessage = completed.ErrorMessage,
                ReadCount = action == IntegrationCanonicalActions.Pull ? completed.SyncedCount : 0,
                WriteCount = action is IntegrationCanonicalActions.Push or IntegrationCanonicalActions.Backfill ? completed.SyncedCount : 0,
                ConflictCount = quickBooksConflicts,
                ResultJson = JsonSerializer.Serialize(new
                {
                    action,
                    completed.Success,
                    completed.SyncedCount,
                    completed.Message,
                    conflicts = quickBooksConflicts
                })
            };
        }

        private async Task<CanonicalIntegrationActionResult> ExecuteXeroCanonicalActionAsync(
            IntegrationConnection connection,
            CanonicalIntegrationActionRequest request,
            CancellationToken cancellationToken)
        {
            var action = (request.Action ?? string.Empty).Trim().ToLowerInvariant();
            var metadata = DeserializeMetadata(connection.MetadataJson);
            await HydrateCredentialsAsync(connection.Id, metadata, IntegrationSecretScope.Sync, cancellationToken);

            if (action == IntegrationCanonicalActions.Pull ||
                action == IntegrationCanonicalActions.Backfill ||
                action == IntegrationCanonicalActions.Reconcile)
            {
                await EnsureAccountingMappingCoverageConflictsAsync(connection.Id, connection.ProviderKey, cancellationToken);
                var syncResult = await SyncXeroAsync(connection.Id, metadata, cancellationToken);
                syncResult = await CompleteSyncResultAsync(connection.Id, connection.ProviderKey, metadata, syncResult, cancellationToken);
                connection.MetadataJson = syncResult.MetadataJson ?? connection.MetadataJson;
                connection.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync(cancellationToken);

                var conflictCount = syncResult.Success
                    ? await CountOpenConflictsForConnectionAsync(connection.Id, cancellationToken)
                    : 0;

                return new CanonicalIntegrationActionResult
                {
                    Success = syncResult.Success,
                    Retryable = syncResult.Retryable,
                    Action = action,
                    Status = syncResult.Success ? "completed" : "failed",
                    Message = syncResult.Message,
                    ErrorCode = syncResult.ErrorCode,
                    ErrorMessage = syncResult.ErrorMessage,
                    ReadCount = syncResult.Success ? syncResult.SyncedCount : 0,
                    ConflictCount = conflictCount,
                    ResultJson = JsonSerializer.Serialize(new
                    {
                        action,
                        syncResult.Success,
                        syncResult.SyncedCount,
                        syncResult.Message,
                        conflicts = conflictCount
                    })
                };
            }

            if (action == IntegrationCanonicalActions.Push)
            {
                await RefreshXeroTokenIfNeededAsync(metadata, cancellationToken);
                var accessToken = metadata.Credentials.AccessToken;
                var tenantId = metadata.Credentials.TenantId;
                if (string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(tenantId))
                {
                    return new CanonicalIntegrationActionResult
                    {
                        Success = false,
                        Retryable = false,
                        Action = action,
                        Status = "failed",
                        ErrorCode = "missing_credentials",
                        ErrorMessage = "Xero credentials are incomplete."
                    };
                }

                var pushLock = await EvaluateAccountingPushPeriodLockAsync(connection.Id, IntegrationProviderKeys.Xero, cancellationToken);
                if (pushLock.hasLockedRecords)
                {
                    return new CanonicalIntegrationActionResult
                    {
                        Success = false,
                        Retryable = false,
                        Action = action,
                        Status = "blocked",
                        ErrorCode = "billing_period_locked",
                        ErrorMessage = $"Accounting push blocked by billing lock. LockedInvoices={pushLock.lockedInvoices}, LockedPayments={pushLock.lockedPayments}.",
                        ResultJson = JsonSerializer.Serialize(new
                        {
                            action,
                            pushLock.lockedInvoices,
                            pushLock.lockedPayments,
                            policy = "period_lock_blocks_push"
                        })
                    };
                }

                await EnsureAccountingMappingCoverageConflictsAsync(connection.Id, connection.ProviderKey, cancellationToken);
                var writes = await SyncXeroOutboundInvoicesAsync(connection.Id, accessToken, tenantId, cancellationToken);
                writes += await SyncXeroOutboundPaymentsAsync(connection.Id, accessToken, tenantId, cancellationToken);
                var conflictCount = await CountOpenConflictsForConnectionAsync(connection.Id, cancellationToken);

                var completed = await CompleteSyncResultAsync(
                    connection.Id,
                    connection.ProviderKey,
                    metadata,
                    new IntegrationSyncResult
                    {
                        Success = true,
                        SyncedCount = writes,
                        Message = $"Xero push completed. Writes={writes}, Conflicts={conflictCount}."
                    },
                    cancellationToken);

                connection.MetadataJson = completed.MetadataJson ?? connection.MetadataJson;
                connection.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync(cancellationToken);

                return new CanonicalIntegrationActionResult
                {
                    Success = true,
                    Retryable = false,
                    Action = action,
                    Status = "completed",
                    Message = completed.Message,
                    WriteCount = writes,
                    ConflictCount = conflictCount,
                    ResultJson = JsonSerializer.Serialize(new { action, writes, conflicts = conflictCount })
                };
            }

            return CanonicalIntegrationActionResult.Unsupported(action, connection.ProviderKey);
        }

        private async Task<IntegrationSyncResult> SyncCourtListenerDocketsAsync(
            string connectionId,
            IntegrationMetadataEnvelope metadata,
            CancellationToken cancellationToken)
        {
            var apiToken = metadata.Credentials.ApiKey;
            if (string.IsNullOrWhiteSpace(apiToken))
            {
                return new IntegrationSyncResult
                {
                    Success = false,
                    Retryable = false,
                    ErrorCode = "missing_credentials",
                    ErrorMessage = "CourtListener API token is missing."
                };
            }

            if (!HasCourtListenerPolicyAcknowledgement(metadata))
            {
                return new IntegrationSyncResult
                {
                    Success = false,
                    Retryable = false,
                    ErrorCode = "policy_ack_required",
                    ErrorMessage = "Court data usage policy acknowledgement is required for CourtListener docket sync."
                };
            }

            var baseUrl = _configuration["Integrations:CourtListener:ApiBaseUrl"]?.TrimEnd('/')
                          ?? "https://www.courtlistener.com/api/rest/v4";
            var syncState = await _context.IntegrationConnections
                .AsNoTracking()
                .Where(c => c.Id == connectionId)
                .Select(c => new { c.SyncCursor })
                .FirstOrDefaultAsync(cancellationToken);

            var sinceUtc = ParseProviderDateTime(syncState?.SyncCursor)
                           ?? (metadata.LastSync != null ? metadata.LastSync.SyncedAtUtc.AddMinutes(-5) : null);
            var pageLimit = Math.Clamp(_configuration.GetValue<int?>("Integrations:CourtListener:DocketsPollingMaxPages") ?? 4, 1, 20);
            var requestUrl = $"{baseUrl}/dockets/?page_size=50&ordering=-date_modified";
            var pages = 0;
            var moreAvailable = false;
            var newestModifiedAt = sinceUtc;
            var incoming = new List<JsonElement>();

            while (!string.IsNullOrWhiteSpace(requestUrl))
            {
                using var doc = await GetJsonAsync(
                    requestUrl,
                    authScheme: "Token",
                    authToken: apiToken,
                    additionalHeaders: null,
                    cancellationToken: cancellationToken);

                var pageRows = ResolveSubmissionRows(doc.RootElement);
                if (pageRows.Count == 0)
                {
                    pages++;
                    break;
                }

                var sawNewerThanCursor = false;
                foreach (var row in pageRows)
                {
                    var modifiedAt = ParseProviderDateTime(GetString(row, "date_modified"));
                    if (modifiedAt.HasValue && (!newestModifiedAt.HasValue || modifiedAt.Value > newestModifiedAt.Value))
                    {
                        newestModifiedAt = modifiedAt.Value;
                    }

                    if (sinceUtc.HasValue && modifiedAt.HasValue && modifiedAt.Value <= sinceUtc.Value)
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(GetString(row, "id")) || row.TryGetProperty("id", out _))
                    {
                        incoming.Add(row.Clone());
                        sawNewerThanCursor = true;
                    }
                }

                var nextLink = GetString(doc.RootElement, "next");
                pages++;

                if (!string.IsNullOrWhiteSpace(nextLink) && pages >= pageLimit)
                {
                    moreAvailable = true;
                    break;
                }

                if (sinceUtc.HasValue && !sawNewerThanCursor)
                {
                    break;
                }

                requestUrl = nextLink;
            }

            if (incoming.Count == 0)
            {
                return new IntegrationSyncResult
                {
                    Success = true,
                    SyncedCount = 0,
                    NextCursor = (newestModifiedAt ?? DateTime.UtcNow).ToString("O"),
                    Message = $"CourtListener returned no docket updates. Pages={pages}{(moreAvailable ? ", TruncatedByPageLimit=true" : string.Empty)}."
                };
            }

            var externalIds = incoming
                .Select(item => GetString(item, "id") ?? (item.TryGetProperty("id", out var idEl) ? idEl.ToString() : null))
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Cast<string>()
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var existing = externalIds.Count == 0
                ? new Dictionary<string, CourtDocketEntry>(StringComparer.Ordinal)
                : await _context.CourtDocketEntries
                    .Where(d => d.ProviderKey == IntegrationProviderKeys.CourtListenerDockets && externalIds.Contains(d.ExternalDocketId))
                    .ToDictionaryAsync(d => d.ExternalDocketId, StringComparer.Ordinal, cancellationToken);

            var matters = await _context.Matters
                .Select(m => new MatterLookupItem
                {
                    Id = m.Id,
                    ClientId = m.ClientId,
                    CaseNumber = m.CaseNumber,
                    Name = m.Name
                })
                .ToListAsync(cancellationToken);

            var now = DateTime.UtcNow;
            var changed = 0;
            var changedDocketEntryIds = new List<string>();
            foreach (var row in incoming)
            {
                var externalId = GetString(row, "id") ?? (row.TryGetProperty("id", out var idElement) ? idElement.ToString() : null);
                if (string.IsNullOrWhiteSpace(externalId))
                {
                    continue;
                }

                var caseName = GetString(row, "case_name");
                var docketNumber = GetString(row, "docket_number") ?? GetString(row, "docketNumber");
                var externalCaseId = GetString(row, "docket_id") ?? GetString(row, "cluster_id");
                var sourceUrl = GetString(row, "absolute_url") ?? GetString(row, "url");
                var court = GetString(row, "court") ?? GetNestedString(row, "court", "id");
                var matterId = ResolveMatterIdFromDocket(caseName, docketNumber, matters);

                if (existing.TryGetValue(externalId, out var entity))
                {
                    entity.CaseName = caseName ?? entity.CaseName;
                    entity.DocketNumber = docketNumber ?? entity.DocketNumber;
                    entity.ExternalCaseId = externalCaseId ?? entity.ExternalCaseId;
                    entity.SourceUrl = sourceUrl ?? entity.SourceUrl;
                    entity.Court = court ?? entity.Court;
                    entity.FiledAt = ParseProviderDateTime(GetString(row, "date_filed")) ?? entity.FiledAt;
                    entity.ModifiedAt = ParseProviderDateTime(GetString(row, "date_modified")) ?? entity.ModifiedAt;
                    entity.LastSeenAt = now;
                    entity.MatterId = matterId ?? entity.MatterId;
                    entity.MetadataJson = _piiMinimizer.SanitizeProviderMetadataJsonForStorage(
                        IntegrationProviderKeys.CourtListenerDockets,
                        "court_docket_entry",
                        row.GetRawText());
                    entity.UpdatedAt = now;
                    changed++;
                    changedDocketEntryIds.Add(entity.Id);
                }
                else
                {
                    var createdEntry = new CourtDocketEntry
                    {
                        Id = Guid.NewGuid().ToString(),
                        ProviderKey = IntegrationProviderKeys.CourtListenerDockets,
                        ExternalDocketId = externalId,
                        ExternalCaseId = externalCaseId,
                        DocketNumber = docketNumber,
                        CaseName = caseName,
                        Court = court,
                        SourceUrl = sourceUrl,
                        FiledAt = ParseProviderDateTime(GetString(row, "date_filed")),
                        ModifiedAt = ParseProviderDateTime(GetString(row, "date_modified")),
                        LastSeenAt = now,
                        MatterId = matterId,
                        MetadataJson = _piiMinimizer.SanitizeProviderMetadataJsonForStorage(
                            IntegrationProviderKeys.CourtListenerDockets,
                            "court_docket_entry",
                            row.GetRawText()),
                        CreatedAt = now,
                        UpdatedAt = now
                    };
                    _context.CourtDocketEntries.Add(createdEntry);
                    changed++;
                    changedDocketEntryIds.Add(createdEntry.Id);
                }
            }

            EfilingDocketAutomationResult? automationResult = null;
            if (changed > 0)
            {
                await _context.SaveChangesAsync(cancellationToken);
                automationResult = await _efilingAutomationService.ProcessDocketSyncAutomationAsync(
                    connectionId,
                    changedDocketEntryIds.Distinct(StringComparer.Ordinal).ToArray(),
                    cancellationToken);
            }

            return new IntegrationSyncResult
            {
                Success = true,
                SyncedCount = changed,
                NextCursor = (newestModifiedAt ?? DateTime.UtcNow).ToString("O"),
                Message = automationResult == null
                    ? $"CourtListener docket sync completed. Upserts={changed}, Pages={pages}{(moreAvailable ? ", TruncatedByPageLimit=true" : string.Empty)}."
                    : $"CourtListener docket sync completed. Upserts={changed}, Tasks={automationResult.TasksCreated}, Deadlines={automationResult.DeadlinesCreated}, Reviews={automationResult.ReviewsQueued}, Pages={pages}{(moreAvailable ? ", TruncatedByPageLimit=true" : string.Empty)}."
            };
        }

        private async Task<IntegrationSyncResult> SyncCourtListenerRecapAsync(
            string connectionId,
            IntegrationMetadataEnvelope metadata,
            CancellationToken cancellationToken)
        {
            var apiToken = metadata.Credentials.ApiKey;
            if (string.IsNullOrWhiteSpace(apiToken))
            {
                return new IntegrationSyncResult
                {
                    Success = false,
                    Retryable = false,
                    ErrorCode = "missing_credentials",
                    ErrorMessage = "CourtListener API token is missing."
                };
            }

            if (!HasCourtListenerPolicyAcknowledgement(metadata))
            {
                return new IntegrationSyncResult
                {
                    Success = false,
                    Retryable = false,
                    ErrorCode = "policy_ack_required",
                    ErrorMessage = "Court data usage policy acknowledgement is required for CourtListener RECAP sync."
                };
            }

            var baseUrl = _configuration["Integrations:CourtListener:ApiBaseUrl"]?.TrimEnd('/')
                          ?? "https://www.courtlistener.com/api/rest/v4";
            var syncState = await _context.IntegrationConnections
                .AsNoTracking()
                .Where(c => c.Id == connectionId)
                .Select(c => new { c.SyncCursor })
                .FirstOrDefaultAsync(cancellationToken);

            var sinceUtc = ParseProviderDateTime(syncState?.SyncCursor)
                           ?? (metadata.LastSync != null ? metadata.LastSync.SyncedAtUtc.AddMinutes(-5) : null);
            var pageLimit = Math.Clamp(_configuration.GetValue<int?>("Integrations:CourtListener:RecapPollingMaxPages") ?? 4, 1, 20);
            var requestUrl = $"{baseUrl}/recap-fetch/?page_size=50&ordering=-date_created";
            var pages = 0;
            var moreAvailable = false;
            var newestCreatedAt = sinceUtc;
            var rows = new List<JsonElement>();

            while (!string.IsNullOrWhiteSpace(requestUrl))
            {
                using var doc = await GetJsonAsync(
                    requestUrl,
                    authScheme: "Token",
                    authToken: apiToken,
                    additionalHeaders: null,
                    cancellationToken: cancellationToken);

                var pageRows = ResolveSubmissionRows(doc.RootElement);
                if (pageRows.Count == 0)
                {
                    pages++;
                    break;
                }

                var sawNewerThanCursor = false;
                foreach (var row in pageRows)
                {
                    var createdAt = ParseProviderDateTime(GetString(row, "date_created"))
                                    ?? ParseProviderDateTime(GetString(row, "date_modified"));
                    if (createdAt.HasValue && (!newestCreatedAt.HasValue || createdAt.Value > newestCreatedAt.Value))
                    {
                        newestCreatedAt = createdAt.Value;
                    }

                    if (sinceUtc.HasValue && createdAt.HasValue && createdAt.Value <= sinceUtc.Value)
                    {
                        continue;
                    }

                    rows.Add(row.Clone());
                    sawNewerThanCursor = true;
                }

                var nextLink = GetString(doc.RootElement, "next");
                pages++;
                if (!string.IsNullOrWhiteSpace(nextLink) && pages >= pageLimit)
                {
                    moreAvailable = true;
                    break;
                }

                if (sinceUtc.HasValue && !sawNewerThanCursor)
                {
                    break;
                }

                requestUrl = nextLink;
            }

            var result = await UpsertEfilingSubmissionsAsync(
                connectionId,
                IntegrationProviderKeys.CourtListenerRecap,
                rows,
                cancellationToken);

            result.NextCursor = (newestCreatedAt ?? DateTime.UtcNow).ToString("O");
            if (result.Success)
            {
                result.Message = $"{result.Message} Pages={pages}{(moreAvailable ? ", TruncatedByPageLimit=true" : string.Empty)}.";
            }

            return result;
        }

        private async Task<IntegrationSyncResult> SyncGenericEfilingPartnerAsync(
            string connectionId,
            string providerKey,
            string configurationPrefix,
            IntegrationMetadataEnvelope metadata,
            CancellationToken cancellationToken)
        {
            var apiToken = metadata.Credentials.ApiKey;
            if (string.IsNullOrWhiteSpace(apiToken))
            {
                return new IntegrationSyncResult
                {
                    Success = false,
                    Retryable = false,
                    ErrorCode = "missing_credentials",
                    ErrorMessage = $"{providerKey} API token is missing."
                };
            }

            var baseUrl = _configuration[$"{configurationPrefix}:ApiBaseUrl"]?.TrimEnd('/');
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                return new IntegrationSyncResult
                {
                    Success = false,
                    Retryable = false,
                    ErrorCode = "missing_configuration",
                    ErrorMessage = $"{providerKey} API base URL is not configured."
                };
            }

            var syncPath = _configuration[$"{configurationPrefix}:SyncPath"] ?? "/submissions";
            using var doc = await GetJsonAsync(
                $"{baseUrl}{NormalizeApiPath(syncPath)}",
                authScheme: "Bearer",
                authToken: apiToken,
                additionalHeaders: null,
                cancellationToken: cancellationToken);

            return await UpsertEfilingSubmissionsAsync(
                connectionId,
                providerKey,
                doc,
                cancellationToken);
        }

        private async Task<int> SyncQuickBooksOutboundInvoicesAsync(
            string connectionId,
            string apiBaseUrl,
            string realmId,
            string accessToken,
            CancellationToken cancellationToken)
        {
            var since = DateTime.UtcNow.AddDays(-30);
            var invoices = await _context.Invoices
                .Include(i => i.LineItems)
                .Where(i => i.UpdatedAt >= since && i.Total > 0)
                .OrderBy(i => i.UpdatedAt)
                .Take(100)
                .ToListAsync(cancellationToken);

            if (invoices.Count == 0)
            {
                return 0;
            }

            var localInvoiceIds = invoices.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
            var localClientIds = invoices.Select(i => i.ClientId).Where(id => !string.IsNullOrWhiteSpace(id)).ToHashSet(StringComparer.Ordinal);

            var invoiceLinks = await _context.IntegrationEntityLinks
                .Where(l => l.ConnectionId == connectionId &&
                            l.LocalEntityType == "invoice" &&
                            localInvoiceIds.Contains(l.LocalEntityId))
                .ToDictionaryAsync(l => l.LocalEntityId, StringComparer.Ordinal, cancellationToken);

            var clientLinks = await _context.IntegrationEntityLinks
                .Where(l => l.ConnectionId == connectionId &&
                            l.LocalEntityType == "client" &&
                            localClientIds.Contains(l.LocalEntityId))
                .ToDictionaryAsync(l => l.LocalEntityId, StringComparer.Ordinal, cancellationToken);

            var clients = await _context.Clients
                .Where(c => localClientIds.Contains(c.Id))
                .Select(c => new { c.Id, c.Name, c.Email })
                .ToDictionaryAsync(c => c.Id, StringComparer.Ordinal, cancellationToken);

            var existingOpenConflicts = await _context.IntegrationConflictQueueItems
                .Where(c => c.ConnectionId == connectionId &&
                            c.ProviderKey == IntegrationProviderKeys.QuickBooksOnline &&
                            (c.Status == IntegrationConflictStatuses.Open || c.Status == IntegrationConflictStatuses.InReview))
                .ToDictionaryAsync(c => c.Fingerprint ?? c.Id, StringComparer.OrdinalIgnoreCase, cancellationToken);

            var synced = 0;
            var billingLockRanges = await LoadBillingLockRangesAsync(cancellationToken);
            foreach (var invoice in invoices)
            {
                if (IsBillingPeriodLocked(invoice.IssueDate, billingLockRanges))
                {
                    continue;
                }

                var splitPolicy = await ResolveAccountingSplitReceivablePolicyAsync(
                    connectionId,
                    IntegrationProviderKeys.QuickBooksOnline,
                    invoice,
                    existingOpenConflicts,
                    cancellationToken);
                if (splitPolicy.BlockOutbound)
                {
                    continue;
                }

                var billingClientId = splitPolicy.BillToClientId ?? invoice.ClientId;
                if (string.IsNullOrWhiteSpace(billingClientId))
                {
                    continue;
                }

                if (!clients.TryGetValue(billingClientId, out var billingClient))
                {
                    var payorClient = await _context.Clients
                        .Where(c => c.Id == billingClientId)
                        .Select(c => new { c.Id, c.Name, c.Email })
                        .FirstOrDefaultAsync(cancellationToken);
                    if (payorClient == null)
                    {
                        continue;
                    }

                    clients[billingClientId] = payorClient;
                    billingClient = payorClient;
                }
                var customerId = await EnsureQuickBooksCustomerRefAsync(
                    connectionId,
                    apiBaseUrl,
                    realmId,
                    accessToken,
                    billingClient.Id,
                    billingClient.Name,
                    billingClient.Email,
                    clientLinks,
                    cancellationToken);

                if (string.IsNullOrWhiteSpace(customerId))
                {
                    continue;
                }

                if (invoice.Status == InvoiceStatus.Cancelled)
                {
                    if (!invoiceLinks.TryGetValue(invoice.Id, out var cancelledLink) || string.IsNullOrWhiteSpace(cancelledLink.ExternalEntityId))
                    {
                        continue;
                    }

                    using var voidDoc = await PostQuickBooksEntityAsync(
                        apiBaseUrl,
                        realmId,
                        "invoice",
                        "void",
                        new
                        {
                            Id = cancelledLink.ExternalEntityId,
                            SyncToken = string.IsNullOrWhiteSpace(cancelledLink.ExternalVersion) ? "0" : cancelledLink.ExternalVersion
                        },
                        accessToken,
                        cancellationToken);

                    var voidNode = voidDoc.RootElement.TryGetProperty("Invoice", out var voidInvoiceElement)
                        ? voidInvoiceElement
                        : voidDoc.RootElement;

                    var voidExternalId = GetString(voidNode, "Id");
                    if (string.IsNullOrWhiteSpace(voidExternalId))
                    {
                        continue;
                    }

                    UpsertIntegrationEntityLink(
                        existingByLocalId: invoiceLinks,
                        connectionId: connectionId,
                        providerKey: IntegrationProviderKeys.QuickBooksOnline,
                        localEntityType: "invoice",
                        localEntityId: invoice.Id,
                        externalEntityType: "invoice",
                        externalEntityId: voidExternalId,
                        externalVersion: GetString(voidNode, "SyncToken"),
                        direction: "outbound",
                        metadataJson: voidNode.GetRawText());
                    synced++;
                    continue;
                }

                var lineItems = invoice.LineItems.Count == 0
                    ? new[]
                    {
                        new
                        {
                            Amount = NormalizeConnectorMoney(invoice.Total),
                            DetailType = "SalesItemLineDetail",
                            Description = string.IsNullOrWhiteSpace(invoice.Notes) ? $"Invoice {invoice.Number ?? invoice.Id}" : invoice.Notes,
                            SalesItemLineDetail = new { Qty = 1m, UnitPrice = NormalizeConnectorMoney(invoice.Total) }
                        }
                    }
                    : invoice.LineItems.Select(li => new
                    {
                        Amount = NormalizeConnectorMoney(li.Amount),
                        DetailType = "SalesItemLineDetail",
                        Description = string.IsNullOrWhiteSpace(li.Description) ? "Legal services" : li.Description,
                        SalesItemLineDetail = new
                        {
                            Qty = li.Quantity <= 0 ? 1 : NormalizeConnectorMoney(li.Quantity),
                            UnitPrice = NormalizeConnectorMoney(li.Rate)
                        }
                    }).ToArray();

                object payload;
                if (invoiceLinks.TryGetValue(invoice.Id, out var link) && !string.IsNullOrWhiteSpace(link.ExternalEntityId))
                {
                    payload = new
                    {
                        Id = link.ExternalEntityId,
                        SyncToken = string.IsNullOrWhiteSpace(link.ExternalVersion) ? "0" : link.ExternalVersion,
                        sparse = true,
                        DocNumber = invoice.Number,
                        TxnDate = invoice.IssueDate.ToString("yyyy-MM-dd"),
                        DueDate = invoice.DueDate?.ToString("yyyy-MM-dd"),
                        CustomerRef = new { value = customerId },
                        Line = lineItems
                    };
                }
                else
                {
                    payload = new
                    {
                        DocNumber = invoice.Number,
                        TxnDate = invoice.IssueDate.ToString("yyyy-MM-dd"),
                        DueDate = invoice.DueDate?.ToString("yyyy-MM-dd"),
                        CustomerMemo = new { value = invoice.Notes },
                        CustomerRef = new { value = customerId },
                        Line = lineItems
                    };
                }

                var operation = invoiceLinks.ContainsKey(invoice.Id) ? "update" : null;
                using var responseDoc = await PostQuickBooksEntityAsync(
                    apiBaseUrl,
                    realmId,
                    "invoice",
                    operation,
                    payload,
                    accessToken,
                    cancellationToken);

                var invoiceNode = responseDoc.RootElement.TryGetProperty("Invoice", out var invoiceElement)
                    ? invoiceElement
                    : responseDoc.RootElement;

                var externalId = GetString(invoiceNode, "Id");
                if (string.IsNullOrWhiteSpace(externalId))
                {
                    continue;
                }

                var externalVersion = GetString(invoiceNode, "SyncToken");
                UpsertIntegrationEntityLink(
                    existingByLocalId: invoiceLinks,
                    connectionId: connectionId,
                    providerKey: IntegrationProviderKeys.QuickBooksOnline,
                    localEntityType: "invoice",
                    localEntityId: invoice.Id,
                    externalEntityType: "invoice",
                    externalEntityId: externalId,
                    externalVersion: externalVersion,
                    direction: "outbound",
                    metadataJson: invoiceNode.GetRawText());
                synced++;
            }

            if (synced > 0)
            {
                await _context.SaveChangesAsync(cancellationToken);
            }

            return synced;
        }

        private async Task<string?> EnsureQuickBooksCustomerRefAsync(
            string connectionId,
            string apiBaseUrl,
            string realmId,
            string accessToken,
            string clientId,
            string clientName,
            string? clientEmail,
            Dictionary<string, IntegrationEntityLink> clientLinks,
            CancellationToken cancellationToken)
        {
            if (clientLinks.TryGetValue(clientId, out var existing) && !string.IsNullOrWhiteSpace(existing.ExternalEntityId))
            {
                return existing.ExternalEntityId;
            }

            string? escapedDisplayName = null;
            if (!string.IsNullOrWhiteSpace(clientName))
            {
                escapedDisplayName = clientName.Replace("'", "''", StringComparison.Ordinal);
            }

            JsonDocument? customerDoc = null;
            if (!string.IsNullOrWhiteSpace(escapedDisplayName))
            {
                var query = Uri.EscapeDataString($"SELECT Id, SyncToken FROM Customer WHERE DisplayName = '{escapedDisplayName}' MAXRESULTS 1");
                customerDoc = await GetJsonAsync(
                    $"{apiBaseUrl}/v3/company/{Uri.EscapeDataString(realmId)}/query?query={query}",
                    authScheme: "Bearer",
                    authToken: accessToken,
                    additionalHeaders: new Dictionary<string, string> { ["Accept"] = "application/json" },
                    cancellationToken: cancellationToken);
            }

            string? customerId = null;
            string? syncToken = null;
            string? customerMetadataJson = null;
            if (customerDoc != null &&
                customerDoc.RootElement.TryGetProperty("QueryResponse", out var queryResponse) &&
                queryResponse.TryGetProperty("Customer", out var customerArray) &&
                customerArray.ValueKind == JsonValueKind.Array &&
                customerArray.GetArrayLength() > 0)
            {
                var first = customerArray.EnumerateArray().First();
                customerId = GetString(first, "Id");
                syncToken = GetString(first, "SyncToken");
                customerMetadataJson = first.GetRawText();
            }

            if (string.IsNullOrWhiteSpace(customerId))
            {
                var payload = new
                {
                    DisplayName = string.IsNullOrWhiteSpace(clientName) ? $"Client-{clientId}" : clientName,
                    PrimaryEmailAddr = string.IsNullOrWhiteSpace(clientEmail) ? null : new { Address = clientEmail }
                };

                using var createdDoc = await PostQuickBooksEntityAsync(
                    apiBaseUrl,
                    realmId,
                    "customer",
                    operation: null,
                    payload,
                    accessToken,
                    cancellationToken);

                var customerNode = createdDoc.RootElement.TryGetProperty("Customer", out var customerElement)
                    ? customerElement
                    : createdDoc.RootElement;
                customerId = GetString(customerNode, "Id");
                syncToken = GetString(customerNode, "SyncToken");
                customerMetadataJson = customerNode.GetRawText();
            }

            if (string.IsNullOrWhiteSpace(customerId))
            {
                return null;
            }

            UpsertIntegrationEntityLink(
                existingByLocalId: clientLinks,
                connectionId: connectionId,
                providerKey: IntegrationProviderKeys.QuickBooksOnline,
                localEntityType: "client",
                localEntityId: clientId,
                externalEntityType: "customer",
                externalEntityId: customerId,
                externalVersion: syncToken,
                direction: "outbound",
                metadataJson: customerMetadataJson);

            return customerId;
        }

        private async Task<int> SyncQuickBooksOutboundPaymentsAsync(
            string connectionId,
            string apiBaseUrl,
            string realmId,
            string accessToken,
            CancellationToken cancellationToken)
        {
            var since = DateTime.UtcNow.AddDays(-30);
            var payments = await _context.PaymentTransactions
                .Where(p =>
                    p.Status == "Succeeded" &&
                    p.InvoiceId != null &&
                    (p.Source == null || p.Source != "QuickBooksSync") &&
                    p.UpdatedAt >= since)
                .OrderBy(p => p.UpdatedAt)
                .Take(100)
                .ToListAsync(cancellationToken);

            if (payments.Count == 0)
            {
                return 0;
            }

            var localPaymentIds = payments.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
            var localInvoiceIds = payments.Where(p => p.InvoiceId != null).Select(p => p.InvoiceId!).ToHashSet(StringComparer.Ordinal);
            var localClientIds = payments
                .SelectMany(p => new[] { p.ClientId, p.PayorClientId })
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id!)
                .ToHashSet(StringComparer.Ordinal);

            var paymentLinks = await _context.IntegrationEntityLinks
                .Where(l => l.ConnectionId == connectionId &&
                            l.LocalEntityType == "payment" &&
                            localPaymentIds.Contains(l.LocalEntityId))
                .ToDictionaryAsync(l => l.LocalEntityId, StringComparer.Ordinal, cancellationToken);

            var invoiceLinks = await _context.IntegrationEntityLinks
                .Where(l => l.ConnectionId == connectionId &&
                            l.LocalEntityType == "invoice" &&
                            localInvoiceIds.Contains(l.LocalEntityId))
                .ToDictionaryAsync(l => l.LocalEntityId, StringComparer.Ordinal, cancellationToken);

            var clientLinks = await _context.IntegrationEntityLinks
                .Where(l => l.ConnectionId == connectionId &&
                            l.LocalEntityType == "client" &&
                            localClientIds.Contains(l.LocalEntityId))
                .ToDictionaryAsync(l => l.LocalEntityId, StringComparer.Ordinal, cancellationToken);

            var localInvoices = await _context.Invoices
                .Where(i => localInvoiceIds.Contains(i.Id))
                .Select(i => new { i.Id, i.Number, i.ClientId, i.MatterId })
                .ToDictionaryAsync(i => i.Id, StringComparer.Ordinal, cancellationToken);

            var existingOpenConflicts = await _context.IntegrationConflictQueueItems
                .Where(c => c.ConnectionId == connectionId &&
                            c.ProviderKey == IntegrationProviderKeys.QuickBooksOnline &&
                            (c.Status == IntegrationConflictStatuses.Open || c.Status == IntegrationConflictStatuses.InReview))
                .ToDictionaryAsync(c => c.Fingerprint ?? c.Id, StringComparer.OrdinalIgnoreCase, cancellationToken);

            var synced = 0;
            var billingLockRanges = await LoadBillingLockRangesAsync(cancellationToken);
            foreach (var payment in payments)
            {
                if (IsProviderAuthoredAccountingPayment(payment, IntegrationProviderKeys.QuickBooksOnline))
                {
                    continue;
                }

                if (IsBillingPeriodLocked(payment.ProcessedAt ?? payment.CreatedAt, billingLockRanges))
                {
                    continue;
                }

                if (paymentLinks.ContainsKey(payment.Id))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(payment.InvoiceId) || !invoiceLinks.TryGetValue(payment.InvoiceId, out var invoiceLink))
                {
                    continue;
                }

                if (!localInvoices.TryGetValue(payment.InvoiceId, out var localInvoice))
                {
                    continue;
                }

                var splitPolicy = await ResolveAccountingSplitReceivablePolicyAsync(
                    connectionId,
                    IntegrationProviderKeys.QuickBooksOnline,
                    new Invoice
                    {
                        Id = localInvoice.Id,
                        Number = localInvoice.Number,
                        ClientId = localInvoice.ClientId,
                        MatterId = localInvoice.MatterId
                    },
                    existingOpenConflicts,
                    cancellationToken);
                if (splitPolicy.BlockOutbound)
                {
                    continue;
                }

                var payorTargetClientId = payment.PayorClientId ?? splitPolicy.BillToClientId ?? payment.ClientId ?? localInvoice.ClientId;
                if (string.IsNullOrWhiteSpace(payorTargetClientId))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(payment.PayorClientId) && splitPolicy.InvoicePayorAllocationId != null)
                {
                    QueueOrRefreshConflict(
                        existingOpenConflicts,
                        connectionId,
                        IntegrationProviderKeys.QuickBooksOnline,
                        runId: null,
                        entityType: "payment",
                        localEntityId: payment.Id,
                        externalEntityId: null,
                        conflictType: "payment_payor_target_required",
                        severity: "high",
                        summary: $"Payment {payment.Id} belongs to a split-billed invoice and requires explicit payor targeting before QBO push.",
                        localSnapshotJson: JsonSerializer.Serialize(new { payment.Id, payment.InvoiceId, payment.ClientId, payment.PayorClientId, splitPolicy.Mode }),
                        externalSnapshotJson: null,
                        suggestedResolutionJson: JsonSerializer.Serialize(new { action = "set_payment_payor_target", paymentId = payment.Id, providerKey = IntegrationProviderKeys.QuickBooksOnline }),
                        sourceHint: $"qbo_payment_payor_target_required:{payment.Id}");
                    continue;
                }

                if (!clientLinks.TryGetValue(payorTargetClientId, out var clientLink))
                {
                    var payorClientRef = await _context.Clients
                        .Where(c => c.Id == payorTargetClientId)
                        .Select(c => new { c.Id, c.Name, c.Email })
                        .FirstOrDefaultAsync(cancellationToken);
                    if (payorClientRef == null)
                    {
                        continue;
                    }

                    var externalCustomerId = await EnsureQuickBooksCustomerRefAsync(
                        connectionId,
                        apiBaseUrl,
                        realmId,
                        accessToken,
                        payorClientRef.Id,
                        payorClientRef.Name,
                        payorClientRef.Email,
                        clientLinks,
                        cancellationToken);
                    if (string.IsNullOrWhiteSpace(externalCustomerId) || !clientLinks.TryGetValue(payorTargetClientId, out clientLink))
                    {
                        continue;
                    }
                }

                var payload = new
                {
                    CustomerRef = new { value = clientLink.ExternalEntityId },
                    TotalAmt = NormalizeConnectorMoney(payment.Amount),
                    TxnDate = (payment.ProcessedAt ?? payment.CreatedAt).ToString("yyyy-MM-dd"),
                    PrivateNote = $"JurisFlow payment {payment.Id}",
                    Line = new[]
                    {
                        new
                        {
                            Amount = NormalizeConnectorMoney(payment.Amount),
                            LinkedTxn = new[]
                            {
                                new
                                {
                                    TxnId = invoiceLink.ExternalEntityId,
                                    TxnType = "Invoice"
                                }
                            }
                        }
                    }
                };

                using var paymentDoc = await PostQuickBooksEntityAsync(
                    apiBaseUrl,
                    realmId,
                    "payment",
                    operation: null,
                    payload,
                    accessToken,
                    cancellationToken);

                var paymentNode = paymentDoc.RootElement.TryGetProperty("Payment", out var paymentElement)
                    ? paymentElement
                    : paymentDoc.RootElement;
                var externalId = GetString(paymentNode, "Id");
                if (string.IsNullOrWhiteSpace(externalId))
                {
                    continue;
                }

                UpsertIntegrationEntityLink(
                    existingByLocalId: paymentLinks,
                    connectionId: connectionId,
                    providerKey: IntegrationProviderKeys.QuickBooksOnline,
                    localEntityType: "payment",
                    localEntityId: payment.Id,
                    externalEntityType: "payment",
                    externalEntityId: externalId,
                    externalVersion: GetString(paymentNode, "SyncToken"),
                    direction: "outbound",
                    metadataJson: paymentNode.GetRawText());
                synced++;
            }

            if (synced > 0)
            {
                await _context.SaveChangesAsync(cancellationToken);
            }

            return synced;
        }

        private async Task<int> SyncQuickBooksInboundInvoicesAsync(
            string connectionId,
            string apiBaseUrl,
            string realmId,
            string accessToken,
            CancellationToken cancellationToken)
        {
            var query = Uri.EscapeDataString("SELECT Id, SyncToken, DocNumber, TxnDate, DueDate, TotalAmt, Balance, CustomerRef, PrivateNote FROM Invoice MAXRESULTS 100");
            using var doc = await GetJsonAsync(
                $"{apiBaseUrl}/v3/company/{Uri.EscapeDataString(realmId)}/query?query={query}",
                authScheme: "Bearer",
                authToken: accessToken,
                additionalHeaders: new Dictionary<string, string> { ["Accept"] = "application/json" },
                cancellationToken: cancellationToken);

            if (!doc.RootElement.TryGetProperty("QueryResponse", out var response) ||
                !response.TryGetProperty("Invoice", out var invoiceArray) ||
                invoiceArray.ValueKind != JsonValueKind.Array)
            {
                return 0;
            }

            var remoteInvoices = invoiceArray.EnumerateArray().ToList();
            if (remoteInvoices.Count == 0)
            {
                return 0;
            }

            var providerKey = IntegrationProviderKeys.QuickBooksOnline;
            var externalIds = remoteInvoices
                .Select(i => GetString(i, "Id"))
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Cast<string>()
                .ToHashSet(StringComparer.Ordinal);

            var existingByExternalId = await _context.IntegrationEntityLinks
                .Where(l =>
                    l.ConnectionId == connectionId &&
                    l.ProviderKey == providerKey &&
                    l.ExternalEntityType == "invoice" &&
                    externalIds.Contains(l.ExternalEntityId))
                .ToDictionaryAsync(l => l.ExternalEntityId, StringComparer.Ordinal, cancellationToken);

            var candidateNumbers = remoteInvoices
                .Select(i => GetString(i, "DocNumber")?.Trim())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var localInvoiceCandidates = candidateNumbers.Length == 0
                ? new List<Invoice>()
                : await _context.Invoices
                    .Where(i => i.Number != null && candidateNumbers.Contains(i.Number))
                    .ToListAsync(cancellationToken);

            var localInvoiceByNumber = new Dictionary<string, Invoice>(StringComparer.OrdinalIgnoreCase);
            var ambiguousNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in localInvoiceCandidates
                         .Where(i => !string.IsNullOrWhiteSpace(i.Number))
                         .GroupBy(i => i.Number!, StringComparer.OrdinalIgnoreCase))
            {
                var items = group.OrderByDescending(i => i.UpdatedAt).ToList();
                if (items.Count == 1)
                {
                    localInvoiceByNumber[group.Key] = items[0];
                }
                else
                {
                    ambiguousNumbers.Add(group.Key);
                }
            }

            var localIdsFromExternalLinks = existingByExternalId.Values
                .Select(l => l.LocalEntityId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet(StringComparer.Ordinal);
            var localIdsFromNumberMatches = localInvoiceByNumber.Values
                .Select(i => i.Id)
                .ToHashSet(StringComparer.Ordinal);
            localIdsFromExternalLinks.UnionWith(localIdsFromNumberMatches);

            var localInvoicesById = localIdsFromExternalLinks.Count == 0
                ? new Dictionary<string, Invoice>(StringComparer.Ordinal)
                : await _context.Invoices
                    .Where(i => localIdsFromExternalLinks.Contains(i.Id))
                    .ToDictionaryAsync(i => i.Id, StringComparer.Ordinal, cancellationToken);

            var invoiceLinksByLocalId = localIdsFromExternalLinks.Count == 0
                ? new Dictionary<string, IntegrationEntityLink>(StringComparer.Ordinal)
                : await _context.IntegrationEntityLinks
                    .Where(l =>
                        l.ConnectionId == connectionId &&
                        l.ProviderKey == providerKey &&
                        l.LocalEntityType == "invoice" &&
                        localIdsFromExternalLinks.Contains(l.LocalEntityId))
                    .ToDictionaryAsync(l => l.LocalEntityId, StringComparer.Ordinal, cancellationToken);

            var localClientIds = localInvoicesById.Values
                .Select(i => i.ClientId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            var clientLinksByLocalId = localClientIds.Length == 0
                ? new Dictionary<string, IntegrationEntityLink>(StringComparer.Ordinal)
                : await _context.IntegrationEntityLinks
                    .Where(l =>
                        l.ConnectionId == connectionId &&
                        l.ProviderKey == providerKey &&
                        l.LocalEntityType == "client" &&
                        localClientIds.Contains(l.LocalEntityId))
                    .ToDictionaryAsync(l => l.LocalEntityId, StringComparer.Ordinal, cancellationToken);

            var customerExternalIds = remoteInvoices
                .Select(r => GetNestedString(r, "CustomerRef", "value"))
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Cast<string>()
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            var clientLinksByExternalCustomerId = customerExternalIds.Length == 0
                ? new Dictionary<string, IntegrationEntityLink>(StringComparer.Ordinal)
                : await _context.IntegrationEntityLinks
                    .Where(l =>
                        l.ConnectionId == connectionId &&
                        l.ProviderKey == providerKey &&
                        l.LocalEntityType == "client" &&
                        l.ExternalEntityType == "customer" &&
                        customerExternalIds.Contains(l.ExternalEntityId))
                    .ToDictionaryAsync(l => l.ExternalEntityId, StringComparer.Ordinal, cancellationToken);

            var existingOpen = await _context.IntegrationConflictQueueItems
                .Where(c =>
                    c.ConnectionId == connectionId &&
                    c.ProviderKey == providerKey &&
                    (c.Status == IntegrationConflictStatuses.Open || c.Status == IntegrationConflictStatuses.InReview))
                .ToDictionaryAsync(c => c.Fingerprint ?? c.Id, StringComparer.OrdinalIgnoreCase, cancellationToken);

            var touchedLinks = 0;
            var queuedConflicts = 0;
            foreach (var remote in remoteInvoices)
            {
                var externalId = GetString(remote, "Id");
                if (string.IsNullOrWhiteSpace(externalId))
                {
                    continue;
                }

                var docNumber = GetString(remote, "DocNumber")?.Trim();
                if (!string.IsNullOrWhiteSpace(docNumber) && ambiguousNumbers.Contains(docNumber))
                {
                    queuedConflicts += QueueOrRefreshConflict(
                        existingOpen,
                        connectionId,
                        providerKey,
                        runId: null,
                        entityType: "invoice",
                        localEntityId: null,
                        externalEntityId: externalId,
                        conflictType: "duplicate_local_candidates",
                        severity: "high",
                        summary: $"QuickBooks invoice {docNumber} matches multiple local invoices; manual mapping is required.",
                        localSnapshotJson: JsonSerializer.Serialize(new { docNumber, candidateCount = localInvoiceCandidates.Count(i => string.Equals(i.Number, docNumber, StringComparison.OrdinalIgnoreCase)) }),
                        externalSnapshotJson: remote.GetRawText(),
                        suggestedResolutionJson: JsonSerializer.Serialize(new { action = "reconcile", entityType = "invoice", externalEntityId = externalId, reason = "duplicate_doc_number" }),
                        sourceHint: "qbo_invoice_duplicate_local_candidates");
                    continue;
                }

                Invoice? localInvoice = null;
                if (existingByExternalId.TryGetValue(externalId, out var existingExternalLink))
                {
                    localInvoicesById.TryGetValue(existingExternalLink.LocalEntityId, out localInvoice);
                }

                if (localInvoice == null &&
                    !string.IsNullOrWhiteSpace(docNumber) &&
                    localInvoiceByNumber.TryGetValue(docNumber, out var matchedByNumber))
                {
                    localInvoice = matchedByNumber;
                }

                if (localInvoice == null)
                {
                    queuedConflicts += QueueOrRefreshConflict(
                        existingOpen,
                        connectionId,
                        providerKey,
                        runId: null,
                        entityType: "invoice",
                        localEntityId: null,
                        externalEntityId: externalId,
                        conflictType: "missing_local_record",
                        severity: "medium",
                        summary: $"QuickBooks invoice {(docNumber ?? externalId)} is not mapped to a local invoice.",
                        localSnapshotJson: null,
                        externalSnapshotJson: remote.GetRawText(),
                        suggestedResolutionJson: JsonSerializer.Serialize(new { action = "reconcile", entityType = "invoice", externalEntityId = externalId, docNumber }),
                        sourceHint: "qbo_invoice_missing_local_record");
                    continue;
                }

                UpsertIntegrationEntityLink(
                    existingByLocalId: invoiceLinksByLocalId,
                    connectionId: connectionId,
                    providerKey: providerKey,
                    localEntityType: "invoice",
                    localEntityId: localInvoice.Id,
                    externalEntityType: "invoice",
                    externalEntityId: externalId,
                    externalVersion: GetString(remote, "SyncToken"),
                    direction: "inbound",
                    metadataJson: remote.GetRawText());

                var remoteCustomerId = GetNestedString(remote, "CustomerRef", "value");
                if (!string.IsNullOrWhiteSpace(localInvoice.ClientId) && !string.IsNullOrWhiteSpace(remoteCustomerId))
                {
                    if (clientLinksByLocalId.TryGetValue(localInvoice.ClientId, out var existingClientLink) &&
                        !string.IsNullOrWhiteSpace(existingClientLink.ExternalEntityId) &&
                        !string.Equals(existingClientLink.ExternalEntityId, remoteCustomerId, StringComparison.Ordinal))
                    {
                        queuedConflicts += QueueOrRefreshConflict(
                            existingOpen,
                            connectionId,
                            providerKey,
                            runId: null,
                            entityType: "client",
                            localEntityId: localInvoice.ClientId,
                            externalEntityId: remoteCustomerId,
                            conflictType: "customer_link_mismatch",
                            severity: "high",
                            summary: $"QuickBooks invoice customer reference for invoice {(docNumber ?? localInvoice.Number ?? localInvoice.Id)} does not match the linked local client mapping.",
                            localSnapshotJson: JsonSerializer.Serialize(new
                            {
                                clientId = localInvoice.ClientId,
                                existingCustomerExternalId = existingClientLink.ExternalEntityId,
                                invoiceId = localInvoice.Id,
                                invoiceNumber = localInvoice.Number
                            }),
                            externalSnapshotJson: JsonSerializer.Serialize(new
                            {
                                remoteInvoiceId = externalId,
                                remoteCustomerId,
                                docNumber
                            }),
                            suggestedResolutionJson: JsonSerializer.Serialize(new { action = "reconcile", entityType = "client", localEntityId = localInvoice.ClientId, externalEntityId = remoteCustomerId }),
                            sourceHint: "qbo_customer_link_mismatch");
                    }
                    else if (clientLinksByExternalCustomerId.TryGetValue(remoteCustomerId, out var existingCustomerLink) &&
                             !string.Equals(existingCustomerLink.LocalEntityId, localInvoice.ClientId, StringComparison.Ordinal))
                    {
                        queuedConflicts += QueueOrRefreshConflict(
                            existingOpen,
                            connectionId,
                            providerKey,
                            runId: null,
                            entityType: "client",
                            localEntityId: localInvoice.ClientId,
                            externalEntityId: remoteCustomerId,
                            conflictType: "customer_dedupe_collision",
                            severity: "high",
                            summary: $"QuickBooks customer {remoteCustomerId} is already linked to another local client; manual dedupe/mapping review required.",
                            localSnapshotJson: JsonSerializer.Serialize(new
                            {
                                targetClientId = localInvoice.ClientId,
                                existingLinkedClientId = existingCustomerLink.LocalEntityId,
                                invoiceId = localInvoice.Id,
                                invoiceNumber = localInvoice.Number
                            }),
                            externalSnapshotJson: JsonSerializer.Serialize(new
                            {
                                remoteInvoiceId = externalId,
                                remoteCustomerId,
                                docNumber
                            }),
                            suggestedResolutionJson: JsonSerializer.Serialize(new { action = "reconcile", entityType = "client", externalEntityId = remoteCustomerId, reason = "dedupe_collision" }),
                            sourceHint: "qbo_customer_dedupe_collision");
                    }
                    else
                    {
                        UpsertIntegrationEntityLink(
                            existingByLocalId: clientLinksByLocalId,
                            connectionId: connectionId,
                            providerKey: providerKey,
                            localEntityType: "client",
                            localEntityId: localInvoice.ClientId,
                            externalEntityType: "customer",
                            externalEntityId: remoteCustomerId,
                            externalVersion: null,
                            direction: "inbound",
                            metadataJson: JsonSerializer.Serialize(new
                            {
                                source = "qbo_invoice_customer_ref",
                                invoiceExternalId = externalId,
                                invoiceDocNumber = docNumber,
                                customerExternalId = remoteCustomerId
                            }));
                        clientLinksByExternalCustomerId[remoteCustomerId] = clientLinksByLocalId[localInvoice.ClientId];
                        touchedLinks++;
                    }
                }

                // Source-of-truth for invoice core fields remains JurisFlow.
                // Pull refreshes remote snapshot/link metadata so reconcile can detect mismatches deterministically.
                touchedLinks++;
            }

            if (touchedLinks > 0 || queuedConflicts > 0)
            {
                await _context.SaveChangesAsync(cancellationToken);
            }

            return touchedLinks;
        }

        private async Task<int> SyncQuickBooksInboundPaymentsAsync(
            string connectionId,
            string apiBaseUrl,
            string realmId,
            string accessToken,
            CancellationToken cancellationToken)
        {
            var query = Uri.EscapeDataString("SELECT Id, SyncToken, TxnDate, TotalAmt, Line FROM Payment MAXRESULTS 100");
            using var doc = await GetJsonAsync(
                $"{apiBaseUrl}/v3/company/{Uri.EscapeDataString(realmId)}/query?query={query}",
                authScheme: "Bearer",
                authToken: accessToken,
                additionalHeaders: new Dictionary<string, string> { ["Accept"] = "application/json" },
                cancellationToken: cancellationToken);

            if (!doc.RootElement.TryGetProperty("QueryResponse", out var response) ||
                !response.TryGetProperty("Payment", out var paymentArray) ||
                paymentArray.ValueKind != JsonValueKind.Array)
            {
                return 0;
            }

            var remotePayments = paymentArray.EnumerateArray().ToList();
            if (remotePayments.Count == 0)
            {
                return 0;
            }

            var externalIds = remotePayments
                .Select(p => GetString(p, "Id"))
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Cast<string>()
                .ToHashSet(StringComparer.Ordinal);

            var existingExternalLinks = await _context.IntegrationEntityLinks
                .Where(l => l.ConnectionId == connectionId &&
                            l.ExternalEntityType == "payment" &&
                            externalIds.Contains(l.ExternalEntityId))
                .ToDictionaryAsync(l => l.ExternalEntityId, StringComparer.Ordinal, cancellationToken);

            var invoiceLinks = await _context.IntegrationEntityLinks
                .Where(l => l.ConnectionId == connectionId && l.ExternalEntityType == "invoice")
                .ToDictionaryAsync(l => l.ExternalEntityId, StringComparer.Ordinal, cancellationToken);

            var linkedLocalInvoiceIds = invoiceLinks.Values
                .Select(v => v.LocalEntityId)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var localInvoicesById = await _context.Invoices
                .Where(i => linkedLocalInvoiceIds.Contains(i.Id))
                .ToDictionaryAsync(i => i.Id, StringComparer.Ordinal, cancellationToken);

            var inserted = 0;
            foreach (var remote in remotePayments)
            {
                var externalPaymentId = GetString(remote, "Id");
                if (string.IsNullOrWhiteSpace(externalPaymentId) || existingExternalLinks.ContainsKey(externalPaymentId))
                {
                    continue;
                }

                var linkedInvoiceExternalId = TryExtractQuickBooksLinkedInvoiceId(remote);
                if (string.IsNullOrWhiteSpace(linkedInvoiceExternalId) || !invoiceLinks.TryGetValue(linkedInvoiceExternalId, out var invoiceLink))
                {
                    continue;
                }

                if (!localInvoicesById.TryGetValue(invoiceLink.LocalEntityId, out var invoice))
                {
                    continue;
                }

                var amount = NormalizeConnectorMoney(ParseProviderDecimal(GetString(remote, "TotalAmt")) ?? 0m);
                if (amount <= 0)
                {
                    continue;
                }

                var transaction = new PaymentTransaction
                {
                    Id = Guid.NewGuid().ToString(),
                    InvoiceId = invoice.Id,
                    MatterId = invoice.MatterId,
                    ClientId = invoice.ClientId,
                    Amount = amount,
                    Currency = "USD",
                    PaymentMethod = "QuickBooks",
                    ExternalTransactionId = externalPaymentId,
                    Status = "Succeeded",
                    Source = "QuickBooksSync",
                    ProcessedAt = ParseProviderDateTime(GetString(remote, "TxnDate")) ?? DateTime.UtcNow,
                    InvoiceAppliedAmount = amount,
                    InvoiceAppliedAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _context.PaymentTransactions.Add(transaction);

                ApplyInvoicePayment(invoice, amount);

                var link = new IntegrationEntityLink
                {
                    Id = Guid.NewGuid().ToString(),
                    ConnectionId = connectionId,
                    ProviderKey = IntegrationProviderKeys.QuickBooksOnline,
                    LocalEntityType = "payment",
                    LocalEntityId = transaction.Id,
                    ExternalEntityType = "payment",
                    ExternalEntityId = externalPaymentId,
                    ExternalVersion = GetString(remote, "SyncToken"),
                    LastDirection = "inbound",
                    IdempotencyKey = BuildExternalVersionIdempotencyKey(
                        IntegrationProviderKeys.QuickBooksOnline,
                        "payment",
                        externalPaymentId,
                        GetString(remote, "SyncToken")),
                    LastSyncedAt = DateTime.UtcNow
                };
                _context.IntegrationEntityLinks.Add(link);
                existingExternalLinks[externalPaymentId] = link;

                inserted++;
            }

            if (inserted > 0)
            {
                await _context.SaveChangesAsync(cancellationToken);
            }

            return inserted;
        }

        private async Task<(int created, int updated, int relinked, int attachmentsImported, int attachmentsDeduped, int reviewSignalsQueued)> UpsertMatterLinkedEmailsAsync(
            string connectionId,
            string provider,
            string providerKey,
            IReadOnlyCollection<ProviderEmailEnvelope> envelopes,
            CancellationToken cancellationToken)
        {
            if (envelopes.Count == 0)
            {
                return (0, 0, 0, 0, 0, 0);
            }

            var externalIds = envelopes
                .Select(m => m.ExternalId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            var existing = externalIds.Length == 0
                ? new Dictionary<string, EmailMessage>(StringComparer.Ordinal)
                : await _context.EmailMessages
                    .Where(e =>
                        e.EmailAccountId == connectionId &&
                        e.Provider == provider &&
                        e.ExternalId != null &&
                        externalIds.Contains(e.ExternalId))
                    .ToDictionaryAsync(e => e.ExternalId!, StringComparer.Ordinal, cancellationToken);

            var clients = await _context.Clients
                .Select(c => new ClientLookupItem
                {
                    Id = c.Id,
                    Email = c.Email
                })
                .Where(c => !string.IsNullOrWhiteSpace(c.Email))
                .ToListAsync(cancellationToken);

            var matters = await _context.Matters
                .Select(m => new MatterLookupItem
                {
                    Id = m.Id,
                    ClientId = m.ClientId,
                    CaseNumber = m.CaseNumber,
                    Name = m.Name
                })
                .ToListAsync(cancellationToken);

            var now = DateTime.UtcNow;
            var created = 0;
            var updated = 0;
            var relinked = 0;
            var trackedMessages = new List<(EmailMessage message, ProviderEmailEnvelope envelope)>(envelopes.Count);
            foreach (var envelope in envelopes)
            {
                if (string.IsNullOrWhiteSpace(envelope.ExternalId))
                {
                    continue;
                }

                var resolvedClientId = ResolveClientIdFromEnvelope(envelope, clients);
                var resolvedMatterId = ResolveMatterIdFromEnvelope(envelope, resolvedClientId, matters);

                if (existing.TryGetValue(envelope.ExternalId, out var message))
                {
                    message.Subject = envelope.Subject;
                    message.FromAddress = envelope.FromAddress;
                    message.FromName = envelope.FromName;
                    message.ToAddresses = envelope.ToAddresses;
                    message.CcAddresses = envelope.CcAddresses;
                    message.BccAddresses = envelope.BccAddresses;
                    message.BodyText = envelope.BodyText;
                    message.BodyHtml = envelope.BodyHtml;
                    message.Folder = envelope.Folder;
                    message.IsRead = envelope.IsRead;
                    message.HasAttachments = envelope.HasAttachments;
                    message.AttachmentCount = envelope.AttachmentCount;
                    message.Importance = envelope.Importance;
                    message.ReceivedAt = envelope.ReceivedAt;
                    message.SentAt = envelope.SentAt;
                    message.SyncedAt = now;

                    if (string.IsNullOrWhiteSpace(message.ClientId) && !string.IsNullOrWhiteSpace(resolvedClientId))
                    {
                        message.ClientId = resolvedClientId;
                        relinked++;
                    }

                    if (string.IsNullOrWhiteSpace(message.MatterId) && !string.IsNullOrWhiteSpace(resolvedMatterId))
                    {
                        message.MatterId = resolvedMatterId;
                        relinked++;
                    }

                    ApplyEmailWorkflowStateHints(message, envelope);

                    updated++;
                    trackedMessages.Add((message, envelope));
                }
                else
                {
                    var createdMessage = new EmailMessage
                    {
                        Id = Guid.NewGuid().ToString(),
                        ExternalId = envelope.ExternalId,
                        Provider = provider,
                        EmailAccountId = connectionId,
                        MatterId = resolvedMatterId,
                        ClientId = resolvedClientId,
                        Subject = envelope.Subject,
                        FromAddress = envelope.FromAddress,
                        FromName = envelope.FromName,
                        ToAddresses = envelope.ToAddresses,
                        CcAddresses = envelope.CcAddresses,
                        BccAddresses = envelope.BccAddresses,
                        BodyText = envelope.BodyText,
                        BodyHtml = envelope.BodyHtml,
                        Folder = envelope.Folder,
                        IsRead = envelope.IsRead,
                        HasAttachments = envelope.HasAttachments,
                        AttachmentCount = envelope.AttachmentCount,
                        Importance = envelope.Importance,
                        ReceivedAt = envelope.ReceivedAt,
                        SentAt = envelope.SentAt,
                        SyncedAt = now,
                        CreatedAt = now
                    };
                    ApplyEmailWorkflowStateHints(createdMessage, envelope);
                    _context.EmailMessages.Add(createdMessage);
                    created++;
                    trackedMessages.Add((createdMessage, envelope));
                }
            }

            if (created > 0 || updated > 0)
            {
                await _context.SaveChangesAsync(cancellationToken);
            }

            var (attachmentsImported, attachmentsDeduped) = await SyncEmailAttachmentsToDmsAsync(
                connectionId,
                providerKey,
                trackedMessages,
                cancellationToken);

            var reviewSignalsQueued = await QueueEmailSignalReviewsAsync(
                connectionId,
                providerKey,
                trackedMessages,
                cancellationToken);

            return (created, updated, relinked, attachmentsImported, attachmentsDeduped, reviewSignalsQueued);
        }

        private async Task<(int imported, int deduped)> SyncEmailAttachmentsToDmsAsync(
            string connectionId,
            string providerKey,
            IReadOnlyCollection<(EmailMessage message, ProviderEmailEnvelope envelope)> trackedMessages,
            CancellationToken cancellationToken)
        {
            const long maxAttachmentBytes = 15L * 1024L * 1024L;

            var candidates = trackedMessages
                .Where(x => x.envelope.Attachments.Count > 0 && !string.IsNullOrWhiteSpace(x.envelope.ExternalId))
                .ToList();
            if (candidates.Count == 0)
            {
                return (0, 0);
            }

            var externalAttachmentIds = new List<string>();
            foreach (var (message, envelope) in candidates)
            {
                for (var index = 0; index < envelope.Attachments.Count; index++)
                {
                    var attachment = envelope.Attachments[index];
                    if (attachment.IsInline)
                    {
                        continue;
                    }

                    var externalAttachmentId = BuildEmailAttachmentExternalEntityId(envelope, attachment, index);
                    if (!string.IsNullOrWhiteSpace(externalAttachmentId))
                    {
                        externalAttachmentIds.Add(externalAttachmentId);
                    }
                }
            }

            var existingAttachmentLinks = externalAttachmentIds.Count == 0
                ? new Dictionary<string, IntegrationEntityLink>(StringComparer.Ordinal)
                : await _context.IntegrationEntityLinks
                    .Where(l => l.ConnectionId == connectionId &&
                                l.ProviderKey == providerKey &&
                                l.ExternalEntityType == "email_attachment" &&
                                externalAttachmentIds.Contains(l.ExternalEntityId))
                    .ToDictionaryAsync(l => l.ExternalEntityId, StringComparer.Ordinal, cancellationToken);

            var imports = 0;
            var deduped = 0;
            var linksChanged = false;

            foreach (var (message, envelope) in candidates)
            {
                for (var index = 0; index < envelope.Attachments.Count; index++)
                {
                    var attachment = envelope.Attachments[index];
                    if (attachment.IsInline || string.IsNullOrWhiteSpace(attachment.FileName))
                    {
                        continue;
                    }

                    var externalAttachmentId = BuildEmailAttachmentExternalEntityId(envelope, attachment, index);
                    if (existingAttachmentLinks.ContainsKey(externalAttachmentId))
                    {
                        deduped++;
                        continue;
                    }

                    var content = attachment.ContentBytes;
                    if (content == null || content.Length == 0)
                    {
                        continue;
                    }

                    if (content.LongLength > maxAttachmentBytes)
                    {
                        continue;
                    }

                    var sha256 = ComputeSha256Hex(content);
                    var existingVersion = await _context.DocumentVersions
                        .AsNoTracking()
                        .Where(v => v.Sha256 == sha256)
                        .OrderByDescending(v => v.CreatedAt)
                        .Select(v => new { v.DocumentId, v.Id })
                        .FirstOrDefaultAsync(cancellationToken);

                    string documentId;
                    if (existingVersion != null)
                    {
                        documentId = existingVersion.DocumentId;
                        deduped++;
                    }
                    else
                    {
                        var createdDocumentId = await CreateEmailAttachmentDocumentAsync(
                            message,
                            envelope,
                            attachment,
                            providerKey,
                            sha256,
                            cancellationToken);
                        if (string.IsNullOrWhiteSpace(createdDocumentId))
                        {
                            continue;
                        }

                        documentId = createdDocumentId;
                        imports++;
                    }

                    var link = new IntegrationEntityLink
                    {
                        Id = Guid.NewGuid().ToString(),
                        ConnectionId = connectionId,
                        ProviderKey = providerKey,
                        LocalEntityType = "document",
                        LocalEntityId = documentId,
                        ExternalEntityType = "email_attachment",
                        ExternalEntityId = externalAttachmentId,
                        ExternalVersion = sha256,
                        LastDirection = "inbound",
                        IdempotencyKey = BuildExternalVersionIdempotencyKey(providerKey, "email_attachment", externalAttachmentId, sha256),
                        LastSyncedAt = DateTime.UtcNow,
                        MetadataJson = JsonSerializer.Serialize(new
                        {
                            emailMessageId = message.Id,
                            emailExternalId = envelope.ExternalId,
                            attachment.ExternalAttachmentId,
                            attachment.FileName,
                            attachment.MimeType,
                            attachment.SizeBytes,
                            sha256
                        })
                    };

                    _context.IntegrationEntityLinks.Add(link);
                    existingAttachmentLinks[externalAttachmentId] = link;
                    linksChanged = true;
                }
            }

            if (linksChanged)
            {
                await _context.SaveChangesAsync(cancellationToken);
            }

            return (imports, deduped);
        }

        private async Task<string?> CreateEmailAttachmentDocumentAsync(
            EmailMessage message,
            ProviderEmailEnvelope envelope,
            ProviderEmailAttachment attachment,
            string providerKey,
            string sha256,
            CancellationToken cancellationToken)
        {
            if (attachment.ContentBytes == null || attachment.ContentBytes.Length == 0)
            {
                return null;
            }

            var safeFileName = SanitizeFileName(attachment.FileName);
            var uniqueFileName = $"{Guid.NewGuid()}_{safeFileName}";
            var absolutePath = GetTenantRelativeUploadPath(uniqueFileName);
            DocumentEncryptionPayload? encryptionPayload = null;

            if (_documentEncryptionService.Enabled)
            {
                encryptionPayload = _documentEncryptionService.EncryptBytes(attachment.ContentBytes);
                await _fileStorage.SaveBytesAsync(absolutePath, encryptionPayload.Ciphertext, attachment.MimeType, cancellationToken);
            }
            else
            {
                await _fileStorage.SaveBytesAsync(absolutePath, attachment.ContentBytes, attachment.MimeType, cancellationToken);
            }

            var now = DateTime.UtcNow;
            var document = new Document
            {
                Id = Guid.NewGuid().ToString(),
                Name = safeFileName,
                FileName = safeFileName,
                FilePath = GetTenantRelativeUploadPath(uniqueFileName),
                FileSize = attachment.ContentBytes.LongLength,
                MimeType = attachment.MimeType ?? "application/octet-stream",
                IsEncrypted = encryptionPayload != null,
                EncryptionKeyId = encryptionPayload?.KeyId,
                EncryptionIv = encryptionPayload?.Iv,
                EncryptionTag = encryptionPayload?.Tag,
                EncryptionAlgorithm = encryptionPayload?.Algorithm,
                MatterId = message.MatterId,
                Category = "Email Attachment",
                Description = Truncate(
                    $"{envelope.Subject} | {envelope.FromAddress} | {envelope.ReceivedAt:O}",
                    500),
                Tags = SerializeStringList(new[]
                {
                    "email-attachment",
                    NormalizeProviderTag(providerKey),
                    string.IsNullOrWhiteSpace(message.MatterId) ? "unlinked" : "matter-linked"
                }),
                Status = "Draft",
                CreatedAt = now,
                UpdatedAt = now
            };

            _context.Documents.Add(document);
            await _context.SaveChangesAsync(cancellationToken);

            var version = new DocumentVersion
            {
                Id = Guid.NewGuid().ToString(),
                DocumentId = document.Id,
                FileName = safeFileName,
                FilePath = document.FilePath,
                FileSize = document.FileSize,
                IsEncrypted = document.IsEncrypted,
                EncryptionKeyId = document.EncryptionKeyId,
                EncryptionIv = document.EncryptionIv,
                EncryptionTag = document.EncryptionTag,
                EncryptionAlgorithm = document.EncryptionAlgorithm,
                Sha256 = sha256,
                UploadedByUserId = null,
                CreatedAt = now
            };
            _context.DocumentVersions.Add(version);
            await _context.SaveChangesAsync(cancellationToken);

            try
            {
                await _documentIndexService.UpsertIndexAsync(document, attachment.ContentBytes);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to index email attachment document {DocumentId}", document.Id);
            }

            return document.Id;
        }

        private async Task<int> QueueEmailSignalReviewsAsync(
            string connectionId,
            string providerKey,
            IReadOnlyCollection<(EmailMessage message, ProviderEmailEnvelope envelope)> trackedMessages,
            CancellationToken cancellationToken)
        {
            var sourceIds = trackedMessages
                .Select(t => t.message.Id)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (sourceIds.Count == 0)
            {
                return 0;
            }

            var itemTypes = new[]
            {
                "matter_filing_suggestion",
                "privilege_risk_tagging",
                "deadline_extraction_draft"
            };

            var existing = await _context.IntegrationReviewQueueItems
                .Where(r =>
                    r.ConnectionId == connectionId &&
                    r.ProviderKey == providerKey &&
                    r.SourceType == nameof(EmailMessage) &&
                    r.SourceId != null &&
                    sourceIds.Contains(r.SourceId) &&
                    itemTypes.Contains(r.ItemType) &&
                    (r.Status == IntegrationReviewQueueStatuses.Pending || r.Status == IntegrationReviewQueueStatuses.InReview))
                .ToDictionaryAsync(r => BuildEmailReviewKey(r.SourceId!, r.ItemType), StringComparer.Ordinal, cancellationToken);

            var created = 0;
            var changed = false;

            foreach (var (message, envelope) in trackedMessages)
            {
                if (string.IsNullOrWhiteSpace(message.Id))
                {
                    continue;
                }

                var combinedText = BuildEmailSignalText(envelope);
                var attachmentNames = envelope.Attachments
                    .Where(a => !a.IsInline && !string.IsNullOrWhiteSpace(a.FileName))
                    .Select(a => a.FileName)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(5)
                    .ToArray();

                if (attachmentNames.Length > 0 && (string.IsNullOrWhiteSpace(message.MatterId) || LooksLikeFilingEmail(combinedText)))
                {
                    created += QueueOrRefreshEmailReview(
                        existing,
                        connectionId,
                        providerKey,
                        message,
                        "matter_filing_suggestion",
                        priority: string.IsNullOrWhiteSpace(message.MatterId) ? "high" : "medium",
                        title: string.IsNullOrWhiteSpace(message.MatterId)
                            ? "Matter link review needed before filing"
                            : "Matter-linked filing suggestion",
                        summary: string.IsNullOrWhiteSpace(message.MatterId)
                            ? $"Email attachments were ingested but no matter match was resolved for '{message.Subject}'."
                            : $"Email '{message.Subject}' includes attachments suitable for filing/document foldering review.",
                        contextJson: JsonSerializer.Serialize(new
                        {
                            messageId = message.Id,
                            message.ExternalId,
                            message.MatterId,
                            message.ClientId,
                            message.Subject,
                            provider = providerKey,
                            suggestedFolder = SuggestDocumentFolder(combinedText),
                            suggestedDocumentType = SuggestDocumentType(combinedText, attachmentNames),
                            attachments = attachmentNames
                        }),
                        suggestedActionsJson: JsonSerializer.Serialize(new object[]
                        {
                            new { action = "open_email", sourceId = message.Id },
                            new { action = "assign_matter", matterId = message.MatterId },
                            new { action = "apply_document_tags", tags = new[] { "Filed", "Needs Review" } }
                        }));
                    changed = true;
                }

                if (LooksLikePrivilegeOrRiskEmail(combinedText))
                {
                    created += QueueOrRefreshEmailReview(
                        existing,
                        connectionId,
                        providerKey,
                        message,
                        "privilege_risk_tagging",
                        priority: "medium",
                        title: "Privilege / risk tagging review",
                        summary: $"Email '{message.Subject}' includes privilege/risk keywords and should be tagged before broader sync/sharing.",
                        contextJson: JsonSerializer.Serialize(new
                        {
                            messageId = message.Id,
                            message.MatterId,
                            message.ClientId,
                            message.Subject,
                            suggestedTags = new[] { "attorney-client", "work-product", "confidential" },
                            preview = Truncate(combinedText, 600)
                        }),
                        suggestedActionsJson: JsonSerializer.Serialize(new object[]
                        {
                            new { action = "apply_risk_tags" },
                            new { action = "mark_needs_review" }
                        }));
                    changed = true;
                }

                if (LooksLikeDeadlineEmail(combinedText))
                {
                    created += QueueOrRefreshEmailReview(
                        existing,
                        connectionId,
                        providerKey,
                        message,
                        "deadline_extraction_draft",
                        priority: "high",
                        title: "Deadline extraction draft review",
                        summary: $"Email '{message.Subject}' appears to contain a deadline/hearing commitment; review draft docket/calendar extraction.",
                        contextJson: JsonSerializer.Serialize(new
                        {
                            messageId = message.Id,
                            message.MatterId,
                            message.ClientId,
                            message.Subject,
                            provider = providerKey,
                            draftType = "docket_calendar",
                            preview = Truncate(combinedText, 800)
                        }),
                        suggestedActionsJson: JsonSerializer.Serialize(new object[]
                        {
                            new { action = "draft_deadline" },
                            new { action = "draft_calendar_event" }
                        }));
                    changed = true;
                }
            }

            if (changed)
            {
                await _context.SaveChangesAsync(cancellationToken);
            }

            return created;
        }

        private int QueueOrRefreshEmailReview(
            IDictionary<string, IntegrationReviewQueueItem> existing,
            string connectionId,
            string providerKey,
            EmailMessage message,
            string itemType,
            string priority,
            string title,
            string summary,
            string? contextJson,
            string? suggestedActionsJson)
        {
            if (string.IsNullOrWhiteSpace(message.Id))
            {
                return 0;
            }

            var key = BuildEmailReviewKey(message.Id, itemType);
            if (existing.TryGetValue(key, out var item))
            {
                item.Priority = priority;
                item.Title = Truncate(title, 160);
                item.Summary = Truncate(summary, 2048);
                item.ContextJson = contextJson;
                item.SuggestedActionsJson = suggestedActionsJson;
                item.UpdatedAt = DateTime.UtcNow;
                return 0;
            }

            var created = new IntegrationReviewQueueItem
            {
                Id = Guid.NewGuid().ToString(),
                ConnectionId = connectionId,
                ProviderKey = providerKey,
                RunId = null,
                ItemType = itemType,
                SourceType = nameof(EmailMessage),
                SourceId = message.Id,
                ConflictId = null,
                Status = IntegrationReviewQueueStatuses.Pending,
                Priority = priority,
                Title = Truncate(title, 160),
                Summary = Truncate(summary, 2048),
                ContextJson = contextJson,
                SuggestedActionsJson = suggestedActionsJson,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.IntegrationReviewQueueItems.Add(created);
            existing[key] = created;
            return 1;
        }

        private static string BuildEmailReviewKey(string sourceId, string itemType)
        {
            return $"{sourceId}|{itemType}";
        }

        private static string BuildEmailSignalText(ProviderEmailEnvelope envelope)
        {
            var attachmentNames = envelope.Attachments.Count == 0
                ? string.Empty
                : string.Join(" ", envelope.Attachments.Where(a => !string.IsNullOrWhiteSpace(a.FileName)).Select(a => a.FileName));
            return $"{envelope.Subject} {envelope.BodyText} {envelope.BodyHtml} {attachmentNames}";
        }

        private static void ApplyEmailWorkflowStateHints(EmailMessage message, ProviderEmailEnvelope envelope)
        {
            if (message == null)
            {
                return;
            }

            var combinedText = BuildEmailSignalText(envelope);
            var hasRealAttachments = envelope.Attachments.Any(a => !a.IsInline && !string.IsNullOrWhiteSpace(a.FileName));
            if (!hasRealAttachments)
            {
                return;
            }

            var needsReview = string.IsNullOrWhiteSpace(message.MatterId) ||
                              LooksLikePrivilegeOrRiskEmail(combinedText) ||
                              LooksLikeDeadlineEmail(combinedText);

            var filingCandidate = !string.IsNullOrWhiteSpace(message.MatterId) && LooksLikeFilingEmail(combinedText);

            if (filingCandidate)
            {
                // Push layer maps this to archive/folder move depending on provider.
                message.Folder = "Filed";
                message.IsRead = true;
                return;
            }

            if (needsReview)
            {
                // Push layer maps this to Gmail labels or Outlook folder move when configured.
                message.Folder = "Needs Review";
                message.IsRead = false;
            }
        }

        private static bool LooksLikeFilingEmail(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var normalized = text.ToLowerInvariant();
            return normalized.Contains("filed")
                   || normalized.Contains("filing")
                   || normalized.Contains("complaint")
                   || normalized.Contains("motion")
                   || normalized.Contains("petition")
                   || normalized.Contains("serve")
                   || normalized.Contains("service");
        }

        private static bool LooksLikePrivilegeOrRiskEmail(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var normalized = text.ToLowerInvariant();
            return normalized.Contains("attorney-client")
                   || normalized.Contains("work product")
                   || normalized.Contains("privileged")
                   || normalized.Contains("legal advice")
                   || normalized.Contains("confidential");
        }

        private static bool LooksLikeDeadlineEmail(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var normalized = text.ToLowerInvariant();
            if (!(normalized.Contains("deadline") ||
                  normalized.Contains("hearing") ||
                  normalized.Contains("appearance") ||
                  normalized.Contains("response due") ||
                  normalized.Contains("due by") ||
                  normalized.Contains("must file by")))
            {
                return false;
            }

            return Regex.IsMatch(normalized, @"\b(\d{1,2}/\d{1,2}/\d{2,4}|[a-z]{3,9}\s+\d{1,2}(,\s*\d{4})?)\b");
        }

        private static string SuggestDocumentFolder(string text)
        {
            var normalized = (text ?? string.Empty).ToLowerInvariant();
            if (normalized.Contains("invoice") || normalized.Contains("payment"))
            {
                return "Billing";
            }

            if (normalized.Contains("discovery") || normalized.Contains("interrogator") || normalized.Contains("request for production"))
            {
                return "Discovery";
            }

            if (normalized.Contains("motion") || normalized.Contains("complaint") || normalized.Contains("petition") || normalized.Contains("order"))
            {
                return "Pleadings";
            }

            return "Correspondence";
        }

        private static string SuggestDocumentType(string text, IReadOnlyCollection<string> attachmentNames)
        {
            var normalized = (text ?? string.Empty).ToLowerInvariant();
            if (normalized.Contains("invoice") || attachmentNames.Any(a => a.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)))
            {
                return "billing_attachment";
            }

            if (normalized.Contains("motion") || normalized.Contains("complaint") || normalized.Contains("petition"))
            {
                return "pleading";
            }

            if (normalized.Contains("notice") || normalized.Contains("service"))
            {
                return "notice";
            }

            return "email_attachment";
        }

        private ProviderEmailEnvelope? ParseOutlookEnvelope(JsonElement item)
        {
            var externalId = GetString(item, "id");
            if (string.IsNullOrWhiteSpace(externalId))
            {
                return null;
            }

            var fromAddress = GetNestedString(item, "from", "emailAddress", "address") ?? string.Empty;
            var fromName = GetNestedString(item, "from", "emailAddress", "name") ?? string.Empty;
            var toAddresses = JoinRecipientAddresses(item, "toRecipients");
            var ccAddresses = JoinRecipientAddresses(item, "ccRecipients");
            var bccAddresses = JoinRecipientAddresses(item, "bccRecipients");
            var subject = GetString(item, "subject") ?? "(No Subject)";
            var bodyText = GetString(item, "bodyPreview");
            var bodyHtml = GetNestedString(item, "body", "content");

            var importanceRaw = GetString(item, "importance");
            var importance = string.Equals(importanceRaw, "high", StringComparison.OrdinalIgnoreCase)
                ? "High"
                : string.Equals(importanceRaw, "low", StringComparison.OrdinalIgnoreCase)
                    ? "Low"
                    : "Normal";

            return new ProviderEmailEnvelope
            {
                ExternalId = externalId,
                Subject = subject,
                FromAddress = fromAddress,
                FromName = fromName,
                ToAddresses = toAddresses,
                CcAddresses = ccAddresses,
                BccAddresses = bccAddresses,
                BodyText = bodyText,
                BodyHtml = bodyHtml,
                Folder = "Inbox",
                IsRead = GetBoolean(item, "isRead"),
                HasAttachments = GetBoolean(item, "hasAttachments"),
                AttachmentCount = GetBoolean(item, "hasAttachments") ? 1 : 0,
                Importance = importance,
                ReceivedAt = ParseProviderDateTime(GetString(item, "receivedDateTime")) ?? DateTime.UtcNow,
                SentAt = ParseProviderDateTime(GetString(item, "sentDateTime"))
            };
        }

        private ProviderEmailEnvelope? ParseGmailEnvelope(JsonElement root)
        {
            var externalId = GetString(root, "id");
            if (string.IsNullOrWhiteSpace(externalId))
            {
                return null;
            }

            if (!root.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var headers = payload.TryGetProperty("headers", out var headersElement) && headersElement.ValueKind == JsonValueKind.Array
                ? headersElement
                : default;

            var fromRaw = GetHeaderValue(headers, "From") ?? string.Empty;
            var (fromAddress, fromName) = ParseMailAddress(fromRaw);
            var toAddresses = GetHeaderValue(headers, "To") ?? string.Empty;
            var ccAddresses = GetHeaderValue(headers, "Cc");
            var bccAddresses = GetHeaderValue(headers, "Bcc");
            var subject = GetHeaderValue(headers, "Subject") ?? "(No Subject)";
            var dateRaw = GetHeaderValue(headers, "Date");

            var bodyText = ExtractBody(payload, "text/plain");
            var bodyHtml = ExtractBody(payload, "text/html");
            if (string.IsNullOrWhiteSpace(bodyText))
            {
                bodyText = GetString(root, "snippet");
            }

            var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("labelIds", out var labelElement) && labelElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var label in labelElement.EnumerateArray())
                {
                    var value = label.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        labels.Add(value);
                    }
                }
            }

            var receivedAt = ParseDateTimeFromInternalDate(root, dateRaw) ?? DateTime.UtcNow;
            var sentAt = ParseProviderDateTime(dateRaw);
            var attachmentCount = CountAttachments(payload);

            return new ProviderEmailEnvelope
            {
                ExternalId = externalId,
                Subject = subject,
                FromAddress = fromAddress,
                FromName = fromName,
                ToAddresses = toAddresses,
                CcAddresses = ccAddresses,
                BccAddresses = bccAddresses,
                BodyText = bodyText,
                BodyHtml = bodyHtml,
                Folder = labels.Contains("SENT") ? "Sent" : "Inbox",
                IsRead = !labels.Contains("UNREAD"),
                HasAttachments = attachmentCount > 0,
                AttachmentCount = attachmentCount,
                Importance = labels.Contains("IMPORTANT") ? "High" : "Normal",
                ReceivedAt = receivedAt,
                SentAt = sentAt
            };
        }

        private async Task<List<ProviderEmailAttachment>> LoadGmailAttachmentsAsync(
            JsonElement messageRoot,
            string accessToken,
            CancellationToken cancellationToken)
        {
            if (!messageRoot.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object)
            {
                return new List<ProviderEmailAttachment>();
            }

            var messageId = GetString(messageRoot, "id");
            var attachments = new List<ProviderEmailAttachment>();
            await CollectGmailAttachmentsRecursiveAsync(
                payload,
                messageId,
                accessToken,
                attachments,
                cancellationToken);
            return attachments;
        }

        private async Task CollectGmailAttachmentsRecursiveAsync(
            JsonElement node,
            string? messageId,
            string accessToken,
            ICollection<ProviderEmailAttachment> attachments,
            CancellationToken cancellationToken)
        {
            if (node.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            var fileName = GetString(node, "filename");
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                var body = node.TryGetProperty("body", out var bodyElement) && bodyElement.ValueKind == JsonValueKind.Object
                    ? bodyElement
                    : default;

                byte[]? contentBytes = null;
                var bodyData = body.ValueKind == JsonValueKind.Object && body.TryGetProperty("data", out var dataElement) && dataElement.ValueKind == JsonValueKind.String
                    ? dataElement.GetString()
                    : null;
                if (!string.IsNullOrWhiteSpace(bodyData))
                {
                    contentBytes = DecodeBase64UrlBytes(bodyData);
                }

                var attachmentId = body.ValueKind == JsonValueKind.Object && body.TryGetProperty("attachmentId", out var attachmentIdElement) && attachmentIdElement.ValueKind == JsonValueKind.String
                    ? attachmentIdElement.GetString()
                    : null;

                if ((contentBytes == null || contentBytes.Length == 0) &&
                    !string.IsNullOrWhiteSpace(messageId) &&
                    !string.IsNullOrWhiteSpace(attachmentId))
                {
                    contentBytes = await DownloadGmailAttachmentBytesAsync(
                        messageId!,
                        attachmentId!,
                        accessToken,
                        cancellationToken);
                }

                var mimeType = GetString(node, "mimeType");
                var sizeBytes = body.ValueKind == JsonValueKind.Object
                    ? (long?)GetInt(body, "size")
                    : null;

                var disposition = GetPartHeaderValue(node, "Content-Disposition");
                var isInline = !string.IsNullOrWhiteSpace(disposition) &&
                               disposition.Contains("inline", StringComparison.OrdinalIgnoreCase);

                attachments.Add(new ProviderEmailAttachment
                {
                    ExternalAttachmentId = attachmentId,
                    FileName = fileName!,
                    MimeType = mimeType,
                    SizeBytes = sizeBytes ?? contentBytes?.LongLength ?? 0,
                    IsInline = isInline,
                    ContentBytes = contentBytes,
                    SourceReference = "gmail.payload"
                });
            }

            if (node.TryGetProperty("parts", out var partsElement) && partsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in partsElement.EnumerateArray())
                {
                    await CollectGmailAttachmentsRecursiveAsync(
                        child,
                        messageId,
                        accessToken,
                        attachments,
                        cancellationToken);
                }
            }
        }

        private async Task<byte[]?> DownloadGmailAttachmentBytesAsync(
            string messageId,
            string attachmentId,
            string accessToken,
            CancellationToken cancellationToken)
        {
            using var doc = await GetJsonAsync(
                $"https://gmail.googleapis.com/gmail/v1/users/me/messages/{Uri.EscapeDataString(messageId)}/attachments/{Uri.EscapeDataString(attachmentId)}",
                authScheme: "Bearer",
                authToken: accessToken,
                additionalHeaders: null,
                cancellationToken: cancellationToken);

            var data = GetString(doc.RootElement, "data");
            return DecodeBase64UrlBytes(data);
        }

        private async Task<List<ProviderEmailAttachment>> LoadOutlookAttachmentsAsync(
            string messageId,
            string accessToken,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(messageId))
            {
                return new List<ProviderEmailAttachment>();
            }

            using var doc = await GetJsonAsync(
                $"https://graph.microsoft.com/v1.0/me/messages/{Uri.EscapeDataString(messageId)}/attachments?$top=25&$select=id,name,contentType,size,isInline,contentBytes",
                authScheme: "Bearer",
                authToken: accessToken,
                additionalHeaders: null,
                cancellationToken: cancellationToken);

            var results = new List<ProviderEmailAttachment>();
            if (!doc.RootElement.TryGetProperty("value", out var attachmentsElement) || attachmentsElement.ValueKind != JsonValueKind.Array)
            {
                return results;
            }

            foreach (var item in attachmentsElement.EnumerateArray())
            {
                var odataType = GetString(item, "@odata.type");
                if (!string.IsNullOrWhiteSpace(odataType) &&
                    !odataType.Contains("fileAttachment", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var fileName = GetString(item, "name");
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    continue;
                }

                var contentBytes = DecodeBase64Bytes(GetString(item, "contentBytes"));
                results.Add(new ProviderEmailAttachment
                {
                    ExternalAttachmentId = GetString(item, "id"),
                    FileName = fileName!,
                    MimeType = GetString(item, "contentType"),
                    SizeBytes = GetInt(item, "size") ?? contentBytes?.Length ?? 0,
                    IsInline = GetBoolean(item, "isInline"),
                    ContentBytes = contentBytes,
                    SourceReference = "outlook.attachments"
                });
            }

            return results;
        }

        private static string GetPartHeaderValue(JsonElement part, string name)
        {
            if (!part.TryGetProperty("headers", out var headers) || headers.ValueKind != JsonValueKind.Array)
            {
                return string.Empty;
            }

            return GetHeaderValue(headers, name);
        }

        private string? ResolveClientIdFromEnvelope(
            ProviderEmailEnvelope envelope,
            IReadOnlyCollection<ClientLookupItem> clients)
        {
            var participants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var address in ExtractEmailAddresses(envelope.FromAddress))
            {
                participants.Add(address);
            }

            foreach (var address in ExtractEmailAddresses(envelope.ToAddresses))
            {
                participants.Add(address);
            }

            foreach (var address in ExtractEmailAddresses(envelope.CcAddresses))
            {
                participants.Add(address);
            }

            if (participants.Count == 0)
            {
                return null;
            }

            return clients.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c.Email) && participants.Contains(c.Email))?.Id;
        }

        private string? ResolveMatterIdFromEnvelope(
            ProviderEmailEnvelope envelope,
            string? resolvedClientId,
            IReadOnlyCollection<MatterLookupItem> matters)
        {
            var subjectAndBody = $"{envelope.Subject} {envelope.BodyText}";
            var tokenMatches = CaseNumberTokenRegex.Matches(subjectAndBody)
                .Select(m => m.Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var scoped = string.IsNullOrWhiteSpace(resolvedClientId)
                ? matters
                : matters.Where(m => m.ClientId == resolvedClientId).ToList();

            if (tokenMatches.Count > 0)
            {
                var matched = scoped
                    .Where(m => !string.IsNullOrWhiteSpace(m.CaseNumber) && tokenMatches.Contains(m.CaseNumber))
                    .Select(m => m.Id)
                    .ToList();
                if (matched.Count == 1)
                {
                    return matched[0];
                }
            }

            var openForClient = scoped.ToList();
            if (openForClient.Count == 1)
            {
                return openForClient[0].Id;
            }

            return null;
        }

        private string? ResolveMatterIdFromDocket(
            string? caseName,
            string? docketNumber,
            IReadOnlyCollection<MatterLookupItem> matters)
        {
            var searchText = $"{caseName} {docketNumber}";
            var tokens = CaseNumberTokenRegex.Matches(searchText)
                .Select(m => m.Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (tokens.Count == 0)
            {
                return null;
            }

            var matches = matters
                .Where(m => !string.IsNullOrWhiteSpace(m.CaseNumber) && tokens.Contains(m.CaseNumber))
                .Select(m => m.Id)
                .ToList();

            return matches.Count == 1 ? matches[0] : null;
        }

        private async Task<IntegrationSyncResult> UpsertEfilingSubmissionsAsync(
            string connectionId,
            string providerKey,
            JsonDocument payload,
            CancellationToken cancellationToken)
        {
            return await UpsertEfilingSubmissionsAsync(
                connectionId,
                providerKey,
                ResolveSubmissionRows(payload.RootElement),
                cancellationToken);
        }

        private async Task<IntegrationSyncResult> UpsertEfilingSubmissionsAsync(
            string connectionId,
            string providerKey,
            IReadOnlyCollection<JsonElement> rows,
            CancellationToken cancellationToken)
        {
            if (rows.Count == 0)
            {
                return new IntegrationSyncResult
                {
                    Success = true,
                    SyncedCount = 0,
                    Message = $"{providerKey} returned no filing submissions."
                };
            }

            var externalIds = rows
                .Select(row => GetSubmissionId(row))
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Cast<string>()
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var existing = externalIds.Count == 0
                ? new Dictionary<string, EfilingSubmission>(StringComparer.Ordinal)
                : await _context.EfilingSubmissions
                    .Where(s => s.ProviderKey == providerKey && externalIds.Contains(s.ExternalSubmissionId))
                    .ToDictionaryAsync(s => s.ExternalSubmissionId, StringComparer.Ordinal, cancellationToken);

            var docketMap = await _context.CourtDocketEntries
                .Where(d => d.ProviderKey == IntegrationProviderKeys.CourtListenerDockets && d.ExternalDocketId != null)
                .ToDictionaryAsync(d => d.ExternalDocketId, d => d.MatterId, StringComparer.Ordinal, cancellationToken);

            var now = DateTime.UtcNow;
            var changed = 0;
            var submissionSignals = new List<EfilingSubmissionSyncSignal>();
            var artifactCandidates = new List<(EfilingSubmission submission, JsonElement row)>();
            foreach (var row in rows)
            {
                var externalSubmissionId = GetSubmissionId(row);
                if (string.IsNullOrWhiteSpace(externalSubmissionId))
                {
                    continue;
                }

                var externalDocketId = GetString(row, "docket") ?? GetString(row, "docket_id");
                docketMap.TryGetValue(externalDocketId ?? string.Empty, out var mappedMatterId);

                var status = NormalizeSubmissionStatus(
                    GetString(row, "status")
                    ?? GetString(row, "state")
                    ?? GetString(row, "processing_status"));

                if (existing.TryGetValue(externalSubmissionId, out var entity))
                {
                    var previousStatus = entity.Status;
                    var previousRejectionReason = entity.RejectionReason;
                    entity.ExternalDocketId = externalDocketId ?? entity.ExternalDocketId;
                    entity.Status = status;
                    entity.ReferenceNumber = GetString(row, "reference_number") ?? GetString(row, "reference") ?? entity.ReferenceNumber;
                    entity.SubmittedAt = ParseProviderDateTime(GetString(row, "date_created")) ?? entity.SubmittedAt;
                    entity.AcceptedAt = ParseProviderDateTime(GetString(row, "accepted_at")) ?? entity.AcceptedAt;
                    entity.RejectedAt = ParseProviderDateTime(GetString(row, "rejected_at")) ?? entity.RejectedAt;
                    entity.RejectionReason = GetString(row, "rejection_reason") ?? entity.RejectionReason;
                    entity.MatterId = mappedMatterId ?? entity.MatterId;
                    entity.LastSeenAt = now;
                    entity.MetadataJson = _piiMinimizer.SanitizeProviderMetadataJsonForStorage(
                        providerKey,
                        "efiling_submission",
                        row.GetRawText());
                    entity.UpdatedAt = now;
                    changed++;
                    submissionSignals.Add(new EfilingSubmissionSyncSignal
                    {
                        SubmissionId = entity.Id,
                        PreviousStatus = previousStatus,
                        CurrentStatus = entity.Status,
                        CurrentRejectionReason = entity.RejectionReason ?? previousRejectionReason
                    });
                    artifactCandidates.Add((entity, row.Clone()));
                }
                else
                {
                    var createdSubmission = new EfilingSubmission
                    {
                        Id = Guid.NewGuid().ToString(),
                        ProviderKey = providerKey,
                        ExternalSubmissionId = externalSubmissionId,
                        ExternalDocketId = externalDocketId,
                        ReferenceNumber = GetString(row, "reference_number") ?? GetString(row, "reference"),
                        Status = status,
                        MatterId = mappedMatterId,
                        SubmittedAt = ParseProviderDateTime(GetString(row, "date_created")),
                        AcceptedAt = ParseProviderDateTime(GetString(row, "accepted_at")),
                        RejectedAt = ParseProviderDateTime(GetString(row, "rejected_at")),
                        RejectionReason = GetString(row, "rejection_reason"),
                        MetadataJson = _piiMinimizer.SanitizeProviderMetadataJsonForStorage(
                            providerKey,
                            "efiling_submission",
                            row.GetRawText()),
                        LastSeenAt = now,
                        CreatedAt = now,
                        UpdatedAt = now
                    };
                    _context.EfilingSubmissions.Add(createdSubmission);
                    changed++;
                    submissionSignals.Add(new EfilingSubmissionSyncSignal
                    {
                        SubmissionId = createdSubmission.Id,
                        PreviousStatus = null,
                        CurrentStatus = createdSubmission.Status,
                        CurrentRejectionReason = createdSubmission.RejectionReason
                    });
                    artifactCandidates.Add((createdSubmission, row.Clone()));
                }
            }

            EfilingSubmissionSignalResult? signalResult = null;
            var artifactIngestion = new EfilingArtifactIngestionResult();
            if (changed > 0)
            {
                await _context.SaveChangesAsync(cancellationToken);
                artifactIngestion = await IngestEfilingSubmissionArtifactsBatchAsync(connectionId, providerKey, artifactCandidates, cancellationToken);
                signalResult = await _efilingAutomationService.ProcessSubmissionSyncSignalsAsync(
                    connectionId,
                    submissionSignals,
                    cancellationToken);
            }

            return new IntegrationSyncResult
            {
                Success = true,
                SyncedCount = changed,
                Message = signalResult == null
                    ? $"{providerKey} filing sync completed. Upserts={changed}."
                    : $"{providerKey} filing sync completed. Upserts={changed}, Reviews={signalResult.ReviewsQueued}, Tasks={signalResult.TasksQueued}, Outbox={signalResult.OutboxQueued}, NoticeDocs={artifactIngestion.DocumentsImported}, ArtifactReviews={artifactIngestion.ReviewsQueued}, ArtifactDeduped={artifactIngestion.Deduped}."
            };
        }

        private async Task<EfilingArtifactIngestionResult> IngestEfilingSubmissionArtifactsBatchAsync(
            string connectionId,
            string providerKey,
            IReadOnlyCollection<(EfilingSubmission submission, JsonElement row)> items,
            CancellationToken cancellationToken)
        {
            if (items.Count == 0)
            {
                return new EfilingArtifactIngestionResult();
            }

            var result = new EfilingArtifactIngestionResult();
            foreach (var (submission, row) in items)
            {
                var perSubmission = await IngestEfilingSubmissionArtifactsAsync(connectionId, providerKey, submission, row, cancellationToken);
                result.DocumentsImported += perSubmission.DocumentsImported;
                result.ReviewsQueued += perSubmission.ReviewsQueued;
                result.Deduped += perSubmission.Deduped;
            }

            return result;
        }

        private async Task<EfilingArtifactIngestionResult> IngestEfilingSubmissionArtifactsAsync(
            string connectionId,
            string providerKey,
            EfilingSubmission submission,
            JsonElement row,
            CancellationToken cancellationToken)
        {
            var artifacts = ResolveEfilingSubmissionArtifacts(row);
            if (artifacts.Count == 0)
            {
                return new EfilingArtifactIngestionResult();
            }

            var result = new EfilingArtifactIngestionResult();
            var externalArtifactIds = artifacts
                .Select(a => a.ExternalArtifactId)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            var existingLinks = externalArtifactIds.Length == 0
                ? new Dictionary<string, IntegrationEntityLink>(StringComparer.Ordinal)
                : await _context.IntegrationEntityLinks
                    .Where(l => l.ConnectionId == connectionId &&
                                l.ProviderKey == providerKey &&
                                l.ExternalEntityType == "efiling_artifact" &&
                                externalArtifactIds.Contains(l.ExternalEntityId))
                    .ToDictionaryAsync(l => l.ExternalEntityId, StringComparer.Ordinal, cancellationToken);

            foreach (var artifact in artifacts)
            {
                if (string.IsNullOrWhiteSpace(artifact.ExternalArtifactId))
                {
                    continue;
                }

                if (existingLinks.ContainsKey(artifact.ExternalArtifactId))
                {
                    result.Deduped++;
                    continue;
                }

                byte[]? bytes = null;
                if (!string.IsNullOrWhiteSpace(artifact.ContentBase64))
                {
                    try
                    {
                        bytes = Convert.FromBase64String(artifact.ContentBase64);
                    }
                    catch
                    {
                        bytes = null;
                    }
                }

                string? documentId = null;
                string? sha256 = null;
                if (bytes is { Length: > 0 })
                {
                    sha256 = ComputeSha256Hex(bytes);
                    var existingVersion = await _context.DocumentVersions
                        .AsNoTracking()
                        .Where(v => v.Sha256 == sha256)
                        .OrderByDescending(v => v.CreatedAt)
                        .Select(v => new { v.DocumentId })
                        .FirstOrDefaultAsync(cancellationToken);

                    if (existingVersion != null)
                    {
                        documentId = existingVersion.DocumentId;
                        result.Deduped++;
                    }
                    else
                    {
                        documentId = await CreateEfilingArtifactDocumentAsync(submission, artifact, bytes, sha256, cancellationToken);
                        if (!string.IsNullOrWhiteSpace(documentId))
                        {
                            result.DocumentsImported++;
                        }
                    }
                }
                else
                {
                    result.ReviewsQueued += await QueueEfilingArtifactReviewAsync(connectionId, providerKey, submission, artifact, cancellationToken);
                }

                if (string.IsNullOrWhiteSpace(documentId))
                {
                    continue;
                }

                var link = new IntegrationEntityLink
                {
                    Id = Guid.NewGuid().ToString(),
                    ConnectionId = connectionId,
                    ProviderKey = providerKey,
                    LocalEntityType = "document",
                    LocalEntityId = documentId,
                    ExternalEntityType = "efiling_artifact",
                    ExternalEntityId = artifact.ExternalArtifactId,
                    ExternalVersion = sha256 ?? artifact.VersionHint,
                    LastDirection = "inbound",
                    IdempotencyKey = BuildExternalVersionIdempotencyKey(providerKey, "efiling_artifact", artifact.ExternalArtifactId, sha256 ?? artifact.VersionHint),
                    LastSyncedAt = DateTime.UtcNow,
                    MetadataJson = JsonSerializer.Serialize(new
                    {
                        submissionId = submission.Id,
                        submission.ExternalSubmissionId,
                        artifact.ArtifactType,
                        artifact.Label,
                        artifact.FileName,
                        artifact.MimeType,
                        artifact.DownloadUrl,
                        sha256
                    })
                };
                _context.IntegrationEntityLinks.Add(link);
                existingLinks[artifact.ExternalArtifactId] = link;
            }

            if (_context.ChangeTracker.HasChanges())
            {
                await _context.SaveChangesAsync(cancellationToken);
            }

            return result;
        }

        private List<EfilingSubmissionArtifact> ResolveEfilingSubmissionArtifacts(JsonElement row)
        {
            var result = new List<EfilingSubmissionArtifact>();
            var collections = new (string property, string defaultType)[]
            {
                ("documents", "submission_document"),
                ("artifacts", "artifact"),
                ("attachments", "attachment"),
                ("notices", "notice"),
                ("notice_documents", "notice"),
                ("stampedCopies", "stamped_copy"),
                ("stamped_copies", "stamped_copy"),
                ("returnedDocuments", "returned_document"),
                ("returned_documents", "returned_document")
            };

            foreach (var (property, defaultType) in collections)
            {
                if (!row.TryGetProperty(property, out var items) || items.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var item in items.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var externalId = GetString(item, "id")
                                     ?? GetString(item, "artifactId")
                                     ?? GetString(item, "documentId")
                                     ?? GetString(item, "fileId")
                                     ?? GetString(item, "uuid");
                    var fileName = GetString(item, "fileName")
                                   ?? GetString(item, "filename")
                                   ?? GetString(item, "name")
                                   ?? GetString(item, "title");
                    if (string.IsNullOrWhiteSpace(fileName))
                    {
                        fileName = "efiling-artifact.bin";
                    }

                    var normalizedType = (GetString(item, "type")
                                          ?? GetString(item, "artifactType")
                                          ?? GetString(item, "category")
                                          ?? defaultType)
                        .Trim()
                        .ToLowerInvariant();

                    var label = GetString(item, "label")
                                ?? GetString(item, "description")
                                ?? GetString(item, "name")
                                ?? normalizedType;
                    var mimeType = GetString(item, "mimeType") ?? GetString(item, "contentType");
                    var contentBase64 = GetString(item, "contentBase64")
                                        ?? GetString(item, "data")
                                        ?? GetNestedString(item, "content", "base64");
                    var downloadUrl = GetString(item, "downloadUrl")
                                      ?? GetString(item, "url")
                                      ?? GetNestedString(item, "links", "download");
                    var isStamped = GetBoolean(item, "isStamped")
                                    || normalizedType.Contains("stamped", StringComparison.OrdinalIgnoreCase)
                                    || normalizedType.Contains("stamped_copy", StringComparison.OrdinalIgnoreCase);
                    var isNotice = GetBoolean(item, "isNotice")
                                   || normalizedType.Contains("notice", StringComparison.OrdinalIgnoreCase);

                    if (string.IsNullOrWhiteSpace(externalId))
                    {
                        var suffix = !string.IsNullOrWhiteSpace(downloadUrl) ? downloadUrl : $"{fileName}:{normalizedType}:{label}";
                        externalId = ComputeSha256Hex(Encoding.UTF8.GetBytes($"efiling_artifact|{suffix}"));
                    }

                    result.Add(new EfilingSubmissionArtifact
                    {
                        ExternalArtifactId = externalId,
                        FileName = fileName,
                        MimeType = mimeType,
                        ArtifactType = normalizedType,
                        Label = label,
                        DownloadUrl = downloadUrl,
                        ContentBase64 = contentBase64,
                        IsNotice = isNotice,
                        IsStampedCopy = isStamped,
                        VersionHint = GetString(item, "updatedAt")
                                      ?? GetString(item, "lastModifiedDateTime")
                                      ?? GetString(item, "version")
                    });
                }
            }

            return result;
        }

        private async Task<string?> CreateEfilingArtifactDocumentAsync(
            EfilingSubmission submission,
            EfilingSubmissionArtifact artifact,
            byte[] contentBytes,
            string sha256,
            CancellationToken cancellationToken)
        {
            var safeFileName = SanitizeFileName(artifact.FileName);
            var uniqueFileName = $"{Guid.NewGuid()}_{safeFileName}";
            var absolutePath = GetTenantRelativeUploadPath(uniqueFileName);
            DocumentEncryptionPayload? encryptionPayload = null;

            if (_documentEncryptionService.Enabled)
            {
                encryptionPayload = _documentEncryptionService.EncryptBytes(contentBytes);
                await _fileStorage.SaveBytesAsync(absolutePath, encryptionPayload.Ciphertext, artifact.MimeType, cancellationToken);
            }
            else
            {
                await _fileStorage.SaveBytesAsync(absolutePath, contentBytes, artifact.MimeType, cancellationToken);
            }

            var now = DateTime.UtcNow;
            var category = artifact.IsStampedCopy ? "Stamped Copy" : artifact.IsNotice ? "E-Filing Notice" : "E-Filing Artifact";
            var document = new Document
            {
                Id = Guid.NewGuid().ToString(),
                Name = safeFileName,
                FileName = safeFileName,
                FilePath = GetTenantRelativeUploadPath(uniqueFileName),
                FileSize = contentBytes.LongLength,
                MimeType = string.IsNullOrWhiteSpace(artifact.MimeType) ? "application/octet-stream" : artifact.MimeType!,
                IsEncrypted = encryptionPayload != null,
                EncryptionKeyId = encryptionPayload?.KeyId,
                EncryptionIv = encryptionPayload?.Iv,
                EncryptionTag = encryptionPayload?.Tag,
                EncryptionAlgorithm = encryptionPayload?.Algorithm,
                MatterId = submission.MatterId,
                Category = category,
                Description = Truncate($"E-filing {artifact.Label} | Submission {submission.ReferenceNumber ?? submission.ExternalSubmissionId}", 500),
                Tags = SerializeStringList(new[]
                {
                    "efiling",
                    NormalizeProviderTag(submission.ProviderKey),
                    artifact.IsStampedCopy ? "stamped-copy" : null,
                    artifact.IsNotice ? "notice" : null
                }),
                Status = "Draft",
                CreatedAt = now,
                UpdatedAt = now
            };

            _context.Documents.Add(document);
            await _context.SaveChangesAsync(cancellationToken);

            var version = new DocumentVersion
            {
                Id = Guid.NewGuid().ToString(),
                DocumentId = document.Id,
                FileName = safeFileName,
                FilePath = document.FilePath,
                FileSize = document.FileSize,
                IsEncrypted = document.IsEncrypted,
                EncryptionKeyId = document.EncryptionKeyId,
                EncryptionIv = document.EncryptionIv,
                EncryptionTag = document.EncryptionTag,
                EncryptionAlgorithm = document.EncryptionAlgorithm,
                Sha256 = sha256,
                UploadedByUserId = null,
                CreatedAt = now
            };
            _context.DocumentVersions.Add(version);
            await _context.SaveChangesAsync(cancellationToken);

            try
            {
                await _documentIndexService.UpsertIndexAsync(document, contentBytes);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to index e-filing artifact document {DocumentId}", document.Id);
            }

            return document.Id;
        }

        private async Task<int> QueueEfilingArtifactReviewAsync(
            string connectionId,
            string providerKey,
            EfilingSubmission submission,
            EfilingSubmissionArtifact artifact,
            CancellationToken cancellationToken)
        {
            var sourceId = $"{submission.Id}:{artifact.ExternalArtifactId}";
            var itemType = artifact.IsStampedCopy ? "efile_stamped_copy_ingest_review" : "efile_notice_ingest_review";
            var existing = await _context.IntegrationReviewQueueItems.FirstOrDefaultAsync(r =>
                r.ConnectionId == connectionId &&
                r.ProviderKey == providerKey &&
                r.SourceType == nameof(EfilingSubmission) &&
                r.SourceId == sourceId &&
                r.ItemType == itemType &&
                (r.Status == IntegrationReviewQueueStatuses.Pending || r.Status == IntegrationReviewQueueStatuses.InReview),
                cancellationToken);

            if (existing != null)
            {
                existing.UpdatedAt = DateTime.UtcNow;
                existing.Summary = Truncate($"Artifact available for manual ingest/download: {artifact.FileName} ({artifact.DownloadUrl ?? "no-url"})", 2048);
                existing.ContextJson = JsonSerializer.Serialize(new
                {
                    submissionId = submission.Id,
                    submission.ExternalSubmissionId,
                    artifact.ExternalArtifactId,
                    artifact.FileName,
                    artifact.MimeType,
                    artifact.ArtifactType,
                    artifact.DownloadUrl,
                    artifact.IsNotice,
                    artifact.IsStampedCopy
                });
                return 0;
            }

            _context.IntegrationReviewQueueItems.Add(new IntegrationReviewQueueItem
            {
                Id = Guid.NewGuid().ToString(),
                ConnectionId = connectionId,
                ProviderKey = providerKey,
                ItemType = itemType,
                SourceType = nameof(EfilingSubmission),
                SourceId = sourceId,
                Status = IntegrationReviewQueueStatuses.Pending,
                Priority = artifact.IsStampedCopy ? "high" : "medium",
                Title = artifact.IsStampedCopy ? "Stamped copy available for ingest" : "Provider notice available for ingest",
                Summary = Truncate($"Artifact available for manual ingest/download: {artifact.FileName} ({artifact.DownloadUrl ?? "no-url"})", 2048),
                ContextJson = JsonSerializer.Serialize(new
                {
                    submissionId = submission.Id,
                    submission.ExternalSubmissionId,
                    artifact.ExternalArtifactId,
                    artifact.FileName,
                    artifact.MimeType,
                    artifact.ArtifactType,
                    artifact.Label,
                    artifact.DownloadUrl,
                    artifact.IsNotice,
                    artifact.IsStampedCopy
                }),
                SuggestedActionsJson = JsonSerializer.Serialize(new object[]
                {
                    new { action = "download_and_attach_notice" },
                    new { action = "link_to_matter_documents" },
                    new { action = "mark_artifact_reviewed" }
                }),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            return 1;
        }

        private async Task<JsonDocument> PostQuickBooksEntityAsync(
            string apiBaseUrl,
            string realmId,
            string entityName,
            string? operation,
            object payload,
            string accessToken,
            CancellationToken cancellationToken)
        {
            var endpoint = string.IsNullOrWhiteSpace(operation)
                ? $"{apiBaseUrl}/v3/company/{Uri.EscapeDataString(realmId)}/{entityName}"
                : $"{apiBaseUrl}/v3/company/{Uri.EscapeDataString(realmId)}/{entityName}?operation={Uri.EscapeDataString(operation)}";

            return await PostJsonAsync(
                endpoint,
                payload,
                authScheme: "Bearer",
                authToken: accessToken,
                additionalHeaders: new Dictionary<string, string> { ["Accept"] = "application/json" },
                cancellationToken: cancellationToken);
        }

        private void UpsertIntegrationEntityLink(
            IDictionary<string, IntegrationEntityLink> existingByLocalId,
            string connectionId,
            string providerKey,
            string localEntityType,
            string localEntityId,
            string externalEntityType,
            string externalEntityId,
            string? externalVersion,
            string direction,
            string? metadataJson)
        {
            if (existingByLocalId.TryGetValue(localEntityId, out var existing))
            {
                existing.ExternalEntityType = externalEntityType;
                existing.ExternalEntityId = externalEntityId;
                existing.ExternalVersion = externalVersion ?? existing.ExternalVersion;
                existing.LastDirection = direction;
                existing.IdempotencyKey = BuildExternalVersionIdempotencyKey(providerKey, externalEntityType, externalEntityId, externalVersion);
                existing.LastSyncedAt = DateTime.UtcNow;
                existing.MetadataJson = metadataJson;
                return;
            }

            var created = new IntegrationEntityLink
            {
                Id = Guid.NewGuid().ToString(),
                ConnectionId = connectionId,
                ProviderKey = providerKey,
                LocalEntityType = localEntityType,
                LocalEntityId = localEntityId,
                ExternalEntityType = externalEntityType,
                ExternalEntityId = externalEntityId,
                ExternalVersion = externalVersion,
                LastDirection = direction,
                IdempotencyKey = BuildExternalVersionIdempotencyKey(providerKey, externalEntityType, externalEntityId, externalVersion),
                LastSyncedAt = DateTime.UtcNow,
                MetadataJson = metadataJson
            };
            _context.IntegrationEntityLinks.Add(created);
            existingByLocalId[localEntityId] = created;
        }

        private async Task<int> CountOpenConflictsForConnectionAsync(string connectionId, CancellationToken cancellationToken)
        {
            return await _context.IntegrationConflictQueueItems
                .AsNoTracking()
                .Where(c => c.ConnectionId == connectionId &&
                            (c.Status == IntegrationConflictStatuses.Open || c.Status == IntegrationConflictStatuses.InReview))
                .CountAsync(cancellationToken);
        }

        private async Task<int> CountOpenReviewsForConnectionProviderAsync(string connectionId, string providerKey, CancellationToken cancellationToken)
        {
            return await _context.IntegrationReviewQueueItems
                .AsNoTracking()
                .Where(r =>
                    r.ConnectionId == connectionId &&
                    r.ProviderKey == providerKey &&
                    (r.Status == IntegrationReviewQueueStatuses.Pending || r.Status == IntegrationReviewQueueStatuses.InReview))
                .CountAsync(cancellationToken);
        }

        private async Task<(int upserted, int files, int folders, int deleted, int reviews)> UpsertExternalDocumentCatalogAsync(
            string connectionId,
            string providerKey,
            IReadOnlyCollection<ProviderDocumentCatalogEntry> entries,
            CancellationToken cancellationToken)
        {
            if (entries.Count == 0)
            {
                return (0, 0, 0, 0, 0);
            }

            var externalIds = entries
                .Where(e => !string.IsNullOrWhiteSpace(e.ExternalId))
                .Select(e => e.ExternalId)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            var existingLinks = externalIds.Length == 0
                ? new Dictionary<string, IntegrationEntityLink>(StringComparer.Ordinal)
                : await _context.IntegrationEntityLinks
                    .Where(l =>
                        l.ConnectionId == connectionId &&
                        l.ProviderKey == providerKey &&
                        externalIds.Contains(l.ExternalEntityId))
                    .ToDictionaryAsync(l => l.ExternalEntityId, StringComparer.Ordinal, cancellationToken);

            var matters = await _context.Matters
                .Select(m => new MatterLookupItem
                {
                    Id = m.Id,
                    ClientId = m.ClientId,
                    CaseNumber = m.CaseNumber,
                    Name = m.Name
                })
                .ToListAsync(cancellationToken);

            var now = DateTime.UtcNow;
            var upserted = 0;
            var files = 0;
            var folders = 0;
            var deleted = 0;

            foreach (var entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry.ExternalId))
                {
                    continue;
                }

                var externalVersion = entry.IsDeleted
                    ? $"deleted:{(entry.ModifiedAt ?? now):O}"
                    : entry.ModifiedAt?.ToString("O") ?? (entry.SizeBytes > 0 ? entry.SizeBytes.ToString() : null);
                var matterId = ResolveMatterIdFromCatalogEntry(entry, matters);
                var metadataJson = _piiMinimizer.SanitizeObjectForStorage(new
                {
                    entry.Name,
                    entry.MimeType,
                    entry.SizeBytes,
                    entry.ModifiedAt,
                    entry.WebUrl,
                    entry.ParentReference,
                    entry.IsFolder,
                    entry.IsDeleted,
                    matterIdSuggestion = matterId
                }, $"{providerKey}:external_document_catalog");

                if (existingLinks.TryGetValue(entry.ExternalId, out var link))
                {
                    link.LocalEntityType = "external_document_catalog";
                    link.LocalEntityId = entry.ExternalId;
                    if (!entry.IsDeleted || entry.IsFolder)
                    {
                        link.ExternalEntityType = entry.IsFolder ? "folder" : "file";
                    }
                    link.ExternalVersion = externalVersion ?? link.ExternalVersion;
                    link.LastDirection = "inbound";
                    link.IdempotencyKey = BuildExternalVersionIdempotencyKey(providerKey, link.ExternalEntityType, entry.ExternalId, externalVersion);
                    link.LastSyncedAt = now;
                    link.MetadataJson = metadataJson;
                    upserted++;
                }
                else
                {
                    if (entry.IsDeleted)
                    {
                        continue;
                    }

                    var created = new IntegrationEntityLink
                    {
                        Id = Guid.NewGuid().ToString(),
                        ConnectionId = connectionId,
                        ProviderKey = providerKey,
                        LocalEntityType = "external_document_catalog",
                        LocalEntityId = entry.ExternalId,
                        ExternalEntityType = entry.IsFolder ? "folder" : "file",
                        ExternalEntityId = entry.ExternalId,
                        ExternalVersion = externalVersion,
                        LastDirection = "inbound",
                        IdempotencyKey = BuildExternalVersionIdempotencyKey(providerKey, entry.IsFolder ? "folder" : "file", entry.ExternalId, externalVersion),
                        LastSyncedAt = now,
                        MetadataJson = metadataJson
                    };
                    _context.IntegrationEntityLinks.Add(created);
                    existingLinks[entry.ExternalId] = created;
                    upserted++;
                }

                if (entry.IsDeleted) deleted++;
                else if (entry.IsFolder) folders++;
                else files++;
            }

            if (upserted > 0)
            {
                await _context.SaveChangesAsync(cancellationToken);
            }

            var reviews = await QueueExternalDocumentCatalogReviewsAsync(
                connectionId,
                providerKey,
                entries,
                existingLinks,
                matters,
                cancellationToken);

            return (upserted, files, folders, deleted, reviews);
        }

        private async Task<int> QueueExternalDocumentCatalogReviewsAsync(
            string connectionId,
            string providerKey,
            IReadOnlyCollection<ProviderDocumentCatalogEntry> entries,
            IDictionary<string, IntegrationEntityLink> linksByExternalId,
            IReadOnlyCollection<MatterLookupItem> matters,
            CancellationToken cancellationToken)
        {
            var linkIds = linksByExternalId.Values.Select(l => l.Id).Distinct(StringComparer.Ordinal).ToList();
            if (linkIds.Count == 0)
            {
                return 0;
            }

            var existing = await _context.IntegrationReviewQueueItems
                .Where(r =>
                    r.ConnectionId == connectionId &&
                    r.ProviderKey == providerKey &&
                    r.SourceType == nameof(IntegrationEntityLink) &&
                    r.SourceId != null &&
                    linkIds.Contains(r.SourceId) &&
                    (r.Status == IntegrationReviewQueueStatuses.Pending || r.Status == IntegrationReviewQueueStatuses.InReview))
                .ToDictionaryAsync(r => $"{r.SourceId}|{r.ItemType}", StringComparer.Ordinal, cancellationToken);

            var created = 0;
            foreach (var entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry.ExternalId) || (entry.IsFolder && !entry.IsDeleted))
                {
                    continue;
                }

                if (!linksByExternalId.TryGetValue(entry.ExternalId, out var link))
                {
                    continue;
                }

                var suggestedMatterId = ResolveMatterIdFromCatalogEntry(entry, matters);
                var itemType = entry.IsDeleted
                    ? "document_remote_delete_review"
                    : string.IsNullOrWhiteSpace(suggestedMatterId)
                        ? "document_workspace_link_review"
                        : "document_metadata_sync_review";

                var key = $"{link.Id}|{itemType}";
                var title = entry.IsDeleted
                    ? "External document deletion/archive detected"
                    : string.IsNullOrWhiteSpace(suggestedMatterId)
                        ? "External document requires matter/workspace link review"
                        : "External document metadata sync review";
                var summary = entry.IsDeleted
                    ? $"{providerKey} {(entry.IsFolder ? "folder" : "file")} '{entry.Name}' was deleted/trashed remotely. Review local archive/metadata actions."
                    : string.IsNullOrWhiteSpace(suggestedMatterId)
                        ? $"{providerKey} file '{entry.Name}' could not be mapped to a matter automatically."
                        : $"{providerKey} file '{entry.Name}' mapped to matter candidate '{suggestedMatterId}' for metadata sync confirmation.";

                var contextJson = JsonSerializer.Serialize(new
                {
                    entry.ExternalId,
                    entry.Name,
                    entry.MimeType,
                    entry.SizeBytes,
                    entry.ModifiedAt,
                    entry.WebUrl,
                    entry.ParentReference,
                    entry.IsDeleted,
                    suggestedMatterId,
                    providerKey
                });

                if (existing.TryGetValue(key, out var review))
                {
                    review.Title = Truncate(title, 160);
                    review.Summary = Truncate(summary, 2048);
                    review.ContextJson = contextJson;
                    review.SuggestedActionsJson = JsonSerializer.Serialize(new object[]
                    {
                        entry.IsDeleted
                            ? (object)new { action = "archive_local_document", matterId = (string?)null }
                            : new { action = "assign_matter", matterId = suggestedMatterId },
                        entry.IsDeleted
                            ? (object)new { action = "confirm_remote_delete" }
                            : new { action = "confirm_metadata_sync" }
                    });
                    review.UpdatedAt = DateTime.UtcNow;
                    continue;
                }

                _context.IntegrationReviewQueueItems.Add(new IntegrationReviewQueueItem
                {
                    Id = Guid.NewGuid().ToString(),
                    ConnectionId = connectionId,
                    ProviderKey = providerKey,
                    ItemType = itemType,
                    SourceType = nameof(IntegrationEntityLink),
                    SourceId = link.Id,
                    Status = IntegrationReviewQueueStatuses.Pending,
                    Priority = entry.IsDeleted ? "high" : string.IsNullOrWhiteSpace(suggestedMatterId) ? "high" : "medium",
                    Title = Truncate(title, 160),
                    Summary = Truncate(summary, 2048),
                    ContextJson = contextJson,
                    SuggestedActionsJson = JsonSerializer.Serialize(new object[]
                    {
                        entry.IsDeleted
                            ? (object)new { action = "archive_local_document", matterId = (string?)null }
                            : new { action = "assign_matter", matterId = suggestedMatterId },
                        entry.IsDeleted
                            ? (object)new { action = "confirm_remote_delete" }
                            : new { action = "confirm_metadata_sync" }
                    }),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
                created++;
            }

            if (created > 0)
            {
                await _context.SaveChangesAsync(cancellationToken);
            }

            return created;
        }

        private string? ResolveMatterIdFromCatalogEntry(
            ProviderDocumentCatalogEntry entry,
            IReadOnlyCollection<MatterLookupItem> matters)
        {
            if (entry.IsDeleted)
            {
                return null;
            }

            var search = $"{entry.Name} {entry.ParentReference}";
            var tokens = CaseNumberTokenRegex.Matches(search)
                .Select(m => m.Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (tokens.Count == 0)
            {
                return null;
            }

            var matches = matters
                .Where(m => !string.IsNullOrWhiteSpace(m.CaseNumber) && tokens.Contains(m.CaseNumber))
                .Select(m => m.Id)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            return matches.Count == 1 ? matches[0] : null;
        }

        private async Task<int> QueueGenericErpReviewSignalsAsync(
            string connectionId,
            string providerKey,
            IReadOnlyCollection<JsonElement> rows,
            CancellationToken cancellationToken)
        {
            if (rows.Count == 0)
            {
                return 0;
            }

            var ids = rows
                .Select(r => GetString(r, "id")
                           ?? GetString(r, "Id")
                           ?? GetString(r, "number")
                           ?? GetString(r, "No")
                           ?? GetString(r, "Document_No"))
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Cast<string>()
                .Distinct(StringComparer.Ordinal)
                .Take(50)
                .ToArray();

            var existing = await _context.IntegrationReviewQueueItems
                .Where(r =>
                    r.ConnectionId == connectionId &&
                    r.ProviderKey == providerKey &&
                    r.ItemType == "erp_sync_review" &&
                    r.SourceType == "erp_row" &&
                    r.SourceId != null &&
                    ids.Contains(r.SourceId) &&
                    (r.Status == IntegrationReviewQueueStatuses.Pending || r.Status == IntegrationReviewQueueStatuses.InReview))
                .ToDictionaryAsync(r => r.SourceId!, StringComparer.Ordinal, cancellationToken);

            var created = 0;
            foreach (var row in rows.Take(50))
            {
                var sourceId = GetString(row, "id")
                               ?? GetString(row, "Id")
                               ?? GetString(row, "number")
                               ?? GetString(row, "No")
                               ?? GetString(row, "Document_No");
                if (string.IsNullOrWhiteSpace(sourceId))
                {
                    continue;
                }

                var title = $"ERP row review: {providerKey}";
                var summary = $"{providerKey} row '{sourceId}' synced; review mapping/reconciliation applicability.";

                if (existing.TryGetValue(sourceId, out var review))
                {
                    review.Summary = Truncate(summary, 2048);
                    review.ContextJson = row.GetRawText();
                    review.UpdatedAt = DateTime.UtcNow;
                    continue;
                }

                _context.IntegrationReviewQueueItems.Add(new IntegrationReviewQueueItem
                {
                    Id = Guid.NewGuid().ToString(),
                    ConnectionId = connectionId,
                    ProviderKey = providerKey,
                    ItemType = "erp_sync_review",
                    SourceType = "erp_row",
                    SourceId = sourceId,
                    Status = IntegrationReviewQueueStatuses.Pending,
                    Priority = "medium",
                    Title = Truncate(title, 160),
                    Summary = Truncate(summary, 2048),
                    ContextJson = row.GetRawText(),
                    SuggestedActionsJson = JsonSerializer.Serialize(new object[]
                    {
                        new { action = "map_entity" },
                        new { action = "reconcile" }
                    }),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
                created++;
            }

            if (created > 0)
            {
                await _context.SaveChangesAsync(cancellationToken);
            }

            return created;
        }

        private async Task<int> QueueDocumentProviderMetadataPushReviewsAsync(
            string connectionId,
            string providerKey,
            CancellationToken cancellationToken)
        {
            var docs = await _context.Documents
                .AsNoTracking()
                .Where(d => d.UpdatedAt >= DateTime.UtcNow.AddDays(-14))
                .OrderByDescending(d => d.UpdatedAt)
                .Take(25)
                .Select(d => new { d.Id, d.Name, d.MatterId, d.FileName, d.UpdatedAt, d.Tags, d.Category })
                .ToListAsync(cancellationToken);

            if (docs.Count == 0)
            {
                return 0;
            }

            var docIds = docs.Select(d => d.Id).ToArray();
            var existing = await _context.IntegrationReviewQueueItems
                .Where(r =>
                    r.ConnectionId == connectionId &&
                    r.ProviderKey == providerKey &&
                    r.ItemType == "document_metadata_push_review" &&
                    r.SourceType == nameof(Document) &&
                    r.SourceId != null &&
                    docIds.Contains(r.SourceId) &&
                    (r.Status == IntegrationReviewQueueStatuses.Pending || r.Status == IntegrationReviewQueueStatuses.InReview))
                .ToDictionaryAsync(r => r.SourceId!, StringComparer.Ordinal, cancellationToken);

            var created = 0;
            foreach (var doc in docs)
            {
                if (existing.ContainsKey(doc.Id))
                {
                    continue;
                }

                _context.IntegrationReviewQueueItems.Add(new IntegrationReviewQueueItem
                {
                    Id = Guid.NewGuid().ToString(),
                    ConnectionId = connectionId,
                    ProviderKey = providerKey,
                    ItemType = "document_metadata_push_review",
                    SourceType = nameof(Document),
                    SourceId = doc.Id,
                    Status = IntegrationReviewQueueStatuses.Pending,
                    Priority = string.IsNullOrWhiteSpace(doc.MatterId) ? "medium" : "low",
                    Title = Truncate($"Push document metadata to {providerKey}", 160),
                    Summary = Truncate($"Review outbound metadata/workspace sync for document '{doc.Name}'.", 2048),
                    ContextJson = _piiMinimizer.SanitizeObjectForStorage(doc, $"{providerKey}:document_metadata_push_review"),
                    SuggestedActionsJson = JsonSerializer.Serialize(new object[]
                    {
                        new { action = "push_metadata" },
                        new { action = "ensure_workspace_link", matterId = doc.MatterId }
                    }),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
                created++;
            }

            if (created > 0)
            {
                await _context.SaveChangesAsync(cancellationToken);
            }

            return created;
        }

        private async Task<int> QueueDmsWorkspaceAndFilingWorkflowPushReviewsAsync(
            string connectionId,
            string providerKey,
            CancellationToken cancellationToken)
        {
            var now = DateTime.UtcNow;
            var recentDocs = await _context.Documents
                .AsNoTracking()
                .Where(d => d.MatterId != null && d.UpdatedAt >= now.AddDays(-30))
                .OrderByDescending(d => d.UpdatedAt)
                .Take(200)
                .Select(d => new
                {
                    d.Id,
                    d.MatterId,
                    d.Name,
                    d.FileName,
                    d.Category,
                    d.Status,
                    d.UpdatedAt
                })
                .ToListAsync(cancellationToken);

            var recentSubmissions = await _context.EfilingSubmissions
                .AsNoTracking()
                .Where(s => s.MatterId != null && s.UpdatedAt >= now.AddDays(-45))
                .OrderByDescending(s => s.UpdatedAt)
                .Take(100)
                .Select(s => new
                {
                    s.Id,
                    s.MatterId,
                    s.ProviderKey,
                    s.ExternalSubmissionId,
                    s.Status,
                    s.ReferenceNumber,
                    s.RejectionReason,
                    s.UpdatedAt,
                    s.AcceptedAt,
                    s.RejectedAt
                })
                .ToListAsync(cancellationToken);

            var matterIds = recentDocs.Select(d => d.MatterId!)
                .Concat(recentSubmissions.Select(s => s.MatterId!))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (matterIds.Length == 0)
            {
                return 0;
            }

            var matters = await _context.Matters
                .AsNoTracking()
                .Where(m => matterIds.Contains(m.Id))
                .Select(m => new { m.Id, m.CaseNumber, m.Name, m.CourtType, m.Status })
                .ToDictionaryAsync(m => m.Id, StringComparer.Ordinal, cancellationToken);

            var openReviews = await _context.IntegrationReviewQueueItems
                .Where(r =>
                    r.ConnectionId == connectionId &&
                    r.ProviderKey == providerKey &&
                    (r.Status == IntegrationReviewQueueStatuses.Pending || r.Status == IntegrationReviewQueueStatuses.InReview) &&
                    (r.ItemType == "dms_workspace_push_review" || r.ItemType == "dms_filing_packet_push_review"))
                .ToListAsync(cancellationToken);

            var workspaceExisting = openReviews
                .Where(r => r.ItemType == "dms_workspace_push_review" && r.SourceId != null)
                .ToDictionary(r => r.SourceId!, StringComparer.Ordinal);
            var filingExisting = openReviews
                .Where(r => r.ItemType == "dms_filing_packet_push_review" && r.SourceId != null)
                .ToDictionary(r => r.SourceId!, StringComparer.Ordinal);

            var created = 0;

            foreach (var matterGroup in recentDocs.GroupBy(d => d.MatterId!, StringComparer.Ordinal))
            {
                var matterId = matterGroup.Key;
                if (workspaceExisting.ContainsKey(matterId))
                {
                    continue;
                }

                matters.TryGetValue(matterId, out var matter);
                var sampleDocs = matterGroup
                    .OrderByDescending(d => d.UpdatedAt)
                    .Take(5)
                    .Select(d => new { d.Id, d.Name, d.FileName, d.Category, d.Status, d.UpdatedAt })
                    .ToList();

                _context.IntegrationReviewQueueItems.Add(new IntegrationReviewQueueItem
                {
                    Id = Guid.NewGuid().ToString(),
                    ConnectionId = connectionId,
                    ProviderKey = providerKey,
                    ItemType = "dms_workspace_push_review",
                    SourceType = nameof(Matter),
                    SourceId = matterId,
                    Status = IntegrationReviewQueueStatuses.Pending,
                    Priority = "medium",
                    Title = Truncate($"DMS workspace sync review ({providerKey})", 160),
                    Summary = Truncate($"Review workspace/document sync plan for matter {(matter?.CaseNumber ?? matterId)} with {matterGroup.Count()} recent documents.", 2048),
                    ContextJson = _piiMinimizer.SanitizeObjectForStorage(new
                    {
                        matterId,
                        matter?.CaseNumber,
                        matter?.Name,
                        matter?.CourtType,
                        matterStatus = matter?.Status,
                        documentCount = matterGroup.Count(),
                        sampleDocuments = sampleDocs
                    }, $"{providerKey}:dms_workspace_push_review"),
                    SuggestedActionsJson = JsonSerializer.Serialize(new object[]
                    {
                        new { action = "ensure_workspace_link", matterId },
                        new { action = "push_document_metadata_batch", matterId },
                        new { action = "open_matter_documents", matterId }
                    }),
                    CreatedAt = now,
                    UpdatedAt = now
                });
                created++;
            }

            var docCountsByMatter = recentDocs
                .GroupBy(d => d.MatterId!, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

            foreach (var submission in recentSubmissions)
            {
                if (filingExisting.ContainsKey(submission.Id))
                {
                    continue;
                }

                matters.TryGetValue(submission.MatterId!, out var matter);
                docCountsByMatter.TryGetValue(submission.MatterId!, out var matterDocCount);

                var priority = submission.Status switch
                {
                    "rejected" => "high",
                    "accepted" => "medium",
                    "submitted" => "medium",
                    "corrected" => "medium",
                    _ => "low"
                };

                _context.IntegrationReviewQueueItems.Add(new IntegrationReviewQueueItem
                {
                    Id = Guid.NewGuid().ToString(),
                    ConnectionId = connectionId,
                    ProviderKey = providerKey,
                    ItemType = "dms_filing_packet_push_review",
                    SourceType = nameof(EfilingSubmission),
                    SourceId = submission.Id,
                    Status = IntegrationReviewQueueStatuses.Pending,
                    Priority = priority,
                    Title = Truncate($"DMS filing packet workflow review ({providerKey})", 160),
                    Summary = Truncate($"Review matter-bound filing packet sync for submission {submission.ExternalSubmissionId} ({submission.Status}).", 2048),
                    ContextJson = _piiMinimizer.SanitizeObjectForStorage(new
                    {
                        submission.Id,
                        submission.ProviderKey,
                        submission.ExternalSubmissionId,
                        submission.ReferenceNumber,
                        submission.Status,
                        submission.RejectionReason,
                        submission.AcceptedAt,
                        submission.RejectedAt,
                        submission.UpdatedAt,
                        submission.MatterId,
                        matter?.CaseNumber,
                        matter?.Name,
                        matterDocumentCount = matterDocCount
                    }, $"{providerKey}:dms_filing_packet_push_review"),
                    SuggestedActionsJson = JsonSerializer.Serialize(new object[]
                    {
                        new { action = "ensure_workspace_link", matterId = submission.MatterId },
                        new { action = "push_filing_packet", submissionId = submission.Id },
                        new { action = "attach_notice_or_stamped_copy", submissionId = submission.Id }
                    }),
                    CreatedAt = now,
                    UpdatedAt = now
                });
                created++;
            }

            if (created > 0)
            {
                await _context.SaveChangesAsync(cancellationToken);
            }

            return created;
        }

        private async Task<int> QueueErpPushReviewPlanAsync(
            string connectionId,
            string providerKey,
            CancellationToken cancellationToken)
        {
            var existing = await _context.IntegrationReviewQueueItems
                .FirstOrDefaultAsync(r =>
                    r.ConnectionId == connectionId &&
                    r.ProviderKey == providerKey &&
                    r.ItemType == "erp_push_batch_review" &&
                    r.SourceType == "erp_push_plan" &&
                    r.SourceId == connectionId &&
                    (r.Status == IntegrationReviewQueueStatuses.Pending || r.Status == IntegrationReviewQueueStatuses.InReview),
                    cancellationToken);

            var invoiceCount = await _context.Invoices
                .AsNoTracking()
                .Where(i => i.UpdatedAt >= DateTime.UtcNow.AddDays(-30))
                .CountAsync(cancellationToken);
            var paymentCount = await _context.PaymentTransactions
                .AsNoTracking()
                .Where(p => p.UpdatedAt >= DateTime.UtcNow.AddDays(-30))
                .CountAsync(cancellationToken);
            var clientCount = await _context.Clients
                .AsNoTracking()
                .Where(c => c.UpdatedAt >= DateTime.UtcNow.AddDays(-30))
                .CountAsync(cancellationToken);

            var summary = $"Prepare ERP push batch for {providerKey}. Invoices={invoiceCount}, Payments={paymentCount}, Clients={clientCount}.";
            var contextJson = JsonSerializer.Serialize(new { invoiceCount, paymentCount, clientCount, providerKey });

            if (existing != null)
            {
                existing.Summary = Truncate(summary, 2048);
                existing.ContextJson = contextJson;
                existing.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync(cancellationToken);
                return 0;
            }

            _context.IntegrationReviewQueueItems.Add(new IntegrationReviewQueueItem
            {
                Id = Guid.NewGuid().ToString(),
                ConnectionId = connectionId,
                ProviderKey = providerKey,
                ItemType = "erp_push_batch_review",
                SourceType = "erp_push_plan",
                SourceId = connectionId,
                Status = IntegrationReviewQueueStatuses.Pending,
                Priority = "high",
                Title = Truncate($"ERP push review plan ({providerKey})", 160),
                Summary = Truncate(summary, 2048),
                ContextJson = contextJson,
                SuggestedActionsJson = JsonSerializer.Serialize(new object[]
                {
                    new { action = "configure_mapping_profile" },
                    new { action = "approve_push_batch" }
                }),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync(cancellationToken);
            return 1;
        }

        private static List<ProviderDocumentCatalogEntry> ResolveGenericDocumentCatalogEntries(JsonElement root)
        {
            var rows = ResolveSubmissionRows(root);
            var result = new List<ProviderDocumentCatalogEntry>(rows.Count);
            foreach (var row in rows)
            {
                var id = GetString(row, "id")
                         ?? GetString(row, "Id")
                         ?? GetString(row, "documentId")
                         ?? GetString(row, "DocumentId");
                var name = GetString(row, "name")
                           ?? GetString(row, "Name")
                           ?? GetString(row, "filename")
                           ?? GetString(row, "FileName")
                           ?? GetString(row, "title");
                var isDeleted = IsDeletedDocumentCatalogRow(row);
                if (string.IsNullOrWhiteSpace(id) || (string.IsNullOrWhiteSpace(name) && !isDeleted))
                {
                    continue;
                }

                result.Add(new ProviderDocumentCatalogEntry
                {
                    ExternalId = id,
                    Name = string.IsNullOrWhiteSpace(name) ? "(deleted item)" : name,
                    MimeType = GetString(row, "mimeType") ?? GetString(row, "contentType"),
                    SizeBytes = GetLong(row, "size") ?? GetLong(row, "fileSize") ?? 0,
                    ModifiedAt = ParseProviderDateTime(GetString(row, "modifiedTime")
                                                      ?? GetString(row, "lastModifiedDateTime")
                                                      ?? GetString(row, "updatedAt")),
                    WebUrl = GetString(row, "webUrl") ?? GetString(row, "url"),
                    ParentReference = GetString(row, "parentPath") ?? GetString(row, "folder"),
                    IsFolder = string.Equals(GetString(row, "type"), "folder", StringComparison.OrdinalIgnoreCase),
                    IsDeleted = isDeleted
                });
            }

            return result;
        }

        private (string? baseUrl, string? listPath) ResolveGenericProviderEndpoints(string configPrefix, string? defaultListPath)
        {
            var baseUrl = _configuration[$"{configPrefix}:ApiBaseUrl"]?.TrimEnd('/');
            var listPath = _configuration[$"{configPrefix}:ListPath"];
            if (string.IsNullOrWhiteSpace(listPath))
            {
                listPath = defaultListPath;
            }

            return (baseUrl, listPath);
        }

        private string? ResolveGenericProviderValidatePath(string configPrefix)
        {
            return _configuration[$"{configPrefix}:ValidatePath"] ?? _configuration[$"{configPrefix}:PingPath"];
        }

        private (string? authScheme, string? authToken, IReadOnlyDictionary<string, string>? additionalHeaders) ResolveGenericProviderAuth(
            string configPrefix,
            IntegrationMetadataEnvelope metadata)
        {
            if (!string.IsNullOrWhiteSpace(metadata.Credentials.AccessToken))
            {
                return ("Bearer", metadata.Credentials.AccessToken, null);
            }

            if (string.IsNullOrWhiteSpace(metadata.Credentials.ApiKey))
            {
                return (null, null, null);
            }

            var apiKey = metadata.Credentials.ApiKey!.Trim();
            var headerName = _configuration[$"{configPrefix}:ApiKeyHeaderName"];
            var apiKeyScheme = _configuration[$"{configPrefix}:ApiKeyScheme"];

            if (string.IsNullOrWhiteSpace(headerName) || string.Equals(headerName, "Authorization", StringComparison.OrdinalIgnoreCase))
            {
                return (string.IsNullOrWhiteSpace(apiKeyScheme) ? "Bearer" : apiKeyScheme.Trim(), apiKey, null);
            }

            return (null, null, new Dictionary<string, string> { [headerName.Trim()] = apiKey });
        }

        private async Task<IntegrationConnectResult> ValidateGenericRestProviderAsync(
            string providerDisplayName,
            string configPrefix,
            IntegrationMetadataEnvelope metadata,
            string category,
            CancellationToken cancellationToken)
        {
            var baseUrl = _configuration[$"{configPrefix}:ApiBaseUrl"]?.TrimEnd('/');
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                return new IntegrationConnectResult
                {
                    Success = false,
                    Status = "error",
                    ErrorMessage = $"{providerDisplayName} API base URL is not configured."
                };
            }

            var auth = ResolveGenericProviderAuth(configPrefix, metadata);
            if (string.IsNullOrWhiteSpace(auth.authScheme) && string.IsNullOrWhiteSpace(auth.authToken) && auth.additionalHeaders == null)
            {
                return new IntegrationConnectResult
                {
                    Success = false,
                    Status = "error",
                    ErrorMessage = $"{providerDisplayName} credentials are missing (access token or API key)."
                };
            }

            var validatePath = ResolveGenericProviderValidatePath(configPrefix)
                               ?? _configuration[$"{configPrefix}:ListPath"];
            if (string.IsNullOrWhiteSpace(validatePath))
            {
                return new IntegrationConnectResult
                {
                    Success = false,
                    Status = "error",
                    ErrorMessage = $"{providerDisplayName} validate/list path is not configured."
                };
            }

            using var doc = await GetJsonAsync(
                $"{baseUrl}{NormalizeApiPath(validatePath)}",
                auth.authScheme,
                auth.authToken,
                auth.additionalHeaders,
                cancellationToken);

            var accountLabel = providerDisplayName;
            var accountEmail = GetString(doc.RootElement, "email")
                               ?? GetString(doc.RootElement, "userEmail")
                               ?? GetNestedString(doc.RootElement, "user", "email");
            var externalAccountId = GetString(doc.RootElement, "id")
                                    ?? GetString(doc.RootElement, "tenantId")
                                    ?? GetString(doc.RootElement, "companyId");

            metadata.Credentials.AccountEmail = accountEmail ?? metadata.Credentials.AccountEmail;
            metadata.Credentials.ExternalAccountId = externalAccountId ?? metadata.Credentials.ExternalAccountId;

            return new IntegrationConnectResult
            {
                Success = true,
                Status = "connected",
                AccountLabel = accountLabel,
                AccountEmail = metadata.Credentials.AccountEmail,
                ExternalAccountId = metadata.Credentials.ExternalAccountId,
                Message = $"{providerDisplayName} {category} connector validated."
            };
        }

        private async Task<(bool hasLockedRecords, int lockedInvoices, int lockedPayments)> EvaluateAccountingPushPeriodLockAsync(
            string connectionId,
            CancellationToken cancellationToken)
        {
            return await EvaluateAccountingPushPeriodLockAsync(
                connectionId,
                IntegrationProviderKeys.QuickBooksOnline,
                cancellationToken);
        }

        private async Task<(bool hasLockedRecords, int lockedInvoices, int lockedPayments)> EvaluateAccountingPushPeriodLockAsync(
            string connectionId,
            string providerKey,
            CancellationToken cancellationToken)
        {
            var lockRanges = await LoadBillingLockRangesAsync(cancellationToken);
            if (lockRanges.Count == 0)
            {
                return (false, 0, 0);
            }

            var since = DateTime.UtcNow.AddDays(-30);
            var candidateInvoices = await _context.Invoices
                .Where(i => i.UpdatedAt >= since && i.Total > 0)
                .Select(i => new { i.Id, i.IssueDate })
                .ToListAsync(cancellationToken);

            var candidatePayments = await _context.PaymentTransactions
                .Where(p => p.Status == "Succeeded" &&
                            p.InvoiceId != null &&
                            p.UpdatedAt >= since)
                .Select(p => new { p.Id, p.Source, p.ProcessedAt, p.CreatedAt })
                .ToListAsync(cancellationToken);

            var lockedInvoices = candidateInvoices.Count(i => IsBillingPeriodLocked(i.IssueDate, lockRanges));
            var lockedPayments = candidatePayments.Count(p =>
                !IsProviderAuthoredAccountingPaymentSource(p.Source, providerKey) &&
                IsBillingPeriodLocked(p.ProcessedAt ?? p.CreatedAt, lockRanges));

            return (lockedInvoices > 0 || lockedPayments > 0, lockedInvoices, lockedPayments);
        }

        private async Task EnsureAccountingMappingCoverageConflictsAsync(
            string connectionId,
            string providerKey,
            CancellationToken cancellationToken)
        {
            var normalizedProviderKey = (providerKey ?? string.Empty).Trim().ToLowerInvariant();
            if (normalizedProviderKey is not ("quickbooks-online" or "xero" or "microsoft-business-central" or "netsuite"))
            {
                return;
            }

            var profiles = await _context.IntegrationMappingProfiles
                .Where(p => p.ConnectionId == connectionId &&
                            p.ProviderKey == normalizedProviderKey &&
                            p.Status == "active")
                .OrderByDescending(p => p.IsDefault)
                .ThenBy(p => p.EntityType)
                .ToListAsync(cancellationToken);

            var existingOpen = await _context.IntegrationConflictQueueItems
                .Where(c => c.ConnectionId == connectionId &&
                            c.ProviderKey == normalizedProviderKey &&
                            (c.Status == IntegrationConflictStatuses.Open || c.Status == IntegrationConflictStatuses.InReview))
                .ToDictionaryAsync(c => c.Fingerprint ?? c.Id, StringComparer.OrdinalIgnoreCase, cancellationToken);

            var createdOrUpdated = 0;

            createdOrUpdated += EnsureRequiredMappingProfileConflict(
                profiles,
                existingOpen,
                connectionId,
                normalizedProviderKey,
                "invoice",
                requireTaxMappings: true,
                requireTrustOperatingSplit: false);

            createdOrUpdated += EnsureRequiredMappingProfileConflict(
                profiles,
                existingOpen,
                connectionId,
                normalizedProviderKey,
                "payment",
                requireTaxMappings: false,
                requireTrustOperatingSplit: true);

            createdOrUpdated += EnsureRequiredMappingProfileConflict(
                profiles,
                existingOpen,
                connectionId,
                normalizedProviderKey,
                "customer",
                requireTaxMappings: false,
                requireTrustOperatingSplit: false);

            if (createdOrUpdated > 0)
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
        }

        private int EnsureRequiredMappingProfileConflict(
            IReadOnlyCollection<IntegrationMappingProfile> profiles,
            IDictionary<string, IntegrationConflictQueueItem> existingOpen,
            string connectionId,
            string providerKey,
            string entityType,
            bool requireTaxMappings,
            bool requireTrustOperatingSplit)
        {
            var profile = profiles.FirstOrDefault(p =>
                string.Equals(p.EntityType, entityType, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(p.Direction, "both", StringComparison.OrdinalIgnoreCase) &&
                p.IsDefault)
                ?? profiles.FirstOrDefault(p =>
                    string.Equals(p.EntityType, entityType, StringComparison.OrdinalIgnoreCase) &&
                    p.IsDefault)
                ?? profiles.FirstOrDefault(p =>
                    string.Equals(p.EntityType, entityType, StringComparison.OrdinalIgnoreCase));

            if (profile == null)
            {
                return QueueOrRefreshConflict(
                    existingOpen,
                    connectionId,
                    providerKey,
                    runId: null,
                    entityType: "mapping_profile",
                    localEntityId: $"{entityType}:default",
                    externalEntityId: null,
                    conflictType: "mapping_profile_missing",
                    severity: "high",
                    summary: $"Default {entityType} mapping profile is missing for {providerKey} accounting push/reconcile.",
                    localSnapshotJson: null,
                    externalSnapshotJson: null,
                    suggestedResolutionJson: JsonSerializer.Serialize(new
                    {
                        action = "configure_mapping_profile",
                        providerKey,
                        entityType,
                        sourceOfTruth = ResolveAccountingSourceOfTruth(entityType)
                    }),
                    sourceHint: $"mapping_profile_missing:{entityType}");
            }

            var createdOrUpdated = 0;
            if (!TryParseJson(profile.AccountMappingsJson, out var accountMappings))
            {
                createdOrUpdated += QueueOrRefreshConflict(
                    existingOpen,
                    connectionId,
                    providerKey,
                    runId: null,
                    entityType: "mapping_profile",
                    localEntityId: profile.Id,
                    externalEntityId: null,
                    conflictType: "account_mapping_missing",
                    severity: "high",
                    summary: $"{entityType} mapping profile '{profile.Name}' is missing valid account mappings JSON.",
                    localSnapshotJson: profile.MetadataJson,
                    externalSnapshotJson: null,
                    suggestedResolutionJson: JsonSerializer.Serialize(new
                    {
                        action = "configure_account_mappings",
                        profileId = profile.Id,
                        required = entityType == "payment"
                            ? new[] { "operatingClearing", "trustLiability" }
                            : new[] { "accountsReceivable", "income" }
                    }),
                    sourceHint: $"account_mapping_missing:{entityType}");
            }
            else if (requireTrustOperatingSplit)
            {
                var hasTrust = accountMappings.ValueKind == JsonValueKind.Object &&
                               (accountMappings.TryGetProperty("trustLiability", out _) ||
                                accountMappings.TryGetProperty("trustliability", out _));
                var hasOperating = accountMappings.ValueKind == JsonValueKind.Object &&
                                   (accountMappings.TryGetProperty("operatingClearing", out _) ||
                                    accountMappings.TryGetProperty("operatingBank", out _) ||
                                    accountMappings.TryGetProperty("operating", out _));
                if (!hasTrust || !hasOperating)
                {
                    createdOrUpdated += QueueOrRefreshConflict(
                        existingOpen,
                        connectionId,
                        providerKey,
                        runId: null,
                        entityType: "mapping_profile",
                        localEntityId: profile.Id,
                        externalEntityId: null,
                        conflictType: "trust_operating_mapping_missing",
                        severity: "high",
                        summary: $"Payment mapping profile '{profile.Name}' must define trust and operating account mappings (IOLTA guardrail).",
                        localSnapshotJson: profile.AccountMappingsJson,
                        externalSnapshotJson: null,
                        suggestedResolutionJson: JsonSerializer.Serialize(new
                        {
                            action = "configure_account_mappings",
                            profileId = profile.Id,
                            required = new[] { "trustLiability", "operatingClearing" }
                        }),
                        sourceHint: "trust_operating_mapping_missing");
                }
            }

            if (requireTaxMappings && !TryParseJson(profile.TaxMappingsJson, out _))
            {
                createdOrUpdated += QueueOrRefreshConflict(
                    existingOpen,
                    connectionId,
                    providerKey,
                    runId: null,
                    entityType: "mapping_profile",
                    localEntityId: profile.Id,
                    externalEntityId: null,
                    conflictType: "tax_mapping_missing",
                    severity: "medium",
                    summary: $"Invoice mapping profile '{profile.Name}' is missing valid tax mappings JSON.",
                    localSnapshotJson: profile.MetadataJson,
                    externalSnapshotJson: null,
                    suggestedResolutionJson: JsonSerializer.Serialize(new
                    {
                        action = "configure_tax_mappings",
                        profileId = profile.Id
                    }),
                    sourceHint: $"tax_mapping_missing:{entityType}");
            }

            return createdOrUpdated;
        }

        private async Task<SplitReceivablePolicyResolution> ResolveAccountingSplitReceivablePolicyAsync(
            string connectionId,
            string providerKey,
            Invoice invoice,
            IDictionary<string, IntegrationConflictQueueItem> existingOpen,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(invoice.Id))
            {
                return SplitReceivablePolicyResolution.Allow(invoice.ClientId, null, "single_payor");
            }

            var activeAllocations = await _context.InvoicePayorAllocations.AsNoTracking()
                .Where(a => a.InvoiceId == invoice.Id && a.Status == "active")
                .OrderBy(a => a.Priority)
                .ThenByDescending(a => a.IsPrimary)
                .ToListAsync(cancellationToken);

            if (activeAllocations.Count <= 1)
            {
                var singlePayor = activeAllocations.FirstOrDefault()?.PayorClientId ?? invoice.ClientId;
                return SplitReceivablePolicyResolution.Allow(singlePayor, activeAllocations.FirstOrDefault()?.Id, "single_payor");
            }

            if (string.IsNullOrWhiteSpace(invoice.MatterId))
            {
                QueueOrRefreshConflict(
                    existingOpen,
                    connectionId,
                    providerKey,
                    runId: null,
                    entityType: "invoice",
                    localEntityId: invoice.Id,
                    externalEntityId: null,
                    conflictType: "split_receivable_policy_required",
                    severity: "high",
                    summary: $"Invoice {invoice.Number ?? invoice.Id} has split payors but no MatterId to resolve accounting split receivable policy.",
                    localSnapshotJson: JsonSerializer.Serialize(new { invoice.Id, invoice.Number, invoice.ClientId, payorAllocationCount = activeAllocations.Count }),
                    externalSnapshotJson: null,
                    suggestedResolutionJson: JsonSerializer.Serialize(new { action = "configure_matter_billing_policy", providerKey, invoiceId = invoice.Id, mode = "primary_payor_single_receivable" }),
                    sourceHint: $"split_receivable_policy_missing_matter:{invoice.Id}");
                return SplitReceivablePolicyResolution.Block("split_policy_missing_matter");
            }

            var policy = await _context.MatterBillingPolicies.AsNoTracking()
                .Where(p => p.MatterId == invoice.MatterId && p.Status == "active")
                .OrderByDescending(p => p.EffectiveFrom)
                .FirstOrDefaultAsync(cancellationToken);

            var mode = ResolveAccountingSplitReceivableMode(policy?.CollectionPolicyJson, providerKey);
            if (string.IsNullOrWhiteSpace(mode))
            {
                QueueOrRefreshConflict(
                    existingOpen,
                    connectionId,
                    providerKey,
                    runId: null,
                    entityType: "invoice",
                    localEntityId: invoice.Id,
                    externalEntityId: null,
                    conflictType: "split_receivable_policy_required",
                    severity: "high",
                    summary: $"Split-billed invoice {invoice.Number ?? invoice.Id} requires an accounting split receivable policy for {providerKey}.",
                    localSnapshotJson: JsonSerializer.Serialize(new { invoice.Id, invoice.Number, invoice.MatterId, payorAllocationCount = activeAllocations.Count, policyId = policy?.Id }),
                    externalSnapshotJson: policy?.CollectionPolicyJson,
                    suggestedResolutionJson: JsonSerializer.Serialize(new
                    {
                        action = "configure_collection_policy",
                        matterId = invoice.MatterId,
                        providerKey,
                        supportedModes = new[] { "primary_payor_single_receivable", "review_only" }
                    }),
                    sourceHint: $"split_receivable_policy_required:{invoice.Id}");
                return SplitReceivablePolicyResolution.Block("split_policy_required");
            }

            var normalizedMode = mode.Trim().ToLowerInvariant();
            if (normalizedMode is "review_only" or "block" or "manual")
            {
                QueueOrRefreshConflict(
                    existingOpen,
                    connectionId,
                    providerKey,
                    runId: null,
                    entityType: "invoice",
                    localEntityId: invoice.Id,
                    externalEntityId: null,
                    conflictType: "split_receivable_policy_manual_review",
                    severity: "medium",
                    summary: $"Split-billed invoice {invoice.Number ?? invoice.Id} is configured for manual/review-only accounting posting ({normalizedMode}).",
                    localSnapshotJson: JsonSerializer.Serialize(new { invoice.Id, invoice.Number, invoice.MatterId, payorAllocationCount = activeAllocations.Count, mode = normalizedMode }),
                    externalSnapshotJson: policy?.CollectionPolicyJson,
                    suggestedResolutionJson: JsonSerializer.Serialize(new { action = "review", entityType = "invoice", localEntityId = invoice.Id, providerKey }),
                    sourceHint: $"split_receivable_policy_review:{invoice.Id}:{normalizedMode}");
                return SplitReceivablePolicyResolution.Block(normalizedMode);
            }

            if (normalizedMode is "per_payor_receivable" or "mirror_per_payor" or "split_receivable_per_payor")
            {
                QueueOrRefreshConflict(
                    existingOpen,
                    connectionId,
                    providerKey,
                    runId: null,
                    entityType: "invoice",
                    localEntityId: invoice.Id,
                    externalEntityId: null,
                    conflictType: "split_receivable_policy_unsupported",
                    severity: "high",
                    summary: $"Split receivable mode '{normalizedMode}' is not yet supported for {providerKey} connector runtime.",
                    localSnapshotJson: JsonSerializer.Serialize(new { invoice.Id, invoice.Number, invoice.MatterId, payorAllocationCount = activeAllocations.Count }),
                    externalSnapshotJson: policy?.CollectionPolicyJson,
                    suggestedResolutionJson: JsonSerializer.Serialize(new { action = "use_primary_payor_single_receivable", providerKey, matterId = invoice.MatterId }),
                    sourceHint: $"split_receivable_policy_unsupported:{invoice.Id}:{normalizedMode}");
                return SplitReceivablePolicyResolution.Block(normalizedMode);
            }

            if (normalizedMode is "primary" or "primary_payor_single_receivable" or "single_customer_summary")
            {
                var primary = activeAllocations.FirstOrDefault(a => a.IsPrimary)
                             ?? activeAllocations.FirstOrDefault(a => string.Equals(a.ResponsibilityType, "primary", StringComparison.OrdinalIgnoreCase))
                             ?? activeAllocations.First();
                return SplitReceivablePolicyResolution.Allow(primary.PayorClientId, primary.Id, normalizedMode);
            }

            QueueOrRefreshConflict(
                existingOpen,
                connectionId,
                providerKey,
                runId: null,
                entityType: "invoice",
                localEntityId: invoice.Id,
                externalEntityId: null,
                conflictType: "split_receivable_policy_invalid",
                severity: "high",
                summary: $"Split receivable policy mode '{normalizedMode}' is invalid for {providerKey}.",
                localSnapshotJson: JsonSerializer.Serialize(new { invoice.Id, invoice.Number, invoice.MatterId, payorAllocationCount = activeAllocations.Count }),
                externalSnapshotJson: policy?.CollectionPolicyJson,
                suggestedResolutionJson: JsonSerializer.Serialize(new { action = "configure_collection_policy", providerKey, supportedModes = new[] { "primary_payor_single_receivable", "review_only" } }),
                sourceHint: $"split_receivable_policy_invalid:{invoice.Id}:{normalizedMode}");
            return SplitReceivablePolicyResolution.Block(normalizedMode);
        }

        private static string? ResolveAccountingSplitReceivableMode(string? collectionPolicyJson, string providerKey)
        {
            if (!TryParseJson(collectionPolicyJson, out var root) || root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (TryResolveSplitPolicyModeFromNode(root, providerKey, out var mode))
            {
                return mode;
            }

            if (root.TryGetProperty("accountingSplitReceivablePolicy", out var accountingNode) &&
                TryResolveSplitPolicyModeFromNode(accountingNode, providerKey, out mode))
            {
                return mode;
            }

            if (root.TryGetProperty("splitReceivablePolicy", out var splitNode) &&
                TryResolveSplitPolicyModeFromNode(splitNode, providerKey, out mode))
            {
                return mode;
            }

            return null;
        }

        private static bool TryResolveSplitPolicyModeFromNode(JsonElement node, string providerKey, out string? mode)
        {
            mode = null;
            if (node.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (TryGetStringPropertyCaseInsensitive(node, "mode", out mode) && !string.IsNullOrWhiteSpace(mode))
            {
                return true;
            }

            if (TryGetPropertyCaseInsensitive(node, providerKey, out var providerNode))
            {
                if (providerNode.ValueKind == JsonValueKind.String)
                {
                    mode = providerNode.GetString();
                    return !string.IsNullOrWhiteSpace(mode);
                }

                if (providerNode.ValueKind == JsonValueKind.Object &&
                    TryGetStringPropertyCaseInsensitive(providerNode, "mode", out mode) &&
                    !string.IsNullOrWhiteSpace(mode))
                {
                    return true;
                }
            }

            if (TryGetPropertyCaseInsensitive(node, "providers", out var providersNode) &&
                providersNode.ValueKind == JsonValueKind.Object &&
                TryGetPropertyCaseInsensitive(providersNode, providerKey, out var providerPolicyNode))
            {
                if (providerPolicyNode.ValueKind == JsonValueKind.String)
                {
                    mode = providerPolicyNode.GetString();
                    return !string.IsNullOrWhiteSpace(mode);
                }

                if (providerPolicyNode.ValueKind == JsonValueKind.Object &&
                    TryGetStringPropertyCaseInsensitive(providerPolicyNode, "mode", out mode) &&
                    !string.IsNullOrWhiteSpace(mode))
                {
                    return true;
                }
            }

            if (TryGetPropertyCaseInsensitive(node, "default", out var defaultNode))
            {
                if (defaultNode.ValueKind == JsonValueKind.String)
                {
                    mode = defaultNode.GetString();
                    return !string.IsNullOrWhiteSpace(mode);
                }

                if (defaultNode.ValueKind == JsonValueKind.Object &&
                    TryGetStringPropertyCaseInsensitive(defaultNode, "mode", out mode) &&
                    !string.IsNullOrWhiteSpace(mode))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetPropertyCaseInsensitive(JsonElement node, string propertyName, out JsonElement value)
        {
            value = default;
            if (node.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            foreach (var prop in node.EnumerateObject())
            {
                if (string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = prop.Value;
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetStringPropertyCaseInsensitive(JsonElement node, string propertyName, out string? value)
        {
            value = null;
            if (!TryGetPropertyCaseInsensitive(node, propertyName, out var propertyValue))
            {
                return false;
            }

            if (propertyValue.ValueKind == JsonValueKind.String)
            {
                value = propertyValue.GetString();
                return true;
            }

            return false;
        }

        private readonly record struct SplitReceivablePolicyResolution(
            bool BlockOutbound,
            string? BillToClientId,
            string? InvoicePayorAllocationId,
            string? Mode)
        {
            public static SplitReceivablePolicyResolution Allow(string? billToClientId, string? invoicePayorAllocationId, string? mode) =>
                new(false, billToClientId, invoicePayorAllocationId, mode);

            public static SplitReceivablePolicyResolution Block(string? mode) =>
                new(true, null, null, mode);
        }

        private async Task<int> GenerateStripeAccountingBridgeConflictsAsync(
            string stripeConnectionId,
            CancellationToken cancellationToken)
        {
            var providerKey = IntegrationProviderKeys.Stripe;
            var since = DateTime.UtcNow.AddDays(-90);

            var stripePayments = await _context.PaymentTransactions
                .Where(p => p.PaymentMethod == "Stripe" &&
                            p.Amount > 0 &&
                            p.CreatedAt >= since &&
                            (p.Status == "Succeeded" || p.Status == "Partially Refunded" || p.Status == "Refunded"))
                .ToListAsync(cancellationToken);

            var existingOpen = await _context.IntegrationConflictQueueItems
                .Where(c => c.ConnectionId == stripeConnectionId &&
                            c.ProviderKey == providerKey &&
                            (c.Status == IntegrationConflictStatuses.Open || c.Status == IntegrationConflictStatuses.InReview))
                .ToDictionaryAsync(c => c.Fingerprint ?? c.Id, StringComparer.OrdinalIgnoreCase, cancellationToken);

            var accountingConnections = await _context.IntegrationConnections
                .Where(c => (c.ProviderKey == IntegrationProviderKeys.QuickBooksOnline || c.ProviderKey == IntegrationProviderKeys.Xero) &&
                            c.Status == "connected")
                .Select(c => new { c.Id, c.ProviderKey, c.SyncEnabled })
                .ToListAsync(cancellationToken);
            var accountingConnectionIds = accountingConnections.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);

            var createdOrUpdated = 0;
            if (accountingConnections.Count == 0)
            {
                createdOrUpdated += QueueOrRefreshConflict(
                    existingOpen,
                    stripeConnectionId,
                    providerKey,
                    runId: null,
                    entityType: "payment",
                    localEntityId: "stripe-bridge",
                    externalEntityId: null,
                    conflictType: "missing_accounting_bridge_target",
                    severity: "high",
                    summary: "Stripe reconciliation bridge has no connected accounting target (QuickBooks/Xero).",
                    localSnapshotJson: JsonSerializer.Serialize(new { stripePaymentCount = stripePayments.Count }),
                    externalSnapshotJson: null,
                    suggestedResolutionJson: JsonSerializer.Serialize(new { action = "connect_accounting_provider", providers = new[] { "quickbooks-online", "xero" } }),
                    sourceHint: "stripe_bridge_no_target");

                if (createdOrUpdated > 0)
                {
                    await _context.SaveChangesAsync(cancellationToken);
                }

                return await CountOpenConflictsForConnectionAsync(stripeConnectionId, cancellationToken);
            }

            var localPaymentIds = stripePayments.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
            var accountingLinks = await _context.IntegrationEntityLinks
                .Where(l => accountingConnectionIds.Contains(l.ConnectionId) &&
                            l.LocalEntityType == "payment" &&
                            localPaymentIds.Contains(l.LocalEntityId) &&
                            (l.ProviderKey == IntegrationProviderKeys.QuickBooksOnline || l.ProviderKey == IntegrationProviderKeys.Xero))
                .ToListAsync(cancellationToken);

            var linksByPaymentId = accountingLinks
                .GroupBy(l => l.LocalEntityId)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

            foreach (var payment in stripePayments)
            {
                if (!linksByPaymentId.TryGetValue(payment.Id, out var paymentLinks) || paymentLinks.Count == 0)
                {
                    createdOrUpdated += QueueOrRefreshConflict(
                        existingOpen,
                        stripeConnectionId,
                        providerKey,
                        runId: null,
                        entityType: "payment",
                        localEntityId: payment.Id,
                        externalEntityId: payment.ExternalTransactionId ?? payment.ProviderPaymentIntentId,
                        conflictType: "missing_accounting_posting",
                        severity: "high",
                        summary: $"Stripe payment {payment.Id} is not linked to an accounting posting (QBO/Xero).",
                        localSnapshotJson: JsonSerializer.Serialize(new
                        {
                            payment.Id,
                            payment.InvoiceId,
                            payment.Amount,
                            payment.RefundAmount,
                            payment.Currency,
                            payment.Status,
                            sourceOfTruth = ResolveAccountingSourceOfTruth("payment")
                        }),
                        externalSnapshotJson: null,
                        suggestedResolutionJson: JsonSerializer.Serialize(new
                        {
                            action = "push",
                            entityType = "payment",
                            localEntityId = payment.Id,
                            sourceOfTruth = ResolveAccountingSourceOfTruth("payment")
                        }),
                        sourceHint: "stripe_missing_accounting_link");
                    continue;
                }

                var qboLink = paymentLinks.FirstOrDefault(l => l.ProviderKey == IntegrationProviderKeys.QuickBooksOnline);
                if (qboLink != null && TryParseJson(qboLink.MetadataJson, out var qboPaymentSnapshot))
                {
                    var remoteTotal = NormalizeConnectorMoney(GetDecimal(qboPaymentSnapshot, "TotalAmt") ?? 0m);
                    var localTotal = NormalizeConnectorMoney(payment.Amount);
                    if (remoteTotal > 0m && remoteTotal != localTotal)
                    {
                        createdOrUpdated += QueueOrRefreshConflict(
                            existingOpen,
                            stripeConnectionId,
                            providerKey,
                            runId: null,
                            entityType: "payment",
                            localEntityId: payment.Id,
                            externalEntityId: qboLink.ExternalEntityId,
                            conflictType: "accounting_amount_mismatch",
                            severity: "high",
                            summary: "Stripe payment amount does not match QuickBooks posted payment amount.",
                            localSnapshotJson: JsonSerializer.Serialize(new { payment.Id, amount = localTotal, payment.Status, payment.RefundAmount }),
                            externalSnapshotJson: qboPaymentSnapshot.GetRawText(),
                            suggestedResolutionJson: JsonSerializer.Serialize(new { action = "reconcile", entityType = "payment", localEntityId = payment.Id }),
                            sourceHint: "stripe_qbo_amount_mismatch");
                    }
                }

                if ((payment.RefundAmount ?? 0m) > 0m)
                {
                    createdOrUpdated += QueueOrRefreshConflict(
                        existingOpen,
                        stripeConnectionId,
                        providerKey,
                        runId: null,
                        entityType: "payment",
                        localEntityId: payment.Id,
                        externalEntityId: payment.ExternalTransactionId ?? payment.ProviderPaymentIntentId,
                        conflictType: "refund_reconciliation_required",
                        severity: "high",
                        summary: "Stripe payment has refund activity; accounting refund/credit reconciliation requires review.",
                        localSnapshotJson: JsonSerializer.Serialize(new
                        {
                            payment.Id,
                            payment.Amount,
                            payment.RefundAmount,
                            payment.Status,
                            sourceOfTruth = ResolveAccountingSourceOfTruth("payment")
                        }),
                        externalSnapshotJson: null,
                        suggestedResolutionJson: JsonSerializer.Serialize(new
                        {
                            action = "reconcile",
                            entityType = "payment",
                            localEntityId = payment.Id,
                            note = "refund_or_chargeback_flow"
                        }),
                        sourceHint: "stripe_refund_reconcile");
                }
            }

            if (createdOrUpdated > 0)
            {
                await _context.SaveChangesAsync(cancellationToken);
            }

            return await CountOpenConflictsForConnectionAsync(stripeConnectionId, cancellationToken);
        }

        private async Task<int> GenerateQuickBooksAccountingConflictsAsync(
            string connectionId,
            CancellationToken cancellationToken)
        {
            var providerKey = IntegrationProviderKeys.QuickBooksOnline;
            var since = DateTime.UtcNow.AddDays(-90);
            var invoices = await _context.Invoices
                .Where(i => i.UpdatedAt >= since && i.Total > 0)
                .ToListAsync(cancellationToken);

            var payments = await _context.PaymentTransactions
                .Where(p => p.CreatedAt >= since &&
                            p.Amount > 0 &&
                            (p.Status == "Succeeded" || p.Status == "Partially Refunded" || p.Status == "Refunded"))
                .ToListAsync(cancellationToken);

            var localInvoiceIds = invoices.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
            var localPaymentIds = payments.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);

            var invoiceLinks = await _context.IntegrationEntityLinks
                .Where(l => l.ConnectionId == connectionId &&
                            l.ProviderKey == providerKey &&
                            l.LocalEntityType == "invoice" &&
                            localInvoiceIds.Contains(l.LocalEntityId))
                .ToDictionaryAsync(l => l.LocalEntityId, StringComparer.Ordinal, cancellationToken);

            var paymentLinks = await _context.IntegrationEntityLinks
                .Where(l => l.ConnectionId == connectionId &&
                            l.ProviderKey == providerKey &&
                            l.LocalEntityType == "payment" &&
                            localPaymentIds.Contains(l.LocalEntityId))
                .ToDictionaryAsync(l => l.LocalEntityId, StringComparer.Ordinal, cancellationToken);

            var existingOpen = await _context.IntegrationConflictQueueItems
                .Where(c => c.ConnectionId == connectionId &&
                            c.ProviderKey == providerKey &&
                            (c.Status == IntegrationConflictStatuses.Open || c.Status == IntegrationConflictStatuses.InReview))
                .ToDictionaryAsync(c => c.Fingerprint ?? c.Id, StringComparer.OrdinalIgnoreCase, cancellationToken);

            var createdOrUpdated = 0;
            foreach (var invoice in invoices)
            {
                if (!invoiceLinks.TryGetValue(invoice.Id, out var link))
                {
                    if (invoice.Status != InvoiceStatus.Draft)
                    {
                        createdOrUpdated += QueueOrRefreshConflict(
                            existingOpen,
                            connectionId,
                            providerKey,
                            runId: null,
                            entityType: "invoice",
                            localEntityId: invoice.Id,
                            externalEntityId: null,
                            conflictType: "missing_external_link",
                            severity: "medium",
                            summary: $"Invoice {invoice.Number ?? invoice.Id} is missing QuickBooks linkage.",
                            localSnapshotJson: JsonSerializer.Serialize(new
                            {
                                invoice.Id,
                                invoice.Number,
                                invoice.Total,
                                invoice.AmountPaid,
                                invoice.Balance,
                                status = invoice.Status.ToString()
                            }),
                            externalSnapshotJson: null,
                            suggestedResolutionJson: JsonSerializer.Serialize(new { action = "push", entityType = "invoice", localEntityId = invoice.Id }),
                            sourceHint: "qbo_reconcile_missing_link");
                    }

                    continue;
                }

                if (!TryParseJson(link.MetadataJson, out var invoiceSnapshot))
                {
                    continue;
                }

                var remoteTotal = GetDecimal(invoiceSnapshot, "TotalAmt");
                var remoteBalance = GetDecimal(invoiceSnapshot, "Balance");
                var remoteDocNumber = GetString(invoiceSnapshot, "DocNumber");

                var mismatchReasons = new List<string>();
                if (!string.IsNullOrWhiteSpace(invoice.Number) &&
                    !string.IsNullOrWhiteSpace(remoteDocNumber) &&
                    !string.Equals(invoice.Number, remoteDocNumber, StringComparison.OrdinalIgnoreCase))
                {
                    mismatchReasons.Add("doc_number");
                }

                if (remoteTotal.HasValue && Math.Abs(invoice.Total - remoteTotal.Value) > 0.01m)
                {
                    mismatchReasons.Add("total");
                }

                if (remoteBalance.HasValue && Math.Abs(invoice.Balance - remoteBalance.Value) > 0.01m)
                {
                    mismatchReasons.Add("balance");
                }

                if (mismatchReasons.Count > 0)
                {
                    createdOrUpdated += QueueOrRefreshConflict(
                        existingOpen,
                        connectionId,
                        providerKey,
                        runId: null,
                        entityType: "invoice",
                        localEntityId: invoice.Id,
                        externalEntityId: link.ExternalEntityId,
                        conflictType: "field_mismatch",
                        severity: "high",
                        summary: $"QuickBooks invoice mismatch ({string.Join(", ", mismatchReasons)}).",
                        localSnapshotJson: JsonSerializer.Serialize(new
                        {
                            invoice.Id,
                            invoice.Number,
                            invoice.Total,
                            invoice.AmountPaid,
                            invoice.Balance,
                            status = invoice.Status.ToString()
                        }),
                        externalSnapshotJson: invoiceSnapshot.GetRawText(),
                        suggestedResolutionJson: JsonSerializer.Serialize(new { action = "reconcile", entityType = "invoice", localEntityId = invoice.Id }),
                        sourceHint: "qbo_invoice_field_mismatch");
                }
            }

            foreach (var payment in payments)
            {
                if (string.Equals(payment.Source, "QuickBooksSync", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!paymentLinks.TryGetValue(payment.Id, out var link))
                {
                    createdOrUpdated += QueueOrRefreshConflict(
                        existingOpen,
                        connectionId,
                        providerKey,
                        runId: null,
                        entityType: "payment",
                        localEntityId: payment.Id,
                        externalEntityId: null,
                        conflictType: "missing_external_link",
                        severity: "medium",
                        summary: $"Payment {payment.Id} is missing QuickBooks linkage.",
                        localSnapshotJson: JsonSerializer.Serialize(new
                        {
                            payment.Id,
                            payment.InvoiceId,
                            payment.Amount,
                            payment.Currency,
                            payment.Status,
                            payment.ProcessedAt
                        }),
                        externalSnapshotJson: null,
                        suggestedResolutionJson: JsonSerializer.Serialize(new { action = "push", entityType = "payment", localEntityId = payment.Id }),
                        sourceHint: "qbo_payment_missing_link");
                    continue;
                }

                if (!TryParseJson(link.MetadataJson, out var paymentSnapshot))
                {
                    continue;
                }

                var remoteTotal = GetDecimal(paymentSnapshot, "TotalAmt");
                if (remoteTotal.HasValue && Math.Abs(payment.Amount - remoteTotal.Value) > 0.01m)
                {
                    createdOrUpdated += QueueOrRefreshConflict(
                        existingOpen,
                        connectionId,
                        providerKey,
                        runId: null,
                        entityType: "payment",
                        localEntityId: payment.Id,
                        externalEntityId: link.ExternalEntityId,
                        conflictType: "field_mismatch",
                        severity: "high",
                        summary: "QuickBooks payment amount mismatch.",
                        localSnapshotJson: JsonSerializer.Serialize(new
                        {
                            payment.Id,
                            payment.InvoiceId,
                            payment.Amount,
                            payment.Currency,
                            payment.Status,
                            payment.ProcessedAt
                        }),
                        externalSnapshotJson: paymentSnapshot.GetRawText(),
                        suggestedResolutionJson: JsonSerializer.Serialize(new { action = "reconcile", entityType = "payment", localEntityId = payment.Id }),
                        sourceHint: "qbo_payment_field_mismatch");
                }

                if ((payment.RefundAmount ?? 0m) > 0m)
                {
                    createdOrUpdated += QueueOrRefreshConflict(
                        existingOpen,
                        connectionId,
                        providerKey,
                        runId: null,
                        entityType: "payment",
                        localEntityId: payment.Id,
                        externalEntityId: link.ExternalEntityId,
                        conflictType: "refund_reconciliation_required",
                        severity: "high",
                        summary: "Payment contains refund activity; QBO refund/credit reconciliation review is required.",
                        localSnapshotJson: JsonSerializer.Serialize(new
                        {
                            payment.Id,
                            payment.Amount,
                            payment.RefundAmount,
                            payment.Status,
                            sourceOfTruth = ResolveAccountingSourceOfTruth("payment")
                        }),
                        externalSnapshotJson: paymentSnapshot.GetRawText(),
                        suggestedResolutionJson: JsonSerializer.Serialize(new { action = "reconcile", entityType = "payment", localEntityId = payment.Id, note = "refund_or_chargeback_flow" }),
                        sourceHint: "qbo_payment_refund_reconcile");
                }
            }

            if (createdOrUpdated > 0)
            {
                await _context.SaveChangesAsync(cancellationToken);
            }

            return await CountOpenConflictsForConnectionAsync(connectionId, cancellationToken);
        }

        private async Task<int> GenerateXeroAccountingCoverageConflictsAsync(
            string connectionId,
            IReadOnlyCollection<JsonElement> remoteInvoices,
            CancellationToken cancellationToken)
        {
            var providerKey = IntegrationProviderKeys.Xero;
            var since = DateTime.UtcNow.AddDays(-90);

            var localInvoices = await _context.Invoices
                .Where(i => i.UpdatedAt >= since && i.Total > 0 && i.Number != null && i.Number != "")
                .ToListAsync(cancellationToken);

            var localIds = localInvoices.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
            var invoiceLinks = await _context.IntegrationEntityLinks
                .Where(l => l.ConnectionId == connectionId &&
                            l.ProviderKey == providerKey &&
                            l.LocalEntityType == "invoice" &&
                            localIds.Contains(l.LocalEntityId))
                .ToDictionaryAsync(l => l.LocalEntityId, StringComparer.Ordinal, cancellationToken);

            var existingOpen = await _context.IntegrationConflictQueueItems
                .Where(c => c.ConnectionId == connectionId &&
                            c.ProviderKey == providerKey &&
                            (c.Status == IntegrationConflictStatuses.Open || c.Status == IntegrationConflictStatuses.InReview))
                .ToDictionaryAsync(c => c.Fingerprint ?? c.Id, StringComparer.OrdinalIgnoreCase, cancellationToken);

            var remoteByNumber = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            foreach (var remote in remoteInvoices)
            {
                var number = GetString(remote, "InvoiceNumber");
                if (!string.IsNullOrWhiteSpace(number) && !remoteByNumber.ContainsKey(number))
                {
                    remoteByNumber[number] = remote;
                }
            }

            var createdOrUpdated = 0;
            foreach (var invoice in localInvoices)
            {
                if (string.IsNullOrWhiteSpace(invoice.Number))
                {
                    continue;
                }

                if (!remoteByNumber.TryGetValue(invoice.Number, out var remote))
                {
                    if (invoice.Status != InvoiceStatus.Draft)
                    {
                        createdOrUpdated += QueueOrRefreshConflict(
                            existingOpen,
                            connectionId,
                            providerKey,
                            runId: null,
                            entityType: "invoice",
                            localEntityId: invoice.Id,
                            externalEntityId: null,
                            conflictType: "missing_remote_record",
                            severity: "medium",
                            summary: $"Xero invoice {invoice.Number} not found during sync snapshot.",
                            localSnapshotJson: JsonSerializer.Serialize(new
                            {
                                invoice.Id,
                                invoice.Number,
                                invoice.Total,
                                invoice.Balance,
                                status = invoice.Status.ToString()
                            }),
                            externalSnapshotJson: null,
                            suggestedResolutionJson: JsonSerializer.Serialize(new { action = "push", entityType = "invoice", localEntityId = invoice.Id }),
                            sourceHint: "xero_missing_remote_invoice");
                    }
                    continue;
                }

                UpsertIntegrationEntityLink(
                    invoiceLinks,
                    connectionId,
                    providerKey,
                    "invoice",
                    invoice.Id,
                    "invoice",
                    GetString(remote, "InvoiceID") ?? invoice.Number,
                    GetString(remote, "UpdatedDateUTC"),
                    "inbound",
                    remote.GetRawText());

                var remoteTotal = GetDecimal(remote, "Total");
                var remoteBalance = GetDecimal(remote, "AmountDue");
                var mismatchReasons = new List<string>();
                if (remoteTotal.HasValue && Math.Abs(invoice.Total - remoteTotal.Value) > 0.01m)
                {
                    mismatchReasons.Add("total");
                }
                if (remoteBalance.HasValue && Math.Abs(invoice.Balance - remoteBalance.Value) > 0.01m)
                {
                    mismatchReasons.Add("balance");
                }

                if (mismatchReasons.Count > 0)
                {
                    createdOrUpdated += QueueOrRefreshConflict(
                        existingOpen,
                        connectionId,
                        providerKey,
                        runId: null,
                        entityType: "invoice",
                        localEntityId: invoice.Id,
                        externalEntityId: GetString(remote, "InvoiceID"),
                        conflictType: "field_mismatch",
                        severity: "high",
                        summary: $"Xero invoice mismatch ({string.Join(", ", mismatchReasons)}).",
                        localSnapshotJson: JsonSerializer.Serialize(new
                        {
                            invoice.Id,
                            invoice.Number,
                            invoice.Total,
                            invoice.Balance,
                            status = invoice.Status.ToString()
                        }),
                        externalSnapshotJson: remote.GetRawText(),
                        suggestedResolutionJson: JsonSerializer.Serialize(new { action = "reconcile", entityType = "invoice", localEntityId = invoice.Id }),
                        sourceHint: "xero_invoice_field_mismatch");
                }
            }

            if (createdOrUpdated > 0)
            {
                await _context.SaveChangesAsync(cancellationToken);
            }

            return await CountOpenConflictsForConnectionAsync(connectionId, cancellationToken);
        }

        private int QueueOrRefreshConflict(
            IDictionary<string, IntegrationConflictQueueItem> existingOpen,
            string connectionId,
            string providerKey,
            string? runId,
            string entityType,
            string? localEntityId,
            string? externalEntityId,
            string conflictType,
            string severity,
            string summary,
            string? localSnapshotJson,
            string? externalSnapshotJson,
            string? suggestedResolutionJson,
            string sourceHint)
        {
            var fingerprint = BuildConflictFingerprint(connectionId, providerKey, entityType, localEntityId, externalEntityId, conflictType, sourceHint);
            if (existingOpen.TryGetValue(fingerprint, out var existing))
            {
                existing.Summary = Truncate(summary, 2048);
                existing.LocalSnapshotJson = localSnapshotJson;
                existing.ExternalSnapshotJson = externalSnapshotJson;
                existing.SuggestedResolutionJson = suggestedResolutionJson;
                existing.UpdatedAt = DateTime.UtcNow;
                return 0;
            }

            var conflict = new IntegrationConflictQueueItem
            {
                Id = Guid.NewGuid().ToString(),
                ConnectionId = connectionId,
                RunId = runId,
                ProviderKey = providerKey,
                EntityType = entityType,
                LocalEntityId = localEntityId,
                ExternalEntityId = externalEntityId,
                ConflictType = conflictType,
                Severity = severity,
                Status = IntegrationConflictStatuses.Open,
                Fingerprint = fingerprint,
                Summary = Truncate(summary, 2048),
                LocalSnapshotJson = localSnapshotJson,
                ExternalSnapshotJson = externalSnapshotJson,
                SuggestedResolutionJson = suggestedResolutionJson,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _context.IntegrationConflictQueueItems.Add(conflict);
            existingOpen[fingerprint] = conflict;

            _context.IntegrationReviewQueueItems.Add(new IntegrationReviewQueueItem
            {
                Id = Guid.NewGuid().ToString(),
                ConnectionId = connectionId,
                RunId = runId,
                ProviderKey = providerKey,
                ItemType = "conflict_review",
                SourceType = nameof(IntegrationConflictQueueItem),
                SourceId = conflict.Id,
                ConflictId = conflict.Id,
                Status = IntegrationReviewQueueStatuses.Pending,
                Priority = severity switch
                {
                    "high" => "high",
                    "critical" => "high",
                    _ => "medium"
                },
                Title = Truncate($"Integration conflict: {conflictType}", 160),
                Summary = Truncate(summary, 2048),
                ContextJson = JsonSerializer.Serialize(new
                {
                    conflictId = conflict.Id,
                    connectionId,
                    providerKey,
                    entityType,
                    localEntityId,
                    externalEntityId,
                    conflictType,
                    severity
                }),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            return 1;
        }

        private static string BuildConflictFingerprint(
            string connectionId,
            string providerKey,
            string entityType,
            string? localEntityId,
            string? externalEntityId,
            string conflictType,
            string sourceHint)
        {
            var source = $"{connectionId}|{providerKey}|{entityType}|{localEntityId}|{externalEntityId}|{conflictType}|{sourceHint}";
            var bytes = Encoding.UTF8.GetBytes(source);
            var hash = System.Security.Cryptography.SHA256.HashData(bytes);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private static string ResolveAccountingSourceOfTruth(string entityType)
        {
            var normalized = (entityType ?? string.Empty).Trim().ToLowerInvariant();
            return normalized switch
            {
                "invoice" => "jurisflow",
                "customer" => "jurisflow",
                "client" => "jurisflow",
                "payment" => "provider",
                _ => "jurisflow"
            };
        }

        private static string BuildExternalVersionIdempotencyKey(
            string providerKey,
            string externalEntityType,
            string externalEntityId,
            string? externalVersion)
        {
            var raw = $"{providerKey}|{externalEntityType}|{externalEntityId}|{externalVersion ?? "na"}";
            var bytes = Encoding.UTF8.GetBytes(raw);
            var hash = System.Security.Cryptography.SHA256.HashData(bytes);
            var key = Convert.ToHexString(hash).ToLowerInvariant();
            return key.Length <= 128 ? key : key[..128];
        }

        private string RequireTenantIdForIntegrationFiles()
        {
            if (string.IsNullOrWhiteSpace(_tenantContext.TenantId))
            {
                throw new InvalidOperationException("Tenant context is required for integration attachment ingest.");
            }

            return _tenantContext.TenantId;
        }

        private string GetTenantRelativeUploadPath(string fileName)
        {
            var tenantId = RequireTenantIdForIntegrationFiles();
            return $"uploads/{tenantId}/{fileName}";
        }

        private static string BuildEmailAttachmentExternalEntityId(ProviderEmailEnvelope envelope, ProviderEmailAttachment attachment, int ordinal)
        {
            var attachmentId = !string.IsNullOrWhiteSpace(attachment.ExternalAttachmentId)
                ? attachment.ExternalAttachmentId!.Trim()
                : $"idx:{ordinal}";
            var source = $"{envelope.ExternalId}:{attachmentId}";
            if (source.Length <= 240)
            {
                return source;
            }

            var hash = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(source));
            return $"{(envelope.ExternalId ?? "msg").Trim()}:{Convert.ToHexString(hash).ToLowerInvariant()[..32]}";
        }

        private static string ComputeSha256Hex(byte[] bytes)
        {
            var hash = System.Security.Cryptography.SHA256.HashData(bytes);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private static string SanitizeFileName(string fileName)
        {
            var raw = Path.GetFileName(fileName ?? string.Empty);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return "attachment.bin";
            }

            foreach (var invalid in Path.GetInvalidFileNameChars())
            {
                raw = raw.Replace(invalid, '_');
            }

            return raw.Length <= 180 ? raw : raw[^180..];
        }

        private static string? SerializeStringList(IEnumerable<string?> values)
        {
            var normalized = values
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return normalized.Length == 0 ? null : JsonSerializer.Serialize(normalized);
        }

        private static string NormalizeProviderTag(string providerKey)
        {
            var normalized = (providerKey ?? "integration").Trim().ToLowerInvariant();
            return normalized.Replace('_', '-');
        }

        private async Task<List<(DateOnly start, DateOnly end)>> LoadBillingLockRangesAsync(CancellationToken cancellationToken)
        {
            var locks = await _context.BillingLocks
                .AsNoTracking()
                .Select(l => new { l.PeriodStart, l.PeriodEnd })
                .ToListAsync(cancellationToken);

            var ranges = new List<(DateOnly start, DateOnly end)>(locks.Count);
            foreach (var row in locks)
            {
                if (!TryParseBillingLockRange(row.PeriodStart, row.PeriodEnd, out var range))
                {
                    continue;
                }

                ranges.Add(range);
            }

            return ranges;
        }

        private static bool IsBillingPeriodLocked(DateTime date, IReadOnlyCollection<(DateOnly start, DateOnly end)> ranges)
        {
            if (ranges.Count == 0)
            {
                return false;
            }

            var d = DateOnly.FromDateTime(date.Date);
            foreach (var range in ranges)
            {
                if (d >= range.start && d <= range.end)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryParseBillingLockRange(string? periodStart, string? periodEnd, out (DateOnly start, DateOnly end) range)
        {
            range = default;
            if (!DateOnly.TryParseExact(
                    periodStart,
                    "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None,
                    out var start))
            {
                return false;
            }

            if (!DateOnly.TryParseExact(
                    periodEnd,
                    "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None,
                    out var end))
            {
                return false;
            }

            if (end < start)
            {
                return false;
            }

            range = (start, end);
            return true;
        }

        private static bool IsProviderAuthoredAccountingPayment(PaymentTransaction payment, string providerKey)
        {
            return IsProviderAuthoredAccountingPaymentSource(payment.Source, providerKey);
        }

        private static bool IsProviderAuthoredAccountingPaymentSource(string? source, string providerKey)
        {
            var normalizedSource = (source ?? string.Empty).Trim();
            var normalizedProvider = (providerKey ?? string.Empty).Trim().ToLowerInvariant();
            return normalizedProvider switch
            {
                "quickbooks-online" => string.Equals(normalizedSource, "QuickBooksSync", StringComparison.OrdinalIgnoreCase),
                "xero" => string.Equals(normalizedSource, "XeroSync", StringComparison.OrdinalIgnoreCase),
                _ => false
            };
        }

        private static decimal NormalizeConnectorMoney(decimal value)
        {
            return Math.Round(value, 2, MidpointRounding.AwayFromZero);
        }

        private static bool TryParseJson(string? json, out JsonElement element)
        {
            element = default;
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            try
            {
                using var doc = JsonDocument.Parse(json);
                element = doc.RootElement.Clone();
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static string? TryExtractQuickBooksLinkedInvoiceId(JsonElement paymentRoot)
        {
            if (!paymentRoot.TryGetProperty("Line", out var lineArray) || lineArray.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var line in lineArray.EnumerateArray())
            {
                if (!line.TryGetProperty("LinkedTxn", out var linkedTxn) || linkedTxn.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var txn in linkedTxn.EnumerateArray())
                {
                    var txnType = GetString(txn, "TxnType");
                    if (!string.Equals(txnType, "Invoice", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var txnId = GetString(txn, "TxnId");
                    if (!string.IsNullOrWhiteSpace(txnId))
                    {
                        return txnId;
                    }
                }
            }

            return null;
        }

        private static void ApplyInvoicePayment(Invoice invoice, decimal amount)
        {
            invoice.AmountPaid += amount;
            invoice.Balance -= amount;
            if (invoice.Balance < 0)
            {
                invoice.Balance = 0;
            }

            if (invoice.Balance == 0)
            {
                invoice.Status = InvoiceStatus.Paid;
            }
            else if (invoice.Status == InvoiceStatus.Sent || invoice.Status == InvoiceStatus.Approved)
            {
                invoice.Status = InvoiceStatus.PartiallyPaid;
            }

            invoice.UpdatedAt = DateTime.UtcNow;
        }

        private static List<JsonElement> ResolveSubmissionRows(JsonElement root)
        {
            if (root.ValueKind == JsonValueKind.Array)
            {
                return root.EnumerateArray().ToList();
            }

            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("results", out var results) &&
                results.ValueKind == JsonValueKind.Array)
            {
                return results.EnumerateArray().ToList();
            }

            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("items", out var items) &&
                items.ValueKind == JsonValueKind.Array)
            {
                return items.EnumerateArray().ToList();
            }

            return new List<JsonElement>();
        }

        private static string? GetSubmissionId(JsonElement row)
        {
            return GetString(row, "id")
                   ?? GetString(row, "submission_id")
                   ?? GetString(row, "submissionId")
                   ?? GetString(row, "filing_id");
        }

        private static string NormalizeSubmissionStatus(string? value)
        {
            var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
            return normalized switch
            {
                "accepted" => "accepted",
                "rejected" => "rejected",
                "failed" => "failed",
                "processing" => "processing",
                "submitted" => "submitted",
                "corrected" => "corrected",
                _ => string.IsNullOrWhiteSpace(normalized) ? "pending" : normalized
            };
        }

        private static string NormalizeApiPath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "/";
            }

            var trimmed = value.Trim();
            return trimmed.StartsWith("/", StringComparison.Ordinal) ? trimmed : $"/{trimmed}";
        }

        private static DateTime? ParseProviderDateTime(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return DateTimeOffset.TryParse(value, out var dto) ? dto.UtcDateTime : null;
        }

        private static decimal? ParseProviderDecimal(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            if (decimal.TryParse(value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            {
                return NormalizeConnectorMoney(parsed);
            }

            return null;
        }

        private static IEnumerable<string> ExtractEmailAddresses(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                yield break;
            }

            var parts = value.Split(',', ';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var part in parts)
            {
                var normalized = NormalizeEmail(part);
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    yield return normalized;
                }
            }
        }

        private static string? NormalizeEmail(string value)
        {
            var trimmed = value.Trim();
            if (trimmed.Length == 0)
            {
                return null;
            }

            var lt = trimmed.LastIndexOf('<');
            var gt = trimmed.LastIndexOf('>');
            if (lt >= 0 && gt > lt)
            {
                trimmed = trimmed.Substring(lt + 1, gt - lt - 1);
            }

            return trimmed.Trim().Trim('"').ToLowerInvariant();
        }

        private static string JoinRecipientAddresses(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out var recipients) || recipients.ValueKind != JsonValueKind.Array)
            {
                return string.Empty;
            }

            var addresses = new List<string>();
            foreach (var recipient in recipients.EnumerateArray())
            {
                var address = GetNestedString(recipient, "emailAddress", "address");
                if (!string.IsNullOrWhiteSpace(address))
                {
                    addresses.Add(address);
                }
            }

            return string.Join(", ", addresses);
        }

        private static string GetHeaderValue(JsonElement headers, string name)
        {
            if (headers.ValueKind != JsonValueKind.Array)
            {
                return string.Empty;
            }

            foreach (var header in headers.EnumerateArray())
            {
                var headerName = GetString(header, "name");
                if (!string.Equals(headerName, name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return GetString(header, "value") ?? string.Empty;
            }

            return string.Empty;
        }

        private static string? ExtractBody(JsonElement payload, string mimeType)
        {
            if (payload.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var currentMimeType = GetString(payload, "mimeType");
            if (string.Equals(currentMimeType, mimeType, StringComparison.OrdinalIgnoreCase))
            {
                if (payload.TryGetProperty("body", out var bodyElement) &&
                    bodyElement.TryGetProperty("data", out var dataElement) &&
                    dataElement.ValueKind == JsonValueKind.String)
                {
                    return DecodeBase64Url(dataElement.GetString());
                }
            }

            if (payload.TryGetProperty("parts", out var partsElement) && partsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var part in partsElement.EnumerateArray())
                {
                    var nested = ExtractBody(part, mimeType);
                    if (!string.IsNullOrWhiteSpace(nested))
                    {
                        return nested;
                    }
                }
            }

            return null;
        }

        private static int CountAttachments(JsonElement payload)
        {
            var count = 0;
            CountAttachmentsRecursive(payload, ref count);
            return count;
        }

        private static void CountAttachmentsRecursive(JsonElement node, ref int count)
        {
            if (node.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            if (node.TryGetProperty("filename", out var filenameElement) &&
                filenameElement.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(filenameElement.GetString()))
            {
                count++;
            }

            if (node.TryGetProperty("parts", out var partsElement) && partsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in partsElement.EnumerateArray())
                {
                    CountAttachmentsRecursive(child, ref count);
                }
            }
        }

        private static string DecodeBase64Url(string? value)
        {
            var bytes = DecodeBase64UrlBytes(value);
            if (bytes == null || bytes.Length == 0)
            {
                return string.Empty;
            }

            try
            {
                return Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static byte[]? DecodeBase64UrlBytes(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var normalized = value.Replace('-', '+').Replace('_', '/');
            switch (normalized.Length % 4)
            {
                case 2:
                    normalized += "==";
                    break;
                case 3:
                    normalized += "=";
                    break;
            }

            try
            {
                return Convert.FromBase64String(normalized);
            }
            catch
            {
                return null;
            }
        }

        private static byte[]? DecodeBase64Bytes(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            try
            {
                return Convert.FromBase64String(value);
            }
            catch
            {
                return null;
            }
        }

        private static DateTime? ParseDateTimeFromInternalDate(JsonElement root, string? fallback)
        {
            if (root.TryGetProperty("internalDate", out var internalDateElement) &&
                internalDateElement.ValueKind == JsonValueKind.String &&
                long.TryParse(internalDateElement.GetString(), out var milliseconds))
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).UtcDateTime;
            }

            return ParseProviderDateTime(fallback);
        }

        private static (string address, string displayName) ParseMailAddress(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return (string.Empty, string.Empty);
            }

            try
            {
                var parsed = new MailAddress(value.Trim());
                return (parsed.Address, parsed.DisplayName ?? string.Empty);
            }
            catch
            {
                return (value.Trim(), string.Empty);
            }
        }

        private static bool GetBoolean(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out var value))
            {
                return false;
            }

            return value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String => bool.TryParse(value.GetString(), out var parsed) && parsed,
                _ => false
            };
        }

        private static decimal? GetDecimal(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out var value))
            {
                return null;
            }

            if (value.ValueKind == JsonValueKind.Number)
            {
                if (value.TryGetDecimal(out var dec))
                {
                    return NormalizeConnectorMoney(dec);
                }

                if (value.TryGetDouble(out var dbl))
                {
                    return NormalizeConnectorMoney((decimal)dbl);
                }
            }

            if (value.ValueKind == JsonValueKind.String &&
                decimal.TryParse(value.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            {
                return NormalizeConnectorMoney(parsed);
            }

            return null;
        }

        private static string? Truncate(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var trimmed = value.Trim();
            return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
        }

        private async Task EnsureGoogleTokenAsync(
            IntegrationConnectPayload payload,
            IntegrationMetadataEnvelope metadata,
            CancellationToken cancellationToken)
        {
            if (!string.IsNullOrWhiteSpace(payload.AccessToken))
            {
                metadata.Credentials.AccessToken = payload.AccessToken.Trim();
                metadata.Credentials.RefreshToken = string.IsNullOrWhiteSpace(payload.RefreshToken)
                    ? metadata.Credentials.RefreshToken
                    : payload.RefreshToken.Trim();
                return;
            }

            if (!string.IsNullOrWhiteSpace(payload.AuthorizationCode))
            {
                await ExchangeGoogleAuthorizationCodeAsync(payload.AuthorizationCode, payload.RedirectUri, metadata, cancellationToken);
            }
        }

        private async Task EnsureOutlookTokenAsync(
            IntegrationConnectPayload payload,
            IntegrationMetadataEnvelope metadata,
            CancellationToken cancellationToken)
        {
            if (!string.IsNullOrWhiteSpace(payload.AccessToken))
            {
                metadata.Credentials.AccessToken = payload.AccessToken.Trim();
                metadata.Credentials.RefreshToken = string.IsNullOrWhiteSpace(payload.RefreshToken)
                    ? metadata.Credentials.RefreshToken
                    : payload.RefreshToken.Trim();
                return;
            }

            if (!string.IsNullOrWhiteSpace(payload.AuthorizationCode))
            {
                await ExchangeOutlookAuthorizationCodeAsync(payload.AuthorizationCode, payload.RedirectUri, metadata, cancellationToken);
            }
        }

        private async Task EnsureQuickBooksTokenAsync(
            IntegrationConnectPayload payload,
            IntegrationMetadataEnvelope metadata,
            CancellationToken cancellationToken)
        {
            if (!string.IsNullOrWhiteSpace(payload.AccessToken))
            {
                metadata.Credentials.AccessToken = payload.AccessToken.Trim();
                metadata.Credentials.RefreshToken = string.IsNullOrWhiteSpace(payload.RefreshToken)
                    ? metadata.Credentials.RefreshToken
                    : payload.RefreshToken.Trim();
                if (!string.IsNullOrWhiteSpace(payload.RealmId))
                {
                    metadata.Credentials.RealmId = payload.RealmId.Trim();
                }
                return;
            }

            if (!string.IsNullOrWhiteSpace(payload.AuthorizationCode))
            {
                await ExchangeQuickBooksAuthorizationCodeAsync(payload.AuthorizationCode, payload.RedirectUri, metadata, cancellationToken);
            }

            if (!string.IsNullOrWhiteSpace(payload.RealmId))
            {
                metadata.Credentials.RealmId = payload.RealmId.Trim();
            }
        }

        private async Task EnsureXeroTokenAsync(
            IntegrationConnectPayload payload,
            IntegrationMetadataEnvelope metadata,
            CancellationToken cancellationToken)
        {
            if (!string.IsNullOrWhiteSpace(payload.AccessToken))
            {
                metadata.Credentials.AccessToken = payload.AccessToken.Trim();
                metadata.Credentials.RefreshToken = string.IsNullOrWhiteSpace(payload.RefreshToken)
                    ? metadata.Credentials.RefreshToken
                    : payload.RefreshToken.Trim();
                if (!string.IsNullOrWhiteSpace(payload.TenantId))
                {
                    metadata.Credentials.TenantId = payload.TenantId.Trim();
                }
                return;
            }

            if (!string.IsNullOrWhiteSpace(payload.AuthorizationCode))
            {
                await ExchangeXeroAuthorizationCodeAsync(payload.AuthorizationCode, payload.RedirectUri, metadata, cancellationToken);
            }

            if (!string.IsNullOrWhiteSpace(payload.TenantId))
            {
                metadata.Credentials.TenantId = payload.TenantId.Trim();
            }
        }

        private async Task RefreshGoogleTokenIfNeededAsync(IntegrationMetadataEnvelope metadata, CancellationToken cancellationToken)
        {
            if (!TokenRefreshRequired(metadata.Credentials))
            {
                return;
            }

            var clientId = _configuration["Integrations:Google:ClientId"];
            var clientSecret = _configuration["Integrations:Google:ClientSecret"];
            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            {
                throw new InvalidOperationException("Google OAuth is not configured.");
            }

            if (string.IsNullOrWhiteSpace(metadata.Credentials.RefreshToken))
            {
                throw new InvalidOperationException("Google refresh token is missing.");
            }

            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = metadata.Credentials.RefreshToken!,
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret
            };

            await ExchangeTokenIntoMetadataAsync("https://oauth2.googleapis.com/token", form, null, metadata, cancellationToken);
        }

        private async Task RefreshOutlookTokenIfNeededAsync(IntegrationMetadataEnvelope metadata, CancellationToken cancellationToken)
        {
            if (!TokenRefreshRequired(metadata.Credentials))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(metadata.Credentials.RefreshToken))
            {
                throw new InvalidOperationException("Outlook refresh token is missing.");
            }

            var clientId = _configuration["Integrations:Outlook:ClientId"];
            var clientSecret = _configuration["Integrations:Outlook:ClientSecret"];
            var tenantId = _configuration["Integrations:Outlook:TenantId"] ?? "common";
            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            {
                throw new InvalidOperationException("Outlook OAuth is not configured.");
            }

            var scopes = _configuration["Integrations:Outlook:Scopes"] ?? "offline_access Calendars.Read User.Read";
            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = metadata.Credentials.RefreshToken!,
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["scope"] = scopes
            };

            var tokenUrl = $"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token";
            await ExchangeTokenIntoMetadataAsync(tokenUrl, form, null, metadata, cancellationToken);
        }

        private async Task RefreshQuickBooksTokenIfNeededAsync(IntegrationMetadataEnvelope metadata, CancellationToken cancellationToken)
        {
            if (!TokenRefreshRequired(metadata.Credentials))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(metadata.Credentials.RefreshToken))
            {
                throw new InvalidOperationException("QuickBooks refresh token is missing.");
            }

            var clientId = _configuration["Integrations:QuickBooks:ClientId"];
            var clientSecret = _configuration["Integrations:QuickBooks:ClientSecret"];
            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            {
                throw new InvalidOperationException("QuickBooks OAuth is not configured.");
            }

            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = metadata.Credentials.RefreshToken!
            };
            var basicAuth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));

            await ExchangeTokenIntoMetadataAsync(
                "https://oauth.platform.intuit.com/oauth2/v1/tokens/bearer",
                form,
                new Dictionary<string, string> { ["Authorization"] = $"Basic {basicAuth}" },
                metadata,
                cancellationToken);
        }

        private async Task RefreshXeroTokenIfNeededAsync(IntegrationMetadataEnvelope metadata, CancellationToken cancellationToken)
        {
            if (!TokenRefreshRequired(metadata.Credentials))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(metadata.Credentials.RefreshToken))
            {
                throw new InvalidOperationException("Xero refresh token is missing.");
            }

            var clientId = _configuration["Integrations:Xero:ClientId"];
            var clientSecret = _configuration["Integrations:Xero:ClientSecret"];
            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            {
                throw new InvalidOperationException("Xero OAuth is not configured.");
            }

            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = metadata.Credentials.RefreshToken!
            };
            var basicAuth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));

            await ExchangeTokenIntoMetadataAsync(
                "https://identity.xero.com/connect/token",
                form,
                new Dictionary<string, string> { ["Authorization"] = $"Basic {basicAuth}" },
                metadata,
                cancellationToken);
        }

        private async Task ExchangeGoogleAuthorizationCodeAsync(
            string code,
            string? redirectUri,
            IntegrationMetadataEnvelope metadata,
            CancellationToken cancellationToken)
        {
            var clientId = _configuration["Integrations:Google:ClientId"];
            var clientSecret = _configuration["Integrations:Google:ClientSecret"];
            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            {
                throw new InvalidOperationException("Google OAuth is not configured.");
            }

            var resolvedRedirect = ResolveRedirectUri(
                redirectUri,
                _configuration["Integrations:Google:RedirectUri"],
                "/auth/google/callback");

            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code.Trim(),
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["redirect_uri"] = resolvedRedirect
            };

            await ExchangeTokenIntoMetadataAsync("https://oauth2.googleapis.com/token", form, null, metadata, cancellationToken);
        }

        private async Task ExchangeOutlookAuthorizationCodeAsync(
            string code,
            string? redirectUri,
            IntegrationMetadataEnvelope metadata,
            CancellationToken cancellationToken)
        {
            var clientId = _configuration["Integrations:Outlook:ClientId"];
            var clientSecret = _configuration["Integrations:Outlook:ClientSecret"];
            var tenantId = _configuration["Integrations:Outlook:TenantId"] ?? "common";
            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            {
                throw new InvalidOperationException("Outlook OAuth is not configured.");
            }

            var scopes = _configuration["Integrations:Outlook:Scopes"] ?? "offline_access Calendars.Read User.Read";
            var resolvedRedirect = ResolveRedirectUri(
                redirectUri,
                _configuration["Integrations:Outlook:RedirectUri"],
                "/auth/outlook/callback");

            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code.Trim(),
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["redirect_uri"] = resolvedRedirect,
                ["scope"] = scopes
            };

            var tokenUrl = $"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token";
            await ExchangeTokenIntoMetadataAsync(tokenUrl, form, null, metadata, cancellationToken);
        }

        private async Task ExchangeQuickBooksAuthorizationCodeAsync(
            string code,
            string? redirectUri,
            IntegrationMetadataEnvelope metadata,
            CancellationToken cancellationToken)
        {
            var clientId = _configuration["Integrations:QuickBooks:ClientId"];
            var clientSecret = _configuration["Integrations:QuickBooks:ClientSecret"];
            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            {
                throw new InvalidOperationException("QuickBooks OAuth is not configured.");
            }

            var resolvedRedirect = ResolveRedirectUri(
                redirectUri,
                _configuration["Integrations:QuickBooks:RedirectUri"],
                "/auth/quickbooks/callback");

            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code.Trim(),
                ["redirect_uri"] = resolvedRedirect
            };

            var basicAuth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));
            await ExchangeTokenIntoMetadataAsync(
                "https://oauth.platform.intuit.com/oauth2/v1/tokens/bearer",
                form,
                new Dictionary<string, string> { ["Authorization"] = $"Basic {basicAuth}" },
                metadata,
                cancellationToken);
        }

        private async Task ExchangeXeroAuthorizationCodeAsync(
            string code,
            string? redirectUri,
            IntegrationMetadataEnvelope metadata,
            CancellationToken cancellationToken)
        {
            var clientId = _configuration["Integrations:Xero:ClientId"];
            var clientSecret = _configuration["Integrations:Xero:ClientSecret"];
            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            {
                throw new InvalidOperationException("Xero OAuth is not configured.");
            }

            var resolvedRedirect = ResolveRedirectUri(
                redirectUri,
                _configuration["Integrations:Xero:RedirectUri"],
                "/auth/xero/callback");

            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code.Trim(),
                ["redirect_uri"] = resolvedRedirect
            };
            var basicAuth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));

            await ExchangeTokenIntoMetadataAsync(
                "https://identity.xero.com/connect/token",
                form,
                new Dictionary<string, string> { ["Authorization"] = $"Basic {basicAuth}" },
                metadata,
                cancellationToken);
        }

        private async Task ExchangeTokenIntoMetadataAsync(
            string tokenUrl,
            IReadOnlyDictionary<string, string> formValues,
            IReadOnlyDictionary<string, string>? additionalHeaders,
            IntegrationMetadataEnvelope metadata,
            CancellationToken cancellationToken)
        {
            using var tokenDoc = await PostFormAsync(tokenUrl, formValues, additionalHeaders, cancellationToken);
            var root = tokenDoc.RootElement;

            var accessToken = GetString(root, "access_token");
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                throw new InvalidOperationException("Token endpoint did not return an access token.");
            }

            metadata.Credentials.AccessToken = accessToken;

            var refreshToken = GetString(root, "refresh_token");
            if (!string.IsNullOrWhiteSpace(refreshToken))
            {
                metadata.Credentials.RefreshToken = refreshToken;
            }

            var tokenType = GetString(root, "token_type");
            if (!string.IsNullOrWhiteSpace(tokenType))
            {
                metadata.Credentials.TokenType = tokenType;
            }

            var scope = GetString(root, "scope");
            if (!string.IsNullOrWhiteSpace(scope))
            {
                metadata.Credentials.Scope = scope;
            }

            var expiresIn = GetInt(root, "expires_in");
            if (expiresIn.HasValue && expiresIn.Value > 0)
            {
                metadata.Credentials.ExpiresAtUtc = DateTime.UtcNow.AddSeconds(Math.Max(30, expiresIn.Value - 60));
            }
            else
            {
                metadata.Credentials.ExpiresAtUtc = null;
            }
        }

        private async Task<JsonDocument> GetJsonAsync(
            string url,
            string? authScheme,
            string? authToken,
            IReadOnlyDictionary<string, string>? additionalHeaders,
            CancellationToken cancellationToken)
        {
            var client = _httpClientFactory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, url);

            if (!string.IsNullOrWhiteSpace(authScheme) && !string.IsNullOrWhiteSpace(authToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue(authScheme.Trim(), authToken.Trim());
            }

            if (additionalHeaders != null)
            {
                foreach (var header in additionalHeaders)
                {
                    request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            using var response = await client.SendAsync(request, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                ThrowProviderHttpFailure(response, payload, "request");
            }

            try
            {
                return JsonDocument.Parse(payload);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"Provider returned invalid JSON: {ex.Message}", ex);
            }
        }

        private async Task<JsonDocument> PostFormAsync(
            string url,
            IReadOnlyDictionary<string, string> formValues,
            IReadOnlyDictionary<string, string>? additionalHeaders,
            CancellationToken cancellationToken)
        {
            var client = _httpClientFactory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new FormUrlEncodedContent(formValues)
            };

            if (additionalHeaders != null)
            {
                foreach (var header in additionalHeaders)
                {
                    if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value))
                    {
                        request.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    }
                }
            }

            using var response = await client.SendAsync(request, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                ThrowProviderHttpFailure(response, payload, "token_exchange");
            }

            try
            {
                return JsonDocument.Parse(payload);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"Token endpoint returned invalid JSON: {ex.Message}", ex);
            }
        }

        private async Task<JsonDocument> PostJsonAsync(
            string url,
            object payload,
            string? authScheme,
            string? authToken,
            IReadOnlyDictionary<string, string>? additionalHeaders,
            CancellationToken cancellationToken)
        {
            var client = _httpClientFactory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(payload),
                    Encoding.UTF8,
                    "application/json")
            };

            if (!string.IsNullOrWhiteSpace(authScheme) && !string.IsNullOrWhiteSpace(authToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue(authScheme.Trim(), authToken.Trim());
            }

            if (additionalHeaders != null)
            {
                foreach (var header in additionalHeaders)
                {
                    if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value))
                    {
                        request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    }
                }
            }

            using var response = await client.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                ThrowProviderHttpFailure(response, body, "post");
            }

            try
            {
                return JsonDocument.Parse(body);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"Provider returned invalid JSON: {ex.Message}", ex);
            }
        }

        private async Task<JsonDocument?> SendJsonAsync(
            HttpMethod method,
            string url,
            object payload,
            string? authScheme,
            string? authToken,
            IReadOnlyDictionary<string, string>? additionalHeaders,
            CancellationToken cancellationToken,
            bool allowEmptyBody = false)
        {
            var client = _httpClientFactory.CreateClient();
            using var request = new HttpRequestMessage(method, url)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(payload),
                    Encoding.UTF8,
                    "application/json")
            };

            if (!string.IsNullOrWhiteSpace(authScheme) && !string.IsNullOrWhiteSpace(authToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue(authScheme.Trim(), authToken.Trim());
            }

            if (additionalHeaders != null)
            {
                foreach (var header in additionalHeaders)
                {
                    if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value))
                    {
                        request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    }
                }
            }

            using var response = await client.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                ThrowProviderHttpFailure(response, body, method.Method);
            }

            if (string.IsNullOrWhiteSpace(body))
            {
                if (allowEmptyBody)
                {
                    return null;
                }

                throw new InvalidOperationException($"Provider {method.Method} returned an empty body.");
            }

            try
            {
                return JsonDocument.Parse(body);
            }
            catch (JsonException ex)
            {
                if (allowEmptyBody)
                {
                    return null;
                }

                throw new InvalidOperationException($"Provider returned invalid JSON: {ex.Message}", ex);
            }
        }

        private async Task<Dictionary<string, string>> GetOutlookFolderMapAsync(string accessToken, CancellationToken cancellationToken)
        {
            using var doc = await GetJsonAsync(
                "https://graph.microsoft.com/v1.0/me/mailFolders?$top=100&$select=id,displayName",
                authScheme: "Bearer",
                authToken: accessToken,
                additionalHeaders: null,
                cancellationToken: cancellationToken);

            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!doc.RootElement.TryGetProperty("value", out var folders) || folders.ValueKind != JsonValueKind.Array)
            {
                return map;
            }

            foreach (var folder in folders.EnumerateArray())
            {
                var id = GetString(folder, "id");
                var displayName = GetString(folder, "displayName");
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(displayName))
                {
                    continue;
                }

                map[displayName] = id;
                if (displayName.Equals("Archive", StringComparison.OrdinalIgnoreCase))
                {
                    map["Archived"] = id;
                }
            }

            return map;
        }

        private static void ThrowProviderHttpFailure(HttpResponseMessage response, string? body, string operationName)
        {
            var detail = string.IsNullOrWhiteSpace(body) ? response.ReasonPhrase : body;
            var statusCode = (int)response.StatusCode;
            var normalizedDetail = string.IsNullOrWhiteSpace(detail) ? "Provider request failed." : detail.Trim();

            if (statusCode == 429)
            {
                var retryAfter = TryGetRetryAfter(response);
                var message = $"Provider {operationName} rate-limited (429): {normalizedDetail}";
                if (retryAfter.HasValue)
                {
                    message = $"{message} (retry_after={Math.Ceiling(retryAfter.Value.TotalSeconds)}s)";
                }

                throw new IntegrationProviderRateLimitException(message, retryAfter);
            }

            if (statusCode >= 500 || statusCode == 408)
            {
                throw new HttpRequestException(
                    $"Provider {operationName} failed ({statusCode}): {normalizedDetail}",
                    inner: null,
                    statusCode: response.StatusCode);
            }

            throw new InvalidOperationException($"Provider {operationName} failed ({statusCode}): {normalizedDetail}");
        }

        private static TimeSpan? TryGetRetryAfter(HttpResponseMessage response)
        {
            if (response.Headers.RetryAfter?.Delta is TimeSpan delta && delta > TimeSpan.Zero)
            {
                return delta;
            }

            if (response.Headers.RetryAfter?.Date is DateTimeOffset date)
            {
                var remaining = date.UtcDateTime - DateTime.UtcNow;
                if (remaining > TimeSpan.Zero)
                {
                    return remaining;
                }
            }

            if (response.Headers.TryGetValues("Retry-After", out var values))
            {
                var raw = values.FirstOrDefault();
                if (int.TryParse(raw, out var seconds) && seconds > 0)
                {
                    return TimeSpan.FromSeconds(seconds);
                }
            }

            return null;
        }

        private static bool TokenRefreshRequired(IntegrationCredentials credentials)
        {
            if (string.IsNullOrWhiteSpace(credentials.AccessToken))
            {
                return true;
            }

            if (!credentials.ExpiresAtUtc.HasValue)
            {
                return false;
            }

            return credentials.ExpiresAtUtc.Value <= DateTime.UtcNow.AddMinutes(2);
        }

        private string ResolveRedirectUri(string? requestedRedirectUri, string? configuredRedirectUri, string fallbackPath)
        {
            if (Uri.TryCreate(requestedRedirectUri?.Trim(), UriKind.Absolute, out var requestUri))
            {
                return requestUri.ToString();
            }

            if (Uri.TryCreate(configuredRedirectUri?.Trim(), UriKind.Absolute, out var configuredUri))
            {
                return configuredUri.ToString();
            }

            var appBaseUrl = _configuration["Client:BaseUrl"]
                             ?? _configuration["App:BaseUrl"]
                             ?? _configuration["App:PublicBaseUrl"]
                             ?? "http://localhost:3000";

            return $"{appBaseUrl.TrimEnd('/')}{fallbackPath}";
        }

        private bool HasCourtListenerPolicyAcknowledgement(IntegrationMetadataEnvelope metadata)
        {
            var required = _configuration.GetValue<bool?>("Integrations:CourtListener:RequirePolicyAcknowledgement") ?? false;
            if (!required)
            {
                return true;
            }

            return metadata.Compliance?.CourtDataPolicyAcknowledged == true &&
                   metadata.Compliance.CourtDataPolicyAcknowledgedAtUtc.HasValue;
        }

        private static string? GetString(JsonElement root, string propertyName)
        {
            return root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;
        }

        private static string? GetNestedString(JsonElement root, string nestedProperty, string propertyName)
        {
            if (!root.TryGetProperty(nestedProperty, out var nested) || nested.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return GetString(nested, propertyName);
        }

        private static string? GetNestedString(JsonElement root, params string[] path)
        {
            var current = root;
            foreach (var segment in path)
            {
                if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out var next))
                {
                    return null;
                }

                current = next;
            }

            return current.ValueKind == JsonValueKind.String ? current.GetString() : null;
        }

        private static int? GetInt(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out var property))
            {
                return null;
            }

            return property.ValueKind switch
            {
                JsonValueKind.Number when property.TryGetInt32(out var intValue) => intValue,
                JsonValueKind.String when int.TryParse(property.GetString(), out var parsed) => parsed,
                _ => null
            };
        }

        private static long? GetLong(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out var property))
            {
                return null;
            }

            return property.ValueKind switch
            {
                JsonValueKind.Number when property.TryGetInt64(out var longValue) => longValue,
                JsonValueKind.String when long.TryParse(property.GetString(), out var parsed) => parsed,
                _ => null
            };
        }

        private static bool IsDeletedDocumentCatalogRow(JsonElement row)
        {
            if (row.TryGetProperty("deleted", out var deleted))
            {
                if (deleted.ValueKind is JsonValueKind.True or JsonValueKind.Object)
                {
                    return true;
                }

                if (deleted.ValueKind == JsonValueKind.String &&
                    bool.TryParse(deleted.GetString(), out var deletedFlag) &&
                    deletedFlag)
                {
                    return true;
                }
            }

            if (row.TryGetProperty("isDeleted", out var isDeleted))
            {
                if (isDeleted.ValueKind == JsonValueKind.True)
                {
                    return true;
                }

                if (isDeleted.ValueKind == JsonValueKind.String &&
                    bool.TryParse(isDeleted.GetString(), out var isDeletedFlag) &&
                    isDeletedFlag)
                {
                    return true;
                }
            }

            if (row.TryGetProperty("trashed", out var trashed))
            {
                if (trashed.ValueKind == JsonValueKind.True)
                {
                    return true;
                }

                if (trashed.ValueKind == JsonValueKind.String &&
                    bool.TryParse(trashed.GetString(), out var trashedFlag) &&
                    trashedFlag)
                {
                    return true;
                }
            }

            var state = GetString(row, "state") ?? GetString(row, "status");
            return string.Equals(state, "deleted", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(state, "trashed", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(state, "removed", StringComparison.OrdinalIgnoreCase);
        }

        private static string? ExtractFirstString(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var item in property.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
                {
                    return item.GetString();
                }
            }

            return null;
        }

        private async Task HydrateCredentialsAsync(
            string? connectionId,
            IntegrationMetadataEnvelope metadata,
            IntegrationSecretScope scope,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(connectionId))
            {
                return;
            }

            var secretMaterial = await _secretStore.GetAsync(connectionId, scope, cancellationToken);
            if (secretMaterial == null)
            {
                return;
            }

            ApplySecretMaterial(metadata.Credentials, secretMaterial);
        }

        private async Task PersistCredentialsAsync(
            string connectionId,
            string providerKey,
            IntegrationMetadataEnvelope metadata,
            IntegrationSecretScope scope,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(connectionId))
            {
                return;
            }

            await _secretStore.UpsertAsync(
                connectionId,
                providerKey,
                ToSecretMaterial(metadata.Credentials),
                scope,
                cancellationToken);

            ClearSecretFields(metadata.Credentials);
        }

        private static IntegrationMetadataEnvelope DeserializeMetadata(string? metadataJson)
        {
            if (string.IsNullOrWhiteSpace(metadataJson))
            {
                return new IntegrationMetadataEnvelope();
            }

            try
            {
                var parsed = JsonSerializer.Deserialize<IntegrationMetadataEnvelope>(metadataJson);
                if (parsed == null)
                {
                    return new IntegrationMetadataEnvelope();
                }

                parsed.Credentials ??= new IntegrationCredentials();
                parsed.Compliance ??= new IntegrationComplianceFlags();
                return parsed;
            }
            catch
            {
                return new IntegrationMetadataEnvelope();
            }
        }

        private string SerializeMetadata(IntegrationMetadataEnvelope metadata)
        {
            metadata.Credentials ??= new IntegrationCredentials();
            metadata.Compliance ??= new IntegrationComplianceFlags();
            return JsonSerializer.Serialize(metadata, _jsonOptions);
        }

        private static void ApplyComplianceFlags(IntegrationMetadataEnvelope metadata, IntegrationConnectPayload payload)
        {
            metadata.Compliance ??= new IntegrationComplianceFlags();
            if (payload.CourtDataPolicyAcknowledged == true)
            {
                metadata.Compliance.CourtDataPolicyAcknowledged = true;
                metadata.Compliance.CourtDataPolicyAcknowledgedAtUtc = DateTime.UtcNow;
                if (!string.IsNullOrWhiteSpace(payload.CourtDataPolicyAcknowledgedBy))
                {
                    metadata.Compliance.CourtDataPolicyAcknowledgedBy = payload.CourtDataPolicyAcknowledgedBy.Trim();
                }
                if (!string.IsNullOrWhiteSpace(payload.CourtDataPolicyVersion))
                {
                    metadata.Compliance.CourtDataPolicyVersion = payload.CourtDataPolicyVersion.Trim();
                }
            }
        }

        private static void MergeIncomingCredentials(IntegrationCredentials credentials, IntegrationConnectPayload payload)
        {
            if (!string.IsNullOrWhiteSpace(payload.ApiKey))
            {
                credentials.ApiKey = payload.ApiKey.Trim();
            }

            if (!string.IsNullOrWhiteSpace(payload.ApiSecret))
            {
                credentials.ApiSecret = payload.ApiSecret.Trim();
            }

            if (!string.IsNullOrWhiteSpace(payload.AccessToken))
            {
                credentials.AccessToken = payload.AccessToken.Trim();
            }

            if (!string.IsNullOrWhiteSpace(payload.RefreshToken))
            {
                credentials.RefreshToken = payload.RefreshToken.Trim();
            }

            if (!string.IsNullOrWhiteSpace(payload.RealmId))
            {
                credentials.RealmId = payload.RealmId.Trim();
            }

            if (!string.IsNullOrWhiteSpace(payload.TenantId))
            {
                credentials.TenantId = payload.TenantId.Trim();
            }

            if (!string.IsNullOrWhiteSpace(payload.AccountLabel))
            {
                credentials.AccountLabel = payload.AccountLabel.Trim();
            }

            if (!string.IsNullOrWhiteSpace(payload.AccountEmail))
            {
                credentials.AccountEmail = payload.AccountEmail.Trim();
            }
        }

        private static IntegrationSecretMaterial ToSecretMaterial(IntegrationCredentials credentials)
        {
            return new IntegrationSecretMaterial
            {
                ApiKey = credentials.ApiKey,
                ApiSecret = credentials.ApiSecret,
                AccessToken = credentials.AccessToken,
                RefreshToken = credentials.RefreshToken,
                TokenType = credentials.TokenType,
                Scope = credentials.Scope,
                ExpiresAtUtc = credentials.ExpiresAtUtc,
                RealmId = credentials.RealmId,
                TenantId = credentials.TenantId,
                TenantName = credentials.TenantName,
                CalendarId = credentials.CalendarId,
                ExternalAccountId = credentials.ExternalAccountId,
                AccountEmail = credentials.AccountEmail,
                AccountLabel = credentials.AccountLabel
            };
        }

        private static void ApplySecretMaterial(IntegrationCredentials credentials, IntegrationSecretMaterial secret)
        {
            credentials.ApiKey = secret.ApiKey;
            credentials.ApiSecret = secret.ApiSecret;
            credentials.AccessToken = secret.AccessToken;
            credentials.RefreshToken = secret.RefreshToken;
            credentials.TokenType = secret.TokenType;
            credentials.Scope = secret.Scope;
            credentials.ExpiresAtUtc = secret.ExpiresAtUtc;
            credentials.RealmId = secret.RealmId;
            credentials.TenantId = secret.TenantId;
            credentials.TenantName = secret.TenantName;
            credentials.CalendarId = secret.CalendarId;
            credentials.ExternalAccountId = secret.ExternalAccountId;
            credentials.AccountEmail = secret.AccountEmail;
            credentials.AccountLabel = secret.AccountLabel;
        }

        private static void ClearSecretFields(IntegrationCredentials credentials)
        {
            credentials.ApiKey = null;
            credentials.ApiSecret = null;
            credentials.AccessToken = null;
            credentials.RefreshToken = null;
            credentials.TokenType = null;
            credentials.Scope = null;
            credentials.ExpiresAtUtc = null;
            credentials.RealmId = null;
            credentials.TenantId = null;
            credentials.TenantName = null;
            credentials.CalendarId = null;
            credentials.ExternalAccountId = null;
            credentials.AccountEmail = null;
            credentials.AccountLabel = null;
        }

        private static bool ContainsSensitiveCredentialData(IntegrationCredentials credentials)
        {
            return !string.IsNullOrWhiteSpace(credentials.ApiKey)
                   || !string.IsNullOrWhiteSpace(credentials.ApiSecret)
                   || !string.IsNullOrWhiteSpace(credentials.AccessToken)
                   || !string.IsNullOrWhiteSpace(credentials.RefreshToken)
                   || !string.IsNullOrWhiteSpace(credentials.TokenType)
                   || !string.IsNullOrWhiteSpace(credentials.Scope)
                   || credentials.ExpiresAtUtc.HasValue
                   || !string.IsNullOrWhiteSpace(credentials.RealmId)
                   || !string.IsNullOrWhiteSpace(credentials.TenantId)
                   || !string.IsNullOrWhiteSpace(credentials.TenantName)
                   || !string.IsNullOrWhiteSpace(credentials.CalendarId)
                   || !string.IsNullOrWhiteSpace(credentials.ExternalAccountId)
                   || !string.IsNullOrWhiteSpace(credentials.AccountEmail)
                   || !string.IsNullOrWhiteSpace(credentials.AccountLabel);
        }

        private sealed class ProviderEmailEnvelope
        {
            public string ExternalId { get; set; } = string.Empty;
            public string Subject { get; set; } = string.Empty;
            public string FromAddress { get; set; } = string.Empty;
            public string FromName { get; set; } = string.Empty;
            public string ToAddresses { get; set; } = string.Empty;
            public string? CcAddresses { get; set; }
            public string? BccAddresses { get; set; }
            public string? BodyText { get; set; }
            public string? BodyHtml { get; set; }
            public string Folder { get; set; } = "Inbox";
            public bool IsRead { get; set; }
            public bool HasAttachments { get; set; }
            public int AttachmentCount { get; set; }
            public string Importance { get; set; } = "Normal";
            public DateTime ReceivedAt { get; set; }
            public DateTime? SentAt { get; set; }
            public List<ProviderEmailAttachment> Attachments { get; set; } = new();
        }

        private sealed class ProviderEmailAttachment
        {
            public string? ExternalAttachmentId { get; set; }
            public string FileName { get; set; } = string.Empty;
            public string? MimeType { get; set; }
            public long SizeBytes { get; set; }
            public bool IsInline { get; set; }
            public byte[]? ContentBytes { get; set; }
            public string? SourceReference { get; set; }
        }

        private sealed class ProviderDocumentCatalogEntry
        {
            public string ExternalId { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string? MimeType { get; set; }
            public long SizeBytes { get; set; }
            public DateTime? ModifiedAt { get; set; }
            public string? WebUrl { get; set; }
            public string? ParentReference { get; set; }
            public bool IsFolder { get; set; }
            public bool IsDeleted { get; set; }
        }

        private sealed class EfilingSubmissionArtifact
        {
            public string ExternalArtifactId { get; set; } = string.Empty;
            public string FileName { get; set; } = "efiling-artifact.bin";
            public string? MimeType { get; set; }
            public string ArtifactType { get; set; } = "artifact";
            public string? Label { get; set; }
            public string? DownloadUrl { get; set; }
            public string? ContentBase64 { get; set; }
            public string? VersionHint { get; set; }
            public bool IsNotice { get; set; }
            public bool IsStampedCopy { get; set; }
        }

        private sealed class EfilingArtifactIngestionResult
        {
            public int DocumentsImported { get; set; }
            public int ReviewsQueued { get; set; }
            public int Deduped { get; set; }
        }

        private sealed class ClientLookupItem
        {
            public string Id { get; set; } = string.Empty;
            public string? Email { get; set; }
        }

        private sealed class MatterLookupItem
        {
            public string Id { get; set; } = string.Empty;
            public string ClientId { get; set; } = string.Empty;
            public string CaseNumber { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
        }

        private sealed class IntegrationMetadataEnvelope
        {
            public IntegrationCredentials Credentials { get; set; } = new();
            public IntegrationValidationSnapshot? LastValidation { get; set; }
            public IntegrationSyncSnapshot? LastSync { get; set; }
            public IntegrationComplianceFlags Compliance { get; set; } = new();
        }

        private sealed class IntegrationCredentials
        {
            public string? ApiKey { get; set; }
            public string? ApiSecret { get; set; }
            public string? AccessToken { get; set; }
            public string? RefreshToken { get; set; }
            public string? TokenType { get; set; }
            public string? Scope { get; set; }
            public DateTime? ExpiresAtUtc { get; set; }
            public string? RealmId { get; set; }
            public string? TenantId { get; set; }
            public string? TenantName { get; set; }
            public string? CalendarId { get; set; }
            public string? ExternalAccountId { get; set; }
            public string? AccountEmail { get; set; }
            public string? AccountLabel { get; set; }
        }

        private sealed class IntegrationValidationSnapshot
        {
            public DateTime ValidAtUtc { get; set; }
            public string? Message { get; set; }
        }

        private sealed class IntegrationSyncSnapshot
        {
            public DateTime SyncedAtUtc { get; set; }
            public bool Success { get; set; }
            public string? Message { get; set; }
            public int SyncedCount { get; set; }
        }

        private sealed class IntegrationComplianceFlags
        {
            public bool CourtDataPolicyAcknowledged { get; set; }
            public DateTime? CourtDataPolicyAcknowledgedAtUtc { get; set; }
            public string? CourtDataPolicyAcknowledgedBy { get; set; }
            public string? CourtDataPolicyVersion { get; set; }
        }
    }

    public sealed class IntegrationConnectPayload
    {
        public string? ApiKey { get; set; }
        public string? ApiSecret { get; set; }
        public string? AccessToken { get; set; }
        public string? RefreshToken { get; set; }
        public string? AuthorizationCode { get; set; }
        public string? RedirectUri { get; set; }
        public string? RealmId { get; set; }
        public string? TenantId { get; set; }
        public string? AccountLabel { get; set; }
        public string? AccountEmail { get; set; }
        public bool? SyncEnabled { get; set; }
        public bool? CourtDataPolicyAcknowledged { get; set; }
        public string? CourtDataPolicyAcknowledgedBy { get; set; }
        public string? CourtDataPolicyVersion { get; set; }
    }

    public sealed class IntegrationConnectResult
    {
        public bool Success { get; set; }
        public string Status { get; set; } = "connected";
        public string? Message { get; set; }
        public string? ErrorMessage { get; set; }
        public string? AccountLabel { get; set; }
        public string? AccountEmail { get; set; }
        public string? ExternalAccountId { get; set; }
        public string? Notes { get; set; }
        public string? MetadataJson { get; set; }
    }

    public sealed class IntegrationSyncResult
    {
        public bool Success { get; set; }
        public int SyncedCount { get; set; }
        public bool Retryable { get; set; } = true;
        public string? ErrorCode { get; set; }
        public string? NextCursor { get; set; }
        public string? NextDeltaToken { get; set; }
        public string? Message { get; set; }
        public string? ErrorMessage { get; set; }
        public string? MetadataJson { get; set; }
    }

    public sealed class EfilingPartnerSubmitRequest
    {
        public string ProviderKey { get; set; } = string.Empty;
        public string? ConnectionId { get; set; }
        public string MatterId { get; set; } = string.Empty;
        public string? ExistingSubmissionId { get; set; }
        public string? PacketName { get; set; }
        public string? FilingType { get; set; }
        public List<string> DocumentIds { get; set; } = new();
        public Dictionary<string, string>? Metadata { get; set; }
    }

    public sealed class EfilingPartnerSubmitResult
    {
        public bool Success { get; set; }
        public string? ProviderKey { get; set; }
        public string? ConnectionId { get; set; }
        public int SyncedCount { get; set; }
        public string? Message { get; set; }
        public string? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
        public List<EfilingPartnerSubmitSubmissionItem> Submissions { get; set; } = new();
    }

    public sealed class EfilingPartnerSubmitSubmissionItem
    {
        public string SubmissionId { get; set; } = string.Empty;
        public string ExternalSubmissionId { get; set; } = string.Empty;
        public string Status { get; set; } = "pending";
        public string? ReferenceNumber { get; set; }
        public DateTime? SubmittedAt { get; set; }
        public DateTime? AcceptedAt { get; set; }
        public DateTime? RejectedAt { get; set; }
        public string? RejectionReason { get; set; }
    }

    public sealed class MetadataSecretMigrationResult
    {
        public bool Migrated { get; set; }
        public string? MetadataJson { get; set; }
    }
}
