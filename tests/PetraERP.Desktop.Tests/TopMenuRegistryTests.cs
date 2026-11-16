using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Tests;

/// <summary>
/// The top menu has one description (<see cref="MenuRegistry"/>): reference order, no route listed twice, every real route
/// reachable, permissions identical to the navigation table, and nothing unimplemented or unauthorized shown.
/// </summary>
public sealed class TopMenuRegistryTests
{
    private static string[] AllPermissions() =>
        [.. NavigationService.Routes.Select(route => route.Permission).OfType<string>().Distinct(StringComparer.Ordinal), "Ledger.Journals.Manage"];

    private static NavigationService Navigation(string[] permissions, string language = "ar")
    {
        var navigation = new NavigationService(new LocalizationService(language));
        navigation.Configure(permissions);
        return navigation;
    }

    private static IEnumerable<TopMenuNode> Flatten(IEnumerable<TopMenuNode> nodes) =>
        nodes.SelectMany(node => new[] { node }.Concat(Flatten(node.Children)));

    [Fact]
    public void RootsFollowTheAlAmeenOrderAndHideUnimplementedModules()
    {
        var roots = Navigation(AllPermissions()).TopMenu.Select(node => node.Id).ToArray();
        Assert.Equal(["file", "invoices", "vouchers", "parties", "inventory", "accounts", "financial", "cost-centers", "window", "maintenance", "tools"], roots);
        Assert.DoesNotContain("pos", roots);
        Assert.DoesNotContain("help", roots);
    }

    [Fact]
    public void RootCaptionsAreTheReferenceArabicNames()
    {
        var captions = Navigation(AllPermissions()).TopMenu.Select(node => node.Label).ToArray();
        Assert.Equal(["ملف", "فاتورة", "سندات", "زبائن", "مواد", "حسابات", "مالية", "مراكز", "إطار", "صيانة الملفات", "أدوات"], captions);
    }

    [Fact]
    public void EveryRegistryRouteExistsInTheNavigationTableWithTheSamePermission()
    {
        var routes = NavigationService.Routes.ToDictionary(route => route.Id, route => route.Permission, StringComparer.Ordinal);
        foreach (var node in MenuRegistry.AllNodes.Where(node => node.Route is not null))
        {
            Assert.True(routes.ContainsKey(node.Route!), $"{node.Id} points to unknown route {node.Route}");
            Assert.Equal(routes[node.Route!], node.RequiredPermission);
        }
    }

    [Fact]
    public void NoRouteAppearsTwiceAndEveryNavigationRouteIsReachableFromTheMenu()
    {
        var routes = MenuRegistry.AllNodes.Where(node => node.Route is not null).Select(node => node.Route!).ToArray();
        Assert.Equal(routes.Length, routes.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(NavigationService.Routes.Select(route => route.Id).Order(StringComparer.Ordinal), routes.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void NodeIdsAreUniqueAndEveryParentExists()
    {
        var ids = MenuRegistry.AllNodes.Select(node => node.Id).ToArray();
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
        foreach (var node in MenuRegistry.AllNodes.Where(node => node.ParentId is not null))
            Assert.Contains(node.ParentId, ids);
    }

    [Fact]
    public void AGroupNeverCarriesARouteOrACommand()
    {
        foreach (var node in MenuRegistry.AllNodes)
        {
            var isGroup = MenuRegistry.AllNodes.Any(other => other.ParentId == node.Id);
            if (isGroup) Assert.True(node.Route is null && node.Command is null, $"{node.Id} is a group");
            else if (node.IsImplemented) Assert.True(node.Route is not null || node.Command is not null, $"{node.Id} does nothing");
        }
    }

    [Fact]
    public void CurrenciesAreASubMenuUnderAccounts()
    {
        var accounts = Assert.Single(Navigation(AllPermissions()).TopMenu, node => node.Id == "accounts");
        var currencies = Assert.Single(accounts.Children, node => node.Id == "accounts.currencies-group");
        Assert.Equal(["currencies", "rate-bulletin", "fx-revaluation"], currencies.Children.Select(node => node.Route).ToArray());
    }

    [Fact]
    public void UnauthorizedUsersSeeOnlyShellCommandsAndHome()
    {
        var nodes = Flatten(Navigation([]).TopMenu).ToArray();
        Assert.Equal(["home"], nodes.Where(node => node.Route is not null).Select(node => node.Route).ToArray());
        Assert.DoesNotContain(nodes, node => node.Id == "file.new-journal");
        Assert.Contains(nodes, node => node.Id == "window.cascade");
    }

    [Fact]
    public void NewJournalNeedsTheManagePermissionAndTheJournalsRoute()
    {
        Assert.DoesNotContain(Flatten(Navigation(["Ledger.Journals.View"]).TopMenu), node => node.Id == "file.new-journal");
        Assert.Contains(Flatten(Navigation(["Ledger.Journals.View", "Ledger.Journals.Manage"]).TopMenu), node => node.Id == "file.new-journal");
    }

    [Fact]
    public void ARouteIsInTheMenuExactlyWhenTheUserMayOpenIt()
    {
        var navigation = Navigation(["Sales.Invoices.View", "Ledger.Reports.View"]);
        var routes = Flatten(navigation.TopMenu).Where(node => node.Route is not null).Select(node => node.Route!).ToArray();
        Assert.Equal(navigation.Items.Select(item => item.Id).Order(StringComparer.Ordinal), routes.Where(route => navigation.Allows(route)).Order(StringComparer.Ordinal));
        Assert.All(routes, route => Assert.True(navigation.Allows(route)));
        Assert.DoesNotContain("purchase-invoices", routes);
    }

    [Fact]
    public void LiveListNodesAreMarkedForTheBuilderAndLanguageChangesRelocalizeTheBar()
    {
        var localization = new LocalizationService("ar");
        var navigation = new NavigationService(localization);
        navigation.Configure(AllPermissions());
        var arabic = navigation.TopMenu.Select(node => node.Label).ToArray();
        Assert.Contains(Flatten(navigation.TopMenu), node => node.Command == MenuRegistry.OpenWindowsCommand);
        Assert.Contains(Flatten(navigation.TopMenu), node => node.Command == MenuRegistry.SkinListCommand);

        localization.SetLanguage("en");
        Assert.NotEqual(arabic, navigation.TopMenu.Select(node => node.Label).ToArray());
    }
}
