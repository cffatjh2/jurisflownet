using System.Data;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JurisFlow.Server.Contracts;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Task = System.Threading.Tasks.Task;

namespace JurisFlow.Server.Services
{
    public sealed class TrustAccountingService
    {
        private const int MaxTrustIdempotencyKeyLength = 160;
        private readonly JurisFlowDbContext _context;
        private readonly AuditLogger _auditLogger;
        private readonly BillingPeriodLockService _billingPeriodLockService;
        private readonly FirmStructureService _firmStructure;
        private readonly TrustRiskRadarService _trustRiskRadarService;
        private readonly TrustActionAuthorizationService _authorization;
        private readonly TrustPolicyResolverService _policyResolver;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IOptionsMonitor<TrustAccountingOptions> _optionsMonitor;

        public TrustAccountingService(
            JurisFlowDbContext context,
            AuditLogger auditLogger,
            BillingPeriodLockService billingPeriodLockService,
            FirmStructureService firmStructure,
            TrustRiskRadarService trustRiskRadarService,
            TrustActionAuthorizationService authorization,
            TrustPolicyResolverService policyResolver,
            IHttpContextAccessor httpContextAccessor,
            IOptionsMonitor<TrustAccountingOptions> optionsMonitor)
        {
            _context = context;
            _auditLogger = auditLogger;
            _billingPeriodLockService = billingPeriodLockService;
            _firmStructure = firmStructure;
            _trustRiskRadarService = trustRiskRadarService;
            _authorization = authorization;
            _policyResolver = policyResolver;
            _httpContextAccessor = httpContextAccessor;
            _optionsMonitor = optionsMonitor;
        }

        public async Task<TrustBankAccount> CreateTrustAccountAsync(CreateTrustAccountRequest request, CancellationToken ct = default)
        {
            var accountNumber = string.IsNullOrWhiteSpace(request.AccountNumberEnc)
                ? request.AccountNumber?.Trim()
                : request.AccountNumberEnc.Trim();

            if (string.IsNullOrWhiteSpace(request.Name) ||
                string.IsNullOrWhiteSpace(request.BankName) ||
                string.IsNullOrWhiteSpace(request.RoutingNumber) ||
                string.IsNullOrWhiteSpace(accountNumber) ||
                string.IsNullOrWhiteSpace(request.Jurisdiction))
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Name, bank, routing number, account number, and jurisdiction are required.");
            }

            if (request.RoutingNumber.Trim().Length != 9 || !request.RoutingNumber.Trim().All(char.IsDigit))
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Routing number must be exactly 9 digits.");
            }

            var resolved = await _firmStructure.ResolveEntityOfficeAsync(request.EntityId, request.OfficeId);
            var account = new TrustBankAccount
            {
                Id = Guid.NewGuid().ToString(),
                Name = request.Name.Trim(),
                BankName = request.BankName.Trim(),
                RoutingNumber = request.RoutingNumber.Trim(),
                AccountNumberEnc = accountNumber,
                Jurisdiction = request.Jurisdiction.Trim().ToUpperInvariant(),
                AccountType = "iolta",
                StatementCadence = "monthly",
                OverdraftNotificationEnabled = true,
                JurisdictionPolicyKey = TrustPolicyResolverService.BuildBaselinePolicyKey(request.Jurisdiction, "iolta"),
                EntityId = resolved.entityId,
                OfficeId = resolved.officeId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.TrustBankAccounts.Add(account);
            await _context.SaveChangesAsync(ct);
            await LogAsync("trust.account.create", "TrustBankAccount", account.Id, $"Name={account.Name}, Balance={account.CurrentBalance}");
            return account;
        }

        public async Task<ClientTrustLedger> CreateLedgerAsync(ClientTrustLedger ledger, CancellationToken ct = default)
        {
            var account = await _context.TrustBankAccounts.FirstOrDefaultAsync(a => a.Id == ledger.TrustAccountId, ct);
            if (account == null)
            {
                throw new TrustCommandException(StatusCodes.Status404NotFound, "Trust account not found");
            }

            if (account.Status != TrustAccountStatus.ACTIVE)
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Trust account is not active.");
            }

