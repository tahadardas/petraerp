using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;
using PetraERP.Desktop.Accounting;
using PetraERP.Desktop.Currencies;
using PetraERP.Desktop.Inventory;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Parties;
using PetraERP.Desktop.Patterns;
using PetraERP.Desktop.Sales;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Purchases;

public sealed record OpenPurchaseRequest(Guid InvoiceId);

public sealed record NewPurchaseRequest(Guid? SupplierId = null);

public sealed record PaymentMethodOption(PaymentMethod Method, string Label);

/// <summary>An editable purchase line; the displayed total is a preview, the server recomputes it.</summary>
public sealed partial class PurchaseLineItem : ObservableObject, Controls.IDocumentEntryRow
{
    public Action? Changed { get; set; }

    [ObservableProperty] private Guid? itemId;
    [ObservableProperty] private Guid? unitId;
    [ObservableProperty] private string quantity = "0";
    [ObservableProperty] private string unitCost = "0";
    [ObservableProperty] private string discountAmount = "0";
    [ObservableProperty] private string taxAmount = "0";
    [ObservableProperty] private Guid? costCenterId;
    [ObservableProperty] private string? notes;

    /// <summary>The units this line's item accepts, base unit first. Never the whole company list.</summary>
    [ObservableProperty] private IReadOnlyList<Controls.LookupItem> unitOptions = [];

    private IReadOnlyList<Guid> allowedUnits = [];

    /// <summary>The item the unit options belong to; null until they were set for the current item.</summary>
    internal Guid? UnitsResolvedFor { get; private set; }
    /// <summary>The item the server was asked about for this line, so one line asks once per item and never per keystroke.</summary>
    internal Guid? UnitsAskedFor { get; set; }
    /// <summary>True once the server stated the item's units, so the chosen unit can be judged.</summary>
    internal bool UnitsKnown { get; private set; }
    /// <summary>True when the server stated the item has no base unit (its unit setup is missing).</summary>
    public bool UnitSetupMissing { get; private set; }

    /// <summary>The chosen unit has no conversion to the item's base unit; the server would reject the document.</summary>
    public bool HasDisallowedUnit => UnitsKnown && UnitId is { } id && !allowedUnits.Contains(id);

    /// <summary>Starts over for another item (or none): nothing is known about its units yet.</summary>
    internal void MarkUnitsFor(Guid? itemId)
    {
        UnitsResolvedFor = itemId; UnitsAskedFor = null; UnitsKnown = false; UnitSetupMissing = false; allowedUnits = [];
        UnitOptions = [];
        NotifyState();
    }

    /// <summary>Shows the line's own unit while the item's units are not known (a document that is only viewed, or no answer yet).</summary>
    internal void ShowUnit(Controls.LookupItem? current)
    {
        if (UnitsKnown) return;
        UnitOptions = current is null ? [] : [current];
    }

    /// <summary>
    /// Applies the server's answer: an empty unit takes the item's base unit. A stored unit that does not fit is never rewritten
    /// silently; the row reports it and the operator chooses.
    /// </summary>
    internal void ApplyUnits(Inventory.ItemUnitChoices choices, IReadOnlyList<Controls.LookupItem> options)
    {
        if (!choices.Resolved)
        {
            if (UnitId is null && choices.BaseUnitId is { } certain) UnitId = certain;
            return;
        }
        if (choices.BaseUnitId is not { } baseUnit) { UnitSetupMissing = true; NotifyState(); return; } // cannot be judged, only reported
        UnitsKnown = true; UnitSetupMissing = false; allowedUnits = choices.UnitIds;
        UnitOptions = options;
        if (UnitId is null) UnitId = baseUnit;
        NotifyState();
    }

    private bool isPersisted;

    public static PurchaseLineItem CreateBlankSlot(Action? changed = null) => new()
    {
        Quantity = "0",
        UnitCost = "0",
        DiscountAmount = "0",
        TaxAmount = "0",
        Changed = changed,
    };

