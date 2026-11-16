using PetraERP.Desktop.Accounting;
using PetraERP.Desktop.Administration;
using PetraERP.Desktop.GeneralLedger;
using PetraERP.Desktop.Inventory;
using PetraERP.Desktop.Parties;
using PetraERP.Desktop.Patterns;
using PetraERP.Desktop.Sales;

namespace PetraERP.Desktop.Shell;

/// <summary>Administration views share one view model; these wrappers let the shell select a view per route.</summary>
public sealed record UsersPage(AdministrationViewModel Model);

public sealed record RolesPage(AdministrationViewModel Model);

public sealed record AuditPage(AdministrationViewModel Model);

/// <summary>All workspace pages the shell can host. Only routes listed in <see cref="NavigationService"/> are reachable.</summary>
public sealed class ShellPages
{
    public ShellPages(AdministrationViewModel administration, GeneralLedgerViewModel journals, ChartOfAccountsViewModel accounts,
        CostCentersViewModel costCenters, FiscalPeriodsViewModel periods, GeneralJournalViewModel generalJournal,
        AccountLedgerViewModel accountLedger, TrialBalanceViewModel trialBalance, IFiscalCalendarClient calendar, AccountCardFactory cards,
        JournalDocumentFactory journalDocuments, DocumentPatternsViewModel patterns, PatternCardFactory patternCards,
        PartiesViewModel parties, PartyCardFactory partyCards,
        ItemsViewModel items, ItemCardFactory itemCards, ItemGroupCardFactory itemGroupCards,
        SalesInvoicesViewModel salesInvoices, SalesInvoiceCardFactory salesInvoiceCards,
        Treasury.VouchersViewModel? vouchers = null, Purchases.PurchasesViewModel? purchases = null, InventoryOperationsViewModel? inventoryOperations = null,
        Reports.CommercialReportsViewModel? commercialReports = null, Currencies.CurrencySettingsViewModel? currencySettings = null,
        UnitsViewModel? units = null, Treasury.ChequesViewModel? cheques = null, Inventory.StocktakingWizardViewModel? stocktaking = null, Currencies.FxRevaluationViewModel? fxRevaluation = null,
        ProfessionalReportPages? professionalReports = null,
        OpeningBalancesViewModel? openingBalances = null,
        ReconciliationViewModel? reconciliation = null,
        FiscalYearRolloverViewModel? fiscalYearRollover = null,
        BackupViewModel? backups = null,
        Migration.AlAmeenMigrationViewModel? alAmeenImport = null,
        DocumentProcessing.PostingManagementViewModel? postingManagement = null,
        Management.ManagementPages? management = null,
        DailyVoucherViewModel? dailyVouchers = null,
        AccountDirectoryViewModel? accountDirectory = null, ItemDirectoryViewModel? itemDirectory = null,
        Patterns.PatternManagerViewModel? patternManager = null, ItemLimitsViewModel? itemLimits = null, ItemPriceToolViewModel? itemPriceTool = null)
    {
        DailyVouchers = dailyVouchers; PatternManager = patternManager; ItemLimits = itemLimits; ItemPriceTool = itemPriceTool; AccountDirectory = accountDirectory; ItemDirectory = itemDirectory;
        Management = management;
        Administration = administration; Journals = journals; Accounts = accounts; CostCenters = costCenters; Periods = periods;
        GeneralJournal = generalJournal; AccountLedger = accountLedger; TrialBalance = trialBalance; Calendar = calendar; Cards = cards;
        JournalDocuments = journalDocuments; Patterns = patterns; PatternCards = patternCards;
        Parties = parties; PartyCards = partyCards;
        Items = items; ItemCards = itemCards; ItemGroupCards = itemGroupCards;
        SalesInvoices = salesInvoices; SalesInvoiceCards = salesInvoiceCards; Vouchers = vouchers; Cheques = cheques; Purchases = purchases;
        Users = new UsersPage(administration); Roles = new RolesPage(administration); Audit = new AuditPage(administration);
        InventoryOperations = inventoryOperations; Stocktaking = stocktaking; CommercialReports = commercialReports; CurrencySettings = currencySettings; FxRevaluation = fxRevaluation;
        Units = units;
        ProfessionalReports = professionalReports;
        OpeningBalances = openingBalances;
        Reconciliation = reconciliation;
        FiscalYearRollover = fiscalYearRollover;
        Backups = backups;
        AlAmeenImport = alAmeenImport;
        PostingManagement = postingManagement;
        journals.Calendar = calendar;
    }

