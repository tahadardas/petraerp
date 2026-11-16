using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;
using PetraERP.Desktop.Controls;
using PetraERP.Desktop.Inventory;
using PetraERP.Desktop.Localization;

namespace PetraERP.Desktop.Shell;

/// <summary>
/// Al-Ameen "تعديل أسعار بطاقات المواد": raise or lower the general prices of the active items by a percentage, optionally for one
/// group and one price type. The tool always previews first (how many items and price rows would change) and asks before applying.
/// </summary>
public sealed partial class ItemPriceToolViewModel : PageViewModel
{
    private readonly IInventoryClient client;
    private readonly IUserConfirmation confirmation;

    [ObservableProperty] private Guid? groupId;
    [ObservableProperty] private string priceType = string.Empty;
    [ObservableProperty] private string percentText = string.Empty;
    [ObservableProperty] private string roundingText = "2";
    [ObservableProperty] private string statusText = string.Empty;
    [ObservableProperty] private bool previewed;

    public ItemPriceToolViewModel(IInventoryClient client, IUserConfirmation confirmation, DesktopOperation operation, ILocalizationService localization) : base(operation, localization)
    {
        this.client = client; this.confirmation = confirmation;
        PreviewCommand = new AsyncRelayCommand(() => RunAsync(PreviewAsync), () => CanRun);
        ApplyCommand = new AsyncRelayCommand(() => RunAsync(ApplyAsync), () => CanRun && Previewed && Can("Inventory.Items.Manage"));
    }

    protected override string RequiredPermission => "Inventory.Items.Manage";
    public ObservableCollection<LookupItem> Groups { get; } = [];
    public IReadOnlyList<string> PriceTypes { get; } = ["", "WHOLESALE", "SEMI_WHOLESALE", "DISTRIBUTOR", "EXPORT", "RETAIL", "CONSUMER", "LAST_PURCHASE"];
    public AsyncRelayCommand PreviewCommand { get; }
    public AsyncRelayCommand ApplyCommand { get; }
    public bool CanRun => HasContext && Idle && TryRequest(out _);

    public void Localize() => OnPropertyChanged(nameof(StatusText));

    protected override async Task LoadAsync(object? parameter, CancellationToken cancellationToken)
    {
        if (Groups.Count == 0)
        {
            var groups = await client.ListItemGroupsAsync(CompanyId, BranchId, cancellationToken).ConfigureAwait(true);
            foreach (var group in groups.OrderBy(g => g.Code, StringComparer.Ordinal))
                Groups.Add(new LookupItem(group.Id ?? Guid.Empty, group.Code ?? string.Empty, group.NameArabic ?? string.Empty, null));
        }

        NotifyCommands();
    }

    partial void OnGroupIdChanged(Guid? value) => Invalidate();

    partial void OnPriceTypeChanged(string value) => Invalidate();

    partial void OnPercentTextChanged(string value) => Invalidate();

    partial void OnRoundingTextChanged(string value) => Invalidate();

    private void Invalidate()
    {
        Previewed = false; StatusText = string.Empty;
        NotifyCommands();
    }

    private bool TryRequest(out AdjustItemPricesRequest? request)
    {
        request = null;
        if (!decimal.TryParse(PercentText?.Trim(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var percent)
            || percent == 0m || percent < -90m || percent > 1000m || decimal.Round(percent, 4) != percent) return false;
        if (!int.TryParse(RoundingText?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var scale) || scale > 4) return false;
        request = new AdjustItemPricesRequest
        {
            GroupId = GroupId, PriceListCode = string.IsNullOrWhiteSpace(PriceType) ? null : PriceType,
            Percent = percent.ToString("0.####", CultureInfo.InvariantCulture), RoundingScale = scale, PreviewOnly = true,
        };
        return true;
    }

    private async Task PreviewAsync(CancellationToken cancellationToken)
    {
        if (!TryRequest(out var request) || request is null) return;
        var result = await client.AdjustItemPricesAsync(CompanyId, BranchId, request, cancellationToken).ConfigureAwait(true);
        Previewed = (result.RowCount) > 0;
        StatusText = string.Format(Localization.Culture, Localization["D_PriceTool_Preview"], result.ItemCount, result.RowCount);
        NotifyCommands();
    }

    private async Task ApplyAsync(CancellationToken cancellationToken)
    {
        if (!TryRequest(out var request) || request is null) return;
        if (!await confirmation.ConfirmAsync("D_PriceTool_Confirm").ConfigureAwait(true)) return;
        request.PreviewOnly = false;
        var result = await client.AdjustItemPricesAsync(CompanyId, BranchId, request, cancellationToken).ConfigureAwait(true);
        Previewed = false;
        StatusText = string.Format(Localization.Culture, Localization["D_PriceTool_Applied"], result.ItemCount, result.RowCount);
        NotifyCommands();
    }

    protected override void ClearPage()
    {
        Groups.Clear(); GroupId = null; PriceType = string.Empty; PercentText = string.Empty; RoundingText = "2"; StatusText = string.Empty; Previewed = false;
    }

    protected override void NotifyCommands()
    {
        OnPropertyChanged(nameof(CanRun));
        PreviewCommand?.NotifyCanExecuteChanged(); ApplyCommand?.NotifyCanExecuteChanged();
    }
}
