using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;
using PetraERP.Desktop.Accounting;
using PetraERP.Desktop.Controls;
using PetraERP.Desktop.Currencies;
using PetraERP.Desktop.Parties;
using PetraERP.Desktop.Patterns;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Shell;
using PetraERP.Desktop.Sales;

namespace PetraERP.Desktop.Inventory;

public sealed partial class ItemCardViewModel : EntityCardViewModel
{
    public SalesDocumentLinksViewModel? DocumentLinks { get; init; }
    /// <summary>Server price rows of an existing item are added/removed immediately through this client (optional seam).</summary>
    public IPricingClient? Pricing { get; init; }
    public ICurrenciesClient? Currencies { get; init; }
    public IPartiesClient? PartyLookupClient { get; init; }
    public IPatternsClient? PatternLookupClient { get; init; }
    private Guid? linkedMovementId;
    private readonly IInventoryClient inventoryClient;
    private readonly ShellNavigator navigator;
    private readonly IUserConfirmation confirmation;
    private readonly IChartOfAccountsClient? accountsClient;

    [ObservableProperty] private ItemDetailDto? item;
    [ObservableProperty] private Guid? itemId;
    [ObservableProperty] private ItemStockSummaryDto? stockSummary;

    // --- Draft fields: General ---
    [ObservableProperty] private string draftCode = string.Empty;
    [ObservableProperty] private string draftNameArabic = string.Empty;
    [ObservableProperty] private string? draftNameEnglish;
    [ObservableProperty] private Guid? draftGroupId;
    [ObservableProperty] private Guid? draftBaseUnitId;
    [ObservableProperty] private Guid? draftDefaultWarehouseId;
    [ObservableProperty] private CostMethod draftCostMethod = CostMethod.WeightedAverage;
    [ObservableProperty] private bool draftIsActive = true;
    [ObservableProperty] private string? draftMinStock;
    [ObservableProperty] private string? draftMaxStock;
    [ObservableProperty] private string? draftReorderPoint;

    // --- Draft sub-items: Barcodes ---
    [ObservableProperty] private string newBarcodeText = string.Empty;
    [ObservableProperty] private Guid? newBarcodeUnitId;
    [ObservableProperty] private bool newBarcodeIsDefault;

    // --- Draft sub-items: Prices ---
    // Currency defaults to the company base currency (a company setting), never to a hard-coded code.
    [ObservableProperty] private string newPriceListCode = "DEFAULT";
    [ObservableProperty] private string newPriceAmount = "0.0000";
    [ObservableProperty] private string newPriceCurrency = string.Empty;
    [ObservableProperty] private Guid? newPriceUnitId;
    [ObservableProperty] private string newPriceMinQuantity = "0.0000";
    [ObservableProperty] private Guid? newPricePartyId;
    [ObservableProperty] private Guid? newPricePatternId;
    [ObservableProperty] private DateTime? newPriceValidFrom;
    [ObservableProperty] private DateTime? newPriceValidTo;

    // Lookups & Collections
    public ObservableCollection<ItemGroupDto> GroupLookups { get; } = [];
    public ObservableCollection<UnitOfMeasureDto> UnitLookups { get; } = [];
    public ObservableCollection<WarehouseDto> WarehouseLookups { get; } = [];
    public ObservableCollection<PartyLookupDto> PriceCustomerLookups { get; } = [];
    public ObservableCollection<LookupItem> PricePatternLookups { get; } = [];

    // --- Accounting Overrides & Effective Mappings ---
    [ObservableProperty] private Guid? overrideRevenueAccountId;
    [ObservableProperty] private Guid? overrideInventoryAccountId;
    [ObservableProperty] private Guid? overrideCogsAccountId;
    [ObservableProperty] private Guid? overridePurchaseAccountId;
    [ObservableProperty] private Guid? overrideSalesDiscountAccountId;
    [ObservableProperty] private Guid? overridePurchaseDiscountAccountId;
    [ObservableProperty] private Guid? overrideSalesTaxAccountId;
    [ObservableProperty] private Guid? overridePurchaseTaxAccountId;

    [ObservableProperty] private string effectiveRevenueDisplay = string.Empty;
    [ObservableProperty] private string effectiveInventoryDisplay = string.Empty;
    [ObservableProperty] private string effectiveCogsDisplay = string.Empty;
    [ObservableProperty] private string effectivePurchaseDisplay = string.Empty;
    [ObservableProperty] private string effectiveSalesDiscountDisplay = string.Empty;
    [ObservableProperty] private string effectivePurchaseDiscountDisplay = string.Empty;
    [ObservableProperty] private string effectiveSalesTaxDisplay = string.Empty;
    [ObservableProperty] private string effectivePurchaseTaxDisplay = string.Empty;

