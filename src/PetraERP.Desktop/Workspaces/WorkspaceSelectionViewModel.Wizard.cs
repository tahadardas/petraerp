using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.ApiClient;

namespace PetraERP.Desktop.Workspaces;

public sealed partial class WorkspaceSelectionViewModel
{
    [ObservableProperty] private WorkspaceWizardKind wizard;
    [ObservableProperty] private int wizardStep;
    [ObservableProperty] private string stepError = string.Empty;

    [ObservableProperty] private string newDatabaseName = string.Empty;
    [ObservableProperty] private RegisteredChartTemplate? selectedAccountTemplate;
    public string AdministratorPassword { private get; set; } = string.Empty;
    public ObservableCollection<RegisteredChartTemplate> AccountTemplates { get; } = [];
    public string TemplatePreview => SelectedAccountTemplate is { } t
        ? t.TemplateSource + Environment.NewLine + string.Join(Environment.NewLine, t.Accounts.Select(a => a.AccountCode + " — " + a.AccountNameArabic)) : string.Empty;
    public string PeriodPreview => NewCompanyStart is { } start && NewCompanyEnd is { } end && start <= end
        ? string.Join(Environment.NewLine, PreviewPeriods(DateOnly.FromDateTime(start), DateOnly.FromDateTime(end))) : string.Empty;
    private static IEnumerable<string> PreviewPeriods(DateOnly start, DateOnly end)
    {
        var cursor = start; var index = 1;
        while (cursor <= end)
        {
            var next = start.AddMonths(index); var last = next.AddDays(-1) > end ? end : next.AddDays(-1);
            yield return $"P{index:D2}: {cursor:yyyy-MM-dd} — {last:yyyy-MM-dd}";
            if (last == end) yield break;
            cursor = last.AddDays(1); index++;
        }
    }
    partial void OnSelectedAccountTemplateChanged(RegisteredChartTemplate? value) => OnPropertyChanged(nameof(TemplatePreview));
    public bool ShowCompanyDatabase => Wizard == WorkspaceWizardKind.Company && WizardStep == 1;
    public bool ShowAdministratorConfirmation => Wizard == WorkspaceWizardKind.Company && WizardStep == 3;
    public bool ShowAccountTemplate => Wizard == WorkspaceWizardKind.Company && WizardStep == 4;

    // New company
    [ObservableProperty] private string newCompanyCode = string.Empty;
    [ObservableProperty] private string newCompanyNameArabic = string.Empty;
    [ObservableProperty] private string newCompanyNameEnglish = string.Empty;
    [ObservableProperty] private SupportedCurrencyResponse? newCompanyCurrency;
    [ObservableProperty] private int newCompanyYear = DateTime.Now.Year;
    [ObservableProperty] private DateTime? newCompanyStart = new DateTime(DateTime.Now.Year, 1, 1);
    [ObservableProperty] private DateTime? newCompanyEnd = new DateTime(DateTime.Now.Year, 12, 31);

    // New fiscal year
    [ObservableProperty] private int newYearNumber = DateTime.Now.Year + 1;
    [ObservableProperty] private string newYearCode = $"FY{DateTime.Now.Year + 1}";
    [ObservableProperty] private bool copyChartOfAccounts = true;
    [ObservableProperty] private bool copyWarehouses = true;
    [ObservableProperty] private bool copyPatterns = true;

    public ObservableCollection<SupportedCurrencyResponse> Currencies { get; } = [];
    public ObservableCollection<WorkspaceWizardStep> Steps { get; } = [];

    public IAsyncRelayCommand StartCreateCompanyCommand { get; }
    public IRelayCommand StartCreateFiscalYearCommand { get; }
    public IRelayCommand NextCommand { get; }
    public IRelayCommand BackCommand { get; }
    public IAsyncRelayCommand FinishCommand { get; }
    public IRelayCommand CancelWizardCommand { get; }

    public bool IsWizardOpen => Wizard != WorkspaceWizardKind.None;
    public bool IsLastStep => WizardStep == Steps.Count - 1;
    public string WizardTitle => Wizard == WorkspaceWizardKind.Company ? Text("معالج إنشاء شركة جديدة", "New company wizard")
        : Text("معالج إنشاء سنة مالية جديدة", "New fiscal year wizard");
    public string StepTitle => WizardStep < Steps.Count ? Steps[WizardStep].Title : string.Empty;
    public string StepCounter => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{WizardStep + 1} / {Steps.Count}");