    public bool IsPersisted
    {
        get => isPersisted;
        set { if (SetProperty(ref isPersisted, value)) NotifyState(); }
    }

    /// <summary>Judged on the entered values alone, so an empty entry slot is discarded instead of blocking the invoice.</summary>
    public bool IsPristineBlank =>
        !IsPersisted && ItemId == null && UnitId == null && CostCenterId == null && string.IsNullOrWhiteSpace(Notes) &&
        IsBlankOrZero(Quantity) && IsBlankOrZero(UnitCost) && IsBlankOrZero(DiscountAmount) && IsBlankOrZero(TaxAmount);

    private static bool IsBlankOrZero(string? text) =>
        string.IsNullOrWhiteSpace(text) || Parse(text) == 0m;

    public bool IsComplete =>
        ItemId is not null && UnitId is not null && !HasDisallowedUnit && Parse(Quantity) is > 0m && Total is >= 0m
        && Parse(DiscountAmount) is >= 0m && Parse(TaxAmount) is >= 0m;

    public bool IsIncomplete => !IsPristineBlank && !IsComplete;

    public Controls.DocumentRowState State =>
        IsPersisted ? Controls.DocumentRowState.Persisted :
        IsPristineBlank ? Controls.DocumentRowState.PristineBlank :
        IsComplete ? Controls.DocumentRowState.Complete :
        Controls.DocumentRowState.Incomplete;

    public string? RowErrorCode =>
        IsPristineBlank || IsComplete ? null :
        ItemId is null ? "D_LineItemRequired" :
        UnitId is null ? (UnitSetupMissing ? "D_LineUnitSetupMissing" : "D_LineUnitRequired") :
        HasDisallowedUnit ? "D_LineUnitNotAllowed" :
        Parse(Quantity) is not > 0m ? "D_LineQuantityPositive" :
        "D_LineDataIncomplete";

    public string? RowError => RowErrorCode switch
    {
        null => null,
        "D_LineItemRequired" => "المادة مطلوبة",
        "D_LineUnitRequired" => "الوحدة مطلوبة",
        "D_LineUnitSetupMissing" => "لا توجد وحدة أساسية معرّفة لهذه المادة. عرّف وحدات المادة من بطاقتها ثم أعد الاختيار.",
        "D_LineUnitNotAllowed" => "الوحدة المختارة غير معرّفة لهذه المادة. اختر إحدى الوحدات المعتمدة.",
        "D_LineQuantityPositive" => "الكمية يجب أن تكون أكبر من الصفر",
        _ => "بيانات السطر غير مكتملة",
    };

    public decimal? Total =>
        IsPristineBlank ? 0m :
        Parse(Quantity) is { } q && Parse(UnitCost) is { } c && Parse(DiscountAmount) is { } d && Parse(TaxAmount) is { } t
            ? decimal.Round(q * c, 4, MidpointRounding.AwayFromZero) - d + t : null;

    public string TotalDisplay => IsPristineBlank ? "0.0000" : Total?.ToString("F4", CultureInfo.InvariantCulture) ?? "—";

    private void NotifyState()
    {
        OnPropertyChanged(nameof(IsPristineBlank));
        OnPropertyChanged(nameof(IsComplete));
        OnPropertyChanged(nameof(IsIncomplete));
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(RowErrorCode));
        OnPropertyChanged(nameof(RowError));
        OnPropertyChanged(nameof(HasDisallowedUnit));
    }

    partial void OnItemIdChanged(Guid? value)
    {
        if (value.HasValue && (quantity == "0" || string.IsNullOrWhiteSpace(quantity)))
        {
            quantity = "1";
            OnPropertyChanged(nameof(Quantity));
        }
        Touch();
    }

    partial void OnUnitIdChanged(Guid? value) => Touch();
    partial void OnCostCenterIdChanged(Guid? value) => Touch();
    partial void OnQuantityChanged(string value) => Touch();
    partial void OnUnitCostChanged(string value) => Touch();
    partial void OnDiscountAmountChanged(string value) => Touch();
    partial void OnTaxAmountChanged(string value) => Touch();
    partial void OnNotesChanged(string? value) => Touch();

    private void Touch()
    {
        NotifyState();
        OnPropertyChanged(nameof(Total));
        OnPropertyChanged(nameof(TotalDisplay));
        Changed?.Invoke();
    }

    internal static decimal? Parse(string? text) =>
        decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) && decimal.Round(value, 4) == value ? value : null;
}

