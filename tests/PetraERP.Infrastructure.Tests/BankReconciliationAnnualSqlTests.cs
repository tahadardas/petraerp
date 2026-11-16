using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.Treasury;
using PetraERP.Infrastructure.Treasury;

namespace PetraERP.Infrastructure.Tests;

/// <summary>The production path: an annual workspace, where writes need a bound authority and every actor must be a verified reference.</summary>
public sealed class BankReconciliationAnnualSqlTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task StartDiscardAndAuditRunUnderTheBoundOperationAndRejectOtherScopes()
    {
        var fixture = await IndependentAccountingSqlFixture.CreateAsync().ConfigureAwait(true);
        await using (fixture.ConfigureAwait(true))
        {
            var result = await fixture.ProvisionAsync("BANKREC").ConfigureAwait(true);
            Assert.True(result.Succeeded, result.Error);
            using var annual = await fixture.Factory.CreateDbContextAsync(result.WorkspaceId, Cancellation).ConfigureAwait(true);
            var branch = await annual.Branches.AsNoTracking().Where(b => b.CompanyId == result.CompanyId).Select(b => b.Id).FirstAsync(Cancellation).ConfigureAwait(true);
            annual.BindWriteScope(new(result.CompanyId, result.FiscalYearId, fixture.Actor, PetraERP.Application.Security.PermissionCatalog.AccountsManage));
            var created = await new PetraERP.Infrastructure.GeneralLedger.SqlChartOfAccountsService(annual, TimeProvider.System).CreateAccountAsync(
                PetraERP.Application.GeneralLedger.ChartOfAccounts.LedgerMasterScope.Create(result.CompanyId, fixture.Actor),
                new PetraERP.Application.GeneralLedger.ChartOfAccounts.AccountInput
                {
                    Code = "1102", NameArabic = "بنك", AccountType = PetraERP.Application.GeneralLedger.ChartOfAccounts.AccountTypeCode.Assets, PostingAllowed = true,
                    CostCenterPolicy = PetraERP.Application.GeneralLedger.ChartOfAccounts.CostCenterPolicyCode.Optional,
                    CurrencyMode = PetraERP.Application.GeneralLedger.ChartOfAccounts.CurrencyModeCode.AnyEnabledCurrency,
                }, Cancellation).ConfigureAwait(true);
            var account = created.Id;

            // A context is bound to exactly one operation, so the reconciliation runs on its own context.
            using var reconciliationContext = await fixture.Factory.CreateDbContextAsync(result.WorkspaceId, Cancellation).ConfigureAwait(true);
            var service = new SqlBankReconciliationService(reconciliationContext);
            var started = await service.StartAsync(result.CompanyId, result.FiscalYearId, branch, fixture.Actor, account, new DateOnly(2026, 1, 31), 0m, Cancellation).ConfigureAwait(true);
            Assert.Equal(BankReconciliationStatus.Draft, started.Status);
            Assert.Equal(0m, started.Difference);
            // Zero difference with nothing cleared is a legitimate (empty) statement.
            var done = await service.CompleteAsync(result.CompanyId, result.FiscalYearId, branch, fixture.Actor, started.Id, started.VersionBase64, Cancellation).ConfigureAwait(true);
            Assert.Equal(BankReconciliationStatus.Completed, done.Status);

            // A completed reconciliation is immutable even for the same authorised actor (the mutation policy and the service agree).
            await Assert.ThrowsAsync<BusinessRuleException>(() => service.DiscardAsync(result.CompanyId, result.FiscalYearId, branch, fixture.Actor, started.Id, done.VersionBase64, Cancellation)).ConfigureAwait(true);
            Assert.Equal(1, await annual.BankReconciliations.AsNoTracking().CountAsync(Cancellation).ConfigureAwait(true));
        }
    }
}
