#pragma warning disable CA1707

using Microsoft.EntityFrameworkCore;
using PetraERP.Application.GeneralLedger.OpeningBalances;
using PetraERP.Domain.GeneralLedger;
using PetraERP.Infrastructure.GeneralLedger;
using PetraERP.Infrastructure.GeneralLedger.OpeningBalances;
using PetraERP.Infrastructure.GeneralLedger.Reconciliation;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Persistence.Actors;

namespace PetraERP.Infrastructure.Tests;

/// <summary>
/// P02: cross-year continuity. A rollover opening batch is compared with the previous year's closing balances read through the trusted
/// <see cref="IPriorYearClosingSource"/>. When that read cannot run the outcome is UNVERIFIED (WARNING), never PASS; a real mismatch is BLOCKING.
/// </summary>
public sealed class AccountingReconciliationRolloverSqlTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private sealed class FakePriorYear(PriorYearClosingResult result) : IPriorYearClosingSource
    {
        public Task<PriorYearClosingResult> LoadAsync(Guid companyId, Guid fiscalYearId, string? sourceYearCode, CancellationToken cancellationToken) => Task.FromResult(result);
    }

    private static PriorYearClosingResult Loaded(Guid cash, decimal cashNet, Guid capital, decimal capitalNet, decimal netIncome) =>
        new(PriorYearStatus.Loaded, new(Guid.NewGuid(), "2025", new Dictionary<Guid, decimal> { [cash] = cashNet, [capital] = capitalNet }, netIncome), null);

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task RolloverContinuity_IsUnverifiedWithoutTrustedSource_AndBlockingOnMismatch()
    {
        var fixture = await IndependentAccountingSqlFixture.CreateAsync().ConfigureAwait(true);
        await using (fixture.ConfigureAwait(true))
        {
            var provisioned = await fixture.ProvisionAsync().ConfigureAwait(true);
            Assert.True(provisioned.Succeeded, provisioned.Error);
            Guid companyId, branchId;
            var cashId = Guid.NewGuid();
            var capitalId = Guid.NewGuid();
            using (var setup = await fixture.Factory.CreateDbContextAsync(provisioned.WorkspaceId, Cancellation).ConfigureAwait(true))
            {
                companyId = (await setup.Companies.SingleAsync(Cancellation).ConfigureAwait(true)).Id;
                branchId = (await setup.Branches.SingleAsync(Cancellation).ConfigureAwait(true)).Id;
                setup.BindWriteScope(new(companyId, provisioned.FiscalYearId, fixture.Actor, AccountingOperation.ManageAccounts, branchId));
                setup.Accounts.AddRange(
                    new AccountRow { Id = cashId, CompanyId = companyId, Code = "1011", NameArabic = "الصندوق", NameEnglish = "Cash", AccountType = (byte)AccountType.Assets, PostingAllowed = true, CostCenterPolicy = 1, CurrencyMode = 1, IsActive = true, FxMonetaryEligibility = 1 },
                    new AccountRow { Id = capitalId, CompanyId = companyId, Code = "3011", NameArabic = "رأس المال", NameEnglish = "Capital", AccountType = (byte)AccountType.Equity, PostingAllowed = true, CostCenterPolicy = 1, CurrencyMode = 1, IsActive = true, FxMonetaryEligibility = 2 });
                await setup.SaveChangesAsync(Cancellation).ConfigureAwait(true);
            }

            async Task<ReconciliationFindingDtoView> RunAsync(IPriorYearClosingSource? source)
            {
                using var context = await fixture.Factory.CreateDbContextAsync(provisioned.WorkspaceId, Cancellation).ConfigureAwait(true);
                var report = await new SqlAccountingReconciliationService(context, TimeProvider.System, source)
                    .RunAllReconciliationsAsync(companyId, provisioned.FiscalYearId, branchId, Cancellation).ConfigureAwait(true);
                var finding = report.Findings.Single(f => f.CheckCode == "CHECK_11_YEAR_ROLLOVER_CONTINUITY");
                return new(finding.Severity, finding.Explanation);
            }

            // No opening batch at all: a first year can only be PROVEN by a trusted source; absent one, the answer is UNVERIFIED.
            var noSource = await RunAsync(null).ConfigureAwait(true);
            Assert.True(noSource.Severity == "WARNING" && noSource.Explanation.Contains("UNVERIFIED", StringComparison.Ordinal), $"{noSource.Severity}: {noSource.Explanation}");
            var firstYear = await RunAsync(new FakePriorYear(new(PriorYearStatus.NoPredecessor, null, null))).ConfigureAwait(true);
            Assert.True(firstYear.Severity == "PASS" && firstYear.Explanation.Contains("NO_DATA", StringComparison.Ordinal), $"{firstYear.Severity}: {firstYear.Explanation}");
            var predecessorWithoutBatch = await RunAsync(new FakePriorYear(Loaded(cashId, 1000m, capitalId, -1000m, 0m))).ConfigureAwait(true);
            Assert.Equal("BLOCKING", predecessorWithoutBatch.Severity);

            // A real, posted Rollover batch: cash 1000 / capital 1000.
            var command = new CreateOpeningBalanceBatchCommand(new DateOnly(2026, 1, 31), "Rollover", "2025", new string('A', 64), "USD", "USD", branchId, "ROLL-IDEMP-01",
                [new OpeningBalanceLineInput(cashId, null, "USD", 1000m, 0m, 1m, 1000m, 0m, "Cash", 1), new OpeningBalanceLineInput(capitalId, null, "USD", 0m, 1000m, 1m, 0m, 1000m, "Capital", 2)]);
            OpeningBalanceBatchDto batch;
            using (var draft = await fixture.Factory.CreateDbContextAsync(provisioned.WorkspaceId, Cancellation).ConfigureAwait(true))
            {
                draft.BindWriteScope(new(companyId, provisioned.FiscalYearId, fixture.Actor, AccountingOperation.ManageJournals, branchId));
                batch = await new SqlOpeningBalanceService(draft, TimeProvider.System).CreateDraftAsync(companyId, provisioned.FiscalYearId, fixture.Actor, command, Cancellation).ConfigureAwait(true);
            }
            using (var approve = await fixture.Factory.CreateDbContextAsync(provisioned.WorkspaceId, Cancellation).ConfigureAwait(true))
            {
                approve.BindWriteScope(new(companyId, provisioned.FiscalYearId, fixture.Actor, AccountingOperation.ApproveJournal, branchId));
                batch = await new SqlOpeningBalanceService(approve, TimeProvider.System).ApproveAsync(companyId, batch.Id, fixture.Actor, "approve", Cancellation).ConfigureAwait(true);
            }
            using (var post = await fixture.Factory.CreateDbContextAsync(provisioned.WorkspaceId, Cancellation).ConfigureAwait(true))
            {
                post.BindWriteScope(new(companyId, provisioned.FiscalYearId, fixture.Actor, AccountingOperation.PostJournal, branchId));
                batch = await new SqlOpeningBalanceService(post, TimeProvider.System).PostAsync(companyId, batch.Id, fixture.Actor, "post", Cancellation).ConfigureAwait(true);
            }
            Assert.Equal((byte)OpeningBalanceBatchState.Posted, batch.ApprovalState);

            // Internal integrity alone is not proof of cross-year continuity.
            var internalOnly = await RunAsync(null).ConfigureAwait(true);
            Assert.True(internalOnly.Severity == "WARNING" && internalOnly.Explanation.Contains("UNVERIFIED", StringComparison.Ordinal), $"{internalOnly.Severity}: {internalOnly.Explanation}");
            var unreadable = await RunAsync(new FakePriorYear(new(PriorYearStatus.Unavailable, null, "previous-year database could not be read"))).ConfigureAwait(true);
            Assert.True(unreadable.Severity == "WARNING" && unreadable.Explanation.Contains("UNVERIFIED", StringComparison.Ordinal), $"{unreadable.Severity}: {unreadable.Explanation}");

            // Matching closing: PASS. Any difference in an account, or income that was not carried to retained earnings: BLOCKING.
            var matching = await RunAsync(new FakePriorYear(Loaded(cashId, 1000m, capitalId, -1000m, 0m))).ConfigureAwait(true);
            Assert.True(matching.Severity == "PASS", $"{matching.Severity}: {matching.Explanation}");
            var oneCent = await RunAsync(new FakePriorYear(Loaded(cashId, 1000.01m, capitalId, -1000m, 0m))).ConfigureAwait(true);
            Assert.True(oneCent.Severity == "BLOCKING", $"{oneCent.Severity}: {oneCent.Explanation}");
            var missingAccount = await RunAsync(new FakePriorYear(new(PriorYearStatus.Loaded, new(Guid.NewGuid(), "2025", new Dictionary<Guid, decimal> { [cashId] = 1000m, [capitalId] = -1000m, [Guid.NewGuid()] = 5m }, 0m), null))).ConfigureAwait(true);
            Assert.True(missingAccount.Severity == "BLOCKING", $"{missingAccount.Severity}: {missingAccount.Explanation}");
            var incomeNotCarried = await RunAsync(new FakePriorYear(Loaded(cashId, 1000m, capitalId, -1000m, 50m))).ConfigureAwait(true);
            Assert.True(incomeNotCarried.Severity == "BLOCKING", $"{incomeNotCarried.Severity}: {incomeNotCarried.Explanation}");
        }
    }

    private sealed record ReconciliationFindingDtoView(string Severity, string Explanation);
}
