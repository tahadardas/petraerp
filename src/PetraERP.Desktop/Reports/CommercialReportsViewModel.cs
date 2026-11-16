using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;
using PetraERP.Desktop.Accounting;
using PetraERP.Desktop.Inventory;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Parties;
using PetraERP.Desktop.Purchases;
using PetraERP.Desktop.Shell;
using PetraERP.Desktop.Treasury;

namespace PetraERP.Desktop.Reports;

public sealed record OpenPartyStatementRequest(Guid PartyId, PartyStatementKind Kind, string? CurrencyCode = null);

/// <summary>Opens the reports page directly on one tab, optionally with the party kind the catalogue entry implies.</summary>
public sealed record OpenCommercialReportRequest(CommercialReportTab Tab, PartyStatementKind? PartyKind = null);

/// <summary>Report tabs in display order; the selected tab decides which server report Run executes.</summary>
public enum CommercialReportTab
{
    IncomeStatement = 0,
    BalanceSheet = 1,
    PartyStatement = 2,
    Aging = 3,
    SalesAnalysis = 4,
    PurchaseAnalysis = 5,
    InventoryValuation = 6,
    InventoryMovements = 7,
    CashStatement = 8,
}

/// <summary>
/// Financial and commercial reports (PT-118..PT-124). The page only collects parameters, renders server results and drills to
/// source documents; it never recomputes report figures.
/// </summary>
public sealed partial class CommercialReportsViewModel : PageViewModel
{
    public const string ViewPermission = "Ledger.Reports.View";

    private readonly IReportsClient reports;
    private readonly IPartiesClient partiesClient;
    private readonly IInventoryClient inventoryClient;
    private readonly ShellNavigator navigator;
    private int generation;

    [ObservableProperty] private int selectedTab;
    [ObservableProperty] private DateTime? fromDate;
    [ObservableProperty] private DateTime? toDate;
    [ObservableProperty] private DateTime? asOfDate;
    [ObservableProperty] private bool consolidated;
    [ObservableProperty] private string currencyCode = string.Empty;
    [ObservableProperty] private ChoiceOption<PartyStatementKind>? partyKind;
    [ObservableProperty] private Guid? partyId;
    [ObservableProperty] private string partySearch = string.Empty;
    [ObservableProperty] private ChoiceOption<SalesAnalysisGrouping>? salesGrouping;
    [ObservableProperty] private ChoiceOption<PurchaseAnalysisGrouping>? purchaseGrouping;
    [ObservableProperty] private Guid? itemId;
    [ObservableProperty] private Guid? warehouseId;
    [ObservableProperty] private Guid? cashAccountId;

    [ObservableProperty] private IncomeStatementResponse? incomeStatement;
    [ObservableProperty] private BalanceSheetResponse? balanceSheet;
    [ObservableProperty] private PartyStatementResponse? partyStatement;
    [ObservableProperty] private AgingResponse? aging;
    [ObservableProperty] private SalesAnalysisResponse? salesAnalysis;
    [ObservableProperty] private PurchaseAnalysisResponse? purchaseAnalysis;
    [ObservableProperty] private InventoryValuationResponse? inventoryValuation;
    [ObservableProperty] private InventoryMovementReportResponse? inventoryMovements;
    [ObservableProperty] private CashStatementResponse? cashStatement;

    [ObservableProperty] private StatementAccountLineDto? selectedIncomeStatementLine;
    [ObservableProperty] private StatementAccountLineDto? selectedBalanceSheetLine;
    [ObservableProperty] private PartyStatementLineDto? selectedStatementLine;
    [ObservableProperty] private AgingDocumentDto? selectedAgingDocument;
    [ObservableProperty] private InventoryMovementLineDto? selectedMovementLine;
    [ObservableProperty] private CashStatementLineDto? selectedCashLine;

