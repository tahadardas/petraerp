using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace PetraERP.Infrastructure.DocumentProcessing;

/// <summary>
/// Canonical SHA-256 of the complete financial input of a save command, computed from the command exactly as the client
/// sent it. There is no tolerance: one cent, the last exchange-rate digit, tax, discount, cost centre, line amount,
/// allocation destination or document instant produces a different hash.
/// Documented normalization (the only one): strings are trimmed (null and empty are the same), decimals compare by
/// value (10.0 == 10.00, never 10.00 == 10.01), instants compare as UTC ticks, GUIDs as 32 hex digits, enums by name,
/// and property order is fixed alphabetically. Collection order is significant. Server-assigned or transport-only
/// members (<see cref="Excluded"/>) are never part of the identity.
/// </summary>
internal static class DocumentRequestHash
{
    private static readonly HashSet<string> Excluded = new(StringComparer.Ordinal)
    {
        "DocumentId", "ActorUserId", "ExpectedVersion", "RowVersion", "ConcurrencyToken",
    };

    public static string Of(string documentKind, object command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentKind);
        ArgumentNullException.ThrowIfNull(command);
        var text = new StringBuilder(512);
        text.Append(documentKind).Append('\u001f');
        Write(text, command, 0);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()))).ToLowerInvariant();
    }

    private static void Write(StringBuilder sb, object? value, int depth)
    {
        if (depth > 12) throw new InvalidOperationException("Request graph is too deep to fingerprint.");
        switch (value)
        {
            case null: sb.Append('~'); return;
            case string s:
                {
                    var t = s.Trim();
                    if (t.Length == 0) sb.Append('~'); else sb.Append('"').Append(t.Replace("\"", "\\\"", StringComparison.Ordinal)).Append('"');
                    return;
                }
            case bool b: sb.Append(b ? "true" : "false"); return;
            case Guid g: sb.Append(g.ToString("N")); return;
            case decimal d: sb.Append(Normalize(d)); return;
            case double or float: sb.Append(Convert.ToString(value, CultureInfo.InvariantCulture)); return;
            case DateTimeOffset o: sb.Append('@').Append(o.UtcTicks.ToString(CultureInfo.InvariantCulture)); return;
            case DateTime dt: sb.Append('@').Append(dt.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture)); return;
            case DateOnly day: sb.Append('D').Append(day.DayNumber.ToString(CultureInfo.InvariantCulture)); return;
            case byte[] bytes: sb.Append('x').Append(Convert.ToHexString(bytes)); return;
            case Enum e: sb.Append(e.GetType().Name).Append('.').Append(e); return;
            case IConvertible c when value.GetType().IsPrimitive: sb.Append(c.ToString(CultureInfo.InvariantCulture)); return;
            case IEnumerable items:
                sb.Append('[');
                foreach (var item in items) { Write(sb, item, depth + 1); sb.Append(','); }
                sb.Append(']');
                return;
        }

        sb.Append('{');
        foreach (var property in value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .Where(p => p.GetIndexParameters().Length == 0 && p.CanRead && !Excluded.Contains(p.Name))
                     .OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            sb.Append(property.Name).Append('=');
            Write(sb, property.GetValue(value), depth + 1);
            sb.Append(';');
        }
        sb.Append('}');
    }

    /// <summary>Value-based decimal text: trailing zeros are not significant, every significant digit is.</summary>
    internal static string Normalize(decimal value) =>
        value == 0m ? "0" : value.ToString("0.############################", CultureInfo.InvariantCulture);
}
