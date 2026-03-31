using System.Text;

namespace JurisFlow.Server.Services;

public static class IntegrationSecretConfigurationResolver
{
    public static bool TryResolveConfiguredKeyValue(
        IConfiguration configuration,
        string keysPath,
        string requestedKeyId,
        out string resolvedKeyId,
        out string rawValue)
    {
        resolvedKeyId = string.Empty;
        rawValue = string.Empty;

        if (string.IsNullOrWhiteSpace(requestedKeyId))
        {
            return false;
        }

        var exactPath = $"{keysPath}:{requestedKeyId}";
        var exactValue = configuration[exactPath]?.Trim();
        if (!string.IsNullOrWhiteSpace(exactValue))
        {
            resolvedKeyId = requestedKeyId;
            rawValue = exactValue;
            return true;
        }

        foreach (var child in configuration.GetSection(keysPath).GetChildren())
        {
            var candidateKeyId = child.Key?.Trim();
            var candidateValue = child.Value?.Trim();
            if (string.IsNullOrWhiteSpace(candidateKeyId) || string.IsNullOrWhiteSpace(candidateValue))
            {
                continue;
            }

            if (!IsEquivalentKeyId(candidateKeyId, requestedKeyId))
            {
                continue;
            }

            resolvedKeyId = candidateKeyId;
            rawValue = candidateValue;
            return true;
        }

        return false;
    }

    public static string? FindEquivalentKeyId(IEnumerable<string> candidateKeyIds, string requestedKeyId)
    {
        if (string.IsNullOrWhiteSpace(requestedKeyId))
        {
            return null;
        }

        var keyIds = candidateKeyIds
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .ToArray();

        var exactMatch = keyIds.FirstOrDefault(k => string.Equals(k, requestedKeyId, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(exactMatch))
        {
            return exactMatch;
        }

        return keyIds.FirstOrDefault(k => IsEquivalentKeyId(k, requestedKeyId));
    }

    public static bool IsEquivalentKeyId(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        return string.Equals(CanonicalizeKeyId(left), CanonicalizeKeyId(right), StringComparison.Ordinal);
    }

    private static string CanonicalizeKeyId(string value)
    {
        var builder = new StringBuilder(value.Length);

        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToUpperInvariant(ch));
            }
        }

        return builder.ToString();
    }
}
