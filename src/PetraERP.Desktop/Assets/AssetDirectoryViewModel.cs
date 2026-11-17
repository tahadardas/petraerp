using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Assets;

/// <summary>
/// Directory and management workstation for all fixed, movable, and fleet/machinery assets.
/// </summary>
public sealed partial class AssetDirectoryViewModel : PageViewModel
{
    private readonly IAssetsClient client;
    private readonly ShellNavigator navigator;

    [ObservableProperty] private string searchQuery = string.Empty;
    [ObservableProperty] private string selectedTypeFilter = "All";
    [ObservableProperty] private AssetDto? selectedAsset;
    [ObservableProperty] private int totalAssetCount;
    [ObservableProperty] private decimal totalAcquisitionCost;
    [ObservableProperty] private decimal totalCurrentBookValue;

    public ObservableCollection<AssetDto> FilteredAssets { get; } = [];
    private readonly List<AssetDto> allAssets = [];

    public AssetDirectoryViewModel(IAssetsClient client, ShellNavigator navigator, DesktopOperation operation, ILocalizationService localization)
        : base(operation, localization)
    {
        this.client = client;
        this.navigator = navigator;
        RefreshCommand = new AsyncRelayCommand(() => LoadAsync(null, CancellationToken.None));
        OpenDossierCommand = new AsyncRelayCommand(OpenDossierAsync, () => SelectedAsset != null);
    }

    protected override string RequiredPermission => "Ledger.Accounts.View";

    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand OpenDossierCommand { get; }

    protected override async Task LoadAsync(object? parameter, CancellationToken cancellationToken)
    {
        var items = await client.ListAssetsAsync(cancellationToken);
        allAssets.Clear();
        allAssets.AddRange(items);

        ApplyFilter();
    }

    partial void OnSearchQueryChanged(string value) => ApplyFilter();
    partial void OnSelectedTypeFilterChanged(string value) => ApplyFilter();
    partial void OnSelectedAssetChanged(AssetDto? value) => OpenDossierCommand.NotifyCanExecuteChanged();

    private void ApplyFilter()
    {
        FilteredAssets.Clear();
        var q = (SearchQuery ?? string.Empty).Trim();

        var query = allAssets.AsEnumerable();

        if (SelectedTypeFilter == "Machinery")
            query = query.Where(a => a.AssetType == AssetType.MachineryVehicle);
        else if (SelectedTypeFilter == "Movable")
            query = query.Where(a => a.AssetType == AssetType.Movable);
        else if (SelectedTypeFilter == "Fixed")
            query = query.Where(a => a.AssetType == AssetType.Fixed);

        if (!string.IsNullOrWhiteSpace(q))
        {
            query = query.Where(a =>
                a.AssetCode.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                a.NameArabic.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                a.SerialNumber.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                (a.MachineryDetails != null && a.MachineryDetails.PlateNumber.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                (a.CurrentCustodian != null && a.CurrentCustodian.Contains(q, StringComparison.OrdinalIgnoreCase)));
        }

        foreach (var item in query)
        {
            FilteredAssets.Add(item);
        }

        TotalAssetCount = FilteredAssets.Count;
        TotalAcquisitionCost = FilteredAssets.Sum(a => a.AcquisitionCost);
        TotalCurrentBookValue = FilteredAssets.Sum(a => a.CurrentBookValue);

        if (SelectedAsset == null || !FilteredAssets.Contains(SelectedAsset))
        {
            SelectedAsset = FilteredAssets.FirstOrDefault();
        }
    }

    private Task OpenDossierAsync()
    {
        if (SelectedAsset is null) return Task.CompletedTask;
        return navigator.NavigateAsync("asset-dossier", SelectedAsset.Id);
    }

    protected override void ClearPage()
    {
        FilteredAssets.Clear();
        SelectedAsset = null;
    }
}
