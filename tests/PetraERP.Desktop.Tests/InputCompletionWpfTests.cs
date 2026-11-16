using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using PetraERP.ApiClient;
using PetraERP.Desktop.Controls;
using PetraERP.Desktop.Shell;
using PetraERP.Desktop.Theming;

namespace PetraERP.Desktop.Tests;

public sealed partial class WpfFoundationTests
{
    private static void VerifyInputCompletion(WpfThemeService themes)
    {
        VerifyRowNumberRecycling();
        var folder = Path.Combine(FindRoot(), ".artifacts/input-completion-2026-10-19/visual"); Directory.CreateDirectory(folder);
        foreach (var theme in new[] { ThemePreference.Light, ThemePreference.Dark })
        foreach (var language in new[] { "ar", "en" })
        {
            themes.Apply(theme);
            var fixture = new ShellFixture(); fixture.User.Permissions = ["Patterns.Documents.View", "Patterns.Documents.Manage", "Ledger.Accounts.View", "Ledger.CostCenters.View",
                "Parties.Directory.View", "Parties.Directory.Manage", "Treasury.Vouchers.View", "Treasury.Vouchers.Manage"];
            var data = new AccountingFixture(); var accountId = Guid.NewGuid();
            data.Accounts.Add(new() { Id = accountId, Code = "4001", NameArabic = "مبيعات بيتون جاهز", NameEnglish = "Ready mixed concrete sales", PostingAllowed = true, IsActive = true });
            using var shell = fixture.Create(new AdministrationFixture(), accounting: data);
            shell.UserName = "admin"; shell.Password = "test"; shell.LoginCommand.ExecuteAsync(null).GetAwaiter().GetResult(); shell.Localization.SetLanguage(language);
            shell.NavigateToAsync("patterns", new NewPatternCardRequest()).GetAwaiter().GetResult();
            var pattern = Assert.IsType<Patterns.PatternCardViewModel>(shell.CurrentView);
            var view = new Patterns.PatternCardView { DataContext = pattern, FlowDirection = shell.Localization.FlowDirection };
            var window = new Window { Content = view, Width = 1200, Height = 768, Left = -10000, ShowActivated = false };
            window.Show(); Pump();
            try
            {
                Assert.Contains(Descendants<TextBlock>(view), b => b.Text == shell.Localization["D_Code"] + " *");
                var tabs = Assert.Single(Descendants<TabControl>(view)); tabs.SelectedIndex = 1; Pump(); window.UpdateLayout();
                pattern.DraftSalesEnforced = true; Pump();
                Assert.Contains(Descendants<TextBlock>(view), b => b.Text == shell.Localization["D_Role_Sales"] + " *");
                var picker = Assert.Single(Descendants<LookupPicker>(view), p => BindingOperations.GetBinding(p, LookupPicker.SelectedIdProperty)?.Path.Path == "DraftSalesAccountId");
                picker.Search("مب جا"); Assert.Single(picker.Results); Assert.True(picker.CommitHighlighted()); Assert.Equal(accountId, pattern.DraftSalesAccountId);
                Pump(); window.UpdateLayout();
                Assert.Contains(Descendants<TextBox>(picker), box => box.Text.Contains("مبيعات", StringComparison.Ordinal) || box.Text.Contains("concrete", StringComparison.Ordinal));
                Assert.False(pattern.CanSaveDraft); // Required code/name/short-code still missing.
                RenderEntry(window, Path.Combine(folder, $"pattern-accounts-{language}-{theme}.png"));
                window.Content = null;
                shell.NavigateToAsync("treasury-vouchers", new Treasury.NewVoucherRequest(VoucherKind.Payment)).GetAwaiter().GetResult();
                var treasury = shell.Pages.Vouchers!;
                var voucherView = new Treasury.VouchersView { DataContext = treasury, FlowDirection = shell.Localization.FlowDirection }; window.Content = voucherView;
                Pump(); window.UpdateLayout();
                var accountPicker = Descendants<LookupPicker>(voucherView).First(p => BindingOperations.GetBinding(p, LookupPicker.SelectedIdProperty)?.Path.Path == "AccountId");
                accountPicker.Search("جا مب"); Assert.Single(accountPicker.Results); Assert.True(accountPicker.CommitHighlighted()); Assert.Equal(accountId, treasury.LineRows[0].AccountId);
                Pump(); window.UpdateLayout();
                Assert.Contains(Descendants<TextBlock>(voucherView), b => b.Text == shell.Localization["D_AutomaticNumberAtPosting"]);
                var grid = Assert.Single(Descendants<DataGrid>(voucherView), g => ReferenceEquals(g.ItemsSource, treasury.LineRows));
                Assert.True(GridRowNumbers.GetEnabled(grid)); Assert.Equal(42, grid.RowHeaderWidth); Assert.True(grid.HeadersVisibility.HasFlag(DataGridHeadersVisibility.Row));
                Assert.Contains(Descendants<System.Windows.Controls.Primitives.DataGridRowHeader>(grid), header => header.ActualWidth >= 40 && header.IsVisible);
                Assert.Equal("1", ((DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(0)).Header);
                RenderEntry(window, Path.Combine(folder, $"payment-{language}-{theme}.png"));
            }
            finally { window.Close(); Pump(); }
        }
    }

    private static void VerifyRowNumberRecycling()
    {
        var rows = new ObservableCollection<Sales.SalesInvoiceLineItem>(Enumerable.Range(0, 80).Select(i => new Sales.SalesInvoiceLineItem { Notes = i.ToString("D3", System.Globalization.CultureInfo.InvariantCulture) }));
        var grid = new DataGrid { ItemsSource = rows, CanUserAddRows = false }; grid.Columns.Add(new DataGridTextColumn { Binding = new Binding("Notes") }); GridRowNumbers.SetEnabled(grid, true);
        var window = new Window { Content = grid, Width = 500, Height = 220, Left = -10000, ShowActivated = false }; window.Show(); Pump();
        try
        {
            Assert.Equal("1", ((DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(0)).Header);
            var second = rows[1]; rows.RemoveAt(0); window.UpdateLayout(); Pump();
            Assert.Equal("1", ((DataGridRow)grid.ItemContainerGenerator.ContainerFromItem(second)).Header);
            var last = rows[^1]; grid.ScrollIntoView(last); Pump(); window.UpdateLayout();
            Assert.Equal("79", ((DataGridRow)grid.ItemContainerGenerator.ContainerFromItem(last)).Header);
            rows.RemoveAt(0); Pump(); window.UpdateLayout();
            Assert.Equal("78", ((DataGridRow)grid.ItemContainerGenerator.ContainerFromItem(last)).Header);
            grid.Items.SortDescriptions.Add(new SortDescription("Notes", ListSortDirection.Descending)); grid.ScrollIntoView(last); Pump(); window.UpdateLayout();
            Assert.Equal("1", ((DataGridRow)grid.ItemContainerGenerator.ContainerFromItem(last)).Header);
            Assert.True(Descendants<DataGridRow>(grid).Count() < rows.Count);
        }
        finally { window.Close(); Pump(); }
    }
}
