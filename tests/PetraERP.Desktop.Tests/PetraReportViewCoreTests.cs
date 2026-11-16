using System.ComponentModel;
using System.Windows.Threading;
using PetraERP.Desktop.Reporting;
using PetraERP.Reporting.OpenSource;

namespace PetraERP.Desktop.Tests;

/// <summary>P03 (defects D-P03-*): raw-value distinct grouping, bounded searchable value list, stable off-thread sort.</summary>
public sealed class PetraReportViewCoreTests
{
    private static readonly ReportColumn Amount = new("Debit", "مدين", ReportValueKind.Amount, 2);
    private static readonly ReportColumn Name = new("Name", "البيان");
    private static readonly ReportColumn Day = new("Date", "التاريخ", ReportValueKind.Date);

    private static Dictionary<string, object?> Row(object? debit, string name = "x", string date = "2026-01-01") =>
        new() { ["Debit"] = debit, ["Name"] = name, ["Date"] = date };

    internal static ReportDocument Doc(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows) =>
        new("T", "T", "C", "2026", "USD", [Day, Name, Amount], rows);

    private static IReadOnlyDictionary<string, object?>[] Rows(params object?[] debits) =>
        debits.Select(value => (IReadOnlyDictionary<string, object?>)Row(value)).ToArray();

    [Fact]
    public void RawValuesThatRenderIdenticallyStaySeparateAndFilterExactly()
    {
        var document = Doc(Rows(1.231m, 1.234m, 1.231m, 2m));
        Assert.Equal("1.23", ReportGridCellConverter.Format(1.231m, Amount));
        Assert.Equal("1.23", ReportGridCellConverter.Format(1.234m, Amount));

        var items = PetraReportDistinctValues.Build(document, Amount, null, CancellationToken.None);
        Assert.Equal(3, items.Count);
        var a = Assert.Single(items, i => i.RawValue == "1.231");
        var b = Assert.Single(items, i => i.RawValue == "1.234");
        Assert.Equal(2, a.Count);
        Assert.Equal(1, b.Count);
        Assert.Contains("[1.231]", a.DisplayLabel);   // collision is disambiguated in the UI
        Assert.Contains("[1.234]", b.DisplayLabel);
        Assert.DoesNotContain("[", items.Single(i => i.RawValue == "2").DisplayLabel);

        var only231 = new PetraReportFilterCondition("Debit", PetraReportFilterOperator.In, Values: ["1.231"]);
        Assert.True(PetraReportFilterEngine.Validate(document, only231).IsValid);
        Assert.Equal(2, PetraReportFilterEngine.Apply(document, document.Rows, only231).Count);

        var both = new PetraReportFilterCondition("Debit", PetraReportFilterOperator.In, Values: ["1.231", "1.234"]);
        Assert.Equal(3, PetraReportFilterEngine.Apply(document, document.Rows, both).Count);

        var notIn234 = new PetraReportFilterCondition("Debit", PetraReportFilterOperator.NotIn, Values: ["1.234"]);
        Assert.Equal(3, PetraReportFilterEngine.Apply(document, document.Rows, notIn234).Count);
    }

    [Fact]
    public void SelectionRoundTripPreselectsByRawValueEvenWhenSavedWithTrailingZeros()
    {
        var document = Doc(Rows(1.231m, 1.234m, 5.50m));
        var items = PetraReportDistinctValues.Build(document, Amount, ["1.2310", "5.5"], CancellationToken.None);
        Assert.True(items.Single(i => i.RawValue == "1.231").IsSelected);
        Assert.False(items.Single(i => i.RawValue == "1.234").IsSelected);
        Assert.True(items.Single(i => i.RawValue == "5.5").IsSelected);
    }

