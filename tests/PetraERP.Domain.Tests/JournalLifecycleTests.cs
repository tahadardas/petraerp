using PetraERP.Domain.AccountingCalendars;
using PetraERP.Domain.FinancialValues;
using PetraERP.Domain.GeneralLedger;
using PetraERP.Domain.Organizations;
using PetraERP.Domain.Primitives;

namespace PetraERP.Domain.Tests;

public sealed class JournalLifecycleTests
{
    [Fact]
    public void ManualJournalMustValidateAndApproveBeforePostingAndThenIsImmutable()
    {
        var fixture = new JournalFixture();
        var draft = fixture.Draft();
        var number = JournalPostingNumber.Create(fixture.Scope, fixture.Year, 1);
        Assert.Throws<InvalidOperationException>(() => draft.Post(fixture.Validation(), number, fixture.Action()));
        Assert.Throws<InvalidOperationException>(() => draft.Approve(fixture.Action()));
        var validated = draft.Validate(fixture.Validation(), fixture.Action());
        var approved = validated.Approve(fixture.Action());
        var posted = approved.Post(fixture.Validation(), number, fixture.Action());
        Assert.Equal(JournalEntryState.Draft, draft.State);
        Assert.Equal(JournalEntryState.Posted, posted.State);
        Assert.Equal(number, posted.PostingNumber);
        Assert.Equal(fixture.Year.Periods[0].Id, posted.PostingPeriodId);
        Assert.Equal(4, posted.Revision);
        Assert.Throws<InvalidOperationException>(() => posted.ReturnToDraft(fixture.Action()));
        Assert.Throws<InvalidOperationException>(() => posted.Revise(posted.AccountingDate, "تعديل", posted.Lines, fixture.Action()));
    }

    [Theory]
    [InlineData(AccountingPeriodState.SoftClosed)]
    [InlineData(AccountingPeriodState.Closed)]
    public void ClosingPeriodAfterApprovalStillPreventsPosting(AccountingPeriodState state)
    {
        var fixture = new JournalFixture();
        var approved = fixture.Draft().Validate(fixture.Validation(), fixture.Action()).Approve(fixture.Action());
        var periodId = fixture.Year.Periods[0].Id;
        var closed = (state == AccountingPeriodState.SoftClosed
            ? fixture.Year.SoftClosePeriod(fixture.Company, periodId, FiscalCalendarTests.Context())
            : fixture.Year.ClosePeriod(fixture.Company, periodId, FiscalCalendarTests.Context())).UpdatedYear;
        Assert.Throws<InvalidOperationException>(() => approved.Post(fixture.Validation(closed),
            JournalPostingNumber.Create(fixture.Scope, fixture.Year, 1), fixture.Action()));
        Assert.Equal(JournalEntryState.Approved, approved.State);
    }

    [Theory]
    [InlineData(JournalEntryState.Draft)]
    [InlineData(JournalEntryState.Validated)]
    [InlineData(JournalEntryState.Approved)]
    [InlineData(JournalEntryState.Posted)]
    [InlineData(JournalEntryState.Reversed)]
    public void LifecycleMatrixRejectsOperationsFromEveryUnsupportedState(JournalEntryState state)
    {
        var fixture = new JournalFixture();
        var journal = fixture.AtState(state);
        var number = JournalPostingNumber.Create(fixture.Scope, fixture.Year, 3);
        var checks = new (Func<JournalEntry> Run, bool Allowed)[]
        {
            (() => journal.Validate(fixture.Validation(), fixture.Action()), state == JournalEntryState.Draft),
            (() => journal.Approve(fixture.Action()), state == JournalEntryState.Validated),
            (() => journal.Post(fixture.Validation(), number, fixture.Action()), state == JournalEntryState.Approved),
            (() => journal.ReturnToDraft(fixture.Action()), state is JournalEntryState.Validated or JournalEntryState.Approved),
            (() => journal.Revise(journal.AccountingDate, "تصحيح مسودة", journal.Lines, fixture.Action()), state == JournalEntryState.Draft),
            (() => journal.CreateReversalDraft(fixture.Scope, fixture.Date, "عكس", fixture.Action()), state == JournalEntryState.Posted),
            (() => journal.CreateCorrectionDraft(fixture.Scope, fixture.Date, "تصحيح",
                [fixture.Line(fixture.DebitAccount, 100m, 0m), fixture.Line(fixture.CreditAccount, 0m, 100m)], fixture.Action()), state == JournalEntryState.Reversed),
        };
        foreach (var check in checks)
        {
            if (check.Allowed)
            {
                Assert.NotNull(check.Run());
            }
            else
            {
                Assert.Throws<InvalidOperationException>(() => check.Run());
            }
        }

        Assert.Equal(state, journal.State);
    }