public sealed partial class PurchasesViewModel : PageViewModel, IUnsavedPage
{
    public const string ViewPermission = "Purchases.Invoices.View";
    public const string ManagePermission = "Purchases.Invoices.Manage";
    public const string ApprovePermission = "Purchases.Invoices.Approve";
    public const string PostPermission = "Purchases.Invoices.Post";
    public const string ReturnPermission = "Purchases.Invoices.Return";

    private readonly IPurchaseInvoicesClient client;
    private readonly IPartiesClient partiesClient;
    private readonly IPatternsClient patternsClient;
    private readonly IInventoryClient inventoryClient;
    private readonly IChartOfAccountsClient accountsClient;
    private readonly ShellNavigator navigator;
    private readonly IUserConfirmation confirmation;
    private readonly DocumentNumberPeek numberPeek;
    private readonly DocumentCurrencyReference currencyReference;
    private int generation;
    private readonly Inventory.ItemUnitCatalog unitCatalog;
    private int rateGeneration;
    private string? lastAutoRate;
    private string baseCurrency = string.Empty;

    [ObservableProperty] private PurchaseSummaryDto? selectedInvoice;
    [ObservableProperty] private int totalCount;
    [ObservableProperty] private int workspaceTab;
    [ObservableProperty] private Guid? filterSupplierId;
    [ObservableProperty] private PurchaseInvoiceStatus? filterStatus;
    [ObservableProperty] private DateTimeOffset? filterFromDate;
    [ObservableProperty] private DateTimeOffset? filterToDate;
    [ObservableProperty] private int pageNumber = 1;

    public const int PageSize = 50;

    public sealed record StatusFilterOption(string Label, PurchaseInvoiceStatus? Value);

    public IReadOnlyList<StatusFilterOption> StatusFilterOptions { get; } =
    [
        new("كل الحالات", null),
        new("مسودة", PurchaseInvoiceStatus.Draft),
        new("معتمدة", PurchaseInvoiceStatus.Approved),
        new("مرحلة", PurchaseInvoiceStatus.Posted),
        new("معكوسة", PurchaseInvoiceStatus.Reversed),
    ];

    /// <summary>Calendar value for the "from" picker; the query uses the start of that day.</summary>
    public DateTime? FilterFromDateValue
    {
        get => FilterFromDate?.Date;
        set => FilterFromDate = value is { } day ? new DateTimeOffset(DateTime.SpecifyKind(day.Date, DateTimeKind.Utc)) : null;
    }

    /// <summary>Calendar value for the "to" picker; the query includes the whole of that day.</summary>
    public DateTime? FilterToDateValue
    {
        get => FilterToDate?.Date;
        set => FilterToDate = value is { } day ? new DateTimeOffset(DateTime.SpecifyKind(day.Date, DateTimeKind.Utc)).AddDays(1).AddTicks(-1) : null;
    }

    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber * PageSize < TotalCount;
    public int PageCount => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
    public string PageSummary => $"صفحة {PageNumber} من {PageCount} — {TotalCount} فاتورة";

    public AsyncRelayCommand ApplyFiltersCommand { get; private set; } = null!;
    public AsyncRelayCommand ClearFiltersCommand { get; private set; } = null!;
    public AsyncRelayCommand PreviousPageCommand { get; private set; } = null!;
    public AsyncRelayCommand NextPageCommand { get; private set; } = null!;

    /// <summary>The number this draft would most likely get, read without reserving it.</summary>
    [ObservableProperty] private string? numberPreview;

