using PetraERP.Domain.AccountingCalendars;
using PetraERP.Domain.FinancialValues;
using PetraERP.Domain.GeneralLedger;
using PetraERP.Domain.Primitives;

namespace PetraERP.Domain.Tests;

public sealed class JournalReversalTests
{
    [Fact]
    public void ReversalPreservesHistoricalValuesAndLinksBothJournalsAndEveryLine()
    {
        var fixture = new JournalFixture();
        var foreign = fixture.Line(fixture.DebitAccount, 9.9999m, 0m, CurrencyCode.Create("SYP"), 0.33333333m);
        var original = fixture.Draft([foreign, fixture.Line(fixture.CreditAccount, 0m, 3.3333m)])
            .Validate(fixture.Validation(), fixture.Action()).Approve(fixture.Action())
            .Post(fixture.Validation(), JournalPostingNumber.Create(fixture.Scope, fixture.Year, 1), fixture.Action());
        var draft = original.CreateReversalDraft(fixture.Scope, fixture.Date.AddDays(1), "عكس لتصحيح قيد", fixture.Action());
        Assert.NotEqual(original.Id, draft.Id);
        Assert.Equal(original.Id, draft.ReversesEntryId);
        Assert.Throws<InvalidOperationException>(() => draft.Revise(fixture.Date, "تغيير", original.Lines, fixture.Action()));
        var approved = draft.Validate(fixture.Validation(), fixture.Action()).Approve(fixture.Action());
        var number = JournalPostingNumber.Create(fixture.Scope, fixture.Year, 2);
        Assert.Throws<InvalidOperationException>(() => draft.PostReversal(original, fixture.Validation(), number, fixture.Action()));
        Assert.Throws<InvalidOperationException>(() => approved.Post(fixture.Validation(), number, fixture.Action()));
        var result = approved.PostReversal(original, fixture.Validation(), number, fixture.Action());
        Assert.Equal(JournalEntryState.Reversed, result.OriginalEntry.State);
        Assert.Equal(JournalEntryState.Posted, result.ReversalEntry.State);
        Assert.Equal(draft.Id, result.OriginalEntry.ReversedByEntryId);
        Assert.Equal(original.Id, result.ReversalEntry.ReversesEntryId);
        Assert.Equal(original.PostingNumber, result.OriginalEntry.PostingNumber);
        Assert.Equal(original.PostingPeriodId, result.OriginalEntry.PostingPeriodId);
        Assert.Equal(JournalEntryState.Posted, original.State);
        Assert.Equal(JournalAuditAction.Reversed, result.OriginalEntry.Changes[^1].Action);
        Assert.Equal(draft.Id, result.OriginalEntry.Changes[^1].RelatedEntryId);
        Assert.Equal(original.Id, result.ReversalEntry.Changes[^1].RelatedEntryId);
        AssertReversedLines(original, result.ReversalEntry);
        Assert.Throws<InvalidOperationException>(() => result.OriginalEntry.CreateReversalDraft(fixture.Scope, fixture.Date, "تكرار", fixture.Action()));
        Assert.Throws<InvalidOperationException>(() => approved.PostReversal(result.OriginalEntry, fixture.Validation(), number, fixture.Action()));
        var correction = result.OriginalEntry.CreateCorrectionDraft(fixture.Scope, fixture.Date.AddDays(2), "قيد مصحّح",
            [fixture.Line(fixture.DebitAccount, 90m, 0m), fixture.Line(fixture.CreditAccount, 0m, 90m)], fixture.Action());
        Assert.Equal(original.Id, correction.CorrectsEntryId);
        Assert.Null(correction.ReversesEntryId);
        Assert.Equal(JournalEntryState.Draft, correction.State);
        Assert.Equal(JournalEntryState.Posted, correction.Validate(fixture.Validation(), fixture.Action()).Approve(fixture.Action())
            .Post(fixture.Validation(), JournalPostingNumber.Create(fixture.Scope, fixture.Year, 3), fixture.Action()).State);
    }

    [Fact]
    public void ReversalRetainsCostCenterAndStillRequiresItInTheResolvedContext()
    {
        var fixture = new JournalFixture();
        var center = CostCenter.Create(fixture.Company, EntityCode.Create("DMS"), BilingualName.Create("مركز دمشق"));
        var validation = fixture.Validation(centers: [center]);
        var original = fixture.Draft([fixture.Line(fixture.DebitAccount, 100m, 0m, centerId: center.Id),
            fixture.Line(fixture.CreditAccount, 0m, 100m)]).Validate(validation, fixture.Action()).Approve(fixture.Action())
            .Post(validation, JournalPostingNumber.Create(fixture.Scope, fixture.Year, 1), fixture.Action());
        var reversal = original.CreateReversalDraft(fixture.Scope, fixture.Date, "عكس مع المركز", fixture.Action());
        Assert.Equal(center.Id, reversal.Lines[0].CostCenterId);
        Assert.Throws<InvalidOperationException>(() => reversal.Validate(fixture.Validation(), fixture.Action()));
        var result = reversal.Validate(validation, fixture.Action()).Approve(fixture.Action()).PostReversal(original,
            validation, JournalPostingNumber.Create(fixture.Scope, fixture.Year, 2), fixture.Action());
        AssertReversedLines(original, result.ReversalEntry);
    }

