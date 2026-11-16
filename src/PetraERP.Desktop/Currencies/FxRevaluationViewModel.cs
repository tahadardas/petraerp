using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;
using PetraERP.Desktop.Accounting;
using PetraERP.Desktop.Connection;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Currencies;

public sealed record AccountLookupItem(Guid Id, string Code, string NameArabic, string? NameEnglish)
{
    public string DisplayText => $"{Code} - {NameArabic}";
}

public sealed partial class FxRevaluationViewModel : PageViewModel
{
    public const string ViewPermission = "Ledger.Accounts.View";
    public const string ManagePermission = "Ledger.Accounts.Manage";

    private readonly IFxRevaluationClient client;
    private readonly ICurrenciesClient currenciesClient;
    private readonly IChartOfAccountsClient accountsClient;
    private readonly IUserConfirmation confirmation;
    private readonly ShellNavigator navigator;
    private int generation;

    [ObservableProperty] private DateTime asOfDate = DateTime.Today;
    [ObservableProperty] private string? selectedCurrencyCode;
    [ObservableProperty] private string? overrideClosingRate;
    [ObservableProperty] private Guid? selectedGainAccountId;
    [ObservableProperty] private Guid? selectedLossAccountId;
    [ObservableProperty] private string notes = "إعادة تقييم أرصدة العملات وفروقات الصرف الدورية";
    [ObservableProperty] private bool isExecuted;
    [ObservableProperty] private string? executedJournalNumber;

    // Live KPI Counters
    [ObservableProperty] private int totalCandidateAccounts;
    [ObservableProperty] private int gainAccountsCount;
    [ObservableProperty] private int lossAccountsCount;
    [ObservableProperty] private decimal totalUnrealizedGains;
    [ObservableProperty] private decimal totalUnrealizedLosses;
    [ObservableProperty] private decimal netDifference;
    [ObservableProperty] private string baseCurrencyCode = "SYP";

    // Rate audit (read-only): posted foreign lines whose own rate differs from the bulletin rate on their date.
    [ObservableProperty] private DateTime auditFrom = new(DateTime.Today.Year, 1, 1);
    [ObservableProperty] private DateTime auditTo = DateTime.Today;
    [ObservableProperty] private bool auditRan;
    [ObservableProperty] private int auditLinesChecked;
    [ObservableProperty] private int auditLinesWithoutRate;
    [ObservableProperty] private decimal auditTotalDifference;

    public ObservableCollection<FxRateAuditItemDto> AuditItems { get; } = [];
    public AsyncRelayCommand AuditCommand { get; }

    public ObservableCollection<CompanyCurrencyDto> EnabledCurrencies { get; } = [];
    public ObservableCollection<AccountLookupItem> Accounts { get; } = [];
    public ObservableCollection<FxAccountRevaluationItemDto> RevaluationItems { get; } = [];

    public AsyncRelayCommand PreviewCommand { get; }
    public AsyncRelayCommand ExecuteRevaluationCommand { get; }
    public RelayCommand ResetCommand { get; }

    public FxRevaluationViewModel(
        IFxRevaluationClient client,
        ICurrenciesClient currenciesClient,
        IChartOfAccountsClient accountsClient,
        IUserConfirmation confirmation,
        ShellNavigator navigator,
        DesktopOperation operation,
        ILocalizationService localization) : base(operation, localization)
    {
        this.client = client;
        this.currenciesClient = currenciesClient;
        this.accountsClient = accountsClient;
        this.confirmation = confirmation;
        this.navigator = navigator;

        PreviewCommand = new AsyncRelayCommand(PreviewAsync, () => HasContext && Idle);
        ExecuteRevaluationCommand = new AsyncRelayCommand(ExecuteAsync, () => HasContext && Idle && CanManage && RevaluationItems.Count > 0 && SelectedGainAccountId.HasValue && SelectedLossAccountId.HasValue && !IsExecuted);
        ResetCommand = new RelayCommand(Reset);
        AuditCommand = new AsyncRelayCommand(AuditAsync, () => HasContext && Idle);
    }

