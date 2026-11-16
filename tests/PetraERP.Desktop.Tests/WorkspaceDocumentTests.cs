using PetraERP.ApiClient;
using PetraERP.Application.Security;
using PetraERP.Desktop.Accounting;
using PetraERP.Desktop.GeneralLedger;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Tests;

/// <summary>Multi-document workspace: stable scoped keys, activation instead of duplication, safe close and the account card links.</summary>
public sealed class WorkspaceDocumentTests
{
    private static readonly string[] All = PermissionCatalog.All.Select(permission => permission.Code).ToArray();

    [Fact]
    public void ManagerActivatesExistingKeysCyclesAndReturnsToThePreviousDocumentOnClose()
    {
        var manager = new WorkspaceDocumentManager("Home");
        WorkspaceDocument Doc(string key) => new(key, WorkspaceDocumentKind.Route, key, new object(), key, string.Empty, Guid.Empty, Guid.Empty);
        var first = manager.Open(Doc("a")); var second = manager.Open(Doc("b"));
        Assert.Same(first, manager.Open(Doc("a"))); Assert.Equal(3, manager.Count); Assert.Same(first, manager.Active);
        Assert.True(first.IsActive); Assert.False(second.IsActive);
        Assert.Same(second, manager.Neighbor(1)); Assert.Same(manager.Home, manager.Neighbor(-1));
        manager.Activate(second); manager.Activate(first);
        Assert.True(manager.Remove(first)); Assert.Same(second, manager.Active);
        Assert.False(manager.Remove(manager.Home)); Assert.False(manager.Home.IsClosable);
        manager.Reset(); Assert.Same(manager.Home, Assert.Single(manager.Documents)); Assert.Same(manager.Home, manager.Active);
    }

    [Fact]
    public void DocumentKeysCarryCompanyBranchAndAccount()
    {
        var company = Guid.NewGuid(); var branch = Guid.NewGuid(); var account = Guid.NewGuid();
        Assert.Equal($"Account/{company:N}/{branch:N}/{account:N}", WorkspaceDocument.AccountKey(company, branch, account));
        Assert.Equal($"Route/journals/{company:N}/{branch:N}", WorkspaceDocument.RouteKey("journals", company, branch));
        Assert.Equal("Route/users", WorkspaceDocument.RouteKey("users", null, null));
        var journal = Guid.NewGuid();
        Assert.Equal($"Journal/{company:N}/{branch:N}/{journal:N}", WorkspaceDocument.JournalKey(company, branch, journal));
        Assert.Equal($"JournalDraft/{company:N}/{branch:N}/7", WorkspaceDocument.DraftKey(company, branch, 7));
        Assert.NotEqual(WorkspaceDocument.DraftKey(company, branch, 1), WorkspaceDocument.DraftKey(company, branch, 2));
    }

    [Fact]
    public void ManagerNeverDetachesTheActiveDocumentAndRekeysOnlyToAFreeKey()
    {
        var manager = new WorkspaceDocumentManager("Home");
        WorkspaceDocument Doc(string key) => new(key, WorkspaceDocumentKind.Journal, "journals", new object(), key, string.Empty, Guid.Empty, Guid.Empty);
        var first = manager.Open(Doc("a")); var second = manager.Open(Doc("b"));
        WorkspaceDocument? requested = null; manager.DetachedActivationRequested += (_, document) => requested = document;
        Assert.False(manager.DetachDocument(manager.Home)); Assert.False(manager.AttachDocument(second));
        Assert.True(manager.DetachDocument(second));
        Assert.True(second.IsDetached); Assert.False(second.IsActive); Assert.Same(first, manager.Active);
        Assert.False(manager.DetachDocument(second));
        // Activating a detached document asks its window to come forward; the main host keeps its document.
        manager.Activate(second); Assert.Same(second, requested); Assert.Same(first, manager.Active);
        foreach (var direction in new[] { 1, 1, 1, -1, -1 }) { manager.Activate(manager.Neighbor(direction)); Assert.False(manager.Active.IsDetached); }
        Assert.True(manager.DetachDocument(manager.Find("a")!)); Assert.Same(manager.Home, manager.Active);
        Assert.True(manager.AttachDocument(second)); Assert.Same(second, manager.Active); Assert.False(second.IsDetached);
        Assert.False(manager.Rekey(second, "a")); Assert.True(manager.Rekey(second, "c")); Assert.Same(second, manager.Find("c")); Assert.Null(manager.Find("b"));
        Assert.True(manager.Remove(first)); Assert.False(first.IsDetached); Assert.Same(second, manager.Active);
    }

    [Fact]
    public async Task TwoNewDraftsOpenSideBySideEachWithItsOwnViewModelAndASavedDraftTakesItsJournalKey()
    {
        var fixture = new ShellFixture(); fixture.User.Permissions = All; var client = new FakeLedgerClient();
        using var shell = fixture.Create(new AdministrationFixture(), client); shell.UserName = "admin"; shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);
        await shell.NavigateToAsync("journals", NewJournalRequest.Instance).ConfigureAwait(true);
        var first = shell.ActiveJournal!; var firstDocument = shell.Documents.Active;
        await shell.NavigateToAsync("journals", NewJournalRequest.Instance).ConfigureAwait(true);
        var second = shell.ActiveJournal!; var secondDocument = shell.Documents.Active;
        Assert.NotSame(first, second); Assert.NotSame(shell.Ledger, first); Assert.NotSame(shell.Ledger, second);
        Assert.NotEqual(firstDocument.Key, secondDocument.Key);
        var draftPrefix = $"JournalDraft/{fixture.Company.Id:N}/{fixture.Branch.Id:N}/";
        Assert.StartsWith(draftPrefix, firstDocument.Key, StringComparison.Ordinal); Assert.StartsWith(draftPrefix, secondDocument.Key, StringComparison.Ordinal);
        Assert.All(new[] { firstDocument, secondDocument }, document => Assert.Equal(WorkspaceDocumentKind.Journal, document.Kind));
        Assert.Equal("قيد جديد", firstDocument.Title);
        first.JournalDescription = "القيد الأول"; second.JournalDescription = "القيد الثاني";
        Assert.True(firstDocument.IsDirty); Assert.True(secondDocument.IsDirty);
        shell.Documents.Activate(firstDocument);
        Assert.Same(first, shell.CurrentView); Assert.Equal("القيد الأول", first.JournalDescription); Assert.Equal("القيد الثاني", second.JournalDescription);

