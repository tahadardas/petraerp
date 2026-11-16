using PetraERP.Desktop.Reporting;
using PetraERP.Reporting.OpenSource;

namespace PetraERP.Desktop.Tests;

public sealed class PetraReportSummaryManagerTests
{
    [Fact]
    public void SummaryManagerCalculatesCustomMetricsWithoutAlteringAuthoritativeTotals()
    {
        var document = CreateDocument();
        var manager = new PetraReportSummaryManager();

        manager.AddSummary("Debit", "مدين", PetraSummaryKind.Sum);
        manager.AddSummary("Debit", "مدين", PetraSummaryKind.Average);
        manager.AddSummary("Debit", "مدين", PetraSummaryKind.Min);
        manager.AddSummary("Debit", "مدين", PetraSummaryKind.Max);
        manager.AddSummary("Description", "البيان", PetraSummaryKind.Count);
        manager.AddSummary("Description", "البيان", PetraSummaryKind.DistinctCount);

        var results = manager.Calculate(document, document.Rows);

        Assert.Equal(6, results.Count);

        // Sum: 1000 + 2000 + 3000 = 6,000.00
        Assert.Equal("6,000.00", results[0].Value);
        Assert.Equal("SYP", results[0].Currency);

        // Average: 2,000.00
        Assert.Equal("2,000.00", results[1].Value);

        // Min: 1,000.00
        Assert.Equal("1,000.00", results[2].Value);

        // Max: 3,000.00
        Assert.Equal("3,000.00", results[3].Value);

        // Count: 3
        Assert.Equal("3", results[4].Value);

        // DistinctCount: 3
        Assert.Equal("3", results[5].Value);

        // Authoritative summary remains exactly 99,999.00
        Assert.Single(document.Summaries);
        Assert.Equal(99999m, document.Summaries[0].Value);
    }

    [Fact]
    public void SummaryManagerRemovesAndClearsSummaries()
    {
        var manager = new PetraReportSummaryManager();
        manager.AddSummary("Debit", "مدين", PetraSummaryKind.Sum);
        manager.AddSummary("Credit", "دائن", PetraSummaryKind.Sum);
        Assert.Equal(2, manager.ConfiguredSummaries.Count);

        manager.RemoveSummary(manager.ConfiguredSummaries[0]);
        Assert.Single(manager.ConfiguredSummaries);
        Assert.Equal("Credit", manager.ConfiguredSummaries[0].ColumnKey);

        manager.Clear();
        Assert.Empty(manager.ConfiguredSummaries);
    }

    private static ReportDocument CreateDocument() => new(
        "Ledger", "دفتر الأستاذ", "شركة الأمل", "2026", "SYP",
        [
            new ReportColumn("Date", "التاريخ", ReportValueKind.Date),
            new ReportColumn("Description", "البيان"),
            new ReportColumn("Debit", "مدين", ReportValueKind.Amount, 2),
            new ReportColumn("Credit", "دائن", ReportValueKind.Amount, 2),
        ],
        [
            new Dictionary<string, object?> { ["Date"] = "2026-01-01", ["Description"] = "حركة 1", ["Debit"] = 1000m, ["Credit"] = 0m },
            new Dictionary<string, object?> { ["Date"] = "2026-01-02", ["Description"] = "حركة 2", ["Debit"] = 2000m, ["Credit"] = 0m },
            new Dictionary<string, object?> { ["Date"] = "2026-01-03", ["Description"] = "حركة 3", ["Debit"] = 3000m, ["Credit"] = 0m },
        ],
        summaries: [new ReportSummaryValue("Closing", "الرصيد الفعلي", 99999m, "SYP")]);
}
