using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;
using PetraERP.Desktop.Controls;
using PetraERP.Desktop.Inventory;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Parties;
using PetraERP.Desktop.Patterns;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Sales;

public sealed partial class SalesInvoiceSummaryItem : ObservableObject
{
    public Guid Id { get; init; }
    /// <summary>Visual order in the list (1-based); independent of the accounting document number.</summary>
    public int RowNumber { get; set; }
    public string? InvoiceNumber { get; init; }
    public Guid? CustomerId { get; init; }
    public string? CustomerCode { get; init; }
    public string? CustomerName { get; init; }
    public Guid? WarehouseId { get; init; }
    public string? WarehouseCode { get; init; }
    public string? PatternCode { get; init; }
    public DateTimeOffset? InvoiceDateUtc { get; init; }
    public PaymentMethod? PaymentMethod { get; init; }
    public SalesInvoiceStatus? Status { get; set; }
    public string Version { get; set; } = string.Empty;
    public string GrandTotal { get; init; } = "0.0000";
    public string? SettledAmount { get; init; }
    public string? ReturnedAmount { get; init; }
    public string? OutstandingAmount { get; init; }
    public string CurrencyCode { get; init; } = string.Empty;

    public string SettledDisplay => string.IsNullOrWhiteSpace(SettledAmount) ? "0.0000" : SettledAmount;
    public string ReturnedDisplay => string.IsNullOrWhiteSpace(ReturnedAmount) ? "0.0000" : ReturnedAmount;
    public string OutstandingDisplay => string.IsNullOrWhiteSpace(OutstandingAmount) ? (Status == SalesInvoiceStatus.Posted && PaymentMethod == ApiClient.PaymentMethod.Credit ? GrandTotal : "0.0000") : OutstandingAmount;

    /// <summary>Stable reference shown for a document that has no official number yet; it is not an accounting number.</summary>
    public string DraftReference => "مسودة " + Id.ToString("N")[..8].ToUpperInvariant();

    public string InvoiceNumberDisplay => string.IsNullOrWhiteSpace(InvoiceNumber) ? DraftReference : InvoiceNumber;

    public string DateDisplay => InvoiceDateUtc?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;

    public string StatusDisplay => Status switch
    {
        SalesInvoiceStatus.Draft => "مسودة",
        SalesInvoiceStatus.Approved => "معتمدة",
        SalesInvoiceStatus.Posted => "مرحلة",
        SalesInvoiceStatus.Reversed => "معكوسة",
        SalesInvoiceStatus.Cancelled => "ملغاة",
        _ => "غير محدد",
    };

    public string PaymentMethodDisplay => PaymentMethod switch
    {
        ApiClient.PaymentMethod.Cash => "نقداً",
        ApiClient.PaymentMethod.Credit => "ذمم (آجل)",
        _ => "نقداً",
    };
}

public sealed partial class SalesInvoicesViewModel : PageViewModel, IShellPage
{
    public const string ViewPermission = "Sales.Invoices.View";
    public const string ManagePermission = "Sales.Invoices.Manage";
    public const string ApprovePermission = "Sales.Invoices.Approve";
    public const string PostPermission = "Sales.Invoices.Post";
    public const string ReversePermission = "Sales.Invoices.Reverse";

    private readonly ISalesInvoiceClient salesInvoiceClient;
    private readonly IPartiesClient partiesClient;
    private readonly IInventoryClient inventoryClient;
    private readonly ShellNavigator navigator;
    private readonly IUserConfirmation confirmation;
    private readonly IPatternsClient patternsClient;

    [ObservableProperty] private string search = string.Empty;
    [ObservableProperty] private SalesInvoiceStatus? selectedStatus;
    [ObservableProperty] private Guid? selectedCustomerId;
    [ObservableProperty] private Guid? selectedWarehouseId;
    [ObservableProperty] private DateTimeOffset? fromDate;
    [ObservableProperty] private DateTimeOffset? toDate;
    [ObservableProperty] private SalesInvoiceSummaryItem? selectedInvoice;
    [ObservableProperty] private int totalCount;
    [ObservableProperty] private bool isLoadingMore;

