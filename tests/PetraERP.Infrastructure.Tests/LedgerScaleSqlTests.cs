using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using PetraERP.Application.Common;
using PetraERP.Application.GeneralLedger.ChartOfAccounts;
using PetraERP.Application.GeneralLedger.Reports;
using PetraERP.Infrastructure.GeneralLedger;

namespace PetraERP.Infrastructure.Tests;

/// <summary>
/// Account ledger at scale. The data is bulk-inserted straight into the ledger tables (posted journals of two lines each) so that
/// 100,000+ lines exist in seconds; this measures the READ path only and makes no claim about how such data would be posted.
/// Set PETRAERP_LEDGER_SCALE_JOURNALS (default 50000 journals = 100000 lines on the measured account).
/// </summary>
public sealed class LedgerScaleSqlTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private sealed class Signer : PetraERP.Application.GeneralLedger.Reports.ILedgerCursorSigner
    {
        public string Protect(ReadOnlySpan<byte> payload) => Convert.ToBase64String(payload);

        public bool TryUnprotect(string token, out byte[] payload)
        {
            payload = Convert.FromBase64String(token);
            return true;
        }
    }

    [Fact(Explicit = true)]
    [Trait("Category", "SqlServerBenchmark")]
    public async Task MeasureFirstPageDeepOffsetPageAndCursorPageOnALargeLedger()
    {
        var journals = int.Parse(Environment.GetEnvironmentVariable("PETRAERP_LEDGER_SCALE_JOURNALS") ?? "50000", System.Globalization.CultureInfo.InvariantCulture);
        var database = await SqlTestDatabase.CreateAsync().ConfigureAwait(true);
        await using (database.ConfigureAwait(true))
        {
            var s = await LedgerScenario.CreateAsync(database).ConfigureAwait(true);
            var cash = await s.AccountAsync(s.CompanyA, "1101", AccountTypeCode.Assets, true).ConfigureAwait(true);
            var sales = await s.AccountAsync(s.CompanyA, "4101", AccountTypeCode.Revenue, true).ConfigureAwait(true);
            using var context = database.Context();
            var year = await context.FiscalYears.AsNoTracking().Where(y => y.CompanyId == s.CompanyA).Select(y => y.Id).SingleAsync(Cancellation).ConfigureAwait(true);
            var period = await context.AccountingPeriods.AsNoTracking().Where(p => p.CompanyId == s.CompanyA && p.FiscalYearId == year).OrderBy(p => p.RangeStart)
                .Select(p => new { p.Id, p.RangeStart }).FirstAsync(Cancellation).ConfigureAwait(true);

            // A million journals take minutes to insert; only the seed gets the long timeout, the measured reads keep the normal one.
            context.Database.SetCommandTimeout(TimeSpan.FromMinutes(20));
            var seed = Stopwatch.StartNew();
            await context.Database.ExecuteSqlInterpolatedAsync($@"
                ;WITH n AS (SELECT TOP ({journals}) ROW_NUMBER() OVER (ORDER BY (SELECT 1)) AS i FROM sys.all_objects a CROSS JOIN sys.all_objects b)
                SELECT i INTO #n FROM n;
                INSERT [ledger].[JournalEntries]([Id],[CompanyId],[BranchId],[AccountingDate],[Description],[State],[PostingSequence],[PostingFiscalYearId],[PostingPeriodId])
                SELECT NEWID(), {s.CompanyA}, {s.BranchA1}, DATEADD(DAY, i % 27, {period.RangeStart}), N'قيد حجم كبير', 4, 1000000 + i, {year}, {period.Id} FROM #n;
                INSERT [ledger].[JournalLines]([Id],[CompanyId],[JournalId],[AccountId],[TransactionCurrencyCode],[TransactionDebit],[TransactionCredit],
                    [BaseCurrencyCode],[BaseDebit],[BaseCredit],[RateSourceCurrencyCode],[RateTargetCurrencyCode],[RateValue],[Rounding],[Description])
                SELECT NEWID(), {s.CompanyA}, j.[Id], x.AccountId, N'USD', x.D, x.C, N'USD', x.D, x.C, N'USD', N'USD', 1, 0, N'سطر'
                FROM [ledger].[JournalEntries] j
                CROSS APPLY (VALUES ({cash.Id}, CAST(10 AS decimal(19,4)), CAST(0 AS decimal(19,4))), ({sales.Id}, CAST(0 AS decimal(19,4)), CAST(10 AS decimal(19,4)))) x(AccountId, D, C)
                WHERE j.[CompanyId] = {s.CompanyA} AND j.[Description] = N'قيد حجم كبير';", Cancellation).ConfigureAwait(true);
            // Experiment switch: the configured line index is called "covering" but includes no amounts, so every balance rollup does one
            // key lookup per line. PETRAERP_LEDGER_SCALE_COVERING=1 adds covering copies before measuring, to price the change before a migration.
            if (Environment.GetEnvironmentVariable("PETRAERP_LEDGER_SCALE_COVERING") == "1")
            {
                await context.Database.ExecuteSqlRawAsync(@"
                    CREATE INDEX IX_Scale_Lines_Covering ON [ledger].[JournalLines]([CompanyId],[AccountId],[JournalId]) INCLUDE ([BaseDebit],[BaseCredit],[Description],[TransactionCurrencyCode],[TransactionDebit],[TransactionCredit],[CostCenterId]);
                    CREATE INDEX IX_Scale_Entries_Covering ON [ledger].[JournalEntries]([CompanyId],[BranchId],[State],[AccountingDate],[PostingSequence],[Id]) INCLUDE ([Description],[ReversesEntryId]);
                    UPDATE STATISTICS [ledger].[JournalLines]; UPDATE STATISTICS [ledger].[JournalEntries];", Cancellation).ConfigureAwait(true);
            }

            // A bulk load of millions of rows leaves statistics describing an empty table; production maintenance refreshes them.
            await context.Database.ExecuteSqlRawAsync("UPDATE STATISTICS [ledger].[JournalLines] WITH FULLSCAN; UPDATE STATISTICS [ledger].[JournalEntries] WITH FULLSCAN;", Cancellation).ConfigureAwait(true);
            seed.Stop();

            var scope = LedgerReportScope.Create(s.CompanyA, s.BranchA1, s.Actor, false);
            var signer = new Signer();
            const int PageSize = 100;
            async Task<(double Ms, AccountLedgerDto Page)> TimeAsync(AccountLedgerQuery query)
            {
                using var read = database.Context();
                var timer = Stopwatch.StartNew();
                var page = await new SqlLedgerQueryService(read, signer).GetAccountLedgerAsync(scope, query, Cancellation).ConfigureAwait(true);
                return (timer.Elapsed.TotalMilliseconds, page);
            }

            var first = new List<double>();
            AccountLedgerDto firstPage = null!;
            for (var i = 0; i < 5; i++)
            {
                var (ms, page) = await TimeAsync(new AccountLedgerQuery(cash.Id, null, null, PageRequest.Create(1, PageSize))).ConfigureAwait(true);
                first.Add(ms);
                firstPage = page;
            }

            var deepPageNumber = journals / PageSize / 2;
            var deepOffset = new List<double>();
            AccountLedgerDto deep = null!;
            for (var i = 0; i < 3; i++)
            {
                var (ms, page) = await TimeAsync(new AccountLedgerQuery(cash.Id, null, null, PageRequest.Create(deepPageNumber, PageSize))).ConfigureAwait(true);
                deepOffset.Add(ms);
                deep = page;
            }

            var cursorNext = new List<double>();
            for (var i = 0; i < 5; i++)
            {
                var (ms, _) = await TimeAsync(new AccountLedgerQuery(cash.Id, null, null, PageRequest.Create(deepPageNumber + 1, PageSize), deep.NextCursor)).ConfigureAwait(true);
                cursorNext.Add(ms);
            }

            double Median(List<double> values) { values.Sort(); return values[values.Count / 2]; }
            var line = FormattableString.Invariant(
                $"LEDGER_SCALE journals={journals} lines_on_account={firstPage.TotalCount} seed_s={seed.Elapsed.TotalSeconds:F1} first_page_p50_ms={Median(first):F0} deep_offset_page_{deepPageNumber}_p50_ms={Median(deepOffset):F0} cursor_next_at_depth_p50_ms={Median(cursorNext):F0} closing={firstPage.ClosingBalance}");
            var path = Environment.GetEnvironmentVariable("PETRAERP_BENCH_OUT_LEDGER_SCALE") ?? Path.Combine(Path.GetTempPath(), "petra_ledger_scale.txt");
            await File.WriteAllTextAsync(path, line + Environment.NewLine, Cancellation).ConfigureAwait(true);
            Assert.Equal(journals, firstPage.TotalCount);
            Assert.Equal(journals * 10m, firstPage.ClosingBalance);
        }
    }
}
