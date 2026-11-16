using PetraERP.Desktop.Controls;
using PetraERP.ApiClient;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Tests;

public sealed class InputCompletionTests
{
    [Theory]
    [InlineData("مب جا")]
    [InlineData("جا مب")]
    [InlineData("م ب ج")]
    [InlineData("مَبـ جا")]
    [InlineData("مب ١١٦")]
    public void WordPrefixesFindTheRequestedArabicName(string query)
    {
        var wanted = new LookupItem(Guid.NewGuid(), "116", "مبيعات بيتون جاهز", "Ready mixed concrete sales");
        var other = new LookupItem(Guid.NewGuid(), "117", "مشتريات رمل", "Sand purchases");
        Assert.Same(wanted, Assert.Single(LookupFilter.Filter([wanted, other], query)));
    }

    [Fact]
    public void PrefixResultsRetainExactCodeRankingInactiveExclusionAndBoundedResults()
    {
        var exact = new LookupItem(Guid.NewGuid(), "116", "مبيعات", null);
        var prefix = new LookupItem(Guid.NewGuid(), "1161", "مبيعات بيتون جاهز", null);
        var inactive = new LookupItem(Guid.NewGuid(), "1162", "مبيعات بيتون جاهز", null, false);
        Assert.Equal(new[] { exact, prefix }, LookupFilter.Filter([prefix, inactive, exact], "١١٦"));
        Assert.Empty(LookupFilter.Filter([prefix], "مب حديد"));
        Assert.Single(LookupFilter.Filter([prefix, exact], "", 1));
    }

    [Fact]
    public async Task BothPartyRequiresBothAccountsAndRequiredNames()
    {
        var fixture = new ShellFixture(); fixture.User.Permissions = ["Parties.Directory.View", "Parties.Directory.Manage"];
        using var shell = fixture.Create(new AdministrationFixture());
        shell.UserName = "admin"; shell.Password = "test"; await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);
        await shell.NavigateToAsync("party-card", new NewPartyCardRequest()).ConfigureAwait(true);
        var vm = Assert.IsType<Parties.PartyCardViewModel>(shell.CurrentView);
        vm.DraftCode = "CS1"; vm.DraftNameArabic = "مبيعات بيتون جاهز"; vm.DraftRole = PartyRole.Both;
        Assert.True(vm.RequiresReceivableAccount); Assert.True(vm.RequiresPayableAccount);
        // Both accounts may stay empty (the server creates the party's own accounts); the required names still gate the save.
        Assert.True(vm.CanSaveDraft);
        vm.DraftReceivableAccountId = Guid.NewGuid(); Assert.True(vm.CanSaveDraft);
        vm.DraftPayableAccountId = Guid.NewGuid(); Assert.True(vm.CanSaveDraft);
        vm.DraftNameArabic = "  "; Assert.False(vm.CanSaveDraft);
    }

    [Fact]
    public async Task SalesDraftCannotSaveMissingDateOrAmount()
    {
        var fixture = new ShellFixture(); fixture.User.Permissions = ["Sales.Invoices.View", "Sales.Invoices.Manage"];
        using var shell = fixture.Create(new AdministrationFixture());
        shell.UserName = "admin"; shell.Password = "test"; await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);
        await shell.NavigateToAsync("sales-invoice-card", new NewSalesInvoiceCardRequest()).ConfigureAwait(true);
        var vm = Assert.IsType<Sales.SalesInvoiceCardViewModel>(shell.CurrentView);
        vm.DraftCustomerId = Guid.NewGuid(); vm.DraftWarehouseId = Guid.NewGuid(); vm.DraftPatternRevisionId = Guid.NewGuid();
        vm.Lines[0].ItemId = Guid.NewGuid(); vm.Lines[0].UnitId = Guid.NewGuid();
        Assert.True(vm.CanSaveDraft);
        vm.DraftInvoiceDate = null; Assert.False(vm.CanSaveDraft);
        vm.DraftInvoiceDate = DateTime.Today; vm.Lines[0].UnitPrice = ""; Assert.False(vm.CanSaveDraft);
        vm.Lines[0].UnitPrice = "0"; Assert.True(vm.CanSaveDraft);
        vm.Lines[0].Quantity = ""; Assert.False(vm.CanSaveDraft);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CommercialDraftCannotSaveMissingDateCurrencyOrRate(bool purchase)
    {
        var fixture = new ShellFixture(); fixture.User.Permissions = ["Purchases.Invoices.View", "Purchases.Invoices.Manage", "Treasury.Vouchers.View", "Treasury.Vouchers.Manage"];
        using var shell = fixture.Create(new AdministrationFixture());
        shell.UserName = "admin"; shell.Password = "test"; await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);
        if (purchase)
        {
            await shell.NavigateToAsync("purchase-invoices", new Purchases.NewPurchaseRequest()).ConfigureAwait(true);
            var vm = shell.Pages.Purchases!; vm.DraftPatternRevisionId = Guid.NewGuid(); vm.DraftSupplierId = Guid.NewGuid(); vm.DraftWarehouseId = Guid.NewGuid();
            vm.Lines[0].ItemId = Guid.NewGuid(); vm.Lines[0].UnitId = Guid.NewGuid(); Assert.True(vm.IsDraftComplete());
            vm.DraftDate = null; Assert.False(vm.IsDraftComplete()); vm.DraftDate = DateTime.Today;
            vm.DraftCurrency = ""; Assert.False(vm.IsDraftComplete()); vm.DraftCurrency = "JOD";
            vm.DraftRate = ""; Assert.False(vm.IsDraftComplete());
        }
        else
        {
            await shell.NavigateToAsync("treasury-vouchers", new Treasury.NewVoucherRequest(VoucherKind.Receipt)).ConfigureAwait(true);
            var vm = shell.Pages.Vouchers!; vm.DraftPatternRevisionId = Guid.NewGuid(); vm.DraftPartyId = Guid.NewGuid(); vm.DraftAmount = "100";
            Assert.True(vm.IsDraftComplete()); vm.DraftDate = null; Assert.False(vm.IsDraftComplete()); vm.DraftDate = DateTime.Today;
            vm.DraftCurrency = ""; Assert.False(vm.IsDraftComplete()); vm.DraftCurrency = "JOD";
            vm.DraftRate = ""; Assert.False(vm.IsDraftComplete());
        }
    }

    [Fact]
    public async Task EnforcedPatternAccountMustBeSelectedBeforeSaving()
    {
        var fixture = new ShellFixture(); fixture.User.Permissions = ["Patterns.Documents.View", "Patterns.Documents.Manage", "Ledger.Accounts.View", "Ledger.CostCenters.View"];
        using var shell = fixture.Create(new AdministrationFixture());
        shell.UserName = "admin"; shell.Password = "test"; await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);
        await shell.NavigateToAsync("patterns", new NewPatternCardRequest()).ConfigureAwait(true);
        var vm = Assert.IsType<Patterns.PatternCardViewModel>(shell.CurrentView);
        vm.DraftCode = "SI"; vm.DraftShortCode = "SI"; vm.DraftNameArabic = "مبيعات";
        Assert.True(vm.CanSaveDraft); vm.DraftSalesEnforced = true; Assert.False(vm.CanSaveDraft);
        vm.DraftSalesAccountId = Guid.NewGuid(); Assert.True(vm.CanSaveDraft);
    }
}