    public const int PageSize = 100;
    private int loadedPages;
    private bool listLoading;
    private bool reloadRequested;
    private bool referencesLoaded;
    // Bumped by every reload; a LoadMore that started under an older generation is discarded so stale pages never mix into the current list.
    private int listGeneration;
    // The filter set the CURRENT list was loaded with; LoadMore pages with exactly this, never with half-edited filter fields.
    private SalesInvoiceListScope activeScope = new();

    public sealed record StatusFilterOption(string Label, SalesInvoiceStatus? Value);

    public IReadOnlyList<StatusFilterOption> StatusFilterOptions { get; } =
    [
        new("كل الحالات", null),
        new("مسودة", SalesInvoiceStatus.Draft),
        new("معتمدة", SalesInvoiceStatus.Approved),
        new("مرحلة", SalesInvoiceStatus.Posted),
        new("معكوسة", SalesInvoiceStatus.Reversed),
        new("ملغاة", SalesInvoiceStatus.Cancelled),
    ];

    /// <summary>Calendar value for the "from" picker; the query uses the start of that day.</summary>
    public DateTime? FromDateValue
    {
        get => FromDate?.Date;
        set => FromDate = value is { } day ? new DateTimeOffset(DateTime.SpecifyKind(day.Date, DateTimeKind.Utc)) : null;
    }

    /// <summary>Calendar value for the "to" picker; the query includes the whole of that day.</summary>
    public DateTime? ToDateValue
    {
        get => ToDate?.Date;
        set => ToDate = value is { } day ? new DateTimeOffset(DateTime.SpecifyKind(day.Date, DateTimeKind.Utc)).AddDays(1).AddTicks(-1) : null;
    }

    /// <summary>True while more server pages exist beyond what the list shows.</summary>
    public bool HasMore => Invoices.Count < TotalCount;

    public SalesInvoicesViewModel(
        ISalesInvoiceClient salesInvoiceClient,
        IPartiesClient partiesClient,
        IInventoryClient inventoryClient,
        IPatternsClient patternsClient,
        ShellNavigator navigator,
        DesktopOperation operation,
        ILocalizationService localization,
        IUserConfirmation confirmation)
        : base(operation, localization)
    {
        this.salesInvoiceClient = salesInvoiceClient;
        this.partiesClient = partiesClient;
        this.inventoryClient = inventoryClient;
        this.patternsClient = patternsClient;
        this.navigator = navigator;
        this.confirmation = confirmation;

        RefreshCommand = new AsyncRelayCommand(LoadListAsync, () => HasContext && Idle);
        OpenCardCommand = new AsyncRelayCommand(OpenSelectedCardAsync, () => SelectedInvoice is not null && Idle);
        NewInvoiceCommand = new AsyncRelayCommand(NewInvoiceAsync, () => HasContext && Idle && CanManage);
        NewByPatternCommand = new AsyncRelayCommand<PatternLookupItem>(NewByPatternAsync, pattern => pattern is not null && HasContext && Idle && CanManage);
        ApproveInvoiceCommand = new AsyncRelayCommand(ApproveSelectedInvoiceAsync, () => SelectedInvoice is not null && SelectedInvoice.Status == SalesInvoiceStatus.Draft && Idle && CanApprove);
        BulkPostCommand = new AsyncRelayCommand(BulkPostAsync, () => HasContext && Idle && CanManagePostingBatches);
        ClearFiltersCommand = new RelayCommand(ClearFilters, () => HasContext);
    }

    protected override string RequiredPermission => ViewPermission;
    public bool CanManage => Can(ManagePermission);
    public bool CanApprove => Can(ApprovePermission);
    public bool CanPost => Can(PostPermission);
    public bool CanReverse => Can(ReversePermission);
    public bool CanManagePostingBatches => Can(DocumentProcessing.PostingManagementViewModel.ManagePermission);

    public ObservableCollection<SalesInvoiceSummaryItem> Invoices { get; } = [];
    public ObservableCollection<PartyLookupDto> Customers { get; } = [];
    public ObservableCollection<WarehouseDto> Warehouses { get; } = [];

    /// <summary>The server already applied every filter and the search term, so the list shows exactly what was returned.
    /// The same collection instance is bound for the whole life of the page (changes are incremental), so the grid keeps its scroll and selection.</summary>
    public IReadOnlyList<SalesInvoiceSummaryItem> VisibleInvoices => Invoices;

