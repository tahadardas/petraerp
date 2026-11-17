using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;
using PetraERP.Desktop.Accounting;
using PetraERP.Desktop.Currencies;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Parties;
using PetraERP.Desktop.Patterns;
using PetraERP.Desktop.Sales;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Treasury;

/// <summary>Opens the voucher workstation on one voucher.</summary>
public sealed record OpenVoucherRequest(Guid VoucherId);

/// <summary>
/// Starts a new voucher, optionally for a party and pre-allocated to one of its open documents: the sales invoice of a receipt or the
/// purchase invoice of a payment. The outstanding list is loaded for the voucher's kind, so the id is matched against that page.
/// </summary>
public sealed record NewVoucherRequest(VoucherKind Kind, Guid? PartyId = null, Guid? InvoiceId = null);

/// <summary>An outstanding invoice and the amount this receipt allocates to it (blank = not allocated).</summary>
public sealed partial class VoucherAllocationItem : ObservableObject
{
    public Guid InvoiceId { get; init; }
    public string InvoiceNumber { get; init; } = string.Empty;
    public string DateDisplay { get; init; } = string.Empty;
    public decimal Outstanding { get; init; }
    public string CurrencyCode { get; init; } = string.Empty;
    public string StateKey { get; init; } = string.Empty;
    [ObservableProperty] private string stateDisplay = string.Empty;
    public string TotalDisplay { get; init; } = string.Empty;
    public string PaidDisplay { get; init; } = string.Empty;
    public string OutstandingDisplay => Outstanding.ToString("F4", CultureInfo.InvariantCulture);
    public Action? Changed { get; set; }

    [ObservableProperty] private string amount = string.Empty;
    [ObservableProperty] private string settlementAmount = string.Empty;
    [ObservableProperty] private string crossRate = "1";

    partial void OnAmountChanged(string value)
    {
        if (crossRate == "1" || string.IsNullOrWhiteSpace(crossRate))
        {
            settlementAmount = value;
            OnPropertyChanged(nameof(SettlementAmount));
        }
        Changed?.Invoke();
    }

    partial void OnSettlementAmountChanged(string value)
    {
        if (crossRate == "1" || string.IsNullOrWhiteSpace(crossRate))
        {
            amount = value;
            OnPropertyChanged(nameof(Amount));
        }
        Changed?.Invoke();
    }
    partial void OnCrossRateChanged(string value) => Changed?.Invoke();
}

/// <summary>A payment distribution line: a supplier or a posting account.</summary>
public sealed partial class VoucherLineItem : ObservableObject, Controls.IDocumentEntryRow
{
    public Action? Changed { get; set; }

    public bool IsPersisted { get; set; }

    [ObservableProperty] private Guid? partyId;
    [ObservableProperty] private Guid? accountId;
    [ObservableProperty] private Guid? costCenterId;
    [ObservableProperty] private string amount = string.Empty;
    [ObservableProperty] private string? description;

    public bool IsPristineBlank =>
        !IsPersisted
        && PartyId is null
        && AccountId is null
        && CostCenterId is null
        && string.IsNullOrWhiteSpace(Description)
        && (string.IsNullOrWhiteSpace(Amount) || Parse(Amount) == 0m);

    private static decimal? Parse(string? text) =>
        decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : null;

