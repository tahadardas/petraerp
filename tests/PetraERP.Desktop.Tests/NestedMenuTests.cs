using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Tests;

/// <summary>The top menu nests report/tool routes in sub-menus (Al-Ameen style) without dropping or inventing any destination.</summary>
public sealed class NestedMenuTests
{
    private static NavigationService Navigation(params string[] permissions)
    {
        var nav = new NavigationService(new LocalizationService("ar"));
        nav.Configure(permissions);
        return nav;
    }

    [Fact]
    public void AccountingNestsReportsAndToolsButKeepsCardsDirect()
    {
        var nav = Navigation("Ledger.Accounts.View", "Ledger.Reports.View", "Posting.Batches.Manage", "Ledger.Journals.View");
        var menu = nav.AccountingMenu;

        Assert.Contains(menu, e => e.IsLeaf && e.Id == "accounts");
        Assert.Contains(menu, e => e.IsLeaf && e.Id == "journals");
        Assert.DoesNotContain(menu, e => e.Id == "trial-balance");
        var reports = Assert.Single(menu, e => e.Id == "sub:accounting:reports");
        Assert.False(reports.IsLeaf);
        Assert.Equal(["account-ledger", "currency-balance", "financial-reports", "general-journal", "trial-balance"], reports.Children.Select(c => c.Id).Order(StringComparer.Ordinal));
        var tools = Assert.Single(menu, e => e.Id == "sub:accounting:tools");
        Assert.Equal(["fx-revaluation", "posting-management"], tools.Children.Select(c => c.Id).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void EveryAuthorizedItemAppearsExactlyOnceInItsNestedMenu()
    {
        var all = NavigationService.Routes.Select(r => r.Permission).OfType<string>().Distinct().ToArray();
        var nav = Navigation(all);
        foreach (var group in nav.Items.Select(i => i.Group).Distinct().Where(g => g != "general" && g != "settings"))
        {
            var leaves = Flatten(nav.MenuFor(group)).Select(e => e.Id).Order(StringComparer.Ordinal).ToArray();
            Assert.Equal(nav.Items.Where(i => i.Group == group).Select(i => i.Id).Order(StringComparer.Ordinal), leaves);
        }
    }

    [Fact]
    public void EmptySubMenusNeverAppearAndUnauthorizedUsersSeeNothing()
    {
        Assert.Empty(Navigation().AccountingMenu);
        var onlyCards = Navigation("Ledger.CostCenters.View").AccountingMenu; // only a direct item (cost centres) — no reports/tools sub-menu is authorised
        Assert.All(onlyCards, e => Assert.True(e.IsLeaf));
    }

    private static IEnumerable<MenuEntry> Flatten(IEnumerable<MenuEntry> entries) =>
        entries.SelectMany(e => e.IsLeaf ? [e] : Flatten(e.Children));
}
