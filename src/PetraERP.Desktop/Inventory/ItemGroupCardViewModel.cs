using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;
using PetraERP.Desktop.Accounting;
using PetraERP.Desktop.Controls;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Inventory;

public sealed partial class ItemGroupCardViewModel : EntityCardViewModel
{
    private readonly IInventoryClient inventoryClient;
    private readonly IChartOfAccountsClient accountsClient;
    private readonly ShellNavigator navigator;
    private readonly IUserConfirmation confirmation;
    private Guid? pendingParentId;

    [ObservableProperty] private Guid? groupId;
    [ObservableProperty] private ItemGroupDetailDto? groupDetail;

    // --- Draft fields ---
    [ObservableProperty] private string draftCode = string.Empty;
    [ObservableProperty] private string draftNameArabic = string.Empty;
    [ObservableProperty] private string? draftNameEnglish;
    [ObservableProperty] private Guid? draftParentId;
    [ObservableProperty] private string? draftDescription;
    [ObservableProperty] private bool draftIsActive = true;

    // --- Mapped Accounts ---
    [ObservableProperty] private Guid? revenueAccountId;
    [ObservableProperty] private Guid? inventoryAccountId;
    [ObservableProperty] private Guid? cogsAccountId;
    [ObservableProperty] private Guid? purchaseAccountId;
    [ObservableProperty] private Guid? salesDiscountAccountId;
    [ObservableProperty] private Guid? purchaseDiscountAccountId;
    [ObservableProperty] private Guid? salesTaxAccountId;
    [ObservableProperty] private Guid? purchaseTaxAccountId;

    // --- Collections ---
    public ObservableCollection<LookupItem> ParentGroupLookups { get; } = [];
    public ObservableCollection<LookupItem> AccountLookups { get; } = [];
    public ObservableCollection<ItemGroupDto> Subgroups { get; } = [];
    public ObservableCollection<ItemSummaryItem> ItemsInGroup { get; } = [];

    public ItemGroupCardViewModel(
        IInventoryClient inventoryClient,
        IChartOfAccountsClient accountsClient,
        ShellNavigator navigator,
        DesktopOperation operation,
        ILocalizationService localization,
        IUserConfirmation confirmation)
        : base(operation, localization, confirmation)
    {
        this.inventoryClient = inventoryClient;
        this.accountsClient = accountsClient;
        this.navigator = navigator;
        this.confirmation = confirmation;

        RefreshCommand = new AsyncRelayCommand(
            () => IsViewMode && GroupId.HasValue ? RunAsync(ct => LoadExistingGroupAsync(GroupId.Value, ct)) : Task.CompletedTask,
            () => HasContext && Idle && GroupId.HasValue && IsViewMode);

        DeactivateCommand = new AsyncRelayCommand(DeactivateAsync, () => HasContext && Idle && CanManage && GroupId.HasValue && DraftIsActive && IsViewMode);
        ReactivateCommand = new AsyncRelayCommand(ReactivateAsync, () => HasContext && Idle && CanManage && GroupId.HasValue && !DraftIsActive && IsViewMode);
        DeleteCommand = new AsyncRelayCommand(DeleteAsync, () => HasContext && Idle && CanManage && GroupId.HasValue && IsViewMode);
    }

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand DeactivateCommand { get; }
    public AsyncRelayCommand ReactivateCommand { get; }
    public AsyncRelayCommand DeleteCommand { get; }

    protected override string RequiredPermission => ItemsViewModel.ViewPermission;
    public bool CanManage => Can(ItemsViewModel.ManagePermission);
    public bool IsCodeReadOnly => !IsNewMode;

    public string Title => IsNewMode
        ? Localization["D_NewItemGroup"]
        : GroupDetail is not null
            ? $"{GroupDetail.Code} — {GroupDetail.NameArabic}"
            : Localization["D_ItemGroupCard"];

    public string StatusText => DraftIsActive ? Localization["D_Active"] : Localization["D_Inactive"];