    [Fact]
    public void EditingAfterReturnToDraftRequiresValidationAndApprovalAgain()
    {
        var fixture = new JournalFixture();
        var approved = fixture.AtState(JournalEntryState.Approved);
        var draft = approved.ReturnToDraft(fixture.Action());
        var revised = draft.Revise(fixture.Date.AddDays(1), "قيد معدّل",
            [fixture.Line(fixture.DebitAccount, 200m, 0m), fixture.Line(fixture.CreditAccount, 0m, 200m)], fixture.Action());
        var number = JournalPostingNumber.Create(fixture.Scope, fixture.Year, 1);
        Assert.Throws<InvalidOperationException>(() => revised.Post(fixture.Validation(), number, fixture.Action()));
        Assert.Throws<InvalidOperationException>(() => revised.Approve(fixture.Action()));
        var posted = revised.Validate(fixture.Validation(), fixture.Action()).Approve(fixture.Action())
            .Post(fixture.Validation(), number, fixture.Action());
        Assert.Equal(200m, posted.Lines[0].BaseAmounts.Debit.Amount);
        Assert.Equal(100m, approved.Lines[0].BaseAmounts.Debit.Amount);
        Assert.Equal(fixture.Date.AddDays(1), posted.AccountingDate);
    }

    [Fact]
    public void SnapshotsAndAuditIntentsPreserveIdentityAndCannotBeMutated()
    {
        var fixture = new JournalFixture();
        var lines = new List<JournalLine> { fixture.Line(fixture.DebitAccount, 100m, 0m), fixture.Line(fixture.CreditAccount, 0m, 100m) };
        var draft = fixture.Draft(lines);
        lines.Clear();
        var posted = draft.Validate(fixture.Validation(), fixture.Action()).Approve(fixture.Action())
            .Post(fixture.Validation(), JournalPostingNumber.Create(fixture.Scope, fixture.Year, 1), fixture.Action());
        Assert.Equal(draft.Id, posted.Id);
        Assert.Equal(7, draft.Id.Version);
        Assert.Equal(1, draft.Revision);
        Assert.Equal(4, posted.Changes.Count);
        Assert.Equal(new[] { JournalAuditAction.Created, JournalAuditAction.Validated, JournalAuditAction.Approved, JournalAuditAction.Posted },
            posted.Changes.Select(static change => change.Action));
        Assert.Null(posted.Changes[0].PreviousState);
        Assert.Equal(JournalEntryState.Approved, posted.Changes[^1].PreviousState);
        Assert.Equal(posted.Changes.Count, posted.Changes.Select(static change => change.EventId).Distinct().Count());
        foreach (var change in posted.Changes)
        {
            Assert.Equal(fixture.Company.Id, change.CompanyId);
            Assert.Equal(fixture.Scope.BranchId, change.BranchId);
            Assert.Equal(posted.Id, change.JournalId);
            Assert.Equal(fixture.ActorUserId, change.Context.ActorUserId);
            Assert.Equal(TimeSpan.Zero, change.Context.OccurredAtUtc.Offset);
            Assert.False(string.IsNullOrWhiteSpace(change.Context.Reason));
        }

        Assert.Throws<NotSupportedException>(() => Assert.IsAssignableFrom<IList<JournalLine>>(posted.Lines).Clear());
        Assert.Throws<NotSupportedException>(() => Assert.IsAssignableFrom<IList<JournalAuditEvent>>(posted.Changes).Clear());
    }

