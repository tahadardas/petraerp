using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;
using PetraERP.Desktop.Accounting;
using PetraERP.Desktop.Controls;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Inventory;

public sealed partial class InventoryDraftLine : ObservableObject
{
    [ObservableProperty] private Guid? itemId;
    [ObservableProperty] private Guid? unitId;
    [ObservableProperty] private string quantity = "1.0000";
    [ObservableProperty] private string openingUnitCost = string.Empty;
    public string SnapshotQuantity { get; init; } = string.Empty;
    public string Cost { get; init; } = string.Empty;
}
public sealed record InventoryKindOption(InventoryDocumentKind Kind, string Label);
public sealed record OpenInventoryDocumentRequest(Guid Id);

/// <summary>Opens the warehouse page on one warehouse: its movements and balances, not the general list.</summary>
public sealed record OpenWarehouseRequest(Guid WarehouseId);

public sealed partial class InventoryOperationsViewModel : PageViewModel, ISavablePage
{
    private readonly IInventoryDocumentsClient client;
    private readonly IInventoryClient inventory;
    private readonly IChartOfAccountsClient accounts;
    private readonly IUserConfirmation confirmation;
    private readonly ShellNavigator navigator;
    private int page = 1, total, movementPage = 1, movementTotal;
    private string? actionKey, actionFingerprint;
    private string creationKey = Guid.NewGuid().ToString("N");
    [ObservableProperty] private InventoryDocumentDto? selectedDocument;
    [ObservableProperty] private InventoryDocumentDto? current;
    [ObservableProperty] private InventoryDocumentKind kind = InventoryDocumentKind.Transfer;
    [ObservableProperty] private Guid? warehouseId;
    [ObservableProperty] private Guid? targetWarehouseId;
    [ObservableProperty] private Guid? costCenterId;
    [ObservableProperty] private Guid? inventoryAccountId;
    [ObservableProperty] private Guid? offsetAccountId;
    [ObservableProperty] private DateTime? date = DateTime.Today;
    [ObservableProperty] private string reason = string.Empty;
    [ObservableProperty] private string actionReason = string.Empty;
    [ObservableProperty] private string search = string.Empty;
    [ObservableProperty] private string itemSearch = string.Empty;
    [ObservableProperty] private bool isEditing;
    [ObservableProperty] private InventoryDraftLine? selectedLine;
    [ObservableProperty] private Guid? filterWarehouseId;
    [ObservableProperty] private Guid? filterItemId;
    [ObservableProperty] private DateTime? fromDate;
    [ObservableProperty] private DateTime? toDate;
    [ObservableProperty] private InventoryMovementAuditDto? selectedMovement;
    private const int MovementsTab = 1;
    [ObservableProperty] private int workspaceTab;

    public InventoryOperationsViewModel(IInventoryDocumentsClient client, IInventoryClient inventory, IChartOfAccountsClient accounts,
        IUserConfirmation confirmation, ShellNavigator navigator, DesktopOperation operation, ILocalizationService localization) : base(operation, localization)
    {
        this.client = client; this.inventory = inventory; this.accounts = accounts; this.confirmation = confirmation; this.navigator = navigator;
        RefreshCommand = new(() => RunAsync(ListAsync), () => HasContext && Idle);
        NewCommand = new(NewAsync, () => HasContext && Idle && CanAnyManage);
        OpenCommand = new(OpenAsync, () => SelectedDocument?.Id is not null && Idle);
        EditCommand = new(() => { IsEditing = true; CaptureDraftBaseline(); NotifyCommands(); }, () => Current?.State == InventoryDocumentState.Draft && CanManage && Idle && !IsEditing);
        CancelCommand = new(CancelAsync, () => IsEditing && Idle);
        SaveCommand = new(SaveAsync, () => HasContext && Idle && IsEditing && Complete());
        AddLineCommand = new(() => AddLine(new()), () => IsEditing && Idle && Lines.Count < 200);
        RemoveLineCommand = new(() => { if (SelectedLine is { } line) { line.PropertyChanged -= LineChanged; Lines.Remove(line); SelectedLine = null; NotifyCommands(); } }, () => IsEditing && Idle && SelectedLine is not null);
        PostCommand = new(() => ActAsync(false), () => !IsEditing && Current?.State == InventoryDocumentState.Draft && CanManage && Idle && !string.IsNullOrWhiteSpace(ActionReason));
        ReverseCommand = new(() => ActAsync(true), () => !IsEditing && Current?.State == InventoryDocumentState.Posted && CanManage && Idle && !string.IsNullOrWhiteSpace(ActionReason));
        PreviousCommand = new(() => ChangePageAsync(-1), () => page > 1 && Idle);
        NextCommand = new(() => ChangePageAsync(1), () => (long)page * 50 < total && Idle);
        LoadMovementsCommand = new(() => RunAsync(t => LoadMovementsAsync(true, t)), () => HasContext && Idle);
        SearchItemsCommand = new(() => RunAsync(SearchItemsAsync), () => HasContext && Idle && Can("Inventory.Items.View") && !string.IsNullOrWhiteSpace(ItemSearch));
        PreviousMovementCommand = new(() => MoveHistoryAsync(-1), () => movementPage > 1 && Idle);
        NextMovementCommand = new(() => MoveHistoryAsync(1), () => (long)movementPage * 100 < movementTotal && Idle);
        OpenSourceCommand = new(OpenSourceAsync, CanOpenSource);
        BulkPostCommand = new(BulkPostAsync, () => HasContext && Idle && CanManagePostingBatches);
        OpenJournalCommand = new(() => Current?.JournalId is { } id ? navigator.NavigateAsync("journals", new OpenJournalRequest(id)) : Task.CompletedTask,
            () => Current?.JournalId is not null && Can("Ledger.Journals.View") && Idle);
    }