    /// <summary>Server-side customer search (beyond the first preloaded customers) for the filter picker.</summary>
    public Func<string, CancellationToken, Task<IReadOnlyList<LookupItem>>> SearchCustomersAsync => async (query, ct) =>
    {
        if (!HasContext) return [];
        // The workspace the request was issued for; a result is only applied while it is still the current one.
        var companyId = CompanyId;
        var branchId = BranchId;
        var epoch = contextEpoch;
        var parties = await partiesClient.LookupPartiesAsync(companyId, branchId, 50, query, PartyRole.Customer, ct).ConfigureAwait(false);
        var found = parties.Where(p => p.Id.HasValue).ToList();
        if (ct.IsCancellationRequested) return [];
        if (App.Current?.Dispatcher is { } dispatcher) _ = dispatcher.BeginInvoke(() => MergeCustomerSearchResult(found, companyId, branchId, epoch, ct));
        else MergeCustomerSearchResult(found, companyId, branchId, epoch, ct);
        return found.Select(p => new LookupItem(p.Id!.Value, p.Code ?? string.Empty, p.NameArabic ?? string.Empty, p.NameEnglish)).ToList();
    };

    // Bumped whenever the page is cleared for another company/branch/year, so late callbacks of the old workspace can be recognised.
    private int contextEpoch;

    /// <summary>Runs on the UI thread. A canceled, stale or workspace-switched search result never reaches the customer cache.</summary>
    internal void MergeCustomerSearchResult(IReadOnlyList<PartyLookupDto> found, Guid companyId, Guid branchId, int epoch, CancellationToken ct)
    {
        if (ct.IsCancellationRequested || epoch != contextEpoch || !HasContext || companyId != CompanyId || branchId != BranchId) return;
        foreach (var party in found)
            if (!Customers.Any(c => c.Id == party.Id)) Customers.Add(party);
    }

    internal int ContextEpoch => contextEpoch;
    internal (Guid Company, Guid Branch) Workspace => (CompanyId, BranchId);

    /// <summary>The filters of the list as shown, passed to the invoice card so previous/next walk this list only.</summary>
    public SalesInvoiceListScope CurrentScope => activeScope;

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand OpenCardCommand { get; }
    public AsyncRelayCommand NewInvoiceCommand { get; }
    /// <summary>One button per runnable sales pattern: opens a new invoice already fixed on that pattern.</summary>
    public AsyncRelayCommand<PatternLookupItem> NewByPatternCommand { get; }
    public ObservableCollection<PatternLookupItem> PatternShortcuts { get; } = [];
    public bool HasPatternShortcuts => PatternShortcuts.Count > 0;
    public AsyncRelayCommand ApproveInvoiceCommand { get; }
    public AsyncRelayCommand BulkPostCommand { get; }
    public RelayCommand ClearFiltersCommand { get; }

    private void ClearFilters()
    {
        // Several property setters would each trigger a reload; suppress them and reload once.
        suppressReload = true;
        try
        {
            Search = string.Empty;
            SelectedStatus = null;
            SelectedCustomerId = null;
            SelectedWarehouseId = null;
            FromDate = null;
            ToDate = null;
        }
        finally { suppressReload = false; }
        RunSearch();
    }

    private bool suppressReload;

    // Server-side sort of the WHOLE result (null = natural order). Part of the scope, so paging and card navigation follow it.
    private string? sortBy;
    private bool? sortDescending;

    public string? SortBy => sortBy;
    public bool? SortDescending => sortDescending;

    /// <summary>Sorts the full server result (not just the loaded rows) and reloads from page 1; null clears the sort.</summary>
    public Task ApplySortAsync(string? field, bool descending)
    {
        if (string.IsNullOrWhiteSpace(field)) { sortBy = null; sortDescending = null; }
        else { sortBy = field.Trim(); sortDescending = descending; }
        return LoadListAsync();
    }

    /// <summary>Server sort key for a grid column's SortMemberPath, or null when the column cannot be sorted on the server.</summary>
    public static string? ServerSortField(string? sortMemberPath) => sortMemberPath switch
    {
        "InvoiceNumber" => "InvoiceNumber",
        "PatternCode" => "Pattern",
        "InvoiceDateUtc" => "Date",
        "CustomerName" => "Customer",
        "WarehouseCode" => "Warehouse",
        "PaymentMethod" => "PaymentMethod",
        "GrandTotal" => "GrandTotal",
        "CurrencyCode" => "Currency",
        "Status" => "Status",
        _ => null,
    };

