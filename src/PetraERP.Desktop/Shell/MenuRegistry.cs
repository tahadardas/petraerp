namespace PetraERP.Desktop.Shell;

/// <summary>
/// One node of the single description of the top menu (Al-Ameen classic order, right to left:
/// ملف، فاتورة، سندات، زبائن، مواد، حسابات، مالية، مراكز، [نقاط بيع]، إطار، أدوات، تعليمات).
/// A node is exactly one of: a <b>route</b> (opens a workspace document), a <b>command</b> (a shell action dispatched by
/// <see cref="DesktopViewModel.MenuCommand"/>) or a <b>group</b> (neither; shown only while it has a visible child).
/// Production UI shows only nodes that are implemented, authorized and capable.
/// </summary>
/// <param name="Id">Stable unique id.</param>
/// <param name="TitleKey">Localization key of the caption (Arabic caption lives in Strings.ar.resx).</param>
/// <param name="Glyph">Segoe Fluent Icons code point (system font, not bundled).</param>
/// <param name="Shortcut">Display-only gesture text; routing of shortcuts stays in <c>DesktopShortcutRouter</c>.</param>
/// <param name="ParentId">Parent node id, null for a top-level menu.</param>
/// <param name="RequiredPermission">Must equal the permission of the same route in <see cref="NavigationService.Routes"/> (enforced by a test).</param>
/// <param name="RequiredCapability">Optional capability gate (for example an optional POS module).</param>
/// <param name="Route">Navigation route id; the route must exist in <see cref="NavigationService.Routes"/>.</param>
/// <param name="IsImplemented">False keeps a spec-only node out of every production menu.</param>
/// <param name="Order">Sort order among siblings.</param>
/// <param name="Command">Shell command id (see <see cref="DesktopViewModel.MenuCommand"/>).</param>
/// <param name="SeparatorBefore">Draws a separator above the node.</param>
public sealed record MenuNode(
    string Id,
    string TitleKey,
    string? Glyph = null,
    string? Shortcut = null,
    string? ParentId = null,
    string? RequiredPermission = null,
    string? RequiredCapability = null,
    string? Route = null,
    bool IsImplemented = true,
    int Order = 0,
    string? Command = null,
    bool SeparatorBefore = false)
{
    /// <summary>
    /// Evaluates whether this node should be visible in the user interface.
    /// Spec-only nodes (<see cref="IsImplemented"/> is false) are never shown in production menus.
    /// </summary>
    public bool IsVisible(IReadOnlyCollection<string> permissions, string capability = "Desktop")
    {
        if (!IsImplemented) return false;
        if (RequiredCapability is not null && !string.Equals(RequiredCapability, capability, StringComparison.OrdinalIgnoreCase)) return false;
        if (RequiredPermission is not null && !permissions.Contains(RequiredPermission, StringComparer.Ordinal)) return false;
        return true;
    }
}

/// <summary>A resolved, localized and authorized menu entry ready for the WPF menu builder.</summary>
public sealed record TopMenuNode(string Id, string Label, string? Glyph, string? Shortcut, string? Route, string? Command, bool SeparatorBefore, IReadOnlyList<TopMenuNode> Children)
{
    public bool IsLeaf => Children.Count == 0;
}

/// <summary>
/// The single source of the top menu. <see cref="NavigationService"/> resolves it against the user's permissions; the window
/// builds its menu bar from the result, so there is no second description to drift out of date.
/// </summary>
public static class MenuRegistry
{
    // Dynamic command ids handled by the WPF builder (live lists that cannot be known statically).
    public const string OpenWindowsCommand = "open-windows";
    public const string SkinListCommand = "skin-list";

