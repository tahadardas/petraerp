using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PetraERP.Desktop.Controls;
using PetraERP.Desktop.Theming;

namespace PetraERP.Desktop.Tests;

public sealed partial class WpfFoundationTests
{
    private static void VerifyPhysicalViewport()
    {
        var fixture = new ShellFixture(); fixture.User.Permissions = PetraERP.Application.Security.PermissionCatalog.All.Select(permission => permission.Code).ToArray();
        var accounting = new AccountingFixture();
        using var shell = fixture.Create(new AdministrationFixture(), accounting: accounting);
        var window = new DesktopWindow(shell) { Width = 1366d / 2, Height = 768d / 2, Left = -10000, ShowActivated = false };
        window.Show(); window.UpdateLayout(); Pump();
        try
        {
            Assert.True(window.ActualWidth <= 684 && window.ActualHeight <= 385,
                $"1366×768 at 200% must fit 683×384 DIP (±1 rounding); actual {window.ActualWidth}×{window.ActualHeight}");
            Assert.False(window.HandleShortcut(Key.K, ModifierKeys.Control));
            shell.UserName = "admin"; shell.Password = "test"; shell.LoginCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            shell.NavigateToAsync("accounts", null).GetAwaiter().GetResult(); window.UpdateLayout(); Pump();
            var host = Assert.Single(Descendants<AdaptivePaneGrid>(window)); Assert.True(host.IsCompact);
            Assert.True(window.HandleShortcut(Key.Tab, ModifierKeys.Control)); Assert.True(shell.IsHome);
            Assert.True(window.HandleShortcut(Key.W, ModifierKeys.Control)); Assert.True(shell.IsHome); Assert.Equal(2, shell.Documents.Count);
            Assert.True(window.HandleShortcut(Key.Tab, ModifierKeys.Control | ModifierKeys.Shift)); Assert.Same(shell.Pages.Accounts, shell.CurrentView);
            window.UpdateLayout(); Pump(); host = Assert.Single(Descendants<AdaptivePaneGrid>(window));
            shell.Pages.Accounts.NewCommand.Execute(null); shell.Pages.Accounts.Code = "1101"; shell.Pages.Accounts.NameArabic = "حساب جديد";
            window.UpdateLayout(); Pump(); Assert.Equal(1, host.SelectedIndex);
            Assert.True(window.HandleShortcut(Key.D1, ModifierKeys.Alt)); Assert.Equal(0, host.SelectedIndex);
            Assert.True(window.HandleShortcut(Key.D2, ModifierKeys.Alt)); Assert.Equal(1, host.SelectedIndex);
            Assert.False(window.HandleShortcut(Key.D3, ModifierKeys.Alt));
            shell.Pages.Accounts.Filter = "1101";
            Assert.True(window.HandleShortcut(Key.F, ModifierKeys.Control)); Assert.Equal(0, host.SelectedIndex);
            var search = Assert.Single(Descendants<TextBox>(window), box => SearchTarget.GetIsTarget(box));
            Assert.True(search.IsVisible); Assert.Equal(search.Text.Length, search.SelectionLength);
            Assert.True(window.HandleShortcut(Key.K, ModifierKeys.Control)); Assert.True(shell.Palette.IsOpen);
            Assert.False(window.HandleShortcut(Key.D2, ModifierKeys.Alt)); Assert.Equal(0, host.SelectedIndex);
            Assert.True(window.HandleShortcut(Key.K, ModifierKeys.Control)); Assert.False(shell.Palette.IsOpen);
            Assert.False(window.HandleShortcut(Key.K, ModifierKeys.Control | ModifierKeys.Shift));
            var confirmation = shell.Dialogs!.ConfirmAsync("D_ConfirmPostJournal"); window.UpdateLayout(); Pump();
            Assert.False(window.HandleShortcut(Key.K, ModifierKeys.Control));
            Assert.False(window.HandleShortcut(Key.D2, ModifierKeys.Alt));
            var overlay = Assert.Single(Descendants<PetraERP.Desktop.Shell.ShellOverlays>(window));
            var cancel = Assert.Single(Descendants<Button>(overlay), button => button.Name == "DialogCancel");
            var escape = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            cancel.RaiseEvent(escape); Assert.True(escape.Handled);
            WaitForUiTask(confirmation); Assert.True(confirmation.IsCompletedSuccessfully); Assert.False(confirmation.Result);
            Assert.Null(shell.Dialogs.Current);
            foreach (var width in new[] { 1366d, 683d, 1920d, 683d })
            {
                window.Width = width; ArrangeViewport(window);
                Assert.Equal("1101", shell.Pages.Accounts.Code); Assert.Equal("حساب جديد", shell.Pages.Accounts.NameArabic);
                Assert.True(shell.Pages.Accounts.HasUnsavedChanges);
            }
            var page = Assert.Single(Descendants<PetraERP.Desktop.Accounting.ChartOfAccountsView>(window));
            Assert.Same(shell.Pages.Accounts.NewCommand, Assert.Single(page.InputBindings.OfType<KeyBinding>(), binding => binding.Key == Key.N).Command);
            Assert.Same(shell.Pages.Accounts.SaveCommand, Assert.Single(page.InputBindings.OfType<KeyBinding>(), binding => binding.Key == Key.S).Command);
            // A physical 48px taskbar at 200% leaves 360 DIP; still a bounded scrolling page.
            window.Height = 360; ArrangeViewport(window);
            var viewport = Assert.Single(Descendants<WorkspaceViewport>(window));
            Assert.True(viewport.ScrollableHeight > 0); Assert.True(viewport.ViewportHeight > 80);
            var font = page.FontSize;
            Assert.Equal(13, font); Assert.False(page.LayoutTransform is ScaleTransform);
            window.Width = 1366; ArrangeViewport(window);
            host.ColumnDefinitions[2].Width = new(500); window.UpdateLayout();
            window.Width = 683; ArrangeViewport(window); Assert.True(host.IsCompact);
            window.Width = 1366; ArrangeViewport(window); Assert.False(host.IsCompact);
            Assert.Equal(500, host.ColumnDefinitions[2].Width.Value);
        }
        finally { window.Close(); }
        VerifyBoundedGridVirtualization();
    }

