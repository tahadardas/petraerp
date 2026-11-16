using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Reports;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Reporting;

/// <summary>Persistent report parameters. Opening or closing a preview never replaces this instance or its criteria.</summary>
public sealed partial class ProfessionalReportPageViewModel : PageViewModel
{
    private readonly IProfessionalReportsClient reports;
    private readonly IProfessionalReportPresetStore presets;
    private readonly IProfessionalReportLookups lookups;
    private CancellationTokenSource? partyAccountLoad;
    private CancellationTokenSource? partyCurrencyLoad;
    private long partyCurrencyGeneration;
    private bool partyCurrenciesLoaded;
    private long partyAccountGeneration;
    private PartyControlAccountState? partyAccountState;
    private ProfessionalReportDefinitionDto? definition;
    private Guid? loadedFiscalYear;
    private Guid? lastUser;
    private Guid? lastWorkspaceYear;
    private CancellationTokenSource? execution;
    [ObservableProperty] private DateTime? fromDate;
    [ObservableProperty] private DateTime? toDate;
    [ObservableProperty] private DateTime? postingFrom;
    [ObservableProperty] private DateTime? postingTo;
    [ObservableProperty] private DateTime? createdFrom;
    [ObservableProperty] private DateTime? createdTo;
    [ObservableProperty] private string currencyCode = string.Empty;
    [ObservableProperty] private ProfessionalCurrencyBasis currencyBasis = ProfessionalCurrencyBasis.Base;
    /// <summary>Currency the result is converted to through the exchange-rate register; empty keeps the report's own currency.</summary>
    [ObservableProperty] private string displayCurrencyCode = string.Empty;
    [ObservableProperty] private ProfessionalReportGrouping grouping;
    [ObservableProperty] private ProfessionalReportOrder order = ProfessionalReportOrder.Date;
    [ObservableProperty] private ProfessionalBalancePresentation balancePresentation = ProfessionalBalancePresentation.Actual;
    [ObservableProperty] private string descriptionContains = string.Empty;
    [ObservableProperty] private string descriptionNotContains = string.Empty;
    [ObservableProperty] private long? fromSequence;
    [ObservableProperty] private long? toSequence;
    [ObservableProperty] private int? accountLevel;
    [ObservableProperty] private bool includeParents;
    [ObservableProperty] private bool includePostingAccounts = true;
    [ObservableProperty] private bool includeEmptyAccounts;
    [ObservableProperty] private bool onlyNonZero;
    [ObservableProperty] private bool includeInvoiceDetails;
    [ObservableProperty] private bool includeAging;
    [ObservableProperty] private ProfessionalFiscalYearDto? selectedFiscalYear;
    [ObservableProperty] private string sourceSearch = string.Empty;
    [ObservableProperty] private string patternSearch = string.Empty;
    [ObservableProperty] private ProfessionalAccountCategory? accountCategory;
    [ObservableProperty] private string message = string.Empty;
    [ObservableProperty] private string presetName = string.Empty;
    [ObservableProperty] private ProfessionalReportPreset? selectedPreset;
    [ObservableProperty] private bool isExecuting;
    [ObservableProperty] private string descriptionSearchScope = string.Empty;
    [ObservableProperty] private string partyAccountContext = string.Empty;
    [ObservableProperty] private string partyCurrencyStatus = string.Empty;
    [ObservableProperty] private ProfessionalReportChoice<ProfessionalReportField>? selectedField;
    [ObservableProperty] private string exchangeRate = "1.00000";
    [ObservableProperty] private bool filterByPostingDate = true;
    [ObservableProperty] private bool filterByCreatedDate;
    [ObservableProperty] private bool isFromLastReconciliation;
    [ObservableProperty] private string exchangeRateType = "كلفة تاريخية";
    [ObservableProperty] private string accountOrderText = "رمز الحساب";
    [ObservableProperty] private string monthlyTrialText = "بدون";
    [ObservableProperty] private bool filterByAccountCurrency;
    [ObservableProperty] private string accountCurrencyCode = string.Empty;
    [ObservableProperty] private bool filterBySequence;
    [ObservableProperty] private string trialBalancePresentationKind = "أرصدة الأرصدة";
    [ObservableProperty] private string materialQuery = string.Empty;
    [ObservableProperty] private string groupQuery = string.Empty;
    [ObservableProperty] private string userQuery = string.Empty;
    [ObservableProperty] private string voucherPatternCondition = "بدون";
    [ObservableProperty] private bool showAccountName = true;
    [ObservableProperty] private bool showAccountCode = true;
    [ObservableProperty] private bool includeAggregateAccounts = true;
    [ObservableProperty] private bool showLastCustomerPayment;
    [ObservableProperty] private bool showLastDebitActivity;
    [ObservableProperty] private bool showLastCreditActivity;
    [ObservableProperty] private bool showPostedEntries = true;
    [ObservableProperty] private bool readUnpostedEntries = true;
    [ObservableProperty] private bool groupDuplicateAccountsInJournal;
    [ObservableProperty] private bool mergeCommercialPapers;
    [ObservableProperty] private bool handleNarrationInOpeningBalance = true;
    [ObservableProperty] private bool eachAccountOnNewPage;
    [ObservableProperty] private bool readAuditedLines;
    [ObservableProperty] private bool readUnauditedLines;
    [ObservableProperty] private bool showTafqeet;
    [ObservableProperty] private bool showExchangeDiffHistorical;
    [ObservableProperty] private bool showLineAndVoucherNarration;
    [ObservableProperty] private bool showPostingDate;

    public bool ZeroBalanceAtStart
    {
        get => BalancePresentation == ProfessionalBalancePresentation.ZeroAtPeriodStart;
        set
        {
            if (ZeroBalanceAtStart != value)
            {
                BalancePresentation = value ? ProfessionalBalancePresentation.ZeroAtPeriodStart : ProfessionalBalancePresentation.Actual;
                OnPropertyChanged(nameof(ZeroBalanceAtStart));
            }
        }
    }

