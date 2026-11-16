namespace PetraERP.Application.Security;

/// <summary>
/// Login identifier rules. Login names are restricted to ASCII letters, digits, '.', '_' and '-'
/// so that visually confusable Unicode characters cannot create look-alike accounts. Display names
/// remain full Unicode (Arabic first). Comparison uses the invariant upper-case normalized form.
/// </summary>
public static class LoginName
{
    public const int MinimumLength = 3;
    public const int MaximumLength = 64;

    public static bool TryNormalize(string? input, out string userName, out string normalizedUserName)
    {
        userName = string.Empty;
        normalizedUserName = string.Empty;
        if (input is null)
        {
            return false;
        }

        var trimmed = input.Trim();
        if (trimmed.Length is < MinimumLength or > MaximumLength)
        {
            return false;
        }

        foreach (var character in trimmed)
        {
            if (!(char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-'))
            {
                return false;
            }
        }

        userName = trimmed;
        normalizedUserName = trimmed.ToUpperInvariant();
        return true;
    }

    /// <summary>
    /// Normalizes an attempted login name for lookup and audit without validating it,
    /// bounded to the stored length. Never returns secrets other than what the caller typed as a name.
    /// </summary>
    public static string NormalizeForLookup(string? input)
    {
        var trimmed = (input ?? string.Empty).Trim();
        if (trimmed.Length > MaximumLength)
        {
            trimmed = trimmed[..MaximumLength];
        }

        return trimmed.ToUpperInvariant();
    }
}
