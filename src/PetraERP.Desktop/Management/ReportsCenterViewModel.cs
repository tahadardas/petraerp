using System.Net.Http;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Management;

public sealed record ReportEntryRow(ReportCatalogEntryDto Entry, bool IsOpenable, string StateLabel)
{
    public string Id => Entry.Id ?? string.Empty;
    public string Group => Entry.Group ?? string.Empty;
    public string Name => Entry.NameArabic ?? Entry.NameEnglish ?? string.Empty;
}

/// <summary>A top-menu group of the report tree (the menu a report naturally belongs to) with its reports.</summary>
public sealed record ReportTreeNode(string MenuId, string Title, IReadOnlyList<ReportEntryRow> Reports);

/// <summary>
/// Report catalogue browser. An entry opens only when the server marks it Implemented / Partial AND the desktop has a matching route the user may open;
/// everything else is shown as a visibly disabled "not implemented" row — never a silent no-op.
/// </summary>
public sealed partial class ReportsCenterViewModel : PageViewModel
{
    public const string ViewPermission = "Ledger.Reports.View";

    private readonly IManagementClient client;
    private readonly ShellNavigator navigator;
    private readonly NavigationService navigation;
    private IReadOnlyList<ReportEntryRow> all = [];
    private IReadOnlyList<ReportEntryRow> menuRows = [];
    private (Guid Company, Guid Branch)? menuScope;

    [ObservableProperty] private string search = string.Empty;
    [ObservableProperty] private string groupFilter = string.Empty;
    [ObservableProperty] private string deliveryFilter = string.Empty;
    [ObservableProperty] private ReportEntryRow? selectedEntry;
    [ObservableProperty] private string summary = string.Empty;

    public ReportsCenterViewModel(IManagementClient client, ShellNavigator navigator, NavigationService navigation, DesktopOperation operation, ILocalizationService localization)
        : base(operation, localization)
    {
        this.client = client;
        this.navigator = navigator;
        this.navigation = navigation;
        RefreshCommand = new AsyncRelayCommand(() => RunAsync(LoadAllAsync), () => HasContext && Idle);
        OpenCommand = new AsyncRelayCommand(OpenAsync, () => SelectedEntry is { IsOpenable: true });
    }

    public ObservableCollection<ReportEntryRow> Entries { get; } = [];
    /// <summary>The same filtered reports grouped by the menu they naturally belong to (حسابات، زبائن، مواد...).</summary>
    public ObservableCollection<ReportTreeNode> Tree { get; } = [];
    public ObservableCollection<string> Groups { get; } = [];
    public IReadOnlyList<string> Deliveries { get; } = ["", "Implemented", "Partial", "NotImplemented"];
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand OpenCommand { get; }

    protected override string RequiredPermission => ViewPermission;
    protected override Task LoadAsync(object? parameter, CancellationToken cancellationToken) => RunAsync(LoadAllAsync);
    protected override void ClearPage() { Entries.Clear(); Tree.Clear(); Groups.Clear(); all = []; menuRows = []; menuScope = null; SelectedEntry = null; Summary = string.Empty; }
    protected override void NotifyCommands() { RefreshCommand?.NotifyCanExecuteChanged(); OpenCommand?.NotifyCanExecuteChanged(); }

    partial void OnSearchChanged(string value) => ApplyFilter();
    partial void OnGroupFilterChanged(string value) => ApplyFilter();
    partial void OnDeliveryFilterChanged(string value) => ApplyFilter();
    partial void OnSelectedEntryChanged(ReportEntryRow? value) => OpenCommand.NotifyCanExecuteChanged();

    private async Task LoadAllAsync(CancellationToken token)
    {
        var catalog = await client.GetReportCatalogAsync(CompanyId, BranchId, token).ConfigureAwait(true);
        all = BuildRows(catalog);
        Groups.Clear(); Groups.Add(string.Empty);
        foreach (var g in all.Select(r => r.Group).Distinct(StringComparer.Ordinal).OrderBy(g => g, StringComparer.Ordinal)) Groups.Add(g);
        Summary = $"الإجمالي {catalog.Total} — منفّذ {catalog.Implemented} — جزئي {catalog.Partial} — غير منفّذ {catalog.NotImplemented}";
        ApplyFilter();
    }

    private List<ReportEntryRow> BuildRows(ReportMasterCatalogResponse catalog) => (catalog.Items ?? []).Select(e =>
    {
        var destination = ReportRouteDispatcher.Resolve(e.Route, e.Id);
        var routeOk = e.Delivery != "NotImplemented" && destination is not null && navigation.Allows(destination.Route);
        return new ReportEntryRow(e, routeOk, e.Delivery switch { "Implemented" => "منفّذ", "Partial" => "جزئي", _ => "غير منفّذ" });
    }).ToList();

    /// <summary>
    /// Loads the catalogue once per company and branch for the top-menu "reports" sub-menus (independent of the page being open).
    /// Returns false when it could not be loaded; the menu then says so instead of listing anything.
    /// </summary>
    public async Task<bool> EnsureCatalogAsync(Guid companyId, Guid branchId, CancellationToken cancellationToken)
    {
        if (menuScope == (companyId, branchId)) return true;
        if (!navigation.Has(ViewPermission)) return false;
        try
        {
            var catalog = await client.GetReportCatalogAsync(companyId, branchId, cancellationToken).ConfigureAwait(true);
            menuRows = BuildRows(catalog);
            menuScope = (companyId, branchId);
            return true;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>The implemented, permitted reports that belong to one top menu, ordered by catalogue id.</summary>
    public IReadOnlyList<ReportEntryRow> OpenableForMenu(string menuId) =>
        menuRows.Where(row => row.IsOpenable && ReportMenuPlacement.MenuFor(row.Entry.Group) == menuId).OrderBy(row => row.Id, StringComparer.Ordinal).ToArray();

    private void ApplyFilter()
    {
        Entries.Clear();
        foreach (var r in all.Where(r => (GroupFilter.Length == 0 || r.Group == GroupFilter) && (DeliveryFilter.Length == 0 || r.Entry.Delivery == DeliveryFilter)
            && (Search.Length == 0 || r.Name.Contains(Search, StringComparison.CurrentCultureIgnoreCase) || r.Id.Contains(Search, StringComparison.OrdinalIgnoreCase))))
            Entries.Add(r);
        Tree.Clear();
        foreach (var node in Entries.GroupBy(r => ReportMenuPlacement.MenuFor(r.Entry.Group), StringComparer.Ordinal)
                     .OrderBy(g => Array.IndexOf(MenuOrder, g.Key)))
            Tree.Add(new ReportTreeNode(node.Key, Localization[ReportMenuPlacement.TitleKey(node.Key)], node.OrderBy(r => r.Id, StringComparer.Ordinal).ToArray()));
    }

    private static readonly string[] MenuOrder = ["invoices", "vouchers", "parties", "inventory", "accounts", "financial", "cost-centers", "tools"];

    private async Task OpenAsync()
    {
        if (SelectedEntry is { IsOpenable: true, Entry.Route: { } route } && ReportRouteDispatcher.Resolve(route, SelectedEntry.Id) is { } destination)
            await navigator.NavigateAsync(destination.Route, destination.Parameter).ConfigureAwait(true);
    }
}
