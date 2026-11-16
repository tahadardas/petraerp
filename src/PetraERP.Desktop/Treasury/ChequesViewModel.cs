using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;
using PetraERP.Desktop.Accounting;
using PetraERP.Desktop.Connection;
using PetraERP.Desktop.Currencies;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Parties;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Treasury;

public sealed record CommercialPaperKindOption(CommercialPaperKind? Kind, string Label);
public sealed record CommercialPaperStatusOption(CommercialPaperStatus? Status, string Label);

public sealed partial class ChequesViewModel : PageViewModel
{
    public const string ViewPermission = "Treasury.Vouchers.View";
    public const string ManagePermission = "Treasury.Vouchers.Manage";

    private readonly ICommercialPapersClient client;
    private readonly IPartiesClient partiesClient;
    private readonly IChartOfAccountsClient accountsClient;
    private readonly ShellNavigator navigator;
    private readonly IUserConfirmation confirmation;
    private readonly DocumentCurrencyReference currencyReference;
    private int generation;
    private int rateGeneration;
    private string? lastAutoRate;
    private string baseCurrency = string.Empty;

    [ObservableProperty] private CommercialPaperKind? kindFilter;
    [ObservableProperty] private CommercialPaperStatus? statusFilter;
    [ObservableProperty] private string searchQuery = string.Empty;
    [ObservableProperty] private CommercialPaperDto? selectedPaper;
    [ObservableProperty] private CommercialPaperAlertsResponse? alerts;
    [ObservableProperty] private int overdueCount;
    [ObservableProperty] private int dueTodayCount;
    [ObservableProperty] private int dueSoonCount;

    // Registration state
    [ObservableProperty] private bool isRegistering;
    [ObservableProperty] private CommercialPaperKind newPaperKind = CommercialPaperKind.ChequeReceived;
    [ObservableProperty] private string newPaperNumber = string.Empty;
    [ObservableProperty] private string newBankName = string.Empty;
    [ObservableProperty] private string? newBranchName;
    [ObservableProperty] private string newDrawerName = string.Empty;
    [ObservableProperty] private string? newPayeeName;
    [ObservableProperty] private DateTime newIssueDate = DateTime.Today;
    [ObservableProperty] private DateTime newDueDate = DateTime.Today;
    [ObservableProperty] private string newAmount = string.Empty;
    [ObservableProperty] private string newCurrencyCode = string.Empty;
    [ObservableProperty] private string newExchangeRate = string.Empty;
    /// <summary>Where the registration rate came from, or why it is missing; the operator is never left guessing.</summary>
    [ObservableProperty] private string newRateInfo = string.Empty;
    [ObservableProperty] private Guid? newPartyId;
    [ObservableProperty] private Guid? newPortfolioAccountId;
    [ObservableProperty] private string? newNotes;

    // Transition state
    [ObservableProperty] private bool isTransitioning;
    [ObservableProperty] private CommercialPaperStatus targetStatus;
    [ObservableProperty] private string transitionTitle = string.Empty;
    [ObservableProperty] private Guid? targetAccountId;
    [ObservableProperty] private Guid? endorsedPartyId;
    [ObservableProperty] private string? transitionReason;
    [ObservableProperty] private bool needsTargetAccount;
    [ObservableProperty] private bool needsEndorsedParty;

    public ObservableCollection<CommercialPaperDto> Papers { get; } = [];
    public ObservableCollection<PartyLookupDto> Parties { get; } = [];
    public ObservableCollection<Controls.LookupItem> Accounts { get; } = [];
    /// <summary>Currencies the company has enabled; the base currency is first because the server lists it first.</summary>
    public ObservableCollection<string> CurrencyOptions { get; } = [];

    public IReadOnlyList<CommercialPaperKindOption> KindOptions =>
    [
        new(null, Localization["D_AllVouchers"]),
        new(CommercialPaperKind.ChequeReceived, Localization["D_ChequesReceived"]),
        new(CommercialPaperKind.ChequeIssued, Localization["D_ChequesIssued"]),
        new(CommercialPaperKind.PromissoryNote, Localization["D_PromissoryNotes"])
    ];

