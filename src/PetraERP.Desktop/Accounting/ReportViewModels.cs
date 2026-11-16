using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;
using PetraERP.Desktop.Controls;
using PetraERP.Desktop.GeneralLedger;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Accounting;

/// <summary>General Journal: server-filtered, server-paged journal lines; double-click opens the source journal.</summary>
public sealed partial class GeneralJournalViewModel : PageViewModel, ISavedFilterSource
{
    protected override string RequiredPermission => "Ledger.Reports.View";
    public const int PageSize = 100;
    private readonly ILedgerReportsClient reports;
    private readonly ShellNavigator navigator;

    [ObservableProperty] private DateTime? fromDate;
    [ObservableProperty] private DateTime? toDate;
    [ObservableProperty] private Guid? accountId;
    [ObservableProperty] private Guid? costCenterId;
    [ObservableProperty] private ChoiceOption<JournalState?>? state;
    [ObservableProperty] private string search = string.Empty;
    [ObservableProperty] private bool consolidated;
    [ObservableProperty] private GeneralJournalLineResponse? selectedLine;
    [ObservableProperty] private GeneralJournalResponse? result;

    public GeneralJournalViewModel(ILedgerReportsClient reports, IGeneralLedgerClient ledger, ShellNavigator navigator, DesktopOperation operation, ILocalizationService localization)
        : base(operation, localization)
    {
        this.reports = reports; this.navigator = navigator; Lookups = new LedgerLookups(ledger);
        RunCommand = new AsyncRelayCommand(() => RunAsync(token => LoadPageAsync(1, token)), () => HasContext && Idle);
        PreviousPageCommand = new AsyncRelayCommand(() => RunAsync(token => LoadPageAsync(Page - 1, token)), () => HasContext && Idle && Page > 1);
        NextPageCommand = new AsyncRelayCommand(() => RunAsync(token => LoadPageAsync(Page + 1, token)), () => HasContext && Idle && Page * (long)PageSize < TotalCount);
        OpenJournalCommand = new AsyncRelayCommand<GeneralJournalLineResponse>(line => OpenAsync(line ?? SelectedLine), _ => Can("Ledger.Journals.View"));
        ClearFiltersCommand = new RelayCommand(ClearFilters);
        SaveFilterCommand = new RelayCommand(() => { if (CaptureFilter(CompanyId) is { } filter) FilterSink?.Save(filter); }, () => FilterSink is not null && HasResult);
        Localize();
    }

    public LedgerLookups Lookups { get; }
    public ObservableCollection<GeneralJournalLineResponse> Lines { get; } = [];
    public IReadOnlyList<ChoiceOption<JournalState?>> States { get; private set; } = [];
    public int Page => Result?.Page ?? 1;
    public long TotalCount => Result?.TotalCount ?? 0;
    public string PageStatus => string.Format(Localization.Culture, Localization["D_PageStatus"], Page, Lines.Count, TotalCount);
    public string TotalDebit => MoneyText.Format(Result?.TotalBaseDebit);
    public string TotalCredit => MoneyText.Format(Result?.TotalBaseCredit);
    public string BaseCurrency => Result?.BaseCurrencyCode ?? Context?.BaseCurrencyCode ?? string.Empty;
    public bool IsBalanced => Result is not null && Result.TotalBaseDebit == Result.TotalBaseCredit;
    public bool HasResult => Result is not null;
    public bool IsEmpty => Result is not null && Lines.Count == 0;

    public AsyncRelayCommand RunCommand { get; }
    public AsyncRelayCommand PreviousPageCommand { get; }
    public AsyncRelayCommand NextPageCommand { get; }
    public AsyncRelayCommand<GeneralJournalLineResponse> OpenJournalCommand { get; }
    public RelayCommand ClearFiltersCommand { get; }

    /// <summary>Set by the shell; receives this page's criteria when the user saves a filter.</summary>
    public ISavedFilterSink? FilterSink { get => filterSink; set { filterSink = value; SaveFilterCommand.NotifyCanExecuteChanged(); } }
    private ISavedFilterSink? filterSink;
    public RelayCommand SaveFilterCommand { get; }

