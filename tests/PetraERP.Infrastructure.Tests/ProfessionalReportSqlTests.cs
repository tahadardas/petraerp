using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger;
using PetraERP.Application.GeneralLedger.ChartOfAccounts;
using PetraERP.Application.GeneralLedger.Reports;
using PetraERP.Application.Reports.Professional;
using PetraERP.Application.Security;
using PetraERP.Domain.GeneralLedger;
using PetraERP.Infrastructure.GeneralLedger;
using PetraERP.Infrastructure.Reports;
using PetraERP.Infrastructure.Reports.Professional;
using PetraERP.Infrastructure.Persistence;
using PetraERP.Infrastructure.Security;

namespace PetraERP.Infrastructure.Tests;

public sealed class ProfessionalReportSqlTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task ProfessionalLedgerLiteralUnicodeFiltersKeepActualOpeningAndClosingIndependent()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var s = await LedgerScenario.CreateAsync(database).ConfigureAwait(true);
            var cash = await s.AccountAsync(s.CompanyA, "1101", AccountTypeCode.Assets, true).ConfigureAwait(true);
            var other = await s.AccountAsync(s.CompanyA, "4101", AccountTypeCode.Revenue, true).ConfigureAwait(true);
            await JournalAsync(s, cash.Id, other.Id, new DateOnly(2026, 1, 5), 100m, "رصيد سابق").ConfigureAwait(true);
            await JournalAsync(s, cash.Id, other.Id, new DateOnly(2026, 2, 5), 200m, "أحمد [%]_ مُحَمَّد").ConfigureAwait(true);
            await JournalAsync(s, other.Id, cash.Id, new DateOnly(2026, 2, 6), 40m, "أحمد مستبعد").ConfigureAwait(true);
            await s.JournalAsync(s.CompanyA, s.BranchA2, new DateOnly(2026, 2, 5), "USD", true, (cash.Id, 999m, 0m), (other.Id, 0m, 999m)).ConfigureAwait(true);
            using var context = database.Context();
            var service = new SqlProfessionalReportService(context, new SqlAccessEvaluator(context), new SqlCommercialReportService(context));
            var year = await context.FiscalYears.Where(y => y.CompanyId == s.CompanyA).Select(y => y.Id).SingleAsync(Cancellation).ConfigureAwait(true);
            var request = Request(year, cash.Id) with { DescriptionContains = "[%]_", DescriptionNotContains = "مستبعد" };
            var scope = LedgerReportScope.Create(s.CompanyA, s.BranchA1, s.Actor, false);
            var result = await service.ExecuteAsync(scope, request, Cancellation).ConfigureAwait(true);
            Assert.Equal(100m, result.Summary.ActualOpening);
            Assert.Equal(200m, result.Summary.ActualPeriodDebit);
            Assert.Equal(40m, result.Summary.ActualPeriodCredit);
            Assert.Equal(260m, result.Summary.ActualClosing);
            Assert.Equal(200m, result.Summary.FilteredDebit);
            Assert.Equal(0m, result.Summary.FilteredCredit);
            Assert.Equal(300m, Assert.Single(result.Rows).ActualBalance);
            Assert.Contains("أحمد [%]_ مُحَمَّد", result.Rows[0].Description, StringComparison.Ordinal);
            Assert.Equal(ProfessionalReportSource.UnclassifiedJournal, result.Rows[0].Source);
            var zero = await service.ExecuteAsync(scope, request with { BalancePresentation = ProfessionalBalancePresentation.ZeroAtPeriodStart,
                Fields = [ProfessionalReportField.Date] }, Cancellation).ConfigureAwait(true);
            Assert.Equal(result.Summary.ActualClosing, zero.Summary.ActualClosing);
            Assert.Equal(200m, zero.Summary.DisplayClosing);
            Assert.Single(zero.SelectedFields);
            Assert.Equal(300m, Assert.Single(zero.Rows).ActualBalance);
            var excluded = await service.ExecuteAsync(scope, request with { DescriptionContains = null, DescriptionNotContains = "مستبعد" }, Cancellation).ConfigureAwait(true);
            Assert.Single(excluded.Rows);
            Assert.Equal(260m, excluded.Summary.ActualClosing);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task ProfessionalTrialParentsDoNotDoubleTotalsAndUnauthorizedBranchAndFiscalYearFail()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var s = await LedgerScenario.CreateAsync(database).ConfigureAwait(true);
            var parent = await s.AccountAsync(s.CompanyA, "1", AccountTypeCode.Assets, false).ConfigureAwait(true);
            var cash = await s.AccountAsync(s.CompanyA, "1101", AccountTypeCode.Assets, true, parent.Id).ConfigureAwait(true);
            var other = await s.AccountAsync(s.CompanyA, "4101", AccountTypeCode.Revenue, true).ConfigureAwait(true);
            await s.AccountAsync(s.CompanyA, "1102", AccountTypeCode.Assets, true, parent.Id).ConfigureAwait(true);
            await JournalAsync(s, cash.Id, other.Id, new DateOnly(2026, 1, 5), 100m, "افتتاح").ConfigureAwait(true);
            await JournalAsync(s, cash.Id, other.Id, new DateOnly(2026, 2, 5), 200m, "فترة").ConfigureAwait(true);
            using var context = database.Context();
            var service = new SqlProfessionalReportService(context, new SqlAccessEvaluator(context), new SqlCommercialReportService(context));
            var year = await context.FiscalYears.Where(y => y.CompanyId == s.CompanyA).Select(y => y.Id).SingleAsync(Cancellation).ConfigureAwait(true);
            var request = Request(year, cash.Id) with { Kind = ProfessionalReportKind.TrialBalance, Order = ProfessionalReportOrder.AccountCode,
                AccountId = null, IncludeParents = true, IncludeEmptyAccounts = true };
            var scope = LedgerReportScope.Create(s.CompanyA, s.BranchA1, s.Actor, false);
            var result = await service.ExecuteAsync(scope, request, Cancellation).ConfigureAwait(true);
            Assert.Equal(4, result.Rows.Count);
            Assert.Equal(100m, result.Summary.OpeningDebit);
            Assert.Equal(100m, result.Summary.OpeningCredit);
            Assert.Equal(300m, result.Summary.ClosingDebit);
            Assert.Equal(300m, result.Summary.ClosingCredit);
            Assert.Equal(200m, result.Summary.ActualPeriodDebit);
            Assert.Equal(300m, result.Rows.Single(r => r.AccountId == parent.Id).ClosingDebit);
            Assert.Single(result.Rows, r => r.IsAggregate);
            var nonzero = await service.ExecuteAsync(scope, request with { OnlyNonZero = true }, Cancellation).ConfigureAwait(true);
            Assert.Equal(3, nonzero.Rows.Count);
            var assets = await service.ExecuteAsync(scope, request with { AccountCategory = ProfessionalAccountCategory.Assets }, Cancellation).ConfigureAwait(true);
            Assert.All(assets.Rows, r => Assert.NotEqual(other.Id, r.AccountId));
            Assert.Equal(300m, assets.Summary.ClosingDebit);
            Assert.Equal(0m, assets.Summary.ClosingCredit);
            var wrongYear = await context.FiscalYears.Where(y => y.CompanyId == s.CompanyB).Select(y => y.Id).SingleAsync(Cancellation).ConfigureAwait(true);
            await Assert.ThrowsAsync<ResourceNotFoundException>(() => service.ExecuteAsync(scope, request with { FiscalYearId = wrongYear }, Cancellation)).ConfigureAwait(true);
            await Assert.ThrowsAsync<RequestValidationException>(() => service.ExecuteAsync(scope, request with { To = new DateOnly(2027, 1, 1) }, Cancellation)).ConfigureAwait(true);
            await s.RevokeBranchAsync(s.BranchA2).ConfigureAwait(true);
            await Assert.ThrowsAsync<ReportScopeDeniedException>(() => service.ExecuteAsync(LedgerReportScope.Create(s.CompanyA, s.BranchA2, s.Actor, false), request, Cancellation)).ConfigureAwait(true);
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task AccountLedgerParentAndPostingReconcileExactlyAgainstPostedJournalLines()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var s = await LedgerScenario.CreateAsync(database).ConfigureAwait(true);
            var root = await s.AccountAsync(s.CompanyA, "1", AccountTypeCode.Assets, false).ConfigureAwait(true);
            var cash1 = await s.AccountAsync(s.CompanyA, "1101", AccountTypeCode.Assets, true, root.Id).ConfigureAwait(true);
            var cash2 = await s.AccountAsync(s.CompanyA, "1102", AccountTypeCode.Assets, true, root.Id).ConfigureAwait(true);
            var revenue = await s.AccountAsync(s.CompanyA, "4101", AccountTypeCode.Revenue, true).ConfigureAwait(true);
            // Prior-period opening movement, then same-day movements across two children to pin deterministic ordering.
            await JournalAsync(s, cash1.Id, revenue.Id, new DateOnly(2026, 1, 10), 500m, "افتتاح").ConfigureAwait(true);
            await JournalAsync(s, cash1.Id, revenue.Id, new DateOnly(2026, 2, 5), 120m, "حركة أول").ConfigureAwait(true);
            await JournalAsync(s, cash2.Id, revenue.Id, new DateOnly(2026, 2, 5), 80m, "حركة ثان").ConfigureAwait(true);
            await JournalAsync(s, revenue.Id, cash1.Id, new DateOnly(2026, 2, 20), 30m, "مقابل").ConfigureAwait(true);
            using var context = database.Context();
            var service = new SqlProfessionalReportService(context, new SqlAccessEvaluator(context), new SqlCommercialReportService(context));
            var year = await context.FiscalYears.Where(y => y.CompanyId == s.CompanyA).Select(y => y.Id).SingleAsync(Cancellation).ConfigureAwait(true);
            var scope = LedgerReportScope.Create(s.CompanyA, s.BranchA1, s.Actor, false);

            // Authoritative raw sums over posted journal lines for the whole subtree, computed independently of the report.
            var subtree = new[] { root.Id, cash1.Id, cash2.Id };
            var rawOpening = await context.JournalLines.AsNoTracking().Where(l => l.CompanyId == s.CompanyA && l.Journal!.BranchId == s.BranchA1
                && subtree.Contains(l.AccountId) && l.Journal.State == (byte)JournalEntryState.Posted && l.Journal.AccountingDate < new DateOnly(2026, 2, 1))
                .SumAsync(l => l.BaseDebit - l.BaseCredit, Cancellation).ConfigureAwait(true);
            var rawPeriodDebit = await context.JournalLines.AsNoTracking().Where(l => l.CompanyId == s.CompanyA && l.Journal!.BranchId == s.BranchA1
                && subtree.Contains(l.AccountId) && l.Journal.State == (byte)JournalEntryState.Posted && l.Journal.AccountingDate >= new DateOnly(2026, 2, 1) && l.Journal.AccountingDate <= new DateOnly(2026, 2, 28))
                .SumAsync(l => l.BaseDebit, Cancellation).ConfigureAwait(true);
            var rawPeriodCredit = await context.JournalLines.AsNoTracking().Where(l => l.CompanyId == s.CompanyA && l.Journal!.BranchId == s.BranchA1
                && subtree.Contains(l.AccountId) && l.Journal.State == (byte)JournalEntryState.Posted && l.Journal.AccountingDate >= new DateOnly(2026, 2, 1) && l.Journal.AccountingDate <= new DateOnly(2026, 2, 28))
                .SumAsync(l => l.BaseCredit, Cancellation).ConfigureAwait(true);

            var parentLedger = await service.ExecuteAsync(scope, Request(year, root.Id), Cancellation).ConfigureAwait(true);
            // Every eligible posted line in the subtree appears exactly once (both children, none from the revenue legs):
            // cash1 +120, cash2 +80, cash1 -30; the January journal forms the opening balance only.
            Assert.Equal(3, parentLedger.Rows.Count);
            Assert.Equal(rawOpening, parentLedger.Summary.ActualOpening);
            Assert.Equal(rawPeriodDebit, parentLedger.Summary.ActualPeriodDebit);
            Assert.Equal(rawPeriodCredit, parentLedger.Summary.ActualPeriodCredit);
            Assert.Equal(rawOpening + rawPeriodDebit - rawPeriodCredit, parentLedger.Summary.ActualClosing);
            var lastRow = parentLedger.Rows[^1];
            Assert.Equal(parentLedger.Summary.ActualClosing, lastRow.ActualBalance);
            Assert.Contains(parentLedger.Rows, r => r.AccountId == cash2.Id);
            Assert.All(parentLedger.Rows, row => Assert.False(string.IsNullOrEmpty(row.OppositeAccountCode)));

            var leafLedger = await service.ExecuteAsync(scope, Request(year, cash1.Id), Cancellation).ConfigureAwait(true);
            Assert.Equal(2, leafLedger.Rows.Count);
            Assert.Equal(500m, leafLedger.Summary.ActualOpening);
            Assert.Equal(590m, leafLedger.Summary.ActualClosing); // 500 + 120 - 30 reconciles against the leaf's own lines only.
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task TrialBalanceParentAggregatesNeverMultiplyTotalsAndLeafRowsDrillThrough()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var s = await LedgerScenario.CreateAsync(database).ConfigureAwait(true);
            var root = await s.AccountAsync(s.CompanyA, "1", AccountTypeCode.Assets, false).ConfigureAwait(true);
            var cash1 = await s.AccountAsync(s.CompanyA, "1101", AccountTypeCode.Assets, true, root.Id).ConfigureAwait(true);
            var cash2 = await s.AccountAsync(s.CompanyA, "1102", AccountTypeCode.Assets, true, root.Id).ConfigureAwait(true);
            var revenue = await s.AccountAsync(s.CompanyA, "4101", AccountTypeCode.Revenue, true).ConfigureAwait(true);
            await JournalAsync(s, cash1.Id, revenue.Id, new DateOnly(2026, 2, 5), 60m, "حركة نقدية").ConfigureAwait(true);
            await JournalAsync(s, cash2.Id, revenue.Id, new DateOnly(2026, 2, 6), 40m, "حركة ثانية").ConfigureAwait(true);
            using var context = database.Context();
            var service = new SqlProfessionalReportService(context, new SqlAccessEvaluator(context), new SqlCommercialReportService(context));
            var year = await context.FiscalYears.Where(y => y.CompanyId == s.CompanyA).Select(y => y.Id).SingleAsync(Cancellation).ConfigureAwait(true);
            var scope = LedgerReportScope.Create(s.CompanyA, s.BranchA1, s.Actor, false);
            var request = Request(year, cash1.Id) with { Kind = ProfessionalReportKind.TrialBalance, AccountId = null, IncludeParents = true };

            var both = await service.ExecuteAsync(scope, request, Cancellation).ConfigureAwait(true);
            var aggregates = both.Rows.Where(row => row.IsAggregate).ToArray();
            var postings = both.Rows.Where(row => !row.IsAggregate).ToArray();
            Assert.Single(aggregates);
            Assert.Equal(root.Id, aggregates[0].AccountId);
            Assert.False(aggregates[0].CanDrillThrough);
            Assert.All(postings, row => Assert.True(row.CanDrillThrough));
            Assert.Equal(3, postings.Length);
            // Parent presentation rows never multiply the official totals: the leaf rows alone tie to the footer.
            Assert.Equal(both.Summary.ClosingDebit, postings.Sum(row => row.ClosingDebit));
            Assert.Equal(both.Summary.ClosingCredit, postings.Sum(row => row.ClosingCredit));
            Assert.Equal(100m, both.Summary.ClosingDebit);
            Assert.Equal(100m, both.Summary.ClosingCredit);
            Assert.Equal(100m, aggregates[0].ClosingDebit); // The rolled parent still presents the subtree balance.

            var withoutParents = await service.ExecuteAsync(scope, request with { IncludeParents = false }, Cancellation).ConfigureAwait(true);
            Assert.All(withoutParents.Rows, row => Assert.False(row.IsAggregate));
            Assert.Equal(both.Summary.ClosingDebit, withoutParents.Rows.Sum(row => row.ClosingDebit));
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServer")]
    public async Task CustomerStatementReportsMissingSalesAndTreasuryPermissionsIndependentlyAndNeverAutoGrants()
    {
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var s = await LedgerScenario.CreateAsync(database).ConfigureAwait(true);
            using var context = database.Context();
            var service = new SqlProfessionalReportService(context, new SqlAccessEvaluator(context), new SqlCommercialReportService(context));
            var year = await context.FiscalYears.Where(y => y.CompanyId == s.CompanyA).Select(y => y.Id).SingleAsync(Cancellation).ConfigureAwait(true);
            var scope = LedgerReportScope.Create(s.CompanyA, s.BranchA1, s.Actor, false);
            // The scenario actor holds the general report/journal/party/inventory grants but not Sales or Treasury.
            var request = new ProfessionalReportRequest
            {
                Kind = ProfessionalReportKind.CustomerStatement, FiscalYearId = year, PartyId = Guid.NewGuid(),
                From = new DateOnly(2026, 2, 1), To = new DateOnly(2026, 2, 28), CurrencyCode = "USD", CurrencyBasis = ProfessionalCurrencyBasis.Transaction,
                Sources = [ProfessionalReportSource.SalesInvoice, ProfessionalReportSource.SalesReturn, ProfessionalReportSource.ReceiptVoucher, ProfessionalReportSource.Settlement],
            };
            var first = await Assert.ThrowsAsync<ReportScopeDeniedException>(() => service.ExecuteAsync(scope, request, Cancellation)).ConfigureAwait(true);
            Assert.Equal(PermissionCatalog.SalesInvoicesView, first.Permission);
            Assert.Contains("عرض فواتير المبيعات", first.Message);
            Assert.False(await HasPermissionAsync(context, s.Actor, PermissionCatalog.SalesInvoicesView).ConfigureAwait(true));

            await GrantAsync(context, s.Actor, PermissionCatalog.SalesInvoicesView).ConfigureAwait(true);
            var second = await Assert.ThrowsAsync<ReportScopeDeniedException>(() => service.ExecuteAsync(scope, request, Cancellation)).ConfigureAwait(true);
            Assert.Equal(PermissionCatalog.TreasuryVouchersView, second.Permission);
            Assert.False(await HasPermissionAsync(context, s.Actor, PermissionCatalog.TreasuryVouchersView).ConfigureAwait(true));
        }
    }

    private static async Task<bool> HasPermissionAsync(PetraDbContext context, Guid user, string permission) =>
        await CountRolePermissionsAsync(context, user, permission).ConfigureAwait(true) > 0;

    private static async Task GrantAsync(PetraDbContext context, Guid user, string permission) => await context.Database
        .ExecuteSqlInterpolatedAsync($"INSERT INTO [security].[RolePermissions]([RoleId],[PermissionCode]) SELECT [RoleId],{permission} FROM [security].[UserRoles] WHERE [UserId]={user}", Cancellation).ConfigureAwait(true);

    private static async Task<int> CountRolePermissionsAsync(PetraDbContext context, Guid user, string permission)
    {
        var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT COUNT(1) FROM [security].[RolePermissions] rp JOIN [security].[UserRoles] ur ON ur.[RoleId]=rp.[RoleId] WHERE ur.[UserId]=@u AND rp.[PermissionCode]=@p";
        var p1 = command.CreateParameter(); p1.ParameterName = "@u"; p1.Value = user; command.Parameters.Add(p1);
        var p2 = command.CreateParameter(); p2.ParameterName = "@p"; p2.Value = permission; command.Parameters.Add(p2);
        await context.Database.OpenConnectionAsync(Cancellation).ConfigureAwait(true);
        try
        {
            var result = await command.ExecuteScalarAsync(Cancellation).ConfigureAwait(true);
            return System.Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture);
        }
        finally { await context.Database.CloseConnectionAsync().ConfigureAwait(true); }
    }

    private static ProfessionalReportRequest Request(Guid year, Guid account) => new()    {
        Kind = ProfessionalReportKind.AccountLedger, FiscalYearId = year, AccountId = account,
        From = new DateOnly(2026, 2, 1), To = new DateOnly(2026, 2, 28), Sources = [ProfessionalReportSource.UnclassifiedJournal],
    };

    private static async Task JournalAsync(LedgerScenario s, Guid debit, Guid credit, DateOnly date, decimal amount, string description)
    {
        using var context = s.Database.Context();
        var created = await new SqlJournalEntryService(context, TimeProvider.System).CreateDraftAsync(
            JournalEntryScope.Create(s.CompanyA, s.BranchA1, s.Actor), new CreateJournalDraftCommand
            {
                AccountingDate = date, Description = description,
                Lines = [Line(debit, amount, 0m), Line(credit, 0m, amount)],
            }, Cancellation).ConfigureAwait(false);
        await s.PostAsync(s.CompanyA, s.BranchA1, created.Id).ConfigureAwait(false);
    }

    private static JournalDraftLineInput Line(Guid account, decimal debit, decimal credit) => new()
    {
        AccountId = account, TransactionCurrencyCode = "USD", RateSourceCurrencyCode = "USD", RateTargetCurrencyCode = "USD",
        RateValue = 1m, TransactionDebit = debit, TransactionCredit = credit, Description = "سطر اختبار",
    };
}