    [Fact]
    public void BlankBucketSelectsNullAndEmptyForTypedAndTextColumns()
    {
        var document = Doc([Row(null, "a"), Row(3m, ""), Row(4m, "b")]);
        var numeric = PetraReportDistinctValues.Build(document, Amount, null, CancellationToken.None);
        Assert.Contains(numeric, i => i.RawValue.Length == 0);

        var blankNumber = new PetraReportFilterCondition("Debit", PetraReportFilterOperator.In, Values: [""]);
        Assert.True(PetraReportFilterEngine.Validate(document, blankNumber).IsValid);
        Assert.Single(PetraReportFilterEngine.Apply(document, document.Rows, blankNumber));

        var blankOrFour = new PetraReportFilterCondition("Debit", PetraReportFilterOperator.In, Values: ["", "4"]);
        Assert.Equal(2, PetraReportFilterEngine.Apply(document, document.Rows, blankOrFour).Count);

        var blankText = new PetraReportFilterCondition("Name", PetraReportFilterOperator.In, Values: [""]);
        Assert.Single(PetraReportFilterEngine.Apply(document, document.Rows, blankText));
        var notBlankText = new PetraReportFilterCondition("Name", PetraReportFilterOperator.NotIn, Values: [""]);
        Assert.Equal(2, PetraReportFilterEngine.Apply(document, document.Rows, notBlankText).Count);
    }

    [Fact]
    public void DateValuesGroupByCalendarDayAndFilterByDay()
    {
        var document = Doc([Row(1m, "a", "2026-01-01"), Row(2m, "b", "2026-01-02"), Row(3m, "c", "2026-01-01")]);
        var items = PetraReportDistinctValues.Build(document, Day, null, CancellationToken.None);
        Assert.Equal(2, items.Count);
        var in1 = new PetraReportFilterCondition("Date", PetraReportFilterOperator.In, Values: ["2026-01-01"]);
        Assert.Equal(2, PetraReportFilterEngine.Apply(document, document.Rows, in1).Count);
    }

    [Fact]
    public void SearchCoversEveryDistinctValueAndOnlyBoundsTheSliceShown()
    {
        var rows = Enumerable.Range(0, 5000).Select(i => (IReadOnlyDictionary<string, object?>)Row(i, "n" + i)).ToArray();
        var document = Doc(rows);
        var all = PetraReportDistinctValues.Build(document, Name, null, CancellationToken.None);
        Assert.Equal(5000, all.Count);

        var everything = PetraReportDistinctValues.Search(all, "", PetraReportDistinctValues.VisibleLimit, CancellationToken.None);
        Assert.Equal(5000, everything.Matches.Count);
        Assert.Equal(PetraReportDistinctValues.VisibleLimit, everything.Shown.Count);

        // a value far beyond the first 500 rows of the list is still found
        var hit = PetraReportDistinctValues.Search(all, "n4999", PetraReportDistinctValues.VisibleLimit, CancellationToken.None);
        Assert.Single(hit.Matches);
        Assert.Equal("n4999", hit.Shown[0].RawValue);

        var many = PetraReportDistinctValues.Search(all, "n49", PetraReportDistinctValues.VisibleLimit, CancellationToken.None);
        Assert.Equal(111, many.Matches.Count);          // n49, n490-n499, n4900-n4999
        Assert.Equal(111, many.Shown.Count);

        var broad = PetraReportDistinctValues.Search(all, "n", PetraReportDistinctValues.VisibleLimit, CancellationToken.None);
        Assert.Equal(5000, broad.Matches.Count);        // select-all acts on all matches, not the slice
        Assert.Equal(PetraReportDistinctValues.VisibleLimit, broad.Shown.Count);
    }

    [Fact]
    public void SearchMatchesRawValueOfCollidingAmounts()
    {
        var document = Doc(Rows(1.231m, 1.234m));
        var all = PetraReportDistinctValues.Build(document, Amount, null, CancellationToken.None);
        Assert.Single(PetraReportDistinctValues.Search(all, "1.234", 500, CancellationToken.None).Matches);
        Assert.Equal(2, PetraReportDistinctValues.Search(all, "1.23", 500, CancellationToken.None).Matches.Count);
    }