    public IReadOnlyList<CommercialPaperStatusOption> StatusOptions =>
    [
        new(null, Localization["UX_Status"]),
        new(CommercialPaperStatus.Received, Localization["D_Status_Received"]),
        new(CommercialPaperStatus.UnderCollection, Localization["D_Status_UnderCollection"]),
        new(CommercialPaperStatus.Cleared, Localization["D_Status_Cleared"]),
        new(CommercialPaperStatus.Bounced, Localization["D_Status_Bounced"]),
        new(CommercialPaperStatus.Endorsed, Localization["D_Status_Endorsed"]),
        new(CommercialPaperStatus.Cancelled, Localization["D_Status_Cancelled"])
    ];

    public AsyncRelayCommand RefreshCommand { get; }
    public RelayCommand NewChequeCommand { get; }
    public AsyncRelayCommand SaveNewChequeCommand { get; }
    public RelayCommand CancelNewChequeCommand { get; }

    public RelayCommand StartDepositUnderCollectionCommand { get; }
    public RelayCommand StartClearChequeCommand { get; }
    public AsyncRelayCommand StartBounceChequeCommand { get; }
    public RelayCommand StartEndorseChequeCommand { get; }
    public AsyncRelayCommand StartCancelChequeCommand { get; }
    public AsyncRelayCommand ExecuteTransitionCommand { get; }
    public RelayCommand CancelTransitionCommand { get; }

    public ChequesViewModel(
        ICommercialPapersClient client,
        IPartiesClient partiesClient,
        IChartOfAccountsClient accountsClient,
        ICurrenciesClient currenciesClient,
        ShellNavigator navigator,
        DesktopOperation operation,
        ILocalizationService localization,
        IUserConfirmation confirmation) : base(operation, localization)
    {
        this.client = client;
        this.partiesClient = partiesClient;
        this.accountsClient = accountsClient;
        this.navigator = navigator;
        this.confirmation = confirmation;
        currencyReference = new DocumentCurrencyReference(currenciesClient);

        RefreshCommand = new AsyncRelayCommand(() => RunAsync(ListAsync), () => HasContext && Idle);
        NewChequeCommand = new RelayCommand(StartRegistering, () => HasContext && Idle && CanManage);
        SaveNewChequeCommand = new AsyncRelayCommand(SaveNewChequeAsync, () => HasContext && Idle && CanManage && IsRegistering);
        CancelNewChequeCommand = new RelayCommand(() => IsRegistering = false);

        StartDepositUnderCollectionCommand = new RelayCommand(() => OpenTransition(CommercialPaperStatus.UnderCollection, Localization["D_DepositUnderCollection"], needsAccount: true, needsParty: false), () => CanDeposit);
        StartClearChequeCommand = new RelayCommand(() => OpenTransition(CommercialPaperStatus.Cleared, Localization["D_ClearCheque"], needsAccount: true, needsParty: false), () => CanClear);
        StartBounceChequeCommand = new AsyncRelayCommand(BounceDirectAsync, () => CanBounce);
        StartEndorseChequeCommand = new RelayCommand(() => OpenTransition(CommercialPaperStatus.Endorsed, Localization["D_EndorseCheque"], needsAccount: false, needsParty: true), () => CanEndorse);
        StartCancelChequeCommand = new AsyncRelayCommand(CancelDirectAsync, () => CanCancel);

        ExecuteTransitionCommand = new AsyncRelayCommand(ExecuteTransitionAsync, () => HasContext && Idle && IsTransitioning);
        CancelTransitionCommand = new RelayCommand(() => IsTransitioning = false);
    }

    protected override string RequiredPermission => ViewPermission;
    public bool CanManage => Can(ManagePermission);

    public bool CanDeposit => SelectedPaper is not null && SelectedPaper.PaperKind == CommercialPaperKind.ChequeReceived && SelectedPaper.Status == CommercialPaperStatus.Received && Idle;
    public bool CanClear => SelectedPaper is not null && (SelectedPaper.Status == CommercialPaperStatus.UnderCollection || (SelectedPaper.PaperKind == CommercialPaperKind.ChequeIssued && SelectedPaper.Status == CommercialPaperStatus.Received)) && Idle;
    public bool CanBounce => SelectedPaper is not null && (SelectedPaper.Status == CommercialPaperStatus.Received || SelectedPaper.Status == CommercialPaperStatus.UnderCollection) && Idle;
    public bool CanEndorse => SelectedPaper is not null && SelectedPaper.PaperKind == CommercialPaperKind.ChequeReceived && SelectedPaper.Status == CommercialPaperStatus.Received && Idle;
    public bool CanCancel => SelectedPaper is not null && SelectedPaper.Status != CommercialPaperStatus.Cleared && SelectedPaper.Status != CommercialPaperStatus.Cancelled && Idle;

