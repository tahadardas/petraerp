using PetraERP.ApiClient;
using PetraERP.Desktop.Sales;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Tests;

/// <summary>P04: server-side sort state, selection resolution after reload, stale customer-search results and in-flight paging.</summary>
public sealed class SalesInvoicesListBehaviorTests
{
    private static SalesInvoiceSummaryDto Dto(Guid id, string customer, SalesInvoiceStatus status = SalesInvoiceStatus.Draft, string version = "v1") => new()
    {
        Id = id, InvoiceNumber = null, CustomerName = customer, WarehouseCode = "WH", InvoiceDateUtc = DateTimeOffset.UtcNow,
        PaymentMethod = PaymentMethod.Cash, Status = status, GrandTotal = "10.0000", Version = version, CurrencyCode = "USD",
    };

    private static SalesInvoicePageResponse Page(int total, IEnumerable<SalesInvoiceSummaryDto> items) =>
        new() { Page = 1, PageSize = SalesInvoicesViewModel.PageSize, TotalCount = total, Items = items.ToList() };

    private static async Task<(ShellFixture Fixture, DesktopViewModel Shell, AccountingFixture Accounting, SalesInvoicesViewModel Vm)> StartAsync(Action<AccountingFixture> arrange)
    {
        var fixture = new ShellFixture();
        fixture.User.Permissions = ["Sales.Invoices.View", "Sales.Invoices.Manage", "Sales.Invoices.Approve", "Sales.Invoices.Post", "Sales.Invoices.Reverse"];
        var accounting = new AccountingFixture();
        arrange(accounting);
        var shell = fixture.Create(new AdministrationFixture(), accounting: accounting);
        shell.UserName = "admin"; shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);
        await shell.NavigateToAsync("sales-invoices", null).ConfigureAwait(true);
        return (fixture, shell, accounting, shell.Pages.SalesInvoices);
    }

    private static async Task WaitAsync(Func<bool> condition)
    {
        for (var i = 0; i < 400 && !condition(); i++) await Task.Delay(10).ConfigureAwait(true);
        Assert.True(condition(), "condition not reached in time");
    }

    [Fact]
    public async Task ReloadRebindsSelectionToTheNewObjectWithTheSameIdAndRefreshesCommands()
    {
        var id = Guid.NewGuid();
        var (_, shell, accounting, vm) = await StartAsync(a => a.Invoices.Add(Dto(id, "عميل", SalesInvoiceStatus.Draft, "v1"))).ConfigureAwait(true);
        using (shell)
        {
            vm.SelectedInvoice = vm.Invoices.Single();
            var before = vm.SelectedInvoice;
            Assert.True(vm.ApproveInvoiceCommand.CanExecute(null));

            // The same document comes back as a different object in a different state (posted elsewhere).
            accounting.Invoices[0] = Dto(id, "عميل", SalesInvoiceStatus.Posted, "v2");
            await vm.LoadInvoicesAsync().ConfigureAwait(true);

            Assert.NotSame(before, vm.SelectedInvoice);
            Assert.Same(vm.Invoices.Single(), vm.SelectedInvoice);
            Assert.Equal(SalesInvoiceStatus.Posted, vm.SelectedInvoice!.Status);
            Assert.False(vm.ApproveInvoiceCommand.CanExecute(null));
        }
    }

    [Fact]
    public async Task ReloadClearsTheSelectionOfARowThatNoLongerMatches()
    {
        var keep = Guid.NewGuid();
        var gone = Guid.NewGuid();
        var (_, shell, accounting, vm) = await StartAsync(a => { a.Invoices.Add(Dto(keep, "أ")); a.Invoices.Add(Dto(gone, "ب")); }).ConfigureAwait(true);
        using (shell)
        {
            vm.SelectedInvoice = vm.Invoices.Single(i => i.Id == gone);
            Assert.True(vm.OpenCardCommand.CanExecute(null));
            Assert.True(vm.ApproveInvoiceCommand.CanExecute(null));

            accounting.Invoices.RemoveAll(i => i.Id == gone);
            await vm.LoadInvoicesAsync().ConfigureAwait(true);

            Assert.Null(vm.SelectedInvoice);
            Assert.False(vm.OpenCardCommand.CanExecute(null));
            Assert.False(vm.ApproveInvoiceCommand.CanExecute(null));
            Assert.Equal(keep, Assert.Single(vm.Invoices).Id);
        }
    }

    [Fact]
    public async Task SortIsSentToTheServerAndFollowsPagingAndCardNavigation()
    {
        var (_, shell, accounting, vm) = await StartAsync(a => { a.Invoices.Add(Dto(Guid.NewGuid(), "أ")); a.Invoices.Add(Dto(Guid.NewGuid(), "ب")); }).ConfigureAwait(true);
        using (shell)
        {
            Assert.Null(vm.SortBy);
            await vm.ApplySortAsync(SalesInvoicesViewModel.ServerSortField("CustomerName"), descending: true).ConfigureAwait(true);

            var last = accounting.SearchCalls[^1];
            Assert.Equal(1, last.Page);
            Assert.Equal("Customer", last.SortBy);
            Assert.True(last.SortDescending);
            Assert.Equal("Customer", vm.CurrentScope.SortBy);
            Assert.True(vm.CurrentScope.SortDescending);
            Assert.Equal([1, 2], vm.Invoices.Select(i => i.RowNumber));

            await vm.ApplySortAsync(null, false).ConfigureAwait(true);
            Assert.Null(accounting.SearchCalls[^1].SortBy);
            Assert.Null(accounting.SearchCalls[^1].SortDescending);
            Assert.Null(vm.CurrentScope.SortBy);
        }
    }

    [Theory]
    [InlineData("InvoiceNumber", "InvoiceNumber")]
    [InlineData("PatternCode", "Pattern")]
    [InlineData("InvoiceDateUtc", "Date")]
    [InlineData("CustomerName", "Customer")]
    [InlineData("WarehouseCode", "Warehouse")]
    [InlineData("PaymentMethod", "PaymentMethod")]
    [InlineData("GrandTotal", "GrandTotal")]
    [InlineData("CurrencyCode", "Currency")]
    [InlineData("Status", "Status")]
    [InlineData("SettledDisplay", null)]
    public void GridColumnsMapToServerSortFields(string member, string? expected) =>
        Assert.Equal(expected, SalesInvoicesViewModel.ServerSortField(member));

    [Fact]
    public async Task CustomerSearchResultIsIgnoredWhenCanceledStaleOrFromAnotherWorkspace()
    {
        var (_, shell, _, vm) = await StartAsync(_ => { }).ConfigureAwait(true);
        using (shell)
        {
            var party = new PartyLookupDto { Id = Guid.NewGuid(), Code = "C9", NameArabic = "عميل" };
            var (company, branch) = vm.Workspace;
            var epoch = vm.ContextEpoch;
            var before = vm.Customers.Count;

            using var canceled = new CancellationTokenSource();
            await canceled.CancelAsync().ConfigureAwait(true);
            vm.MergeCustomerSearchResult([party], company, branch, epoch, canceled.Token);
            vm.MergeCustomerSearchResult([party], company, branch, epoch - 1, CancellationToken.None);   // stale generation
            vm.MergeCustomerSearchResult([party], Guid.NewGuid(), branch, epoch, CancellationToken.None); // other company
            vm.MergeCustomerSearchResult([party], company, Guid.NewGuid(), epoch, CancellationToken.None); // other branch
            Assert.Equal(before, vm.Customers.Count);

            vm.MergeCustomerSearchResult([party], company, branch, epoch, CancellationToken.None);
            vm.MergeCustomerSearchResult([party], company, branch, epoch, CancellationToken.None); // idempotent
            Assert.Equal(before + 1, vm.Customers.Count);
        }
    }

    [Fact]
    public async Task FilterChangeWhilePagingDiscardsTheLatePageOfTheOldFilter()
    {
        var oldPage = Enumerable.Range(0, SalesInvoicesViewModel.PageSize).Select(i => Dto(Guid.NewGuid(), "قديم " + i)).ToList();
        var newRows = Enumerable.Range(0, 3).Select(i => Dto(Guid.NewGuid(), "جديد " + i)).ToList();
        var latePage = Enumerable.Range(0, SalesInvoicesViewModel.PageSize).Select(i => Dto(Guid.NewGuid(), "متأخر " + i)).ToList();
        var gate = new TaskCompletionSource<SalesInvoicePageResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var (_, shell, accounting, vm) = await StartAsync(a => a.SearchOverride = (page, search, _) => (page, search) switch
        {
            (1, null) => Task.FromResult(Page(250, oldPage)),
            (2, null) => gate.Task,
            (1, "جديد") => Task.FromResult(Page(3, newRows)),
            _ => null,
        }).ConfigureAwait(true);
        using (shell)
        {
            Assert.Equal(SalesInvoicesViewModel.PageSize, vm.Invoices.Count);
            Assert.True(vm.HasMore);

            var paging = vm.LoadMoreAsync();                 // page 2 of the OLD filter is now in flight
            await WaitAsync(() => accounting.SearchCalls.Any(c => c.Page == 2)).ConfigureAwait(true);
            vm.Search = "جديد";                               // the operator changes the filter meanwhile
            await WaitAsync(() => vm.Invoices.Count == 3).ConfigureAwait(true);

            gate.SetResult(Page(250, latePage));             // the late page of the old filter finally arrives
            await paging.ConfigureAwait(true);

            Assert.Equal(newRows.Select(r => r.Id!.Value).ToList(), vm.Invoices.Select(i => i.Id).ToList());
            Assert.Equal(3, vm.TotalCount);
            Assert.False(vm.HasMore);
            Assert.DoesNotContain(vm.Invoices, i => i.CustomerName!.StartsWith("متأخر", StringComparison.Ordinal));
        }
    }
}
