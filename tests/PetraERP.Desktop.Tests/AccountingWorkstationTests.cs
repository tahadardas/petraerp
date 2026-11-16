using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using PetraERP.ApiClient;
using PetraERP.Application.Productivity;
using PetraERP.Application.Security;
using PetraERP.Desktop.Accounting;
using PetraERP.Desktop.Controls;
using PetraERP.Desktop.GeneralLedger;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Tests;

public sealed class AccountingWorkstationTests
{
    private static readonly string[] All = PermissionCatalog.All.Select(permission => permission.Code).ToArray();
    private static ShellContext Context(ShellFixture fixture, params string[] permissions) => new(fixture.Company, fixture.Branch, permissions.ToHashSet(StringComparer.Ordinal));

    [Fact]
    public async Task AllCurrentRoutesMapToPagesAndUnsupportedModulesAreAbsent()
    {
        var fixture = new ShellFixture(); fixture.User.Permissions = All;
        using var shell = fixture.Create(new AdministrationFixture());
        shell.UserName = "admin"; shell.Password = "test"; await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.True(shell.Workspace.ContextReady); // Must load during the initial login operation, before IsOnline becomes true.
        Assert.Equal(49, shell.Navigation.Items.Count);
        foreach (var route in new[] { "accounts", "cost-centers", "periods", "journals", "general-journal", "account-ledger", "trial-balance", "posting-management", "patterns", "parties", "items", "sales-invoices", "treasury-vouchers", "purchase-invoices" })
        {
            await shell.NavigateToAsync(route, null).ConfigureAwait(true);
            // Report routes open in their own window, so the page is checked on its document rather than in the tab host.
            Assert.Empty(shell.Operation.Error);
            Assert.Same(shell.Pages.Page(route), shell.Documents.Documents.Single(document => document.Route == route).Content);
        }
        Assert.DoesNotContain(shell.Navigation.Items, route => route.Id is "sales" or "purchases" or "workspace");
    }