    public PurchasesViewModel(IPurchaseInvoicesClient client, IPartiesClient partiesClient, IPatternsClient patternsClient, IInventoryClient inventoryClient,
        IChartOfAccountsClient accountsClient, ICurrenciesClient currenciesClient, ShellNavigator navigator, DesktopOperation operation,
        ILocalizationService localization, IUserConfirmation confirmation) : base(operation, localization)
    {
        this.client = client; this.partiesClient = partiesClient; this.patternsClient = patternsClient; this.inventoryClient = inventoryClient;
        this.accountsClient = accountsClient; this.navigator = navigator; this.confirmation = confirmation;
        numberPeek = new DocumentNumberPeek(patternsClient);
        unitCatalog = new Inventory.ItemUnitCatalog(inventoryClient);
        currencyReference = new DocumentCurrencyReference(currenciesClient);
        RefreshCommand = new AsyncRelayCommand(() => RunAsync(ListAsync), () => HasContext && Idle);
        OpenCommand = new AsyncRelayCommand(async () =>
        {
            var id = SelectedInvoice?.Id;
            if (id is null || !await ConfirmReplacementAsync().ConfigureAwait(true)) return;
            await RunAsync(t => OpenAsync(id.Value, t)).ConfigureAwait(true);
        },
            () => HasContext && Idle && SelectedInvoice?.Id is not null);
        NewCommand = new AsyncRelayCommand(async () =>
        {
            if (await ConfirmReplacementAsync().ConfigureAwait(true)) StartNew(null);
        }, () => HasContext && Idle && CanManage);
        NewByPatternCommand = new AsyncRelayCommand<PatternLookupItem>(async pattern =>
        {
            if (pattern is null || !await ConfirmReplacementAsync().ConfigureAwait(true)) return;
            StartNew(null);
            DraftPatternRevisionId = pattern.RevisionId;
            lockedPatternRevisionId = pattern.RevisionId;
            OnPropertyChanged(nameof(PatternPickerEnabled));
        }, pattern => pattern is not null && HasContext && Idle && CanManage);
        ManagePatternsCommand = new AsyncRelayCommand(() => CanViewPatterns ? navigator.NavigateAsync("patterns") : Task.CompletedTask,
            () => HasContext && Idle && CanViewPatterns);
        OpenItemCommand = new AsyncRelayCommand(() => SelectedLine?.ItemId is { } id ? navigator.NavigateAsync("item-card", new OpenItemCardRequest(id)) : Task.CompletedTask,
            () => HasContext && Idle && SelectedLine?.ItemId is not null && Can("Inventory.Items.View"));
        OpenWarehouseCommand = new AsyncRelayCommand(() => DocumentWarehouseId is { } warehouseId
                ? navigator.NavigateAsync("inventory-operations", new Inventory.OpenWarehouseRequest(warehouseId)) : Task.CompletedTask,
            () => HasContext && Idle && DocumentWarehouseId is not null && Can("Inventory.Movements.View"));
        BulkPostCommand = new AsyncRelayCommand(BulkPostAsync, () => HasContext && Idle && CanManagePostingBatches);
        CancelCommand = new RelayCommand(() =>
        {
            if (Current is not null)
            {
                Show(Current);
            }
            else
            {
                ResetEditor();
                WorkspaceTab = 0;
            }
        }, () => HasContext && Idle && (IsEditing || Current is null));
        ApplyFiltersCommand = new AsyncRelayCommand(() => RunAsync(ct => { PageNumber = 1; return ListAsync(ct); }), () => HasContext && Idle);
        ClearFiltersCommand = new AsyncRelayCommand(() => RunAsync(ClearFiltersAsync), () => HasContext && Idle);
        PreviousPageCommand = new AsyncRelayCommand(() => RunAsync(ct => GoToPageAsync(PageNumber - 1, ct)), () => HasContext && Idle && HasPreviousPage);
        NextPageCommand = new AsyncRelayCommand(() => RunAsync(ct => GoToPageAsync(PageNumber + 1, ct)), () => HasContext && Idle && HasNextPage);
        InitializeEditor();
    }

