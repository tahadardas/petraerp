using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;
using PetraERP.Desktop.Connection;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Migration;

/// <summary>One reconciliation check as shown to the operator (source value, target value and the exact difference).</summary>
public sealed record MigrationCheckRow(string Name, string Source, string Target, string Difference, bool Passed, string Result, string Details);

/// <summary>
/// Import from Al-Ameen. Four deliberate steps: check the source and preview (writes nothing), import into the company
/// (administrator password), reconcile source against target, and only after every check passes activate. Each step is
/// enabled only when the previous one succeeded, and changing any input discards the preview so a stale run can never be
/// executed against different settings.
/// </summary>
public sealed partial class AlAmeenMigrationViewModel : PageViewModel
{
    private readonly IAlAmeenMigrationClient client;

    [ObservableProperty] private string sourceConnectionString = string.Empty;
    [ObservableProperty] private string targetFiscalYearCode = string.Empty;
    [ObservableProperty] private DateTime targetFiscalYearStart = new(DateTime.Today.Year, 1, 1);
    [ObservableProperty] private DateTime targetFiscalYearEnd = new(DateTime.Today.Year, 12, 31);
    [ObservableProperty] private string targetBaseCurrencyCode = string.Empty;
    [ObservableProperty] private bool isFullHistory;
    [ObservableProperty] private bool useCutoffDate;
    [ObservableProperty] private DateTime cutoffDate = DateTime.Today;
    /// <summary>Administrator password that confirms the write. Kept only in memory and cleared after every use.</summary>
    [ObservableProperty] private string administratorPassword = string.Empty;

    [ObservableProperty] private Guid migrationRunId;
    [ObservableProperty] private int accountsCount;
    [ObservableProperty] private int partiesCount;
    [ObservableProperty] private int itemsCount;
    [ObservableProperty] private int warehousesCount;
    [ObservableProperty] private int currenciesCount;
    [ObservableProperty] private string openingBalanceDebit = "0.0000";
    [ObservableProperty] private string openingBalanceCredit = "0.0000";
    [ObservableProperty] private int customerOpenItemsCount;
    [ObservableProperty] private string customerOpenItemsTotal = "0.0000";
    [ObservableProperty] private int supplierOpenItemsCount;
    [ObservableProperty] private string supplierOpenItemsTotal = "0.0000";
    [ObservableProperty] private int inventoryItemsCount;
    [ObservableProperty] private string inventoryValuation = "0.0000";

    [ObservableProperty] private bool isPreviewCompleted;
    [ObservableProperty] private bool isExecutionCompleted;
    [ObservableProperty] private bool isReconciled;
    [ObservableProperty] private bool reconciliationPassed;
    [ObservableProperty] private bool isActivated;
    [ObservableProperty] private string targetDatabaseName = string.Empty;
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private bool lastActionFailed;

    public AlAmeenMigrationViewModel(IAlAmeenMigrationClient client, DesktopOperation operation, ILocalizationService localization)
        : base(operation, localization)
    {
        this.client = client;
        PreviewCommand = new AsyncRelayCommand(PreviewAsync, () => HasContext && Idle && !IsExecutionCompleted
            && !string.IsNullOrWhiteSpace(SourceConnectionString) && !string.IsNullOrWhiteSpace(TargetFiscalYearCode));
        ExecuteCommand = new AsyncRelayCommand(ExecuteAsync, () => HasContext && Idle && IsPreviewCompleted && !IsExecutionCompleted
            && !string.IsNullOrEmpty(AdministratorPassword));
        ReconcileCommand = new AsyncRelayCommand(ReconcileAsync, () => HasContext && Idle && IsExecutionCompleted && !IsActivated);
        ActivateCommand = new AsyncRelayCommand(ActivateAsync, () => HasContext && Idle && IsExecutionCompleted && ReconciliationPassed
            && !IsActivated && !string.IsNullOrEmpty(AdministratorPassword));
    }

    public void Localize() => NotifyCommands();

    protected override string RequiredPermission => "System.Database.Manage";

