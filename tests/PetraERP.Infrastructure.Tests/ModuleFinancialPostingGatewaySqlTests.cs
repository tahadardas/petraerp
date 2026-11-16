using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.Posting;
using PetraERP.Application.Security;
using PetraERP.Domain.GeneralLedger;
using PetraERP.Infrastructure.Modules;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Modules.Abstractions;

namespace PetraERP.Infrastructure.Tests;

/// <summary>
/// The module posting gateway against real SQL Server: a module's proposal must become a normally posted journal
/// (sequence, audit, period rules), be replay-safe by idempotency key, and be reversible only through a linked reversal.
/// Requires PETRAERP_SQL_TEST_SERVER.
/// </summary>
public sealed class ModuleFinancialPostingGatewaySqlTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private sealed record Fixture(PetraDbContext Db, SqlFinancialPostingGateway Gateway, ModuleRequestContext Context, Guid YearId, Guid Cash, Guid Revenue);

    private static async Task<Fixture> StartAsync(SqlTestDatabase database, bool grantPosting = true, bool grantReversing = true)
    {
        var ctx = database.Context();
        await ctx.Database.MigrateAsync(Cancellation).ConfigureAwait(true);
        var companyId = await PostingSqlTests.SeedCompanyAsync(ctx).ConfigureAwait(true);
        var branchId = await PostingSqlTests.SeedBranchAsync(ctx, companyId).ConfigureAwait(true);
        var (yearId, _) = await PostingSqlTests.SeedFiscalYearAndPeriodsAsync(ctx, companyId).ConfigureAwait(true);
        var (cash, revenue) = await PostingSqlTests.SeedAccountsAsync(ctx, companyId).ConfigureAwait(true);
        var user = await PostingSqlTests.SeedAuthorizedActorAsync(ctx, companyId, branchId, canReverse: true, userName: "module-actor").ConfigureAwait(true);
        var granted = new HashSet<string>();
        if (grantPosting) granted.Add(PermissionCatalog.JournalsPost);
        if (grantReversing) granted.Add(PermissionCatalog.JournalsReverse);
        var context = new ModuleRequestContext(companyId, yearId, branchId, user, "ar", granted, "demo.collections");
        return new Fixture(ctx, new SqlFinancialPostingGateway(new SharedModuleLedgerContexts(ctx), TimeProvider.System), context, yearId, cash, revenue);
    }

    private static PostingProposal Proposal(Fixture f, decimal amount = 250m, Guid? key = null, DateOnly? date = null) => new(
        date ?? new DateOnly(2026, 3, 10), "Module collection", "EXT-1001", key ?? Guid.NewGuid(),
        [
            new PostingProposalLine(f.Cash, "JOD", amount, 0m, 1m, "Cash", null),
            new PostingProposalLine(f.Revenue, "JOD", 0m, amount, 1m, "Revenue", null),
        ]);

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task ProposalIsPostedThroughTheCoreEngineAndReplayReturnsTheOriginalReceipt()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var f = await StartAsync(database).ConfigureAwait(true);
            using var _ = f.Db;
            var proposal = Proposal(f);

            var check = await f.Gateway.ValidateAsync(f.Context, proposal, Cancellation).ConfigureAwait(true);
            Assert.True(check.IsValid, check.Reason);

            var receipt = await f.Gateway.PostAsync(f.Context, proposal, Cancellation).ConfigureAwait(true);
            Assert.False(receipt.WasAlreadyPosted);
            Assert.Equal(1, receipt.PostingSequence);
            var row = await f.Db.JournalEntries.AsNoTracking().SingleAsync(j => j.Id == receipt.JournalId, Cancellation).ConfigureAwait(true);
            Assert.Equal((byte)JournalEntryState.Posted, row.State);
            Assert.Equal(f.YearId, row.PostingFiscalYearId);
            Assert.Contains("EXT-1001", row.Description, StringComparison.Ordinal);
            Assert.Contains("[module:demo.collections]", row.Description, StringComparison.Ordinal);

            var replay = await f.Gateway.PostAsync(f.Context, proposal, Cancellation).ConfigureAwait(true);
            Assert.True(replay.WasAlreadyPosted);
            Assert.Equal(receipt.JournalId, replay.JournalId);
            Assert.Equal(1, await f.Db.JournalEntries.AsNoTracking().CountAsync(Cancellation).ConfigureAwait(true));

            var conflict = await Assert.ThrowsAsync<PostingIdempotencyConflictException>(
                () => f.Gateway.PostAsync(f.Context, Proposal(f, amount: 999m, key: proposal.IdempotencyKey), Cancellation)).ConfigureAwait(true);
            Assert.Equal(proposal.IdempotencyKey, conflict.IdempotencyKey);
            Assert.Equal(1, await f.Db.JournalEntries.AsNoTracking().CountAsync(Cancellation).ConfigureAwait(true));
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task InvalidProposalsAreRejectedBeforeAnythingIsWritten()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var f = await StartAsync(database).ConfigureAwait(true);
            using var _ = f.Db;

            var unbalanced = Proposal(f) with { Lines = [new PostingProposalLine(f.Cash, "JOD", 100m, 0m, 1m, "x", null), new PostingProposalLine(f.Revenue, "JOD", 0m, 90m, 1m, "y", null)] };
            Assert.False((await f.Gateway.ValidateAsync(f.Context, unbalanced, Cancellation).ConfigureAwait(true)).IsValid);
            await Assert.ThrowsAsync<BusinessRuleException>(() => f.Gateway.PostAsync(f.Context, unbalanced, Cancellation)).ConfigureAwait(true);

            var outsideYear = Proposal(f, date: new DateOnly(2031, 1, 1));
            Assert.False((await f.Gateway.ValidateAsync(f.Context, outsideYear, Cancellation).ConfigureAwait(true)).IsValid);
            await Assert.ThrowsAsync<BusinessRuleException>(() => f.Gateway.PostAsync(f.Context, outsideYear, Cancellation)).ConfigureAwait(true);

            var foreignBranch = f.Context with { BranchId = Guid.NewGuid() };
            Assert.False((await f.Gateway.ValidateAsync(foreignBranch, Proposal(f), Cancellation).ConfigureAwait(true)).IsValid);

            Assert.Equal(0, await f.Db.JournalEntries.AsNoTracking().CountAsync(Cancellation).ConfigureAwait(true));
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task ModuleWithoutThePostingPermissionCannotPost()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var f = await StartAsync(database, grantPosting: false, grantReversing: false).ConfigureAwait(true);
            using var _ = f.Db;
            await Assert.ThrowsAsync<PostingAuthorizationException>(() => f.Gateway.PostAsync(f.Context, Proposal(f), Cancellation)).ConfigureAwait(true);
            await Assert.ThrowsAsync<PostingAuthorizationException>(() => f.Gateway.ReverseAsync(f.Context, Guid.NewGuid(), Guid.NewGuid(), "x", Cancellation)).ConfigureAwait(true);
            Assert.Equal(0, await f.Db.JournalEntries.AsNoTracking().CountAsync(Cancellation).ConfigureAwait(true));
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task ReversalIsALinkedPostedEntryAndReplayIsSafe()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var f = await StartAsync(database).ConfigureAwait(true);
            using var _ = f.Db;
            var posted = await f.Gateway.PostAsync(f.Context, Proposal(f), Cancellation).ConfigureAwait(true);

            // Another module (or one with an unrelated identity) cannot reverse this module's entry.
            var stranger = f.Context with { ModuleId = "other.module" };
            await Assert.ThrowsAsync<PostingAuthorizationException>(
                () => f.Gateway.ReverseAsync(stranger, posted.JournalId, Guid.NewGuid(), "not mine", Cancellation)).ConfigureAwait(true);
            Assert.Equal(1, await f.Db.JournalEntries.AsNoTracking().CountAsync(Cancellation).ConfigureAwait(true));

            var key = Guid.NewGuid();
            var reversal = await f.Gateway.ReverseAsync(f.Context, posted.JournalId, key, "Cancel the collection", Cancellation).ConfigureAwait(true);
            Assert.False(reversal.WasAlreadyPosted);
            Assert.NotEqual(posted.JournalId, reversal.JournalId);
            Assert.Equal(2, reversal.PostingSequence);

            var original = await f.Db.JournalEntries.AsNoTracking().SingleAsync(j => j.Id == posted.JournalId, Cancellation).ConfigureAwait(true);
            var reversalRow = await f.Db.JournalEntries.AsNoTracking().SingleAsync(j => j.Id == reversal.JournalId, Cancellation).ConfigureAwait(true);
            Assert.Equal((byte)JournalEntryState.Reversed, original.State);
            Assert.Equal(reversal.JournalId, original.ReversedByEntryId);
            Assert.Equal(posted.JournalId, reversalRow.ReversesEntryId);

            var replay = await f.Gateway.ReverseAsync(f.Context, posted.JournalId, key, "Cancel the collection", Cancellation).ConfigureAwait(true);
            Assert.True(replay.WasAlreadyPosted);
            Assert.Equal(reversal.JournalId, replay.JournalId);
            Assert.Equal(2, await f.Db.JournalEntries.AsNoTracking().CountAsync(Cancellation).ConfigureAwait(true));

            await Assert.ThrowsAsync<PostingIdempotencyConflictException>(
                () => f.Gateway.ReverseAsync(f.Context, Guid.NewGuid(), key, "x", Cancellation)).ConfigureAwait(true);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task ReferenceLookupIsReadOnlyAndFilteredToTheCallersCompany()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var f = await StartAsync(database).ConfigureAwait(true);
            using var _ = f.Db;
            var query = new SqlReferenceDataQuery(new SharedModuleLedgerContexts(f.Db));

            var all = await query.FindAccountsAsync(f.Context, null, 50, Cancellation).ConfigureAwait(true);
            Assert.Equal(2, all.Count);
            var one = await query.FindAccountsAsync(f.Context, "1001", 50, Cancellation).ConfigureAwait(true);
            Assert.Equal(f.Cash, Assert.Single(one).Id);
            Assert.Empty(await query.FindAccountsAsync(f.Context with { CompanyId = Guid.NewGuid() }, null, 50, Cancellation).ConfigureAwait(true));
            Assert.Single(await query.FindAccountsAsync(f.Context, null, 1, Cancellation).ConfigureAwait(true));
            Assert.Empty(await query.FindItemsAsync(f.Context, null, 50, Cancellation).ConfigureAwait(true));
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task ModuleActionIsWrittenToTheDurableSecurityAuditTrail()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var f = await StartAsync(database).ConfigureAwait(true);
            using var _ = f.Db;
            using var system = new SystemDbContext(new DbContextOptionsBuilder<SystemDbContext>().UseSqlServer(database.ConnectionString,
                sql => sql.UseCompatibilityLevel(database.CompatibilityLevel)).Options);

            await new SqlModuleAuditTrail(system, TimeProvider.System).RecordAsync(f.Context, "collection.printed", "Collection", "C-77", "printed once", Cancellation).ConfigureAwait(true);

            var rows = await f.Db.Database.SqlQuery<string>(
                $"SELECT [Details] AS [Value] FROM [security].[SecurityAuditEvents] WHERE [EventType] = 26").ToListAsync(Cancellation).ConfigureAwait(true);
            var details = Assert.Single(rows);
            Assert.Contains("demo.collections", details, StringComparison.Ordinal);
            Assert.Contains("C-77", details, StringComparison.Ordinal);
            await Assert.ThrowsAsync<BusinessRuleException>(() => new SqlModuleAuditTrail(system, TimeProvider.System)
                .RecordAsync(f.Context with { UserId = Guid.Empty }, "x", "y", "z", "", Cancellation)).ConfigureAwait(true);
        }
    }
}
