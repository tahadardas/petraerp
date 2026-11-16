using System.ComponentModel;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Tests;

/// <summary>
/// Unit and WPF tests for Prompt 01: Top horizontal menus for Purchases and Treasury,
/// property change notifications for SalesItems/HasSales/Purchases/Treasury, and the structured MenuRegistry.
/// </summary>
public sealed class DesktopMenuNavigationTests
{
    private static readonly string[] PurchaseOnly = ["Purchases.Invoices.View"];
    private static readonly string[] TreasuryOnly = ["Treasury.Vouchers.View"];
    private static readonly string[] SalesOnly = ["Sales.Invoices.View"];

    [Fact]
    public void AuthorizedPurchaseOnlyUserSeesPurchasesButNotTreasuryOrSales()
    {
        var loc = new LocalizationService("ar");
        var nav = new NavigationService(loc);
        nav.Configure(PurchaseOnly);

        Assert.True(nav.HasPurchases);
        Assert.Single(nav.PurchasesItems);
        Assert.Equal("purchase-invoices", nav.PurchasesItems[0].Id);

        Assert.False(nav.HasTreasury);
        Assert.Empty(nav.TreasuryItems);

        Assert.False(nav.HasSales);
        Assert.Empty(nav.SalesItems);

        Assert.True(nav.Allows("purchase-invoices"));
        Assert.False(nav.Allows("treasury-vouchers"));
        Assert.False(nav.Allows("sales-invoices"));
    }

    [Fact]
    public void AuthorizedTreasuryOnlyUserSeesTreasuryButNotPurchasesOrSales()
    {
        var loc = new LocalizationService("ar");
        var nav = new NavigationService(loc);
        nav.Configure(TreasuryOnly);

        Assert.True(nav.HasTreasury);
        // Commercial papers (cheques) share the voucher view permission since the gap-closure phase.
        Assert.Equal(["treasury-vouchers", "cheques", "bank-reconciliation"], nav.TreasuryItems.Select(item => item.Id));

        Assert.False(nav.HasPurchases);
        Assert.Empty(nav.PurchasesItems);

        Assert.False(nav.HasSales);
        Assert.Empty(nav.SalesItems);

        Assert.True(nav.Allows("treasury-vouchers"));
        Assert.False(nav.Allows("purchase-invoices"));
    }

    [Fact]
    public void UnauthorizedUserSeesNeitherPurchasesNorTreasuryNorSales()
    {
        var loc = new LocalizationService("ar");
        var nav = new NavigationService(loc);
        nav.Configure([]);

        Assert.False(nav.HasPurchases);
        Assert.Empty(nav.PurchasesItems);
        Assert.False(nav.HasTreasury);
        Assert.Empty(nav.TreasuryItems);
        Assert.False(nav.HasSales);
        Assert.Empty(nav.SalesItems);
        Assert.False(nav.HasAccounting);
        Assert.Empty(nav.AccountingItems);

        Assert.Single(nav.Items); // Home is permitted for any authenticated user
        Assert.Equal("home", nav.Items[0].Id);
    }

