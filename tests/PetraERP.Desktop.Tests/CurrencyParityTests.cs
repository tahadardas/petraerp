#pragma warning disable CA1707
using System.Globalization;
using PetraERP.ApiClient;
using PetraERP.Desktop.Currencies;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Purchases;
using PetraERP.Desktop.Sales;
using PetraERP.Desktop.Shell;
using PetraERP.Desktop.Treasury;

namespace PetraERP.Desktop.Tests;

/// <summary>
/// Document screens must show what the server actually holds: the company base currency, the enabled codes and the rate
/// registered for the document date. Nothing may be guessed, and a missing rate must stop the save rather than silently
/// price a foreign document at one.
/// </summary>
public sealed class CurrencyParityTests
{
    private static readonly string[] Permissions = [
        "Sales.Invoices.View", "Sales.Invoices.Manage", "Purchases.Invoices.View", "Purchases.Invoices.Manage",
        "Treasury.Vouchers.View", "Treasury.Vouchers.Manage", "Ledger.Journals.View", "Ledger.Accounts.View",
        "Inventory.Items.View", "Parties.Directory.View", "Patterns.Documents.View",
    ];

    private static readonly LocalizationService Arabic = new("ar");

    private static CurrencyFixture WithUsdRate()
    {
        var currencies = new CurrencyFixture();
        currencies.Register["USD"] = ("15.50000000", DateOnly.FromDateTime(DateTime.Today.AddDays(-4)), "Central Bank");
        return currencies;
    }

    [Fact]
    public async Task PurchaseDraft_OpensInTheCompanyBaseCurrencyAndNeverAGuessedCode()
    {
        var fixture = NewFixture();
        var currencies = new CurrencyFixture { BaseCurrency = "AED" };
        using var shell = fixture.Create(new AdministrationFixture(), currencies: currencies);
        shell.UserName = "admin"; shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);

        await shell.NavigateToAsync("purchase-invoices", new NewPurchaseRequest()).ConfigureAwait(true);
        var vm = shell.Pages.Purchases!;

