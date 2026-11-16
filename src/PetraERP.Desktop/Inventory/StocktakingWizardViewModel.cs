using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;
using PetraERP.Desktop.Accounting;
using PetraERP.Desktop.Connection;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Inventory;

public enum StocktakingFilterMode
{
    All = 0,
    DiscrepanciesOnly = 1,
    SurplusOnly = 2,
    DeficitOnly = 3,
}

public sealed partial class StocktakingItemModel : ObservableObject
{
    public Guid ItemId { get; init; }
    public string ItemCode { get; init; } = string.Empty;
    public string ItemName { get; init; } = string.Empty;
    public Guid BaseUnitId { get; init; }
    public string UnitName { get; init; } = string.Empty;
    public decimal BookQuantity { get; init; }
    public decimal UnitCost { get; init; }

    [ObservableProperty] private string physicalCountText = string.Empty;
    [ObservableProperty] private decimal countedQuantity;
    [ObservableProperty] private decimal varianceQuantity;
    [ObservableProperty] private decimal varianceValue;
    [ObservableProperty] private string varianceStatus = "Matched";

    public Action? Changed { get; set; }

    partial void OnPhysicalCountTextChanged(string value)
    {
        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0)
        {
            CountedQuantity = parsed;
        }
        else
        {
            CountedQuantity = 0;
        }
        VarianceQuantity = CountedQuantity - BookQuantity;
        VarianceValue = Math.Round(VarianceQuantity * UnitCost, 4, MidpointRounding.ToEven);
        VarianceStatus = VarianceQuantity switch
        {
            > 0 => "Surplus",
            < 0 => "Deficit",
            _ => "Matched"
        };
        Changed?.Invoke();
    }
}

public sealed partial class StocktakingWizardViewModel : PageViewModel
{
    public const string ViewPermission = "Inventory.Movements.View";
    public const string ManagePermission = "Inventory.Movements.Manage";

    private readonly IInventoryDocumentsClient client;
    private readonly IInventoryClient inventoryClient;
    private readonly IUserConfirmation confirmation;
    private readonly ShellNavigator navigator;
    private int generation;

    [ObservableProperty] private Guid? selectedWarehouseId;
    [ObservableProperty] private DateTime countDate = DateTime.Today;
    [ObservableProperty] private string notes = "مطابقة الجرد الفعلي للمخزون";
    [ObservableProperty] private string searchText = string.Empty;
    [ObservableProperty] private StocktakingFilterMode filterMode = StocktakingFilterMode.All;
    [ObservableProperty] private bool isReconciled;
    [ObservableProperty] private string? reconciledDocumentNumber;

    // KPI Counters
    [ObservableProperty] private int totalCountedItems;
    [ObservableProperty] private int matchedItemsCount;
    [ObservableProperty] private int surplusItemsCount;
    [ObservableProperty] private int deficitItemsCount;
    [ObservableProperty] private decimal totalSurplusValue;
    [ObservableProperty] private decimal totalDeficitValue;
    [ObservableProperty] private decimal netVarianceValue;

    public ObservableCollection<WarehouseDto> Warehouses { get; } = [];
    public ObservableCollection<StocktakingItemModel> AllItems { get; } = [];
    public ObservableCollection<StocktakingItemModel> VisibleItems { get; } = [];

    public AsyncRelayCommand LoadBalancesCommand { get; }
    public RelayCommand SetAllCountsToBookCommand { get; }
    public AsyncRelayCommand ReconcileAndPostCommand { get; }
    public RelayCommand ResetWizardCommand { get; }

    public StocktakingWizardViewModel(
        IInventoryDocumentsClient client,
        IInventoryClient inventoryClient,
        IUserConfirmation confirmation,
        ShellNavigator navigator,
        DesktopOperation operation,
        ILocalizationService localization) : base(operation, localization)
    {
        this.client = client;
        this.inventoryClient = inventoryClient;
        this.confirmation = confirmation;
        this.navigator = navigator;

        LoadBalancesCommand = new AsyncRelayCommand(LoadBalancesAsync, () => HasContext && Idle && SelectedWarehouseId.HasValue);
        SetAllCountsToBookCommand = new RelayCommand(SetAllCountsToBook, () => HasContext && Idle && AllItems.Count > 0 && !IsReconciled);
        ReconcileAndPostCommand = new AsyncRelayCommand(ReconcileAndPostAsync, () => HasContext && Idle && CanManage && AllItems.Count > 0 && !IsReconciled);
        ResetWizardCommand = new RelayCommand(ResetWizard);
    }

