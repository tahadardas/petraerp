using PetraERP.Domain.AccountingCalendars;

namespace PetraERP.Domain.Tests;

public sealed class FiscalPeriodTransitionTests
{
    [Theory]
    [InlineData(AccountingPeriodState.Open, AccountingPeriodState.Open, false)]
    [InlineData(AccountingPeriodState.Open, AccountingPeriodState.SoftClosed, true)]
    [InlineData(AccountingPeriodState.Open, AccountingPeriodState.Closed, true)]
    [InlineData(AccountingPeriodState.SoftClosed, AccountingPeriodState.Open, true)]
    [InlineData(AccountingPeriodState.SoftClosed, AccountingPeriodState.SoftClosed, false)]
    [InlineData(AccountingPeriodState.SoftClosed, AccountingPeriodState.Closed, true)]
    [InlineData(AccountingPeriodState.Closed, AccountingPeriodState.Open, true)]
    [InlineData(AccountingPeriodState.Closed, AccountingPeriodState.SoftClosed, false)]
    [InlineData(AccountingPeriodState.Closed, AccountingPeriodState.Closed, false)]
    public void TransitionMatrixIsExplicitAndCarriesCompleteAuditIntent(
        AccountingPeriodState previous, AccountingPeriodState next, bool allowed)
    {
        var company = OrganizationTests.Company();
        var initial = FiscalCalendarTests.Year(company);
        var id = initial.Periods[0].Id;
        var context = FiscalCalendarTests.Context("مراجعة معتمدة\nالمرجع: ١٢٣");
        var year = previous switch
        {
            AccountingPeriodState.Open => initial,
            AccountingPeriodState.SoftClosed => initial.SoftClosePeriod(company, id, context).UpdatedYear,
            AccountingPeriodState.Closed => initial.ClosePeriod(company, id, context).UpdatedYear,
            _ => throw new InvalidOperationException(),
        };
        FiscalPeriodChange Change() => next switch
        {
            AccountingPeriodState.Open => year.ReopenPeriod(company, id, context),
            AccountingPeriodState.SoftClosed => year.SoftClosePeriod(company, id, context),
            AccountingPeriodState.Closed => year.ClosePeriod(company, id, context),
            _ => throw new InvalidOperationException(),
        };
        if (!allowed)
        {
            Assert.Throws<InvalidOperationException>(() => Change());
            Assert.Equal(previous, year.Periods[0].State);
            return;
        }

        var result = Change();
        Assert.Equal(previous, result.Change.PreviousState);
        Assert.Equal(next, result.Change.NewState);
        Assert.Equal(next, result.UpdatedYear.Periods[0].State);
        Assert.Equal(previous, year.Periods[0].State);
        Assert.Equal(initial.Id, result.UpdatedYear.Id);
        Assert.Equal(company.Id, result.Change.CompanyId);
        Assert.Equal(initial.Id, result.Change.FiscalYearId);
        Assert.Equal(id, result.Change.PeriodId);
        Assert.Equal(context.ActorUserId, result.Change.ActorUserId);
        Assert.Equal(context.CorrelationId, result.Change.CorrelationId);
        Assert.Equal(context.OccurredAtUtc, result.Change.OccurredAtUtc);
        Assert.Equal(context.Reason, result.Change.Reason);
        Assert.Equal(initial.Periods[1], result.UpdatedYear.Periods[1]);
        Assert.NotEqual(Guid.Empty, result.Change.EventId);
    }

    [Fact]
    public void TransitionContextRequiresARealUtcTimestampAndSupportsArabicMultilineReasons()
    {
        var id = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => PeriodTransitionContext.Create(id, id, "سبب", default));
        Assert.Throws<ArgumentException>(() => FiscalCalendarTests.Context(new string('س', 1001)));
        var context = FiscalCalendarTests.Context("مراجعة\nتفصيل\tإضافي");
        Assert.Equal("مراجعة\nتفصيل\tإضافي", context.Reason);
    }
}