    partial void OnKindFilterChanged(CommercialPaperKind? value) => RefreshCommand.Execute(null);
    partial void OnStatusFilterChanged(CommercialPaperStatus? value) => RefreshCommand.Execute(null);

    partial void OnSelectedPaperChanged(CommercialPaperDto? value)
    {
        StartDepositUnderCollectionCommand.NotifyCanExecuteChanged();
        StartClearChequeCommand.NotifyCanExecuteChanged();
        StartBounceChequeCommand.NotifyCanExecuteChanged();
        StartEndorseChequeCommand.NotifyCanExecuteChanged();
        StartCancelChequeCommand.NotifyCanExecuteChanged();
    }

    partial void OnNewCurrencyCodeChanged(string value)
    {
        // The rate belongs to the currency, so the register is asked again; the old value was auto-filled, not typed.
        lastAutoRate = null;
        if (IsRegistering && NewExchangeRate.Length > 0) NewExchangeRate = string.Empty;
        RefreshNewRate();
    }

    partial void OnNewIssueDateChanged(DateTime value) => RefreshNewRate();

    protected override async Task LoadAsync(object? parameter, CancellationToken cancellationToken)
    {
        var requested = ++generation;
        await LoadLookupsAsync(cancellationToken).ConfigureAwait(true);
        if (requested != generation) return;
        await ListAsync(cancellationToken).ConfigureAwait(true);
        NotifyCommands();
    }

    private async Task LoadLookupsAsync(CancellationToken cancellationToken)
    {
        if (!HasContext) return;
        try
        {
            // Independent reads run together; the sequential form made the page restock itself list by list.
            var partiesTask = partiesClient.LookupPartiesAsync(CompanyId, BranchId, 200, null, null, cancellationToken);
            var accountsTask = Can("Ledger.Accounts.View") ? accountsClient.GetAccountTreeAsync(CompanyId, BranchId, cancellationToken) : null;
            var currenciesTask = currencyReference.ReadSettingsAsync(CompanyId, BranchId, cancellationToken);

            Parties.Clear();
            foreach (var p in await partiesTask.ConfigureAwait(true)) Parties.Add(p);

            Accounts.Clear();
            if (accountsTask is not null)
                foreach (var a in (await accountsTask.ConfigureAwait(true))
                    .Where(a => a.PostingAllowed == true && a.IsActive == true && a.Id != Guid.Empty).OrderBy(a => a.Code, StringComparer.Ordinal))
                {
                    Accounts.Add(new(a.Id, a.Code, a.NameArabic, a.NameEnglish));
                }

            CurrencyOptions.Clear();
            var settings = await currenciesTask.ConfigureAwait(true);
            baseCurrency = settings?.BaseCurrencyCode ?? string.Empty;
            foreach (var currency in settings?.Currencies ?? [])
                if (currency.Code is { Length: 3 } code && currency.IsActive == true) CurrencyOptions.Add(code);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Operation.SetError(DesktopErrors.Key(exception));
        }
    }

    /// <summary>
    /// Fills the registration rate from the company register for the chosen currency and issue date. A rate the operator
    /// typed is never overwritten, the base currency is always one, and a currency with no registered rate is reported.
    /// </summary>
    private void RefreshNewRate()
    {
        if (!IsRegistering || !HasContext || !Controls.RequiredInput.Currency(NewCurrencyCode))
        {
            NewRateInfo = string.Empty;
            return;
        }

        _ = ReadNewRateAsync(NewCurrencyCode.Trim().ToUpperInvariant(), DateOnly.FromDateTime(NewIssueDate));
    }

