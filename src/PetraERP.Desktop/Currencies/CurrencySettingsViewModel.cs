using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;
using PetraERP.Desktop.Controls;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Currencies;

/// <summary>
/// Company currencies and the exchange-rate register. The page collects input and shows server state only: the base
/// currency lock, enabled currencies and append-only rate entries (effective date, source, user, reason) all come from the
/// API, which enforces every rule. No currency or rate is pre-filled by the client.
/// </summary>
public sealed partial class CurrencySettingsViewModel : PageViewModel
{
    public const string ViewPermission = "Ledger.Accounts.View";
    public const string ManagePermission = "Ledger.Accounts.Manage";

    private readonly ICurrenciesClient client;
    private readonly NotificationCenter notifications;
    private int ratePage = 1;
    private long rateTotal;
    private bool suppressingRateReload;

    [ObservableProperty] private string baseCurrencyCode = string.Empty;
    [ObservableProperty] private bool baseCurrencyLocked;
    [ObservableProperty] private string? baseCurrencyLockReason;

    [ObservableProperty] private string newCurrencyCode = string.Empty;
    [ObservableProperty] private string newCurrencyNameArabic = string.Empty;
    [ObservableProperty] private string? newCurrencyNameEnglish;
    [ObservableProperty] private string enableReason = string.Empty;

    [ObservableProperty] private CompanyCurrencyDto? selectedCurrency;
    [ObservableProperty] private string cardSymbol = string.Empty;
    [ObservableProperty] private string cardMinorAr = string.Empty;
    [ObservableProperty] private string cardMinorEn = string.Empty;
    [ObservableProperty] private string cardDecimalPlaces = string.Empty;
    [ObservableProperty] private string? cardRoundingMode;
    [ObservableProperty] private string cardReason = string.Empty;

    [ObservableProperty] private string? newBaseCurrencyCode;
    [ObservableProperty] private string baseChangeReason = string.Empty;

    [ObservableProperty] private string? rateFilterCurrency;
    [ObservableProperty] private DateTime? rateFilterFrom;
    [ObservableProperty] private DateTime? rateFilterTo;
    [ObservableProperty] private string? rateCurrencyCode;
    [ObservableProperty] private string rateValue = string.Empty;
    [ObservableProperty] private DateTime? rateEffectiveDate = DateTime.Today;
    [ObservableProperty] private string rateSource = string.Empty;
    [ObservableProperty] private string rateReason = string.Empty;

    [ObservableProperty] private string? lookupCurrencyCode;
    [ObservableProperty] private DateTime? lookupDate = DateTime.Today;
    [ObservableProperty] private string lookupResult = string.Empty;

