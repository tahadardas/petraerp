using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PetraERP.Desktop.Controls;
using PetraERP.Desktop.Purchases;
using PetraERP.ApiClient;
using PetraERP.Desktop.Sales;
using PetraERP.Desktop.Shell;
using PetraERP.Desktop.Theming;

namespace PetraERP.Desktop.Tests;

public sealed partial class WpfFoundationTests
{
    private static void VerifyEntryExperience(WpfThemeService themes)
    {
        var fixture = new ShellFixture();
        fixture.User.Permissions = ["Sales.Invoices.View", "Sales.Invoices.Manage", "Purchases.Invoices.View", "Purchases.Invoices.Manage",
            "Inventory.Items.View", "Inventory.Items.Manage", "Parties.Directory.View", "Parties.Directory.Manage", "Patterns.Documents.View", "Patterns.Documents.Manage"];
        var data = new AccountingFixture();
        data.Parties.Add(new() { Id = Guid.NewGuid(), Code = "C001", NameArabic = "شركة الشام", Role = PartyRole.Both, IsActive = true });
        data.Items.Add(new() { Id = Guid.NewGuid(), Code = "M001", NameArabic = "إسمنت", IsActive = true });
        data.Patterns.Add(new() { Id = Guid.NewGuid(), Code = "SI", NameArabic = "فاتورة مبيعات", ShortCode = "SI", OperationKind = BusinessOperationKind.SalesInvoice,
            IsActive = true, IsRunnable = true, ActiveRevision = new() { Id = Guid.NewGuid(), RevisionNumber = 1 } });
        data.Patterns.Add(new() { Id = Guid.NewGuid(), Code = "PI", NameArabic = "فاتورة مشتريات", ShortCode = "PI", OperationKind = BusinessOperationKind.PurchaseInvoice,
            IsActive = true, IsRunnable = true, ActiveRevision = new() { Id = Guid.NewGuid(), RevisionNumber = 1 } });
        using var shell = fixture.Create(new AdministrationFixture(), accounting: data);
        shell.UserName = "admin"; shell.Password = "test"; shell.LoginCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        var phase = Environment.GetEnvironmentVariable("PETRAERP_UX_CAPTURE_PHASE") ?? "after";
        var directory = Path.Combine(FindRoot(), ".artifacts/entry-experience", phase); Directory.CreateDirectory(directory);
        foreach (var theme in new[] { ThemePreference.Light, ThemePreference.Dark })
        foreach (var language in new[] { "ar", "en" })
        {
            themes.Apply(theme); shell.Localization.SetLanguage(language);
            foreach (var route in new[] { "items", "parties", "patterns", "sales-invoice-card", "purchase-invoices" })
            {
                object? request = route switch { "sales-invoice-card" => new NewSalesInvoiceCardRequest(), "purchase-invoices" => new NewPurchaseRequest(), _ => null };
                shell.NavigateToAsync(route, request).GetAwaiter().GetResult();
                FrameworkElement view = route switch
                {
                    "items" => new PetraERP.Desktop.Inventory.ItemsView { DataContext = shell.Pages.Items },
                    "parties" => new PetraERP.Desktop.Parties.PartiesView { DataContext = shell.Pages.Parties },
                    "patterns" => new PetraERP.Desktop.Patterns.DocumentPatternsView { DataContext = shell.Pages.Patterns },
                    "purchase-invoices" => new PetraERP.Desktop.Purchases.PurchasesView { DataContext = shell.Pages.Purchases },
                    _ => new SalesInvoiceCardView { DataContext = shell.Documents.Active!.Content },
                };
                view.FlowDirection = shell.Localization.FlowDirection;
                var window = new Window { Content = view, Width = 1366, Height = 768, Left = -10000, ShowActivated = false };
                window.Show(); Pump(); window.UpdateLayout();
                try
                {
                    if (phase != "before") VerifyEntryActions(view, shell, route, window);
                    RenderEntry(window, Path.Combine(directory, $"{route}-{language}-{theme}.png"));
                    if (route == "purchase-invoices")
                    {
                        shell.Pages!.Purchases!.AddAdjustmentCommand.Execute(null);
                        foreach (var expander in Descendants<Expander>(view)) expander.IsExpanded = true;
                        Pump(); window.UpdateLayout(); Pump();
                        var headers = Descendants<System.Windows.Controls.Primitives.DataGridColumnHeader>(view).Select(h => string.Join("", Descendants<TextBlock>(h).Select(t => t.Text))).ToList();
                        File.WriteAllText(Path.Combine(directory, $"purchase-adjustment-headers-{language}-{theme}.txt"), string.Join("\n", headers));
                        RenderEntry(window, Path.Combine(directory, $"purchase-invoice-adjustments-{language}-{theme}.png"));
                    }
                    if (route == "sales-invoice-card" && shell.Documents.Active!.Content is SalesInvoiceCardViewModel card)
                    {
                        card.AddAdjustmentCommand.Execute(null);
                        foreach (var expander in Descendants<Expander>(view)) expander.IsExpanded = true;
                        Pump(); window.UpdateLayout(); Pump();
                        var headers = Descendants<System.Windows.Controls.Primitives.DataGridColumnHeader>(view).Select(h => string.Join("", Descendants<TextBlock>(h).Select(t => t.Text))).ToList();
                        File.WriteAllText(Path.Combine(directory, $"sales-adjustment-headers-{language}-{theme}.txt"), string.Join("\n", headers));
                        RenderEntry(window, Path.Combine(directory, $"sales-invoice-adjustments-{language}-{theme}.png"));
                    }
                }
                finally { window.Close(); Pump(); }
            }
        }
    }