    [ObservableProperty] private string revenueSourceDisplay = string.Empty;
    [ObservableProperty] private string inventorySourceDisplay = string.Empty;
    [ObservableProperty] private string cogsSourceDisplay = string.Empty;
    [ObservableProperty] private string purchaseSourceDisplay = string.Empty;
    [ObservableProperty] private string salesDiscountSourceDisplay = string.Empty;
    [ObservableProperty] private string purchaseDiscountSourceDisplay = string.Empty;
    [ObservableProperty] private string salesTaxSourceDisplay = string.Empty;
    [ObservableProperty] private string purchaseTaxSourceDisplay = string.Empty;

    public ObservableCollection<LookupItem> AccountLookups { get; } = [];

    // Card tabs collections
    public ObservableCollection<ItemBarcodeDto> Barcodes { get; } = [];
    public ObservableCollection<ItemPriceDto> Prices { get; } = [];
    public ObservableCollection<UnitConversionDto> Conversions { get; } = [];
    public ObservableCollection<WarehouseStockDto> WarehouseBalances { get; } = [];
    public ObservableCollection<StockMovementDto> Movements { get; } = [];

    public ItemCardViewModel(
        IInventoryClient inventoryClient,
        ShellNavigator navigator,
        DesktopOperation operation,
        ILocalizationService localization,
        IUserConfirmation confirmation,
        IChartOfAccountsClient? accountsClient = null)
        : base(operation, localization, confirmation)
    {
        this.inventoryClient = inventoryClient;
        this.navigator = navigator;
        this.confirmation = confirmation;
        this.accountsClient = accountsClient;

        ResetOverrideCommand = new RelayCommand<string>(ResetOverride, _ => IsEditing);

        RefreshCommand = new AsyncRelayCommand(
            () => IsViewMode ? RunAsync(ct => LoadItemAsync(ct)) : Task.CompletedTask,
            () => HasContext && Idle && ItemId is not null && IsViewMode);

        AddBarcodeCommand = new RelayCommand(AddDraftBarcode, () => IsEditing && !string.IsNullOrWhiteSpace(NewBarcodeText));
        RemoveBarcodeCommand = new RelayCommand<ItemBarcodeDto>(RemoveDraftBarcode, _ => IsEditing);
        // New item: price rows travel with the create request. Existing item: each row is added/removed on the server at once,
        // because the item update contract carries no prices (a local-only edit would be silently lost).
        AddPriceCommand = new RelayCommand(AddDraftPrice, () => CanEditPrices && !string.IsNullOrWhiteSpace(NewPriceListCode)
            && RequiredInput.Currency(NewPriceCurrency) && RequiredInput.AmountText(NewPriceAmount) && RequiredInput.AmountText(NewPriceMinQuantity));
        RemovePriceCommand = new RelayCommand<ItemPriceDto>(RemoveDraftPrice, price => CanEditPrices && price is not null);
    }

    public AsyncRelayCommand RefreshCommand { get; }
    public RelayCommand AddBarcodeCommand { get; }
    public RelayCommand<ItemBarcodeDto> RemoveBarcodeCommand { get; }
    public RelayCommand AddPriceCommand { get; }
    public RelayCommand<ItemPriceDto> RemovePriceCommand { get; }
    public RelayCommand<string> ResetOverrideCommand { get; }

    public bool HasItem => Item is not null;
    /// <summary>Draft rows while creating; immediate server rows for a saved item when the pricing seam exists.</summary>
    public bool CanEditPrices => IsNewMode || (CanManage && Item is not null && Pricing is not null && Idle);
    public bool CanManage => Can(ItemsViewModel.ManagePermission);
    protected override string RequiredPermission => ItemsViewModel.ViewPermission;
    public bool IsCodeReadOnly => !IsNewMode && (EntryOptionsHub.Current.PreventModifyingCodeWithMovements || Movements.Count > 0);

    public string Title => IsNewMode
        ? Localization["D_NewItem"]
        : Item is not null
            ? $"{Item.Code} - {Item.NameArabic}"
            : Localization["D_ItemCard"];

    public string StatusText => Item is null
        ? string.Empty
        : Item.IsActive == true ? "نشط" : "معطل";

