using PetraERP.Application.Reports;

namespace PetraERP.Application.Tests;

public sealed class ReportMasterCatalogTests
{
    [Fact]
    public void IdsAreUniqueAndFollowTheGroupPrefix()
    {
        var all = ReportMasterCatalog.All;
        Assert.Equal(all.Count, all.Select(e => e.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(all, e =>
        {
            Assert.StartsWith(e.Group + "-", e.Id, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(e.NameArabic));
            Assert.False(string.IsNullOrWhiteSpace(e.NameEnglish));
        });
    }

    [Fact]
    public void NothingIsDeliveredWithoutARoute()
    {
        foreach (var entry in ReportMasterCatalog.All)
        {
            if (entry.Delivery == ReportDelivery.Implemented) Assert.False(string.IsNullOrWhiteSpace(entry.Route), entry.Id);
            if (entry.Delivery == ReportDelivery.NotImplemented) Assert.Null(entry.Route);
            if (entry.Delivery != ReportDelivery.Implemented) Assert.False(string.IsNullOrWhiteSpace(entry.Note) && entry.Delivery == ReportDelivery.Partial, entry.Id);
        }
    }

    [Fact]
    public void BudgetComparisonStaysBlockedUntilAnApprovedBudgetSourceExists()
    {
        var budget = ReportMasterCatalog.Find("FIN-12");
        Assert.NotNull(budget);
        Assert.Equal(ReportDelivery.NotImplemented, budget.Delivery);
        Assert.Contains("budget", budget.Note, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EveryDocumentedGroupIsPresent()
    {
        var groups = ReportMasterCatalog.All.Select(e => e.Group).Distinct().ToHashSet(StringComparer.Ordinal);
        Assert.Superset(new HashSet<string> { "GL", "FIN", "FX", "ARAP", "CASH", "INV", "SALES", "BUY", "CC", "SYSTEM" }, groups);
        Assert.Equal(13, ReportMasterCatalog.All.Count(e => e.Group == "GL"));
        Assert.Equal(17, ReportMasterCatalog.All.Count(e => e.Group == "INV"));
    }
}
