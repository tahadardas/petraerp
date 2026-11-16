using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.ChartOfAccounts;

namespace PetraERP.Infrastructure.Tests;

public sealed class CostCenterHistorySqlTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;
    private const string Previous = "20261018162014_AccountLifecycleAudit";

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task LifecycleKeepsNormalizedStatesContextPagingAndRejectsTamperingNoopsAndStaleWrites()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var s = await LedgerScenario.CreateAsync(database).ConfigureAwait(true);
            using var context = database.Context(); var chart = LedgerScenario.Chart(context);
            var correlation = Guid.NewGuid(); var scope = LedgerMasterScope.Create(s.CompanyA, s.Actor, s.BranchA1, correlation);
            var input = new CostCenterInput { Code = "HQ", NameArabic = "إدارة دمشق", NameEnglish = null };
            var center = await chart.CreateCostCenterAsync(scope, input, Cancellation).ConfigureAwait(true);
            var created = Assert.Single((await chart.GetCostCenterHistoryAsync(scope, center.Id, PageRequest.Create(1, 2), Cancellation).ConfigureAwait(true)).Items);
            Assert.Equal(CostCenterAuditActionCode.Created, created.Action); Assert.Null(created.Before); Assert.Null(created.After.NameEnglish);
            Assert.Equal((s.Actor, s.BranchA1, correlation), (created.ActorUserId, created.WorkingBranchId, created.CorrelationId));
            Assert.Equal("إدارة دمشق", created.After.NameArabic); var original = center.VersionBase64;
            var revised = input with { NameArabic = "الإدارة الرئيسية", NameEnglish = "Head office" };
            center = await chart.UpdateCostCenterAsync(scope, center.Id, revised, LedgerScenario.Token(center.VersionBase64), Cancellation).ConfigureAwait(true);
            await Assert.ThrowsAsync<ConcurrencyConflictException>(() => chart.UpdateCostCenterAsync(scope, center.Id, input, LedgerScenario.Token(original), Cancellation)).ConfigureAwait(true);
            center = await chart.UpdateCostCenterAsync(scope, center.Id, revised, LedgerScenario.Token(center.VersionBase64), Cancellation).ConfigureAwait(true);
            center = await chart.SetCostCenterActiveAsync(scope, center.Id, false, LedgerScenario.Token(center.VersionBase64), Cancellation).ConfigureAwait(true);
            center = await chart.SetCostCenterActiveAsync(scope, center.Id, false, LedgerScenario.Token(center.VersionBase64), Cancellation).ConfigureAwait(true);
            center = await chart.SetCostCenterActiveAsync(scope, center.Id, true, LedgerScenario.Token(center.VersionBase64), Cancellation).ConfigureAwait(true);
            var first = await chart.GetCostCenterHistoryAsync(scope, center.Id, PageRequest.Create(1, 2), Cancellation).ConfigureAwait(true);
            var second = await chart.GetCostCenterHistoryAsync(scope, center.Id, PageRequest.Create(2, 2), Cancellation).ConfigureAwait(true);
            Assert.Equal(4, first.TotalCount);
            Assert.Equal([CostCenterAuditActionCode.Activated, CostCenterAuditActionCode.Deactivated], first.Items.Select(row => row.Action));
            Assert.Equal([CostCenterAuditActionCode.Updated, CostCenterAuditActionCode.Created], second.Items.Select(row => row.Action));
            Assert.Empty(first.Items.Select(row => row.EventId).Intersect(second.Items.Select(row => row.EventId)));
            Assert.Equal(("إدارة دمشق", "الإدارة الرئيسية"), (second.Items[0].Before!.NameArabic, second.Items[0].After.NameArabic));
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => chart.CreateCostCenterAsync(
                LedgerMasterScope.Create(s.CompanyA, s.Actor, s.BranchB1), input with { Code = "INVALID" }, Cancellation)).ConfigureAwait(true);
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => chart.GetCostCenterHistoryAsync(s.Master(s.CompanyB), center.Id, PageRequest.Create(1, 2), Cancellation)).ConfigureAwait(true);
            foreach (var statement in new[] { "UPDATE [ledger].[CostCenterAudit] SET [Action]=1", "DELETE FROM [ledger].[CostCenterAudit]",
                "UPDATE [ledger].[CostCenterAuditStates] SET [NameArabic]=N'تلاعب'", "DELETE FROM [ledger].[CostCenterAuditStates]" })
                await Assert.ThrowsAnyAsync<Exception>(() => context.Database.ExecuteSqlRawAsync(statement, Cancellation)).ConfigureAwait(true);
            var error = await Assert.ThrowsAnyAsync<Exception>(() => context.GetService<IMigrator>().MigrateAsync(Previous, Cancellation)).ConfigureAwait(true);
            Assert.Contains("instead of discarding audit history", error.Message, StringComparison.Ordinal);
            Assert.Equal(4, (await chart.GetCostCenterHistoryAsync(scope, center.Id, PageRequest.Create(1, 10), Cancellation).ConfigureAwait(true)).TotalCount);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task AuditFailureRollsBackBothCreationAndUpdate()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var s = await LedgerScenario.CreateAsync(database).ConfigureAwait(true);
            using var context = database.Context(); var chart = LedgerScenario.Chart(context); var scope = s.Master(s.CompanyA);
            var input = new CostCenterInput { Code = "HQ", NameArabic = "قبل التغيير" };
            var center = await chart.CreateCostCenterAsync(scope, input, Cancellation).ConfigureAwait(true);
            await context.Database.ExecuteSqlRawAsync("CREATE TRIGGER [ledger].[TR_Test_RejectCenterAudit] ON [ledger].[CostCenterAuditStates] AFTER INSERT AS THROW 51020, 'Test audit failure', 1;", Cancellation).ConfigureAwait(true);
            await Assert.ThrowsAnyAsync<Exception>(() => chart.CreateCostCenterAsync(scope, input with { Code = "OTHER" }, Cancellation)).ConfigureAwait(true);
            context.ChangeTracker.Clear();
            await Assert.ThrowsAnyAsync<Exception>(() => chart.UpdateCostCenterAsync(scope, center.Id, input with { NameArabic = "مرفوض" }, LedgerScenario.Token(center.VersionBase64), Cancellation)).ConfigureAwait(true);
            context.ChangeTracker.Clear();
            Assert.False(await context.CostCenters.AnyAsync(row => row.Code == "OTHER", Cancellation).ConfigureAwait(true));
            Assert.Equal("قبل التغيير", (await chart.GetCostCenterAsync(scope, center.Id, Cancellation).ConfigureAwait(true)).NameArabic);
            Assert.Equal(1, await context.CostCenterAudit.CountAsync(Cancellation).ConfigureAwait(true));
            Assert.Equal(1, await context.CostCenterAuditStates.CountAsync(Cancellation).ConfigureAwait(true));
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task UpgradePreservesExistingCentersWithoutInventingPriorEvents()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var s = await LedgerScenario.CreateAsync(database).ConfigureAwait(true);
            using var context = database.Context(); var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync(Previous, Cancellation).ConfigureAwait(true);
            var id = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
            await context.Database.ExecuteSqlInterpolatedAsync($"INSERT [ledger].[CostCenters] ([Id],[CompanyId],[Code],[NameArabic],[IsActive],[CreatedAtUtc],[UpdatedAtUtc]) VALUES ({id},{s.CompanyA},N'OLD',N'مركز قائم',1,{now},{now})", Cancellation).ConfigureAwait(true);
            await context.Database.MigrateAsync(Cancellation).ConfigureAwait(true);
            var chart = LedgerScenario.Chart(context); var scope = s.Master(s.CompanyA);
            Assert.Empty((await chart.GetCostCenterHistoryAsync(scope, id, PageRequest.Create(1, 10), Cancellation).ConfigureAwait(true)).Items);
            var center = await chart.GetCostCenterAsync(scope, id, Cancellation).ConfigureAwait(true);
            Assert.Equal("مركز قائم", center.NameArabic);
            await chart.UpdateCostCenterAsync(scope, id, new() { Code = "OLD", NameArabic = "مركز بعد التحديث" }, LedgerScenario.Token(center.VersionBase64), Cancellation).ConfigureAwait(true);
            var change = Assert.Single((await chart.GetCostCenterHistoryAsync(scope, id, PageRequest.Create(1, 10), Cancellation).ConfigureAwait(true)).Items);
            Assert.Equal(CostCenterAuditActionCode.Updated, change.Action); Assert.Equal("مركز قائم", change.Before!.NameArabic);
        }
    }
}