    public CommercialReportsViewModel(IReportsClient reports, IPartiesClient partiesClient, IInventoryClient inventoryClient, ShellNavigator navigator,
        DesktopOperation operation, ILocalizationService localization) : base(operation, localization)
    {
        this.reports = reports; this.partiesClient = partiesClient; this.inventoryClient = inventoryClient; this.navigator = navigator;
        RunCommand = new AsyncRelayCommand(() => RunAsync(RunReportAsync), CanRun);
        SearchPartiesCommand = new AsyncRelayCommand(() => RunAsync(SearchPartiesAsync), () => HasContext && Idle && CanParties);
        OpenDocumentCommand = new AsyncRelayCommand(OpenDocumentAsync, CanOpenDocument);
        InitializeLedgerStatementCommand();
        BuildOptions();
    }

    protected override string RequiredPermission => ViewPermission;

    public AsyncRelayCommand RunCommand { get; }
    public AsyncRelayCommand SearchPartiesCommand { get; }
    public AsyncRelayCommand OpenDocumentCommand { get; }

    /// <summary>
    /// This tab lists the party's commercial documents. Manual journals booked on the party's control account are not documents, so the
    /// ledger statement is a different (and authoritative) answer; the button opens it instead of leaving the two numbers to be compared.
    /// </summary>
    public AsyncRelayCommand OpenLedgerStatementCommand { get; private set; } = null!;

    private void InitializeLedgerStatementCommand() =>
        OpenLedgerStatementCommand = new AsyncRelayCommand(() => PartyId is { } party
                ? navigator.NavigateAsync("financial-reports", new OpenPartyStatementRequest(party, PartyKind!.Value,
                    CurrencyCode.Trim().Length == 3 ? CurrencyCode.Trim().ToUpperInvariant() : null))
                : Task.CompletedTask,
            () => HasContext && Idle && ShowParty && PartyId is not null && PartyKind is not null);

    public ObservableCollection<PartyLookupDto> Customers { get; } = [];
    public ObservableCollection<PartyLookupDto> Suppliers { get; } = [];
    public ObservableCollection<ItemLookupDto> Items { get; } = [];
    public ObservableCollection<WarehouseDto> Warehouses { get; } = [];
    public ObservableCollection<CashAccountDto> CashAccounts { get; } = [];
    public IReadOnlyList<ChoiceOption<PartyStatementKind>> PartyKinds { get; private set; } = [];
    public IReadOnlyList<ChoiceOption<SalesAnalysisGrouping>> SalesGroupings { get; private set; } = [];
    public IReadOnlyList<ChoiceOption<PurchaseAnalysisGrouping>> PurchaseGroupings { get; private set; } = [];
    public IEnumerable<PartyLookupDto> Parties => PartyKind?.Value == PartyStatementKind.Supplier ? Suppliers : Customers;

    /// <summary>Tabs whose source module the user may read; the server enforces the same permissions on every route.</summary>
    public bool CanParties => Can("Parties.Directory.View");
    public bool CanSales => Can("Sales.Invoices.View");
    public bool CanPurchases => Can("Purchases.Invoices.View");
    public bool CanInventory => Can("Inventory.Items.View");
    public bool CanMovements => Can("Inventory.Movements.View");
    public CommercialReportTab Tab => (CommercialReportTab)SelectedTab;

    private void BuildOptions()
    {
        var kind = PartyKind?.Value; var sales = SalesGrouping?.Value; var purchase = PurchaseGrouping?.Value;
        PartyKinds = [new(PartyStatementKind.Customer, Localization["RPT_Customer"]), new(PartyStatementKind.Supplier, Localization["RPT_Supplier"])];
        SalesGroupings = [new(SalesAnalysisGrouping.Customer, Localization["RPT_ByCustomer"]), new(SalesAnalysisGrouping.Item, Localization["RPT_ByItem"]),
            new(SalesAnalysisGrouping.Pattern, Localization["RPT_ByPattern"])];
        PurchaseGroupings = [new(PurchaseAnalysisGrouping.Supplier, Localization["RPT_BySupplier"]), new(PurchaseAnalysisGrouping.Item, Localization["RPT_ByItem"]),
            new(PurchaseAnalysisGrouping.Pattern, Localization["RPT_ByPattern"])];
        PartyKind = PartyKinds.FirstOrDefault(o => o.Value == kind) ?? PartyKinds[0];
        SalesGrouping = SalesGroupings.FirstOrDefault(o => o.Value == sales) ?? SalesGroupings[0];
        PurchaseGrouping = PurchaseGroupings.FirstOrDefault(o => o.Value == purchase) ?? PurchaseGroupings[0];
        OnPropertyChanged(nameof(PartyKinds)); OnPropertyChanged(nameof(SalesGroupings)); OnPropertyChanged(nameof(PurchaseGroupings));
    }