    public ObservableCollection<string> ValidationMessages { get; } = [];
    public ObservableCollection<MigrationCheckRow> Checks { get; } = [];
    public IAsyncRelayCommand PreviewCommand { get; }
    public IAsyncRelayCommand ExecuteCommand { get; }
    public IAsyncRelayCommand ReconcileCommand { get; }
    public IAsyncRelayCommand ActivateCommand { get; }
    public bool HasStatus => !string.IsNullOrEmpty(StatusMessage);
    public bool HasFindings => ValidationMessages.Count > 0;
    public bool HasChecks => Checks.Count > 0;

    partial void OnSourceConnectionStringChanged(string value) => InputChanged();
    partial void OnTargetFiscalYearCodeChanged(string value) => InputChanged();
    partial void OnTargetFiscalYearStartChanged(DateTime value) => InputChanged();
    partial void OnTargetFiscalYearEndChanged(DateTime value) => InputChanged();
    partial void OnTargetBaseCurrencyCodeChanged(string value) => InputChanged();
    partial void OnIsFullHistoryChanged(bool value) => InputChanged();
    partial void OnUseCutoffDateChanged(bool value) => InputChanged();
    partial void OnCutoffDateChanged(DateTime value) => InputChanged();
    partial void OnAdministratorPasswordChanged(string value) => NotifyCommands();
    partial void OnStatusMessageChanged(string value) => OnPropertyChanged(nameof(HasStatus));

    protected override Task LoadAsync(object? parameter, CancellationToken cancellationToken)
    {
        if (Context is { } context)
        {
            // Defaults come from the working company so the operator only confirms them; they stay editable.
            if (string.IsNullOrEmpty(TargetBaseCurrencyCode)) TargetBaseCurrencyCode = context.BaseCurrencyCode;
            if (context.FiscalYearStart is { } start && context.FiscalYearEnd is { } end && string.IsNullOrEmpty(TargetFiscalYearCode))
            {
                TargetFiscalYearStart = start.ToDateTime(TimeOnly.MinValue);
                TargetFiscalYearEnd = end.ToDateTime(TimeOnly.MinValue);
                TargetFiscalYearCode = "FY" + start.Year.ToString(CultureInfo.InvariantCulture);
            }
        }

        return Task.CompletedTask;
    }

    protected override void ClearPage()
    {
        AdministratorPassword = string.Empty;
        ResetRun();
        StatusMessage = string.Empty;
    }

