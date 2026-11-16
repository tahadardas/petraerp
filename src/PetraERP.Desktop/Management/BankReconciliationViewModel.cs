using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;
using PetraERP.Desktop.Accounting;
using PetraERP.Desktop.Controls;
using PetraERP.Desktop.GeneralLedger;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Management;

/// <summary>One posted book line offered for matching against the bank statement.</summary>
public sealed class BankReconciliationRowItem(BankReconciliationLineResponse line) : ObservableObject
{
    private bool cleared = line.Cleared;
    public BankReconciliationLineResponse Line { get; } = line;
    public bool Cleared { get => cleared; internal set => SetProperty(ref cleared, value); }
}

/// <summary>
/// Bank reconciliation of one book account. The server owns every number (cleared total, difference, eligibility); this page
/// only sends the user's choices and renders what comes back. Completion is offered only when the server-reported difference is zero.
/// </summary>
public sealed partial class BankReconciliationViewModel : PageViewModel
{
    public const string ViewPermission = "Treasury.Vouchers.View";
    public const string ManagePermission = "Treasury.BankReconciliations.Manage";

    private readonly IBankReconciliationClient client;
    private readonly NotificationCenter notifications;

    [ObservableProperty] private Guid? accountId;
    [ObservableProperty] private DateTime? statementDate;
    [ObservableProperty] private string statementBalanceText = string.Empty;
    [ObservableProperty] private BankReconciliationResponse? current;
    [ObservableProperty] private BankReconciliationRowItem? selectedRow;

    public BankReconciliationViewModel(IBankReconciliationClient client, IGeneralLedgerClient ledger, NotificationCenter notifications,
        DesktopOperation operation, ILocalizationService localization) : base(operation, localization)
    {
        this.client = client;
        this.notifications = notifications;
        Lookups = new LedgerLookups(ledger);
        OpenCommand = new AsyncRelayCommand(() => RunAsync(OpenAsync, OperationKind.Save), () => HasContext && Idle && CanManage && AccountId is not null && !IsOpen);
        ToggleCommand = new AsyncRelayCommand(() => RunAsync(token => SetAsync(SelectedRow is { } row ? [row] : [], SelectedRow?.Cleared != true, token), OperationKind.Save),
            () => CanEdit && SelectedRow is not null);
        ClearAllCommand = new AsyncRelayCommand(() => RunAsync(token => SetAsync(Rows.Where(r => !r.Cleared).ToArray(), true, token), OperationKind.Save), () => CanEdit && Rows.Any(r => !r.Cleared));
        UnclearAllCommand = new AsyncRelayCommand(() => RunAsync(token => SetAsync(Rows.Where(r => r.Cleared).ToArray(), false, token), OperationKind.Save), () => CanEdit && Rows.Any(r => r.Cleared));
        CompleteCommand = new AsyncRelayCommand(() => RunAsync(CompleteAsync, OperationKind.Save), () => CanEdit && DifferenceIsZero);
        DiscardCommand = new AsyncRelayCommand(() => RunAsync(DiscardAsync, OperationKind.Save), () => CanEdit);
        CloseCommand = new RelayCommand(Reset, () => Current is not null);
    }

    public LedgerLookups Lookups { get; }
    public ObservableCollection<BankReconciliationRowItem> Rows { get; } = [];
    public AsyncRelayCommand OpenCommand { get; }
    public AsyncRelayCommand ToggleCommand { get; }
    public AsyncRelayCommand ClearAllCommand { get; }
    public AsyncRelayCommand UnclearAllCommand { get; }
    public AsyncRelayCommand CompleteCommand { get; }
    public AsyncRelayCommand DiscardCommand { get; }
    public RelayCommand CloseCommand { get; }

    protected override string RequiredPermission => ViewPermission;
    public bool CanManage => Can(ManagePermission);
    public bool IsOpen => Current is not null;
    public bool IsDraft => Current is { Status: "Draft" };
    private bool CanEdit => HasContext && Idle && CanManage && IsDraft;
    private bool DifferenceIsZero => Current is not null && PetraNumberFormat.TryParseWire(Current.Difference, out var difference) && difference == 0m;
    public string AccountTitle => Current is null ? string.Empty : Current.AccountCode;
    public string Statement => PetraNumberFormat.Format(Current?.StatementBalance, NumberKind.Money, true);
    public string PriorCleared => PetraNumberFormat.Format(Current?.PriorClearedBalance, NumberKind.Money, true);
    public string ClearedNow => PetraNumberFormat.Format(Current?.ClearedInThisReconciliation, NumberKind.Money, true);
    public string Difference => PetraNumberFormat.Format(Current?.Difference, NumberKind.Money, true);
    public string BookBalance => PetraNumberFormat.Format(Current?.BookBalanceAtStatementDate, NumberKind.Money, true);
    public bool DifferenceIsNonZero => IsOpen && !DifferenceIsZero;

