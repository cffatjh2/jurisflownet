using System.Text.Json;
using System.Text.RegularExpressions;

namespace JurisFlow.Server.Services
{
    public sealed class AppDirectoryManifest
    {
        public string ProviderKey { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = "General";
        public string ConnectionMode { get; set; } = "oauth";
        public string Summary { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string ManifestVersion { get; set; } = "1.0";
        public string? WebsiteUrl { get; set; }
        public string? DocumentationUrl { get; set; }
        public string? SupportEmail { get; set; }
        public string? SupportUrl { get; set; }
        public string? LogoUrl { get; set; }
        public bool SupportsWebhook { get; set; }
        public bool WebhookFirst { get; set; }
        public int? FallbackPollingMinutes { get; set; }
        public List<string> Capabilities { get; set; } = new();
        public Dictionary<string, string>? ConfigurationHints { get; set; }
    }

    public sealed class AppDirectorySlaProfile
    {
        public string Tier { get; set; } = "standard";
        public int? ResponseHours { get; set; }
        public int? ResolutionHours { get; set; }
        public double? UptimePercent { get; set; }
    }

    public sealed class AppDirectoryHarnessCheck
    {
        public string Key { get; init; } = string.Empty;
        public string Severity { get; init; } = "info";
        public bool Passed { get; init; }
        public string Message { get; init; } = string.Empty;
    }

    public sealed class AppDirectoryHarnessResult
    {
        public bool Passed { get; init; }
        public int ErrorCount { get; init; }
        public int WarningCount { get; init; }
        public string Summary { get; init; } = string.Empty;
        public IReadOnlyList<AppDirectoryHarnessCheck> Checks { get; init; } = Array.Empty<AppDirectoryHarnessCheck>();
    }

    public sealed class AppDirectoryOnboardingService
    {
        private static readonly Regex ProviderKeyRegex = new("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.Compiled);
        private static readonly HashSet<string> SupportedConnectionModes = new(StringComparer.OrdinalIgnoreCase)
        {
            "oauth",
            "api_key",
            "hybrid"
        };

        public AppDirectoryManifest NormalizeManifest(AppDirectoryManifest input)
        {
            return new AppDirectoryManifest
            {
                ProviderKey = NormalizeRequired(input.ProviderKey),
                Name = NormalizeRequired(input.Name),
                Category = NormalizeRequired(input.Category, fallback: "General"),
                ConnectionMode = NormalizeRequired(input.ConnectionMode, fallback: "oauth").ToLowerInvariant(),
                Summary = NormalizeRequired(input.Summary),
                Description = NormalizeOptional(input.Description),
                ManifestVersion = NormalizeRequired(input.ManifestVersion, fallback: "1.0"),
                WebsiteUrl = NormalizeOptional(input.WebsiteUrl),
                DocumentationUrl = NormalizeOptional(input.DocumentationUrl),
                SupportEmail = NormalizeOptional(input.SupportEmail),
                SupportUrl = NormalizeOptional(input.SupportUrl),
                LogoUrl = NormalizeOptional(input.LogoUrl),
                SupportsWebhook = input.SupportsWebhook,
                WebhookFirst = input.WebhookFirst,
                FallbackPollingMinutes = input.FallbackPollingMinutes,
                Capabilities = input.Capabilities
                    .Select(NormalizeOptional)
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .Select(v => v!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                ConfigurationHints = input.ConfigurationHints?
                    .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Key))
                    .ToDictionary(kvp => kvp.Key.Trim(), kvp => kvp.Value ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            };
        }

        public AppDirectorySlaProfile NormalizeSla(AppDirectorySlaProfile? input)
        {
            var profile = input ?? new AppDirectorySlaProfile();
            return new AppDirectorySlaProfile
            {
                Tier = NormalizeRequired(profile.Tier, fallback: "standard").ToLowerInvariant(),
                ResponseHours = profile.ResponseHours,
                ResolutionHours = profile.ResolutionHours,
                UptimePercent = profile.UptimePercent
            };
        }

        public AppDirectoryHarnessResult RunHarness(AppDirectoryManifest manifest, AppDirectorySlaProfile sla)
        {
            var checks = new List<AppDirectoryHarnessCheck>();

            AddCheck(checks, "provider_key.required", !string.IsNullOrWhiteSpace(manifest.ProviderKey), "error", "Provider key is required.");
            AddCheck(checks, "provider_key.slug", ProviderKeyRegex.IsMatch(manifest.ProviderKey), "error", "Provider key must be lowercase kebab-case.");
            AddCheck(checks, "name.required", !string.IsNullOrWhiteSpace(manifest.Name), "error", "App name is required.");
            AddCheck(checks, "summary.required", !string.IsNullOrWhiteSpace(manifest.Summary), "error", "Summary is required.");
            AddCheck(checks, "category.required", !string.IsNullOrWhiteSpace(manifest.Category), "error", "Category is required.");
            AddCheck(checks, "connection_mode.supported", SupportedConnectionModes.Contains(manifest.ConnectionMode), "error", "Connection mode must be oauth, api_key, or hybrid.");
            AddCheck(checks, "capabilities.min", manifest.Capabilities.Count > 0, "warning", "At least one capability should be provided.");
            AddCheck(checks, "manifest_version.required", !string.IsNullOrWhiteSpace(manifest.ManifestVersion), "error", "Manifest version is required.");
            AddCheck(checks, "website.https", IsHttpsUrl(manifest.WebsiteUrl), "warning", "Website URL should be a valid https URL.");
            AddCheck(checks, "docs.https", IsHttpsUrl(manifest.DocumentationUrl), "warning", "Documentation URL should be a valid https URL.");
            AddCheck(checks, "support.url", IsHttpsUrl(manifest.SupportUrl), "warning", "Support URL should be a valid https URL.");
            AddCheck(checks, "logo.url", IsHttpsUrl(manifest.LogoUrl), "warning", "Logo URL should be a valid https URL.");
            AddCheck(checks, "support.email", IsEmail(manifest.SupportEmail), "warning", "Support email should be valid.");

            if (manifest.WebhookFirst)
            {
                AddCheck(checks, "webhook_first.requires_webhook", manifest.SupportsWebhook, "error", "Webhook-first apps must declare supportsWebhook=true.");
                AddCheck(checks, "webhook_first.fallback_polling", manifest.FallbackPollingMinutes is >= 60 and <= 1440, "error", "Webhook-first apps must define fallbackPollingMinutes between 60 and 1440.");
            }
            else if (manifest.SupportsWebhook && manifest.FallbackPollingMinutes is < 30)
            {
                AddCheck(checks, "webhook.polling_hint", false, "warning", "Fallback polling under 30 minutes is usually too aggressive.");
            }

            AddCheck(checks, "sla.tier.required", !string.IsNullOrWhiteSpace(sla.Tier), "error", "SLA tier is required.");
            AddCheck(checks, "sla.response.range", sla.ResponseHours is >= 1 and <= 168, "warning", "SLA responseHours should be between 1 and 168.");
            AddCheck(checks, "sla.resolution.range", sla.ResolutionHours is >= 1 and <= 720, "warning", "SLA resolutionHours should be between 1 and 720.");
            AddCheck(checks, "sla.resolution.gte_response", !sla.ResponseHours.HasValue || !sla.ResolutionHours.HasValue || sla.ResolutionHours >= sla.ResponseHours, "error", "SLA resolutionHours must be >= responseHours.");
            AddCheck(checks, "sla.uptime.range", sla.UptimePercent is >= 95 and <= 100, "warning", "SLA uptime should be between 95 and 100.");

            var errorCount = checks.Count(c => string.Equals(c.Severity, "error", StringComparison.OrdinalIgnoreCase) && !c.Passed);
            var warningCount = checks.Count(c => string.Equals(c.Severity, "warning", StringComparison.OrdinalIgnoreCase) && !c.Passed);
            var passed = errorCount == 0;

            return new AppDirectoryHarnessResult
            {
                Passed = passed,
                ErrorCount = errorCount,
                WarningCount = warningCount,
                Summary = passed
                    ? $"Manifest tests passed with {warningCount} warning(s)."
                    : $"Manifest tests failed with {errorCount} error(s) and {warningCount} warning(s).",
                Checks = checks
            };
        }

        public string SerializeManifest(AppDirectoryManifest manifest)
        {
            return JsonSerializer.Serialize(manifest);
        }

        public string SerializeHarness(AppDirectoryHarnessResult harness)
        {
            return JsonSerializer.Serialize(harness);
        }

        public string SerializeFailedChecks(AppDirectoryHarnessResult harness)
        {
            var failed = harness.Checks.Where(c => !c.Passed).ToList();
            return JsonSerializer.Serialize(failed);
        }

        public string ResolveListingStatus(AppDirectoryHarnessResult harness)
        {
            return harness.Passed ? "in_review" : "changes_requested";
        }

        public string ResolveSubmissionStatus(AppDirectoryHarnessResult harness)
        {
            return harness.Passed ? "submitted" : "needs_changes";
        }

        public string ResolveTestStatus(AppDirectoryHarnessResult harness)
        {
            return harness.Passed ? "passed" : "failed";
        }

        private static void AddCheck(
            ICollection<AppDirectoryHarnessCheck> checks,
            string key,
            bool passed,
            string severity,
            string failureMessage)
        {
            checks.Add(new AppDirectoryHarnessCheck
            {
                Key = key,
                Severity = severity,
                Passed = passed,
                Message = passed ? "ok" : failureMessage
            });
        }

        private static bool IsHttpsUrl(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return true;
            }

            return Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) &&
                   string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsEmail(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return true;
            }

            try
            {
                _ = new System.Net.Mail.MailAddress(value.Trim());
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string NormalizeRequired(string? value, string fallback = "")
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        private static string? NormalizeOptional(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
