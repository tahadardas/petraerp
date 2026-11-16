using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger;
using PetraERP.Application.GeneralLedger.Posting;
using PetraERP.Application.Security;
using PetraERP.Domain.GeneralLedger;
using PetraERP.Domain.Primitives;
using PetraERP.Infrastructure.GeneralLedger;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Tests;

public sealed class JournalDraftLifecycleSqlTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static async Task<Guid> SeedCompanyAsync(PetraDbContext ctx, string currency = "JOD")
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

    private static async Task<Guid> SeedBranchAsync(PetraDbContext ctx, Guid companyId, string code = "HQ")
    {
        var branchId = Guid.NewGuid();
        await ctx.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO [system].[Branches]([Id],[CompanyId],[Code],[NameArabic],[IsActive]) VALUES({branchId},{companyId},{code},N'المقر الرئيسي',1)",
            Cancellation).ConfigureAwait(true);

        return branchId;
    }

    private static async Task<(Guid YearId, Guid PeriodId)> SeedFiscalYearAndPeriodsAsync(
        PetraDbContext ctx, Guid companyId, int year = 2026)
    {
        var yearId = Guid.NewGuid();
        var yearCode = $"FY{year}";
        var rangeStart = new DateOnly(year, 1, 1);
        var rangeEnd = new DateOnly(year, 12, 31);

        await ctx.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO [ledger].[FiscalYears]([Id],[CompanyId],[Code],[RangeStart],[RangeEnd]) VALUES({yearId},{companyId},{yearCode},{rangeStart},{rangeEnd})",
            Cancellation).ConfigureAwait(true);

        var periodId = Guid.NewGuid();
        await ctx.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO [ledger].[AccountingPeriods]([Id],[CompanyId],[FiscalYearId],[Code],[RangeStart],[RangeEnd],[State]) VALUES({periodId},{companyId},{yearId},N'P01','2026-01-01','2026-01-31',0)",
            Cancellation).ConfigureAwait(true);

        return (yearId, periodId);
    }

    private static async Task<(Guid CashAccountId, Guid RevenueAccountId)> SeedAccountsAsync(
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

    private static async Task<Guid> SeedAuthorizedActorAsync(
        PetraDbContext ctx, Guid companyId, Guid branchId)
    {
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var salt = new byte[32]; salt[0] = 1;
        var hash = new byte[64]; hash[0] = 1;

        await ctx.Database.ExecuteSqlInterpolatedAsync(
            $@"INSERT INTO [security].[Users]
                ([Id],[UserName],[NormalizedUserName],[DisplayNameArabic],[DisplayNameEnglish],
                 [PasswordHash],[PasswordSalt],[PasswordIterations],[PasswordAlgorithm],[PasswordChangedAtUtc],
                 [MustChangePassword],[IsActive],[AccessFailedCount],[SecurityStamp],
                 [CreatedAtUtc],[UpdatedAtUtc])
                VALUES
                ({userId},N'gl_user',N'GL_USER',N'مستخدم محاسبة',N'GL User',
                 {hash},{salt},100000,1,{now},
                 0,1,0,{Guid.NewGuid()},{now},{now})",
            Cancellation).ConfigureAwait(true);

        await ctx.Database.ExecuteSqlInterpolatedAsync(
            $@"INSERT INTO [security].[Roles]
                ([Id],[Code],[NameArabic],[NameEnglish],[CreatedAtUtc],[UpdatedAtUtc])
                VALUES
                ({roleId},N'GL_ADMIN',N'مدير محاسبة',N'GL Admin',{now},{now})",
            Cancellation).ConfigureAwait(true);

        await ctx.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO [security].[RolePermissions] ([RoleId],[PermissionCode]) VALUES ({roleId},{PermissionCatalog.JournalsManage})",
            Cancellation).ConfigureAwait(true);
        await ctx.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO [security].[RolePermissions] ([RoleId],[PermissionCode]) VALUES ({roleId},{PermissionCatalog.JournalsApprove})",
            Cancellation).ConfigureAwait(true);
        await ctx.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO [security].[RolePermissions] ([RoleId],[PermissionCode]) VALUES ({roleId},{PermissionCatalog.JournalsPost})",
            Cancellation).ConfigureAwait(true);
        await ctx.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO [security].[RolePermissions] ([RoleId],[PermissionCode]) VALUES ({roleId},{PermissionCatalog.JournalsView})",
            Cancellation).ConfigureAwait(true);

        await ctx.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO [security].[UserRoles] ([UserId],[RoleId]) VALUES ({userId},{roleId})",
            Cancellation).ConfigureAwait(true);
        await ctx.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO [security].[UserCompanyScopes] ([UserId],[CompanyId]) VALUES ({userId},{companyId})",
            Cancellation).ConfigureAwait(true);
        await ctx.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO [security].[UserBranchScopes] ([UserId],[CompanyId],[BranchId]) VALUES ({userId},{companyId},{branchId})",
            Cancellation).ConfigureAwait(true);

        return userId;
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task DraftCreationAndRevisionWithLinesAndAuditTrail()
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
            var actorId = await SeedAuthorizedActorAsync(ctx, companyId, branchId).ConfigureAwait(true);

            var service = new SqlJournalEntryService(ctx, TimeProvider.System);
            var scope = JournalEntryScope.Create(companyId, branchId, actorId);

            // 1. Create Draft
            var createCommand = new CreateJournalDraftCommand
            {
                AccountingDate = new DateOnly(2026, 1, 15),
                Description = "قيد رواتب تجريبي",
                Lines =
                [
                    new JournalDraftLineInput
                    {
                        AccountId = cashId,
                        TransactionCurrencyCode = "JOD",
                        TransactionDebit = 500m,
                        TransactionCredit = 0m,
                        RateValue = 1.0m,
                        Description = "مدين نقدية",
                    },
                    new JournalDraftLineInput
                    {
                        AccountId = revId,
                        TransactionCurrencyCode = "JOD",
                        TransactionDebit = 0m,
                        TransactionCredit = 500m,
                        RateValue = 1.0m,
                        Description = "دائن إيرادات",
                    }
                ]
            };

            var created = await service.CreateDraftAsync(scope, createCommand, Cancellation).ConfigureAwait(true);

            Assert.NotEqual(Guid.Empty, created.Id);
            Assert.Equal((byte)JournalEntryState.Draft, created.State);
            Assert.Equal(1, created.Revision);
            Assert.Equal(500m, created.TotalDebit);
            Assert.Equal(500m, created.TotalCredit);
            Assert.Equal(2, created.Lines.Count);
            Assert.Single(created.AuditTrail);
            Assert.Equal((byte)JournalAuditAction.Created, created.AuditTrail[0].Action);

            // 2. Revise Draft
            var reviseCommand = new ReviseJournalDraftCommand
            {
                ExpectedVersion = ConcurrencyToken.TryParse(created.VersionBase64, out var tok) ? tok : throw new InvalidOperationException(),
                AccountingDate = new DateOnly(2026, 1, 16),
                Description = "قيد رواتب معدل",
                Lines =
                [
                    new JournalDraftLineInput
                    {
                        AccountId = cashId,
                        TransactionCurrencyCode = "JOD",
                        TransactionDebit = 750m,
                        TransactionCredit = 0m,
                        RateValue = 1.0m,
                        Description = "مدين نقدية معدل",
                    },
                    new JournalDraftLineInput
                    {
                        AccountId = revId,
                        TransactionCurrencyCode = "JOD",
                        TransactionDebit = 0m,
                        TransactionCredit = 750m,
                        RateValue = 1.0m,
                        Description = "دائن إيرادات معدل",
                    }
                ],
                Reason = "تعديل مبالغ الرواتب وفق الكشف النهائي",
            };

            var revised = await service.ReviseDraftAsync(scope, created.Id, reviseCommand, Cancellation).ConfigureAwait(true);

            Assert.Equal(2, revised.Revision);
            Assert.Equal("قيد رواتب معدل", revised.Description);
            Assert.Equal(750m, revised.TotalDebit);
            Assert.Equal(750m, revised.TotalCredit);
            Assert.Equal(2, revised.AuditTrail.Count);
            Assert.Equal((byte)JournalAuditAction.Revised, revised.AuditTrail[1].Action);
            Assert.Equal("تعديل مبالغ الرواتب وفق الكشف النهائي", revised.AuditTrail[1].Reason);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task DraftValidationApprovalAndReturnWorkflow()
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
            var actorId = await SeedAuthorizedActorAsync(ctx, companyId, branchId).ConfigureAwait(true);

            var service = new SqlJournalEntryService(ctx, TimeProvider.System);
            var scope = JournalEntryScope.Create(companyId, branchId, actorId);

            var created = await service.CreateDraftAsync(scope, new CreateJournalDraftCommand
            {
                AccountingDate = new DateOnly(2026, 1, 20),
                Description = "قيد تدقيق دورة الاعتماد",
                Lines =
                [
                    new JournalDraftLineInput { AccountId = cashId, TransactionCurrencyCode = "JOD", TransactionDebit = 100m, TransactionCredit = 0m, Description = "مدين" },
                    new JournalDraftLineInput { AccountId = revId, TransactionCurrencyCode = "JOD", TransactionDebit = 0m, TransactionCredit = 100m, Description = "دائن" },
                ]
            }, Cancellation).ConfigureAwait(true);

            var token1 = ConcurrencyToken.TryParse(created.VersionBase64, out var t1) ? t1 : throw new InvalidOperationException();

            // Validate
            var validated = await service.ValidateDraftAsync(scope, created.Id, new JournalDraftActionCommand
            {
                ExpectedVersion = token1,
                Reason = "تدقيق مطابق للسياسات المحاسبية",
            }, Cancellation).ConfigureAwait(true);

            Assert.Equal((byte)JournalEntryState.Validated, validated.State);
            Assert.Equal(2, validated.Revision);

            // Approve
            var token2 = ConcurrencyToken.TryParse(validated.VersionBase64, out var t2) ? t2 : throw new InvalidOperationException();
            var approved = await service.ApproveDraftAsync(scope, created.Id, new JournalDraftActionCommand
            {
                ExpectedVersion = token2,
                Reason = "اعتماد القيد للترحيل النهائي",
            }, Cancellation).ConfigureAwait(true);

            Assert.Equal((byte)JournalEntryState.Approved, approved.State);
            Assert.Equal(3, approved.Revision);

            // Return to draft
            var token3 = ConcurrencyToken.TryParse(approved.VersionBase64, out var t3) ? t3 : throw new InvalidOperationException();
            var returned = await service.ReturnToDraftAsync(scope, created.Id, new JournalDraftActionCommand
            {
                ExpectedVersion = token3,
                Reason = "إعادة للمسودة لتصحيح التوجيه",
            }, Cancellation).ConfigureAwait(true);

            Assert.Equal((byte)JournalEntryState.Draft, returned.State);
            Assert.Equal(4, returned.Revision);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task ConcurrencyConflictPreventsStaleRevision()
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
            var actorId = await SeedAuthorizedActorAsync(ctx, companyId, branchId).ConfigureAwait(true);

            var service = new SqlJournalEntryService(ctx, TimeProvider.System);
            var scope = JournalEntryScope.Create(companyId, branchId, actorId);

            var created = await service.CreateDraftAsync(scope, new CreateJournalDraftCommand
            {
                AccountingDate = new DateOnly(2026, 1, 20),
                Description = "قيد تنازع التعديل",
                Lines =
                [
                    new JournalDraftLineInput { AccountId = cashId, TransactionCurrencyCode = "JOD", TransactionDebit = 100m, TransactionCredit = 0m, Description = "مدين" },
                    new JournalDraftLineInput { AccountId = revId, TransactionCurrencyCode = "JOD", TransactionDebit = 0m, TransactionCredit = 100m, Description = "دائن" },
                ]
            }, Cancellation).ConfigureAwait(true);

            var staleToken = ConcurrencyToken.TryParse(created.VersionBase64, out var st) ? st : throw new InvalidOperationException();

            // Valid revision updates version
            var revised = await service.ReviseDraftAsync(scope, created.Id, new ReviseJournalDraftCommand
            {
                ExpectedVersion = staleToken,
                AccountingDate = new DateOnly(2026, 1, 20),
                Description = "تعديل أول ناجح",
                Lines =
                [
                    new JournalDraftLineInput { AccountId = cashId, TransactionCurrencyCode = "JOD", TransactionDebit = 150m, TransactionCredit = 0m, Description = "مدين" },
                    new JournalDraftLineInput { AccountId = revId, TransactionCurrencyCode = "JOD", TransactionDebit = 0m, TransactionCredit = 150m, Description = "دائن" },
                ],
                Reason = "تعديل أولي",
            }, Cancellation).ConfigureAwait(true);

            Assert.NotEqual(created.VersionBase64, revised.VersionBase64);

            // Stale revision fails with ConcurrencyConflictException
            await Assert.ThrowsAsync<ConcurrencyConflictException>(() => service.ReviseDraftAsync(scope, created.Id, new ReviseJournalDraftCommand
            {
                ExpectedVersion = staleToken,
                AccountingDate = new DateOnly(2026, 1, 20),
                Description = "تعديل متأخر بنفس النسخة القديمة",
                Lines =
                [
                    new JournalDraftLineInput { AccountId = cashId, TransactionCurrencyCode = "JOD", TransactionDebit = 200m, TransactionCredit = 0m, Description = "مدين" },
                    new JournalDraftLineInput { AccountId = revId, TransactionCurrencyCode = "JOD", TransactionDebit = 0m, TransactionCredit = 200m, Description = "دائن" },
                ],
                Reason = "تعديل متضارب",
            }, Cancellation)).ConfigureAwait(true);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task DraftDeletionRemovesUnvalidatedDraft()
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
            var actorId = await SeedAuthorizedActorAsync(ctx, companyId, branchId).ConfigureAwait(true);

            var service = new SqlJournalEntryService(ctx, TimeProvider.System);
            var scope = JournalEntryScope.Create(companyId, branchId, actorId);

            var created = await service.CreateDraftAsync(scope, new CreateJournalDraftCommand
            {
                AccountingDate = new DateOnly(2026, 1, 20),
                Description = "قيد للحذف",
                Lines =
                [
                    new JournalDraftLineInput { AccountId = cashId, TransactionCurrencyCode = "JOD", TransactionDebit = 100m, TransactionCredit = 0m, Description = "مدين" },
                    new JournalDraftLineInput { AccountId = revId, TransactionCurrencyCode = "JOD", TransactionDebit = 0m, TransactionCredit = 100m, Description = "دائن" },
                ]
            }, Cancellation).ConfigureAwait(true);

            var token = ConcurrencyToken.TryParse(created.VersionBase64, out var t) ? t : throw new InvalidOperationException();

            await service.DeleteDraftAsync(scope, created.Id, token, Cancellation).ConfigureAwait(true);

            await Assert.ThrowsAsync<ResourceNotFoundException>(() => service.GetJournalAsync(scope, created.Id, Cancellation)).ConfigureAwait(true);
        }
    }
}
