using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;
using PetraERP.Desktop.Controls;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Accounting;

public sealed record PeriodRow(AccountingPeriodResponse Period, int Number, string StateLabel, BadgeKind Kind, bool IsCurrent);

/// <summary>
/// Fiscal years and accounting periods. Transitions are offered only where the Domain allows them (Open → SoftClose,
/// Open/SoftClosed → Close, SoftClosed/Closed → Reopen) and the user holds the permission; each needs a reason and an
/// explicit confirmation, and the period audit is shown after the change. The server re-checks everything.
/// </summary>
public sealed partial class FiscalPeriodsViewModel : PageViewModel
{
    protected override string RequiredPermission => "Ledger.Periods.View";
    public const string ManagePermission = "Ledger.Periods.Manage";
    public const string ReopenPermission = "Ledger.Periods.Reopen";

    private readonly IFiscalCalendarClient client;
    private readonly IUserConfirmation confirmation;
    private readonly ShellNavigator navigator;
    private readonly NotificationCenter notifications;
    private readonly TimeProvider time;

    [ObservableProperty] private FiscalYearResponse? selectedYear;
    [ObservableProperty] private PeriodRow? selectedPeriod;
    [ObservableProperty] private string reason = string.Empty;
    [ObservableProperty] private bool isCreatingYear;
    [ObservableProperty] private string newYearCode = string.Empty;
    [ObservableProperty] private DateTime? newYearStart;
    [ObservableProperty] private DateTime? newYearEnd;
    [ObservableProperty] private string newYearError = string.Empty;

    public FiscalPeriodsViewModel(IFiscalCalendarClient client, IUserConfirmation confirmation, ShellNavigator navigator, DesktopOperation operation, ILocalizationService localization,
        NotificationCenter notifications, TimeProvider time) : base(operation, localization)
    {
        this.client = client; this.confirmation = confirmation; this.navigator = navigator; this.notifications = notifications; this.time = time;
        SoftCloseCommand = new AsyncRelayCommand(() => TransitionAsync(PeriodAction.SoftClose), () => CanSoftClose && HasReason && Idle);
        CloseCommand = new AsyncRelayCommand(() => TransitionAsync(PeriodAction.Close), () => CanClose && HasReason && Idle);
        ReopenCommand = new AsyncRelayCommand(() => TransitionAsync(PeriodAction.Reopen), () => CanReopen && HasReason && Idle);
        RefreshCommand = new AsyncRelayCommand(() => RunAsync(token => ReloadAsync(SelectedPeriod?.Period.Id, token)), () => HasContext && Idle);
        StartCreateYearCommand = new RelayCommand(StartCreateYear, () => Can(ManagePermission) && Idle);
        CreateYearCommand = new AsyncRelayCommand(CreateYearAsync, () => Can(ManagePermission) && IsCreatingYear && Idle);
        CancelCreateYearCommand = new RelayCommand(() => IsCreatingYear = false);
        OpenTrialBalanceCommand = new AsyncRelayCommand(OpenTrialBalanceAsync, () => CanViewTrialBalance && SelectedPeriod is not null && Idle);
    }

    public event EventHandler? CalendarChanged;
    public ObservableCollection<FiscalYearResponse> Years { get; } = [];
    public ObservableCollection<PeriodRow> Periods { get; } = [];
    public ObservableCollection<PeriodAuditResponse> Audit { get; } = [];
    /// <summary>The period containing today's date, if any (also shown in the shell's top bar).</summary>
    public PeriodRow? CurrentPeriod => Periods.FirstOrDefault(row => row.IsCurrent);
    public bool CanSoftClose => Can(ManagePermission) && SelectedPeriod?.Period.State == AccountingPeriodState.Open;
    public bool CanClose => Can(ManagePermission) && SelectedPeriod?.Period.State is AccountingPeriodState.Open or AccountingPeriodState.SoftClosed;
    public bool CanReopen => Can(ReopenPermission) && SelectedPeriod?.Period.State is AccountingPeriodState.SoftClosed or AccountingPeriodState.Closed;
    public bool CanViewTrialBalance => Can("Ledger.Reports.View");
    public bool HasActions => CanSoftClose || CanClose || CanReopen;
    public bool CanManage => Can(ManagePermission);
    private bool HasReason => !string.IsNullOrWhiteSpace(Reason);