    public SavedFilter? CaptureFilter(Guid companyId) => companyId == Guid.Empty ? null
        : new SavedFilter(Guid.NewGuid(), UserPreferencesService.DescribeFilter(Localization["D_GeneralJournal"], DateOnlyOrNull(FromDate), DateOnlyOrNull(ToDate)),
            "general-journal", companyId, DateOnlyOrNull(FromDate), DateOnlyOrNull(ToDate), AccountId, null, CostCenterId, Consolidated, FiscalYearId: Context?.FiscalYearId);

    public void Localize()
    {
        var selected = State?.Value;
        States = new[] { new ChoiceOption<JournalState?>(null, Localization["D_PostedOnly"]) }
            .Concat(Enum.GetValues<JournalState>().Select(value => new ChoiceOption<JournalState?>(value, Localization["D_State_" + value]))).ToArray();
        State = States.First(option => option.Value == selected);
        OnPropertyChanged(nameof(States)); OnPropertyChanged(nameof(PageStatus));
    }

    protected override async Task LoadAsync(object? parameter, CancellationToken cancellationToken)
    {
        if (Lookups.Accounts.Count == 0 && Lookups.CostCenters.Count == 0) await Lookups.LoadAsync(Context!, cancellationToken).ConfigureAwait(true);
        if (parameter is OpenGeneralJournalRequest request)
        {
            CostCenterId = request.CostCenterId;
            AccountId = request.AccountId;
            FromDate = request.From?.ToDateTime(TimeOnly.MinValue);
            ToDate = request.To?.ToDateTime(TimeOnly.MinValue);
            if (request.Consolidated is { } consolidatedRequest) Consolidated = consolidatedRequest;
            await LoadPageAsync(1, cancellationToken).ConfigureAwait(true);
        }
        else
        {
            await LoadPageAsync(Page, cancellationToken).ConfigureAwait(true);
        }
    }

    protected override void ClearPage()
    {
        Lookups.Clear(); Lines.Clear(); Result = null; SelectedLine = null; ClearFilters();
    }

    private void ClearFilters()
    {
        FromDate = null; ToDate = null; AccountId = null; CostCenterId = null; Search = string.Empty; Consolidated = false; State = States.Count > 0 ? States[0] : null;
    }

    private async Task LoadPageAsync(int target, CancellationToken cancellationToken)
    {
        var filter = new GeneralJournalFilter(DateOnlyOrNull(FromDate), DateOnlyOrNull(ToDate), AccountId, CostCenterId, State?.Value,
            string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(), Consolidated);
        var page = await reports.GetGeneralJournalAsync(CompanyId, BranchId, filter, Math.Max(1, target), PageSize, cancellationToken).ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested();
        Lines.Clear();
        foreach (var line in page.Lines) Lines.Add(line);
        Result = page;
    }

    private Task OpenAsync(GeneralJournalLineResponse? line) =>
        line is null || !Can("Ledger.Journals.View") ? Task.CompletedTask : navigator.NavigateAsync("journals", new OpenJournalRequest(line.JournalId));

    internal static DateOnly? DateOnlyOrNull(DateTime? value) => value is { } date ? DateOnly.FromDateTime(date) : null;

    partial void OnResultChanged(GeneralJournalResponse? value)
    {
        OnPropertyChanged(nameof(Page)); OnPropertyChanged(nameof(TotalCount)); OnPropertyChanged(nameof(PageStatus)); OnPropertyChanged(nameof(TotalDebit));
        OnPropertyChanged(nameof(TotalCredit)); OnPropertyChanged(nameof(BaseCurrency)); OnPropertyChanged(nameof(IsBalanced)); OnPropertyChanged(nameof(HasResult));
        OnPropertyChanged(nameof(IsEmpty));
        NotifyCommands();
    }

    protected override void NotifyCommands()
    {
        RunCommand?.NotifyCanExecuteChanged(); PreviousPageCommand?.NotifyCanExecuteChanged(); NextPageCommand?.NotifyCanExecuteChanged();
        OpenJournalCommand?.NotifyCanExecuteChanged(); SaveFilterCommand?.NotifyCanExecuteChanged();
    }
}

/// <summary>Account Ledger: opening balance, server-computed running balance across pages, closing balance; drill to journals.</summary>
public sealed partial class AccountLedgerViewModel : PageViewModel, ISavedFilterSource
{
    protected override string RequiredPermission => "Ledger.Reports.View";
    public const int PageSize = 100;
    private readonly ILedgerReportsClient reports;
    private readonly ShellNavigator navigator;

