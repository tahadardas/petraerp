using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;
using PetraERP.Desktop.GeneralLedger;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Treasury;

namespace PetraERP.Desktop.Shell;

/// <summary>
/// One dashboard tile. <see cref="Value"/> is a count returned by the server for the current company/branch, or "—" when
/// it could not be read; a tile is never filled with an estimate, a sample or a cached value from another scope.
/// </summary>
public sealed partial class DashboardTile(string id, string label, string glyph, string route, object? parameter) : ObservableObject
{
    [ObservableProperty] private string value = "—";
    [ObservableProperty] private bool isAvailable;
    [ObservableProperty] private bool needsAttention;

    public string Id { get; } = id;
    public string Label { get; } = label;
    public string Glyph { get; } = glyph;
    public string Route { get; } = route;
    public object? Parameter { get; } = parameter;
}

/// <summary>
/// Home dashboard: real, permission-filtered counts from existing endpoints only — journals in Draft and Approved
/// (awaiting posting) and commercial papers overdue / due today / due within seven days. There are no financial KPIs
/// (revenue, profit, cash) because no dashboard query exists for them yet; that gap is documented, not simulated.
/// Reads run outside the shared busy flag so the dashboard never blocks a screen, and late results of a previous
/// company/branch are dropped.
/// </summary>
public sealed partial class DashboardViewModel : ObservableObject
{
    private readonly IGeneralLedgerClient? ledger;
    private readonly ICommercialPapersClient? papers;
    private readonly ILocalizationService localization;
    private ShellContext? context;
    private long generation;

    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private string asOf = string.Empty;
    [ObservableProperty] private bool hasError;

    public DashboardViewModel(IGeneralLedgerClient? ledger, ICommercialPapersClient? papers, ILocalizationService localization)
    {
        this.ledger = ledger; this.papers = papers; this.localization = localization;
        RefreshCommand = new AsyncRelayCommand(() => RefreshAsync(context), () => context is not null && !IsLoading);
    }

    public ObservableCollection<DashboardTile> Tiles { get; } = [];
    public bool HasTiles => Tiles.Count > 0;
    public AsyncRelayCommand RefreshCommand { get; }

    /// <summary>Reloads the tiles for a context (null clears them, for sign-out or a company switch).</summary>
    public async Task RefreshAsync(ShellContext? shellContext)
    {
        var stamp = ++generation;
        context = shellContext;
        Build(shellContext);
        RefreshCommand.NotifyCanExecuteChanged();
        if (shellContext is null || Tiles.Count == 0) { AsOf = string.Empty; return; }
        IsLoading = true; HasError = false;
        try
        {
            foreach (var tile in Tiles.ToArray())
            {
                var count = await ReadAsync(tile, shellContext).ConfigureAwait(true);
                if (stamp != generation) return;
                tile.IsAvailable = count is not null;
                tile.Value = count?.ToString("N0", CultureInfo.InvariantCulture) ?? "—";
                tile.NeedsAttention = count > 0 && tile.Id is "papers.overdue" or "papers.today";
                HasError |= count is null;
            }

            AsOf = string.Format(localization.Culture, localization["UI_AsOf"], DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture));
        }
        finally
        {
            if (stamp == generation) { IsLoading = false; RefreshCommand.NotifyCanExecuteChanged(); }
        }
    }

    private void Build(ShellContext? shellContext)
    {
        Tiles.Clear();
        if (shellContext is not null)
        {
            if (ledger is not null && shellContext.Can("Ledger.Journals.View"))
            {
                Tiles.Add(new DashboardTile("journals.draft", localization["UI_Kpi_DraftJournals"], "", "journals", null));
                Tiles.Add(new DashboardTile("journals.approved", localization["UI_Kpi_ApprovedJournals"], "", "journals", null));
            }

            if (papers is not null && shellContext.Can("Treasury.Vouchers.View"))
            {
                Tiles.Add(new DashboardTile("papers.overdue", localization["UI_Kpi_PapersOverdue"], "", "cheques", null));
                Tiles.Add(new DashboardTile("papers.today", localization["UI_Kpi_PapersToday"], "", "cheques", null));
                Tiles.Add(new DashboardTile("papers.soon", localization["UI_Kpi_PapersSoon"], "", "cheques", null));
            }
        }

        OnPropertyChanged(nameof(HasTiles));
    }

    private CommercialPaperAlertsResponse? alerts;

    private async Task<long?> ReadAsync(DashboardTile tile, ShellContext shellContext)
    {
        try
        {
            switch (tile.Id)
            {
                case "journals.draft":
                    return (await ledger!.SearchJournalsAsync(shellContext.CompanyId, shellContext.BranchId, JournalState.Draft, null, null, null, 1, 1, CancellationToken.None).ConfigureAwait(true)).TotalCount;
                case "journals.approved":
                    return (await ledger!.SearchJournalsAsync(shellContext.CompanyId, shellContext.BranchId, JournalState.Approved, null, null, null, 1, 1, CancellationToken.None).ConfigureAwait(true)).TotalCount;
                case "papers.overdue":
                    alerts = await papers!.GetAlertsAsync(shellContext.CompanyId, shellContext.BranchId, 7, CancellationToken.None).ConfigureAwait(true);
                    return alerts.OverdueCount;
                case "papers.today":
                    return alerts?.DueTodayCount;
                case "papers.soon":
                    return alerts?.DueSoonCount;
                default:
                    return null;
            }
        }
        catch (Exception exception) when (exception is PetraApiException or HttpRequestException or OperationCanceledException or TimeoutException
            or Connection.SessionUnavailableException or InvalidOperationException or NotSupportedException or NotImplementedException)
        {
            if (tile.Id == "papers.overdue") alerts = null;
            return null;
        }
    }
}