        first.Lines[0].AccountId = Guid.NewGuid(); first.Lines[0].Debit = 10.5m; first.Lines[1].AccountId = Guid.NewGuid(); first.Lines[1].Credit = 10.5m;
        await first.SaveDraftCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Empty(shell.Operation.Error);
        var id = first.CurrentJournalId!.Value;
        Assert.Equal(WorkspaceDocument.JournalKey(fixture.Company.Id, fixture.Branch.Id, id), firstDocument.Key);
        Assert.Contains("القيد الأول", firstDocument.Title, StringComparison.Ordinal); Assert.False(firstDocument.IsDirty);

        // Opening the saved journal from anywhere activates its document instead of duplicating it.
        shell.Documents.Activate(secondDocument);
        await shell.NavigateToAsync("journals", new OpenJournalRequest(id)).ConfigureAwait(true);
        Assert.Same(first, shell.CurrentView); Assert.Equal(2, shell.Documents.Documents.Count(document => document.Kind == WorkspaceDocumentKind.Journal));
        // The route page stays the list, with its own model and no editor.
        await shell.NavigateToAsync("journals", null).ConfigureAwait(true);
        Assert.Same(shell.Ledger, shell.CurrentView); Assert.False(shell.Ledger.IsEditorOpen); Assert.Contains(shell.Ledger.Journals, item => item.Id == id);
        // The list opens a selected journal as its document.
        shell.Ledger.SelectedJournal = shell.Ledger.Journals.Single(item => item.Id == id);
        await shell.Ledger.OpenJournalCommand.ExecuteAsync(null).ConfigureAwait(true); Assert.Same(first, shell.CurrentView);
        // Deleting the saved draft closes its document; the other draft keeps its input.
        await first.DeleteDraftCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.DoesNotContain(firstDocument, shell.Documents.Documents); Assert.True(secondDocument.IsDirty);
        Assert.Equal("القيد الثاني", second.JournalDescription);
    }

    [Fact]
    public async Task OutageRetainsOnlyJournalDocumentsWithAnUnknownPostingOutcomeAndTheyCannotBeClosedUntilResolved()
    {
        var fixture = new ShellFixture(); fixture.User.Permissions = All; var client = new FakeLedgerClient();
        using var shell = fixture.Create(new AdministrationFixture(), client); shell.UserName = "admin"; shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);
        var pending = client.Add(fixture.Company.Id, fixture.Branch.Id, JournalState.Approved);
        var other = client.Add(fixture.Company.Id, fixture.Branch.Id, JournalState.Approved);
        await shell.NavigateToAsync("journals", new OpenJournalRequest(pending.Id)).ConfigureAwait(true); var pendingModel = shell.ActiveJournal!;
        await shell.NavigateToAsync("journals", new OpenJournalRequest(other.Id)).ConfigureAwait(true); var otherModel = shell.ActiveJournal!;
        Assert.NotSame(pendingModel, otherModel);
        pendingModel.ActionReason = "ترحيل موثق"; client.PostingFailures.Enqueue((new HttpRequestException("ambiguous outcome"), false));
        await pendingModel.PostJournalCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.True(pendingModel.HasPendingPostingAttempt); Assert.False(otherModel.HasPendingPostingAttempt);
        Assert.False(shell.Operation.IsOnline); Assert.Same(shell.Documents.Home, Assert.Single(shell.Documents.Documents));

        await shell.RefreshAsync().ConfigureAwait(true); Assert.True(shell.ContextReady);
        var pendingKey = WorkspaceDocument.JournalKey(fixture.Company.Id, fixture.Branch.Id, pending.Id);
        var restored = shell.Documents.Find(pendingKey)!; Assert.Same(pendingModel, restored.Content);
        Assert.Null(shell.Documents.Find(WorkspaceDocument.JournalKey(fixture.Company.Id, fixture.Branch.Id, other.Id)));
        // A journal whose posting outcome is unknown cannot be closed, whichever document it is.
        await shell.CloseDocumentCommand.ExecuteAsync(restored).ConfigureAwait(true);
        Assert.Contains(restored, shell.Documents.Documents); Assert.True(pendingModel.HasPendingPostingAttempt);

        await shell.NavigateToAsync("journals", new OpenJournalRequest(other.Id)).ConfigureAwait(true);
        Assert.NotSame(otherModel, shell.ActiveJournal); Assert.False(shell.ActiveJournal!.HasPendingPostingAttempt);

        shell.Documents.Activate(restored);
        pendingModel.ActionReason = "ترحيل موثق"; await pendingModel.PostJournalCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Equal(2, client.PostCalls.Count); Assert.Equal(client.PostCalls[0].Key, client.PostCalls[1].Key);
        Assert.False(pendingModel.HasPendingPostingAttempt); Assert.Equal(1, client.Get(pending.Id).PostingSequence);
        await shell.CloseDocumentCommand.ExecuteAsync(restored).ConfigureAwait(true); Assert.DoesNotContain(restored, shell.Documents.Documents);
    }

    [Fact]
    public async Task DetachedDocumentLeavesTheTabHostButKeepsTheCloseScopeAndPermissionRules()
    {
        var fixture = new ShellFixture(); fixture.User.Permissions = All; var client = new FakeLedgerClient();
        using var shell = fixture.Create(new AdministrationFixture(), client); shell.UserName = "admin"; shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);
        await shell.NavigateToAsync("accounts", null).ConfigureAwait(true);
        await shell.NavigateToAsync("journals", NewJournalRequest.Instance).ConfigureAwait(true);
        var draft = shell.ActiveJournal!; var document = shell.Documents.Active; draft.JournalDescription = "مسودة منفصلة";
        Assert.False(shell.DetachDocumentCommand.CanExecute(shell.Documents.Home)); Assert.True(shell.DetachActiveDocumentCommand.CanExecute(null));
        shell.DetachActiveDocumentCommand.Execute(null);
        Assert.True(document.IsDetached); Assert.Same(shell.Pages.Accounts, shell.CurrentView); Assert.Null(shell.ActiveJournal);
        WorkspaceDocument? requested = null; shell.Documents.DetachedActivationRequested += (_, item) => requested = item;
        shell.ActivateDocumentCommand.Execute(document); Assert.Same(document, requested); Assert.Same(shell.Pages.Accounts, shell.CurrentView);

        fixture.Confirm = false;
        await shell.CloseDocumentCommand.ExecuteAsync(document).ConfigureAwait(true);
        Assert.Equal(("D_ConfirmCloseDirtyDocument", (string?)null), (fixture.LastConfirmation, fixture.LastAlternate)); Assert.Contains(document, shell.Documents.Documents); Assert.True(document.IsDetached);
        await shell.LogoutCommand.ExecuteAsync(null).ConfigureAwait(true); Assert.True(shell.IsAuthenticated); Assert.Equal("مسودة منفصلة", draft.JournalDescription);

        shell.AttachDocumentCommand.Execute(document);
        Assert.False(document.IsDetached); Assert.Same(draft, shell.CurrentView); Assert.Equal("مسودة منفصلة", draft.JournalDescription);

        // Revalidation closes a detached document like any tab.
        shell.DetachDocumentCommand.Execute(document); fixture.User.Permissions = ["Ledger.Accounts.View"];
        await shell.RefreshAsync().ConfigureAwait(true);
        Assert.DoesNotContain(document, shell.Documents.Documents); Assert.False(document.IsDetached);
    }

    [Fact]
    public async Task RoutesOpenAsTabsWithoutDuplicatesAndShortcutsCycleThem()
    {
        var fixture = new ShellFixture(); fixture.User.Permissions = All;
        using var shell = await LoginAsync(fixture, new AccountingFixture());
        await shell.NavigateToAsync("accounts", null).ConfigureAwait(true);
        await shell.NavigateToAsync("cost-centers", null).ConfigureAwait(true);
        await shell.NavigateToAsync("accounts", null).ConfigureAwait(true);
        Assert.Equal(3, shell.Documents.Count); Assert.Same(shell.Pages.Accounts, shell.CurrentView); Assert.Equal("accounts", shell.Navigation.Current);
        shell.NextDocumentCommand.Execute(null); Assert.Same(shell.Pages.CostCenters, shell.CurrentView); Assert.Equal("cost-centers", shell.Navigation.Current);
        shell.NextDocumentCommand.Execute(null); Assert.True(shell.IsHome);
        shell.PreviousDocumentCommand.Execute(null); Assert.Same(shell.Pages.CostCenters, shell.CurrentView);
        await shell.CloseOtherDocumentsCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Equal(2, shell.Documents.Count); Assert.Same(shell.Pages.CostCenters, shell.CurrentView); Assert.False(shell.Pages.Accounts.HasContext);
        await shell.CloseAllDocumentsCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.True(shell.IsHome); Assert.Single(shell.Documents.Documents); Assert.False(shell.Pages.Accounts.HasContext);
    }

    /// <summary>Reports are work instruments, not pages of the shell: each one opens in its own window and is skipped by tab cycling.</summary>
    [Theory]
    [InlineData("account-ledger")]
    [InlineData("trial-balance")]
    public async Task ReportRoutesOpenDetachedAndStayOutOfTheTabStrip(string route)
    {
        var fixture = new ShellFixture(); fixture.User.Permissions = All;
        using var shell = await LoginAsync(fixture, new AccountingFixture());
        await shell.NavigateToAsync(route, null).ConfigureAwait(true);
        var document = shell.Documents.Documents.Single(item => item.Route == route);
        Assert.True(document.IsDetached);
        // The report still owns a document (close rules, recents and scope revalidation apply), but not the tab host.
        Assert.NotSame(document, shell.Documents.Active);
        await shell.NavigateToAsync("accounts", null).ConfigureAwait(true);
        Assert.Same(shell.Pages.Accounts, shell.CurrentView);
        shell.NextDocumentCommand.Execute(null);
        Assert.False(shell.Documents.Active.IsDetached); Assert.NotSame(document, shell.Documents.Active);
        // Re-activating the report asks its own window to come forward instead of opening a second document.
        await shell.NavigateToAsync(route, null).ConfigureAwait(true);
        Assert.Same(document, shell.Documents.Documents.Single(item => item.Route == route));
    }

    /// <summary>The party statements need the server report catalog, so only their window declaration is checked here.</summary>
    [Theory]
    [InlineData("account-ledger")]
    [InlineData("trial-balance")]
    [InlineData("customer-statement")]
    [InlineData("supplier-statement")]
    public void EveryReportRouteIsDeclaredToOpenAsItsOwnWindow(string route) => Assert.True(DesktopViewModel.OpensAsWindow(route));

    [Fact]
    public async Task TreeOpensOneCardPerAccountAndTheCardLinksToLedgerAndJournal()
    {
        var fixture = new ShellFixture(); fixture.User.Permissions = All;
        var (accounting, parent, cash) = Chart();
        var journalId = Guid.NewGuid();
        accounting.Ledger = new() { Account = accounting.Accounts[1], BaseCurrencyCode = "JOD", OpeningBalance = "0", PeriodDebit = "125.4321", PeriodCredit = "0", ClosingBalance = "125.4321", Page = 1, PageSize = 10, TotalCount = 1,
            Lines = [new() { JournalId = journalId, AccountingDate = new(2026, 10, 1), PostingSequence = 7, LineDescription = "تمويل", BaseDebit = "125.4321", BaseCredit = "0", RunningBalance = "125.4321" }] };
        using var shell = await LoginAsync(fixture, accounting);
        await shell.NavigateToAsync("accounts", null).ConfigureAwait(true);
        var tree = shell.Pages.Accounts;
        tree.SelectedNode = tree.Nodes.Single(node => node.Id == cash);
        Assert.True(tree.OpenCardCommand.CanExecute(null)); Assert.True(tree.OpenLedgerCommand.CanExecute(null));
        await tree.OpenCardCommand.ExecuteAsync(null).ConfigureAwait(true);
        var card = Assert.IsType<AccountCardViewModel>(shell.CurrentView);
        Assert.Equal(WorkspaceDocument.AccountKey(fixture.Company.Id, fixture.Branch.Id, cash), shell.Documents.Active.Key);
        Assert.Equal("1001 — الصندوق", card.Title); Assert.Equal(card.Title, shell.Documents.Active.Title);
        Assert.Equal("1000 — الأصول المتداولة", card.ParentLabel); Assert.Equal(2, card.Path.Count); Assert.Equal("125.4321", card.Closing);
        Assert.Single(card.RecentLines); Assert.Equal(WorkspaceDocumentKind.AccountCard, shell.Documents.Active.Kind);

        await tree.OpenCardCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Equal(3, shell.Documents.Count); Assert.Same(card, shell.CurrentView);

        await card.OpenParentCommand.ExecuteAsync(null).ConfigureAwait(true);
        var parentCard = Assert.IsType<AccountCardViewModel>(shell.CurrentView);
        Assert.Equal(parent, parentCard.Account!.Id); Assert.Single(parentCard.Children); Assert.Equal(4, shell.Documents.Count);

        shell.Documents.Activate(shell.Documents.Find(WorkspaceDocument.AccountKey(fixture.Company.Id, fixture.Branch.Id, cash)));
        await card.OpenLedgerCommand.ExecuteAsync(null).ConfigureAwait(true);
        // The ledger opens as its own window, so the card the operator was reading stays where it was.
        var ledgerDocument = shell.Documents.Documents.Single(document => document.Route == "account-ledger");
        Assert.True(ledgerDocument.IsDetached);
        Assert.Same(shell.Pages.AccountLedger, ledgerDocument.Content);
        Assert.Equal(cash, shell.Pages.AccountLedger.AccountId);
        Assert.False(shell.Pages.AccountLedger.Consolidated);

        Assert.True(card.OpenJournalCommand.CanExecute(null));
        await card.OpenJournalCommand.ExecuteAsync(card.RecentLines[0]).ConfigureAwait(true);
        // The journal opens as its own document (the fixture's null ledger has no such journal, so no empty tab is left).
        Assert.DoesNotContain(shell.Documents.Documents, document => document.Kind == WorkspaceDocumentKind.Journal);
        Assert.Same(card, shell.CurrentView);
    }

    [Fact]
    public async Task BranchSwitchClosesCardsOfTheOldScopeAndRevocationClosesTheirTabs()
    {
        var fixture = new ShellFixture(); fixture.User.Permissions = All;
        var (accounting, _, cash) = Chart();
        using var shell = await LoginAsync(fixture, accounting);
        await shell.NavigateToAsync("accounts", new OpenAccountCardRequest(cash)).ConfigureAwait(true);
        Assert.IsType<AccountCardViewModel>(shell.CurrentView);
        var other = new BranchContextResponse { Id = Guid.NewGuid(), CompanyId = fixture.Company.Id, Code = "B2", NameArabic = "فرع ثان" };
        fixture.AdditionalBranches.Add(other); await shell.RefreshAsync().ConfigureAwait(true);
        await shell.SelectBranchCommand.ExecuteAsync(other).ConfigureAwait(true);
        Assert.DoesNotContain(shell.Documents.Documents, document => document.Kind == WorkspaceDocumentKind.AccountCard);
        Assert.All(shell.Documents.Documents.Where(document => document.IsScoped), document => Assert.Equal(other.Id, document.BranchId));

        await shell.NavigateToAsync("accounts", new OpenAccountCardRequest(cash)).ConfigureAwait(true);
        Assert.Equal(WorkspaceDocument.AccountKey(fixture.Company.Id, other.Id, cash), shell.Documents.Active.Key);
        fixture.User.Permissions = ["Ledger.Reports.View"];
        await shell.RefreshAsync().ConfigureAwait(true);
        Assert.True(shell.IsHome); Assert.DoesNotContain(shell.Documents.Documents, document => document.Route == "accounts");
        await shell.NavigateToAsync("accounts", new OpenAccountCardRequest(cash)).ConfigureAwait(true);
        Assert.True(shell.IsHome);
    }

    [Fact]
    public async Task TreeCommandsFollowPermissionsAndEditRequestsEditorFocus()
    {
        var fixture = new ShellFixture(); fixture.User.Permissions = ["Ledger.Accounts.View", "Ledger.Accounts.Manage"];
        var (accounting, _, cash) = Chart();
        using var shell = await LoginAsync(fixture, accounting);
        await shell.NavigateToAsync("accounts", null).ConfigureAwait(true);
        var tree = shell.Pages.Accounts; tree.SelectedNode = tree.Nodes.Single(node => node.Id == cash);
        // Top menu lists only permitted, implemented routes; the Administration menu is hidden without its permissions.
        Assert.Equal(["accounts", "account-directory", "fx-revaluation"], shell.Navigation.AccountingItems.Select(item => item.Id)); Assert.False(shell.Navigation.HasAdministration);
        Assert.False(shell.CanCreateJournal); Assert.False(shell.NewJournalCommand.CanExecute(null));
        Assert.False(tree.CanViewLedger); Assert.False(tree.OpenLedgerCommand.CanExecute(null)); Assert.True(tree.OpenCardCommand.CanExecute(null));
        var requested = 0; tree.EditRequested += (_, _) => requested++;
        Assert.True(tree.EditCommand.CanExecute(null)); tree.EditCommand.Execute(null);
        Assert.Equal(1, requested); Assert.True(tree.IsEditorOpen); Assert.Equal("1001", tree.Code);
        await tree.OpenCardCommand.ExecuteAsync(null).ConfigureAwait(true);
        var card = Assert.IsType<AccountCardViewModel>(shell.CurrentView);
        Assert.False(card.CanViewLedger); Assert.False(card.HasSummary); Assert.False(card.OpenLedgerCommand.CanExecute(null));
        Assert.True(card.EditCommand.CanExecute(null));
        await card.EditCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Same(tree, shell.CurrentView); Assert.Equal(cash, tree.SelectedNode!.Id);
    }

    private static (AccountingFixture Accounting, Guid Parent, Guid Cash) Chart()
    {
        var accounting = new AccountingFixture(); var parent = Guid.NewGuid(); var cash = Guid.NewGuid();
        accounting.Accounts.AddRange([
            new() { Id = parent, Code = "1000", NameArabic = "الأصول المتداولة", NameEnglish = "Current assets", AccountType = AccountType.Assets, IsActive = true, PostingAllowed = false, HasChildren = true, Level = 0, Version = "AQ==" },
            new() { Id = cash, Code = "1001", NameArabic = "الصندوق", NameEnglish = "Cash", AccountType = AccountType.Assets, IsActive = true, PostingAllowed = true, ParentId = parent, Level = 1, Version = "Ag==" }]);
        return (accounting, parent, cash);
    }

    private static async Task<DesktopViewModel> LoginAsync(ShellFixture fixture, AccountingFixture accounting)
    {
        var shell = fixture.Create(new AdministrationFixture(), accounting: accounting);
        shell.UserName = "admin"; shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);
        return shell;
    }

    [Fact]
    public async Task CloseOffersSaveForSavableDocumentsAndClosesOnlyAfterASuccessfulSave()
    {
        var fixture = new ShellFixture(); fixture.User.Permissions = All; var client = new FakeLedgerClient();
        using var shell = fixture.Create(new AdministrationFixture(), client); shell.UserName = "admin"; shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);
        await shell.NavigateToAsync("journals", NewJournalRequest.Instance).ConfigureAwait(true);
        var draft = shell.ActiveJournal!; var document = shell.Documents.Active;

        // Incomplete required fields: Save is unavailable; cancelling the discard prompt retains the input.
        fixture.Confirm = false;
        fixture.Choice = DialogChoice.Alternate;
        await shell.CloseDocumentCommand.ExecuteAsync(document).ConfigureAwait(true);
        Assert.Equal(("D_ConfirmCloseDirtyDocument", (string?)null), (fixture.LastConfirmation, fixture.LastAlternate));
        Assert.Contains(document, shell.Documents.Documents); Assert.False(draft.CanSaveDraft); Assert.Null(client.LastCreateRequest);

        // Cancel keeps it; Save with valid input saves through the server and closes.
        draft.JournalDescription = "حفظ عند الإغلاق";
        draft.Lines[0].AccountId = Guid.NewGuid(); draft.Lines[0].Debit = 7m; draft.Lines[1].AccountId = Guid.NewGuid(); draft.Lines[1].Credit = 7m;
        fixture.Choice = DialogChoice.Cancel;
        await shell.CloseDocumentCommand.ExecuteAsync(document).ConfigureAwait(true);
        Assert.Contains(document, shell.Documents.Documents); Assert.Null(client.LastCreateRequest);
        fixture.Choice = DialogChoice.Alternate;
        await shell.CloseDocumentCommand.ExecuteAsync(document).ConfigureAwait(true);
        Assert.Equal("حفظ عند الإغلاق", client.LastCreateRequest!.Description);
        Assert.DoesNotContain(document, shell.Documents.Documents);

        // Discard closes without saving.
        await shell.NavigateToAsync("journals", NewJournalRequest.Instance).ConfigureAwait(true);
        var second = shell.Documents.Active; shell.ActiveJournal!.JournalDescription = "لا يُحفظ";
        fixture.Confirm = true;
        fixture.Choice = DialogChoice.Confirm;
        await shell.CloseDocumentCommand.ExecuteAsync(second).ConfigureAwait(true);
        Assert.DoesNotContain(second, shell.Documents.Documents); Assert.Equal("حفظ عند الإغلاق", client.LastCreateRequest.Description);
    }

    [Fact]
    public async Task ChartOfAccountsCloseCanSaveTheEditorAndAJournalWriteRefreshesTheListWhenShown()
    {
        var fixture = new ShellFixture(); fixture.User.Permissions = All; var client = new FakeLedgerClient();
        var (accounting, parent, _) = Chart();
        using var shell = fixture.Create(new AdministrationFixture(), client, accounting: accounting); shell.UserName = "admin"; shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);
        await shell.NavigateToAsync("accounts", null).ConfigureAwait(true);
        var tree = shell.Pages.Accounts; tree.SelectedNode = tree.Nodes.Single(node => node.Id == parent);
        tree.NewChildCommand.Execute(null); tree.Code = "1102"; tree.NameArabic = "البنك";
        fixture.Choice = DialogChoice.Alternate;
        await shell.CloseActiveDocumentCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Contains(accounting.Accounts, account => account.Code == "1102" && account.ParentId == parent);
        Assert.DoesNotContain(shell.Documents.Documents, document => document.Route == "accounts");

        // The list is loaded, then a journal document saves: the list reloads when it is shown again.
        await shell.NavigateToAsync("journals", null).ConfigureAwait(true);
        var listDocument = shell.Documents.Active; Assert.Empty(shell.Ledger.Journals);
        await shell.NavigateToAsync("journals", NewJournalRequest.Instance).ConfigureAwait(true);
        var draft = shell.ActiveJournal!; draft.JournalDescription = "قيد جديد للقائمة";
        draft.Lines[0].AccountId = Guid.NewGuid(); draft.Lines[0].Debit = 3m; draft.Lines[1].AccountId = Guid.NewGuid(); draft.Lines[1].Credit = 3m;
        await draft.SaveDraftCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.True(shell.Ledger.IsListStale); Assert.Empty(shell.Ledger.Journals);
        shell.Documents.Activate(listDocument);
        Assert.False(shell.Ledger.IsListStale); Assert.Contains(shell.Ledger.Journals, item => item.Id == draft.CurrentJournalId);
    }

    [Fact]
    public async Task OnlyAnUnusedLeafCanBeDeletedWithAReasonAndItsCardCloses()
    {
        var fixture = new ShellFixture(); fixture.User.Permissions = All;
        var (accounting, parent, cash) = Chart();
        var unused = new AccountResponse { Id = Guid.NewGuid(), ParentId = parent, Level = 2, Code = "1109", NameArabic = "صندوق مؤقت", AccountType = AccountType.Assets,
            PostingAllowed = true, IsActive = true, Version = "AAAAAAAAB9A=" };
        accounting.Accounts.Add(unused);
        accounting.Accounts.Single(account => account.Id == cash).HasJournalLines = true;
        using var shell = await LoginAsync(fixture, accounting);
        await shell.NavigateToAsync("accounts", new OpenAccountCardRequest(unused.Id)).ConfigureAwait(true);
        var card = shell.Documents.Active; Assert.IsType<AccountCardViewModel>(card.Content);
        await shell.NavigateToAsync("accounts", null).ConfigureAwait(true);
        var tree = shell.Pages.Accounts;

        tree.SelectedNode = tree.Nodes.Single(node => node.Id == cash);
        Assert.False(tree.RequestDeleteCommand.CanExecute(null)); Assert.False(tree.CanDelete);
        tree.SelectedNode = tree.Nodes.Single(node => node.Id == parent);
        Assert.False(tree.RequestDeleteCommand.CanExecute(null));

        tree.SelectedNode = tree.Nodes.Single(node => node.Id == unused.Id);
        Assert.True(tree.RequestDeleteCommand.CanExecute(null));
        tree.RequestDeleteCommand.Execute(null);
        Assert.True(tree.IsDeletePending); Assert.True(tree.CanDelete); Assert.False(tree.DeleteCommand.CanExecute(null));
        tree.DeleteReason = "   "; Assert.False(tree.DeleteCommand.CanExecute(null));
        tree.DeleteReason = "  أُنشئ بالخطأ  "; Assert.True(tree.DeleteCommand.CanExecute(null));
        fixture.Confirm = false;
        await tree.DeleteCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Equal("D_ConfirmDeleteAccount", fixture.LastConfirmation); Assert.Empty(accounting.Deletions);
        fixture.Confirm = true;
        await tree.DeleteCommand.ExecuteAsync(null).ConfigureAwait(true);
        var (deletedId, request) = Assert.Single(accounting.Deletions);
        Assert.Equal((unused.Id, "أُنشئ بالخطأ", "AAAAAAAAB9A="), (deletedId, request.Reason, request.ExpectedVersion));
        Assert.DoesNotContain(tree.Nodes, node => node.Id == unused.Id); Assert.False(tree.IsDeletePending); Assert.Empty(shell.Operation.Error);
        // The deleted account's card is closed, not left pointing at a missing account.
        Assert.DoesNotContain(card, shell.Documents.Documents);

        // Cancel clears a pending delete.
        tree.SelectedNode = tree.Nodes.Single(node => node.Id == cash);
        Assert.False(tree.IsDeletePending);
    }

    [Fact]
    public async Task CardRecordNavigationMovesTheSameTabInCodeOrderAndActivatesAnAlreadyOpenCard()
    {
        var fixture = new ShellFixture(); fixture.User.Permissions = All;
        var (accounting, parent, cash) = Chart();
        var bank = new AccountResponse { Id = Guid.NewGuid(), ParentId = parent, Level = 1, Code = "1002", NameArabic = "البنك", AccountType = AccountType.Assets,
            PostingAllowed = true, IsActive = true, Version = "Aw==" };
        accounting.Accounts.Add(bank);
        using var shell = await LoginAsync(fixture, accounting);
        await shell.NavigateToAsync("accounts", new OpenAccountCardRequest(cash)).ConfigureAwait(true);
        var document = shell.Documents.Active; var card = Assert.IsType<AccountCardViewModel>(document.Content);
        var count = shell.Documents.Count;

        await card.NextCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Same(document, shell.Documents.Active); Assert.Equal(count, shell.Documents.Count);
        Assert.Equal(bank.Id, card.AccountId); Assert.Equal(WorkspaceDocument.AccountKey(fixture.Company.Id, fixture.Branch.Id, bank.Id), document.Key);
        Assert.Equal("1002 — البنك", document.Title);

        // Past the end nothing moves; the card says so.
        await card.NextCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Equal(bank.Id, card.AccountId); Assert.Equal(shell.Localization["D_NoNextRecord"], card.NavigationHint);
        await card.FirstCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Equal(parent, card.AccountId); Assert.Empty(card.NavigationHint);
        await card.LastCommand.ExecuteAsync(null).ConfigureAwait(true);
        await card.PreviousCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Equal(cash, card.AccountId);
        Assert.Equal(5, accounting.NavigationCalls);

        // When the target already has its own card, that card is activated and the source card stays as it is.
        await shell.NavigateToAsync("accounts", new OpenAccountCardRequest(bank.Id)).ConfigureAwait(true);
        var bankDocument = shell.Documents.Active; Assert.NotSame(document, bankDocument);
        shell.Documents.Activate(document);
        await card.NextCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Same(bankDocument, shell.Documents.Active); Assert.Equal(cash, card.AccountId);
        Assert.Equal(WorkspaceDocument.AccountKey(fixture.Company.Id, fixture.Branch.Id, cash), document.Key);
    }

    [Fact]
    public async Task AccountCardHasExplicitViewEditAndNewModesWithRowVersionAndDirtyRules()
    {
        var fixture = new ShellFixture(); fixture.User.Permissions = All;
        var (accounting, parent, cash) = Chart();
        using var shell = await LoginAsync(fixture, accounting);
        await shell.NavigateToAsync("accounts", new OpenAccountCardRequest(cash)).ConfigureAwait(true);
        var document = shell.Documents.Active; var card = Assert.IsType<AccountCardViewModel>(document.Content);
        Assert.Equal(EntityCardMode.View, card.Mode); Assert.False(card.BeginNewCommand.CanExecute(null)); // a posting account has no sub-accounts

        card.BeginEditCommand.Execute(null);
        Assert.True(card.IsEditMode); Assert.True(card.CanEditCode); Assert.False(card.IsCodeReadOnly); Assert.False(card.NextCommand.CanExecute(null));
        card.DraftNameArabic = "الصندوق الرئيسي"; card.DraftCostCenterPolicy = CostCenterPolicy.Required;
        Assert.True(card.HasUnsavedChanges); Assert.True(document.IsDirty);
        // Closing the dirty card offers Save; Cancel keeps it.
        fixture.Choice = DialogChoice.Cancel;
        await shell.CloseDocumentCommand.ExecuteAsync(document).ConfigureAwait(true);
        Assert.Equal(("D_ConfirmCloseDirtyDocument", "D_SaveAndClose"), (fixture.LastConfirmation, fixture.LastAlternate)); Assert.Contains(document, shell.Documents.Documents);
        await card.SaveCardCommand.ExecuteAsync(null).ConfigureAwait(true);
        var update = Assert.Single(accounting.Updates);
        Assert.Equal(("الصندوق الرئيسي", CostCenterPolicy.Required, AccountType.Assets, true, parent), (update.NameArabic, update.CostCenterPolicy, update.AccountType, update.PostingAllowed, update.ParentId));
        Assert.True(card.IsViewMode); Assert.False(document.IsDirty); Assert.Equal("1001 — الصندوق الرئيسي", document.Title);

        // Stale row version: the server rejects, the card stays in Edit with the input.
        card.BeginEditCommand.Execute(null); card.DraftNameArabic = "تعديل متزامن";
        var stored = accounting.Accounts.Single(a => a.Id == cash);
        var changed = System.Text.Json.JsonSerializer.Deserialize<AccountResponse>(System.Text.Json.JsonSerializer.Serialize(stored))!; changed.Version = "changed-elsewhere";
        accounting.Accounts[accounting.Accounts.IndexOf(stored)] = changed;
        await card.SaveCardCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.True(card.IsEditMode); Assert.Equal("تعديل متزامن", card.DraftNameArabic); Assert.NotEmpty(shell.Operation.Error);
        fixture.Confirm = false; await card.CancelCardCommand.ExecuteAsync(null).ConfigureAwait(true); Assert.True(card.IsEditMode);
        fixture.Confirm = true; await card.CancelCardCommand.ExecuteAsync(null).ConfigureAwait(true); Assert.True(card.IsViewMode);
        Assert.Equal("D_ConfirmDiscardChanges", fixture.LastConfirmation);

        // New: a sub-account of a control account, created through the server, then shown in the same tab.
        await card.OpenParentCommand.ExecuteAsync(null).ConfigureAwait(true);
        var parentDocument = shell.Documents.Active; var parentCard = Assert.IsType<AccountCardViewModel>(parentDocument.Content);
        Assert.True(parentCard.BeginNewCommand.CanExecute(null));
        parentCard.BeginNewCommand.Execute(null);
        Assert.True(parentCard.IsNewMode); Assert.True(parentCard.CanEditCode); Assert.Equal("1000", parentCard.DraftCode);
        Assert.False(parentCard.CanSaveDraft);
        parentCard.DraftCode = "1003"; parentCard.DraftNameArabic = "سلف الموظفين";
        await parentCard.SaveCardCommand.ExecuteAsync(null).ConfigureAwait(true);
        var created = accounting.Accounts.Single(a => a.Code == "1003");
        Assert.Equal((parent, AccountType.Assets, AccountCurrencyMode.BaseCurrencyOnly), (created.ParentId, created.AccountType, created.CurrencyMode));
        Assert.True(parentCard.IsViewMode); Assert.Equal(created.Id, parentCard.AccountId); Assert.Same(parentDocument, shell.Documents.Active);
        Assert.Equal(WorkspaceDocument.AccountKey(fixture.Company.Id, fixture.Branch.Id, created.Id), parentDocument.Key);
    }

    [Fact]
    public async Task ReopeningAnEditingCardPreservesItsOriginalVersionAndRejectsAConcurrentUpdate()
    {
        var fixture = new ShellFixture(); fixture.User.Permissions = All;
        var (accounting, _, cash) = Chart();
        using var shell = await LoginAsync(fixture, accounting);
        await shell.NavigateToAsync("accounts", new OpenAccountCardRequest(cash)).ConfigureAwait(true);
        var document = shell.Documents.Active; var card = Assert.IsType<AccountCardViewModel>(document.Content);
        var originalVersion = card.Account!.Version;
        card.BeginEditCommand.Execute(null); card.DraftNameArabic = "تعديل المحاسب الأول";

        // Another user saves while this card is open. Reopening must not adopt that user's version for our old draft.
        var stored = accounting.Accounts.Single(account => account.Id == cash);
        var changed = System.Text.Json.JsonSerializer.Deserialize<AccountResponse>(System.Text.Json.JsonSerializer.Serialize(stored))!;
        changed.NameArabic = "تعديل المحاسب الثاني"; changed.Version = "changed-elsewhere";
        accounting.Accounts[accounting.Accounts.IndexOf(stored)] = changed;
        await shell.NavigateToAsync("accounts", null).ConfigureAwait(true);
        await shell.NavigateToAsync("accounts", new OpenAccountCardRequest(cash)).ConfigureAwait(true);
        Assert.Same(document, shell.Documents.Active); Assert.True(document.IsDirty);
        Assert.Equal("تعديل المحاسب الأول", card.DraftNameArabic);
        Assert.Equal(originalVersion, card.Account.Version);
        Assert.False(card.RefreshCommand.CanExecute(null));
        await card.SaveCardCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Equal("Conflict", shell.Operation.ErrorKey); Assert.Empty(accounting.Updates);
        Assert.True(card.IsEditMode); Assert.True(document.IsDirty);

        fixture.Confirm = true; await card.CancelCardCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.True(card.RefreshCommand.CanExecute(null));
        await card.RefreshCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Equal("changed-elsewhere", card.Account.Version);
        Assert.Equal("تعديل المحاسب الثاني", card.Account.NameArabic);
        card.BeginEditCommand.Execute(null); card.DraftNameArabic = "تعديل بعد إعادة التحميل";
        await card.SaveCardCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.Empty(shell.Operation.Error); Assert.Single(accounting.Updates); Assert.False(document.IsDirty);
    }

    [Fact]
    public async Task CardModeChangesNotifyNavigationAndRefreshAndReopeningPreservesANewChildDraft()
    {
        var fixture = new ShellFixture(); fixture.User.Permissions = All;
        var (accounting, parent, _) = Chart();
        using var shell = await LoginAsync(fixture, accounting);
        await shell.NavigateToAsync("accounts", new OpenAccountCardRequest(parent)).ConfigureAwait(true);
        var document = shell.Documents.Active; var card = Assert.IsType<AccountCardViewModel>(document.Content);
        var refreshChanges = 0; var navigationChanges = 0;
        card.RefreshCommand.CanExecuteChanged += (_, _) => refreshChanges++;
        card.NextCommand.CanExecuteChanged += (_, _) => navigationChanges++;
        card.BeginNewCommand.Execute(null);
        Assert.True(refreshChanges > 0); Assert.True(navigationChanges > 0);
        Assert.False(card.RefreshCommand.CanExecute(null)); Assert.False(card.NextCommand.CanExecute(null));
        card.DraftCode = "1003"; card.DraftNameArabic = "عهدة الموظفين";
        await shell.NavigateToAsync("accounts", new OpenAccountCardRequest(parent)).ConfigureAwait(true);
        Assert.True(card.IsNewMode); Assert.Equal("1003", card.DraftCode); Assert.Equal("عهدة الموظفين", card.DraftNameArabic);
        fixture.Choice = DialogChoice.Alternate;
        await shell.CloseDocumentCommand.ExecuteAsync(document).ConfigureAwait(true);
        Assert.Contains(accounting.Accounts, account => account.Code == "1003" && account.ParentId == parent);
        Assert.DoesNotContain(document, shell.Documents.Documents);
    }

    [Fact]
    public async Task JournalLineOpensItsScopedLedgerWithoutSavingOrClosingTheDraftAndRequiresReportPermission()
    {
        var fixture = new ShellFixture(); fixture.User.Permissions = All;
        var (accounting, _, cash) = Chart();
        using var shell = await LoginAsync(fixture, accounting);
        await shell.NavigateToAsync("journals", NewJournalRequest.Instance).ConfigureAwait(true);
        var document = shell.Documents.Active; var journal = shell.ActiveJournal!;
        journal.JournalDescription = "قيد غير محفوظ";
        var line = journal.Lines[0]; line.AccountId = cash;
        Assert.True(journal.OpenLineLedgerCommand.CanExecute(line));
        Assert.False(journal.OpenLineLedgerCommand.CanExecute(new JournalLineRow { AccountId = cash }));
        Assert.False(journal.OpenLineLedgerCommand.CanExecute(journal.Lines[1]));
        await journal.OpenLineLedgerCommand.ExecuteAsync(line).ConfigureAwait(true);
        // The ledger opens as its own window; the unsaved draft behind it stays open and untouched.
        var ledgerDocument = shell.Documents.Documents.Single(item => item.Route == "account-ledger");
        Assert.True(ledgerDocument.IsDetached);
        Assert.Same(shell.Pages.AccountLedger, ledgerDocument.Content);
        Assert.Equal(cash, shell.Pages.AccountLedger.AccountId);
        Assert.Equal((fixture.Company.Id, fixture.Branch.Id), (ledgerDocument.CompanyId, ledgerDocument.BranchId));
        Assert.Contains(document, shell.Documents.Documents); Assert.True(document.IsDirty);
        Assert.Null(journal.CurrentJournalId); Assert.Equal("قيد غير محفوظ", journal.JournalDescription);

        WorkspaceDocument? reactivated = null;
        shell.Documents.DetachedActivationRequested += (_, item) => reactivated = item;
        shell.Documents.Activate(document);
        await journal.OpenLineLedgerCommand.ExecuteAsync(line).ConfigureAwait(true);
        Assert.Same(ledgerDocument, reactivated);
        Assert.Same(ledgerDocument, shell.Documents.Documents.Single(item => item.Route == "account-ledger"));

        journal.Configure(["Ledger.Journals.View", "Ledger.Journals.Manage"]);
        Assert.False(journal.CanViewAccountLedgers); Assert.False(journal.OpenLineLedgerCommand.CanExecute(line));
        shell.Documents.Activate(document);
        await journal.OpenLineLedgerCommand.ExecuteAsync(line).ConfigureAwait(true);
        Assert.Same(document, shell.Documents.Active);
    }

    [Fact]
    public async Task OpenDocumentsAreRestoredAfterSignInByKeyOnlyAndNewDraftsAreNeverStored()
    {
        var fixture = new ShellFixture(); fixture.User.Permissions = All; var client = new FakeLedgerClient();
        var (accounting, _, cash) = Chart();
        var journal = client.Add(fixture.Company.Id, fixture.Branch.Id, JournalState.Posted);
        using var shell = fixture.Create(new AdministrationFixture(), client, accounting: accounting); shell.UserName = "admin"; shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);
        await shell.NavigateToAsync("accounts", null).ConfigureAwait(true);
        await shell.NavigateToAsync("accounts", new OpenAccountCardRequest(cash)).ConfigureAwait(true);
        var card = shell.Documents.Active;
        await shell.NavigateToAsync("journals", new OpenJournalRequest(journal.Id)).ConfigureAwait(true);
        await shell.NavigateToAsync("journals", NewJournalRequest.Instance).ConfigureAwait(true);
        shell.ActiveJournal!.JournalDescription = "مسودة لا تُحفظ على القرص"; var draft = shell.Documents.Active;
        shell.DetachDocumentCommand.Execute(card);
        shell.Documents.Activate(shell.Documents.Find(WorkspaceDocument.JournalKey(fixture.Company.Id, fixture.Branch.Id, journal.Id)));

        var saved = fixture.Saved!;
        Assert.Equal((fixture.Company.Id, fixture.Branch.Id), (saved.CompanyId, saved.BranchId));
        Assert.Equal(["route:accounts", "account:" + cash, "journal:" + journal.Id], saved.Documents!.Select(item => item.Kind + ":" + (item.Route ?? item.Id.ToString())));
        Assert.True(saved.Documents![1].Detached); Assert.True(saved.Documents[2].Active);
        Assert.DoesNotContain(saved.Documents, item => item.Kind == "journal" && item.Id is null);

        // The draft is discarded (never stored); logout keeps the remembered list; signing in reopens it through the authorized path.
        fixture.Choice = DialogChoice.Confirm; await shell.CloseDocumentCommand.ExecuteAsync(draft).ConfigureAwait(true);
        await shell.LogoutCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.False(shell.IsAuthenticated); Assert.Equal(3, fixture.Saved!.Documents!.Count);
        shell.UserName = "admin"; shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true);
        Assert.NotNull(shell.Documents.Find(WorkspaceDocument.RouteKey("accounts", fixture.Company.Id, fixture.Branch.Id)));
        var restoredCard = shell.Documents.Find(WorkspaceDocument.AccountKey(fixture.Company.Id, fixture.Branch.Id, cash))!;
        Assert.True(restoredCard.IsDetached); Assert.Equal("1001 — الصندوق", restoredCard.Title);
        var restoredJournal = shell.Documents.Find(WorkspaceDocument.JournalKey(fixture.Company.Id, fixture.Branch.Id, journal.Id))!;
        Assert.Same(restoredJournal, shell.Documents.Active); Assert.Equal(JournalState.Posted, shell.ActiveJournal!.CurrentState);
        Assert.DoesNotContain(shell.Documents.Documents, document => document.Key.StartsWith("JournalDraft/", StringComparison.Ordinal));

        // A document that is no longer permitted is not reopened.
        await shell.LogoutCommand.ExecuteAsync(null).ConfigureAwait(true);
        fixture.User.Permissions = ["Ledger.Accounts.View"]; shell.Password = "test";
        await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true); Assert.True(shell.IsAuthenticated);
        Assert.Null(shell.Documents.Find(WorkspaceDocument.JournalKey(fixture.Company.Id, fixture.Branch.Id, journal.Id)));
        Assert.NotNull(shell.Documents.Find(WorkspaceDocument.AccountKey(fixture.Company.Id, fixture.Branch.Id, cash)));
    }
}
