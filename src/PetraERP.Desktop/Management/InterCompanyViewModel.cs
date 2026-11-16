using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Management;

/// <summary>
/// Inter-company links and the side-by-side reconciliation: this company's account against the counterparty's mirror account (by account code),
/// with each side's latest movements and the unmatched difference. Legal entities and their yearly databases stay separate.
/// </summary>
public sealed partial class InterCompanyViewModel : PageViewModel
{
    public const string ViewPermission = "Ledger.Reports.View";
    public const string ManagePermission = "Ledger.Accounts.Manage";

    private readonly IManagementClient client;
    private readonly NotificationCenter notifications;

    [ObservableProperty] private InterCompanyLinkDto? selectedLink;
    [ObservableProperty] private DateTime asOf = DateTime.Today;
    [ObservableProperty] private string counterpartyCompanyIdText = string.Empty;
    [ObservableProperty] private string accountCode = string.Empty;
    [ObservableProperty] private string counterpartyAccountCode = string.Empty;
    [ObservableProperty] private string currencyCode = string.Empty;
    [ObservableProperty] private string? note;
    [ObservableProperty] private InterCompanyReconciliationResponse? result;

    public InterCompanyViewModel(IManagementClient client, NotificationCenter notifications, DesktopOperation operation, ILocalizationService localization)
        : base(operation, localization)
    {
        this.client = client;
        this.notifications = notifications;
        RefreshCommand = new AsyncRelayCommand(() => RunAsync(LoadAllAsync), () => HasContext && Idle);
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => HasContext && Idle && CanManage && Guid.TryParse(CounterpartyCompanyIdText, out _)
            && !string.IsNullOrWhiteSpace(AccountCode) && !string.IsNullOrWhiteSpace(CounterpartyAccountCode) && !string.IsNullOrWhiteSpace(CurrencyCode));
        ToggleActiveCommand = new AsyncRelayCommand(ToggleActiveAsync, () => HasContext && Idle && CanManage && SelectedLink is not null);
        ReconcileCommand = new AsyncRelayCommand(ReconcileAsync, () => HasContext && Idle && SelectedLink is { IsActive: true });
    }

    public ObservableCollection<InterCompanyLinkDto> Links { get; } = [];
    public ObservableCollection<InterCompanyMovementDto> ThisMovements { get; } = [];
    public ObservableCollection<InterCompanyMovementDto> CounterpartyMovements { get; } = [];
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand SaveCommand { get; }
    public AsyncRelayCommand ToggleActiveCommand { get; }
    public AsyncRelayCommand ReconcileCommand { get; }
    public bool CanManage => Can(ManagePermission);

    protected override string RequiredPermission => ViewPermission;
    protected override Task LoadAsync(object? parameter, CancellationToken cancellationToken) => RunAsync(LoadAllAsync);

    protected override void ClearPage()
    {
        Links.Clear(); ThisMovements.Clear(); CounterpartyMovements.Clear(); SelectedLink = null; Result = null;
        CounterpartyCompanyIdText = string.Empty; AccountCode = string.Empty; CounterpartyAccountCode = string.Empty; CurrencyCode = string.Empty; Note = null;
    }

    protected override void NotifyCommands()
    {
        RefreshCommand?.NotifyCanExecuteChanged(); SaveCommand?.NotifyCanExecuteChanged(); ToggleActiveCommand?.NotifyCanExecuteChanged();
        ReconcileCommand?.NotifyCanExecuteChanged(); OnPropertyChanged(nameof(CanManage));
    }

    partial void OnCounterpartyCompanyIdTextChanged(string value) => SaveCommand.NotifyCanExecuteChanged();
    partial void OnAccountCodeChanged(string value) => SaveCommand.NotifyCanExecuteChanged();
    partial void OnCounterpartyAccountCodeChanged(string value) => SaveCommand.NotifyCanExecuteChanged();
    partial void OnCurrencyCodeChanged(string value) => SaveCommand.NotifyCanExecuteChanged();
    partial void OnSelectedLinkChanged(InterCompanyLinkDto? value)
    {
        Result = null; ThisMovements.Clear(); CounterpartyMovements.Clear(); NotifyCommands();
    }

    public bool HasResult => Result is not null;
    public string MatchingLabel => Result?.Matching is not { } m ? string.Empty
        : $"مقترح مطابقة بالمبلغ (غير مُرحَّل): {m.Pairs?.Count ?? 0} زوج مطابق، غير مطابق لدينا {m.UnmatchedThis?.Count ?? 0}، غير مطابق لدى المقابل {m.UnmatchedCounterparty?.Count ?? 0}"
          + (m.Truncated == true ? " — تم الاقتصار على أحدث 2000 سطر" : string.Empty);
    public string DifferenceLabel => Result is null ? string.Empty : Result.Matched == true ? "مطابق" : "فرق غير مطابق: " + Result.Difference;
    partial void OnResultChanged(InterCompanyReconciliationResponse? value) { OnPropertyChanged(nameof(HasResult)); OnPropertyChanged(nameof(DifferenceLabel)); OnPropertyChanged(nameof(MatchingLabel)); }

    private async Task LoadAllAsync(CancellationToken token)
    {
        var keep = SelectedLink?.Id;
        var list = await client.ListInterCompanyLinksAsync(CompanyId, BranchId, token).ConfigureAwait(true);
        Links.Clear(); foreach (var l in list) Links.Add(l);
        SelectedLink = keep is null ? null : Links.FirstOrDefault(l => l.Id == keep);
    }

    private async Task SaveAsync()
    {
        var ok = await RunAsync(async token =>
        {
            await client.SaveInterCompanyLinkAsync(CompanyId, BranchId, new SaveInterCompanyLinkRequest
            {
                CounterpartyCompanyId = Guid.Parse(CounterpartyCompanyIdText), AccountCode = AccountCode.Trim(), CounterpartyAccountCode = CounterpartyAccountCode.Trim(),
                CurrencyCode = CurrencyCode.Trim().ToUpperInvariant(), Note = string.IsNullOrWhiteSpace(Note) ? null : Note.Trim(),
            }, token).ConfigureAwait(true);
            await LoadAllAsync(token).ConfigureAwait(true);
        }, OperationKind.Save).ConfigureAwait(true);
        if (ok) notifications.Success("D_Saved");
    }

    private async Task ToggleActiveAsync()
    {
        if (SelectedLink?.Id is not { } id) return;
        var active = !(SelectedLink.IsActive ?? false);
        var ok = await RunAsync(async token =>
        {
            await client.SetInterCompanyLinkActiveAsync(CompanyId, BranchId, id, active, token).ConfigureAwait(true);
            await LoadAllAsync(token).ConfigureAwait(true);
        }, OperationKind.Save).ConfigureAwait(true);
        if (ok) notifications.Success("D_Saved");
    }

    private async Task ReconcileAsync()
    {
        if (SelectedLink?.Id is not { } id) return;
        await RunAsync(async token =>
        {
            var r = await client.ReconcileInterCompanyAsync(CompanyId, BranchId, id, DateOnly.FromDateTime(AsOf), token).ConfigureAwait(true);
            Result = r;
            // With matching, the grids list only the lines still unpaired: those are what needs a correcting entry.
            var thisRows = r.Matching is { } mt ? mt.UnmatchedThis : r.ThisSide?.RecentMovements;
            var otherRows = r.Matching is { } mo ? mo.UnmatchedCounterparty : r.CounterpartySide?.RecentMovements;
            ThisMovements.Clear(); foreach (var m in thisRows ?? []) ThisMovements.Add(m);
            CounterpartyMovements.Clear(); foreach (var m in otherRows ?? []) CounterpartyMovements.Add(m);
        }).ConfigureAwait(true);
    }
}