    public DailyVoucherViewModel? DailyVouchers { get; }
    public AccountDirectoryViewModel? AccountDirectory { get; }
    public Patterns.PatternManagerViewModel? PatternManager { get; }
    public ItemLimitsViewModel? ItemLimits { get; }
    public ItemPriceToolViewModel? ItemPriceTool { get; }
    public ItemDirectoryViewModel? ItemDirectory { get; }
    public AdministrationViewModel Administration { get; }
    public GeneralLedgerViewModel Journals { get; }
    public ChartOfAccountsViewModel Accounts { get; }
    public CostCentersViewModel CostCenters { get; }
    public FiscalPeriodsViewModel Periods { get; }
    public GeneralJournalViewModel GeneralJournal { get; }
    public AccountLedgerViewModel AccountLedger { get; }
    public TrialBalanceViewModel TrialBalance { get; }
    public IFiscalCalendarClient Calendar { get; }
    /// <summary>Creates one account card per opened account document.</summary>
    public AccountCardFactory Cards { get; }
    /// <summary>Creates one journal view model per journal document; <see cref="Journals"/> stays the shared list page.</summary>
    public JournalDocumentFactory JournalDocuments { get; }
    public DocumentPatternsViewModel Patterns { get; }
    public PatternCardFactory PatternCards { get; }
    public PartiesViewModel Parties { get; }
    public PartyCardFactory PartyCards { get; }
    public ItemsViewModel Items { get; }
    public InventoryOperationsViewModel? InventoryOperations { get; }
    public Inventory.StocktakingWizardViewModel? Stocktaking { get; }
    public UnitsViewModel? Units { get; }
    public Reports.CommercialReportsViewModel? CommercialReports { get; }
    public ProfessionalReportPages? ProfessionalReports { get; }
    public Currencies.CurrencySettingsViewModel? CurrencySettings { get; }
    public Currencies.FxRevaluationViewModel? FxRevaluation { get; }
    public ItemCardFactory ItemCards { get; }
    public ItemGroupCardFactory ItemGroupCards { get; }
    public SalesInvoicesViewModel SalesInvoices { get; }
    public SalesInvoiceCardFactory SalesInvoiceCards { get; }
    public Treasury.VouchersViewModel? Vouchers { get; }
    public Treasury.ChequesViewModel? Cheques { get; }
    public Purchases.PurchasesViewModel? Purchases { get; }
    public OpeningBalancesViewModel? OpeningBalances { get; }
    public ReconciliationViewModel? Reconciliation { get; }
    public BackupViewModel? Backups { get; }
    public Migration.AlAmeenMigrationViewModel? AlAmeenImport { get; }
    public FiscalYearRolloverViewModel? FiscalYearRollover { get; }
    public DocumentProcessing.PostingManagementViewModel? PostingManagement { get; }
    public Management.ManagementPages? Management { get; }
    public UsersPage Users { get; }
    public RolesPage Roles { get; }
    public AuditPage Audit { get; }

    /// <summary>Context-bound ledger pages (administration is installation-wide and handled separately).</summary>
    public IReadOnlyList<IShellPage> LedgerPages => [Journals, Accounts, CostCenters, Periods, GeneralJournal, AccountLedger, TrialBalance, Patterns, Parties, Items, SalesInvoices, .. (Vouchers is null ? Array.Empty<IShellPage>() : [Vouchers]),
        .. (Cheques is null ? Array.Empty<IShellPage>() : [Cheques]),
        .. (DailyVouchers is null ? Array.Empty<IShellPage>() : [DailyVouchers]),
        .. (AccountDirectory is null ? Array.Empty<IShellPage>() : [AccountDirectory]),
        .. (ItemDirectory is null ? Array.Empty<IShellPage>() : [ItemDirectory]),
        .. (PatternManager is null ? Array.Empty<IShellPage>() : [PatternManager]),
        .. (ItemLimits is null ? Array.Empty<IShellPage>() : [ItemLimits]),
        .. (ItemPriceTool is null ? Array.Empty<IShellPage>() : [ItemPriceTool]),
        .. (Purchases is null ? Array.Empty<IShellPage>() : [Purchases]), .. (InventoryOperations is null ? Array.Empty<IShellPage>() : [InventoryOperations]),
        .. (Stocktaking is null ? Array.Empty<IShellPage>() : [Stocktaking]),
        .. (CommercialReports is null ? Array.Empty<IShellPage>() : [CommercialReports]), .. (CurrencySettings is null ? Array.Empty<IShellPage>() : [CurrencySettings]),
        .. (Units is null ? Array.Empty<IShellPage>() : [Units]), .. (FxRevaluation is null ? Array.Empty<IShellPage>() : [FxRevaluation]),
        .. (OpeningBalances is null ? Array.Empty<IShellPage>() : [OpeningBalances]),
        .. (Reconciliation is null ? Array.Empty<IShellPage>() : [Reconciliation]),
        .. (FiscalYearRollover is null ? Array.Empty<IShellPage>() : [FiscalYearRollover]),
        .. (Backups is null ? Array.Empty<IShellPage>() : [Backups]),
        .. (AlAmeenImport is null ? Array.Empty<IShellPage>() : [AlAmeenImport]),
        .. (PostingManagement is null ? Array.Empty<IShellPage>() : [PostingManagement]),
        .. (Management?.Pages ?? Array.Empty<IShellPage>()),
        .. (ProfessionalReports?.Pages ?? Array.Empty<IShellPage>())];

