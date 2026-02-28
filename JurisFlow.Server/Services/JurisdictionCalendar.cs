using System;
using System.Linq;

namespace JurisFlow.Server.Services
{
    public static class JurisdictionCalendar
    {
        public static string? Normalize(string? jurisdiction)
        {
            if (string.IsNullOrWhiteSpace(jurisdiction))
            {
                return null;
            }

            var trimmed = jurisdiction.Trim();
            if (trimmed.StartsWith("US-", StringComparison.OrdinalIgnoreCase))
            {
                var suffix = trimmed.Substring(3);
                if (IsFederalAlias(suffix))
                {
                    return "US-Federal";
                }

                if (IsStateCode(suffix))
                {
                    return $"US-{suffix.ToUpperInvariant()}";
                }

                return $"US-{suffix.ToUpperInvariant()}";
            }

            if (IsFederalAlias(trimmed))
            {
                return "US-Federal";
            }

            if (IsStateCode(trimmed))
            {
                return $"US-{trimmed.ToUpperInvariant()}";
            }

            var stateSuffix = ExtractStateSuffix(trimmed);
            if (!string.IsNullOrWhiteSpace(stateSuffix))
            {
                return $"US-{stateSuffix}";
            }

            return trimmed;
        }

        private static bool IsFederalAlias(string value)
        {
            return string.Equals(value, "FRCP", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "FEDERAL", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "FED", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "US-FEDERAL", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsStateCode(string value)
        {
            return value.Length == 2 && value.All(char.IsLetter);
        }

        private static string? ExtractStateSuffix(string value)
        {
            if (!value.Contains('-'))
            {
                return null;
            }

            var parts = value.Split('-', StringSplitOptions.RemoveEmptyEntries);
            var last = parts.LastOrDefault();
            if (string.IsNullOrWhiteSpace(last))
            {
                return null;
            }

            return IsStateCode(last) ? last.ToUpperInvariant() : null;
        }
    }
}
