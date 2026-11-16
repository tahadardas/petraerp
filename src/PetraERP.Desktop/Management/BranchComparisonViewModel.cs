using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Management;

public sealed record BranchComparisonRow(string AccountCode, string AccountName, string Branch, string Opening, string Debit, string Credit, string Closing, bool IsClearing);

public sealed partial class BranchPick : ObservableObject
{
    public BranchPick(Guid id, string label) { Id = id; Label = label; }
    public Guid Id { get; }
    public string Label { get; }
    [ObservableProperty] private bool isSelected = true;
}

/// <summary>
/// Branch-by-branch trial balance with optional inter-branch elimination. The server requires the report permission on every selected
/// branch; a non-zero inter-branch clearing balance is shown as a residual, never hidden.
/// </summary>
public sealed partial class BranchComparisonViewModel : PageViewModel
{
    public const string ViewPermission = "Ledger.Reports.View";

    private readonly IManagementClient client;

    [ObservableProperty] private DateTime fromDate = new(DateTime.Today.Year, 1, 1);
    [ObservableProperty] private DateTime toDate = DateTime.Today;
    [ObservableProperty] private bool eliminate = true;
    [ObservableProperty] private string totalDebit = string.Empty;
    [ObservableProperty] private string totalCredit = string.Empty;
    [ObservableProperty] private string residual = string.Empty;
    [ObservableProperty] private bool hasResidual;

    public BranchComparisonViewModel(IManagementClient client, DesktopOperation operation, ILocalizationService localization) : base(operation, localization)
    {
        this.client = client;
        RunCommand = new AsyncRelayCommand(() => RunAsync(RunReportAsync), () => HasContext && Idle && Branches.Any(b => b.IsSelected));
    }

    public ObservableCollection<BranchPick> Branches { get; } = [];
    public ObservableCollection<BranchComparisonRow> Rows { get; } = [];
    public AsyncRelayCommand RunCommand { get; }

    protected override string RequiredPermission => ViewPermission;

    protected override async Task LoadAsync(object? parameter, CancellationToken cancellationToken)
    {
        await RunAsync(async token =>
        {
            var list = await client.ListBranchesAsync(CompanyId, BranchId, token).ConfigureAwait(true);
            foreach (var b in Branches) b.PropertyChanged -= PickChanged;
            Branches.Clear();
            foreach (var b in list.Where(b => b.Id.HasValue && b.IsActive == true).OrderBy(b => b.Code, StringComparer.Ordinal))
            {
                var pick = new BranchPick(b.Id!.Value, $"{b.Code} - {b.NameArabic}");
                pick.PropertyChanged += PickChanged;
                Branches.Add(pick);
            }
        }).ConfigureAwait(true);
        NotifyCommands();
    }

    private void PickChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => RunCommand.NotifyCanExecuteChanged();

    protected override void ClearPage() { Branches.Clear(); Rows.Clear(); TotalDebit = TotalCredit = Residual = string.Empty; HasResidual = false; }
    protected override void NotifyCommands() => RunCommand?.NotifyCanExecuteChanged();

    private async Task RunReportAsync(CancellationToken token)
    {
        var ids = string.Join(',', Branches.Where(b => b.IsSelected).Select(b => b.Id));
        var r = await client.GetBranchTrialBalanceAsync(CompanyId, BranchId, DateOnly.FromDateTime(FromDate), DateOnly.FromDateTime(ToDate), ids, Eliminate, token).ConfigureAwait(true);
        var names = (r.Branches ?? []).ToDictionary(b => b.BranchId ?? Guid.Empty, b => b.Code ?? string.Empty);
        Rows.Clear();
        foreach (var row in r.Rows ?? [])
            foreach (var col in row.ByBranch ?? [])
                Rows.Add(new(row.AccountCode ?? string.Empty, row.AccountName ?? string.Empty, names.GetValueOrDefault(col.BranchId ?? Guid.Empty, string.Empty),
                    col.Opening ?? string.Empty, col.Debit ?? string.Empty, col.Credit ?? string.Empty, col.Closing ?? string.Empty, row.IsInterBranchClearing == true));
        TotalDebit = r.TotalDebit ?? string.Empty; TotalCredit = r.TotalCredit ?? string.Empty;
        Residual = r.InterBranchResidual ?? string.Empty;
        HasResidual = !string.IsNullOrEmpty(Residual) && decimal.TryParse(Residual, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var d) && d != 0m;
    }
}