    protected override void NotifyCommands()
    {
        PreviewCommand.NotifyCanExecuteChanged();
        ExecuteCommand.NotifyCanExecuteChanged();
        ReconcileCommand.NotifyCanExecuteChanged();
        ActivateCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Any input change discards a preview that has not been written yet; an executed run keeps its evidence.</summary>
    private void InputChanged()
    {
        if (!IsExecutionCompleted) ResetRun();
        NotifyCommands();
    }

    private void ResetRun()
    {
        MigrationRunId = Guid.Empty;
        IsPreviewCompleted = false;
        IsExecutionCompleted = false;
        IsReconciled = false;
        ReconciliationPassed = false;
        IsActivated = false;
        TargetDatabaseName = string.Empty;
        AccountsCount = PartiesCount = ItemsCount = WarehousesCount = CurrenciesCount = 0;
        CustomerOpenItemsCount = SupplierOpenItemsCount = InventoryItemsCount = 0;
        OpeningBalanceDebit = OpeningBalanceCredit = CustomerOpenItemsTotal = SupplierOpenItemsTotal = InventoryValuation = "0.0000";
        ValidationMessages.Clear();
        Checks.Clear();
        OnPropertyChanged(nameof(HasFindings));
        OnPropertyChanged(nameof(HasChecks));
    }

    private Task<bool> PreviewAsync() => RunAsync(async token =>
    {
        ResetRun();
        var request = new MigrationPreviewRequest
        {
            SourceConnectionString = SourceConnectionString.Trim(),
            TargetCompanyId = CompanyId,
            TargetFiscalYearCode = TargetFiscalYearCode.Trim(),
            TargetFiscalYearStart = DateOnly.FromDateTime(TargetFiscalYearStart),
            TargetFiscalYearEnd = DateOnly.FromDateTime(TargetFiscalYearEnd),
            TargetBaseCurrencyCode = TargetBaseCurrencyCode.Trim().ToUpperInvariant(),
            Mode = IsFullHistory ? MigrationMode.FullHistorical : MigrationMode.FastCutover,
        };
        if (UseCutoffDate) request.CutoffDate = DateOnly.FromDateTime(CutoffDate);

        var response = await client.PreviewAlAmeenMigrationAsync(request, token).ConfigureAwait(true);
        MigrationRunId = response.MigrationRunId;
        AccountsCount = response.AccountsCount;
        PartiesCount = response.PartiesCount;
        ItemsCount = response.ItemsCount;
        WarehousesCount = response.WarehousesCount;
        CurrenciesCount = response.CurrenciesCount;
        OpeningBalanceDebit = response.OpeningBalanceDebit.ToString(CultureInfo.InvariantCulture);
        OpeningBalanceCredit = response.OpeningBalanceCredit.ToString(CultureInfo.InvariantCulture);
        CustomerOpenItemsCount = response.CustomerOpenItemsCount;
        CustomerOpenItemsTotal = response.CustomerOpenItemsTotal.ToString(CultureInfo.InvariantCulture);
        SupplierOpenItemsCount = response.SupplierOpenItemsCount;
        SupplierOpenItemsTotal = response.SupplierOpenItemsTotal.ToString(CultureInfo.InvariantCulture);
        InventoryItemsCount = response.InventoryItemsCount;
        InventoryValuation = response.InventoryValuationTotal.ToString(CultureInfo.InvariantCulture);
        foreach (var issue in response.ValidationIssues) ValidationMessages.Add(issue);
        OnPropertyChanged(nameof(HasFindings));

        IsPreviewCompleted = response.State != MigrationState.FailedRecoverable;
        LastActionFailed = !IsPreviewCompleted;
        StatusMessage = Localization[IsPreviewCompleted ? "AMM_PreviewDone" : "AMM_PreviewBlocked"];
        NotifyCommands();
    });

    private Task<bool> ExecuteAsync() => RunAsync(async token =>
    {
        var password = AdministratorPassword;
        AdministratorPassword = string.Empty;
        var response = await client.ExecuteAlAmeenMigrationAsync(
            new MigrationExecuteRequest { MigrationRunId = MigrationRunId, AdministratorPassword = password }, token).ConfigureAwait(true);
        IsExecutionCompleted = response.Success;
        TargetDatabaseName = response.TargetDatabaseName ?? string.Empty;
        LastActionFailed = !response.Success;
        StatusMessage = response.Message;
        NotifyCommands();
        if (response.Success) await ReconcileCoreAsync(token).ConfigureAwait(true);
    }, OperationKind.Post);

    private Task<bool> ReconcileAsync() => RunAsync(ReconcileCoreAsync);

    private async Task ReconcileCoreAsync(CancellationToken token)
    {
        var result = await client.ReconcileAlAmeenMigrationAsync(MigrationRunId, token).ConfigureAwait(true);
        Checks.Clear();
        foreach (var check in result.Checks)
        {
            Checks.Add(new MigrationCheckRow(
                check.CheckName,
                check.SourceValue.ToString(CultureInfo.InvariantCulture),
                check.TargetValue.ToString(CultureInfo.InvariantCulture),
                check.Difference.ToString(CultureInfo.InvariantCulture),
                check.Passed,
                Localization[check.Passed ? "AMM_Pass" : "AMM_Fail"],
                check.Details ?? string.Empty));
        }

        IsReconciled = true;
        ReconciliationPassed = result.Passed;
        LastActionFailed = !result.Passed;
        StatusMessage = Localization[result.Passed ? "AMM_ReconPassed" : "AMM_ReconFailed"];
        OnPropertyChanged(nameof(HasChecks));
        NotifyCommands();
    }

    private Task<bool> ActivateAsync() => RunAsync(async token =>
    {
        var password = AdministratorPassword;
        AdministratorPassword = string.Empty;
        var response = await client.ActivateAlAmeenMigrationAsync(
            new MigrationActivationRequest { MigrationRunId = MigrationRunId, AdministratorPassword = password }, token).ConfigureAwait(true);
        IsActivated = response.Success;
        LastActionFailed = !response.Success;
        StatusMessage = response.Message;
        NotifyCommands();
    }, OperationKind.Post);
}