    protected override async Task LoadAsync(object? parameter, CancellationToken cancellationToken)
    {
        if (Lookups.Accounts.Count == 0) await Lookups.LoadAsync(Context!, cancellationToken).ConfigureAwait(true);
    }

    protected override void ClearPage()
    {
        Lookups.Clear();
        Reset();
        AccountId = null;
        StatementDate = null;
        StatementBalanceText = string.Empty;
    }

    private void Reset()
    {
        Rows.Clear();
        SelectedRow = null;
        Current = null;
    }

    /// <summary>Resumes the account's open draft when there is one; otherwise starts a new reconciliation from the entered statement.</summary>
    private async Task OpenAsync(CancellationToken cancellationToken)
    {
        if (AccountId is not { } account) return;
        var open = (await client.ListAsync(CompanyId, BranchId, account, cancellationToken).ConfigureAwait(true)).FirstOrDefault(r => r.Status == "Draft");
        if (open is not null)
        {
            Apply(await client.GetAsync(CompanyId, BranchId, open.Id, cancellationToken).ConfigureAwait(true));
            return;
        }

        var balance = 0m;
        if (StatementDate is null || !PetraNumberFormat.TryParseInput(StatementBalanceText, out balance))
        {
            notifications.Warning("BKR_StatementRequired");
            return;
        }

        var wire = balance.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture);
        Apply(await client.StartAsync(CompanyId, BranchId, account, DateOnly.FromDateTime(StatementDate.Value), wire, cancellationToken).ConfigureAwait(true));
    }

    private async Task SetAsync(BankReconciliationRowItem[] targets, bool cleared, CancellationToken cancellationToken)
    {
        if (targets.Length == 0 || Current is not { } current) return;
        var ids = targets.Select(r => r.Line.JournalLineId).ToArray();
        Apply(await client.SetClearedAsync(CompanyId, BranchId, current.Id, ids, cleared, current.Version, cancellationToken).ConfigureAwait(true));
    }

    private async Task CompleteAsync(CancellationToken cancellationToken)
    {
        if (Current is not { } current) return;
        Apply(await client.CompleteAsync(CompanyId, BranchId, current.Id, current.Version, cancellationToken).ConfigureAwait(true));
        notifications.Success("BKR_Completed");
    }

    private async Task DiscardAsync(CancellationToken cancellationToken)
    {
        if (Current is not { } current) return;
        await client.DiscardAsync(CompanyId, BranchId, current.Id, current.Version, cancellationToken).ConfigureAwait(true);
        Reset();
        notifications.Success("BKR_Discarded");
    }

    private void Apply(BankReconciliationResponse response)
    {
        var selected = SelectedRow?.Line.JournalLineId;
        Rows.Clear();
        foreach (var line in response.Lines) Rows.Add(new BankReconciliationRowItem(line));
        SelectedRow = selected is { } id ? Rows.FirstOrDefault(r => r.Line.JournalLineId == id) : null;
        Current = response;
    }

    partial void OnCurrentChanged(BankReconciliationResponse? value)
    {
        foreach (var name in new[] { nameof(IsOpen), nameof(IsDraft), nameof(AccountTitle), nameof(Statement), nameof(PriorCleared), nameof(ClearedNow),
                     nameof(Difference), nameof(BookBalance), nameof(DifferenceIsNonZero) })
            OnPropertyChanged(name);
        NotifyCommands();
    }

    partial void OnAccountIdChanged(Guid? value) => NotifyCommands();

    partial void OnSelectedRowChanged(BankReconciliationRowItem? value) => NotifyCommands();

    public void Localize() => OnCurrentChanged(Current);

    protected override void NotifyCommands()
    {
        OpenCommand?.NotifyCanExecuteChanged(); ToggleCommand?.NotifyCanExecuteChanged(); ClearAllCommand?.NotifyCanExecuteChanged();
        UnclearAllCommand?.NotifyCanExecuteChanged(); CompleteCommand?.NotifyCanExecuteChanged(); DiscardCommand?.NotifyCanExecuteChanged();
        CloseCommand?.NotifyCanExecuteChanged();
    }
}