    [Fact]
    public void BuildAndSearchHonourCancellation()
    {
        var rows = Enumerable.Range(0, 3000).Select(i => (IReadOnlyDictionary<string, object?>)Row(i, "n" + i)).ToArray();
        var document = Doc(rows);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => PetraReportDistinctValues.Build(document, Name, null, cts.Token));
        var all = PetraReportDistinctValues.Build(document, Name, null, CancellationToken.None);
        Assert.Throws<OperationCanceledException>(() => PetraReportDistinctValues.Search(all, "n1", 500, cts.Token));
    }

    [Fact]
    public void SortUsesRawValuesStablyAndNullsFirstAscending()
    {
        var first = Row(1.234m, "first");
        var second = Row(1.231m, "second");
        var third = Row(1.234m, "third");
        var blank = Row(null, "blank");
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows = [first, second, third, blank];

        var ascending = PetraReportRowSorter.Sort(rows, Amount, ListSortDirection.Ascending, CancellationToken.None);
        Assert.Equal(["blank", "second", "first", "third"], ascending.Select(r => (string)r["Name"]!).ToArray());

        var descending = PetraReportRowSorter.Sort(rows, Amount, ListSortDirection.Descending, CancellationToken.None);
        // equal keys keep snapshot order (stable) in the descending direction too
        Assert.Equal(["first", "third", "second", "blank"], descending.Select(r => (string)r["Name"]!).ToArray());
        Assert.Equal(4, rows.Count);                      // input untouched
    }

    [Fact]
    public void SortOf100kRowsIsCorrectAndCancellable()
    {
        var rows = new IReadOnlyDictionary<string, object?>[100_000];
        for (var i = 0; i < rows.Length; i++) rows[i] = Row((i * 7919L) % 100_003L, "n" + i);
        var sorted = PetraReportRowSorter.Sort(rows, Amount, ListSortDirection.Ascending, CancellationToken.None);
        Assert.Equal(rows.Length, sorted.Count);
        for (var i = 1; i < sorted.Count; i++)
            Assert.True(ReportDocument.DecimalValue(sorted[i - 1]["Debit"]) <= ReportDocument.DecimalValue(sorted[i]["Debit"]));

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => PetraReportRowSorter.Sort(rows, Amount, ListSortDirection.Descending, cts.Token));
    }

    [Fact]
    public void LargeInListIsEvaluatedByHashLookupWithExactSemantics()
    {
        var rows = new IReadOnlyDictionary<string, object?>[100_000];
        for (var i = 0; i < rows.Length; i++) rows[i] = Row(i % 20_000 + 0.001m, "n" + i);
        var document = Doc(rows);
        var values = Enumerable.Range(0, 10_000).Select(i => (i * 2 + 0.001m).ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        var filter = new PetraReportFilterCondition("Debit", PetraReportFilterOperator.In, Values: values);
        var result = PetraReportFilterEngine.Apply(document, rows, filter);
        Assert.Equal(50_000, result.Count);               // even indexes only, 5 repetitions of 10,000 values
        Assert.All(result, r => Assert.Equal(0, (int)Math.Floor(ReportDocument.DecimalValue(r["Debit"])) % 2));
    }
}

[Collection(WpfApplicationGroup.Name)]
public sealed class PetraReportGridGenerationWpfTests
{
    // The report grid runs on the one shared WPF host thread (a second Application or dispatcher thread cannot use its theme resources).
    private static Task RunSta(Func<Task> body) => WpfApplicationHost.RunAsync(async () =>
    {
        WpfApplicationHost.ResetResources();
        try { await body().ConfigureAwait(true); }
        finally { WpfApplicationHost.ResetResources(); }
    }, TimeSpan.FromSeconds(60));

    private static ReportDocument Numbered(string prefix, int count) =>
        PetraReportViewCoreTests.Doc(Enumerable.Range(0, count)
            .Select(i => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
            {
                ["Debit"] = (decimal)((count - i) % 50), ["Name"] = prefix + i, ["Date"] = "2026-01-01",
            }).ToArray());

    private static PetraReportFilterCondition Over(decimal value) =>
        new("Debit", PetraReportFilterOperator.GreaterThanOrEqual, value.ToString(System.Globalization.CultureInfo.InvariantCulture));

