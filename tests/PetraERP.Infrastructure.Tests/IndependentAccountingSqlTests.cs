using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.Security;
using PetraERP.Infrastructure.GeneralLedger;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Persistence.Actors;

namespace PetraERP.Infrastructure.Tests;

public sealed partial class IndependentAccountingSqlTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task CleanAnnualDatabaseHasOnlyBusinessSchemaValidSeedAndIdempotentMigration()
    {
        var fixture = await IndependentAccountingSqlFixture.CreateAsync().ConfigureAwait(true);
        await using (fixture.ConfigureAwait(true))
        {
            var result = await fixture.ProvisionAsync().ConfigureAwait(true);
            Assert.True(result.Succeeded, result.Error);
            using var annual = await fixture.Factory.CreateDbContextAsync(result.WorkspaceId, Cancellation).ConfigureAwait(true);
            Assert.Equal(AccountingSchema.ApprovedMigrations, await annual.Database.GetAppliedMigrationsAsync(Cancellation).ConfigureAwait(true));
            Assert.Equal(5, await annual.Accounts.CountAsync(Cancellation).ConfigureAwait(true));
            Assert.Equal(result.CompanyId, (await annual.Companies.SingleAsync(Cancellation).ConfigureAwait(true)).Id);
            var branch = await annual.Branches.SingleAsync(Cancellation).ConfigureAwait(true);
            Assert.Equal(branch.Id, (await annual.Warehouses.SingleAsync(Cancellation).ConfigureAwait(true)).BranchId);
            Assert.True(await fixture.System.Branches.AnyAsync(b => b.Id == branch.Id && b.CompanyId == result.CompanyId, Cancellation).ConfigureAwait(true));
            var periods = await annual.AccountingPeriods.OrderBy(p => p.RangeStart).ToListAsync(Cancellation).ConfigureAwait(true);
            Assert.Equal(new DateOnly(2026, 1, 31), periods[0].RangeStart);
            Assert.Equal(new DateOnly(2026, 6, 30), periods[^1].RangeEnd);
            Assert.All(periods, p => Assert.Equal(0, p.State));
            for (var i = 1; i < periods.Count; i++) Assert.Equal(periods[i - 1].RangeEnd.AddDays(1), periods[i].RangeStart);
            var tables = await annual.Database.SqlQueryRaw<string>("SELECT SCHEMA_NAME(schema_id)+N'.'+name AS [Value] FROM sys.tables").ToListAsync(Cancellation).ConfigureAwait(true);
            Assert.Equal(80, tables.Count);
            Assert.Equal(79, await annual.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM sys.key_constraints WHERE type='PK' AND parent_object_id<>OBJECT_ID(N'system.__AccountingMigrationsHistory')").SingleAsync(Cancellation).ConfigureAwait(true));
            Assert.Contains("inventory.ItemAccountMappings", tables);
            Assert.Contains("ledger.CostCenterAllocations", tables);
            Assert.Contains("ledger.AccountProfiles", tables);
            Assert.Contains("policy.CompanyPolicies", tables);
            Assert.Contains("policy.NegativeIssueApprovals", tables);
            Assert.Contains("audit.CompanyPolicyAudit", tables);
            Assert.Contains("system.__AccountingMigrationsHistory", tables);
            Assert.DoesNotContain(tables, t => t.StartsWith("security.", StringComparison.OrdinalIgnoreCase) || t.EndsWith(".__EFMigrationsHistory", StringComparison.Ordinal));
            // SQL Server's built-in sys.sysowners compatibility view exposes a column named password.
            // Check all application tables/views, retaining credential exclusion without counting system objects.
            Assert.Equal(0, await annual.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM sys.columns c JOIN sys.objects o ON o.object_id=c.object_id WHERE o.is_ms_shipped=0 AND (c.name LIKE '%Password%' OR c.name LIKE '%Token%')").SingleAsync(Cancellation).ConfigureAwait(true));
            Assert.Equal(0, await annual.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM sys.foreign_keys WHERE is_disabled=1 OR is_not_trusted=1").SingleAsync(Cancellation).ConfigureAwait(true));
            Assert.True(await annual.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM sys.foreign_keys WHERE referenced_object_id=OBJECT_ID(N'audit.ActorReferences')").SingleAsync(Cancellation).ConfigureAwait(true) > 10);
            await annual.Database.MigrateAsync(Cancellation).ConfigureAwait(true);
            Assert.Equal(5, await annual.Accounts.CountAsync(Cancellation).ConfigureAwait(true));
            var resolved = await fixture.Resolver.ResolveFreshAsync(result.WorkspaceId, Cancellation).ConfigureAwait(true);
            Assert.Equal(result.CompanyId, resolved.CompanyId);
            Assert.Equal(result.FiscalYearId, resolved.FiscalYearId);
            var registration = await fixture.System.FiscalDatabases.SingleAsync(r => r.Id == result.WorkspaceId, Cancellation).ConfigureAwait(true);
            Assert.Equal("Active", registration.State);
            Assert.True(registration.IsDefault);
            Assert.Equal("Succeeded", (await fixture.System.ProvisioningOperations.SingleAsync(Cancellation).ConfigureAwait(true)).Status);
        }
    }

    [Theory(Explicit = true)]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "SqlServer")]
    public async Task ResolverRejectsRealAnnualDatabaseRegisteredForDifferentCompanyOrYear(bool wrongCompany)
    {
        var fixture = await IndependentAccountingSqlFixture.CreateAsync().ConfigureAwait(true);
        await using (fixture.ConfigureAwait(true))
        {
            var result = await fixture.ProvisionAsync().ConfigureAwait(true);
            Assert.True(result.Succeeded, result.Error);
            var registry = await fixture.System.FiscalDatabases.SingleAsync(r => r.Id == result.WorkspaceId, Cancellation).ConfigureAwait(true);
            if (wrongCompany)
                registry.CompanyId = await fixture.System.Companies.Where(c => c.Id != result.CompanyId).Select(c => c.Id).SingleAsync(Cancellation).ConfigureAwait(true);
            else registry.FiscalYearId = Guid.NewGuid();
            await fixture.System.SaveChangesAsync(Cancellation).ConfigureAwait(true);
            var error = await Assert.ThrowsAsync<BusinessRuleException>(() => fixture.Resolver.ResolveFreshAsync(result.WorkspaceId, Cancellation)).ConfigureAwait(true);
            Assert.Equal("workspace.scope_mismatch", error.Code);
        }
    }

    [Theory(Explicit = true)]
    [InlineData("CREATE TABLE [ledger].[Accounts]", "ApplyingMigrations")]
    [InlineData("INSERT INTO [inventory].[Warehouses]", "SeedingMasterData")]
    [Trait("Category", "SqlServer")]
    public async Task RealSqlFailureLeavesWorkspaceInactiveAndRetryReusesReservation(string command, string step)
    {
        var fixture = await IndependentAccountingSqlFixture.CreateAsync().ConfigureAwait(true);
        await using (fixture.ConfigureAwait(true))
        {
            fixture.Fault.Match = command;
            var first = await fixture.ProvisionAsync().ConfigureAwait(true);
            Assert.False(first.Succeeded);
            Assert.True(fixture.Fault.Failures > 0);
            fixture.System.ChangeTracker.Clear();
            Assert.False((await fixture.System.Companies.SingleAsync(c => c.Id == first.CompanyId, Cancellation).ConfigureAwait(true)).IsActive);
            var operation = await fixture.System.ProvisioningOperations.SingleAsync(Cancellation).ConfigureAwait(true);
            Assert.Equal("Failed", operation.Status);
            Assert.Equal(step, operation.CurrentStep);
            Assert.Equal("Failed", (await fixture.System.FiscalDatabases.SingleAsync(Cancellation).ConfigureAwait(true)).State);
            if (step == "SeedingMasterData")
            {
                using var failed = fixture.Factory.CreateDbContext(new SqlConnectionStringBuilder(fixture.Database.ConnectionString) { InitialCatalog = first.DatabaseName }.ConnectionString);
                Assert.Equal(0, await failed.Companies.CountAsync(Cancellation).ConfigureAwait(true));
                Assert.Equal(0, await failed.Accounts.CountAsync(Cancellation).ConfigureAwait(true));
                Assert.Equal(0, await failed.ActorReferences.CountAsync(Cancellation).ConfigureAwait(true));
            }
            fixture.Fault.Match = null;
            var retry = await fixture.Service.ProvisionCompanyAsync(IndependentAccountingSqlFixture.Input(), fixture.Actor, Cancellation).ConfigureAwait(true);
            Assert.True(retry.Succeeded, retry.Error);
            Assert.Equal(first.CompanyId, retry.CompanyId);
            Assert.Equal(first.FiscalYearId, retry.FiscalYearId);
            Assert.Equal(first.OperationId, retry.OperationId);
            Assert.Equal(first.DatabaseName, retry.DatabaseName);
            Assert.Equal(1, await fixture.System.Companies.CountAsync(c => c.Code == "ANNUAL", Cancellation).ConfigureAwait(true));
            Assert.Equal(1, await fixture.System.FiscalDatabases.CountAsync(Cancellation).ConfigureAwait(true));
            using var annual = await fixture.Factory.CreateDbContextAsync(retry.WorkspaceId, Cancellation).ConfigureAwait(true);
            Assert.Equal(5, await annual.Accounts.CountAsync(Cancellation).ConfigureAwait(true));
            Assert.Equal(1, await annual.Warehouses.CountAsync(Cancellation).ConfigureAwait(true));
            Assert.Equal(1, await annual.ActorReferences.CountAsync(Cancellation).ConfigureAwait(true));
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task VerifiedActorSnapshotPreservesArabicHistoryOnRenameAndRejectsDisabledUser()
    {
        var fixture = await IndependentAccountingSqlFixture.CreateAsync().ConfigureAwait(true);
        await using (fixture.ConfigureAwait(true))
        {
            var result = await fixture.ProvisionAsync().ConfigureAwait(true);
            Assert.True(result.Succeeded, result.Error);
            using var annual = await fixture.Factory.CreateDbContextAsync(result.WorkspaceId, Cancellation).ConfigureAwait(true);
            annual.BindWriteScope(new(result.CompanyId, result.FiscalYearId, fixture.Actor, PermissionCatalog.AccountsManage));
            var original = await annual.ActorReferences.AsNoTracking().SingleAsync(Cancellation).ConfigureAwait(true);
            annual.AccountAudit.Add(Audit(result.CompanyId, fixture.Actor));
            await annual.SaveChangesAsync(Cancellation).ConfigureAwait(true);
            var central = await fixture.System.Users.SingleAsync(u => u.Id == fixture.Actor, Cancellation).ConfigureAwait(true);
            central.DisplayNameArabic = "اسم المدير الجديد";
            await fixture.System.SaveChangesAsync(Cancellation).ConfigureAwait(true);
            annual.AccountAudit.Add(Audit(result.CompanyId, fixture.Actor));
            await annual.SaveChangesAsync(Cancellation).ConfigureAwait(true);
            var snapshot = await annual.ActorReferences.AsNoTracking().SingleAsync(Cancellation).ConfigureAwait(true);
            Assert.Equal(original.DisplayNameArabic, snapshot.DisplayNameArabic);
            Assert.Equal(original.FirstVerifiedAtUtc, snapshot.FirstVerifiedAtUtc);
            central.IsActive = false;
            await fixture.System.SaveChangesAsync(Cancellation).ConfigureAwait(true);
            annual.AccountAudit.Add(Audit(result.CompanyId, fixture.Actor));
            var denied = await Assert.ThrowsAsync<BusinessRuleException>(() => annual.SaveChangesAsync(Cancellation)).ConfigureAwait(true);
            Assert.Equal("accounting.actor_denied", denied.Code);
            Assert.Equal(2, await annual.AccountAudit.CountAsync(Cancellation).ConfigureAwait(true));
            Assert.Equal(fixture.Actor, (await annual.AccountAudit.AsNoTracking().FirstAsync(Cancellation).ConfigureAwait(true)).ActorUserId);
            await Assert.ThrowsAsync<SqlException>(() => annual.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [audit].[ActorReferences] SET DisplayNameArabic=N'تغيير' WHERE Id={fixture.Actor}", Cancellation)).ConfigureAwait(true);
            await Assert.ThrowsAsync<SqlException>(() => annual.Database.ExecuteSqlRawAsync(
                "UPDATE [ledger].[AccountAudit] SET NameArabic=N'تغيير'", Cancellation)).ConfigureAwait(true);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task RelationalIntegrityRejectsMissingActorWrongBranchAndDisabledAuditProtection()
    {
        var fixture = await IndependentAccountingSqlFixture.CreateAsync().ConfigureAwait(true);
        await using (fixture.ConfigureAwait(true))
        {
            var result = await fixture.ProvisionAsync().ConfigureAwait(true);
            Assert.True(result.Succeeded, result.Error);
            using var annual = await fixture.Factory.CreateDbContextAsync(result.WorkspaceId, Cancellation).ConfigureAwait(true);
            annual.BindWriteScope(new(result.CompanyId, result.FiscalYearId, fixture.Actor, PermissionCatalog.AccountsManage));
            annual.AccountAudit.Add(Audit(result.CompanyId, fixture.Actor));
            await annual.SaveChangesAsync(Cancellation).ConfigureAwait(true);
            var unknown = Guid.NewGuid();
            await Assert.ThrowsAsync<SqlException>(() => annual.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO ledger.AccountAudit (EventId,CompanyId,AccountId,Action,Code,NameArabic,AccountType,ActorUserId,CorrelationId,OccurredAtUtc)
                VALUES (NEWID(),{result.CompanyId},NEWID(),2,N'1',N'اختبار',1,{unknown},NEWID(),SYSUTCDATETIME())
                """, Cancellation)).ConfigureAwait(true);
            await Assert.ThrowsAsync<SqlException>(() => annual.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE inventory.Warehouses SET BranchId={unknown} WHERE CompanyId={result.CompanyId}
                """, Cancellation)).ConfigureAwait(true);
            await annual.Database.ExecuteSqlRawAsync("DISABLE TRIGGER [audit].[TR_ActorReferences_Immutable] ON [audit].[ActorReferences]", Cancellation).ConfigureAwait(true);
            var rejected = await Assert.ThrowsAsync<BusinessRuleException>(() => fixture.Resolver.ResolveFreshAsync(result.WorkspaceId, Cancellation)).ConfigureAwait(true);
            Assert.Equal("workspace.schema_mismatch", rejected.Code);
        }
    }

    private static AccountAuditRow Audit(Guid company, Guid actor) => new()
    {
        EventId = Guid.NewGuid(), CompanyId = company, AccountId = Guid.NewGuid(), Action = 2, Code = "1", NameArabic = "حساب عربي",
        AccountType = 1, ActorUserId = actor, CorrelationId = Guid.NewGuid(), OccurredAtUtc = DateTimeOffset.UtcNow,
    };
}
