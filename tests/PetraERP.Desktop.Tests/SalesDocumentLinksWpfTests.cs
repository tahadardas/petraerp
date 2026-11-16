using System.Windows;
using System.Windows.Controls;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Sales;
using PetraERP.Desktop.Theming;

namespace PetraERP.Desktop.Tests;

public sealed partial class WpfFoundationTests
{
    private static void VerifySalesDocumentLinks(WpfThemeService themes)
    {
        foreach (var theme in new[] { ThemePreference.Light, ThemePreference.Dark })
        foreach (var language in new[] { "ar", "en" })
        {
            themes.Apply(theme);
            var fixture = new ShellFixture(); var localization = new LocalizationService(language);
            var client = new SalesDocumentLinkTests.LinksFixture();
            var vm = new SalesDocumentLinksViewModel(client, new(), new(localization), localization);
            vm.SetSource(SalesDocumentLinkTests.Scope(fixture), SalesLinkSource.Customer, Guid.NewGuid());
            var view = new SalesDocumentLinksView { DataContext = vm };
            var window = new Window { Content = view, Width = 1000, Height = 350, Left = -10000, ShowActivated = false };
            window.Show();
            try
            {
                Pump(); vm.RefreshCommand.ExecuteAsync(null).GetAwaiter().GetResult(); Pump(); window.UpdateLayout();
                var grid = Assert.Single(Descendants<DataGrid>(view)); Assert.True(grid.IsReadOnly); Assert.Equal(50, grid.Items.Count);
                Assert.Contains(Descendants<TextBlock>(view), block => block.IsVisible && block.Text == localization["D_LinkNumber"]);
                Assert.Contains(Descendants<TextBlock>(view), block => block.IsVisible && block.Text == "فاتورة زبون عربي");
                var next = Assert.Single(Descendants<Button>(view), button => ReferenceEquals(button.Command, vm.NextCommand)); Assert.True(next.IsEnabled);
                vm.NextCommand.ExecuteAsync(null).GetAwaiter().GetResult(); Pump(); Assert.Single(grid.Items);
                grid.SelectedIndex = 0; Pump();
                Assert.True(Assert.Single(Descendants<Button>(view), button => ReferenceEquals(button.Command, vm.OpenCommand)).IsEnabled);
            }
            finally { window.Close(); Pump(); }
        }
    }
}
