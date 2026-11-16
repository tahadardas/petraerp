using PetraERP.ApiClient;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Relationships;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Tests;

public sealed class RelatedDocumentsTests
{
    [Theory]
    [InlineData(RelatedDocumentSource.SupplierPurchases)]
    [InlineData(RelatedDocumentSource.CustomerReceipts)]
    [InlineData(RelatedDocumentSource.SupplierPayments)]
    [InlineData(RelatedDocumentSource.InvoiceReceipts)]
    [InlineData(RelatedDocumentSource.JournalVouchers)]
    public async Task BrowsersPageServerResultsOpenStableIdsAndRejectForeignSelection(RelatedDocumentSource source)
    {
        var fixture = new ShellFixture(); var client = new RelatedFixture { Total = 51 };
        var navigator = new ShellNavigator(); object? request = null; string? route = null;
        navigator.Requested += (r, p) => { route = r; request = p; return Task.CompletedTask; };
        var loc = new LocalizationService("ar"); var vm = new RelatedDocumentsViewModel(client, navigator, new(loc), loc);
        var scope = Scope(fixture); var sourceId = Guid.NewGuid(); vm.SetSource(scope, source, sourceId);
        await vm.RefreshCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Equal((scope.CompanyId, scope.BranchId, source, sourceId, 1, 50), client.Calls[0]);
        Assert.Equal(50, vm.Items.Count); Assert.True(vm.NextCommand.CanExecute(null));
        await vm.NextCommand.ExecuteAsync(null).ConfigureAwait(true);
        vm.Selected = Assert.Single(vm.Items); await vm.OpenCommand.ExecuteAsync(null).ConfigureAwait(true);
        if (source == RelatedDocumentSource.SupplierPurchases)
        {
            Assert.Equal("purchase-invoices", route); Assert.Equal(new Purchases.OpenPurchaseRequest(vm.Selected.Id), request);
        }
        else { Assert.Equal("treasury-vouchers", route); Assert.Equal(new Treasury.OpenVoucherRequest(vm.Selected.Id), request); }
        vm.Selected = vm.Selected with { Id = Guid.NewGuid() }; Assert.False(vm.OpenCommand.CanExecute(null));
        await vm.PreviousCommand.ExecuteAsync(null).ConfigureAwait(true); Assert.Equal(1, vm.Page);
        vm.SetSource(null, source, null); Assert.Empty(vm.Items); Assert.False(vm.IsAvailable); Assert.False(vm.OpenCommand.CanExecute(null));
    }

    [Fact]
    public async Task SourceScopeAndPermissionChangesDiscardLateDataAndNeverLeaveSensitiveRows()
    {
        var fixture = new ShellFixture(); var client = new RelatedFixture { Total = 51, Pending = new() };
        var loc = new LocalizationService("ar"); var vm = new RelatedDocumentsViewModel(client, new(), new(loc), loc);
        var scope = Scope(fixture); var id = Guid.NewGuid(); vm.SetSource(scope, RelatedDocumentSource.CustomerReceipts, id);
        var pending = vm.RefreshCommand.ExecuteAsync(null);
        vm.SetSource(scope with { Branch = new() { Id = Guid.NewGuid() } }, RelatedDocumentSource.CustomerReceipts, id);
        client.Pending.SetResult(new(1, 1, [new(Guid.NewGuid(), "سند قديم", null, "1", "USD", "Posted")]));
        await pending.ConfigureAwait(true); Assert.Empty(vm.Items);
        client.Pending = null; vm.SetSource(scope, RelatedDocumentSource.CustomerReceipts, id);
        await vm.RefreshCommand.ExecuteAsync(null).ConfigureAwait(true); Assert.NotEmpty(vm.Items);
        ((HashSet<string>)scope.Permissions).Remove("Treasury.Vouchers.View");
        vm.SetSource(scope, RelatedDocumentSource.CustomerReceipts, id);
        Assert.False(vm.IsAvailable); Assert.Empty(vm.Items); Assert.False(vm.RefreshCommand.CanExecute(null));
    }