            var client = await _context.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == ledger.ClientId, ct);
            if (client == null)
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Client not found");
            }

            if (!string.IsNullOrWhiteSpace(ledger.MatterId))
            {
                var matter = await _context.Matters.AsNoTracking().FirstOrDefaultAsync(m => m.Id == ledger.MatterId, ct);
                if (matter == null)
                {
                    throw new TrustCommandException(StatusCodes.Status400BadRequest, "Matter not found");
                }

                if (!string.Equals(matter.ClientId, ledger.ClientId, StringComparison.Ordinal))
                {
                    throw new TrustCommandException(StatusCodes.Status400BadRequest, "Selected matter does not belong to the selected client.");
                }
            }

            ledger.Id = Guid.NewGuid().ToString();
            ledger.EntityId = account.EntityId;
            ledger.OfficeId = account.OfficeId;
            ledger.CreatedAt = DateTime.UtcNow;
            ledger.UpdatedAt = DateTime.UtcNow;

            _context.ClientTrustLedgers.Add(ledger);
            await _context.SaveChangesAsync(ct);
            await LogAsync("trust.ledger.create", "ClientTrustLedger", ledger.Id, $"ClientId={ledger.ClientId}, Account={ledger.TrustAccountId}");
            return ledger;
        }

        public async Task<TrustAccountGovernanceDto> GetAccountGovernanceAsync(string trustAccountId, CancellationToken ct = default)
        {
            _authorization.EnsureAllowed(TrustActionKeys.ManageGovernance, GetCurrentUser());
            var resolved = await _policyResolver.ResolveEffectivePolicyAsync(trustAccountId, ct);
            return TrustPolicyResolverService.ToGovernanceDto(resolved.Account);
        }

        public async Task<TrustAccountGovernanceDto> UpdateAccountGovernanceAsync(string trustAccountId, TrustAccountGovernanceDto request, CancellationToken ct = default)
        {
            _authorization.EnsureAllowed(TrustActionKeys.ManageGovernance, GetCurrentUser());

            var account = await _context.TrustBankAccounts.FirstOrDefaultAsync(a => a.Id == trustAccountId, ct);
            if (account == null)
            {
                throw new TrustCommandException(StatusCodes.Status404NotFound, "Trust account not found.");
            }

            account.AccountType = NormalizeAccountType(request.AccountType);
            account.ResponsibleLawyerUserId = NullIfWhiteSpace(request.ResponsibleLawyerUserId);
            account.AllowedSignatoriesJson = JsonSerializer.Serialize(
                (request.AllowedSignatories ?? [])
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim())
                    .Distinct(StringComparer.Ordinal)
                    .ToList());
            account.JurisdictionPolicyKey = string.IsNullOrWhiteSpace(request.JurisdictionPolicyKey)
                ? TrustPolicyResolverService.BuildBaselinePolicyKey(TrustPolicyResolverService.NormalizeJurisdiction(account.Jurisdiction), account.AccountType)
                : request.JurisdictionPolicyKey.Trim();
            account.StatementCadence = NormalizeStatementCadence(request.StatementCadence);
            account.OverdraftNotificationEnabled = request.OverdraftNotificationEnabled;
            account.BankReferenceMetadataJson = request.BankReferenceMetadataJson?.Trim();
            account.UpdatedAt = DateTime.UtcNow;
            account.RowVersion = NewRowVersion();

            await SaveChangesWithConcurrencyHandlingAsync(ct);
            await LogAsync("trust.account.governance.update", "TrustBankAccount", account.Id, $"Policy={account.JurisdictionPolicyKey}, ResponsibleLawyer={account.ResponsibleLawyerUserId}");
            return TrustPolicyResolverService.ToGovernanceDto(account);
        }

        public async Task<IReadOnlyList<TrustJurisdictionPolicyUpsertDto>> GetJurisdictionPoliciesAsync(string? jurisdiction = null, string? accountType = null, CancellationToken ct = default)
        {
            _authorization.EnsureAllowed(TrustActionKeys.ManagePolicies, GetCurrentUser());
            var normalizedJurisdiction = string.IsNullOrWhiteSpace(jurisdiction)
                ? null
                : TrustPolicyResolverService.NormalizeJurisdiction(jurisdiction);
            var normalizedAccountType = string.IsNullOrWhiteSpace(accountType)
                ? null
                : TrustPolicyResolverService.NormalizePolicyAccountType(accountType);

            var policies = await _context.TrustJurisdictionPolicies.AsNoTracking()
                .Where(p => normalizedJurisdiction == null || p.Jurisdiction == normalizedJurisdiction || p.Jurisdiction == "DEFAULT")
                .Where(p => normalizedAccountType == null || p.AccountType == normalizedAccountType || p.AccountType == "all")
                .OrderBy(p => p.Jurisdiction)
                .ThenBy(p => p.PolicyKey)
                .ThenBy(p => p.AccountType)
                .ThenByDescending(p => p.VersionNumber)
                .ToListAsync(ct);

            return policies.Select(TrustPolicyResolverService.ToUpsertDto).ToList();
        }

        public async Task<TrustJurisdictionPolicyUpsertDto> UpsertJurisdictionPolicyAsync(TrustJurisdictionPolicyUpsertDto request, CancellationToken ct = default)
        {
            _authorization.EnsureAllowed(TrustActionKeys.ManagePolicies, GetCurrentUser());

            if (string.IsNullOrWhiteSpace(request.PolicyKey))
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Policy key is required.");
            }

            var normalizedJurisdiction = TrustPolicyResolverService.NormalizeJurisdiction(request.Jurisdiction);
            var policyKey = request.PolicyKey.Trim();
            var policyAccountType = TrustPolicyResolverService.NormalizePolicyAccountType(request.AccountType);
            var versionNumber = request.VersionNumber <= 0 ? 1 : request.VersionNumber;
            var policy = await _context.TrustJurisdictionPolicies
                .FirstOrDefaultAsync(p =>
                    p.PolicyKey == policyKey &&
                    p.Jurisdiction == normalizedJurisdiction &&
                    p.AccountType == policyAccountType &&
                    p.VersionNumber == versionNumber,
                    ct);

            if (policy == null)
            {
                policy = new TrustJurisdictionPolicy
                {
                    Id = Guid.NewGuid().ToString(),
                    PolicyKey = policyKey,
                    Jurisdiction = normalizedJurisdiction,
                    AccountType = policyAccountType,
                    VersionNumber = versionNumber,
                    CreatedAt = DateTime.UtcNow
                };
                _context.TrustJurisdictionPolicies.Add(policy);
            }

            policy.Name = request.Name?.Trim();
            policy.AccountType = policyAccountType;
            policy.VersionNumber = versionNumber;
            policy.IsActive = request.IsActive;
            policy.IsSystemBaseline = request.IsSystemBaseline;
            policy.RequireMakerChecker = request.RequireMakerChecker;
            policy.RequireOverrideReason = request.RequireOverrideReason;
            policy.DualApprovalThreshold = NormalizeMoney(request.DualApprovalThreshold);
            policy.ResponsibleLawyerApprovalThreshold = NormalizeMoney(request.ResponsibleLawyerApprovalThreshold);
            policy.SignatoryApprovalThreshold = NormalizeMoney(request.SignatoryApprovalThreshold);
            policy.MonthlyCloseCadenceDays = Math.Clamp(request.MonthlyCloseCadenceDays, 1, 120);
            policy.ExceptionAgingSlaHours = Math.Clamp(request.ExceptionAgingSlaHours, 1, 24 * 60);
            policy.RetentionPeriodMonths = Math.Clamp(request.RetentionPeriodMonths, 12, 240);
            policy.RequireMonthlyThreeWayReconciliation = request.RequireMonthlyThreeWayReconciliation;
            policy.RequireResponsibleLawyerAssignment = request.RequireResponsibleLawyerAssignment;
            policy.DisbursementClassesRequiringSignatoryJson = JsonSerializer.Serialize(NormalizeStringList(request.DisbursementClassesRequiringSignatory, normalizeLower: true));
            policy.OperationalApproverRolesJson = JsonSerializer.Serialize(NormalizeStringList(request.OperationalApproverRoles, normalizeLower: false));
            policy.OverrideApproverRolesJson = JsonSerializer.Serialize(NormalizeStringList(request.OverrideApproverRoles, normalizeLower: false));
            policy.MetadataJson = request.MetadataJson?.Trim();
            policy.UpdatedAt = DateTime.UtcNow;

            await SaveChangesWithConcurrencyHandlingAsync(ct);
            await LogAsync("trust.policy.upsert", "TrustJurisdictionPolicy", policy.Id, $"PolicyKey={policy.PolicyKey}, Jurisdiction={policy.Jurisdiction}");
            return TrustPolicyResolverService.ToUpsertDto(policy);
        }

        public async Task<IReadOnlyList<TrustJurisdictionPacketTemplateUpsertDto>> GetPacketTemplatesAsync(string? jurisdiction = null, string? accountType = null, string? policyKey = null, CancellationToken ct = default)
        {
            _authorization.EnsureAllowed(TrustActionKeys.ManagePolicies, GetCurrentUser());

            var normalizedJurisdiction = string.IsNullOrWhiteSpace(jurisdiction)
                ? null
                : TrustPolicyResolverService.NormalizeJurisdiction(jurisdiction);
            var normalizedAccountType = string.IsNullOrWhiteSpace(accountType)
                ? null
                : TrustPolicyResolverService.NormalizePolicyAccountType(accountType);
            var normalizedPolicyKey = string.IsNullOrWhiteSpace(policyKey) ? null : policyKey.Trim();

            var templates = await _context.TrustJurisdictionPacketTemplates.AsNoTracking()
                .Where(t => normalizedJurisdiction == null || t.Jurisdiction == normalizedJurisdiction || t.Jurisdiction == "DEFAULT")
                .Where(t => normalizedAccountType == null || t.AccountType == normalizedAccountType || t.AccountType == "all")
                .Where(t => normalizedPolicyKey == null || t.PolicyKey == normalizedPolicyKey)
                .OrderBy(t => t.Jurisdiction)
                .ThenBy(t => t.PolicyKey)
                .ThenBy(t => t.AccountType)
                .ThenByDescending(t => t.VersionNumber)
                .ToListAsync(ct);

            return templates.Select(TrustPolicyResolverService.ToTemplateUpsertDto).ToList();
        }

        public async Task<TrustJurisdictionPacketTemplateUpsertDto> UpsertPacketTemplateAsync(TrustJurisdictionPacketTemplateUpsertDto request, CancellationToken ct = default)
        {
            _authorization.EnsureAllowed(TrustActionKeys.ManagePolicies, GetCurrentUser());

            if (string.IsNullOrWhiteSpace(request.PolicyKey))
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Policy key is required.");
            }

            if (string.IsNullOrWhiteSpace(request.TemplateKey))
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Template key is required.");
            }

            var normalizedJurisdiction = TrustPolicyResolverService.NormalizeJurisdiction(request.Jurisdiction);
            var normalizedAccountType = TrustPolicyResolverService.NormalizePolicyAccountType(request.AccountType);
            var versionNumber = request.VersionNumber <= 0 ? 1 : request.VersionNumber;
            var template = await _context.TrustJurisdictionPacketTemplates.FirstOrDefaultAsync(
                t => t.PolicyKey == request.PolicyKey.Trim() &&
                     t.Jurisdiction == normalizedJurisdiction &&
                     t.AccountType == normalizedAccountType &&
                     t.TemplateKey == request.TemplateKey.Trim() &&
                     t.VersionNumber == versionNumber,
                ct);

            if (template == null)
            {
                template = new TrustJurisdictionPacketTemplate
                {
                    Id = Guid.NewGuid().ToString(),
                    PolicyKey = request.PolicyKey.Trim(),
                    Jurisdiction = normalizedJurisdiction,
                    AccountType = normalizedAccountType,
                    TemplateKey = request.TemplateKey.Trim(),
                    VersionNumber = versionNumber,
                    CreatedAt = DateTime.UtcNow
                };
                _context.TrustJurisdictionPacketTemplates.Add(template);
            }

            template.Name = request.Name?.Trim();
            template.IsActive = request.IsActive;
            template.RequiredSectionsJson = JsonSerializer.Serialize(NormalizeStringList(request.RequiredSections, normalizeLower: true));
            template.RequiredAttestationsJson = JsonSerializer.Serialize(
                (request.RequiredAttestations ?? [])
                    .Where(x => !string.IsNullOrWhiteSpace(x.Key))
                    .Select(x => new TrustPacketTemplateAttestationDto
                    {
                        Key = x.Key.Trim().ToLowerInvariant(),
                        Label = string.IsNullOrWhiteSpace(x.Label) ? x.Key.Trim() : x.Label.Trim(),
                        Role = NormalizeMonthCloseRole(x.Role),
                        HelpText = x.HelpText?.Trim(),
                        Required = x.Required
                    })
                    .GroupBy(x => $"{x.Role}:{x.Key}", StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .ToList());
            template.DisclosureBlocksJson = JsonSerializer.Serialize(NormalizeStringList(request.DisclosureBlocks, normalizeLower: true));
            template.RenderingProfileJson = request.RenderingProfileJson?.Trim();
            template.MetadataJson = request.MetadataJson?.Trim();
            template.UpdatedAt = DateTime.UtcNow;

            await SaveChangesWithConcurrencyHandlingAsync(ct);
            await LogAsync("trust.packet_template.upsert", nameof(TrustJurisdictionPacketTemplate), template.Id, $"PolicyKey={template.PolicyKey}, TemplateKey={template.TemplateKey}");
            return TrustPolicyResolverService.ToTemplateUpsertDto(template);
        }

        public async Task<TrustTransactionApprovalStateDto> GetTransactionApprovalStateAsync(string trustTransactionId, CancellationToken ct = default)
        {
            var tx = await _context.TrustTransactions.AsNoTracking().FirstOrDefaultAsync(t => t.Id == trustTransactionId, ct);
            if (tx == null)
            {
                throw new TrustCommandException(StatusCodes.Status404NotFound, "Transaction not found.");
            }

            var requirements = await _context.TrustApprovalRequirements.AsNoTracking()
                .Where(r => r.TrustTransactionId == trustTransactionId)
                .OrderBy(r => r.RequirementType)
                .ThenBy(r => r.CreatedAt)
                .ToListAsync(ct);
            var decisions = await _context.TrustApprovalDecisions.AsNoTracking()
                .Where(d => d.TrustTransactionId == trustTransactionId)
                .OrderBy(d => d.CreatedAt)
                .ToListAsync(ct);
            var hasOverride = await _context.TrustApprovalOverrides.AsNoTracking()
                .AnyAsync(o => o.TrustTransactionId == trustTransactionId, ct);

            return BuildApprovalStateDto(tx, requirements, decisions, hasOverride);
        }

        public async Task<IReadOnlyList<TrustApprovalQueueItemDto>> GetApprovalQueueAsync(string? trustAccountId = null, CancellationToken ct = default)
        {
            _authorization.EnsureAllowed(TrustActionKeys.ApproveTransaction, GetCurrentUser());

            var query = _context.TrustTransactions.AsNoTracking()
                .Where(t => t.Status == "PENDING" && t.ApprovalStatus != null && t.ApprovalStatus != "not_required");
            if (!string.IsNullOrWhiteSpace(trustAccountId))
            {
                query = query.Where(t => t.TrustAccountId == trustAccountId);
            }

            var transactions = await query
                .OrderByDescending(t => t.CreatedAt)
                .Take(250)
                .ToListAsync(ct);

            var transactionIds = transactions.Select(t => t.Id).ToList();
            var requirements = transactionIds.Count == 0
                ? []
                : await _context.TrustApprovalRequirements.AsNoTracking()
                    .Where(r => transactionIds.Contains(r.TrustTransactionId))
                    .OrderBy(r => r.CreatedAt)
                    .ToListAsync(ct);

            return transactions.Select(tx => new TrustApprovalQueueItemDto
            {
                TrustTransactionId = tx.Id,
                TrustAccountId = tx.TrustAccountId,
                TransactionType = tx.Type,
                DisbursementClass = tx.DisbursementClass,
                Amount = tx.Amount,
                ApprovalStatus = tx.ApprovalStatus ?? "pending",
                CreatedBy = tx.CreatedBy,
                CreatedAt = tx.CreatedAt,
                MatterId = tx.MatterId,
                PolicySummary = TryReadPolicySummary(tx.PolicyDecisionJson),
                Requirements = requirements
                    .Where(r => r.TrustTransactionId == tx.Id)
                    .Select(r => new TrustApprovalRequirementDto
                    {
                        Id = r.Id,
                        TrustTransactionId = r.TrustTransactionId,
                        RequirementType = r.RequirementType,
                        RequiredCount = r.RequiredCount,
                        SatisfiedCount = r.SatisfiedCount,
                        Status = r.Status,
                        Summary = r.Summary
                    })
                    .ToList()
            }).ToList();
        }

        public async Task<IReadOnlyList<TrustMonthCloseDto>> GetMonthClosesAsync(string? trustAccountId = null, bool includeHistory = false, CancellationToken ct = default)
        {
            _authorization.EnsureAllowed(TrustActionKeys.PrepareReconciliationPacket, GetCurrentUser());
            var query = _context.TrustMonthCloses.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(trustAccountId))
            {
                query = query.Where(c => c.TrustAccountId == trustAccountId);
            }

            if (!includeHistory)
            {
                query = query.Where(c => c.IsCanonical);
            }

            var closes = await query
                .OrderByDescending(c => c.PeriodEnd)
                .ThenByDescending(c => c.VersionNumber)
                .ThenByDescending(c => c.PreparedAt)
                .Take(120)
                .ToListAsync(ct);
            var closeIds = closes.Select(c => c.Id).ToList();
            var steps = closeIds.Count == 0
                ? []
                : await _context.TrustMonthCloseSteps.AsNoTracking()
                    .Where(s => closeIds.Contains(s.TrustMonthCloseId))
                    .OrderBy(s => s.StepKey)
                    .ToListAsync(ct);

            return closes.Select(close => BuildMonthCloseDto(close, steps.Where(s => s.TrustMonthCloseId == close.Id).ToList())).ToList();
        }

        public async Task<TrustMonthCloseDto> PrepareMonthCloseAsync(TrustMonthClosePrepareDto request, CancellationToken ct = default)
        {
            _authorization.EnsureAllowed(TrustActionKeys.PrepareReconciliationPacket, GetCurrentUser());

            if (string.IsNullOrWhiteSpace(request.TrustAccountId))
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Trust account is required.");
            }

            if (request.PeriodEnd.Date < request.PeriodStart.Date)
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Month-close period is invalid.");
            }

            var resolvedPolicy = await _policyResolver.ResolveEffectivePolicyAsync(request.TrustAccountId, ct);
            var packetTemplate = await _policyResolver.ResolvePacketTemplateAsync(resolvedPolicy, ct);
            var periodStart = NormalizeUtcDate(request.PeriodStart);
            var periodEnd = NormalizeUtcDate(request.PeriodEnd);

            var packet = !string.IsNullOrWhiteSpace(request.ReconciliationPacketId)
                ? await _context.TrustReconciliationPackets.FirstOrDefaultAsync(p => p.Id == request.ReconciliationPacketId, ct)
                : null;
            if (packet == null && request.AutoGeneratePacket)
            {
                packet = await GenerateReconciliationPacketAsync(new TrustReconciliationPacketCreateDto
                {
                    TrustAccountId = request.TrustAccountId,
                    PeriodStart = periodStart,
                    PeriodEnd = periodEnd,
                    StatementEndingBalance = request.StatementEndingBalance,
                    Notes = request.Notes
                }, ct);
            }

            if (packet == null)
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Month close requires a reconciliation packet.");
            }

            if (!string.Equals(packet.TrustAccountId, request.TrustAccountId, StringComparison.Ordinal))
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Reconciliation packet does not belong to the selected trust account.");
            }

            if (NormalizeUtcDate(packet.PeriodStart) != periodStart || NormalizeUtcDate(packet.PeriodEnd) != periodEnd)
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Reconciliation packet period does not match the month-close period.");
            }

            if (!packet.IsCanonical)
            {
                throw new TrustCommandException(StatusCodes.Status409Conflict, "Month close requires the canonical reconciliation packet for the period.");
            }

            var openExceptions = await _context.TrustOutstandingItems.AsNoTracking()
                .CountAsync(i => i.TrustAccountId == request.TrustAccountId &&
                                 i.PeriodStart == periodStart &&
                                 i.PeriodEnd == periodEnd &&
                                 i.Status == "open", ct);
            var signoffs = await _context.TrustReconciliationSignoffs.AsNoTracking()
                .Where(s => s.TrustReconciliationPacketId == packet.Id)
                .ToListAsync(ct);

            var close = await _context.TrustMonthCloses
                .Where(c => c.TrustAccountId == request.TrustAccountId && c.PeriodEnd == periodEnd && c.IsCanonical)
                .OrderByDescending(c => c.VersionNumber)
                .FirstOrDefaultAsync(ct);
            if (close == null)
            {
                var nextVersionNumber = await _context.TrustMonthCloses.AsNoTracking()
                    .Where(c => c.TrustAccountId == request.TrustAccountId && c.PeriodEnd == periodEnd)
                    .Select(c => (int?)c.VersionNumber)
                    .MaxAsync(ct) ?? 0;

                close = new TrustMonthClose
                {
                    Id = Guid.NewGuid().ToString(),
                    TrustAccountId = request.TrustAccountId,
                    PolicyKey = resolvedPolicy.Policy.PolicyKey,
                    PeriodStart = periodStart,
                    PeriodEnd = periodEnd,
                    VersionNumber = nextVersionNumber + 1,
                    IsCanonical = true,
                    CreatedAt = DateTime.UtcNow
                };
                _context.TrustMonthCloses.Add(close);
            }
            else if (string.Equals(close.Status, "closed", StringComparison.OrdinalIgnoreCase))
            {
                throw new TrustCommandException(StatusCodes.Status409Conflict, "Closed month-close packets are immutable. Supersede or reopen flow is required.");
            }

            close.ReconciliationPacketId = packet.Id;
            close.PolicyKey = resolvedPolicy.Policy.PolicyKey;
            close.OpenExceptionCount = openExceptions;
            close.PreparedBy = RequireCurrentUserId();
            close.PreparedAt = DateTime.UtcNow;
            close.UpdatedAt = DateTime.UtcNow;
            var templateStatus = await BuildMonthCloseTemplateStatusAsync(close, packet, resolvedPolicy, packetTemplate, ct);
            close.SummaryJson = BuildMonthCloseSummaryJson(close, packet, openExceptions, signoffs, resolvedPolicy, templateStatus);

            var steps = await EnsureMonthCloseStepsAsync(close, packet, openExceptions, signoffs, resolvedPolicy, templateStatus, ct);
            close.Status = DetermineMonthCloseStatus(close, steps);

            await SaveChangesWithConcurrencyHandlingAsync(ct);
            await LogAsync("trust.month_close.prepare", "TrustMonthClose", close.Id, $"Packet={packet.Id}, Status={close.Status}");
            return BuildMonthCloseDto(close, steps);
        }

        public async Task<TrustMonthCloseDto> SignoffMonthCloseAsync(string closeId, TrustMonthCloseSignoffDto dto, CancellationToken ct = default)
        {
            _authorization.EnsureAllowed(TrustActionKeys.SignoffReconciliationPacket, GetCurrentUser());

            var close = await _context.TrustMonthCloses.FirstOrDefaultAsync(c => c.Id == closeId, ct);
            if (close == null)
            {
                throw new TrustCommandException(StatusCodes.Status404NotFound, "Month close not found.");
            }

            if (!close.IsCanonical)
            {
                throw new TrustCommandException(StatusCodes.Status409Conflict, "Only the canonical month close can be signed off.");
            }

            var packet = await _context.TrustReconciliationPackets.FirstOrDefaultAsync(p => p.Id == close.ReconciliationPacketId, ct);
            if (packet == null)
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Month close is missing its reconciliation packet.");
            }

            if (!packet.IsCanonical)
            {
                throw new TrustCommandException(StatusCodes.Status409Conflict, "Month close signoff requires the canonical reconciliation packet.");
            }

            if (!IsPacketEligibleForMonthClose(packet.Status))
            {
                throw new TrustCommandException(StatusCodes.Status409Conflict, $"Month close cannot proceed while packet status is {packet.Status}.");
            }

            var resolvedPolicy = await _policyResolver.ResolveEffectivePolicyAsync(close.TrustAccountId, ct);
            var packetTemplate = await _policyResolver.ResolvePacketTemplateAsync(resolvedPolicy, ct);
            var signoffs = await _context.TrustReconciliationSignoffs.AsNoTracking()
                .Where(s => s.TrustReconciliationPacketId == packet.Id)
                .ToListAsync(ct);
            var steps = await _context.TrustMonthCloseSteps
                .Where(s => s.TrustMonthCloseId == close.Id)
                .ToListAsync(ct);
            var templateStatus = await BuildMonthCloseTemplateStatusAsync(close, packet, resolvedPolicy, packetTemplate, ct);
            steps = await EnsureMonthCloseStepsAsync(close, packet, close.OpenExceptionCount, signoffs, resolvedPolicy, templateStatus, ct);

            if (steps.Any(s => s.StepKey != "reviewer_signoff" && s.StepKey != "responsible_lawyer_signoff" && s.Status != "completed"))
            {
                throw new TrustCommandException(StatusCodes.Status409Conflict, "Month close cannot be signed off while prerequisite steps remain incomplete.");
            }

            if (templateStatus.MissingRequiredSections.Count > 0)
            {
                throw new TrustCommandException(StatusCodes.Status409Conflict, $"Month close packet is missing required sections: {string.Join(", ", templateStatus.MissingRequiredSections)}.");
            }

            var role = NormalizeMonthCloseRole(dto.Role);
            var currentUserId = RequireCurrentUserId();
            var acceptedAttestationKeys = (dto.Attestations ?? [])
                .Where(a => a.Accepted && !string.IsNullOrWhiteSpace(a.Key))
                .Select(a => a.Key.Trim().ToLowerInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var requiredRoleAttestations = templateStatus.RequiredAttestations
                .Where(a => string.Equals(NormalizeMonthCloseRole(a.Role), role, StringComparison.OrdinalIgnoreCase) && a.Required)
                .ToList();
            var missingAttestations = requiredRoleAttestations
                .Where(a => !acceptedAttestationKeys.Contains(a.Key))
                .Select(a => a.Label)
                .ToList();

            if (missingAttestations.Count > 0)
            {
                throw new TrustCommandException(StatusCodes.Status409Conflict, $"Required attestations are incomplete: {string.Join("; ", missingAttestations)}.");
            }

            if (role == "reviewer")
            {
                if (close.ReviewerSignedAt.HasValue)
                {
                    throw new TrustCommandException(StatusCodes.Status409Conflict, "Reviewer signoff is already complete for this month close.");
                }

                if (string.Equals(currentUserId, close.PreparedBy, StringComparison.Ordinal) ||
                    (!string.IsNullOrWhiteSpace(packet.PreparedBy) && string.Equals(currentUserId, packet.PreparedBy, StringComparison.Ordinal)))
                {
                    throw new TrustCommandException(StatusCodes.Status403Forbidden, "Maker-checker policy forbids the preparer from signing as reviewer.");
                }

                await PersistMonthCloseAttestationsAsync(close.Id, role, currentUserId, dto.Attestations, requiredRoleAttestations, ct);
                close.ReviewerSignedBy = currentUserId;
                close.ReviewerSignedAt = DateTime.UtcNow;
                CompleteMonthCloseStep(steps, "reviewer_signoff", currentUserId, dto.Notes);
                packet.Status = "reviewer_signed";
            }
            else if (role == "responsible_lawyer")
            {
                if (close.ResponsibleLawyerSignedAt.HasValue)
                {
                    throw new TrustCommandException(StatusCodes.Status409Conflict, "Responsible lawyer signoff is already complete for this month close.");
                }

                if (!resolvedPolicy.IsResponsibleLawyer(currentUserId))
                {
                    throw new TrustCommandException(StatusCodes.Status403Forbidden, "Only the configured responsible lawyer can complete this signoff.");
                }

                if (!close.ReviewerSignedAt.HasValue)
                {
                    throw new TrustCommandException(StatusCodes.Status409Conflict, "Reviewer signoff must complete before responsible lawyer signoff.");
                }

                if (string.Equals(currentUserId, close.PreparedBy, StringComparison.Ordinal) ||
                    (!string.IsNullOrWhiteSpace(packet.PreparedBy) && string.Equals(currentUserId, packet.PreparedBy, StringComparison.Ordinal)) ||
                    string.Equals(currentUserId, close.ReviewerSignedBy, StringComparison.Ordinal))
                {
                    throw new TrustCommandException(StatusCodes.Status403Forbidden, "Final lawyer signoff requires a different actor than the preparer and reviewer.");
                }

                await PersistMonthCloseAttestationsAsync(close.Id, role, currentUserId, dto.Attestations, requiredRoleAttestations, ct);
                close.ResponsibleLawyerSignedBy = currentUserId;
                close.ResponsibleLawyerSignedAt = DateTime.UtcNow;
                CompleteMonthCloseStep(steps, "responsible_lawyer_signoff", currentUserId, dto.Notes);
                packet.Status = "closed";
            }
            else
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Month-close signoff role must be reviewer or responsible_lawyer.");
            }

            close.UpdatedAt = DateTime.UtcNow;
            templateStatus = await BuildMonthCloseTemplateStatusAsync(close, packet, resolvedPolicy, packetTemplate, ct);
            close.SummaryJson = BuildMonthCloseSummaryJson(close, packet, close.OpenExceptionCount, signoffs, resolvedPolicy, templateStatus);
            close.Status = DetermineMonthCloseStatus(close, steps);
            packet.UpdatedAt = close.UpdatedAt;
            await SaveChangesWithConcurrencyHandlingAsync(ct);
            await LogAsync("trust.month_close.signoff", "TrustMonthClose", close.Id, $"Role={role}, Status={close.Status}");
            return BuildMonthCloseDto(close, steps);
        }

        public async Task<TrustMonthCloseDto> ReopenMonthCloseAsync(string closeId, TrustMonthCloseReopenDto dto, CancellationToken ct = default)
        {
            _authorization.EnsureAllowed(TrustActionKeys.PrepareReconciliationPacket, GetCurrentUser());

            if (string.IsNullOrWhiteSpace(dto.Reason))
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Reopen reason is required.");
            }

            var close = await _context.TrustMonthCloses.FirstOrDefaultAsync(c => c.Id == closeId, ct);
            if (close == null)
            {
                throw new TrustCommandException(StatusCodes.Status404NotFound, "Month close not found.");
            }

            if (!close.IsCanonical)
            {
                throw new TrustCommandException(StatusCodes.Status409Conflict, "Only the canonical month close can be reopened.");
            }

            var now = DateTime.UtcNow;
            var actorUserId = RequireCurrentUserId();
            var packetId = string.IsNullOrWhiteSpace(dto.ReconciliationPacketId) ? close.ReconciliationPacketId : dto.ReconciliationPacketId!.Trim();
            if (string.IsNullOrWhiteSpace(packetId))
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Month close is missing its reconciliation packet.");
            }

            var packet = await _context.TrustReconciliationPackets.FirstOrDefaultAsync(p => p.Id == packetId, ct);
            if (packet == null)
            {
                throw new TrustCommandException(StatusCodes.Status404NotFound, "Reconciliation packet not found for reopened month close.");
            }

            if (!string.Equals(packet.TrustAccountId, close.TrustAccountId, StringComparison.Ordinal) ||
                packet.PeriodEnd != close.PeriodEnd ||
                packet.PeriodStart != close.PeriodStart)
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Reopened month close must reference a packet for the same trust account and period.");
            }

            var nextVersionNumber = await _context.TrustMonthCloses.AsNoTracking()
                .Where(c => c.TrustAccountId == close.TrustAccountId && c.PeriodEnd == close.PeriodEnd)
                .Select(c => (int?)c.VersionNumber)
                .MaxAsync(ct) ?? 0;

            var reopened = new TrustMonthClose
            {
                Id = Guid.NewGuid().ToString(),
                TrustAccountId = close.TrustAccountId,
                PolicyKey = close.PolicyKey,
                PeriodStart = close.PeriodStart,
                PeriodEnd = close.PeriodEnd,
                ReconciliationPacketId = packet.Id,
                VersionNumber = nextVersionNumber + 1,
                IsCanonical = true,
                OpenExceptionCount = close.OpenExceptionCount,
                PreparedBy = actorUserId,
                PreparedAt = now,
                ReopenedFromMonthCloseId = close.Id,
                ReopenedBy = actorUserId,
                ReopenedAt = now,
                ReopenReason = dto.Reason.Trim(),
                SummaryJson = JsonSerializer.Serialize(new
                {
                    reopenedFromMonthCloseId = close.Id,
                    sourceStatus = close.Status,
                    packetId = packet.Id,
                    reason = dto.Reason.Trim(),
                    notes = dto.Notes?.Trim()
                }),
                CreatedAt = now,
                UpdatedAt = now
            };

            close.IsCanonical = false;
            close.SupersededByMonthCloseId = reopened.Id;
            close.SupersededBy = actorUserId;
            close.SupersededAt = now;
            close.SupersedeReason = dto.Reason.Trim();
            close.Status = "superseded";
            close.UpdatedAt = now;

            _context.TrustMonthCloses.Add(reopened);

            var signoffs = await _context.TrustReconciliationSignoffs.AsNoTracking()
                .Where(s => s.TrustReconciliationPacketId == packet.Id)
                .ToListAsync(ct);
            var openExceptions = await _context.TrustOutstandingItems.AsNoTracking()
                .CountAsync(i => i.TrustAccountId == reopened.TrustAccountId &&
                                 i.PeriodStart == reopened.PeriodStart &&
                                 i.PeriodEnd == reopened.PeriodEnd &&
                                 i.Status == "open", ct);
            reopened.OpenExceptionCount = openExceptions;

            var resolvedPolicy = await _policyResolver.ResolveEffectivePolicyAsync(reopened.TrustAccountId, ct);
            var packetTemplate = await _policyResolver.ResolvePacketTemplateAsync(resolvedPolicy, ct);
            var templateStatus = await BuildMonthCloseTemplateStatusAsync(reopened, packet, resolvedPolicy, packetTemplate, ct);
            reopened.SummaryJson = BuildMonthCloseSummaryJson(reopened, packet, openExceptions, signoffs, resolvedPolicy, templateStatus);
            var steps = await EnsureMonthCloseStepsAsync(reopened, packet, openExceptions, signoffs, resolvedPolicy, templateStatus, ct);
            reopened.Status = DetermineMonthCloseStatus(reopened, steps);

            await SaveChangesWithConcurrencyHandlingAsync(ct);
            await LogAsync("trust.month_close.reopen", "TrustMonthClose", reopened.Id, $"ReopenedFrom={close.Id}, Packet={packet.Id}, Version={reopened.VersionNumber}");
            return BuildMonthCloseDto(reopened, steps);
        }

        public async Task<TrustTransaction> CreateDepositAsync(DepositRequest request, string? idempotencyKey = null, CancellationToken ct = default)
        {
            var normalizedIdempotencyKey = NormalizeIdempotencyKey(idempotencyKey);
            if (string.IsNullOrWhiteSpace(normalizedIdempotencyKey))
            {
                return await CreateDepositCoreAsync(request, ct);
            }

            await using var dbTx = await _context.Database.BeginTransactionAsync(ct);
            var (record, replay) = await BeginTrustTransactionCommandAsync(
                "trust_deposit_create",
                normalizedIdempotencyKey,
                BuildDepositFingerprint(request),
                ct);
            if (replay != null)
            {
                return replay;
            }

            var tx = await CreateDepositCoreAsync(request, ct);
            CompleteTrustTransactionCommand(record, tx);
            await SaveChangesWithConcurrencyHandlingAsync(ct);
            await dbTx.CommitAsync(ct);
            return tx;
        }

        public async Task<TrustTransaction> CreateWithdrawalAsync(WithdrawalRequest request, string? idempotencyKey = null, CancellationToken ct = default)
        {
            var normalizedIdempotencyKey = NormalizeIdempotencyKey(idempotencyKey);
            if (string.IsNullOrWhiteSpace(normalizedIdempotencyKey))
            {
                return await CreateWithdrawalCoreAsync(request, ct);
            }

            await using var dbTx = await _context.Database.BeginTransactionAsync(ct);
            var (record, replay) = await BeginTrustTransactionCommandAsync(
                "trust_withdrawal_create",
                normalizedIdempotencyKey,
                BuildWithdrawalFingerprint(request),
                ct);
            if (replay != null)
            {
                return replay;
            }

            var tx = await CreateWithdrawalCoreAsync(request, ct);
            CompleteTrustTransactionCommand(record, tx);
            await SaveChangesWithConcurrencyHandlingAsync(ct);
            await dbTx.CommitAsync(ct);
            return tx;
        }

        public async Task<TrustTransaction> ApproveTransactionAsync(string id, TrustApproveStepDto? step = null, string? idempotencyKey = null, CancellationToken ct = default)
        {
            var normalizedIdempotencyKey = NormalizeIdempotencyKey(idempotencyKey);
            await using var dbTx = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

            TrustCommandDeduplication? record = null;
            if (!string.IsNullOrWhiteSpace(normalizedIdempotencyKey))
            {
                var start = await BeginTrustTransactionCommandAsync(
                    "trust_transaction_approve",
                    normalizedIdempotencyKey,
                    BuildTargetOnlyFingerprint(id),
                    ct);
                record = start.Record;
                if (start.Replay != null)
                {
                    return start.Replay;
                }
            }

            var tx = await ApproveTransactionCoreAsync(id, step, ct);
            CompleteTrustTransactionCommand(record, tx);
            await SaveChangesWithConcurrencyHandlingAsync(ct);
            await dbTx.CommitAsync(ct);
            return tx;
        }

        public async Task<TrustTransaction> RejectTransactionAsync(string id, TrustRejectDto? dto, string? idempotencyKey = null, CancellationToken ct = default)
        {
            var normalizedIdempotencyKey = NormalizeIdempotencyKey(idempotencyKey);
            if (string.IsNullOrWhiteSpace(normalizedIdempotencyKey))
            {
                return await RejectTransactionCoreAsync(id, dto, ct);
            }

            await using var dbTx = await _context.Database.BeginTransactionAsync(ct);
            var (record, replay) = await BeginTrustTransactionCommandAsync(
                "trust_transaction_reject",
                normalizedIdempotencyKey,
                BuildRejectFingerprint(id, dto),
                ct);
            if (replay != null)
            {
                return replay;
            }

            var tx = await RejectTransactionCoreAsync(id, dto, ct);
            CompleteTrustTransactionCommand(record, tx);
            await SaveChangesWithConcurrencyHandlingAsync(ct);
            await dbTx.CommitAsync(ct);
            return tx;
        }

        public async Task<TrustTransaction> VoidTransactionAsync(string id, TrustVoidDto? dto, string? idempotencyKey = null, CancellationToken ct = default)
        {
            var normalizedIdempotencyKey = NormalizeIdempotencyKey(idempotencyKey);
            await using var dbTx = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

            TrustCommandDeduplication? record = null;
            if (!string.IsNullOrWhiteSpace(normalizedIdempotencyKey))
            {
                var start = await BeginTrustTransactionCommandAsync(
                    "trust_transaction_void",
                    normalizedIdempotencyKey,
                    BuildVoidFingerprint(id, dto),
                    ct);
                record = start.Record;
                if (start.Replay != null)
                {
                    return start.Replay;
                }
            }

            var tx = await VoidTransactionCoreAsync(id, dto, ct);
            CompleteTrustTransactionCommand(record, tx);
            await SaveChangesWithConcurrencyHandlingAsync(ct);
            await dbTx.CommitAsync(ct);
            return tx;
        }

        public async Task<TrustTransaction> OverrideTransactionAsync(string id, TrustOverrideDto dto, string? idempotencyKey = null, CancellationToken ct = default)
        {
            var normalizedIdempotencyKey = NormalizeIdempotencyKey(idempotencyKey);
            await using var dbTx = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

            TrustCommandDeduplication? record = null;
            if (!string.IsNullOrWhiteSpace(normalizedIdempotencyKey))
            {
                var start = await BeginTrustTransactionCommandAsync(
                    "trust_transaction_override",
                    normalizedIdempotencyKey,
                    BuildTargetOnlyFingerprint(id),
                    ct);
                record = start.Record;
                if (start.Replay != null)
                {
                    return start.Replay;
                }
            }

            var tx = await OverrideTransactionCoreAsync(id, dto, ct);
            CompleteTrustTransactionCommand(record, tx);
            await SaveChangesWithConcurrencyHandlingAsync(ct);
            await dbTx.CommitAsync(ct);
            return tx;
        }

        public async Task<TrustTransaction> ClearDepositAsync(string id, TrustClearDepositDto? dto, string? idempotencyKey = null, CancellationToken ct = default)
        {
            var normalizedIdempotencyKey = NormalizeIdempotencyKey(idempotencyKey);
            await using var dbTx = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

            TrustCommandDeduplication? record = null;
            if (!string.IsNullOrWhiteSpace(normalizedIdempotencyKey))
            {
                var start = await BeginTrustTransactionCommandAsync(
                    "trust_deposit_clear",
                    normalizedIdempotencyKey,
                    BuildClearFingerprint(id, dto),
                    ct);
                record = start.Record;
                if (start.Replay != null)
                {
                    return start.Replay;
                }
            }

            var tx = await ClearDepositCoreAsync(id, dto, ct);
            CompleteTrustTransactionCommand(record, tx);
            await SaveChangesWithConcurrencyHandlingAsync(ct);
            await dbTx.CommitAsync(ct);
            return tx;
        }

        public async Task<TrustTransaction> ReturnDepositAsync(string id, TrustReturnDepositDto? dto, string? idempotencyKey = null, CancellationToken ct = default)
        {
            var normalizedIdempotencyKey = NormalizeIdempotencyKey(idempotencyKey);
            await using var dbTx = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

            TrustCommandDeduplication? record = null;
            if (!string.IsNullOrWhiteSpace(normalizedIdempotencyKey))
            {
                var start = await BeginTrustTransactionCommandAsync(
                    "trust_deposit_return",
                    normalizedIdempotencyKey,
                    BuildReturnFingerprint(id, dto),
                    ct);
                record = start.Record;
                if (start.Replay != null)
                {
                    return start.Replay;
                }
            }

            var tx = await ReturnDepositCoreAsync(id, dto, ct);
            CompleteTrustTransactionCommand(record, tx);
            await SaveChangesWithConcurrencyHandlingAsync(ct);
            await dbTx.CommitAsync(ct);
            return tx;
        }

        public async Task<TrustStatementImport> ImportStatementAsync(TrustStatementImportRequest request, CancellationToken ct = default)
        {
            _authorization.EnsureAllowed(TrustActionKeys.ImportStatement, GetCurrentUser());

            if (string.IsNullOrWhiteSpace(request.TrustAccountId))
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Trust account is required.");
            }

            if (request.PeriodEnd.Date < request.PeriodStart.Date)
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Statement period end must be on or after period start.");
            }

            var account = await _context.TrustBankAccounts
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == request.TrustAccountId, ct);
            if (account == null)
            {
                throw new TrustCommandException(StatusCodes.Status404NotFound, "Trust account not found.");
            }

            var currentUserId = RequireCurrentUserId();
            var periodStart = NormalizeUtcDate(request.PeriodStart);
            var periodEnd = NormalizeUtcDate(request.PeriodEnd);
            var importedAt = DateTime.UtcNow;
            var normalizedSourceFileHash = NormalizeHashToken(request.SourceFileHash);
            var importFingerprint = BuildStatementImportFingerprint(request, periodStart, periodEnd, normalizedSourceFileHash);
            var duplicateCandidate = await FindDuplicateStatementImportAsync(
                request.TrustAccountId,
                periodStart,
                periodEnd,
                importFingerprint,
                normalizedSourceFileHash,
                ct);
            var duplicateOfStatementImportId = duplicateCandidate == null
                ? null
                : string.IsNullOrWhiteSpace(duplicateCandidate.DuplicateOfStatementImportId)
                    ? duplicateCandidate.Id
                    : duplicateCandidate.DuplicateOfStatementImportId;
            var isDuplicateImport = duplicateOfStatementImportId != null && !request.AllowDuplicateImport;

            var import = new TrustStatementImport
            {
                Id = Guid.NewGuid().ToString(),
                TrustAccountId = request.TrustAccountId,
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                StatementEndingBalance = NormalizeMoney(request.StatementEndingBalance),
                Status = "imported",
                Source = string.IsNullOrWhiteSpace(request.Source) ? "manual" : request.Source.Trim(),
                SourceFileName = request.SourceFileName?.Trim(),
                SourceFileHash = normalizedSourceFileHash,
                SourceEvidenceKey = request.SourceEvidenceKey?.Trim(),
                SourceFileSizeBytes = request.SourceFileSizeBytes,
                ImportFingerprint = importFingerprint,
                DuplicateOfStatementImportId = isDuplicateImport ? duplicateOfStatementImportId : null,
                SupersededByStatementImportId = null,
                SupersededBy = null,
                SupersededAt = null,
                Currency = "USD",
                ImportedBy = currentUserId,
                Notes = request.Notes?.Trim(),
                MetadataJson = SerializeStatementImportMetadata(request, importFingerprint, duplicateOfStatementImportId, duplicateCandidate != null && request.AllowDuplicateImport),
                ImportedAt = importedAt,
                LineCount = request.Lines?.Count ?? 0,
                CreatedAt = importedAt,
                UpdatedAt = importedAt
            };

            _context.TrustStatementImports.Add(import);

            if (isDuplicateImport)
            {
                import.Status = "duplicate";
                await SaveChangesWithConcurrencyHandlingAsync(ct);
                await LogAsync("trust.statement.import_duplicate", "TrustStatementImport", import.Id, $"Account={import.TrustAccountId}, PeriodEnd={import.PeriodEnd:yyyy-MM-dd}, DuplicateOf={duplicateOfStatementImportId}");
                return import;
            }

            var lineDtos = request.Lines ?? [];
            if (lineDtos.Count > 0)
            {
                var candidateTransactions = await LoadStatementMatchCandidatesAsync(request.TrustAccountId, ToInclusivePeriodCutoff(periodEnd), ct);
                var statementLines = BuildStatementLinesForImport(import, lineDtos, candidateTransactions, importedAt);

                _context.TrustStatementLines.AddRange(statementLines);
                import.Status = DetermineStatementImportStatus(statementLines);
            }

            await SaveChangesWithConcurrencyHandlingAsync(ct);
            await SupersedePriorStatementImportsAsync(import, currentUserId, importedAt, ct);
            await LogAsync("trust.statement.import", "TrustStatementImport", import.Id, $"Account={import.TrustAccountId}, PeriodEnd={import.PeriodEnd:yyyy-MM-dd}, Lines={import.LineCount}");
            return import;
        }

        public async Task<IReadOnlyList<TrustStatementLine>> GetStatementLinesAsync(string statementImportId, CancellationToken ct = default)
        {
            _authorization.EnsureAllowed(TrustActionKeys.ImportStatement, GetCurrentUser());

            if (string.IsNullOrWhiteSpace(statementImportId))
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Statement import id is required.");
            }

            var exists = await _context.TrustStatementImports.AsNoTracking().AnyAsync(i => i.Id == statementImportId, ct);
            if (!exists)
            {
                throw new TrustCommandException(StatusCodes.Status404NotFound, "Statement import not found.");
            }

            return await _context.TrustStatementLines.AsNoTracking()
                .Where(l => l.TrustStatementImportId == statementImportId)
                .OrderBy(l => l.PostedAt)
                .ThenBy(l => l.Amount)
                .ToListAsync(ct);
        }

        public async Task<TrustStatementMatchingRunResultDto> RunStatementMatchingAsync(string statementImportId, CancellationToken ct = default)
        {
            _authorization.EnsureAllowed(TrustActionKeys.ImportStatement, GetCurrentUser());

            if (string.IsNullOrWhiteSpace(statementImportId))
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Statement import id is required.");
            }

            var import = await _context.TrustStatementImports.FirstOrDefaultAsync(i => i.Id == statementImportId, ct);
            if (import == null)
            {
                throw new TrustCommandException(StatusCodes.Status404NotFound, "Statement import not found.");
            }

            if (string.Equals(import.Status, "duplicate", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(import.Status, "superseded", StringComparison.OrdinalIgnoreCase))
            {
                throw new TrustCommandException(StatusCodes.Status409Conflict, "This statement import is frozen and cannot be auto-matched.");
            }

            var lines = await _context.TrustStatementLines
                .Where(l => l.TrustStatementImportId == statementImportId)
                .OrderBy(l => l.PostedAt)
                .ThenBy(l => l.Amount)
                .ToListAsync(ct);

            var candidates = await LoadStatementMatchCandidatesAsync(import.TrustAccountId, ToInclusivePeriodCutoff(import.PeriodEnd), ct);
            var candidateMap = BuildStatementCandidateMap(candidates);
            var reservedTransactionIds = lines
                .Where(l => string.Equals(l.MatchMethod, "manual", StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(l.MatchStatus, "matched", StringComparison.OrdinalIgnoreCase) &&
                            !string.IsNullOrWhiteSpace(l.MatchedTrustTransactionId))
                .Select(l => l.MatchedTrustTransactionId!)
                .ToHashSet(StringComparer.Ordinal);
            var now = DateTime.UtcNow;

            foreach (var line in lines)
            {
                if (string.Equals(line.MatchStatus, "ignored", StringComparison.OrdinalIgnoreCase))
                {
                    line.UpdatedAt = now;
                    continue;
                }

                if (string.Equals(line.MatchMethod, "manual", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(line.MatchStatus, "matched", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(line.MatchedTrustTransactionId))
                {
                    line.UpdatedAt = now;
                    continue;
                }

                var matched = TryResolveAutoStatementMatch(line, candidateMap, reservedTransactionIds);
                if (matched == null)
                {
                    line.MatchStatus = "unmatched";
                    line.MatchMethod = "none";
                    line.MatchConfidence = null;
                    line.MatchedTrustTransactionId = null;
                    line.MatchedBy = null;
                    line.MatchedAt = null;
                    line.MatchNotes = null;
                }
                else
                {
                    reservedTransactionIds.Add(matched.Id);
                    line.MatchStatus = "matched";
                    line.MatchMethod = "auto";
                    line.MatchConfidence = matched.Confidence;
                    line.MatchedTrustTransactionId = matched.Id;
                    line.MatchedBy = null;
                    line.MatchedAt = now;
                    line.MatchNotes = $"Auto-matched by {matched.Strategy}.";
                }

                line.UpdatedAt = now;
            }

            import.Status = DetermineStatementImportStatus(lines);
            import.UpdatedAt = now;

            await SaveChangesWithConcurrencyHandlingAsync(ct);
            await RefreshCanonicalPacketForPeriodIfPresentAsync(import.TrustAccountId, import.PeriodStart, import.PeriodEnd, import.Id, ct);

            var result = BuildStatementMatchingRunResult(import.Id, lines, now);
            await LogAsync("trust.statement.match_run", "TrustStatementImport", import.Id, $"Matched={result.MatchedLineCount}, Unmatched={result.UnmatchedLineCount}, Ignored={result.IgnoredLineCount}");
            return result;
        }

        public async Task<TrustStatementLine> ResolveStatementLineAsync(string lineId, TrustStatementLineMatchDto dto, CancellationToken ct = default)
        {
            _authorization.EnsureAllowed(TrustActionKeys.ManageOutstandingItems, GetCurrentUser());

            if (string.IsNullOrWhiteSpace(lineId))
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Statement line id is required.");
            }

            var line = await _context.TrustStatementLines.FirstOrDefaultAsync(l => l.Id == lineId, ct);
            if (line == null)
            {
                throw new TrustCommandException(StatusCodes.Status404NotFound, "Statement line not found.");
            }

            var import = await _context.TrustStatementImports.FirstOrDefaultAsync(i => i.Id == line.TrustStatementImportId, ct);
            if (import == null)
            {
                throw new TrustCommandException(StatusCodes.Status404NotFound, "Statement import not found.");
            }

            if (string.Equals(import.Status, "duplicate", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(import.Status, "superseded", StringComparison.OrdinalIgnoreCase))
            {
                throw new TrustCommandException(StatusCodes.Status409Conflict, "This statement import is frozen and cannot be updated.");
            }

            var action = (dto.Action ?? "match").Trim().ToLowerInvariant();
            var currentUserId = RequireCurrentUserId();
            var now = DateTime.UtcNow;

            switch (action)
            {
                case "match":
                {
                    if (string.IsNullOrWhiteSpace(dto.TrustTransactionId))
                    {
                        throw new TrustCommandException(StatusCodes.Status400BadRequest, "Trust transaction id is required for manual matching.");
                    }

                    var transaction = await _context.TrustTransactions
                        .AsNoTracking()
                        .FirstOrDefaultAsync(t => t.Id == dto.TrustTransactionId, ct);
                    if (transaction == null || !string.Equals(transaction.TrustAccountId, line.TrustAccountId, StringComparison.Ordinal))
                    {
                        throw new TrustCommandException(StatusCodes.Status400BadRequest, "Matching transaction was not found for this trust account.");
                    }

                    var amountMatches = NormalizeMoney(Math.Abs(transaction.Amount)) == NormalizeMoney(Math.Abs(line.Amount));
                    line.MatchStatus = "matched";
                    line.MatchMethod = amountMatches ? "manual" : "manual_override";
                    line.MatchConfidence = amountMatches ? 1.00m : 0.60m;
                    line.MatchedTrustTransactionId = transaction.Id;
                    line.MatchedBy = currentUserId;
                    line.MatchedAt = now;
                    line.MatchNotes = dto.Notes?.Trim();
                    break;
                }
                case "ignore":
                    line.MatchStatus = "ignored";
                    line.MatchMethod = "manual";
                    line.MatchConfidence = 0m;
                    line.MatchedTrustTransactionId = null;
                    line.MatchedBy = currentUserId;
                    line.MatchedAt = now;
                    line.MatchNotes = dto.Notes?.Trim();
                    break;
                case "reject":
                    line.MatchStatus = "rejected";
                    line.MatchMethod = "manual";
                    line.MatchConfidence = 0m;
                    line.MatchedTrustTransactionId = null;
                    line.MatchedBy = currentUserId;
                    line.MatchedAt = now;
                    line.MatchNotes = dto.Notes?.Trim();
                    break;
                case "unmatch":
                    line.MatchStatus = "unmatched";
                    line.MatchMethod = "none";
                    line.MatchConfidence = null;
                    line.MatchedTrustTransactionId = null;
                    line.MatchedBy = currentUserId;
                    line.MatchedAt = now;
                    line.MatchNotes = dto.Notes?.Trim();
                    break;
                default:
                    throw new TrustCommandException(StatusCodes.Status400BadRequest, "Statement line action must be match, ignore, reject, or unmatch.");
            }

            line.UpdatedAt = now;
            var importLines = await _context.TrustStatementLines
                .Where(l => l.TrustStatementImportId == import.Id)
                .ToListAsync(ct);
            import.Status = DetermineStatementImportStatus(importLines);
            import.UpdatedAt = now;

            await SaveChangesWithConcurrencyHandlingAsync(ct);
            await RefreshCanonicalPacketForPeriodIfPresentAsync(import.TrustAccountId, import.PeriodStart, import.PeriodEnd, import.Id, ct);
            await LogAsync("trust.statement.line.resolve", "TrustStatementLine", line.Id, $"Action={action}, Status={line.MatchStatus}");
            return line;
        }

        public async Task<TrustOutstandingItem> CreateOutstandingItemAsync(TrustOutstandingItemCreateDto request, CancellationToken ct = default)
        {
            _authorization.EnsureAllowed(TrustActionKeys.ManageOutstandingItems, GetCurrentUser());

            if (string.IsNullOrWhiteSpace(request.TrustAccountId))
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Trust account is required.");
            }

            if (request.Amount <= 0m)
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Outstanding item amount must be greater than zero.");
            }

            if (request.PeriodEnd.Date < request.PeriodStart.Date)
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Outstanding item period is invalid.");
            }

            var accountExists = await _context.TrustBankAccounts.AnyAsync(a => a.Id == request.TrustAccountId, ct);
            if (!accountExists)
            {
                throw new TrustCommandException(StatusCodes.Status404NotFound, "Trust account not found.");
            }

            var now = DateTime.UtcNow;
            var item = new TrustOutstandingItem
            {
                Id = Guid.NewGuid().ToString(),
                TrustAccountId = request.TrustAccountId,
                TrustTransactionId = NullIfWhiteSpace(request.TrustTransactionId),
                ClientTrustLedgerId = NullIfWhiteSpace(request.ClientTrustLedgerId),
                PeriodStart = NormalizeUtcDate(request.PeriodStart),
                PeriodEnd = NormalizeUtcDate(request.PeriodEnd),
                OccurredAt = request.OccurredAt?.ToUniversalTime() ?? now,
                ItemType = NormalizeOutstandingItemType(request.ItemType),
                ImpactDirection = NormalizeOutstandingImpactDirection(request.ImpactDirection),
                Status = "open",
                Source = "manual",
                Amount = NormalizeMoney(request.Amount),
                Reference = request.Reference?.Trim(),
                Description = request.Description?.Trim(),
                ReasonCode = NormalizeOutstandingReasonCode(request.ReasonCode),
                AttachmentEvidenceKey = NullIfWhiteSpace(request.AttachmentEvidenceKey),
                CreatedBy = RequireCurrentUserId(),
                MetadataJson = JsonSerializer.Serialize(new
                {
                    createdVia = "manual_form",
                    reasonCode = NormalizeOutstandingReasonCode(request.ReasonCode),
                    attachmentEvidenceKey = NullIfWhiteSpace(request.AttachmentEvidenceKey)
                }),
                CreatedAt = now,
                UpdatedAt = now
            };

            _context.TrustOutstandingItems.Add(item);
            await SaveChangesWithConcurrencyHandlingAsync(ct);
            await LogAsync("trust.outstanding_item.create", "TrustOutstandingItem", item.Id, $"Account={item.TrustAccountId}, Type={item.ItemType}, Amount={item.Amount}");
            return item;
        }

        public async Task<TrustReconciliationPacket> GenerateReconciliationPacketAsync(TrustReconciliationPacketCreateDto request, CancellationToken ct = default)
        {
            _authorization.EnsureAllowed(TrustActionKeys.PrepareReconciliationPacket, GetCurrentUser());
            await EnsureBillingPeriodUnlockedAsync("trust_reconciliation_packet_prepare", "Billing period is locked. Cannot prepare reconciliation packet.", ct);

            if (string.IsNullOrWhiteSpace(request.TrustAccountId))
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Trust account is required.");
            }

            if (request.PeriodEnd.Date < request.PeriodStart.Date)
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Reconciliation period is invalid.");
            }

            var account = await _context.TrustBankAccounts
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == request.TrustAccountId, ct);
            if (account == null)
            {
                throw new TrustCommandException(StatusCodes.Status404NotFound, "Trust account not found.");
            }

            var preparedBy = RequireCurrentUserId();
            var preparedAt = DateTime.UtcNow;
            var periodStart = NormalizeUtcDate(request.PeriodStart);
            var periodEnd = NormalizeUtcDate(request.PeriodEnd);
            var cutoff = ToInclusivePeriodCutoff(periodEnd);

            var statementImport = await ResolveStatementImportAsync(request.TrustAccountId, request.StatementImportId, periodStart, periodEnd, ct);
            var statementEndingBalance = statementImport != null
                ? statementImport.StatementEndingBalance
                : request.StatementEndingBalance.HasValue
                    ? NormalizeMoney(request.StatementEndingBalance.Value)
                    : throw new TrustCommandException(StatusCodes.Status400BadRequest, "Statement ending balance or statement import is required.");

            var journalAggregate = await _context.TrustJournalEntries
                .AsNoTracking()
                .Where(j => j.TrustAccountId == request.TrustAccountId && j.EffectiveAt <= cutoff)
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    JournalBalance = g.Sum(x => x.Amount),
                    ClientLedgerBalance = g.Where(x => x.ClientTrustLedgerId != null).Sum(x => x.Amount)
                })
                .FirstOrDefaultAsync(ct);

            var journalBalance = NormalizeMoney(journalAggregate?.JournalBalance ?? 0m);
            var clientLedgerBalance = NormalizeMoney(journalAggregate?.ClientLedgerBalance ?? 0m);

            var manualItems = await _context.TrustOutstandingItems
                .Where(i =>
                    i.TrustAccountId == request.TrustAccountId &&
                    i.Source == "manual" &&
                    i.PeriodStart == periodStart &&
                    i.PeriodEnd == periodEnd &&
                    i.Status == "open")
                .ToListAsync(ct);

            var periodPackets = await _context.TrustReconciliationPackets
                .Where(p =>
                    p.TrustAccountId == request.TrustAccountId &&
                    p.PeriodStart == periodStart &&
                    p.PeriodEnd == periodEnd)
                .OrderByDescending(p => p.PreparedAt)
                .ToListAsync(ct);

            TrustReconciliationPacket? supersedeSourcePacket = null;
            if (!string.IsNullOrWhiteSpace(request.SupersedePacketId))
            {
                supersedeSourcePacket = periodPackets.FirstOrDefault(p => p.Id == request.SupersedePacketId);
                if (supersedeSourcePacket == null)
                {
                    throw new TrustCommandException(StatusCodes.Status404NotFound, "Superseded reconciliation packet was not found for the requested period.");
                }
            }

            var statementLines = statementImport == null
                ? []
                : await _context.TrustStatementLines
                    .Where(l => l.TrustStatementImportId == statementImport.Id)
                    .ToListAsync(ct);

            var matchedStatementLineCount = statementLines.Count(IsStatementLineMatchedForPacket);
            var unmatchedStatementLineCount = statementLines.Count(l => !IsStatementLineResolvedForPacket(l));

            var editablePacket = request.ForceNewVersion || supersedeSourcePacket != null
                ? null
                : periodPackets.FirstOrDefault(p => p.IsCanonical && !IsPacketTerminal(p.Status))
                    ?? periodPackets.FirstOrDefault(p => !IsPacketTerminal(p.Status));

            var autoItems = await BuildAutoOutstandingItemsAsync(
                request.TrustAccountId,
                statementImport,
                statementLines,
                periodStart,
                periodEnd,
                cutoff,
                preparedBy,
                preparedAt,
                ct);

            var syncedAutoItems = await SyncAutoOutstandingItemsAsync(
                request.TrustAccountId,
                periodStart,
                periodEnd,
                statementImport?.Id,
                editablePacket?.Id,
                autoItems,
                ct);

            var allOutstandingItems = manualItems
                .Concat(syncedAutoItems)
                .OrderBy(i => i.OccurredAt)
                .ThenBy(i => i.ItemType, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var outstandingDepositsTotal = NormalizeMoney(allOutstandingItems
                .Where(i => i.ItemType == "deposit_in_transit" && i.Status == "open")
                .Sum(i => i.Amount));
            var outstandingChecksTotal = NormalizeMoney(allOutstandingItems
                .Where(i => i.ItemType == "outstanding_check" && i.Status == "open")
                .Sum(i => i.Amount));
            var otherAdjustmentsTotal = NormalizeMoney(allOutstandingItems
                .Where(i => i.ItemType != "deposit_in_transit" && i.ItemType != "outstanding_check" && i.Status == "open")
                .Sum(ComputeBankAdjustmentDelta));

            var adjustedBankBalance = NormalizeMoney(
                statementEndingBalance +
                outstandingDepositsTotal -
                outstandingChecksTotal +
                otherAdjustmentsTotal);

            var bankVsJournalMismatch = Math.Abs(adjustedBankBalance - journalBalance) >= 0.01m;
            var journalVsLedgerMismatch = Math.Abs(journalBalance - clientLedgerBalance) >= 0.01m;
            var exceptionCount = unmatchedStatementLineCount;
            if (bankVsJournalMismatch)
            {
                exceptionCount++;
            }

            if (journalVsLedgerMismatch)
            {
                exceptionCount++;
            }

            var packet = editablePacket ?? new TrustReconciliationPacket
            {
                Id = Guid.NewGuid().ToString(),
                TrustAccountId = request.TrustAccountId,
                VersionNumber = (periodPackets.Select(p => (int?)p.VersionNumber).Max() ?? 0) + 1,
                CreatedAt = preparedAt
            };

            packet.StatementImportId = statementImport?.Id;
            packet.PeriodStart = periodStart;
            packet.PeriodEnd = periodEnd;
            packet.StatementEndingBalance = statementEndingBalance;
            packet.AdjustedBankBalance = adjustedBankBalance;
            packet.JournalBalance = journalBalance;
            packet.ClientLedgerBalance = clientLedgerBalance;
            packet.OutstandingDepositsTotal = outstandingDepositsTotal;
            packet.OutstandingChecksTotal = outstandingChecksTotal;
            packet.OtherAdjustmentsTotal = otherAdjustmentsTotal;
            packet.ExceptionCount = exceptionCount;
            packet.MatchedStatementLineCount = matchedStatementLineCount;
            packet.UnmatchedStatementLineCount = unmatchedStatementLineCount;
            packet.VersionNumber = editablePacket?.VersionNumber ?? packet.VersionNumber;
            packet.IsCanonical = true;
            packet.Status = DetermineReconciliationPacketStatus(statementImport != null, unmatchedStatementLineCount, bankVsJournalMismatch, journalVsLedgerMismatch);
            packet.PreparedBy = preparedBy;
            packet.SupersededByPacketId = null;
            packet.SupersededBy = null;
            packet.SupersededAt = null;
            packet.SupersedeReason = null;
            packet.PreparedAt = preparedAt;
            packet.Notes = request.Notes?.Trim();
            packet.UpdatedAt = preparedAt;
            packet.PayloadJson = JsonSerializer.Serialize(new
            {
                generatedAt = preparedAt,
                accountName = account.Name,
                statementImportId = statementImport?.Id,
                statementLineCount = statementLines.Count,
                matchedStatementLineCount,
                unmatchedStatementLineCount,
                outstandingItems = allOutstandingItems.Select(i => new
                {
                    i.Id,
                    i.ItemType,
                    i.ImpactDirection,
                    i.Amount,
                    i.Reference,
                    i.Description,
                    i.Status,
                    i.Source,
                    i.OccurredAt,
                    i.TrustTransactionId,
                    i.ClientTrustLedgerId
                }).ToArray(),
                balances = new
                {
                    statementEndingBalance,
                    adjustedBankBalance,
                    journalBalance,
                    clientLedgerBalance
                },
                status = packet.Status,
                versionNumber = packet.VersionNumber,
                supersedeSourcePacketId = supersedeSourcePacket?.Id
            });

            if (editablePacket == null)
            {
                _context.TrustReconciliationPackets.Add(packet);
            }

            foreach (var priorPacket in periodPackets.Where(p => !string.Equals(p.Id, packet.Id, StringComparison.Ordinal) && p.IsCanonical))
            {
                priorPacket.IsCanonical = false;
                priorPacket.UpdatedAt = preparedAt;
                priorPacket.SupersededByPacketId = packet.Id;
                priorPacket.SupersededBy = preparedBy;
                priorPacket.SupersededAt = preparedAt;
                priorPacket.SupersedeReason = request.SupersedeReason?.Trim()
                    ?? (supersedeSourcePacket != null ? $"Superseded by reconciliation packet {packet.Id}." : "Canonical reconciliation packet regenerated.");
                if (!IsPacketTerminal(priorPacket.Status))
                {
                    priorPacket.Status = "superseded";
                }
            }

            foreach (var item in allOutstandingItems)
            {
                item.TrustReconciliationPacketId = packet.Id;
                item.UpdatedAt = preparedAt;
            }

            var existingRecord = await _context.ReconciliationRecords
                .FirstOrDefaultAsync(r => r.TrustAccountId == request.TrustAccountId && r.PeriodEnd == periodEnd, ct);
            if (existingRecord == null)
            {
                existingRecord = new ReconciliationRecord
                {
                    Id = Guid.NewGuid().ToString(),
                    TrustAccountId = request.TrustAccountId,
                    PeriodEnd = periodEnd,
                    CreatedAt = preparedAt
                };
                _context.ReconciliationRecords.Add(existingRecord);
            }

            existingRecord.BankStatementBalance = statementEndingBalance;
            existingRecord.TrustLedgerBalance = journalBalance;
            existingRecord.ClientLedgerSumBalance = clientLedgerBalance;
            existingRecord.IsReconciled = !bankVsJournalMismatch && !journalVsLedgerMismatch && unmatchedStatementLineCount == 0;
            existingRecord.DiscrepancyAmount = NormalizeMoney(Math.Abs(adjustedBankBalance - journalBalance));
            existingRecord.Notes = request.Notes?.Trim();

            await SaveChangesWithConcurrencyHandlingAsync(ct);
            await LogAsync("trust.reconciliation_packet.prepare", "TrustReconciliationPacket", packet.Id, $"Account={packet.TrustAccountId}, PeriodEnd={packet.PeriodEnd:yyyy-MM-dd}, Exceptions={packet.ExceptionCount}");
            return packet;
        }

        public async Task<TrustReconciliationPacket> SignoffReconciliationPacketAsync(string id, TrustReconciliationPacketSignoffDto? dto, CancellationToken ct = default)
        {
            _authorization.EnsureAllowed(TrustActionKeys.SignoffReconciliationPacket, GetCurrentUser());

            var packet = await _context.TrustReconciliationPackets.FirstOrDefaultAsync(p => p.Id == id, ct);
            if (packet == null)
            {
                throw new TrustCommandException(StatusCodes.Status404NotFound, "Reconciliation packet not found.");
            }

            var currentUserId = RequireCurrentUserId();
            if (!string.IsNullOrWhiteSpace(packet.PreparedBy) &&
                string.Equals(packet.PreparedBy, currentUserId, StringComparison.Ordinal))
            {
                throw new TrustCommandException(StatusCodes.Status403Forbidden, "Maker-checker policy forbids signing off your own reconciliation packet.");
            }

            if (string.Equals(packet.Status, "signed_off", StringComparison.OrdinalIgnoreCase))
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Reconciliation packet is already signed off.");
            }

            if (!packet.IsCanonical)
            {
                throw new TrustCommandException(StatusCodes.Status409Conflict, "Only the canonical reconciliation packet can be signed off.");
            }

            if (!string.Equals(packet.Status, "ready_for_signoff", StringComparison.OrdinalIgnoreCase))
            {
                throw new TrustCommandException(StatusCodes.Status409Conflict, "Reconciliation packet must be ready for signoff before it can be signed.");
            }

            var signoff = new TrustReconciliationSignoff
            {
                Id = Guid.NewGuid().ToString(),
                TrustReconciliationPacketId = packet.Id,
                SignedBy = currentUserId,
                SignerRole = TrustActionAuthorizationService.GetRole(GetCurrentUser()),
                Status = "signed_off",
                Notes = dto?.Notes?.Trim(),
                SignedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };

            packet.Status = "signed_off";
            packet.UpdatedAt = signoff.SignedAt;
            _context.TrustReconciliationSignoffs.Add(signoff);

            await SaveChangesWithConcurrencyHandlingAsync(ct);
            await LogAsync("trust.reconciliation_packet.signoff", "TrustReconciliationPacket", packet.Id, $"SignedBy={currentUserId}");
            return packet;
        }

        public async Task<TrustReconciliationPacket> SupersedeReconciliationPacketAsync(string packetId, TrustReconciliationPacketSupersedeDto dto, CancellationToken ct = default)
        {
            _authorization.EnsureAllowed(TrustActionKeys.PrepareReconciliationPacket, GetCurrentUser());

            if (string.IsNullOrWhiteSpace(dto.Reason))
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Supersede reason is required.");
            }

            var packet = await _context.TrustReconciliationPackets.FirstOrDefaultAsync(p => p.Id == packetId, ct);
            if (packet == null)
            {
                throw new TrustCommandException(StatusCodes.Status404NotFound, "Reconciliation packet not found.");
            }

            if (!packet.IsCanonical)
            {
                throw new TrustCommandException(StatusCodes.Status409Conflict, "Only the canonical reconciliation packet can be superseded.");
            }

            var closedMonthCloseExists = await _context.TrustMonthCloses.AsNoTracking()
                .AnyAsync(c => c.ReconciliationPacketId == packet.Id && c.IsCanonical && c.Status == "closed", ct);
            if (closedMonthCloseExists)
            {
                throw new TrustCommandException(StatusCodes.Status409Conflict, "Closed month-close packets must be reopened before the reconciliation packet can be superseded.");
            }

            var replacement = await GenerateReconciliationPacketAsync(new TrustReconciliationPacketCreateDto
            {
                TrustAccountId = packet.TrustAccountId,
                PeriodStart = packet.PeriodStart,
                PeriodEnd = packet.PeriodEnd,
                StatementImportId = string.IsNullOrWhiteSpace(dto.StatementImportId) ? packet.StatementImportId : dto.StatementImportId.Trim(),
                StatementEndingBalance = dto.StatementEndingBalance ?? packet.StatementEndingBalance,
                Notes = string.IsNullOrWhiteSpace(dto.Notes) ? packet.Notes : dto.Notes.Trim(),
                ForceNewVersion = true,
                SupersedePacketId = packet.Id,
                SupersedeReason = dto.Reason.Trim()
            }, ct);

            await LogAsync("trust.reconciliation_packet.supersede", "TrustReconciliationPacket", replacement.Id, $"SupersededFrom={packet.Id}, Version={replacement.VersionNumber}");
            return replacement;
        }

        public async Task<ReconciliationRecord> ReconcileAsync(ReconcileRequest request, CancellationToken ct = default)
        {
            if (!DateTime.TryParse(request.PeriodEnd, out var periodEnd))
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Period end is invalid.");
            }

            DateTime periodStart;
            if (!string.IsNullOrWhiteSpace(request.PeriodStart) && DateTime.TryParse(request.PeriodStart, out var parsedPeriodStart))
            {
                periodStart = NormalizeUtcDate(parsedPeriodStart);
            }
            else
            {
                periodStart = NormalizeUtcDate(new DateTime(periodEnd.Year, periodEnd.Month, 1, 0, 0, 0, DateTimeKind.Utc));
            }

            var packet = await GenerateReconciliationPacketAsync(new TrustReconciliationPacketCreateDto
            {
                TrustAccountId = request.TrustAccountId,
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                StatementEndingBalance = request.BankStatementBalance,
                Notes = request.Notes
            }, ct);

            var record = await _context.ReconciliationRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.TrustAccountId == request.TrustAccountId && r.PeriodEnd == packet.PeriodEnd, ct);

            if (record == null)
            {
                throw new TrustCommandException(StatusCodes.Status500InternalServerError, "Canonical reconciliation packet did not emit a compatibility reconciliation record.");
            }

            await LogAsync("trust.reconcile.compat", "ReconciliationRecord", record.Id, $"Packet={packet.Id}, Status={packet.Status}");
            return record;
        }

        public async Task<TrustTransaction> PostEarnedFeeTransferAsync(TrustEarnedFeeTransferCommand command, CancellationToken ct = default)
        {
            await EnsureBillingPeriodUnlockedAsync("trust_earned_fee_transfer", "Billing period is locked. Cannot post earned-fee transfer.", ct);
            _authorization.EnsureAllowed(TrustActionKeys.EarnedFeeTransfer, GetCurrentUser());

            if (string.IsNullOrWhiteSpace(command.TrustAccountId) || string.IsNullOrWhiteSpace(command.LedgerId))
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Trust account and ledger are required for earned-fee transfer.");
            }

            if (command.Amount <= 0m)
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Earned-fee transfer amount must be positive.");
            }

            var currentUserId = RequireCurrentUserId();
            var effectiveAt = command.EffectiveAt?.ToUniversalTime() ?? DateTime.UtcNow;
            var account = await _context.TrustBankAccounts.FirstOrDefaultAsync(a => a.Id == command.TrustAccountId, ct);
            if (account == null)
            {
                throw new TrustCommandException(StatusCodes.Status404NotFound, "Trust account not found.");
            }

            if (account.Status != TrustAccountStatus.ACTIVE)
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Trust account is not active.");
            }

            var ledger = await _context.ClientTrustLedgers.FirstOrDefaultAsync(l => l.Id == command.LedgerId, ct);
            if (ledger == null)
            {
                throw new TrustCommandException(StatusCodes.Status404NotFound, "Client trust ledger not found.");
            }

            if (!string.Equals(ledger.TrustAccountId, account.Id, StringComparison.Ordinal))
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Client trust ledger does not belong to the selected trust account.");
            }

            if (ledger.Status != LedgerStatus.ACTIVE)
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Client trust ledger is not active.");
            }

            var amount = NormalizeMoney(command.Amount);
            if (!string.IsNullOrWhiteSpace(command.MatterId) &&
                !string.IsNullOrWhiteSpace(ledger.MatterId) &&
                !string.Equals(command.MatterId, ledger.MatterId, StringComparison.Ordinal))
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Selected matter does not match the client trust ledger.");
            }

            if (ledger.AvailableToDisburse < amount)
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Earned-fee transfer exceeds the client's cleared funds available to disburse.");
            }

            if (account.AvailableDisbursementCapacity < amount)
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Earned-fee transfer exceeds the trust account's cleared funds capacity.");
            }

            var resolvedPolicy = await _policyResolver.ResolveEffectivePolicyAsync(account.Id, ct);
            var approvalPlan = BuildApprovalPlan(
                resolvedPolicy,
                "EARNED_FEE_TRANSFER",
                "earned_fee_transfer",
                amount,
                currentUserId,
                TrustActionAuthorizationService.GetRole(GetCurrentUser()));
            await _trustRiskRadarService.EnforceNoActiveHardHoldsAsync(new TrustRiskRadarService.TrustRiskHoldGuardContext
            {
                OperationType = "earned_fee_transfer",
                BillingPaymentAllocationId = command.BillingPaymentAllocationId,
                PaymentTransactionId = command.PaymentTransactionId,
                InvoiceId = command.InvoiceId,
                MatterId = command.MatterId ?? ledger.MatterId,
                ClientId = ledger.ClientId
            }, ct);

            var createdAt = effectiveAt;

            var tx = new TrustTransaction
            {
                Id = Guid.NewGuid().ToString(),
                TrustAccountId = account.Id,
                LedgerId = ledger.Id,
                MatterId = command.MatterId ?? ledger.MatterId,
                Type = "EARNED_FEE_TRANSFER",
                DisbursementClass = "earned_fee_transfer",
                Amount = amount,
                Description = string.IsNullOrWhiteSpace(command.Description) ? "Earned fee transfer from trust to operating." : command.Description.Trim(),
                PayorPayee = string.IsNullOrWhiteSpace(command.PayorPayee) ? "Operating Account" : command.PayorPayee.Trim(),
                EntityId = account.EntityId,
                OfficeId = account.OfficeId,
                Status = approvalPlan.Requirements.Count == 0 || command.ShadowPolicyOnly ? "APPROVED" : "PENDING",
                ApprovalStatus = approvalPlan.Requirements.Count == 0 ? "not_required" : command.ShadowPolicyOnly ? "shadow_bypassed" : "pending",
                ApprovedBy = approvalPlan.Requirements.Count == 0 || command.ShadowPolicyOnly ? currentUserId : null,
                ApprovedAt = approvalPlan.Requirements.Count == 0 || command.ShadowPolicyOnly ? effectiveAt : null,
                CreatedBy = currentUserId,
                CreatedAt = createdAt,
                UpdatedAt = createdAt,
                ClearingStatus = "not_applicable",
                IsEarned = true,
                EarnedDate = effectiveAt,
                BalanceBefore = account.CurrentBalance,
                Reference = command.Reference?.Trim() ?? $"billing-allocation:{command.BillingPaymentAllocationId}",
                PolicyDecisionJson = SerializePolicyDecision(account, resolvedPolicy.Policy, approvalPlan, hasOverride: false)
            };

            _context.TrustTransactions.Add(tx);

            if (approvalPlan.Requirements.Count > 0 && !command.ShadowPolicyOnly)
            {
                await EnsureApprovalRequirementsAsync(tx, account, ledger, ct);
                await SaveChangesWithConcurrencyHandlingAsync(ct);
                await _trustRiskRadarService.RecordTrustTransactionRiskAsync(tx, "earned_fee_transfer_pending_approval", ct);
                await LogAsync("trust.earned_fee_transfer.pending", "TrustTransaction", tx.Id, $"Amount={tx.Amount}, AllocationId={command.BillingPaymentAllocationId}");
                return tx;
            }

            var journalEntries = BuildPostedEarnedFeeTransferJournalEntries(tx, ledger, currentUserId, effectiveAt, command);
            var postingBatch = CreatePostingBatch(tx, account.Id, "posting", currentUserId, effectiveAt, null, journalEntries);
            foreach (var journalEntry in journalEntries)
            {
                journalEntry.PostingBatchId = postingBatch.Id;
            }

            tx.BalanceAfter = NormalizeMoney(account.CurrentBalance + journalEntries.Sum(j => j.Amount));
            tx.PostingBatchId = postingBatch.Id;
            tx.PrimaryJournalEntryId = journalEntries.FirstOrDefault()?.Id;

            _context.TrustPostingBatches.Add(postingBatch);
            _context.TrustJournalEntries.AddRange(journalEntries);
            await ApplyJournalEntriesToProjectionsAsync(account, journalEntries, ct);

            await SaveChangesWithConcurrencyHandlingAsync(ct);
            await _trustRiskRadarService.RecordTrustTransactionRiskAsync(tx, "earned_fee_transfer_posted", ct);
            await LogAsync("trust.earned_fee_transfer.post", "TrustTransaction", tx.Id, $"Amount={tx.Amount}, AllocationId={command.BillingPaymentAllocationId}");
            return tx;
        }

        public async Task<TrustTransaction> ReverseEarnedFeeTransferAsync(string trustTransactionId, string? reason, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(trustTransactionId))
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Trust transaction id is required.");
            }

            var tx = await _context.TrustTransactions
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == trustTransactionId, ct);
            if (tx == null)
            {
                throw new TrustCommandException(StatusCodes.Status404NotFound, "Trust transaction not found.");
            }

            if (!string.Equals(tx.Type, "EARNED_FEE_TRANSFER", StringComparison.OrdinalIgnoreCase))
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Only earned-fee transfer transactions can be reversed from billing.");
            }

            return await VoidTransactionCoreAsync(trustTransactionId, new TrustVoidDto
            {
                Reason = reason
            }, ct);
        }

        public async Task<TrustProjectionHealthResponse> GetProjectionHealthAsync(string? trustAccountId = null, CancellationToken ct = default)
        {
            _authorization.EnsureAllowed(TrustActionKeys.RebuildProjections, GetCurrentUser());

            var accounts = await _context.TrustBankAccounts
                .AsNoTracking()
                .Where(a => string.IsNullOrWhiteSpace(trustAccountId) || a.Id == trustAccountId)
                .OrderBy(a => a.Name)
                .ToListAsync(ct);

            var response = new TrustProjectionHealthResponse
            {
                GeneratedAt = DateTime.UtcNow
            };

            var accountIds = accounts.Select(a => a.Id).ToList();
            if (accountIds.Count == 0)
            {
                return response;
            }

            var ledgers = await _context.ClientTrustLedgers
                .AsNoTracking()
                .Where(l => accountIds.Contains(l.TrustAccountId))
                .OrderBy(l => l.ClientId)
                .ThenBy(l => l.MatterId)
                .ToListAsync(ct);

            var accountJournal = await _context.TrustJournalEntries
                .AsNoTracking()
                .Where(j => accountIds.Contains(j.TrustAccountId))
                .GroupBy(j => j.TrustAccountId)
                .Select(g => new
                {
                    TrustAccountId = g.Key,
                    CurrentBalance = g.Sum(x => x.Amount),
                    ClearedBalance = g.Where(x => x.AvailabilityClass == "cleared").Sum(x => x.Amount),
                    UnclearedBalance = g.Where(x => x.AvailabilityClass == "uncleared").Sum(x => x.Amount)
                })
                .ToListAsync(ct);

            var ledgerJournal = await _context.TrustJournalEntries
                .AsNoTracking()
                .Where(j => j.ClientTrustLedgerId != null && accountIds.Contains(j.TrustAccountId))
                .GroupBy(j => j.ClientTrustLedgerId!)
                .Select(g => new
                {
                    LedgerId = g.Key,
                    RunningBalance = g.Sum(x => x.Amount),
                    ClearedBalance = g.Where(x => x.AvailabilityClass == "cleared").Sum(x => x.Amount),
                    UnclearedBalance = g.Where(x => x.AvailabilityClass == "uncleared").Sum(x => x.Amount)
                })
                .ToListAsync(ct);

            var accountJournalMap = accountJournal.ToDictionary(
                item => item.TrustAccountId,
                item => (
                    NormalizeMoney(item.CurrentBalance),
                    NormalizeMoney(item.ClearedBalance),
                    NormalizeMoney(item.UnclearedBalance)),
                StringComparer.Ordinal);
            var ledgerJournalMap = ledgerJournal.ToDictionary(
                item => item.LedgerId,
                item => (
                    NormalizeMoney(item.RunningBalance),
                    NormalizeMoney(item.ClearedBalance),
                    NormalizeMoney(item.UnclearedBalance)),
                StringComparer.Ordinal);

            foreach (var account in accounts)
            {
                var accountExpected = accountJournalMap.TryGetValue(account.Id, out var accountValues)
                    ? accountValues
                    : (0m, 0m, 0m);
                var expectedAvailable = NormalizeMoney(Math.Max(0m, accountExpected.Item2));

                var driftedLedgers = ledgers
                    .Where(l => string.Equals(l.TrustAccountId, account.Id, StringComparison.Ordinal))
                    .Select(ledger =>
                    {
                        var ledgerExpected = ledgerJournalMap.TryGetValue(ledger.Id, out var ledgerValues)
                            ? ledgerValues
                            : (0m, 0m, 0m);
                        var expectedLedgerAvailable = NormalizeMoney(Math.Max(0m, ledgerExpected.Item2 - ledger.HoldAmount));
                        var hasLedgerDrift =
                            HasProjectionDrift(ledger.RunningBalance, ledgerExpected.Item1) ||
                            HasProjectionDrift(ledger.ClearedBalance, ledgerExpected.Item2) ||
                            HasProjectionDrift(ledger.UnclearedBalance, ledgerExpected.Item3) ||
                            HasProjectionDrift(ledger.AvailableToDisburse, expectedLedgerAvailable);

                        return new TrustProjectionHealthLedgerDto
                        {
                            LedgerId = ledger.Id,
                            ClientId = ledger.ClientId,
                            MatterId = ledger.MatterId,
                            HoldAmount = NormalizeMoney(ledger.HoldAmount),
                            ProjectedRunningBalance = NormalizeMoney(ledger.RunningBalance),
                            ProjectedClearedBalance = NormalizeMoney(ledger.ClearedBalance),
                            ProjectedUnclearedBalance = NormalizeMoney(ledger.UnclearedBalance),
                            ProjectedAvailableToDisburse = NormalizeMoney(ledger.AvailableToDisburse),
                            JournalRunningBalance = ledgerExpected.Item1,
                            JournalClearedBalance = ledgerExpected.Item2,
                            JournalUnclearedBalance = ledgerExpected.Item3,
                            ExpectedAvailableToDisburse = expectedLedgerAvailable,
                            HasDrift = hasLedgerDrift
                        };
                    })
                    .Where(item => item.HasDrift)
                    .ToList();

                var hasAccountDrift =
                    HasProjectionDrift(account.CurrentBalance, accountExpected.Item1) ||
                    HasProjectionDrift(account.ClearedBalance, accountExpected.Item2) ||
                    HasProjectionDrift(account.UnclearedBalance, accountExpected.Item3) ||
                    HasProjectionDrift(account.AvailableDisbursementCapacity, expectedAvailable);

                response.Accounts.Add(new TrustProjectionHealthAccountDto
                {
                    TrustAccountId = account.Id,
                    TrustAccountName = account.Name,
                    ProjectedCurrentBalance = NormalizeMoney(account.CurrentBalance),
                    ProjectedClearedBalance = NormalizeMoney(account.ClearedBalance),
                    ProjectedUnclearedBalance = NormalizeMoney(account.UnclearedBalance),
                    ProjectedAvailableDisbursementCapacity = NormalizeMoney(account.AvailableDisbursementCapacity),
                    JournalCurrentBalance = accountExpected.Item1,
                    JournalClearedBalance = accountExpected.Item2,
                    JournalUnclearedBalance = accountExpected.Item3,
                    ExpectedAvailableDisbursementCapacity = expectedAvailable,
                    HasDrift = hasAccountDrift || driftedLedgers.Count > 0,
                    DriftedLedgers = driftedLedgers
                });
            }

            return response;
        }

        public async Task<TrustProjectionRebuildResult> RebuildProjectionsAsync(TrustProjectionRebuildRequest? request, CancellationToken ct = default)
        {
            _authorization.EnsureAllowed(TrustActionKeys.RebuildProjections, GetCurrentUser());

            var targetTrustAccountId = request?.TrustAccountId;
            var onlyIfDrifted = request?.OnlyIfDrifted != false;
            var accounts = await _context.TrustBankAccounts
                .Where(a => string.IsNullOrWhiteSpace(targetTrustAccountId) || a.Id == targetTrustAccountId)
                .OrderBy(a => a.Name)
                .ToListAsync(ct);
            var accountIds = accounts.Select(a => a.Id).ToList();
            if (accountIds.Count == 0)
            {
                return new TrustProjectionRebuildResult
                {
                    RebuiltAt = DateTime.UtcNow
                };
            }

            var ledgers = await _context.ClientTrustLedgers
                .Where(l => accountIds.Contains(l.TrustAccountId))
                .ToListAsync(ct);

            var accountJournal = await _context.TrustJournalEntries
                .AsNoTracking()
                .Where(j => accountIds.Contains(j.TrustAccountId))
                .GroupBy(j => j.TrustAccountId)
                .Select(g => new
                {
                    TrustAccountId = g.Key,
                    CurrentBalance = g.Sum(x => x.Amount),
                    ClearedBalance = g.Where(x => x.AvailabilityClass == "cleared").Sum(x => x.Amount),
                    UnclearedBalance = g.Where(x => x.AvailabilityClass == "uncleared").Sum(x => x.Amount)
                })
                .ToListAsync(ct);

            var ledgerJournal = await _context.TrustJournalEntries
                .AsNoTracking()
                .Where(j => j.ClientTrustLedgerId != null && accountIds.Contains(j.TrustAccountId))
                .GroupBy(j => j.ClientTrustLedgerId!)
                .Select(g => new
                {
                    LedgerId = g.Key,
                    RunningBalance = g.Sum(x => x.Amount),
                    ClearedBalance = g.Where(x => x.AvailabilityClass == "cleared").Sum(x => x.Amount),
                    UnclearedBalance = g.Where(x => x.AvailabilityClass == "uncleared").Sum(x => x.Amount)
                })
                .ToListAsync(ct);

            var accountJournalMap = accountJournal.ToDictionary(
                item => item.TrustAccountId,
                item => (
                    NormalizeMoney(item.CurrentBalance),
                    NormalizeMoney(item.ClearedBalance),
                    NormalizeMoney(item.UnclearedBalance)),
                StringComparer.Ordinal);
            var ledgerJournalMap = ledgerJournal.ToDictionary(
                item => item.LedgerId,
                item => (
                    NormalizeMoney(item.RunningBalance),
                    NormalizeMoney(item.ClearedBalance),
                    NormalizeMoney(item.UnclearedBalance)),
                StringComparer.Ordinal);

            var driftedAccountCount = 0;
            var driftedLedgerCount = 0;
            var touchedAccountIds = new HashSet<string>(StringComparer.Ordinal);
            var now = DateTime.UtcNow;

            foreach (var account in accounts)
            {
                var expected = accountJournalMap.TryGetValue(account.Id, out var values)
                    ? values
                    : (0m, 0m, 0m);
                var expectedAvailable = NormalizeMoney(Math.Max(0m, expected.Item2));
                var hasDrift =
                    HasProjectionDrift(account.CurrentBalance, expected.Item1) ||
                    HasProjectionDrift(account.ClearedBalance, expected.Item2) ||
                    HasProjectionDrift(account.UnclearedBalance, expected.Item3) ||
                    HasProjectionDrift(account.AvailableDisbursementCapacity, expectedAvailable);

                if (!hasDrift && onlyIfDrifted)
                {
                    continue;
                }

                if (hasDrift)
                {
                    driftedAccountCount++;
                }

                account.CurrentBalance = expected.Item1;
                account.ClearedBalance = expected.Item2;
                account.UnclearedBalance = expected.Item3;
                account.AvailableDisbursementCapacity = expectedAvailable;
                account.UpdatedAt = now;
                account.RowVersion = NewRowVersion();
                touchedAccountIds.Add(account.Id);
            }

            foreach (var ledger in ledgers)
            {
                var expected = ledgerJournalMap.TryGetValue(ledger.Id, out var values)
                    ? values
                    : (0m, 0m, 0m);
                var expectedAvailable = NormalizeMoney(Math.Max(0m, expected.Item2 - ledger.HoldAmount));
                var hasDrift =
                    HasProjectionDrift(ledger.RunningBalance, expected.Item1) ||
                    HasProjectionDrift(ledger.ClearedBalance, expected.Item2) ||
                    HasProjectionDrift(ledger.UnclearedBalance, expected.Item3) ||
                    HasProjectionDrift(ledger.AvailableToDisburse, expectedAvailable);

                if (!hasDrift && onlyIfDrifted)
                {
                    continue;
                }

                if (hasDrift)
                {
                    driftedLedgerCount++;
                }

                ledger.RunningBalance = expected.Item1;
                ledger.ClearedBalance = expected.Item2;
                ledger.UnclearedBalance = expected.Item3;
                ledger.AvailableToDisburse = expectedAvailable;
                ledger.UpdatedAt = now;
                ledger.RowVersion = NewRowVersion();
                touchedAccountIds.Add(ledger.TrustAccountId);
            }

            if (touchedAccountIds.Count > 0)
            {
                await SaveChangesWithConcurrencyHandlingAsync(ct);
            }

            var result = new TrustProjectionRebuildResult
            {
                RebuiltAt = now,
                AccountCount = touchedAccountIds.Count == 0 && !onlyIfDrifted ? accounts.Count : touchedAccountIds.Count,
                LedgerCount = onlyIfDrifted ? driftedLedgerCount : ledgers.Count,
                DriftedAccountCount = driftedAccountCount,
                DriftedLedgerCount = driftedLedgerCount,
                TrustAccountIds = touchedAccountIds.OrderBy(id => id, StringComparer.Ordinal).ToList()
            };

            await LogAsync("trust.projection.rebuild", "TrustProjection", targetTrustAccountId, $"Accounts={result.AccountCount}, Ledgers={result.LedgerCount}, DriftedAccounts={driftedAccountCount}, DriftedLedgers={driftedLedgerCount}");
            return result;
        }

        private async Task<TrustTransaction> CreateDepositCoreAsync(DepositRequest request, CancellationToken ct)
        {
            await EnsureBillingPeriodUnlockedAsync("trust_deposit_create", "Billing period is locked. Cannot post deposit.", ct);
            var currentUserId = RequireCurrentUserId();

            if (request.Amount <= 0)
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Deposit amount must be positive");
            }

            var allocations = request.Allocations ?? new List<AllocationDto>();
            if (allocations.Count == 0)
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "At least one allocation is required.");
            }

            if (allocations.Any(a => a.Amount <= 0))
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Allocation amounts must be positive.");
            }

            var totalAllocations = allocations.Sum(a => a.Amount);
            if (Math.Abs(totalAllocations - request.Amount) > 0.01m)
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Allocation total must match deposit amount.");
            }

            var account = await _context.TrustBankAccounts
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == request.TrustAccountId, ct);
            if (account == null)
            {
                throw new TrustCommandException(StatusCodes.Status404NotFound, "Trust account not found");
            }

            if (account.Status != TrustAccountStatus.ACTIVE)
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Trust account is not active.");
            }

            var ledgerIds = allocations.Select(a => a.LedgerId).Distinct().ToList();
            var ledgers = await _context.ClientTrustLedgers
                .AsNoTracking()
                .Where(l => ledgerIds.Contains(l.Id))
                .Select(l => new { l.Id, l.TrustAccountId, l.Status, l.MatterId })
                .ToListAsync(ct);

            if (ledgers.Count != ledgerIds.Count)
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "One or more client ledgers were not found.");
            }

            if (ledgers.Any(l => l.TrustAccountId != request.TrustAccountId))
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Ledger does not belong to the selected trust account.");
            }

            if (ledgers.Any(l => l.Status != LedgerStatus.ACTIVE))
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "One or more ledgers are not active.");
            }

            var matterId = ledgers.FirstOrDefault()?.MatterId;
            if (string.IsNullOrWhiteSpace(matterId) || ledgers.Any(l => l.MatterId != matterId))
            {
                matterId = null;
            }

            var tx = new TrustTransaction
            {
                Id = Guid.NewGuid().ToString(),
                TrustAccountId = request.TrustAccountId,
                MatterId = matterId,
                LedgerId = allocations.Count == 1 ? allocations[0].LedgerId : null,
                EntityId = account.EntityId,
                OfficeId = account.OfficeId,
                Type = "DEPOSIT",
                Amount = request.Amount,
                Description = request.Description,
                Reference = request.CheckNumber,
                PayorPayee = request.PayorPayee,
                CheckNumber = request.CheckNumber,
                AllocationsJson = JsonSerializer.Serialize(allocations),
                Status = "PENDING",
                CreatedBy = currentUserId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.TrustTransactions.Add(tx);
            await _context.SaveChangesAsync(ct);
            await LogAsync("trust.deposit", "TrustTransaction", tx.Id, $"Amount={request.Amount}, Account={account.Id}, Status={tx.Status}");
            return tx;
        }

        private async Task<TrustTransaction> CreateWithdrawalCoreAsync(WithdrawalRequest request, CancellationToken ct)
        {
            await EnsureBillingPeriodUnlockedAsync("trust_withdrawal_create", "Billing period is locked. Cannot post withdrawal.", ct);
            var currentUserId = RequireCurrentUserId();

            if (request.Amount <= 0)
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Withdrawal amount must be positive");
            }

            var account = await _context.TrustBankAccounts
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == request.TrustAccountId, ct);
            if (account == null)
            {
                throw new TrustCommandException(StatusCodes.Status404NotFound, "Trust account not found");
            }

            if (account.Status != TrustAccountStatus.ACTIVE)
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Trust account is not active.");
            }

            var ledger = await _context.ClientTrustLedgers
                .AsNoTracking()
                .FirstOrDefaultAsync(l => l.Id == request.LedgerId, ct);
            if (ledger == null)
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Client ledger not found");
            }

            if (ledger.TrustAccountId != request.TrustAccountId)
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Ledger does not belong to the selected trust account.");
            }

            if (ledger.Status != LedgerStatus.ACTIVE)
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Ledger is not active.");
            }

            var disbursementClass = NormalizeDisbursementClass(request.DisbursementClass, "client_disbursement");

            var tx = new TrustTransaction
            {
                Id = Guid.NewGuid().ToString(),
                TrustAccountId = request.TrustAccountId,
                LedgerId = request.LedgerId,
                MatterId = ledger.MatterId,
                EntityId = account.EntityId,
                OfficeId = account.OfficeId,
                Type = "WITHDRAWAL",
                Amount = request.Amount,
                Description = request.Description,
                Reference = request.CheckNumber,
                PayorPayee = request.PayorPayee,
                CheckNumber = request.CheckNumber,
                DisbursementClass = disbursementClass,
                Status = "PENDING",
                ApprovalStatus = "pending",
                CreatedBy = currentUserId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.TrustTransactions.Add(tx);
            await EnsureApprovalRequirementsAsync(tx, account, ledger, ct);
            await _context.SaveChangesAsync(ct);
            await _trustRiskRadarService.RecordTrustTransactionRiskAsync(tx, "trust_transaction_created", ct);
            await LogAsync("trust.withdrawal", "TrustTransaction", tx.Id, $"Amount={request.Amount}, Account={account.Id}, Status={tx.Status}");
            return tx;
        }

        private async Task<TrustTransaction> ApproveTransactionCoreAsync(string id, TrustApproveStepDto? step, CancellationToken ct)
        {
            _authorization.EnsureAllowed(TrustActionKeys.ApproveTransaction, GetCurrentUser());
            await EnsureBillingPeriodUnlockedAsync("trust_transaction_approve", "Billing period is locked. Cannot approve transaction.", ct);

            var currentUserId = RequireCurrentUserId();
            var currentUserRole = TrustActionAuthorizationService.GetRole(GetCurrentUser());
            var tx = await _context.TrustTransactions.FirstOrDefaultAsync(t => t.Id == id, ct);
            if (tx == null)
            {
                throw new TrustCommandException(StatusCodes.Status404NotFound, "Transaction not found");
            }

            if (tx.Status == "APPROVED")
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Transaction already approved.");
            }

            if (tx.Status == "VOIDED")
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Voided transactions cannot be approved.");
            }

            if (tx.Status == "REJECTED")
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Rejected transactions cannot be approved.");
            }

            var requirements = await _context.TrustApprovalRequirements
                .Where(r => r.TrustTransactionId == tx.Id)
                .OrderBy(r => r.CreatedAt)
                .ToListAsync(ct);

            if (requirements.Count == 0)
            {
                if (!string.IsNullOrWhiteSpace(tx.CreatedBy) &&
                    string.Equals(tx.CreatedBy, currentUserId, StringComparison.Ordinal))
                {
                    throw new TrustCommandException(StatusCodes.Status403Forbidden, "Maker-checker policy forbids approving your own transaction.");
                }

                return await PostApprovedTransactionAsync(tx, currentUserId, ct);
            }

            var decisions = await _context.TrustApprovalDecisions
                .Where(d => d.TrustTransactionId == tx.Id)
                .OrderBy(d => d.CreatedAt)
                .ToListAsync(ct);
            var existingOverride = await _context.TrustApprovalOverrides
                .AnyAsync(o => o.TrustTransactionId == tx.Id, ct);
            var resolvedPolicy = await _policyResolver.ResolveEffectivePolicyAsync(tx.TrustAccountId, ct);

            if (resolvedPolicy.Policy.RequireMakerChecker &&
                !string.IsNullOrWhiteSpace(tx.CreatedBy) &&
                string.Equals(tx.CreatedBy, currentUserId, StringComparison.Ordinal))
            {
                throw new TrustCommandException(StatusCodes.Status403Forbidden, "Maker-checker policy forbids approving your own transaction.");
            }

            var requestedRequirementType = step?.RequirementType?.Trim();
            var openRequirements = requirements.Where(r => r.Status == "pending").ToList();
            if (openRequirements.Count == 0)
            {
                return tx.Status == "APPROVED"
                    ? tx
                    : await PostApprovedTransactionAsync(tx, currentUserId, ct);
            }

            var eligibleRequirements = openRequirements
                .Where(r => string.IsNullOrWhiteSpace(requestedRequirementType) || string.Equals(r.RequirementType, requestedRequirementType, StringComparison.OrdinalIgnoreCase))
                .Where(r => CanCurrentActorSatisfyRequirement(r, tx, resolvedPolicy, currentUserId, currentUserRole))
                .ToList();

            if (eligibleRequirements.Count == 0)
            {
                throw new TrustCommandException(StatusCodes.Status403Forbidden, "Current actor cannot satisfy any open approval requirement for this transaction.");
            }

            var notes = step?.Notes?.Trim();
            foreach (var requirement in eligibleRequirements)
            {
                if (decisions.Any(d =>
                    d.TrustApprovalRequirementId == requirement.Id &&
                    string.Equals(d.ActorUserId, currentUserId, StringComparison.Ordinal)))
                {
                    continue;
                }

                var decision = new TrustApprovalDecision
                {
                    Id = Guid.NewGuid().ToString(),
                    TrustTransactionId = tx.Id,
                    TrustApprovalRequirementId = requirement.Id,
                    ActorUserId = currentUserId,
                    ActorRole = currentUserRole,
                    DecisionType = "approve",
                    Notes = notes,
                    CreatedAt = DateTime.UtcNow
                };
                _context.TrustApprovalDecisions.Add(decision);
                decisions.Add(decision);
            }

            RecomputeRequirementStatuses(requirements, decisions);
            tx.ApprovalStatus = requirements.All(r => r.Status != "pending") ? "approved" : "partially_approved";
            tx.PolicyDecisionJson = SerializePolicyDecision(tx.TrustAccountId, resolvedPolicy.Policy, requirements, decisions, existingOverride);
            tx.UpdatedAt = DateTime.UtcNow;
            tx.RowVersion = NewRowVersion();

            if (requirements.Any(r => r.Status == "pending"))
            {
                await SaveChangesWithConcurrencyHandlingAsync(ct);
                await LogAsync("trust.transaction.approve_step", "TrustTransaction", tx.Id, $"ApprovalStatus={tx.ApprovalStatus}");
                return tx;
            }

            return await PostApprovedTransactionAsync(tx, currentUserId, ct);
        }

        private async Task<TrustTransaction> OverrideTransactionCoreAsync(string id, TrustOverrideDto dto, CancellationToken ct)
        {
            _authorization.EnsureAllowed(TrustActionKeys.OverrideTransaction, GetCurrentUser());
            await EnsureBillingPeriodUnlockedAsync("trust_transaction_override", "Billing period is locked. Cannot override transaction approvals.", ct);

            if (dto == null || string.IsNullOrWhiteSpace(dto.Reason))
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Override reason is required.");
            }

            var currentUserId = RequireCurrentUserId();
            var currentUserRole = TrustActionAuthorizationService.GetRole(GetCurrentUser());
            var tx = await _context.TrustTransactions.FirstOrDefaultAsync(t => t.Id == id, ct);
            if (tx == null)
            {
                throw new TrustCommandException(StatusCodes.Status404NotFound, "Transaction not found.");
            }

            if (tx.Status == "APPROVED" || tx.Status == "VOIDED" || tx.Status == "REJECTED")
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, $"Cannot override transaction in status '{tx.Status}'.");
            }

            var resolvedPolicy = await _policyResolver.ResolveEffectivePolicyAsync(tx.TrustAccountId, ct);
            if (!resolvedPolicy.OverrideApproverRoles.Contains(currentUserRole ?? string.Empty, StringComparer.OrdinalIgnoreCase) &&
                !_authorization.IsAllowed(TrustActionKeys.OverrideTransaction, currentUserRole))
            {
                throw new TrustCommandException(StatusCodes.Status403Forbidden, "Current role is not allowed to override trust approval requirements.");
            }

            var requirements = await _context.TrustApprovalRequirements
                .Where(r => r.TrustTransactionId == tx.Id)
                .OrderBy(r => r.CreatedAt)
                .ToListAsync(ct);

            if (requirements.Count == 0)
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Transaction has no active approval requirements.");
            }

            var targetRequirements = requirements
                .Where(r => r.Status == "pending")
                .Where(r => string.IsNullOrWhiteSpace(dto.RequirementType) || string.Equals(r.RequirementType, dto.RequirementType, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (targetRequirements.Count == 0)
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "No matching pending approval requirement was found.");
            }

            foreach (var requirement in targetRequirements)
            {
                _context.TrustApprovalOverrides.Add(new TrustApprovalOverride
                {
                    Id = Guid.NewGuid().ToString(),
                    TrustTransactionId = tx.Id,
                    TrustApprovalRequirementId = requirement.Id,
                    ActorUserId = currentUserId,
                    ActorRole = currentUserRole,
                    Reason = dto.Reason.Trim(),
                    MetadataJson = dto.MetadataJson?.Trim(),
                    CreatedAt = DateTime.UtcNow
                });
                requirement.Status = "overridden";
                requirement.SatisfiedCount = requirement.RequiredCount;
                requirement.UpdatedAt = DateTime.UtcNow;
            }

            var decisions = await _context.TrustApprovalDecisions
                .Where(d => d.TrustTransactionId == tx.Id)
                .OrderBy(d => d.CreatedAt)
                .ToListAsync(ct);

            tx.ApprovalStatus = requirements.All(r => r.Status != "pending") ? "overridden" : "pending";
            tx.PolicyDecisionJson = SerializePolicyDecision(tx.TrustAccountId, resolvedPolicy.Policy, requirements, decisions, hasOverride: true);
            tx.UpdatedAt = DateTime.UtcNow;
            tx.RowVersion = NewRowVersion();

            if (requirements.All(r => r.Status != "pending"))
            {
                return await PostApprovedTransactionAsync(tx, currentUserId, ct);
            }

            await SaveChangesWithConcurrencyHandlingAsync(ct);
            await LogAsync("trust.transaction.override", "TrustTransaction", tx.Id, $"Reason={dto.Reason}");
            return tx;
        }

        private async Task<TrustTransaction> RejectTransactionCoreAsync(string id, TrustRejectDto? dto, CancellationToken ct)
        {
            _authorization.EnsureAllowed(TrustActionKeys.RejectTransaction, GetCurrentUser());

            var tx = await _context.TrustTransactions.FirstOrDefaultAsync(t => t.Id == id, ct);
            if (tx == null)
            {
                throw new TrustCommandException(StatusCodes.Status404NotFound, "Transaction not found");
            }

            if (tx.Status == "APPROVED")
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Approved transactions cannot be rejected.");
            }

            if (tx.Status == "VOIDED")
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Voided transactions cannot be rejected.");
            }

            if (tx.Status == "REJECTED")
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Transaction already rejected.");
            }

            tx.Status = "REJECTED";
            tx.RejectedBy = RequireCurrentUserId();
            tx.RejectedAt = DateTime.UtcNow;
            tx.RejectionReason = dto?.Reason;
            tx.UpdatedAt = DateTime.UtcNow;
            tx.RowVersion = NewRowVersion();

            await SaveChangesWithConcurrencyHandlingAsync(ct);
            await LogAsync("trust.transaction.reject", "TrustTransaction", tx.Id, $"Reason={dto?.Reason}");
            return tx;
        }

        private async Task<TrustTransaction> VoidTransactionCoreAsync(string id, TrustVoidDto? dto, CancellationToken ct)
        {
            _authorization.EnsureAllowed(TrustActionKeys.VoidTransaction, GetCurrentUser());
            await EnsureBillingPeriodUnlockedAsync("trust_transaction_void", "Billing period is locked. Cannot void transaction.", ct);

            var tx = await _context.TrustTransactions.FirstOrDefaultAsync(t => t.Id == id, ct);
            if (tx == null)
            {
                throw new TrustCommandException(StatusCodes.Status404NotFound, "Transaction not found");
            }

            if (tx.Status != "APPROVED")
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Only approved transactions can be voided.");
            }

            if (tx.IsVoided || tx.Status == "VOIDED")
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Transaction already voided.");
            }

            ClientTrustLedger? ledger = null;
            if (!string.IsNullOrWhiteSpace(tx.LedgerId))
            {
                ledger = await _context.ClientTrustLedgers.FirstOrDefaultAsync(l => l.Id == tx.LedgerId, ct);
                if (ledger == null && string.Equals(tx.Type, "WITHDRAWAL", StringComparison.OrdinalIgnoreCase))
                {
                    throw new TrustCommandException(StatusCodes.Status400BadRequest, "Client ledger not found");
                }
            }

            await _trustRiskRadarService.EnforceNoActiveHardHoldsAsync(new TrustRiskRadarService.TrustRiskHoldGuardContext
            {
                OperationType = "trust_transaction_void",
                TrustTransactionId = tx.Id,
                MatterId = tx.MatterId,
                ClientId = ledger?.ClientId
            }, ct);

            var account = await _context.TrustBankAccounts.FirstOrDefaultAsync(a => a.Id == tx.TrustAccountId, ct);
            if (account == null)
            {
                throw new TrustCommandException(StatusCodes.Status404NotFound, "Trust account not found");
            }

            var currentUserId = RequireCurrentUserId();
            var effectiveAt = DateTime.UtcNow;
            var entriesToReverse = await LoadOpenJournalEntriesForTransactionAsync(tx.Id, ct);
            if (entriesToReverse.Count == 0)
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "No open posted journal entries were found for this transaction.");
            }

            var reversalEntries = BuildReversalJournalEntries(tx, entriesToReverse, currentUserId, effectiveAt, tx.VoidReason ?? dto?.Reason, "reversal");
            var reversalBatch = CreatePostingBatch(tx, account.Id, "reversal", currentUserId, effectiveAt, tx.PostingBatchId, reversalEntries);
            foreach (var journalEntry in reversalEntries)
            {
                journalEntry.PostingBatchId = reversalBatch.Id;
            }

            _context.TrustPostingBatches.Add(reversalBatch);
            _context.TrustJournalEntries.AddRange(reversalEntries);
            await ApplyJournalEntriesToProjectionsAsync(account, reversalEntries, ct);

            tx.IsVoided = true;
            tx.Status = "VOIDED";
            tx.VoidReason = dto?.Reason;
            tx.VoidedAt = effectiveAt;
            tx.UpdatedAt = effectiveAt;
            tx.RowVersion = NewRowVersion();

            await SaveChangesWithConcurrencyHandlingAsync(ct);
            await LogAsync("trust.transaction.void", "TrustTransaction", tx.Id, $"Reason={dto?.Reason}");
            return tx;
        }

        private async Task<TrustTransaction> ClearDepositCoreAsync(string id, TrustClearDepositDto? dto, CancellationToken ct)
        {
            _authorization.EnsureAllowed(TrustActionKeys.ClearDeposit, GetCurrentUser());
            await EnsureBillingPeriodUnlockedAsync("trust_deposit_clear", "Billing period is locked. Cannot clear deposit.", ct);

            var tx = await _context.TrustTransactions.FirstOrDefaultAsync(t => t.Id == id, ct);
            if (tx == null)
            {
                throw new TrustCommandException(StatusCodes.Status404NotFound, "Transaction not found");
            }

            if (!string.Equals(tx.Type, "DEPOSIT", StringComparison.OrdinalIgnoreCase))
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Only deposit transactions can be cleared.");
            }

            if (!string.Equals(tx.Status, "APPROVED", StringComparison.OrdinalIgnoreCase))
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Only approved deposits can be cleared.");
            }

            if (!string.Equals(tx.ClearingStatus, "pending_clearance", StringComparison.OrdinalIgnoreCase))
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Deposit is not pending clearance.");
            }

            var account = await _context.TrustBankAccounts.FirstOrDefaultAsync(a => a.Id == tx.TrustAccountId, ct);
            if (account == null)
            {
                throw new TrustCommandException(StatusCodes.Status404NotFound, "Trust account not found");
            }

            var postedEntries = await _context.TrustJournalEntries
                .Where(j =>
                    j.TrustTransactionId == tx.Id &&
                    j.EntryKind == "posting" &&
                    j.AvailabilityClass == "uncleared")
                .OrderBy(j => j.CreatedAt)
                .ToListAsync(ct);

            if (postedEntries.Count == 0)
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "No uncleared deposit journal entries were found for this transaction.");
            }

            var currentUserId = RequireCurrentUserId();
            var effectiveAt = dto?.ClearedAt?.ToUniversalTime() ?? DateTime.UtcNow;
            var clearanceEntries = BuildClearanceJournalEntries(tx, postedEntries, currentUserId, effectiveAt, dto?.Notes);
            var clearanceBatch = CreatePostingBatch(tx, account.Id, "clearance", currentUserId, effectiveAt, tx.PostingBatchId, clearanceEntries);
            foreach (var journalEntry in clearanceEntries)
            {
                journalEntry.PostingBatchId = clearanceBatch.Id;
            }

            _context.TrustPostingBatches.Add(clearanceBatch);
            _context.TrustJournalEntries.AddRange(clearanceEntries);
            await ApplyJournalEntriesToProjectionsAsync(account, clearanceEntries, ct);

            tx.ClearingStatus = "cleared";
            tx.ClearedAt = effectiveAt;
            tx.UpdatedAt = effectiveAt;
            tx.RowVersion = NewRowVersion();

            await SaveChangesWithConcurrencyHandlingAsync(ct);
            await LogAsync("trust.deposit.clear", "TrustTransaction", tx.Id, $"ClearedAt={effectiveAt:O}");
            return tx;
        }

        private async Task<TrustTransaction> ReturnDepositCoreAsync(string id, TrustReturnDepositDto? dto, CancellationToken ct)
        {
            _authorization.EnsureAllowed(TrustActionKeys.ReturnDeposit, GetCurrentUser());
            await EnsureBillingPeriodUnlockedAsync("trust_deposit_return", "Billing period is locked. Cannot return deposit.", ct);

            var tx = await _context.TrustTransactions.FirstOrDefaultAsync(t => t.Id == id, ct);
            if (tx == null)
            {
                throw new TrustCommandException(StatusCodes.Status404NotFound, "Transaction not found");
            }

            if (!string.Equals(tx.Type, "DEPOSIT", StringComparison.OrdinalIgnoreCase))
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Only deposit transactions can be returned.");
            }

            if (!string.Equals(tx.Status, "APPROVED", StringComparison.OrdinalIgnoreCase))
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Only approved deposits can be returned.");
            }

            if (string.Equals(tx.ClearingStatus, "returned", StringComparison.OrdinalIgnoreCase))
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Deposit already marked as returned.");
            }

            var account = await _context.TrustBankAccounts.FirstOrDefaultAsync(a => a.Id == tx.TrustAccountId, ct);
            if (account == null)
            {
                throw new TrustCommandException(StatusCodes.Status404NotFound, "Trust account not found");
            }

            var entriesToReverse = await LoadOpenJournalEntriesForTransactionAsync(tx.Id, ct);
            if (entriesToReverse.Count == 0)
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "No open posted journal entries were found for this deposit.");
            }

            var currentUserId = RequireCurrentUserId();
            var effectiveAt = dto?.ReturnedAt?.ToUniversalTime() ?? DateTime.UtcNow;
            var returnEntries = BuildReversalJournalEntries(tx, entriesToReverse, currentUserId, effectiveAt, dto?.Reason, "return");
            var returnBatch = CreatePostingBatch(tx, account.Id, "reversal", currentUserId, effectiveAt, tx.PostingBatchId, returnEntries);
            foreach (var journalEntry in returnEntries)
            {
                journalEntry.PostingBatchId = returnBatch.Id;
            }

            _context.TrustPostingBatches.Add(returnBatch);
            _context.TrustJournalEntries.AddRange(returnEntries);
            await ApplyJournalEntriesToProjectionsAsync(account, returnEntries, ct);

            tx.ClearingStatus = "returned";
            tx.ReturnedAt = effectiveAt;
            tx.ReturnReason = dto?.Reason;
            tx.UpdatedAt = effectiveAt;
            tx.RowVersion = NewRowVersion();

            await SaveChangesWithConcurrencyHandlingAsync(ct);
            await LogAsync("trust.deposit.return", "TrustTransaction", tx.Id, $"ReturnedAt={effectiveAt:O}, Reason={dto?.Reason}");
            return tx;
        }

        private async Task<(TrustCommandDeduplication? Record, TrustTransaction? Replay)> BeginTrustTransactionCommandAsync(
            string commandName,
            string idempotencyKey,
            string requestFingerprint,
            CancellationToken ct)
        {
            var actorUserId = RequireCurrentUserId();
            var existing = await FindTrustCommandDeduplicationAsync(commandName, actorUserId, idempotencyKey, ct);
            if (existing != null)
            {
                return (null, await ResolveTrustCommandReplayAsync(existing, requestFingerprint, ct));
            }

            var record = new TrustCommandDeduplication
            {
                Id = Guid.NewGuid().ToString(),
                CommandName = commandName,
                ActorUserId = actorUserId,
                IdempotencyKey = idempotencyKey,
                RequestFingerprint = requestFingerprint,
                Status = "in_progress",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.TrustCommandDeduplications.Add(record);
            try
            {
                await _context.SaveChangesAsync(ct);
                return (record, null);
            }
            catch (DbUpdateException)
            {
                _context.Entry(record).State = EntityState.Detached;
                existing = await FindTrustCommandDeduplicationAsync(commandName, actorUserId, idempotencyKey, ct);
                if (existing == null)
                {
                    throw;
                }

                return (null, await ResolveTrustCommandReplayAsync(existing, requestFingerprint, ct));
            }
        }

        private async Task<TrustCommandDeduplication?> FindTrustCommandDeduplicationAsync(
            string commandName,
            string actorUserId,
            string idempotencyKey,
            CancellationToken ct)
        {
            return await _context.TrustCommandDeduplications
                .FirstOrDefaultAsync(
                    r => r.CommandName == commandName &&
                         r.ActorUserId == actorUserId &&
                         r.IdempotencyKey == idempotencyKey,
                    ct);
        }

        private async Task<TrustTransaction?> ResolveTrustCommandReplayAsync(
            TrustCommandDeduplication record,
            string requestFingerprint,
            CancellationToken ct)
        {
            if (!string.Equals(record.RequestFingerprint, requestFingerprint, StringComparison.Ordinal))
            {
                throw new TrustCommandException(StatusCodes.Status409Conflict, "Idempotency key was already used for a different trust command payload.");
            }

            if (!string.Equals(record.Status, "completed", StringComparison.OrdinalIgnoreCase))
            {
                throw new TrustCommandException(StatusCodes.Status409Conflict, "A matching trust command is already in progress. Retry after it completes.");
            }

            if (!string.Equals(record.ResultEntityType, nameof(TrustTransaction), StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(record.ResultEntityId))
            {
                throw new TrustCommandException(StatusCodes.Status409Conflict, "The idempotent trust command completed but its replay target is unavailable.");
            }

            var replay = await _context.TrustTransactions.FirstOrDefaultAsync(t => t.Id == record.ResultEntityId, ct);
            if (replay == null)
            {
                throw new TrustCommandException(StatusCodes.Status409Conflict, "The idempotent trust command completed but its transaction could not be reloaded.");
            }

            return replay;
        }

        private static void CompleteTrustTransactionCommand(TrustCommandDeduplication? record, TrustTransaction transaction)
        {
            if (record == null)
            {
                return;
            }

            record.Status = "completed";
            record.ResultEntityType = nameof(TrustTransaction);
            record.ResultEntityId = transaction.Id;
            record.ResultStatusCode = StatusCodes.Status200OK;
            record.CompletedAt = DateTime.UtcNow;
            record.UpdatedAt = DateTime.UtcNow;
        }

        private static string BuildDepositFingerprint(DepositRequest request)
        {
            var allocations = (request.Allocations ?? [])
                .Select(a => new
                {
                    LedgerId = a.LedgerId?.Trim(),
                    Amount = NormalizeMoney(a.Amount),
                    Description = a.Description?.Trim()
                })
                .OrderBy(a => a.LedgerId, StringComparer.Ordinal)
                .ThenBy(a => a.Amount)
                .ThenBy(a => a.Description, StringComparer.Ordinal)
                .ToArray();

            return ComputeFingerprint(new
            {
                TrustAccountId = request.TrustAccountId?.Trim(),
                Amount = NormalizeMoney(request.Amount),
                Description = request.Description?.Trim(),
                PayorPayee = request.PayorPayee?.Trim(),
                CheckNumber = request.CheckNumber?.Trim(),
                Allocations = allocations
            });
        }

        private static string BuildWithdrawalFingerprint(WithdrawalRequest request)
        {
            return ComputeFingerprint(new
            {
                TrustAccountId = request.TrustAccountId?.Trim(),
                LedgerId = request.LedgerId?.Trim(),
                Amount = NormalizeMoney(request.Amount),
                Description = request.Description?.Trim(),
                PayorPayee = request.PayorPayee?.Trim(),
                CheckNumber = request.CheckNumber?.Trim()
            });
        }

        private static string BuildRejectFingerprint(string id, TrustRejectDto? dto)
        {
            return ComputeFingerprint(new
            {
                TransactionId = id?.Trim(),
                Reason = dto?.Reason?.Trim()
            });
        }

        private static string BuildVoidFingerprint(string id, TrustVoidDto? dto)
        {
            return ComputeFingerprint(new
            {
                TransactionId = id?.Trim(),
                Reason = dto?.Reason?.Trim()
            });
        }

        private static string BuildClearFingerprint(string id, TrustClearDepositDto? dto)
        {
            return ComputeFingerprint(new
            {
                TransactionId = id?.Trim(),
                ClearedAt = dto?.ClearedAt?.ToUniversalTime(),
                Notes = dto?.Notes?.Trim()
            });
        }

        private static string BuildReturnFingerprint(string id, TrustReturnDepositDto? dto)
        {
            return ComputeFingerprint(new
            {
                TransactionId = id?.Trim(),
                ReturnedAt = dto?.ReturnedAt?.ToUniversalTime(),
                Reason = dto?.Reason?.Trim()
            });
        }

        private static string BuildTargetOnlyFingerprint(string id)
        {
            return ComputeFingerprint(new
            {
                TransactionId = id?.Trim()
            });
        }

        private async Task<TrustStatementImport?> ResolveStatementImportAsync(
            string trustAccountId,
            string? statementImportId,
            DateTime periodStart,
            DateTime periodEnd,
            CancellationToken ct)
        {
            if (!string.IsNullOrWhiteSpace(statementImportId))
            {
                var selected = await _context.TrustStatementImports
                    .FirstOrDefaultAsync(s => s.Id == statementImportId, ct);
                if (selected == null)
                {
                    throw new TrustCommandException(StatusCodes.Status404NotFound, "Statement import not found.");
                }

                if (!string.Equals(selected.TrustAccountId, trustAccountId, StringComparison.Ordinal))
                {
                    throw new TrustCommandException(StatusCodes.Status400BadRequest, "Statement import does not belong to the selected trust account.");
                }

                if (string.Equals(selected.Status, "superseded", StringComparison.OrdinalIgnoreCase))
                {
                    throw new TrustCommandException(StatusCodes.Status409Conflict, "Selected statement import has been superseded. Use the current statement version.");
                }

                if (string.Equals(selected.Status, "duplicate", StringComparison.OrdinalIgnoreCase))
                {
                    throw new TrustCommandException(StatusCodes.Status409Conflict, "Selected statement import is an evidence-only duplicate. Use the active statement version.");
                }

                return selected;
            }

            return await _context.TrustStatementImports
                .AsNoTracking()
                .Where(s =>
                    s.TrustAccountId == trustAccountId &&
                    s.PeriodStart == periodStart &&
                    s.PeriodEnd == periodEnd &&
                    s.Status != "duplicate")
                .OrderBy(s => s.Status == "superseded" ? 1 : 0)
                .ThenByDescending(s => s.ImportedAt)
                .FirstOrDefaultAsync(ct);
        }

        private async Task<List<TrustOutstandingItem>> BuildAutoOutstandingItemsAsync(
            string trustAccountId,
            TrustStatementImport? statementImport,
            IReadOnlyList<TrustStatementLine> statementLines,
            DateTime periodStart,
            DateTime periodEnd,
            DateTime cutoff,
            string currentUserId,
            DateTime now,
            CancellationToken ct)
        {
            var unclearedDeposits = await _context.TrustJournalEntries
                .AsNoTracking()
                .Where(j =>
                    j.TrustAccountId == trustAccountId &&
                    j.OperationType == "deposit" &&
                    j.AvailabilityClass == "uncleared" &&
                    j.EffectiveAt <= cutoff)
                .GroupBy(j => new { j.TrustTransactionId, j.ClientTrustLedgerId })
                .Select(g => new
                {
                    g.Key.TrustTransactionId,
                    g.Key.ClientTrustLedgerId,
                    Amount = g.Sum(x => x.Amount),
                    OccurredAt = g.Min(x => x.EffectiveAt),
                    Reference = g.Select(x => x.Description).FirstOrDefault()
                })
                .ToListAsync(ct);

            var items = unclearedDeposits
                .Where(x => NormalizeMoney(x.Amount) > 0m)
                .Select(x => new TrustOutstandingItem
                {
                    Id = Guid.NewGuid().ToString(),
                    TrustAccountId = trustAccountId,
                    TrustTransactionId = x.TrustTransactionId,
                    ClientTrustLedgerId = x.ClientTrustLedgerId,
                    TrustStatementImportId = statementImport?.Id,
                    PeriodStart = periodStart,
                    PeriodEnd = periodEnd,
                    OccurredAt = x.OccurredAt,
                    ItemType = "deposit_in_transit",
                    ImpactDirection = "increase_bank",
                    Status = "open",
                    Source = "auto",
                    Amount = NormalizeMoney(x.Amount),
                    CorrelationKey = $"auto:{trustAccountId}:{periodEnd:yyyyMMdd}:deposit:{x.TrustTransactionId}",
                    Reference = x.TrustTransactionId,
                    Description = x.Reference ?? "Deposit pending bank clearance.",
                    CreatedBy = currentUserId,
                    CreatedAt = now,
                    UpdatedAt = now,
                    MetadataJson = JsonSerializer.Serialize(new { rule = "uncleared_deposit_balance" })
                })
                .ToList();

            if (statementImport == null)
            {
                return items;
            }

            var matchedTransactionIds = statementLines
                .Where(IsStatementLineMatchedForPacket)
                .Select(l => l.MatchedTrustTransactionId!)
                .ToHashSet(StringComparer.Ordinal);

            var withdrawals = await _context.TrustTransactions
                .AsNoTracking()
                .Where(t =>
                    t.TrustAccountId == trustAccountId &&
                    t.Status == "APPROVED" &&
                    !t.IsVoided &&
                    t.Type == "WITHDRAWAL" &&
                    t.CreatedAt <= cutoff &&
                    !string.IsNullOrWhiteSpace(t.CheckNumber))
                .Select(t => new
                {
                    t.Id,
                    t.LedgerId,
                    t.CheckNumber,
                    t.Reference,
                    t.Description,
                    t.Amount,
                    OccurredAt = t.ApprovedAt ?? t.CreatedAt
                })
                .ToListAsync(ct);

            items.AddRange(withdrawals
                .Where(t => !matchedTransactionIds.Contains(t.Id))
                .Select(t => new TrustOutstandingItem
                {
                    Id = Guid.NewGuid().ToString(),
                    TrustAccountId = trustAccountId,
                    TrustTransactionId = t.Id,
                    ClientTrustLedgerId = t.LedgerId,
                    TrustStatementImportId = statementImport.Id,
                    PeriodStart = periodStart,
                    PeriodEnd = periodEnd,
                    OccurredAt = t.OccurredAt,
                    ItemType = "outstanding_check",
                    ImpactDirection = "decrease_bank",
                    Status = "open",
                    Source = "auto",
                    Amount = NormalizeMoney(t.Amount),
                    CorrelationKey = $"auto:{trustAccountId}:{periodEnd:yyyyMMdd}:withdrawal:{t.Id}",
                    Reference = t.CheckNumber ?? t.Reference,
                    Description = t.Description ?? "Withdrawal not found in imported statement lines.",
                    CreatedBy = currentUserId,
                    CreatedAt = now,
                    UpdatedAt = now,
                    MetadataJson = JsonSerializer.Serialize(new { rule = "statement_line_check_match" })
                }));

            items.AddRange(statementLines
                .Where(l => !IsStatementLineResolvedForPacket(l))
                .Select(l =>
                {
                    var isDepositLike = l.Amount >= 0m;
                    return new TrustOutstandingItem
                    {
                        Id = Guid.NewGuid().ToString(),
                        TrustAccountId = trustAccountId,
                        TrustStatementImportId = statementImport.Id,
                        TrustStatementLineId = l.Id,
                        PeriodStart = periodStart,
                        PeriodEnd = periodEnd,
                        OccurredAt = l.EffectiveAt ?? l.PostedAt,
                        ItemType = "exception",
                        ImpactDirection = isDepositLike ? "decrease_bank" : "increase_bank",
                        Status = "open",
                        Source = "auto",
                        Amount = NormalizeMoney(Math.Abs(l.Amount)),
                        CorrelationKey = $"auto:{trustAccountId}:{periodEnd:yyyyMMdd}:statement_line:{l.Id}",
                        Reference = l.CheckNumber ?? l.Reference ?? l.ExternalLineId,
                        Description = l.Description ?? "Statement line is not matched to a trust transaction.",
                        CreatedBy = currentUserId,
                        CreatedAt = now,
                        UpdatedAt = now,
                        MetadataJson = JsonSerializer.Serialize(new
                        {
                            rule = "bank_statement_unmatched_activity",
                            matchStatus = l.MatchStatus,
                            matchMethod = l.MatchMethod
                        })
                    };
                }));

            return items;
        }

        private async Task<List<TrustOutstandingItem>> SyncAutoOutstandingItemsAsync(
            string trustAccountId,
            DateTime periodStart,
            DateTime periodEnd,
            string? statementImportId,
            string? activePacketId,
            IReadOnlyList<TrustOutstandingItem> computedItems,
            CancellationToken ct)
        {
            var existingItems = await _context.TrustOutstandingItems
                .Where(i =>
                    i.TrustAccountId == trustAccountId &&
                    i.Source == "auto" &&
                    i.PeriodStart == periodStart &&
                    i.PeriodEnd == periodEnd &&
                    (i.TrustReconciliationPacketId == null || i.TrustReconciliationPacketId == activePacketId))
                .ToListAsync(ct);

            var existingByKey = existingItems
                .Where(i => !string.IsNullOrWhiteSpace(i.CorrelationKey))
                .ToDictionary(i => i.CorrelationKey!, StringComparer.Ordinal);

            var nextKeys = new HashSet<string>(StringComparer.Ordinal);
            var results = new List<TrustOutstandingItem>(computedItems.Count);

            foreach (var item in computedItems)
            {
                if (!string.IsNullOrWhiteSpace(item.CorrelationKey))
                {
                    nextKeys.Add(item.CorrelationKey);
                }

                if (!string.IsNullOrWhiteSpace(item.CorrelationKey) && existingByKey.TryGetValue(item.CorrelationKey, out var existing))
                {
                    existing.TrustTransactionId = item.TrustTransactionId;
                    existing.ClientTrustLedgerId = item.ClientTrustLedgerId;
                    existing.TrustStatementImportId = statementImportId;
                    existing.TrustStatementLineId = item.TrustStatementLineId;
                    existing.OccurredAt = item.OccurredAt;
                    existing.ItemType = item.ItemType;
                    existing.ImpactDirection = item.ImpactDirection;
                    existing.Status = item.Status;
                    existing.Amount = item.Amount;
                    existing.Reference = item.Reference;
                    existing.Description = item.Description;
                    existing.MetadataJson = item.MetadataJson;
                    existing.UpdatedAt = item.UpdatedAt;
                    results.Add(existing);
                    continue;
                }

                item.TrustStatementImportId = statementImportId;
                _context.TrustOutstandingItems.Add(item);
                results.Add(item);
            }

            var toRemove = existingItems
                .Where(i => string.IsNullOrWhiteSpace(i.CorrelationKey) || !nextKeys.Contains(i.CorrelationKey))
                .ToList();
            if (toRemove.Count > 0)
            {
                _context.TrustOutstandingItems.RemoveRange(toRemove);
            }

            return results;
        }

        private List<TrustStatementLine> BuildStatementLinesForImport(
            TrustStatementImport import,
            IReadOnlyList<TrustStatementLineDto> lineDtos,
            IReadOnlyList<TrustStatementMatchCandidate> candidateTransactions,
            DateTime importedAt)
        {
            var candidateMap = BuildStatementCandidateMap(candidateTransactions);
            var reservedTransactionIds = new HashSet<string>(StringComparer.Ordinal);
            var statementLines = new List<TrustStatementLine>(lineDtos.Count);

            for (var index = 0; index < lineDtos.Count; index++)
            {
                var dto = lineDtos[index];
                var line = new TrustStatementLine
                {
                    Id = Guid.NewGuid().ToString(),
                    TrustStatementImportId = import.Id,
                    TrustAccountId = import.TrustAccountId,
                    PostedAt = EnsureUtc(dto.PostedAt),
                    EffectiveAt = dto.EffectiveAt?.ToUniversalTime(),
                    Amount = NormalizeMoney(dto.Amount),
                    BalanceAfter = dto.BalanceAfter.HasValue ? NormalizeMoney(dto.BalanceAfter.Value) : null,
                    Reference = dto.Reference?.Trim(),
                    CheckNumber = dto.CheckNumber?.Trim(),
                    Description = dto.Description?.Trim(),
                    Counterparty = dto.Counterparty?.Trim(),
                    ExternalLineId = string.IsNullOrWhiteSpace(dto.ExternalLineId) ? $"manual:{index + 1}" : dto.ExternalLineId.Trim(),
                    CreatedAt = importedAt,
                    UpdatedAt = importedAt
                };

                var matched = TryResolveAutoStatementMatch(line, candidateMap, reservedTransactionIds);
                if (matched == null)
                {
                    line.MatchStatus = "unmatched";
                    line.MatchMethod = "none";
                    line.MetadataJson = JsonSerializer.Serialize(new
                    {
                        source = import.Source,
                        importId = import.Id
                    });
                }
                else
                {
                    reservedTransactionIds.Add(matched.Id);
                    line.MatchStatus = "matched";
                    line.MatchMethod = "auto";
                    line.MatchConfidence = matched.Confidence;
                    line.MatchedTrustTransactionId = matched.Id;
                    line.MatchedAt = importedAt;
                    line.MatchNotes = $"Auto-matched by {matched.Strategy}.";
                    line.MetadataJson = JsonSerializer.Serialize(new
                    {
                        source = import.Source,
                        importId = import.Id,
                        candidateTrustTransactionId = matched.Id,
                        strategy = matched.Strategy
                    });
                }

                statementLines.Add(line);
            }

            return statementLines;
        }

        private async Task<List<TrustStatementMatchCandidate>> LoadStatementMatchCandidatesAsync(string trustAccountId, DateTime cutoff, CancellationToken ct)
        {
            var rows = await _context.TrustTransactions
                .AsNoTracking()
                .Where(t =>
                    t.TrustAccountId == trustAccountId &&
                    t.Status == "APPROVED" &&
                    !t.IsVoided &&
                    (t.ApprovedAt ?? t.CreatedAt) <= cutoff)
                .Select(t => new
                {
                    Id = t.Id,
                    t.Amount,
                    t.CheckNumber,
                    t.Reference,
                    OccurredAt = t.ApprovedAt ?? t.CreatedAt,
                    TransactionType = t.Type
                })
                .ToListAsync(ct);

            return rows.Select(row => new TrustStatementMatchCandidate
            {
                Id = row.Id,
                Amount = NormalizeMoney(Math.Abs(row.Amount)),
                MatchKey = NormalizeMatchKey(row.CheckNumber) ?? NormalizeMatchKey(row.Reference),
                OccurredAt = row.OccurredAt,
                TransactionType = row.TransactionType
            }).ToList();
        }

        private static Dictionary<string, List<TrustStatementMatchCandidate>> BuildStatementCandidateMap(IReadOnlyList<TrustStatementMatchCandidate> candidates)
        {
            return candidates
                .Where(c => !string.IsNullOrWhiteSpace(c.MatchKey))
                .GroupBy(c => BuildStatementCandidateMapKey(c.MatchKey!, c.Amount), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderBy(c => c.OccurredAt).ThenBy(c => c.Id, StringComparer.Ordinal).ToList(),
                    StringComparer.OrdinalIgnoreCase);
        }

        private static TrustStatementMatchCandidate? TryResolveAutoStatementMatch(
            TrustStatementLine line,
            IReadOnlyDictionary<string, List<TrustStatementMatchCandidate>> candidateMap,
            ISet<string> reservedTransactionIds)
        {
            var lineKey = NormalizeMatchKey(line.CheckNumber) ?? NormalizeMatchKey(line.Reference);
            if (string.IsNullOrWhiteSpace(lineKey))
            {
                return null;
            }

            var mapKey = BuildStatementCandidateMapKey(lineKey, NormalizeMoney(Math.Abs(line.Amount)));
            if (!candidateMap.TryGetValue(mapKey, out var candidates))
            {
                return null;
            }

            var best = candidates
                .Where(c => !reservedTransactionIds.Contains(c.Id))
                .OrderBy(c => Math.Abs((c.OccurredAt.Date - line.PostedAt.Date).TotalDays))
                .ThenBy(c => c.Id, StringComparer.Ordinal)
                .FirstOrDefault();

            if (best == null)
            {
                return null;
            }

            best.Strategy = "reference_amount";
            best.Confidence = 1.00m;
            return best;
        }

        private async Task RefreshCanonicalPacketForPeriodIfPresentAsync(
            string trustAccountId,
            DateTime periodStart,
            DateTime periodEnd,
            string? statementImportId,
            CancellationToken ct)
        {
            var existingPacket = await _context.TrustReconciliationPackets
                .AsNoTracking()
                .Where(p =>
                    p.TrustAccountId == trustAccountId &&
                    p.PeriodStart == periodStart &&
                    p.PeriodEnd == periodEnd)
                .OrderByDescending(p => p.IsCanonical)
                .ThenByDescending(p => p.PreparedAt)
                .FirstOrDefaultAsync(ct);

            if (existingPacket == null)
            {
                return;
            }

            if (existingPacket.IsCanonical && IsPacketTerminal(existingPacket.Status))
            {
                return;
            }

            var canonicalMonthCloseIsClosed = await _context.TrustMonthCloses.AsNoTracking()
                .AnyAsync(c =>
                    c.TrustAccountId == trustAccountId &&
                    c.PeriodStart == periodStart &&
                    c.PeriodEnd == periodEnd &&
                    c.IsCanonical &&
                    c.Status == "closed",
                    ct);
            if (canonicalMonthCloseIsClosed)
            {
                return;
            }

            await GenerateReconciliationPacketAsync(new TrustReconciliationPacketCreateDto
            {
                TrustAccountId = trustAccountId,
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                StatementImportId = statementImportId,
                Notes = existingPacket.Notes
            }, ct);
        }

        private static TrustStatementMatchingRunResultDto BuildStatementMatchingRunResult(string statementImportId, IReadOnlyList<TrustStatementLine> lines, DateTime processedAt)
        {
            return new TrustStatementMatchingRunResultDto
            {
                TrustStatementImportId = statementImportId,
                TotalLineCount = lines.Count,
                MatchedLineCount = lines.Count(IsStatementLineMatchedForPacket),
                UnmatchedLineCount = lines.Count(l => string.Equals(l.MatchStatus, "unmatched", StringComparison.OrdinalIgnoreCase)),
                IgnoredLineCount = lines.Count(l => string.Equals(l.MatchStatus, "ignored", StringComparison.OrdinalIgnoreCase)),
                ProcessedAt = processedAt
            };
        }

        private static string DetermineStatementImportStatus(IReadOnlyCollection<TrustStatementLine> lines)
        {
            if (lines.Count == 0)
            {
                return "imported";
            }

            return lines.All(IsStatementLineResolvedForPacket) ? "matched" : "needs_review";
        }

        private static bool IsStatementLineMatchedForPacket(TrustStatementLine line)
        {
            return string.Equals(line.MatchStatus, "matched", StringComparison.OrdinalIgnoreCase) &&
                   !string.IsNullOrWhiteSpace(line.MatchedTrustTransactionId);
        }

        private static bool IsStatementLineResolvedForPacket(TrustStatementLine line)
        {
            return IsStatementLineMatchedForPacket(line) ||
                   string.Equals(line.MatchStatus, "ignored", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(line.MatchStatus, "rejected", StringComparison.OrdinalIgnoreCase);
        }

        private static string DetermineReconciliationPacketStatus(
            bool hasStatementImport,
            int unmatchedStatementLineCount,
            bool bankVsJournalMismatch,
            bool journalVsLedgerMismatch)
        {
            if (!hasStatementImport)
            {
                return "draft";
            }

            if (unmatchedStatementLineCount > 0)
            {
                return "matching_in_progress";
            }

            if (bankVsJournalMismatch || journalVsLedgerMismatch)
            {
                return "needs_review";
            }

            return "ready_for_signoff";
        }

        private static bool IsPacketTerminal(string status)
        {
            return status.Equals("signed_off", StringComparison.OrdinalIgnoreCase) ||
                   status.Equals("reviewer_signed", StringComparison.OrdinalIgnoreCase) ||
                   status.Equals("lawyer_signed", StringComparison.OrdinalIgnoreCase) ||
                   status.Equals("closed", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPacketEligibleForMonthClose(string status)
        {
            return status.Equals("ready_for_signoff", StringComparison.OrdinalIgnoreCase) ||
                   status.Equals("signed_off", StringComparison.OrdinalIgnoreCase) ||
                   status.Equals("reviewer_signed", StringComparison.OrdinalIgnoreCase) ||
                   status.Equals("lawyer_signed", StringComparison.OrdinalIgnoreCase) ||
                   status.Equals("closed", StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildStatementCandidateMapKey(string matchKey, decimal amount)
        {
            return $"{matchKey}|{NormalizeMoney(amount):0.00}";
        }

        private sealed class TrustStatementMatchCandidate
        {
            public string Id { get; set; } = string.Empty;
            public decimal Amount { get; set; }
            public string? MatchKey { get; set; }
            public DateTime OccurredAt { get; set; }
            public string TransactionType { get; set; } = string.Empty;
            public string Strategy { get; set; } = "reference_amount";
            public decimal Confidence { get; set; } = 1.00m;
        }

        private static decimal ComputeBankAdjustmentDelta(TrustOutstandingItem item)
        {
            return item.ImpactDirection switch
            {
                "increase_bank" => NormalizeMoney(item.Amount),
                "decrease_bank" => NormalizeMoney(-item.Amount),
                _ => 0m
            };
        }

        private static string NormalizeOutstandingItemType(string? value)
        {
            var normalized = (value ?? "other_adjustment").Trim().ToLowerInvariant();
            return normalized switch
            {
                "deposit_in_transit" => "deposit_in_transit",
                "outstanding_check" => "outstanding_check",
                "other_adjustment" => "other_adjustment",
                "bank_fee" => "bank_fee",
                "exception" => "exception",
                _ => "other_adjustment"
            };
        }

        private static string NormalizeOutstandingImpactDirection(string? value)
        {
            var normalized = (value ?? "decrease_bank").Trim().ToLowerInvariant();
            return normalized switch
            {
                "increase_bank" => "increase_bank",
                "decrease_bank" => "decrease_bank",
                _ => "decrease_bank"
            };
        }

        private static string? NormalizeOutstandingReasonCode(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var normalized = new string(value.Trim().ToLowerInvariant()
                .Where(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-')
                .ToArray());

            return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
        }

        private static DateTime NormalizeUtcDate(DateTime value)
        {
            return DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);
        }

        private static DateTime ToInclusivePeriodCutoff(DateTime periodEndDate)
        {
            return NormalizeUtcDate(periodEndDate).AddDays(1).AddTicks(-1);
        }

        private static DateTime EnsureUtc(DateTime value)
        {
            return value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Local => value.ToUniversalTime(),
                _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
            };
        }

        private static string? NullIfWhiteSpace(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static string? NormalizeMatchKey(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var chars = value.Trim().Where(char.IsLetterOrDigit).ToArray();
            return chars.Length == 0 ? null : new string(chars).ToUpperInvariant();
        }

        private static string ComputeFingerprint<T>(T value)
        {
            var json = JsonSerializer.Serialize(value);
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        }

        private async Task EnsureBillingPeriodUnlockedAsync(string operationType, string errorMessage, CancellationToken ct)
        {
            if (!await _billingPeriodLockService.IsLockedAsync(DateTime.UtcNow, ct))
            {
                return;
            }

            await _trustRiskRadarService.RecordPeriodLockAttemptAsync(DateTime.UtcNow, operationType, ct);
            throw new TrustCommandException(StatusCodes.Status400BadRequest, errorMessage);
        }

        private ClaimsPrincipal GetCurrentUser()
        {
            return GetCurrentHttpContext().User;
        }

        private string? GetCurrentUserId()
        {
            var user = GetCurrentUser();
            return user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.FindFirst("sub")?.Value;
        }

        private string RequireCurrentUserId()
        {
            var userId = GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                throw new TrustCommandException(StatusCodes.Status401Unauthorized, "Authenticated user id is required.");
            }

            return userId;
        }

        private HttpContext GetCurrentHttpContext()
        {
            return _httpContextAccessor.HttpContext
                ?? throw new TrustCommandException(StatusCodes.Status500InternalServerError, "Request context is unavailable.");
        }

        private async Task LogAsync(string action, string? entity, string? entityId, string? details)
        {
            await _auditLogger.LogAsync(GetCurrentHttpContext(), action, entity, entityId, details);
        }

        private async Task SaveChangesWithConcurrencyHandlingAsync(CancellationToken ct)
        {
            try
            {
                await _context.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new TrustCommandException(StatusCodes.Status409Conflict, "Trust record was modified by another request. Reload and try again.");
            }
        }

        private static string? NormalizeIdempotencyKey(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var normalized = value.Trim();
            if (normalized.Length <= MaxTrustIdempotencyKeyLength)
            {
                return normalized;
            }

            return normalized[..MaxTrustIdempotencyKeyLength];
        }

        private static List<AllocationDto> ParseAllocations(string? allocationsJson)
        {
            if (string.IsNullOrWhiteSpace(allocationsJson))
            {
                return new List<AllocationDto>();
            }

            try
            {
                return JsonSerializer.Deserialize<List<AllocationDto>>(allocationsJson) ?? new List<AllocationDto>();
            }
            catch
            {
                return new List<AllocationDto>();
            }
        }

        private async Task<List<TrustJournalEntry>> BuildPostedDepositJournalEntriesAsync(
            TrustTransaction tx,
            TrustBankAccount account,
            List<AllocationDto> allocations,
            string currentUserId,
            DateTime effectiveAt,
            CancellationToken ct)
        {
            if (allocations.Count == 0)
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "At least one allocation is required.");
            }

            var ledgerIds = allocations.Select(a => a.LedgerId).Distinct().ToList();
            var ledgers = await _context.ClientTrustLedgers
                .Where(l => ledgerIds.Contains(l.Id))
                .ToDictionaryAsync(l => l.Id, ct);
            if (ledgers.Count != ledgerIds.Count)
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "One or more client ledgers were not found.");
            }

            if (ledgers.Values.Any(l => l.TrustAccountId != account.Id))
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Ledger does not belong to the selected trust account.");
            }

            if (ledgers.Values.Any(l => l.Status != LedgerStatus.ACTIVE))
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "One or more ledgers are not active.");
            }

            var availabilityClass = ResolveInitialDepositAvailabilityClass(tx);
            return allocations
                .Select((allocation, index) => new TrustJournalEntry
                {
                    Id = Guid.NewGuid().ToString(),
                    TrustTransactionId = tx.Id,
                    PostingBatchId = string.Empty,
                    TrustAccountId = tx.TrustAccountId,
                    ClientTrustLedgerId = allocation.LedgerId,
                    MatterId = tx.MatterId,
                    EntryKind = "posting",
                    OperationType = "deposit",
                    Amount = NormalizeMoney(allocation.Amount),
                    Currency = "USD",
                    AvailabilityClass = availabilityClass,
                    CorrelationKey = $"trust-journal:{tx.Id}:posting:{index}",
                    Description = allocation.Description ?? tx.Description,
                    CreatedBy = currentUserId,
                    EffectiveAt = effectiveAt,
                    CreatedAt = DateTime.UtcNow,
                    MetadataJson = JsonSerializer.Serialize(new
                    {
                        trustTransactionId = tx.Id,
                        availabilityClass,
                        allocationLedgerId = allocation.LedgerId
                    })
                })
                .ToList();
        }

        private static List<TrustJournalEntry> BuildPostedWithdrawalJournalEntries(
            TrustTransaction tx,
            ClientTrustLedger withdrawalLedger,
            string currentUserId,
            DateTime effectiveAt)
        {
            return
            [
                new TrustJournalEntry
                {
                    Id = Guid.NewGuid().ToString(),
                    TrustTransactionId = tx.Id,
                    PostingBatchId = string.Empty,
                    TrustAccountId = tx.TrustAccountId,
                    ClientTrustLedgerId = withdrawalLedger.Id,
                    MatterId = tx.MatterId,
                    EntryKind = "posting",
                    OperationType = "withdrawal",
                    Amount = -NormalizeMoney(tx.Amount),
                    Currency = "USD",
                    AvailabilityClass = "cleared",
                    CorrelationKey = $"trust-journal:{tx.Id}:posting:0",
                    Description = tx.Description,
                    CreatedBy = currentUserId,
                    EffectiveAt = effectiveAt,
                    CreatedAt = DateTime.UtcNow
                }
            ];
        }

        private static List<TrustJournalEntry> BuildPostedEarnedFeeTransferJournalEntries(
            TrustTransaction tx,
            ClientTrustLedger ledger,
            string currentUserId,
            DateTime effectiveAt,
            TrustEarnedFeeTransferCommand command)
        {
            return
            [
                new TrustJournalEntry
                {
                    Id = Guid.NewGuid().ToString(),
                    TrustTransactionId = tx.Id,
                    PostingBatchId = string.Empty,
                    TrustAccountId = tx.TrustAccountId,
                    ClientTrustLedgerId = ledger.Id,
                    MatterId = tx.MatterId,
                    EntryKind = "posting",
                    OperationType = "earned_fee_transfer",
                    Amount = -NormalizeMoney(tx.Amount),
                    Currency = "USD",
                    AvailabilityClass = "cleared",
                    CorrelationKey = $"trust-journal:{tx.Id}:posting:0",
                    Description = tx.Description,
                    CreatedBy = currentUserId,
                    EffectiveAt = effectiveAt,
                    CreatedAt = DateTime.UtcNow,
                    MetadataJson = JsonSerializer.Serialize(new
                    {
                        billingPaymentAllocationId = command.BillingPaymentAllocationId,
                        paymentTransactionId = command.PaymentTransactionId,
                        invoiceId = command.InvoiceId,
                        reference = command.Reference,
                        source = "billing_allocation"
                    })
                }
            ];
        }

        private static List<TrustJournalEntry> BuildClearanceJournalEntries(
            TrustTransaction tx,
            IReadOnlyList<TrustJournalEntry> unclearedEntries,
            string currentUserId,
            DateTime effectiveAt,
            string? notes)
        {
            var entries = new List<TrustJournalEntry>(unclearedEntries.Count * 2);
            for (var i = 0; i < unclearedEntries.Count; i++)
            {
                var source = unclearedEntries[i];
                entries.Add(new TrustJournalEntry
                {
                    Id = Guid.NewGuid().ToString(),
                    TrustTransactionId = tx.Id,
                    PostingBatchId = string.Empty,
                    TrustAccountId = source.TrustAccountId,
                    ClientTrustLedgerId = source.ClientTrustLedgerId,
                    MatterId = source.MatterId,
                    EntryKind = "clearance",
                    OperationType = "clearance",
                    Amount = -NormalizeMoney(source.Amount),
                    Currency = source.Currency,
                    AvailabilityClass = "uncleared",
                    CorrelationKey = $"trust-journal:{tx.Id}:clearance:{i}:uncleared",
                    Description = string.IsNullOrWhiteSpace(notes) ? "Deposit cleared." : $"Deposit cleared. {notes}",
                    CreatedBy = currentUserId,
                    EffectiveAt = effectiveAt,
                    CreatedAt = DateTime.UtcNow,
                    MetadataJson = JsonSerializer.Serialize(new { sourceEntryId = source.Id, phase = "uncleared_release" })
                });
                entries.Add(new TrustJournalEntry
                {
                    Id = Guid.NewGuid().ToString(),
                    TrustTransactionId = tx.Id,
                    PostingBatchId = string.Empty,
                    TrustAccountId = source.TrustAccountId,
                    ClientTrustLedgerId = source.ClientTrustLedgerId,
                    MatterId = source.MatterId,
                    EntryKind = "clearance",
                    OperationType = "clearance",
                    Amount = NormalizeMoney(source.Amount),
                    Currency = source.Currency,
                    AvailabilityClass = "cleared",
                    CorrelationKey = $"trust-journal:{tx.Id}:clearance:{i}:cleared",
                    Description = string.IsNullOrWhiteSpace(notes) ? "Deposit cleared." : $"Deposit cleared. {notes}",
                    CreatedBy = currentUserId,
                    EffectiveAt = effectiveAt,
                    CreatedAt = DateTime.UtcNow,
                    MetadataJson = JsonSerializer.Serialize(new { sourceEntryId = source.Id, phase = "cleared_release" })
                });
            }

            return entries;
        }

        private static List<TrustJournalEntry> BuildReversalJournalEntries(
            TrustTransaction tx,
            IReadOnlyList<TrustJournalEntry> entriesToReverse,
            string currentUserId,
            DateTime effectiveAt,
            string? reason,
            string operationType)
        {
            return entriesToReverse
                .Select((entry, index) => new TrustJournalEntry
                {
                    Id = Guid.NewGuid().ToString(),
                    TrustTransactionId = tx.Id,
                    PostingBatchId = string.Empty,
                    TrustAccountId = entry.TrustAccountId,
                    ClientTrustLedgerId = entry.ClientTrustLedgerId,
                    MatterId = entry.MatterId,
                    EntryKind = "reversal",
                    OperationType = operationType,
                    Amount = -NormalizeMoney(entry.Amount),
                    Currency = entry.Currency,
                    AvailabilityClass = entry.AvailabilityClass,
                    ReversalOfTrustJournalEntryId = entry.Id,
                    CorrelationKey = $"trust-journal:{tx.Id}:{operationType}:{index}",
                    Description = string.IsNullOrWhiteSpace(reason)
                        ? $"Reversal of trust journal entry {entry.Id}."
                        : $"Reversal of trust journal entry {entry.Id}. Reason: {reason}",
                    CreatedBy = currentUserId,
                    EffectiveAt = effectiveAt,
                    CreatedAt = DateTime.UtcNow,
                    MetadataJson = JsonSerializer.Serialize(new { originalEntryId = entry.Id, trustTransactionId = tx.Id, operationType })
                })
                .ToList();
        }

        private TrustPostingBatch CreatePostingBatch(
            TrustTransaction tx,
            string trustAccountId,
            string batchType,
            string currentUserId,
            DateTime effectiveAt,
            string? parentPostingBatchId,
            IReadOnlyCollection<TrustJournalEntry> journalEntries)
        {
            return new TrustPostingBatch
            {
                Id = Guid.NewGuid().ToString(),
                TrustTransactionId = tx.Id,
                TrustAccountId = trustAccountId,
                BatchType = batchType,
                ParentPostingBatchId = parentPostingBatchId,
                CreatedBy = currentUserId,
                JournalEntryCount = journalEntries.Count,
                TotalAmount = NormalizeMoney(journalEntries.Sum(j => Math.Abs(j.Amount))),
                EffectiveAt = effectiveAt,
                CreatedAt = DateTime.UtcNow
            };
        }

        private async Task<List<TrustJournalEntry>> LoadOpenJournalEntriesForTransactionAsync(string trustTransactionId, CancellationToken ct)
        {
            var reversedIds = await _context.TrustJournalEntries
                .Where(j => j.TrustTransactionId == trustTransactionId && j.ReversalOfTrustJournalEntryId != null)
                .Select(j => j.ReversalOfTrustJournalEntryId!)
                .ToListAsync(ct);

            return await _context.TrustJournalEntries
                .Where(j =>
                    j.TrustTransactionId == trustTransactionId &&
                    j.ReversalOfTrustJournalEntryId == null &&
                    !reversedIds.Contains(j.Id))
                .OrderBy(j => j.CreatedAt)
                .ToListAsync(ct);
        }

        private async Task ApplyJournalEntriesToProjectionsAsync(
            TrustBankAccount account,
            IReadOnlyList<TrustJournalEntry> entries,
            CancellationToken ct)
        {
            if (entries.Count == 0)
            {
                return;
            }

            var ledgerIds = entries
                .Where(e => !string.IsNullOrWhiteSpace(e.ClientTrustLedgerId))
                .Select(e => e.ClientTrustLedgerId!)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var ledgers = ledgerIds.Count == 0
                ? new Dictionary<string, ClientTrustLedger>(StringComparer.Ordinal)
                : await _context.ClientTrustLedgers
                    .Where(l => ledgerIds.Contains(l.Id))
                    .ToDictionaryAsync(l => l.Id, StringComparer.Ordinal, ct);

            if (ledgers.Count != ledgerIds.Count)
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "One or more trust ledgers could not be loaded for projection update.");
            }

            var accountCurrentDelta = NormalizeMoney(entries.Sum(e => e.Amount));
            var accountClearedDelta = NormalizeMoney(entries.Where(e => e.AvailabilityClass == "cleared").Sum(e => e.Amount));
            var accountUnclearedDelta = NormalizeMoney(entries.Where(e => e.AvailabilityClass == "uncleared").Sum(e => e.Amount));

            EnsureNonNegativeProjection(account.CurrentBalance + accountCurrentDelta, "Trust account current balance would become negative.");
            EnsureNonNegativeProjection(account.ClearedBalance + accountClearedDelta, "Trust account cleared balance would become negative.");
            EnsureNonNegativeProjection(account.UnclearedBalance + accountUnclearedDelta, "Trust account uncleared balance would become negative.");

            foreach (var ledgerGroup in entries.Where(e => !string.IsNullOrWhiteSpace(e.ClientTrustLedgerId)).GroupBy(e => e.ClientTrustLedgerId!, StringComparer.Ordinal))
            {
                var ledger = ledgers[ledgerGroup.Key];
                var currentDelta = NormalizeMoney(ledgerGroup.Sum(e => e.Amount));
                var clearedDelta = NormalizeMoney(ledgerGroup.Where(e => e.AvailabilityClass == "cleared").Sum(e => e.Amount));
                var unclearedDelta = NormalizeMoney(ledgerGroup.Where(e => e.AvailabilityClass == "uncleared").Sum(e => e.Amount));

                EnsureNonNegativeProjection(ledger.RunningBalance + currentDelta, "Client trust ledger balance would become negative.");
                EnsureNonNegativeProjection(ledger.ClearedBalance + clearedDelta, "Client trust ledger cleared balance would become negative.");
                EnsureNonNegativeProjection(ledger.UnclearedBalance + unclearedDelta, "Client trust ledger uncleared balance would become negative.");
            }

            account.CurrentBalance = NormalizeMoney(account.CurrentBalance + accountCurrentDelta);
            account.ClearedBalance = NormalizeMoney(account.ClearedBalance + accountClearedDelta);
            account.UnclearedBalance = NormalizeMoney(account.UnclearedBalance + accountUnclearedDelta);
            account.AvailableDisbursementCapacity = NormalizeMoney(Math.Max(0m, account.ClearedBalance));
            account.UpdatedAt = DateTime.UtcNow;
            account.RowVersion = NewRowVersion();

            foreach (var ledgerGroup in entries.Where(e => !string.IsNullOrWhiteSpace(e.ClientTrustLedgerId)).GroupBy(e => e.ClientTrustLedgerId!, StringComparer.Ordinal))
            {
                var ledger = ledgers[ledgerGroup.Key];
                var currentDelta = NormalizeMoney(ledgerGroup.Sum(e => e.Amount));
                var clearedDelta = NormalizeMoney(ledgerGroup.Where(e => e.AvailabilityClass == "cleared").Sum(e => e.Amount));
                var unclearedDelta = NormalizeMoney(ledgerGroup.Where(e => e.AvailabilityClass == "uncleared").Sum(e => e.Amount));

                ledger.RunningBalance = NormalizeMoney(ledger.RunningBalance + currentDelta);
                ledger.ClearedBalance = NormalizeMoney(ledger.ClearedBalance + clearedDelta);
                ledger.UnclearedBalance = NormalizeMoney(ledger.UnclearedBalance + unclearedDelta);
                ledger.AvailableToDisburse = NormalizeMoney(Math.Max(0m, ledger.ClearedBalance - ledger.HoldAmount));
                ledger.UpdatedAt = DateTime.UtcNow;
                ledger.RowVersion = NewRowVersion();
            }
        }

        private string ResolveInitialDepositAvailabilityClass(TrustTransaction tx)
        {
            if (!_optionsMonitor.CurrentValue.AutoClearDepositsWithoutCheckNumber)
            {
                return "uncleared";
            }

            return string.IsNullOrWhiteSpace(tx.CheckNumber) ? "cleared" : "uncleared";
        }

        private static void EnsureNonNegativeProjection(decimal value, string message)
        {
            if (value < 0m)
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, message);
            }
        }

        private static bool HasProjectionDrift(decimal projected, decimal expected)
        {
            return NormalizeMoney(projected) != NormalizeMoney(expected);
        }

        private async Task<bool> ApplyDepositAsync(TrustTransaction tx, TrustBankAccount account, List<AllocationDto> allocations, CancellationToken ct)
        {
            if (tx.Amount <= 0 || account.Status != TrustAccountStatus.ACTIVE)
            {
                return false;
            }

            var balanceBefore = account.CurrentBalance;
            account.CurrentBalance += tx.Amount;
            account.UpdatedAt = DateTime.UtcNow;
            account.RowVersion = NewRowVersion();

            if (allocations.Count > 0)
            {
                var ledgerIds = allocations.Select(a => a.LedgerId).Distinct().ToList();
                var ledgers = await _context.ClientTrustLedgers
                    .Where(l => ledgerIds.Contains(l.Id))
                    .ToListAsync(ct);
                if (ledgers.Count != ledgerIds.Count) return false;
                if (ledgers.Any(l => l.TrustAccountId != account.Id)) return false;
                if (ledgers.Any(l => l.Status != LedgerStatus.ACTIVE)) return false;

                foreach (var allocation in allocations)
                {
                    if (allocation.Amount <= 0) return false;
                    var ledger = ledgers.First(l => l.Id == allocation.LedgerId);
                    ledger.RunningBalance += allocation.Amount;
                    ledger.UpdatedAt = DateTime.UtcNow;
                    ledger.RowVersion = NewRowVersion();
                }
            }

            tx.BalanceBefore = balanceBefore;
            tx.BalanceAfter = account.CurrentBalance;
            tx.UpdatedAt = DateTime.UtcNow;
            return true;
        }

        private static Task<bool> ApplyWithdrawalAsync(TrustTransaction tx, TrustBankAccount account, ClientTrustLedger ledger)
        {
            if (tx.Amount <= 0 ||
                account.Status != TrustAccountStatus.ACTIVE ||
                ledger.Status != LedgerStatus.ACTIVE ||
                ledger.TrustAccountId != account.Id ||
                ledger.RunningBalance < tx.Amount ||
                account.CurrentBalance < tx.Amount)
            {
                return System.Threading.Tasks.Task.FromResult(false);
            }

            var balanceBefore = account.CurrentBalance;
            account.CurrentBalance -= tx.Amount;
            ledger.RunningBalance -= tx.Amount;
            account.UpdatedAt = DateTime.UtcNow;
            ledger.UpdatedAt = DateTime.UtcNow;
            account.RowVersion = NewRowVersion();
            ledger.RowVersion = NewRowVersion();

            tx.BalanceBefore = balanceBefore;
            tx.BalanceAfter = account.CurrentBalance;
            tx.UpdatedAt = DateTime.UtcNow;
            return System.Threading.Tasks.Task.FromResult(true);
        }

        private async Task<bool> ReverseDepositAsync(TrustTransaction tx, TrustBankAccount account, List<AllocationDto> allocations, CancellationToken ct)
        {
            if (tx.Amount <= 0 || account.CurrentBalance < tx.Amount)
            {
                return false;
            }

            if (allocations.Count > 0)
            {
                var ledgerIds = allocations.Select(a => a.LedgerId).Distinct().ToList();
                var ledgers = await _context.ClientTrustLedgers
                    .Where(l => ledgerIds.Contains(l.Id))
                    .ToListAsync(ct);
                if (ledgers.Count != ledgerIds.Count) return false;
                if (ledgers.Any(l => l.TrustAccountId != account.Id)) return false;

                foreach (var allocation in allocations)
                {
                    var ledger = ledgers.First(l => l.Id == allocation.LedgerId);
                    if (ledger.RunningBalance < allocation.Amount) return false;
                }

                foreach (var allocation in allocations)
                {
                    var ledger = ledgers.First(l => l.Id == allocation.LedgerId);
                    ledger.RunningBalance -= allocation.Amount;
                    ledger.UpdatedAt = DateTime.UtcNow;
                    ledger.RowVersion = NewRowVersion();
                }
            }

            account.CurrentBalance -= tx.Amount;
            account.UpdatedAt = DateTime.UtcNow;
            account.RowVersion = NewRowVersion();
            return true;
        }

        private static Task<bool> ReverseWithdrawalAsync(TrustTransaction tx, TrustBankAccount account, ClientTrustLedger ledger)
        {
            if (tx.Amount <= 0 || ledger.TrustAccountId != account.Id)
            {
                return System.Threading.Tasks.Task.FromResult(false);
            }

            ledger.RunningBalance += tx.Amount;
            account.CurrentBalance += tx.Amount;
            ledger.UpdatedAt = DateTime.UtcNow;
            account.UpdatedAt = DateTime.UtcNow;
            ledger.RowVersion = NewRowVersion();
            account.RowVersion = NewRowVersion();
            return System.Threading.Tasks.Task.FromResult(true);
        }

        private async Task CreateJournalEntriesForApprovedTransactionAsync(TrustTransaction tx, string currentUserId, ClientTrustLedger? withdrawalLedger, CancellationToken ct)
        {
            if (string.Equals(tx.Type, "DEPOSIT", StringComparison.OrdinalIgnoreCase))
            {
                var allocations = ParseAllocations(tx.AllocationsJson);
                foreach (var (allocation, index) in allocations.Select((value, idx) => (value, idx)))
                {
                    _context.TrustJournalEntries.Add(new TrustJournalEntry
                    {
                        Id = Guid.NewGuid().ToString(),
                        TrustTransactionId = tx.Id,
                        TrustAccountId = tx.TrustAccountId,
                        ClientTrustLedgerId = allocation.LedgerId,
                        MatterId = tx.MatterId,
                        EntryKind = "posting",
                        OperationType = "deposit",
                        Amount = NormalizeMoney(allocation.Amount),
                        CorrelationKey = BuildJournalCorrelationKey(tx.Id, index),
                        Description = tx.Description,
                        CreatedBy = currentUserId,
                        EffectiveAt = tx.ApprovedAt ?? DateTime.UtcNow,
                        CreatedAt = DateTime.UtcNow
                    });
                }

                return;
            }

            if (string.Equals(tx.Type, "WITHDRAWAL", StringComparison.OrdinalIgnoreCase))
            {
                _context.TrustJournalEntries.Add(new TrustJournalEntry
                {
                    Id = Guid.NewGuid().ToString(),
                    TrustTransactionId = tx.Id,
                    TrustAccountId = tx.TrustAccountId,
                    ClientTrustLedgerId = withdrawalLedger?.Id ?? tx.LedgerId,
                    MatterId = tx.MatterId,
                    EntryKind = "posting",
                    OperationType = "withdrawal",
                    Amount = -NormalizeMoney(tx.Amount),
                    CorrelationKey = BuildJournalCorrelationKey(tx.Id, 0),
                    Description = tx.Description,
                    CreatedBy = currentUserId,
                    EffectiveAt = tx.ApprovedAt ?? DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow
                });
            }
        }

        private async Task CreateReversalJournalEntriesForVoidAsync(TrustTransaction tx, string currentUserId, CancellationToken ct)
        {
            var existingEntries = await _context.TrustJournalEntries
                .Where(j => j.TrustTransactionId == tx.Id && j.EntryKind == "posting")
                .OrderBy(j => j.CreatedAt)
                .ToListAsync(ct);

            if (existingEntries.Count == 0)
            {
                return;
            }

            foreach (var entry in existingEntries)
            {
                _context.TrustJournalEntries.Add(new TrustJournalEntry
                {
                    Id = Guid.NewGuid().ToString(),
                    TrustTransactionId = tx.Id,
                    TrustAccountId = entry.TrustAccountId,
                    ClientTrustLedgerId = entry.ClientTrustLedgerId,
                    MatterId = entry.MatterId,
                    EntryKind = "reversal",
                    OperationType = entry.OperationType,
                    Amount = -entry.Amount,
                    Currency = entry.Currency,
                    ReversalOfTrustJournalEntryId = entry.Id,
                    CorrelationKey = $"{entry.CorrelationKey}:reversal",
                    Description = string.IsNullOrWhiteSpace(tx.VoidReason)
                        ? $"Reversal of trust journal entry {entry.Id}."
                        : $"Reversal of trust journal entry {entry.Id}. Reason: {tx.VoidReason}",
                    CreatedBy = currentUserId,
                    EffectiveAt = tx.VoidedAt ?? DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow,
                    MetadataJson = JsonSerializer.Serialize(new { originalEntryId = entry.Id, trustTransactionId = tx.Id })
                });
            }
        }

        private async Task EnsureApprovalRequirementsAsync(TrustTransaction tx, TrustBankAccount account, ClientTrustLedger? ledger, CancellationToken ct)
        {
            if (!string.Equals(tx.Type, "WITHDRAWAL", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(tx.Type, "EARNED_FEE_TRANSFER", StringComparison.OrdinalIgnoreCase))
            {
                tx.ApprovalStatus = "not_required";
                return;
            }

            var existing = await _context.TrustApprovalRequirements
                .Where(r => r.TrustTransactionId == tx.Id)
                .AnyAsync(ct);
            if (existing)
            {
                return;
            }

            var resolvedPolicy = await _policyResolver.ResolveEffectivePolicyAsync(account.Id, ct);
            var approvalPlan = BuildApprovalPlan(
                resolvedPolicy,
                tx.Type,
                NormalizeDisbursementClass(tx.DisbursementClass, "client_disbursement"),
                tx.Amount,
                tx.CreatedBy,
                TrustActionAuthorizationService.GetRole(GetCurrentUser()));

            foreach (var requirement in approvalPlan.Requirements)
            {
                _context.TrustApprovalRequirements.Add(new TrustApprovalRequirement
                {
                    Id = Guid.NewGuid().ToString(),
                    TrustTransactionId = tx.Id,
                    RequirementType = requirement.RequirementType,
                    RequiredCount = requirement.RequiredCount,
                    SatisfiedCount = 0,
                    Status = "pending",
                    PolicyKey = resolvedPolicy.Policy.PolicyKey,
                    Summary = requirement.Summary,
                    MetadataJson = JsonSerializer.Serialize(new
                    {
                        tx.Type,
                        tx.DisbursementClass,
                        tx.Amount,
                        trustAccountId = account.Id,
                        ledgerId = ledger?.Id,
                        policyKey = resolvedPolicy.Policy.PolicyKey,
                        jurisdiction = resolvedPolicy.Policy.Jurisdiction
                    }),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            }

            tx.ApprovalStatus = approvalPlan.Requirements.Count == 0 ? "not_required" : "pending";
            tx.PolicyDecisionJson = SerializePolicyDecision(account, resolvedPolicy.Policy, approvalPlan, hasOverride: false);
        }

        private async Task<TrustTransaction> PostApprovedTransactionAsync(TrustTransaction tx, string currentUserId, CancellationToken ct)
        {
            var account = await _context.TrustBankAccounts.FirstOrDefaultAsync(a => a.Id == tx.TrustAccountId, ct);
            if (account == null)
            {
                throw new TrustCommandException(StatusCodes.Status404NotFound, "Trust account not found");
            }

            if (account.Status != TrustAccountStatus.ACTIVE)
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Trust account is not active.");
            }

            var effectiveAt = DateTime.UtcNow;
            List<TrustJournalEntry> journalEntries;
            ClientTrustLedger? withdrawalLedger = null;
            if (string.Equals(tx.Type, "DEPOSIT", StringComparison.OrdinalIgnoreCase))
            {
                var allocations = ParseAllocations(tx.AllocationsJson);
                journalEntries = await BuildPostedDepositJournalEntriesAsync(tx, account, allocations, currentUserId, effectiveAt, ct);
            }
            else if (string.Equals(tx.Type, "WITHDRAWAL", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(tx.LedgerId))
                {
                    throw new TrustCommandException(StatusCodes.Status400BadRequest, "Ledger is required for withdrawals.");
                }

                withdrawalLedger = await _context.ClientTrustLedgers.FirstOrDefaultAsync(l => l.Id == tx.LedgerId, ct);
                if (withdrawalLedger == null)
                {
                    throw new TrustCommandException(StatusCodes.Status400BadRequest, "Client ledger not found");
                }

                if (withdrawalLedger.Status != LedgerStatus.ACTIVE)
                {
                    throw new TrustCommandException(StatusCodes.Status400BadRequest, "Ledger is not active.");
                }

                if (withdrawalLedger.AvailableToDisburse < tx.Amount)
                {
                    throw new TrustCommandException(StatusCodes.Status400BadRequest, "Withdrawal exceeds the client's cleared funds available to disburse.");
                }

                if (account.AvailableDisbursementCapacity < tx.Amount)
                {
                    throw new TrustCommandException(StatusCodes.Status400BadRequest, "Withdrawal exceeds the trust account's cleared funds capacity.");
                }

                await _trustRiskRadarService.EnforceNoActiveHardHoldsAsync(new TrustRiskRadarService.TrustRiskHoldGuardContext
                {
                    OperationType = "trust_withdrawal_approve",
                    TrustTransactionId = tx.Id,
                    MatterId = tx.MatterId,
                    ClientId = withdrawalLedger.ClientId
                }, ct);

                journalEntries = BuildPostedWithdrawalJournalEntries(tx, withdrawalLedger, currentUserId, effectiveAt);
            }
            else if (string.Equals(tx.Type, "EARNED_FEE_TRANSFER", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(tx.LedgerId))
                {
                    throw new TrustCommandException(StatusCodes.Status400BadRequest, "Ledger is required for earned-fee transfer.");
                }

                withdrawalLedger = await _context.ClientTrustLedgers.FirstOrDefaultAsync(l => l.Id == tx.LedgerId, ct);
                if (withdrawalLedger == null)
                {
                    throw new TrustCommandException(StatusCodes.Status400BadRequest, "Client ledger not found");
                }

                if (withdrawalLedger.Status != LedgerStatus.ACTIVE)
                {
                    throw new TrustCommandException(StatusCodes.Status400BadRequest, "Ledger is not active.");
                }

                if (withdrawalLedger.AvailableToDisburse < tx.Amount)
                {
                    throw new TrustCommandException(StatusCodes.Status400BadRequest, "Earned-fee transfer exceeds the client's cleared funds available to disburse.");
                }

                if (account.AvailableDisbursementCapacity < tx.Amount)
                {
                    throw new TrustCommandException(StatusCodes.Status400BadRequest, "Earned-fee transfer exceeds the trust account's cleared funds capacity.");
                }

                await _trustRiskRadarService.EnforceNoActiveHardHoldsAsync(new TrustRiskRadarService.TrustRiskHoldGuardContext
                {
                    OperationType = "earned_fee_transfer_approve",
                    TrustTransactionId = tx.Id,
                    MatterId = tx.MatterId,
                    ClientId = withdrawalLedger.ClientId
                }, ct);

                journalEntries = BuildPostedEarnedFeeTransferJournalEntries(
                    tx,
                    withdrawalLedger,
                    currentUserId,
                    effectiveAt,
                    new TrustEarnedFeeTransferCommand
                    {
                        TrustAccountId = tx.TrustAccountId,
                        LedgerId = withdrawalLedger.Id,
                        MatterId = tx.MatterId,
                        Amount = tx.Amount,
                        Description = tx.Description,
                        PayorPayee = tx.PayorPayee,
                        Reference = tx.Reference,
                        EffectiveAt = effectiveAt
                    });
            }
            else
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Unsupported transaction type.");
            }

            var postingBatch = CreatePostingBatch(tx, account.Id, "posting", currentUserId, effectiveAt, null, journalEntries);
            foreach (var journalEntry in journalEntries)
            {
                journalEntry.PostingBatchId = postingBatch.Id;
            }

            tx.BalanceBefore = account.CurrentBalance;
            tx.BalanceAfter = NormalizeMoney(account.CurrentBalance + journalEntries.Sum(j => j.Amount));
            _context.TrustPostingBatches.Add(postingBatch);
            _context.TrustJournalEntries.AddRange(journalEntries);
            await ApplyJournalEntriesToProjectionsAsync(account, journalEntries, ct);

            tx.Status = "APPROVED";
            tx.ApprovalStatus = "approved";
            tx.ApprovedBy = currentUserId;
            tx.ApprovedAt = effectiveAt;
            tx.UpdatedAt = effectiveAt;
            tx.RowVersion = NewRowVersion();
            tx.PostingBatchId = postingBatch.Id;
            tx.PrimaryJournalEntryId = journalEntries.FirstOrDefault()?.Id;
            if (string.Equals(tx.Type, "DEPOSIT", StringComparison.OrdinalIgnoreCase))
            {
                if (journalEntries.Any(j => string.Equals(j.AvailabilityClass, "uncleared", StringComparison.OrdinalIgnoreCase)))
                {
                    tx.ClearingStatus = "pending_clearance";
                    tx.ClearedAt = null;
                }
                else
                {
                    tx.ClearingStatus = "cleared";
                    tx.ClearedAt = effectiveAt;
                }
            }
            else
            {
                tx.ClearingStatus = "not_applicable";
                tx.ClearedAt = null;
            }

            await SaveChangesWithConcurrencyHandlingAsync(ct);
            await _trustRiskRadarService.RecordTrustTransactionRiskAsync(tx, "trust_transaction_approved", ct);
            await LogAsync("trust.transaction.approve", "TrustTransaction", tx.Id, $"Amount={tx.Amount}, Account={tx.TrustAccountId}");
            return tx;
        }

        private TrustApprovalPlan BuildApprovalPlan(
            TrustResolvedPolicyContext resolvedPolicy,
            string transactionType,
            string disbursementClass,
            decimal amount,
            string? creatorUserId,
            string? creatorRole)
        {
            var requirements = new List<TrustApprovalRequirementSeed>();
            if (!string.Equals(transactionType, "WITHDRAWAL", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(transactionType, "EARNED_FEE_TRANSFER", StringComparison.OrdinalIgnoreCase))
            {
                return new TrustApprovalPlan(transactionType, disbursementClass, requirements);
            }

            var operationalCount = amount >= resolvedPolicy.Policy.DualApprovalThreshold ? 2 : 1;
            requirements.Add(new TrustApprovalRequirementSeed(
                "operational_approval",
                operationalCount,
                operationalCount > 1 ? "Two operational approvers required by policy threshold." : "Operational approver required."));

            if (amount >= resolvedPolicy.Policy.ResponsibleLawyerApprovalThreshold)
            {
                requirements.Add(new TrustApprovalRequirementSeed(
                    "responsible_lawyer",
                    1,
                    "Responsible lawyer approval required by policy threshold."));
            }

            if (amount >= resolvedPolicy.Policy.SignatoryApprovalThreshold ||
                resolvedPolicy.SignatoryRequiredClasses.Contains(disbursementClass, StringComparer.OrdinalIgnoreCase))
            {
                requirements.Add(new TrustApprovalRequirementSeed(
                    "signatory",
                    1,
                    "Approved signatory required for this disbursement."));
            }

            return new TrustApprovalPlan(transactionType, disbursementClass, requirements);
        }

        private bool CanCurrentActorSatisfyRequirement(
            TrustApprovalRequirement requirement,
            TrustTransaction tx,
            TrustResolvedPolicyContext resolvedPolicy,
            string currentUserId,
            string? currentUserRole)
        {
            if (!string.IsNullOrWhiteSpace(tx.CreatedBy) &&
                string.Equals(tx.CreatedBy, currentUserId, StringComparison.Ordinal) &&
                resolvedPolicy.Policy.RequireMakerChecker)
            {
                return false;
            }

            return requirement.RequirementType switch
            {
                "operational_approval" =>
                    resolvedPolicy.OperationalApproverRoles.Contains(currentUserRole ?? string.Empty, StringComparer.OrdinalIgnoreCase) ||
                    _authorization.IsAllowed(TrustActionKeys.ApproveTransaction, currentUserRole),
                "signatory" => resolvedPolicy.IsSignatory(currentUserId),
                "responsible_lawyer" => resolvedPolicy.IsResponsibleLawyer(currentUserId),
                _ => false
            };
        }

        private static void RecomputeRequirementStatuses(
            IReadOnlyList<TrustApprovalRequirement> requirements,
            IReadOnlyList<TrustApprovalDecision> decisions)
        {
            foreach (var requirement in requirements)
            {
                if (requirement.Status == "overridden")
                {
                    requirement.SatisfiedCount = requirement.RequiredCount;
                    continue;
                }

                var satisfiedCount = decisions
                    .Where(d => d.TrustApprovalRequirementId == requirement.Id && d.DecisionType == "approve")
                    .Select(d => d.ActorUserId)
                    .Distinct(StringComparer.Ordinal)
                    .Count();
                requirement.SatisfiedCount = Math.Min(requirement.RequiredCount, satisfiedCount);
                requirement.Status = requirement.SatisfiedCount >= requirement.RequiredCount ? "approved" : "pending";
                requirement.UpdatedAt = DateTime.UtcNow;
            }
        }

        private static TrustTransactionApprovalStateDto BuildApprovalStateDto(
            TrustTransaction tx,
            IReadOnlyList<TrustApprovalRequirement> requirements,
            IReadOnlyList<TrustApprovalDecision> decisions,
            bool hasOverride)
        {
            return new TrustTransactionApprovalStateDto
            {
                TrustTransactionId = tx.Id,
                TransactionStatus = tx.Status,
                ApprovalStatus = tx.ApprovalStatus ?? (requirements.Count == 0 ? "not_required" : "pending"),
                IsReadyToPost = requirements.All(r => r.Status != "pending"),
                HasOverride = hasOverride,
                Requirements = requirements.Select(r => new TrustApprovalRequirementDto
                {
                    Id = r.Id,
                    TrustTransactionId = r.TrustTransactionId,
                    RequirementType = r.RequirementType,
                    RequiredCount = r.RequiredCount,
                    SatisfiedCount = r.SatisfiedCount,
                    Status = r.Status,
                    Summary = r.Summary
                }).ToList(),
                Decisions = decisions.Select(d => new TrustApprovalDecisionDto
                {
                    Id = d.Id,
                    RequirementId = d.TrustApprovalRequirementId,
                    ActorUserId = d.ActorUserId,
                    ActorRole = d.ActorRole,
                    DecisionType = d.DecisionType,
                    Notes = d.Notes,
                    Reason = d.Reason,
                    CreatedAt = d.CreatedAt
                }).ToList()
            };
        }

        private static string SerializePolicyDecision(
            TrustBankAccount account,
            TrustJurisdictionPolicy policy,
            TrustApprovalPlan approvalPlan,
            bool hasOverride)
        {
            return JsonSerializer.Serialize(new
            {
                accountId = account.Id,
                accountType = account.AccountType,
                jurisdiction = policy.Jurisdiction,
                policyKey = policy.PolicyKey,
                approvalPlan.TransactionType,
                approvalPlan.DisbursementClass,
                hasOverride,
                requirements = approvalPlan.Requirements.Select(r => new
                {
                    r.RequirementType,
                    r.RequiredCount,
                    r.Summary
                }).ToList()
            });
        }

        private static string SerializePolicyDecision(
            string trustAccountId,
            TrustJurisdictionPolicy policy,
            IReadOnlyList<TrustApprovalRequirement> requirements,
            IReadOnlyList<TrustApprovalDecision> decisions,
            bool hasOverride)
        {
            return JsonSerializer.Serialize(new
            {
                accountId = trustAccountId,
                jurisdiction = policy.Jurisdiction,
                policyKey = policy.PolicyKey,
                hasOverride,
                requirements = requirements.Select(r => new
                {
                    r.RequirementType,
                    r.RequiredCount,
                    r.SatisfiedCount,
                    r.Status,
                    r.Summary
                }).ToList(),
                decisions = decisions.Select(d => new
                {
                    d.TrustApprovalRequirementId,
                    d.ActorUserId,
                    d.ActorRole,
                    d.DecisionType,
                    d.CreatedAt
                }).ToList()
            });
        }

        private async Task<TrustMonthCloseTemplateStatus> BuildMonthCloseTemplateStatusAsync(
            TrustMonthClose close,
            TrustReconciliationPacket packet,
            TrustResolvedPolicyContext resolvedPolicy,
            TrustJurisdictionPacketTemplate template,
            CancellationToken ct)
        {
            var requiredSections = TrustPolicyResolverService.ParseList(template.RequiredSectionsJson);
            var missingSections = requiredSections
                .Where(section => !IsMonthCloseSectionAvailable(section, resolvedPolicy.Account, packet))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var disclosureBlocks = TrustPolicyResolverService.ParseList(template.DisclosureBlocksJson);
            var requiredAttestations = TrustPolicyResolverService.ParseAttestations(template.RequiredAttestationsJson);
            var completedAttestations = await _context.TrustMonthCloseAttestations.AsNoTracking()
                .Where(a => a.TrustMonthCloseId == close.Id)
                .OrderBy(a => a.Role)
                .ThenBy(a => a.AttestationKey)
                .Select(a => new TrustMonthCloseAttestationDto
                {
                    Key = a.AttestationKey,
                    Label = a.Label,
                    Role = a.Role,
                    Accepted = a.Accepted,
                    Notes = a.Notes,
                    SignedBy = a.SignedBy,
                    SignedAt = a.SignedAt
                })
                .ToListAsync(ct);

            return new TrustMonthCloseTemplateStatus(template, missingSections, disclosureBlocks, requiredAttestations, completedAttestations);
        }

        private async Task PersistMonthCloseAttestationsAsync(
            string trustMonthCloseId,
            string role,
            string actorUserId,
            IReadOnlyCollection<TrustMonthCloseAttestationDto>? requestedAttestations,
            IReadOnlyCollection<TrustPacketTemplateAttestationDto> templateAttestations,
            CancellationToken ct)
        {
            var existing = await _context.TrustMonthCloseAttestations
                .Where(a => a.TrustMonthCloseId == trustMonthCloseId && a.Role == role)
                .ToListAsync(ct);

            if (existing.Count > 0)
            {
                _context.TrustMonthCloseAttestations.RemoveRange(existing);
            }

            var acceptedLookup = (requestedAttestations ?? [])
                .Where(a => !string.IsNullOrWhiteSpace(a.Key))
                .GroupBy(a => a.Key.Trim().ToLowerInvariant(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            var now = DateTime.UtcNow;

            foreach (var templateAttestation in templateAttestations)
            {
                acceptedLookup.TryGetValue(templateAttestation.Key, out var requested);
                var attestation = new TrustMonthCloseAttestation
                {
                    Id = Guid.NewGuid().ToString(),
                    TrustMonthCloseId = trustMonthCloseId,
                    Role = role,
                    AttestationKey = templateAttestation.Key,
                    Label = templateAttestation.Label,
                    Accepted = requested?.Accepted == true,
                    Notes = requested?.Notes?.Trim(),
                    SignedBy = actorUserId,
                    SignedAt = now,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                _context.TrustMonthCloseAttestations.Add(attestation);
            }
        }

        private string BuildMonthCloseSummaryJson(
            TrustMonthClose close,
            TrustReconciliationPacket packet,
            int openExceptions,
            IReadOnlyCollection<TrustReconciliationSignoff> signoffs,
            TrustResolvedPolicyContext resolvedPolicy,
            TrustMonthCloseTemplateStatus templateStatus)
        {
            return JsonSerializer.Serialize(new
            {
                packetId = packet.Id,
                packetStatus = packet.Status,
                packetExceptionCount = packet.ExceptionCount,
                openExceptionCount = openExceptions,
                signoffCount = signoffs.Count,
                jurisdiction = resolvedPolicy.Policy.Jurisdiction,
                packetTemplate = new
                {
                    templateKey = templateStatus.Template.TemplateKey,
                    name = templateStatus.Template.Name,
                    versionNumber = templateStatus.Template.VersionNumber,
                    missingRequiredSections = templateStatus.MissingRequiredSections,
                    disclosureBlocks = templateStatus.DisclosureBlocks,
                    requiredAttestations = templateStatus.RequiredAttestations,
                    completedAttestations = templateStatus.CompletedAttestations
                }
            });
        }

        private static bool IsMonthCloseSectionAvailable(string section, TrustBankAccount account, TrustReconciliationPacket packet)
        {
            return section switch
            {
                "statement_summary" => !string.IsNullOrWhiteSpace(packet.StatementImportId),
                "responsible_lawyer_block" => !string.IsNullOrWhiteSpace(account.ResponsibleLawyerUserId),
                "entity_disclosure" => !string.IsNullOrWhiteSpace(account.EntityId),
                "office_disclosure" => !string.IsNullOrWhiteSpace(account.OfficeId),
                "three_way_summary" or "outstanding_schedule" or "exception_register" or "signoff_chain" => true,
                _ => true
            };
        }

        private static string NormalizeMonthCloseRole(string? role)
        {
            var normalized = (role ?? string.Empty).Trim().ToLowerInvariant();
            return normalized switch
            {
                "reviewer" => "reviewer",
                "responsible_lawyer" => "responsible_lawyer",
                _ => normalized
            };
        }

        private static TrustMonthCloseTemplateSummary? TryReadMonthCloseTemplateSummary(string? summaryJson)
        {
            if (string.IsNullOrWhiteSpace(summaryJson))
            {
                return null;
            }

            try
            {
                using var document = JsonDocument.Parse(summaryJson);
                if (!document.RootElement.TryGetProperty("packetTemplate", out var templateElement))
                {
                    return null;
                }

                var requiredAttestations = templateElement.TryGetProperty("requiredAttestations", out var requiredElement)
                    ? JsonSerializer.Deserialize<List<TrustPacketTemplateAttestationDto>>(requiredElement.GetRawText()) ?? new List<TrustPacketTemplateAttestationDto>()
                    : new List<TrustPacketTemplateAttestationDto>();
                var completedAttestations = templateElement.TryGetProperty("completedAttestations", out var completedElement)
                    ? JsonSerializer.Deserialize<List<TrustMonthCloseAttestationDto>>(completedElement.GetRawText()) ?? new List<TrustMonthCloseAttestationDto>()
                    : new List<TrustMonthCloseAttestationDto>();
                var missingRequiredSections = templateElement.TryGetProperty("missingRequiredSections", out var missingElement)
                    ? JsonSerializer.Deserialize<List<string>>(missingElement.GetRawText()) ?? new List<string>()
                    : new List<string>();
                var disclosureBlocks = templateElement.TryGetProperty("disclosureBlocks", out var disclosureElement)
                    ? JsonSerializer.Deserialize<List<string>>(disclosureElement.GetRawText()) ?? new List<string>()
                    : new List<string>();

                return new TrustMonthCloseTemplateSummary(
                    templateElement.TryGetProperty("templateKey", out var keyElement) ? keyElement.GetString() : null,
                    templateElement.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null,
                    templateElement.TryGetProperty("versionNumber", out var versionElement) && versionElement.TryGetInt32(out var versionNumber) ? versionNumber : null,
                    missingRequiredSections,
                    disclosureBlocks,
                    requiredAttestations,
                    completedAttestations);
            }
            catch
            {
                return null;
            }
        }

        private async Task<List<TrustMonthCloseStep>> EnsureMonthCloseStepsAsync(
            TrustMonthClose close,
            TrustReconciliationPacket packet,
            int openExceptions,
            IReadOnlyList<TrustReconciliationSignoff> packetSignoffs,
            TrustResolvedPolicyContext resolvedPolicy,
            TrustMonthCloseTemplateStatus templateStatus,
            CancellationToken ct)
        {
            var now = DateTime.UtcNow;
            var steps = await _context.TrustMonthCloseSteps
                .Where(s => s.TrustMonthCloseId == close.Id)
                .ToListAsync(ct);
            var map = steps.ToDictionary(s => s.StepKey, StringComparer.OrdinalIgnoreCase);

            TrustMonthCloseStep Ensure(string key)
            {
                if (map.TryGetValue(key, out var existing))
                {
                    return existing;
                }

                var step = new TrustMonthCloseStep
                {
                    Id = Guid.NewGuid().ToString(),
                    TrustMonthCloseId = close.Id,
                    StepKey = key,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                _context.TrustMonthCloseSteps.Add(step);
                steps.Add(step);
                map[key] = step;
                return step;
            }

            var statementStep = Ensure("statement_import");
            statementStep.Status = !string.IsNullOrWhiteSpace(packet.StatementImportId) ? "completed" : "blocked";
            statementStep.Notes = !string.IsNullOrWhiteSpace(packet.StatementImportId) ? "Statement import attached." : "Statement import is missing.";
            if (statementStep.Status == "completed" && string.IsNullOrWhiteSpace(statementStep.CompletedBy))
            {
                statementStep.CompletedBy = close.PreparedBy;
                statementStep.CompletedAt = close.PreparedAt;
            }
            statementStep.UpdatedAt = now;

            var templateStep = Ensure("jurisdiction_packet_template");
            templateStep.Status = templateStatus.MissingRequiredSections.Count == 0 ? "completed" : "blocked";
            templateStep.Notes = templateStatus.MissingRequiredSections.Count == 0
                ? $"Packet template {templateStatus.Template.TemplateKey} validated."
                : $"Missing required sections: {string.Join(", ", templateStatus.MissingRequiredSections)}.";
            if (templateStep.Status == "completed" && string.IsNullOrWhiteSpace(templateStep.CompletedBy))
            {
                templateStep.CompletedBy = close.PreparedBy;
                templateStep.CompletedAt = close.PreparedAt;
            }
            templateStep.UpdatedAt = now;

            var packetStep = Ensure("reconciliation_packet");
            var packetReady = packet.IsCanonical &&
                              packet.Status is "ready_for_signoff" or "signed_off" or "reviewer_signed" or "lawyer_signed" or "closed";
            packetStep.Status = packetReady ? "completed" : "blocked";
            packetStep.Notes = packetReady
                ? $"Canonical packet ready with status {packet.Status}."
                : $"Canonical packet is not ready. Current status: {packet.Status}.";
            if (packetReady)
            {
                packetStep.CompletedBy ??= close.PreparedBy;
                packetStep.CompletedAt ??= close.PreparedAt;
            }
            packetStep.UpdatedAt = now;

            var exceptionStep = Ensure("exception_review");
            exceptionStep.Status = openExceptions == 0 ? "completed" : "blocked";
            exceptionStep.Notes = openExceptions == 0 ? "No open exceptions remain." : $"{openExceptions} open exceptions must be resolved.";
            if (exceptionStep.Status == "completed" && string.IsNullOrWhiteSpace(exceptionStep.CompletedBy))
            {
                exceptionStep.CompletedBy = close.PreparedBy;
                exceptionStep.CompletedAt = close.PreparedAt;
            }
            exceptionStep.UpdatedAt = now;

            var reviewerStep = Ensure("reviewer_signoff");
            reviewerStep.Status = close.ReviewerSignedAt.HasValue ? "completed" : ArePrerequisiteStepsComplete(steps, "reviewer_signoff") ? "ready" : "blocked";
            reviewerStep.Notes = close.ReviewerSignedAt.HasValue ? "Reviewer signoff completed." : "Reviewer signoff pending.";
            reviewerStep.CompletedBy = close.ReviewerSignedBy;
            reviewerStep.CompletedAt = close.ReviewerSignedAt;
            reviewerStep.UpdatedAt = now;

            var responsibleStep = Ensure("responsible_lawyer_signoff");
            responsibleStep.Status = close.ResponsibleLawyerSignedAt.HasValue ? "completed" : close.ReviewerSignedAt.HasValue ? "ready" : "blocked";
            responsibleStep.Notes = close.ResponsibleLawyerSignedAt.HasValue
                ? "Responsible lawyer signoff completed."
                : $"Responsible lawyer signoff pending for {resolvedPolicy.Account.ResponsibleLawyerUserId ?? "unassigned"}";
            responsibleStep.CompletedBy = close.ResponsibleLawyerSignedBy;
            responsibleStep.CompletedAt = close.ResponsibleLawyerSignedAt;
            responsibleStep.UpdatedAt = now;

            return steps.OrderBy(s => s.StepKey, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static bool ArePrerequisiteStepsComplete(IReadOnlyCollection<TrustMonthCloseStep> steps, string targetKey)
        {
            return steps
                .Where(s => !string.Equals(s.StepKey, targetKey, StringComparison.OrdinalIgnoreCase))
                .Where(s => s.StepKey is "statement_import" or "jurisdiction_packet_template" or "reconciliation_packet" or "exception_review")
                .All(s => s.Status == "completed");
        }

        private static void CompleteMonthCloseStep(IReadOnlyCollection<TrustMonthCloseStep> steps, string stepKey, string actorUserId, string? notes)
        {
            var step = steps.FirstOrDefault(s => string.Equals(s.StepKey, stepKey, StringComparison.OrdinalIgnoreCase));
            if (step == null)
            {
                return;
            }

            step.Status = "completed";
            step.CompletedBy = actorUserId;
            step.CompletedAt = DateTime.UtcNow;
            step.Notes = notes?.Trim();
            step.UpdatedAt = DateTime.UtcNow;
        }

        private static string DetermineMonthCloseStatus(TrustMonthClose close, IReadOnlyCollection<TrustMonthCloseStep> steps)
        {
            if (!close.IsCanonical && string.Equals(close.Status, "superseded", StringComparison.OrdinalIgnoreCase))
            {
                return "superseded";
            }

            if (close.ReviewerSignedAt.HasValue && close.ResponsibleLawyerSignedAt.HasValue)
            {
                return "closed";
            }

            if (close.ReviewerSignedAt.HasValue || close.ResponsibleLawyerSignedAt.HasValue)
            {
                return "partially_signed";
            }

            if (steps.All(s => s.Status is "completed" or "ready"))
            {
                return "ready_for_signoff";
            }

            return "in_progress";
        }

        private static TrustMonthCloseDto BuildMonthCloseDto(TrustMonthClose close, IReadOnlyCollection<TrustMonthCloseStep> steps)
        {
            var templateSummary = TryReadMonthCloseTemplateSummary(close.SummaryJson);
            return new TrustMonthCloseDto
            {
                Id = close.Id,
                TrustAccountId = close.TrustAccountId,
                PolicyKey = close.PolicyKey,
                PeriodStart = close.PeriodStart,
                PeriodEnd = close.PeriodEnd,
                ReconciliationPacketId = close.ReconciliationPacketId,
                VersionNumber = close.VersionNumber,
                IsCanonical = close.IsCanonical,
                Status = close.Status,
                OpenExceptionCount = close.OpenExceptionCount,
                PreparedBy = close.PreparedBy,
                PreparedAt = close.PreparedAt,
                ReviewerSignedBy = close.ReviewerSignedBy,
                ReviewerSignedAt = close.ReviewerSignedAt,
                ResponsibleLawyerSignedBy = close.ResponsibleLawyerSignedBy,
                ResponsibleLawyerSignedAt = close.ResponsibleLawyerSignedAt,
                ReopenedFromMonthCloseId = close.ReopenedFromMonthCloseId,
                SupersededByMonthCloseId = close.SupersededByMonthCloseId,
                ReopenedBy = close.ReopenedBy,
                ReopenedAt = close.ReopenedAt,
                ReopenReason = close.ReopenReason,
                SupersededBy = close.SupersededBy,
                SupersededAt = close.SupersededAt,
                SupersedeReason = close.SupersedeReason,
                PacketTemplateKey = templateSummary?.TemplateKey,
                PacketTemplateName = templateSummary?.Name,
                PacketTemplateVersionNumber = templateSummary?.VersionNumber,
                MissingRequiredSections = templateSummary?.MissingRequiredSections ?? new List<string>(),
                DisclosureBlocks = templateSummary?.DisclosureBlocks ?? new List<string>(),
                RequiredAttestations = templateSummary?.RequiredAttestations ?? new List<TrustPacketTemplateAttestationDto>(),
                CompletedAttestations = templateSummary?.CompletedAttestations ?? new List<TrustMonthCloseAttestationDto>(),
                Steps = steps.Select(s => new TrustMonthCloseStepDto
                {
                    StepKey = s.StepKey,
                    Status = s.Status,
                    Notes = s.Notes,
                    CompletedBy = s.CompletedBy,
                    CompletedAt = s.CompletedAt
                }).OrderBy(s => s.StepKey, StringComparer.OrdinalIgnoreCase).ToList()
            };
        }

        private static string? TryReadPolicySummary(string? policyDecisionJson)
        {
            if (string.IsNullOrWhiteSpace(policyDecisionJson))
            {
                return null;
            }

            try
            {
                using var document = JsonDocument.Parse(policyDecisionJson);
                if (document.RootElement.TryGetProperty("requirements", out var requirements) &&
                    requirements.ValueKind == JsonValueKind.Array)
                {
                    var summaries = requirements.EnumerateArray()
                        .Select(r => r.TryGetProperty("summary", out var summary) && summary.ValueKind == JsonValueKind.String
                            ? summary.GetString()
                            : null)
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .ToList();
                    return summaries.Count == 0 ? null : string.Join(" ", summaries);
                }
            }
            catch
            {
            }

            return null;
        }

        private static string NormalizeDisbursementClass(string? value, string fallback)
        {
            var normalized = (value ?? fallback).Trim().ToLowerInvariant();
            return normalized switch
            {
                "client_disbursement" or "settlement_payout" or "third_party_payment" or "earned_fee_transfer" or "cost_reimbursement" or "refund" => normalized,
                _ => fallback
            };
        }

        private static string NormalizeAccountType(string? value)
        {
            var normalized = (value ?? "iolta").Trim().ToLowerInvariant();
            return normalized is "iolta" or "non_iolta" ? normalized : "iolta";
        }

        private static string NormalizeStatementCadence(string? value)
        {
            var normalized = (value ?? "monthly").Trim().ToLowerInvariant();
            return normalized is "daily" or "weekly" or "monthly" or "quarterly" ? normalized : "monthly";
        }

        private static List<string> NormalizeStringList(IEnumerable<string>? values, bool normalizeLower)
        {
            return (values ?? [])
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => normalizeLower ? x.Trim().ToLowerInvariant() : x.Trim())
                .Distinct(normalizeLower ? StringComparer.OrdinalIgnoreCase : StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private async Task<TrustStatementImport?> FindDuplicateStatementImportAsync(
            string trustAccountId,
            DateTime periodStart,
            DateTime periodEnd,
            string importFingerprint,
            string? sourceFileHash,
            CancellationToken ct)
        {
            return await _context.TrustStatementImports
                .AsNoTracking()
                .Where(i => i.TrustAccountId == trustAccountId &&
                            i.PeriodStart == periodStart &&
                            i.PeriodEnd == periodEnd)
                .Where(i => i.ImportFingerprint == importFingerprint ||
                            (!string.IsNullOrWhiteSpace(sourceFileHash) && i.SourceFileHash == sourceFileHash))
                .OrderByDescending(i => i.ImportedAt)
                .FirstOrDefaultAsync(ct);
        }

        private async Task SupersedePriorStatementImportsAsync(TrustStatementImport currentImport, string actorUserId, DateTime supersededAt, CancellationToken ct)
        {
            var priorImports = await _context.TrustStatementImports
                .Where(i =>
                    i.Id != currentImport.Id &&
                    i.TrustAccountId == currentImport.TrustAccountId &&
                    i.PeriodStart == currentImport.PeriodStart &&
                    i.PeriodEnd == currentImport.PeriodEnd &&
                    i.DuplicateOfStatementImportId == null &&
                    i.Status != "duplicate" &&
                    i.Status != "superseded")
                .ToListAsync(ct);

            if (priorImports.Count == 0)
            {
                return;
            }

            foreach (var prior in priorImports)
            {
                prior.Status = "superseded";
                prior.SupersededByStatementImportId = currentImport.Id;
                prior.SupersededBy = actorUserId;
                prior.SupersededAt = supersededAt;
                prior.UpdatedAt = supersededAt;
            }

            await SaveChangesWithConcurrencyHandlingAsync(ct);
        }

        private static string SerializeStatementImportMetadata(
            TrustStatementImportRequest request,
            string importFingerprint,
            string? duplicateOfStatementImportId,
            bool duplicateOverrideRequested)
        {
            var metadata = new Dictionary<string, object?>
            {
                ["importFingerprint"] = importFingerprint,
                ["sourceFileName"] = request.SourceFileName?.Trim(),
                ["sourceFileHash"] = NormalizeHashToken(request.SourceFileHash),
                ["sourceEvidenceKey"] = request.SourceEvidenceKey?.Trim(),
                ["sourceFileSizeBytes"] = request.SourceFileSizeBytes,
                ["lineCount"] = request.Lines?.Count ?? 0,
                ["duplicateOfStatementImportId"] = duplicateOfStatementImportId,
                ["duplicateOverrideRequested"] = duplicateOverrideRequested
            };

            return JsonSerializer.Serialize(metadata);
        }

        private static string BuildStatementImportFingerprint(
            TrustStatementImportRequest request,
            DateTime periodStart,
            DateTime periodEnd,
            string? normalizedSourceFileHash)
        {
            var payload = new StringBuilder()
                .Append(request.TrustAccountId.Trim())
                .Append('|')
                .Append(periodStart.ToString("yyyy-MM-dd"))
                .Append('|')
                .Append(periodEnd.ToString("yyyy-MM-dd"))
                .Append('|')
                .Append(NormalizeMoney(request.StatementEndingBalance).ToString("0.00"))
                .Append('|')
                .Append((request.Source ?? "manual").Trim().ToLowerInvariant())
                .Append('|')
                .Append(normalizedSourceFileHash ?? string.Empty);

            foreach (var line in (request.Lines ?? []).OrderBy(l => l.PostedAt).ThenBy(l => l.Amount).ThenBy(l => l.Reference))
            {
                payload
                    .Append('|')
                    .Append(line.PostedAt.ToUniversalTime().ToString("O"))
                    .Append('|')
                    .Append(line.EffectiveAt?.ToUniversalTime().ToString("O") ?? string.Empty)
                    .Append('|')
                    .Append(NormalizeMoney(line.Amount).ToString("0.00"))
                    .Append('|')
                    .Append(line.BalanceAfter.HasValue ? NormalizeMoney(line.BalanceAfter.Value).ToString("0.00") : string.Empty)
                    .Append('|')
                    .Append(line.Reference?.Trim() ?? string.Empty)
                    .Append('|')
                    .Append(line.CheckNumber?.Trim() ?? string.Empty)
                    .Append('|')
                    .Append(line.Description?.Trim() ?? string.Empty)
                    .Append('|')
                    .Append(line.Counterparty?.Trim() ?? string.Empty)
                    .Append('|')
                    .Append(line.ExternalLineId?.Trim() ?? string.Empty);
            }

            var digest = SHA256.HashData(Encoding.UTF8.GetBytes(payload.ToString()));
            return Convert.ToHexString(digest).ToLowerInvariant();
        }

        private static string? NormalizeHashToken(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var normalized = value.Trim().Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
            return normalized.Length == 0 ? null : normalized;
        }

        private static decimal NormalizeMoney(decimal value)
        {
            return decimal.Round(value, 2, MidpointRounding.AwayFromZero);
        }

        private static string BuildJournalCorrelationKey(string trustTransactionId, int index)
        {
            return $"trust-journal:{trustTransactionId}:{index}";
        }

        private static string NewRowVersion()
        {
            return Guid.NewGuid().ToString("N");
        }

        private sealed record TrustApprovalRequirementSeed(string RequirementType, int RequiredCount, string Summary);
        private sealed record TrustApprovalPlan(string TransactionType, string DisbursementClass, IReadOnlyList<TrustApprovalRequirementSeed> Requirements);
        private sealed record TrustMonthCloseTemplateStatus(
            TrustJurisdictionPacketTemplate Template,
            List<string> MissingRequiredSections,
            List<string> DisclosureBlocks,
            List<TrustPacketTemplateAttestationDto> RequiredAttestations,
            List<TrustMonthCloseAttestationDto> CompletedAttestations);
        private sealed record TrustMonthCloseTemplateSummary(
            string? TemplateKey,
            string? Name,
            int? VersionNumber,
            List<string> MissingRequiredSections,
            List<string> DisclosureBlocks,
            List<TrustPacketTemplateAttestationDto> RequiredAttestations,
            List<TrustMonthCloseAttestationDto> CompletedAttestations);
    }

    public sealed class TrustCommandException : Exception
    {
        public TrustCommandException(int statusCode, string message)
            : base(message)
        {
            StatusCode = statusCode;
        }

        public int StatusCode { get; }
    }
}
