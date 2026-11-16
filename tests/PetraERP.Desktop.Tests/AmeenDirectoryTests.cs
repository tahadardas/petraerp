using PetraERP.ApiClient;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Tests;

/// <summary>The Al-Ameen directories: group tree, cards of the selected group, search, and opening the card.</summary>
public sealed class AmeenDirectoryTests
{
    private static async Task<(AccountingFixture Accounting, DesktopViewModel Shell)> StartAsync()
    {
        var fixture = new ShellFixture();
        fixture.User.Permissions = ["Ledger.Accounts.View", "Inventory.Items.View", "Inventory.Movements.View"];
        var accounting = new AccountingFixture();
        var shell = fixture.Create(new AdministrationFixture(), accounting: accounting);
        shell.UserName = "admin";
        shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);
        return (accounting, shell);
    }

    [Fact]
    public async Task AccountDirectoryListsControlAccountsInTheTreeAndTheirChildrenAsCards()
    {
        var (accounting, shell) = await StartAsync().ConfigureAwait(true);
        using var _ = shell;
        var root = new AccountResponse { Id = Guid.NewGuid(), Code = "1", NameArabic = "الأصول", PostingAllowed = false, IsActive = true };
        var cash = new AccountResponse { Id = Guid.NewGuid(), Code = "11", NameArabic = "الصندوق", PostingAllowed = true, ParentId = root.Id, IsActive = true };
        var bank = new AccountResponse { Id = Guid.NewGuid(), Code = "12", NameArabic = "المصرف", PostingAllowed = true, ParentId = root.Id, IsActive = true };
        accounting.Accounts.AddRange([root, cash, bank]);

        await shell.NavigateToAsync("account-directory", null).ConfigureAwait(true);
        var page = Assert.IsType<AccountDirectoryViewModel>(shell.Documents.Documents.Single(d => d.Route == "account-directory").Content);

        var node = Assert.Single(page.Roots);
        Assert.Equal("1-الأصول", node.Caption);
        Assert.Empty(node.Children);
        Assert.Equal(["1"], page.Rows.Select(r => r.Code));

        page.SelectedNode = node;
        await Task.Delay(100, TestContext.Current.CancellationToken).ConfigureAwait(true);
        Assert.Equal(["11", "12"], page.Rows.Select(r => r.Code));

        page.SearchText = "مصرف";
        await Task.Delay(100, TestContext.Current.CancellationToken).ConfigureAwait(true);
        Assert.Equal(["12"], page.Rows.Select(r => r.Code));

        page.SelectedRow = page.Rows[0];
        Assert.True(page.OpenCommand.CanExecute(null));
    }

    [Fact]
    public async Task ItemDirectoryBuildsTheGroupTree()
    {
        var (accounting, shell) = await StartAsync().ConfigureAwait(true);
        using var _ = shell;
        var parent = new ItemGroupDto { Id = Guid.NewGuid(), Code = "01", NameArabic = "اسمنت", IsActive = true };
        var child = new ItemGroupDto { Id = Guid.NewGuid(), Code = "011", NameArabic = "بيتون", ParentId = parent.Id, IsActive = true };
        accounting.ItemGroups.AddRange([parent, child]);

        await shell.NavigateToAsync("item-directory", null).ConfigureAwait(true);
        var page = Assert.IsType<ItemDirectoryViewModel>(shell.Documents.Documents.Single(d => d.Route == "item-directory").Content);

        var root = Assert.Single(page.Roots);
        Assert.Equal("01-اسمنت", root.Caption);
        Assert.Equal("011-بيتون", Assert.Single(root.Children).Caption);
    }
}

/// <summary>The Al-Ameen pattern manager: families of patterns that open the pattern list narrowed to that family.</summary>
public sealed class AmeenPatternManagerTests
{
    [Fact]
    public async Task EachFamilyOpensThePatternListNarrowedToIt()
    {
        var fixture = new ShellFixture();
        fixture.User.Permissions = ["Patterns.Documents.View", "Patterns.Documents.Manage"];
        var shell = fixture.Create(new AdministrationFixture(), accounting: new AccountingFixture());
        using var _ = shell;
        shell.UserName = "admin";
        shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);

