using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.ChartOfAccounts;
using PetraERP.Infrastructure.GeneralLedger;

namespace PetraERP.Infrastructure.Tests;

public sealed class AccountHistorySqlTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;
    private static AccountInput Input(string name) => new()
    {
        Code = "1010", NameArabic = name, NameEnglish = null, AccountType = AccountTypeCode.Assets,
        PostingAllowed = true, CostCenterPolicy = CostCenterPolicyCode.Optional, CurrencyMode = CurrencyModeCode.BaseCurrencyOnly,
    };

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task UpgradePreservesLegacyDeletionAndDowngradeCannotDiscardNewAuditEvidence()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var scenario = await LedgerScenario.CreateAsync(database).ConfigureAwait(true);
            using var context = database.Context();
            var migrator = context.GetService<IMigrator>();
            const string previous = "20261018142409_ChartOfAccountsDeletionAudit";
            await migrator.MigrateAsync(previous, Cancellation).ConfigureAwait(true);
            var id = Guid.NewGuid(); var account = Guid.NewGuid(); var correlation = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
            await context.Database.ExecuteSqlInterpolatedAsync($"INSERT [ledger].[AccountAudit] ([EventId],[CompanyId],[AccountId],[Action],[Code],[NameArabic],[AccountType],[ActorUserId],[CorrelationId],[Reason],[OccurredAtUtc]) VALUES ({id},{scenario.CompanyA},{account},1,N'OLD',N'حذف سابق',1,{scenario.Actor},{correlation},N'سجل قديم',{now});", Cancellation).ConfigureAwait(true);
            await context.Database.MigrateAsync(Cancellation).ConfigureAwait(true);
            var chart = LedgerScenario.Chart(context); var scope = scenario.Master(scenario.CompanyA);
            var legacy = Assert.Single((await chart.GetAccountHistoryAsync(scope, account, PageRequest.Create(1, 10), Cancellation).ConfigureAwait(true)).Items);
            Assert.Equal((id, "حذف سابق", "سجل قديم", now), (legacy.EventId, legacy.NameArabic, legacy.Reason, legacy.OccurredAtUtc));
            Assert.Null(legacy.Before); Assert.Null(legacy.After);
            var created = await chart.CreateAccountAsync(scope, Input("حساب بعد الترقية"), Cancellation).ConfigureAwait(true);
            var failure = await Assert.ThrowsAnyAsync<Exception>(() => migrator.MigrateAsync(previous, Cancellation)).ConfigureAwait(true);
            Assert.Contains("instead of discarding audit history", failure.Message, StringComparison.Ordinal);
            Assert.Single((await chart.GetAccountHistoryAsync(scope, created.Id, PageRequest.Create(1, 10), Cancellation).ConfigureAwait(true)).Items);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task AccountHistoryKeepsBothStatesActorScopePagingAndDeletedIdentityWithoutAuditingFailedOrUnchangedWrites()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var scenario = await LedgerScenario.CreateAsync(database).ConfigureAwait(true);
            using var context = database.Context();
            var chart = LedgerScenario.Chart(context);
            var correlation = Guid.NewGuid();
            var scope = LedgerMasterScope.Create(scenario.CompanyA, scenario.Actor, scenario.BranchA1, correlation);
            var account = await chart.CreateAccountAsync(scope, Input("صندوق المَزّة"), Cancellation).ConfigureAwait(true);
            var first = Assert.Single((await chart.GetAccountHistoryAsync(scope, account.Id, PageRequest.Create(1, 2), Cancellation).ConfigureAwait(true)).Items);
            Assert.Equal(AccountAuditActionCode.Created, first.Action); Assert.Null(first.Before);
            Assert.Equal("صندوق المَزّة", first.After!.NameArabic); Assert.Null(first.After.NameEnglish);
            Assert.Equal((scenario.Actor, scenario.BranchA1, correlation), (first.ActorUserId, first.WorkingBranchId, first.CorrelationId));
            var originalVersion = account.VersionBase64;
            account = await chart.UpdateAccountAsync(scope, account.Id, Input("الصندوق الرئيسي") with { CostCenterPolicy = CostCenterPolicyCode.Required },
                LedgerScenario.Token(account.VersionBase64), Cancellation).ConfigureAwait(true);
            var changed = (await chart.GetAccountHistoryAsync(scope, account.Id, PageRequest.Create(1, 2), Cancellation).ConfigureAwait(true)).Items[0];
            Assert.Equal(AccountAuditActionCode.Updated, changed.Action);
            Assert.Equal("صندوق المَزّة", changed.Before!.NameArabic); Assert.Equal("الصندوق الرئيسي", changed.After!.NameArabic);
            Assert.Equal((CostCenterPolicyCode.Optional, CostCenterPolicyCode.Required), (changed.Before.CostCenterPolicy, changed.After.CostCenterPolicy));
            await Assert.ThrowsAsync<ConcurrencyConflictException>(() => chart.UpdateAccountAsync(scope, account.Id, Input("نسخة متعارضة"),
                LedgerScenario.Token(originalVersion), Cancellation)).ConfigureAwait(true);
            account = await chart.UpdateAccountAsync(scope, account.Id, Input("الصندوق الرئيسي") with { CostCenterPolicy = CostCenterPolicyCode.Required },
                LedgerScenario.Token(account.VersionBase64), Cancellation).ConfigureAwait(true);
            account = await chart.SetAccountActiveAsync(scope, account.Id, false, LedgerScenario.Token(account.VersionBase64), Cancellation).ConfigureAwait(true);
            account = await chart.SetAccountActiveAsync(scope, account.Id, false, LedgerScenario.Token(account.VersionBase64), Cancellation).ConfigureAwait(true);
            account = await chart.SetAccountActiveAsync(scope, account.Id, true, LedgerScenario.Token(account.VersionBase64), Cancellation).ConfigureAwait(true);
            await chart.DeleteAccountAsync(scope, account.Id, LedgerScenario.Token(account.VersionBase64), "أُنشئ بالخطأ", Cancellation).ConfigureAwait(true);
            var page = await chart.GetAccountHistoryAsync(scope, account.Id, PageRequest.Create(1, 2), Cancellation).ConfigureAwait(true);
            Assert.Equal(5, page.TotalCount); Assert.Equal(2, page.Items.Count);
            Assert.Equal([AccountAuditActionCode.Deleted, AccountAuditActionCode.Activated], page.Items.Select(item => item.Action));
            Assert.True(page.Items[0].Before!.IsActive); Assert.Null(page.Items[0].After);
            var next = await chart.GetAccountHistoryAsync(scope, account.Id, PageRequest.Create(2, 2), Cancellation).ConfigureAwait(true);
            Assert.Equal([AccountAuditActionCode.Deactivated, AccountAuditActionCode.Updated], next.Items.Select(item => item.Action));
            Assert.Empty(page.Items.Select(item => item.EventId).Intersect(next.Items.Select(item => item.EventId)));
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => chart.GetAccountHistoryAsync(scenario.Master(scenario.CompanyB), account.Id,
                PageRequest.Create(1, 2), Cancellation)).ConfigureAwait(true);
            await Assert.ThrowsAnyAsync<Exception>(() => context.Database.ExecuteSqlRawAsync("UPDATE [ledger].[AccountAuditStates] SET [NameArabic]=N'تلاعب'", Cancellation)).ConfigureAwait(true);
            await Assert.ThrowsAnyAsync<Exception>(() => context.Database.ExecuteSqlRawAsync("DELETE FROM [ledger].[AccountAuditStates]", Cancellation)).ConfigureAwait(true);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task AuditWriteFailureRollsBackAccountCreationAndUpdateAndLegacyDeletionsStayExplicitlyIncomplete()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var scenario = await LedgerScenario.CreateAsync(database).ConfigureAwait(true);
            using var context = database.Context(); var chart = LedgerScenario.Chart(context); var scope = scenario.Master(scenario.CompanyA);
            var account = await chart.CreateAccountAsync(scope, Input("قبل الرفض"), Cancellation).ConfigureAwait(true);
            await context.Database.ExecuteSqlRawAsync("CREATE TRIGGER [ledger].[TR_Test_RejectAccountAudit] ON [ledger].[AccountAudit] AFTER INSERT AS THROW 51020, 'Test audit rejection', 1;", Cancellation).ConfigureAwait(true);
            await Assert.ThrowsAnyAsync<Exception>(() => chart.CreateAccountAsync(scope, Input("مرفوض") with { Code = "1020" }, Cancellation)).ConfigureAwait(true);
            context.ChangeTracker.Clear();
            await Assert.ThrowsAnyAsync<Exception>(() => chart.UpdateAccountAsync(scope, account.Id, Input("مرفوض"), LedgerScenario.Token(account.VersionBase64), Cancellation)).ConfigureAwait(true);
            context.ChangeTracker.Clear();
            Assert.False(await context.Accounts.AnyAsync(row => row.Code == "1020", Cancellation).ConfigureAwait(true));
            Assert.Equal("قبل الرفض", (await chart.GetAccountAsync(scope, account.Id, Cancellation).ConfigureAwait(true)).NameArabic);
            Assert.Single((await chart.GetAccountHistoryAsync(scope, account.Id, PageRequest.Create(1, 10), Cancellation).ConfigureAwait(true)).Items);
            await context.Database.ExecuteSqlRawAsync("DROP TRIGGER [ledger].[TR_Test_RejectAccountAudit];", Cancellation).ConfigureAwait(true);
            var legacyId = Guid.NewGuid();
            context.AccountAudit.Add(new AccountAuditRow { EventId = Guid.NewGuid(), CompanyId = scope.CompanyId, AccountId = legacyId,
                Action = AccountAuditRow.Deleted, Code = "OLD", NameArabic = "حساب محذوف قديم", AccountType = 1, ActorUserId = scope.ActorUserId,
                CorrelationId = Guid.NewGuid(), Reason = "سجل سابق", OccurredAtUtc = DateTimeOffset.UtcNow });
            await context.SaveChangesAsync(Cancellation).ConfigureAwait(true);
            var legacy = Assert.Single((await chart.GetAccountHistoryAsync(scope, legacyId, PageRequest.Create(1, 10), Cancellation).ConfigureAwait(true)).Items);
            Assert.Null(legacy.Before); Assert.Null(legacy.After); Assert.Null(legacy.WorkingBranchId);
        }
    }
}
