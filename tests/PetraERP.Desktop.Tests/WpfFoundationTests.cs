using System.IO;
using Microsoft.Extensions.DependencyInjection;
using System.Xml.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PetraERP.ApiClient;
using PetraERP.Desktop.Controls;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Productivity;
using PetraERP.Desktop.Theming;

namespace PetraERP.Desktop.Tests;

// The process-wide WPF Application lives on this class's STA thread; WPF tests that create windows on another thread share the collection.
[Collection(WpfApplicationGroup.Name)]
public sealed partial class WpfFoundationTests
{
    [Fact]
    public void ArabicAndEnglishResourcesHaveMatchingUniqueKeys()
    {
        var root = Directory.GetCurrentDirectory();
        while (!File.Exists(Path.Combine(root, "PetraERP.slnx"))) root = Directory.GetParent(root)!.FullName;
        var english = XDocument.Load(Path.Combine(root, "src/PetraERP.Desktop/Localization/Strings.resx")).Root!.Elements("data").Select(element => element.Attribute("name")!.Value).ToArray();
        var arabic = XDocument.Load(Path.Combine(root, "src/PetraERP.Desktop/Localization/Strings.ar.resx")).Root!.Elements("data").Select(element => element.Attribute("name")!.Value).ToArray();
        Assert.Equal(english.Length, english.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(arabic.Length, arabic.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(english.Order(StringComparer.Ordinal), arabic.Order(StringComparer.Ordinal));
    }
    [Fact]
    public async Task RealWpfResourcesRenderBothDirectionsAndThemesAndPreserveGridLayouts()
    {
        // This run needs the one Application the process is allowed to own, so it executes on the shared WPF host
        // thread (WpfApplicationHost) instead of spawning its own; a dispatcher synchronization context is
        // deliberately not installed there, because the bodies below block on GetAwaiter().GetResult().
        var cancellationToken = TestContext.Current.CancellationToken;
        await Task.Run(() => WpfApplicationHost.Run(VerifyOnSta), cancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(600), cancellationToken).ConfigureAwait(true);
    }

    private static void VerifyOnSta()
    {
        var application = WpfApplicationHost.Application;
        // Another class may have left theme or skin overrides on the shared Application: start from a clean resource state.
        WpfApplicationHost.ResetResources();
        try { VerifyApplication(application); }
        finally
        {
            // The shared Application outlives this run and must not be shut down (WpfApplicationHost), so the only
            // safe cleanup is dropping the theme resources this run put on it - the next test asserts on that state.
            WpfApplicationHost.ResetResources();
        }
    }

    private static void VerifyApplication(System.Windows.Application application)
    {
        WpfApplicationHost.MergeThemes("Themes/Light.xaml", "Themes/Dense.xaml", "Themes/Components.xaml");
        application.Resources["StandardLabel"] = new StandardLabelConverter();
        using var themes = new WpfThemeService(application);
        using var listener = new BindingTrace();
        System.Diagnostics.PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
        System.Diagnostics.PresentationTraceSources.DataBindingSource.Switch.Level = System.Diagnostics.SourceLevels.Warning;
        using (var services = PetraERP.Desktop.Hosting.DesktopComposition.Build(application, new(new Uri("https://localhost/"))))
        {
            var window = services.GetRequiredService<PetraERP.Desktop.DesktopWindow>(); Assert.NotNull(window.Model.Pages.TrialBalance); window.Close();
        }
        VerifyThemes(application, themes);
        VerifyEntryExperience(themes);
        VerifyInputCompletion(themes);
        VerifyAccountAuditCard(themes);
        VerifyCostCenterHistory(themes);
        VerifySalesDocumentLinks(themes);
        VerifyRelatedDocuments(themes);
        VerifyGridLayout();
        VerifySystemShell(themes);
        VerifyPhysicalViewport();
        VerifyDetachedDocumentWindows();
        VerifyClassicMdi();
        VerifyPrintPreviewWindow();
        VerifyPrintTemplateDesigner();
        VerifyReportPopupWindows();
        VerifyAccountTreePointerAndKeys();
        VerifyLedgerScreens(themes);
        VerifyLookupBinding();
        VerifyJournalReadOnlyBindings();
        VerifyAllViewsCanInstantiate();
        VerifyWorkspaceWizard(themes);
        System.Diagnostics.PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);
        File.WriteAllText(Path.Combine(FindRoot(), ".artifacts/viewport-acceptance/bindings.log"), listener.Messages.ToString());
        Assert.True(listener.Messages.Length == 0, listener.Messages.ToString());
    }
    private static void VerifyThemes(System.Windows.Application application, WpfThemeService themes)
    {
        themes.Apply(ThemePreference.Light); var light = ((SolidColorBrush)application.Resources["Surface"]).Color;
        themes.Apply(ThemePreference.Dark); var dark = ((SolidColorBrush)application.Resources["Surface"]).Color; Assert.NotEqual(light, dark);
        themes.Apply(ThemePreference.System); Assert.Equal(ThemePreference.System, themes.Preference);
        Assert.Equal(themes.EffectiveTheme == AppTheme.Light ? light : dark, ((SolidColorBrush)application.Resources["Surface"]).Color);
    }
    private static void VerifyGridLayout()
    {
        var grid = new DataGrid { Style = (Style)System.Windows.Application.Current.Resources[typeof(DataGrid)] }; grid.Columns.Add(new DataGridTextColumn { SortMemberPath = "Code" }); grid.Columns.Add(new DataGridTextColumn { SortMemberPath = "Name" });
        GridLayouts.Apply(grid, new GridPreferenceRequest { Key = "ledger.accounts", Columns = [new() { Key = "Code", Width = 173, Order = 1, Visible = false }, new() { Key = "Name", Width = 241, Order = 0, Visible = true }] });
        grid.Measure(new Size(600, 400)); grid.Arrange(new Rect(0, 0, 600, 400)); grid.UpdateLayout();
        Assert.Equal(28, grid.RowHeight); Assert.Equal(173, grid.Columns[0].Width.Value);
        var saved = GridLayouts.Capture(grid, "ledger.accounts"); Assert.False(saved.Columns[0].Visible); Assert.Equal(1, saved.Columns[0].Order);
    }
    private static void VerifySystemShell(WpfThemeService themes)
    {
        var fixture = new ShellFixture();
        using var shell = fixture.Create(new AdministrationFixture());
        foreach (var preference in new[] { ThemePreference.Light, ThemePreference.Dark })
        foreach (var language in new[] { "ar", "en" })
        {
            themes.Apply(preference); shell.Localization.SetLanguage(language);
            var window = new PetraERP.Desktop.DesktopWindow(shell);
            window.ShowActivated = false; window.Left = -10000; window.Show();
            window.Measure(new Size(1100, 760)); window.Arrange(new Rect(0, 0, 1100, 760)); window.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.ContextIdle);
            var body = Assert.IsAssignableFrom<FrameworkElement>(window.Content);
            var password = Descendants<PasswordBox>(body).First();
            shell.UserName = "admin"; password.Password = "transient";
            Assert.Equal("transient", shell.Password); Assert.True(shell.LoginCommand.CanExecute(null));
            RenderSystem(body, "login", language, preference);
            shell.LoginCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            Assert.Empty(password.Password); Assert.True(shell.IsAuthenticated);
            Assert.Null(shell.Dialogs!.Current); Assert.DoesNotContain(Descendants<Button>(window), button => button.Name == "DialogCancel" && button.IsVisible);
            shell.Localization.SetLanguage(language);
            shell.NavigateCommand.ExecuteAsync("users").GetAwaiter().GetResult();
            body.UpdateLayout(); Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.ContextIdle);
            var usersGrid = Assert.Single(Descendants<DataGrid>(body), grid => grid.Name == "UsersGrid");
            Assert.True(usersGrid.EnableRowVirtualization); Assert.False(usersGrid.CanUserSortColumns);
            shell.Administration.SelectedUser = shell.Administration.Users[0]; shell.Administration.OpenUserCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            body.UpdateLayout(); Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.ContextIdle);
            Assert.All(Descendants<TextBlock>(body), text => Assert.DoesNotContain("DependencyProperty.UnsetValue", text.Text));
            Assert.Equal(language == "ar" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight, window.FlowDirection);
            RenderSystem(body, "users", language, preference);
            VerifyResponsiveViewports(window, "users", language, preference);
            Assert.Same(shell.Company, Assert.Single(Descendants<ComboBox>(body), picker => ReferenceEquals(picker.ItemsSource, shell.Companies)).SelectedItem);
            shell.NavigateCommand.ExecuteAsync("roles").GetAwaiter().GetResult();
            body.UpdateLayout(); Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.ContextIdle);
            shell.Administration.SelectedRole = shell.Administration.Roles[0]; shell.Administration.OpenRoleCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            RenderSystem(body, "roles", language, preference);
            VerifyResponsiveViewports(window, "roles", language, preference);
            shell.NavigateCommand.ExecuteAsync("audit").GetAwaiter().GetResult();
            shell.Administration.AuditType = SecurityAuditEventType.UserCreated;
            Assert.Equal("2026-01-02 00:04:05Z", new UtcTextConverter().Convert(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(3)), typeof(string), null!, System.Globalization.CultureInfo.InvariantCulture));
            RenderSystem(body, "audit", language, preference);
            VerifyResponsiveViewports(window, "audit", language, preference);
            shell.LogoutCommand.ExecuteAsync(null).GetAwaiter().GetResult(); window.Close();
        }
    }
    /// <summary>Real WPF bindings: IsReadOnly receives a Boolean (not Visibility) and follows the typed journal state.</summary>
    private static void VerifyJournalReadOnlyBindings()
    {
        var shell = new ShellFixture();
        using var desktop = shell.Create(new AdministrationFixture());
        var client = new FakeLedgerClient();
        var localization = new LocalizationService("ar");
        using var ledger = new PetraERP.Desktop.GeneralLedger.GeneralLedgerViewModel(client, shell, new PetraERP.Desktop.Shell.DesktopOperation(localization), localization);
        ledger.Configure(["Ledger.Journals.View", "Ledger.Journals.Manage", "Ledger.Journals.Approve", "Ledger.Journals.Post", "Ledger.Journals.Reverse"]);
        var company = Guid.NewGuid(); var branch = Guid.NewGuid();
        var draft = client.Add(company, branch, JournalState.Draft);
        var approved = client.Add(company, branch, JournalState.Approved);
        var posted = client.Add(company, branch, JournalState.Posted);
        foreach (var journal in new[] { draft, approved, posted })
            journal.Lines = [new JournalLineResponse { Id = Guid.NewGuid(), AccountId = Guid.NewGuid(), AccountCode = "1001", AccountNameArabic = "الصندوق",
                TransactionCurrencyCode = "JOD", TransactionDebit = "100", TransactionCredit = "0", RateValue = "1", Description = "سطر للانتقال إلى الأستاذ" }];
        ledger.LoadPageAsync(company, branch, "JOD", CancellationToken.None).GetAwaiter().GetResult();
        var view = new PetraERP.Desktop.GeneralLedger.JournalsView { DataContext = ledger };
        var window = new Window { DataContext = desktop, Content = view, ShowActivated = false, Left = -10000 };
        // IsVisible must be asserted in a shown WPF tree (a merely measured, hidden Window always reports false).
        window.Show();
        foreach (var (journal, readOnly) in new[] { (draft, false), (approved, true), (posted, true) })
        {
            ledger.SelectedJournal = ledger.Journals.Single(item => item.Id == journal.Id);
            ledger.OpenJournalCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            window.Measure(new Size(1200, 760)); window.Arrange(new Rect(0, 0, 1200, 760)); window.UpdateLayout();
            view.Measure(new Size(1180, 720)); view.Arrange(new Rect(0, 0, 1180, 720)); view.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.ContextIdle);
            var editor = Descendants<TextBox>(view).Where(box => BindingOperations.GetBinding(box, TextBox.IsReadOnlyProperty) is not null).ToArray();
            Assert.True(editor.Length == 2, $"{journal.State}: {editor.Length} bound editor text boxes; open={ledger.IsEditorOpen}; all={Descendants<TextBox>(view).Count()}");
            Assert.All(editor, box => Assert.Equal(readOnly, box.IsReadOnly));
            var lines = Assert.Single(Descendants<DataGrid>(view), grid => BindingOperations.GetBinding(grid, DataGrid.IsReadOnlyProperty) is not null);
            Assert.Equal(readOnly, lines.IsReadOnly);
            Assert.Null(BindingOperations.GetBindingExpression(lines, DataGrid.IsReadOnlyProperty)!.ValidationError);
            // The account link works in read-only posted/approved rows too, without making their fields editable.
            ledger.Navigator = new PetraERP.Desktop.Shell.ShellNavigator();
            ledger.Configure(["Ledger.Journals.View", "Ledger.Journals.Manage", "Ledger.Journals.Approve", "Ledger.Journals.Post", "Ledger.Journals.Reverse", "Ledger.Reports.View"]);
            window.UpdateLayout(); view.UpdateLayout(); Pump();
            var link = Assert.Single(Descendants<Button>(lines), button => ReferenceEquals(button.Command, ledger.OpenLineLedgerCommand));
            Assert.Same(ledger.Lines[0], link.CommandParameter);
            Assert.True(link.IsEnabled, "The scoped account link must remain enabled in read-only journal rows.");
            Assert.True(link.IsVisible, "Reports.View must expose the account link in the shown journal view.");
            ledger.Configure(["Ledger.Journals.View", "Ledger.Journals.Manage", "Ledger.Journals.Approve", "Ledger.Journals.Post", "Ledger.Journals.Reverse"]);
            window.UpdateLayout(); view.UpdateLayout(); Pump();
            Assert.False(link.IsVisible); Assert.False(link.Command.CanExecute(link.CommandParameter));
            ledger.Navigator = null;
        }

        window.Close();
    }

    private static void VerifyLookupBinding()
    {
        var row = new PetraERP.Desktop.GeneralLedger.JournalLineRow();
        var item = new LookupItem(Guid.NewGuid(), "1001", "الصندوق", "Cash");
        var picker = new LookupPicker { DataContext = row, ItemsSource = new[] { item } };
        BindingOperations.SetBinding(picker, LookupPicker.SelectedIdProperty, new Binding(nameof(row.CostCenterId)) { Mode = BindingMode.TwoWay });
        picker.Search("cash"); Assert.True(picker.CommitHighlighted()); Assert.Equal(item.Id, row.CostCenterId);
        Assert.NotNull(BindingOperations.GetBinding(picker, LookupPicker.SelectedIdProperty));
        picker.IsReadOnly = true; Assert.False(picker.CommitHighlighted());
    }
    private static readonly string[] CaptureRoutes = ["home", "accounts", "cost-centers", "periods", "journals", "journal", "general-journal", "account-ledger", "trial-balance"];
    private static readonly string[] CaptureRoutesWithDocuments = [.. CaptureRoutes, "sales-invoices", "treasury-vouchers", "fx-revaluation"];

    private static void VerifyLedgerScreens(WpfThemeService themes)
    {
        var fixture = new ShellFixture(); fixture.User.Permissions = PetraERP.Application.Security.PermissionCatalog.All.Select(p => p.Code).ToArray();
        var ledger = new FakeLedgerClient(); var saved = ledger.Add(fixture.Company.Id, fixture.Branch.Id, JournalState.Posted);
        var accounting = new AccountingFixture(); var cash = Guid.NewGuid(); var capital = Guid.NewGuid();
        accounting.Accounts.AddRange([new() { Id = cash, Code = "1001", NameArabic = "الصندوق", NameEnglish = "Cash", AccountType = AccountType.Assets, IsActive = true, PostingAllowed = true, HasJournalLines = true },
            new() { Id = capital, Code = "3001", NameArabic = "رأس المال", NameEnglish = "Capital", AccountType = AccountType.Equity, IsActive = true, PostingAllowed = true }]);
        accounting.CostCenters.Add(new() { Id = Guid.NewGuid(), Code = "HQ", NameArabic = "الإدارة", NameEnglish = "Administration", IsActive = true });
        accounting.Years.Add(new() { Id = Guid.NewGuid(), Code = "FY2026", RangeStart = new(2026, 1, 1), RangeEnd = new(2026, 12, 31), Periods = [new() { Id = Guid.NewGuid(), Code = "P10", RangeStart = new(2026, 10, 1), RangeEnd = new(2026, 10, 31), State = AccountingPeriodState.Open }] });
        accounting.GeneralJournal = new() { Page = 1, PageSize = 100, TotalCount = 2, BaseCurrencyCode = "JOD", TotalBaseDebit = "125.4321", TotalBaseCredit = "125.4321", Lines = [new() { JournalId = saved.Id, AccountingDate = saved.AccountingDate, PostingSequence = 1, State = JournalState.Posted, AccountCode = "1001", AccountNameArabic = "الصندوق", LineDescription = "تمويل نقدي", BaseDebit = "125.4321", BaseCredit = "0" }] };
        accounting.Ledger = new() { Account = accounting.Accounts[0], BaseCurrencyCode = "JOD", OpeningBalance = "0", PeriodDebit = "125.4321", PeriodCredit = "0", ClosingBalance = "125.4321", Page = 1, PageSize = 100, TotalCount = 1, Lines = [new() { JournalId = saved.Id, AccountingDate = saved.AccountingDate, PostingSequence = 1, LineDescription = "تمويل نقدي", BaseDebit = "125.4321", BaseCredit = "0", RunningBalance = "125.4321" }] };
        accounting.Balance = new() { BaseCurrencyCode = "JOD", To = new(2026, 12, 31), TotalOpeningDebit = "0", TotalOpeningCredit = "0", TotalPeriodDebit = "125.4321", TotalPeriodCredit = "125.4321", TotalEndingDebit = "125.4321", TotalEndingCredit = "125.4321", IsBalanced = true, Rows = [new() { AccountId = cash, Code = "1001", NameArabic = "الصندوق", NameEnglish = "Cash", OpeningDebit = "0", OpeningCredit = "0", PeriodDebit = "125.4321", PeriodCredit = "0", EndingDebit = "125.4321", EndingCredit = "0" }, new() { AccountId = capital, Code = "3001", NameArabic = "رأس المال", NameEnglish = "Capital", OpeningDebit = "0", OpeningCredit = "0", PeriodDebit = "0", PeriodCredit = "125.4321", EndingDebit = "0", EndingCredit = "125.4321" }] };
        accounting.Invoices.Add(new SalesInvoiceSummaryDto { Id = Guid.NewGuid(), InvoiceNumber = "INV-0001", CustomerName = "عميل تجريبي", WarehouseCode = "WH", InvoiceDateUtc = new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.Zero),
            PaymentMethod = PaymentMethod.Cash, Status = SalesInvoiceStatus.Posted, GrandTotal = "125.4321", Version = "v1", CurrencyCode = "USD" });
        var fx = new FxRevaluationFixture();
        using var shell = fixture.Create(new AdministrationFixture(), ledger, accounting: accounting, fxRevaluation: fx);
        foreach (var theme in new[] { ThemePreference.Light, ThemePreference.Dark })
        foreach (var language in new[] { "ar", "en" })
        {
            var window = new PetraERP.Desktop.DesktopWindow(shell) { Left = -10000, ShowActivated = false };
            window.Show(); shell.UserName = "admin"; shell.Password = "test"; RunPumped(shell.LoginCommand.ExecuteAsync(null), "login");
            shell.Localization.SetLanguage(language); themes.Apply(theme);
            Assert.Single(System.Windows.Application.Current.Resources.MergedDictionaries, d => d.Source?.OriginalString.Contains("Themes/Light.xaml", StringComparison.Ordinal) == true || d.Source?.OriginalString.Contains("Themes/Dark.xaml", StringComparison.Ordinal) == true);
            // The document and currency screens are captured once (Arabic, light) to keep the shared STA host fast.
            var routes = theme == ThemePreference.Light && language == "ar" ? CaptureRoutesWithDocuments : CaptureRoutes;
            foreach (var route in routes)
            {
                // "journal" renders a new-draft journal document; every other entry is a route page.
                if (route == "journal") shell.NavigateToAsync("journals", PetraERP.Desktop.Shell.NewJournalRequest.Instance).GetAwaiter().GetResult();
                else
                {
                    // Pump the dispatcher while waiting: pages resume on the UI thread, so a blocking Wait() would deadlock them.
                    var navigation = shell.NavigateToAsync(route, null); var deadline = DateTime.UtcNow.AddSeconds(60);
                    while (!navigation.IsCompleted && DateTime.UtcNow < deadline) { Pump(); Thread.Sleep(5); }
                    Assert.True(navigation.IsCompleted, $"navigation to '{route}' did not complete"); navigation.GetAwaiter().GetResult();
                }
                Assert.Empty(shell.Operation.Error);
                if (route == "fx-revaluation")
                {
                    var page = shell.Pages.FxRevaluation!; var audit = page.AuditCommand.ExecuteAsync(null); var auditDeadline = DateTime.UtcNow.AddSeconds(60);
                    while (!audit.IsCompleted && DateTime.UtcNow < auditDeadline) { Pump(); Thread.Sleep(5); }
                    Assert.True(audit.IsCompleted, "the rate audit did not complete"); audit.GetAwaiter().GetResult();
                    Assert.Equal(2, page.AuditItems.Count); Assert.Single(fx.AuditRequests);
                    foreach (var expander in Descendants<Expander>(window)) expander.IsExpanded = true; window.UpdateLayout(); Pump(); foreach (var viewport in Descendants<PetraERP.Desktop.Controls.WorkspaceViewport>(window)) viewport.ScrollToBottom(); window.UpdateLayout(); Pump(); window.Width = 1366; window.Height = 768; ArrangeViewport(window); foreach (var expander2 in Descendants<Expander>(window)) expander2.IsExpanded = true; window.UpdateLayout(); Pump(); foreach (var viewport2 in Descendants<PetraERP.Desktop.Controls.WorkspaceViewport>(window)) viewport2.ScrollToBottom(); window.UpdateLayout(); Pump();
                    var auditBitmap = new RenderTargetBitmap(1366, 768, 96, 96, PixelFormats.Pbgra32); auditBitmap.Render(window);
                    using (var auditStream = File.Create(Path.Combine(FindRoot(), ".artifacts/viewport-acceptance/visual/fx-revaluation-audit-full.png"))) { var auditEncoder = new PngBitmapEncoder(); auditEncoder.Frames.Add(BitmapFrame.Create(auditBitmap)); auditEncoder.Save(auditStream); } 
                }
                if (route == "accounts") shell.Pages.Accounts.SelectedNode = shell.Pages.Accounts.Nodes[0];
                if (route == "cost-centers") shell.Pages.CostCenters.Selected = shell.Pages.CostCenters.Items[0];
                if (route == "journals") Assert.NotEmpty(shell.Ledger.Journals);
                if (route == "journal") { var draft = shell.ActiveJournal!; Assert.True(draft.IsNewDraft); draft.JournalDescription = "قيد اختبار واجهة / UI fixture";
                    draft.AccountItems.Add(new(cash, "1001", "الصندوق", "Cash")); draft.AccountItems.Add(new(capital, "3001", "رأس المال", "Capital"));
                    draft.Lines[0].AccountId = cash; draft.Lines[0].Debit = 125.4321m; draft.Lines[1].AccountId = capital; draft.Lines[1].Credit = 125.4321m;
                    window.UpdateLayout(); Pump(); Assert.Contains(Descendants<PetraERP.Desktop.GeneralLedger.JournalDocumentView>(window), view => ReferenceEquals(view.DataContext, draft));
                    Assert.DoesNotContain(Descendants<PetraERP.Desktop.GeneralLedger.JournalsView>(window), view => view.IsVisible); VerifyJournalNumericCells(window); }
                if (route == "account-ledger") shell.NavigateToAsync(route, new PetraERP.Desktop.Shell.OpenAccountLedgerRequest(cash, null, new(2026, 12, 31), false)).GetAwaiter().GetResult();
                if (route == "trial-balance") shell.Pages.TrialBalance.RunCommand.ExecuteAsync(null).GetAwaiter().GetResult();
                foreach (var (width, height) in new[] { (1366, 768), (1920, 1080) })
                {
                    window.Width = width; window.Height = height; window.UpdateLayout(); Pump();
                    Assert.Equal(language == "ar" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight, window.FlowDirection);
                    Assert.DoesNotContain(Descendants<TextBlock>(window), text => text.IsVisible && (text.Text.StartsWith("D_", StringComparison.Ordinal) || text.Text == "Select a date"));
                    foreach (var grid in Descendants<DataGrid>(window))
                    {
                        Assert.True(grid.EnableRowVirtualization); Assert.True(grid.EnableColumnVirtualization);
                        Assert.Equal(VirtualizationMode.Recycling, VirtualizingPanel.GetVirtualizationMode(grid));
                    }
                    foreach (var scale in new[] { 1d, 1.25d, 1.5d, 2d })
                    {
                        var body = (FrameworkElement)window.Content;
                        var bitmap = new RenderTargetBitmap((int)(width * scale), (int)(height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                        bitmap.Render(window);
                        if ((theme == ThemePreference.Light || language == "ar") && ((scale == 1 && width == 1366) || (scale == 1.5 && width == 1920)))
                        {
                            var directory = Path.Combine(FindRoot(), ".artifacts/viewport-acceptance/visual"); Directory.CreateDirectory(directory);
                            using var stream = File.Create(Path.Combine(directory, $"{route}-{language}-{theme}-{width}-{scale * 100:0}.png"));
                            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); encoder.Save(stream);
                        }
                    }
                }
                VerifyResponsiveViewports(window, route, language, theme);
            }
            RunPumped(shell.LogoutCommand.ExecuteAsync(null), "logout"); window.Close();
        }
    }
    private static void VerifyAllViewsCanInstantiate()
    {
        _ = new PetraERP.Desktop.Inventory.ItemCardView();
        _ = new PetraERP.Desktop.Inventory.ItemsView();
        _ = new PetraERP.Desktop.Shell.DirectoryView();
        _ = new PetraERP.Desktop.Patterns.PatternManagerView();
        _ = new PetraERP.Desktop.Shell.ItemLimitsView();
        _ = new PetraERP.Desktop.Shell.ItemPriceToolView();
        _ = new PetraERP.Desktop.Parties.PartiesView();
        _ = new PetraERP.Desktop.Parties.PartyCardView();
        _ = new PetraERP.Desktop.Patterns.DocumentPatternsView();
        _ = new PetraERP.Desktop.Patterns.PatternCardView();
        _ = new PetraERP.Desktop.Purchases.PurchasesView();
        _ = new PetraERP.Desktop.Treasury.VouchersView();
        _ = new PetraERP.Desktop.Sales.SalesInvoicesView();
        _ = new PetraERP.Desktop.Sales.SalesInvoiceCardView();
        _ = new PetraERP.Desktop.Sales.SalesDocumentLinksView();
    }
    private static void RunPumped(Task task, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (!task.IsCompleted && DateTime.UtcNow < deadline) { Pump(); Thread.Sleep(5); }
        Assert.True(task.IsCompleted, $"{what} did not complete"); task.GetAwaiter().GetResult();
    }
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(static () => { }, DispatcherPriority.ContextIdle);
    private static void RenderSystem(FrameworkElement body, string screen, string language, ThemePreference theme)
    {
        var window = Window.GetWindow(body)!; window.Width = 1366; window.Height = 768; window.UpdateLayout(); Pump();
        var bitmap = new RenderTargetBitmap(1366, 768, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
        var output = Path.Combine(FindRoot(), ".artifacts", "viewport-acceptance", "visual"); Directory.CreateDirectory(output);
        using var stream = File.Create(Path.Combine(output, $"{screen}-{language}-{theme}-1366-100.png"));
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); encoder.Save(stream);
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    private sealed class BindingTrace : System.Diagnostics.TraceListener
    {
        public System.Text.StringBuilder Messages { get; } = new();
        public override void Write(string? message) => Messages.Append(message);
        public override void WriteLine(string? message) => Messages.AppendLine(message);
    }
    private static string FindRoot()
    {
        var root = Directory.GetCurrentDirectory();
        while (!File.Exists(Path.Combine(root, "PetraERP.slnx"))) root = Directory.GetParent(root)!.FullName;
        return root;
    }
}
