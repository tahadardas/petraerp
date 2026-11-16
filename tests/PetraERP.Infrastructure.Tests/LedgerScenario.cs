using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger;
using PetraERP.Application.GeneralLedger.Calendars;
using PetraERP.Application.GeneralLedger.ChartOfAccounts;
using PetraERP.Application.GeneralLedger.Posting;
using PetraERP.Application.Security;
using PetraERP.Domain.Primitives;
using PetraERP.Infrastructure.AccountingCalendars;
using PetraERP.Infrastructure.GeneralLedger;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Infrastructure.Tests;

/// <summary>
/// Isolated SQL scenario: company A (USD base, SYP additional) with branches A1/A2 and company B (SYP base) with B1,
/// an actor granted every ledger permission on A1, A2 and B1, and a 2026 monthly calendar for both companies. All
/// journals are created, validated, approved and posted through the real services.
/// </summary>
internal sealed class LedgerScenario
{
    private LedgerScenario(SqlTestDatabase database) => Database = database;

    internal SqlTestDatabase Database { get; }
    internal Guid CompanyA { get; private set; }
    internal Guid CompanyB { get; private set; }
    internal Guid BranchA1 { get; private set; }
    internal Guid BranchA2 { get; private set; }
    internal Guid BranchB1 { get; private set; }
    internal Guid Actor { get; private set; }
    internal static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    internal static async Task<LedgerScenario> CreateAsync(SqlTestDatabase database)
    {
        var scenario = new LedgerScenario(database);
        using var context = database.Context();
        await context.Database.MigrateAsync(Cancellation).ConfigureAwait(false);
        foreach (var (code, name) in new[] { ("USD", "دولار أمريكي"), ("SYP", "ليرة سورية"), ("EUR", "يورو") })
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO [system].[Currencies]([Code],[NameArabic],[IsActive]) VALUES({code},{name},1)", Cancellation).ConfigureAwait(false);
        }

        scenario.CompanyA = await CompanyAsync(context, "PETRA", "شركة البتراء", "USD").ConfigureAwait(false);
        scenario.CompanyB = await CompanyAsync(context, "OTHER", "شركة أخرى", "SYP").ConfigureAwait(false);
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO [system].[CompanyAdditionalCurrencies]([CompanyId],[CurrencyCode]) VALUES({scenario.CompanyA},N'SYP')", Cancellation).ConfigureAwait(false);
        scenario.BranchA1 = await BranchAsync(context, scenario.CompanyA, "A1", "فرع دمشق").ConfigureAwait(false);
        scenario.BranchA2 = await BranchAsync(context, scenario.CompanyA, "A2", "فرع حلب").ConfigureAwait(false);
        scenario.BranchB1 = await BranchAsync(context, scenario.CompanyB, "B1", "فرع حمص").ConfigureAwait(false);
        scenario.Actor = await ActorAsync(context).ConfigureAwait(false);
        await scenario.GrantAsync(scenario.CompanyA, scenario.BranchA1).ConfigureAwait(false);
        await scenario.GrantAsync(scenario.CompanyA, scenario.BranchA2).ConfigureAwait(false);
        await scenario.GrantAsync(scenario.CompanyB, scenario.BranchB1).ConfigureAwait(false);
        foreach (var company in new[] { scenario.CompanyA, scenario.CompanyB })
        {
            await Calendar(context).CreateFiscalYearAsync(scenario.Master(company),
                new CreateFiscalYearInput("FY2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), null), Cancellation).ConfigureAwait(false);
        }

        return scenario;
    }

    internal LedgerMasterScope Master(Guid company) => LedgerMasterScope.Create(company, Actor);
    internal static SqlAccountingCalendarService Calendar(PetraDbContext context) => new(context, TimeProvider.System);
    internal static SqlChartOfAccountsService Chart(PetraDbContext context) => new(context, TimeProvider.System);

    internal async Task GrantAsync(Guid company, Guid branch)
    {
        using var context = Database.Context();
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"IF NOT EXISTS (SELECT 1 FROM [security].[UserCompanyScopes] WHERE [UserId]={Actor} AND [CompanyId]={company}) INSERT INTO [security].[UserCompanyScopes]([UserId],[CompanyId]) VALUES({Actor},{company})",
            Cancellation).ConfigureAwait(false);
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO [security].[UserBranchScopes]([UserId],[CompanyId],[BranchId]) VALUES({Actor},{company},{branch})", Cancellation).ConfigureAwait(false);
    }

