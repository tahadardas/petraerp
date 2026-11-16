using PetraERP.ApiClient;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Sales;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Tests;

public sealed class SalesDocumentLinkTests
{
    [Fact]
    public async Task DocumentFactoriesConnectCustomerItemJournalAndMovementToOneInvoiceWithoutOverwritingAnOpenItemDraft()
    {
        var fixture = new ShellFixture(); fixture.User.Permissions = PetraERP.Application.Security.PermissionCatalog.All.Select(p => p.Code).ToArray();
        var accounting = new AccountingFixture(); var links = new LinksFixture(); var ledger = new FakeLedgerClient();
        var journal = ledger.Add(fixture.Company.Id, fixture.Branch.Id, JournalState.Posted);
        var invoiceId = Guid.NewGuid(); var partyId = Guid.NewGuid();
        accounting.Parties.Add(new() { Id = partyId, Code = "C1", NameArabic = "زبون عربي", IsActive = true });
        accounting.Items.Add(new() { Id = links.ItemId, Code = "I1", NameArabic = "مادة", IsActive = true });
        accounting.SalesDetail = new() { Id = invoiceId, CustomerId = partyId, JournalEntryId = journal.Id, Status = SalesInvoiceStatus.Posted,
            Lines = [new() { Id = Guid.NewGuid(), ItemId = links.ItemId, Quantity = "1.0000", UnitPrice = "10.0000", LineTotal = "10.0000" }] };
        using var shell = fixture.Create(new AdministrationFixture(), ledger, accounting: accounting, salesLinks: links);
        shell.UserName = "admin"; shell.Password = "test"; await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);
        await shell.NavigateToAsync("sales-invoice-card", new OpenSalesInvoiceCardRequest(invoiceId)).ConfigureAwait(true);
        var invoice = Assert.IsType<SalesInvoiceCardViewModel>(shell.CurrentView);
        await invoice.OpenCustomerCommand.ExecuteAsync(null).ConfigureAwait(true);
        var party = Assert.IsType<PetraERP.Desktop.Parties.PartyCardViewModel>(shell.CurrentView);
        await party.DocumentLinks!.RefreshCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Equal((SalesLinkSource.Customer, partyId), (links.Calls[^1].Item3, links.Calls[^1].Item4));
        invoice.SelectedLine = invoice.Lines[0]; await invoice.OpenItemCommand.ExecuteAsync(null).ConfigureAwait(true);
        var item = Assert.IsType<PetraERP.Desktop.Inventory.ItemCardViewModel>(shell.CurrentView);
        await item.ViewItemInvoicesCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Equal((SalesLinkSource.Item, links.ItemId), (links.Calls[^1].Item3, links.Calls[^1].Item4));
        await invoice.OpenJournalCommand.ExecuteAsync(null).ConfigureAwait(true);
        var journalCard = Assert.IsType<PetraERP.Desktop.GeneralLedger.GeneralLedgerViewModel>(shell.CurrentView);
        await journalCard.DocumentLinks!.RefreshCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Equal((SalesLinkSource.Journal, journal.Id), (links.Calls[^1].Item3, links.Calls[^1].Item4));
        await invoice.DocumentLinks!.RefreshCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Equal((SalesLinkSource.Invoice, invoiceId), (links.Calls[^1].Item3, links.Calls[^1].Item4));
        // Opening a movement focuses the existing item card's source browser and preserves its separate editor.
        item.BeginEditCommand.Execute(null); item.DraftNameArabic = "تعديل محفوظ في البطاقة";
        invoice.DocumentLinks.Selected = invoice.DocumentLinks.Items[0]; var movementId = invoice.DocumentLinks.Selected.Id!.Value;
        await invoice.DocumentLinks.OpenCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Same(item, shell.CurrentView); Assert.True(item.HasUnsavedChanges); Assert.Equal("تعديل محفوظ في البطاقة", item.DraftNameArabic);
        await item.DocumentLinks!.RefreshCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Equal((SalesLinkSource.Movement, movementId), (links.Calls[^1].Item3, links.Calls[^1].Item4));
        Assert.All(links.Calls, call => { Assert.Equal(fixture.Company.Id, call.Item1); Assert.Equal(fixture.Branch.Id, call.Item2); });
        Assert.Equal(1, shell.Documents.Documents.Count(d => d.Kind == WorkspaceDocumentKind.ItemCard));
    }

    [Fact]
    public async Task InvoiceCommandsFollowPersistedIdsAndRespectSelectionPermissionsAndEditMode()
    {
        var fixture = new ShellFixture(); var accounting = new AccountingFixture();
        var localization = new LocalizationService("ar"); var operation = new DesktopOperation(localization);
        var navigator = new ShellNavigator(); var visits = new List<(string Route, object? Request)>();
        navigator.Requested += (route, request) => { visits.Add((route, request)); return Task.CompletedTask; };
        using var card = new SalesInvoiceCardViewModel(accounting, accounting, accounting, accounting, accounting, navigator, operation, localization, fixture);
        var context = new ShellContext(fixture.Company, fixture.Branch, new HashSet<string>
            { "Sales.Invoices.View", "Sales.Invoices.Manage", "Parties.Directory.View", "Inventory.Items.View", "Ledger.Journals.View" });
        await card.ActivateAsync(context, new OpenSalesInvoiceCardRequest(Guid.NewGuid()), TestContext.Current.CancellationToken).ConfigureAwait(true);
        var customer = Guid.NewGuid(); var journal = Guid.NewGuid(); var item = Guid.NewGuid();
        card.Invoice = new() { Id = card.InvoiceId, CustomerId = customer, JournalEntryId = journal, Status = SalesInvoiceStatus.Draft };
        card.Lines.Add(new() { ItemId = item }); card.SelectedLine = card.Lines[0];
        await card.OpenCustomerCommand.ExecuteAsync(null).ConfigureAwait(true);
        await card.OpenItemCommand.ExecuteAsync(null).ConfigureAwait(true);
        await card.OpenJournalCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Equal(new OpenPartyCardRequest(customer), visits[0].Request); Assert.Equal("party-card", visits[0].Route);
        Assert.Equal(new OpenItemCardRequest(item), visits[1].Request); Assert.Equal("item-card", visits[1].Route);
        Assert.Equal(new OpenJournalRequest(journal), visits[2].Request); Assert.Equal("journals", visits[2].Route);
        card.SelectedLine = new() { ItemId = item }; Assert.False(card.OpenItemCommand.CanExecute(null));
        card.SelectedLine = card.Lines[0]; card.BeginEditCommand.Execute(null);
        Assert.False(card.OpenCustomerCommand.CanExecute(null)); Assert.False(card.OpenItemCommand.CanExecute(null)); Assert.False(card.OpenJournalCommand.CanExecute(null));
        await card.CancelCardCommand.ExecuteAsync(null).ConfigureAwait(true);
        await card.ActivateAsync(context with { Permissions = new HashSet<string> { "Sales.Invoices.View" } }, null, TestContext.Current.CancellationToken).ConfigureAwait(true);
        Assert.False(card.OpenCustomerCommand.CanExecute(null)); Assert.False(card.OpenItemCommand.CanExecute(null)); Assert.False(card.OpenJournalCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(SalesLinkSource.Customer)] [InlineData(SalesLinkSource.Item)] [InlineData(SalesLinkSource.Journal)] [InlineData(SalesLinkSource.Movement)] [InlineData(SalesLinkSource.Invoice)]
    public async Task LinkBrowserPagesAndNavigatesWithTypedIds(SalesLinkSource kind)
    {
        var fixture = new ShellFixture(); var client = new LinksFixture(); var navigator = new ShellNavigator();
        object? opened = null; string? route = null;
        navigator.Requested += (target, request) => { route = target; opened = request; return Task.CompletedTask; };
        var localization = new LocalizationService("ar");
        var vm = new SalesDocumentLinksViewModel(client, navigator, new(localization), localization);
        var scope = Scope(fixture); var sourceId = Guid.NewGuid(); vm.SetSource(scope, kind, sourceId);
        await vm.RefreshCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Equal((scope.CompanyId, scope.BranchId, kind, sourceId, 1), client.Calls[0]); Assert.Equal(50, vm.Items.Count);
        await vm.NextCommand.ExecuteAsync(null).ConfigureAwait(true); Assert.Equal(2, vm.Page); Assert.Single(vm.Items);
        vm.Selected = vm.Items[0]; await vm.OpenCommand.ExecuteAsync(null).ConfigureAwait(true);
        if (kind == SalesLinkSource.Invoice) { Assert.Equal("item-card", route); Assert.Equal(new OpenItemCardRequest(client.ItemId, vm.Selected.Id), opened); }
        else { Assert.Equal("sales-invoice-card", route); Assert.Equal(new OpenSalesInvoiceCardRequest(vm.Selected.Id!.Value), opened); }
        vm.Selected = new() { Id = Guid.NewGuid() }; Assert.False(vm.OpenCommand.CanExecute(null));
        await vm.PreviousCommand.ExecuteAsync(null).ConfigureAwait(true); Assert.Equal(1, vm.Page);
        vm.SetSource(null, kind, null); Assert.Empty(vm.Items); Assert.False(vm.RefreshCommand.CanExecute(null)); Assert.Null(vm.Selected);
    }

    [Fact]
    public async Task LateLinksAreDroppedAfterScopeChangeAndRevokedSourcePermissionClearsRows()
    {
        var fixture = new ShellFixture(); var client = new LinksFixture { Pending = new() };
        var localization = new LocalizationService("ar"); var vm = new SalesDocumentLinksViewModel(client, new(), new(localization), localization);
        var scope = Scope(fixture); var id = Guid.NewGuid(); vm.SetSource(scope, SalesLinkSource.Item, id);
        var pending = vm.RefreshCommand.ExecuteAsync(null);
        vm.SetSource(scope with { Branch = new() { Id = Guid.NewGuid(), CompanyId = fixture.Company.Id } }, SalesLinkSource.Item, id);
        client.Pending.SetResult(new() { Items = [new() { Id = Guid.NewGuid(), Description = "فرع سابق" }], TotalCount = 1, Page = 1 });
        await pending.ConfigureAwait(true); Assert.Empty(vm.Items); Assert.Equal(0, vm.TotalCount);
        client.Pending = null; vm.SetSource(scope, SalesLinkSource.Item, id); await vm.RefreshCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.NotEmpty(vm.Items);
        ((HashSet<string>)scope.Permissions).Remove("Inventory.Items.View");
        vm.SetSource(scope, SalesLinkSource.Item, id); Assert.Empty(vm.Items); Assert.False(vm.IsAvailable); Assert.False(vm.RefreshCommand.CanExecute(null));
    }

    internal static ShellContext Scope(ShellFixture fixture) => new(fixture.Company, fixture.Branch, new HashSet<string>
        { "Sales.Invoices.View", "Parties.Directory.View", "Inventory.Items.View", "Inventory.Movements.View", "Ledger.Journals.View" });

    internal sealed class LinksFixture : ISalesDocumentLinksClient
    {
        public Guid ItemId { get; } = Guid.NewGuid();
        public TaskCompletionSource<SalesDocumentLinkPageResponse>? Pending { get; set; }
        public List<(Guid, Guid, SalesLinkSource, Guid, int)> Calls { get; } = [];
        public Task<SalesDocumentLinkPageResponse> ListAsync(Guid companyId, Guid branchId, SalesLinkSource source, Guid sourceId, int page, int pageSize, CancellationToken cancellationToken)
        {
            Calls.Add((companyId, branchId, source, sourceId, page));
            return Pending?.Task ?? Task.FromResult(new SalesDocumentLinkPageResponse
            {
                Page = page, PageSize = pageSize, TotalCount = 51,
                Items = Enumerable.Range((page - 1) * pageSize, page == 1 ? 50 : 1).Select(index => new SalesDocumentLinkDto
                { Id = Guid.NewGuid(), ItemId = ItemId, Number = $"SI-{index}", Description = "فاتورة زبون عربي", Amount = "210.0000", CurrencyCode = "SYP", DateUtc = DateTimeOffset.UtcNow }).ToArray(),
            });
        }
    }
}
