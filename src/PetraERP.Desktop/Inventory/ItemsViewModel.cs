using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Inventory;

public sealed partial class ItemGroupTreeNode : ObservableObject
{
    public Guid? Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string NameArabic { get; init; } = string.Empty;
    public string? NameEnglish { get; init; }
    public int MaterialsCount { get; set; }
    public bool IsActive { get; set; } = true;
    public ObservableCollection<ItemGroupTreeNode> Children { get; } = [];
    [ObservableProperty] private bool isExpanded = true;
    [ObservableProperty] private bool isSelected;

    public string DisplayText => string.IsNullOrWhiteSpace(Code)
        ? $"{NameArabic} ({MaterialsCount})"
        : $"{Code} - {NameArabic} ({MaterialsCount})";
}

public sealed partial class ItemSummaryItem : ObservableObject
{
    public Guid Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string NameArabic { get; init; } = string.Empty;
    public string? NameEnglish { get; init; }
    public Guid? GroupId { get; init; }
    public string? GroupName { get; init; }
    public Guid? BaseUnitId { get; init; }
    public string? BaseUnitCode { get; init; }
    public string? BaseUnitSymbol { get; init; }
    public Guid? DefaultWarehouseId { get; init; }
    public CostMethod CostMethod { get; init; }
    public string? MinStock { get; init; }
    public string? MaxStock { get; init; }
    public string? ReorderPoint { get; init; }
    public string CurrentStock { get; set; } = "0.0000";
    public bool IsActive { get; set; }

    public string CostMethodDisplay => CostMethod switch
    {
        CostMethod.Fifo => "الوارد أولاً صادر أولاً",
        _ => "متوسط مرجح",
    };

    public string StatusDisplay => IsActive ? "نشط" : "معطل";
}

public sealed partial class ItemsViewModel : PageViewModel, IShellPage
{
    public const string ViewPermission = "Inventory.Items.View";
    public const string ManagePermission = "Inventory.Items.Manage";

    private readonly IInventoryClient inventoryClient;
    private readonly ShellNavigator navigator;
    private readonly IUserConfirmation confirmation;

    [ObservableProperty] private string search = string.Empty;
    [ObservableProperty] private Guid? selectedGroupId;
    [ObservableProperty] private Guid? selectedWarehouseId;
    [ObservableProperty] private bool activeOnly;
    [ObservableProperty] private ItemSummaryItem? selectedItem;
    [ObservableProperty] private ItemGroupTreeNode? selectedGroupNode;
    [ObservableProperty] private int totalCount;

    public ItemsViewModel(
        IInventoryClient inventoryClient,
        ShellNavigator navigator,
        DesktopOperation operation,
        ILocalizationService localization,
        IUserConfirmation confirmation)
        : base(operation, localization)
    {
        this.inventoryClient = inventoryClient;
        this.navigator = navigator;
        this.confirmation = confirmation;

        RefreshCommand = new AsyncRelayCommand(LoadItemsAsync, () => HasContext && Idle);
        OpenCardCommand = new AsyncRelayCommand(OpenSelectedCardAsync, () => SelectedItem is not null && Idle);
        NewItemCommand = new AsyncRelayCommand(NewItemAsync, () => HasContext && Idle && CanManage);
        ToggleActiveCommand = new AsyncRelayCommand(ToggleSelectedActiveAsync, () => SelectedItem is not null && Idle && CanManage);

        NewGroupCommand = new AsyncRelayCommand(NewGroupAsync, () => HasContext && Idle && CanManage);
        NewSubgroupCommand = new AsyncRelayCommand(NewSubgroupAsync, () => HasContext && Idle && CanManage);
        OpenGroupCardCommand = new AsyncRelayCommand(OpenGroupCardAsync, () => SelectedGroupNode?.Id.HasValue == true && Idle);
        AddItemToGroupCommand = new AsyncRelayCommand(AddItemToGroupAsync, () => HasContext && Idle && CanManage);
    }

    protected override string RequiredPermission => ViewPermission;
    public bool CanManage => Can(ManagePermission);

    public ObservableCollection<ItemSummaryItem> Items { get; } = [];
    public ObservableCollection<ItemGroupDto> Groups { get; } = [];
    public ObservableCollection<ItemGroupTreeNode> GroupTree { get; } = [];
    public ObservableCollection<WarehouseDto> Warehouses { get; } = [];

    public IReadOnlyList<ItemSummaryItem> VisibleItems => Items
        .Where(MatchesFilter)
        .ToList();

    public int TotalItemsCount => Items.Count;
    public int TotalGroupsCount => Groups.Count;
    public string CurrentGroupName => SelectedGroupNode?.NameArabic ?? Localization["D_AllMaterials"];
    public int ActiveItemsCount => Items.Count(i => i.IsActive);
    public int InactiveItemsCount => Items.Count(i => !i.IsActive);

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand OpenCardCommand { get; }
    public AsyncRelayCommand NewItemCommand { get; }
    public AsyncRelayCommand ToggleActiveCommand { get; }
    public AsyncRelayCommand NewGroupCommand { get; }
    public AsyncRelayCommand NewSubgroupCommand { get; }
    public AsyncRelayCommand OpenGroupCardCommand { get; }
    public AsyncRelayCommand AddItemToGroupCommand { get; }