    // Notifications for draft tracking
    partial void OnDraftCodeChanged(string value) => DraftChanged();
    partial void OnDraftNameArabicChanged(string value) => DraftChanged();
    partial void OnDraftNameEnglishChanged(string? value) => DraftChanged();
    partial void OnDraftParentIdChanged(Guid? value) => DraftChanged();
    partial void OnDraftDescriptionChanged(string? value) => DraftChanged();
    partial void OnRevenueAccountIdChanged(Guid? value) => DraftChanged();
    partial void OnInventoryAccountIdChanged(Guid? value) => DraftChanged();
    partial void OnCogsAccountIdChanged(Guid? value) => DraftChanged();
    partial void OnPurchaseAccountIdChanged(Guid? value) => DraftChanged();
    partial void OnSalesDiscountAccountIdChanged(Guid? value) => DraftChanged();
    partial void OnPurchaseDiscountAccountIdChanged(Guid? value) => DraftChanged();
    partial void OnSalesTaxAccountIdChanged(Guid? value) => DraftChanged();
    partial void OnPurchaseTaxAccountIdChanged(Guid? value) => DraftChanged();

    protected override bool CanEditRecord => CanManage && GroupId.HasValue;
    protected override bool CanCreateRecord => CanManage;

    public override bool CanSaveDraft =>
        !string.IsNullOrWhiteSpace(DraftCode) &&
        !string.IsNullOrWhiteSpace(DraftNameArabic) &&
        (!GroupId.HasValue || !DraftParentId.HasValue || DraftParentId.Value != GroupId.Value);