    partial void OnBalancePresentationChanged(ProfessionalBalancePresentation value) => OnPropertyChanged(nameof(ZeroBalanceAtStart));

    partial void OnSelectedFieldChanged(ProfessionalReportChoice<ProfessionalReportField>? value)
    {
        MoveFieldUpCommand?.NotifyCanExecuteChanged();
        MoveFieldDownCommand?.NotifyCanExecuteChanged();
    }

    public ProfessionalReportPageViewModel(ProfessionalReportKind kind, IProfessionalReportsClient reports, IProfessionalReportLookups lookups,
        IProfessionalReportPresetStore presets, DesktopOperation operation, ILocalizationService localization) : base(operation, localization)
    {
        Kind = kind; this.reports = reports; this.presets = presets; this.lookups = lookups;
        Order = DefaultOrder;
        Account = Lookup(ProfessionalLookupKind.Account);
        Party = Lookup(kind == ProfessionalReportKind.SupplierStatement ? ProfessionalLookupKind.Supplier
            : kind == ProfessionalReportKind.CombinedPartyStatement ? ProfessionalLookupKind.Party : ProfessionalLookupKind.Customer);
        OppositeAccount = Lookup(ProfessionalLookupKind.Account);
        CostCenter = Lookup(ProfessionalLookupKind.CostCenter);
        Party.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ProfessionalLookupViewModel.Selected))
            {
                _ = RefreshPartyAccountAsync();
                _ = RefreshPartyCurrenciesAsync();
            }
        };
        Account.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ProfessionalLookupViewModel.Selected)) ApplyAccountCurrency();
        };
        OpenPartyCardCommand = new RelayCommand(() =>
        {
            if (Party.Selected is { } selected && Can("Parties.Directory.View")) PartyCardRequested?.Invoke(selected.Id);
        }, () => Party.Selected is not null && Can("Parties.Directory.View"));
        PreviewCommand = new AsyncRelayCommand(ExecuteAsync, () => HasContext && !IsExecuting && definition is not null);
        CancelCommand = new RelayCommand(() => execution?.Cancel(), () => IsExecuting);
        SelectAllSourcesCommand = new RelayCommand(() => SelectSources(true));
        ClearSourcesCommand = new RelayCommand(() => SelectSources(false));
        SelectAllFieldsCommand = new RelayCommand(() => SelectFields(true));
        ClearFieldsCommand = new RelayCommand(() => SelectFields(false));
        SelectAllPatternsCommand = new RelayCommand(() => SelectPatterns(true));
        ClearPatternsCommand = new RelayCommand(() => SelectPatterns(false));
        SavePresetCommand = new AsyncRelayCommand(SavePresetAsync, () => HasContext && !IsExecuting);
        RestorePresetCommand = new AsyncRelayCommand(RestorePresetAsync, () => SelectedPreset is not null && !IsExecuting);
        MoveFieldUpCommand = new RelayCommand(() =>
        {
            if (SelectedField is null) return;
            var index = Fields.IndexOf(SelectedField);
            if (index > 0)
            {
                Fields.Move(index, index - 1);
                MoveFieldUpCommand?.NotifyCanExecuteChanged();
                MoveFieldDownCommand?.NotifyCanExecuteChanged();
            }
        }, () => SelectedField is not null && Fields.IndexOf(SelectedField) > 0);
        MoveFieldDownCommand = new RelayCommand(() =>
        {
            if (SelectedField is null) return;
            var index = Fields.IndexOf(SelectedField);
            if (index >= 0 && index < Fields.Count - 1)
            {
                Fields.Move(index, index + 1);
                MoveFieldUpCommand?.NotifyCanExecuteChanged();
                MoveFieldDownCommand?.NotifyCanExecuteChanged();
            }
        }, () => SelectedField is not null && Fields.IndexOf(SelectedField) >= 0 && Fields.IndexOf(SelectedField) < Fields.Count - 1);
        SummaryCommand = new AsyncRelayCommand(async () =>
        {
            Grouping = Grouping == ProfessionalReportGrouping.None ? ProfessionalReportGrouping.Month : ProfessionalReportGrouping.None;
            await ExecuteAsync().ConfigureAwait(true);
        }, () => HasContext && !IsExecuting && definition is not null);
        PrintCommand = new AsyncRelayCommand(async () =>
        {
            await ExecuteAsync().ConfigureAwait(true);
            PrintRequested?.Invoke();
        }, () => HasContext && !IsExecuting && definition is not null);
        CloseCommand = new RelayCommand(() => CloseRequested?.Invoke());

        ProfessionalLookupViewModel Lookup(ProfessionalLookupKind lookupKind) => new((query, token) =>
            lookups.SearchAsync(CompanyId, BranchId, lookupKind, query, token), () => Token, Text,
            (id, token) => lookups.ResolveAsync(CompanyId, BranchId, lookupKind, id, token),
            (query, page, token) => lookups.SearchPageAsync(CompanyId, BranchId, lookupKind, query, page, token));
    }

    public ProfessionalReportKind Kind { get; }
    protected override string RequiredPermission => "Ledger.Reports.View";
    public ProfessionalLookupViewModel Account { get; }
    public ProfessionalLookupViewModel Party { get; }
    public ProfessionalLookupViewModel OppositeAccount { get; }
    public ProfessionalLookupViewModel CostCenter { get; }
    public ObservableCollection<ProfessionalReportChoice<ProfessionalReportSource>> Sources { get; } = [];
    public ObservableCollection<ProfessionalReportChoice<ProfessionalReportField>> Fields { get; } = [];
    public PetraReportFilterBuilderViewModel FilterBuilder { get; } = new();
    public ObservableCollection<ProfessionalOptionDefinitionDto> UnsupportedOptions { get; } = [];
    public ObservableCollection<ProfessionalPatternChoice> Patterns { get; } = [];
    public ObservableCollection<ProfessionalReportPreset> Presets { get; } = [];
    public ObservableCollection<ProfessionalFiscalYearDto> FiscalYears { get; } = [];
    public ObservableCollection<string> PartyActivityCurrencies { get; } = [];
    public IEnumerable<ProfessionalReportChoice<ProfessionalReportSource>> VisibleSources => Sources.Where(source => string.IsNullOrWhiteSpace(SourceSearch) ||
        source.Label.Contains(SourceSearch.Trim(), StringComparison.CurrentCultureIgnoreCase));
    /// <summary>Patterns of currently selected sources only; none selected means every pattern of those sources.</summary>
    public IEnumerable<ProfessionalPatternChoice> VisiblePatterns => Patterns.Where(pattern => IsSourceSelected(pattern.Source)
        && (string.IsNullOrWhiteSpace(PatternSearch) || pattern.Label.Contains(PatternSearch.Trim(), StringComparison.CurrentCultureIgnoreCase)));
    public string PatternCount => $"{VisiblePatterns.Count(pattern => pattern.IsSelected)} / {VisiblePatterns.Count()}";
    public string SourceCount => $"{Sources.Count(source => source.IsSelected)} / {Sources.Count(source => source.IsSupported)}";
    public string FieldCount => $"{Fields.Count(choice => choice.IsSelected)} / {Fields.Count(choice => choice.IsSupported)}";
    public string Title => definition is null ? Kind.ToString() : Text(definition.NameArabic, definition.NameEnglish);
    public string WorkspaceLabel => Context is null ? string.Empty :
        Text(Context.Company.NameArabic, Context.Company.NameEnglish ?? Context.Company.NameArabic) + " · " +
        Text(Context.Branch.NameArabic, Context.Branch.NameEnglish ?? Context.Branch.NameArabic) + " · " +
        (SelectedFiscalYear?.Code ?? Text("السنة غير محددة", "Fiscal year not selected"));
    public bool IsPartyStatement => Kind is ProfessionalReportKind.CustomerStatement or ProfessionalReportKind.SupplierStatement or ProfessionalReportKind.CombinedPartyStatement;
    public bool IsCombinedStatement => Kind == ProfessionalReportKind.CombinedPartyStatement;
    public bool IsCurrencyBalance => Kind == ProfessionalReportKind.CurrencyBalance;
    /// <summary>True for the plain trial balance only (account levels, parents and empty accounts do not apply to the currency balance).</summary>
    public bool IsAccountTrial => Kind == ProfessionalReportKind.TrialBalance;
    /// <summary>Summarised layouts: trial balance and currency balance (no per-movement field picker).</summary>
    public bool IsTrialBalance => Kind is ProfessionalReportKind.TrialBalance or ProfessionalReportKind.CurrencyBalance;

    /// <summary>The server catalog only accepts account-code ordering for the trial balance; every default must match it.</summary>
    public ProfessionalReportOrder DefaultOrder => IsTrialBalance
        ? ProfessionalReportOrder.AccountCode : ProfessionalReportOrder.Date;
    public ProfessionalReportOrder EffectiveOrder => IsTrialBalance ? ProfessionalReportOrder.AccountCode : Order;
    public bool CanSelectFiscalYear => Context?.FiscalYearId is null;
    private Guid? EffectiveFiscalYearId => Context?.FiscalYearId ?? SelectedFiscalYear?.Id;
    private ShellContext PresetContext => Context! with { FiscalYearId = EffectiveFiscalYearId };
    private static DateOnly ClampToYear(DateOnly date, DateOnly from, DateOnly to) => date < from ? from : date > to ? to : date;
    public AsyncRelayCommand PreviewCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand OpenPartyCardCommand { get; }
    public RelayCommand SelectAllSourcesCommand { get; }
    public RelayCommand ClearSourcesCommand { get; }
    public RelayCommand SelectAllFieldsCommand { get; }
    public RelayCommand ClearFieldsCommand { get; }
    public RelayCommand SelectAllPatternsCommand { get; }
    public RelayCommand ClearPatternsCommand { get; }
    public AsyncRelayCommand SavePresetCommand { get; }
    public AsyncRelayCommand RestorePresetCommand { get; }
    public RelayCommand MoveFieldUpCommand { get; }
    public RelayCommand MoveFieldDownCommand { get; }
    public AsyncRelayCommand SummaryCommand { get; }
    public AsyncRelayCommand PrintCommand { get; }
    public RelayCommand CloseCommand { get; }
    public event Action? PrintRequested;
    public event Action? CloseRequested;
    public event Action<ProfessionalPreviewRequest>? PreviewRequested;
    public event Action<Guid>? JournalRequested;
    public event Action<ProfessionalReportRowDto>? SourceRequested;
    public event Action<ProfessionalReportRowDto>? AccountLedgerRequested;
    public event Action<Guid>? PartyCardRequested;
    public event EventHandler? DefinitionChanged;

    public string Text(string arabic, string english) => Localization.IsRightToLeft ? arabic : english;

    private async Task RefreshPartyCurrenciesAsync()
    {
        var current = ++partyCurrencyGeneration;
        partyCurrencyLoad?.Cancel(); partyCurrencyLoad = null;
        partyCurrenciesLoaded = false;
        PartyActivityCurrencies.Clear(); PartyCurrencyStatus = string.Empty;
        if (!IsPartyStatement || Context is not { } context || Party.Selected is not { } party || EffectiveFiscalYearId is not { } year || ToDate is not { } date) return;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        partyCurrencyLoad = cancellation;
        PartyCurrencyStatus = Text("جارٍ تحميل عملات الحركة…", "Loading activity currencies…");
        try
        {
            var currencies = await reports.PartyActivityCurrenciesAsync(context.CompanyId, context.BranchId, party.Id, year, Kind,
                DateOnly.FromDateTime(date), cancellation.Token).ConfigureAwait(true);
            cancellation.Token.ThrowIfCancellationRequested();
            if (current != partyCurrencyGeneration || Context != context || Party.Selected?.Id != party.Id || EffectiveFiscalYearId != year || ToDate != date) return;
            foreach (var currency in currencies) PartyActivityCurrencies.Add(currency);
            partyCurrenciesLoaded = true;
            UpdatePartyCurrencyStatus();
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (current == partyCurrencyGeneration)
                PartyCurrencyStatus = Text("تعذر تحميل عملات الحركة؛ تحقق من التاريخ والصلاحيات أو أدخل العملة صراحةً.", "Could not load activity currencies; check date and permissions or enter a currency explicitly.");
        }
        finally { if (current == partyCurrencyGeneration) partyCurrencyLoad = null; }
    }

    private void UpdatePartyCurrencyStatus()
    {
        if (!partyCurrenciesLoaded) return;
        var selected = CurrencyCode.Trim();
        if (PartyActivityCurrencies.Count == 0)
            PartyCurrencyStatus = Text("لا توجد حركة مرحلة حتى تاريخ النهاية؛ اختر العملة صراحةً.", "No posted activity through the end date; choose a currency explicitly.");
        else if (selected.Length > 0 && !PartyActivityCurrencies.Contains(selected, StringComparer.OrdinalIgnoreCase))
            PartyCurrencyStatus = Text("لا توجد حركة بهذه العملة حتى تاريخ النهاية؛ العملات المتاحة: ", "No activity in this currency through the end date; available currencies: ") + string.Join(", ", PartyActivityCurrencies);
        else
            PartyCurrencyStatus = Text("اختر عملة حركة واحدة؛ لا يتم التحويل أو جمع العملات.", "Choose one transaction currency; currencies are not converted or summed.");
    }

    public ObservableCollection<string> CurrencyOptions { get; } = [];

    private async Task LoadCurrencyOptionsAsync(CancellationToken cancellationToken)
    {
        CurrencyOptions.Clear();
        CurrencyOptions.Add(string.Empty);
        if (Context is not { } context) return;
        try
        {
            foreach (var code in await lookups.ListCurrenciesAsync(context.CompanyId, context.BranchId, cancellationToken).ConfigureAwait(true))
                CurrencyOptions.Add(code);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (exception is not OutOfMemoryException) { /* the drop-down stays minimal; the report itself is unaffected */ }
    }

    private void ApplyAccountCurrency()
    {
        if (Kind != ProfessionalReportKind.AccountLedger) return;
        if (Account.Selected is { CurrencyCode: { Length: 3 } fixedCurrency })
        {
            // A fixed-currency account is shown in its own currency by default, with every movement (converted through the register).
            CurrencyCode = string.Empty; CurrencyBasis = ProfessionalCurrencyBasis.Base; DisplayCurrencyCode = fixedCurrency;
        }
        else
        {
            CurrencyCode = string.Empty; CurrencyBasis = ProfessionalCurrencyBasis.Base; DisplayCurrencyCode = string.Empty;
        }
    }

    private async Task RefreshPartyAccountAsync()
    {
        var current = ++partyAccountGeneration;
        partyAccountLoad?.Cancel();
        partyAccountLoad = null;
        partyAccountState = null;
        PartyAccountContext = string.Empty;
        OpenPartyCardCommand.NotifyCanExecuteChanged();
        if (!IsPartyStatement || IsCombinedStatement || Context is not { } context || Party.Selected is not { } party) return;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        partyAccountLoad = cancellation;
        PartyAccountContext = Text("جارٍ تحميل حساب الذمة المرتبط…", "Loading the linked control account…");
        try
        {
            var kind = Kind == ProfessionalReportKind.SupplierStatement ? ProfessionalLookupKind.Supplier : ProfessionalLookupKind.Customer;
            var result = await lookups.GetPartyControlAccountAsync(context.CompanyId, context.BranchId, kind, party.Id, cancellation.Token).ConfigureAwait(true);
            cancellation.Token.ThrowIfCancellationRequested();
            if (current != partyAccountGeneration || Context != context || Party.Selected?.Id != party.Id) return;
            partyAccountState = result.State;
            if (result.State == PartyControlAccountState.Available && result.CurrencyCode is { Length: 3 } accountCurrency)
            {
                // The statement opens in the currency of the party's own account, not the program base currency.
                CurrencyCode = accountCurrency; CurrencyBasis = ProfessionalCurrencyBasis.Transaction;
            }
            PartyAccountContext = result.State switch
            {
                PartyControlAccountState.Available => Text("حساب الذمة المرتبط: ", "Linked control account: ") + result.Code + " · " + result.Name,
                PartyControlAccountState.Missing => Text("لم يُحدّد حساب الذمة. افتح بطاقة الطرف لتعيينه.", "No control account is mapped. Open the party card to assign one."),
                _ => Text("حساب الذمة غير نشط أو لا يقبل الترحيل. راجع بطاقة الطرف.", "The control account is inactive or does not allow posting. Review the party card."),
            };
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (current == partyAccountGeneration)
                PartyAccountContext = Text("تعذر تحميل حساب الذمة؛ تحقق من صلاحية عرض الحساب والطرف.", "Cannot load the control account; check party and account viewing permissions.");
        }
        finally { if (current == partyAccountGeneration) partyAccountLoad = null; }
    }
    public bool Supports(ProfessionalReportOption option) => definition?.Options.Any(value => value.Option == option && value.Supported) == true;
    public string? OptionReason(ProfessionalReportOption option) => definition?.Options.FirstOrDefault(value => value.Option == option)?.Reason;
    public void RefreshLabels()
    {
        foreach (var choice in Sources)
        {
            var source = definition?.Sources.SingleOrDefault(value => value.Source == choice.Value);
            if (source is not null) choice.Label = Text(source.LabelArabic, source.Source.ToString());
        }
        foreach (var choice in Fields)
        {
            var field = definition?.Fields.SingleOrDefault(value => value.Field == choice.Value);
            if (field is not null) choice.Label = Text(field.LabelArabic, field.Field.ToString());
        }
        OnPropertyChanged(nameof(Title)); OnPropertyChanged(nameof(WorkspaceLabel)); OnPropertyChanged(nameof(VisibleSources));
    }

    protected override async Task LoadAsync(object? parameter, CancellationToken cancellationToken)
    {
        if (lastUser != Context?.UserId || lastWorkspaceYear != Context?.FiscalYearId) ClearPage();
        lastUser = Context?.UserId; lastWorkspaceYear = Context?.FiscalYearId;
        ProfessionalReportCatalogResponse catalog;
        try { catalog = await reports.CatalogAsync(CompanyId, BranchId, cancellationToken).ConfigureAwait(true); }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        { definition = null; Message = DescribeFailure(exception); NotifyCommands(); return; }
        cancellationToken.ThrowIfCancellationRequested();
        var previousYear = EffectiveFiscalYearId;
        FiscalYears.Clear(); foreach (var year in catalog.FiscalYears) FiscalYears.Add(year);
        var today = DateOnly.FromDateTime(TimeProvider.System.GetLocalNow().DateTime);
        SelectedFiscalYear = (previousYear is not null ? FiscalYears.SingleOrDefault(year => year.Id == previousYear) : null)
            ?? FiscalYears.FirstOrDefault(year => year.Id == Context?.FiscalYearId)
            ?? FiscalYears.FirstOrDefault(year => year.From <= today && today <= year.To);
        definition = catalog.Reports.SingleOrDefault(report => report.Kind == Kind);
        if (definition is null) { Message = Text("هذا التقرير غير متاح للصلاحيات الحالية.", "This report is unavailable for the current permissions."); return; }
        DescriptionSearchScope = catalog.DescriptionSearchScope;
        maximumRows = catalog.MaximumRows;
        var firstLoad = Sources.Count == 0;
        var selectedSources = Sources.Where(choice => choice.IsSelected).Select(choice => choice.Value).ToHashSet();
        var selectedFields = Fields.Where(choice => choice.IsSelected).Select(choice => choice.Value).ToHashSet();
        var selectedPatterns = Patterns.Where(choice => choice.IsSelected).Select(choice => choice.RevisionId).ToHashSet();
        Sources.Clear(); Fields.Clear(); Patterns.Clear();
        foreach (var source in definition.Sources)
        {
            var choice = new ProfessionalReportChoice<ProfessionalReportSource>(source.Source, Text(source.LabelArabic, source.Source.ToString()), source.Supported, source.Reason, firstLoad || selectedSources.Contains(source.Source));
            choice.PropertyChanged += (_, _) => { OnPropertyChanged(nameof(SourceCount)); OnPatternsChanged(); }; Sources.Add(choice);
        }
        foreach (var field in definition.Fields)
        {
            var choice = new ProfessionalReportChoice<ProfessionalReportField>(field.Field, Text(field.LabelArabic, field.Field.ToString()), field.Supported, field.Reason, firstLoad ? field.DefaultSelected : selectedFields.Contains(field.Field));
            choice.PropertyChanged += (_, _) => OnPropertyChanged(nameof(FieldCount)); Fields.Add(choice);
        }
        FilterBuilder.SetAvailableColumns(definition.Fields.Where(f => f.Supported).Select(f => new PetraERP.Reporting.OpenSource.ReportColumn(f.Field.ToString(), Text(f.LabelArabic, f.Field.ToString()))));
        foreach (var pattern in catalog.Patterns.Where(pattern => definition.Sources.Any(source => source.Source == pattern.Source && source.Supported)))
        {
            var choice = new ProfessionalPatternChoice(pattern, selectedPatterns.Contains(pattern.RevisionId));
            choice.PropertyChanged += (_, _) => OnPropertyChanged(nameof(PatternCount)); Patterns.Add(choice);
        }
        if (firstLoad || FromDate is null || ToDate is null)
        {
            // Al-Ameen opens a report on the year to date, not on a closed twelve-month span the operator must trim.
            FromDate = SelectedFiscalYear?.From.ToDateTime(TimeOnly.MinValue) ?? new DateTime(today.Year, 1, 1);
            ToDate = SelectedFiscalYear is { From: var from, To: var to }
                ? ClampToYear(today, from, to).ToDateTime(TimeOnly.MinValue)
                : DateTime.Today;
            CurrencyCode = IsPartyStatement || IsCurrencyBalance || Kind == ProfessionalReportKind.AccountLedger ? string.Empty : (Context?.BaseCurrencyCode ?? string.Empty);
            CurrencyBasis = IsPartyStatement ? ProfessionalCurrencyBasis.Transaction : ProfessionalCurrencyBasis.Base;
            Order = DefaultOrder;
        }
        else if (IsTrialBalance) Order = ProfessionalReportOrder.AccountCode;
        await LoadCurrencyOptionsAsync(cancellationToken).ConfigureAwait(true);
        UnsupportedOptions.Clear();
        foreach (var option in definition.Options.Where(option => !option.Supported)) UnsupportedOptions.Add(option);
        Presets.Clear();
        if (EffectiveFiscalYearId is not null)
            foreach (var preset in await presets.LoadAsync(PresetContext, Kind, cancellationToken).ConfigureAwait(true)) Presets.Add(preset);
        await ApplyNavigationAsync(parameter).ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested();
        if (IsPartyStatement && Party.Selected is not null) await RefreshPartyAccountAsync().ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested();
        Message = string.Empty;
        OnPropertyChanged(nameof(Title)); OnPropertyChanged(nameof(WorkspaceLabel)); OnPropertyChanged(nameof(VisibleSources));
        OnPropertyChanged(nameof(SourceCount)); OnPropertyChanged(nameof(FieldCount)); OnPatternsChanged();
        DefinitionChanged?.Invoke(this, EventArgs.Empty); NotifyCommands();
    }

    private async Task ApplyNavigationAsync(object? parameter)
    {
        var context = Context;
        var fiscalYear = EffectiveFiscalYearId;
        if (parameter is OpenAccountLedgerRequest ledger)
        {
            await Account.RestoreAsync(ledger.AccountId).ConfigureAwait(true);
            if (context != Context || fiscalYear != EffectiveFiscalYearId) return;
            FromDate = ledger.From?.ToDateTime(TimeOnly.MinValue) ?? FromDate;
            ToDate = ledger.To?.ToDateTime(TimeOnly.MinValue) ?? ToDate;
        }
        if (parameter is OpenPartyStatementRequest party)
        {
            await Party.RestoreAsync(party.PartyId).ConfigureAwait(true);
            if (context != Context || fiscalYear != EffectiveFiscalYearId) return;
            if (!string.IsNullOrWhiteSpace(party.CurrencyCode)) CurrencyCode = party.CurrencyCode;
        }
        if (parameter is OpenTrialBalanceRequest trial)
        {
            FromDate = trial.From?.ToDateTime(TimeOnly.MinValue) ?? FromDate; ToDate = trial.To?.ToDateTime(TimeOnly.MinValue) ?? ToDate;
            IncludeEmptyAccounts = trial.IncludeZeroRows ?? IncludeEmptyAccounts; AccountLevel = trial.MaximumLevel ?? AccountLevel;
        }
    }

    public ProfessionalReportRequest CaptureParameters()
    {
        if (EffectiveFiscalYearId is not { } fiscalYear || FromDate is not { } from || ToDate is not { } to || from > to)
            throw new InvalidOperationException(Text("السنة المالية والفترة مطلوبة، ويجب أن يكون تاريخ البداية قبل النهاية.", "A fiscal workspace and a valid date range are required."));
        // No account selected means the ledger covers every account; nothing is picked on the operator's behalf.
        if (IsPartyStatement && Party.Selected is null && Party.Items.Count > 0)
            Party.Selected = Party.Items[0];
        if (IsPartyStatement && Party.Selected is null)
            throw new InvalidOperationException(Text("اختر الزبون أو المورد من نتائج البحث.", "Select the customer or supplier from the lookup results."));
        if (IsPartyStatement && partyAccountState is PartyControlAccountState.Missing or PartyControlAccountState.Ineligible)
            throw new InvalidOperationException(PartyAccountContext);
        var sources = Sources.Where(choice => choice.IsSupported && choice.IsSelected).Select(choice => choice.Value).ToArray();
        var fields = Fields.Where(choice => choice.IsSupported && choice.IsSelected).Select(choice => choice.Value).ToArray();
        if (sources.Length == 0 || fields.Length == 0)
            throw new InvalidOperationException(Text("اختر مصدراً واحداً وعموداً واحداً على الأقل.", "Select at least one source and one column."));
        if (CurrencyBasis == ProfessionalCurrencyBasis.Transaction && CurrencyCode.Trim().Length != 3)
            throw new InvalidOperationException(Text("رمز العملة مطلوب لعرض عملة الحركة.", "A currency code is required for transaction currency."));
        return new ProfessionalReportRequest
        {
            Kind = Kind, FiscalYearId = fiscalYear, From = DateOnly.FromDateTime(from), To = DateOnly.FromDateTime(to),
            AccountId = IsPartyStatement ? null : Account.Selected?.Id, PartyId = Party.Selected?.Id, CurrencyCode = CurrencyCode.Trim(), CurrencyBasis = CurrencyBasis,
            DisplayCurrencyCode = IsCurrencyBalance || string.IsNullOrWhiteSpace(DisplayCurrencyCode) ? null : DisplayCurrencyCode.Trim(),
            Sources = sources, Fields = fields, Grouping = Grouping, Order = EffectiveOrder, BalancePresentation = BalancePresentation,
            DescriptionContains = DescriptionContains.Trim(), DescriptionNotContains = DescriptionNotContains.Trim(),
            OppositeAccountId = Supports(ProfessionalReportOption.OppositeAccount) ? OppositeAccount.Selected?.Id : null,
            CostCenterId = Supports(ProfessionalReportOption.CostCenter) ? CostCenter.Selected?.Id : null,
            PostingFrom = Date(PostingFrom), PostingTo = Date(PostingTo), CreatedFrom = Date(CreatedFrom), CreatedTo = Date(CreatedTo),
            FromSequence = FromSequence, ToSequence = ToSequence, AccountLevel = IsAccountTrial ? AccountLevel : null,
            IncludeParents = IsAccountTrial && IncludeParents, IncludePostingAccounts = !IsAccountTrial || IncludePostingAccounts,
            IncludeEmptyAccounts = IsAccountTrial && IncludeEmptyAccounts, OnlyNonZero = IsTrialBalance && OnlyNonZero,
            IncludeInvoiceDetails = Supports(ProfessionalReportOption.InvoiceLineDetails) && IncludeInvoiceDetails,
            IncludeAging = Supports(ProfessionalReportOption.Aging) && IncludeAging,
            AccountCategory = IsTrialBalance ? AccountCategory : null,
            PatternRevisionIds = Patterns.Where(pattern => pattern.IsSelected && sources.Contains(pattern.Source)).Select(pattern => pattern.RevisionId).ToArray(),
            AdvancedFilter = PetraReportFilterConverter.ToContract(FilterBuilder.Build()),
        };
    }

    private async Task ExecuteAsync()
    {
        Message = string.Empty;
        ProfessionalReportExecutionResponse? snapshot = null;
        try
        {
            await Account.EnsureSelectedAsync().ConfigureAwait(true);
            await Party.EnsureSelectedAsync().ConfigureAwait(true);
            await CostCenter.EnsureSelectedAsync().ConfigureAwait(true);
            await OppositeAccount.EnsureSelectedAsync().ConfigureAwait(true);
            var request = CaptureParameters();
            execution = CancellationTokenSource.CreateLinkedTokenSource(Token); IsExecuting = true;
            snapshot = await reports.ExecuteAsync(CompanyId, BranchId, request, execution.Token).ConfigureAwait(true);
            execution.Token.ThrowIfCancellationRequested();
            if (snapshot.CompanyId != CompanyId || snapshot.BranchId != BranchId || snapshot.FiscalYearId != EffectiveFiscalYearId)
                throw new InvalidOperationException(Text("تغير سياق التقرير؛ أعد فتحه من مساحة العمل.", "The report workspace changed. Reopen it from the workspace."));
        }
        catch (OperationCanceledException) { snapshot = null; Message = Text("أُلغي تحميل التقرير.", "Report loading cancelled."); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        { snapshot = null; Message = DescribeFailure(exception); }
        finally { IsExecuting = false; execution?.Dispose(); execution = null; }
        if (snapshot is null) return;
        // The modeless viewer opens only after execution completes, so it always receives one immutable snapshot.
        try { PreviewRequested?.Invoke(new ProfessionalPreviewRequest(snapshot, Title, Fields.ToDictionary(field => field.Value, field => field.Label), Context?.UserId ?? Guid.Empty)); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        { Message = Text("تعذر عرض المعاينة؛ بقيت الخيارات كما هي: ", "The preview could not be shown; parameters are unchanged: ") + exception.Message; }
    }

    /// <summary>Localized title plus the server problem detail and correlation identifier, so a failure is explainable and reportable.</summary>
    private int maximumRows;

    private string DescribeFailure(Exception exception)
    {
        if (exception is InvalidOperationException) return exception.Message;
        // The server refuses an over-limit result outright (nothing partial is generated); say so and how to proceed.
        if (exception is PetraApiException limit && Connection.DesktopErrors.Code(limit) == "reporting.result_limit_exceeded")
        {
            var rows = maximumRows > 0 ? maximumRows.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) : "—";
            return Text($"النتيجة الكاملة تتجاوز {rows} سجل؛ لم يُنشأ أي عرض أو تصدير جزئي. ضيّق الفترة أو نطاق الحساب، أو اطلب تصديراً كاملاً عبر الخدمة الخادمية.",
                $"The complete result exceeds {rows} rows; no partial preview or export was produced. Narrow the period or account scope, or request a full server-side export.");
        }
        var text = Localization["D_" + Connection.DesktopErrors.Key(exception)];
        if (exception is PetraApiException api && Connection.DesktopErrors.Diagnostics(api) is { Length: > 0 } diagnostics)
            text += Environment.NewLine + diagnostics;
        return text;
    }

    public void OpenJournal(ProfessionalReportRowDto row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (row.CanDrillThrough && !row.IsAggregate && row.JournalId is { } journal && Can("Ledger.Journals.View")) JournalRequested?.Invoke(journal);
    }

    public void OpenSource(ProfessionalReportRowDto row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (!row.CanDrillThrough || row.IsAggregate) return;
        if (row.DocumentId is not null && row.Source is not null) SourceRequested?.Invoke(row);
        else if (row.JournalId is not null) OpenJournal(row);
        else if (row.AccountId is not null && row.AccountId != Guid.Empty) AccountLedgerRequested?.Invoke(row);
    }

    private async Task SavePresetAsync()
    {
        try
        {
            if (Context is null) return;
            var preset = new ProfessionalReportPreset(PresetName.Trim(), CaptureParameters());
            await presets.SaveAsync(PresetContext, preset, Token).ConfigureAwait(true);
            Presets.Clear(); foreach (var value in await presets.LoadAsync(PresetContext, Kind, Token).ConfigureAwait(true)) Presets.Add(value);
            Message = Text("حُفظت الخيارات لهذا المستخدم والشركة والفرع والسنة.", "Parameters saved for this user, company, branch and fiscal year.");
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        { Message = Text("تعذر حفظ الخيارات: ", "Could not save parameters: ") + (exception is ArgumentException or InvalidOperationException ? exception.Message : Connection.DesktopErrors.Key(exception)); }
    }

    private async Task RestorePresetAsync()
    {
        if (SelectedPreset?.Parameters is not { } value || value.Kind != Kind || value.FiscalYearId != EffectiveFiscalYearId) return;
        var context = Context;
        var scopeToken = Token;
        FromDate = value.From.ToDateTime(TimeOnly.MinValue); ToDate = value.To.ToDateTime(TimeOnly.MinValue);
        await Task.WhenAll(Account.RestoreAsync(IsPartyStatement ? null : value.AccountId), Party.RestoreAsync(value.PartyId),
            OppositeAccount.RestoreAsync(value.OppositeAccountId), CostCenter.RestoreAsync(value.CostCenterId)).ConfigureAwait(true);
        if (context != Context || value.FiscalYearId != EffectiveFiscalYearId || scopeToken.IsCancellationRequested) return;
        CurrencyCode = value.CurrencyCode ?? string.Empty; CurrencyBasis = value.CurrencyBasis ?? ProfessionalCurrencyBasis.Base;
        Grouping = value.Grouping ?? ProfessionalReportGrouping.None; Order = IsTrialBalance ? ProfessionalReportOrder.AccountCode : value.Order ?? ProfessionalReportOrder.Date;
        BalancePresentation = value.BalancePresentation ?? ProfessionalBalancePresentation.Actual;
        DescriptionContains = value.DescriptionContains ?? string.Empty; DescriptionNotContains = value.DescriptionNotContains ?? string.Empty;
        PostingFrom = value.PostingFrom?.ToDateTime(TimeOnly.MinValue); PostingTo = value.PostingTo?.ToDateTime(TimeOnly.MinValue);
        CreatedFrom = value.CreatedFrom?.ToDateTime(TimeOnly.MinValue); CreatedTo = value.CreatedTo?.ToDateTime(TimeOnly.MinValue);
        FromSequence = value.FromSequence; ToSequence = value.ToSequence; AccountLevel = value.AccountLevel;
        IncludeParents = value.IncludeParents ?? false; IncludePostingAccounts = value.IncludePostingAccounts ?? true;
        IncludeEmptyAccounts = value.IncludeEmptyAccounts ?? false; OnlyNonZero = value.OnlyNonZero ?? false;
        IncludeInvoiceDetails = value.IncludeInvoiceDetails ?? false; IncludeAging = value.IncludeAging ?? false;
        foreach (var source in Sources) source.IsSelected = source.IsSupported && value.Sources?.Contains(source.Value) == true;
        foreach (var field in Fields) field.IsSelected = field.IsSupported && value.Fields?.Contains(field.Value) == true;
        foreach (var pattern in Patterns) pattern.IsSelected = value.PatternRevisionIds?.Contains(pattern.RevisionId) == true;
        AccountCategory = IsTrialBalance ? value.AccountCategory : null;
        Message = new[] { Account, Party, OppositeAccount, CostCenter }.Any(lookup => !string.IsNullOrEmpty(lookup.Error))
            ? Text("تعذرت استعادة بعض السجلات؛ راجع حقول البحث.", "Some records could not be restored; review the lookup fields.")
            : Text("استعيدت الخيارات المتاحة؛ يعيد الخادم التحقق من الصلاحيات عند المعاينة.", "Available parameters restored. The server validates permissions again when previewing.");
    }

    private static DateOnly? Date(DateTime? value) => value is { } date ? DateOnly.FromDateTime(date) : null;
    private void SelectSources(bool selected) { foreach (var source in VisibleSources) source.IsSelected = selected; }
    private void SelectFields(bool selected) { foreach (var field in Fields) field.IsSelected = selected; }
    private void SelectPatterns(bool selected) { foreach (var pattern in VisiblePatterns) pattern.IsSelected = selected; }
    private bool IsSourceSelected(ProfessionalReportSource source) => Sources.Any(choice => choice.Value == source && choice.IsSupported && choice.IsSelected);
    private void OnPatternsChanged() { OnPropertyChanged(nameof(VisiblePatterns)); OnPropertyChanged(nameof(PatternCount)); }
    partial void OnSourceSearchChanged(string value) => OnPropertyChanged(nameof(VisibleSources));
    partial void OnPatternSearchChanged(string value) => OnPatternsChanged();
    partial void OnSelectedPresetChanged(ProfessionalReportPreset? value) => RestorePresetCommand.NotifyCanExecuteChanged();
    partial void OnIsExecutingChanged(bool value) => NotifyCommands();
    partial void OnCurrencyCodeChanged(string value) => UpdatePartyCurrencyStatus();
    partial void OnToDateChanged(DateTime? value) => _ = RefreshPartyCurrenciesAsync();
    partial void OnSelectedFiscalYearChanged(ProfessionalFiscalYearDto? value)
    {
        if (value?.Id == loadedFiscalYear) return;
        Account.ClearCommand.Execute(null); Party.ClearCommand.Execute(null);
        OppositeAccount.ClearCommand.Execute(null); CostCenter.ClearCommand.Execute(null);
        if (value is null) { loadedFiscalYear = null; Presets.Clear(); SelectedPreset = null; return; }
        loadedFiscalYear = value.Id; FromDate = value.From.ToDateTime(TimeOnly.MinValue); ToDate = value.To.ToDateTime(TimeOnly.MinValue);
        Presets.Clear(); SelectedPreset = null;
        OnPropertyChanged(nameof(WorkspaceLabel));
        _ = LoadFiscalPresetsAsync(value.Id);
    }
    private async Task LoadFiscalPresetsAsync(Guid yearId)
    {
        try
        {
            if (Context is null) return;
            var values = await presets.LoadAsync(PresetContext, Kind, Token).ConfigureAwait(true);
            if (EffectiveFiscalYearId != yearId) return;
            Presets.Clear(); foreach (var value in values) Presets.Add(value);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        { Message = Text("تعذر تحميل الخيارات المحفوظة: ", "Saved parameters could not be loaded: ") + Connection.DesktopErrors.Key(exception); }
    }
    protected override void NotifyCommands()
    {
        PreviewCommand?.NotifyCanExecuteChanged(); CancelCommand?.NotifyCanExecuteChanged(); SavePresetCommand?.NotifyCanExecuteChanged(); RestorePresetCommand?.NotifyCanExecuteChanged();
        OpenPartyCardCommand?.NotifyCanExecuteChanged();
    }
    protected override void ClearPage()
    {
        ++partyCurrencyGeneration; partyCurrencyLoad?.Cancel(); partyCurrencyLoad = null; partyCurrenciesLoaded = false;
        PartyActivityCurrencies.Clear(); PartyCurrencyStatus = string.Empty;
        execution?.Cancel(); definition = null; Sources.Clear(); Fields.Clear(); Patterns.Clear(); UnsupportedOptions.Clear(); Presets.Clear();
        Account.ClearCommand.Execute(null); Party.ClearCommand.Execute(null); OppositeAccount.ClearCommand.Execute(null); CostCenter.ClearCommand.Execute(null);
        Message = string.Empty; SelectedPreset = null; FiscalYears.Clear(); SelectedFiscalYear = null; loadedFiscalYear = null;
        FromDate = null; ToDate = null; PostingFrom = null; PostingTo = null; CreatedFrom = null; CreatedTo = null;
        CurrencyCode = string.Empty; CurrencyBasis = ProfessionalCurrencyBasis.Base; DisplayCurrencyCode = string.Empty; Grouping = ProfessionalReportGrouping.None;
        Order = DefaultOrder; BalancePresentation = ProfessionalBalancePresentation.Actual;
        DescriptionContains = string.Empty; DescriptionNotContains = string.Empty; FromSequence = null; ToSequence = null; AccountLevel = null;
        IncludeParents = false; IncludePostingAccounts = true; IncludeEmptyAccounts = false; OnlyNonZero = false;
        IncludeInvoiceDetails = false; IncludeAging = false; SourceSearch = string.Empty; PatternSearch = string.Empty; AccountCategory = null; PresetName = string.Empty;
    }
}
