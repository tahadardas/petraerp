using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Reports;

namespace PetraERP.Desktop.Shell;

/// <summary>
/// Al-Ameen 10 Executive Financial Dashboard & Analytics Workspace (PT-125):
/// A dedicated executive screen providing high-level financial KPIs, liquidity ratios,
/// cash & bank portfolio management, commercial papers risk matrices, and direct audit drill-downs.
/// </summary>
public sealed partial class ExecutiveDashboardViewModel : PageViewModel
{
    public const string ViewPermission = "Ledger.Reports.View";
    protected override string RequiredPermission => ViewPermission;

    private readonly DashboardViewModel dashboard;
    private readonly ShellNavigator navigator;

    public ExecutiveDashboardViewModel(
        DesktopOperation operation,
        DashboardViewModel dashboard,
        ShellNavigator navigator,
        ILocalizationService localization) : base(operation, localization)
    {
        this.dashboard = dashboard;
        this.navigator = navigator;

        pageTitle = localization["D_ExecutiveDashboard"];
    }

    public DashboardViewModel Dashboard => dashboard;

    [ObservableProperty] private string pageTitle;

    protected override async Task LoadAsync(object? parameter, CancellationToken cancellationToken)
    {
        PageTitle = Localization["D_ExecutiveDashboard"];
        if (Context is not null)
        {
            await dashboard.RefreshAsync(Context).ConfigureAwait(true);
        }
    }

    protected override void ClearPage()
    {
    }

    // Top Header Commands
    [RelayCommand]
    private async Task RefreshDashboardAsync()
    {
        if (Context is not null)
        {
            await dashboard.RefreshAsync(Context).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task OpenTrialBalanceAsync()
    {
        await navigator.NavigateAsync("trial-balance", null).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task OpenIncomeStatementAsync()
    {
        await navigator.NavigateAsync("financial-reports", new OpenCommercialReportRequest(CommercialReportTab.IncomeStatement)).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task OpenBalanceSheetAsync()
    {
        await navigator.NavigateAsync("financial-reports", new OpenCommercialReportRequest(CommercialReportTab.BalanceSheet)).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task OpenChequesPortfolioAsync()
    {
        await navigator.NavigateAsync("cheques", null).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task OpenGeneralLedgerAsync()
    {
        await navigator.NavigateAsync("account-ledger", null).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task OpenJournalsWorkflowAsync()
    {
        await navigator.NavigateAsync("journals", null).ConfigureAwait(true);
    }
}
