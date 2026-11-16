namespace PetraERP.Application.Reports;

/// <summary>How far a catalogued report is delivered. Nothing is marked delivered without a route that serves it.</summary>
public enum ReportDelivery : byte
{
    /// <summary>Not built; no route. Listed so the gap is visible instead of silently missing.</summary>
    NotImplemented = 0,
    /// <summary>Served by an existing route that covers part of the definition (see <see cref="ReportCatalogEntry.Note"/>).</summary>
    Partial = 1,
    /// <summary>Served by a route that covers the definition.</summary>
    Implemented = 2,
}

/// <param name="Id">Stable catalogue id, for example GL-05.</param>
/// <param name="Group">Id prefix: GL, FIN, FX, ARAP, CASH, INV, SALES, BUY, CC or SYSTEM.</param>
/// <param name="Route">Path relative to /api/v1/companies/{companyId}/branches/{branchId}; null when not delivered.</param>
/// <param name="Permission">Permission the route enforces (in addition to scope).</param>
/// <param name="Note">What is missing for Partial, or the blocking dependency for NotImplemented.</param>
public sealed record ReportCatalogEntry(string Id, string Group, string NameArabic, string NameEnglish, ReportDelivery Delivery, string? Route, string Permission, string? Note);

/// <summary>
/// The master list of accounting and inventory reports. Each report keeps its own identity and filter contract even when
/// several share one data source; this list is the single place that states which of them exist today.
/// </summary>
public static class ReportMasterCatalog
{
    private const string View = "Ledger.Reports.View";

    private static ReportCatalogEntry E(string id, string ar, string en, ReportDelivery d = ReportDelivery.NotImplemented, string? route = null, string? note = null) =>
        new(id, id[..id.IndexOf('-', StringComparison.Ordinal)], ar, en, d, route, View, note);

    private const ReportDelivery Done = ReportDelivery.Implemented;
    private const ReportDelivery Part = ReportDelivery.Partial;

