using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.Security;
using PetraERP.Domain.GeneralLedger;
using PetraERP.Infrastructure.GeneralLedger;
using PetraERP.Infrastructure.Persistence.Actors;

namespace PetraERP.Infrastructure.Tests;

public sealed class AccountingActorSecuritySqlTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task DirectPersistenceCannotUseDraftOrSalesOperationForUnrelatedWrites()
    {
        var fixture = await IndependentAccountingSqlFixture.CreateAsync().ConfigureAwait(true);
        await using (fixture.ConfigureAwait(true))
        {
            var result = await fixture.ProvisionAsync().ConfigureAwait(true);
            Assert.True(result.Succeeded, result.Error);
            using var sales = await fixture.Factory.CreateDbContextAsync(result.WorkspaceId, Cancellation).ConfigureAwait(true);
            var branch = await sales.Branches.Select(b => b.Id).SingleAsync(Cancellation).ConfigureAwait(true);
            sales.BindWriteScope(new(result.CompanyId, result.FiscalYearId, fixture.Actor, AccountingOperation.PostSalesInvoice, branch));
            var account = await sales.Accounts.FirstAsync(Cancellation).ConfigureAwait(true);
            account.NameArabic = "تعديل ممنوع من عملية بيع";
            var error = await Assert.ThrowsAsync<BusinessRuleException>(() => sales.SaveChangesAsync(Cancellation)).ConfigureAwait(true);
            Assert.Equal("accounting.operation_denied", error.Code);

            using var draft = await fixture.Factory.CreateDbContextAsync(result.WorkspaceId, Cancellation).ConfigureAwait(true);
            draft.BindWriteScope(new(result.CompanyId, result.FiscalYearId, fixture.Actor, AccountingOperation.ManageJournals, branch));
            draft.JournalEntries.Add(new JournalEntryRow { Id = Guid.NewGuid(), CompanyId = result.CompanyId, BranchId = branch,
                Description = "ترحيل غير مصرح", AccountingDate = new(2026, 2, 1), State = (byte)JournalEntryState.Posted });
            error = await Assert.ThrowsAsync<BusinessRuleException>(() => draft.SaveChangesAsync(Cancellation)).ConfigureAwait(true);
            Assert.Equal("accounting.operation_denied", error.Code);
            Assert.Equal(0, await draft.JournalEntries.CountAsync(Cancellation).ConfigureAwait(true));
            using var persisted = await fixture.Factory.CreateDbContextAsync(result.WorkspaceId, Cancellation).ConfigureAwait(true);
            Assert.NotEqual(account.NameArabic, (await persisted.Accounts.SingleAsync(a => a.Id == account.Id, Cancellation).ConfigureAwait(true)).NameArabic);
        }
    }

    [Theory(Explicit = true)]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "SqlServer")]
    public async Task SubsequentOperationRejectsDisabledOrPasswordResetActor(bool mustResetPassword)
    {
        var fixture = await IndependentAccountingSqlFixture.CreateAsync().ConfigureAwait(true);
        await using (fixture.ConfigureAwait(true))
        {
            var result = await fixture.ProvisionAsync().ConfigureAwait(true);
            Assert.True(result.Succeeded, result.Error);
            var actor = await fixture.System.Users.SingleAsync(u => u.Id == fixture.Actor, Cancellation).ConfigureAwait(true);
            if (mustResetPassword) actor.MustChangePassword = true; else actor.IsActive = false;
            await fixture.System.SaveChangesAsync(Cancellation).ConfigureAwait(true);
            using var denied = await fixture.Factory.CreateDbContextAsync(result.WorkspaceId, Cancellation).ConfigureAwait(true);
            denied.BindWriteScope(new(result.CompanyId, result.FiscalYearId, fixture.Actor, AccountingOperation.ManageAccounts));
            var account = await denied.Accounts.FirstAsync(Cancellation).ConfigureAwait(true);
            var original = account.NameArabic;
            account.NameArabic = "يجب ألا يحفظ";
            var error = await Assert.ThrowsAsync<BusinessRuleException>(() => denied.SaveChangesAsync(Cancellation)).ConfigureAwait(true);
            Assert.Equal("accounting.actor_denied", error.Code);
            await Assert.ThrowsAsync<InvalidOperationException>(() => denied.SaveChangesAsync(Cancellation)).ConfigureAwait(true);
            using var clean = await fixture.Factory.CreateDbContextAsync(result.WorkspaceId, Cancellation).ConfigureAwait(true);
            Assert.Equal(original, (await clean.Accounts.SingleAsync(a => a.Id == account.Id, Cancellation).ConfigureAwait(true)).NameArabic);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task ReadOnlyPermissionCannotBeUsedAsAccountWriteAuthority()
    {
        var fixture = await IndependentAccountingSqlFixture.CreateAsync().ConfigureAwait(true);
        await using (fixture.ConfigureAwait(true))
        {
            var result = await fixture.ProvisionAsync().ConfigureAwait(true);
            Assert.True(result.Succeeded, result.Error);
            var grants = await fixture.System.RolePermissions.Where(r => r.PermissionCode == PermissionCatalog.AccountsManage).ToListAsync(Cancellation).ConfigureAwait(true);
            fixture.System.RolePermissions.RemoveRange(grants);
            await fixture.System.SaveChangesAsync(Cancellation).ConfigureAwait(true);
            Assert.Throws<BusinessRuleException>(() => new AccountingWriteScope(result.CompanyId, result.FiscalYearId, fixture.Actor, PermissionCatalog.AccountsView));
            using var annual = await fixture.Factory.CreateDbContextAsync(result.WorkspaceId, Cancellation).ConfigureAwait(true);
            annual.BindWriteScope(new(result.CompanyId, result.FiscalYearId, fixture.Actor, AccountingOperation.ManageAccounts));
            annual.Accounts.Add(new AccountRow { Id = Guid.NewGuid(), CompanyId = result.CompanyId, Code = "999", NameArabic = "حساب ممنوع",
                AccountType = 1, CostCenterPolicy = 1, CurrencyMode = 1, IsActive = true });
            var error = await Assert.ThrowsAsync<BusinessRuleException>(() => annual.SaveChangesAsync(Cancellation)).ConfigureAwait(true);
            Assert.Equal("accounting.actor_denied", error.Code);
            Assert.Equal(5, await annual.Accounts.CountAsync(Cancellation).ConfigureAwait(true));
        }
    }
}