    public IShellPage? Page(string route) => route switch
    {
        "journals" => Journals,
        "accounts" => Accounts,
        "cost-centers" => CostCenters,
        "periods" => Periods,
        "general-journal" => GeneralJournal,
        "account-ledger" => (IShellPage?)ProfessionalReports?.AccountLedger ?? AccountLedger,
        "trial-balance" => (IShellPage?)ProfessionalReports?.TrialBalance ?? TrialBalance,
        "customer-statement" => ProfessionalReports?.CustomerStatement,
        "supplier-statement" => ProfessionalReports?.SupplierStatement,
        "party-statement" => ProfessionalReports?.CombinedStatement,
        "currency-balance" => ProfessionalReports?.CurrencyBalance,
        "patterns" => Patterns,
        "parties" => Parties,
        "items" => Items,
        "inventory-operations" => InventoryOperations,
        "stocktaking" => Stocktaking,
        "financial-reports" => CommercialReports,
        "currencies" => CurrencySettings,
        "fx-revaluation" => FxRevaluation,
        "units" => Units,
        "sales-invoices" => SalesInvoices,
        "treasury-vouchers" => Vouchers,
        "daily-vouchers" => DailyVouchers,
        "account-directory" => AccountDirectory,
        "item-directory" => ItemDirectory,
        "pattern-manager" => PatternManager,
        "item-limits" => ItemLimits,
        "item-price-tool" => ItemPriceTool,
        "cheques" => Cheques,
        "purchase-invoices" => Purchases,
        "opening-balances" => OpeningBalances,
        "reconciliation" => Reconciliation,
        "fiscal-year-rollover" => FiscalYearRollover,
        "backups" => Backups,
            "alameen-import" => AlAmeenImport,
        "posting-management" => PostingManagement,
        "warehouses" => Management?.Warehouses,
        "branches" => Management?.Branches,
        "branch-comparison" => Management?.BranchComparison,
        "inter-company" => Management?.InterCompany,
        "rate-bulletin" => Management?.RateBulletin,
        "document-numbers" => Management?.DocumentNumbers,
        "bank-reconciliation" => Management?.BankReconciliation,
        "reports-center" => Management?.ReportsCenter,
        "analytical-report" => Management?.Analytical,
        _ => null,
    };

    /// <summary>The object the content host renders for a route (data templates select the view by type).</summary>
    public object? View(string route) => route switch
    {
        "users" => Users,
        "roles" => Roles,
        "audit" => Audit,
        _ => Page(route),
    };

    public void ClearLedger()
    {
        foreach (var page in LedgerPages) page.Clear();
    }

    public void Localize()
    {
        Journals.Localize(); Accounts.Localize(); CostCenters.Localize(); Periods.Localize();
        GeneralJournal.Localize(); AccountLedger.Localize(); TrialBalance.Localize();
        Patterns.Localize(); Parties.Localize(); Items.Localize(); SalesInvoices.Localize();
        Vouchers?.Localize();
        DailyVouchers?.Localize(); AccountDirectory?.Localize(); ItemDirectory?.Localize(); PatternManager?.Localize(); ItemLimits?.Localize(); ItemPriceTool?.Localize();
        Cheques?.Localize();
        InventoryOperations?.Localize(); CurrencySettings?.Localize(); Units?.Localize(); FxRevaluation?.Localize();
        OpeningBalances?.Localize(); Reconciliation?.Localize(); FiscalYearRollover?.Localize(); Backups?.Localize(); AlAmeenImport?.Localize();
        PostingManagement?.Localize();
    }
}
