using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.ChartOfAccounts;
using PetraERP.Domain.Primitives;
using PetraERP.Infrastructure.GeneralLedger;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Tests;

public sealed class AccountCardContextSqlTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task LargeChartReadsOnlyThePathAndOneChildPageAndNavigationNeverMaterializesTheChart()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var s = await LedgerScenario.CreateAsync(database).ConfigureAwait(true);
            var root = await s.AccountAsync(s.CompanyA, "1000", AccountTypeCode.Assets, false).ConfigureAwait(true);
            var parent = await s.AccountAsync(s.CompanyA, "1100", AccountTypeCode.Assets, false, root.Id).ConfigureAwait(true);
            var branch = await s.AccountAsync(s.CompanyA, "1110", AccountTypeCode.Assets, false, parent.Id).ConfigureAwait(true);
            var foreign = await s.AccountAsync(s.CompanyB, "1000", AccountTypeCode.Assets, false).ConfigureAwait(true);
            using (var seed = database.Context())
            {
                // Synthetic fixture only, never legacy/customer data. Thousands of unrelated siblings must not be read.
                await seed.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT [ledger].[Accounts] ([Id],[CompanyId],[Code],[NameArabic],[AccountType],[PostingAllowed],[CostCenterPolicy],[CurrencyMode],[ParentId],[IsActive],[CreatedAtUtc],[UpdatedAtUtc])
                    SELECT NEWID(),{s.CompanyA},'Z'+RIGHT('00000'+CAST([value] AS varchar(5)),5),N'حساب اصطناعي',1,1,2,1,NULL,1,SYSUTCDATETIME(),SYSUTCDATETIME()
                    FROM GENERATE_SERIES(1,10000);
                    INSERT [ledger].[Accounts] ([Id],[CompanyId],[Code],[NameArabic],[AccountType],[PostingAllowed],[CostCenterPolicy],[CurrencyMode],[ParentId],[IsActive],[CreatedAtUtc],[UpdatedAtUtc])
                    SELECT NEWID(),{s.CompanyA},'1110-'+RIGHT('000'+CAST([value] AS varchar(3)),3),N'ابن اصطناعي',1,1,2,1,{branch.Id},1,SYSUTCDATETIME(),SYSUTCDATETIME()
                    FROM GENERATE_SERIES(1,56);
                    """, Cancellation).ConfigureAwait(true);
            }
            var probe = new ReadProbe();
            using var context = new PetraDbContext(new DbContextOptionsBuilder<PetraDbContext>()
                .UseSqlServer(database.ConnectionString, sql => sql.UseCompatibilityLevel(database.CompatibilityLevel)).AddInterceptors(probe).Options);
            var chart = LedgerScenario.Chart(context); var scope = s.Master(s.CompanyA);
            var first = await chart.GetAccountCardContextAsync(scope, branch.Id, PageRequest.Create(1, 25), Cancellation).ConfigureAwait(true);
            Assert.Equal([root.Id, parent.Id, branch.Id], first.Path.Select(row => row.Id));
            Assert.Equal([0, 1, 2], first.Path.Select(row => row.Level));
            Assert.Equal(56, first.Children.TotalCount); Assert.Equal(25, first.Children.Items.Count);
            Assert.Equal("1110-001", first.Children.Items[0].Code); Assert.Equal("1110-025", first.Children.Items[^1].Code);
            Assert.All(first.Children.Items, row => { Assert.Equal(3, row.Level); Assert.Equal(branch.Id, row.ParentId); Assert.Null(row.NameEnglish); });
            Assert.Equal(28, probe.AccountRows); Assert.Equal(4, probe.Readers);
            // RecordsAffected does not count SELECT rows. EF materialization counts bound entity payload, and every
            // command is retained so an unbounded scalar parent map (the previous hidden cost) also fails this check.
            Assert.Contains(probe.Sql, sql => sql.Contains("MAXRECURSION", StringComparison.Ordinal));
            Assert.DoesNotContain(probe.Sql, sql => sql.Contains("SELECT [a].[Id], [a].[ParentId]", StringComparison.Ordinal));
            probe.Reset();
            var third = await chart.GetAccountCardContextAsync(scope, branch.Id, PageRequest.Create(3, 25), Cancellation).ConfigureAwait(true);
            Assert.Equal(6, third.Children.Items.Count); Assert.Equal("1110-051", third.Children.Items[0].Code);
            Assert.Empty(first.Children.Items.Select(row => row.Id).Intersect(third.Children.Items.Select(row => row.Id)));
            Assert.Equal(9, probe.AccountRows); Assert.Equal(4, probe.Readers);
            probe.Reset();
            var last = await chart.NavigateAccountAsync(scope, AccountNavigationCode.Last, null, Cancellation).ConfigureAwait(true);
            Assert.Equal("Z10000", last!.Code); Assert.Equal(2, probe.AccountRows); Assert.Equal(3, probe.Readers);
            Assert.DoesNotContain(probe.Sql, sql => sql.Contains("SELECT [a].[Id], [a].[ParentId]", StringComparison.Ordinal));
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => chart.GetAccountCardContextAsync(scope, foreign.Id, PageRequest.Create(1, 25), Cancellation)).ConfigureAwait(true);
            Assert.Empty((await chart.GetAccountCardContextAsync(scope, branch.Id, PageRequest.Create(4, 25), Cancellation).ConfigureAwait(true)).Children.Items);
            // Out-of-band corruption must fail closed, rather than emit another company's ancestor or loop forever.
            await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE [ledger].[Accounts] SET [ParentId]={foreign.Id} WHERE [Id]={root.Id}", Cancellation).ConfigureAwait(true);
            var cross = await Assert.ThrowsAsync<InvalidOperationException>(() => chart.GetAccountCardContextAsync(scope, branch.Id, PageRequest.Create(1, 25), Cancellation)).ConfigureAwait(true);
            Assert.Equal(RuleViolation.AccountParentInvalid, RuleViolation.CodeOf(cross));
            await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE [ledger].[Accounts] SET [ParentId]={branch.Id} WHERE [Id]={root.Id}", Cancellation).ConfigureAwait(true);
            var cycle = await Assert.ThrowsAsync<InvalidOperationException>(() => chart.GetAccountCardContextAsync(scope, branch.Id, PageRequest.Create(1, 25), Cancellation)).ConfigureAwait(true);
            Assert.Equal(RuleViolation.AccountParentInvalid, RuleViolation.CodeOf(cycle));
        }
    }

    private sealed class ReadProbe : DbCommandInterceptor, IMaterializationInterceptor
    {
        internal int AccountRows { get; private set; }
        internal int Readers { get; private set; }
        internal List<string> Sql { get; } = [];
        internal void Reset() { AccountRows = 0; Readers = 0; Sql.Clear(); }
        public object InitializedInstance(MaterializationInterceptionData materializationData, object entity)
        {
            if (entity is AccountRow) AccountRows++;
            return entity;
        }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Readers++; Sql.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
