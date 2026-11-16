#pragma warning disable CA1707

using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.OpeningBalances;
using PetraERP.Domain.GeneralLedger;
using PetraERP.Infrastructure.GeneralLedger;
using PetraERP.Infrastructure.GeneralLedger.OpeningBalances;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Persistence.Actors;

namespace PetraERP.Infrastructure.Tests;

public sealed class OpeningBalanceSqlTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static SqlOpeningBalanceService CreateService(FinancialDbContext context) => new(context, TimeProvider.System);

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task OpeningBalanceBatch_Lifecycle_Draft_Validate_Approve_Post_Reverse()
    {
        var fixture = await IndependentAccountingSqlFixture.CreateAsync().ConfigureAwait(true);
        await using (fixture.ConfigureAwait(true))
        {
            var provisioned = await fixture.ProvisionAsync().ConfigureAwait(true);
            Assert.True(provisioned.Succeeded, provisioned.Error);

            Guid companyId;
            Guid branchId;
            Guid cashId = Guid.NewGuid();
            Guid capitalId = Guid.NewGuid();

            using (var setupContext = await fixture.Factory.CreateDbContextAsync(provisioned.WorkspaceId, Cancellation).ConfigureAwait(true))
            {
                var company = await setupContext.Companies.SingleAsync(Cancellation).ConfigureAwait(true);
                var branch = await setupContext.Branches.SingleAsync(Cancellation).ConfigureAwait(true);
                companyId = company.Id;
                branchId = branch.Id;

                setupContext.BindWriteScope(new(companyId, provisioned.FiscalYearId, fixture.Actor, AccountingOperation.ManageAccounts, branchId));
                var cash = new AccountRow
                {
                    Id = cashId,
                    CompanyId = companyId,
                    Code = "1011",
                    NameArabic = "الصندوق الرئيسي",
                    NameEnglish = "Main Cash",
                    AccountType = (byte)AccountType.Assets,
                    PostingAllowed = true,
                    CostCenterPolicy = 1,
                    CurrencyMode = 1,
                    IsActive = true,
                    FxMonetaryEligibility = 1
                };
                var capital = new AccountRow
                {
                    Id = capitalId,
                    CompanyId = companyId,
                    Code = "3011",
                    NameArabic = "رأس المال",
                    NameEnglish = "Share Capital",
                    AccountType = (byte)AccountType.Equity,
                    PostingAllowed = true,
                    CostCenterPolicy = 1,
                    CurrencyMode = 1,
                    IsActive = true,
                    FxMonetaryEligibility = 2
                };
                setupContext.Accounts.AddRange(cash, capital);
                await setupContext.SaveChangesAsync(Cancellation).ConfigureAwait(true);
            }

            var command = new CreateOpeningBalanceBatchCommand(
                OpeningDate: new DateOnly(2026, 1, 31),
                SourceType: "MANUAL",
                SourceReference: "OPEN-2026-01",
                SourceFingerprint: "FINGERPRINT-01",
                OriginalCurrency: "USD",
                BaseCurrency: "USD",
                BranchId: branchId,
                IdempotencyKey: "OPEN-IDEMP-01",
                Lines: [
                    new OpeningBalanceLineInput(cashId, null, "USD", 1000m, 0m, 1m, 1000m, 0m, "Opening cash", 1),
                    new OpeningBalanceLineInput(capitalId, null, "USD", 0m, 1000m, 1m, 0m, 1000m, "Opening capital", 2)
                ]);

            // 1. Create draft batch
            OpeningBalanceBatchDto draft;
            using (var draftContext = await fixture.Factory.CreateDbContextAsync(provisioned.WorkspaceId, Cancellation).ConfigureAwait(true))
            {
                draftContext.BindWriteScope(new(companyId, provisioned.FiscalYearId, fixture.Actor, AccountingOperation.ManageJournals, branchId));
                var service = CreateService(draftContext);
                draft = await service.CreateDraftAsync(companyId, provisioned.FiscalYearId, fixture.Actor, command, Cancellation).ConfigureAwait(true);
            }
            Assert.NotNull(draft);
            Assert.Equal((byte)OpeningBalanceBatchState.Validated, draft.ApprovalState);
            Assert.True(draft.IsBalanced);
            Assert.Equal(1000m, draft.TotalDebitBase);
            Assert.Equal(1000m, draft.TotalCreditBase);

            // 2. Validate
            using (var validateContext = await fixture.Factory.CreateDbContextAsync(provisioned.WorkspaceId, Cancellation).ConfigureAwait(true))
            {
                validateContext.BindWriteScope(new(companyId, provisioned.FiscalYearId, fixture.Actor, AccountingOperation.ManageJournals, branchId));
                var service = CreateService(validateContext);
                var validated = await service.ValidateAsync(companyId, draft.Id, Cancellation).ConfigureAwait(true);
                Assert.True(validated.IsBalanced);
            }

            // 3. Approve
            OpeningBalanceBatchDto approved;
            using (var approveContext = await fixture.Factory.CreateDbContextAsync(provisioned.WorkspaceId, Cancellation).ConfigureAwait(true))
            {
                approveContext.BindWriteScope(new(companyId, provisioned.FiscalYearId, fixture.Actor, AccountingOperation.ApproveJournal, branchId));
                var service = CreateService(approveContext);
                approved = await service.ApproveAsync(companyId, draft.Id, fixture.Actor, "Approved opening balances", Cancellation).ConfigureAwait(true);
            }
            Assert.Equal((byte)OpeningBalanceBatchState.Approved, approved.ApprovalState);
            Assert.NotNull(approved.ApprovedAtUtc);

            // 4. Post
            OpeningBalanceBatchDto posted;
            using (var postContext = await fixture.Factory.CreateDbContextAsync(provisioned.WorkspaceId, Cancellation).ConfigureAwait(true))
            {
                postContext.BindWriteScope(new(companyId, provisioned.FiscalYearId, fixture.Actor, AccountingOperation.PostJournal, branchId));
                var service = CreateService(postContext);
                posted = await service.PostAsync(companyId, draft.Id, fixture.Actor, "Posting opening balances", Cancellation).ConfigureAwait(true);
            }
            Assert.Equal((byte)OpeningBalanceBatchState.Posted, posted.ApprovalState);
            Assert.NotNull(posted.PostedJournalId);

            // Verify journal exists and is posted
            using (var readContext = await fixture.Factory.CreateDbContextAsync(provisioned.WorkspaceId, Cancellation).ConfigureAwait(true))
            {
                var journal = await readContext.JournalEntries.Include(j => j.Lines).SingleAsync(j => j.Id == posted.PostedJournalId, Cancellation).ConfigureAwait(true);
                Assert.Equal((byte)JournalEntryState.Posted, journal.State); // Posted
                Assert.Equal(2, journal.Lines.Count);
                Assert.Equal(1000m, journal.Lines.Sum(l => l.BaseDebit));
                Assert.Equal(1000m, journal.Lines.Sum(l => l.BaseCredit));
            }

            // 5. Reverse
            OpeningBalanceBatchDto reversed;
            using (var reverseContext = await fixture.Factory.CreateDbContextAsync(provisioned.WorkspaceId, Cancellation).ConfigureAwait(true))
            {
                reverseContext.BindWriteScope(new(companyId, provisioned.FiscalYearId, fixture.Actor, AccountingOperation.ReverseJournal, branchId));
                var service = CreateService(reverseContext);
                reversed = await service.ReverseAsync(companyId, draft.Id, fixture.Actor, "Reversing opening balances", Cancellation).ConfigureAwait(true);
            }
            Assert.Equal((byte)OpeningBalanceBatchState.Reversed, reversed.ApprovalState);
            Assert.NotNull(reversed.ReversalJournalId);

            using (var readContext = await fixture.Factory.CreateDbContextAsync(provisioned.WorkspaceId, Cancellation).ConfigureAwait(true))
            {
                var reversalJournal = await readContext.JournalEntries.SingleAsync(j => j.Id == reversed.ReversalJournalId, Cancellation).ConfigureAwait(true);
                Assert.Equal((byte)JournalEntryState.Posted, reversalJournal.State); // Posted reversal
            }
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task OpeningBalance_RejectsUnbalancedBatch()
    {
        var fixture = await IndependentAccountingSqlFixture.CreateAsync().ConfigureAwait(true);
        await using (fixture.ConfigureAwait(true))
        {
            var provisioned = await fixture.ProvisionAsync().ConfigureAwait(true);
            Assert.True(provisioned.Succeeded, provisioned.Error);

            Guid companyId;
            Guid branchId;
            Guid cashId = Guid.NewGuid();
            Guid capitalId = Guid.NewGuid();

            using (var setupContext = await fixture.Factory.CreateDbContextAsync(provisioned.WorkspaceId, Cancellation).ConfigureAwait(true))
            {
                var company = await setupContext.Companies.SingleAsync(Cancellation).ConfigureAwait(true);
                var branch = await setupContext.Branches.SingleAsync(Cancellation).ConfigureAwait(true);
                companyId = company.Id;
                branchId = branch.Id;

                setupContext.BindWriteScope(new(companyId, provisioned.FiscalYearId, fixture.Actor, AccountingOperation.ManageAccounts, branchId));
                var cash = new AccountRow
                {
                    Id = cashId,
                    CompanyId = companyId,
                    Code = "1011",
                    NameArabic = "الصندوق الرئيسي",
                    NameEnglish = "Main Cash",
                    AccountType = (byte)AccountType.Assets,
                    PostingAllowed = true,
                    CostCenterPolicy = 1,
                    CurrencyMode = 1,
                    IsActive = true,
                    FxMonetaryEligibility = 1
                };
                var capital = new AccountRow
                {
                    Id = capitalId,
                    CompanyId = companyId,
                    Code = "3011",
                    NameArabic = "رأس المال",
                    NameEnglish = "Share Capital",
                    AccountType = (byte)AccountType.Equity,
                    PostingAllowed = true,
                    CostCenterPolicy = 1,
                    CurrencyMode = 1,
                    IsActive = true,
                    FxMonetaryEligibility = 2
                };
                setupContext.Accounts.AddRange(cash, capital);
                await setupContext.SaveChangesAsync(Cancellation).ConfigureAwait(true);
            }

            // Unbalanced command (1000 debit vs 800 credit)
            var command = new CreateOpeningBalanceBatchCommand(
                OpeningDate: new DateOnly(2026, 1, 31),
                SourceType: "MANUAL",
                SourceReference: "UNBALANCED",
                SourceFingerprint: "FP-02",
                OriginalCurrency: "USD",
                BaseCurrency: "USD",
                BranchId: branchId,
                IdempotencyKey: "OPEN-IDEMP-UNBAL",
                Lines: [
                    new OpeningBalanceLineInput(cashId, null, "USD", 1000m, 0m, 1m, 1000m, 0m, "Opening cash", 1),
                    new OpeningBalanceLineInput(capitalId, null, "USD", 0m, 800m, 1m, 0m, 800m, "Opening capital", 2)
                ]);

            OpeningBalanceBatchDto draft;
            using (var draftContext = await fixture.Factory.CreateDbContextAsync(provisioned.WorkspaceId, Cancellation).ConfigureAwait(true))
            {
                draftContext.BindWriteScope(new(companyId, provisioned.FiscalYearId, fixture.Actor, AccountingOperation.ManageJournals, branchId));
                var service = CreateService(draftContext);
                draft = await service.CreateDraftAsync(companyId, provisioned.FiscalYearId, fixture.Actor, command, Cancellation).ConfigureAwait(true);
            }
            Assert.False(draft.IsBalanced);

            // Validating or approving unbalanced batch must throw BusinessRuleException
            using (var validateContext = await fixture.Factory.CreateDbContextAsync(provisioned.WorkspaceId, Cancellation).ConfigureAwait(true))
            {
                var service = CreateService(validateContext);
                var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => service.ValidateAsync(companyId, draft.Id, Cancellation)).ConfigureAwait(true);
                Assert.Equal("opening_balances.unbalanced", ex.Code);
            }
        }
    }
}