    protected override string RequiredPermission => "Inventory.Movements.View";
    public bool CanAnyManage => Can("Inventory.Transfers.Manage") || Can("Inventory.Adjustments.Manage");
    public bool CanManage => Current?.IsLegacy != true && Can(Kind == InventoryDocumentKind.Transfer ? "Inventory.Transfers.Manage" : "Inventory.Adjustments.Manage");
    public bool CanManagePostingBatches => Can(DocumentProcessing.PostingManagementViewModel.ManagePermission);
    public bool IsTransfer => Kind == InventoryDocumentKind.Transfer;
    public bool IsEntryReadOnly => !IsEditing;
    public bool IsOpeningCostReadOnly => !IsEditing || Kind != InventoryDocumentKind.Count;
    public bool CanChangeKind => IsEditing && Current is null;
    public bool RequiresAccounts => !IsTransfer;
    public bool CanSaveBeforeClose => HasUnsavedChanges && SaveCommand.CanExecute(null);
    public string NumberDisplay => Current?.Number ?? Localization["INV_AutomaticNumber"];
    public string PageStatus => $"{page} · {total}";
    public string MovementPageStatus => $"{movementPage} · {movementTotal}";
    public IEnumerable<InventoryKindOption> KindOptions => Enum.GetValues<InventoryDocumentKind>().Select(k => new InventoryKindOption(k, Localization["INV_Kind_" + k]));
    public ObservableCollection<InventoryDocumentDto> Documents { get; } = [];
    public ObservableCollection<InventoryDraftLine> Lines { get; } = [];
    public ObservableCollection<LookupItem> Items { get; } = [];
    public ObservableCollection<LookupItem> Warehouses { get; } = [];
    public ObservableCollection<LookupItem> Accounts { get; } = [];
    public ObservableCollection<LookupItem> CostCenters { get; } = [];
    public ObservableCollection<LookupItem> Units { get; } = [];
    public ObservableCollection<InventoryMovementAuditDto> Movements { get; } = [];
    public ObservableCollection<InventoryBalanceDto> Balances { get; } = [];
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand NewCommand { get; }
    public AsyncRelayCommand OpenCommand { get; }
    public RelayCommand EditCommand { get; }
    public AsyncRelayCommand CancelCommand { get; }
    public AsyncRelayCommand SaveCommand { get; }
    public RelayCommand AddLineCommand { get; }
    public RelayCommand RemoveLineCommand { get; }
    public AsyncRelayCommand PostCommand { get; }
    public AsyncRelayCommand ReverseCommand { get; }
    public AsyncRelayCommand PreviousCommand { get; }
    public AsyncRelayCommand NextCommand { get; }
    public AsyncRelayCommand LoadMovementsCommand { get; }
    public AsyncRelayCommand SearchItemsCommand { get; }
    public AsyncRelayCommand PreviousMovementCommand { get; }
    public AsyncRelayCommand NextMovementCommand { get; }
    public AsyncRelayCommand OpenSourceCommand { get; }
    public AsyncRelayCommand OpenJournalCommand { get; }
    public AsyncRelayCommand BulkPostCommand { get; }

