using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PetraERP.Desktop.Localization;

namespace PetraERP.Desktop.Shell;

/// <summary>A real, permitted destination. <see cref="Glyph"/> is a Segoe Fluent Icons code point (system font, not bundled).</summary>
public sealed record NavigationItem(string Id, string Label, string Group, string Glyph);

/// <summary>
/// A node of the nested top menu (Al-Ameen style: a menu holds cards plus sub-menus such as "reports" and "tools"). A leaf navigates to
/// <see cref="Id"/>; a sub-menu has children and no destination.
/// </summary>
public sealed record MenuEntry(string Id, string Label, IReadOnlyList<MenuEntry> Children)
{
    public bool IsLeaf => Children.Count == 0;
}

public sealed record NavigationGroup(string Id, string Label, IReadOnlyList<NavigationItem> Items);

/// <summary>
/// Builds the top menu and toolbar only from implemented routes the user's permissions allow. A route is listed here only
/// together with its backend workflow; unimplemented modules are absent.
/// </summary>
public sealed class NavigationService : ObservableObject
{
    private readonly ILocalizationService localization;

    public NavigationService(ILocalizationService localization)
    {
        this.localization = localization;
        if (localization is LocalizationService concrete)
        {
            concrete.LanguageChanged += (_, _) => Relocalize();
        }
    }

    /// <summary>Route id, group, permission (null = any authenticated user), label key, glyph.</summary>
    internal static readonly (string Id, string Group, string? Permission, string Label, string Glyph)[] Routes =
    [
        ("home", "general", null, "D_Home", ""),
        ("accounts", "accounting", "Ledger.Accounts.View", "D_ChartOfAccounts", ""),
        ("cost-centers", "accounting", "Ledger.CostCenters.View", "D_CostCenters", ""),
        ("journals", "accounting", "Ledger.Journals.View", "D_Journals", ""),
        ("general-journal", "accounting", "Ledger.Reports.View", "D_GeneralJournal", ""),
        ("account-ledger", "accounting", "Ledger.Reports.View", "D_AccountLedger", ""),
        ("trial-balance", "accounting", "Ledger.Reports.View", "D_TrialBalance", ""),
        ("currency-balance", "accounting", "Ledger.Reports.View", "RPT_CurrencyBalance", ""),
        ("customer-statement", "parties", "Ledger.Reports.View", "RPT_CustomerStatement", ""),
        ("supplier-statement", "parties", "Ledger.Reports.View", "RPT_SupplierStatement", ""),
        ("party-statement", "parties", "Ledger.Reports.View", "RPT_CombinedStatement", ""),
        ("periods", "accounting", "Ledger.Periods.View", "D_FiscalPeriods", ""),
        ("parties", "parties", "Parties.Directory.View", "D_Parties", ""),
        ("items", "inventory", "Inventory.Items.View", "D_Items", ""),
        ("units", "inventory", "Inventory.Items.View", "UNT_Title", ""),
        ("inventory-operations", "inventory", "Inventory.Movements.View", "INV_Title", ""),
        ("assembly-disassembly", "inventory", "Inventory.Movements.View", "D_AssemblyDisassembly", "\uE74C"),
        ("assets-directory", "assets", "Ledger.Accounts.View", "AST_Directory", "\uE71D"),
        ("fleet-expenses", "assets", "Ledger.Journals.Manage", "AST_ExpenseJournal", "\uE8A5"),
        ("fuel-dispensing", "assets", "Inventory.Movements.View", "AST_FuelVoucher", "\uE9D2"),
        ("asset-custody", "assets", "Ledger.Accounts.View", "AST_Custody", "\uE77B"),
        ("customer-rentals", "assets", "Sales.Invoices.View", "AST_Rental", "\uE749"),
        ("asset-depreciation", "assets", "Ledger.Journals.Manage", "AST_Depreciation", "\uE74C"),
        ("asset-dossier", "assets", "Ledger.Reports.View", "AST_DossierReport", "\uE9D2"),
        ("stocktaking", "inventory", "Inventory.Movements.View", "INV_StocktakingWizard", ""),
        ("sales-invoices", "sales", "Sales.Invoices.View", "D_SalesInvoices", ""),
        ("treasury-vouchers", "treasury", "Treasury.Vouchers.View", "D_Vouchers", ""),
        ("daily-vouchers", "treasury", "Ledger.Journals.View", "D_DailyVoucher", ""),
        ("account-directory", "accounting", "Ledger.Accounts.View", "D_AccountDirectory", "\uE8FD"),
        ("item-price-tool", "inventory", "Inventory.Items.Manage", "D_PriceTool_Title", "\uE8EF"),
        ("item-limits", "inventory", "Inventory.Items.View", "D_ItemLimits", "\uE9D2"),
        ("item-directory", "inventory", "Inventory.Items.View", "D_ItemDirectory", "\uE8FD"),
        ("cheques", "treasury", "Treasury.Vouchers.View", "D_CommercialPapers", ""),
        ("bank-reconciliation", "treasury", "Treasury.Vouchers.View", "BKR_Title", ""),
        ("purchase-invoices", "purchases", "Purchases.Invoices.View", "D_PurchaseInvoices", ""),
        ("executive-dashboard", "accounting", "Ledger.Reports.View", "D_ExecutiveDashboard", "\uE9D2"),
        ("financial-reports", "accounting", "Ledger.Reports.View", "D_FinancialStatements", ""),
        ("fx-revaluation", "accounting", "Ledger.Accounts.View", "CUR_FxRevaluationWizard", ""),
        ("posting-management", "accounting", "Posting.Batches.Manage", "D_PostingManagement", "\uE8A5"),
        ("opening-balances", "maintenance", "Ledger.Journals.View", "ACC_OpeningBalances", ""),
        ("fiscal-year-rollover", "maintenance", "System.Database.Manage", "ACC_FiscalYearRollover", ""),
        ("reconciliation", "maintenance", "Ledger.Reports.View", "ACC_Reconciliation", ""),
        ("backups", "maintenance", "System.Database.Manage", "BK_Title", ""),
        ("alameen-import", "maintenance", "System.Database.Manage", "AMM_Title", ""),
        ("warehouses", "inventory", "Inventory.Warehouses.View", "WH_Title", ""),
        ("branches", "multi", "System.Branches.View", "BR_Title", ""),
        ("branch-comparison", "multi", "Ledger.Reports.View", "BR_Comparison", ""),
        ("inter-company", "multi", "Ledger.Reports.View", "IC_Title", ""),
        ("rate-bulletin", "currency", "Ledger.Accounts.View", "CUR_RateBulletin", ""),
        ("document-numbers", "tools", "Documents.Numbers.View", "DN_Title", ""),
        ("reports-center", "reports", "Ledger.Reports.View", "RC_Title", ""),
        ("analytical-report", "reports", "Ledger.Reports.View", "RC_Analytical", ""),
        ("users", "administration", "System.Users.View", "D_Users", ""),
        ("roles", "administration", "System.Roles.View", "D_Roles", ""),
        ("audit", "administration", "Audit.Security.View", "D_Audit", ""),
        ("pattern-manager", "settings", "Patterns.Documents.View", "D_PM_Title", "\uE713"),
        ("patterns", "settings", "Patterns.Documents.View", "D_DocumentPatterns", ""),
        ("currencies", "settings", "Ledger.Accounts.View", "CUR_Title", ""),
    ];