    protected override async Task LoadAsync(object? parameter, CancellationToken cancellationToken)
    {
        var requested = ++generation;
        var today = DateTime.Today;
        FromDate ??= new DateTime(today.Year, 1, 1);
        ToDate ??= today;
        AsOfDate ??= today;
        if (string.IsNullOrWhiteSpace(CurrencyCode)) CurrencyCode = Context?.BaseCurrencyCode ?? string.Empty;
        BuildOptions();
        // Lookups are loaded only for modules the user may read; the server re-checks every request.
        Customers.Clear(); Suppliers.Clear(); Items.Clear(); Warehouses.Clear(); CashAccounts.Clear();
        if (CanParties)
        {
            foreach (var c in await partiesClient.LookupPartiesAsync(CompanyId, BranchId, 500, null, PartyRole.Customer, cancellationToken).ConfigureAwait(true)) Customers.Add(c);
            foreach (var s in await partiesClient.LookupPartiesAsync(CompanyId, BranchId, 500, null, PartyRole.Supplier, cancellationToken).ConfigureAwait(true)) Suppliers.Add(s);
        }
        if (requested != generation) return;
        if (CanInventory)
        {
            foreach (var i in await inventoryClient.LookupItemsAsync(CompanyId, BranchId, 500, null, cancellationToken).ConfigureAwait(true)) Items.Add(i);
            foreach (var w in await inventoryClient.ListWarehousesAsync(CompanyId, BranchId, BranchId, cancellationToken).ConfigureAwait(true)) Warehouses.Add(w);
        }
        if (requested != generation) return;
        foreach (var a in await reports.CashAccountsAsync(CompanyId, BranchId, cancellationToken).ConfigureAwait(true)) CashAccounts.Add(a);
        if (parameter is OpenCommercialReportRequest tabRequest)
        {
            SelectedTab = (int)tabRequest.Tab;
            if (tabRequest.PartyKind is { } requestedKind) PartyKind = PartyKinds.Single(o => o.Value == requestedKind);
        }
        else if (parameter is OpenPartyStatementRequest request && CanParties)
        {
            SelectedTab = (int)CommercialReportTab.PartyStatement;
            PartyKind = PartyKinds.Single(o => o.Value == request.Kind);
            // A party opened from its card must remain selectable beyond the initial bounded lookup result.
            var options = request.Kind == PartyStatementKind.Supplier ? Suppliers : Customers;
            if (options.All(p => p.Id != request.PartyId))
            {
                var party = await partiesClient.GetPartyAsync(CompanyId, BranchId, request.PartyId, cancellationToken).ConfigureAwait(true);
                options.Add(new PartyLookupDto { Id = party.Id, Code = party.Code, NameArabic = party.NameArabic,
                    NameEnglish = party.NameEnglish, Role = party.Role, IsActive = party.IsActive });
            }
            PartyId = request.PartyId;
            if (!string.IsNullOrWhiteSpace(request.CurrencyCode)) CurrencyCode = request.CurrencyCode;
        }
        OnPropertyChanged(nameof(Parties));
        NotifyCommands();
    }

    private bool CanRun()
    {
        if (!HasContext || !Idle) return false;
        var range = FromDate is { } from && ToDate is { } to && from <= to;
        return Tab switch
        {
            CommercialReportTab.IncomeStatement => range,
            CommercialReportTab.BalanceSheet => AsOfDate is not null,
            CommercialReportTab.PartyStatement => CanParties && range && PartyId is not null && CurrencyCode.Trim().Length == 3,
            CommercialReportTab.Aging => CanParties && AsOfDate is not null && CurrencyCode.Trim().Length == 3,
            CommercialReportTab.SalesAnalysis => CanSales && range,
            CommercialReportTab.PurchaseAnalysis => CanPurchases && range,
            CommercialReportTab.InventoryValuation => CanInventory && AsOfDate is not null,
            CommercialReportTab.InventoryMovements => CanMovements && range && ItemId is not null,
            CommercialReportTab.CashStatement => range && CashAccountId is not null,
            _ => false,
        };
    }

