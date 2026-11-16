using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;
using PetraERP.Desktop.Connection;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Workspaces;

/// <summary>Company files list (double-click opens) plus step-by-step wizards for a new company or fiscal year.</summary>
public sealed partial class WorkspaceSelectionViewModel : ObservableObject
{
    private readonly IDesktopClient client;
    private readonly ILocalizationService localization;
    private readonly DesktopOperation operation;
    private readonly Action<WorkspaceSummaryResponse> onSelect;
    private readonly Action? onCancel;

    [ObservableProperty] private string searchText = string.Empty;
    [ObservableProperty] private WorkspaceSummaryResponse? selectedWorkspace;
    [ObservableProperty] private bool canCancel;
    [ObservableProperty] private string statusMessage = string.Empty;

    public ObservableCollection<WorkspaceSummaryResponse> AllWorkspaces { get; } = [];
    public ObservableCollection<WorkspaceSummaryResponse> FilteredWorkspaces { get; } = [];

    public ILocalizationService Localization => localization;
    public DesktopOperation Operation => operation;
    public bool HasNoWorkspaces => FilteredWorkspaces.Count == 0;

    public IAsyncRelayCommand RefreshCommand { get; }
    public IRelayCommand OpenWorkspaceCommand { get; }
    public IRelayCommand<WorkspaceSummaryResponse> OpenWorkspaceItemCommand { get; }
    public IRelayCommand CancelCommand { get; }

    public WorkspaceSelectionViewModel(
        IDesktopClient client,
        ILocalizationService localization,
        DesktopOperation operation,
        Action<WorkspaceSummaryResponse> onSelect,
        Action? onCancel = null)
    {
        this.client = client;
        this.localization = localization;
        this.operation = operation;
        this.onSelect = onSelect;
        this.onCancel = onCancel;

        RefreshCommand = new AsyncRelayCommand(LoadWorkspacesAsync);
        OpenWorkspaceCommand = new RelayCommand(OpenSelected, () => SelectedWorkspace?.State == "Active" && !IsWizardOpen);
        OpenWorkspaceItemCommand = new RelayCommand<WorkspaceSummaryResponse>(item =>
        {
            if (item is null || IsWizardOpen) return;
            SelectedWorkspace = item;
            onSelect(item);
        });
        CancelCommand = new RelayCommand(() => onCancel?.Invoke(), () => CanCancel);
        StartCreateCompanyCommand = new AsyncRelayCommand(StartCreateCompanyAsync);
        StartCreateFiscalYearCommand = new RelayCommand(StartCreateFiscalYear, () => SelectedWorkspace is not null);
        NextCommand = new RelayCommand(Next, () => IsWizardOpen && !IsLastStep && StepIsValid());
        BackCommand = new RelayCommand(() => WizardStep--, () => IsWizardOpen && WizardStep > 0);
        FinishCommand = new AsyncRelayCommand(FinishAsync, () => IsWizardOpen && IsLastStep && !operation.IsBusy);
        CancelWizardCommand = new RelayCommand(() => Wizard = WorkspaceWizardKind.None);
    }

    private string Text(string arabic, string english) => localization.IsRightToLeft ? arabic : english;

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnCanCancelChanged(bool value) => CancelCommand.NotifyCanExecuteChanged();

    partial void OnSelectedWorkspaceChanged(WorkspaceSummaryResponse? value)
    {
        OpenWorkspaceCommand.NotifyCanExecuteChanged();
        StartCreateFiscalYearCommand.NotifyCanExecuteChanged();
    }

    public async Task LoadWorkspacesAsync()
    {
        await operation.RunAsync(LoadWorkspacesCoreAsync).ConfigureAwait(true);
    }

    // Sign-in already owns the shell operation; let its caller handle failures without nesting RunAsync.
    internal async Task LoadWorkspacesCoreAsync()
    {
        var result = await client.WorkspacesAsync(CancellationToken.None).ConfigureAwait(true);
        AllWorkspaces.Clear();
        foreach (var item in result.Items.OrderBy(w => w.CompanyCode, StringComparer.Ordinal).ThenByDescending(w => w.YearNumber))
            AllWorkspaces.Add(item);
        ApplyFilter();
        if (SelectedWorkspace is null || !AllWorkspaces.Any(w => w.WorkspaceId == SelectedWorkspace.WorkspaceId))
            SelectedWorkspace = FilteredWorkspaces.FirstOrDefault(w => w.IsDefault) ?? FilteredWorkspaces.FirstOrDefault();
    }

    public void Preselect(Guid? companyId, Guid? fiscalYearId)
    {
        if (companyId is null) return;
        var match = AllWorkspaces.FirstOrDefault(w => w.CompanyId == companyId.Value && (fiscalYearId == null || w.FiscalYearId == fiscalYearId.Value))
            ?? AllWorkspaces.FirstOrDefault(w => w.CompanyId == companyId.Value);
        if (match is not null) SelectedWorkspace = match;
    }

    private void ApplyFilter()
    {
        FilteredWorkspaces.Clear();
        var query = SearchText.Trim();
        foreach (var w in AllWorkspaces)
        {
            if (string.IsNullOrWhiteSpace(query)
                || w.CompanyCode.Contains(query, StringComparison.OrdinalIgnoreCase)
                || w.CompanyNameArabic.Contains(query, StringComparison.OrdinalIgnoreCase)
                || (w.CompanyNameEnglish != null && w.CompanyNameEnglish.Contains(query, StringComparison.OrdinalIgnoreCase))
                || w.FiscalYearCode.Contains(query, StringComparison.OrdinalIgnoreCase)
                || w.YearNumber.ToString(System.Globalization.CultureInfo.InvariantCulture).Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                FilteredWorkspaces.Add(w);
            }
        }
        OnPropertyChanged(nameof(HasNoWorkspaces));
    }

    private void OpenSelected()
    {
        if (SelectedWorkspace is not null) onSelect(SelectedWorkspace);
    }
}
