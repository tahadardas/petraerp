using PetraERP.Desktop.Reporting;
using PetraERP.Reporting.OpenSource;

namespace PetraERP.Desktop.Tests;

public sealed class PetraReportFilterEngineTests
{
    [Fact]
    public void SearchNormalizesArabicAndCanTargetOneTypedColumn()
    {
        var document = Document();
        var arabic = PetraReportFilterEngine.Search(document, document.Rows, new PetraReportSearchOptions("اسمنت"));
        Assert.Single(arabic);
        Assert.Equal("Description", arabic[0].ColumnKey);

        var amount = PetraReportFilterEngine.Search(document, document.Rows, new PetraReportSearchOptions("1,250.50", "Debit", WholeCell: true));
        Assert.Single(amount);
        Assert.Equal(document.Rows[0], amount[0].Row);

        var wrongColumn = PetraReportFilterEngine.Search(document, document.Rows, new PetraReportSearchOptions("اسمنت", "Debit"));
        Assert.Empty(wrongColumn);
    }

    [Fact]
    public void NestedTypedFiltersDoNotMutateAuthoritativeSummaries()
    {
        var document = Document();
        var filter = new PetraReportFilterGroup(PetraReportFilterLogic.And,
        [
            new PetraReportFilterCondition("Date", PetraReportFilterOperator.Between, "2026-01-01", "2026-01-31"),
            new PetraReportFilterGroup(PetraReportFilterLogic.Or,
            [
                new PetraReportFilterCondition("Description", PetraReportFilterOperator.Contains, "إسمنت"),
                new PetraReportFilterCondition("Debit", PetraReportFilterOperator.GreaterThanOrEqual, "1000"),
            ]),
        ]);

        var result = PetraReportFilterEngine.Apply(document, document.Rows, filter);
        Assert.Single(result);
        Assert.Equal(9999m, document.Summaries[0].Value);
        Assert.Equal(3, document.Rows.Count);
    }

    [Fact]
    public void FilterValidationRejectsUnknownFieldsInvalidTypesAndInvalidOperators()
    {
        var document = Document();
        Assert.False(PetraReportFilterEngine.Validate(document,
            new PetraReportFilterCondition("Missing", PetraReportFilterOperator.Equals, "1")).IsValid);
        Assert.False(PetraReportFilterEngine.Validate(document,
            new PetraReportFilterCondition("Debit", PetraReportFilterOperator.GreaterThan, "not-a-number")).IsValid);
        Assert.False(PetraReportFilterEngine.Validate(document,
            new PetraReportFilterCondition("Description", PetraReportFilterOperator.Between, "a", "z")).IsValid);
    }

    [Fact]
    public void InAndNotInOperatorsWorkForTextAndAmounts()
    {
        var document = Document();
        var inFilter = new PetraReportFilterCondition("Debit", PetraReportFilterOperator.In, Values: ["100", "250"]);
        var inRows = PetraReportFilterEngine.Apply(document, document.Rows, inFilter);
        Assert.Equal(2, inRows.Count);

        var notInFilter = new PetraReportFilterCondition("Debit", PetraReportFilterOperator.NotIn, Values: ["100", "250"]);
        var notInRows = PetraReportFilterEngine.Apply(document, document.Rows, notInFilter);
        Assert.Single(notInRows);
        Assert.Equal(1250.50m, notInRows[0]["Debit"]);

        var textInFilter = new PetraReportFilterCondition("Description", PetraReportFilterOperator.In, Values: ["رمل", "غير موجود"]);
        var textInRows = PetraReportFilterEngine.Apply(document, document.Rows, textInFilter);
        Assert.Single(textInRows);
        Assert.Equal("رمل", textInRows[0]["Description"]);
    }

    [Fact]
    public void FormatExpressionRendersNestedParenthesesAndOperators()
    {
        var filter = new PetraReportFilterGroup(PetraReportFilterLogic.And,
        [
            new PetraReportFilterCondition("Date", PetraReportFilterOperator.GreaterThanOrEqual, "2026-01-01"),
            new PetraReportFilterGroup(PetraReportFilterLogic.Or,
            [
                new PetraReportFilterCondition("Debit", PetraReportFilterOperator.GreaterThan, "500"),
                new PetraReportFilterCondition("Description", PetraReportFilterOperator.Contains, "اسمنت"),
            ]),
        ]);

        var formatted = PetraReportFilterEngine.FormatExpression(filter);
        Assert.Equal("([Date] >= '2026-01-01' AND ([Debit] > '500' OR [Description] يحوي 'اسمنت'))", formatted);
    }

    [Fact]
    public void FilterConverterMapsBetweenDesktopNodeAndApiContractRoundTrip()
    {
        var filter = new PetraReportFilterGroup(PetraReportFilterLogic.And,
        [
            new PetraReportFilterCondition("Date", PetraReportFilterOperator.GreaterThanOrEqual, "2026-01-01"),
            new PetraReportFilterCondition("Debit", PetraReportFilterOperator.In, Values: ["100", "200"]),
        ]);

        var contract = PetraReportFilterConverter.ToContract(filter);
        Assert.NotNull(contract);
        Assert.Equal(2, contract.Conditions?.Count);

        var restored = PetraReportFilterConverter.FromContract(contract);
        Assert.NotNull(restored);
        var restoredGroup = Assert.IsType<PetraReportFilterGroup>(restored);
        Assert.Equal(2, restoredGroup.Children.Count);
    }

    private static ReportDocument Document() => new(
        "Ledger", "دفتر الأستاذ", "شركة", "2026", "SYP",
        [
            new ReportColumn("Date", "التاريخ", ReportValueKind.Date),
            new ReportColumn("Description", "البيان"),
            new ReportColumn("Debit", "مدين", ReportValueKind.Amount, 2),
        ],
        [
            Row("2026-01-15", "إِسْمَنْت أبيض", 1250.50m),
            Row("2026-02-01", "رمل", 100m),
            Row("2026-03-01", "Cement", 250m),
        ],
        summaries: [new ReportSummaryValue("ActualClosing", "الرصيد الفعلي", 9999m, "SYP")]);

    private static Dictionary<string, object?> Row(string date, string description, decimal debit) =>
        new Dictionary<string, object?> { ["Date"] = date, ["Description"] = description, ["Debit"] = debit };
}