    protected override Task LoadAsync(object? parameter, CancellationToken cancellationToken)
    {
        NotifyPermissions();
        return LoadItemsCoreAsync(cancellationToken);
    }

    protected override void ClearPage()
    {
        Items.Clear();
        Groups.Clear();
        GroupTree.Clear();
        Warehouses.Clear();
        SelectedItem = null;
        SelectedGroupNode = null;
        Search = string.Empty;
        SelectedGroupId = null;
        SelectedWarehouseId = null;
        ActiveOnly = false;
        TotalCount = 0;
        NotifyCounters();
        OnPropertyChanged(nameof(VisibleItems));
    }

    public void Localize()
    {
        OnPropertyChanged(nameof(VisibleItems));
        NotifyCounters();
        NotifyCommands();
    }

    private void NotifyCounters()
    {
        OnPropertyChanged(nameof(TotalItemsCount));
        OnPropertyChanged(nameof(TotalGroupsCount));
        OnPropertyChanged(nameof(CurrentGroupName));
        OnPropertyChanged(nameof(ActiveItemsCount));
        OnPropertyChanged(nameof(InactiveItemsCount));
    }

    partial void OnSearchChanged(string value) => RunSearch();
    partial void OnSelectedGroupIdChanged(Guid? value) => RunSearch();
    partial void OnSelectedWarehouseIdChanged(Guid? value) => RunSearch();
    partial void OnActiveOnlyChanged(bool value) => RunSearch();
    partial void OnSelectedItemChanged(ItemSummaryItem? value) => NotifyCommands();

    partial void OnSelectedGroupNodeChanged(ItemGroupTreeNode? value)
    {
        SelectedGroupId = value?.Id;
        NotifyCounters();
        OpenGroupCardCommand?.NotifyCanExecuteChanged();
        NewSubgroupCommand?.NotifyCanExecuteChanged();
    }

    public void SelectGroupNode(ItemGroupTreeNode? node)
    {
        SelectedGroupNode = node;
    }

    private void RunSearch()
    {
        if (HasContext)
        {
            _ = LoadItemsAsync();
        }
        else
        {
            OnPropertyChanged(nameof(VisibleItems));
            NotifyCounters();
        }
    }

    protected override void NotifyCommands()
    {
        OpenCardCommand?.NotifyCanExecuteChanged();
        NewItemCommand?.NotifyCanExecuteChanged();
        ToggleActiveCommand?.NotifyCanExecuteChanged();
        NewGroupCommand?.NotifyCanExecuteChanged();
        NewSubgroupCommand?.NotifyCanExecuteChanged();
        OpenGroupCardCommand?.NotifyCanExecuteChanged();
        AddItemToGroupCommand?.NotifyCanExecuteChanged();
    }

    private void NotifyPermissions()
    {
        OnPropertyChanged(nameof(CanManage));
        NotifyCommands();
    }

    private bool MatchesFilter(ItemSummaryItem item)
    {
        if (ActiveOnly && !item.IsActive) return false;
        if (SelectedGroupId.HasValue && item.GroupId != SelectedGroupId.Value) return false;

        if (string.IsNullOrWhiteSpace(Search)) return true;

        var term = Search.Trim();
        return Controls.LookupText.Matches(term, item.Code, item.NameArabic, item.NameEnglish, item.GroupName);
    }

    public Task LoadItemsAsync() => RunAsync(LoadItemsCoreAsync);

    private async Task LoadItemsCoreAsync(CancellationToken cancellationToken)
    {
        if (!HasContext) return;

        try
        {
            var groups = await inventoryClient.ListItemGroupsAsync(CompanyId, BranchId, cancellationToken).ConfigureAwait(true);
            Groups.Clear();
            foreach (var g in groups) Groups.Add(g);

            var warehouses = await inventoryClient.ListWarehousesAsync(CompanyId, BranchId, null, cancellationToken).ConfigureAwait(true);
            Warehouses.Clear();
            foreach (var w in warehouses) Warehouses.Add(w);
        }
        catch
        {
            // Fallback gracefully
        }

        var response = await inventoryClient.ListItemsAsync(
            CompanyId,
            BranchId,
            page: 1,
            pageSize: 200,
            search: string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(),
            groupId: SelectedGroupId,
            warehouseId: SelectedWarehouseId,
            isActive: ActiveOnly ? true : null,
            cancellationToken: cancellationToken).ConfigureAwait(true);

        Items.Clear();
        if (response.Items is not null)
        {
            foreach (var item in response.Items)
            {
                Items.Add(new ItemSummaryItem
                {
                    Id = item.Id ?? Guid.Empty,
                    Code = item.Code ?? string.Empty,
                    NameArabic = item.NameArabic ?? string.Empty,
                    NameEnglish = item.NameEnglish,
                    GroupId = item.GroupId,
                    GroupName = item.GroupName,
                    BaseUnitId = item.BaseUnitId,
                    BaseUnitCode = item.BaseUnitCode,
                    BaseUnitSymbol = item.BaseUnitSymbol,
                    CostMethod = item.CostMethod ?? CostMethod.WeightedAverage,
                    MinStock = item.MinStock,
                    MaxStock = item.MaxStock,
                    ReorderPoint = item.ReorderPoint,
                    CurrentStock = item.CurrentStock ?? "0.0000",
                    IsActive = item.IsActive ?? true,
                });
            }
        }

        TotalCount = response.TotalCount ?? Items.Count;

        // Build Group Tree
        BuildGroupTree(TotalCount);

        OnPropertyChanged(nameof(VisibleItems));
        NotifyCounters();
    }

