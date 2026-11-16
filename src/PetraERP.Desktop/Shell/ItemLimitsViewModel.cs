using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.Desktop.Inventory;
using PetraERP.Desktop.Localization;

namespace PetraERP.Desktop.Shell;

/// <summary>One item against its stock limits.</summary>
public sealed record ItemLimitRow(Guid Id, string Code, string Name, decimal Stock, decimal? Minimum, decimal? Maximum)
{
    public bool BelowMinimum => Minimum is { } min && Stock < min;
    public bool AboveMaximum => Maximum is { } max && max > 0m && Stock > max;
    public string StockText => Stock.ToString("0.####", CultureInfo.InvariantCulture);
    public string MinimumText => Minimum?.ToString("0.####", CultureInfo.InvariantCulture) ?? string.Empty;
    public string MaximumText => Maximum?.ToString("0.####", CultureInfo.InvariantCulture) ?? string.Empty;
}

/// <summary>
/// Al-Ameen "حدود المادة وجردها" (materials ▸ tools): every item's current stock against its minimum and maximum, so the items to
/// reorder or to stop buying are visible at once. Read-only; the numbers come from the item cards and the posted stock.
/// </summary>
public sealed partial class ItemLimitsViewModel : PageViewModel
{
    private const int PageSize = 200;
    private readonly IInventoryClient client;
    private readonly ShellNavigator navigator;
    private List<ItemLimitRow> all = [];

    [ObservableProperty] private string filter = "all";
    [ObservableProperty] private ItemLimitRow? selectedRow;
    [ObservableProperty] private string search = string.Empty;

    public ItemLimitsViewModel(IInventoryClient client, ShellNavigator navigator, DesktopOperation operation, ILocalizationService localization) : base(operation, localization)
    {
        this.client = client; this.navigator = navigator;
        RefreshCommand = new AsyncRelayCommand(() => RunAsync(ReloadAsync), () => HasContext && Idle);
        OpenCommand = new AsyncRelayCommand(() => SelectedRow is { } row ? navigator.NavigateAsync("item-card", new OpenItemCardRequest(row.Id)) : Task.CompletedTask,
            () => HasContext && SelectedRow is not null);
    }

    protected override string RequiredPermission => "Inventory.Items.View";
    public ObservableCollection<ItemLimitRow> Rows { get; } = [];
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand OpenCommand { get; }
    public int BelowMinimumCount => all.Count(r => r.BelowMinimum);
    public int AboveMaximumCount => all.Count(r => r.AboveMaximum);
    public string Summary => string.Format(Localization.Culture, Localization["D_ItemLimitsSummary"], all.Count, BelowMinimumCount, AboveMaximumCount);

    public void Localize() => OnPropertyChanged(nameof(Summary));

    protected override Task LoadAsync(object? parameter, CancellationToken cancellationToken) => ReloadAsync(cancellationToken);

    private async Task ReloadAsync(CancellationToken cancellationToken)
    {
        var rows = new List<ItemLimitRow>();
        for (var page = 1; ; page++)
        {
            var result = await client.ListItemsAsync(CompanyId, BranchId, page, PageSize, null, null, null, true, cancellationToken).ConfigureAwait(true);
            var items = result.Items ?? [];
            foreach (var item in items)
            {
                rows.Add(new ItemLimitRow(item.Id ?? Guid.Empty, item.Code ?? string.Empty,
                    !Localization.IsRightToLeft && !string.IsNullOrWhiteSpace(item.NameEnglish) ? item.NameEnglish! : item.NameArabic ?? string.Empty,
                    Parse(item.CurrentStock) ?? 0m, Parse(item.MinStock), Parse(item.MaxStock)));
            }

            if (items.Count < PageSize || rows.Count >= (result.TotalCount ?? rows.Count)) break;
        }

        cancellationToken.ThrowIfCancellationRequested();
        all = rows;
        Apply();
        OnPropertyChanged(nameof(Summary));
    }

    private static decimal? Parse(string? text) =>
        decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : null;

    partial void OnFilterChanged(string value) => Apply();

    partial void OnSearchChanged(string value) => Apply();

    partial void OnSelectedRowChanged(ItemLimitRow? value) => OpenCommand.NotifyCanExecuteChanged();

    private void Apply()
    {
        var term = Search.Trim();
        IEnumerable<ItemLimitRow> query = all;
        if (Filter == "below") query = query.Where(r => r.BelowMinimum);
        else if (Filter == "above") query = query.Where(r => r.AboveMaximum);
        if (term.Length > 0) query = query.Where(r => Controls.LookupText.Matches(term, r.Code, r.Name, null));
        Rows.Clear();
        foreach (var row in query.OrderBy(r => r.Code, StringComparer.Ordinal)) Rows.Add(row);
    }

    protected override void ClearPage()
    {
        all = []; Rows.Clear(); SelectedRow = null; Filter = "all"; Search = string.Empty;
    }

    protected override void NotifyCommands()
    {
        RefreshCommand?.NotifyCanExecuteChanged(); OpenCommand?.NotifyCanExecuteChanged();
    }
}