    protected override string RequiredPermission => ViewPermission;
    public bool CanManage => Can(ManagePermission);
    public bool CanApprove => Can(ApprovePermission);
    public bool CanViewPatterns => HasContext && Can("Patterns.Documents.View");
    public bool CanManagePostingBatches => Can(DocumentProcessing.PostingManagementViewModel.ManagePermission);
    public AsyncRelayCommand ManagePatternsCommand { get; }
    public AsyncRelayCommand BulkPostCommand { get; }

    public ObservableCollection<PurchaseSummaryDto> Invoices { get; } = [];
    public ObservableCollection<PatternLookupItem> PurchasePatterns { get; } = [];
    /// <summary>Line-grid column captions defined by the selected pattern (field name to caption).</summary>
    public IReadOnlyDictionary<string, string> FieldCaptions =>
        PurchasePatterns.FirstOrDefault(p => p.RevisionId == DraftPatternRevisionId)?.FieldCaptions ?? new Dictionary<string, string>();
    public ObservableCollection<PartyLookupDto> Suppliers { get; } = [];
    public ObservableCollection<WarehouseDto> Warehouses { get; } = [];
    public ObservableCollection<ItemLookupDto> Items { get; } = [];
    public ObservableCollection<UnitOfMeasureDto> Units { get; } = [];
    public ObservableCollection<CostCenterResponse> CostCenters { get; } = [];
    /// <summary>Currencies the company has enabled; the base currency is first because the server lists it first.</summary>
    public ObservableCollection<string> CurrencyOptions { get; } = [];
    public IReadOnlyList<PaymentMethodOption> PaymentOptions =>
        [new(PaymentMethod.Credit, Localization["D_PurchaseOnCredit"]), new(PaymentMethod.Cash, Localization["D_PurchaseInCash"])];

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand OpenCommand { get; }
    public AsyncRelayCommand NewCommand { get; }
    public RelayCommand CancelCommand { get; }
    /// <summary>One button per purchase pattern (PurchasePatterns): a new invoice already fixed on that pattern.</summary>
    public AsyncRelayCommand<PatternLookupItem> NewByPatternCommand { get; }
    public AsyncRelayCommand OpenItemCommand { get; private set; } = null!;
    public AsyncRelayCommand OpenWarehouseCommand { get; private set; } = null!;

    /// <summary>The warehouse of the shown document: the draft choice while editing, otherwise the stored one.</summary>
    private Guid? DocumentWarehouseId => IsEditing ? DraftWarehouseId ?? Current?.WarehouseId : Current?.WarehouseId ?? DraftWarehouseId;

    private async Task<bool> ConfirmReplacementAsync()
    {
        var requestedContext = Context;
        if (HasUnsavedChanges && !await confirmation.ConfirmAsync("D_ConfirmDiscardChanges").ConfigureAwait(true)) return false;
        return requestedContext is not null && ReferenceEquals(Context, requestedContext) && Idle;
    }

    partial void OnSelectedInvoiceChanged(PurchaseSummaryDto? value) => OpenCommand.NotifyCanExecuteChanged();

    /// <summary>
    /// Asks the server, without reserving, for the number this draft would most likely get, so leaving a draft unsaved
    /// consumes nothing and a saved document always carries the number allocated at save time.
    /// </summary>
    private void RefreshNumberPreview()
    {
        if (!IsEditing || !HasContext || !string.IsNullOrWhiteSpace(Current?.InvoiceNumber)
            || PurchasePatterns.FirstOrDefault(p => p.RevisionId == DraftPatternRevisionId) is not { } pattern)
        {
            NumberPreview = null;
            return;
        }

        _ = PeekNumberAsync(pattern);
    }

    private async Task PeekNumberAsync(PatternLookupItem pattern)
    {
        var token = Token;
        var preview = await numberPeek.ReadAsync(CompanyId, BranchId, pattern.PatternId, pattern.RevisionId,
            DraftDate is { } date ? DateOnly.FromDateTime(date.Date) : null, token).ConfigureAwait(true);
        if (!token.IsCancellationRequested && IsEditing) NumberPreview = preview;
    }