    private async Task ReadNewRateAsync(string currency, DateOnly date)
    {
        var attempt = ++rateGeneration;
        var token = Token;
        var typedRate = NewExchangeRate is { Length: > 0 } current && !string.Equals(current, lastAutoRate, StringComparison.Ordinal) ? current : null;
        var reading = await currencyReference.ReadRateAsync(CompanyId, BranchId, currency, date, token).ConfigureAwait(true);
        if (token.IsCancellationRequested || attempt != rateGeneration || !IsRegistering) return;
        if (reading is null)
        {
            NewRateInfo = Localization["CUR_RateUnavailable"];
            return;
        }
        var isBase = string.Equals(currency, reading.BaseCurrencyCode, StringComparison.Ordinal);
        if (reading.Found)
        {
            // The base currency is one by definition, so even a typed value is corrected to what the server states.
            if (typedRate is null || isBase) SetAutoRate(reading.Rate ?? string.Empty);
            NewRateInfo = isBase
                ? Localization["CUR_BaseRate"]
                : typedRate is not null
                    ? string.Format(CultureInfo.CurrentCulture, Localization["CUR_ExplicitRate"], reading.Rate)
                    : string.Format(CultureInfo.CurrentCulture, Localization["CUR_RateFromRegister"], reading.EffectiveDate, reading.Source);
            return;
        }
        NewRateInfo = Localization["CUR_RateMissing"];
        if (typedRate is null) SetAutoRate(string.Empty);
    }

    private void SetAutoRate(string rate)
    {
        lastAutoRate = rate;
        NewExchangeRate = rate;
    }

    private async Task ListAsync(CancellationToken cancellationToken)
    {
        if (!HasContext) return;
        try
        {
            var result = await client.ListAsync(
                CompanyId,
                BranchId,
                StatusFilter,
                KindFilter,
                null,
                null,
                null,
                string.IsNullOrWhiteSpace(SearchQuery) ? null : SearchQuery,
                cancellationToken).ConfigureAwait(true);

            Papers.Clear();
            foreach (var item in result.Items) Papers.Add(item);

            var alertsResult = await client.GetAlertsAsync(CompanyId, BranchId, 7, cancellationToken).ConfigureAwait(true);
            Alerts = alertsResult;
            OverdueCount = alertsResult.OverdueCount;
            DueTodayCount = alertsResult.DueTodayCount;
            DueSoonCount = alertsResult.DueSoonCount;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Operation.SetError(DesktopErrors.Key(exception));
        }
    }

    private void StartRegistering()
    {
        NewPaperKind = CommercialPaperKind.ChequeReceived;
        NewPaperNumber = string.Empty;
        NewBankName = string.Empty;
        NewBranchName = null;
        NewDrawerName = string.Empty;
        NewPayeeName = null;
        NewIssueDate = DateTime.Today;
        NewDueDate = DateTime.Today;
        NewAmount = string.Empty;
        NewCurrencyCode = baseCurrency;
        NewExchangeRate = string.Empty;
        NewPartyId = null;
        NewPortfolioAccountId = null;
        NewNotes = null;
        IsRegistering = true;
        IsTransitioning = false;
        lastAutoRate = null; NewRateInfo = string.Empty;
        RefreshNewRate();
        SaveNewChequeCommand.NotifyCanExecuteChanged();
    }

