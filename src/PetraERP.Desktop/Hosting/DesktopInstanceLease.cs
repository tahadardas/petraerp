using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace PetraERP.Desktop.Hosting;

/// <summary>Exclusive per-Windows-profile/API file handle, including across RDP logon sessions.</summary>
public sealed class DesktopInstanceLease : IDisposable
{
    private readonly FileStream lease;
    public DesktopInstanceLease(Uri endpoint)
    {
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(endpoint.AbsoluteUri)));
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PetraERP", "Sessions", id);
        Directory.CreateDirectory(directory);
        lease = new FileStream(Path.Combine(directory, "desktop.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    public void Dispose() => lease.Dispose();
}
