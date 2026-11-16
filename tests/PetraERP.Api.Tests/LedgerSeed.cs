using Microsoft.EntityFrameworkCore;
using PetraERP.Infrastructure.Persistence;

namespace PetraERP.Api.Tests;

/// <summary>Seeds a fiscal year, two accounts and an approved balanced journal. The legacy overloads seed the central
/// database and are used by the (deferred) workspace-search tests. The workspace overloads seed the independent annual
/// accounting database via raw SQL (A02: no domain service builds an already-approved historical journal in one step);
/// application authorization is still validated by executing the real HTTP endpoints.</summary>
internal static class LedgerSeed
{
    // ---- Legacy (central database) overloads — used by the workspace search/preferences tests (Task B, deferred) ----

    internal static async Task<(Guid CompanyId, Guid BranchId, Guid PeriodId)> CalendarAsync(PetraDbContext context)
    {
        var cancellation = TestContext.Current.CancellationToken;
        var companyId = (await context.Companies.AsNoTracking().SingleAsync(cancellation).ConfigureAwait(false)).Id;
        var branchId = (await context.Branches.AsNoTracking().SingleAsync(cancellation).ConfigureAwait(false)).Id;
        var yearId = Guid.CreateVersion7();
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO [ledger].[FiscalYears]([Id],[CompanyId],[Code],[RangeStart],[RangeEnd]) VALUES({yearId},{companyId},N'FY2026',{new DateOnly(2026, 1, 1)},{new DateOnly(2026, 12, 31)})",
            cancellation).ConfigureAwait(false);
        var periodId = Guid.Empty;
        var start = new DateOnly(2026, 1, 1);
        for (var month = 1; month <= 12; month++)
        {
            var id = Guid.CreateVersion7();
            periodId = month == 1 ? id : periodId;
            var end = start.AddMonths(1).AddDays(-1);
            var code = "P" + month.ToString("D2", System.Globalization.CultureInfo.InvariantCulture);
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO [ledger].[AccountingPeriods]([Id],[CompanyId],[FiscalYearId],[Code],[RangeStart],[RangeEnd],[State]) VALUES({id},{companyId},{yearId},{code},{start},{end},0)",
                cancellation).ConfigureAwait(false);
            start = end.AddDays(1);
        }

        return (companyId, branchId, periodId);
    }

    internal static async Task<(Guid JournalId, string Version)> ApprovedJournalAsync(PetraDbContext context, Guid companyId,
        Guid branchId, string currency)
    {
        var cancellation = TestContext.Current.CancellationToken;
        var cash = Guid.CreateVersion7();
        var revenue = Guid.CreateVersion7();
        var cashCode = "1001-" + cash.ToString("N")[^8..];
        var revenueCode = "4001-" + revenue.ToString("N")[^8..];
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO [ledger].[Accounts]([Id],[CompanyId],[Code],[NameArabic],[AccountType],[PostingAllowed],[CostCenterPolicy],[CurrencyMode],[IsActive]) VALUES({cash},{companyId},{cashCode},N'الصندوق',1,1,1,1,1)",
            cancellation).ConfigureAwait(false);
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO [ledger].[Accounts]([Id],[CompanyId],[Code],[NameArabic],[AccountType],[PostingAllowed],[CostCenterPolicy],[CurrencyMode],[IsActive]) VALUES({revenue},{companyId},{revenueCode},N'المبيعات',4,1,1,1,1)",
            cancellation).ConfigureAwait(false);

        var journalId = Guid.CreateVersion7();
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO [ledger].[JournalEntries]([Id],[CompanyId],[BranchId],[AccountingDate],[Description],[State]) VALUES({journalId},{companyId},{branchId},{new DateOnly(2026, 1, 15)},N'قيد مبيعات نقدية',3)",
            cancellation).ConfigureAwait(false);
        await InsertLegacyLineAsync(context, companyId, journalId, cash, currency, 250.5m, 0m, "قبض نقدي").ConfigureAwait(false);
        await InsertLegacyLineAsync(context, companyId, journalId, revenue, currency, 0m, 250.5m, "إيراد مبيعات").ConfigureAwait(false);

        var actor = Guid.CreateVersion7();
        var correlation = Guid.CreateVersion7();
        var states = new (int Revision, int Action, int? Previous, int New, string Reason)[]
        {
            (1, 1, null, 1, "إنشاء مسودة"),
            (2, 3, 1, 2, "تدقيق"),
            (3, 4, 2, 3, "اعتماد"),
        };
        var when = DateTimeOffset.UtcNow.AddMinutes(-30);
        foreach (var state in states)
        {
            when = when.AddMinutes(5);
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO [ledger].[JournalAudit]([EventId],[CompanyId],[BranchId],[JournalId],[Revision],[Action],[PreviousState],[NewState],[ActorUserId],[CorrelationId],[Reason],[OccurredAtUtc]) VALUES({Guid.CreateVersion7()},{companyId},{branchId},{journalId},{state.Revision},{state.Action},{state.Previous},{state.New},{actor},{correlation},{state.Reason},{when})",
                cancellation).ConfigureAwait(false);
        }

        var version = await context.Database
            .SqlQuery<byte[]>($"SELECT [RowVersion] AS [Value] FROM [ledger].[JournalEntries] WHERE [Id] = {journalId}")
            .FirstAsync(cancellation).ConfigureAwait(false);
        return (journalId, Convert.ToBase64String(version));
    }

    private static Task<int> InsertLegacyLineAsync(PetraDbContext context, Guid companyId, Guid journalId, Guid accountId, string currency,
        decimal debit, decimal credit, string description) =>
        context.Database.ExecuteSqlInterpolatedAsync(
            $@"INSERT INTO [ledger].[JournalLines]([Id],[CompanyId],[JournalId],[AccountId],[TransactionCurrencyCode],[TransactionDebit],[TransactionCredit],
                [BaseCurrencyCode],[BaseDebit],[BaseCredit],[RateSourceCurrencyCode],[RateTargetCurrencyCode],[RateValue],[Rounding],[Description])
               VALUES({Guid.CreateVersion7()},{companyId},{journalId},{accountId},{currency},{debit},{credit},{currency},{debit},{credit},{currency},{currency},1.0,0,{description})",
            TestContext.Current.CancellationToken);

    // ---- Independent annual-workspace overloads — used by the financial HTTP integration tests (Task A) ----

    internal static async Task<(Guid CompanyId, Guid BranchId, Guid PeriodId)> CalendarAsync(ApiSqlHost host)
    {
        var cancellation = TestContext.Current.CancellationToken;
        using var annual = host.AnnualContext();
        var periodId = await annual.Database.SqlQuery<Guid>(
            $@"SELECT TOP 1 [Id] AS [Value] FROM [ledger].[AccountingPeriods]
               WHERE [CompanyId] = {host.CompanyId} AND [FiscalYearId] = {host.FiscalYearId} AND [State] = 0
               ORDER BY [RangeStart]").FirstAsync(cancellation).ConfigureAwait(false);
        return (host.CompanyId, host.BranchId, periodId);
    }

    internal static Task<(Guid JournalId, string Version)> ApprovedJournalAsync(ApiSqlHost host, string currency) =>
        ApprovedJournalAsync(host, currency, new DateOnly(2026, 1, 15));

    internal static async Task<(Guid JournalId, string Version)> ApprovedJournalAsync(ApiSqlHost host, string currency, DateOnly accountingDate)
    {
        var cancellation = TestContext.Current.CancellationToken;
        using var annual = host.AnnualContext();
        var now = DateTimeOffset.UtcNow;
        var cash = Guid.CreateVersion7();
        var revenue = Guid.CreateVersion7();
        var cashCode = "1001-" + cash.ToString("N")[^8..];
        var revenueCode = "4001-" + revenue.ToString("N")[^8..];
        await annual.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO [ledger].[Accounts]([Id],[CompanyId],[Code],[NameArabic],[AccountType],[PostingAllowed],[CostCenterPolicy],[CurrencyMode],[IsActive],[CreatedAtUtc],[UpdatedAtUtc]) VALUES({cash},{host.CompanyId},{cashCode},N'الصندوق',1,1,1,1,1,{now},{now})",
            cancellation).ConfigureAwait(false);
        await annual.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO [ledger].[Accounts]([Id],[CompanyId],[Code],[NameArabic],[AccountType],[PostingAllowed],[CostCenterPolicy],[CurrencyMode],[IsActive],[CreatedAtUtc],[UpdatedAtUtc]) VALUES({revenue},{host.CompanyId},{revenueCode},N'المبيعات',4,1,1,1,1,{now},{now})",
            cancellation).ConfigureAwait(false);

        var journalId = Guid.CreateVersion7();
        await annual.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO [ledger].[JournalEntries]([Id],[CompanyId],[BranchId],[AccountingDate],[Description],[State],[CreatedAtUtc],[UpdatedAtUtc]) VALUES({journalId},{host.CompanyId},{host.BranchId},{accountingDate},N'قيد مبيعات نقدية',3,{now},{now})",
            cancellation).ConfigureAwait(false);
        await InsertAnnualLineAsync(annual, host.CompanyId, journalId, cash, currency, 250.5m, 0m, "قبض نقدي").ConfigureAwait(false);
        await InsertAnnualLineAsync(annual, host.CompanyId, journalId, revenue, currency, 0m, 250.5m, "إيراد مبيعات").ConfigureAwait(false);

        var correlation = Guid.CreateVersion7();
        var states = new (int Revision, int Action, int? Previous, int New, string Reason)[]
        {
            (1, 1, null, 1, "إنشاء مسودة"),
            (2, 3, 1, 2, "تدقيق"),
            (3, 4, 2, 3, "اعتماد"),
        };
        var when = now.AddMinutes(-30);
        foreach (var state in states)
        {
            when = when.AddMinutes(5);
            await annual.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO [ledger].[JournalAudit]([EventId],[CompanyId],[BranchId],[JournalId],[Revision],[Action],[PreviousState],[NewState],[ActorUserId],[CorrelationId],[Reason],[OccurredAtUtc],[CreatedAtUtc]) VALUES({Guid.CreateVersion7()},{host.CompanyId},{host.BranchId},{journalId},{state.Revision},{state.Action},{state.Previous},{state.New},{host.UserId},{correlation},{state.Reason},{when},{now})",
                cancellation).ConfigureAwait(false);
        }

        var version = await annual.Database
            .SqlQuery<byte[]>($"SELECT [RowVersion] AS [Value] FROM [ledger].[JournalEntries] WHERE [Id] = {journalId}")
            .FirstAsync(cancellation).ConfigureAwait(false);
        return (journalId, Convert.ToBase64String(version));
    }

    private static Task<int> InsertAnnualLineAsync(PetraDbContext annual, Guid companyId, Guid journalId, Guid accountId, string currency,
        decimal debit, decimal credit, string description)
    {
        var now = DateTimeOffset.UtcNow;
        return annual.Database.ExecuteSqlInterpolatedAsync(
            $@"INSERT INTO [ledger].[JournalLines]([Id],[CompanyId],[JournalId],[AccountId],[TransactionCurrencyCode],[TransactionDebit],[TransactionCredit],
                [BaseCurrencyCode],[BaseDebit],[BaseCredit],[RateSourceCurrencyCode],[RateTargetCurrencyCode],[RateValue],[Rounding],[Description],[CreatedAtUtc],[UpdatedAtUtc])
               VALUES({Guid.CreateVersion7()},{companyId},{journalId},{accountId},{currency},{debit},{credit},{currency},{debit},{credit},{currency},{currency},1.0,0,{description},{now},{now})",
            TestContext.Current.CancellationToken);
    }
}