    public static readonly IReadOnlyList<MenuNode> AllNodes =
    [
        // ───────── 1. ملف ─────────
        new("file", "MN_File", Glyph: "", Order: 10),
        new("file.home", "D_Home", Glyph: "", ParentId: "file", Route: "home", Order: 10),
        new("file.workspace", "D_SelectWorkspace", Glyph: "", ParentId: "file", Command: "workspace", Order: 20),
        new("file.new-journal", "D_NewJournal", Glyph: "", ParentId: "file", Command: "new-journal", Order: 30),
        new("file.search", "D_SearchPlaceholder", Glyph: "", Shortcut: "Ctrl+K", ParentId: "file", Command: "palette", Order: 40),
        new("file.reconnect", "D_Reconnect", Glyph: "", ParentId: "file", Command: "reconnect", Order: 50),
        new("file.logout", "UX_SignOut", Glyph: "", ParentId: "file", Command: "logout", Order: 60, SeparatorBefore: true),
        new("file.logout-all", "D_LogoutAll", Glyph: "", ParentId: "file", Command: "logout-all", Order: 70),

        // ───────── 2. فاتورة ─────────
        new("invoices", "MN_Invoice", Glyph: "", Order: 20),
        new("invoices.sales", "D_SalesInvoices", Glyph: "", ParentId: "invoices", RequiredPermission: "Sales.Invoices.View", Route: "sales-invoices", Order: 10),
        new("invoices.purchases", "D_PurchaseInvoices", Glyph: "", ParentId: "invoices", RequiredPermission: "Purchases.Invoices.View", Route: "purchase-invoices", Order: 20),
        new("invoices.assembly", "D_AssemblyDisassembly", Glyph: "\uE74C", ParentId: "invoices", RequiredPermission: "Inventory.Movements.View", Route: "assembly-disassembly", Order: 30),

        // ───────── 3. سندات ─────────
        new("vouchers", "MN_Vouchers", Glyph: "", Order: 30),
        new("vouchers.treasury", "D_Vouchers", Glyph: "", ParentId: "vouchers", RequiredPermission: "Treasury.Vouchers.View", Route: "treasury-vouchers", Order: 10),
        new("vouchers.daily", "D_DailyVoucher", Glyph: "", ParentId: "vouchers", RequiredPermission: "Ledger.Journals.View", Route: "daily-vouchers", Order: 15),
        new("vouchers.journals", "D_Journals", Glyph: "", ParentId: "vouchers", RequiredPermission: "Ledger.Journals.View", Route: "journals", Order: 20),
        new("vouchers.bank-reconciliation", "BKR_Title", ParentId: "vouchers", RequiredPermission: "Treasury.Vouchers.View", Route: "bank-reconciliation", Order: 40),
        new("vouchers.cheques", "D_CommercialPapers", Glyph: "", ParentId: "vouchers", RequiredPermission: "Treasury.Vouchers.View", Route: "cheques", Order: 30),

        // ───────── 4. زبائن ─────────
        new("parties", "MN_Customers", Glyph: "", Order: 40),
        new("parties.directory", "D_Parties", Glyph: "", Shortcut: "F5", ParentId: "parties", RequiredPermission: "Parties.Directory.View", Route: "parties", Order: 10),
        new("parties.customer-statement", "RPT_CustomerStatement", Glyph: "", ParentId: "parties", RequiredPermission: "Ledger.Reports.View", Route: "customer-statement", Order: 20),
        new("parties.supplier-statement", "RPT_SupplierStatement", Glyph: "", ParentId: "parties", RequiredPermission: "Ledger.Reports.View", Route: "supplier-statement", Order: 30),
        new("parties.party-statement", "RPT_CombinedStatement", Glyph: "", ParentId: "parties", RequiredPermission: "Ledger.Reports.View", Route: "party-statement", Order: 40),

        // ───────── 5. مواد ─────────
        new("inventory", "MN_Materials", Glyph: "", Order: 50),
        new("inventory.items", "D_Items", Glyph: "", Shortcut: "F7", ParentId: "inventory", RequiredPermission: "Inventory.Items.View", Route: "items", Order: 10),
        new("inventory.directory", "D_ItemDirectory", Glyph: "", ParentId: "inventory", RequiredPermission: "Inventory.Items.View", Route: "item-directory", Order: 15),
        new("inventory.tools", "D_ItemTools", Glyph: "\ue90f", ParentId: "inventory", Order: 90, SeparatorBefore: true),
        new("inventory.tools.search", "D_SearchPlaceholder", Glyph: "\ue721", Shortcut: "Ctrl+K", ParentId: "inventory.tools", Command: "palette", Order: 10),
        new("inventory.tools.prices", "D_PriceTool_Title", Glyph: "\ue8ef", ParentId: "inventory.tools", RequiredPermission: "Inventory.Items.Manage", Route: "item-price-tool", Order: 15),
        new("inventory.tools.limits", "D_ItemLimits", Glyph: "\ue9d2", ParentId: "inventory.tools", RequiredPermission: "Inventory.Items.View", Route: "item-limits", Order: 20),
        new("inventory.units", "UNT_Title", Glyph: "", ParentId: "inventory", RequiredPermission: "Inventory.Items.View", Route: "units", Order: 20),
        new("inventory.warehouses", "WH_Title", Glyph: "", ParentId: "inventory", RequiredPermission: "Inventory.Warehouses.View", Route: "warehouses", Order: 30),
        new("inventory.operations", "INV_Title", Glyph: "", ParentId: "inventory", RequiredPermission: "Inventory.Movements.View", Route: "inventory-operations", Order: 40),
        new("inventory.stocktaking", "INV_StocktakingWizard", Glyph: "", ParentId: "inventory", RequiredPermission: "Inventory.Movements.View", Route: "stocktaking", Order: 50),

        // ───────── 6. حسابات ─────────
        new("accounts", "MN_Accounts", Glyph: "", Order: 60),
        new("accounts.chart", "D_ChartOfAccounts", Glyph: "", Shortcut: "Ctrl+F11", ParentId: "accounts", RequiredPermission: "Ledger.Accounts.View", Route: "accounts", Order: 10),
        new("accounts.directory", "D_AccountDirectory", Glyph: "", ParentId: "accounts", RequiredPermission: "Ledger.Accounts.View", Route: "account-directory", Order: 15),
        new("accounts.general-journal", "D_GeneralJournal", Glyph: "", ParentId: "accounts", RequiredPermission: "Ledger.Reports.View", Route: "general-journal", Order: 20),
        new("accounts.ledger", "D_AccountLedger", Glyph: "", Shortcut: "Ctrl+L", ParentId: "accounts", RequiredPermission: "Ledger.Reports.View", Route: "account-ledger", Order: 30),
        new("accounts.trial-balance", "D_TrialBalance", Glyph: "", ParentId: "accounts", RequiredPermission: "Ledger.Reports.View", Route: "trial-balance", Order: 40),
        new("accounts.currency-balance", "RPT_CurrencyBalance", Glyph: "", ParentId: "accounts", RequiredPermission: "Ledger.Reports.View", Route: "currency-balance", Order: 45),
        new("accounts.periods", "D_FiscalPeriods", Glyph: "", ParentId: "accounts", RequiredPermission: "Ledger.Periods.View", Route: "periods", Order: 50),
        new("accounts.posting-management", "D_PostingManagement", Glyph: "", ParentId: "accounts", RequiredPermission: "Posting.Batches.Manage", Route: "posting-management", Order: 60),
        // العملات: a sub-menu, as in the reference screens.
        new("accounts.currencies-group", "MN_Currencies", Glyph: "", ParentId: "accounts", Order: 70, SeparatorBefore: true),
        new("accounts.currencies", "CUR_Title", Glyph: "", ParentId: "accounts.currencies-group", RequiredPermission: "Ledger.Accounts.View", Route: "currencies", Order: 10),
        new("accounts.rate-bulletin", "CUR_RateBulletin", Glyph: "", ParentId: "accounts.currencies-group", RequiredPermission: "Ledger.Accounts.View", Route: "rate-bulletin", Order: 20),
        new("accounts.fx-revaluation", "CUR_FxRevaluationWizard", Glyph: "", ParentId: "accounts.currencies-group", RequiredPermission: "Ledger.Accounts.View", Route: "fx-revaluation", Order: 30),

        // ───────── 6.5. أصول (Assets) ─────────
        new("assets", "MN_Assets", Glyph: "\uE71D", Order: 65),
        new("assets.directory", "AST_Directory", Glyph: "\uE71D", ParentId: "assets", RequiredPermission: "Ledger.Accounts.View", Route: "assets-directory", Order: 10),
        new("assets.expense-journal", "AST_ExpenseJournal", Glyph: "\uE8A5", ParentId: "assets", RequiredPermission: "Ledger.Journals.Manage", Route: "fleet-expenses", Order: 20),
        new("assets.fuel-voucher", "AST_FuelVoucher", Glyph: "\uE9D2", ParentId: "assets", RequiredPermission: "Inventory.Movements.View", Route: "fuel-dispensing", Order: 30),
        new("assets.custody", "AST_Custody", Glyph: "\uE77B", ParentId: "assets", RequiredPermission: "Ledger.Accounts.View", Route: "asset-custody", Order: 40),
        new("assets.rental", "AST_Rental", Glyph: "\uE749", ParentId: "assets", RequiredPermission: "Sales.Invoices.View", Route: "customer-rentals", Order: 50),
        new("assets.depreciation", "AST_Depreciation", Glyph: "\uE74C", ParentId: "assets", RequiredPermission: "Ledger.Journals.Manage", Route: "asset-depreciation", Order: 60),
        new("assets.dossier-report", "AST_DossierReport", Glyph: "\uE9D2", ParentId: "assets", RequiredPermission: "Ledger.Reports.View", Route: "asset-dossier", Order: 70),

        // ───────── 7. مالية ─────────
        new("financial", "MN_Finance", Glyph: "", Order: 70),
        new("financial.statements", "D_FinancialStatements", Glyph: "", ParentId: "financial", RequiredPermission: "Ledger.Reports.View", Route: "financial-reports", Order: 10),

        // ───────── 8. مراكز ─────────
        new("cost-centers", "MN_Centers", Glyph: "", Order: 80),
        new("cost-centers.directory", "D_CostCenters", Glyph: "", ParentId: "cost-centers", RequiredPermission: "Ledger.CostCenters.View", Route: "cost-centers", Order: 10),

        // ───────── 9. نقاط بيع (يظهر فقط عند تثبيت موديول POS لاحقًا) ─────────
        new("pos", "D_Menu_Pos", Glyph: "", RequiredCapability: "Pos", IsImplemented: false, Order: 85),
        new("pos.terminal", "D_PointOfSaleTerminal", Glyph: "", ParentId: "pos", RequiredCapability: "Pos", IsImplemented: false, Order: 10),

        // ───────── 10. إطار ─────────
        new("window", "MN_Window", Glyph: "", Order: 90),
        new("window.next", "D_NextTab", Shortcut: "Ctrl+Tab", ParentId: "window", Command: "window-next", Order: 10),
        new("window.previous", "D_PreviousTab", Shortcut: "Ctrl+Shift+Tab", ParentId: "window", Command: "window-previous", Order: 20),
        new("window.close", "D_CloseTab", Shortcut: "Ctrl+W", ParentId: "window", Command: "window-close", Order: 30),
        new("window.close-other", "D_CloseOtherTabs", ParentId: "window", Command: "window-close-others", Order: 40),
        new("window.close-all", "D_CloseAllTabs", ParentId: "window", Command: "window-close-all", Order: 50),
        new("window.detach", "D_DetachTab", ParentId: "window", Command: "window-detach", Order: 60),
        new("window.tile-horizontal", "D_TileHorizontally", ParentId: "window", Command: "window-tile-horizontal", Order: 70, SeparatorBefore: true),
        new("window.tile-vertical", "D_TileVertically", ParentId: "window", Command: "window-tile-vertical", Order: 80),
        new("window.cascade", "D_CascadeWindows", ParentId: "window", Command: "window-cascade", Order: 90),
        new("window.minimize", "D_MinimizeActive", ParentId: "window", Command: "window-minimize", Order: 100),
        new("window.restore-all", "D_RestoreAll", ParentId: "window", Command: "window-restore-all", Order: 110),
        new("window.bring-front", "D_BringAllToFront", ParentId: "window", Command: "window-bring-front", Order: 120),
        new("window.reattach", "D_ReattachAllTabs", ParentId: "window", Command: "window-reattach", Order: 130),
        new("window.open-list", "D_OpenTabs", ParentId: "window", Command: OpenWindowsCommand, Order: 140, SeparatorBefore: true),

        // ───────── صيانة الملفات (Al-Ameen "الصيانات"): screens that already exist, one menu for the file-level work ─────────
        new("maintenance", "MN_Maintenance", Glyph: "", Order: 95),
        new("maintenance.opening-balances", "ACC_OpeningBalances", ParentId: "maintenance", RequiredPermission: "Ledger.Journals.View", Route: "opening-balances", Order: 10),
        new("maintenance.rollover", "ACC_FiscalYearRollover", ParentId: "maintenance", RequiredPermission: "System.Database.Manage", Route: "fiscal-year-rollover", Order: 20),
        new("maintenance.reconciliation", "ACC_Reconciliation", ParentId: "maintenance", RequiredPermission: "Ledger.Reports.View", Route: "reconciliation", Order: 30),
        new("maintenance.backups", "BK_Title", ParentId: "maintenance", RequiredPermission: "System.Database.Manage", Route: "backups", Order: 40),
        new("maintenance.alameen-import", "AMM_Title", ParentId: "maintenance", RequiredPermission: "System.Database.Manage", Route: "alameen-import", Order: 50),

        // ───────── 11. أدوات ─────────
        new("tools", "MN_Tools", Glyph: "", Order: 100),
        new("tools.template-designer", "PRN_TemplateDesignerTitle", Glyph: "\uE790", ParentId: "tools", Command: "template-designer", Order: 5),
        new("tools.admin", "D_Menu_Administration", Glyph: "", ParentId: "tools", Order: 10),
        new("tools.users", "D_Users", Glyph: "", ParentId: "tools.admin", RequiredPermission: "System.Users.View", Route: "users", Order: 10),
        new("tools.roles", "D_Roles", Glyph: "", ParentId: "tools.admin", RequiredPermission: "System.Roles.View", Route: "roles", Order: 20),
        new("tools.audit", "D_Audit", Glyph: "", ParentId: "tools.admin", RequiredPermission: "Audit.Security.View", Route: "audit", Order: 30),
        new("tools.document-numbers", "DN_Title", Glyph: "", ParentId: "tools.admin", RequiredPermission: "Documents.Numbers.View", Route: "document-numbers", Order: 40),
        new("tools.pattern-manager", "D_PM_Title", Glyph: "\ue713", ParentId: "tools.admin", RequiredPermission: "Patterns.Documents.View", Route: "pattern-manager", Order: 45),
        new("tools.patterns", "D_DocumentPatterns", Glyph: "", ParentId: "tools.admin", RequiredPermission: "Patterns.Documents.View", Route: "patterns", Order: 50),
        new("tools.branches-group", "D_Menu_Multi", Glyph: "", ParentId: "tools", Order: 20),
        new("tools.branches", "BR_Title", ParentId: "tools.branches-group", RequiredPermission: "System.Branches.View", Route: "branches", Order: 10),
        new("tools.branch-comparison", "BR_Comparison", ParentId: "tools.branches-group", RequiredPermission: "Ledger.Reports.View", Route: "branch-comparison", Order: 20),
        new("tools.inter-company", "IC_Title", ParentId: "tools.branches-group", RequiredPermission: "Ledger.Reports.View", Route: "inter-company", Order: 30),
        new("tools.reports-group", "D_Menu_Reports", Glyph: "", ParentId: "tools", Order: 30),
        new("tools.reports-center", "RC_Title", ParentId: "tools.reports-group", RequiredPermission: "Ledger.Reports.View", Route: "reports-center", Order: 10),
        new("tools.analytical-report", "RC_Analytical", ParentId: "tools.reports-group", RequiredPermission: "Ledger.Reports.View", Route: "analytical-report", Order: 20),
        new("tools.preferences", "UI_Preferences", Glyph: "", ParentId: "tools", Command: "preferences", Order: 40, SeparatorBefore: true),
        new("tools.report-options", "REP_Title", Glyph: "\uE9D2", ParentId: "tools", Command: "report-options", Order: 45),
        new("tools.language", "UX_Language", Glyph: "", ParentId: "tools", Command: "language", Order: 50),
        new("tools.theme", "D_Menu_Theme", Glyph: "", ParentId: "tools", Command: "theme", Order: 60),
        new("tools.skin", "D_Menu_Skin", ParentId: "tools", Command: SkinListCommand, Order: 70),

        // ───────── 12. تعليمات (لا توجد صفحة بعد: لا تظهر) ─────────
        new("help", "MN_Help", Glyph: "", IsImplemented: false, Order: 110),
        new("help.shortcuts", "D_KeyboardShortcuts", Glyph: "", ParentId: "help", IsImplemented: false, Order: 10),
    ];