    [Fact]
    public async Task RevokedRouteClearsPageAndDirectNavigationCannotReopenIt()
    {
        var fixture = new ShellFixture(); fixture.User.Permissions = All;
        using var shell = fixture.Create(new AdministrationFixture()); shell.UserName = "admin"; shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true); await shell.NavigateToAsync("accounts", null).ConfigureAwait(true);
        fixture.User.Permissions = ["Ledger.Reports.View"];
        await shell.RefreshAsync().ConfigureAwait(true);
        Assert.Null(shell.CurrentView); Assert.False(shell.Pages.Accounts.HasContext); Assert.False(shell.Pages.Accounts.NewCommand.CanExecute(null));
        await shell.NavigateToAsync("accounts", null).ConfigureAwait(true); Assert.Null(shell.CurrentView);
    }

    [Fact]
    public async Task BranchAndCompanyChangesReloadTheVisiblePageWithTheNewBaseCurrency()
    {
        var fixture = new ShellFixture(); fixture.User.Permissions = All;
        var api = new FakeLedgerClient(); api.Companies[fixture.Company.Id] = ("JOD", []);
        using var shell = fixture.Create(new AdministrationFixture(), api); shell.UserName = "admin"; shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true); await shell.NavigateToAsync("journals", null).ConfigureAwait(true);
        var old = api.Add(fixture.Company.Id, fixture.Branch.Id, JournalState.Posted);
        await shell.Ledger.OpenByIdAsync(old.Id, Cancellation).ConfigureAwait(true);
        var otherBranch = new BranchContextResponse { Id = Guid.NewGuid(), CompanyId = fixture.Company.Id, Code = "B2", NameArabic = "ثان" };
        var otherCompany = new CompanyContextResponse { Id = Guid.NewGuid(), Code = "USD", NameArabic = "شركة الدولار", BaseCurrencyCode = "USD" };
        var companyBranch = new BranchContextResponse { Id = Guid.NewGuid(), CompanyId = otherCompany.Id, Code = "HQ", NameArabic = "رئيسي" };
        fixture.AdditionalBranches.AddRange([otherBranch, companyBranch]); fixture.AdditionalCompanies.Add(otherCompany); api.Companies[otherCompany.Id] = ("USD", []);
        await shell.RefreshAsync().ConfigureAwait(true); await shell.SelectBranchCommand.ExecuteAsync(otherBranch).ConfigureAwait(true);
        Assert.False(shell.Ledger.IsEditorOpen); Assert.Empty(shell.Ledger.Journals); Assert.Equal(otherBranch.Id, shell.Workspace.Branch!.Id);
        await shell.SelectCompanyCommand.ExecuteAsync(otherCompany).ConfigureAwait(true);
        Assert.Equal("USD", shell.Ledger.BaseCurrencyCode); Assert.Equal(companyBranch.Id, shell.Branch!.Id); Assert.Empty(shell.Operation.Error);
    }

    [Fact]
    public async Task UnsavedJournalCannotBeValidatedOrDiscardedWithoutConfirmation()
    {
        var fixture = new ShellFixture(); fixture.User.Permissions = All;
        using var shell = fixture.Create(new AdministrationFixture(), new FakeLedgerClient()); shell.UserName = "admin"; shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true); await shell.NavigateToAsync("journals", NewJournalRequest.Instance).ConfigureAwait(true);
        var draft = Assert.IsType<GeneralLedgerViewModel>(shell.CurrentView); Assert.True(draft.IsDocument); Assert.NotSame(shell.Ledger, draft);
        draft.JournalDescription = "تعديلات غير محفوظة"; Assert.True(draft.HasUnsavedChanges); Assert.False(draft.ValidateDraftCommand.CanExecute(null));
        var dirty = shell.Documents.Active; Assert.True(dirty.IsDirty);
        // Opening another tab keeps the dirty journal in its own tab; nothing is discarded and no confirmation is needed.
        fixture.Confirm = false; await shell.NavigateToAsync("accounts", null).ConfigureAwait(true);
        Assert.Same(shell.Pages.Accounts, shell.CurrentView); Assert.Null(fixture.LastConfirmation); Assert.True(draft.HasUnsavedChanges);
        await shell.NavigateToAsync("journals", null).ConfigureAwait(true);
        Assert.Same(shell.Ledger, shell.CurrentView); Assert.Equal("تعديلات غير محفوظة", draft.JournalDescription);
        // Another new journal is another document; the dirty draft is neither replaced nor asked about.
        await shell.NavigateToAsync("journals", NewJournalRequest.Instance).ConfigureAwait(true);
        Assert.Null(fixture.LastConfirmation); Assert.NotSame(draft, shell.CurrentView); Assert.Equal("تعديلات غير محفوظة", draft.JournalDescription);
        var other = shell.Documents.Active; Assert.NotSame(dirty, other);
        shell.Documents.Activate(dirty);
        await shell.CloseActiveDocumentCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Equal("D_ConfirmCloseDirtyDocument", fixture.LastConfirmation); Assert.Same(draft, shell.CurrentView);
        await shell.LogoutCommand.ExecuteAsync(null).ConfigureAwait(true); Assert.True(shell.IsAuthenticated);
        fixture.Confirm = true; await shell.CloseActiveDocumentCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.DoesNotContain(dirty, shell.Documents.Documents); Assert.False(shell.Ledger.HasUnsavedChanges);
        Assert.Same(other, Assert.Single(shell.Documents.Documents, document => document.Kind == WorkspaceDocumentKind.Journal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShellOutageRecoveryPreservesAmbiguousPostingUntilTheServerOutcomeIsKnown(bool serverCommitted)
    {
        var fixture = new ShellFixture(); fixture.User.Permissions = All; var client = new FakeLedgerClient();
        using var shell = fixture.Create(new AdministrationFixture(), client); shell.UserName = "admin"; shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);
        var journal = client.Add(fixture.Company.Id, fixture.Branch.Id, JournalState.Approved);
        await shell.NavigateToAsync("journals", new OpenJournalRequest(journal.Id)).ConfigureAwait(true);
        var document = Assert.IsType<GeneralLedgerViewModel>(shell.CurrentView); Assert.True(document.IsDocument);
        var key = WorkspaceDocument.JournalKey(fixture.Company.Id, fixture.Branch.Id, journal.Id); Assert.Equal(key, shell.Documents.Active.Key);
        document.ActionReason = "ترحيل موثق"; client.PostingFailures.Enqueue((new HttpRequestException("ambiguous outcome"), serverCommitted));
        await document.PostJournalCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.True(document.HasPendingPostingAttempt); Assert.False(shell.Operation.IsOnline); Assert.False(shell.ContextReady); Assert.Null(shell.CurrentView);
        await shell.RefreshAsync().ConfigureAwait(true); Assert.True(shell.ContextReady);
        // The retained document (same view model, so the same idempotency key) is back after the scope is reauthorized.
        Assert.Same(document, shell.Documents.Find(key)?.Content);
        await shell.NavigateToAsync("journals", new OpenJournalRequest(journal.Id)).ConfigureAwait(true);
        Assert.Same(document, shell.CurrentView);
        if (serverCommitted)
        {
            Assert.Equal(JournalState.Posted, document.CurrentState); Assert.False(document.PostJournalCommand.CanExecute(null)); Assert.Single(client.PostCalls);
        }
        else
        {
            document.ActionReason = "ترحيل موثق"; await document.PostJournalCommand.ExecuteAsync(null).ConfigureAwait(true);
            Assert.Equal(2, client.PostCalls.Count); Assert.Equal(client.PostCalls[0].Key, client.PostCalls[1].Key); Assert.Equal(JournalState.Posted, document.CurrentState);
        }
        Assert.False(document.HasPendingPostingAttempt); Assert.Equal(1, client.Get(journal.Id).PostingSequence);
    }

    [Theory]
    [InlineData("0", "10")]
    [InlineData("-1", "10")]
    [InlineData("1", "10.12345")]
    [InlineData("1.000000001", "10")]
    [InlineData("79228162514264337593543950335", "2")]
    public async Task InvalidAmountsAndRatesAreRejectedWithoutCorrection(string rate, string debit)
    {
        var fixture = new ShellFixture(); var client = new FakeLedgerClient(); var loc = new LocalizationService("en");
        using var model = new GeneralLedgerViewModel(client, fixture, new(loc), loc); model.Configure(All);
        await model.LoadPageAsync(fixture.Company.Id, fixture.Branch.Id, "JOD", Cancellation).ConfigureAwait(true);
        await model.NewDraftCommand.ExecuteAsync(null).ConfigureAwait(true); model.JournalDescription = "Validation";
        model.Lines[0].AccountId = Guid.NewGuid(); model.Lines[1].AccountId = Guid.NewGuid();
        model.Lines[0].Rate = decimal.Parse(rate, System.Globalization.CultureInfo.InvariantCulture); model.Lines[0].Debit = decimal.Parse(debit, System.Globalization.CultureInfo.InvariantCulture);
        await model.SaveDraftCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Null(client.LastCreateRequest); Assert.NotEmpty(model.ValidationMessage); Assert.Equal(decimal.Parse(rate, System.Globalization.CultureInfo.InvariantCulture), model.Lines[0].Rate);
    }

    [Theory]
    [InlineData("1001")]
    [InlineData("الصندوق")]
    [InlineData("cash")]
    public void LookupMatchesCodeAndBothNamesAndOmitsInactiveEntries(string query)
    {
        var cash = new LookupItem(Guid.NewGuid(), "1001", "الصندوق", "Cash");
        var inactive = new LookupItem(Guid.NewGuid(), "10010", "الصندوق", "Cash", false);
        Assert.Equal(cash.Id, Assert.Single(LookupFilter.Filter([cash, inactive], query)).Id);
        Assert.Equal(50, LookupFilter.Filter(Enumerable.Range(0, 5000).Select(index => new LookupItem(Guid.NewGuid(), index.ToString(System.Globalization.CultureInfo.InvariantCulture), "مركز", "Center")), "مركز").Count);
    }

    [Fact]
    public async Task AccountTreeRetainsAncestorsAndLocksUsedStructureAndParentsPosting()
    {
        var fixture = new ShellFixture(); var api = new AccountingFixture(); var loc = new LocalizationService("ar");
        var root = new AccountResponse { Id = Guid.NewGuid(), Code = "1", NameArabic = "الأصول", HasChildren = true, IsActive = true, PostingAllowed = false, AccountType = AccountType.Assets };
        var child = new AccountResponse { Id = Guid.NewGuid(), ParentId = root.Id, Level = 1, Code = "1001", NameArabic = "الصندوق", HasJournalLines = true, IsActive = true, PostingAllowed = true, AccountType = AccountType.Assets };
        api.Accounts.AddRange([root, child]);
        using var model = new ChartOfAccountsViewModel(api, new FakeLedgerClient(), new ShellNavigator(), new(loc), loc, new(loc), fixture);
        await model.ActivateAsync(Context(fixture, All), null, Cancellation).ConfigureAwait(true);
        model.Filter = "الصندوق"; Assert.Equal(2, model.Nodes.Count);
        model.SelectedNode = model.Nodes[1]; Assert.False(model.CanEditStructure); Assert.True(model.CanEditDetails);
        model.SelectedNode = model.Nodes[0]; Assert.True(model.CanEditStructure); Assert.False(model.CanEditPosting);
        await model.ActivateAsync(Context(fixture, "Ledger.Accounts.View"), null, Cancellation).ConfigureAwait(true);
        Assert.False(model.SaveCommand.CanExecute(null)); Assert.False(model.CanEditDetails);
    }

    [Fact]
    public async Task ReportsUseServerTotalsFiltersAndTypedDrillDowns()
    {
        var fixture = new ShellFixture(); var api = new AccountingFixture(); var loc = new LocalizationService("en"); var nav = new ShellNavigator();
        string? route = null; object? parameter = null; nav.Requested += (r, p) => { route = r; parameter = p; return Task.CompletedTask; };
        var journalId = Guid.NewGuid(); var account = Guid.NewGuid();
        api.GeneralJournal = new() { Page = 1, PageSize = 100, TotalCount = 150, TotalBaseDebit = "777.1250", TotalBaseCredit = "777.1250", Lines = [new() { JournalId = journalId, BaseDebit = "2.0000" }] };
        using var journal = new GeneralJournalViewModel(api, new FakeLedgerClient(), nav, new(loc), loc);
        await journal.ActivateAsync(Context(fixture, All), null, Cancellation).ConfigureAwait(true);
        Assert.Equal("777.125", journal.TotalDebit); Assert.True(journal.NextPageCommand.CanExecute(null));
        journal.Search = "reference"; journal.AccountId = account; journal.FromDate = new(2026, 1, 1);
        await journal.RunCommand.ExecuteAsync(null).ConfigureAwait(true); Assert.Equal(account, api.LastFilter!.AccountId); Assert.Equal("reference", api.LastFilter.Search);
        await journal.OpenJournalCommand.ExecuteAsync(journal.Lines[0]).ConfigureAwait(true);
        Assert.Equal("journals", route); Assert.Equal(journalId, Assert.IsType<OpenJournalRequest>(parameter).JournalId);
        api.Ledger = new() { Account = new() { Id = account, Code = "1001", NameArabic = "الصندوق" }, OpeningBalance = "500.0000", ClosingBalance = "999.0000", Lines = [new() { JournalId = journalId, RunningBalance = "987.6543", BaseDebit = "1" }] };
        using var ledger = new AccountLedgerViewModel(api, new FakeLedgerClient(), nav, new(loc), loc);
        await ledger.ActivateAsync(Context(fixture, All), new OpenAccountLedgerRequest(account, null, null, false), Cancellation).ConfigureAwait(true);
        Assert.Equal("987.6543", ledger.Lines[0].RunningBalance); Assert.Equal("999.00", ledger.Closing);
        await ledger.OpenJournalCommand.ExecuteAsync(ledger.Lines[0]).ConfigureAwait(true); Assert.Equal(journalId, Assert.IsType<OpenJournalRequest>(parameter).JournalId);
        api.Balance = new() { From = new(2026, 1, 1), To = new(2026, 12, 31), Rows = [new() { AccountId = account }], TotalPeriodDebit = "543.2100", IsBalanced = true };
        using var balance = new TrialBalanceViewModel(api, api, nav, new(loc), loc, TimeProvider.System);
        await balance.ActivateAsync(Context(fixture, All), null, Cancellation).ConfigureAwait(true); await balance.RunCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Equal("543.21", balance.TotalPeriodDebit); Assert.True(balance.IsBalanced);
        await balance.OpenLedgerCommand.ExecuteAsync(balance.Rows[0]).ConfigureAwait(true);
        Assert.Equal("account-ledger", route); Assert.Equal(account, Assert.IsType<OpenAccountLedgerRequest>(parameter).AccountId);
    }

    [Fact]
    public async Task AccountLedgerPagesForwardWithTheServerCursorAndRestartsWithoutOne()
    {
        var fixture = new ShellFixture(); var api = new AccountingFixture(); var loc = new LocalizationService("en"); var account = Guid.NewGuid();
        api.Ledger = new() { Account = new() { Id = account, Code = "1001" }, Lines = [], Page = 1, PageSize = 100, TotalCount = 250, NextCursor = "cursor-2" };
        using var ledger = new AccountLedgerViewModel(api, new FakeLedgerClient(), new ShellNavigator(), new(loc), loc);
        await ledger.ActivateAsync(Context(fixture, All), new OpenAccountLedgerRequest(account, null, null, false), Cancellation).ConfigureAwait(true);
        Assert.Equal((1, (string?)null), api.LedgerCursors[^1]);

        api.Ledger = new() { Account = new() { Id = account, Code = "1001" }, Lines = [], Page = 2, PageSize = 100, TotalCount = 250, NextCursor = "cursor-3" };
        await ledger.NextPageCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Equal((2, "cursor-2"), api.LedgerCursors[^1]);
        await ledger.NextPageCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Equal((3, "cursor-3"), api.LedgerCursors[^1]);

        await ledger.RunCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Equal((1, (string?)null), api.LedgerCursors[^1]);
    }

    [Fact]
    public async Task TrialBalanceSendsTheReportParametersAndOnlyWhenTheyAreSet()
    {
        var fixture = new ShellFixture(); var api = new AccountingFixture(); var loc = new LocalizationService("en");
        var account = Guid.NewGuid(); var center = Guid.NewGuid();
        api.Balance = new() { From = new(2026, 1, 1), To = new(2026, 12, 31), Rows = [], IsBalanced = true, BaseCurrencyCode = "USD", DisplayCurrencyCode = "EUR" };
        using var balance = new TrialBalanceViewModel(api, api, new ShellNavigator(), new(loc), loc, TimeProvider.System);
        await balance.ActivateAsync(Context(fixture, All), null, Cancellation).ConfigureAwait(true);
        await balance.RunCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Null(api.LastBalanceFilter!.Parameters);
        Assert.Equal("EUR", balance.BaseCurrency);

        balance.ParameterAccountId = account; balance.ParameterCostCenterId = center; balance.AccountScope = balance.AccountScopes[1];
        balance.FromJournalNumberText = "10"; balance.ToJournalNumberText = "20"; balance.SourceVoucher = false; balance.SourceSalesInvoice = false;
        balance.DisplayCurrencyText = "eur"; balance.DisplayRateText = "2.5"; balance.ShowLastMovement = true;
        await balance.RunCommand.ExecuteAsync(null).ConfigureAwait(true);
        var sent = api.LastBalanceFilter!.Parameters!;
        Assert.Equal(account, sent.AccountId); Assert.Equal(center, sent.CostCenterId); Assert.Equal(1, sent.AccountScope);
        Assert.Equal(10, sent.FromJournalNumber); Assert.Equal(20, sent.ToJournalNumber);
        Assert.Equal("JOURNAL,PURCHASE_INVOICE", sent.Sources); Assert.Equal("EUR", sent.DisplayCurrency); Assert.Equal("2.5", sent.DisplayRate);
        Assert.True(sent.ShowLastMovement); Assert.True(balance.LastMovementColumns);
        balance.DisplayRateText = "0.12345678";
        Assert.Equal("0.12345678", balance.BuildParameters()!.DisplayRate);
        balance.DisplayRateText = "1e-8";
        Assert.Equal("1e-8", balance.BuildParameters()!.DisplayRate); // Invalid text is sent for server validation, never silently omitted.
    }

    [Fact]
    public async Task DailyVoucherBuildsCashAndCounterLinesAndPostsThroughTheJournal()
    {
        var fixture = new ShellFixture(); var api = new AccountingFixture(); var loc = new LocalizationService("en");
        var ledger = new FakeLedgerClient(); var cash = Guid.NewGuid(); var customer = Guid.NewGuid(); var expense = Guid.NewGuid();
        ledger.Companies[fixture.Company.Id] = ("SYP", ["USD"]);
        api.Ledger = new() { Account = new() { Id = cash, Code = "1011" }, ClosingBalance = "1000.0000", Lines = [] };
        using var voucher = new Shell.DailyVoucherViewModel(ledger, api, new(loc), loc, TimeProvider.System, fixture);
        await voucher.ActivateAsync(Context(fixture, All), null, Cancellation).ConfigureAwait(true);

        // Validation first: nothing is sent while the voucher is incomplete.
        Assert.Null(voucher.BuildLines()); Assert.NotEmpty(voucher.ValidationMessage);
        voucher.CashAccountId = cash;
        Assert.Null(voucher.BuildLines());
        voucher.Rows[0].AccountId = cash; voucher.Rows[0].Receive = 100m;
        Assert.Null(voucher.BuildLines()); // counter account may not be the cash account itself
        voucher.Rows[0].AccountId = customer; voucher.Rows[0].Pay = 5m;
        Assert.Null(voucher.BuildLines()); // both pay and receive on one row
        voucher.Rows[0].Pay = 0m;
        voucher.AddRowCommand.Execute(null); voucher.Rows[1].AccountId = expense; voucher.Rows[1].Pay = 30m; voucher.Rows[1].Description = "fuel";

        var lines = voucher.BuildLines()!;
        Assert.Equal(4, lines.Count);
        Assert.Equal(cash, lines[0].AccountId); Assert.Equal("100.0000", lines[0].TransactionDebit); Assert.Equal("0.0000", lines[0].TransactionCredit);
        Assert.Equal(customer, lines[1].AccountId); Assert.Equal("100.0000", lines[1].TransactionCredit);
        Assert.Equal(cash, lines[2].AccountId); Assert.Equal("30.0000", lines[2].TransactionCredit);
        Assert.Equal(expense, lines[3].AccountId); Assert.Equal("30.0000", lines[3].TransactionDebit); Assert.Equal("fuel", lines[3].Description);
        Assert.Equal(lines.Sum(l => decimal.Parse(l.BaseDebit!, System.Globalization.CultureInfo.InvariantCulture)), lines.Sum(l => decimal.Parse(l.BaseCredit!, System.Globalization.CultureInfo.InvariantCulture)));

        Assert.Equal(100m, voucher.TotalReceive); Assert.Equal(30m, voucher.TotalPay);
        await voucher.PostCommand.ExecuteAsync(null).ConfigureAwait(true);
        var created = Assert.Single(ledger.PostCalls);
        Assert.Equal(4, ledger.LastCreateRequest!.Lines.Count);
        Assert.Equal(cash, ledger.VoucherHeaders[created.JournalId].CashAccountId); Assert.Equal("DAILY", ledger.VoucherHeaders[created.JournalId].Kind);
        Assert.True(voucher.IsPosted); Assert.False(voucher.CanPost); Assert.Equal(created.JournalId, voucher.JournalId);

        // "إلغاء ترحيل": a posted voucher is reversed through the journal's own reversal (draft → validated → approved → posted).
        fixture.Confirm = true;
        Assert.True(voucher.CanReverse); Assert.True(voucher.ReverseCommand.CanExecute(null));
        await voucher.ReverseCommand.ExecuteAsync(null).ConfigureAwait(true);
        var reversal = Assert.Single(ledger.ReversalCalls);
        Assert.Equal(created.JournalId, reversal.Item3);
        Assert.False(voucher.CanReverse); // the original is now Reversed

        // Opening a journal that carries no marker is refused; one that does is shown with the cash account restored.
        using var reopened = new Shell.DailyVoucherViewModel(ledger, api, new(loc), loc, TimeProvider.System, fixture);
        await reopened.ActivateAsync(Context(fixture, All), new Shell.OpenDailyVoucherRequest(created.JournalId), Cancellation).ConfigureAwait(true);
        Assert.Equal(cash, reopened.CashAccountId); Assert.True(reopened.IsPosted);
        var plain = ledger.Add(fixture.Company.Id, fixture.Branch.Id, PetraERP.ApiClient.JournalState.Draft);
        using var other = new Shell.DailyVoucherViewModel(ledger, api, new(loc), loc, TimeProvider.System, fixture);
        await other.ActivateAsync(Context(fixture, All), new Shell.OpenDailyVoucherRequest(plain.Id), Cancellation).ConfigureAwait(true);
        Assert.Null(other.CashAccountId); Assert.NotEmpty(other.ValidationMessage);
    }

    [Fact]
    public async Task LateReportCannotRepopulateAfterContextClear()
    {
        var fixture = new ShellFixture(); var api = new AccountingFixture(); var loc = new LocalizationService("en");
        using var model = new GeneralJournalViewModel(api, new FakeLedgerClient(), new(), new(loc), loc);
        var pending = new TaskCompletionSource<GeneralJournalResponse>(TaskCreationOptions.RunContinuationsAsynchronously); api.PendingReport = pending;
        var loading = model.ActivateAsync(Context(fixture, All), null, Cancellation); model.Clear();
        pending.SetResult(new() { Lines = [new() { LineDescription = "OLD COMPANY" }] });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => loading).ConfigureAwait(true);
        Assert.Empty(model.Lines); Assert.Null(model.Result);
    }

    [Fact]
    public async Task FiscalTransitionsRequirePermissionReasonAndConfirmationAndReloadTheServerState()
    {
        var fixture = new ShellFixture(); var api = new AccountingFixture(); var loc = new LocalizationService("en");
        var period = new AccountingPeriodResponse { Id = Guid.NewGuid(), Code = "P01", RangeStart = new(2026, 1, 1), RangeEnd = new(2026, 12, 31), State = AccountingPeriodState.Open, Version = "version" };
        api.Years.Add(new() { Id = Guid.NewGuid(), Code = "2026", Periods = [period] });
        using var model = new FiscalPeriodsViewModel(api, fixture, new ShellNavigator(), new(loc), loc, new(loc), TimeProvider.System);
        await model.ActivateAsync(Context(fixture, All), null, Cancellation).ConfigureAwait(true);
        Assert.False(model.CloseCommand.CanExecute(null)); model.Reason = "Reviewed"; fixture.Confirm = false;
        await model.CloseCommand.ExecuteAsync(null).ConfigureAwait(true); Assert.Null(api.LastTransition);
        fixture.Confirm = true; await model.SoftCloseCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Equal("Reviewed", api.LastTransition!.Reason); Assert.Equal(AccountingPeriodState.SoftClosed, model.SelectedPeriod!.Period.State); Assert.True(model.CanReopen);
        await model.ActivateAsync(Context(fixture, "Ledger.Periods.View"), null, Cancellation).ConfigureAwait(true);
        Assert.False(model.ReopenCommand.CanExecute(null)); Assert.False(model.CanReopen);
    }

    [Fact]
    public async Task CostCenterDrillsDownToGeneralJournalAndFiscalPeriodDrillsDownToTrialBalance()
    {
        var fixture = new ShellFixture(); var api = new AccountingFixture(); var loc = new LocalizationService("en"); var nav = new ShellNavigator();
        string? route = null; object? parameter = null; nav.Requested += (r, p) => { route = r; parameter = p; return Task.CompletedTask; };

        // 1. Cost Center -> General Journal navigation
        var centerId = Guid.NewGuid();
        api.CostCenters.Add(new() { Id = centerId, Code = "CC-PROJ1", NameArabic = "مشروع 1", IsActive = true, Version = "v1" });
        using var centersVm = new CostCentersViewModel(api, nav, new(loc), loc, new(loc));
        await centersVm.ActivateAsync(Context(fixture, All), null, Cancellation).ConfigureAwait(true);
        centersVm.Selected = centersVm.Items[0];
        Assert.True(centersVm.CanViewGeneralJournal);
        Assert.True(centersVm.OpenGeneralJournalCommand.CanExecute(null));
        await centersVm.OpenGeneralJournalCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Equal("general-journal", route);
        var gjReq = Assert.IsType<OpenGeneralJournalRequest>(parameter);
        Assert.Equal(centerId, gjReq.CostCenterId);

        // General Journal receives OpenGeneralJournalRequest and sets CostCenterId filter
        api.GeneralJournal = new() { Page = 1, PageSize = 100, TotalCount = 1, TotalBaseDebit = "100.0000", TotalBaseCredit = "100.0000", Lines = [new() { JournalId = Guid.NewGuid(), BaseDebit = "100.0000" }] };
        using var journalVm = new GeneralJournalViewModel(api, new FakeLedgerClient(), nav, new(loc), loc);
        await journalVm.ActivateAsync(Context(fixture, All), gjReq, Cancellation).ConfigureAwait(true);
        Assert.Equal(centerId, journalVm.CostCenterId);
        Assert.Equal(centerId, api.LastFilter!.CostCenterId);

        // 2. Fiscal Period -> Trial Balance navigation
        var periodId = Guid.NewGuid();
        var period = new AccountingPeriodResponse { Id = periodId, Code = "P06", RangeStart = new(2026, 6, 1), RangeEnd = new(2026, 6, 30), State = AccountingPeriodState.Open, Version = "v1" };
        api.Years.Add(new() { Id = Guid.NewGuid(), Code = "2026", Periods = [period] });
        using var periodsVm = new FiscalPeriodsViewModel(api, fixture, nav, new(loc), loc, new(loc), TimeProvider.System);
        await periodsVm.ActivateAsync(Context(fixture, All), null, Cancellation).ConfigureAwait(true);
        periodsVm.SelectedPeriod = periodsVm.Periods.First(p => p.Period.Id == periodId);
        Assert.True(periodsVm.CanViewTrialBalance);
        Assert.True(periodsVm.OpenTrialBalanceCommand.CanExecute(null));
        await periodsVm.OpenTrialBalanceCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Equal("trial-balance", route);
        var tbReq = Assert.IsType<OpenTrialBalanceRequest>(parameter);
        Assert.Equal(periodId, tbReq.PeriodId);

        // Trial Balance receives OpenTrialBalanceRequest and sets PeriodId filter
        api.Balance = new() { From = new(2026, 6, 1), To = new(2026, 6, 30), Rows = [], TotalPeriodDebit = "0", IsBalanced = true };
        using var balanceVm = new TrialBalanceViewModel(api, api, nav, new(loc), loc, TimeProvider.System);
        await balanceVm.ActivateAsync(Context(fixture, All), tbReq, Cancellation).ConfigureAwait(true);
        Assert.Equal(periodId, balanceVm.PeriodId);
        Assert.True(balanceVm.HasResult);
    }

    [Fact]
    public async Task NotificationsMapConflictAndNetworkWithoutLeakingExceptionText()
    {
        var loc = new LocalizationService("ar"); var notifications = new NotificationCenter(loc, TimeSpan.Zero); var operation = new DesktopOperation(loc, notifications);
        await operation.RunAsync(() => throw new PetraApiException("secret stack", 409, "{\"code\":\"concurrency.conflict\"}", null!, null)).ConfigureAwait(true);
        Assert.Equal("Conflict", operation.ErrorKey); Assert.Equal(loc["D_Toast_Conflict"], notifications.Items[0].Title); Assert.DoesNotContain("secret", notifications.Items[0].Message);
        await operation.RunAsync(() => throw new HttpRequestException("private address")).ConfigureAwait(true);
        Assert.Equal("ServerUnavailable", operation.ErrorKey); Assert.False(operation.IsOnline); Assert.Equal(loc["D_Toast_Network"], notifications.Items[0].Title);
    }

    [Fact]
    public async Task PaletteRejectsForgedOrRevokedCommandsAndNavigatesPermittedEntries()
    {
        var fixture = new WorkspaceFixture(); var loc = new LocalizationService("en"); using var workspace = new PetraERP.Desktop.Productivity.WorkspaceViewModel(fixture, loc, new TestThemes());
        await workspace.SetContextAsync(fixture.A, fixture.Branches[0]).ConfigureAwait(true);
        var navigation = new NavigationService(loc); navigation.Configure(["Ledger.Accounts.View"]); var navigator = new ShellNavigator(); string? route = null;
        navigator.Requested += (r, _) => { route = r; return Task.CompletedTask; };
        using var palette = new CommandPaletteViewModel(navigation, workspace, navigator, loc); palette.Open();
        Assert.DoesNotContain(palette.Entries, entry => entry.Route == "journals");
        await palette.InvokeAsync(new(PaletteEntryKind.Action, "fake", "fake", "", "", "accounts", null)).ConfigureAwait(true); Assert.Null(route);
        var allowed = Assert.Single(palette.Entries, e => e.Route == "accounts"); await palette.InvokeAsync(allowed).ConfigureAwait(true); Assert.Equal("accounts", route);
        route = null; navigation.Configure([]); await palette.InvokeAsync(allowed).ConfigureAwait(true); Assert.Null(route);
    }

    [Fact]
    public void EveryPersistentGridUsesServerApprovedKeysAndAllLiteralLabelsExist()
    {
        var root = FindRoot(); var ns = XNamespace.Get("clr-namespace:PetraERP.Desktop.Controls");
        var loc = new LocalizationService("en");
        foreach (var path in Directory.EnumerateFiles(Path.Combine(root, "src/PetraERP.Desktop"), "*.xaml", SearchOption.AllDirectories).Where(path => !path.Contains("\\obj\\", StringComparison.Ordinal)))
        {
            var document = XDocument.Load(path);
            foreach (var grid in document.Descendants().Where(element => element.Attribute(ns + "GridLayouts.Key") is not null))
            {
                var key = grid.Attribute(ns + "GridLayouts.Key")!.Value; Assert.True(WorkspaceRules.GridColumns.ContainsKey(key), key);
                foreach (var column in grid.Descendants().Where(element => element.Attribute("SortMemberPath") is not null))
                    Assert.Contains(column.Attribute("SortMemberPath")!.Value, WorkspaceRules.GridColumns[key]);
            }
            foreach (Match match in Regex.Matches(File.ReadAllText(path), @"Localization\[([A-Za-z0-9_]+)\]", RegexOptions.CultureInvariant))
                Assert.NotEqual(match.Groups[1].Value, loc[match.Groups[1].Value]);
        }
        foreach (var (type, prefix) in new[] { (typeof(AccountType), "D_AccountType_"), (typeof(CostCenterPolicy), "D_CostPolicy_"),
            (typeof(AccountCurrencyMode), "D_CurrencyMode_"), (typeof(AccountingPeriodState), "D_PeriodState_"), (typeof(JournalState), "D_State_") })
            foreach (var value in Enum.GetNames(type)) Assert.NotEqual(prefix + value, loc[prefix + value]);
        foreach (var permission in PermissionCatalog.All) Assert.NotEqual("Permission_" + permission.Code.Replace('.', '_'), loc["Permission_" + permission.Code.Replace('.', '_')]);
        foreach (var obsolete in new[] { "Controls/EntityWindowShell.xaml", "Controls/DocumentWindowShell.xaml", "Productivity/DocumentWorkflow.cs", "Productivity/WorkspaceView.xaml", "WorkspaceWindow.xaml" })
            Assert.False(File.Exists(Path.Combine(root, "src/PetraERP.Desktop", obsolete)), obsolete);
    }

    private static string FindRoot()
    {
        var root = Directory.GetCurrentDirectory(); while (!File.Exists(Path.Combine(root, "PetraERP.slnx"))) root = Directory.GetParent(root)!.FullName; return root;
    }
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;
}