        await shell.NavigateToAsync("pattern-manager", null).ConfigureAwait(true);
        var manager = Assert.IsType<PetraERP.Desktop.Patterns.PatternManagerViewModel>(shell.Documents.Documents.Single(d => d.Route == "pattern-manager").Content);
        Assert.Equal(["invoices", "vouchers", "cash", "inventory"], manager.Categories.Select(c => c.Id));

        manager.SelectedCategory = manager.Categories.Single(c => c.Id == "cash");
        await manager.EditCommand.ExecuteAsync(null).ConfigureAwait(true);

        Assert.Equal("cash", shell.Pages.Patterns.FilterTab);
        Assert.True(manager.NewCommand.CanExecute(null));
    }
}

/// <summary>"حدود المادة وجردها": stock against the minimum and maximum of every item.</summary>
public sealed class AmeenItemLimitsTests
{
    [Fact]
    public async Task ItemLimitsFlagItemsBelowMinimumAndAboveMaximum()
    {
        var fixture = new ShellFixture();
        fixture.User.Permissions = ["Inventory.Items.View", "Inventory.Movements.View"];
        var accounting = new AccountingFixture();
        var shell = fixture.Create(new AdministrationFixture(), accounting: accounting);
        using var _ = shell;
        shell.UserName = "admin";
        shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);
        accounting.Items.AddRange([
            new() { Id = Guid.NewGuid(), Code = "A", NameArabic = "منخفضة", CurrentStock = "2", MinStock = "5", MaxStock = "50", IsActive = true },
            new() { Id = Guid.NewGuid(), Code = "B", NameArabic = "طبيعية", CurrentStock = "20", MinStock = "5", MaxStock = "50", IsActive = true },
            new() { Id = Guid.NewGuid(), Code = "C", NameArabic = "مرتفعة", CurrentStock = "90", MinStock = "5", MaxStock = "50", IsActive = true },
        ]);

        await shell.NavigateToAsync("item-limits", null).ConfigureAwait(true);
        var page = Assert.IsType<ItemLimitsViewModel>(shell.Documents.Documents.Single(d => d.Route == "item-limits").Content);

        Assert.Equal(["A", "B", "C"], page.Rows.Select(r => r.Code));
        Assert.Equal(1, page.BelowMinimumCount);
        Assert.Equal(1, page.AboveMaximumCount);
        page.Filter = "below";
        Assert.Equal(["A"], page.Rows.Select(r => r.Code));
        page.Filter = "above";
        Assert.Equal(["C"], page.Rows.Select(r => r.Code));
    }
}

/// <summary>"تعديل أسعار المواد": preview first, then apply after a confirmation.</summary>
public sealed class AmeenItemPriceToolTests
{
    [Fact]
    public async Task ApplyNeedsAPreviewAndAConfirmationAndSendsTheSameRequest()
    {
        var fixture = new ShellFixture();
        fixture.User.Permissions = ["Inventory.Items.View", "Inventory.Items.Manage"];
        var accounting = new AccountingFixture();
        var shell = fixture.Create(new AdministrationFixture(), accounting: accounting);
        using var _ = shell;
        shell.UserName = "admin";
        shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);
        await shell.NavigateToAsync("item-price-tool", null).ConfigureAwait(true);
        var page = Assert.IsType<ItemPriceToolViewModel>(shell.Documents.Documents.Single(d => d.Route == "item-price-tool").Content);

        Assert.False(page.PreviewCommand.CanExecute(null));
        page.PercentText = "10";
        page.PriceType = "WHOLESALE";
        Assert.True(page.PreviewCommand.CanExecute(null));
        Assert.False(page.ApplyCommand.CanExecute(null));

        await page.PreviewCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.True(page.ApplyCommand.CanExecute(null));
        Assert.Contains("5", page.StatusText, StringComparison.Ordinal);

        fixture.Confirm = false;
        await page.ApplyCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Single(accounting.PriceAdjustments);

        fixture.Confirm = true;
        await page.ApplyCommand.ExecuteAsync(null).ConfigureAwait(true);
        var applied = accounting.PriceAdjustments[^1];
        Assert.Equal(2, accounting.PriceAdjustments.Count);
        Assert.False(applied.PreviewOnly);
        Assert.Equal("10", applied.Percent);
        Assert.Equal("WHOLESALE", applied.PriceListCode);

        page.PercentText = "0";
        Assert.False(page.PreviewCommand.CanExecute(null));
    }
}