    internal async Task RevokeBranchAsync(Guid branch)
    {
        using var context = Database.Context();
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM [security].[UserBranchScopes] WHERE [UserId]={Actor} AND [BranchId]={branch}", Cancellation).ConfigureAwait(false);
    }

    internal async Task<AccountDto> AccountAsync(Guid company, string code, AccountTypeCode type, bool posting, Guid? parent = null,
        CostCenterPolicyCode cost = CostCenterPolicyCode.Optional)
    {
        using var context = Database.Context();
        return await Chart(context).CreateAccountAsync(Master(company), new AccountInput
        {
            Code = code,
            NameArabic = "حساب " + code,
            NameEnglish = "Account " + code,
            AccountType = type,
            PostingAllowed = posting,
            CostCenterPolicy = cost,
            CurrencyMode = CurrencyModeCode.AnyEnabledCurrency,
            ParentId = parent,
        }, Cancellation).ConfigureAwait(false);
    }

    internal static JournalDraftLineInput Line(Guid account, decimal debit, decimal credit, string currency) => new()
    {
        AccountId = account,
        TransactionCurrencyCode = currency,
        TransactionDebit = debit,
        TransactionCredit = credit,
        Description = "سطر قيد",
    };

    /// <summary>Creates a draft; with <paramref name="post"/> it is validated, approved and posted through SqlPostingService.</summary>
    internal async Task<JournalDetailsDto> JournalAsync(Guid company, Guid branch, DateOnly date, string currency, bool post,
        params (Guid Account, decimal Debit, decimal Credit)[] lines)
    {
        using var context = Database.Context();
        var service = new SqlJournalEntryService(context, TimeProvider.System);
        var scope = JournalEntryScope.Create(company, branch, Actor);
        var draft = await service.CreateDraftAsync(scope, new CreateJournalDraftCommand
        {
            AccountingDate = date,
            Description = "قيد " + date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            Lines = lines.Select(l => Line(l.Account, l.Debit, l.Credit, currency)).ToArray(),
        }, Cancellation).ConfigureAwait(false);
        return post ? await PostAsync(company, branch, draft.Id).ConfigureAwait(false) : draft;
    }

    internal async Task<JournalDetailsDto> ApproveAsync(Guid company, Guid branch, Guid journalId)
    {
        using var context = Database.Context();
        var service = new SqlJournalEntryService(context, TimeProvider.System);
        var scope = JournalEntryScope.Create(company, branch, Actor);
        var current = await service.GetJournalAsync(scope, journalId, Cancellation).ConfigureAwait(false);
        var validated = await service.ValidateDraftAsync(scope, journalId, Action(current), Cancellation).ConfigureAwait(false);
        return await service.ApproveDraftAsync(scope, journalId, Action(validated), Cancellation).ConfigureAwait(false);
    }

    internal async Task<JournalDetailsDto> PostAsync(Guid company, Guid branch, Guid journalId)
    {
        var approved = await ApproveAsync(company, branch, journalId).ConfigureAwait(false);
        using var context = Database.Context();
        var request = PostingRequest.Post(PostingRequestScope.Create(company, branch, Actor), Guid.NewGuid(), journalId,
            ExpectedJournalVersion.FromBase64(approved.VersionBase64), AuditReason.Create("ترحيل اختبار"));
        await new SqlPostingService(context).PostAsync(request, Cancellation).ConfigureAwait(false);
        return await new SqlJournalEntryService(context, TimeProvider.System)
            .GetJournalAsync(JournalEntryScope.Create(company, branch, Actor), journalId, Cancellation).ConfigureAwait(false);
    }