    [Fact]
    public void ActionsCannotMoveBackwardInUtcTime()
    {
        var fixture = new JournalFixture();
        var draft = fixture.Draft();
        var past = JournalActionContext.Create(fixture.ActorUserId, Guid.NewGuid(), "زمن سابق", JournalFixture.Time.AddTicks(-1));
        Assert.Throws<ArgumentException>(() => draft.Validate(fixture.Validation(), past));
        Assert.Equal(JournalEntryState.Draft, draft.State);
        var later = JournalActionContext.Create(fixture.ActorUserId, Guid.NewGuid(), "زمن لاحق", JournalFixture.Time.AddDays(1));
        var validated = draft.Validate(fixture.Validation(), later);
        Assert.Throws<ArgumentException>(() => validated.Approve(fixture.Action()));
    }
}

internal sealed class JournalFixture
{
    internal JournalFixture()
    {
        Company = OrganizationTests.Company();
        Scope = FiscalCalendarTests.Scope(Company);
        Year = FiscalCalendarTests.Year(Company);
        DebitAccount = Account.Create(Company, EntityCode.Create("1101"), BilingualName.Create("الصندوق"),
            AccountType.Assets, true, CostCenterPolicy.Optional, AccountCurrencyPolicy.AnyEnabled());
        CreditAccount = Account.Create(Company, EntityCode.Create("3101"), BilingualName.Create("رأس المال"),
            AccountType.Equity, true, CostCenterPolicy.Optional, AccountCurrencyPolicy.AnyEnabled());
    }

    internal Company Company { get; }
    internal BranchScope Scope { get; }
    internal FiscalYear Year { get; }
    internal Account DebitAccount { get; }
    internal Account CreditAccount { get; }
    internal DateOnly Date { get; } = new(2025, 8, 15);
    internal Guid ActorUserId { get; } = Guid.NewGuid();
    internal static DateTimeOffset Time => new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    internal JournalActionContext Action(string reason = "عملية محاسبية معتمدة") =>
        JournalActionContext.Create(ActorUserId, Guid.NewGuid(), reason, Time);

    internal JournalLine Line(Account account, decimal debit, decimal credit, CurrencyCode? currency = null,
        decimal rate = 1m, Guid? centerId = null) => JournalLine.Create(Company, account.Id, centerId,
            JournalLineAmounts.Create(Money.Create(debit, currency ?? Company.BaseCurrency),
                Money.Create(credit, currency ?? Company.BaseCurrency)),
            ExchangeRate.Create(currency ?? Company.BaseCurrency, Company.BaseCurrency, rate),
            MidpointRounding.ToEven, "سطر قيد عربي");

    internal JournalEntry Draft(IEnumerable<JournalLine>? lines = null) =>
        JournalEntry.CreateManualDraft(Scope, Date, "إثبات رأس المال",
            lines ?? [Line(DebitAccount, 100m, 0m), Line(CreditAccount, 0m, 100m)], Action());

    internal JournalValidationContext Validation(FiscalYear? year = null, IEnumerable<Account>? accounts = null,
        IEnumerable<CostCenter>? centers = null) => JournalValidationContext.Create(Company, Scope, year ?? Year,
            accounts ?? [DebitAccount, CreditAccount], centers ?? []);

    internal JournalEntry Posted() => Draft().Validate(Validation(), Action()).Approve(Action())
        .Post(Validation(), JournalPostingNumber.Create(Scope, Year, 1), Action());

    internal JournalEntry AtState(JournalEntryState state)
    {
        var draft = Draft();
        if (state == JournalEntryState.Draft) { return draft; }
        var validated = draft.Validate(Validation(), Action());
        if (state == JournalEntryState.Validated) { return validated; }
        var approved = validated.Approve(Action());
        if (state == JournalEntryState.Approved) { return approved; }
        var posted = approved.Post(Validation(), JournalPostingNumber.Create(Scope, Year, 1), Action());
        if (state == JournalEntryState.Posted) { return posted; }
        return posted.CreateReversalDraft(Scope, Date, "عكس للاختبار", Action()).Validate(Validation(), Action())
            .Approve(Action()).PostReversal(posted, Validation(), JournalPostingNumber.Create(Scope, Year, 2), Action()).OriginalEntry;
    }
}
