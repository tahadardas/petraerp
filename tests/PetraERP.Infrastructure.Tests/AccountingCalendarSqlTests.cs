using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.Calendars;
using PetraERP.Application.GeneralLedger.ChartOfAccounts;
using PetraERP.Domain.Primitives;

namespace PetraERP.Infrastructure.Tests;

public sealed class AccountingCalendarSqlTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task FiscalYearsGenerateMonthlyPeriodsAndNeverOverlapWithinACompany()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var s = await LedgerScenario.CreateAsync(database).ConfigureAwait(true);
            using var context = database.Context();
            var calendar = LedgerScenario.Calendar(context);
            var year = Assert.Single(await calendar.ListFiscalYearsAsync(s.Master(s.CompanyA), Cancellation).ConfigureAwait(true));
            Assert.Equal(12, year.Periods.Count);
            Assert.Equal(new DateOnly(2026, 2, 28), year.Periods[1].RangeEnd);
            Assert.All(year.Periods, p => Assert.Equal(PeriodStateCode.Open, p.State));

            var overlap = await Assert.ThrowsAsync<ResourceConflictException>(() => calendar.CreateFiscalYearAsync(s.Master(s.CompanyA),
                new CreateFiscalYearInput("FY2026B", new DateOnly(2026, 7, 1), new DateOnly(2027, 6, 30), null), Cancellation)).ConfigureAwait(true);
            Assert.Equal(LedgerMasterErrorCodes.FiscalYearOverlap, overlap.Code);
            var gap = await Assert.ThrowsAsync<BusinessRuleException>(() => calendar.CreateFiscalYearAsync(s.Master(s.CompanyA),
                new CreateFiscalYearInput("FY2027", new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31),
                    [new("H1", new DateOnly(2027, 1, 1), new DateOnly(2027, 6, 29)), new("H2", new DateOnly(2027, 7, 1), new DateOnly(2027, 12, 31))]), Cancellation)).ConfigureAwait(true);
            Assert.Equal(LedgerMasterErrorCodes.FiscalYearInvalid, gap.Code);
            var halves = await calendar.CreateFiscalYearAsync(s.Master(s.CompanyA), new CreateFiscalYearInput("FY2027", new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31),
                [new("H1", new DateOnly(2027, 1, 1), new DateOnly(2027, 6, 30)), new("H2", new DateOnly(2027, 7, 1), new DateOnly(2027, 12, 31))]), Cancellation).ConfigureAwait(true);
            Assert.Equal(2, halves.Periods.Count);
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => calendar.GetFiscalYearAsync(s.Master(s.CompanyB), halves.Id, Cancellation)).ConfigureAwait(true);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task PeriodTransitionsAreAuditedConcurrencyCheckedAndFollowDomainRules()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var s = await LedgerScenario.CreateAsync(database).ConfigureAwait(true);
            using var context = database.Context();
            var calendar = LedgerScenario.Calendar(context);
            var january = (await calendar.ListFiscalYearsAsync(s.Master(s.CompanyA), Cancellation).ConfigureAwait(true))[0].Periods[0];

            var soft = await calendar.TransitionPeriodAsync(s.Master(s.CompanyA), january.Id, PeriodTransition.SoftClose, "مراجعة نهاية الشهر",
                LedgerScenario.Token(january.VersionBase64), Cancellation).ConfigureAwait(true);
            Assert.Equal(PeriodStateCode.SoftClosed, soft.State);
            await Assert.ThrowsAsync<ConcurrencyConflictException>(() => calendar.TransitionPeriodAsync(s.Master(s.CompanyA), january.Id,
                PeriodTransition.Close, "قديم", LedgerScenario.Token(january.VersionBase64), Cancellation)).ConfigureAwait(true);
            var invalid = await Assert.ThrowsAsync<InvalidOperationException>(() => calendar.TransitionPeriodAsync(s.Master(s.CompanyA), january.Id,
                PeriodTransition.SoftClose, "تكرار", LedgerScenario.Token(soft.VersionBase64), Cancellation)).ConfigureAwait(true);
            Assert.Equal(RuleViolation.PeriodTransitionInvalid, RuleViolation.CodeOf(invalid));
            var closed = await calendar.TransitionPeriodAsync(s.Master(s.CompanyA), january.Id, PeriodTransition.Close, "إقفال",
                LedgerScenario.Token(soft.VersionBase64), Cancellation).ConfigureAwait(true);
            var reopened = await calendar.TransitionPeriodAsync(s.Master(s.CompanyA), january.Id, PeriodTransition.Reopen, "تصحيح معتمد",
                LedgerScenario.Token(closed.VersionBase64), Cancellation).ConfigureAwait(true);
            Assert.Equal(PeriodStateCode.Open, reopened.State);
            await Assert.ThrowsAsync<RequestValidationException>(() => calendar.TransitionPeriodAsync(s.Master(s.CompanyA), january.Id,
                PeriodTransition.Close, "   ", LedgerScenario.Token(reopened.VersionBase64), Cancellation)).ConfigureAwait(true);

            var audit = await calendar.GetPeriodAuditAsync(s.Master(s.CompanyA), january.Id, Cancellation).ConfigureAwait(true);
            Assert.Equal([(PeriodStateCode.Open, PeriodStateCode.SoftClosed), (PeriodStateCode.SoftClosed, PeriodStateCode.Closed), (PeriodStateCode.Closed, PeriodStateCode.Open)],
                audit.Select(a => (a.PreviousState, a.NewState)));
            Assert.Equal("تصحيح معتمد", audit[2].Reason);
            Assert.All(audit, a => Assert.Equal(s.Actor, a.ActorUserId));
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => calendar.TransitionPeriodAsync(s.Master(s.CompanyB), january.Id,
                PeriodTransition.Close, "شركة أخرى", LedgerScenario.Token(reopened.VersionBase64), Cancellation)).ConfigureAwait(true);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task ClosedPeriodRejectsPostingWithAStableCodeUntilReopened()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var s = await LedgerScenario.CreateAsync(database).ConfigureAwait(true);
            var cash = await s.AccountAsync(s.CompanyA, "1101", AccountTypeCode.Assets, true).ConfigureAwait(true);
            var sales = await s.AccountAsync(s.CompanyA, "4101", AccountTypeCode.Revenue, true).ConfigureAwait(true);
            var draft = await s.JournalAsync(s.CompanyA, s.BranchA1, new DateOnly(2026, 1, 12), "USD", false, (cash.Id, 50m, 0m), (sales.Id, 0m, 50m)).ConfigureAwait(true);
            await s.ApproveAsync(s.CompanyA, s.BranchA1, draft.Id).ConfigureAwait(true);

            AccountingPeriodDto january;
            using (var context = database.Context())
            {
                var calendar = LedgerScenario.Calendar(context);
                january = (await calendar.ListFiscalYearsAsync(s.Master(s.CompanyA), Cancellation).ConfigureAwait(true))[0].Periods[0];
                january = await calendar.TransitionPeriodAsync(s.Master(s.CompanyA), january.Id, PeriodTransition.Close, "إقفال كانون الثاني",
                    LedgerScenario.Token(january.VersionBase64), Cancellation).ConfigureAwait(true);
            }

            using (var context = database.Context())
            {
                var approved = await context.JournalEntries.AsNoTracking().SingleAsync(j => j.Id == draft.Id, Cancellation).ConfigureAwait(true);
                var request = PetraERP.Application.GeneralLedger.Posting.PostingRequest.Post(
                    PetraERP.Application.GeneralLedger.Posting.PostingRequestScope.Create(s.CompanyA, s.BranchA1, s.Actor), Guid.NewGuid(), draft.Id,
                    PetraERP.Application.GeneralLedger.Posting.ExpectedJournalVersion.FromBase64(Convert.ToBase64String(approved.RowVersion)), AuditReason.Create("ترحيل"));
                var closed = await Assert.ThrowsAsync<InvalidOperationException>(() => new PetraERP.Infrastructure.GeneralLedger.SqlPostingService(context)
                    .PostAsync(request, Cancellation)).ConfigureAwait(true);
                Assert.Equal(RuleViolation.PeriodClosed, RuleViolation.CodeOf(closed));
                Assert.Equal((byte)3, (await context.JournalEntries.AsNoTracking().SingleAsync(j => j.Id == draft.Id, Cancellation).ConfigureAwait(true)).State);

                await LedgerScenario.Calendar(context).TransitionPeriodAsync(s.Master(s.CompanyA), january.Id, PeriodTransition.Reopen, "إعادة فتح للتصحيح",
                    LedgerScenario.Token(january.VersionBase64), Cancellation).ConfigureAwait(true);
            }

            using (var context = database.Context())
            {
                var approved = await context.JournalEntries.AsNoTracking().SingleAsync(j => j.Id == draft.Id, Cancellation).ConfigureAwait(true);
                var request = PetraERP.Application.GeneralLedger.Posting.PostingRequest.Post(
                    PetraERP.Application.GeneralLedger.Posting.PostingRequestScope.Create(s.CompanyA, s.BranchA1, s.Actor), Guid.NewGuid(), draft.Id,
                    PetraERP.Application.GeneralLedger.Posting.ExpectedJournalVersion.FromBase64(Convert.ToBase64String(approved.RowVersion)), AuditReason.Create("ترحيل"));
                var receipt = await new PetraERP.Infrastructure.GeneralLedger.SqlPostingService(context).PostAsync(request, Cancellation).ConfigureAwait(true);
                Assert.Equal(1, receipt.PostingNumber.Sequence);
            }
        }
    }
}