    partial void OnNumberPreviewChanged(string? value) => OnPropertyChanged(nameof(HasNumberPreview));

    /// <summary>Drives the header chip; the estimate is shown only while it is the one the current pattern and date produced.</summary>
    public bool HasNumberPreview => !string.IsNullOrWhiteSpace(NumberPreview);

    /// <summary>
    /// Fills the rate from the company register for the chosen currency and document date. A rate the operator typed is
    /// never overwritten, the base currency is always one, and a currency with no registered rate is reported instead of
    /// being quietly priced at one.
    /// </summary>
    private void RefreshRate()
    {
        if (!IsEditing || !HasContext || !Controls.RequiredInput.Currency(DraftCurrency) || DraftDate is not { } date)
        {
            RateInfo = string.Empty;
            return;
        }

        _ = ReadRateAsync(DraftCurrency.Trim().ToUpperInvariant(), DateOnly.FromDateTime(date.Date));
    }

    private async Task ReadRateAsync(string currency, DateOnly date)
    {
        var attempt = ++rateGeneration;
        var token = Token;
        var typedRate = DraftRate is { Length: > 0 } current && !string.Equals(current, lastAutoRate, StringComparison.Ordinal) ? current : null;
        var reading = await currencyReference.ReadRateAsync(CompanyId, BranchId, currency, date, token).ConfigureAwait(true);
        if (token.IsCancellationRequested || attempt != rateGeneration || !IsEditing) return;
        if (reading is null)
        {
            RateInfo = Localization["CUR_RateUnavailable"];
            return;
        }
        var isBase = string.Equals(currency, reading.BaseCurrencyCode, StringComparison.Ordinal);
        if (reading.Found)
        {
            // The base currency is one by definition, so even a typed value is corrected to what the server states.
            if (typedRate is null || isBase) SetAutoRate(reading.Rate ?? string.Empty);
            RateInfo = isBase
                ? Localization["CUR_BaseRate"]
                : typedRate is not null
                    ? string.Format(CultureInfo.CurrentCulture, Localization["CUR_ExplicitRate"], reading.Rate)
                    : string.Format(CultureInfo.CurrentCulture, Localization["CUR_RateFromRegister"], reading.EffectiveDate, reading.Source);
            return;
        }
        RateInfo = Localization["CUR_RateMissing"];
        if (typedRate is null) SetAutoRate(string.Empty);
    }

    private void SetAutoRate(string rate)
    {
        lastAutoRate = rate;
        ApplySystemValue(() => DraftRate = rate);
    }

    protected override async Task LoadAsync(object? parameter, CancellationToken cancellationToken)
    {
        // Runs inside the shell's navigation operation; a late response after a scope change is dropped by generation.
        var requested = ++generation;
        await LoadLookupsAsync(cancellationToken).ConfigureAwait(true);
        if (requested != generation) return;
        await ListAsync(cancellationToken).ConfigureAwait(true);
        if (requested != generation) return;
        switch (parameter)
        {
            case OpenPurchaseRequest open: await OpenAsync(open.InvoiceId, cancellationToken).ConfigureAwait(true); break;
            case NewPurchaseRequest create: StartNew(create.SupplierId); break;
        }
        NotifyCommands();
    }

