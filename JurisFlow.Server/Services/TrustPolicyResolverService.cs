using System.Text.Json;
using JurisFlow.Server.Contracts;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace JurisFlow.Server.Services
{
    public sealed class TrustPolicyResolverService
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        private readonly JurisFlowDbContext _context;
        private readonly Dictionary<string, TrustResolvedPolicyContext> _cache = new(StringComparer.Ordinal);
        private readonly Dictionary<string, TrustJurisdictionPacketTemplate> _packetTemplateCache = new(StringComparer.Ordinal);

        public TrustPolicyResolverService(JurisFlowDbContext context)
        {
            _context = context;
        }

        public async Task<TrustResolvedPolicyContext> ResolveEffectivePolicyAsync(string trustAccountId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(trustAccountId))
            {
                throw new TrustCommandException(StatusCodes.Status400BadRequest, "Trust account id is required.");
            }

            if (_cache.TryGetValue(trustAccountId, out var cached))
            {
                return cached;
            }

            var account = await _context.TrustBankAccounts.FirstOrDefaultAsync(a => a.Id == trustAccountId, ct);
            if (account == null)
            {
                throw new TrustCommandException(StatusCodes.Status404NotFound, "Trust account not found.");
            }

            var jurisdiction = NormalizeJurisdiction(account.Jurisdiction);
            var accountType = NormalizePolicyAccountType(account.AccountType, allowWildcard: false);
            var requestedPolicyKey = string.IsNullOrWhiteSpace(account.JurisdictionPolicyKey)
                ? BuildBaselinePolicyKey(jurisdiction, accountType)
                : account.JurisdictionPolicyKey!.Trim();

            var policy = await _context.TrustJurisdictionPolicies
                .Where(p =>
                    p.PolicyKey == requestedPolicyKey &&
                    (p.Jurisdiction == jurisdiction || p.Jurisdiction == "DEFAULT") &&
                    (p.AccountType == accountType || p.AccountType == "all"))
                .OrderByDescending(p => p.Jurisdiction == jurisdiction)
                .ThenByDescending(p => p.AccountType == accountType)
                .ThenByDescending(p => p.IsActive)
                .ThenByDescending(p => p.VersionNumber)
                .ThenByDescending(p => p.UpdatedAt)
                .FirstOrDefaultAsync(ct);

            if (policy == null)
            {
                policy = new TrustJurisdictionPolicy
                {
                    Id = Guid.NewGuid().ToString(),
                    PolicyKey = requestedPolicyKey,
                    Jurisdiction = jurisdiction,
                    Name = $"{jurisdiction} {accountType} Baseline Trust Policy",
                    AccountType = accountType,
                    VersionNumber = 1,
                    IsSystemBaseline = true,
                    IsActive = true,
                    RequireMakerChecker = true,
                    RequireOverrideReason = true,
                    DualApprovalThreshold = 10000m,
                    ResponsibleLawyerApprovalThreshold = 25000m,
                    SignatoryApprovalThreshold = 5000m,
                    MonthlyCloseCadenceDays = 30,
                    ExceptionAgingSlaHours = 48,
                    RetentionPeriodMonths = 60,
                    RequireMonthlyThreeWayReconciliation = true,
                    RequireResponsibleLawyerAssignment = true,
                    DisbursementClassesRequiringSignatoryJson = JsonSerializer.Serialize(new[] { "settlement_payout", "third_party_payment" }, JsonOptions),
                    OperationalApproverRolesJson = JsonSerializer.Serialize(new[] { "Admin", "Partner", "Accountant" }, JsonOptions),
                    OverrideApproverRolesJson = JsonSerializer.Serialize(new[] { "Admin", "Partner" }, JsonOptions),
                    MetadataJson = JsonSerializer.Serialize(new
                    {
                        policyPack = "baseline",
                        jurisdiction,
                        accountType,
                        source = "system_generated"
                    }, JsonOptions),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _context.TrustJurisdictionPolicies.Add(policy);
                if (string.IsNullOrWhiteSpace(account.JurisdictionPolicyKey))
                {
                    account.JurisdictionPolicyKey = requestedPolicyKey;
                    account.UpdatedAt = DateTime.UtcNow;
                }

                await _context.SaveChangesAsync(ct);
            }

            var context = new TrustResolvedPolicyContext(
                account,
                policy,
                ParseList(account.AllowedSignatoriesJson),
                ParseList(policy.DisbursementClassesRequiringSignatoryJson),
                ParseList(policy.OperationalApproverRolesJson),
                ParseList(policy.OverrideApproverRolesJson));

            _cache[trustAccountId] = context;
            return context;
        }

        public static TrustAccountGovernanceDto ToGovernanceDto(TrustBankAccount account)
        {
            return new TrustAccountGovernanceDto
            {
                AccountType = string.IsNullOrWhiteSpace(account.AccountType) ? "iolta" : account.AccountType,
                ResponsibleLawyerUserId = account.ResponsibleLawyerUserId,
                AllowedSignatories = ParseList(account.AllowedSignatoriesJson),
                JurisdictionPolicyKey = account.JurisdictionPolicyKey,
                StatementCadence = string.IsNullOrWhiteSpace(account.StatementCadence) ? "monthly" : account.StatementCadence,
                OverdraftNotificationEnabled = account.OverdraftNotificationEnabled,
                BankReferenceMetadataJson = account.BankReferenceMetadataJson
            };
        }

        public static TrustJurisdictionPolicyUpsertDto ToUpsertDto(TrustJurisdictionPolicy policy)
        {
            return new TrustJurisdictionPolicyUpsertDto
            {
                PolicyKey = policy.PolicyKey,
                Jurisdiction = policy.Jurisdiction,
                Name = policy.Name,
                AccountType = NormalizePolicyAccountType(policy.AccountType),
                VersionNumber = policy.VersionNumber <= 0 ? 1 : policy.VersionNumber,
                IsActive = policy.IsActive,
                IsSystemBaseline = policy.IsSystemBaseline,
                RequireMakerChecker = policy.RequireMakerChecker,
                RequireOverrideReason = policy.RequireOverrideReason,
                DualApprovalThreshold = policy.DualApprovalThreshold,
                ResponsibleLawyerApprovalThreshold = policy.ResponsibleLawyerApprovalThreshold,
                SignatoryApprovalThreshold = policy.SignatoryApprovalThreshold,
                MonthlyCloseCadenceDays = policy.MonthlyCloseCadenceDays,
                ExceptionAgingSlaHours = policy.ExceptionAgingSlaHours,
                RetentionPeriodMonths = policy.RetentionPeriodMonths <= 0 ? 60 : policy.RetentionPeriodMonths,
                RequireMonthlyThreeWayReconciliation = policy.RequireMonthlyThreeWayReconciliation,
                RequireResponsibleLawyerAssignment = policy.RequireResponsibleLawyerAssignment,
                DisbursementClassesRequiringSignatory = ParseList(policy.DisbursementClassesRequiringSignatoryJson),
                OperationalApproverRoles = ParseList(policy.OperationalApproverRolesJson),
                OverrideApproverRoles = ParseList(policy.OverrideApproverRolesJson),
                MetadataJson = policy.MetadataJson
            };
        }

        public async Task<TrustJurisdictionPacketTemplate> ResolvePacketTemplateAsync(TrustResolvedPolicyContext resolvedPolicy, CancellationToken ct = default)
        {
            var cacheKey = $"{resolvedPolicy.Policy.PolicyKey}:{resolvedPolicy.Policy.Jurisdiction}:{resolvedPolicy.Account.AccountType}";
            if (_packetTemplateCache.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }

            var jurisdiction = NormalizeJurisdiction(resolvedPolicy.Policy.Jurisdiction);
            var accountType = NormalizePolicyAccountType(resolvedPolicy.Account.AccountType, allowWildcard: false);
            var requestedTemplateKey = BuildBaselinePacketTemplateKey(resolvedPolicy.Policy.PolicyKey, accountType);

            var template = await _context.TrustJurisdictionPacketTemplates
                .Where(t =>
                    (t.PolicyKey == resolvedPolicy.Policy.PolicyKey || t.PolicyKey == requestedTemplateKey) &&
                    (t.Jurisdiction == jurisdiction || t.Jurisdiction == "DEFAULT") &&
                    (t.AccountType == accountType || t.AccountType == "all"))
                .OrderByDescending(t => t.PolicyKey == resolvedPolicy.Policy.PolicyKey)
                .ThenByDescending(t => t.Jurisdiction == jurisdiction)
                .ThenByDescending(t => t.AccountType == accountType)
                .ThenByDescending(t => t.IsActive)
                .ThenByDescending(t => t.VersionNumber)
                .ThenByDescending(t => t.UpdatedAt)
                .FirstOrDefaultAsync(ct);

            if (template == null)
            {
                template = new TrustJurisdictionPacketTemplate
                {
                    Id = Guid.NewGuid().ToString(),
                    PolicyKey = resolvedPolicy.Policy.PolicyKey,
                    Jurisdiction = jurisdiction,
                    AccountType = accountType,
                    TemplateKey = requestedTemplateKey,
                    Name = $"{jurisdiction} {accountType} Canonical Close Packet",
                    VersionNumber = 1,
                    IsActive = true,
                    RequiredSectionsJson = JsonSerializer.Serialize(new[]
                    {
                        "statement_summary",
                        "three_way_summary",
                        "outstanding_schedule",
                        "signoff_chain",
                        "responsible_lawyer_block"
                    }, JsonOptions),
                    RequiredAttestationsJson = JsonSerializer.Serialize(new[]
                    {
                        new TrustPacketTemplateAttestationDto
                        {
                            Key = "reviewed_three_way_reconciliation",
                            Label = "I reviewed the three-way reconciliation packet and exception register.",
                            Role = "reviewer",
                            HelpText = "Reviewer must confirm the packet is complete before legal sign-off.",
                            Required = true
                        },
                        new TrustPacketTemplateAttestationDto
                        {
                            Key = "responsible_lawyer_certification",
                            Label = "I certify this month-close packet is complete and consistent with trust records.",
                            Role = "responsible_lawyer",
                            HelpText = "Responsible lawyer certification is required before close.",
                            Required = true
                        }
                    }, JsonOptions),
                    DisclosureBlocksJson = JsonSerializer.Serialize(new[]
                    {
                        "firm_header",
                        "entity_disclosure",
                        "office_disclosure"
                    }, JsonOptions),
                    RenderingProfileJson = JsonSerializer.Serialize(new
                    {
                        profile = "canonical_month_close_packet",
                        includeSchedules = true,
                        includeAttestationBlocks = true
                    }, JsonOptions),
                    MetadataJson = JsonSerializer.Serialize(new
                    {
                        source = "system_generated",
                        policyKey = resolvedPolicy.Policy.PolicyKey,
                        jurisdiction,
                        accountType
                    }, JsonOptions),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                _context.TrustJurisdictionPacketTemplates.Add(template);
                await _context.SaveChangesAsync(ct);
            }

            _packetTemplateCache[cacheKey] = template;
            return template;
        }

        public static TrustJurisdictionPacketTemplateUpsertDto ToTemplateUpsertDto(TrustJurisdictionPacketTemplate template)
        {
            return new TrustJurisdictionPacketTemplateUpsertDto
            {
                PolicyKey = template.PolicyKey,
                Jurisdiction = template.Jurisdiction,
                AccountType = NormalizePolicyAccountType(template.AccountType),
                TemplateKey = template.TemplateKey,
                Name = template.Name,
                VersionNumber = template.VersionNumber <= 0 ? 1 : template.VersionNumber,
                IsActive = template.IsActive,
                RequiredSections = ParseList(template.RequiredSectionsJson),
                RequiredAttestations = ParseAttestations(template.RequiredAttestationsJson),
                DisclosureBlocks = ParseList(template.DisclosureBlocksJson),
                RenderingProfileJson = template.RenderingProfileJson,
                MetadataJson = template.MetadataJson
            };
        }

        public static string NormalizeJurisdiction(string? value) =>
            string.IsNullOrWhiteSpace(value) ? "DEFAULT" : value.Trim().ToUpperInvariant();

        public static string BuildBaselinePolicyKey(string jurisdiction, string? accountType = null)
        {
            var normalizedJurisdiction = NormalizeJurisdiction(jurisdiction);
            var normalizedAccountType = NormalizePolicyAccountType(accountType, allowWildcard: false);
            return $"{normalizedJurisdiction}-{normalizedAccountType}-baseline";
        }

        public static string BuildBaselinePacketTemplateKey(string policyKey, string? accountType = null)
        {
            var normalizedAccountType = NormalizePolicyAccountType(accountType, allowWildcard: false);
            var normalizedPolicyKey = string.IsNullOrWhiteSpace(policyKey) ? "default" : policyKey.Trim().ToLowerInvariant();
            return $"{normalizedPolicyKey}-{normalizedAccountType}-packet-template";
        }

        public static string NormalizePolicyAccountType(string? value, bool allowWildcard = true)
        {
            var normalized = string.IsNullOrWhiteSpace(value)
                ? (allowWildcard ? "all" : "iolta")
                : value.Trim().ToLowerInvariant().Replace("-", "_");

            return normalized switch
            {
                "iolta" => "iolta",
                "non_iolta" => "non_iolta",
                "noniolta" => "non_iolta",
                "all" when allowWildcard => "all",
                _ => allowWildcard ? "all" : "iolta"
            };
        }

        public static List<string> ParseList(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new List<string>();
            }

            try
            {
                return JsonSerializer.Deserialize<List<string>>(json, JsonOptions)?
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList() ?? new List<string>();
            }
            catch
            {
                return new List<string>();
            }
        }

        public static List<TrustPacketTemplateAttestationDto> ParseAttestations(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new List<TrustPacketTemplateAttestationDto>();
            }

            try
            {
                return JsonSerializer.Deserialize<List<TrustPacketTemplateAttestationDto>>(json, JsonOptions)?
                    .Where(x => !string.IsNullOrWhiteSpace(x.Key))
                    .GroupBy(x => $"{x.Role}:{x.Key}", StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .ToList() ?? new List<TrustPacketTemplateAttestationDto>();
            }
            catch
            {
                return new List<TrustPacketTemplateAttestationDto>();
            }
        }
    }

    public sealed record TrustResolvedPolicyContext(
        TrustBankAccount Account,
        TrustJurisdictionPolicy Policy,
        IReadOnlyList<string> AllowedSignatories,
        IReadOnlyList<string> SignatoryRequiredClasses,
        IReadOnlyList<string> OperationalApproverRoles,
        IReadOnlyList<string> OverrideApproverRoles)
    {
        public bool IsSignatory(string? userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(Account.ResponsibleLawyerUserId) &&
                string.Equals(Account.ResponsibleLawyerUserId, userId, StringComparison.Ordinal))
            {
                return true;
            }

            return AllowedSignatories.Contains(userId, StringComparer.Ordinal);
        }

        public bool IsResponsibleLawyer(string? userId) =>
            !string.IsNullOrWhiteSpace(userId) &&
            !string.IsNullOrWhiteSpace(Account.ResponsibleLawyerUserId) &&
            string.Equals(Account.ResponsibleLawyerUserId, userId, StringComparison.Ordinal);
    }
}
