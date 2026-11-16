using PetraERP.Domain.AccountingCalendars;
using PetraERP.Domain.Organizations;
using PetraERP.Domain.Primitives;

namespace PetraERP.Domain.Tests;

public sealed class FiscalCalendarTests
{
    private static readonly DateOnly Start = new(2025, 7, 1);
    private static readonly DateOnly Middle = new(2025, 12, 31);
    private static readonly DateOnly End = new(2026, 6, 30);

    internal static FiscalYear Year(Company company) => FiscalYear.Create(company, EntityCode.Create("FY25-26"),
        DateRange.Create(Start, End),
        [PeriodDefinition.Create(EntityCode.Create("H1"), DateRange.Create(Start, Middle)),
         PeriodDefinition.Create(EntityCode.Create("H2"), DateRange.Create(Middle.AddDays(1), End))]);

    internal static BranchScope Scope(Company company) => BranchScope.Create(company,
        Branch.Create(company, EntityCode.Create("BR"), BilingualName.Create("فرع دمشق")));

    internal static PeriodTransitionContext Context(string reason = "إغلاق الفترة بعد المطابقة") =>
        PeriodTransitionContext.Create(Guid.NewGuid(), Guid.NewGuid(), reason,
            new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public void ExplicitFiscalYearSupportsNonJanuaryStartAndInclusiveBoundaries()
    {
        var company = OrganizationTests.Company();
        var year = Year(company);
        var scope = Scope(company);
        Assert.Equal(year.Periods[0].Id, year.GetOpenPeriodForPosting(scope, Start).Id);
        Assert.Equal(year.Periods[0].Id, year.GetOpenPeriodForPosting(scope, Middle).Id);
        Assert.Equal(year.Periods[1].Id, year.GetOpenPeriodForPosting(scope, Middle.AddDays(1)).Id);
        Assert.Equal(year.Periods[1].Id, year.GetOpenPeriodForPosting(scope, End).Id);
        Assert.Throws<ArgumentOutOfRangeException>(() => year.GetOpenPeriodForPosting(scope, Start.AddDays(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => year.GetOpenPeriodForPosting(scope, End.AddDays(1)));
    }

    [Fact]
    public void GapsOverlapsAndIncompleteCoverageAreRejected()
    {
        var company = OrganizationTests.Company();
        foreach (var secondStart in new[] { Middle, Middle.AddDays(2) })
        {
            Assert.Throws<ArgumentException>(() => FiscalYear.Create(company, EntityCode.Create("FY"), DateRange.Create(Start, End),
                [PeriodDefinition.Create(EntityCode.Create("A"), DateRange.Create(Start, Middle)),
                 PeriodDefinition.Create(EntityCode.Create("B"), DateRange.Create(secondStart, End))]));
        }
        Assert.Throws<ArgumentException>(() => FiscalYear.Create(company, EntityCode.Create("FY"), DateRange.Create(Start, End),
            [PeriodDefinition.Create(EntityCode.Create("A"), DateRange.Create(Start, Middle))]));
    }

    [Fact]
    public void SoftCloseAndCloseBlockPostingUntilAnAuditedReopen()
    {
        var company = OrganizationTests.Company();
        var year = Year(company);
        var scope = Scope(company);
        var periodId = year.Periods[0].Id;
        var context = Context();
        var soft = year.SoftClosePeriod(company, periodId, context);
        Assert.Throws<InvalidOperationException>(() => soft.UpdatedYear.GetOpenPeriodForPosting(scope, Start));
        var closed = soft.UpdatedYear.ClosePeriod(company, periodId, context);
        Assert.Throws<InvalidOperationException>(() => closed.UpdatedYear.GetOpenPeriodForPosting(scope, Start));
        var reopened = closed.UpdatedYear.ReopenPeriod(company, periodId, Context("إعادة فتح معتمدة للتصحيح"));
        Assert.Equal(periodId, reopened.UpdatedYear.GetOpenPeriodForPosting(scope, Start).Id);
        Assert.Equal(AccountingPeriodState.Closed, reopened.Change.PreviousState);
        Assert.Equal(AccountingPeriodState.Open, reopened.Change.NewState);
        Assert.Equal(company.Id, reopened.Change.CompanyId);
        Assert.Equal(periodId, reopened.Change.PeriodId);
        Assert.Equal("إعادة فتح معتمدة للتصحيح", reopened.Change.Reason);
        Assert.Equal(AccountingPeriodState.Open, year.Periods[0].State);
    }

    [Fact]
    public void CompanyMismatchAndForeignPeriodsFailClosed()
    {
        var company = OrganizationTests.Company();
        var other = OrganizationTests.Company("OTHER");
        var year = Year(company);
        Assert.Throws<ArgumentException>(() => year.GetOpenPeriodForPosting(Scope(other), Start));
        Assert.Throws<ArgumentException>(() => year.ClosePeriod(other, year.Periods[0].Id, Context()));
        Assert.Throws<ArgumentException>(() => year.ClosePeriod(company, Year(other).Periods[0].Id, Context()));
    }

    [Fact]
    public void AuditContextRejectsMissingReasonActorCorrelationAndNonUtcTime()
    {
        Assert.Throws<ArgumentException>(() => Context(" "));
        Assert.Throws<ArgumentException>(() => PeriodTransitionContext.Create(Guid.Empty, Guid.NewGuid(), "سبب", DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentException>(() => PeriodTransitionContext.Create(Guid.NewGuid(), Guid.Empty, "سبب", DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentException>(() => PeriodTransitionContext.Create(Guid.NewGuid(), Guid.NewGuid(), "سبب",
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.FromHours(3))));
    }
}
