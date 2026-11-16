using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;
using PetraERP.Desktop.Connection;
using PetraERP.Desktop.Controls;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Accounting;

public sealed partial class ReconciliationViewModel : PageViewModel
{
    protected override string RequiredPermission => "Ledger.Reports.View";

    private readonly DesktopSession session;
    private readonly SemaphoreSlim reportLoad = new(1, 1);

    [ObservableProperty] private ReconciliationReportResponse? report;
    [ObservableProperty] private ReconciliationFindingResponse? selectedFinding;
    [ObservableProperty] private string statusMessage = string.Empty;

    public ObservableCollection<ReconciliationFindingResponse> Findings { get; } = [];

    public bool HasReport => Report is not null;
    public bool HasBlockingDifferences => Report?.HasBlockingDifferences ?? false;
    public string SummaryText => Report is null ? string.Empty :
        $"إجمالي الفحوصات: {Report.TotalChecks} | الناجحة: {Report.PassedChecks} | التنبيهات: {Report.WarningChecks} | المعوقات: {Report.BlockingChecks}";

    public IAsyncRelayCommand RunCommand { get; }
    public IAsyncRelayCommand ExportJsonCommand { get; }
    public IAsyncRelayCommand ExportCsvCommand { get; }

    public ReconciliationViewModel(
        DesktopSession session,
        DesktopOperation operation,
        ILocalizationService localization) : base(operation, localization)
    {
        this.session = session;

        RunCommand = new AsyncRelayCommand(RunReconciliationAsync, () => HasContext && Idle);
        ExportJsonCommand = new AsyncRelayCommand(ExportJsonAsync, () => HasReport && Idle);
        ExportCsvCommand = new AsyncRelayCommand(ExportCsvAsync, () => HasReport && Idle);
    }

    private async Task RunReconciliationAsync()
    {
        if (!HasContext) return;
        var requestedScope = Token;
        try
        {
            await reportLoad.WaitAsync(requestedScope).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (requestedScope.IsCancellationRequested) { return; }

        try
        {
            if (requestedScope.IsCancellationRequested || !HasContext) return;
            await RunAsync(async token =>
            {
                ReconciliationReportResponse rep;
                try { rep = await session.Api.RunAccountingReconciliationAsync(CompanyId, BranchId, token).ConfigureAwait(true); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
                if (token.IsCancellationRequested) return;
                Report = rep;
                Findings.Clear();
                foreach (var f in rep.Findings) Findings.Add(f);
                SelectedFinding = Findings.FirstOrDefault();
                OnPropertyChanged(nameof(SummaryText));
                OnPropertyChanged(nameof(HasReport));
                OnPropertyChanged(nameof(HasBlockingDifferences));
                ExportJsonCommand.NotifyCanExecuteChanged();
                ExportCsvCommand.NotifyCanExecuteChanged();
            }).ConfigureAwait(true);
        }
        finally { reportLoad.Release(); }
    }

    private async Task ExportJsonAsync()
    {
        if (Report is not { } report) return;
        await RunAsync(async token =>
        {
            var json = ReconciliationReportExport.ToJson(report);
            var path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), $"Reconciliation_{DateTime.Now:yyyyMMdd_HHmmss}.json");
            await System.IO.File.WriteAllTextAsync(path, json, token).ConfigureAwait(true);
            token.ThrowIfCancellationRequested();
            StatusMessage = $"تم تصدير التقرير إلى: {path}";
        }).ConfigureAwait(true);
    }

    private async Task ExportCsvAsync()
    {
        if (Report is not { } report) return;
        await RunAsync(async token =>
        {
            var csv = ReconciliationReportExport.ToCsv(report);
            var path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), $"Reconciliation_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            await System.IO.File.WriteAllTextAsync(path, csv, new System.Text.UTF8Encoding(true), token).ConfigureAwait(true);
            token.ThrowIfCancellationRequested();
            StatusMessage = $"تم تصدير التقرير إلى: {path}";
        }).ConfigureAwait(true);
    }

    protected override Task LoadAsync(object? parameter, CancellationToken cancellationToken) => RunReconciliationAsync();

    public void Localize() => NotifyCommands();

    protected override void ClearPage()
    {
        Findings.Clear();
        Report = null;
        SelectedFinding = null;
        StatusMessage = string.Empty;
    }

    protected override void NotifyCommands()
    {
        base.NotifyCommands();
        RunCommand.NotifyCanExecuteChanged();
        ExportJsonCommand.NotifyCanExecuteChanged();
        ExportCsvCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HasReport));
        OnPropertyChanged(nameof(HasBlockingDifferences));
    }
}
