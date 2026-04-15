using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JurisFlow.Server.Contracts;
using JurisFlow.Server.Data;
using JurisFlow.Server.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace JurisFlow.Server.Services
{
    public sealed class TrustBundleIntegrityService
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        {
            WriteIndented = false
        };

        private readonly JurisFlowDbContext _context;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IConfiguration _configuration;

        public TrustBundleIntegrityService(
            JurisFlowDbContext context,
            IHttpContextAccessor httpContextAccessor,
            IConfiguration configuration)
        {
            _context = context;
            _httpContextAccessor = httpContextAccessor;
            _configuration = configuration;
        }

        public async Task<TrustBundleIntegrityDto> GetBundleIntegrityAsync(string manifestExportId, CancellationToken ct = default)
        {
            var manifest = await GetManifestAsync(manifestExportId, ct);
            var signature = await _context.TrustBundleSignatures
                .Where(s => s.ManifestExportId == manifestExportId)
                .OrderByDescending(s => s.SignedAt)
                .FirstOrDefaultAsync(ct);

            var computedDigest = ComputeManifestDigest(manifest);
            if (signature == null)
            {
                return BuildIntegrityDto(manifest, null, computedDigest, "unsigned");
            }

            var verificationStatus = string.Equals(signature.SignatureDigest, computedDigest, StringComparison.Ordinal)
                ? "verified"
                : "invalid";
            signature.VerificationStatus = verificationStatus;
            signature.IntegrityStatus = verificationStatus == "verified" ? "verified" : "invalid";
            signature.VerifiedAt = DateTime.UtcNow;
            signature.UpdatedAt = DateTime.UtcNow;
            manifest.IntegrityStatus = signature.IntegrityStatus;
            manifest.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(ct);

            return BuildIntegrityDto(manifest, signature, computedDigest, verificationStatus);
        }

        public async Task<TrustBundleIntegrityDto> SignBundleAsync(string manifestExportId, TrustBundleSignRequest? request, CancellationToken ct = default)
        {
            var manifest = await GetManifestAsync(manifestExportId, ct);
            var digest = ComputeManifestDigest(manifest);
            var now = DateTime.UtcNow;
            var signature = new TrustBundleSignature
            {
                Id = Guid.NewGuid().ToString(),
                ManifestExportId = manifest.Id,
                SignatureAlgorithm = "hmac-sha256",
                SignatureDigest = digest,
                IntegrityStatus = "signed",
                VerificationStatus = "verified",
                SignedBy = GetCurrentUserId(),
                SignedAt = now,
                VerifiedAt = now,
                RetentionPolicyTag = request?.RetentionPolicyTag?.Trim(),
                RedactionProfile = request?.RedactionProfile?.Trim(),
                ParentManifestExportId = manifest.ParentExportId,
                EvidenceManifestJson = BuildEvidenceManifestJson(manifest),
                MetadataJson = request == null ? null : JsonSerializer.Serialize(new { request.Notes }, JsonOptions),
                CreatedAt = now,
                UpdatedAt = now
            };

            manifest.IntegrityStatus = "signed";
            manifest.RetentionPolicyTag = signature.RetentionPolicyTag ?? manifest.RetentionPolicyTag ?? "trust_default";
            manifest.RedactionProfile = signature.RedactionProfile ?? manifest.RedactionProfile ?? "internal_unredacted";
            manifest.ProvenanceJson = MergeProvenance(manifest.ProvenanceJson, signature);
            manifest.UpdatedAt = now;

            _context.TrustBundleSignatures.Add(signature);
            await _context.SaveChangesAsync(ct);

            return BuildIntegrityDto(manifest, signature, digest, "verified");
        }

        private async Task<TrustComplianceExport> GetManifestAsync(string manifestExportId, CancellationToken ct)
        {
            var manifest = await _context.TrustComplianceExports.FirstOrDefaultAsync(
                e => e.Id == manifestExportId && e.ExportType == "compliance_bundle_manifest",
                ct);
            if (manifest == null)
            {
                throw new TrustCommandException(StatusCodes.Status404NotFound, "Compliance bundle manifest not found.");
            }

            return manifest;
        }

        private string ComputeManifestDigest(TrustComplianceExport manifest)
        {
            var key = _configuration["Security:TrustBundleSignatureKey"];
            if (string.IsNullOrWhiteSpace(key))
            {
                key = "jurisflow-dev-trust-bundle-signature-key";
            }

            var canonical = JsonSerializer.Serialize(new
            {
                manifest.Id,
                manifest.ExportType,
                manifest.Format,
                manifest.TrustAccountId,
                manifest.TrustMonthCloseId,
                manifest.TrustReconciliationPacketId,
                manifest.GeneratedAt,
                manifest.SummaryJson,
                manifest.PayloadJson,
                manifest.ParentExportId
            }, JsonOptions);

            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
            var bytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        private static string BuildEvidenceManifestJson(TrustComplianceExport manifest)
        {
            if (string.IsNullOrWhiteSpace(manifest.PayloadJson))
            {
                return JsonSerializer.Serialize(new { evidenceReferences = Array.Empty<object>() }, JsonOptions);
            }

            try
            {
                using var document = JsonDocument.Parse(manifest.PayloadJson);
                if (document.RootElement.TryGetProperty("evidenceReferences", out var evidenceReferences))
                {
                    return JsonSerializer.Serialize(new { evidenceReferences }, JsonOptions);
                }
            }
            catch
            {
                // Keep chain-of-custody generation resilient even when old payloads are malformed.
            }

            return JsonSerializer.Serialize(new { evidenceReferences = Array.Empty<object>() }, JsonOptions);
        }

        private static string MergeProvenance(string? provenanceJson, TrustBundleSignature signature)
        {
            object? prior = null;
            if (!string.IsNullOrWhiteSpace(provenanceJson))
            {
                try
                {
                    prior = JsonSerializer.Deserialize<object>(provenanceJson);
                }
                catch
                {
                    prior = provenanceJson;
                }
            }

            return JsonSerializer.Serialize(new
            {
                prior,
                latestSignature = new
                {
                    signature.ManifestExportId,
                    signature.SignatureAlgorithm,
                    signature.SignatureDigest,
                    signature.SignedBy,
                    signature.SignedAt,
                    signature.RetentionPolicyTag,
                    signature.RedactionProfile,
                    signature.ParentManifestExportId
                }
            }, JsonOptions);
        }

        private static TrustBundleIntegrityDto BuildIntegrityDto(
            TrustComplianceExport manifest,
            TrustBundleSignature? signature,
            string computedDigest,
            string verificationStatus)
        {
            var evidenceReferenceCount = 0;
            var exportCount = 0;
            if (!string.IsNullOrWhiteSpace(manifest.PayloadJson))
            {
                try
                {
                    using var document = JsonDocument.Parse(manifest.PayloadJson);
                    if (document.RootElement.TryGetProperty("exports", out var exports) && exports.ValueKind == JsonValueKind.Array)
                    {
                        exportCount = exports.GetArrayLength();
                    }

                    if (document.RootElement.TryGetProperty("evidenceReferences", out var evidenceReferences) && evidenceReferences.ValueKind == JsonValueKind.Array)
                    {
                        evidenceReferenceCount = evidenceReferences.GetArrayLength();
                    }
                }
                catch
                {
                    exportCount = 0;
                    evidenceReferenceCount = 0;
                }
            }

            return new TrustBundleIntegrityDto
            {
                ManifestExportId = manifest.Id,
                ManifestFileName = manifest.FileName,
                IntegrityStatus = signature?.IntegrityStatus ?? manifest.IntegrityStatus,
                SignatureAlgorithm = signature?.SignatureAlgorithm ?? "hmac-sha256",
                SignatureDigest = signature?.SignatureDigest ?? computedDigest,
                SignedBy = signature?.SignedBy,
                SignedAt = signature?.SignedAt,
                VerificationStatus = verificationStatus,
                VerifiedAt = signature?.VerifiedAt,
                RetentionPolicyTag = signature?.RetentionPolicyTag ?? manifest.RetentionPolicyTag,
                RedactionProfile = signature?.RedactionProfile ?? manifest.RedactionProfile,
                ParentManifestExportId = signature?.ParentManifestExportId ?? manifest.ParentExportId,
                EvidenceReferenceCount = evidenceReferenceCount,
                ExportCount = exportCount,
                ProvenanceJson = manifest.ProvenanceJson
            };
        }

        private string? GetCurrentUserId()
        {
            var user = _httpContextAccessor.HttpContext?.User;
            return user?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                   ?? user?.FindFirst("sub")?.Value
                   ?? user?.FindFirst("userId")?.Value
                   ?? user?.Identity?.Name;
        }
    }
}
