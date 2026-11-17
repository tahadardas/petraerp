using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using PetraERP.Desktop.Accounting;
using PetraERP.Desktop.Administration;
using PetraERP.Desktop.Connection;
using PetraERP.Desktop.GeneralLedger;
using PetraERP.Desktop.Inventory;
using PetraERP.Desktop.Localization;
using PetraERP.Desktop.Parties;
using PetraERP.Desktop.Patterns;
using PetraERP.Desktop.Productivity;
using PetraERP.Desktop.Sales;
using PetraERP.Desktop.Security;
using PetraERP.Desktop.Shell;
using PetraERP.Desktop.Theming;

namespace PetraERP.Desktop.Hosting;

/// <summary>The only service-provider resolution boundary. All features receive constructor dependencies.</summary>
public static class DesktopComposition
{
    public static ServiceProvider Build(System.Windows.Application application, DesktopSettings settings)
    {
        var services = new ServiceCollection();
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PetraERP");
        var endpoint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(settings.ApiBaseUri.AbsoluteUri)));
        services.AddSingleton(settings);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(new LocalizationService("ar"));
        services.AddSingleton<ILocalizationService>(provider => provider.GetRequiredService<LocalizationService>());
        services.AddSingleton<IThemeService>(_ => new WpfThemeService(application));
        services.AddSingleton<IProtectedSessionStore>(_ => new WindowsSessionStore(settings.ApiBaseUri, Path.Combine(directory, "Sessions")));
        services.AddSingleton(provider => new DesktopSession(settings.ApiBaseUri, provider.GetRequiredService<IProtectedSessionStore>()));
        services.AddSingleton<IDesktopClient, DesktopClient>();
        services.AddSingleton<IAlAmeenMigrationClient>(provider => (DesktopClient)provider.GetRequiredService<IDesktopClient>());
        services.AddSingleton<IAdministrationClient, AdministrationClient>();
        services.AddSingleton<IGeneralLedgerClient, GeneralLedgerClient>();
        services.AddSingleton<IWorkspaceClient, WorkspaceClient>();
        services.AddSingleton<IChartOfAccountsClient, ChartOfAccountsClient>();
        services.AddSingleton<IFiscalCalendarClient, FiscalCalendarClient>();
        services.AddSingleton<DocumentProcessing.IDocumentPostingClient, DocumentPostingClient>();
        services.AddSingleton<ILedgerReportsClient, LedgerReportsClient>();
        services.AddSingleton<IPatternsClient, PatternsClient>();
        services.AddSingleton<IPartiesClient, PartiesClient>();
        services.AddSingleton<IInventoryClient, InventoryClient>();
        services.AddSingleton<IInventoryDocumentsClient, InventoryDocumentsClient>();
        services.AddSingleton<InventoryOperationsViewModel>();
        services.AddSingleton<IUnitsClient, UnitsClient>();
        services.AddSingleton<UnitsViewModel>();
        services.AddSingleton<ISalesInvoiceClient, SalesInvoiceClient>();
        services.AddSingleton<Treasury.ITreasuryVouchersClient, TreasuryVouchersClient>();
        services.AddSingleton<Treasury.ICommercialPapersClient, CommercialPapersClient>();
        services.AddSingleton<Currencies.IFxRevaluationClient, FxRevaluationClient>();
        services.AddSingleton<Purchases.IPurchaseInvoicesClient, PurchaseInvoicesClient>();
        services.AddSingleton<Reports.IReportsClient, ReportsClient>();
        services.AddSingleton<ProfessionalReportsClient>();
        services.AddSingleton<Reporting.IProfessionalReportsClient>(provider => provider.GetRequiredService<ProfessionalReportsClient>());
        services.AddSingleton<Reporting.IProfessionalReportLookups>(provider => provider.GetRequiredService<ProfessionalReportsClient>());
        services.AddSingleton<Reporting.IProfessionalReportPresetStore>(_ => new Reporting.ProfessionalReportPresetStore(Path.Combine(directory, "Preferences", endpoint)));
        services.AddSingleton<ProfessionalReportPages>();
        services.AddSingleton<CurrenciesClient>();
        services.AddSingleton<Currencies.ICurrenciesClient>(provider => provider.GetRequiredService<CurrenciesClient>());
        services.AddSingleton<Currencies.IPricingClient>(provider => provider.GetRequiredService<CurrenciesClient>());
        services.AddSingleton<ISalesDocumentLinksClient, SalesDocumentLinksClient>();
        services.AddSingleton<SalesDocumentLinksFactory>();
        services.AddSingleton<Relationships.IRelatedDocumentsClient, Relationships.RelatedDocumentsClient>();
        services.AddSingleton<Relationships.RelatedDocumentsFactory>();
        services.AddSingleton<IContextPreferenceStore>(_ => new ContextPreferenceStore(Path.Combine(directory, "Context", endpoint)));
        services.AddSingleton<IUserPreferenceStore>(_ => new UserPreferenceStore(Path.Combine(directory, "Preferences", endpoint)));
        services.AddSingleton<UserPreferencesService>();
        services.AddSingleton(provider => new DashboardViewModel(provider.GetRequiredService<IGeneralLedgerClient>(),
            provider.GetRequiredService<Treasury.ICommercialPapersClient>(), provider.GetRequiredService<ILocalizationService>(),
            provider.GetService<Reports.IReportsClient>(), provider.GetService<ShellNavigator>()));
        services.AddSingleton<ExecutiveDashboardViewModel>();
        services.AddSingleton<ShellDialogService>();
        services.AddSingleton<IUserConfirmation>(provider => provider.GetRequiredService<ShellDialogService>());
        services.AddSingleton<NotificationCenter>();
        services.AddSingleton<ShellNavigator>();
        services.AddSingleton(provider => new DesktopOperation(provider.GetRequiredService<ILocalizationService>(), provider.GetRequiredService<NotificationCenter>()));
        services.AddSingleton<NavigationService>();
        services.AddSingleton<AdministrationViewModel>(); services.AddSingleton<GeneralLedgerViewModel>(); services.AddSingleton<WorkspaceViewModel>();
        services.AddSingleton<ChartOfAccountsViewModel>(); services.AddSingleton<CostCentersViewModel>(); services.AddSingleton<FiscalPeriodsViewModel>();
        services.AddSingleton<DailyVoucherViewModel>(); services.AddSingleton<AccountDirectoryViewModel>(); services.AddSingleton<ItemLimitsViewModel>(); services.AddSingleton<ItemPriceToolViewModel>(); services.AddSingleton<Patterns.PatternManagerViewModel>(); services.AddSingleton<ItemDirectoryViewModel>(); services.AddSingleton<GeneralJournalViewModel>(); services.AddSingleton<AccountLedgerViewModel>(); services.AddSingleton<TrialBalanceViewModel>();
        services.AddSingleton<DocumentPatternsViewModel>(); services.AddSingleton<PartiesViewModel>(); services.AddSingleton<ItemsViewModel>();
        services.AddSingleton<Inventory.StocktakingWizardViewModel>();
        services.AddSingleton<Inventory.AssemblyDisassemblyViewModel>();
        services.AddSingleton<Assets.IAssetsClient, Assets.AssetsMockService>();
        services.AddSingleton<Assets.AssetDirectoryViewModel>();
        services.AddSingleton<Assets.FleetExpenseJournalViewModel>();
        services.AddSingleton<Assets.FuelDispensingViewModel>();
        services.AddSingleton<Assets.AssetCustodyViewModel>();
        services.AddSingleton<Assets.CustomerRentalViewModel>();
        services.AddSingleton<Assets.AssetDossierTcoViewModel>();
        services.AddSingleton<Assets.AssetDepreciationRunViewModel>();
        services.AddSingleton<Assets.MaintenanceWorkOrderViewModel>();
        services.AddSingleton<SalesInvoicesViewModel>();
        services.AddSingleton<Treasury.VouchersViewModel>();
        services.AddSingleton<Treasury.ChequesViewModel>();
        services.AddSingleton<Currencies.FxRevaluationViewModel>();
        services.AddSingleton<Management.IManagementClient, ManagementClient>();
        services.AddSingleton<Management.BranchesViewModel>();
        services.AddSingleton<Management.WarehousesViewModel>();
        services.AddSingleton<Management.RateBulletinViewModel>();
        services.AddSingleton<Management.DocumentNumbersViewModel>();
        services.AddSingleton<Management.IBankReconciliationClient, BankReconciliationClient>();
        services.AddSingleton<Management.BankReconciliationViewModel>();
        services.AddSingleton<Management.InterCompanyViewModel>();
        services.AddSingleton<Management.BranchComparisonViewModel>();
        services.AddSingleton<Management.ReportsCenterViewModel>();
        services.AddSingleton<Management.AnalyticalReportViewModel>();
        services.AddSingleton<Management.ManagementPages>();
        services.AddSingleton<Purchases.PurchasesViewModel>();
        services.AddSingleton<Reports.CommercialReportsViewModel>();
        services.AddSingleton<Currencies.CurrencySettingsViewModel>();
        services.AddSingleton<OpeningBalancesViewModel>();
        services.AddSingleton<ReconciliationViewModel>();
        services.AddSingleton<FiscalYearRolloverViewModel>();
        services.AddSingleton<BackupViewModel>();
        services.AddSingleton<DocumentProcessing.PostingManagementViewModel>();
        services.AddSingleton<Migration.AlAmeenMigrationViewModel>();
        services.AddSingleton<AccountCardFactory>(); services.AddSingleton<JournalDocumentFactory>(); services.AddSingleton<PatternCardFactory>();
        services.AddSingleton<PartyCardFactory>(); services.AddSingleton<ItemCardFactory>(); services.AddSingleton<ItemGroupCardFactory>(); services.AddSingleton<SalesInvoiceCardFactory>();
        services.AddSingleton<Sales.FirstPaymentRegistrar>();
        services.AddSingleton<ShellPages>();
        services.AddSingleton<DesktopViewModel>(); services.AddSingleton<DesktopWindow>();
        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        WireDesignSystem(provider);
        return provider;
    }

    /// <summary>Connects the design-system controls (which hold no services) to localized text and shell notifications.</summary>
    private static void WireDesignSystem(ServiceProvider provider)
    {
        var localization = provider.GetRequiredService<ILocalizationService>();
        var notifications = provider.GetRequiredService<NotificationCenter>();
        Controls.PetraInputText.Localize = key => localization[key];
        Controls.GridClipboard.PasteCompleted = (applied, rejected) =>
        {
            var message = string.Format(localization.Culture, localization["UI_PasteResult"], applied, rejected);
            notifications.Text(rejected == 0 ? NotificationKind.Info : NotificationKind.Warning, rejected == 0 ? "D_Toast_Info" : "D_Toast_Warning", message);
        };
    }
}