    internal async Task<JournalDetailsDto> ReverseAsync(Guid company, Guid branch, JournalDetailsDto original, DateOnly date)
    {
        Guid reversalId;
        using (var context = Database.Context())
        {
            var service = new SqlJournalEntryService(context, TimeProvider.System);
            var draft = await service.CreateReversalDraftAsync(JournalEntryScope.Create(company, branch, Actor), new CreateReversalDraftCommand
            {
                OriginalJournalId = original.Id,
                OriginalExpectedVersion = Token(original.VersionBase64),
                AccountingDate = date,
                Reason = "عكس قيد خاطئ",
            }, Cancellation).ConfigureAwait(false);
            reversalId = draft.Id;
        }

        var approved = await ApproveAsync(company, branch, reversalId).ConfigureAwait(false);
        using var postContext = Database.Context();
        var current = await new SqlJournalEntryService(postContext, TimeProvider.System)
            .GetJournalAsync(JournalEntryScope.Create(company, branch, Actor), original.Id, Cancellation).ConfigureAwait(false);
        var request = PostingRequest.Reverse(PostingRequestScope.Create(company, branch, Actor), Guid.NewGuid(), reversalId,
            ExpectedJournalVersion.FromBase64(approved.VersionBase64), original.Id, ExpectedJournalVersion.FromBase64(current.VersionBase64),
            AuditReason.Create("ترحيل العكس"));
        await new SqlPostingService(postContext).PostAsync(request, Cancellation).ConfigureAwait(false);
        return await new SqlJournalEntryService(postContext, TimeProvider.System)
            .GetJournalAsync(JournalEntryScope.Create(company, branch, Actor), reversalId, Cancellation).ConfigureAwait(false);
    }

    internal static ConcurrencyToken Token(string version) =>
        ConcurrencyToken.TryParse(version, out var token) ? token : throw new InvalidOperationException("Invalid version.");

    private static JournalDraftActionCommand Action(JournalDetailsDto journal) =>
        new() { ExpectedVersion = Token(journal.VersionBase64), Reason = "إجراء اختبار" };

    private static async Task<Guid> CompanyAsync(PetraDbContext context, string code, string name, string currency)
    {
        var id = Guid.CreateVersion7();
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO [system].[Companies]([Id],[Code],[NameArabic],[BaseCurrencyCode],[IsActive]) VALUES({id},{code},{name},{currency},1)",
            Cancellation).ConfigureAwait(false);
        return id;
    }

    private static async Task<Guid> BranchAsync(PetraDbContext context, Guid company, string code, string name)
    {
        var id = Guid.CreateVersion7();
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO [system].[Branches]([Id],[CompanyId],[Code],[NameArabic],[IsActive]) VALUES({id},{company},{code},{name},1)", Cancellation).ConfigureAwait(false);
        return id;
    }

    private static async Task<Guid> ActorAsync(PetraDbContext context)
    {
        var userId = Guid.CreateVersion7();
        var roleId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;
        var salt = new byte[32]; salt[0] = 1;
        var hash = new byte[64]; hash[0] = 1;
        await context.Database.ExecuteSqlInterpolatedAsync(
            $@"INSERT INTO [security].[Users]([Id],[UserName],[NormalizedUserName],[DisplayNameArabic],[DisplayNameEnglish],[PasswordHash],[PasswordSalt],
                [PasswordIterations],[PasswordAlgorithm],[PasswordChangedAtUtc],[MustChangePassword],[IsActive],[AccessFailedCount],[SecurityStamp],[CreatedAtUtc],[UpdatedAtUtc])
               VALUES({userId},N'ledger_user',N'LEDGER_USER',N'محاسب',N'Accountant',{hash},{salt},100000,1,{now},0,1,0,{Guid.NewGuid()},{now},{now})",
            Cancellation).ConfigureAwait(false);
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO [security].[Roles]([Id],[Code],[NameArabic],[NameEnglish],[CreatedAtUtc],[UpdatedAtUtc]) VALUES({roleId},N'LEDGER',N'محاسبة',N'Ledger',{now},{now})",
            Cancellation).ConfigureAwait(false);
        foreach (var permission in PermissionCatalog.All.Where(p => p.Code.StartsWith("Ledger.", StringComparison.Ordinal) || p.Code.StartsWith("Inventory.", StringComparison.Ordinal) || p.Code.StartsWith("Parties.", StringComparison.Ordinal)))
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO [security].[RolePermissions]([RoleId],[PermissionCode]) VALUES({roleId},{permission.Code})", Cancellation).ConfigureAwait(false);
        }

        await context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO [security].[UserRoles]([UserId],[RoleId]) VALUES({userId},{roleId})", Cancellation).ConfigureAwait(false);
        return userId;
    }

    internal static async Task AddStockMovementAsync(PetraDbContext context, PetraERP.Infrastructure.Inventory.StockMovementRow movement, CancellationToken cancellationToken)
    {
        var tx = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        await using (tx.ConfigureAwait(false))
        {
            context.StockMovements.Add(movement);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
