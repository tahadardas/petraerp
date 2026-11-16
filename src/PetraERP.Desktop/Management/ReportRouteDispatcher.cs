using PetraERP.ApiClient;
using PetraERP.Desktop.Reports;

namespace PetraERP.Desktop.Management;

/// <summary>A desktop destination for a catalogue report: the navigation route and the optional page parameter.</summary>
public sealed record ReportDestination(string Route, object? Parameter = null);

/// <summary>
/// Maps the API route recorded in the report catalogue to the desktop page that actually shows it. Only destinations that exist as pages are
/// returned; a report whose catalogue route has no page here resolves to <c>null</c> and is shown as not openable instead of being routed to
/// an unrelated screen.
/// </summary>
public static class ReportRouteDispatcher
{
    private static readonly (string Prefix, string Route, CommercialReportTab? Tab)[] Static =
    [
        ("/ledger/chart/accounts", "accounts", null),
        ("/ledger/chart/cost-centers", "cost-centers", null),
        ("/ledger/reports/trial-balance", "trial-balance", null),
        ("/ledger/reports/account-ledger", "account-ledger", null),
        ("/ledger/reports/general-journal", "general-journal", null),
        ("/reports/financial/", "financial-reports", null),
        ("/reports/parties/", "customer-statement", null),
        ("/branch-reports/trial-balance", "branch-comparison", null),
        ("/inventory/warehouses", "warehouses", null),
        ("/inventory/items", "items", null),
        ("/inventory/movements", "inventory-operations", null),
        ("/currencies/rates", "rate-bulletin", null),
        ("/currencies", "currencies", null),
        ("/reconciliation", "reconciliation", null),
        ("/audit/security-events", "audit", null),
        ("/backups", "backups", null),
        // The commercial reports page hosts these tabs; the catalogue route names the tab, not a page of its own.
        ("/reports/aging", "financial-reports", CommercialReportTab.Aging),
        ("/reports/sales-analysis", "financial-reports", CommercialReportTab.SalesAnalysis),
        ("/reports/purchase-analysis", "financial-reports", CommercialReportTab.PurchaseAnalysis),
        ("/reports/inventory/valuation", "financial-reports", CommercialReportTab.InventoryValuation),
        ("/reports/inventory/movements", "financial-reports", CommercialReportTab.InventoryMovements),
        ("/reports/cash/", "financial-reports", CommercialReportTab.CashStatement),
    ];

    /// <summary>
    /// Catalogue routes shared by two different reports, or carrying the variant in a server-side body parameter instead of the path.
    /// The catalogue id decides the page; the prefix alone would send both entries to the same one.
    /// </summary>
    private static readonly Dictionary<string, ReportDestination> ByReportId =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["ARAP-01"] = new("customer-statement"),
            ["ARAP-02"] = new("supplier-statement"),
            ["ARAP-03"] = Aging(PartyStatementKind.Customer),
            ["ARAP-04"] = Aging(PartyStatementKind.Supplier),
            // One professional executor serves several kinds; the catalogue row names the kind in its note, not in the route.
            ["GL-06"] = new("trial-balance"),
            ["FX-10"] = new("account-ledger"),
        };

    private static ReportDestination Aging(PartyStatementKind kind) =>
        new("financial-reports", new OpenCommercialReportRequest(CommercialReportTab.Aging, kind));

    public static ReportDestination? Resolve(string? catalogueRoute, string? reportId = null)
    {
        if (string.IsNullOrWhiteSpace(catalogueRoute)) return null;
        if (AnalyticalReportViewModel.TryReportId(catalogueRoute, out var analyticalId)) return new ReportDestination("analytical-report", analyticalId);
        if (!string.IsNullOrWhiteSpace(reportId) && ByReportId.TryGetValue(reportId, out var byId)) return byId;
        foreach (var (prefix, route, tab) in Static)
            if (catalogueRoute.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return new ReportDestination(route, tab is { } selected ? new OpenCommercialReportRequest(selected) : null);
        return null;
    }
}
