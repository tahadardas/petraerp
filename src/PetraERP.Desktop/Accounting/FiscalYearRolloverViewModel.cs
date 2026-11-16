using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;
using PetraERP.Desktop.Connection;
using PetraERP.Desktop.Controls;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Accounting;

public sealed partial class FiscalYearRolloverViewModel : PageViewModel
{
    protected override string RequiredPermission => "System.Database.Manage";

    private readonly DesktopSession session;
    private readonly IUserConfirmation confirmation;

    [ObservableProperty] private FiscalYearResponse? selectedSourceYear;
    [ObservableProperty] private FiscalYearResponse? selectedTargetYear;
    [ObservableProperty] private RolloverPreviewResponse? preview;
    [ObservableProperty] private string reason = "Year-end closing rollover";
    [ObservableProperty] private string statusMessage = string.Empty;

    public ObservableCollection<FiscalYearResponse> FiscalYears { get; } = [];

    public bool HasPreview => Preview is not null;
    public bool CanPreview => HasContext && Idle && SelectedSourceYear is not null && SelectedTargetYear is not null && SelectedSourceYear.Id != SelectedTargetYear.Id;
    public bool CanExecute => HasContext && Idle && Can("System.Database.Manage") && Preview is not null && !string.IsNullOrWhiteSpace(Reason);

    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand PreviewCommand { get; }
    public IAsyncRelayCommand ExecuteCommand { get; }

    public FiscalYearRolloverViewModel(
        DesktopSession session,
        IUserConfirmation confirmation,
        DesktopOperation operation,
        ILocalizationService localization) : base(operation, localization)
    {
        this.session = session;
        this.confirmation = confirmation;

        RefreshCommand = new AsyncRelayCommand(LoadFiscalYearsAsync, () => HasContext && Idle);
        PreviewCommand = new AsyncRelayCommand(PreviewRolloverAsync, () => CanPreview);
        ExecuteCommand = new AsyncRelayCommand(ExecuteRolloverAsync, () => CanExecute);
    }

    public async Task LoadFiscalYearsAsync()
    {
        if (!HasContext) return;
        await RunAsync(async token =>
        {
            var years = await session.Api.ListFiscalYearsAsync(CompanyId, BranchId, token).ConfigureAwait(false);
            FiscalYears.Clear();
            foreach (var y in years) FiscalYears.Add(y);
            SelectedSourceYear = FiscalYears.FirstOrDefault();
            SelectedTargetYear = FiscalYears.Skip(1).FirstOrDefault();
            NotifyCommands();
        }).ConfigureAwait(false);
    }

    private async Task PreviewRolloverAsync()
    {
        if (SelectedSourceYear is null || SelectedTargetYear is null) return;
        await RunAsync(async token =>
        {
            StatusMessage = Localization["D_Processing"];
            var request = new RolloverPreviewRequest
            {
                CompanyId = CompanyId,
                SourceFiscalYearId = SelectedSourceYear.Id,
                TargetFiscalYearId = SelectedTargetYear.Id
            };
            Preview = await session.Api.PreviewFiscalYearRolloverAsync(request, token).ConfigureAwait(false);
            StatusMessage = Localization["ACC_RolloverPreviewReady"];
            NotifyCommands();
        }).ConfigureAwait(false);
    }

    private async Task ExecuteRolloverAsync()
    {
        if (Preview is null || SelectedSourceYear is null || SelectedTargetYear is null) return;

        var confirmed = await confirmation.ConfirmAsync("ACC_ConfirmRollover").ConfigureAwait(false);
        if (!confirmed) return;

        await RunAsync(async token =>
        {
            StatusMessage = Localization["D_Processing"];
            var request = new ExecuteRolloverRequest
            {
                CompanyId = CompanyId,
                SourceFiscalYearId = SelectedSourceYear.Id,
                TargetFiscalYearId = SelectedTargetYear.Id,
                SourceFingerprint = Preview.SourceFingerprint,
                Reason = Reason,
                IdempotencyKey = Guid.NewGuid().ToString("N")
            };
            var result = await session.Api.ExecuteFiscalYearRolloverAsync(request, token).ConfigureAwait(false);
            StatusMessage = result.Message;
            Preview = null;
            NotifyCommands();
        }).ConfigureAwait(false);
    }

    protected override void NotifyCommands()
    {
        base.NotifyCommands();
        PreviewCommand.NotifyCanExecuteChanged();
        ExecuteCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HasPreview));
        OnPropertyChanged(nameof(CanPreview));
        OnPropertyChanged(nameof(CanExecute));
    }

    protected override Task LoadAsync(object? parameter, CancellationToken cancellationToken) => LoadFiscalYearsAsync();

    public void Localize() => NotifyCommands();

    protected override void ClearPage()
    {
        FiscalYears.Clear();
        SelectedSourceYear = null;
        SelectedTargetYear = null;
        Preview = null;
        StatusMessage = string.Empty;
    }
}
