using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;
using PetraERP.Desktop.Accounting;
using PetraERP.Desktop.Controls;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Inventory;

/// <summary>
/// Units and conversions. Classifying a unit (mass, volume, count …) lets the server enforce whole pieces and refuse
/// company-wide conversions across dimensions; a density such as m³ → ton is entered only as an item-specific conversion.
/// Item-specific conversions always win over company-wide ones. Every rule is enforced by the server.
/// </summary>
public sealed partial class UnitsViewModel : PageViewModel
{
    public const string ViewPermission = "Inventory.Items.View";
    public const string ManagePermission = "Inventory.Items.Manage";

    private readonly IUnitsClient client;
    private readonly NotificationCenter notifications;

    [ObservableProperty] private UnitOfMeasureDto? selectedUnit;
    [ObservableProperty] private string newUnitCode = string.Empty;
    [ObservableProperty] private string newUnitNameArabic = string.Empty;
    [ObservableProperty] private string? newUnitNameEnglish;
    [ObservableProperty] private string newUnitSymbol = string.Empty;
    [ObservableProperty] private int newUnitDecimalPlaces;
    [ObservableProperty] private UnitDimension newUnitDimension = UnitDimension.Unspecified;
    [ObservableProperty] private UnitDimension selectedUnitDimension = UnitDimension.Unspecified;

    [ObservableProperty] private Guid? conversionItemId;
    [ObservableProperty] private Guid? conversionFromUnitId;
    [ObservableProperty] private Guid? conversionToUnitId;
    [ObservableProperty] private string conversionFactor = string.Empty;
    [ObservableProperty] private string itemSearch = string.Empty;

    public UnitsViewModel(IUnitsClient client, NotificationCenter notifications, DesktopOperation operation, ILocalizationService localization)
        : base(operation, localization)
    {
        this.client = client;
        this.notifications = notifications;
        RefreshCommand = new AsyncRelayCommand(() => RunAsync(LoadAllAsync), () => HasContext && Idle);
        CreateUnitCommand = new AsyncRelayCommand(CreateUnitAsync, () => HasContext && Idle && CanManage && !string.IsNullOrWhiteSpace(NewUnitCode)
            && !string.IsNullOrWhiteSpace(NewUnitNameArabic) && !string.IsNullOrWhiteSpace(NewUnitSymbol) && NewUnitDecimalPlaces is >= 0 and <= 4);
        ClassifyUnitCommand = new AsyncRelayCommand(ClassifyUnitAsync, () => HasContext && Idle && CanManage && SelectedUnit?.Id is not null
            && SelectedUnit.Dimension != SelectedUnitDimension);
        CreateConversionCommand = new AsyncRelayCommand(CreateConversionAsync, () => HasContext && Idle && CanManage && RequiredInput.Selection(ConversionFromUnitId)
            && RequiredInput.Selection(ConversionToUnitId) && ConversionFromUnitId != ConversionToUnitId && RequiredInput.AmountText(ConversionFactor, 6, true));
        SearchItemsCommand = new AsyncRelayCommand(() => RunAsync(SearchItemsAsync), () => HasContext && Idle);
    }

    public ObservableCollection<UnitOfMeasureDto> Units { get; } = [];
    public ObservableCollection<UnitConversionDto> Conversions { get; } = [];
    public ObservableCollection<ItemLookupDto> ItemLookups { get; } = [];
    public IReadOnlyList<ChoiceOption<UnitDimension>> DimensionOptions => Options.For<UnitDimension>(Localization, "UnitDimension_");

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand CreateUnitCommand { get; }
    public AsyncRelayCommand ClassifyUnitCommand { get; }
    public AsyncRelayCommand CreateConversionCommand { get; }
    public AsyncRelayCommand SearchItemsCommand { get; }
    public IReadOnlyList<int> DecimalOptions { get; } = [0, 1, 2, 3, 4];
    public bool CanManage => Can(ManagePermission);

    protected override string RequiredPermission => ViewPermission;

    protected override Task LoadAsync(object? parameter, CancellationToken cancellationToken) => RunAsync(LoadAllAsync);

    protected override void ClearPage()
    {
        Units.Clear(); Conversions.Clear(); ItemLookups.Clear(); SelectedUnit = null;
        NewUnitCode = string.Empty; NewUnitNameArabic = string.Empty; NewUnitNameEnglish = null; NewUnitSymbol = string.Empty;
        NewUnitDecimalPlaces = 0; NewUnitDimension = UnitDimension.Unspecified;
        ConversionItemId = null; ConversionFromUnitId = null; ConversionToUnitId = null; ConversionFactor = string.Empty;
        ItemSearch = string.Empty;
    }