    public string TotalStockDisplay => StockSummary is not null
        ? $"{StockSummary.TotalQuantity} {StockSummary.BaseUnitSymbol}"
        : Item is not null
            ? Item.CurrentStock
            : "0.0000";

    public IReadOnlyList<ChoiceOption<CostMethod>> CostMethodOptions =>
        Options.For<CostMethod>(Localization, "CostMethod_");

    // Notifications for draft tracking
    partial void OnDraftCodeChanged(string value) => DraftChanged();
    partial void OnDraftNameArabicChanged(string value) => DraftChanged();
    partial void OnDraftNameEnglishChanged(string? value) => DraftChanged();
    partial void OnDraftGroupIdChanged(Guid? value) => DraftChanged();
    partial void OnDraftBaseUnitIdChanged(Guid? value) => DraftChanged();
    partial void OnDraftDefaultWarehouseIdChanged(Guid? value) => DraftChanged();
    partial void OnDraftCostMethodChanged(CostMethod value) => DraftChanged();
    partial void OnDraftIsActiveChanged(bool value) => DraftChanged();
    partial void OnDraftMinStockChanged(string? value) => DraftChanged();
    partial void OnDraftMaxStockChanged(string? value) => DraftChanged();
    partial void OnDraftReorderPointChanged(string? value) => DraftChanged();
    partial void OnOverrideRevenueAccountIdChanged(Guid? value) => DraftChanged();
    partial void OnOverrideInventoryAccountIdChanged(Guid? value) => DraftChanged();
    partial void OnOverrideCogsAccountIdChanged(Guid? value) => DraftChanged();
    partial void OnOverridePurchaseAccountIdChanged(Guid? value) => DraftChanged();
    partial void OnOverrideSalesDiscountAccountIdChanged(Guid? value) => DraftChanged();
    partial void OnOverridePurchaseDiscountAccountIdChanged(Guid? value) => DraftChanged();
    partial void OnOverrideSalesTaxAccountIdChanged(Guid? value) => DraftChanged();
    partial void OnOverridePurchaseTaxAccountIdChanged(Guid? value) => DraftChanged();

    private void ResetOverride(string? role)
    {
        if (role == null) return;
        switch (role.ToUpperInvariant())
        {
            case "REVENUE": OverrideRevenueAccountId = null; break;
            case "INVENTORY": OverrideInventoryAccountId = null; break;
            case "COGS": OverrideCogsAccountId = null; break;
            case "PURCHASE": OverridePurchaseAccountId = null; break;
            case "SALESDISCOUNT": OverrideSalesDiscountAccountId = null; break;
            case "PURCHASEDISCOUNT": OverridePurchaseDiscountAccountId = null; break;
            case "SALESTAX": OverrideSalesTaxAccountId = null; break;
            case "PURCHASETAX": OverridePurchaseTaxAccountId = null; break;
        }
        DraftChanged();
    }
    partial void OnNewBarcodeTextChanged(string value) => AddBarcodeCommand.NotifyCanExecuteChanged();
    partial void OnNewPriceListCodeChanged(string value) => AddPriceCommand.NotifyCanExecuteChanged();
    partial void OnNewPriceAmountChanged(string value) => AddPriceCommand.NotifyCanExecuteChanged();
    partial void OnNewPriceCurrencyChanged(string value) => AddPriceCommand.NotifyCanExecuteChanged();
    partial void OnNewPriceMinQuantityChanged(string value) => AddPriceCommand.NotifyCanExecuteChanged();

    protected override bool CanEditRecord => CanManage && Item is not null;
    protected override bool CanCreateRecord => CanManage;

    public override bool CanSaveDraft =>
        !string.IsNullOrWhiteSpace(DraftCode) &&
        !string.IsNullOrWhiteSpace(DraftNameArabic) &&
        DraftBaseUnitId.HasValue;

