using System.Globalization;
using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;

namespace JurisFlow.Server.Services
{
    public static class ForwardedHeadersConfiguration
    {
        public const string SectionName = "ForwardedHeaders";

        public static ForwardedHeadersOptions CreateOptions(IConfiguration configuration)
        {
            var section = configuration.GetSection(SectionName);
            var options = new ForwardedHeadersOptions
            {
                ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
                ForwardLimit = GetForwardLimit(section)
            };

            options.RequireHeaderSymmetry = section.GetValue("RequireHeaderSymmetry", false);

            if (section.GetValue("TrustAllProxies", false))
            {
                options.KnownProxies.Clear();
                options.KnownIPNetworks.Clear();
                return options;
            }

            foreach (var proxy in GetConfiguredValues(section, "KnownProxies"))
            {
                if (!IPAddress.TryParse(proxy, out var address))
                {
                    throw new InvalidOperationException($"ForwardedHeaders:KnownProxies contains invalid IP address '{proxy}'.");
                }

                options.KnownProxies.Add(address);
            }

            foreach (var network in GetConfiguredValues(section, "KnownNetworks"))
            {
                options.KnownIPNetworks.Add(ParseNetwork(network));
            }

            return options;
        }

        public static bool IsEnabled(IConfiguration configuration)
        {
            return configuration.GetSection(SectionName).GetValue("Enabled", true);
        }

        public static bool HasExplicitTrustBoundary(IConfiguration configuration)
        {
            var section = configuration.GetSection(SectionName);
            return !IsEnabled(configuration) ||
                   section.GetValue("TrustAllProxies", false) ||
                   GetConfiguredValues(section, "KnownProxies").Any() ||
                   GetConfiguredValues(section, "KnownNetworks").Any();
        }

        public static bool IsTrustAllAllowedInProduction(IConfiguration configuration)
        {
            var section = configuration.GetSection(SectionName);
            return !section.GetValue("TrustAllProxies", false) ||
                   section.GetValue("AllowTrustAllProxiesInProduction", false);
        }

        private static int GetForwardLimit(IConfigurationSection section)
        {
            var configuredLimit = section.GetValue<int?>("ForwardLimit") ?? 1;
            if (configuredLimit < 1)
            {
                throw new InvalidOperationException("ForwardedHeaders:ForwardLimit must be 1 or greater.");
            }

            return configuredLimit;
        }

        private static System.Net.IPNetwork ParseNetwork(string value)
        {
            var parts = value.Split('/', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2 ||
                !IPAddress.TryParse(parts[0], out var prefix) ||
                !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var prefixLength))
            {
                throw new InvalidOperationException($"ForwardedHeaders:KnownNetworks contains invalid CIDR '{value}'.");
            }

            var maxPrefixLength = prefix.AddressFamily switch
            {
                AddressFamily.InterNetwork => 32,
                AddressFamily.InterNetworkV6 => 128,
                _ => throw new InvalidOperationException($"ForwardedHeaders:KnownNetworks contains unsupported address family in '{value}'.")
            };

            if (prefixLength < 0 || prefixLength > maxPrefixLength)
            {
                throw new InvalidOperationException($"ForwardedHeaders:KnownNetworks contains invalid prefix length in '{value}'.");
            }

            return new System.Net.IPNetwork(prefix, prefixLength);
        }

        private static IReadOnlyList<string> GetConfiguredValues(IConfigurationSection section, string key)
        {
            var values = new List<string>();
            var arrayValues = section.GetSection(key).Get<string[]>();
            if (arrayValues != null)
            {
                values.AddRange(arrayValues);
            }

            var scalarValue = section[key];
            if (!string.IsNullOrWhiteSpace(scalarValue))
            {
                values.AddRange(scalarValue.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            }

            return values
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