    private static void VerifyBoundedGridVirtualization()
    {
        var grid = new DataGrid { ItemsSource = Enumerable.Range(1, 5000).ToArray() };
        grid.Columns.Add(new DataGridTextColumn { Binding = new System.Windows.Data.Binding(), Width = 200 });
        var viewport = new WorkspaceViewport { Content = grid };
        var window = new Window { Content = viewport, Width = 683, Height = 360, Left = -10000, ShowActivated = false };
        window.Show(); ArrangeViewport(window);
        try
        {
            Assert.True(viewport.ScrollableHeight > 0);
            Assert.InRange(Descendants<DataGridRow>(grid).Count(), 1, 40);
            viewport.ScrollToBottom(); grid.ScrollIntoView(5000); window.UpdateLayout(); Pump();
            Assert.InRange(Descendants<DataGridRow>(grid).Count(), 1, 40);
            Assert.NotNull(grid.ItemContainerGenerator.ContainerFromItem(5000));
        }
        finally { window.Close(); }
    }

    private static void WaitForUiTask(Task task)
    {
        var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
        var frame = new System.Windows.Threading.DispatcherFrame();
        var timer = new System.Windows.Threading.DispatcherTimer(TimeSpan.FromSeconds(3), System.Windows.Threading.DispatcherPriority.Send, (_, _) => frame.Continue = false, dispatcher);
        _ = task.ContinueWith(_ => dispatcher.BeginInvoke(() => frame.Continue = false), TaskScheduler.Default);
        try { System.Windows.Threading.Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
    }

    private static void ArrangeViewport(Window window)
    {
        var size = new Size(window.Width, window.Height);
        var body = (FrameworkElement)window.Content;
        body.ClearValue(FrameworkElement.WidthProperty); body.ClearValue(FrameworkElement.HeightProperty);
        window.Measure(size); window.Arrange(new Rect(size)); window.UpdateLayout(); Pump();
        // Windows caps a shown HWND to this machine's monitor. Exercise the requested
        // logical client viewport minus real window chrome; native certification is separate.
        var chromeWidth = Math.Max(0, window.ActualWidth - body.ActualWidth);
        var chromeHeight = Math.Max(0, window.ActualHeight - body.ActualHeight);
        body.Width = window.Width - chromeWidth; body.Height = window.Height - chromeHeight;
        window.UpdateLayout(); Pump();
    }

    /// <summary>Real pointer and keyboard routing on the account tree: right-click selects the row under the pointer first; keys hit the commands.</summary>
    private static void VerifyAccountTreePointerAndKeys()
    {
        var fixture = new ShellFixture(); fixture.User.Permissions = PetraERP.Application.Security.PermissionCatalog.All.Select(permission => permission.Code).ToArray();
        var accounting = new AccountingFixture(); var parent = Guid.NewGuid();
        accounting.Accounts.AddRange([
            new() { Id = parent, Code = "1000", NameArabic = "الأصول", AccountType = PetraERP.ApiClient.AccountType.Assets, IsActive = true, PostingAllowed = false, HasChildren = true, Version = "AQ==" },
            new() { Id = Guid.NewGuid(), Code = "1001", NameArabic = "الصندوق", AccountType = PetraERP.ApiClient.AccountType.Assets, IsActive = true, PostingAllowed = true, ParentId = parent, Level = 1, HasJournalLines = true, Version = "Ag==" },
            new() { Id = Guid.NewGuid(), Code = "1002", NameArabic = "صندوق مؤقت", AccountType = PetraERP.ApiClient.AccountType.Assets, IsActive = true, PostingAllowed = true, ParentId = parent, Level = 1, Version = "Aw==" }]);
        using var shell = fixture.Create(new AdministrationFixture(), accounting: accounting);
        var window = new DesktopWindow(shell) { Left = -10000, ShowActivated = false, Width = 1366, Height = 768 };
        window.Show();
        try
        {
            shell.UserName = "admin"; shell.Password = "test"; shell.LoginCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            shell.NavigateToAsync("accounts", null).GetAwaiter().GetResult(); window.UpdateLayout(); Pump();
            var tree = shell.Pages.Accounts;
            var grid = Assert.Single(Descendants<DataGrid>(window), item => item.Name == "AccountsGrid");
            tree.SelectedNode = tree.Nodes[0]; window.UpdateLayout(); Pump();

            // Right-click on the third row selects it before the menu opens (not the stale first row).
            var row = (DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(2);
            var target = Descendants<TextBlock>(row).First(text => text.IsVisible);
            // A real right-click tunnels PreviewMouseDown; WPF raises PreviewMouseRightButtonDown on every element of that route.
            target.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right) { RoutedEvent = Mouse.PreviewMouseDownEvent });
            Pump();
            Assert.Equal("1002", tree.SelectedNode!.Account.Code); Assert.Single(grid.SelectedItems);
            var menu = grid.ContextMenu!; menu.PlacementTarget = grid; menu.IsOpen = true; Pump();
            var deleteItem = Assert.Single(menu.Items.OfType<MenuItem>(), item => item.InputGestureText == "Del");
            Assert.Same(tree.RequestDeleteCommand, deleteItem.Command); Assert.True(deleteItem.IsEnabled);
            menu.IsOpen = false; Pump();

            // Keys are routed through the grid's input bindings to the same commands as the menu.
            void Press(Key key, ModifierKeys modifiers = ModifierKeys.None)
            {
                var binding = Assert.Single(grid.InputBindings.OfType<KeyBinding>(), item => item.Key == key && item.Modifiers == modifiers);
                Assert.True(binding.Command.CanExecute(binding.CommandParameter), key.ToString());
                binding.Command.Execute(binding.CommandParameter); Pump();
            }

            var edits = 0; tree.EditRequested += (_, _) => edits++;
            Press(Key.E, ModifierKeys.Control); Assert.Equal(1, edits);
            Press(Key.Delete); Assert.True(tree.IsDeletePending);
            Assert.Same(tree.OpenLedgerCommand, Assert.Single(grid.InputBindings.OfType<KeyBinding>(), item => item.Key == Key.L && item.Modifiers == ModifierKeys.Control).Command);
            tree.CancelDeleteCommand.Execute(null);
            // A used account has no Delete: the key binding's command cannot run.
            tree.SelectedNode = tree.Nodes.Single(node => node.Account.Code == "1001"); Pump();
            Assert.False(Assert.Single(grid.InputBindings.OfType<KeyBinding>(), item => item.Key == Key.Delete).Command.CanExecute(null));
            Press(Key.Enter);
            Assert.IsType<PetraERP.Desktop.Accounting.AccountCardViewModel>(shell.CurrentView);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>A detached journal renders only in its own window, returns to the tab host, and closes under the tab rules.</summary>
    private static void VerifyDetachedDocumentWindows()
    {
        var fixture = new ShellFixture(); fixture.User.Permissions = PetraERP.Application.Security.PermissionCatalog.All.Select(permission => permission.Code).ToArray();
        var ledger = new FakeLedgerClient(); var posted = ledger.Add(fixture.Company.Id, fixture.Branch.Id, PetraERP.ApiClient.JournalState.Posted);
        using var shell = fixture.Create(new AdministrationFixture(), ledger, accounting: new AccountingFixture());
        var window = new DesktopWindow(shell) { Left = -10000, ShowActivated = false };
        window.Show();
        try
        {
            shell.UserName = "admin"; shell.Password = "test"; shell.LoginCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            shell.NavigateToAsync("journals", new PetraERP.Desktop.Shell.OpenJournalRequest(posted.Id)).GetAwaiter().GetResult();
            var postedDocument = shell.Documents.Active; var postedModel = shell.ActiveJournal!;
            shell.NavigateToAsync("journals", PetraERP.Desktop.Shell.NewJournalRequest.Instance).GetAwaiter().GetResult();
            var draftDocument = shell.Documents.Active; var draft = shell.ActiveJournal!; draft.JournalDescription = "مسودة في نافذة مستقلة";
            window.UpdateLayout(); Pump();

            shell.DetachDocumentCommand.Execute(draftDocument); window.UpdateLayout(); Pump();
            var detached = window.DetachedWindows.WindowOf(draftDocument)!; detached.UpdateLayout(); Pump();
            detached.Width = 700; detached.Height = 420; detached.Left += 12; detached.Top += 12; Pump();
            window.DetachedWindows.FlushPlacements();
            var remembered = PetraERP.Desktop.Platform.WindowsDocumentPlacement.Capture(detached)!;
            Assert.True(remembered.IsValid);
            Assert.DoesNotContain(fixture.Saved!.Documents!, reference => reference.Kind == "journal" && reference.Id is null);
            Assert.Same(postedModel, shell.CurrentView);
            Assert.Single(Descendants<PetraERP.Desktop.GeneralLedger.JournalDocumentView>(detached), view => ReferenceEquals(view.DataContext, draft));
            Assert.DoesNotContain(Descendants<PetraERP.Desktop.GeneralLedger.JournalDocumentView>(window), view => ReferenceEquals(view.DataContext, draft));
            Assert.Contains(Descendants<PetraERP.Desktop.GeneralLedger.JournalDocumentView>(window), view => ReferenceEquals(view.DataContext, postedModel));
            Assert.Equal(window.FlowDirection, detached.FlowDirection);
            // Contrast with a report window: an ordinary detached document can still go back to the tab host.
            Assert.Equal(Visibility.Visible, detached.AttachButton.Visibility);
            Assert.Equal(shell.Localization["D_CloseTab"], detached.CloseButton.Content);
            SaveViewport(detached, "journal-detached-ar");

            // The window's close box follows the tab rules: a cancelled confirmation keeps the window and the input.
            fixture.Confirm = false; detached.Close(); Pump();
            Assert.Equal("D_ConfirmCloseDirtyDocument", fixture.LastConfirmation); Assert.True(detached.IsVisible); Assert.Contains(draftDocument, shell.Documents.Documents);
            // A confirmation raised while a document is detached is shown (and answered) in the detached window too.
            var pending = shell.Dialogs!.ConfirmAsync("D_ConfirmPostJournal"); detached.UpdateLayout(); Pump();
            var dialogHost = Assert.Single(Descendants<PetraERP.Desktop.Shell.DialogHost>(detached)); Assert.True(dialogHost.IsVisible);
            Assert.False(detached.HandleShortcut(Key.W, ModifierKeys.Control));
            Assert.Single(Descendants<Button>(dialogHost), button => button.Name == "DialogCancel").Command.Execute(null);
            WaitForUiTask(pending); Assert.False(pending.Result); detached.UpdateLayout(); Pump(); Assert.False(dialogHost.IsVisible);
            // Shell shortcuts in the detached window: Ctrl+K opens the main palette; Ctrl+F/Alt+n act only inside the document.
            Assert.True(detached.HandleShortcut(Key.K, ModifierKeys.Control)); Assert.True(shell.Palette.IsOpen); shell.Palette.IsOpen = false;
            Assert.False(detached.HandleShortcut(Key.D1, ModifierKeys.Alt));
            // Main-window shortcuts act only on the tabs it hosts.
            Assert.True(window.HandleShortcut(Key.Tab, ModifierKeys.Control)); Assert.False(shell.Documents.Active.IsDetached);

            shell.AttachDocumentCommand.Execute(draftDocument); window.UpdateLayout(); Pump();
            Assert.Null(window.DetachedWindows.WindowOf(draftDocument)); Assert.False(detached.IsVisible); Assert.Null(((ContentControl)detached.ContentHost).Content);
            Assert.Same(draft, shell.CurrentView); Assert.Equal("مسودة في نافذة مستقلة", draft.JournalDescription);
            Assert.Single(Descendants<PetraERP.Desktop.GeneralLedger.JournalDocumentView>(window), view => ReferenceEquals(view.DataContext, draft));

            shell.DetachDocumentCommand.Execute(draftDocument); Pump();
            var again = window.DetachedWindows.WindowOf(draftDocument)!;
            var reopened = PetraERP.Desktop.Platform.WindowsDocumentPlacement.Capture(again)!;
            Assert.InRange(reopened.Width, remembered.Width - 1, remembered.Width + 1);
            Assert.InRange(reopened.Height, remembered.Height - 1, remembered.Height + 1);
            Assert.InRange(reopened.OffsetX, remembered.OffsetX - 1, remembered.OffsetX + 1);
            Assert.InRange(reopened.OffsetY, remembered.OffsetY - 1, remembered.OffsetY + 1);
            fixture.Confirm = true; again.Close(); Pump();
            Assert.DoesNotContain(draftDocument, shell.Documents.Documents); Assert.Empty(window.DetachedWindows.Windows); Assert.False(again.IsVisible);

            // Logout (like revocation and context switches) removes the document, which closes its window.
            // A removed/rearranged monitor and off-screen persisted offsets fall back to the owner's current monitor.
            shell.RememberWindowPlacement(postedDocument, new("removed-monitor", 100000, -100000, 900, 540, false));
            shell.DetachDocumentCommand.Execute(postedDocument); Pump(); Assert.Single(window.DetachedWindows.Windows);
            var savedWindow = window.DetachedWindows.WindowOf(postedDocument)!;
            var normal = PetraERP.Desktop.Platform.WindowsDocumentPlacement.Capture(savedWindow)!;
            Assert.NotEqual("removed-monitor", normal.Monitor); Assert.True(normal.OffsetX >= 0 && normal.OffsetY >= 0);
            savedWindow.WindowState = WindowState.Maximized; Pump(); window.DetachedWindows.FlushPlacements();
            var maximized = Assert.Single(fixture.Saved!.Documents!, item => item.Kind == "journal" && item.Id == posted.Id).Placement!;
            Assert.True(maximized.Maximized); Assert.InRange(maximized.Width, normal.Width - 1, normal.Width + 1);
            savedWindow.WindowState = WindowState.Minimized; Pump(); window.DetachedWindows.FlushPlacements();
            Assert.True(Assert.Single(fixture.Saved.Documents!, item => item.Kind == "journal" && item.Id == posted.Id).Placement!.Maximized);
            shell.ActivateDocumentCommand.Execute(postedDocument); Pump(); Assert.Equal(WindowState.Maximized, savedWindow.WindowState);
            savedWindow.WindowState = WindowState.Minimized; Pump();
            shell.LogoutCommand.ExecuteAsync(null).GetAwaiter().GetResult(); Pump();
            Assert.Empty(window.DetachedWindows.Windows);
            shell.UserName = "admin"; shell.Password = "test"; shell.LoginCommand.ExecuteAsync(null).GetAwaiter().GetResult(); Pump();
            var restored = Assert.Single(window.DetachedWindows.Windows);
            Assert.Equal(WindowState.Maximized, restored.WindowState);
            restored.WindowState = WindowState.Normal; Pump();
            var restoredBounds = PetraERP.Desktop.Platform.WindowsDocumentPlacement.Capture(restored)!;
            Assert.InRange(restoredBounds.Width, normal.Width - 1, normal.Width + 1);
            Assert.InRange(restoredBounds.Height, normal.Height - 1, normal.Height + 1);
            Assert.True(restoredBounds.OffsetX >= 0 && restoredBounds.OffsetY >= 0);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Reports are work instruments, so a report route must produce a real top-level window rather than only a tab:
    /// this shows the shell, navigates the ledger and trial-balance routes and asserts each has its own HWND rendering
    /// the report view outside the shell host, then captures what the operator actually sees.
    /// </summary>
    private static void VerifyReportPopupWindows()
    {
        var fixture = new ShellFixture();
        fixture.User.Permissions = PetraERP.Application.Security.PermissionCatalog.All.Select(permission => permission.Code).ToArray();
        using var shell = fixture.Create(new AdministrationFixture(), accounting: new AccountingFixture());
        var window = new DesktopWindow(shell) { Left = -20000, Top = -20000, Width = 1500, Height = 900, ShowActivated = false };
        window.Show();
        try
        {
            shell.UserName = "admin"; shell.Password = "test"; shell.LoginCommand.ExecuteAsync(null).GetAwaiter().GetResult(); Pump();
            var directory = Path.Combine(FindRoot(), ".artifacts", "reporting", "windows"); Directory.CreateDirectory(directory);
            foreach (var route in new[] { "account-ledger", "trial-balance" })
            {
                var page = shell.Pages.Page(route); Assert.NotNull(page);
                shell.NavigateToAsync(route, null).GetAwaiter().GetResult(); Pump(); window.UpdateLayout(); Pump();
                var document = Assert.Single(shell.Documents.Documents, item => item.Route == route);
                Assert.True(document.IsDetached, route + ": the report must leave the tab host.");
                Assert.NotSame(document, shell.Documents.Active);
                var reportWindow = window.DetachedWindows.WindowOf(document);
                Assert.NotNull(reportWindow);
                Assert.True(reportWindow!.IsVisible, route + ": the report window is not actually shown.");
                Assert.NotEqual(IntPtr.Zero, new System.Windows.Interop.WindowInteropHelper(reportWindow).Handle);
                Assert.Equal(window.FlowDirection, reportWindow.FlowDirection);
                Assert.Contains(Descendants<FrameworkElement>(reportWindow), element => ReferenceEquals(element.DataContext, page));
                Assert.DoesNotContain(Descendants<FrameworkElement>(window), element => ReferenceEquals(element.DataContext, page));
                // A report is a window by design, so its own bar must not talk about tabs.
                Assert.Equal(Visibility.Collapsed, reportWindow.AttachButton.Visibility);
                Assert.Equal(shell.Localization["D_CloseWindow"], reportWindow.CloseButton.Content);
                reportWindow.Width = 1180; reportWindow.Height = 760; Pump(); reportWindow.UpdateLayout();
                var bitmap = new RenderTargetBitmap((int)reportWindow.ActualWidth, (int)reportWindow.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(reportWindow);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var stream = File.Create(Path.Combine(directory, "popup-" + route + ".png"))) encoder.Save(stream);
            }
            Assert.Equal(2, window.DetachedWindows.Windows.Count);

            // Re-opening a report brings its own window forward instead of stacking a second document or HWND.
            var ledgerDocument = Assert.Single(shell.Documents.Documents, item => item.Route == "account-ledger");
            var firstHandle = new System.Windows.Interop.WindowInteropHelper(window.DetachedWindows.WindowOf(ledgerDocument)!).Handle;
            PetraERP.Desktop.Shell.WorkspaceDocument? reactivated = null; shell.Documents.DetachedActivationRequested += (_, item) => reactivated = item;
            shell.NavigateToAsync("account-ledger", null).GetAwaiter().GetResult(); Pump();
            Assert.Same(ledgerDocument, reactivated);
            Assert.Equal(2, window.DetachedWindows.Windows.Count);
            Assert.Equal(firstHandle, new System.Windows.Interop.WindowInteropHelper(window.DetachedWindows.WindowOf(ledgerDocument)!).Handle);

            // The window's close box is a document close: the shell removes the report and the host releases the HWND.
            window.DetachedWindows.WindowOf(ledgerDocument)!.Close(); Pump();
            Assert.DoesNotContain(shell.Documents.Documents, item => item.Route == "account-ledger");
            Assert.Single(window.DetachedWindows.Windows);
        }
        finally
        {
            foreach (var reportWindow in window.DetachedWindows.Windows.ToArray()) reportWindow.Close();
            window.Close();
        }
    }

    private static void VerifyResponsiveViewports(DesktopWindow window, string route, string language, ThemePreference theme)
    {
        foreach (var (physicalWidth, physicalHeight) in new[] { (1366, 768), (1920, 1080) })
        foreach (var scale in new[] { 1d, 1.25d, 1.5d, 2d })
        {
            window.Width = physicalWidth / scale; window.Height = physicalHeight / scale; ArrangeViewport(window); Assert.InRange(window.ActualWidth, window.MinWidth, window.Width + 1);
            Assert.InRange(window.ActualHeight, window.MinHeight, window.Height + 1);
            var body = (FrameworkElement)window.Content;
            Assert.InRange(body.ActualWidth, body.Width - 1, body.Width + 1);
            Assert.InRange(body.ActualHeight, body.Height - 1, body.Height + 1);
            Assert.Equal(language == "ar" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight, window.FlowDirection);
            var viewport = Assert.Single(Descendants<WorkspaceViewport>(window));
            Assert.True(viewport.ViewportWidth > 400 && viewport.ViewportHeight > 80, $"{route}: viewport {viewport.ViewportWidth}×{viewport.ViewportHeight}");
            if (physicalWidth == 1366 && scale == 2)
            {
                Assert.True(viewport.ScrollableHeight > 0);
                foreach (var host in Descendants<AdaptivePaneGrid>(window).Where(candidate => candidate.IsVisible))
                {
                    Assert.True(host.IsCompact, route);
                    // All panes retain their live controls; tab switching never recreates the page/model.
                    var model = host.DataContext;
                    for (var index = 0; host.SelectPane(index); index++)
                    {
                        window.UpdateLayout(); Pump(); Assert.Same(model, host.DataContext);
                        VerifyJournalNumericCells(host);
                        Assert.All(host.Children.OfType<FrameworkElement>().Where(child => child.IsVisible && child is not WrapPanel), pane =>
                            Assert.InRange(pane.ActualWidth, 1, host.ActualWidth + 1));
                        viewport.ScrollToTop(); window.UpdateLayout(); Pump();
                        SaveViewport(window, $"{route}-pane{index + 1}-{language}-{theme}");
                        viewport.ScrollToBottom(); window.UpdateLayout(); Pump();
                        SaveViewport(window, $"{route}-pane{index + 1}-bottom-{language}-{theme}");
                    }
                    host.SelectPane(0); window.UpdateLayout(); Pump();
                }
                viewport.ScrollToTop(); window.UpdateLayout(); Pump();
            }
            foreach (var grid in Descendants<DataGrid>(window))
            {
                Assert.True(grid.EnableRowVirtualization && grid.EnableColumnVirtualization);
                Assert.True(double.IsFinite(grid.ActualHeight));
            }
            if (physicalWidth == 1366 && scale == 2)
            {
                SaveViewport(window, $"{route}-{language}-{theme}");
            }
        }
    }
    private static void SaveViewport(Window window, string name)
    {
        var output = Path.Combine(FindRoot(), ".artifacts/viewport-acceptance/visual"); Directory.CreateDirectory(output);
        var bitmap = new RenderTargetBitmap(1366, 768, 192, 192, PixelFormats.Pbgra32); bitmap.Render(window);
        using var stream = File.Create(Path.Combine(output, $"{name}-1366-200.png"));
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); encoder.Save(stream);
    }
    private static void VerifyJournalNumericCells(DependencyObject root)
    {
        foreach (var grid in Descendants<DataGrid>(root).Where(grid => grid.IsVisible && GridLayouts.GetKey(grid) == "ledger.journal-lines"))
        foreach (var column in grid.Columns.OfType<DataGridTextColumn>().Where(column => column.SortMemberPath is "Debit" or "Credit" or "Rate"))
        foreach (var item in grid.Items)
        {
            grid.ScrollIntoView(item, column); grid.UpdateLayout(); Pump();
            var text = Assert.IsType<TextBlock>(column.GetCellContent(item));
            Assert.True(text.IsVisible);
            var formatted = new FormattedText(text.Text, System.Globalization.CultureInfo.InvariantCulture, text.FlowDirection,
                new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch), text.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(text).PixelsPerDip);
            Assert.True(formatted.WidthIncludingTrailingWhitespace + text.Padding.Left + text.Padding.Right <= text.ActualWidth + 1,
                $"{column.SortMemberPath}: common fixture amount '{text.Text}' must remain readable ({formatted.WidthIncludingTrailingWhitespace} > {text.ActualWidth})");
        }
    }
}