    public AsyncRelayCommand SoftCloseCommand { get; }
    public AsyncRelayCommand CloseCommand { get; }
    public AsyncRelayCommand ReopenCommand { get; }
    public AsyncRelayCommand RefreshCommand { get; }
    public RelayCommand StartCreateYearCommand { get; }
    public AsyncRelayCommand CreateYearCommand { get; }
    public RelayCommand CancelCreateYearCommand { get; }
    public AsyncRelayCommand OpenTrialBalanceCommand { get; }

    private Task OpenTrialBalanceAsync() =>
        SelectedPeriod is null || !CanViewTrialBalance ? Task.CompletedTask : navigator.NavigateAsync("trial-balance", new OpenTrialBalanceRequest(PeriodId: SelectedPeriod.Period.Id));

    public static BadgeKind KindOf(AccountingPeriodState state) => state switch
    {
        AccountingPeriodState.Open => BadgeKind.Success,
        AccountingPeriodState.SoftClosed => BadgeKind.Warning,
        _ => BadgeKind.Neutral,
    };

    /// <summary>The period whose range contains <paramref name="today"/> across all years, or null.</summary>
    public static AccountingPeriodResponse? PeriodFor(IEnumerable<FiscalYearResponse> years, DateOnly today) =>
        years.SelectMany(year => year.Periods).FirstOrDefault(period => period.RangeStart <= today && period.RangeEnd >= today);

    protected override Task LoadAsync(object? parameter, CancellationToken cancellationToken) => ReloadAsync(SelectedPeriod?.Period.Id, cancellationToken);

    protected override void ClearPage()
    {
        Years.Clear(); Periods.Clear(); Audit.Clear(); SelectedYear = null; SelectedPeriod = null; Reason = string.Empty; IsCreatingYear = false;
        Notify();
    }

    private DateOnly Today => DateOnly.FromDateTime(time.GetLocalNow().DateTime);

    private async Task ReloadAsync(Guid? selectPeriod, CancellationToken cancellationToken)
    {
        var years = await client.ListFiscalYearsAsync(CompanyId, BranchId, cancellationToken).ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested();
        var keepYear = SelectedYear?.Id;
        SelectedYear = null;
        Years.Clear();
        foreach (var year in years) Years.Add(year);
        var current = PeriodFor(years, Today);
        SelectedYear = Years.FirstOrDefault(year => year.Id == keepYear)
            ?? Years.FirstOrDefault(year => year.Periods.Any(period => period.Id == (selectPeriod ?? current?.Id)))
            ?? Years.FirstOrDefault();
        if ((selectPeriod ?? current?.Id) is { } id && Periods.FirstOrDefault(row => row.Period.Id == id) is { } row) SelectedPeriod = row;
        if (SelectedPeriod is not null) await LoadAuditAsync(SelectedPeriod.Period.Id, cancellationToken).ConfigureAwait(true);
    }

    partial void OnSelectedYearChanged(FiscalYearResponse? value)
    {
        SelectedPeriod = null;
        Periods.Clear();
        if (value is not null)
        {
            var today = Today;
            var number = 0;
            foreach (var period in value.Periods.OrderBy(period => period.RangeStart))
            {
                Periods.Add(new PeriodRow(period, ++number, Localization["D_PeriodState_" + period.State], KindOf(period.State),
                    period.RangeStart <= today && period.RangeEnd >= today));
            }
        }

        Notify();
    }

    partial void OnSelectedPeriodChanged(PeriodRow? value)
    {
        Audit.Clear();
        Notify();
        if (value is not null && HasContext) _ = RunAsync(token => LoadAuditAsync(value.Period.Id, token));
    }

    partial void OnReasonChanged(string value) => NotifyCommands();

    private async Task LoadAuditAsync(Guid periodId, CancellationToken cancellationToken)
    {
        var audit = await client.GetPeriodAuditAsync(CompanyId, BranchId, periodId, cancellationToken).ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested();
        if (SelectedPeriod?.Period.Id != periodId) return;
        Audit.Clear();
        foreach (var entry in audit.OrderByDescending(entry => entry.OccurredAtUtc)) Audit.Add(entry);
    }