    public CurrencySettingsViewModel(ICurrenciesClient client, NotificationCenter notifications, DesktopOperation operation, ILocalizationService localization)
        : base(operation, localization)
    {
        this.client = client;
        this.notifications = notifications;
        RefreshCommand = new AsyncRelayCommand(() => RunAsync(LoadAllAsync), () => HasContext && Idle);
        EnableCurrencyCommand = new AsyncRelayCommand(EnableAsync, () => HasContext && Idle && CanManage
            && RequiredInput.Currency(NewCurrencyCode) && !string.IsNullOrWhiteSpace(EnableReason));
        SaveCardCommand = new AsyncRelayCommand(SaveCardAsync, () => HasContext && Idle && CanManage && SelectedCurrency?.Code is { Length: > 0 }
            && int.TryParse(CardDecimalPlaces, NumberStyles.None, CultureInfo.InvariantCulture, out var dp) && dp is >= 0 and <= 8
            && !string.IsNullOrWhiteSpace(CardRoundingMode) && !string.IsNullOrWhiteSpace(CardReason));
        ChangeBaseCommand = new AsyncRelayCommand(ChangeBaseAsync, () => HasContext && Idle && CanManage && !BaseCurrencyLocked
            && RequiredInput.Currency(NewBaseCurrencyCode) && !string.Equals(NewBaseCurrencyCode, BaseCurrencyCode, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(BaseChangeReason));
        RegisterRateCommand = new AsyncRelayCommand(RegisterRateAsync, () => HasContext && Idle && CanManage && RequiredInput.Currency(RateCurrencyCode)
            && RequiredInput.AmountText(RateValue, 8, true) && RateEffectiveDate.HasValue && !string.IsNullOrWhiteSpace(RateSource)
            && !string.IsNullOrWhiteSpace(RateReason));
        LookupCommand = new AsyncRelayCommand(LookupAsync, () => HasContext && Idle && RequiredInput.Currency(LookupCurrencyCode) && LookupDate.HasValue);
        PreviousRatesCommand = new AsyncRelayCommand(() => MoveRatesAsync(-1), () => HasContext && Idle && ratePage > 1);
        NextRatesCommand = new AsyncRelayCommand(() => MoveRatesAsync(1), () => HasContext && Idle && ratePage * 50L < rateTotal);
        ClearRateFilterCommand = new RelayCommand(() =>
        {
            suppressingRateReload = true;
            try { RateFilterCurrency = null; RateFilterFrom = null; RateFilterTo = null; }
            finally { suppressingRateReload = false; }
            ReloadRates();
        }, () => HasContext && Idle);
    }

    public ObservableCollection<CompanyCurrencyDto> Currencies { get; } = [];
    /// <summary>Enabled non-base currencies: the only ones a register entry can target.</summary>
    public ObservableCollection<string> ForeignCurrencyCodes { get; } = [];
    public ObservableCollection<string> AllCurrencyCodes { get; } = [];
    public ObservableCollection<ExchangeRateDto> Rates { get; } = [];

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand EnableCurrencyCommand { get; }
    public AsyncRelayCommand ChangeBaseCommand { get; }
    public AsyncRelayCommand SaveCardCommand { get; }
    public IReadOnlyList<string> RoundingModes { get; } = ["HalfAwayFromZero", "HalfToEven", "TowardZero", "AwayFromZero"];
    public AsyncRelayCommand RegisterRateCommand { get; }
    public AsyncRelayCommand LookupCommand { get; }
    public AsyncRelayCommand PreviousRatesCommand { get; }
    public AsyncRelayCommand NextRatesCommand { get; }
    public RelayCommand ClearRateFilterCommand { get; }
    public string RatesPageText => string.Format(CultureInfo.CurrentCulture, Localization["CUR_RatesPage"], ratePage, rateTotal);

    public bool CanManage => Can(ManagePermission);
    public bool BaseCurrencyEditable => CanManage && !BaseCurrencyLocked;
    public string BaseLockText => BaseCurrencyLocked
        ? string.Format(CultureInfo.CurrentCulture, Localization["CUR_BaseLocked"], BaseCurrencyLockReason ?? string.Empty)
        : Localization["CUR_BaseOpen"];

    protected override string RequiredPermission => ViewPermission;

    protected override Task LoadAsync(object? parameter, CancellationToken cancellationToken) => RunAsync(LoadAllAsync);

    protected override void ClearPage()
    {
        SelectedCurrency = null;
        Currencies.Clear(); ForeignCurrencyCodes.Clear(); AllCurrencyCodes.Clear(); Rates.Clear();
        BaseCurrencyCode = string.Empty; BaseCurrencyLocked = false; BaseCurrencyLockReason = null;
        NewCurrencyCode = string.Empty; NewCurrencyNameArabic = string.Empty; NewCurrencyNameEnglish = null; EnableReason = string.Empty;
        NewBaseCurrencyCode = null; BaseChangeReason = string.Empty;
        RateFilterCurrency = null; RateFilterFrom = null; RateFilterTo = null;
        RateCurrencyCode = null; RateValue = string.Empty; RateEffectiveDate = DateTime.Today; RateSource = string.Empty; RateReason = string.Empty;
        LookupCurrencyCode = null; LookupDate = DateTime.Today; LookupResult = string.Empty;
        ratePage = 1; rateTotal = 0; OnPropertyChanged(nameof(RatesPageText));
    }

    private async Task LoadAllAsync(CancellationToken cancellationToken)
    {
        Apply(await client.GetSettingsAsync(CompanyId, BranchId, cancellationToken).ConfigureAwait(true));
        await LoadRatesAsync(cancellationToken).ConfigureAwait(true);
    }

    private async Task LoadRatesAsync(CancellationToken cancellationToken)
    {
        var page = await client.ListRatesAsync(CompanyId, BranchId,
            string.IsNullOrWhiteSpace(RateFilterCurrency) ? null : RateFilterCurrency,
            RateFilterFrom is { } from ? DateOnly.FromDateTime(from.Date) : null,
            RateFilterTo is { } to ? DateOnly.FromDateTime(to.Date) : null,
            ratePage, 50, cancellationToken).ConfigureAwait(true);
        Rates.Clear();
        foreach (var rate in page.Items ?? []) Rates.Add(rate);
        rateTotal = page.TotalCount ?? 0; OnPropertyChanged(nameof(RatesPageText)); NotifyCommands();
    }

    private Task<bool> MoveRatesAsync(int delta) => RunAsync(async token =>
    {
        ratePage += delta;
        await LoadRatesAsync(token).ConfigureAwait(true);
    });

    private void Apply(CompanyCurrencySettingsResponse settings)
    {
        BaseCurrencyCode = settings.BaseCurrencyCode ?? string.Empty;
        BaseCurrencyLocked = settings.BaseCurrencyLocked == true;
        BaseCurrencyLockReason = settings.BaseCurrencyLockReason;
        var keep = SelectedCurrency?.Code;
        Currencies.Clear(); ForeignCurrencyCodes.Clear(); AllCurrencyCodes.Clear();
        foreach (var currency in settings.Currencies ?? [])
        {
            Currencies.Add(currency);
            if (currency.Code is not { Length: > 0 } code || currency.IsActive != true) continue;
            AllCurrencyCodes.Add(code);
            if (currency.IsBase != true) ForeignCurrencyCodes.Add(code);
        }
        if (keep is not null) SelectedCurrency = Currencies.FirstOrDefault(c => c.Code == keep);
        OnPropertyChanged(nameof(BaseLockText));
        NotifyCommands();
    }

    private async Task EnableAsync()
    {
        var request = new EnableCurrencyRequest
        {
            Code = NewCurrencyCode.Trim().ToUpperInvariant(),
            NameArabic = string.IsNullOrWhiteSpace(NewCurrencyNameArabic) ? null : NewCurrencyNameArabic.Trim(),
            NameEnglish = string.IsNullOrWhiteSpace(NewCurrencyNameEnglish) ? null : NewCurrencyNameEnglish.Trim(),
            Reason = EnableReason.Trim(),
        };
        if (await RunAsync(async token => Apply(await client.EnableCurrencyAsync(CompanyId, BranchId, request, token).ConfigureAwait(true))).ConfigureAwait(true))
        {
            NewCurrencyCode = string.Empty; NewCurrencyNameArabic = string.Empty; NewCurrencyNameEnglish = null; EnableReason = string.Empty;
            notifications.Success("D_Saved");
        }
    }

    partial void OnSelectedCurrencyChanged(CompanyCurrencyDto? value)
    {
        CardSymbol = value?.Symbol ?? string.Empty;
        CardMinorAr = value?.MinorUnitNameArabic ?? string.Empty;
        CardMinorEn = value?.MinorUnitNameEnglish ?? string.Empty;
        CardDecimalPlaces = value?.DecimalPlaces?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        CardRoundingMode = value?.RoundingMode;
        CardReason = string.Empty;
        NotifyCommands();
    }

    partial void OnCardSymbolChanged(string value) => NotifyCommands();
    partial void OnCardDecimalPlacesChanged(string value) => NotifyCommands();
    partial void OnCardRoundingModeChanged(string? value) => NotifyCommands();
    partial void OnCardReasonChanged(string value) => NotifyCommands();

    private async Task SaveCardAsync()
    {
        var code = SelectedCurrency!.Code!;
        var request = new UpdateCurrencyCardRequest
        {
            Symbol = string.IsNullOrWhiteSpace(CardSymbol) ? null : CardSymbol.Trim(),
            MinorUnitNameArabic = string.IsNullOrWhiteSpace(CardMinorAr) ? null : CardMinorAr.Trim(),
            MinorUnitNameEnglish = string.IsNullOrWhiteSpace(CardMinorEn) ? null : CardMinorEn.Trim(),
            DecimalPlaces = int.Parse(CardDecimalPlaces, CultureInfo.InvariantCulture),
            RoundingMode = CardRoundingMode!,
            Reason = CardReason.Trim(),
        };
        if (await RunAsync(async token => Apply(await client.UpdateCardAsync(CompanyId, BranchId, code, request, token).ConfigureAwait(true))).ConfigureAwait(true))
        {
            CardReason = string.Empty;
            notifications.Success("D_Saved");
        }
    }

    private async Task ChangeBaseAsync()
    {
        var request = new ChangeBaseCurrencyRequest { Code = NewBaseCurrencyCode!.Trim().ToUpperInvariant(), Reason = BaseChangeReason.Trim() };
        if (await RunAsync(async token => Apply(await client.ChangeBaseCurrencyAsync(CompanyId, BranchId, request, token).ConfigureAwait(true))).ConfigureAwait(true))
        {
            BaseChangeReason = string.Empty;
            notifications.Success("D_Saved");
        }
    }

    private async Task RegisterRateAsync()
    {
        var request = new RegisterExchangeRateRequest
        {
            CurrencyCode = RateCurrencyCode!.Trim().ToUpperInvariant(),
            Rate = RateValue.Trim(),
            EffectiveDate = DateOnly.FromDateTime(RateEffectiveDate!.Value),
            Source = RateSource.Trim(),
            Reason = RateReason.Trim(),
        };
        if (await RunAsync(async token =>
            {
                await client.RegisterRateAsync(CompanyId, BranchId, request, token).ConfigureAwait(true);
                Apply(await client.GetSettingsAsync(CompanyId, BranchId, token).ConfigureAwait(true));
                await LoadRatesAsync(token).ConfigureAwait(true);
            }).ConfigureAwait(true))
        {
            RateValue = string.Empty; RateReason = string.Empty;
            notifications.Success("D_Saved");
        }
    }

    private Task<bool> LookupAsync() => RunAsync(async token =>
    {
        var result = await client.GetEffectiveRateAsync(CompanyId, BranchId, LookupCurrencyCode!.Trim().ToUpperInvariant(), DateOnly.FromDateTime(LookupDate!.Value), token)
            .ConfigureAwait(true);
        LookupResult = result.Found == true
            ? string.Format(CultureInfo.CurrentCulture, Localization["CUR_LookupFound"], result.Rate, result.BaseCurrencyCode, result.EffectiveDate, result.Source)
            : Localization["CUR_LookupMissing"];
    });

    public void Localize() { OnPropertyChanged(nameof(BaseLockText)); OnPropertyChanged(nameof(RatesPageText)); }

    partial void OnNewCurrencyCodeChanged(string value) => NotifyCommands();
    partial void OnEnableReasonChanged(string value) => NotifyCommands();
    partial void OnNewBaseCurrencyCodeChanged(string? value) => NotifyCommands();
    partial void OnBaseChangeReasonChanged(string value) => NotifyCommands();
    partial void OnRateCurrencyCodeChanged(string? value) => NotifyCommands();
    partial void OnRateValueChanged(string value) => NotifyCommands();
    partial void OnRateEffectiveDateChanged(DateTime? value) => NotifyCommands();
    partial void OnRateSourceChanged(string value) => NotifyCommands();
    partial void OnRateReasonChanged(string value) => NotifyCommands();
    partial void OnLookupCurrencyCodeChanged(string? value) => NotifyCommands();
    partial void OnLookupDateChanged(DateTime? value) => NotifyCommands();
    partial void OnRateFilterCurrencyChanged(string? value) => ReloadRates();
    partial void OnRateFilterFromChanged(DateTime? value) => ReloadRates();
    partial void OnRateFilterToChanged(DateTime? value) => ReloadRates();

    /// <summary>Clearing three filters at once must restock the register once, not three times behind a busy overlay.</summary>
    private void ReloadRates()
    {
        if (suppressingRateReload) return;
        ratePage = 1;
        if (HasContext) _ = RunAsync(LoadRatesAsync);
    }

    protected override void NotifyCommands()
    {
        OnPropertyChanged(nameof(CanManage));
        OnPropertyChanged(nameof(BaseCurrencyEditable));
        RefreshCommand?.NotifyCanExecuteChanged();
        EnableCurrencyCommand?.NotifyCanExecuteChanged();
        ChangeBaseCommand?.NotifyCanExecuteChanged();
        SaveCardCommand?.NotifyCanExecuteChanged();
        RegisterRateCommand?.NotifyCanExecuteChanged();
        LookupCommand?.NotifyCanExecuteChanged();
        PreviousRatesCommand?.NotifyCanExecuteChanged(); NextRatesCommand?.NotifyCanExecuteChanged(); ClearRateFilterCommand?.NotifyCanExecuteChanged();
    }
}