    [ObservableProperty] private Guid? accountId;
    [ObservableProperty] private DateTime? fromDate;
    [ObservableProperty] private DateTime? toDate;
    [ObservableProperty] private bool consolidated;
    [ObservableProperty] private AccountLedgerLineResponse? selectedLine;
    [ObservableProperty] private AccountLedgerResponse? result;

    public AccountLedgerViewModel(ILedgerReportsClient reports, IGeneralLedgerClient ledger, ShellNavigator navigator, DesktopOperation operation, ILocalizationService localization)
        : base(operation, localization)
    {
        this.reports = reports; this.navigator = navigator; Lookups = new LedgerLookups(ledger);
        RunCommand = new AsyncRelayCommand(() => RunAsync(token => LoadPageAsync(1, token)), () => HasContext && Idle && AccountId is not null);
        PreviousPageCommand = new AsyncRelayCommand(() => RunAsync(token => LoadPageAsync(Page - 1, token)), () => HasContext && Idle && Page > 1);
        NextPageCommand = new AsyncRelayCommand(() => RunAsync(token => LoadPageAsync(Page + 1, token)), () => HasContext && Idle && Page * (long)PageSize < TotalCount);
        OpenJournalCommand = new AsyncRelayCommand<AccountLedgerLineResponse>(line => OpenAsync(line ?? SelectedLine), _ => Can("Ledger.Journals.View"));
        SaveFilterCommand = new RelayCommand(() => { if (CaptureFilter(CompanyId) is { } filter) FilterSink?.Save(filter); }, () => FilterSink is not null && HasResult);
    }

    public LedgerLookups Lookups { get; }
    public ObservableCollection<AccountLedgerLineResponse> Lines { get; } = [];
    public int Page => Result?.Page ?? 1;
    public long TotalCount => Result?.TotalCount ?? 0;
    public string PageStatus => string.Format(Localization.Culture, Localization["D_PageStatus"], Page, Lines.Count, TotalCount);
    public string AccountTitle => Result is null ? string.Empty : Result.Account.Code + " — " + (Localization.IsRightToLeft ? Result.Account.NameArabic : Result.Account.NameEnglish ?? Result.Account.NameArabic);
    public string Opening => MoneyText.Format(Result?.OpeningBalance, true);
    public string PeriodDebit => MoneyText.Format(Result?.PeriodDebit);
    public string PeriodCredit => MoneyText.Format(Result?.PeriodCredit);
    public string Closing => MoneyText.Format(Result?.ClosingBalance, true);
    public string BaseCurrency => Result?.BaseCurrencyCode ?? Context?.BaseCurrencyCode ?? string.Empty;
    public bool HasResult => Result is not null;
    public bool IncludesDescendants => Result is { IncludedAccountCount: > 1 };

    public AsyncRelayCommand RunCommand { get; }
    public AsyncRelayCommand PreviousPageCommand { get; }
    public AsyncRelayCommand NextPageCommand { get; }
    public AsyncRelayCommand<AccountLedgerLineResponse> OpenJournalCommand { get; }

    /// <summary>Set by the shell; receives this page's criteria when the user saves a filter.</summary>
    public ISavedFilterSink? FilterSink { get => filterSink; set { filterSink = value; SaveFilterCommand.NotifyCanExecuteChanged(); } }
    private ISavedFilterSink? filterSink;
    public RelayCommand SaveFilterCommand { get; }

    public SavedFilter? CaptureFilter(Guid companyId) => companyId == Guid.Empty || AccountId is not { } account ? null
        : new SavedFilter(Guid.NewGuid(), UserPreferencesService.DescribeFilter(AccountTitle.Length > 0 ? AccountTitle : Localization["D_AccountLedger"],
            GeneralJournalViewModel.DateOnlyOrNull(FromDate), GeneralJournalViewModel.DateOnlyOrNull(ToDate)),
            "account-ledger", companyId, GeneralJournalViewModel.DateOnlyOrNull(FromDate), GeneralJournalViewModel.DateOnlyOrNull(ToDate), account, Consolidated: Consolidated, FiscalYearId: Context?.FiscalYearId);