    // One panel per step; the view binds Visibility to these.
    public bool ShowCompanyInfo => Wizard == WorkspaceWizardKind.Company && WizardStep == 0;
    public bool ShowCompanyCurrency => Wizard == WorkspaceWizardKind.Company && WizardStep == 2;
    public bool ShowCompanyYear => Wizard == WorkspaceWizardKind.Company && WizardStep == 5;
    public bool ShowYearInfo => Wizard == WorkspaceWizardKind.FiscalYear && WizardStep == 0;
    public bool ShowYearCopy => Wizard == WorkspaceWizardKind.FiscalYear && WizardStep == 1;
    public bool ShowReview => IsWizardOpen && IsLastStep;
    public string ReviewText => Wizard == WorkspaceWizardKind.Company ? CompanyReview() : YearReview();

    private async Task StartCreateCompanyAsync()
    {
        NewCompanyCode = string.Empty; NewCompanyNameArabic = string.Empty; NewCompanyNameEnglish = string.Empty;
        NewCompanyYear = DateTime.Now.Year;
        if (Currencies.Count == 0)
        {
            await operation.RunAsync(async () =>
            {
                var list = await client.SupportedCurrenciesAsync(CancellationToken.None).ConfigureAwait(true);
                foreach (var currency in list.Items) Currencies.Add(currency);
            }).ConfigureAwait(true);
        }
        AdministratorPassword = string.Empty;
        NewDatabaseName = $"Petra_Company_{NewCompanyYear}";
        await operation.RunAsync(async () =>
        {
            var templates = await client.AccountTemplatesAsync(CancellationToken.None).ConfigureAwait(true);
            AccountTemplates.Clear(); foreach (var template in templates) AccountTemplates.Add(template);
        }).ConfigureAwait(true);
        SelectedAccountTemplate = AccountTemplates.FirstOrDefault();
        // The base currency is the operator's accounting choice; it is never pre-selected.
        NewCompanyCurrency = null;
        Open(WorkspaceWizardKind.Company, [Text("بيانات الشركة", "Company details"), Text("قاعدة SQL", "SQL database"), Text("العملة الأساسية", "Base currency"),
            Text("تأكيد المدير", "Administrator confirmation"), Text("دليل الحسابات", "Account template"), Text("السنة المالية الأولى", "First fiscal year"), Text("المراجعة والإنشاء", "Review and create")]);
    }

    private void StartCreateFiscalYear()
    {
        if (SelectedWorkspace is null) return;
        NewYearNumber = SelectedWorkspace.YearNumber + 1;
        NewYearCode = $"FY{NewYearNumber}";
        CopyChartOfAccounts = true; CopyWarehouses = true; CopyPatterns = true;
        Open(WorkspaceWizardKind.FiscalYear, [Text("السنة الجديدة", "New year"), Text("ما يُنقل من السنة السابقة", "Carry over"),
            Text("المراجعة والإنشاء", "Review and create")]);
    }

    private void Open(WorkspaceWizardKind kind, IReadOnlyList<string> titles)
    {
        Steps.Clear();
        for (var index = 0; index < titles.Count; index++) Steps.Add(new WorkspaceWizardStep(index + 1, titles[index]));
        StepError = string.Empty;
        Wizard = kind;
        WizardStep = 0;
        RefreshStep();
    }

    private void Next()
    {
        if (!StepIsValid()) return;
        WizardStep++;
    }

    partial void OnWizardChanged(WorkspaceWizardKind value)
    {
        OnPropertyChanged(nameof(IsWizardOpen)); OnPropertyChanged(nameof(WizardTitle));
        OpenWorkspaceCommand.NotifyCanExecuteChanged();
        RefreshStep();
    }

    partial void OnWizardStepChanged(int value) => RefreshStep();

    private void RefreshStep()
    {
        for (var index = 0; index < Steps.Count; index++) { Steps[index].IsCurrent = index == WizardStep; Steps[index].IsDone = index < WizardStep; }
        StepError = string.Empty;
        foreach (var name in new[] { nameof(IsLastStep), nameof(StepTitle), nameof(StepCounter), nameof(ShowCompanyInfo), nameof(ShowCompanyCurrency),
            nameof(ShowCompanyDatabase), nameof(ShowAdministratorConfirmation), nameof(ShowAccountTemplate), nameof(PeriodPreview), nameof(TemplatePreview), nameof(ShowCompanyYear), nameof(ShowYearInfo), nameof(ShowYearCopy), nameof(ShowReview), nameof(ReviewText) })
            OnPropertyChanged(name);
        NotifyWizardCommands();
    }

    private void NotifyWizardCommands()
    {
        NextCommand.NotifyCanExecuteChanged(); BackCommand.NotifyCanExecuteChanged(); FinishCommand.NotifyCanExecuteChanged();
    }
}