    private async Task SaveNewChequeAsync()
    {
        if (!HasContext) return;
        if (string.IsNullOrWhiteSpace(NewPaperNumber))
        {
            Operation.SetError(Localization["D_ChequeNumber"] + ": " + Localization["D_RequiredField"]);
            return;
        }
        if (string.IsNullOrWhiteSpace(NewBankName))
        {
            Operation.SetError(Localization["D_BankName"] + ": " + Localization["D_RequiredField"]);
            return;
        }
        if (!decimal.TryParse(NewAmount, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedAmount) || parsedAmount <= 0)
        {
            Operation.SetError(Localization["D_Amount"] + ": " + Localization["UI_InvalidNumber"]);
            return;
        }

        var req = new RegisterCommercialPaperRequest
        {
            PaperKind = NewPaperKind,
            PaperNumber = NewPaperNumber.Trim(),
            BankName = NewBankName.Trim(),
            BranchName = NewBranchName?.Trim(),
            DrawerName = NewDrawerName.Trim(),
            PayeeName = NewPayeeName?.Trim(),
            IssueDate = DateOnly.FromDateTime(NewIssueDate),
            DueDate = DateOnly.FromDateTime(NewDueDate),
            Amount = parsedAmount.ToString("F4", CultureInfo.InvariantCulture),
            CurrencyCode = string.IsNullOrWhiteSpace(NewCurrencyCode) ? null : NewCurrencyCode.Trim().ToUpperInvariant(),
            ExchangeRate = string.IsNullOrWhiteSpace(NewExchangeRate) ? null : NewExchangeRate.Trim(),
            PartyId = NewPartyId,
            PortfolioAccountId = NewPortfolioAccountId,
            Notes = NewNotes?.Trim()
        };

        try
        {
            await client.RegisterAsync(CompanyId, BranchId, req, CancellationToken.None).ConfigureAwait(true);
            IsRegistering = false;
            await ListAsync(CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Operation.SetError(DesktopErrors.Key(exception));
        }
    }

    private void OpenTransition(CommercialPaperStatus status, string title, bool needsAccount, bool needsParty)
    {
        TargetStatus = status;
        TransitionTitle = title;
        NeedsTargetAccount = needsAccount;
        NeedsEndorsedParty = needsParty;
        TargetAccountId = null;
        EndorsedPartyId = null;
        TransitionReason = string.Empty;
        IsTransitioning = true;
        IsRegistering = false;
        ExecuteTransitionCommand.NotifyCanExecuteChanged();
    }

    private async Task BounceDirectAsync()
    {
        if (SelectedPaper is null) return;
        var confirmed = await confirmation.ConfirmAsync("D_BounceCheque").ConfigureAwait(true);
        if (!confirmed) return;

        OpenTransition(CommercialPaperStatus.Bounced, Localization["D_BounceCheque"], needsAccount: false, needsParty: false);
        await ExecuteTransitionAsync().ConfigureAwait(true);
    }

    private async Task CancelDirectAsync()
    {
        if (SelectedPaper is null) return;
        var confirmed = await confirmation.ConfirmAsync("D_CancelCheque").ConfigureAwait(true);
        if (!confirmed) return;

        OpenTransition(CommercialPaperStatus.Cancelled, Localization["D_CancelCheque"], needsAccount: false, needsParty: false);
        await ExecuteTransitionAsync().ConfigureAwait(true);
    }

    private async Task ExecuteTransitionAsync()
    {
        if (!HasContext || SelectedPaper is null) return;

        if (NeedsTargetAccount && TargetAccountId is null)
        {
            Operation.SetError("Validation");
            return;
        }

        if (NeedsEndorsedParty && EndorsedPartyId is null)
        {
            Operation.SetError("Validation");
            return;
        }

        var req = new TransitionCommercialPaperStatusRequest
        {
            NewStatus = TargetStatus,
            TargetAccountId = TargetAccountId,
            EndorsedPartyId = EndorsedPartyId,
            TransitionDate = DateOnly.FromDateTime(DateTime.Today),
            Reason = TransitionReason?.Trim()
        };

        try
        {
            await client.TransitionStatusAsync(CompanyId, BranchId, SelectedPaper.Id, req, CancellationToken.None).ConfigureAwait(true);
            IsTransitioning = false;
            await ListAsync(CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Operation.SetError(DesktopErrors.Key(exception));
        }
    }

    public void Localize()
    {
        OnPropertyChanged(nameof(KindOptions));
        OnPropertyChanged(nameof(StatusOptions));
    }

    protected override void ClearPage()
    {
        Papers.Clear();
        Parties.Clear();
        Accounts.Clear();
        CurrencyOptions.Clear();
        baseCurrency = string.Empty;
        SelectedPaper = null;
        Alerts = null;
        OverdueCount = 0;
        DueTodayCount = 0;
        DueSoonCount = 0;
        IsRegistering = false;
        IsTransitioning = false;
    }

    protected override void NotifyCommands()
    {
        base.NotifyCommands();
        RefreshCommand.NotifyCanExecuteChanged();
        NewChequeCommand.NotifyCanExecuteChanged();
        SaveNewChequeCommand.NotifyCanExecuteChanged();
        ExecuteTransitionCommand.NotifyCanExecuteChanged();
        OnSelectedPaperChanged(SelectedPaper);
    }
}
