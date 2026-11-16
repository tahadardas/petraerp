using PetraERP.Desktop.Caching;

namespace PetraERP.Desktop.Tests;

[Collection(WpfApplicationGroup.Name)]
public sealed class PermissionFingerprintTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ChangedPermissionsNeverServeListsLoadedUnderTheOldSet()
    {
        PermissionFingerprint.Reset();
        Assert.True(PermissionFingerprint.Update(["Ledger.Accounts.View", "Ledger.Reports.View"]));
        Assert.False(PermissionFingerprint.Update(["Ledger.Reports.View", "Ledger.Accounts.View"]), "order and duplicates do not change the set");
        var cache = new ReferenceDataCache(TimeProvider.System);
        var company = Guid.NewGuid(); var year = Guid.NewGuid(); var workspace = Guid.NewGuid();
        var before = new CacheScope(company, year, workspace, null, PermissionFingerprint.Current);
        var loads = 0;
        Task<int> Load(CacheScope scope) => cache.GetOrCreateAsync(scope, "accounts", "tree", _ => Task.FromResult(++loads), TimeSpan.FromMinutes(5), Cancellation);
        Assert.Equal(1, await Load(before).ConfigureAwait(true));
        Assert.Equal(1, await Load(before).ConfigureAwait(true)); // cached

        Assert.True(PermissionFingerprint.Update(["Ledger.Reports.View"]));
        var after = new CacheScope(company, year, workspace, null, PermissionFingerprint.Current);
        Assert.NotEqual(before.ScopePart, after.ScopePart);
        Assert.Equal(2, await Load(after).ConfigureAwait(true)); // a reduced permission set reloads instead of reusing the old list
        PermissionFingerprint.Reset();
    }
}