        Assert.Equal("AED", vm.DraftCurrency);
        Assert.NotEqual("SYP", vm.DraftCurrency);
        Assert.Equal(["AED", "USD"], vm.CurrencyOptions.ToArray());
        // The base currency is one by server rule, so showing it filled is a reading, not an invention.
        Assert.Equal("1.00000000", vm.DraftRate);
        Assert.Equal(Arabic["CUR_BaseRate"], vm.RateInfo);
    }

    [Fact]
    public async Task VoucherDraft_ForeignCurrencyTakesTheRegisteredRateAndNamesItsSource()
    {
        var fixture = NewFixture();
        var currencies = WithUsdRate();
        using var shell = fixture.Create(new AdministrationFixture(), currencies: currencies);
        shell.UserName = "admin"; shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);

        await shell.NavigateToAsync("treasury-vouchers", new NewVoucherRequest(VoucherKind.Receipt)).ConfigureAwait(true);
        var vm = shell.Pages.Vouchers!;
        Assert.Equal("JOD", vm.DraftCurrency);

        vm.DraftCurrency = "USD";

        Assert.Equal("15.50000000", vm.DraftRate);
        Assert.Contains("Central Bank", vm.RateInfo, StringComparison.Ordinal);
        Assert.Equal(("USD", DateOnly.FromDateTime(DateTime.Today)), currencies.RateQueries[^1]);
    }

    [Fact]
    public async Task VoucherDraft_CurrencyWithoutARegisteredRateStaysEmptyAndSaysWhy()
    {
        var fixture = NewFixture();
        var currencies = new CurrencyFixture();
        using var shell = fixture.Create(new AdministrationFixture(), currencies: currencies);
        shell.UserName = "admin"; shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);

        await shell.NavigateToAsync("treasury-vouchers", new NewVoucherRequest(VoucherKind.Receipt)).ConfigureAwait(true);
        var vm = shell.Pages.Vouchers!;

        vm.DraftCurrency = "USD";

        // No rate exists, so none is displayed: a blank field plus the reason beats a silent conversion at one.
        Assert.Equal(string.Empty, vm.DraftRate);
        Assert.Equal(Arabic["CUR_RateMissing"], vm.RateInfo);
    }

    [Fact]
    public async Task PurchaseDraft_TypedRateSurvivesADateChangeAndIsMarkedAsTheOperatorsOwn()
    {
        var fixture = NewFixture();
        var currencies = WithUsdRate();
        using var shell = fixture.Create(new AdministrationFixture(), currencies: currencies);
        shell.UserName = "admin"; shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);

        await shell.NavigateToAsync("purchase-invoices", new NewPurchaseRequest()).ConfigureAwait(true);
        var vm = shell.Pages.Purchases!;
        vm.DraftCurrency = "USD";
        Assert.Equal("15.50000000", vm.DraftRate);

        vm.DraftRate = "16.75";
        vm.DraftDate = DateTime.Today.AddDays(-1);

        Assert.Equal("16.75", vm.DraftRate);
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, Arabic["CUR_ExplicitRate"], "15.50000000"), vm.RateInfo);
    }

    [Fact]
    public async Task PurchaseDraft_SwitchingCurrencyDropsThePreviousRateInsteadOfReusingIt()
    {
        var fixture = NewFixture();
        var currencies = WithUsdRate();
        using var shell = fixture.Create(new AdministrationFixture(), currencies: currencies);
        shell.UserName = "admin"; shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);

        await shell.NavigateToAsync("purchase-invoices", new NewPurchaseRequest()).ConfigureAwait(true);
        var vm = shell.Pages.Purchases!;
        vm.DraftCurrency = "USD";
        Assert.Equal("15.50000000", vm.DraftRate);

        vm.DraftCurrency = "JOD";

        Assert.Equal("1.00000000", vm.DraftRate);
        Assert.Equal(Arabic["CUR_BaseRate"], vm.RateInfo);
    }

    [Fact]
    public async Task PurchaseDraft_UnreadableRegisterIsReportedNotPaperedOver()
    {
        var fixture = NewFixture();
        var currencies = new CurrencyFixture
        {
            RateFailure = new PetraApiException("offline", 503, "{\"code\":\"currency.rate_missing\"}", new Dictionary<string, IEnumerable<string>>(), null),
        };
        using var shell = fixture.Create(new AdministrationFixture(), currencies: currencies);
        shell.UserName = "admin"; shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);

        await shell.NavigateToAsync("purchase-invoices", new NewPurchaseRequest()).ConfigureAwait(true);
        var vm = shell.Pages.Purchases!;

        vm.DraftCurrency = "USD";

        Assert.Equal(string.Empty, vm.DraftRate);
        Assert.Equal(Arabic["CUR_RateUnavailable"], vm.RateInfo);
    }

    [Fact]
    public async Task PurchaseDraft_HalfTypedCurrencyCodeAsksTheRegisterOnceNotPerKeystroke()
    {
        var fixture = NewFixture();
        var currencies = WithUsdRate();
        using var shell = fixture.Create(new AdministrationFixture(), currencies: currencies);
        shell.UserName = "admin"; shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);

        await shell.NavigateToAsync("purchase-invoices", new NewPurchaseRequest()).ConfigureAwait(true);
        var vm = shell.Pages.Purchases!;
        var afterOpening = currencies.RateReads;

        vm.DraftCurrency = "U";
        vm.DraftCurrency = "US";
        vm.DraftCurrency = "USD";

        // Only a well-formed code is a question; a partial one is noise the server would reject anyway.
        Assert.Equal(afterOpening + 1, currencies.RateReads);
        Assert.Single(currencies.RateQueries, query => query.Currency == "USD");
    }

    [Fact]
    public async Task SalesDraft_StartsInTheCompanyBaseCurrencyAndStaysEditable()
    {
        var fixture = NewFixture();
        var currencies = WithUsdRate();
        using var shell = fixture.Create(new AdministrationFixture(), currencies: currencies);
        shell.UserName = "admin"; shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);

        await shell.NavigateToAsync("sales-invoice-card", new NewSalesInvoiceCardRequest()).ConfigureAwait(true);
        var vm = Assert.IsType<SalesInvoiceCardViewModel>(shell.Documents.Active!.Content);

        // A new invoice starts in the company base currency instead of an empty field; another currency is still a choice.
        Assert.Equal("JOD", vm.DraftCurrencyCode);
        Assert.Equal(["", "JOD", "USD"], vm.CurrencyOptions.ToArray());

        vm.DraftCurrencyCode = "USD";

        // The register rate is looked up asynchronously after the currency changes: wait for it instead of racing it.
        for (var attempt = 0; attempt < 100 && vm.DraftExchangeRate != "15.50000000"; attempt++)
            await Task.Delay(20, TestContext.Current.CancellationToken).ConfigureAwait(true);

        Assert.Equal("15.50000000", vm.DraftExchangeRate);
        Assert.Contains("Central Bank", vm.RateInfo, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RateRegister_SendsTheChosenDateRangeAndClearingItRestocksOnce()
    {
        var fixture = NewFixture();
        var currencies = new CurrencyFixture();
        currencies.Register["USD"] = ("15.50000000", DateOnly.FromDateTime(DateTime.Today), "Central Bank");
        currencies.Register["EUR"] = ("16.00000000", DateOnly.FromDateTime(DateTime.Today), "Market");
        using var shell = fixture.Create(new AdministrationFixture(), currencies: currencies);
        shell.UserName = "admin"; shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);

        await shell.NavigateToAsync("currencies", null).ConfigureAwait(true);
        var vm = shell.Pages.CurrencySettings!;
        currencies.RateListQueries.Clear();

        vm.RateFilterFrom = new DateTime(2026, 1, 1);
        vm.RateFilterTo = new DateTime(2026, 3, 31);

        Assert.Equal(new DateOnly(2026, 1, 1), currencies.RateListQueries[^1].From);
        Assert.Equal(new DateOnly(2026, 3, 31), currencies.RateListQueries[^1].To);
        // Both entries take effect after the window, so the list is honestly empty rather than showing a stale rate.
        Assert.Empty(vm.Rates);

        vm.ClearRateFilterCommand.Execute(null);

        Assert.Single(currencies.RateListQueries, query => query.Currency is null && query.From is null && query.To is null);
        Assert.Equal(2, vm.Rates.Count);
    }

    private static ShellFixture NewFixture()
    {
        var fixture = new ShellFixture();
        fixture.User.Permissions = Permissions;
        return fixture;
    }
}