    private async Task LoadLookupsAsync(CancellationToken cancellationToken)
    {
        // Independent reads run together; awaiting each in turn is what made the screen restock itself line by line.
        var patternsTask = patternsClient.GetRunnablePatternsAsync(CompanyId, BranchId, cancellationToken);
        var suppliersTask = partiesClient.LookupPartiesAsync(CompanyId, BranchId, 200, null, PartyRole.Supplier, cancellationToken);
        var currenciesTask = currencyReference.ReadSettingsAsync(CompanyId, BranchId, cancellationToken);
        var warehousesTask = Can("Inventory.Items.View") ? inventoryClient.ListWarehousesAsync(CompanyId, BranchId, BranchId, cancellationToken) : null;
        var itemsTask = Can("Inventory.Items.View") ? inventoryClient.LookupItemsAsync(CompanyId, BranchId, 500, null, cancellationToken) : null;
        var unitsTask = Can("Inventory.Items.View") ? inventoryClient.ListUnitsAsync(CompanyId, BranchId, cancellationToken) : null;
        var accountsTask = Can("Ledger.Accounts.View") ? accountsClient.GetAccountTreeAsync(CompanyId, BranchId, cancellationToken) : null;
        var costCentersTask = Can("Ledger.CostCenters.View") ? accountsClient.SearchCostCentersAsync(CompanyId, BranchId, null, true, 1, 200, cancellationToken) : null;

        PurchasePatterns.Clear();
        var runnable = await patternsTask.ConfigureAwait(true);
        var revisionTasks = runnable
            .Where(p => p.Id.HasValue && (p.OperationKind == BusinessOperationKind.PurchaseInvoice || p.OperationKind == BusinessOperationKind.PurchaseReturn))
            .Select(async p =>
            {
                var detail = await patternsClient.GetPatternAsync(CompanyId, BranchId, p.Id!.Value, cancellationToken).ConfigureAwait(false);
                var cfg = detail.ActiveRevision?.Configuration;
                return detail.ActiveRevision?.Id is not { } revision
                    ? null
                    : new PatternLookupItem
                    {
                        AllowManualNumbering = cfg?.NumberingPolicy?.AllowManualNumbering == true,
                        LockDocumentDate = cfg?.EntryPolicy?.LockDocumentDate == true,
                        DefaultWarehouseId = cfg?.DefaultWarehouseId,
                        DefaultCostCenterId = cfg?.DefaultCostCenterId,
                        DefaultCurrencyCode = cfg?.DefaultCurrencyCode,
                        DefaultPaymentMethod = cfg?.AllowedPaymentMethods is { Count: > 0 } methods ? methods[0] : null,
                        FieldCaptions = (cfg?.LineFields ?? []).Where(f => !string.IsNullOrWhiteSpace(f.FieldName) && !string.IsNullOrWhiteSpace(f.CaptionArabic))
                            .GroupBy(f => f.FieldName!, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().CaptionArabic!.Trim(), StringComparer.OrdinalIgnoreCase),
                        OperationKind = p.OperationKind,
                        PatternId = p.Id!.Value,
                        RevisionId = revision,
                        Name = p.NameArabic ?? p.Code ?? string.Empty
                    };
            }).ToArray();
        foreach (var pattern in await Task.WhenAll(revisionTasks).ConfigureAwait(true))
            if (pattern is not null) PurchasePatterns.Add(pattern);
            OnPropertyChanged(nameof(FieldCaptions));

        Suppliers.Clear();
        foreach (var s in await suppliersTask.ConfigureAwait(true)) Suppliers.Add(s);
        CurrencyOptions.Clear();
        var settings = await currenciesTask.ConfigureAwait(true);
        baseCurrency = settings?.BaseCurrencyCode ?? string.Empty;
        foreach (var currency in settings?.Currencies ?? [])
            if (currency.Code is { Length: 3 } code && currency.IsActive == true) CurrencyOptions.Add(code);
        Warehouses.Clear(); Items.Clear(); Units.Clear(); unitCatalog.Clear();
        if (warehousesTask is not null) foreach (var w in await warehousesTask.ConfigureAwait(true)) Warehouses.Add(w);
        if (itemsTask is not null) foreach (var i in await itemsTask.ConfigureAwait(true)) Items.Add(i);
        if (unitsTask is not null) foreach (var u in await unitsTask.ConfigureAwait(true)) Units.Add(u);
        CostCenters.Clear();
        ReturnVarianceAccounts.Clear();
        if (accountsTask is not null)
            foreach (var a in await accountsTask.ConfigureAwait(true))
                if (a.IsActive && a.PostingAllowed) ReturnVarianceAccounts.Add(new(a.Id, a.Code, a.NameArabic, a.NameEnglish));
        if (costCentersTask is not null)
            foreach (var c in (await costCentersTask.ConfigureAwait(true)).Items ?? [])
                CostCenters.Add(c);
    }

