namespace PetraERP.Modules.Host;

/// <summary>
/// Small, explicit version comparison for dependency ranges. Supported forms: exact ("1.2.3"), caret ("^1.2.0" - same
/// major, at least the given version), tilde ("~1.2.0" - same major and minor, at least the given version) and open
/// (">=1.2.0"). Anything else is rejected rather than guessed.
/// </summary>
public static class ModuleVersionRange
{
    public static int Compare(string left, string right)
    {
        var a = Parse(left);
        var b = Parse(right);
        if (a is null || b is null)
            throw new ArgumentException($"إصدار غير صالح: {left} أو {right}");
        return a.CompareTo(b);
    }

    public static bool Matches(string version, string range)
    {
        var candidate = Parse(version);
        if (candidate is null)
            return false;

        var trimmed = range?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            return true;

        if (trimmed.StartsWith(">=", StringComparison.Ordinal))
        {
            var floor = Parse(trimmed[2..].Trim());
            return floor is not null && candidate >= floor;
        }

        if (trimmed.StartsWith('^'))
        {
            var floor = Parse(trimmed[1..].Trim());
            return floor is not null && candidate >= floor && candidate.Major == floor.Major;
        }

        if (trimmed.StartsWith('~'))
        {
            var floor = Parse(trimmed[1..].Trim());
            return floor is not null && candidate >= floor && candidate.Major == floor.Major && candidate.Minor == floor.Minor;
        }

        var exact = Parse(trimmed);
        return exact is not null && candidate == exact;
    }

    public static bool IsCompatible(string installedVersion, string minCoreVersion, string? maxCoreVersion, string coreVersion)
    {
        _ = installedVersion;
        if (!Version.TryParse(Normalize(coreVersion), out var core))
            return false;
        if (!Version.TryParse(Normalize(minCoreVersion), out var minimum))
            return false;
        if (core < minimum)
            return false;
        if (maxCoreVersion is not null && Version.TryParse(Normalize(maxCoreVersion), out var maximum) && core > maximum)
            return false;
        return true;
    }

    private static Version? Parse(string value)
    {
        var normalized = Normalize(value);
        if (Version.TryParse(normalized, out var version))
            return version;
        var parts = normalized.Split('.', StringSplitOptions.TrimEntries);
        if (parts.Length == 2 && int.TryParse(parts[0], out var major) && int.TryParse(parts[1], out var minor))
            return new Version(major, minor, 0);
        if (parts.Length == 1 && int.TryParse(parts[0], out var single))
            return new Version(single, 0, 0);
        return null;
    }

    private static string Normalize(string value)
    {
        var text = value.Trim();
        var dash = text.IndexOf('-');
        if (dash > 0)
            text = text[..dash];
        var parts = text.Split('.', StringSplitOptions.TrimEntries);
        return parts.Length switch
        {
            1 => $"{text}.0.0",
            2 => $"{text}.0",
            _ => text,
        };
    }
}