    private async Task SearchPartiesAsync(CancellationToken cancellationToken)
    {
        var kind = PartyKind?.Value == PartyStatementKind.Supplier ? PartyRole.Supplier : PartyRole.Customer;
        var result = await partiesClient.LookupPartiesAsync(CompanyId, BranchId, 100, PartySearch, kind, cancellationToken).ConfigureAwait(true);
        var rows = kind == PartyRole.Supplier ? Suppliers : Customers;
        var selected = rows.FirstOrDefault(p => p.Id == PartyId);
        rows.Clear();
        foreach (var row in result) rows.Add(row);
        if (selected is not null && rows.All(p => p.Id != selected.Id)) rows.Add(selected);
        OnPropertyChanged(nameof(Parties));
    }

    private async Task RunReportAsync(CancellationToken cancellationToken)
    {
        if (!CanRun()) return;
        var requested = ++generation;
        var from = DateOnly.FromDateTime(FromDate ?? DateTime.Today); var to = DateOnly.FromDateTime(ToDate ?? DateTime.Today);
        var asOf = DateOnly.FromDateTime(AsOfDate ?? DateTime.Today);
        var currency = CurrencyCode.Trim().ToUpperInvariant();
        switch (Tab)
        {
            case CommercialReportTab.IncomeStatement:
                var income = await reports.IncomeStatementAsync(CompanyId, BranchId, from, to, Consolidated, cancellationToken).ConfigureAwait(true);
                if (requested == generation) IncomeStatement = income;
                break;
            case CommercialReportTab.BalanceSheet:
                var balance = await reports.BalanceSheetAsync(CompanyId, BranchId, asOf, Consolidated, cancellationToken).ConfigureAwait(true);
                if (requested == generation) BalanceSheet = balance;
                break;
            case CommercialReportTab.PartyStatement:
                var statement = await reports.PartyStatementAsync(CompanyId, BranchId, PartyId!.Value, PartyKind!.Value, currency, from, to, cancellationToken).ConfigureAwait(true);
                if (requested == generation) PartyStatement = statement;
                break;
            case CommercialReportTab.Aging:
                var aged = await reports.AgingAsync(CompanyId, BranchId, PartyKind!.Value, currency, asOf, cancellationToken).ConfigureAwait(true);
                if (requested == generation) Aging = aged;
                break;
            case CommercialReportTab.SalesAnalysis:
                var sales = await reports.SalesAnalysisAsync(CompanyId, BranchId, from, to, SalesGrouping!.Value, ItemId, cancellationToken).ConfigureAwait(true);
                if (requested == generation) SalesAnalysis = sales;
                break;
            case CommercialReportTab.PurchaseAnalysis:
                var purchases = await reports.PurchaseAnalysisAsync(CompanyId, BranchId, from, to, PurchaseGrouping!.Value, ItemId, cancellationToken).ConfigureAwait(true);
                if (requested == generation) PurchaseAnalysis = purchases;
                break;
            case CommercialReportTab.InventoryValuation:
                var valuation = await reports.InventoryValuationAsync(CompanyId, BranchId, asOf, WarehouseId, cancellationToken).ConfigureAwait(true);
                if (requested == generation) InventoryValuation = valuation;
                break;
            case CommercialReportTab.InventoryMovements:
                var movements = await reports.InventoryMovementsAsync(CompanyId, BranchId, ItemId!.Value, WarehouseId, from, to, cancellationToken).ConfigureAwait(true);
                if (requested == generation) InventoryMovements = movements;
                break;
            case CommercialReportTab.CashStatement:
                var cash = await reports.CashStatementAsync(CompanyId, BranchId, CashAccountId!.Value, from, to, Consolidated, cancellationToken).ConfigureAwait(true);
                if (requested == generation) CashStatement = cash;
                break;
        }
    }