    private static void VerifyEntryActions(FrameworkElement view, DesktopViewModel shell, string route, Window window)
    {
        if (route is "items" or "parties" or "patterns")
        {
            var grid = Assert.Single(Descendants<DataGrid>(view));
            Assert.True(GridRowContext.GetEnabled(grid));
            grid.SelectedIndex = 0;
            var cell = Descendants<DataGridCell>(grid).First();
            RightClick(cell); Pump();
            var menu = grid.ContextMenu!; menu.PlacementTarget = grid; menu.IsOpen = true; Pump();
            Assert.Same(grid.DataContext, menu.DataContext);
            var action = menu.Items.OfType<MenuItem>().First();
            Assert.NotNull(action.Command); Assert.True(action.IsEnabled);
            Assert.DoesNotContain(Descendants<TextBlock>(view), block => block.Text.Contains('Ø') || block.Text.StartsWith("D_", StringComparison.Ordinal));
            menu.IsOpen = false;
            Assert.True(GridRowContext.SelectTarget(grid, grid));
            Assert.Null(grid.SelectedItem);
            Assert.False(action.Command.CanExecute(action.CommandParameter));
            return;
        }

        DataGrid lines;
        object lower;
        string pricePath;
        Func<string> price;
        Action<string> setPrice;
        Func<string?> notes;
        if (route == "sales-invoice-card")
        {
            var vm = Assert.IsType<SalesInvoiceCardViewModel>(view.DataContext);
            var first = vm.Lines[0]; first.UnitPrice = "123.4500"; first.Notes = "نص عربي من السطر الأعلى";
            var second = vm.Lines[1]; second.UnitPrice = "7.0000";
            lower = second; pricePath = nameof(second.UnitPrice); price = () => second.UnitPrice; setPrice = value => second.UnitPrice = value; notes = () => second.Notes;
            lines = Assert.Single(Descendants<DataGrid>(view), grid => grid.Name == "LinesGrid");
            Assert.NotEqual(first.Id, second.Id);
        }
        else
        {
            var vm = Assert.IsType<PurchasesViewModel>(view.DataContext);
            Assert.Equal(1, vm.WorkspaceTab);
            var first = vm.Lines[0]; first.UnitCost = "123.4500"; first.Notes = "نص عربي من السطر الأعلى";
            var second = vm.Lines[1]; second.UnitCost = "7.0000";
            lower = second; pricePath = nameof(second.UnitCost); price = () => second.UnitCost; setPrice = value => second.UnitCost = value; notes = () => second.Notes;
            lines = Assert.Single(Descendants<DataGrid>(view), grid => grid.Name == "PurchaseLinesGrid");
        }
        window.UpdateLayout(); Pump();
        Assert.False(lines.CanUserDeleteRows); Assert.False(lines.IsReadOnly);
        var priceColumn = Assert.Single(lines.Columns, column => GridEntry.GetCopyPath(column) == pricePath);
        lines.CurrentCell = new(lower, priceColumn); lines.ScrollIntoView(lower, priceColumn); lines.BeginEdit(); Pump();
        // This invokes the real PreviewKeyDown shortcut, including an active cell editor.
        var key = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.F4) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        lines.RaiseEvent(key); Pump(); Assert.True(key.Handled); Assert.Equal("123.4500", price());
        lines.CommitEdit();
        var noteColumn = Assert.Single(lines.Columns, column => GridEntry.GetCopyPath(column) == "Notes");
        lines.CurrentCell = new(lower, noteColumn); lines.ScrollIntoView(lower, noteColumn); Pump();
        Assert.True(GridEntry.CopyFromPrevious(lines)); Assert.Equal("نص عربي من السطر الأعلى", notes()); lines.CommitEdit();
        lines.CurrentCell = new(lines.Items[0], priceColumn); Assert.False(GridEntry.CopyFromPrevious(lines));
        lines.CurrentCell = new(lower, lines.Columns.First(column => column.IsReadOnly)); Assert.False(GridEntry.CopyFromPrevious(lines));
        lines.CurrentCell = new(lower, priceColumn); lines.IsReadOnly = true; setPrice("8.0000"); Assert.False(GridEntry.CopyFromPrevious(lines)); Assert.Equal("8.0000", price());
        lines.IsReadOnly = false;
        lines.SelectedIndex = 0;
        lines.ScrollIntoView(lower, priceColumn); window.UpdateLayout(); Pump();
        var lowerRow = (DataGridRow)lines.ItemContainerGenerator.ContainerFromItem(lower);
        var clicked = Descendants<DataGridCell>(lowerRow).First(cell => ReferenceEquals(cell.Column, priceColumn));
        RightClick(clicked); Pump(); Assert.Same(lower, lines.SelectedItem); Assert.Same(lower, lines.CurrentCell.Item);
        var context = lines.ContextMenu!; context.PlacementTarget = lines; context.IsOpen = true;
        CommandManager.InvalidateRequerySuggested(); Pump();
        Assert.Same(view.DataContext, context.DataContext);
        var copy = context.Items.OfType<MenuItem>().First(); Assert.True(copy.IsEnabled);
        Assert.Same(lines, copy.CommandTarget); ((RoutedCommand)copy.Command).Execute(null, lines); Assert.Equal("123.4500", price());
        context.IsOpen = false; lines.CommitEdit();
        Assert.True(GridRowContext.SelectTarget(lines, lines)); Assert.Null(lines.SelectedItem);
        Assert.False(GridEntry.CopyPreviousCell.CanExecute(null, lines));
        // Search uses loaded code/name records and preserves typed IDs through the real control binding.
        var picker = Descendants<LookupPicker>(view).First(p => p.IsEnabled && p.ItemsSource is not null && p.ItemsSource.Cast<LookupItem>().Any(item => item.Code == "M001"));
        picker.Search("إس"); Assert.Single(picker.Results); Assert.True(picker.CommitHighlighted()); Assert.Equal(dataItemId(picker), picker.SelectedId);
        static Guid dataItemId(LookupPicker picker) => picker.ItemsSource!.Cast<LookupItem>().Single(item => item.Code == "M001").Id;
    }

    private static void RightClick(DataGridCell cell) => cell.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right)
        { RoutedEvent = Mouse.PreviewMouseDownEvent });

    private static void RenderEntry(Window window, string path)
    {
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}