    private async Task AuditAsync()
    {
        if (!HasContext) return;
        try
        {
            var response = await client.AuditFxRatesAsync(CompanyId, BranchId, new FxRateAuditRequest
            {
                From = DateOnly.FromDateTime(AuditFrom),
                To = DateOnly.FromDateTime(AuditTo),
                CurrencyCode = string.IsNullOrWhiteSpace(SelectedCurrencyCode) ? null : SelectedCurrencyCode.Trim(),
            }, CancellationToken.None).ConfigureAwait(true);

            AuditItems.Clear();
            foreach (var item in response.Items ?? []) AuditItems.Add(item);
            AuditLinesChecked = response.LinesChecked;
            AuditLinesWithoutRate = response.LinesWithoutBulletinRate;
            _ = decimal.TryParse(response.TotalDifference, NumberStyles.Number, CultureInfo.InvariantCulture, out var total);
            AuditTotalDifference = total;
            AuditRan = true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Operation.SetError(DesktopErrors.Key(exception));
        }
    }

    protected override string RequiredPermission => ViewPermission;
    public bool CanManage => Can(ManagePermission);

    public void Localize()
    {
        OnPropertyChanged(nameof(Notes));
    }

    partial void OnSelectedGainAccountIdChanged(Guid? value) => ExecuteRevaluationCommand.NotifyCanExecuteChanged();
    partial void OnSelectedLossAccountIdChanged(Guid? value) => ExecuteRevaluationCommand.NotifyCanExecuteChanged();

    protected override async Task LoadAsync(object? parameter, CancellationToken cancellationToken)
    {
        var requested = ++generation;
        await LoadLookupsAsync(cancellationToken).ConfigureAwait(true);
        if (requested != generation) return;
        await PreviewAsync().ConfigureAwait(true);
        NotifyCommands();
    }

