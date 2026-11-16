using System.Security.Cryptography;
using System.Text;

namespace PetraERP.Desktop.Caching;

/// <summary>
/// Fingerprint of the signed-in user's effective permissions. It is part of every reference-data cache scope, so lists loaded under
/// one permission set are never served after the set changes (a role edited while the user stays signed in).
/// </summary>
public static class PermissionFingerprint
{
    private static string? current;

    public static string? Current => Volatile.Read(ref current);

    /// <summary>Stores the fingerprint of the given permissions; true when it differs from the previous one.</summary>
    public static bool Update(IEnumerable<string> permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        var text = string.Join('\n', permissions.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];
        return !string.Equals(Interlocked.Exchange(ref current, fingerprint), fingerprint, StringComparison.Ordinal);
    }

    public static void Reset() => Volatile.Write(ref current, null);
}
