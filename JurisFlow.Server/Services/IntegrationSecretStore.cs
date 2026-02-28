using System.Text.Json;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace JurisFlow.Server.Services
{
    public interface IIntegrationSecretStore
    {
        Task<IntegrationSecretMaterial?> GetAsync(
            string connectionId,
            IntegrationSecretScope scope,
            CancellationToken cancellationToken);

        Task UpsertAsync(
            string connectionId,
            string providerKey,
            IntegrationSecretMaterial secrets,
            IntegrationSecretScope scope,
            CancellationToken cancellationToken);

        Task DeleteAsync(
            string connectionId,
            IntegrationSecretScope scope,
            CancellationToken cancellationToken);

        Task<int> RotateOutdatedSecretsAsync(
            IntegrationSecretScope scope,
            CancellationToken cancellationToken);
    }

    public sealed class IntegrationSecretStore : IIntegrationSecretStore
    {
        private readonly JurisFlowDbContext _context;
        private readonly IIntegrationSecretCryptoService _cryptoService;
        private readonly IIntegrationSecretAccessPolicy _accessPolicy;
        private readonly ILogger<IntegrationSecretStore> _logger;
        private readonly IConfiguration _configuration;
        private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

        public IntegrationSecretStore(
            JurisFlowDbContext context,
            IIntegrationSecretCryptoService cryptoService,
            IIntegrationSecretAccessPolicy accessPolicy,
            ILogger<IntegrationSecretStore> logger,
            IConfiguration configuration)
        {
            _context = context;
            _cryptoService = cryptoService;
            _accessPolicy = accessPolicy;
            _logger = logger;
            _configuration = configuration;
        }

        public async Task<IntegrationSecretMaterial?> GetAsync(
            string connectionId,
            IntegrationSecretScope scope,
            CancellationToken cancellationToken)
        {
            _accessPolicy.EnsureAllowed(scope, IntegrationSecretOperation.Read);

            if (string.IsNullOrWhiteSpace(connectionId))
            {
                return null;
            }

            var entry = await _context.IntegrationSecrets
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.ConnectionId == connectionId, cancellationToken);

            if (entry == null || string.IsNullOrWhiteSpace(entry.SecretJson))
            {
                return null;
            }

            try
            {
                var decrypted = await _cryptoService.DecryptAsync(entry.SecretJson, cancellationToken);
                return JsonSerializer.Deserialize<IntegrationSecretMaterial>(decrypted.Plaintext, _jsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Unable to decrypt integration secret payload. ConnectionId={ConnectionId} ProviderKey={ProviderKey}",
                    entry.ConnectionId,
                    entry.ProviderKey);
                throw new InvalidOperationException(
                    $"Encrypted integration secret payload could not be read for connection '{entry.ConnectionId}'.",
                    ex);
            }
        }

        public async Task UpsertAsync(
            string connectionId,
            string providerKey,
            IntegrationSecretMaterial secrets,
            IntegrationSecretScope scope,
            CancellationToken cancellationToken)
        {
            _accessPolicy.EnsureAllowed(scope, IntegrationSecretOperation.Write);

            if (string.IsNullOrWhiteSpace(connectionId))
            {
                throw new ArgumentException("Connection id is required.", nameof(connectionId));
            }

            if (string.IsNullOrWhiteSpace(providerKey))
            {
                throw new ArgumentException("Provider key is required.", nameof(providerKey));
            }

            if (secrets.IsEmpty())
            {
                await DeleteAsync(connectionId, scope, cancellationToken);
                return;
            }

            var existing = await _context.IntegrationSecrets
                .FirstOrDefaultAsync(s => s.ConnectionId == connectionId, cancellationToken);

            if (existing == null)
            {
                existing = new IntegrationSecret
                {
                    Id = Guid.NewGuid().ToString(),
                    ConnectionId = connectionId
                };
                _context.IntegrationSecrets.Add(existing);
            }

            var serialized = JsonSerializer.Serialize(secrets, _jsonOptions);
            var encrypted = await _cryptoService.EncryptAsync(serialized, cancellationToken);

            existing.ProviderKey = providerKey.Trim().ToLowerInvariant();
            existing.SecretJson = encrypted.Payload;
            existing.EncryptionProvider = _cryptoService.EncryptionProviderId;
            existing.EncryptionKeyId = encrypted.KeyId;
            existing.UpdatedAt = DateTime.UtcNow;
        }

        public async Task DeleteAsync(
            string connectionId,
            IntegrationSecretScope scope,
            CancellationToken cancellationToken)
        {
            _accessPolicy.EnsureAllowed(scope, IntegrationSecretOperation.Delete);

            if (string.IsNullOrWhiteSpace(connectionId))
            {
                return;
            }

            var existing = await _context.IntegrationSecrets
                .FirstOrDefaultAsync(s => s.ConnectionId == connectionId, cancellationToken);

            if (existing != null)
            {
                _context.IntegrationSecrets.Remove(existing);
            }
        }

        public async Task<int> RotateOutdatedSecretsAsync(
            IntegrationSecretScope scope,
            CancellationToken cancellationToken)
        {
            _accessPolicy.EnsureAllowed(scope, IntegrationSecretOperation.Rotate);

            var rotateEnabled = _configuration.GetValue("Security:IntegrationSecrets:RotationEnabled", true);
            if (!rotateEnabled)
            {
                return 0;
            }

            var activeKeyId = await _cryptoService.GetActiveKeyIdAsync(cancellationToken);
            var records = await _context.IntegrationSecrets
                .ToListAsync(cancellationToken);

            var rotated = 0;
            foreach (var record in records)
            {
                try
                {
                    var decrypted = await _cryptoService.DecryptAsync(record.SecretJson, cancellationToken);
                    if (!decrypted.ShouldRotate &&
                        string.Equals(record.EncryptionKeyId, activeKeyId, StringComparison.Ordinal) &&
                        string.Equals(record.EncryptionProvider, _cryptoService.EncryptionProviderId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var encrypted = await _cryptoService.EncryptAsync(decrypted.Plaintext, cancellationToken);
                    record.SecretJson = encrypted.Payload;
                    record.EncryptionProvider = _cryptoService.EncryptionProviderId;
                    record.EncryptionKeyId = encrypted.KeyId;
                    record.UpdatedAt = DateTime.UtcNow;
                    rotated++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Integration secret rotation failed. ConnectionId={ConnectionId} ProviderKey={ProviderKey}",
                        record.ConnectionId,
                        record.ProviderKey);
                }
            }

            return rotated;
        }
    }

    public sealed class IntegrationSecretMaterial
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

        public bool IsEmpty()
        {
            return string.IsNullOrWhiteSpace(ApiKey)
                   && string.IsNullOrWhiteSpace(ApiSecret)
                   && string.IsNullOrWhiteSpace(AccessToken)
                   && string.IsNullOrWhiteSpace(RefreshToken)
                   && string.IsNullOrWhiteSpace(TokenType)
                   && string.IsNullOrWhiteSpace(Scope)
                   && !ExpiresAtUtc.HasValue
                   && string.IsNullOrWhiteSpace(RealmId)
                   && string.IsNullOrWhiteSpace(TenantId)
                   && string.IsNullOrWhiteSpace(TenantName)
                   && string.IsNullOrWhiteSpace(CalendarId)
                   && string.IsNullOrWhiteSpace(ExternalAccountId)
                   && string.IsNullOrWhiteSpace(AccountEmail)
                   && string.IsNullOrWhiteSpace(AccountLabel);
        }
    }
}