    public static IReadOnlyList<ReportCatalogEntry> All { get; } =
    [
        E("GL-01", "دليل الحسابات", "Chart of accounts", Done, "/ledger/chart/accounts/tree"),
        E("GL-02", "دفتر اليومية", "General journal", Done, "/ledger/reports/general-journal"),
        E("GL-03", "دفتر الأستاذ لحساب واحد ومتعدد", "Account ledger (single and multiple)", Part, "/ledger/reports/account-ledger/{accountId}", "Single account only; a multi-account selection runs through the professional report executor."),
        E("GL-04", "كشف حركة حساب", "Account activity statement", Done, "/ledger/reports/account-ledger/{accountId}"),
        E("GL-05", "ميزان مراجعة", "Trial balance", Done, "/ledger/reports/trial-balance"),
        E("GL-06", "ميزان مراجعة افتتاح/حركة/إقفال", "Opening / movement / closing trial balance", Done, "/professional-reports/execute", "Kind = TrialBalance."),
        E("GL-07", "أرصدة الحسابات", "Account balances", Part, "/ledger/reports/trial-balance", "Balances come from the trial balance; no standalone balances-at-date report."),
        E("GL-08", "حسابات راكدة", "Dormant accounts", Done, "/reports/analytical/GL-08"),
        E("GL-09", "حسابات تجاوزت الحد", "Accounts over limit"),
        E("GL-10", "التسويات والعكس", "Adjustments and reversals", Done, "/reports/analytical/GL-10", "Reversal entries and their originals in the period."),
        E("GL-11", "قيود معدّلة/مرفوضة", "Modified / rejected entries", Done, "/reports/analytical/GL-11", "Reversed, reversing and correcting entries; pre-post rejection is not stored."),
        E("GL-12", "قيود غير مرحّلة", "Unposted entries", Done, "/reports/analytical/GL-12"),
        E("GL-13", "حسابات غير مربوطة بمخطط ترحيل", "Accounts without a posting map", Done, "/reports/analytical/GL-13", "Dated branch mapping snapshot including unmapped roles; reproducible by date (no transactional outbox exists)."),

        E("FIN-01", "الأرباح والخسائر", "Income statement", Done, "/reports/financial/income-statement"),
        E("FIN-02", "الميزانية العمومية", "Balance sheet", Done, "/reports/financial/balance-sheet"),
        E("FIN-03", "قائمة التدفقات النقدية مباشر", "Cash flow (direct)", Part, "/reports/analytical/FIN-03", "Classified by counter-account type (equity = financing, otherwise operating); no per-account investing class."),
        E("FIN-04", "التدفقات النقدية غير مباشر", "Cash flow (indirect)", Part, "/reports/analytical/FIN-04", "Net income adjusted by balance-sheet movements; fixed assets are not split out as investing."),
        E("FIN-05", "المتاجرة", "Trading account", Done, "/reports/analytical/FIN-05", "Role-mapped sales/returns/COGS from posted journals; purchases informational."),
        E("FIN-06", "التشغيل", "Operating account", Done, "/reports/analytical/FIN-06", "Revenue and expense accounts outside trading roles."),
        E("FIN-07", "حركة حقوق الملكية", "Statement of changes in equity", Done, "/reports/analytical/FIN-07", "Posted equity accounts opening/additions/reductions plus unclosed period result."),
        E("FIN-08", "تحليل إيرادات/مصاريف شهري", "Monthly revenue / expense analysis", Done, "/reports/analytical/FIN-08"),
        E("FIN-09", "نتائج فرع", "Branch results", Done, "/reports/analytical/FIN-09"),
        E("FIN-10", "مقارنة فروع", "Branch comparison", Part, "/branch-reports/trial-balance", "Side-by-side branch columns with optional inter-branch elimination."),
        E("FIN-11", "مقارنة سنوات", "Year comparison"),
        E("FIN-12", "مقارنة فترات وموازنة تقديرية", "Period and budget comparison", ReportDelivery.NotImplemented, null, "Blocked: no approved budget source exists; numbers are never invented."),

        E("FX-01", "بطاقة العملات", "Currency card", Done, "/currencies"),
        E("FX-02", "أسعار الصرف التاريخية", "Historical exchange rates", Done, "/currencies/rates"),
        E("FX-03", "نشرة أسعار العملات", "Rate bulletin", Part, "/currencies/rates", "Bulletins are registered atomically (POST /currencies/rates/bulletin); listing is by date filter."),
        E("FX-04", "دفتر أستاذ عملة", "Currency ledger", Done, "/reports/analytical/FX-04"),
        E("FX-05", "ميزان العملات", "Currency trial balance", Done, "/reports/analytical/FX-05"),
        E("FX-06", "أرصدة بعملتين", "Dual-currency balances", Done, "/reports/analytical/FX-06", "Balances in transaction and base currency."),
        E("FX-07", "فروقات صرف محققة", "Realized exchange differences", Done, "/reports/analytical/FX-07", "Posted movement on the FxGain/FxLoss pattern-role accounts (no narration matching)."),
        E("FX-08", "إعادة تقييم غير محققة", "Unrealized revaluation", ReportDelivery.NotImplemented, null, "Revaluation executes through the FX revaluation workflow; there is no standalone report yet."),
        E("FX-09", "حركة أسعار الصرف", "Exchange rate movement", Done, "/reports/analytical/FX-09"),
        E("FX-10", "كشف حساب بعملات", "Multi-currency account statement", Part, "/professional-reports/execute", "Transaction-currency columns are available on ledger executions."),

        E("ARAP-01", "كشف زبون", "Customer statement", Done, "/reports/parties/{partyId}/statement"),
        E("ARAP-02", "كشف مورد", "Supplier statement", Done, "/reports/parties/{partyId}/statement"),
        E("ARAP-03", "أعمار ذمم مدينة", "Receivables aging", Done, "/reports/aging"),
        E("ARAP-04", "أعمار ذمم دائنة", "Payables aging", Done, "/reports/aging"),
        E("ARAP-05", "فواتير مستحقة", "Due invoices", Done, "/reports/analytical/ARAP-05", "Open items with due dates and days late as of the To date."),
        E("ARAP-06", "دفعات غير مخصصة", "Unallocated payments", Done, "/reports/analytical/ARAP-06", "Posted vouchers minus invoice allocations, party lines and active settlement applications."),
        E("ARAP-07", "سلف ومدفوعات مقدمة", "Advances and prepayments", Done, "/reports/analytical/ARAP-07", "Unallocated posted vouchers grouped by party, kind and currency."),
        E("ARAP-08", "ملخص تحصيل شهري", "Monthly collection summary", Done, "/reports/analytical/ARAP-08", "Monthly posted receipts/payments by currency."),
        E("ARAP-09", "أعلى زبائن/موردين", "Top customers / suppliers", Part, "/reports/sales-analysis", "Sales and purchase analysis grouped by customer or supplier."),
        E("ARAP-10", "مطابقة أرصدة أطراف وGL", "Party balances vs GL reconciliation", Part, "/reconciliation", "Runs inside the multi-layer reconciliation."),
        E("ARAP-11", "حركات ربط/فك تسوية", "Settlement link / unlink movements", Done, "/reports/analytical/ARAP-11", "Settlement batches with reversal state and reasons."),

        E("CASH-01", "حركة صندوق", "Cash movement", Done, "/reports/cash/{accountId}/statement"),
        E("CASH-02", "حركة بنك", "Bank movement", Done, "/reports/cash/{accountId}/statement"),
        E("CASH-03", "أرصدة الصناديق والبنوك", "Cash and bank balances", Done, "/reports/cash/accounts"),
        E("CASH-04", "تسوية بنك", "Bank reconciliation"),
        E("CASH-05", "حركات شيكات", "Cheque movements", Done, "/reports/analytical/CASH-05", "Commercial papers by current status date (not full transition history)."),
        E("CASH-06", "مصاريف نقدية", "Cash expenses", Done, "/reports/analytical/CASH-06", "Expense lines in journals touching branch Cash/Bank accounts."),
        E("CASH-07", "كشف سندات قبض/دفع", "Receipt / payment voucher list", Done, "/reports/analytical/CASH-07", "All vouchers in period with state."),

        E("INV-01", "دليل المواد", "Item catalogue", Done, "/inventory/items"),
        E("INV-02", "دليل المستودعات", "Warehouse catalogue", Done, "/inventory/warehouses"),
        E("INV-03", "حركة مادة", "Item movement", Done, "/reports/inventory/movements/{itemId}"),
        E("INV-04", "حركة مستودع", "Warehouse movement", Part, "/inventory/movements", "Movement list filtered by warehouse."),
        E("INV-05", "أرصدة مخزون", "Stock balances", Done, "/reports/inventory/valuation"),
        E("INV-06", "تقييم WAC", "WAC valuation", Done, "/reports/inventory/valuation"),
        E("INV-07", "تقييم FIFO", "FIFO valuation", Done, "/reports/analytical/INV-07", "Open layers; with a To date the layers are rebuilt as-of that date from the append-only allocation ledger."),
        E("INV-08", "جرد وفروقات", "Count and differences"),
        E("INV-09", "تحويلات", "Transfers", Done, "/reports/analytical/INV-09", "Transfer in/out movements."),
        E("INV-10", "مواد تحت الحد", "Items below minimum", Done, "/reports/analytical/INV-10", "Items under MinStock/ReorderPoint from movements."),
        E("INV-11", "مخزون سالب", "Negative stock", Done, "/reports/analytical/INV-11"),
        E("INV-12", "تكلفة بضاعة مباعة", "Cost of goods sold", Done, "/reports/analytical/INV-12", "Sales delivery and return movements valued at cost."),
        E("INV-13", "مواد راكدة", "Slow-moving items", Done, "/reports/analytical/INV-13"),
        E("INV-14", "دوران المخزون", "Inventory turnover", Done, "/reports/analytical/INV-14", "Quantity turnover from stock movements (not value based)."),
        E("INV-15", "تطابق GL مع المخزون", "GL to inventory match", Part, "/reconciliation", "Runs inside the multi-layer reconciliation."),
        E("INV-16", "تغير أسعار المواد", "Item price changes", Done, "/reports/analytical/INV-16", "Item price records created in period."),
        E("INV-17", "أرصدة وحدات بديلة", "Alternate-unit balances"),

        E("SALES-01", "مبيعات يومية", "Daily sales", Done, "/reports/analytical/SALES-01", "Daily role-mapped sales and returns."),
        E("SALES-02", "مبيعات شهرية", "Monthly sales", Done, "/reports/analytical/SALES-02"),
        E("SALES-03", "مبيعات بالفرع", "Sales by branch", Done, "/reports/analytical/SALES-03"),
        E("SALES-04", "مبيعات بنمط الفاتورة", "Sales by invoice pattern", Done, "/reports/sales-analysis"),
        E("SALES-05", "مبيعات بالزبون", "Sales by customer", Done, "/reports/sales-analysis"),
        E("SALES-06", "مبيعات بالمادة", "Sales by item", Done, "/reports/sales-analysis"),
        E("SALES-07", "مردودات وخصومات", "Returns and discounts", Done, "/reports/analytical/SALES-07", "Monthly sales returns from role accounts (no separate discount account exists)."),
        E("SALES-08", "تكاليف وربح فاتورة", "Invoice cost and profit"),
        E("SALES-09", "أرباح مواد", "Item profit"),
        E("SALES-10", "هامش ربح فرع", "Branch margin", Done, "/reports/analytical/SALES-10", "Per-branch net sales vs COGS role accounts."),
        E("SALES-11", "فواتير معلقة", "Pending invoices", Done, "/reports/analytical/SALES-11", "Draft/approved unposted sales invoices."),
        E("SALES-12", "مبيعات حسب العملة", "Sales by currency", Done, "/reports/analytical/SALES-12", "Posted sales grouped by document currency."),

        E("BUY-01", "مشتريات يومية", "Daily purchases", Done, "/reports/analytical/BUY-01", "Daily role-mapped purchases and returns."),
        E("BUY-02", "مشتريات شهرية", "Monthly purchases", Done, "/reports/analytical/BUY-02"),
        E("BUY-03", "مشتريات حسب المورد", "Purchases by supplier", Done, "/reports/purchase-analysis"),
        E("BUY-04", "مواد مشتراة", "Purchased items", Done, "/reports/purchase-analysis"),
        E("BUY-05", "مردودات", "Purchase returns", Done, "/reports/analytical/BUY-05", "Monthly purchase returns from role accounts."),
        E("BUY-06", "تكلفة مشتريات وشحن", "Purchase and freight cost"),
        E("BUY-07", "فواتير مشتريات معلقة", "Pending purchase invoices", Done, "/reports/analytical/BUY-07", "Draft/approved unposted purchase invoices."),
        E("BUY-08", "تحليل أسعار موردين", "Supplier price analysis", Done, "/reports/analytical/BUY-08", "Posted purchase cost min/max/weighted average per item and supplier; document currency."),

        E("CC-01", "دليل المراكز", "Cost center catalogue", Done, "/ledger/chart/cost-centers/tree"),
        E("CC-02", "حركة مركز", "Cost center movement", Part, "/ledger/chart/cost-centers/{costCenterId}/allocations", "Allocations of one center."),
        E("CC-03", "ميزان مركز", "Cost center balance", Done, "/reports/analytical/CC-03", "Posted lines carrying a cost centre, debit/credit/balance per centre."),
        E("CC-04", "تقرير الربحية حسب مركز/فرع", "Profitability by center / branch", Done, "/reports/analytical/CC-04", "Revenue vs expense per cost centre."),
        E("CC-05", "توزيع التكاليف", "Cost allocation"),
        E("CC-06", "مقارنة مراكز", "Cost center comparison", Done, "/reports/analytical/CC-06", "Cost centre by branch revenue/expense."),
        E("CC-07", "مصروفات غير مخصصة", "Unallocated expenses", Done, "/reports/analytical/CC-07", "Revenue/expense lines without a cost centre."),
        E("CC-08", "مراكز الأكثر حركة", "Most active centers", Done, "/reports/analytical/CC-08", "Top 20 centres by posted line count."),

        E("SYSTEM-01", "صلاحيات فعلية", "Effective permissions"),
        E("SYSTEM-02", "سجل التدقيق", "Audit log", Part, "/audit/security-events", "Security events; document-number and branch audits have their own routes."),
        E("SYSTEM-03", "أحداث الترحيل والعكس", "Posting and reversal events"),
        E("SYSTEM-04", "قاعدة/سنة مصدر ومقارنة افتتاح", "Source year and opening comparison"),
        E("SYSTEM-05", "نسخ احتياطي/استعادة", "Backup / restore", Done, "/backups", "Administration route: /api/v1/administration/backups (not company-scoped)."),
        E("SYSTEM-06", "تقرير سلامة البيانات", "Data integrity report", Done, "/reconciliation"),
        E("SYSTEM-07", "اختبارات غياب مصادر التقرير", "Missing report source tests"),
    ];

    public static ReportCatalogEntry? Find(string id) => All.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.Ordinal));
}
