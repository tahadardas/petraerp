using System.Windows;
using System.Windows.Controls;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Relationships;
using PetraERP.Desktop.Theming;

namespace PetraERP.Desktop.Tests;

public sealed partial class WpfFoundationTests
{
    private static void VerifyRelatedDocuments(WpfThemeService themes)
    {
        foreach (var theme in new[] { ThemePreference.Light, ThemePreference.Dark })
        foreach (var language in new[] { "ar", "en" })
        {
            themes.Apply(theme);
            var fixture = new ShellFixture(); var loc = new LocalizationService(language);
            var vm = new RelatedDocumentsViewModel(new RelatedDocumentsTests.RelatedFixture { Total = 51 }, new(), new(loc), loc);
            vm.SetSource(RelatedDocumentsTests.Scope(fixture), RelatedDocumentSource.SupplierPurchases, Guid.NewGuid());
            var view = new RelatedDocumentsView { DataContext = vm, FlowDirection = loc.FlowDirection };
            var window = new Window { Content = view, Width = 1000, Height = 400, Left = -10000, ShowActivated = false };
            window.Show();
            try
            {
                Pump(); vm.RefreshCommand.ExecuteAsync(null).GetAwaiter().GetResult(); Pump(); window.UpdateLayout();
                var grid = Assert.Single(Descendants<DataGrid>(view)); Assert.True(grid.IsReadOnly); Assert.Equal(50, grid.Items.Count);
                Assert.Contains(Descendants<TextBlock>(view), block => block.IsVisible && block.Text == "سند مرتبط");
                Assert.Contains(Descendants<TextBlock>(view), block => block.IsVisible && block.Text == loc["D_LinkedPurchases"]);
                loc.SetLanguage(language == "ar" ? "en" : "ar"); Pump();
                Assert.Contains(Descendants<TextBlock>(view), block => block.IsVisible && block.Text == loc["D_LinkedPurchases"]);
                Assert.Contains(Descendants<TextBlock>(view), block => block.IsVisible && block.Text == loc["D_State_Posted"]);
                vm.NextCommand.ExecuteAsync(null).GetAwaiter().GetResult(); Pump(); Assert.Single(grid.Items);
                grid.SelectedIndex = 0; Pump();
                Assert.True(Assert.Single(Descendants<Button>(view), b => ReferenceEquals(b.Command, vm.OpenCommand)).IsEnabled);
                vm.SetSource(null, RelatedDocumentSource.SupplierPurchases, null); Pump(); Assert.False(view.IsVisible);
            }
            finally { window.Close(); Pump(); }
        }
    }
}