    [Fact]
    public void PropertyChangedNotifiesSalesItemsHasSalesPurchasesAndTreasuryOnConfigureAndReset()
    {
        var loc = new LocalizationService("ar");
        var nav = new NavigationService(loc);
        var changedProperties = new HashSet<string>(StringComparer.Ordinal);
        nav.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is not null) changedProperties.Add(e.PropertyName);
        };

        nav.Configure(["Sales.Invoices.View", "Purchases.Invoices.View", "Treasury.Vouchers.View"]);

        Assert.Contains(nameof(nav.SalesItems), changedProperties);
        Assert.Contains(nameof(nav.HasSales), changedProperties);
        Assert.Contains(nameof(nav.PurchasesItems), changedProperties);
        Assert.Contains(nameof(nav.HasPurchases), changedProperties);
        Assert.Contains(nameof(nav.TreasuryItems), changedProperties);
        Assert.Contains(nameof(nav.HasTreasury), changedProperties);

        changedProperties.Clear();
        nav.Reset();

        Assert.Contains(nameof(nav.SalesItems), changedProperties);
        Assert.Contains(nameof(nav.HasSales), changedProperties);
        Assert.Contains(nameof(nav.PurchasesItems), changedProperties);
        Assert.Contains(nameof(nav.HasPurchases), changedProperties);
        Assert.Contains(nameof(nav.TreasuryItems), changedProperties);
        Assert.Contains(nameof(nav.HasTreasury), changedProperties);
        Assert.False(nav.HasPurchases);
        Assert.False(nav.HasTreasury);
        Assert.False(nav.HasSales);
    }

    [Fact]
    public void LanguageChangeRelocalizesNavigationItems()
    {
        var loc = new LocalizationService("ar");
        var nav = new NavigationService(loc);
        nav.Configure(["Purchases.Invoices.View", "Treasury.Vouchers.View"]);

        loc.SetLanguage("ar");
        var arPurchase = nav.PurchasesItems[0].Label;
        var arTreasury = nav.TreasuryItems[0].Label;

        loc.SetLanguage("en");
        var enPurchase = nav.PurchasesItems[0].Label;
        var enTreasury = nav.TreasuryItems[0].Label;

        Assert.NotEqual(arPurchase, enPurchase);
        Assert.NotEqual(arTreasury, enTreasury);
    }

    [Fact]
    public void MenuRegistryEncapsulatesAmeenHierarchyAndHidesSpecOnlyFutureNodes()
    {
        var rootGroups = MenuRegistry.RootGroups.Select(g => g.Id).ToArray();
        Assert.Contains("file", rootGroups);
        Assert.Contains("invoices", rootGroups);
        Assert.Contains("vouchers", rootGroups);
        Assert.Contains("parties", rootGroups);
        Assert.Contains("inventory", rootGroups);
        Assert.Contains("accounts", rootGroups);
        Assert.Contains("financial", rootGroups);
        Assert.Contains("cost-centers", rootGroups);
        Assert.Contains("pos", rootGroups);
        Assert.Contains("window", rootGroups);
        Assert.Contains("tools", rootGroups);
        Assert.Contains("help", rootGroups);

        // Spec-only future nodes must never appear in production UI visible children
        var allPermissions = new[] { "Ledger.Accounts.View", "Sales.Invoices.View", "Purchases.Invoices.View", "Treasury.Vouchers.View" };
        var financialChildren = MenuRegistry.GetVisibleChildren("financial", allPermissions).ToArray();
        Assert.Empty(financialChildren);

        var posChildren = MenuRegistry.GetVisibleChildren("pos", allPermissions).ToArray();
        Assert.Empty(posChildren);

        var helpChildren = MenuRegistry.GetVisibleChildren("help", allPermissions).ToArray();
        Assert.Empty(helpChildren);

        // Implemented sections return their authorized nodes
        var voucherChildren = MenuRegistry.GetVisibleChildren("vouchers", allPermissions).Select(c => c.Route).ToArray();
        Assert.Contains("treasury-vouchers", voucherChildren);

        var invoiceChildren = MenuRegistry.GetVisibleChildren("invoices", allPermissions).Select(c => c.Route).ToArray();
        Assert.Contains("sales-invoices", invoiceChildren);
        Assert.Contains("purchase-invoices", invoiceChildren);
    }

    [Fact]
    public async Task DesktopViewModelNavigatesToPurchasesAndTreasuryRoutes()
    {
        var fixture = new ShellFixture();
        fixture.User.Permissions = ["Purchases.Invoices.View", "Treasury.Vouchers.View", "Sales.Invoices.View"];
        using var shell = fixture.Create(new AdministrationFixture());
        shell.UserName = "admin";
        shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.True(shell.Navigation.HasPurchases);
        Assert.True(shell.Navigation.HasTreasury);
        Assert.True(shell.Navigation.HasSales);

        await shell.NavigateToAsync("purchase-invoices", null).ConfigureAwait(true);
        Assert.Same(shell.Pages.Purchases, shell.CurrentView);
        Assert.Empty(shell.Operation.Error);

        await shell.NavigateToAsync("treasury-vouchers", null).ConfigureAwait(true);
        Assert.Same(shell.Pages.Vouchers, shell.CurrentView);
        Assert.Empty(shell.Operation.Error);
    }

    [Theory]
    [InlineData("home")]
    [InlineData("accounts")]
    [InlineData("cost-centers")]
    [InlineData("journals")]
    [InlineData("general-journal")]
    [InlineData("account-ledger")]
    [InlineData("trial-balance")]
    [InlineData("customer-statement")]
    [InlineData("supplier-statement")]
    [InlineData("periods")]
    [InlineData("parties")]
    [InlineData("items")]
    [InlineData("units")]
    [InlineData("inventory-operations")]
    [InlineData("stocktaking")]
    [InlineData("sales-invoices")]
    [InlineData("treasury-vouchers")]
    [InlineData("cheques")]
    [InlineData("purchase-invoices")]
    [InlineData("financial-reports")]
    [InlineData("fx-revaluation")]
    [InlineData("users")]
    [InlineData("roles")]
    [InlineData("audit")]
    [InlineData("patterns")]
    [InlineData("currencies")]
    public void EveryImplementedNavigationRouteHasValidDestinationAndRegistration(string route)
    {
        var registeredRoute = NavigationService.Routes.FirstOrDefault(r => r.Id == route);
        Assert.NotNull(registeredRoute.Id);

        var menuNodes = MenuRegistry.AllNodes.Where(n => n.Route == route).ToArray();
        foreach (var node in menuNodes)
        {
            Assert.True(node.IsImplemented, $"Route {route} on node {node.Id} should be implemented.");
            if (node.RequiredPermission is not null)
            {
                Assert.Equal(registeredRoute.Permission, node.RequiredPermission);
            }
        }
    }

    [Theory]
    [InlineData("sales-invoices", "Sales.Invoices.View")]
    [InlineData("purchase-invoices", "Purchases.Invoices.View")]
    [InlineData("treasury-vouchers", "Treasury.Vouchers.View")]
    [InlineData("accounts", "Ledger.Accounts.View")]
    [InlineData("cost-centers", "Ledger.CostCenters.View")]
    [InlineData("journals", "Ledger.Journals.View")]
    [InlineData("general-journal", "Ledger.Reports.View")]
    [InlineData("account-ledger", "Ledger.Reports.View")]
    [InlineData("trial-balance", "Ledger.Reports.View")]
    [InlineData("parties", "Parties.Directory.View")]
    [InlineData("items", "Inventory.Items.View")]
    [InlineData("patterns", "Patterns.Documents.View")]
    [InlineData("users", "System.Users.View")]
    [InlineData("roles", "System.Roles.View")]
    [InlineData("audit", "Audit.Security.View")]
    public async Task ImplementedRoutesNavigateSuccessfullyWhenAuthorizedAndRejectWhenUnauthorized(string route, string permission)
    {
        var loc = new LocalizationService("ar");
        var nav = new NavigationService(loc);
        nav.Configure([]);
        Assert.False(nav.Allows(route));

        nav.Configure([permission]);
        Assert.True(nav.Allows(route));

        var fixture = new ShellFixture();
        fixture.User.Permissions = [permission];
        using var shell = fixture.Create(new AdministrationFixture());
        shell.UserName = "admin";
        shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);

        await shell.NavigateToAsync(route, null).ConfigureAwait(true);
        // A report route may open in its own window, so success is the document existing rather than the tab host showing it.
        Assert.Contains(shell.Documents.Documents, document => document.Route == route);
        Assert.Empty(shell.Operation.Error);
    }
}