    protected override async Task LoadAsync(object? parameter, CancellationToken cancellationToken)
    {
        if (Lookups.Accounts.Count == 0) await Lookups.LoadAsync(Context!, cancellationToken).ConfigureAwait(true);
        if (parameter is OpenAccountLedgerRequest request)
        {
            AccountId = request.AccountId;
            FromDate = request.From?.ToDateTime(TimeOnly.MinValue);
            ToDate = request.To is { } to && to != DateOnly.MaxValue ? to.ToDateTime(TimeOnly.MinValue) : null;
            Consolidated = request.Consolidated;
            await LoadPageAsync(1, cancellationToken).ConfigureAwait(true);
        }
        else if (AccountId is not null)
        {
            await LoadPageAsync(Page, cancellationToken).ConfigureAwait(true);
        }
    }

    protected override void ClearPage()
    {
        Lookups.Clear(); Lines.Clear(); pageCursors.Clear(); Result = null; SelectedLine = null; AccountId = null; FromDate = null; ToDate = null; Consolidated = false;
    }

    // Cursor that opens page N, learned while paging forward. Keyset reads keep one server snapshot across the whole walk and
    // do not slow down on deep pages; a missing, expired or rejected cursor falls back to plain page numbers.
    private readonly Dictionary<int, string> pageCursors = [];

    private async Task LoadPageAsync(int target, CancellationToken cancellationToken)
    {
        if (AccountId is not { } account) return;
        target = Math.Max(1, target);
        if (target == 1) pageCursors.Clear();
        var from = GeneralJournalViewModel.DateOnlyOrNull(FromDate);
        var to = GeneralJournalViewModel.DateOnlyOrNull(ToDate);
        AccountLedgerResponse page;
        if (pageCursors.TryGetValue(target, out var cursor))
        {
            try
            {
                page = await reports.GetAccountLedgerAsync(CompanyId, BranchId, account, from, to, Consolidated, target, PageSize, cursor, cancellationToken).ConfigureAwait(true);
            }
            catch (PetraApiException)
            {
                pageCursors.Clear();
                page = await reports.GetAccountLedgerAsync(CompanyId, BranchId, account, from, to, Consolidated, target, PageSize, null, cancellationToken).ConfigureAwait(true);
            }
        }
        else
        {
            page = await reports.GetAccountLedgerAsync(CompanyId, BranchId, account, from, to, Consolidated, target, PageSize, null, cancellationToken).ConfigureAwait(true);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (page.NextCursor is { Length: > 0 } next) pageCursors[page.Page + 1] = next;
        else pageCursors.Remove(page.Page + 1);
        Lines.Clear();
        foreach (var line in page.Lines) Lines.Add(line);
        Result = page;
    }

    private Task OpenAsync(AccountLedgerLineResponse? line) =>
        line is null || !Can("Ledger.Journals.View") ? Task.CompletedTask : navigator.NavigateAsync("journals", new OpenJournalRequest(line.JournalId));

    public void Localize() => OnResultChanged(Result);

    partial void OnAccountIdChanged(Guid? value) => NotifyCommands();

    partial void OnResultChanged(AccountLedgerResponse? value)
    {
        foreach (var name in new[] { nameof(Page), nameof(TotalCount), nameof(PageStatus), nameof(AccountTitle), nameof(Opening), nameof(PeriodDebit),
                     nameof(PeriodCredit), nameof(Closing), nameof(BaseCurrency), nameof(HasResult), nameof(IncludesDescendants) })
        {
            OnPropertyChanged(name);
        }

        NotifyCommands();
    }

    protected override void NotifyCommands()
    {
        RunCommand?.NotifyCanExecuteChanged(); PreviousPageCommand?.NotifyCanExecuteChanged(); NextPageCommand?.NotifyCanExecuteChanged();
        OpenJournalCommand?.NotifyCanExecuteChanged(); SaveFilterCommand?.NotifyCanExecuteChanged();
    }
}

/// <summary>Trial Balance: server rows and reconciled totals rendered as received; double-click drills into the Account Ledger.</summary>
public sealed partial class TrialBalanceViewModel : PageViewModel, ISavedFilterSource
{
    protected override string RequiredPermission => "Ledger.Reports.View";
    private readonly ILedgerReportsClient reports;
    private readonly IFiscalCalendarClient calendar;
    private readonly ShellNavigator navigator;
    private readonly TimeProvider time;