    [Fact]
    public void ReversalCanUseAnOpenLaterYearWithoutChangingTheOriginalClosedPeriod()
    {
        var fixture = new JournalFixture();
        var original = fixture.Posted();
        var closed = fixture.Year.ClosePeriod(fixture.Company, fixture.Year.Periods[0].Id, FiscalCalendarTests.Context()).UpdatedYear;
        var sameDate = original.CreateReversalDraft(fixture.Scope, fixture.Date, "عكس", fixture.Action());
        Assert.Throws<InvalidOperationException>(() => sameDate.Validate(fixture.Validation(closed), fixture.Action()));
        var range = DateRange.Create(new DateOnly(2026, 7, 1), new DateOnly(2027, 6, 30));
        var next = FiscalYear.Create(fixture.Company, EntityCode.Create("FY26-27"), range,
            [PeriodDefinition.Create(EntityCode.Create("ALL"), range)]);
        var validation = fixture.Validation(next);
        var approved = original.CreateReversalDraft(fixture.Scope, range.Start, "عكس في سنة مفتوحة", fixture.Action())
            .Validate(validation, fixture.Action()).Approve(fixture.Action());
        var result = approved.PostReversal(original, validation, JournalPostingNumber.Create(fixture.Scope, next, 1), fixture.Action());
        Assert.Equal(fixture.Year.Id, result.OriginalEntry.PostingNumber!.FiscalYearId);
        Assert.Equal(next.Id, result.ReversalEntry.PostingNumber!.FiscalYearId);
        Assert.Equal(AccountingPeriodState.Closed, closed.Periods[0].State);
        AssertReversedLines(original, result.ReversalEntry);
    }

    [Fact]
    public void ReversalRejectsWrongOriginalOrReusedNumberAndCannotLeakCopiedLinesIntoManualDraft()
    {
        var fixture = new JournalFixture();
        var original = fixture.Posted();
        var draft = original.CreateReversalDraft(fixture.Scope, fixture.Date, "عكس", fixture.Action());
        var approved = draft.Validate(fixture.Validation(), fixture.Action()).Approve(fixture.Action());
        Assert.Throws<ArgumentException>(() => approved.PostReversal(fixture.Posted(), fixture.Validation(),
            JournalPostingNumber.Create(fixture.Scope, fixture.Year, 2), fixture.Action()));
        Assert.Throws<ArgumentException>(() => approved.PostReversal(original, fixture.Validation(), original.PostingNumber!, fixture.Action()));
        Assert.Throws<ArgumentException>(() => fixture.Draft(draft.Lines));
        Assert.Throws<ArgumentException>(() => original.CreateReversalDraft(FiscalCalendarTests.Scope(fixture.Company), fixture.Date, "عكس", fixture.Action()));
        Assert.Equal(JournalEntryState.Posted, original.State);
        Assert.Equal(JournalEntryState.Approved, approved.State);
    }

    [Fact]
    public void GeneratedForeignCurrencyReversalsCancelBothTransactionAndBaseValuesExactly()
    {
        var fixture = new JournalFixture();
        var random = new Random(719302);
        for (var iteration = 0; iteration < 250; iteration++)
        {
            var amount = random.Next(100, 10_000_000) / 10_000m;
            var expectedBase = decimal.Round(amount * 0.33333333m, 4, MidpointRounding.ToEven);
            var original = fixture.Draft([fixture.Line(fixture.DebitAccount, amount, 0m, CurrencyCode.Create("SYP"), 0.33333333m),
                fixture.Line(fixture.CreditAccount, 0m, expectedBase)])
                .Validate(fixture.Validation(), fixture.Action()).Approve(fixture.Action())
                .Post(fixture.Validation(), JournalPostingNumber.Create(fixture.Scope, fixture.Year, iteration * 2 + 1), fixture.Action());
            var reversed = original.CreateReversalDraft(fixture.Scope, fixture.Date, "عكس للتحقق", fixture.Action())
                .Validate(fixture.Validation(), fixture.Action()).Approve(fixture.Action())
                .PostReversal(original, fixture.Validation(), JournalPostingNumber.Create(fixture.Scope, fixture.Year, iteration * 2 + 2), fixture.Action());
            Assert.Equal(expectedBase, original.Lines[0].BaseAmounts.Debit.Amount);
            AssertReversedLines(original, reversed.ReversalEntry);
            foreach (var group in original.Lines.Concat(reversed.ReversalEntry.Lines).GroupBy(static line => line.AccountId))
            {
                Assert.Equal(0m, group.Sum(static line => line.TransactionAmounts.Debit.Amount - line.TransactionAmounts.Credit.Amount));
                Assert.Equal(0m, group.Sum(static line => line.BaseAmounts.Debit.Amount - line.BaseAmounts.Credit.Amount));
            }
        }
    }

    private static void AssertReversedLines(JournalEntry original, JournalEntry reversal)
    {
        Assert.Equal(original.Lines.Count, reversal.Lines.Count);
        foreach (var line in original.Lines)
        {
            var reverse = Assert.Single(reversal.Lines, candidate => candidate.ReversesLineId == line.Id);
            Assert.NotEqual(line.Id, reverse.Id);
            Assert.Equal(original.Id, line.JournalId);
            Assert.Equal(reversal.Id, reverse.JournalId);
            Assert.Equal(line.AccountId, reverse.AccountId);
            Assert.Equal(line.CostCenterId, reverse.CostCenterId);
            Assert.Equal(line.Rate, reverse.Rate);
            Assert.Equal(line.Rounding, reverse.Rounding);
            Assert.Equal(line.TransactionAmounts.Debit, reverse.TransactionAmounts.Credit);
            Assert.Equal(line.TransactionAmounts.Credit, reverse.TransactionAmounts.Debit);
            Assert.Equal(line.BaseAmounts.Debit, reverse.BaseAmounts.Credit);
            Assert.Equal(line.BaseAmounts.Credit, reverse.BaseAmounts.Debit);
        }
    }
}
