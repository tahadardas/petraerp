using System.Data.Common;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PetraERP.Application.Workspaces;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Persistence.Actors;
using PetraERP.Infrastructure.Workspaces;

namespace PetraERP.Api.Tests;

public sealed partial class FxRevaluationRegressionSqlTests
{
    [Theory(Explicit = true)]
    [InlineData("draft", 0)]
    [InlineData("journal-update", 1)]
    [InlineData("journal-update", 2)]
    [InlineData("posting-complete", 0)]
    [InlineData("fx-complete", 0)]
    [Trait("Category", "SqlServer")]
    public async Task SqlFailureRollsBackItsStageAndRetryCompletesOneTrackedJournal(string stage, int updateNumber)
    {
        var fault = new FxSqlFault();
        var host = await ApiSqlHost.CreateWithWorkspaceAsync(overrides: services =>
            services.AddScoped<IAccountingDbContextFactory>(provider => new FaultAccountingFactory(
                provider.GetRequiredService<IFiscalDatabaseResolver>(),
                provider.GetRequiredService<IOptions<FiscalDatabaseOptions>>(),
                provider.GetRequiredService<IAccountingActorAuthority>(),
                provider.GetRequiredService<SystemDbContext>(), fault))).ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            var a = await SeedAccountsAsync(host).ConfigureAwait(true);
            await SeedRateAsync(host, "USD", 0.709m, Oct).ConfigureAwait(true);
            await SeedPostedForeignLineAsync(host, host.BranchId, a, "USD", 1000m, 700m, Oct, 5000).ConfigureAwait(true);
            using var client = Workspace(host, await AdminTokenAsync(host).ConfigureAwait(true));
            var request = Exec(a, Oct, Guid.NewGuid().ToString("N"));
            fault.Arm(stage, updateNumber);
            var (failed, _, _) = await ExecuteAsync(client, host, request).ConfigureAwait(true);
            Assert.Equal(1, fault.Hits);
            Assert.False(failed == HttpStatusCode.OK, "Controlled SQL THROW must reach the real request failure path.");
            fault.Disable();
            using var annual = host.AnnualContext();
            var before = await annual.JournalEntries.Where(j => j.PostingSequence == null || j.PostingSequence < 5000)
                .Select(j => j.Id).ToListAsync(Cancellation).ConfigureAwait(true);
            var orphans = await annual.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM [ledger].[JournalEntries] j WHERE (j.[PostingSequence] IS NULL OR j.[PostingSequence]<5000) AND NOT EXISTS(SELECT 1 FROM [ledger].[FxRevaluationRequests] r WHERE r.[DraftJournalId]=j.[Id])")
                .SingleAsync(Cancellation).ConfigureAwait(true);
            Assert.Equal(0, orphans);
            Assert.Equal(stage == "draft" ? 0 : 1, before.Count);
            if (before.Count == 1)
            {
                var expectedState = stage == "fx-complete" ? 4 : stage == "posting-complete" ? 3 : updateNumber;
                Assert.Equal((byte)expectedState, await annual.JournalEntries.Where(j => j.Id == before[0]).Select(j => j.State).SingleAsync(Cancellation).ConfigureAwait(true));
            }
            Assert.Equal(stage == "fx-complete" ? 1 : 0, await PostedFxJournalCountAsync(host).ConfigureAwait(true));
            var (status, result, _) = await ExecuteAsync(client, host, request).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, status);
            Assert.NotNull(result);
            if (before.Count == 1) Assert.Equal(before[0], result.JournalEntryId);
            Assert.Equal(1, await annual.JournalEntries.CountAsync(j => j.PostingSequence == null || j.PostingSequence < 5000, Cancellation).ConfigureAwait(true));
            Assert.Equal(1, await PostedFxJournalCountAsync(host).ConfigureAwait(true));
            Assert.Equal(9m, await GainBalanceAsync(host, a.Gain).ConfigureAwait(true));
            Assert.Equal(1, await annual.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM [ledger].[FxRevaluationRequests] WHERE [Status]=2 AND [OutcomeJournalId]=[DraftJournalId]").SingleAsync(Cancellation).ConfigureAwait(true));
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task CurrencyFilterCannotHideAmbiguousHistoricalCarryingValue()
    {
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            var a = await SeedAccountsAsync(host).ConfigureAwait(true);
            using (var annual = host.AnnualContext())
                await EnsureCurrencyAsync(annual, "EUR", "يورو", "Euro").ConfigureAwait(true);
            await SeedPostedForeignLineAsync(host, host.BranchId, a, "USD", 1000m, 700m, Oct, 5000).ConfigureAwait(true);
            await SeedPostedForeignLineAsync(host, host.BranchId, a, "EUR", 1000m, 800m, Oct, 5001).ConfigureAwait(true);
            using var client = Workspace(host, await AdminTokenAsync(host).ConfigureAwait(true));
            var (status, _, code) = await ExecuteAsync(client, host, Exec(a, Oct, Guid.NewGuid().ToString("N")) with { CurrencyCode = "USD", OverrideClosingRate = "0.709" }).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
            Assert.Equal("fx.carrying_value_ambiguous", code);
            using var check = host.AnnualContext();
            Assert.Equal(0, await check.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM [ledger].[FxRevaluationRequests]").SingleAsync(Cancellation).ConfigureAwait(true));
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task DecimalScaleAndDuplicateAccountSelectionsHaveOneCanonicalFingerprint()
    {
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            var a = await SeedAccountsAsync(host).ConfigureAwait(true);
            await SeedPostedForeignLineAsync(host, host.BranchId, a, "USD", 1000m, 700m, Oct, 5000).ConfigureAwait(true);
            using var client = Workspace(host, await AdminTokenAsync(host).ConfigureAwait(true));
            var request = Exec(a, Oct, Guid.NewGuid().ToString("N")) with { OverrideClosingRate = "0.7090", SelectedAccountIds = new[] { a.Fx, a.Fx } };
            var (firstStatus, first, _) = await ExecuteAsync(client, host, request).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, firstStatus);
            var (secondStatus, second, _) = await ExecuteAsync(client, host, request with { OverrideClosingRate = "0.709", SelectedAccountIds = new[] { a.Fx } }).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, secondStatus);
            Assert.Equal(first!.JournalEntryId, second!.JournalEntryId);
            Assert.Equal(1, await PostedFxJournalCountAsync(host).ConfigureAwait(true));
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task CompleteSettlementClearsResidualPriorRevaluation()
    {
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            var a = await SeedAccountsAsync(host).ConfigureAwait(true);
            await SeedRateAsync(host, "USD", 0.709m, Oct).ConfigureAwait(true);
            await SeedPostedForeignLineAsync(host, host.BranchId, a, "USD", 1000m, 700m, Oct, 5000).ConfigureAwait(true);
            using var client = Workspace(host, await AdminTokenAsync(host).ConfigureAwait(true));
            Assert.Equal(HttpStatusCode.OK, (await ExecuteAsync(client, host, Exec(a, Oct, Guid.NewGuid().ToString("N"))).ConfigureAwait(true)).Status);
            await SeedPostedForeignLineAsync(host, host.BranchId, a, "USD", 1000m, 700m, Nov, 5001, debitExposure: false).ConfigureAwait(true);
            var (status, body, _) = await ExecuteAsync(client, host, Exec(a, Nov, Guid.NewGuid().ToString("N"))).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, status);
            Assert.Equal("9.0000", body!.TotalLossAmount);
            var (previewStatus, preview, _) = await PreviewAsync(client, host, Nov).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, previewStatus);
            Assert.Empty(preview!.Items);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task PendingReservationCannotAttributeAnUnrelatedBaseCurrencyMovement()
    {
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            var a = await SeedAccountsAsync(host).ConfigureAwait(true);
            await SeedPostedForeignLineAsync(host, host.BranchId, a, "USD", 1000m, 700m, Oct, 5000).ConfigureAwait(true);
            await SeedPostedForeignLineAsync(host, host.BranchId, a, "JOD", 100m, 100m, Oct, 5001).ConfigureAwait(true);
            using var client = Workspace(host, await AdminTokenAsync(host).ConfigureAwait(true));
            var (status, _, code) = await ExecuteAsync(client, host, Exec(a, Oct, Guid.NewGuid().ToString("N")) with { OverrideClosingRate = "0.709" }).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
            Assert.Equal("fx.carrying_value_ambiguous", code);
            Assert.Equal(0, await PostedFxJournalCountAsync(host).ConfigureAwait(true));
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task ClosedPeriodRejectsPostingAndSameKeyResumesAfterPeriodReopens()
    {
        var host = await ApiSqlHost.CreateWithWorkspaceAsync().ConfigureAwait(true);
        await using (host.ConfigureAwait(true))
        {
            var a = await SeedAccountsAsync(host).ConfigureAwait(true);
            await SeedRateAsync(host, "USD", 0.709m, Oct).ConfigureAwait(true);
            await SeedPostedForeignLineAsync(host, host.BranchId, a, "USD", 1000m, 700m, Oct, 5000).ConfigureAwait(true);
            // Arrange an actually closed period in the isolated annual database.
            using var annual = host.AnnualContext();
            await annual.Database.ExecuteSqlInterpolatedAsync($"UPDATE [ledger].[AccountingPeriods] SET [State]=2 WHERE [CompanyId]={host.CompanyId}", Cancellation).ConfigureAwait(true);
            using var client = Workspace(host, await AdminTokenAsync(host).ConfigureAwait(true));
            var request = Exec(a, Oct, Guid.NewGuid().ToString("N"));
            var (failed, _, _) = await ExecuteAsync(client, host, request).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, failed);
            Assert.Equal(0, await PostedFxJournalCountAsync(host).ConfigureAwait(true));
            await annual.Database.ExecuteSqlInterpolatedAsync($"UPDATE [ledger].[AccountingPeriods] SET [State]=0 WHERE [CompanyId]={host.CompanyId}", Cancellation).ConfigureAwait(true);
            var (status, _, _) = await ExecuteAsync(client, host, request).ConfigureAwait(true);
            Assert.Equal(HttpStatusCode.OK, status);
            Assert.Equal(1, await PostedFxJournalCountAsync(host).ConfigureAwait(true));
        }
    }

    // Real SQL exception injection; no alternate ledger, in-memory database, fake posting or schema changes.
    private sealed class FxSqlFault : DbCommandInterceptor
    {
        private string? stage;
        private int updateNumber;
        private int updates;
        public int Hits { get; private set; }
        public void Arm(string value, int number) { stage = value; updateNumber = number; updates = 0; Hits = 0; }
        public void Disable() => stage = null;
        private void Inspect(DbCommand command)
        {
            if (stage is null) return;
            var text = command.CommandText;
            var journalUpdate = text.Contains("UPDATE [ledger].[JournalEntries]", StringComparison.Ordinal);
            if (journalUpdate) updates++;
            var matches = stage switch
            {
                "draft" => text.Contains("INSERT INTO [ledger].[JournalEntries]", StringComparison.Ordinal),
                "journal-update" => journalUpdate && updates == updateNumber,
                "posting-complete" => text.Contains("UPDATE [ledger].[PostingRequests]", StringComparison.Ordinal) && text.Contains("CompletedAtUtc", StringComparison.Ordinal),
                "fx-complete" => text.Contains("UPDATE [ledger].[FxRevaluationRequests]", StringComparison.Ordinal) && text.Contains("[CompletedAtUtc] =", StringComparison.Ordinal),
                _ => false
            };
            if (!matches) return;
            Hits++;
            stage = null;
            command.CommandText = "THROW 51099, N'Controlled FX stage fault', 1;";
        }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { Inspect(command); return ValueTask.FromResult(result); }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Inspect(command); return ValueTask.FromResult(result); }
    }