    public bool IsComplete =>
        ((PartyId is not null && AccountId is null) || (PartyId is null && AccountId is not null))
        && decimal.TryParse(Amount, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
        && parsed > 0m;

    public bool IsIncomplete => !IsPristineBlank && !IsComplete;

    public Controls.DocumentRowState State =>
        IsPersisted ? Controls.DocumentRowState.Persisted :
        IsPristineBlank ? Controls.DocumentRowState.PristineBlank :
        IsComplete ? Controls.DocumentRowState.Complete :
        Controls.DocumentRowState.Incomplete;

    public Controls.DocumentRowState RowState => State;

    public string? RowErrorCode =>
        IsPristineBlank || IsComplete ? null :
        (PartyId is null && AccountId is null) ? "D_LineAccountOrParty" :
        (PartyId is not null && AccountId is not null) ? "D_LineAccountAndParty" :
        !decimal.TryParse(Amount, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var parsed) || parsed <= 0m ? "D_LineAmountInvalid" :
        "D_LineDataIncomplete";

    public string? RowError => RowErrorCode switch
    {
        null => null,
        "D_LineAccountOrParty" => "يجب تحديد الحساب أو الطرف",
        "D_LineAccountAndParty" => "لا يمكن تحديد الحساب والطرف معاً",
        "D_LineAmountInvalid" => "المبلغ غير صالح",
        _ => "بيانات السطر غير مكتملة",
    };

    partial void OnPartyIdChanged(Guid? value) { if (value is not null) AccountId = null; NotifyState(); Changed?.Invoke(); }
    partial void OnAccountIdChanged(Guid? value) { if (value is not null) PartyId = null; NotifyState(); Changed?.Invoke(); }
    partial void OnCostCenterIdChanged(Guid? value) { NotifyState(); Changed?.Invoke(); }
    partial void OnAmountChanged(string value) { NotifyState(); Changed?.Invoke(); }
    partial void OnDescriptionChanged(string? value) { NotifyState(); Changed?.Invoke(); }

    private void NotifyState()
    {
        OnPropertyChanged(nameof(IsPristineBlank));
        OnPropertyChanged(nameof(IsComplete));
        OnPropertyChanged(nameof(IsIncomplete));
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(RowState));
        OnPropertyChanged(nameof(RowErrorCode));
        OnPropertyChanged(nameof(RowError));
    }

    public static VoucherLineItem CreateBlankSlot(Action? changed = null) => new() { Changed = changed };
}

public sealed record VoucherKindOption(VoucherKind? Kind, string Label);

public sealed record SettlementOption(SettlementMethod Method, string Label);

public sealed partial class VouchersViewModel : PageViewModel, ISavablePage
{
    public bool CanSaveBeforeClose => EditorHasChanges && !IsApplyingAdvance && HasContext && Idle && IsDraftComplete();
    public async Task<bool> SaveBeforeCloseAsync()
    {
        if (!CanSaveBeforeClose) return false;
        await SaveCommand.ExecuteAsync(null).ConfigureAwait(true);
        return !EditingSession;
    }
    public const string ViewPermission = "Treasury.Vouchers.View";
    public const string ManagePermission = "Treasury.Vouchers.Manage";
    public const string PostPermission = "Treasury.Vouchers.Post";
    public const string ReversePermission = "Treasury.Vouchers.Reverse";

    private readonly ITreasuryVouchersClient client;
    private readonly IPartiesClient partiesClient;
    private readonly IPatternsClient patternsClient;
    private readonly IChartOfAccountsClient accountsClient;
    private readonly ShellNavigator navigator;
    private readonly IUserConfirmation confirmation;
    private readonly DocumentNumberPeek numberPeek;
    private readonly DocumentCurrencyReference currencyReference;
    private int generation;
    private int voucherPage = 1;
    private int rateGeneration;
    private string? lastAutoRate;
    private string baseCurrency = string.Empty;

    [ObservableProperty] private VoucherKind? kindFilter;
    [ObservableProperty] private VoucherSummaryDto? selectedVoucher;
    [ObservableProperty] private int totalCount;
    [ObservableProperty] private Guid? filterPartyId;
    [ObservableProperty] private int workspaceTab;

    /// <summary>The number this draft would most likely get, read without reserving it.</summary>
    [ObservableProperty] private string? numberPreview;

    public VouchersViewModel(ITreasuryVouchersClient client, IPartiesClient partiesClient, IPatternsClient patternsClient,
        IChartOfAccountsClient accountsClient, ICurrenciesClient currenciesClient, ShellNavigator navigator, DesktopOperation operation,
        ILocalizationService localization, IUserConfirmation confirmation) : base(operation, localization)
    {
        this.client = client; this.partiesClient = partiesClient; this.patternsClient = patternsClient;
        this.accountsClient = accountsClient; this.navigator = navigator; this.confirmation = confirmation;
        numberPeek = new DocumentNumberPeek(patternsClient);
        currencyReference = new DocumentCurrencyReference(currenciesClient);
        RefreshCommand = new AsyncRelayCommand(() => RunAsync(ListAsync), () => HasContext && Idle);
        OpenCommand = new AsyncRelayCommand(() => SelectedVoucher?.Id is { } id ? RunAsync(t => OpenAsync(id, t)) : Task.CompletedTask,
            () => HasContext && Idle && SelectedVoucher?.Id is not null);
        NewReceiptCommand = new AsyncRelayCommand(() => StartNewAsync(VoucherKind.Receipt, null), () => HasContext && Idle && CanManage);
        NewPaymentCommand = new AsyncRelayCommand(() => StartNewAsync(VoucherKind.Payment, null), () => HasContext && Idle && CanManage);
        NewByPatternCommand = new AsyncRelayCommand<VoucherPatternButton>(async button =>
        {
            if (button is null || !await StartNewAsync(button.Kind, null).ConfigureAwait(true)) return;
            DraftPatternRevisionId = button.Pattern.RevisionId;
            lockedPatternRevisionId = button.Pattern.RevisionId;
            OnPropertyChanged(nameof(PatternPickerEnabled));
        }, button => button is not null && HasContext && Idle && CanManage);
        PreviousVouchersCommand = new(() => ChangeVoucherPageAsync(-1), () => HasContext && Idle && voucherPage > 1);
        NextVouchersCommand = new(() => ChangeVoucherPageAsync(1), () => HasContext && Idle && (long)voucherPage * 50 < TotalCount);
        BulkPostCommand = new AsyncRelayCommand(BulkPostAsync, () => HasContext && Idle && CanManagePostingBatches);
        InitializeEditor();
    }

