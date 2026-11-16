using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.ChartOfAccounts;
using PetraERP.Application.CompanyOptions;
using PetraERP.Application.Parties;
using PetraERP.Domain.GeneralLedger;
using PetraERP.Domain.Parties;
using PetraERP.Infrastructure.CompanyOptions;
using PetraERP.Infrastructure.Parties;

namespace PetraERP.Infrastructure.Tests;

/// <summary>
/// The company policy for party accounts: a customer saved without an account gets its own account under the configured
/// heading, the preview states exactly that beforehand, an invalid heading is refused, and a shared account is never renamed.
/// </summary>
public sealed class PartyAccountPolicySqlTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static async Task<Guid> FiscalYearAsync(PetraERP.Infrastructure.Persistence.PetraDbContext context, Guid company) =>
        await context.FiscalYears.AsNoTracking().Where(y => y.CompanyId == company).Select(y => y.Id).FirstAsync(Cancellation).ConfigureAwait(true);

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task ACustomerWithoutAnAccountGetsItsOwnAccountUnderTheConfiguredHeadingAndThePreviewSaysSoFirst()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var scenario = await LedgerScenario.CreateAsync(database).ConfigureAwait(true);
            var heading = await scenario.AccountAsync(scenario.CompanyA, "1150", AccountTypeCode.Assets, false).ConfigureAwait(true);
            var posting = await scenario.AccountAsync(scenario.CompanyA, "1190", AccountTypeCode.Assets, true).ConfigureAwait(true);
            using var context = database.Context();
            var options = new SqlCompanyOptionsService(context);
            var year = await FiscalYearAsync(context, scenario.CompanyA).ConfigureAwait(true);
            var current = await options.GetAsync(scenario.CompanyA, Cancellation).ConfigureAwait(true);

            // A posting account is not a heading: refused, nothing saved.
            var invalid = await Assert.ThrowsAsync<BusinessRuleException>(() => options.SaveAsync(scenario.CompanyA, year, scenario.Actor,
                current.Policy with { CustomersParentAccountId = posting.Id }, current.Version, "تحديد حساب الزبائن", Cancellation)).ConfigureAwait(true);
            Assert.Equal("company.party_parent_account_invalid", invalid.Code);
            context.ChangeTracker.Clear();

            var saved = await options.SaveAsync(scenario.CompanyA, year, scenario.Actor,
                current.Policy with { CustomersParentAccountId = heading.Id }, current.Version, "تحديد حساب الزبائن", Cancellation).ConfigureAwait(true);
            Assert.Equal(heading.Id, saved.Policy.CustomersParentAccountId);
            Assert.Equal(heading.Id, (await options.GetAsync(scenario.CompanyA, Cancellation).ConfigureAwait(true)).Policy.CustomersParentAccountId);

            var parties = new SqlPartyService(context);
            var plan = await parties.GetAccountPlanAsync(scenario.CompanyA, Cancellation).ConfigureAwait(true);
            Assert.Equal(heading.Id, plan.Receivable.ParentAccountId);
            Assert.True(plan.Receivable.FromCompanyPolicy);
            Assert.False(plan.Receivable.ParentWillBeCreated);
            Assert.Equal("1150001", plan.Receivable.NextAccountCode);
            Assert.False(plan.Payable.FromCompanyPolicy); // suppliers were not configured: the chart convention still applies

            var accountsBefore = await context.Accounts.AsNoTracking().CountAsync(a => a.CompanyId == scenario.CompanyA, Cancellation).ConfigureAwait(true);
            var customer = await parties.CreatePartyAsync(scenario.CompanyA, new CreatePartyCommand("C-POL", "زبون السياسة", null, PartyRole.Customer)
                { ChangedByUserId = scenario.Actor, ChangedInBranchId = scenario.BranchA1 }, Cancellation).ConfigureAwait(true);

            var account = await context.Accounts.AsNoTracking().SingleAsync(a => a.Id == customer.ReceivableAccountId, Cancellation).ConfigureAwait(true);
            Assert.Equal(heading.Id, account.ParentId);
            Assert.Equal(plan.Receivable.NextAccountCode, account.Code); // exactly what the preview promised
            Assert.Equal("زبون السياسة", account.NameArabic);
            Assert.True(account.PostingAllowed);
            Assert.Equal(accountsBefore + 1, await context.Accounts.AsNoTracking().CountAsync(a => a.CompanyId == scenario.CompanyA, Cancellation).ConfigureAwait(true));
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task ASharedAccountKeepsItsNameWhenOneOfItsPartiesIsRenamed()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var scenario = await LedgerScenario.CreateAsync(database).ConfigureAwait(true);
            var shared = await scenario.AccountAsync(scenario.CompanyA, "1160", AccountTypeCode.Assets, true).ConfigureAwait(true);
            using var context = database.Context();
            var parties = new SqlPartyService(context);
            var sharedName = (await context.Accounts.AsNoTracking().SingleAsync(a => a.Id == shared.Id, Cancellation).ConfigureAwait(true)).NameArabic;
            PartyDetailResult first = await parties.CreatePartyAsync(scenario.CompanyA, new CreatePartyCommand("C-SH1", "زبون أول", null, PartyRole.Customer, ReceivableAccountId: shared.Id)
                { ChangedByUserId = scenario.Actor, ChangedInBranchId = scenario.BranchA1 }, Cancellation).ConfigureAwait(true);
            await parties.CreatePartyAsync(scenario.CompanyA, new CreatePartyCommand("C-SH2", "زبون ثان", null, PartyRole.Customer, ReceivableAccountId: shared.Id)
                { ChangedByUserId = scenario.Actor, ChangedInBranchId = scenario.BranchA1 }, Cancellation).ConfigureAwait(true);

            await parties.UpdatePartyAsync(scenario.CompanyA, first.Id, new UpdatePartyCommand("زبون أول باسم جديد", null, PartyRole.Customer, ReceivableAccountId: shared.Id)
                { ExpectedVersion = first.VersionToken, ChangedByUserId = scenario.Actor, ChangedInBranchId = scenario.BranchA1 }, Cancellation).ConfigureAwait(true);

            context.ChangeTracker.Clear();
            Assert.Equal(sharedName, (await context.Accounts.AsNoTracking().SingleAsync(a => a.Id == shared.Id, Cancellation).ConfigureAwait(true)).NameArabic);
        }
    }
}