    [ObservableProperty] private DateTime? fromDate;
    [ObservableProperty] private DateTime? toDate;
    [ObservableProperty] private Guid? periodId;
    [ObservableProperty] private ChoiceOption<AccountType?>? accountType;
    [ObservableProperty] private bool includeZeroRows;
    [ObservableProperty] private int? maximumLevel;
    [ObservableProperty] private bool consolidated;
    [ObservableProperty] private TrialBalanceRowResponse? selectedRow;
    [ObservableProperty] private TrialBalanceResponse? result;
    [ObservableProperty] private Guid? parameterAccountId;
    [ObservableProperty] private Guid? parameterCostCenterId;
    [ObservableProperty] private ChoiceOption<int>? accountScope;
    [ObservableProperty] private string fromJournalNumberText = string.Empty;
    [ObservableProperty] private string toJournalNumberText = string.Empty;
    [ObservableProperty] private bool sourceJournal = true;
    [ObservableProperty] private bool sourceSalesInvoice = true;
    [ObservableProperty] private bool sourcePurchaseInvoice = true;
    [ObservableProperty] private bool sourceVoucher = true;
    [ObservableProperty] private string displayCurrencyText = string.Empty;
    [ObservableProperty] private string displayRateText = string.Empty;
    [ObservableProperty] private bool showLastMovement;
    [ObservableProperty] private bool lastMovementColumns;

    public TrialBalanceViewModel(ILedgerReportsClient reports, IFiscalCalendarClient calendar, ShellNavigator navigator, DesktopOperation operation,
        ILocalizationService localization, TimeProvider time, IGeneralLedgerClient? ledger = null) : base(operation, localization)
    {
        this.reports = reports; this.calendar = calendar; this.navigator = navigator; this.time = time;
        Lookups = ledger is null ? null : new LedgerLookups(ledger);
        RunCommand = new AsyncRelayCommand(() => RunAsync(LoadReportAsync), () => HasContext && Idle && (ToDate is not null || PeriodId is not null));
        OpenLedgerCommand = new AsyncRelayCommand<TrialBalanceRowResponse>(row => OpenAsync(row ?? SelectedRow), _ => Can("Ledger.Reports.View"));
        SaveFilterCommand = new RelayCommand(() => { if (CaptureFilter(CompanyId) is { } filter) FilterSink?.Save(filter); }, () => FilterSink is not null && HasResult);
        Localize();
    }

    public ObservableCollection<TrialBalanceRowResponse> Rows { get; } = [];
    public ObservableCollection<LookupItem> PeriodOptions { get; } = [];
    /// <summary>Account and cost-center pickers of the request parameters; null when the page was built without a ledger client.</summary>
    public LedgerLookups? Lookups { get; }
    public IReadOnlyList<ChoiceOption<int>> AccountScopes { get; private set; } = [];
    public bool HasLookups => Lookups is not null;
    public string DisplayRateHint => Localization["D_TB_DisplayRateHint"];
    public IReadOnlyList<ChoiceOption<AccountType?>> AccountTypes { get; private set; } = [];
    public IReadOnlyList<int> Levels { get; } = [0, 1, 2, 3, 4, 5];
    public string TotalOpeningDebit => MoneyText.Format(Result?.TotalOpeningDebit);
    public string TotalOpeningCredit => MoneyText.Format(Result?.TotalOpeningCredit);
    public string TotalPeriodDebit => MoneyText.Format(Result?.TotalPeriodDebit);
    public string TotalPeriodCredit => MoneyText.Format(Result?.TotalPeriodCredit);
    public string TotalEndingDebit => MoneyText.Format(Result?.TotalEndingDebit);
    public string TotalEndingCredit => MoneyText.Format(Result?.TotalEndingCredit);
    public bool IsBalanced => Result?.IsBalanced == true;
    public bool HasResult => Result is not null;
    public string BalanceText => Localization[IsBalanced ? "D_Balanced" : "D_NotBalanced"];
    public BadgeKind BalanceKind => IsBalanced ? BadgeKind.Success : BadgeKind.Error;
    public string BaseCurrency => Result?.DisplayCurrencyCode ?? Result?.BaseCurrencyCode ?? Context?.BaseCurrencyCode ?? string.Empty;
    public bool CanPickPeriod => Can("Ledger.Periods.View");