    protected override string RequiredPermission => ViewPermission;
    public bool CanManage => Can(ManagePermission);
    public bool CanPostPermission => Can(PostPermission);
    public bool CanReversePermission => Can(ReversePermission);

    public string VoucherPageStatus => $"{voucherPage} / {TotalCount}";
    public IEnumerable<PartyLookupDto> FilterParties => Customers.Concat(Suppliers).DistinctBy(p => p.Id);
    public AsyncRelayCommand PreviousVouchersCommand { get; }
    public AsyncRelayCommand NextVouchersCommand { get; }

    public ObservableCollection<VoucherSummaryDto> Vouchers { get; } = [];
    public ObservableCollection<PatternLookupItem> ReceiptPatterns { get; } = [];
    public ObservableCollection<PatternLookupItem> PaymentPatterns { get; } = [];
    /// <summary>One button per receipt or payment pattern: a new voucher already fixed on that pattern.</summary>
    public ObservableCollection<VoucherPatternButton> PatternButtons { get; } = [];
    public AsyncRelayCommand<VoucherPatternButton> NewByPatternCommand { get; }
    public ObservableCollection<PartyLookupDto> Customers { get; } = [];
    public ObservableCollection<PartyLookupDto> Suppliers { get; } = [];
    public ObservableCollection<Controls.LookupItem> Accounts { get; } = [];
    public ObservableCollection<CostCenterResponse> CostCenters { get; } = [];
    /// <summary>Currencies the company has enabled; the base currency is first because the server lists it first.</summary>
    public ObservableCollection<string> CurrencyOptions { get; } = [];
    public IReadOnlyList<VoucherKindOption> KindOptions =>
        [new(null, Localization["D_AllVouchers"]), new(VoucherKind.Receipt, Localization["D_ReceiptVouchers"]), new(VoucherKind.Payment, Localization["D_PaymentVouchers"])];
    public IReadOnlyList<SettlementOption> SettlementOptions =>
        [new(SettlementMethod.Cash, Localization["D_SettlementCash"]), new(SettlementMethod.Bank, Localization["D_SettlementBank"])];

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand OpenCommand { get; }
    public AsyncRelayCommand NewReceiptCommand { get; }
    public AsyncRelayCommand NewPaymentCommand { get; }
    public AsyncRelayCommand BulkPostCommand { get; }
    public bool CanManagePostingBatches => Can(DocumentProcessing.PostingManagementViewModel.ManagePermission);

    partial void OnKindFilterChanged(VoucherKind? value) { voucherPage = 1; RefreshCommand.Execute(null); }
    partial void OnFilterPartyIdChanged(Guid? value) { voucherPage = 1; RefreshCommand.Execute(null); }
    partial void OnSelectedVoucherChanged(VoucherSummaryDto? value) => OpenCommand.NotifyCanExecuteChanged();

