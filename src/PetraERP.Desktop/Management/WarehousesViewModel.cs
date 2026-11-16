using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;
using PetraERP.Desktop.Accounting;
using PetraERP.Desktop.Currencies;
using PetraERP.Desktop.GeneralLedger;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Management;

/// <summary>Warehouse directory and card. Creation is bound to the active branch; the code is immutable after creation (server rule).</summary>
public sealed partial class WarehousesViewModel : PageViewModel
{
    public const string ViewPermission = "Inventory.Warehouses.View";
    public const string ManagePermission = "Inventory.Warehouses.Manage";

    private readonly IManagementClient client;
    private readonly NotificationCenter notifications;
    private readonly IChartOfAccountsClient accountsClient;
    private readonly IGeneralLedgerClient ledgerClient;
    private bool reloading;

    [ObservableProperty] private WarehouseDto? selectedWarehouse;
    [ObservableProperty] private string code = string.Empty;
    [ObservableProperty] private string nameArabic = string.Empty;
    [ObservableProperty] private string? nameEnglish;
    [ObservableProperty] private string? location;
    [ObservableProperty] private Guid? parentWarehouseId;
    [ObservableProperty] private Guid? inventoryAccountId;
    [ObservableProperty] private Guid? inTransitAccountId;
    [ObservableProperty] private Guid? defaultCostCenterId;

