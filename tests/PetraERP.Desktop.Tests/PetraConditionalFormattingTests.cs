using PetraERP.Desktop.Reporting;
using PetraERP.Reporting.OpenSource;

namespace PetraERP.Desktop.Tests;

public sealed class PetraConditionalFormattingTests
{
    private static ReportDocument CreateSampleDocument()
    {
        var columns = new List<ReportColumn>
        {
            new("Narration", "البيان", ReportValueKind.Text),
            new("Debit", "مدين", ReportValueKind.Amount, 2),
            new("Credit", "دائن", ReportValueKind.Amount, 2),
            new("Date", "التاريخ", ReportValueKind.Date),
        };

        var rows = new List<IReadOnlyDictionary<string, object?>>
        {
            new Dictionary<string, object?>
            {
                ["Narration"] = "فاتورة نقدية رقم 101",
                ["Debit"] = 15000m,
                ["Credit"] = 0m,
                ["Date"] = new DateTime(2026, 1, 15),
            },
            new Dictionary<string, object?>
            {
                ["Narration"] = "سداد ذمة آجل شركة الأمل",
                ["Debit"] = 0m,
                ["Credit"] = 45000m,
                ["Date"] = new DateTime(2026, 3, 20),
            },
            new Dictionary<string, object?>
            {
                ["Narration"] = "مصاريف نثرية مكتبية",
                ["Debit"] = 2500m,
                ["Credit"] = 0m,
                ["Date"] = new DateTime(2026, 6, 10),
            },
        };

        return new ReportDocument("ledger", "دفتر الأستاذ", "شركة الاختبار", "2026", "SYP", columns, rows);
    }

    [Fact]
    public void NumericRuleMatchesCorrectly()
    {
        var doc = CreateSampleDocument();
        var debitCol = doc.Columns[1];

        var gtRule = new PetraConditionalFormatRule("Debit", "مدين", PetraFormatOperator.GreaterThan, "10000", "", PetraFormatColor.SoftGreen);
        Assert.True(gtRule.Matches(doc.Rows[0], debitCol));  // 15000 > 10000
        Assert.False(gtRule.Matches(doc.Rows[1], debitCol)); // 0 not > 10000
        Assert.False(gtRule.Matches(doc.Rows[2], debitCol)); // 2500 not > 10000

        var ltRule = new PetraConditionalFormatRule("Debit", "مدين", PetraFormatOperator.LessThan, "5000", "", PetraFormatColor.SoftRed);
        Assert.False(ltRule.Matches(doc.Rows[0], debitCol));
        Assert.True(ltRule.Matches(doc.Rows[1], debitCol));  // 0 < 5000
        Assert.True(ltRule.Matches(doc.Rows[2], debitCol));  // 2500 < 5000

        var betweenRule = new PetraConditionalFormatRule("Debit", "مدين", PetraFormatOperator.Between, "2000", "5000", PetraFormatColor.SoftYellow);
        Assert.False(betweenRule.Matches(doc.Rows[0], debitCol)); // 15000
        Assert.False(betweenRule.Matches(doc.Rows[1], debitCol)); // 0
        Assert.True(betweenRule.Matches(doc.Rows[2], debitCol));  // 2500 in [2000, 5000]
    }

    [Fact]
    public void TextRuleMatchesCorrectly()
    {
        var doc = CreateSampleDocument();
        var narrationCol = doc.Columns[0];

        var containsRule = new PetraConditionalFormatRule("Narration", "البيان", PetraFormatOperator.Contains, "نقدية", "", PetraFormatColor.SoftBlue);
        Assert.True(containsRule.Matches(doc.Rows[0], narrationCol));
        Assert.False(containsRule.Matches(doc.Rows[1], narrationCol));
        Assert.False(containsRule.Matches(doc.Rows[2], narrationCol));
    }

    [Fact]
    public void DateRuleMatchesCorrectly()
    {
        var doc = CreateSampleDocument();
        var dateCol = doc.Columns[3];

        var dateRule = new PetraConditionalFormatRule("Date", "التاريخ", PetraFormatOperator.GreaterThan, "2026-02-01", "", PetraFormatColor.SoftYellow);
        Assert.False(dateRule.Matches(doc.Rows[0], dateCol)); // Jan 15 <= Feb 01
        Assert.True(dateRule.Matches(doc.Rows[1], dateCol));  // Mar 20 > Feb 01
        Assert.True(dateRule.Matches(doc.Rows[2], dateCol));  // Jun 10 > Feb 01
    }

    [Theory]
    [InlineData(PetraFormatColor.SoftRed)]
    [InlineData(PetraFormatColor.SoftGreen)]
    [InlineData(PetraFormatColor.SoftYellow)]
    [InlineData(PetraFormatColor.SoftBlue)]
    public void PaletteColorsProvideFrozenBrushes(PetraFormatColor style)
    {
        var rule = new PetraConditionalFormatRule("Debit", "مدين", PetraFormatOperator.GreaterThan, "0", "", style);
        Assert.NotNull(rule.BackgroundBrush);
        Assert.NotNull(rule.ForegroundBrush);
        Assert.True(rule.BackgroundBrush.IsFrozen);
        Assert.True(rule.ForegroundBrush.IsFrozen);
        Assert.NotEmpty(rule.DisplayText);
    }
}