    /// <summary>
    /// Asks the server, without reserving, for the number this draft would most likely get, so leaving a voucher unsaved
    /// consumes nothing and a saved voucher always carries the number allocated at save time.
    /// </summary>
    private void RefreshNumberPreview()
    {
        var patterns = DraftKind == VoucherKind.Payment ? PaymentPatterns : ReceiptPatterns;
        if (!IsEditing || !HasContext || !string.IsNullOrWhiteSpace(Current?.VoucherNumber)
            || patterns.FirstOrDefault(p => p.RevisionId == DraftPatternRevisionId) is not { } pattern)
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
    /// Fills the rate from the company register for the chosen currency and voucher date. A rate the operator typed is
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
            case OpenVoucherRequest open: await OpenAsync(open.VoucherId, cancellationToken).ConfigureAwait(true); break;
            case NewVoucherRequest create:
                if (!await StartNewAsync(create.Kind, create.PartyId).ConfigureAwait(true)) break;
                if (create.PartyId is not null)
                    await LoadOutstandingCoreAsync(create.InvoiceId, cancellationToken).ConfigureAwait(true);
                break;
        }
        NotifyCommands();
    }

    public void Localize()
    {
        OnPropertyChanged(nameof(KindOptions)); OnPropertyChanged(nameof(SettlementOptions)); OnPropertyChanged(nameof(InvoiceStateOptions));
        foreach (var row in AllocationRows.Where(r => r.StateKey.Length > 0)) row.StateDisplay = Localization[row.StateKey];
        var sources = SettlementBatches.Select(b => b.Source).ToArray(); var selected = SelectedBatch?.Source.Id;
        SettlementBatches.Clear();
        foreach (var source in sources) SettlementBatches.Add(new VoucherSettlementHistory(source, Localization));
        SelectedBatch = SettlementBatches.FirstOrDefault(b => b.Source.Id == selected);
        RefreshEditor();
    }

    private async Task LoadLookupsAsync(CancellationToken cancellationToken)
    {
        // Independent reads run together; the sequential form left the workstation refilling itself one list at a time.
        var patternsTask = patternsClient.GetRunnablePatternsAsync(CompanyId, BranchId, cancellationToken);
        var customersTask = partiesClient.LookupPartiesAsync(CompanyId, BranchId, 200, null, PartyRole.Customer, cancellationToken);
        var suppliersTask = partiesClient.LookupPartiesAsync(CompanyId, BranchId, 200, null, PartyRole.Supplier, cancellationToken);
        var currenciesTask = currencyReference.ReadSettingsAsync(CompanyId, BranchId, cancellationToken);
        var accountsTask = Can("Ledger.Accounts.View") ? accountsClient.GetAccountTreeAsync(CompanyId, BranchId, cancellationToken) : null;
        var costCentersTask = Can("Ledger.CostCenters.View") ? accountsClient.SearchCostCentersAsync(CompanyId, BranchId, null, true, 1, 200, cancellationToken) : null;

        var patterns = await patternsTask.ConfigureAwait(true);
        var revisionTasks = patterns
            .Where(p => p.Id.HasValue && p.OperationKind is BusinessOperationKind.CashReceipt or BusinessOperationKind.CashPayment)
            .Select(ReadVoucherPatternAsync)
            .ToArray();

        ReceiptPatterns.Clear(); PaymentPatterns.Clear();
        foreach (var entry in await Task.WhenAll(revisionTasks).ConfigureAwait(true))
            if (entry is { } read) (read.Pattern == BusinessOperationKind.CashReceipt ? ReceiptPatterns : PaymentPatterns).Add(read.Item);
        PatternButtons.Clear();
        foreach (var pattern in ReceiptPatterns) PatternButtons.Add(new VoucherPatternButton(pattern, VoucherKind.Receipt));
        foreach (var pattern in PaymentPatterns) PatternButtons.Add(new VoucherPatternButton(pattern, VoucherKind.Payment));

        async Task<(BusinessOperationKind Pattern, PatternLookupItem Item)?> ReadVoucherPatternAsync(DocumentPatternSummaryResponse pattern)
        {
            var detail = await patternsClient.GetPatternAsync(CompanyId, BranchId, pattern.Id!.Value, cancellationToken).ConfigureAwait(false);
            if (detail.ActiveRevision?.Id is not { } revision) return null;
            var cfg = detail.ActiveRevision?.Configuration;
            var paymentMethod = cfg?.AllowedPaymentMethods is { Count: > 0 } methods ? methods[0] : null;
            return (pattern.OperationKind!.Value, new PatternLookupItem
            {
                AllowManualNumbering = cfg?.NumberingPolicy?.AllowManualNumbering == true,
                LockDocumentDate = cfg?.EntryPolicy?.LockDocumentDate == true,
                FieldCaptions = (cfg?.LineFields ?? []).Where(f => !string.IsNullOrWhiteSpace(f.FieldName) && !string.IsNullOrWhiteSpace(f.CaptionArabic))
                    .GroupBy(f => f.FieldName!, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().CaptionArabic!.Trim(), StringComparer.OrdinalIgnoreCase),
                PatternId = pattern.Id!.Value,
                RevisionId = revision,
                Name = pattern.NameArabic ?? pattern.Code ?? string.Empty,
                DefaultCurrencyCode = cfg?.DefaultCurrencyCode,
                DefaultCostCenterId = cfg?.DefaultCostCenterId,
                DefaultPaymentMethod = paymentMethod,
            });
        }

        Customers.Clear(); Suppliers.Clear();
        foreach (var c in await customersTask.ConfigureAwait(true)) Customers.Add(c);
        foreach (var s in await suppliersTask.ConfigureAwait(true)) Suppliers.Add(s);
        Accounts.Clear();
        if (accountsTask is not null)
            foreach (var a in (await accountsTask.ConfigureAwait(true))
                .Where(a => a.PostingAllowed == true && a.IsActive == true && a.Id != Guid.Empty).OrderBy(a => a.Code, StringComparer.Ordinal))
                Accounts.Add(new(a.Id, a.Code, a.NameArabic, a.NameEnglish));
        OnPropertyChanged(nameof(FilterParties));
        CurrencyOptions.Clear();
        var settings = await currenciesTask.ConfigureAwait(true);
        baseCurrency = settings?.BaseCurrencyCode ?? string.Empty;
        if (!string.IsNullOrEmpty(baseCurrency)) CurrencyOptions.Add(baseCurrency);
        foreach (var currency in settings?.Currencies ?? [])
            if (currency.Code is { Length: 3 } code && currency.IsActive == true && !CurrencyOptions.Contains(code)) CurrencyOptions.Add(code);
        if (string.IsNullOrWhiteSpace(DraftCurrency) && CurrencyOptions.Count > 0)
            DraftCurrency = CurrencyOptions[0];
        CostCenters.Clear();
        if (costCentersTask is not null)
            foreach (var c in (await costCentersTask.ConfigureAwait(true)).Items ?? []) CostCenters.Add(c);
    }

    private async Task ListAsync(CancellationToken cancellationToken)
    {
        var page = await client.ListAsync(CompanyId, BranchId, KindFilter, FilterPartyId, null, voucherPage, 50, cancellationToken).ConfigureAwait(true);
        Vouchers.Clear();
        foreach (var v in page.Items ?? []) Vouchers.Add(v);
        TotalCount = page.TotalCount ?? Vouchers.Count;
        OnPropertyChanged(nameof(VoucherPageStatus)); NotifyCommands();
    }
    private async Task ChangeVoucherPageAsync(int offset)
    {
        var previous = voucherPage; voucherPage += offset;
        if (!await RunAsync(ListAsync).ConfigureAwait(true)) voucherPage = previous;
        OnPropertyChanged(nameof(VoucherPageStatus)); NotifyCommands();
    }

    protected override void ClearPage()
    {
        generation++;
        Vouchers.Clear(); ReceiptPatterns.Clear(); PaymentPatterns.Clear(); PatternButtons.Clear(); Customers.Clear(); Suppliers.Clear(); Accounts.Clear(); CostCenters.Clear();
        CurrencyOptions.Clear(); baseCurrency = string.Empty;
        SelectedVoucher = null; TotalCount = 0; voucherPage = 1; FilterPartyId = null;
        ResetEditor();
    }

    protected override void NotifyCommands()
    {
        OnPropertyChanged(nameof(CanUseManualNumber));
        base.NotifyCommands();
        RefreshCommand?.NotifyCanExecuteChanged(); OpenCommand?.NotifyCanExecuteChanged();
        NewReceiptCommand?.NotifyCanExecuteChanged(); NewByPatternCommand?.NotifyCanExecuteChanged(); NewPaymentCommand?.NotifyCanExecuteChanged();
        PreviousVouchersCommand?.NotifyCanExecuteChanged(); NextVouchersCommand?.NotifyCanExecuteChanged();
        RefreshEditor();
        OnPropertyChanged(nameof(CanManage)); OnPropertyChanged(nameof(CanPostPermission)); OnPropertyChanged(nameof(CanReversePermission));
        OnPropertyChanged(nameof(CanManagePostingBatches)); BulkPostCommand?.NotifyCanExecuteChanged();
    }

    private Task BulkPostAsync() => navigator.NavigateAsync("posting-management",
        new DocumentProcessing.BulkPostingNavigation(BulkPostingDocumentType.Voucher));
}