    /// <summary>Returns root menu categories in display order.</summary>
    public static IEnumerable<MenuNode> RootGroups => AllNodes.Where(n => n.ParentId is null).OrderBy(n => n.Order);

    /// <summary>Returns child nodes for a parent in display order, optionally filtering out uncompleted features.</summary>
    public static IEnumerable<MenuNode> GetChildren(string parentId, bool implementedOnly = false) =>
        AllNodes.Where(n => n.ParentId == parentId && (!implementedOnly || n.IsImplemented)).OrderBy(n => n.Order);

    /// <summary>Returns authorized, implemented children for the given permissions and capability.</summary>
    public static IEnumerable<MenuNode> GetVisibleChildren(string parentId, IReadOnlyCollection<string> permissions, string capability = "Desktop") =>
        AllNodes.Where(n => n.ParentId == parentId && n.IsVisible(permissions, capability)).OrderBy(n => n.Order);

    /// <summary>
    /// Resolves the registry for one user: keeps implemented, authorized nodes whose route the navigation allows, localizes captions
    /// and drops every group that ends up without a visible child.
    /// </summary>
    /// <param name="permissions">Granted permission names.</param>
    /// <param name="routeAllowed">True when the route is a real, permitted destination (<see cref="NavigationService.Allows"/>).</param>
    /// <param name="label">Localizes a caption key.</param>
    /// <param name="commandAllowed">Optional gate for shell commands (for example "new-journal" needs the journal manage permission).</param>
    /// <param name="capability">Capability of the running product ("Desktop"; a POS module would add "Pos").</param>
    public static IReadOnlyList<TopMenuNode> BuildTree(IReadOnlyCollection<string> permissions, Func<string, bool> routeAllowed,
        Func<string, string> label, Func<string, bool>? commandAllowed = null, string capability = "Desktop")
    {
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(routeAllowed);
        ArgumentNullException.ThrowIfNull(label);
        return AllNodes.Where(n => n.ParentId is null).OrderBy(n => n.Order)
            .Select(n => Resolve(n, permissions, routeAllowed, label, commandAllowed, capability))
            .OfType<TopMenuNode>().ToArray();
    }

    private static TopMenuNode? Resolve(MenuNode node, IReadOnlyCollection<string> permissions, Func<string, bool> routeAllowed,
        Func<string, string> label, Func<string, bool>? commandAllowed, string capability)
    {
        if (!node.IsVisible(permissions, capability)) return null;
        if (node.Route is not null)
            return routeAllowed(node.Route) ? new(node.Id, label(node.TitleKey), node.Glyph, node.Shortcut, node.Route, null, node.SeparatorBefore, []) : null;
        if (node.Command is not null)
            return commandAllowed?.Invoke(node.Command) ?? true ? new(node.Id, label(node.TitleKey), node.Glyph, node.Shortcut, null, node.Command, node.SeparatorBefore, []) : null;

        var children = AllNodes.Where(n => n.ParentId == node.Id).OrderBy(n => n.Order)
            .Select(n => Resolve(n, permissions, routeAllowed, label, commandAllowed, capability)).OfType<TopMenuNode>().ToArray();
        return children.Length == 0 ? null : new(node.Id, label(node.TitleKey), node.Glyph, node.Shortcut, null, null, node.SeparatorBefore, children);
    }
}