    protected override string RequiredPermission => ViewPermission;
    public bool CanManage => Can(ManagePermission);

    partial void OnSelectedWarehouseIdChanged(Guid? value)
    {
        LoadBalancesCommand.NotifyCanExecuteChanged();
        if (value.HasValue)
        {
            _ = LoadBalancesAsync();
        }
    }

    partial void OnSearchTextChanged(string value) => RefreshVisibleItems();
    partial void OnFilterModeChanged(StocktakingFilterMode value) => RefreshVisibleItems();

    protected override async Task LoadAsync(object? parameter, CancellationToken cancellationToken)
    {
        var requested = ++generation;
        await LoadWarehousesAsync(cancellationToken).ConfigureAwait(true);
        if (requested != generation) return;
        NotifyCommands();
    }

    private async Task LoadWarehousesAsync(CancellationToken cancellationToken)
    {
        if (!HasContext) return;
        try
        {
            var list = await inventoryClient.ListWarehousesAsync(CompanyId, BranchId, null, cancellationToken).ConfigureAwait(true);
            Warehouses.Clear();
            foreach (var w in list) Warehouses.Add(w);
            if (SelectedWarehouseId is null && Warehouses.Count > 0)
            {
                SelectedWarehouseId = Warehouses[0].Id;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Operation.SetError(DesktopErrors.Key(exception));
        }
    }

    private async Task LoadBalancesAsync()
    {
        if (!HasContext || !SelectedWarehouseId.HasValue) return;

        try
        {
            var balances = await client.BalancesAsync(CompanyId, BranchId, SelectedWarehouseId.Value, null, CancellationToken.None).ConfigureAwait(true);
            var itemsPage = await inventoryClient.ListItemsAsync(CompanyId, BranchId, 1, 1000, null, null, null, true, CancellationToken.None).ConfigureAwait(true);
            var itemMap = (itemsPage.Items ?? []).Where(i => i.Id.HasValue).ToDictionary(i => i.Id!.Value);

            AllItems.Clear();
            foreach (var b in balances)
            {
                if (!b.ItemId.HasValue) continue;
                var itemId = b.ItemId.Value;

                var name = b.ItemCode ?? string.Empty;
                var unitId = Guid.Empty;
                var unitName = string.Empty;
                var cost = 0m;

                if (itemMap.TryGetValue(itemId, out var itemDto))
                {
                    name = itemDto.NameArabic ?? itemDto.Code ?? string.Empty;
                    unitId = itemDto.BaseUnitId ?? Guid.Empty;
                    unitName = itemDto.BaseUnitCode ?? itemDto.BaseUnitSymbol ?? string.Empty;
                }

                if (decimal.TryParse(b.Quantity, NumberStyles.Number, CultureInfo.InvariantCulture, out var bookQty))
                {
                    if (decimal.TryParse(b.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var bookVal) && bookQty > 0)
                    {
                        cost = Math.Round(bookVal / bookQty, 4, MidpointRounding.ToEven);
                    }

                    var model = new StocktakingItemModel
                    {
                        ItemId = itemId,
                        ItemCode = b.ItemCode ?? string.Empty,
                        ItemName = name,
                        BaseUnitId = unitId,
                        UnitName = unitName,
                        BookQuantity = bookQty,
                        UnitCost = cost,
                        PhysicalCountText = bookQty.ToString("F4", CultureInfo.InvariantCulture),
                        CountedQuantity = bookQty,
                        Changed = RecomputeKPIs
                    };
                    AllItems.Add(model);
                }
            }

            IsReconciled = false;
            ReconciledDocumentNumber = null;
            RecomputeKPIs();
            RefreshVisibleItems();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Operation.SetError(DesktopErrors.Key(exception));
        }
    }

    private void SetAllCountsToBook()
    {
        foreach (var item in AllItems)
        {
            item.PhysicalCountText = item.BookQuantity.ToString("F4", CultureInfo.InvariantCulture);
        }
        RecomputeKPIs();
        RefreshVisibleItems();
    }

    private void RefreshVisibleItems()
    {
        VisibleItems.Clear();
        var search = SearchText?.Trim() ?? string.Empty;

        foreach (var item in AllItems)
        {
            if (!string.IsNullOrEmpty(search))
            {
                if (!item.ItemCode.Contains(search, StringComparison.OrdinalIgnoreCase) &&
                    !item.ItemName.Contains(search, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
            }

            var matchesFilter = FilterMode switch
            {
                StocktakingFilterMode.DiscrepanciesOnly => item.VarianceQuantity != 0,
                StocktakingFilterMode.SurplusOnly => item.VarianceQuantity > 0,
                StocktakingFilterMode.DeficitOnly => item.VarianceQuantity < 0,
                _ => true
            };

            if (matchesFilter)
            {
                VisibleItems.Add(item);
            }
        }
    }

    private void RecomputeKPIs()
    {
        TotalCountedItems = AllItems.Count;
        MatchedItemsCount = AllItems.Count(i => i.VarianceQuantity == 0);
        SurplusItemsCount = AllItems.Count(i => i.VarianceQuantity > 0);
        DeficitItemsCount = AllItems.Count(i => i.VarianceQuantity < 0);

        TotalSurplusValue = AllItems.Where(i => i.VarianceValue > 0).Sum(i => i.VarianceValue);
        TotalDeficitValue = Math.Abs(AllItems.Where(i => i.VarianceValue < 0).Sum(i => i.VarianceValue));
        NetVarianceValue = TotalSurplusValue - TotalDeficitValue;

        ReconcileAndPostCommand.NotifyCanExecuteChanged();
    }

    private async Task ReconcileAndPostAsync()
    {
        if (!HasContext || !SelectedWarehouseId.HasValue || AllItems.Count == 0 || IsReconciled) return;

        var confirmed = await confirmation.ConfirmAsync("INV_ConfirmReconcile").ConfigureAwait(true);
        if (!confirmed) return;

        try
        {
            var dateUtc = DateTime.SpecifyKind(CountDate, DateTimeKind.Utc);
            var saveReq = new SaveInventoryDocumentRequest
            {
                Kind = InventoryDocumentKind.Count,
                WarehouseId = SelectedWarehouseId.Value,
                DateUtc = dateUtc,
                Reason = string.IsNullOrWhiteSpace(Notes) ? "مطابقة وتسوية الجرد الفعلي للمخزون" : Notes.Trim(),
                CreationKey = Guid.NewGuid().ToString("N"),
                Lines = AllItems.Select(item => new InventoryDocumentLineInputDto
                {
                    ItemId = item.ItemId,
                    UnitId = item.BaseUnitId,
                    Quantity = item.CountedQuantity.ToString("F4", CultureInfo.InvariantCulture)
                }).ToList()
            };

            // Save draft count document
            var doc = await client.SaveAsync(CompanyId, BranchId, null, saveReq, CancellationToken.None).ConfigureAwait(true);
            if (!doc.Id.HasValue) return;

            // Post count document to automatically record movement variance and accounting postings
            var postReq = new InventoryDocumentActionRequest
            {
                ExpectedVersion = doc.Version ?? string.Empty,
                IdempotencyKey = Guid.NewGuid().ToString("N"),
                Reason = "اعتماد وترحيل تسوية الجرد الفعلي"
            };

            var posted = await client.PostAsync(CompanyId, BranchId, doc.Id.Value, postReq, CancellationToken.None).ConfigureAwait(true);

            IsReconciled = true;
            ReconciledDocumentNumber = posted.Number;
            NotifyCommands();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Operation.SetError(DesktopErrors.Key(exception));
        }
    }

    private void ResetWizard()
    {
        IsReconciled = false;
        ReconciledDocumentNumber = null;
        AllItems.Clear();
        VisibleItems.Clear();
        TotalCountedItems = 0;
        MatchedItemsCount = 0;
        SurplusItemsCount = 0;
        DeficitItemsCount = 0;
        TotalSurplusValue = 0;
        TotalDeficitValue = 0;
        NetVarianceValue = 0;
        _ = LoadBalancesAsync();
    }

    protected override void ClearPage()
    {
        Warehouses.Clear();
        AllItems.Clear();
        VisibleItems.Clear();
        SelectedWarehouseId = null;
        IsReconciled = false;
        ReconciledDocumentNumber = null;
    }

    protected override void NotifyCommands()
    {
        base.NotifyCommands();
        LoadBalancesCommand.NotifyCanExecuteChanged();
        SetAllCountsToBookCommand.NotifyCanExecuteChanged();
        ReconcileAndPostCommand.NotifyCanExecuteChanged();
    }
}