    private async Task LoadAllAsync(CancellationToken cancellationToken)
    {
        var units = await client.ListUnitsAsync(CompanyId, BranchId, cancellationToken).ConfigureAwait(true);
        Units.Clear();
        foreach (var unit in units) Units.Add(unit);
        var conversions = await client.ListConversionsAsync(CompanyId, BranchId, null, cancellationToken).ConfigureAwait(true);
        Conversions.Clear();
        foreach (var conversion in conversions) Conversions.Add(conversion);
        await SearchItemsAsync(cancellationToken).ConfigureAwait(true);
    }

    private async Task SearchItemsAsync(CancellationToken cancellationToken)
    {
        var items = await client.LookupItemsAsync(CompanyId, BranchId, ItemSearch, cancellationToken).ConfigureAwait(true);
        ItemLookups.Clear();
        foreach (var item in items) ItemLookups.Add(item);
    }

    private async Task CreateUnitAsync()
    {
        var request = new CreateUnitRequest
        {
            Code = NewUnitCode.Trim(), NameArabic = NewUnitNameArabic.Trim(),
            NameEnglish = string.IsNullOrWhiteSpace(NewUnitNameEnglish) ? null : NewUnitNameEnglish.Trim(),
            Symbol = NewUnitSymbol.Trim(), DecimalPlaces = NewUnitDecimalPlaces, Dimension = NewUnitDimension,
        };
        if (await RunAsync(async token => { await client.CreateUnitAsync(CompanyId, BranchId, request, token).ConfigureAwait(true); await LoadAllAsync(token).ConfigureAwait(true); })
                .ConfigureAwait(true))
        {
            NewUnitCode = string.Empty; NewUnitNameArabic = string.Empty; NewUnitNameEnglish = null; NewUnitSymbol = string.Empty;
            notifications.Success("D_Saved");
        }
    }

    private async Task ClassifyUnitAsync()
    {
        if (SelectedUnit is not { Id: { } unitId } unit) return;
        var request = new UpdateUnitRequest
        {
            NameArabic = unit.NameArabic ?? string.Empty, NameEnglish = unit.NameEnglish, Symbol = unit.Symbol ?? string.Empty,
            DecimalPlaces = unit.DecimalPlaces ?? 0, Dimension = SelectedUnitDimension,
        };
        if (await RunAsync(async token => { await client.UpdateUnitAsync(CompanyId, BranchId, unitId, request, token).ConfigureAwait(true); await LoadAllAsync(token).ConfigureAwait(true); })
                .ConfigureAwait(true))
            notifications.Success("D_Saved");
    }

    private async Task CreateConversionAsync()
    {
        var request = new CreateUnitConversionRequest
        {
            ItemId = ConversionItemId, FromUnitId = ConversionFromUnitId, ToUnitId = ConversionToUnitId, Factor = ConversionFactor.Trim(),
        };
        if (await RunAsync(async token => { await client.CreateConversionAsync(CompanyId, BranchId, request, token).ConfigureAwait(true); await LoadAllAsync(token).ConfigureAwait(true); })
                .ConfigureAwait(true))
        {
            ConversionFactor = string.Empty;
            notifications.Success("D_Saved");
        }
    }

    public void Localize() => OnPropertyChanged(nameof(DimensionOptions));

    partial void OnSelectedUnitChanged(UnitOfMeasureDto? value) { SelectedUnitDimension = value?.Dimension ?? UnitDimension.Unspecified; NotifyCommands(); }
    partial void OnSelectedUnitDimensionChanged(UnitDimension value) => NotifyCommands();
    partial void OnNewUnitCodeChanged(string value) => NotifyCommands();
    partial void OnNewUnitNameArabicChanged(string value) => NotifyCommands();
    partial void OnNewUnitSymbolChanged(string value) => NotifyCommands();
    partial void OnNewUnitDecimalPlacesChanged(int value) => NotifyCommands();
    partial void OnConversionFromUnitIdChanged(Guid? value) => NotifyCommands();
    partial void OnConversionToUnitIdChanged(Guid? value) => NotifyCommands();
    partial void OnConversionFactorChanged(string value) => NotifyCommands();

    protected override void NotifyCommands()
    {
        OnPropertyChanged(nameof(CanManage));
        RefreshCommand?.NotifyCanExecuteChanged();
        CreateUnitCommand?.NotifyCanExecuteChanged();
        ClassifyUnitCommand?.NotifyCanExecuteChanged();
        CreateConversionCommand?.NotifyCanExecuteChanged();
        SearchItemsCommand?.NotifyCanExecuteChanged();
    }
}