    protected override Task LoadAsync(object? parameter, CancellationToken cancellationToken)
    {
        NotifyPermissions();
        return LoadWithPatternsAsync(cancellationToken);
    }

    private async Task LoadWithPatternsAsync(CancellationToken cancellationToken)
    {
        var patternsTask = PatternShortcuts_LoadAsync(cancellationToken);
        await LoadInvoicesCoreAsync(cancellationToken).ConfigureAwait(true);
        await patternsTask.ConfigureAwait(true);
    }

    private async Task PatternShortcuts_LoadAsync(CancellationToken cancellationToken)
    {
        var patterns = await PatternShortcutLoader.LoadAsync(patternsClient, CompanyId, BranchId, BusinessOperationKind.SalesInvoice, cancellationToken).ConfigureAwait(true);
        PatternShortcuts.Clear();
        foreach (var pattern in patterns) PatternShortcuts.Add(pattern);
        OnPropertyChanged(nameof(HasPatternShortcuts));
    }

    protected override void ClearPage()
    {
        PatternShortcuts.Clear();
        OnPropertyChanged(nameof(HasPatternShortcuts));
        suppressReload = true;
        try
        {
            Invoices.Clear();
            Customers.Clear();
            Warehouses.Clear();
            SelectedInvoice = null;
            Search = string.Empty;
            SelectedStatus = null;
            SelectedCustomerId = null;
            SelectedWarehouseId = null;
            FromDate = null;
            ToDate = null;
            TotalCount = 0;
        }
        finally { suppressReload = false; }
        referencesLoaded = false;
        loadedPages = 0;
        listGeneration++;
        contextEpoch++;
        sortBy = null;
        sortDescending = null;
        activeScope = new();
        OnPropertyChanged(nameof(HasMore));
    }

    public void Localize()
    {
        NotifyCommands();
    }

    private CancellationTokenSource? searchDebounceCts;

    partial void OnSearchChanged(string value)
    {
        if (suppressReload) return;
        searchDebounceCts?.Cancel();
        searchDebounceCts?.Dispose();
        var cts = searchDebounceCts = new CancellationTokenSource();
        _ = DebounceSearchAsync(cts.Token);
    }

