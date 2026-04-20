using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using JurisFlow.Server.Contracts;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using ThreadingTask = System.Threading.Tasks.Task;

namespace JurisFlow.Server.Services
{
    public sealed class ClientApplicationService
    {
        private readonly JurisFlowDbContext _context;
        private readonly AuditLogger _auditLogger;
        private readonly PasswordPolicyService _passwordPolicy;
        private readonly IConfiguration _configuration;
        private readonly TenantContext _tenantContext;
        private readonly ClientRequestValidator _validator;
        private readonly BusinessSurfaceAuthorizationService _authorization;
        private readonly RefactorWaveOptions _options;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public ClientApplicationService(
            JurisFlowDbContext context,
            AuditLogger auditLogger,
            PasswordPolicyService passwordPolicy,
            IConfiguration configuration,
            TenantContext tenantContext,
            ClientRequestValidator validator,
            BusinessSurfaceAuthorizationService authorization,
            IOptions<RefactorWaveOptions> options,
            IHttpContextAccessor httpContextAccessor)
        {
            _context = context;
            _auditLogger = auditLogger;
            _passwordPolicy = passwordPolicy;
            _configuration = configuration;
            _tenantContext = tenantContext;
            _validator = validator;
            _authorization = authorization;
            _options = options.Value;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task<IReadOnlyList<ClientListItemResponse>> GetClientsAsync()
        {
            IQueryable<Client> query = TenantScope(_context.Clients).AsNoTracking();
            if (ShouldHideSeedClient())
            {
                var demoEmail = NormalizeEmail(GetSeedClientEmail());
                query = query.Where(c => c.NormalizedEmail != demoEmail);
            }

            var clients = await query
                .OrderByDescending(c => c.CreatedAt)
                .ToListAsync();

            return clients.Select(ClientListItemResponse.FromModel).ToList();
        }

        public async Task<ClientReadModelCollectionResponse> GetClientReadModelPageAsync(
            int page,
            int pageSize,
            string? search = null,
            string? status = null)
        {
            IQueryable<Client> query = TenantScope(_context.Clients).AsNoTracking();
            if (ShouldHideSeedClient())
            {
                var demoEmail = NormalizeEmail(GetSeedClientEmail());
                query = query.Where(c => c.NormalizedEmail != demoEmail);
            }

            var normalizedSearch = search?.Trim().ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(normalizedSearch))
            {
                query = query.Where(c =>
                    c.Name.ToLower().Contains(normalizedSearch) ||
                    c.Email.ToLower().Contains(normalizedSearch) ||
                    (c.Phone != null && c.Phone.ToLower().Contains(normalizedSearch)) ||
                    (c.Mobile != null && c.Mobile.ToLower().Contains(normalizedSearch)) ||
                    (c.Company != null && c.Company.ToLower().Contains(normalizedSearch)) ||
                    (c.ClientNumber != null && c.ClientNumber.ToLower().Contains(normalizedSearch)));
            }

            var normalizedStatus = status?.Trim();
            if (!string.IsNullOrWhiteSpace(normalizedStatus) &&
                !string.Equals(normalizedStatus, "all", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(c => c.Status == normalizedStatus);
            }

            var totalCount = await query.CountAsync();
            var items = await query
                .OrderByDescending(c => c.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(c => new ClientListItemResponse
                {
                    Id = c.Id,
                    ClientNumber = c.ClientNumber,
                    Name = c.Name,
                    Email = c.Email,
                    Phone = c.Phone,
                    Mobile = c.Mobile,
                    Company = c.Company,
                    Type = c.Type,
                    Status = c.Status,
                    PortalEnabled = c.PortalEnabled,
                    CreatedAt = c.CreatedAt,
                    UpdatedAt = c.UpdatedAt
                })
                .ToListAsync();

            return new ClientReadModelCollectionResponse
            {
                Items = items,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize,
                HasMore = (page * pageSize) < totalCount
            };
        }

        public async Task<ClientDetailResponse?> GetClientAsync(string id)
        {
            var client = await TenantScope(_context.Clients)
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == id);

            if (client == null || IsSeedClientHidden(client))
            {
                return null;
            }

            return ClientDetailResponse.FromModel(client);
        }

        public async Task<IReadOnlyList<ClientStatusHistoryResponse>?> GetStatusHistoryAsync(string id)
        {
            IQueryable<Client> query = TenantScope(_context.Clients).AsNoTracking();
            if (ShouldHideSeedClient())
            {
                var demoEmail = NormalizeEmail(GetSeedClientEmail());
                query = query.Where(c => c.NormalizedEmail != demoEmail);
            }

            var exists = await query.AnyAsync(c => c.Id == id);
            if (!exists)
            {
                return null;
            }

            var history = await TenantScope(_context.ClientStatusHistories)
                .AsNoTracking()
                .Where(h => h.ClientId == id)
                .OrderByDescending(h => h.CreatedAt)
                .ToListAsync();

            return history.Select(ClientStatusHistoryResponse.FromModel).ToList();
        }

        public async Task<ApplicationServiceResult<ClientDetailResponse>> CreateClientAsync(ClientCreateRequest request)
        {
            var validation = _validator.ValidateForCreateOrReplace(request);
            if (!validation.Succeeded || validation.Value == null)
            {
                return ApplicationServiceResult<ClientDetailResponse>.Failure(validation.StatusCode, validation.Title!, validation.Detail!);
            }

            var duplicateEmail = await TenantScope(_context.Clients)
                .AnyAsync(c => c.NormalizedEmail == validation.Value.NormalizedEmail);
            if (duplicateEmail)
            {
                return ApplicationServiceResult<ClientDetailResponse>.Failure(StatusCodes.Status400BadRequest, "Duplicate client", "Email already exists.");
            }

            var synchronizedCompany = await ResolveTenantCompanyNameAsync(validation.Value.Company);
            var client = new Client
            {
                Id = Guid.NewGuid().ToString(),
                ClientNumber = validation.Value.ClientNumber,
                Name = validation.Value.Name,
                Email = validation.Value.Email,
                NormalizedEmail = validation.Value.NormalizedEmail,
                Phone = validation.Value.Phone,
                Mobile = validation.Value.Mobile,
                Company = synchronizedCompany,
                Type = validation.Value.Type,
                Status = validation.Value.Status,
                Address = validation.Value.Address,
                City = validation.Value.City,
                State = validation.Value.State,
                ZipCode = validation.Value.ZipCode,
                Country = validation.Value.Country,
                TaxId = validation.Value.TaxId,
                IncorporationState = validation.Value.IncorporationState,
                RegisteredAgent = validation.Value.RegisteredAgent,
                AuthorizedRepresentatives = validation.Value.AuthorizedRepresentatives,
                Notes = validation.Value.Notes,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.Clients.Add(client);
            _context.ClientStatusHistories.Add(new ClientStatusHistory
            {
                ClientId = client.Id,
                PreviousStatus = "New",
                NewStatus = client.Status,
                ChangedByUserId = GetUserId(),
                ChangedByName = GetUserEmail(),
                CreatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();
            await LogAuditAsync("client.create", "Client", client.Id, $"Created client {client.Email}");

            return ApplicationServiceResult<ClientDetailResponse>.Success(ClientDetailResponse.FromModel(client));
        }

        public async Task<ApplicationServiceResult<ClientDetailResponse>> ReplaceClientAsync(string id, ClientReplaceRequest request)
        {
            var existing = await TenantScope(_context.Clients).FirstOrDefaultAsync(c => c.Id == id);
            if (existing == null || IsSeedClientHidden(existing))
            {
                return ApplicationServiceResult<ClientDetailResponse>.Failure(StatusCodes.Status404NotFound, "Client not found", "Client was not found.");
            }

            var validation = _validator.ValidateForCreateOrReplace(new ClientCreateRequest
            {
                ClientNumber = request.ClientNumber,
                Name = request.Name,
                Email = request.Email,
                Phone = request.Phone,
                Mobile = request.Mobile,
                Company = request.Company,
                Type = request.Type,
                Status = request.Status,
                Address = request.Address,
                City = request.City,
                State = request.State,
                ZipCode = request.ZipCode,
                Country = request.Country,
                TaxId = request.TaxId,
                IncorporationState = request.IncorporationState,
                RegisteredAgent = request.RegisteredAgent,
                AuthorizedRepresentatives = request.AuthorizedRepresentatives,
                Notes = request.Notes
            });
            if (!validation.Succeeded || validation.Value == null)
            {
                return ApplicationServiceResult<ClientDetailResponse>.Failure(validation.StatusCode, validation.Title!, validation.Detail!);
            }

            var duplicateEmail = await TenantScope(_context.Clients)
                .AnyAsync(c => c.NormalizedEmail == validation.Value.NormalizedEmail && c.Id != id);
            if (duplicateEmail)
            {
                return ApplicationServiceResult<ClientDetailResponse>.Failure(StatusCodes.Status400BadRequest, "Duplicate client", "Email already exists.");
            }

            var previousStatus = existing.Status;
            ApplyWriteModel(existing, validation.Value, await ResolveTenantCompanyNameAsync(validation.Value.Company));

            if (!string.Equals(previousStatus, existing.Status, StringComparison.OrdinalIgnoreCase))
            {
                AddStatusHistory(existing.Id, previousStatus, existing.Status, null);
            }

            await _context.SaveChangesAsync();
            await LogAuditAsync("client.update", "Client", existing.Id, $"Updated client {existing.Email}");

            return ApplicationServiceResult<ClientDetailResponse>.Success(ClientDetailResponse.FromModel(existing));
        }

        public async Task<ApplicationServiceResult<ClientDetailResponse>> PatchClientAsync(string id, ClientPatchRequest request)
        {
            var client = await TenantScope(_context.Clients).FirstOrDefaultAsync(c => c.Id == id);
            if (client == null || IsSeedClientHidden(client))
            {
                return ApplicationServiceResult<ClientDetailResponse>.Failure(StatusCodes.Status404NotFound, "Client not found", "Client was not found.");
            }

            var validation = _validator.ValidateForPatch(request);
            if (!validation.Succeeded || validation.Value == null)
            {
                return ApplicationServiceResult<ClientDetailResponse>.Failure(validation.StatusCode, validation.Title!, validation.Detail!);
            }

            if (!string.IsNullOrWhiteSpace(validation.Value.NormalizedEmail))
            {
                var duplicateEmail = await TenantScope(_context.Clients)
                    .AnyAsync(c => c.NormalizedEmail == validation.Value.NormalizedEmail && c.Id != id);
                if (duplicateEmail)
                {
                    return ApplicationServiceResult<ClientDetailResponse>.Failure(StatusCodes.Status400BadRequest, "Duplicate client", "Email already exists.");
                }
            }

            var previousStatus = client.Status;

            if (validation.Value.ClientNumber != null) client.ClientNumber = validation.Value.ClientNumber;
            if (validation.Value.Name != null) client.Name = validation.Value.Name;
            if (validation.Value.Email != null)
            {
                client.Email = validation.Value.Email;
                client.NormalizedEmail = validation.Value.NormalizedEmail!;
            }
            if (validation.Value.Phone != null) client.Phone = validation.Value.Phone;
            if (validation.Value.Mobile != null) client.Mobile = validation.Value.Mobile;
            if (validation.Value.Type != null) client.Type = validation.Value.Type;
            if (validation.Value.Status != null) client.Status = validation.Value.Status;
            if (validation.Value.Address != null) client.Address = validation.Value.Address;
            if (validation.Value.City != null) client.City = validation.Value.City;
            if (validation.Value.State != null) client.State = validation.Value.State;
            if (validation.Value.ZipCode != null) client.ZipCode = validation.Value.ZipCode;
            if (validation.Value.Country != null) client.Country = validation.Value.Country;
            if (validation.Value.TaxId != null) client.TaxId = validation.Value.TaxId;
            if (validation.Value.IncorporationState != null) client.IncorporationState = validation.Value.IncorporationState;
            if (validation.Value.RegisteredAgent != null) client.RegisteredAgent = validation.Value.RegisteredAgent;
            if (validation.Value.AuthorizedRepresentatives != null) client.AuthorizedRepresentatives = validation.Value.AuthorizedRepresentatives;
            if (validation.Value.Notes != null) client.Notes = validation.Value.Notes;
            if (validation.Value.Company != null)
            {
                client.Company = await ResolveTenantCompanyNameAsync(validation.Value.Company);
            }

            if (validation.Value.PortalEnabled.HasValue)
            {
                if (validation.Value.PortalEnabled.Value && string.IsNullOrWhiteSpace(client.PasswordHash))
                {
                    return ApplicationServiceResult<ClientDetailResponse>.Failure(StatusCodes.Status400BadRequest, "Invalid client update", "Password is required before enabling portal access.");
                }

                client.PortalEnabled = validation.Value.PortalEnabled.Value;
            }

            client.UpdatedAt = DateTime.UtcNow;

            if (!string.Equals(previousStatus, client.Status, StringComparison.OrdinalIgnoreCase))
            {
                AddStatusHistory(client.Id, previousStatus, client.Status, validation.Value.StatusChangeNote);
            }

            await _context.SaveChangesAsync();
            await LogAuditAsync("client.update", "Client", client.Id, $"Updated client {client.Email}");

            return ApplicationServiceResult<ClientDetailResponse>.Success(ClientDetailResponse.FromModel(client));
        }

        public async Task<ApplicationServiceResult<object>> ArchiveClientAsync(string id)
        {
            if (!_authorization.CanArchiveClient(CurrentUser))
            {
                return ApplicationServiceResult<object>.Failure(StatusCodes.Status403Forbidden, "Forbidden", "You do not have permission to archive clients.");
            }

            var client = await TenantScope(_context.Clients).FirstOrDefaultAsync(c => c.Id == id);
            if (client == null || IsSeedClientHidden(client))
            {
                return ApplicationServiceResult<object>.Failure(StatusCodes.Status404NotFound, "Client not found", "Client was not found.");
            }

            var previousStatus = client.Status;
            client.Status = "Inactive";
            client.PortalEnabled = false;
            client.UpdatedAt = DateTime.UtcNow;

            if (!string.Equals(previousStatus, client.Status, StringComparison.OrdinalIgnoreCase))
            {
                AddStatusHistory(client.Id, previousStatus, client.Status, "Archived via delete endpoint (hard delete disabled).");
            }

            await _context.SaveChangesAsync();
            await LogAuditAsync("client.archive", "Client", id, $"Archived client {client.Email}; hard delete disabled.");

            return ApplicationServiceResult<object>.Success(new { message = "Client archived. Hard delete is disabled." });
        }

        public async Task<ApplicationServiceResult<object>> SetPortalPasswordAsync(string id, ClientSetPortalPasswordRequest request)
        {
            if (!_authorization.CanManageClientPortal(CurrentUser))
            {
                return ApplicationServiceResult<object>.Failure(StatusCodes.Status403Forbidden, "Forbidden", "You do not have permission to manage client portal passwords.");
            }

            var passwordValidation = _validator.ValidatePortalPassword(request);
            if (!passwordValidation.Succeeded || passwordValidation.Value == null)
            {
                return ApplicationServiceResult<object>.Failure(passwordValidation.StatusCode, passwordValidation.Title!, passwordValidation.Detail!);
            }

            var client = await TenantScope(_context.Clients).FirstOrDefaultAsync(c => c.Id == id);
            if (client == null || IsSeedClientHidden(client))
            {
                return ApplicationServiceResult<object>.Failure(StatusCodes.Status404NotFound, "Client not found", "Client was not found.");
            }

            var passwordResult = _passwordPolicy.Validate(passwordValidation.Value, client.Email, client.Name);
            if (!passwordResult.IsValid)
            {
                return ApplicationServiceResult<object>.Failure(StatusCodes.Status400BadRequest, "Invalid password", passwordResult.Message);
            }

            client.PasswordHash = PasswordHashingHelper.HashPassword(passwordValidation.Value, _configuration);
            client.PortalEnabled = true;
            client.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await LogAuditAsync("client.portal.password_set", "Client", client.Id, $"Portal password set for client {client.Email}");

            return ApplicationServiceResult<object>.Success(new { message = "Password set successfully", portalEnabled = true });
        }

        private IQueryable<T> TenantScope<T>(IQueryable<T> query) where T : class
        {
            var tenantId = RequireTenantId();
            return query.Where(e => EF.Property<string>(e, "TenantId") == tenantId);
        }

        private string RequireTenantId()
        {
            if (string.IsNullOrWhiteSpace(_tenantContext.TenantId))
            {
                throw new InvalidOperationException("Tenant context is required.");
            }

            return _tenantContext.TenantId;
        }

        private ClaimsPrincipal CurrentUser => _httpContextAccessor.HttpContext?.User ?? new ClaimsPrincipal();

        private string? GetUserId()
        {
            return CurrentUser.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? CurrentUser.FindFirst("sub")?.Value;
        }

        private string? GetUserEmail()
        {
            return CurrentUser.FindFirst(ClaimTypes.Email)?.Value ?? CurrentUser.FindFirst("email")?.Value;
        }

        private void ApplyWriteModel(Client client, ClientWriteModel model, string? company)
        {
            client.ClientNumber = model.ClientNumber;
            client.Name = model.Name;
            client.Email = model.Email;
            client.NormalizedEmail = model.NormalizedEmail;
            client.Phone = model.Phone;
            client.Mobile = model.Mobile;
            client.Company = company;
            client.Type = model.Type;
            client.Status = model.Status;
            client.Address = model.Address;
            client.City = model.City;
            client.State = model.State;
            client.ZipCode = model.ZipCode;
            client.Country = model.Country;
            client.TaxId = model.TaxId;
            client.IncorporationState = model.IncorporationState;
            client.RegisteredAgent = model.RegisteredAgent;
            client.AuthorizedRepresentatives = model.AuthorizedRepresentatives;
            client.Notes = model.Notes;
            client.UpdatedAt = DateTime.UtcNow;
        }

        private void AddStatusHistory(string clientId, string? previousStatus, string? newStatus, string? notes)
        {
            _context.ClientStatusHistories.Add(new ClientStatusHistory
            {
                ClientId = clientId,
                PreviousStatus = previousStatus ?? "Unknown",
                NewStatus = newStatus ?? "Unknown",
                Notes = notes,
                ChangedByUserId = GetUserId(),
                ChangedByName = GetUserEmail(),
                CreatedAt = DateTime.UtcNow
            });
        }

        private bool ShouldHideSeedClient()
        {
            var explicitHide = _configuration.GetValue<bool?>("Seed:HidePortalClient");
            if (explicitHide.HasValue)
            {
                return explicitHide.Value;
            }

            return !_configuration.GetValue("Seed:PortalClientEnabled", false);
        }

        private string GetSeedClientEmail()
        {
            return _configuration["Seed:PortalClientEmail"] ?? "client.demo@jurisflow.local";
        }

        private bool IsSeedClientHidden(Client client)
        {
            if (!ShouldHideSeedClient())
            {
                return false;
            }

            return string.Equals(client.NormalizedEmail, NormalizeEmail(GetSeedClientEmail()), StringComparison.Ordinal);
        }

        private static string NormalizeEmail(string? email)
        {
            return EmailAddressNormalizer.Normalize(email);
        }

        private async Task<string?> ResolveTenantCompanyNameAsync(string? fallbackCompany)
        {
            if (!_options.UseTenantCompanySynchronization)
            {
                return string.IsNullOrWhiteSpace(fallbackCompany) ? null : fallbackCompany.Trim();
            }

            var tenantId = RequireTenantId();
            var tenantName = await _context.Tenants
                .AsNoTracking()
                .Where(t => t.Id == tenantId)
                .Select(t => t.Name)
                .FirstOrDefaultAsync();

            if (!string.IsNullOrWhiteSpace(tenantName))
            {
                return tenantName.Trim();
            }

            return string.IsNullOrWhiteSpace(fallbackCompany) ? null : fallbackCompany.Trim();
        }

        private ThreadingTask LogAuditAsync(string action, string entityType, string entityId, string details)
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext == null)
            {
                return ThreadingTask.CompletedTask;
            }

            return _auditLogger.LogAsync(httpContext, action, entityType, entityId, details);
        }
    }
}
