using PetraERP.Domain.AccountingCalendars;
using PetraERP.Domain.Primitives;

namespace PetraERP.Domain.Tests;

public sealed class FiscalCalendarBoundaryTests
{
    [Fact]
    public void LeapDayAndMaximumSupportedBusinessDateAreHandledExactly()
    {
        var company = OrganizationTests.Company();
        var scope = FiscalCalendarTests.Scope(company);
        var leap = DateRange.Create(new DateOnly(2024, 2, 28), new DateOnly(2024, 3, 1));
        var leapYear = FiscalYear.Create(company, EntityCode.Create("LEAP"), leap,
            [PeriodDefinition.Create(EntityCode.Create("P"), leap)]);
        Assert.Equal(leapYear.Periods[0], leapYear.GetOpenPeriodForPosting(scope, new DateOnly(2024, 2, 29)));
        var maximum = DateRange.Create(DateOnly.MaxValue, DateOnly.MaxValue);
        var lastYear = FiscalYear.Create(company, EntityCode.Create("LAST"), maximum,
            [PeriodDefinition.Create(EntityCode.Create("LAST-P"), maximum)]);
        Assert.Equal(maximum.End, lastYear.GetOpenPeriodForPosting(scope, DateOnly.MaxValue).Range.End);
        Assert.Throws<ArgumentException>(() => DateRange.Create(new DateOnly(2025, 2, 1), new DateOnly(2025, 1, 31)));
    }

    [Fact]
    public void PeriodCodesMustBeUniqueAndSnapshotsCannotBeMutated()
    {
        var company = OrganizationTests.Company();
        var range = DateRange.Create(new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31));
        var first = PeriodDefinition.Create(EntityCode.Create("P1"), DateRange.Create(range.Start, new DateOnly(2025, 6, 30)));
        var second = PeriodDefinition.Create(EntityCode.Create("P2"), DateRange.Create(new DateOnly(2025, 7, 1), range.End));
        var source = new List<PeriodDefinition> { second, first };
        var year = FiscalYear.Create(company, EntityCode.Create("FY"), range, source);
        source.Clear();
        Assert.Equal(range.Start, year.Periods[0].Range.Start);
        var view = Assert.IsAssignableFrom<IList<AccountingPeriod>>(year.Periods);
        Assert.Throws<NotSupportedException>(() => view.RemoveAt(0));
        var duplicate = PeriodDefinition.Create(EntityCode.Create(" P1 "), second.Range);
        Assert.Throws<ArgumentException>(() => FiscalYear.Create(company, EntityCode.Create("DUP"), range, [first, duplicate]));
        Assert.Throws<ArgumentException>(() => FiscalYear.Create(company, EntityCode.Create("EMPTY"), range, []));
        Assert.Throws<ArgumentException>(() => FiscalYear.Create(company, EntityCode.Create("NULL"), range, [null!]));
    }

    [Fact]
    public void GeneratedCalendarsCoverEveryBusinessDayExactlyOnce()
    {
        var random = new Random(491829);
        var company = OrganizationTests.Company();
        var scope = FiscalCalendarTests.Scope(company);
        for (var iteration = 0; iteration < 250; iteration++)
        {
            var start = new DateOnly(random.Next(2000, 2080), random.Next(1, 13), random.Next(1, 28));
            var span = random.Next(1, 500);
            var end = start.AddDays(span - 1);
            var definitions = new List<PeriodDefinition>();
            var cursor = start;
            while (cursor <= end)
            {
                var periodEnd = cursor.AddDays(Math.Min(random.Next(1, 45), end.DayNumber - cursor.DayNumber));
                definitions.Add(PeriodDefinition.Create(EntityCode.Create($"P{definitions.Count}"), DateRange.Create(cursor, periodEnd)));
                cursor = periodEnd.AddDays(1);
            }

            var calendar = FiscalYear.Create(company, EntityCode.Create("GENERATED"), DateRange.Create(start, end), definitions);
            for (var offset = 0; offset < span; offset++)
            {
                var day = start.AddDays(offset);
                Assert.Single(calendar.Periods, period => period.Range.Contains(day));
                Assert.True(calendar.GetOpenPeriodForPosting(scope, day).Range.Contains(day));
            }
        }
    }
}
