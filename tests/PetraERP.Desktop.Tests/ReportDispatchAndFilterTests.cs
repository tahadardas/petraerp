using PetraERP.ApiClient;
using PetraERP.Desktop.Management;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Tests;

public sealed class ReportDispatchAndFilterTests
{
    [Theory]
    [InlineData("/reports/analytical/FX-07", "analytical-report")]
    [InlineData("/ledger/reports/trial-balance", "trial-balance")]
    [InlineData("/ledger/reports/account-ledger/{accountId}", "account-ledger")]
    [InlineData("/ledger/chart/cost-centers/tree", "cost-centers")]
    [InlineData("/inventory/warehouses", "warehouses")]
    [InlineData("/currencies/rates", "rate-bulletin")]
    [InlineData("/reconciliation", "reconciliation")]
    public void KnownCatalogueRoutesResolveToExistingPages(string catalogueRoute, string expected)
    {
        var destination = ReportRouteDispatcher.Resolve(catalogueRoute);
        Assert.NotNull(destination);
        Assert.Equal(expected, destination.Route);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/something/unknown")]
    public void UnknownRoutesAreNotRoutedToUnrelatedPages(string? catalogueRoute) => Assert.Null(ReportRouteDispatcher.Resolve(catalogueRoute));

    /// <summary>
    /// Reports the server already implements but whose catalogue route names a tab or a body parameter instead of a page must still open
    /// something real; an entry that resolves to null is shown to the operator as unopenable.
    /// </summary>
    [Theory]
    [InlineData("/reports/aging", "ARAP-03", "financial-reports")]
    [InlineData("/reports/sales-analysis", "SALES-05", "financial-reports")]
    [InlineData("/reports/purchase-analysis", "BUY-03", "financial-reports")]
    [InlineData("/reports/inventory/valuation", "INV-05", "financial-reports")]
    [InlineData("/reports/inventory/movements/{itemId}", "INV-03", "financial-reports")]
    [InlineData("/reports/cash/{accountId}/statement", "CASH-01", "financial-reports")]
    [InlineData("/professional-reports/execute", "GL-06", "trial-balance")]
    [InlineData("/professional-reports/execute", "FX-10", "account-ledger")]
    [InlineData("/reports/parties/{partyId}/statement", "ARAP-01", "customer-statement")]
    [InlineData("/reports/parties/{partyId}/statement", "ARAP-02", "supplier-statement")]
    [InlineData("/backups", "SYSTEM-05", "backups")]
    public void ImplementedReportsOpenThePageThatShowsThem(string catalogueRoute, string reportId, string expected)
    {
        var destination = ReportRouteDispatcher.Resolve(catalogueRoute, reportId);
        Assert.NotNull(destination);
        Assert.Equal(expected, destination.Route);
    }

    [Fact]
    public void AgingEntriesOpenTheirOwnSideOfTheAgingTab()
    {
        var receivables = ReportRouteDispatcher.Resolve("/reports/aging", "ARAP-03");
        var payables = ReportRouteDispatcher.Resolve("/reports/aging", "ARAP-04");
        var request = Assert.IsType<PetraERP.Desktop.Reports.OpenCommercialReportRequest>(receivables!.Parameter);
        Assert.Equal(PetraERP.Desktop.Reports.CommercialReportTab.Aging, request.Tab);
        Assert.Equal(PartyStatementKind.Customer, request.PartyKind);
        Assert.Equal(PartyStatementKind.Supplier, Assert.IsType<PetraERP.Desktop.Reports.OpenCommercialReportRequest>(payables!.Parameter).PartyKind);
    }

    [Fact]
    public void InventoryAndCashEntriesSelectTheirOwnTab()
    {
        Assert.Equal(PetraERP.Desktop.Reports.CommercialReportTab.InventoryValuation,
            Assert.IsType<PetraERP.Desktop.Reports.OpenCommercialReportRequest>(
                ReportRouteDispatcher.Resolve("/reports/inventory/valuation", "INV-06")!.Parameter).Tab);
        Assert.Equal(PetraERP.Desktop.Reports.CommercialReportTab.CashStatement,
            Assert.IsType<PetraERP.Desktop.Reports.OpenCommercialReportRequest>(
                ReportRouteDispatcher.Resolve("/reports/cash/accounts", "CASH-03")!.Parameter).Tab);
    }

    [Fact]
    public void AnalyticalReportSavedFilterReopensTheSameReportWithItsCriteria()
    {
        var account = Guid.NewGuid();
        var filter = new SavedFilter(Guid.NewGuid(), "x", "analytical-report", Guid.NewGuid(), new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31), account, ReportId: "FX-04", CurrencyCode: "EUR");
        var request = Assert.IsType<OpenAnalyticalReportRequest>(filter.ToRequest());
        Assert.Equal("FX-04", request.ReportId);
        Assert.Equal("EUR", request.CurrencyCode);
        Assert.Equal(account, request.AccountId);
        Assert.Equal(new DateOnly(2026, 3, 31), request.To);
        Assert.Null(new SavedFilter(Guid.NewGuid(), "x", "analytical-report", Guid.NewGuid()).ToRequest()); // no report id: nothing to reopen
    }

    [Fact]
    public void AnalyticalGridKeyIsServerWhitelisted() =>
        Assert.True(PetraERP.Application.Productivity.WorkspaceRules.GridColumns.ContainsKey("reports.analytical"));
}
