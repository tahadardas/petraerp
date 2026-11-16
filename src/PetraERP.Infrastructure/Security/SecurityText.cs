using System.Text;

namespace PetraERP.Infrastructure.Security;

internal static class SecurityText
{
    /// <summary>Trims, replaces control characters and bounds free-form client-supplied labels.</summary>
    internal static string? BoundLabel(string? value, int length)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        var builder = new StringBuilder(Math.Min(trimmed.Length, length));
        foreach (var character in trimmed)
        {
            if (builder.Length == length)
            {
                break;
            }

            builder.Append(char.IsControl(character) || char.IsSurrogate(character) ? '?' : character);
        }

        var result = builder.ToString().Trim();
        return result.Length == 0 ? null : result;
    }
}
