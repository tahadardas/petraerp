using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PetraERP.Desktop.Shell;

/// <summary>
/// The program options window. Local presentation preferences preview at once and <b>Cancel</b> restores them; company financial
/// policies are never sent while editing: only <b>Apply</b> / <b>OK</b> save them through the API, which checks permission,
/// version and the audit reason. The last visited category is remembered for the session.
/// </summary>
public partial class ProgramOptionsWindow : Window
{
    private static string? lastCategory;

    private readonly DesktopViewModel shell;
    private readonly PreferencesViewModel page;
    private readonly Dictionary<string, FrameworkElement> sections = [];
    private PreferencesViewModel.AppearanceSnapshot snapshot;
    private bool committed;
    private bool reverted;

    public ProgramOptionsWindow(DesktopViewModel shell, PreferencesViewModel page)
    {
        InitializeComponent();
        this.shell = shell;
        this.page = page;
        DataContext = page;
        snapshot = page.CaptureAppearance();
        foreach (var category in ProgramOptionsCatalog.Categories)
            if (FindName("Section_" + category.Id) is FrameworkElement section) sections[category.Id] = section;

        var items = ProgramOptionsCatalog.Categories.Where(IsAvailable)
            .Select(category => new CategoryItem(category, page.Localization[category.TitleKey])).ToList();
        CategoryList.ItemsSource = items;
        CategoryList.SelectedItem = items.FirstOrDefault(item => item.Category.Id == lastCategory) ?? items.FirstOrDefault();
        Closing += (_, _) => { if (!committed) Revert(); };
    }

    /// <summary>A category shown in the list.</summary>
    public sealed record CategoryItem(OptionCategory Category, string Title);

    private bool IsAvailable(OptionCategory category) => category.Scope switch
    {
        OptionScope.Company => page.HasCompanyContext,
        OptionScope.Link => category.Route is not null && shell.Navigation.Allows(category.Route),
        _ => true,
    };

    private void OnCategoryChanged(object sender, SelectionChangedEventArgs args)
    {
        if (CategoryList.SelectedItem is CategoryItem item) ShowCategory(item);
    }

    private void ShowCategory(CategoryItem item)
    {
        lastCategory = item.Category.Id;
        foreach (var (id, section) in sections) section.Visibility = id == item.Category.Id ? Visibility.Visible : Visibility.Collapsed;
        SectionTitle.Text = item.Title;
        ScopeNote.Text = page.Localization[item.Category.Scope switch
        {
            OptionScope.Company => "OPT_ScopeCompany",
            OptionScope.Link => "OPT_ScopeLink",
            _ => "OPT_ScopeLocal",
        }];
        CompanyFooter.Visibility = item.Category.Scope == OptionScope.Company ? Visibility.Visible : Visibility.Collapsed;
        SectionScroll.ScrollToTop();
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs args)
    {
        var results = ProgramOptionsCatalog.Search(SearchBox.Text, key => page.Localization[key], IsAvailable);
        var searching = !string.IsNullOrWhiteSpace(SearchBox.Text);
        ResultPanel.Visibility = searching ? Visibility.Visible : Visibility.Collapsed;
        NoResults.Visibility = searching && results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ResultList.ItemsSource = results;
    }

    private void OnResultChanged(object sender, SelectionChangedEventArgs args)
    {
        if (ResultList.SelectedItem is not OptionSearchResult result) return;
        if (CategoryList.ItemsSource is not List<CategoryItem> items || items.FirstOrDefault(item => item.Category.Id == result.Category.Id) is not { } target) return;
        CategoryList.SelectedItem = target;
        if (result.Entry is { } entry && FindName(entry.ElementName) is FrameworkElement field)
        {
            UpdateLayout();
            field.BringIntoView();
            field.Focus();
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            args.Handled = true;
        }
    }

    /// <summary>Saves pending company policy changes through the API; false (and the window stays open) when they were not accepted.</summary>
    private async Task<bool> ApplyAsync()
    {
        if (page.HasPolicyChanges)
        {
            if (!page.CanManageCompanyPolicy) { page.PolicyStatusMessage = page.Localization["OPT_NotAllowed"]; return false; }
            if (string.IsNullOrWhiteSpace(page.PolicyReason))
            {
                page.PolicyStatusMessage = page.Localization["OPT_ReasonRequired"];
                CompanyFooter.Visibility = Visibility.Visible;
                return false;
            }

            await page.SaveCompanyPolicyAsync().ConfigureAwait(true);
            if (page.HasPolicyChanges) return false;
        }

        snapshot = page.CaptureAppearance();
        return true;
    }

    private async void OnOk(object sender, RoutedEventArgs args)
    {
        if (!await ApplyAsync().ConfigureAwait(true)) return;
        committed = true;
        Close();
    }

    private async void OnApply(object sender, RoutedEventArgs args) => _ = await ApplyAsync().ConfigureAwait(true);

    private void OnCancel(object sender, RoutedEventArgs args) => Close();

    private void OnDefault(object sender, RoutedEventArgs args) => page.ResetAppearanceCommand.Execute(null);

    private async void OnOpenRoute(object sender, RoutedEventArgs args)
    {
        if (sender is not FrameworkElement { Tag: string route }) return;
        if (!await ApplyAsync().ConfigureAwait(true)) return;
        committed = true;
        Close();
        await shell.NavigateToAsync(route, null).ConfigureAwait(true);
    }

    /// <summary>Restores what the options looked like on opening and drops unsaved policy edits.</summary>
    private void Revert()
    {
        if (reverted) return;
        reverted = true;
        page.RestoreAppearance(snapshot);
        if (page.HasPolicyChanges) _ = page.LoadCompanyPolicyAsync();
    }
}