    private (string Type, Guid Id)? SelectedDocument() => Tab switch
    {
        CommercialReportTab.IncomeStatement when SelectedIncomeStatementLine is { AccountId: { } aid } => ("Account", aid),
        CommercialReportTab.BalanceSheet when SelectedBalanceSheetLine is { AccountId: { } aid } => ("Account", aid),
        CommercialReportTab.PartyStatement when SelectedStatementLine is { DocumentType: { } dt, DocumentId: { } di } => (dt, di),
        CommercialReportTab.Aging when SelectedAgingDocument is { InvoiceId: { } ii } d => (Aging?.Kind == PartyStatementKind.Supplier ? "PurchaseInvoice" : "SalesInvoice", ii),
        CommercialReportTab.InventoryMovements when SelectedMovementLine is { DocumentType: { } mt, DocumentId: { } id } => (mt, id),
        CommercialReportTab.CashStatement when SelectedCashLine is { } c => c is { SourceType: { } type, SourceId: { } source } ? (type, source) : (c.JournalId.HasValue ? ("Journal", c.JournalId.Value) : null),
        _ => null,
    };

    private bool CanOpenDocument()
    {
        if (!HasContext || !Idle || SelectedDocument() is not { } document) return false;
        var type = document.Type;
        if (type == "Account") return Can("Ledger.Reports.View");
        if (type.StartsWith("Sales", StringComparison.Ordinal)) return Can("Sales.Invoices.View");
        if (type.StartsWith("Purchase", StringComparison.Ordinal)) return Can("Purchases.Invoices.View");
        if (type.StartsWith("ReceiptVoucher", StringComparison.Ordinal) || type.StartsWith("PaymentVoucher", StringComparison.Ordinal))
            return Can("Treasury.Vouchers.View");
        if (type.StartsWith("InventoryDocument", StringComparison.Ordinal)) return Can("Inventory.Movements.View");
        return type == "Journal" && Can("Ledger.Journals.View");
    }

    /// <summary>Drills to the authoritative source document; the shell re-checks the target route's permission.</summary>
    private Task OpenDocumentAsync()
    {
        if (!CanOpenDocument() || SelectedDocument() is not { } document) return Task.CompletedTask;
        var (type, id) = document;
        if (type == "Account")
        {
            var from = FromDate.HasValue ? DateOnly.FromDateTime(FromDate.Value) : (DateOnly?)null;
            var to = ToDate.HasValue ? DateOnly.FromDateTime(ToDate.Value) : (DateOnly?)null;
            return navigator.NavigateAsync("account-ledger", new OpenAccountLedgerRequest(id, from, to, Consolidated));
        }
        if (type.StartsWith("Sales", StringComparison.Ordinal)) return navigator.NavigateAsync("sales-invoice-card", new OpenSalesInvoiceCardRequest(id));
        if (type.StartsWith("Purchase", StringComparison.Ordinal)) return navigator.NavigateAsync("purchase-invoices", new OpenPurchaseRequest(id));
        if (type.StartsWith("ReceiptVoucher", StringComparison.Ordinal) || type.StartsWith("PaymentVoucher", StringComparison.Ordinal))
            return navigator.NavigateAsync("treasury-vouchers", new OpenVoucherRequest(id));
        if (type.StartsWith("InventoryDocument", StringComparison.Ordinal)) return navigator.NavigateAsync("inventory-operations", new OpenInventoryDocumentRequest(id));
        if (type == "Journal") return navigator.NavigateAsync("journals", new OpenJournalRequest(id));
        return Task.CompletedTask;
    }

    protected override void ClearPage()
    {
        generation++;
        Customers.Clear(); Suppliers.Clear(); Items.Clear(); Warehouses.Clear(); CashAccounts.Clear();
        IncomeStatement = null; BalanceSheet = null; PartyStatement = null; Aging = null; SalesAnalysis = null; PurchaseAnalysis = null;
        InventoryValuation = null; InventoryMovements = null; CashStatement = null;
        PartyId = null; ItemId = null; WarehouseId = null; CashAccountId = null; Consolidated = false; CurrencyCode = string.Empty;
        PartySearch = string.Empty;
    }

