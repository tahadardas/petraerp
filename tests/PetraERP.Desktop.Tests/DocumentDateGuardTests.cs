using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Tests;

/// <summary>The Al-Ameen date rules: outside the working year is refused, earlier than today needs confirmation.</summary>
public sealed class DocumentDateGuardTests
{
    private static readonly DateOnly Start = new(2026, 1, 1);
    private static readonly DateOnly End = new(2026, 12, 31);
    private static readonly DateOnly Today = new(2026, 10, 31);

    [Theory]
    [InlineData("2026-10-31", DocumentDateIssue.None)]
    [InlineData("2026-11-15", DocumentDateIssue.None)]
    [InlineData("2026-10-30", DocumentDateIssue.EarlierThanToday)]
    [InlineData("2026-01-01", DocumentDateIssue.EarlierThanToday)]
    [InlineData("2025-12-31", DocumentDateIssue.BeforeFiscalYear)]
    [InlineData("2027-01-01", DocumentDateIssue.AfterFiscalYear)]
    public void EvaluatesTheDateAgainstTheYearAndToday(string text, DocumentDateIssue expected) =>
        Assert.Equal(expected, DocumentDateGuard.Evaluate(DateOnly.Parse(text, System.Globalization.CultureInfo.InvariantCulture), Today, Start, End));

    [Fact]
    public void WithoutAKnownYearOnlyThePastDateRuleApplies()
    {
        Assert.Equal(DocumentDateIssue.None, DocumentDateGuard.Evaluate(new DateOnly(2030, 5, 5), Today, null, null));
        Assert.Equal(DocumentDateIssue.EarlierThanToday, DocumentDateGuard.Evaluate(new DateOnly(2020, 5, 5), Today, null, null));
    }
}
