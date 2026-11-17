using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.Contracts.Assets;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Assets;

/// <summary>
/// Executive report workstation displaying the complete asset dossier and Total Cost of Ownership (TCO).
/// </summary>
public sealed partial class AssetDossierTcoViewModel : PageViewModel
{
    private readonly IAssetsClient client;

    [ObservableProperty] private AssetDto? selectedAsset;
    [ObservableProperty] private AssetDossierTcoReportDto? dossierReport;
    [ObservableProperty] private bool isLoading;

    public ObservableCollection<AssetDto> AssetsList { get; } = [];
    public ObservableCollection<AssetDossierExpenseSummaryLineDto> ExpenseCategories { get; } = [];

    public AssetDossierTcoViewModel(IAssetsClient client, DesktopOperation operation, ILocalizationService localization)
        : base(operation, localization)
    {
        this.client = client;
        RefreshReportCommand = new AsyncRelayCommand(LoadReportAsync, () => SelectedAsset != null);
    }

    protected override string RequiredPermission => "Ledger.Reports.View";

    public IAsyncRelayCommand RefreshReportCommand { get; }

    protected override async Task LoadAsync(object? parameter, CancellationToken cancellationToken)
    {
        var list = await client.ListAssetsAsync(cancellationToken);
        AssetsList.Clear();
        foreach (var a in list) AssetsList.Add(a);

        if (parameter is Guid targetId)
        {
            SelectedAsset = AssetsList.FirstOrDefault(a => a.Id == targetId) ?? AssetsList.FirstOrDefault();
        }
        else
        {
            SelectedAsset ??= AssetsList.FirstOrDefault();
        }

        await LoadReportAsync();
    }

    partial void OnSelectedAssetChanged(AssetDto? value)
    {
        RefreshReportCommand.NotifyCanExecuteChanged();
        if (value != null)
        {
            _ = LoadReportAsync();
        }
    }

    private async Task LoadReportAsync()
    {
        if (SelectedAsset == null) return;

        IsLoading = true;
        try
        {
            var report = await client.GetAssetDossierTcoReportAsync(SelectedAsset.Id);
            DossierReport = report;

            ExpenseCategories.Clear();
            foreach (var cat in report.ExpenseCategories)
            {
                ExpenseCategories.Add(cat);
            }
        }
        finally
        {
            IsLoading = false;
        }
    }
}
