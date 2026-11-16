using Microsoft.EntityFrameworkCore;
using PetraERP.Application.GeneralLedger.Posting;
using PetraERP.Application.Security;
using PetraERP.Domain.GeneralLedger;
using PetraERP.Domain.Primitives;
using PetraERP.Infrastructure.GeneralLedger;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Tests;

/// <summary>
/// SQL Server integration tests for the durable posting service (SqlPostingService).
/// Verifies atomic sequence allocation, idempotent replay, semantic fingerprint conflict detection,
/// optimistic concurrency via rowversion, period state enforcement, paired reversal posting,
/// and concurrent posting race condition resolution.
/// Explicitly tagged with Category=SqlServer; requires PETRAERP_SQL_TEST_SERVER.
/// </summary>
public sealed class PostingSqlTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    // ── Fixture Seed Helpers ──────────────────────────────────────────────────

    internal static async Task<Guid> SeedAuthorizedActorAsync(
        PetraDbContext ctx, Guid companyId, Guid branchId, bool canReverse = false, string userName = "poster")
    {
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var normalized = userName.ToUpperInvariant();
        var roleCode = ("ROLE_" + Guid.NewGuid().ToString("N")).ToUpperInvariant();
        var salt = new byte[32];
        salt[0] = 1;
        var hash = new byte[64];
        hash[0] = 1;

        await ctx.Database.ExecuteSqlInterpolatedAsync(
            $@"INSERT INTO [security].[Users]
                ([Id],[UserName],[NormalizedUserName],[DisplayNameArabic],[DisplayNameEnglish],
                 [PasswordHash],[PasswordSalt],[PasswordIterations],[PasswordAlgorithm],[PasswordChangedAtUtc],
                 [MustChangePassword],[IsActive],[AccessFailedCount],[SecurityStamp],
                 [CreatedAtUtc],[UpdatedAtUtc])
                VALUES
                ({userId},{userName},{normalized},N'مستخدم ترحيل',N'Posting Actor',
                 {hash},{salt},100000,1,{now},
                 0,1,0,{Guid.NewGuid()},{now},{now})",
            Cancellation).ConfigureAwait(true);

        await ctx.Database.ExecuteSqlInterpolatedAsync(
            $@"INSERT INTO [security].[Roles]
                ([Id],[Code],[NameArabic],[NameEnglish],[CreatedAtUtc],[UpdatedAtUtc])
                VALUES
                ({roleId},{roleCode},N'دور ترحيل',N'Posting Role',{now},{now})",
            Cancellation).ConfigureAwait(true);

        await ctx.Database.ExecuteSqlInterpolatedAsync(
            $@"INSERT INTO [security].[RolePermissions] ([RoleId],[PermissionCode]) VALUES ({roleId},{PermissionCatalog.JournalsPost})",
            Cancellation).ConfigureAwait(true);

        if (canReverse)
        {
            await ctx.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO [security].[RolePermissions] ([RoleId],[PermissionCode]) VALUES ({roleId},{PermissionCatalog.JournalsReverse})",
                Cancellation).ConfigureAwait(true);
        }

        await ctx.Database.ExecuteSqlInterpolatedAsync(
            $@"INSERT INTO [security].[UserRoles] ([UserId],[RoleId]) VALUES ({userId},{roleId})",
            Cancellation).ConfigureAwait(true);

        await ctx.Database.ExecuteSqlInterpolatedAsync(
            $@"INSERT INTO [security].[UserCompanyScopes] ([UserId],[CompanyId]) VALUES ({userId},{companyId})",
            Cancellation).ConfigureAwait(true);

        await ctx.Database.ExecuteSqlInterpolatedAsync(
            $@"INSERT INTO [security].[UserBranchScopes] ([UserId],[CompanyId],[BranchId]) VALUES ({userId},{companyId},{branchId})",
            Cancellation).ConfigureAwait(true);

        return userId;
    }

    internal static async Task<Guid> SeedCompanyAsync(PetraDbContext ctx, string currency = "JOD")
    {
        await ctx.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO [system].[Currencies]([Code],[NameArabic],[IsActive]) VALUES({currency},N'دينار أردني',1)",
            Cancellation).ConfigureAwait(true);

        var companyId = Guid.NewGuid();
        await ctx.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO [system].[Companies]([Id],[Code],[NameArabic],[BaseCurrencyCode],[IsActive]) VALUES({companyId},N'ACME',N'شركة آكمي التجارية',{currency},1)",
            Cancellation).ConfigureAwait(true);

        return companyId;
    }

    internal static async Task<Guid> SeedBranchAsync(PetraDbContext ctx, Guid companyId, string code = "HQ")
    {
        var branchId = Guid.NewGuid();
        await ctx.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO [system].[Branches]([Id],[CompanyId],[Code],[NameArabic],[IsActive]) VALUES({branchId},{companyId},{code},N'المقر الرئيسي',1)",
            Cancellation).ConfigureAwait(true);

        return branchId;
    }

    internal static async Task<(Guid YearId, Guid PeriodId)> SeedFiscalYearAndPeriodsAsync(
        PetraDbContext ctx, Guid companyId, int year = 2026)
    {
        var yearId = Guid.NewGuid();
        var yearCode = $"FY{year}";
        var rangeStart = new DateOnly(year, 1, 1);
        var rangeEnd = new DateOnly(year, 12, 31);

        await ctx.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO [ledger].[FiscalYears]([Id],[CompanyId],[Code],[RangeStart],[RangeEnd]) VALUES({yearId},{companyId},{yearCode},{rangeStart},{rangeEnd})",
            Cancellation).ConfigureAwait(true);

        Guid janPeriodId = Guid.Empty;
        var start = rangeStart;

        for (var month = 1; month <= 12; month++)
        {
            var periodId = Guid.NewGuid();
            if (month == 1)
            {
                janPeriodId = periodId;
            }

            var periodEnd = start.AddMonths(1).AddDays(-1);
            var periodCode = $"P{month:D2}";

            await ctx.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO [ledger].[AccountingPeriods]([Id],[CompanyId],[FiscalYearId],[Code],[RangeStart],[RangeEnd],[State]) VALUES({periodId},{companyId},{yearId},{periodCode},{start},{periodEnd},0)",
                Cancellation).ConfigureAwait(true);

            start = periodEnd.AddDays(1);
        }

        return (yearId, janPeriodId);
    }

    internal static async Task<(Guid CashAccountId, Guid RevenueAccountId)> SeedAccountsAsync(
        PetraDbContext ctx, Guid companyId)
    {
        var cashId = Guid.NewGuid();
        await ctx.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO [ledger].[Accounts]([Id],[CompanyId],[Code],[NameArabic],[AccountType],[PostingAllowed],[CostCenterPolicy],[CurrencyMode],[IsActive]) VALUES({cashId},{companyId},N'1001',N'صندوق المركز',1,1,1,1,1)",
            Cancellation).ConfigureAwait(true);

        var revId = Guid.NewGuid();
        await ctx.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO [ledger].[Accounts]([Id],[CompanyId],[Code],[NameArabic],[AccountType],[PostingAllowed],[CostCenterPolicy],[CurrencyMode],[IsActive]) VALUES({revId},{companyId},N'4001',N'إيرادات المبيعات',4,1,1,1,1)",
            Cancellation).ConfigureAwait(true);

        return (cashId, revId);
    }

    private static async Task<(Guid JournalId, byte[] RowVersion, Guid DebitLineId, Guid CreditLineId)> SeedApprovedJournalAsync(
        PetraDbContext ctx, Guid companyId, Guid branchId, Guid debitAccountId, Guid creditAccountId,
        decimal amount = 100.000000m, DateOnly? accountingDate = null)
    {
        var journalId = Guid.NewGuid();
        var date = accountingDate ?? new DateOnly(2026, 1, 15);
        var actorId = Guid.NewGuid();
        var corrId = Guid.NewGuid();

        await ctx.Database.ExecuteSqlInterpolatedAsync(
            $@"INSERT INTO [ledger].[JournalEntries]
                ([Id],[CompanyId],[BranchId],[AccountingDate],[Description],[State])
                VALUES({journalId},{companyId},{branchId},{date},N'قيد مبيعات نقدية',3)",
            Cancellation).ConfigureAwait(true);

        var line1Id = Guid.NewGuid();
        await ctx.Database.ExecuteSqlInterpolatedAsync(
            $@"INSERT INTO [ledger].[JournalLines]
                ([Id],[CompanyId],[JournalId],[AccountId],[TransactionCurrencyCode],[TransactionDebit],[TransactionCredit],
                 [BaseCurrencyCode],[BaseDebit],[BaseCredit],[RateSourceCurrencyCode],[RateTargetCurrencyCode],[RateValue],[Rounding],[Description])
                VALUES({line1Id},{companyId},{journalId},{debitAccountId},N'JOD',{amount},0,N'JOD',{amount},0,N'JOD',N'JOD',1.0,0,N'قبض نقدي')",
            Cancellation).ConfigureAwait(true);

        var line2Id = Guid.NewGuid();
        await ctx.Database.ExecuteSqlInterpolatedAsync(
            $@"INSERT INTO [ledger].[JournalLines]
                ([Id],[CompanyId],[JournalId],[AccountId],[TransactionCurrencyCode],[TransactionDebit],[TransactionCredit],
                 [BaseCurrencyCode],[BaseDebit],[BaseCredit],[RateSourceCurrencyCode],[RateTargetCurrencyCode],[RateValue],[Rounding],[Description])
                VALUES({line2Id},{companyId},{journalId},{creditAccountId},N'JOD',0,{amount},N'JOD',0,{amount},N'JOD',N'JOD',1.0,0,N'إيراد مبيعات')",
            Cancellation).ConfigureAwait(true);

        // Seed domain revision lifecycle: Draft (1) -> Validated (2) -> Approved (3)
        var t1 = DateTimeOffset.UtcNow.AddMinutes(-30);
        var t2 = DateTimeOffset.UtcNow.AddMinutes(-20);
        var t3 = DateTimeOffset.UtcNow.AddMinutes(-10);

        await ctx.Database.ExecuteSqlInterpolatedAsync(
            $@"INSERT INTO [ledger].[JournalAudit]
                ([EventId],[CompanyId],[BranchId],[JournalId],[Revision],[Action],[PreviousState],[NewState],[ActorUserId],[CorrelationId],[Reason],[OccurredAtUtc])
                VALUES({Guid.NewGuid()},{companyId},{branchId},{journalId},1,1,NULL,1,{actorId},{corrId},N'إنشاء مسودة قيد',{t1})",
            Cancellation).ConfigureAwait(true);

        await ctx.Database.ExecuteSqlInterpolatedAsync(
            $@"INSERT INTO [ledger].[JournalAudit]
                ([EventId],[CompanyId],[BranchId],[JournalId],[Revision],[Action],[PreviousState],[NewState],[ActorUserId],[CorrelationId],[Reason],[OccurredAtUtc])
                VALUES({Guid.NewGuid()},{companyId},{branchId},{journalId},2,3,1,2,{actorId},{corrId},N'تدقيق القيد',{t2})",
            Cancellation).ConfigureAwait(true);

        await ctx.Database.ExecuteSqlInterpolatedAsync(
            $@"INSERT INTO [ledger].[JournalAudit]
                ([EventId],[CompanyId],[BranchId],[JournalId],[Revision],[Action],[PreviousState],[NewState],[ActorUserId],[CorrelationId],[Reason],[OccurredAtUtc])
                VALUES({Guid.NewGuid()},{companyId},{branchId},{journalId},3,4,2,3,{actorId},{corrId},N'اعتماد القيد للترحيل',{t3})",
            Cancellation).ConfigureAwait(true);

        var rowVersion = await ctx.Database
            .SqlQuery<byte[]>($"SELECT [RowVersion] AS [Value] FROM [ledger].[JournalEntries] WHERE [Id] = {journalId}")
            .FirstAsync(Cancellation).ConfigureAwait(true);

        return (journalId, rowVersion, line1Id, line2Id);
    }

    private static async Task<(Guid JournalId, byte[] RowVersion)> SeedApprovedReversalDraftAsync(
        PetraDbContext ctx, Guid companyId, Guid branchId, Guid originalJournalId,
        Guid originalDebitLineId, Guid originalCreditLineId,
        Guid debitAccountId, Guid creditAccountId, decimal amount = 100.000000m)
    {
        var reversalId = Guid.NewGuid();
        var date = new DateOnly(2026, 1, 16);
        var actorId = Guid.NewGuid();
        var corrId = Guid.NewGuid();

        await ctx.Database.ExecuteSqlInterpolatedAsync(
            $@"INSERT INTO [ledger].[JournalEntries]
                ([Id],[CompanyId],[BranchId],[AccountingDate],[Description],[State],[ReversesEntryId])
                VALUES({reversalId},{companyId},{branchId},{date},N'عكس قيد مبيعات',3,{originalJournalId})",
            Cancellation).ConfigureAwait(true);

        // Swap sides: original Debit becomes Credit, original Credit becomes Debit
        var revLine1Id = Guid.NewGuid();
        await ctx.Database.ExecuteSqlInterpolatedAsync(
            $@"INSERT INTO [ledger].[JournalLines]
                ([Id],[CompanyId],[JournalId],[AccountId],[TransactionCurrencyCode],[TransactionDebit],[TransactionCredit],
                 [BaseCurrencyCode],[BaseDebit],[BaseCredit],[RateSourceCurrencyCode],[RateTargetCurrencyCode],[RateValue],[Rounding],[Description],[ReversesLineId])
                VALUES({revLine1Id},{companyId},{reversalId},{debitAccountId},N'JOD',0,{amount},N'JOD',0,{amount},N'JOD',N'JOD',1.0,0,N'عكس قبض نقدي',{originalDebitLineId})",
            Cancellation).ConfigureAwait(true);

        var revLine2Id = Guid.NewGuid();
        await ctx.Database.ExecuteSqlInterpolatedAsync(
            $@"INSERT INTO [ledger].[JournalLines]
                ([Id],[CompanyId],[JournalId],[AccountId],[TransactionCurrencyCode],[TransactionDebit],[TransactionCredit],
                 [BaseCurrencyCode],[BaseDebit],[BaseCredit],[RateSourceCurrencyCode],[RateTargetCurrencyCode],[RateValue],[Rounding],[Description],[ReversesLineId])
                VALUES({revLine2Id},{companyId},{reversalId},{creditAccountId},N'JOD',{amount},0,N'JOD',{amount},0,N'JOD',N'JOD',1.0,0,N'عكس إيراد مبيعات',{originalCreditLineId})",
            Cancellation).ConfigureAwait(true);

        var t1 = DateTimeOffset.UtcNow.AddMinutes(-25);
        var t2 = DateTimeOffset.UtcNow.AddMinutes(-15);
        var t3 = DateTimeOffset.UtcNow.AddMinutes(-5);

        await ctx.Database.ExecuteSqlInterpolatedAsync(
            $@"INSERT INTO [ledger].[JournalAudit]
                ([EventId],[CompanyId],[BranchId],[JournalId],[Revision],[Action],[PreviousState],[NewState],[ActorUserId],[CorrelationId],[Reason],[OccurredAtUtc],[RelatedEntryId])
                VALUES({Guid.NewGuid()},{companyId},{branchId},{reversalId},1,1,NULL,1,{actorId},{corrId},N'إنشاء مسودة عكس القيد',{t1},{originalJournalId})",
            Cancellation).ConfigureAwait(true);

        await ctx.Database.ExecuteSqlInterpolatedAsync(
            $@"INSERT INTO [ledger].[JournalAudit]
                ([EventId],[CompanyId],[BranchId],[JournalId],[Revision],[Action],[PreviousState],[NewState],[ActorUserId],[CorrelationId],[Reason],[OccurredAtUtc],[RelatedEntryId])
                VALUES({Guid.NewGuid()},{companyId},{branchId},{reversalId},2,3,1,2,{actorId},{corrId},N'تدقيق مسودة العكس',{t2},{originalJournalId})",
            Cancellation).ConfigureAwait(true);

        await ctx.Database.ExecuteSqlInterpolatedAsync(
            $@"INSERT INTO [ledger].[JournalAudit]
                ([EventId],[CompanyId],[BranchId],[JournalId],[Revision],[Action],[PreviousState],[NewState],[ActorUserId],[CorrelationId],[Reason],[OccurredAtUtc],[RelatedEntryId])
                VALUES({Guid.NewGuid()},{companyId},{branchId},{reversalId},3,4,2,3,{actorId},{corrId},N'اعتماد العكس للترحيل',{t3},{originalJournalId})",
            Cancellation).ConfigureAwait(true);

        var rowVersion = await ctx.Database
            .SqlQuery<byte[]>($"SELECT [RowVersion] AS [Value] FROM [ledger].[JournalEntries] WHERE [Id] = {reversalId}")
            .FirstAsync(Cancellation).ConfigureAwait(true);

        return (reversalId, rowVersion);
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task PostJournalSucceedsAndAllocatesSequentialNumber()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            using var ctx = database.Context();
            await ctx.Database.MigrateAsync(Cancellation).ConfigureAwait(true);

            var companyId = await SeedCompanyAsync(ctx).ConfigureAwait(true);
            var branchId = await SeedBranchAsync(ctx, companyId).ConfigureAwait(true);
            var (yearId, periodId) = await SeedFiscalYearAndPeriodsAsync(ctx, companyId).ConfigureAwait(true);
            var (cashId, revId) = await SeedAccountsAsync(ctx, companyId).ConfigureAwait(true);
            var (journalId, rowVersion, _, _) = await SeedApprovedJournalAsync(ctx, companyId, branchId, cashId, revId).ConfigureAwait(true);

            var actorUserId = await SeedAuthorizedActorAsync(ctx, companyId, branchId, userName: "actor1").ConfigureAwait(true);
            var idempotencyKey = Guid.NewGuid();
            var scope = PostingRequestScope.Create(companyId, branchId, actorUserId);
            var expectedVersion = ExpectedJournalVersion.Create(rowVersion);
            var reason = AuditReason.Create("ترحيل قيد مبيعات يومية معتمد");
            var request = PostingRequest.Post(scope, idempotencyKey, journalId, expectedVersion, reason);

            var service = new SqlPostingService(ctx);
            var receipt = await service.PostAsync(request, Cancellation).ConfigureAwait(true);

            // Assertions on receipt
            Assert.NotNull(receipt);
            Assert.Equal(journalId, receipt.JournalId);
            Assert.Equal(1, receipt.PostingNumber.Sequence);
            Assert.Equal(PostingOperation.PostJournal, receipt.Operation);
            Assert.Equal(idempotencyKey, receipt.IdempotencyKey);

            // Assertions on database JournalEntries row
            var journalRow = await ctx.JournalEntries.AsNoTracking().FirstAsync(j => j.Id == journalId, Cancellation).ConfigureAwait(true);
            Assert.Equal((byte)JournalEntryState.Posted, journalRow.State);
            Assert.Equal(1L, journalRow.PostingSequence);
            Assert.Equal(yearId, journalRow.PostingFiscalYearId);
            Assert.Equal(periodId, journalRow.PostingPeriodId);

            // Assertions on database PostingSequences row
            var seq = await ctx.Database
                .SqlQuery<long>($"SELECT [LastSequence] AS [Value] FROM [ledger].[PostingSequences] WHERE [CompanyId]={companyId} AND [BranchId]={branchId} AND [FiscalYearId]={yearId}")
                .FirstAsync(Cancellation).ConfigureAwait(true);
            Assert.Equal(1L, seq);

            // Assertions on database JournalAudit rows
            var audits = await ctx.JournalAudit.AsNoTracking().Where(a => a.JournalId == journalId).OrderBy(a => a.Revision).ToListAsync(Cancellation).ConfigureAwait(true);
            Assert.Equal(4, audits.Count);
            var lastAudit = audits[^1];
            Assert.Equal(4L, lastAudit.Revision);
            Assert.Equal((byte)JournalAuditAction.Posted, lastAudit.Action);
            Assert.Equal((byte)JournalEntryState.Approved, lastAudit.PreviousState);
            Assert.Equal((byte)JournalEntryState.Posted, lastAudit.NewState);
            Assert.Equal(actorUserId, lastAudit.ActorUserId);
            Assert.Equal(reason.Value, lastAudit.Reason);

            // Assertions on database PostingRequests row
            var reqRow = await ctx.PostingRequests.AsNoTracking().FirstAsync(r => r.IdempotencyKey == idempotencyKey, Cancellation).ConfigureAwait(true);
            Assert.NotNull(reqRow.CompletedAtUtc);
            Assert.Equal(journalId, reqRow.OutcomeJournalId);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task IdempotentReplayReturnsSameReceiptWithoutReExecuting()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            using var ctx = database.Context();
            await ctx.Database.MigrateAsync(Cancellation).ConfigureAwait(true);

            var companyId = await SeedCompanyAsync(ctx).ConfigureAwait(true);
            var branchId = await SeedBranchAsync(ctx, companyId).ConfigureAwait(true);
            var (yearId, _) = await SeedFiscalYearAndPeriodsAsync(ctx, companyId).ConfigureAwait(true);
            var (cashId, revId) = await SeedAccountsAsync(ctx, companyId).ConfigureAwait(true);
            var (journalId, rowVersion, _, _) = await SeedApprovedJournalAsync(ctx, companyId, branchId, cashId, revId).ConfigureAwait(true);

            var actorUserId = await SeedAuthorizedActorAsync(ctx, companyId, branchId, userName: "actor2").ConfigureAwait(true);
            var idempotencyKey = Guid.NewGuid();
            var scope = PostingRequestScope.Create(companyId, branchId, actorUserId);
            var expectedVersion = ExpectedJournalVersion.Create(rowVersion);
            var reason = AuditReason.Create("ترحيل قيد مبيعات أول مرة");
            var request = PostingRequest.Post(scope, idempotencyKey, journalId, expectedVersion, reason);

            var service = new SqlPostingService(ctx);
            var firstReceipt = await service.PostAsync(request, Cancellation).ConfigureAwait(true);

            // Replay call with identical request
            var replayReceipt = await service.PostAsync(request, Cancellation).ConfigureAwait(true);

            Assert.Equal(firstReceipt.PostingNumber.Sequence, replayReceipt.PostingNumber.Sequence);
            Assert.Equal(firstReceipt.JournalId, replayReceipt.JournalId);
            Assert.Equal(firstReceipt.IdempotencyKey, replayReceipt.IdempotencyKey);
            Assert.Equal(PostingReplayDecision.ReturnRecordedResult, firstReceipt.EvaluateReplay(request));

            // Verify sequence was not incremented
            var seq = await ctx.Database
                .SqlQuery<long>($"SELECT [LastSequence] AS [Value] FROM [ledger].[PostingSequences] WHERE [CompanyId]={companyId} AND [BranchId]={branchId} AND [FiscalYearId]={yearId}")
                .FirstAsync(Cancellation).ConfigureAwait(true);
            Assert.Equal(1L, seq);

            // Verify audit row was not duplicated
            var auditCount = await ctx.JournalAudit.CountAsync(a => a.JournalId == journalId, Cancellation).ConfigureAwait(true);
            Assert.Equal(4, auditCount);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task ConflictingPayloadWithSameKeyThrowsIdempotencyConflict()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            using var ctx = database.Context();
            await ctx.Database.MigrateAsync(Cancellation).ConfigureAwait(true);

            var companyId = await SeedCompanyAsync(ctx).ConfigureAwait(true);
            var branchId = await SeedBranchAsync(ctx, companyId).ConfigureAwait(true);
            var (yearId, _) = await SeedFiscalYearAndPeriodsAsync(ctx, companyId).ConfigureAwait(true);
            var (cashId, revId) = await SeedAccountsAsync(ctx, companyId).ConfigureAwait(true);
            var (journalId, rowVersion, _, _) = await SeedApprovedJournalAsync(ctx, companyId, branchId, cashId, revId).ConfigureAwait(true);

            var actorUserId = await SeedAuthorizedActorAsync(ctx, companyId, branchId, userName: "actor3").ConfigureAwait(true);
            var idempotencyKey = Guid.NewGuid();
            var scope = PostingRequestScope.Create(companyId, branchId, actorUserId);
            var expectedVersion = ExpectedJournalVersion.Create(rowVersion);
            var reason1 = AuditReason.Create("السبب الأصلي للترحيل");
            var request1 = PostingRequest.Post(scope, idempotencyKey, journalId, expectedVersion, reason1);

            var service = new SqlPostingService(ctx);
            _ = await service.PostAsync(request1, Cancellation).ConfigureAwait(true);

            // Conflicting request: same IdempotencyKey but different Reason
            var reason2 = AuditReason.Create("سبب متضارب ومختلف تماماً");
            var request2 = PostingRequest.Post(scope, idempotencyKey, journalId, expectedVersion, reason2);

            var ex = await Assert.ThrowsAsync<PostingIdempotencyConflictException>(
                () => service.PostAsync(request2, Cancellation)).ConfigureAwait(true);

            Assert.Equal(idempotencyKey, ex.IdempotencyKey);

            // Counter must remain 1
            var seq = await ctx.Database
                .SqlQuery<long>($"SELECT [LastSequence] AS [Value] FROM [ledger].[PostingSequences] WHERE [CompanyId]={companyId} AND [BranchId]={branchId} AND [FiscalYearId]={yearId}")
                .FirstAsync(Cancellation).ConfigureAwait(true);
            Assert.Equal(1L, seq);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task ConcurrencyRowVersionMismatchThrowsPostingConcurrencyException()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            using var ctx = database.Context();
            await ctx.Database.MigrateAsync(Cancellation).ConfigureAwait(true);

            var companyId = await SeedCompanyAsync(ctx).ConfigureAwait(true);
            var branchId = await SeedBranchAsync(ctx, companyId).ConfigureAwait(true);
            await SeedFiscalYearAndPeriodsAsync(ctx, companyId).ConfigureAwait(true);
            var (cashId, revId) = await SeedAccountsAsync(ctx, companyId).ConfigureAwait(true);
            var (journalId, _, _, _) = await SeedApprovedJournalAsync(ctx, companyId, branchId, cashId, revId).ConfigureAwait(true);

            var actorUserId = await SeedAuthorizedActorAsync(ctx, companyId, branchId, userName: "actor4").ConfigureAwait(true);
            var idempotencyKey = Guid.NewGuid();
            var scope = PostingRequestScope.Create(companyId, branchId, actorUserId);

            // Stale / mismatching rowversion
            var staleBytes = new byte[8];
            staleBytes[7] = 255;
            var staleVersion = ExpectedJournalVersion.Create(staleBytes);
            var reason = AuditReason.Create("محاولة ترحيل بإصدار متقادم");
            var request = PostingRequest.Post(scope, idempotencyKey, journalId, staleVersion, reason);

            var service = new SqlPostingService(ctx);
            var ex = await Assert.ThrowsAsync<PostingConcurrencyException>(
                () => service.PostAsync(request, Cancellation)).ConfigureAwait(true);

            Assert.Equal(journalId, ex.JournalId);

            // Journal must remain in Approved state
            var state = await ctx.Database
                .SqlQuery<byte>($"SELECT [State] AS [Value] FROM [ledger].[JournalEntries] WHERE [Id] = {journalId}")
                .FirstAsync(Cancellation).ConfigureAwait(true);
            Assert.Equal((byte)JournalEntryState.Approved, state);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task ClosedPeriodRejectsPosting()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            using var ctx = database.Context();
            await ctx.Database.MigrateAsync(Cancellation).ConfigureAwait(true);

            var companyId = await SeedCompanyAsync(ctx).ConfigureAwait(true);
            var branchId = await SeedBranchAsync(ctx, companyId).ConfigureAwait(true);
            var (_, periodId) = await SeedFiscalYearAndPeriodsAsync(ctx, companyId).ConfigureAwait(true);
            var (cashId, revId) = await SeedAccountsAsync(ctx, companyId).ConfigureAwait(true);
            var (journalId, rowVersion, _, _) = await SeedApprovedJournalAsync(ctx, companyId, branchId, cashId, revId).ConfigureAwait(true);

            // Close period P01: State = 2 (Closed)
            await ctx.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [ledger].[AccountingPeriods] SET [State] = 2 WHERE [Id] = {periodId}",
                Cancellation).ConfigureAwait(true);

            var actorUserId = await SeedAuthorizedActorAsync(ctx, companyId, branchId, userName: "actor5").ConfigureAwait(true);
            var idempotencyKey = Guid.NewGuid();
            var scope = PostingRequestScope.Create(companyId, branchId, actorUserId);
            var expectedVersion = ExpectedJournalVersion.Create(rowVersion);
            var reason = AuditReason.Create("محاولة ترحيل في فترة مقفلة");
            var request = PostingRequest.Post(scope, idempotencyKey, journalId, expectedVersion, reason);

            var service = new SqlPostingService(ctx);
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.PostAsync(request, Cancellation)).ConfigureAwait(true);

            Assert.Contains("not open", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(PetraERP.Domain.Primitives.RuleViolation.PeriodClosed, PetraERP.Domain.Primitives.RuleViolation.CodeOf(ex));

            // Journal remains in Approved state
            var state = await ctx.Database
                .SqlQuery<byte>($"SELECT [State] AS [Value] FROM [ledger].[JournalEntries] WHERE [Id] = {journalId}")
                .FirstAsync(Cancellation).ConfigureAwait(true);
            Assert.Equal((byte)JournalEntryState.Approved, state);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task PostReversalSucceedsAndLinksBothJournals()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            using var ctx = database.Context();
            await ctx.Database.MigrateAsync(Cancellation).ConfigureAwait(true);

            var companyId = await SeedCompanyAsync(ctx).ConfigureAwait(true);
            var branchId = await SeedBranchAsync(ctx, companyId).ConfigureAwait(true);
            var (yearId, _) = await SeedFiscalYearAndPeriodsAsync(ctx, companyId).ConfigureAwait(true);
            var (cashId, revId) = await SeedAccountsAsync(ctx, companyId).ConfigureAwait(true);

            // 1. Post original journal J1
            var (origJournalId, origRowVersion, origDebitLineId, origCreditLineId) =
                await SeedApprovedJournalAsync(ctx, companyId, branchId, cashId, revId).ConfigureAwait(true);

            var actorUserId = await SeedAuthorizedActorAsync(ctx, companyId, branchId, userName: "rev_actor", canReverse: true).ConfigureAwait(true);
            var scope = PostingRequestScope.Create(companyId, branchId, actorUserId);
            var service = new SqlPostingService(ctx);

            var postRequest = PostingRequest.Post(
                scope, Guid.NewGuid(), origJournalId,
                ExpectedJournalVersion.Create(origRowVersion),
                AuditReason.Create("ترحيل القيد الأصلي"));

            var postReceipt = await service.PostAsync(postRequest, Cancellation).ConfigureAwait(true);
            Assert.Equal(1L, postReceipt.PostingNumber.Sequence);

            // 2. Fetch fresh rowversion of posted J1
            var freshOrigRowVersion = await ctx.Database
                .SqlQuery<byte[]>($"SELECT [RowVersion] AS [Value] FROM [ledger].[JournalEntries] WHERE [Id] = {origJournalId}")
                .FirstAsync(Cancellation).ConfigureAwait(true);

            // 3. Seed approved reversal journal J2
            var (revJournalId, revRowVersion) = await SeedApprovedReversalDraftAsync(
                ctx, companyId, branchId, origJournalId, origDebitLineId, origCreditLineId,
                cashId, revId).ConfigureAwait(true);

            // 4. Post reversal
            var revKey = Guid.NewGuid();
            var revReason = AuditReason.Create("عكس القيد لخطأ في تسجيل العملية الأصلية");
            var revRequest = PostingRequest.Reverse(
                scope, revKey, revJournalId,
                ExpectedJournalVersion.Create(revRowVersion),
                origJournalId,
                ExpectedJournalVersion.Create(freshOrigRowVersion),
                revReason);

            var revReceipt = await service.PostAsync(revRequest, Cancellation).ConfigureAwait(true);

            // Assertions on reversal receipt
            Assert.Equal(PostingOperation.PostJournalReversal, revReceipt.Operation);
            Assert.Equal(revJournalId, revReceipt.JournalId);
            Assert.Equal(origJournalId, revReceipt.OriginalJournalId);
            Assert.Equal(2L, revReceipt.PostingNumber.Sequence);

            // Database assertions on Reversal Journal J2
            var j2Row = await ctx.JournalEntries.AsNoTracking().FirstAsync(j => j.Id == revJournalId, Cancellation).ConfigureAwait(true);
            Assert.Equal((byte)JournalEntryState.Posted, j2Row.State);
            Assert.Equal(2L, j2Row.PostingSequence);
            Assert.Equal(origJournalId, j2Row.ReversesEntryId);

            // Database assertions on Original Journal J1
            var j1Row = await ctx.JournalEntries.AsNoTracking().FirstAsync(j => j.Id == origJournalId, Cancellation).ConfigureAwait(true);
            Assert.Equal((byte)JournalEntryState.Reversed, j1Row.State);
            Assert.Equal(revJournalId, j1Row.ReversedByEntryId);

            // Database assertions on Sequence counter
            var seq = await ctx.Database
                .SqlQuery<long>($"SELECT [LastSequence] AS [Value] FROM [ledger].[PostingSequences] WHERE [CompanyId]={companyId} AND [BranchId]={branchId} AND [FiscalYearId]={yearId}")
                .FirstAsync(Cancellation).ConfigureAwait(true);
            Assert.Equal(2L, seq);

            // Database assertions on JournalAudit
            var j2Audits = await ctx.JournalAudit.AsNoTracking().Where(a => a.JournalId == revJournalId).OrderBy(a => a.Revision).ToListAsync(Cancellation).ConfigureAwait(true);
            Assert.Equal(4, j2Audits.Count);
            Assert.Equal((byte)JournalAuditAction.Posted, j2Audits[^1].Action);
            Assert.Equal(origJournalId, j2Audits[^1].RelatedEntryId);

            var j1Audits = await ctx.JournalAudit.AsNoTracking().Where(a => a.JournalId == origJournalId).OrderBy(a => a.Revision).ToListAsync(Cancellation).ConfigureAwait(true);
            Assert.Equal(5, j1Audits.Count);
            Assert.Equal((byte)JournalAuditAction.Reversed, j1Audits[^1].Action);
            Assert.Equal(revJournalId, j1Audits[^1].RelatedEntryId);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task ConcurrentPostingsAllocateConsecutiveSequencesWithoutCollision()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            using var ctx = database.Context();
            await ctx.Database.MigrateAsync(Cancellation).ConfigureAwait(true);

            var companyId = await SeedCompanyAsync(ctx).ConfigureAwait(true);
            var branchId = await SeedBranchAsync(ctx, companyId).ConfigureAwait(true);
            var (yearId, _) = await SeedFiscalYearAndPeriodsAsync(ctx, companyId).ConfigureAwait(true);
            var (cashId, revId) = await SeedAccountsAsync(ctx, companyId).ConfigureAwait(true);

            var actorUserId = await SeedAuthorizedActorAsync(ctx, companyId, branchId, userName: "concurrent_actor").ConfigureAwait(true);
            const int concurrentCount = 10;
            var requests = new List<PostingRequest>();

            for (var i = 0; i < concurrentCount; i++)
            {
                var (jId, rVer, _, _) = await SeedApprovedJournalAsync(
                    ctx, companyId, branchId, cashId, revId,
                    amount: 50.000000m * (i + 1),
                    accountingDate: new DateOnly(2026, 1, 10 + (i % 15))).ConfigureAwait(true);

                var scope = PostingRequestScope.Create(companyId, branchId, actorUserId);
                var key = Guid.NewGuid();
                var ver = ExpectedJournalVersion.Create(rVer);
                var reason = AuditReason.Create($"ترحيل متزامن رقم {i + 1}");

                requests.Add(PostingRequest.Post(scope, key, jId, ver, reason));
            }

            // Launch concurrent postings across separate DbContext instances
            var tasks = requests.Select(req => Task.Run(async () =>
            {
                using var workerCtx = database.Context();
                var service = new SqlPostingService(workerCtx);
                return await service.PostAsync(req, Cancellation).ConfigureAwait(true);
            })).ToArray();

            var receipts = await Task.WhenAll(tasks).ConfigureAwait(true);

            // Assert all 10 completed successfully
            Assert.Equal(concurrentCount, receipts.Length);

            // Assert sequences are distinct and consecutive 1..10
            var sequences = receipts.Select(r => r.PostingNumber.Sequence).OrderBy(s => s).ToArray();
            var expectedSequences = Enumerable.Range(1, concurrentCount).Select(i => (long)i).ToArray();
            Assert.Equal(expectedSequences, sequences);

            // Assert database counter is exactly 10
            var finalSeq = await ctx.Database
                .SqlQuery<long>($"SELECT [LastSequence] AS [Value] FROM [ledger].[PostingSequences] WHERE [CompanyId]={companyId} AND [BranchId]={branchId} AND [FiscalYearId]={yearId}")
                .FirstAsync(Cancellation).ConfigureAwait(true);
            Assert.Equal(concurrentCount, finalSeq);

            // Assert all 10 journals in database are Posted with their assigned sequence
            var postedJournals = await ctx.JournalEntries.AsNoTracking()
                .Where(j => j.CompanyId == companyId && j.BranchId == branchId)
                .ToListAsync(Cancellation).ConfigureAwait(true);

            Assert.Equal(concurrentCount, postedJournals.Count);
            Assert.All(postedJournals, j => Assert.Equal((byte)JournalEntryState.Posted, j.State));

            var dbSequences = postedJournals.Select(j => j.PostingSequence!.Value).OrderBy(s => s).ToArray();
            Assert.Equal(expectedSequences, dbSequences);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task CrossScopeAndCrossOperationPostingWithSameIdempotencyKeySucceeds()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            using var ctx = database.Context();
            await ctx.Database.MigrateAsync(Cancellation).ConfigureAwait(true);

            // Company 1
            var company1 = await SeedCompanyAsync(ctx, "JOD").ConfigureAwait(true);
            var branch1 = await SeedBranchAsync(ctx, company1, "BR1").ConfigureAwait(true);
            var (year1, _) = await SeedFiscalYearAndPeriodsAsync(ctx, company1, 2026).ConfigureAwait(true);
            var (cash1, rev1) = await SeedAccountsAsync(ctx, company1).ConfigureAwait(true);
            var (journal1, v1, _, _) = await SeedApprovedJournalAsync(ctx, company1, branch1, cash1, rev1).ConfigureAwait(true);

            // Company 2
            var company2 = Guid.NewGuid();
            await ctx.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO [system].[Companies]([Id],[Code],[NameArabic],[BaseCurrencyCode],[IsActive]) VALUES({company2},N'BETA',N'شركة بيتا التجارية','JOD',1)",
                Cancellation).ConfigureAwait(true);
            var branch2 = await SeedBranchAsync(ctx, company2, "BR2").ConfigureAwait(true);
            var (year2, _) = await SeedFiscalYearAndPeriodsAsync(ctx, company2, 2026).ConfigureAwait(true);
            var (cash2, rev2) = await SeedAccountsAsync(ctx, company2).ConfigureAwait(true);
            var (journal2, v2, _, _) = await SeedApprovedJournalAsync(ctx, company2, branch2, cash2, rev2).ConfigureAwait(true);

            var sharedIdempotencyKey = Guid.NewGuid();
            var actor = await SeedAuthorizedActorAsync(ctx, company1, branch1, userName: "cross_scope_actor").ConfigureAwait(true);
            await ctx.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO [security].[UserCompanyScopes] ([UserId],[CompanyId]) VALUES ({actor},{company2})",
                Cancellation).ConfigureAwait(true);
            await ctx.Database.ExecuteSqlInterpolatedAsync(
                $@"INSERT INTO [security].[UserBranchScopes] ([UserId],[CompanyId],[BranchId]) VALUES ({actor},{company2},{branch2})",
                Cancellation).ConfigureAwait(true);

            var service = new SqlPostingService(ctx);

            // 1. Post for Company 1 with sharedIdempotencyKey
            var req1 = PostingRequest.Post(
                PostingRequestScope.Create(company1, branch1, actor),
                sharedIdempotencyKey,
                journal1,
                ExpectedJournalVersion.Create(v1),
                AuditReason.Create("ترحيل شركة 1"));
            var receipt1 = await service.PostAsync(req1, Cancellation).ConfigureAwait(true);

            // 2. Post for Company 2 with same sharedIdempotencyKey
            var req2 = PostingRequest.Post(
                PostingRequestScope.Create(company2, branch2, actor),
                sharedIdempotencyKey,
                journal2,
                ExpectedJournalVersion.Create(v2),
                AuditReason.Create("ترحيل شركة 2"));
            var receipt2 = await service.PostAsync(req2, Cancellation).ConfigureAwait(true);

            Assert.Equal(sharedIdempotencyKey, receipt1.IdempotencyKey);
            Assert.Equal(sharedIdempotencyKey, receipt2.IdempotencyKey);
            Assert.Equal(company1, receipt1.Scope.CompanyId);
            Assert.Equal(company2, receipt2.Scope.CompanyId);
            Assert.Equal(journal1, receipt1.JournalId);
            Assert.Equal(journal2, receipt2.JournalId);

            // 3. Replay Company 1 returns receipt1
            var replay1 = await service.PostAsync(req1, Cancellation).ConfigureAwait(true);
            Assert.Equal(receipt1.PostingNumber.Sequence, replay1.PostingNumber.Sequence);
            Assert.Equal(receipt1.JournalId, replay1.JournalId);

            // 4. Replay Company 2 returns receipt2
            var replay2 = await service.PostAsync(req2, Cancellation).ConfigureAwait(true);
            Assert.Equal(receipt2.PostingNumber.Sequence, replay2.PostingNumber.Sequence);
            Assert.Equal(receipt2.JournalId, replay2.JournalId);

            // 5. Database has 2 separate rows in PostingRequests
            var rows = await ctx.PostingRequests.AsNoTracking()
                .Where(r => r.IdempotencyKey == sharedIdempotencyKey)
                .ToListAsync(Cancellation).ConfigureAwait(true);
            Assert.Equal(2, rows.Count);
            Assert.Contains(rows, r => r.CompanyId == company1);
            Assert.Contains(rows, r => r.CompanyId == company2);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task PostingFailsWhenActorLacksRequiredPermission()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            using var ctx = database.Context();
            await ctx.Database.MigrateAsync(Cancellation).ConfigureAwait(true);

            var companyId = await SeedCompanyAsync(ctx).ConfigureAwait(true);
            var branchId = await SeedBranchAsync(ctx, companyId).ConfigureAwait(true);
            await SeedFiscalYearAndPeriodsAsync(ctx, companyId).ConfigureAwait(true);
            var (cashId, revId) = await SeedAccountsAsync(ctx, companyId).ConfigureAwait(true);
            var (journalId, rowVersion, _, _) = await SeedApprovedJournalAsync(ctx, companyId, branchId, cashId, revId).ConfigureAwait(true);

            // Seed actor who has role and scopes, but remove PermissionCatalog.JournalsPost permission
            var actorUserId = await SeedAuthorizedActorAsync(ctx, companyId, branchId, userName: "no_perm_actor").ConfigureAwait(true);
            await ctx.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM [security].[RolePermissions] WHERE [PermissionCode] = {PermissionCatalog.JournalsPost}",
                Cancellation).ConfigureAwait(true);

            var scope = PostingRequestScope.Create(companyId, branchId, actorUserId);
            var request = PostingRequest.Post(
                scope, Guid.NewGuid(), journalId,
                ExpectedJournalVersion.Create(rowVersion),
                AuditReason.Create("محاولة ترحيل بدون صلاحية"));

            var service = new SqlPostingService(ctx);
            var ex = await Assert.ThrowsAsync<PostingAuthorizationException>(
                () => service.PostAsync(request, Cancellation)).ConfigureAwait(true);

            Assert.Contains(PermissionCatalog.JournalsPost, ex.Message, StringComparison.OrdinalIgnoreCase);

            // Verify journal state remains Approved
            var state = await ctx.Database
                .SqlQuery<byte>($"SELECT [State] AS [Value] FROM [ledger].[JournalEntries] WHERE [Id] = {journalId}")
                .FirstAsync(Cancellation).ConfigureAwait(true);
            Assert.Equal((byte)JournalEntryState.Approved, state);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task PostingFailsWhenActorLacksBranchScope()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            using var ctx = database.Context();
            await ctx.Database.MigrateAsync(Cancellation).ConfigureAwait(true);

            var companyId = await SeedCompanyAsync(ctx).ConfigureAwait(true);
            var branchId = await SeedBranchAsync(ctx, companyId).ConfigureAwait(true);
            await SeedFiscalYearAndPeriodsAsync(ctx, companyId).ConfigureAwait(true);
            var (cashId, revId) = await SeedAccountsAsync(ctx, companyId).ConfigureAwait(true);
            var (journalId, rowVersion, _, _) = await SeedApprovedJournalAsync(ctx, companyId, branchId, cashId, revId).ConfigureAwait(true);

            // Seed actor, then remove branch scope
            var actorUserId = await SeedAuthorizedActorAsync(ctx, companyId, branchId, userName: "no_branch_scope_actor").ConfigureAwait(true);
            await ctx.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM [security].[UserBranchScopes] WHERE [UserId] = {actorUserId}",
                Cancellation).ConfigureAwait(true);

            var scope = PostingRequestScope.Create(companyId, branchId, actorUserId);
            var request = PostingRequest.Post(
                scope, Guid.NewGuid(), journalId,
                ExpectedJournalVersion.Create(rowVersion),
                AuditReason.Create("محاولة ترحيل بدون نطاق الفرع"));

            var service = new SqlPostingService(ctx);
            var ex = await Assert.ThrowsAsync<PostingAuthorizationException>(
                () => service.PostAsync(request, Cancellation)).ConfigureAwait(true);

            Assert.Contains("branch", ex.Message, StringComparison.OrdinalIgnoreCase);

            var state = await ctx.Database
                .SqlQuery<byte>($"SELECT [State] AS [Value] FROM [ledger].[JournalEntries] WHERE [Id] = {journalId}")
                .FirstAsync(Cancellation).ConfigureAwait(true);
            Assert.Equal((byte)JournalEntryState.Approved, state);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task PostingFailsWhenActorIsDeactivated()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            using var ctx = database.Context();
            await ctx.Database.MigrateAsync(Cancellation).ConfigureAwait(true);

            var companyId = await SeedCompanyAsync(ctx).ConfigureAwait(true);
            var branchId = await SeedBranchAsync(ctx, companyId).ConfigureAwait(true);
            await SeedFiscalYearAndPeriodsAsync(ctx, companyId).ConfigureAwait(true);
            var (cashId, revId) = await SeedAccountsAsync(ctx, companyId).ConfigureAwait(true);
            var (journalId, rowVersion, _, _) = await SeedApprovedJournalAsync(ctx, companyId, branchId, cashId, revId).ConfigureAwait(true);

            var actorUserId = await SeedAuthorizedActorAsync(ctx, companyId, branchId, userName: "inactive_actor").ConfigureAwait(true);
            // Deactivate user
            await ctx.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [security].[Users] SET [IsActive] = 0 WHERE [Id] = {actorUserId}",
                Cancellation).ConfigureAwait(true);

            var scope = PostingRequestScope.Create(companyId, branchId, actorUserId);
            var request = PostingRequest.Post(
                scope, Guid.NewGuid(), journalId,
                ExpectedJournalVersion.Create(rowVersion),
                AuditReason.Create("محاولة ترحيل بمستخدم معطل"));

            var service = new SqlPostingService(ctx);
            var ex = await Assert.ThrowsAsync<PostingAuthorizationException>(
                () => service.PostAsync(request, Cancellation)).ConfigureAwait(true);

            Assert.Contains("inactive", ex.Message, StringComparison.OrdinalIgnoreCase);

            var state = await ctx.Database
                .SqlQuery<byte>($"SELECT [State] AS [Value] FROM [ledger].[JournalEntries] WHERE [Id] = {journalId}")
                .FirstAsync(Cancellation).ConfigureAwait(true);
            Assert.Equal((byte)JournalEntryState.Approved, state);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task ReplayFailsWhenActorRoleRevokedAfterInitialPosting()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            using var ctx = database.Context();
            await ctx.Database.MigrateAsync(Cancellation).ConfigureAwait(true);

            var companyId = await SeedCompanyAsync(ctx).ConfigureAwait(true);
            var branchId = await SeedBranchAsync(ctx, companyId).ConfigureAwait(true);
            await SeedFiscalYearAndPeriodsAsync(ctx, companyId).ConfigureAwait(true);
            var (cashId, revId) = await SeedAccountsAsync(ctx, companyId).ConfigureAwait(true);
            var (journalId, rowVersion, _, _) = await SeedApprovedJournalAsync(ctx, companyId, branchId, cashId, revId).ConfigureAwait(true);

            var actorUserId = await SeedAuthorizedActorAsync(ctx, companyId, branchId, userName: "revoked_replay_actor").ConfigureAwait(true);
            var scope = PostingRequestScope.Create(companyId, branchId, actorUserId);
            var idempotencyKey = Guid.NewGuid();
            var request = PostingRequest.Post(
                scope, idempotencyKey, journalId,
                ExpectedJournalVersion.Create(rowVersion),
                AuditReason.Create("ترحيل أصلي ناجح"));

            var service = new SqlPostingService(ctx);
            var receipt = await service.PostAsync(request, Cancellation).ConfigureAwait(true);
            Assert.NotNull(receipt);

            // Now revoke the actor's role
            await ctx.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM [security].[UserRoles] WHERE [UserId] = {actorUserId}",
                Cancellation).ConfigureAwait(true);

            // Replay attempt must be rejected because actor no longer has permission
            var ex = await Assert.ThrowsAsync<PostingAuthorizationException>(
                () => service.PostAsync(request, Cancellation)).ConfigureAwait(true);

            Assert.Contains(PermissionCatalog.JournalsPost, ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }
}
