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

    private readonly DashboardViewModel dashboard;
    private readonly ShellNavigator navigator;
    private readonly ILocalizationService localization;

    private ShellContext? currentContext;

    public ExecutiveDashboardViewModel(
        DashboardViewModel dashboard,
        ShellNavigator navigator,
        ILocalizationService localization)
    {
        this.dashboard = dashboard;
        this.navigator = navigator;
        this.localization = localization;

        PageTitle = localization["D_ExecutiveDashboard"];
    }

    public DashboardViewModel Dashboard => dashboard;

    [ObservableProperty] private string pageTitle;

    public override async Task ActivateAsync(ShellContext shellContext, object? parameter, CancellationToken cancellationToken)
    {
        await base.ActivateAsync(shellContext, parameter, cancellationToken).ConfigureAwait(true);
        currentContext = shellContext;
        PageTitle = localization["D_ExecutiveDashboard"];
        await dashboard.RefreshAsync(shellContext).ConfigureAwait(true);
    }

    public override void Clear()
    {
        currentContext = null;
        base.Clear();
    }

    // Top Header Commands
    [RelayCommand]
    private async Task RefreshDashboardAsync()
    {
        if (currentContext is not null)
        {
            await dashboard.RefreshAsync(currentContext).ConfigureAwait(true);
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
