using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PetraERP.Desktop.Security;

/// <summary>Current-Windows-user DPAPI. No access tokens or passwords are serialized.</summary>
public sealed class WindowsSessionStore : IProtectedSessionStore
{
    private readonly string path;
    private readonly byte[] entropy;
    public WindowsSessionStore(Uri endpoint, string directory)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        entropy = SHA256.HashData(Encoding.UTF8.GetBytes("PetraERP.Desktop.Session.v1|" + endpoint.AbsoluteUri));
        path = Path.Combine(directory, Convert.ToHexString(entropy), "session.bin");
    }
    public StoredSession? Read()
    {
        if (!File.Exists(path)) return null;
        byte[]? clear = null;
        try
        {
            if (new FileInfo(path).Length > 16384) { Delete(); return null; }
            clear = ProtectedData.Unprotect(File.ReadAllBytes(path), entropy, DataProtectionScope.CurrentUser);
            var value = JsonSerializer.Deserialize<StoredSession>(clear);
            if (value is null || string.IsNullOrWhiteSpace(value.RefreshToken) || value.RefreshToken.Length > 4096 ||
                value.RefreshExpiresUtc <= DateTimeOffset.UtcNow || value.SessionExpiresUtc <= DateTimeOffset.UtcNow)
            { Delete(); return null; }
            return value;
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException)
        { Delete(); return null; }
        finally { if (clear is not null) CryptographicOperations.ZeroMemory(clear); }
    }
    public void Save(StoredSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var clear = JsonSerializer.SerializeToUtf8Bytes(session);
        try
        {
            var encrypted = ProtectedData.Protect(clear, entropy, DataProtectionScope.CurrentUser);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllBytes(temporary, encrypted); File.Move(temporary, path, overwrite: true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }
    public void Delete()
    {
        try { File.Delete(path); }
        catch (DirectoryNotFoundException) { /* An installation with no persisted session is already clear. */ }
    }
}