    protected override void NotifyCommands()
    {
        RunCommand?.NotifyCanExecuteChanged(); OpenDocumentCommand?.NotifyCanExecuteChanged(); SearchPartiesCommand?.NotifyCanExecuteChanged();
        OpenLedgerStatementCommand?.NotifyCanExecuteChanged();
        foreach (var name in new[] { nameof(CanParties), nameof(CanSales), nameof(CanPurchases), nameof(CanInventory), nameof(CanMovements) })
            OnPropertyChanged(name);
    }

    // Only the parameters the selected report actually uses are shown (they match CanRun and RunReportAsync above).
    public bool ShowDateRange => Tab is CommercialReportTab.IncomeStatement or CommercialReportTab.PartyStatement or CommercialReportTab.SalesAnalysis
        or CommercialReportTab.PurchaseAnalysis or CommercialReportTab.InventoryMovements or CommercialReportTab.CashStatement;
    public bool ShowAsOf => Tab is CommercialReportTab.BalanceSheet or CommercialReportTab.Aging or CommercialReportTab.InventoryValuation;
    public bool ShowCurrency => Tab is CommercialReportTab.PartyStatement or CommercialReportTab.Aging;
    public bool ShowPartyKind => Tab is CommercialReportTab.PartyStatement or CommercialReportTab.Aging;
    public bool ShowParty => Tab == CommercialReportTab.PartyStatement;
    public bool ShowItem => Tab is CommercialReportTab.SalesAnalysis or CommercialReportTab.PurchaseAnalysis or CommercialReportTab.InventoryMovements;
    public bool ShowWarehouse => Tab is CommercialReportTab.InventoryValuation or CommercialReportTab.InventoryMovements;
    public bool ShowCashAccount => Tab == CommercialReportTab.CashStatement;
    public bool ShowConsolidated => Tab is CommercialReportTab.IncomeStatement or CommercialReportTab.BalanceSheet or CommercialReportTab.CashStatement;

    private void NotifyParameterVisibility()
    {
        foreach (var name in new[] { nameof(ShowDateRange), nameof(ShowAsOf), nameof(ShowCurrency), nameof(ShowPartyKind), nameof(ShowParty),
                     nameof(ShowItem), nameof(ShowWarehouse), nameof(ShowCashAccount), nameof(ShowConsolidated) })
            OnPropertyChanged(name);
    }

    partial void OnSelectedTabChanged(int value) { OnPropertyChanged(nameof(Tab)); NotifyParameterVisibility(); NotifyCommands(); }
    partial void OnFromDateChanged(DateTime? value) => NotifyCommands();
    partial void OnToDateChanged(DateTime? value) => NotifyCommands();
    partial void OnAsOfDateChanged(DateTime? value) => NotifyCommands();
    partial void OnCurrencyCodeChanged(string value) => NotifyCommands();
    partial void OnPartyIdChanged(Guid? value) => NotifyCommands();
    partial void OnItemIdChanged(Guid? value) => NotifyCommands();
    partial void OnCashAccountIdChanged(Guid? value) => NotifyCommands();
    partial void OnPartyKindChanged(ChoiceOption<PartyStatementKind>? value) { PartyId = null; OnPropertyChanged(nameof(Parties)); NotifyCommands(); }
    partial void OnSelectedIncomeStatementLineChanged(StatementAccountLineDto? value) => OpenDocumentCommand?.NotifyCanExecuteChanged();
    partial void OnSelectedBalanceSheetLineChanged(StatementAccountLineDto? value) => OpenDocumentCommand?.NotifyCanExecuteChanged();
    partial void OnSelectedStatementLineChanged(PartyStatementLineDto? value) => OpenDocumentCommand?.NotifyCanExecuteChanged();
    partial void OnSelectedAgingDocumentChanged(AgingDocumentDto? value) => OpenDocumentCommand?.NotifyCanExecuteChanged();
    partial void OnSelectedMovementLineChanged(InventoryMovementLineDto? value) => OpenDocumentCommand?.NotifyCanExecuteChanged();
    partial void OnSelectedCashLineChanged(CashStatementLineDto? value) => OpenDocumentCommand?.NotifyCanExecuteChanged();
}
