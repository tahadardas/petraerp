using System.IO;
using System.Text;
using PetraERP.Desktop.Security;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Tests;

public sealed class WindowsSessionStoreTests
{
    [Fact]
    public void RealDpapiRoundTripsWithoutPlaintextAndRejectsCorruptionAndExpiredSecrets()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PetraERP_Secrets_" + Guid.NewGuid().ToString("N"));
        var store = new WindowsSessionStore(new Uri("https://localhost:7180/"), directory);
        try
        {
            store.Delete(); Assert.Null(store.Read());
            var value = new StoredSession("refresh-private-معرّف", DateTimeOffset.UtcNow.AddHours(1), DateTimeOffset.UtcNow.AddHours(2));
            store.Save(value); Assert.Equal(value, store.Read()); Assert.DoesNotContain(value.RefreshToken, value.ToString());
            var path = Assert.Single(Directory.GetFiles(directory, "session.bin", SearchOption.AllDirectories));
            Assert.DoesNotContain(value.RefreshToken, Encoding.UTF8.GetString(File.ReadAllBytes(path)));
            File.WriteAllBytes(path, [1, 2, 3, 4]); Assert.Null(store.Read()); Assert.False(File.Exists(path));
            store.Save(value with { RefreshExpiresUtc = DateTimeOffset.UtcNow.AddSeconds(-1) }); Assert.Null(store.Read());
            store.Save(value); store.Delete(); Assert.Null(store.Read());
            var other = new WindowsSessionStore(new Uri("https://different-server/"), directory); Assert.Null(other.Read());
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact]
    public void ContextPreferenceIsPerUserAndContainsOnlyIds()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PetraERP_Context_" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ContextPreferenceStore(directory); var user = Guid.NewGuid(); var context = new ContextPreference(Guid.NewGuid(), Guid.NewGuid());
            store.Save(user, context); Assert.Equal(context, store.Read(user)); Assert.Null(store.Read(Guid.NewGuid()));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