    private void BuildGroupTree(int totalMaterials)
    {
        var rootNode = new ItemGroupTreeNode
        {
            Id = null,
            Code = string.Empty,
            NameArabic = Localization["D_AllMaterials"],
            NameEnglish = "All Materials",
            MaterialsCount = totalMaterials,
            IsActive = true,
            IsExpanded = true
        };

        var groupCounts = Items.Where(i => i.GroupId.HasValue).GroupBy(i => i.GroupId!.Value).ToDictionary(g => g.Key, g => g.Count());
        var nodeMap = Groups.Where(g => g.Id.HasValue).ToDictionary(g => g.Id!.Value, g => new ItemGroupTreeNode
        {
            Id = g.Id,
            Code = g.Code ?? string.Empty,
            NameArabic = g.NameArabic ?? string.Empty,
            NameEnglish = g.NameEnglish,
            MaterialsCount = groupCounts.TryGetValue(g.Id!.Value, out var c) ? c : 0,
            IsActive = g.IsActive ?? true,
            IsExpanded = true
        });

        foreach (var g in Groups.Where(g => g.Id.HasValue))
        {
            if (g.ParentId.HasValue && nodeMap.TryGetValue(g.ParentId.Value, out var parentNode))
            {
                parentNode.Children.Add(nodeMap[g.Id!.Value]);
            }
            else if (nodeMap.TryGetValue(g.Id!.Value, out var childNode))
            {
                rootNode.Children.Add(childNode);
            }
        }

        GroupTree.Clear();
        GroupTree.Add(rootNode);

        if (SelectedGroupId.HasValue && nodeMap.TryGetValue(SelectedGroupId.Value, out var selectedNode))
        {
            selectedNode.IsSelected = true;
            SelectedGroupNode = selectedNode;
        }
        else if (SelectedGroupId is null)
        {
            rootNode.IsSelected = true;
            SelectedGroupNode = rootNode;
        }
    }

    private async Task OpenSelectedCardAsync()
    {
        if (SelectedItem is null) return;
        await navigator.NavigateAsync("item-card", new OpenItemCardRequest(SelectedItem.Id)).ConfigureAwait(true);
    }

    private async Task NewItemAsync()
    {
        await navigator.NavigateAsync("item-card", new NewItemCardRequest()).ConfigureAwait(true);
    }

    private async Task NewGroupAsync()
    {
        await navigator.NavigateAsync("item-card", new NewItemGroupCardRequest()).ConfigureAwait(true);
    }

    private async Task NewSubgroupAsync()
    {
        await navigator.NavigateAsync("item-card", new NewItemGroupCardRequest(SelectedGroupNode?.Id)).ConfigureAwait(true);
    }

    private async Task OpenGroupCardAsync()
    {
        if (SelectedGroupNode?.Id is { } gid)
        {
            await navigator.NavigateAsync("item-card", new OpenItemGroupCardRequest(gid)).ConfigureAwait(true);
        }
    }

    private async Task AddItemToGroupAsync()
    {
        await navigator.NavigateAsync("item-card", new NewItemCardRequest()).ConfigureAwait(true);
    }

    private async Task ToggleSelectedActiveAsync()
    {
        if (SelectedItem is null || !HasContext || !Idle || !CanManage) return;
        var item = SelectedItem;
        await RunAsync(async cancellationToken =>
        {
            if (item.IsActive)
            {
                await inventoryClient.DeactivateItemAsync(CompanyId, BranchId, item.Id, cancellationToken).ConfigureAwait(true);
                item.IsActive = false;
            }
            else
            {
                await inventoryClient.ReactivateItemAsync(CompanyId, BranchId, item.Id, cancellationToken).ConfigureAwait(true);
                item.IsActive = true;
            }

            await LoadItemsCoreAsync(cancellationToken).ConfigureAwait(true);
            SelectedItem = Items.FirstOrDefault(i => i.Id == item.Id) ?? item;
        }).ConfigureAwait(true);
    }
}