    public AsyncRelayCommand RunCommand { get; }
    public AsyncRelayCommand<TrialBalanceRowResponse> OpenLedgerCommand { get; }

    /// <summary>Set by the shell; receives this page's criteria when the user saves a filter.</summary>
    public ISavedFilterSink? FilterSink { get => filterSink; set { filterSink = value; SaveFilterCommand.NotifyCanExecuteChanged(); } }
    private ISavedFilterSink? filterSink;
    public RelayCommand SaveFilterCommand { get; }

    public SavedFilter? CaptureFilter(Guid companyId) => companyId == Guid.Empty || (ToDate is null && PeriodId is null) ? null
        : new SavedFilter(Guid.NewGuid(), UserPreferencesService.DescribeFilter(Localization["D_TrialBalance"],
            GeneralJournalViewModel.DateOnlyOrNull(FromDate), GeneralJournalViewModel.DateOnlyOrNull(ToDate)),
            "trial-balance", companyId, GeneralJournalViewModel.DateOnlyOrNull(FromDate), GeneralJournalViewModel.DateOnlyOrNull(ToDate),
            PeriodId: PeriodId, Consolidated: Consolidated, IncludeZeroRows: IncludeZeroRows, MaximumLevel: MaximumLevel, FiscalYearId: Context?.FiscalYearId);

    public void Localize()
    {
        var selected = AccountType?.Value;
        AccountTypes = new[] { new ChoiceOption<AccountType?>(null, Localization["D_AllTypes"]) }
            .Concat(Enum.GetValues<AccountType>().Select(value => new ChoiceOption<AccountType?>(value, Localization["D_AccountType_" + value]))).ToArray();
        AccountType = AccountTypes.First(option => option.Value == selected);
        var scope = AccountScope?.Value ?? 0;
        AccountScopes = [new(0, Localization["D_TB_ScopeAll"]), new(1, Localization["D_TB_ScopePosting"]), new(2, Localization["D_TB_ScopeParents"])];
        AccountScope = AccountScopes.First(option => option.Value == scope);
        OnPropertyChanged(nameof(AccountScopes)); OnPropertyChanged(nameof(DisplayRateHint));
        OnPropertyChanged(nameof(AccountTypes)); OnPropertyChanged(nameof(BalanceText));
    }

    protected override async Task LoadAsync(object? parameter, CancellationToken cancellationToken)
    {
        ToDate ??= time.GetLocalNow().Date;
        if (Lookups is not null && Lookups.Accounts.Count == 0 && Lookups.CostCenters.Count == 0) await Lookups.LoadAsync(Context!, cancellationToken).ConfigureAwait(true);
        if (CanPickPeriod && PeriodOptions.Count == 0)
        {
            var years = await calendar.ListFiscalYearsAsync(CompanyId, BranchId, cancellationToken).ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var year in years.OrderByDescending(year => year.RangeStart))
            {
                foreach (var period in year.Periods.OrderBy(period => period.RangeStart))
                {
                    PeriodOptions.Add(new LookupItem(period.Id, year.Code + "/" + period.Code, period.RangeStart.ToString("yyyy-MM-dd", Localization.Culture) + " → "
                        + period.RangeEnd.ToString("yyyy-MM-dd", Localization.Culture), null));
                }
            }
        }

        if (parameter is OpenTrialBalanceRequest request)
        {
            PeriodId = request.PeriodId;
            if (request.From is { } from) FromDate = from.ToDateTime(TimeOnly.MinValue);
            if (request.To is { } to) ToDate = to.ToDateTime(TimeOnly.MinValue);
            if (request.IncludeZeroRows is { } zero) IncludeZeroRows = zero;
            if (request.MaximumLevel is { } level) MaximumLevel = level;
            if (request.Consolidated is { } consolidatedRequest) Consolidated = consolidatedRequest;
            await LoadReportAsync(cancellationToken).ConfigureAwait(true);
        }
        else if (Result is not null)
        {
            await LoadReportAsync(cancellationToken).ConfigureAwait(true);
        }
    }