    /// <summary>
    /// Routes shown inside a sub-menu of their group instead of directly in it: route id → sub-menu key. Sub-menu keys are the
    /// labels "D_Menu_Sub_{key}" (reports, tools), mirroring Al-Ameen's "تقارير" and "أدوات" branches.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> SubMenus = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["general-journal"] = "reports", ["account-ledger"] = "reports", ["trial-balance"] = "reports", ["financial-reports"] = "reports", ["executive-dashboard"] = "reports",
        ["fx-revaluation"] = "tools", ["posting-management"] = "tools",
        ["customer-statement"] = "reports", ["supplier-statement"] = "reports", ["party-statement"] = "reports", ["currency-balance"] = "reports",
        ["stocktaking"] = "tools", ["units"] = "tools",
        ["branch-comparison"] = "reports", ["inter-company"] = "reports",
    };

    /// <summary>The nested menu of one group: direct items first, then its sub-menus (only non-empty ones).</summary>
    public IReadOnlyList<MenuEntry> MenuFor(string group)
    {
        var inGroup = Items.Where(item => item.Group == group).ToArray();
        var direct = inGroup.Where(item => !SubMenus.ContainsKey(item.Id)).Select(item => new MenuEntry(item.Id, item.Label, [])).ToList();
        foreach (var sub in inGroup.Where(item => SubMenus.ContainsKey(item.Id)).GroupBy(item => SubMenus[item.Id]))
            direct.Add(new MenuEntry("sub:" + group + ":" + sub.Key, localization["D_Menu_Sub_" + sub.Key], sub.Select(item => new MenuEntry(item.Id, item.Label, [])).ToArray()));
        return direct;
    }

    /// <summary>
    /// The whole top menu (ملف، فاتورة، سندات، زبائن، مواد، حسابات، مالية، مراكز، إطار، أدوات، تعليمات) resolved from the single
    /// <see cref="MenuRegistry"/> for the signed-in user's permissions. Routes appear only if they are in <see cref="Routes"/>.
    /// </summary>
    public IReadOnlyList<TopMenuNode> TopMenu => MenuRegistry.BuildTree(granted, Allows, key => localization[key], CommandAllowed);

    /// <summary>Shell commands that need more than being signed in.</summary>
    private bool CommandAllowed(string command) => command switch
    {
        "new-journal" => Has("Ledger.Journals.Manage") && Allows("journals"),
        _ => true,
    };

    public IReadOnlyList<MenuEntry> AccountingMenu => MenuFor("accounting");
    public IReadOnlyList<MenuEntry> TreasuryMenu => MenuFor("treasury");
    public IReadOnlyList<MenuEntry> SalesMenu => MenuFor("sales");
    public IReadOnlyList<MenuEntry> PurchasesMenu => MenuFor("purchases");
    public IReadOnlyList<MenuEntry> PartiesMenu => MenuFor("parties");
    public IReadOnlyList<MenuEntry> InventoryMenu => MenuFor("inventory");
    public IReadOnlyList<MenuEntry> AssetsMenu => MenuFor("assets");
    public IReadOnlyList<MenuEntry> MultiMenu => MenuFor("multi");
    public IReadOnlyList<MenuEntry> CurrencyMenu => MenuFor("currency");
    public IReadOnlyList<MenuEntry> ReportsMenu => MenuFor("reports");
    public IReadOnlyList<MenuEntry> ToolsMenu => MenuFor("tools");
    public IReadOnlyList<MenuEntry> AdministrationMenu => MenuFor("administration");

    private string current = "home";
    private string query = string.Empty;
    private IReadOnlyCollection<string> granted = [];

    public string Query { get => query; set { SetProperty(ref query, value); OnPropertyChanged(nameof(VisibleItems)); OnPropertyChanged(nameof(Groups)); } }
    public IEnumerable<NavigationItem> VisibleItems => Items.Where(Matches);
    public ObservableCollection<NavigationItem> Items { get; } = [];
    public IReadOnlyList<NavigationGroup> Groups => Items.Where(Matches).GroupBy(item => item.Group)
        .Select(group => new NavigationGroup(group.Key, localization["D_NavGroup_" + group.Key], group.ToArray())).ToArray();
    public string Current { get => current; private set { if (SetProperty(ref current, value)) OnPropertyChanged(nameof(CurrentLabel)); } }
    public string CurrentLabel => Items.FirstOrDefault(item => item.Id == Current)?.Label ?? localization["D_Home"];
    public string CurrentGroupLabel => Items.FirstOrDefault(item => item.Id == Current) is { } item ? localization["D_NavGroup_" + item.Group] : string.Empty;
    /// <summary>Top menu "Accounts": permitted ledger routes only.</summary>
    public IReadOnlyList<NavigationItem> AccountingItems => Items.Where(item => item.Group == "accounting").ToArray();
    /// <summary>Top menu "Administration": permitted installation-wide routes only (the menu is hidden when empty).</summary>
    public IReadOnlyList<NavigationItem> AdministrationItems => Items.Where(item => item.Group == "administration").ToArray();
    /// <summary>Top menu "Parties": permitted parties directory routes only.</summary>
    public IReadOnlyList<NavigationItem> PartiesItems => Items.Where(item => item.Group == "parties").ToArray();
    /// <summary>Top menu "Inventory": permitted inventory directory routes only.</summary>
    public IReadOnlyList<NavigationItem> InventoryItems => Items.Where(item => item.Group == "inventory").ToArray();
    /// <summary>Top menu "Sales": permitted sales routes only.</summary>
    public IReadOnlyList<NavigationItem> SalesItems => Items.Where(item => item.Group == "sales").ToArray();
    /// <summary>Top menu "Purchases": permitted purchases routes only.</summary>
    public IReadOnlyList<NavigationItem> PurchasesItems => Items.Where(item => item.Group == "purchases").ToArray();
    /// <summary>Top menu "Treasury": permitted treasury routes only.</summary>
    public IReadOnlyList<NavigationItem> TreasuryItems => Items.Where(item => item.Group == "treasury").ToArray();
    /// <summary>Top menu "Settings": permitted settings routes only.</summary>
    public IReadOnlyList<NavigationItem> SettingsItems => Items.Where(item => item.Group == "settings").ToArray();
    public IReadOnlyList<NavigationItem> MultiItems => Items.Where(item => item.Group == "multi").ToArray();
    public IReadOnlyList<NavigationItem> CurrencyItems => Items.Where(item => item.Group == "currency").ToArray();
    public IReadOnlyList<NavigationItem> ToolsItems => Items.Where(item => item.Group == "tools").ToArray();
    public IReadOnlyList<NavigationItem> ReportsItems => Items.Where(item => item.Group == "reports").ToArray();
    public bool HasMulti => Items.Any(item => item.Group == "multi");
    public bool HasCurrency => Items.Any(item => item.Group == "currency");
    public bool HasTools => Items.Any(item => item.Group == "tools");
    public bool HasReports => Items.Any(item => item.Group == "reports");
    public bool HasAccounting => Items.Any(item => item.Group == "accounting");
    public bool HasParties => Items.Any(item => item.Group == "parties");
    public bool HasInventory => Items.Any(item => item.Group == "inventory");
    public bool HasSales => Items.Any(item => item.Group == "sales");
    public bool HasPurchases => Items.Any(item => item.Group == "purchases");
    public bool HasTreasury => Items.Any(item => item.Group == "treasury");
    public bool HasAdministration => Items.Any(item => item.Group == "administration");
    public bool HasSettings => Items.Any(item => item.Group == "settings");

    public bool Allows(string id) => Items.Any(item => item.Id == id);

    public bool Has(string permission) => granted.Contains(permission, StringComparer.Ordinal);

    public void Configure(IReadOnlyCollection<string> permissions)
    {
        granted = permissions.ToArray();
        Relocalize();
    }

    public void Relocalize()
    {
        var currentGranted = granted;
        Items.Clear();
        foreach (var route in Routes.Where(route => route.Permission is null || currentGranted.Contains(route.Permission, StringComparer.Ordinal)))
        {
            Items.Add(new(route.Id, localization[route.Label], route.Group, route.Glyph));
        }

        if (!Items.Any(item => item.Id == Current)) Current = "home";
        Notify();
    }

    public bool Navigate(string? id)
    {
        if (id is null || !Items.Any(item => item.Id == id)) return false;
        Current = id;
        OnPropertyChanged(nameof(CurrentGroupLabel));
        return true;
    }

    public void Reset() { Current = "home"; Items.Clear(); granted = []; Query = string.Empty; Notify(); }

    private bool Matches(NavigationItem item) =>
        item.Label.Contains(Query, StringComparison.CurrentCultureIgnoreCase) || item.Id.Contains(Query, StringComparison.OrdinalIgnoreCase);

    private void Notify()
    {
        OnPropertyChanged(nameof(VisibleItems));
        OnPropertyChanged(nameof(Groups));
        OnPropertyChanged(nameof(CurrentLabel));
        OnPropertyChanged(nameof(CurrentGroupLabel));
        OnPropertyChanged(nameof(AccountingItems)); OnPropertyChanged(nameof(AdministrationItems));
        OnPropertyChanged(nameof(PartiesItems)); OnPropertyChanged(nameof(InventoryItems));
        OnPropertyChanged(nameof(SalesItems)); OnPropertyChanged(nameof(PurchasesItems));
        OnPropertyChanged(nameof(TreasuryItems)); OnPropertyChanged(nameof(SettingsItems));
        OnPropertyChanged(nameof(HasAccounting)); OnPropertyChanged(nameof(HasParties));
        OnPropertyChanged(nameof(HasInventory)); OnPropertyChanged(nameof(HasSales));
        OnPropertyChanged(nameof(HasPurchases)); OnPropertyChanged(nameof(HasTreasury));
        OnPropertyChanged(nameof(HasAdministration)); OnPropertyChanged(nameof(HasSettings));
        OnPropertyChanged(nameof(MultiItems)); OnPropertyChanged(nameof(CurrencyItems)); OnPropertyChanged(nameof(ToolsItems)); OnPropertyChanged(nameof(ReportsItems));
        foreach (var name in new[] { nameof(AccountingMenu), nameof(TreasuryMenu), nameof(SalesMenu), nameof(PurchasesMenu), nameof(PartiesMenu), nameof(InventoryMenu),
                     nameof(MultiMenu), nameof(CurrencyMenu), nameof(ReportsMenu), nameof(ToolsMenu), nameof(AdministrationMenu) }) OnPropertyChanged(name);
        OnPropertyChanged(nameof(TopMenu));
        OnPropertyChanged(nameof(HasMulti)); OnPropertyChanged(nameof(HasCurrency)); OnPropertyChanged(nameof(HasTools)); OnPropertyChanged(nameof(HasReports));
    }
}