    [Fact]
    public async Task PartyInvoiceAndJournalFactoriesOwnIndependentRelatedBrowsersAndRetainSourceTabs()
    {
        var fixture = new ShellFixture(); fixture.User.Permissions = PetraERP.Application.Security.PermissionCatalog.All.Select(p => p.Code).ToArray();
        var accounting = new AccountingFixture(); var client = new RelatedFixture { Total = 1 };
        var supplier = Guid.NewGuid(); var customer = Guid.NewGuid(); var invoice = Guid.NewGuid();
        accounting.Parties.Add(new() { Id = supplier, Code = "S1", NameArabic = "مورد إسمنت", Role = PartyRole.Supplier, IsActive = true });
        accounting.Parties.Add(new() { Id = customer, Code = "C1", NameArabic = "زبون", Role = PartyRole.Customer, IsActive = true });
        accounting.SalesDetail = new() { Id = invoice, CustomerId = customer, Status = SalesInvoiceStatus.Posted, Lines = [] };
        var ledger = new FakeLedgerClient(); var journal = ledger.Add(fixture.Company.Id, fixture.Branch.Id, JournalState.Posted);
        var purchases = new PurchaseFixture(); var treasury = new TreasuryFixture();
        using var shell = fixture.Create(new AdministrationFixture(), ledger, accounting: accounting, relatedDocuments: client, purchases: purchases, treasury: treasury);
        shell.UserName = "admin"; shell.Password = "test"; await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);
        await shell.NavigateToAsync("party-card", new OpenPartyCardRequest(supplier)).ConfigureAwait(true);
        var card = Assert.IsType<Parties.PartyCardViewModel>(shell.CurrentView);
        Assert.True(card.PurchaseLinks!.IsAvailable); Assert.True(card.PaymentLinks!.IsAvailable); Assert.False(card.ReceiptLinks!.IsAvailable);
        await card.PurchaseLinks.RefreshCommand.ExecuteAsync(null).ConfigureAwait(true);
        card.PurchaseLinks.Selected = Assert.Single(card.PurchaseLinks.Items);
        purchases.Details[card.PurchaseLinks.Selected.Id] = new()
        {
            Id = card.PurchaseLinks.Selected.Id, SupplierId = supplier, Status = PurchaseInvoiceStatus.Posted, Lines = [],
        };
        await card.PurchaseLinks.OpenCommand.ExecuteAsync(null).ConfigureAwait(true);
        var purchase = Assert.IsType<Purchases.PurchasesViewModel>(shell.CurrentView);
        Assert.Equal(card.PurchaseLinks.Selected.Id, purchase.Current!.Id);
        await purchase.OpenSupplierCommand.ExecuteAsync(null).ConfigureAwait(true); Assert.Same(card, shell.CurrentView);
        Assert.Contains(shell.Documents.Documents, d => ReferenceEquals(d.Content, card));
        await shell.NavigateToAsync("party-card", new OpenPartyCardRequest(customer)).ConfigureAwait(true);
        var other = Assert.IsType<Parties.PartyCardViewModel>(shell.CurrentView);
        Assert.False(other.PurchaseLinks!.IsAvailable); Assert.True(other.ReceiptLinks!.IsAvailable); Assert.NotSame(card.ReceiptLinks, other.ReceiptLinks);
        other.BeginEditCommand.Execute(null); Assert.False(other.ReceiptLinks.IsAvailable);
        await other.CancelCardCommand.ExecuteAsync(null).ConfigureAwait(true); Assert.True(other.ReceiptLinks.IsAvailable);
        await shell.NavigateToAsync("sales-invoice-card", new OpenSalesInvoiceCardRequest(invoice)).ConfigureAwait(true);
        var sales = Assert.IsType<Sales.SalesInvoiceCardViewModel>(shell.CurrentView);
        await sales.ReceiptLinks!.RefreshCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Equal((RelatedDocumentSource.InvoiceReceipts, invoice), (client.Calls[^1].Source, client.Calls[^1].Id));
        sales.ReceiptLinks.Selected = Assert.Single(sales.ReceiptLinks.Items);
        treasury.Details[sales.ReceiptLinks.Selected.Id] = new() { Id = sales.ReceiptLinks.Selected.Id, Kind = VoucherKind.Receipt, PartyId = customer, Status = VoucherStatus.Posted };
        await sales.ReceiptLinks.OpenCommand.ExecuteAsync(null).ConfigureAwait(true);
        var receipt = Assert.IsType<Treasury.VouchersViewModel>(shell.CurrentView);
        Assert.Equal(sales.ReceiptLinks.Selected.Id, receipt.Current!.Id);
        await receipt.OpenPartyCommand.ExecuteAsync(null).ConfigureAwait(true); Assert.Same(other, shell.CurrentView);
        await shell.NavigateToAsync("journals", new OpenJournalRequest(journal.Id)).ConfigureAwait(true);
        var journalCard = Assert.IsType<GeneralLedger.GeneralLedgerViewModel>(shell.CurrentView);
        await journalCard.VoucherLinks!.RefreshCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Equal((RelatedDocumentSource.JournalVouchers, journal.Id), (client.Calls[^1].Source, client.Calls[^1].Id));
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task RelatedNavigationCannotReplaceAnUnsavedPurchaseOrVoucherWithoutConfirmation(bool isPurchase)
    {
        var fixture = new ShellFixture(); fixture.User.Permissions = PetraERP.Application.Security.PermissionCatalog.All.Select(p => p.Code).ToArray();
        using var shell = fixture.Create(new AdministrationFixture());
        shell.UserName = "admin"; shell.Password = "test"; await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);
        var route = isPurchase ? "purchase-invoices" : "treasury-vouchers";
        await shell.NavigateToAsync(route, isPurchase ? new Purchases.NewPurchaseRequest() : new Treasury.NewVoucherRequest(VoucherKind.Receipt)).ConfigureAwait(true);
        if (isPurchase) ((Purchases.PurchasesViewModel)shell.CurrentView!).DraftNotes = "مسودة يجب حفظها";
        else ((Treasury.VouchersViewModel)shell.CurrentView!).DraftNotes = "مسودة يجب حفظها";
        Assert.True(shell.Documents.Active.IsDirty); fixture.Confirm = false;
        await shell.NavigateToAsync(route, isPurchase ? new Purchases.OpenPurchaseRequest(Guid.NewGuid()) : new Treasury.OpenVoucherRequest(Guid.NewGuid())).ConfigureAwait(true);
        Assert.Equal("D_ConfirmDiscardChanges", fixture.LastConfirmation);
        Assert.Equal("مسودة يجب حفظها", isPurchase ? ((Purchases.PurchasesViewModel)shell.CurrentView!).DraftNotes : ((Treasury.VouchersViewModel)shell.CurrentView!).DraftNotes);
    }

    internal static ShellContext Scope(ShellFixture f) => new(f.Company, f.Branch, new HashSet<string>
        { "Parties.Directory.View", "Purchases.Invoices.View", "Treasury.Vouchers.View", "Sales.Invoices.View", "Ledger.Journals.View" });

    internal sealed class RelatedFixture : IRelatedDocumentsClient
    {
        internal int Total { get; set; }
        internal TaskCompletionSource<RelatedDocumentPage>? Pending { get; set; }
        internal List<(Guid Company, Guid Branch, RelatedDocumentSource Source, Guid Id, int Page, int Size)> Calls { get; } = [];
        public Task<RelatedDocumentPage> ListAsync(Guid companyId, Guid branchId, RelatedDocumentSource source, Guid sourceId,
            int page, int pageSize, CancellationToken cancellationToken)
        {
            Calls.Add((companyId, branchId, source, sourceId, page, pageSize));
            return Pending?.Task ?? Task.FromResult(new RelatedDocumentPage(page, Total,
                Enumerable.Range(0, Math.Max(0, Math.Min(pageSize, Total - (page - 1) * pageSize)))
                    .Select(i => new RelatedDocumentRow(Guid.NewGuid(), "سند مرتبط", DateTimeOffset.UtcNow, "1.0000", "USD", "Posted")).ToArray()));
        }
    }
}