    private sealed class FaultAccountingFactory(IFiscalDatabaseResolver resolver, IOptions<FiscalDatabaseOptions> options,
        IAccountingActorAuthority authority, SystemDbContext identity, FxSqlFault fault) : IAccountingDbContextFactory
    {
        public async Task<AccountingDbContext> CreateDbContextAsync(Guid workspaceId, CancellationToken cancellationToken = default)
            => CreateDbContext((await resolver.ResolveAsync(workspaceId, cancellationToken).ConfigureAwait(false)).ConnectionString);
        public async Task<AccountingDbContext> CreateDbContextAsync(Guid companyId, Guid fiscalYearId, CancellationToken cancellationToken = default)
            => CreateDbContext((await resolver.ResolveByCompanyAndYearAsync(companyId, fiscalYearId, cancellationToken).ConfigureAwait(false)).ConnectionString);
        public AccountingDbContext CreateDbContext(string connectionString)
        {
            var builder = new DbContextOptionsBuilder<AccountingDbContext>()
                .UseSqlServer(connectionString, sql => sql.UseCompatibilityLevel(options.Value.CompatibilityLevel).CommandTimeout(options.Value.CommandTimeoutSeconds))
                .AddInterceptors(fault);
            var context = new AccountingDbContext(builder.Options, authority);
            context.BindIdentity(identity);
            return context;
        }
    }
}