    private async Task LoadLookupsAsync(CancellationToken cancellationToken)
    {
        if (!HasContext) return;
        try
        {
            var settings = await currenciesClient.GetSettingsAsync(CompanyId, BranchId, cancellationToken).ConfigureAwait(true);
            BaseCurrencyCode = settings.BaseCurrencyCode ?? "SYP";

            EnabledCurrencies.Clear();
            foreach (var c in settings.Currencies ?? [])
            {
                if (c.IsBase != true && c.IsActive == true)
                {
                    EnabledCurrencies.Add(c);
                }
            }

            Accounts.Clear();
            if (Can(ViewPermission))
            {
                var aTree = await accountsClient.GetAccountTreeAsync(CompanyId, BranchId, cancellationToken).ConfigureAwait(true);
                foreach (var a in aTree.Where(a => a.PostingAllowed == true && a.IsActive == true && a.Id != Guid.Empty).OrderBy(a => a.Code, StringComparer.Ordinal))
                {
                    Accounts.Add(new(a.Id, a.Code ?? string.Empty, a.NameArabic ?? string.Empty, a.NameEnglish));
                }

                // Auto-suggest default gain / loss accounts if found by name or code
                var gainAcc = Accounts.FirstOrDefault(a => a.NameArabic.Contains("فروقات") && a.NameArabic.Contains("أرباح"))
                              ?? Accounts.FirstOrDefault(a => a.NameArabic.Contains("أرباح") && a.NameArabic.Contains("صرف"));
                if (gainAcc is not null) SelectedGainAccountId = gainAcc.Id;

                var lossAcc = Accounts.FirstOrDefault(a => a.NameArabic.Contains("فروقات") && a.NameArabic.Contains("خسائر"))
                              ?? Accounts.FirstOrDefault(a => a.NameArabic.Contains("خسائر") && a.NameArabic.Contains("صرف"));
                if (lossAcc is not null) SelectedLossAccountId = lossAcc.Id;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Operation.SetError(DesktopErrors.Key(exception));
        }
    }

    private async Task PreviewAsync()
    {
        if (!HasContext) return;

        try
        {
            var req = new FxRevaluationPreviewRequest
            {
                AsOfDate = DateOnly.FromDateTime(AsOfDate),
                CurrencyCode = string.IsNullOrWhiteSpace(SelectedCurrencyCode) ? null : SelectedCurrencyCode.Trim(),
                OverrideClosingRate = string.IsNullOrWhiteSpace(OverrideClosingRate) ? null : OverrideClosingRate.Trim()
            };

            var preview = await client.PreviewFxRevaluationAsync(CompanyId, BranchId, req, CancellationToken.None).ConfigureAwait(true);

            RevaluationItems.Clear();
            foreach (var item in preview.Items ?? [])
            {
                RevaluationItems.Add(item);
            }

            BaseCurrencyCode = preview.BaseCurrencyCode ?? BaseCurrencyCode;
            TotalCandidateAccounts = RevaluationItems.Count;
            GainAccountsCount = RevaluationItems.Count(i => i.IsGain);
            LossAccountsCount = RevaluationItems.Count(i => !i.IsGain);

            _ = decimal.TryParse(preview.TotalUnrealizedGains, NumberStyles.Number, CultureInfo.InvariantCulture, out var gains);
            _ = decimal.TryParse(preview.TotalUnrealizedLosses, NumberStyles.Number, CultureInfo.InvariantCulture, out var losses);
            _ = decimal.TryParse(preview.NetDifference, NumberStyles.Number, CultureInfo.InvariantCulture, out var net);

            TotalUnrealizedGains = gains;
            TotalUnrealizedLosses = losses;
            NetDifference = net;

            IsExecuted = false;
            ExecutedJournalNumber = null;
            NotifyCommands();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Operation.SetError(DesktopErrors.Key(exception));
        }
    }

    private async Task ExecuteAsync()
    {
        if (!HasContext || RevaluationItems.Count == 0 || !SelectedGainAccountId.HasValue || !SelectedLossAccountId.HasValue || IsExecuted) return;

        var confirmed = await confirmation.ConfirmAsync("CUR_ConfirmFxRevaluation").ConfigureAwait(true);
        if (!confirmed) return;

        try
        {
            var req = new ExecuteFxRevaluationRequest
            {
                AsOfDate = DateOnly.FromDateTime(AsOfDate),
                GainAccountId = SelectedGainAccountId.Value,
                LossAccountId = SelectedLossAccountId.Value,
                CurrencyCode = string.IsNullOrWhiteSpace(SelectedCurrencyCode) ? null : SelectedCurrencyCode.Trim(),
                OverrideClosingRate = string.IsNullOrWhiteSpace(OverrideClosingRate) ? null : OverrideClosingRate.Trim(),
                Description = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim()
            };

            var response = await client.ExecuteFxRevaluationAsync(CompanyId, BranchId, req, CancellationToken.None).ConfigureAwait(true);

            IsExecuted = true;
            ExecutedJournalNumber = response.JournalEntryNumber;
            NotifyCommands();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Operation.SetError(DesktopErrors.Key(exception));
        }
    }

    private void Reset()
    {
        IsExecuted = false;
        ExecutedJournalNumber = null;
        _ = PreviewAsync();
    }

    protected override void ClearPage()
    {
        RevaluationItems.Clear();
        AuditItems.Clear();
        AuditRan = false;
        EnabledCurrencies.Clear();
        Accounts.Clear();
        SelectedGainAccountId = null;
        SelectedLossAccountId = null;
        IsExecuted = false;
        ExecutedJournalNumber = null;
    }

    protected override void NotifyCommands()
    {
        base.NotifyCommands();
        PreviewCommand.NotifyCanExecuteChanged();
        ExecuteRevaluationCommand.NotifyCanExecuteChanged();
        AuditCommand.NotifyCanExecuteChanged();
    }
}