    public override IReadOnlyList<string> GetValidationErrors()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(DraftCode))
            errors.Add(Localization["D_Code"] + ": " + Localization["D_RequiredField"]);
        if (string.IsNullOrWhiteSpace(DraftNameArabic))
            errors.Add(Localization["D_NameArabic"] + ": " + Localization["D_RequiredField"]);
        if (GroupId.HasValue && DraftParentId.HasValue && DraftParentId.Value == GroupId.Value)
            errors.Add(Localization["D_HierarchyCycle"]);
        return errors;
    }

    protected override string? GetValidationFocusTarget() => string.IsNullOrWhiteSpace(DraftCode)
        ? "code"
        : string.IsNullOrWhiteSpace(DraftNameArabic) ? "name-ar"
        : (GroupId.HasValue && DraftParentId.HasValue && DraftParentId.Value == GroupId.Value) ? "parent" : null;

    protected override bool HasDraftChanges
    {
        get
        {
            if (IsNewMode)
            {
                return !string.IsNullOrWhiteSpace(DraftCode) ||
                       !string.IsNullOrWhiteSpace(DraftNameArabic) ||
                       DraftParentId.HasValue;
            }

            if (GroupDetail is null) return false;

            return DraftNameArabic != GroupDetail.NameArabic ||
                   DraftNameEnglish != GroupDetail.NameEnglish ||
                   DraftParentId != GroupDetail.ParentId ||
                   DraftDescription != GroupDetail.Description;
        }
    }

    protected override void LoadDraft(EntityCardMode mode)
    {
        if (mode == EntityCardMode.New)
        {
            DraftCode = string.Empty;
            DraftNameArabic = string.Empty;
            DraftNameEnglish = null;
            DraftParentId = pendingParentId;
            DraftDescription = null;
            DraftIsActive = true;
            RevenueAccountId = null;
            InventoryAccountId = null;
            CogsAccountId = null;
            PurchaseAccountId = null;
            SalesDiscountAccountId = null;
            PurchaseDiscountAccountId = null;
            SalesTaxAccountId = null;
            PurchaseTaxAccountId = null;
            Subgroups.Clear();
            ItemsInGroup.Clear();
        }
        else if (GroupDetail is not null)
        {
            DraftCode = GroupDetail.Code ?? string.Empty;
            DraftNameArabic = GroupDetail.NameArabic ?? string.Empty;
            DraftNameEnglish = GroupDetail.NameEnglish;
            DraftParentId = GroupDetail.ParentId;
            DraftDescription = GroupDetail.Description;
            DraftIsActive = GroupDetail.IsActive ?? true;
        }

        DraftChanged();
    }

    protected override async Task<bool> SaveDraftAsync(EntityCardMode mode)
    {
        if (!HasContext) return false;

        if (mode == EntityCardMode.New)
        {
            var req = new CreateItemGroupRequest
            {
                Code = DraftCode.Trim(),
                NameArabic = DraftNameArabic.Trim(),
                NameEnglish = string.IsNullOrWhiteSpace(DraftNameEnglish) ? null : DraftNameEnglish.Trim(),
                ParentId = DraftParentId,
                Description = string.IsNullOrWhiteSpace(DraftDescription) ? null : DraftDescription.Trim()
            };

            var created = await inventoryClient.CreateItemGroupAsync(CompanyId, BranchId, req, Token).ConfigureAwait(true);
            GroupId = created.Id;
        }
        else if (GroupId.HasValue)
        {
            var req = new UpdateItemGroupRequest
            {
                NameArabic = DraftNameArabic.Trim(),
                NameEnglish = string.IsNullOrWhiteSpace(DraftNameEnglish) ? null : DraftNameEnglish.Trim(),
                ParentId = DraftParentId,
                Description = string.IsNullOrWhiteSpace(DraftDescription) ? null : DraftDescription.Trim()
            };

            await inventoryClient.UpdateItemGroupAsync(CompanyId, BranchId, GroupId.Value, req, Token).ConfigureAwait(true);
        }

        if (GroupId.HasValue)
        {
            var mappings = new List<SetItemAccountMappingRequest>();
            void AddMap(string role, Guid? id) { if (id.HasValue && id.Value != Guid.Empty) mappings.Add(new SetItemAccountMappingRequest { Role = role, AccountId = id.Value }); }

            AddMap("Revenue", RevenueAccountId);
            AddMap("Inventory", InventoryAccountId);
            AddMap("COGS", CogsAccountId);
            AddMap("Purchase", PurchaseAccountId);
            AddMap("SalesDiscount", SalesDiscountAccountId);
            AddMap("PurchaseDiscount", PurchaseDiscountAccountId);
            AddMap("SalesTax", SalesTaxAccountId);
            AddMap("PurchaseTax", PurchaseTaxAccountId);

            await inventoryClient.SetAccountMappingsAsync(CompanyId, BranchId, new SetAccountMappingsRequest
            {
                OwnerType = 2,
                OwnerId = GroupId.Value,
                Mappings = mappings
            }, Token).ConfigureAwait(true);
        }

        ResetMode();
        if (GroupId.HasValue)
        {
            await LoadExistingGroupAsync(GroupId.Value, Token).ConfigureAwait(true);
        }
        return true;
    }

    protected override async Task LoadAsync(object? parameter, CancellationToken cancellationToken)
    {
        NotifyCardCommands();

        if (parameter is OpenItemGroupCardRequest open)
        {
            GroupId = open.GroupId;
            ResetMode();
            await LoadLookupsAsync(cancellationToken).ConfigureAwait(true);
            await LoadExistingGroupAsync(open.GroupId, cancellationToken).ConfigureAwait(true);
        }
        else if (parameter is NewItemGroupCardRequest newReq)
        {
            GroupId = null;
            pendingParentId = newReq.ParentId;
            GroupDetail = null;
            await LoadLookupsAsync(cancellationToken).ConfigureAwait(true);
            EnterNew();
        }
    }

    private void EnterNew()
    {
        LoadDraft(EntityCardMode.New);
        var prop = typeof(EntityCardViewModel).GetProperty(nameof(Mode));
        prop?.SetValue(this, EntityCardMode.New);
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(StatusText));
        NotifyCardCommands();
    }

    protected override void ClearPage()
    {
        GroupId = null;
        GroupDetail = null;
        pendingParentId = null;
        ClearDraft();
        ResetMode();
    }

    private async Task LoadExistingGroupAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var detail = await inventoryClient.GetItemGroupAsync(CompanyId, BranchId, id, cancellationToken).ConfigureAwait(true);
            GroupDetail = detail;
            DraftCode = detail.Code ?? string.Empty;
            DraftNameArabic = detail.NameArabic ?? string.Empty;
            DraftNameEnglish = detail.NameEnglish;
            DraftParentId = detail.ParentId;
            DraftDescription = detail.Description;
            DraftIsActive = detail.IsActive ?? true;

            // Load Subgroups & Items
            var allGroups = await inventoryClient.ListItemGroupsAsync(CompanyId, BranchId, cancellationToken).ConfigureAwait(true);
            Subgroups.Clear();
            foreach (var g in allGroups.Where(x => x.ParentId == id))
            {
                Subgroups.Add(g);
            }

            var allItems = await inventoryClient.ListItemsAsync(CompanyId, BranchId, page: 1, pageSize: 200, search: null, groupId: id, warehouseId: null, isActive: null, cancellationToken: cancellationToken).ConfigureAwait(true);
            ItemsInGroup.Clear();
            if (allItems.Items is not null)
            {
                foreach (var itm in allItems.Items)
                {
                    ItemsInGroup.Add(new ItemSummaryItem
                    {
                        Id = itm.Id ?? Guid.Empty,
                        Code = itm.Code ?? string.Empty,
                        NameArabic = itm.NameArabic ?? string.Empty,
                        NameEnglish = itm.NameEnglish,
                        GroupId = itm.GroupId,
                        GroupName = itm.GroupName,
                        BaseUnitId = itm.BaseUnitId,
                        BaseUnitCode = itm.BaseUnitCode,
                        BaseUnitSymbol = itm.BaseUnitSymbol,
                        CostMethod = itm.CostMethod ?? CostMethod.WeightedAverage,
                        MinStock = itm.MinStock,
                        MaxStock = itm.MaxStock,
                        ReorderPoint = itm.ReorderPoint,
                        CurrentStock = itm.CurrentStock ?? "0.0000",
                        IsActive = itm.IsActive ?? true,
                    });
                }
            }

            // Load Mappings
            var mappings = await inventoryClient.GetAccountMappingsAsync(CompanyId, BranchId, 2, id, cancellationToken).ConfigureAwait(true);
            RevenueAccountId = mappings.FirstOrDefault(m => string.Equals(m.Role, "Revenue", StringComparison.OrdinalIgnoreCase))?.AccountId;
            InventoryAccountId = mappings.FirstOrDefault(m => string.Equals(m.Role, "Inventory", StringComparison.OrdinalIgnoreCase))?.AccountId;
            CogsAccountId = mappings.FirstOrDefault(m => string.Equals(m.Role, "COGS", StringComparison.OrdinalIgnoreCase))?.AccountId;
            PurchaseAccountId = mappings.FirstOrDefault(m => string.Equals(m.Role, "Purchase", StringComparison.OrdinalIgnoreCase))?.AccountId;
            SalesDiscountAccountId = mappings.FirstOrDefault(m => string.Equals(m.Role, "SalesDiscount", StringComparison.OrdinalIgnoreCase))?.AccountId;
            PurchaseDiscountAccountId = mappings.FirstOrDefault(m => string.Equals(m.Role, "PurchaseDiscount", StringComparison.OrdinalIgnoreCase))?.AccountId;
            SalesTaxAccountId = mappings.FirstOrDefault(m => string.Equals(m.Role, "SalesTax", StringComparison.OrdinalIgnoreCase))?.AccountId;
            PurchaseTaxAccountId = mappings.FirstOrDefault(m => string.Equals(m.Role, "PurchaseTax", StringComparison.OrdinalIgnoreCase))?.AccountId;

            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(StatusText));
            NotifyCardCommands();
        }
        catch (Exception ex)
        {
            Operation.SetError(Connection.DesktopErrors.Key(ex));
        }
    }

    public async Task LoadLookupsAsync(CancellationToken cancellationToken)
    {
        if (!HasContext) return;

        try
        {
            var groups = await inventoryClient.ListItemGroupsAsync(CompanyId, BranchId, cancellationToken).ConfigureAwait(true);
            ParentGroupLookups.Clear();
            foreach (var g in groups.Where(x => x.Id.HasValue && (!GroupId.HasValue || x.Id.Value != GroupId.Value)))
            {
                ParentGroupLookups.Add(new LookupItem(g.Id!.Value, g.Code ?? string.Empty, g.NameArabic ?? string.Empty, g.NameEnglish));
            }

            var accounts = await accountsClient.GetAccountTreeAsync(CompanyId, BranchId, cancellationToken).ConfigureAwait(true);
            AccountLookups.Clear();
            foreach (var a in accounts.Where(x => x.PostingAllowed && x.IsActive))
            {
                AccountLookups.Add(new LookupItem(a.Id, a.Code, a.NameArabic, a.NameEnglish));
            }
        }
        catch
        {
            // Graceful fallback
        }
    }

    private async Task DeactivateAsync()
    {
        if (!GroupId.HasValue) return;
        await RunAsync(async token =>
        {
            await inventoryClient.DeactivateItemGroupAsync(CompanyId, BranchId, GroupId.Value, token).ConfigureAwait(true);
            DraftIsActive = false;
            OnPropertyChanged(nameof(StatusText));
            NotifyCommands();
        }).ConfigureAwait(true);
    }

    private async Task ReactivateAsync()
    {
        if (!GroupId.HasValue) return;
        await RunAsync(async token =>
        {
            await inventoryClient.ReactivateItemGroupAsync(CompanyId, BranchId, GroupId.Value, token).ConfigureAwait(true);
            DraftIsActive = true;
            OnPropertyChanged(nameof(StatusText));
            NotifyCommands();
        }).ConfigureAwait(true);
    }

    private async Task DeleteAsync()
    {
        if (!GroupId.HasValue) return;
        var confirmed = await confirmation.ConfirmAsync("D_DeleteItemGroupPrompt").ConfigureAwait(true);
        if (!confirmed) return;

        await RunAsync(async token =>
        {
            await inventoryClient.DeleteItemGroupAsync(CompanyId, BranchId, GroupId.Value, token).ConfigureAwait(true);
            await navigator.NavigateAsync("items").ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    private void ClearDraft()
    {
        DraftCode = string.Empty;
        DraftNameArabic = string.Empty;
        DraftNameEnglish = null;
        DraftParentId = null;
        DraftDescription = null;
        DraftIsActive = true;
        RevenueAccountId = null;
        InventoryAccountId = null;
        CogsAccountId = null;
        PurchaseAccountId = null;
        SalesDiscountAccountId = null;
        PurchaseDiscountAccountId = null;
        SalesTaxAccountId = null;
        PurchaseTaxAccountId = null;
        Subgroups.Clear();
        ItemsInGroup.Clear();
    }

    protected override void NotifyCommands()
    {
        base.NotifyCommands();
        NotifyNavigation();
        RefreshCommand?.NotifyCanExecuteChanged();
        DeactivateCommand?.NotifyCanExecuteChanged();
        ReactivateCommand?.NotifyCanExecuteChanged();
        DeleteCommand?.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(IsCodeReadOnly));
        NotifyCardCommands();
    }
}

public sealed class ItemGroupCardFactory(
    IInventoryClient inventoryClient,
    IChartOfAccountsClient accountsClient,
    ShellNavigator navigator,
    DesktopOperation operation,
    ILocalizationService localization,
    IUserConfirmation confirmation)
{
    public ItemGroupCardViewModel Create() => new(inventoryClient, accountsClient, navigator, operation, localization, confirmation);
}