    protected override async Task LoadAsync(object? parameter, CancellationToken cancellationToken)
    {
        var token = cancellationToken;
        Warehouses.Clear(); Items.Clear(); Units.Clear(); Accounts.Clear(); CostCenters.Clear();
        if (Can("Inventory.Warehouses.View"))
            foreach (var w in await inventory.ListWarehousesAsync(CompanyId, BranchId, BranchId, cancellationToken).ConfigureAwait(true))
                if (w.Id is { } id) Warehouses.Add(new(id, w.Code ?? string.Empty, w.NameArabic ?? string.Empty, w.NameEnglish));
        if (Can("Inventory.Items.View"))
        {
            foreach (var i in await inventory.LookupItemsAsync(CompanyId, BranchId, 500, null, token).ConfigureAwait(true))
                if (i.Id is { } id) Items.Add(new(id, i.Code ?? string.Empty, i.NameArabic ?? string.Empty, i.NameEnglish));
            foreach (var u in await inventory.ListUnitsAsync(CompanyId, BranchId, token).ConfigureAwait(true))
                if (u.Id is { } id) Units.Add(new(id, u.Code ?? string.Empty, u.NameArabic ?? string.Empty, u.NameEnglish));
        }
        if (Can("Ledger.Accounts.View"))
            foreach (var a in await accounts.GetAccountTreeAsync(CompanyId, BranchId, token).ConfigureAwait(true))
                if (a.IsActive && a.PostingAllowed) Accounts.Add(new(a.Id, a.Code, a.NameArabic, a.NameEnglish));
        if (Can("Ledger.CostCenters.View"))
            foreach (var c in (await accounts.SearchCostCentersAsync(CompanyId, BranchId, null, true, 1, 200, token).ConfigureAwait(true)).Items ?? [])
                CostCenters.Add(new(c.Id, c.Code, c.NameArabic, c.NameEnglish));
        token.ThrowIfCancellationRequested();
        await ListAsync(cancellationToken).ConfigureAwait(true);
        if (parameter is OpenInventoryDocumentRequest request) Show(await client.GetAsync(CompanyId, BranchId, request.Id, token).ConfigureAwait(true));
        if (parameter is OpenWarehouseRequest warehouse)
        {
            // Only a warehouse this user and branch may see is applied; an unknown id never falls back to another warehouse.
            FilterWarehouseId = Warehouses.Any(w => w.Id == warehouse.WarehouseId) ? warehouse.WarehouseId : null;
            FilterItemId = null;
            if (FilterWarehouseId is null) Operation.SetError("INV_WarehouseNotAvailable");
            else WorkspaceTab = MovementsTab;
        }
        await LoadMovementsAsync(true, cancellationToken).ConfigureAwait(true);
    }