    private async Task ListAsync(CancellationToken cancellationToken)
    {
        var page = await client.ListAsync(CompanyId, BranchId, FilterSupplierId, FilterStatus, FilterFromDate, FilterToDate, null, PageNumber, PageSize,
            cancellationToken).ConfigureAwait(true);
        Invoices.Clear();
        foreach (var i in page.Items ?? []) Invoices.Add(i);
        TotalCount = page.TotalCount ?? Invoices.Count;
    }

    private Task GoToPageAsync(int target, CancellationToken cancellationToken)
    {
        PageNumber = Math.Max(1, target);
        return ListAsync(cancellationToken);
    }

    private async Task ClearFiltersAsync(CancellationToken cancellationToken)
    {
        FilterSupplierId = null; FilterStatus = null; FilterFromDate = null; FilterToDate = null; PageNumber = 1;
        await ListAsync(cancellationToken).ConfigureAwait(true);
    }

    partial void OnPageNumberChanged(int value) { OnPropertyChanged(nameof(HasPreviousPage)); OnPropertyChanged(nameof(HasNextPage)); OnPropertyChanged(nameof(PageSummary)); }
    partial void OnTotalCountChanged(int value) { OnPropertyChanged(nameof(HasNextPage)); OnPropertyChanged(nameof(PageCount)); OnPropertyChanged(nameof(PageSummary)); }
    partial void OnFilterFromDateChanged(DateTimeOffset? value) => OnPropertyChanged(nameof(FilterFromDateValue));
    partial void OnFilterToDateChanged(DateTimeOffset? value) => OnPropertyChanged(nameof(FilterToDateValue));

    protected override void ClearPage()
    {
        generation++; unitCatalog.Clear();
        Invoices.Clear(); PurchasePatterns.Clear(); Suppliers.Clear(); Warehouses.Clear(); Items.Clear(); Units.Clear(); CostCenters.Clear();
        CurrencyOptions.Clear(); baseCurrency = string.Empty;
        SelectedInvoice = null; TotalCount = 0;
        FilterSupplierId = null; FilterStatus = null; FilterFromDate = null; FilterToDate = null; PageNumber = 1;
        WorkspaceTab = 0;
        ResetEditor();
    }

    protected override void NotifyCommands()
    {
        OnPropertyChanged(nameof(CanUseManualNumber));
        base.NotifyCommands();
        RefreshCommand?.NotifyCanExecuteChanged(); OpenCommand?.NotifyCanExecuteChanged(); NewCommand?.NotifyCanExecuteChanged(); NewByPatternCommand?.NotifyCanExecuteChanged();
        CancelCommand?.NotifyCanExecuteChanged();
        OpenItemCommand?.NotifyCanExecuteChanged(); OpenWarehouseCommand?.NotifyCanExecuteChanged();
        NotifyEditorCommands();
        OnPropertyChanged(nameof(CanManage));
        OnPropertyChanged(nameof(CanApprove));
        OnPropertyChanged(nameof(CanViewPatterns)); ManagePatternsCommand?.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanManagePostingBatches)); BulkPostCommand?.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HasPreviousPage)); OnPropertyChanged(nameof(HasNextPage)); OnPropertyChanged(nameof(PageSummary));
        ApplyFiltersCommand?.NotifyCanExecuteChanged(); ClearFiltersCommand?.NotifyCanExecuteChanged();
        PreviousPageCommand?.NotifyCanExecuteChanged(); NextPageCommand?.NotifyCanExecuteChanged();
    }

    private Task BulkPostAsync() => navigator.NavigateAsync("posting-management",
        new DocumentProcessing.BulkPostingNavigation(BulkPostingDocumentType.PurchaseInvoice));
}