    public override IReadOnlyList<string> GetValidationErrors()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(DraftCode))
            errors.Add(Localization["D_Code"] + ": " + Localization["D_RequiredField"]);
        if (string.IsNullOrWhiteSpace(DraftNameArabic))
            errors.Add(Localization["D_NameArabic"] + ": " + Localization["D_RequiredField"]);
        if (!DraftBaseUnitId.HasValue)
            errors.Add(Localization["D_BaseUnit"] + ": " + Localization["D_RequiredField"]);
        errors.AddRange(ProfileValidationErrors());
        return errors;
    }

    protected override string? GetValidationFocusTarget() => string.IsNullOrWhiteSpace(DraftCode)
        ? "code"
        : string.IsNullOrWhiteSpace(DraftNameArabic) ? "name-ar"
        : !DraftBaseUnitId.HasValue ? "base-unit" : null;

    protected override bool HasDraftChanges
    {
        get
        {
            if (IsNewMode)
            {
                return !string.IsNullOrWhiteSpace(DraftCode) ||
                       !string.IsNullOrWhiteSpace(DraftNameArabic) ||
                       DraftGroupId is not null ||
                       ProfileHasChanges();
            }

            if (Item is null) return false;

            return DraftNameArabic != Item.NameArabic ||
                   DraftNameEnglish != Item.NameEnglish ||
                   DraftGroupId != Item.GroupId ||
                   DraftBaseUnitId != Item.BaseUnitId ||
                   DraftDefaultWarehouseId != Item.DefaultWarehouseId ||
                   DraftCostMethod != (Item.CostMethod ?? CostMethod.WeightedAverage) ||
                   DraftMinStock != Item.MinStock ||
                   DraftMaxStock != Item.MaxStock ||
                   DraftReorderPoint != Item.ReorderPoint ||
                   ProfileHasChanges();
        }
    }

    protected override void LoadDraft(EntityCardMode mode)
    {
        if (mode == EntityCardMode.New)
        {
            DraftCode = string.Empty;
            DraftNameArabic = string.Empty;
            DraftNameEnglish = null;
            DraftGroupId = GroupLookups.FirstOrDefault()?.Id;
            DraftBaseUnitId = UnitLookups.FirstOrDefault()?.Id;
            DraftDefaultWarehouseId = WarehouseLookups.FirstOrDefault()?.Id;
            DraftCostMethod = CostMethod.WeightedAverage;
            DraftIsActive = true;
            DraftMinStock = "0.0000";
            DraftMaxStock = "0.0000";
            DraftReorderPoint = "0.0000";
            Barcodes.Clear();
            Prices.Clear();
            Conversions.Clear();
            WarehouseBalances.Clear();
            Movements.Clear();
            ResetProfileDraft();
        }
        else if (Item is not null)
        {
            DraftCode = Item.Code ?? string.Empty;
            DraftNameArabic = Item.NameArabic ?? string.Empty;
            DraftNameEnglish = Item.NameEnglish;
            DraftGroupId = Item.GroupId;
            DraftBaseUnitId = Item.BaseUnitId;
            DraftDefaultWarehouseId = Item.DefaultWarehouseId;
            DraftCostMethod = Item.CostMethod ?? CostMethod.WeightedAverage;
            DraftIsActive = Item.IsActive ?? true;
            DraftMinStock = Item.MinStock;
            DraftMaxStock = Item.MaxStock;
            DraftReorderPoint = Item.ReorderPoint;
            LoadProfileDraft(Item.Profile);
        }

        DraftChanged();
    }

    protected override async Task<bool> SaveDraftAsync(EntityCardMode mode)
    {
        if (!HasContext) return false;

        if (mode == EntityCardMode.New)
        {
            ApplySlotBarcodes();
            var barcodeRequests = BarcodeRequests();
            var priceRequests = CreatePriceRequests();

            var createReq = new CreateItemRequest
            {
                Code = DraftCode.Trim(),
                NameArabic = DraftNameArabic.Trim(),
                NameEnglish = string.IsNullOrWhiteSpace(DraftNameEnglish) ? null : DraftNameEnglish.Trim(),
                GroupId = DraftGroupId,
                BaseUnitId = DraftBaseUnitId,
                DefaultWarehouseId = DraftDefaultWarehouseId,
                CostMethod = DraftCostMethod,
                MinStock = DraftMinStock,
                MaxStock = DraftMaxStock,
                ReorderPoint = DraftReorderPoint,
                Barcodes = barcodeRequests,
                Prices = priceRequests,
                Profile = BuildProfile(),
            };

            var created = await inventoryClient.CreateItemAsync(CompanyId, BranchId, createReq, Token).ConfigureAwait(true);
            Item = created;
            ItemId = created.Id;
            ForgetNavigation();
            if (ItemId is { } currentSavedId)
            {
                var mappings = new List<SetItemAccountMappingRequest>();
                void AddItemMap(string role, Guid? accId)
                {
                    if (accId.HasValue && accId.Value != Guid.Empty)
                        mappings.Add(new SetItemAccountMappingRequest { Role = role, AccountId = accId.Value });
                }
                AddItemMap("Revenue", OverrideRevenueAccountId);
                AddItemMap("Inventory", OverrideInventoryAccountId);
                AddItemMap("COGS", OverrideCogsAccountId);
                AddItemMap("Purchase", OverridePurchaseAccountId);
                AddItemMap("SalesDiscount", OverrideSalesDiscountAccountId);
                AddItemMap("PurchaseDiscount", OverridePurchaseDiscountAccountId);
                AddItemMap("SalesTax", OverrideSalesTaxAccountId);
                AddItemMap("PurchaseTax", OverridePurchaseTaxAccountId);

                await inventoryClient.SetAccountMappingsAsync(CompanyId, BranchId, new SetAccountMappingsRequest
                {
                    OwnerType = 1,
                    OwnerId = currentSavedId,
                    Mappings = mappings
                }, Token).ConfigureAwait(true);
            }

            ResetMode();
            await LoadItemAsync(Token).ConfigureAwait(true);
            return true;
        }

        if (mode == EntityCardMode.Edit && ItemId is { } existingId)
        {
            ApplySlotBarcodes();
            var updateReq = new UpdateItemRequest
            {
                Profile = BuildProfile(),
                Barcodes = BarcodeRequests(),
                NameArabic = DraftNameArabic.Trim(),
                NameEnglish = string.IsNullOrWhiteSpace(DraftNameEnglish) ? null : DraftNameEnglish.Trim(),
                GroupId = DraftGroupId,
                BaseUnitId = DraftBaseUnitId,
                DefaultWarehouseId = DraftDefaultWarehouseId,
                CostMethod = DraftCostMethod,
                MinStock = DraftMinStock,
                MaxStock = DraftMaxStock,
                ReorderPoint = DraftReorderPoint,
            };

            var updated = await inventoryClient.UpdateItemAsync(CompanyId, BranchId, existingId, updateReq, Token).ConfigureAwait(true);
            await SyncMatrixPricesAsync(existingId, Token).ConfigureAwait(true);
            Item = updated;
            ResetMode();
            await LoadItemAsync(Token).ConfigureAwait(true);
            return true;
        }

        return false;
    }

    protected override async Task LoadAsync(object? parameter, CancellationToken cancellationToken)
    {
        NotifyCardCommands();

        if (parameter is OpenItemCardRequest open)
        {
            ItemId = open.ItemId;
            linkedMovementId = open.MovementId;
            ResetMode();
            await LoadLookupsAsync(cancellationToken).ConfigureAwait(true);
            await LoadItemAsync(cancellationToken).ConfigureAwait(true);
        }
        else if (parameter is NewItemCardRequest)
        {
            ItemId = null;
            Item = null;
            StockSummary = null;
            await LoadLookupsAsync(cancellationToken).ConfigureAwait(true);
            EnterNew();
        }
    }

    private void EnterNew()
    {
        LoadDraft(EntityCardMode.New);
        // Direct property set through base mechanics
        var prop = typeof(EntityCardViewModel).GetProperty(nameof(Mode));
        prop?.SetValue(this, EntityCardMode.New);
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(StatusText));
        NotifyCardCommands();
    }

    public async Task LoadLookupsAsync(CancellationToken cancellationToken)
    {
        if (!HasContext) return;

        try
        {
            var groups = await inventoryClient.ListItemGroupsAsync(CompanyId, BranchId, cancellationToken).ConfigureAwait(true);
            GroupLookups.Clear();
            foreach (var g in groups) GroupLookups.Add(g);

            var units = await inventoryClient.ListUnitsAsync(CompanyId, BranchId, cancellationToken).ConfigureAwait(true);
            UnitLookups.Clear();
            foreach (var u in units) UnitLookups.Add(u);

            var warehouses = await inventoryClient.ListWarehousesAsync(CompanyId, BranchId, null, cancellationToken).ConfigureAwait(true);
            WarehouseLookups.Clear();
            foreach (var w in warehouses) WarehouseLookups.Add(w);

            ComponentLookups.Clear();
            for (var page = 1; page <= 25; page++)
            {
                var response = await inventoryClient.ListItemsAsync(CompanyId, BranchId, page, 200, null, null, null, true, cancellationToken).ConfigureAwait(true);
                var batch = response.Items ?? [];
                foreach (var item in batch.Where(i => i.Id is not null && i.Code is not null))
                    ComponentLookups.Add(new LookupItem(item.Id!.Value, item.Code!, item.NameArabic ?? item.Code!, item.NameEnglish));
                if (batch.Count == 0 || page * 200 >= (response.TotalCount ?? 0)) break;
            }

            if (accountsClient is not null)
            {
                var accounts = await accountsClient.GetAccountTreeAsync(CompanyId, BranchId, cancellationToken).ConfigureAwait(true);
                AccountLookups.Clear();
                foreach (var a in accounts.Where(x => x.PostingAllowed && x.IsActive))
                {
                    AccountLookups.Add(new LookupItem(a.Id, a.Code, a.NameArabic, a.NameEnglish));
                }
            }

            if (string.IsNullOrWhiteSpace(NewPriceCurrency))
            {
                NewPriceCurrency = Currencies is null ? Context?.BaseCurrencyCode ?? string.Empty
                    : (await Currencies.GetSettingsAsync(CompanyId, BranchId, cancellationToken).ConfigureAwait(true)).BaseCurrencyCode ?? string.Empty;
            }
            PriceCustomerLookups.Clear();
            if (PartyLookupClient is not null)
                foreach (var party in await PartyLookupClient.LookupPartiesAsync(CompanyId, BranchId, 200, null, PartyRole.Customer, cancellationToken).ConfigureAwait(true))
                    PriceCustomerLookups.Add(party);
            PricePatternLookups.Clear();
            if (PatternLookupClient is not null)
                foreach (var pattern in await PatternLookupClient.GetRunnablePatternsAsync(CompanyId, BranchId, cancellationToken).ConfigureAwait(true))
                    if (pattern.Id is { } patternId && pattern.OperationKind == BusinessOperationKind.SalesInvoice)
                        PricePatternLookups.Add(new LookupItem(patternId, pattern.Code ?? string.Empty, pattern.NameArabic ?? pattern.Code ?? string.Empty, null));
        }
        catch
        {
            // Fallback gracefully on lookup errors
        }
    }

    public async Task LoadItemAsync(CancellationToken cancellationToken)
    {
        if (!HasContext || ItemId is not { } id) return;

        try
        {
            var detail = await inventoryClient.GetItemAsync(CompanyId, BranchId, id, cancellationToken).ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();
            Item = detail;
            DocumentLinks?.SetSource(Context, linkedMovementId is null ? SalesLinkSource.Item : SalesLinkSource.Movement, linkedMovementId ?? ItemId);
            DraftCode = detail.Code ?? string.Empty;
            DraftNameArabic = detail.NameArabic ?? string.Empty;
            DraftNameEnglish = detail.NameEnglish;
            DraftGroupId = detail.GroupId;
            DraftBaseUnitId = detail.BaseUnitId;
            DraftDefaultWarehouseId = detail.DefaultWarehouseId;
            DraftCostMethod = detail.CostMethod ?? CostMethod.WeightedAverage;
            DraftIsActive = detail.IsActive ?? true;
            DraftMinStock = detail.MinStock;
            DraftMaxStock = detail.MaxStock;
            DraftReorderPoint = detail.ReorderPoint;

            // Load account overrides
            var directMappings = await inventoryClient.GetAccountMappingsAsync(CompanyId, BranchId, 1, id, cancellationToken).ConfigureAwait(true);
            OverrideRevenueAccountId = directMappings.FirstOrDefault(m => string.Equals(m.Role, "Revenue", StringComparison.OrdinalIgnoreCase))?.AccountId;
            OverrideInventoryAccountId = directMappings.FirstOrDefault(m => string.Equals(m.Role, "Inventory", StringComparison.OrdinalIgnoreCase))?.AccountId;
            OverrideCogsAccountId = directMappings.FirstOrDefault(m => string.Equals(m.Role, "COGS", StringComparison.OrdinalIgnoreCase))?.AccountId;
            OverridePurchaseAccountId = directMappings.FirstOrDefault(m => string.Equals(m.Role, "Purchase", StringComparison.OrdinalIgnoreCase))?.AccountId;
            OverrideSalesDiscountAccountId = directMappings.FirstOrDefault(m => string.Equals(m.Role, "SalesDiscount", StringComparison.OrdinalIgnoreCase))?.AccountId;
            OverridePurchaseDiscountAccountId = directMappings.FirstOrDefault(m => string.Equals(m.Role, "PurchaseDiscount", StringComparison.OrdinalIgnoreCase))?.AccountId;
            OverrideSalesTaxAccountId = directMappings.FirstOrDefault(m => string.Equals(m.Role, "SalesTax", StringComparison.OrdinalIgnoreCase))?.AccountId;
            OverridePurchaseTaxAccountId = directMappings.FirstOrDefault(m => string.Equals(m.Role, "PurchaseTax", StringComparison.OrdinalIgnoreCase))?.AccountId;

            // Load effective mappings
            try
            {
                var effective = await inventoryClient.GetEffectiveAccountMappingAsync(CompanyId, BranchId, id, cancellationToken).ConfigureAwait(true);
                void SetEff(string role, Action<string, string> assign)
                {
                    var acc = effective?.Accounts?.FirstOrDefault(a => string.Equals(a.Role, role, StringComparison.OrdinalIgnoreCase));
                    if (acc is not null && !string.IsNullOrWhiteSpace(acc.AccountCode))
                    {
                        var src = acc.SourceDescription ?? acc.Source ?? "محدد";
                        assign($"{acc.AccountCode} — {acc.AccountNameArabic}", src);
                    }
                    else
                    {
                        assign("— غير محدد —", "غير معين");
                    }
                }

                SetEff("Revenue", (d, s) => { EffectiveRevenueDisplay = d; RevenueSourceDisplay = s; });
                SetEff("Inventory", (d, s) => { EffectiveInventoryDisplay = d; InventorySourceDisplay = s; });
                SetEff("COGS", (d, s) => { EffectiveCogsDisplay = d; CogsSourceDisplay = s; });
                SetEff("Purchase", (d, s) => { EffectivePurchaseDisplay = d; PurchaseSourceDisplay = s; });
                SetEff("SalesDiscount", (d, s) => { EffectiveSalesDiscountDisplay = d; SalesDiscountSourceDisplay = s; });
                SetEff("PurchaseDiscount", (d, s) => { EffectivePurchaseDiscountDisplay = d; PurchaseDiscountSourceDisplay = s; });
                SetEff("SalesTax", (d, s) => { EffectiveSalesTaxDisplay = d; SalesTaxSourceDisplay = s; });
                SetEff("PurchaseTax", (d, s) => { EffectivePurchaseTaxDisplay = d; PurchaseTaxSourceDisplay = s; });
            }
            catch
            {
                // Fallback gracefully
            }

            // Load sub collections
            Barcodes.Clear();
            if (detail.Barcodes is not null)
            {
                foreach (var b in detail.Barcodes) Barcodes.Add(b);
            }

            Prices.Clear();
            if (detail.Prices is not null)
            {
                foreach (var p in detail.Prices) Prices.Add(p);
            }

            Conversions.Clear();
            if (detail.Conversions is not null)
            {
                foreach (var c in detail.Conversions) Conversions.Add(c);
            }

            LoadProfileDraft(detail.Profile);

            // Load derived stock summary
            try
            {
                var summary = await inventoryClient.GetItemStockAsync(CompanyId, BranchId, id, null, cancellationToken).ConfigureAwait(true);
                StockSummary = summary;
                WarehouseBalances.Clear();
                if (summary.Warehouses is not null)
                {
                    foreach (var w in summary.Warehouses) WarehouseBalances.Add(w);
                }
            }
            catch
            {
                // Fallback gracefully
            }

            // Load recent authoritative stock movements for this item
            try
            {
                var movResponse = await inventoryClient.ListStockMovementsAsync(CompanyId, BranchId, 1, 50, null, id, null, null, cancellationToken).ConfigureAwait(true);
                Movements.Clear();
                if (movResponse.Items is not null)
                {
                    foreach (var m in movResponse.Items) Movements.Add(m);
                }
            }
            catch
            {
                // Fallback gracefully
            }

            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(TotalStockDisplay));
            OnPropertyChanged(nameof(CurrentQuantityDisplay));
            NotifyCardCommands();
        }
        catch (Exception ex)
        {
            Operation.SetError(Connection.DesktopErrors.Key(ex));
        }
    }

    private void AddDraftBarcode()
    {
        if (string.IsNullOrWhiteSpace(NewBarcodeText)) return;
        var unit = UnitLookups.FirstOrDefault(u => u.Id == NewBarcodeUnitId) ?? UnitLookups.FirstOrDefault();
        Barcodes.Add(new ItemBarcodeDto
        {
            Id = Guid.NewGuid(),
            Barcode = NewBarcodeText.Trim(),
            UnitId = unit?.Id,
            UnitCode = unit?.Code,
            IsDefault = NewBarcodeIsDefault,
        });
        NewBarcodeText = string.Empty;
        NewBarcodeIsDefault = false;
        DraftChanged();
    }

    private void RemoveDraftBarcode(ItemBarcodeDto? barcode)
    {
        if (barcode is null) return;
        Barcodes.Remove(barcode);
        DraftChanged();
    }

    private void AddDraftPrice()
    {
        if (string.IsNullOrWhiteSpace(NewPriceListCode)) return;
        var unit = UnitLookups.FirstOrDefault(u => u.Id == NewPriceUnitId) ?? UnitLookups.FirstOrDefault(u => u.Id == DraftBaseUnitId);
        var row = new ItemPriceDto
        {
            Id = Guid.NewGuid(),
            PriceListCode = NewPriceListCode.Trim().ToUpperInvariant(),
            Price = NewPriceAmount.Trim(),
            CurrencyCode = NewPriceCurrency.Trim().ToUpperInvariant(),
            UnitId = unit?.Id,
            UnitCode = unit?.Code,
            MinQuantity = NewPriceMinQuantity.Trim(),
            PartyId = NewPricePartyId,
            PatternId = NewPricePatternId,
            ValidFrom = NewPriceValidFrom is { } from ? DateOnly.FromDateTime(from) : null,
            ValidTo = NewPriceValidTo is { } to ? DateOnly.FromDateTime(to) : null,
        };
        if (IsNewMode)
        {
            Prices.Add(row);
            DraftChanged();
            return;
        }
        if (Pricing is null || ItemId is not { } itemId) return;
        _ = RunAsync(async token =>
        {
            var saved = await Pricing.AddPriceAsync(CompanyId, BranchId, itemId, ToPriceRequest(row), token).ConfigureAwait(true);
            Prices.Add(saved);
        });
    }

    private void RemoveDraftPrice(ItemPriceDto? price)
    {
        if (price is null) return;
        if (IsNewMode)
        {
            Prices.Remove(price);
            DraftChanged();
            return;
        }
        if (Pricing is null || ItemId is not { } itemId || price.Id is not { } priceId) return;
        _ = RunAsync(async token =>
        {
            await Pricing.DeletePriceAsync(CompanyId, BranchId, itemId, priceId, token).ConfigureAwait(true);
            Prices.Remove(price);
        });
    }

    private static CreateItemPriceRequest ToPriceRequest(ItemPriceDto p) => new()
    {
        PriceListCode = p.PriceListCode ?? string.Empty,
        Price = p.Price ?? string.Empty,
        CurrencyCode = p.CurrencyCode ?? string.Empty,
        UnitId = p.UnitId,
        MinQuantity = p.MinQuantity ?? "0.0000",
        PartyId = p.PartyId,
        PatternId = p.PatternId,
        ValidFrom = p.ValidFrom,
        ValidTo = p.ValidTo,
    };

    protected override void ClearPage()
    {
        Item = null;
        ItemId = null;
        StockSummary = null;
        Barcodes.Clear();
        Prices.Clear();
        Conversions.Clear();
        WarehouseBalances.Clear();
        Movements.Clear();
        DocumentLinks?.SetSource(null, SalesLinkSource.Item, null);
        GroupLookups.Clear();
        UnitLookups.Clear();
        WarehouseLookups.Clear();
        PriceCustomerLookups.Clear(); PricePatternLookups.Clear();
        NewPriceCurrency = string.Empty; NewPricePartyId = null; NewPricePatternId = null;
        ResetMode();
    }

    protected override void NotifyCommands()
    {
        NotifyNavigation();
        DocumentLinks?.SetSource(Context, linkedMovementId is null ? SalesLinkSource.Item : SalesLinkSource.Movement, linkedMovementId ?? ItemId);
        openMovementSourceCommand?.NotifyCanExecuteChanged();
        viewItemInvoicesCommand?.NotifyCanExecuteChanged();
        base.NotifyCommands();
        RefreshCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanEditPrices));
        AddPriceCommand.NotifyCanExecuteChanged();
        RemovePriceCommand.NotifyCanExecuteChanged();
        NotifyCardCommands();
    }
}

public sealed class ItemCardFactory(
    IInventoryClient inventoryClient,
    ShellNavigator navigator,
    DesktopOperation operation,
    ILocalizationService localization,
    IUserConfirmation confirmation,
    SalesDocumentLinksFactory links,
    IChartOfAccountsClient? accounts = null,
    IPricingClient? pricing = null,
    IPartiesClient? parties = null,
    IPatternsClient? patterns = null,
    ICurrenciesClient? currencies = null)
{
    public ItemCardViewModel Create() => new(inventoryClient, navigator, operation, localization, confirmation, accounts)
    {
        DocumentLinks = links.Create(), Pricing = pricing, PartyLookupClient = parties, PatternLookupClient = patterns, Currencies = currencies,
    };
}