    public WarehousesViewModel(IManagementClient client, IChartOfAccountsClient accountsClient, IGeneralLedgerClient ledgerClient, NotificationCenter notifications, DesktopOperation operation, ILocalizationService localization)
        : base(operation, localization)
    {
        this.client = client;
        this.notifications = notifications;
        this.accountsClient = accountsClient;
        this.ledgerClient = ledgerClient;
        RefreshCommand = new AsyncRelayCommand(() => RunAsync(LoadAllAsync), () => HasContext && Idle);
        NewCommand = new RelayCommand(() => SelectedWarehouse = null, () => HasContext && CanManage);
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => HasContext && Idle && CanManage && !string.IsNullOrWhiteSpace(NameArabic) && (SelectedWarehouse is not null || !string.IsNullOrWhiteSpace(Code)));
    }

    public ObservableCollection<WarehouseDto> Warehouses { get; } = [];
    public ObservableCollection<AccountLookupItem> ParentOptions { get; } = [];
    public ObservableCollection<AccountLookupItem> Accounts { get; } = [];
    public ObservableCollection<AccountLookupItem> CostCenters { get; } = [];
    public AsyncRelayCommand RefreshCommand { get; }
    public RelayCommand NewCommand { get; }
    public AsyncRelayCommand SaveCommand { get; }
    public bool CanManage => Can(ManagePermission);
    public bool IsExisting => SelectedWarehouse is not null;
    public bool CanEditCode => SelectedWarehouse is null;

    protected override string RequiredPermission => ViewPermission;
    protected override Task LoadAsync(object? parameter, CancellationToken cancellationToken) => RunAsync(LoadAllAsync);

    protected override void ClearPage() { Warehouses.Clear(); SelectedWarehouse = null; Code = string.Empty; NameArabic = string.Empty; NameEnglish = null; Location = null; ParentOptions.Clear(); Accounts.Clear(); CostCenters.Clear(); ParentWarehouseId = InventoryAccountId = InTransitAccountId = DefaultCostCenterId = null; }

    protected override void NotifyCommands()
    {
        RefreshCommand?.NotifyCanExecuteChanged(); NewCommand?.NotifyCanExecuteChanged(); SaveCommand?.NotifyCanExecuteChanged(); OnPropertyChanged(nameof(CanManage));
    }

    partial void OnCodeChanged(string value) => SaveCommand.NotifyCanExecuteChanged();
    partial void OnNameArabicChanged(string value) => SaveCommand.NotifyCanExecuteChanged();

    partial void OnSelectedWarehouseChanged(WarehouseDto? value)
    {
        Code = value?.Code ?? string.Empty; NameArabic = value?.NameArabic ?? string.Empty; NameEnglish = value?.NameEnglish; Location = value?.Location;
        OnPropertyChanged(nameof(IsExisting)); OnPropertyChanged(nameof(CanEditCode)); NotifyCommands();
        ParentWarehouseId = InventoryAccountId = InTransitAccountId = DefaultCostCenterId = null;
        ParentOptions.Clear();
        foreach (var w in Warehouses.Where(w => w.Id.HasValue && w.Id != value?.Id && w.IsActive == true)) ParentOptions.Add(new(w.Id!.Value, w.Code ?? string.Empty, w.NameArabic ?? string.Empty, w.NameEnglish));
        if (value is not null && !reloading) _ = RunAsync(LoadAccountingAsync);
    }

    private async Task LoadAccountingAsync(CancellationToken token)
    {
        if (SelectedWarehouse is not { } w) return;
        var profile = await client.GetWarehouseAccountingAsync(CompanyId, BranchId, w.Id ?? Guid.Empty, token).ConfigureAwait(true);
        ParentWarehouseId = profile.ParentWarehouseId; InventoryAccountId = profile.InventoryAccountId;
        InTransitAccountId = profile.InTransitAccountId; DefaultCostCenterId = profile.DefaultCostCenterId;
    }

    private async Task LoadAllAsync(CancellationToken token)
    {
        var list = await client.ListWarehousesAsync(CompanyId, BranchId, token).ConfigureAwait(true);
        var keep = SelectedWarehouse?.Id;
        Warehouses.Clear();
        foreach (var w in list.OrderBy(w => w.Code, StringComparer.Ordinal)) Warehouses.Add(w);
        reloading = true;
        try { SelectedWarehouse = keep is null ? null : Warehouses.FirstOrDefault(w => w.Id == keep); }
        finally { reloading = false; }
        Accounts.Clear(); CostCenters.Clear();
        if (Can("Ledger.Accounts.View"))
            foreach (var a in (await accountsClient.GetAccountTreeAsync(CompanyId, BranchId, token).ConfigureAwait(true))
                         .Where(a => a.PostingAllowed == true && a.IsActive == true && a.Id != Guid.Empty).OrderBy(a => a.Code, StringComparer.Ordinal))
                Accounts.Add(new(a.Id, a.Code ?? string.Empty, a.NameArabic ?? string.Empty, a.NameEnglish));
        if (Can("Ledger.Accounts.View"))
            foreach (var c in (await ledgerClient.GetCostCentersAsync(CompanyId, BranchId, token).ConfigureAwait(true)).Where(c => c.IsActive).OrderBy(c => c.Code, StringComparer.Ordinal))
                CostCenters.Add(new(c.Id, c.Code, c.NameArabic, c.NameEnglish));
        if (SelectedWarehouse is not null) await LoadAccountingAsync(token).ConfigureAwait(true);
    }

    private async Task SaveAsync()
    {
        string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        var selected = SelectedWarehouse;
        var ok = await RunAsync(async token =>
        {
            if (selected?.Id is { } id)
            {
                // Two server operations. The heavily validated accounting profile goes first; the card (names/location) cannot fail business validation
                // beyond field rules. If the second step still fails, the screen is reloaded from the server and the user is told exactly what was saved,
                // so a partial result is never silent.
                await client.SetWarehouseAccountingAsync(CompanyId, BranchId, id, new SetWarehouseAccountingRequest
                {
                    ParentWarehouseId = ParentWarehouseId, InventoryAccountId = InventoryAccountId, InTransitAccountId = InTransitAccountId, DefaultCostCenterId = DefaultCostCenterId,
                }, token).ConfigureAwait(true);
                try
                {
                    await client.UpdateWarehouseAsync(CompanyId, BranchId, id, new UpdateWarehouseRequest { NameArabic = NameArabic.Trim(), NameEnglish = Clean(NameEnglish), Location = Clean(Location) }, token).ConfigureAwait(true);
                }
                catch (Exception) when (!token.IsCancellationRequested)
                {
                    notifications.Warning("D_WarehousePartialSave");
                    await LoadAllAsync(CancellationToken.None).ConfigureAwait(true);
                    throw;
                }
            }
            else
                await client.CreateWarehouseAsync(CompanyId, BranchId, new CreateWarehouseRequest { BranchId = BranchId, Code = Code.Trim(), NameArabic = NameArabic.Trim(), NameEnglish = Clean(NameEnglish), Location = Clean(Location) }, token).ConfigureAwait(true);
            await LoadAllAsync(token).ConfigureAwait(true);
        }, OperationKind.Save).ConfigureAwait(true);
        if (ok) notifications.Success("D_Saved");
    }
}
