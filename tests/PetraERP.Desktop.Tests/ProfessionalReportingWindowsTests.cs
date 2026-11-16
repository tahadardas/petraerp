using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PetraERP.ApiClient;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Reporting;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Tests;

[Collection(WpfApplicationGroup.Name)]
public sealed class ProfessionalReportingWindowsTests
{
    [Fact]
    public async Task FourReportWindowsRenderBothThemesAtWideAndCompactWidthsAndPreviewReturnsToExactParameters()
    {
        // The report windows contain component styles whose corner radii are StaticResource references into the theme
        // dictionaries, and WPF resolves those against the ambient Application only - so this renders on the shared
        // WPF host thread, which owns the one Application the process may have, exactly like App.xaml does.
        await WpfApplicationHost.RunAsync(async () =>
        {
            try { await VerifyAsync().ConfigureAwait(true); }
            finally { WpfApplicationHost.ResetResources(); }
        }, TimeSpan.FromSeconds(120), TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    private static async Task VerifyAsync()
    {
        WpfApplicationHost.MergeThemes("Themes/Light.xaml", "Themes/Dense.xaml", "Themes/Components.xaml");
        WpfApplicationHost.Application.Resources["StandardLabel"] = new StandardLabelConverter();
        var root = Directory.GetCurrentDirectory();
        while (!File.Exists(Path.Combine(root, "PetraERP.slnx"))) root = Directory.GetParent(root)!.FullName;
        var directory = Path.Combine(root, ".artifacts", "reporting", "windows"); Directory.CreateDirectory(directory);
        foreach (var kind in Enum.GetValues<ProfessionalReportKind>())
        {
            var client = new ProfessionalReportingWorkflowTests.ReportClient { Kind = kind };
            var localization = new LocalizationService("ar");
            using var page = new ProfessionalReportPageViewModel(kind, client, new Lookups(), new Presets(), new DesktopOperation(localization), localization);
            await page.ActivateAsync(ProfessionalReportingWorkflowTests.Context() with { FiscalYearId = client.Year.Id }, null, TestContext.Current.CancellationToken).ConfigureAwait(true);
            page.Account.Selected = new(Guid.NewGuid(), "4110", "حساب");
            page.Party.Selected = new(Guid.NewGuid(), "C001", "زبون"); page.DescriptionContains = "دفعة عربية";
            if (page.IsPartyStatement) page.CurrencyCode = "USD";
            page.DescriptionNotContains = "ملغى"; page.IncludeEmptyAccounts = true;
            var view = new ProfessionalReportParametersView { DataContext = page };
            var window = new Window { Content = view, Width = 1420, Height = 900, ShowActivated = false, Left = -20000, Top = -20000, WindowStartupLocation = WindowStartupLocation.Manual };
            var workspaceDirectory = Path.Combine(directory, "workspace", kind.ToString());
            try
            {
                foreach (var theme in new[] { "Light", "Dark" })
                {
                    window.Resources.MergedDictionaries.Clear();
                    foreach (var file in new[] { theme, "Dense", "Components" })
                        window.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/PetraERP.Desktop;component/Themes/" + file + ".xaml", UriKind.Relative) });
                    window.Show();
                    foreach (var width in new[] { 1420, 860 })
                    {
                        window.Width = width; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
                        var compactLayout = ((TabControl)view.FindName("CompactLayout")).Visibility;
                        Assert.True((width < 1000 ? Visibility.Visible : Visibility.Collapsed) == compactLayout,
                            $"{kind}/{theme}/{width}: compact layout is {compactLayout}; layout option={ReportDisplayOptionsHub.Current.ParametersLayout}, view width={view.ActualWidth}, window width={window.ActualWidth}");
                        Save(window, Path.Combine(directory, kind + "_" + theme + "_" + width + ".png"));
                    }
                }
                Assert.Equal(0, client.Executions);
                var account = page.Account.Selected?.Id; var from = page.FromDate; var to = page.ToDate;
                var sources = page.Sources.Select(choice => choice.IsSelected).ToArray(); var fields = page.Fields.Select(choice => choice.IsSelected).ToArray();
                var previewClosed = false;
                var drillThroughs = 0;
                page.SourceRequested += _ => drillThroughs++;
                var previewVerified = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                view.PreviewOpened += preview =>
                {
                    preview.WorkspaceStore = new PetraReportWorkspaceStore(workspaceDirectory);
                    preview.ShowActivated = false;
                    preview.ContentRendered += async (_, _) =>
                    {
                        try
                        {
                            var grid = (PetraInteractiveReportGrid)preview.FindName("ResultsGrid");
                            for (var attempt = 0; attempt < 200 && grid.Document is null; attempt++)
                            {
                                await Task.Delay(10).ConfigureAwait(true);
                                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                            }
                            Assert.NotNull(grid.Document);
                            Assert.Equal(1, grid.VisibleRowCount);
                            var viewer = (DocumentViewer)preview.FindName("PrintViewer");
                            Assert.Null(viewer.Document);
                            Save(preview, Path.Combine(directory, kind + "_results.png"));
                            ((Button)preview.FindName("FieldsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            var fieldsList = (ListBox)preview.FindName("ColumnFields");
                            fieldsList.SelectedIndex = 0;
                            var visibleColumns = grid.DataGrid.Columns.Count(column => column.Visibility == Visibility.Visible);
                            ((Button)preview.FindName("HideColumnButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            Assert.Equal(visibleColumns - 1, grid.DataGrid.Columns.Count(column => column.Visibility == Visibility.Visible));
                            ((Button)preview.FindName("ShowColumnButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            Assert.Equal(visibleColumns, grid.DataGrid.Columns.Count(column => column.Visibility == Visibility.Visible));
                            var originalDisplayIndex = grid.DataGrid.Columns[0].DisplayIndex;
                            ((Button)preview.FindName("MoveColumnDownButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            Assert.NotEqual(originalDisplayIndex, grid.DataGrid.Columns[0].DisplayIndex);
                            ((Button)preview.FindName("FreezeColumnButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            Assert.True(grid.DataGrid.FrozenColumnCount > 0);
                            ((Button)preview.FindName("ResetColumnsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            Assert.Equal(0, grid.DataGrid.FrozenColumnCount);
                            ((TextBox)preview.FindName("WorkspaceName")).Text = "مساحة اختبار";
                            ((Button)preview.FindName("SaveWorkspaceButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            var workspaceList = (ComboBox)preview.FindName("WorkspaceList");
                            for (var attempt = 0; attempt < 200 && workspaceList.Items.Count == 0; attempt++)
                                await Task.Delay(10).ConfigureAwait(true);
                            Assert.Single(workspaceList.Items);
                            ((Button)preview.FindName("HideColumnButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            ((Button)preview.FindName("LoadWorkspaceButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            for (var attempt = 0; attempt < 200 && grid.DataGrid.Columns.Count(column => column.Visibility == Visibility.Visible) != visibleColumns; attempt++)
                                await Task.Delay(10).ConfigureAwait(true);
                            Assert.Equal(visibleColumns, grid.DataGrid.Columns.Count(column => column.Visibility == Visibility.Visible));
                            for (var attempt = 0; attempt < 5; attempt++) await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                            Save(preview, Path.Combine(directory, kind + "_fields.png"));
                            ((Button)preview.FindName("FieldsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            ((Button)preview.FindName("AddGroupingButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            Assert.Single(grid.GroupingKeys);
                            grid.UpdateLayout();
                            var groupExpander = FindVisual<Expander>(grid);
                            Assert.NotNull(groupExpander);
                            groupExpander.IsExpanded = false;
                            Assert.False(groupExpander.IsExpanded);
                            groupExpander.IsExpanded = true;
                            Save(preview, Path.Combine(directory, kind + "_grouped.png"));
                            ((Button)preview.FindName("ClearGroupingButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            Assert.Empty(grid.GroupingKeys);
                            grid.DataGrid.SelectedIndex = 0;
                            ((Button)preview.FindName("OpenSourceButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            Assert.True(preview.IsVisible);
                            Assert.Equal(1, drillThroughs);
                            var search = (TextBox)preview.FindName("SearchBox");
                            var searchResult = (TextBlock)preview.FindName("SearchResultLabel");
                            search.Text = "دفعة";
                            ((Button)preview.FindName("NextMatchButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            for (var attempt = 0; attempt < 100 && searchResult.Text != "1 / 1"; attempt++)
                                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                            Assert.Equal("1 / 1", searchResult.Text);
                            ((Button)preview.FindName("PreviousMatchButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            Assert.Equal("1 / 1", searchResult.Text);
                            ((Button)preview.FindName("OpenSourceButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            Assert.True(preview.IsVisible);
                            Assert.Equal(2, drillThroughs);
                            Assert.Equal("1 / 1", searchResult.Text);
                            var filterColumn = (ComboBox)preview.FindName("FilterColumn");
                            var filterOperator = (ComboBox)preview.FindName("FilterOperator");
                            filterColumn.SelectedValue = "Debit";
                            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                            filterOperator.SelectedValue = PetraReportFilterOperator.GreaterThan;
                            ((TextBox)preview.FindName("FilterValue")).Text = "100";
                            ((Button)preview.FindName("AddFilterButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            ((Button)preview.FindName("ApplyFilterButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            for (var deadline = DateTime.UtcNow.AddSeconds(15); DateTime.UtcNow < deadline && grid.VisibleRowCount != 0;)
                                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                            Assert.Equal(0, grid.VisibleRowCount);
                            // An empty result must still read as an empty report: columns present, message shown.
                            Assert.NotEmpty(grid.DataGrid.Columns);
                            Assert.True(grid.ShowsEmptyState);
                            Assert.NotEmpty(((ItemsControl)preview.FindName("AuthoritativeTotals")).Items);
                            ((ComboBox)preview.FindName("PrintScope")).SelectedIndex = 1;
                            Assert.NotNull(preview.OutputDocument);
                            Assert.Empty(preview.OutputDocument!.Rows);
                            Assert.NotEmpty(preview.OutputDocument.Summaries);
                            Assert.Contains("Output scope / نطاق الإخراج", preview.OutputDocument.Filters.Keys);
                            ((Button)preview.FindName("FilterButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            Save(preview, Path.Combine(directory, kind + "_filter.png"));
                            ((Button)preview.FindName("ClearFiltersButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            for (var deadline = DateTime.UtcNow.AddSeconds(15); DateTime.UtcNow < deadline && grid.VisibleRowCount != 1;)
                                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                            Assert.Equal(1, grid.VisibleRowCount);
                            Assert.False(grid.ShowsEmptyState);
                            ((TabControl)preview.FindName("ViewerTabs")).SelectedIndex = 1;
                            for (var attempt = 0; attempt < 200 && viewer.Document is null; attempt++)
                                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                            Assert.NotNull(viewer.Document);
                            Save(preview, Path.Combine(directory, kind + "_preview.png"));
                            previewVerified.TrySetResult();
                        }
                        catch (Exception exception) { previewVerified.TrySetException(exception); }
                        finally { preview.Close(); previewClosed = true; }
                    };
                };
                await page.PreviewCommand.ExecuteAsync(null).ConfigureAwait(true);
                for (var attempt = 0; attempt < 10 && !previewVerified.Task.IsCompleted; attempt++)
                    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                if (!previewVerified.Task.IsCompleted && !string.IsNullOrWhiteSpace(page.Message))
                    throw new InvalidOperationException(page.Message);
                await previewVerified.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken).ConfigureAwait(true);
                Assert.True(previewClosed, kind + ": " + page.Message); Assert.Equal(1, client.Executions); Assert.Same(page, view.DataContext);
                Assert.Equal(account, page.Account.Selected?.Id); Assert.Equal(from, page.FromDate); Assert.Equal(to, page.ToDate);
                Assert.Equal("دفعة عربية", page.DescriptionContains); Assert.Equal("ملغى", page.DescriptionNotContains);
                Assert.Equal(sources, page.Sources.Select(choice => choice.IsSelected)); Assert.Equal(fields, page.Fields.Select(choice => choice.IsSelected));
                Assert.True(page.IncludeEmptyAccounts); Assert.False(page.IsExecuting);
            }
            finally
            {
                window.Close();
                if (Directory.Exists(workspaceDirectory)) Directory.Delete(workspaceDirectory, true);
            }
        }
    }

    private static void Save(FrameworkElement element, string path)
    {
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)element.ActualWidth, (int)element.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }

    private static T? FindVisual<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T found) return found;
            if (FindVisual<T>(child) is { } nested) return nested;
        }
        return null;
    }

    private sealed class Lookups : IProfessionalReportLookups
    {
        public Task<ProfessionalLookupPage> SearchPageAsync(Guid companyId, Guid branchId, ProfessionalLookupKind kind, string text, int page, CancellationToken cancellationToken) => Task.FromResult(new ProfessionalLookupPage([], 0, false, false));
        public Task<PartyControlAccount> GetPartyControlAccountAsync(Guid companyId, Guid branchId, ProfessionalLookupKind kind, Guid partyId, CancellationToken cancellationToken) => Task.FromResult(new PartyControlAccount(PartyControlAccountState.Available, "1100", "ذمم"));
        public Task<IReadOnlyList<ProfessionalLookupItem>> SearchAsync(Guid companyId, Guid branchId, ProfessionalLookupKind kind, string text, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ProfessionalLookupItem>>([]);
        public Task<ProfessionalLookupItem> ResolveAsync(Guid companyId, Guid branchId, ProfessionalLookupKind kind, Guid id, CancellationToken cancellationToken) => Task.FromResult(new ProfessionalLookupItem(id, "4110", "حساب"));
    }
    private sealed class Presets : IProfessionalReportPresetStore
    {
        public Task<IReadOnlyList<ProfessionalReportPreset>> LoadAsync(ShellContext context, ProfessionalReportKind kind, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ProfessionalReportPreset>>([]);
        public Task SaveAsync(ShellContext context, ProfessionalReportPreset preset, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
