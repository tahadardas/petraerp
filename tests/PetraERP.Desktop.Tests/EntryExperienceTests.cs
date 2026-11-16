using System.Collections.ObjectModel;
using System.Globalization;
using PetraERP.ApiClient;
using PetraERP.Desktop.Controls;
using PetraERP.Desktop.Purchases;
using PetraERP.Desktop.Sales;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Tests;

public sealed class EntryExperienceTests
{
    [Fact]
    public void LookupProjectionReadsCurrentCollectionAfterScopeClearAndReload()
    {
        var rows = new ObservableCollection<ItemLookupDto> { new() { Id = Guid.NewGuid(), Code = "M1", NameArabic = "إسمنت", NameEnglish = "Cement" } };
        var converted = Assert.IsAssignableFrom<IEnumerable<LookupItem>>(new EntryLookupConverter().Convert(rows, typeof(object), null!, CultureInfo.InvariantCulture));
        Assert.Equal("إسمنت", Assert.Single(LookupFilter.Filter(converted, "إس")).NameArabic);
        rows.Clear(); Assert.Empty(converted);
        rows.Add(new() { Id = Guid.NewGuid(), Code = "M2", NameArabic = "رمل", NameEnglish = "Sand" });
        Assert.Empty(LookupFilter.Filter(converted, "M1")); Assert.Equal(rows[0].Id, Assert.Single(LookupFilter.Filter(converted, "sand")).Id);
        Assert.Equal("نمط", new LookupItem(Guid.NewGuid(), "", "نمط", null).Display(true));
        var account = new LookupItem(Guid.NewGuid(), "1101", "الزبائن", "Customers");
        var accounts = Assert.IsAssignableFrom<IEnumerable<LookupItem>>(new EntryLookupConverter().Convert(new[] { account }, typeof(object), null!, CultureInfo.InvariantCulture));
        Assert.Same(account, Assert.Single(accounts));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PatternShortcutRequiresPermissionAndKeepsInvoiceDraft(bool allowed)
    {
        var fixture = new ShellFixture();
        fixture.User.Permissions = ["Sales.Invoices.View", "Sales.Invoices.Manage", .. (allowed ? new[] { "Patterns.Documents.View" } : [])];
        using var shell = fixture.Create(new AdministrationFixture());
        shell.UserName = "admin"; shell.Password = "test"; await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);
        await shell.NavigateToAsync("sales-invoice-card", new NewSalesInvoiceCardRequest()).ConfigureAwait(true);
        var card = Assert.IsType<SalesInvoiceCardViewModel>(shell.Documents.Active!.Content);
        card.Lines[0].Notes = "مسودة محفوظة في البطاقة";
        Assert.Equal(allowed, card.CanViewPatterns); Assert.Equal(allowed, card.ManagePatternsCommand.CanExecute(null));
        await card.ManagePatternsCommand.ExecuteAsync(null).ConfigureAwait(true);
        if (allowed) Assert.Same(shell.Pages.Patterns, shell.CurrentView);
        else Assert.Same(card, shell.CurrentView);
        Assert.Equal("مسودة محفوظة في البطاقة", card.Lines[0].Notes); Assert.True(card.HasUnsavedChanges);
    }

    [Fact]
    public async Task PurchaseListActionsAskBeforeReplacingEditorAndOpenDetailsTab()
    {
        var fixture = new ShellFixture(); fixture.User.Permissions = ["Purchases.Invoices.View", "Purchases.Invoices.Manage"];
        var data = new PurchaseFixture(); var id = Guid.NewGuid();
        data.Details[id] = new() { Id = id, Status = PurchaseInvoiceStatus.Draft, Version = "AAAAAAAAB9E=", Lines = [] };
        using var shell = fixture.Create(new AdministrationFixture(), purchases: data);
        shell.UserName = "admin"; shell.Password = "test"; await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);
        await shell.NavigateToAsync("purchase-invoices", new NewPurchaseRequest()).ConfigureAwait(true);
        var vm = shell.Pages.Purchases!; Assert.Equal(50, vm.Lines.Count); var line = vm.Lines[0]; line.UnitCost = "99";
        fixture.Confirm = false;
        await vm.NewCommand.ExecuteAsync(null).ConfigureAwait(true); Assert.Same(line, vm.Lines[0]);
        vm.WorkspaceTab = 0; vm.SelectedInvoice = new() { Id = id };
        await vm.OpenCommand.ExecuteAsync(null).ConfigureAwait(true); Assert.Null(vm.Current); Assert.Same(line, vm.Lines[0]);
        Assert.Equal("D_ConfirmDiscardChanges", fixture.LastConfirmation);
        fixture.Confirm = true;
        await vm.OpenCommand.ExecuteAsync(null).ConfigureAwait(true); Assert.Equal(id, vm.Current!.Id); Assert.Equal(1, vm.WorkspaceTab);
    }

    [Fact]
    public async Task PostedSalesLineActionsStayReadOnly()
    {
        var fixture = new ShellFixture(); fixture.User.Permissions = ["Sales.Invoices.View", "Sales.Invoices.Manage"];
        var id = Guid.NewGuid(); var data = new AccountingFixture { SalesDetail = new()
        {
            Id = id, Status = SalesInvoiceStatus.Posted, Version = "AAAAAAAAB9E=", Lines = [new() { Id = Guid.NewGuid(), ItemId = Guid.NewGuid(), Quantity = "1", UnitPrice = "10" }],
        } };
        using var shell = fixture.Create(new AdministrationFixture(), accounting: data);
        shell.UserName = "admin"; shell.Password = "test"; await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);
        await shell.NavigateToAsync("sales-invoice-card", new OpenSalesInvoiceCardRequest(id)).ConfigureAwait(true);
        var vm = Assert.IsType<SalesInvoiceCardViewModel>(shell.CurrentView); var line = Assert.Single(vm.Lines);
        vm.SelectedLine = line;
        Assert.True(vm.IsViewMode); Assert.False(vm.AddLineCommand.CanExecute(null)); Assert.False(vm.RemoveLineCommand.CanExecute(line));
        vm.RemoveLine(line); Assert.Same(line, Assert.Single(vm.Lines));
    }
}