    private async Task ListAsync(CancellationToken token)
    {
        var result = await client.ListAsync(CompanyId, BranchId, Search, page, token).ConfigureAwait(true);
        token.ThrowIfCancellationRequested(); Documents.Clear(); foreach (var d in result.Items ?? []) Documents.Add(d);
        total = result.TotalCount ?? 0; OnPropertyChanged(nameof(PageStatus)); NotifyCommands();
    }
    private async Task SearchItemsAsync(CancellationToken token)
    {
        var found = await inventory.LookupItemsAsync(CompanyId, BranchId, 500, ItemSearch.Trim(), token).ConfigureAwait(true);
        token.ThrowIfCancellationRequested();
        foreach (var item in found)
            if (item.Id is { } id && Items.All(i => i.Id != id)) Items.Add(new(id, item.Code ?? string.Empty, item.NameArabic ?? string.Empty, item.NameEnglish));
    }
    private async Task ChangePageAsync(int delta) { page += delta; await RunAsync(ListAsync).ConfigureAwait(true); }
    private async Task<bool> DiscardAsync() => !HasUnsavedChanges || await confirmation.ConfirmAsync("D_ConfirmDiscardChanges").ConfigureAwait(true);
    private async Task NewAsync()
    {
        if (!await DiscardAsync().ConfigureAwait(true)) return;
        Show(null); Kind = Can("Inventory.Transfers.Manage") ? InventoryDocumentKind.Transfer : InventoryDocumentKind.Count;
        Date = DateTime.Today; IsEditing = true; AddLine(new()); CaptureDraftBaseline(); NotifyCommands();
    }
    private async Task OpenAsync()
    {
        if (SelectedDocument?.Id is not { } id || !await DiscardAsync().ConfigureAwait(true)) return;
        await RunAsync(async t => Show(await client.GetAsync(CompanyId, BranchId, id, t).ConfigureAwait(true))).ConfigureAwait(true);
    }
    private async Task CancelAsync()
    {
        if (!await DiscardAsync().ConfigureAwait(true)) return;
        if (Current?.Id is { } id) await RunAsync(async t => Show(await client.GetAsync(CompanyId, BranchId, id, t).ConfigureAwait(true))).ConfigureAwait(true);
        else Show(null);
    }
    private void Show(InventoryDocumentDto? doc)
    {
        WorkspaceTab = 0;
        creationKey = Guid.NewGuid().ToString("N");
        foreach (var line in Lines) line.PropertyChanged -= LineChanged;
        Lines.Clear(); Current = doc; Kind = doc?.Kind ?? InventoryDocumentKind.Transfer; WarehouseId = doc?.WarehouseId; TargetWarehouseId = doc?.TargetWarehouseId;
        CostCenterId = doc?.CostCenterId; InventoryAccountId = doc?.InventoryAccountId; OffsetAccountId = doc?.OffsetAccountId;
        Date = doc?.DateUtc?.UtcDateTime.Date ?? DateTime.Today; Reason = doc?.Reason ?? string.Empty; ActionReason = Reason; IsEditing = false; draftBaseline = null;
        foreach (var l in doc?.Lines ?? []) AddLine(new() { ItemId = l.ItemId, UnitId = l.UnitId, Quantity = l.Quantity ?? "0.0000", SnapshotQuantity = l.SnapshotQuantity ?? string.Empty, Cost = l.Cost ?? string.Empty, OpeningUnitCost = l.OpeningUnitCost ?? string.Empty });
        actionKey = actionFingerprint = null; OnPropertyChanged(nameof(NumberDisplay)); NotifyCommands();
    }
    private void AddLine(InventoryDraftLine line) { line.PropertyChanged += LineChanged; Lines.Add(line); NotifyCommands(); }
    private void LineChanged(object? sender, PropertyChangedEventArgs args) => NotifyCommands();
    private bool Complete() => CanManage && Date.HasValue && WarehouseId.HasValue && !string.IsNullOrWhiteSpace(Reason) && Reason.Trim().Length <= 500
        && (!IsTransfer || TargetWarehouseId.HasValue && TargetWarehouseId != WarehouseId)
        && (IsTransfer || InventoryAccountId.HasValue && OffsetAccountId.HasValue && InventoryAccountId != OffsetAccountId)
        && (Kind != InventoryDocumentKind.InternalIssue || CostCenterId.HasValue) && Lines.Count is > 0 and <= 200
        && Lines.All(l => l.ItemId.HasValue && l.UnitId.HasValue && Parse(l.Quantity) is { } q && (q > 0 || Kind == InventoryDocumentKind.Count && q == 0))
        && Lines.All(l => string.IsNullOrWhiteSpace(l.OpeningUnitCost) || Kind == InventoryDocumentKind.Count && Parse(l.OpeningUnitCost) is >= 0m)
        && Lines.Select(l => l.ItemId).Distinct().Count() == Lines.Count;
    private static decimal? Parse(string text) => decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
        CultureInfo.InvariantCulture, out var n) && n is >= -999999999999999.9999m and <= 999999999999999.9999m && decimal.Round(n, 4) == n ? n : null;
    private async Task SaveAsync()
    {
        if (!Complete()) return;
        var request = new SaveInventoryDocumentRequest { Kind = Kind, WarehouseId = WarehouseId, TargetWarehouseId = IsTransfer ? TargetWarehouseId : null,
            CostCenterId = CostCenterId, InventoryAccountId = IsTransfer ? null : InventoryAccountId, OffsetAccountId = IsTransfer ? null : OffsetAccountId,
            DateUtc = new DateTimeOffset(DateTime.SpecifyKind(Date!.Value.Date, DateTimeKind.Utc)), Reason = Reason.Trim(), ExpectedVersion = Current?.Version, CreationKey = Current is null ? creationKey : null,
            Lines = Lines.Select(l => new InventoryDocumentLineInputDto { ItemId = l.ItemId, UnitId = l.UnitId, Quantity = Parse(l.Quantity)!.Value.ToString("F4", CultureInfo.InvariantCulture),
                OpeningUnitCost = string.IsNullOrWhiteSpace(l.OpeningUnitCost) ? null : Parse(l.OpeningUnitCost)!.Value.ToString("F4", CultureInfo.InvariantCulture) }).ToList() };
        await RunAsync(async t => { Show(await client.SaveAsync(CompanyId, BranchId, Current?.Id, request, t).ConfigureAwait(true)); await ListAsync(t).ConfigureAwait(true); }).ConfigureAwait(true);
    }
    public async Task<bool> SaveBeforeCloseAsync() { await SaveCommand.ExecuteAsync(null).ConfigureAwait(true); return !IsEditing; }
    private async Task ActAsync(bool reverse)
    {
        if (Current?.Id is not { } id || IsEditing || !CanManage || !await confirmation.ConfirmAsync(reverse ? "INV_ConfirmReverse" : "INV_ConfirmPost").ConfigureAwait(true)) return;
        var fingerprint = $"{id:N}:{Current.Version}:{reverse}:{ActionReason.Trim()}";
        if (actionFingerprint != fingerprint || actionKey is null) { actionFingerprint = fingerprint; actionKey = Guid.NewGuid().ToString("N"); }
        var request = new InventoryDocumentActionRequest { ExpectedVersion = Current.Version ?? string.Empty, IdempotencyKey = actionKey, Reason = ActionReason.Trim() };
        await RunAsync(async t =>
        {
            var result = reverse ? await client.ReverseAsync(CompanyId, BranchId, id, request, t).ConfigureAwait(true) : await client.PostAsync(CompanyId, BranchId, id, request, t).ConfigureAwait(true);
            Show(result); await ListAsync(t).ConfigureAwait(true); await LoadMovementsAsync(true, t).ConfigureAwait(true);
        }).ConfigureAwait(true);
    }
    private async Task LoadMovementsAsync(bool reset, CancellationToken token)
    {
        if (reset) movementPage = 1;
        DateTimeOffset? From(DateTime? value) => value.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(value.Value.Date, DateTimeKind.Utc)) : null;
        var result = await client.MovementsAsync(CompanyId, BranchId, FilterWarehouseId, FilterItemId, From(FromDate), ToDate.HasValue ? From(ToDate)!.Value.AddDays(1).AddTicks(-1) : null, movementPage, token).ConfigureAwait(true);
        token.ThrowIfCancellationRequested(); Movements.Clear(); foreach (var m in result.Items ?? []) Movements.Add(m);
        movementTotal = result.TotalCount ?? 0; OnPropertyChanged(nameof(MovementPageStatus)); Balances.Clear();
        foreach (var b in await client.BalancesAsync(CompanyId, BranchId, FilterWarehouseId, FilterItemId, token).ConfigureAwait(true)) Balances.Add(b);
        NotifyCommands();
    }
    private async Task MoveHistoryAsync(int delta) { movementPage += delta; await RunAsync(t => LoadMovementsAsync(false, t)).ConfigureAwait(true); }
    private async Task OpenSourceAsync()
    {
        if (SelectedMovement?.DocumentId is not { } id) return;
        var type = SelectedMovement.DocumentType ?? string.Empty;
        if (type.StartsWith("InventoryDocument", StringComparison.Ordinal) || type is "InventoryReversal" or "StockTransfer" or "StockAdjustment")
            await RunAsync(async t => Show(await client.GetAsync(CompanyId, BranchId, id, t).ConfigureAwait(true))).ConfigureAwait(true);
        else if ((type is "SalesInvoice" or "SalesInvoiceReturn" or "SalesInvoiceReversal") && Can("Sales.Invoices.View"))
            await navigator.NavigateAsync("sales-invoice-card", new OpenSalesInvoiceCardRequest(id)).ConfigureAwait(true);
        else if ((type is "PurchaseInvoice" or "PurchaseReturn") && Can("Purchases.Invoices.View"))
            await navigator.NavigateAsync("purchase-invoices", new Purchases.OpenPurchaseRequest(id)).ConfigureAwait(true);
        else Operation.SetError(Localization["INV_SourceHistoryOnly"]);
    }
    private bool CanOpenSource()
    {
        if (!HasContext || !Idle || IsEditing || SelectedMovement?.DocumentId is null) return false;
        return SelectedMovement.DocumentType switch
        {
            "InventoryDocumentIn" or "InventoryDocumentOut" or "InventoryReversal" or "StockTransfer" or "StockAdjustment" => true,
            "SalesInvoice" or "SalesInvoiceReturn" or "SalesInvoiceReversal" => Can("Sales.Invoices.View"),
            "PurchaseInvoice" or "PurchaseReturn" => Can("Purchases.Invoices.View"),
            _ => false
        };
    }
    protected override void ClearPage()
    {
        Show(null); Documents.Clear(); Movements.Clear(); Balances.Clear(); Warehouses.Clear(); Items.Clear(); Units.Clear(); Accounts.Clear(); CostCenters.Clear();
        Search = string.Empty; FilterWarehouseId = FilterItemId = null; SelectedDocument = null; SelectedMovement = null; SelectedLine = null;
        page = movementPage = 1; total = movementTotal = 0;
    }
    public void Localize() { OnPropertyChanged(nameof(KindOptions)); OnPropertyChanged(nameof(NumberDisplay)); }
    /// <summary>Opens the central posting page scoped to inventory documents with the current search.</summary>
    private Task BulkPostAsync() => navigator.NavigateAsync("posting-management",
        new DocumentProcessing.BulkPostingNavigation(BulkPostingDocumentType.InventoryDocument,
            Search: string.IsNullOrWhiteSpace(Search) ? null : Search.Trim()));

    protected override void NotifyCommands()
    {
        foreach (var c in new IRelayCommand?[] { RefreshCommand, NewCommand, OpenCommand, EditCommand, CancelCommand, SaveCommand, AddLineCommand, RemoveLineCommand,
            PostCommand, ReverseCommand, PreviousCommand, NextCommand, LoadMovementsCommand, SearchItemsCommand, PreviousMovementCommand, NextMovementCommand, OpenSourceCommand, OpenJournalCommand, BulkPostCommand, backToListCommand }) c?.NotifyCanExecuteChanged();
        foreach (var p in new[] { nameof(HasUnsavedChanges), nameof(CanSaveBeforeClose), nameof(CanManage), nameof(CanAnyManage), nameof(CanManagePostingBatches), nameof(IsTransfer), nameof(RequiresAccounts), nameof(IsEntryReadOnly), nameof(IsOpeningCostReadOnly), nameof(CanChangeKind), nameof(Lifecycle), nameof(DocumentStatusText), nameof(DocumentStatusKind), nameof(HasDocumentStatus), nameof(ShowDocumentList), nameof(IsCostCenterRequired) }) OnPropertyChanged(p);
    }
    partial void OnKindChanged(InventoryDocumentKind value)
    {
        if (value == InventoryDocumentKind.Transfer) { InventoryAccountId = null; OffsetAccountId = null; }
        else TargetWarehouseId = null;
        if (value != InventoryDocumentKind.Count) foreach (var line in Lines) line.OpeningUnitCost = string.Empty;
        NotifyCommands();
    }
    partial void OnWarehouseIdChanged(Guid? value) => NotifyCommands();
    partial void OnTargetWarehouseIdChanged(Guid? value) => NotifyCommands();
    partial void OnInventoryAccountIdChanged(Guid? value) => NotifyCommands();
    partial void OnOffsetAccountIdChanged(Guid? value) => NotifyCommands();
    partial void OnCostCenterIdChanged(Guid? value) => NotifyCommands();
    partial void OnDateChanged(DateTime? value) => NotifyCommands();
    partial void OnReasonChanged(string value) => NotifyCommands();
    partial void OnActionReasonChanged(string value) => NotifyCommands();
    partial void OnSelectedLineChanged(InventoryDraftLine? value) => NotifyCommands();
    partial void OnSelectedDocumentChanged(InventoryDocumentDto? value) => NotifyCommands();
    partial void OnSelectedMovementChanged(InventoryMovementAuditDto? value) => NotifyCommands();
    partial void OnItemSearchChanged(string value) => NotifyCommands();
    partial void OnSearchChanged(string value) => page = 1;
    partial void OnIsEditingChanged(bool value) => NotifyCommands();
}
