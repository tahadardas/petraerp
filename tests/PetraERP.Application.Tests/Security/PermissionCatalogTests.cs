using PetraERP.Application.Security;

namespace PetraERP.Application.Tests.Security;

public sealed class PermissionCatalogTests
{
    [Fact]
    public void CatalogCodesAreUniqueOrdinalSortedAndWellFormed()
    {
        var codes = PermissionCatalog.All.Select(static permission => permission.Code).ToArray();
        Assert.Equal(codes.Length, codes.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(codes.Order(StringComparer.Ordinal), codes);
        Assert.All(codes, static code =>
        {
            Assert.InRange(code.Length, 3, 128);
            Assert.All(code, static character => Assert.True(char.IsAsciiLetter(character) || character == '.'));
            Assert.Equal(3, code.Split('.').Length);
        });
    }

    [Fact]
    public void LedgerPostingPermissionsAreBranchScopedAndAdministrationIsInstallationWide()
    {
        Assert.Equal(PermissionScope.Branch, PermissionCatalog.Get(PermissionCatalog.JournalsPost).Scope);
        Assert.Equal(PermissionScope.Branch, PermissionCatalog.Get(PermissionCatalog.JournalsReverse).Scope);
        Assert.Equal(PermissionScope.Branch, PermissionCatalog.Get(PermissionCatalog.JournalsManage).Scope);
        Assert.Equal(PermissionScope.Branch, PermissionCatalog.Get(PermissionCatalog.JournalsApprove).Scope);
        Assert.Equal(PermissionScope.Branch, PermissionCatalog.Get(PermissionCatalog.JournalsView).Scope);
        Assert.Equal(PermissionScope.Branch, PermissionCatalog.Get(PermissionCatalog.AccountsView).Scope);
        Assert.Equal(PermissionScope.Installation, PermissionCatalog.Get(PermissionCatalog.UsersManage).Scope);
        Assert.Equal(PermissionScope.Installation, PermissionCatalog.Get(PermissionCatalog.SecurityAuditView).Scope);
        Assert.All(PermissionCatalog.AdministrationCore, static code => Assert.True(PermissionCatalog.TryGet(code, out _)));
    }

    [Fact]
    public void UnknownOrDifferentlyCasedCodesAreRejected()
    {
        Assert.False(PermissionCatalog.TryGet("system.users.manage", out _));
        Assert.False(PermissionCatalog.TryGet("Ledger.Journals.Delete", out _));
        Assert.Throws<ArgumentException>(static () => PermissionCatalog.Get("Unknown.Code.Here"));
    }
}