    private async Task TransitionAsync(PeriodAction action)
    {
        if (SelectedPeriod is not { } row || !HasReason || !Idle || !(action switch
            { PeriodAction.SoftClose => CanSoftClose, PeriodAction.Close => CanClose, _ => CanReopen })) return;
        var companyId = CompanyId; var branchId = BranchId; var tokenAtPrompt = Token;
        if (!await confirmation.ConfirmAsync(action switch
            {
                PeriodAction.SoftClose => "D_ConfirmSoftClosePeriod",
                PeriodAction.Close => "D_ConfirmClosePeriod",
                _ => "D_ConfirmReopenPeriod",
            }).ConfigureAwait(true)) return;
        if (tokenAtPrompt.IsCancellationRequested || CompanyId != companyId || BranchId != branchId) return;
        var request = new PeriodTransitionRequest { ExpectedVersion = row.Period.Version, Reason = Reason.Trim() };
        if (await RunAsync(async token =>
            {
                await client.TransitionAsync(CompanyId, BranchId, row.Period.Id, action, request, token).ConfigureAwait(true);
                Reason = string.Empty;
                await ReloadAsync(row.Period.Id, token).ConfigureAwait(true);
            }).ConfigureAwait(true))
        {
            CalendarChanged?.Invoke(this, EventArgs.Empty);
            notifications.Success("D_PeriodStateChanged");
        }
    }

    private void StartCreateYear()
    {
        var last = Years.OrderByDescending(year => year.RangeEnd).FirstOrDefault();
        var start = last?.RangeEnd.AddDays(1) ?? new DateOnly(Today.Year, 1, 1);
        NewYearStart = start.ToDateTime(TimeOnly.MinValue);
        NewYearEnd = start.AddYears(1).AddDays(-1).ToDateTime(TimeOnly.MinValue);
        NewYearCode = "FY" + start.Year.ToString(System.Globalization.CultureInfo.InvariantCulture);
        NewYearError = string.Empty;
        IsCreatingYear = true;
    }

    private async Task CreateYearAsync()
    {
        if (!CanManage || !Idle) return;
        if (string.IsNullOrWhiteSpace(NewYearCode) || NewYearStart is null || NewYearEnd is null || NewYearEnd < NewYearStart)
        {
            NewYearError = Localization["D_FiscalYearInputInvalid"];
            return;
        }

        var request = new CreateFiscalYearRequest
        {
            Code = NewYearCode.Trim(),
            Start = DateOnly.FromDateTime(NewYearStart.Value),
            End = DateOnly.FromDateTime(NewYearEnd.Value),
        };
        if (await RunAsync(async token =>
            {
                var created = await client.CreateFiscalYearAsync(CompanyId, BranchId, request, token).ConfigureAwait(true);
                IsCreatingYear = false;
                SelectedYear = null;
                await ReloadAsync(created.Periods.Count > 0 ? created.Periods[0].Id : null, token).ConfigureAwait(true);
            }).ConfigureAwait(true))
        {
            CalendarChanged?.Invoke(this, EventArgs.Empty);
            notifications.Success("D_FiscalYearCreated");
        }
    }

    public void Localize()
    {
        var keep = SelectedPeriod?.Period.Id;
        OnSelectedYearChanged(SelectedYear);
        if (keep is { } id) SelectedPeriod = Periods.FirstOrDefault(row => row.Period.Id == id);
    }

    private void Notify()
    {
        OnPropertyChanged(nameof(CurrentPeriod)); OnPropertyChanged(nameof(CanSoftClose)); OnPropertyChanged(nameof(CanClose));
        OnPropertyChanged(nameof(CanReopen)); OnPropertyChanged(nameof(HasActions));
        NotifyCommands();
    }

    protected override void NotifyCommands()
    {
        OnPropertyChanged(nameof(CanManage)); OnPropertyChanged(nameof(CanViewTrialBalance)); OnPropertyChanged(nameof(CanSoftClose)); OnPropertyChanged(nameof(CanClose));
        OnPropertyChanged(nameof(CanReopen)); OnPropertyChanged(nameof(HasActions));
        SoftCloseCommand?.NotifyCanExecuteChanged(); CloseCommand?.NotifyCanExecuteChanged(); ReopenCommand?.NotifyCanExecuteChanged();
        RefreshCommand?.NotifyCanExecuteChanged(); StartCreateYearCommand?.NotifyCanExecuteChanged(); CreateYearCommand?.NotifyCanExecuteChanged();
        OpenTrialBalanceCommand?.NotifyCanExecuteChanged();
    }

    partial void OnIsCreatingYearChanged(bool value) => NotifyCommands();
}
