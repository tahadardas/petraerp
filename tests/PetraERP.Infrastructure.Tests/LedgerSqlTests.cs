using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Tests;

/// <summary>
/// SQL integration tests for the LedgerFoundation migration (slice 2 of phase 3).
/// These tests require an explicit SQL Server instance with PETRAERP_SQL_TEST_SERVER set;
/// they are Explicit and will not run unless opted in.
/// Each test creates its own isolated database, verifies assertions, then removes it.
/// </summary>
public sealed class LedgerSqlTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    // ── helpers ──────────────────────────────────────────────────────────────

    private static async Task<Guid> SeedCurrencyAndCompanyAsync(PetraDbContext ctx, string currency = "JOD")
    {
        await ctx.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO [system].[Currencies]([Code],[NameArabic],[IsActive]) VALUES({currency},N'دينار أردني',1)",
            Cancellation).ConfigureAwait(true);
        var companyId = Guid.NewGuid();
        await ctx.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO [system].[Companies]([Id],[Code],[NameArabic],[BaseCurrencyCode],[IsActive]) VALUES({companyId},N'ACME',N'شركة آكمي',{currency},1)",
            Cancellation).ConfigureAwait(true);
        return companyId;
    }

    private static async Task<Guid> SeedBranchAsync(PetraDbContext ctx, Guid companyId)
    {
        var branchId = Guid.NewGuid();
        await ctx.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO [system].[Branches]([Id],[CompanyId],[Code],[NameArabic],[IsActive]) VALUES({branchId},{companyId},N'HQ',N'المقر الرئيسي',1)",
            Cancellation).ConfigureAwait(true);
        return branchId;
    }

    private static async Task<Guid> SeedFiscalYearAsync(PetraDbContext ctx, Guid companyId, string code = "FY2026")
    {
        var yearId = Guid.NewGuid();
        await ctx.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO [ledger].[FiscalYears]([Id],[CompanyId],[Code],[RangeStart],[RangeEnd]) VALUES({yearId},{companyId},{code},'2026-01-01','2026-12-31')",
            Cancellation).ConfigureAwait(true);
        return yearId;
    }

    private static async Task<Guid> SeedPeriodAsync(PetraDbContext ctx, Guid companyId, Guid yearId, string code = "P01")
    {
        var periodId = Guid.NewGuid();
        await ctx.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO [ledger].[AccountingPeriods]([Id],[CompanyId],[FiscalYearId],[Code],[RangeStart],[RangeEnd],[State]) VALUES({periodId},{companyId},{yearId},{code},'2026-01-01','2026-01-31',0)",
            Cancellation).ConfigureAwait(true);
        return periodId;
    }

    private static async Task<Guid> SeedAccountAsync(PetraDbContext ctx, Guid companyId,
        string code = "1001", byte accountType = 1)
    {
        var accountId = Guid.NewGuid();
        await ctx.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO [ledger].[Accounts]([Id],[CompanyId],[Code],[NameArabic],[AccountType],[PostingAllowed],[CostCenterPolicy],[CurrencyMode],[IsActive]) VALUES({accountId},{companyId},{code},N'حساب نقدي',{accountType},1,2,1,1)",
            Cancellation).ConfigureAwait(true);
        return accountId;
    }

    private static async Task SqlFailure(Func<Task> action, params int[] expectedNumbers)
    {
        var exception = await Assert.ThrowsAsync<SqlException>(action).ConfigureAwait(false);
        Assert.Contains(exception.Number, expectedNumbers);
    }

    // ── tests ─────────────────────────────────────────────────────────────────

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task LedgerSchemaIsCreatedWithExpectedTables()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            using var ctx = database.Context();
            await ctx.Database.MigrateAsync(Cancellation).ConfigureAwait(true);

            var tables = new[]
            {
                "FiscalYears", "AccountingPeriods", "Accounts", "CostCenters",
                "JournalEntries", "JournalLines", "JournalAudit",
                "PostingRequests", "PostingSequences"
            };

            foreach (var table in tables)
            {
                var exists = await ctx.Database
                    .SqlQuery<int>($"SELECT COUNT(1) AS [Value] FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA='ledger' AND TABLE_NAME={table}")
                    .FirstAsync(Cancellation).ConfigureAwait(true);
                Assert.True(exists == 1, $"Expected table ledger.{table} to exist after LedgerFoundation migration.");
            }
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task FiscalYearUniqueCodePerCompanyIsEnforced()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            using var ctx = database.Context();
            await ctx.Database.MigrateAsync(Cancellation).ConfigureAwait(true);
            var companyId = await SeedCurrencyAndCompanyAsync(ctx).ConfigureAwait(true);
            await SeedFiscalYearAsync(ctx, companyId, "FY2026").ConfigureAwait(true);

            await SqlFailure(() => SeedFiscalYearAsync(ctx, companyId, "FY2026"), 2601, 2627).ConfigureAwait(true);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task AccountingPeriodStateCheckRejectsInvalidValues()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            using var ctx = database.Context();
            await ctx.Database.MigrateAsync(Cancellation).ConfigureAwait(true);
            var companyId = await SeedCurrencyAndCompanyAsync(ctx).ConfigureAwait(true);
            var yearId = await SeedFiscalYearAsync(ctx, companyId).ConfigureAwait(true);
            var periodId = Guid.NewGuid();

            await SqlFailure(() => ctx.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO [ledger].[AccountingPeriods]([Id],[CompanyId],[FiscalYearId],[Code],[RangeStart],[RangeEnd],[State]) VALUES({periodId},{companyId},{yearId},N'P99','2026-01-01','2026-01-31',5)",
                Cancellation), 547).ConfigureAwait(true);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task AccountFixedCurrencyConsistencyIsEnforced()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            using var ctx = database.Context();
            await ctx.Database.MigrateAsync(Cancellation).ConfigureAwait(true);
            var companyId = await SeedCurrencyAndCompanyAsync(ctx).ConfigureAwait(true);
            var accountId = Guid.NewGuid();

            // CurrencyMode=3 (FixedCurrency) without FixedCurrencyCode must fail CK_Accounts_FixedCurrency (547)
            await SqlFailure(() => ctx.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO [ledger].[Accounts]([Id],[CompanyId],[Code],[NameArabic],[AccountType],[PostingAllowed],[CostCenterPolicy],[CurrencyMode],[IsActive]) VALUES({accountId},{companyId},N'9001',N'حساب ثابت العملة',1,1,2,3,1)",
                Cancellation), 547).ConfigureAwait(true);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task AccountParentSelfReferenceIsRejected()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            using var ctx = database.Context();
            await ctx.Database.MigrateAsync(Cancellation).ConfigureAwait(true);
            var companyId = await SeedCurrencyAndCompanyAsync(ctx).ConfigureAwait(true);
            var accountId = await SeedAccountAsync(ctx, companyId).ConfigureAwait(true);

            // ParentId = Id must fail CK_Accounts_ParentNotSelf (547)
            await SqlFailure(() => ctx.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [ledger].[Accounts] SET [ParentId]={accountId} WHERE [Id]={accountId}",
                Cancellation), 547).ConfigureAwait(true);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task JournalEntryPostingStateConsistencyIsEnforced()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            using var ctx = database.Context();
            await ctx.Database.MigrateAsync(Cancellation).ConfigureAwait(true);
            var companyId = await SeedCurrencyAndCompanyAsync(ctx).ConfigureAwait(true);
            var branchId = await SeedBranchAsync(ctx, companyId).ConfigureAwait(true);
            var journalId = Guid.NewGuid();

            // State=4 (Posted) without PostingSequence must fail CK_JournalEntries_PostedState (547)
            await SqlFailure(() => ctx.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO [ledger].[JournalEntries]([Id],[CompanyId],[BranchId],[AccountingDate],[Description],[State]) VALUES({journalId},{companyId},{branchId},'2026-01-15',N'قيد ترحيل',4)",
                Cancellation), 547).ConfigureAwait(true);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task JournalEntrySelfReversalIsRejected()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            using var ctx = database.Context();
            await ctx.Database.MigrateAsync(Cancellation).ConfigureAwait(true);
            var companyId = await SeedCurrencyAndCompanyAsync(ctx).ConfigureAwait(true);
            var branchId = await SeedBranchAsync(ctx, companyId).ConfigureAwait(true);
            var journalId = Guid.NewGuid();
            await ctx.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO [ledger].[JournalEntries]([Id],[CompanyId],[BranchId],[AccountingDate],[Description],[State]) VALUES({journalId},{companyId},{branchId},'2026-01-15',N'قيد تجريبي',1)",
                Cancellation).ConfigureAwait(true);

            await SqlFailure(() => ctx.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [ledger].[JournalEntries] SET [ReversesEntryId]={journalId} WHERE [Id]={journalId}",
                Cancellation), 547).ConfigureAwait(true);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task JournalLineSingleSideAmountIsEnforced()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            using var ctx = database.Context();
            await ctx.Database.MigrateAsync(Cancellation).ConfigureAwait(true);
            var companyId = await SeedCurrencyAndCompanyAsync(ctx).ConfigureAwait(true);
            var branchId = await SeedBranchAsync(ctx, companyId).ConfigureAwait(true);
            var accountId = await SeedAccountAsync(ctx, companyId).ConfigureAwait(true);
            var journalId = Guid.NewGuid();
            await ctx.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO [ledger].[JournalEntries]([Id],[CompanyId],[BranchId],[AccountingDate],[Description],[State]) VALUES({journalId},{companyId},{branchId},'2026-01-15',N'قيد',1)",
                Cancellation).ConfigureAwait(true);

            var lineId = Guid.NewGuid();
            // Both Debit and Credit > 0 simultaneously — must fail CHECK (547)
            await SqlFailure(() => ctx.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO [ledger].[JournalLines]([Id],[CompanyId],[JournalId],[AccountId],[TransactionCurrencyCode],[TransactionDebit],[TransactionCredit],[BaseCurrencyCode],[BaseDebit],[BaseCredit],[RateSourceCurrencyCode],[RateTargetCurrencyCode],[RateValue],[Rounding],[Description]) VALUES({lineId},{companyId},{journalId},{accountId},'JOD',100.00,50.00,'JOD',100.00,50.00,'JOD','JOD',1.0,0,N'سطر')",
                Cancellation), 547).ConfigureAwait(true);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task PostingSequenceCompositePkPreventsRaceConditionDuplicate()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            using var ctx = database.Context();
            await ctx.Database.MigrateAsync(Cancellation).ConfigureAwait(true);
            var companyId = await SeedCurrencyAndCompanyAsync(ctx).ConfigureAwait(true);
            var branchId = await SeedBranchAsync(ctx, companyId).ConfigureAwait(true);
            var yearId = await SeedFiscalYearAsync(ctx, companyId).ConfigureAwait(true);

            await ctx.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO [ledger].[PostingSequences]([CompanyId],[BranchId],[FiscalYearId],[LastSequence]) VALUES({companyId},{branchId},{yearId},0)",
                Cancellation).ConfigureAwait(true);

            await SqlFailure(() => ctx.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO [ledger].[PostingSequences]([CompanyId],[BranchId],[FiscalYearId],[LastSequence]) VALUES({companyId},{branchId},{yearId},0)",
                Cancellation), 2601, 2627).ConfigureAwait(true);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task JournalAuditUniqueRevisionPerJournalIsEnforced()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            using var ctx = database.Context();
            await ctx.Database.MigrateAsync(Cancellation).ConfigureAwait(true);
            var companyId = await SeedCurrencyAndCompanyAsync(ctx).ConfigureAwait(true);
            var branchId = await SeedBranchAsync(ctx, companyId).ConfigureAwait(true);
            var journalId = Guid.NewGuid();
            var actorId = Guid.NewGuid();
            var corrId = Guid.NewGuid();
            await ctx.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO [ledger].[JournalEntries]([Id],[CompanyId],[BranchId],[AccountingDate],[Description],[State]) VALUES({journalId},{companyId},{branchId},'2026-01-15',N'قيد',1)",
                Cancellation).ConfigureAwait(true);

            var event1 = Guid.NewGuid();
            await ctx.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO [ledger].[JournalAudit]([EventId],[CompanyId],[BranchId],[JournalId],[Revision],[Action],[NewState],[ActorUserId],[CorrelationId],[Reason],[OccurredAtUtc]) VALUES({event1},{companyId},{branchId},{journalId},1,1,1,{actorId},{corrId},N'إنشاء قيد','2026-01-15T10:00:00+00:00')",
                Cancellation).ConfigureAwait(true);

            // Duplicate revision for same journal — must fail UQ_JournalAudit_JournalRevision (2601/2627)
            var event2 = Guid.NewGuid();
            await SqlFailure(() => ctx.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO [ledger].[JournalAudit]([EventId],[CompanyId],[BranchId],[JournalId],[Revision],[Action],[NewState],[ActorUserId],[CorrelationId],[Reason],[OccurredAtUtc]) VALUES({event2},{companyId},{branchId},{journalId},1,3,2,{actorId},{corrId},N'تحقق قيد','2026-01-15T10:01:00+00:00')",
                Cancellation), 2601, 2627).ConfigureAwait(true);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task PostingRequestReversalConsistencyIsEnforced()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            using var ctx = database.Context();
            await ctx.Database.MigrateAsync(Cancellation).ConfigureAwait(true);
            var companyId = await SeedCurrencyAndCompanyAsync(ctx).ConfigureAwait(true);
            var branchId = await SeedBranchAsync(ctx, companyId).ConfigureAwait(true);
            var key = Guid.NewGuid();
            var journalId = Guid.NewGuid();
            var actorId = Guid.NewGuid();
            var fingerprint = new string('A', 64);

            // Operation=2 (Reversal) without OriginalJournalId must fail CK_PostingRequests_ReversalConsistency (547)
            await SqlFailure(() => ctx.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO [ledger].[PostingRequests]([IdempotencyKey],[CompanyId],[BranchId],[ActorUserId],[Operation],[JournalId],[ExpectedRevision],[ExpectedVersionBase64],[Reason],[FingerprintHex]) VALUES({key},{companyId},{branchId},{actorId},2,{journalId},1,N'dmVyc2lvbg==',N'عكس قيد',{fingerprint})",
                Cancellation), 547).ConfigureAwait(true);
        }
    }
}