    protected override void ClearPage()
    {
        Rows.Clear(); PeriodOptions.Clear(); Result = null; SelectedRow = null; FromDate = null; ToDate = null; PeriodId = null; IncludeZeroRows = false;
        MaximumLevel = null; Consolidated = false; AccountType = AccountTypes.Count > 0 ? AccountTypes[0] : null;
        Lookups?.Clear(); ParameterAccountId = null; ParameterCostCenterId = null; AccountScope = AccountScopes.Count > 0 ? AccountScopes[0] : null;
        FromJournalNumberText = string.Empty; ToJournalNumberText = string.Empty; SourceJournal = SourceSalesInvoice = SourcePurchaseInvoice = SourceVoucher = true;
        DisplayCurrencyText = string.Empty; DisplayRateText = string.Empty; ShowLastMovement = false; LastMovementColumns = false;
    }

    private async Task LoadReportAsync(CancellationToken cancellationToken)
    {
        var filter = new TrialBalanceFilter(GeneralJournalViewModel.DateOnlyOrNull(FromDate), GeneralJournalViewModel.DateOnlyOrNull(ToDate), PeriodId,
            AccountType?.Value, IncludeZeroRows, MaximumLevel, Consolidated, BuildParameters());
        var report = await reports.GetTrialBalanceAsync(CompanyId, BranchId, filter, cancellationToken).ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested();
        Rows.Clear();
        foreach (var row in report.Rows) Rows.Add(row);
        LastMovementColumns = ShowLastMovement;
        Result = report;
    }

    /// <summary>The report-request parameters; null while every one of them is at its default so the plain report is requested.</summary>
    internal TrialBalanceParameters? BuildParameters()
    {
        var sources = new List<string>();
        if (SourceJournal) sources.Add("JOURNAL");
        if (SourceSalesInvoice) sources.Add("SALES_INVOICE");
        if (SourcePurchaseInvoice) sources.Add("PURCHASE_INVOICE");
        if (SourceVoucher) sources.Add("VOUCHER");
        var allSources = sources.Count == 4;
        var rate = string.IsNullOrWhiteSpace(DisplayRateText) ? null : DisplayRateText.Trim();
        long? from = long.TryParse(FromJournalNumberText?.Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var f) ? f : null;
        long? to = long.TryParse(ToJournalNumberText?.Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var t) ? t : null;
        var scope = AccountScope?.Value ?? 0;
        var currency = string.IsNullOrWhiteSpace(DisplayCurrencyText) ? null : DisplayCurrencyText.Trim().ToUpperInvariant();
        if (ParameterAccountId is null && ParameterCostCenterId is null && scope == 0 && from is null && to is null && allSources && currency is null && rate is null && !ShowLastMovement)
            return null;
        // No source ticked means "no restriction" would be misleading; the server treats an empty list as unrestricted, so send all of them instead.
        return new TrialBalanceParameters(ParameterAccountId, ParameterCostCenterId, scope, from, to, allSources || sources.Count == 0 ? null : string.Join(',', sources),
            currency, currency is null && rate is null ? null : rate, ShowLastMovement);
    }

    private Task OpenAsync(TrialBalanceRowResponse? row) =>
        row is null || Result is null || !Can("Ledger.Reports.View") ? Task.CompletedTask
            : navigator.NavigateAsync("account-ledger", new OpenAccountLedgerRequest(row.AccountId, Result.From, Result.To, Consolidated));

    partial void OnToDateChanged(DateTime? value) => NotifyCommands();
    partial void OnPeriodIdChanged(Guid? value) => NotifyCommands();

    partial void OnResultChanged(TrialBalanceResponse? value)
    {
        foreach (var name in new[] { nameof(TotalOpeningDebit), nameof(TotalOpeningCredit), nameof(TotalPeriodDebit), nameof(TotalPeriodCredit),
                     nameof(TotalEndingDebit), nameof(TotalEndingCredit), nameof(IsBalanced), nameof(HasResult), nameof(BalanceText), nameof(BalanceKind), nameof(BaseCurrency) })
        {
            OnPropertyChanged(name);
        }

        NotifyCommands();
    }

    protected override void NotifyCommands()
    {
        OnPropertyChanged(nameof(CanPickPeriod));
        RunCommand?.NotifyCanExecuteChanged(); OpenLedgerCommand?.NotifyCanExecuteChanged(); SaveFilterCommand?.NotifyCanExecuteChanged();
    }
}