    private async Task DebounceSearchAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(80, token).ConfigureAwait(true);
            if (!token.IsCancellationRequested) RunSearch();
        }
        catch (OperationCanceledException) { }
    }
    partial void OnSelectedStatusChanged(SalesInvoiceStatus? value) => RunSearch();
    partial void OnSelectedCustomerIdChanged(Guid? value) => RunSearch();
    partial void OnSelectedWarehouseIdChanged(Guid? value) => RunSearch();
    partial void OnFromDateChanged(DateTimeOffset? value) { OnPropertyChanged(nameof(FromDateValue)); RunSearch(); }
    partial void OnToDateChanged(DateTimeOffset? value) { OnPropertyChanged(nameof(ToDateValue)); RunSearch(); }
    partial void OnSelectedInvoiceChanged(SalesInvoiceSummaryItem? value) => NotifyCommands();

    private void RunSearch()
    {
        if (suppressReload) return;
        if (HasContext)
        {
            // A change made while a load is in flight is not dropped: the running load repeats once it finishes.
            if (listLoading) { reloadRequested = true; return; }
            _ = LoadInvoicesAsync();
        }
    }

    protected override void NotifyCommands()
    {
        OpenCardCommand?.NotifyCanExecuteChanged();
        NewInvoiceCommand?.NotifyCanExecuteChanged();
        NewByPatternCommand?.NotifyCanExecuteChanged();
        ApproveInvoiceCommand?.NotifyCanExecuteChanged();
        BulkPostCommand?.NotifyCanExecuteChanged();
        ClearFiltersCommand?.NotifyCanExecuteChanged();
    }

    private void NotifyPermissions()
    {
        OnPropertyChanged(nameof(CanManage));
        OnPropertyChanged(nameof(CanApprove));
        OnPropertyChanged(nameof(CanPost));
        OnPropertyChanged(nameof(CanReverse));
        OnPropertyChanged(nameof(CanManagePostingBatches));
        NotifyCommands();
    }

    public Task LoadInvoicesAsync() => LoadListAsync();

    private async Task LoadListAsync()
    {
        if (listLoading) { reloadRequested = true; return; }
        listLoading = true;
        try
        {
            do
            {
                reloadRequested = false;
                await RunAsync(LoadInvoicesCoreAsync).ConfigureAwait(true);
            }
            while (reloadRequested && HasContext);
        }
        finally { listLoading = false; }
    }

    private async Task EnsureReferencesAsync(CancellationToken cancellationToken)
    {
        if (referencesLoaded) return;
        try
        {
            var customerList = await partiesClient.LookupPartiesAsync(CompanyId, BranchId, 200, null, PartyRole.Customer, cancellationToken).ConfigureAwait(true);
            var warehouseList = await inventoryClient.ListWarehousesAsync(CompanyId, BranchId, null, cancellationToken).ConfigureAwait(true);
            Customers.Clear();
            foreach (var c in customerList) Customers.Add(c);
            Warehouses.Clear();
            foreach (var w in warehouseList) Warehouses.Add(w);
            referencesLoaded = true;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            // The lists are filter conveniences only; the invoice list itself must still load, and the next load retries.
        }
    }

    private SalesInvoiceListScope CaptureScope() =>
        new(string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(), SelectedCustomerId, SelectedWarehouseId, SelectedStatus, FromDate, ToDate, sortBy, sortBy is null ? null : sortDescending);

    private async Task<SalesInvoicePageResponse> FetchPageAsync(SalesInvoiceListScope scope, int page, CancellationToken cancellationToken) =>
        await salesInvoiceClient.SearchSalesInvoicesAsync(
            CompanyId, BranchId, page, PageSize, scope.Search,
            scope.CustomerId, scope.WarehouseId, null, scope.Status, scope.FromDate, scope.ToDate, scope.SortBy, scope.SortBy is null ? null : scope.SortDescending, cancellationToken).ConfigureAwait(true);

    private static bool SameRow(SalesInvoiceSummaryItem a, SalesInvoiceSummaryItem b) =>
        a.Id == b.Id && a.Version == b.Version && a.Status == b.Status && a.GrandTotal == b.GrandTotal
        && a.SettledAmount == b.SettledAmount && a.ReturnedAmount == b.ReturnedAmount && a.OutstandingAmount == b.OutstandingAmount
        && a.InvoiceNumber == b.InvoiceNumber && a.CustomerName == b.CustomerName;

    /// <summary>Applies a fresh first page by changing only the rows that differ (common prefix kept), instead of clearing and rebuilding the list.</summary>
    private void ReplaceItems(List<SalesInvoiceSummaryItem> fresh)
    {
        var common = 0;
        while (common < Invoices.Count && common < fresh.Count && SameRow(Invoices[common], fresh[common])) common++;
        for (var i = Invoices.Count - 1; i >= common; i--) Invoices.RemoveAt(i);
        for (var i = common; i < fresh.Count; i++) Invoices.Add(fresh[i]);
        ResolveSelection();
    }

    /// <summary>
    /// Re-binds the selection to the object that is actually in the freshly loaded collection (the same id may be a NEW object),
    /// or clears it when the row is gone (filtered out / deleted); always refreshes Approve/Open command state.
    /// </summary>
    private void ResolveSelection()
    {
        var selectedId = SelectedInvoice?.Id;
        var resolved = selectedId is { } id ? Invoices.FirstOrDefault(i => i.Id == id) : null;
        if (!ReferenceEquals(SelectedInvoice, resolved)) SelectedInvoice = resolved;
        NotifyCommands();
    }

    private static SalesInvoiceSummaryItem ToItem(SalesInvoiceSummaryDto item, int rowNumber) => new()
    {
        RowNumber = rowNumber,
        Id = item.Id ?? Guid.Empty,
        InvoiceNumber = item.InvoiceNumber,
        CustomerId = item.CustomerId,
        CustomerCode = item.CustomerCode,
        CustomerName = item.CustomerName,
        WarehouseId = item.WarehouseId,
        WarehouseCode = item.WarehouseCode,
        PatternCode = item.PatternCode,
        InvoiceDateUtc = item.InvoiceDateUtc,
        PaymentMethod = item.PaymentMethod,
        Status = item.Status,
        GrandTotal = item.GrandTotal ?? "0.0000",
        SettledAmount = item.SettledAmount,
        ReturnedAmount = item.ReturnedAmount,
        OutstandingAmount = item.OutstandingAmount,
        CurrencyCode = item.CurrencyCode ?? string.Empty,
        Version = item.Version ?? string.Empty,
    };

    private async Task LoadInvoicesCoreAsync(CancellationToken cancellationToken)
    {
        if (!HasContext) return;

        await EnsureReferencesAsync(cancellationToken).ConfigureAwait(true);
        var scope = CaptureScope();
        var generation = ++listGeneration;
        var response = await FetchPageAsync(scope, 1, cancellationToken).ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested();
        if (generation != listGeneration) return;

        var fresh = (response.Items ?? []).Select((item, index) => ToItem(item, index + 1)).ToList();
        ReplaceItems(fresh);
        activeScope = scope;
        loadedPages = 1;
        TotalCount = response.TotalCount ?? Invoices.Count;
        OnPropertyChanged(nameof(HasMore));
    }

    /// <summary>Appends the next server page; called when the grid is scrolled near its end. Never shows a pager.</summary>
    public async Task LoadMoreAsync()
    {
        if (!HasContext || listLoading || IsLoadingMore || !HasMore) return;
        IsLoadingMore = true;
        var token = Token;
        var generation = listGeneration;
        var scope = activeScope;
        try
        {
            var response = await FetchPageAsync(scope, loadedPages + 1, token).ConfigureAwait(true);
            token.ThrowIfCancellationRequested();
            if (generation != listGeneration) return; // a newer filter/refresh replaced the list while this page was in flight
            var known = Invoices.Select(i => i.Id).ToHashSet();
            foreach (var item in response.Items ?? [])
                if (item.Id is { } id && known.Add(id)) Invoices.Add(ToItem(item, Invoices.Count + 1));
            loadedPages++;
            TotalCount = response.TotalCount ?? TotalCount;
            OnPropertyChanged(nameof(HasMore));
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            Operation.SetError(Connection.DesktopErrors.Key(exception));
        }
        finally { IsLoadingMore = false; }
    }

    /// <summary>Opens the central posting page with this list's document type, date range and search already applied.</summary>
    private Task BulkPostAsync() => navigator.NavigateAsync("posting-management",
        new DocumentProcessing.BulkPostingNavigation(BulkPostingDocumentType.SalesInvoice, FromDate, ToDate, Search));

    private async Task OpenSelectedCardAsync()
    {
        if (SelectedInvoice is null) return;
        await navigator.NavigateAsync("sales-invoice-card", new OpenSalesInvoiceCardRequest(SelectedInvoice.Id, CurrentScope)).ConfigureAwait(true);
    }

    private async Task NewInvoiceAsync()
    {
        await navigator.NavigateAsync("sales-invoice-card", new NewSalesInvoiceCardRequest()).ConfigureAwait(true);
    }

    private Task NewByPatternAsync(PatternLookupItem? pattern) =>
        pattern is null ? Task.CompletedTask : navigator.NavigateAsync("sales-invoice-card", new NewSalesInvoiceCardRequest(pattern.PatternId));

    private async Task ApproveSelectedInvoiceAsync()
    {
        if (SelectedInvoice is null || !HasContext || !CanApprove) return;

        if (!await confirmation.ConfirmAsync("D_ConfirmApproveInvoice").ConfigureAwait(true)) return;

        try
        {
            var updated = await salesInvoiceClient.ApproveSalesInvoiceAsync(CompanyId, BranchId, SelectedInvoice.Id, SelectedInvoice.Version, CancellationToken.None).ConfigureAwait(true);
            SelectedInvoice.Status = updated.Status;
            SelectedInvoice.Version = updated.Version ?? string.Empty;
            NotifyCommands();
        }
        catch (Exception ex)
        {
            Operation.SetError(Connection.DesktopErrors.Key(ex));
        }
    }
}
