using PetraERP.Api.Tests;
using PetraERP.ApiClient;
using PetraERP.Desktop.Administration;
using PetraERP.Desktop.Accounting;
using PetraERP.Desktop.Connection;
using PetraERP.Desktop.GeneralLedger;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Productivity;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Tests;

/// <summary>Acceptance through real Desktop commands, generated client, HTTP API and an isolated migrated SQL database.</summary>
public sealed class LedgerWorkstationSqlTests
{
    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task AccountantCanManageMasterDataPostDrillDownReverseAndAuditThroughTheSingleShell()
    {
        var host = await ApiSqlHost.CreateBootstrapWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            using var session = new DesktopSession(new Uri("https://localhost/"), host.Factory.Server.CreateHandler(), host.Factory.Server.CreateHandler());
            var fixture = new ShellFixture(); var loc = new LocalizationService("ar"); var notifications = new NotificationCenter(loc, TimeSpan.Zero);
            var operation = new DesktopOperation(loc, notifications); var navigator = new ShellNavigator(); var ledgerClient = new GeneralLedgerClient(session);
            using var admin = new AdministrationViewModel(new AdministrationClient(session), fixture, operation, loc);
            using var journalList = new GeneralLedgerViewModel(ledgerClient, fixture, operation, loc);
            var pages = TestPages.Create(admin, journalList, ledgerClient, fixture, operation, loc, navigator,
                new ChartOfAccountsClient(session), new FiscalCalendarClient(session), new LedgerReportsClient(session));
            using var workspace = new WorkspaceViewModel(new WorkspaceClient(session), loc, fixture);
            using var shell = new DesktopViewModel(new DesktopClient(session), fixture, fixture, loc, fixture, new(loc), operation, pages, workspace, navigator, notifications, TimeProvider.System);
            shell.UserName = ApiSqlHost.AdminUser; shell.Password = ApiSqlHost.AdminPassword;
            await shell.LoginCommand.ExecuteAsync(null).ConfigureAwait(true); Ok(); Assert.True(shell.ContextReady);
            await shell.NavigateToAsync("periods", null).ConfigureAwait(true); Ok();
            // Provisioning owns fiscal-year creation; the desktop must read its registered annual calendar.
            Assert.Equal((host.CompanyId, host.BranchId), (shell.Company!.Id, shell.Branch!.Id));
            var periods = pages.Periods;
            Assert.Equal(12, periods.Periods.Count);
            Assert.All(periods.Periods, p => Assert.Equal(AccountingPeriodState.Open, p.Period.State));
            Assert.NotNull(shell.CurrentPeriod); // Refreshed by the next shell navigation below.
            await shell.NavigateToAsync("accounts", null).ConfigureAwait(true); Ok();
            var accounts = pages.Accounts;
            accounts.NewCommand.Execute(null); accounts.Code = "1001"; accounts.NameArabic = "الصندوق"; accounts.NameEnglish = "Cash"; accounts.AccountType = AccountType.Assets;
            await accounts.SaveCommand.ExecuteAsync(null).ConfigureAwait(true); Ok(); var cash = accounts.SelectedNode!.Id;
            accounts.NewCommand.Execute(null); accounts.Code = "3001"; accounts.NameArabic = "رأس المال"; accounts.NameEnglish = "Capital"; accounts.AccountType = AccountType.Equity;
            await accounts.SaveCommand.ExecuteAsync(null).ConfigureAwait(true); Ok(); var capital = accounts.SelectedNode!.Id;
            accounts.NameEnglish = "Owner capital"; await accounts.SaveCommand.ExecuteAsync(null).ConfigureAwait(true); Ok();
            // Account-card history uses the actual generated-client audit endpoint, including card-local writes.
            await shell.NavigateToAsync("accounts", new OpenAccountCardRequest(capital)).ConfigureAwait(true); Ok();
            var capitalCard = Assert.IsType<AccountCardViewModel>(shell.CurrentView);
            Assert.Equal(2, capitalCard.AuditTotalCount);
            Assert.Equal(AccountAuditAction.Updated, capitalCard.SelectedAudit!.Action);
            Assert.Equal(("Capital", "Owner capital"), (capitalCard.SelectedAudit.Before!.NameEnglish, capitalCard.SelectedAudit.After!.NameEnglish));
            Assert.Equal("مدير النظام", capitalCard.AuditActor);
            capitalCard.BeginEditCommand.Execute(null); capitalCard.DraftNameEnglish = "Owner equity";
            await capitalCard.SaveCardCommand.ExecuteAsync(null).ConfigureAwait(true); Ok();
            Assert.Equal(3, capitalCard.AuditTotalCount); Assert.Equal("Owner equity", capitalCard.SelectedAudit!.After!.NameEnglish);
            await shell.NavigateToAsync("accounts", null).ConfigureAwait(true); Ok();
            accounts.NewCommand.Execute(null); accounts.Code = "9999"; accounts.NameArabic = "حساب مؤقت للتدقيق"; accounts.AccountType = AccountType.Assets;
            await accounts.SaveCommand.ExecuteAsync(null).ConfigureAwait(true); Ok(); var unused = accounts.SelectedNode!.Id;
            await accounts.ToggleActiveCommand.ExecuteAsync(null).ConfigureAwait(true); Ok();
            await accounts.ToggleActiveCommand.ExecuteAsync(null).ConfigureAwait(true); Ok();
            await shell.NavigateToAsync("accounts", new OpenAccountCardRequest(unused)).ConfigureAwait(true); Ok();
            var unusedDocument = shell.Documents.Active;
            Assert.Equal(3, ((AccountCardViewModel)unusedDocument.Content!).AuditTotalCount);
            await shell.NavigateToAsync("accounts", new OpenAccountRequest(unused)).ConfigureAwait(true); Ok();
            accounts.RequestDeleteCommand.Execute(null); accounts.DeleteReason = "إنشاء عرضي دون أي حركة";
            await accounts.DeleteCommand.ExecuteAsync(null).ConfigureAwait(true); Ok();
            Assert.DoesNotContain(unusedDocument, shell.Documents.Documents); Assert.DoesNotContain(accounts.Nodes, row => row.Id == unused);
            var deletedHistory = await session.Api.GetAccountHistoryAsync(shell.Company!.Id, shell.Branch!.Id, unused, 1, 25, TestContext.Current.CancellationToken).ConfigureAwait(true);
            Assert.Equal([AccountAuditAction.Deleted, AccountAuditAction.Activated, AccountAuditAction.Deactivated, AccountAuditAction.Created],
                deletedHistory.Items.Select(item => item.Action));
            Assert.All(deletedHistory.Items, item => Assert.Equal(shell.Branch.Id, item.WorkingBranchId));
            Assert.Equal("إنشاء عرضي دون أي حركة", deletedHistory.Items[0].Reason); Assert.Null(deletedHistory.Items[0].After);
            // Actual context endpoint and generated adapter: page immediate children without reloading a whole chart.
            await shell.NavigateToAsync("accounts", null).ConfigureAwait(true); Ok();
            accounts.NewCommand.Execute(null); accounts.Code = "2000"; accounts.NameArabic = "حساب تجميعي"; accounts.PostingAllowed = false;
            accounts.AccountType = AccountType.Assets;
            await accounts.SaveCommand.ExecuteAsync(null).ConfigureAwait(true); Ok(); var control = accounts.SelectedNode!.Id;
            for (var i = 0; i < 26; i++) await session.Api.CreateAccountAsync(shell.Company.Id, shell.Branch.Id, new()
            {
                Code = "2000-" + i.ToString("D3", System.Globalization.CultureInfo.InvariantCulture), NameArabic = "حساب فرعي",
                AccountType = AccountType.Assets, PostingAllowed = true, CostCenterPolicy = CostCenterPolicy.Optional,
                CurrencyMode = AccountCurrencyMode.BaseCurrencyOnly, ParentId = control,
            }, TestContext.Current.CancellationToken).ConfigureAwait(true);
            await shell.NavigateToAsync("accounts", new OpenAccountCardRequest(control)).ConfigureAwait(true); Ok();
            var controlCard = Assert.IsType<AccountCardViewModel>(shell.CurrentView);
            Assert.Equal(control, Assert.Single(controlCard.Path).Id); Assert.Equal(25, controlCard.Children.Count); Assert.Equal(26, controlCard.ChildrenTotalCount);
            await controlCard.NextChildrenCommand.ExecuteAsync(null).ConfigureAwait(true); Ok();
            var child = Assert.Single(controlCard.Children); Assert.Equal("2000-025", child.Code);
            await controlCard.OpenChildCommand.ExecuteAsync(child).ConfigureAwait(true); Ok();
            var childCard = Assert.IsType<AccountCardViewModel>(shell.CurrentView);
            Assert.Equal([control, child.Id], childCard.Path.Select(item => item.Id)); Assert.Equal(control, childCard.Parent!.Id);
            Assert.Empty(childCard.Children);
            await shell.NavigateToAsync("cost-centers", null).ConfigureAwait(true); Ok(); var centers = pages.CostCenters;
            centers.NewCommand.Execute(null); centers.Code = "HQ"; centers.NameArabic = "الإدارة";
            await centers.SaveCommand.ExecuteAsync(null).ConfigureAwait(true); Ok(); var center = centers.Selected!.Id;
            await centers.RefreshHistoryCommand.ExecuteAsync(null).ConfigureAwait(true); Ok();
            Assert.Equal(CostCenterAuditAction.Created, Assert.Single(centers.History).Action); Assert.Equal("الإدارة", centers.SelectedHistory!.After.NameArabic);
            centers.NameArabic = "الإدارة الرئيسية"; await centers.SaveCommand.ExecuteAsync(null).ConfigureAwait(true); Ok();
            await centers.ToggleActiveCommand.ExecuteAsync(null).ConfigureAwait(true); Ok();
            await centers.ToggleActiveCommand.ExecuteAsync(null).ConfigureAwait(true); Ok();
            await centers.RefreshHistoryCommand.ExecuteAsync(null).ConfigureAwait(true); Ok();
            Assert.Equal([CostCenterAuditAction.Activated, CostCenterAuditAction.Deactivated, CostCenterAuditAction.Updated, CostCenterAuditAction.Created], centers.History.Select(item => item.Action));
            Assert.Equal(("الإدارة", "الإدارة الرئيسية"), (centers.History[2].Before!.NameArabic, centers.History[2].After.NameArabic));
            Assert.All(centers.History, item => Assert.Equal(shell.Branch.Id, item.WorkingBranchId));
            await shell.NavigateToAsync("journals", NewJournalRequest.Instance).ConfigureAwait(true); Ok();
            var journal = shell.ActiveJournal!; Assert.NotSame(journalList, journal); // Each journal is its own document.
            Assert.True(journal.IsNewDraft); Assert.Equal(shell.Company!.BaseCurrencyCode, journal.BaseCurrencyCode);
            Assert.Contains(journal.AccountItems, item => item.Id == cash); Assert.Contains(journal.CostCenterItems, item => item.Id == center);
            journal.AccountingDate = new(2026, 10, 17); journal.JournalDescription = "تمويل نقدي لاختبار مسار المحاسب";
            journal.Lines[0].AccountId = cash; journal.Lines[0].CostCenterId = center; journal.Lines[0].Debit = 125.4321m;
            journal.Lines[1].AccountId = capital; journal.Lines[1].Credit = 125.4321m;
            await journal.SaveDraftCommand.ExecuteAsync(null).ConfigureAwait(true); Ok(); Assert.Equal(JournalState.Draft, journal.CurrentState); Assert.False(journal.IsTotalsPreview);
            await journal.ValidateDraftCommand.ExecuteAsync(null).ConfigureAwait(true); Ok();
            await journal.ApproveDraftCommand.ExecuteAsync(null).ConfigureAwait(true); Ok(); Assert.True(journal.IsDraftReadOnly);
            journal.ActionReason = "تم التدقيق والاعتماد"; await journal.PostJournalCommand.ExecuteAsync(null).ConfigureAwait(true); Ok();
            var original = journal.LastPostingReceipt!.JournalId; Assert.Equal(JournalState.Posted, journal.CurrentState); Assert.Equal(125.4321m, journal.TotalDebit);
            Assert.Contains(journal.AuditTrail, audit => audit.ActorNameArabic == "مدير النظام");
            await journal.OpenLineLedgerCommand.ExecuteAsync(journal.Lines.Single(line => line.AccountId == cash)).ConfigureAwait(true); Ok();
            // The ledger is a report: it opens in its own window, so it is read from its document rather than the tab host.
            var ledgerWindow = shell.Documents.Documents.Single(document => document.Route == "account-ledger");
            Assert.True(ledgerWindow.IsDetached); Assert.Same(pages.AccountLedger, ledgerWindow.Content);
            Assert.Equal(cash, pages.AccountLedger.AccountId);
            Assert.Equal("125.4321", pages.AccountLedger.Closing);
            await pages.AccountLedger.OpenJournalCommand.ExecuteAsync(pages.AccountLedger.Lines[0]).ConfigureAwait(true); Ok();
            Assert.Same(journal, shell.CurrentView); Assert.Equal(JournalState.Posted, journal.CurrentState);
            await shell.NavigateToAsync("general-journal", null).ConfigureAwait(true); Ok();
            Assert.Equal(2, pages.GeneralJournal.Lines.Count); Assert.Equal("125.4321", pages.GeneralJournal.TotalDebit);
            await pages.GeneralJournal.OpenJournalCommand.ExecuteAsync(pages.GeneralJournal.Lines[0]).ConfigureAwait(true); Ok(); Assert.Same(journal, shell.CurrentView);
            await shell.NavigateToAsync("trial-balance", null).ConfigureAwait(true); Ok();
            pages.TrialBalance.ToDate = new(2026, 12, 31); await pages.TrialBalance.RunCommand.ExecuteAsync(null).ConfigureAwait(true); Ok(); Assert.True(pages.TrialBalance.IsBalanced);
            await pages.TrialBalance.OpenLedgerCommand.ExecuteAsync(pages.TrialBalance.Rows.Single(row => row.AccountId == cash)).ConfigureAwait(true); Ok();
            Assert.Same(pages.AccountLedger, shell.Documents.Documents.Single(document => document.Route == "account-ledger").Content);
            Assert.Equal("125.4321", pages.AccountLedger.Closing);
            await pages.AccountLedger.OpenJournalCommand.ExecuteAsync(pages.AccountLedger.Lines[0]).ConfigureAwait(true); Ok();
            journal.ActionReason = "تصحيح تمويل تجريبي"; await journal.CreateReversalCommand.ExecuteAsync(null).ConfigureAwait(true); Ok(); Assert.True(journal.IsReversal);
            await journal.ValidateDraftCommand.ExecuteAsync(null).ConfigureAwait(true); Ok(); await journal.ApproveDraftCommand.ExecuteAsync(null).ConfigureAwait(true); Ok();
            journal.ActionReason = "عكس معتمد"; await journal.PostReversalCommand.ExecuteAsync(null).ConfigureAwait(true); Ok();
            await journal.OpenOriginalCommand.ExecuteAsync(null).ConfigureAwait(true); Ok();
            var originalDocument = shell.ActiveJournal!; Assert.NotSame(journal, originalDocument); Assert.Equal(JournalState.Reversed, originalDocument.CurrentState);
            Assert.False(originalDocument.CreateReversalCommand.CanExecute(null)); Assert.True(originalDocument.OpenReversalCommand.CanExecute(null));
            await originalDocument.OpenReversalCommand.ExecuteAsync(null).ConfigureAwait(true); Ok(); Assert.Same(journal, shell.CurrentView); Assert.True(journal.IsReversal);
            await shell.NavigateToAsync("trial-balance", null).ConfigureAwait(true); Ok(); pages.TrialBalance.ToDate = new(2026, 12, 31);
            await pages.TrialBalance.RunCommand.ExecuteAsync(null).ConfigureAwait(true); Ok(); Assert.Equal("0.00", pages.TrialBalance.TotalEndingDebit); Assert.True(pages.TrialBalance.IsBalanced);
            await shell.NavigateToAsync("periods", null).ConfigureAwait(true); Ok(); periods.Reason = "إقفال بعد المطابقة";
            await periods.SoftCloseCommand.ExecuteAsync(null).ConfigureAwait(true); Ok(); Assert.Equal(AccountingPeriodState.SoftClosed, periods.SelectedPeriod!.Period.State);
            periods.Reason = "فتح للمراجعة"; await periods.ReopenCommand.ExecuteAsync(null).ConfigureAwait(true); Ok(); Assert.Equal(AccountingPeriodState.Open, periods.SelectedPeriod!.Period.State); Assert.Equal(2, periods.Audit.Count);
            await workspace.ToggleFavoriteAsync("trial-balance.report").ConfigureAwait(true); Assert.Empty(workspace.Error); Assert.Contains(workspace.Favorites, command => command.Id == "trial-balance.report");
            Assert.Contains(workspace.Recent, hit => hit.Id == original);
            shell.ToggleLanguageCommand.Execute(null); shell.ToggleThemeCommand.Execute(null); shell.Palette.Open(); Assert.True(shell.Palette.IsOpen);
            await shell.LogoutCommand.ExecuteAsync(null).ConfigureAwait(true); Ok(); Assert.False(shell.IsAuthenticated); Assert.Empty(journalList.Lines);
            Assert.DoesNotContain(shell.Documents.Documents, document => document.Kind == WorkspaceDocumentKind.Journal);
            void Ok() => Assert.True(operation.Error.Length == 0, operation.Error + " / " + operation.ErrorKey);
        }
    }
}