    [Fact]
    public async Task FilterCompletingAfterTheReportSectionChangedIsRejectedBeforeAnyRowIsTouched()
    {
        await RunSta(async () =>
        {
            var grid = new PetraInteractiveReportGrid();
            var first = Numbered("a", 2000);
            var second = Numbered("b", 10);
            grid.Document = first;
            var generationBefore = grid.ViewGeneration;
            var pending = grid.ApplyFilterAsync(Over(25), CancellationToken.None); // runs on a worker; continuation needs this dispatcher
            grid.Document = second;                                                  // section change happens first
            Assert.True(grid.ViewGeneration > generationBefore);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending).ConfigureAwait(true);
            Assert.Equal(second.Rows.Count, grid.VisibleRowCount);                  // the stale result of `first` never reached the grid
            Assert.Same(second.Rows[0], grid.VisibleRowsSnapshot[0]);
        }).ConfigureAwait(true);
    }

    [Fact]
    public async Task CancelPendingViewWorkRejectsInFlightResultAndKeepsCommittedRows()
    {
        await RunSta(async () =>
        {
            var grid = new PetraInteractiveReportGrid { Document = Numbered("a", 2000) };
            var pending = grid.ApplyFilterAsync(Over(25), CancellationToken.None);
            grid.CancelPendingViewWork();                                            // window closing
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending).ConfigureAwait(true);
            Assert.Equal(2000, grid.VisibleRowCount);
        }).ConfigureAwait(true);
    }

    [Fact]
    public async Task SortRequestedWhileFilteringKeepsTheFilterAndTheSupersededCallerIsCancelled()
    {
        await RunSta(async () =>
        {
            var document = Numbered("a", 2000);
            var grid = new PetraInteractiveReportGrid { Document = document };
            var filtering = grid.ApplyFilterAsync(Over(25), CancellationToken.None);
            var sorting = grid.ApplySortAsync("Debit", ListSortDirection.Descending, CancellationToken.None);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => filtering).ConfigureAwait(true);
            Assert.True(await sorting.ConfigureAwait(true));
            var expected = document.Rows.Count(r => ReportDocument.DecimalValue(r["Debit"]) >= 25);
            Assert.Equal(expected, grid.VisibleRowCount);                           // the filter was not lost
            var values = grid.VisibleRowsSnapshot.Select(r => ReportDocument.DecimalValue(r["Debit"])).ToArray();
            Assert.Equal(values.OrderByDescending(v => v), values);                  // and the sort applied
        }).ConfigureAwait(true);
    }

    [Fact]
    public async Task CancelledFilterDoesNotLeakIntoALaterSort()
    {
        await RunSta(async () =>
        {
            var document = Numbered("a", 2000);
            var grid = new PetraInteractiveReportGrid { Document = document };
            using var cts = new CancellationTokenSource();
            var filtering = grid.ApplyFilterAsync(Over(25), cts.Token);
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => filtering).ConfigureAwait(true);
            Assert.True(await grid.ApplySortAsync("Debit", ListSortDirection.Ascending, CancellationToken.None).ConfigureAwait(true));
            Assert.Equal(2000, grid.VisibleRowCount);                                // the cancelled filter must not apply
        }).ConfigureAwait(true);
    }

    [Fact]
    public async Task SelectedRowIsKeptWhenStillVisibleAndClearedWhenFilteredOut()
    {
        await RunSta(async () =>
        {
            var document = Numbered("a", 200);
            var grid = new PetraInteractiveReportGrid { Document = document };
            var keep = document.Rows.First(r => ReportDocument.DecimalValue(r["Debit"]) >= 25);
            grid.DataGrid.SelectedItem = keep;
            await grid.ApplySortAsync("Debit", ListSortDirection.Descending, CancellationToken.None).ConfigureAwait(true);
            Assert.Same(keep, grid.DataGrid.SelectedItem);
            var drop = document.Rows.First(r => ReportDocument.DecimalValue(r["Debit"]) < 25);
            grid.DataGrid.SelectedItem = drop;
            await grid.ApplyFilterAsync(Over(25), CancellationToken.None).ConfigureAwait(true);
            Assert.Null(grid.DataGrid.SelectedItem);
        }).ConfigureAwait(true);
    }
}
